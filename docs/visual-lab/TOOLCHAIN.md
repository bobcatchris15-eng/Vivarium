# Verified local tool operations

Run from E:/Vivarium in PowerShell. All generated evidence belongs under ignored build/visual-lab; scripts honor .clanker/HALT. Bootstrap reuses installations and a pinned isolated Material Maker source checkout. It does not reset other repositories or reinstall working machine tools.

```powershell
pwsh -NoProfile -File scripts/visual-lab/Toolchain.ps1 -Action Inventory
pwsh -NoProfile -File scripts/visual-lab/Toolchain.ps1 -Action Bootstrap
pwsh -NoProfile -File scripts/visual-lab/Toolchain.ps1 -Action BlenderGenerate -OutputPath E:/Vivarium/build/visual-lab/leaf-smoke
pwsh -NoProfile -File scripts/visual-lab/Toolchain.ps1 -Action BlenderReference -OutputPath E:/Vivarium/build/visual-lab/leaf-reference
pwsh -NoProfile -File scripts/visual-lab/Toolchain.ps1 -Action ValidateGltf -InputPath E:/Vivarium/build/visual-lab/leaf-smoke/organic_leaf.glb
pwsh -NoProfile -File scripts/visual-lab/Toolchain.ps1 -Action OptimizeGltf -InputPath E:/Vivarium/build/visual-lab/leaf-smoke/organic_leaf.glb -OutputPath E:/Vivarium/build/visual-lab/leaf-smoke/optimized.glb
pwsh -NoProfile -File scripts/visual-lab/Toolchain.ps1 -Action MaterialExport -InputPath E:/Vivarium/build/visual-lab/material-templates/soil.ptex -OutputPath E:/Vivarium/build/visual-lab/soil-export
pwsh -NoProfile -File scripts/visual-lab/Toolchain.ps1 -Action EncodeMotion -InputPath E:/Vivarium/build/visual-lab/baseline-motion/motion/frame-%03d.png -OutputPath E:/Vivarium/build/visual-lab/baseline-motion/motion/reencoded.mp4
```

Inspect material_templates.py arguments before generating recipes. Recipes are editable starting points derived from official Material Maker examples, not finished materials or validated anatomy. Exported soil was inspected: nonmetallic, roughness 0.9, normal mapping enabled. Its displacement scale needs calibration before any game integration. The nine recipes have not all been exported or aesthetically assessed.

RenderDoc's actual game capture is build/visual-lab/tool-tests/renderdoc. Native operations verified:

```powershell
renderdoccmd replay -l 1 E:/Vivarium/build/visual-lab/tool-tests/renderdoc/vivarium_frame1040.rdc
pwsh -NoProfile -File scripts/visual-lab/Toolchain.ps1 -Action InspectCapture -InputPath E:/Vivarium/build/visual-lab/tool-tests/renderdoc/vivarium_frame1040.rdc -OutputPath E:/Vivarium/build/visual-lab/capture-inspection.zip.xml
```

Confirm the actual capture filename in that directory before invoking these examples. Native replay uses the GPU: acquire the shared laboratory lease and serialize it with other owned GPU operations. Conversion reports native draw/dispatch chunks, not GPU timing, texture correctness or visual quality. Standalone Python replay bindings were not available in this installation; failed probes are retained.

Blender smoke covers deterministic organic mesh construction, UVs, applied thickness, smooth normals, a morph animation and GLB export. Original and optimized assets passed Khronos validation without errors or warnings. A real Cycles CPU render succeeded at 384x384 and 16 samples. This is a capability test, not a high-quality reference or game import acceptance.

Installed Material Maker executable failed with an access violation. The successful alternative runs its MIT source pinned to 8719010210b84cc45a4a1d4f0f5c1bc62d7b68e1 through the existing Godot executable. prepare_material_maker.py records batch-only repairs and hashes; startup/export succeeds, but shutdown reports retained resources. Do not describe it as free of errors or extend these repairs to the user's installed application.

Exact versions, executable paths, sources and license evidence are retained in build/visual-lab/recon/toolkit-worker, build/visual-lab/tool-tests/blender and Material Maker source/repair receipts. Inventory: Blender 5.2.0 LTS, RenderDoc 1.46, GIMP 3.2.4, Inkscape 1.4.4, Python 3.14.5, .NET 8.0.425, PowerShell 7.6.5, FFmpeg 9.0.2, ImageMagick 7.1.2-25, glTF-Transform 4.5.1, gltfpack 1.3 and Khronos validator 2.0.0-dev.3.10. Version discovery alone is not operational validation. GIMP/Inkscape authoring, PlantFactory installation and OpenImageIO remain unverified; avoid tool-count installation without a concrete capability need.

Hardware observed: GTX 1080 with 8192 MiB VRAM, NVIDIA driver 581.57, Ryzen 5 1600 and approximately 32 GB RAM. WMI AdapterRAM truncated the VRAM value; use the verified nvidia-smi result. Unrelated Godot sessions remain running, so diagnostic output cannot establish isolated performance acceptance.

Matched motion capture authenticates the current source against its corresponding still capture before launching and refuses existing evidence directories:

```powershell
pwsh -NoProfile -File scripts/visual-lab/CaptureMotion.ps1 -Experiment VL-0003-foliage-construction -Side before
```

This command has already been executed for both sides; do not rerun it over those immutable directories. It scopes isolated user storage, records competing processes and encodes 48 actual renderer frames. Frame phase advances are fixed; encoding at 24fps does not measure runtime FPS.

DiagnosticWorld.ps1 reuses the existing real GameSession landscape measurement/capture route, with honest isolation_confirmed=false provenance. It creates a fresh initial or mature world, 1600x900 high quality and fixed illumination, and never invokes or claims an isolated acceptance pass:

```powershell
pwsh -NoProfile -File scripts/visual-lab/DiagnosticWorld.ps1 -Scenario frozen -Preparation initial
```

The initial route is diagnostic coverage, not a mature ecological acceptance. The same reference-report validator remains authoritative for full gates; diagnostic provenance must fail isolation acceptance. This script requires an up-to-date game build first and holds the shared GPU lease until its own process exits.
