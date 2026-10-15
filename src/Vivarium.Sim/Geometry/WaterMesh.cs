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
        double level = w.Water.WaterTable, step = w.Grid.CellSize / 4;
        foreach (int cell in w.Grid.DomainCells)
        {
            int i = cell % w.Grid.Nx, j = cell / w.Grid.Nx;
            double cx0 = w.Grid.OriginX + i * w.Grid.CellSize;
            double cz0 = w.Grid.OriginZ + j * w.Grid.CellSize;
            double cx1 = cx0 + w.Grid.CellSize, cz1 = cz0 + w.Grid.CellSize;
            if (w.Water.Bed[cell] > level &&
                terrain.Height(new Vec2(cx0, cz0)) > level &&
                terrain.Height(new Vec2(cx1, cz0)) > level &&
                terrain.Height(new Vec2(cx0, cz1)) > level &&
                terrain.Height(new Vec2(cx1, cz1)) > level)
            {
                if (!w.Grid.IsBoundaryCell[cell]) continue;
            }
            double x0 = cx0, z0 = cz0, x1 = cx1, z1 = cz1;
            ExtendBoundarySquare(w.Grid, cell, i, j, ref x0, ref z0, ref x1, ref z1);
            if (terrain.Height(new Vec2(x0, z0)) > level &&
                terrain.Height(new Vec2(x1, z0)) > level &&
                terrain.Height(new Vec2(x0, z1)) > level &&
                terrain.Height(new Vec2(x1, z1)) > level &&
                w.Water.Bed[cell] > level)
                continue;
            for (double z = z0; z < z1 - 1e-9; z += step)
                for (double x = x0; x < x1 - 1e-9; x += step)
                {
                    var a = new Vec2(x,z); var b = new Vec2(x+step,z);
                    var c = new Vec2(x+step,z+step); var d = new Vec2(x,z+step);
                    Emit(new[]{a,b,c}); Emit(new[]{a,c,d});
                }
        }
        return mesh;
        void Emit(Vec2[] triangle)
        {
            var polygon = w.Domain.ClipPolygon(triangle);
            var wet = new List<Vec2>();
            for(int k=0;k<polygon.Count;k++)
            {
                var a=polygon[k]; var b=polygon[(k+1)%polygon.Count];
                double ha=terrain.Height(a)-level, hb=terrain.Height(b)-level;
                if(ha<0)wet.Add(a);
                if((ha<0)!=(hb<0))
                {
                    // Bisect the actual heightfield, rather than averaging neighboring water heights.
                    double lo=0,hi=1;
                    for(int n=0;n<24;n++)
                    {
                        double t=(lo+hi)*.5; var q=a+(b-a)*t;
                        if((terrain.Height(q)<level)==(ha<0))lo=t;else hi=t;
                    }
                    wet.Add(a+(b-a)*((lo+hi)*.5));
                }
            }
            if(wet.Count<3)return;
            var ids=wet.Select(q=>mesh.AddVertex(Vec3.FromXZ(q,level),Vec3.Up,.2,.55,.7,
                MathD.Clamp01((level-terrain.Height(q))/.15),q.X,q.Z)).ToArray();
            for(int k=1;k+1<ids.Length;k++)mesh.AddTriangle(ids[0],ids[k],ids[k+1]);
            AddCutFaces(mesh,w.Domain,terrain,wet,_=>level,.15);
        }
    }

    private static void ExtendBoundarySquare(
        GridSpec g, int idx, int i, int j, ref double x0, ref double z0, ref double x1, ref double z1)
    {
        if (!g.IsBoundaryCell[idx]) return;
        double cs = g.CellSize;
        if (!g.InDomain(i - 1, j)) x0 -= cs;
        if (!g.InDomain(i + 1, j)) x1 += cs;
        if (!g.InDomain(i, j - 1)) z0 -= cs;
        if (!g.InDomain(i, j + 1)) z1 += cs;
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
