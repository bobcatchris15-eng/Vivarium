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
    public PilotTreeState? PilotTree { get; }
    public StrataModel Strata { get; }
    public EnvironmentFields Fields { get; }
    public LitterSystem Litter { get; }
    public Hydrology Water { get; }
    public PropSet Props { get; set; } = new();
    public FloraPopulation Flora { get; }
    public DeadPlantPopulation DeadFlora { get; }
    public SeedBank SeedBank { get; }
    public AmbientGroundCoverSystem AmbientGroundCover { get; }
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
    public DeadFloraSystem DeadFloraSystem { get; }
    public ReproductionSystem ReproductionSystem { get; }
    public FaunaSystem FaunaSystem { get; }
    public EcologySystem Ecology { get; }
    /// <summary>Moss/lichen growth on the coverage layers (docs/overhaul/growth_models.md §4, §5, §9).</summary>
    public CoverageSystem CoverageSystem { get; }
    public AquaticSystem AquaticSystem { get; }

    /// <summary>Tick cadences (10 s ticks). Documented in docs/architecture/architecture.md.</summary>
    public static class Cadence
    {
        // Behaviour is the only fauna system that re-decides steering. Locomotion integrates from the heading the
        // last decision chose, so this cadence sets how often animals move, not how well they steer; the decision
        // fan-out is throttled again inside FaunaSystem.StepBehaviour. Movement cost per step is unchanged.
        public const int FaunaBehaviour = 4, FaunaMetabolism = 6, FaunaLifecycle = 120, Hydrology = 1,
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
        PilotTree = PilotTreeState.Generate(Descriptor, Domain, Terrain);
        PilotTree?.ShapeTerrain(Domain);
        Strata = new StrataModel(content.Strata);
        Fields = new EnvironmentFields(Grid, content.Ecology);
        Litter = new LitterSystem(this);
        Fields.GenerateBaseSubstrate(Descriptor, Terrain);
        Water = new Hydrology(Grid, Descriptor.Water, Terrain);
        Flora = new FloraPopulation(Domain.Radius + 1);
        DeadFlora = new DeadPlantPopulation();
        SeedBank = new SeedBank(this);
        AmbientGroundCover = new AmbientGroundCoverSystem(this);
        Fauna = new FaunaPopulation(Domain.Radius + 1);
        Coverage = new CoverageWorld(Descriptor.Seed);
        Scheduler = new Scheduler(Clock);
        Clock.BioAcceleration = Descriptor.BioAcceleration;
        Placement = new PropPlacement(this);
        FloraSystem = new FloraSystem(this);
        DeadFloraSystem = new DeadFloraSystem(this);
        ReproductionSystem = new ReproductionSystem(this);
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
        w.Fields.RecomputeLight(w.Terrain, w.Props, w.PilotTree);
        w.Water.InitializeFromWaterTable();
        // A short physical warmup establishes spring runoff without inventing a water-table plane.
        for (int i = 0; i < 10; i++) w.Water.Step(30);
        for (int i = 0; i < 48; i++) w.Water.CoupleMoisture(w.Fields.Moisture, content.Ecology, 1800, w.Fields.Scratch);
        w.PilotTree?.CoupleHabitat(w, SimUnits.Day);
        foreach (int idx in w.Grid.DomainCells) w.Fields.Detritus[idx] = content.Ecology.DetritusMax * 0.05;
        w.PilotTree?.DepositLitter(w, SimUnits.Day * 5);
        w.AquaticSystem.SeedInitial();
        if (populate)
        {
            w.CoverageSystem.SeedInitial();
            Populate.Starters(w);
        }
        w.AmbientGroundCover.InitializeFromHabitat();
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
            PilotTree?.CoupleHabitat(this, dt);
            Fields.StepNutrients(eco, dt * Clock.BioAcceleration);
            if (Fields.LightStale(Props)) Fields.RecomputeLight(Terrain, Props, PilotTree);
        }, phase: 13);
        Scheduler.Register("ecology.resources", Cadence.Resources, 60, Bio(Ecology.StepResources), phase: 19);
        Scheduler.Register("aquatic", Cadence.Flora, 65, dt => AquaticSystem.Step(dt, dt * Clock.BioAcceleration), phase: 20);
        Scheduler.Register("ecology.litter", Cadence.Resources, 66, Bio(dt => { PilotTree?.DepositLitter(this, dt); Litter.Step(dt); }), phase: 20);
        Scheduler.Register("flora", Cadence.Flora, 70, Bio(FloraSystem.Step), phase: 29);
        Scheduler.Register("flora.dead", Cadence.Flora, 71, Bio(DeadFloraSystem.Step), phase: 29);
        Scheduler.Register("flora.reproduction", Cadence.Flora, 72, Bio(ReproductionSystem.Step), phase: 29);
        Scheduler.Register("flora.seedbank", Cadence.Flora, 73, Bio(SeedBank.Step), phase: 29);
        Scheduler.Register("flora.ambient", Cadence.Flora, 74, Bio(AmbientGroundCover.Step), phase: 29);
        Scheduler.Register("coverage", Cadence.Flora, 75, Bio(CoverageSystem.StepMat), phase: 30);
        Scheduler.Register("coverage.lichen", Cadence.Flora, 76, Bio(CoverageSystem.StepLichen), phase: 31);
        Scheduler.Register("coverage.plasmodium", Cadence.FaunaMetabolism, 77, Bio(CoverageSystem.StepPlasmodium), phase: 2);
        Scheduler.Register("genetics.prune", Cadence.GeneticsPrune, 90, _ => FaunaSystem.PruneGenetics(), phase: 4321);
    }

    // ------------------------------------------------------------------ queries

    /// <summary>Terrain surface elevation.</summary>
    public double SurfaceHeight(Vec2 p) => Terrain.Height(p);

    /// <summary>Walkable ground: terrain or the top of a prop, whichever is higher.</summary>
    public double GroundHeight(Vec2 p)
    {
        double h = Terrain.Height(p);
        double root = PilotTree?.RootSurfaceHeight(p) ?? double.NaN;
        if (!double.IsNaN(root)) h = Math.Max(h, root);
        double t = Props.PropTopAt(p);
        return double.IsNaN(t) ? h : Math.Max(h, t);
    }

    /// <summary>
    /// Habitat substrate at p (independent from visual material): rock/log footprints, then water cover,
    /// then gravel patches, then the generated base classification.
    /// </summary>
    public Substrate SubstrateAt(Vec2 p)
    {
        if (PilotTree?.BlocksDisc(p) == true) return Substrate.Wood;
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
        if (PilotTree?.BlocksDisc(p) == true) return Substrate.Wood;
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
    public void RefreshDerived() { if (Fields.LightStale(Props)) Fields.RecomputeLight(Terrain, Props, PilotTree); }

    public void Step(long ticks = 1) => Scheduler.StepTicks(ticks);
    public void RunFor(double simSeconds) => Scheduler.StepTicks((long)Math.Round(simSeconds / SimClock.FixedStepSeconds));

    /// <summary>Checks invariants; returns problems (empty when healthy).</summary>
    public List<string> CheckInvariants()
    {
        var e = new List<string>();
        if (!Fields.AllFinite()) e.Add("environment field contains non-finite values");
        if (!Litter.AllFinite()) e.Add("litter contains negative or non-finite mass");
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
