using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Repr;

internal static class EcoTestKit
{
    private static ContentLibrary? _content;

    public static ContentLibrary Content => _content ??= LoadContent();

    private static ContentLibrary LoadContent()
    {
        string? dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            string candidate = Path.Combine(dir, "game", "content");
            if (File.Exists(Path.Combine(candidate, "index.json")))
                return ContentLoader.Load(new DirectoryContentSource(candidate));
            dir = Directory.GetParent(dir)?.FullName;
        }
        dir = AppContext.BaseDirectory;
        while (dir != null)
        {
            string candidate = Path.Combine(dir, "game", "content");
            if (File.Exists(Path.Combine(candidate, "index.json")))
                return ContentLoader.Load(new DirectoryContentSource(candidate));
            dir = Directory.GetParent(dir)?.FullName;
        }
        throw new DirectoryNotFoundException("Could not locate game/content/index.json for EcologySelfTests.");
    }

    public static VivariumWorld SmallWorld(ulong seed = 777UL, double diameter = 12.0, double cellSize = 0.40, bool populate = false)
    {
        var d = Content.PresetOrThrow("default").Clone();
        d.Seed = seed;
        d.Diameter = diameter;
        d.CellSize = cellSize;
        bool prev = WorldDescriptor.AllowLegacyDiameter;
        WorldDescriptor.AllowLegacyDiameter = true;
        try
        {
            return VivariumWorld.Create(Content, d, populate: populate, warmupSurfaceWater: false);
        }
        finally
        {
            WorldDescriptor.AllowLegacyDiameter = prev;
        }
    }

    public static double ShannonIndex(VivariumWorld w)
    {
        if (w.Flora.Count == 0) return 0;
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var f in w.Flora.Items)
            counts[f.SpeciesId] = counts.GetValueOrDefault(f.SpeciesId) + 1;
        double n = w.Flora.Count, h = 0;
        foreach (var c in counts.Values)
        {
            double p = c / n;
            if (p > 0) h -= p * Math.Log(p);
        }
        return h;
    }
}

public sealed class EcoGridTest : IReprSelfTest
{
    public string Name => "eco.grid";
    public string? Run()
    {
        var w = EcoTestKit.SmallWorld(seed: 101UL, diameter: 12.0, cellSize: 0.40, populate: false);
        var ws = w.WorldSubstrate;
        if (ws.Substrate.Nx != w.Grid.Nx || ws.Substrate.Nz != w.Grid.Nz)
            return $"substrate grid {ws.Substrate.Nx}x{ws.Substrate.Nz} != world grid {w.Grid.Nx}x{w.Grid.Nz}";
        if (Math.Abs(ws.Substrate.Cell - w.Grid.CellSize) > 1e-9)
            return $"substrate cell {ws.Substrate.Cell} != world cell {w.Grid.CellSize}";

        foreach (int idx in w.Grid.DomainCells)
        {
            int col = ws.ColumnForCell(idx);
            if (col < 0 || col >= ws.Substrate.ColumnCount)
                return $"invalid column {col} for cell {idx}";
            int roundTrip = ws.CellForColumn(col);
            if (roundTrip != idx)
                return $"cell round-trip mismatch: {idx} -> col {col} -> {roundTrip}";
            var gc = w.Grid.CellCenter(idx);
            var cc = ws.Substrate.ColumnCentre(col);
            if (Vec2.Distance(gc, cc) > 1e-9)
                return $"centre mismatch at cell {idx}: grid {gc} vs column {cc}";
        }

        var b = ws.Step(600);
        if (!b.Closed(1e-9 + 1e-9 * b.StoredAfter))
            return $"nondefault grid step budget not closed: residual {b.Residual:E2}";
        return null;
    }
}

public sealed class EcoContinuityAndSaveTest : IReprSelfTest
{
    public string Name => "eco.continuity";
    public string? Run()
    {
        var w = EcoTestKit.SmallWorld(seed: 202UL, diameter: 12.0, cellSize: 0.40, populate: true);
        var ws = w.WorldSubstrate;
        ws.Step(600);

        // Snapshot untouched columns (> 2.2 m from sculpt centre) before sculpting.
        var centre = new Vec2(1.2, -0.8);
        const double radius = 0.9;
        var beforeTheta = (double[])ws.Substrate.Water.Theta.Clone();
        var beforeStanding = (double[])ws.Substrate.Water.Standing.Clone();
        var beforeSurf = (double[])ws.Substrate.ColSurf.Clone();

        int changedVerts = TerrainEditing.Sculpt(w, centre, radius, 0.12, SculptMode.Lower);
        if (changedVerts <= 0) return "sculpt changed 0 terrain vertices";
        int changedCols = ws.EnsureTerrainSynced();
        if (changedCols <= 0) return "EnsureTerrainSynced updated 0 columns after sculpt";

        int untouchedChecked = 0;
        for (int c = 0; c < ws.Substrate.ColumnCount; c++)
        {
            double dist = Vec2.Distance(ws.Substrate.ColumnCentre(c), centre);
            if (dist <= radius + ws.Substrate.Cell * 2.0) continue;
            untouchedChecked++;
            if (Math.Abs(ws.Substrate.ColSurf[c] - beforeSurf[c]) > 1e-12)
                return $"untouched column {c} surface changed by {Math.Abs(ws.Substrate.ColSurf[c] - beforeSurf[c]):E2}";
            if (Math.Abs(ws.Substrate.Water.Standing[c] - beforeStanding[c]) > 1e-12)
                return $"untouched column {c} standing water changed";
            for (int l = 0; l < 4; l++)
                if (Math.Abs(ws.Substrate.Water.Theta[4 * c + l] - beforeTheta[4 * c + l]) > 1e-12)
                    return $"untouched column {c} layer {l} theta changed";
        }
        if (untouchedChecked < 50) return $"too few untouched columns checked ({untouchedChecked})";
        if (!ws.TotalWaterLedger.Closed(1e-7))
            return $"water ledger not closed after terrain sculpt: residual {ws.TotalWaterLedger.Residual:E2}";

        // Verify pure serialization (no side effects) and exact round-trip digest.
        string digestBefore = WorldSerializer.Digest(w);
        var payloads = WorldSerializer.Serialize(w);
        string digestAfterSerialize = WorldSerializer.Digest(w);
        if (digestBefore != digestAfterSerialize)
            return "Serialize mutated world state";

        var loaded = WorldSerializer.Deserialize(EcoTestKit.Content, payloads);
        string digestLoaded = WorldSerializer.Digest(loaded);
        if (digestLoaded != digestBefore)
            return $"save/load round-trip digest mismatch ({digestLoaded[..12]} != {digestBefore[..12]})";
        if (Math.Abs(loaded.WorldSubstrate.Substrate.Water.Stored() - ws.Substrate.Water.Stored()) > 1e-12)
            return "loaded WorldSubstrate stored water differs from saved world";
        return null;
    }
}

public sealed class EcoInfiltrationTest : IReprSelfTest
{
    public string Name => "eco.infiltration";
    public string? Run()
    {
        var w = EcoTestKit.SmallWorld(seed: 303UL, diameter: 12.0, cellSize: 0.40, populate: false);
        var ws = w.WorldSubstrate;

        // Drain top layers slightly below saturation so infiltration capacity is available everywhere.
        for (int c = 0; c < ws.Substrate.ColumnCount; c++)
            for (int l = 0; l < 4; l++)
                ws.Substrate.Water.Theta[4 * c + l] = ws.Substrate.ColFC[4 * c + l] * 0.85;
        ws.ResetLedger();

        double budgetInf0 = w.Water.Budget.Infiltration;
        if (ws.LastInfiltrationDelta != 0 || ws.CumulativeFluxes.Infiltration != 0)
            return "infiltration delta not zero after ResetLedger";

        // First step after creation/ResetLedger: must record exact positive infiltration from mist/wet cells.
        var b1 = ws.Step(300);
        if (!b1.Closed(1e-9 + 1e-9 * b1.StoredAfter))
            return $"first step budget not closed: {b1.Residual:E2}";
        if (!(ws.LastInfiltrationDelta > 0))
            return $"first step infiltration delta was {ws.LastInfiltrationDelta:E2} (expected > 0)";
        if (Math.Abs(ws.LastInfiltrationDelta - ws.Substrate.Water.LastFluxes.Infiltration) > 1e-15)
            return "LastInfiltrationDelta disagrees with Substrate.Water.LastFluxes.Infiltration";
        double budgetInfDelta = w.Water.Budget.Infiltration - budgetInf0;
        if (Math.Abs(budgetInfDelta - ws.LastInfiltrationDelta) > 1e-12)
            return $"Hydrology.Budget.Infiltration delta {budgetInfDelta:E4} != LastInfiltrationDelta {ws.LastInfiltrationDelta:E4}";

        // Simulate external liquid infiltration bump (e.g. warmup or save restore) followed by ResetLedger.
        w.Water.Budget.Infiltration += 0.25;
        ws.ResetLedger();
        double budgetInf1 = w.Water.Budget.Infiltration;
        ws.Step(300);
        double budgetInfDelta2 = w.Water.Budget.Infiltration - budgetInf1;
        if (Math.Abs(budgetInfDelta2 - ws.LastInfiltrationDelta) > 1e-12)
            return $"post-reset infiltration delta leaked pre-reset value ({budgetInfDelta2:E4} vs {ws.LastInfiltrationDelta:E4})";
        return null;
    }
}

public sealed class EcoWeatherTest : IReprSelfTest
{
    public string Name => "eco.weather";
    public string? Run()
    {
        var w = EcoTestKit.SmallWorld(seed: 404UL, diameter: 12.0, cellSize: 0.40, populate: false);
        var ws = w.WorldSubstrate;
        var pHollow = new Vec2(0.0, 0.0);
        var pEdge = new Vec2(4.2, 0.0);

        double m0 = ws.MeanMistRate(pHollow, 0, 600);
        if (!(m0 > 0)) return $"mist rate nonpositive: {m0}";

        // Exact antiderivative invariance: 60 x 10 s vs 12 x 50 s vs 1 x 600 s must integrate identically.
        const double totalDt = 3600.0;
        double fullDepth = ws.MeanMistRate(pHollow, 1200.0, totalDt) * totalDt;
        double sum10 = 0;
        for (int i = 0; i < 360; i++)
            sum10 += ws.MeanMistRate(pHollow, 1200.0 + i * 10.0, 10.0) * 10.0;
        double sum120 = 0;
        for (int i = 0; i < 30; i++)
            sum120 += ws.MeanMistRate(pHollow, 1200.0 + i * 120.0, 120.0) * 120.0;

        if (Math.Abs(fullDepth - sum10) > 1e-13 || Math.Abs(fullDepth - sum120) > 1e-13)
            return $"mist antiderivative not dt-invariant: full={fullDepth:E8} sum10={sum10:E8} sum120={sum120:E8}";

        // Disabling mist weather must produce zero mist rate.
        ws.MistWeatherEnabled = false;
        if (ws.MeanMistRate(pHollow, 0, 600) != 0)
            return "disabled mist weather returned non-zero rate";
        ws.MistWeatherEnabled = true;
        return null;
    }
}

public sealed class EcoUptakeTest : IReprSelfTest
{
    public string Name => "eco.uptake";
    public string? Run()
    {
        var wBare = EcoTestKit.SmallWorld(seed: 505UL, diameter: 12.0, cellSize: 0.40, populate: false);
        var wPlanted = EcoTestKit.SmallWorld(seed: 505UL, diameter: 12.0, cellSize: 0.40, populate: false);

        // Remove springs so surface water does not mask root drawdown, and disable mist during the drawdown window.
        wBare.Water.Springs.Clear();
        wPlanted.Water.Springs.Clear();
        wBare.WorldSubstrate.MistWeatherEnabled = false;
        wPlanted.WorldSubstrate.MistWeatherEnabled = false;

        // Add rooted plants across the planted world.
        var sp = EcoTestKit.Content.Flora.First(s => !s.Decomposer && !s.UnlimitedWaterDepth && s.Woody != null);
        foreach (int idx in wPlanted.Grid.DomainCells)
        {
            var p = wPlanted.Grid.CellCenter(idx);
            if (p.Length > 3.5) continue;
            wPlanted.Flora.Add(new FloraIndividual
            {
                Id = wPlanted.Ids.Next(EntityKind.Flora),
                SpeciesId = sp.Id,
                X = p.X,
                Z = p.Z,
                Biomass = sp.MaxBiomass * 0.75,
                Health = 1.0,
                LifespanFactor = 1.0,
            });
        }
        wPlanted.WorldSubstrate.SyncRoots(force: true);

        double maxRoot = 0;
        for (int i = 0; i < wPlanted.WorldSubstrate.Substrate.NRoot.Length; i++)
            maxRoot = Math.Max(maxRoot, wPlanted.WorldSubstrate.Substrate.NRoot[i]);
        if (!(maxRoot > 0)) return "SyncRoots deposited zero fine-root density for planted flora";

        wBare.WorldSubstrate.ResetLedger();
        wPlanted.WorldSubstrate.ResetLedger();

        for (int k = 0; k < 12; k++)
        {
            wBare.WorldSubstrate.Step(1800);
            wPlanted.WorldSubstrate.Step(1800);
        }

        double trPlanted = wPlanted.WorldSubstrate.CumulativeFluxes.Transpiration;
        double trBare = wBare.WorldSubstrate.CumulativeFluxes.Transpiration;
        if (!(trPlanted > trBare + 1e-4))
            return $"planted transpiration ({trPlanted:E4} m3) did not exceed bare ({trBare:E4} m3)";
        if (!(wPlanted.WorldSubstrate.Substrate.Water.Stored() < wBare.WorldSubstrate.Substrate.Water.Stored()))
            return "planted substrate did not draw down stored soil water relative to bare control";
        if (!wPlanted.WorldSubstrate.TotalWaterLedger.Closed(1e-8))
            return $"planted ledger not closed: residual {wPlanted.WorldSubstrate.TotalWaterLedger.Residual:E2}";
        return null;
    }
}

public sealed class EcoRootInvalidationTest : IReprSelfTest
{
    public string Name => "eco.rootInvalidation";
    public string? Run()
    {
        var low = EcoTestKit.SmallWorld(seed: 515UL, diameter: 12.0, cellSize: 0.40, populate: false);
        var grown = EcoTestKit.SmallWorld(seed: 515UL, diameter: 12.0, cellSize: 0.40, populate: false);
        var species = EcoTestKit.Content.Flora.First(s => !s.Decomposer && !s.UnlimitedWaterDepth && s.Woody != null);
        FloraIndividual Plant(VivariumWorld world) => new()
        {
            Id = world.Ids.Next(EntityKind.Flora),
            SpeciesId = species.Id,
            X = 0.0,
            Z = 0.0,
            Biomass = species.MaxBiomass * 0.15,
            Health = 1.0,
            LifespanFactor = 1.0,
        };
        var lowPlant = Plant(low);
        var grownPlant = Plant(grown);
        low.Flora.Add(lowPlant);
        grown.Flora.Add(grownPlant);
        low.Water.Springs.Clear();
        grown.Water.Springs.Clear();
        low.WorldSubstrate.MistWeatherEnabled = false;
        grown.WorldSubstrate.MistWeatherEnabled = false;
        low.WorldSubstrate.SyncRoots();
        grown.WorldSubstrate.SyncRoots();

        long initialRevision = grown.WorldSubstrate.RootRevision;
        float[] initialRoots = (float[])grown.WorldSubstrate.Substrate.NRoot.Clone();
        grown.WorldSubstrate.SyncRoots();
        if (grown.WorldSubstrate.RootRevision != initialRevision)
            return "unchanged root inputs caused an unnecessary root-field rebuild";

        grownPlant.Biomass = species.MaxBiomass * 0.90; // Ordinary growth mutates the existing individual.
        grown.WorldSubstrate.Step(3600); // Production step must observe it without force=true or membership change.
        low.WorldSubstrate.Step(3600);
        if (grown.WorldSubstrate.RootRevision != initialRevision + 1)
            return "in-place biomass growth did not trigger exactly one root-field rebuild";
        if (initialRoots.AsSpan().SequenceEqual(grown.WorldSubstrate.Substrate.NRoot))
            return "in-place biomass growth rebuilt without changing fine-root density";
        if (!(grown.WorldSubstrate.CumulativeFluxes.Transpiration > low.WorldSubstrate.CumulativeFluxes.Transpiration + 1e-9))
            return $"grown roots did not increase actual water uptake ({grown.WorldSubstrate.CumulativeFluxes.Transpiration:E4} vs {low.WorldSubstrate.CumulativeFluxes.Transpiration:E4} m3)";

        float[] beforeMove = (float[])grown.WorldSubstrate.Substrate.NRoot.Clone();
        long beforeMoveRevision = grown.WorldSubstrate.RootRevision;
        grown.Flora.Move(grownPlant, new Vec2(1.35, 0.65));
        grown.WorldSubstrate.SyncRoots();
        if (grown.WorldSubstrate.RootRevision != beforeMoveRevision + 1)
            return "flora movement did not trigger a root-field rebuild";
        if (beforeMove.AsSpan().SequenceEqual(grown.WorldSubstrate.Substrate.NRoot))
            return "flora movement did not relocate fine-root density";
        grown.WorldSubstrate.SyncRoots();
        if (grown.WorldSubstrate.RootRevision != beforeMoveRevision + 1)
            return "unchanged moved-plant inputs caused an unnecessary root-field rebuild";
        return null;
    }
}

public sealed class EcoLedgerTest : IReprSelfTest
{
    public string Name => "eco.ledger";
    public string? Run()
    {
        var w = EcoTestKit.SmallWorld(seed: 606UL, diameter: 12.0, cellSize: 0.40, populate: true);
        var ws = w.WorldSubstrate;
        double[] dts = { 30.0, 120.0, 300.0, 900.0 };

        for (int k = 0; k < 40; k++)
        {
            if (k == 15)
                TerrainEditing.Sculpt(w, new Vec2(0.5, 0.5), 1.0, 0.08, SculptMode.Raise);
            if (k == 25)
                ws.MistWeatherEnabled = false;
            if (k == 32)
                ws.MistWeatherEnabled = true;

            var b = ws.Step(dts[k % dts.Length]);
            if (!b.Closed(1e-9 + 1e-9 * b.StoredAfter))
                return $"step {k} budget not closed: residual {b.Residual:E2}";
            if (!ws.TotalWaterLedger.Closed(1e-7 + 1e-8 * ws.InitialStoredWater))
                return $"total ledger not closed at step {k}: residual {ws.TotalWaterLedger.Residual:E2}";
        }

        var problems = w.CheckInvariants();
        if (problems.Count > 0)
            return "world invariants failed: " + string.Join("; ", problems);
        return null;
    }
}

public sealed class EcoDtInvarianceTest : IReprSelfTest
{
    public string Name => "eco.dtInvariance";
    public string? Run()
    {
        var wFine = EcoTestKit.SmallWorld(seed: 707UL, diameter: 12.0, cellSize: 0.40, populate: true);
        var wCoarse = EcoTestKit.SmallWorld(seed: 707UL, diameter: 12.0, cellSize: 0.40, populate: true);

        // Advance 6 hours: 36 x 600 s vs 12 x 1800 s.
        for (int i = 0; i < 36; i++) wFine.WorldSubstrate.Step(600);
        for (int i = 0; i < 12; i++) wCoarse.WorldSubstrate.Step(1800);

        double sFine = wFine.WorldSubstrate.Substrate.Water.Stored();
        double sCoarse = wCoarse.WorldSubstrate.Substrate.Water.Stored();
        double relDiff = Math.Abs(sFine - sCoarse) / Math.Max(1e-6, sFine);
        if (relDiff > 0.015)
            return $"stored water diverged by {relDiff * 100:0.00}% between dt=600 and dt=1800";

        double rainFine = wFine.WorldSubstrate.CumulativeFluxes.Rain;
        double rainCoarse = wCoarse.WorldSubstrate.CumulativeFluxes.Rain;
        if (Math.Abs(rainFine - rainCoarse) > 1e-10)
            return $"cumulative rain/mist diverged between timesteps: {rainFine:E6} vs {rainCoarse:E6}";
        return null;
    }
}

public sealed class EcoNinetyDayTest : IReprSelfTest
{
    public string Name => "eco.ninetyDay";
    public string? Run()
    {
        var wCoupled = EcoTestKit.SmallWorld(seed: 808UL, diameter: 12.0, cellSize: 0.40, populate: true);
        var wUncoupled = EcoTestKit.SmallWorld(seed: 808UL, diameter: 12.0, cellSize: 0.40, populate: true);
        wCoupled.WorldSubstrate.Coupled = true;
        wUncoupled.WorldSubstrate.Coupled = false;

        // Accelerated biological clock so 90 bio-days completes quickly in headless verification while exercising all systems.
        wCoupled.Clock.BioAcceleration = 72.0;
        wUncoupled.Clock.BioAcceleration = 72.0;
        double physSeconds = 90.0 * SimUnits.Day / wCoupled.Clock.BioAcceleration;

        var sw = Stopwatch.StartNew();
        wCoupled.RunFor(physSeconds);
        double coupledMs = sw.Elapsed.TotalMilliseconds;

        wUncoupled.RunFor(physSeconds);

        var inv = wCoupled.CheckInvariants();
        if (inv.Count > 0) return "coupled 90-day invariants failed: " + string.Join("; ", inv);

        // Verify wet-to-dry moisture gradient across terrestrial drainage tertiles (excluding submerged pond/stream bed).
        var byDrain = wCoupled.Grid.DomainCells
            .Where(c => !wCoupled.Water.IsWaterTable(c) && wCoupled.Water.SurfaceWaterDepth(c) <= 0.005)
            .Select(c => (Drain: wCoupled.RootZone.DrainagePotential.Values[c], Moist: wCoupled.Fields.Moisture.Values[c]))
            .OrderBy(x => x.Drain)
            .ToArray();
        int third = Math.Max(1, byDrain.Length / 3);
        double wetHollowM = byDrain.Take(third).Average(x => x.Moist);
        double midSlopeM = byDrain.Skip(third).Take(third).Average(x => x.Moist);
        double dryRidgeM = byDrain.Skip(2 * third).Average(x => x.Moist);
        if (!(wetHollowM > midSlopeM && midSlopeM > dryRidgeM))
            return $"moisture gradient broken after 90d: wet={wetHollowM:0.000}, mid={midSlopeM:0.000}, dry={dryRidgeM:0.000}";

        int coupledSpecies = wCoupled.Flora.Items.Select(f => f.SpeciesId).Distinct(StringComparer.Ordinal).Count();
        int uncoupledSpecies = wUncoupled.Flora.Items.Select(f => f.SpeciesId).Distinct(StringComparer.Ordinal).Count();
        double hCoupled = EcoTestKit.ShannonIndex(wCoupled);
        double hUncoupled = EcoTestKit.ShannonIndex(wUncoupled);

        if (wCoupled.Flora.Count < 25 || coupledSpecies < 8 || hCoupled < 1.5)
            return $"coupled 90d diversity too low: count={wCoupled.Flora.Count}, species={coupledSpecies}, H={hCoupled:0.00}";
        if (wCoupled.EnvironmentMoistureMs > 50.0)
            return $"coupled environment moisture step too slow: {wCoupled.EnvironmentMoistureMs:0.0} ms";

        Console.Write($"[90d coupled: flora={wCoupled.Flora.Count} sp={coupledSpecies} H={hCoupled:0.00} wet/mid/dry={wetHollowM:0.00}/{midSlopeM:0.00}/{dryRidgeM:0.00} envMs={wCoupled.EnvironmentMoistureMs:0.00} totalMs={coupledMs:0} | uncoupled: flora={wUncoupled.Flora.Count} sp={uncoupledSpecies} H={hUncoupled:0.00}] ");
        return null;
    }
}
