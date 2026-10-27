# Fauna black rendering correction - 2026-10-25

Only production change made by this task: four lines at the start of fragment() in game/Shaders/fauna.gdshader. Preserve all pre-existing shader and checkout edits.

The WP23 tissue block conditionally writes SSS_TRANSMITTANCE_DEPTH and SSS_TRANSMITTANCE_COLOR. These outputs enable transmission shader-wide. Species without an active transmission branch inherited a zero depth. Initialize a positive 0.001 depth and zero transmission colour before species overrides. This prevents the invalid zero-depth transmission term while retaining the existing optical layers.

Evidence:
- fauna-black-baseline-20261025: production Rustcoil live renderer, 240 PNG, stable source, zero engine errors, visibly black against lit support.
- fauna-black-no-tissue-20261025: removing tissue block restores colour; 240 PNG and stable source, but native finalizer crash on exit means run is not a clean pass.
- fauna-black-depth-fix-20261025: complete tissue block retained plus defaults; Rustcoil colour restored. 240 PNG, stable source. Vulkan VK_ERROR_OUT_OF_HOST_MEMORY presentation errors invalidate clean-run acceptance.
- fauna-black-aquatic-short-20261025: bounded 100-engine-frame Glintfin preview, exit 0, empty stderr, colour visible. Intentional early exit before 240-frame preview completion.
- fauna-black-chitin-short-20261025: bounded Coalback check; see stdout/stderr and captures.

Build succeeded with zero warnings/errors. These are native Godot 4.7.1 Forward+ GTX 1080 diagnostics using disposable preview worlds. No save migration, ecology change, editor shutdown, commit, or FPS claim. Original shader preserved at build/fauna-black-diagnostic/fauna-original.gdshader. No full populated-world reacceptance claimed.

Coalback short result: exit 0, 100 PNG, empty stderr; visibly lit body and appendages. Aquatic short result: 100 PNG. Both intentionally bounded by --quit-after 100.
