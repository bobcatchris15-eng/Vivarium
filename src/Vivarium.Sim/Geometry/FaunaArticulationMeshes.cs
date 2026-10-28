using System.Runtime.CompilerServices;
using Vivarium.Sim.Core;
namespace Vivarium.Sim.Geometry;
public static partial class OrganismMeshes
{
    private sealed record FaunaJoint(int First, int Stride, int KneeRing, int EndRing);
    private static readonly ConditionalWeakTable<MeshData,List<FaunaJoint>> FaunaJoints = new();
    private static void RegisterFaunaJoint(MeshData mesh,int first,int sides,Vec3[] path)
    {
        var axis=(path[^1]-path[0]).Normalized();int knee=1;double best=-1;
        for(int k=1;k<path.Length-1;k++)
        {
            var v=path[k]-path[0];double distance=(v-axis*v.Dot(axis)).LengthSq;
            if(distance>best){best=distance;knee=k;}
        }
        // DetailedTube creates three Catmull-Rom stations per source segment;
        // Appendage inserts one flared socket ring ahead of those stations.
        FaunaJoints.GetOrCreateValue(mesh).Add(new FaunaJoint(first,Math.Max(6,sides)+1,1+3*knee,1+3*(path.Length-1)));
    }
    private static Vec3 FaunaOffset(MeshData m,int i)=>new(m.Colors[i*4],m.Colors[i*4+1],m.Colors[i*4+2]);
    // 1e-3 grid cell index for the joint attach lookup in FinalizeFaunaArticulation.
    private static long CellKey(double x)=>(long)Math.Floor(x*1000);
    private static Vec3 FaunaRingOffset(MeshData mesh,FaunaJoint joint,int ring)
    {
        Vec3 sum=Vec3.Zero;int sides=joint.Stride-1;
        for(int k=0;k<sides;k++)sum+=FaunaOffset(mesh,joint.First+ring*joint.Stride+k);
        return sum/sides;
    }
    // Called after inherited morphology: joint data is derived from the actual
    // transformed rings, so leg genes and whole-body support shifts cannot detach it.
    private static void FinalizeFaunaArticulation(MeshData mesh)
    {
        if(!FaunaJoints.TryGetValue(mesh,out var joints)||joints.Count==0)return;
        if(mesh.Custom0.Count!=0||mesh.Custom1.Count!=0)throw new InvalidOperationException("Fauna joint channels already occupied");
        for(int i=0;i<mesh.VertexCount*4;i++){mesh.Custom0.Add(0);mesh.Custom1.Add(0);}
        // Per-joint frames, kept in joint order. Degenerate joints are dropped exactly as before.
        var frames=new List<(Vec3 Attach,Vec3 Knee,Vec3 Ankle)>(joints.Count);
        foreach(var joint in joints)
        {
            Vec3 attach=mesh.Position(joint.First)-FaunaOffset(mesh,joint.First);
            Vec3 knee=FaunaRingOffset(mesh,joint,joint.KneeRing),ankle=FaunaRingOffset(mesh,joint,joint.EndRing);
            if(knee.Length<1e-6||(ankle-knee).Length<1e-6)continue;
            frames.Add((attach,knee,ankle));
        }
        // Spatial index on attach points (1e-3 cells). A match requires |root-attach| <= 1e-4 per
        // component, so every candidate lies in the 3x3x3 cell neighbourhood of the root.
        // Candidates are still filtered by the original exact tolerance test.
        var grid=new Dictionary<(long,long,long),List<int>>();
        for(int f=0;f<frames.Count;f++)
        {
            var a=frames[f].Attach;
            var key=(CellKey(a.X),CellKey(a.Y),CellKey(a.Z));
            if(!grid.TryGetValue(key,out var list))grid[key]=list=new List<int>();
            list.Add(f);
        }
        for(int i=0;i<mesh.VertexCount;i++)
        {
            if(frames.Count==0)break;
            if(mesh.Colors[i*4+3]<.5||mesh.UV2[i*2+1]>1.5)continue;
            Vec3 off=FaunaOffset(mesh,i),root=mesh.Position(i)-off;
            // Original semantics: every matching joint applies in joint order, so the last match
            // overwrites Custom0/Custom1; UV2 is raised if any joint matched.
            int winner=-1;
            long rx=CellKey(root.X),ry=CellKey(root.Y),rz=CellKey(root.Z);
            for(long dx=-1;dx<=1;dx++)
            for(long dy=-1;dy<=1;dy++)
            for(long dz=-1;dz<=1;dz++)
            {
                if(!grid.TryGetValue((rx+dx,ry+dy,rz+dz),out var list))continue;
                foreach(int f in list)
                {
                    if((root-frames[f].Attach).LengthSq>1e-8)continue;
                    if(f>winner)winner=f;
                }
            }
            if(winner<0)continue;
            var (_,knee,ankle)=frames[winner];
            // Anatomical chain coordinate; hard tissue and joint spheres use
            // the nearest bone, while small joint blends preserve the skin envelope.
            double u=off.Dot(knee)/knee.LengthSq;
            var tibia=ankle-knee;
            double v=(off-knee).Dot(tibia)/tibia.LengthSq;
            double d0=(off-knee*Math.Clamp(u,0,1)).LengthSq;
            double d1=(off-(knee+tibia*Math.Clamp(v,0,1))).LengthSq;
            double coordinate=d0<d1?Math.Clamp(u,0,1):1+Math.Clamp(v,0,1);
            if(v>1)coordinate=2+Math.Min(.99,(v-1)*4);
            int c=i*4;mesh.Custom1[c]=(float)knee.X;mesh.Custom1[c+1]=(float)knee.Y;mesh.Custom1[c+2]=(float)knee.Z;mesh.Custom1[c+3]=(float)coordinate;
            mesh.Custom0[c]=(float)ankle.X;mesh.Custom0[c+1]=(float)ankle.Y;mesh.Custom0[c+2]=(float)ankle.Z;mesh.Custom0[c+3]=1;
            // Joint spheres authored with body role now participate in their limb.
            if(mesh.UV2[i*2+1]<.5)mesh.UV2[i*2+1]=1.125f;
        }
        FaunaJoints.Remove(mesh);
    }
}
