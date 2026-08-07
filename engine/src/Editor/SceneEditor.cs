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
    public PartPropertyInspectorUI PropertyInspector { get; set; } = null!;

    private string? _activePartType;
    private Node3D? _previewNode;
    private float _previewRotationY;
    private (Node3D node, string instanceId, string partType)? _selectedPart;
    private readonly List<(Node3D node, string instanceId, string partType)> _placedParts = new();

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
            else if (keyEvent.Keycode == Key.M && _selectedPart.HasValue)
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
        if (_history.Undo())
        {
            GD.Print("Undid last editor action");
        }
    }

    public void Redo()
    {
        if (_history.Redo())
        {
            GD.Print("Redid last editor action");
        }
    }

    private void StartMoveSelectedPart()
    {
        if (!_selectedPart.HasValue) return;

        var entry = _selectedPart.Value;
        SetPlacementPart(entry.partType);
        if (_previewNode is not null)
        {
            _previewNode.Position = entry.node.Position;
            _previewNode.Rotation = entry.node.Rotation;
        }

        DeleteSelectedPart();
    }

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

        // Find nearest placed part hit by ray
        float minDist = float.MaxValue;
        (Node3D node, string instanceId, string partType)? hitPart = null;

        foreach (var entry in _placedParts)
        {
            float dist = entry.node.GlobalPosition.DistanceTo(from);
            if (dist < minDist && dist < 15.0f)
            {
                var localPos = entry.node.GlobalPosition;
                var proj = from + dir * ((localPos.Y - from.Y) / (dir.Y == 0 ? 0.001f : dir.Y));
                if (proj.DistanceTo(localPos) < 1.0f)
                {
                    minDist = dist;
                    hitPart = entry;
                }
            }
        }

        if (hitPart.HasValue)
        {
            _selectedPart = hitPart;
            _gizmo.AttachToNode(hitPart.Value.node);
            PropertyInspector?.InspectNode(hitPart.Value.node, hitPart.Value.instanceId, hitPart.Value.partType);
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
        if (!_selectedPart.HasValue) return;

        var entry = _selectedPart.Value;
        entry.node.QueueFree();
        _placedParts.Remove(entry);
        GD.Print($"Deleted part '{entry.instanceId}'");

        DeselectPart();
        TagInspector?.RebuildTagList();
    }

    public void RegisterDefaultSceneParts()
    {
        ClearAllPlacedParts();

        double length = SortingScene.RemoverPos - SortingScene.EmitterPos;
        var beltNode = new ConveyorBelt
        {
            Position = new Vector3((float)(length / 2), 0.5f, 0),
            Size = new Vector3((float)length, 0.12f, 0.5f),
            Speed = 0.5f,
        };
        GetParent()?.AddChild(beltNode);
        _placedParts.Add((beltNode, "conveyor.rotate", "ConveyorBelt"));

        var sensorLowNode = new PhotoelectricSensor
        {
            Position = new Vector3((float)SortingScene.SensorLowPos, 0.5f, 0.37f),
            Range = 0.6f,
        };
        GetParent()?.AddChild(sensorLowNode);
        _placedParts.Add((sensorLowNode, "sensor_low.detect", "PhotoelectricSensor"));

        var sensorHighNode = new PhotoelectricSensor
        {
            Position = new Vector3((float)SortingScene.SensorHighPos, 0.5f, 0.37f),
            Range = 0.6f,
        };
        GetParent()?.AddChild(sensorHighNode);
        _placedParts.Add((sensorHighNode, "sensor_high.detect", "PhotoelectricSensor"));

        var pusherNode = new PusherMechanism
        {
            Position = new Vector3((float)SortingScene.PusherPos, 0.65f, 0),
            StrokeLength = 0.35f,
        };
        GetParent()?.AddChild(pusherNode);
        _placedParts.Add((pusherNode, "pusher.extend", "PusherMechanism"));

        var chuteNode = new Chute
        {
            Position = new Vector3((float)SortingScene.PusherPos, 0.45f, 0.60f),
        };
        GetParent()?.AddChild(chuteNode);
        _placedParts.Add((chuteNode, "chute_1", "Chute"));

        var lightNode = new StackLight
        {
            Position = new Vector3(-0.18f, 0.0f, 0.36f),
        };
        GetParent()?.AddChild(lightNode);
        _placedParts.Add((lightNode, "stack_light.green", "StackLight"));

        TagInspector?.RebuildTagList();
    }

    public void SaveSceneToFile(string path = "user://custom_scene.json")
    {
        var data = new SceneData { Name = "custom-factory-scene" };
        foreach (var (node, instanceId, partType) in _placedParts)
        {
            data.Parts.Add(new PartInstanceData
            {
                Id = instanceId,
                Type = partType,
                Position = new float[] { node.Position.X, node.Position.Y, node.Position.Z },
                Rotation = new float[] { node.Rotation.X, node.Rotation.Y, node.Rotation.Z }
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

                string instanceId = PartTagManager.RegisterPartTags(node, p.Type, Tags);
                _placedParts.Add((node, instanceId, p.Type));
            }
        }

        TagInspector?.RebuildTagList();
        GD.Print($"Loaded scene from {path} ({_placedParts.Count} parts)");
    }

    public void ClearAllPlacedParts()
    {
        foreach (var (node, _, _) in _placedParts)
        {
            node.QueueFree();
        }
        _placedParts.Clear();
        PartTagManager.ResetCounters();
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

        var placedNode = CreatePartNode(_activePartType);
        if (placedNode is not null)
        {
            placedNode.Position = _previewNode.Position;
            placedNode.Rotation = _previewNode.Rotation;
            GetParent()?.AddChild(placedNode);

            string instanceId = "part";
            if (Tags is not null)
            {
                instanceId = PartTagManager.RegisterPartTags(placedNode, _activePartType, Tags);
                TagInspector?.RebuildTagList();
            }

            _placedParts.Add((placedNode, instanceId, _activePartType));
            GD.Print($"Placed component '{_activePartType}' (id: {instanceId}) at {placedNode.Position}");
        }

        ClearPreview();
    }

    public override void _Process(double delta)
    {
        if (Tags is null) return;
        float dt = (float)delta;

        foreach (var (node, instanceId, partType) in _placedParts)
        {
            switch (partType)
            {
                case "ConveyorBelt":
                    if (node is ConveyorBelt belt && Tags.Contains($"{instanceId}.rotate"))
                    {
                        belt.SetRunning((bool)Tags.Visible($"{instanceId}.rotate"));
                    }
                    break;

                case "PusherMechanism":
                    if (node is PusherMechanism pusher && Tags.Contains($"{instanceId}.extend"))
                    {
                        bool extend = (bool)Tags.Visible($"{instanceId}.extend");
                        pusher.UpdateExtension(extend, dt);
                        Tags.Set($"{instanceId}.extended", pusher.IsExtended);
                        Tags.Set($"{instanceId}.retracted", pusher.IsRetracted);
                    }
                    break;

                case "PhotoelectricSensor":
                    if (node is PhotoelectricSensor sensor && Tags.Contains($"{instanceId}.detect"))
                    {
                        Tags.Set($"{instanceId}.detect", sensor.IsDetected);
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
                        else if (Tags.Contains("stack_light.green"))
                            light.SetGreenLamp((bool)Tags.Visible("stack_light.green"));

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
    }

    private static Node3D? CreatePartNode(string partType)
    {
        return partType switch
        {
            "ConveyorBelt" => new ConveyorBelt { Size = new Vector3(1.5f, 0.12f, 0.5f) },
            "PhotoelectricSensor" => new PhotoelectricSensor { Range = 0.6f },
            "PusherMechanism" => new PusherMechanism { StrokeLength = 0.35f },
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
