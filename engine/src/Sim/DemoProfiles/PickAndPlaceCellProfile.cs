using Godot;
using FactoryForge.TagBus;

namespace FactoryForge.Sim.DemoProfiles;

/// <summary>
/// The pick-and-place cell (CP-30). A sequencer, which is what this scene is
/// for: three motions that have to happen in an order, each waiting on the
/// feedback of the one before rather than on a timer.
///
/// Every transition here is guarded by a real input — `inposition`, `lowered`,
/// `raised`, `holding` — and none of them by a delay. That is deliberate and it
/// is the lesson: a sequence written on timers works until the day the machine
/// is slower than the timer, and this scene will show that immediately because
/// the travel speed is a slider in the property panel.
///
/// The grip step is the one worth reading. After closing the vacuum the
/// sequence checks `holding`, and if the cup caught nothing it goes back to
/// waiting instead of running the rest of the cycle carrying air.
/// </summary>
public sealed class PickAndPlaceCellProfile : IDemoProfile
{
    private enum Step
    {
        ToPick,      // travel to the pick station
        Lower,       // drop the column onto the carton
        Grip,        // close the vacuum and check it caught something
        Raise,       // lift clear before travelling
        ToPlace,     // travel to the discharge point
        Release,     // lower, open the vacuum, come back up
        Home,
    }

    /// <summary>Rail percentage of the pick station and the discharge point.
    /// The template puts the pick station under 0 % and clear floor above the
    /// remover under 100 %.</summary>
    private const float PickAt = 0.0f;
    private const float PlaceAt = 96.0f;

    /// <summary>Seconds between emissions while there is room to feed into.
    ///
    /// Short, because the queue is what bounds the feed here, not the clock: the
    /// infeed holds while a carton is indexed, so cartons accumulate on the belt
    /// and the next one is already waiting when the station clears. A six-second
    /// cadence — the first version — added its own dead time *on top of* the
    /// travel and the gantry cycle, and the cell managed one carton in thirty
    /// seconds. Feeding is gated on space instead (see <see cref="DriveInfeed"/>),
    /// which is both faster and the rule a real cell uses.</summary>
    private const double FeedInterval = 2.0;

    private readonly OperatorStation _station =
        new OperatorStation("panel", "gantry.fault", "infeed.fault");

    private Step _step;
    private double _feedTimer;
    private double _settle;
    private int _placed;

    public void Start(TagTable tags)
    {
        _station.Begin();
        _step = Step.ToPick;
        _feedTimer = 1.0;
        _settle = 0.0;
        _placed = 0;

        OperatorStation.Set(tags, "gantry.target", (double)PickAt);
        OperatorStation.Set(tags, "gantry.lower", false);
        OperatorStation.Set(tags, "gantry.grip", false);
        OperatorStation.Set(tags, "scanner.enable", true);
        OperatorStation.Set(tags, "emitter.emit", false);
        _station.Lamps(tags);
    }

    public void Tick(double delta, TagTable tags)
    {
        _station.Scan(tags);
        _station.Lamps(tags);

        bool running = _station.Running;

        // The beacon is the annunciator, not a second stack light: it turns
        // only for a condition somebody has to come and deal with.
        OperatorStation.Set(tags, "alarm.beacon", _station.Tripped || _station.DriveFaulted);
        OperatorStation.Set(tags, "alarm.horn", _station.DriveFaulted);

        DriveInfeed(delta, tags, running);
        OperatorStation.Set(tags, "rate.value", OperatorStation.Num(tags, "infeed.actual"));

        if (!running)
        {
            // Stopped means stopped, and it means safe: the vacuum stays as it
            // is so a carried carton is not dropped by an E-stop, but nothing
            // new starts moving.
            OperatorStation.Set(tags, "gantry.lower", false);
            return;
        }

        RunSequence(delta, tags);
    }

    /// <summary>
    /// Ramp the infeed to whatever the pot asks for and feed cartons at a fixed
    /// cadence. The reference is the pot's own number — the panel's plate is
    /// graduated 10–100 %, so the operator really is setting the drive.
    /// </summary>
    private void DriveInfeed(double delta, TagTable tags, bool running)
    {
        // The pick station indexes: it runs until a carton breaks the beam at
        // the stop position, then holds it there. Coasting a carton onto a dead
        // plate and hoping it lands under the cup is not a control strategy --
        // this is the interlock that makes a pick repeatable rather than lucky.
        bool atStation = OperatorStation.Bit(tags, "atstation.detect");

        // The infeed runs whenever the line does, and deliberately does *not*
        // hold while a carton is indexed. Holding it looks like the obvious
        // accumulation interlock and it jams this line solid: a carton stopped
        // exactly on the joint between two conveyors is resting against the
        // downstream deck's leading face, and rigid-body contact slop means it
        // cannot climb back onto it when the belt restarts. Carried across at
        // speed it never touches that face. The queue is bounded by the feed
        // gate below instead, which costs nothing and cannot wedge anything.
        OperatorStation.Set(tags, "infeed.run", running);
        OperatorStation.Set(tags, "infeed.speed", running ? _station.Setpoint(tags, 60.0) : 0.0);
        OperatorStation.Set(tags, "pickstation.rotate", running && !atStation);

        // One-scan pulse, cleared on the tick after it is raised — the emitter
        // fires on the rising edge, so holding it high spawns nothing.
        OperatorStation.Set(tags, "emitter.emit", false);
        if (!running) return;

        // Feed only into space: not while a carton is indexed at the station,
        // and not while one is still under the scanner head. Together those
        // bound the queue to what the infeed can hold without needing a count,
        // and they are the rule a real cell uses rather than a timer someone
        // tuned once.
        if (atStation || OperatorStation.Bit(tags, "scanner.present")) return;

        _feedTimer -= delta;
        if (_feedTimer > 0.0) return;
        _feedTimer = FeedInterval;
        OperatorStation.Set(tags, "emitter.emit", true);
    }

    /// <summary>
    /// Has the axis arrived at <paramref name="destination"/>?
    ///
    /// Deliberately *not* just `gantry.inposition`. That bit compares the
    /// axis to the target the machine currently holds, and the target this
    /// controller just wrote does not reach the machine until the next tick —
    /// so on the scan that issues a move, `inposition` is still reporting
    /// "arrived", at the place we are trying to leave. The first version of
    /// this sequencer trusted it and released every carton back onto the pick
    /// station, having never travelled at all.
    ///
    /// Checking the position feedback against the destination *this step
    /// wants* has no such window, and is what a real program does when the
    /// drive gives it a position.
    /// </summary>
    private static bool Arrived(TagTable tags, float destination) =>
        Mathf.Abs((float)OperatorStation.Num(tags, "gantry.position") - destination) <= ArrivalWindow;

    /// <summary>Percent of the rail counted as arrived. Wider than the
    /// machine's own in-position window so the two never disagree in a way
    /// that stalls the sequence.</summary>
    private const float ArrivalWindow = 2.5f;

    private void RunSequence(double delta, TagTable tags)
    {
        bool inPosition = OperatorStation.Bit(tags, "gantry.inposition");
        bool lowered = OperatorStation.Bit(tags, "gantry.lowered");
        bool raised = OperatorStation.Bit(tags, "gantry.raised");
        bool holding = OperatorStation.Bit(tags, "gantry.holding");

        switch (_step)
        {
            case Step.ToPick:
                OperatorStation.Set(tags, "gantry.target", (double)PickAt);
                OperatorStation.Set(tags, "gantry.lower", false);
                // Wait for a carton to be *indexed at the stop*, not merely
                // seen upstream by the scanner. The first version waited on the
                // scanner and a fixed settle time, which is the timer-based
                // sequence this scene exists to argue against: change the
                // drive's speed slider and it misses every carton.
                if (inPosition && Arrived(tags, PickAt) && raised
                    && OperatorStation.Bit(tags, "atstation.detect"))
                {
                    // A short dwell so the belt's own stop has settled before
                    // the cup comes down on a carton still sliding.
                    _settle = 0.4;
                    _step = Step.Lower;
                }
                break;

            case Step.Lower:
                _settle -= delta;
                if (_settle > 0.0) break;
                OperatorStation.Set(tags, "gantry.lower", true);
                if (lowered) { _settle = 0.25; _step = Step.Grip; }
                break;

            case Step.Grip:
                OperatorStation.Set(tags, "gantry.grip", true);
                _settle -= delta;
                if (_settle > 0.0) break;
                // The check that matters. A cup that caught nothing goes back
                // to waiting instead of flying an empty cycle.
                if (holding) _step = Step.Raise;
                else { OperatorStation.Set(tags, "gantry.grip", false); _step = Step.ToPick; }
                break;

            case Step.Raise:
                OperatorStation.Set(tags, "gantry.lower", false);
                if (raised) _step = Step.ToPlace;
                break;

            case Step.ToPlace:
                OperatorStation.Set(tags, "gantry.target", (double)PlaceAt);
                if (inPosition && Arrived(tags, PlaceAt)) _step = Step.Release;
                break;

            case Step.Release:
                // Released high on purpose: the carton drops into the outfeed
                // zone with the carriage's own velocity, which is what a real
                // handoff looks like and what the remover is placed to catch.
                OperatorStation.Set(tags, "gantry.grip", false);
                if (!holding) { _placed++; _step = Step.Home; }
                break;

            case Step.Home:
                OperatorStation.Set(tags, "gantry.target", (double)PickAt);
                if (inPosition && Arrived(tags, PickAt)) _step = Step.ToPick;
                break;
        }
    }

    /// <summary>Cartons this run has put down. Not a tag — the cell has no
    /// display for it — but the number a test can ask for to know the sequence
    /// completed cycles rather than merely moved.</summary>
    public int Placed => _placed;
}
