using System.Collections.Generic;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Assert that DemoDriver picks the right profile for each shipped scene and
/// refuses honestly for one it does not know (UX-16).
///
/// <code>godot --headless --path engine -- --self-test=demo</code>
///
/// This checks the *dispatch*, not each profile's physics -- a profile reacts
/// to real Jolt physics over several seconds of wall-clock time, and asserting
/// that is `tools/try_scene.py`'s job (UX-21/22), deliberately not this one's
/// (see docs/UX_PLAN.md Phase 2: "Assertions: none — it is a demo" for the C#
/// profiles). What belongs here is cheap and easy to get wrong by hand: five
/// hand-typed scene-id strings that have to exactly match
/// engine/templates/manifest.json, and the refuse-instead-of-lie behaviour a
/// missing profile needs.
/// </summary>
public partial class DemoDriverSelfTest : Node
{
    public TagTable Tags { get; set; } = null!;

    private readonly List<string> _failures = new();
    private int _step;

    private void Expect(bool condition, string what)
    {
        if (condition) return;
        _failures.Add(what);
        GD.PrintErr($"  FAIL  {what}");
    }

    public override void _Process(double delta)
    {
        _step++;
        if (_step < 2) return;

        // None of these join the scene tree -- they only need field access, not
        // a running port -- so each is freed explicitly rather than relying on
        // QueueFree(), which only fires for nodes the tree actually owns.

        // Every manifest scene id must have a profile, or "Watch it run" on a
        // freshly loaded template silently does nothing (§2.1's whole point).
        foreach (string id in new[]
        {
            "sorting-by-height", "start-stop-station", "tank-level-control",
            "light-curtain-sorting", "roller-line-weighing",
        })
        {
            var bus = new TagBusServer { Tags = Tags, SceneName = id };
            var demo = new DemoDriver { Tags = Tags, Bus = bus };
            demo.Start();
            Expect(demo.Active, $"{id}: a known scene starts");
            Expect(demo.RefusalReason is null, $"{id}: a known scene gives no refusal reason");
            demo.Stop();
            Expect(!demo.Active, $"{id}: Stop() clears Active");
            demo.Free();
            bus.Free();
        }

        // An unknown scene (a custom empty scene, or a template with a typo'd
        // name) must refuse audibly rather than turn the button green over
        // nothing -- the exact dishonesty FF-06 and FF-23 were about.
        var strayBus = new TagBusServer { Tags = Tags, SceneName = "not-a-real-scene" };
        var stray = new DemoDriver { Tags = Tags, Bus = strayBus };
        stray.Start();
        Expect(!stray.Active, "unknown scene: Start() does not lie about running");
        Expect(stray.RefusalReason is not null, "unknown scene: a reason is given");
        stray.Free();
        strayBus.Free();

        if (_failures.Count == 0)
        {
            GD.Print("self-test demo: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr($"self-test demo: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }
}
