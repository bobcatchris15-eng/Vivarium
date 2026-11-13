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
#include "res://Shaders/repr_skyvis.gdshaderinc"

uniform float sway = 0.0;
uniform float stiffness = 1.0;
uniform bool dry_fungal_cuticle = false;
uniform int surface_mode = 2;
uniform bool dewbonnet_gills = false;
uniform sampler2D dewbonnet_gill_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
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
uniform int stem_family = 0;
uniform float vein_strength = 0.5;
uniform float cuticle_sheen = 0.3;
// Normal-only lamina roll: stress-tested separately from silhouettes.
uniform float lamina_curl_strength = 1.3;
uniform float leaf_transmission = 0.25;
uniform float canopy_occlusion = 0.52;
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
    bool has_custom = (abs(INSTANCE_CUSTOM.x) + abs(INSTANCE_CUSTOM.y) + abs(INSTANCE_CUSTOM.z) + abs(INSTANCE_CUSTOM.w)) > 1e-4;
    inst = has_custom ? INSTANCE_CUSTOM : vec4(0.5, 1.0, 0.5, 0.35);
    obj_pos = VERTEX;
    // Review captures freeze review_time; live play follows the real clock.
    float clock = review_time >= 0.0 ? review_time : TIME;

    if (surface_mode == 4) {
        vec3 wp = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        float phase = clock * 1.8 - wp.x * 18.0 - wp.z * 11.0;
        // Micrometre-scale streaming thickness, never translation of the colony.
        VERTEX += NORMAL * (0.00018 * sin(phase));
    }

    if (surface_mode == 2) {
        // Micro-breeze for living foliage. Phase comes from world position plus the
        // per-instance seed; weight is tip-based for blades and height-based for stems,
        // so the base stays planted and only the free tissue drifts.
        float is_leaf = step(0.5, UV2.y);
        float tip = mix(clamp(VERTEX.y * 4.0, 0.0, 1.0), clamp(UV.x, 0.0, 1.0), is_leaf);
        tip = tip * tip * (3.0 - 2.0 * tip);
        vec3 wp0 = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
        float phase = wp0.x * 1.10 + wp0.z * 0.80 + inst.w * 6.2831853 + inst.x * 2.1;
        float amp = 0.0019 * mix(0.50, 1.20, clamp(sway, 0.0, 1.0));
        VERTEX.x += sin(clock * 0.61 + phase) * amp * tip;
        VERTEX.z += cos(clock * 0.49 + phase * 1.17) * amp * 0.80 * tip;
        // Tiny normal flutter only on blades, never on woody stems.
        VERTEX += NORMAL * sin(clock * 2.55 + phase * 2.7) * 0.00055 * tip * is_leaf;
    }

    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float lum(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

void fragment() {
    tissue_fiber_view = vec3(1.0, 0.0, 0.0);
    tissue_fiber_mask = 0.0;
    tissue_phase_mask = surface_mode == 4 ? 0.65 :
        (surface_mode == 2 && UV2.y > .5 && UV2.x < 1.2 ? 1.0 : 0.0);
    tissue_nonleaf_mask = surface_mode == 2 && (UV2.x > 1.5 || (UV2.y >= 0.0 && UV2.y < .5 && UV2.x < .5)) ? 1.0 : 0.0;
    // Shared light() uses optical thickness directly; slime is thin living plasmodium.
    tissue_tau = surface_mode == 4 ? 0.4 : 0.0;
    tissue_rim = 0.0;
    // Double-sided normal handling: invert backface normal so undersides do not render dark or inverted.
    if (!FRONT_FACING) {
        NORMAL = -NORMAL;
    }
    // Resolvable detail distance: high-frequency epidermal noise is faded out beyond ~5 m.
    float cam_dist = length(CAMERA_POSITION_WORLD - world_pos);
    float micro_fade = leaf_micro_fade(CAMERA_POSITION_WORLD, world_pos);
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
        float gills = !dewbonnet_gills && UV2.x > 0.5 ? 0.90 + 0.10 * smoothstep(0.35, 0.65, fract((UV.x + warp) * 31.0)) : 1.0;
        float zones = 0.94 + 0.09 * vnoise(vec2(UV.x * 8.0, UV.y * 5.0) + inst.w * 19.0);
        float flesh = vnoise(obj_pos.xz * 29.0 + inst.w * 11.0);
        c *= gills * zones * (0.88 + 0.16 * flesh);
        // Organ split: near-vertical authored surfaces are stipe tissue, near-horizontal
        // ones are pileus. The gill marking (UV2.x >= 0.5) keeps the underside out of both.
        float gill_face = step(0.5, UV2.x) * (1.0 - step(1.5, UV2.x));
        float stipe = (1.0 - gill_face) * smoothstep(0.62, 0.18, abs(wn.y));
        float pileus = (1.0 - gill_face) * (1.0 - stipe);
        float cap_top = smoothstep(0.42, 0.76, wn.y) * (1.0 - gill_face);
        c *= mix(vec3(1.0), vec3(0.76, 0.71, 0.62), cap_top * 0.82);
        c *= 1.0 - cap_top * 0.12 * smoothstep(0.70, 1.0, UV.y);
        n = normalize(wn + vec3(dFdx(flesh), 0.0, dFdy(flesh)) * colony_relief * 0.35);
        if (dewbonnet_gills && UV2.x > 0.5 && UV2.x < 1.5) {
            vec2 gill_uv = vec2(UV.x, UV.y / 16.0);
            vec3 relief = texture(dewbonnet_gill_normal, gill_uv).rgb * 2.0 - 1.0;
            float relief_mask = smoothstep(0.025, 0.12, gill_uv.y)
                * (1.0 - smoothstep(0.96, 1.0, gill_uv.y));
            relief = normalize(mix(vec3(0.0, 0.0, 1.0), relief, relief_mask));
            n = surface_normal(wn, world_pos, gill_uv, relief);
        }
        rough = UV2.x > 0.5 ? 0.93 : mix(0.80, 0.95, flesh);
        if (pileus > 0.001) {
            // Pileus cuticle: fine radial striation, a hygrophanous wet umbo and a thin,
            // translucent striate margin. Cap tissue is damp radially-fibred flesh, not plastic.
            float striate = 0.5 + 0.5 * sin(UV.y * 64.0 + vnoise(world_pos.xz * 18.0) * 2.5);
            float striate_vis = mix(0.5, striate, micro_fade);
            float rim = smoothstep(0.62, 1.0, UV.y);
            float umbo = 1.0 - smoothstep(0.0, 0.42, UV.y);
            c *= mix(1.0, 0.90 + 0.14 * striate_vis, (0.35 + 0.65 * rim) * pileus);
            // The wet umbo holds deeper, richer pigment; the margin is thin and lit through.
            c = mix(c, c * vec3(0.84, 0.90, 0.82), pileus * umbo * 0.35);
            c = mix(c, c * vec3(1.08, 1.14, 1.00) + vec3(0.010, 0.012, 0.006), pileus * rim * 0.42);
            rough = clamp(rough - pileus * (0.06 + 0.10 * rim), 0.55, 0.95);
            // Viscid cuticular micro-relief: only resolved close to the camera.
            if (micro_fade > 0.01) {
                float e = 0.15;
                float cg = vnoise(vec2(UV.y * 34.0, UV.x * 11.0) + inst.w * 21.0);
                float cgx = vnoise(vec2((UV.y + e) * 34.0, UV.x * 11.0) + inst.w * 21.0);
                float cgy = vnoise(vec2(UV.y * 34.0, (UV.x + e) * 11.0) + inst.w * 21.0);
                vec2 cuticle_slope = vec2(cgx - cg, cgy - cg) * (0.55 * micro_fade * pileus);
                n = surface_normal(n, world_pos, UV, normalize(vec3(cuticle_slope, 1.0)));
            }
        }
        if (stipe > 0.001) {
            // Stipe: long silky parallel fibres, a bruised veil ring and a dark
            // mycelial foot so the stem grows out of the litter instead of sitting on it.
            float fib = sin(UV.y * 90.0 + vnoise(vec2(UV.x * 9.0, inst.w * 7.0)) * 2.0);
            float fib_vis = mix(0.5, 0.5 + 0.5 * fib, micro_fade);
            c *= mix(1.0, 0.90 + 0.13 * fib_vis, stipe);
            float ring = 1.0 - smoothstep(0.025, 0.075, abs(UV.x - 0.55));
            c = mix(c, c * vec3(1.10, 0.92, 0.78), stipe * ring * 0.55);
            float basal_stain = 1.0 - smoothstep(0.02, 0.18, UV.x);
            c = mix(c, vec3(0.055, 0.043, 0.028), stipe * basal_stain * 0.65);
            rough = clamp(rough + stipe * ring * 0.04, 0.60, 0.96);
            if (micro_fade > 0.01) {
                float f0 = vnoise(vec2(UV.y * 26.0, UV.x * 9.0) + inst.w * 13.0);
                float f1 = vnoise(vec2((UV.y + 0.2) * 26.0, UV.x * 9.0) + inst.w * 13.0);
                vec2 fib_slope = vec2(f1 - f0, 0.0) * (0.50 * micro_fade * stipe);
                n = surface_normal(n, world_pos, UV, normalize(vec3(fib_slope, 1.0)));
            }
        }
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
            c = bark * bark_tint * 1.38;
            c *= mix(1.0 - bark_weathering * 0.26, 1.0 + bark_weathering * 0.09, exposure);
            vec3 bn = unpack_normal(texture(bark_nrm, buv).rgb);
            float raw_rgh = texture(bark_rgh, buv).r;
            vec3 cork, cork_normal;
            float cork_ao, cork_groove;
            bark_fissure_pbr(c, bn, raw_rgh, buv, u_bark_fissures, bark_weathering, exposure,
                cork, cork_normal, rough, cork_ao, cork_groove);
            c = cork * max(cork_ao, 0.44);
            if (UV2.x > 2.5) {
                // Decayed Heartwood / Callus / Hollow Log Interior:
                // Modulate texture luminance/detail with vertex color (srgb_to_linear(COLOR.rgb) * 1.65),
                // preserving dark rot gradients, warm amber callus tones, and deep cavity AO.
                c = c * srgb_to_linear(COLOR.rgb) * 1.65;
                rough = clamp(rough, 0.84, 0.98);
            }
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
                vec3 raw_organ = srgb_to_linear(COLOR.rgb);
                // Compress near-white floral pigments into realistic petal reflectance (max ~0.46 linear)
                c = raw_organ / (1.0 + raw_organ * 1.05) * 0.86;
                float floret_cell = vnoise(obj_pos.xz * 26.0 + obj_pos.y * 19.0 + inst.w * 7.0);
                float floret_ao = (0.70 + 0.30 * smoothstep(-0.45, 0.75, wn.y)) * (0.85 + 0.22 * floret_cell);
                c *= mix(vec3(0.74, 0.72, 0.64), vec3(1.04, 1.02, 0.98), floret_ao);
                rough = 0.74;
            }
        } else if (UV2.y > 0.5) {
            float along = UV.x, across = UV.y - 0.5;
            float face = (!FRONT_FACING || (UV2.x > 0.5 && UV2.x < 1.5)) ? 1.0 : 0.0;
            vec3 actual_leaf_top = top_pigment.r >= 0.0 ? top_pigment : leaf_top;
            vec3 actual_leaf_back = back_pigment.r >= 0.0 ? back_pigment : leaf_back;
            vec3 pigment = mix(actual_leaf_top, actual_leaf_back, face);
            // Abaxial mesophyll is paler and less waxy than the adaxial face.
            pigment = leaf_abaxial_tint(pigment, face);
            float basal = smoothstep(0.0, 0.12, along);
            float cells = 0.5;
            float micro = 0.5;
            if (micro_fade > 0.01) {
                cells = mix(0.5, vnoise(vec2(along * 24.0, across * 31.0) + inst.w * 13.0), micro_fade);
                micro = mix(0.5, vnoise(vec2(along * 91.0, across * 105.0) + inst.w * 27.0), micro_fade);
            }
            // 1) Primary midrib, narrowing from the petiole toward the apex.
            float mid_width = mix(0.023, 0.006, smoothstep(0.05, 0.95, along));
            float mid = (1.0 - smoothstep(mid_width * 0.5, mid_width + fwidth(across) * 0.8, abs(across))) * basal;
            // 2) Pinnate secondaries: they leave the midrib and arch forward toward the margin.
            float pairs = mix(6.5, 10.0, float(leaf_type == 2));
            float sec_phase = along * pairs - (0.45 + 0.95 * abs(across)) * abs(across);
            float sec_line = abs(fract(sec_phase) - 0.5);
            float sec = 1.0 - smoothstep(0.032, 0.042 + fwidth(sec_phase) * 0.9, sec_line);
            sec *= smoothstep(0.52, 0.27, abs(across)) * basal;
            // 3) Tertiary reticulum: sub-millimetre, so it is only evaluated close to camera.
            float tert = 0.0;
            if (cam_dist < 3.0) {
                vec2 rp = vec2(along * 48.0, across * 66.0) + inst.w * 9.0;
                float warp = vnoise(rp * 0.31 + 3.7);
                vec2 rq = rp + vec2(warp, 1.0 - warp) * 2.2;
                float tl = min(abs(fract(rq.x) - 0.5), abs(fract(rq.y) - 0.5));
                tert = (1.0 - smoothstep(0.02, 0.055 + fwidth(rq.x) + fwidth(rq.y), tl))
                    * 0.34 * basal * (1.0 - smoothstep(2.2, 3.0, cam_dist));
            }
            float venation = (mid * 0.62 + sec * 0.30 + tert) * vein_strength;
            float vein_mask = clamp(mid * 0.90 + sec * 0.55 + tert * 0.40, 0.0, 1.0) * basal;
            // Vascular tissue is optically thicker: veins attenuate transmission and read
            // dark in silhouette while the thin mesophyll between them glows.
            tissue_tau = leaf_vein_tau(0.62, vein_mask, face);
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
            if (leaf_pattern == 0) c = mix(c, c * vec3(1.06, 1.08, 0.98), clamp(mid * 0.7 + sec * 0.3, 0.0, 1.0) * (1.0 - face) * 0.20);
            // Each tissue family carries a different visible pigment pattern. Keep
            // the markings broad enough to survive the game's normal camera distance.
            if (leaf_pattern == 3) {
                // Ivy-like leaves: lustrous cuticle and conspicuous pale vein fans.
                float pale_vein = clamp(mid * 0.58 + sec * 0.42, 0.0, 1.0);
                c = mix(c, c * vec3(1.08, 1.11, 1.02), pale_vein * (1.0 - face) * 0.22);
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
                c *= 0.90 + 0.14 * cells + 0.10 * sec;
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
            c = mix(c, c * vec3(1.06, 1.08, 0.98) + vec3(.001, .0015, .0005), midrib * top_face * (vein_strength * 0.25));
            c = mix(c, c * vec3(1.03, 1.05, 1.00), sec * top_face * (leaf_pattern == 5 ? .04 : (vein_strength * 0.18)));
            float young = smoothstep(0.6, 0.95, leaf_age);
            c = mix(c, c * vec3(.99,1.08,.96),young * .3);
            c *= mix(1.07, 1.0, smoothstep(0.015, 0.07, lum(c)));
            if (leaf_variegation == 1) {
                float pv = clamp(midrib * 0.9 + sec * 0.75, 0.0, 1.0);
                c = mix(c, vec3(lum(c)) * vec3(2.5, 2.8, 2.2) + vec3(0.03, 0.04, 0.03), pv * top_face * 0.65);
            } else if (leaf_variegation == 2) {
                float sv = smoothstep(0.42, 0.62, vnoise(vec2(along * 6.0, across * 8.0) + inst.w * 5.0));
                sv *= smoothstep(0.44, 0.22, abs(across)) * (1.0 - midrib) * (1.0 - sec * 0.7);
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
            else if (leaf_pattern == 6) ridge = venation * 0.24 + sec * 0.10;
            else if (leaf_pattern == 7) ridge = venation * 0.05;
            // Two-sided vascular relief: adaxial veins are gently channeled, abaxial
            // veins stand proud of the mesophyll, and only the top keeps a midrib groove.
            ridge *= mix(-0.55, 1.10, face);
            ridge -= midrib * 0.10 * (1.0 - face);
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
            rough = clamp(family_rough - 0.10 - cuticle_sheen * 0.20 * top_face + (cells - 0.5) * 0.12 + face * 0.18, 0.22, 0.95);
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
            // Reeds show parallel epidermal fibers and occasional nodes; soft and
            // fleshy stems keep quieter, less interrupted tissue. Filter the fine
            // rib before it becomes subpixel to prevent a shimmering tube.
            float fiber_phase = UV.y * 32.0;
            float fiber_filter = 1.0 - smoothstep(.25, .75, fwidth(fiber_phase));
            float fiber = cos(fiber_phase * 6.2831853) * fiber_filter;
            float fiber_strength = stem_family == 1 ? .10 : stem_family == 3 ? .07 : stem_family == 2 ? .015 : .045;
            float node_strength = stem_family == 1 ? .07 : stem_family == 3 ? .035 : .015;
            c *= mix(1.0, .88 + .18 * rib + fiber * fiber_strength - node * node_strength, living_strength);
            vec3 shaft_normal = surface_normal(wn, world_pos, UV,
                normalize(vec3(node * living_strength * node_strength,
                    ((rib - .5) * .32 + fiber * fiber_strength * 1.4) * living_strength, 1.0)));
            n = normalize(mix(n, shaft_normal, living_strength * .68));
            rough = stem_family == 2 ? .56 : stem_family == 1 ? .72 : stem_family == 3 ? .78 : .70;
            rough = clamp(rough + .10 * node * living_strength, .30, .92);
            if (stem_family == 3) {
                c = max(c, vec3(0.026, 0.021, 0.017)) * 1.24;
                vec2 buv = vec2(UV.y, UV.x * bark_repeats_per_metre + inst.w * 3.0);
                vec3 bn = unpack_normal(texture(bark_nrm, buv).rgb);
                n = normalize(mix(n, stem_surface_normal(wn, world_pos, buv, bn), 0.58));
            }
        }
    }
    float basal_contact = smoothstep(0.0, 0.14, max(obj_pos.y, 0.0));
    c *= mix(vec3(0.52, 0.48, 0.40), vec3(1.0), basal_contact);
    // Stress fades to a luminance-matched olive-brown; only genuinely failing tissue reads as straw.
    // Only genuinely failing tissue fades; mid-health foliage keeps its full pigment.
    float health = smoothstep(0.0, 0.28, clamp(inst.y, 0.0, 1.0));
    vec3 faded = vec3(dot(c, vec3(0.33))) * vec3(1.25, 1.05, 0.55);
    ALBEDO = mix(faded, c, 0.30 + 0.70 * health);
    ROUGHNESS = dry_fungal_cuticle ? max(rough, 0.93) : rough;
    bool woody_surface = surface_mode == 2 && UV2.x > 1.5;
    float is_bark = woody_surface ? 1.0 : 0.0;
    float depth_shade = clamp(1.0 - canopy_occlusion * (1.0 - clamp(obj_pos.y * 1.5, 0.0, 1.0)) * 0.75, 0.35, 1.0);
    float ao = depth_shade * mix(0.48, 1.0, basal_contact);
    if (surface_mode == 0 || surface_mode == 1) {
        ao = mix(0.65, 1.0, basal_contact);
    }
    AO = clamp(ao, 0.20, 1.0);
    AO_LIGHT_AFFECT = mix(0.45, 0.65, is_bark);
    if (repr_skyvis_strength > 0.0) { // L1 cached occlusion (default off)
        AO *= repr_sky_visibility(world_pos, wn);
        AO_LIGHT_AFFECT = min(AO_LIGHT_AFFECT, REPR_SKYVIS_LIGHT_AFFECT);
    }
    float top_side = 1.0 - float(!FRONT_FACING || (UV2.y > 0.5 && UV2.x > 0.5 && UV2.x < 1.5));
    float leaf_spec = mix(0.20, mix(0.38, 0.50, cuticle_sheen), top_side);
    // A farina-coated blade is dust, not varnish: it keeps almost no specular lobe.
    if (leaf_pattern == 7) leaf_spec = mix(leaf_spec, 0.07, 0.86);
    float bark_spec = mix(0.42, 0.12, smoothstep(0.55, 0.92, rough));
    if (UV2.x > 2.5) bark_spec = 0.12;
    SPECULAR = surface_mode == 2 ? (woody_surface ? bark_spec : leaf_spec) : surface_mode == 3 ? (dry_fungal_cuticle ? 0.0 : 0.12) : 0.24;
    float actual_leaf_trans = transmission >= 0.0 ? transmission : leaf_transmission;
    // Foliage (surface_mode 2) transmission is owned by light() in
    // leaf_direct_light.gdshaderinc; no built-in BACKLIGHT/SSS transmittance
    // duplicate and no residual c*0.02 leak when transmission is 0.
    BACKLIGHT = surface_mode == 2 ? vec3(0.0) : c * (surface_mode == 3 ? 0.18 : 0.05);
    if (surface_mode == 3 && ghost_membrane <= 0.5) {
        // Warm fleshy subsurface glow. Fungal tissue is thin, damp and strongly
        // translucent, so low sun reads as amber flesh rather than a matte cone.
        BACKLIGHT = c * vec3(0.32, 0.20, 0.12);
        SSS_STRENGTH = dry_fungal_cuticle ? 0.13 : 0.35;
        SSS_TRANSMITTANCE_COLOR = vec4(0.42, 0.24, 0.12, 1.0);
        SSS_TRANSMITTANCE_DEPTH = 0.22;
    }
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
