using FactoryForge.TagBus;

namespace FactoryForge.Sim.DemoProfiles;

/// <summary>
/// Momentary buttons, a latching E-stop, and a batch (§4.2, OP-04). Plays the
/// PLC's part of the exercise: reacts to whatever the operator presses on the
/// panel rather than pressing its own buttons, since the point of this scene
/// is that a human drives it.
///
/// <c>panel.estop</c> is normally closed: true means the circuit is healthy,
/// false means the mushroom is struck. A profile that got that backwards would
/// run happily with the E-stop wire cut, which is the exact bug NC wiring
/// exists to catch — so this is worth getting right here, not just leaving
/// students to discover it. That reading now lives in
/// <see cref="OperatorStation"/>, shared with the other four scenes.
///
/// What this scene adds on top of the shared contract is the panel's pot as a
/// <b>batch size</b>. "Run until someone presses Stop" has no right answer;
/// "make exactly this many and stop yourself" does, and the line either hits
/// it or it does not. Pressing Start after a finished batch begins the next
/// one, the way a real batch controller works.
/// </summary>
public sealed class StartStopStationProfile : IDemoProfile
{
    private const double EmitHalfPeriod = 1.5;

    /// <summary>Used only on a line built without a panel.</summary>
    private const int DefaultBatch = 12;

    private readonly OperatorStation _station = new();

    private bool _prevPresent;
    private int _produced;
    private double _elapsed;
    private bool _emitFlag;
    private double _nextToggle;

    public void Start(TagTable tags)
    {
        _prevPresent = false;
        _produced = 0;
        _elapsed = 0;
        _emitFlag = false;
        _nextToggle = EmitHalfPeriod;
        _station.Begin();
        Apply(tags);
    }

    public void Tick(double delta, TagTable tags)
    {
        int target = (int)System.Math.Round(_station.Setpoint(tags, DefaultBatch));
        bool wasDone = target > 0 && _produced >= target;

        _station.Scan(tags);

        // Start on a finished batch starts the next one. Without this the
        // panel would have a Start button that does nothing until someone
        // found a way to zero the count, which is not a control system.
        if (_station.StartEdge && wasDone) _produced = 0;

        bool present = OperatorStation.Bit(tags, "part_present.detect");
        if (present && !_prevPresent && _station.Running) _produced++;
        _prevPresent = present;

        if (target > 0 && _produced >= target) _station.HoldOff();

        _elapsed += delta;
        if (_station.Running)
        {
            if (_elapsed >= _nextToggle)
            {
                _emitFlag = !_emitFlag;
                _nextToggle = _elapsed + EmitHalfPeriod;
            }
        }
        else
        {
            _emitFlag = false;
            _nextToggle = _elapsed + EmitHalfPeriod;
        }

        Apply(tags);
    }

    private void Apply(TagTable tags)
    {
        OperatorStation.Set(tags, "belt.rotate", _station.Running);
        OperatorStation.Set(tags, "emitter.emit", _emitFlag);
        OperatorStation.Set(tags, "produced.value", _produced);
        // Mutually exclusive: green while running, red while tripped, yellow
        // for stopped-but-healthy -- lamp state and belt state can never
        // disagree if there is exactly one true at a time.
        _station.Lamps(tags);
    }
}
