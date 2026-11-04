# Guardian roster and independent selection

Approved by the user on 2026-10-29, with the correction that guardian choice belongs entirely to the user. This supersedes species-to-pilot-tree pairings and the proposed habitat-based assignment of the four additions.

## Selection

- The target roster is ten guardian animals. The user can select any guardian independently of pilot tree, habitat, or preset. Random and None remain explicit options.
- Trees and water can provide physical support, shelter, climbing surfaces, and behavioral opportunities. They do not choose or restrict a guardian species.
- Random selection uses the world seed and the independent `world.guardian` random stream. Changing the pilot tree must not change the selected species.
- Legacy `auto` selections resolve like Random. A saved guardian individual retains its identity when loaded, even if its descriptor still says `auto`.
- Preserve existing guardian IDs and asset archetype paths so saved individuals remain compatible.

## Approved art direction

A recognizable animal with one quietly impossible feature. Preserve believable anatomy, weight, and movement. Put whimsy into a small anatomical flourish and distinctive mannerisms. Avoid accumulating ornaments or using a shared glow effect to make every guardian fantastical.

Names below are working names. Features and mannerisms are authored concepts, not assertions about real animal behavior.

| Working name | Base and identifying anatomy | Fantastical feature | Mannerism |
| --- | --- | --- | --- |
| Budcrest | Hoatzin; rust plumage, blue face, crest, branch-clambering silhouette | Curled crest-feather tips unfurl into a loose crown when interested | Solemn, slightly awkward climbing; an extravagant crest display over a small discovery |
| Coneback | Pangolin; tapered head, heavy tail, overlapping armor | Scale tips curl outward slightly, giving the curled animal a pinecone silhouette | Noses objects aside, then sits back with forepaws tucked together |
| Leafbrow | Tree boa; muscular coils and a distinctly serpentine head | Two flexible leaf-shaped brow scales tilt with attention; these are skin, not attached plants | Settled coils with an attentive moving head and brows |
| Emberthroat | Asian water monitor; powerful low body, lateral tail, investigative tongue | A short row of rounded amber throat scales catches warm light like polished beads | Heavy exploration interrupted by delicate examination of small objects |
| Curltail | Coati; flexible snout, dexterous forefeet, upright ringed tail | Tail tip forms a loose spiral with a slightly fuller brush of fur | Tail uncurls during concentrated rooting and rises when distracted |
| Chimequill | Brazilian porcupine; rounded body, prehensile tail, contrasting quills | A few hollow shoulder quills make a soft wooden chime during a shake | Exact climbing, deliberate settling, occasional sleepy rattle |
| Fernback | Thylacine; long muzzle, rounded ears, lean body, heavy tapering tail | Dark hindquarter stripes branch into simple fern patterns toward their lower ends | Quiet patrols, attentive pauses, and a long stretch; reserved curiosity |
| Dewhorn | Saiga; distinctive soft nose, compact body, slender legs | Short translucent amber horns curl inward at the tips, collecting droplets | Nose-led investigation and an occasional buoyant hop |
| Hushbill | Shoebill; tall upright silhouette and enormous sculptural bill | Two long crest feathers curl into a loose question mark that straightens when startled | Long stillness, slow head turns, and a formal greeting bow |
| Ruffleback | Mata mata turtle; flat rugged shell, broad head, fringed outline | Neck fringes form loose ribbon curls that open underwater and fold on land | Patient resting, purposeful bottom-walking, nose breaking the water surface |

### Fernback priority brief

Develop the thylacine concept first. Keep the long head, striped rear half, and substantial tapering tail readable. Palette: warm grey-brown coat, pale chin and belly, charcoal fern stripes, muted reddish ear interiors. Five or six broad branching marks should remain legible at gameplay distance, with finer variations reserved for close views. Independent ear turns, a slight head tilt, and a tentative sniff express its watchfulness.

Anatomical references: [Australian Museum](https://australian.museum/learn/australia-over-time/extinct-animals/the-thylacine/) and [Tasmania Parks and Wildlife](https://parks.tas.gov.au/discovery-and-learning/wildlife/tasmanian-tiger).

## Implementation status

The runtime catalog now contains all ten animals. Fernback (Thylacine), Dewhorn (Saiga), Hushbill (Shoebill), and Ruffleback (Mata mata) are selectable and spawn with distinct procedural starter models. These are functional initial representations, not finished skinned art assets. Detailed visual acceptance remains pending.

Implemented in this selection change:

- Removed the catalog's pilot-tree association field and lookup.
- Removed tree-based legacy automatic species selection.
- Built guardian picker entries from the playable catalog, with an independent-choice tooltip.
- Made review scenes use a common tree fixture, optionally selected with `--guardian-tree <pilot-tree-id>` (including `none`).
- Replaced pairing assertions with independent-choice and legacy save regression coverage.
- Added the four new runtime definitions, behavior profiles, and stable save identities.
- Added species-specific starter geometry and simple leg/head motion for the four additions. Their detailed GLB assets will take precedence when authored.
- Added a headless Godot check of the real New World picker, selected world descriptors, and production model instantiation (`--guardian-review <output-dir> --guardian-picker-check`).

Remaining art and gameplay work:

- Refine the six existing assets with their approved features and mannerisms.
- Replace the four starter models with detailed thylacine, saiga, shoebill, and mata mata meshes, materials, rigs, and animation clips. Refine their species-specific locomotion and feeding contact.
- Validate all ten in native gameplay at the normal camera distance, including movement, feeding, support contact, and water behavior where relevant.
- Validate the ten-way silhouette distinction: crest, armored taper, coil, sprawling lizard, upright curled tail, quilled ball, long prowler, nose and horns, towering bill, flat fringed shell.

Physical home/support data in existing saves remains compatible. A guardian's use of a tree in the simulation is separate from the removed species assignment rule.

## Selection-change validation

- `dotnet build Vivarium.sln --nologo -v quiet`: passed, zero warnings/errors.
- `dotnet build game/Vivarium.csproj --nologo -v quiet`: passed, zero errors; three warnings in unchanged LoamGroundReview, SmokeRunner, and GuardianSkinContact code.
- `dotnet test tests/Vivarium.Sim.Tests/Vivarium.Sim.Tests.csproj --no-build --filter FullyQualifiedName~Guardian --nologo -v minimal`: 94 passed, zero failed/skipped.
- `git diff --check`: passed.
- No remaining `AssociatedPilotTreeId` or `ForPilotTree` references in C# source, game, or tests.
- Native headless Godot check: ten guardians, twelve picker entries including Random/None, all four new selections spawn with finite species-specific geometry and no generic fallback boxes.
- No native visual review was run for this selection change. The existing editor sessions were preserved. Starter geometry is not final art or visual acceptance.
