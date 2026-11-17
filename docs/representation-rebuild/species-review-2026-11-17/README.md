# Flora review handoff — 2026-11-17

**Owner feedback is recorded for all 21 sheets, and the dispatched implementation batches have completed.** Final agent reports the playable Game build, all 28 plant checks, full 125-species numerical sweep (zero errors/duplicate fingerprints), and isolated headless boot with successful roundtrip and zero invariants passed. Corrected forms remain unaccepted unless explicitly stated; the owner is judging them during gameplay.

Open [the portable gallery](index.html#sheet-21). It contains all 125 species, in two-column, three-row sheets, with matching previous-form references. The last sheet has five species. **The sheets are frozen images from the start of review, before the corrections below.** Do not mistake them for the latest code's appearance. The two capture reports record the original native Godot run and module provenance. All gallery links work after cloning, without the ignored `build/` directory.

Each of sheets 1–7 also has a linked **revision draft** assembled from the newest targeted native captures, with unchanged accepted species carried forward from the original run. These drafts are not owner-approved. Their per-species run names are recorded in `review-order.json`.

## Review workflow and artistic direction

Show one sheet at a time. Updated owner instruction: dispatch fixes to fresh Terra agents with high reasoning (`gpt-6.1-terra`, `high`) and move straight to the next sheet while they work. Record precise feedback and coordinate integration without making the owner wait through a root render/revision loop for every sheet. The owner plans to play afterward and report what they like or dislike; dispatch does not imply art acceptance. Preserve accepted silhouettes. Avoid generic radial sprays, uniform straight stems, identical tiers, and smooth geometric substitutes for organic details. Growth habit is the organizing structure, not a minor parameter difference. Lush, lifelike, believable, whimsical forms; no new bioluminescence. Performance changes may not reduce visual richness. Save compatibility is not required.

Moonbark and Shiverleaf were explicitly praised before this review and should retain their accepted forms. Sheet 13 explicitly authorizes Moonbark branch alignment and smooth trunk junctions only; preserve its broader silhouette and foliage. Do not broaden changes beyond owner feedback.

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

## Sheet 7 feedback — 2026-11-17

Sheet 7 has owner feedback. Its revised forms still require native inspection and owner acceptance. The direction is believable, lifelike, mildly fantastical fictional species rather than copies of particular real species.

| Species | Requested correction |
| --- | --- |
| Duskpuff | A cluster of distinct, organically varying dark slate-grey puffballs. |
| Emberbracken | Delicate fern fronds arching upward from a vibrant red, striated runner. |
| Embercrown | Preserve the broadly accepted form; increase branch sinuousness and permitted footprint. |
| Embercrust lichen | Restore its underlying lichen mat. |
| Embercup | Restore its underlying moss mat. |
| Emberfan fungus | Fantastical polypore/bracket fungus growing attached to logs and tree trunks. |

Open the [sheet 7 revision draft](sheet-07-draft.jpg). It contains six fresh native captures from `build/sheet7-revisions/plant_catalog/`; the frozen current/previous sheets remain the original review references. Capture/module provenance is in [the sheet 7 report](capture-report-sheet-07-draft.json).

The recipes now provide individually varied slate puffballs; an arching fern on a red fluted, jointed runner; wider, more sinuous Embercrown branches; continuous lichen and green moss mats; and irregular, banded bracket fans. Emberfan's production support query samples actual log geometry and compiled graph trunk wood, preserves world-up orientation, and hides unsupported bodies on the next presentation scan. This adds no save-schema change. The gallery's simple support pole illustrates the bracket form; separate native [log](emberfan-log-contact-draft.jpg) and [tree-trunk](emberfan-trunk-contact-draft.jpg) views use actual production host meshes and the shared production attachment pose. Their [separate report](capture-report-sheet-07-support.json) records provenance.

Sheet 7 validation: solution and final Game builds passed; all 18 focused plant tests passed, including support rejection/contact/movement/removal; the full catalog completed all 125 species with zero errors and zero duplicate fingerprints; headless boot passed with `roundtrip=True` and `invariants=0`. Native captures completed and root inspected the six-species sheet plus both wood-contact views. Independent source review has no blocking findings. One pre-existing nullable warning in `PlantFieldDemo.cs:158` and the catalog's existing seven Texture RID/one ObjectDB exit warnings remain. Local evidence is in `build/sheet7-*.log` and `build/sheet7-validation.json`.

Art acceptance remains pending. Duskpuff still has some faceting, pear-like variation and visible sheen; the sheet is provided for owner judgement rather than declaring that treatment accepted. Production attachment is derived rather than a persisted host relationship, and paused direct host mutations without version invalidation can remain stale until the next tick or explicit invalidation. These captures do not establish full-world performance or live coverage art acceptance.

Reproduce the wood-contact fixture by adding `--plant-catalog-support-proof` to a native `plant_catalog` review command. That flag selects a separate two-shot support mode; run the ordinary six-species command separately for the sheet images.

## Sheet 8 feedback — 2026-11-17

Completed by `sheet8_fixes`, a fresh `gpt-6.1-terra` agent at `high` reasoning. Agent reports all 18 plant tests and the playable Game build passed. No new native renders or owner art acceptance; judge changes during gameplay.

| Species | Owner direction |
| --- | --- |
| Fenbead | Accepted; preserve. |
| Fencomb | More sinuous and twisting, with milfoil-like fine anatomy. |
| Fenfoam | Denser flower head; Queen Anne's lace analogy, but thicker and heavier. |
| Fenforge | More asymmetrical and branching trunk; improve organic root/trunk joints. |
| Fenhook | Accepted; preserve. |
| Floodlace moss | Restore the moss mat. |

All analogies describe believable, lifelike, mildly fantastical fictional species rather than copies of real species.

## Sheet 9 feedback — 2026-11-17

Completed by `sheet9_fixes`, a fresh `gpt-6.1-terra` agent at `high` reasoning. Agent reports all 18 plant tests and the playable Game build passed (existing nullable warning only). No native visual review or owner art acceptance. Ghostbonnet gills/torn veil and Glassfinger fine surface detail remain approximations of previous forms; judge during gameplay.

| Species | Owner direction |
| --- | --- |
| Glasslily | Spread pads/leaves across the water surface. |
| Frosttussock | Independent stems and blades rising vertically from the root, without joined stalks; blades arc outward into a fringe, stems stand tall. |
| Ghostbonnet | Basically restore previous form. |
| Glassfinger | Basically restore previous form. |
| Glassrush | Short cattail-like reeds; about half of taller stems have tail heads, half have lacy yellow-green blossoms. |
| Glass Antlers | No feedback given; preserve without inferring acceptance. |

## Sheet 10 feedback — 2026-11-17

`sheet10_fixes` implemented Heddlelace, Greywatch, Hookthicket and Hallowsting. Agent reports 18/18 plant checks and the Game build passed (existing nullable warning). Glowbriar was left unfinished because end-to-end axis welding does not fuse branching joints; follow-up implementation is dispatched to `glowbriar_junctions`, another fresh Terra high agent. That follow-up has now completed; see background completion updates below. No native visual review or owner art acceptance.

| Species | Owner direction |
| --- | --- |
| Heddlelace | Seed heads tilt slightly, each facing a substantially randomized direction; use stable seeded variation. |
| Greywatch | Branches arch up and sharply back down; more natural foliage canopy. |
| Glowbriar | Correct root/stem junctions in the actual dedicated generator. |
| Hookthicket | Lusher with more leaf clusters; noticeable curved, shiny black thorns along stems. |
| Hallowsting | Broadly okay conditional on foliage extending up the entire stem and slight organic stem bends. |
| Hagshair lichen | No feedback given; preserve without inferring acceptance. |

## Sheet 11 feedback — 2026-11-17

Dispatched to `sheet11_fixes`, a fresh `gpt-6.1-terra` agent at `high` reasoning. Implementation/validation completed; see background completion updates below. Catalog edits and builds are coordinated with the separate Glowbriar junction follow-up.

| Species | Owner direction |
| --- | --- |
| Lacemantle | More organic/natural bends and knots, lusher foliage, and drooping pollen structures at the ends of some twigs. |
| Lanternbrush | Wider basal footprint; stems rise more vertically as a bunch from distributed origins, rather than fanning from one point. |
| Hushclover, Kinkcane, Lanternsedge, Marrowbloom | No feedback given; preserve without inferring acceptance. |

## Sheet 12 feedback — 2026-11-17

Owner says these forms look pretty good overall. Preserve Marshkettle, Miremyrtle, Mirrorleaf and Mirrorstrap fern. Requested edits dispatched to `sheet12_fixes`, a fresh Terra high agent; source and build locks are coordinated with sheet 11 and Glowbriar work. Continue to sheet 13; implementation/validation pending.

| Species | Owner direction |
| --- | --- |
| Mirepocket | Replace straight/angular joints with organic bends; broaden basal footprint with distributed, more upright stems, as requested for Lanternbrush. |
| Mirespire | Thinner, longer needles, with leaflet-like fringes on each side to make canopy visually denser. Preserve overall form. |

## Sheet 13 feedback — 2026-11-17

Dispatched to `sheet13_fixes`, a fresh Terra high agent. It must wait for sheet 12 to release shared Woody source/build locks before implementation. Continue to sheet 14; implementation/validation pending.

| Species | Owner direction |
| --- | --- |
| Moonshell lichen | Restore underlying lichen mat. |
| Moonbark | Align branches and smooth junctions with trunks; preserve the rest of the accepted form. |
| Nutglade | Wider basal footprint, more upright stems from distributed origins rather than a single fanning point. |
| Mistviolet, Mooncoin, Moonfern | Good/accepted; preserve. |

### Background completion updates

Glowbriar follow-up completed: each runner now curves into its cane through a welded ring; inner runner ends sit inside a broadened stool. This is not a general branching mesh union. Agent reports the new geometry check spans three seeds, three ages and three LODs, all 19 plant checks passed, and the Game build passed with the existing nullable warning. Gameplay art judgement remains open.

Sheet 11 completed: Lacemantle bends/knots/foliage and pendant pollen racemes, plus distributed upright Lanternbrush bases. Agent reports all 19 plant checks and the playable Game build passed. No native render or owner art acceptance.

## Sheet 14 feedback — 2026-11-17

Dispatched to `sheet14_fixes`, a fresh Terra high agent, after sheet 13 released source/build locks. Implementation/validation pending; continue to sheet 15.

| Species | Owner direction |
| --- | --- |
| Pondstars | Duckweed-like floating leaflets, each a 4-, 5-, or 6-pointed star. |
| Pearl-cushion moss, Pinequilt moss | Restore underlying moss mats. |
| Raincup Pitcher | Textured organic outer pitcher surfaces with patterned swirled brown/green/yellow. Preserve pitcher anatomy. |
| Paddlerib, Prismstar | Okay/accepted; preserve. |

Sheet 12 completion: Mirepocket curved/distributed basal shoots and longer, finer Mirespire fringes implemented. Agent reports 19 plant checks, full 125-species catalog (zero errors/duplicate fingerprints), Game build and diff check passed. No native captures or owner art acceptance.

Sheet 13 completion: early Moonshell mat, aligned continuous Moonbark trunk and embedded/tangent-aligned lateral limbs, distributed upright Nutglade bases implemented. Moonbark lateral forks remain overlapping separate surfaces rather than a mesh union; visual smoothness needs gameplay judgement. Agent reports all 20 plant checks, Game build (existing nullable warning), and scoped diff check passed. No native captures or owner art acceptance.

## Sheet 15 feedback — 2026-11-17

Dispatched to `sheet15_fixes`, a fresh Terra high agent, after sheet 14 released its source/build locks. Implementation/validation pending; continue to sheet 16.

| Species | Owner direction |
| --- | --- |
| Rainshield | Larger scalloped leaves; slightly sinuous stems with striations/ridges; more natural leaf/stem joints. |
| Quillfeather moss | Restore moss mat. |
| Quillsedge | Restore previous twisty/curly blades; green-to-black gradient away from stems. |
| Ringreed | Horsetail-like: no stem fringes, pronounced joints and longitudinal ridges, black-green-black terminal nubs. |
| Reekhorn | Correct lace-net alignment; more organic/delicate lace, muddy red-brown head. |
| Rain Jelly | No feedback given; preserve without inferring acceptance. |

Sheet 14 completion: mixed 4/5/6-point horizontal Pondstars, early Pearl-cushion/Pinequilt mats, and live material routing for swirled pitcher surfaces implemented. Agent reports all 21 plant checks across seeds/ages/LODs and the Game build passed (existing nullable warning). Paddlerib and Prismstar preserved. No native captures or owner art acceptance.

## Sheet 16 feedback — 2026-11-17

Dispatched to `sheet16_fixes`, a fresh Terra high agent. It coordinates with sheet 15 and waits for shared source/build lock release. Implementation/validation pending; continue to sheet 17.

| Species | Owner direction |
| --- | --- |
| Ruffle lichen | Restore underlying lichen mat. |
| Rustbellow moss | Restore underlying moss mat. |
| Shadebell | Joint alignment and smoothing in the actual dedicated generator; preserve overall character. |
| Sceptrefern, Shadewrap, Shiverleaf | Good/accepted; preserve. |

## Sheet 17 feedback — 2026-11-17

Dispatched to `sheet17_fixes`, a fresh Terra high agent after sheet 16 source/build release. Implementation/validation pending; continue to sheet 18.

| Species | Owner direction |
| --- | --- |
| Latchjaw (`snaptrap`) | Restore previous form: snap-trap leaves studded up and down a thick central column like Brussels sprouts, with a scalloped trumpet-shaped terminal flower. |
| Silverfelt | Broader basal footprint with distributed, more upright stems rather than a single fanning point. |
| Silkgleam moss, Siltstars, Silverweed, Slickmantle | Good/accepted; preserve. |

Sheet 15 completion: five requested species revised; agent reports all 22 plant checks and Game build passed (existing nullable warning). Rainshield joint is an overlapping extension, not mesh union; ribbed overlay approximates a curved stalk with a straight surface. Reekhorn lace is warped perforated surface rather than separate threads. Gameplay visual judgement remains open.

Sheet 16 completion: Ruffle/Rustbellow mats restored with early overlapping understory; Shadebell trunk sections share mesh rings and lateral forks enter along parent tangent. Lateral forks remain overlapping separate surfaces. Agent reports all 23 plant checks across seeds/ages/LODs, Game build (existing nullable warning), and scoped diff check passed. No native captures or owner art acceptance.

## Sheet 18 feedback — 2026-11-17

Dispatched to `sheet18_fixes`, a fresh Terra high agent. Implementation/validation pending; continue to sheet 19.

| Species | Owner direction |
| --- | --- |
| Starbulb | Spread basal leaves to avoid overlap; denser multicolored blooms on the top globe. |
| Sootband | Restore previous form. |
| Splitcrown | Restore previous form, with thicker main trunks and more thin branches before terminal structures. |
| Starsplit | Single large brilliant-white puffball; star-shaped split at top exposes dark-orange flesh. |
| Starmoss | Restore moss mat. |
| Spiralvine | Good/accepted; preserve. |

## Sheet 19 feedback — 2026-11-17

Dispatched to `sheet19_fixes`, fresh Terra high, queued after `sheet17_recovery`. Continue to sheet 20 without waiting for revised captures.

| Species | Owner direction |
| --- | --- |
| Streamribbon | Restore previous form with twisted, sinuously twining leaves. |
| Swanfrond | Small herbaceous shrub with round, blobbish, asymmetrical profile; one long broad dense elegant fernlike frond arches out of one side. |
| Stoneknot moss | Restore underlying moss mat. |
| Starveil, Sunscald lichen, Sunstone rosette | Acceptable; preserve. |

Sheet 17 recovery: initial source/test changes were present but incomplete; later validation reproduced `plant.sheet17Habit` failure on Silverfelt. `sheet17_recovery` must finish Silverfelt and Latchjaw after sheet 18 release, then pass the combined tests before releasing to sheet 19. Do not treat prior dispatch as completion.

Sheet 18 completion: requested five recipes implemented; agent reports Game build passed (existing nullable warning), new sheet 18 opening/geometry checks passed, and full plant suite 24/25 with only known incomplete sheet 17 failure. Starsplit has an open white shell with orange interior. No native captures or owner acceptance; combined suite must be rerun by the recovery agent.

## Sheet 20 feedback — 2026-11-17

Dispatched to `sheet20_fixes`, a fresh Terra high agent after sheet 19 source/build release. Continue to final sheet 21; implementation/validation pending.

| Species | Owner direction |
| --- | --- |
| Tankbloom | Spike flower brilliant, almost toxic red. |
| Thornmantle | Broader distributed basal footprint, more upright stems rather than common-point fan. |
| Tidebell | Redesign with dense compound leaves and bioluminescent bell blooms that rise above the water surface if submerged. Explicit exception to earlier no-new-bioluminescence direction, for this species only. |
| Trifold | Taller, with three leaflets per grouping. |
| Trumpetbind, Vaultfern | Good/accepted; preserve. |

Sheet 17 recovery completed: distributed Silverfelt stems and actual Latchjaw column/paired traps/scalloped trumpet implemented. Agent reports all 25 plant checks and Game build passed (existing nullable warning). No native captures or art acceptance.

Sheet 19 completed: spreading sinuous/twisted Streamribbon, compact asymmetric Swanfrond with one arching pinnate frond, early Stoneknot mat. Agent reports focused test proven red then green, full 26/26 plant checks, Game build and scoped diff check passed. Starveil/Sunscald/Sunstone preserved. No native captures or art acceptance.

## Sheet 21 feedback — 2026-11-17

Dispatched to `sheet21_fixes`, a fresh Terra high agent after the owner finished the final sheet. It waits for `sheet20_fixes` to release source/build locks. Both final batches remain pending until their completion reports. The final agent is tasked with updating playable assemblies and running numerical/headless checks; no new native visual review loop.

| Species | Owner direction |
| --- | --- |
| Veilblade | Restore toward previous form: wide arching blades from a distributed basal cluster rather than a single fanning point, plus a couple of tall seedhead stalks. |
| Veilfern | Restore fern anatomy. |
| Velvetweave moss, Wispthread moss | Restore underlying moss mats. |
| Widowspire | Preserve broad form; central stems thicker at base, sharply tapering to a small curlicue pistil at the end of each stem. |

## Gameplay follow-up — groundcover lighting flicker

Owner reports grass alternating lighter/darker, including with camera held still and simulation paused. Two screenshots supplied. Investigation dispatched to Terra high `grass_lighting_flicker`; root cause/fix pending. Preserve active game/editor and user saves/settings. Screenshot camera positions differ; still images alone do not establish temporal root cause.

Final batch completion: sheet 20 reports 27 plant checks and Game build passed; Tidebell has flower-only emission and water-depth-aware bell stalks. Sheet 21 reports final 28 plant checks, full 125-species numerical sweep and isolated headless boot passed after last tip weld change. No native visual acceptance was claimed. Widowspire's finest terminal tip can still be culled at LOD 2 by the existing thin-twig rule.

## Post-review round — sheet 1 feedback (2026-11-17 14:53)

Fresh 79-species post-review sheets: [post-review gallery](../post-review-2026-11-17/index.html) (native capture `build/post-review-species-20261117`, 14:37–14:39). Owner feedback on post-review sheet 1:

| Species | Owner direction |
| --- | --- |
| Plumestep grass | Recover the single central stem, with feathery rainbow-coloured fronds around the heavier seeds. |
| Bannerreed | Concentrate leaves lower on the stems, leaving the rice-like heads more clearance. |
| Coalspike | Widen base/footprint instead of fanning out from a single point. |
| Hollowcrown | Correct roots and branches to look natural and organic; fix unaligned joints and jarring regularity. |
| Banksorrow, Witchberry | Denser foliage. |

The earlier two-task dispatch never landed; implemented directly afterwards. Owner added: **Hollowcrown should take much more of its previous form** (legacy `OrganismMeshes.Hollowcrown.cs`).

Implemented (sheet-1 iteration 3, [sheet-01-iter3.jpg](../post-review-2026-11-17/sheet-01-iter3.jpg), native capture `build/sheet1-iter3`, seed 804584441):

- Plumestep: dedicated recipe — narrow basal tussock plus one central culm; nine seed tiers each haloed by large feathery fronds graded blue (base) → pink (tip).
- Bannerreed: culm leaves moved to the lower ~40% of each culm.
- Coalspike: 24 straps from distributed rhizome crowns (patch .42), inner upright / outer arching.
- Banksorrow: three twigs per lateral, 24 larger leaves per withe.
- Witchberry: six twigs per limb, four fan-spray nodes per twig, larger fan leaves.
- Hollowcrown: rebuilt after the legacy proportions — taller flared bole (.56h), six irregular buttress roots descending from the flare then running along soil, six elbowed scaffold limbs from the bolling head, each forking into outer spreading, ascending inner and low skirt boughs with crooked leafy branchlets. Opening faces +Z (catalog camera). `PlantCompiler.WindowAngle` widened (peak 1.7 sectors, was 1.0) so the hollow reads as an opening; Hollowcrown is the only window user.

Validation: solution build (existing nullable warning only), 28/28 plant self-tests, headless boot `VIVARIUM_BOOT_OK roundtrip=True invariants=0`. No owner acceptance yet. Residual: Hollowcrown limb/bole junctions are overlapping surfaces, not a mesh union; Witchberry's large round burls unchanged.

## Post-review round — sheet 2 (2026-11-17)

Owner: **sheet 2 acceptable as is** (Antlerlace lichen, Ashfingers lichen, Blackneedle, Bogfringe, Bogglass moss, Burlmantle). Preserve.

## Post-review round — sheets 3–14 (2026-11-17)

Accepted as is: **sheets 3, 4, 14**, and every species on sheets 5–13 not listed below. Preserve.

| Sheet | Species | Owner direction |
| --- | --- | --- |
| 5 | Fenfoam | Too rigidly straight; add bends and asymmetries like real plants. |
| 5 | Fenforge | High root "knees" like cypress or mangrove. |
| 6 | Ghostbonnet | Twisty, wavy stems; flatter, less domed caps. |
| 7 | Heddlelace, Hallowsting | Organic bends and curves. |
| 8 | Mirespire | More convincing needle canopy: longer, thinner, less sharply tapered needles, each with a fringe of smaller sub-needles on both sides. |
| 8 | Moonbark | Upper branches aligned and smoothed where they join the trunks. |
| 9 | Quillsedge | Thin whorling ribbon-like leaves, not broad flat discs on edge. |
| 9 | Raincup Pitcher (`pitcher_plant`) | Pitchers bell out at the bottom; much more detailed central blossom; thicker, more detailed stem. |
| 10 | Ringreed | Rod-straight horsetail, no fringes; ridged/striated stem; tops smoothed and rounded. |
| 11 | Starbulb | Central stem slightly curved. |
| 11 | Sootband | Smoothed out, more organic. |
| 11 | Splitcrown | Broader leaves for a fuller canopy. |
| 12 | Swanfrond | Single frond longer and arching higher out of the shrub. |
| 12 | Streamribbon | Long, relatively narrow, spiraling and twining blades. |
| 12 | Thornmantle | Canopy of spreading wide compound leaves at stem ends; rest is good. |
| 13 | Veilblade | Seed heads with more seeds and filaments; more substantial stems. |
| 13 | Tidebell | Thicker stems; bells more curved and organic. |

Implemented 2026-11-17 by four file-scoped workers (woody / upright / aquatic+pitcher / low+fungus) plus integration fixes. Native renders (`build/post-review2-final`, seed 804584441): [round2-a](../post-review-2026-11-17/round2-a.jpg), [round2-b](../post-review-2026-11-17/round2-b.jpg), [round2-c](../post-review-2026-11-17/round2-c.jpg).

Integration notes:
- **Lamina twist bug:** unit-shape Y scales with organ *length*, so `Twist` displaced a ribbon's width by length; Twist≈5 turned Quillsedge/Streamribbon into huge slabs. Added opt-in `OrganShape.TwistDepth` (default 1 = legacy) and set .02/.04 on those two ribbons. Other shapes unchanged.
- Thornmantle tip compound leaves enlarged (rachis .22–.32, 4 pairs, 3 per limb); Veilblade stalks r .024→.006 and 4 seeds + filaments per tier; Fenforge roots lengthened, knees taller/further out, lowest branches lifted to expose them; Raincup vessels re-profiled to a belled base/slim neck (hood moved to the narrower rim) and bloom enlarged ~1.6×.
- Ringreed is a straight bundle with rounded black caps; the lower shaft cluster reads somewhat block-like at catalog distance — judge in game. Streamribbon twists but does not yet truly twine around neighbours (flats cannot follow a helical path).
- Cost risks: Raincup ~2× organs, Mirespire needles are now tube axes, Moonbark +~96 collar organs. No full-world perf run.

Validation: solution build (existing nullable warning only), 28/28 plant self-tests, headless boot `VIVARIUM_BOOT_OK roundtrip=True invariants=0`. No owner acceptance yet.

## Round 3 — Quillsedge, Ringreed, Sootband (2026-11-17)

Owner: Quillsedge needs a distinctive, odd but lifelike central feature instead of being only a grass tuft. Ringreed and Sootband need serious work; both should be horsetail-inspired variants.

Implemented ([round3.jpg](../post-review-2026-11-17/round3.jpg), native capture `build/round3-iter2`):
- Quillsedge: ribbon tuft opened at the centre around a glossy oxblood bulb; 7–10 stiff cream/black banded quills rise from it, each tipped with a golden bristle plume.
- Ringreed (scouring-rush type): 11 rod-straight unbranched stems from distributed rhizome crowns; 7–10 ribbed internodes each (compiler striation), ash sheath + black ring at every node with fine dark teeth on alternate nodes; alternate rods end in a rounded spore cone or a smooth dark tip.
- Sootband (bottlebrush type): 7 gently curving sterile stems with sooty black node sheaths and whorls of 6–9 two-segment jointed branchlets shortening upward; 3 short pale fertile stems with loose dark sheaths and tan cones.
- Sheet15 self-test updated to assert the new Ringreed anatomy.

Validation: build, 28/28 plant self-tests, headless boot OK. Cost note: Sootband is now ~1,000 thin axes per plant — check density in game.

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

## Material and lighting pass (2026-11-17)

Owner: plants and ground are too bright (albedo) and too often glossy; only berries, waxy leaves and the like should shine. Guide: lifelike, realistic, whimsical, believable, natural, organic, textured; each species with its own slightly different material. Measured/estimated targets: [material-reference-2026-11-17.md](../material-reference-2026-11-17.md).

Root causes found: SPECULAR 0.35–0.58 on leaves, bark, soil and litter (sky reflections on everything); a near-mirror wax lobe on every leaf; albedo gains up to ×1.65 with 0.98 clamps; damp soil clear-coated from dampness 0.3; DayExposure 1.18 + upward auto-exposure + SDFGI 1.15; saturated chartreuse terrain cover; and **OrganShape.Roughness never reached the GPU**.

Changes:
- Plant shaders (leaf, leaf_direct_light, flora): leaf spec .25–.32 (waxy ≤.45), underside .18, bark .12–.22, moss/lichen .10, flowers .28, fruit .50; wax lobe gated to waxy species; specular no longer double-applied; foliage roughness floor .50 (waxy tops .26); albedo gains ≤1.0, foliage clamp .55, bark .45; gloss rough only on real fruit.
- Shape roughness plumbed via COLOR.a (`a = .55 + .44·r`, 1 = unset) for lamina/bell/ellipsoid meshes; shaders shift roughness by (r−.55)·.6 and r<.35 enables waxy/glossy spec. Not yet in BuildRadialSurface or far-LOD aggregate.
- Per-species Roughness on ~80 catalog shape sites (felted .76–.86, ordinary .52–.72, satin .50–.58, waxy .34–.40, petals .42–.70, berries .18–.30, fungi .34–.76, moss .85–.95, lichen .90–.97).
- Ground: terrain albedo ×.8, clamp .30 dry/.18 wet, spec .18–.30, clear-coat only when genuinely wet; moss and ambient cover tints moved from lime to olive; substrate/colony/coverage/rock/log spec .10–.20.
- Lighting: DayExposure 1.18→1.00, AutoExposureScale .22→.15 (sensitivity 90–105), SDFGI energy 1.15→1.0. Catalog review floor darkened to sRGB (.36,.33,.29).

Evidence: [materials-a](../post-review-2026-11-17/materials-a.jpg), [materials-b](../post-review-2026-11-17/materials-b.jpg) (catalog before/after; "before" frames predate round-2 shape changes for some species), [materials-scene](../post-review-2026-11-17/materials-scene.jpg) (game scene, baseline left). Game-scene mean luminance down ~10–25%. Validation: build, 28/28 plant self-tests, headless boot OK, windowed renders with no shader errors. Not done: per-species colour audit (some legacy species colours such as saturated cyan fronds remain), perf re-measure.

## Colour softening and Starmoss glow (2026-11-17)

Owner: tone down oversaturated species colours; Starmoss should glow only on its vertical structures, not the ground mat.

- Saturation: `experiments/plant_catalog_test/tone_down_saturation.py` applies a soft knee in HSV (saturation ≤ .60 unchanged; excess above it halved; hue and value kept) to `game/content/flora/*.json` visual colours and catalog/generator `Srgb()` literals. 101 colours changed (38 content files + catalog). Bioluminescence colours untouched. Rerunnable with different `KNEE, SLOPE` for a stronger pass.
- Glow mask: plant-compiled `*_colony_understory` mat vertices now carry UV2.x = −1; new `emission_mask_mode` 10 (flora.gdshader, leaf.gdshader) lights everything except that mat. Starmoss and the default for glowing mosses/lichens use mode 10. The game's separate coverage sheet (coverage_mat) never emitted.
- Review: `--plant-catalog-night` adds a deep-night (day fraction .80) shot per catalog species.

Evidence: [starmoss-night.jpg](../post-review-2026-11-17/starmoss-night.jpg), [colour-pass.jpg](../post-review-2026-11-17/colour-pass.jpg). Validation: build, 28/28 plant self-tests, headless boot OK, windowed catalog/night renders.
