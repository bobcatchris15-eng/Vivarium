using System;
using System.Collections.Generic;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Fields;
using Vivarium.Sim.Flora;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Repr;

/// <summary>
/// Production bridge coupling <see cref="LayeredSubstrate"/> (horizons, perched subsurface Darcy flow,
/// surface film/standing water, and fine-root density) with <see cref="VivariumWorld"/> ecology, mist weather,
/// root water uptake, and a closed water conservation ledger.
/// </summary>
public sealed class WorldSubstrate
{
    private readonly VivariumWorld _w;
    private int _terrainVersion;
    private int _floraVersion = -1;
    private double _simSeconds;
    private double _lastObservedWaterBudgetInfiltration;

    // Mist weather parameters (m/s): gentle humid enclosure misting + periodic mist pulses.
    // Rate is strictly positive and integrated analytically so any timestep split gives the exact same inflow.
    private const double BaseMistRate = 1.4e-7;         // ~0.50 mm/h baseline humid condensation
    private const double PulseMistAmp1 = 1.1e-7;        // ~0.40 mm/h 6-hour mist cycle
    private const double PulseMistAmp2 = 0.7e-7;        // ~0.25 mm/h 24-hour diurnal cycle
    private const double Omega1 = 2.0 * Math.PI / (6.0 * 3600.0);
    private const double Omega2 = 2.0 * Math.PI / (24.0 * 3600.0);
    private const double DefaultEvapDemand = 1.1e-7;    // ~0.40 mm/h evaporative demand
    private const double DefaultTranspDemand = 2.2e-9;  // m3 water per m of fine root per second

    private readonly double _phase1;
    private readonly double _phase2;

    public LayeredSubstrate Substrate { get; }

    /// <summary>When true (default), substrate hydrology, mist weather and root uptake drive world moisture and flora.</summary>
    public bool Coupled { get; set; } = true;

    /// <summary>When true (default), deterministic enclosure mist weather supplies atmospheric moisture.</summary>
    public bool MistWeatherEnabled { get; set; } = true;

    public double EvaporationDemand { get; set; } = DefaultEvapDemand;
    public double TranspirationDemandPerRootMetre { get; set; } = DefaultTranspDemand;

    /// <summary>Closed budget of the most recent <see cref="Step"/> call.</summary>
    public Budget LastStepBudget { get; private set; }

    /// <summary>Infiltration volume (m3) into the soil pores during the most recent <see cref="Step"/> call.</summary>
    public double LastInfiltrationDelta { get; private set; }

    /// <summary>Cumulative component fluxes (m3) since creation or last <see cref="ResetLedger"/>.</summary>
    public WaterFluxes CumulativeFluxes { get; private set; }

    /// <summary>Cumulative perched subsurface boundary outflow (m3) since creation or last <see cref="ResetLedger"/>.</summary>
    public double CumulativeSubsurfaceOutflow { get; private set; }

    /// <summary>Stored water volume (m3) at the start of the ledger period.</summary>
    public double InitialStoredWater { get; private set; }

    /// <summary>Cumulative closed water ledger across all steps since creation or <see cref="ResetLedger"/>.</summary>
    public Budget TotalWaterLedger => new(
        "world_substrate_water_m3",
        InitialStoredWater,
        Substrate.Water.Stored(),
        CumulativeFluxes.Rain,
        CumulativeFluxes.Evaporation + CumulativeFluxes.Transpiration +
        CumulativeFluxes.Drainage + CumulativeFluxes.Runoff + CumulativeSubsurfaceOutflow);

    public WorldSubstrate(VivariumWorld world)
    {
        _w = world ?? throw new ArgumentNullException(nameof(world));
        ulong h = ReprId.Hash(_w.Seed, 0xEC01061CUL);
        _phase1 = ReprId.Unit(h, 1) * 2.0 * Math.PI;
        _phase2 = ReprId.Unit(h, 2) * 2.0 * Math.PI;

        var grid = _w.Grid;
        var spec = new SubstrateSpec
        {
            Seed = _w.Seed,
            Origin = new Vec2(grid.OriginX, grid.OriginZ),
            Nx = grid.Nx,
            Nz = grid.Nz,
            Cell = grid.CellSize,
            Depth = 1.0,
            MaxLatticeSpacing = Math.Max(0.01, grid.CellSize * 0.25),
            SurfaceHeight = p => _w.Terrain.Height(p),
            DrainagePotential = p =>
            {
                _w.RootZone.EnsureCurrent();
                return _w.RootZone.DrainagePotential.Sample(p);
            },
            OrganicInput = p =>
            {
                _w.RootZone.EnsureCurrent();
                double d = _w.RootZone.DrainagePotential.Sample(p);
                double pilot = _w.PilotTree?.CanopyInfluence(p) ?? 0;
                return MathD.Clamp01(0.35 + 0.35 * (1.0 - d) + 0.25 * pilot);
            },
            BedrockDepth = p =>
            {
                _w.RootZone.EnsureCurrent();
                double retained = _w.RootZone.RetainedTopsoilDepth.Sample(p);
                double d = _w.RootZone.DrainagePotential.Sample(p);
                return MathD.Clamp(retained * (1.45 - 0.65 * d), 0.05, 0.95);
            },
        };

        Substrate = new LayeredSubstrate(spec);
        _terrainVersion = _w.Terrain.Version;
        ResetLedger();
    }

    /// <summary>Maps a <see cref="GridSpec"/> cell index (j * Nx + i) to a <see cref="LayeredSubstrate"/> column index (i * Nz + j).</summary>
    public int ColumnForCell(int cellIdx)
    {
        if ((uint)cellIdx >= (uint)_w.Grid.Count) return -1;
        int i = cellIdx % _w.Grid.Nx;
        int j = cellIdx / _w.Grid.Nx;
        return i * Substrate.Nz + j;
    }

    /// <summary>Maps a <see cref="LayeredSubstrate"/> column index (i * Nz + j) to a <see cref="GridSpec"/> cell index (j * Nx + i).</summary>
    public int CellForColumn(int col)
    {
        if ((uint)col >= (uint)Substrate.ColumnCount) return -1;
        int i = col / Substrate.Nz;
        int j = col % Substrate.Nz;
        return j * _w.Grid.Nx + i;
    }

    /// <summary>Resets cumulative water accounting to the current stored water state (used after world creation warmup or save load).</summary>
    public void ResetLedger()
    {
        double stored = Substrate.Water.Stored();
        InitialStoredWater = stored;
        CumulativeFluxes = default;
        CumulativeSubsurfaceOutflow = 0;
        LastInfiltrationDelta = 0;
        LastStepBudget = new Budget("water_m3", stored, stored, 0, 0);
        _lastObservedWaterBudgetInfiltration = _w.Water.Budget.Infiltration;
        _simSeconds = _w.Clock.SimSeconds;
    }

    /// <summary>Synchronises local terrain height edits without resetting unaffected columns' water, roots, or excavations.</summary>
    public int EnsureTerrainSynced()
    {
        if (_terrainVersion == _w.Terrain.Version) return 0;
        _w.RootZone.EnsureCurrent();
        double before = Substrate.Water.Stored();
        int changed = Substrate.SyncSurface(_w.Clock.SimSeconds);
        double after = Substrate.Water.Stored();
        // Adjust InitialStoredWater by any numerical rounding (<1e-15) so ledger remains closed across sculpts.
        InitialStoredWater += (after - before);
        _terrainVersion = _w.Terrain.Version;
        return changed;
    }

    /// <summary>Rebuilds the substrate fine-root density field from authoritative living flora and pilot tree.</summary>
    public void SyncRoots(bool force = false)
    {
        if (!force && _floraVersion == _w.Flora.Version) return;
        Substrate.ClearRoots();
        if (_w.PilotTree is { } pt)
        {
            double py = _w.Terrain.Height(pt.Anchor);
            Substrate.AddRootDensity(new Vec3(pt.Anchor.X, py, pt.Anchor.Z), Math.Max(1.0, pt.CanopyRadius * 0.55), 0.65, 180.0);
        }
        foreach (var f in _w.Flora.Items)
        {
            var sp = _w.Content.FloraOrThrow(f.SpeciesId);
            if (sp.Decomposer || sp.UnlimitedWaterDepth) continue;
            double frac = Math.Clamp(f.BiomassFraction(sp), 0.05, 1.0);
            double radius = sp.Woody != null
                ? Math.Max(0.18, sp.Woody.CanopyRadius * (0.40 + 0.60 * Math.Sqrt(frac)))
                : Math.Max(0.10, Math.Max(f.Radius(sp) * 1.5, sp.CompetitionRadius * 0.55));
            double depth = sp.Woody != null
                ? (sp.Woody.Layer == WoodyLayer.Tree ? 0.55 : 0.35)
                : 0.20;
            double rootMetres = Math.Max(0.15, f.Biomass * (sp.Woody != null ? 45.0 : 85.0));
            double gy = _w.Terrain.Height(f.Position);
            Substrate.AddRootDensity(new Vec3(f.Position.X, gy, f.Position.Z), radius, depth, rootMetres);
        }
        _floraVersion = _w.Flora.Version;
    }

    /// <summary>Spatial mist condensation factor (0.75..1.25) from terrain shelter and deterministic position.</summary>
    public double SpatialMistFactor(Vec2 p)
    {
        _w.RootZone.EnsureCurrent();
        double drain = _w.RootZone.DrainagePotential.Sample(p);
        double canopy = _w.PilotTree?.CanopyInfluence(p) ?? 0;
        // Sheltered concavities and canopy drip zones collect slightly more mist condensation than exposed ridges.
        return MathD.Clamp(0.82 + 0.28 * (1.0 - drain) + 0.12 * canopy, 0.75, 1.25);
    }

    /// <summary>Analytic antiderivative of temporal mist rate (m of water column from t=0 to t=simSeconds).</summary>
    public double CumulativeTemporalMist(double simSeconds)
    {
        if (!MistWeatherEnabled) return 0;
        double linear = (BaseMistRate + PulseMistAmp1 + PulseMistAmp2) * simSeconds;
        double osc1 = -(PulseMistAmp1 / Omega1) * (Math.Cos(Omega1 * simSeconds + _phase1) - Math.Cos(_phase1));
        double osc2 = (PulseMistAmp2 / Omega2) * (Math.Sin(Omega2 * simSeconds + _phase2) - Math.Sin(_phase2));
        return linear + osc1 + osc2;
    }

    /// <summary>Exact mean mist rate (m/s) at p over [t0, t0 + dt]. Invariant to timestep subdivision.</summary>
    public double MeanMistRate(Vec2 p, double t0, double dt)
    {
        if (!MistWeatherEnabled || !(dt > 0)) return 0;
        double temporal = (CumulativeTemporalMist(t0 + dt) - CumulativeTemporalMist(t0)) / dt;
        return Math.Max(0, temporal * SpatialMistFactor(p));
    }

    /// <summary>Advances the substrate hydrology and couples moisture into <see cref="EnvironmentFields.Moisture"/>.</summary>
    public Budget Step(double dt)
    {
        EnsureTerrainSynced();
        if (!Coupled)
        {
            _w.Water.CoupleMoisture(_w.Fields.Moisture, _w.Content.Ecology, dt, _w.Fields.Scratch, _w.RootZone);
            double s0 = Substrate.Water.Stored();
            LastStepBudget = new Budget("water_m3", s0, s0, 0, 0);
            LastInfiltrationDelta = 0;
            _simSeconds += Math.Max(0, dt);
            _lastObservedWaterBudgetInfiltration = _w.Water.Budget.Infiltration;
            return LastStepBudget;
        }

        if (!(dt > 0) || !double.IsFinite(dt))
        {
            double s0 = Substrate.Water.Stored();
            LastStepBudget = new Budget("water_m3", s0, s0, 0, 0);
            LastInfiltrationDelta = 0;
            return LastStepBudget;
        }

        SyncRoots();

        double t0 = _simSeconds;
        double temporalMist = MistWeatherEnabled
            ? Math.Max(0, (CumulativeTemporalMist(t0 + dt) - CumulativeTemporalMist(t0)) / dt)
            : 0;

        // If external surface liquid advanced since our last step, distribute its infiltrated volume across wet cells.
        double externalLiquidInf = Math.Max(0, _w.Water.Budget.Infiltration - _lastObservedWaterBudgetInfiltration);
        double wetCellSupplyRate = 0;
        if (externalLiquidInf > 0)
        {
            int wetCount = 0;
            foreach (int idx in _w.Grid.DomainCells)
                if (_w.Water.IsWet(idx)) wetCount++;
            if (wetCount > 0)
                wetCellSupplyRate = externalLiquidInf / (wetCount * _w.Water.CellArea * dt);
        }

        double RainAt(Vec2 p)
        {
            int cell = _w.Grid.CellAt(p);
            if (cell < 0 || !_w.Grid.InDomain(cell)) return 0;
            double rate = temporalMist * SpatialMistFactor(p);
            double surfDepth = _w.Water.SurfaceWaterDepth(cell);
            if (surfDepth > 1e-5)
            {
                // Active surface water (stream, pond, or poured water) supplies infiltration head to the column.
                rate += Math.Min(2.5e-6, surfDepth * 1.5e-4) + wetCellSupplyRate;
            }
            else if (_w.Water.IsWaterTable(cell))
            {
                rate += 1.2e-6 + wetCellSupplyRate;
            }
            return rate;
        }

        var stepBudget = Substrate.Water.Step(dt, RainAt, EvaporationDemand, TranspirationDemandPerRootMetre);
        _simSeconds = t0 + dt;

        var f = Substrate.Water.LastFluxes;
        LastStepBudget = stepBudget;
        LastInfiltrationDelta = f.Infiltration;
        CumulativeFluxes = new WaterFluxes(
            CumulativeFluxes.Rain + f.Rain,
            CumulativeFluxes.Infiltration + f.Infiltration,
            CumulativeFluxes.Percolation + f.Percolation,
            CumulativeFluxes.Evaporation + f.Evaporation,
            CumulativeFluxes.Transpiration + f.Transpiration,
            CumulativeFluxes.Drainage + f.Drainage,
            CumulativeFluxes.Runoff + f.Runoff);
        CumulativeSubsurfaceOutflow += Substrate.Water.LastSubsurfaceOutflow;

        // Record substrate infiltration in Hydrology.Budget when SurfaceLiquid did not already book it.
        if (externalLiquidInf <= 1e-15)
            _w.Water.Budget.Infiltration += f.Infiltration;
        _lastObservedWaterBudgetInfiltration = _w.Water.Budget.Infiltration;

        CoupleToFields(dt);
        return stepBudget;
    }

    /// <summary>Projects physical column moisture and surface/groundwater capillary support onto <see cref="EnvironmentFields.Moisture"/>.</summary>
    public void CoupleToFields(double dt)
    {
        _w.RootZone.EnsureCurrent();
        var eco = _w.Content.Ecology;
        var moisture = _w.Fields.Moisture;
        var scratch = _w.Fields.Scratch;
        var grid = _w.Grid;

        double wetRate = 1.0 - Math.Exp(-eco.MoistureWetting * Math.Max(dt, 1.0));
        double dryBase = 1.0 - Math.Exp(-eco.MoistureDrying * Math.Max(dt, 1.0));
        double depthScale = Math.Max(_w.Water.Config.WetDepth * 2.5, 0.02);
        double capRange = Math.Max(eco.MoistureCapillaryRange, grid.CellSize);

        double SurfaceHydration(double depth) =>
            depth <= 0 ? 0 : MathD.Clamp01(1.0 - Math.Exp(-depth / depthScale));

        foreach (int idx in grid.DomainCells)
            scratch[idx] = SurfaceHydration(_w.Water.SurfaceWaterDepth(idx));

        Span<double> neighbourWeight = stackalloc double[25];
        for (int dj = -2; dj <= 2; dj++)
            for (int di = -2; di <= 2; di++)
            {
                double dist = grid.CellSize * Math.Sqrt(di * di + dj * dj);
                neighbourWeight[(dj + 2) * 5 + di + 2] = Math.Exp(-dist / (capRange * 2.5));
            }

        foreach (int idx in grid.DomainCells)
        {
            int col = ColumnForCell(idx);
            double drainage = _w.RootZone.DrainagePotential.Values[idx];

            // 1. Physical soil-column moisture across horizons (weighted toward root zone).
            double soilM = ColumnEcologicalMoisture(col, drainage);

            // 2. Open surface water and capillary bank support from Hydrology.
            double aboveTable = Math.Max(0, _w.Water.Bed[idx] - _w.Water.WaterTable);
            double capillary = MathD.Clamp01((1.0 - aboveTable / Math.Max(eco.MoistureWaterTableRange, 1e-9)) *
                                             (1.20 - 0.40 * drainage));
            double groundwaterTarget = _w.Water.IsWaterTable(idx)
                ? 1.0
                : Math.Max(eco.MoistureDryBaseline, 0.85 * capillary * capillary);
            double localSurface = 0.98 * scratch[idx];

            int i = idx % grid.Nx, j = idx / grid.Nx;
            double neighbourSurface = 0;
            for (int dj = -2; dj <= 2; dj++)
                for (int di = -2; di <= 2; di++)
                {
                    if (di == 0 && dj == 0) continue;
                    int ii = i + di, jj = j + dj;
                    if (!grid.InDomain(ii, jj)) continue;
                    double h = scratch[grid.Index(ii, jj)];
                    if (h <= 0) continue;
                    neighbourSurface = Math.Max(neighbourSurface, 0.98 * h * neighbourWeight[(dj + 2) * 5 + di + 2]);
                }

            double hydroTarget = Math.Max(groundwaterTarget, Math.Max(localSurface, neighbourSurface * (1.0 - 0.45 * drainage)));
            double target = localSurface > 0.05 || _w.Water.IsWaterTable(idx)
                ? MathD.Clamp01(Math.Max(soilM, hydroTarget))
                : MathD.Clamp01(0.55 * soilM + 0.45 * Math.Max(soilM, hydroTarget));

            double m = moisture.Values[idx];
            double drying = 1.0 - Math.Exp(-eco.MoistureDrying * Math.Max(dt, 1.0) * (0.65 + 1.40 * drainage));
            double rate = target >= m ? Math.Max(wetRate, 0.35) : Math.Max(drying, 0.25);
            moisture[idx] = m + (target - m) * rate;
        }
        moisture.Diffuse(eco.MoistureDiffusion * Math.Max(dt, 1.0), scratch);
    }

    /// <summary>Computes ecological moisture (0..1) from the layered substrate's volumetric water content, field capacity, and wilting point.</summary>
    public double ColumnEcologicalMoisture(int col, double drainage)
    {
        if ((uint)col >= (uint)Substrate.ColumnCount) return _w.Content.Ecology.MoistureDryBaseline;
        ReadOnlySpan<double> layerWeights = [0.30, 0.35, 0.25, 0.10];
        double wSum = 0, mSum = 0;
        for (int l = 0; l < 4; l++)
        {
            int k = 4 * col + l;
            double h = Substrate.ColH[k];
            if (h <= 1e-9) continue;
            double th = Substrate.Water.Theta[k];
            double wp = Substrate.ColWP[k];
            double fc = Substrate.ColFC[k];
            double phi = Substrate.ColPhi[k];
            double lm;
            if (th <= wp)
                lm = 0.16 * (th / Math.Max(wp, 1e-6));
            else if (th <= fc)
                lm = 0.16 + 0.38 * ((th - wp) / Math.Max(fc - wp, 1e-6));
            else
                lm = 0.54 + 0.40 * ((th - fc) / Math.Max(phi - fc, 1e-6));
            double w = layerWeights[l] * Math.Sqrt(Math.Min(h, 0.35));
            mSum += lm * w;
            wSum += w;
        }
        double baseM = wSum > 1e-9 ? mSum / wSum : 0.16;
        double soilThick = Substrate.ColumnSoilThickness(col);
        double depthRetention = MathD.Clamp(0.34 + 0.62 * MathD.Clamp01(soilThick / 0.48) * (1.0 - 0.45 * drainage) - 0.28 * drainage, 0.22, 1.08);
        double surfaceBonus = Math.Min(0.22, (Substrate.Water.Standing[col] * 30.0 + Substrate.Water.Film[col] * 120.0) * (1.0 - 0.50 * drainage));
        return MathD.Clamp01(baseM * depthRetention + surfaceBonus);
    }

    /// <summary>Root-zone topsoil depth (m) at p from the physical substrate profile.</summary>
    public double TopsoilDepthAt(Vec2 p)
    {
        double t = Substrate.SoilThicknessAt(p);
        if (double.IsNaN(t))
        {
            _w.RootZone.EnsureCurrent();
            return _w.RootZone.RetainedTopsoilDepth.Sample(p);
        }
        _w.RootZone.EnsureCurrent();
        double r = _w.RootZone.RetainedTopsoilDepth.Sample(p);
        return Math.Max(0.025, 0.55 * t + 0.45 * r);
    }

    /// <summary>Root-zone aeration/oxygen (0..1) at p from air-filled porosity and drainage.</summary>
    public double OxygenAt(Vec2 p, double moisture)
    {
        int col = Substrate.ColumnAt(p);
        _w.RootZone.EnsureCurrent();
        double drainage = _w.RootZone.DrainagePotential.Sample(p);
        if (col < 0) return _w.RootZone.OxygenAt(p, moisture);
        double sat = Substrate.Water.TopSaturation(col);
        double effM = 0.5 * MathD.Clamp01(moisture) + 0.5 * MathD.Clamp01(sat);
        return MathD.Clamp01((1.0 - effM * effM) * (0.50 + 0.50 * drainage));
    }

    /// <summary>Root water availability factor (0..1) for plant growth at p.</summary>
    public double RootWaterFactor(Vec2 p)
    {
        int col = Substrate.ColumnAt(p);
        if (col < 0) return 1.0;
        double avail = 0, cap = 0;
        for (int l = 0; l < 4; l++)
        {
            int k = 4 * col + l;
            double h = Substrate.ColH[k];
            if (h <= 1e-9) continue;
            avail += Math.Max(0, Substrate.Water.Theta[k] - Substrate.ColWP[k]) * h;
            cap += Math.Max(1e-6, Substrate.ColFC[k] - Substrate.ColWP[k]) * h;
        }
        if (cap <= 1e-9) return 0.35;
        return MathD.Clamp(0.35 + 0.65 * Math.Min(1.2, avail / cap), 0.25, 1.05);
    }

    /// <summary>Exports compact column water state (Theta, Standing, Film) for deterministic save/load continuity.</summary>
    public double[] ExportWaterState()
    {
        int nc = Substrate.ColumnCount;
        var data = new double[nc * 6];
        Array.Copy(Substrate.Water.Theta, 0, data, 0, nc * 4);
        Array.Copy(Substrate.Water.Standing, 0, data, nc * 4, nc);
        Array.Copy(Substrate.Water.Film, 0, data, nc * 5, nc);
        return data;
    }

    /// <summary>Restores column water state after deserialization and resets ledger baselines without mutating world fields.</summary>
    public void RestoreWaterState(double[]? data)
    {
        EnsureTerrainSynced();
        int nc = Substrate.ColumnCount;
        if (data != null && data.Length == nc * 6)
        {
            Array.Copy(data, 0, Substrate.Water.Theta, 0, nc * 4);
            Array.Copy(data, nc * 4, Substrate.Water.Standing, 0, nc);
            Array.Copy(data, nc * 5, Substrate.Water.Film, 0, nc);
        }
        else
        {
            // Legacy save without explicit substrate water array: infer column water from restored Fields.Moisture.
            foreach (int idx in _w.Grid.DomainCells)
            {
                int col = ColumnForCell(idx);
                if ((uint)col >= (uint)nc) continue;
                double m = MathD.Clamp01(_w.Fields.Moisture.Values[idx]);
                for (int l = 0; l < 4; l++)
                {
                    int k = 4 * col + l;
                    if (Substrate.ColH[k] <= 1e-9) continue;
                    double wp = Substrate.ColWP[k], fc = Substrate.ColFC[k], phi = Substrate.ColPhi[k];
                    Substrate.Water.Theta[k] = m <= 0.58
                        ? wp + (fc - wp) * MathD.Clamp01((m - 0.18) / 0.40)
                        : fc + (phi - fc) * MathD.Clamp01((m - 0.58) / 0.34);
                }
            }
        }
        ResetLedger();
    }
}
