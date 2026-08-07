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
        // 1. Conveyor Belt (Surface velocity constraint)
        _belt = new ConveyorBelt
        {
            Name = "Belt",
            Position = new Vector3(1.5f, 0.5f, 0),
            Size = new Vector3(3.0f, 0.12f, 0.5f),
            Speed = 0.5f,
        };
        AddChild(_belt);

        // 2. Pusher Mechanism (AnimatableBody3D physics piston)
        _pusher = new PusherMechanism
        {
            Name = "Pusher",
            Position = new Vector3(2.5f, 0.65f, 0),
            StrokeLength = 0.35f,
            ExtendSpeed = 2.0f,
        };
        AddChild(_pusher);

        // 3. Sensors (RayCast3D diffuse photoelectric sensors)
        _sensorLow = new PhotoelectricSensor
        {
            Name = "SensorLow",
            Position = new Vector3(1.5f, 0.60f, 0.37f),
            Range = 0.60f,
        };
        AddChild(_sensorLow);

        _sensorHigh = new PhotoelectricSensor
        {
            Name = "SensorHigh",
            Position = new Vector3(2.0f, 0.80f, 0.37f),
            Range = 0.60f,
        };
        AddChild(_sensorHigh);

        // 4. Emitter
        _emitter = new Emitter
        {
            Name = "Emitter",
            Position = new Vector3(0.0f, 0.5f, 0),
        };
        AddChild(_emitter);

        // 5. Chute (12-degree inclined physics ramp)
        var chuteSize = new Vector3(0.5f, 0.04f, 0.6f);
        var chuteBody = new StaticBody3D
        {
            Name = "Chute",
            Position = new Vector3(2.5f, 0.45f, 0.60f),
        };
        chuteBody.RotateX(Mathf.DegToRad(12));
        chuteBody.AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = chuteSize } });
        AddChild(chuteBody);

        // 6. Area3D Removers (Catch zones)
        _removerShort = new Remover
        {
            Name = "RemoverShort",
            Position = new Vector3(3.2f, 0.30f, 0),
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
            Position = new Vector3(2.5f, 0.10f, 0.95f),
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
