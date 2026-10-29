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
        public long LastFloraUploadRevision = -1;
    }
    private readonly Dictionary<(ulong, int), Plant> _plants = new();
    private IEnumerator<(MultiMeshInstance3D, int)>? _scan;
    private readonly record struct DrawInfo(MultiMeshInstance3D Draw,MultiMesh Buffer,Transform3D Global,Transform3D Inverse,Aabb Bounds,int Count);
    private readonly Dictionary<MultiMeshInstance3D,DrawInfo> _frameDraws=new(ReferenceEqualityComparer.Instance);
    private bool _insideFrame;
    private bool _farthestValid;
    private (ulong,int) _farthestKey;
    private float _farthestDistance;
    private (ulong,int) _farthestSecondKey;
    private float _farthestSecondDistance;
    public long RegistryCacheQueries {get;private set;}
    public long RegistryCacheHits {get;private set;}
    public int RegistryCacheAudits {get;private set;}
    public int RegistryCacheAcceptedAudits {get;private set;}
    public int RegistryCacheRejectedAudits {get;private set;}
    public int RegistryCacheReuseAudits {get;private set;}
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
    /// <summary>Plants indexed by their stem rather than their whole bounds. The
    /// tail only ever collides with a stem, so the solver's inner loop can use
    /// this index instead of testing every plant whose bounds overlap it.</summary>
    private readonly Dictionary<(int,int),UInt128> _stemCells=new();
    /// <summary>Half-width of the stem index margin: the largest stem radius plus the largest contact radius.</summary>
    private const float StemMargin=.25f;
    /// <summary>How far the stem index has to reach: the widest stem plus the
    /// caller's contact radius. The margin follows the caller's own reach
    /// instead of a fixed guess, so a narrow tail searches a small area.</summary>
    public float ContactReach { get; set; } = .2f;
    private float StemExtent => Math.Min(StemMargin, .045f + Math.Max(0f, ContactReach));
    /// <summary>Cell size of the stem index. Finer than the bounds index
    /// because only the plants whose stem stands within reach of the tail can
    /// push it, and their roots are often clustered within centimetres.</summary>
    private const float StemCell=.125f;
    private readonly Plant[] _orderedPlants=new Plant[128];
    private readonly Aabb[] _orderedBounds=new Aabb[128];
    /// <summary>Flat copies of the stem data. Reading a class field per
    /// candidate chased a pointer into scattered heap objects; these keep the
    /// per-candidate test in cache, and the three vectors share one record so a
    /// candidate touches a single line.</summary>
    private struct StemData
    {
        public Vector3 Root;
        public Vector3 Size;
        public Vector3 Center;
    }
    private readonly StemData[] _orderedStems=new StemData[128];
    private UInt128 _widePlants,_allPlants;
    private bool _contactIndexDirty=true;
    public long SpatialQueries {get;private set;}
    public long SpatialCandidates {get;private set;}
    public long SpatialBroadphase {get;private set;}
    public long SpatialFullScans {get;private set;}
    public long SpatialProjected {get;private set;}
    public double SpatialBroadphaseMs {get;private set;}
    public double SpatialNarrowMs {get;private set;}
    public long SpatialPlantTests {get;private set;}
    public long SpatialCellLookups {get;private set;}
    public long SpatialMaxSpan {get;private set;}
    public long SpatialIndexBuilds {get;private set;}
    public long SpatialIndexCells {get;private set;}
    public long SpatialNarrowCalls {get;private set;}
    /// <summary>Audit seam: compares the stem projection with the original full scan.</summary>
    public int StemAuditBudget {get;set;}
    public int StemAudits {get;private set;}
    public float MaximumStemAuditError {get;private set;}
    public int SpatialAuditCases {get;private set;}
    public int SpatialAuditContactCases {get;private set;}
    public float MaximumSpatialAuditError {get;private set;}
    public FloraRenderer? Flora { get; set; }
    public Vector3 Focus { get; set; }
    public int ContactCount { get; private set; }
    public float MaximumBend { get; private set; }
    public int CachedPlantCount => _plants.Count;
    public long CpuBaseTransformReads { get; private set; }
    public long NativeTransformFallbackReads { get; private set; }
    public int CpuVsGpuAuditChecks { get; private set; }
    public long AvoidedPoseUploads { get; private set; }
    public long ActualPoseUploads { get; private set; }
    public long SourcePoseReapplies { get; private set; }
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
    /// <summary>Enables the stem-projection audit for the next N projections.</summary>
    public void BeginStemAudit(int budget)
    {
        StemAuditBudget=budget;
    }
    private void Register(MultiMeshInstance3D draw,int index,Transform3D transform,DrawInfo info)
    {
        var key=(draw.GetInstanceId(),index);
        if(!_plants.TryGetValue(key,out var plant))
        {
            if(_plants.Count>=128)
            {
                var incoming=(info.Global*transform).Origin.DistanceSquaredTo(Focus);
                RegistryCacheQueries++;
                (ulong,int) farthest;float distance;(ulong,int) second;float secondDistance;bool reused=false;
                if(!_insideFrame || !float.IsFinite(incoming))
                    (farthest,distance,second,secondDistance)=FindFarthest(incoming);
                else
                {
                    if(!_farthestValid)
                    {
                        (_farthestKey,_farthestDistance,_farthestSecondKey,_farthestSecondDistance)=FindFarthest(float.NegativeInfinity);
                        _farthestValid=true;
                    }
                    else {RegistryCacheHits++;reused=true;}
                    farthest=_farthestKey;distance=_farthestDistance;
                    second=_farthestSecondKey;secondDistance=_farthestSecondDistance;
                }
                bool rejected=distance<=incoming;
                if((rejected?RegistryCacheRejectedAudits:RegistryCacheAcceptedAudits)<32 && Array.IndexOf(Vivarium.Game.App.Main.UserArgs,"--guardian-registry-cache-audit")>=0)
                {
                    var reference=FindFarthest(incoming);
                    bool referenceRejected=reference.Distance<=incoming;
                    if(rejected!=referenceRejected || (!rejected && farthest!=reference.Key))
                        throw new InvalidOperationException("Guardian registry farthest cache decision mismatch");
                    RegistryCacheAudits++;
                    if(reused)RegistryCacheReuseAudits++;
                    if(rejected)RegistryCacheRejectedAudits++;else RegistryCacheAcceptedAudits++;
                }
                if(distance<=incoming)return;
                var removed=_plants[farthest];
                if(TryDrawInfo(removed.Draw,out var removedInfo)&&removed.Index<removedInfo.Count&&removedInfo.Buffer.GetInstanceTransform(removed.Index).IsEqualApprox(removed.Applied))
                    removedInfo.Buffer.SetInstanceTransform(removed.Index,removed.Base);
                _plants.Remove(farthest);
                // The runner-up is now the farthest of the remaining plants, so
                // the registry does not have to be scanned again for the next
                // registration. A registry that held a single plant has no
                // runner-up, and the next registration recomputes it.
                if(secondDistance>=0 && _plants.ContainsKey(second))
                {
                    _farthestKey=second;_farthestDistance=secondDistance;_farthestValid=true;
                }
                else _farthestValid=false;
            }
            _plants[key]=plant=new Plant{Draw=draw,Index=index,Base=transform,Applied=transform};
            if(_farthestValid)
            {
                // The set grew: the new plant only becomes the farthest if it is
                // further from the focus than the current one.
                float d=(info.Global*transform).Origin.DistanceSquaredTo(Focus);
                if(d>_farthestDistance){_farthestKey=key;_farthestDistance=d;}
            }
        }
        else if(!transform.IsEqualApprox(plant.Applied))
        {
            plant.Base=transform;
            // A plant that moved only changes the farthest plant if it is now
            // further from the focus than the current one. Rescanning the
            // registry for every moved plant cost a scan per registration.
            if(_farthestValid)
            {
                float d=(info.Global*transform).Origin.DistanceSquaredTo(Focus);
                if(key==_farthestKey)_farthestDistance=d;
                else if(d>_farthestDistance){_farthestKey=key;_farthestDistance=d;}
            }
        }
        var world=info.Global*plant.Base;plant.Root=world.Origin;plant.Bounds=world*info.Bounds;_contactIndexDirty=true;
    }
    private ((ulong,int) Key,float Distance,(ulong,int) SecondKey,float SecondDistance) FindFarthest(float incoming)
    {
        // Strict > retains the first dictionary entry on a distance tie. An
        // invalid draw still wins immediately, exactly as the original scan.
        // The runner-up is tracked as well: evicting the farthest leaves it as
        // the new farthest, which saves rescanning the registry.
        (ulong,int) farthest=default;float distance=incoming;
        (ulong,int) second=default;float secondDistance=incoming;
        foreach(var (candidate,existing) in _plants)
        {
            if(!TryDrawInfo(existing.Draw,out var info))return(candidate,float.PositiveInfinity,default,float.PositiveInfinity);
            float d=(info.Global*existing.Base).Origin.DistanceSquaredTo(Focus);
            if(d>distance){second=farthest;secondDistance=distance;farthest=candidate;distance=d;}
            else if(d>secondDistance){second=candidate;secondDistance=d;}
        }
        return(farthest,distance,second,secondDistance);
    }
    public override void _Process(double delta)
    {
        using var timing=Vivarium.Game.App.FrameProfiler.Measure("Guardian.Vegetation");
        ContactCount=0;MaximumBend=0;_frameDraws.Clear();_farthestValid=false;_insideFrame=true;
        try
        {
            using (Vivarium.Game.App.FrameProfiler.Measure("Guardian.Vegetation.Scan"))
            if(Flora!=null)
            {
                _scan??=Flora.GuardianContactInstancesNear(Focus,4f).GetEnumerator();
                for(int i=0;i<24;i++)
                {
                    if(!_scan.MoveNext()){_scan.Dispose();_scan=null;break;}
                    var (draw,index)=_scan.Current;
                    if(!TryDrawInfo(draw,out var info)||index>=info.Count)continue;
                    // Flora already retains the exact instance transforms in
                    // its upload buffer. Avoid native per-instance readback,
                    // which can synchronise with rendering and stall a frame.
                    Transform3D transform;
                    if(Flora.TryGuardianBaseTransform(draw,index,out var baseTransform))
                    {
                        transform=baseTransform; CpuBaseTransformReads++;
                        if(CpuVsGpuAuditChecks<96 &&
                            Vivarium.Game.App.Main.UserArgs.Contains("--guardian-cpu-cache-audit") &&
                            !_plants.ContainsKey((draw.GetInstanceId(),index)))
                        {
                            var native=info.Buffer.GetInstanceTransform(index);
                            if(!native.IsEqualApprox(baseTransform))
                                throw new InvalidOperationException(
                                    $"Guardian flora CPU/GPU transform mismatch: {draw.Name} instance {index}");
                            CpuVsGpuAuditChecks++;
                        }
                    }
                    else
                    {
                        transform=info.Buffer.GetInstanceTransform(index);
                        NativeTransformFallbackReads++;
                    }
                    var world=info.Global*transform;
                    if(world.Origin.DistanceSquaredTo(Focus)>16)continue;
                    var bounds=world*info.Bounds;if(bounds.Size.Y<.16f||bounds.Size.Y>2.5f)continue;
                    Register(draw,index,transform,info);
                }
            }
            using var applyTiming=Vivarium.Game.App.FrameProfiler.Measure("Guardian.Vegetation.Apply");
            var expired=new List<(ulong,int)>();
            foreach(var (key,plant) in _plants)
            {
                if(!TryDrawInfo(plant.Draw,out var info)||plant.Index>=info.Count){expired.Add(key);continue;}
                if(plant.Target.LengthSquared()<1e-8f && plant.Bend.LengthSquared()<1e-8f && plant.Applied.IsEqualApprox(plant.Base))
                {
                    AvoidedPoseUploads++;
                    if(plant.Root.DistanceSquaredTo(Focus)>25)
                    {
                        expired.Add(key);
                    }
                    continue;
                }
                // The flora CPU upload cache is authoritative for undeformed
                // plant placement. A native read is only needed for atypical
                // buffers not managed by the flora renderer.
                Transform3D stored=default;
                long uploadRevision=-1;
                bool fromFlora=Flora!=null && Flora.TryGuardianBaseTransform(
                    plant.Draw,plant.Index,out stored,out uploadRevision);
                var current=fromFlora?stored:info.Buffer.GetInstanceTransform(plant.Index);
                if(!current.IsEqualApprox(plant.Applied)){plant.Base=current;_farthestValid=false;}
                float blend=1-MathF.Exp(-(float)Math.Clamp(delta,0,.1)*(plant.Target.LengthSquared()>0?14:4));
                plant.Bend=plant.Bend.Lerp(plant.Target,blend);
                if(plant.Target.LengthSquared()>0)ContactCount++;MaximumBend=Math.Max(MaximumBend,plant.Bend.Length());
                var root=info.Global*plant.Base;plant.Root=root.Origin;var axis=Vector3.Up.Cross(plant.Bend);
                var rotation=axis.LengthSquared()<1e-8f?Basis.Identity:new Basis(axis.Normalized(),plant.Bend.Length());
                root.Basis=rotation*root.Basis;
                var previous=plant.Applied;
                plant.Applied=info.Inverse*root;
                // Flora uploads the entire packed MultiMesh buffer periodically,
                // resetting previous individual bends even when the intended
                // target pose is identical. Upload revision is the precise
                // invalidation token; reapply once for each newer flora upload.
                // When flora has just reset an instance to its undeformed
                // base, there is nothing to reapply for a resting plant.
                // Only a genuinely bent plant needs its individual override
                // reinstated after an upload revision change.
                bool sourceReset=fromFlora &&
                    plant.LastFloraUploadRevision!=uploadRevision &&
                    !plant.Applied.IsEqualApprox(current);
                if(sourceReset||!plant.Applied.IsEqualApprox(previous))
                {
                    info.Buffer.SetInstanceTransform(plant.Index,plant.Applied);
                    ActualPoseUploads++;
                    if(sourceReset)SourcePoseReapplies++;
                }
                else AvoidedPoseUploads++;
                if(fromFlora)plant.LastFloraUploadRevision=uploadRevision;
                plant.Bounds=root*info.Bounds;plant.Target=Vector3.Zero;
                if(root.Origin.DistanceSquaredTo(Focus)>25&&plant.Bend.LengthSquared()<1e-6f){info.Buffer.SetInstanceTransform(plant.Index,plant.Base);expired.Add(key);}
            }
            foreach(var key in expired)_plants.Remove(key);
        }
        finally{_insideFrame=false;_farthestValid=false;_contactIndexDirty=true;}
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
        SpatialIndexBuilds++;
        _contactCells.Clear();_stemCells.Clear();_widePlants=0;_allPlants=0;int index=0;
        // Bit order exactly matches the existing dictionary traversal. Querying
        // fewer bounds must not reorder sequential stem projections.
        foreach(var plant in _plants.Values)
        {
            _orderedPlants[index]=plant;UInt128 bit=(UInt128)1<<index++;_allPlants|=bit;
            // Flat copy of the contact bounds. The broadphase rejects almost every
            // candidate, and reading a class field per candidate chased a pointer
            // into scattered heap objects. The flat array keeps that test in cache.
            _orderedBounds[index-1]=plant.Bounds;
            _orderedStems[index-1]=new StemData{Root=plant.Root,Size=plant.Bounds.Size,Center=plant.Bounds.GetCenter()};
            var box=plant.Bounds;
            int x0=(int)MathF.Floor(box.Position.X/ContactCell),x1=(int)MathF.Floor(box.End.X/ContactCell);
            int z0=(int)MathF.Floor(box.Position.Z/ContactCell),z1=(int)MathF.Floor(box.End.Z/ContactCell);
            if((long)(x1-x0+1)*(z1-z0+1)>256){_widePlants|=bit;continue;}
            for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)_contactCells[(x,z)]=_contactCells.GetValueOrDefault((x,z))|bit;
            SpatialIndexCells+=(long)(x1-x0+1)*(z1-z0+1);
            // The stem stands at the plant's root, not at the centre of its
            // bounds, so it gets its own index.
            var root=plant.Root;
            float stemExtent=StemExtent;
            int sx0=(int)MathF.Floor((root.X-stemExtent)/StemCell),sx1=(int)MathF.Floor((root.X+stemExtent)/StemCell);
            int sz0=(int)MathF.Floor((root.Z-stemExtent)/StemCell),sz1=(int)MathF.Floor((root.Z+stemExtent)/StemCell);
            for(int z=sz0;z<=sz1;z++)for(int x=sx0;x<=sx1;x++)_stemCells[(x,z)]=_stemCells.GetValueOrDefault((x,z))|bit;
        }
        _contactIndexDirty=false;
    }

    /// <summary>Moves a point out of the stems it overlaps, without touching the
    /// plants' bend targets. The solver calls this for every constraint
    /// iteration; only a handful of plants have a stem near the tail, so this
    /// index keeps the inner loop proportional to the plants actually in reach
    /// rather than to the plants whose bounds happen to overlap the point.</summary>
    public Vector3 ProjectStem(Vector3 point,float radius)
    {
        if(!point.IsFinite()||!float.IsFinite(radius)||radius<0||_plants.Count>128)return point;
        if(_contactIndexDirty)BuildContactIndex();
        SpatialQueries++;
        // Audit: the original full scan is the reference for the projection.
        Vector3? reference=null;
        if(StemAuditBudget>0){StemAuditBudget--;reference=ResolveAll(point,radius,true);}
        float extent=StemExtent;
        int x0=(int)MathF.Floor((point.X-extent)/StemCell),x1=(int)MathF.Floor((point.X+extent)/StemCell);
        int z0=(int)MathF.Floor((point.Z-extent)/StemCell),z1=(int)MathF.Floor((point.Z+extent)/StemCell);
        UInt128 candidates=0;
        for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)candidates|=_stemCells.GetValueOrDefault((x,z));
        while(candidates!=0)
        {
            ulong low=(ulong)candidates;
            int index=low!=0?System.Numerics.BitOperations.TrailingZeroCount(low):64+System.Numerics.BitOperations.TrailingZeroCount((ulong)(candidates>>64));
            candidates&=candidates-1;SpatialCandidates++;SpatialPlantTests++;
            var stem=_orderedStems[index];
            var root=stem.Root;
            var lateral=point-root;lateral.Y=0;
            float stemRadius=Math.Min(.045f,stem.Size.X*.10f)+radius;
            if(lateral.LengthSquared()>=stemRadius*stemRadius)continue;
            if(point.Y>=root.Y+stem.Size.Y*.65f)continue;
            var away=stem.Center-point;away.Y=0;
            if(away.LengthSquared()<1e-6f)away=Vector3.Right;
            if(lateral.LengthSquared()<1e-8f)lateral=-away.Normalized();
            float lateralLength=lateral.Length();
            point+=lateral.Normalized()*(stemRadius-lateralLength);
            SpatialProjected++;
        }
        if(reference is { } expected)
        {
            float error=point.DistanceTo(expected);
            MaximumStemAuditError=Math.Max(MaximumStemAuditError,error);
            StemAudits++;
            if(!float.IsFinite(error)||error>1e-5f)
                throw new InvalidOperationException($"Guardian stem projection differs from the full scan by {error:G6}m");
        }
        return point;
    }
    private UInt128 Nearby(Vector3 point,float radius)
    {
        SpatialBroadphase++;
        if(radius>ContactCell*32)return _allPlants;
        float extent=radius+.0001f; // Conservative broadphase padding only; narrowphase is unchanged.
        int x0=(int)MathF.Floor((point.X-extent)/ContactCell),x1=(int)MathF.Floor((point.X+extent)/ContactCell);
        int z0=(int)MathF.Floor((point.Z-extent)/ContactCell),z1=(int)MathF.Floor((point.Z+extent)/ContactCell);
        UInt128 mask=_widePlants;
        int cells=0;
        for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++){mask|=_contactCells.GetValueOrDefault((x,z));cells++;}
        SpatialCellLookups+=cells;
        long span=Math.Max((long)x1-x0+1,(long)z1-z0+1);
        if(span>SpatialMaxSpan)SpatialMaxSpan=span;
        return mask;
    }
    private static Vector3 ResolvePlant(Plant plant,Vector3 point,float radius,bool flexible,bool cachedRoot=false)
    {
        var box=plant.Bounds;
        var closest=new Vector3(Math.Clamp(point.X,box.Position.X,box.End.X),Math.Clamp(point.Y,box.Position.Y,box.End.Y),Math.Clamp(point.Z,box.Position.Z,box.End.Z));
        var separation=point-closest;
        if(separation.LengthSquared()>=radius*radius)return point;

        var center=box.GetCenter();
        float halfX=Math.Max(0.001f,box.Size.X*0.5f);
        float halfZ=Math.Max(0.001f,box.Size.Z*0.5f);
        float height=Math.Max(0.001f,box.Size.Y);

        // Botanical vase profile: narrow stem clearance at ground, expanding to full canopy
        float u=Math.Clamp((point.Y-box.Position.Y)/height,0f,1f);
        float profile=0.22f+0.78f*MathF.Sqrt(u);
        float plantRadiusX=halfX*profile;
        float plantRadiusZ=halfZ*profile;

        float dx=point.X-center.X;
        float dz=point.Z-center.Z;
        float normDist=MathF.Sqrt((dx*dx)/(plantRadiusX*plantRadiusX)+(dz*dz)/(plantRadiusZ*plantRadiusZ));
        float effectiveRadius=0.5f*(plantRadiusX+plantRadiusZ);
        float overlap=radius+(1.0f-normDist)*effectiveRadius;

        var away=center-point;away.Y=0;
        if(away.LengthSquared()<1e-6f)away=Vector3.Right;

        if(overlap>0)
        {
            var requested=away.Normalized()*Math.Min(.38f,overlap*2.5f);
            if(requested.LengthSquared()>plant.Target.LengthSquared())plant.Target=requested;
        }

        if(!flexible)return point;
        // Bounds and original root are sampled together when the visible instance
        // registry updates. Avoid another native transform read for every contact.
        var root=cachedRoot?plant.Root:(plant.Draw.GlobalTransform*plant.Base).Origin;
        return PushOutOfStem(plant,box,point,radius,root,away);
    }

    /// <summary>Resolves vegetation contact along a connected capsule segment between points a and b.
    /// Finds the closest point on the segment to each nearby plant's canopy center and evaluates contact.</summary>
    public void ResolveCapsule(Vector3 a, Vector3 b, float radius)
    {
        if(!a.IsFinite()||!b.IsFinite()||!float.IsFinite(radius)||radius<=0||_plants.Count==0)return;
        if(_contactIndexDirty)BuildContactIndex();

        var mid=(a+b)*0.5f;
        float segHalfLen=(b-a).Length()*0.5f;
        float broadRadius=segHalfLen+radius;
        if(broadRadius>ContactCell*32)
        {
            foreach(var plant in _plants.Values)
                ResolvePlantOnSegment(plant,a,b,radius);
            return;
        }

        UInt128 candidates=Nearby(mid,broadRadius);
        while(candidates!=0)
        {
            ulong low=(ulong)candidates;
            int index=low!=0?System.Numerics.BitOperations.TrailingZeroCount(low):64+System.Numerics.BitOperations.TrailingZeroCount((ulong)(candidates>>64));
            candidates&=candidates-1;
            var flat=_orderedBounds[index];
            var segMin=new Vector3(Math.Min(a.X,b.X)-radius,Math.Min(a.Y,b.Y)-radius,Math.Min(a.Z,b.Z)-radius);
            var segMax=new Vector3(Math.Max(a.X,b.X)+radius,Math.Max(a.Y,b.Y)+radius,Math.Max(a.Z,b.Z)+radius);
            if(segMax.X<flat.Position.X||segMin.X>flat.End.X||
               segMax.Y<flat.Position.Y||segMin.Y>flat.End.Y||
               segMax.Z<flat.Position.Z||segMin.Z>flat.End.Z)
                continue;

            ResolvePlantOnSegment(_orderedPlants[index],a,b,radius);
        }
    }

    private static void ResolvePlantOnSegment(Plant plant,Vector3 a,Vector3 b,float radius)
    {
        var center=plant.Bounds.GetCenter();
        var ab=b-a;
        float abLenSq=ab.LengthSquared();
        float t=0f;
        if(abLenSq>1e-8f)
        {
            t=Math.Clamp(((center.X-a.X)*ab.X+(center.Y-a.Y)*ab.Y+(center.Z-a.Z)*ab.Z)/abLenSq,0f,1f);
        }
        var closestOnSegment=a+ab*t;
        ResolvePlant(plant,closestOnSegment,radius,false,true);
    }

    /// <summary>Pushes a point out of a plant's stem. This is the only part of the
    /// contact that moves the tail; the plant's bend target is a separate effect.</summary>
    private static Vector3 PushOutOfStem(Plant plant,Aabb box,Vector3 point,float radius,Vector3 root,Vector3 away)
    {
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
        if(!point.IsFinite()||!float.IsFinite(radius)||radius<0||_plants.Count>128){SpatialFullScans++;return ResolveAll(point,radius,flexible);}
        if(_contactIndexDirty)BuildContactIndex();
        SpatialQueries++;
        long narrowStart=System.Diagnostics.Stopwatch.GetTimestamp();
        UInt128 candidates=Nearby(point,radius);
        long afterBroad=System.Diagnostics.Stopwatch.GetTimestamp();
        SpatialBroadphaseMs+=System.Diagnostics.Stopwatch.GetElapsedTime(narrowStart,afterBroad).TotalMilliseconds;
        while(candidates!=0)
        {
            ulong low=(ulong)candidates;
            int index=low!=0?System.Numerics.BitOperations.TrailingZeroCount(low):64+System.Numerics.BitOperations.TrailingZeroCount((ulong)(candidates>>64));
            candidates&=candidates-1;SpatialCandidates++;SpatialPlantTests++;
            // Fast rejection against the flat bounds copy. Only a plant whose box
            // is genuinely closer than the contact radius can project the point,
            // so the class instance is touched for those alone.
            var flat=_orderedBounds[index];
            var flatClosest=new Vector3(Math.Clamp(point.X,flat.Position.X,flat.End.X),Math.Clamp(point.Y,flat.Position.Y,flat.End.Y),Math.Clamp(point.Z,flat.Position.Z,flat.End.Z));
            var flatSeparation=point-flatClosest;
            var before=point;
            if(flatSeparation.LengthSquared()<radius*radius){SpatialNarrowCalls++;point=ResolvePlant(_orderedPlants[index],point,radius,flexible,true);}
            if(point!=before)SpatialProjected++;
            // A projection can enter another bound. Add only later plants:
            // earlier entries were already visited by the original full scan.
            if(point!=before&&index<127)candidates|=Nearby(point,radius)&(UInt128.MaxValue<<(index+1));
        }
        SpatialNarrowMs+=System.Diagnostics.Stopwatch.GetElapsedTime(afterBroad).TotalMilliseconds;
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
    /// <summary>Read the exact CPU data submitted to the flora MultiMesh GPU.
    /// Guardian bending only overrides individual GPU transforms; the packed
    /// flora buffer remains the stable undeformed plant placement.</summary>
    internal bool TryGuardianBaseTransform(MultiMeshInstance3D draw,int index,out Transform3D transform)
        => TryGuardianBaseTransform(draw,index,out transform,out _);

    internal bool TryGuardianBaseTransform(MultiMeshInstance3D draw,int index,out Transform3D transform,
        out long uploadRevision)
    {
        transform=Transform3D.Identity;
        uploadRevision=-1;
        var multimesh=draw.Multimesh;
        if(multimesh==null||index<0||
            !_pooledBuffers.TryGetValue(multimesh,out var pool)||
            index>=pool.VisibleCount)return false;
        var buffer=pool.Buffer;
        uploadRevision=pool.UploadRevision;
        int o=index*20;
        if(o+11>=buffer.Length)return false;
        transform=new Transform3D(
            new Basis(
                new Vector3(buffer[o],buffer[o+4],buffer[o+8]),
                new Vector3(buffer[o+1],buffer[o+5],buffer[o+9]),
                new Vector3(buffer[o+2],buffer[o+6],buffer[o+10])),
            new Vector3(buffer[o+3],buffer[o+7],buffer[o+11]));
        return true;
    }

    private sealed class GuardianBin
    {
        public long Revision = -1;
        public int Count = -1;
        public Transform3D Global;
        public readonly Dictionary<(int, int), List<int>> Cells = new();
    }
    private const float GuardianBinCell = 2f;
    private readonly Dictionary<MultiMesh, GuardianBin> _guardianBins = new(ReferenceEqualityComparer.Instance);

    /// <summary>Instances whose origin may lie within <paramref name="radius"/> of <paramref name="focus"/>.
    /// Binned in 2 m world cells from the packed upload buffer (rebuilt only when that draw is re-uploaded),
    /// so cost is O(nearby). Callers still apply the exact distance test. Draws not managed by the
    /// pooled buffers fall back to a full walk.</summary>
    internal IEnumerable<(MultiMeshInstance3D, int)> GuardianContactInstancesNear(Vector3 focus, float radius)
    {
        var draws = new List<MultiMeshInstance3D>();
        foreach (var layer in _layers.Values)
            foreach (var variant in layer.Variants)
            {
                if (variant?.Full != null) draws.Add(variant.Full);
                if (variant?.Juvenile != null) draws.Add(variant.Juvenile);
                if (variant?.Fruit != null) draws.Add(variant.Fruit);
            }
        if (_guardianBins.Count > _pooledBuffers.Count * 2 + 16) _guardianBins.Clear();
        int cx0 = (int)MathF.Floor((focus.X - radius) / GuardianBinCell), cx1 = (int)MathF.Floor((focus.X + radius) / GuardianBinCell);
        int cz0 = (int)MathF.Floor((focus.Z - radius) / GuardianBinCell), cz1 = (int)MathF.Floor((focus.Z + radius) / GuardianBinCell);
        foreach (var draw in draws)
        {
            if (!GodotObject.IsInstanceValid(draw) || draw.Multimesh == null) continue;
            var mm = draw.Multimesh;
            if (!_pooledBuffers.TryGetValue(mm, out var pool))
            {
                int count = mm.VisibleInstanceCount < 0 ? mm.InstanceCount : mm.VisibleInstanceCount;
                for (int index = 0; index < count; index++) yield return (draw, index);
                continue;
            }
            var global = draw.GlobalTransform;
            if (!_guardianBins.TryGetValue(mm, out var bin)) _guardianBins[mm] = bin = new GuardianBin();
            if (bin.Revision != pool.UploadRevision || bin.Count != pool.VisibleCount || !bin.Global.IsEqualApprox(global))
            {
                bin.Cells.Clear();
                var buffer = pool.Buffer;
                int n = Math.Min(pool.VisibleCount, buffer.Length / 20);
                for (int i = 0; i < n; i++)
                {
                    int o = i * 20;
                    var world = global * new Vector3(buffer[o + 3], buffer[o + 7], buffer[o + 11]);
                    var key = ((int)MathF.Floor(world.X / GuardianBinCell), (int)MathF.Floor(world.Z / GuardianBinCell));
                    if (!bin.Cells.TryGetValue(key, out var list)) bin.Cells[key] = list = new List<int>();
                    list.Add(i);
                }
                bin.Revision = pool.UploadRevision; bin.Count = pool.VisibleCount; bin.Global = global;
            }
            for (int z = cz0; z <= cz1; z++)
                for (int x = cx0; x <= cx1; x++)
                    if (bin.Cells.TryGetValue((x, z), out var cell))
                        for (int k = 0; k < cell.Count; k++) yield return (draw, cell[k]);
        }
    }

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
