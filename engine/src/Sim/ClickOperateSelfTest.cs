using System.Collections.Generic;
using FactoryForge.Editor;
using FactoryForge.Parts;
using FactoryForge.Scenes;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Headless check that Run mode's click actually operates the part it lands
/// on, not just the Control Panel (UX-37, §2.7). Run it with:
///
/// <code>godot --headless --path engine -- --self-test=operate</code>
///
/// Builds a real world-space ray from each part's own transform -- the same
/// technique <see cref="PanelSelfTest.CheckHitTest"/> uses -- and drives
/// <see cref="SceneEditor.PressControlAtRay"/> directly rather than through a
/// screen position, since headless has no camera to project one through
/// (<see cref="ClickPathSelfTest"/> is the on-screen half of this coverage).
///
/// Three scenes, because no one scene has everything this covers: the default
/// scene supplies a conveyor, a pusher and an emitter; its own stack light
/// exposes only the one tag (<c>.green</c>) the deterministic scene actually
/// drives, so lamp independence is checked instead against the
/// start/stop station's "tower", a StackLight placed through the normal
/// editor path with all three tags; and the tank template supplies the two
/// independently-clickable valves no other scene has.
/// </summary>
public partial class ClickOperateSelfTest : Node
{
    public TagTable Tags { get; set; } = null!;
    public SceneEditor Editor { get; set; } = null!;

    private readonly List<string> _failures = new();
    private int _step;

    private void Expect(bool condition, string what)
    {
        if (condition) return;
        _failures.Add(what);
        GD.PrintErr($"  FAIL  {what}");
    }

    private bool Bit(string id) => Tags.Contains(id) && (bool)Tags.Visible(id);

    /// <summary>A ray aimed straight down through a part's own global
    /// position, the way a click on the part (rather than on one of its
    /// sub-regions) would land -- good enough for every whole-body operable
    /// part, whose geometry straddles its own origin.</summary>
    private static (Vector3 From, Vector3 Dir) RayThrough(Node3D node) =>
        (node.GlobalPosition + Vector3.Up * 5f, Vector3.Down);

    /// <summary>A ray aimed at a specific local-space point on a part, built
    /// from the part's own transform -- mirrors
    /// <see cref="PanelSelfTest.CheckHitTestAtCurrentRotation"/>.</summary>
    private static (Vector3 From, Vector3 Dir) RayAt(Node3D node, Vector3 local)
    {
        Vector3 outward = node.GlobalTransform.Basis.Z.Normalized();
        Vector3 target = node.GlobalTransform * local;
        return (target + outward * 1.5f, -outward);
    }

    public override void _PhysicsProcess(double delta)
    {
        _step++;

        switch (_step)
        {
            case 1:
                // PressControlAtRay now refuses outright in Edit mode (UX-44) --
                // the editor starts in Edit, so this test has to enter Run
                // explicitly, the same way a real click would have to.
                Editor.SetMode(EditorMode.Run);
                RunDefaultSceneChecks();
                break;

            case 3:
                Editor.LoadTemplate("res://templates/start_stop_station.json");
                break;

            case 5:
                RunTowerCheck();
                Editor.LoadTemplate("res://templates/tank_level_control.json");
                break;

            case 7:
                RunTankCheck();
                Finish();
                break;
        }
    }

    private void RunDefaultSceneChecks()
    {
        ConveyorBelt? conveyor = null;
        PusherMechanism? pusher = null;
        Emitter? emitter = null;
        StackLight? stackLight = null;
        foreach (var child in GetParent().GetChildren())
        {
            switch (child)
            {
                case PusherMechanism p: pusher = p; break;
                case Emitter e: emitter = e; break;
                case StackLight s: stackLight = s; break;
                case ConveyorBelt c: conveyor = c; break;
            }
        }
        if (conveyor is null || pusher is null || emitter is null || stackLight is null)
        {
            Expect(false, "the default scene did not have every part this test needs");
            return;
        }

        Expect(!Bit($"{SortingTags.ConveyorId}.rotate"), "conveyor.rotate starts low");
        var (from, dir) = RayThrough(conveyor);
        Editor.PressControlAtRay(from, dir);
        Expect(Bit($"{SortingTags.ConveyorId}.rotate"), "a click on the conveyor toggles rotate on");
        Editor.PressControlAtRay(from, dir);
        Expect(!Bit($"{SortingTags.ConveyorId}.rotate"), "a second click toggles it back off");

        Expect(!Bit($"{SortingTags.PusherId}.extend"), "pusher.extend starts low");
        var pusherRay = RayThrough(pusher);
        Editor.PressControlAtRay(pusherRay.From, pusherRay.Dir);
        Expect(Bit($"{SortingTags.PusherId}.extend"), "a click on the pusher strokes it out");

        Expect(!Bit($"{SortingTags.EmitterId}.emit"), "emitter.emit starts low");
        var emitterRay = RayThrough(emitter);
        Editor.PressControlAtRay(emitterRay.From, emitterRay.Dir);
        Expect(Bit($"{SortingTags.EmitterId}.emit"), "a click on the emitter pulses emit high immediately");

        Expect(!Bit($"{SortingTags.StackLightId}.green"), "stack_light.green starts low");
        var greenRay = RayAt(stackLight, new Vector3(0, 0.45f - PartLayout.FloorDrop, 0));
        Editor.PressControlAtRay(greenRay.From, greenRay.Dir);
        Expect(Bit($"{SortingTags.StackLightId}.green"), "a click on the green lamp toggles it on");
    }

    private void RunTowerCheck()
    {
        StackLight? tower = null;
        foreach (var child in GetParent().GetChildren())
        {
            if (child is StackLight s) { tower = s; break; }
        }
        if (tower is null)
        {
            Expect(false, "start-stop-station: no StackLight ('tower') after loading the template");
            return;
        }

        Expect(!Bit("tower.green") && !Bit("tower.yellow") && !Bit("tower.red"), "tower starts fully dark");

        var greenRay = RayAt(tower, new Vector3(0, 0.45f - PartLayout.FloorDrop, 0));
        Editor.PressControlAtRay(greenRay.From, greenRay.Dir);
        Expect(Bit("tower.green"), "a click on the green lamp toggles it on");
        Expect(!Bit("tower.yellow") && !Bit("tower.red"), "clicking the green lamp leaves the other two stages alone");

        var yellowRay = RayAt(tower, new Vector3(0, 0.55f - PartLayout.FloorDrop, 0));
        Editor.PressControlAtRay(yellowRay.From, yellowRay.Dir);
        Expect(Bit("tower.yellow"), "a click on the yellow lamp toggles it independently");
        Expect(Bit("tower.green"), "clicking yellow leaves green as it was");

        var redRay = RayAt(tower, new Vector3(0, 0.65f - PartLayout.FloorDrop, 0));
        Editor.PressControlAtRay(redRay.From, redRay.Dir);
        Expect(Bit("tower.red"), "a click on the red lamp toggles it independently");
    }

    private void RunTankCheck()
    {
        LevelTank? tank = null;
        foreach (var child in GetParent().GetChildren())
        {
            if (child is LevelTank t) { tank = t; break; }
        }
        if (tank is null)
        {
            Expect(false, "tank-level-control: no LevelTank after loading the template");
            return;
        }

        Expect(System.Convert.ToDouble(Tags.Visible("tank.fill")) < 0.5, "tank.fill starts at 0");
        var fillRay = RayAt(tank, new Vector3(0, 0.81f, 0));
        Editor.PressControlAtRay(fillRay.From, fillRay.Dir);
        Expect(System.Convert.ToDouble(Tags.Visible("tank.fill")) > 99.0, "a click on the inlet pipe opens the fill valve");
        Expect(System.Convert.ToDouble(Tags.Visible("tank.drain")) < 0.5, "clicking the fill valve leaves drain alone");

        var drainRay = RayAt(tank, new Vector3(0, 0.02f, 0.30f));
        Editor.PressControlAtRay(drainRay.From, drainRay.Dir);
        Expect(System.Convert.ToDouble(Tags.Visible("tank.drain")) > 99.0, "a click on the outlet pipe opens the drain valve");

        Editor.PressControlAtRay(fillRay.From, fillRay.Dir);
        Expect(System.Convert.ToDouble(Tags.Visible("tank.fill")) < 0.5, "clicking the open fill valve shuts it again");
    }

    private void Finish()
    {
        if (_failures.Count == 0)
        {
            GD.Print("self-test operate: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr($"self-test operate: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }
}
