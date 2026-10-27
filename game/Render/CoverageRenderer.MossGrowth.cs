using System;
using Godot;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Coverage;

namespace Vivarium.Game.Render;

/// <summary>
/// Draw-only moss growth nuclei. Their locations are keyed to occupied living coverage
/// cells, not a tiled decorative pillow pattern. Biomass makes each point grow outward
/// and overlapping shoots form an optically porous continuous patch.
/// No ecological state, seed history, or event log is generated or stored here.
/// </summary>
public partial class CoverageRenderer
{
    // Opt-in while comparing against existing real GPU captures. 1=moderate, 4=extreme.
    private static int MossMicroGrowthStrength
    {
        get
        {
            string? setting = System.Environment.GetEnvironmentVariable("VIVARIUM_MOSS_MICRO_GROWTH");
            return int.TryParse(setting, out int value) ? Math.Clamp(value, 0, 4) : 1;
        }
    }

    // Merge biomass-driven growth discs from ecological seed locations.
    // This is presentation-only, deterministic from current coverage, never a history.
    private static float MossColonizationField(CoverageLayer layer, double wx, double wz)
    {
        const double spacing = .075;
        int ix = (int)Math.Floor(wx / spacing), iz = (int)Math.Floor(wz / spacing);
        float union = 0f;
        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int px = ix + dx, pz = iz + dz;
            uint hash = ReliefHash(px, pz);
            double sx = (px + .5 + ((hash & 255) / 255.0 - .5) * .48) * spacing;
            double sz = (pz + .5 + (((hash >> 8) & 255) / 255.0 - .5) * .48) * spacing;
            int gx = (int)Math.Floor(sx / CoverageSpec.CellSize);
            int gz = (int)Math.Floor(sz / CoverageSpec.CellSize);
            if (layer.GetOcc(gx, gz) == 0) continue;
            float b = layer.GetB(gx, gz);
            if (b <= .025f) continue;
            double radius = (.022 + .068 * Math.Sqrt(Math.Clamp(b, 0f, 1f))) *
                (.86 + ((hash >> 16) & 255) / 255.0 * .28);
            double d2 = ((wx - sx) * (wx - sx) + (wz - sz) * (wz - sz)) / (radius * radius);
            if (d2 >= 1.0) continue;
            float crown = (float)(1 - d2);
            crown = crown * crown * (3f - 2f * crown);
            union = 1f - (1f - union) * (1f - crown);
        }
        return union;
    }

    /// <summary>Adds a fan of real opaque leaflet geometry around an established moss growing point.</summary>
    private static void AppendMossGrowthNucleus(MeshData mesh, Vec3 foot, Vec3 normal,
        Color live, Color old, uint hash, float biomass, double speciesHeight,
        bool rim, int strength)
    {
        if (strength <= 0 || biomass <= 0.03f) return;
        float mature = Math.Clamp(biomass, 0f, 1f);
        float age = MathF.Sqrt(mature);
        double h = Math.Clamp(speciesHeight * 0.46, 0.003, 0.010) *
            (0.35 + 0.65 * age) * (rim ? 0.65 : 1.0);
        double reach = Math.Clamp(0.0025 + age * 0.0045, 0.0025, 0.007) *
            (rim ? 0.72 : 1.0);
        float colorVariance = ((hash >> 16) & 255) / 255f;
        Color mid = old.Lerp(live, Math.Clamp(0.4f + 0.50f * age + 0.15f * colorVariance, 0f, 1f));
        Color tipColor = mid.Lerp(live, 0.45f) * (0.92f + colorVariance * .13f);
        double phase = (hash & 1023) / 1023.0 * Math.PI * 2;
        Vec3 up = normal.Normalized();
        int fans = strength switch
        {
            >= 4 => 9,
            3 => 7,
            2 => 6,
            _ => 5
        };
        // Construct orthonormal basis on substrate tangent plane so leaflets drape
        // naturally across steep slopes, rocks, and curved logs without clipping substrate.
        Vec3 tangentU = Math.Abs(up.Y) < 0.99
            ? Vec3.Up.Cross(up).Normalized()
            : Vec3.Right;
        Vec3 tangentV = up.Cross(tangentU).Normalized();
        var colBase = new[] { (double)(mid.R * 0.68f), (double)(mid.G * 0.72f), (double)(mid.B * 0.59f) };
        var colTip = new[] { (double)tipColor.R, (double)tipColor.G, (double)tipColor.B };
        // Actual triangular surfaces rather than alpha-discarded cards. Random azimuth breaks
        // the regular lattice and folds the leaflets over their own central growing point.
        for (int k = 0; k < fans; k++)
        {
            uint kh = ReliefHash(unchecked((int)hash) + k * 67, k * 173);
            double azimuth = phase + k * Math.PI * 2 / fans +
                ((kh & 255) / 255.0 - 0.5) * 0.45;
            double radial = reach * (0.68 + ((kh >> 8) & 255) / 255.0 * 0.60);
            double rise = h * (0.55 + ((kh >> 16) & 255) / 255.0 * 0.55);
            var dir = tangentU * Math.Cos(azimuth) + tangentV * Math.Sin(azimuth);
            var widthDir = up.Cross(dir).Normalized();
            double width = radial * 0.32;
            var basePt = foot + up * 0.0002;
            var shoulder = basePt + dir * (radial * .50) + up * (rise * 0.70);
            var tip = basePt + dir * radial + up * (rise * 0.98);
            var leafNormal = (up * 0.85 + dir * 0.35).Normalized();
            int a = mesh.AddVertex(basePt - widthDir * (width * 0.27), up, colBase, 1.0,
                basePt.X, basePt.Z, 1.0, 0.15);
            int b = mesh.AddVertex(basePt + widthDir * (width * 0.27), up, colBase, 1.0,
                basePt.X, basePt.Z, 1.0, 0.15);
            int c = mesh.AddVertex(shoulder - widthDir * width, leafNormal, colTip, 1.0,
                shoulder.X, shoulder.Z, 1.0, 0.65);
            int d = mesh.AddVertex(shoulder + widthDir * width, leafNormal, colTip, 1.0,
                shoulder.X, shoulder.Z, 1.0, 0.65);
            int e = mesh.AddVertex(tip, leafNormal, colTip, 1.0, tip.X, tip.Z, 1.0, 1.0);
            Primitives.TriangleFacing(mesh, a, b, c, up);
            Primitives.TriangleFacing(mesh, b, d, c, up);
            Primitives.TriangleFacing(mesh, c, d, e, up);
        }
    }
}
