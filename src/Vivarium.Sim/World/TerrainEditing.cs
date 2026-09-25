using Vivarium.Sim.Core;
using Vivarium.Sim.Content;
using Vivarium.Sim.Coverage;

namespace Vivarium.Sim.World;

public enum SculptMode { Raise, Lower, Smooth }

/// <summary>
/// Authoritative terrain sculpting. Edits the heightfield vertices under a disc with a smooth falloff, clamped
/// to the world's height limits, then brings every dependent system back into agreement: hydrology beds (water
/// finds its new level on its own, and ground dug below the water table fills as a pond), prop seating, animal
/// resting heights and the light field.
/// </summary>
public static class TerrainEditing
{
    /// <summary>
    /// Applies one brush dab. <paramref name="amount"/> is metres at the brush centre for Raise/Lower, and the
    /// blend fraction (0..1) toward the local average for Smooth. Returns the number of vertices changed.
    /// </summary>
    public static int Sculpt(VivariumWorld w, Vec2 centre, double radius, double amount, SculptMode mode)
    {
        var hf = w.Terrain;
        if (radius <= 0 || amount <= 0) return 0;
        hf.BeginEdit();
        int i0 = Math.Max(0, (int)Math.Floor((centre.X - radius - hf.OriginX) / hf.Step));
        int i1 = Math.Min(hf.Nx - 1, (int)Math.Ceiling((centre.X + radius - hf.OriginX) / hf.Step));
        int j0 = Math.Max(0, (int)Math.Floor((centre.Z - radius - hf.OriginZ) / hf.Step));
        int j1 = Math.Min(hf.Nz - 1, (int)Math.Ceiling((centre.Z + radius - hf.OriginZ) / hf.Step));
        if (i0 > i1 || j0 > j1) return 0;
        // smoothing reads from a snapshot so the result does not depend on visiting order
        double[]? before = mode == SculptMode.Smooth ? (double[])hf.H.Clone() : null;
        int changed = 0;
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                double d = Vec2.Distance(hf.VertexPos(i, j), centre) / radius;
                if (d >= 1) continue;
                double fall = (1 - d * d) * (1 - d * d);
                int k = hf.VertexIndex(i, j);
                double h = hf.H[k];
                double next = mode switch
                {
                    SculptMode.Raise => h + amount * fall,
                    SculptMode.Lower => h - amount * fall,
                    _ => h + (Average3x3(hf, before!, i, j) - h) * MathD.Clamp01(amount * fall),
                };
                next = MathD.Clamp(next, hf.MinHeight, hf.MaxHeight);
                if (next == h) continue;
                hf.H[k] = next;
                changed++;
            }
        if (changed == 0) return 0;
        hf.Touch();
        w.Water.RefreshBed(hf);
        w.Placement.Reseat(centre, radius);
        w.Fields.MarkLightStale();
        // sculpting disturbs the ground: crusts and stable-soil classification reset under the brush (§2)
        CoverageEnvironment.StabilityOf(w).Reset(centre, radius, w.Clock.SimSeconds);
        foreach (var f in w.Fauna.Items)
        {
            if (f.Grabbed || Vec2.Distance(f.PositionXZ, centre) > radius) continue;
            var sp = w.Content.FaunaOrThrow(f.SpeciesId);
            f.Y = sp.Medium == Medium.Aquatic ? w.FaunaSystem.RestingY(sp, f.PositionXZ, f.Pitch) : w.GroundHeight(f.PositionXZ);
        }
        return changed;
    }

    private static double Average3x3(Heightfield hf, double[] h, int i, int j)
    {
        double sum = 0; int n = 0;
        for (int dj = -1; dj <= 1; dj++)
            for (int di = -1; di <= 1; di++)
            {
                int a = i + di, b = j + dj;
                if (a < 0 || b < 0 || a >= hf.Nx || b >= hf.Nz) continue;
                sum += h[hf.VertexIndex(a, b)]; n++;
            }
        return sum / n;
    }
}
