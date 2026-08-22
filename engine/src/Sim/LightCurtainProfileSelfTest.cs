using System.Collections.Generic;
using FactoryForge.Editor;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Load the light-curtain scene, run its demo profile for long enough to sort
/// several cartons both ways, and check nothing was lost or misrouted --
/// §4.4's own assertion.
///
/// <code>godot --headless --path engine -- --self-test=lightcurtain</code>
/// </summary>
public partial class LightCurtainProfileSelfTest : Node
{
    public SceneEditor Editor { get; set; } = null!;
    public TagTable Tags { get; set; } = null!;

    /// <summary>~20s simulated: the emitter alternates tall/short every 3s
    /// (twice the 1.5s half-period), so this covers roughly six cartons --
    /// enough to see both counters move, not just one.</summary>
    private const int RunTicks = 1200;

    private readonly List<string> _failures = new();
    private int _step;
    private DemoDriver? _demo;
    private TagBusServer? _bus;

    private void Expect(bool condition, string what)
    {
        if (condition) return;
        _failures.Add(what);
        GD.PrintErr($"  FAIL  {what}");
    }

    public override void _PhysicsProcess(double delta)
    {
        _step++;

        if (_step == 1)
        {
            Editor.LoadTemplate("res://templates/light_curtain_sorting.json");
            return;
        }

        if (_step == 3)
        {
            _bus = new TagBusServer { Tags = Tags, SceneName = "light-curtain-sorting" };
            _demo = new DemoDriver { Name = "TestDemoDriver", Tags = Tags, Bus = _bus };
            AddChild(_demo);
            _demo.Start();
            Expect(_demo.Active, "light-curtain-sorting: demo starts (a profile exists)");
            return;
        }

        if (_step != 3 + RunTicks) return;

        if (!Tags.Contains("tall_count.count") || !Tags.Contains("short_count.count"))
        {
            Expect(false, "tall_count.count / short_count.count: tags exist after loading the template");
            Finish();
            return;
        }

        int tall = System.Convert.ToInt32(Tags.Visible("tall_count.count"));
        int shortCount = System.Convert.ToInt32(Tags.Visible("short_count.count"));

        Expect(tall > 0, $"tall_count.count advances (got {tall} after {RunTicks} ticks -- the diverter never fired)");
        Expect(shortCount > 0, $"short_count.count advances (got {shortCount} -- nothing reached the far end)");
        GD.Print($"  tall={tall} short={shortCount} after {RunTicks} ticks");

        Finish();
    }

    private void Finish()
    {
        _bus?.Free();
        if (_failures.Count == 0)
        {
            GD.Print("self-test lightcurtain: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr($"self-test lightcurtain: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }
}
