using System.Collections.Generic;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// Dynamically registers and manages tags for newly placed factory components in the TagBus.
/// </summary>
public static class PartTagManager
{
    private static readonly Dictionary<string, int> TypeCounters = new();

    public static void ResetCounters()
    {
        TypeCounters.Clear();
    }

    public static string RegisterPartTags(Node3D partNode, string partType, TagTable tags)
    {
        if (!TypeCounters.ContainsKey(partType))
            TypeCounters[partType] = 0;

        TypeCounters[partType]++;
        int index = TypeCounters[partType];
        string instanceId = $"{partType.ToLower()}_{index}";

        switch (partType)
        {
            case "ConveyorBelt":
                tags.Add(new Tag($"{instanceId}.rotate", $"Conveyor {index} (Rotate)", TagType.Bit, TagKind.Output));
                break;

            case "PhotoelectricSensor":
                tags.Add(new Tag($"{instanceId}.detect", $"Sensor {index} (Detect)", TagType.Bit, TagKind.Input));
                break;

            case "PusherMechanism":
                tags.Add(new Tag($"{instanceId}.extend", $"Pusher {index} (Extend)", TagType.Bit, TagKind.Output));
                tags.Add(new Tag($"{instanceId}.extended", $"Pusher {index} (Extended)", TagType.Bit, TagKind.Input));
                tags.Add(new Tag($"{instanceId}.retracted", $"Pusher {index} (Retracted)", TagType.Bit, TagKind.Input));
                tags.Set($"{instanceId}.retracted", true);
                break;

            case "Emitter":
                tags.Add(new Tag($"{instanceId}.emit", $"Emitter {index} (Emit)", TagType.Bit, TagKind.Output));
                break;

            case "Remover":
                tags.Add(new Tag($"{instanceId}.count", $"Remover {index} (Count)", TagType.Int, TagKind.Input));
                break;

            case "ButtonPanel":
                tags.Add(new Tag($"{instanceId}.green", $"Panel {index} Green Lamp", TagType.Bit, TagKind.Output));
                tags.Add(new Tag($"{instanceId}.red", $"Panel {index} Red Lamp", TagType.Bit, TagKind.Output));
                break;

            case "StackLight":
                tags.Add(new Tag($"{instanceId}.green", $"StackLight {index} Green", TagType.Bit, TagKind.Output));
                tags.Add(new Tag($"{instanceId}.yellow", $"StackLight {index} Yellow", TagType.Bit, TagKind.Output));
                tags.Add(new Tag($"{instanceId}.red", $"StackLight {index} Red", TagType.Bit, TagKind.Output));
                break;

            case "DigitalDisplay":
                tags.Add(new Tag($"{instanceId}.value", $"Display {index} Value", TagType.Int, TagKind.Output));
                break;

            case "WeighingConveyor":
                tags.Add(new Tag($"{instanceId}.rotate", $"WeighConveyor {index} Rotate", TagType.Bit, TagKind.Output));
                tags.Add(new Tag($"{instanceId}.weight", $"WeighConveyor {index} Weight", TagType.Int, TagKind.Input));
                break;
        }

        return instanceId;
    }
}
