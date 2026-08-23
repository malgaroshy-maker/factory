using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using FactoryForge.Editor;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Load every manifest scene in turn and check its tag set -- id, type, kind
/// -- matches <c>tests/fixtures/scene_tag_sets.json</c> exactly (UX-42).
///
/// <code>godot --headless --path engine -- --self-test=scenes</code>
///
/// Nothing else in the suite would catch a template edit that quietly
/// renames a tag, or a part swapped for one with a different tag set: the
/// per-scene profile self-tests (UX-17...UX-20) exercise *behaviour*, which
/// keeps working right up until a mapping file elsewhere in a real project
/// points at a tag id that no longer exists. This checks the contract
/// directly, the same way <c>--self-test=parity</c> checks the tag model
/// against a shared fixture rather than trusting the two implementations to
/// agree by construction.
/// </summary>
public partial class SceneTagSetSelfTest : Node
{
    public SceneEditor Editor { get; set; } = null!;
    public TagTable Tags { get; set; } = null!;

    private readonly List<string> _failures = new();
    private JsonObject _expected = null!;
    private IReadOnlyList<TemplateEntry> _manifest = null!;
    private int _step;

    // Load at step 1, 4, 7, ...; check (and load the next) two ticks later --
    // the same settle gap every other template-loading self-test this plan
    // added uses, since a load's effects are not all visible the same tick.
    private const int StepStride = 3;

    private void Expect(bool condition, string what)
    {
        if (condition) return;
        _failures.Add(what);
        GD.PrintErr($"  FAIL  {what}");
    }

    public override void _Ready()
    {
        string fixturePath = Path.Combine(
            ProjectSettings.GlobalizePath("res://"), "..", "tests", "fixtures", "scene_tag_sets.json");
        _expected = JsonNode.Parse(File.ReadAllText(fixturePath))!.AsObject()["scenes"]!.AsObject();
        _manifest = TemplateManifest.Load();
    }

    public override void _PhysicsProcess(double delta)
    {
        _step++;

        for (int i = 0; i < _manifest.Count; i++)
        {
            if (_step == 1 + i * StepStride) { LoadScene(_manifest[i]); return; }
            if (_step == 1 + i * StepStride + 2)
            {
                CheckScene(_manifest[i]);
                if (i == _manifest.Count - 1) Finish();
                return;
            }
        }
    }

    private void LoadScene(TemplateEntry entry)
    {
        if (entry.Path.Length == 0) Editor.LoadDefaultSortingScene();
        else Editor.LoadTemplate(entry.Path);
    }

    private void CheckScene(TemplateEntry entry)
    {
        if (_expected[entry.Id] is not JsonArray expectedTags)
        {
            Expect(false, $"{entry.Id}: no expectation in scene_tag_sets.json");
            return;
        }

        var expected = new SortedDictionary<string, (string Type, string Kind)>();
        foreach (var node in expectedTags)
        {
            expected[node!["id"]!.GetValue<string>()] =
                (node["type"]!.GetValue<string>(), node["kind"]!.GetValue<string>());
        }

        var actual = new SortedDictionary<string, (string Type, string Kind)>();
        foreach (var tag in Tags)
        {
            actual[tag.Id] = (Tag.TypeName(tag.Type), Tag.KindName(tag.Kind));
        }

        foreach (var (id, want) in expected)
        {
            if (!actual.TryGetValue(id, out var got))
            {
                Expect(false, $"{entry.Id}: missing tag '{id}' ({want.Type}/{want.Kind})");
                continue;
            }
            Expect(got == want,
                   $"{entry.Id}: '{id}' expected {want.Type}/{want.Kind}, got {got.Type}/{got.Kind}");
        }

        foreach (var id in actual.Keys)
        {
            Expect(expected.ContainsKey(id), $"{entry.Id}: unexpected tag '{id}' not in the fixture");
        }
    }

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("self-test scenes: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr($"self-test scenes: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }
}
