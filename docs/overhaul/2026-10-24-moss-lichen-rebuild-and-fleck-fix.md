# Moss/lichen renderer rebuild and chromatic fleck fix — 2026-10-24

Status: implementation, matched defect controls and final native regression captures complete. First-pass species forms are ready for user art review; artistic and performance acceptance remain separate.

## Confirmed reproduction

The six user screenshots show intermittent compact magenta/black blocks, with a pale halo when out of focus. They occur around covered fallen logs and elsewhere. Earlier close or isolated tests were not adequate reproductions. A far reverse-side log view reproduced the matching purple block at approximately (637, 286) in a 1600×900 native capture. The core includes pixels such as (42, 0, 42): red and blue survive while green is zero.

Diagnostic localization (`log-flecks-localize-02`) retained the block with Flora, Props and Water individually hidden, and with glow, SSIL, SSR and SDFGI individually disabled. Hiding Coverage removed it. Disabling MSAA removed it in the sample. Disabling DOF reduced the colored footprint to a tiny cluster; disabling bokeh jitter did not remove it. The earlier footprint was up to 44 purple pixels. These are sampled candidates and a confirmed local defect, not a statistical guarantee about every image artifact.

The first localization captures are retained as diagnostic evidence only: unrelated C# geometry files changed during those runs, failing the source-stability gate. Subsequent matched tests use a private frozen source tree so ongoing work in the shared checkout cannot invalidate the code comparison. Source/config files are copied; large immutable asset/cache inputs are hardlinked and remain covered by before/after hashes.

## Matched causal control

`moss-rebuild-baseline-02`, `moss-srgb-clamp-only-03` and `moss-rebuild-world-01` each produced 53 PNGs with source stability, exit 0 and zero engine errors. Their canonical world digest and camera eye/target match. The saved world is the normal startup autosave snapshot; no simulation or save data is changed during capture. Diagnostic loading now pauses the loaded world immediately, avoiding variable simulation steps before the review starts.

The baseline reproduces the block in 2/12 baseline frames and 2/12 restored frames, up to 52 purple pixels in this matched view. The old renderer with **only the shared sRGB input clamp** shows no tracked purple pixels in the sampled fixed views. Comparing source manifests identifies exactly one changed file: `game/Shaders/surface.gdshaderinc`. No normal, geometry, light, AA or DOF change was included in that causal control. The separate rebuilt-renderer capture also has zero tracked purple pixels in all 36 fixed-view samples (baseline, DOF off and restored, 12 each).

The old sRGB conversion evaluated fractional `pow()` before `mix()` selected the linear branch. Negative extrapolated inputs can put that expression outside its real-valued domain. Clamping the input to nonnegative values removes that hazard and fixes this reproduced case while retaining MSAA and bokeh. The experiments did **not** read back the intermediate HDR NaN values or trace the engine's exact UNORM/tonemapper conversion, so that last GPU conversion mechanism is not presented as directly measured proof.

`moss-srgb-clamp-only-01` exited before producing images and is excluded. Attempt02 could not acquire the GPU lease and is excluded. The wrapper now rejects zero-image/incomplete world runs and can wait for the shared GPU lease without disturbing another capture.

## Numerical repairs

- Shared `srgb_to_linear` and the litter-specific equivalent clamp negative inputs before fractional power.
- `finite_surface_normal` now validates and normalizes its fallback too. The legacy coverage and log paths use it for reconstructed normals, tangent frames and half vectors; the log Fresnel cosine is bounded before power.
- Leaf direct lighting validates color/attenuation inputs, reconstructs finite unit directions, rejects invalid contributions and bounds material masks. Existing finite, physically valid inputs retain their material response.

These repairs accompany the new renderer; they are not a reason to disable MSAA, TAA, FXAA or gameplay depth of field.

## Ground-up moss/lichen presentation

The active terrestrial path now uses physical opaque cell contours and deterministic closed clumps instead of the old alpha-discard/interpolated-pigment shader. Ecological occupancy, biomass, moisture, dormancy, dead flags and growth rules remain authoritative. Biomass and dormancy drive presentation size/color; no visual mass is written back to the simulation.

Seven forms have separate geometry: bogglass upright branched shoots, pearl cushions, forked floodlace fronds, short velvetweave rosettes, branching antlerlace, broad ruffle lobes, and embercrust pads with raised fruiting discs. The previously rebuilt sundew keeps its separate clump/tissue path; aquatic coverage retains its own renderer.

Species and organ identity select actual draw surfaces. The new shader uses uniform pigment, bounded health/life interpolation, restrained scan luminance, ordinary rough diffuse response, finite normals and no animation. It does not use interpolated vertex pigment, alpha-discard edges, custom direct lighting or legacy microsurface branch chains.

For log contact, a render-only projected-triangle sampler queries the actual generated bark mesh, including taper, bending and knots. The ecology's ideal-cylinder `LogProp.TopAt` remains unchanged. Adaptive small triangles follow support surfaces and reject discontinuities; steep unsupported edges do not become large curtains.

This is a first procedural art pass, not a claim of photographic final assets or improved FPS. Per-clump budgets are bounded, but aggregate triangle/draw cost still requires its own measurement. The older terrestrial implementation remains available only through an explicit diagnostic CLI flag for matched A/B comparisons.

## Verification and outstanding review

Focused geometry/sampler/sundew checks passed 20/20: determinism, finite positions and unit normals, nondegenerate triangles, Godot winding, meter-scale bounds, per-organ markers, triangle budgets, projected support interpolation and real-log sampling.

Final `moss-rebuild-world-03` compiled all guards and refined geometry, captured 53 PNGs with stable source, exit 0 and zero engine errors, and matches the old/clamp-only camera and canonical world digest. The tracked purple defect is absent in all 36 fixed-camera frames. The compact-purple candidate scan found zero candidates across all 53 images; this color detector does not prove the absence of every possible black/cyan defect.

Final `moss-rebuild-isolated-02` captured 144 PNGs across all seven rebuilt species and the sundew, with stable source and zero engine errors. The final focused geometry tests pass 20/20 (`build/overhaul-checks/moss-lichen-geometry-tests-02.log`). Temporal triage scored all rebuilt moss/lichen fixtures zero; the sundew has one candidate, which is not by itself a confirmed rendering defect. The gallery records actual fixture triangle counts and whether old/new ecological fixture digests match.

Visual inspection confirms the separate forms and continuous patch surfaces. Remaining art limitations are conspicuous grid-shaped patch outlines, faceted small organs and a fairly uniform basal surface. These are explicitly first-pass assets for review, not photographic acceptance.

Production renderer/geometry/shader files match the tested frozen snapshot byte-for-byte. Main.cs differs only by a concurrent, unrelated `--flora-profile-check` diagnostic branch, which was preserved. Snapshot results establish this subsystem against the recorded sources, not a regression test of every concurrent checkout change.

Review: `build/photorealism/moss-lichen-rebuild-review-20261024/index.html`; machine-readable evidence includes `comparison-proof.json`, `species-proof.json`, and `production-source-proof.json`. The existing seven-Texture-RID shutdown warning persists in native world runs. No performance acceptance or whole-plan visual acceptance is claimed.

The independent performance chat owns its GPU experiments; these captures wait for the shared lease. Its editor closures and unrelated source changes are preserved. No commits, publication, save writes or cleanup of unrelated work are part of this change.
