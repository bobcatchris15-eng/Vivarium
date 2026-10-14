# Bioluminescence correction requested October 10, 2026

All seven luminous species visible on screen must illuminate their surroundings at the same time.
Illumination must be gentle and local. The current bonfire intensity and rotating light allocation are bugs.

Luminous tissue: ghostbonnet (called moonbonnet by the user) gills only; lanternsedge seed heads only;
siltstars broad central stripe on each leaf; tidebell thin strip around each leaf edge.
Preserve the existing distinct glowbriar form. Check all seven species under the normal night cycle,
including multiple individuals together. Emission alone is insufficient: terrain and neighbours must
receive visible, softly coloured light. User has requested these corrections completed by morning.

Implemented and verified October 11. Stable per-organism lights replace the rotating nearest-32 pool;
all visible emitters receive the current night envelope simultaneously. Energy, range, overlap and tissue
emission have been reduced. Ghostbonnet retains complete bonnets at every detail tier and only downward
gill faces emit; Siltstars' floor-hugging leaves now use the correct physical height. Tidebell's glowing
strip closes around the tip/base as well as the long margins.

The production fixture passed with 63 mature plants (nine per species), all positive lights concurrently,
and unchanged light ownership after camera movement. Peak energies in the fixture are 0.0275–0.045.
See `artifacts/fluid-rebuild/night-final/report.json`, `night-cast-light.png`, `night-emission-only.png`
and `emission-mask-{ghostbonnet,lanternsedge,siltstars,tidebell}.png`. The last four isolate emission
from cast light and make each tissue mask visible. Full implementation notes: `surface-liquid-rebuild.md`.
