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

    private string? _activePartType;
    private Node3D? _previewNode;
    private float _previewRotationY;
    /// <summary>
    /// A part in the scene. <paramref name="OwnsTags"/> distinguishes a part the
    /// editor registered tags for from one that is only a *view* of tags the
    /// simulation owns: deleting the default belt must not delete
    /// conveyor.rotate, which SortingScene writes on every tick.
    /// </summary>
    private sealed record PlacedPart(Node3D Node, string InstanceId, string PartType, bool OwnsTags);

    private PlacedPart? _selectedPart;
    private readonly List<PlacedPart> _placedParts = new();

    /// <summary>Emitters whose emit tag is currently high, for edge detection.</summary>
    private readonly HashSet<string> _emitEdges = new();
    private bool _emitAlternate;

    public void SetPlacementPart(string partType)
    {
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
            else if (keyEvent.Keycode == Key.M && _selectedPart is not null)
            {
                StartMoveSelectedPart();
            }
            else if (keyEvent.Keycode == Key.R && _previewNode is not null)
            {
                _previewRotationY += Mathf.Pi / 2.0f;
                _previewNode.Rotation = new Vector3(0, _previewRotationY, 0);
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
        GD.Print(_history.Undo() ? "Undid last editor action" : "Nothing to undo");
    }

    public void Redo()
    {
        GD.Print(_history.Redo() ? "Redid last editor action" : "Nothing to redo");
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
            TagInspector?.RebuildTagList();
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
            TagInspector?.RebuildTagList();
            return;
        }
    }

    /// <summary>Detach a part from the scene, taking its tags with it if it owns
    /// them. A view of simulation-owned tags leaves them alone.</summary>
    private void ForgetPart(PlacedPart part)
    {
        if (part.OwnsTags && Tags is not null)
            PartTagManager.UnregisterPartTags(part.InstanceId, Tags);

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
        TagInspector?.RebuildTagList();
    }

    public void RegisterDefaultSceneParts()
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
        _placedParts.Add(new PlacedPart(beltNode, "conveyor", "ConveyorBelt", OwnsTags: false));

        // Mounting heights are what makes the scene sort: the low beam sees every
        // box, the high beam only clears the tall ones. Range reaches from the
        // post across to the far belt edge.
        var sensorLowNode = new PhotoelectricSensor
        {
            Position = new Vector3((float)SortingScene.SensorLowPos, y, lane),
            Range = 0.75f,
            HeightAboveBelt = 0.04f,
            VisualOnly = true,
        };
        GetParent()?.AddChild(sensorLowNode);
        _placedParts.Add(new PlacedPart(sensorLowNode, "sensor_low", "PhotoelectricSensor", OwnsTags: false));

        var sensorHighNode = new PhotoelectricSensor
        {
            Position = new Vector3((float)SortingScene.SensorHighPos, y, lane),
            Range = 0.75f,
            HeightAboveBelt = 0.20f,
            VisualOnly = true,
        };
        GetParent()?.AddChild(sensorHighNode);
        _placedParts.Add(new PlacedPart(sensorHighNode, "sensor_high", "PhotoelectricSensor", OwnsTags: false));

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
            VisualOnly = true,
        };
        GetParent()?.AddChild(pusherNode);
        _placedParts.Add(new PlacedPart(pusherNode, "pusher", "PusherMechanism", OwnsTags: false));

        var chuteNode = new Chute
        {
            Position = new Vector3((float)SortingScene.PusherPos, y, lane),
        };
        GetParent()?.AddChild(chuteNode);
        _placedParts.Add(new PlacedPart(chuteNode, "chute_1", "Chute", OwnsTags: false));

        var lightNode = new StackLight
        {
            Position = new Vector3(-lane, y, lane),
        };
        GetParent()?.AddChild(lightNode);
        _placedParts.Add(new PlacedPart(lightNode, "stack_light", "StackLight", OwnsTags: false));

        TagInspector?.RebuildTagList();
    }

    public void SaveSceneToFile(string path = "user://custom_scene.json")
    {
        var data = new SceneData { Name = "custom-factory-scene" };
        foreach (var part in _placedParts)
        {
            data.Parts.Add(new PartInstanceData
            {
                Id = part.InstanceId,
                Type = part.PartType,
                Position = new float[] { part.Node.Position.X, part.Node.Position.Y, part.Node.Position.Z },
                Rotation = new float[] { part.Node.Rotation.X, part.Node.Rotation.Y, part.Node.Rotation.Z }
            });
        }

        string json = data.ToJson();
        using var file = Godot.FileAccess.Open(path, Godot.FileAccess.ModeFlags.Write);
        file?.StoreString(json);
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

        foreach (var p in data.Parts)
        {
            var node = CreatePartNode(p.Type);
            if (node is not null)
            {
                node.Position = new Vector3(p.Position[0], p.Position[1], p.Position[2]);
                node.Rotation = new Vector3(p.Rotation[0], p.Rotation[1], p.Rotation[2]);
                GetParent()?.AddChild(node);

                // Reuse the saved id so the reloaded scene keeps its wiring.
                var (instanceId, owns) = PartTagManager.RegisterPartTags(node, p.Type, Tags, p.Id);
                _placedParts.Add(new PlacedPart(node, instanceId, p.Type, owns));
            }
        }

        TagInspector?.RebuildTagList();
        GD.Print($"Loaded scene from {path} ({_placedParts.Count} parts)");
    }

    public void ClearAllPlacedParts()
    {
        foreach (var part in _placedParts)
        {
            if (part.OwnsTags && Tags is not null)
                PartTagManager.UnregisterPartTags(part.InstanceId, Tags);
            part.Node.QueueFree();
        }
        _placedParts.Clear();
        PartTagManager.ResetCounters();
        _history.Clear();   // its commands refer to parts that are now gone
        _movingPart = null;
        DeselectPart();
        TagInspector?.RebuildTagList();
        GD.Print("Cleared all editor placed parts");
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
        GD.Print($"Placed component '{_activePartType}' at {_previewNode.Position}");
        ClearPreview();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (Tags is null) return;
        float dt = (float)delta;

        foreach (var (node, instanceId, partType, _) in _placedParts)
        {
            switch (partType)
            {
                case "ConveyorBelt":
                    if (node is ConveyorBelt belt && Tags.Contains($"{instanceId}.rotate"))
                    {
                        belt.SetRunning((bool)Tags.Visible($"{instanceId}.rotate"));
                        if (Scene is not null && instanceId == "conveyor")
                            Scene.TransportSpeed = belt.Speed;
                    }
                    break;

                case "PusherMechanism":
                    if (node is PusherMechanism pusher && Tags.Contains($"{instanceId}.extend"))
                    {
                        bool extend = (bool)Tags.Visible($"{instanceId}.extend");
                        pusher.UpdateExtension(extend, dt);
                        // A VisualOnly pusher mirrors a pusher the scene already
                        // simulates, so the scene keeps the limit switches.
                        if (!pusher.VisualOnly)
                        {
                            Tags.Set($"{instanceId}.extended", pusher.IsExtended);
                            Tags.Set($"{instanceId}.retracted", pusher.IsRetracted);
                        }
                    }
                    break;

                case "PhotoelectricSensor":
                    if (node is PhotoelectricSensor sensor && Tags.Contains($"{instanceId}.detect"))
                    {
                        if (sensor.VisualOnly)
                            sensor.SetBeamActive((bool)Tags.Visible($"{instanceId}.detect"));
                        else
                            Tags.Set($"{instanceId}.detect", sensor.IsDetected);
                    }
                    break;

                case "Emitter":
                    // Rising edge only: holding the tag high must not fire a box
                    // every frame, which is how a real emitter input behaves.
                    if (node is Emitter emitter && Tags.Contains($"{instanceId}.emit"))
                    {
                        bool emit = (bool)Tags.Visible($"{instanceId}.emit");
                        if (emit && !_emitEdges.Contains(instanceId))
                        {
                            _emitEdges.Add(instanceId);
                            emitter.SpawnBox(_emitAlternate);
                            _emitAlternate = !_emitAlternate;
                        }
                        else if (!emit)
                        {
                            _emitEdges.Remove(instanceId);
                        }
                    }
                    break;

                case "Remover":
                    if (node is Remover remover && Tags.Contains($"{instanceId}.count"))
                    {
                        Tags.Set($"{instanceId}.count", remover.RemovedCount);
                    }
                    break;

                case "ButtonPanel":
                    if (node is ButtonPanel panel)
                    {
                        if (Tags.Contains($"{instanceId}.green"))
                            panel.SetGreenLamp((bool)Tags.Visible($"{instanceId}.green"));
                        if (Tags.Contains($"{instanceId}.red"))
                            panel.SetRedLamp((bool)Tags.Visible($"{instanceId}.red"));
                    }
                    break;

                case "StackLight":
                    if (node is StackLight light)
                    {
                        if (Tags.Contains($"{instanceId}.green"))
                            light.SetGreenLamp((bool)Tags.Visible($"{instanceId}.green"));

                        if (Tags.Contains($"{instanceId}.yellow"))
                            light.SetYellowLamp((bool)Tags.Visible($"{instanceId}.yellow"));

                        if (Tags.Contains($"{instanceId}.red"))
                            light.SetRedLamp((bool)Tags.Visible($"{instanceId}.red"));
                    }
                    break;

                case "DigitalDisplay":
                    if (node is DigitalDisplay display && Tags.Contains($"{instanceId}.value"))
                    {
                        display.Value = (int)Tags.Visible($"{instanceId}.value");
                    }
                    break;

                case "WeighingConveyor":
                    if (node is WeighingConveyor weighBelt)
                    {
                        if (Tags.Contains($"{instanceId}.rotate"))
                            weighBelt.SetRunning((bool)Tags.Visible($"{instanceId}.rotate"));
                        if (Tags.Contains($"{instanceId}.weight"))
                            Tags.Set($"{instanceId}.weight", (int)weighBelt.MeasuredWeight);
                    }
                    break;
            }
        }
    }

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
            _ => null
        };
    }
}
