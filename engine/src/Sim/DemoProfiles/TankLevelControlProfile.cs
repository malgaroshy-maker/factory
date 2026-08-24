using Godot;
using FactoryForge.TagBus;

namespace FactoryForge.Sim.DemoProfiles;

/// <summary>
/// Analog end to end (§4.3, OP-05). A proportional controller holding whatever
/// the panel's pot is set to — deliberately not a tuned PID with anti-windup
/// and the rest. This is the exercise a student is meant to read, delete, and
/// replace with their own; a polished controller would be a worse example, not
/// a better one.
///
/// The setpoint used to be a constant in this file, which made the scene's own
/// lesson unreachable without an editor: outflow follows Torricelli (see
/// <c>LevelTank.Step</c>), so process gain falls as the tank empties, and a
/// fixed proportional gain that holds 70% steadily will crawl toward 20%.
/// Turning the knob mid-run is how you see that, and it is now a knob.
///
/// Stopped means stopped: both valves shut. An E-stop that leaves the fill
/// valve open is not an E-stop, and this scene had no way to strike one at all
/// until the panel was wired up.
/// </summary>
public sealed class TankLevelControlProfile : IDemoProfile
{
    /// <summary>Used only on a tank built without a panel; the shipped
    /// template's pot ships set to 70%.</summary>
    private const float DefaultSetpointPercent = 55.0f;

    /// <summary>Valve-percent opened per percent of error. Chosen so a
    /// full-range error does not slam the valve past what a real modulating
    /// valve would see, while still converging in well under the exercise's own
    /// 10-second settling window.</summary>
    private const float Gain = 4.0f;

    private readonly OperatorStation _station = new OperatorStation("panel", "tank.fault");

    public void Start(TagTable tags)
    {
        _station.Begin();
        OperatorStation.Set(tags, "tank.fill", 0.0);
        OperatorStation.Set(tags, "tank.drain", 0.0);
        _station.Lamps(tags);
    }

    public void Tick(double delta, TagTable tags)
    {
        _station.Scan(tags);
        _station.Lamps(tags);

        if (!tags.Contains("tank.level")) return;

        double level = OperatorStation.Num(tags, "tank.level");
        float fill = 0.0f, drain = 0.0f;

        if (_station.Running)
        {
            float setpoint = (float)_station.Setpoint(tags, DefaultSetpointPercent);
            float error = setpoint - (float)level;
            if (error > 0) fill = Mathf.Clamp(error * Gain, 0.0f, 100.0f);
            else drain = Mathf.Clamp(-error * Gain, 0.0f, 100.0f);
        }

        OperatorStation.Set(tags, "tank.fill", (double)fill);
        OperatorStation.Set(tags, "tank.drain", (double)drain);
        OperatorStation.Set(tags, "level_readout.value", Mathf.RoundToInt((float)level));
    }
}
