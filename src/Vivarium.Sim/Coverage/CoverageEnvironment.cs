using System.Runtime.CompilerServices;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Fields;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Coverage;

/// <summary>Substrate classification for coverage rules (docs/overhaul/growth_models.md §2). Distinct from the
/// coarser gameplay <see cref="Substrate"/> — this one splits soil by disturbance history.</summary>
public enum CoverageSubstrate : byte { Rock, Log, Bark, StableSoil, LooseSoil, Gravel, Water }

/// <summary>
/// The only view coverage growth rules get of the world: everything below is derived, testable-with-synthetic-
/// inputs state (docs/overhaul/growth_models.md §2). Immutable per sample.
/// </summary>
public readonly struct MicroEnv
{
    public double Moisture { get; init; }
    public double Humidity { get; init; }
    public double Light { get; init; }
    public double Nutrients { get; init; }
    public double Detritus { get; init; }
    public CoverageSubstrate Substrate { get; init; }
    /// <summary>Terrain slope, radians (0 = flat).</summary>
    public double Slope { get; init; }
    /// <summary>Unit horizontal direction of steepest descent; zero vector on flat ground.</summary>
    public Vec2 DownslopeDir { get; init; }
    /// <summary>Spatial gradient of the coarse moisture field (per metre); drives spread anisotropy.</summary>
    public Vec2 MoistureGradient { get; init; }
}

/// <summary>
/// Per-environment-grid-cell moisture addend that growth rules (or tools) may write directly — e.g. a rule that
/// locally wicks water toward a thriving patch. Dense over <see cref="GridSpec.DomainCells"/>; added into
/// <see cref="CoverageEnvironment.Sample"/>'s moisture term and clamped there, never clamped in storage.
/// </summary>
public sealed class MoistureBonusField
{
    public GridSpec Grid { get; }
    public double[] Values { get; }

    public MoistureBonusField(GridSpec grid)
    {
        Grid = grid;
        Values = new double[grid.Count];
    }

    public double At(Vec2 p)
    {
        int c = Grid.NearestDomainCell(p);
        return c >= 0 ? Values[c] : 0;
    }

    public void Add(Vec2 p, double amount)
    {
        int c = Grid.NearestDomainCell(p);
        if (c >= 0) Values[c] += amount;
    }

    public double[] ExportDomainValues()
    {
        var a = new double[Grid.DomainCells.Length];
        for (int k = 0; k < a.Length; k++) a[k] = Values[Grid.DomainCells[k]];
        return a;
    }

    public void ImportDomainValues(double[] a)
    {
        if (a.Length != Grid.DomainCells.Length)
            throw new InvalidDataException($"moisture bonus expects {Grid.DomainCells.Length} values, got {a.Length}.");
        for (int k = 0; k < a.Length; k++)
        {
            double v = a[k];
            if (!double.IsFinite(v)) throw new InvalidDataException($"moisture bonus value {k} is not finite.");
            Values[Grid.DomainCells[k]] = v;
        }
    }
}

public readonly record struct ShadeCaster(double X, double Z, double Radius, double InvRadius);

/// <summary>
/// Samples the micro-environment coverage growth rules see (docs/overhaul/growth_models.md §2). Stateless except
/// for the two per-world side-tables (<see cref="SubstrateStability"/>, <see cref="MoistureBonusField"/>), which
/// are attached lazily via a weak table so this stays addable without widening <see cref="VivariumWorld"/>'s
/// own surface. <see cref="Sample"/> itself never allocates.
/// </summary>
public static class CoverageEnvironment
{
    private static readonly ConditionalWeakTable<VivariumWorld, SubstrateStability> Stability = new();
    private static readonly ConditionalWeakTable<VivariumWorld, MoistureBonusField> MoistureBonus = new();

    public static SubstrateStability StabilityOf(VivariumWorld w) => Stability.GetValue(w, ww => new SubstrateStability(ww.Grid));
    public static MoistureBonusField MoistureBonusOf(VivariumWorld w) => MoistureBonus.GetValue(w, ww => new MoistureBonusField(ww.Grid));

    private const double SeepageRadius = 0.03;         // 3 cm (§2)
    private const double ConcavitySampleStep = 0.15;   // m, terrain curvature finite-difference offset
    private const double ConcavityGain = 0.35;          // moisture per unit Laplacian (m^-1)
    private const double GradientStep = 0.15;           // m, moisture-field finite-difference offset

    /// <summary>Lightweight sample for physiology only: evaluates moisture, humidity, light, nutrients without substrate or slope.</summary>
    public static (double Moisture, double Humidity, double Light, double Nutrients) SamplePhysiology(VivariumWorld w, Vec2 p, double[]? waterDist = null, double? laplacian = null, IReadOnlyList<ShadeCaster>? shadeCasters = null, MoistureBonusField? bonus = null)
    {
        double d = double.PositiveInfinity;
        if (waterDist != null)
        {
            int c = w.Grid.NearestDomainCell(p);
            if (c >= 0) d = waterDist[c];
        }
        double moisture = SampleMoisture(w, p, laplacian, bonus, d);
        double humidity = SampleHumidityFast(moisture, d);
        double light = SampleLight(w, p, shadeCasters);
        double nutrients = w.Fields.Nutrients.Sample(p);
        return (moisture, humidity, light, nutrients);
    }

    private static double SampleHumidityFast(double moisture, double d)
    {
        double proximity = double.IsInfinity(d) ? 0 : Math.Exp(-d / 0.6);
        return MathD.Clamp01(0.7 * moisture + 0.3 * proximity);
    }

    /// <summary>Samples the full micro-environment at world position p. Deterministic given world state.</summary>
    public static MicroEnv Sample(VivariumWorld w, Vec2 p, double[]? waterDist = null, (double slope, Vec2 downslope, double laplacian)? terrainGeo = null, IReadOnlyList<Flora.FloraIndividual>? candidateFlora = null)
    {
        double now = w.Clock.SimSeconds;

        double moisture = SampleMoisture(w, p, terrainGeo?.laplacian);
        double humidity = SampleHumidity(w, p, moisture, waterDist);
        double light = SampleLight(w, p, candidateFlora);
        double nutrients = w.Fields.Nutrients.Sample(p);
        double detritus = w.Litter.DetritusAt(p);
        var substrate = ClassifySubstrate(w, p, now);
        double slope;
        Vec2 downslope;
        if (terrainGeo.HasValue)
        {
            slope = terrainGeo.Value.slope;
            downslope = terrainGeo.Value.downslope;
        }
        else
        {
            var n = w.Terrain.Normal(p);
            slope = Math.Acos(MathD.Clamp(n.Y, -1, 1));
            downslope = new Vec2(n.X, n.Z).Normalized();
        }
        var gradient = SampleMoistureGradient(w, p);

        return new MicroEnv
        {
            Moisture = moisture,
            Humidity = humidity,
            Light = light,
            Nutrients = nutrients,
            Detritus = detritus,
            Substrate = substrate,
            Slope = slope,
            DownslopeDir = downslope,
            MoistureGradient = gradient,
        };
    }

    // ------------------------------------------------------------------ moisture

    private static double SampleMoisture(VivariumWorld w, Vec2 p, double? laplacian = null, MoistureBonusField? bonus = null, double distToWater = -1)
    {
        if ((distToWater < 0 || distToWater <= 0.08) && HasNearbySeepage(w, p)) return 1;

        double bonusVal = bonus != null ? bonus.At(p) : MoistureBonusOf(w).At(p);
        double m = w.Fields.Moisture.Sample(p) + bonusVal;
        double lap = laplacian ?? TerrainLaplacian(w, p);
        m += ConcavityGain * MathD.Clamp(lap, -1, 1);
        return MathD.Clamp01(m);
    }

    private static readonly Vec2[] SeepageOffsets =
    {
        new(SeepageRadius, 0),
        new(0, SeepageRadius),
        new(-SeepageRadius, 0),
        new(0, -SeepageRadius),
    };

    /// <summary>Water within 3 cm of p (own cell or a small ring around it) saturates the ground (§2).</summary>
    private static bool HasNearbySeepage(VivariumWorld w, Vec2 p)
    {
        if (w.Water.DepthAt(p) > 0) return true;
        for (int k = 0; k < SeepageOffsets.Length; k++)
        {
            if (w.Water.DepthAt(p + SeepageOffsets[k]) > 0) return true;
        }
        return false;
    }

    /// <summary>Discrete terrain Laplacian: positive in a bowl (concave up, wetter), negative on a ridge.</summary>
    internal static double TerrainLaplacian(VivariumWorld w, Vec2 p)
    {
        double h = ConcavitySampleStep;
        double c = w.Terrain.Height(p);
        double sum = w.Terrain.Height(p + new Vec2(h, 0)) + w.Terrain.Height(p - new Vec2(h, 0))
                   + w.Terrain.Height(p + new Vec2(0, h)) + w.Terrain.Height(p - new Vec2(0, h)) - 4 * c;
        return sum / (h * h);
    }

    internal static Vec2 SampleMoistureGradient(VivariumWorld w, Vec2 p)
    {
        double h = GradientStep;
        double mx1 = w.Fields.Moisture.Sample(p + new Vec2(h, 0)), mx0 = w.Fields.Moisture.Sample(p - new Vec2(h, 0));
        double mz1 = w.Fields.Moisture.Sample(p + new Vec2(0, h)), mz0 = w.Fields.Moisture.Sample(p - new Vec2(0, h));
        return new Vec2((mx1 - mx0) / (2 * h), (mz1 - mz0) / (2 * h));
    }

    private static double SampleHumidity(VivariumWorld w, Vec2 p, double moisture, double[]? waterDist = null)
    {
        var dist = waterDist ?? w.Water.DistanceToWater();
        int c = w.Grid.NearestDomainCell(p);
        double d = c >= 0 ? dist[c] : double.PositiveInfinity;
        double proximity = double.IsInfinity(d) ? 0 : Math.Exp(-d / 0.6);
        return MathD.Clamp01(0.7 * moisture + 0.3 * proximity);
    }

    // ------------------------------------------------------------------ light

    [ThreadStatic] private static List<Flora.FloraIndividual>? _lightBuf;

    /// <summary>
    /// Structural tree/shrub canopy comes from FloraSystem. The pre-existing low vascular-plant microshade stays
    /// local to coverage growth so ordinary herbs do not turn every FloraSystem suitability check into a broad
    /// neighbour scan.
    /// </summary>
    private static double SampleLight(VivariumWorld w, Vec2 p, IReadOnlyList<Flora.FloraIndividual>? candidateFlora)
    {
        if (candidateFlora == null) return SampleLight(w, p, (IReadOnlyList<ShadeCaster>?)null);
        var casters = new ShadeCaster[candidateFlora.Count];
        int count = 0;
        for (int i = 0; i < candidateFlora.Count; i++)
        {
            var f = candidateFlora[i];
            var sp = w.Content.FloraById(f.SpeciesId);
            if (sp == null || sp.Archetype != "plant" || sp.Woody != null) continue;
            double r = f.Radius(sp);
            if (r > 1e-6) casters[count++] = new ShadeCaster(f.X, f.Z, r, 1.0 / r);
        }
        return SampleLight(w, p, count == casters.Length ? casters : casters.AsSpan(0, count).ToArray());
    }

    private static double SampleLight(VivariumWorld w, Vec2 p, IReadOnlyList<ShadeCaster>? shadeCasters = null)
    {
        double light = w.FloraSystem.EffectiveLight(p);
        double shade = 0;
        if (shadeCasters != null)
        {
            for (int i = 0; i < shadeCasters.Count; i++)
            {
                var c = shadeCasters[i];
                double dx = Math.Abs(c.X - p.X);
                if (dx >= c.Radius) continue;
                double dz = Math.Abs(c.Z - p.Z);
                if (dz >= c.Radius) continue;
                double d2 = dx * dx + dz * dz;
                if (d2 >= c.Radius * c.Radius) continue;
                shade += 0.22 * (1 - Math.Sqrt(d2) * c.InvRadius);
            }
        }
        else
        {
            var buf = _lightBuf ??= new List<Flora.FloraIndividual>(16);
            w.Flora.Neighbours(p, 1.5, buf);
            foreach (var f in buf)
            {
                var sp = w.Content.FloraById(f.SpeciesId);
                if (sp == null || sp.Archetype != "plant" || sp.Woody != null) continue;
                double r = f.Radius(sp);
                if (r <= 1e-6) continue;
                double dx = Math.Abs(f.X - p.X);
                if (dx >= r) continue;
                double dz = Math.Abs(f.Z - p.Z);
                if (dz >= r) continue;
                double d2 = dx * dx + dz * dz;
                if (d2 >= r * r) continue;
                shade += 0.22 * (1 - Math.Sqrt(d2) / r);
            }
        }
        return MathD.Clamp01(light - Math.Min(shade, light));
    }

    // ------------------------------------------------------------------ substrate

    public static CoverageSubstrate SampleSubstrate(VivariumWorld w, Vec2 p) =>
        ClassifySubstrate(w, p, w.Clock.SimSeconds);

    private static CoverageSubstrate ClassifySubstrate(VivariumWorld w, Vec2 p, double now)
    {
        var s = w.SubstrateAt(p);
        return s switch
        {
            Content.Substrate.Rock => CoverageSubstrate.Rock,
            Content.Substrate.Wood => CoverageSubstrate.Log,
            Content.Substrate.Water => CoverageSubstrate.Water,
            Content.Substrate.Gravel => CoverageSubstrate.Gravel,
            _ => StabilityOf(w).IsStable(p, now) ? CoverageSubstrate.StableSoil : CoverageSubstrate.LooseSoil,
        };
    }
}
