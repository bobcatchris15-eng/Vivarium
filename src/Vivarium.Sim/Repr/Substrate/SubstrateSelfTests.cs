using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.IO;
using System.Text;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

/// Shared helpers for the substrate self tests.
internal static class SubstrateTestKit
{
    public const ulong Seed = 7;
    public const double RainRate = 10e-3 / 3600.0;   // 10 mm/h in m/s

    public static LayeredSubstrate Cube(ulong seed = Seed) => new(SubstrateSpec.TestCube(seed));

    /// Deterministic pseudo-random point in the cube's box (x,z in [0,1), y in [0,0.7)).
    public static Vec3 HashPoint(ulong seed, int k) =>
        new(ReprId.Unit(ReprId.Hash(seed, (ulong)k), 1), 0.7 * ReprId.Unit(ReprId.Hash(seed, (ulong)k), 2), ReprId.Unit(ReprId.Hash(seed, (ulong)k), 3));

    public static Func<Vec2, double> Rain(double rate) => _ => rate;

    public static double MeanTopSat(LayeredSubstrate s, Func<Vec2, bool> pick)
    {
        double sum = 0; int n = 0;
        for (int c = 0; c < s.ColumnCount; c++)
        {
            if (!pick(s.ColumnCentre(c))) continue;
            sum += s.Water.TopSaturation(c); n++;
        }
        return n > 0 ? sum / n : 0;
    }

}

public sealed class SubstrateFractionsTest : IReprSelfTest
{
    public string Name => "substrate.fractions";
    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        int solid = 0; double worst = 0;
        for (int k = 0; k < 5000; k++)
        {
            var smp = s.SampleSubstrate(SubstrateTestKit.HashPoint(SubstrateTestKit.Seed, k));
            if (!smp.Solid) continue;
            solid++;
            double sum = smp.CoarseFragments + smp.Sand + smp.Silt + smp.Clay + smp.Organic;
            worst = Math.Max(worst, Math.Abs(sum - 1));
        }
        if (solid < 1000) return $"only {solid} solid samples of 5000";
        return worst > 1e-6 ? $"fractions sum off by {worst:E2}" : null;
    }
}

public sealed class SubstrateDeterminismTest : IReprSelfTest
{
    public string Name => "substrate.determinism";
    public string? Run()
    {
        var a = SubstrateTestKit.Cube(7); var b = SubstrateTestKit.Cube(7); var other = SubstrateTestKit.Cube(8);
        int diffOther = 0;
        for (int k = 0; k < 1000; k++)
        {
            var p = SubstrateTestKit.HashPoint(7, k);
            var sa = a.SampleSubstrate(p);
            if (!sa.Equals(b.SampleSubstrate(p))) return $"same seed differs at point {k}";
            if (!sa.Equals(other.SampleSubstrate(p))) diffOther++;
        }
        return diffOther == 0 ? "different seed produced identical samples" : null;
    }
}

public sealed class SubstrateHorizonOrderTest : IReprSelfTest
{
    public string Name => "substrate.horizon-order";
    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        for (int c = 0; c < s.ColumnCount; c++)
        {
            var xz = s.ColumnCentre(c);
            double surf = s.Height(xz);
            int prev = 0;
            for (double d = -0.02; d <= 0.9; d += 0.002)
            {
                var p = new Vec3(xz.X, surf - d, xz.Z);
                var smp = s.SampleSubstrate(p);
                if (!smp.Solid) continue;
                if (s.IsInStone(p)) continue;
                int h = (int)smp.Horizon;
                if (h < prev) return $"horizon decreased ({prev}->{h}) walking down column {c} at depth {d:0.000} m";
                prev = h;
            }
        }
        return null;
    }
}

public sealed class SubstrateContinuityTest : IReprSelfTest
{
    public string Name => "substrate.continuity";
    // Orchestrator decision (2026-11-13): steep boundaries are legitimate (a bedrock knoll rises at 60-70 deg),
    // stair steps are not. So test the SAMPLED function at 5 mm spacing: between adjacent samples a boundary
    // may move at most 2 cm (about 76 deg). A per-column step would show as a multi-cm jump at one sample.
    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        var b = s.Bounds;
        const double step = 0.005;
        for (int level = 1; level <= 4; level++)
        {
            for (double z = b.Min.Z + 0.03; z < b.Max.Z - 0.03; z += 0.05)
            {
                double prevD = double.NaN;
                for (double x = b.Min.X + 0.03; x < b.Max.X - 0.03; x += step)
                {
                    double d = s.HorizonBoundaryDepth(new Vec2(x, z), level);
                    if (!double.IsNaN(d) && !double.IsNaN(prevD) && Math.Abs(d - prevD) >= 0.02)
                        return $"boundary {level} steps {Math.Abs(d - prevD) * 100:0.00} cm within 5 mm at x={x:0.000}, z={z:0.00}";
                    prevD = d;
                }
            }
        }
        return null;
    }
}

public sealed class SubstrateOutcropTest : IReprSelfTest
{
    public string Name => "substrate.outcrop";
    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        for (int c = 0; c < s.ColumnCount; c++)
        {
            if (!s.IsOutcrop(c)) continue;
            var xz = s.ColumnCentre(c);
            double surf = s.Height(xz);
            var top = s.SampleSubstrate(new Vec3(xz.X, surf - 0.001, xz.Z));
            var deep = s.SampleSubstrate(new Vec3(xz.X, surf - 0.30, xz.Z));
            if (top.Horizon != Horizon.Bedrock) return $"outcrop column {c} top horizon is {top.Horizon}, not Bedrock";
            if (deep.Horizon != Horizon.Bedrock) return $"outcrop column {c} 30 cm down is {deep.Horizon}";
            if (!top.BeddingNormal.Equals(deep.BeddingNormal)) return "bedding normal differs between surface and depth";
            if (!top.MineralHue.Equals(deep.MineralHue)) return "mineral hue differs between surface and depth";
            return null;
        }
        double tmin = 1e9, tmax = -1e9, hmin = 1e9, hmax = -1e9;
        for (int c = 0; c < s.ColumnCount; c++) { double t = s.ColumnSoilThickness(c); tmin = Math.Min(tmin, t); tmax = Math.Max(tmax, t); double hh = s.Height(s.ColumnCentre(c)); hmin = Math.Min(hmin, hh); hmax = Math.Max(hmax, hh); }
        return $"no outcrop column in the test cube (soil {tmin:0.000}..{tmax:0.000} m, surface {hmin:0.000}..{hmax:0.000} m)";
    }
}

public sealed class SubstrateCausalTest : IReprSelfTest
{
    public string Name => "substrate.causal";
    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        double humusWet = 0, humusDry = 0, sandFree = 0, sandWet = 0; int nWet = 0, nDry = 0, nFree = 0, nWet2 = 0;
        var steep = new List<(double st, double thick)>();
        for (int c = 0; c < s.ColumnCount; c++)
        {
            var xz = s.ColumnCentre(c);
            double thick = s.ColumnSoilThickness(c);
            steep.Add((s.ColumnSteepness(c), thick));
            if (xz.X < 0.5) { humusWet += s.ColumnLayerThickness(c, 1); nWet++; }
            else { humusDry += s.ColumnLayerThickness(c, 1); nDry++; }
            double mh = s.ColumnLayerThickness(c, 2);
            if (mh > 0.01)
            {
                double dep = s.ColumnLayerThickness(c, 0) + s.ColumnLayerThickness(c, 1) + 0.5 * mh;
                var smp = s.SampleSubstrate(new Vec3(xz.X, s.Height(xz) - dep, xz.Z));
                if (smp.Horizon == Horizon.MineralSoil)
                {
                    if (xz.X >= 0.5) { sandFree += smp.Sand; nFree++; }
                    else { sandWet += smp.Sand; nWet2++; }
                }
            }
        }
        if (!(humusWet / nWet > humusDry / nDry)) return $"humus not thicker on wet/organic side ({humusWet / nWet * 100:0.0} vs {humusDry / nDry * 100:0.0} cm)";
        if (nFree == 0 || nWet2 == 0) return "no mineral samples to compare sand";
        if (!(sandFree / nFree > sandWet / nWet2)) return "sand not higher on the free-draining side";
        steep.Sort((a, b) => a.st.CompareTo(b.st));
        int dec = Math.Max(1, steep.Count / 10);
        double flat = 0, stp = 0;
        for (int k = 0; k < dec; k++) { flat += steep[k].thick; stp += steep[steep.Count - 1 - k].thick; }
        if (!(stp / dec < flat / dec)) return $"soil not thinner on steepest decile ({stp / dec * 100:0.0} vs {flat / dec * 100:0.0} cm)";
        return null;
    }
}

public sealed class SubstrateWaterBudgetTest : IReprSelfTest
{
    public string Name => "substrate.water-budget";
    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        var w = s.Water;
        double[] dts = { 0.5, 30, 600, 3600 };
        const int steps = 2000, rainSteps = 500;
        double satStart = SubstrateTestKit.MeanTopSat(s, _ => true), satRainEnd = 0;
        for (int k = 0; k < steps; k++)
        {
            double dt = dts[k % 4];
            var rate = k < rainSteps ? SubstrateTestKit.RainRate : 0.0;
            var b = w.Step(dt, SubstrateTestKit.Rain(rate), 1e-7, 2e-10);
            if (!b.Closed(1e-9 + 1e-9 * b.StoredAfter)) return $"budget not closed at step {k} (residual {b.Residual:E2} m3)";
            if (k == rainSteps - 1) satRainEnd = SubstrateTestKit.MeanTopSat(s, _ => true);
            for (int c = 0; c < s.ColumnCount; c++)
            {
                if (w.Standing[c] < 0 || w.Film[c] < 0) return $"negative surface water at step {k}, column {c}";
                for (int l = 0; l < 4; l++) if (w.Theta[4 * c + l] < 0) return $"negative soil water at step {k}, column {c}";
            }
        }
        double satEnd = SubstrateTestKit.MeanTopSat(s, _ => true);
        if (!(satRainEnd > satStart)) return "top-layer saturation did not rise during rain";
        if (!(satEnd < satRainEnd)) return "top-layer saturation did not fall after rain";
        return null;
    }
}

public sealed class SubstrateClosedDomainTest : IReprSelfTest
{
    public string Name => "substrate.water-closed";
    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        var w = s.Water;
        w.DeepDrainageEnabled = false; w.EdgeRunoffEnabled = false; w.SubsurfaceOutflowEnabled = false;
        w.EvaporationEnabled = false; w.TranspirationEnabled = false;
        // Put water on the surface first, then let it move with no outflow paths.
        for (int k = 0; k < 20; k++)
        {
            var b = w.Step(60, SubstrateTestKit.Rain(SubstrateTestKit.RainRate), 0, 0);
            if (!b.Closed(1e-9 + 1e-9 * b.StoredAfter)) return $"rain phase not closed at step {k}";
        }
        double total0 = w.Stored();
        for (int k = 0; k < 300; k++)
        {
            var b = w.Step(600, SubstrateTestKit.Rain(0), 0, 0);
            if (!b.Closed(1e-9)) return $"closed budget residual {b.Residual:E2} at step {k}";
        }
        double drift = Math.Abs(w.Stored() - total0);
        return drift > 1e-9 ? $"closed domain drifted by {drift:E2} m3" : null;
    }
}

public sealed class SubstrateWetDryTest : IReprSelfTest
{
    public string Name => "substrate.wet-dry";
    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        for (int k = 0; k < 60; k++) s.Water.Step(60, SubstrateTestKit.Rain(SubstrateTestKit.RainRate), 0, 0);
        double wet = SubstrateTestKit.MeanTopSat(s, p => p.X < 0.25);
        double dry = SubstrateTestKit.MeanTopSat(s, p => p.X > 0.75);
        return wet > dry ? null : $"wet side saturation {wet:0.000} not above dry side {dry:0.000}";
    }
}

public sealed class SubstrateDigTest : IReprSelfTest
{
    public string Name => "substrate.dig";
    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        const double r = 0.05;
        double surf = s.Height(new Vec2(0.5, 0.5));
        var c = new Vec3(0.5, surf, 0.5);
        var far = new List<Vec3>();
        for (int k = 0; k < 200; k++) far.Add(new Vec3(0.2, 0.25 + 0.002 * k, 0.1 + 0.004 * (k % 150)));
        var before = new SubstrateSample[far.Count];
        for (int k = 0; k < far.Count; k++) before[k] = s.SampleSubstrate(far[k]);

        // Dense bilinear heights before, to integrate the removed volume independently of the ledger.
        const int g = 200; double lo = 0.40, span = 0.20, dA = (span / g) * (span / g);
        var h0 = new double[g * g];
        for (int i = 0; i < g; i++) for (int j = 0; j < g; j++)
            h0[i * g + j] = s.Height(new Vec2(lo + (i + 0.5) * span / g, lo + (j + 0.5) * span / g));

        double ledgerBefore = s.Ledger.SolidTotal;
        s.Dig(c, r, 0);
        double ledgerRemoved = s.Ledger.SolidTotal - ledgerBefore;

        var recs = new List<DirtyRecord>();
        s.DirtyLog.Read(0, (DirtyDomain)63, recs);
        int digs = 0; DirtyRecord rec = default;
        foreach (var x in recs) if (x.Reason == "dig") { digs++; rec = x; }
        if (digs != 1) return $"expected exactly one dig record, got {digs}";
        double pad = s.Cell;
        if (rec.Bounds.Min.X < c.X - r - pad || rec.Bounds.Max.X > c.X + r + pad ||
            rec.Bounds.Min.Z < c.Z - r - pad || rec.Bounds.Max.Z > c.Z + r + pad ||
            rec.Bounds.Min.Y < c.Y - r - pad || rec.Bounds.Max.Y > c.Y + r + pad)
            return "dig record bounds exceed the sphere AABB padded by one cell";

        for (int k = 0; k < far.Count; k++)
            if (!s.SampleSubstrate(far[k]).Equals(before[k])) return $"sample 30 cm away changed at point {k}";

        double removed = 0;
        for (int i = 0; i < g; i++) for (int j = 0; j < g; j++)
        {
            double hn = s.Height(new Vec2(lo + (i + 0.5) * span / g, lo + (j + 0.5) * span / g));
            removed += Math.Max(0, h0[i * g + j] - hn) * dA;
        }
        if (removed <= 0) return "geometric removal is zero";
        double rel = Math.Abs(ledgerRemoved - removed) / removed;
        return rel > 0.02 ? $"ledger solid {ledgerRemoved:E3} m3 vs removed {removed:E3} m3 ({rel * 100:0.0}% off)" : null;
    }
}

public sealed class SubstrateRootTest : IReprSelfTest
{
    public string Name => "substrate.roots";
    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        // A non-outcrop column near (0.25, 0.5) as the root centre.
        int c0 = -1; double best = double.MaxValue;
        for (int c = 0; c < s.ColumnCount; c++)
        {
            if (s.IsOutcrop(c)) continue;
            var xz = s.ColumnCentre(c);
            double dd = (xz.X - 0.25) * (xz.X - 0.25) + (xz.Z - 0.5) * (xz.Z - 0.5);
            if (dd < best) { best = dd; c0 = c; }
        }
        if (c0 < 0) return "no soil column for the root test";
        var cc = s.ColumnCentre(c0);
        var centre = new Vec3(cc.X, s.Height(cc), cc.Z);
        const double metres = 3.0;
        s.AddRootDensity(centre, 0.1, 0.3, metres);
        double total = 0;
        const double st = 0.005, sz = 0.002;
        for (double x = centre.X - 0.15; x <= centre.X + 0.15; x += st)
            for (double z = centre.Z - 0.15; z <= centre.Z + 0.15; z += st)
            {
                double surf = s.Height(new Vec2(x, z));
                if (double.IsNaN(surf)) continue;
                for (double d = 0; d <= 0.5; d += sz)
                    total += s.SampleSubstrate(new Vec3(x, surf - d, z)).FineRootDensity * st * st * sz;
            }
        double rel = Math.Abs(total - metres) / metres;
        return rel > 0.01 ? $"root integral {total:0.000} m vs deposited {metres} m ({rel * 100:0.0}% off)" : null;
    }
}

public sealed class SubstratePerformanceTest : IReprSelfTest
{
    private string _label = "substrate.performance";
    public string Name => _label;
    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        float check = 0;
        for (int k = 0; k < 20000; k++) check += s.SampleSubstrate(SubstrateTestKit.HashPoint(3, k)).Porosity;
        var sw = Stopwatch.StartNew();
        long a0 = GC.GetAllocatedBytesForCurrentThread();
        for (int k = 0; k < 1_000_000; k++)
        {
            double x = (k * 0.6180339887) % 1.0, z = (k * 0.7548776662) % 1.0, y = 0.02 + 0.5 * ((k * 0.5698402909) % 1.0);
            check += s.SampleSubstrate(new Vec3(x, y, z)).Porosity;
        }
        double ns = sw.Elapsed.TotalMilliseconds * 1e6 / 1_000_000;
        long alloc = GC.GetAllocatedBytesForCurrentThread() - a0;
        var w = s.Water;
        w.Step(600, SubstrateTestKit.Rain(SubstrateTestKit.RainRate), 1e-7, 2e-10);   // warm-up
        var sw2 = Stopwatch.StartNew();
        for (int k = 0; k < 10; k++) w.Step(600, SubstrateTestKit.Rain(SubstrateTestKit.RainRate), 1e-7, 2e-10);
        double ms = sw2.Elapsed.TotalMilliseconds / 10;

        var big = SubstrateSpec.TestCube(7); big.Nx = 128; big.Nz = 128;
        var swc = Stopwatch.StartNew();
        var sb = new LayeredSubstrate(big);
        double build = swc.Elapsed.TotalMilliseconds;
        sb.Water.Step(600, SubstrateTestKit.Rain(SubstrateTestKit.RainRate), 1e-7, 2e-10);
        var sw3 = Stopwatch.StartNew();
        for (int k = 0; k < 5; k++) sb.Water.Step(600, SubstrateTestKit.Rain(SubstrateTestKit.RainRate), 1e-7, 2e-10);
        double msBig = sw3.Elapsed.TotalMilliseconds / 5;
        _label = $"substrate.performance [{ns:0} ns/call sample ({alloc} B alloc), {ms:0.00} ms/step 20x20 water, {msBig:0.0} ms/step 128x128 water dt=600 (build {build:0} ms)]";
        if (float.IsNaN(check)) return "checksum NaN";
        if (alloc != 0) return $"SampleSubstrate allocated {alloc} bytes";
        if (ns >= 400) return $"{ns:0} ns/call exceeds 400 ns";
        if (ms >= 5) return $"{ms:0.00} ms/step exceeds 5 ms (20x20)";
        if (msBig >= 40) return $"{msBig:0.0} ms/step exceeds 40 ms (128x128)";
        return null;
    }
}

public sealed class SubstrateProfileTest : IReprSelfTest
{
    public string Name => "substrate.profile";
    public string? Run()
    {
        try
        {
            var s = SubstrateTestKit.Cube();
            int wet = Column(s, 0, 10), dry = Column(s, s.Nx - 1, 10), outc = -1;
            for (int c = 0; c < s.ColumnCount; c++) if (s.IsOutcrop(c)) { outc = c; break; }
            var sb = new StringBuilder();
            sb.AppendLine("Substrate profile, test cube seed 7");
            Report(sb, s, "wet side", wet);
            Report(sb, s, "dry side", dry);
            if (outc >= 0) Report(sb, s, "outcrop", outc); else sb.AppendLine("outcrop: none found");
            var before = new double[s.ColumnCount * 4];
            for (int k = 0; k < before.Length; k++) before[k] = s.Water.Theta[k];
            for (int k = 0; k < 144; k++) s.Water.Step(600, SubstrateTestKit.Rain(2e-3 / 3600.0), 0, 0);
            sb.AppendLine();
            sb.AppendLine("After 24 h of uniform 2 mm/h rain (no evaporation): water content (volumetric) before -> after, water table depth, standing water");
            foreach (var (name, c) in new[] { ("wet side", wet), ("dry side", dry), ("outcrop", outc) })
            {
                if (c < 0) continue;
                var line = new StringBuilder($"  {name,-9}");
                for (int l = 0; l < 4; l++)
                    line.Append($"  L{l}: {before[4 * c + l]:0.000} -> {s.Water.Theta[4 * c + l]:0.000}");
                double wt = s.Water.WaterTableDepth(c);
                line.Append($"  | table depth {(double.IsNaN(wt) ? "none" : (wt * 100).ToString("0.0") + " cm")}, standing {s.Water.StandingAt(c) * 1000:0.00} mm, film {s.Water.FilmAt(c) * 1000:0.000} mm, top sat {s.Water.TopSaturation(c):0.00}");
                sb.AppendLine(line.ToString());
            }
            double maxSt = 0; int mc = 0;
            for (int c = 0; c < s.ColumnCount; c++) if (s.Water.StandingAt(c) > maxSt) { maxSt = s.Water.StandingAt(c); mc = c; }
            var p = s.ColumnCentre(mc);
            sb.AppendLine($"  deepest standing water {maxSt * 1000:0.0} mm at x={p.X:0.00} z={p.Z:0.00}; subsurface outflow last step {s.Water.LastSubsurfaceOutflow * 1000:0.000} L");
            string dir = "C:/Misc/Vivarium/build/overhaul/S1b";
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "profile.txt"), sb.ToString());
        }
        catch (Exception e)
        {
            Console.WriteLine($"substrate.profile: could not write profile ({e.Message})");
        }
        return null;
    }

    private static int Column(LayeredSubstrate s, int i, int j) => i * s.Nz + j;

    private static void Report(StringBuilder sb, LayeredSubstrate s, string name, int c)
    {
        var xz = s.ColumnCentre(c);
        sb.AppendLine();
        sb.AppendLine($"{name} (column {c}, x={xz.X:0.000} z={xz.Z:0.000}, surface {s.Height(xz):0.000} m, outcrop={s.IsOutcrop(c)})");
        sb.AppendLine("  horizon     top(cm)  thick(cm)  sand  silt  clay  organic  coarse  porosity  Ksat(m/s)");
        double top = 0;
        double surf = s.Height(xz);
        for (int l = 0; l < 4; l++)
        {
            double th = s.ColumnLayerThickness(c, l);
            double mid = top + 0.5 * th;
            var smp = s.SampleSubstrate(new Vec3(xz.X, surf - mid, xz.Z));
            sb.AppendLine($"  {(Horizon)(l + 1),-10} {top * 100,7:0.0} {th * 100,10:0.0}  {smp.Sand,4:0.00}  {smp.Silt,4:0.00}  {smp.Clay,4:0.00}  {smp.Organic,7:0.00}  {smp.CoarseFragments,6:0.00}  {smp.Porosity,8:0.000}  {smp.Permeability,9:0.0E0}");
            top += th;
        }
        sb.AppendLine($"  {"Bedrock",-10} {top * 100,7:0.0}          -");
    }
}

// ------------------------------------------------------------------------------------------------ S1b tests

public sealed class SubstrateContactTest : IReprSelfTest
{
    public string Name => "substrate.contact";
    /// The soil/rock contact is a continuous curve: no column staircase, sub-column resolution, no collar step.
    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        // 1. sub-column resolution: points where the continuous test and the column flag disagree
        int fine = 0, total = 0;
        for (double x = 0.002; x < 1; x += 0.0101)
            for (double z = 0.002; z < 1; z += 0.0101)
            {
                int c = s.ColumnAt(new Vec2(x, z));
                if (s.IsOutcropAt(new Vec2(x, z)) != s.IsOutcrop(c)) fine++;
                total++;
            }
        if (fine < 40) return $"only {fine} of {total} samples resolve the contact below column size";
        // 2. contour crossings on z lines are spread over the whole column width (a column staircase would pin them to column edges)
        int curves = 0; double sumFrac = 0; int nCross = 0; var bins = new int[5];
        double? prev = null;
        for (double z = 0.02; z < 0.98; z += 0.003)
        {
            double? cross = null;
            for (double x = 0.02; x < 0.98 && cross == null; x += 0.004)
            {
                bool a = s.IsOutcropAt(new Vec2(x, z)), b = s.IsOutcropAt(new Vec2(x + 0.004, z));
                if (a != b)
                {
                    double lo = x, hi = x + 0.004;
                    for (int i = 0; i < 30; i++) { double m = 0.5 * (lo + hi); if (s.IsOutcropAt(new Vec2(m, z)) == a) lo = m; else hi = m; }
                    cross = 0.5 * (lo + hi);
                }
            }
            if (cross != null)
            {
                double f = (cross.Value / s.Cell) - Math.Floor(cross.Value / s.Cell);
                sumFrac += f; nCross++; bins[Math.Min(4, (int)(f * 5))]++;
                if (prev != null) curves++;
            }
            prev = cross;
        }
        if (curves < 15) return $"contact contour found on only {curves} z lines";
        if (nCross < 30) return $"only {nCross} contact crossings";
        // A column staircase has columns that are all rock or all soil; a continuous contact cuts through many columns.
        int mixed = 0;
        for (int c = 0; c < s.ColumnCount; c++)
        {
            var cc = s.ColumnCentre(c); int rockN = 0;
            for (int i = 0; i < 7; i++) for (int j = 0; j < 7; j++)
                if (s.IsOutcropAt(new Vec2(cc.X + (i - 3) * s.Cell / 7.5, cc.Z + (j - 3) * s.Cell / 7.5))) rockN++;
            if (rockN >= 5 && rockN <= 44) mixed++;
        }
        if (mixed < 8) return $"only {mixed} columns are cut by the contact (staircase)";
        // 3. soil thins to a feather edge: thickness along a path into the soil grows from 0 without a step
        double worstStep = 0;
        for (double z = 0.05; z < 0.95; z += 0.05)
        {
            double tPrev = double.NaN;
            for (double x = 0.02; x < 0.98; x += 0.002)
            {
                double t = s.SoilThicknessAt(new Vec2(x, z));
                if (!double.IsNaN(tPrev)) worstStep = Math.Max(worstStep, Math.Abs(t - tPrev));
                tPrev = t;
            }
        }
        if (worstStep > 0.009) return $"soil thickness steps {worstStep * 100:0.00} cm within 2 mm";
        // 4. Height is continuous (<= 5 mm over 1 mm of travel)
        double worstH = 0;
        for (double z = 0.03; z < 0.97; z += 0.05)
        {
            double hPrev = s.Height(new Vec2(0.02, z));
            for (double x = 0.021; x < 0.98; x += 0.001)
            {
                double h = s.Height(new Vec2(x, z));
                worstH = Math.Max(worstH, Math.Abs(h - hPrev)); hPrev = h;
            }
        }
        return worstH > 0.005 ? $"Height jumps {worstH * 1000:0.0} mm within 1 mm" : null;
    }
}

public sealed class SubstrateDigBowlTest : IReprSelfTest
{
    public string Name => "substrate.dig-bowl";
    public string? Run()
    {
        // 12 cm sphere dug into ~20 cm of soil: round bowl, undisturbed horizons exposed, books close.
        var s = SubstrateTestKit.Cube();
        Vec2? site = null; double best = 1e9;
        for (double x = 0.1; x < 0.45; x += 0.01)
            for (double z = 0.15; z < 0.85; z += 0.01)
            {
                double t = s.SoilThicknessAt(new Vec2(x, z));
                if (Math.Abs(t - 0.20) < best) { best = Math.Abs(t - 0.20); site = new Vec2(x, z); }
            }
        var p = site!.Value;
        double thick = s.SoilThicknessAt(p);
        if (Math.Abs(thick - 0.20) > 0.03) return $"no 20 cm soil site (best {thick * 100:0.0} cm)";
        double r = 0.12, surf = s.Height(p);
        var cen = new Vec3(p.X, surf, p.Z);
        // expected floor material: the undisturbed horizon just below the sphere bottom
        var expected = s.SampleSubstrate(new Vec3(p.X, surf - r - 0.002, p.Z));

        const int gN = 200; double gSpan = 2 * r + 0.04, gLo = p.X - r - 0.02, gLo2 = p.Z - r - 0.02;
        var h0 = new double[gN * gN];
        for (int i = 0; i < gN; i++) for (int j = 0; j < gN; j++) h0[i * gN + j] = s.Height(new Vec2(gLo + (i + 0.5) * gSpan / gN, gLo2 + (j + 0.5) * gSpan / gN));
        s.AddRootDensity(new Vec3(p.X, surf, p.Z), 0.14, 0.2, 2.0);
        double rootsBefore = s.Ledger.RootMetres;
        s.Dig(cen, r, 0);
        double h = s.Height(p);
        if (Math.Abs(h - (surf - r)) > 1e-9) return $"bowl bottom {surf - h:0.0000} m, expected {r}";
        // round: height follows the sphere at any step, no stair steps
        double worst = 0, worstStep = 0, prevH = double.NaN;
        for (double d = -0.11; d <= 0.11; d += 0.001)
        {
            double hh = s.Height(new Vec2(p.X + d, p.Z));
            double sph = cen.Y - Math.Sqrt(r * r - d * d);
            worst = Math.Max(worst, Math.Abs(hh - sph));
            if (!double.IsNaN(prevH)) worstStep = Math.Max(worstStep, Math.Abs(hh - prevH));
            prevH = hh;
        }
        if (worst > 1e-9) return $"bowl deviates from the sphere by {worst:E2} m";
        if (worstStep > 0.0075) return $"bowl surface steps {worstStep * 1000:0.0} mm within 1 mm (stair stepping)";
        var floor = s.SampleSubstrate(new Vec3(p.X, h - 0.002, p.Z));
        if (!floor.Solid) return "floor sample is void";
        if (floor.Horizon == Horizon.Bedrock) return $"floor of a {r * 100:0} cm bowl in {thick * 100:0} cm of soil exposes bedrock";
        if (floor.Horizon != Horizon.MineralSoil && floor.Horizon != Horizon.Humus && floor.Horizon != Horizon.Weathered)
            return $"floor exposes {floor.Horizon}, not soil";
        if (floor.Horizon != expected.Horizon) return $"floor horizon {floor.Horizon} differs from the undisturbed profile at that depth ({expected.Horizon})";
        if (s.SampleSubstrate(new Vec3(p.X, surf - 0.05, p.Z)).Solid) return "bowl interior is not void";
        if (s.Ledger.RootMetres <= rootsBefore) return "no roots were removed by a dig through rooted soil";
        double removed = 0;
        for (int i = 0; i < gN; i++) for (int j = 0; j < gN; j++)
            removed += Math.Max(0, h0[i * gN + j] - s.Height(new Vec2(gLo + (i + 0.5) * gSpan / gN, gLo2 + (j + 0.5) * gSpan / gN))) * (gSpan / gN) * (gSpan / gN);
        double rel = Math.Abs(s.Ledger.SolidTotal - removed) / removed;
        if (!(removed > 0) || rel > 0.02) return $"ledger solid {s.Ledger.SolidTotal:E3} m3 vs integrated Height removal {removed:E3} m3 ({rel * 100:0.0}% off)";
        if (s.Ledger.SolidM3[3] <= 0) return "no mineral soil booked";
        return null;
    }
}

public sealed class SubstrateRain24Test : IReprSelfTest
{
    private string _detail = "";
    public string Name => "substrate.rain24" + _detail;
    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        var w = s.Water;
        var rate = SubstrateTestKit.Rain(2e-3 / 3600.0);
        for (int k = 0; k < 144; k++)
        {
            var b = w.Step(600, rate, 0, 0);
            if (!b.Closed(1e-9 + 1e-9 * b.StoredAfter)) return $"budget not closed at step {k}";
        }
        double wet = Mean(s, w, p => p.X < 0.25), dry = Mean(s, w, p => p.X > 0.75);
        double hollowMax = 0, dryMax = 0;
        for (int c = 0; c < s.ColumnCount; c++)
        {
            var p = s.ColumnCentre(c);
            double st = w.StandingAt(c) * 1000;
            if (Math.Abs(p.X - 0.5) < 0.15 && Math.Abs(p.Z - 0.5) < 0.15) hollowMax = Math.Max(hollowMax, st);
            if (p.X > 0.75) dryMax = Math.Max(dryMax, st);
        }
        _detail = $" [wet {wet:0.00} dry {dry:0.00}, hollow standing {hollowMax:0.0} mm, dry side max {dryMax:0.00} mm]";
        if (!(wet - dry >= 0.15)) return $"wet side top saturation {wet:0.000} exceeds dry side {dry:0.000} by only {wet - dry:0.000}";
        if (!(hollowMax > 2)) return $"standing water in the hollow only {hollowMax:0.00} mm";
        if (!(dryMax < 1)) return $"{dryMax:0.00} mm of standing water on the upper dry side";
        if (!(w.LastFluxes.Drainage < 0.01 * w.LastFluxes.Rain + 1e-9)) return "deep drainage through bedrock is not negligible";

        // Dry-down: hollow stays wetter than the ridge for 5 days of evaporation (4 mm/day).
        double evap = 4e-3 / 86400;
        var lines = new System.Text.StringBuilder();
        for (int day = 1; day <= 5; day++)
        {
            for (int k = 0; k < 144; k++)
            {
                var b = w.Step(600, SubstrateTestKit.Rain(0), evap, 0);
                if (!b.Closed(1e-9 + 1e-9 * b.StoredAfter)) return $"dry-down budget not closed on day {day}";
            }
            double hs = Mean(s, w, p => Math.Abs(p.X - 0.45) < 0.12 && Math.Abs(p.Z - 0.5) < 0.12);
            double rs = Mean(s, w, p => p.X > 0.75);
            double hw = ColumnWater(s, w, p => Math.Abs(p.X - 0.45) < 0.12 && Math.Abs(p.Z - 0.5) < 0.12);
            double rw = ColumnWater(s, w, p => p.X > 0.75);
            lines.Append($" d{day}:{hs:0.000}/{rs:0.000}");
            // The 0.6 cm litter layer reaches the evaporative floor on both sides within about a day (no capillary supply is modelled),
            // so after day 1 the top layer cannot separate them; the whole-profile saturation does.
            if (day == 1 ? !(hs > rs) : hs < rs - 0.01) return $"day {day}: hollow top saturation {hs:0.000} below ridge {rs:0.000}";
            if (!(hw > rw)) return $"day {day}: hollow profile saturation {hw:0.000} not above ridge {rw:0.000}";
        }
        _detail += " [hollow/ridge top sat" + lines + "]";
        return null;
    }

    internal static double Mean(LayeredSubstrate s, SubstrateWater w, Func<Vec2, bool> pick)
    {
        double sum = 0; int n = 0;
        for (int c = 0; c < s.ColumnCount; c++)
        {
            if (s.IsOutcrop(c) || !pick(s.ColumnCentre(c))) continue;
            sum += w.TopSaturation(c); n++;
        }
        return n > 0 ? sum / n : 0;
    }

    /// Mean whole-profile saturation (stored soil water over pore volume) of the soil columns picked.
    internal static double ColumnWater(LayeredSubstrate s, SubstrateWater w, Func<Vec2, bool> pick)
    {
        double sum = 0; int n = 0;
        for (int c = 0; c < s.ColumnCount; c++)
        {
            if (s.IsOutcrop(c) || !pick(s.ColumnCentre(c))) continue;
            double v = 0, cap = 0;
            for (int l = 0; l < 4; l++) { double th = s.ColumnLayerThickness(c, l); v += w.Theta[4 * c + l] * th; cap += s.ColPhi[4 * c + l] * th; }
            sum += cap > 0 ? v / cap : 0; n++;
        }
        return n > 0 ? sum / n : 0;
    }
}

public sealed class SubstrateDtInvarianceTest : IReprSelfTest
{
    private string _detail = "";
    public string Name => "substrate.dt-invariance" + _detail;
    public string? Run()
    {
        var rate = SubstrateTestKit.Rain(2e-3 / 3600.0);
        // A: 24 steps of 1 h.  B: a mix from 0.5 s to 1 h that adds up to exactly 24 h.
        var a = Totals(Enumerable.Repeat(3600.0, 24), rate, out string? ea);
        if (ea != null) return ea;
        var seq = new List<double>();
        double t = 0;
        for (int k = 0; k < 120; k++) { seq.Add(0.5); t += 0.5; }
        for (int k = 0; k < 60; k++) { seq.Add(30); t += 30; }
        for (int k = 0; k < 30; k++) { seq.Add(600); t += 600; }
        while (t + 3600 <= 86400 - 1e-6) { seq.Add(3600); t += 3600; }
        if (86400 - t > 1e-6) seq.Add(86400 - t);
        var b = Totals(seq, rate, out string? eb);
        if (eb != null) return eb;
        double rain = a.Rain;
        string[] names = { "stored", "subsurface outflow", "surface runoff", "evaporation" };
        double[] va = { a.Stored, a.Sub, a.Run, a.Evap }, vb = { b.Stored, b.Sub, b.Run, b.Evap };
        double worst = 0;
        for (int i = 0; i < 4; i++)
        {
            // within 2 % of the quantity (or of 5 % of the rain total for quantities that small)
            double scale = Math.Max(Math.Abs(va[i]), 0.05 * rain);
            double e = Math.Abs(va[i] - vb[i]) / scale;
            worst = Math.Max(worst, e);
            if (e > 0.02) return $"{names[i]}: dt=1h gives {va[i] * 1000:0.00} L, mixed dt gives {vb[i] * 1000:0.00} L ({e * 100:0.0}% off)";
        }
        _detail = $" [worst {worst * 100:0.00}% over stored/outflow/runoff/evap, {seq.Count} mixed steps 0.5 s..1 h]";
        return null;
    }

    private static (double Rain, double Stored, double Sub, double Run, double Evap) Totals(IEnumerable<double> dts, Func<Vec2, double> rate, out string? err)
    {
        err = null;
        var s = SubstrateTestKit.Cube();
        var w = s.Water;
        double rain = 0, sub = 0, run = 0, evap = 0;
        foreach (double dt in dts)
        {
            var b = w.Step(dt, rate, 2e-8, 0);
            if (!b.Closed(1e-9 + 1e-9 * b.StoredAfter)) { err = $"budget not closed at dt={dt}"; return default; }
            rain += w.LastFluxes.Rain; sub += w.LastSubsurfaceOutflow; run += w.LastFluxes.Runoff; evap += w.LastFluxes.Evaporation;
        }
        return (rain, w.Stored(), sub, run, evap);
    }
}

public sealed class SubstrateStonesTest : IReprSelfTest
{
    private string _detail = "";
    public string Name => "substrate.stones" + _detail;
    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        int breaking = 0, nearN = 0, farN = 0;
        double minSize = 1e9, maxSize = 0, minDepthFrac = 1e9, maxDepthFrac = 0;
        var brk = new List<Stone>();
        foreach (var st in s.StonesIn(s.Bounds))
        {
            var xz = new Vec2(st.Centre.X, st.Centre.Z);
            if (!(xz.X >= 0 && xz.X < 1 && xz.Z >= 0 && xz.Z < 1)) continue;
            double h = s.Height(xz), ve = LayeredSubstrate.VerticalExtent(st);
            if (!(st.Centre.Y + ve > h)) continue;
            breaking++;
            double len = 2 * st.Semi.X;
            minSize = Math.Min(minSize, len); maxSize = Math.Max(maxSize, len);
            double df = (h - st.Centre.Y) / ve;
            if (df > 0) { minDepthFrac = Math.Min(minDepthFrac, df); maxDepthFrac = Math.Max(maxDepthFrac, df); }
            brk.Add(st);
        }
        if (breaking < 5 || breaking > 60) return $"{breaking} stones break the top surface (want 5..60)";
        // density within 20 cm of the outcrop vs elsewhere, per area of soil
        var mask = new bool[100 * 100];
        for (int i = 0; i < 100; i++) for (int j = 0; j < 100; j++) mask[i * 100 + j] = s.IsOutcropAt(new Vec2((i + 0.5) * 0.01, (j + 0.5) * 0.01));
        bool Near(double x, double z)
        {
            int ci = Math.Clamp((int)(x / 0.01), 0, 99), cj = Math.Clamp((int)(z / 0.01), 0, 99);
            for (int i = Math.Max(0, ci - 20); i <= Math.Min(99, ci + 20); i++)
                for (int j = Math.Max(0, cj - 20); j <= Math.Min(99, cj + 20); j++)
                    if (mask[i * 100 + j] && (i - ci) * (i - ci) + (j - cj) * (j - cj) <= 400) return true;
            return false;
        }
        double areaNear = 0, areaFar = 0;
        for (int i = 0; i < 100; i++) for (int j = 0; j < 100; j++)
        {
            if (mask[i * 100 + j]) continue;
            if (Near((i + 0.5) * 0.01, (j + 0.5) * 0.01)) areaNear += 1e-4; else areaFar += 1e-4;
        }
        foreach (var st in brk) { if (Near(st.Centre.X, st.Centre.Z)) nearN++; else farN++; }
        double dn = nearN / Math.Max(areaNear, 1e-9), df2 = farN / Math.Max(areaFar, 1e-9);
        _detail = $" [{breaking} break the surface: {nearN} within 20 cm of rock ({dn:0} /m2) vs {farN} elsewhere ({df2:0} /m2); length {minSize * 100:0.0}..{maxSize * 100:0.0} cm, centre {minDepthFrac:0.00}..{maxDepthFrac:0.00} of vertical semi-axis below surface]";
        if (!(dn > df2)) return $"stones not denser near the outcrop ({dn:0.0} vs {df2:0.0} per m2)";
        if (maxDepthFrac > 0.9 + 1e-6 || minDepthFrac < 0.2 - 1e-6) return $"embedding {minDepthFrac:0.00}..{maxDepthFrac:0.00} outside 0.2..0.9";
        // size distribution is skewed: many small, few large (all stones, any depth)
        var sizes = new List<double>();
        foreach (var st in s.StonesIn(s.Bounds)) sizes.Add(2 * st.Semi.X);
        sizes.Sort();
        if (sizes.Count < 20) return "too few stones for a size distribution";
        double median = sizes[sizes.Count / 2], mean = 0; foreach (double z in sizes) mean += z; mean /= sizes.Count;
        if (!(mean > median)) return $"size distribution not right-skewed (mean {mean * 100:0.0} <= median {median * 100:0.0} cm)";
        if (sizes[0] < 0.0099 || sizes[^1] > 0.1201) return $"stone sizes {sizes[0] * 100:0.0}..{sizes[^1] * 100:0.0} cm outside 1..12 cm";
        return null;
    }
}
