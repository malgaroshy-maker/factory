using FactoryForge.Editor;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Assert that entering Run mode says what is clickable, or says plainly that
/// nothing is (UX-39) -- the same FF-06/FF-23/UX-30 dishonesty class, this
/// time for a Run mode that used to be silent about a line with no Control
/// Panel. Run it with:
///
/// <code>godot --headless --path engine -- --self-test=modehint</code>
///
/// Covers the two pieces separately: <see cref="SceneEditor.DescribeOperableParts"/>
/// counts real placed parts correctly (against the live default scene), and
/// <see cref="IdleHintUI.ShowModeEnteredHint"/> renders both of its messages
/// into the actual label a player would read -- the same standalone-IdleHintUI
/// technique <see cref="DemoRefusalSelfTest"/> uses, so this needs no running
/// editor of its own to check the UI half.
/// </summary>
public partial class RunModeHintSelfTest : Node
{
    public SceneEditor Editor { get; set; } = null!;

    private int _step;

    public override void _Process(double delta)
    {
        _step++;
        if (_step != 2) return;

        bool ok = true;
        ok &= CheckDescribeOperableParts();
        ok &= CheckHintText();

        if (ok)
        {
            GD.Print("self-test modehint: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr("self-test modehint: FAIL");
            GetTree().Quit(1);
        }
    }

    /// <summary>The default headless scene has five operable parts (conveyor,
    /// pusher, stack light, panel, emitter) and three that are not (two
    /// sensors, a chute) -- a real count, not a hand-picked fixture, so a
    /// future part type gaining or losing operability shows up here.</summary>
    private bool CheckDescribeOperableParts()
    {
        var (count, kinds) = Editor.DescribeOperableParts();
        bool ok = Expect(count == 5, $"default scene: 5 operable parts (got {count}: {kinds})");
        foreach (string expected in new[] { "conveyor", "pusher", "stack light", "panel", "emitter" })
            ok &= Expect(kinds.Contains(expected), $"default scene: kinds mentions '{expected}' (got '{kinds}')");

        Editor.ClearAllPlacedParts();
        var (emptyCount, emptyKinds) = Editor.DescribeOperableParts();
        ok &= Expect(emptyCount == 0 && emptyKinds.Length == 0,
                     $"an emptied scene: nothing operable (got {emptyCount}: '{emptyKinds}')");

        // Leave the scene as this test found it, for whatever runs after it
        // in the same process.
        Editor.RegisterDefaultSceneParts(physical: true);
        return ok;
    }

    private bool CheckHintText()
    {
        var tags = new TagTable();
        var bus = new TagBusServer { Tags = tags, SceneName = "untitled" };
        var idleHint = new IdleHintUI { Bus = bus, Tags = tags };
        AddChild(idleHint);   // needs _Ready to build its panel

        var panel = idleHint.GetChild<PanelContainer>(0);
        var label = FindLabel(panel);
        bool ok = Expect(label is not null, "IdleHintUI: has a label to read");
        if (label is null) { bus.Free(); return ok; }

        idleHint.ShowModeEnteredHint(0, "");
        ok &= Expect(panel.Visible, "empty scene: the hint is forced visible");
        ok &= Expect(label.Text.Contains("Nothing in this scene responds"),
                     $"empty scene: says plainly that nothing is clickable (got '{label.Text}')");

        idleHint.ShowModeEnteredHint(3, "conveyor, pusher, panel");
        ok &= Expect(label.Text.Contains("3 parts respond") && label.Text.Contains("conveyor, pusher, panel"),
                     $"populated scene: names the count and the kinds (got '{label.Text}')");

        idleHint.ShowModeEnteredHint(1, "conveyor");
        ok &= Expect(label.Text.Contains("1 part respond"),
                     $"a single operable part: singular, not '1 parts' (got '{label.Text}')");

        bus.Free();
        return ok;
    }

    private static bool Expect(bool condition, string what)
    {
        if (!condition) GD.PrintErr($"  FAIL  {what}");
        return condition;
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
