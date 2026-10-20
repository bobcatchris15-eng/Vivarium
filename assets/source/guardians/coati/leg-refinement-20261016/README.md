# Coati leg revision and fur normal source

`refined-legs-and-fur-normal.blend` is an editable derivative of the retained production GLB, with packed textures and the four retained/retargeted clips. The original `game/assets/guardians/woodland_omnivore/guardian.blend` is preserved. `detailed-original.glb` is the exact pre-revision detailed source, SHA256 fa2559a9f667e8035571c8d38b44c21754a6bc58369bfd2a9e632d3148fd7c54.

The fiber coat was rejected by the user. This revision has no coat fiber geometry; only16whiskers remain. Geometry/rig/materials remain a first revision: close shoulder and hip transitions need further polish, and full gait/performance acceptance is open. Do not describe numerical contact passes as final visual acceptance.

Reproduce in an isolated output folder with Python numpy/scipy/Pillow and Blender5.2. Keep all six modules in `recipe/` together:

1. `python recipe/coati_leg_surface.py detailed-original.glb <out>/stance.glb --geometry-only --retarget-stance --volume 1.6`
2. `blender --background --factory-startup --python recipe/coati_limb_union.py -- <out>/stance.glb <out>/body.glb`
3. `python recipe/coati_anatomical_weights.py <out>/body.glb <out>/weighted.glb`
4. `python recipe/coati_normal_surface.py <out>/weighted.glb <out>/final.glb`

The fourth stage also saves an editable2048normal PNG. Review candidate GLBs through the opt-in `--guardian-coati-model-candidate` native fixture before changing production. Existing whole-model `generate_coati.py` is the older authoring route and will not reproduce this refinement; do not run it over this asset unintentionally.

Evidence: `build/guardian-polish-20261016/leg-anatomy/`; source-backed integrated studio/motion in `build/forest-reference-20261015/coati-leg-integrated-*`. Original failed and rejected experiments remain preserved. Native current task and next action are documented in `docs/references/forest-2026-10-15/NEXT_WAKE.md`.
