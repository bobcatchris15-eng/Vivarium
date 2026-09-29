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
    private readonly Dictionary<EntityId, (string Cause, double Detritus)> _predationDeaths = new();
    public const string ReproStream = "fauna.reproduction";
    /// <summary>Behaviour steps between spatial-index rebuilds. A rebuild refills every bucket, so running it each
    /// step made behaviour cost O(fauna) per pass even when nothing had changed bucket.</summary>
    private const int IndexRebuildPeriod = 4;
    /// <summary>Fraction of the appetite a feeding attempt must have met from the preferred diet before the guild
    /// fallback diet is consulted. Low enough that a merely reduced meal keeps an animal on its own diet, high
    /// enough that a genuinely absent resource is caught within the same attempt.</summary>
    private const double FallbackTriggerFraction = 0.25;
    /// <summary>Smallest prey mid-size, as a fraction of the hunter's, that a hunter will attempt to eat. Below this
    /// the quarry is too small for the energy to pay for finding it.</summary>
    private const double MinPreySizeRatio = 0.01;
    /// <summary>Largest prey mid-size, as a fraction of the hunter's. Above this the quarry is not viable; this bound is
    /// what stops predation running away up the size ladder until every predator dies of starvation.</summary>
    private const double MaxPreySizeRatio = 0.75;
    /// <summary>Behaviour steps since the last rebuild. Not serialized: a fresh counter after a load only means
    /// the first behaviour step refills an index that the loader already built incrementally anyway.</summary>
    private int _behaviourStepsSinceIndexRebuild;
    /// <summary>Lifecycle passes since the last rebuild. Separate from the behaviour counter so each system's
    /// cadence is self-contained: lifecycle runs far less often, and sharing one counter would make whether a
    /// lifecycle pass rebuilds depend on how many behaviour steps happened to run before it.</summary>
    /// <summary>Behaviour steps between steering-intent refreshes for an animal that is comfortable where it is. The
    /// habitat/food probe fan-out and the schooling neighbour query are the expensive part of behaviour, so between
    /// refreshes an animal just steers toward the heading it last chose. Anything that must react promptly refreshes
    /// on every step regardless: a disturbed animal, one stranded by flooding or drying, and one sitting in habitat
    /// below its species' tolerance.</summary>
    private const int IntentRefreshPeriod = 2;
    /// <summary>Longest simulated interval an animal may cross before its destination is re-tested for passability.
    /// Pinned rather than derived from <c>Cadence.FaunaBehaviour</c> on purpose: raising the cadence then re-decides
    /// steering less often without ever letting one step carry an animal across a whole habitat boundary between
    /// two samples. At or below this slice length the advance is the original single one.</summary>
    private const double MaxLocomotionSlice = 20.0;
    private int _lifecycleStepsSinceIndexRebuild;

    public FaunaSystem(VivariumWorld w) { _w = w; }
    private ContentLibrary C => _w.Content;

    /// <summary>
    /// True when the whole-vivarium fauna budget is already spent, regardless of species mix. The world budget
    /// is separate from the per-species safety cap: the cap throttles one species, this bounds the vivarium.
    /// </summary>
    public bool AtFaunaBudget => _w.Fauna.Count >= _w.Descriptor.FaunaBudget;

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
        if (_w.PilotTree?.BlocksTrunk(p, 0.03) == true) return new FaunaSuitability { HardRefused = true, RefusalReason = "inside Pilot Tree trunk" };
        double depth = _w.Water.OpenWaterDepth(p);
        double moisture = _w.Fields.Moisture.Sample(p);
        var sub = _w.SubstrateAtCell(p);
        if (sp.Flies)
        {
            var groundSub = sub == Substrate.Water ? Substrate.Soil : sub;
            double flightAffinity = sp.SubstrateAffinity.GetValueOrDefault(groundSub);
            double flightScore = flightAffinity * sp.Moisture.Eval(moisture);
            if (depth > sp.MaxWaterDepth) flightScore *= 0.75; // crossing water is allowed; lingering there is merely suboptimal
            return new FaunaSuitability { Score = MathD.Clamp01(flightScore), WaterDepth = depth, Moisture = moisture, Substrate = sub };
        }
        if (sp.Medium == Medium.Aquatic)
        {
            if (depth < sp.MinWaterDepth) return new FaunaSuitability { HardRefused = true, RefusalReason = $"{sp.Name} needs water at least {sp.MinWaterDepth * 100:0.#} cm deep (found {depth * 100:0.#} cm)", WaterDepth = depth, Moisture = moisture, Substrate = sub };
            double s = MathD.SmoothStep(sp.MinWaterDepth, sp.MinWaterDepth * 3, depth) * 0.7 + 0.3;
            if (depth > sp.MaxWaterDepth) s *= 0.6;
            return new FaunaSuitability { Score = MathD.Clamp01(s), WaterDepth = depth, Moisture = moisture, Substrate = sub };
        }
        bool onProp = !double.IsNaN(_w.Props.PropTopAt(p)) || _w.PilotTree?.BlocksDisc(p) == true;
        if (!onProp && depth > sp.MaxWaterDepth) return new FaunaSuitability { HardRefused = true, RefusalReason = $"{sp.Name} cannot live in water ({depth * 100:0.#} cm deep)", WaterDepth = depth, Moisture = moisture, Substrate = sub };
        double aff = sp.SubstrateAffinity.GetValueOrDefault(sub == Substrate.Water ? Substrate.Soil : sub);
        double score = aff * sp.Moisture.Eval(moisture);
        return new FaunaSuitability { Score = MathD.Clamp01(score), WaterDepth = depth, Moisture = moisture, Substrate = sub };
    }

    /// <summary>Position an animal of this species would occupy at xz (Y from terrain, prop top, or water column).</summary>
    public double RestingY(FaunaSpeciesDef sp, Vec2 p, double columnFraction = 0.5)
    {
        if (sp.Flies) return _w.GroundHeight(p) + Math.Max(0.14, sp.VisualScale * (sp.SizeMin + sp.SizeMax) * 0.8);
        if (sp.Medium == Medium.Aquatic)
        {
            double bed = _w.Terrain.Height(p), depth = _w.Water.OpenWaterDepth(p);
            double margin = Math.Min(0.01, depth * 0.25);
            return bed + margin + MathD.Clamp01(columnFraction) * Math.Max(0, depth - 2 * margin);
        }
        return _w.GroundHeight(p);
    }

    // ------------------------------------------------------------------ behaviour (every tick)

    public void StepBehaviour(double dt)
    {
        var fauna = _w.Fauna;
        // Refill the spatial index on a slow cadence rather than every step. Membership stays exact regardless:
        // Add inserts incrementally, and Remove/RemoveMany/Clear already rebuild. Only bucket *placement* can lag,
        // and a lagging bucket can only drop a neighbour that has just crossed a bucket boundary, never admit a
        // wrong one, because Query re-tests each candidate's live position. Bucket contents stay in ascending id
        // order (Add enforces ascending ids), so query order is unchanged.
        if (++_behaviourStepsSinceIndexRebuild >= IndexRebuildPeriod) { _behaviourStepsSinceIndexRebuild = 0; fauna.RebuildIndex(); }
        long tick = _w.Clock.Tick;
        double now = _w.Clock.SimSeconds;
        foreach (var f in fauna.Items)
        {
            // A held animal is teleported by the grab tool, so any heading it decided earlier describes a place it is
            // no longer in. Drop the cached intent and it re-decides on the first step after release.
            if (f.Grabbed) { f.IntentActive = false; continue; }
            f.IntentSteps++;
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
                f.IntentActive = false;   // the world moved under it while it was rolled up
                continue;
            }
            // Cheap locomotion (wander, turn integration, advance, clamping) runs every step; only the expensive
            // decision fan-out is throttled. The refresh point is a pure function of this animal: its own step count
            // offset by its own id, so a population does not re-decide in lockstep and no animal's timing can depend
            // on another's. No randomness is involved, so the same seed replays the same trajectory.
            bool poor = sp.HabitatSeek > 0 && f.LastSuitability < sp.MinSuitability * 2.5;
            long phase = (f.IntentSteps + (long)(f.Id.Serial % IntentRefreshPeriod)) % IntentRefreshPeriod;
            if (!f.IntentActive || disturbed || stranded || poor || phase == 0)
            {
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
                else if (sp.HabitatSeek > 0 && (poor || (tick + (long)(f.Id.Serial % 3)) % 3 == 0))
                {
                    // Probe headings ahead and steer toward the best habitat/food. An animal in poor habitat
                    // (per its species' minSuitability) probes wider and farther, and wanders less, so it leaves.
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
                f.IntentActive = !double.IsNaN(desired);
                if (f.IntentActive) f.IntentHeading = desired;
            }

            if (f.IntentActive)
            {
                double diff = MathD.WrapAngle(f.IntentHeading - f.Heading);
                double weight = disturbed || stranded ? 1 : Math.Max(sp.HabitatSeek, sp.Schooling != null ? 0.8 : 0);
                turn += diff * weight;
            }
            double maxTurn = sp.TurnRate * dt * (disturbed || stranded ? 2 : 1);
            f.Heading = MathD.WrapAngle(f.Heading + MathD.Clamp(turn, -maxTurn, maxTurn));

            double speed = sp.Speed * ph.SpeedScale * (disturbed ? 3.0 : 1.0) * (f.Energy < 0.15 * sp.MaxEnergy ? 0.6 : 1.0);
            // Advance in fixed slices so a coarse cadence cannot stretch the gap between two passability samples.
            int slices = (int)Math.Max(1, Math.Ceiling(dt / MaxLocomotionSlice));
            double slice = dt / slices;
            for (int sl = 0; sl < slices; sl++)
            {
                var from = f.PositionXZ;
                var cand = from + Vec2.FromAngle(f.Heading) * (speed * slice);
                if (stranded || IsPassable(sp, cand))
                {
                    f.X = cand.X; f.Z = cand.Z;
                }
                else
                {
                    // blocked: turn away deterministically and try a sidestep
                    double flip = Rng.HashUnit(_w.Seed, _wanderHash, f.Id.Value, (ulong)tick ^ 0xABCDUL) < 0.5 ? 1 : -1;
                    f.Heading = MathD.WrapAngle(f.Heading + flip * (Math.PI * 0.6));
                    var side = from + Vec2.FromAngle(f.Heading) * (speed * slice * 0.5);
                    if (IsPassable(sp, side)) { f.X = side.X; f.Z = side.Z; }
                }
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
            else f.Y = RestingY(sp, f.PositionXZ);
        }
    }

    /// <summary>True while a conglobating animal (pill bug) is rolled up after being disturbed.</summary>
    public bool IsCurled(FaunaIndividual f) =>
        !f.Grabbed && _w.Clock.SimSeconds < f.DisturbedUntil && C.FaunaOrThrow(f.SpeciesId).Conglobates;

    public bool IsPassable(FaunaSpeciesDef sp, Vec2 q)
    {
        if (!_w.Domain.ContainsDisc(q, 0.04)) return false;
        if (_w.PilotTree?.BlocksTrunk(q, 0.04) == true) return false;
        if (sp.Flies) return true;
        double depth = _w.Water.OpenWaterDepth(q);
        if (sp.Medium == Medium.Aquatic) return depth >= sp.MinWaterDepth;
        return depth <= sp.MaxWaterDepth || !double.IsNaN(_w.Props.PropTopAt(q)) || _w.PilotTree?.BlocksDisc(q) == true;
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

        // Carrion-scent fungi are navigation attractors rather than food. Keep the hook tag-based so future
        // stinkhorn/carrion-flower analogues can reuse it without teaching fauna about a specific species id.
        // Only small fliers respond strongly; large flying fauna keep their ordinary habitat/diet steering.
        if (sp.Flies && sp.SizeMax <= 0.03 && sp.SenseRadius > 0)
        {
            _w.Flora.Neighbours(p, sp.SenseRadius, _fnb);
            foreach (var fl in _fnb)
            {
                var fsp = C.FloraOrThrow(fl.SpeciesId);
                if (!fsp.Tags.Contains("carrion_attractor")) continue;
                double d = Vec2.Distance(p, fl.Position);
                double scent = 0.90 * MathD.Clamp01(1.0 - d / sp.SenseRadius);
                best = Math.Max(best, scent);
            }
        }

        foreach (var d in sp.Diet)
        {
            if (d.Resource.StartsWith("fauna:", StringComparison.Ordinal))
            {
                string prey = d.Resource[6..];
                _w.Fauna.Neighbours(p, sp.SenseRadius, _nb);
                int count = _nb.Count(x => x.SpeciesId == prey && x.Energy > 0);
                best = Math.Max(best, MathD.Clamp01(count / 3.0));
                // The named quarry keeps full priority, but its scarcity must not strand a predator that could still
                // eat something else: any eligible community member is a weaker secondary attractor.
                int community = _nb.Count(x => IsEdiblePrey(sp, x));
                if (community > 0) best = Math.Max(best, MathD.Clamp01(community / 6.0) * 0.7);
                continue;
            }
            if (d.Resource == "detritus")
            {
                double detritus = _w.Litter.DetritusAt(p);
                best = Math.Max(best, MathD.Clamp01(detritus / Math.Max(C.Ecology.DetritusMax * 0.25, 1e-9)));
                continue;
            }
            var field = _w.Fields.Resource(d.Resource);
            if (field != null) best = Math.Max(best, MathD.Clamp01(field.Sample(p) / Math.Max(field.Max * 0.25, 1e-9)));
        }
        return best;
    }

    // ------------------------------------------------------------------ metabolism + feeding

    /// <summary>
    /// Energy accounting, run once per <c>Cadence.FaunaMetabolism</c> ticks (60 simulated seconds) instead of on a
    /// small step. Every term below is a rate multiplied by the interval the scheduler actually handed us, so what an
    /// animal holds is a function of elapsed simulated time and never of how that time happened to be chopped into
    /// passes: one pass of dt and two of dt/2 move the same energy whenever the exposure is the same, because dt scales
    /// every term. Nothing here reads render frame time or a wall-clock delta.
    /// </summary>
    public void StepMetabolism(double dt)
    {
        // A pass that covers no simulated time must move no energy: no drain, no meal, no death. Stated explicitly so a
        // zero or nonsensical interval can never be what resolves an animal's starvation.
        if (!(dt > 0) || double.IsInfinity(dt)) return;
        double now = _w.Clock.SimSeconds;
        // Span this pass accounts for, in the simulated-seconds unit that DisturbedUntil is stamped in. The scheduler
        // passes biological seconds (Cadence * FixedStepSeconds * BioAcceleration), so undo the acceleration to get
        // back the wall-clock span the exposure below is measured against.
        double accel = _w.Clock.BioAcceleration;
        double span = accel >= 1 ? dt / accel : dt;
        var naturalDeaths = new Dictionary<EntityId, string>();
        foreach (var f in _w.Fauna.Items)
        {
            if (_predationDeaths.ContainsKey(f.Id)) continue;
            var sp = C.FaunaOrThrow(f.SpeciesId);
            var ph = PhenotypeOf(f);
            // Habitat is sampled once because it cannot change inside the pass: no other system runs while this loop is
            // open and the pass moves nothing, so the value read now is the value the whole interval sees. The one
            // exposure that does straddle the interval boundary is the disturbance, which is a pure timestamp, so it is
            // integrated rather than sampled.
            var suit = Suitability(sp, f.PositionXZ);
            f.LastSuitability = suit.HardRefused ? 0 : suit.Score;
            double stress = suit.HardRefused && !f.Grabbed ? 6.0 : 1.0;
            // Seconds of this interval the animal spent disturbed: the overlap of [now, now+span] with the tail of its
            // disturbance. Clamping both ends makes the extra drain depend only on how long the animal was actually
            // disturbed, not on where the pass boundary happened to fall - charging a whole wide interval because the
            // animal was disturbed at its first instant is the frame-rate dependence a coarser cadence would otherwise
            // introduce, and at cadence 6 it would charge 60 s of stress for a poke that ended 1 s in.
            double disturbedSeconds = MathD.Clamp(f.DisturbedUntil - now, 0, span);
            stress *= 1 + 0.3 * (disturbedSeconds / span);
            f.Energy -= sp.BasalRate * ph.MetabolicScale * stress * dt;
            // One meal per pass, offered at the accounting boundary and scaled by the whole interval, so intake is
            // rate * dt exactly like the drain. It is deliberately not subdivided: sub-passes would multiply the
            // number of feeding opportunities - and therefore the number of predation kills - that a span of time
            // contains, which is a behavioural change, not an accounting one.
            if (f.Energy < sp.HungerThreshold * sp.MaxEnergy && !f.Grabbed) Feed(f, sp, ph, dt);
            // Clamp and resolve death at the boundary, after feeding has had its chance, so starvation is a pure
            // function of the state this pass produced and not of the order animals happen to be stored in.
            f.Energy = MathD.Clamp(f.Energy, 0, sp.MaxEnergy);
            if (f.Energy <= 0 && !_predationDeaths.ContainsKey(f.Id))
                naturalDeaths[f.Id] = suit.HardRefused ? "stranded" : "starvation";
        }

        foreach (var f in _w.Fauna.Items.Where(x => x.Energy <= 0).ToList())
        {
            if (_predationDeaths.Remove(f.Id, out var pred))
                RemoveFauna(f, pred.Cause, pred.Detritus);
            else
                Kill(f, naturalDeaths.GetValueOrDefault(f.Id, "starvation"));
        }
    }

    private void Feed(FaunaIndividual f, FaunaSpeciesDef sp, Phenotype ph, double dt)
    {
        var p = f.PositionXZ;
        int cell = _w.Grid.NearestDomainCell(p);
        if (cell < 0) return;
        double room = sp.MaxEnergy - f.Energy;
        double consumed = 0, appetite = 0;
        // The kill budget for this pass, shared by every predation arm of the diet. A predator normally names three
        // or four prey species and each of those entries is an independent chance to reach a victim, so without one
        // shared budget a single feeding pass could remove several whole prey and then do it again on the next
        // metabolism step - which is what wiped out the small fauna. One kill per pass; abstract bites are still
        // allowed after it is spent.
        bool preyKilledThisPass = false;
        foreach (var d in sp.Diet)
        {
            if (room <= 1e-12) break;
            double rate = d.RatePerSecond * ph.MassScale * dt;
            appetite += rate;
            double want = Math.Min(rate, room / Math.Max(d.Efficiency, 1e-9));
            double got = TakeResource(f, sp, p, cell, d.Resource, want, ref preyKilledThisPass);
            if (got <= 0) continue;
            consumed += got;
            f.Energy += got * d.Efficiency;
            room = sp.MaxEnergy - f.Energy;
        }
        // The preferred diet has had its turn. Only if it could not even be partly satisfied does the animal fall
        // back on its guild's resource, so a species' own preferences keep full priority and the fallback only ever
        // sees an empty or near-empty pool of that resource. Predators have no fallback and are unaffected.
        if (room > 1e-12 && consumed < appetite * FallbackTriggerFraction)
        {
            var fb = EcologyGuildFallbacks.Fallback(sp.Guild);
            if (fb != null)
            {
                double want = Math.Min(fb.RatePerSecond * ph.MassScale * dt, room / Math.Max(fb.Efficiency, 1e-9));
                // A guild fallback is always detritus or biofilm, never a fauna: resource, so threading the shared
                // kill budget through the dispatcher here is inert - it only keeps one code path for both callers.
                double got = TakeResource(f, sp, p, cell, fb.Resource, want, ref preyKilledThisPass);
                if (got > 0) { consumed += got; f.Energy += got * fb.Efficiency; }
            }
        }
        if (consumed > 0) _w.Ecology.AddWasteNutrients(p, consumed * sp.WasteFraction);
    }

    /// <summary>
    /// Resource dispatch shared by the preferred diet and the guild fallback, so a fallback meal is removed from
    /// exactly the same pool a diet entry would use, at exactly the same efficiency model.
    /// </summary>
    private double TakeResource(FaunaIndividual f, FaunaSpeciesDef sp, Vec2 p, int cell, string resource, double want, ref bool preyKilledThisPass)
    {
        if (resource == "biofilm")
            return Coverage.Aquatic.AquaticBiofilm.GrazeCell(_w.Coverage.AlgaeBed, _w.Fields.Biofilm, cell, want);
        if (resource.StartsWith("flora:", StringComparison.Ordinal)) return GrazeFlora(p, resource[6..], want);
        if (resource.StartsWith("fauna:", StringComparison.Ordinal)) return GrazeFauna(f, sp, resource[6..], want, ref preyKilledThisPass);
        if (resource == "detritus") return _w.Litter.TakeDetritus(cell, want);
        var field = _w.Fields.Resource(resource);
        return field == null ? 0 : field.Take(cell, want);
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

    /// <summary>
    /// Whether <paramref name="x"/> is a member of the community this hunter is allowed to fall back on when its named
    /// quarry is absent: alive and unheld, sharing the hunter's medium, not itself a predator, and within the
    /// size window. This is what decouples a predator's survival from whatever species its diet happens to name -
    /// without it, a predator whose named prey is locally extinct simply starves beside edible animals.
    /// </summary>
    private bool IsEdiblePrey(FaunaSpeciesDef hunterSp, FaunaIndividual x)
    {
        if (x.Energy <= 0 || x.Grabbed) return false;
        var preySp = C.FaunaOrThrow(x.SpeciesId);
        if (preySp.Medium != hunterSp.Medium) return false;      // no cross-medium eating
        if (preySp.Guild == EcologyGuild.Predator) return false; // no cannibalism by default
        double hunterMid = (hunterSp.SizeMin + hunterSp.SizeMax) * 0.5;
        double preyMid = (preySp.SizeMin + preySp.SizeMax) * 0.5;
        if (hunterMid <= 0 || preyMid <= 0) return false;
        return preyMid >= hunterMid * MinPreySizeRatio && preyMid <= hunterMid * MaxPreySizeRatio;
    }

    /// <summary>
    /// The one path that yields predation energy and the only path that kills. A <c>fauna:species-id</c> diet entry
    /// names a steering preference, never a guaranteed kill: the victim is always drawn from the community
    /// <see cref="IsEdiblePrey"/> admits (alive and unheld, same medium, not itself a predator, mid-size inside the
    /// huntable window), and the named species only wins first refusal *within that eligible set*. A diet naming four
    /// prey species therefore reaches this same capped path four times instead of removing four animals, and the
    /// steering that actually makes those species worth hunting still lives in <see cref="FoodAt"/>.
    /// </summary>
    private double GrazeFauna(FaunaIndividual hunter, FaunaSpeciesDef hunterSp, string preySpecies, double want, ref bool preyKilledThisPass)
    {
        double radius = MathD.Clamp(hunterSp.SenseRadius * 0.35, 0.10, 0.30);
        _w.Fauna.Neighbours(hunter.PositionXZ, radius, _nb);
        _nb.RemoveAll(x => x.Id == hunter.Id || !IsEdiblePrey(hunterSp, x));
        if (_nb.Count == 0) return 0;
        // lowest id first, so victim selection stays a pure function of world state: same seed, same victim
        if (_nb.Count > 1) _nb.Sort((a, b) => a.Id.Value.CompareTo(b.Id.Value));

        // The named species is the author's stated intent, so it is taken first when one is eligible; otherwise the
        // lowest-id eligible neighbour is taken. Never an ineligible one, so naming a species in a diet can no longer
        // remove an animal the hunter should not be able to eat at all.
        int pick = _nb.FindIndex(x => x.SpeciesId == preySpecies);
        if (pick < 0) pick = 0;

        var prey = _nb[pick];
        var preySp = C.FaunaOrThrow(prey.SpeciesId);
        double biomass = preySp.MassAtMid * PhenotypeOf(prey).MassScale;
        double take = Math.Min(want, biomass);
        if (take <= 0) return 0;

        // The victim is removed only when the bite covers its whole body or it is too weak to survive it. Anything
        // less is an abstract bite: the hunter takes the energy, the prey keeps the rest of its body, and no corpse or
        // detritus is recorded - the survivor returns its own mass once, when it actually dies, so recording a death
        // here would count that mass twice.
        if (take < biomass && take < prey.Energy)
        {
            prey.Energy -= take;
            return take;
        }

        // This bite would kill, but the pass has already spent its one kill. Leave the victim entirely alone rather
        // than removing it under a second cause, which is what let a three-entry diet wipe a species in one pass.
        if (preyKilledThisPass) return 0;
        preyKilledThisPass = true;

        // The bite covers the whole body or finishes it off, so the individual dies here. Predation consumes part of
        // the prey's organic mass and returns the uneaten remainder to visible detritus, recorded once against this
        // one individual.
        double organic = biomass * preySp.DetritusOnDeath;
        _predationDeaths[prey.Id] = ($"predation:{hunter.SpeciesId}", Math.Max(0, organic - take));
        prey.Energy = 0;
        return take;
    }

    // ------------------------------------------------------------------ lifecycle

    public void StepLifecycle(double dt)
    {
        // Every quantity in this pass is a function of the interval the scheduler hands over, never of "one pass
        // = one day". The cadence is coarse (Cadence.FaunaLifecycle) and may be raised again, so the pass must stay
        // correct when a single call spans far more biological time than the last one did.
        //   dt  - elapsed BIOLOGICAL seconds for this window: the Bio(...) wrapper has already applied
        //         BioAcceleration, so it grows with the cadence instead of being a fixed per-call amount.
        //   now - the authoritative biological clock, the same axis ages and cooldowns are written on.
        // Ageing, maturity, old age, cooldowns and the death hazard below are therefore all expressed in
        // biological seconds (or per biological day) and scale with dt by construction.
        double now = _w.Clock.BioSeconds;
        double bioDays = dt / SimUnits.Day;
        long tick = _w.Clock.Tick;
        // Refill the spatial index on the same bounded cadence as behaviour rather than every lifecycle pass. Ageing,
        // maturity, mortality, cooldown and birth order read no index at all; the only readers are the mate search and
        // the local-density check, so the same argument holds. Membership stays exact (Add inserts in place,
        // Remove/RemoveMany rebuild), and a lagging bucket can only drop a candidate that has just crossed a bucket
        // boundary, never admit a wrong one, because Query re-tests each candidate's live position. Bucket contents
        // stay in ascending id order, so the id-ordered mate choice is unchanged. Counted in calls rather than
        // Clock.Tick % N, because Clock.Tick is assigned after systems run and a modulus on the observed tick would
        // fire on a different phase than intended.
        if (++_lifecycleStepsSinceIndexRebuild >= IndexRebuildPeriod) { _lifecycleStepsSinceIndexRebuild = 0; _w.Fauna.RebuildIndex(); }
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
                // Probability of dying somewhere inside the WHOLE window, not on one nominal day: a coarser
                // cadence must not make an animal less likely to die over the same elapsed biological time. The
                // draw is still keyed per individual per pass, so it stays deterministic and save-safe (the tick
                // is part of the serialized clock).
                double pDie = 1 - Math.Pow(1 - sp.DailyMortality, bioDays);
                if (Rng.HashUnit(_w.Seed, _mortalityHash, f.Id.Value, (ulong)tick) < pDie) { dead.Add((f, "mortality")); continue; }
            }
            if (reproducedThisStep.Contains(f.Id)) continue;
            // Eligibility is judged once per pass, against the world as it stood when the window opened: a
            // coarse cadence means many animals clear it at the same moment. That is a queue, not a licence -
            // the caps are re-tested per birth in the loop below, so a large eligible cohort is throttled to
            // what the population can actually carry rather than all firing at once. Cooldowns are written on
            // `now`, the biological clock, so they last a real biological interval and not "until the next pass".
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
                if (AtFaunaBudget) break;
                // MaxLocalDensity is the one cap that was only ever consulted at eligibility time, which under a
                // coarse window let every pair admitted at the window's opening overfill the same patch together.
                // Re-test it per birth, with the same radius and threshold CanReproduce uses, so it can only ever
                // refuse a birth the eligibility pass had not yet accounted for. PopulationCap and the world budget
                // are untouched above.
                _w.Fauna.Neighbours(a.PositionXZ, sp.SenseRadius, _nb);
                if (_nb.Count(o => o.SpeciesId == sp.Id) > sp.MaxLocalDensity) break;
                CreateOffspring(sp, a, b != null && _w.Fauna.Get(b.Id) != null ? b : null);
            }
            a.Energy = Math.Max(0.01, a.Energy);
        }
    }

    /// <summary>Reproduction eligibility from age, energy, habitat, cooldown, the species safety cap, the world budget and crowding.</summary>
    public bool CanReproduce(FaunaIndividual f, FaunaSpeciesDef sp, double now, out string reason)
    {
        if (f.Grabbed) { reason = "held"; return false; }
        if (f.Stage != FaunaLifeStage.Adult) { reason = "not mature"; return false; }
        if (f.Energy < sp.ReproMinEnergy * sp.MaxEnergy) { reason = "not enough energy"; return false; }
        if (now < f.ReproCooldownUntil) { reason = "recovering"; return false; }
        var s = Suitability(sp, f.PositionXZ);
        if (s.HardRefused || s.Score < sp.MinSuitability) { reason = "poor habitat"; return false; }
        if (_w.Fauna.CountOf(sp.Id) >= sp.PopulationCap) { reason = "population safety cap"; return false; }
        if (AtFaunaBudget) { reason = "world fauna budget"; return false; }
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

    /// <summary>
    /// Introduction eligibility against the world budget, for the deliberate reintroduction tool. The per-species
    /// safety cap is checked by the caller that owns the introduction report; this covers the whole-vivarium bound.
    /// </summary>
    public bool CanIntroduce(FaunaSpeciesDef sp, out string reason)
    {
        if (AtFaunaBudget) { reason = $"{sp.Name} cannot be introduced: world fauna budget reached ({_w.Descriptor.FaunaBudget})"; return false; }
        reason = "eligible";
        return true;
    }

    /// <summary>Creates a founder individual (starter population, reintroduction, introduction tool).</summary>
    public FaunaIndividual CreateFounder(FaunaSpeciesDef sp, Vec2 pos, double? ageFraction = null)
    {
        // Refuse before any id is allocated, so a rejected introduction cannot perturb the deterministic id stream.
        if (!CanIntroduce(sp, out var why)) throw new InvalidOperationException(why);
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
        var sp = C.FaunaOrThrow(f.SpeciesId);
        var ph = PhenotypeOf(f);
        double mass = sp.MassAtMid * ph.MassScale * sp.DetritusOnDeath;
        return RemoveFauna(f, cause, mass);
    }

    private bool RemoveFauna(FaunaIndividual f, string cause, double detritus)
    {
        if (_w.Fauna.Get(f.Id) == null) return false;
        _w.Fauna.Remove(f.Id);
        var sp = C.FaunaOrThrow(f.SpeciesId);
        if (detritus > 0) _w.Ecology.ReturnOrganicMatter(f.PositionXZ, detritus, 0, fromFlora: false);
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
