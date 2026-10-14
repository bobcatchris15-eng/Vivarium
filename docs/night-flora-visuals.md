# Night flora visual contract

The `pampas` shape is used by plumegrass. It combines a low tuft of narrow, arching leaves with several curved stalks. Each stalk carries two airy plume clusters at distinct heights; the terminal tuft is the largest. Broad, tapering rachilla blades and fine branch hairs create a soft feathered fringe. The rachilla organ marker bypasses the green/photographed leaf palette so the plume remains pale straw. Geometry is deterministic and reduces rachilla count, tube resolution, and basal leaves through the three flora detail tiers. Its local origin remains at the basal crown so species placement continues to root at the substrate.

Glowbriar uses the dedicated `shrub_glowbriar` form: a low spreading tangle of arching canes, alternating narrow leaves, and tiny underside mint nodules. Its material emission mask targets only those marked nodules, separating its silhouette and glow organs from the hanging lanterns of lanternbrush.

The other six luminous species reuse the existing reed, paired-leaf herb, carpet, mushroom-cluster, and fern meshes. `NightFloraMaterials.Bind` reads each definition's tint and strength and configures shared emission uniforms for ordinary flora and photographed-leaf materials. Vascular species emit on foliage vertices; moss, lichen, and fungus surfaces use a broad body mask. Emission is material-based, with no per-instance point lights.

`NightFloraMaterials.NightFactorAtSeconds` follows the presentation `DayNightClock` cycle only: it stays off through day, eases from dusk at 660 seconds to full night by 900 seconds, holds until dawn at 1140 seconds, then fades to zero at the 1200 second wrap. The simulation clock, ecology, and saved world state are not read or modified. A daylight preset at 450 seconds should return zero, 900/1020 seconds should return one, and dawn at 1170 seconds should be between zero and one.

Aquatic siltstars remains a floor-spreading carpet mesh placed by existing flora placement. The visual work adds no water-surface anchor, depth ceiling, or floating geometry. Its unlimited depth behavior belongs to the species/content definition.

The shader glow gives visible self-emission, but this material change does not add scene illumination to nearby terrain or organisms. Integrated day/night captures should be reviewed at normal gameplay framing before treating the glow as visually accepted.
