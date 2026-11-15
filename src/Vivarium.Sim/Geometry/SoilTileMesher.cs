using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Geometry;

/// <summary>Pure, version-bound soil tile generation. Render LOD never changes physical height queries.</summary>
public static class SoilTileMesher
{
    public readonly record struct Tile(int X, int Z);
    public enum Detail { Fine, Medium, Coarse }
    public readonly record struct Request(Tile Tile, Detail Detail);

    // Tile boundaries are integer fine lattice coordinates and every boundary is sampled at fine spacing,
    // independent of either tile's detail. Thus changing either tile cannot open a vertical crack.
    public sealed class Snapshot
    {
        public SoilSurface Surface { get; }
        public HexDomain Domain { get; }
        public int TileCells { get; }
        public int MaxX { get; }
        public int MaxZ { get; }
        public int MediumStride { get; }
        public int CoarseStride { get; }
        public Snapshot(Heightfield hf, HexDomain domain, double tileSize = 0.75)
        {
            if (hf.SoilSurfaceVersion != 1) throw new ArgumentException("Version 1 soil required", nameof(hf));
            if (!double.IsFinite(tileSize) || tileSize < .5 || tileSize > 1) throw new ArgumentOutOfRangeException(nameof(tileSize));
            Surface = hf.SoilSurface;
            // Copy the domain; callers may mutate its public arrays.
            Domain = new HexDomain(domain.Diameter, domain.Tolerance);
            TileCells = Math.Max(1, (int)Math.Round(tileSize / Surface.Spacing));
            MaxX = checked((hf.Nx - 1) * Surface.Subdivisions);
            MaxZ = checked((hf.Nz - 1) * Surface.Subdivisions);
            MediumStride = Math.Max(1, (int)Math.Round(.05 / Surface.Spacing));
            CoarseStride = Surface.Subdivisions;
        }
        public int TilesX => (MaxX + TileCells - 1) / TileCells;
        public int TilesZ => (MaxZ + TileCells - 1) / TileCells;
    }

    /// <summary>Wet tile flags are captured by the caller before dispatch, including a conservative halo.</summary>
    public static Detail ChooseDetail(Snapshot s, Tile t, Vec2 camera, bool wet, double near = 1.5, double medium = 5, double? cameraY = null)
    {
        if (wet) return Detail.Fine;
        double x0 = s.Surface.OriginX + t.X * s.TileCells * s.Surface.Spacing;
        double z0 = s.Surface.OriginZ + t.Z * s.TileCells * s.Surface.Spacing;
        double x1 = s.Surface.OriginX + Math.Min((t.X + 1) * s.TileCells, s.MaxX) * s.Surface.Spacing;
        double z1 = s.Surface.OriginZ + Math.Min((t.Z + 1) * s.TileCells, s.MaxZ) * s.Surface.Spacing;
        double dx = Math.Max(0, Math.Max(x0 - camera.X, camera.X - x1));
        double dz = Math.Max(0, Math.Max(z0 - camera.Z, camera.Z - z1));
        double dy = cameraY is double y ? y - s.Surface.Height(Math.Clamp(camera.X, x0, x1), Math.Clamp(camera.Z, z0, z1)) : 0;
        double d = Math.Sqrt(dx * dx + dz * dz + dy * dy);
        return d <= near ? Detail.Fine : d <= medium ? Detail.Medium : Detail.Coarse;
    }

    public static TopMesh Build(Snapshot s, Request request)
    {
        var t = request.Tile;
        if (t.X < 0 || t.Z < 0 || t.X >= s.TilesX || t.Z >= s.TilesZ) throw new ArgumentOutOfRangeException(nameof(request));
        int x0 = t.X * s.TileCells, z0 = t.Z * s.TileCells;
        int x1 = Math.Min(x0 + s.TileCells, s.MaxX), z1 = Math.Min(z0 + s.TileCells, s.MaxZ);
        int stride = request.Detail switch { Detail.Fine => 1, Detail.Medium => s.MediumStride, _ => s.CoarseStride };
        var mesh = new MeshData(); var xz = new List<Vec2>();
        var vertices = new Dictionary<(double X, double Z, double Y), int>();
        Vec3 V(int x, int z) => s.Surface.Vertex(x, z);
        int Add(Vec3 p)
        {
            var key = (p.X, p.Z, p.Y);
            if (vertices.TryGetValue(key, out int i)) return i;
            var n = s.Surface.ShadingNormal(p.X, p.Z);
            i = mesh.AddVertex(p, n, .4, .3, .2, 1, p.X, p.Z);
            xz.Add(new Vec2(p.X, p.Z)); vertices.Add(key, i); return i;
        }
        void Triangle(Vec3 a, Vec3 b, Vec3 c)
        {
            var aa = new Vec2(a.X,a.Z); var bb = new Vec2(b.X,b.Z); var cc = new Vec2(c.X,c.Z);
            List<Vec2> poly;
            if (s.Domain.SignedDistance(aa) <= 0 && s.Domain.SignedDistance(bb) <= 0 && s.Domain.SignedDistance(cc) <= 0)
                poly = new List<Vec2> { aa, bb, cc };
            else poly = s.Domain.ClipPolygon(new[] { aa, bb, cc });
            if (poly.Count < 3) return;
            double det = (bb-aa).Cross(cc-aa);
            if (Math.Abs(det) < 1e-15) return;
            Vec3 Point(Vec2 p)
            {
                // Preserve the approximating triangle plane at the clipping boundary.
                double u = (p-aa).Cross(cc-aa) / det, v = (bb-aa).Cross(p-aa) / det;
                return new Vec3(p.X, a.Y + u*(b.Y-a.Y) + v*(c.Y-a.Y), p.Z);
            }
            int first = Add(Point(poly[0]));
            for (int k=1; k+1<poly.Count; k++)
                if (Math.Abs((poly[k]-poly[0]).Cross(poly[k+1]-poly[0])) >= 1e-12)
                    mesh.AddTriangle(first, Add(Point(poly[k])), Add(Point(poly[k+1])));
        }
        for (int z=z0; z<z1; z+=stride) for (int x=x0; x<x1; x+=stride)
        {
            int xx=Math.Min(x+stride,x1), zz=Math.Min(z+stride,z1);
            if (stride == 1) { Triangle(V(x,z),V(xx,z),V(xx,zz)); Triangle(V(x,z),V(xx,zz),V(x,zz)); continue; }
            // Interior edges have just endpoints; tile edges include every fine sample. Fan keeps
            // matching adjacent LOD edges without skirts or T-junction cracks at tile boundaries.
            var ring = new List<Vec3>();
            void Edge(int ax,int az,int bx,int bz,bool fine)
            {
                int n = fine ? Math.Max(Math.Abs(bx-ax),Math.Abs(bz-az)) : 1;
                for (int k=0;k<n;k++) ring.Add(V(ax+(bx-ax)*k/n,az+(bz-az)*k/n));
            }
            Edge(x,z,xx,z,z==z0); Edge(xx,z,xx,zz,xx==x1);
            Edge(xx,zz,x,zz,zz==z1); Edge(x,zz,x,z,x==x0);
            double cx=(s.Surface.OriginX+(x+xx)*.5*s.Surface.Spacing), cz=(s.Surface.OriginZ+(z+zz)*.5*s.Surface.Spacing);
            var center=new Vec3(cx,s.Surface.Height(cx,cz),cz);
            for(int k=0;k<ring.Count;k++) Triangle(center,ring[k],ring[(k+1)%ring.Count]);
        }
        return new TopMesh(mesh,xz);
    }
}
