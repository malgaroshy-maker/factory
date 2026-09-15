using System.Collections.Generic;
using FactoryForge.Editor;
using FactoryForge.Parts;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Sim;

/// <summary>
/// Headless check on the loop a person is in while they build a line (BF-06):
///
/// <code>godot --headless --path engine -- --self-test=buildflow</code>
///
/// What is asserted is the *loop*, not the calls. "SetPlacementPart sets a
/// field" is true by construction; "a second click after a placement makes a
/// second part" is the thing a user is doing, and it is what was broken —
/// along with Ctrl+D, which put its copy one grid cell from the original and
/// selected nothing, so the second press duplicated the original again and
/// stacked two parts in one cell with no sign on screen that it had.
/// </summary>
public partial class BuildFlowSelfTest : Node
{
    public SceneEditor Editor { get; set; } = null!;
    public TagTable Tags { get; set; } = null!;

    private readonly List<string> _failures = new();
    private int _step;
    private bool _done;

    private void Expect(bool condition, string what)
    {
        if (condition) return;
        _failures.Add(what);
        GD.PrintErr($"  FAIL  {what}");
    }

    public override void _PhysicsProcess(double delta)
    {
        // One tick of grace: parts build their geometry in _Ready, and the
        // duplicate offset is measured from those meshes.
        if (++_step != 2 || _done) return;
        _done = true;

        try
        {
            Editor.SetMode(EditorMode.Edit);
            Editor.ClearAllPlacedParts();

            CheckPlacementStaysArmed();
            CheckEscapePutsItDown();
            CheckDuplicateWalksALine();
            CheckNudge();
            CheckMoveDisarms();
        }
        catch (System.Exception ex)
        {
            _failures.Add(ex.Message);
            GD.PrintErr($"  FAIL  threw: {ex.GetType().Name}: {ex.Message}");
        }

        if (_failures.Count == 0)
        {
            GD.Print("self-test buildflow: PASS");
            GetTree().Quit(0);
        }
        else
        {
            GD.PrintErr($"self-test buildflow: FAIL ({_failures.Count})");
            GetTree().Quit(1);
        }
    }

    private int PartCount() => Editor.PlacedPartIds().Count;

    // ---------- BF-01

    private void CheckPlacementStaysArmed()
    {
        Editor.SetPlacementPart("ConveyorBelt");
        Expect(Editor.HasPlacementPreview, "arming the tool makes a ghost");
        Expect(Editor.ArmedPartType == "ConveyorBelt", "and the editor says what it is holding");

        int before = PartCount();
        Editor.PlacePreviewAt(new Vector3(0, 0, 0));
        Expect(PartCount() == before + 1, "a click places a part");

        // The whole of BF-01: no second trip to the palette.
        Expect(Editor.HasPlacementPreview,
               "and the tool is still holding one, ready for the next cell");
        Expect(Editor.ArmedPartType == "ConveyorBelt", "still the same part");

        Editor.PlacePreviewAt(new Vector3(2.0f, 0, 0));
        Editor.PlacePreviewAt(new Vector3(4.0f, 0, 0));
        Expect(PartCount() == before + 3,
               $"three clicks make three parts (got {PartCount() - before})");

        // Distinct ids, because a repeat placement that adopted the first
        // part's tags would give three belts one `rotate` between them.
        var ids = new HashSet<string>(Editor.PlacedPartIds());
        Expect(ids.Count == PartCount(),
               $"each one gets its own instance id ({ids.Count} ids for {PartCount()} parts)");
    }

    /// <summary>A rotation set while placing survives the placement. A tool
    /// that silently springs back to 0° after every drop is worse than one
    /// that never rotated, because a line of turned belts comes out with one
    /// of them straight.</summary>
    private void CheckRotationSurvives()
    {
        Editor.RotatePreview();
        float turned = Editor.PreviewRotationY;
        Editor.PlacePreviewAt(new Vector3(6.0f, 0, 0));
        Expect(Mathf.IsEqualApprox(Editor.PreviewRotationY, turned),
               "the rotation you set while placing is still set for the next one");
    }

    // ---------- BF-01, the other half

    private void CheckEscapePutsItDown()
    {
        CheckRotationSurvives();

        Editor.CancelPlacement();
        Expect(!Editor.HasPlacementPreview, "Escape puts the part down");
        Expect(Editor.ArmedPartType is null, "and the editor says it is holding nothing");

        int before = PartCount();
        Editor.PlacePreviewAt(new Vector3(8.0f, 0, 0));
        Expect(PartCount() == before,
               "a click with nothing in hand places nothing");
    }

    // ---------- BF-02

    private void CheckDuplicateWalksALine()
    {
        Editor.ClearAllPlacedParts();
        Editor.SetPlacementPart("ConveyorBelt");
        Editor.PlacePreviewAt(new Vector3(0, 0, 0));
        Editor.CancelPlacement();

        Editor.SelectPartByIndex(0);
        Expect(Editor.SelectedInstanceId is not null, "the placed belt can be selected");

        Editor.DuplicateSelectedPart();
        Expect(PartCount() == 2, "Ctrl+D makes a copy");
        Expect(Editor.SelectedInstanceId != null && Editor.SelectedPosition is not null,
               "and the copy is what is now selected");

        Vector3 first = Editor.SelectedPosition!.Value;
        Expect(first.X >= 1.5f - 0.01f,
               $"the copy lands clear of the belt it came from, not inside it (x={first.X:0.00})");

        Editor.DuplicateSelectedPart();
        Expect(PartCount() == 3, "a second Ctrl+D makes a third");

        Vector3 second = Editor.SelectedPosition!.Value;
        Expect(second.X >= first.X + 1.5f - 0.01f,
               $"and it walks on from the copy rather than stacking on the original "
               + $"(x={second.X:0.00}, previous {first.X:0.00})");

        // The bug this replaces, stated as a check: three duplicates used to
        // occupy two cells, because nothing selected the copy.
        var cells = new HashSet<string>();
        foreach (var id in Editor.PlacedPartIds())
        {
            if (Editor.NodeFor(id) is { } node)
                cells.Add($"{node.Position.X:0.00},{node.Position.Z:0.00}");
        }
        Expect(cells.Count == 3, $"three parts stand in three cells (got {cells.Count})");

        // Every part on the grid it was placed on, and on the work plane.
        foreach (var id in Editor.PlacedPartIds())
        {
            if (Editor.NodeFor(id) is not { } node) continue;
            Expect(Mathf.IsEqualApprox(node.Position.Y, PartLayout.WorkPlaneY),
                   $"{id} sits on the work plane");
        }
    }

    // ---------- BF-03

    private void CheckNudge()
    {
        Editor.SelectPartByIndex(0);
        Vector3 before = Editor.SelectedPosition!.Value;

        Editor.NudgeSelectedPart(new Vector2(1, 0));
        Vector3 after = Editor.SelectedPosition!.Value;
        float moved = before.DistanceTo(after);

        Expect(moved > 0.01f, "an arrow key moves the selected part");
        Expect(Mathf.IsEqualApprox(moved, 0.5f),
               $"by exactly one grid cell (moved {moved:0.000} m)");
        Expect(Mathf.IsEqualApprox(after.Y, PartLayout.WorkPlaneY),
               "and keeps it on the work plane");

        Editor.Undo();
        Expect(Editor.SelectedPosition!.Value.IsEqualApprox(before),
               "one Ctrl+Z puts one nudge back");

        // Four nudges round the compass return to where they started, which is
        // the claim that the screen-relative mapping is a rotation and not four
        // unrelated directions.
        Vector3 start = Editor.SelectedPosition!.Value;
        Editor.NudgeSelectedPart(new Vector2(1, 0));
        Editor.NudgeSelectedPart(new Vector2(0, 1));
        Editor.NudgeSelectedPart(new Vector2(-1, 0));
        Editor.NudgeSelectedPart(new Vector2(0, -1));
        Expect(Editor.SelectedPosition!.Value.IsEqualApprox(start),
               "right, up, left, down comes back to where it started");
    }

    // ---------- BF-01's exception

    private void CheckMoveDisarms()
    {
        Editor.SelectPartByIndex(0);
        int before = PartCount();

        Editor.StartMoveSelected();
        Expect(Editor.HasPlacementPreview, "M picks the part up");

        Editor.PlacePreviewAt(new Vector3(0, 0, 3.0f));
        Expect(PartCount() == before,
               $"putting it down moves it rather than copying it (was {before}, now {PartCount()})");
        // The exception to BF-01, and the reason it has to be one: a re-armed
        // ghost here would drop a second copy of the part you just moved on the
        // very next click.
        Expect(!Editor.HasPlacementPreview, "and the tool is empty afterwards, not holding a copy");
    }
}
