# Selective mesh-detail integration, 2026-10-13

The user's clarification is authoritative: take specific useful details from the incoming meshes and work them into the existing detailed candidates. Do not replace the existing candidates wholesale or transplant the incoming reduced rigs/materials/clips.

Incoming reference commit: `381bb7abe97bc077d79c4fa6c4e90435b9279a69`. Existing detailed baseline: `7836b7aa5de0d7415e738d80c0d4c262a7eaeb44`. Both remain in Git history. Readable comparison copies are under `pull-20261013T1905/incoming` and `pull-20261013T1905/previous`; large assets/captures stay local.

## Selected details and review queue

| Guardian | Specific reference details | Integration status |
|---|---|---|
| Boa | Distinct cervical constriction, taller/narrower arboreal trunk, fuller anterior muzzle and broad head roof | Integrated selectively into the detailed v6 topology/37-joint rig in solo v8; retained independent jaw, tongue, sockets, heat pits and six embedded PBR images. Native comparisons confirm the slimmer trunk and less washed-out green response. |
| Hoatzin | More readable anterior crop mass and neck/torso separation | Selectively integrated restrained crop/neck separation in detailed solo v14 with rounded projected coverts, four lids and retained maps/tail/gait. Native v15 full buff rings were rejected for a gold collar; v14 restored and rerun with final runtime. |
| Pangolin | More continuous arched back/head silhouette | Native incoming clay view reviewed. Keep only the envelope idea if a matched detailed-candidate comparison improves it; incoming flat plates, bare angular limbs and missing contact bone aliases are regressions. |
| Monitor | Broad flattened ribcage, boxed muzzle/gular fold and compressed tail keel | Incoming native overview reviewed; angular limbs and featureless white surface are unsuitable. Queue individual comparisons before accepting any silhouette detail into the detailed monitor candidate. |
| Coati | Thoracic depth, tucked lumbar waist, rhinarium shape and erect balancing tail | Source/reference mesh preserved; native all-species review crashed before coati. These are hypotheses for a future individual comparison, not verified improvements. |
| Porcupine | Forehead dome/blunt muzzle, arched scansorial back and grasping tail tip | Source/reference mesh preserved; no completed incoming native comparison. Evaluate during its own wake; preserve current coat/quills, two-link limbs and lids. |

The other five active GLBs are restored byte-for-byte to their detailed baseline. This preserves established candidates while reference details await individual review. The incoming legacy authoring files remain available; canonical generators use the individual cinematic/solo modules.

## Incoming regressions verified before integration

All six incoming GLBs contain zero embedded images and one uniform material. Five non-boa walk clips have identical normalized channel values at every exported key; source keys at frames 1/16/31 sample sine at 0/pi/2pi. Boa slither does move, but its Spine chain replaces Body00..Body32 and cannot be consumed by the continuous-body contact modifier. Other leg names do not resolve through the runtime leg aliases. Native contact reports found no qualifying Body/Hand_L/Foot_L skin vertices. The review fixture assumes a walk alias whereas the production serpentine selector accepts slither; distinguish that fixture issue from the actual missing body contact chain.

The 27 pre-integration tests and structural audits passed despite those visual/runtime failures. They do not establish art acceptance. Raw comparison reports: `pull-20261013T1905/asset-contract-comparison.json`, `normalized-clip-motion-audit.json`, `incoming-structural.json`, import/native logs. The earlier unnormalized quaternion audit is diagnostic only; use normalized component changes.

## Failed experiments and evidence limits

The incoming all-species native review saved 585 PNGs before a fatal Godot Texture2D.GetImage readback failure. The v7 boa run completed studio animation/transition/contact data but crashed during its final legacy world scene. Neither is a completed full review. Failures and candidate v7 are retained.

`--guardian-studio-only` now completes studio/transition/contact independently. Use the separate `--guardian-populated` production session for populated evidence. A studio completion marker never substitutes for the populated run.

Attempted source playback: https://movie.biologists.com/video/10.1242/jeb.166199/video-1 and earthworm reference https://commons.wikimedia.org/wiki/File:Earthworm_moving.webm. The browser failed attachment, Edge was unavailable and subsequent transport closed. Research text describes belly anchoring/recovery during boa rectilinear motion; no actual playback or measured timing was obtained this wake. Authored boa and worm motion timing therefore remains unchanged. Retry actual video playback before the next motion revision; do not invent observed video poses.

Sparse skin samples, centerline capsules and rendered shrub bounds do not prove exact triangle/branch/leaf collision. Near-photoreal shading, natural populated motion, boa posterior mandibular/neck join and worm terminal rounding remain open.
