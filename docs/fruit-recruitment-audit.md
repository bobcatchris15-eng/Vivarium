# Fruit lifecycle and shrub recruitment audit

## Finding

Fruit production and release are implemented. The reproduced defect was that viable shrub seed could be deposited but fail to recruit. Seed-bank attempts retried only the lot's one cell center, and the gravity/default dispersal kernel placed seeds close to parents, where woody spacing rejects establishment. Small viable lots were also discarded below 0.001 before later releases could accumulate. The prior release arithmetic additionally omitted nonviable-kernel mass from material accounting.

A regression using actual Embercrown, Lanternbrush, and Shadebell definitions reproduced the issue before repair: all dropped fruit and deposited viable seed but none recruited within 240 simulated days. Lanternbrush's old site was about 0.125 m from the parent against 0.55 m minSpacing. Separately, Shadebell's authored 0.55 m dispersal radius was less than its 0.62 m minSpacing, making same-layer recruitment around an isolated parent geometrically impossible. Shadebell's radius is now explicitly 0.85 m, above its spacing and consistent with its 0.78 m mature crown; this content correction is intentional and documented.

## End-to-end trace

- `ReproductionSystem.Step` accumulates a healthy mature plant's reserve, advances Dormant → Developing → Ripe → Spent, and releases load during the ripe interval (`src/Vivarium.Sim/Flora/Reproduction.cs`).
- `Release` deterministically selects a landing cell within the configured woody dispersal radius and outside the parent's minimum-spacing disc. Gravity/animal-labeled woody releases use a stable per-individual crown-edge cluster so small releases aggregate into a seed lot over time. Fruit litter and viable seed are deposited at the same landing point. `LitterSystem` records FruitMass and increments `FruitDropped` (`src/Vivarium.Sim/Ecology/LitterSystem.cs`). Nonviable seed fraction remains in the fallen-fruit pool, so viable seed plus fruit equals released load.
- Flora's normal generic spread branch is gated by `sp.Reproduction == null` (`src/Vivarium.Sim/Flora/FloraSystem.cs:395`). Reproduction-enabled shrubs depend on seed-bank recruitment; this policy is unchanged.
- `SeedBank.Step` decays lots, observes dormancy and retry intervals, and calls `CanEstablish` at the lot's fixed deposited cell. Attempts never relocate seed. A lot must contain at least `InitialBiomass` before germination can consume that mass and establish a recruit; undersized viable lots remain available to aggregate. Habitat and spacing checks still apply (`src/Vivarium.Sim/Flora/Reproduction.cs`).
- The scheduler runs flora, reproduction, then seed bank at orders 70, 72, and 73 on the same phase/cadence (`src/Vivarium.Sim/World/VivariumWorld.cs:171-174`). Cadence.Flora is 60 fixed 10-second ticks, or 600 simulated seconds per run.
- Serialization already includes per-plant reproductive fields in the `flora` payload, FruitMass in `litter`, and `SeedLot` state in `seed_bank` (`src/Vivarium.Sim/Persistence/WorldSerializer.cs:160,183,291,325`). No persistence schema change or migration is required; existing `ReproductionTests.FruitAndSeedBankRoundTripExactly` covers save digest round-trip.
- Attached reproductive state selects a fruiting plant mesh in `OrganismRenderers.cs:702-706`; `SoilDetailRenderer.cs:137,291-311,383` derives fallen fruit instances from authoritative FruitMass. Release updates aggregate state immediately; there is no separate falling-trajectory entity. The lifecycle plan permits ordinary fruit to aggregate into FruitMass and render as ground patches (`docs/ground_continuity_lifecycle_plan.md:582-592`).

## Bounded correction and evidence

`SeedBank` retains viable mass down to a small numerical floor (`1e-9`) so sub-0.001 releases are not discarded. Germination waits until a lot holds enough real mass to fund the configured initial recruit; the existing viability chance and suitability checks remain. Woody gravity/animal-labeled fruit lands at a stable, deterministic site in the crown-edge annulus, bounded by the authored parent-centered radius. The deposited cell is fixed across retries. Shadebell's content radius is explicitly raised from 0.55 to 0.85 m to resolve its former radius < minSpacing contradiction. Full release mass is conserved between viable seed and fallen fruit, including nonviable kernels.

`tests/Vivarium.Sim.Tests/FruitRecruitmentRegressionTests.cs` exercises actual woody configs for 900 simulated days. It verifies fruit detachment, viable deposits bounded by parent-centered radius, germination, recruits outside adult spacing and inside that same radius, residual ground fruit, trace-lot retention, no seed movement on failed retries, and mass conservation. Command: `dotnet test tests/Vivarium.Sim.Tests/Vivarium.Sim.Tests.csproj --no-restore --filter FullyQualifiedName~FruitRecruitmentRegressionTests --verbosity minimal` — passed 7/7.

The focused regression uses a suitable flat fixture and calls reproduction/seed-bank systems directly. It does not establish that shrubs survive every real habitat or that a full scheduled world has indefinite population stability. Habitat, competition, seed mass, viability, dormancy, and configured dispersal continue to constrain recruitment.

## Remaining plan gaps and risks

Embercrown and Shadebell content say `animal` dispersal, but no fauna-carried seed payload exists; these woody releases currently use a local crown-edge kernel. The ground continuity plan lists animal carriage as a later capability (`docs/ground_continuity_lifecycle_plan.md:633-641`). Water transport is also deferred. No claim of animal-mediated transport is made here.

The initial failure depended on woody spacing, dispersal radius, and the environment grid's 0.25 m cell-center quantization. The regression verifies actual lot and recruit locations remain within the parent-centered radius. Future content edits that set radius below minimum spacing should be rejected or reconciled explicitly, since no same-layer seed site can then meet both constraints.
