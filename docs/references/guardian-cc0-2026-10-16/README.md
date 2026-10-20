# CC0 guardian donor search — 2026-10-16

Original downloads and source-page snapshots are local; SHA256 receipts are in manifest.json. No donor has replaced a runtime guardian. Downloaded does not mean approved quality.

| Guardian | Source | Status / adaptation |
|---|---|---|
| Snake | https://opengameart.org/content/cobra-0 | Downloaded cobra.zip; Micket, CC0, rigged 330-triangle untextured cobra. Remove hood, remodel boa skull/body, new surfacing. |
| Hoatzin | https://opengameart.org/content/bird | Downloaded bird_0.blend; gonzalfj/Fabio Gonzalez, CC0 textured rigged animated generic bird. Inspect anatomy before crest/tail conversion. |
| Pangolin | https://opengameart.org/content/armadillo-foe | Downloaded FBX/OBJ pack; OveractionGameStudio, CC0. Cartoon armadillo is only a fallback; replace bands with scales, remodel head/tail. |
| Porcupine | https://opengameart.org/content/rat-0 | Downloaded rat_godot.zip; br-n518, CC0 animated 270-triangle rodent. Very distant fallback needing full proportions, fur and quills. No exact CC0 porcupine verified. |
| Monitor | https://sketchfab.com/3d-models/cc0-japanese-grass-lizard-18012190e6a84e74b4444119e2a1cf0f | ffish.asia/floraZia CC0 grass-lizard listing, approximately191k triangles. Not downloaded; scan cleanup/retopology and varanid proportions required. Primary fetch failed; reconfirm downloadable license and inspect before use. |
| Coati | https://www.meshy.ai/3d-models/Coati-v2-019d0359-9768-70b2-b913-ef5a3fec2e81 | Exact coati listing explicitly CC0, cyber_fox; AI-generated. Not downloaded; account/download availability and geometry/rig quality unverified. |

Further candidates: https://blendswap.com/blend/8422 (CC0 Woodpecker, crest-bearing bird base; download not acquired); https://pinshape.com/items/30304-3d-printed-raccoon (explicit CC0 MorenaP raccoon STL, account required, retopology needed for coati). Meshy pangolin gallery https://www.meshy.ai/tags/pangolin has natural and fantasy entries but specific naturalistic model license still needs verification. Its gallery requires a free account for downloads. No paid API or account creation used.

Excluded: exact Sketchfab Hoatzin https://sketchfab.com/3d-models/hoatzin-607caf85a0f24586bb8b70a124d168a1 is CC Attribution/NoAI, not CC0. Generic free downloads and website-footer licenses do not establish asset CC0.

Next: inspect the four local donors in isolated Blender background processes with auto-execution disabled; audit meshes, armatures, actions, embedded textures, orientation and topology, render comparable previews. Choose a donor only when it is a credible visual improvement; retain current guardians otherwise. Seek a better naturalistic porcupine and exact pangolin in parallel with local evaluation. Preserve current generator and rig contracts.

## Inspection result, next work interval

CPU clay previews and Blender audits completed for cobra, bird and armadillo (build/guardian-polish-20261016/cc0-donors). All three retained as references only; inspected against current native guardian clay views. Their low-detail anatomy does not justify replacing the existing detailed boa, hoatzin or pangolin. Cobra:117 base vertices/165triangles before mirror,30bones,no actions,missing images. Bird:716vertices/1428triangles,19bones,one0–171frame action,packed640x400image. Armadillo: static cartoon FBX/OBJ; OBJ references nonexistent Tatu_maligno.mtl despite a supplied Armadillo.mtl, so clay audit intentionally does not imply working material import. Rat COLLADA contains one geometry/skin controller and100animation channel nodes; not a reviewed animation count or full rig preview.

Stronger leads/access: CC0 Woodpecker download https://blendswap.com/blend/8422/download explicitly requires sign-in. CC0 grass-lizard scan canonical page https://sketchfab.com/3d-models/cc0-japanese-grass-lizard-8ec4811184f34fc5b6699eca34008f68 is383.8ktriangles, download needs account per https://sketchfab.com/developers/download-api/downloading-models . Other Sakishima scan: https://sketchfab.com/3d-models/cc0-sakishima-tree-lizard-15f772d7febd45a48d3cecf1cef6fe10 . No authentication gate bypassed. 3DAssets.dev reptile pack claims CC0 but manifest request returnsHTTP403; no model acquired, not a validated replacement. Meshy porcupine gallery currently mostly toy/fantasy/Sonic-like designs; do not count these as a naturalistic porcupine.

Continue source acquisition/evaluation before anatomical replacement. Meanwhile address separately authorized close flora contacts; this does not count as completion of all four guardian passes.
