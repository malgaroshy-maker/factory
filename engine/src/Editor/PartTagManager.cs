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

    /// <summary>Does the table already hold tags for this instance?</summary>
    public static bool HasTagsFor(string instanceId, TagTable tags)
    {
        string prefix = instanceId + ".";
        foreach (var tag in tags)
        {
            if (tag.Id.StartsWith(prefix)) return true;
        }
        return false;
    }

    /// <summary>
    /// Remove every tag this manager registered for an instance. Ids are always
    /// "&lt;instanceId&gt;.&lt;suffix&gt;", so the prefix identifies them without
    /// needing to know which suffixes the part type used.
    /// </summary>
    public static void UnregisterPartTags(string instanceId, TagTable tags)
    {
        string prefix = instanceId + ".";
        var doomed = new List<string>();
        foreach (var tag in tags)
        {
            if (tag.Id.StartsWith(prefix)) doomed.Add(tag.Id);
        }
        foreach (var id in doomed) tags.Remove(id);
    }

    /// <summary>
    /// Register the tags a part type exposes.
    /// </summary>
    /// <param name="preferredId">Id to reuse instead of minting a fresh one —
    /// how a loaded scene keeps the wiring it was saved with. Without it, every
    /// load renamed the parts and left the reloaded scene driven by nothing.</param>
    /// <returns>The instance id, and whether this call actually created the tags.
    /// It did not when they already existed, which means the part is a view of
    /// tags something else owns (the default scene's belt and sensors), and the
    /// caller must not delete them with the part.</returns>
    public static (string Id, bool Owns) RegisterPartTags(Node3D partNode, string partType,
                                                          TagTable tags, string? preferredId = null)
    {
        if (!TypeCounters.ContainsKey(partType))
            TypeCounters[partType] = 0;

        string instanceId;
        if (preferredId is { Length: > 0 })
        {
            instanceId = preferredId;
            // Keep auto-numbering ahead of ids that arrived from a file, so a
            // part placed after a load cannot collide with one from it.
            int underscore = preferredId.LastIndexOf('_');
            if (underscore >= 0 && int.TryParse(preferredId[(underscore + 1)..], out int loaded))
                TypeCounters[partType] = System.Math.Max(TypeCounters[partType], loaded);
        }
        else
        {
            TypeCounters[partType]++;
            instanceId = $"{partType.ToLower()}_{TypeCounters[partType]}";
        }

        // Tags already under this prefix belong to whoever created them first —
        // the simulation, for the default scene's belt and sensors. Adopt them
        // rather than re-adding (TagTable.Add throws on a duplicate id).
        if (HasTagsFor(instanceId, tags)) return (instanceId, false);

        int index = TypeCounters[partType];
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

            case "LevelTank":
                // The library's first analog part: valve openings and a level
                // transmitter, all in percent, all Float.
                tags.Add(new Tag($"{instanceId}.fill", $"Tank {index} Fill Valve (%)", TagType.Float, TagKind.Output));
                tags.Add(new Tag($"{instanceId}.drain", $"Tank {index} Drain Valve (%)", TagType.Float, TagKind.Output));
                tags.Add(new Tag($"{instanceId}.level", $"Tank {index} Level (%)", TagType.Float, TagKind.Input));
                break;

            case "WeighingConveyor":
                tags.Add(new Tag($"{instanceId}.rotate", $"WeighConveyor {index} Rotate", TagType.Bit, TagKind.Output));
                tags.Add(new Tag($"{instanceId}.weight", $"WeighConveyor {index} Weight", TagType.Int, TagKind.Input));
                break;
        }

        return (instanceId, true);
    }
}
