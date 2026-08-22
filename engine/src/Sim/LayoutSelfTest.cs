using System.Collections.Generic;
using FactoryForge.Editor;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Assert that a modal still fits on screen once it has something long to say.
///
/// <code>godot --headless --path engine -- --self-test=layout</code>
///
/// The F5 driver dialog's status Label sat in the footer HBox with no
/// AutowrapMode. A Label without autowrap reports its entire single-line text
/// as its minimum width, and a minimum size propagates up through every
/// Container above it. So the honest multi-sentence auto-detect message from
/// FF-06 -- around 200 characters -- pushed the modal's minimum width past the
/// 1600px viewport. CenterContainer then had no slack left to centre with, the
/// panel's edges fell outside the window, and the Close, Stop Sidecar and
/// Apply &amp; Connect buttons were all pushed off the right-hand edge. The
/// dialog became unusable at exactly the moment it had something to report,
/// and every other self-test passed the whole time, because nothing here is
/// about tags.
///
/// This drives the real probe rather than a copy of its strings, so a longer
/// message added later is covered without anyone remembering to update a
/// fixture.
/// </summary>
public partial class LayoutSelfTest : Node
{
    private readonly List<string> _failures = new();
    private DriverConnectionUI _driver = null!;
    private int _step;

    private void Expect(bool condition, string what)
    {
        if (condition) return;
        _failures.Add(what);
        GD.PrintErr($"  FAIL  {what}");
    }

    public override void _Ready()
    {
        _driver = new DriverConnectionUI { Name = "DriverConnectionUI" };
        AddChild(_driver);
    }

    public override void _Process(double delta)
    {
        _step++;

        // One frame for _Ready to build the tree, then kick off the probe. An
        // empty IP short-circuits every network branch, so this returns on the
        // first await without touching the network -- and lands on the longest
        // message the dialog has.
        if (_step == 2)
        {
            _driver.Visible = true;
            _ = _driver.RunAutoDetectAsync();
            return;
        }

        // The probe applies its result through CallDeferred, and containers
        // re-sort on the frame after a child's minimum size changes.
        if (_step < 8) return;

        Check("F5 driver dialog", _driver);

        if (_failures.Count == 0)
        {
            GD.Print("self-test layout: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr($"self-test layout: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private void Check(string what, Control modal)
    {
        var panel = FindPanel(modal);
        if (panel is null)
        {
            Expect(false, $"{what}: no PanelContainer to measure");
            return;
        }

        // The design resolution, not GetViewport() -- a headless run has no real
        // window and reports a square one, which would quietly slacken the
        // width budget and tighten the height one.
        float screenX = (int)ProjectSettings.GetSetting("display/window/size/viewport_width");
        float screenY = (int)ProjectSettings.GetSetting("display/window/size/viewport_height");

        // Measure the minimum, not the current size: a Control clamped by its
        // parent reports a size that fits while its content overflows. The
        // minimum is what CenterContainer actually has to satisfy.
        Vector2 needed = panel.GetCombinedMinimumSize();

        // Budget a fraction of the screen rather than all of it. "Fits the
        // viewport" is the wrong bar: the broken dialog measured 1594px against
        // a 1600px screen and would have passed, while on screen it ran edge to
        // edge with its buttons clipped against the frame. This is a centred
        // modal — if it needs nearly the whole screen, its layout is wrong,
        // whether or not the last pixel technically lands inside.
        const float WidthBudget = 0.85f;
        const float HeightBudget = 0.90f;

        Expect(needed.X <= screenX * WidthBudget,
            $"{what}: needs {needed.X:0}px of width, budget is {screenX * WidthBudget:0}px " +
            $"of a {screenX:0}px screen — an unwrapped label is stretching the modal");
        Expect(needed.Y <= screenY * HeightBudget,
            $"{what}: needs {needed.Y:0}px of height, budget is {screenY * HeightBudget:0}px " +
            $"of a {screenY:0}px screen");

        if (_failures.Count == 0)
        {
            GD.Print($"  ok    {what}: {needed.X:0}x{needed.Y:0} within budget " +
                     $"{screenX * WidthBudget:0}x{screenY * HeightBudget:0} of {screenX:0}x{screenY:0}");
        }
    }

    private static PanelContainer? FindPanel(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is PanelContainer panel) return panel;
            var found = FindPanel(child);
            if (found is not null) return found;
        }
        return null;
    }
}
