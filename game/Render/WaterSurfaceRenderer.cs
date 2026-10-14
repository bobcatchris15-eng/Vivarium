using Godot;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Vivarium.Game.App;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;
namespace Vivarium.Game.Render;

/// <summary>Uploads the closed surface of the authoritative liquid. No ground/wetness/depth render input.</summary>
public partial class WaterSurfaceRenderer : Node3D
{
    private MeshInstance3D _node=null!;
    private ArrayMesh? _mesh;
    private VivariumWorld _w=null!;
    private Task<MeshData>? _pending;
    private VivariumWorld? _pendingWorld;
    private double _elapsed;
    private long _version=-1;
    private readonly Dictionary<ulong,MeshInstance3D> _outlets=new();
    private ArrayMesh _outletMesh=null!;
    public int TrianglesBuilt {get;private set;}
    public long SurfaceUpdates {get;private set;}
    public int GridCells=>0;
    public override void _Ready()
    {
        _node=new MeshInstance3D{Name="LiquidSurface3D",CastShadow=GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride=Bridge.Shader("res://Shaders/liquid.gdshader")};AddChild(_node);
        _outletMesh=Bridge.ToArrayMesh(SpringOutletMesh.Build(),new StandardMaterial3D{AlbedoColor=Colors.White,VertexColorUseAsAlbedo=true,Roughness=.65f,CullMode=BaseMaterial3D.CullModeEnum.Disabled});
    }
    public void Bind(VivariumWorld world){_w=world;_elapsed=1;_version=-1;_mesh?.ClearSurfaces();TrianglesBuilt=0;SurfaceUpdates=0;foreach(var n in _outlets.Values)n.QueueFree();_outlets.Clear();}
    public override void _Process(double delta)
    {
        using var scope=FrameProfiler.Measure("LiquidSurface");if(_w==null)return;_elapsed+=delta;
        foreach(var spring in _w.Water.Springs)
        {
            if(!_outlets.TryGetValue(spring.Id.Value,out var lip))
            {lip=new MeshInstance3D{Name="SpringOutlet_"+spring.Id.Value,Mesh=_outletMesh};AddChild(lip);_outlets.Add(spring.Id.Value,lip);}
            lip.Position=new Vector3((float)spring.X,(float)_w.GroundHeight(spring.Position),(float)spring.Z);
        }
        foreach(var id in _outlets.Keys.Where(id=>!_w.Water.Springs.Any(s=>s.Id.Value==id)).ToArray())
        {_outlets[id].QueueFree();_outlets.Remove(id);}
        if(_pending!=null)
        {
            if(!_pending.IsCompleted)return;var task=_pending;_pending=null;
            if(task.IsFaulted)GD.PushError("Liquid surface: "+task.Exception);
            else if(task.IsCompletedSuccessfully&&ReferenceEquals(_w,_pendingWorld))
            {var data=task.Result;SurfaceUpdates++;TrianglesBuilt=data.TriangleCount;if(TrianglesBuilt==0)_mesh?.ClearSurfaces();
                else{_mesh=Bridge.ToArrayMesh(data,null,_mesh);_node.Mesh=_mesh;}}
        }
        if(_elapsed<1.0/30||_version==_w.Liquid.Version)return;
        _elapsed=0;_version=_w.Liquid.Version;
        var particles=_w.Liquid.Particles.ToArray();_pendingWorld=_w;
        _pending=Task.Run(()=>{var start=System.Diagnostics.Stopwatch.GetTimestamp();var mesh=LiquidSurfaceMesh.Build(particles);Bridge.PrepareMeshUpload(mesh);
            FrameProfiler.Record("LiquidSurface.Geometry",System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);return mesh;});
    }
}
