using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

/// Shared helpers for the colony self tests. Water is stepped alongside colonies; rain and evaporation in m/day.
internal static class ColonyTestKit
{
    public const double Dt = 21600;     // 6 h colony step
    public const double Rain = 3e-3;    // m/day
    public const double Evap = 2e-3;    // m/day
    public static readonly Func<Vec3, double> LightFn = _ => 0.6;

    public static readonly Vec2[] Grid12 = BuildGrid();

    private static Vec2[] BuildGrid()
    {
        var a = new List<Vec2>();
        foreach (double x in new[] { 0.125, 0.375, 0.625, 0.875 })
            foreach (double z in new[] { 0.2, 0.5, 0.8 })
                a.Add(new Vec2(x, z));
        return a.ToArray();
    }

    public static LayeredSubstrate Cube() => new(SubstrateSpec.TestCube(7));

    public sealed class Timing
    {
        public double Ms;
        public int Steps;
        public double MsPerStep => Steps > 0 ? Ms / Steps : 0;
    }

    public static string? Closed(Budget b) =>
        Math.Abs(b.Residual) <= 1e-9 * Math.Max(1.0, Math.Abs(b.StoredAfter)) ? null
        : $"budget {b.Quantity} not closed: residual {b.Residual:E3} (in {b.In:E3}, out {b.Out:E3})";

    /// The cube's drainage barely changes top-layer saturation under uniform rain, so the wet/dry contrast is
    /// driven by the rain field: 3 mm/day on the wet half, 0.3 mm/day on the dry half.
    public static readonly Func<Vec2, double> RainGradient = p => p.X < 0.5 ? 3e-3 : 0.3e-3;

    /// Advances water and colonies together. Returns the first budget failure, or null.
    public static string? Advance(ColonySystem c, LayeredSubstrate s, double seconds, double rain, double evap, double dt, Timing? timing = null) =>
        Advance(c, s, seconds, _ => rain, evap, dt, timing);

    public static string? Advance(ColonySystem c, LayeredSubstrate s, double seconds, Func<Vec2, double> rain, double evap, double dt, Timing? timing = null)
    {
        double t = 0;
        var sw = new Stopwatch();
        while (t < seconds - 1e-6)
        {
            double h = Math.Min(dt, seconds - t);
            s.Water.Step(h, p => rain(p) / ColonySystem.Day, evap / ColonySystem.Day, 0);
            sw.Restart();
            var b = c.Step(h);
            sw.Stop();
            if (timing != null) { timing.Ms += sw.Elapsed.TotalMilliseconds; timing.Steps++; }
            string? err = Closed(b);
            if (err != null) return err;
            t += h;
        }
        return null;
    }

    public static void SeedGrid(ColonySystem c, string species, double radius)
    {
        foreach (var p in Grid12) c.Seed(species, p, radius);
    }

    public static double Area(ColonySystem c, Func<string, bool> speciesPick, Func<Vec2, bool> cellPick)
    {
        double a = 0, cellArea = c.Field.CellSize * c.Field.CellSize;
        foreach (var t in c.Field.Tiles)
            for (int cell = 0; cell < ColonyTile.N; cell++)
            {
                ColonyField.CellCoords(t, cell, out int ix, out int iz);
                if (!cellPick(c.Field.CellCentre(ix, iz))) continue;
                for (int slot = 0; slot < 2; slot++)
                {
                    int k = cell * 2 + slot, sid = t.Species[k];
                    if (sid != 0 && speciesPick(c.Species[sid - 1].Id)) a += t.Coverage[k] * cellArea;
                }
            }
        return a;
    }

    /// Rock = the surface the colony actually samples is bedrock (covers outcrops and columns with a sliver of soil).
    public static bool IsRock(LayeredSubstrate s, Vec2 p)
    {
        double h = s.Height(p);
        if (!double.IsFinite(h)) return false;
        return s.SampleSubstrate(new Vec3(p.X, h - 1e-4, p.Z)).Horizon == Horizon.Bedrock;
    }

    /// Same moisture rule the colony uses (top-layer saturation plus film and standing water).
    public static double Moisture(LayeredSubstrate s, int col)
    {
        if (col < 0) return 0;
        double sat = s.Water.TopSaturation(col);
        double wet = (s.Water.Film[col] + s.Water.Standing[col]) / 0.0002;
        return MathD.Clamp01(sat + 0.7 * MathD.Clamp01(wet));
    }

    public static double MossCover(ColonyCellSample smp, string id)
    {
        double sum = 0;
        foreach (var o in smp.Occupants) if (o.SpeciesId == id) sum += o.Coverage;
        return sum;
    }

    /// Plain-text PPM (P3), 1 cell = 8 px. Green = moss, red = lichens, blue = litter.
    public static string Ppm(ColonySystem c)
    {
        const int Cells = 50, Scale = 8;
        int size = Cells * Scale;
        var sb = new StringBuilder();
        sb.Append("P3\n").Append(size).Append(' ').Append(size).Append("\n255\n");
        for (int py = 0; py < size; py++)
        {
            for (int px = 0; px < size; px++)
            {
                double r = 0, g = 0, b = 0;
                var t = c.Field.FindCell(px / Scale, py / Scale, out int cell);
                if (t != null)
                    for (int slot = 0; slot < 2; slot++)
                    {
                        int k = cell * 2 + slot, sid = t.Species[k];
                        if (sid == 0) continue;
                        var kind = c.Species[sid - 1].Kind;
                        double cov = t.Coverage[k];
                        if (kind == ColonyKind.Moss) g += cov;
                        else if (kind == ColonyKind.CrustoseLichen || kind == ColonyKind.FolioseLichen) r += cov;
                        else if (kind == ColonyKind.Litter) b += cov;
                    }
                sb.Append(To255(r)).Append(' ').Append(To255(g)).Append(' ').Append(To255(b)).Append(' ');
            }
            sb.Append('\n');
        }
        return sb.ToString();
    }

    private static int To255(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);

    public sealed class NicheRun
    {
        public ColonySystem C = null!;
        public LayeredSubstrate S = null!;
        public readonly Dictionary<string, string> Ppm = new();
        public readonly Timing Timing = new();
        public string? Error;
        public double SteadyMs;
        public double SteadyFraction;
    }

    /// Two-year cube run with moss and crust seeded at the 12 grid points. Shared by niche, cost and snapshot tests.
    public static readonly Lazy<NicheRun> Niche = new(BuildNiche);

    private static NicheRun BuildNiche()
    {
        var r = new NicheRun { S = Cube() };
        r.C = new ColonySystem(r.S, 7, LightFn);
        SeedGrid(r.C, "cushion_moss", 0.04);
        SeedGrid(r.C, "crust_lichen", 0.04);
        r.Error = Advance(r.C, r.S, 182.5 * ColonySystem.Day, RainGradient, Evap, Dt, r.Timing);
        if (r.Error != null) return r;
        r.Ppm["coverage_year0.5.pgm"] = Ppm(r.C);
        r.Error = Advance(r.C, r.S, 182.5 * ColonySystem.Day, RainGradient, Evap, Dt, r.Timing);
        if (r.Error != null) return r;
        r.Ppm["coverage_year1.pgm"] = Ppm(r.C);
        r.Error = Advance(r.C, r.S, 365 * ColonySystem.Day, RainGradient, Evap, Dt, r.Timing);
        if (r.Error != null) return r;
        r.Ppm["coverage_year2.pgm"] = Ppm(r.C);

        // Steady timing: colony step only, water held fixed.
        var st = new Timing();
        for (int i = 0; i < 50; i++)
        {
            var sw = Stopwatch.StartNew();
            r.C.Step(Dt);
            sw.Stop();
            st.Ms += sw.Elapsed.TotalMilliseconds; st.Steps++;
        }
        r.SteadyMs = st.MsPerStep;
        int occ = 0, steady = 0;
        foreach (var t in r.C.Field.Tiles)
            for (int cell = 0; cell < ColonyTile.N; cell++)
            {
                if (t.Species[cell * 2] == 0 && t.Species[cell * 2 + 1] == 0) continue;
                occ++;
                if (t.Steady[cell]) steady++;
            }
        r.SteadyFraction = occ > 0 ? (double)steady / occ : 0;
        return r;
    }
}

public sealed class ColonyDeterminismTest : IReprSelfTest
{
    public string Name => "colony.determinism";
    public string? Run()
    {
        var s1 = ColonyTestKit.Cube(); var c1 = new ColonySystem(s1, 7, ColonyTestKit.LightFn);
        var s2 = ColonyTestKit.Cube(); var c2 = new ColonySystem(s2, 7, ColonyTestKit.LightFn);
        foreach (var c in new[] { c1, c2 }) c.Seed("cushion_moss", new Vec2(0.4, 0.5), 0.04);
        string? e1 = ColonyTestKit.Advance(c1, s1, 30 * ColonySystem.Day, ColonyTestKit.Rain, ColonyTestKit.Evap, ColonyTestKit.Dt);
        string? e2 = ColonyTestKit.Advance(c2, s2, 30 * ColonySystem.Day, ColonyTestKit.Rain, ColonyTestKit.Evap, ColonyTestKit.Dt);
        if (e1 != null) return e1;
        if (e2 != null) return e2;
        if (c1.Stored() != c2.Stored()) return $"stored differs: {c1.Stored():R} vs {c2.Stored():R}";
        if (c1.CoveredArea("cushion_moss") != c2.CoveredArea("cushion_moss")) return "covered area differs";
        if (c1.Field.Tiles.Count != c2.Field.Tiles.Count) return "tile counts differ";
        return null;
    }
}

public sealed class ColonyBudgetTest : IReprSelfTest
{
    public string Name => "colony.budget";
    public string? Run()
    {
        var s = ColonyTestKit.Cube();
        var c = new ColonySystem(s, 11, ColonyTestKit.LightFn);
        c.Seed("cushion_moss", new Vec2(0.3, 0.5), 0.05);
        c.Seed("crust_lichen", new Vec2(0.7, 0.5), 0.05);
        double[] dts = { 600, 3600, 21600 };
        for (int i = 0; i < 2000; i++)
        {
            double dt = dts[(int)(ReprId.Hash(11, (ulong)i) % 3)];
            s.Water.Step(dt, _ => ColonyTestKit.Rain / ColonySystem.Day, ColonyTestKit.Evap / ColonySystem.Day, 0);
            if (i == 500) c.DepositLitter(new Vec2(0.5, 0.5), 0.2, 0.5);
            if (i == 1200) c.Seed("feather_moss", new Vec2(0.8, 0.2), 0.03);
            var b = c.Step(dt);
            string? err = ColonyTestKit.Closed(b);
            if (err != null) return $"step {i}: {err}";
            if (i % 100 == 0 || i == 1999) { string? bad = Check(c); if (bad != null) return $"step {i}: {bad}"; }
        }
        return null;
    }

    private static string? Check(ColonySystem c)
    {
        foreach (var t in c.Field.Tiles)
            for (int cell = 0; cell < ColonyTile.N; cell++)
            {
                double sum = 0;
                for (int slot = 0; slot < 2; slot++)
                {
                    int k = cell * 2 + slot;
                    if (t.Biomass[k] < -1e-12) return $"negative biomass {t.Biomass[k]:E3}";
                    if (t.Coverage[k] < -1e-12) return $"negative coverage {t.Coverage[k]:E3}";
                    sum += t.Coverage[k];
                }
                if (sum > 1 + 1e-6) return $"cell coverage sum {sum:0.000000} exceeds 1";
            }
        return null;
    }
}

public sealed class ColonyDtInvarianceTest : IReprSelfTest
{
    public string Name => "colony.dtInvariance";
    public string? Run()
    {
        var s1 = ColonyTestKit.Cube(); var c1 = new ColonySystem(s1, 7, ColonyTestKit.LightFn);
        var s2 = ColonyTestKit.Cube(); var c2 = new ColonySystem(s2, 7, ColonyTestKit.LightFn);
        c1.Seed("cushion_moss", new Vec2(0.4, 0.5), 0.04);
        c2.Seed("cushion_moss", new Vec2(0.4, 0.5), 0.04);
        string? e1 = ColonyTestKit.Advance(c1, s1, 30 * ColonySystem.Day, ColonyTestKit.Rain, ColonyTestKit.Evap, 3600);
        string? e2 = ColonyTestKit.Advance(c2, s2, 30 * ColonySystem.Day, ColonyTestKit.Rain, ColonyTestKit.Evap, 600);
        if (e1 != null) return e1;
        if (e2 != null) return e2;
        double b1 = c1.Stored(), b2 = c2.Stored(), a1 = c1.CoveredArea("cushion_moss"), a2 = c2.CoveredArea("cushion_moss");
        if (b1 <= 0 || a1 <= 0) return "no growth in the 3600 s run";
        double rb = Math.Abs(b1 - b2) / b1, ra = Math.Abs(a1 - a2) / a1;
        if (rb > 0.02) return $"biomass differs {rb * 100:0.00}% (3600 s x1: {b1:E4}, 600 s x6: {b2:E4})";
        if (ra > 0.02) return $"covered area differs {ra * 100:0.00}% (3600 s x1: {a1:0.00000}, 600 s x6: {a2:0.00000})";
        return null;
    }
}

public sealed class ColonyNicheTest : IReprSelfTest
{
    private string _detail = "";
    public string Name => "colony.niche" + _detail;
    public string? Run()
    {
        var r = ColonyTestKit.Niche.Value;
        if (r.Error != null) return r.Error;
        var c = r.C; var s = r.S;
        double wet = ColonyTestKit.Area(c, id => id == "cushion_moss", p => p.X < 0.5);
        double dry = ColonyTestKit.Area(c, id => id == "cushion_moss", p => p.X >= 0.5);
        if (!(wet > 3 * dry)) return $"moss wet {wet:0.0000} m2 not > 3x dry {dry:0.0000} m2";
        double rock = ColonyTestKit.Area(c, id => id == "crust_lichen" || id == "leaf_lichen", p => ColonyTestKit.IsRock(s, p));
        double humus = ColonyTestKit.Area(c, id => id == "crust_lichen" || id == "leaf_lichen", p => !ColonyTestKit.IsRock(s, p));
        _detail = $" [moss wet/dry={wet / Math.Max(dry, 1e-9):0.00} lichen rock/humus={rock / Math.Max(humus, 1e-9):0.00}]";
        if (!(rock > 5 * humus)) return $"lichen on rock {rock:0.0000} m2 not > 5x humus {humus:0.0000} m2";

        // Damp rock: rock whose moisture (same rule as the colony) is >= 0.5 at the end of the run.
        double mossDamp = 0, lichDamp = 0; int damp = 0;
        for (int ix = 0; ix < 50; ix++)
            for (int iz = 0; iz < 50; iz++)
            {
                var p = c.Field.CellCentre(ix, iz);
                if (!ColonyTestKit.IsRock(s, p)) continue;
                if (ColonyTestKit.Moisture(s, s.ColumnAt(p)) < 0.5) continue;
                damp++;
                var smp = c.Sample(p);
                mossDamp += ColonyTestKit.MossCover(smp, "cushion_moss");
                lichDamp += ColonyTestKit.MossCover(smp, "crust_lichen") + ColonyTestKit.MossCover(smp, "leaf_lichen");
            }
        if (damp == 0) return "no damp rock (moisture >= 0.5) at end of run: the substrate water model gives bare rock only film water (< 0.2 mm), so the damp-rock clause cannot be measured here";
        if (!(mossDamp > lichDamp)) return $"on damp rock moss {mossDamp:0.00} does not dominate lichen {lichDamp:0.00}";
        return null;
    }
}

public sealed class ColonyRouteTest : IReprSelfTest
{
    private string _detail = "";
    public string Name => "colony.route" + _detail;
    public string? Run()
    {
        var s = ColonyTestKit.Cube();
        // Shaded understory light (0.25 = moss optimum) so the front moves at a measurable rate in 240 days.
        var c = new ColonySystem(s, 7, _ => 0.25);
        const double x0 = 0.4;
        c.Seed("cushion_moss", new Vec2(x0, 0.5), 0.03);
        string? err = ColonyTestKit.Advance(c, s, 240 * ColonySystem.Day, ColonyTestKit.RainGradient, ColonyTestKit.Evap, ColonyTestKit.Dt);
        if (err != null) return err;
        double west = 0, east = 0;
        foreach (var t in c.Field.Tiles)
            for (int cell = 0; cell < ColonyTile.N; cell++)
            {
                if (t.Species[cell * 2] != 1 && t.Species[cell * 2 + 1] != 1) continue;
                ColonyField.CellCoords(t, cell, out int ix, out int iz);
                double x = c.Field.CellCentre(ix, iz).X;
                for (int slot = 0; slot < 2; slot++)
                {
                    int k = cell * 2 + slot;
                    if (t.Species[k] != 1 || t.Coverage[k] < 0.05) continue;
                    if (x < x0) west = Math.Max(west, x0 - x); else east = Math.Max(east, x - x0);
                }
            }
        _detail = $" [advance wet/dry={west / Math.Max(east, 1e-9):0.00} ({west:0.000} m vs {east:0.000} m)]";
        if (!(west > 2 * east)) return $"front advance wet/hospitable {west:0.000} m not > 2x dry side {east:0.000} m";
        return null;
    }
}

public sealed class ColonyMergeTest : IReprSelfTest
{
    public string Name => "colony.merge";
    public string? Run()
    {
        var s = ColonyTestKit.Cube();
        var c = new ColonySystem(s, 7, ColonyTestKit.LightFn);
        // Both nuclei on the wet half, 20 cm apart.
        c.Seed("cushion_moss", new Vec2(0.21, 0.5), 0.03);
        c.Seed("cushion_moss", new Vec2(0.41, 0.5), 0.03);
        // Run until the nuclei have joined AND the seam between them has filled (both within 2 years).
        bool joined = false, seamFilled = false;
        double seamMin = 0, seamInterior = 0;
        for (int day = 0; day <= 730 && !seamFilled; day += 10)
        {
            if (day > 0)
            {
                string? err = ColonyTestKit.Advance(c, s, 10 * ColonySystem.Day, ColonyTestKit.RainGradient, ColonyTestKit.Evap, ColonyTestKit.Dt);
                if (err != null) return err;
            }
            joined = c.ExtractPatches("cushion_moss").Count == 1;
            if (!joined) continue;
            (seamMin, seamInterior) = SeamStats(c);
            seamFilled = seamInterior > 0 && seamMin >= 0.9 * seamInterior;
        }
        if (!joined) return "seeds did not join into one patch within 2 years";
        foreach (double x in new[] { 0.21, 0.41 })
            if (ColonyTestKit.MossCover(c.Sample(new Vec2(x, 0.5)), "cushion_moss") < 0.5)
                return $"seed nucleus at x={x} died; merge would be spurious";
        if (!seamFilled) return $"seam not filled within 2 years: line min {seamMin:0.000} vs 0.9 x interior {seamInterior:0.000}";

        // Live moss only: litter from dieback is capped by its own coverage rule (0.5 x mass), not MaxBiomass.
        foreach (var t in c.Field.Tiles)
            for (int cell = 0; cell < ColonyTile.N; cell++)
                for (int slot = 0; slot < 2; slot++)
                {
                    int k = cell * 2 + slot, sid = t.Species[k];
                    if (sid == 0 || t.Coverage[k] <= 0 || c.Species[sid - 1].Kind == ColonyKind.Litter) continue;
                    if (t.Biomass[k] / t.Coverage[k] > c.Species[sid - 1].MaxBiomass * (1 + 1e-6))
                        return "a live cell's intensity exceeds MaxBiomass after merge";
                }
        return null;
    }

    /// Minimum moss coverage on the line between the seeds, and the mean coverage of fully interior cells.
    private static (double LineMin, double Interior) SeamStats(ColonySystem c)
    {
        double interiorSum = 0; int interiorN = 0;
        foreach (var t in c.Field.Tiles)
            for (int cell = 0; cell < ColonyTile.N; cell++)
            {
                ColonyField.CellCoords(t, cell, out int ix, out int iz);
                if (Moss(t, cell) <= 0.5) continue;
                bool inner = true;
                for (int dx = -1; dx <= 1 && inner; dx++)
                    for (int dz = -1; dz <= 1 && inner; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        var nt = c.Field.FindCell(ix + dx, iz + dz, out int nc);
                        if (nt == null || Moss(nt, nc) <= 0.5) inner = false;
                    }
                if (inner) { interiorSum += Moss(t, cell); interiorN++; }
            }
        if (interiorN == 0) return (0, 0);
        double interior = interiorSum / interiorN;

        // Line between the seeds (x 0.21..0.41 at z 0.5): cells ix 10..20 on row iz 25.
        double minLine = double.MaxValue;
        for (int ix = 10; ix <= 20; ix++)
        {
            var t = c.Field.FindCell(ix, 25, out int cell);
            double v = t == null ? 0 : Moss(t, cell);
            minLine = Math.Min(minLine, v);
        }
        return (minLine, interior);
    }

    private static double Moss(ColonyTile t, int cell)
    {
        double v = 0;
        for (int slot = 0; slot < 2; slot++)
            if (t.Species[cell * 2 + slot] == 1) v += t.Coverage[cell * 2 + slot];
        return v;
    }
}

public sealed class ColonyIrregularTest : IReprSelfTest
{
    public string Name => "colony.irregular";
    public string? Run()
    {
        // Flat, uniform habitat (no rock nearby) so the only shape cues come from the rules and the hash jitter.
        var spec = new SubstrateSpec
        {
            Seed = 7, Nx = 40, Nz = 40, Cell = 0.05, Depth = 1.0,
            SurfaceHeight = _ => 0.5, DrainagePotential = _ => 0.5, OrganicInput = _ => 0.5,
        };
        var s = new LayeredSubstrate(spec);
        Vec2? site = null;
        for (double x = 0.5; x <= 1.5 && site == null; x += 0.05)
            for (double z = 0.5; z <= 1.5 && site == null; z += 0.05)
            {
                var p = new Vec2(x, z);
                bool clear = true;
                for (int col = 0; col < s.ColumnCount && clear; col++)
                    if (s.IsOutcrop(col) && Vec2.Distance(s.ColumnCentre(col), p) < 0.35) clear = false;
                if (clear) site = p;
            }
        if (site == null) return "no outcrop-free site for the uniform habitat";
        var o = site.Value;

        var c = new ColonySystem(s, 7, ColonyTestKit.LightFn);
        c.Seed("cushion_moss", o, 0.02);
        string? err = ColonyTestKit.Advance(c, s, 60 * ColonySystem.Day, ColonyTestKit.Rain, ColonyTestKit.Evap, ColonyTestKit.Dt);
        if (err != null) return err;

        var radii = new double[64];
        for (int i = 0; i < 64; i++)
        {
            double th = 2 * Math.PI * i / 64, rr = 0;
            for (double d = 0.002; d <= 0.3; d += 0.002)
            {
                var q = new Vec2(o.X + d * Math.Cos(th), o.Z + d * Math.Sin(th));
                if (ColonyTestKit.MossCover(c.Sample(q), "cushion_moss") > 0.5) rr = d; else break;
            }
            radii[i] = rr;
        }
        double axis = (radii[0] + radii[16] + radii[32] + radii[48]) / 4;
        double diag = (radii[8] + radii[24] + radii[40] + radii[56]) / 4;
        if (axis <= 0) return "no spread after 60 days";
        if (Math.Abs(diag - axis) / axis >= 0.15) return $"axis radius {axis:0.000} m vs diagonal {diag:0.000} m differ >= 15%";
        double mean = 0;
        foreach (double r in radii) mean += r;
        mean /= radii.Length;
        double var2 = 0;
        foreach (double r in radii) var2 += (r - mean) * (r - mean);
        double cv = Math.Sqrt(var2 / radii.Length) / mean;
        if (cv < 0.02 || cv > 0.25) return $"perimeter radius CV {cv * 100:0.0}% outside 2-25%";
        return null;
    }
}

public sealed class ColonyDroughtTest : IReprSelfTest
{
    public string Name => "colony.drought";
    public string? Run()
    {
        var s = ColonyTestKit.Cube();
        var c = new ColonySystem(s, 7, ColonyTestKit.LightFn);
        ColonyTestKit.SeedGrid(c, "cushion_moss", 0.04);
        ColonyTestKit.SeedGrid(c, "crust_lichen", 0.04);
        string? err = ColonyTestKit.Advance(c, s, 365 * ColonySystem.Day, ColonyTestKit.RainGradient, ColonyTestKit.Evap, ColonyTestKit.Dt);
        if (err != null) return err;
        double mossB0 = c.Biomass("cushion_moss"), crustB0 = c.Biomass("crust_lichen");
        if (mossB0 <= 0 || crustB0 <= 0) return "no biomass before the drought";

        err = ColonyTestKit.Advance(c, s, 60 * ColonySystem.Day, _ => 0.0, 6e-3, ColonyTestKit.Dt);
        if (err != null) return err;
        bool dormant = false;
        foreach (var t in c.Field.Tiles)
            for (int k = 0; k < 2 * ColonyTile.N; k++)
                if (t.Species[k] == 1 && t.Dormant[k]) dormant = true;
        double mossB1 = c.Biomass("cushion_moss"), crustB1 = c.Biomass("crust_lichen");
        if (!dormant) return "moss never went dormant during the 60 d drought";
        if (!(mossB1 < mossB0)) return $"moss did not lose biomass ({mossB0:E3} -> {mossB1:E3})";
        if (crustB1 < 0.8 * crustB0) return $"crust lichen kept only {100 * crustB1 / crustB0:0.0}% of its biomass";
        return null;
    }
}

public sealed class ColonyLitterTest : IReprSelfTest
{
    public string Name => "colony.litter";
    public string? Run()
    {
        var remaining = new double[2];
        Vec2[] spots = { new Vec2(0.2, 0.5), new Vec2(0.8, 0.5) };
        for (int i = 0; i < 2; i++)
        {
            var s = ColonyTestKit.Cube();
            var c = new ColonySystem(s, 7, ColonyTestKit.LightFn);
            string? err = ColonyTestKit.Advance(c, s, 10 * ColonySystem.Day, ColonyTestKit.RainGradient, ColonyTestKit.Evap, ColonyTestKit.Dt);
            if (err != null) return err;
            double before = c.Stored();
            c.DepositLitter(spots[i], 0.3, 1.0);
            double rise = c.Stored() - before;
            if (Math.Abs(rise - 1.0) > 1e-9) return $"stored rose by {rise:0.000000000} kg, not 1 kg";
            err = ColonyTestKit.Advance(c, s, 30 * ColonySystem.Day, ColonyTestKit.RainGradient, ColonyTestKit.Evap, ColonyTestKit.Dt);
            if (err != null) return err;
            remaining[i] = c.Biomass("leaf_litter");
        }
        if (!(remaining[0] < remaining[1])) return $"litter decomposed no faster on the wet side ({remaining[0]:0.0000} kg vs dry {remaining[1]:0.0000} kg left)";
        return null;
    }
}

public sealed class ColonyLocalityTest : IReprSelfTest
{
    public string Name => "colony.locality";
    public string? Run()
    {
        var s = ColonyTestKit.Cube();
        var c = new ColonySystem(s, 7, ColonyTestKit.LightFn);
        c.Seed("cushion_moss", new Vec2(0.2, 0.5), 0.05);
        string? err = ColonyTestKit.Advance(c, s, 30 * ColonySystem.Day, ColonyTestKit.Rain, ColonyTestKit.Evap, ColonyTestKit.Dt);
        if (err != null) return err;

        // A dig far from the colony changes no colony cell.
        var snapBefore = Snapshot(c);
        s.Dig(new Vec3(0.9, s.Height(new Vec2(0.9, 0.9)), 0.9), 0.05, 0);
        c.Step(0);
        if (c.LastRefreshCount != 0) return $"far dig refreshed {c.LastRefreshCount} colony cells";
        if (!SameState(snapBefore, Snapshot(c))) return "far dig changed colony state";

        // A dig under the colony refreshes only cells inside its bounds.
        double dx = 0.2, dz = 0.5, r = 0.03;
        double y = s.Height(new Vec2(dx, dz));
        int inside = 0;
        foreach (var t in c.Field.Tiles)
            for (int cell = 0; cell < ColonyTile.N; cell++)
            {
                ColonyField.CellCoords(t, cell, out int ix, out int iz);
                var p = c.Field.CellCentre(ix, iz);
                if (Math.Abs(p.X - dx) <= r + 1e-9 && Math.Abs(p.Z - dz) <= r + 1e-9) inside++;   // boundary cells: float tolerance
            }
        s.Dig(new Vec3(dx, y, dz), r, 0);
        c.Step(0);
        if (c.LastRefreshCount <= 0) return "dig under the colony refreshed nothing";
        if (c.LastRefreshCount > inside) return $"dig refreshed {c.LastRefreshCount} cells, more than the {inside} inside its bounds";
        return null;
    }

    private static double[] Snapshot(ColonySystem c)
    {
        var list = new List<double>();
        foreach (var t in c.Field.Tiles)
        {
            list.AddRange(t.Coverage);
            list.AddRange(t.Biomass);
        }
        return list.ToArray();
    }

    private static bool SameState(double[] a, double[] b)
    {
        if (a.Length != b.Length) return false;
        for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }
}

public sealed class ColonyCostTest : IReprSelfTest
{
    private string _name = "colony.cost";
    public string Name => _name;
    public string? Run()
    {
        var r = ColonyTestKit.Niche.Value;
        if (r.Error != null) return r.Error;
        _name = $"colony.cost cells_allocated={r.C.AllocatedCells} bytes_per_cell={ColonySystem.BytesPerAllocatedCell:0} " +
                $"rim_cells={r.C.RimCellCount} ms_per_step_after_2y={r.Timing.MsPerStep:0.000} " +
                $"ms_per_step_steady={r.SteadyMs:0.000} steady_fraction={r.SteadyFraction:0.00}";
        return null;
    }
}

public sealed class ColonySnapshotTest : IReprSelfTest
{
    public string Name => "colony.snapshot";
    public string? Run()
    {
        var r = ColonyTestKit.Niche.Value;
        if (r.Error != null) return r.Error;
        string dir = "C:/Misc/Vivarium/build/overhaul/C1";
        Directory.CreateDirectory(dir);
        foreach (var kv in r.Ppm)
        {
            string path = Path.Combine(dir, kv.Key);
            File.WriteAllText(path, kv.Value);
            if (new FileInfo(path).Length == 0) return $"empty snapshot {path}";
        }
        return r.Ppm.Count == 3 ? null : "expected three snapshots";
    }
}
