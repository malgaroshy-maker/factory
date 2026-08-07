using System.Collections.Generic;
using FactoryForge.Parts;
using FactoryForge.Scenes;
using Godot;

namespace FactoryForge.View;

/// <summary>
/// Builds the 3D representation of the sorting scene and keeps it in step with
/// the simulation each frame.
///
/// The view is strictly a *reader* of scene state: it never writes tags and
/// never influences the simulation. That separation is what lets the same scene
/// run headless in CI with no renderer at all.
///
/// Geometry is primitives for now. Good lighting matters more than good models
/// for perceived quality at this stage — see docs/PRD.md.
/// </summary>
public partial class SceneView : Node3D
{
    private const float BeltY = 0.5f;
    private const float BeltWidth = 0.5f;

    private SortingScene _scene = null!;
    private readonly List<MeshInstance3D> _boxPool = new();
    private Node3D _pusher = null!;
    private PusherMechanism _pusherComponent = null!;
    private ConveyorBelt _beltComponent = null!;
    private PhotoelectricSensor _sensorLowComponent = null!;
    private PhotoelectricSensor _sensorHighComponent = null!;
    private MeshInstance3D _lampMesh = null!;
    private StandardMaterial3D _lampMat = null!;
    private MeshInstance3D _sensorLowBeam = null!;
    private MeshInstance3D _sensorHighBeam = null!;
    private MeshInstance3D _belt = null!;
    private StandardMaterial3D _beltMat = null!;

    private static readonly Color ShortColour = new(0.30f, 0.55f, 0.85f);
    private static readonly Color TallColour = new(0.95f, 0.55f, 0.15f);
    private static readonly Color BeamOff = new(0.35f, 0.10f, 0.10f);
    private static readonly Color BeamOn = new(1.0f, 0.15f, 0.15f);

    public void Build(SortingScene scene)
    {
        _scene = scene;
        AddEnvironment();
        AddFloor();
        AddChute();
        AddLamp();
    }

    // --- construction ---

    private void AddEnvironment()
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky { SkyMaterial = new ProceduralSkyMaterial() },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightSkyContribution = 0.9f,
            AmbientLightEnergy = 1.3f,
            SsaoEnabled = true,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        AddChild(new WorldEnvironment { Environment = env });

        var sun = new DirectionalLight3D
        {
            ShadowEnabled = true,
            LightEnergy = 1.1f,
            ShadowBlur = 2.0f,
        };
        sun.RotateX(Mathf.DegToRad(-55));
        sun.RotateY(Mathf.DegToRad(-40));
        AddChild(sun);

        var fill = new DirectionalLight3D { ShadowEnabled = false, LightEnergy = 0.35f };
        fill.RotateX(Mathf.DegToRad(-20));
        fill.RotateY(Mathf.DegToRad(140));
        AddChild(fill);
    }

    private void AddFloor()
    {
        var floorMat = Mat(new Color(0.24f, 0.22f, 0.21f), roughness: 0.95f);
        var floorMesh = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(7, 7) },
            MaterialOverride = floorMat,
        };
        AddChild(floorMesh);

        // Floor physics static body so boxes that fall or slide land on the ground
        var floorBody = new StaticBody3D();
        floorBody.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(7f, 0.1f, 7f) },
            Position = new Vector3(0, -0.05f, 0)
        });
        AddChild(floorBody);

        AddChild(new VoxelGrid { Name = "VoxelGrid", GridExtentX = 7, GridExtentZ = 7, CellSize = 0.5f });
    }

    private void AddBelt()
    {
        double length = SortingScene.RemoverPos - SortingScene.EmitterPos;
        _beltComponent = new ConveyorBelt
        {
            Name = "ConveyorBeltPhysics",
            Position = new Vector3((float)(length / 2), BeltY, 0),
            Size = new Vector3((float)length, 0.12f, BeltWidth),
            Speed = 0.5f,
        };
        AddChild(_beltComponent);

        // Legs, so it reads as a machine rather than a floating slab.
        var legMat = Mat(new Color(0.35f, 0.36f, 0.38f), metallic: 0.6f);
        for (double x = 0.2; x < length; x += 0.9)
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh { Size = new Vector3(0.06f, BeltY - 0.06f, 0.06f) },
                Position = new Vector3((float)x, (BeltY - 0.06f) / 2, 0),
                MaterialOverride = legMat,
            });
        }
    }

    private void AddSensors()
    {
        _sensorLowBeam = AddSensor(SortingScene.SensorLowPos, 0.10f, "low");
        _sensorHighBeam = AddSensor(SortingScene.SensorHighPos, 0.35f, "high");
    }

    private MeshInstance3D AddSensor(double x, float height, string name)
    {
        var postMat = Mat(new Color(0.85f, 0.85f, 0.88f), metallic: 0.4f);
        float postZ = BeltWidth / 2 + 0.12f;
        float sensorY = BeltY + height;

        AddChild(new MeshInstance3D
        {
            Name = $"sensor_{name}_post",
            Mesh = new BoxMesh { Size = new Vector3(0.04f, sensorY, 0.04f) },
            Position = new Vector3((float)x, sensorY / 2.0f, postZ),
            MaterialOverride = postMat,
        });

        var sensorBodyMat = Mat(new Color(0.95f, 0.75f, 0.10f), roughness: 0.4f);
        AddChild(new MeshInstance3D
        {
            Name = $"sensor_{name}_head",
            Mesh = new BoxMesh { Size = new Vector3(0.06f, 0.08f, 0.10f) },
            Position = new Vector3((float)x, sensorY, postZ - 0.04f),
            MaterialOverride = sensorBodyMat,
        });

        var beamMat = Mat(BeamOff, emission: BeamOff, emissionEnergy: 0.5f);
        var beam = new MeshInstance3D
        {
            Name = $"sensor_{name}_beam",
            Mesh = new CylinderMesh
            {
                TopRadius = 0.012f,
                BottomRadius = 0.012f,
                Height = BeltWidth + 0.24f,
            },
            Position = new Vector3((float)x, sensorY, 0),
            MaterialOverride = beamMat,
        };
        beam.RotateX(Mathf.Pi / 2);
        AddChild(beam);
        return beam;
    }

    private void AddPusher()
    {
        _pusher = new Node3D { Name = "pusher" };
        _pusher.Position = new Vector3((float)SortingScene.PusherPos, BeltY + 0.15f, 0);
        AddChild(_pusher);

        var bodyMat = Mat(new Color(0.75f, 0.75f, 0.78f), metallic: 0.7f, roughness: 0.35f);
        // Body sits on the far side; the head travels towards the belt centre.
        _pusher.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.22f, 0.18f, 0.22f) },
            Position = new Vector3(0, 0, -(BeltWidth / 2 + 0.30f)),
            MaterialOverride = bodyMat,
        });
        _pusher.AddChild(new MeshInstance3D
        {
            Name = "head",
            Mesh = new BoxMesh { Size = new Vector3(0.20f, 0.16f, 0.10f) },
            Position = new Vector3(0, 0, -(BeltWidth / 2 + 0.10f)),
            MaterialOverride = Mat(new Color(0.90f, 0.35f, 0.10f)),
        });
    }

    private void AddChute()
    {
        var chuteSize = new Vector3(0.5f, 0.04f, 0.6f);
        var chuteMat = Mat(new Color(0.45f, 0.42f, 0.38f), roughness: 0.9f);

        var chuteBody = new StaticBody3D
        {
            Position = new Vector3((float)SortingScene.PusherPos, BeltY - 0.05f, BeltWidth / 2 + 0.35f),
        };
        chuteBody.RotateX(Mathf.DegToRad(12));

        chuteBody.AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = chuteSize },
            MaterialOverride = chuteMat,
        });
        chuteBody.AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = chuteSize }
        });
        AddChild(chuteBody);
    }

    private void AddLamp()
    {
        _lampMat = Mat(new Color(0.2f, 0.4f, 0.2f), emission: new Color(0, 0.1f, 0),
                       emissionEnergy: 0.2f);
        _lampMesh = new MeshInstance3D
        {
            Name = "stack_light",
            Mesh = new SphereMesh { Radius = 0.08f, Height = 0.16f },
            Position = new Vector3(-0.18f, BeltY + 0.55f, 0.36f),
            MaterialOverride = _lampMat,
        };
        AddChild(_lampMesh);
        AddChild(new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.05f, BeltY + 0.5f, 0.05f) },
            Position = new Vector3(-0.18f, (BeltY + 0.5f) / 2, 0.36f),
            MaterialOverride = Mat(new Color(0.3f, 0.3f, 0.32f), metallic: 0.5f),
        });
    }

    private static StandardMaterial3D Mat(Color albedo, float metallic = 0.0f,
                                          float roughness = 0.5f,
                                          Color? emission = null,
                                          float emissionEnergy = 0f)
    {
        var m = new StandardMaterial3D
        {
            AlbedoColor = albedo,
            Metallic = metallic,
            Roughness = roughness,
        };
        if (emission is { } e)
        {
            m.EmissionEnabled = true;
            m.Emission = e;
            m.EmissionEnergyMultiplier = emissionEnergy;
        }
        return m;
    }

    // --- per-frame sync ---

    public override void _Process(double delta)
    {
        if (_scene is null) return;
        SyncBelt();
        SyncBoxes();
        SyncPusher();
        SyncIndicators();
    }

    private void SyncBelt()
    {
        if (_beltComponent is null) return;
        bool rotate = (bool)_scene.Tags.Visible("conveyor.rotate");
        _beltComponent.SetRunning(rotate);
    }

    private void SyncBoxes()
    {
        // Pool the meshes: boxes are created and removed constantly, and
        // allocating a MeshInstance3D per box per frame would churn badly.
        while (_boxPool.Count < _scene.Boxes.Count)
        {
            var mesh = new MeshInstance3D { Mesh = new BoxMesh() };
            AddChild(mesh);
            _boxPool.Add(mesh);
        }

        for (int i = 0; i < _boxPool.Count; i++)
        {
            var view = _boxPool[i];
            if (i >= _scene.Boxes.Count) { view.Visible = false; continue; }

            var box = _scene.Boxes[i];
            float h = (float)box.Height;
            view.Visible = true;
            ((BoxMesh)view.Mesh).Size = new Vector3((float)SortingScene.BoxLength, h, 0.24f);
            if (!box.IsDiverted)
            {
                view.Position = new Vector3((float)box.Position, BeltY + 0.06f + h / 2, 0);
            }
            else
            {
                float z = (float)(BeltWidth / 2 + box.ChutePosition);
                float y = (float)(BeltY + 0.06f + h / 2 - box.ChutePosition * 0.21);
                view.Position = new Vector3((float)SortingScene.PusherPos, y, z);
            }
            view.MaterialOverride = Mat(box.IsTall ? TallColour : ShortColour,
                                        roughness: 0.55f);
        }
    }

    private void SyncPusher()
    {
        if (_pusher is null) return;
        bool extending = (bool)_scene.Tags.Visible("pusher.extend");
        var head = _pusher.GetNodeOrNull<MeshInstance3D>("head");
        if (head is null) return;
        float rest = -(BeltWidth / 2 + 0.10f);
        float target = extending ? -0.02f : rest;
        head.Position = new Vector3(0, 0,
            Mathf.Lerp(head.Position.Z, target, (float)(1 - Mathf.Exp(-12 * 0.016))));
    }

    private void SyncIndicators()
    {
        if (_sensorLowBeam is not null)
            SetBeam(_sensorLowBeam, (bool)_scene.Tags.Visible("sensor_low.detect"));
        if (_sensorHighBeam is not null)
            SetBeam(_sensorHighBeam, (bool)_scene.Tags.Visible("sensor_high.detect"));

        if (_lampMat is not null)
        {
            bool green = (bool)_scene.Tags.Visible("stack_light.green");
            _lampMat.AlbedoColor = green ? new Color(0.3f, 1.0f, 0.35f) : new Color(0.2f, 0.35f, 0.2f);
            _lampMat.Emission = green ? new Color(0.2f, 1.0f, 0.3f) : new Color(0, 0.05f, 0);
            _lampMat.EmissionEnergyMultiplier = green ? 2.5f : 0.2f;
        }
    }

    private static void SetBeam(MeshInstance3D beam, bool on)
    {
        var m = (StandardMaterial3D)beam.MaterialOverride;
        m.AlbedoColor = on ? BeamOn : BeamOff;
        m.Emission = on ? BeamOn : BeamOff;
        m.EmissionEnergyMultiplier = on ? 3.0f : 0.4f;
    }
}
