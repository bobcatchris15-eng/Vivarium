// Flora instances: baked vertex colours per species, multiplied by the per-instance MultiMesh colour (colonial
// moss/lichen cell tint; white = unchanged for ordinary species) before reaching COLOR here.
// INSTANCE_CUSTOM = (tint variation, health, wobble, seed).
// surface_mode picks the micro-detail: 0 moss (photo-scanned moss, CC0 ambientCG, tinted to the species),
// 1 lichen (lobed, crusty procedural relief), 2 vascular plants (veined, waxy leaves with translucency),
// 3 fungi (matte flesh, gills, growth zones), 4 slime mold (glossy streaming protoplasm).
// Sway and poke wobble are purely cosmetic.
shader_type spatial;
render_mode diffuse_burley, cull_disabled;

#include "res://Shaders/surface.gdshaderinc"
#include "res://Shaders/bark_fissure.gdshaderinc"
#include "res://Shaders/moss_aggregate.gdshaderinc"
#include "res://Shaders/living_leaf_optics.gdshaderinc"
#include "res://Shaders/leaf_direct_light.gdshaderinc"

uniform float sway = 0.0;
uniform float stiffness = 1.0;
uniform int surface_mode = 2;
uniform float ghost_membrane = 0.0; // Species-specific thin fungal membrane lighting, never dewbonnet
uniform bool deform_leaf_tips = false;
uniform float review_time = -1.0;
uniform vec3 bioluminescence_color = vec3(0.0);
uniform float bioluminescence_strength = 0.0;
uniform float night_factor = 0.0;
uniform int emission_mask_mode = 0; // 0 foliage organs, 1 exposed body surface
uniform vec3 pampas_plume_color = vec3(0.82, 0.73, 0.54);
uniform vec3 leaf_top = vec3(0.07, 0.16, 0.055);
uniform vec3 leaf_back = vec3(0.14, 0.19, 0.09);
uniform int leaf_type = 0;
uniform float vein_strength = 0.5;
uniform float cuticle_sheen = 0.3;
// Normal-only lamina roll: stress-tested separately from silhouettes.
uniform float lamina_curl_strength = 1.3;
uniform float leaf_transmission = 0.25;
uniform int leaf_pattern = 0;
uniform bool pitcher_tissue = false;
// 0 none, 1 pale veins, 2 silver-patterned, 3 red-purple underside (+ flushed margins).
uniform int leaf_variegation = 0;
uniform bool single_leaf_sheets = false;
uniform sampler2D leaf_scan_col : source_color, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D leaf_scan_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D leaf_scan_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec4 leaf_scan_rect = vec4(0.0);
uniform bool leaf_scan_swap = false;
uniform vec3 leaf_scan_gain = vec3(1.0);
uniform float leaf_scan_blend = 0.0;
uniform float leaf_scan_normal = 0.0;
uniform float bark_repeats_per_metre = 2.2;
uniform float bark_weathering = 0.3;
uniform vec3 bark_tint = vec3(0.7, 0.67, 0.61);
uniform float colony_relief = 0.4;
uniform float u_moss_aggregate = 0.75;
uniform float u_bark_fissures = 0.65;
uniform float u_living_stem = 0.80;
uniform sampler2D moss_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;

uniform sampler2D bark_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D bark_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D bark_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;

uniform vec3 top_pigment = vec3(-1.0);
uniform vec3 back_pigment = vec3(-1.0);
uniform float transmission = -1.0;

// Derivative frame follows each branch/leaf's authored UVs, including curved attachments.
vec3 surface_normal(vec3 base, vec3 p, vec2 uv, vec3 detail) {
    return cotangent_normal(base, p, uv, detail);
}

varying vec4 inst;
varying vec3 world_pos;
varying vec3 obj_pos;

void vertex() {
    inst = INSTANCE_CUSTOM;
    obj_pos = VERTEX;
    float clock = review_time >= 0.0 ? review_time : 0.0;

    if (surface_mode == 4) {
        vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        float phase = clock * 1.8 - wp.x * 18.0 - wp.z * 11.0;
        // Micrometre-scale streaming thickness, never translation of the colony.
        VERTEX += NORMAL * (0.00018 * sin(phase));
    }

    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float lum(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

void fragment() {
    tissue_fiber_view = vec3(1.0, 0.0, 0.0);
    tissue_fiber_mask = 0.0;
    tissue_phase_mask = surface_mode == 4 ? 0.65 :
        (surface_mode == 2 && UV2.y > .5 && UV2.x < 1.2 ? 1.0 : 0.0);
    // Shared light() uses optical thickness directly; slime is thin living plasmodium.
    tissue_tau = surface_mode == 4 ? 0.4 : 0.0;
    tissue_rim = 0.0;
    // Double-sided normal handling: invert backface normal so undersides do not render dark or inverted.
    if (!FRONT_FACING) {
        NORMAL = -NORMAL;
    }
    vec3 c = srgb_to_linear(COLOR.rgb) * (1.0 + (inst.x - 0.5) * 0.3);
    vec3 wn = finite_surface_normal((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz, vec3(0.0, 1.0, 0.0));
    vec3 n = wn;
    float rough = 0.75;
    if (surface_mode == 0) {
        // moss: scanned moss detail projected from above, recoloured to the species
        vec3 tn;
        vec4 m = sample_untiled(moss_col, moss_nrm, moss_rgh, world_pos.xz / 0.35 + inst.w * 13.0, tn);
        float avg = max(lum(textureLod(moss_col, vec2(0.5), 10.0).rgb), 0.02);
        // keep the scan's natural greens and browns, steered toward the species colour
        c = mix(c * (0.65 + 0.35 * m.rgb / avg), m.rgb * lum(c) / avg, 0.34);
        n = normalize(wn + vec3(tn.x, 0.0, -tn.y) * colony_relief);
        vec3 porous, porous_n;
        float porous_occ;
        moss_aggregate(c, wn, CAMERA_POSITION_WORLD - world_pos,
            world_pos.xz, 1.0 - smoothstep(.55, 1.04, length(obj_pos.xz)),
            u_moss_aggregate, porous, porous_n, porous_occ);
        c = porous * porous_occ;
        n = normalize(mix(n, porous_n, .58 * clamp(u_moss_aggregate, 0.0, 1.0)));
        rough = clamp(m.a, 0.76, 0.98);
        // mats thin out and darken toward their rim so they settle into the soil instead of reading as a plate
        float rimd = length(obj_pos.xz);
        c *= mix(0.45, 1.0, smoothstep(1.05, 0.55, rimd));
    } else if (surface_mode == 1) {
        // lichen: lobed rosettes with raised rims and powdery centres
        vec2 p = obj_pos.xz * 9.0 + inst.w * 17.0;
        float lobes = mix(vnoise(p), vnoise(p * 1.87 + 4.3), 0.35);
        float rim = smoothstep(0.42, 0.55, lobes) - smoothstep(0.55, 0.7, lobes);
        c *= 0.84 + 0.18 * lobes + 0.07 * rim;
        c = mix(c, vec3(lum(c)) * 1.1, smoothstep(0.66, 0.84, vnoise(p * 2.6 + 7.1)) * 0.22);
        float e = 0.02;
        float dx = fbm(p + vec2(e, 0.0)) - lobes, dz = fbm(p + vec2(0.0, e)) - lobes;
        n = normalize(wn - vec3(dx, 0.0, dz) / e * colony_relief * 0.055);
        rough = 0.95;
    } else if (surface_mode == 4) {
        float stream = 0.5 + 0.5 * sin(UV.x * 42.0 - (review_time >= 0.0 ? review_time : 0.0) * 1.8 + world_pos.z * 18.0);
        float vein = pow(0.5 + 0.5 * sin(UV.y * 29.0 + stream * 1.8), 6.0);
        c = mix(c, srgb_to_linear(vec3(0.88, 0.64, 0.07)), 0.55);
        c *= 0.88 + 0.12 * stream + 0.10 * vein;
        rough = 0.12;
    } else if (surface_mode == 3) {
        // fungi: matte, faintly translucent flesh; radial gill lines on undersides (UV2.x = 1) and fine
        // concentric growth zones on caps and shelves (UV.x runs from centre to rim)
        float warp = (vnoise(obj_pos.xz * 17.0 + inst.w * 9.0) - 0.5) * 0.12;
        float gills = UV2.x > 0.5 ? 0.90 + 0.10 * smoothstep(0.35, 0.65, fract((UV.x + warp) * 31.0)) : 1.0;
        float zones = 0.94 + 0.09 * vnoise(vec2(UV.x * 8.0, UV.y * 5.0) + inst.w * 19.0);
        float flesh = vnoise(obj_pos.xz * 29.0 + inst.w * 11.0);
        c *= gills * zones * (0.88 + 0.16 * flesh);
        float cap_top = smoothstep(0.42, 0.76, wn.y) * (1.0 - step(0.5, UV2.x));
        c *= mix(vec3(1.0), vec3(0.76, 0.71, 0.62), cap_top * 0.82);
        c *= 1.0 - cap_top * 0.12 * smoothstep(0.70, 1.0, UV.y);
        n = normalize(wn + vec3(dFdx(flesh), 0.0, dFdy(flesh)) * colony_relief * 0.35);
        rough = UV2.x > 0.5 ? 0.93 : mix(0.80, 0.95, flesh);
        // velvety bloom: fine fibrous speckle and a soft grazing-angle sheen instead of a plastic highlight
        float fuzz = vnoise(obj_pos.xy * 140.0 + obj_pos.zy * 97.0 + inst.w * 5.0);
        c *= 0.9 + 0.14 * fuzz;
        float ndv = clamp(dot(wn, normalize(CAMERA_POSITION_WORLD - world_pos)), 0.0, 1.0);
        c *= 1.0 + 0.22 * pow(1.0 - ndv, 2.0);
        if (ghost_membrane > 0.5) {
            // Unlike Dewbonnet's damp flesh, spectral tissue is a very thin
            // light-scattering membrane. Edge emphasis is driven by real cap
            // radial UVs and is never applied to stalks or the underside glow.
            float cap = (1.0 - step(0.5, UV2.x)) * step(0.5, UV2.y);
            float rim = cap * smoothstep(0.37, 0.98, UV.y);
            vec3 cold = vec3(0.66, 0.83, 0.94) * max(lum(c), 0.08);
            c = mix(c, cold, 0.18 * cap + 0.33 * rim);
            c *= 1.0 - 0.24 * cap * (1.0 - smoothstep(0.12, 0.94, UV.y));
            rough = mix(rough, 0.49, cap * 0.70);
            rough = mix(rough, 0.72, step(0.5, UV2.x) * 0.38);
        }
    } else {
        // vascular plants normally use leaf veins; woody meshes mark trunk/branch vertices with UV2.x=2 so
        // bark stays matte, opaque-looking and longitudinally rough instead of acquiring leaf venation.
        if (UV2.x > 1.5) {
            // Isotropic physical mapping: UV.x is distance along trunk in metres; UV.y is 0..1 around circumference.
            vec2 buv = vec2(UV.y, UV.x * bark_repeats_per_metre + inst.w * 3.0);
            vec3 bark = texture(bark_col, buv).rgb;
            // Scanned species bark stays recognisable; exposure, old cracks and moss add restrained history.
            float exposure = fbm(obj_pos.xz * 1.4 + UV.x * 0.12 + inst.w * 9.0);
            c = bark * bark_tint;
            c *= mix(1.0 - bark_weathering * 0.26, 1.0 + bark_weathering * 0.09, exposure);
            vec3 bn = unpack_normal(texture(bark_nrm, buv).rgb);
            float raw_rgh = texture(bark_rgh, buv).r;
            vec3 cork, cork_normal;
            float cork_ao, cork_groove;
            bark_fissure_pbr(c, bn, raw_rgh, buv, u_bark_fissures, bark_weathering, exposure,
                cork, cork_normal, rough, cork_ao, cork_groove);
            c = cork * cork_ao;
            n = stem_surface_normal(wn, world_pos, buv, cork_normal);
        } else if (pitcher_tissue && UV2.y < -1.5 && UV2.y > -3.5) {
            // Trap walls and hoods carry ochre/green/brown living tissue, distinct from the flower.
            // Original procedural pigment follows organ UVs; it is stable while the plant sways.
            vec2 p = vec2(UV.x * 8.0, UV.y * 17.0) + vec2(inst.w * 19.0, inst.w * 7.0);
            float patch = fbm(p + vec2(vnoise(p * 0.7), vnoise(p * 0.9 + 8.0)) * 1.3);
            vec3 yellow = srgb_to_linear(vec3(0.72, 0.69, 0.30));
            vec3 green = srgb_to_linear(vec3(0.34, 0.48, 0.17));
            vec3 brown = srgb_to_linear(vec3(0.42, 0.27, 0.12));
            vec3 mottled = mix(yellow, green, smoothstep(0.26, 0.55, patch));
            mottled = mix(mottled, brown, smoothstep(0.56, 0.72, patch));
            c = mix(c, mottled, 0.65);
            if (UV2.y < -2.5) c *= 0.40 + UV.x * 0.25;
            c *= 0.94 + 0.08 * vnoise(p * 5.0);
            rough = 0.49;
        } else if (pitcher_tissue && UV2.y < -3.5) {
            rough = 0.19;
        } else if (UV2.y > 0.5 && UV2.x > 1.20 && UV2.x < 1.50) {
            // Authored organs keep their own pigment and geometric normals. Do not
            // sample foliage textures or apply leaf veins before shading petals.
            // Authored pampas rachillae use a dedicated organ marker so scanned leaf palettes cannot turn
            // their pale, dry plume tissue green. Glowbriar nodules retain their own mint vertex pigment.
            if (UV2.x > 1.20 && UV2.x < 1.26) {
                c = srgb_to_linear(pampas_plume_color);
                rough = 0.88;
            } else if (UV2.x >= 1.26 && UV2.x < 1.30) {
                // Prismatic plume (plumegrass). The tuft's place in the spectral ladder is baked
                // into the filament pigment, so each step of the ladder is its own colour; the
                // view angle then rotates that pigment the way a thin film does, and each step
                // glints differently as the plant turns.
                c = srgb_to_linear(COLOR.rgb);
                float ndv = clamp(dot(wn, normalize(CAMERA_POSITION_WORLD - world_pos)), 0.0, 1.0);
                float shift = pow(1.0 - ndv, 2.2);
                c = mix(c, c.gbr * 1.18, shift * 0.62);
                rough = 0.66;
            } else if (UV2.x > 1.30 && UV2.x < 1.50) {
                c = srgb_to_linear(COLOR.rgb);
                rough = 0.42;
            }
        } else if (UV2.y > 0.5) {
            float along = UV.x, across = UV.y - 0.5;
            float face = (!FRONT_FACING || (UV2.x > 0.5 && UV2.x < 1.5)) ? 1.0 : 0.0;
            vec3 actual_leaf_top = top_pigment.r >= 0.0 ? top_pigment : leaf_top;
            vec3 actual_leaf_back = back_pigment.r >= 0.0 ? back_pigment : leaf_back;
            vec3 pigment = mix(actual_leaf_top, actual_leaf_back, face);
            float cells = vnoise(vec2(along * 24.0, across * 31.0) + inst.w * 13.0);
            float micro = vnoise(vec2(along * 91.0, across * 105.0) + inst.w * 27.0);
            float mid = 1.0 - smoothstep(0.006, 0.026 + fwidth(across), abs(across));
            float pair = abs(fract(along * mix(6.0, 10.0, float(leaf_type == 2)) + inst.w * 0.13) - 0.5);
            float branch = 1.0 - smoothstep(0.01, 0.055 + fwidth(pair), abs(pair - abs(across) * 0.46));
            branch *= smoothstep(0.48, 0.30, abs(across));
            float venation = (mid * 0.65 + branch * 0.35) * vein_strength;
            float edge = smoothstep(0.36, 0.50, abs(across));
            float age = smoothstep(0.73, 1.0, along) * (0.25 + 0.75 * vnoise(vec2(inst.w * 9.0, along * 5.0)));
            c = leaf_pigment(c, pigment,
                fract(inst.w * 7.31 + UV2.x * .37 + inst.x * 3.7),
                inst.w * 11.13 + UV2.x * 5.27);
            float leaf_age = fract(inst.w * 7.31 + floor(along * 0.0) + UV2.x * 0.37 + inst.x * 3.7);
            // Preserve developmental change without a cumulative fluorescent green boost.
            c = mix(c * vec3(.96,1.01,1.00),
                    c * vec3(1.06,.98,.88),smoothstep(.25,.95,leaf_age));
            c *= leaf_mesophyll_pattern(vec2(along,across+.5),inst.w*13.7);
            c *= (0.94 + 0.09 * cells + 0.023 * micro + 0.04 * venation) * (1.0 - edge * .07);
            c = mix(c, c * vec3(1.11, 1.04, 0.78), age * (leaf_type == 5 ? 0.05 : 0.08));
            // Distal blade pigment shift, not a baked-in directional light highlight.
            if (leaf_type == 5) c *= mix(.97,1.04,smoothstep(.15,.85,along));
            float stripe = 0.5;
            if (leaf_pattern == 0) c = mix(c, c * vec3(1.35, 1.40, 1.10), clamp(mid * 0.7 + branch * 0.3, 0.0, 1.0) * (1.0 - face) * 0.35);
            // Each tissue family carries a different visible pigment pattern. Keep
            // the markings broad enough to survive the game's normal camera distance.
            if (leaf_pattern == 3) {
                // Ivy-like leaves: lustrous cuticle and conspicuous pale vein fans.
                float pale_vein = clamp(mid * 0.58 + branch * 0.42, 0.0, 1.0);
                c = mix(c, c * vec3(1.27, 1.34, 1.14), pale_vein * (1.0 - face) * 0.42);
                c *= 0.94 + 0.12 * cells;
            } else if (leaf_pattern == 4) {
                // Succulents: wax bloom with sparse, irregular light flecks.
                float fleck = smoothstep(0.76, 0.91, micro) * smoothstep(0.11, 0.38, along);
                c = mix(c, c * vec3(1.20, 1.18, 1.12), fleck * 0.28 + face * 0.12);
                c *= 0.93 + 0.11 * cells;
            } else if (leaf_pattern == 5) {
                // Needles and reeds carry fine parallel longitudinal striations.
                stripe = 0.5 + 0.5 * cos(across * 55.0 + cells * 2.5);
                c *= 0.90 + 0.16 * stripe;
            } else if (leaf_pattern == 6) {
                // Fern pinnules: fine branching veins and light transmitted edges.
                c *= 0.90 + 0.14 * cells + 0.10 * branch;
                c = mix(c, c * vec3(1.06, 1.13, 0.95), edge * 0.22);
            } else if (leaf_pattern == 7) {
                // Glaucous farina (mooncoin): the blade wears a dry, dusty silver bloom rather
                // than a wet cuticle. It reads matte, and the bloom lifts the tissue towards a
                // cool dust instead of towards a specular highlight.
                float dust = smoothstep(0.30, 0.78, cells * 0.6 + micro * 0.4);
                c = mix(c, c * vec3(1.10, 1.12, 1.13), dust * 0.42);
                c *= 0.95 + 0.07 * micro;
            }
            // Every broad/strap blade: pale raised midrib and secondary veins on the upper face, fresher
            // saturated young leaves, and a lift so shaded foliage still reads as leaf rather than silhouette.
            float midrib = 1.0 - smoothstep(0.010, 0.034 + fwidth(across), abs(across));
            float top_face = 1.0 - face;
            c = mix(c, c * vec3(1.19,1.24,1.13) + vec3(.002,.003,.001),midrib * top_face * .42);
            c = mix(c, c * vec3(1.09,1.12,1.04),branch * top_face * (leaf_pattern == 5 ? .08 : .26));
            float young = smoothstep(0.6, 0.95, leaf_age);
            c = mix(c, c * vec3(.99,1.08,.96),young * .3);
            c *= mix(1.07, 1.0, smoothstep(0.015, 0.07, lum(c)));
            if (leaf_variegation == 1) {
                float pv = clamp(midrib * 0.9 + branch * 0.75, 0.0, 1.0);
                c = mix(c, vec3(lum(c)) * vec3(2.5, 2.8, 2.2) + vec3(0.03, 0.04, 0.03), pv * top_face * 0.65);
            } else if (leaf_variegation == 2) {
                float sv = smoothstep(0.42, 0.62, vnoise(vec2(along * 6.0, across * 8.0) + inst.w * 5.0));
                sv *= smoothstep(0.44, 0.22, abs(across)) * (1.0 - midrib) * (1.0 - branch * 0.7);
                c = mix(c, vec3(0.30, 0.34, 0.32), sv * top_face * 0.62);
            } else if (leaf_variegation == 3) {
                c = mix(c, vec3(0.20, 0.025, 0.07) * (0.8 + 0.4 * cells), face * 0.85);
                c = mix(c, c * vec3(1.9, 0.6, 0.85), edge * top_face * 0.45);
            } else if (leaf_variegation == 4) {
                // White clover's chevron: a pale band across the upper face pointing at the leaflet
                // apex. Drawn from the blade's own coordinates, so it stays a clean band at any
                // leaf size instead of breaking into the generic vein lattice. It is a paler green
                // tissue band, not a white stripe, so it keeps the blade's own shading.
                float chevron_axis = 0.34 + 0.30 * abs(across);
                float chevron = 1.0 - smoothstep(0.025, 0.100, abs(along - chevron_axis));
                chevron *= 1.0 - midrib * 0.30;
                vec3 pale = mix(c, vec3(lum(c)) * vec3(1.62, 1.70, 1.40) + vec3(0.018, 0.024, 0.014), 0.8);
                c = mix(c, pale, chevron * top_face * 0.72);
            } else if (leaf_variegation == 5) {
                // Azure crystal frost (glassfinger): the distal blade grows a saturated blue
                // crystalline veneer over the dorsal flanks, densest towards the tip and
                // strongest along the ridge, with the red tip flush burning through it.
                float crystal_ridge = 1.0 - smoothstep(0.03, 0.42, abs(across));
                float cellA = vnoise(vec2(along * 20.0, across * 16.0) + inst.w * 23.7);
                float cellB = vnoise(vec2(along * 47.0, across * 38.0) + inst.w * 51.3);
                float vesicle = smoothstep(0.54, 0.86, cellA * 0.68 + cellB * 0.32);
                float crystal_reach = smoothstep(0.42, 0.92, along);
                float frost = (0.30 + 0.70 * vesicle) * crystal_reach * (0.34 + 0.66 * crystal_ridge);
                vec3 azure = vec3(0.004, 0.026, 0.22);
                vec3 azureHot = vec3(0.012, 0.145, 0.90);
                c = mix(c, mix(azure, azureHot, vesicle), clamp(frost, 0.0, 1.0) * 0.90);
                float crystal_flush = smoothstep(0.84, 1.0, along);
                c = mix(c, vec3(0.42, 0.030, 0.050), crystal_flush * 0.72);
            }
            float ridge = venation * (leaf_type == 1 || leaf_type == 4 ? 0.11 : 0.32);
            if (leaf_pattern == 3) ridge = venation * 0.50;
            else if (leaf_pattern == 4) ridge = venation * 0.06;
            else if (leaf_pattern == 5) ridge = venation * 0.08 + (stripe - 0.5) * 0.10;
            else if (leaf_pattern == 6) ridge = venation * 0.24 + branch * 0.10;
            else if (leaf_pattern == 7) ridge = venation * 0.05;
            ridge -= midrib * 0.10; // shallow midrib groove
            vec2 uv_slope = leaf_uv_gradient(ridge, UV);
            float longitudinal = smoothstep(.03,.24,along) *
                (1.0 - smoothstep(.86,.99,along));
            float transverse = sign(across) * smoothstep(.06,.44,abs(across));
            float curl_identity = fract(inst.w * 15.71 + inst.x * 3.1);
            float roll_sign = curl_identity < .5 ? -1.0 : 1.0;
            vec2 lamina_curve = vec2(
                cos(along * 3.14159265) * .32 * longitudinal,
                transverse * sin(along * 3.14159265) *
                longitudinal * roll_sign * (leaf_pattern == 5 ? .52 : .79));
            vec3 leaf_n = normalize(vec3(
                -uv_slope * .055 + lamina_curl_strength * lamina_curve, 1.0));
            n = surface_normal(wn, world_pos, UV, leaf_n);
            float family_rough = leaf_type == 1 ? 0.78 : leaf_type == 2 ? 0.74 : leaf_type == 3 ? 0.52 : leaf_type == 4 ? 0.46 : leaf_type == 5 ? 0.68 : 0.61;
            rough = clamp(family_rough - 0.10 - cuticle_sheen * 0.20 * top_face + (cells - 0.5) * 0.12 + face * 0.16, 0.22, 0.95);
            if (leaf_pattern == 3) rough = clamp(rough - 0.18 * top_face, 0.24, 0.85);
            else if (leaf_pattern == 4) rough = clamp(rough - 0.08 + (cells - 0.5) * 0.08, 0.32, 0.85);
            else if (leaf_pattern == 5) rough = clamp(rough - 0.04, 0.32, 0.92);
            else if (leaf_pattern == 7) rough = clamp(rough + 0.22, 0.50, 0.95);
            if (leaf_scan_blend > 0.0) {
                vec2 atlas_axis = leaf_scan_swap ? vec2(UV.y, UV.x) : UV;
                vec2 scan_uv = leaf_scan_rect.xy + atlas_axis * leaf_scan_rect.zw;
                vec3 scan_col = texture(leaf_scan_col, scan_uv).rgb * leaf_scan_gain;
                // The back has its own tissue pigment; carry over photographed
                // fine variation without painting a green top onto the underside.
                float scan_value = clamp(lum(scan_col) / 0.12, 0.65, 1.35);
                vec3 scanned = mix(scan_col, actual_leaf_back * scan_value, face);
                c = mix(c, scanned, leaf_scan_blend);
                if (leaf_pattern == 4)
                    c = mix(c, srgb_to_linear(COLOR.rgb), smoothstep(0.68, 0.96, along) * 0.72);
                vec3 scan_n = unpack_normal(texture(leaf_scan_nrm, scan_uv).rgb);
                n = surface_normal(wn, world_pos, scan_uv,
                    normalize(vec3(scan_n.xy * leaf_scan_normal, scan_n.z)));
                rough = clamp(mix(rough, texture(leaf_scan_rgh, scan_uv).r,
                    leaf_scan_blend * 0.8) + face * 0.12, 0.28, 0.98);
            }

        } else if (UV2.x > 0.5) {
            // Glossy fruit / berries
            rough = 0.22;
        } else {
            // Living epidermis, not mature cork. Longitudinal ridges follow the
            // shaft, narrow transverse node scars mark growth interruptions.
            vec2 fiber_uv = vec2(UV.y * 22.0, UV.x * 2.4 + inst.w * 6.0);
            float rib = vnoise(fiber_uv * vec2(1.0, .55));
            float node = 1.0 - smoothstep(.04, .13 + fwidth(UV.x * 2.4),
                abs(fract(UV.x * 2.4 + inst.w * .31) - .5));
            float living_strength = clamp(u_living_stem, 0.0, 1.0);
            c *= mix(1.0, .83 + .24 * rib - .12 * node, living_strength);
            vec3 shaft_normal = surface_normal(wn, world_pos, UV,
                normalize(vec3((rib - .5) * u_living_stem * .42,
                    node * u_living_stem * .26, 1.0)));
            n = normalize(mix(n, shaft_normal, living_strength * .68));
            rough = leaf_type == 4 ? 0.52 : leaf_type == 5 ? 0.66 : 0.72;
            rough = clamp(rough + .10 * node * living_strength, .30, .92);
        }
    }
    // Stress fades to a luminance-matched olive-brown; only genuinely failing tissue reads as straw.
    // Only genuinely failing tissue fades; mid-health foliage keeps its full pigment.
    float health = smoothstep(0.0, 0.28, clamp(inst.y, 0.0, 1.0));
    vec3 faded = vec3(dot(c, vec3(0.33))) * vec3(1.25, 1.05, 0.55);
    ALBEDO = mix(faded, c, 0.30 + 0.70 * health);
    ROUGHNESS = rough;
    bool woody_surface = surface_mode == 2 && UV2.x > 1.5;
    float top_side = 1.0 - float(!FRONT_FACING || (UV2.y > 0.5 && UV2.x > 0.5 && UV2.x < 1.5));
    float leaf_spec = mix(0.35, mix(0.38, 0.50, cuticle_sheen), top_side);
    // A farina-coated blade is dust, not varnish: it keeps almost no specular lobe.
    if (leaf_pattern == 7) leaf_spec = mix(leaf_spec, 0.07, 0.86);
    float bark_spec = mix(0.42, 0.12, smoothstep(0.55, 0.92, rough));
    SPECULAR = surface_mode == 2 ? (woody_surface ? bark_spec : leaf_spec) : surface_mode == 3 ? 0.12 : 0.24;
    float actual_leaf_trans = transmission >= 0.0 ? transmission : leaf_transmission;
    float trans_clamped = clamp(actual_leaf_trans, 0.0, 0.5);
    float vein_mass = UV2.y > .5 ?
      (1. - smoothstep(.010,.034+fwidth(UV.y-.5),abs(UV.y-.5))) : 0.;
    float tissue_depth = leaf_type == 4 ? 2.10 : leaf_type == 3 ? 1.25 :
       leaf_type == 5 ? .56 : leaf_type == 2 ? .43 : .82;
    vec3 leaf_backlight=leaf_through(c,trans_clamped,vein_mass,tissue_depth,1.-top_side);
    BACKLIGHT = surface_mode == 2 ? (woody_surface ? vec3(0.0) : (UV2.y > 0.5 ? leaf_backlight : c * 0.02)) : c * (surface_mode == 3 ? 0.18 : 0.05);
    if (surface_mode == 4) {
        SSS_STRENGTH = 0.85;
        BACKLIGHT = vec3(0.65, 0.48, 0.05);
        CLEARCOAT = 1.0;
        CLEARCOAT_ROUGHNESS = 0.03;
        SPECULAR = 0.58;
        SSS_TRANSMITTANCE_COLOR = vec4(0.90, 0.61, 0.06, 1.0);
        SSS_TRANSMITTANCE_DEPTH = 0.015;
    }
    if (surface_mode == 3 && ghost_membrane > 0.5) {
        // Stable opaque depth writes stay intact; thin gills/caps scatter
        // transmitted light rather than making a sorting-prone alpha cloud.
        float membrane = (1.0 - step(0.5, UV2.x)) * step(0.5, UV2.y);
        float gill = step(0.5, UV2.x);
        float edge = membrane * smoothstep(0.40, 0.98, UV.y);
        BACKLIGHT += vec3(0.14, 0.29, 0.38) * (0.40 * membrane + 0.65 * edge + 0.24 * gill);
        SSS_TRANSMITTANCE_COLOR = vec4(0.60, 0.83, 0.95, 1.0);
        SSS_TRANSMITTANCE_DEPTH = 0.14;
        SSS_TRANSMITTANCE_BOOST = 0.25;
    }
    if (surface_mode == 2 && !woody_surface && UV2.y > .5) {
        SSS_TRANSMITTANCE_COLOR=vec4(clamp(leaf_backlight/max(c.g*.36,.015),vec3(0.),vec3(1.)),1.);
        SSS_TRANSMITTANCE_DEPTH=mix(.60,.18,vein_mass);
        SSS_TRANSMITTANCE_BOOST=.12;
    }
    float marked_nodule = step(1.30, UV2.x) * (1.0 - step(1.50, UV2.x)) * step(0.5, UV2.y);
    float gill_mask = step(0.5, UV2.x) * (1.0 - step(1.5, UV2.x));
    float seed_head_mask = step(1.30, UV2.x) * (1.0 - step(1.50, UV2.x));
    float center_stripe = (1.0 - smoothstep(0.16, 0.25, abs(UV.y - 0.5))) * step(0.5, UV2.y);
    float edge_distance = min(min(UV.x, 1.0 - UV.x), 0.5 - abs(UV.y - 0.5));
    float edge_strip = (1.0 - smoothstep(0.025, 0.075, edge_distance)) * step(0.5, UV2.y);
    float night_mask = 0.0;
    if (emission_mask_mode == 1) night_mask = 1.0;
    else if (emission_mask_mode == 2) night_mask = marked_nodule;
    else if (emission_mask_mode == 3) night_mask = gill_mask * float(FRONT_FACING);
    else if (emission_mask_mode == 4) night_mask = seed_head_mask;
    else if (emission_mask_mode == 5) night_mask = center_stripe;
    else if (emission_mask_mode == 6) night_mask = edge_strip;
    else if (emission_mask_mode == 7) {
        // Clover chevron: the same band the leaf shader paints, so the nocturnal glow follows the
        // marking instead of lighting the whole blade.
        float clover_across = UV.y - 0.5;
        float clover_axis = 0.34 + 0.30 * abs(clover_across);
        night_mask = (1.0 - smoothstep(0.030, 0.085, abs(UV.x - clover_axis)))
            * step(0.5, UV2.y) * float(FRONT_FACING);
    }
    else night_mask = smoothstep(0.45, 0.60, UV2.y);
    EMISSION = bioluminescence_color * bioluminescence_strength * night_factor * night_mask;
    if (surface_mode == 3 && ghost_membrane > 0.5) {
        float spectral_rim = (1.0 - step(0.5, UV2.x)) * step(0.5, UV2.y) *
            smoothstep(0.70, 1.0, UV.y);
        EMISSION += bioluminescence_color * bioluminescence_strength * night_factor * 0.11 * spectral_rim;
    }
    n = finite_surface_normal(n, wn);
    NORMAL = finite_surface_normal((VIEW_MATRIX * vec4(n, 0.0)).xyz, vec3(0.0, 0.0, 1.0));
    // Evaluate quad derivatives before per-fragment organ/face branches.
    vec2 ux = dFdx(UV), uy = dFdy(UV);
    vec3 px = dFdx(world_pos), py = dFdy(world_pos);
    vec3 normal_dx = dFdx(NORMAL), normal_dy = dFdy(NORMAL);
    float normal_variance = dot(normal_dx, normal_dx) + dot(normal_dy, normal_dy);
    if (!(normal_variance >= 0.0 && normal_variance < 1e12)) normal_variance = 0.0;
    // Fiber axis for real leaf blades only. Reeds, stems, berries and
    // underside organ tags retain isotropic cuticle reflectance.
    if (surface_mode == 2 && UV2.y > .5 && UV2.x < 1.2 && FRONT_FACING) {
        float det = ux.x * uy.y - uy.x * ux.y;
        vec3 fiber_world = (px * uy.y - py * ux.y) /
            (abs(det) > 1e-7 ? det : 1.0);
        vec3 fiber_view = (VIEW_MATRIX * vec4(fiber_world, 0.0)).xyz;
        float fiber_len = length(fiber_view);
        if (fiber_len > 1e-5 && abs(det) > 1e-7) {
            tissue_fiber_view = fiber_view / fiber_len;
            tissue_fiber_mask = 1.0;
        }
    }
    if (surface_mode == 2 && !woody_surface && UV2.y > .5 && UV2.x < 1.2) {
        ROUGHNESS = clamp(sqrt(ROUGHNESS * ROUGHNESS + min(2.0 * normal_variance, .25)), .35, 1.0);
    }
    if (!(ROUGHNESS >= 0.0 && ROUGHNESS <= 1.0)) ROUGHNESS = 0.75;
    // Photographic/color modulation changes reflectance, never emission.
    // Bright plume/scan ratios must remain inside the diffuse energy budget.
    ALBEDO = clamp(ALBEDO, vec3(0.0), vec3(0.98));
    if (tissue_light_diagnostic == 5) {
        SSS_STRENGTH = 0.0;
        SSS_TRANSMITTANCE_COLOR = vec4(0.0);
        SSS_TRANSMITTANCE_DEPTH = 0.1;
        SSS_TRANSMITTANCE_BOOST = 0.0;
        BACKLIGHT = vec3(0.0);
    }
}
