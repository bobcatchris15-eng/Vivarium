"""Generates the fauna species library (game/content/fauna/*.json). Editable source for bulk tuning."""
import json, os

ROOT = os.path.join(os.path.dirname(__file__), "..", "..", "game", "content")
ALL_TRAITS = ["size", "ornament_density", "hue_shift", "pattern_strength", "appendage_length", "metabolic_efficiency"]

def P(o, t): return {"optimum": o, "tolerance": t}

fauna = [
 dict(id="springtail", name="Springtail", medium="terrestrial",
  role="Detritivore of moist litter; recycles dead plant matter.",
  description="Tiny six-legged hexapods that graze fungal films on decaying matter and flick away with a spring-loaded tail.",
  habitat=dict(moisture=P(0.7, 0.25), substrates={"soil": 1.0, "wood": 1.0, "gravel": 0.6, "rock": 0.5}, minWaterDepth=0, maxWaterDepth=0.006, minSuitability=0.1),
  movement=dict(speedPerHour=1.6, turnRatePerMinute=3.0, wander=0.7, habitatSeek=0.6, senseRadius=0.3),
  diet=[dict(resource="detritus", ratePerHour=0.0025, efficiency=12)],
  metabolism=dict(basalPerHour=0.012, maxEnergy=1.0, initialEnergy=0.7, hungerThreshold=0.85, wasteFraction=0.3),
  lifecycle=dict(maturityDays=6, lifespanDays=30, lifespanVariance=0.2, dailyMortality=0.01),
  reproduction=dict(mode="sexual", minEnergy=0.6, cost=0.25, clutchMin=1, clutchMax=3, cooldownDays=3, mateRadius=0.4, offspringEnergy=0.45, maxLocalDensity=14, populationCap=450),
  body=dict(sizeMin=0.0015, sizeMax=0.003, visualScale=7, massAtMid=0.004, detritusOnDeath=1.0),
  genetics=dict(traits=ALL_TRAITS, mutationMagnitude=0.15, initialVariance=0.08),
  visual=dict(model="springtail", baseColor=[0.88, 0.64, 0.30], ornamentColor=[0.32, 0.22, 0.58]),
  behaviors=[]),
 dict(id="shrimp", name="Cherry Shrimp", medium="aquatic",
  role="Aquatic grazer of biofilm and detritus.",
  description="Small freshwater shrimp that pick biofilm from every submerged surface; colour varies strongly between lines.",
  habitat=dict(moisture=P(1.0, 0.5), substrates={"soil": 1.0, "gravel": 1.0, "rock": 1.0, "wood": 1.0, "water": 1.0}, minWaterDepth=0.025, maxWaterDepth=1.0, minSuitability=0.1),
  movement=dict(speedPerHour=1.2, turnRatePerMinute=2.0, wander=0.5, habitatSeek=0.6, senseRadius=0.35),
  diet=[dict(resource="biofilm", ratePerHour=0.004, efficiency=7), dict(resource="detritus", ratePerHour=0.002, efficiency=5)],
  metabolism=dict(basalPerHour=0.008, maxEnergy=1.0, initialEnergy=0.7, hungerThreshold=0.8, wasteFraction=0.3),
  lifecycle=dict(maturityDays=18, lifespanDays=110, lifespanVariance=0.15, dailyMortality=0.004),
  reproduction=dict(mode="sexual", minEnergy=0.65, cost=0.35, clutchMin=2, clutchMax=5, cooldownDays=12, mateRadius=0.6, offspringEnergy=0.5, maxLocalDensity=16, populationCap=160),
  body=dict(sizeMin=0.012, sizeMax=0.026, visualScale=1.4, massAtMid=0.02, detritusOnDeath=1.0),
  genetics=dict(traits=ALL_TRAITS, mutationMagnitude=0.12, initialVariance=0.08),
  visual=dict(model="shrimp", baseColor=[0.92, 0.22, 0.16], ornamentColor=[0.98, 0.92, 0.86]),
  behaviors=[]),
 dict(id="triops", name="Triops", medium="aquatic",
  role="Fast-living shallow-water forager; boom-and-bust detritivore.",
  description="Shield-backed 'tadpole shrimp' of shallow pools: short lives, rapid asexual breeding, voracious foraging.",
  habitat=dict(moisture=P(1.0, 0.5), substrates={"soil": 1.0, "gravel": 1.0, "rock": 0.6, "wood": 0.5, "water": 1.0}, minWaterDepth=0.015, maxWaterDepth=0.6, minSuitability=0.1),
  movement=dict(speedPerHour=1.8, turnRatePerMinute=1.6, wander=0.6, habitatSeek=0.7, senseRadius=0.35),
  diet=[dict(resource="detritus", ratePerHour=0.006, efficiency=6), dict(resource="biofilm", ratePerHour=0.004, efficiency=6)],
  metabolism=dict(basalPerHour=0.02, maxEnergy=1.0, initialEnergy=0.7, hungerThreshold=0.85, wasteFraction=0.35),
  lifecycle=dict(maturityDays=5, lifespanDays=28, lifespanVariance=0.2, dailyMortality=0.01),
  reproduction=dict(mode="asexual", minEnergy=0.6, cost=0.3, clutchMin=2, clutchMax=4, cooldownDays=3, mateRadius=0, offspringEnergy=0.5, maxLocalDensity=8, populationCap=60),
  body=dict(sizeMin=0.012, sizeMax=0.03, visualScale=1.2, massAtMid=0.03, detritusOnDeath=1.0),
  genetics=dict(traits=["size", "pattern_strength", "hue_shift", "appendage_length", "metabolic_efficiency"], mutationMagnitude=0.14, initialVariance=0.08),
  visual=dict(model="triops", baseColor=[0.58, 0.64, 0.36], ornamentColor=[0.22, 0.26, 0.16]),
  behaviors=[]),
 dict(id="microminnow", name="Microminnow", medium="aquatic",
  role="Schooling plankton feeder of open water.",
  description="Among the smallest fish: glittering schools that drift through the pond feeding on plankton.",
  habitat=dict(moisture=P(1.0, 0.5), substrates={"soil": 1.0, "gravel": 1.0, "rock": 1.0, "wood": 1.0, "water": 1.0}, minWaterDepth=0.05, maxWaterDepth=1.5, minSuitability=0.1),
  movement=dict(speedPerHour=2.6, turnRatePerMinute=3.0, wander=0.4, habitatSeek=0.5, senseRadius=0.5),
  diet=[dict(resource="plankton", ratePerHour=0.005, efficiency=7), dict(resource="biofilm", ratePerHour=0.001, efficiency=3)],
  metabolism=dict(basalPerHour=0.01, maxEnergy=1.0, initialEnergy=0.75, hungerThreshold=0.8, wasteFraction=0.3),
  lifecycle=dict(maturityDays=25, lifespanDays=140, lifespanVariance=0.15, dailyMortality=0.003),
  reproduction=dict(mode="sexual", minEnergy=0.7, cost=0.35, clutchMin=2, clutchMax=6, cooldownDays=10, mateRadius=0.6, offspringEnergy=0.45, maxLocalDensity=80, populationCap=90),
  body=dict(sizeMin=0.010, sizeMax=0.020, visualScale=1.5, massAtMid=0.02, detritusOnDeath=1.0),
  genetics=dict(traits=ALL_TRAITS, mutationMagnitude=0.12, initialVariance=0.08),
  visual=dict(model="minnow", baseColor=[0.30, 0.74, 0.88], ornamentColor=[0.98, 0.36, 0.24]),
  behaviors=["schooling"],
  schooling=dict(radius=0.6, cohesion=1.0, alignment=1.2, separation=1.5, separationDistance=0.05)),
]

if __name__ == "__main__":
    for f in fauna:
        with open(os.path.join(ROOT, "fauna", f["id"] + ".json"), "w", newline="\n") as fh:
            json.dump(f, fh, indent=2); fh.write("\n")
    print(f"wrote {len(fauna)} fauna species")
