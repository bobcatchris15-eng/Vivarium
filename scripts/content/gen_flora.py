"""Generates the flora species library and interaction matrix (game/content/flora/*.json).
Kept as the editable source for bulk tuning; the JSON files are the runtime content."""
import json, os

ROOT = os.path.join(os.path.dirname(__file__), "..", "..", "game", "content")

def P(o, t): return {"optimum": o, "tolerance": t}

flora = [
 dict(id="carpet_moss", name="Carpet Moss", archetype="moss", role="Groundcover colonist of moist, shaded soil; spreads laterally in mats.",
  description="A bright feather-moss forming continuous carpets. Avoids fresh bark, which sheds before it can anchor.",
  habitat=dict(substrates={"soil": 1.0, "wood": 0.8, "rock": 0.35, "gravel": 0.2}, refuseSubstrates=["water"], refuseTags=["bark_fresh"],
               moisture=P(0.68, 0.2), light=P(0.45, 0.3), nutrients=P(0.25, 0.3), maxWaterDepth=0.0, minSuitability=0.18),
  growth=dict(ratePerDay=0.35, maxBiomass=1.0, initialBiomass=0.05, declinePerDay=0.25, maturityDays=6, lifespanDays=150, nutrientPerBiomass=0.2, radiusAtMax=0.34, minRadius=0.05),
  spread=dict(intervalDays=4, radius=0.45, propagules=2, minBiomassFraction=0.5),
  competition=dict(radius=0.35, crowdingLimit=2.4, sensitivity=0.8),
  proximity=[dict(target="feature:log", radius=0.6, bonus=0.15)], litterFraction=0.8, grazingValue=0.0,
  visual=dict(shape="carpet", color=[0.30, 0.66, 0.20], color2=[0.52, 0.78, 0.24], colorVariance=0.06, height=0.025), tags=["moss", "groundcover"]),
 dict(id="cushion_moss", name="Cushion Moss", archetype="moss", role="Slow clumping moss of stones and thin soil.",
  description="Dense rounded cushions that grow slowly but persist for years on rock and gravel.",
  habitat=dict(substrates={"rock": 0.95, "soil": 0.55, "gravel": 0.5, "wood": 0.5}, refuseSubstrates=["water"], refuseTags=[],
               moisture=P(0.55, 0.25), light=P(0.55, 0.3), nutrients=P(0.15, 0.35), maxWaterDepth=0.0, minSuitability=0.15),
  growth=dict(ratePerDay=0.16, maxBiomass=0.6, initialBiomass=0.04, declinePerDay=0.1, maturityDays=14, lifespanDays=320, nutrientPerBiomass=0.15, radiusAtMax=0.14, minRadius=0.04),
  spread=dict(intervalDays=8, radius=0.3, propagules=1, minBiomassFraction=0.6),
  competition=dict(radius=0.25, crowdingLimit=2.0, sensitivity=0.6),
  proximity=[dict(target="feature:rock", radius=0.3, bonus=0.12)], litterFraction=0.8, grazingValue=0.0,
  visual=dict(shape="cushion", color=[0.22, 0.52, 0.18], color2=[0.62, 0.74, 0.20], colorVariance=0.05, height=0.06), tags=["moss"]),
 dict(id="wetbank_moss", name="Bank Moss", archetype="moss", role="Saturation specialist of stream banks and pond margins.",
  description="A glossy emerald moss that needs near-saturated ground; tolerates splash but not submersion.",
  habitat=dict(substrates={"soil": 1.0, "rock": 0.8, "wood": 0.6, "gravel": 0.5}, refuseSubstrates=[], refuseTags=[],
               moisture=P(0.95, 0.12), light=P(0.5, 0.35), nutrients=P(0.35, 0.35), maxWaterDepth=0.012, hardMinMoisture=0.55, minSuitability=0.18),
  growth=dict(ratePerDay=0.4, maxBiomass=0.9, initialBiomass=0.05, declinePerDay=0.35, maturityDays=5, lifespanDays=120, nutrientPerBiomass=0.2, radiusAtMax=0.28, minRadius=0.05),
  spread=dict(intervalDays=3, radius=0.4, propagules=2, minBiomassFraction=0.45),
  competition=dict(radius=0.3, crowdingLimit=2.4, sensitivity=0.8),
  proximity=[dict(target="feature:water", radius=0.5, bonus=0.25)], litterFraction=0.85, grazingValue=0.0,
  visual=dict(shape="carpet", color=[0.12, 0.60, 0.34], color2=[0.20, 0.80, 0.45], colorVariance=0.05, height=0.03), tags=["moss", "riparian"]),
 dict(id="crust_lichen", name="Crust Lichen", archetype="lichen", role="Pioneer of bare, sunlit stone.",
  description="Flat mineral-orange rosettes that etch into exposed rock. Extremely slow, extremely persistent.",
  habitat=dict(substrates={"rock": 1.0, "gravel": 0.55, "wood": 0.15}, refuseSubstrates=["soil", "water"], refuseTags=[],
               moisture=P(0.35, 0.3), light=P(0.85, 0.25), nutrients=P(0.1, 0.3), maxWaterDepth=0.0, minSuitability=0.12),
  growth=dict(ratePerDay=0.14, maxBiomass=0.3, initialBiomass=0.03, declinePerDay=0.03, maturityDays=10, lifespanDays=900, nutrientPerBiomass=0.05, radiusAtMax=0.16, minRadius=0.03),
  spread=dict(intervalDays=9, radius=0.35, propagules=2, minBiomassFraction=0.45),
  competition=dict(radius=0.2, crowdingLimit=2.2, sensitivity=0.4),
  proximity=[], litterFraction=0.6, grazingValue=0.0,
  visual=dict(shape="crust", color=[0.95, 0.55, 0.12], color2=[0.98, 0.80, 0.30], colorVariance=0.07, height=0.004), tags=["lichen", "pioneer"]),
 dict(id="foliose_lichen", name="Leafy Lichen", archetype="lichen", role="Leafy lichen of weathered bark and shaded stone.",
  description="Blue-grey leafy lobes on weathered logs and shaded rock; cannot hold on to crumbling rotten wood.",
  habitat=dict(substrates={"wood": 1.0, "rock": 0.6}, refuseSubstrates=["soil", "water", "gravel"], refuseTags=["wood_rotting"],
               moisture=P(0.5, 0.25), light=P(0.65, 0.3), nutrients=P(0.15, 0.3), maxWaterDepth=0.0, minSuitability=0.12),
  growth=dict(ratePerDay=0.18, maxBiomass=0.4, initialBiomass=0.03, declinePerDay=0.05, maturityDays=8, lifespanDays=600, nutrientPerBiomass=0.08, radiusAtMax=0.13, minRadius=0.03),
  spread=dict(intervalDays=7, radius=0.35, propagules=2, minBiomassFraction=0.45),
  competition=dict(radius=0.2, crowdingLimit=2.0, sensitivity=0.5),
  proximity=[dict(target="feature:log", radius=0.2, bonus=0.1)], litterFraction=0.6, grazingValue=0.0,
  visual=dict(shape="foliose", color=[0.55, 0.72, 0.72], color2=[0.78, 0.88, 0.80], colorVariance=0.05, height=0.012), tags=["lichen"]),
 dict(id="creeping_groundcover", name="Creeping Pennywort", archetype="plant", role="Fast vascular groundcover of rich soil.",
  description="Round glossy leaves on creeping runners; outcompetes mosses where nutrients are plentiful.",
  habitat=dict(substrates={"soil": 1.0, "gravel": 0.3}, refuseSubstrates=["rock", "water", "wood"], refuseTags=[],
               moisture=P(0.6, 0.25), light=P(0.6, 0.3), nutrients=P(0.5, 0.3), maxWaterDepth=0.0, minSuitability=0.2),
  growth=dict(ratePerDay=0.3, maxBiomass=1.2, initialBiomass=0.06, declinePerDay=0.3, maturityDays=7, lifespanDays=90, nutrientPerBiomass=0.35, radiusAtMax=0.3, minRadius=0.05),
  spread=dict(intervalDays=3, radius=0.5, propagules=2, minBiomassFraction=0.5),
  competition=dict(radius=0.35, crowdingLimit=2.2, sensitivity=1.0),
  proximity=[], litterFraction=0.85, grazingValue=0.0,
  visual=dict(shape="creeper", color=[0.36, 0.72, 0.26], color2=[0.55, 0.85, 0.35], colorVariance=0.06, height=0.05), tags=["plant", "groundcover"]),
 dict(id="marginal_waterside", name="Dwarf Rush", archetype="plant", role="Emergent plant of wet margins and shallows.",
  description="Upright green rushes rooted in the shallows and saturated banks; dies back on dry ground.",
  habitat=dict(substrates={"soil": 1.0, "gravel": 0.6, "water": 0.9}, refuseSubstrates=["rock", "wood"], refuseTags=[],
               moisture=P(1.0, 0.15), light=P(0.7, 0.3), nutrients=P(0.45, 0.35), maxWaterDepth=0.09, hardMinMoisture=0.7, minSuitability=0.2),
  growth=dict(ratePerDay=0.35, maxBiomass=1.5, initialBiomass=0.06, declinePerDay=0.35, maturityDays=8, lifespanDays=80, nutrientPerBiomass=0.3, radiusAtMax=0.18, minRadius=0.04),
  spread=dict(intervalDays=4, radius=0.4, propagules=2, minBiomassFraction=0.5),
  competition=dict(radius=0.25, crowdingLimit=2.6, sensitivity=0.8),
  proximity=[dict(target="feature:water", radius=0.4, bonus=0.2)], litterFraction=0.9, grazingValue=0.0,
  visual=dict(shape="reed", color=[0.30, 0.62, 0.22], color2=[0.72, 0.80, 0.34], colorVariance=0.05, height=0.28), tags=["plant", "riparian"]),
 dict(id="ornamental_herb", name="Starflower", archetype="plant", role="Small flowering herb of bright, fertile ground.",
  description="A compact rosette crowned with vivid violet star flowers; hungry for light and nutrients.",
  habitat=dict(substrates={"soil": 1.0, "gravel": 0.3}, refuseSubstrates=["rock", "water", "wood"], refuseTags=[],
               moisture=P(0.5, 0.25), light=P(0.8, 0.25), nutrients=P(0.55, 0.25), maxWaterDepth=0.0, minSuitability=0.2),
  growth=dict(ratePerDay=0.25, maxBiomass=0.8, initialBiomass=0.05, declinePerDay=0.3, maturityDays=10, lifespanDays=60, nutrientPerBiomass=0.4, radiusAtMax=0.12, minRadius=0.04),
  spread=dict(intervalDays=5, radius=0.6, propagules=2, minBiomassFraction=0.6),
  competition=dict(radius=0.25, crowdingLimit=1.8, sensitivity=1.1),
  proximity=[], litterFraction=0.85, grazingValue=0.0,
  visual=dict(shape="herb", color=[0.30, 0.62, 0.24], color2=[0.62, 0.36, 0.95], colorVariance=0.08, height=0.12), tags=["plant", "flowering"]),
]

inter = {
 "neutralCompetition": 0.5, "intraspecificCompetition": 1.0,
 "relations": [
  {"a": "carpet_moss", "b": "cushion_moss", "type": "compete", "strength": 1.1, "reason": "Both mosses hold the same moist ground and trap the same water film."},
  {"a": "cushion_moss", "b": "carpet_moss", "type": "compete", "strength": 1.2, "reason": "Carpet moss mats smother slow cushions."},
  {"a": "carpet_moss", "b": "creeping_groundcover", "type": "compete", "strength": 1.3, "reason": "Pennywort leaves shade the moss mat."},
  {"a": "creeping_groundcover", "b": "carpet_moss", "type": "compete", "strength": 0.8, "reason": "Moss mats hold moisture but compete weakly for nutrients."},
  {"a": "ornamental_herb", "b": "creeping_groundcover", "type": "compete", "strength": 1.5, "reason": "Runners overtop the low starflower rosettes."},
  {"a": "crust_lichen", "b": "carpet_moss", "type": "refuse", "radius": 0.25, "reason": "Crust lichens cannot establish beneath a closed moss carpet."},
  {"a": "crust_lichen", "b": "cushion_moss", "type": "refuse", "radius": 0.15, "reason": "Moss cushions cover the bare rock crust lichen needs."},
  {"a": "foliose_lichen", "b": "crust_lichen", "type": "benefit", "radius": 0.4, "bonus": 0.1, "reason": "Crust lichen weathers the surface into footholds for leafy lobes."},
  {"a": "wetbank_moss", "b": "marginal_waterside", "type": "benefit", "radius": 0.5, "bonus": 0.15, "reason": "Rush clumps shade and humidify the bank."},
  {"a": "marginal_waterside", "b": "wetbank_moss", "type": "neutral"},
  {"a": "ornamental_herb", "b": "cushion_moss", "type": "neutral"},
 ]}

if __name__ == "__main__":
    for f in flora:
        with open(os.path.join(ROOT, "flora", f["id"] + ".json"), "w", newline="\n") as fh:
            json.dump(f, fh, indent=2); fh.write("\n")
    with open(os.path.join(ROOT, "flora_interactions.json"), "w", newline="\n") as fh:
        json.dump(inter, fh, indent=2); fh.write("\n")
    print(f"wrote {len(flora)} flora species")
