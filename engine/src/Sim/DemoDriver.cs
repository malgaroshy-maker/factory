using Godot;
using FactoryForge.TagBus;

namespace FactoryForge.Sim;

/// <summary>
/// An in-engine stand-in for a PLC, so the app demonstrates itself with
/// nothing installed. A first launch used to sit perfectly still — nothing
/// writes <c>conveyor.rotate</c> or <c>emitter.emit</c> unless a sidecar is
/// running, and the app shipped no way to see it move without one — which
/// reads as "this is broken" in the first thirty seconds. See FF-23.
///
/// Mirrors <c>tools/live_driver.py</c>'s logic exactly, but needs no Python:
/// runs the belt, pulses the emitter, and fires the pusher when the high
/// sensor sees a tall carton. Every write is conditional on the tag existing,
/// so this degrades to a no-op rather than an error on a scene that does not
/// define the reference sorting line's tags.
///
/// Stands down the instant a real driver connects — it must never fight a
/// PLC for the same tags, so <see cref="Bus"/> is checked every frame rather
/// than trusting whoever started this to also remember to stop it.
/// </summary>
public partial class DemoDriver : Node
{
    public TagTable Tags { get; set; } = null!;
    public TagBusServer Bus { get; set; } = null!;

    /// <summary>Raised when the demo starts or stops, including the automatic
    /// stand-down on a real driver connecting — so the toolbar toggle and the
    /// idle hint both follow the true state instead of only what they were
    /// told.</summary>
    [Signal] public delegate void ActiveChangedEventHandler(bool active);

    public bool Active { get; private set; }

    private const double EmitHalfPeriod = 1.5;
    private const double PushDelay = 0.9;
    private const double PushHold = 0.5;

    private double _elapsed;
    private bool _emitFlag;
    private double _nextToggle;
    private bool _highSeen;
    private double? _extendAt;
    private double? _retractAt;

    public void Start()
    {
        if (Active) return;
        Active = true;
        _elapsed = 0;
        _nextToggle = EmitHalfPeriod;
        _emitFlag = false;
        _highSeen = false;
        _extendAt = null;
        _retractAt = null;
        SetIfPresent("conveyor.rotate", true);
        SetIfPresent("stack_light.green", true);
        EmitSignal(SignalName.ActiveChanged, true);
    }

    public void Stop()
    {
        if (!Active) return;
        Active = false;
        // Leave outputs where they are. A real driver about to take over
        // writes them itself; snapping a running belt to "off" the instant a
        // PLC connects would look like a fault at the exact moment the
        // handoff is supposed to be invisible.
        EmitSignal(SignalName.ActiveChanged, false);
    }

    public override void _Process(double delta)
    {
        if (!Active) return;

        if (Bus.HasClient)
        {
            Stop();
            return;
        }

        _elapsed += delta;
        if (_elapsed >= _nextToggle)
        {
            _emitFlag = !_emitFlag;
            _nextToggle = _elapsed + EmitHalfPeriod;
            SetIfPresent("emitter.emit", _emitFlag);
        }

        bool high = GetBitIfPresent("sensor_high.detect");
        if (high && !_highSeen) _extendAt = _elapsed + PushDelay;
        _highSeen = high;

        if (_extendAt is { } extendAt && _elapsed >= extendAt)
        {
            SetIfPresent("pusher.extend", true);
            _retractAt = extendAt + PushHold;
            _extendAt = null;
        }
        if (_retractAt is { } retractAt && _elapsed >= retractAt)
        {
            SetIfPresent("pusher.extend", false);
            _retractAt = null;
        }
    }

    private void SetIfPresent(string tagId, object value)
    {
        if (Tags.Contains(tagId)) Tags.Set(tagId, value);
    }

    private bool GetBitIfPresent(string tagId) =>
        Tags.Contains(tagId) && Tags.Visible(tagId) is true;
}
