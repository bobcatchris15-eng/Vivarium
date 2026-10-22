# Surface-bound vine growth

## The problem with the old climbers

Fenhook, Clinglace and Spiralvine each had their own growth rule hard-coded into
`FloraSystem.ClimberStep`, and each rule told the plant *where to put the next node*:

| Species | Old rule |
|---|---|
| Spiralvine | wind in a helix of fixed pitch (0.82 rad) around a cylinder of assumed radius |
| Fenhook | climb with random wander; hang swags downward past the trunk top |
| Clinglace | ascend the trunk, splaying laterals around the circumference |

Three unrelated models, each of which only coincidentally agreed with a tree trunk.
The stem was never required to touch anything: the node was *placed near* the host, and
on logs and rocks the generic fallback stepped straight up in Y at fixed XZ, so the
stem slid through the log.

That is the wrong way round. A vine does not know how wide the trunk is. It lays stem
down on whatever it is touching and grows, and the trunk's shape decides where the stem
ends up. Hedera hugs bark because bark is there; a twiner coils because the trunk is
round; a scrambler sprawls because it is not committed to going up.

## The law

A shoot carries two vectors inside the tangent plane of the surface under its tip:

* its **heading**, and
* a **lateral reference** that is *parallel-transported* along the surface as the shoot
  advances (the rotation-minimising frame of the surface).

Each internode:

1. **Travel direction.** The shoot wants to travel along its host's axis (a trunk: up; a
   log: along it). On open ground the travel direction is the shoot's own course, which
   it steers toward the nearest host it can sense.
2. **Heading.** `heading = travel·climbWeight + lateral·driftWeight`, rotated inside the
   surface by a circumnutation angle `wanderAmp·sin(2π·arc/wanderPeriod)`.
   The light *tilts* the travel direction; it never competes with it.
3. **March and lay down.** `raw = P + heading·segmentLength`, then project `raw` onto the
   surface. The stem lies on the surface, so the direction it actually travels is the
   tangent of that step, not the heading that was aimed with. **The surface has the last
   word.**
4. **Transport.** The lateral reference is parallel-transported from the old surface
   normal to the new one, and the shoot's frame continues from there.

### Why parallel transport is the whole trick

Parallel transport is the identity across a flat plane, so a shoot on a flat wall or on
open ground keeps the heading it had and runs straight (or holds a consistent course).
Around a round host the transported frame rotates as the shoot travels, so the same
heading produces a helix.

One law, one parameter, and the same vine winds around a round trunk while climbing a
flat wall in a straight line, **because the wall gives it nothing to wind around**.

The drift term is deliberately left un-normalised: its length is how perpendicular the
lateral reference happens to be to the direction of travel. A shoot whose reference runs
along its travel direction — open ground, where the surface offers no sideways direction
at all — gets no sideways push. A shoot on a round host gets the full one. That is the
mechanism by which the surface, not the species, decides the behaviour.

### Branching

A lateral shoot leaves the parent at the branch angle: the parent's *frame* (both its
travel direction and its lateral reference) is turned by that angle about the surface
normal. The lateral therefore leaves the stem at that angle on any surface — around a
trunk it splays circumferentially, on a wall it spreads horizontally, on the ground it
diverges from the parent's course.

Each shoot carries its own growth credit, so the leader advances steadily while lateral
shoots fill in behind it at their own rate. That is apical dominance expressed as a
speed rather than as a rule. A lateral shoot stops at the species' branch length, so side
shoots cannot eat the plant's node budget.

### Support binding

A shoot is bound to one surface at a time. It binds to a host when the host's surface
comes within `grabRadius` of where the shoot is actually growing — the vine reaches out
and takes hold; it never teleports. Sensing only biases the course: the seek turn rate
is limited, and it is limited in the way a pursuer needs to be (roughly one radian per
metre of separation), or the shoot would orbit the host instead of landing on it.

When a support ends — the shoot tops a trunk, runs off a log — it steps back to the
ground if it can still reach it, otherwise it runs free for a while: a vine that tops a
trunk reaches out into the light before it gives up.

### Spreading, and not taking over the place

A creeping vine spreads by walking, not by seeding into the air. A runner that has covered
`stolonLength` over the ground roots where it lands and starts a plant of its own, continuing
the line it was already on, so the network reads as one creeping stem rather than a scatter
of unrelated shoots. A climber only seeds once it has actually found something to climb — a
shoot that is still searching is not established, and a species made of searching runners is
a lawn, not a population.

Two budgets keep a species a population rather than a plague: `populationCap` (living plants
of the species) and the stem budget (nodes of the species across the whole world, four
plants' worth). A branch also has a length limit, and a branch off a branch is shorter still,
so side shoots cannot eat the budget the leader needs to climb.

## Content

The `climber` block gains the weights the law needs:

| Field | Meaning |
|---|---|
| `climbWeight` | weight of the travel direction |
| `driftWeight` | weight of the transported lateral reference; negative winds the other way |
| `lightWeight` | how far the light tilts the travel direction |
| `wanderAmp` | circumnutation amplitude, in radians |
| `wanderPeriod` | circumnutation wavelength along the stem, in metres |
| `branchRate` | chance per internode of throwing a lateral shoot |
| `branchAngle` | angle between a lateral shoot and the parent stem, in degrees |
| `lateralSpeed` | lateral shoot growth rate relative to the leader |
| `lateralClimb` | lateral shoot travel direction relative to the leader; negative makes it hang |
| `maxBranchOrder` | maximum branch generation |
| `branchLength` | maximum length of one lateral shoot, in metres |
| `baseRadius` | stem radius at the base of the plant, in metres; also the cap, since no node is ever thicker |
| `stemTaper` | per-metre falloff of the stem radius from base to tip |
| `tipRadius` | the thinnest the stem may get, in metres |
| `grabRadius` | how close a shoot must come to a host before it grabs it, in metres |
| `seekRate` | how eagerly the shoot turns toward the nearest host while searching |
| `stolonLength` | how far a runner walks over the ground before it roots and starts a plant |
| `populationCap` | living plants of this species the world may carry |

`branchChance` is gone: branching is now a rate per internode rather than a coin flip
when a parent buds.

## The species

| | Clinglace | Spiralvine | Fenhook |
|---|---|---|---|
| growth habit | root climber | twiner | scrambler |
| `climbWeight` | 1.0 | 1.0 | 0.9 |
| `driftWeight` | 0.55 | 1.35 | 0.55 |
| `lightWeight` | 0.25 | 0.7 | 0.5 |
| `wanderAmp` | 0.45 | 0.16 | 0.3 |
| `branchRate` | 0.13 | 0.06 | 0.14 |
| `branchAngle` | 68° | 38° | 82° |
| `lateralClimb` | 0.25 | 0.55 | 0.25 |
| `branchLength` | 0.6 m | 0.75 m | 0.55 m |
| `baseRadius` (cap) | 11 mm | 20 mm | 16 mm |

Clinglace hugs and splays; Spiralvine winds; Fenhook sprawls and climbs hard. None of that is
special-cased any more: it is what the numbers above produce on whatever surface the vine finds.

Measured on a 2.4 m shrub, stem laid down on the trunk:

| | turns around the trunk | turns per metre of climb |
|---|---|---|
| Clinglace | 1.8 | 1.2 |
| Spiralvine | 3.6 | 2.3 |
| Fenhook | 0.3 | 0.2 |

The ordering falls out of the law rather than being written down: a twining species has a
large drift weight and a slow circumnutation, so its heading keeps leaning the same way
round the host; a scrambler has a fast, short circumnutation, which swings the heading back
and forth and cancels most of the winding.

## What the vines cost

A species is a population, not a plague. `populationCap` (living plants), the stem budget
(nodes of the species across the world) and the branch length keep it that way: a branch off a
branch is shorter still, so side shoots cannot eat the budget the leader needs to climb. A
climber only seeds once it has found something to climb, and a runner spreads by rooting where
it walks rather than by seeding into the air.

Measured over thirty days of a populated habitat: 10, 8 and 10 plants of the three species,
holding at 800, 630 and 800 nodes — flat from the second week on, with the height they reach
still climbing. The stem is drawn as one tapering tube (four rings, six sides, overlapping at
the joints) and the foliage as a leaf cluster every 18 cm, which works out at roughly 3000
triangles a metre of stem at the nearest detail tier and 590 at the farthest.

## The stem's thickness

The radius at a node is `basal·stemTaper^(depth·segmentLength)`, where `basal` is the plant's
current basal radius and `depth` is the node's distance along the plant from the founder.

Both ends of that range are bounded, and both bounds are the species':

* the **cap** is `baseRadius`: the plant approaches it as it matures and never passes it;
* the **floor** is `tipRadius`: without it a long shoot's taper would drive the tip to nothing.

The radius is refreshed for *every* node whenever the plant grows, not only for the nodes laid
down at that moment. A vine adds secondary wood over its life, so the base of an old vine is its
thickest part; baking each node's radius in when it was laid down would leave the base as thin as
the seedling that made it and the cap would never be reached.

Because the radius is a pure function of the plant's size and the node's depth, the ratio
between adjacent nodes is exactly `stemTaper^segmentLength`, which is what lets the stem be
drawn as one tapering tube with no steps at the joints.

## Drawing the vine

The stem is drawn as one tapering tube: each internode is an instance of a tube whose radius ratio across
its length is the species' stem taper, and consecutive internodes overlap slightly so their end caps stay
buried inside the stem. The instance's radius is the node's own radius, so the stem tapers from the base of
the plant to the growing tip and thickens as the plant ages.

**The foliage always sits on the upper side of the stem.** A node's local frame is `+Y` along the stem, `+Z`
up and `+X` across it, where "up" is world up projected perpendicular to the stem — not the host's surface
normal. A shoot that is spiralling round a trunk therefore still carries its leaves on top of it, and a stem
that twists does not turn its foliage with it: everything a node does is measured in that frame, which is
rebuilt from the node's own current state.

The growing tip of every shoot is a bare tendril rather than a leaf cluster: the node with no child is drawn
with the tendril mesh, so the end of each shoot is a thin, reaching curl. The tendril is short enough to stay
against the surface it is growing on, which is where a tendril belongs.

The side shoots that hang are not decoration on every node: they are part of the grown stem, which is why they
lie on the bark by construction rather than trailing off into the air.

## Looking at a grown vine

`--vine-review` grows a vine in a fixture world and renders it, which is how the size, the foliage and the
habit are judged against each other and against the host:

```
tools/godot/Godot_v4.7.1-stable_mono_win64/Godot_v4.7.1-stable_mono_win64_console.exe \
  --path game -- --vine-review spiralvine --output build/vine-review
```

`--tip` frames the growing tip instead of the whole plant, `--habitat` grows the vines in a whole populated
habitat and frames the best established one, `--days` sets how long it grows, `--angle` and `--zoom` move the
camera. The tool prints the growth it produced (node count, attached nodes, branch count, base radius against the
species cap, the height reached and how many plants the species carries) alongside the capture.

## Where the code lives

* `src/Vivarium.Sim/Flora/ClimberSurface.cs` — the surfaces (ground, woody stem, log,
  rock), the growth law, the stem's thickness law, and the stem frame the foliage is built in.
* `src/Vivarium.Sim/Flora/FloraSystem.cs` — `ClimberStep`, `GrowClimber`,
  `ExtendClimber`: the simulation loop that drives the law.
* `src/Vivarium.Sim/Flora/Flora.cs` — `ClimberSegment` is now also the state of the
  shoot that ends there (heading, lateral reference, travel direction, support binding,
  credit, radius).
* `src/Vivarium.Sim/Geometry/OrganismMeshes.cs` — `ClimberNode` (the foliage cluster on a
  node) and `ClimberNodeTip` (the thin tendril at the growing tip).
* `game/App/VineReview.cs` — the review harness.
* `tests/Vivarium.Sim.Tests/ClimberGrowthTests.cs` — the law and the simulation.
* `tests/Vivarium.Sim.Tests/FormTests.cs` — the node mesh's foliage and the tendril.
