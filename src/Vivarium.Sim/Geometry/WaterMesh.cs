using Vivarium.Sim.Core;
using Vivarium.Sim.Fields;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Geometry;

/// <summary>Unchanged hydrostatic pond geometry. Mobile surfaces are GPU-displaced by the client.</summary>
public static class WaterMesh
{
    /// <summary>Standing groundwater intersects terrain at one hydrostatic elevation.</summary>
    public static MeshData BuildWaterTable(VivariumWorld w, Heightfield terrain)
    {
        var mesh = new MeshData();
        double level = w.Water.WaterTable;
        // Work on the authoritative terrain faces, rather than an independently
        // centered hydrology grid. A coarse cell's dry corners/center cannot prove
        // there is no submerged terrain vertex inside it.
        for (int z = 0; z < terrain.Nz - 1; z++)
            for (int x = 0; x < terrain.Nx - 1; x++)
            {
                var a = terrain.VertexPos(x, z);
                var b = terrain.VertexPos(x + 1, z);
                var c = terrain.VertexPos(x + 1, z + 1);
                var d = terrain.VertexPos(x, z + 1);
                Emit(new[] { a, b, c }); Emit(new[] { a, c, d });
            }
        return mesh;

        void Emit(Vec2[] triangle)
        {
            if (triangle.All(p => terrain.Height(p) >= level)) return;
            // Domain clipping preserves the terrain face's single plane. Every
            // shoreline intersection is then linear on exactly that same plane.
            var polygon = w.Domain.ClipPolygon(triangle);
            var wet = new List<Vec2>();
            for (int k = 0; k < polygon.Count; k++)
            {
                var a = polygon[k]; var b = polygon[(k + 1) % polygon.Count];
                double ha = terrain.Height(a) - level, hb = terrain.Height(b) - level;
                if (ha < 0) Append(a);
                if ((ha < 0) != (hb < 0)) Append(a + (b - a) * (ha / (ha - hb)));
            }
            if (wet.Count > 1 && Vec2.DistanceSq(wet[0], wet[^1]) < 1e-20) wet.RemoveAt(wet.Count - 1);
            if (wet.Count < 3) return;
            var ids = wet.Select(q => mesh.AddVertex(Vec3.FromXZ(q, level), Vec3.Up, .2, .55, .7,
                MathD.Clamp01((level - terrain.Height(q)) / .15), q.X, q.Z)).ToArray();
            for (int k = 1; k + 1 < ids.Length; k++)
                if (Math.Abs((wet[k] - wet[0]).Cross(wet[k + 1] - wet[0])) > 1e-12)
                    mesh.AddTriangle(ids[0], ids[k], ids[k + 1]);
            AddCutFaces(mesh, w.Domain, terrain, wet, _ => level, .15);

            void Append(Vec2 p)
            {
                if (wet.Count == 0 || Vec2.DistanceSq(wet[^1], p) > 1e-20) wet.Add(p);
            }
        }
    }

    private static void AddCutFaces(
        MeshData mesh, HexDomain dom, Heightfield hf, IReadOnlyList<Vec2> poly,
        Func<Vec2, double> surface, double depthScale)
    {
        for (int k = 0; k < poly.Count; k++)
        {
            var a = poly[k];
            var b = poly[(k + 1) % poly.Count];
            int side = SharedSide(dom, a, b);
            if (side < 0) continue;

            double sa = surface(a), sb = surface(b);
            double ba = Math.Min(hf.Height(a), sa), bb = Math.Min(hf.Height(b), sb);
            if (sa - ba < 1e-4 && sb - bb < 1e-4) continue;

            var n2 = dom.EdgeNormals[side];
            var n = new Vec3(n2.X, 0, n2.Z);
            double dfa = MathD.Clamp01((sa - ba) / depthScale);
            double dfb = MathD.Clamp01((sb - bb) / depthScale);
            int ta = mesh.AddVertex(new Vec3(a.X, sa, a.Z), n, 0.9, 0.55, 0.7, dfa, a.X, 0, 0, 0);
            int tb = mesh.AddVertex(new Vec3(b.X, sb, b.Z), n, 0.9, 0.55, 0.7, dfb, b.X, 0, 0, 0);
            int la = mesh.AddVertex(new Vec3(a.X, ba, a.Z), n, 0.9, 0.55, 0.7, dfa, a.X, sa - ba, 0, 0);
            int lb = mesh.AddVertex(new Vec3(b.X, bb, b.Z), n, 0.9, 0.55, 0.7, dfb, b.X, sb - bb, 0, 0);
            mesh.AddTriangle(ta, la, lb);
            mesh.AddTriangle(ta, lb, tb);
        }
    }

    /// <summary>Index of the hexagon side both points lie on (within 1e-7), or -1.</summary>
    public static int SharedSide(HexDomain dom, Vec2 a, Vec2 b)
    {
        for (int k = 0; k < 6; k++)
        {
            var n = dom.EdgeNormals[k];
            if (Math.Abs(a.Dot(n) - dom.Apothem) < 1e-7 &&
                Math.Abs(b.Dot(n) - dom.Apothem) < 1e-7 &&
                Vec2.DistanceSq(a, b) > 1e-14) return k;
        }
        return -1;
    }

    public static Vec2 VisualFlowDirection(VivariumWorld w, int cell) =>
        new Vec2(w.Water.FlowX[cell], w.Water.FlowZ[cell]).Normalized();
}
