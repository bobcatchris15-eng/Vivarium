using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Flora;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>
/// Immutable/copied versioned presentation snapshot of flora, coverage, and environment state.
/// Decouples background render and mesh build tasks from the mutable authoritative simulation,
/// ensuring concurrent worker threads never race live collections.
/// </summary>
public sealed class RenderSnapshot
{
    private static long _globalVersion;

    public long Version { get; }
    public long SimTick { get; }
    public double SimSeconds { get; }
    public int TerrainVersion { get; }
    public int PropsVersion { get; }
    public int FloraVersion { get; }
    public int DeadFloraVersion { get; }
    public double WaterTable { get; }

    public IReadOnlyList<FloraSnapshotItem> Flora { get; }
    public IReadOnlyList<DeadFloraSnapshotItem> DeadFlora { get; }
    public CoverageSnapshot Coverage { get; }
    public PropsSnapshot Props { get; }

    private readonly Dictionary<EntityId, FloraSnapshotItem> _floraById;
    private readonly Dictionary<EntityId, DeadFloraSnapshotItem> _deadFloraById;

    public int LivingCount => Flora.Count;
    public int DeadCount => DeadFlora.Count;

    public bool TryGetFlora(EntityId id, out FloraSnapshotItem item) => _floraById.TryGetValue(id, out item);
    public bool TryGetDeadFlora(EntityId id, out DeadFloraSnapshotItem item) => _deadFloraById.TryGetValue(id, out item);

    public RenderSnapshot(
        long version,
        long simTick,
        double simSeconds,
        int terrainVersion,
        int propsVersion,
        int floraVersion,
        int deadFloraVersion,
        double waterTable,
        List<FloraSnapshotItem> flora,
        List<DeadFloraSnapshotItem> deadFlora,
        CoverageSnapshot coverage,
        PropsSnapshot props)
    {
        Version = version;
        SimTick = simTick;
        SimSeconds = simSeconds;
        TerrainVersion = terrainVersion;
        PropsVersion = propsVersion;
        FloraVersion = floraVersion;
        DeadFloraVersion = deadFloraVersion;
        WaterTable = waterTable;
        Flora = flora;
        DeadFlora = deadFlora;
        Coverage = coverage;
        Props = props;

        _floraById = new Dictionary<EntityId, FloraSnapshotItem>(flora.Count);
        for (int i = 0; i < flora.Count; i++)
        {
            _floraById[flora[i].Id] = flora[i];
        }

        _deadFloraById = new Dictionary<EntityId, DeadFloraSnapshotItem>(deadFlora.Count);
        for (int i = 0; i < deadFlora.Count; i++)
        {
            _deadFloraById[deadFlora[i].Id] = deadFlora[i];
        }
    }

    /// <summary>
    /// Captures an immutable snapshot of render-relevant simulation state on the calling thread.
    /// </summary>
    public static RenderSnapshot Capture(VivariumWorld w)
    {
        long version = System.Threading.Interlocked.Increment(ref _globalVersion);
        long tick = w.Clock.Tick;
        double simSec = w.Clock.SimSeconds;
        int terrainVer = w.Terrain.Version;
        int propsVer = w.Props.Version;
        int floraVer = w.Flora.Version;
        int deadFloraVer = w.DeadFlora.Version;
        double waterTable = w.Water.WaterTable;

        // Copy living flora items
        var floraItems = w.Flora.Items;
        var floraList = new List<FloraSnapshotItem>(floraItems.Count);
        for (int i = 0; i < floraItems.Count; i++)
        {
            var f = floraItems[i];
            floraList.Add(new FloraSnapshotItem(f));
        }

        // Copy dead flora items
        var deadItems = w.DeadFlora.Items;
        var deadList = new List<DeadFloraSnapshotItem>(deadItems.Count);
        for (int i = 0; i < deadItems.Count; i++)
        {
            var d = deadItems[i];
            deadList.Add(new DeadFloraSnapshotItem(d));
        }

        // Snapshot coverage layers
        var coverage = new CoverageSnapshot(
            w.Coverage.Mat.Step,
            w.Coverage.Crust.Step,
            w.Coverage.Plasmodium.Step,
            w.Coverage.Mat.TileCount,
            w.Coverage.Crust.TileCount,
            w.Coverage.Plasmodium.TileCount);

        // Snapshot props
        var logs = w.Props.Logs;
        var logList = new List<LogSnapshotItem>(logs.Count);
        for (int i = 0; i < logs.Count; i++)
        {
            var l = logs[i];
            logList.Add(new LogSnapshotItem(l.Position, l.RotationY, l.Length, l.Radius, l.Y));
        }

        var rocks = w.Props.Rocks;
        var rockList = new List<RockSnapshotItem>(rocks.Count);
        for (int i = 0; i < rocks.Count; i++)
        {
            var r = rocks[i];
            rockList.Add(new RockSnapshotItem(r.Position, r.Y, r.SizeY, r.FootprintRadius, r.X, r.Z));
        }

        var props = new PropsSnapshot(logList, rockList);

        return new RenderSnapshot(
            version,
            tick,
            simSec,
            terrainVer,
            propsVer,
            floraVer,
            deadFloraVer,
            waterTable,
            floraList,
            deadList,
            coverage,
            props);
    }
}

/// <summary>
/// Copied, immutable snapshot of a single living plant individual.
/// </summary>
public readonly struct FloraSnapshotItem
{
    public EntityId Id { get; }
    public string SpeciesId { get; }
    public double X { get; }
    public double Z { get; }
    public Vec2 Position => new(X, Z);
    public double Age { get; }
    public double Biomass { get; }
    public double Health { get; }
    public PlantReproductiveStage ReproductiveStage { get; }
    public double FruitLoad { get; }
    public double LifespanFactor { get; }
    public bool Fruiting { get; }
    public EntityId ParentId { get; }
    public EntityId ColonyRoot { get; }
    public Color Tint { get; }
    public double HeightFactor { get; }
    public double MergeFactor { get; }
    public bool ClimberAttached { get; }
    public IReadOnlyList<ClimberSegmentSnapshot>? ClimberSegments { get; }

    public FloraSnapshotItem(FloraIndividual f)
    {
        Id = f.Id;
        SpeciesId = f.SpeciesId;
        X = f.X;
        Z = f.Z;
        Age = f.Age;
        Biomass = f.Biomass;
        Health = f.Health;
        ReproductiveStage = f.ReproductiveStage;
        FruitLoad = f.FruitLoad;
        LifespanFactor = f.LifespanFactor;
        Fruiting = f.Fruiting;
        ParentId = f.ParentId;
        ColonyRoot = f.ColonyRoot;
        Tint = f.Tint != null && f.Tint.Length >= 3
            ? new Color((float)f.Tint[0], (float)f.Tint[1], (float)f.Tint[2], 1f)
            : Colors.White;
        HeightFactor = f.HeightFactor;
        MergeFactor = f.MergeFactor;
        ClimberAttached = f.ClimberAttached;

        if (f.ClimberSegments != null && f.ClimberSegments.Count > 0)
        {
            var segs = new ClimberSegmentSnapshot[f.ClimberSegments.Count];
            for (int i = 0; i < f.ClimberSegments.Count; i++)
            {
                var s = f.ClimberSegments[i];
                segs[i] = new ClimberSegmentSnapshot(s.Position, s.Normal, s.Forward, s.ParentIndex, s.Attached, s.Senescent);
            }
            ClimberSegments = segs;
        }
        else
        {
            ClimberSegments = null;
        }
    }

    public FloraStage Stage(FloraSpeciesDef sp) =>
        Age < sp.MaturityAge ? FloraStage.Juvenile : Age > sp.Lifespan * LifespanFactor * 0.85 ? FloraStage.Senescent : FloraStage.Mature;

    public double BiomassFraction(FloraSpeciesDef sp) => MathD.Clamp01(Biomass / sp.MaxBiomass);

    public double Radius(FloraSpeciesDef sp) => sp.Colony != null
        ? sp.Colony.CellRadius * (0.9 + 0.3 * HeightFactor) * Math.Sqrt(MergeFactor)
        : sp.MinRadius + (sp.RadiusAtMax - sp.MinRadius) * Math.Sqrt(BiomassFraction(sp));

    public double Height(FloraSpeciesDef sp) => sp.Colony != null
        ? sp.Colony.MaxHeight * (0.15 + 0.85 * HeightFactor)
        : sp.Height * (0.45 + 0.55 * Math.Sqrt(BiomassFraction(sp)));
}

public readonly struct ClimberSegmentSnapshot
{
    public Vec3 Position { get; }
    public Vec3 Normal { get; }
    public Vec3 Forward { get; }
    public int ParentIndex { get; }
    public bool Attached { get; }
    public bool Senescent { get; }

    public ClimberSegmentSnapshot(Vec3 pos, Vec3 normal, Vec3 forward, int parentIdx, bool attached, bool senescent)
    {
        Position = pos;
        Normal = normal;
        Forward = forward;
        ParentIndex = parentIdx;
        Attached = attached;
        Senescent = senescent;
    }
}

/// <summary>
/// Copied, immutable snapshot of a dead vascular plant.
/// </summary>
public readonly struct DeadFloraSnapshotItem
{
    public EntityId Id { get; }
    public string SpeciesId { get; }
    public double X { get; }
    public double Z { get; }
    public Vec2 Position => new(X, Z);
    public double OriginalRadius { get; }
    public double OriginalHeight { get; }
    public double OriginalBiomass { get; }
    public double RemainingBiomass { get; }
    public double RemainingFraction => OriginalBiomass <= 1e-12 ? 0 : MathD.Clamp01(RemainingBiomass / OriginalBiomass);
    public DeadPlantStage Stage { get; }
    public double StageProgress { get; }
    public double CollapseHeading { get; }

    public DeadFloraSnapshotItem(DeadPlant d)
    {
        Id = d.Id;
        SpeciesId = d.SpeciesId;
        X = d.X;
        Z = d.Z;
        OriginalRadius = d.OriginalRadius;
        OriginalHeight = d.OriginalHeight;
        OriginalBiomass = d.OriginalBiomass;
        RemainingBiomass = d.RemainingBiomass;
        Stage = d.Stage;
        StageProgress = d.StageProgress;
        CollapseHeading = d.CollapseHeading;
    }
}

/// <summary>
/// Copied, immutable snapshot of coverage layer tokens and counts.
/// </summary>
public sealed class CoverageSnapshot
{
    public long MatStep { get; }
    public long CrustStep { get; }
    public long PlasmodiumStep { get; }
    public long MatVersion => MatStep;
    public long CrustVersion => CrustStep;
    public long PlasmodiumVersion => PlasmodiumStep;
    public int MatTileCount { get; }
    public int CrustTileCount { get; }
    public int PlasmodiumTileCount { get; }

    public CoverageSnapshot(long matStep, long crustStep, long plasStep, int matCount, int crustCount, int plasCount)
    {
        MatStep = matStep;
        CrustStep = crustStep;
        PlasmodiumStep = plasStep;
        MatTileCount = matCount;
        CrustTileCount = crustCount;
        PlasmodiumTileCount = plasCount;
    }
}

/// <summary>
/// Copied, immutable snapshot of world props for anchor computation.
/// </summary>
public sealed class PropsSnapshot
{
    public IReadOnlyList<LogSnapshotItem> Logs { get; }
    public IReadOnlyList<RockSnapshotItem> Rocks { get; }

    public PropsSnapshot(IReadOnlyList<LogSnapshotItem> logs, IReadOnlyList<RockSnapshotItem> rocks)
    {
        Logs = logs;
        Rocks = rocks;
    }
}

public readonly struct LogSnapshotItem
{
    public Vec2 Position { get; }
    public double RotationY { get; }
    public double Length { get; }
    public double Radius { get; }
    public double Y { get; }

    public LogSnapshotItem(Vec2 pos, double rotY, double len, double rad, double y)
    {
        Position = pos;
        RotationY = rotY;
        Length = len;
        Radius = rad;
        Y = y;
    }
}

public readonly struct RockSnapshotItem
{
    public Vec2 Position { get; }
    public double Y { get; }
    public double SizeY { get; }
    public double FootprintRadius { get; }
    public double X { get; }
    public double Z { get; }

    public RockSnapshotItem(Vec2 pos, double y, double sizeY, double footRad, double x, double z)
    {
        Position = pos;
        Y = y;
        SizeY = sizeY;
        FootprintRadius = footRad;
        X = x;
        Z = z;
    }
}
