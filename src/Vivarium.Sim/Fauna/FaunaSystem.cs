using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Genetics;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Fauna;

public sealed class FaunaSuitability
{
    public bool HardRefused { get; init; }
    public string RefusalReason { get; init; } = "";
    public double Score { get; init; }
    public double WaterDepth { get; init; }
    public double Moisture { get; init; }
    public Substrate Substrate { get; init; }
    public override string ToString() => HardRefused ? $"refused: {RefusalReason}" : $"score {Score:0.00} (depth {WaterDepth * 100:0.#} cm, moisture {Moisture:0.00}, {SubstrateIds.Id(Substrate)})";
}

/// <summary>
/// Fauna behaviour, metabolism and lifecycle. Every quantity is integrated over simulated time delivered by
/// the scheduler; nothing reads render frame time. All stochastic choices use keyed randomness.
/// </summary>
public sealed class FaunaSystem
{
    private readonly VivariumWorld _w;
    private readonly List<FaunaIndividual> _nb = new();
    private readonly List<Flora.FloraIndividual> _fnb = new();
    private readonly ulong _wanderHash = Hash.Fnv1a64("fauna.wander");
    /// <summary>Ticks between wander control values (60 ticks = 10 simulated minutes).</summary>
    private const int WanderPeriod = 60;
    private readonly ulong _mortalityHash = Hash.Fnv1a64("fauna.mortality");
    public const string ReproStream = "fauna.reproduction";

    public FaunaSystem(VivariumWorld w) { _w = w; }
    private ContentLibrary C => _w.Content;

    // Genomes are immutable, so phenotypes are cached by genome id (derived data, never saved).
    private readonly Dictionary<EntityId, Phenotype> _phenotypes = new();

    public Phenotype PhenotypeOf(FaunaIndividual f)
    {
        if (_phenotypes.TryGetValue(f.GenomeId, out var cached)) return cached;
        var sp = C.FaunaOrThrow(f.SpeciesId);
        var g = _w.Genomes.Get(f.GenomeId);
        var ph = g == null ? Phenotype.From(sp, new Genome { SpeciesId = sp.Id, Traits = sp.Traits.Select(t => C.Genetics.Get(t)!.Default).ToArray() }) : Phenotype.From(sp, g);
        if (g != null) _phenotypes[f.GenomeId] = ph;
        return ph;
    }

    // ------------------------------------------------------------------ habitat

    public FaunaSuitability Suitability(FaunaSpeciesDef sp, Vec2 p)
    {
        if (!_w.Domain.ContainsDisc(p, 0.03)) return new FaunaSuitability { HardRefused = true, RefusalReason = "outside the island" };
        double depth = _w.Water.DepthAt(p);
        double moisture = _w.Fields.Moisture.Sample(p);
        var sub = _w.SubstrateAtCell(p);
        if (sp.Medium == Medium.Aquatic)
        {
            if (depth < sp.MinWaterDepth) return new FaunaSuitability { HardRefused = true, RefusalReason = $"{sp.Name} needs water at least {sp.MinWaterDepth * 100:0.#} cm deep (found {depth * 100:0.#} cm)", WaterDepth = depth, Moisture = moisture, Substrate = sub };
            double s = MathD.SmoothStep(sp.MinWaterDepth, sp.MinWaterDepth * 3, depth) * 0.7 + 0.3;
            if (depth > sp.MaxWaterDepth) s *= 0.6;
            return new FaunaSuitability { Score = MathD.Clamp01(s), WaterDepth = depth, Moisture = moisture, Substrate = sub };
        }
        bool onProp = !double.IsNaN(_w.Props.PropTopAt(p));
        if (!onProp && depth > sp.MaxWaterDepth) return new FaunaSuitability { HardRefused = true, RefusalReason = $"{sp.Name} cannot live in water ({depth * 100:0.#} cm deep)", WaterDepth = depth, Moisture = moisture, Substrate = sub };
        double aff = sp.SubstrateAffinity.GetValueOrDefault(sub == Substrate.Water ? Substrate.Soil : sub);
        double score = aff * sp.Moisture.Eval(moisture);
        return new FaunaSuitability { Score = MathD.Clamp01(score), WaterDepth = depth, Moisture = moisture, Substrate = sub };
    }

    /// <summary>Position an animal of this species would occupy at xz (Y from terrain, prop top, or water column).</summary>
    public double RestingY(FaunaSpeciesDef sp, Vec2 p, double columnFraction = 0.5)
    {
        if (sp.Medium == Medium.Aquatic)
        {
            double bed = _w.Terrain.Height(p), depth = _w.Water.DepthAt(p);
            double margin = Math.Min(0.01, depth * 0.25);
            return bed + margin + MathD.Clamp01(columnFraction) * Math.Max(0, depth - 2 * margin);
        }
        return _w.GroundHeight(p);
    }

    // ------------------------------------------------------------------ behaviour (every tick)

    public void StepBehaviour(double dt)
    {
        var fauna = _w.Fauna;
        fauna.RebuildIndex();
        long tick = _w.Clock.Tick;
        double now = _w.Clock.SimSeconds;
        foreach (var f in fauna.Items)
        {
            if (f.Grabbed) continue;
            var sp = C.FaunaOrThrow(f.SpeciesId);
            var ph = PhenotypeOf(f);
            var p = f.PositionXZ;
            double turn = 0;
            // correlated wander: smooth value noise over time (a new control value every WanderPeriod ticks), so
            // animals trace gentle curves instead of zig-zagging on every behaviour step
            double wt = (double)tick / WanderPeriod + (f.Id.Serial % 97) / 97.0;
            long w0 = (long)Math.Floor(wt);
            double wf = wt - w0; wf = wf * wf * (3 - 2 * wf);
            double wander = MathD.Lerp(Rng.HashUnit(_w.Seed, _wanderHash, f.Id.Value, (ulong)w0), Rng.HashUnit(_w.Seed, _wanderHash, f.Id.Value, (ulong)(w0 + 1)), wf) * 2 - 1;
            turn += wander * sp.Wander * 0.45;

            bool disturbed = now < f.DisturbedUntil;
            bool stranded = !IsPassable(sp, p);
            if (disturbed && !stranded && sp.Conglobates)
            {
                // rolls into a ball and stays put until the disturbance passes (see IsCurled)
                f.Y = _w.GroundHeight(p);
                continue;
            }
            double desired = double.NaN;
            if (stranded)
            {
                // habitat changed underneath (flooding or drying): head for the nearest passable ground/water
                double bestD = double.PositiveInfinity;
                for (int k = 0; k < 8; k++)
                    for (double r = sp.SenseRadius * 0.5; r <= sp.SenseRadius * 4; r += sp.SenseRadius * 0.5)
                    {
                        var q = p + Vec2.FromAngle(k * Math.PI / 4) * r;
                        if (IsPassable(sp, q)) { if (r < bestD) { bestD = r; desired = k * Math.PI / 4; } break; }
                    }
            }
            else if (disturbed)
            {
                var away = p - new Vec2(f.DisturbX, f.DisturbZ);
                if (away.LengthSq > 1e-10) desired = away.Angle;
            }
            else if (sp.HabitatSeek > 0 && (f.LastSuitability < sp.MinSuitability * 2.5 || (tick + (long)(f.Id.Serial % 3)) % 3 == 0))
            {
                // Probe headings ahead and steer toward the best habitat/food. An animal in poor habitat
                // (per its species' minSuitability) probes wider and farther, and wanders less, so it leaves.
                bool poor = f.LastSuitability < sp.MinSuitability * 2.5;
                int span = poor ? 3 : 1;
                double reach = sp.SenseRadius * (poor ? 2.0 : 1.0);
                double best = double.NegativeInfinity, bestAng = f.Heading;
                for (int k = -span; k <= span; k++)
                {
                    double ang = f.Heading + k * (poor ? 0.9 : 0.7);
                    var q = p + Vec2.FromAngle(ang) * reach;
                    var s = Suitability(sp, q);
                    double v = s.HardRefused ? -1 : s.Score + 0.5 * FoodAt(sp, q);
                    if (k == 0) v += 0.02; // mild preference to keep going
                    if (v > best) { best = v; bestAng = ang; }
                }
                desired = bestAng;
                if (poor)
                {
                    turn *= 0.3;
                    var here = Suitability(sp, p);
                    f.LastSuitability = here.HardRefused ? 0 : here.Score;
                }
            }

            if (sp.Schooling != null && sp.Behaviors.Contains("schooling") && !disturbed)
            {
                var sch = SchoolingHeading(f, sp);
                if (sch.HasValue) desired = double.IsNaN(desired) ? sch.Value : BlendAngles(desired, sch.Value, 0.65);
            }

            if (!double.IsNaN(desired))
            {
                double diff = MathD.WrapAngle(desired - f.Heading);
                double weight = disturbed || stranded ? 1 : Math.Max(sp.HabitatSeek, sp.Schooling != null ? 0.8 : 0);
                turn += diff * weight;
            }
            double maxTurn = sp.TurnRate * dt * (disturbed || stranded ? 2 : 1);
            f.Heading = MathD.WrapAngle(f.Heading + MathD.Clamp(turn, -maxTurn, maxTurn));

            double speed = sp.Speed * ph.SpeedScale * (disturbed ? 3.0 : 1.0) * (f.Energy < 0.15 * sp.MaxEnergy ? 0.6 : 1.0);
            var cand = p + Vec2.FromAngle(f.Heading) * (speed * dt);
            if (stranded || IsPassable(sp, cand))
            {
                f.X = cand.X; f.Z = cand.Z;
            }
            else
            {
                // blocked: turn away deterministically and try a sidestep
                double flip = Rng.HashUnit(_w.Seed, _wanderHash, f.Id.Value, (ulong)tick ^ 0xABCDUL) < 0.5 ? 1 : -1;
                f.Heading = MathD.WrapAngle(f.Heading + flip * (Math.PI * 0.6));
                var side = p + Vec2.FromAngle(f.Heading) * (speed * dt * 0.5);
                if (IsPassable(sp, side)) { f.X = side.X; f.Z = side.Z; }
            }
            // stay inside the island regardless of anything else
            var clamped = _w.Domain.ClampInside(f.PositionXZ, 0.04);
            f.X = clamped.X; f.Z = clamped.Z;

            if (sp.Medium == Medium.Aquatic)
            {
                double pitchNoise = Rng.HashUnit(_w.Seed, _wanderHash, f.Id.Value, (ulong)tick ^ 0x5151UL) * 2 - 1;
                f.Pitch = MathD.Clamp(f.Pitch + pitchNoise * 0.08, 0.05, 0.95);
                if (sp.Schooling != null) f.Pitch = MathD.Lerp(f.Pitch, 0.55, 0.05);
                f.Y = RestingY(sp, f.PositionXZ, f.Pitch);
            }
            else f.Y = _w.GroundHeight(f.PositionXZ);
        }
    }

    /// <summary>True while a conglobating animal (pill bug) is rolled up after being disturbed.</summary>
    public bool IsCurled(FaunaIndividual f) =>
        !f.Grabbed && _w.Clock.SimSeconds < f.DisturbedUntil && C.FaunaOrThrow(f.SpeciesId).Conglobates;

    public bool IsPassable(FaunaSpeciesDef sp, Vec2 q)
    {
        if (!_w.Domain.ContainsDisc(q, 0.04)) return false;
        double depth = _w.Water.DepthAt(q);
        if (sp.Medium == Medium.Aquatic) return depth >= sp.MinWaterDepth;
        return depth <= sp.MaxWaterDepth || !double.IsNaN(_w.Props.PropTopAt(q));
    }

    private double? SchoolingHeading(FaunaIndividual f, FaunaSpeciesDef sp)
    {
        var s = sp.Schooling!;
        _w.Fauna.Neighbours(f.PositionXZ, s.Radius, _nb);
        Vec2 centre = Vec2.Zero, align = Vec2.Zero, sep = Vec2.Zero; int n = 0;
        foreach (var o in _nb)
        {
            if (o.Id == f.Id || o.SpeciesId != f.SpeciesId || o.Grabbed) continue;
            n++;
            centre += o.PositionXZ;
            align += Vec2.FromAngle(o.Heading);
            var d = f.PositionXZ - o.PositionXZ;
            double dl = d.Length;
            if (dl < s.SeparationDistance && dl > 1e-9) sep += d / dl * (1 - dl / s.SeparationDistance);
        }
        if (n == 0) return null;
        centre /= n;
        var v = (centre - f.PositionXZ).Normalized() * s.Cohesion + align.Normalized() * s.Alignment + sep * s.Separation + Vec2.FromAngle(f.Heading) * 0.5;
        return v.LengthSq > 1e-12 ? v.Angle : null;
    }

    /// <summary>Local cohesion metric: mean distance of individuals of a species to their nearest conspecific.</summary>
    public double MeanNearestNeighbourDistance(string species)
    {
        var members = _w.Fauna.Items.Where(f => f.SpeciesId == species).ToList();
        if (members.Count < 2) return double.NaN;
        double sum = 0;
        foreach (var a in members)
        {
            double best = double.PositiveInfinity;
            foreach (var b in members) if (a != b) best = Math.Min(best, Vec2.Distance(a.PositionXZ, b.PositionXZ));
            sum += best;
        }
        return sum / members.Count;
    }

    private static double BlendAngles(double a, double b, double t) => a + MathD.WrapAngle(b - a) * t;

    /// <summary>Normalized food availability (0..1) for sp at p, used for steering.</summary>
    public double FoodAt(FaunaSpeciesDef sp, Vec2 p)
    {
        double best = 0;
        foreach (var d in sp.Diet)
        {
            var field = _w.Fields.Resource(d.Resource);
            if (field != null) best = Math.Max(best, MathD.Clamp01(field.Sample(p) / Math.Max(field.Max * 0.25, 1e-9)));
        }
        return best;
    }

    // ------------------------------------------------------------------ metabolism + feeding

    public void StepMetabolism(double dt)
    {
        double now = _w.Clock.SimSeconds;
        var dead = new List<(FaunaIndividual, string)>();
        foreach (var f in _w.Fauna.Items)
        {
            var sp = C.FaunaOrThrow(f.SpeciesId);
            var ph = PhenotypeOf(f);
            var suit = Suitability(sp, f.PositionXZ);
            f.LastSuitability = suit.HardRefused ? 0 : suit.Score;
            double stress = suit.HardRefused && !f.Grabbed ? 6.0 : 1.0;
            if (now < f.DisturbedUntil) stress *= 1.3;
            f.Energy -= sp.BasalRate * ph.MetabolicScale * stress * dt;
            if (f.Energy < sp.HungerThreshold * sp.MaxEnergy && !f.Grabbed) Feed(f, sp, ph, dt);
            f.Energy = MathD.Clamp(f.Energy, 0, sp.MaxEnergy);
            if (f.Energy <= 0) dead.Add((f, suit.HardRefused ? "stranded" : "starvation"));
        }
        foreach (var (f, cause) in dead) Kill(f, cause);
    }

    private void Feed(FaunaIndividual f, FaunaSpeciesDef sp, Phenotype ph, double dt)
    {
        var p = f.PositionXZ;
        int cell = _w.Grid.NearestDomainCell(p);
        if (cell < 0) return;
        double room = sp.MaxEnergy - f.Energy;
        double consumed = 0;
        foreach (var d in sp.Diet)
        {
            if (room <= 1e-12) break;
            double want = Math.Min(d.RatePerSecond * ph.MassScale * dt, room / Math.Max(d.Efficiency, 1e-9));
            double got;
            if (d.Resource.StartsWith("flora:", StringComparison.Ordinal)) got = GrazeFlora(p, d.Resource[6..], want);
            else
            {
                var field = _w.Fields.Resource(d.Resource);
                if (field == null) continue;
                got = field.Take(cell, want);
            }
            if (got <= 0) continue;
            consumed += got;
            f.Energy += got * d.Efficiency;
            room = sp.MaxEnergy - f.Energy;
        }
        if (consumed > 0) _w.Ecology.AddWasteNutrients(p, consumed * sp.WasteFraction);
    }

    private double GrazeFlora(Vec2 p, string archetype, double want)
    {
        _w.Flora.Neighbours(p, 0.12, _fnb);
        // keep only grazeable organisms of this kind (usually none), then visit them in id order for determinism
        _fnb.RemoveAll(fl => C.FloraOrThrow(fl.SpeciesId) is var s && (s.Archetype != archetype || s.GrazingValue <= 0));
        if (_fnb.Count == 0) return 0;
        if (_fnb.Count > 1) _fnb.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));
        double got = 0;
        foreach (var fl in _fnb)
        {
            var fsp = C.FloraOrThrow(fl.SpeciesId);
            double avail = Math.Max(0, fl.Biomass - fsp.InitialBiomass * 0.5) * fsp.GrazingValue;
            double take = Math.Min(avail, want - got);
            if (take <= 0) continue;
            fl.Biomass -= take / fsp.GrazingValue;
            got += take;
            if (got >= want) break;
        }
        return got;
    }

    // ------------------------------------------------------------------ lifecycle

    public void StepLifecycle(double dt)
    {
        double now = _w.Clock.BioSeconds;
        long tick = _w.Clock.Tick;
        _w.Fauna.RebuildIndex();
        var dead = new List<(FaunaIndividual, string)>();
        var births = new List<(FaunaSpeciesDef Sp, FaunaIndividual A, FaunaIndividual? B)>();
        var reproducedThisStep = new HashSet<EntityId>();
        foreach (var f in _w.Fauna.Items)
        {
            var sp = C.FaunaOrThrow(f.SpeciesId);
            f.Age += dt;
            if (f.Stage == FaunaLifeStage.Juvenile && f.Age >= sp.MaturityAge) f.Stage = FaunaLifeStage.Adult;
            if (f.Age >= sp.Lifespan * f.LifespanFactor) { dead.Add((f, "old age")); continue; }
            if (sp.DailyMortality > 0)
            {
                double pDie = 1 - Math.Pow(1 - sp.DailyMortality, dt / SimUnits.Day);
                if (Rng.HashUnit(_w.Seed, _mortalityHash, f.Id.Value, (ulong)tick) < pDie) { dead.Add((f, "mortality")); continue; }
            }
            if (reproducedThisStep.Contains(f.Id)) continue;
            if (!CanReproduce(f, sp, now, out _)) continue;
            FaunaIndividual? mate = null;
            if (sp.Sexual)
            {
                _w.Fauna.Neighbours(f.PositionXZ, sp.MateRadius, _nb);
                var candidates = _nb.ToArray(); // CanReproduce reuses _nb
                mate = candidates.Where(o => o.Id != f.Id && o.SpeciesId == f.SpeciesId && !reproducedThisStep.Contains(o.Id) && CanReproduce(o, sp, now, out _))
                          .OrderBy(o => o.Id.Value).FirstOrDefault();
                if (mate == null) continue;
                reproducedThisStep.Add(mate.Id);
                mate.ReproCooldownUntil = now + sp.ReproCooldown * 0.5;
                mate.Energy -= sp.ReproCost * sp.MaxEnergy * 0.3;
            }
            reproducedThisStep.Add(f.Id);
            births.Add((sp, f, mate));
        }
        foreach (var (f, cause) in dead) Kill(f, cause);
        foreach (var (sp, a, b) in births)
        {
            if (_w.Fauna.Get(a.Id) == null) continue;
            var rng = Rng.Keyed(_w.Seed, ReproStream, a.Id.Value, a.RngCounter++);
            int clutch = sp.ClutchMin + rng.NextInt(sp.ClutchMax - sp.ClutchMin + 1);
            a.Energy -= sp.ReproCost * sp.MaxEnergy;
            a.ReproCooldownUntil = now + sp.ReproCooldown;
            for (int i = 0; i < clutch; i++)
            {
                if (_w.Fauna.CountOf(sp.Id) >= sp.PopulationCap) break;
                CreateOffspring(sp, a, b != null && _w.Fauna.Get(b.Id) != null ? b : null);
            }
            a.Energy = Math.Max(0.01, a.Energy);
        }
    }

    /// <summary>Reproduction eligibility from age, energy, habitat, cooldown, crowding and the safety cap.</summary>
    public bool CanReproduce(FaunaIndividual f, FaunaSpeciesDef sp, double now, out string reason)
    {
        if (f.Grabbed) { reason = "held"; return false; }
        if (f.Stage != FaunaLifeStage.Adult) { reason = "not mature"; return false; }
        if (f.Energy < sp.ReproMinEnergy * sp.MaxEnergy) { reason = "not enough energy"; return false; }
        if (now < f.ReproCooldownUntil) { reason = "recovering"; return false; }
        var s = Suitability(sp, f.PositionXZ);
        if (s.HardRefused || s.Score < sp.MinSuitability) { reason = "poor habitat"; return false; }
        if (_w.Fauna.CountOf(sp.Id) >= sp.PopulationCap) { reason = "population safety cap"; return false; }
        _w.Fauna.Neighbours(f.PositionXZ, sp.SenseRadius, _nb);
        int local = _nb.Count(o => o.SpeciesId == sp.Id);
        if (local > sp.MaxLocalDensity) { reason = "too crowded"; return false; }
        reason = "eligible";
        return true;
    }

    /// <summary>
    /// The single offspring pipeline: allocates ids, delegates genome creation to Genetics.Inheritance,
    /// records lineage, and places the young at a valid spot near the parent.
    /// </summary>
    public FaunaIndividual CreateOffspring(FaunaSpeciesDef sp, FaunaIndividual a, FaunaIndividual? b)
    {
        var id = _w.Ids.Next(EntityKind.Fauna);
        var gid = _w.Ids.Next(EntityKind.Genome);
        var ga = _w.Genomes.Get(a.GenomeId) ?? throw new InvalidOperationException($"Parent {a.Id} has no genome.");
        var gb = b != null ? _w.Genomes.Get(b.GenomeId) : null;
        var genome = Inheritance.CreateOffspring(sp, C.Genetics, ga, gb, gid, _w.Seed);
        _w.Genomes.Add(genome);
        var rng = Rng.Keyed(_w.Seed, "fauna.offspring", id.Value);
        var pos = a.PositionXZ;
        for (int tries = 0; tries < 6; tries++)
        {
            var q = pos + Vec2.FromAngle(rng.Range(0, 2 * Math.PI)) * rng.Range(0.01, 0.08);
            if (IsPassable(sp, q)) { pos = q; break; }
        }
        var child = new FaunaIndividual
        {
            Id = id, SpeciesId = sp.Id, X = pos.X, Z = pos.Z, Heading = rng.Range(-Math.PI, Math.PI), Pitch = rng.Range(0.2, 0.8),
            Age = 0, Energy = Math.Min(sp.OffspringEnergy, sp.MaxEnergy), Stage = FaunaLifeStage.Juvenile,
            LifespanFactor = 1 + (rng.NextDouble() * 2 - 1) * sp.LifespanVariance,
            GenomeId = gid, ParentA = a.Id, ParentB = b?.Id ?? EntityId.None,
        };
        child.Y = RestingY(sp, pos, child.Pitch);
        _w.Fauna.Add(child);
        a.Offspring++;
        if (b != null) b.Offspring++;
        _w.Lineage.Add(new LineageRecord
        {
            Id = id, SpeciesId = sp.Id, GenomeId = gid, ParentA = a.Id, ParentB = b?.Id ?? EntityId.None,
            BirthTick = _w.Clock.Tick, Generation = genome.Generation,
        });
        _w.Tally.Birth(sp.Id);
        return child;
    }

    /// <summary>Creates a founder individual (starter population, reintroduction, introduction tool).</summary>
    public FaunaIndividual CreateFounder(FaunaSpeciesDef sp, Vec2 pos, double? ageFraction = null)
    {
        var id = _w.Ids.Next(EntityKind.Fauna);
        var gid = _w.Ids.Next(EntityKind.Genome);
        var genome = Inheritance.CreateFounder(sp, C.Genetics, gid, _w.Seed);
        _w.Genomes.Add(genome);
        var rng = Rng.Keyed(_w.Seed, "fauna.founder", id.Value);
        double age = (ageFraction ?? rng.Range(0.1, 0.5)) * sp.Lifespan;
        var f = new FaunaIndividual
        {
            Id = id, SpeciesId = sp.Id, X = pos.X, Z = pos.Z, Heading = rng.Range(-Math.PI, Math.PI), Pitch = rng.Range(0.2, 0.8),
            Age = age, Energy = sp.InitialEnergy, Stage = age >= sp.MaturityAge ? FaunaLifeStage.Adult : FaunaLifeStage.Juvenile,
            LifespanFactor = 1 + (rng.NextDouble() * 2 - 1) * sp.LifespanVariance, GenomeId = gid,
            // stagger breeding so a founding group does not all reproduce on the same tick
            ReproCooldownUntil = _w.Clock.BioSeconds + rng.Range(0.3, 1.0) * sp.ReproCooldown,
        };
        f.Y = RestingY(sp, pos, f.Pitch);
        _w.Fauna.Add(f);
        _w.Lineage.Add(new LineageRecord { Id = id, SpeciesId = sp.Id, GenomeId = gid, BirthTick = _w.Clock.Tick, Generation = 0 });
        return f;
    }

    /// <summary>Death: removal, lineage bookkeeping, and detritus return exactly once.</summary>
    public bool Kill(FaunaIndividual f, string cause)
    {
        if (_w.Fauna.Get(f.Id) == null) return false;
        _w.Fauna.Remove(f.Id);
        var sp = C.FaunaOrThrow(f.SpeciesId);
        var ph = PhenotypeOf(f);
        double mass = sp.MassAtMid * ph.MassScale * sp.DetritusOnDeath;
        _w.Ecology.ReturnOrganicMatter(f.PositionXZ, mass, 0, fromFlora: false);
        _w.Lineage.MarkDead(f.Id, _w.Clock.Tick, cause, _w.Clock.BioDays);
        _w.Tally.Death(sp.Id, cause);
        return true;
    }

    /// <summary>Drops lineage records no longer needed and genomes no longer referenced.</summary>
    public void PruneGenetics()
    {
        _phenotypes.Clear();
        _w.Lineage.Prune();
        var referenced = new HashSet<EntityId>();
        foreach (var f in _w.Fauna.Items) referenced.Add(f.GenomeId);
        foreach (var r in _w.Lineage.Ordered()) referenced.Add(r.GenomeId);
        foreach (var g in _w.Genomes.Ordered().ToList()) if (!referenced.Contains(g.Id)) _w.Genomes.Remove(g.Id);
    }
}
