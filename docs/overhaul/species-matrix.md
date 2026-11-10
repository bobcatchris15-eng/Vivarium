# Species matrix (baseline2 flora triage)

Date: 2026-11-10. Source captures: `build/overhaul/baseline2/specimen-flora/` (plain, far, macro), `specimen-flora-slope/`, `specimen-stages/`. Contact sheets: `build/overhaul/S0/sheets/`.

Scales (1-5, whole numbers; 5 = no visible defect at specimen distance):

- cont: continuity. Gaps, open tube ends, floating parts, hard joints, leaves detached from stems, ground contact.
- believ: reads as a real living organism with plausible structure.
- distinct: could be told apart from neighbouring species.

Rank is worst-first by cont+believ+distinct (ties alphabetical). Audit ids F1-F15 from `docs/overhaul/audit-flora.md`. Builder file is the best grep match of the species id in `OrganismMeshes.*.cs`; where the id appears in no builder (data-driven mats and a few others) the generic `OrganismMeshes.cs` is listed with the content archetype.

| rank | species | builder file | cont | believ | distinct | worst defect | audit id | notes (<=12 words) | status |
|---|---|---|---|---|---|---|---|---|---|
| 1 | starmoss | OrganismMeshes.OrganismMeshes.cs | 2 | 2 | 2 | single dark slab floats on rock | F2 | one flat leaf slab; reads wrong | baseline |
| 2 | ashwire | OrganismMeshes.OrganismMeshes.cs | 3 | 2 | 2 | nearly invisible thin wires | F15 | barely readable at plain | baseline |
| 3 | emberfan_fungus | OrganismMeshes.cs (fungus) | 2 | 2 | 3 | flat red stack slabs, floating | F13 | stacked plates look broken | baseline |
| 4 | siltstars | OrganismMeshes.OrganismMeshes.cs | 3 | 2 | 2 | nearly invisible runners | F15 | faint ground cover | baseline |
| 5 | bitterbine | OrganismMeshes.OrganismMeshes.cs | 2 | 3 | 3 | host trunk slabs, vine clips through bark | F10 | climber contact planar slabs | baseline |
| 6 | clinglace | OrganismMeshes.OrganismMeshes.cs | 2 | 3 | 3 | vine slabs clip trunk; leaves detached | F10 | climber on dark trunk | baseline |
| 7 | embercup | OrganismMeshes.OrganismMeshes.Embercup.cs | 3 | 3 | 2 | flat dark blob, indistinct | F15 | dark mound; looks like rock | baseline |
| 8 | fenhook | OrganismMeshes.OrganismMeshes.cs | 2 | 3 | 3 | vine slabs clip trunk | F10 | climber on trunk planar | baseline |
| 9 | floodlace_moss | OrganismMeshes.OrganismMeshes.cs | 4 | 2 | 2 | neon green blob mat | F15 | saturated flat mat | baseline |
| 10 | pearl_cushion_moss | OrganismMeshes.OrganismMeshes.cs | 4 | 2 | 2 | neon green blob; dark stalk pins | F15 | saturated mat pins | baseline |
| 11 | prismstar | OrganismMeshes.cs (plant) | 3 | 2 | 3 | single dark green blade fan, odd bulb | F6 | dark thin tuft with brown pod | baseline |
| 12 | shadewrap | OrganismMeshes.OrganismMeshes.cs | 2 | 3 | 3 | vine slabs clip trunk | F9 | pale slab vine on trunk | baseline |
| 13 | spiralvine | OrganismMeshes.OrganismMeshes.cs | 2 | 3 | 3 | vine slabs clip dark trunk | F10 | climber lost in black trunk | baseline |
| 14 | velvetweave_moss | OrganismMeshes.OrganismMeshes.cs | 4 | 2 | 2 | neon green blob with pins | F15 | same as pearl cushion | baseline |
| 15 | barbarrow | OrganismMeshes.OrganismMeshes.cs | 3 | 3 | 3 | water stretch; sparse blades | F13 | tiny aquatic spike | baseline |
| 16 | bogfringe | OrganismMeshes.OrganismMeshes.cs | 3 | 3 | 3 | leaves flat on water, floating | F13 | small aquatic rosette | baseline |
| 17 | bogglass_moss | OrganismMeshes.OrganismMeshes.cs | 3 | 3 | 3 | stalk tips black open, flat green mat | F5 | bright mat; black tipped stalks | baseline |
| 18 | bonecup | OrganismMeshes.OrganismMeshes.Bonecup.cs | 3 | 3 | 3 | black base slab; caps float on thin stalks | F5 | black disc under caps | baseline |
| 19 | brooklace | OrganismMeshes.OrganismMeshes.Brooklace.cs | 3 | 3 | 3 | oversized ribbon stem in macro, tiny at plain | F12 | scale mismatch plain vs macro | baseline |
| 20 | capstar_moss | OrganismMeshes.OrganismMeshes.cs | 3 | 3 | 3 | stems pasted on flat sheet | F6 | skinny stems on mat | baseline |
| 21 | cinderberry | OrganismMeshes.OrganismMeshes.cs | 3 | 3 | 3 | thin stems, fruit swapped onto tubes | F8 | sparse sprawl; red fruit pasted | baseline |
| 22 | cloudtuft | OrganismMeshes.OrganismMeshes.cs | 4 | 3 | 2 | thin blades, tiny seed heads | F15 | generic grass tuft | baseline |
| 23 | coinrunner | OrganismMeshes.OrganismMeshes.Coinrunner.cs | 3 | 3 | 3 | leaf discs detached from runner | F6 | pad discs float on vine | baseline |
| 24 | deepquill | OrganismMeshes.OrganismMeshes.cs | 3 | 3 | 3 | blade stretch under water | F13 | teal tuft underwater | baseline |
| 25 | embercrust_lichen | OrganismMeshes.OrganismMeshes.cs | 4 | 3 | 2 | flat orange plate, cracked texture | F15 | saturated orange decal | baseline |
| 26 | fenbead | OrganismMeshes.OrganismMeshes.Fenbead.cs | 3 | 3 | 3 | stem tips open; thick ribbed stems | F5 | bead nodes plausible | baseline |
| 27 | fencomb | OrganismMeshes.OrganismMeshes.cs | 4 | 3 | 2 | single thin spike barely visible | F15 | spindly stalk | baseline |
| 28 | hagshair_lichen | OrganismMeshes.cs (lichen) | 4 | 3 | 2 | grey shard mat repeats | F15 | same mat as neighbours | baseline |
| 29 | lanternsedge | OrganismMeshes.OrganismMeshes.cs | 3 | 3 | 3 | umbel stalk thin, dark base | F5 | spindly dark stalks | baseline |
| 30 | mirepocket | OrganismMeshes.OrganismMeshes.cs | 3 | 3 | 3 | water stretch; tiny plain | F13 | barely visible aquatic | baseline |
| 31 | mirrorleaf | OrganismMeshes.cs (plant) | 3 | 3 | 3 | pads thin, water stretch | F13 | sparse pads | baseline |
| 32 | mooncoin | OrganismMeshes.OrganismMeshes.Mooncoin.cs | 3 | 3 | 3 | discs lift off ground, no stalk | F13 | translucent coins floating | baseline |
| 33 | moonshell_lichen | OrganismMeshes.cs (lichen) | 4 | 3 | 2 | pale blue shard mat | F15 | same mat family | baseline |
| 34 | paddlerib | OrganismMeshes.OrganismMeshes.cs | 3 | 3 | 3 | leaf cluster dark flat; water stretch | F13 | small aquatic | baseline |
| 35 | pinequilt_moss | OrganismMeshes.cs (moss) | 4 | 3 | 2 | yellow blob mat | F15 | mat blob | baseline |
| 36 | pondstars | OrganismMeshes.OrganismMeshes.cs | 3 | 3 | 3 | tiny stars sparse on water | F13 | runners thin, barely visible | baseline |
| 37 | quillfeather_moss | OrganismMeshes.cs (moss) | 4 | 3 | 2 | neon green blob | F15 | mat blob | baseline |
| 38 | rain_jelly | OrganismMeshes.OrganismMeshes.cs | 3 | 2 | 4 | dark blob on box slab | F4 | dark blobs on brown plate | baseline |
| 39 | rustbellow_moss | OrganismMeshes.cs (moss) | 4 | 3 | 2 | orange blob mat | F15 | mat blob | baseline |
| 40 | silkgleam_moss | OrganismMeshes.cs (moss) | 4 | 3 | 2 | yellow-orange blob mat | F15 | mat blob | baseline |
| 41 | silverweed | OrganismMeshes.OrganismMeshes.Silverweed.cs | 4 | 3 | 2 | small leaf tufts, generic | F15 | generic rosette tuft | baseline |
| 42 | slickmantle | OrganismMeshes.cs (moss) | 4 | 3 | 2 | pale leaf mat | F15 | mat blob | baseline |
| 43 | stoneknot_moss | OrganismMeshes.cs (moss) | 4 | 3 | 2 | green disc mat | F15 | mat blob | baseline |
| 44 | streamribbon | OrganismMeshes.OrganismMeshes.cs | 3 | 3 | 3 | single blade clump thin | F13 | sparse blades | baseline |
| 45 | sunscald_lichen | OrganismMeshes.cs (lichen) | 4 | 3 | 2 | orange leaf mat | F15 | mat blob | baseline |
| 46 | sunstone_rosette | OrganismMeshes.cs (plant) | 3 | 3 | 3 | rosette lobes sparse; thin red scape | F5 | small rosette | baseline |
| 47 | tidebell | OrganismMeshes.OrganismMeshes.cs | 3 | 3 | 3 | tiny dark sprigs, hard joins | F15 | barely readable | baseline |
| 48 | wispthread_moss | OrganismMeshes.cs (moss) | 4 | 3 | 2 | dark green mat blob | F15 | mat blob | baseline |
| 49 | antlerlace_lichen | OrganismMeshes.OrganismMeshes.cs | 4 | 3 | 3 | flat plate, shard texture repeats | F15 | lichen pale slab | baseline |
| 50 | ashfingers_lichen | OrganismMeshes.cs (lichen) | 4 | 3 | 3 | spiky mat, grey | F15 | dense dry mat | baseline |
| 51 | blackneedle | OrganismMeshes.OrganismMeshes.Blackneedle.cs | 4 | 3 | 3 | stem-leaf base reads as point; dark flat leaves | F6 | dark rosette leaves; slope contact okay | baseline |
| 52 | blue_sundew | OrganismMeshes.OrganismMeshes.Cragparasol.cs | 4 | 3 | 3 | leaf mat repeats one pool; slope clipping | F15 | dense clone mat | baseline |
| 53 | brightknot | OrganismMeshes.OrganismMeshes.cs | 4 | 3 | 3 | flat leaf pads, small | F6 | yellow flowers on runner | baseline |
| 54 | burlmantle | OrganismMeshes.OrganismMeshes.cs | 4 | 3 | 3 | blade bases pointed; thin stem | F6 | sparse flower stalk | baseline |
| 55 | burrflag | OrganismMeshes.OrganismMeshes.cs | 4 | 3 | 3 | water depth stretch of blades | F13 | iris-like tuft | baseline |
| 56 | carrion_bell | OrganismMeshes.OrganismMeshes.cs | 3 | 3 | 4 | cap tubes open, hard stalk joins | F5 | red bell caps odd | baseline |
| 57 | cradlecup | OrganismMeshes.OrganismMeshes.cs | 4 | 3 | 3 | cups tiny floating over litter | F13 | cup ring barely visible | baseline |
| 58 | duskbristle | OrganismMeshes.OrganismMeshes.cs | 4 | 3 | 3 | bristle fine, seeds float on thin stalks | F5 | thin grey tuft | baseline |
| 59 | duskpuff | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 2 | smooth puffball no texture | F4 | plain mushroom | baseline |
| 60 | ghostbonnet | OrganismMeshes.OrganismMeshes.cs | 3 | 3 | 4 | cap flaps torn; stalks thin hard joins | F5 | ghostly caps good | baseline |
| 61 | glass_antlers | OrganismMeshes.OrganismMeshes.cs | 3 | 3 | 4 | black hole base, antlers open tips | F5 | dark crater at base | baseline |
| 62 | glassfinger | OrganismMeshes.OrganismMeshes.Glassfinger.cs | 4 | 3 | 3 | arms fused into blue star, ground intersect | F2 | star tangle | baseline |
| 63 | glasslily | OrganismMeshes.OrganismMeshes.Glasslily.cs | 4 | 3 | 3 | pad camber flat; water stretch | F13 | pads hover over bare ground | baseline |
| 64 | hallowsting | OrganismMeshes.OrganismMeshes.cs | 3 | 3 | 4 | stacked leaf slabs reading as blocks | F6 | blocky leaf stack | baseline |
| 65 | hookthicket | OrganismMeshes.OrganismMeshes.Hookthicket.cs | 3 | 3 | 4 | leaf bases floating on arching canes | F6 | dark leaves on thin arches | baseline |
| 66 | hushclover | OrganismMeshes.OrganismMeshes.cs | 4 | 3 | 3 | leaf bases pointed | F6 | clover clump | baseline |
| 67 | marrowbloom | OrganismMeshes.OrganismMeshes.Marrowbloom.cs | 3 | 3 | 4 | white stalks open tips under bulbs | F5 | odd pale pods | baseline |
| 68 | pitcher_plant | OrganismMeshes.OrganismMeshes.PitcherPlant.cs | 3 | 3 | 4 | pitcher tube tops open; collar bead | F5 | stalks pasted | baseline |
| 69 | ruffle_lichen | OrganismMeshes.OrganismMeshes.Bonecup.cs | 4 | 3 | 3 | flat shard slab, same pale teal as neighbours | F15 | reads as pasted decal | baseline |
| 70 | snaptrap | OrganismMeshes.OrganismMeshes.Snaptrap.cs | 3 | 3 | 4 | trumpet mouth hard joint to stalk | F5 | stalk joins open | baseline |
| 71 | starsplit | OrganismMeshes.OrganismMeshes.cs | 3 | 3 | 4 | petals flat discs under ball | F6 | puffball in star; hard joint | baseline |
| 72 | starveil | OrganismMeshes.OrganismMeshes.cs | 4 | 3 | 3 | leaf bases pointed | F6 | small white flower clump | baseline |
| 73 | trumpetbind | OrganismMeshes.OrganismMeshes.cs | 3 | 3 | 4 | vine tubes parallel to trunk, flat leaves | F10 | climber stiff vertical | baseline |
| 74 | bannerreed | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 3 | stem tips open under plumes | F5 | reed with plumes | baseline |
| 75 | barrowbole | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 3 | smooth cap, no gill detail | F4 | plain bolete | baseline |
| 76 | butterglint | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 3 | leaf base points; stalks open | F6 | buttercup clump | baseline |
| 77 | coalspike | OrganismMeshes.OrganismMeshes.Coalspike.cs | 4 | 4 | 3 | stalk tip open under spike | F5 | sparse; two spikes only | baseline |
| 78 | cragset | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 3 | rosette lobes smooth, stalkless | F4 | pink-grey succulent ok | baseline |
| 79 | dewbonnet | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 3 | caps flat joins, no gills | F4 | cluster of caps plausible | baseline |
| 80 | emberbracken | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 3 | frond stem bases pointed | F6 | good fern | baseline |
| 81 | embercrown | OrganismMeshes.OrganismMeshes.cs | 3 | 4 | 4 | trunk forks, fruit spikes pasted | F8 | lush crown on thin trunk | baseline |
| 82 | fenfoam | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 3 | stem forks open, umbel small | F5 | umbel forb fine | baseline |
| 83 | fenforge | OrganismMeshes.OrganismMeshes.cs | 3 | 4 | 4 | fork equal radius; black trunk | F1 | small tree believable | baseline |
| 84 | frosttussock | OrganismMeshes.cs (plant) | 3 | 4 | 4 | bare white wire stems cross blades | F5 | odd white wires in macro | baseline |
| 85 | glassrush | OrganismMeshes.OrganismMeshes.Glassrush.cs | 4 | 4 | 3 | culm tips open | F5 | rush clump fine | baseline |
| 86 | glowbriar | OrganismMeshes.OrganismMeshes.cs | 3 | 4 | 4 | fruit pasted on stems | F8 | good shrub silhouette | baseline |
| 87 | greywatch | OrganismMeshes.OrganismMeshes.cs | 3 | 4 | 4 | black base pit, forks open | F1 | branch fork thin | baseline |
| 88 | hollowcrown | OrganismMeshes.OrganismMeshes.Hollowcrown.cs | 3 | 4 | 4 | bough forks equal radius, open ends | F1 | good form; seams | baseline |
| 89 | kinkcane | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 3 | cane forks open, leaf bases pointed | F1 | bamboo-like canes fine | baseline |
| 90 | lacemantle | OrganismMeshes.OrganismMeshes.Lacemantle.cs | 4 | 4 | 3 | fruit pasted on branches | F8 | small tree believable | baseline |
| 91 | marshkettle | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 3 | leaf bases pointed; flat flowers | F6 | marigold-like clump | baseline |
| 92 | miremyrtle | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 3 | branch forks equal radius | F1 | good shrub | baseline |
| 93 | mirrorstrap_fern | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 3 | leaf base points, no veins | F6 | strap leaves clean | baseline |
| 94 | mistviolet | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 3 | stalk tips open under flowers | F5 | violet clump good | baseline |
| 95 | moonbark | OrganismMeshes.OrganismMeshes.cs | 3 | 4 | 4 | fork radius equal; leaning fronds | F1 | odd leaning trunks | baseline |
| 96 | moonfern | OrganismMeshes.OrganismMeshes.Moonfern.cs | 4 | 4 | 3 | frond base unrooted on slope | F2 | thin; few fronds | baseline |
| 97 | nutglade | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 3 | forks equal radius | F1 | good shrub | baseline |
| 98 | plumegrass | OrganismMeshes.OrganismMeshes.Plumegrass.cs | 4 | 3 | 4 | plumes saturated, no stem joins | F15 | rainbow plumes odd | baseline |
| 99 | quillsedge | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 3 | blade tips fine, base unrooted | F2 | grass tuft ok | baseline |
| 100 | rainshield | OrganismMeshes.OrganismMeshes.cs | 3 | 4 | 4 | petiole open ends, hard joins | F6 | good umbrella leaves | baseline |
| 101 | ringreed | OrganismMeshes.OrganismMeshes.Ringreed.cs | 3 | 4 | 4 | green wire rings around culms | F11 | ring collars look like wire | baseline |
| 102 | shadebell | OrganismMeshes.OrganismMeshes.cs | 3 | 4 | 4 | fruit pasted; forks equal | F8 | decent shrub | baseline |
| 103 | shiverleaf | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 3 | branch forks equal radius | F1 | ok small tree | baseline |
| 104 | silverfelt | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 3 | flat leaf bases, hard joins | F6 | grey cushion shrub | baseline |
| 105 | sootband | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 3 | culm tips open under spikes | F5 | rush clump fine | baseline |
| 106 | swanfrond | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 3 | frond bases pointed | F6 | ok fern | baseline |
| 107 | thornmantle | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 3 | forks equal radius; fruit pasted | F8 | thin thorny shrub | baseline |
| 108 | trifold | OrganismMeshes.OrganismMeshes.Trifold.cs | 4 | 4 | 3 | leaf blade bases pointed, stalks open | F6 | three-lobe leaves generic | baseline |
| 109 | vaultfern | OrganismMeshes.OrganismMeshes.Vaultfern.cs | 3 | 4 | 4 | black trunk base pit, fronds clip on slope | F2 | trunk base dark hole | baseline |
| 110 | veilblade | OrganismMeshes.OrganismMeshes.Veilblade.cs | 4 | 4 | 3 | culm tips open | F5 | grass tuft fine | baseline |
| 111 | veilfern | OrganismMeshes.OrganismMeshes.Veilfern.cs | 4 | 4 | 3 | frond bases unrooted on slope | F2 | dark fronds generic | baseline |
| 112 | widowspire | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 3 | spike tips open | F5 | purple spikes fine | baseline |
| 113 | ashcomb | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 4 | cap seam, stalk tube open | F4 | good morel | baseline |
| 114 | banksorrow | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 4 | bough forks equal radius | F1 | good weeping form | baseline |
| 115 | candlecoral | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 4 | branch tips open | F5 | clean coral fungus | baseline |
| 116 | cragparasol | OrganismMeshes.OrganismMeshes.Cragparasol.cs | 4 | 4 | 4 | fruit hard-attached to branch tubes | F8 | good silhouette; fruit pasted | baseline |
| 117 | heddlelace | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 4 | umbel stalk tips open | F5 | elegant umbel | baseline |
| 118 | lanternbrush | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 4 | leaf blade bases pointed | F6 | colourful leaf fan | baseline |
| 119 | mirespire | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 4 | trunk top open between boughs | F5 | good conifer | baseline |
| 120 | sceptrefern | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 4 | frond bases pointed | F6 | good fern with spikes | baseline |
| 121 | splitcrown | OrganismMeshes.OrganismMeshes.Splitcrown.cs | 4 | 4 | 4 | fork junction equal radius, fruit pasted | F1 | decent compound leaves | baseline |
| 122 | starbulb | OrganismMeshes.OrganismMeshes.Starbulb.cs | 4 | 4 | 4 | scape tip open under flower globe | F5 | clean allium form | baseline |
| 123 | tankbloom | OrganismMeshes.OrganismMeshes.Tankbloom.cs | 4 | 4 | 4 | stalk tip open under spike | F5 | agave rosette good | baseline |
| 124 | witchberry | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 4 | fork equal radius; fruit pasted | F8 | good small tree | baseline |
| 125 | reekhorn | OrganismMeshes.OrganismMeshes.cs | 4 | 4 | 5 | mesh cage cap thin edges | F5 | distinct stinkhorn cage | baseline |

## Shared defects

Count = species whose single worst visible defect maps to that audit id (>=5 listed).

| audit id | defect | species count |
|---|---|---|
| F5 | open tube ends (stalk/culm tips) | 27 |
| F15 | distinctiveness (mat blobs, generic tufts) | 26 |
| F6 | blade base point / petiole open | 21 |
| F13 | water-depth Y stretch / floating pads | 13 |
| F1 | fork junctions equal radius / open parent end | 10 |
| F8 | fruit pasted on tubes | 8 |
| F2 | ground contact on slopes | 6 |
| F4 | texture seams, flat surfaces | 6 |
| F10 | climber contact planar slabs | 5 |
