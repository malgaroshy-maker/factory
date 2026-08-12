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

    /// <summary>Instance ids the default scene registers its parts under. The id
    /// is the tag *prefix*, so "pusher" resolves pusher.extend and friends.</summary>
    public const string ConveyorId = "conveyor";
    public const string EmitterId = "emitter";
    public const string PusherId = "pusher";
    public const string StackLightId = "stack_light";
    public const string SensorLowId = "sensor_low";
    public const string SensorHighId = "sensor_high";

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

        tags.Set(PusherRetracted, true);
    }
}
