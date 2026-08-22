using FactoryForge.Editor;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Assert that a refused Demo actually says so in the UI, not just on the
/// console (UX-30) -- the FF-06/FF-23 dishonesty class, this time for the
/// Demo button on a scene with no built-in exercise.
///
/// <code>godot --headless --path engine -- --self-test=refusal</code>
/// </summary>
public partial class DemoRefusalSelfTest : Node
{
    private int _step;

    public override void _Process(double delta)
    {
        _step++;
        if (_step != 2) return;

        var tags = new TagTable();
        var bus = new TagBusServer { Tags = tags, SceneName = "untitled" };
        var demo = new DemoDriver { Tags = tags, Bus = bus };
        var idleHint = new IdleHintUI { Bus = bus, Tags = tags, Demo = demo };
        AddChild(idleHint);   // needs _Ready to build its panel and subscribe

        CallDeferred(nameof(Probe), demo, idleHint, bus);
    }

    private void Probe(DemoDriver demo, IdleHintUI idleHint, TagBusServer bus)
    {
        bool ok = true;

        demo.Start();
        ok &= Expect(!demo.Active, "an unnamed scene: Demo does not turn on");
        ok &= Expect(demo.RefusalReason is not null, "an unnamed scene: RefusalReason is set");

        var panel = idleHint.GetChild<PanelContainer>(0);
        ok &= Expect(panel.Visible, "IdleHintUI: the panel is forced visible on refusal");

        var label = FindLabel(panel);
        ok &= Expect(label is not null && label.Text.Contains(demo.RefusalReason!),
                     "IdleHintUI: the label shows the actual refusal reason, not a generic one");

        demo.Free();
        bus.Free();

        if (ok)
        {
            GD.Print("self-test refusal: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr("self-test refusal: FAIL");
            GetTree().Quit(1);
        }
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
