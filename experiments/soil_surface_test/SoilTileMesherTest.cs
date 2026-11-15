using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

// Draft standalone test: compile alongside SoilTileMesher after review.
static class SoilTileMesherTest
{
    static void Check(bool value, string why) { if (!value) throw new Exception(why); }
    static void Main()
    {
        var d = new WorldDescriptor { Diameter=5, CellSize=.23, Seed=42 };
        d.Terrain.SoilSurfaceVersion=1;
        var domain=new HexDomain(d.Diameter); var hf=Heightfield.Generate(d,domain);
        var s=new SoilTileMesher.Snapshot(hf,domain);
        var a=new SoilTileMesher.Tile(s.TilesX/2-1,s.TilesZ/2);
        var b=new SoilTileMesher.Tile(a.X+1,a.Z);
        double seam=s.Surface.OriginX+(a.X+1)*s.TileCells*s.Surface.Spacing;
        HashSet<(float,float,float)> Edge(TopMesh m)
        {
            var set=new HashSet<(float,float,float)>();
            for(int i=0;i<m.Mesh.VertexCount;i++) { var p=m.Mesh.Position(i); if(Math.Abs(p.X-seam)<1e-6) set.Add(((float)p.X,(float)p.Y,(float)p.Z)); }
            return set;
        }
        HashSet<(float,float)> Segments(TopMesh m)
        {
            var set=new HashSet<(float,float)>();
            for(int i=0;i<m.Mesh.Indices.Count;i+=3) for(int k=0;k<3;k++)
            {
                var p=m.Mesh.Position(m.Mesh.Indices[i+k]); var q=m.Mesh.Position(m.Mesh.Indices[i+(k+1)%3]);
                if(Math.Abs(p.X-seam)<1e-6 && Math.Abs(q.X-seam)<1e-6 && Math.Abs(p.Z-q.Z)>1e-7)
                    set.Add(((float)Math.Min(p.Z,q.Z),(float)Math.Max(p.Z,q.Z)));
            }
            return set;
        }
        foreach (SoilTileMesher.Detail l in Enum.GetValues<SoilTileMesher.Detail>())
        foreach (SoilTileMesher.Detail r in Enum.GetValues<SoilTileMesher.Detail>())
        {
            var left=SoilTileMesher.Build(s,new(a,l)); var right=SoilTileMesher.Build(s,new(b,r));
            Check(Edge(left).Count>2 && Edge(left).SetEquals(Edge(right)), $"seam vertices {l}/{r}");
            Check(Segments(left).SetEquals(Segments(right)), $"seam segments {l}/{r}");
            Check(left.Mesh.Indices.Count%3==0 && right.Mesh.Indices.Count%3==0,"triangle indices");
        }
        // Measure actual vertical approximation error at triangle centroids; this is an observed
        // sample, not a global bound. Fine triangles must match the physical surface.
        foreach (SoilTileMesher.Detail lod in Enum.GetValues<SoilTileMesher.Detail>())
        {
            var mesh=SoilTileMesher.Build(s,new(a,lod)).Mesh; double max=0;
            for(int i=0;i<mesh.Indices.Count;i+=3)
            {
                var p=mesh.Position(mesh.Indices[i]); var q=mesh.Position(mesh.Indices[i+1]); var r=mesh.Position(mesh.Indices[i+2]);
                double x=(p.X+q.X+r.X)/3, z=(p.Z+q.Z+r.Z)/3, y=(p.Y+q.Y+r.Y)/3;
                max=Math.Max(max,Math.Abs(y-s.Surface.Height(x,z)));
            }
            Console.WriteLine($"{lod}: {mesh.TriangleCount} triangles, sampled maximum error {max*1000:F3} mm");
            Check(double.IsFinite(max),"finite approximation error");
            if(lod==SoilTileMesher.Detail.Fine) Check(max<1e-5,"fine physical agreement");
        }
        Check(SoilTileMesher.ChooseDetail(s,a,new Vec2(100,100),true)==SoilTileMesher.Detail.Fine,"wet stays fine");
    }
}
