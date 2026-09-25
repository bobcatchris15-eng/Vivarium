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
    private const double CanopyShadePerPlant = 0.22;    // light fraction removed at a canopy's centre

    /// <summary>Samples the full micro-environment at world position p. Deterministic given world state.</summary>
    public static MicroEnv Sample(VivariumWorld w, Vec2 p)
    {
        double now = w.Clock.SimSeconds;

        double moisture = SampleMoisture(w, p);
        double humidity = SampleHumidity(w, p, moisture);
        double light = SampleLight(w, p);
        double nutrients = w.Fields.Nutrients.Sample(p);
        double detritus = w.Fields.Detritus.Sample(p);
        var substrate = ClassifySubstrate(w, p, now);
        double slope = w.Terrain.Slope(p);
        var n = w.Terrain.Normal(p);
        var downslope = new Vec2(n.X, n.Z).Normalized();
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

    private static double SampleMoisture(VivariumWorld w, Vec2 p)
    {
        if (HasNearbySeepage(w, p)) return 1;

        double m = w.Fields.Moisture.Sample(p) + MoistureBonusOf(w).At(p);
        m += ConcavityGain * MathD.Clamp(TerrainLaplacian(w, p), -1, 1);
        return MathD.Clamp01(m);
    }

    /// <summary>Water within 3 cm of p (own cell or a small ring around it) saturates the ground (§2).</summary>
    private static bool HasNearbySeepage(VivariumWorld w, Vec2 p)
    {
        if (w.Water.DepthAt(p) > 0) return true;
        for (int k = 0; k < 4; k++)
        {
            var dir = Vec2.FromAngle(k * Math.PI / 2);
            if (w.Water.DepthAt(p + dir * SeepageRadius) > 0) return true;
        }
        return false;
    }

    /// <summary>Discrete terrain Laplacian: positive in a bowl (concave up, wetter), negative on a ridge.</summary>
    private static double TerrainLaplacian(VivariumWorld w, Vec2 p)
    {
        double h = ConcavitySampleStep;
        double c = w.Terrain.Height(p);
        double sum = w.Terrain.Height(p + new Vec2(h, 0)) + w.Terrain.Height(p - new Vec2(h, 0))
                   + w.Terrain.Height(p + new Vec2(0, h)) + w.Terrain.Height(p - new Vec2(0, h)) - 4 * c;
        return sum / (h * h);
    }

    private static Vec2 SampleMoistureGradient(VivariumWorld w, Vec2 p)
    {
        double h = GradientStep;
        double mx1 = w.Fields.Moisture.Sample(p + new Vec2(h, 0)), mx0 = w.Fields.Moisture.Sample(p - new Vec2(h, 0));
        double mz1 = w.Fields.Moisture.Sample(p + new Vec2(0, h)), mz0 = w.Fields.Moisture.Sample(p - new Vec2(0, h));
        return new Vec2((mx1 - mx0) / (2 * h), (mz1 - mz0) / (2 * h));
    }

    private static double SampleHumidity(VivariumWorld w, Vec2 p, double moisture)
    {
        var dist = w.Water.DistanceToWater();
        int c = w.Grid.NearestDomainCell(p);
        double d = c >= 0 ? dist[c] : double.PositiveInfinity;
        double proximity = double.IsInfinity(d) ? 0 : Math.Exp(-d / 0.6);
        return MathD.Clamp01(0.7 * moisture + 0.3 * proximity);
    }

    // ------------------------------------------------------------------ light

    [ThreadStatic] private static List<Flora.FloraIndividual>? _lightBuf;

    /// <summary>Base light already bakes in terrain horizon and prop shading (<see cref="EnvironmentFields.RecomputeLight"/>);
    /// this subtracts vascular-plant canopy shade sampled from the flora spatial index (§2).</summary>
    private static double SampleLight(VivariumWorld w, Vec2 p)
    {
        double light = w.Fields.Light.Sample(p);
        double shade = 0;
        var buf = _lightBuf ??= new List<Flora.FloraIndividual>(16);
        buf.Clear();
        w.Flora.Neighbours(p, 1.5, buf);
        foreach (var f in buf)
        {
            var sp = w.Content.FloraById(f.SpeciesId);
            if (sp == null) continue;
            double r = f.Radius(sp);
            if (r <= 1e-6) continue;
            double d = Vec2.Distance(f.Position, p);
            if (d >= r) continue;
            shade += CanopyShadePerPlant * (1 - d / r);
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
