# Hushbill guardian

Original Blender-authored shoebill-based mesh and textures. The two curved crest feathers are the approved Hushbill fantasy detail.

`guardian.glb` is the runtime asset loaded by the existing `GuardianRenderer` Hushbill archetype path. `guardian.blend` is the editable Z-up source, with packed authored texture images and named actions/NLA tracks. The six PNGs are the original texture maps, also embedded in the GLB. Reference photographs are not game textures and are not embedded in these files.

Authoring and evidence: `scratch/hushbill_realism/build_hushbill.py`, `REFERENCES.md`, `review_r*.md`, `renders/`, `build_report.json`, and `import_audit.json`. Reproduce with Blender 5.2 in background mode, eight CPU threads, and the revision recorded in the build report. Each run exports the GLB, saves the editable blend, and renders six studio views/poses. Review images retain earlier revisions for comparison.

The export transforms both mesh bind geometry and bone rest transforms to match the existing renderer's import-axis correction. A root-only axis rotation would make raw bounds disagree with the skinned geometry and cause false ground lift; the isolated Godot audit checks both.

Clips: `idle`, `walk`, `feed`, `rest`, `bow`, `look`, `alert`. These are basic pose/motion studies, not a claim of final locomotion or terrain-contact acceptance. The headless audit verifies import, skin/bone data, named clips, finite transforms, vertex colors, upright dimensions, and agreement of raw and skinned rest bounds. It does not verify appearance in the production renderer, final animation quality, or performance.
