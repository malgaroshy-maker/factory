using FactoryForge.TagBus;

namespace FactoryForge.Sim.DemoProfiles;

/// <summary>
/// The reference line (§4.1). Unchanged from what <see cref="DemoDriver"/> used
/// to hardcode directly -- moved here rather than rewritten, so this is the one
/// profile with nothing to verify empirically: it is the same program.
/// </summary>
public sealed class SortingByHeightProfile : IDemoProfile
{
    private const double EmitHalfPeriod = 1.5;
    private const double PushDelay = 0.9;
    private const double PushHold = 0.5;

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
        SetIfPresent(tags, "conveyor.rotate", true);
        SetIfPresent(tags, "stack_light.green", true);
    }

    public void Tick(double delta, TagTable tags)
    {
        _elapsed += delta;
        if (_elapsed >= _nextToggle)
        {
            _emitFlag = !_emitFlag;
            _nextToggle = _elapsed + EmitHalfPeriod;
            SetIfPresent(tags, "emitter.emit", _emitFlag);
        }

        bool high = GetBitIfPresent(tags, "sensor_high.detect");
        if (high && !_highSeen) _extendAt = _elapsed + PushDelay;
        _highSeen = high;

        if (_extendAt is { } extendAt && _elapsed >= extendAt)
        {
            SetIfPresent(tags, "pusher.extend", true);
            _retractAt = extendAt + PushHold;
            _extendAt = null;
        }
        if (_retractAt is { } retractAt && _elapsed >= retractAt)
        {
            SetIfPresent(tags, "pusher.extend", false);
            _retractAt = null;
        }
    }

    private static void SetIfPresent(TagTable tags, string tagId, object value)
    {
        if (tags.Contains(tagId)) tags.Set(tagId, value);
    }

    private static bool GetBitIfPresent(TagTable tags, string tagId) =>
        tags.Contains(tagId) && tags.Visible(tagId) is true;
}
