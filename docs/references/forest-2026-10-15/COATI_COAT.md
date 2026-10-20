# Coati model correction and normal-map fur - current direction

The user rejected the geometric short-fiber coat: its intended purpose had been to cover awkward legs. The active direction is actual model correction plus fur-look normal detail. Earlier fiber coat receipts and screenshots are historical rejected work, not the current production appearance.

Retained first model revision: fuller limb volumes, less angular elbow/knee rest arrangement, smoother skin ownership across torso/limbs and a reconstructed torso/leg surface. All four existing clips remain; limb tracks were retargeted rather than discarded. The detailed facial adornments and separate eye/iris/nasal/claw/lip meshes are preserved. Shoulder and hip roots remain rough at close range and require further anatomy/skin polish; this is not a finished-character claim.

The production model now has zero coat fibers and only16retained whiskers. Fur look comes from an embedded2048x2048directional normal map, strength0.55, with reduced facial strength and no fur bump on sole-facing areas. Facial detail surfaces retain their original material normal response. The fiber shader was retired into the historical experiment folder. GLB size is14,287,084bytes versus83,284,672for the rejected dense coat. No FPS claim follows from file size.

Native evidence: coati-leg-normal-v11-studio/motion and coati-leg-integrated-studio/motion under build/forest-reference-20261015. Forced builds and source guards passed. Both motion sequences run900frames and coverForage/Feed/Explore/Groom/Rest. Read final-validation.json for actual contact maxima. The normally imported asset screenshots differ from the runtime candidate import; exact image parity is not claimed.

Independent exported-clip reconstruction samples49times per clip. Maximum foot displacement difference: idle0.490mm,walk1.506mm,feed<0.001mm,rest0.047mm; head and snout world transform differences0. Native contact checks remain separate proof. This is more precise than the earlier constructed key-point0error claim.

Canonical editable/provenance source: assets/source/guardians/coati/leg-refinement-20261016/. It retains the exact original detailedGLB, a new packed-texture editable.blend and the complete recipe snapshots/README. Original guardian.blend/maps are preserved. Existing generate_coati.py is the older whole-model route and must not overwrite this refinement unintentionally.

Production files: game/assets/guardians/woodland_omnivore/guardian.glb and coati_fur_normal.png. Review fixtures and optional model loader are in GuardianReview.cs/GuardianRenderer.cs. Authoring scripts: gltf_mesh_patch.py,coati_leg_surface.py,coati_limb_retarget.py,coati_limb_union.py,coati_anatomical_weights.py,coati_normal_surface.py.

Reference direction, not replacement assets: Smithsonian coati imagery and the National Park Service description of plantigrade stance with shorter forelegs. No external model or texture was acquired for this pass. The shape remains a provisional authored animal rather than a scan.

Existing saves are compatible; no new world is needed. A newly started game process loads the new model/materials. Preserve all user editor sessions. Environment work remains closed, all guardians remain half-size, performance deferred, and anatomical production feeding contact remains open.
