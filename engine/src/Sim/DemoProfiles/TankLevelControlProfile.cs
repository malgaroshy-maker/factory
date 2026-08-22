using Godot;
using FactoryForge.TagBus;

namespace FactoryForge.Sim.DemoProfiles;

/// <summary>
/// Analog end to end (§4.3). A proportional controller holding one setpoint --
/// deliberately not a tuned PID with anti-windup and the rest. This is the
/// exercise a student is meant to read, delete, and replace with their own; a
/// polished controller would be a worse example, not a better one.
///
/// Outflow follows Torricelli (see <c>LevelTank.Step</c>), so process gain
/// falls as the tank empties -- a fixed proportional gain will hold this
/// setpoint steadily but would not necessarily hold a much lower one the same
/// way. That contrast is the lesson `tools/try_scene.py` (UX-22) checks for;
/// this profile only has to demonstrate the scene moving convincingly.
/// </summary>
public sealed class TankLevelControlProfile : IDemoProfile
{
    private const float SetpointPercent = 55.0f;

    /// <summary>Valve-percent opened per percent of error. Chosen so a full-range
    /// error (55%) does not slam the valve past what a real modulating valve
    /// would see, while still converging in well under the exercise's own
    /// 10-second settling window.</summary>
    private const float Gain = 4.0f;

    public void Start(TagTable tags)
    {
        Set(tags, "tank.fill", 0.0);
        Set(tags, "tank.drain", 0.0);
    }

    public void Tick(double delta, TagTable tags)
    {
        if (!tags.Contains("tank.level")) return;

        double level = System.Convert.ToDouble(tags.Visible("tank.level"));
        float error = SetpointPercent - (float)level;

        float fill = 0.0f, drain = 0.0f;
        if (error > 0) fill = Mathf.Clamp(error * Gain, 0.0f, 100.0f);
        else drain = Mathf.Clamp(-error * Gain, 0.0f, 100.0f);

        Set(tags, "tank.fill", (double)fill);
        Set(tags, "tank.drain", (double)drain);
        Set(tags, "level_readout.value", Mathf.RoundToInt((float)level));
    }

    private static void Set(TagTable tags, string tagId, object value)
    {
        if (tags.Contains(tagId)) tags.Set(tagId, value);
    }
}
