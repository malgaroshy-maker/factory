using FactoryForge.TagBus;

namespace FactoryForge.Sim.DemoProfiles;

/// <summary>
/// The reference line (§4.1), now answering to its own control panel (OP-03).
///
/// The program is the one <see cref="DemoDriver"/> used to hardcode, with two
/// things added that the scene always had hardware for and never used: the
/// operator station gates the line, and the push delay comes off the panel's
/// pot instead of a constant here. That pot is the timing adjustment every
/// real diverter has — fire too early and the plate hits the carton on the
/// nose, too late and it sails past — so it belongs on the panel where an
/// operator can reach it, not in this file where only a programmer can.
/// </summary>
public sealed class SortingByHeightProfile : IDemoProfile
{
    private const double EmitHalfPeriod = 1.5;
    private const double PushHold = 0.5;

    /// <summary>Used only on a line built without a panel; the shipped scene
    /// always has one, and its pot ships set to this same value.</summary>
    private const double DefaultPushDelay = 0.9;

    private readonly OperatorStation _station = new OperatorStation("panel", "conveyor.fault", "pusher.fault");

    private double _elapsed;
    private bool _emitFlag;
    private double _nextToggle;
    private bool _highSeen;
    private double? _extendAt;
    private double? _retractAt;

    public void Start(TagTable tags)
    {
        _elapsed = 0;
        _nextToggle = EmitHalfPeriod;
        _emitFlag = false;
        _highSeen = false;
        _extendAt = null;
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

        bool high = OperatorStation.Bit(tags, "sensor_high.detect");
        if (high && !_highSeen && _station.Running)
        {
            // Read fresh at the moment the beam breaks, not latched at
            // startup: turning the knob has to change the next carton, not
            // the next run.
            _extendAt = _elapsed + _station.Setpoint(tags, DefaultPushDelay);
        }
        _highSeen = high;

        bool extend = OperatorStation.Bit(tags, "pusher.extend");
        if (_extendAt is { } extendAt && _elapsed >= extendAt && _station.Running)
        {
            extend = true;
            _retractAt = _elapsed + PushHold;
            _extendAt = null;
        }
        if (_retractAt is { } retractAt && _elapsed >= retractAt)
        {
            extend = false;
            _retractAt = null;
        }
        if (!_station.Running)
        {
            // A stopped line leaves nothing held out across the lane.
            extend = false;
            _extendAt = null;
        }

        Apply(tags, extend);
    }

    private void Apply(TagTable tags, bool extend)
    {
        OperatorStation.Set(tags, "conveyor.rotate", _station.Running);
        OperatorStation.Set(tags, "emitter.emit", _emitFlag);
        OperatorStation.Set(tags, "pusher.extend", extend);
        _station.Lamps(tags);
    }
}
