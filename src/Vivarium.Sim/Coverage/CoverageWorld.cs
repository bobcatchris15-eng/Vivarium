namespace Vivarium.Sim.Coverage;

/// <summary>
/// The three coverage layers (Mat, Crust, Plasmodium) held by a <see cref="World.VivariumWorld"/>. Empty by
/// default; the growth rules (G3+) populate them. No scheduler system is registered here (§13, G1 scope).
/// </summary>
public sealed class CoverageWorld
{
    public CoverageLayer Mat { get; }
    public CoverageLayer Crust { get; }
    public CoverageLayer Plasmodium { get; }

    public CoverageWorld(ulong worldSeed)
    {
        Mat = new CoverageLayer(CoverageLayerId.Mat, worldSeed);
        Crust = new CoverageLayer(CoverageLayerId.Crust, worldSeed);
        Plasmodium = new CoverageLayer(CoverageLayerId.Plasmodium, worldSeed);
    }

    public IEnumerable<CoverageLayer> All => new[] { Mat, Crust, Plasmodium };

    public CoverageLayer ById(CoverageLayerId id) => id switch
    {
        CoverageLayerId.Mat => Mat,
        CoverageLayerId.Crust => Crust,
        CoverageLayerId.Plasmodium => Plasmodium,
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };
}
