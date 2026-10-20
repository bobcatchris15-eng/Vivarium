using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>The one ancient habitat tree, using the same geometry in play and review captures.</summary>
public partial class PilotTreeRenderer : Node3D
{
    public MeshInstance3D? Tree { get; private set; }
    public MeshInstance3D? Roots { get; private set; }
    private VivariumWorld? _world;
    private int _terrainVersion;
    private ulong _rootHeightSignature;
    public long RootMeshRebuilds { get; private set; }
    public long UnchangedRootFootprints { get; private set; }
    private double _sinceRefresh;
    private ShaderMaterial? _material;
    private bool _authoredCrown;
public bool AuthoredCrownActive => _authoredCrown;

    public void Build(VivariumWorld world)
    {
        _world = world;
        _terrainVersion = world.Terrain.Version;
        _rootHeightSignature = PilotTreeMeshes.RootGroundSignature(world);
        RootMeshRebuilds = 0;
        UnchangedRootFootprints = 0;
        _sinceRefresh = 0;
        _authoredCrown = false;
        if (Tree != null) { RemoveChild(Tree); Tree.QueueFree(); Tree = null; }
        if (Roots != null) { RemoveChild(Roots); Roots.QueueFree(); Roots = null; }
        if (world.PilotTree == null) return;
        if (_material == null)
        {
            _material = Bridge.Shader("res://Shaders/pilot_tree.gdshader");
            Bridge.BindSurface(_material, "bark", Bridge.Surfaces.Bark);
        }
        _material.SetShaderParameter("hex_apothem", (float)world.Domain.Apothem);
        Tree = new MeshInstance3D { Name = "AncientPilotTree", Mesh = Bridge.ToArrayMesh(
            PilotTreeMeshes.BuildTrunkAndCrown(world), _material) };
        AddChild(Tree);
        Roots = new MeshInstance3D { Name = "AncientPilotRoots", Mesh = Bridge.ToArrayMesh(
            PilotTreeMeshes.BuildRoots(world), _material) };
        AddChild(Roots);
    }

    public override void _Process(double delta)
    {
        using var timing=Vivarium.Game.App.FrameProfiler.Measure("PilotTree");
        if (_world == null || Tree == null || Roots == null) return;
        _sinceRefresh += delta;
        // Never remove the legacy canopy until *every* authored stage and
        // connected axis is GPU resident. On renderer failure restore legacy.
        bool switchToAuthored = false;
        bool transition = switchToAuthored != _authoredCrown;
        bool terrainDirty = _world.Terrain.Version != _terrainVersion && _sinceRefresh >= .25;
        if (!transition && !terrainDirty) return;
        if (transition)
        {
            // This heavy mesh changes only when the validated crown is swapped.
            // It never needs to be rebuilt for erosion/soil edits.
            Tree.Mesh = Bridge.ToArrayMesh(PilotTreeMeshes.BuildTrunkAndCrown(_world,
                includeCrown: !switchToAuthored), _material, Tree.Mesh as ArrayMesh);
            _authoredCrown = switchToAuthored;
        }
        if (terrainDirty)
        {
            // Global erosion revision is not proof that the 4cm-scale terrain
            // beneath the ancient root sample rings moved. Hash precisely
            // the samples consumed by the deterministic root mesh instead
            // of rebuilding this large clipped mesh for distant terrain edits.
            ulong footprint=PilotTreeMeshes.RootGroundSignature(_world);
            if (footprint != _rootHeightSignature)
            {
                Roots.Mesh = Bridge.ToArrayMesh(PilotTreeMeshes.BuildRoots(_world),
                    _material, Roots.Mesh as ArrayMesh);
                _rootHeightSignature = footprint;
                RootMeshRebuilds++;
            }
            else UnchangedRootFootprints++;
            _terrainVersion = _world.Terrain.Version;
            _sinceRefresh = 0;
        }
    }
}
