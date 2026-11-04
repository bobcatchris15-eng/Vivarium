# Fernback anatomical model and review record

**User correction:** Revision 06 was rejected by the user as cartoonish in proportion and insufficiently differentiated at the limb joints. The earlier positive Luna assessment was too permissive and is historical evidence only. The correction pass begins at revision 07; see `user-correction-anatomy-audit.md`. Revision 07's whole head/neck compression was rejected internally for making the skull too small. Revision 08 restores skull presence while shortening the neck and trunk independently, and rebuilds anatomical limb segments with reduced global smoothing. These candidates require visual review against live photographs, not acceptance from structural checks alone.

Work date: 2026-10-31. Scope: original Blender mesh, coat, rig, pose renders and Luna art review for the existing `fernback` guardian. Existing unrelated workspace changes are preserved.

## Current anatomy correction — revision 12

Revision 12 replaces the rejected revision 06 runtime GLB. The old GLB remains available in `revision-06/guardian.glb`, and the earlier original remains in `before/guardian.glb`. The primary comparison is now [anatomy-review.html](anatomy-review.html); the older gallery and positive revision-06 review describe historical work and do not override the user's rejection.

Changes made in response to the user's feedback:

- Shortened shoulder-to-hip span by about 24% relative to the original construction, with a larger skull retained independently of the shortened exposed neck. Removed separate cheek and nasal-bridge ellipsoids, reduced/tapered the pinnae, softened the eye border, and buried the axial neck cap inside the skull/throat volume.
- Changed the uniform barrel into a deeper thorax, narrower/higher loin, and lower sloping pelvis/croup; slimmed the conical tail base while preserving tail length.
- Rebuilt forelimbs with distinct deltoid/triceps, olecranon/elbow, forearm belly, narrow distal shaft, carpus and pastern. Hindlimbs now distinguish the thigh, forward stifle, calf, Achilles/hock and metatarsus. The final skin is continuous; landmarks are shaped into the surface rather than represented as exposed spherical joints.
- Reduced global smoothing from factor 1.35 / six iterations to 0.35 / two iterations. Additional weighted smoothing is confined to fleshy shoulder/pelvic attachment regions above the distal joints.
- Removed the old spherical dewclaw and replaced it with an attached tapered digit; reduced and elongated the patellar/calcaneal relief; revised paws, toes and claws. Revision 07's smaller skull and revision 08's bead-like protrusions were rejected. Revision 10 was stopped after diagnostic renders exposed an added ear-root nub; that process was ours, and no user application was stopped.
- Rendered color poses and clean clay side/forelimb/hindlimb studies. Supplemental `forelimb-clay-full.png` and `hindlimb-clay-full.png` use a wider camera to include full paws; these are actual rerenders of the mesh, without image postprocessing.

Revision 12 structural evidence: 260,022 triangles in the main skin; 318,569 total GLB triangles; 253,919 exported vertices across materials; 31 bones and four clips; 20,270,328-byte GLB. The main skin has zero nonmanifold edges, maximum retained weight-sum error 4.70e-8, and zero nonfinite evaluated vertices across 52 pose samples. The minimum sampled local skin Y during walk was -0.000223 m; this is not a zero-penetration or continuous terrain-contact claim. Sole selection in the audit now follows actual foot/pastern vertex groups instead of old hardcoded foot locations.

The bounded cross-roster audit is [user-correction-anatomy-audit.md](user-correction-anatomy-audit.md). Dewhorn is the strongest likely shared smooth-limb issue; Ruffleback is a secondary possible issue. Other species have different anatomy, obscuring coverings, or insufficient comparable evidence. Only Fernback's mesh was changed during this correction pass.

Current editable model: `revision-12/fernback-studio.blend`. Executed model source: `revision-12-source.py`. Supplemental limb-camera source: `render_limb_clay.py`. Native capture: `native-anatomy-12/`; final independent art review: `review-12-luna.md`. These remain separate from full gameplay/contact, performance and user visual acceptance.

Final correction review: Luna inspected the color and clean clay views against revision 06, revision 11 and the live-animal references. It judges both original complaints materially addressed: proportions are more compact with a larger skull/lower croup/continuous throat, and limb chains have differentiated integrated joints without detached spheres. Residual concerns are broad/smooth body planes, a stylized face, and toe separation at distance. This is a structurally reviewable candidate, not a photorealism or user-acceptance claim.

The revision-12 native guardian review completed with `GUARDIAN_REVIEW_OK` and 27 PNGs. The promoted GLB, retained revision-06 GLB, production guardian shader and game assembly hashes remained stable across that capture (`anatomy-verification.json`). The same reflection-probe-setting and shutdown texture-RID warnings seen in earlier native captures remain in the log. No C# or shader changes were made during the anatomy correction, so the prior successful solution/game builds were not redundantly repeated. No performance acceptance is claimed. All owned rendering processes have finished.

## Reference and design

See [REFERENCES.md](REFERENCES.md). The animal keeps the long thylacine skull, rounded pinnae, deep thorax, narrowing loin, relatively low hocks, and substantial tapering tail. The fantasy feature is six branching charcoal fern markings. Color and motion are artistic interpretations rather than a scientific reconstruction.

## Iterations

1. Built a new continuous voxel-fused skin from anatomical volumes, a 31-bone armature, eyes, pinnae, nasal pad, lips, claws and whiskers. Rendered seven views/poses. Luna rejected the rectangular trunk, chest lobe, tube limbs, ringed eyes, large ears, jagged markings and thin tail root.
2. Shaped the trunk/waist/croup, thickened the tail root, smoothed limb curves, reduced eyes/ears/nose, broadened marks, added short coat geometry, and replaced discontinuous skin-region weights. Luna confirmed improvements but identified a shoulder shelf, limb caps, pointed muzzle, straight lips, facial stubble and weak stance/swing separation.
3. Changed axial construction to fixed vertical cross-sections to remove folded neck/shoulder rings. Added a planar two-link elbow/stifle solve, with distal foot orientation held through stance/swing; refined facial details and markings; added coat UVs and a packed tangent normal. Luna confirmed improved silhouette and paw separation but rejected the repeating normal-map corrugation, remaining limb cap, facial stubble and eye rim.
4. Buried limb roots within the torso; removed facial coat geometry; softened the cheek and bridge; shortened pinnae; randomized coat grain and stripe spacing. Luna judged this the strongest version then, while retaining concerns about eyes, pinnae, coarse fibers and the marking rhythm.
5. Replaced tubular eye rims with a skin-shaped eyelid annulus; filled the tiny decimation boundary hole; normalized retained skin weights; matched the studio support plane to the asset's lowest skin surface. Changed coat scatter to area-uniform sampling and embedded hair roots; filled out the proximal tail and kept stripe stems continuous through branching points. Luna identified a significant regression: a raised pale mound around the eye. This revision was rejected as the final candidate despite passing structural checks.
6. Sculpted a shallow orbital depression; used BVH raycasts to fit the eyelids and eye placement to the actual skull surface; reduced eyelid relief to 0.2–1.2 mm and darkened the iris. The full-size close-up resolves the conspicuous mound. Current candidate: revision 06, installed as `game/assets/guardians/fernback/guardian.glb`. Luna's sixth review is recorded separately.

Each completed revision retains its GLB, packed editable Blender model, render set, and audit. The source snapshots named `revision-NN-source.py` record the executed script for that pass; copy a snapshot to `build_fernback.py` in this directory before rerunning it, because output paths are relative to the script. Revision 01 predates these source snapshots; its binary and renders are preserved.

## Verification completed so far

- `dotnet build Vivarium.sln --nologo -v quiet`: success, 0 warnings/errors.
- `dotnet build game/Vivarium.csproj --nologo -v quiet`: success, 0 warnings/errors, run after the solution build.
- Isolated Godot 4.7.1 GLB import checks for revisions 01–03, 05 and 06: skeleton and all four clips (`idle`, `walk`, `feed`, `rest`) load; vertex colors preserved; no nonfinite vertices/bone transforms in the checked state.
- Revision 03 Blender evaluated-skin audit: no nonfinite evaluated vertices across 13 sampled frames of each of the four clips. The sampled walk sole minima varied approximately 0.004–0.040 m in model coordinates, with explicit stance and swing phases. This is not continuous native terrain-contact or foot-lock acceptance.
- The same structural audit found three boundary edges (one tiny triangular hole) after decimation in revision 03. Repaired and rechecked in revisions 05 and 06: **zero nonmanifold skin edges**, maximum retained weight-sum error **4.68e-8**, **zero nonfinite evaluated vertices** over 52 evaluated pose samples (13 frames in each of four clips).
- Revision 06 runtime GLB: **261,398 triangles total**, **224,226 exported vertices** across materials, **31 bones**, four animation clips, packed coat normal map and vertex-painted coat. Main continuous skin: 203,294 triangles. GLB size: 17,678,600 bytes. This is a detailed candidate, without a completed performance budget/LOD acceptance pass.
- The actual Vivarium `--guardian-review --guardian-species fernback --guardian-material-review` route completed with `GUARDIAN_REVIEW_OK` for revision 06, emitting **27 PNGs** under `native-final/fernback/`, material metadata, scene-motion data and `native-final/review-fernback.json`. These use the existing production guardian renderer, shader, lighting and orientation/contact setup. Native camera framing is distant in the standard studio views; `face-detail.png` actually frames most of the animal at this guardian scale. Use Blender close-ups for facial art judgment.
- The native review emitted a reflection-probe-resolution warning and a shutdown warning about seven texture RIDs. No `GUARDIAN_REVIEW_FAILED`, `ERROR` or `VK_ERROR` occurred. These warnings are retained in `native-final.log`; this run is not described as warning-free.
- `final-verification.json` confirms the promoted GLB, saved previous GLB, production guardian shader and executing game assembly hashes remained unchanged across final native capture. The exact final GLB matches the revision 06 export. Previous runtime asset preserved at `before/guardian.glb`; no commit was made.

## Deliverables

- [Comparison gallery](review.html): six revisions, seven full-size views per revision; native captures and Luna reviews linked.
- [Editable packed Blender studio](revision-06/fernback-studio.blend): animal, rig, materials, lights and review camera.
- [Editable packed animal source](revision-06/fernback.blend): mesh, rig and packed material image without studio objects.
- [Runtime GLB](revision-06/guardian.glb), also promoted to the existing game asset path.
- [Final source script](build_fernback.py), reproducible using Blender 5.2 with `--revision 6`.
- [Final geometry audit](revision-06/mesh-audit.json), [native import audit](revision-06/native-audit.json), [native review receipt](native-final/review-fernback.json), [hash verification](final-verification.json).

## Evidence boundaries

CPU studio images show the actual skinned mesh in different poses, with no image-generation paintover. Build, native import, Blender geometry checks, visual review, full in-game motion/contact, and performance acceptance are separate claims. Full locomotion quality, animation transitions and populated-world frame-time performance remain separate validation work. A review cannot prove that no further artistic improvement is possible.

## Final Luna assessment

[Revision 06 review](review-06-luna.md) confirms the orbital mound is resolved and reports no major full-body anatomy or pose regression. Remaining concerns are finer refinements to mouth-line shape, ear-rim softness, coat grooming and stripe rhythm. This is the strongest retained candidate from six build/render/review cycles, not a proof of absolute artistic perfection. Further lifelike polish would benefit chiefly from a dedicated sculpt-and-groom pass and art direction; continuous game motion/contact and frame-time acceptance remain open.
