using System.Collections.Generic;
using FactoryForge.Editor;
using FactoryForge.Parts;
using FactoryForge.Sim.DemoProfiles;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Drive <see cref="StartStopStationProfile"/> through a full Start / Stop /
/// E-stop / Reset cycle and check the latch never disagrees with itself.
///
/// <code>godot --headless --path engine -- --self-test=startstop</code>
///
/// Everything else in Phase 2 is timer-driven and low-risk; this profile is
/// the one with real state (a latch that must survive a stray Start press
/// while tripped) so it is the one worth a dedicated physics-timed test
/// rather than a manual spot check. Presses go through the real
/// <see cref="ButtonPanel.Press"/> API a Run-mode click would use, not a
/// direct tag write, so a regression in that path fails here too.
/// </summary>
public partial class StartStopProfileSelfTest : Node
{
    public SceneEditor Editor { get; set; } = null!;
    public TagTable Tags { get; set; } = null!;

    private readonly List<string> _failures = new();
    private int _step;
    private ButtonPanel? _panel;
    private DemoDriver? _demo;
    private TagBusServer? _bus;

    private void Expect(bool condition, string what)
    {
        if (condition) return;
        _failures.Add(what);
        GD.PrintErr($"  FAIL  {what}");
    }

    private bool Bit(string id) => Tags.Contains(id) && Tags.Visible(id) is true;

    public override void _PhysicsProcess(double delta)
    {
        _step++;

        switch (_step)
        {
            case 1:
                Editor.LoadTemplate("res://templates/start_stop_station.json");
                break;

            case 3:
                foreach (var child in GetParent().GetChildren())
                {
                    if (child is ButtonPanel found) _panel = found;
                }
                if (_panel is null)
                {
                    Expect(false, "start-stop-station: no ButtonPanel after loading the template");
                    Finish();
                    return;
                }
                _bus = new TagBusServer { Tags = Tags, SceneName = "start-stop-station" };
                _demo = new DemoDriver { Name = "TestDemoDriver", Tags = Tags, Bus = _bus };
                AddChild(_demo);
                _demo.Start();
                Expect(_demo.Active, "start-stop-station: demo starts (a profile exists)");
                break;

            // DemoDriver.Tick runs on the frame clock (_Process), not the
            // physics clock (_PhysicsProcess) this test steps on -- headless
            // has no vsync to lock the two together, so a gap of a handful of
            // physics ticks is not reliably enough real time for a frame to
            // land in between. 60 ticks (~1s at the default 60Hz) is.
            case 6:
                // The demo starts the line running (OP-02): "Watch it run" that
                // produces a still factory reads as broken. What matters is that
                // the panel is authoritative from here on -- Stop really stops it.
                Expect(Bit("belt.rotate"), "the demo starts the line running");
                Expect(Bit("tower.green") && !Bit("tower.yellow") && !Bit("tower.red"),
                       "running: green only");
                _panel!.Press(PanelButton.Stop);
                break;

            // DemoDriver.Tick runs on the frame clock (_Process), not the
            // physics clock (_PhysicsProcess) this test steps on -- headless
            // has no vsync to lock the two together, so a gap of a handful of
            // physics ticks is not reliably enough real time for a frame to
            // land in between. 60 ticks (~1s at the default 60Hz) is.
            case 66:
                Expect(!Bit("belt.rotate"), "after Stop: belt off");
                Expect(Bit("tower.yellow") && !Bit("tower.green") && !Bit("tower.red"),
                       "after Stop: stopped-healthy shows yellow only");
                _panel!.Press(PanelButton.Start);
                break;

            case 126:
                Expect(Bit("belt.rotate"), "after Start: belt runs");
                Expect(Bit("tower.green") && !Bit("tower.yellow") && !Bit("tower.red"),
                       "after Start: green only");
                _panel!.Press(PanelButton.EmergencyStop);
                break;

            case 186:
                Expect(!Bit("belt.rotate"), "after E-stop: belt off");
                Expect(Bit("tower.red") && !Bit("tower.green") && !Bit("tower.yellow"),
                       "after E-stop: red only");
                _panel!.Press(PanelButton.Start);
                break;

            case 246:
                Expect(!Bit("belt.rotate"),
                       "Start while tripped: does NOT restart the belt (§4.2's whole point)");
                Expect(Bit("tower.red"), "Start while tripped: still shows red, not green");
                _panel!.Press(PanelButton.EmergencyStop);   // release the mushroom
                break;

            case 306:
                Expect(!Bit("belt.rotate"),
                       "releasing the mushroom alone does not restart the belt");
                _panel!.Press(PanelButton.Reset);
                break;

            case 366:
                Expect(!Bit("tower.red"), "after Reset: fault cleared");
                Expect(Bit("tower.yellow") && !Bit("belt.rotate"),
                       "after Reset: stopped-healthy again, belt still off until Start");
                _panel!.Press(PanelButton.Start);
                break;

            case 426:
                Expect(Bit("belt.rotate"), "Start after Reset: belt runs again");
                Expect(Bit("tower.green"), "Start after Reset: green");
                Finish();
                break;
        }
    }

    private void Finish()
    {
        // Never joined the tree (it only needs field access, not a running
        // port), so it needs freeing explicitly -- QueueFree() only fires for
        // nodes the tree actually owns.
        _bus?.Free();

        if (_failures.Count == 0)
        {
            GD.Print("self-test startstop: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr($"self-test startstop: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }
}
