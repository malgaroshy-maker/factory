using FactoryForge.TagBus;

namespace FactoryForge.Sim.DemoProfiles;

/// <summary>
/// Sorting on a measurement instead of two bits (§4.4). The curtain reports how
/// tall each carton is; everything else about the line is the same shape as the
/// reference sorting line, so the interesting line is one comparison against
/// <see cref="TallThreshold"/> -- move that one constant and the line re-sorts,
/// with no rewiring.
/// </summary>
public sealed class LightCurtainSortingProfile : IDemoProfile
{
    private const double EmitHalfPeriod = 1.5;

    /// <summary>Metres above the belt. Short cartons (0.10 m tall) block beams
    /// up to roughly 0.10 m; tall cartons (0.30 m) block beams up to roughly
    /// 0.27 m -- this sits cleanly between the two.</summary>
    private const float TallThreshold = 0.15f;

    /// <summary>Belt runs at 0.5 m/s and the curtain sits 1.0 m upstream of the
    /// diverter, so a box takes ~2 s to arrive. Same idea as the reference
    /// line's PushDelay, just re-derived for this scene's own geometry.</summary>
    private const double PushDelay = 2.0;

    /// <summary>How long to hold the diverter out once it confirms it reached
    /// full stroke, so the box actually clears the lane before it retracts --
    /// the handshake is on <c>diverter.extended</c>, this is only the dwell
    /// after that acknowledgement, not a blind guess at the whole cycle.</summary>
    private const double ClearDwell = 0.4;

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
        Set(tags, "belt.rotate", true);
        Set(tags, "diverter.extend", false);
    }

    public void Tick(double delta, TagTable tags)
    {
        _elapsed += delta;
        if (_elapsed >= _nextToggle)
        {
            _emitFlag = !_emitFlag;
            _nextToggle = _elapsed + EmitHalfPeriod;
            Set(tags, "emitter.emit", _emitFlag);
        }

        bool blocked = Bit(tags, "height_gauge.blocked");
        if (blocked && !_prevBlocked)
        {
            double height = tags.Contains("height_gauge.height")
                ? System.Convert.ToDouble(tags.Visible("height_gauge.height")) : 0.0;
            if (height >= TallThreshold) _extendAt = _elapsed + PushDelay;
        }
        _prevBlocked = blocked;

        if (!_extending && _extendAt is { } extendAt && _elapsed >= extendAt)
        {
            Set(tags, "diverter.extend", true);
            _extending = true;
            _extendAt = null;
        }

        // Retract only once the mechanism confirms it actually got there --
        // the handshake the spec asks for, rather than guessing a stroke time.
        if (_extending && _retractAt is null && Bit(tags, "diverter.extended"))
        {
            _retractAt = _elapsed + ClearDwell;
        }
        if (_retractAt is { } retractAt && _elapsed >= retractAt)
        {
            Set(tags, "diverter.extend", false);
            _extending = false;
            _retractAt = null;
        }
    }

    private static bool Bit(TagTable tags, string tagId) =>
        tags.Contains(tagId) && tags.Visible(tagId) is true;

    private static void Set(TagTable tags, string tagId, object value)
    {
        if (tags.Contains(tagId)) tags.Set(tagId, value);
    }
}
