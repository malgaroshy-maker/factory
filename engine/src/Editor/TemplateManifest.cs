using System;
using System.Collections.Generic;
using Godot;

namespace FactoryForge.Editor;

/// <summary>One row of <c>engine/templates/manifest.json</c>.</summary>
public sealed record TemplateEntry(string Id, string Title, string Blurb, string Path, string Scene);

/// <summary>
/// The single place that knows what templates ship. Before this, the start
/// screen's blurb text and the template self-test's path list were two
/// hand-maintained copies of the same template list, and nothing checked
/// they agreed. Add a template once, here, and it shows up on the start
/// screen and in the self-test with no second edit (UX-13).
/// </summary>
public static class TemplateManifest
{
    private const string ManifestPath = "res://templates/manifest.json";

    public static IReadOnlyList<TemplateEntry> Load()
    {
        using var file = FileAccess.Open(ManifestPath, FileAccess.ModeFlags.Read);
        if (file is null)
        {
            GD.PrintErr($"template manifest: could not open {ManifestPath} ({FileAccess.GetOpenError()})");
            return Array.Empty<TemplateEntry>();
        }

        var arr = System.Text.Json.Nodes.JsonNode.Parse(file.GetAsText())!.AsArray();
        var list = new List<TemplateEntry>(arr.Count);
        foreach (var node in arr)
        {
            list.Add(new TemplateEntry(
                node!["id"]!.GetValue<string>(),
                node["title"]!.GetValue<string>(),
                node["blurb"]!.GetValue<string>(),
                node["path"]!.GetValue<string>(),
                node["scene"]!.GetValue<string>()));
        }
        return list;
    }
}
