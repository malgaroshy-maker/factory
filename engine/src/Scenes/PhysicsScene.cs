using System.Collections.Generic;
using FactoryForge.Parts;
using FactoryForge.TagBus;
using Godot;

namespace FactoryForge.Scenes;

/// <summary>
/// 100% 3D Physics-driven sorting scene.
/// Driven by Jolt 3D Physics engine: real gravity, real rigid bodies, real surface velocity,
/// real raycast photoelectric sensors, real physical pusher stroke, and Area3D triggers.
/// </summary>
public partial class PhysicsScene : Node3D
{
    public string SceneName => "sorting-by-height";
    public TagTable Tags { get; } = new();

    private ConveyorBelt _belt = null!;
    private PusherMechanism _pusher = null!;
    private PhotoelectricSensor _sensorLow = null!;
    private PhotoelectricSensor _sensorHigh = null!;
    private Emitter _emitter = null!;
    private Remover _removerShort = null!;
    private Remover _removerTall = null!;

    private bool _emitEdge;
    private int _emitIndex;
    private readonly List<bool> _emitPattern = new() { false, true };

    public int TallCount { get; private set; }
    public int ShortCount { get; private set; }

    public override void _Ready()
    {
        // Register Tag bus tags matching SortingScene
        Tags.Add(new Tag("conveyor.rotate", "Belt Conveyor (Rotate)", TagType.Bit, TagKind.Output));
        Tags.Add(new Tag("emitter.emit", "Emitter (Emit)", TagType.Bit, TagKind.Output));
        Tags.Add(new Tag("pusher.extend", "Pusher (Extend)", TagType.Bit, TagKind.Output));
        Tags.Add(new Tag("stack_light.green", "Stack Light (Green)", TagType.Bit, TagKind.Output));

        Tags.Add(new Tag("sensor_low.detect", "Diffuse Sensor Low (Detect)", TagType.Bit, TagKind.Input));
        Tags.Add(new Tag("sensor_high.detect", "Diffuse Sensor High (Detect)", TagType.Bit, TagKind.Input));
        Tags.Add(new Tag("pusher.extended", "Pusher (Extended)", TagType.Bit, TagKind.Input));
        Tags.Add(new Tag("pusher.retracted", "Pusher (Retracted)", TagType.Bit, TagKind.Input));
        Tags.Add(new Tag("counter.tall", "Counter (Tall)", TagType.Int, TagKind.Input));
        Tags.Add(new Tag("counter.short", "Counter (Short)", TagType.Int, TagKind.Input));

        Tags.Set("pusher.retracted", true);

        BuildPhysicsScene();
    }

    private void BuildPhysicsScene()
    {
        // Same staging and the same grid convention as the deterministic scene:
        // every part on a grid point at PartLayout.WorkPlaneY.
        View.StudioEnvironment.AddEnvironment(this);
        View.StudioEnvironment.AddFloor(this);

        const float y = PartLayout.WorkPlaneY;
        const float lane = 0.5f;

        // 1. Conveyor Belt (Surface velocity constraint)
        _belt = new ConveyorBelt
        {
            Name = "Belt",
            Position = new Vector3(1.5f, y, 0),
            Size = new Vector3(3.0f, PartLayout.BeltThickness, 0.5f),
            Speed = 0.5f,
        };
        AddChild(_belt);

        // 2. Pusher Mechanism (AnimatableBody3D physics piston)
        _pusher = new PusherMechanism
        {
            Name = "Pusher",
            Position = new Vector3(2.5f, y, -lane),
            StrokeLength = 0.55f,
            // ~0.45 s for the full stroke, the pace of a real pneumatic diverter.
            ExtendSpeed = 1.2f,
        };
        AddChild(_pusher);

        // 3. Sensors (RayCast3D diffuse photoelectric sensors)
        _sensorLow = new PhotoelectricSensor
        {
            Name = "SensorLow",
            Position = new Vector3(1.5f, y, lane),
            Range = 0.75f,
            HeightAboveBelt = 0.04f,
        };
        AddChild(_sensorLow);

        _sensorHigh = new PhotoelectricSensor
        {
            Name = "SensorHigh",
            Position = new Vector3(2.0f, y, lane),
            Range = 0.75f,
            HeightAboveBelt = 0.20f,
        };
        AddChild(_sensorHigh);

        // 4. Emitter
        _emitter = new Emitter
        {
            Name = "Emitter",
            Position = new Vector3(0.0f, y, 0),
        };
        AddChild(_emitter);

        // 5. Chute — the real part, so its incline and friction stay in one place
        AddChild(new Chute
        {
            Name = "Chute",
            Position = new Vector3(2.5f, y, lane),
        });

        // 6. Area3D Removers (Catch zones)
        _removerShort = new Remover
        {
            Name = "RemoverShort",
            Position = new Vector3(3.0f, 0.30f, 0),
            ZoneSize = new Vector3(0.5f, 0.5f, 0.6f),
        };
        _removerShort.BodyEntered += (body) =>
        {
            if (body is BoxPhysics)
            {
                ShortCount++;
                Tags.Set("counter.short", ShortCount);
            }
        };
        AddChild(_removerShort);

        _removerTall = new Remover
        {
            Name = "RemoverTall",
            Position = new Vector3(2.5f, 0.10f, 1.0f),
            ZoneSize = new Vector3(0.6f, 0.4f, 0.6f),
        };
        _removerTall.BodyEntered += (body) =>
        {
            if (body is BoxPhysics)
            {
                TallCount++;
                Tags.Set("counter.tall", TallCount);
            }
        };
        AddChild(_removerTall);
    }

    public override void _PhysicsProcess(double delta)
    {
        float dt = (float)delta;

        // Sync inputs from TagBus
        bool rotate = (bool)Tags.Visible("conveyor.rotate");
        _belt.SetRunning(rotate);

        bool extend = (bool)Tags.Visible("pusher.extend");
        _pusher.UpdateExtension(extend, dt);

        Tags.Set("pusher.extended", _pusher.IsExtended);
        Tags.Set("pusher.retracted", _pusher.IsRetracted);

        // Step emitter logic
        bool emit = (bool)Tags.Visible("emitter.emit");
        if (emit && !_emitEdge)
        {
            bool isTall = _emitPattern[_emitIndex % _emitPattern.Count];
            _emitIndex++;
            _emitter.SpawnBox(isTall);
        }
        _emitEdge = emit;

        // Step raycast sensors
        Tags.Set("sensor_low.detect", _sensorLow.IsDetected);
        Tags.Set("sensor_high.detect", _sensorHigh.IsDetected);
    }
}
