using FactoryForge.TagBus;

namespace FactoryForge.Scenes;

/// <summary>
/// The tag interface of the sorting scene — the contract a PLC program, a
/// Node-RED flow or a SCADA client codes against.
///
/// Declared in one place because two scenes present it: the fixed-timestep
/// <see cref="SortingScene"/> and the rigid-body scene assembled from real
/// parts. The same `Sorting.scl` has to drive either without noticing, so the
/// ids, types and kinds must not drift apart.
///
/// `kind` is from the *controller's* point of view: output = the PLC writes it,
/// input = the simulator writes it.
/// </summary>
public static class SortingTags
{
    public const string ConveyorRotate = "conveyor.rotate";
    public const string EmitterEmit = "emitter.emit";
    public const string PusherExtend = "pusher.extend";
    public const string StackLightGreen = "stack_light.green";

    public const string SensorLowDetect = "sensor_low.detect";
    public const string SensorHighDetect = "sensor_high.detect";
    public const string PusherExtended = "pusher.extended";
    public const string PusherRetracted = "pusher.retracted";
    public const string CounterTall = "counter.tall";
    public const string CounterShort = "counter.short";

    /// <summary>Drive fault contacts (FI-01). Inputs, like every other tag the
    /// simulator owns — but unlike the rest, nothing computes them: they are
    /// raised by whoever is playing maintenance. Declared here so the
    /// reference line can be broken exactly like a template can; a fault tool
    /// that silently did nothing on the flagship scene would be worse than no
    /// fault tool at all.</summary>
    public const string ConveyorFault = "conveyor.fault";
    public const string PusherFault = "pusher.fault";

    /// <summary>Instance ids the default scene registers its parts under. The id
    /// is the tag *prefix*, so "pusher" resolves pusher.extend and friends.</summary>
    public const string ConveyorId = "conveyor";
    public const string EmitterId = "emitter";
    public const string PusherId = "pusher";
    public const string StackLightId = "stack_light";
    public const string SensorLowId = "sensor_low";
    public const string SensorHighId = "sensor_high";

    /// <summary>Every tag id <see cref="Declare"/> adds.</summary>
    public static readonly string[] All =
    {
        ConveyorRotate, EmitterEmit, PusherExtend, StackLightGreen,
        SensorLowDetect, SensorHighDetect, PusherExtended, PusherRetracted,
        CounterTall, CounterShort, ConveyorFault, PusherFault,
    };

    /// <summary>
    /// Take the sorting line's tags back out of the table.
    ///
    /// Needed when the user switches to a different scene: these are
    /// declared by the engine at startup rather than owned by any part, so
    /// clearing the parts leaves them behind, and a tank scene would list a
    /// conveyor and two box counters that do not exist.
    ///
    /// Only safe when no <see cref="SortingScene"/> is running — that one writes
    /// these by name on every tick.
    /// </summary>
    public static void Undeclare(TagTable tags)
    {
        foreach (string id in All) tags.Remove(id);
    }

    public static void Declare(TagTable tags)
    {
        // PLC outputs — the program writes these.
        tags.Add(new Tag(ConveyorRotate, "Belt Conveyor (Rotate)", TagType.Bit, TagKind.Output));
        tags.Add(new Tag(EmitterEmit, "Emitter (Emit)", TagType.Bit, TagKind.Output));
        tags.Add(new Tag(PusherExtend, "Pusher (Extend)", TagType.Bit, TagKind.Output));
        tags.Add(new Tag(StackLightGreen, "Stack Light (Green)", TagType.Bit, TagKind.Output));

        // PLC inputs — the simulator writes these.
        tags.Add(new Tag(SensorLowDetect, "Diffuse Sensor Low (Detect)", TagType.Bit, TagKind.Input));
        tags.Add(new Tag(SensorHighDetect, "Diffuse Sensor High (Detect)", TagType.Bit, TagKind.Input));
        tags.Add(new Tag(PusherExtended, "Pusher (Extended)", TagType.Bit, TagKind.Input));
        tags.Add(new Tag(PusherRetracted, "Pusher (Retracted)", TagType.Bit, TagKind.Input));
        tags.Add(new Tag(CounterTall, "Counter (Tall)", TagType.Int, TagKind.Input));
        tags.Add(new Tag(CounterShort, "Counter (Short)", TagType.Int, TagKind.Input));
        tags.Add(new Tag(ConveyorFault, "Belt Conveyor Drive Fault", TagType.Bit, TagKind.Input));
        tags.Add(new Tag(PusherFault, "Pusher Drive Fault", TagType.Bit, TagKind.Input));

        tags.Set(PusherRetracted, true);
    }
}
