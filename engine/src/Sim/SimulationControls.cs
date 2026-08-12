using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Run / pause / reset and time scale for the simulation.
///
/// Pausing is what makes a simulator usable for debugging PLC logic: you freeze
/// the line mid-cycle and read every sensor and actuator at that instant,
/// instead of trying to catch a 200 ms pulse by eye. Slow motion does the same
/// job for sequences that are too fast to follow, and fast forward skips the
/// boring parts of a long cycle.
///
/// Implemented with <see cref="Engine.TimeScale"/> so it applies to *everything*
/// at once — the fixed-timestep accumulator, Jolt's physics steps, and every
/// part animation — rather than each system needing its own notion of pause.
/// The tag bus is deliberately left running: it polls from `_Process`, which
/// Godot still calls at time scale 0, so a paused scene keeps its PLC connected
/// and stays readable rather than dropping the session.
/// </summary>
public partial class SimulationControls : Node
{
    /// <summary>Selectable rates, mirroring what a technician actually wants:
    /// quarter speed to watch an interlock, up to 4x to skip a long cycle.</summary>
    public static readonly float[] Rates = { 0.25f, 0.5f, 1.0f, 2.0f, 4.0f };

    [Signal] public delegate void StateChangedEventHandler(bool paused, float rate);

    public bool Paused { get; private set; }
    public float Rate { get; private set; } = 1.0f;

    public void SetPaused(bool paused)
    {
        Paused = paused;
        Apply();
    }

    public void TogglePause() => SetPaused(!Paused);

    public void SetRate(float rate)
    {
        Rate = rate;
        // Changing speed while paused should not secretly resume; the rate is
        // remembered and takes effect when the user presses play.
        Apply();
    }

    private void Apply()
    {
        Engine.TimeScale = Paused ? 0.0f : Rate;
        EmitSignal(SignalName.StateChanged, Paused, Rate);
    }

    public override void _Ready() => Apply();

    /// <summary>Time scale is global engine state, so leaving it at 0 or 4x
    /// would poison anything that ran afterwards.</summary>
    public override void _ExitTree() => Engine.TimeScale = 1.0;
}
