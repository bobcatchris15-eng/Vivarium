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
    public static void StepMetabolism(GuardianIndividual g, GuardianDef def, double bioDt, VivariumWorld? world, double daylight01 = 0.7, double nocturnalBias = 0.3)
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

        // 5. Circadian rhythm modulation: nocturnalBias vs daylight01 modulates active fatigue and target arousal
        double fatigueMultiplier = 1.0;
        double targetArousal = 0.3;
        if (nocturnalBias > 0.55)
        {
            if (daylight01 > 0.6)
            {
                fatigueMultiplier = 1.35; // accumulates 35% faster in bright daylight
                targetArousal = 0.18;     // arousal is lower during day
            }
            else if (daylight01 < 0.45)
            {
                fatigueMultiplier = 0.75; // accumulates slower at dusk/night
                targetArousal = 0.48;     // arousal/foraging drive rises
            }
        }
        else if (nocturnalBias < 0.45)
        {
            if (daylight01 < 0.35)
            {
                fatigueMultiplier = 1.35; // accumulates faster at night
                targetArousal = 0.18;     // lower arousal at night
            }
            else if (daylight01 > 0.45)
            {
                fatigueMultiplier = 0.80; // accumulates slower in daylight
                targetArousal = 0.45;     // energized during daylight
            }
        }

        bool isActive = g.Activity is not (GuardianActivity.Sleep or GuardianActivity.Rest or GuardianActivity.Settle);
        if (isActive)
        {
            double basalFatigue = 0.0006 * fatigueMultiplier * bioDt / SimClock.DefaultSimSecondsPerRealSecond;
            g.Fatigue = Math.Clamp(g.Fatigue + basalFatigue, 0.0, 1.0);
        }

        // Arousal: drifts toward targetArousal
        double arousalDrift = 0.0015 * bioDt / SimClock.DefaultSimSecondsPerRealSecond;
        if (g.Arousal > targetArousal)
            g.Arousal = Math.Max(targetArousal, g.Arousal - arousalDrift);
        else if (g.Arousal < targetArousal)
            g.Arousal = Math.Min(targetArousal, g.Arousal + arousalDrift);
        g.Arousal = Math.Clamp(g.Arousal, 0.0, 1.0);






        g.Arousal = Math.Clamp(g.Arousal, 0.0, 1.0);

        // 6. Alertness: decays toward baseline 0.2 at 0.005 * bioDt / SimClock.DefaultSimSecondsPerRealSecond
        double alertDrift = 0.005 * bioDt / SimClock.DefaultSimSecondsPerRealSecond;
        if (g.Alertness > 0.2)
            g.Alertness = Math.Max(0.2, g.Alertness - alertDrift);
        else if (g.Alertness < 0.2)
            g.Alertness = Math.Min(0.2, g.Alertness + alertDrift);
        g.Alertness = Math.Clamp(g.Alertness, 0.0, 1.0);

        // Clamping of all physiological needs
        g.Energy = Math.Clamp(g.Energy, 0.0, def.EnergyMax);
        g.Hydration = Math.Clamp(g.Hydration, 0.0, 1.0);
        g.Fatigue = Math.Clamp(g.Fatigue, 0.0, 1.0);
        g.Comfort = Math.Clamp(g.Comfort, 0.0, 1.0);
        g.BodyTemperature = Math.Clamp(g.BodyTemperature, 0.0, 1.0);
        g.Arousal = Math.Clamp(g.Arousal, 0.0, 1.0);
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
