using System.Collections.Generic;
using FactoryForge.Editor;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Assert that the Tag Inspector can force int and float tags, not just bits.
///
/// <code>godot --headless --path engine -- --self-test=force</code>
///
/// <see cref="TagTable.Force"/> has always taken any <c>object</c>, and parts
/// have always read through <c>TryGetVisible</c> -- the plumbing was never the
/// gap. <see cref="TagInspectorUI"/>'s Force button was: it only handled
/// <see cref="TagType.Bit"/>, so pressing Force on an int or float tag did
/// nothing, silently, and a scene like the tank template -- all float I/O --
/// could not be operated by hand at all (UX-35).
///
/// This drives the inspector's real controls -- the same LineEdit and Button
/// a click would use -- rather than calling private logic directly, so a
/// regression in the wiring fails here too, not just a regression in parsing.
/// </summary>
public partial class TagForceSelfTest : Node
{
    private readonly List<string> _failures = new();
    private TagTable _tags = null!;
    private int _step;

    private void Expect(bool condition, string what)
    {
        if (condition) return;
        _failures.Add(what);
        GD.PrintErr($"  FAIL  {what}");
    }

    public override void _Ready()
    {
        _tags = new TagTable();
        _tags.Add(new Tag("test.bit", "bit", TagType.Bit, TagKind.Output));
        _tags.Add(new Tag("test.count", "count", TagType.Int, TagKind.Output));
        _tags.Add(new Tag("test.level", "level", TagType.Float, TagKind.Output, 1.0));

        var inspector = new TagInspectorUI { Name = "TagInspectorUI" };
        AddChild(inspector);
        inspector.Setup(_tags);
    }

    public override void _Process(double delta)
    {
        _step++;
        // One frame for _Ready to build the row tree before probing it.
        if (_step < 2) return;

        CheckBit();
        CheckInt();
        CheckFloat();
        CheckInvalid();

        if (_failures.Count == 0)
        {
            GD.Print("self-test force: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr($"self-test force: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private void CheckBit()
    {
        var (input, btn) = FindRow("test.bit");
        Expect(input is null, "test.bit: a bit tag gets no value field (one click is the only choice)");
        if (btn is null) { Expect(false, "test.bit: inspector built no row"); return; }

        bool before = (bool)_tags.Visible("test.bit");
        Click(btn);
        Expect(_tags.IsForced("test.bit"), "test.bit: Force did not force");
        Expect(!Equals(_tags.Visible("test.bit"), before), "test.bit: Force did not flip the value");

        Click(btn);
        Expect(!_tags.IsForced("test.bit"), "test.bit: UNFORCE did not clear");
    }

    private void CheckInt()
    {
        var (input, btn) = FindRow("test.count");
        if (input is null || btn is null) { Expect(false, "test.count: inspector built no row"); return; }

        input.Text = "42";
        Click(btn);
        Expect(_tags.IsForced("test.count"), "test.count: Force did not force");
        Expect(Equals(_tags.Visible("test.count"), 42), $"test.count: expected 42, got {_tags.Visible("test.count")}");

        Click(btn);
        Expect(!_tags.IsForced("test.count"), "test.count: UNFORCE did not clear");
    }

    private void CheckFloat()
    {
        var (input, btn) = FindRow("test.level");
        if (input is null || btn is null) { Expect(false, "test.level: inspector built no row"); return; }

        input.Text = "3.5";
        Click(btn);
        Expect(_tags.IsForced("test.level"), "test.level: Force did not force");
        double got = (double)_tags.Visible("test.level")!;
        Expect(System.Math.Abs(got - 3.5) < 1e-6, $"test.level: expected 3.5, got {got}");

        Click(btn);
        Expect(!_tags.IsForced("test.level"), "test.level: UNFORCE did not clear");
    }

    private void CheckInvalid()
    {
        var (input, btn) = FindRow("test.count");
        if (input is null || btn is null) { Expect(false, "test.count: inspector built no row"); return; }

        input.Text = "not a number";
        Click(btn);
        Expect(!_tags.IsForced("test.count"), "test.count: a bad value must not force anything (UX-36)");
    }

    private static void Click(Button btn) => btn.EmitSignal(BaseButton.SignalName.Pressed);

    /// <summary>Find a tag's row the way a user's eye would -- by the tooltip
    /// the inspector puts on that row's name button -- rather than through a
    /// test-only accessor into the panel's private state.</summary>
    private (LineEdit? input, Button? force) FindRow(string tagId)
    {
        Button? nameBtn = null;
        foreach (var b in FindDescendants<Button>(this))
        {
            if (b.TooltipText.StartsWith(tagId + "\n"))
            {
                nameBtn = b;
                break;
            }
        }
        if (nameBtn?.GetParent() is not HBoxContainer row) return (null, null);

        LineEdit? input = null;
        Button? force = null;
        foreach (var child in row.GetChildren())
        {
            if (child is LineEdit le) input = le;
            if (child is Button b && b != nameBtn) force = b;
        }
        return (input, force);
    }

    private static IEnumerable<T> FindDescendants<T>(Node node) where T : Node
    {
        foreach (var child in node.GetChildren())
        {
            if (child is T match) yield return match;
            foreach (var d in FindDescendants<T>(child)) yield return d;
        }
    }
}
