# Water layers and spring-fluid correction

The water-table layer remains a hydrostatic surface for ponds, creeks, inlets and shoreline water. Its elevation comes from the configured water table. Its geometry is clipped against the terrain and island, and ecological wetness queries include it. It is separate from the mobile volume injected by springs.

The current spring implementation still uses the earlier depth grid. The user rejected its floating slope plates and disconnected puddles. Earlier shallow-water conservation checks do not establish correct visible fluid behavior. A 3D particle solver has been started but is not integrated or visually accepted yet. Completion requires exact ground-point emission, gravity and solid contacts, a particle-derived water surface, persistence, conservation checks, and a visible mound/inlet review.

Terrain surface classification and shaders no longer generate exposed rock. Placed rock props retain their own material and rock substrate.
