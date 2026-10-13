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
    public GuardianDef? Def { get; private set; }
    public GuardianHabitat? Habitat { get; private set; }

    public GuardianSystem(VivariumWorld world)
    {
        _world = world;
        RefreshDefinition();
    }

    public void RefreshDefinition()
    {
        if (_world.Guardian != null)
        {
            Def = GuardianCatalog.Find(_world.Guardian.ArchetypeId);
        }
        else if (_world.PilotTree != null && _world.Descriptor.GuardianId != "none")
        {
            Def = _world.Descriptor.GuardianId is "auto" or "" or null
                ? GuardianCatalog.ForPilotTree(_world.PilotTree.Def.Id)
                : GuardianCatalog.Find(_world.Descriptor.GuardianId);
        }
        else
        {
            Def = null;
        }

        if (_world.PilotTree != null)
        {
            Habitat = new GuardianHabitat(_world.PilotTree, _world.Domain);
        }
    }

    /// <summary>
    /// Creates or re-initializes the island's guardian deterministically.
    /// </summary>
    public GuardianIndividual? InitializeGuardian(string? archetypeOverride = null)
    {
        if (_world.PilotTree == null && archetypeOverride == null) return null;

        string targetArchetype = archetypeOverride
            ?? (_world.Descriptor.GuardianId is "auto" or "" or null
                ? GuardianCatalog.ForPilotTree(_world.PilotTree!.Def.Id)?.Id ?? GuardianCatalog.GreatGround
                : _world.Descriptor.GuardianId);

        if (targetArchetype == "none") return null;

        var def = GuardianCatalog.Find(targetArchetype);
        if (def == null) return null;

        Def = def;
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

        // Execute locomotion towards TargetPosition
        ExecuteLocomotion(g, dt);

        if (g.Activity == GuardianActivity.Feed)
        {
            ExecuteFeeding(g, dt);
        }

        g.ActivityDuration -= dt;
        if (g.ActivityDuration <= 0)
        {
            SelectNextActivity(g, dt);
        }
    }

    private void SelectNextActivity(GuardianIndividual g, double dt)
    {
        var rng = Rng.Keyed(g.Seed, "guardian.activity", g.RngCounter);
        var def = Def!;

        double hungerScore = 1.0 - (g.Energy / def.EnergyMax);
        double fatigueScore = g.Fatigue;

        // 1. Fatigue drive: needs Rest
        if (fatigueScore > 0.65)
        {
            g.Activity = GuardianActivity.ReturnHome;
            var restSite = Habitat?.NearestRestSite(g.Position) ?? new Vec3(0, _world.SurfaceHeight(Vec2.Zero), 0);
            g.TargetPosition = restSite;
            g.ActivityDuration = rng.Range(20.0, 45.0);
            return;
        }

        // 2. Hunger drive: needs Food/Forage
        if (hungerScore > 0.35)
        {
            int targetCell = FindRichestFoodCell(g.PositionXZ, 3.5);
            if (targetCell >= 0)
            {
                var targetXZ = _world.Grid.CellCenter(targetCell);
                double targetY = _world.GroundHeight(targetXZ);
                g.TargetPosition = new Vec3(targetXZ.X, targetY, targetXZ.Z);
                g.Activity = Vec2.Distance(g.PositionXZ, targetXZ) < 0.4 ? GuardianActivity.Feed : GuardianActivity.Forage;
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

        // 3. Low arousal / idle: Groom or Explore
        double roll = rng.NextDouble();
        if (roll < 0.15)
        {
            g.Activity = GuardianActivity.Groom;
            g.TargetPosition = g.Position;
            g.ActivityDuration = rng.Range(3.0, 6.0);
        }
        else if (roll < 0.90)
        {
            g.Activity = GuardianActivity.Explore;
            double wanderDist = rng.Range(1.0, 2.5);
            double wanderAngle = rng.Range(0, Math.PI * 2);
            var destXZ = _world.Domain.ClampInside(g.PositionXZ + new Vec2(Math.Cos(wanderAngle), Math.Sin(wanderAngle)) * wanderDist, 0.4);
            g.TargetPosition = new Vec3(destXZ.X, _world.GroundHeight(destXZ), destXZ.Z);
            g.ActivityDuration = rng.Range(15.0, 35.0);
        }
        else
        {
            g.Activity = GuardianActivity.Rest;
            g.TargetPosition = g.Position;
            g.ActivityDuration = rng.Range(4.0, 9.0);
        }
    }

    private int FindRichestFoodCell(Vec2 current, double radius)
    {
        var cells = _world.Grid.CellsInRadius(current, radius);
        int bestCell = -1;
        double bestFood = 0.001;

        foreach (int cell in cells)
        {
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

        if (dist > 0.05 && g.Activity != GuardianActivity.Rest && g.Activity != GuardianActivity.Groom)
        {
            // Turn towards target
            var dir = (targetXZ - currentXZ).Normalized();
            double targetHeading = Math.Atan2(dir.Z, dir.X);
            g.Heading = TurnTowards(g.Heading, targetHeading, 2.5 * dt);

            // Step forward
            double speed = g.Activity == GuardianActivity.Retreat ? def.RunSpeed : def.WalkSpeed;
            double step = Math.Min(dist, speed * dt);
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
        }
        else
        {
            // Reached target
            if (g.Activity == GuardianActivity.Forage)
            {
                g.Activity = GuardianActivity.Feed;
                g.ActivityDuration = 10.0;
            }
            else if (g.Activity == GuardianActivity.ReturnHome)
            {
                g.Activity = GuardianActivity.Rest;
                g.ActivityDuration = 30.0;
            }

            if (g.Activity == GuardianActivity.Explore)
            {
                g.Activity = GuardianActivity.Groom;
                g.ActivityDuration = 2.0;
            }

            // Resting recovers fatigue
            if (g.Activity == GuardianActivity.Rest)
            {
                g.Fatigue = MathD.Clamp01(g.Fatigue - 0.015 * (dt / 10.0));
            }
        }
    }

    private void ExecuteFeeding(GuardianIndividual g, double dt)
    {
        int cell = _world.Grid.NearestDomainCell(g.PositionXZ);
        if (cell < 0) return;

        double consumed = _world.Litter.TakeDetritus(cell, 0.05 * dt);
        if (consumed > 0)
        {
            g.Energy = Math.Min(Def!.EnergyMax, g.Energy + consumed * 25.0);

            // Queue seed dispersal event: fruit consumption carries seeds
            string seedSpecies = "shadebell";
            g.PendingSeeds.Add(new GuardianPendingSeed
            {
                SpeciesId = seedSpecies,
                ViableMass = consumed * 0.08,
                BioSecondsUntilDeposit = 600.0 // Deposited after ~10 bio-minutes
            });

            // Soil disturbance
            _world.Litter.Deposit(g.PositionXZ, fine: 0.002, coarse: 0, radius: 0.3);
        }
    }

    /// <summary>
    /// Biological updates: metabolism, energy burn, and seed digestion / excretion.
    /// Runs on biological time (scaled by BioAcceleration).
    /// </summary>
    public void StepMetabolism(double bioDt)
    {
        var g = _world.Guardian;
        if (g == null || Def == null) return;

        // Base metabolic energy consumption
        double energyBurn = Def.BaseMetabolismRate * bioDt;
        g.Energy = Math.Max(0, g.Energy - energyBurn);

        // Digestion and seed excretion
        if (g.PendingSeeds.Count > 0)
        {
            for (int i = g.PendingSeeds.Count - 1; i >= 0; i--)
            {
                var pending = g.PendingSeeds[i];
                pending.BioSecondsUntilDeposit -= bioDt;

                if (pending.BioSecondsUntilDeposit <= 0)
                {
                    // Excrete digested seeds into SeedBank
                    _world.SeedBank.Deposit(pending.SpeciesId, g.PositionXZ, pending.ViableMass, dormancy: 120.0);

                    // Excrete waste nutrients into soil
                    _world.Ecology.AddWasteNutrients(g.PositionXZ, pending.ViableMass * 1.5);

                    // Substrate disturbance mark
                    _world.Litter.Deposit(g.PositionXZ, fine: 0.003, coarse: 0.001, radius: 0.25);

                    g.PendingSeeds.RemoveAt(i);
                }
            }
        }
    }

    private static double TurnTowards(double current, double target, double maxTurn)
    {
        double diff = (target - current + Math.PI) % (Math.PI * 2) - Math.PI;
        if (diff < -Math.PI) diff += Math.PI * 2;
        return current + Math.Clamp(diff, -maxTurn, maxTurn);
    }
}
