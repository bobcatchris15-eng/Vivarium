# Flora review handoff — 2026-11-17

Resume owner review at **sheet 7 of 21**. Sheets 1–6 have feedback and source corrections; the owner has not yet accepted the corrected renders. Sheet 7 was shown but has no feedback yet.

Open [the portable gallery](index.html#sheet-07). It contains all 125 species, in two-column, three-row sheets, with matching previous-form references. The last sheet has five species. **The sheets are frozen images from the start of review, before the corrections below.** Do not mistake them for the latest code's appearance. The two capture reports record the original native Godot run and module provenance. All gallery links work after cloning, without the ignored `build/` directory.

Each of sheets 1–6 also has a linked **revision draft** assembled from the newest targeted native captures, with unchanged accepted species carried forward from the original run. These drafts are not owner-approved. Their per-species run names are recorded in `review-order.json`.

## Review workflow and artistic direction

Show one sheet at a time. Record accepted forms and precise changes; delegate each materially new task to fresh Terra/Luna agents. Root coordinates integration and actual Godot renders. Preserve accepted silhouettes. Avoid generic radial sprays, uniform straight stems, identical tiers, and smooth geometric substitutes for organic details. Growth habit is the organizing structure, not a minor parameter difference. Lush, lifelike, believable, whimsical forms; no new bioluminescence. Performance changes may not reduce visual richness. Save compatibility is not required.

Moonbark and Shiverleaf were explicitly praised before this review and should retain their accepted forms. Do not broaden changes to them without feedback.

## Feedback through sheet 6

| Sheet | Accepted forms | Requested corrections |
| --- | --- | --- |
| 1 | No final corrected-form acceptance yet | Plumestep: wider arching basal blades independently entering a narrow tussock; keep new seedhead structure, add rainbow feathery fronds around seeds. Bannerreed: keep new culms/heads, restore wavy curling leaves up stalk. Coalspike: single tall black-headed stalk with arching basal leaves, thicker wrap-jointed/sheathing stalk allowed. Banksorrow: branching limbs, branches and twigs continuing into tendrils of small alternating lenticular leaves, ragged curtain nearly to ground. Hollowcrown: smooth waisted hollow bole, vulva-shaped opening with grown-over curled rim, substantial smooth branches from upper poll/lobes, sharply deeply lobed oak/holly leaves. Witchberry: strongly twisting/doubling-back gnarled trunk(s), short twisting branches and leafy twigs, ginkgo fan sprays at joints, few asymmetric lumpy whorled burls and irregular branch thickness, small dark red fruit clusters. |
| 2 | Antlerlace upright form, Ashcomb, Ashfingers upright form, Ashwire, Barbarrow, Barrowbole | Missing underlying lichen/moss colony mats. Preserve upright forms. |
| 3 | Bitterbine, Blue Sundew Mat, Bonecup; Bogglass upright form | Blackneedle: restore intricate heads and black leaf spines. Bogfringe: spread colony farther with rippled/ruffled watercress-like leaves. Bogglass: missing mat. |
| 4 | Brightknot, Brooklace, Candlecoral | Burlmantle: spread out long spear-shaped fronds fanning from center. Burrflag: straight-sided blades with rounder tips, restore dark spiny burrs. Butterglint: complex layered heavy flowers bending stems into droop. |
| 5 | Cinderberry, Clinglace; Coinrunner existing form apart from density | Capstar: missing mat. Carrion Bell: restore skirt, align head to shaft, add vertical head ridges. Cloudtuft: cloudier open-milkweed/cotton tuft, denser narrow slightly sinuous interwoven blades. Coinrunner: more runners and branching. |
| 6 | Cragset, Deepquill, Dewbonnet fungus | Cradlecup: thicker eggcup-like fungal stem. Cragparasol: restore separated upward-curling leaf tufts each with deep blue fuzzy flower spike; remove mid-trunk stairstep. Duskbristle: larger footprint, more steeper/near-vertical blades, seedhead with sideways seeds arranged as a single dense spiral up cone/head. |

## Implementation and remaining review

All requested recipe corrections through sheet 6 have been implemented in the checkpoint, with these qualifications:

- Low herbs, upright herbs, aquatic/fern forms, and vines now have dedicated growth-habit recipes rather than a common generic shrub form. New partial `HerbGraphSpecies.Habits.*.cs` files must travel with the commit.
- Banksorrow terminals use continuous position, tangent, and radius from fine parent branches. The catalog validation checks this contract.
- Hollowcrown has `AxisWaist` support and upper scaffolds, but its hollow opening still has flat cut-wall edges in the compiler. **The requested grown-over curled rim and truly merged branch junctions remain open.** Verify camera orientation too; the initial catalog view hides the side opening.
- Witchberry has a contiguous contorted bole, ginkgo-like fan sprays, dark red fruit, and opt-in `AxisGnarling` for asymmetric swelling, creasing and bark UV distortion. A native render exposed cap collars; burl/limb chains were subsequently welded with `Continuous`. **Further art tuning is still needed:** major swellings can read too rounded, roots need scrutiny, the fan leaf lacks a true recessed center-tip notch, and the overall form has not been accepted by the owner.
- Coalspike's sheath-like laminae are close to the stalk, but are not true encircling tubular sheaths.
- Lichen/moss standalone specimens now include overlapping low colony geometry. Live coverage already has a separate terrain-conforming sheet; extraction selects a shoot subtree, so added root-sibling understory is excluded. Native first attempt looked like detached coins and was replaced with overlapping curled blades. **Live coverage verification and final mat art review remain open.**
- Blackneedle's independent black spines approximate the curled blade surface; inspect close-up for floating or buried joins.
- Carrion Bell's longitudinal ribs currently use a species-specific radial compiler branch; inspect angularity at reduced LODs. Cloudtuft fiber readability and geometry cost need review.
- Cragparasol tuft separation, upward curl, blue spike density; Duskbristle seed visibility and Cradlecup cup/stem joining need native art review.
- Publication native review specifically found **Cragparasol's blue spikes are not visibly established at the whole-plant camera**, and **Cloudtuft still reads as smooth pods with sparse fibers rather than the requested cloudy cotton/milkweed mass**. These remain unfinished art tasks despite source additions. Carrion Bell's skirt and vertical head relief are visible, but still need owner review.
- The supported/fallback climber continuation path can still progressively clamp segment radii at parent attachments. This is a known follow-up when reviewing those species, not yet fixed.

## Validation and artifacts

The publication check runs sequential solution/game builds, headless boot with save roundtrip and zero invariants, the 17 focused plant geometry tests, and the full 125-species numerical catalog sweep. Native targeted captures were also run through the actual Godot renderer. These checks establish build/geometry/runtime viability, not owner art acceptance or full-world performance acceptance.

Publication results: both builds passed; `VIVARIUM_BOOT_OK`, `roundtrip=True`, `invariants=0`; 17 focused tests passed; catalog/content/completed counts all 125, zero errors and zero duplicate fingerprints. The portable gallery has 48 sheets including the six revision drafts, with zero broken local links. A focused staged-text credential-pattern scan and `git diff --cached --check` passed.

Local evidence on the originating workstation is under `build/plant-graph-tuning-20261115/`:

- `publication-build.log`, `publication-game.log`, `publication-boot.log`, `publication-selftests.log`, `publication-validation.json/log`.
- `sheets01-04-revisions/plant_catalog/` includes sheet 1–4 revised forms and macros, including the second Witchberry weld/mat iteration.
- `publication-revisions/plant_catalog/` includes Carrion Bell, Cloudtuft, Coinrunner, Cradlecup, Cragparasol, Duskbristle.
- Original full 125 mature current/previous renders: `review-sheets-current` and `review-sheets-previous`. The portable sheets here preserve their review content; individual full-resolution originals stay local.

Regenerate mature catalog captures with the bundled Godot console:

```powershell
& ./tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe --path ./game -- --repr-review <ABS_OUTPUT_DIRECTORY> --repr-demo plant_catalog --plant-catalog-mature-only --plant-catalog-species <comma-separated-ids>
```

Add `--plant-catalog-macro` for detail shots or `--plant-catalog-legacy` for previous-form references. The fixed review order is in [review-order.json](review-order.json). Original gallery composition/export scripts are in `experiments/plant_catalog_test/`.

## Workspace preservation

The originating checkout also contains pre-existing unrelated dirty legacy Geometry, flora height metadata, texture import metadata, project settings, historical docs and UID files. Those are excluded from this checkpoint and left untouched. Therefore a fresh checkout reproduces the committed graph work but may differ from the originating machine's legacy-reference code/settings. The frozen reference sheets preserve what the owner actually reviewed. Do not reset, clean, or overwrite user saves/editor state to resume this work.
