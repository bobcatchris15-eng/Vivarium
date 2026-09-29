using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Geometry;

/// <summary>Cut solid wood against the specimen boundary and close each exposed cross-section.</summary>
public static class IslandMeshClipper
{
    private sealed record Vertex(Vec3 P, Vec3 N, double[] C, double U, double V)
    {
        public static Vertex Mix(Vertex a, Vertex b, double t) => new(Vec3.Lerp(a.P,b.P,t),
            Vec3.Lerp(a.N,b.N,t).Normalized(), Enumerable.Range(0,4).Select(i => MathD.Lerp(a.C[i],b.C[i],t)).ToArray(),
            MathD.Lerp(a.U,b.U,t),MathD.Lerp(a.V,b.V,t));
    }
    public static MeshData ClipClosed(MeshData source, HexDomain domain, double[] cutColor)
    {
        var current = source;
        foreach (var normal in domain.EdgeNormals)
        {
            var next = new MeshData();
            Vertex Read(int i) => new(current.Position(i),current.NormalAt(i),
                Enumerable.Range(0,4).Select(k => (double)current.Colors[i*4+k]).ToArray(),current.UV[i*2],current.UV[i*2+1]);
            double D(Vec3 p) => p.X*normal.X+p.Z*normal.Z-domain.Apothem;
            for(int t=0;t<current.Indices.Count;t+=3)
            {
                var input = new[] {Read(current.Indices[t]),Read(current.Indices[t+1]),Read(current.Indices[t+2])};
                for(int k=0;k<3;k++)
                    if(Math.Abs(D(input[k].P))<2e-6)
                        input[k]=input[k] with {P=input[k].P-new Vec3(normal.X,0,normal.Z)*D(input[k].P)};
                var polygon = new List<Vertex>();
                for(int k=0;k<3;k++)
                {
                    var a=input[k]; var b=input[(k+1)%3]; double da=D(a.P),db=D(b.P);
                    bool ia=da<=1e-9,ib=db<=1e-9;
                    if(ia) polygon.Add(a);
                    if(ia!=ib)
                    {
                        var v=Vertex.Mix(a,b,Math.Clamp(da/(da-db),0,1));
                        // Snap precisely onto the same plane as the island cut.
                        v=v with {P=v.P-new Vec3(normal.X,0,normal.Z)*D(v.P)};
                        polygon.Add(v);
                    }
                }
                if(polygon.Count<3) continue;
                int first=next.VertexCount;
                foreach(var v in polygon) next.AddVertex(v.P,v.N,v.C[0],v.C[1],v.C[2],v.C[3],v.U,v.V);
                for(int k=1;k+1<polygon.Count;k++)
                    if((polygon[k].P-polygon[0].P).Cross(polygon[k+1].P-polygon[0].P).Length>1e-10)
                        next.AddTriangle(first,first+k,first+k+1);
            }
            // Recover actual open edges after clipping, including original vertices/edges on the plane.
            // Merely pairing triangle intersections misses those exactly aligned root crests.
            var boundary = new Dictionary<((long,long,long),(long,long,long)), (Vec3 A,Vec3 B,int Count)>();
            (long,long,long) Key(Vec3 p) => ((long)Math.Round(p.X*100000),(long)Math.Round(p.Y*100000),(long)Math.Round(p.Z*100000));
            for(int t=0;t<next.Indices.Count;t+=3)
                for(int k=0;k<3;k++)
                {
                    var a=next.Position(next.Indices[t+k]);var b=next.Position(next.Indices[t+(k+1)%3]);
                    if(Math.Abs(D(a))>2e-6||Math.Abs(D(b))>2e-6||(a-b).Length<1e-7)continue;
                    var ka=Key(a);var kb=Key(b);if(ka==kb)continue;
                    var key=ka.CompareTo(kb)<0?(ka,kb):(kb,ka);
                    if(boundary.TryGetValue(key,out var value))boundary[key]=(value.A,value.B,value.Count+1);
                    else boundary[key]=(a,b,1);
                }
            Cap(next,boundary.Values.Where(v=>v.Count==1).Select(v=>(v.A,v.B)).ToList(),normal,cutColor);
            current=next;
        }
        return current;
    }
    private static void Cap(MeshData mesh,List<(Vec3,Vec3)> segments,Vec2 normal,double[] color)
    {
        var points=new List<Vec3>(); var ids=new Dictionary<(long,long,long),int>();
        int Id(Vec3 p)
        {
            var key=((long)Math.Round(p.X*100000),(long)Math.Round(p.Y*100000),(long)Math.Round(p.Z*100000));
            if(ids.TryGetValue(key,out int i))return i;
            i=points.Count;points.Add(p);ids[key]=i;return i;
        }
        var edges=new HashSet<(int,int)>();
        foreach(var(a,b)in segments){int i=Id(a),j=Id(b);if(i!=j)edges.Add(i<j?(i,j):(j,i));}
        while(edges.Count>0)
        {
            var edge=edges.First();edges.Remove(edge);
            var loop=new List<int>{edge.Item1,edge.Item2};int current=edge.Item2;
            while(current!=loop[0])
            {
                var following=edges.FirstOrDefault(e=>e.Item1==current||e.Item2==current,(-1,-1));
                if(following.Item1<0) throw new InvalidOperationException($"Open Pilot Tree cut contour normal={normal} start={points[loop[0]]} end={points[current]} edges={edges.Count}");
                edges.Remove(following); current=following.Item1==current?following.Item2:following.Item1;
                if(current!=loop[0])loop.Add(current);
            }
            if(loop.Count<3)continue;
            var uv=loop.Select(i=>new Vec2(-normal.Z*points[i].X+normal.X*points[i].Z,points[i].Y)).ToList();
            double area=0;for(int i=0;i<uv.Count;i++) area+=uv[i].X*uv[(i+1)%uv.Count].Z-uv[(i+1)%uv.Count].X*uv[i].Z;
            if(area<0){loop.Reverse();uv.Reverse();}
            var pending=Enumerable.Range(0,loop.Count).ToList();
            double Cross(Vec2 a,Vec2 b,Vec2 c)=>(b.X-a.X)*(c.Z-a.Z)-(b.Z-a.Z)*(c.X-a.X);
            var n=new Vec3(normal.X,0,normal.Z);
            void Tri(int a,int b,int c)
            {
                int first=mesh.VertexCount;
                foreach(int i in new[]{a,b,c})mesh.AddVertex(points[loop[i]],n,color,.95,uv[i].X,uv[i].Z);
                Primitives.TriangleFacing(mesh,first,first+1,first+2,n);
            }
            while(pending.Count>3)
            {
                bool clipped=false;
                for(int k=0;k<pending.Count;k++)
                {
                    int a=pending[(k+pending.Count-1)%pending.Count],b=pending[k],c=pending[(k+1)%pending.Count];
                    double cross=Cross(uv[a],uv[b],uv[c]);
                    if(Math.Abs(cross)<1e-10){pending.RemoveAt(k);clipped=true;break;}
                    if(cross<0)continue;
                    bool occupied=pending.Any(i=>i!=a&&i!=b&&i!=c&&Cross(uv[a],uv[b],uv[i])>=-1e-10&&Cross(uv[b],uv[c],uv[i])>=-1e-10&&Cross(uv[c],uv[a],uv[i])>=-1e-10);
                    if(occupied)continue;
                    Tri(a,b,c);pending.RemoveAt(k);clipped=true;break;
                }
                if(!clipped)throw new InvalidOperationException($"Invalid Pilot Tree cut contour normal={normal} remaining={string.Join(";",pending.Select(i=>uv[i]))}");
            }
            if(pending.Count==3&&Math.Abs(Cross(uv[pending[0]],uv[pending[1]],uv[pending[2]]))>1e-10)Tri(pending[0],pending[1],pending[2]);
        }
    }
}
