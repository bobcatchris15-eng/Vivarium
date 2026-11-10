# Vivarium overhaul — orchestrator ledger
Updated: 2026-11-10 | HEAD: a193e6d | Graph: none | Checkpoint tag: overhaul-base

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
- D4 2026-11-10: Owner rule — each Clanker gets the lightest model/effort that can reliably do its task.
  Default sonnet/low for scoped edits, sonnet/medium for audits, haiku for re-validation. Escalate only on evidence.
- D5 2026-11-10: Wholesale botanical geometry replacement previously made visuals worse (directive). Fix
  junctions/attachments in place; replace a species' geometry only with before/after renders approved here.

## Tasks
| id | targets | status | attempts | last return line |
|----|---------|--------|----------|------------------|
| b0 | build/overhaul/baseline, docs/overhaul/baseline.md | DONE | 1 | 3 visual, 126 specimen (63 species only), 30+30 guardian PNGs. Soak steady ~25 fps, p50 38.6 ms, p95 52-59, 707 draws, 7.17M prims, VRAM 4.2 GB, working set 7.6 GB. Close-ups 12-13 fps at ~10M prims. CPU max: Coverage 25-27 ms, LiquidSurface 8-9, Session.Advance 7.5, Guardian.Vegetation 5.5. --perf-test has no runner; --soak never exits; 7 texture RIDs leak at exit; 0/2 starter Burlmantle placed |
| a1 | docs/overhaul/audit-flora.md | DONE | 1 | 125 species (106 MultiMesh flora, 19 coverage). top: F1 fork junctions full-radius children + open trunk top (SoftTube fillet unused), F2 single terrain sample ground contact, F3 hard LOD swaps 3m/5.5m, F4 bark UV seams/stretch, F5 Primitives.Tube no end caps (360 sites), F6 blade base zero-width + open petiole. Shared: F1-F6 F8 F10-F12 F14. All vascular share 4 leaf pools + 3 bark sets. Review runner cannot test slopes or force LOD tier |
| a2 | docs/overhaul/audit-fauna-guardians.md | DONE | 1 | 17 fauna (own mesh builders, shader-animated), 10 guardians (skinned GLB). top: G1 clip chosen by Activity not motion (sliding), A1 fauna constant-speed random walk, G3/G4 foot slide + slope hover (G4 unconfirmed), G2 21 activities on 4 clips, G5/G6 water+veg contact, A2/A5 fauna feet rigid tilt. A0: no fauna review harness (specimen-review is flora-only) |
| a3 | docs/overhaul/audit-ecology.md | DONE | 1 | top: E6 coverage moisture never decays (persisted), E1 fauna budget first-come lock-out, E2 predation never kills, E7 trait variance collapses, E3 nutrient loop creates mass, E12/E13 flora+ambient tick cost. Inferred from code; needs --soak to confirm. Headless Step leaves water frozen |
| a4 | docs/overhaul/audit-performance-architecture.md | DONE | 1 | static only. SAFE: P1 env params rewritten per frame, P2 caustics in per-light light(), P3 sculpt full terrain+pond rebuild, P11/R4/R6 4K sets + dead code (GillTrial, BranchModuleLoader). DECIDE: P12 post/GI/fog stack, P13/R2 ambient cover regen ~115k pts per 2m move, R1 snapshot only used by flora + coverage thread race, R3 guardian bends written into flora MultiMesh. R9: add GPU timer, alloc bytes, instance/VRAM counters |
| h3 | game/App/{Main,LiveDiagnostics,FrameProfiler,GameSession}.cs | DISPATCHED | 1 | soak self-exit + perf_summary.json |
| a5 | docs/overhaul/audit-ui-ux.md | DONE | 1 | top: U1 Tab-hide swallows clicks, U2 no Esc/blocked keys, U3 bad autosave silently replaced, U20 per-frame window relayout, U6 scale slider font-only, U7 invalid-placement reason hidden. Owner decisions: U5 U6 U13-U18 U23 U24 |

## Unverified assumptions
- 173 locally modified `.import` files are line-ending noise except 3 crested_canopy files; never stage them.

## Orchestrator render review (baseline sheets, 2026-11-10)
- visual-review has only 3 scenes; `overview` shows the island at ~5% of frame, `grazing_transition` has black void at frame edges. Needs more, better-framed scenes (H4).
- guardian-review "closeup" puts the animal at ~10% of frame width; joints and feet cannot be judged. Needs tight framing + motion sequence (H5).
- specimen-review covers 63 of 106 MultiMesh flora species; trees appear to be missing. Litter ground reads as a repeating orange leaf tile; fungi caps look smooth/plastic.
