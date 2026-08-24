using System.Collections.Generic;
using FactoryForge.Editor;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Load the tank scene, run the demo profile's controller, and check it
/// actually holds the setpoint -- §4.3's own assertion (±5% for 10s), reused
/// here rather than invented, since it is the one number both this and the
/// future `tools/try_scene.py` (UX-22) need to agree on.
///
/// <code>godot --headless --path engine -- --self-test=tank</code>
/// </summary>
public partial class TankProfileSelfTest : Node
{
    public SceneEditor Editor { get; set; } = null!;
    public TagTable Tags { get; set; } = null!;

    /// <summary>What the template's own setpoint pot ships set to (OP-05).
    /// The controller no longer carries a setpoint of its own — it reads the
    /// panel — so this is the number on the plate, written out here rather
    /// than read off the panel under test, which would pass whatever the panel
    /// said.</summary>
    private const float Setpoint = 70.0f;
    private const float BandPercent = 5.0f;

    /// <summary>~13s of simulated time at the default 60Hz physics rate --
    /// comfortably past the ~7s the controller's own exponential decay needs
    /// to settle within the band (see TankLevelControlProfile's Gain comment).</summary>
    private const int SettleTicks = 800;

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
            Editor.LoadTemplate("res://templates/tank_level_control.json");
            return;
        }

        if (_step == 3)
        {
            _bus = new TagBusServer { Tags = Tags, SceneName = "tank-level-control" };
            _demo = new DemoDriver { Name = "TestDemoDriver", Tags = Tags, Bus = _bus };
            AddChild(_demo);
            _demo.Start();
            Expect(_demo.Active, "tank-level-control: demo starts (a profile exists)");
            return;
        }

        if (_step != 3 + SettleTicks) return;

        if (!Tags.Contains("tank.level"))
        {
            Expect(false, "tank.level: tag exists after loading the template");
            Finish();
            return;
        }

        double level = System.Convert.ToDouble(Tags.Visible("tank.level"));
        float band = Setpoint * BandPercent / 100.0f;
        Expect(level >= Setpoint - band && level <= Setpoint + band,
               $"tank.level settled within ±{band:0.0} of {Setpoint} (got {level:0.0} after {SettleTicks} ticks)");

        if (Tags.Contains("level_readout.value"))
        {
            int readout = System.Convert.ToInt32(Tags.Visible("level_readout.value"));
            Expect(System.Math.Abs(readout - level) <= 1.0,
                   $"level_readout.value ({readout}) tracks tank.level ({level:0.0})");
        }

        Finish();
    }

    private void Finish()
    {
        _bus?.Free();
        if (_failures.Count == 0)
        {
            GD.Print("self-test tank: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr($"self-test tank: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }
}
