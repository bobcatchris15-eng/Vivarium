using System;
using Vivarium.Sim.Core;
using Vivarium.Sim.Time;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Guardians;

/// <summary>
/// Biological need modeling and metabolic updates for Guardian Animals.
/// Governs basal energy burn, hydration, comfort, body temperature, arousal, alertness,
/// and seed digestion/excretion into the world.
/// </summary>
public static class GuardianNeeds
{
    public static void StepMetabolism(GuardianIndividual g, GuardianDef def, double bioDt, VivariumWorld? world)
    {
        if (g == null || def == null) return;

        // 1. Basal energy burn
        double energyBurn = def.BaseMetabolismRate * bioDt / SimClock.DefaultSimSecondsPerRealSecond;
        g.Energy = Math.Max(0, g.Energy - energyBurn);

        // 2. Hydration decay: basal rate 0.005 * bioDt / SimClock.DefaultSimSecondsPerRealSecond
        double basalHydration = 0.005 * bioDt / SimClock.DefaultSimSecondsPerRealSecond;
        g.Hydration = Math.Clamp(g.Hydration - basalHydration, 0.0, 1.0);

        // 3. Comfort decay: accumulates dirt/itch over time: 0.002 * bioDt / SimClock.DefaultSimSecondsPerRealSecond
        double comfortLoss = 0.002 * bioDt / SimClock.DefaultSimSecondsPerRealSecond;
        g.Comfort = Math.Clamp(g.Comfort - comfortLoss, 0.0, 1.0);

        // 4. BodyTemperature drift: drifts toward ambient (shaded=0.3, sun=0.7, water=0.25). Default ambient 0.5.
        // Drift rate: 0.003 * bioDt / SimClock.DefaultSimSecondsPerRealSecond
        double targetAmbient = 0.5;
        if (world != null)
        {
            if (world.Water != null && world.Water.OpenWaterDepth(g.PositionXZ) > 0.02)
            {
                targetAmbient = 0.25;
            }
            else
            {
                int cell = world.Grid != null ? world.Grid.NearestDomainCell(g.PositionXZ) : -1;
                double ambientLight = cell < 0 || world.Fields?.Light == null ? 0
                    : world.FloraSystem != null ? world.FloraSystem.EffectiveLightCell(cell)
                    : world.Fields.Light[cell];
                if (ambientLight > 0.45)
                {
                    targetAmbient = 0.7;
                }
                else
                {
                    targetAmbient = 0.3;
                }
            }
        }
        double tempDrift = 0.003 * bioDt / SimClock.DefaultSimSecondsPerRealSecond;
        if (g.BodyTemperature < targetAmbient)
            g.BodyTemperature = Math.Min(targetAmbient, g.BodyTemperature + tempDrift);
        else if (g.BodyTemperature > targetAmbient)
            g.BodyTemperature = Math.Max(targetAmbient, g.BodyTemperature - tempDrift);
        g.BodyTemperature = Math.Clamp(g.BodyTemperature, 0.0, 1.0);

        // 5. Arousal: decays slowly toward 0.3 at 0.001 * bioDt / SimClock.DefaultSimSecondsPerRealSecond
        double arousalDrift = 0.001 * bioDt / SimClock.DefaultSimSecondsPerRealSecond;
        if (g.Arousal > 0.3)
            g.Arousal = Math.Max(0.3, g.Arousal - arousalDrift);
        else if (g.Arousal < 0.3)
            g.Arousal = Math.Min(0.3, g.Arousal + arousalDrift);
        g.Arousal = Math.Clamp(g.Arousal, 0.0, 1.0);

        // 6. Alertness: decays toward baseline 0.2 at 0.005 * bioDt / SimClock.DefaultSimSecondsPerRealSecond
        double alertDrift = 0.005 * bioDt / SimClock.DefaultSimSecondsPerRealSecond;
        if (g.Alertness > 0.2)
            g.Alertness = Math.Max(0.2, g.Alertness - alertDrift);
        else if (g.Alertness < 0.2)
            g.Alertness = Math.Min(0.2, g.Alertness + alertDrift);
        g.Alertness = Math.Clamp(g.Alertness, 0.0, 1.0);

        // 7. Seed digestion and excretion: keep existing seed digestion logic
        if (g.PendingSeeds.Count > 0)
        {
            for (int i = g.PendingSeeds.Count - 1; i >= 0; i--)
            {
                var pending = g.PendingSeeds[i];
                pending.BioSecondsUntilDeposit -= bioDt;

                if (pending.BioSecondsUntilDeposit <= 0)
                {
                    // Excrete digested seeds into SeedBank
                    world?.SeedBank.Deposit(pending.SpeciesId, g.PositionXZ, pending.ViableMass, dormancy: 120.0);

                    // Excrete waste nutrients into soil
                    world?.Ecology.AddWasteNutrients(g.PositionXZ, pending.ViableMass * 1.5);

                    // Substrate disturbance mark
                    world?.Litter.Deposit(g.PositionXZ, fine: 0.003, coarse: 0.001, radius: 0.25);

                    g.PendingSeeds.RemoveAt(i);
                }
            }
        }
    }
}
