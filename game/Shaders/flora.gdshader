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

uniform float sway = 1.0;
uniform float stiffness = 1.0;
uniform int surface_mode = 2;
uniform bool deform_leaf_tips = false;
uniform float review_time = -1.0;
uniform vec3 leaf_top = vec3(0.07, 0.16, 0.055);
uniform vec3 leaf_back = vec3(0.14, 0.19, 0.09);
uniform int leaf_type = 0;
uniform float vein_strength = 0.5;
uniform float cuticle_sheen = 0.3;
uniform float leaf_transmission = 0.25;
uniform int leaf_pattern = 0;
uniform bool pitcher_tissue = false;
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
uniform sampler2D moss_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;

uniform sampler2D bark_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D bark_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D bark_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;

// Derivative frame follows each branch/leaf's authored UVs, including curved attachments.
vec3 surface_normal(vec3 base, vec3 p, vec2 uv, vec3 detail) {
    vec3 px = dFdx(p), py = dFdy(p);
    vec2 ux = dFdx(uv), uy = dFdy(uv);
    vec3 r1 = cross(py, base), r2 = cross(base, px);
    vec3 t = r1 * ux.x + r2 * uy.x;
    vec3 b = r1 * ux.y + r2 * uy.y;
    float inv = inversesqrt(max(max(dot(t,t), dot(b,b)), 1e-12));
    return normalize(base * detail.z + (t * detail.x - b * detail.y) * inv);
}

varying vec4 inst;
varying vec3 world_pos;
varying vec3 obj_pos;

void vertex() {
    float clock = review_time >= 0.0 ? review_time : TIME;
    inst = INSTANCE_CUSTOM;
    obj_pos = VERTEX;
    float h = max(VERTEX.y, 0.0);
    // Migrated blades encode base-to-tip position in UV.x. Keep attachments fixed and
    // bend the distal blade with smooth, seed-stable variation rather than vertex noise.
    if (deform_leaf_tips && UV2.y > 0.5) {
        float tip = smoothstep(0.08, 0.92, UV.x);
        float above_base = smoothstep(0.0, 0.12, h);
        float weight = tip * tip * above_base;
        float bend_x = sin(inst.w * 31.416 + 0.8);
        float bend_z = cos(inst.w * 24.973 + 1.7);
        float spread = 1.0 + (inst.w - 0.5) * 0.12 * weight;
        VERTEX.xz *= vec2(spread, 2.0 - spread);
        VERTEX.xz += vec2(bend_x, bend_z) * weight * 0.028;
    }
    float phase = clock * 1.3 + inst.w * 6.2831;
    float amount = (0.012 * sway + inst.z * 0.15) / stiffness;
    VERTEX.x += sin(phase) * h * h * amount * 3.0 + sin(clock * 18.0 + inst.w * 40.0) * inst.z * h * 0.2;
    VERTEX.z += cos(phase * 0.8) * h * h * amount * 2.0;
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

float lum(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

void fragment() {
    vec3 c = srgb_to_linear(COLOR.rgb) * (1.0 + (inst.x - 0.5) * 0.3);
    vec3 wn = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
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
        rough = UV2.x > 0.5 ? 0.93 : mix(0.68, 0.83, flesh);
    } else if (surface_mode == 4) {
        // slime mold: glossy, translucent yellow protoplasm with a slow streaming pulse along the veins
        float pulse = 0.5 + 0.5 * sin(TIME * 1.6 - length(obj_pos.xz) * 9.0 + inst.w * 20.0);
        c *= 0.85 + 0.3 * pulse;
        rough = 0.3;
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
            n = surface_normal(wn, world_pos, buv, normalize(vec3(bn.xy * 0.9, bn.z)));
            rough = clamp(texture(bark_rgh, buv).r + bark_weathering * 0.07, 0.7, 0.98);
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
        } else if (UV2.y > 0.5) {
            float along = UV.x, across = UV.y - 0.5;
            float face = single_leaf_sheets ? float(!FRONT_FACING) : step(0.5, UV2.x);
            vec3 pigment = mix(leaf_top, leaf_back, face);
            float cells = vnoise(vec2(along * 24.0, across * 31.0) + inst.w * 13.0);
            float micro = vnoise(vec2(along * 91.0, across * 105.0) + inst.w * 27.0);
            float mid = 1.0 - smoothstep(0.006, 0.026 + fwidth(across), abs(across));
            float pair = abs(fract(along * mix(6.0, 10.0, float(leaf_type == 2)) + inst.w * 0.13) - 0.5);
            float branch = 1.0 - smoothstep(0.01, 0.055 + fwidth(pair), abs(pair - abs(across) * 0.46));
            branch *= smoothstep(0.48, 0.30, abs(across));
            float venation = (mid * 0.65 + branch * 0.35) * vein_strength;
            float edge = smoothstep(0.36, 0.50, abs(across));
            float age = smoothstep(0.73, 1.0, along) * (0.25 + 0.75 * vnoise(vec2(inst.w * 9.0, along * 5.0)));
            c = mix(c, pigment, 0.77);
            float leaf_age = fract(inst.w * 7.31 + floor(along * 0.0) + UV2.x * 0.37 + inst.x * 3.7);
            c = mix(c * vec3(0.78, 0.82, 0.80), c * vec3(1.45, 1.38, 0.82), smoothstep(0.25, 0.95, leaf_age));
            c *= (0.91 + 0.12 * cells + 0.035 * micro + 0.08 * venation) * (1.0 - edge * 0.11);
            c = mix(c, c * vec3(1.11, 1.04, 0.78), age * 0.16);
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
            }
            float ridge = venation * (leaf_type == 1 || leaf_type == 4 ? 0.11 : 0.32);
            if (leaf_pattern == 3) ridge = venation * 0.50;
            else if (leaf_pattern == 4) ridge = venation * 0.06;
            else if (leaf_pattern == 5) ridge = venation * 0.08 + (stripe - 0.5) * 0.10;
            else if (leaf_pattern == 6) ridge = venation * 0.24 + branch * 0.10;
            vec3 leaf_n = normalize(vec3(-dFdx(ridge) * 0.8, -dFdy(ridge) * 0.8, 1.0));
            n = surface_normal(wn, world_pos, UV, leaf_n);
            float family_rough = leaf_type == 1 ? 0.78 : leaf_type == 2 ? 0.74 : leaf_type == 3 ? 0.52 : leaf_type == 4 ? 0.46 : leaf_type == 5 ? 0.68 : 0.61;
            rough = clamp(family_rough - 0.12 - cuticle_sheen * 0.22 + (cells - 0.5) * 0.13 + edge * 0.05 + face * 0.10, 0.24, 0.9);
            if (leaf_pattern == 3) rough = clamp(rough - 0.18 * (1.0 - face), 0.28, 0.82);
            else if (leaf_pattern == 4) rough = clamp(rough - 0.09 + (cells - 0.5) * 0.08, 0.34, 0.82);
            else if (leaf_pattern == 5) rough = clamp(rough + 0.10, 0.58, 0.96);
            if (leaf_scan_blend > 0.0) {
                vec2 atlas_axis = leaf_scan_swap ? vec2(UV.y, UV.x) : UV;
                vec2 scan_uv = leaf_scan_rect.xy + atlas_axis * leaf_scan_rect.zw;
                vec3 scan_col = texture(leaf_scan_col, scan_uv).rgb * leaf_scan_gain;
                // The back has its own tissue pigment; carry over photographed
                // fine variation without painting a green top onto the underside.
                float scan_value = clamp(lum(scan_col) / 0.12, 0.65, 1.35);
                vec3 scanned = mix(scan_col, leaf_back * scan_value, face);
                c = mix(c, scanned, leaf_scan_blend);
                if (leaf_pattern == 4)
                    c = mix(c, srgb_to_linear(COLOR.rgb), smoothstep(0.68, 0.96, along) * 0.72);
                vec3 scan_n = unpack_normal(texture(leaf_scan_nrm, scan_uv).rgb);
                n = surface_normal(wn, world_pos, scan_uv,
                    normalize(vec3(scan_n.xy * leaf_scan_normal, scan_n.z)));
                rough = clamp(mix(rough, texture(leaf_scan_rgh, scan_uv).r,
                    leaf_scan_blend * 0.8) + face * 0.08, 0.30, 0.98);
            }
        } else if (UV2.x > 0.5) {
            // Glossy fruit / berries
            rough = 0.22;
        } else {
            // Petioles and young stems stay opaque and waxy, without leaf veins.
            rough = leaf_type == 4 ? 0.52 : leaf_type == 5 ? 0.66 : 0.72;
        }
    }
    // Stress fades to a luminance-matched olive-brown; only genuinely failing tissue reads as straw.
    float health = smoothstep(0.0, 0.45, clamp(inst.y, 0.0, 1.0));
    vec3 faded = vec3(dot(c, vec3(0.33))) * vec3(1.25, 1.05, 0.55);
    ALBEDO = mix(faded, c, 0.30 + 0.70 * health);
    ROUGHNESS = rough;
    bool woody_surface = surface_mode == 2 && UV2.x > 1.5;
    SPECULAR = surface_mode == 2 ? (woody_surface ? 0.22 : mix(0.45, 0.65, cuticle_sheen)) : surface_mode == 4 ? 0.52 : 0.24;
    BACKLIGHT = surface_mode == 2 ? (woody_surface ? vec3(0.0) : (UV2.y > 0.5 ? c * vec3(1.30, 1.35, 0.60) * leaf_transmission * 1.4 : c * 0.025)) : c * (surface_mode == 3 ? 0.12 : 0.07);
    NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
}
