using Vivarium.Sim.Core;
using Vivarium.Sim.Water;
namespace Vivarium.Sim.Geometry;

/// <summary>Closed 3D surface reconstructed exclusively from volume-carrying liquid positions.
/// A transient sparse surface-net sampling lattice is geometry work, never terrain depth or fluid state.
/// The surface can hang in air, curve vertically, form a deep pool and spill over a lip.</summary>
public static class LiquidSurfaceMesh
{
    public const double Spacing=.0085,Support=.02773445097402539,Contour=.25;
    private static readonly double Coefficient=315/(64*Math.PI*Math.Pow(Support,9));
    private readonly record struct Node(double Density,Vec3 Normal,Vec3 Velocity);
    private static readonly (int,int,int)[] Corners=[(0,0,0),(1,0,0),(1,1,0),(0,1,0),(0,0,1),(1,0,1),(1,1,1),(0,1,1)];
    private static readonly (int,int)[] CubeEdges=[(0,1),(1,2),(2,3),(3,0),(4,5),(5,6),(6,7),(7,4),(0,4),(1,5),(2,6),(3,7)];
    public static MeshData Build(IReadOnlyList<FluidParticle> water)
    {
        var nodes=new Dictionary<(int,int,int),Node>();double r2=Support*Support;
        foreach(var f in water)
        {
            var p=f.Position;double stretch=Math.Clamp(1+f.Velocity.Length*.9,1,2.4);
            var axis=f.Velocity.Normalized();double along2=r2*stretch*stretch,cross2=r2/stretch;
            // Tight bounds of the determinant-preserving ellipsoid. A cube around the long axis
            // otherwise spends most of its time sampling empty air around a thin falling jet.
            double rx=Math.Sqrt(cross2+(along2-cross2)*axis.X*axis.X);
            double ry=Math.Sqrt(cross2+(along2-cross2)*axis.Y*axis.Y);
            double rz=Math.Sqrt(cross2+(along2-cross2)*axis.Z*axis.Z);
            int x0=(int)Math.Ceiling((p.X-rx)/Spacing),x1=(int)Math.Floor((p.X+rx)/Spacing);
            int y0=(int)Math.Ceiling((p.Y-ry)/Spacing),y1=(int)Math.Floor((p.Y+ry)/Spacing);
            int z0=(int)Math.Ceiling((p.Z-rz)/Spacing),z1=(int)Math.Floor((p.Z+rz)/Spacing);
            for(int z=z0;z<=z1;z++)for(int y=y0;y<=y1;y++)for(int x=x0;x<=x1;x++)
            {
                var d=new Vec3(x*Spacing,y*Spacing,z*Spacing)-p;
                double along=d.Dot(axis);var metric=d*stretch+axis*(along*(1/(stretch*stretch)-stretch));
                double q=r2-d.Dot(metric);if(q<=0)continue;
                double density=Coefficient*f.Volume*q*q*q;var key=(x,y,z);var a=nodes.GetValueOrDefault(key);
                nodes[key]=new(a.Density+density,a.Normal+metric*(6*Coefficient*f.Volume*q*q),a.Velocity+f.Velocity*density);
            }
        }
        var cells=new HashSet<(int,int,int)>();
        foreach(var(key,node)in nodes)
            if(node.Density>=Contour)for(int dz=-1;dz<=0;dz++)for(int dy=-1;dy<=0;dy++)for(int dx=-1;dx<=0;dx++)cells.Add((key.Item1+dx,key.Item2+dy,key.Item3+dz));
        var mesh=new MeshData();var vertices=new Dictionary<(int,int,int),int>();
        Span<(int,int,int)> keys=stackalloc (int,int,int)[8];
        Span<Node> values=stackalloc Node[8];
        void Triangle(int a,int b,int c)
        {
            var n=(mesh.Position(b)-mesh.Position(a)).Cross(mesh.Position(c)-mesh.Position(a));
            if(n.Dot(mesh.NormalAt(a)+mesh.NormalAt(b)+mesh.NormalAt(c))<0)(b,c)=(c,b);
            // Godot front faces are clockwise; normals are still outward.
            mesh.AddTriangle(a,c,b);
        }
        foreach(var cell in cells)
        {
            int inside=0;
            for(int i=0;i<8;i++)
            {
                keys[i]=(cell.Item1+Corners[i].Item1,cell.Item2+Corners[i].Item2,cell.Item3+Corners[i].Item3);
                values[i]=nodes.GetValueOrDefault(keys[i]);if(values[i].Density>=Contour)inside++;
            }
            if(inside==0||inside==8)continue;
            Vec3 position=Vec3.Zero,normal=Vec3.Zero,velocity=Vec3.Zero;int crossings=0;
            foreach(var(a,b)in CubeEdges)
            {
                var na=values[a];var nb=values[b];if((na.Density>=Contour)==(nb.Density>=Contour))continue;
                double t=(Contour-na.Density)/(nb.Density-na.Density);var ka=keys[a];var kb=keys[b];
                position+=Vec3.Lerp(new Vec3(ka.Item1*Spacing,ka.Item2*Spacing,ka.Item3*Spacing),new Vec3(kb.Item1*Spacing,kb.Item2*Spacing,kb.Item3*Spacing),t);
                normal+=Vec3.Lerp(na.Normal,nb.Normal,t);velocity+=Vec3.Lerp(na.Velocity,nb.Velocity,t);crossings++;
            }
            position/=crossings;velocity/=crossings*Contour;
            vertices[cell]=mesh.AddVertex(position,normal.Normalized(),.02,.04,.035,.028,position.X,position.Z,velocity.X,velocity.Z);
        }
        void Quad((int,int,int) a,(int,int,int) b,(int,int,int) c,(int,int,int) d)
        {if(vertices.TryGetValue(a,out int ia)&&vertices.TryGetValue(b,out int ib)&&vertices.TryGetValue(c,out int ic)&&vertices.TryGetValue(d,out int id))
            {Triangle(ia,ib,ic);Triangle(ia,ic,id);}}
        // Four neighbouring surface cells surround each sign-changing sample edge. One quad
        // connects them; the net is closed and carries the analytic particle-kernel normals.
        foreach(var cell in vertices.Keys)
        {
            var(x,y,z)=cell;bool inside=nodes.GetValueOrDefault(cell).Density>=Contour;
            if(inside!=(nodes.GetValueOrDefault((x+1,y,z)).Density>=Contour))Quad(cell,(x,y-1,z),(x,y-1,z-1),(x,y,z-1));
            if(inside!=(nodes.GetValueOrDefault((x,y+1,z)).Density>=Contour))Quad(cell,(x-1,y,z),(x-1,y,z-1),(x,y,z-1));
            if(inside!=(nodes.GetValueOrDefault((x,y,z+1)).Density>=Contour))Quad(cell,(x-1,y,z),(x-1,y-1,z),(x,y-1,z));
        }
        return mesh;
    }
}
