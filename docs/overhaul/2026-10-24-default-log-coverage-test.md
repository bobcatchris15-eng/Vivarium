# Startup-world fallen-log coverage test — 2026-10-24

User request: quick test of the world loaded on normal startup; artifacts reportedly concentrate on fallen logs with moss/lichen, less on bare ground.

Normal startup (`Main.cs`) attempts to load `APPDATA/Vivarium/saves/autosave.vivsave` before creating a new default preset. Previous habitat controls created a fresh preset instead. This test loads a byte-identical private copy of that actual startup autosave. SHA256: `A83D48B66FE68B5DE02AE4FE8BA140A504D7D7D364E4DDD438CD7D69A82E3CB8`. Input snapshot and provenance: `build/photorealism/default-log-input-20261024/`. No save writes are authorized or performed.

Diagnostic-only scene selection samples existing Mat+Crust biomass along log axes and selects the highest covered sample, with nearby bare ground as control. No coverage is planted or simulation advanced. The chosen weathered log is approximately 1.63 m long, radius 0.234 m, at (-2.87, 0.332, 0.485); the target biomass sum is 0.191 and nearby ground is zero. This is a mixed-coverage scene, including rebuilt sundew and a pale crust patch; total biomass selection is not a moss-only species mask. A large foreground leaf partially occludes the upper image. The lower log remains visible.

The bounded batch uses the corrected habitat probe: actual settled gameplay focus, manual exposure, baseline/DOF off/jitter off/MSAA off/coverage hidden/restored controls, four frames each; matching eight-frame camera sweeps with DOF on/off. Two views, 82 expected PNGs. Coverage-hidden is authenticated by recorded visibility and a runtime check.

Source inspection gives a geometry lead, not a confirmed artifact cause: log coverage samples `LogProp.TopAt`, an ideal cylindrical height, whereas rendered logs have taper, bending, sag, bark ridges and knots in `PropMeshes.Log`. Log-only coverage triangles also retain the older height-span rejection, while rock tiles use adaptive support-plane subdivision. This can explain why a prop contact problem would be absent from a flat isolated fixture; it does not establish that these reported flecks are depth conflicts.

The native test completed in 144.4 seconds: 82 PNGs, stable source, zero engine errors, unchanged canonical world digest (seed 88314, tick 10133, 376 flora, 24 fauna). Both hidden controls record coverage invisible. The original startup autosave hash is unchanged after capture. Existing seven-Texture-RID shutdown warning persists; no performance claim.

Root and independent Luna inspection did not identify a reliable reproduction of the reported intermittent black/cyan/magenta flecks in these sampled views. Dark bark cracks and coverage silhouettes remain, and DOF changes sharpness. This negative sample does not establish the absence of the live event. The upper leaf occlusion and mixed coverage limit this camera sample. No production material, DOF, AA or geometry correction has been promoted.

Gallery: `build/photorealism/default-log-coverage-01/index.html`. Use log-top motion_baseline versus motion_off for matched movement. Recorded target: `captures/log_coverage_target.json`. Camera-moving frames are excluded from fixed-pixel temporal screening. Next work, if needed, should target a clearly visible pale moss/lichen patch on this same loaded world and test actual bark/coverage depth interaction; avoid another flat-specimen survey.
