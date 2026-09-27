using System.Collections.Concurrent;
using System.Diagnostics;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Coverage.Rules;
using Vivarium.Sim.Coverage.Plasmodium;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Coverage;

/// <summary>
/// Runs moss (Mat layer) and lichen (Crust layer) growth against the real world (docs/overhaul/growth_models.md
/// §4, §5, §8, §9): a scheduler-driven <see cref="Step"/> at the flora cadence, and <see cref="SeedInitial"/> to
/// place each coverage species' first presence at world creation (small discs, not individuals). Species
/// parameter tables (occupant ids into each layer's <c>Occ</c> byte) are built once from
/// <see cref="ContentLibrary.Flora"/> at construction.
/// </summary>
public sealed class CoverageSystem : IMicroEnvSource, ILichenEnvSource, IDetritusSink
{
    private readonly VivariumWorld _w;
    private readonly MoistureBonusField _moistureBonus;
    private readonly List<MatParams> _matSpecies = new();
    private readonly Dictionary<byte, string> _matSpeciesId = new();
    private readonly List<LichenParams> _lichenSpecies = new();
    private readonly Dictionary<byte, string> _lichenSpeciesId = new();
    private readonly PlasmodiumParams? _plasmodiumParams;
    private readonly PlasmodiumColony? _plasmodiumColony;
    private readonly Attractant? _plasmodiumAttractant;
    private readonly Network? _plasmodiumNetwork;
    private readonly LifecycleController? _plasmodiumLifecycle;
    private readonly PlasmodiumWorldEnv? _plasmodiumEnv;
    private readonly string? _plasmodiumSpeciesId;
    private long _step;

    /// <summary>Number of coverage growth steps completed; part of authoritative state because rules hash it.</summary>
    public long StepIndex => _step;

    internal void RestoreStep(long step)
    {
        if (step < 0) throw new InvalidDataException($"coverage step is invalid ({step})");
        _step = step;
    }

    /// <summary>Per-phase cost of the last Mat step (docs/overhaul/growth_models.md §10), for the perf print.</summary>
    public MatStepStats LastMatStats { get; private set; }
    public LichenStepStats LastLichenStats { get; private set; }
    public (double WaterMs, double MatMs, double LichenMs) LastPhaseTimes { get; private set; }

    public CoverageSystem(VivariumWorld w)
    {
        _w = w;
        _moistureBonus = CoverageEnvironment.MoistureBonusOf(w);
        byte matId = 0, lichenId = 0;
        foreach (var sp in w.Content.Flora)
        {
            if (sp.Mat is { } md)
            {
                matId++;
                _matSpeciesId[matId] = sp.Id;
                _matSpecies.Add(new MatParams(matId, sp.Id, md.HeightForm, md.Lateral, md.MaxHeightM, md.GrowthRatePerDay,
                    md.KWet, md.KDry, md.WMin, md.WOpt, md.KLight, md.LightMax, md.DormBrownDays, md.DormDeathDays,
                    md.SporeRate, md.MoistureFeedback, sp.HardMinMoisture, md.SeedBiomass, md.DomeLength));
            }
            if (sp.Lichen is { } ld)
            {
                lichenId++;
                _lichenSpeciesId[lichenId] = sp.Id;
                _lichenSpecies.Add(new LichenParams
                {
                    OccSlot = lichenId,
                    Form = ld.Form,
                    Substrates = ld.Substrates.ToArray(),
                    GravelRateMultiplier = ld.GravelRateMultiplier,
                    Lateral = ld.Lateral,
                    TipBiasGamma = ld.TipBiasGamma,
                    OpennessRadius = ld.OpennessRadius,
                    MinNeighboursToColonise = ld.MinNeighboursToColonise,
                    CentreDeathDays = ld.CentreDeathDays,
                    CentreDeathMinD2E = ld.CentreDeathMinD2E,
                    DeadDecayPerDay = ld.DeadDecayPerDay,
                    KWet = ld.KWet,
                    KDry = ld.KDry,
                    WMin = ld.WMin,
                    WOpt = ld.WOpt,
                    KLight = ld.KLight,
                    SeedBiomass = ld.SeedBiomass,
                    MaxBiomass = ld.MaxBiomass,
                    EdenNoiseExponent = ld.EdenNoiseExponent,
                });
            }
            if (sp.Archetype == "slime_mold")
            {
                _plasmodiumSpeciesId = sp.Id;
                _plasmodiumParams = new PlasmodiumParams
                {
                    InitialMass = 500,
                    Beta = 0.05,
                    LambdaF = 4.0,
                    Sigma = 5.0,
                    Dc = 0.04,
                    Delta = 0.10,
                    FeedRate = 3.0,
                };
                _plasmodiumColony = new PlasmodiumColony(1, _plasmodiumParams);
                _plasmodiumAttractant = new Attractant(_plasmodiumParams);
                _plasmodiumNetwork = new Network { Gamma = 0.3, QGain = 5.0 };
                _plasmodiumLifecycle = new LifecycleController(_plasmodiumParams);
                _plasmodiumEnv = new PlasmodiumWorldEnv(w);
            }
        }
    }

    public bool HasCoverageSpecies => _matSpecies.Count > 0 || _lichenSpecies.Count > 0 || _plasmodiumColony != null;

    // ------------------------------------------------------------------ IMicroEnvSource / ILichenEnvSource

    private readonly Dictionary<long, MicroEnv> _stepEnvCache = new();
    private readonly ConcurrentDictionary<long, CoverageSubstrate> _stepSubstrateCache = new();
    private readonly ConcurrentDictionary<long, (double slope, Vec2 downslope, double laplacian, bool complete)> _terrainGeoCache = new();
    private readonly ConcurrentDictionary<(int ti, int tj), List<Flora.FloraIndividual>> _stepTileFlora = new();
    private readonly ConcurrentDictionary<(int ti, int tj), List<ShadeCaster>> _stepTileShadeCasters = new();
    private readonly ConcurrentDictionary<(int ti, int tj), double> _stepTileLaplacian = new();
    private int _terrainVersion = -1;
    private double[]? _stepWaterDist;
    private bool _inStep;

    public MicroEnv Sample(int gx, int gz)
    {
        if (_inStep)
        {
            long key = ((long)gx << 32) | (uint)gz;
            if (_stepEnvCache.TryGetValue(key, out var cached)) return cached;
            var p = CellCentre(gx, gz);
            if (_terrainVersion != _w.Terrain.Version)
            {
                _terrainGeoCache.Clear();
                _terrainVersion = _w.Terrain.Version;
            }
            if (!_terrainGeoCache.TryGetValue(key, out var geo) || !geo.complete)
            {
                var n = _w.Terrain.Normal(p);
                double slope = Math.Acos(MathD.Clamp(n.Y, -1, 1));
                var downslope = new Vec2(n.X, n.Z).Normalized();
                double laplacian = CoverageEnvironment.TerrainLaplacian(_w, p);
                geo = (slope, downslope, laplacian, true);
                _terrainGeoCache[key] = geo;
            }
            var (ti, tj) = CoverageSpec.TileOf(gx, gz);
            var tileFlora = _stepTileFlora.GetOrAdd((ti, tj), static (tCoord, state) =>
            {
                var (w, tc) = state;
                var tileCenter = new Vec2((tc.ti + 0.5) * CoverageSpec.TileEdge * CoverageSpec.CellSize, (tc.tj + 0.5) * CoverageSpec.TileEdge * CoverageSpec.CellSize);
                var list = new List<Flora.FloraIndividual>(8);
                w.Flora.Neighbours(tileCenter, 2.65, list);
                return list;
            }, (_w, (ti, tj)));

            var env = CoverageEnvironment.Sample(_w, p, _stepWaterDist, (geo.slope, geo.downslope, geo.laplacian), tileFlora);
            _stepEnvCache[key] = env;
            return env;
        }
        return CoverageEnvironment.Sample(_w, CellCentre(gx, gz));
    }

    public double? SampleTerrainLaplacian(int gx, int gz) =>
        CoverageEnvironment.TerrainLaplacian(_w, CellCentre(gx, gz));

    public IReadOnlyList<ShadeCaster> GetTileShadeCasters(int ti, int tj)
    {
        return _stepTileShadeCasters.GetOrAdd((ti, tj), static (tCoord, state) =>
        {
            var (w, tc) = state;
            var tileCenter = new Vec2((tc.ti + 0.5) * CoverageSpec.TileEdge * CoverageSpec.CellSize, (tc.tj + 0.5) * CoverageSpec.TileEdge * CoverageSpec.CellSize);
            var list = new List<Flora.FloraIndividual>(8);
            w.Flora.Neighbours(tileCenter, 2.65, list);
            var casters = new List<ShadeCaster>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                var f = list[i];
                var sp = w.Content.FloraById(f.SpeciesId);
                if (sp == null || sp.Archetype != "plant" || sp.Woody != null) continue;
                double r = f.Radius(sp);
                if (r > 1e-6) casters.Add(new ShadeCaster(f.X, f.Z, r, 1.0 / r));
            }
            return casters;
        }, (_w, (ti, tj)));
    }

    public (double Moisture, double Humidity, double Light, double Nutrients) SamplePhysiology(int gx, int gz) =>
        SamplePhysiology(gx, gz, null, null);

    public (double Moisture, double Humidity, double Light, double Nutrients) SamplePhysiology(int gx, int gz, double? laplacian) =>
        SamplePhysiology(gx, gz, laplacian, null);

    public (double Moisture, double Humidity, double Light, double Nutrients) SamplePhysiology(int gx, int gz, double? laplacian, IReadOnlyList<ShadeCaster>? casters)
    {
        if (_inStep)
        {
            var p = CellCentre(gx, gz);
            var (ti, tj) = CoverageSpec.TileOf(gx, gz);
            casters ??= GetTileShadeCasters(ti, tj);

            if (!laplacian.HasValue)
            {
                laplacian = _stepTileLaplacian.GetOrAdd((ti, tj), static (tCoord, state) =>
                {
                    var (w, tc) = state;
                    var tileCenter = new Vec2((tc.ti + 0.5) * CoverageSpec.TileEdge * CoverageSpec.CellSize, (tc.tj + 0.5) * CoverageSpec.TileEdge * CoverageSpec.CellSize);
                    return CoverageEnvironment.TerrainLaplacian(w, tileCenter);
                }, (_w, (ti, tj)));
            }

            return CoverageEnvironment.SamplePhysiology(_w, p, _stepWaterDist, laplacian, casters, _moistureBonus);
        }
        return CoverageEnvironment.SamplePhysiology(_w, CellCentre(gx, gz), laplacian: laplacian, bonus: _moistureBonus);
    }

    public (Vec2 DownslopeDir, Vec2 MoistureGradient) SampleGradient(int gx, int gz)
    {
        var p = CellCentre(gx, gz);
        long key = ((long)gx << 32) | (uint)gz;
        Vec2 downslope;
        if (_terrainGeoCache.TryGetValue(key, out var geo) && geo.complete)
        {
            downslope = geo.downslope;
        }
        else
        {
            var n = _w.Terrain.Normal(p);
            double slope = Math.Acos(MathD.Clamp(n.Y, -1, 1));
            downslope = new Vec2(n.X, n.Z).Normalized();
            double laplacian = geo.laplacian != 0 ? geo.laplacian : CoverageEnvironment.TerrainLaplacian(_w, p);
            _terrainGeoCache[key] = (slope, downslope, laplacian, true);
        }
        var gradient = CoverageEnvironment.SampleMoistureGradient(_w, p);
        return (downslope, gradient);
    }

    public CoverageSubstrate Substrate(int gx, int gz)
    {
        if (_inStep)
        {
            long key = ((long)gx << 32) | (uint)gz;
            if (_stepSubstrateCache.TryGetValue(key, out var cached)) return cached;
            var p = CellCentre(gx, gz);
            var sub = CoverageEnvironment.SampleSubstrate(_w, p);
            _stepSubstrateCache[key] = sub;
            return sub;
        }
        return CoverageEnvironment.SampleSubstrate(_w, CellCentre(gx, gz));
    }

    // ------------------------------------------------------------------ IDetritusSink

    public void AddDetritus(int gx, int gz, double amount)
    {
        var p = CellCentre(gx, gz);
        int cell = _w.Grid.NearestDomainCell(p);
        if (cell >= 0) _w.Fields.Detritus.Add(cell, amount);
    }

    public void AddMoistureBonus(int gx, int gz, double amount)
    {
        var p = CellCentre(gx, gz);
        CoverageEnvironment.MoistureBonusOf(_w).Add(p, amount);
    }

    private static Vec2 CellCentre(int gx, int gz) => new((gx + 0.5) * CoverageSpec.CellSize, (gz + 0.5) * CoverageSpec.CellSize);

    // ------------------------------------------------------------------ step (scheduler, flora cadence)

    public bool AsyncMode { get; set; } = false;
    private Task? _stepTask;

    public void WaitPending()
    {
        var task = _stepTask;
        if (task != null && !task.IsCompleted)
        {
            task.GetAwaiter().GetResult();
        }
    }

    public void Step(double dtSeconds)
    {
        WaitPending();
        RunStepPart(dtSeconds, runMat: true, runLichen: true);
    }

    // The game schedules these on adjacent simulation ticks. When AsyncMode is enabled,
    // each step runs on a threadpool task so the Godot main thread never hitches.
    public void StepMat(double dtSeconds)
    {
        if (AsyncMode)
        {
            if (_stepTask != null && !_stepTask.IsCompleted) return;
            _stepTask = Task.Run(() => RunStepPart(dtSeconds, runMat: true, runLichen: false));
        }
        else
        {
            RunStepPart(dtSeconds, runMat: true, runLichen: false);
        }
    }

    public void StepLichen(double dtSeconds)
    {
        if (AsyncMode)
        {
            if (_stepTask != null && !_stepTask.IsCompleted) return;
            _stepTask = Task.Run(() => RunStepPart(dtSeconds, runMat: false, runLichen: true));
        }
        else
        {
            RunStepPart(dtSeconds, runMat: false, runLichen: true);
        }
    }

    private void RunStepPart(double dtSeconds, bool runMat, bool runLichen)
    {
        double dtDays = dtSeconds / SimUnits.Day;
        var phaseClock = Stopwatch.StartNew();
        _inStep = true;
        _stepWaterDist = _w.Water.DistanceToWater();
        double waterMs = phaseClock.Elapsed.TotalMilliseconds;
        phaseClock.Restart();
        _stepEnvCache.Clear();
        _stepSubstrateCache.Clear();
        _stepTileFlora.Clear();
        _stepTileShadeCasters.Clear();
        try
        {
            if (runMat && _matSpecies.Count > 0)
                LastMatStats = MatRules.Step(_w.Coverage.Mat, this, _matSpecies, dtDays, _step, _w.Seed, this);
            double matMs = phaseClock.Elapsed.TotalMilliseconds;
            phaseClock.Restart();
            if (runLichen && _lichenSpecies.Count > 0)
                LastLichenStats = LichenRules.Step(_w.Coverage.Crust, this, _lichenSpecies, dtDays, _step, _w.Seed);
            LastPhaseTimes = (waterMs, matMs, phaseClock.Elapsed.TotalMilliseconds);
            if (runLichen) _step++;
        }
        finally
        {
            _inStep = false;
            _stepWaterDist = null;
            _stepEnvCache.Clear();
            _stepSubstrateCache.Clear();
            _stepTileFlora.Clear();
            _stepTileShadeCasters.Clear();
            _stepTileLaplacian.Clear();
        }
    }
    // ------------------------------------------------------------------ initial seeding (world creation)

    /// <summary>Seeds a few small discs per coverage species where the terrain already suits it, so growth (not
    /// individual placement) is what fills the island out from world creation onward.</summary>
    public void SeedInitial()
    {
        const int DiscsPerSpecies = 5;
        const double MinSeparation = 1.2; // m, keeps starter discs spread across the island
        const int DiscRadiusCells = 4;    // ~8 cm discs

        foreach (var mp in _matSpecies)
            SeedSpecies(_w.Coverage.Mat, mp.OccupantId, mp.SeedBiomass, DiscsPerSpecies, MinSeparation, DiscRadiusCells,
                p => IsMatSuitable(mp, p));

        foreach (var lp in _lichenSpecies)
            SeedSpecies(_w.Coverage.Crust, lp.OccSlot, lp.SeedBiomass, DiscsPerSpecies, MinSeparation, DiscRadiusCells,
                p => IsLichenSuitable(lp, p));

        if (_plasmodiumColony != null && _plasmodiumSpeciesId != null)
            SeedPlasmodiumInitial();
    }

    private bool IsMatSuitable(MatParams mp, Vec2 p)
    {
        var e = CoverageEnvironment.Sample(_w, p);
        if (e.Substrate == CoverageSubstrate.Water) return false;
        if (e.Moisture < mp.HardMinMoisture) return false;
        double gW = WaterBalance.GrowthMultiplierWater(e.Moisture, mp);
        double gL = WaterBalance.GrowthMultiplierLight(e.Light, mp);
        return gW * gL > 0.15;
    }

    private bool IsPlasmodiumSuitable(Vec2 p)
    {
        var e = CoverageEnvironment.Sample(_w, p);
        if (e.Substrate == CoverageSubstrate.Water) return false;
        if (e.Moisture < 0.25) return false;
        int cell = _w.Grid.NearestDomainCell(p);
        if (cell < 0) return false;
        double det = _w.Fields.Detritus[cell];
        return det > 0.02;
    }

    private void SeedPlasmodiumPatch(Vec2 p, int radiusCells)
    {
        if (_plasmodiumColony == null) return;
        var seededCells = new List<(int gx, int gz)>();
        var (gx0, gz0) = CoverageSpec.CellOf(p);

        // Find direction of highest detritus/moisture to orient the foraging front
        Vec2 bestDir = new Vec2(1, 0);
        double bestVal = double.MinValue;
        for (int a = 0; a < 8; a++)
        {
            double angle = a * Math.PI * 0.25;
            Vec2 testP = p + new Vec2(Math.Cos(angle), Math.Sin(angle)) * (radiusCells * CoverageSpec.CellSize * 1.5);
            int domainCell = _w.Grid.NearestDomainCell(testP);
            double val = domainCell >= 0 ? _w.Fields.Detritus[domainCell] * 2.0 + _w.Fields.Moisture.Sample(testP) : 0;
            if (val > bestVal)
            {
                bestVal = val;
                bestDir = new Vec2(Math.Cos(angle), Math.Sin(angle)).Normalized();
            }
        }

        for (int dz = -radiusCells; dz <= radiusCells; dz++)
        for (int dx = -radiusCells; dx <= radiusCells; dx++)
        {
            double d = Math.Sqrt(dx * dx + dz * dz);
            if (d > radiusCells) continue;

            int gx = gx0 + dx, gz = gz0 + dz;
            Vec2 cPos = CellCentre(gx, gz);
            if (!IsPlasmodiumSuitable(cPos)) continue;

            double u = d / radiusCells;
            Vec2 offset = new Vec2(dx, dz);
            Vec2 dirNorm = d > 0.001 ? offset.Normalized() : bestDir;
            double frontAlign = dirNorm.Dot(bestDir);
            double angle = Math.Atan2(dz, dx);

            byte flags = 0;
            byte w;
            float b;

            bool isFront = frontAlign > 0.60 && u > 0.80;
            if (isFront)
            {
                flags = (byte)CoverageFlags.Front;
                b = (float)(1.2 + 0.3 * frontAlign);
                w = (byte)Math.Clamp(60 + 40 * frontAlign, 50, 100);
            }
            else
            {
                double cordWave = Math.Cos(angle * 5.0 + 0.35 * Math.Sin(d * 0.9));
                bool isTrunkCord = Math.Abs(cordWave) > 0.48;
                double bridgeWave = Math.Sin(d * 1.4 + angle * 3.0);
                bool isLoopBridge = Math.Abs(bridgeWave) > 0.75 && u > 0.30 && u < 0.85;
                bool isCore = u < 0.28;

                if (isCore)
                {
                    w = 210;
                    b = 1.4f;
                }
                else if (isTrunkCord || isLoopBridge)
                {
                    double widthFactor = 0.5 + 0.5 * Math.Abs(cordWave);
                    w = (byte)Math.Clamp(150 + 65 * widthFactor, 120, 225);
                    b = (float)(1.0 + 0.35 * widthFactor);
                }
                else
                {
                    bool isTrailing = frontAlign < -0.5 && u > 0.75;
                    if (isTrailing)
                    {
                        flags = (byte)CoverageFlags.Rim;
                        w = 15;
                        b = 0.20f;
                    }
                    else
                    {
                        w = 25;
                        b = 0.35f;
                    }
                }
            }

            _w.Coverage.Plasmodium.SetCell(gx, gz, occ: 1, b: b, w: w, 0, 0, flags, (byte)Math.Min(255, (int)(d + 1)));
            seededCells.Add((gx, gz));
        }

        if (seededCells.Count > 0)
        {
            _plasmodiumColony.Seed(seededCells);
        }
    }

    private void SeedPlasmodiumInitial()
    {
        if (_plasmodiumColony == null) return;
        var cells = _w.Grid.DomainCells;
        if (cells.Length == 0) return;
        var candidates = cells
            .Select(c => (_w.Grid.CellCenter(c), _w.Fields.Detritus[c]))
            .Where(x => IsPlasmodiumSuitable(x.Item1))
            .OrderByDescending(x => x.Item2)
            .Select(x => x.Item1)
            .ToList();
        var chosen = new List<Vec2>();
        foreach (var p in candidates)
        {
            if (chosen.Count >= 3) break;
            bool tooClose = false;
            foreach (var c in chosen) if (Vec2.Distance(c, p) < 1.8) { tooClose = true; break; }
            if (tooClose) continue;
            chosen.Add(p);
        }
        foreach (var p in chosen)
        {
            SeedPlasmodiumPatch(p, radiusCells: 10);
        }
    }

    public void StepPlasmodium(double dtSeconds)
    {
        if (_plasmodiumColony == null || _plasmodiumNetwork == null || _plasmodiumAttractant == null || _plasmodiumEnv == null || _plasmodiumLifecycle == null) return;
        var occupied = _plasmodiumColony.CellId.Keys.ToList();
        if (occupied.Count == 0) return;
        var sources = new HashSet<(int cx, int cz)>();
        foreach (var (gx, gz) in occupied)
        {
            var coarse = Attractant.CoarseOf(gx, gz);
            if (_plasmodiumEnv.Detritus(gx, gz) > 0.015) sources.Add(coarse);
        }
        if (sources.Count < 2)
        {
            int minGx = int.MaxValue, maxGx = int.MinValue, minGz = int.MaxValue, maxGz = int.MinValue;
            foreach (var (gx, gz) in occupied)
            {
                if (gx < minGx) minGx = gx;
                if (gx > maxGx) maxGx = gx;
                if (gz < minGz) minGz = gz;
                if (gz > maxGz) maxGz = gz;
            }
            sources.Add(Attractant.CoarseOf(minGx, minGz));
            sources.Add(Attractant.CoarseOf(maxGx, maxGz));
        }
        _plasmodiumNetwork.SetSources(sources);
        double dt = Math.Clamp(dtSeconds, 0.1, 5.0);
        _plasmodiumNetwork.Step(_w.Coverage.Plasmodium, occupied, dt);
        _plasmodiumColony.Step(_w.Coverage.Plasmodium, _plasmodiumAttractant, _plasmodiumEnv, _step, dt, _plasmodiumNetwork);
        _plasmodiumColony.Relabel(_plasmodiumColony.CellId.Keys.ToList());
        _plasmodiumLifecycle.Step(_w.Coverage.Plasmodium, _plasmodiumColony, _plasmodiumNetwork, _plasmodiumEnv, _step, dt);
        foreach (var ((gx, gz), mass) in _plasmodiumColony.Mass)
        {
            _w.Coverage.Plasmodium.SetB(gx, gz, (float)Math.Clamp(mass, 0.1, 5.0));
        }
        _w.Coverage.Plasmodium.Advance();
    }

    private bool IsLichenSuitable(LichenParams lp, Vec2 p)
    {
        var e = CoverageEnvironment.Sample(_w, p);
        if (!lp.AllowsSubstrate(e.Substrate)) return false;
        double gW = LichenWater.GrowthWaterFactor(e.Moisture, lp.WMin, lp.WOpt);
        double gL = LichenWater.GrowthLightFactor(e.Light, lp.KLight);
        return gW * gL > 0.05;
    }

    private void SeedSpecies(CoverageLayer layer, byte occ, double seedB, int discCount, double minSeparation, int radiusCells, Func<Vec2, bool> suitable)
    {
        var rng = Rng.Stream(_w.Seed, "coverage.seed." + layer.Id + "." + occ);
        var cells = _w.Grid.DomainCells;
        if (cells.Length == 0) return;

        // Sample a bounded number of shuffled candidates rather than every domain cell (island-size independent).
        var order = Enumerable.Range(0, cells.Length).ToList();
        for (int i = order.Count - 1; i > 0; i--) { int j = rng.NextInt(i + 1); (order[i], order[j]) = (order[j], order[i]); }

        var chosen = new List<Vec2>();
        int budget = Math.Min(cells.Length, 4000);
        for (int k = 0; k < budget && chosen.Count < discCount; k++)
        {
            var p = _w.Grid.CellCenter(cells[order[k]]);
            if (!suitable(p)) continue;
            bool tooClose = false;
            foreach (var c in chosen) if (Vec2.Distance(c, p) < minSeparation) { tooClose = true; break; }
            if (tooClose) continue;
            chosen.Add(p);
        }

        byte seedW = (byte)Math.Round(0.6 * 255);
        foreach (var p in chosen)
        {
            var (gx0, gz0) = CoverageSpec.CellOf(p);
            for (int dz = -radiusCells; dz <= radiusCells; dz++)
            for (int dx = -radiusCells; dx <= radiusCells; dx++)
            {
                double angle = Math.Atan2(dz, dx);
                double wobble = 1.0 + 0.22 * Math.Sin(angle * 3.0 + occ * 1.7) + 0.14 * Math.Cos(angle * 5.0 + occ);
                double rLim = radiusCells * wobble;
                if (dx * dx + dz * dz > rLim * rLim) continue;
                int gx = gx0 + dx, gz = gz0 + dz;
                if (!suitable(CellCentre(gx, gz)) || layer.GetOcc(gx, gz) != 0) continue;
                layer.SetCell(gx, gz, occ, (float)seedB, seedW, 0, 0, 0, 0);
            }
        }
    }

    // ------------------------------------------------------------------ player introduction / removal (tools)

    /// <summary>True when speciesId (a coverage species) may be seeded at p, using the same habitat/substrate
    /// gates as <see cref="SeedInitial"/>. False with a human reason otherwise (unknown/non-coverage species,
    /// wrong substrate, too dry/dark, etc).</summary>
    public bool CanSeed(string speciesId, Vec2 p, out string reason)
    {
        foreach (var mp in _matSpecies)
            if (mp.Name == speciesId)
            {
                if (IsMatSuitable(mp, p)) { reason = "ok"; return true; }
                var e = CoverageEnvironment.Sample(_w, p);
                reason = e.Substrate == CoverageSubstrate.Water ? "underwater" :
                    e.Moisture < mp.HardMinMoisture ? "too dry here" : "conditions too poor here";
                return false;
            }
        foreach (var lp in _lichenSpecies)
            if (_lichenSpeciesId[lp.OccSlot] == speciesId)
            {
                if (IsLichenSuitable(lp, p)) { reason = "ok"; return true; }
                var e = CoverageEnvironment.Sample(_w, p);
                reason = !lp.AllowsSubstrate(e.Substrate) ? "wrong substrate (needs rock, log or stable soil)" : "conditions too poor here";
                return false;
            }
        if (_plasmodiumSpeciesId != null && speciesId == _plasmodiumSpeciesId)
        {
            if (IsPlasmodiumSuitable(p)) { reason = "ok"; return true; }
            var e = CoverageEnvironment.Sample(_w, p);
            reason = e.Substrate == CoverageSubstrate.Water ? "underwater" :
                e.Moisture < 0.25 ? "too dry here" : "needs dead organic matter (detritus)";
            return false;
        }
        reason = $"unknown coverage species '{speciesId}'";
        return false;
    }

    /// <summary>Seeds a small clump of speciesId centred on p, at seed biomass, filling every suitable
    /// unoccupied cell within radius (m). Caller must have checked <see cref="CanSeed"/> first.</summary>
    public void SeedClump(string speciesId, Vec2 center, double radius)
    {
        WaitPending();
        int radiusCells = Math.Max(1, (int)Math.Round(radius / CoverageSpec.CellSize));
        var (gx0, gz0) = CoverageSpec.CellOf(center);

        foreach (var mp in _matSpecies)
            if (mp.Name == speciesId)
            {
                SeedClumpCells(_w.Coverage.Mat, mp.OccupantId, mp.SeedBiomass, gx0, gz0, radiusCells, q => IsMatSuitable(mp, q));
                return;
            }
        foreach (var lp in _lichenSpecies)
            if (_lichenSpeciesId[lp.OccSlot] == speciesId)
            {
                SeedClumpCells(_w.Coverage.Crust, lp.OccSlot, lp.SeedBiomass, gx0, gz0, radiusCells, q => IsLichenSuitable(lp, q));
                return;
            }
        if (_plasmodiumSpeciesId != null && speciesId == _plasmodiumSpeciesId && _plasmodiumColony != null)
        {
            SeedPlasmodiumPatch(center, Math.Max(5, radiusCells));
            return;
        }
    }

    private void SeedClumpCells(CoverageLayer layer, byte occ, double seedB, int gx0, int gz0, int radiusCells, Func<Vec2, bool> suitable)
    {
        byte seedW = (byte)Math.Round(0.6 * 255);
        for (int dz = -radiusCells; dz <= radiusCells; dz++)
        for (int dx = -radiusCells; dx <= radiusCells; dx++)
        {
            double angle = Math.Atan2(dz, dx);
            double wobble = 1.0 + 0.22 * Math.Sin(angle * 3.0 + occ * 1.7) + 0.14 * Math.Cos(angle * 5.0 + occ);
            double rLim = radiusCells * wobble;
            if (dx * dx + dz * dz > rLim * rLim) continue;
            int gx = gx0 + dx, gz = gz0 + dz;
            if (layer.GetOcc(gx, gz) != 0 || !suitable(CellCentre(gx, gz))) continue;
            layer.SetCell(gx, gz, occ, (float)seedB, seedW, 0, 0, 0, 0);
        }
    }

    /// <summary>Clears all coverage cells (any species, or only speciesFilter if given) within radius (m) of center.</summary>
    public int ClearDisc(Vec2 center, double radius, string? speciesFilter = null)
    {
        WaitPending();
        int radiusCells = Math.Max(1, (int)Math.Round(radius / CoverageSpec.CellSize));
        var (gx0, gz0) = CoverageSpec.CellOf(center);
        int cleared = 0;
        cleared += ClearDiscLayer(_w.Coverage.Mat, gx0, gz0, radiusCells, occ => _matSpeciesId.GetValueOrDefault(occ) is { } id && (speciesFilter == null || id == speciesFilter));
        cleared += ClearDiscLayer(_w.Coverage.Crust, gx0, gz0, radiusCells, occ => _lichenSpeciesId.GetValueOrDefault(occ) is { } id && (speciesFilter == null || id == speciesFilter));
        cleared += ClearDiscLayer(_w.Coverage.Plasmodium, gx0, gz0, radiusCells, occ => occ == 1 && (speciesFilter == null || _plasmodiumSpeciesId == speciesFilter));
        return cleared;
    }

    private static int ClearDiscLayer(CoverageLayer layer, int gx0, int gz0, int radiusCells, Func<byte, bool> matches)
    {
        int cleared = 0;
        for (int dz = -radiusCells; dz <= radiusCells; dz++)
        for (int dx = -radiusCells; dx <= radiusCells; dx++)
        {
            if (dx * dx + dz * dz > radiusCells * radiusCells) continue;
            int gx = gx0 + dx, gz = gz0 + dz;
            byte occ = layer.GetOcc(gx, gz);
            if (occ == 0 || !matches(occ)) continue;
            layer.SetCell(gx, gz, 0, 0, 0, 0, 0, 0, 0);
            cleared++;
        }
        return cleared;
    }

    // ------------------------------------------------------------------ queries (stats/catalog, §7, §9)

    /// <summary>Resolves an occupied coverage cell to its content species. Occupant slots are local to each
    /// layer; zero and unsupported layers have no species.</summary>
    public string? SpeciesId(CoverageLayerId layer, byte occupant) => layer switch
    {
        CoverageLayerId.Mat => _matSpeciesId.GetValueOrDefault(occupant),
        CoverageLayerId.Crust => _lichenSpeciesId.GetValueOrDefault(occupant),
        CoverageLayerId.Plasmodium => occupant == 1 ? _plasmodiumSpeciesId : null,
        _ => null,
    };

    /// <summary>Total covered area (m²) for a species id, across whichever coverage layer it occupies. 0 for a
    /// non-coverage species or one with nothing grown yet.</summary>
    public double CoveredArea(string speciesId)
    {
        double cellArea = CoverageSpec.CellSize * CoverageSpec.CellSize;
        double total = 0;
        foreach (var (occ, id) in _matSpeciesId)
            if (id == speciesId) total += CountOccupied(_w.Coverage.Mat, occ) * cellArea;
        foreach (var (occ, id) in _lichenSpeciesId)
            if (id == speciesId) total += CountOccupied(_w.Coverage.Crust, occ) * cellArea;
        if (_plasmodiumSpeciesId != null && speciesId == _plasmodiumSpeciesId)
            total += CountOccupied(_w.Coverage.Plasmodium, 1) * cellArea;
        return total;
    }

    private static long CountOccupied(CoverageLayer layer, byte occ)
    {
        long n = 0;
        foreach (var t in layer.Tiles)
            for (int i = 0; i < CoverageTile.N; i++)
                if (t.Occ[i] == occ) n++;
        return n;
    }
}

internal sealed class PlasmodiumWorldEnv : IPlasmodiumEnvironment
{
    private readonly VivariumWorld _w;
    public PlasmodiumWorldEnv(VivariumWorld w) => _w = w;

    public double Moisture(int gx, int gz)
    {
        var p = new Vec2((gx + 0.5) * CoverageSpec.CellSize, (gz + 0.5) * CoverageSpec.CellSize);
        return _w.Fields.Moisture.Sample(p);
    }

    public double Detritus(int gx, int gz)
    {
        var p = new Vec2((gx + 0.5) * CoverageSpec.CellSize, (gz + 0.5) * CoverageSpec.CellSize);
        int cell = _w.Grid.NearestDomainCell(p);
        return cell >= 0 ? _w.Fields.Detritus[cell] : 0;
    }

    public double TakeDetritus(int gx, int gz, double amount)
    {
        var p = new Vec2((gx + 0.5) * CoverageSpec.CellSize, (gz + 0.5) * CoverageSpec.CellSize);
        int cell = _w.Grid.NearestDomainCell(p);
        if (cell < 0 || amount <= 0) return 0;
        double current = _w.Fields.Detritus[cell];
        if (current <= 0) return 0;
        double taken = Math.Min(current, amount);
        _w.Fields.Detritus[cell] = current - taken;
        return taken;
    }
}
