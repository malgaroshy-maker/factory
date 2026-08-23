using FactoryForge.Editor;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Assert the toolbar's "Try this scene" button (UX-31) finds the right
/// manifest entry, and refuses honestly through the actual on-screen hint
/// when the loaded scene has no built-in exercise -- the same
/// FF-06/FF-23/UX-30 dishonesty class closed everywhere else in this plan.
///
/// <code>godot --headless --path engine -- --self-test=tryscene</code>
///
/// Deliberately does not exercise the matching path through
/// <see cref="SceneToolbarUI.TryThisScene"/> itself: that path spawns a real
/// terminal process (<see cref="TerminalLauncher.Spawn"/>), which is not
/// something a headless CI run should ever trigger. The matching logic
/// (<see cref="SceneToolbarUI.FindManifestEntry"/>) is checked directly
/// instead, since it is a pure lookup with no side effect; only the refusal
/// path -- which returns before any process would be spawned -- is driven
/// through the real button method.
/// </summary>
public partial class TryThisSceneSelfTest : Node
{
    public SceneEditor Editor { get; set; } = null!;

    private int _step;

    private void Expect(bool condition, string what, ref bool ok)
    {
        if (!condition) { ok = false; GD.PrintErr($"  FAIL  {what}"); }
    }

    public override void _Process(double delta)
    {
        _step++;
        if (_step != 2) return;

        bool ok = true;
        CheckFindManifestEntry(ref ok);
        CheckRefusalReachesUi(ref ok);

        if (ok)
        {
            GD.Print("self-test tryscene: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr("self-test tryscene: FAIL");
            GetTree().Quit(1);
        }
    }

    private void CheckFindManifestEntry(ref bool ok)
    {
        var sorting = SceneToolbarUI.FindManifestEntry("sorting-by-height");
        Expect(sorting is { Id: "sorting-by-height" }, "sorting-by-height resolves to its manifest entry", ref ok);

        var tank = SceneToolbarUI.FindManifestEntry("tank-level-control");
        Expect(tank is { Id: "tank-level-control" }, "tank-level-control resolves to its manifest entry", ref ok);

        var unknown = SceneToolbarUI.FindManifestEntry("a-custom-scene-nobody-shipped");
        Expect(unknown is null, "a custom scene name resolves to no manifest entry", ref ok);
    }

    private void CheckRefusalReachesUi(ref bool ok)
    {
        // A real custom scene, saved and reloaded the way a user's own would
        // be -- not a name typed straight into SceneName, which no public
        // path can do.
        const string path = "user://tryscene_selftest.json";
        var data = new SceneData { Name = "my_custom_line" };
        using (var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write))
        {
            file!.StoreString(data.ToJson());
        }
        Editor.LoadSceneFromFile(path);
        Expect(Editor.SceneName == "my_custom_line", "the custom scene's name actually loaded", ref ok);

        var tags = new TagTable();
        var bus = new TagBusServer { Tags = tags, SceneName = Editor.SceneName };
        var idleHint = new IdleHintUI { Bus = bus, Tags = tags };
        AddChild(idleHint);

        var toolbar = new SceneToolbarUI { Editor = Editor, IdleHint = idleHint };
        AddChild(toolbar);

        toolbar.TryThisScene();   // no manifest match -> refuses, spawns nothing

        var panel = idleHint.GetChild<PanelContainer>(0);
        var label = FindLabel(panel);
        Expect(panel.Visible, "the hint is forced visible on refusal", ref ok);
        Expect(label is not null && label.Text.Contains("No built-in exercise")
               && label.Text.Contains("my_custom_line"),
               $"the label names the actual scene, not a generic message (got '{label?.Text}')", ref ok);

        bus.Free();
        Godot.DirAccess.RemoveAbsolute(ProjectSettings.GlobalizePath(path));
    }

    private static Label? FindLabel(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is Label l) return l;
            var found = FindLabel(child);
            if (found is not null) return found;
        }
        return null;
    }
}
