using System;
using System.Collections.Generic;
using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// What a scene is asking you to build (BR-01).
///
/// The blurb says what a template *is*; this says what to do with it. Every
/// scene here teaches something specific and, until this existed, the app never
/// said what — you opened the heat-treat station, saw a hot plate, and had to
/// guess. The lesson lived in `tools/try_scene.py`, which is the last place a
/// person learning PLC programming will look.
/// </summary>
/// <param name="Uses">The tags your program drives and reads, ` · `-separated.
/// Checked against the scene's own tag set by <c>--self-test=templates</c>: a
/// brief naming a tag the scene does not have is a broken lesson, and it is the
/// way this goes wrong when a template is edited later.</param>
public sealed record TemplateBrief(string Task, string Uses, string Done);

/// <summary>One row of <c>engine/templates/manifest.json</c>.</summary>
public sealed record TemplateEntry(string Id, string Title, string Blurb, string Path,
                                   string Scene, TemplateBrief? Brief);

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

    /// <summary>The brief for a scene, by the name the engine knows it as —
    /// <c>SceneEditor.SceneName</c>. Null for a scene somebody built
    /// themselves, which has no lesson attached to it.</summary>
    public static TemplateBrief? BriefForScene(string sceneName)
    {
        foreach (var entry in Load())
        {
            if (entry.Scene == sceneName) return entry.Brief;
        }
        return null;
    }

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
            TemplateBrief? brief = null;
            if (node!["brief"] is { } b)
            {
                brief = new TemplateBrief(
                    b["task"]!.GetValue<string>(),
                    b["uses"]!.GetValue<string>(),
                    b["done"]!.GetValue<string>());
            }

            list.Add(new TemplateEntry(
                node["id"]!.GetValue<string>(),
                node["title"]!.GetValue<string>(),
                node["blurb"]!.GetValue<string>(),
                node["path"]!.GetValue<string>(),
                node["scene"]!.GetValue<string>(),
                brief));
        }
        return list;
    }
}
