# Moss and bokeh artifact investigation — 2026-10-24

Status: bounded diagnostic batch complete; reported flecks still not reproduced and root cause unresolved. No production correction promoted.

The user reports intermittent black/cyan/magenta flecks in-world, associated with moss and bokeh, absent in focused close views and not apparent in isolated specimens. Softness caused by defocus is not reproduction of those flecks. The earlier isolated survey cannot establish a DOF cause because that survey disabled DOF.

## Isolated control

`build/photorealism/moss-dof-isolated-02`: 300 native PNGs, exit 0, stable source manifest, zero engine errors. Four mosses (bogglass, pearl cushion, floodlace, velvetweave) plus rebuilt blue sundew. Perspective camera, manual exposure, fixed world/material clocks. Five settings (DOF off, focused, focus nearer, focus farther, focus nearer without jitter) crossed with MSAA 4x/off; six frames per setting. TAA and FXAA retained.

Deliberate defocus produces ordinary blur. Neither visual inspection nor temporal candidate screening establishes the reported wrong-color flicker in these fixtures. Jitter/MSAA controls do not establish a fix. Temporal thresholds are triage, not acceptance; they can flag valid sampling changes and miss intermittent defects outside the sample.

## Habitat harness correction

`moss-dof-world-01` completed with 179 PNGs, stable source and zero engine errors. It is not causal evidence: CoverageRenderer forces itself visible in `_Process`, invalidating the hidden control; simulation pause also froze camera focus smoothing at the earlier camera distance. Those limitations were found by inspecting recorded settings, not assumed away.

The corrected world02 diagnostic explicitly advances only camera presentation smoothing before freezing its parameters, disables coverage processing during the hidden control and checks that visibility stays false, and records near/far blur settings. Two actual habitat views (bogglass moss and blue sundew) compare baseline, DOF off, jitter off, MSAA off, coverage hidden, restored baseline. Four frames per static setting. Matching eight-frame camera sweeps compare bokeh on/off; fixed-pixel temporal screening is deliberately skipped for those moving sequences.

These are disposable diagnostic-process changes. Gameplay bokeh, AA and moss shading remain unchanged. The ecology stays paused; available material clocks are locked, but raw shader TIME and initial animal poses are not fully controlled. No performance acceptance is claimed.

## Corrected result

`build/photorealism/moss-dof-world-02`: 82 native PNGs, exit 0, stable source, zero engine errors, unchanged canonical world digest. Both coverage-hidden records authenticate false visibility and the images visibly remove the coverage. Client build has zero errors and six pre-existing warnings; the existing seven-Texture-RID shutdown warning persists.

The actual center-hit focus settled to approximately 0.576 m in the sundew habitat (blur amount 0.0721) and 0.254 m in the bogglass habitat (amount 0.0819). The DOF-off controls are visibly sharper. Neither root inspection nor independent Luna review found a reliable reproduction of the reported intermittent wrong-color/detached black flecks in the fixed or sampled moving views. That confirms blur behaviour, not an artifact cause or a fix. These short paused-world samples cannot rule out the user's live event.

Review `build/photorealism/moss-dof-world-02/index.html` for paired playback, including `motion_baseline` versus `motion_off`. The isolated comparison is `build/photorealism/moss-dof-isolated-02/index.html`. Both include native-pixel zoom, pause, recorded integrity and temporal candidate screening. Camera-moving sequences are excluded from fixed-pixel screening. Threshold candidates are not verified artifacts.

Next: match the exact user-reported gallery sequence or live save/camera. Do not remove gameplay DOF, globally disable MSAA, or rewrite moss based on the negative reproduction result. The user has been asked which view/save exhibits the event while this bounded batch finishes. No GPU process remains active; editors and unrelated local work are preserved. No commits or save changes.
