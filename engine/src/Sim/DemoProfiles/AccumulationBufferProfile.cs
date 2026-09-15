using FactoryForge.TagBus;

namespace FactoryForge.Sim.DemoProfiles;

/// <summary>
/// Accumulation and a metered release (LP-20).
///
/// Cartons pile up behind a raised blade stop on a belt that never stops, and
/// are let out in batches. The thing worth learning is how the release is
/// timed: <b>by belt travel, not by seconds</b>. The window is a number of
/// encoder pulses, so the same setting releases the same amount of product at
/// any line speed — and a controller written with a timer instead would release
/// twice as much the moment somebody turned the drive up, which is the mistake
/// this scene exists to make visible.
///
/// Mirrors the exercise in <c>tools/try_scene.py</c>, as every profile here
/// must: the two have to agree, or the demo and the test are describing
/// different machines.
/// </summary>
public sealed class AccumulationBufferProfile : IDemoProfile
{
    /// <summary>Drive reference, percent. Well under full, so there is room to
    /// turn it up and watch the release stay the same size.</summary>
    private const double DriveReference = 60.0;

    /// <summary>Seconds between cartons fed onto the tail of the queue.</summary>
    private const double EmitHalfPeriod = 0.6;

    /// <summary>Pulses of belt travel spent accumulating before each release.
    /// Long enough to build a queue worth releasing at the feed rate above.
    /// </summary>
    private const double AccumulatePulses = 260.0;

    /// <summary>Release window when the line has no panel to read it from.
    /// 100 pulses is one metre of belt.</summary>
    private const double DefaultReleasePulses = 120.0;

    private readonly OperatorStation _station = new OperatorStation("panel", "buffer.fault", "stop.fault");

    private bool _releasing;
    private double _phaseStartCount;
    private double _elapsed;
    private bool _emitFlag;
    private double _nextToggle;
    private bool _flowSeen;

    public void Start(TagTable tags)
    {
        _releasing = false;
        _phaseStartCount = 0.0;
        _elapsed = 0.0;
        _emitFlag = false;
        _nextToggle = EmitHalfPeriod;
        _flowSeen = false;
        _station.Begin();
        Apply(tags);
    }

    public void Tick(double delta, TagTable tags)
    {
        _station.Scan(tags);
        _elapsed += delta;

        double count = OperatorStation.Num(tags, "enc.count");

        if (!_station.Running)
        {
            // A stopped line holds what it has. The blade stays up, which is
            // the safe state here: dropping it on a stop would spill the whole
            // buffer the moment the belt started again.
            _releasing = false;
            _phaseStartCount = count;
            _emitFlag = false;
            _nextToggle = _elapsed + EmitHalfPeriod;
        }
        else
        {
            if (_elapsed >= _nextToggle)
            {
                _emitFlag = !_emitFlag;
                _nextToggle = _elapsed + EmitHalfPeriod;
            }

            // The whole lesson, in two lines: both phases end on a *distance*
            // the encoder measured, so neither one changes length when the
            // drive reference does.
            double window = _releasing
                ? _station.Setpoint(tags, DefaultReleasePulses)
                : AccumulatePulses;

            if (count - _phaseStartCount >= window)
            {
                _releasing = !_releasing;
                _phaseStartCount = count;
            }
        }

        // The eye is watched, not counted on.
        //
        // Accumulated product travels *touching*, so a batch coming past the
        // blade breaks the beam once and clears it once however many cartons
        // are in it — an eye cannot separate two cartons with no gap, which is
        // true of a real eye too. What it can honestly say is whether anything
        // is moving past the stop at all, which is what makes a blade that has
        // seized down visible from the tag list: the controller is commanding
        // `raise`, and product is still going by.
        _flowSeen = OperatorStation.Bit(tags, "exit_eye.detect");

        Apply(tags);
    }

    private void Apply(TagTable tags)
    {
        OperatorStation.Set(tags, "buffer.run", _station.Running);
        OperatorStation.Set(tags, "buffer.speed", _station.Running ? DriveReference : 0.0);
        OperatorStation.Set(tags, "outfeed.rotate", _station.Running);
        OperatorStation.Set(tags, "emitter.emit", _emitFlag);

        // Raised while accumulating, dropped while releasing. Raised too when
        // the line is stopped, for the reason above.
        OperatorStation.Set(tags, "stop.raise", !_releasing || !_station.Running);

        // The remover's own count, not a count of beam breaks -- see Tick.
        OperatorStation.Set(tags, "count_display.value",
                            (int)OperatorStation.Num(tags, "released.count"));

        _station.Lamps(tags);
        // Yellow while the buffer is filling. This is the one scene that lights
        // two stages at once, deliberately: green still means "the line is
        // running", and amber is the second thing worth annunciating here --
        // that the line is running and *holding*, which on a real buffer is
        // exactly the condition an operator wants to see from across the floor.
        // Set after Lamps, which owns the stopped-but-healthy meaning of amber.
        if (_station.Running && !_releasing) OperatorStation.Set(tags, "tower.yellow", true);
        // Something is moving past the stop. Only read here so the field is not
        // dead; a line that wants a flow alarm has its input already.
        if (_flowSeen && !_releasing) OperatorStation.Set(tags, "panel.red", true);
    }
}
