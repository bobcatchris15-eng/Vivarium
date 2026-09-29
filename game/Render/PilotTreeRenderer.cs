using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>The one ancient habitat tree, using the same geometry in play and review captures.</summary>
public partial class PilotTreeRenderer : Node3D
{
    public MeshInstance3D? Tree { get; private set; }
    private VivariumWorld? _world;
    private int _terrainVersion;
    private double _sinceRefresh;
    private ShaderMaterial? _material;

    public void Build(VivariumWorld world)
    {
        _world = world;
        _terrainVersion = world.Terrain.Version;
        _sinceRefresh = 0;
        if (Tree != null) { RemoveChild(Tree); Tree.QueueFree(); Tree = null; }
        if (world.PilotTree == null) return;
        if (_material == null)
        {
            _material = Bridge.Shader("res://Shaders/pilot_tree.gdshader");
            Bridge.BindSurface(_material, "bark", Bridge.Surfaces.Bark);
        }
        _material.SetShaderParameter("hex_apothem", (float)world.Domain.Apothem);
        Tree = new MeshInstance3D { Name = "AncientPilotTree", Mesh = Bridge.ToArrayMesh(PilotTreeMeshes.Build(world), _material) };
        AddChild(Tree);
    }

    public override void _Process(double delta)
    {
        if (_world == null || Tree == null) return;
        _sinceRefresh += delta;
        if (_world.Terrain.Version == _terrainVersion || _sinceRefresh < 0.25) return;
        // Root mesh and collision both follow the edited ground. Coalesce brush updates to four uploads/second.
        Tree.Mesh = Bridge.ToArrayMesh(PilotTreeMeshes.Build(_world), _material, Tree.Mesh as ArrayMesh);
        _terrainVersion = _world.Terrain.Version;
        _sinceRefresh = 0;
    }
}
