using Vivarium.Sim.Core;
using Vivarium.Sim.Water;
using Vivarium.Sim.World;
namespace Vivarium.Sim.Geometry;

public sealed record SpringWaterMeshResult(MeshData Mesh, int WetNodes);
/// <summary>Pure sparse surface reconstruction. Safe on a worker with immutable particle/ground inputs.</summary>
public static class SpringWaterMesh
{
    private const double Cell=0.012, Support=0.045;
    private const float Contour=0.12f;
    private readonly record struct Sample(double X, double Z, double Density, double Height, double Fx, double Fz, double Nx = 0, double Nz = 0);
    public static SpringWaterMeshResult Build(IReadOnlyList<FluidParticle> particles, SpringFluidGroundSnapshot ground, HexDomain domain)
    {
        if (particles.Count == 0) return new(new MeshData(), 0);
        double minX = double.MaxValue, minZ = double.MaxValue, maxX = double.MinValue, maxZ = double.MinValue;
        foreach (var p in particles) {
            minX = Math.Min(minX,p.Position.X); maxX = Math.Max(maxX,p.Position.X);
            minZ = Math.Min(minZ,p.Position.Z); maxZ = Math.Max(maxZ,p.Position.Z);
        }
        double ox = Math.Floor((minX-Support)/Cell)*Cell, oz = Math.Floor((minZ-Support)/Cell)*Cell;
        int nx = (int)Math.Ceiling((maxX+Support-ox)/Cell)+1, nz = (int)Math.Ceiling((maxZ+Support-oz)/Cell)+1;
        // Sparse work scales with wetted nodes; only guard against integer index overflow.
        if ((long)nx*nz > int.MaxValue - 2L*nx) return new(new MeshData(), 0);
        var nodes = new Dictionary<int, (double Density, double Fx, double Fz)>();
        foreach (var p in particles) {
            int x0 = Math.Max(0,(int)Math.Floor((p.Position.X-Support-ox)/Cell));
            int x1 = Math.Min(nx-1,(int)Math.Ceiling((p.Position.X+Support-ox)/Cell));
            int z0 = Math.Max(0,(int)Math.Floor((p.Position.Z-Support-oz)/Cell));
            int z1 = Math.Min(nz-1,(int)Math.Ceiling((p.Position.Z+Support-oz)/Cell));
            for(int z=z0;z<=z1;z++) for(int x=x0;x<=x1;x++) {
                double dx=ox+x*Cell-p.Position.X,dz=oz+z*Cell-p.Position.Z;
                double q=1-(dx*dx+dz*dz)/(Support*Support);
                if(q<=0)continue;
                double weight=q*q*q; int k=z*nx+x;
                var a=nodes.GetValueOrDefault(k);
                nodes[k]=(a.Density+weight,a.Fx+p.Velocity.X*weight,a.Fz+p.Velocity.Z*weight);
            }
        }
        var samples = new Dictionary<int, Sample>(nodes.Count);
        int wetNodes=0;
        var cells=new HashSet<int>();
        foreach(var pair in nodes)
        {
            int k=pair.Key,x=k%nx,z=k/nx;
            var a=pair.Value;
            double d=a.Density,wx=ox+x*Cell,wz=oz+z*Cell;
            if(!domain.Contains(new Vec2(wx,wz)))d=0;
            if(d>Contour)wetNodes++;
            samples[k]=MakeSample(x,z,d,a.Fx/a.Density,a.Fz/a.Density);
            for(int dz=-1;dz<=0;dz++)for(int dx=-1;dx<=0;dx++)
                if(x+dx>=0&&x+dx<nx-1&&z+dz>=0&&z+dz<nz-1)cells.Add((z+dz)*nx+x+dx);
        }
        Sample MakeSample(int x,int z,double d=0,double vx=0,double vz=0)
        {
            double wx=ox+x*Cell,wz=oz+z*Cell;
            return new(wx,wz,d,ground.Height(new Vec2(wx,wz))+0.0005
                +0.018*(1-Math.Exp(-Math.Max(0,d-Contour)*0.22)),vx,vz);
        }
        Sample At(int k)=>samples.TryGetValue(k,out var s)?s:MakeSample(k%nx,k/nx);
        foreach(var k in nodes.Keys)
        {
            var s=At(k);
            samples[k]=s with { Nx=(At(k-1).Height-At(k+1).Height)/(2*Cell),
                Nz=(At(k-nx).Height-At(k+nx).Height)/(2*Cell) };
        }
        var md=new MeshData();
        foreach(int k in cells.OrderBy(k=>k))
        {
            Emit(At(k),At(k+nx),At(k+nx+1));
            Emit(At(k),At(k+nx+1),At(k+1));
        }
        return new(md,wetNodes);
        Sample Crossing(Sample a,Sample b) {
            double t=(Contour-a.Density)/(b.Density-a.Density);
            double x=a.X+(b.X-a.X)*t,z=a.Z+(b.Z-a.Z)*t;
            return new(x,z,Contour,ground.Height(new Vec2(x,z))+0.0005,a.Fx+(b.Fx-a.Fx)*t,a.Fz+(b.Fz-a.Fz)*t,a.Nx+(b.Nx-a.Nx)*t,a.Nz+(b.Nz-a.Nz)*t);
        }
        int Vertex(Sample s) {
            double depth=Math.Max(0,s.Height-ground.Height(new Vec2(s.X,s.Z)));
            return md.AddVertex(new Vec3(s.X,s.Height,s.Z),new Vec3(s.Nx,1,s.Nz).Normalized(),.2,.55,.7,Math.Clamp(depth/.15,0,1),s.X,s.Z,s.Fx,s.Fz);
        }
        void Emit(Sample a,Sample b,Sample c) {
            Span<Sample> input=stackalloc Sample[]{a,b,c};
            Span<Sample> poly=stackalloc Sample[4]; int count=0;
            for(int i=0;i<3;i++) {
                var p=input[i];var q=input[(i+1)%3];
                if(p.Density>Contour)poly[count++]=p;
                if((p.Density>Contour)!=(q.Density>Contour))poly[count++]=Crossing(p,q);
            }
            for(int i=1;i+1<count;i++) {
                var p=poly[0];var q=poly[i];var r=poly[i+1];
                md.AddTriangle(Vertex(p),Vertex(q),Vertex(r));
            }
        }
    }
}
