using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Fields;

/// <summary>Mutable environment quantities shared by ecology systems. All values bounded; NaN is never stored.</summary>
public sealed class EnvironmentFields
{
    public GridSpec Grid { get; }
    /// <summary>Generated base substrate (soil/rock); props and water override it in queries.</summary>
    public CategoricalField BaseSubstrate { get; }
    /// <summary>Normalized habitat light/exposure (0..1). Derived from terrain + props; recomputed when props change.</summary>
    public ScalarField Light { get; }
    public ScalarField Nutrients { get; }
    public ScalarField Moisture { get; }
    public ScalarField Detritus { get; }
    public ScalarField Biofilm { get; }
    public ScalarField Plankton { get; }

    /// <summary>Reusable scratch buffer for diffusion passes (no state).</summary>
    public double[] Scratch { get; }
    private int _lightPropsVersion = int.MinValue;

    public EnvironmentFields(GridSpec grid, EcologyConfig eco)
    {
        Grid = grid;
        BaseSubstrate = new CategoricalField("substrate", grid, (byte)Substrate.Soil, (byte)Substrate.Soil);
        Light = new ScalarField("light", grid, 1, 0, 1);
        Nutrients = new ScalarField("nutrients", grid, eco.NutrientBaseline, 0, eco.NutrientMax);
        Moisture = new ScalarField("moisture", grid, eco.MoistureDryBaseline, 0, 1);
        Detritus = new ScalarField("detritus", grid, 0, 0, eco.DetritusMax);
        Biofilm = new ScalarField("biofilm", grid, 0, 0, Math.Max(eco.BiofilmCapacity, 1e-9));
        Plankton = new ScalarField("plankton", grid, 0, 0, Math.Max(eco.PlanktonCapacity, 1e-9));
        Scratch = new double[grid.Count];
    }

    public IEnumerable<ScalarField> MutableScalars() { yield return Nutrients; yield return Moisture; yield return Detritus; yield return Biofilm; yield return Plankton; }

    public ScalarField? Resource(string id) => id switch
    {
        "detritus" => Detritus, "biofilm" => Biofilm, "plankton" => Plankton, "nutrients" => Nutrients, _ => null,
    };

    /// <summary>Generates base substrate classification from terrain slope and seeded exposure noise.</summary>
    public void GenerateBaseSubstrate(WorldDescriptor d, Heightfield hf)
    {
        ulong seed = Rng.Mix(d.Seed, Hash.Fnv1a64("world.substrate"));
        double exposure = d.Terrain.RockExposure;
        foreach (int idx in Grid.DomainCells)
        {
            var p = Grid.CellCenter(idx);
            double slope = hf.Slope(p);
            double n = 0.5 + 0.5 * Noise.Fbm(seed, p.X * 0.6, p.Z * 0.6, 3);
            bool rock = slope > 0.75 || n > 1 - exposure;
            BaseSubstrate[idx] = (byte)(rock ? Substrate.Rock : Substrate.Soil);
        }
    }

    public bool LightStale(PropSet props) => _lightPropsVersion != props.Version;
    /// <summary>Forces a light recompute on the next environment tick (terrain was sculpted).</summary>
    public void MarkLightStale() => _lightPropsVersion = int.MinValue;

    /// <summary>
    /// Computes exposure = sky openness from terrain horizon (8 azimuths, 3 m reach) × prop shading.
    /// Cells on top of props are fully exposed at the prop surface.
    /// </summary>
    public void RecomputeLight(Heightfield hf, PropSet props)
    {
        _lightPropsVersion = props.Version;
        const int dirs = 8;
        foreach (int idx in Grid.DomainCells)
        {
            var p = Grid.CellCenter(idx);
            double top = props.PropTopAt(p);
            double h0 = double.IsNaN(top) ? hf.Height(p) : Math.Max(top, hf.Height(p));
            double occl = 0;
            for (int k = 0; k < dirs; k++)
            {
                var dir = Vec2.FromAngle(k * 2 * Math.PI / dirs);
                double maxTan = 0;
                for (double s = 0.25; s <= 3.0; s += 0.25)
                {
                    var q = p + dir * s;
                    if (!Grid.Domain.Contains(q)) break;
                    double hq = hf.Height(q);
                    double pt = props.PropTopAt(q);
                    if (!double.IsNaN(pt)) hq = Math.Max(hq, pt);
                    maxTan = Math.Max(maxTan, (hq - h0) / s);
                }
                occl += Math.Sin(Math.Atan(maxTan));
            }
            double openness = 1 - occl / dirs;          // 1 = fully open sky
            Light[idx] = MathD.Clamp01(0.08 + 0.92 * openness);
        }
    }

    /// <summary>Nutrient relaxation toward baseline plus diffusion. Deterministic and bounded.</summary>
    public void StepNutrients(EcologyConfig eco, double dt)
    {
        double relax = 1 - Math.Exp(-eco.NutrientRelax * dt);
        foreach (int idx in Grid.DomainCells)
        {
            double v = Nutrients.Values[idx];
            Nutrients[idx] = v + (eco.NutrientBaseline - v) * relax;
        }
        Nutrients.Diffuse(eco.NutrientDiffusion * dt, Scratch);
    }

    public bool AllFinite() => Light.AllFinite() && Nutrients.AllFinite() && Moisture.AllFinite() && Detritus.AllFinite() && Biofilm.AllFinite() && Plankton.AllFinite();
}
