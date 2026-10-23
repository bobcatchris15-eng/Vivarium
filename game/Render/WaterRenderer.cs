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
    public double RefreshSeconds {get;set;}=.5;
    public int TriangleCount {get;private set;}
    /// <summary>Surface wind over the standing pond, metres per second. The table is
    /// hydrostatic, so wind is the only forcing behind its capillary cascade.</summary>
    public float WindSpeed {get;set;}=.55f;
    /// <summary>Horizontal direction the pond wind blows toward.</summary>
    public Vector2 WindDirection {get;set;}=new(.94f,.34f);
    public void Build(VivariumWorld w)
    {
        _w=w;_material??=Bridge.Shader("res://Shaders/water_stream.gdshader");_material.RenderPriority=-2;
        if(_pond==null){_pond=new MeshInstance3D{Name="WaterTablePond",CastShadow=GeometryInstance3D.ShadowCastingSetting.Off};AddChild(_pond);}
        BindOptics();
        Rebuild();
    }
    public void SetUnderwater(bool under)=>_material?.SetShaderParameter("underwater",under?1f:0f);
    public override void _Process(double delta)
    {
        if(_w!=null&&(_terrain!=_w.Terrain.Version||_waterTable!=_w.Water.WaterTable))Rebuild();
        BindOptics();
    }
    /// <summary>Wind and canopy radiance are the only render inputs of the pond. They
    /// are set from the renderer so the lit shader never has to invent a breeze.</summary>
    private void BindOptics()
    {
        if(_material==null)return;
        _material.SetShaderParameter("water_wind_speed",WindSpeed);
        _material.SetShaderParameter("water_wind_direction",WindDirection);
        if(Lighting!=null){_material.SetShaderParameter("sky_energy",Lighting.CanopyEnergy);_material.SetShaderParameter("light_direction",Lighting.CanopyLightDirection);}
    }
    public void Rebuild()
    {
        using var scope=FrameProfiler.Measure("Water.Pond");
        var data=WaterMesh.BuildWaterTable(_w,_w.Terrain);TriangleCount=data.TriangleCount;
        Bridge.ToArrayMesh(data,_material,_mesh);_pond.Mesh=_mesh;_terrain=_w.Terrain.Version;_waterTable=_w.Water.WaterTable;
    }
}
