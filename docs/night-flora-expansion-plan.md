# Night flora and plume grass

Authorized 2026-10-10 alongside the dynamic-water replacement. User confirmed all seven night species glow. An eighth species is pampas-like grass with several plume tufts along each stalk.

| Species | Family / habitat | Form and night identity |
|---|---|---|
| Plumestep grass (`plumegrass`) | Terrestrial tufted herb | Arching blades, tall jointed stalks, several soft feathery plume clusters at different heights; cream seed heads. Non-bioluminescent. |
| Glowbriar (`glowbriar`) | Woody shrub | Dedicated low, spreading arching canes with small alternating narrow leaves and mint nodules along the cane undersides. Distinct silhouette from lanternbrush; no reused hanging-lantern crown. |
| Lanternsedge (`lanternsedge`) | Waterside bank herb | Upright wet-margin growth with luminous nodes/seed structures. |
| Tidebell (`tidebell`) | Waterside shallow emergent | Distinct bell/whorl silhouette at the pond edge, night emission. |
| Siltstars (`siltstars`) | Fully submerged floor grower | Low lateral carpet rooted along the silty floor; underwater establishment required, explicit unlimited maximum depth; luminous star-like tips. Never floated to the pond surface. |
| Ghostbonnet (`ghostbonnet`) | Decomposer fungus | Clustered caps with luminous gill/cap regions, damp litter/wood habitat and fungal food coupling. |
| Starmoss (`starmoss`) | Moss | Low cushion/carpet with small luminous tips, damp shade niche. |
| Moonfern (`moonfern`) | Fern herb | Divided arching fronds and localized luminous accents, shaded moist ground. |

Glow eases in during dusk, remains visible through night, and fades during dawn. It uses the existing presentation lighting clock; ecology, simulation speed and saved state must not depend on the night phase. Existing species default to zero emission. Avoid per-plant light nodes and excessive bloom; the plants should retain readable organic form.

Implementation is split into isolated Luna low content/schema/ecology work and Luna medium geometry/rendering work. Content adds validated emission color/intensity and an explicit unlimited-depth flag rather than a large maximum-depth sentinel. Geometry adds the multi-plume stalk form and distinct night species forms with bounded LOD meshes. Rendering binds species settings through a shared production material helper and a night envelope.

Acceptance: eight validated catalog entries available through existing placement groups; exactly seven emit at night; legacy species remain non-emissive; deep aquatic placement succeeds beyond the old 5 m parser ceiling while dry placement fails; lateral aquatic propagation retains ground anchoring; finite distinct meshes across detail tiers; multi-tuft stalk anatomy; game build; runtime shader compilation; paired day/night review using production materials. Gallery captures verify materials and morphology, not integrated ecological performance or the water solver. Preserve the active user world and all unrelated dirty changes.

Status: content and visual workers dispatched; production integration and visual acceptance pending. Candidate names may change only to resolve an existing catalog conflict; habitat counts and requested forms remain binding.
