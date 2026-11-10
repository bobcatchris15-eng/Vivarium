# Vivarium overhaul — orchestrator ledger
Updated: 2026-11-10 | HEAD: 442952e | Graph: none | Checkpoint tag: overhaul-base

## Objective
Make Vivarium physically continuous, biologically distinctive, efficient and pleasant to use, per the
owner's reconstruction directive: remove visible gaps and artificial joints, give every species a
believable/integrated/distinctive identity, cut measured CPU/GPU/memory cost at equal or better visual
quality, overhaul UI/UX. Evidence (renders, measurements) required for every claim. Record in `docs/overhaul/`.

## Decisions
- D1 2026-11-10: Repo is `C:\Misc\Vivarium` (directive says E:\; owner confirmed C:).
- D2 2026-11-10: Recreated CLAUDE.md with a VALIDATE block; upstream 9650e95 deleted it, the tests and the
  scripts. Validation is build + headless boot-test + in-game review runners. Revisit if owner restores tests.
- D3 2026-11-10: Mode S for all edits (22 GB free, one GPU). Read-only audits run concurrently because they
  write only their own new file under docs/overhaul/ and never run Godot. Only one Clanker may run Godot at a time.
- D4 2026-11-10: Owner rule (restated on resume) — dispatch only haiku or sonnet at low/medium effort; lightest that can reliably do it.
  sonnet/medium for edits that need Godot iteration, sonnet/low for mechanical edits, haiku for re-validation and capture runs. No opus Clankers.
- D6 2026-11-10: Packets carry a `Read_Only_Context` list (sim/render files the Clanker may read, not edit). h1 attempt 1 stopped with NEEDS_NODE because impl-worker may not read outside Target_Nodes.
- D5 2026-11-10: Wholesale botanical geometry replacement previously made visuals worse (directive). Fix
  junctions/attachments in place; replace a species' geometry only with before/after renders approved here.
- D7 2026-11-10: Owner: editors may give any species its own material, normal map, textures or shader params. Target look is a lifelike, believable, slightly fantastical/whimsical slice of an ecosystem. Shared leaf pools/bark sets are not a constraint; weigh per-species assets against measured VRAM/draw cost.

## Tasks
| id | targets | status | attempts | last return line |
|----|---------|--------|----------|------------------|
| b0 | build/overhaul/baseline, docs/overhaul/baseline.md | DONE | 1 | 3 visual, 126 specimen (63 species only), 30+30 guardian PNGs. Soak steady ~25 fps, p50 38.6 ms, p95 52-59, 707 draws, 7.17M prims, VRAM 4.2 GB, working set 7.6 GB. Close-ups 12-13 fps at ~10M prims. CPU max: Coverage 25-27 ms, LiquidSurface 8-9, Session.Advance 7.5, Guardian.Vegetation 5.5. --perf-test has no runner; --soak never exits; 7 texture RIDs leak at exit; 0/2 starter Burlmantle placed |
| a1 | docs/overhaul/audit-flora.md | DONE | 1 | 125 species (106 MultiMesh flora, 19 coverage). top: F1 fork junctions full-radius children + open trunk top (SoftTube fillet unused), F2 single terrain sample ground contact, F3 hard LOD swaps 3m/5.5m, F4 bark UV seams/stretch, F5 Primitives.Tube no end caps (360 sites), F6 blade base zero-width + open petiole. Shared: F1-F6 F8 F10-F12 F14. All vascular share 4 leaf pools + 3 bark sets. Review runner cannot test slopes or force LOD tier |
| a2 | docs/overhaul/audit-fauna-guardians.md | DONE | 1 | 17 fauna (own mesh builders, shader-animated), 10 guardians (skinned GLB). top: G1 clip chosen by Activity not motion (sliding), A1 fauna constant-speed random walk, G3/G4 foot slide + slope hover (G4 unconfirmed), G2 21 activities on 4 clips, G5/G6 water+veg contact, A2/A5 fauna feet rigid tilt. A0: no fauna review harness (specimen-review is flora-only) |
| a3 | docs/overhaul/audit-ecology.md | DONE | 1 | top: E6 coverage moisture never decays (persisted), E1 fauna budget first-come lock-out, E2 predation never kills, E7 trait variance collapses, E3 nutrient loop creates mass, E12/E13 flora+ambient tick cost. Inferred from code; needs --soak to confirm. Headless Step leaves water frozen |
| a4 | docs/overhaul/audit-performance-architecture.md | DONE | 1 | static only. SAFE: P1 env params rewritten per frame, P2 caustics in per-light light(), P3 sculpt full terrain+pond rebuild, P11/R4/R6 4K sets + dead code (GillTrial, BranchModuleLoader). DECIDE: P12 post/GI/fog stack, P13/R2 ambient cover regen ~115k pts per 2m move, R1 snapshot only used by flora + coverage thread race, R3 guardian bends written into flora MultiMesh. R9: add GPU timer, alloc bytes, instance/VRAM counters |
| h3 | game/App/{Main,LiveDiagnostics,FrameProfiler}.cs | DONE | 1 | PASS. soak 150s/warmup 90: 24.2 fps, p50 39.9 p95 69 ms, GPU frame 39.4 ms (GPU-bound), VRAM 4.46 GB, 8.3 MB managed alloc per frame, gc 222/204/72 in 60 s |
| h1 | game/App/SpecimenReviewRunner.cs Main.cs | DONE | 2 | PASS, re-verified (build, boot, counts). 125/125 flora (375 png, 572 s), 17/17 fauna (51 png, 197 s), slope 25.6 deg. specimens.json is now an object. Unverified: _fruiting never produced; _juvenile not checked distinct; fauna+slope not re-run after last edit |
| h45 | game/App/{GuardianReviewRunner,VisualReviewRunner}.cs | DONE | 1 | PASS, re-verified (build, boot, counts, sheets). Guardians 10 species x 10 shots (closeup/side/feet/feed/motion 0-5), 118 s; slope mode 18 png at 48 deg. Visual 9 scenes, near-black repick, review.json records camera + near_black_fraction. Limits: island is 8 m, so meadow/woodland shots show it small; woodland_trunks has pilot trunk column; feet/motion shots hide flora+cover layers |
| b2 | build/overhaul/baseline2 | DONE | 1 | PASS, verified counts. flora 375, slope 375, stages 453 (10 fruiting, 16 juvenile), fauna 51, guardians 100+18, visual 9. Soak x2: 26.4/27.2 fps, p50 36 ms, p95 57-63, GPU 35 ms, 705 draws, 7.15M prims, VRAM 4.46 GB, alloc 7.6-7.8 MB/frame, gc2 82-86 per 60 s |
| h6 | game/App/{FrameProfiler,LiveDiagnostics,Main}.cs | DONE | 1 | PASS, re-verified build+boot. --soak-hide works (flora,coverage: 6.03M->5.36M prims, GPU 33.4->31.3 ms, short runs). Alloc 7.25 MB/frame: Coverage 0.68, Session.Advance 0.61, Flora 0.55, LiquidSurface 0.06 MB; 5.3 MB (74%) outside any scope -> needs more scopes |
| m1 | build/overhaul/m1 | DISPATCHED | 1 | haiku: per-renderer hide soaks, 2 interleaved rounds |
| a5 | docs/overhaul/audit-ui-ux.md | DONE | 1 | top: U1 Tab-hide swallows clicks, U2 no Esc/blocked keys, U3 bad autosave silently replaced, U20 per-frame window relayout, U6 scale slider font-only, U7 invalid-placement reason hidden. Owner decisions: U5 U6 U13-U18 U23 U24 |

## Unverified assumptions
- 173 locally modified `.import` files are line-ending noise except 3 crested_canopy files; never stage them.

## Orchestrator render review (baseline sheets, 2026-11-10)
- visual-review has only 3 scenes; `overview` shows the island at ~5% of frame, `grazing_transition` has black void at frame edges. Needs more, better-framed scenes (H4).
- guardian-review "closeup" puts the animal at ~10% of frame width; joints and feet cannot be judged. Needs tight framing + motion sequence (H5).
- specimen-review covers 63 of 106 MultiMesh flora species; trees appear to be missing. Litter ground reads as a repeating orange leaf tile; fungi caps look smooth/plastic.

## RESUMED 2026-11-10 (was paused after h1)
Order: H4/H5 (guardian tight framing + motion sequence; more and better-framed
visual-review scenes) -> full recapture as baseline2 -> perf attribution per renderer (GPU-bound, 8.3 MB/frame
managed alloc) -> Batch 1 (F5, F1, F6, F2, F4, F8, F3). Owner decisions still open: see docs/overhaul/README.md.

## OVERNIGHT PLAN 2026-11-10 (owner: "iterate through the species autonomously overnight, keep inference quota in mind")
Sequence (one Godot user at a time, serial on main):
1. m1 per-renderer soaks (script, no model cost) -> tabulate into docs/overhaul/baseline.md.
2. S0 species matrix + triage (sonnet/low, vision): per-family contact sheets from baseline2 (flat, slope, stages),
   score continuity / believability / distinctiveness 1-5 + top defect per species -> docs/overhaul/species-matrix.md, ranked worst-first.
3. Shared junction fixes before per-species work: F5 tube end caps -> F1 fork junctions -> F6 leaf base (sonnet/medium each),
   each validated on a 6-12 species subset, before/after sheet.
4. Species packets, worst-first: 4-6 species sharing an OrganismMeshes file per packet (sonnet/medium). Packet carries D5 (fix in
   place) and D7 (own materials/normal maps allowed; lifelike, slightly whimsical). Clanker captures before/after for its species
   only and builds one comparison sheet; orchestrator reviews that one sheet. Accept -> commit + matrix row update.
   Reject -> one revision via SendMessage; second miss -> restore by path, mark BLOCKED, move on.
5. Every 3 accepted packets: haiku soak x2 vs baseline2; GPU frame > +5% or VRAM > +200 MB -> stop and attribute before continuing.
6. Ground track (owner 2026-11-10: "ground / soil / rocks could use some visual work"): Gr0 read-only audit + triage of
   terrain.gdshader, loam_ground/loam_litter, rock.gdshader, IslandRenderer, SoilDetailRenderer, PropRenderer/PropMeshes,
   RockSurfaceProfile, TerrainMesh, IslandMeshClipper (cut-face soil profile) against baseline2 visual + guardian feet shots ->
   docs/overhaul/audit-ground.md with ranked items. Then ground packets (sonnet/medium) interleaved with species packets
   (alternate 1 ground : 2 species) since files do not overlap; same accept/revise/block rule and perf check.
   Gr0 DONE (6c80c7e): order = ground close-up capture set -> GR1 -> GR2 -> GR3 -> GR4 -> GR5 -> GR7(+GR8) -> GR6.
   terrain.gdshader packets (GR1, GR3, GR7) strictly serial. GR2 palette proceeds under D7 (lifelike target); GR6 cosmetic part
   only (no added rocks = no sim change); GR8 sun-direction unification deferred to owner (lighting contract).
   S0 DONE (c6cb9c0): shared counts F5 27, F15 26, F6 21, F13 13, F1 10, F8 8, F2 6, F4 6, F10 5(+F9 1).
Quota guard (checked with get_usage before every dispatch):
- 5-hour window >= 85%: dispatch nothing new; background timer until reset, then resume.
- Weekly all-models >= 30% (was 7% at start): stop for the night, ledger + worklog, wait for owner.
- Orchestrator stays lean: no raw logs, sheets viewed at reduced scale, Clankers return one-line DSL + <=15 lines.
Out of scope tonight: fauna/guardian species work, UI, ecology, owner-decision items.
