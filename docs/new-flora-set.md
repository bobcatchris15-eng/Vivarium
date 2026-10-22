# The 2026-10-21 new-flora set — ten species

Ten new species for the world, added as a set. Between them they fill a lot of ground the
roster did not have: a rooted floating lily, a submerged meadow, a cattail, a shade flower with
no chlorophyll, a spiny pioneer, a bulb plant, a cup fungus, a cushion moss, a veteran tree, and a
tank bromeliad. Three of them are the first trees, the first submerged plant, the first
mycoheterotroph and the first bromeliad the world has had.

Every species follows the same method the existing rebuilds follow. A real plant is chosen as the
structural analogue, its anatomy is reproduced honestly, and then exactly one thing is invented
and carried all the way through — the fantasy layer. The invention is never a general
"magical glow"; it is one specific idea, and the rest of the plant stays true to the family it
comes from.

| Species | Placement | Analogue | The one invented thing |
|---|---|---|---|
| Glasslily | waterside | water lily (*Nymphaea*) | the flower is cut from stained glass |
| Silverweed | waterside | pondweed (*Elodea* / *Cabomba*) | silver filaments comb the leaf margins |
| Coalspike | waterside | cattail (*Typha*) | the spike's armour is fired black, and one violet ember closes it |
| Marrowbloom | terrestrial | ghost pipe (*Monotropa uniflora*) | the bell is thin enough to see its own stamens through |
| Blackneedle | terrestrial | thistle (*Cirsium*) | the spines are black glass |
| Starbulb | terrestrial | ornamental onion (*Allium*) | the tunic is dull gold and the tepals are cold blue glass |
| Bonecup | decomposer | elf cup (*Sarcoscypha*) | the cups are alabaster and hold still black fluid |
| Embercup | moss & lichen | cushion moss with a *Splachnum* capsule | the spore cups are ember-coloured and lit |
| Hollowcrown | woody | a hollowed, pollarded veteran oak | the cavity is lined with moss that glows |
| Tankbloom | terrestrial | tank bromeliad | the tank holds a mirror of still water |

## How they are built

Each species owns one generator file, `src/Vivarium.Sim/Geometry/OrganismMeshes.<Species>.cs`,
and one line in the dispatch in `OrganismMeshes.cs`. The generators are authored in **real
metres** and converted into the engine's unit frame by `NewFloraAppendWorld`, so what is built is
what the player sees at the size the content file declares.

The shared builders live in `OrganismMeshes.NewFlora.cs`:

| Builder | What it is for |
|---|---|
| `NewFloraBlade` | a lofted organic blade: one shared vertex grid, closed section, smooth-shaded, with a keel, a cup, a camber, a tip curl, a twist and a toothed margin |
| `NewFloraPetal` | a cupped double-sided petal with a fanned vein network and a separately coloured back |
| `NewFloraStalk` | a tapering stalk that arcs from a base to a tip |
| `NewFloraFilament` | a fine tapered filament: a hair, a thread, a spine |
| `NewFloraScaleColumn` | a column of shingled scales wound helically around an axis |
| `NewFloraMarkOrgan` | flags a vertex range so the shader keeps the pigment the organ was authored in |
| `NewFloraBudget` / `NewFloraCount` | the render-only detail budget |
| `NewFloraAppendWorld` | metres to engine units, with the inverse-transpose normal fix |

### The organ marker

The vascular branch of `flora.gdshader` mixes every leaf towards the species' own green leaf
pigment. That is right for a leaf and wrong for a petal, a bract, a fungus cup, a glass spine or
a coal-black scale. An organ that has to keep its authored colour is flagged through `UV2`, and
the shader then uses the vertex colour as written:

* `UV2.x = 1.35` — the pigment is used as authored. This band is also the shader's marked-nodule
  band, so a species that asks for night emission lights these organs as well.
* `UV2.x = 1.28` — the pigment is used as authored *and* picks up the view-angle prismatic
  shift, which is what makes cut glass read as cut glass. This band never glows, so it is the one
  to use for the coal around a glowing ember.

`NewFloraMarkOrgan(m, first, count, glass)` writes either band.

### The lit organs

Three of the species ask for night emission, and each asks for it in a different place, so the
shader's emission mask mode has to be set per species in `NightFloraMaterials.Bind`:

| Species | Mode | What lights |
|---|---|---|
| Coalspike | 2 (marked nodule) | the single violet ember at the tip of the spike; the coal around it is on the non-glowing band and stays dead |
| Embercup | 2 (marked nodule) | the ember-coloured spore capsules; the cushion they stand on does not |
| Hollowcrown | 2 (marked nodule) | the moss lining the cavity; nothing else on the tree |

## The species in detail

### Glasslily — *waterside_aquatic*

A water lily is a piece of engineering, not a decoration. A lily pad is a floating raft: it is
dished so the water it carries runs to the centre, it is veined like a leaf so it can hold itself
out of the water, and it is split by a cleft so it can unfurl like a hand without tearing its own
veins. The generator builds the pad as a shell with a real thickness, so the rim rolls over, and
the cleft is a real cut with capped edges.

The flower is three whorls of petals — outer, middle, inner — with the inner whorl standing up and
closing the cup, which is what gives a water lily depth instead of a flat rosette. The fantasy
layer is that the petals are cut from glass: cold blue-green at the tip, warm rose at the base,
with the veins laid in as the lead of a stained-glass panel. A golden boss of stamens sits in the
middle, and the cup holds a still disc of water that never spills.

The underside of the pad keeps the real plant's wine-purple, but its vein network has spread into
the spread of a human palm, because a hand is the thing that holds water.

### Silverweed — *waterside_aquatic*

A creeping stolon on the bed with rootlets at every node, jointed stems rising out of it, and a
whorl of four to six ribbon leaves at every node above the base. Each whorl is turned against the
last, so the plant does not stack its leaves in one plane. The blade is keeled along the midrib
and its margin carries real teeth. The fantasy layer is the comb of silver filaments that stands
off each margin and waves along it, and the silver thread that runs up the stem.

### Coalspike — *waterside_aquatic*

The iconic cattail cigar, solved rather than guessed. The builder lays a shingled plate down at
29° above horizontal, so a plate of length *L* only reaches *0.55 L* up the column; for the rows
to shingle over one another that has to be at least the row spacing, which fixes the plate length
for a given column length and number of rows. The result is a 25–30 mm cigar on a 1.3 m plant.

The fantasy layer is that the armour is fired rather than vegetated: every plate is coal black
with a dull bronze line along its free edge, and a faint warm band runs low on the column where
the male and female parts meet. Where a real cattail carries its thread of male flowers, this one
closes that thread into a single violet ember bud, and the ember is the only lit organ on the
plant.

### Marrowbloom — *terrestrial*

A ghost pipe gave up photosynthesis, and every part of that has to be visible: the scales stand
where leaves would, the stem is fleshy and translucent rather than woody, and the flower hangs
because the ovary sits above the bell and the bell is a weight. The generator builds the stem as
a rising arc that nods over, clothes it in overlapping scales, and hangs a bell of four or five
fused lobes from the nod. There is no green anywhere in the plant.

The fantasy layer is the translucency: the bell is thin enough to see the shadow of its own
stamens through the petal, and it carries a faint warm flush that comes and goes with the light.

### Blackneedle — *terrestrial*

A thistle is a plant that has given up on being soft. The leaves are lobed into spines, the stem
carries spiny wings running the whole way up, and the flower head is a cage of bracts built to
keep anything from getting at the florets. The one thing it cannot make spiny is the wool: a
thistle's leaf back is covered in white hairs, and that white underside is the signature of the
whole family, so every lobe is built as a double-sided blade with a woolly back.

The fantasy layer is the material of the spines: brown and brittle in a real thistle, black glass
here, catching the light as hard black points.

### Starbulb — *terrestrial*

An allium, with the tunicated bulb planted in the grit, two or three bulblets at its foot, a
rosette of glaucous keeled strap leaves, and a bare scape carrying a true globe of star flowers
on pedicels of graded length. The fantasy layer is the tunic — dull gold rather than papery
brown — and tepals that are the colour of cold blue glass.

### Bonecup — *decomposer*

A *Sarcoscypha* cushion on rotten wood, and a crowd of cups growing out of it. Each cup is a real
cup: a lofted surface of revolution with an outer wall, a rolled and wavy rim, a recessed inner
surface and a different material inside and out. The age of a fruit body is carried by its size —
the two oldest cups are the biggest and stand on the longest stems — and that single rule gives
the colony its silhouette without any cup being special.

The fantasy layer is that the cups are cut from alabaster and hold a disc of still black fluid
that never spills.

### Embercup — *moss & lichen*

A cushion moss, cross with the coloured capsules of *Splachnum*. The cushion is a low dome of many
upright micro-stems, each carrying three or four rows of tiny lanceolate microphylls, standing to
slightly different heights so the silhouette is lumpy rather than smooth. From it rises a scatter
of long bare setae, each carrying a capsule with a swelling base, a narrow neck and a pointed
beaked lid.

The fantasy layer is the capsules: ember-orange, and the one lit part of the plant.

### Hollowcrown — *woody*

The first tree in the roster, and the one the set needed most: the world had shrubs and
microcanopies but no vertical structure at all.

A veteran tree is a tree that has been through things. The bole is not a clean cylinder: it is
buttressed where the roots had to hold it up, fluted down its length where the bark has weathered
into deep furrows, and hollow where something has got into it. The generator builds the trunk as
a surface of revolution with a swelling foot and shoulder, seven flutes running its length, and a
doorway cut into the base — the trunk is built over a reduced arc near the ground, and the cavity
behind the opening is modelled as a real inside wall with a broken rim and a floor.

The crown is a pollard: four or five thick blunt limbs ending in sawn faces, each throwing a
dense burst of epicormic shoots from the cut. The fantasy layer is the cavity, which is lined with
moss that glows, so the tree keeps its own dim light inside itself and the doorway is the only
place in the wood where anything shines.

It is 4.4 m tall. The first draft was 6.8 m, which is taller than the radius of the default
island; the world-view capture showed the crown standing clear over the block's cut face, and a
tree that big in an 8 m world reads as a mast rather than as a tree.

### Tankbloom — *terrestrial*

A bromeliad has turned its own leaves into a cistern. The inner leaves stand upright and their
bases are swollen, so the water the plant collects is held in a cup; the outer leaves sweep down
and out, and they are the ones that shade the tank and keep the water from evaporating.

The fantasy layer is the pool: a real tank holds a scummy film of debris, and this one holds a
mirror — still water, darker than the sky, hemmed in by leaves that are waxy and pale inside.

## Where they appear

The ten species are placed by habitat, not scattered:

| Preset | New species |
|---|---|
| default (Streamside Hollow) | all ten |
| misty_cloud_forest | hollowcrown, marrowbloom, embercup, bonecup, silverweed |
| peat_fen | coalspike, glasslily, silverweed, bonecup, embercup |
| bayou_marsh | coalspike, glasslily, tankbloom, silverweed |
| estuary_delta | silverweed, glasslily, coalspike |
| alpine_tundra | starbulb, blackneedle, embercup |
| arid_canyon | starbulb, blackneedle |
| woodland_vernal_pools | marrowbloom, hollowcrown, embercup, tankbloom |

## Captures

Per-species specimen captures are under `game/build/newflora-review/`. The headless specimen run
is the acceptance gate, not the triangle count:

```
Godot_v4.7.1-stable_mono_win64_console.exe --path game -- --specimen <id> --output build/newflora-review/<id>
```

`--close` gives the near view and `--underside` the view from below, which is how the lily pad's
palm-veined underside was checked. `--reverse` shows the hollowcrown's doorway, which faces the
other way.

## What the captures caught

The captures are the reason this set reads the way it does. Things that only showed up on screen
and were then fixed:

* **The lily pad's centre was a degenerate ring.** The pad's inner ring collapsed to a point, which
  made a fan of zero-area faces and showed as a black star in the middle of every pad. The ring
  now keeps a small but real radius and the centre is closed with a fan.
* **The pad's veins ran the wrong way round.** The shader's leaf venation draws its branch veins
  at a constant position along the leaf, so a pad whose UV ran radius-first came out with
  concentric ripples instead of radial veins. The pad's UVs are angle-first, radius-second, which
  is what the game's own lily pad does.
* **The marrowbloom's bell hung upwards.** The bell's profile was written with a negative
  distance along the hanging axis, so the corolla grew up out of the ovary instead of down from
  it.
* **The tankbloom's flower spike floated.** The bracts started a third of the way up the spike's
  own height, leaving a visible gap between the scape's tip and the spike. The lowest whorl now
  sits on the scape.
* **The coalspike's spike was green.** The shingled plates are leaf-surface organs, and the
  shader mixes leaves towards the species' green pigment, so a coal-black column came out green
  until the plates were flagged as organs.
* **The hollowcrown's bole was open at the top.** A trunk built as a surface of revolution with no
  cap shows the inside of the tree from above, which is the kind of gap the eye picks out first.
* **The hollowcrown's bark banded horizontally.** The shader's stem branch reads the trunk's
  `UV.x` as a position along the shaft, so a metre-scale value put a node scar every half metre
  instead of a handful over the whole trunk. The bark's `UV.x` now runs the normalised height.
* **The hollowcrown was taller than the island.** A 6.8 m tree in an 8 m world stood clear over
  the block's cut face. It is 4.4 m now, which is three times a shrub and still a tree.
* **The blackneedle's involucre was a gold pineapple.** The bracts were authored bronze from the
  foot up; the bronze belongs only on the last third, and the bracts read as green below it.
* **The bonecup's cushion was a slab of pink.** The sheet's edge radius swung by 45 per cent, so
  the cushion's lobes lay flat on the ground like cut paper. The swing is smaller and the sheet
  is thicker, and it reads as a blob of jelly.

## Verification

* The sim and the game build with zero errors and zero warnings.
* `BootstrapTests` carries the new roster: 62 species — moss_lichen 9, terrestrial 17,
  waterside_aquatic 17, decomposer 7, woody 12.
* `GeometryLodTests` holds every one of the ten inside its three parametric tiers and its
  top-tier cap, and none of them contributes a degenerate triangle at any tier.
* `IntegrationTests.StarterPopulationsAreCompleteAndValid` passes its flora half: every one of the
  62 species is present in the default preset's starters and none of them is standing in
  refused habitat.
* `ReferenceWarmupTimingTests`' golden digest was re-pinned, as that test's own comment
  instructs when content legitimately moves it. `scratch/pin-warmup-digest.ps1` does the re-pin
  from a measured probe so the value is never transcribed by hand. Note that the committed pin
  was already stale against the committed code before this set, so the digest moves whenever the
  simulation moves and wants re-pinning again after any simulation change.
* The rest of the suite's failures are pre-existing. They were measured against a pristine `HEAD`
  checkout in a separate worktree: **32 failed / 700 passed of 735** at `HEAD`, and **28 failed /
  726 passed of 758** in this tree. The tree has 23 more test cases — the per-species theories
  grew with the roster — and every one of them passes. The failure set is the same one, in the
  moss tier budgets, the legacy save migration, the dead-flora round trip, the coverage-layer
  migration, the fauna food check, the starter flora checks that the in-flight coverage work has
  retired, and the perf canaries.

