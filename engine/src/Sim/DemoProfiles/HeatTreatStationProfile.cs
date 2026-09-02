using Godot;
using FactoryForge.TagBus;

namespace FactoryForge.Sim.DemoProfiles;

/// <summary>
/// The heat treat station (CP-30). Proportional-plus-integral control on a
/// first-order thermal plant, and it is deliberately written to *show* why the
/// integral term is there rather than to hide it.
///
/// The plant loses heat to ambient in proportion to how far above ambient it
/// is, so holding a temperature needs a standing heater output. Pure
/// proportional control cannot produce one without a standing error — the
/// output *is* gain times error — so a P-only controller parks a few degrees
/// low and stays there. That offset is the whole lesson, and the integral term
/// is what closes it — measured, not asserted: `tools/try_scene.py --scene
/// heat-treat-station` runs this plant with the integral switched off, records
/// the standing error, switches it on, and checks the error actually goes away.
///
/// The tank profile next door is a proportional controller with no integral at
/// all, and does not need one: its process has no equivalent standing loss.
/// The two scenes make the point together.
/// </summary>
public sealed class HeatTreatStationProfile : IDemoProfile
{
    /// <summary>Heater percent per degree of error.</summary>
    private const float Gain = 3.5f;

    /// <summary>Heater percent per degree-second of accumulated error.
    ///
    /// Enough authority that the integral term can supply the whole standing
    /// output on its own. At 180 °C the plate loses (180−20)·0.30 ≈ 48 °C/s of
    /// heat, which is a little over half the element; an integral capped at
    /// about 11 % of it — the first tuning here — cannot close the offset it
    /// was added to close, and reads as "integral action does not work".
    /// </summary>
    private const float IntegralGain = 0.6f;

    /// <summary>Clamp on the integrator. Without it a cold start winds the term
    /// up over the whole ramp and the plate overshoots badly — which is a real
    /// failure worth knowing about, and not one this demo should be
    /// demonstrating by accident.</summary>
    private const float IntegralLimit = 140.0f;

    private const float DefaultSetpoint = 180.0f;

    private readonly OperatorStation _station = new OperatorStation("panel", "oven.fault");

    private float _integral;

    public void Start(TagTable tags)
    {
        _station.Begin();
        _integral = 0.0f;
        OperatorStation.Set(tags, "oven.heater", 0.0);
        _station.Lamps(tags);
    }

    public void Tick(double delta, TagTable tags)
    {
        _station.Scan(tags);
        _station.Lamps(tags);

        if (!tags.Contains("oven.temperature")) return;

        float temperature = (float)OperatorStation.Num(tags, "oven.temperature");
        float setpoint = (float)_station.Setpoint(tags, DefaultSetpoint);
        float power = 0.0f;

        if (_station.Running)
        {
            float error = setpoint - temperature;

            // Integrate only while the output is not already saturated. Doing
            // it unconditionally is textbook windup: on a cold start the
            // heater is flat out for a minute and cannot go higher, so every
            // degree-second of that minute would accumulate into an overshoot
            // the controller then has to unwind.
            float proportional = error * Gain;
            if (proportional > -100.0f && proportional < 100.0f)
            {
                _integral = Mathf.Clamp(_integral + error * (float)delta,
                                        -IntegralLimit, IntegralLimit);
            }

            power = Mathf.Clamp(proportional + _integral * IntegralGain, 0.0f, 100.0f);
        }
        else
        {
            // Stopped means the element is off. It does not mean cold — the
            // plate carries its heat, which is the difference between stopping
            // a conveyor and stopping a furnace, and the reason a real one has
            // a cool-down interlock.
            _integral = 0.0f;
        }

        OperatorStation.Set(tags, "oven.heater", (double)power);
        OperatorStation.Set(tags, "temp_gauge.value", (double)temperature);
        OperatorStation.Set(tags, "temp_readout.value", Mathf.RoundToInt(temperature));

        // The beacon is for the condition somebody has to attend to: an
        // over-temperature, or a failed element. Not for "running".
        bool overTemp = temperature > setpoint + 25.0f;
        OperatorStation.Set(tags, "alarm.beacon", overTemp || _station.DriveFaulted);
        OperatorStation.Set(tags, "alarm.horn", _station.DriveFaulted);
    }
}
