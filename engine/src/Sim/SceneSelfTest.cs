using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FactoryForge.Editor;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Headless check that a scene survives being written to disk and read back,
/// for every part type in the palette.
///
/// <code>godot --headless --path engine -- --self-test=scene</code>
///
/// Save/load is the feature with the widest blast radius and the quietest
/// failures: a part whose settings are not captured comes back at its defaults,
/// which looks like a scene that was never tuned rather than a bug. It has gone
/// wrong twice before — removers lost the tag they count into, sensors lost the
/// flag saying the simulation owns their tag.
///
/// Every part type is exercised, not a representative sample, because the cost
/// of forgetting one is exactly the silent kind.
/// </summary>
public partial class SceneSelfTest : Node
{
    public TagTable Tags { get; set; } = null!;
    public SceneEditor? Editor { get; set; }

    /// <summary>Every type the palette offers. A part missing here is a part
    /// nobody checks can be saved.</summary>
    private static readonly string[] AllTypes =
    {
        "ConveyorBelt", "PhotoelectricSensor", "RetroreflectiveSensor", "InductiveSensor",
        "LightArray", "PusherMechanism", "Chute", "StackLight", "DigitalDisplay",
        "WeighingConveyor", "RollerConveyor", "Emitter", "Remover", "ButtonPanel", "LevelTank",
    };

    private const string ScenePath = "user://selftest_scene.json";

    private readonly List<string> _failures = new();
    private int _step;
    private bool _done;

    private void Expect(bool condition, string what)
    {
        if (condition) return;
        _failures.Add(what);
        GD.PrintErr($"  FAIL  {what}");
    }

    public override void _PhysicsProcess(double delta)
    {
        // Phase 1 builds and saves; phase 2 loads and inspects. They cannot be
        // the same tick: parts build their geometry in _Ready, which does not
        // run until the node has been in the tree for a frame.
        if (++_step == 3) { BuildAndSave(); return; }
        if (_step != 8 || _done) return;
        _done = true;

        try
        {
            CheckRoundTrip();
            CheckDispatchSurvives();
        }
        catch (System.Exception ex)
        {
            _failures.Add(ex.Message);
            GD.PrintErr($"  FAIL  threw: {ex.GetType().Name}: {ex.Message}");
        }

        if (_failures.Count == 0)
        {
            GD.Print("self-test scene: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr($"self-test scene: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    /// <summary>
    /// Write a scene containing one of everything, with non-default settings, by
    /// hand. Hand-written rather than saved from a built scene so the test also
    /// covers the case that actually happens to users: a file produced by
    /// something other than this exact build.
    /// </summary>
    private void BuildAndSave()
    {
        var data = new SceneData { Name = "selftest-every-part" };

        for (int i = 0; i < AllTypes.Length; i++)
        {
            var props = new Dictionary<string, string>();

            // Values deliberately unlike the defaults, so "it came back right"
            // cannot be satisfied by a part that ignored the file entirely.
            switch (AllTypes[i])
            {
                case "ConveyorBelt":
                case "RollerConveyor":
                case "WeighingConveyor":
                    props["speed"] = "0.77";
                    props["friction"] = "0.66";
                    break;
                case "PhotoelectricSensor":
                case "RetroreflectiveSensor":
                case "InductiveSensor":
                    props["range"] = "0.93";
                    props["height"] = "0.17";
                    props["mode"] = "Inductive";
                    props["visual_only"] = "true";
                    break;
                case "PusherMechanism":
                    props["stroke"] = "0.61";
                    props["speed"] = "1.9";
                    props["visual_only"] = "true";
                    break;
                case "Chute":
                    props["incline"] = "37";
                    props["friction"] = "0.11";
                    break;
                case "LevelTank":
                    props["fill_rate"] = "22";
                    props["drain_rate"] = "13";
                    break;
                case "LightArray":
                    props["beams"] = "9";
                    props["curtain_height"] = "0.55";
                    break;
                case "Emitter":
                    props["metal_every"] = "4";
                    break;
                case "Remover":
                    props["count_tag"] = "counter.tall";
                    break;
                case "DigitalDisplay":
                    props["unit"] = "kg";
                    break;
            }

            data.Parts.Add(new PartInstanceData
            {
                Id = $"probe_{AllTypes[i].ToLowerInvariant()}",
                Type = AllTypes[i],
                // Spread along X so nothing overlaps; on the work plane, as the
                // mounting convention requires.
                Position = new[] { -6.0f + i * 1.0f, 0.5f, -4.0f },
                Rotation = new[] { 0.0f, i % 2 == 0 ? 0.0f : Mathf.Pi / 2.0f, 0.0f },
                Properties = props,
            });
        }

        // A key no build knows: loading must ignore it, not fail. SceneData's
        // docs promise a scene from a newer build still opens.
        data.Parts[0].Properties!["unknown_future_key"] = "42";

        using (var file = Godot.FileAccess.Open(ScenePath, Godot.FileAccess.ModeFlags.Write))
        {
            file?.StoreString(data.ToJson());
        }

        Editor!.LoadSceneFromFile(ScenePath);
    }

    private void CheckRoundTrip()
    {
        var ids = Editor!.PlacedPartIds();

        Expect(Editor.SceneName == "selftest-every-part",
               $"the scene adopts the name in the file (got '{Editor.SceneName}')");

        foreach (string type in AllTypes)
        {
            string id = $"probe_{type.ToLowerInvariant()}";
            Expect(ids.Contains(id), $"{type} was loaded");

            // Chute is the one part with no I/O of its own, by design.
            if (type == "Chute") continue;
            Expect(PartTagManager.HasTagsFor(id, Tags), $"{type} registered its tags");
        }

        // Save it back out and compare the properties we set. This is the check
        // that catches a setting missing from PartProperties: it loads fine and
        // then quietly saves the default.
        const string resaved = "user://selftest_scene_resaved.json";
        Editor.SaveSceneToFile(resaved);

        using var file = Godot.FileAccess.Open(resaved, Godot.FileAccess.ModeFlags.Read);
        var data = SceneData.FromJson(file?.GetAsText() ?? "");
        Expect(data is not null, "the re-saved scene is readable");
        if (data is null) return;

        Expect(data.Parts.Count == AllTypes.Length,
               $"every part was saved again ({data.Parts.Count} of {AllTypes.Length})");

        foreach (var part in data.Parts)
        {
            var props = part.Properties;
            if (props is null) continue;

            switch (part.Type)
            {
                case "ConveyorBelt":
                case "RollerConveyor":
                case "WeighingConveyor":
                    ExpectNear(props, "speed", 0.77f, part.Type);
                    ExpectNear(props, "friction", 0.66f, part.Type);
                    break;
                case "PhotoelectricSensor":
                case "RetroreflectiveSensor":
                case "InductiveSensor":
                    ExpectNear(props, "range", 0.93f, part.Type);
                    ExpectNear(props, "height", 0.17f, part.Type);
                    Expect(props.GetValueOrDefault("mode") == "Inductive",
                           $"{part.Type} kept its sensing mode");
                    Expect(props.GetValueOrDefault("visual_only") == "true",
                           $"{part.Type} kept visual_only (the flag that stops two authors fighting over one tag)");
                    break;
                case "PusherMechanism":
                    ExpectNear(props, "stroke", 0.61f, part.Type);
                    Expect(props.GetValueOrDefault("visual_only") == "true",
                           $"{part.Type} kept visual_only");
                    break;
                case "Chute":
                    ExpectNear(props, "incline", 37.0f, part.Type);
                    ExpectNear(props, "friction", 0.11f, part.Type);
                    break;
                case "LevelTank":
                    ExpectNear(props, "fill_rate", 22.0f, part.Type);
                    ExpectNear(props, "drain_rate", 13.0f, part.Type);
                    break;
                case "LightArray":
                    ExpectNear(props, "beams", 9.0f, part.Type);
                    ExpectNear(props, "curtain_height", 0.55f, part.Type);
                    break;
                case "Emitter":
                    ExpectNear(props, "metal_every", 4.0f, part.Type);
                    break;
                case "Remover":
                    Expect(props.GetValueOrDefault("count_tag") == "counter.tall",
                           "Remover kept the tag it counts into");
                    break;
                case "DigitalDisplay":
                    Expect(props.GetValueOrDefault("unit") == "kg", "DigitalDisplay kept its unit");
                    break;
            }
        }
    }

    /// <summary>
    /// The loaded parts have to survive being driven. Every output tag is set
    /// high at once — not a realistic scene state, deliberately: it reaches
    /// every branch of the part dispatch in one tick, which is where a null
    /// reference in a rarely-used part would hide.
    /// </summary>
    private void CheckDispatchSurvives()
    {
        foreach (var tag in Tags)
        {
            if (tag.Kind != TagKind.Output) continue;
            Tags.Set(tag.Id, tag.Type switch
            {
                TagType.Bit => true,
                TagType.Int => 1,
                _ => (object)50.0,
            });
        }

        // If the dispatch throws, the catch in _PhysicsProcess records it.
        Editor!._PhysicsProcess(1.0 / 60.0);
        Editor._PhysicsProcess(1.0 / 60.0);
        Expect(true, "part dispatch survives every output being driven");
    }

    private void ExpectNear(IDictionary<string, string> props, string key, float want, string type)
    {
        if (!props.TryGetValue(key, out string? raw)
            || !float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out float got))
        {
            Expect(false, $"{type}.{key} is missing from the saved scene");
            return;
        }
        Expect(Mathf.Abs(got - want) < 0.01f, $"{type}.{key} round-tripped (want {want}, got {got})");
    }
}
