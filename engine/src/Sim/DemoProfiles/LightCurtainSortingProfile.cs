using FactoryForge.TagBus;

namespace FactoryForge.Sim.DemoProfiles;

/// <summary>
/// Sorting on a measurement instead of two bits (§4.4, OP-06). The curtain
/// reports how tall each carton is; everything else about the line is the same
/// shape as the reference sorting line, so the interesting line is one
/// comparison — and that comparison is now against the panel's pot rather than
/// a constant here.
///
/// That is the whole scene's point made reachable. "Move one constant and the
/// line re-sorts, with no rewiring" was true and required an editor and a
/// rebuild; turning a knob mid-run is the same claim with the rebuild taken
/// out, and it is the difference between reading the lesson and seeing it.
/// </summary>
public sealed class LightCurtainSortingProfile : IDemoProfile
{
    private const double EmitHalfPeriod = 1.5;

    /// <summary>Metres above the belt, used only on a line built without a
    /// panel. Short cartons (0.10 m tall) block beams up to roughly 0.10 m;
    /// tall cartons (0.30 m) block beams up to roughly 0.27 m — this sits
    /// cleanly between the two, and the shipped template's pot ships set to
    /// the same value.</summary>
    private const double DefaultTallThreshold = 0.15;

    /// <summary>Belt runs at 0.5 m/s and the curtain sits 1.0 m upstream of the
    /// diverter, so a box takes ~2 s to arrive. Same idea as the reference
    /// line's PushDelay, just re-derived for this scene's own geometry.</summary>
    private const double PushDelay = 2.0;

    /// <summary>How long to hold the diverter out once it confirms it reached
    /// full stroke, so the box actually clears the lane before it retracts --
    /// the handshake is on <c>diverter.extended</c>, this is only the dwell
    /// after that acknowledgement, not a blind guess at the whole cycle.</summary>
    private const double ClearDwell = 0.4;

    private readonly OperatorStation _station = new OperatorStation("panel", "belt.fault", "diverter.fault");

    private double _elapsed;
    private bool _emitFlag;
    private double _nextToggle;
    private bool _prevBlocked;
    private double? _extendAt;
    private bool _extending;
    private double? _retractAt;

    public void Start(TagTable tags)
    {
        _elapsed = 0;
        _emitFlag = false;
        _nextToggle = EmitHalfPeriod;
        _prevBlocked = false;
        _extendAt = null;
        _extending = false;
        _retractAt = null;
        _station.Begin();
        Apply(tags, extend: false);
    }

    public void Tick(double delta, TagTable tags)
    {
        _station.Scan(tags);
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

        bool blocked = OperatorStation.Bit(tags, "height_gauge.blocked");
        if (blocked && !_prevBlocked && _station.Running)
        {
            double height = OperatorStation.Num(tags, "height_gauge.height");
            // The pot, read at the moment of the measurement: turning it
            // re-sorts the next carton, not the next run.
            if (height >= _station.Setpoint(tags, DefaultTallThreshold))
                _extendAt = _elapsed + PushDelay;
        }
        _prevBlocked = blocked;

        bool extend = OperatorStation.Bit(tags, "diverter.extend");

        if (!_extending && _extendAt is { } extendAt && _elapsed >= extendAt)
        {
            extend = true;
            _extending = true;
            _extendAt = null;
        }

        // Retract only once the mechanism confirms it actually got there --
        // the handshake the spec asks for, rather than guessing a stroke time.
        if (_extending && _retractAt is null && OperatorStation.Bit(tags, "diverter.extended"))
        {
            _retractAt = _elapsed + ClearDwell;
        }
        if (_retractAt is { } retractAt && _elapsed >= retractAt)
        {
            extend = false;
            _extending = false;
            _retractAt = null;
        }

        if (!_station.Running)
        {
            extend = false;
            _extending = false;
            _extendAt = null;
            _retractAt = null;
        }

        Apply(tags, extend);
    }

    private void Apply(TagTable tags, bool extend)
    {
        OperatorStation.Set(tags, "belt.rotate", _station.Running);
        OperatorStation.Set(tags, "emitter.emit", _emitFlag);
        OperatorStation.Set(tags, "diverter.extend", extend);
        _station.Lamps(tags);
    }
}
