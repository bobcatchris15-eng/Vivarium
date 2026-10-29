using System;
using System.Collections.Generic;
using System.Linq;
using Vivarium.Sim.Core;
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
            VisualVariant = "default",
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

    private void SelectNextActivity(GuardianIndividual g, double dt)
    {
        var rng = Rng.Keyed(g.Seed, "guardian.activity", g.RngCounter);
        var def = Def!;
        var profile = def.BehaviorProfile ?? new GuardianBehaviorProfile();

        double fatigueScore = g.Fatigue;
        double hungerScore = (1.0 - (g.Energy / def.EnergyMax)) * profile.ForageWeight;
        double thirstScore = (1.0 - g.Hydration) * profile.DrinkFrequency;
        double discomfortScore = 1.0 - g.Comfort;

        if (fatigueScore > .65 || hungerScore > .35 || thirstScore > .35 || discomfortScore > .4)
            g.ClimbRoute.Clear();

        // 1. Fatigue drive: needs Rest / ReturnHome / Settle
        if (fatigueScore > 0.65)
        {
            var restSite = Habitat?.NearestRestSite(g.Position) ?? new Vec3(0, _world.SurfaceHeight(Vec2.Zero), 0);
            double distToRest = Vec2.Distance(g.PositionXZ, new Vec2(restSite.X, restSite.Z));
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
            }
            return;
        }

        // 2. Thirst drive: needs Drink
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
                return;
            }
        }

        // 3. Hunger drive: needs Food / Forage
        if (hungerScore > 0.35)
        {
            int targetCell = FindRichestFoodCell(g.PositionXZ, 3.5);
            if (targetCell >= 0)
            {
                var targetXZ = _world.Grid.CellCenter(targetCell);
                double targetY = _world.GroundHeight(targetXZ);
                g.TargetPosition = new Vec3(targetXZ.X, targetY, targetXZ.Z);
                g.Activity = WithinFeedingReach(g) ? GuardianActivity.Feed : GuardianActivity.Forage;
                g.ActivityDuration = rng.Range(15.0, 30.0);
                return;
            }
            else if (Habitat != null)
            {
                var forageXZ = Habitat.SelectForageTarget(g.Seed, g.RngCounter);
                g.TargetPosition = new Vec3(forageXZ.X, _world.GroundHeight(forageXZ), forageXZ.Z);
                g.Activity = GuardianActivity.Forage;
                g.ActivityDuration = rng.Range(15.0, 30.0);
                return;
            }
        }

        // 4. Discomfort: if > 0.65 and shallow water nearby -> Bathe; else if > 0.4 -> Groom
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

        // 5. Thermoregulation: if BodyTemperature < 0.35 * BaskPriority -> Bask at sunlit point
        if (g.BodyTemperature < 0.35 * profile.BaskPriority)
        {
            var sunlitXZ = Habitat?.NearestSunlitPoint(g.PositionXZ) ?? g.PositionXZ;
            double sunlitY = _world.GroundHeight(sunlitXZ);
            g.TargetPosition = new Vec3(sunlitXZ.X, sunlitY, sunlitXZ.Z);
            double dist = Vec2.Distance(g.PositionXZ, sunlitXZ);
            g.Activity = GuardianActivity.Bask;
            g.ActivityDuration = dist <= 0.4 ? rng.Range(10.0, 20.0) : rng.Range(15.0, 30.0);
            return;
        }

        // 6. Play: if Arousal > PlayThreshold and basic needs met -> Play
        if (g.Arousal > profile.PlayThreshold)
        {
            g.Activity = GuardianActivity.Play;
            double playDist = rng.Range(0.5, 1.5);
            double playAngle = rng.Range(0, Math.PI * 2);
            var playXZ = _world.Domain.ClampInside(g.PositionXZ + new Vec2(Math.Cos(playAngle), Math.Sin(playAngle)) * playDist, 0.4);
            g.TargetPosition = new Vec3(playXZ.X, _world.GroundHeight(playXZ), playXZ.Z);
            g.ActivityDuration = rng.Range(5.0, 12.0);
            return;
        }

        // With needs satisfied, keep one island destination until arrival.
        // Idle rolls used to repeatedly replace short walks with stationary clips.
        // An unmet need with no available resource must not reroll the route
        // every tick. Keep traveling until arrival or the journey timeout.
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
                _world.PilotTree?.BlocksTrunk(point, .35) == true) continue;
            int cell = _world.Grid.NearestDomainCell(point);
            if (cell < 0 || _world.Water.OpenWaterDepth(cell) > .08) continue;
            g.TargetPosition = new Vec3(point.X, _world.GroundHeight(point), point.Z);
            g.Activity = GuardianActivity.Explore;
            // Allow the journey, including trunk detours, to finish. This also
            // supplies a bounded escape if terrain prevents progress.
            g.ActivityDuration = 10 + Vec2.Distance(g.PositionXZ, point) /
                Math.Max(.01, Def!.WalkSpeed * GuardianDef.BodySizeMultiplier) * 2;
            return;
        }
        // No suitable destination this pass; retry without a long idle cycle.
        g.Activity = GuardianActivity.Rest;
        g.TargetPosition = g.Position;
        g.ActivityDuration = .5;
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

    private void ExecuteLocomotion(GuardianIndividual g, double dt)
    {
        var def = Def!;
        var currentXZ = g.PositionXZ;
        var targetXZ = new Vec2(g.TargetX, g.TargetZ);
        double dist = Vec2.Distance(currentXZ, targetXZ);

        if (g.Activity == GuardianActivity.Forage && WithinFeedingReach(g))
        {
            g.Activity = GuardianActivity.Feed;
            g.ActivityDuration = 10.0;
        }

        if (g.Activity == GuardianActivity.Feed)
        {
            if (WithinFeedingReach(g))
            {
                // Plant the feet while eating; turn to the selected patch instead
                // of sliding through a feeding animation toward its cell center.
                if (dist > 1e-6)
                    g.Heading = TurnTowards(g.Heading, Math.Atan2(targetXZ.Z - currentXZ.Z, targetXZ.X - currentXZ.X), 2.5 * dt);
                return;
            }
            // Old saves and displaced targets must approach before consuming.
            g.Activity = GuardianActivity.Forage;
        }

        // Approach transitions for water activities: within reach (0.4m), plant feet
        if ((g.Activity == GuardianActivity.Drink || g.Activity == GuardianActivity.Bathe) && dist <= 0.4)
        {
            if (dist > 1e-6)
                g.Heading = TurnTowards(g.Heading, Math.Atan2(targetXZ.Z - currentXZ.Z, targetXZ.X - currentXZ.X), 2.5 * dt);
            return;
        }

        // Approached target for other stationary-at-destination activities
        if ((g.Activity == GuardianActivity.Bask || g.Activity == GuardianActivity.Dig || g.Activity == GuardianActivity.ScentMark) && dist <= 0.4)
        {
            if (dist > 1e-6)
                g.Heading = TurnTowards(g.Heading, Math.Atan2(targetXZ.Z - currentXZ.Z, targetXZ.X - currentXZ.X), 2.5 * dt);
            return;
        }

        // Strictly stationary activities: stay planted
        if (g.Activity is GuardianActivity.Rest or GuardianActivity.Sleep or GuardianActivity.Settle 
            or GuardianActivity.Stretch or GuardianActivity.Alert or GuardianActivity.ShakeOff or GuardianActivity.Groom)
        {
            return;
        }

        if (dist > 0.05)
        {
            // Turn towards target
            var dir = (targetXZ - currentXZ).Normalized();
            double targetHeading = Math.Atan2(dir.Z, dir.X);
            g.Heading = TurnTowards(g.Heading, targetHeading, 2.5 * dt);

            // Step forward
            double speed = g.Activity == GuardianActivity.Retreat ? def.RunSpeed : def.WalkSpeed;
            double step = Math.Min(dist, speed * GuardianDef.BodySizeMultiplier * dt);
            var nextXZ = currentXZ + dir * step;

            // Island boundary containment
            nextXZ = _world.Domain.ClampInside(nextXZ, 0.2);

            // Route tangentially around the trunk when a direct waypoint crosses it.
            // Merely pushing back to its rim traps the animal against the same obstacle.
            // Trunk collision avoidance
            if (_world.PilotTree != null && _world.PilotTree.BlocksTrunk(nextXZ, 0.35))
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

            // Ground/root contact height
            double groundY = _world.GroundHeight(nextXZ);
            if (_world.PilotTree != null)
            {
                double rootY = _world.PilotTree.RootSurfaceHeight(nextXZ);
                if (!double.IsNaN(rootY) && rootY > groundY) groundY = rootY;
            }
            g.Y = groundY;

            // Locomotion fatigue accumulation
            g.Fatigue = MathD.Clamp01(g.Fatigue + 0.003 * (dt / 10.0));

            // When approaching water for Drink or Bathe, transition when within reach (0.4m)
            double remainingDist = Vec2.Distance(nextXZ, targetXZ);
            if ((g.Activity == GuardianActivity.Drink || g.Activity == GuardianActivity.Bathe) && remainingDist <= 0.4)
            {
                return;
            }
        }
        else
        {
            // Reached target
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
            else if (g.Activity == GuardianActivity.Explore || g.Activity == GuardianActivity.Investigate)
            {
                if (g.ClimbRoute.Count > 1 && Def!.BehaviorProfile?.CanClimbTrunk == true)
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
        
        // The approached patch is authoritative, including when it lies across
        // a grid boundary. Do not eat an unrelated cell under the body instead.
        int cell = _world.Grid.NearestDomainCell(new Vec2(g.TargetX, g.TargetZ));
        if (cell < 0) return;

        g.Hydration = Math.Clamp(g.Hydration + 0.008 * dt, 0.0, 1.0);
        g.Comfort = Math.Clamp(g.Comfort - 0.005 * dt, 0.0, 1.0);

        double energyRoom = Math.Max(0, Def!.EnergyMax - g.Energy);
        double want = Math.Min(0.05 * dt, energyRoom / 25.0);
        var litter = _world.Litter;
        double fruitBefore = litter.FruitMass[cell];
        double consumed = 0;
        if (want > 0 && PrefersFruitFirst(Def!))
        {
            // Diet lists fruit ahead of detritus/litter: eat fruit first.
            consumed = Math.Min(Math.Max(0, fruitBefore), want);
            litter.FruitMass[cell] = fruitBefore - consumed;
            fruitBefore = litter.FruitMass[cell] + consumed;
        }
        if (want - consumed > 0) consumed += litter.TakeDetritus(cell, want - consumed);
        double fruitEaten = Math.Max(0, fruitBefore - litter.FruitMass[cell]);
        if (consumed > 0)
        {
            g.Energy = Math.Min(Def!.EnergyMax, g.Energy + consumed * 25.0);

            // Only fruit carries seeds, and only of the species that bore it.
            string? seedSpecies = fruitEaten > 0 ? FindFruitSourceSpecies(_world.Grid.CellCenter(cell)) : null;
            if (seedSpecies != null)
            {
                g.PendingSeeds.Add(new GuardianPendingSeed
                {
                    SpeciesId = seedSpecies,
                    ViableMass = fruitEaten * 0.08,
                    BioSecondsUntilDeposit = 600.0 // Deposited after ~10 bio-minutes
                });
            }

            // Soil disturbance
            _world.Litter.Deposit(g.PositionXZ, fine: 0.002, coarse: 0, radius: 0.3);
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

    private static bool WithinFeedingReach(GuardianIndividual g)
    {
        double reach = .4 * GuardianDef.BodySizeMultiplier * Math.Clamp(g.Scale, .5, 1.5);
        double dx = g.X - g.TargetX, dy = g.Y - g.TargetY, dz = g.Z - g.TargetZ;
        return dx * dx + dy * dy + dz * dz <= reach * reach;
    }

    /// <summary>
    /// Biological updates: metabolism, energy burn, and seed digestion / excretion.
    /// Runs on biological time (scaled by BioAcceleration).
    /// </summary>
    public void StepMetabolism(double bioDt)
    {
        var g = _world.Guardian;
        if (g == null || Def == null) return;
        GuardianNeeds.StepMetabolism(g, Def, bioDt, _world);
    }

    private static double TurnTowards(double current, double target, double maxTurn)
    {
        double diff = (target - current + Math.PI) % (Math.PI * 2) - Math.PI;
        if (diff < -Math.PI) diff += Math.PI * 2;
        return current + Math.Clamp(diff, -maxTurn, maxTurn);
    }
}
