using System.Collections.Generic;
using FactoryForge.Parts;
using FactoryForge.Scenes;
using FactoryForge.TagBus;
using FactoryForge.View;
using Godot;

namespace FactoryForge.Editor;

/// <summary>
/// Handles interactive 3D part placement, rotation (R), deletion (Delete), and grid snapping on VoxelGrid.
/// </summary>
public partial class SceneEditor : Node3D
{
    [Export] public VoxelGrid Grid { get; set; } = null!;
    public TagTable Tags { get; set; } = null!;
    public TagInspectorUI TagInspector { get; set; } = null!;

    /// <summary>The deterministic scene, when one is running. A conveyor part
    /// that is a view of it drives its transport speed, so changing the belt's
    /// speed in the inspector actually moves the boxes rather than only
    /// scrolling the tread texture faster.</summary>
    public SortingScene? Scene { get; set; }
    public PartPropertyInspectorUI PropertyInspector { get; set; } = null!;

    /// <summary>Set by Main so Ctrl+S / Ctrl+O can open the same dialogs the
    /// toolbar buttons do — one dialog implementation, two ways to reach it.
    /// See FF-21.</summary>
    public SceneToolbarUI? Toolbar { get; set; }

    /// <summary>Raised when the mode changes, so the toolbar and palette follow
    /// it rather than each keeping their own idea of what mode we are in.</summary>
    [Signal] public delegate void ModeChangedEventHandler(bool running);

    /// <summary>Edit or Run. See <see cref="EditorMode"/> for why a click needs
    /// to mean one thing at a time.</summary>
    public EditorMode Mode { get; private set; } = EditorMode.Edit;

    private string? _activePartType;
    private Node3D? _previewNode;
    private float _previewRotationY;
    /// <summary>
    /// A part in the scene. <paramref name="OwnsTags"/> distinguishes a part the
    /// editor registered tags for from one that is only a *view* of tags the
    /// simulation owns: deleting the default belt must not delete
    /// conveyor.rotate, which SortingScene writes on every tick.
    /// </summary>
    private sealed record PlacedPart(Node3D Node, string InstanceId, string PartType, bool OwnsTags)
    {
        private Dictionary<string, string>? _tagIds;

        /// <summary>
        /// Suffix -> full "{InstanceId}.{suffix}" id, for the fixed set of
        /// tags this part type's dispatch reads or writes every physics
        /// tick. Built once, on first access, instead of a fresh string
        /// concatenation per tag per part per tick — the actual allocation
        /// FF-15 measured (~4,500/sec on a 30-part scene).
        ///
        /// Rename must call <see cref="InvalidateTagIds"/>: a record's
        /// <c>with</c> expression copies this cache along with everything
        /// else, so without that a renamed part would keep dispatching
        /// against its old ids.
        /// </summary>
        public Dictionary<string, string> TagIds => _tagIds ??= BuildTagIdCache(InstanceId, PartType);

        public void InvalidateTagIds() => _tagIds = null;

        private static readonly Dictionary<string, string[]> TagSuffixesByType = new()
        {
            ["ConveyorBelt"] = new[] { "rotate" },
            ["RollerConveyor"] = new[] { "rotate" },
            ["WeighingConveyor"] = new[] { "rotate", "weight" },
            ["PusherMechanism"] = new[] { "extend", "extended", "retracted" },
            ["PhotoelectricSensor"] = new[] { "detect" },
            ["RetroreflectiveSensor"] = new[] { "detect" },
            ["InductiveSensor"] = new[] { "detect" },
            ["Emitter"] = new[] { "emit" },
            ["ButtonPanel"] = new[] { "green", "red", "estop" },
            ["StackLight"] = new[] { "green", "yellow", "red" },
            ["DigitalDisplay"] = new[] { "value" },
            ["LightArray"] = new[] { "height", "blocked" },
            ["LevelTank"] = new[] { "level", "fill", "drain" },
        };

        private static Dictionary<string, string> BuildTagIdCache(string instanceId, string partType)
        {
            if (!TagSuffixesByType.TryGetValue(partType, out var suffixes))
                return new Dictionary<string, string>();

            var cache = new Dictionary<string, string>(suffixes.Length);
            foreach (var suffix in suffixes) cache[suffix] = $"{instanceId}.{suffix}";
            return cache;
        }
    }

    private PlacedPart? _selectedPart;
    private readonly List<PlacedPart> _placedParts = new();

    /// <summary>Emitters whose emit tag is currently high, for edge detection.</summary>
    private readonly HashSet<string> _emitEdges = new();
    private bool _emitAlternate;

    /// <summary>
    /// Raised whenever the set of tags changes — a part placed, deleted, renamed,
    /// or a whole scene loaded.
    ///
    /// A connected driver has a copy of the tag list from the last describe, so
    /// without this it never learns that the belt you just placed exists. The
    /// bus already knew how to republish (<c>SendDescribe</c> bumps the epoch);
    /// nothing was asking it to.
    /// </summary>
    [Signal] public delegate void TagsChangedEventHandler();

    /// <summary>The scene's name, as reported on the bus. Loading a file adopts
    /// the name it was saved under, so a driver is not told every custom line is
    /// the sorting demo.</summary>
    public string SceneName { get; private set; } = "sorting-by-height";

    /// <summary>Is a part currently following the cursor, waiting to be placed?</summary>
    public bool HasPlacementPreview => _previewNode is not null;

    /// <summary>Anything placed right now, so Clear can skip its own
    /// confirmation when there is nothing to lose.</summary>
    public bool HasPlacedParts => _placedParts.Count > 0;

    /// <summary>True once the scene differs from what was last saved or
    /// loaded. Drives the unsaved-changes prompt on Home, Load, and quit —
    /// there is no reliable way to tell "safe to discard" apart from
    /// "about to lose twenty minutes of work" without it.</summary>
    public bool IsDirty { get; private set; }

    public void MarkDirty() => IsDirty = true;

    private void NotifyTagsChanged()
    {
        TagInspector?.RebuildTagList();
        EmitSignal(SignalName.TagsChanged);
    }

    public void ToggleMode() => SetMode(Mode == EditorMode.Edit ? EditorMode.Run : EditorMode.Edit);

    /// <summary>
    /// Switch mode. Entering Run drops anything half-done in the editor: a
    /// placement preview left floating under the cursor, or a selection whose
    /// gizmo would otherwise hang around a part you can no longer move. A move
    /// in progress is cancelled the same way Escape cancels it, so the part it
    /// started from stays where it is rather than being lost.
    /// </summary>
    public void SetMode(EditorMode mode)
    {
        if (Mode == mode) return;

        Mode = mode;
        if (mode == EditorMode.Run)
        {
            ClearPreview();
            DeselectPart();
        }

        EmitSignal(SignalName.ModeChanged, mode == EditorMode.Run);
        GD.Print(mode == EditorMode.Run
            ? "Run mode — click the controls to operate the line"
            : "Edit mode — click parts to select, move and delete");
    }

    public void SetPlacementPart(string partType)
    {
        // The palette is hidden in Run mode, but a stray signal must not sneak a
        // ghost part into a running line.
        if (Mode == EditorMode.Run) return;

        ClearPreview();
        _activePartType = partType;
        _previewRotationY = 0f;
        _previewNode = CreatePartNode(partType);

        if (_previewNode is not null)
        {
            _previewNode.Name = "PlacementPreview";
            AddChild(_previewNode);
        }
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // F1 is the one binding that works in both modes; everything else below
        // is editing, and editing is exactly what Run mode switches off.
        if (@event is InputEventKey modeKey && modeKey.Pressed && !modeKey.Echo
            && modeKey.Keycode == Key.F1)
        {
            ToggleMode();
            return;
        }

        if (Mode == EditorMode.Run)
        {
            if (@event is InputEventMouseButton runClick && runClick.Pressed
                && runClick.ButtonIndex == MouseButton.Left)
            {
                // The event's own position, not the live cursor: they agree for a
                // real click but only the event knows where the click happened,
                // which is the difference between a testable path and one that
                // can only be checked by hand.
                PressControlAt(runClick.Position);
            }
            return;
        }

        if (_previewNode is not null && @event is InputEventMouseMotion)
        {
            UpdatePreviewPosition();
        }
        else if (_previewNode is not null && @event is InputEventMouseButton mouseBtn && mouseBtn.Pressed)
        {
            if (mouseBtn.ButtonIndex == MouseButton.Left)
            {
                PlaceCurrentPart();
            }
            else if (mouseBtn.ButtonIndex == MouseButton.Right)
            {
                ClearPreview();
            }
        }
        else if (_previewNode is null && @event is InputEventMouseButton clickBtn && clickBtn.Pressed && clickBtn.ButtonIndex == MouseButton.Left)
        {
            SelectPartAtMouse();
        }
        else if (@event is InputEventKey keyEvent && keyEvent.Pressed && !keyEvent.Echo)
        {
            if (keyEvent.CtrlPressed && keyEvent.Keycode == Key.Z)
            {
                Undo();
            }
            else if (keyEvent.CtrlPressed && keyEvent.Keycode == Key.Y)
            {
                Redo();
            }
            else if (keyEvent.CtrlPressed && keyEvent.Keycode == Key.S)
            {
                Toolbar?.ShowSaveDialog();
            }
            else if (keyEvent.CtrlPressed && keyEvent.Keycode == Key.O)
            {
                Toolbar?.ShowLoadDialog();
            }
            else if (keyEvent.CtrlPressed && keyEvent.Keycode == Key.D && _selectedPart is not null)
            {
                DuplicateSelectedPart();
            }
            else if (keyEvent.Keycode == Key.M && _selectedPart is not null)
            {
                StartMoveSelectedPart();
            }
            else if (keyEvent.Keycode == Key.R && _previewNode is not null)
            {
                _previewRotationY += Mathf.Pi / 2.0f;
                _previewNode.Rotation = new Vector3(0, _previewRotationY, 0);
            }
            else if (keyEvent.Keycode == Key.R && _previewNode is null && _selectedPart is not null)
            {
                RotateSelectedPart();
            }
            else if (keyEvent.Keycode == Key.Escape)
            {
                ClearPreview();
                DeselectPart();
            }
            else if (keyEvent.Keycode == Key.Delete || keyEvent.Keycode == Key.Backspace)
            {
                DeleteSelectedPart();
            }
        }
    }

    private readonly EditorCommandHistory _history = new();

    public void Undo()
    {
        bool did = _history.Undo();
        if (did) MarkDirty();
        GD.Print(did ? "Undid last editor action" : "Nothing to undo");
    }

    public void Redo()
    {
        bool did = _history.Redo();
        if (did) MarkDirty();
        GD.Print(did ? "Redid last editor action" : "Nothing to redo");
    }

    /// <summary>
    /// Place and delete as undoable steps. A freed node cannot be revived, so a
    /// command stores what the part *was* — type and transform — and rebuilds it
    /// on demand. That makes place and delete exact inverses of each other.
    /// </summary>
    private sealed class PartCommand : IEditorCommand
    {
        private readonly SceneEditor _editor;
        private readonly string _partType;
        private readonly Vector3 _position;
        private readonly Vector3 _rotation;
        private readonly bool _isPlacement;

        /// <summary>The id the part was given, remembered so undo/redo restores
        /// the same identity. Without it a redo minted a fresh id and silently
        /// broke any driver wiring pointing at the old one.</summary>
        private string? _instanceId;

        public PartCommand(SceneEditor editor, string partType, Vector3 position,
                           Vector3 rotation, bool isPlacement, string? instanceId = null)
        {
            _editor = editor;
            _partType = partType;
            _position = position;
            _rotation = rotation;
            _isPlacement = isPlacement;
            _instanceId = instanceId;
        }

        public void Execute()
        {
            if (_isPlacement) Respawn();
            else _editor.RemovePartAt(_partType, _position);
        }

        public void Undo()
        {
            if (_isPlacement) _editor.RemovePartAt(_partType, _position);
            else Respawn();
        }

        private void Respawn()
        {
            var placed = _editor.SpawnPart(_partType, _position, _rotation, _instanceId);
            _instanceId ??= placed?.InstanceId;
        }
    }

    /// <summary>Build, parent and register a part. Returns null if the type is
    /// unknown.</summary>
    private PlacedPart? SpawnPart(string partType, Vector3 position, Vector3 rotation,
                                  string? preferredId = null)
    {
        var node = CreatePartNode(partType);
        if (node is null) return null;

        node.Position = position;
        node.Rotation = rotation;
        GetParent()?.AddChild(node);

        string instanceId = "part";
        bool owns = false;
        if (Tags is not null)
        {
            (instanceId, owns) = PartTagManager.RegisterPartTags(node, partType, Tags, preferredId);
            NotifyTagsChanged();
        }

        var placed = new PlacedPart(node, instanceId, partType, owns);
        _placedParts.Add(placed);
        return placed;
    }

    /// <summary>Undo counterpart to <see cref="SpawnPart"/>: drops the most
    /// recently added part of this type sitting at this position.</summary>
    private void RemovePartAt(string partType, Vector3 position)
    {
        for (int i = _placedParts.Count - 1; i >= 0; i--)
        {
            var part = _placedParts[i];
            if (part.PartType != partType) continue;
            if (!part.Node.Position.IsEqualApprox(position)) continue;

            if (_selectedPart == part) DeselectPart();
            ForgetPart(part);
            NotifyTagsChanged();
            return;
        }
    }

    /// <summary>
    /// Clear the rigid-body scene back to its start state: despawn every carton
    /// and zero the removers. The machines themselves stay exactly where they
    /// are — resetting a simulation restarts the run, it does not undo the scene
    /// you built.
    /// </summary>
    public void ResetItems()
    {
        foreach (var node in GetParent()?.GetChildren() ?? new Godot.Collections.Array<Node>())
        {
            if (node is BoxPhysics box) box.QueueFree();
        }

        foreach (var part in _placedParts)
        {
            if (part.Node is LevelTank levelTank)
            {
                levelTank.ResetLevel();
                if (Tags is not null && Tags.Contains($"{part.InstanceId}.level"))
                    Tags.Set($"{part.InstanceId}.level", 0.0);
            }
            if (part.Node is ButtonPanel resetPanel)
            {
                // A reset must not start the next run holding a struck E-stop,
                // and must not deliver a press queued before the reset.
                resetPanel.ResetButtons();
                ClearPanelPulses(part.InstanceId);
                if (Tags is not null && Tags.Contains($"{part.InstanceId}.estop"))
                    Tags.Set($"{part.InstanceId}.estop", true);
            }
            if (part.Node is Emitter emitter) emitter.ResetCount();

            if (part.Node is not Remover remover) continue;

            remover.ResetCount();
            string countTag = remover.CountTag.Length > 0
                ? remover.CountTag
                : $"{part.InstanceId}.count";
            if (Tags is not null && Tags.Contains(countTag)) Tags.Set(countTag, 0);
        }

        _emitEdges.Clear();
        _emitAlternate = false;
    }

    /// <summary>Detach a part from the scene, taking its tags with it if it owns
    /// them. A view of simulation-owned tags leaves them alone.</summary>
    private void ForgetPart(PlacedPart part)
    {
        if (part.OwnsTags && Tags is not null)
            PartTagManager.UnregisterPartTags(part.InstanceId, Tags);

        ClearPanelPulses(part.InstanceId);
        part.Node.QueueFree();
        _placedParts.Remove(part);
    }

    /// <summary>
    /// Pick a part up. The original is only removed once the move is committed:
    /// deleting it up front meant cancelling with Escape destroyed the part
    /// outright, with the preview thrown away and nothing left to put back.
    /// </summary>
    private void StartMoveSelectedPart()
    {
        if (_selectedPart is not { } entry) return;

        SetPlacementPart(entry.PartType);
        if (_previewNode is not null)
        {
            _previewNode.Position = entry.Node.Position;
            _previewNode.Rotation = entry.Node.Rotation;
        }

        _movingPart = entry;
        DeselectPart();
    }

    /// <summary>The part being relocated, still in the scene until the move lands.</summary>
    private PlacedPart? _movingPart;

    private SelectionGizmo _gizmo = null!;

    public override void _Ready()
    {
        _gizmo = new SelectionGizmo { Name = "SelectionGizmo" };
        AddChild(_gizmo);
    }

    private void SelectPartAtMouse()
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;

        var mousePos = GetViewport().GetMousePosition();
        var from = camera.ProjectRayOrigin(mousePos);
        var dir = camera.ProjectRayNormal(mousePos);

        // Pick the part the ray actually enters first. The previous test ranked
        // by camera distance to a part's *origin* and accepted anything within a
        // metre of it, which cannot separate parts that sit a cell apart on the
        // same work plane — and made a 3 m belt clickable only near its middle.
        float nearest = float.MaxValue;
        PlacedPart? hitPart = null;

        foreach (var entry in _placedParts)
        {
            if (PartBounds.RayDistance(entry.Node, from, dir) is not { } distance) continue;
            if (distance >= nearest) continue;

            nearest = distance;
            hitPart = entry;
        }

        if (hitPart is not null)
        {
            _selectedPart = hitPart;
            _gizmo.AttachToNode(hitPart.Node);
            PropertyInspector?.InspectNode(hitPart.Node, hitPart.InstanceId, hitPart.PartType);
        }
        else
        {
            DeselectPart();
        }
    }

    /// <summary>
    /// Run mode's click: find the operator control under the cursor and press it.
    ///
    /// The ray is tested against the panel's <em>caps</em>, not its bounding box.
    /// The box wraps the housing, the pedestal and both lamps, so picking the
    /// part first and then a button would fire Start when you clicked the floor
    /// under the pedestal. Panels are asked directly, and each decides whether
    /// the ray actually hit one of its buttons.
    /// </summary>
    public void PressControlAt(Vector2 screenPosition)
    {
        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;

        var from = camera.ProjectRayOrigin(screenPosition);
        var dir = camera.ProjectRayNormal(screenPosition);

        float nearest = float.MaxValue;
        ButtonPanel? hitPanel = null;
        PanelButton hitButton = default;

        foreach (var entry in _placedParts)
        {
            if (entry.Node is not ButtonPanel panel) continue;
            if (panel.HitTest(from, dir) is not { } which) continue;

            // Two panels can overlap on screen; the nearer one wins, measured to
            // the panel rather than to the cap, which is close enough when the
            // caps sit within a hand's width of the housing.
            float distance = from.DistanceTo(panel.GlobalPosition);
            if (distance >= nearest) continue;

            nearest = distance;
            hitPanel = panel;
            hitButton = which;
        }

        hitPanel?.Press(hitButton);
    }

    /// <summary>
    /// Give a part a name you would willingly write into a PLC program.
    ///
    /// Fails, with a reason, rather than half-succeeding: an id already in use
    /// would collide on the tag table, and a part that only *views* tags the
    /// simulation owns cannot be renamed at all — <see cref="SortingScene"/>
    /// writes <c>conveyor.rotate</c> by that exact name every tick, so moving
    /// the tag would leave the scene talking to nothing.
    /// </summary>
    public bool TryRenameSelectedPart(string newId, out string problem)
    {
        if (_selectedPart is not { } selected) { problem = "nothing selected"; return false; }
        return TryRenamePart(selected.InstanceId, newId, out problem);
    }

    /// <summary>Rename by id, so the rules can be exercised without a mouse.</summary>
    public bool TryRenamePart(string instanceId, string newId, out string problem)
    {
        problem = "";
        if (Tags is null) { problem = "no tag table"; return false; }

        int index = _placedParts.FindIndex(p => p.InstanceId == instanceId);
        if (index < 0) { problem = $"no part called '{instanceId}'"; return false; }
        var entry = _placedParts[index];

        newId = newId.Trim();
        if (newId == entry.InstanceId) return true;

        if (!PartTagManager.IsValidInstanceId(newId))
        {
            problem = "use letters, digits and underscores — no dots or spaces";
            return false;
        }
        if (!entry.OwnsTags)
        {
            problem = "this part mirrors tags the simulation owns and cannot be renamed";
            return false;
        }
        if (PartTagManager.HasTagsFor(newId, Tags))
        {
            problem = $"'{newId}' is already taken";
            return false;
        }
        if (!PartTagManager.RenameInstance(entry.InstanceId, newId, Tags))
        {
            problem = "rename rejected by the tag table";
            return false;
        }

        // Anything holding the old id has to follow it, or it points at a tag
        // that no longer exists: the remover's count tag, and any button pulse
        // waiting to be cleared on the next tick.
        if (entry.Node is Remover remover
            && remover.CountTag.StartsWith(entry.InstanceId + "."))
        {
            remover.CountTag = newId + remover.CountTag[entry.InstanceId.Length..];
        }
        ClearPanelPulses(entry.InstanceId);

        var renamed = entry with { InstanceId = newId };
        renamed.InvalidateTagIds();   // `with` copies the old id cache too
        _placedParts[index] = renamed;
        if (_selectedPart == entry)
        {
            _selectedPart = renamed;
            // Rebuild the inspector so its name field and its idea of the
            // "previous" name both move on. Without this a second rename in a
            // row would restore the *original* id if it were rejected.
            PropertyInspector?.InspectNode(renamed.Node, newId, renamed.PartType);
        }

        MarkDirty();
        NotifyTagsChanged();
        GD.Print($"Renamed '{entry.InstanceId}' to '{newId}'");
        return true;
    }

    /// <summary>Select a part by id and show it in the inspector — what a click
    /// does, without needing a camera to click through.</summary>
    public bool SelectPartForInspection(string instanceId)
    {
        int index = _placedParts.FindIndex(p => p.InstanceId == instanceId);
        if (index < 0) return false;

        _selectedPart = _placedParts[index];
        _gizmo?.AttachToNode(_selectedPart.Node);
        PropertyInspector?.InspectNode(_selectedPart.Node, instanceId, _selectedPart.PartType);
        return true;
    }

    /// <summary>Instance ids currently in the scene, for tests and tooling.</summary>
    public IReadOnlyList<string> PlacedPartIds()
    {
        var ids = new List<string>();
        foreach (var part in _placedParts) ids.Add(part.InstanceId);
        return ids;
    }

    /// <summary>A placed part's Y rotation in radians, for tests and tooling
    /// — verifying FF-20's rotate-in-place without a mouse.</summary>
    public float? RotationYOf(string instanceId)
    {
        int index = _placedParts.FindIndex(p => p.InstanceId == instanceId);
        return index < 0 ? null : _placedParts[index].Node.Rotation.Y;
    }

    private void DeselectPart()
    {
        _selectedPart = null;
        _gizmo?.AttachToNode(null);
        PropertyInspector?.InspectNode(null, "", "");
    }

    private void DeleteSelectedPart()
    {
        if (_selectedPart is not { } entry) return;

        var position = entry.Node.Position;
        var rotation = entry.Node.Rotation;
        string partType = entry.PartType;
        GD.Print($"Deleted part '{entry.InstanceId}'");

        DeselectPart();
        _history.ExecuteCommand(new PartCommand(this, partType, position, rotation,
                                                isPlacement: false, instanceId: entry.InstanceId));
        MarkDirty();
        NotifyTagsChanged();
    }

    /// <summary>
    /// Build the sorting line out of real parts.
    /// </summary>
    /// <param name="physical">
    /// When true the parts are authoritative: sensors raycast against real
    /// cartons, the pusher reports its own limit switches, an emitter spawns
    /// rigid bodies and removers count them. When false they are views of a
    /// <see cref="SortingScene"/> that owns the same tags and simulates the
    /// boxes itself. The layout and the tag interface are identical either way,
    /// which is the point: the same PLC program drives both.
    /// </param>
    public void RegisterDefaultSceneParts(bool physical = false)
    {
        ClearAllPlacedParts();

        // The instance id is the tag *prefix*, never a whole tag name: the part
        // dispatch in _Process appends the suffix ("conveyor" -> conveyor.rotate).
        // Registering "conveyor.rotate" here silently disables the part, because
        // "conveyor.rotate.rotate" matches nothing.
        // Every part sits on a grid point at the work plane (see PartLayout):
        // X and Z are multiples of the 0.5 m cell, Y is always WorkPlaneY. The
        // scene constants are already on the grid, so the layout falls out of
        // them — no hand-tuned offsets, and "Save Scene" round-trips cleanly.
        const float y = PartLayout.WorkPlaneY;
        const float lane = (float)SortingScene.ChuteLane;

        double length = SortingScene.RemoverPos - SortingScene.EmitterPos;
        var beltNode = new ConveyorBelt
        {
            Position = new Vector3((float)(length / 2), y, 0),
            Size = new Vector3((float)length, PartLayout.BeltThickness, 0.5f),
            Speed = (float)SortingScene.BeltSpeed,
        };
        GetParent()?.AddChild(beltNode);
        Adopt(beltNode, SortingTags.ConveyorId, "ConveyorBelt");

        // Mounting heights are what makes the scene sort: the low beam sees every
        // box, the high beam only clears the tall ones. Range reaches from the
        // post across to the far belt edge.
        var sensorLowNode = new PhotoelectricSensor
        {
            Position = new Vector3((float)SortingScene.SensorLowPos, y, lane),
            Range = 0.75f,
            HeightAboveBelt = 0.04f,
            VisualOnly = !physical,
        };
        GetParent()?.AddChild(sensorLowNode);
        Adopt(sensorLowNode, SortingTags.SensorLowId, "PhotoelectricSensor");

        var sensorHighNode = new PhotoelectricSensor
        {
            Position = new Vector3((float)SortingScene.SensorHighPos, y, lane),
            Range = 0.75f,
            HeightAboveBelt = 0.20f,
            VisualOnly = !physical,
        };
        GetParent()?.AddChild(sensorHighNode);
        Adopt(sensorHighNode, SortingTags.SensorHighId, "PhotoelectricSensor");

        // Beside the belt, not on it: the pusher's origin is its mounting point
        // and it strokes towards +Z, across the lane and onto the chute.
        const float pusherStroke = 0.55f;
        var pusherNode = new PusherMechanism
        {
            Position = new Vector3((float)SortingScene.PusherPos, y, -lane),
            StrokeLength = pusherStroke,
            // Match the simulated stroke, so the plate hits its limit exactly
            // when the scene reports pusher.extended.
            ExtendSpeed = pusherStroke / (float)SortingScene.PusherTravelTime,
            VisualOnly = !physical,
        };
        GetParent()?.AddChild(pusherNode);
        Adopt(pusherNode, SortingTags.PusherId, "PusherMechanism");

        var chuteNode = new Chute
        {
            Position = new Vector3((float)SortingScene.PusherPos, y, lane),
        };
        GetParent()?.AddChild(chuteNode);
        Adopt(chuteNode, "chute_1", "Chute");

        var lightNode = new StackLight
        {
            Position = new Vector3(-lane, y, lane),
        };
        GetParent()?.AddChild(lightNode);
        Adopt(lightNode, SortingTags.StackLightId, "StackLight");

        // An operator station at the head of the line. Unlike the parts above it
        // is not a view of tags SortingScene owns — nothing in the deterministic
        // scene presses buttons — so it registers its own, in both modes. Without
        // it in the default scene, Run mode would open onto a line with nothing
        // to click and look broken.
        //
        // On the near side of the line — the same side the default camera looks
        // from — because a button you cannot see is a button you cannot press.
        // The caps already face +Z, which is where an operator stands looking at
        // the machine, so it needs no rotation. Kept at the head of the line so
        // it neither hides the sorting zone (sensors at 1.5 and 2.0, pusher at
        // 2.5) nor sits under the parts palette down the left of the screen.
        var panelNode = new ButtonPanel
        {
            Position = new Vector3(lane, y, 2.0f * lane),
        };
        GetParent()?.AddChild(panelNode);
        if (Tags is not null)
        {
            var (panelId, panelOwns) = PartTagManager.RegisterPartTags(panelNode, "ButtonPanel", Tags, "panel");
            _placedParts.Add(new PlacedPart(panelNode, panelId, "ButtonPanel", panelOwns));
        }

        // Only the rigid-body scene needs these: the deterministic scene creates
        // and retires its own boxes in code.
        if (physical)
        {
            var emitterNode = new Emitter
            {
                Position = new Vector3((float)SortingScene.EmitterPos, y, 0),
            };
            GetParent()?.AddChild(emitterNode);
            Adopt(emitterNode, SortingTags.EmitterId, "Emitter");

            var shortRemover = new Remover
            {
                Position = new Vector3((float)SortingScene.RemoverPos + 0.25f, y - 0.2f, 0),
                ZoneSize = new Vector3(0.5f, 0.6f, 0.6f),
                CountTag = SortingTags.CounterShort,
            };
            GetParent()?.AddChild(shortRemover);
            Adopt(shortRemover, "remover_short", "Remover");

            // Under the chute's discharge, so a diverted carton is counted once
            // it has actually made it down the ramp.
            var tallRemover = new Remover
            {
                Position = new Vector3((float)SortingScene.PusherPos, 0.15f, lane + 0.5f),
                ZoneSize = new Vector3(0.6f, 0.4f, 0.6f),
                CountTag = SortingTags.CounterTall,
            };
            GetParent()?.AddChild(tallRemover);
            Adopt(tallRemover, "remover_tall", "Remover");
        }

        NotifyTagsChanged();

        void Adopt(Node3D node, string instanceId, string partType) =>
            _placedParts.Add(new PlacedPart(node, instanceId, partType, OwnsTags: false));
    }

    /// <summary>
    /// Empty the world: every part gone, and the sorting line's engine-declared
    /// tags with them.
    ///
    /// <see cref="ClearAllPlacedParts"/> alone cannot do this. Those ten tags
    /// are declared at startup rather than owned by a part, so clearing the
    /// parts leaves them in the table and the next scene inherits a conveyor and
    /// two box counters it does not have. Left alone when a
    /// <see cref="SortingScene"/> is running, because that owns them.
    /// </summary>
    public void NewEmptyScene()
    {
        ClearAllPlacedParts();
        if (Scene is null && Tags is not null) SortingTags.Undeclare(Tags);

        SceneName = "untitled";
        IsDirty = false;
        NotifyTagsChanged();
        GD.Print("New empty scene");
    }

    /// <summary>Rebuild the sorting line the engine ships with.</summary>
    public void LoadDefaultSortingScene()
    {
        ClearAllPlacedParts();
        if (Scene is null && Tags is not null)
        {
            SortingTags.Undeclare(Tags);
            SortingTags.Declare(Tags);
        }

        SceneName = "sorting-by-height";
        RegisterDefaultSceneParts(physical: Scene is null);
        IsDirty = false;
        NotifyTagsChanged();
    }

    /// <summary>
    /// Start from a shipped template. Same as loading any scene file, except the
    /// sorting line's tags are dropped first so a template starts from a clean
    /// I/O list rather than inheriting the demo's.
    /// </summary>
    public void LoadTemplate(string path)
    {
        if (Scene is null && Tags is not null) SortingTags.Undeclare(Tags);
        LoadSceneFromFile(path);
    }

    public void SaveSceneToFile(string path = "user://custom_scene.json")
    {
        // Name the scene after the file it lives in, so saving as "palletiser"
        // makes the bus report "palletiser" rather than every scene claiming to
        // be the sorting demo.
        string stem = System.IO.Path.GetFileNameWithoutExtension(path);
        if (stem.Length > 0 && stem != "custom_scene") SceneName = stem;

        var data = new SceneData { Name = SceneName, Parts = CapturePartsSnapshot() };

        string json = data.ToJson();
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(json);
        IsDirty = false;
        GD.Print($"Saved scene to {path} ({_placedParts.Count} parts)");
    }

    public void LoadSceneFromFile(string path = "user://custom_scene.json")
    {
        if (!Godot.FileAccess.FileExists(path))
        {
            GD.Print($"No saved scene file found at {path}");
            return;
        }

        ClearAllPlacedParts();

        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Read);
        string json = file?.GetAsText() ?? "";
        var data = SceneData.FromJson(json);
        if (data is null) return;

        if (data.Name is { Length: > 0 }) SceneName = data.Name;

        // Before AddChild inside RestorePartsFromSnapshot: parts build their
        // geometry from position/rotation/properties in _Ready, so applying
        // them afterwards would leave the mesh showing the old configuration.
        RestorePartsFromSnapshot(data.Parts);

        IsDirty = false;
        GD.Print($"Loaded scene from {path} ({_placedParts.Count} parts)");
    }

    public void ClearAllPlacedParts()
    {
        ClearPlacedPartsCore();
        _history.Clear();   // its commands refer to parts that are now gone
        GD.Print("Cleared all editor placed parts");
    }

    /// <summary>The wipe itself, with no opinion on history. Shared by the
    /// scene-loading paths (which drop history entirely — it refers to parts
    /// this just freed) and <see cref="ClearSceneCommand"/> (which needs the
    /// wipe to be redoable without also being the thing that erases itself).
    /// </summary>
    private void ClearPlacedPartsCore()
    {
        foreach (var part in _placedParts)
        {
            if (part.OwnsTags && Tags is not null)
                PartTagManager.UnregisterPartTags(part.InstanceId, Tags);
            part.Node.QueueFree();
        }
        _placedParts.Clear();
        PartTagManager.ResetCounters();
        _movingPart = null;
        DeselectPart();
        NotifyTagsChanged();
    }

    /// <summary>Everything needed to rebuild the current parts exactly as they
    /// stand — the same shape <see cref="SaveSceneToFile"/> writes to disk,
    /// kept in memory instead so Clear can be undone without a file.</summary>
    private List<PartInstanceData> CapturePartsSnapshot()
    {
        var list = new List<PartInstanceData>();
        foreach (var part in _placedParts)
        {
            list.Add(new PartInstanceData
            {
                Id = part.InstanceId,
                Type = part.PartType,
                Position = new float[] { part.Node.Position.X, part.Node.Position.Y, part.Node.Position.Z },
                Rotation = new float[] { part.Node.Rotation.X, part.Node.Rotation.Y, part.Node.Rotation.Z },
                Properties = PartProperties.Capture(part.Node),
            });
        }
        return list;
    }

    /// <summary>Rebuild parts from a snapshot, preserving their ids so wiring
    /// and mappings survive. Shared by scene loading and Clear's undo.</summary>
    private void RestorePartsFromSnapshot(List<PartInstanceData> parts)
    {
        foreach (var p in parts) SpawnFromData(p, notify: false);
        NotifyTagsChanged();
    }

    /// <summary>
    /// Build, parent and register one part from captured data — position,
    /// rotation, type and every property PartProperties knows how to apply.
    /// The single-part building block <see cref="RestorePartsFromSnapshot"/>
    /// and <see cref="DuplicateSelectedPart"/> both reduce to: the whole
    /// difference between "rebuild ten parts" and "paste one part with its
    /// settings intact" is how many <see cref="PartInstanceData"/> you hand it.
    /// </summary>
    private PlacedPart? SpawnFromData(PartInstanceData p, bool notify = true)
    {
        var node = CreatePartNode(p.Type);
        if (node is null) return null;

        node.Position = new Vector3(p.Position[0], p.Position[1], p.Position[2]);
        node.Rotation = new Vector3(p.Rotation[0], p.Rotation[1], p.Rotation[2]);
        PartProperties.Apply(node, p.Properties);
        GetParent()?.AddChild(node);

        var (instanceId, owns) = PartTagManager.RegisterPartTags(node, p.Type, Tags, p.Id);
        var placed = new PlacedPart(node, instanceId, p.Type, owns);
        _placedParts.Add(placed);
        if (notify) NotifyTagsChanged();
        return placed;
    }

    /// <summary>Duplicate the selected part one grid cell over, with its
    /// current properties, as a single undoable placement. See FF-21.</summary>
    public void DuplicateSelectedPart()
    {
        if (_selectedPart is not { } source) return;

        float step = Grid?.CellSize ?? 0.5f;
        var offset = source.Node.Position + new Vector3(step, 0, 0);
        var data = new PartInstanceData
        {
            // Empty, not "part": RegisterPartTags only auto-numbers a fresh id
            // when preferredId is empty. A literal non-empty placeholder here
            // would make every duplicate "adopt" the first one's tags instead
            // of getting its own — same bug a duplicate id from a file guards
            // against, self-inflicted.
            Id = "",
            Type = source.PartType,
            Position = new[] { offset.X, offset.Y, offset.Z },
            Rotation = new[] { source.Node.Rotation.X, source.Node.Rotation.Y, source.Node.Rotation.Z },
            Properties = PartProperties.Capture(source.Node),
        };

        _history.ExecuteCommand(new DuplicateCommand(this, data));
        MarkDirty();
        GD.Print($"Duplicated '{source.InstanceId}'");
    }

    private sealed class DuplicateCommand : IEditorCommand
    {
        private readonly SceneEditor _editor;
        private readonly PartInstanceData _data;
        private PlacedPart? _placed;

        public DuplicateCommand(SceneEditor editor, PartInstanceData data)
        {
            _editor = editor;
            _data = data;
        }

        public void Execute() => _placed = _editor.SpawnFromData(_data);

        public void Undo()
        {
            if (_placed is { } placed) _editor.ForgetPart(placed);
            _placed = null;
        }
    }

    /// <summary>Rotate the selected part 90° in place, undoable. Before this,
    /// R only rotated a part while it was still following the cursor — to
    /// rotate one already placed you had to press M, then R, then re-commit
    /// with a click. See FF-20.</summary>
    public void RotateSelectedPart()
    {
        if (_selectedPart is not { } selected) return;

        var from = selected.Node.Rotation;
        var to = new Vector3(from.X, from.Y + Mathf.Pi / 2.0f, from.Z);
        _history.ExecuteCommand(new RotateCommand(selected.Node, from, to));
        MarkDirty();
    }

    private sealed class RotateCommand : IEditorCommand
    {
        private readonly Node3D _node;
        private readonly Vector3 _from;
        private readonly Vector3 _to;

        public RotateCommand(Node3D node, Vector3 from, Vector3 to)
        {
            _node = node;
            _from = from;
            _to = to;
        }

        public void Execute() => _node.Rotation = _to;
        public void Undo() => _node.Rotation = _from;
    }

    /// <summary>Clear pushed onto the history as a single undoable step,
    /// rather than dropping history the way loading a different scene does.
    /// Older history is still dropped: it refers to nodes this just freed, and
    /// undoing Clear rebuilds new instances, not the originals.</summary>
    private sealed class ClearSceneCommand : IEditorCommand
    {
        private readonly SceneEditor _editor;
        private readonly List<PartInstanceData> _snapshot;

        public ClearSceneCommand(SceneEditor editor, List<PartInstanceData> snapshot)
        {
            _editor = editor;
            _snapshot = snapshot;
        }

        public void Execute() => _editor.ClearPlacedPartsCore();
        public void Undo() => _editor.RestorePartsFromSnapshot(_snapshot);
    }

    /// <summary>What the toolbar's Clear button actually calls: same wipe as
    /// <see cref="ClearAllPlacedParts"/>, but a single Ctrl+Z brings it back.
    /// Clear is the most destructive action in the editor and the one most
    /// likely to be a mis-click, so it is the one Clear-family action worth
    /// paying for an undo step on.</summary>
    public void ClearAllPlacedPartsWithUndo()
    {
        if (_placedParts.Count == 0)
        {
            ClearAllPlacedParts();
            return;
        }

        var snapshot = CapturePartsSnapshot();
        _history.ExecuteAsOnly(new ClearSceneCommand(this, snapshot));
        MarkDirty();
        GD.Print("Cleared all editor placed parts (Ctrl+Z to restore)");
    }

    private void UpdatePreviewPosition()
    {
        if (_previewNode is null) return;

        var camera = GetViewport().GetCamera3D();
        if (camera is null) return;

        var mousePos = GetViewport().GetMousePosition();
        var from = camera.ProjectRayOrigin(mousePos);
        var dir = camera.ProjectRayNormal(mousePos);

        // Raycast plane intersection with floor at Y=0.5
        if (Mathf.Abs(dir.Y) > 0.001f)
        {
            float t = (0.5f - from.Y) / dir.Y;
            if (t > 0)
            {
                var hitPoint = from + dir * t;
                var snappedPoint = Grid?.SnapToGrid(hitPoint) ?? hitPoint;
                // Clamp to the build volume the grid displays (FF-24) — the
                // floor and its collision extend well past it so a carton
                // that outruns a line still lands somewhere, but nothing
                // should be *placed* out past the visible grid where the
                // student building it can no longer see where its edges are.
                if (Grid is not null)
                {
                    float maxX = Grid.GridExtentX * Grid.CellSize;
                    float maxZ = Grid.GridExtentZ * Grid.CellSize;
                    snappedPoint.X = Mathf.Clamp(snappedPoint.X, -maxX, maxX);
                    snappedPoint.Z = Mathf.Clamp(snappedPoint.Z, -maxZ, maxZ);
                }
                _previewNode.Position = new Vector3(snappedPoint.X, 0.5f, snappedPoint.Z);
            }
        }
    }

    private void PlaceCurrentPart()
    {
        if (_previewNode is null || _activePartType is null) return;

        // Committing a move: drop the original now that the new spot is chosen,
        // and carry its id across so the wiring survives the relocation.
        string? movedId = null;
        if (_movingPart is { } moving)
        {
            movedId = moving.InstanceId;
            ForgetPart(moving);
            _movingPart = null;
        }

        // Through the history, so Ctrl+Z can take it back. Nothing used to be
        // recorded at all, which left undo/redo as buttons that did nothing.
        _history.ExecuteCommand(new PartCommand(this, _activePartType,
                                                _previewNode.Position,
                                                _previewNode.Rotation,
                                                isPlacement: true,
                                                instanceId: movedId));
        MarkDirty();
        GD.Print($"Placed component '{_activePartType}' at {_previewNode.Position}");
        ClearPreview();
    }

    /// <summary>Below this, a carton has fallen off the world and is not
    /// coming back: a live RigidBody3D with ContinuousCd doing broad- and
    /// narrow-phase work every physics step, forever, since the only despawn
    /// paths were a Remover zone and Reset. See FF-12.</summary>
    private const float KillPlaneY = -2.0f;

    /// <summary>Sweeping every tick would be wasted work for something that
    /// only needs to be noticed within about a second.</summary>
    private const float KillPlaneIntervalSeconds = 1.0f;

    /// <summary>Live cartons above this pause the emitter and raise a
    /// warning, rather than an unattended scene accumulating rigid bodies
    /// without bound. Not a hard limit on anything already alive — cartons
    /// already on the belt are left to reach a remover normally.</summary>
    public const int LiveItemCap = 200;

    private float _killPlaneAccumulator;

    /// <summary>As of the last sweep (at most <see cref="KillPlaneIntervalSeconds"/>
    /// stale) — good enough for a status chip, not a physics guarantee.</summary>
    public int LiveItemCount { get; private set; }
    public bool ItemCapHit { get; private set; }

    public override void _PhysicsProcess(double delta)
    {
        if (Tags is null) return;
        float dt = (float)delta;

        _killPlaneAccumulator += dt;
        if (_killPlaneAccumulator >= KillPlaneIntervalSeconds)
        {
            _killPlaneAccumulator = 0f;
            SweepBoxes();
        }

        foreach (var part in _placedParts)
        {
            var node = part.Node;
            string instanceId = part.InstanceId;
            var ids = part.TagIds;

            switch (part.PartType)
            {
                case "RollerConveyor":
                case "ConveyorBelt":
                    if (node is ConveyorBelt belt && ids.TryGetValue("rotate", out var rotateId)
                        && Tags.TryGetVisible(rotateId, out var rotateVal))
                    {
                        belt.SetRunning((bool)rotateVal);
                        if (Scene is not null && instanceId == "conveyor")
                            Scene.TransportSpeed = belt.Speed;
                    }
                    break;

                case "PusherMechanism":
                    if (node is PusherMechanism pusher && ids.TryGetValue("extend", out var extendId)
                        && Tags.TryGetVisible(extendId, out var extendVal))
                    {
                        pusher.UpdateExtension((bool)extendVal, dt);
                        // A VisualOnly pusher mirrors a pusher the scene already
                        // simulates, so the scene keeps the limit switches.
                        if (!pusher.VisualOnly)
                        {
                            Tags.TrySet(ids["extended"], pusher.IsExtended);
                            Tags.TrySet(ids["retracted"], pusher.IsRetracted);
                        }
                    }
                    break;

                case "RetroreflectiveSensor":
                case "InductiveSensor":
                case "PhotoelectricSensor":
                    if (node is PhotoelectricSensor sensor && ids.TryGetValue("detect", out var detectId))
                    {
                        if (sensor.VisualOnly)
                        {
                            if (Tags.TryGetVisible(detectId, out var detectVal))
                                sensor.SetBeamActive((bool)detectVal);
                        }
                        else
                        {
                            Tags.TrySet(detectId, sensor.IsDetected);
                        }
                    }
                    break;

                case "Emitter":
                    // Rising edge only: holding the tag high must not fire a box
                    // every frame, which is how a real emitter input behaves.
                    if (node is Emitter emitter && ids.TryGetValue("emit", out var emitId)
                        && Tags.TryGetVisible(emitId, out var emitVal))
                    {
                        bool emit = (bool)emitVal;
                        if (emit && !_emitEdges.Contains(instanceId))
                        {
                            // Left off _emitEdges (not marked as fired) while
                            // capped, so the same rising edge spawns the
                            // instant the count drops rather than needing the
                            // signal to re-pulse. See FF-12.
                            if (LiveItemCount < LiveItemCap)
                            {
                                _emitEdges.Add(instanceId);
                                emitter.SpawnBox(_emitAlternate);
                                _emitAlternate = !_emitAlternate;
                            }
                        }
                        else if (!emit)
                        {
                            _emitEdges.Remove(instanceId);
                        }
                    }
                    break;

                case "Remover":
                    if (node is Remover remover)
                    {
                        // Not cacheable the way the others are: CountTag is a
                        // user-editable string, not a fixed "{id}.suffix".
                        string countTag = remover.CountTag.Length > 0
                            ? remover.CountTag
                            : $"{instanceId}.count";
                        Tags.TrySet(countTag, remover.RemovedCount);
                    }
                    break;

                case "ButtonPanel":
                    if (node is ButtonPanel panel)
                    {
                        if (ids.TryGetValue("green", out var panelGreenId) && Tags.TryGetVisible(panelGreenId, out var panelGreenVal))
                            panel.SetGreenLamp((bool)panelGreenVal);
                        if (ids.TryGetValue("red", out var panelRedId) && Tags.TryGetVisible(panelRedId, out var panelRedVal))
                            panel.SetRedLamp((bool)panelRedVal);

                        StepPanelButtons(panel, part);
                    }
                    break;

                case "StackLight":
                    if (node is StackLight light)
                    {
                        if (ids.TryGetValue("green", out var lightGreenId) && Tags.TryGetVisible(lightGreenId, out var lightGreenVal))
                            light.SetGreenLamp((bool)lightGreenVal);

                        if (ids.TryGetValue("yellow", out var lightYellowId) && Tags.TryGetVisible(lightYellowId, out var lightYellowVal))
                            light.SetYellowLamp((bool)lightYellowVal);

                        if (ids.TryGetValue("red", out var lightRedId) && Tags.TryGetVisible(lightRedId, out var lightRedVal))
                            light.SetRedLamp((bool)lightRedVal);
                    }
                    break;

                case "DigitalDisplay":
                    if (node is DigitalDisplay display && ids.TryGetValue("value", out var valueId)
                        && Tags.TryGetVisible(valueId, out var valueVal))
                    {
                        display.Value = (int)valueVal;
                    }
                    break;

                case "LightArray":
                    if (node is LightArray curtain)
                    {
                        if (ids.TryGetValue("height", out var heightId))
                            Tags.TrySet(heightId, (double)curtain.MeasuredHeight);
                        if (ids.TryGetValue("blocked", out var blockedId))
                            Tags.TrySet(blockedId, curtain.IsBlocked);
                    }
                    break;

                case "LevelTank":
                    if (node is LevelTank tank && ids.TryGetValue("level", out var levelId)
                        && Tags.TryGetVisible(ids["fill"], out var fillVal)
                        && Tags.TryGetVisible(ids["drain"], out var drainVal))
                    {
                        // dt is scaled simulation time, so the tank obeys pause
                        // and the time-scale control like everything else.
                        tank.Step((float)System.Convert.ToDouble(fillVal),
                                  (float)System.Convert.ToDouble(drainVal),
                                  dt);
                        Tags.TrySet(levelId, (double)tank.Level);
                    }
                    break;

                case "WeighingConveyor":
                    if (node is WeighingConveyor weighBelt)
                    {
                        if (ids.TryGetValue("rotate", out var weighRotateId) && Tags.TryGetVisible(weighRotateId, out var weighRotateVal))
                            weighBelt.SetRunning((bool)weighRotateVal);
                        if (ids.TryGetValue("weight", out var weightId))
                            Tags.TrySet(weightId, (int)weighBelt.MeasuredWeight);
                    }
                    break;
            }
        }
    }

    /// <summary>Tags a panel drove high on the previous tick, so they can be
    /// dropped on this one. Keyed by instance id.</summary>
    private readonly Dictionary<string, List<string>> _panelPulses = new();

    /// <summary>
    /// Free any carton that has fallen off the world, and update the live
    /// count the toolbar's status chip reads. Boxes are children of the
    /// scene root, not <see cref="_placedParts"/> — the same place
    /// <see cref="ResetItems"/> already looks for them.
    /// </summary>
    private void SweepBoxes()
    {
        int count = 0;
        int killed = 0;
        foreach (var child in GetParent()?.GetChildren() ?? new Godot.Collections.Array<Node>())
        {
            if (child is not BoxPhysics box) continue;
            if (box.GlobalPosition.Y < KillPlaneY)
            {
                box.QueueFree();
                killed++;
                continue;
            }
            count++;
        }

        if (killed > 0)
            GD.Print($"kill plane: freed {killed} carton(s) that fell off the world");

        LiveItemCount = count;
        bool overCap = count >= LiveItemCap;
        if (overCap && !ItemCapHit)
        {
            ItemCapHit = true;
            GD.PushWarning($"item cap reached ({LiveItemCap} live cartons) — the emitter is "
                          + "paused until the count drops");
        }
        else if (!overCap && ItemCapHit)
        {
            ItemCapHit = false;
        }
    }

    /// <summary>
    /// Drive a panel's button tags for one tick.
    ///
    /// Momentary buttons are the delicate part. The click arrives on the frame
    /// clock, the tags are written on the physics clock, and the two do not line
    /// up — so the panel queues presses and this drains the queue. Dropping the
    /// previous tick's pulse *before* raising this tick's is what bounds a press
    /// to exactly one scan: a program polling the tag sees a clean edge whether
    /// the mouse was tapped or held down for a second.
    ///
    /// The E-stop is level, not edge, and inverted: the contact is normally
    /// closed, so the tag is true while the circuit is healthy.
    /// </summary>
    private void StepPanelButtons(ButtonPanel panel, PlacedPart part)
    {
        string instanceId = part.InstanceId;
        if (!_panelPulses.TryGetValue(instanceId, out var lastTick))
        {
            lastTick = new List<string>();
            _panelPulses[instanceId] = lastTick;
        }

        foreach (string id in lastTick) Tags.TrySet(id, false);
        lastTick.Clear();

        // Not cached: built only when a button is actually pressed, which is
        // human-interaction frequency rather than the every-tick cost FF-15
        // targeted. Existence and "written" must stay two separate checks
        // here — TrySet's return also folds in "already true" and "forced",
        // and skipping lastTick.Add for either of those would leave a pulse
        // never cleared on the next tick.
        foreach (var which in panel.ConsumePresses())
        {
            string id = $"{instanceId}.{PartTagManager.PanelTagSuffix(which)}";
            if (!Tags.Contains(id)) continue;
            Tags.Set(id, true);
            lastTick.Add(id);
        }

        if (part.TagIds.TryGetValue("estop", out var estopId))
            Tags.TrySet(estopId, !panel.EmergencyStopEngaged);
    }

    /// <summary>Forget a panel's pending pulse, so a tag it raised is not
    /// cleared after the panel it belongs to has gone.</summary>
    private void ClearPanelPulses(string instanceId) => _panelPulses.Remove(instanceId);

    private void ClearPreview()
    {
        if (_previewNode is not null)
        {
            _previewNode.QueueFree();
            _previewNode = null;
        }
        _activePartType = null;
        _movingPart = null;   // a cancelled move leaves the original untouched
    }

    private static Node3D? CreatePartNode(string partType)
    {
        return partType switch
        {
            "ConveyorBelt" => new ConveyorBelt { Size = new Vector3(1.5f, 0.12f, 0.5f) },
            "PhotoelectricSensor" => new PhotoelectricSensor { Range = 0.6f },
            "PusherMechanism" => new PusherMechanism { StrokeLength = 0.45f },
            "Emitter" => new Emitter(),
            "Remover" => new Remover(),
            "ButtonPanel" => new ButtonPanel(),
            "Chute" => new Chute(),
            "StackLight" => new StackLight(),
            "DigitalDisplay" => new DigitalDisplay(),
            "WeighingConveyor" => new WeighingConveyor { Size = new Vector3(1.5f, 0.12f, 0.5f) },
            "LevelTank" => new LevelTank(),
            "LightArray" => new LightArray(),
            "RollerConveyor" => new RollerConveyor { Size = new Vector3(1.5f, 0.12f, 0.5f) },
            "RetroreflectiveSensor" => new PhotoelectricSensor
            {
                Range = 0.75f, HeightAboveBelt = 0.08f, Mode = SensingMode.Retroreflective,
            },
            "InductiveSensor" => new PhotoelectricSensor
            {
                Range = 0.75f, HeightAboveBelt = 0.06f, Mode = SensingMode.Inductive,
            },
            _ => null
        };
    }
}
