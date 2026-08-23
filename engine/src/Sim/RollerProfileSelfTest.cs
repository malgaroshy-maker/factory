using System.Collections.Generic;
using FactoryForge.Editor;
using FactoryForge.Parts;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Load the roller-weighing scene, run its demo profile, and check the scale
/// and the inductive sensor both actually report something -- §4.5's own
/// assertion, specifically the material-aware sensing claim, checked here
/// rather than only asserted in a README.
///
/// <code>godot --headless --path engine -- --self-test=roller</code>
/// </summary>
public partial class RollerProfileSelfTest : Node
{
    public SceneEditor Editor { get; set; } = null!;
    public TagTable Tags { get; set; } = null!;

    /// <summary>~20s simulated: the template's metal_every=3, so this needs to
    /// cover several cartons -- one box every 3s means one metal box roughly
    /// every 9s -- to have a real chance of seeing metal_check.detect fire.</summary>
    private const int RunTicks = 1200;

    private readonly List<string> _failures = new();
    private int _step;
    private DemoDriver? _demo;
    private TagBusServer? _bus;

    /// <summary>metal_check.detect is only true for the instant a metal box
    /// overlaps the sensor's beam -- sampling it once at the end would just be
    /// gambling on the timing, so this tracks whether it was EVER true.</summary>
    private bool _sawMetal;

    /// <summary>A roller's own axis, sampled once the deck is turning. A roller
    /// spins <em>about</em> this; if the axis itself moves, the roller is
    /// tumbling instead of rolling.</summary>
    private RollerConveyor? _deck;
    private Vector3 _rollerAxis;
    private Vector3 _rollerMark;

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
            Editor.LoadTemplate("res://templates/roller_line_weighing.json");
            return;
        }

        if (_step == 3)
        {
            _bus = new TagBusServer { Tags = Tags, SceneName = "roller-line-weighing" };
            _demo = new DemoDriver { Name = "TestDemoDriver", Tags = Tags, Bus = _bus };
            AddChild(_demo);
            _demo.Start();
            Expect(_demo.Active, "roller-line-weighing: demo starts (a profile exists)");
            return;
        }

        if (_step == 30)
        {
            _deck = FindDeck(GetParent());
            if (FirstRoller(_deck) is { } roller)
            {
                _rollerAxis = roller.GlobalBasis.Y;   // the cylinder's own axis
                _rollerMark = roller.GlobalBasis.X;   // a point on its rim
            }
        }

        if (_step > 3 && Tags.Contains("metal_check.detect") && Tags.Visible("metal_check.detect") is true)
            _sawMetal = true;

        if (_step != 3 + RunTicks) return;

        if (!Tags.Contains("outfeed.count"))
        {
            Expect(false, "outfeed.count: tag exists after loading the template");
            Finish();
            return;
        }

        int outfeed = System.Convert.ToInt32(Tags.Visible("outfeed.count"));
        Expect(outfeed > 0, $"outfeed.count advances (got {outfeed} after {RunTicks} ticks)");
        Expect(_sawMetal, "metal_check.detect fired at least once for a metal carton");
        GD.Print($"  outfeed={outfeed} sawMetal={_sawMetal} after {RunTicks} ticks");

        CheckRollersRoll();
        CheckCheckweigherSpacing();
        CheckReadoutFitsItsPanel();
        Finish();
    }

    /// <summary>
    /// A roller must turn <em>about its own axis</em>. Setting a Z euler
    /// alongside the X lay-down does not do that -- Godot composes euler as
    /// Y*X*Z, so the Z term applies first, in the mesh's own frame, where it
    /// tips the cylinder over instead of spinning it. The deck then tumbles
    /// end over end, which is what a person sees and no tag-level assertion
    /// could ever catch.
    /// </summary>
    private void CheckRollersRoll()
    {
        if (FirstRoller(_deck) is not { } roller)
        {
            Expect(false, "roller deck: a roller mesh was found to check");
            return;
        }

        Vector3 axisNow = roller.GlobalBasis.Y;
        Vector3 markNow = roller.GlobalBasis.X;

        float axisDrift = axisNow.AngleTo(_rollerAxis);
        float turned = markNow.AngleTo(_rollerMark);

        Expect(axisDrift < 0.02f,
               $"a running roller keeps its own axis (drifted {Mathf.RadToDeg(axisDrift):0.0} degrees -- it is tumbling, not rolling)");
        Expect(turned > 0.05f,
               $"a running roller actually turns about that axis (rim moved {Mathf.RadToDeg(turned):0.0} degrees)");
        GD.Print($"  roller axis drift={Mathf.RadToDeg(axisDrift):0.00} deg, rim turned={Mathf.RadToDeg(turned):0.0} deg");
    }

    /// <summary>
    /// A checkweigher carrying two cartons at once reads their sum, which is
    /// neither carton's weight -- so the line has to space them further apart
    /// than the scale is long. Nothing at tag level shows this: scale.weight is
    /// a perfectly good number the whole time, just not the number anyone
    /// wanted.
    /// </summary>
    private void CheckCheckweigherSpacing()
    {
        var scale = FindScale(GetParent());
        if (scale is null)
        {
            Expect(false, "roller line: a weighing conveyor was found to check");
            return;
        }

        Expect(scale.PeakCartonsOnScale <= 1,
               $"the checkweigher only ever carries one carton at a time "
               + $"(peaked at {scale.PeakCartonsOnScale} -- it would be reading their sum)");
        GD.Print($"  checkweigher peak occupancy={scale.PeakCartonsOnScale} carton(s)");
    }

    /// <summary>The scale reports grams, so a metal carton reads "12960 g" on a
    /// bezel originally built for "000". Measured with real font metrics rather
    /// than eyeballed, because a number that runs off its own panel looks fine
    /// to every tag-level assertion there is.</summary>
    private void CheckReadoutFitsItsPanel()
    {
        var display = FindDisplay(GetParent());
        if (display is null)
        {
            Expect(false, "roller line: a digital display was found to check");
            return;
        }

        float width = display.RenderedWidth();
        Expect(width <= DigitalDisplay.PanelWidth,
               $"the weight readout fits its own panel "
               + $"({width:0.000}m of {DigitalDisplay.PanelWidth:0.000}m, showing \"{display.Value} g\")");
        GD.Print($"  readout width={width:0.000}m of {DigitalDisplay.PanelWidth:0.000}m");
    }

    private static DigitalDisplay? FindDisplay(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is DigitalDisplay display) return display;
            if (FindDisplay(child) is { } found) return found;
        }
        return null;
    }

    private static WeighingConveyor? FindScale(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is WeighingConveyor scale) return scale;
            if (FindScale(child) is { } found) return found;
        }
        return null;
    }

    private static RollerConveyor? FindDeck(Node node)
    {
        foreach (var child in node.GetChildren())
        {
            if (child is RollerConveyor deck) return deck;
            if (FindDeck(child) is { } found) return found;
        }
        return null;
    }

    private static MeshInstance3D? FirstRoller(RollerConveyor? deck)
    {
        if (deck is null) return null;
        foreach (var child in deck.GetChildren())
            if (child is MeshInstance3D mesh && mesh.Mesh is CylinderMesh) return mesh;
        return null;
    }

    private void Finish()
    {
        _bus?.Free();
        if (_failures.Count == 0)
        {
            GD.Print("self-test roller: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr($"self-test roller: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }
}
