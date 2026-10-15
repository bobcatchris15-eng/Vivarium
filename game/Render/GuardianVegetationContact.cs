using System;
using System.Collections.Generic;
using System.Linq;
using Godot;

namespace Vivarium.Game.Render;

/// <summary>Local contact with the instances actually drawn by FloraRenderer.
/// Plants bend about their roots and recover; no simulation population is moved.</summary>
public partial class GuardianVegetationContact : Node
{
    private sealed class Plant
    {
        public MultiMeshInstance3D Draw = null!;
        public int Index;
        public Transform3D Base, Applied;
        public Vector3 Bend, Target, Root;
        public Aabb Bounds;
    }
    private readonly Dictionary<(ulong, int), Plant> _plants = new();
    private IEnumerator<(MultiMeshInstance3D, int)>? _scan;
    private readonly record struct DrawInfo(MultiMeshInstance3D Draw,MultiMesh Buffer,Transform3D Global,Transform3D Inverse,Aabb Bounds,int Count);
    private readonly Dictionary<MultiMeshInstance3D,DrawInfo> _frameDraws=new(ReferenceEqualityComparer.Instance);
    private bool _insideFrame;
    public int DrawFrameAuditBudget {get;set;}
    public int DrawFrameAudits {get;private set;}
    public float MaximumDrawFrameAuditError {get;private set;}
    private bool TryDrawInfo(MultiMeshInstance3D draw,out DrawInfo info)
    {
        info=default;if(ReferenceEquals(draw,null))return false;
        // No await or node deletion occurs inside this update; a previously
        // validated managed reference avoids repeated native validity/ID calls.
        if(_insideFrame&&_frameDraws.TryGetValue(draw,out info))return true;
        if(!GodotObject.IsInstanceValid(draw))return false;
        var buffer=draw.Multimesh;if(buffer?.Mesh==null)return false;
        var global=draw.GlobalTransform;info=new(draw,buffer,global,global.AffineInverse(),buffer.Mesh.GetAabb(),buffer.InstanceCount);
        if(_insideFrame)_frameDraws[draw]=info;return true;
    }
    private const float ContactCell=.5f;
    private readonly Dictionary<(int,int),UInt128> _contactCells=new();
    private readonly Plant[] _orderedPlants=new Plant[128];
    private UInt128 _widePlants,_allPlants;
    private bool _contactIndexDirty=true;
    public long SpatialQueries {get;private set;}
    public long SpatialCandidates {get;private set;}
    public int SpatialAuditCases {get;private set;}
    public int SpatialAuditContactCases {get;private set;}
    public float MaximumSpatialAuditError {get;private set;}
    public FloraRenderer? Flora { get; set; }
    public Vector3 Focus { get; set; }
    public int ContactCount { get; private set; }
    public float MaximumBend { get; private set; }
    public int CachedPlantCount => _plants.Count;
    public override void _ExitTree()
    {
        _scan?.Dispose();
        foreach (var plant in _plants.Values)
            if (GodotObject.IsInstanceValid(plant.Draw) && plant.Draw.Multimesh != null && plant.Index < plant.Draw.Multimesh.InstanceCount
                && plant.Draw.Multimesh.GetInstanceTransform(plant.Index).IsEqualApprox(plant.Applied))
                plant.Draw.Multimesh.SetInstanceTransform(plant.Index, plant.Base);
    }
    public void Register(MultiMeshInstance3D draw,int index)
    {
        if(!TryDrawInfo(draw,out var info)||index>=info.Count)return;
        Register(draw,index,info.Buffer.GetInstanceTransform(index),info);
    }
    private void Register(MultiMeshInstance3D draw,int index,Transform3D transform,DrawInfo info)
    {
        var key=(draw.GetInstanceId(),index);
        if(!_plants.TryGetValue(key,out var plant))
        {
            if(_plants.Count>=128)
            {
                var incoming=(info.Global*transform).Origin.DistanceSquaredTo(Focus);
                (ulong,int) farthest=default;float distance=incoming;
                foreach(var (candidate,existing) in _plants)
                {
                    if(!TryDrawInfo(existing.Draw,out var existingInfo)){farthest=candidate;distance=float.PositiveInfinity;break;}
                    float d=(existingInfo.Global*existing.Base).Origin.DistanceSquaredTo(Focus);
                    if(d>distance){distance=d;farthest=candidate;}
                }
                if(distance<=incoming)return;
                var removed=_plants[farthest];
                if(TryDrawInfo(removed.Draw,out var removedInfo)&&removed.Index<removedInfo.Count&&removedInfo.Buffer.GetInstanceTransform(removed.Index).IsEqualApprox(removed.Applied))
                    removedInfo.Buffer.SetInstanceTransform(removed.Index,removed.Base);
                _plants.Remove(farthest);
            }
            _plants[key]=plant=new Plant{Draw=draw,Index=index,Base=transform,Applied=transform};
        }
        else if(!transform.IsEqualApprox(plant.Applied))plant.Base=transform;
        var world=info.Global*plant.Base;plant.Root=world.Origin;plant.Bounds=world*info.Bounds;_contactIndexDirty=true;
    }
    public override void _Process(double delta)
    {
        using var timing=Vivarium.Game.App.FrameProfiler.Measure("Guardian.Vegetation");
        ContactCount=0;MaximumBend=0;_frameDraws.Clear();_insideFrame=true;
        try
        {
            if(Flora!=null)
            {
                _scan??=Flora.GuardianContactInstances().GetEnumerator();
                for(int i=0;i<256;i++)
                {
                    if(!_scan.MoveNext()){_scan.Dispose();_scan=null;break;}
                    var (draw,index)=_scan.Current;
                    if(!TryDrawInfo(draw,out var info)||index>=info.Count)continue;
                    var transform=info.Buffer.GetInstanceTransform(index);var world=info.Global*transform;
                    if(world.Origin.DistanceSquaredTo(Focus)>16)continue;
                    var bounds=world*info.Bounds;if(bounds.Size.Y<.16f||bounds.Size.Y>2.5f)continue;
                    Register(draw,index,transform,info);
                }
            }
            var expired=new List<(ulong,int)>();
            foreach(var (key,plant) in _plants)
            {
                if(!TryDrawInfo(plant.Draw,out var info)||plant.Index>=info.Count){expired.Add(key);continue;}
                var current=info.Buffer.GetInstanceTransform(plant.Index);if(!current.IsEqualApprox(plant.Applied))plant.Base=current;
                float blend=1-MathF.Exp(-(float)Math.Clamp(delta,0,.1)*(plant.Target.LengthSquared()>0?14:4));
                plant.Bend=plant.Bend.Lerp(plant.Target,blend);
                if(plant.Target.LengthSquared()>0)ContactCount++;MaximumBend=Math.Max(MaximumBend,plant.Bend.Length());
                var root=info.Global*plant.Base;plant.Root=root.Origin;var axis=Vector3.Up.Cross(plant.Bend);
                var rotation=axis.LengthSquared()<1e-8f?Basis.Identity:new Basis(axis.Normalized(),plant.Bend.Length());
                root.Basis=rotation*root.Basis;plant.Applied=info.Inverse*root;info.Buffer.SetInstanceTransform(plant.Index,plant.Applied);
                plant.Bounds=root*info.Bounds;plant.Target=Vector3.Zero;
                if(root.Origin.DistanceSquaredTo(Focus)>25&&plant.Bend.LengthSquared()<1e-6f){info.Buffer.SetInstanceTransform(plant.Index,plant.Base);expired.Add(key);}
            }
            foreach(var key in expired)_plants.Remove(key);
        }
        finally{_insideFrame=false;_contactIndexDirty=true;}
        if(DrawFrameAuditBudget>0)
        {
            DrawFrameAuditBudget--;
            foreach(var pair in _frameDraws)
            {
                var expected=pair.Value;var actual=expected.Buffer;var global=expected.Draw.GlobalTransform;
                float error=global.Origin.DistanceTo(expected.Global.Origin)+global.Basis.X.DistanceTo(expected.Global.Basis.X)+global.Basis.Y.DistanceTo(expected.Global.Basis.Y)+global.Basis.Z.DistanceTo(expected.Global.Basis.Z);
                if(actual.InstanceCount!=expected.Count||actual.Mesh==null)error=float.PositiveInfinity;
                else{var box=actual.Mesh.GetAabb();error+=box.Position.DistanceTo(expected.Bounds.Position)+box.Size.DistanceTo(expected.Bounds.Size);}
                MaximumDrawFrameAuditError=Math.Max(MaximumDrawFrameAuditError,error);DrawFrameAudits++;
            }
        }
    }
    private void BuildContactIndex()
    {
        _contactCells.Clear();_widePlants=0;_allPlants=0;int index=0;
        // Bit order exactly matches the existing dictionary traversal. Querying
        // fewer bounds must not reorder sequential stem projections.
        foreach(var plant in _plants.Values)
        {
            _orderedPlants[index]=plant;UInt128 bit=(UInt128)1<<index++;_allPlants|=bit;
            var box=plant.Bounds;
            int x0=(int)MathF.Floor(box.Position.X/ContactCell),x1=(int)MathF.Floor(box.End.X/ContactCell);
            int z0=(int)MathF.Floor(box.Position.Z/ContactCell),z1=(int)MathF.Floor(box.End.Z/ContactCell);
            if((long)(x1-x0+1)*(z1-z0+1)>256){_widePlants|=bit;continue;}
            for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)_contactCells[(x,z)]=_contactCells.GetValueOrDefault((x,z))|bit;
        }
        _contactIndexDirty=false;
    }
    private UInt128 Nearby(Vector3 point,float radius)
    {
        if(radius>ContactCell*32)return _allPlants;
        float extent=radius+.0001f; // Conservative broadphase padding only; narrowphase is unchanged.
        int x0=(int)MathF.Floor((point.X-extent)/ContactCell),x1=(int)MathF.Floor((point.X+extent)/ContactCell);
        int z0=(int)MathF.Floor((point.Z-extent)/ContactCell),z1=(int)MathF.Floor((point.Z+extent)/ContactCell);
        UInt128 mask=_widePlants;
        for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)mask|=_contactCells.GetValueOrDefault((x,z));
        return mask;
    }
    private static Vector3 ResolvePlant(Plant plant,Vector3 point,float radius,bool flexible,bool cachedRoot=false)
    {
        var box=plant.Bounds;
        var closest=new Vector3(Math.Clamp(point.X,box.Position.X,box.End.X),Math.Clamp(point.Y,box.Position.Y,box.End.Y),Math.Clamp(point.Z,box.Position.Z,box.End.Z));
        var separation=point-closest;
        if(separation.LengthSquared()>=radius*radius)return point;
        var away=box.GetCenter()-point;away.Y=0;
        if(away.LengthSquared()<1e-6f)away=Vector3.Right;
        float overlap=radius-separation.Length();
        var requested=away.Normalized()*Math.Min(.38f,overlap*2.5f);
        if(requested.LengthSquared()>plant.Target.LengthSquared())plant.Target=requested;
        if(!flexible)return point;
        // Bounds and original root are sampled together when the visible instance
        // registry updates. Avoid another native transform read for every contact.
        var root=cachedRoot?plant.Root:(plant.Draw.GlobalTransform*plant.Base).Origin;
        var lateral=point-root;lateral.Y=0;
        float stemRadius=Math.Min(.045f,box.Size.X*.10f)+radius;
        if(point.Y<root.Y+box.Size.Y*.65f&&lateral.Length()<stemRadius)
        {
            if(lateral.LengthSquared()<1e-8f)lateral=-away.Normalized();
            point+=lateral.Normalized()*(stemRadius-lateral.Length());
        }
        return point;
    }
    private Vector3 ResolveAll(Vector3 point,float radius,bool flexible)
    {foreach(var plant in _plants.Values)point=ResolvePlant(plant,point,radius,flexible);return point;}
    public Vector3 Resolve(Vector3 point,float radius,bool flexible)
    {
        if(!point.IsFinite()||!float.IsFinite(radius)||radius<0||_plants.Count>128)return ResolveAll(point,radius,flexible);
        if(_contactIndexDirty)BuildContactIndex();
        SpatialQueries++;UInt128 candidates=Nearby(point,radius);
        while(candidates!=0)
        {
            ulong low=(ulong)candidates;
            int index=low!=0?System.Numerics.BitOperations.TrailingZeroCount(low):64+System.Numerics.BitOperations.TrailingZeroCount((ulong)(candidates>>64));
            candidates&=candidates-1;SpatialCandidates++;
            var before=point;point=ResolvePlant(_orderedPlants[index],point,radius,flexible,true);
            // A projection can enter another bound. Add only later plants:
            // earlier entries were already visited by the original full scan.
            if(point!=before&&index<127)candidates|=Nearby(point,radius)&(UInt128.MaxValue<<(index+1));
        }
        return point;
    }
    /// <summary>Native regression fixture covering all128 mask bits, dense sequential
    /// projections, transformed parents, dictionary slot reuse and wide bounds.</summary>
    public static (int Cases,int Contacts,int DrawAudits,float Error) RunBroadphaseSelfTest(Node owner)
    {
        var root=new Node3D{Visible=false,Name="GuardianContactSelfTest"};owner.AddChild(root);
        try
        {
            var buffer=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,Mesh=new BoxMesh{Size=new(.08f,.4f,.08f)},InstanceCount=129};
            var draw=new MultiMeshInstance3D{Multimesh=buffer};root.AddChild(draw);
            var contact=new GuardianVegetationContact();root.AddChild(contact);contact.SetProcess(false);
            for(int index=0;index<128;index++)buffer.SetInstanceTransform(index,new(Basis.Identity,new Vector3((index%8)*.055f,.2f,(index/8)*.055f)));
            void RegisterAll(){for(int index=0;index<128;index++)contact.Register(draw,index);}
            void Check()
            {
                contact.DrawFrameAuditBudget=1;contact._Process(1/60.0);contact.AuditSpatialQueries(128,true);
                if(!float.IsFinite(contact.MaximumSpatialAuditError)||contact.MaximumSpatialAuditError!=0||contact.MaximumDrawFrameAuditError!=0)
                    throw new InvalidOperationException("Guardian contact self-test differs from original scan/native draw properties");
            }
            RegisterAll();Check();
            draw.Transform=new(new Basis(Vector3.Up,.37f).Scaled(new Vector3(.8f,1.3f,1.15f)),new Vector3(1.25f,.5f,-.75f));RegisterAll();Check();
            for(int index=0;index<128;index+=3)contact._plants.Remove((draw.GetInstanceId(),index));
            for(int index=126;index>=0;index-=3)contact.Register(draw,index);contact._contactIndexDirty=true;Check();
            var wide=new MultiMeshInstance3D{Multimesh=new MultiMesh{TransformFormat=MultiMesh.TransformFormatEnum.Transform3D,Mesh=new BoxMesh{Size=new(12,.5f,12)},InstanceCount=1}};
            root.AddChild(wide);wide.Multimesh.SetInstanceTransform(0,new(Basis.Identity,new Vector3(0,.25f,0)));contact.Register(wide,0);Check();
            if(contact.SpatialAuditCases<4096||contact.SpatialAuditContactCases==0||contact.DrawFrameAudits<5)throw new InvalidOperationException("Guardian contact fixture did not cover expected cases");
            return(contact.SpatialAuditCases,contact.SpatialAuditContactCases,contact.DrawFrameAudits,contact.MaximumSpatialAuditError);
        }
        finally{root.QueueFree();}
    }

    /// <summary>Compare projected points and every bend target with the original
    /// full scan, then restore targets. No rendered or simulated plant is moved.</summary>
    public float AuditSpatialQueries(int maximumPlants=16,bool includeOuterPoints=false)
    {
        var plants=_plants.Values.ToArray();var before=new Vector3[plants.Length];var expected=new Vector3[plants.Length];
        for(int sample=0;sample<Math.Min(maximumPlants,plants.Length);sample++)
        {
            var plant=plants[sample];var root=(plant.Draw.GlobalTransform*plant.Base).Origin;
            var probes=includeOuterPoints?new[]{plant.Bounds.GetCenter(),root+Vector3.Up*.05f,root+new Vector3(2,.05f,2)}:new[]{plant.Bounds.GetCenter(),root+Vector3.Up*.05f};
            foreach(var point in probes)
            foreach(float radius in new[]{.03f,.10f})foreach(bool flexible in new[]{false,true})
            {
                for(int i=0;i<plants.Length;i++)before[i]=plants[i].Target;
                var reference=ResolveAll(point,radius,flexible);bool touched=false;
                for(int i=0;i<plants.Length;i++){expected[i]=plants[i].Target;touched|=expected[i]!=before[i];plants[i].Target=before[i];}
                var actual=Resolve(point,radius,flexible);float error=actual.DistanceTo(reference);
                for(int i=0;i<plants.Length;i++){error=Math.Max(error,plants[i].Target.DistanceTo(expected[i]));plants[i].Target=before[i];}
                MaximumSpatialAuditError=Math.Max(MaximumSpatialAuditError,error);SpatialAuditCases++;if(touched)SpatialAuditContactCases++;
            }
        }
        return MaximumSpatialAuditError;
    }

}

public partial class FloraRenderer
{
    internal IEnumerable<(MultiMeshInstance3D, int)> GuardianContactInstances()
    {
        // Snapshot draw nodes so an incremental scan survives population rebuilds.
        var draws = new List<MultiMeshInstance3D>();
        foreach (var layer in _layers.Values)
            foreach (var variant in layer.Variants)
            {
                if (variant?.Full != null) draws.Add(variant.Full);
                if (variant?.Juvenile != null) draws.Add(variant.Juvenile);
                if (variant?.Fruit != null) draws.Add(variant.Fruit);
            }
        foreach (var draw in draws)
        {
            if (!GodotObject.IsInstanceValid(draw) || draw.Multimesh == null) continue;
            int count = draw.Multimesh.VisibleInstanceCount < 0 ? draw.Multimesh.InstanceCount : draw.Multimesh.VisibleInstanceCount;
            for (int index = 0; index < count; index++) yield return (draw, index);
        }
    }
}
