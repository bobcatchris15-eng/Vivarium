# Material reference targets — 2026-11-17

Owner direction: plants and ground are too bright and too often glossy. Only berries, waxy leaves, genuinely wet surfaces and the like should be glossy. Aim: lifelike, realistic, whimsical, believable, natural, organic, textured; each species with its own slightly different material.

Values are linear albedo (visible, approx. RGB) and perceptual roughness (0–1). **M** = measured (physicallybased.info, read directly); **E** = estimate from standard published ranges (leaf optics literature, solar/renderer albedo lists). For hard numbers sample USGS splib07 or ECOSTRESS directly.

| Surface | Linear albedo RGB | Roughness | Notes | Basis |
|---|---|---|---|---|
| Grass | .105 .133 .041 | — | IOR 1.5 | M |
| Leaf upper, matte/hairy | .04 .10 .02 | .55–.80 | hairs: soft sheen; T≈5–10% green | E |
| Leaf upper, waxy (holly, succulent) | .03 .08 .02 | .20–.40 | gloss = sharp highlight, not brighter base | E |
| Leaf underside | .07 .14 .04 | .60–.85 | paler, less saturated | E |
| Young/pale leaf | .10 .20 .04 | .50–.70 | T 12–20% | E |
| Dry leaf litter | .20 .11 .04 | .70–.90 | | E |
| Wet leaf litter | .08 .04 .015 | .30–.50 | water film | E |
| Bark, smooth grey | .16 .15 .13 | .60–.80 | | E |
| Bark, rough brown | .09 .055 .03 | .80–1.0 | | E |
| Bark, dark furrowed | .04 .03 .022 | .85–1.0 | strong cavity AO | E |
| Green stem | .05 .12 .03 | .40–.60 | slight sheen | E |
| Dry grass/straw | .35 .25 .10 | .70–.90 | | E |
| Moss, dry / wet | .05 .08 .03 / .02 .05 .015 | .90–1.0 / .50–.70 | darker than vascular leaves | E |
| Lichen, grey foliose | .25 .27 .22 | .80–1.0 | | E |
| Lichen, yellow/orange crust | .45 .30 .04 | .80–1.0 | | E |
| Mushroom cap white / brown | .55 .50 .42 / .12 .06 .03 | .40–.70 | moist caps glossier | E |
| Bracket fungus | .15 .09 .04 | .70–.90 | | E |
| Berry red / black | .30 .01 .01 / .02 .01 .03 | .15–.35 (bloom .40–.50) | | E |
| Petal white / red / yellow | .70 .68 .62 / .40 .01 .01 / .60 .40 .02 | .50–.70 | T 15–40% | E |
| Soil dry loam / wet | .20 .13 .08 / .09 .055 .035 | .85–1.0 / .30–.60 | | E |
| Peat/humus | .04 .03 .02 | .90–1.0 | | E |
| Sand dry / wet | .44 .386 .231 / .20 .17 .10 | .80–1.0 / .30–.50 | | M / E |
| Clay | .256 .118 .091 | — | | M |
| Granite dry / wet | .20 .20 .19 / .10 .10 .095 | .60–.90 / .20–.50 | | E |
| Rotting log | .07 .05 .035 | .85–1.0 | | E |
| Water | — | 0–.05 | IOR 1.333, F0 .02 | M |

## Rules of thumb
1. Fresh green leaves are dark: ~.03–.05 red/blue, ~.10–.15 green linear.
2. Natural matte surfaces sit at ~.04–.30 linear; only snow (~.85), white petals and pale fungi (.5–.7) exceed that.
3. Wetting darkens porous materials 30–60% and drops roughness to ~.3–.5.
4. Keep F0 ≈ .04 (Godot SPECULAR .5) for almost everything; waxy leaves, berries and wet things look glossy through **low roughness**, not higher F0. Hairs, bloom and dry moss: roughness > .8.
5. Leaves are thin and translucent (T ≈ 5–15%); transmitted light is brighter and more yellow-green than reflected. Keep backlight modest.
6. Cavity/AO darkening in bark furrows, litter and moss matters more than base-colour shifts.

## Reference sources (verify before use)
- https://physicallybased.info/ (measured rows above)
- Jacquemoud & Ustin, leaf optics: https://sites.bu.edu/cliveg/files/2023/10/leaf-optics-jacquemoud-ustin.pdf
- USGS splib07: https://www.usgs.gov/labs/spectroscopy-lab/science/spectral-library — ECOSTRESS: https://speclib.jpl.nasa.gov/
- CC0 scans: https://polyhaven.com/textures (Nature)
- Photos: Wikimedia Commons categories Terrariums, Paludariums, Moss, Tree bark, Leaf litter

## Forb & Succulent Optical Split (2026-11-18)

All 61 herbaceous, forb, succulent, rosette, fern, runner, and graminoid species in `game/content/flora/*.json` now author per-species `visual.leaf` and `visual.form` blocks generated from PROSPECT parameters (`tools/prospect_forb_author.py`) across two unified shader paths in `leaf.gdshader` / `flora.gdshader` / `living_leaf_optics.gdshaderinc`:

| Optical Class | Lamina Thickness | Midrib / Edge | Cuticle Roughness & Specular | Transmission Model |
|---|---|---|---|---|
| **Path A: Thin herbaceous** (`class: "thin"`) | 0.14–0.28 mm | 0.40–1.18 mm / 0.07–0.13 mm | Front 0.34–0.65, Vein 0.52–0.83, Back 0.62–0.92; `F0 = 0.030–0.038`; optional epicuticular farina mask (`mooncoin`, `frosttussock`, `ashwire`) | High thin-slab Beer-Lambert transmission modulated by local optical thickness `tau(lamina, midrib, edge)` and species `transmission.color` + Lambert diffuse wrap (`0.55`) |
| **Path B: Thick succulent** (`class: "thick"`) | 2.2–5.5 mm | 3.2–8.0 mm / 0.8–2.2 mm | Waxy front 0.22–0.42 (`F0 = 0.036–0.044`, `cuticle_sheen = 0.42–0.80`), attenuated locally by `leaf_farina()` mask (`farina = 0.10–0.60`) | Volumetric water-parenchyma scattering (`leaf_through_thick`), low central core transmittance rising smoothly toward thinner tips/margins, turgor-driven thickness/curl (`WaterStore`) |

### PROSPECT & Cuticle Reference Ranges

| Parameter | Thin Herbaceous Healthy Default | Succulent / Thick Water Leaf | Glaucous / Farina-Dusted |
|---|---|---|---|
| `Cab` (chlorophyll a+b, ug/cm^2) | 30–55 | 14–38 | 22–28 |
| `Car` (carotenoids, ug/cm^2) | 6–12 | 6.5–12 | 6–7.5 |
| `N` (mesophyll structure index) | 1.35–1.65 | 1.8–2.3 | 1.75–1.85 |
| Cuticle `F0` (Bousquet visible white) | 0.030–0.038 | 0.040–0.044 | 0.030 (suppressed under farina mask to matte bloom) |

### CC0 Reference Mapping (Form & Optics Only — Identities Remain Fictional)

Vivarium species retain their unique fictional identities, niches, and morphology; real-plant studies supply optical slab ranges, packing ratios, and petal specular rules only:
- **Poly Haven `dandelion_01` / `celandine_01`**: golden-angle basal rosette packing (`137.5078 deg`, `r = c * sqrt(n)`), cupped petal sky-specular varnish (`butterglint`, `marshkettle`), thin-margin backlit glow.
- **Poly Haven `calathea_orbifolia_01` / `fern_02` / `nettle_plant`**: inter-vein transmittance vs. opaque vascular ribs, serrated/bullate laminar relief, felted leaf roughness (`burlmantle`, `hallowsting`).
- **Poly Haven `periwinkle_plant` / `grass_medium_01`**: ground-skirting mat/stolon contact (`hushclover`, `mooncoin`, `cinderberry`) and wind-flexible tuft blades (`plumegrass`, `frosttussock`, `quillsedge`).
- **Thick glossy & succulent references (`anthurium_botany_01`, Innerscene Zamioculcas/Ficus)**: low-roughness epicuticular wax over dark parenchyma contrasted with chalky farina bloom (`sunstone_rosette`, `cragset`, `glassfinger`).
