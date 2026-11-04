# Established stream and pool transport

User direction: simulate advancing water and filling terrain; established water only needs a convincing
flowing or still surface. Save compatibility is explicitly not a requirement.

## Implementation

- Fine cells remain 1.25 cm. Sparse 16x16 tiles provide the hot traversal order, direct neighbour links,
  reused face buffers, and generation-stamped work lists. Dictionaries remain for cold tool/export
  access, rather than per-face endpoint lookup and sorting.
- Independent surface and face calculations run in parallel above a 4096-item threshold. Volume
  transfers and budget reductions apply in a stable order. Serial/parallel state parity is tested.
  The pressure solve uses six red/black paired sweeps; cells of each colour can update concurrently.
- Advancing/filling water uses a fixed 60 Hz transport step. This is deliberately a transport and
  filling approximation, not a gravity-wave solver. It no longer subdivides the entire active field
  according to the deepest cell's gravity-wave speed. An implicit neighbour pressure solve stabilizes
  active levels before transfers. Face transport is limited to half a cell per
  step; a shared donor limiter prevents negative volume. Momentum cannot run uphill against the
  current hydraulic gradient and spill a pool before its lip fills.
- After a second of wetting, established cells can retain their supported surface and local current.
  Regions group connected established cells in 5 mm head bands. New discharge follows already-wet
  downhill regions to available active edges. A blocked/higher edge wakes its inlet to fill, rather
  than deleting input. Waking is local; one inlet no longer reactivates the whole connected stream.
- Regional routes rebuild in batches. Snapshot boundaries canonicalize their derived queues so a
  fresh save resumes with the same transport state. Geometry and source changes invalidate routes.
- Surface reconstruction can sleep within a 0.1 mm maximum head tolerance, further bounded to 5%
  of local depth. Sub-visible films retain their volume and resume movement when fed above the
  0.05 mm visible-front depth. No water is discarded for a work budget.
- Stored local velocity continues to drive surface optics on displayed frames. Still water retains
  a still field; established water is not assigned a universal flowing animation.
- Published cells carry their cached solid support, avoiding another terrain/rock/root query per cell
  on the main thread. Diagnostic contact samples are immutable views over published snapshots rather
  than a large particle array rebuilt on every publication. Both remain valid across geometry edits.
- The owner can consume up to 32 queued steps per job, and renderer interpolation can cover up to
  half a second during catch-up. Ordinary displayed-frame optical motion remains continuous.
- Spring input occupies the visible vent's approximately 3 cm radius aperture. A high-rate spring
  no longer puts its entire input into one fine cell and creates an artificial pressure tower.
- New placement preserves terrain. Existing player terrain and props still define solid support.
  Evaporation, infiltration, groundwater transfer, boundary outflow and source input remain budgeted.
- Automatic erosion defaults to disabled, restoring the requirement documented in
  `docs/performance-2026-10-24.md` and the existing erosion tests. Explicit diagnostic opt-in remains.

## Persistence policy

The save schema is 5. Older schemas are rejected before constructing a world; the schema-migration
pipeline was removed. No compatibility migration was added for this implementation. Current-schema
round trips, checksums and continued water accounting remain tested. The live player autosave was
not rewritten. Historical-save probes under `build/water-response/` are diagnostic fixture imports.

## Verification

- 84 focused hydrology, water-response, spring-control, erosion, terrain-tool, resolution, current-save
  persistence, and schema-rejection checks passed.
- Solution build: 0 warnings, 0 errors. Game build: 0 errors, 3 existing warnings.
- A 30 Hz transport experiment caused a small premature closed-pool overflow and was rejected.
  The retained 60 Hz step passes both the early-no-overflow and later-lowest-lip overflow tests.
- Native long-source review and widened-pool review: both passed. Module hashes and
  physical receipts are retained in their evidence directories. These are bounded water fixtures,
  not mature populated-world FPS or all-map acceptance.

## Native results

Long review: `build/water-response/native-published-support/`. Dry 17 m hexagonal fixture,
source at x=-6 m, 2000 L/hour, production tool placement, ordinary renderer, 1x physical time.
The downstream island edge is 14.5 m from the source.

| Wall checkpoint | Fluid time | Downstream wet extent | Queued time |
| --- | --- | --- | --- |
| 30 s | 30.017 s | 3.131 m | 0.0067 s |
| 60 s | 60.017 s | 5.419 m | 0.0090 s |
| 90 s | 89.983 s | 7.631 m | 0.0241 s |
| 120 s | 120.033 s | 9.869 m | 0.0072 s |
| 180 s | 179.867 s | 14.281 m | 0.0459 s |

The subsequent real HUD control check edited the source to 50 and then 500 L/hour without replacing
it, and removed it successfully. At its final checkpoint, fluid time was 186.100 s, wet extent
14.494 m, and queued time 0.00234 s. Rate-edit/capture transient queue peaked at 0.346 s in the
recorded checkpoints, then recovered. Maximum recorded conservation residual was
2.66e-15 cubic metres. No shader/runtime errors were logged; existing reflection-probe and
shutdown texture warnings remain. The live player save was not loaded or modified by these runs.

Widened-pool review: `build/water-response/native-pool-published-support/`. Production rendering,
filling/spilling checks through 45 physical seconds, standing-table geometry invariance, current
save/load digest equality, and water-budget checks passed. Volume-weighted current declined from
0.0494 m/s at 2 s to 0.00555 m/s at 25 s and 0.00719 m/s at 45 s, including the active spill.
The broad pool reads as a calm transparent surface in the reviewed macro and oblique captures.
These static captures are not a separate video/motion acceptance claim.

Earlier native directories in `build/water-response/` retain rejected attempts: explicit transport
produced unstable wet extents, and globally CFL-subdivided versions still accumulated queue. They
are not acceptance evidence for the retained implementation.
