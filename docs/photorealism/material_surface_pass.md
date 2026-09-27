# Flora, soil and daylight material pass — 2026-09-27

This pass changes render-only surface response in the live game tree. The separate species-shape work remains active. No silhouette or simulation definitions were changed here.

## Material decisions

- `FloraSurfaceProfiles` binds a tissue palette for every vascular species, with separate upper and lower leaf reflectance and profile-specific venation, roughness, sheen and transmission. The mesh UV/UV2 contract is unchanged.
- Mature wood uses three CC0 ambientCG bark sets: Bark001 for deeply fissured dark trunks, Bark013 for paler thin bark, and the existing Bark014 for reddish/coarse bark. New source pages: [Bark001](https://ambientcg.com/view?id=Bark001), [Bark013](https://ambientcg.com/view?id=Bark013). Both were downloaded as 1K JPG colour, NormalGL and roughness sets on 2026-09-27. See `THIRD_PARTY_NOTICES.md`.
- Soil, damp ground, moss, litter and gravel share a continuous distorted world projection so their colour, normal and roughness remain registered. Broad patches use moisture and light; the scans keep their own detail. Nearby 3D litter is less golden.
- Daylight uses a more neutral sky, restrained saturation, softer shadowing, and weaker wide-radius AO. No additional per-pixel lighting feature was enabled.

## Review evidence

| Subject | Capture | Review |
|---|---|---|
| Ironlace bark, broadleaf and soil | `game/build/material-review/world-ironlace-lit/species_ironlace_close.png` | Dark fissured bark distinguishes it from pale Kiteleaf; leaf geometry remains visibly faceted. |
| Kiteleaf silver back | `game/build/material-review/kiteleaf-underside/kiteleaf-underside.png` | Underside and pale bark separate from Ironlace; underside reads darker from below under world daylight. |
| Fenneedle | `game/build/material-review/fenneedle/fenneedle-mature.png` | Needle profile has a less glossy response; its silhouette remains the main visual cue. |
| Veilfern and Glassfinger | `game/build/material-review/veilfern/veilfern-mature.png`, `game/build/material-review/glassfinger/glassfinger-mature.png` | Distinct matte thin and waxy fleshy profiles render without shader errors. |
| Bogglass moss | `game/build/material-review/bogglass/bogglass_moss-mature.png` | Shading works; single-specimen shoots still read as geometric at this scale. |
| Dewbonnet and soil | `game/build/material-review/world-dewbonnet-final/species_dewbonnet_close.png` | Cap darkening and ground material work; caps still appear pale and repeated as a group. |

Build: `dotnet build game/Vivarium.csproj -nologo` passed with no warnings. The six solitary captures and two integrated staged views exited cleanly. A 12-second GTX 1080 performance probe recorded 48.2 mean FPS, 26.72 ms median frame time and 439 MB VRAM; an earlier post-change probe recorded 47.7 FPS and 439 MB. These are short, scene-dependent samples, not a controlled pre-change comparison. The render harness wrote its captures but failed during `WorldSerializer.Digest` because current simulation state contains a non-finite number; this blocks its quality-tier assertion independently of material shader compilation.

Visual review remains provisional. The geometry agent's leaf edges, cap forms, and dense planting are needed for a genuinely photographic result. The preview captures here establish material behavior, not user approval of every species.

## Water follow-up

The Dewbonnet habitat view exposed a conspicuous blue/grey polygon mosaic in the pond. Controlled captures isolated three contributors: refraction crossing shallow terrain depth edges, an overly sharp sky highlight, and square cells from the value-noise wave pattern. The shader now guards refraction by depth, fades the shoreline over 5 cm, uses smooth crossing wave trains, and gives the pond a visible but restrained wetland tint and sky reflection. The transmitted bed is blurred and deliberately faint away from the margin. Water geometry and hydrology are unchanged.

Review captures: original `game/build/material-review/world-dewbonnet-final/species_dewbonnet_close.png`; initial overly dark pass `game/build/water-review/final-dewbonnet/species_dewbonnet_close.png`; revised visible surface `game/build/water-review/sine-fresnel/species_dewbonnet_close.png`. The revised staged capture passed `VIVARIUM_SPECIESWORLD_OK checks=2 failed=0` and exited cleanly. The remaining limitation is that this is still a procedural approximation of reflected sky and suspended sediment, so a dedicated close water review in motion remains useful.
