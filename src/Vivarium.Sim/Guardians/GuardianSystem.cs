using System;
using System.Collections.Generic;
using System.Linq;
using Vivarium.Sim.Content; using Vivarium.Sim.Fauna; using Vivarium.Sim.Core;
using Vivarium.Sim.Ecology;
using Vivarium.Sim.Flora;
using Vivarium.Sim.Time;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Guardians;

/// <summary>
/// Simulation system governing the island's unique Guardian Animal.
/// Manages utility-based behavior, locomotion pathing, metabolism, and closed-loop ecological consequences
/// (fruit consumption, seed dispersal, nutrient cycling, and substrate disturbance).
/// </summary>
public sealed class GuardianSystem
{
    private readonly VivariumWorld _world;
    private GuardianClimbing? _climbing;
    public GuardianDef? Def { get; private set; }
    public GuardianHabitat? Habitat { get; private set; }

    public GuardianSystem(VivariumWorld world)
    {
        _world = world;
        RefreshDefinition();
    }

    public void RefreshDefinition()
    {
        _climbing = null;
        if (_world.Guardian != null)
        {
            Def = GuardianCatalog.Find(_world.Guardian.ArchetypeId);
        }
        else if (_world.Descriptor.GuardianId != "none")
        {
            var formId = GuardianCatalog.SeededGuardianFormId(_world.Descriptor);
            Def = formId != null ? GuardianCatalog.Find(formId) : null;
        }
        else
        {
            Def = null;
        }

        if (Def != null || _world.PilotTree != null)
        {
            Habitat = new GuardianHabitat(_world.PilotTree, _world.Domain, _world);
        }
        else
        {
            Habitat = null;
        }
    }

    /// <summary>
    /// Creates or re-initializes the island's guardian deterministically.
    /// </summary>
    private static readonly string[] VisualVariants = { "default", "warm", "umbral", "dappled", "ochre", "moss" };

    /// <summary>
    /// Player interaction hook: startles or alerts the island guardian.
    /// Bold species enter alert posture; shy species alert or retreat.
    /// </summary>
    public void NotifyPoked(Vec2 sourcePos)
    {
        var g = _world.Guardian;
        if (g == null || Def == null) return;

        g.Alertness = 1.0;
        g.Arousal = Math.Clamp(g.Arousal + 0.4, 0.0, 1.0);

        string arch = Def.Archetype;
        bool isBold = arch is "hushbill" or "shoebill_hushbill" or "great_ground" or "monitor_great"
                           or "fernback" or "thylacine_fernback" or "canopy_serpent" or "serpent_canopy";

        var toSource = sourcePos - g.PositionXZ;
        if (isBold)
        {
            if (toSource.LengthSq > 1e-6)
                g.Heading = Math.Atan2(toSource.Z, toSource.X);
            g.Activity = GuardianActivity.Alert;
            g.ActivityDuration = 2.8;
            g.CurrentSpeed = 0.0;
        }
        else
        {
            var rng = Rng.Keyed(g.Seed, "guardian.poke", g.RngCounter++);
            if (rng.NextDouble() < 0.5)
            {
                if (toSource.LengthSq > 1e-6)
                    g.Heading = Math.Atan2(toSource.Z, toSource.X);
                g.Activity = GuardianActivity.Alert;
                g.ActivityDuration = 1.8;
                g.CurrentSpeed = 0.0;
            }
            else
            {
                var away = (g.PositionXZ - sourcePos).Normalized();
                if (away.LengthSq < 1e-6) away = Vec2.FromAngle(g.Heading);
                var retreatPos = _world.Domain.ClampInside(g.PositionXZ + away * 2.5, 0.4);
                g.TargetPosition = new Vec3(retreatPos.X, _world.GroundHeight(retreatPos), retreatPos.Z);
                g.Heading = Math.Atan2(away.Z, away.X);
                g.Activity = GuardianActivity.Retreat;
                g.ActivityDuration = 3.0;
                RecordVisitedSite(g, retreatPos);
            }
        }
    }

    public GuardianIndividual? InitializeGuardian(string? archetypeOverride = null)
    {
        RefreshDefinition();

        string? targetArchetype = archetypeOverride ?? GuardianCatalog.SeededGuardianFormId(_world.Descriptor);
        if (targetArchetype == null || targetArchetype == "none") return null;

        var def = GuardianCatalog.Find(targetArchetype);
        if (def == null) return null;

        Def = def;
        _world.Descriptor.GuardianId = def.Id;
        if (Habitat == null)
        {
            Habitat = new GuardianHabitat(_world.PilotTree, _world.Domain, _world);
        }

        var rng = Rng.Keyed(_world.Seed, "guardian.init", (ulong)(_world.PilotTree?.Corner ?? 0));
        var id = _world.Ids.Next(EntityKind.Fauna);

        var spawnPos = Habitat?.HomeAnchor ?? new Vec3(0, _world.SurfaceHeight(Vec2.Zero), 0);

        var g = new GuardianIndividual
        {
            Id = id,
            ArchetypeId = def.Id,
            HomeTreeId = _world.PilotTree?.Def.Id ?? "none",
            Seed = rng.NextULong(),
            Scale = 1.0 + rng.Range(-0.05, 0.08),
            Temperament = "inquisitive",
            VisualVariant = VisualVariants[rng.NextInt(VisualVariants.Length)],
            Energy = def.EnergyMax * 0.85,
            Hydration = 1.0,
            Fatigue = 0.1,
            Comfort = 1.0,
            BodyTemperature = 0.5,
            Arousal = 0.5,
            Alertness = 0.5,
            Activity = GuardianActivity.Rest,
            ActivityDuration = rng.Range(1.0, 3.0),
            HomeAttachmentId = "trunk_base",
            X = spawnPos.X,
            Y = spawnPos.Y,
            Z = spawnPos.Z,
            Heading = rng.Range(0, Math.PI * 2),
            Pitch = 0,
            Locomotion = def.Locomotion,
            LastDecisionBioSeconds = _world.Clock.BioSeconds,
            RngCounter = 0
        };
        g.Y = _world.GroundHeight(g.PositionXZ);
        g.TargetPosition = g.Position;

        _world.Guardian = g;
        return g;
    }

    /// <summary>
    /// Evaluates needs and decides behavioral intent and movement waypoints.
    /// </summary>
    public void StepBehaviour(double dt)
    {
        // Catalog speeds and activity lengths describe observable seconds at 1x.
        // The ecological clock advances 168 simulated seconds per real second.
        double visibleDt = dt / SimClock.DefaultSimSecondsPerRealSecond;
        int slices = Math.Max(1, (int)Math.Ceiling(visibleDt / 0.1));
        for (int i = 0; i < slices; i++) StepVisibleBehaviour(visibleDt / slices);
    }

    private void StepVisibleBehaviour(double dt)
    {
        var g = _world.Guardian;
        if (g == null || Def == null) return;

        g.ClimbCooldown = Math.Max(0, g.ClimbCooldown - dt);
        if (g.Activity == GuardianActivity.Climb && g.ClimbRoute.Count > 1)
        {
            StepClimbing(g, dt);
            return;
        }

        // Reconcile old saves as well as new spawns with the support surface.
        if (_world.PilotTree is { } tree && tree.BlocksTrunk(g.PositionXZ, 0.35))
        {
            var away = (g.PositionXZ - tree.Anchor).Normalized();
            if (away.LengthSq < 1e-6) away = -tree.Anchor.Normalized();
            var safe = _world.Domain.ClampInside(tree.Anchor + away * (tree.Def.TrunkRadius + 0.85), 0.4);
            g.X = safe.X; g.Z = safe.Z;
        }
        g.Y = _world.GroundHeight(g.PositionXZ);
        g.RngCounter++;

        if (g.Activity == GuardianActivity.Explore)
        {
            var profile = Def.BehaviorProfile ?? new GuardianBehaviorProfile();
            if (g.Fatigue > .65 || (1 - g.Energy / Def.EnergyMax) * profile.ForageWeight > .35 ||
                (1 - g.Hydration) * profile.DrinkFrequency > .35 || g.Comfort < .6 ||
                g.BodyTemperature < .35 * profile.BaskPriority)
                SelectNextActivity(g, dt);
        }

        // Execute locomotion towards TargetPosition
        ExecuteLocomotion(g, dt);

        switch (g.Activity)
        {
            case GuardianActivity.Drink:
                if (Vec2.Distance(g.PositionXZ, new Vec2(g.TargetX, g.TargetZ)) <= .4) ExecuteDrinking(g, dt);
                break;
            case GuardianActivity.Feed:
                ExecuteFeeding(g, dt);
                break;
            case GuardianActivity.Bathe:
                if (Vec2.Distance(g.PositionXZ, new Vec2(g.TargetX, g.TargetZ)) <= .4) ExecuteBathing(g, dt);
                break;
            case GuardianActivity.ShakeOff:
                // Stationary pause (1-3s), finishes bathing cycle
                break;
            case GuardianActivity.Dig:
                ExecuteDigging(g, dt);
                break;
            case GuardianActivity.Bask:
                ExecuteBasking(g, dt);
                break;
            case GuardianActivity.Play:
                ExecutePlaying(g, dt);
                break;
            case GuardianActivity.ScentMark:
                ExecuteScentMarking(g, dt);
                break;
            case GuardianActivity.Alert:
                // Stationary freeze/scan (1-2s)
                break;
            case GuardianActivity.Settle:
                // Circles/tucks for 3-5s, then switches to Sleep
                break;
            case GuardianActivity.Sleep:
                // Recovers fatigue faster
                g.Fatigue = MathD.Clamp01(g.Fatigue - 0.025 * (dt / 10.0));
                break;
            case GuardianActivity.Stretch:
                // Waking stretch (2-4s), then selects next activity
                break;
            case GuardianActivity.Groom:
                g.Comfort = Math.Clamp(g.Comfort + 0.08 * dt, 0.0, 1.0);
                break;
            case GuardianActivity.Yawn:
                break;
            case GuardianActivity.Rest:
                g.Fatigue = MathD.Clamp01(g.Fatigue - 0.015 * (dt / 10.0));
                break;
        }

        g.ActivityDuration -= dt;
        if (g.ActivityDuration <= 0)
        {
            if (g.Activity == GuardianActivity.Settle)
            {
                var rng = Rng.Keyed(g.Seed, "guardian.activity", g.RngCounter);
                g.Activity = GuardianActivity.Sleep;
                g.ActivityDuration = rng.Range(30.0, 60.0);
                g.ConsecutiveRestCycles++;
            }
            else if (g.Activity == GuardianActivity.Sleep)
            {
                var rng = Rng.Keyed(g.Seed, "guardian.activity", g.RngCounter);
                if (rng.NextDouble() < 0.45)
                {
                    g.Activity = GuardianActivity.Yawn;
                    g.ActivityDuration = rng.Range(2.0, 3.5);
                }
                else
                {
                    g.Activity = GuardianActivity.Stretch;
                    g.ActivityDuration = rng.Range(2.0, 4.0);
                }
            }
            else if (g.Activity == GuardianActivity.Yawn)
            {
                var rng = Rng.Keyed(g.Seed, "guardian.activity", g.RngCounter);
                g.Activity = GuardianActivity.Stretch;
                g.ActivityDuration = rng.Range(2.0, 4.0);
            }
            else if (g.Activity == GuardianActivity.Bathe)
            {
                var rng = Rng.Keyed(g.Seed, "guardian.activity", g.RngCounter);
                g.Activity = GuardianActivity.ShakeOff;
                g.ActivityDuration = rng.Range(1.0, 3.0);
            }
            else
            {
                SelectNextActivity(g, dt);
            }
        }
    }

    private static void RecordVisitedSite(GuardianIndividual g, Vec2 site)
    {
        g.RecentVisitedSites.Add(site);
        if (g.RecentVisitedSites.Count > 6)
        {
            g.RecentVisitedSites.RemoveAt(0);
        }
    }

    private static bool IsNearRecentSite(GuardianIndividual g, Vec2 candidate, double radius = 2.0)
    {
        double rSq = radius * radius;
        for (int i = 0; i < g.RecentVisitedSites.Count; i++)
        {
            if ((candidate - g.RecentVisitedSites[i]).LengthSq < rSq) return true;
        }
        return false;
    }

    private void SelectNextActivity(GuardianIndividual g, double dt)
    {
        var rng = Rng.Keyed(g.Seed, "guardian.activity", g.RngCounter);
        var def = Def!;
        var profile = def.BehaviorProfile ?? new GuardianBehaviorProfile();

        double daylight = Math.Clamp(Math.Sin((_world.Clock.BioDays % 1.0) * Math.PI), 0.0, 1.0);
        double nocturnal = profile.NocturnalBias;

        double fatigueScore = g.Fatigue;
        if (nocturnal > 0.55)
        {
            if (daylight > 0.55) fatigueScore += (daylight - 0.5) * 0.35;
            else if (daylight < 0.40) fatigueScore = Math.Max(0.0, fatigueScore - 0.25);
        }
        else if (nocturnal < 0.45)
        {
            if (daylight < 0.35) fatigueScore += (0.45 - daylight) * 0.35;
            else if (daylight > 0.50) fatigueScore = Math.Max(0.0, fatigueScore - 0.20);
        }

        double hungerScore = (1.0 - (g.Energy / def.EnergyMax)) * profile.ForageWeight;
        if (nocturnal > 0.55 && daylight < 0.45) hungerScore *= 1.25;
        else if (nocturnal < 0.45 && daylight > 0.45) hungerScore *= 1.25;

        double thirstScore = (1.0 - g.Hydration) * profile.DrinkFrequency;
        double discomfortScore = 1.0 - g.Comfort;

        if (fatigueScore > .65 || hungerScore > .35 || thirstScore > .35 || discomfortScore > .4)
            g.ClimbRoute.Clear();

        // 1. Retreat: startled / alert with low comfort retreats to cover / home
        if (g.Alertness > 0.88 && g.Comfort < 0.45)
        {
            var coverSite = Habitat?.NearestRestSite(g.Position) ?? Habitat?.HomeAnchor ?? new Vec3(0, _world.GroundHeight(Vec2.Zero), 0);
            g.Activity = GuardianActivity.Retreat;
            g.TargetPosition = coverSite;
            g.ActivityDuration = rng.Range(6.0, 12.0);
            RecordVisitedSite(g, coverSite.XZ);
            return;
        }

        // 2. Alert: elevated alertness pauses and scans surroundings
        if (g.Alertness > 0.78)
        {
            g.Activity = GuardianActivity.Alert;
            g.ActivityDuration = rng.Range(2.0, 3.5);
            g.CurrentSpeed = 0.0;
            return;
        }

        // 3. Yawn: waking transition from Rest / Sleep
        if (g.Activity is GuardianActivity.Rest or GuardianActivity.Sleep && g.ConsecutiveRestCycles > 0 && rng.NextDouble() < 0.35)
        {
            g.Activity = GuardianActivity.Yawn;
            g.ActivityDuration = rng.Range(2.0, 3.5);
            g.CurrentSpeed = 0.0;
            return;
        }

        // 4. Fatigue drive: needs Rest / ReturnHome / Settle
        if (fatigueScore > 0.65)
        {
            var restSite = Habitat?.NearestRestSite(g.Position) ?? new Vec3(0, _world.SurfaceHeight(Vec2.Zero), 0);
            double distToRest = Vec2.Distance(g.PositionXZ, restSite.XZ);
            if (distToRest <= 0.4)
            {
                g.Activity = GuardianActivity.Settle;
                g.TargetPosition = restSite;
                g.ActivityDuration = rng.Range(3.0, 5.0);
            }
            else
            {
                g.Activity = GuardianActivity.ReturnHome;
                g.TargetPosition = restSite;
                g.ActivityDuration = rng.Range(20.0, 45.0);
                RecordVisitedSite(g, restSite.XZ);
            }
            return;
        }

        // 5. Thirst drive: needs Drink
        if (thirstScore > 0.35)
        {
            var waterPoint = Habitat?.NearestWaterAccessPoint(g.PositionXZ);
            if (waterPoint != null)
            {
                var targetXZ = waterPoint.Value;
                double targetY = _world.GroundHeight(targetXZ);
                g.TargetPosition = new Vec3(targetXZ.X, targetY, targetXZ.Z);
                double dist = Vec2.Distance(g.PositionXZ, targetXZ);
                g.Activity = GuardianActivity.Drink;
                g.ActivityDuration = dist <= 0.4 ? rng.Range(5.0, 10.0) : rng.Range(15.0, 30.0);
                RecordVisitedSite(g, targetXZ);
                return;
            }
        }

        // 6. Hunger drive: needs Food / Forage / Dig
        if (hungerScore > 0.35)
        {
            var foodTarget = FindBestForageTarget(g, def, profile.RoamRadius);
            if (foodTarget != null)
            {
                var targetPos = foodTarget.Value;
                g.TargetPosition = targetPos;
                if (profile.CanDig && _world.SubstrateAt(targetPos.XZ) == Substrate.Soil && rng.NextDouble() < 0.45)
                    g.Activity = WithinFeedingReach(g) ? GuardianActivity.Dig : GuardianActivity.Forage;
                else
                    g.Activity = WithinFeedingReach(g) ? GuardianActivity.Feed : GuardianActivity.Forage;
                g.ActivityDuration = rng.Range(15.0, 30.0);
                RecordVisitedSite(g, targetPos.XZ);
                return;
            }
            else if (Habitat != null)
            {
                var forageXZ = Habitat.SelectForageTarget(g.Seed, g.RngCounter);
                if (Habitat.ForageHotspots.Count > 1 && IsNearRecentSite(g, forageXZ, 2.0))
                {
                    for (int i = 0; i < Habitat.ForageHotspots.Count; i++)
                    {
                        var cand = Habitat.ForageHotspots[i];
                        if (!IsNearRecentSite(g, cand, 2.0)) { forageXZ = cand; break; }
                    }
                }
                g.TargetPosition = new Vec3(forageXZ.X, _world.GroundHeight(forageXZ), forageXZ.Z);
                g.Activity = GuardianActivity.Forage;
                g.ActivityDuration = rng.Range(15.0, 30.0);
                RecordVisitedSite(g, forageXZ);
                return;
            }
        }

        // 7. Discomfort: if > 0.65 and shallow water nearby -> Bathe; else if > 0.4 -> Groom
        if (discomfortScore > 0.65)
        {
            var wadingPoint = Habitat?.NearestWadingZone(g.PositionXZ);
            if (wadingPoint != null && profile.CanSwim)
            {
                var targetXZ = wadingPoint.Value;
                double targetY = _world.GroundHeight(targetXZ);
                g.TargetPosition = new Vec3(targetXZ.X, targetY, targetXZ.Z);
                double dist = Vec2.Distance(g.PositionXZ, targetXZ);
                g.Activity = GuardianActivity.Bathe;
                g.ActivityDuration = dist <= 0.4 ? rng.Range(8.0, 15.0) : rng.Range(15.0, 30.0);
                RecordVisitedSite(g, targetXZ);
                return;
            }
            else
            {
                g.Activity = GuardianActivity.Groom;
                g.TargetPosition = g.Position;
                g.ActivityDuration = rng.Range(4.0, 8.0);
                return;
            }
        }
        else if (discomfortScore > 0.4)
        {
            g.Activity = GuardianActivity.Groom;
            g.TargetPosition = g.Position;
            g.ActivityDuration = rng.Range(3.0, 6.0);
            return;
        }

        // 8. Thermoregulation: if BodyTemperature < 0.35 * BaskPriority -> Bask
        if (g.BodyTemperature < 0.35 * profile.BaskPriority)
        {
            var baskSite = Habitat?.NearestBaskingSite(g.Position);
            Vec2 sunlitXZ = baskSite != null ? baskSite.Value.XZ : (Habitat?.NearestSunlitPoint(g.PositionXZ) ?? g.PositionXZ);
            double sunlitY = baskSite != null ? baskSite.Value.Y : _world.GroundHeight(sunlitXZ);
            g.TargetPosition = new Vec3(sunlitXZ.X, sunlitY, sunlitXZ.Z);
            double dist = Vec2.Distance(g.PositionXZ, sunlitXZ);
            g.Activity = GuardianActivity.Bask;
            g.ActivityDuration = dist <= 0.4 ? rng.Range(10.0, 20.0) : rng.Range(15.0, 30.0);
            RecordVisitedSite(g, sunlitXZ);
            return;
        }

        // 9. Scent marking: periodic territorial marking
        if (profile.TerritoryDrive > 0.35 && (_world.Clock.BioSeconds - g.LastScentMarkBioSeconds > 180) && Habitat?.ScentMarkTargets.Count > 0)
        {
            var target = Habitat.ScentMarkTargets[rng.NextInt(Habitat.ScentMarkTargets.Count)];
            g.TargetPosition = target;
            g.Activity = GuardianActivity.ScentMark;
            double dist = Vec2.Distance(g.PositionXZ, target.XZ);
            g.ActivityDuration = dist <= 0.4 ? rng.Range(4.0, 8.0) : rng.Range(12.0, 25.0);
            RecordVisitedSite(g, target.XZ);
            return;
        }

        // 10. Investigate: moderate-high arousal investigating fauna, fruiting flora, or props
        if ((g.Arousal > 0.4 || profile.CuriosityBias * g.Arousal > 0.35) && rng.NextDouble() < 0.65)
        {
            var invTarget = FindInvestigationTarget(g, profile.RoamRadius);
            if (invTarget != null)
            {
                g.TargetPosition = invTarget.Value;
                g.Activity = GuardianActivity.Investigate;
                g.ActivityDuration = rng.Range(8.0, 16.0);
                RecordVisitedSite(g, invTarget.Value.XZ);
                return;
            }
        }

        // 11. Play: if Arousal > PlayThreshold and basic needs met
        if (g.Arousal > profile.PlayThreshold)
        {
            g.Activity = GuardianActivity.Play;
            double playDist = rng.Range(0.5, 1.5);
            double playAngle = rng.Range(0, Math.PI * 2);
            var playXZ = _world.Domain.ClampInside(g.PositionXZ + new Vec2(Math.Cos(playAngle), Math.Sin(playAngle)) * playDist, 0.4);
            g.TargetPosition = new Vec3(playXZ.X, _world.GroundHeight(playXZ), playXZ.Z);
            g.ActivityDuration = rng.Range(5.0, 12.0);
            RecordVisitedSite(g, playXZ);
            return;
        }

        // With needs satisfied, keep one island destination until arrival.
        if (g.Activity == GuardianActivity.Explore && g.ActivityDuration > 0 &&
            Vec2.Distance(g.PositionXZ, new Vec2(g.TargetX, g.TargetZ)) > .05) return;
        SelectRoamingTarget(g);
    }

    private void SelectRoamingTarget(GuardianIndividual g)
    {
        if (Def!.BehaviorProfile?.CanClimbTrunk == true && g.ClimbCooldown <= 0)
        {
            g.ClimbCooldown = 60;
            _climbing ??= new GuardianClimbing(_world);
            if (_climbing.FindRoute(g, out ulong host) is { } route)
            {
                g.ClimbRoute = route; g.ClimbIndex = 1; g.ClimbReturning = false; g.ClimbHostId = host;
                g.TargetPosition = route[0].Position;
                g.Activity = GuardianActivity.Explore;
                g.ActivityDuration = 10 + Vec2.Distance(g.PositionXZ, g.TargetPosition.XZ) /
                    Math.Max(.01, Def.WalkSpeed * GuardianDef.BodySizeMultiplier) * 3;
                return;
            }
        }
        g.ClimbRoute.Clear();
        var rng = Rng.Keyed(g.Seed, "guardian.roam", g.RngCounter);
        double radius = _world.Domain.Radius;
        for (int attempt = 0; attempt < 96; attempt++)
        {
            var point = new Vec2(rng.Range(-radius, radius), rng.Range(-radius, radius));
            if (!_world.Domain.ContainsDisc(point, .4) ||
                Vec2.Distance(point, g.PositionXZ) < Math.Min(2.0, radius * .4) ||
                _world.PilotTree?.BlocksTrunk(point, .35) == true ||
                IsNearRecentSite(g, point, 2.0)) continue;
            int cell = _world.Grid.NearestDomainCell(point);
            if (cell < 0 || _world.Water.OpenWaterDepth(cell) > .08) continue;
            g.TargetPosition = new Vec3(point.X, _world.GroundHeight(point), point.Z);
            g.Activity = GuardianActivity.Explore;
            RecordVisitedSite(g, point);
            g.ActivityDuration = 10 + Vec2.Distance(g.PositionXZ, point) /
                Math.Max(.01, Def!.WalkSpeed * GuardianDef.BodySizeMultiplier) * 2;
            return;
        }
        // No suitable destination this pass; retry without a long idle cycle.
        g.Activity = GuardianActivity.Rest;
        g.TargetPosition = g.Position;
        g.ActivityDuration = .5;
    }

    private Vec3? FindInvestigationTarget(GuardianIndividual g, double radius)
    {
        double radiusSq = radius * radius;
        foreach (var f in _world.Fauna.Items)
        {
            if (f.Grabbed) continue;
            double dSq = (g.PositionXZ - f.PositionXZ).LengthSq;
            if (dSq > 0.25 && dSq <= radiusSq && !IsNearRecentSite(g, f.PositionXZ, 1.5))
            {
                return new Vec3(f.X, f.Y, f.Z);
            }
        }
        foreach (var fl in _world.Flora.Items)
        {
            if (!fl.Fruiting) continue;
            double dSq = (g.PositionXZ - fl.Position).LengthSq;
            if (dSq > 0.35 && dSq <= radiusSq && !IsNearRecentSite(g, fl.Position, 1.5))
            {
                return new Vec3(fl.X, _world.GroundHeight(fl.Position), fl.Z);
            }
        }
        if (_world.Props != null)
        {
            foreach (var rk in _world.Props.Rocks)
            {
                double dSq = (g.PositionXZ - rk.Position).LengthSq;
                if (dSq > 0.5 && dSq <= radiusSq && !IsNearRecentSite(g, rk.Position, 1.5))
                {
                    return new Vec3(rk.X, _world.GroundHeight(rk.Position), rk.Z);
                }
            }
            foreach (var lg in _world.Props.Logs)
            {
                double dSq = (g.PositionXZ - lg.Position).LengthSq;
                if (dSq > 0.5 && dSq <= radiusSq && !IsNearRecentSite(g, lg.Position, 1.5))
                {
                    return new Vec3(lg.X, _world.GroundHeight(lg.Position), lg.Z);
                }
            }
        }
        return null;
    }

    private Vec3? FindBestForageTarget(GuardianIndividual g, GuardianDef def, double radius)
    {
        double radiusSq = radius * radius;
        var diets = def.PreferredDiets;

        if (diets.Contains("fauna:small"))
        {
            FaunaIndividual? bestFauna = null;
            double bestDistSq = radiusSq;
            foreach (var f in _world.Fauna.Items)
            {
                if (f.Grabbed) continue;
                double d = (g.PositionXZ - f.PositionXZ).LengthSq;
                if (d < bestDistSq && !IsNearRecentSite(g, f.PositionXZ, 1.2))
                {
                    bestDistSq = d;
                    bestFauna = f;
                }
            }
            if (bestFauna != null)
                return new Vec3(bestFauna.X, bestFauna.Y, bestFauna.Z);
        }

        bool wantsFoliage = diets.Contains("flora:foliage");
        bool wantsBark = diets.Contains("flora:bark");
        bool wantsFungus = diets.Contains("flora:fungus");
        if (wantsFoliage || wantsBark || wantsFungus)
        {
            FloraIndividual? bestFlora = null;
            double bestDistSq = radiusSq;
            foreach (var fl in _world.Flora.Items)
            {
                if (fl.Biomass < 0.05) continue;
                var species = _world.Content.FloraById(fl.SpeciesId);
                bool isFungus = species?.Archetype == "fungus" || fl.SpeciesId.Contains("fung");
                if (wantsFungus && isFungus)
                {
                    double d = (g.PositionXZ - fl.Position).LengthSq;
                    if (d < bestDistSq && !IsNearRecentSite(g, fl.Position, 1.2))
                    {
                        bestDistSq = d; bestFlora = fl;
                    }
                }
                else if (wantsFoliage && !isFungus)
                {
                    double d = (g.PositionXZ - fl.Position).LengthSq;
                    if (d < bestDistSq && !IsNearRecentSite(g, fl.Position, 1.2))
                    {
                        bestDistSq = d; bestFlora = fl;
                    }
                }
            }
            if (bestFlora != null)
                return new Vec3(bestFlora.X, _world.GroundHeight(bestFlora.Position), bestFlora.Z);

            if (wantsBark && _world.Props != null)
            {
                foreach (var log in _world.Props.Logs)
                {
                    double d = (g.PositionXZ - log.Position).LengthSq;
                    if (d < radiusSq && !IsNearRecentSite(g, log.Position, 1.2))
                    {
                        return new Vec3(log.X, _world.GroundHeight(log.Position), log.Z);
                    }
                }
            }
        }

        if (diets.Contains("biofilm") && _world.Water != null && Habitat?.ShallowWadingZones.Count > 0)
        {
            var wading = Habitat.NearestWadingZone(g.PositionXZ);
            if (wading != null && !IsNearRecentSite(g, wading.Value, 1.5))
            {
                return new Vec3(wading.Value.X, _world.GroundHeight(wading.Value), wading.Value.Z);
            }
        }

        int cell = FindRichestFoodCell(g.PositionXZ, radius);
        if (cell >= 0)
        {
            var cellCenter = _world.Grid.CellCenter(cell);
            if (!IsNearRecentSite(g, cellCenter, 1.5))
                return new Vec3(cellCenter.X, _world.GroundHeight(cellCenter), cellCenter.Z);
        }

        return null;
    }

    private int FindRichestFoodCell(Vec2 current, double radius)
    {
        var cells = _world.Grid.CellsInRadius(current, radius);
        int bestCell = -1;
        double bestFood = 0.001;

        foreach (int cell in cells)
        {
            if (_world.PilotTree?.BlocksTrunk(_world.Grid.CellCenter(cell), .35) == true) continue;
            double fruit = _world.Litter.FruitMass[cell];
            double fine = _world.Litter.FineMass[cell];
            double total = fruit * 3.0 + fine;
            if (total > bestFood)
            {
                bestFood = total;
                bestCell = cell;
            }
        }
        return bestCell;
    }

    private bool IsPositionBlocked(Vec2 p, GuardianIndividual g, GuardianBehaviorProfile profile)
    {
        if (!_world.Domain.ContainsDisc(p, 0.45)) return true;
        if (_world.PilotTree != null && _world.PilotTree.BlocksTrunk(p, 0.38)) return true;

        if (_world.Props != null)
        {
            var rock = _world.Props.RockAt(p);
            if (rock != null)
            {
                bool isBaskingTarget = g.Activity == GuardianActivity.Bask &&
                    Vec2.Distance(new Vec2(g.TargetX, g.TargetZ), rock.Position) <= rock.FootprintRadius + 0.25;
                if (!isBaskingTarget) return true;
            }

            var log = _world.Props.LogAt(p);
            if (log != null)
            {
                bool isBaskingTarget = g.Activity == GuardianActivity.Bask &&
                    Vec2.Distance(new Vec2(g.TargetX, g.TargetZ), log.Position) <= log.FootprintRadius + 0.25;
                if (!isBaskingTarget) return true;
            }
        }

        if (_world.Water != null && !profile.CanSwim && _world.Water.OpenWaterDepth(p) > 0.14)
            return true;

        return false;
    }

    private void ExecuteLocomotion(GuardianIndividual g, double dt)
    {
        var def = Def!;
        var profile = def.BehaviorProfile ?? new GuardianBehaviorProfile();
        var currentXZ = g.PositionXZ;
        var targetXZ = new Vec2(g.TargetX, g.TargetZ);
        double dist = Vec2.Distance(currentXZ, targetXZ);

        if (g.Activity == GuardianActivity.Forage && WithinFeedingReach(g))
        {
            g.Activity = GuardianActivity.Feed;
            g.ActivityDuration = 10.0;
            g.CurrentSpeed = 0.0;
        }

        if (g.Activity == GuardianActivity.Feed)
        {
            if (WithinFeedingReach(g))
            {
                g.CurrentSpeed = Math.Max(0.0, g.CurrentSpeed - profile.Deceleration * dt);
                if (dist > 1e-6)
                    g.Heading = TurnTowards(g.Heading, Math.Atan2(targetXZ.Z - currentXZ.Z, targetXZ.X - currentXZ.X), profile.MaxTurnRate * dt);
                UpdateHopOffset(g, def, profile, dt);
                return;
            }
            g.Activity = GuardianActivity.Forage;
        }

        if ((g.Activity == GuardianActivity.Drink || g.Activity == GuardianActivity.Bathe) && dist <= 0.4)
        {
            g.CurrentSpeed = Math.Max(0.0, g.CurrentSpeed - profile.Deceleration * dt);
            if (dist > 1e-6)
                g.Heading = TurnTowards(g.Heading, Math.Atan2(targetXZ.Z - currentXZ.Z, targetXZ.X - currentXZ.X), profile.MaxTurnRate * dt);
            UpdateHopOffset(g, def, profile, dt);
            return;
        }

        if ((g.Activity == GuardianActivity.Bask || g.Activity == GuardianActivity.Dig || g.Activity == GuardianActivity.ScentMark) && dist <= 0.4)
        {
            g.CurrentSpeed = Math.Max(0.0, g.CurrentSpeed - profile.Deceleration * dt);
            if (dist > 1e-6)
                g.Heading = TurnTowards(g.Heading, Math.Atan2(targetXZ.Z - currentXZ.Z, targetXZ.X - currentXZ.X), profile.MaxTurnRate * dt);
            UpdateHopOffset(g, def, profile, dt);
            return;
        }

        if (g.Activity is GuardianActivity.Rest or GuardianActivity.Sleep or GuardianActivity.Settle 
            or GuardianActivity.Stretch or GuardianActivity.Alert or GuardianActivity.ShakeOff or GuardianActivity.Groom or GuardianActivity.Yawn)
        {
            g.CurrentSpeed = 0.0;
            g.VerticalOffset = Math.Max(0.0, g.VerticalOffset - dt * 0.5);
            return;
        }

        if (dist > 0.05)
        {
            double dz = targetXZ.Z - currentXZ.Z;
            double dx = targetXZ.X - currentXZ.X;
            double targetHeading = Math.Atan2(dz, dx);
            double headingDiff = WrapAngle(targetHeading - g.Heading);
            double headingError = Math.Abs(headingDiff);

            double maxTurn = profile.MaxTurnRate * dt;
            g.Heading = WrapAngle(g.Heading + Math.Clamp(headingDiff, -maxTurn, maxTurn));

            double alignFactor = Math.Clamp(1.0 - (headingError / (Math.PI * 0.75)), 0.12, 1.0);

            var normal = _world.Terrain.Normal(currentXZ);
            double slopeFactor = Math.Clamp(normal.Y, 0.35, 1.0);
            double slopeAngleDegrees = Math.Acos(Math.Clamp(normal.Y, 0.0, 1.0)) * (180.0 / Math.PI);
            if (slopeAngleDegrees > profile.MaxSlopeDegrees) slopeFactor *= 0.5;

            double waterDepth = _world.Water != null ? _world.Water.OpenWaterDepth(currentXZ) : 0.0;
            double waterFactor = waterDepth > 0.01 ? Math.Clamp(1.0 - (waterDepth * 2.0), 0.45, 1.0) : 1.0;

            double baseSpeed = (g.Activity == GuardianActivity.Retreat ? def.RunSpeed : def.WalkSpeed) * GuardianDef.BodySizeMultiplier;
            double desiredSpeed = baseSpeed * alignFactor * slopeFactor * waterFactor;

            if (dist < 0.65)
            {
                double arrivalFactor = Math.Clamp(dist / 0.65, 0.15, 1.0);
                desiredSpeed *= arrivalFactor;
            }

            if (desiredSpeed > g.CurrentSpeed)
                g.CurrentSpeed = Math.Min(desiredSpeed, g.CurrentSpeed + profile.Acceleration * dt);
            else
                g.CurrentSpeed = Math.Max(desiredSpeed, g.CurrentSpeed - profile.Deceleration * dt);

            double step = Math.Min(dist, g.CurrentSpeed * dt);
            Vec2 forwardDir = Vec2.FromAngle(g.Heading);
            Vec2 nextXZ = currentXZ + forwardDir * step;

            if (IsPositionBlocked(nextXZ, g, profile))
            {
                double[] feelerOffsets = { 0.45, -0.45, 0.95, -0.95, 1.45, -1.45 };
                bool feelerFound = false;
                foreach (double offset in feelerOffsets)
                {
                    double feelerHeading = WrapAngle(g.Heading + offset);
                    Vec2 feelerDir = Vec2.FromAngle(feelerHeading);
                    Vec2 feelerXZ = currentXZ + feelerDir * step;
                    if (!IsPositionBlocked(feelerXZ, g, profile))
                    {
                        nextXZ = feelerXZ;
                        g.Heading = TurnTowards(g.Heading, feelerHeading, profile.MaxTurnRate * dt);
                        feelerFound = true;
                        break;
                    }
                }

                if (!feelerFound)
                {
                    SelectRoamingTarget(g);
                    g.CurrentSpeed = 0.0;
                    UpdateHopOffset(g, def, profile, dt);
                    return;
                }
            }

            nextXZ = _world.Domain.ClampInside(nextXZ, 0.2);

            if (_world.PilotTree != null && _world.PilotTree.BlocksTrunk(nextXZ, 0.38))
            {
                var away = (currentXZ - _world.PilotTree.Anchor).Normalized();
                var tangent = new Vec2(-away.Z, away.X);
                if ((targetXZ - currentXZ).X * tangent.X + (targetXZ - currentXZ).Z * tangent.Z < 0) tangent = -tangent;
                nextXZ = currentXZ + tangent * step;
                var radial = (nextXZ - _world.PilotTree.Anchor).Normalized();
                nextXZ = _world.PilotTree.Anchor + radial * Math.Max(Vec2.Distance(nextXZ, _world.PilotTree.Anchor), _world.PilotTree.Def.TrunkRadius + 0.4);
                nextXZ = _world.Domain.ClampInside(nextXZ, 0.2);
            }

            g.X = nextXZ.X;
            g.Z = nextXZ.Z;

            double groundY = _world.GroundHeight(nextXZ);
            if (_world.PilotTree != null)
            {
                double rootY = _world.PilotTree.RootSurfaceHeight(nextXZ);
                if (!double.IsNaN(rootY) && rootY > groundY) groundY = rootY;
            }
            if (_world.Props != null)
            {
                double propTop = _world.Props.PropTopAt(nextXZ);
                if (!double.IsNaN(propTop) && propTop > groundY) groundY = propTop;
            }
            g.Y = groundY;

            g.Fatigue = MathD.Clamp01(g.Fatigue + 0.003 * (dt / 10.0));

            UpdateHopOffset(g, def, profile, dt);

            double remainingDist = Vec2.Distance(nextXZ, targetXZ);
            if ((g.Activity == GuardianActivity.Drink || g.Activity == GuardianActivity.Bathe) && remainingDist <= 0.4)
            {
                return;
            }
        }
        else
        {
            g.CurrentSpeed = Math.Max(0.0, g.CurrentSpeed - profile.Deceleration * dt);
            UpdateHopOffset(g, def, profile, dt);

            if (g.Activity == GuardianActivity.Forage)
            {
                g.Activity = WithinFeedingReach(g) ? GuardianActivity.Feed : GuardianActivity.Groom;
                g.ActivityDuration = g.Activity == GuardianActivity.Feed ? 10.0 : .1;
            }
            else if (g.Activity == GuardianActivity.ReturnHome)
            {
                g.Activity = GuardianActivity.Settle;
                g.ActivityDuration = 3.5;
            }
            else if (g.Activity == GuardianActivity.Explore || g.Activity == GuardianActivity.Investigate || g.Activity == GuardianActivity.Retreat)
            {
                if (g.ClimbRoute.Count > 1 && profile.CanClimbTrunk)
                {
                    g.Activity = GuardianActivity.Climb;
                    g.ClimbIndex = 1; g.ClimbReturning = false;
                    g.ClimbNormal = Vec3.Up;
                    g.TargetPosition = g.ClimbRoute[1].Position;
                }
                else SelectNextActivity(g, dt);
            }
        }
    }

    private static void UpdateHopOffset(GuardianIndividual g, GuardianDef def, GuardianBehaviorProfile profile, double dt)
    {
        if (def.Locomotion == LocomotionMode.AvianPerchHop && g.CurrentSpeed > 0.05 && profile.HopArcHeight > 0)
        {
            g.HopPhase += dt * profile.HopCadence * Math.PI * 2.0;
            g.VerticalOffset = Math.Abs(Math.Sin(g.HopPhase * 0.5)) * profile.HopArcHeight;
        }
        else
        {
            g.VerticalOffset = Math.Max(0.0, g.VerticalOffset - dt * 0.5);
        }
    }
    private void StepClimbing(GuardianIndividual g, double dt)
    {
        var profile = Def!.BehaviorProfile!;
        bool need = g.Fatigue > .65 || (1 - g.Energy / Def.EnergyMax) * profile.ForageWeight > .35 ||
            (1 - g.Hydration) * profile.DrinkFrequency > .35 || g.Comfort < .6;
        bool hostGone = g.ClimbHostId != 0 && !_world.Flora.Items.Any(f => f.Id.Value == g.ClimbHostId);
        if ((need || hostGone) && !g.ClimbReturning)
        {
            g.ClimbReturning = true;
            g.ClimbIndex = Math.Max(0, g.ClimbIndex - 1);
        }
        g.ClimbIndex = Math.Clamp(g.ClimbIndex, 0, g.ClimbRoute.Count - 1);
        var next = g.ClimbRoute[g.ClimbIndex];
        var delta = next.Position - g.Position;
        double distance = delta.Length;
        double step = Def.WalkSpeed * GuardianDef.BodySizeMultiplier * .65 * dt;
        if (distance > 1e-8)
        {
            g.Position += delta / distance * Math.Min(step, distance);
            g.ClimbDirection = delta / distance;
            if (delta.XZ.LengthSq > 1e-6) g.Heading = Math.Atan2(delta.Z, delta.X);
        }
        g.ClimbNormal = Vec3.Lerp(g.ClimbNormal, next.Normal, Math.Min(1, dt * 5)).Normalized();
        g.TargetPosition = next.Position;
        g.Fatigue = MathD.Clamp01(g.Fatigue + dt * .0006);
        if (distance <= step + .005)
        {
            if (g.ClimbReturning && g.ClimbIndex == 0)
            {
                g.ClimbRoute.Clear(); g.ClimbNormal = Vec3.Up; g.ClimbCooldown = 45;
                g.Activity = GuardianActivity.Rest; g.ActivityDuration = 0;
                SelectNextActivity(g, dt);
                return;
            }
            if (!g.ClimbReturning && g.ClimbIndex == g.ClimbRoute.Count - 1) g.ClimbReturning = true;
            g.ClimbIndex += g.ClimbReturning ? -1 : 1;
        }
    }

    private void ExecuteDrinking(GuardianIndividual g, double dt)
    {
        g.Hydration = Math.Clamp(g.Hydration + 0.20 * dt, 0.0, 1.0);
        g.LastDrinkBioSeconds = _world.Clock.BioSeconds;
        if (g.BodyTemperature > 0.4)
            g.BodyTemperature = Math.Max(0.4, g.BodyTemperature - 0.02 * dt);
    }

    private void ExecuteFeeding(GuardianIndividual g, double dt)
    {
        if (!WithinFeedingReach(g)) return;
        var toFood = new Vec2(g.TargetX - g.X, g.TargetZ - g.Z);
        if (toFood.LengthSq > 1e-8 && Math.Cos(Math.Atan2(toFood.Z, toFood.X) - g.Heading) < .75) return;

        var def = Def!;
        var diets = def.PreferredDiets;

        // 1. Check small fauna feeding
        if (diets.Contains("fauna:small"))
        {
            FaunaIndividual? prey = null;
            double reachSq = 1.2 * 1.2;
            foreach (var f in _world.Fauna.Items)
            {
                if (f.Grabbed) continue;
                if ((g.PositionXZ - f.PositionXZ).LengthSq <= reachSq)
                {
                    prey = f;
                    break;
                }
            }
            if (prey != null)
            {
                double energyBite = Math.Min(prey.Energy, 0.20 * dt);
                prey.Energy -= energyBite;
                g.Energy = Math.Min(def.EnergyMax, g.Energy + energyBite * 35.0);
                g.Hydration = Math.Clamp(g.Hydration + 0.015 * dt, 0.0, 1.0);
                g.LastConsumedDiet = "small fauna";

                if (prey.Energy <= 0.02)
                {
                    int preyCell = _world.Grid.NearestDomainCell(prey.PositionXZ);
                    if (preyCell >= 0)
                        _world.Fields.Nutrients[preyCell] = Math.Min(1.0, _world.Fields.Nutrients[preyCell] + 0.02);
                    _world.Litter.Deposit(prey.PositionXZ, fine: 0.01, coarse: 0.005, radius: 0.3);
                    _world.Fauna.Remove(prey.Id);
                }
                return;
            }
        }

        // 2. Check flora feeding (foliage, bark, fungus)
        bool wantsFoliage = diets.Contains("flora:foliage");
        bool wantsBark = diets.Contains("flora:bark");
        bool wantsFungus = diets.Contains("flora:fungus");
        if (wantsFoliage || wantsBark || wantsFungus)
        {
            FloraIndividual? plant = null;
            double plantReachSq = 1.4 * 1.4;
            foreach (var fl in _world.Flora.Items)
            {
                if (fl.Biomass < 0.05) continue;
                if ((g.PositionXZ - fl.Position).LengthSq <= plantReachSq)
                {
                    plant = fl;
                    break;
                }
            }
            if (plant != null)
            {
                var species = _world.Content.FloraById(plant.SpeciesId);
                bool isFungus = species?.Archetype == "fungus" || plant.SpeciesId.Contains("fung");
                g.LastConsumedDiet = isFungus ? "woodland fungi" : "tender foliage";

                if (plant.Biomass > 0.06)
                {
                    double bite = Math.Min(0.015 * dt, plant.Biomass * 0.08 * dt);
                    plant.Biomass -= bite;
                    g.Energy = Math.Min(def.EnergyMax, g.Energy + bite * 35.0);
                    g.Hydration = Math.Clamp(g.Hydration + 0.02 * dt, 0.0, 1.0);
                }

                if (plant.Fruiting && plant.FruitLoad > 0)
                {
                    double fruitBite = Math.Min(plant.FruitLoad, 0.02 * dt);
                    plant.FruitLoad -= fruitBite;
                    g.PendingSeeds.Add(new GuardianPendingSeed
                    {
                        SpeciesId = plant.SpeciesId,
                        ViableMass = fruitBite * 0.1,
                        BioSecondsUntilDeposit = 600.0
                    });
                }
                return;
            }

            if (wantsBark && _world.Props != null)
            {
                var log = _world.Props.LogAt(g.PositionXZ) ?? _world.Props.LogAt(new Vec2(g.TargetX, g.TargetZ));
                if (log != null || (_world.PilotTree != null && Vec2.Distance(g.PositionXZ, _world.PilotTree.Anchor) < _world.PilotTree.Def.TrunkRadius + 1.2))
                {
                    g.Energy = Math.Min(def.EnergyMax, g.Energy + 0.015 * dt * 25.0);
                    g.LastConsumedDiet = "tree bark";
                    return;
                }
            }
        }

        // 3. Biofilm in shallow shoreline water
        if (diets.Contains("biofilm") && _world.Water != null && _world.Water.OpenWaterDepth(g.PositionXZ) > 0.005)
        {
            int cellWater = _world.Grid.NearestDomainCell(g.PositionXZ);
            if (cellWater >= 0)
            {
                double extractedBio = _world.Litter.TakeDetritus(cellWater, 0.02 * dt);
                g.Energy = Math.Min(def.EnergyMax, g.Energy + Math.Max(extractedBio * 15.0, 0.015 * dt * 10.0));
                g.Hydration = Math.Clamp(g.Hydration + 0.04 * dt, 0.0, 1.0);
                g.LastConsumedDiet = "shoreline biofilm";
                return;
            }
        }

        // 4. Default / Litter: fallen fruit or forest detritus
        int cell = _world.Grid.NearestDomainCell(new Vec2(g.TargetX, g.TargetZ));
        if (cell < 0) return;

        g.Hydration = Math.Clamp(g.Hydration + 0.008 * dt, 0.0, 1.0);
        g.Comfort = Math.Clamp(g.Comfort - 0.005 * dt, 0.0, 1.0);

        double energyRoom = Math.Max(0, def.EnergyMax - g.Energy);
        double want = Math.Min(0.05 * dt, energyRoom / 25.0);
        var litter = _world.Litter;
        double fruitBefore = litter.FruitMass[cell];
        double consumed = 0;
        if (want > 0 && PrefersFruitFirst(def))
        {
            consumed = Math.Min(Math.Max(0, fruitBefore), want);
            litter.FruitMass[cell] = fruitBefore - consumed;
            fruitBefore = litter.FruitMass[cell] + consumed;
        }
        if (want - consumed > 0) consumed += litter.TakeDetritus(cell, want - consumed);
        double fruitEaten = Math.Max(0, fruitBefore - litter.FruitMass[cell]);
        if (consumed > 0)
        {
            g.Energy = Math.Min(def.EnergyMax, g.Energy + consumed * 25.0);
            g.LastConsumedDiet = fruitEaten > 0 ? "fallen fruit" : "forest detritus";

            string? seedSpecies = fruitEaten > 0 ? FindFruitSourceSpecies(_world.Grid.CellCenter(cell)) : null;
            if (seedSpecies != null)
            {
                g.PendingSeeds.Add(new GuardianPendingSeed
                {
                    SpeciesId = seedSpecies,
                    ViableMass = fruitEaten * 0.08,
                    BioSecondsUntilDeposit = 600.0
                });
            }

            _world.Litter.Deposit(g.PositionXZ, fine: 0.002, coarse: 0, radius: 0.3);
        }
        else
        {
            g.LastConsumedDiet = "forest detritus";
        }
    }

    private const double FruitSourceReach = 4.0;

    private static bool PrefersFruitFirst(GuardianDef def)
    {
        int fruit = -1, other = -1;
        for (int i = 0; i < def.PreferredDiets.Count; i++)
        {
            string d = def.PreferredDiets[i];
            if (fruit < 0 && d == "fruit") fruit = i;
            if (other < 0 && (d == "detritus" || d == "litter")) other = i;
        }
        return fruit >= 0 && (other < 0 || fruit < other);
    }

    /// <summary>Nearest fruiting flora to p within reach; ties by lowest EntityId. Null if none.</summary>
    private string? FindFruitSourceSpecies(Vec2 p)
    {
        var near = _world.Flora.ItemsNear(p, FruitSourceReach);
        FloraIndividual? best = null; double bestD = double.MaxValue;
        foreach (var f in near)
        {
            if (!f.Fruiting) continue;
            double d = Vec2.DistanceSq(p, f.Position);
            if (d > FruitSourceReach * FruitSourceReach) continue;
            if (best == null || d < bestD || (d == bestD && f.Id.Value < best.Id.Value)) { best = f; bestD = d; }
        }
        return best?.SpeciesId;
    }

    private void ExecuteBathing(GuardianIndividual g, double dt)
    {
        g.Comfort = Math.Clamp(g.Comfort + 0.12 * dt, 0.0, 1.0);
        g.LastBatheBioSeconds = _world.Clock.BioSeconds;
        if (g.BodyTemperature > 0.25)
            g.BodyTemperature = Math.Max(0.25, g.BodyTemperature - 0.05 * dt);
    }

    private void ExecuteDigging(GuardianIndividual g, double dt)
    {
        int cell = _world.Grid.NearestDomainCell(g.PositionXZ);
        if (cell >= 0)
        {
            double extracted = _world.Litter.TakeDetritus(cell, 0.01 * dt);
            if (extracted > 0)
                g.Energy = Math.Min(Def!.EnergyMax, g.Energy + extracted * 15.0);
            _world.Litter.Deposit(g.PositionXZ, fine: 0.004 * dt, coarse: 0.002 * dt, radius: 0.35);
            _world.Fields.Nutrients[cell] = Math.Min(1.0, _world.Fields.Nutrients[cell] + 0.004 * dt);
        }
        g.Comfort = Math.Clamp(g.Comfort - 0.01 * dt, 0.0, 1.0);
        g.Fatigue = MathD.Clamp01(g.Fatigue + 0.002 * (dt / 10.0));
    }

    private void ExecuteBasking(GuardianIndividual g, double dt)
    {
        double target = 0.55;
        if (Def?.BehaviorProfile != null)
        {
            target = Math.Max(0.55, 0.35 * Def.BehaviorProfile.BaskPriority + 0.05);
        }
        if (g.BodyTemperature < target)
            g.BodyTemperature = Math.Min(target, g.BodyTemperature + 0.08 * dt);
        else if (g.BodyTemperature > target)
            g.BodyTemperature = Math.Max(target, g.BodyTemperature - 0.02 * dt);
    }

    private void ExecutePlaying(GuardianIndividual g, double dt)
    {
        g.Arousal = Math.Clamp(g.Arousal - 0.08 * dt, 0.0, 1.0);
        g.Comfort = Math.Clamp(g.Comfort + 0.02 * dt, 0.0, 1.0);
        g.LastPlayBioSeconds = _world.Clock.BioSeconds;
    }

    private void ExecuteScentMarking(GuardianIndividual g, double dt)
    {
        g.LastScentMarkBioSeconds = _world.Clock.BioSeconds;
        _world.Ecology.AddWasteNutrients(g.PositionXZ, 0.005 * dt);
        _world.Litter.Deposit(g.PositionXZ, fine: 0.001 * dt, coarse: 0, radius: 0.2);
    }

    private bool WithinFeedingReach(GuardianIndividual g)
    {
        double distSq = (g.Position - g.TargetPosition).LengthSq;
        double baseReach = 0.5 * GuardianDef.BodySizeMultiplier * Math.Clamp(g.Scale, 0.5, 1.5);
        if (distSq <= baseReach * baseReach) return true;

        var def = Def;
        if (def != null)
        {
            if (def.PreferredDiets.Any(d => d.StartsWith("flora:")) && distSq <= 1.4 * 1.4)
                return true;
            if (def.PreferredDiets.Contains("fauna:small") && distSq <= 1.2 * 1.2)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Biological updates: metabolism, energy burn, and seed digestion / excretion.
    /// Runs on biological time (scaled by BioAcceleration).
    /// </summary>
    public void StepMetabolism(double bioDt)
    {
        var g = _world.Guardian;
        if (g == null || Def == null) return;
        double daylight01 = Math.Clamp(Math.Sin((_world.Clock.BioDays % 1.0) * Math.PI), 0.0, 1.0);
        double nocturnalBias = Def.BehaviorProfile?.NocturnalBias ?? 0.3;
        GuardianNeeds.StepMetabolism(g, Def, bioDt, _world, daylight01, nocturnalBias);
    }

    private static double WrapAngle(double a)
    {
        while (a > Math.PI) a -= Math.PI * 2.0;
        while (a < -Math.PI) a += Math.PI * 2.0;
        return a;
    }

    private static double TurnTowards(double current, double target, double maxTurn)
    {
        double diff = WrapAngle(target - current);
        return WrapAngle(current + Math.Clamp(diff, -maxTurn, maxTurn));
    }
}
