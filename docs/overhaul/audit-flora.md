# Flora / fungi / ground-cover audit (a1)

Read-only code audit, 2026-11-10, HEAD a193e6d. No Godot/dotnet run; every claim below is from source and marked "needs render" in section 4 where it cannot be settled without one. Paths are relative to `C:\Misc\Vivarium`; `OM.` = `src/Vivarium.Sim/Geometry/OrganismMeshes.`; `Prim` = `src/Vivarium.Sim/Geometry/Primitives.cs`.

## 0. Pipeline in one paragraph (needed to read everything else)

`FloraSpeciesDef.Shape` -> `OrganismMeshes.Flora()` switch (`OM.cs:19-250`) builds one `MeshData` per (species, morph[5 or 12], LOD tier[3]) in the unit frame (XZ radius 1, Y height 1; "authored in metres" generators convert at the end with `NewFloraAppendWorld`, `OM.NewFlora.cs:69`). `FloraRenderer.StartSpeciesBuild` (`game/Render/OrganismRenderers.cs:352`) uploads each as a MultiMesh. Vascular species are split into a stem surface (`flora.gdshader`) and a leaf surface (`leaf.gdshader`, 4 shared leaf pools) by `FloraVisualCompiler.Split` (`FloraVisualCompiler.cs:75`, `FloraVisualProfile.Compile` `:405-425`). Every instance is placed by ONE ground sample and a NON-UNIFORM scale `(r, h, r)` (`OrganismRenderers.cs:826, 956`); growth = scaling the adult mesh. Coverage species (12 mats + 7 lichens) never reach FloraRenderer: they are tile geometry in `CoverageRenderer` (`Append*` functions, `CoverageRenderer.cs:1404-2700`).

## 1. Species inventory (125 species: 106 instanced, 19 coverage)

Stages: `I` = single adult mesh scaled by biomass (all instanced species; also reused, tinted/tilted, for dead plants, `OrganismRenderers.cs:1032-1098`); `J` = additional juvenile mesh (`buildsJuvenile`, `:376-380`); `F` = fruiting variant mesh that REPLACES the adult mesh while fruiting (`:981-983`; built by `FloraFruitingFromBase`, `OM.cs:333`); `C` = coverage tile, biomass drives sheet height/density only. Every other species has no distinct young/mature/senescent geometry.

| id | name | kind | geometry producer (file) | renderer | stages |
|---|---|---|---|---|---|
| antlerlace_lichen | Antlerlace lichen | lichen | coverage tile + Append* (CoverageRenderer.cs) | CoverageRenderer | C (biomass->sheet height/density) |
| ashcomb | Ashcomb | fungus | Ashcomb (OM.NewFungi) | FloraRenderer | I |
| ashfingers_lichen | Ashfingers lichen | lichen | coverage tile + Append* (CoverageRenderer.cs) | CoverageRenderer | C (biomass->sheet height/density) |
| ashwire | Ashwire | graminoid | Ashwire (OM.NewGraminoids) | FloraRenderer | I |
| banksorrow | Banksorrow | tree | Banksorrow (OM.NewTreesB) | FloraRenderer | I |
| bannerreed | Bannerreed | graminoid | Bannerreed (OM.NewGraminoids) | FloraRenderer | I |
| barbarrow | Barbarrow | aquatic | Barbarrow (OM.NewAquaticsB) | FloraRenderer | I |
| barrowbole | Barrowbole | fungus | Barrowbole (OM.NewFungi) | FloraRenderer | I |
| bitterbine | Bitterbine | climber | ClimberNode:BitterbineNode (OM.NewVines) + vein tube | FloraRenderer | I J |
| blackneedle | Blackneedle | forb/herb | Blackneedle (OM.Blackneedle) | FloraRenderer | I |
| blue_sundew | Blue Sundew Mat | carnivore mat | coverage tile + Append* (CoverageRenderer.cs) | CoverageRenderer | C (biomass->sheet height/density) |
| bogfringe | Bogfringe | aquatic | Bogfringe (OM.NewAquaticsB) | FloraRenderer | I |
| bogglass_moss | Bogglass moss | moss | coverage tile + Append* (CoverageRenderer.cs) | CoverageRenderer | C (biomass->sheet height/density) |
| bonecup | Bonecup | fungus | Bonecup (OM.Bonecup) | FloraRenderer | I |
| brightknot | Brightknot | succulent/rosette | Brightknot (OM.NewSucculents) | FloraRenderer | I J |
| brooklace | Brooklace | aquatic | Brooklace (OM.Brooklace) | FloraRenderer | I |
| burlmantle | Burlmantle | forb/herb | Burlmantle (OM.NewForbsB) | FloraRenderer | I |
| burrflag | Burrflag | aquatic | Burrflag (OM.NewAquaticsB) | FloraRenderer | I J |
| butterglint | Butterglint | forb/herb | Butterglint (OM.NewForbsA) | FloraRenderer | I |
| candlecoral | Candlecoral | fungus | Candlecoral (OM.NewFungi) | FloraRenderer | I |
| capstar_moss | Capstar moss | moss | CapstarMoss (OM.NewSucculents) | FloraRenderer | I J |
| carrion_bell | Carrion Bell | fungus | CarrionBell (OM) | FloraRenderer | I |
| cinderberry | Cinderberry | forb/herb | Cinderberry (OM.NewForbsA) | FloraRenderer | I J F |
| clinglace | Clinglace | climber | ClimberNode:ClinglaceNode (OM) + vein tube | FloraRenderer | I J |
| cloudtuft | Cloudtuft | graminoid | Cloudtuft (OM.NewGraminoids) | FloraRenderer | I J |
| coalspike | Coalspike | graminoid | Coalspike (OM.Coalspike) | FloraRenderer | I |
| coinrunner | Coinrunner | forb/herb | Coinrunner (OM.Coinrunner) | FloraRenderer | I |
| cradlecup | Cradlecup | fungus | Cradlecup (OM.NewFungi) | FloraRenderer | I |
| cragparasol | Cragparasol | shrub | Cragparasol (OM.Cragparasol) | FloraRenderer | I J F |
| cragset | Cragset | succulent/rosette | Cragset (OM.NewSucculents) | FloraRenderer | I |
| deepquill | Deepquill | aquatic | Deepquill (OM.NewAquaticsA) | FloraRenderer | I J |
| dewbonnet | Dewbonnet fungus | fungus | Mushrooms (OM) | FloraRenderer | I |
| duskbristle | Duskbristle | graminoid | Duskbristle (OM.NewGraminoids) | FloraRenderer | I |
| duskpuff | Duskpuff | fungus | Duskpuff (OM.NewFungi) | FloraRenderer | I |
| emberbracken | Emberbracken | fern | Emberbracken (OM.NewFerns) | FloraRenderer | I |
| embercrown | Embercrown | shrub | ShrubEmbercrown (OM) | FloraRenderer | I F |
| embercrust_lichen | Embercrust lichen | lichen | coverage tile + Append* (CoverageRenderer.cs) | CoverageRenderer | C (biomass->sheet height/density) |
| embercup | Embercup | moss | Embercup (OM.Embercup) | FloraRenderer | I |
| emberfan_fungus | Emberfan fungus | fungus | Bracket (OM) | FloraRenderer | I |
| fenbead | Fenbead | aquatic | Fenbead (OM.Fenbead) | FloraRenderer | I |
| fencomb | Fencomb | aquatic | Fencomb (OM) | FloraRenderer | I |
| fenfoam | Fenfoam | forb/herb | Fenfoam (OM.NewForbsB) | FloraRenderer | I |
| fenforge | Fenforge | tree | Fenforge (OM.NewTreesB) | FloraRenderer | I |
| fenhook | Fenhook | climber | ClimberNode:FenhookNode (OM) + vein tube | FloraRenderer | I J |
| floodlace_moss | Floodlace moss | moss | coverage tile + Append* (CoverageRenderer.cs) | CoverageRenderer | C (biomass->sheet height/density) |
| frosttussock | Frosttussock | graminoid | Tussock (OM) | FloraRenderer | I |
| ghostbonnet | Ghostbonnet | fungus | GhostbonnetGeometry.Build (GhostbonnetGeometry) | FloraRenderer | I |
| glass_antlers | Glass Antlers | fungus | GlassAntlers (OM) | FloraRenderer | I |
| glassfinger | Glassfinger | succulent/rosette | Glassfinger (OM.Glassfinger) | FloraRenderer | I |
| glasslily | Glasslily | aquatic | Glasslily (OM.Glasslily) | FloraRenderer | I |
| glassrush | Glassrush | graminoid | Glassrush (OM.Glassrush) | FloraRenderer | I |
| glowbriar | Glowbriar | shrub | ShrubGlowbriar (OM.NightFlora) | FloraRenderer | I F |
| greywatch | Greywatch | shrub | ShrubGreywatch (OM.NewShrubs) | FloraRenderer | I |
| hagshair_lichen | Hagshair lichen | lichen | coverage tile + Append* (CoverageRenderer.cs) | CoverageRenderer | C (biomass->sheet height/density) |
| hallowsting | Hallowsting | forb/herb | Hallowsting (OM.NewForbsB) | FloraRenderer | I |
| heddlelace | Heddlelace | forb/herb | Heddlelace (OM.NewForbsB) | FloraRenderer | I |
| hollowcrown | Hollowcrown | tree | Hollowcrown (OM.Hollowcrown) | FloraRenderer | I |
| hookthicket | Hookthicket | shrub | HookthicketBramble (OM.Hookthicket) | FloraRenderer | I J |
| hushclover | Hushclover | forb/herb | Hushclover (OM.NewForbsA) | FloraRenderer | I |
| kinkcane | Kinkcane | shrub | KinkcaneAlienGeometry.Build (KinkcaneAlienGeometry) | FloraRenderer | I |
| lacemantle | Lacemantle | shrub | Lacemantle (OM.Lacemantle) | FloraRenderer | I J F |
| lanternbrush | Lanternbrush | shrub | ShrubLanternbrush (OM) | FloraRenderer | I F |
| lanternsedge | Lanternsedge | graminoid | LanternsedgeGeometry.Build (LanternsedgeGeometry) | FloraRenderer | I |
| marrowbloom | Marrowbloom | forb/herb | Marrowbloom (OM.Marrowbloom) | FloraRenderer | I |
| marshkettle | Marshkettle | forb/herb | Marshkettle (OM.NewForbsA) | FloraRenderer | I |
| miremyrtle | Miremyrtle | shrub | ShrubMiremyrtle (OM.NewShrubs) | FloraRenderer | I |
| mirepocket | Mirepocket | aquatic | Mirepocket (OM.NewAquaticsA) | FloraRenderer | I J |
| mirespire | Mirespire | tree | Mirespire (OM.NewTreesB) | FloraRenderer | I |
| mirrorleaf | Mirrorleaf | aquatic | FloatLeaf (OM) | FloraRenderer | I |
| mirrorstrap_fern | Mirrorstrap fern | fern | Mirrorstrap (OM.NewFerns) | FloraRenderer | I |
| mistviolet | Mistviolet | forb/herb | Mistviolet (OM.NewForbsA) | FloraRenderer | I |
| moonbark | Moonbark | tree | Moonbark (OM.NewTreesA) | FloraRenderer | I |
| mooncoin | Mooncoin | forb/herb | Mooncoin (OM.Mooncoin) | FloraRenderer | I |
| moonfern | Moonfern | fern | MoonfernSheet (OM.Moonfern) | FloraRenderer | I J |
| moonshell_lichen | Moonshell lichen | lichen | coverage tile + Append* (CoverageRenderer.cs) | CoverageRenderer | C (biomass->sheet height/density) |
| nutglade | Nutglade | shrub | ShrubNutglade (OM.NewShrubs) | FloraRenderer | I |
| paddlerib | Paddlerib | aquatic | Paddlerib (OM.NewAquaticsB) | FloraRenderer | I |
| pearl_cushion_moss | Pearl-cushion moss | moss | coverage tile + Append* (CoverageRenderer.cs) | CoverageRenderer | C (biomass->sheet height/density) |
| pinequilt_moss | Pinequilt moss | moss | coverage tile + Append* (CoverageRenderer.cs) | CoverageRenderer | C (biomass->sheet height/density) |
| pitcher_plant | Raincup Pitcher | succulent/rosette | PitcherPlant (OM.PitcherPlant) | FloraRenderer | I |
| plumegrass | Plumestep grass | graminoid | Plumegrass (OM.Plumegrass) | FloraRenderer | I |
| pondstars | Pondstars | aquatic | Pondstars (OM.NewAquaticsA) | FloraRenderer | I J |
| prismstar | Prismstar | forb/herb | inline herb case () | FloraRenderer | I |
| quillfeather_moss | Quillfeather moss | moss | coverage tile + Append* (CoverageRenderer.cs) | CoverageRenderer | C (biomass->sheet height/density) |
| quillsedge | Quillsedge | graminoid | Quillsedge (OM.NewGraminoids) | FloraRenderer | I |
| rain_jelly | Rain Jelly | fungus | RainJelly (OM) | FloraRenderer | I |
| rainshield | Rainshield | shrub | MidRainshield (OM.Midstory) | FloraRenderer | I J |
| reekhorn | Reekhorn | fungus | Reekhorn (OM.NewFungi) | FloraRenderer | I |
| ringreed | Ringreed | graminoid | Ringreed (OM.Ringreed) | FloraRenderer | I |
| ruffle_lichen | Ruffle lichen | lichen | coverage tile + Append* (CoverageRenderer.cs) | CoverageRenderer | C (biomass->sheet height/density) |
| rustbellow_moss | Rustbellow moss | moss | coverage tile + Append* (CoverageRenderer.cs) | CoverageRenderer | C (biomass->sheet height/density) |
| sceptrefern | Sceptrefern | fern | Sceptrefern (OM.NewFerns) | FloraRenderer | I |
| shadebell | Shadebell | shrub | ShrubShadebell (OM) | FloraRenderer | I F |
| shadewrap | Shadewrap | climber | ClimberNode:ShadewrapNode (OM.NewVines) + vein tube | FloraRenderer | I J |
| shiverleaf | Shiverleaf | tree | Shiverleaf (OM.NewTreesA) | FloraRenderer | I |
| silkgleam_moss | Silkgleam moss | moss | coverage tile + Append* (CoverageRenderer.cs) | CoverageRenderer | C (biomass->sheet height/density) |
| siltstars | Siltstars | aquatic | Siltstars (OM.NightFlora) | FloraRenderer | I |
| silverfelt | Silverfelt | shrub | ShrubSilverfelt (OM.NewShrubs) | FloraRenderer | I |
| silverweed | Silverweed | aquatic | Silverweed (OM.Silverweed) | FloraRenderer | I |
| slickmantle | Slickmantle | moss | coverage tile + Append* (CoverageRenderer.cs) | CoverageRenderer | C (biomass->sheet height/density) |
| snaptrap | Latchjaw | succulent/rosette | LatchJawColumn (OM.Snaptrap) | FloraRenderer | I |
| sootband | Sootband | graminoid | Sootband (OM.NewGraminoids) | FloraRenderer | I |
| spiralvine | Spiralvine | climber | ClimberNode:SpiralvineNode (OM) + vein tube | FloraRenderer | I J |
| splitcrown | Splitcrown | shrub | Splitcrown (OM.Splitcrown) | FloraRenderer | I J F |
| starbulb | Starbulb | succulent/rosette | Starbulb (OM.Starbulb) | FloraRenderer | I |
| starmoss | Starmoss | moss | Starmoss (OM) | FloraRenderer | I |
| starsplit | Starsplit | fungus | Starsplit (OM.NewFungi) | FloraRenderer | I |
| starveil | Starveil | forb/herb | Starveil (OM.NewForbsA) | FloraRenderer | I |
| stoneknot_moss | Stoneknot moss | moss | coverage tile + Append* (CoverageRenderer.cs) | CoverageRenderer | C (biomass->sheet height/density) |
| streamribbon | Streamribbon | aquatic | Streamribbon (OM) | FloraRenderer | I |
| sunscald_lichen | Sunscald lichen | lichen | coverage tile + Append* (CoverageRenderer.cs) | CoverageRenderer | C (biomass->sheet height/density) |
| sunstone_rosette | Sunstone rosette | succulent/rosette | Succulent (OM) | FloraRenderer | I |
| swanfrond | Swanfrond | fern | Swanfrond (OM.NewFerns) | FloraRenderer | I |
| tankbloom | Tankbloom | succulent/rosette | Tankbloom (OM.Tankbloom) | FloraRenderer | I |
| thornmantle | Thornmantle | shrub | ShrubThornmantle (OM.NewShrubs) | FloraRenderer | I F |
| tidebell | Tidebell | aquatic | PairedLeaf (OM) | FloraRenderer | I |
| trifold | Trifold | forb/herb | ? () | FloraRenderer | I |
| trumpetbind | Trumpetbind | climber | ClimberNode:TrumpetbindNode (OM.NewVines) + vein tube | FloraRenderer | I J |
| vaultfern | Vaultfern | shrub | Vaultfern (OM.Vaultfern) | FloraRenderer | I J |
| veilblade | Veilblade | graminoid | Veilblade (OM.Veilblade) | FloraRenderer | I |
| veilfern | Veilfern | fern | Veilfern (OM.Veilfern) | FloraRenderer | I J |
| velvetweave_moss | Velvetweave moss | moss | coverage tile + Append* (CoverageRenderer.cs) | CoverageRenderer | C (biomass->sheet height/density) |
| widowspire | Widowspire | forb/herb | Widowspire (OM.NewForbsB) | FloraRenderer | I |
| wispthread_moss | Wispthread moss | moss | coverage tile + Append* (CoverageRenderer.cs) | CoverageRenderer | C (biomass->sheet height/density) |
| witchberry | Witchberry | tree | Witchberry (OM.NewTreesA) | FloraRenderer | I F |
### 1a. Distinctiveness flags
- Shared shapes dispatch by id inside the switch (`reed`, `carpet`, `roundleaf`, `pairedleaf`, `fern`, `mushroom_cluster`; `OM.cs:26-125`), so no two species share a generator. Generic fallbacks actually reached: `PairedLeaf` (tidebell), `Mushrooms` (dewbonnet), `Tussock`, `Succulent`, `Bracket`, inline herb (prismstar). The default branch (`OM.cs:250`) is reached by no species.
- Sharing is at MATERIAL level: all ~100 vascular species draw leaf tissue from 4 baked pools (`game/Textures/LeafPools/{embercrown,glassfinger,lanternbrush,shadebell}`, fallback map `FloraVisualProfile.cs:129-141`); woody species use 3 bark sets (Bark001/013/014) varied by tint/repeat (`FloraSurfaceProfiles.cs:236-262`). Only pigment, vein strength and sheen differ (`:70-230`).
- Repeated skeleton templates (needs render): moonbark/shiverleaf/witchberry (`OM.NewTreesA.cs:643, 855, 1064`: 2-3 bowed Bezier trunks + Bezier scaffolds + sprays); splitcrown/lacemantle/cragparasol (fork + 3-4 boughs from one point + copy-pasted `*BranchCollar`: `OM.Splitcrown.cs:82`, `OM.Lacemantle.cs:84`, `OM.Cragparasol.cs:83`); six climbers share "node stub + petiole + lobed sub-blades from one point" (`OM.NewVines.cs:49-120`, `OM.cs:2319-2530`).
- Coverage pairs sharing a tile `CoverageFloraType`: velvetweave/silkgleam, pearl_cushion/stoneknot, bogglass/rustbellow, ruffle/moonshell, antlerlace/ashfingers (`CoverageRenderer.cs:333-346, 424-430`); per-species `Append*` structures differ.
- Dead code: instanced generators for coverage species (Floodlace, Velvetweave, PearlCushion, Embercrust, RuffleLichen, Antlerlace, BogglassMoss, CarpetDefault, generic Fern, RoundLeaf) are built only if a species has live `Flora.Items` (`OrganismRenderers.cs:265-290`), which coverage species never do. Expansion-climber `Flora()` entries (`OM.cs:247-249`) are unreachable (climbers use `ClimberNodeTier`, `OrganismRenderers.cs:395`). `game/App/BranchModuleLoader.cs` has no callers.

## 2. How parts are attached

- **Any tube** (`Prim.Tube` `Primitives.cs:92-183`): rings on a polyline, parallel-transported frames, taper-corrected normals. No end caps (open both ends). No miter compensation, so bends pinch. LOD drops path rows by rounding (`:102-108`) and re-sides to >=5-6.
- **Branch to trunk, Moonbark/Shiverleaf/Witchberry/NewTreesB**: child Bezier starts on the parent centreline; child base radius 0.44-0.48 of trunk, plus a 1.38x flare over the first 22% (`OM.NewTreesA.cs:748-756`) which acts as a cheap fillet. But top boughs receive the FULL trunk top radius each (`:725`): two children as thick as the parent, trunk top left open. Parent is never swollen; UV/bark phase restarts at the child.
- **Branch to trunk, Splitcrown/Lacemantle/Cragparasol**: all boughs start at one point `pFork` (`OM.Splitcrown.cs:775`); `*BranchCollar` (`:82-125`) is a Y-axis-aligned barrel (rings stacked in world Y), 0.85-1.23x radius, not aligned to the branch, no cross-section blend. Reads as a vertical bead on non-vertical limbs.
- **TreeSplitTrunk fallback (unused, same idiom)**: scaffold starts at `ySplit`, offset 0.6r, base radius 0.55r (`OM.cs:5216-5235`); trunk top ring uncapped (`:5121-5150`): discs poke outside the trunk radius, hole between.
- **`SoftTube` junction fillet** (`Form/SoftTube.cs:11-24, 54-83`): implemented, NEVER CALLED. `SoftTube` itself adds pulvinus bulges +38% (base, t<0.12) and +42% (apex) and a +22% collar to every stalk (`:44-49`).
- **Leaf blade to petiole (Broadleaf)**: 4-ring `Prim.Tube` petiole ending at `junctionPos` (`Broadleaf.cs:132-151`), blade grid starts there (`:190`), petiole base on stalk centreline (`:361`). Blade envelope is 0 at t=0 (`:41-90`) so the base is a point; cordate/reniform/orbicular add `lobeBack`, making row 0 a collinear spike and row 1 a wide sliver fan (`:51, 190`). Petiole end is open (0.85r); petiole colour constant (`:149`) vs blade base colour.
- **Blade to stem (CurvedLeaf, LeafBlade, FoliageBlade, TaLightLeaf)**: root vertex is the blade point (`Primitives.cs:210`, `LeafBlade.cs:104-110`, `OM.NewTreesA.cs:200-236`); seat relies on the caller burying the root. `TaLightLeaf.NormalAt` forces normals upward when `n.Y<0.2` (`:188-198`): lighting discontinuity on margins.
- **Pinna to rachis (PinnateFrond)**: `attach = rachisP + rachisS*rAtT` (`PinnateFrond.cs:460`) plus a petiolule tube (`:80`); built from a different frame than the rachis tube (`:398`), and low-side tubes are inscribed polygons, so attach can float.
- **Lofted blades (`NewFloraBlade`, `OM.NewFlora.cs:159-330`)**: sealed base/tip fans, smoothed normals. Best construction in the repo; used by NewForbs/Aquatics/Succulents/Vines.
- **Lobed vine leaf**: 3-5 `NewFloraBlade` sub-blades all rooted at `petioleEnd` (`OM.NewVines.cs:65-117`): gaps between lobes at the base.
- **Flower to stalk**: `FlowerHead` (`OM.cs:593`) translated to the stalk apex; petals begin at the apex; stalk end open; SoftTube stalks swell +42% at the tip (prismstar, NewForbsA `:111-163`).
- **Fruit to branch**: `AppendFruitOrgans` (`OM.cs:343-560`): anchors = vertices on ANY structural tube ring (including petioles) above 25-38% height plus leaf nodes; 3-sided pedicel from the surface vertex to a hanging ellipsoid. No calyx. The whole fruiting mesh REPLACES the adult mesh (hard swap, `OrganismRenderers.cs:981-983`). `lanternbrush`, `embercrown` return early (`:345`): clone with no fruit.
- **Stem to ground, generators**: bases at y=0 or slightly below: tree fallback -0.02 (`OM.cs:5084`), buttress tips -0.02 (`OM.NewTreesA.cs:244-262`), Hookthicket clamp >=-0.02 (`Hookthicket.cs:563`), Veilfern clamp y>=0 flattening base rings (`Veilfern.cs:430-433`), mycelium fan at y=0.015 (`OM.cs:3171-3188`). No skirt, no terrain conformity.
- **Stem to ground, renderer**: `pos = (x, GroundY, z)` from one `Terrain.Height` sample (`OrganismRenderers.cs:826`); upright plants get only a light-gradient lean, creepers a normal tilt + 2-6 mm lift (`:869`). On a slope the base ring is off the surface by slope*radius: buried uphill, gap downhill. Ambient turf is buried 1.5 mm with cell tilt (`AmbientGroundCoverRenderer.cs`, fine).
- **Root to terrain**: instanced species have buttress tubes only. Pilot tree: separate `BuildRoots` mesh, half-disc crests at `ground(p)+sin(a)*Height`, FLAT-shaded `Face()` quads, buried closed fans (`PilotTreeMeshes.cs:154-205`), second MeshInstance (`PilotTreeRenderer.cs:73-77`) apart from the trunk, whose base flare is `BarkRelief` (smooth normals).
- **Climber to host**: only moonbark/shiverleaf/fenforge hosts get ray-projection onto the host mesh (`FloraRenderer.WoodyContact.cs:13-80`); other hosts use `boleR = max(0.08, RadiusAtMax*0.12)` (`OrganismRenderers.cs:712`). Stem = tapering tube per internode with 10% overlap (`:476-495`); node stub radius (`OM.NewVines.cs:160-215`) is independent of that radius: possible step.
- **Moss/lichen structures**: `AddStraightTube` open-ring segments, no joint spheres (`CoverageRenderer.cs:1592-1625`); dichotomous forks start at parent end with a 0.72-0.9 radius step (`:1650-1680`). mm-scale.
- **Material split**: vascular meshes are split into stem and leaf surfaces by leaf spans (`FloraVisualCompiler.cs:75`), so petiole and blade are shaded by different shaders; blade wind weight is `UV.x`, stem weight `VERTEX.y*4` (`flora.gdshader:95-110`, `leaf.gdshader:49-72`).

## 3. Ranked defects (most visible first)

Common validation: `<godot> --path game -- --specimen-review <ABS_DIR> --specimen-species a,b,c` (2 shots per species at `NN_<id>.png` and `NN_<id>_macro.png`; flat dry cells <10 degrees; default list is only the 2026-11 set, so pass ids explicitly); diff before/after per macro image. `--visual-review` gives only 3 fixed shots (`VisualReviewRunner.cs:46-50`) with `--preset` (use `steep_rocky_slope`, `rocky_rise` for slopes).

**F1. Fork junctions: equal-radius children, open parent end, axis-aligned collar bead.** SHARED. Evidence: `OM.NewTreesA.cs:725` (top boughs at full trunk radius), `:753` (flare only on child), `OM.Splitcrown.cs:82-125, 709-714, 775-778` (Y-aligned collars, three copies), `Form/SoftTube.cs` fillet uncalled. Species: all 23 woody (banksorrow, fenforge, hollowcrown, mirespire, moonbark, shiverleaf, witchberry, splitcrown, lacemantle, cragparasol, rainshield, vaultfern, shrubs). Cause: each generator hand-rolls a collar; no area-conserving radius rule; the parent is never thickened. Remedy: one `ForkJunction` helper used by all: (a) child radii at a fork follow r_parent^2.5 = sum r_child^2.5; (b) widen the PARENT radii array within 1.5 child radii of the fork by up to 1.25x (smoothstep) before `Tube`; (c) replace the three `*BranchCollar` copies with one collar whose ring plane is normal to the child's first tangent. Keep each skeleton; change radius arrays and the collar call only. Benefit: no pipe-in-pipe, no beads, no diameter jumps. Validate: macro shots of splitcrown, lacemantle, cragparasol, moonbark, shiverleaf, witchberry at fork height. Size L (helper M, then one generator at a time). Files: `Primitives.cs` or new `Form/ForkJunction.cs`, `OM.Splitcrown.cs`, `OM.Lacemantle.cs`, `OM.Cragparasol.cs`, `OM.NewTreesA.cs`, `OM.NewTreesB.cs`, `OM.Hollowcrown.cs`.

**F2. Ground contact on slopes.** SHARED. Evidence: single sample `OrganismRenderers.cs:826`; no slope tilt for upright (`:850-857`); generators stop at y=0 (`Veilfern.cs:430-433`). Species: every upright plant, worst for wide bases (trees, ferns, fungus troops). Cause: unit mesh with flat base plane at centre height. Remedy: in `SamplePlantPlacement` (`:1166-1205`) take 4 terrain samples at 0.6*base radius and set `GroundY = min - 0.01`; replace Veilfern's y>=0 clamp with a buried skirt to -0.03. Later option: vertex-shader snap of low vertices to terrain. Validate: `--visual-review --preset steep_rocky_slope` `foliage_terrain` shot (specimen review uses flat cells only, cannot test). Size M. Files: `game/Render/OrganismRenderers.cs`, `OM.Veilfern.cs`, `OM.NewTreesA.cs`.

**F3. LOD pops: hard tier swap at 3 m / 5.5 m with leaf and part culling.** SHARED. Evidence: `FloraLeaf.cs:16-38` (DistanceMid 3 m, DistanceFar 5.5 m in an 8 m diorama; 210/75 px), no cross-fade (`OrganismRenderers.cs:1440-1480`); `FinishTier` drops leaves, minor tubes and loose parts by heuristic down to 14% of triangles (`OM.VisualBreakFlora.cs:260-373`); `Tube` decimates rows/sides (`Primitives.cs:102-108`). Species: all, worst for dense foliage (veilfern, trifold, coinrunner, hookthicket, trees). Remedy: after choosing dropped parts in `FinishTier`, scale each KEPT leaf span about its `Attachment` by `sqrt(1/keepFraction)` capped 1.6 to preserve foliage mass; never drop a petiole whose blade is kept; widen hysteresis. Validate: review runners lack a tier override; add env `VIVARIUM_FLORA_TIER=0|1|2` read in `VisualTier` and capture the same specimen at each tier. Size M. Files: `OM.VisualBreakFlora.cs`, `OrganismRenderers.cs`, `SpecimenReviewRunner.cs`.

**F4. Bark texture seams and inconsistent texel density.** SHARED. Evidence: shader `buv = (UV.y, UV.x*repeats)` (`flora.gdshader:259-262`, comment says UV.x in metres, UV.y 0..1 around); `Wood()`/`WoodTube` pass metres (`OM.cs:5045`, `OM.NewShrubs.cs:57`) but `OM.NewTreesA.cs` passes normalised t at ~16 sites (`:694, 907`); v is 0..1 around any circumference, so a 20 cm trunk and a 5 mm twig get the same texture width; UV restarts per tube. Species: all woody + climbers. Remedy: in `Prim.Tube`, when the attr callback marks bark (u2>1.5), overwrite UV: U = arc length in metres, V = v * round(2*pi*r/0.12) (integer so the seam closes; texture is `repeat_enable`), with optional `uStart` for children. Validate: macro shots of moonbark, shiverleaf, fenforge, hollowcrown trunks. Size M. Files: `Primitives.cs`, `OM.NewTreesA.cs`.

**F5. Open tube ends.** SHARED. Evidence: `Primitives.cs:92-183` emits no caps; 360 call sites. Visible at trunk tops between boughs, stubs, stalk tips under flowers, buttress tips. Remedy: append a collapsed closing ring at the tip (mid ring 0.8r at +0.25r, end ring radius 0 at +0.5r) INSIDE the same `Structural` span so `vertexCount % (sides+1) == 0` still holds (fruit anchoring `OM.cs:352-370` and `FinishTier` petiole pairing `owner[end]` depend on that); zero-area triangles are removed by `FinishTier`. Base rings stay open. Validate: macro shots; compare triangle counts in the build log. Size M. File: `Primitives.cs`.

**F6. Blade base is a zero-width point; petiole end open.** SHARED. Evidence: `Broadleaf.cs:41-90, 132-151, 190`; `Primitives.cs:210`; `LeafBlade.cs:104-110`; `OM.NewTreesA.cs:200-236`; sliver fan `Broadleaf.cs:51`. Species: ~50 broadleaf species. Remedy: base seat in each blade builder: `width = max(env*hw, r_pet*(1-t/0.05))` for the first 5%; fade `lobeBack` in with `smoothstep(0,0.08,t)`; ramp petiole vertex colour to blade base colour (`Broadleaf.cs:149`, use the per-ring `attr`); this also hides the stem/leaf shader seam. Validate: macro of starveil, butterglint, mistviolet, hushclover, brooklace, shadebell. Size M. Files: `Form/Broadleaf.cs`, `Primitives.cs`, `Form/LeafBlade.cs`, `Form/FoliageBlade.cs`, `OM.NewTreesA.cs`.

**F7. Pilot tree roots: separate flat-shaded mesh pasted on terrain.** SPECIFIC (hero object). Evidence: `PilotTreeMeshes.cs:154-205`, `PilotTreeRenderer.cs:73-77`. Remedy: ramp root height/width to the trunk flare radius over the first 3 samples, share smooth ring normals, overlap the trunk flare onto the first root sample. Validate: `--visual-review` `overview` plus a low close shot (specimen runner hides the pilot tree). Size M. Files: `PilotTreeMeshes.cs`, `PilotTreeRenderer.cs`.

**F8. Fruit attached to any tube, hard mesh swap.** SHARED routine, 8 species (cinderberry, cragparasol, glowbriar, lacemantle, shadebell, splitcrown, thornmantle, witchberry). Evidence: `OM.cs:343-560`. Remedy: restrict anchors to tubes of radius 0.25-0.8 of the max with >=5 sides; add a calyx ellipsoid at the pedicel top; consider an additive fruit layer instead of a full swap. Validate: specimen shots with fruiting forced (needs a forced-stage option). Size S-M. Files: `OM.cs`, `OrganismRenderers.cs`.

**F9. Lobed vine leaves unwebbed at the base.** SPECIFIC: six climbers (`OM.NewVines.cs:65-117, 160-215`). Remedy: add an ovate core blade (length 0.45*main, half-width 1.4*mainHalfW) before the lobes. Validate: macro of shadewrap, bitterbine, trumpetbind on a moonbark host. Size S.

**F10. Climber contact only on 3 hosts.** SHARED. Evidence: `FloraRenderer.WoodyContact.cs:13-14`, `OrganismRenderers.cs:403, 712`. Remedy: `IsContactHostShape` = `species.Woody != null`, build `StructuralSurfaceIndex` for all woody tier-0 meshes (memory cost to measure). Validate: specimen-review of climbers (hosts are fixed to the three; add a host argument). Size S.

**F11. SoftTube pulvinus/collar bulges on every stalk.** SHARED, few callers. Evidence: `SoftTube.cs:44-49`. Remedy: make them opt-in parameters, default 0. Size S. Files: `Form/SoftTube.cs`, `OM.NewForbsA.cs`, `OM.cs` herb case.

**F12. Non-uniform growth scaling.** SHARED. Evidence: `OrganismRenderers.cs:956`; only ~25 species have `J` meshes. Stem radius scales with `r`, length with `h`, leaves shrink with the plant. Remedy: for non-tree upright plants blend toward uniform scale `sqrt(r*h)` and keep a leaf size floor. Do after F1-F6 (visible re-proportioning). Size M. File: `OrganismRenderers.cs`.

**F13. Water-depth Y stretch.** Species: glasslily, mirrorleaf (`OrganismRenderers.cs:827-845`), pondstars, bogfringe, deepquill, mirepocket. Unit Y = water depth stretches flowers, scroll leaves and pad camber (`OM.Glasslily.cs:13-15, 355`). Needs render. Size M.

**F14. Wind mismatch petiole vs blade.** Stem displaces by `VERTEX.y*4`, blade by `UV.x` with different amplitudes (`flora.gdshader:95-110`, `leaf.gdshader:49-72`): sub-mm, macro only. Remedy: weight both by arc distance from the attachment. Size S.

**F15. Distinctiveness (not construction).** Shared leaf pools (4) and bark sets (3) for ~100 species; repeated skeletons (1a). Remedy: bake extra leaf pools for the most-seen families (`LeafMaterialBaker.cs`, `--flora-bake`) and vary skeleton parameters per species (fork height, bough count, crown profile). Size L. No wholesale geometry replacement (ledger D5).

Shared infrastructure: F1 (helper), F2, F3, F4, F5, F6, F10, F11, F12, F14 (F8 is a shared routine for 8 species). Species-specific: F7, F9, F13, F15.

## 4. Needs a render to confirm
- Magnitude of F2 on this island's slopes (code says `Terrain.Height` matches the mesh diagonal, `Terrain.cs:178-186`).
- Whether F1 beads and F5 open ends are visible at specimen macro distance (0.2-3 m) or only closer.
- Visibility of the cordate sliver fan (`Broadleaf.cs:51`) and the TaLightLeaf upward-normal bias (`NewTreesA.cs:188-198`).
- Climber node stub vs vein-tube radius step; vine lobe gaps.
- Water-depth stretching on the six aquatics at depths 0.1 and 0.9 m.
- LOD pop magnitude at 3 m; whether tier 1 orphans petioles (depends on vertex order per generator).
- Rachis-to-pinna float (PinnateFrond) at 4-5 sided rachis.
- Whether skeleton-template species are distinguishable at overview distance.
- Pilot-tree root/trunk seam (F7).
