using System.Collections.Generic;
using System.Globalization;
using FactoryForge.Parts;
using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// Captures and restores the settings that make a placed part *this* part
/// rather than a default one.
///
/// A scene file used to store only position and rotation, so saving and
/// reloading silently reset every tuned value — belt speeds, sensor ranges,
/// tank rates. Two of those resets were not merely annoying: a remover lost the
/// tag it counts into, and a sensor lost the flag saying the simulation owns its
/// tag, so a reloaded deterministic scene would have had its sensors fighting
/// the scene for the same tag.
///
/// Values are stored as invariant-culture strings so numbers, bools and tag
/// names all travel through the same map, and an unknown key is ignored rather
/// than breaking a scene saved by a newer build.
/// </summary>
public static class PartProperties
{
    private static string N(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    private static string N(int v) => v.ToString(CultureInfo.InvariantCulture);
    private static string N(bool v) => v ? "true" : "false";

    public static Dictionary<string, string> Capture(Node3D node)
    {
        var p = new Dictionary<string, string>();

        // Roller and weighing conveyors are ConveyorBelt subclasses and share
        // its settings, so this covers all three.
        if (node is ConveyorBelt belt)
        {
            // A VFD belt's Speed is not a setting: the drive recomputes it from
            // the speed reference on every dispatch tick. Saving it would store
            // whatever the drive happened to be doing at the moment of the save
            // and restore it as if it were a chosen value — a number that looks
            // like configuration and is really a sample. MaxSpeed, saved below
            // by the VariableConveyor case, is the setting.
            if (belt is not VariableConveyor) p["speed"] = N(belt.Speed);
            p["friction"] = N(belt.SurfaceFriction);
            p["size_x"] = N(belt.Size.X);
            p["size_y"] = N(belt.Size.Y);
            p["size_z"] = N(belt.Size.Z);
        }

        switch (node)
        {
            case PhotoelectricSensor sensor:
                p["range"] = N(sensor.Range);
                p["height"] = N(sensor.HeightAboveBelt);
                p["mode"] = sensor.Mode.ToString();
                p["visual_only"] = N(sensor.VisualOnly);
                break;

            case PusherMechanism pusher:
                p["stroke"] = N(pusher.StrokeLength);
                p["speed"] = N(pusher.ExtendSpeed);
                p["max_carton"] = N(pusher.MaxCartonHeight);
                p["visual_only"] = N(pusher.VisualOnly);
                break;

            case Chute chute:
                p["incline"] = N(chute.InclineAngleDegrees);
                p["friction"] = N(chute.SurfaceFriction);
                p["ramp_length"] = N(chute.RampLength);
                break;

            case LevelTank tank:
                p["fill_rate"] = N(tank.FillRate);
                p["drain_rate"] = N(tank.DrainRate);
                p["capacity"] = N(tank.CapacityLitres);
                break;

            case LightArray curtain:
                p["beams"] = N(curtain.BeamCount);
                p["curtain_height"] = N(curtain.CurtainHeight);
                p["range"] = N(curtain.Range);
                break;

            case Emitter emitter:
                p["metal_every"] = N(emitter.MetalEvery);
                break;

            case Remover remover:
                p["count_tag"] = remover.CountTag;
                p["zone_x"] = N(remover.ZoneSize.X);
                p["zone_y"] = N(remover.ZoneSize.Y);
                p["zone_z"] = N(remover.ZoneSize.Z);
                break;

            case DigitalDisplay display:
                p["unit"] = display.Unit;
                break;

            // The scale plate the pot is graduated against, plus where the
            // pointer was left. Saving the live value rather than a separate
            // "default" is deliberate: a real pot does not spring back, and a
            // scene reloaded mid-tuning should reopen where you left it.
            case ButtonPanel panel:
                p["setpoint_min"] = N(panel.SetpointMin);
                p["setpoint_max"] = N(panel.SetpointMax);
                p["setpoint_unit"] = panel.SetpointUnit;
                p["setpoint"] = N(panel.Setpoint);
                break;

            // VariableConveyor is a ConveyorBelt subclass, so the belt block
            // above already carried its speed, friction and size; these are the
            // two settings that make it a *drive* rather than a belt.
            case VariableConveyor vfd:
                p["max_speed"] = N(vfd.MaxSpeed);
                p["accel_rate"] = N(vfd.AccelRate);
                break;

            case PivotDiverter diverter:
                p["divert_angle"] = N(diverter.DivertAngle);
                p["swing_speed"] = N(diverter.SwingSpeed);
                p["blade_length"] = N(diverter.BladeLength);
                break;

            case PickPlaceArm arm:
                p["rail_length"] = N(arm.RailLength);
                p["travel_speed"] = N(arm.TravelSpeed);
                p["stroke"] = N(arm.StrokeLength);
                p["lower_speed"] = N(arm.LowerSpeed);
                p["tolerance"] = N(arm.PositionTolerance);
                break;

            case BarcodeScanner scanner:
                p["height"] = N(scanner.HeightAboveBelt);
                p["window"] = N(scanner.WindowLength);
                break;

            case AnalogGauge gauge:
                p["scale_min"] = N(gauge.ScaleMin);
                p["scale_max"] = N(gauge.ScaleMax);
                p["alarm_at"] = N(gauge.AlarmAt);
                p["unit"] = gauge.Unit;
                break;

            case AlarmBeacon beacon:
                p["rotation_speed"] = N(beacon.RotationSpeed);
                p["colour_r"] = N(beacon.BeaconColour.R);
                p["colour_g"] = N(beacon.BeaconColour.G);
                p["colour_b"] = N(beacon.BeaconColour.B);
                break;

            case SelectorSwitch selector:
                p["positions"] = N(selector.PositionCount);
                p["labels"] = selector.Labels;
                // Where the switch was left. A real selector does not spring
                // back, and a scene reopened mid-experiment should reopen in
                // the mode it was in -- the same reasoning as the panel pot.
                p["detent"] = N(selector.Detent);
                break;

            case SafetyGate gate:
                p["travel"] = N(gate.TravelDistance);
                p["slide_speed"] = N(gate.SlideSpeed);
                break;

            case HeatingStation heater:
                p["heater_power"] = N(heater.HeaterPower);
                p["loss_rate"] = N(heater.LossRate);
                p["thermal_mass"] = N(heater.ThermalMass);
                p["ambient"] = N(heater.Ambient);
                p["target_temp"] = N(heater.TargetTemp);
                p["tolerance"] = N(heater.Tolerance);
                break;
        }

        return p;
    }

    /// <summary>
    /// Apply saved settings. Must run <em>before</em> the node enters the tree:
    /// most parts build their geometry from these values in _Ready, so setting
    /// them afterwards would leave the mesh describing the old configuration.
    /// </summary>
    public static void Apply(Node3D node, IDictionary<string, string>? props)
    {
        if (props is null || props.Count == 0) return;

        if (node is ConveyorBelt belt)
        {
            if (Num(props, "speed") is { } speed) belt.Speed = speed;
            if (Num(props, "friction") is { } friction) belt.SurfaceFriction = friction;
            if (Num(props, "size_x") is { } sx && Num(props, "size_y") is { } sy
                                               && Num(props, "size_z") is { } sz)
                belt.Size = new Vector3(sx, sy, sz);
        }

        switch (node)
        {
            case PhotoelectricSensor sensor:
                if (Num(props, "range") is { } range) sensor.Range = range;
                if (Num(props, "height") is { } height) sensor.HeightAboveBelt = height;
                if (props.TryGetValue("mode", out var mode)
                    && System.Enum.TryParse<SensingMode>(mode, out var parsed))
                    sensor.Mode = parsed;
                if (Bool(props, "visual_only") is { } sv) sensor.VisualOnly = sv;
                break;

            case PusherMechanism pusher:
                if (Num(props, "stroke") is { } stroke) pusher.StrokeLength = stroke;
                if (Num(props, "speed") is { } pspeed) pusher.ExtendSpeed = pspeed;
                if (Num(props, "max_carton") is { } carton) pusher.MaxCartonHeight = carton;
                if (Bool(props, "visual_only") is { } pv) pusher.VisualOnly = pv;
                break;

            case Chute chute:
                if (Num(props, "incline") is { } incline) chute.InclineAngleDegrees = incline;
                if (Num(props, "friction") is { } cfriction) chute.SurfaceFriction = cfriction;
                if (Num(props, "ramp_length") is { } ramp) chute.RampLength = ramp;
                break;

            case LevelTank tank:
                if (Num(props, "fill_rate") is { } fill) tank.FillRate = fill;
                if (Num(props, "drain_rate") is { } drain) tank.DrainRate = drain;
                if (Num(props, "capacity") is { } capacity) tank.CapacityLitres = capacity;
                break;

            case LightArray curtain:
                if (Num(props, "beams") is { } beams) curtain.BeamCount = (int)beams;
                if (Num(props, "curtain_height") is { } ch) curtain.CurtainHeight = ch;
                if (Num(props, "range") is { } lrange) curtain.Range = lrange;
                break;

            case Emitter emitter:
                if (Num(props, "metal_every") is { } metal) emitter.MetalEvery = (int)metal;
                break;

            case Remover remover:
                if (props.TryGetValue("count_tag", out var tag)) remover.CountTag = tag;
                if (Num(props, "zone_x") is { } zx && Num(props, "zone_y") is { } zy
                                                   && Num(props, "zone_z") is { } zz)
                    remover.ZoneSize = new Vector3(zx, zy, zz);
                break;

            case DigitalDisplay display:
                if (props.TryGetValue("unit", out var unit)) display.Unit = unit;
                break;

            case ButtonPanel panel:
                if (Num(props, "setpoint_min") is { } spMin) panel.SetpointMin = spMin;
                if (Num(props, "setpoint_max") is { } spMax) panel.SetpointMax = spMax;
                if (props.TryGetValue("setpoint_unit", out var spUnit)) panel.SetpointUnit = spUnit;
                // Last, so the clamp sees the range this template asked for
                // rather than the default 0-100 one.
                if (Num(props, "setpoint") is { } sp) panel.SetSetpoint(sp);
                break;

            case VariableConveyor vfd:
                if (Num(props, "max_speed") is { } maxSpeed) vfd.MaxSpeed = maxSpeed;
                if (Num(props, "accel_rate") is { } accel) vfd.AccelRate = accel;
                break;

            case PivotDiverter diverter:
                if (Num(props, "divert_angle") is { } divertAngle) diverter.DivertAngle = divertAngle;
                if (Num(props, "swing_speed") is { } swing) diverter.SwingSpeed = swing;
                if (Num(props, "blade_length") is { } blade) diverter.BladeLength = blade;
                break;

            case PickPlaceArm arm:
                if (Num(props, "rail_length") is { } rail) arm.RailLength = rail;
                if (Num(props, "travel_speed") is { } travel) arm.TravelSpeed = travel;
                if (Num(props, "stroke") is { } armStroke) arm.StrokeLength = armStroke;
                if (Num(props, "lower_speed") is { } lowerSpeed) arm.LowerSpeed = lowerSpeed;
                if (Num(props, "tolerance") is { } tolerance) arm.PositionTolerance = tolerance;
                break;

            case BarcodeScanner scanner:
                if (Num(props, "height") is { } scanHeight) scanner.HeightAboveBelt = scanHeight;
                if (Num(props, "window") is { } window) scanner.WindowLength = window;
                break;

            case AnalogGauge gauge:
                if (Num(props, "scale_min") is { } gMin) gauge.ScaleMin = gMin;
                if (Num(props, "scale_max") is { } gMax) gauge.ScaleMax = gMax;
                if (Num(props, "alarm_at") is { } gAlarm) gauge.AlarmAt = gAlarm;
                if (props.TryGetValue("unit", out var gUnit)) gauge.Unit = gUnit;
                break;

            case AlarmBeacon beacon:
                if (Num(props, "rotation_speed") is { } beaconSpin) beacon.RotationSpeed = beaconSpin;
                if (Num(props, "colour_r") is { } cr && Num(props, "colour_g") is { } cg
                                                     && Num(props, "colour_b") is { } cb)
                    beacon.BeaconColour = new Color(cr, cg, cb);
                break;

            case SelectorSwitch selector:
                if (Num(props, "positions") is { } positions)
                    selector.PositionCount = (int)positions;
                if (props.TryGetValue("labels", out var labels)) selector.Labels = labels;
                // Last, so the clamp sees the detent count this scene asked
                // for rather than the default three.
                if (Num(props, "detent") is { } detent) selector.Detent = (int)detent;
                break;

            case SafetyGate gate:
                if (Num(props, "travel") is { } gateTravel) gate.TravelDistance = gateTravel;
                if (Num(props, "slide_speed") is { } slide) gate.SlideSpeed = slide;
                break;

            case HeatingStation heater:
                if (Num(props, "heater_power") is { } hp) heater.HeaterPower = hp;
                if (Num(props, "loss_rate") is { } loss) heater.LossRate = loss;
                if (Num(props, "thermal_mass") is { } mass) heater.ThermalMass = mass;
                if (Num(props, "ambient") is { } ambient) heater.Ambient = ambient;
                if (Num(props, "target_temp") is { } target) heater.TargetTemp = target;
                if (Num(props, "tolerance") is { } band) heater.Tolerance = band;
                break;
        }
    }

    private static float? Num(IDictionary<string, string> p, string key) =>
        p.TryGetValue(key, out var raw)
        && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : null;

    private static bool? Bool(IDictionary<string, string> p, string key) =>
        p.TryGetValue(key, out var raw) ? raw == "true" : null;
}
