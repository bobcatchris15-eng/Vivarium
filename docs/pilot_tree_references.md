# Pilot Tree forms and references

User direction, 2026-09-28: implement all six forms. Every Pilot Tree stands at a hexagonal island corner. Its two primary buttresses extend along the neighboring sides. Most of the trunk remains outside the sampled patch. These are fictional organisms filling recognizable ecological niches; no form is a replica of a real species.

The sources below guide anatomy and habitat. Reference photographs are not bundled or used as textures.

| Fictional form | Structural reference | Interpretation for Vivarium |
| --- | --- | --- |
| Gloomspire | [NPS hemlock ravines](https://www.nps.gov/dewa/learn/nature/hemlock-ravines.htm); [photographs of mossy old-growth hemlock bases](https://www.wildernesscommittee.org/news/largest-known-old-growth-eastern-hemlock-forest-canada-threatened-logging) | Layered, drooping evergreen sprays above dark furrowed bark; broad, moss-friendly root shelves and sheltered crevices. Dense canopy creates a cool, humid shaded niche. |
| Needlevault | [USDA Sitka spruce botanical description](https://research.fs.usda.gov/feis/species-reviews/picsit); [USDA rooting habit](https://research.fs.usda.gov/silvics/sitka-spruce) | Buttressed bole with long shallow lateral roots; raised root ridges alternate with damp pockets. Narrow needle sprays and hanging cones distinguish its crown. |
| Crowncoil | [Kew Cycas morphology](https://powo.science.kew.org/taxon/urn:lsid:ipni.org:names:326847-2/general-information); [Kew giant cycad](https://www.kew.org/read-and-watch/oldest-pot-plant-in-world-eastern-cape-giant-cycad); [Encephalartos trunk photograph](https://www.onlineplantguide.com/plant-details/4474/) | Overlapping persistent leaf-base armor, arching pinnate fronds and large reproductive cones. Enlarged buttresses are a fictional adaptation to the island-edge niche, rather than a claim about real cycad roots. |
| Emberpillar | [NPS coast redwood](https://www.nps.gov/articles/000/coast-redwood.htm); [NPS about the trees](https://www.nps.gov/redw/learn/nature/about-the-trees.htm) | Thick fibrous bark and a towering bole imply a canopy beyond the sampled patch. Massive sweeping buttresses and bark slough support a persistent woody substrate niche. |
| Basinwarden | [Kew Ceiba pentandra descriptions](https://powo.science.kew.org/taxon/urn:lsid:ipni.org:names:1166232-2/general-information); [Ceiba buttress photographs](https://www.thoughtco.com/ceiba-pentandra-sacred-tree-maya-171615) | Thin tall buttress walls sweep into the trunk, creating basins and sheltered ground. Broad branching crown, fictional broad leaves and fruit distinguish a richer litter-producing niche. |
| Palehollow | [NPS old-growth hike: plated bark and hollows](https://www.nps.gov/neri/planyourvisit/old-growth-forest-hike.htm); [NPS Muir Woods old-growth structure](https://www.nps.gov/muwo/learn/nature/old-growth.htm); [NPS decomposition habitats](https://www.nps.gov/places/burnwood-trail-stop-8-decomposition.htm) | Composite fictional conifer: pale irregular bark plates, an open basal hollow, broken crown and fungal shelves. Old-growth cavities and decaying wood ground its fungal habitat role. |

## Shared construction rules

- Preserve the corner placement on every seed and every selected form. Random selection chooses a form and corner, never an arbitrary edge position.
- Buttresses reach the next vertex along both adjacent sides, with closed faces cut flush to island planes; secondary roots and crevices break symmetry without replacing those structural roots.
- Blend root feet into local ground, with tapering height and width. Basinwarden needs wall-like buttresses rather than cylindrical ropes.
- Keep scale legible from the normal island camera and bark anatomy legible at close range.
- Generate visual state deterministically. Loading preserves the selected form, corner and seed; existing saves without a Pilot Tree remain without one.
- Habitat influence must vary spatially. Ordinary flora cannot spawn through trunk/root volumes.

## Scope

Pilot Tree infrastructure is Campaign 3 of `local_world_context_plan.md`. Its references and ecological parameters provide the foundation for the later Pilot Tree event campaign. Scheduled branch fall, fruiting pulses, and the generic outside-event framework remain separate campaigns.

Palehollow shelves use [USDA Forest Service conk anatomy](https://www.fs.usda.gov/eng/active_dev/views/heterobasidion_root_disease.html): asymmetrical overlapping woody caps, concentric upper bands, a cream growth margin and pale pore-bearing underside. These guide a fictional composite, not a species replica.
