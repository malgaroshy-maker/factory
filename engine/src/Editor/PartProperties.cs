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
            p["speed"] = N(belt.Speed);
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
        }
    }

    private static float? Num(IDictionary<string, string> p, string key) =>
        p.TryGetValue(key, out var raw)
        && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v : null;

    private static bool? Bool(IDictionary<string, string> p, string key) =>
        p.TryGetValue(key, out var raw) ? raw == "true" : null;
}
