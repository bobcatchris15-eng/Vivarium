using Vivarium.Sim.Content;
using Vivarium.Sim.Coverage;
using Vivarium.Sim.Coverage.Aquatic;
using Vivarium.Sim.Core;
using Vivarium.Sim.Ecology;
using Vivarium.Sim.Fauna;
using Vivarium.Sim.Fields;
using Vivarium.Sim.Flora;
using Vivarium.Sim.Genetics;
using Vivarium.Sim.Time;
using Vivarium.Sim.Water;

namespace Vivarium.Sim.World;

/// <summary>
/// The complete authoritative simulation state plus the systems that advance it. Nothing here references
/// rendering. Construct with <see cref="Create"/> (new world) or through Persistence (loaded world).
/// </summary>
public sealed class VivariumWorld
{
    public ContentLibrary Content { get; }
    public WorldDescriptor Descriptor { get; }
    public ulong Seed => Descriptor.Seed;
    public HexDomain Domain { get; }
    public GridSpec Grid { get; }
    public Heightfield Terrain { get; }
    public StrataModel Strata { get; }
    public EnvironmentFields Fields { get; }
    public Hydrology Water { get; }
    public PropSet Props { get; set; } = new();
    public FloraPopulation Flora { get; }
    public FaunaPopulation Fauna { get; }
    /// <summary>Fine coverage rasters (moss/lichen/slime), empty by default. See docs/overhaul/growth_models.md.</summary>
    public CoverageWorld Coverage { get; }
    public GenomeBank Genomes { get; } = new();
    public LineageBook Lineage { get; } = new();
    public EcologyTally Tally { get; set; } = new();
    public IdAllocator Ids { get; } = new();
    public SimClock Clock { get; } = new();
    public Scheduler Scheduler { get; }

    public PropPlacement Placement { get; }
    public FloraSystem FloraSystem { get; }
    public FaunaSystem FaunaSystem { get; }
    public EcologySystem Ecology { get; }
    /// <summary>Moss/lichen growth on the coverage layers (docs/overhaul/growth_models.md §4, §5, §9).</summary>
    public CoverageSystem CoverageSystem { get; }
    public AquaticSystem AquaticSystem { get; }

    /// <summary>Tick cadences (10 s ticks). Documented in docs/architecture/architecture.md.</summary>
    public static class Cadence
    {
        public const int FaunaBehaviour = 2, FaunaMetabolism = 3, FaunaLifecycle = 30, Hydrology = 6,
                         Environment = 30, Resources = 30, Flora = 60, GeneticsPrune = 8640;
    }

    private VivariumWorld(ContentLibrary content, WorldDescriptor descriptor)
    {
        var problems = descriptor.Validate();
        if (problems.Count > 0) throw new ArgumentException("Invalid world descriptor: " + string.Join("; ", problems));
        Content = content;
        Descriptor = descriptor.Clone();
        Domain = new HexDomain(Descriptor.Diameter);
        Grid = new GridSpec(Domain, Descriptor.CellSize);
        Terrain = Heightfield.Generate(Descriptor, Domain);
        Strata = new StrataModel(content.Strata);
        Fields = new EnvironmentFields(Grid, content.Ecology);
        Fields.GenerateBaseSubstrate(Descriptor, Terrain);
        Water = new Hydrology(Grid, Descriptor.Water, Terrain);
        Flora = new FloraPopulation(Domain.Radius + 1);
        Fauna = new FaunaPopulation(Domain.Radius + 1);
        Coverage = new CoverageWorld(Descriptor.Seed);
        Scheduler = new Scheduler(Clock);
        Clock.BioAcceleration = Descriptor.BioAcceleration;
        Placement = new PropPlacement(this);
        FloraSystem = new FloraSystem(this);
        FaunaSystem = new FaunaSystem(this);
        Ecology = new EcologySystem(this);
        CoverageSystem = new CoverageSystem(this);
        AquaticSystem = new AquaticSystem(this);
        RegisterSystems();
    }

    /// <summary>
    /// Builds only the procedural baseline (terrain, strata, grid, base substrate) with no props, water or life.
    /// Persistence restores mutable state onto this.
    /// </summary>
    public static VivariumWorld CreateBaseline(ContentLibrary content, WorldDescriptor d) => new(content, d);

    /// <summary>Creates a complete new world: baseline, springs, props, initial fields, water, starter flora and fauna.</summary>
    public static VivariumWorld Create(ContentLibrary content, WorldDescriptor d, bool populate = true)
    {
        var w = new VivariumWorld(content, d);
        foreach (var s in w.Descriptor.Water.Springs)
        {
            var sp = new Spring { Id = w.Ids.Next(EntityKind.Spring), X = s.X, Z = s.Z, Discharge = s.Discharge / SimUnits.Hour };
            if (!w.Domain.ContainsDisc(sp.Position, 0.05)) throw new ArgumentException($"Spring at {sp.Position} lies outside the island.");
            w.Water.Springs.Add(sp);
        }
        w.Placement.Generate(w.Descriptor);
        w.Fields.RecomputeLight(w.Terrain, w.Props);
        w.Water.InitializeFromWaterTable();
        // settle hydrology (up to 3 sim-days, stopping at equilibrium) + moisture, so the starting world
        // already shows its streams, pond and wet banks before anything is planted
        double lastVol = w.Water.Volume();
        for (int i = 1; i <= 1440; i++)
        {
            w.Water.Step(180);
            if (i % 120 == 0)
            {
                double vol = w.Water.Volume();
                if (Math.Abs(vol - lastVol) < 0.002 * Math.Max(vol, 1)) break;
                lastVol = vol;
            }
        }
        for (int i = 0; i < 48; i++) w.Water.CoupleMoisture(w.Fields.Moisture, content.Ecology, 1800, w.Fields.Scratch);
        foreach (int idx in w.Grid.DomainCells) w.Fields.Detritus[idx] = content.Ecology.DetritusMax * 0.05;
        w.CoverageSystem.SeedInitial();
        w.AquaticSystem.SeedInitial();
        if (populate) Populate.Starters(w);
        Log.Info(LogCategory.World, $"Created world '{w.Descriptor.Name}' seed {w.Seed}: {w.Props.Count} props, {w.Flora.Count} flora, {w.Fauna.Count} fauna.");
        return w;
    }

    private void RegisterSystems()
    {
        var eco = Content.Ecology;
        // physical systems get simulated seconds; biological ones get the (faster) biological seconds
        Action<double> Bio(Action<double> step) => dt => step(dt * Clock.BioAcceleration);
        Scheduler.Register("fauna.behaviour", Cadence.FaunaBehaviour, 10, FaunaSystem.StepBehaviour);
        Scheduler.Register("fauna.metabolism", Cadence.FaunaMetabolism, 20, Bio(FaunaSystem.StepMetabolism), phase: 1);
        Scheduler.Register("fauna.lifecycle", Cadence.FaunaLifecycle, 30, Bio(FaunaSystem.StepLifecycle), phase: 7);
        Scheduler.Register("hydrology", Cadence.Hydrology, 40, Water.Step, phase: 2);
        Scheduler.Register("environment", Cadence.Environment, 50, dt =>
        {
            Water.CoupleMoisture(Fields.Moisture, eco, dt, Fields.Scratch);
            Fields.StepNutrients(eco, dt * Clock.BioAcceleration);
            if (Fields.LightStale(Props)) Fields.RecomputeLight(Terrain, Props);
        }, phase: 13);
        Scheduler.Register("ecology.resources", Cadence.Resources, 60, Bio(Ecology.StepResources), phase: 19);
        Scheduler.Register("aquatic", Cadence.Flora, 65, Bio(AquaticSystem.Step), phase: 20);
        Scheduler.Register("flora", Cadence.Flora, 70, Bio(FloraSystem.Step), phase: 29);
        Scheduler.Register("coverage", Cadence.Flora, 75, Bio(CoverageSystem.Step), phase: 30);
        Scheduler.Register("genetics.prune", Cadence.GeneticsPrune, 90, _ => FaunaSystem.PruneGenetics(), phase: 4321);
    }

    // ------------------------------------------------------------------ queries

    /// <summary>Terrain surface elevation.</summary>
    public double SurfaceHeight(Vec2 p) => Terrain.Height(p);

    /// <summary>Walkable ground: terrain or the top of a prop, whichever is higher.</summary>
    public double GroundHeight(Vec2 p)
    {
        double h = Terrain.Height(p);
        double t = Props.PropTopAt(p);
        return double.IsNaN(t) ? h : Math.Max(h, t);
    }

    /// <summary>
    /// Habitat substrate at p (independent from visual material): rock/log footprints, then water cover,
    /// then gravel patches, then the generated base classification.
    /// </summary>
    public Substrate SubstrateAt(Vec2 p)
    {
        if (Props.RockAt(p) != null) return Substrate.Rock;
        if (Props.LogAt(p) != null) return Substrate.Wood;
        if (Water.IsWet(p)) return Substrate.Water;
        if (Props.GravelAt(p) != null) return Substrate.Gravel;
        return (Substrate)Fields.BaseSubstrate.Get(p);
    }

    private byte[]? _propSubstrateCells;
    private int _propSubstrateVersion = int.MinValue;
    private const byte NoProp = 255;

    /// <summary>
    /// Cell-resolution substrate (prop cover sampled at cell centres, cached per prop version). Used by
    /// high-frequency fauna queries; flora establishment and tools use the exact <see cref="SubstrateAt"/>.
    /// </summary>
    public Substrate SubstrateAtCell(Vec2 p)
    {
        int c = Grid.NearestDomainCell(p);
        if (c < 0) return SubstrateAt(p);
        if (_propSubstrateCells == null || _propSubstrateVersion != Props.Version)
        {
            _propSubstrateCells ??= new byte[Grid.Count];
            foreach (int idx in Grid.DomainCells)
            {
                var q = Grid.CellCenter(idx);
                _propSubstrateCells[idx] = Props.RockAt(q) != null ? (byte)Substrate.Rock : Props.LogAt(q) != null ? (byte)Substrate.Wood
                    : Props.GravelAt(q) != null ? (byte)Substrate.Gravel : NoProp;
            }
            _propSubstrateVersion = Props.Version;
        }
        byte ps = _propSubstrateCells[c];
        if (ps is (byte)Substrate.Rock or (byte)Substrate.Wood) return (Substrate)ps;
        if (Water.IsWet(c)) return Substrate.Water;
        if (ps == (byte)Substrate.Gravel) return Substrate.Gravel;
        return (Substrate)Fields.BaseSubstrate[c];
    }

    public void OnPropsChanged() { /* light recomputes lazily on the next environment tick (LightStale) */ }

    /// <summary>Forces derived caches up to date (used after tool actions so probes read fresh values).</summary>
    public void RefreshDerived() { if (Fields.LightStale(Props)) Fields.RecomputeLight(Terrain, Props); }

    public void Step(long ticks = 1) => Scheduler.StepTicks(ticks);
    public void RunFor(double simSeconds) => Scheduler.StepTicks((long)Math.Round(simSeconds / SimClock.FixedStepSeconds));

    /// <summary>Checks invariants; returns problems (empty when healthy).</summary>
    public List<string> CheckInvariants()
    {
        var e = new List<string>();
        if (!Fields.AllFinite()) e.Add("environment field contains non-finite values");
        if (!Water.AllFinite()) e.Add("water depth contains negative or non-finite values");
        var seen = new HashSet<EntityId>();
        foreach (var f in Flora.Items)
        {
            if (!seen.Add(f.Id)) e.Add($"duplicate id {f.Id}");
            if (!Domain.Contains(f.Position)) e.Add($"flora {f.Id} outside domain at {f.Position}");
            if (!double.IsFinite(f.Biomass) || f.Biomass < 0) e.Add($"flora {f.Id} invalid biomass {f.Biomass}");
            if (Content.FloraById(f.SpeciesId) == null) e.Add($"flora {f.Id} unknown species {f.SpeciesId}");
        }
        foreach (var f in Fauna.Items)
        {
            if (!seen.Add(f.Id)) e.Add($"duplicate id {f.Id}");
            if (!f.Position.IsFinite) e.Add($"fauna {f.Id} non-finite position");
            else if (!Domain.Contains(f.PositionXZ)) e.Add($"fauna {f.Id} escaped the island at {f.PositionXZ}");
            if (!double.IsFinite(f.Energy) || f.Energy < 0) e.Add($"fauna {f.Id} invalid energy {f.Energy}");
            if (!Genomes.Contains(f.GenomeId)) e.Add($"fauna {f.Id} references missing genome {f.GenomeId}");
            if (f.Id.Serial > Ids.LastSerial) e.Add($"fauna {f.Id} id beyond allocator");
        }
        foreach (var g in Genomes.Ordered())
            foreach (var t in g.Traits)
                if (!double.IsFinite(t) || t < 0 || t > 1) { e.Add($"genome {g.Id} trait out of range {t}"); break; }
        return e;
    }
}
