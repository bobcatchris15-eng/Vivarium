# M01 native comparison orchestration

`m01_fixture.py` records source and binary hashes, validates the fixture manifest, prints or launches the existing `MaterialPortReview` and `world-review` modes, refuses an existing output directory, and writes `m01-provenance.json` plus `receipt.json` with output hashes and native readiness markers. It does not modify Godot, add runtime overrides, or claim a unified calibrated photograph.

Run the focused CLI and manifest checks from `E:\Vivarium`:

```powershell
python scripts/visuals/photorealism/m01_fixture.py --help
python scripts/visuals/photorealism/m01_fixture.py --self-check
```

The actual capture command, after the coordinator selects a fresh output folder and an authenticated baseline directory, is:

```powershell
python scripts/visuals/photorealism/m01_fixture.py `
  --manifest scripts/visuals/photorealism/m01-manifest.json `
  --godot 'C:\path\to\Godot_v4.7.1-stable_mono_win64_console.exe' `
  --baseline 'E:\Vivarium\build\visual-lab\experiments\<experiment>\baseline\source\game\Shaders' `
  --output 'E:\Vivarium\build\photorealism\m01\<new-run-id>'
```

Before a native run, use `--dry-run` with those same arguments to inspect the exact native invocations. They are equivalent to:

```powershell
Godot_v4.7.1-stable_mono_win64_console.exe --path E:\Vivarium\game -- --material-port-review <new-run>\material-port-review --material-baseline <authenticated-baseline>\game\Shaders
Godot_v4.7.1-stable_mono_win64_console.exe --path E:\Vivarium\game -- --world-review <new-run>\world-review
```

The captures are separate processes. `MaterialPortReview` owns its fixture camera, fixed exposure (auto exposure off, tonemap exposure 1.12), and fixed day/night light phases (450/1020). `world-review` uses its disposable initial world, reference camera query set, 1600x900 high-quality path, and fixed light phase 420; the world-review source says shader `TIME` is not pinned. Neither mode accepts arbitrary camera, exposure, or light overrides through its current CLI. The material mode does not currently put production leaf, moss, bark, soil, wet rock, shallow water, guardian, and neutral diffuse/glossy targets into a single shared-light view. Thus a receipt can establish source identity, executed mode, output presence, and provenance, but cannot assert same-light calibration, full requested subject coverage, mature ecology, or aesthetic/performance acceptance.

Before launching, the coordinator must ensure no other Godot/Blender visual capture is using the display/GPU. This wrapper does not acquire the Visual Lab GPU lease, because the existing capture process does not honor an M01-specific lease and no capture was run during this tooling task.

The manifest distinguishes declared native behavior from unsupported override claims. Do not edit its fields to imply runtime controls; add an explicit engine-interface blocker until those controls exist. Existing captures in `build/` are inputs or evidence and are never selected as the output destination.

## M02 PNG comparison metrics

`compare_m02.py` compares an F0 baseline with an independent F0 repeat and one or more candidate captures. It requires PNG inputs with identical image dimensions, refuses an existing output directory, hashes every input, and writes `comparison.json`, a raw RGB absolute-difference image, a foreground mask image, and a common exact-black mask image for each comparison. The exact-black test is `RGB=(0,0,0)`; white pixels in the exact-black mask denote pixels where both images are exactly black. White pixels in the foreground mask denote the union of pixels nonblack in either image, so appearance/disappearance differences remain included. Optional `--roi X Y W H` limits masks and metrics to a stable pixel region.

Example:

```powershell
python scripts/visuals/photorealism/compare_m02.py `
  --baseline build/photorealism/m02/F0/baseline.png `
  --repeat build/photorealism/m02/F0/repeat.png `
  --candidate build/photorealism/m02/F1-candidate.png `
  --candidate build/photorealism/m02/F2-candidate.png `
  --roi 420 180 720 520 `
  --output build/photorealism/m02/metrics-20261021T235000Z
```

The report gives exact-black counts/fractions, nonblack bounding boxes, masked RGB MAE, encoded-sRGB Rec.709 luma quantiles, and file SHA256 values. It does not assign a photorealism score. Nonblack masking is not semantic segmentation: standards, subjects, shadows, and other nonblack background pixels stay in scope. Non-opaque PNGs are rejected; native screen captures are expected to be opaque. Diffuse-only separation is not inferred unless the captures were explicitly authored as diffuse diagnostics. Run the synthetic check with `python scripts/visuals/photorealism/compare_m02.py --self-check`.

## M03 exposure frame metrics

`analyze_exposure.py` reads every PNG in an input directory in case-insensitive lexical filename order and preserves `metadata.json` in the report when present. It does not require a metadata schema; filename-matched records are attached only when familiar filename-like keys are found. Supply repeated `--roi X Y W H` rectangles for stable leaf/card regions and optional `--reference filename.png` to report each region's RGB MAE against a fixed frame. Without a reference, the report still contains per-frame summaries and sequential MAE for whole image and each ROI.

```powershell
python scripts/visuals/photorealism/analyze_exposure.py `
  --input build/photorealism/m03/captures `
  --roi 500 280 280 260 `
  --roi 60 60 120 120 `
  --reference fixed-exposure-coverage-0.15-early.png `
  --output build/photorealism/m03/analysis-20261021T235900Z
```

The JSON records PNG SHA256 values, whole-image exact-black fraction, per-channel clipping at 255 and Pillow grayscale-luma-at-255 fraction, encoded-sRGB per-channel means and q50/q95/q99 for each region, ROI MAE versus the selected reference, and sequential stability. Luma clipping uses Pillow's 8-bit RGB-to-L conversion, not radiometric luminance. Black fractions include the original black background; they do not isolate subject brightness. The native M03 captures are intended to measure peripheral card sensitivity with camera and leaf held fixed; these metrics alone do not identify cause or establish production-framing acceptance. Existing output directories are refused. Run `python scripts/visuals/photorealism/analyze_exposure.py --self-check` for a synthetic check that varies an outside-ROI card while the black fraction and ROI remain fixed.

## Named family diagnostic profiles

`family-profiles.json` defines six diagnostic configurations: canopy-only, canopy-plus-key, and key-only lighting crossed with production and reflection-only material controls. These are `relative_engine` values intended for controlled comparisons; they are not photographic presets, calibrated physical light values, or measurements. The source research in `docs/2026-10-21-material-lighting-configurations.md` distinguishes physical quantities from heuristic shader controls. Engine energy and the material gains here are not lux, lumens, or calibrated radiance.

The reflection-only variant sets transmission, phase, and diffuse-wrap gains to zero while retaining the other material controls, including diffuse reflection and anisotropy. It is therefore a diagnostic comparison mode, not a pure specular BRDF or equivalent material. The fixture's built-in transmittance is disabled for this family comparison. Keep camera, exposure, geometry, environment, and capture timing matched when reviewing the six configurations. The planned comparison is six configurations with two comparisons per configuration (12 comparisons total) plus one repeat; this manifest defines settings only and does not schedule or verify captures. Calibration still requires suitable reference targets, recorded runtime state, and validation that all subjects are seen under the same light.

Validate without changing the source manifest:

```powershell
python scripts/visuals/photorealism/validate_family_profiles.py --help
python scripts/visuals/photorealism/validate_family_profiles.py --self-check
python scripts/visuals/photorealism/validate_family_profiles.py --input scripts/visuals/photorealism/family-profiles.json
```

The validator checks schema, numeric bounds, unique safe IDs, all six combinations, and the truthfulness rules for light/material modes. It prints a normalized profile summary and deterministic SHA256; it never writes the manifest. The SHA identifies these normalized settings, not native execution or visual acceptance.
