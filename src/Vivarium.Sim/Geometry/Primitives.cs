using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>Procedural building blocks. Winding follows Godot (right-hand normal points into the surface).</summary>
public static class Primitives
{
    /// <summary>Subdivided icosahedron (unit radius). Returns positions and triangle indices.</summary>
    public static (List<Vec3> Verts, List<int> Tris) Icosphere(int subdivisions)
    {
        double t = (1 + Math.Sqrt(5)) / 2;
        var v = new List<Vec3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        for (int i = 0; i < v.Count; i++) v[i] = v[i].Normalized();
        var f = new List<int>
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11, 1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9, 4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
        };
        for (int s = 0; s < subdivisions; s++)
        {
            var cache = new Dictionary<long, int>();
            int Mid(int a, int b)
            {
                long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                if (cache.TryGetValue(key, out int m)) return m;
                v.Add(((v[a] + v[b]) / 2).Normalized());
                return cache[key] = v.Count - 1;
            }
            var nf = new List<int>(f.Count * 4);
            for (int i = 0; i < f.Count; i += 3)
            {
                int a = f[i], b = f[i + 1], c = f[i + 2];
                int ab = Mid(a, b), bc = Mid(b, c), ca = Mid(c, a);
                nf.AddRange(new[] { a, ab, ca, b, bc, ab, c, ca, bc, ab, bc, ca });
            }
            f = nf;
        }
        // the base icosahedron list above is CCW-outward (OpenGL); flip for Godot
        for (int i = 0; i < f.Count; i += 3) (f[i + 1], f[i + 2]) = (f[i + 2], f[i + 1]);
        return (v, f);
    }

    /// <summary>Adds an ellipsoid (optionally rotated about Z by pitch) with per-vertex colour/uv callback.</summary>
    public static void Ellipsoid(MeshData m, Vec3 centre, Vec3 radii, int rings, int segments,
        Func<double, double, (double[] Col, double A, double U, double V, double U2, double V2)> attr, double pitch = 0)
    {
        // Large procedural organs deserve a smooth macro silhouette; tiny beads/eyes keep their requested economy.
        // This spends geometry according to visible structural importance rather than applying one global detail tier.
        double maxR = Math.Max(radii.X, Math.Max(radii.Y, radii.Z));
        if (maxR >= 0.08) { rings = Math.Max(rings, 6); segments = Math.Max(segments, 12); }
        else if (maxR >= 0.035) { rings = Math.Max(rings, 5); segments = Math.Max(segments, 8); }
        int start = m.VertexCount;
        double cp = Math.Cos(pitch), sp = Math.Sin(pitch);
        for (int r = 0; r <= rings; r++)
        {
            double phi = Math.PI * r / rings;             // 0 = +X tip (front), π = -X tip
            for (int s = 0; s <= segments; s++)
            {
                double th = 2 * Math.PI * s / segments;
                var local = new Vec3(Math.Cos(phi) * radii.X, Math.Sin(phi) * Math.Cos(th) * radii.Y, Math.Sin(phi) * Math.Sin(th) * radii.Z);
                var n = new Vec3(Math.Cos(phi) / radii.X, Math.Sin(phi) * Math.Cos(th) / radii.Y, Math.Sin(phi) * Math.Sin(th) / radii.Z).Normalized();
                var rp = new Vec3(local.X * cp - local.Y * sp, local.X * sp + local.Y * cp, local.Z);
                var rn = new Vec3(n.X * cp - n.Y * sp, n.X * sp + n.Y * cp, n.Z);
                var a = attr((double)r / rings, (double)s / segments);
                m.AddVertex(centre + rp, rn, a.Col, a.A, a.U, a.V, a.U2, a.V2);
            }
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = start + r * (segments + 1) + s, b = a + segments + 1;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
    }

    /// <summary>
    /// Tapered tube through a polyline. Frames are parallel-transported from ring to ring instead of being
    /// rebuilt from world-up, avoiding the little twists/kinks that make curved procedural stems look assembled.
    /// Very low requested side counts are promoted to six so close silhouettes do not become triangles/squares.
    /// </summary>
    public static void Tube(MeshData m, IReadOnlyList<Vec3> path, IReadOnlyList<double> radius, int segments,
        Func<int, double, (double[] Col, double A, double U, double V, double U2, double V2)> attr, Vec3? upHint = null)
    {
        if (path.Count < 2 || radius.Count != path.Count) return;
        segments = Math.Max(6, segments);
        int start = m.VertexCount;
        var tangents = new Vec3[path.Count];
        for (int i = 0; i < path.Count; i++)
        {
            Vec3 d;
            if (i == 0) d = path[1] - path[0];
            else if (i == path.Count - 1) d = path[^1] - path[^2];
            else d = path[i + 1] - path[i - 1];
            tangents[i] = d.Normalized();
        }

        var hint = (upHint ?? Vec3.Up).Normalized();
        var side = tangents[0].Cross(hint);
        if (side.LengthSq < 1e-8) side = tangents[0].Cross(new Vec3(1, 0, 0));
        side = side.Normalized();

        for (int i = 0; i < path.Count; i++)
        {
            var dir = tangents[i];
            if (i > 0)
            {
                // Project the previous frame onto the new tangent plane: a cheap parallel transport that keeps
                // the ring orientation continuous through bends without accumulating an artificial corkscrew.
                side = side - dir * side.Dot(dir);
                if (side.LengthSq < 1e-8)
                {
                    side = dir.Cross(hint);
                    if (side.LengthSq < 1e-8) side = dir.Cross(new Vec3(1, 0, 0));
                }
                side = side.Normalized();
            }
            var up2 = side.Cross(dir).Normalized();

            double drds = 0;
            if (i == 0)
            {
                double ds = (path[1] - path[0]).Length;
                if (ds > 1e-9) drds = (radius[1] - radius[0]) / ds;
            }
            else if (i == path.Count - 1)
            {
                double ds = (path[^1] - path[^2]).Length;
                if (ds > 1e-9) drds = (radius[^1] - radius[^2]) / ds;
            }
            else
            {
                double ds = (path[i + 1] - path[i - 1]).Length;
                if (ds > 1e-9) drds = (radius[i + 1] - radius[i - 1]) / ds;
            }

            for (int s = 0; s <= segments; s++)
            {
                double th = 2 * Math.PI * s / segments;
                var radial = (side * Math.Cos(th) + up2 * Math.Sin(th)).Normalized();
                var normal = (radial - dir * drds).Normalized();
                var a = attr(i, (double)s / segments);
                m.AddVertex(path[i] + radial * radius[i], normal, a.Col, a.A, a.U, a.V, a.U2, a.V2);
            }
        }
        for (int i = 0; i < path.Count - 1; i++)
            for (int s = 0; s < segments; s++)
            {
                int a = start + i * (segments + 1) + s, b = a + segments + 1;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
    }

    /// <summary>
    /// Double-sided cambered leaf surface. A small longitudinal grid gives macro-visible leaves an actual curved
    /// silhouette and changing normal instead of a perfectly planar card; UV.x runs root→tip and UV.y across.
    /// </summary>
    public static void CurvedLeaf(MeshData m, Vec3 root, Vec3 tip, Vec3 sideHint, double halfWidth,
        double[] rootCol, double[] tipCol, double camber = 0.03, int longitudinal = 6, double asymmetry = 0)
    {
        var axis = tip - root;
        double length = axis.Length;
        if (length < 1e-8 || halfWidth <= 0) return;
        var dir = axis / length;
        var side = sideHint - dir * sideHint.Dot(dir);
        if (side.LengthSq < 1e-8) side = dir.Cross(Vec3.Up);
        if (side.LengthSq < 1e-8) side = dir.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();
        var normal = dir.Cross(side).Normalized();
        longitudinal = Math.Max(3, longitudinal);
        const int across = 2; // left / midrib / right

        Vec3 At(double t, double x)
        {
            double envelope = Math.Pow(Math.Max(0, Math.Sin(Math.PI * t)), 0.62);
            double width = halfWidth * envelope * (1.0 + asymmetry * x * (0.25 + 0.75 * t));
            double crown = camber * Math.Sin(Math.PI * t) * (1.0 - 0.42 * x * x);
            double sweep = asymmetry * halfWidth * 0.18 * Math.Sin(Math.PI * t) * t;
            return root + axis * t + side * (width * x + sweep) + normal * crown;
        }

        for (int face = 0; face < 2; face++)
        {
            var faceN = face == 0 ? normal : -normal;
            int start = m.VertexCount;
            for (int i = 0; i <= longitudinal; i++)
            {
                double t = (double)i / longitudinal;
                double dt = 1.0 / longitudinal;
                for (int j = 0; j <= across; j++)
                {
                    double x = j - 1.0;
                    var p = At(t, x);
                    var ahead = At(Math.Min(1, t + dt), x) - At(Math.Max(0, t - dt), x);
                    var cross = At(t, Math.Min(1, x + 0.5)) - At(t, Math.Max(-1, x - 0.5));
                    var n = ahead.Cross(cross).Normalized();
                    if (n.LengthSq < 1e-8) n = faceN;
                    if (n.Dot(faceN) < 0) n = -n;
                    var col = Mix(rootCol, tipCol, t);
                    m.AddVertex(p, n, col, 1, t, (x + 1) * 0.5, 0, 0);
                }
            }
            int row = across + 1;
            for (int i = 0; i < longitudinal; i++)
                for (int j = 0; j < across; j++)
                {
                    int a = start + i * row + j, b = a + row;
                    TriangleFacing(m, a, b, a + 1, faceN);
                    TriangleFacing(m, a + 1, b, b + 1, faceN);
                }
        }
    }

    /// <summary>Double-sided flat polygon (fan) — for leaves, fins, lichen lobes.</summary>
    public static void Fan(MeshData m, Vec3 centre, IReadOnlyList<Vec3> rim, Vec3 normal, double[] colCentre, double[] colRim,
        double a = 1, double u2 = 0, Func<Vec3, Vec3>? appendageOffset = null)
    {
        for (int side = 0; side < 2; side++)
        {
            var n = side == 0 ? normal : -normal;
            int c = m.AddVertex(centre, n, colCentre, a, 0.5, 0.5, u2, 0);
            var ids = new int[rim.Count];
            for (int i = 0; i < rim.Count; i++) ids[i] = m.AddVertex(rim[i], n, colRim, a, (double)i / rim.Count, 1, u2, 0);
            for (int i = 0; i < rim.Count - 1; i++) TriangleFacing(m, c, ids[i], ids[i + 1], n);
        }
    }

    /// <summary>Adds a triangle wound so it is front-facing for a viewer on the side of <paramref name="visibleNormal"/>.</summary>
    public static void TriangleFacing(MeshData m, int a, int b, int c, Vec3 visibleNormal)
    {
        var rh = (m.Position(b) - m.Position(a)).Cross(m.Position(c) - m.Position(a));
        if (rh.Dot(visibleNormal) < 0) m.AddTriangle(a, b, c); else m.AddTriangle(a, c, b);
    }

    public static double[] Mix(double[] a, double[] b, double t) => new[] { a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t, a[2] + (b[2] - a[2]) * t };
    public static double[] Scale(double[] a, double s) => new[] { MathD.Clamp01(a[0] * s), MathD.Clamp01(a[1] * s), MathD.Clamp01(a[2] * s) };
}
