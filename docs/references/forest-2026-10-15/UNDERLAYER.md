# Humus under creeping groundcover - retained 2026-10-16

The pale gray-green floor was primarily the anonymous ambient-cover tint, not the soil scan or damp scan. A temporary RGB mask (red=damp, green=ambient, blue=gravel) confirmed broad ambient coverage in the affected close/context views. Damp-scan recoloring v2 produced negligible improvement and was discarded.

`game/Shaders/terrain.gdshader` now converts the three artist-facing ambient greens from sRGB to linear and mixes that pigmentation with the original Ground048 soil scan. This restores brown organic grain between leaves instead of replacing the substrate with smooth pale green. Separate gravel, litter, colony moss, silt, terrain geometry and all simulation inputs remain unchanged. The successful source delta is this shader only.

Matched native creek close/medium and bayou context images visibly improve the substrate contrast. The scene is still stylized: exposed floor remains substantial, some moss boundaries look painted, and the water film/table optical transition remains conspicuous. This is a retained improvement, not forest convergence.

Four forced-build native runs passed source stability and paused digest checks. Both ROOT and candidate creek/bayou canonical ecology and 100-second fluid states exactly match the retained understory baseline. ROOT/candidate creek 102-second continuation also matches. Four ROOT void full frames are exactly RGB(0,0,0). The ROOT creek includes 32 camera/material frames and 48 actual-fluid frames; representative native frames were inspected, not continuous playback. Performance was not evaluated.

Evidence: `build/forest-reference-20261015/forest-underlayer-20261016/final-validation.json`, `final-void.json`, `review.html`. Native ROOT runs: `forest-underlayer-main` and `forest-underlayer-main-bayou`. Retained source hash: ff0f42b150a27dd37c6c28a304c28292f06bff1ea9320bc7129670cb519791fd.
