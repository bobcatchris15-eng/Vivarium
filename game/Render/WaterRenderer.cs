using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;
namespace Vivarium.Game.Render;

/// <summary>The hydrostatic pond only. No depth, discharge, tracer density or wetness is a render input.</summary>
public partial class WaterRenderer : Node3D
{
    private VivariumWorld _w=null!;
    public EnvironmentRig? Lighting { get; set; }
    private MeshInstance3D _pond=null!;
    private ArrayMesh _mesh=new();
    private ShaderMaterial _material=null!;
    private int _terrain=-1;
    private double _waterTable=double.NaN;

public int TriangleCount {get;private set;}
    public void Build(VivariumWorld w)
    {
        _w=w;_material??=Bridge.Shader("res://Shaders/water_stream.gdshader");_material.RenderPriority=-2;
        if(_pond==null){_pond=new MeshInstance3D{Name="WaterTablePond",CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};AddChild(_pond);}
        Rebuild();
    }
    public void SetUnderwater(bool under)=>_material?.SetShaderParameter("underwater",under?1f:0f);
    public override void _Process(double delta)
    {
        if(_w!=null&&(_terrain!=_w.Terrain.Version||_waterTable!=_w.Water.WaterTable))Rebuild();
        if(Lighting!=null&&_material!=null){_material.SetShaderParameter("sky_energy",Lighting.CanopyEnergy);_material.SetShaderParameter("light_direction",Lighting.CanopyLightDirection);}
    }
    public void Rebuild()
    {
        using var scope=FrameProfiler.Measure("Water.Pond");
        var data=WaterMesh.BuildWaterTable(_w,_w.Terrain);TriangleCount=data.TriangleCount;
        Bridge.ToArrayMesh(data,_material,_mesh);_pond.Mesh=_mesh;_terrain=_w.Terrain.Version;_waterTable=_w.Water.WaterTable;
    }
}
