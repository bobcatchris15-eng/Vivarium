# Version 1 scenario library

All cases use quality 2, seed 20260923 and existing `BuildReferenceScenes` stable queries with mature six-biological-day preparation by default; -Preparation initial is supplemental tick0 diagnostic and cannot satisfy mature/density acceptance. Each report captures full named reference poses plus a 12-second orbit frame sequence after the timing measurement. Light is fixed at presentation second 420 except `wet`, which traverses light phase during measurement (captured separately from fixed reference views). Shader TIME remains unpinned; byte-identical image claims are therefore withheld.

| ID | Timing camera and world behavior | Scene query / limitation |
|---|---|---|
| frozen | 60+ seconds analytic orbit, simulation paused | overview; render publication isolation |
| live | same orbit with ecology running | identical prepared initial world; final digest changes legitimately |
| understory | fixed grazing pose under dense vegetation | ground_vegetation; query fails if absent |
| colony | fixed macro coverage pose | dense_colony; query fails if absent |
| traversal | 60+ seconds fast analytic orbit | 1.5 rad/s versus normal 0.3 rad/s |
| sculpt | orbit, paused ecology, deterministic 10 Hz raise/lower schedule | fixed path in isolated world, expected terrain digest changes |
| wet | fixed water-margin pose, moving daylight phase | water_margin; missing shore is a failed scenario |
| density | unsupported and fails explicitly | no pinned worst-density fixture/count contract exists yet |

Macro, normal interaction, whole-island, sparse/dry, moist ground, shoreline, moss/lichen, flora species, fauna, fungi, rock/log contact and cutaway reference poses reuse the existing reference query library. Query result names, eye/target coordinates, digest, ticks and population counts are in the report. Missing necessary subjects are reported rather than introduced through ecological edits. Understory is a dense grazing scene, not proof that every selected plant forms an overhead canopy. Broadleaf backfaces and individual woody root collars still need explicit per-species review.

Normal orbit is `eye=(sin(0.3t)*9,3.5,cos(0.3t)*9)`, target origin; traversal uses 1.5 rad/s. Presentation time is elapsed monotonic seconds, not biology RNG. Sculpture uses a bounded 0.6 m brush on `(sin(t)*2,cos(0.7t)*2)` with ±0.01 m alternating updates at 10 Hz. It does not save the changed terrain.

Complete-frame elapsed time includes synchronous camera, sculpture, counter collection and runner work, rather than measuring only the signal wait. Named stills and motion capture readback occur afterward so capture cost is explicitly outside the timing scenario. All motion, diagnostic light and quality changes occur within a separate frozen simulation digest invariant. CPU scope maxima report available subsystem costs; gaps stay nullable or explicitly unsupported.

A growth/death/litter burst must be added as a separately validated deterministic fixture. A genuine high-density state must have pinned content, counts and digest before density can run. Those missing cases and the long resource soak block complete G0 acceptance.
