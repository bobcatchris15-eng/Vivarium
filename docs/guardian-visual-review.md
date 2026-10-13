# Guardian animal overhaul

The six guardian assets were authored Y-up inside Blender's Z-up scene. Export
converted their vertical dimension onto glTF's depth axis. The renderer now
corrects the imported asset basis, maps the authored forward direction to the
simulation heading, and builds a right-handed terrain basis. It applies catalog
and individual scale, lifts the neutral mesh to its support plane, and samples
animated feet against terrain, root shelves, and prop surfaces.

The home anchor is outside the tree collision radius. Behavior reconciles older
guardians embedded in the trunk, uses tangential motion around blocked trunks,
and makes exploration more frequent with short investigative pauses. Catalog
speeds and activity durations now run in observable seconds at the default game
rate, rather than consuming a 40-simulated-second behavior update as 40 seconds
of walking. Presentation interpolates between behavior samples with sub-tick
precision. Feeding occurs once per update and consumption scales with elapsed
time. Return-home has a walk animation, serpent locomotion uses its slither clip,
all continuous clips loop, and pausing stops skeletal playback.

All six GLBs have been regenerated. Each has skinned iris/pupil geometry. The
coati and monitor have subdivided body surfaces; the monitor has olive/gold skin
markings. The renderer explicitly enables vertex coat colors. Every authored
animation initializes its pose channels so sleep offsets cannot remain on
unkeyed bones after a transition to walking. Armor, crest, and quill geometry
retain their distinct shapes. Broken local-axis sleep curls were replaced by
restrained, supported breathing poses; species-specific sleep curls remain
future rig work.

## Reproduce

From the repository root in PowerShell:

```powershell
foreach ($name in @('coati','hoatzin','pangolin','serpent','monitor','porcupine')) {
    & 'C:/Program Files/Blender Foundation/Blender 5.2/blender.exe' --background --python "scripts/guardians/generate_$name.py"
}
& 'C:/Program Files/dotnet/dotnet.exe' build game/Vivarium.csproj --nologo
. ./scripts/common.ps1
& (Get-GodotExe) --headless --path game --editor --import
& (Get-GodotExe) --path game -- --guardian-review E:/Vivarium/build/guardian-review/verified
& 'C:/Program Files/dotnet/dotnet.exe' test tests/Vivarium.Sim.Tests/Vivarium.Sim.Tests.csproj --filter FullyQualifiedName~GuardianSimulationTests -p:BaseOutputPath=E:/Vivarium/build/guardian-review/test-bin/
```

The review uses the production GuardianRenderer, IslandRenderer, and
PilotTreeRenderer. It writes four close-up pose captures and fifteen live frames
per archetype, a per-archetype motion receipt, and a sixty-second behavior
distance receipt. Live frames advance the full world scheduler at 30Hz input.
The controlled scene contains terrain and the tree; it does not render the full
flora/fauna population and does not establish a performance gate.

Original asset and script copies are retained under
`build/guardian-review/original-assets` and `original-scripts`. Earlier captures
remain under `before`, `fixed`, and `polished`. The final evidence is under
`verified`; inspect its console/error logs and completion marker before treating
the run as complete.

## Remaining scope

The assets remain lightweight procedural animals. This pass fixes broken
placement and adds anatomical/material detail; it is not a hand-sculpted final
art pass. Arboreal catalog modes still share ground/root navigation: canopy
climbing, branch wrapping, and perching need dedicated routes and poses. Foot
support is sampled and capped rather than solved with skeletal inverse
kinematics. Ecological feeding remains the existing shared detritus/fruit model.

## Full art iteration (authorized October 11)

Near-photoreal acceptance is still open. Three isolated species-pair asset passes
are rebuilding anatomy, skins, ornaments, PBR maps and planted gait clips.
Task-local continuation lives in `.clanker/tasks/guardian-art-overhaul/plan.md`;
this document must not imply the provisional baseline meets that target.

The review now also records three clay silhouette views, matching surface views
and grazing illumination. Use `--guardian-species woodland_omnivore` (or any
archetype/catalog ID) to shorten one-animal iterations. For a fully populated
production GameSession including flora/fauna, append `--guardian-populated`;
its output is `populated-*.png` and `populated-motion.json`, with loaded flora
species counts in the receipt. All review launches disable autosave and use
transient settings.

Run `python scripts/guardians/audit_assets.py --output build/guardian-review/asset-audit.json`
for read-only GLB integrity, skin-weight, animation-channel, UV and PBR evidence.
A passing audit is not artistic or runtime acceptance.

Reference sources for anatomical and material review include the
[Smithsonian coati account](https://nationalzoo.si.edu/animals/white-nosed-coati),
[University of Michigan hoatzin account](https://animaldiversity.org/accounts/Opisthocomus_hoazin/),
[San Diego Zoo porcupine account](https://animals.sandiegozoo.org/animals/porcupine),
[Khronos glTF PBR contract](https://www.khronos.org/gltf/pbr), and
[Godot standard material documentation](https://docs.godotengine.org/en/stable/tutorials/3d/standard_material_3d.html).
References guide decisions; rendered evidence determines acceptance.

## Integrated art candidate v1 — October 12 overnight

All six isolated art candidates were integrated after the worker handoffs froze.
Old root assets/scripts are retained in build/guardian-review/pre-art-integration.
Exact current GLB hashes are .clanker/tasks/guardian-art-overhaul/integrated-v1-manifest.json.
The six imported assets pass the read-only structural audit, with degenerate
triangle warnings retained for some armor/feather/quill primitives. Game build
passes without warnings/errors; six guardian tests pass, including a bound on
per-step travel at the configured locomotion rate.

WalkSpeed now matches the authored stance and visual scale; RunSpeed is1.8x walk
until dedicated fast gaits exist. Animation playback also compensates for each
individual's random size variation. Terrain support remains approximate and
climbing/branch grasping are not implemented.

Actual Godot evidence: build/guardian-review/art-candidate-v1 and art-neutral-v1.
Both completed GUARDIAN_REVIEW_OK with empty stderr. The neutral studio now uses
explicit white ambient/key lighting, closer species-specific framing, and idle
for clay/surface views; live captures restore the production EnvironmentRig.
These diagnostic studio views do not substitute for populated-world review.
All six live transforms have positive determinant close to1; each has15 sampled
frames. The populated-v1 review is being generated separately.

Visual acceptance remains open. V1 has much more anatomy/detail but still reads
as procedural/stylized. Godot reveals hoatzin's projecting orbital discs and thick
wing ornaments; coati's stalk-like eyes, flat ears and smooth coat; pangolin's
stacked-oval limb silhouette; boa's glossy smooth pigment and square markings.
Targeted v2 work is assigned in the same isolated worktrees. Inspect newer worker
receipts/hashes before integrating again; do not recopy unchanged v1 assets.

## V2 production review

Four later candidates (hoatzin, pangolin, boa, coati) are integrated; monitor and
porcupine retain their first rebuilds. Exact hashes: task-local integrated-v2-manifest.json.
Godot neutral captures and per-animal motion: build/guardian-review/art-neutral-v2.
All-six populated GameSession review: build/guardian-review/art-populated-v2,
completed GUARDIAN_POPULATED_REVIEW_OK with empty stderr. Studio source and
runtime contact evidence remain distinct; this run does not prove exhaustive
terrain contact or frame-time acceptance. Godot imports have automatic LOD
creation enabled, but LOD appearance and performance are still unaccepted.

Coati's inset eyes, cupped ears and fine skinned fibers visibly improve the closeup;
the coat remains sparse. Hoatzin's thin vanes and inset eyes improve anatomy but
smooth underplumage and circular facial skin remain. Pangolin's connected two-link
IK replaces independent translated limb segments. Boa loses uniform glossy pigment
and square decals but needs a better cranial/muzzle profile and closed mouth.
Targeted isolated follow-ups are active for hoatzin, boa and monitor. The nearphoto
art target remains open; current version is not final accepted production art.

## October 12 morning continuation

The latest exact asset bytes are recorded in
`.clanker/tasks/guardian-art-overhaul/integrated-latest-manifest.json`.
Monitor v2, porcupine v2, hoatzin v3 facial hotfix, boa v3 lip-sheet fix,
coati v3 animation candidate and pangolin v2 are integrated. Prior candidates and
source receipts remain preserved in the isolated worktrees and review backups.

Actual Godot closeups verify three concrete corrections: the boa's overlapping
internal mouth sheets no longer produce green/cream interference, the bird's
broad facial speckling is removed, and the porcupine has a continuous dark muzzle
with concealed resting incisors. The bird retains minor rough facial boundaries,
coarse feather layering and a needle-like crest. Porcupine limbs and body still
read as smooth segmented forms. Monitor inset eyes, ear recesses, narrowed jaw
and scale maps are improved but remain stylized. Coati fur is sparse, its face
is stylized, and pangolin armor and limb forms still need artistic refinement.

Evidence directories under `build/guardian-review`: `bird-face-fix-runtime`,
`porcupine-v2-runtime`, `monitor-v2-runtime`, `boa-sheet-fix-runtime`,
`coati-blink-closeup`, `coati-blink-pose-diagnostic`, and `art-populated-latest`.
The all-six populated run completed with `GUARDIAN_POPULATED_REVIEW_OK` and
empty stderr. Vegetation can obscure guardians in this scene; it is supplementary
to the neutral closeups. The latest asset audit has zero structural failures;
degenerate ornament/UV warnings are retained. Six focused simulation tests pass.
Game build passes. These do not establish photorealism, frame timing, LOD quality,
or exhaustive contact on irregular terrain.

The coati's new investigative idle and two skinned lid bones are a partial
candidate. Native closure at 1.059 seconds leaves most of the black eye visible;
rest also leaves the eye open. The exact GLB's closed plateau spans
1.0416667–1.125 seconds, so sampling did not miss it. Runtime lid poses change,
and exported lid vertices are rigidly weighted to their own joints. Coverage or
source-to-runtime surface parity needs correction before blink acceptance.
Source renders alone overstated closure. Preserve stable coati v2 and this v3
experiment; do not label the new blink accepted.

Animation sweeps sample complete clips in actual Godot, including boa slither.
They intentionally hold root transport still and reset bone poses between
explicit samples, so they do not prove production clip transitions or terrain
foot placement. Separate live/populated runs exercise the production renderer.
The uncompressed boa import experiment was inconclusive; removing duplicate
mouth geometry, followed by native review, is the demonstrated fix.

The full-cycle boa capture reveals pronounced bulging/kinking along its body
during slither; smoothing deformation remains a concrete next correction. A
first-sample activity-routing race was found in the review harness and fixed by
settling renderer routing before direct sampling. Inspect actualClip/time fields
in the settled sweep receipt; earlier direct sweeps are diagnostic, not exhaustive
animation acceptance.
