using Godot;

namespace FactoryForge.Parts;

/// <summary>
/// Process tank with an analog level, a modulating fill valve and a modulating
/// drain valve — the classic analog exercise a discrete sorting line cannot
/// teach. Everything else in the library is on/off; this is the part you write
/// a PID against.
///
/// The level is not a straight integrator. Outflow follows Torricelli, so it
/// falls with the square root of the head:
///
///     dL/dt = FillRate·(fill/100) − DrainRate·(drain/100)·√(L/100)
///
/// That matters pedagogically: the process gain changes with level, so a
/// controller tuned at the top of the tank overshoots at the bottom. A plain
/// integrator would make the exercise unrealistically easy.
/// </summary>
public partial class LevelTank : Node3D
{
    /// <summary>Percent per second added at a fully open fill valve.</summary>
    [Export] public float FillRate { get; set; } = 18.0f;

    /// <summary>Percent per second drained at a full valve and a full tank.</summary>
    [Export] public float DrainRate { get; set; } = 22.0f;

    /// <summary>Nominal capacity in litres. Display only — the model works in
    /// percent, which is what a level transmitter reports.</summary>
    [Export] public float CapacityLitres { get; set; } = 500.0f;

    private const float TankHeight = 0.70f;
    private const float TankRadius = 0.22f;
    private const float WallInset = 0.015f;

    private MeshInstance3D _liquid = null!;
    private CylinderMesh _liquidMesh = null!;
    private Label3D _readout = null!;

    /// <summary>Level as a percentage, 0–100. This is the measured variable.</summary>
    public float Level { get; private set; }

    /// <summary>True when the tank has run dry or brimmed over — the states an
    /// interlock is supposed to prevent.</summary>
    public bool IsEmpty => Level <= 0.01f;
    public bool IsFull => Level >= 99.99f;

    public override void _Ready()
    {
        var steel = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.62f, 0.64f, 0.68f),
            Metallic = 0.35f,
            Roughness = 0.45f,
        };

        // Glass shell, so the liquid column inside is readable at a glance.
        var glass = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.72f, 0.80f, 0.86f, 0.22f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Metallic = 0.10f,
            Roughness = 0.10f,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };

        float centre = TankHeight / 2;

        AddChild(new MeshInstance3D
        {
            Name = "Shell",
            Mesh = new CylinderMesh
            {
                TopRadius = TankRadius,
                BottomRadius = TankRadius,
                Height = TankHeight,
            },
            MaterialOverride = glass,
            Position = new Vector3(0, centre, 0),
        });

        foreach (float y in new[] { 0.0f, TankHeight })
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new CylinderMesh
                {
                    TopRadius = TankRadius + 0.015f,
                    BottomRadius = TankRadius + 0.015f,
                    Height = 0.03f,
                },
                MaterialOverride = steel,
                Position = new Vector3(0, y, 0),
            });
        }

        // Liquid column. Scaled from the bottom as the level changes.
        _liquidMesh = new CylinderMesh
        {
            TopRadius = TankRadius - WallInset,
            BottomRadius = TankRadius - WallInset,
            Height = 0.001f,
        };
        _liquid = new MeshInstance3D
        {
            Name = "Liquid",
            Mesh = _liquidMesh,
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.20f, 0.55f, 0.85f, 0.80f),
                Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                Roughness = 0.25f,
                Metallic = 0.05f,
            },
        };
        AddChild(_liquid);

        // Inlet over the top, outlet at the foot.
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.035f, BottomRadius = 0.035f, Height = 0.22f },
            MaterialOverride = steel,
            Position = new Vector3(0, TankHeight + 0.11f, 0),
        });
        var outlet = new MeshInstance3D
        {
            Mesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = 0.26f },
            MaterialOverride = steel,
            Position = new Vector3(0, 0.02f, TankRadius + 0.08f),
        };
        outlet.RotateX(Mathf.Pi / 2);
        AddChild(outlet);

        // Legs down to the floor, per the work-plane convention.
        foreach (var offset in new[] { new Vector2(1, 1), new Vector2(-1, 1),
                                       new Vector2(1, -1), new Vector2(-1, -1) })
        {
            AddChild(new MeshInstance3D
            {
                Mesh = new BoxMesh
                {
                    Size = new Vector3(0.035f, PartLayout.FloorDrop, 0.035f),
                },
                MaterialOverride = steel,
                Position = new Vector3(offset.X * TankRadius * 0.62f,
                                       -PartLayout.FloorDrop / 2,
                                       offset.Y * TankRadius * 0.62f),
            });
        }

        _readout = new Label3D
        {
            Name = "LevelReadout",
            Text = "0.0 %",
            Position = new Vector3(0, TankHeight + 0.30f, 0),
            Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
            FontSize = 96,
            PixelSize = 0.0016f,
            Modulate = new Color(0.55f, 0.85f, 1.0f),
        };
        AddChild(_readout);

        ApplyLevel();
    }

    /// <summary>
    /// Integrate one step. <paramref name="fill"/> and <paramref name="drain"/>
    /// are valve openings in percent, which is what an analog output actually
    /// delivers — a PID writing 0–100 needs no scaling to drive this.
    /// </summary>
    public void Step(float fill, float drain, float delta)
    {
        fill = Mathf.Clamp(fill, 0.0f, 100.0f);
        drain = Mathf.Clamp(drain, 0.0f, 100.0f);

        float inflow = FillRate * (fill / 100.0f);
        float outflow = DrainRate * (drain / 100.0f) * Mathf.Sqrt(Mathf.Max(Level, 0.0f) / 100.0f);

        Level = Mathf.Clamp(Level + (inflow - outflow) * delta, 0.0f, 100.0f);
        ApplyLevel();
    }

    public void ResetLevel()
    {
        Level = 0.0f;
        ApplyLevel();
    }

    /// <summary>Level the geometry was last built for, so an unchanged tank
    /// costs nothing.</summary>
    private float _drawnLevel = float.NaN;

    private void ApplyLevel()
    {
        // Assigning CylinderMesh.Height regenerates the mesh and re-uploads it,
        // and setting Label3D.Text rebuilds its glyph mesh. Doing both every
        // physics tick for a value that has not moved is pure waste — and with
        // a renderer attached it was enough to stall the frame loop outright:
        // an idle tank scene ran headless and hung in the GUI.
        if (Mathf.IsEqualApprox(Level, _drawnLevel)) return;
        _drawnLevel = Level;

        float usable = TankHeight - 2 * WallInset;
        float height = Mathf.Max(usable * (Level / 100.0f), 0.001f);

        _liquidMesh.Height = height;
        _liquid.Position = new Vector3(0, WallInset + height / 2, 0);
        _liquid.Visible = Level > 0.05f;

        _readout.Text = $"{Level:0.0} %";
    }
}
