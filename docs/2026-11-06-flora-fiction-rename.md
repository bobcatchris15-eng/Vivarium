# Flora Fiction Rename — 6 November 2026

All 63 species added in the catalog expansion are **slightly fantastical fictional species based on real-world analogues**. This document is the authoritative rename map: every species keeps its ecology (niche, growth, spread, water depth, substrates) exactly as-is; only its identity (id, name, shape, role, description, small palette accents) changes.

**Rules**
- Do not change any `habitat`, `growth`, `spread`, `competition`, `proximity`, `litterFraction`, `sheddingPerDay`, `grazingValue`, `postLife`, `decomposition`, `reproduction` numbers. **Sole exception: `sootband` (formerly `horsetail`), which gets the niche tweak listed beneath its row** so it stops shadowing Ringreed's saturated wet margin.
- Do not add `bioluminescenceColor`/`bioluminescenceStrength`.
- `visual.shape` becomes the new species id for every renamed species (coverage species included).
- Palette: keep existing `color`/`color2` unless a "palette" note says otherwise.

## Rename table

| old id | new id | name | shape | role | description | palette note |
|---|---|---|---|---|---|---|
| feathermoss | quillfeather_moss | Quillfeather moss | quillfeather_moss | Prostrate shade moss whose fronds lie like fallen quills. | Feather-flat fronds sweep one way across shaded ground; when dry each tip curls into a tight quill and springs open again after rain. | — |
| rustsphagnum | rustbellow_moss | Rustbellow moss | rustbellow_moss | Acid-hummock moss of saturated peat. | Rust-red and green mounds stand above the water film, and the hollow mat breathes water in and out with the water table. | — |
| threadmoss | wispthread_moss | Wispthread moss | wispthread_moss | Wispy thread moss of wet shaded timber. | Fine threadlike shoots drift in still air over sodden logs; the strands never quite dry, even under cover. | — |
| silkmoss | silkgleam_moss | Silkgleam moss | silkgleam_moss | Sunlit bank moss with a silken sheen. | Golden-green mats on open banks catch the light like combed silk, brightest where the sun lingers longest. | — |
| rockcap_moss | stoneknot_moss | Stoneknot moss | stoneknot_moss | Drought-hardened pincushion of bare stone. | Charcoal-green cushions draw tight against scree and boulders; each knot shrinks to a bead in drought and swells again with the first damp. | — |
| pinemoss | pinequilt_moss | Pinequilt moss | pinequilt_moss | Tufted acid-litter moss of dry needle-fall shade. | Khaki tufts stitch the needle-fall into a loose quilt; beneath it the litter stays dry and slow to rot. | — |
| ribbon_liverwort | slickmantle | Slickmantle | slickmantle | Thallose mat of dripping rock and bark. | Flat olive tongues keep their stone perpetually glossy, even in drought, as if the rock sweated under them. | — |
| beardlace_lichen | hagshair_lichen | Hagshair lichen | hagshair_lichen | Hanging fruticose lichen of humid air. | Long grey strands hang and curl from old branches, combed by the damp; in dry spells they rattle like straw. | — |
| sunburst_lichen | sunscald_lichen | Sunscald lichen | sunscald_lichen | Radiant crust of sun-baked stone. | A flat mustard-orange sunburst spreads over bare rock, warm to the touch even after dusk. | — |
| porcelain_lichen | moonshell_lichen | Moonshell lichen | moonshell_lichen | Pale foliose lichen of shaded bark. | White-blue lobes lift like shell fragments from quiet bark, cool and faintly pearled in low light. | — |
| reindeer_lichen | ashfingers_lichen | Ashfingers lichen | ashfingers_lichen | Pale branching lichen of dry heath. | Ashen fingers stand upright on thin soil, brittle and slow, breaking with a chalky snap. | — |
| haircap_moss | capstar_moss | Capstar moss | capstar_moss | Star-leaved tall moss ripening gold spore-capsules. | Spear-straight shoots open into star-leaved heads; each fertile shoot raises a long seta and a nodding gold cap that sparks in low sun. | — |
| puffball | duskpuff | Duskpuff | duskpuff | Squat papery spore-puff of rich humus. | A dull papery sphere on a short stem; bruised, it stains violet and puffs a cloud of dusk-coloured spores. | color2 → [0.52, 0.34, 0.58] |
| earthstar | starsplit | Starsplit | starsplit | Star-crowned puffball of open soil. | The outer skin splits into thick tan rays that lie flat on the ground while the pale heart swells and puffs above them. | — |
| stinkhorn | reekhorn | Reekhorn | reekhorn | Tall carrion-scented spike of wet litter. | A cream horn crowned in glistening dark jelly and hung with a tattered lace skirt; its reek draws flies from across the island. | — |
| birdsnest_fungus | cradlecup | Cradlecup | cradlecup | Tiny nest fungus of wet wood. | Little ochre nests hold clutches of glossy spore-eggs, each fitted with a coiled cord that snaps free when rain strikes the cup. | — |
| coral_fungus | candlecoral | Candlecoral | candlecoral | Upright coral fungus of buried wood. | Cream fingers rise and fork like soft candles, their tips blushing rose where the light finds them. | — |
| morel | ashcomb | Ashcomb | ashcomb | Spring honeycomb of hardwood shade. | An ash-grey cap pitted like a drowned comb rides a cream stem; the pits deepen as it ripens and darken at the rim. | — |
| boletus | barrowbole | Barrowbole | barrowbole | Stout bolete of rich soil. | A heavy brown dome like a burial mound sits over a fat stem; beneath, the pore face is cream and fine as bread. | — |
| ladyfern | swanfrond | Swanfrond | swanfrond | Soft bipinnate shuttlecock of rich moist shade. | Pale-green fronds arch out in layered feathers, the youngest standing coiled with their frondlings tucked under them. | — |
| royalfern | sceptrefern | Sceptrefern | sceptrefern | Tall separated-pinnae crown of wet woodland. | Widely spaced pinnae stand along each stem like the teeth of a sceptre, and the fronds hold themselves up long after rain. | — |
| brackenfern | emberbracken | Emberbracken | emberbracken | Stiff triangular pioneer of dry open slopes. | Tough triangular fronds bronze early and stand through winter; a creeping black rhizome marches them uphill a little further each year. | color2 → [0.62, 0.41, 0.23] |
| hartstonguefern | mirrorstrap_fern | Mirrorstrap fern | mirrorstrap_fern | Undivided glossy strap fern of shaded crevices. | Broad undivided straps grow from cracks, varnished so deeply they mirror the light that reaches them; the dark midrib holds the two halves like a hinge. | — |
| sedgefountain | quillsedge | Quillsedge | quillsedge | Arching fine-bladed fountain sedge. | Blades fine as quills fan out and fall back as they lengthen, and each spray carries a few small brown spikelets nodding at its tips. | color2 → [0.62, 0.66, 0.42] |
| cottongrass | cloudtuft | Cloudtuft | cloudtuft | Cottongrass of acid pool margins. | Thin stems carry white tufts that hold together in wind and shine after rain; a stand of it reads as a low cloud resting on the mire. | — |
| woodrush | duskbristle | Duskbristle | duskbristle | Shaggy tuft of shaded woodland floors. | Hairy grey-green blades carry long bristles along their margins; the whole tuft shivers and whispers at the slightest air. | — |
| reedgrass | bannerreed | Bannerreed | bannerreed | Tall vigorous reed-grass of rich marsh. | Broad blades rise above the sedge and lift loose panicles of spikelets that hang like pale banners all summer. | color2 → [0.78, 0.66, 0.48] |
| sheepsfescue | ashwire | Ashwire | ashwire | Fine dry-turf grass of thin exposed soil. | Ashen bristle blades form a wiry turf that stays grey-green through the dry season and turns silver in seed. | — |
| horsetail | sootband | Sootband | sootband | Unbranched evergreen jointed rush of damp banks and shallow margins. | Soot-green shafts stand in dense clumps, ringed black at every joint and ribbed from base to crown; each fertile stem ends in a blunt pale cone that stays long after the rest of the mire has gone. | color1 → [0.20, 0.30, 0.20]; color2 → [0.75, 0.72, 0.52] |
| woodanemone | starveil | Starveil | starveil | White-starred spring ephemeral of deep shade. | Palmate leaves unfold under the first warm days and hold up a veil of white stars; the whole colony retreats to root before the canopy closes. | — |
| buttercup | butterglint | Butterglint | butterglint | Glossy gold-flowered meadow herb. | The cupped petals carry a varnish so deep that each flower keeps a small bright copy of the sky. | — |
| wildstrawberry | cinderberry | Cinderberry | cinderberry | Low runner with scarlet fruit and trifoliate leaves. | Red fruit sits on the ground under three-lobed leaves; the runners root as they run, and the seeds travel in fur and feathers. | — |
| marshviolet | mistviolet | Mistviolet | mistviolet | Violet star of spring flushes and wet meadows. | Round leaves lie flat under pale violet flowers that nod; whole patches surface for a few spring weeks and vanish beneath the summer growth. | — |
| woodsorrel | hushclover | Hushclover | hushclover | Shamrock-leaved clumper of acid shade. | Three heart-leaves fold at dusk and open again at first light, and the small veined white flowers appear to hover well above the leaves. | — |
| nettle | hallowsting | Hallowsting | hallowsting | Serrated coloniser of rich disturbed ground. | Tall stalks of opposite serrated leaves cast drooping green tassels; touch one and pale welts rise and burn for an hour. | — |
| burdock | burlmantle | Burlmantle | burlmantle | Huge felted leaves and hooked seed-burs. | A rosette of enormous woolly leaves shrouds the ground, and the hooked burs stand waiting at shoulder height for anything that passes. | color1 → [0.35, 0.43, 0.32]; color2 → [0.48, 0.35, 0.54] |
| cowparsley | heddlelace | Heddlelace | heddlelace | White lace umbels on hollow stems. | Hollow grooved stems thread up through feathery leaves and open white lace heads; the whole plant nods and rattles in a breeze. | — |
| meadowsweet | fenfoam | Fenfoam | fenfoam | Frothy cream plumes of wet meadow. | Interrupted pinnate leaves climb wine-red stems to frothy plumes; the foam holds its shape for weeks and smells sweet enough to gather. | — |
| marshmarigold | marshkettle | Marshkettle | marshkettle | Glossy gold cups at the edge of spring pools. | Round glossy leaves hold dewdrops like coins and the first gold cups open while there is still ice in the shadows. | — |
| loosestrife | widowspire | Widowspire | widowspire | Tall magenta plant of marsh and streamside. | Square stems carry narrow leaves and spire after spire of magenta flowers that keep their colour long after they dry standing. | — |
| stonecrop | brightknot | Brightknot | brightknot | Low starry succulent of dry stone. | Tiny rosettes knot together over bare ground and break into gold stars; whole mats turn pink at their margins in the sun. | — |
| houseleek | cragset | Cragset | cragset | Tight blue rosette wedged in rock. | A sculpted blue-grey rosette grows perfectly set to the crack, and its children ring it in ever-smaller copies. | — |
| coontail | deepquill | Deepquill | deepquill | Deep-water plume of still pools. | Whorls of stiff forked quills climb dark stems in deep water; the plumes sway with currents too slow for the eye to catch. | — |
| bladderwort | mirepocket | Mirepocket | mirepocket | Carnivorous floater with pocketed stems. | Feathery submerged stems carry little traps, each with a hinged mouth; pale yellow lipped flowers lift clear of the water to be seen. | — |
| waterstarwort | pondstars | Pondstars | pondstars | Floating star-rosettes of calm water. | Tiny star rosettes drift in loose fleets at the surface and re-form within minutes wherever they are parted. | — |
| bogbean | bogfringe | Bogfringe | bogfringe | Trifoliate rafts with fringed white flowers. | Three-lobed leaves lie on still water and the white flowers open feathered like small fringes; the whole raft rises an inch as it flowers. | — |
| burreed | burrflag | Burrflag | burrflag | Floating ribbon and burr of shallow margins. | Long ribbons trail from the bank and end in spiked burr heads; grazing animals carry them to the next pool in their coats. | — |
| waterplantain | paddlerib | Paddlerib | paddlerib | Broad ribbed leaves and whorled airy stems of mud and shallows. | Deep-ribbed leaves stand out of the mud on long stalks, and the branching stems open whorl upon whorl of small pale flowers. | — |
| arrowhead | barbarrow | Barbarrow | barbarrow | Arrow-leaved emergent of shallow margins. | Leaves end in two long barbed lobes and the stems carry whorls of white triple flowers; the barbs hook drifting debris into shelter. | — |
| ivylace | shadewrap | Shadewrap | shadewrap | Deep-shade climber clothing trunks and stone. | Lobed dark leaves wrap trunks in overlapping ranks, held by roots fine as mist along the stem; whole trunks go green with it. | — |
| hop | bitterbine | Bitterbine | bitterbine | Coarse vigorous twiner of rich ground. | The bine climbs faster than it can thicken, slumping back in a tangle of papery cones that smell sharply bitter when crushed. | — |
| honeysuckle | trumpetbind | Trumpetbind | trumpetbind | Wood-edge twiner with tubular flowers. | Opposite oval leaves march up the stem, and at each pair hangs a small cream-pink trumpet that keeps its scent until evening. | — |
| bogmyrtle | miremyrtle | Miremyrtle | miremyrtle | Aromatic grey shrub of fen and peat. | Grey-green leaves crowd slender arching stems and the catkins stand straight up before the leaves; bruised, the whole shrub perfumes the air. | — |
| hazel | nutglade | Nutglade | nutglade | Coppice shrub of generous hardwood edges. | Round scalloped leaves cast a green shade, golden catkins dangle in rows, and the little bearded nuts come in clusters of three. | — |
| hawthorn | thornmantle | Thornmantle | thornmantle | Thorny thicket shrub with white blossom and crimson haws. | Twigs are armed at every joint, and the thicket blossoms white for a fortnight before the crimson haws set it alight in autumn. | — |
| juniper | greywatch | Greywatch | greywatch | Blue-green evergreen of dry rock and scree. | A short fluted trunk splits into ascending limbs clothed in needle sprays; the berry-cones ripen blue-grey and stay on the wood through winter. | — |
| woollywillow | silverfelt | Silverfelt | silverfelt | Silver-downy tundra shrub. | Felted silver leaves catch the low light and furry catkins stand out early; the whole shrub looks dusted with frost long before there is any. | — |
| silverbirch | moonbark | Moonbark | moonbark | Pale pioneer tree of dry-mesic ground. | Peeling white bark keeps a cold radiance even at noon, and the airy crown lets light pour through to the ground beneath. | — |
| quakingaspen | shiverleaf | Shiverleaf | shiverleaf | Pale-barked clonal pioneer of moist ground. | Round leaves hang on flattened stalks and never settle, so the whole crown trembles in air that nothing else feels. | — |
| alder | fenforge | Fenforge | fenforge | Dark waterside tree that knits the bank. | Fissured dark bark leans over the water and cone-catkins hang through winter; its rooting swells the bank where every stream bend needs it. | — |
| waterwillow | banksorrow | Banksorrow | banksorrow | Leaning silky-leaved tree of stream banks. | Stems lean far out over the water and the long silky leaves trail on the surface; grounded tips rise again as new trees. | — |
| mirepine | mirespire | Mirespire | mirespire | Sparse evergreen of bog margins. | A straight red-barked spire stands over the mire with needle sprays on every limb and cones kept close to the trunk. | — |
| rowan | witchberry | Witchberry | witchberry | Berried wood-edge tree of thin soil. | Grey limbs carry feathered leaves and corymbs of scarlet berries; the berries hang on past leaf-fall and no bird is seen to take the first of them. | — |

### sootband niche tweak (sole exception)
The old `horsetail` sat inside Ringreed's own niche (saturated wet margin, branched collars). `sootband` becomes the leaner, drier-tolerant, unbranched evergreen kind:
- `habitat.moisture`: optimum 0.74, tolerance 0.24
- `habitat.light`: optimum 0.70, tolerance 0.28
- `habitat.nutrients`: optimum 0.32, tolerance 0.30
- `habitat.hardMinMoisture`: 0.30; `habitat.maxWaterDepth`: 0.03
- everything else (growth/spread/height 0.60/substrates/placementGroup) unchanged.
- Mesh identity (for the later mesh-iteration pass): NO branchlets at all; 6–10 ribbed unbranched shafts; black bands at every joint with small sheath-teeth collars; 2–4 fertile stems ending in a blunt pale stacked-ring cone; slight clump lean. This deliberately de-overlaps Ringreed (branched collars) and Kinkcane (kinked palmate canes).

## Builder method renames (mesh code)

| old method | new method |
|---|---|
| Polytrichum | CapstarMoss |
| Puffball | Duskpuff |
| Earthstar | Starsplit |
| Stinkhorn | Reekhorn |
| Birdsnest | Cradlecup |
| Coralfungus | Candlecoral |
| Morel | Ashcomb |
| Bolete | Barrowbole |
| Ladyfern | Swanfrond |
| Royalfern | Sceptrefern |
| Brackenfern | Emberbracken |
| Hartstongue | Mirrorstrap |
| Sedge | Quillsedge |
| Cottongrass | Cloudtuft |
| Woodrush | Duskbristle |
| Reedgrass | Bannerreed |
| Horsetail | Sootband |
| Anemone | Starveil |
| Buttercup | Butterglint |
| Strawberry | Cinderberry |
| Violet | Mistviolet |
| Sorrel | Hushclover |
| Nettle | Hallowsting |
| Burdock | Burlmantle |
| Cowparsley | Heddlelace |
| Meadowsweet | Fenfoam |
| Marigold | Marshkettle |
| Loosestrife | Widowspire |
| Stonecrop | Brightknot |
| Houseleek | Cragset |
| Coontail | Deepquill |
| Bladderwort | Mirepocket |
| Starwort | Pondstars |
| Bogbean | Bogfringe |
| Burreed | Burrflag |
| Waterplantain | Paddlerib |
| Arrowhead | Barbarrow |
| IvylaceNode | ShadewrapNode |
| HopNode | BitterbineNode |
| HoneysuckleNode | TrumpetbindNode |
| ShrubBogmyrtle | ShrubMiremyrtle |
| ShrubHazel | ShrubNutglade |
| ShrubHawthorn | ShrubThornmantle |
| ShrubJuniper | ShrubGreywatch |
| ShrubWoollywillow | ShrubSilverfelt |
| Birch | Moonbark |
| Aspen | Shiverleaf |
| Rowan | Witchberry |
| Alder | Fenforge |
| WillowTree | Banksorrow |
| PineTree | Mirespire |

## Reference-update checklist
1. `game/content/flora/*.json` — file renames + `id`, `name`, `role`, `description`, `visual.shape`, palette notes.
2. `game/content/index.json` — 63 file paths.
3. `game/content/presets/*.json` (19 files) — 63 species ids.
4. `game/content/flora_interactions.json` — ids in `a`/`b`.
5. `game/Render/FloraSurfaceProfiles.cs` — `Leaf(id)`, `LeafPattern(id)`, `Bark(id)` ids.
6. `game/Render/FloraVisualProfile.cs` — id lists added in `DetermineMorphologicalFamily`.
7. `game/Render/CoverageRenderer.cs` — the new moss/lichen id→type mappings.
8. `game/Render/OrganismRenderers.cs` — ground-hugging shape list (capstar_moss, brightknot, cragset, pondstars, deepquill, mirepocket, bogfringe).
9. `src/Vivarium.Sim/Geometry/FloraGeometryPlacement.cs` — vine shape names (shadewrap, bitterbine, trumpetbind).
10. `src/Vivarium.Sim/Geometry/OrganismMeshes.cs` — dispatch case labels + builder call names; collapse the duplicate vine cases (`shadewrap` appears once, `bitterbine` once, `trumpetbind` once).
11. The 12 `OrganismMeshes.New*.cs` files — builder method names per the table above.
