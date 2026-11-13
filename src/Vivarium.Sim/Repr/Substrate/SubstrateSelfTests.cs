using System;
using System.Collections.Generic;
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
    public string? Run()
    {
        var s = SubstrateTestKit.Cube();
        for (int level = 1; level <= 4; level++)
        {
            for (int j = 0; j < s.Nz; j++)
            {
                double prevD = double.NaN, prev2D = double.NaN; Vec2 prevXZ = default;
                for (int i = 0; i < s.Nx; i++)
                {
                    int c = i * s.Nz + j;
                    if (s.IsOutcrop(c)) { prevD = double.NaN; prev2D = double.NaN; continue; }
                    var xz = s.ColumnCentre(c);
                    double d = s.HorizonBoundaryDepth(xz, level);
                    // Orchestrator decision (2026-11-13): a dipping boundary is legitimate, a stair step is not.
                    // Bound the slope loosely (3 cm per 5 cm column) and the SECOND difference (2 cm; bedrock knolls bend ~1 cm per column).
                    if (!double.IsNaN(d) && !double.IsNaN(prevD) && Math.Abs(d - prevD) >= 0.03)
                        return $"boundary {level} jumps {Math.Abs(d - prevD) * 100:0.00} cm between columns at x={prevXZ.X:0.00}..{xz.X:0.00}, z={xz.Z:0.00}";
                    if (!double.IsNaN(d) && !double.IsNaN(prevD) && !double.IsNaN(prev2D) && Math.Abs(d - 2 * prevD + prev2D) >= 0.02)
                        return $"boundary {level} kinks {Math.Abs(d - 2 * prevD + prev2D) * 100:0.00} cm at x={prevXZ.X:0.00}, z={xz.Z:0.00}";
                    prev2D = prevD; prevD = d; prevXZ = xz;
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
        w.DeepDrainageEnabled = false; w.EdgeRunoffEnabled = false;
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
        for (int k = 0; k < 1_000_000; k++)
        {
            double x = (k * 0.6180339887) % 1.0, z = (k * 0.7548776662) % 1.0, y = 0.02 + 0.5 * ((k * 0.5698402909) % 1.0);
            check += s.SampleSubstrate(new Vec3(x, y, z)).Porosity;
        }
        double ns = sw.Elapsed.TotalMilliseconds * 1e6 / 1_000_000;
        var w = s.Water;
        var sw2 = Stopwatch.StartNew();
        for (int k = 0; k < 10; k++) w.Step(600, SubstrateTestKit.Rain(SubstrateTestKit.RainRate), 1e-7, 2e-10);
        double ms = sw2.Elapsed.TotalMilliseconds / 10;
        _label = $"substrate.performance [{ns:0} ns/call sample, {ms:0.00} ms/step 20x20 water]";
        if (float.IsNaN(check)) return "checksum NaN";
        return ns < 400 ? null : $"{ns:0} ns/call exceeds 400 ns";
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
            sb.AppendLine("Substrate profile, test cube seed 7, 1 h of 10 mm/h rain");
            Report(sb, s, "wet side", wet);
            Report(sb, s, "dry side", dry);
            if (outc >= 0) Report(sb, s, "outcrop", outc); else sb.AppendLine("outcrop: none found");
            // Simulate 1 h of rain once and report before/after water content per column.
            var before = new double[s.ColumnCount * 4];
            for (int k = 0; k < before.Length; k++) before[k] = s.Water.Theta[k];
            s.Water.Step(3600, SubstrateTestKit.Rain(SubstrateTestKit.RainRate), 0, 0);
            sb.AppendLine();
            sb.AppendLine("Water content (volumetric) before -> after 1 h rain:");
            foreach (var (name, c) in new[] { ("wet side", wet), ("dry side", dry), ("outcrop", outc) })
            {
                if (c < 0) continue;
                var line = new StringBuilder($"  {name,-9}");
                for (int l = 0; l < 4; l++)
                    line.Append($"  L{l}: {before[4 * c + l]:0.000} -> {s.Water.Theta[4 * c + l]:0.000}");
                sb.AppendLine(line.ToString());
            }
            string dir = "C:/Misc/Vivarium/build/overhaul/S1";
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
