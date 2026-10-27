# Isolated flora flicker survey — 2026-10-24

The user requested every flora species rendered alone, including sundew. This survey covers all62 Content.Flora entries:54 individual-specimen species and8 production coverage species. Fauna and pilot-tree catalogue forms are outside this flora survey.

## Method

Each species is the only biological renderer in its fixture. Noncoverage flora use current game-tier geometry and material-profile synthesis with the full species definition. Coverage species use the actual CoverageRenderer in a disposable flat, populated:false world, with only the requested occupant seeded. A neutral support plane and fixed daylight rig replace the habitat context. The coverage build verifies a stable simulation digest.

Each species has two framing scales (whole andmacro). Each scale has6 fixed-camera baseline frames plus3 MSAA-off controls, giving18PNG per species and1116PNG per complete batch. Baseline quality2 uses MSAA4X, TAA andFXAA; the control disables only MSAA. Exposure is manual, DOF disabled, wind zero, and available material clocks fixed atzero. Camera poses and shader path metadata are recorded per specimen. No biological growth steps are advanced.

An automated temporal screen ranks small connected regions whose channel difference from the within-mode temporal median exceeds64 display levels. Components must occupy at most64pixels and fit within16×16pixels. This is a triage metric, not a count of confirmed defects. TAA settling, silhouette sampling, legitimate specular/occlusion changes and mixed edge coverage can trigger it. Six baseline versus three control frames does not support a statistical effect-size estimate.

## Evidence selection

Final batch: `build/photorealism/isolated-flora-flicker-02/`. Its final report and analysis provide the current results.

The first batch01 also completed62 species/1116PNG without engine errors. During review, the older standalone harness was found to omit the species-definition argument when synthesizing material profiles. The harness was corrected to `FloraVisualProfile.Load(sp.Id, sp:sp)` and all62 species were captured again as02. Batch01 is retained as preliminary evidence;02 is the authoritative production-profile fixture pass. Coverage fixtures were unchanged by that correction.

The user explicitly authorized stopping the editor play session. Only play process32 was stopped; editors15536 and21724 were preserved. No production shader or anti-aliasing policy was changed by this survey. Capture evidence is local and uncommitted.

## Final results

Native02 passed:62/62species,1116PNG,exit0,source stable,zeroengine errors. Each of62 specimen manifests accounts for18PNG. The interactive gallery is `build/photorealism/isolated-flora-flicker-02/index.html`; it includes species selection, whole/macro switching, native-pixel inspection and fixed-camera sequence playback beside MSAA-off controls.

**Sundew is the clear reproducible outlier in this survey.** The automated screen flagged416small changing regions across its two baseline views (273whole,143macro); the next-highest species was marrowbloom with11, then velvetweave_moss8,pitcher_plant7,lanternsedge5. Forty-seven species scoredzero under this particular threshold. These are screening counts, not defect counts.

Sundew baseline whole/macro views have1884/904pixels exceeding the64-level temporal threshold. The MSAA-off controls have5/7such pixels and4/7smallcomponents. The unequal frame counts and different sampling modes limit quantitative causal interpretation; no percentage reduction or engine-defect attribution is claimed.

Pixel traces confirm an intermittent color excursion at fixed coordinates. For example, whole-view (578,218) ranges from roughly gray/green to(165,26,0), and its blue channel also drops to5 and33 in other baseline frames. Whole-view (372,303) reaches bluezero in some frames. Raw traces for all baseline/control repeats are saved in `review-findings.json`. Those changes distinguish the instability from stable authored gland pigment.

A Luna reviewer independently examined all8 final coverage species and selected pixel traces. Other coverage tips/colored organs are visibly attached structures without a comparable obvious transient-fleck pattern. Human review of the largest non-sundew preliminary candidates found sparse neutral brightness/edge/occlusion changes rather than the sundew pattern. The noncoverage preliminary material synthesis differed from production in shape-dependent fields; final02 fixes that mismatch and supplies the authoritative full-catalogue temporal screen.

This survey establishes a strong sundew association in the tested fixtures and an MSAA-mode association. It does not prove that no other species can flicker, identify the exact shader/raster stage, or constitute an exhaustive test of all morph variants, developmental stages, view angles, distances, habitat lights or gameplay motion. One representative fixture per species, two scales and short repeat sequences were tested. No production shader or AA setting was changed as a result.
