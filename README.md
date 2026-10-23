# Vivarium

A living, self-running miniature ecosystem on a hexagonal island. Moss and lichen creep outward from their edges, slime mold hunts through the leaf litter, springtails graze, shrimp and triops work the pond, water cascades through dynamic streams into deep pools, and ancient Guardians roam the landscape. You shape the terrain, pour water, place springs, drop in rocks, logs and gravel, and introduce species—then watch the ecology unfold. Nothing is scripted. Every organism follows its own habitat preferences, competes for space and light, eats, breeds, and dies.

![Windows](https://img.shields.io/badge/platform-Windows%20x64-blue) ![Godot 4.7.1](https://img.shields.io/badge/Godot-4.7.1%20mono-478cbf) ![.NET 8](https://img.shields.io/badge/.NET-8-512bd4) ![Simulation](https://img.shields.io/badge/simulation-deterministic-green)

---

## Download and Play

Grab the latest `Vivarium-x.y.z-win64.zip` from the [Releases](https://github.com/bobcatchris15-eng/Vivarium/releases) page, unzip it anywhere, and run `Vivarium.exe`. No installer required.

- **System Requirements:** Windows 10/11 x64, Vulkan-compatible GPU (developed and tested on integrated and discrete GPUs, running Godot 4.7.1 Forward+).
- **User Data:** Saves, preferences, and logs live in `%APPDATA%\Vivarium\`. The current world format uses **Save Schema 4**.

---

## Controls & Interaction

| System | Input | Description |
|---|---|---|
| **Camera** | **Right Mouse Hold + Drag** | Look / rotate view |
| | **W A S D** | Fly forward / left / backward / right |
| | **Space / Ctrl** | Fly up / down |
| | **Q / E** | Turn left / right |
| | **Shift / Alt** | Fast boost / Slow precision fly |
| | **Mouse Wheel** | Zoom toward cursor |
| | **+ / -** | Adjust base flight speed |
| | **F** | Focus and orbit selected organism or prop |
| | **Home** | Reset camera view |
| **Time** | **P** | Pause / resume time |
| | **, / .** | Decrease / increase simulation speed (1 real hour ≈ 1 vivarium week) |
| **Tool Wheel** | **Right Click (Tap)** | Open / close circular tool wheel; number keys also select directly |
| | **1** | **Inspect:** View genome, lineage, metabolism, and stats |
| | **2** | **Grab:** Pick up a critter and relocate it |
| | **3** | **Pick Plant:** Remove an organism |
| | **4** | **Nutrients:** Enrich or deplete soil nutrients (`[` / `]` brush radius) |
| | **5** | **Poke:** Physical interaction / nudge |
| | **6** | **Rock:** Place boulders (`[` / `]` scale, stacks up to 3 high) |
| | **7** | **Log:** Place fallen nurse logs (`R` rotates heading, `[` / `]` length, stacks up to 3) |
| | **8** | **Gravel:** Scatter river pebbles and gravel beds (`[` / `]` radius) |
| | **9** | **Introduce Flora:** Select and plant botanical species from catalog |
| | **0** | **Introduce Fauna:** Select and release animal species from catalog |
| | **G** | **Terrain Sculpt:** Cycle **Raise** → **Lower** → **Smooth** (`[` / `]` radius) |
| | **H** | **Hydrology:** Cycle **Pour Water** → **Drain Water** → **Place Spring** (`[` / `]` flow rate) |
| | **Delete** | Delete selected prop (Rock, Log, Gravel) |
| | **X / Esc** | Cancel active tool / drop held object |
| **Overlays** | **C** | Species catalog |
| | **T** | Island-wide ecology statistics |
| | **Ctrl+S** | Quick save |
| | **F3** | Toggle diagnostic & performance overlays |
| | **F1** | Help / keybinding overlay |
| | **Tab** | Hide all UI for cinematic viewing |

---

## What's in the Ecosystem

The simulation contains **62 species of Flora**, **17 species of Fauna**, and **6 Ancient Guardians**, interacting in a continuous food web and nutrient cycle.

### Ancient Guardians (6)
Colossal, biomechanically articulated keystone creatures that roam the island, interact with terrain, and shape the ecological balance:
1. **Armored Forest (`armored_forest`)**: Heavy subterranean excavator with articulated keratin scales and excavating claws (pangolin morphology).
2. **Crested Canopy (`crested_canopy`)**: Arboreal frugivore with multi-layered plumage, horn bill, and orbital facial crests (hoatzin morphology).
3. **Great Ground (`great_ground`)**: Massive terrestrial apex reptile with anatomical scutes, claw articulation, and lateral locomotion (monitor lizard morphology).
4. **Canopy Serpent (`canopy_serpent`)**: Slender, arboreal predator adapted to high branches and climbing vines.
5. **Quilled Arboreal (`quilled_arboreal`)**: Defensive canopy dweller with erectile quill coats and gripping claws.
6. **Woodland Omnivore (`woodland_omnivore`)**: Generalist forager dispersing seeds and aerating leaf litter.

### Flora (62 Species)
Plants possess realistic morphology, branching architectures, phototropic orientation, and life stages:
- **Mosses & Groundcarpets:** Velvetweave, Pearl-cushion, Floodlace, Bogglass, Starmoss.
- **Lichens:** Embercrust, Ruffle, Antlerlace.
- **Low Ground Cover & Rosettes:** Coinrunner, Mooncoin, Trifold (clover), Glassfinger, Sunstone rosette, Frosttussock, Silverweed, Fenbead, Marrowbloom, Siltstars.
- **Wetland & Aquatic:** Glassrush, Brooklace, Fencomb, Mirrorleaf, Ringreed, Streamribbon, Tidebell, Lanternsedge.
- **Understory Herbs & Ferns:** Veilfern, Vaultfern, Moonfern, Prismstar, Rainshield, Tankbloom.
- **Canopy Trees:** Ironlace, Umbraheart, Fenneedle, Kiteleaf, Blackneedle, Coalspike.
- **Shrubs & Understory Bushes:** Embercrown, Lanternbrush, Shadebell, Glowbriar, Hookthicket, Carrion Bell.
- **Searching Climbers & Vines:** Clinglace, Spiralvine, Fenhook, Lacemantle, Kinkcane. Runners actively seek supports (trunks, rocks, logs) before transitioning to vertical ascent.
- **Fungi & Carnivorous Plants:** Dewbonnet, Ghostbonnet, Emberfan, Rain Jelly, Starbulb, Bonecup, Cragparasol, Hollowcrown, Pitcher Plant, Snaptrap, Blue Sundew.

### Fauna (17 Species)
Fauna carry heritable genomes governing pigmentation, pattern markings, scale size, appendage proportions, and metabolic efficiency:
- **Litter Grazers & Decomposers:** Prismhoppers, Marblebacks, Ghostbristles, Coalback beetles, Dewmantles, Rustcoils, Loamthreads.
- **Active Hunters & Apex Predators:** Stiltclaws, Rainspines, Stonebells.
- **Aerial & Canopy Movement:** Moonveils, Duskflickers.
- **Aquatic & Pond Life:** Emberglass swimmers, Siltshields, Glintfins, Glasscoils, Reedjaws.

### Dynamic Hydrology
Vivarium features a dual-layer water system:
- **Groundwater Hydrology:** 2D moisture wicking and soil saturation governed by hydraulic conductivity, evaporation, and root absorption.
- **Dynamic 3D Surface Fluids:** Real-time shallow-water equations (`WaterSurfaceRenderer`, `surface_fluid.gdshader`, `BallisticFluidRenderer`) modeling free-flowing stream beds, waterfalls, splash sheets, and spring discharges.

---

## Architecture & Code Layout

```
Vivarium/
├── src/
│   ├── Vivarium.Sim/       # Authoritative, deterministic simulation (C# net8.0).
│   │                       # Pure logic: terrain heightfields, hydrology, flora growth,
│   │                       # genetics, food web, procedural geometry. ZERO Godot dependencies.
│   └── Vivarium.Cli/       # Headless CLI runner for batch simulations, soak tests, and digests.
├── game/                   # Godot 4.7.1 Mono client (C# net8.0).
│   ├── App/                # Session lifecycle, tool controller, camera, visual reviews.
│   ├── Render/             # Scene renderers: Island, Water, Organisms, Props, Guardians, Sky.
│   ├── Shaders/            # Godot Forward+ shaders (.gdshader / .gdshaderinc).
│   ├── Textures/           # Surface PBR materials, HDRI panoramas, leaf pools.
│   ├── assets/             # 3D models (.glb), guardian rigs, animations.
│   └── content/            # All species, substrate layers, and presets as JSON.
├── tests/                  # xUnit test suites validating simulation determinism & ecology.
├── scripts/                # Verification, build, packaging, and diagnostic automation.
└── docs/                   # Authoritative architecture, ADRs, product contract, and plans.
```

- **Simulation Authority:** `src/Vivarium.Sim` is the sole source of truth. The Godot client is strictly an observer and command dispatcher. Rendering code cannot alter simulation state or compromise determinism.
- Detailed architecture: [docs/architecture/architecture.md](docs/architecture/architecture.md)
- Product requirements: [docs/product/vivarium-contract.md](docs/product/vivarium-contract.md)

---

## Active Roadmap: Photorealistic Rendering Overhaul

The active engineering plan to elevate Vivarium's rendering into an uncompromising photorealistic presentation is defined in:

📄 **[docs/overhaul/PHOTOREALISTIC_RENDERING_PLAN.md](docs/overhaul/PHOTOREALISTIC_RENDERING_PLAN.md)**

Organized into **24 atomic Work Packets (`WP-01` through `WP-24`)** across 8 phases:
- **Phase 1:** Engine GI (SDFGI, SSR, high-quality SSIL/SSAO), 8K cascaded shadows, HDRI sky radiance, volumetric Tyndall rays, and physical camera bokeh DOF.
- **Phase 2:** 4K 5-channel lossless PBR texture pipeline (`Color`, `NormalGL`, `Roughness`, 16-bit `Displacement`, `AO`), 32-step Parallax Occlusion Mapping, and 3D cutaway strata.
- **Phase 3:** High-density forest floor litter streaming, multi-layer duff, 3D soil aggregates, and bank fringe foliage.
- **Phase 4:** Forward+ lit water with analytic B-spline normals, world-space caustics, and plunge-pool foam.
- **Phase 5:** High-poly geological boulders, decaying hollow nurse logs, and dense riverbed gravel.
- **Phase 6:** Photorealistic 3D gametophyte moss cushions, foliose lichens, and biofilm wetness.
- **Phase 7:** Dual-layer chlorophyll subsurface scattering, true-thickness leaves, reaction-diffusion venation, and procedural pilot tree crown rebuild.
- **Phase 8:** Subsurface scattering fauna, 2-bone IK guardian contact, and procedural fur/feather/scale shaders.

---

## Tooling & Authoring Pipeline

In addition to standard Godot and .NET tooling, Vivarium leverages dedicated authoring and AI-driveable systems:
- **Artcraft (`artcraft.exe`):** AI-driveable 2D creative and material suite used for synthesizing 2K/4K tileable PBR texture sets, cuticle roughness profiles, leaf venation maps, and micro-litter atlases.
- **SolidCraft (`solidcraft.exe`):** Parametric CAD / B-rep solid modeling engine controlled via a local JSON-RPC socket (`127.0.0.1:7878`), generating continuous geological rock fracture models, hollow nurse logs, and articulated guardian carapaces.
- **Blender 5.2.0 LTS:** High-poly prop sculpting, skeletal rigging, and Cycles HDRI ground-truth baking.
- **Material Maker:** Procedural graph authoring for complex surface materials.
- **Python 3.14:** Visual Lab probes, photographic light validation, and automated screenshot comparison harnesses.

---

## Building & Verifying From Source

### Prerequisites
1. Windows 10/11 x64
2. [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
3. [Godot 4.7.1-stable mono](https://godotengine.org/download/archive/4.7.1-stable/) placed in `tools/godot/Godot_v4.7.1-stable_mono_win64/` (or set `VIVARIUM_GODOT` to its executable path)
4. PowerShell 7 (`pwsh`)

### Build
```powershell
# Build simulation logic, CLI, and test assemblies
dotnet build Vivarium.sln

# Build Godot C# game assembly
dotnet build game/Vivarium.csproj

# Launch in editor or standalone
tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64.exe --path game
```

### Test & Verify
```powershell
# Run all fast simulation suites plus Godot boot and UI smoke tests
./scripts/verify.ps1

# Run specific suites
./scripts/verify.ps1 -Suite Flora,Hydrology

# Full validation including packaging and render screenshot tour
./scripts/verify.ps1 -Suite All -IncludeSlow
```

### Export Release
```powershell
./scripts/export.ps1
# Produces build/release/Vivarium/ and build/Vivarium-<version>-win64.zip
```

---

## Third-Party Notices & Licenses

- Vivarium's codebase, simulation kernel, and procedural mesh generators are original to this project.
- Engine: Godot Engine (.NET) — MIT License.
- PBR Source Assets: CC0 scans from ambientCG and PolyHaven.
- See [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md) for full notices. All rights reserved by default.
