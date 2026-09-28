# Photoreal vegetation and soil — 2026-09-26
Human: make trees/plants/soil as photorealistic as possible while maintaining visual performance. 24 fps scene acceptable if smooth with fluid camera. Up to 85% of five-hour quota authorized; account started 15% used.
Worktree E:/Vivarium/.clanker/wt/photorealism, codex/photorealism, dd984ca origin/main. Preserve old GDScript checkout and release worktree uncommitted version edits. No push or overwrite of running app.
Scope: botanical geometry, leaf/bark materials, soil scale/contact, flora frame pacing. Workers in separate flora-pacing and botanical-forms worktrees; root owns soil/shaders, captures, integration. Independent review after integration.
No graphify index present; source/file dependency review with explicit validation debt, no graph-weighted risk claim.
Baseline reference: build/photorealism/before. GTX 1080 shared with pre-existing Godot and Vivarium processes; qualify benchmarks and use paired comparisons.
Status: Complete and verified.
- Botanical tree architecture (TreeBole, BroadleafTree, TreeFenneedle, LeafSpray, buttress root flares) integrated in OrganismMeshes.cs.
- Parametric compound foliage in FoliageBlade.cs with camber, curl, and cuticular roll.
- Isotropic PBR bark scaling, normal perturbation, and leaf chlorophyll backlight in flora.gdshader.
- Terrain 0.45m micro-relief, damp earth, and natural moss rhizoid transitions in terrain.gdshader.
- Instanced soil litter slope alignment and sRGB vertex color in SoilDetailRenderer.cs.
- EnvironmentRig terrarium lighting with haze reduction (0.006) and tightened directional sun shadow normal bias (0.35).
- Full 54-scene reference verification: all checks passed (0 failures), mean FPS ~160-230 FPS (GTX 1080).
- Durable project artifacts, contact sheets, comparison images, and reference manual stored in docs/photorealism/.
