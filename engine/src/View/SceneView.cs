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

    private static readonly Color ShortColour = new(0.30f, 0.55f, 0.85f);
    private static readonly Color TallColour = new(0.95f, 0.55f, 0.15f);

    // Two shared materials, not one per box per frame. Building a
    // StandardMaterial3D allocates a rendering-server resource, and the previous
    // code made one for every carton on every frame.
    private static readonly StandardMaterial3D ShortMaterial =
        new() { AlbedoColor = ShortColour, Roughness = 0.55f };
    private static readonly StandardMaterial3D TallMaterial =
        new() { AlbedoColor = TallColour, Roughness = 0.55f };

    /// <summary>
    /// Builds the parts of the view no factory *component* owns: lighting, the
    /// floor, and the grid. Belt, sensors, pusher, chute and stack light are
    /// real parts registered by <see cref="Editor.SceneEditor"/>, so building
    /// them here too would draw every one of them twice.
    /// </summary>
    public void Build(SortingScene scene)
    {
        _scene = scene;
        StudioEnvironment.AddEnvironment(this);
        StudioEnvironment.AddFloor(this);
    }

    // --- per-frame sync ---

    public override void _Process(double delta)
    {
        if (_scene is null) return;
        SyncBoxes();
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

            // Writing Size regenerates the mesh, so only do it when the box
            // actually changed height — otherwise every carton rebuilds its
            // geometry on every frame.
            var mesh = (BoxMesh)view.Mesh;
            var wanted = new Vector3((float)SortingScene.BoxLength, h, 0.24f);
            if (mesh.Size != wanted) mesh.Size = wanted;
            if (!box.IsDiverted)
            {
                view.Position = new Vector3((float)box.Position, BeltY + 0.06f + h / 2, 0);
                view.Rotation = Vector3.Zero;
            }
            else
            {
                // Ride the real chute deck, tilted to lie flat on it. Anything
                // else here is a guess about the ramp's shape, and a guess is
                // what left boxes sliding through mid-air when the chute was
                // re-angled.
                float travel = (float)(box.ChutePosition / SortingScene.ChuteTravel)
                               * Chute.DefaultRampLength;
                var anchor = new Vector3((float)SortingScene.PusherPos,
                                         PartLayout.WorkPlaneY,
                                         (float)SortingScene.ChuteLane);
                view.Position = anchor + Chute.SurfaceOffset(travel)
                                + Chute.SurfaceNormal * (h / 2);
                view.Rotation = new Vector3(Chute.InclineRadians, 0, 0);
            }
            var wantedMaterial = box.IsTall ? TallMaterial : ShortMaterial;
            if (view.MaterialOverride != wantedMaterial) view.MaterialOverride = wantedMaterial;
        }
    }
}
