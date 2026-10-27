# Sundew dew isolation test — 2026-10-24

The completed native test shows that **glossy dew is not the sole cause of the black flecks**. Black pin/square flecks remain when the dew beads are hidden and when the beads retain their geometry with neutral matte shading. This does not exclude a contribution to individual cyan/magenta or bright points, and does not identify the remaining cause.

Successful evidence: `build/photorealism/sundew-dew-probe-02/`. Open `review.html` for original, hidden, neutral, matte-tinted, bead mask and restored captures. Native run: exit0, source stable, zero engine errors,39PNG. Fixed world/camera/daylight and manual exposure during the six probe captures; shader resources restored afterward. Source and binary hashes are retained with the run.

The verification mask identifies the selected bead geometry:70,122 uploaded bead vertices and8,790 green mask pixels in the inspected patch rectangle (590,340)-(940,530). A dedicated UV2.y=-6 bead tag avoids both color matching and UV2.x leaf-face ambiguity. The baseline production shader still treats these vertices as sundew tissue exactly as before. Shader alternatives are in-memory diagnostic clones only.

In rectangle (650,380)-(875,500), near-black display pixels (all RGB channels below8) count52 original,39 hidden,56 neutral,35 matte-tinted,40 restored. These counts support persistence, not exact attribution: small temporal differences remain between captures. A Luna worker independently inspected the source and four native images and confirmed the narrow conclusion.

The initial color-selected probe01 passed runtime integrity but failed bead-mask verification in the target patch; it is excluded from causal conclusions. No production shading fix is claimed or promoted. Existing open editors and unrelated dirty work were preserved.
