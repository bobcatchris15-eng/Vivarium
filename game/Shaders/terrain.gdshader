// Island top surface from photo-scanned ground materials (CC0, ambientCG): soil, damp forest earth, moss,
// leaf litter and gravel. Live habitat data arrives as a small field texture (R = moisture,
// G = substrate code / 4, B = nutrients, A = light) sampled by world XZ, and decides where each material shows.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

#include "res://Shaders/surface.gdshaderinc"
#include "res://Shaders/water_caustics.gdshaderinc"

uniform sampler2D field_tex : filter_linear, repeat_disable;
uniform sampler2D sub_tex : filter_linear, repeat_disable;
uniform sampler2D litter_tex : filter_linear, repeat_disable;
uniform sampler2D ambient_tex : filter_linear, repeat_disable;
uniform sampler2D cover_tex : filter_linear, repeat_disable;   // moss/lichen colony occupancy (IslandRenderer)
uniform vec4 field_rect;            // origin.x, origin.z, size.x, size.z
uniform float water_table = 0.0;
uniform float review_time = -1.0;
uniform int overlay_mode = 0;       // 0 none, 1 moisture, 2 nutrients, 3 light, 4 substrate
uniform float overlay_strength = 0.85;

uniform sampler2D soil_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_hgt : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_ao : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;

uniform sampler2D damp_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D damp_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D damp_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D damp_hgt : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D damp_ao : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;

uniform sampler2D moss_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_hgt : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_ao : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;

uniform sampler2D leaf_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D leaf_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D leaf_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D leaf_hgt : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D leaf_ao : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;

uniform sampler2D gravel_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_hgt : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_ao : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;

// Integer lattice keys give each neighboring noise cell exactly the same corner
// values. Sine/fract hashes can reassociate dot products across corners on the GPU,
// leaving straight material seams even though the interpolation itself is smooth.
float terrain_hash(vec2 p) {
    uvec2 q = uvec2(ivec2(p));
    uint n = (q.x * 1597334677u) ^ (q.y * 3812015801u);
    n = (n ^ (n >> 16u)) * 2246822519u;
    n = n ^ (n >> 13u);
    return float(n & 16777215u) / 16777215.0;
}
float terrain_noise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(terrain_hash(i), terrain_hash(i + vec2(1, 0)), u.x),
        mix(terrain_hash(i + vec2(0, 1)), terrain_hash(i + vec2(1, 1)), u.x), u.y);
}
uniform float tile_metres = 0.82;    // physical scale of Ground048 in the integrated loam
uniform float soil_relief_amp = 0.012;   // retained control; aggregate detail remains shading-only
uniform float soil_relief_freq = 26.0;   // joined organic aggregate frequency in world metres
uniform float soil_clumps = 1.0;        // 0 skips aggregate normals on low quality tiers
uniform float gravel_normal_strength : hint_range(0.0, 3.0, 0.05) = 1.65; // scanned pebble relief

varying vec3 world_pos;
varying vec3 v_ts;
varying vec3 l_ts;
varying float material_specular;

vec3 ramp(float t) {
    t = clamp(t, 0.0, 1.0);
    return mix(mix(vec3(0.15, 0.2, 0.75), vec3(0.1, 0.8, 0.5), smoothstep(0.0, 0.5, t)), vec3(1.0, 0.85, 0.1), smoothstep(0.5, 1.0, t));
}

// Height-aware blend: the brighter (higher) texels of the incoming layer win first, giving crisp natural edges.
float hblend(float w, float height) { return clamp((w * 1.6 - 0.3) + (height - 0.5) * 0.9 * (1.0 - abs(w * 2.0 - 1.0)), 0.0, 1.0); }

// C1-smooth lookup of a coarse, linearly filtered grid (hides the 25 cm field cells)
vec4 smooth_field(sampler2D tex, vec2 uv) {
    vec2 res = vec2(textureSize(tex, 0));
    vec2 p = uv * res + 0.5;
    vec2 i = floor(p), f = fract(p);
    f = f * f * f * (f * (f * 6.0 - 15.0) + 10.0);
    return texture(tex, (i + f - 0.5) / res);
}

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    
    vec3 view_dir_view = normalize(-VERTEX);
    v_ts = vec3(dot(view_dir_view, TANGENT), dot(view_dir_view, BINORMAL), dot(view_dir_view, NORMAL));
    
    vec3 sun_world = normalize(vec3(0.35, 0.75, 0.45));
    vec3 light_dir_view = normalize((VIEW_MATRIX * vec4(sun_world, 0.0)).xyz);
    l_ts = vec3(dot(light_dir_view, TANGENT), dot(light_dir_view, BINORMAL), dot(light_dir_view, NORMAL));
}

void fragment() {
    vec2 fuv = (world_pos.xz - field_rect.xy) / field_rect.zw;
    vec4 f = smooth_field(field_tex, fuv);
    // Warp the visual sampling of the authoritative cell mask in metres.
    vec2 gravel_warp = vec2(
        vnoise(world_pos.xz * 2.1 + vec2(17.1, 3.4)),
        vnoise(world_pos.xz * 2.1 + vec2(4.3, 29.7))
    ) * 2.0 - 1.0;
    vec2 gravel_uv = (world_pos.xz + gravel_warp * 0.13 - field_rect.xy) / field_rect.zw;
    vec4 sub_smooth = smooth_field(sub_tex, fuv);
    vec4 sub = vec4(texture(sub_tex, gravel_uv).r, sub_smooth.g, sub_smooth.b, sub_smooth.a);
    float contact_mask = sub.a;

    // Shoreline & standing water depth
    float surf_y = (sub.b > 0.0) ? (water_table + (sub.b * 255.0 - 1.0) / 100.0) : water_table;
    float water_surface = (sub.b > 0.0) ? max(water_table, surf_y) : water_table;
    float water_depth = max(0.0, water_surface - world_pos.y);

    // Groundwater submergence is strictly clamped to world_pos.y < water_table
    float gw_sub = (world_pos.y < water_table) ? sub.g : 0.0;
    float silt_edge = (vnoise(world_pos.xz * 4.7 + vec2(21.3, 5.9)) - 0.5) * 0.34;
    float wSilt = (gw_sub > 0.0) ? smoothstep(0.10, 0.74, gw_sub + silt_edge) : 0.0;
    wSilt *= smoothstep(0.002, 0.030, water_depth);
    f.g = texture(field_tex, fuv).g;              // the substrate code (overlay only) must not be smoothed
    float moisture = f.r, light = f.a;
    float code = floor(f.g * 4.0 + 0.5);         // overlay only: 0 soil, 1 gravel, 2 rock, 3 wood, 4 water

    vec2 drift = (vec2(terrain_noise(world_pos.xz * 0.72 + vec2(6.2, 1.7)),
                       terrain_noise(world_pos.xz * 0.72 + vec2(23.7, 14.2))) - 0.5) * 0.18;
    float tile_scale = max(tile_metres, 0.2);
    vec2 uv = world_pos.xz / tile_scale + drift;
    vec3 wn = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);

    // Slope-adaptive triplanar blending across slopes:
    vec3 tri_blend = pow(abs(wn), vec3(4.0));
    tri_blend /= max(tri_blend.x + tri_blend.y + tri_blend.z, 0.0001);
    vec2 uv_x = vec2(world_pos.z, world_pos.y) / tile_scale + drift * 0.36;
    vec2 uv_z = vec2(world_pos.x, world_pos.y) / tile_scale + drift * 0.36;

    float dampness = clamp(mix(0.65, moisture, 0.28), 0.0, 1.0);
    float stored_litter = smooth_field(litter_tex, fuv).r;
    float ambient_cover = smooth_field(ambient_tex, fuv).r;
    float colony = smooth_field(cover_tex, fuv).r;

    // Litter collects preferentially in sheltered contact zones (root collars, logs, rocks)
    float wLeaf = (stored_litter * (1.0 + contact_mask * 0.45)) * smoothstep(0.31, 0.64, fbm(world_pos.xz * 0.83 + 7.2));
    float wMoss = smoothstep(0.04, 0.45, colony) * mix(0.82, 1.0, smoothstep(0.18, 0.78, moisture)) * (1.0 - wSilt);
    float gravel_edge = (vnoise(world_pos.xz * 5.3 + vec2(8.7, 13.2)) - 0.5) * 0.16;
    float wGrav = smoothstep(0.16, 0.84, sub.r + gravel_edge);

    // Keep soil relief separate from gravel. Gravel uses world-space projections
    // below: a single top-down POM offset cannot follow those projection frames,
    // and its old height frequency did not match the visible stones (2.4x).
    float pom_shadow = 1.0;
    float pom_height_scale = 0.025;
    vec2 displaced_uv = uv;
    if (wGrav < 0.95) {
        float soil_shadow = 1.0;
        vec2 soil_uv = pom_raymarch(soil_hgt, uv, v_ts, pom_height_scale, 24, soil_shadow, l_ts);
        displaced_uv = mix(soil_uv, uv, wGrav);
        pom_shadow = mix(soil_shadow, 1.0, wGrav);
    }

    vec3 tn;
    vec4 soil = sample_untiled(soil_col, soil_nrm, soil_rgh, displaced_uv, tn);
    float soil_ao_val = texture(soil_ao, displaced_uv).r;

    // Blend soil into damp (Ground037) as moisture increases (0.35 to 0.78)
    float damp_blend = smoothstep(0.35, 0.78, moisture);
    if (damp_blend > 0.01) {
        vec3 tn_damp;
        vec4 damp = sample_untiled(damp_col, damp_nrm, damp_rgh, displaced_uv, tn_damp);
        float damp_ao_val = texture(damp_ao, displaced_uv).r;
        soil = mix(soil, damp, damp_blend);
        tn = normalize(mix(tn, tn_damp, damp_blend));
        soil_ao_val = mix(soil_ao_val, damp_ao_val, damp_blend);
    }

    // Blend leaf (ScatteredLeaves007) based on stored litter / organic horizon
    float leaf_h = texture(leaf_hgt, displaced_uv * 1.5).r;
    float w_leaf_h = hblend(wLeaf, leaf_h);
    if (w_leaf_h > 0.01) {
        vec2 lu = displaced_uv * 1.5;
        vec4 leaf_samp = vec4(texture(leaf_col, lu).rgb, texture(leaf_rgh, lu).r);
        vec3 leaf_n = unpack_normal(texture(leaf_nrm, lu).rgb);
        float leaf_ao_val = texture(leaf_ao, lu).r;
        soil = mix(soil, leaf_samp, w_leaf_h * 0.82);
        tn = normalize(mix(tn, leaf_n, w_leaf_h * 0.82));
        soil_ao_val = mix(soil_ao_val, leaf_ao_val, w_leaf_h * 0.82);
    }

    vec3 world_pert = vec3(tn.x, 0.0, -tn.y);

    if (tri_blend.y < 0.95) {
        vec3 tn_x, tn_z;
        vec4 soil_x = sample_untiled(soil_col, soil_nrm, soil_rgh, uv_x, tn_x);
        vec4 soil_z = sample_untiled(soil_col, soil_nrm, soil_rgh, uv_z, tn_z);
        soil = soil * tri_blend.y + soil_x * tri_blend.x + soil_z * tri_blend.z;
        vec3 pert_x = vec3(0.0, tn_x.y, -tn_x.x);
        vec3 pert_z = vec3(-tn_z.x, tn_z.y, 0.0);
        world_pert = world_pert * tri_blend.y + pert_x * tri_blend.x + pert_z * tri_blend.z;
    }

    // Unclamped albedo and roughness: full scan contrast and roughness variance
    vec3 col = soil.rgb;
    float rough = soil.a;

    // Joined, shallow aggregate height produces a continuous organic normal field.
    float aggregate = terrain_noise(world_pos.xz * soil_relief_freq + vec2(17.0, 31.0));
    float shoulder = terrain_noise(world_pos.xz * (soil_relief_freq * 1.83) + vec2(12.4, 9.7));
    float pore = terrain_noise(world_pos.xz * (soil_relief_freq * 0.83) + vec2(41.0, 7.0));
    float aggregate_height = clamp(soil_relief_amp * 0.14, 0.0015, 0.0025)
        * ((smoothstep(0.36, 0.72, aggregate) - 0.43) * 0.72
            + (shoulder - 0.5) * 0.15 - smoothstep(0.70, 0.91, pore) * 0.14);
    vec3 aggregate_normal = wn;
    if (soil_clumps > 0.5) {
        vec3 px = dFdx(world_pos), py = dFdy(world_pos);
        float hx = dFdx(aggregate_height), hy = dFdy(aggregate_height);
        float det = px.x * py.z - px.z * py.x;
        float safe_det = abs(det) < 1e-10 ? (det < 0.0 ? -1e-10 : 1e-10) : det;
        float gradient_x = (hx * py.z - hy * px.z) / safe_det;
        float gradient_z = (hy * px.x - hx * py.x) / safe_det;
        aggregate_normal = normalize(wn - vec3(gradient_x, 0.0, gradient_z));
    }

    if (wMoss > 0.01) {
        vec2 mu = displaced_uv * 2.4 + 7.3;
        vec3 mn = unpack_normal(texture(moss_nrm, mu).rgb);
        vec4 m = vec4(texture(moss_col, mu).rgb, texture(moss_rgh, mu).r);
        vec3 natural_moss = mix(m.rgb, m.rgb * vec3(0.74, 0.88, 0.58), 0.5);
        float h = dot(m.rgb, vec3(0.333)) * 1.2;
        float w = hblend(wMoss, h);
        vec3 rhizoid_col = mix(col * 0.78, natural_moss, smoothstep(0.08, 0.45, w));
        col = mix(col, rhizoid_col, w * 0.75);
        rough = mix(rough, m.a, w * 0.7);
        world_pert = mix(world_pert, vec3(mn.x, 0.0, -mn.y), w);
    }
    if (wGrav > 0.01) {
        // One physical frequency for every channel and axis. Remove projections
        // nearly edge-on to the surface so elongated top texels cannot bleed down
        // a steep bank. No top-down warp or camera-dependent displacement.
        vec3 gp = world_pos * (2.4 / tile_scale);
        vec3 gw = pow(max(abs(wn) - vec3(0.2), vec3(0.0)), vec3(4.0));
        gw /= max(gw.x + gw.y + gw.z, 0.0001);
        vec4 g = vec4(0.0);
        vec3 gravel_normal = vec3(0.0);
        if (gw.x > 0.0) {
            g += vec4(texture(gravel_col, gp.zy).rgb, texture(gravel_rgh, gp.zy).r) * gw.x;
            gravel_normal += cotangent_normal(wn, world_pos, gp.zy,
                unpack_normal(texture(gravel_nrm, gp.zy).rgb)) * gw.x;
        }
        if (gw.y > 0.0) {
            g += vec4(texture(gravel_col, gp.xz).rgb, texture(gravel_rgh, gp.xz).r) * gw.y;
            gravel_normal += cotangent_normal(wn, world_pos, gp.xz,
                unpack_normal(texture(gravel_nrm, gp.xz).rgb)) * gw.y;
        }
        if (gw.z > 0.0) {
            g += vec4(texture(gravel_col, gp.xy).rgb, texture(gravel_rgh, gp.xy).r) * gw.z;
            gravel_normal += cotangent_normal(wn, world_pos, gp.xy,
                unpack_normal(texture(gravel_nrm, gp.xy).rgb)) * gw.z;
        }
        gravel_normal = finite_surface_normal(gravel_normal, wn);
        // Strengthen the matched pebble normal without shifting its texture
        // coordinates or adding camera-dependent parallax. This is a surface
        // gradient, so scaling it preserves the geometric normal on flat maps.
        vec3 gravel_pert = (gravel_normal / max(dot(gravel_normal, wn), 0.2) - wn)
            * gravel_normal_strength;
        g.rgb = mix(vec3(dot(g.rgb, vec3(0.3, 0.59, 0.11))), g.rgb, 0.45) * vec3(1.03, 1.0, 0.95);
        float stone_h = dot(g.rgb, vec3(0.33)) * 1.4;
        float soil_h = dot(soil.rgb, vec3(0.33));
        float w = clamp(((wGrav * 1.4 + stone_h * 0.35 - soil_h * 0.25) - 0.15) / 0.70, 0.0, 1.0);
        col = mix(col, g.rgb, w); rough = mix(rough, g.a, w);
        world_pert = mix(world_pert, gravel_pert, w);
    }
    if (wSilt > 0.01) {
        // Riparian alluvial silt: fine-grained mineral sediment with gentle flow micro-ripples
        float silt_grain = fbm(world_pos.xz * 5.3 + vec2(5.1, 31.7));
        float silt_ripple = sin(world_pos.x * 24.0 + world_pos.z * 12.0 + silt_grain * 2.5) * 0.5 + 0.5;

        vec2 su = displaced_uv * 2.0;
        vec3 damp_silt = texture(damp_col, su).rgb * vec3(0.88, 0.85, 0.80);
        vec3 gravel_grain = texture(gravel_col, su * 3.2).rgb;
        float grain_mix = smoothstep(0.60, 0.85, fbm(world_pos.xz * 14.0));
        vec3 silt_bank = mix(damp_silt, gravel_grain, grain_mix * 0.30) * mix(0.9, 1.1, silt_grain);
        vec3 silt_mud = silt_bank * 0.65;
        vec3 silt_col = mix(silt_bank, silt_mud, smoothstep(0.0, 0.02, water_depth));

        float silt_rough = mix(0.72, 0.22, smoothstep(0.0, 0.004, water_depth));
        silt_rough = mix(silt_rough, 0.78, smoothstep(0.006, 0.025, water_depth));

        col = mix(col, silt_col, wSilt);
        rough = mix(rough, silt_rough, wSilt);

        vec3 ripple_pert = vec3(cos(world_pos.x * 24.0) * 0.06, 0.0, sin(world_pos.z * 12.0) * 0.06);
        world_pert = mix(world_pert, ripple_pert, wSilt * 0.85);
    }

    vec3 detail = normalize(aggregate_normal + world_pert * 0.9);

    float ambient_micro = 0.76 + 0.24 * fbm(world_pos.xz * 7.5 + vec2(19.1, 4.7));
    float ambient_weight = smoothstep(0.035, 0.72, ambient_cover) * 0.72 * ambient_micro
        * (1.0 - wGrav) * (1.0 - wLeaf * 0.55) * (1.0 - wSilt);
    vec3 ambient_dry = srgb_to_linear(vec3(0.22, 0.35, 0.09));
    vec3 ambient_mesic = srgb_to_linear(vec3(0.075, 0.29, 0.065));
    vec3 ambient_shade = srgb_to_linear(vec3(0.045, 0.205, 0.105));
    vec3 ambient_col = mix(ambient_dry, ambient_mesic, smoothstep(0.12, 0.58, moisture));
    ambient_col = mix(ambient_col, ambient_shade, smoothstep(0.62, 0.18, light));
    ambient_col = mix(soil.rgb * vec3(0.72, 0.69, 0.55), ambient_col, 0.32);
    ambient_col *= mix(0.74, 1.05, ambient_micro);
    col = mix(col, ambient_col, ambient_weight);
    rough = mix(rough, 0.9, ambient_weight * 0.45);

    col = mix(col, col * 1.06, smoothstep(0.42, 0.18, moisture) * 0.35);
    float silt_clear = 1.0 - wSilt;
    col *= mix(1.0, 0.90, smoothstep(0.3, 0.75, moisture) * (1.0 - wMoss * 0.5) * silt_clear);
    float soil_wet = smoothstep(0.7, 1.0, moisture) * silt_clear;

    float gw_shore_wet = (sub.g > 0.0 && world_pos.y < water_table + 0.02) ? smoothstep(0.001, 0.02, water_table + 0.02 - world_pos.y) : 0.0;
    float surf_shore_wet = (sub.b > 0.0 && world_pos.y < surf_y + 0.02) ? smoothstep(0.001, 0.02, surf_y + 0.02 - world_pos.y) : 0.0;
    float shore_wet = max(gw_shore_wet, surf_shore_wet) * silt_clear;
    float total_wet = clamp(max(soil_wet, shore_wet), 0.0, 1.0);

    // Lekner-Dorf internal reflection darkening on albedo when saturated with water
    if (total_wet > 0.05) {
        col = pow(col, vec3(mix(1.0, 1.42, total_wet))) * mix(1.0, 0.58, total_wet);
    }
    CLEARCOAT = smoothstep(0.45, 0.92, total_wet) * 0.88;
    CLEARCOAT_ROUGHNESS = mix(0.28, 0.04, smoothstep(0.55, 0.98, total_wet));

    col *= mix(1.0, 0.88, total_wet * (1.0 - wMoss * 0.6));
    float wet_pores = vnoise(world_pos.xz * 36.0 + vec2(11.7, 3.1));
    rough = mix(rough, mix(0.52, 0.66, wet_pores), total_wet * 0.65);
    float submerged_bed = smoothstep(0.004, 0.018, water_depth);
    rough = mix(rough, max(rough, 0.78), submerged_bed);
    detail = normalize(mix(detail, wn, total_wet * 0.12));

    EMISSION = vec3(0.0);

    if (contact_mask > 0.01) {
        vec3 humus_enrich = vec3(0.88, 0.85, 0.80);
        col = mix(col, col * humus_enrich, contact_mask * 0.35);
        rough = mix(rough, 0.76, contact_mask * 0.28);
    }

    if (overlay_mode == 1) col = mix(col, ramp(moisture), overlay_strength);
    else if (overlay_mode == 2) col = mix(col, ramp(f.b), overlay_strength);
    else if (overlay_mode == 3) col = mix(col, ramp(light), overlay_strength);
    else if (overlay_mode == 4) col = mix(col, vec3(code == 0.0 ? 0.55 : 0.0, code == 1.0 ? 0.8 : (code == 0.0 ? 0.35 : 0.0), code >= 2.0 ? 0.3 + 0.2 * code : 0.1), overlay_strength);

    float base_ao = min(soil_ao_val, mix(0.82, 1.0, smoothstep(0.12, 0.6, dot(soil.rgb, vec3(0.333)))));
    float contact_ao = mix(1.0, 0.58, contact_mask);
    AO = min(min(base_ao, contact_ao), pom_shadow);
    AO_LIGHT_AFFECT = 0.35;
    col *= mix(0.75, 1.0, pom_shadow);

    ALBEDO = col;
    ROUGHNESS = rough;
    SPECULAR = mix(0.35, 0.32, total_wet);
    SPECULAR = mix(SPECULAR, 0.12, smoothstep(0.006, 0.025, water_depth));
    NORMAL = normalize((VIEW_MATRIX * vec4(detail, 0.0)).xyz);
    material_specular = SPECULAR;
}

void light() {
    vec3 wn = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
    vec3 wl = normalize((INV_VIEW_MATRIX * vec4(LIGHT, 0.0)).xyz);
    vec3 vv = normalize(VIEW);
    float t = (review_time >= 0.0) ? review_time : TIME;

    float ndotl = max(dot(wn, wl), 0.0);
    DIFFUSE_LIGHT += LIGHT_COLOR * ATTENUATION * ndotl * (1.0 / PI);

    if (ndotl > 0.0) {
        vec3 h = normalize(wl + vv);
        float ndoth = max(dot(wn, h), 0.0);
        float ndotv = max(dot(wn, vv), 1e-4);
        float a = max(ROUGHNESS * ROUGHNESS, 0.001);
        float a2 = a * a;
        float denom = ndoth * ndoth * (a2 - 1.0) + 1.0;
        float D = a2 / max(PI * denom * denom, 1e-4);
        float F0 = material_specular * 0.08;
        float vh = max(dot(vv, h), 0.0);
        float F = F0 + (1.0 - F0) * pow(1.0 - vh, 5.0);
        float k = (ROUGHNESS + 1.0) * (ROUGHNESS + 1.0) * 0.125;
        float G = (ndotv / max(ndotv * (1.0 - k) + k, 1e-4)) *
                 (ndotl / max(ndotl * (1.0 - k) + k, 1e-4));
        float spec = D * G * F / max(4.0 * ndotv * ndotl, 1e-4);
        SPECULAR_LIGHT += LIGHT_COLOR * ATTENUATION * spec * ndotl;
    }

    vec2 fuv = (world_pos.xz - field_rect.xy) / field_rect.zw;
    vec4 sub = texture(sub_tex, fuv);
    float water_surface_y = max(water_table, world_pos.y + sub.b * 0.25);
    vec3 caustic = evaluate_submerged_caustics(world_pos, wn, water_surface_y, wl, t);
    DIFFUSE_LIGHT += caustic * ATTENUATION * LIGHT_COLOR;
}
