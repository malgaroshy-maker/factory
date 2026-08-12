using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace FactoryForge.Editor;

public class PartInstanceData
{
    [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
    [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
    [JsonPropertyName("position")] public float[] Position { get; set; } = new float[3];
    [JsonPropertyName("rotation")] public float[] Rotation { get; set; } = new float[3];

    /// <summary>
    /// Everything the part was tuned to. Without it a scene file described only
    /// where the machines stood, not how they were set up, so reloading reset
    /// every value to its default. Unknown keys are ignored on load, so a scene
    /// from a newer build still opens.
    /// </summary>
    [JsonPropertyName("properties")] public Dictionary<string, string>? Properties { get; set; }
}

public class SceneData
{
    [JsonPropertyName("name")] public string Name { get; set; } = "custom-scene";
    [JsonPropertyName("version")] public string Version { get; set; } = "1.0";
    [JsonPropertyName("parts")] public List<PartInstanceData> Parts { get; set; } = new();

    public string ToJson()
    {
        var options = new JsonSerializerOptions { WriteIndented = true };
        return JsonSerializer.Serialize(this, options);
    }

    public static SceneData? FromJson(string json)
    {
        return JsonSerializer.Deserialize<SceneData>(json);
    }
}
