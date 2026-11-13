using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

/// Shared helpers for the colony self tests. Substrate water is stepped with the colony unless a test freezes it
/// after a spin-up (water cost is proportional to simulated time, so long colony horizons freeze it).
internal static class ColonyTestKit
{
    public const double Dt = 21600;     // 6 h colony step
    public const double Evap = 2e-3;    // m/day

    /// Linear gradient across the cube: 3 mm/day at x = 0 down to 0.3 mm/day at x = 1. Smooth, so fronts are not ruled.
    public static readonly Func<Vec2, double> RainGradient = p => (3.0 - 2.7 * Math.Clamp(p.X, 0, 1)) * 1e-3;
    public static readonly Func<Vec2, double> RainUniform = _ => 3e-3;
    public static readonly Func<Vec3, double> LightFn = _ => 0.6;
    public static readonly Func<Vec3, double> LightShaded = _ => 0.25;

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

    public static string? Advance(ColonySystem c, LayeredSubstrate s, double seconds, double rain, double evap, double dt,
        Timing? timing = null, bool stepWater = true) =>
        Advance(c, s, seconds, _ => rain, evap, dt, timing, stepWater);

    /// Advances colonies (and substrate water unless frozen). Returns the first budget failure, or null.
    public static string? Advance(ColonySystem c, LayeredSubstrate s, double seconds, Func<Vec2, double> rain, double evap, double dt,
        Timing? timing = null, bool stepWater = true)
    {
        double t = 0;
        var sw = new Stopwatch();
        while (t < seconds - 1e-6)
        {
            double h = Math.Min(dt, seconds - t);
            if (stepWater) s.Water.Step(h, p => rain(p) / ColonySystem.Day, evap / ColonySystem.Day, 0);
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

    /// Water-only spin-up so the substrate reaches its forced state before colonies are judged.
    public static void Spin(LayeredSubstrate s, double days, Func<Vec2, double> rain, double evap)
    {
        for (double t = 0; t < days * ColonySystem.Day - 1e-6; t += Dt)
            s.Water.Step(Dt, p => rain(p) / ColonySystem.Day, evap / ColonySystem.Day, 0);
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
                    if (sid != 0 && speciesPick(c.Species[sid - 1].Id)) a += t.Cover(k) * cellArea;
                }
            }
        return a;
    }

    /// Rock = the surface the colony samples is bedrock (outcrops and columns with a sliver of soil).
    public static bool IsRock(LayeredSubstrate s, Vec2 p)
    {
        double h = s.Height(p);
        if (!double.IsFinite(h)) return false;
        return s.SampleSubstrate(new Vec3(p.X, h - 1e-4, p.Z)).Horizon == Horizon.Bedrock;
    }

    public static double MossCover(ColonyCellSample smp, string id)
    {
        double sum = 0;
        foreach (var o in smp.Occupants) if (o.SpeciesId == id) sum += o.Coverage;
        return sum;
    }

    public sealed class NicheRun
    {
        public ColonySystem C = null!;
        public LayeredSubstrate S = null!;
        public readonly Dictionary<string, string> Pgm = new();
        public readonly Dictionary<string, byte[]> Ppm = new();
        public readonly Timing Timing = new();
        public string? Error;
        public double SteadyMs;
        public double SteadyFraction;
    }

    /// Two-year cube run: water spun up for 30 days, then moss and crust grown with the substrate water frozen.
    public static readonly Lazy<NicheRun> Niche = new(BuildNiche);

    private static NicheRun BuildNiche()
    {
        var r = new NicheRun { S = Cube() };
        Spin(r.S, 30, RainGradient, Evap);
        r.C = new ColonySystem(r.S, 7, LightFn);
        SeedGrid(r.C, "cushion_moss", 0.04);
        SeedGrid(r.C, "crust_lichen", 0.04);
        r.Error = Advance(r.C, r.S, 182.5 * ColonySystem.Day, RainGradient, Evap, Dt, r.Timing, stepWater: false);
        if (r.Error != null) return r;
        Snap(r, "coverage_year0.5");
        r.Error = Advance(r.C, r.S, 182.5 * ColonySystem.Day, RainGradient, Evap, Dt, r.Timing, stepWater: false);
        if (r.Error != null) return r;
        Snap(r, "coverage_year1");
        r.Error = Advance(r.C, r.S, 365 * ColonySystem.Day, RainGradient, Evap, Dt, r.Timing, stepWater: false);
        if (r.Error != null) return r;
        Snap(r, "coverage_year2");

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
                if (t.Has(cell, ColonyTile.FSteady)) steady++;
            }
        r.SteadyFraction = occ > 0 ? (double)steady / occ : 0;
        return r;
    }

    private static void Snap(NicheRun r, string key)
    {
        Render(r.C, out string pgm, out byte[] ppm);
        r.Pgm[key + ".pgm"] = pgm;
        r.Ppm[key + ".ppm"] = ppm;
    }

    /// 400 x 400 image of the 50 x 50 cell domain, 8 px per cell. Green = moss, red = lichens, blue = litter.
    public static void Render(ColonySystem c, out string pgm, out byte[] ppm)
    {
        const int Cells = 50, Scale = 8;
        int size = Cells * Scale;
        var rgb = new byte[size * size * 3];
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
                        double cov = t.Cover(k);
                        if (kind == ColonyKind.Moss) g += cov;
                        else if (kind == ColonyKind.CrustoseLichen || kind == ColonyKind.FolioseLichen) r += cov;
                        else if (kind == ColonyKind.Litter) b += cov;
                    }
                int R = To255(r), G = To255(g), B = To255(b);
                sb.Append(R).Append(' ').Append(G).Append(' ').Append(B).Append(' ');
                int o = (py * size + px) * 3;
                rgb[o] = (byte)R; rgb[o + 1] = (byte)G; rgb[o + 2] = (byte)B;
            }
            sb.Append('\n');
        }
        pgm = sb.ToString();
        var header = Encoding.ASCII.GetBytes($"P6\n{size} {size}\n255\n");
        ppm = new byte[header.Length + rgb.Length];
        Buffer.BlockCopy(header, 0, ppm, 0, header.Length);
        Buffer.BlockCopy(rgb, 0, ppm, header.Length, rgb.Length);
    }

    private static int To255(double v) => (int)Math.Round(Math.Clamp(v, 0, 1) * 255);
}

public sealed class ColonyDeterminismTest : IReprSelfTest
{
    public string Name => "colony.determinism";
    public string? Run()
    {
        var s1 = ColonyTestKit.Cube(); var c1 = new ColonySystem(s1, 7, ColonyTestKit.LightFn);
        var s2 = ColonyTestKit.Cube(); var c2 = new ColonySystem(s2, 7, ColonyTestKit.LightFn);
        foreach (var c in new[] { c1, c2 }) c.Seed("cushion_moss", new Vec2(0.4, 0.5), 0.04);
        string? e1 = ColonyTestKit.Advance(c1, s1, 10 * ColonySystem.Day, ColonyTestKit.RainGradient, ColonyTestKit.Evap, ColonyTestKit.Dt);
        string? e2 = ColonyTestKit.Advance(c2, s2, 10 * ColonySystem.Day, ColonyTestKit.RainGradient, ColonyTestKit.Evap, ColonyTestKit.Dt);
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
        for (int i = 0; i < 800; i++)
        {
            double dt = dts[(int)(ReprId.Hash(11, (ulong)i) % 3)];
            s.Water.Step(dt, p => ColonyTestKit.RainGradient(p) / ColonySystem.Day, ColonyTestKit.Evap / ColonySystem.Day, 0);
            if (i == 200) c.DepositLitter(new Vec2(0.5, 0.5), 0.2, 0.5);
            if (i == 500) c.Seed("feather_moss", new Vec2(0.8, 0.2), 0.03);
            var b = c.Step(dt);
            string? err = ColonyTestKit.Closed(b);
            if (err != null) return $"step {i}: {err}";
            if (i % 100 == 0 || i == 799) { string? bad = Check(c); if (bad != null) return $"step {i}: {bad}"; }
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
                    if (t.Cover(k) < -1e-12) return $"negative coverage {t.Cover(k):E3}";
                    sum += t.Cover(k);
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
        string? e1 = ColonyTestKit.Advance(c1, s1, 20 * ColonySystem.Day, ColonyTestKit.RainGradient, ColonyTestKit.Evap, 3600);
        string? e2 = ColonyTestKit.Advance(c2, s2, 20 * ColonySystem.Day, ColonyTestKit.RainGradient, ColonyTestKit.Evap, 600);
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
        double rock = ColonyTestKit.Area(c, id => id == "crust_lichen" || id == "leaf_lichen", p => ColonyTestKit.IsRock(s, p));
        double humus = ColonyTestKit.Area(c, id => id == "crust_lichen" || id == "leaf_lichen", p => !ColonyTestKit.IsRock(s, p));
        _detail = $" [moss wet/dry={wet / Math.Max(dry, 1e-9):0.00} lichen rock/humus={rock / Math.Max(humus, 1e-9):0.00}]";
        if (!(wet > 3 * dry)) return $"moss wet {wet:0.0000} m2 not > 3x dry {dry:0.0000} m2";
        if (!(rock > 5 * humus)) return $"lichen on rock {rock:0.0000} m2 not > 5x humus {humus:0.0000} m2";

        // Damp rock: rock whose colony-side moisture (seepage, shade, film) is >= 0.5 at the end of the run.
        double mossDamp = 0, lichDamp = 0, maxRockMoist = 0; int damp = 0, rockCells = 0;
        for (int ix = 0; ix < 50; ix++)
            for (int iz = 0; iz < 50; iz++)
            {
                var p = c.Field.CellCentre(ix, iz);
                if (!ColonyTestKit.IsRock(s, p)) continue;
                rockCells++;
                var smp = c.Sample(p);
                double m = c.MoistureAt(p);
                maxRockMoist = Math.Max(maxRockMoist, m);
                if (m < 0.5) continue;
                damp++;
                mossDamp += ColonyTestKit.MossCover(smp, "cushion_moss");
                lichDamp += ColonyTestKit.MossCover(smp, "crust_lichen") + ColonyTestKit.MossCover(smp, "leaf_lichen");
            }
        _detail += $" [damp rock {damp}/{rockCells} cells, max rock moisture {maxRockMoist:0.00}]";
        if (damp == 0) return "no damp rock (colony-side moisture >= 0.5) at end of run";
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
        ColonyTestKit.Spin(s, 30, ColonyTestKit.RainGradient, ColonyTestKit.Evap);
        // Shaded understory light (moss optimum) so the front moves at a measurable rate.
        var c = new ColonySystem(s, 7, ColonyTestKit.LightShaded);
        const double x0 = 0.4;
        c.Seed("cushion_moss", new Vec2(x0, 0.5), 0.03);
        string? err = ColonyTestKit.Advance(c, s, 240 * ColonySystem.Day, ColonyTestKit.RainGradient, ColonyTestKit.Evap, ColonyTestKit.Dt, null, stepWater: false);
        if (err != null) return err;
        double west = 0, east = 0;
        foreach (var t in c.Field.Tiles)
            for (int cell = 0; cell < ColonyTile.N; cell++)
            {
                ColonyField.CellCoords(t, cell, out int ix, out int iz);
                double x = c.Field.CellCentre(ix, iz).X;
                for (int slot = 0; slot < 2; slot++)
                {
                    int k = cell * 2 + slot;
                    if (t.Species[k] != 1 || t.Cover(k) < 0.05) continue;
                    if (x < x0) west = Math.Max(west, x0 - x); else east = Math.Max(east, x - x0);
                }
            }
        _detail = $" [advance wet/dry={west / Math.Max(east, 1e-9):0.00} ({west:0.000} m vs {east:0.000} m)]";
        if (!(west > 1.05 * east)) return $"front advance wet/hospitable {west:0.000} m not > 1.05x dry side {east:0.000} m";
        return null;
    }
}

public sealed class ColonyMergeTest : IReprSelfTest
{
    public string Name => "colony.merge";
    public string? Run()
    {
        var s = ColonyTestKit.Cube();
        ColonyTestKit.Spin(s, 30, ColonyTestKit.RainUniform, ColonyTestKit.Evap);
        // Good habitat: shaded understory light at moss optimum, uniform rain (a rain gradient would tilt the seam line itself), 20 cm apart.
        var c = new ColonySystem(s, 7, ColonyTestKit.LightShaded);
        c.Seed("cushion_moss", new Vec2(0.21, 0.5), 0.03);
        c.Seed("cushion_moss", new Vec2(0.41, 0.5), 0.03);
        bool joined = false, seamFilled = false;
        double seamMin = 0, seamInterior = 0;
        for (int day = 0; day <= 730 && !seamFilled; day += 10)
        {
            if (day > 0)
            {
                string? err = ColonyTestKit.Advance(c, s, 10 * ColonySystem.Day, ColonyTestKit.RainUniform, ColonyTestKit.Evap, ColonyTestKit.Dt, null, stepWater: false);
                if (err != null) return err;
            }
            joined = c.ExtractPatches("cushion_moss").Count == 1;
            if (!joined) continue;
            (seamMin, seamInterior) = SeamStats(c);
            seamFilled = seamInterior > 0 && seamMin >= 0.9 * seamInterior;
        }
        if (!joined) return "seeds did not join into one patch within 2 years";
        foreach (double x in new[] { 0.21, 0.41 })
            if (ColonyTestKit.MossCover(c.Sample(new Vec2(x, 0.5)), "cushion_moss") < 0.05)
                return $"seed nucleus at x={x} died; merge would be spurious";
        if (!seamFilled) return $"seam not filled within 2 years: line min {seamMin:0.000} vs 0.9 x interior {seamInterior:0.000}";

        // Live moss only: litter from dieback is capped by its own coverage rule, not MaxBiomass.
        foreach (var t in c.Field.Tiles)
            for (int cell = 0; cell < ColonyTile.N; cell++)
                for (int slot = 0; slot < 2; slot++)
                {
                    int k = cell * 2 + slot, sid = t.Species[k];
                    if (sid == 0 || t.Cover(k) <= 0 || c.Species[sid - 1].Kind == ColonyKind.Litter) continue;
                    if (t.Biomass[k] / t.Cover(k) > c.Species[sid - 1].MaxBiomass * (1 + 1e-6))
                        return "a live cell's intensity exceeds MaxBiomass after merge";
                }
        return null;
    }

    /// Minimum moss coverage on the line between the seeds, and the mean coverage of interior cells
    /// (moss present in the cell and all 8 neighbours). Neighbour-based interior: relaxation caps coverage on soil.
    private static (double LineMin, double Interior) SeamStats(ColonySystem c)
    {
        double interiorSum = 0; int interiorN = 0;
        foreach (var t in c.Field.Tiles)
            for (int cell = 0; cell < ColonyTile.N; cell++)
            {
                ColonyField.CellCoords(t, cell, out int ix, out int iz);
                double m = Moss(t, cell);
                if (m <= 1e-3) continue;
                bool inner = true;
                for (int dx = -1; dx <= 1 && inner; dx++)
                    for (int dz = -1; dz <= 1 && inner; dz++)
                    {
                        if (dx == 0 && dz == 0) continue;
                        var nt = c.Field.FindCell(ix + dx, iz + dz, out int nc);
                        if (nt == null || Moss(nt, nc) <= 1e-3) inner = false;
                    }
                if (inner) { interiorSum += m; interiorN++; }
            }
        if (interiorN == 0) return (0, 0);
        double minLine = double.MaxValue;
        for (int ix = 10; ix <= 20; ix++)
        {
            var t = c.Field.FindCell(ix, 25, out int cell);
            minLine = Math.Min(minLine, t == null ? 0 : Moss(t, cell));
        }
        return (minLine, interiorSum / interiorN);
    }

    private static double Moss(ColonyTile t, int cell)
    {
        double v = 0;
        for (int slot = 0; slot < 2; slot++)
            if (t.Species[cell * 2 + slot] == 1) v += t.Cover(cell * 2 + slot);
        return v;
    }
}

public sealed class ColonyIrregularTest : IReprSelfTest
{
    public string Name => "colony.irregular";
    public string? Run()
    {
        // Flat, uniform habitat (1 m, no rock near the site) so only the rules and hash jitter shape the front.
        var spec = new SubstrateSpec
        {
            Seed = 7, Nx = 20, Nz = 20, Cell = 0.05, Depth = 1.0,
            SurfaceHeight = _ => 0.5, DrainagePotential = _ => 0.5, OrganicInput = _ => 0.5,
        };
        var s = new LayeredSubstrate(spec);
        Vec2? site = null;
        for (double x = 0.3; x <= 0.7 && site == null; x += 0.05)
            for (double z = 0.3; z <= 0.7 && site == null; z += 0.05)
            {
                var p = new Vec2(x, z);
                bool clear = true;
                for (int col = 0; col < s.ColumnCount && clear; col++)
                    if (s.IsOutcrop(col) && Vec2.Distance(s.ColumnCentre(col), p) < 0.3) clear = false;
                if (clear) site = p;
            }
        if (site == null) return "no outcrop-free site for the uniform habitat";
        var o = site.Value;

        ColonyTestKit.Spin(s, 10, ColonyTestKit.RainUniform, ColonyTestKit.Evap);
        var c = new ColonySystem(s, 7, ColonyTestKit.LightShaded);
        c.Seed("cushion_moss", o, 0.03);
        string? err = ColonyTestKit.Advance(c, s, 60 * ColonySystem.Day, ColonyTestKit.RainUniform, ColonyTestKit.Evap, ColonyTestKit.Dt, null, stepWater: false);
        if (err != null) return err;

        // Front = where moss coverage falls to half its centre value (the plateau is patchy-capped, not 1).
        double centre = ColonyTestKit.MossCover(c.Sample(o), "cushion_moss");
        if (centre <= 0) return "no moss at the nucleus after 60 days";
        double level = 0.5 * centre;
        var radii = new double[64];
        for (int i = 0; i < 64; i++)
        {
            double th = 2 * Math.PI * i / 64, rr = 0;
            for (double d = 0.002; d <= 0.25; d += 0.002)
            {
                var q = new Vec2(o.X + d * Math.Cos(th), o.Z + d * Math.Sin(th));
                if (ColonyTestKit.MossCover(c.Sample(q), "cushion_moss") >= level) rr = d; else break;
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
        string? err = ColonyTestKit.Advance(c, s, 60 * ColonySystem.Day, ColonyTestKit.RainGradient, ColonyTestKit.Evap, ColonyTestKit.Dt);
        if (err != null) return err;
        double mossB0 = c.Biomass("cushion_moss"), crustB0 = c.Biomass("crust_lichen");
        if (mossB0 <= 0 || crustB0 <= 0) return "no biomass before the drought";

        err = ColonyTestKit.Advance(c, s, 40 * ColonySystem.Day, _ => 0.0, 8e-3, ColonyTestKit.Dt);
        if (err != null) return err;
        bool dormant = false;
        foreach (var t in c.Field.Tiles)
            for (int k = 0; k < 2 * ColonyTile.N; k++)
                if (t.Species[k] == 1 && t.Dormant(k)) dormant = true;
        double mossB1 = c.Biomass("cushion_moss"), crustB1 = c.Biomass("crust_lichen");
        if (!dormant) return "moss never went dormant during the drought";
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
            ColonyTestKit.Spin(s, 5, ColonyTestKit.RainGradient, ColonyTestKit.Evap);
            var c = new ColonySystem(s, 7, ColonyTestKit.LightFn);
            double before = c.Stored();
            c.DepositLitter(spots[i], 0.3, 1.0);
            double rise = c.Stored() - before;
            if (Math.Abs(rise - 1.0) > 1e-9) return $"stored rose by {rise:0.000000000} kg, not 1 kg";
            string? err = ColonyTestKit.Advance(c, s, 20 * ColonySystem.Day, ColonyTestKit.RainGradient, ColonyTestKit.Evap, ColonyTestKit.Dt);
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
        string? err = ColonyTestKit.Advance(c, s, 15 * ColonySystem.Day, ColonyTestKit.RainGradient, ColonyTestKit.Evap, ColonyTestKit.Dt);
        if (err != null) return err;

        // A dig far from the colony changes no colony cell.
        var before = Snapshot(c);
        s.Dig(new Vec3(0.9, s.Height(new Vec2(0.9, 0.9)), 0.9), 0.05, 0);
        c.Step(0);
        if (c.LastRefreshCount != 0) return $"far dig refreshed {c.LastRefreshCount} colony cells";
        if (!SameState(before, Snapshot(c))) return "far dig changed colony state";

        // A dig under the colony refreshes only cells whose centres lie inside its bounds.
        double dx = 0.2, dz = 0.5, r = 0.03;
        double y = s.Height(new Vec2(dx, dz));
        int inside = 0;
        foreach (var t in c.Field.Tiles)
            for (int cell = 0; cell < ColonyTile.N; cell++)
            {
                ColonyField.CellCoords(t, cell, out int ix, out int iz);
                var p = c.Field.CellCentre(ix, iz);
                if (Math.Abs(p.X - dx) <= r + 1e-9 && Math.Abs(p.Z - dz) <= r + 1e-9) inside++;   // float tolerance on edges
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
            for (int k = 0; k < 2 * ColonyTile.N; k++)
            {
                list.Add(t.Cover(k));
                list.Add(t.Biomass[k]);
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
        _name = $"colony.cost cells_allocated={r.C.AllocatedCells} dense_bytes_per_cell={ColonySystem.DenseBytesPerCell} " +
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
        foreach (var kv in r.Pgm) File.WriteAllText(Path.Combine(dir, kv.Key), kv.Value);
        foreach (var kv in r.Ppm) File.WriteAllBytes(Path.Combine(dir, kv.Key), kv.Value);
        foreach (var name in new[] { "coverage_year0.5", "coverage_year1", "coverage_year2" })
            foreach (var ext in new[] { ".pgm", ".ppm" })
            {
                string path = Path.Combine(dir, name + ext);
                if (!File.Exists(path) || new FileInfo(path).Length == 0) return $"missing snapshot {path}";
            }
        return null;
    }
}
