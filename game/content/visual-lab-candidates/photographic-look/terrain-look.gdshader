// Island top surface from photo-scanned ground materials (CC0, ambientCG): soil, damp forest earth, moss,
// leaf litter and gravel. Live habitat data arrives as a small field texture (R = moisture,
// G = substrate code / 4, B = nutrients, A = light) sampled by world XZ, and decides where each material shows.
shader_type spatial;
uniform float photo_look_moss_scale = 1.0;
uniform float photo_look_ground_moss_scale = 1.0;
uniform vec3 photo_look_moss_tint = vec3(1.0);
uniform vec3 photo_look_damp_tint = vec3(1.0);
uniform float photo_look_wet_film = 0.0;
uniform float photo_look_wet_roughness = 0.28;

render_mode diffuse_burley, specular_schlick_ggx;

#include "res://Shaders/surface.gdshaderinc"

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
uniform sampler2D damp_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D damp_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D damp_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D leaf_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D leaf_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D leaf_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
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
float terrain_fbm(vec2 p) {
    float s = 0.0, a = 0.5;
    for (int i = 0; i < 4; i++) { s += a * terrain_noise(p); p *= 2.03; a *= 0.5; }
    return s;
}

uniform float tile_metres = 0.56;    // physical scale of the main soil scan
uniform float soil_relief_amp = 0.012;   // metres of fine geometric relief on soil (lumps, furrows)
uniform float soil_relief_freq = 2.6;
uniform float soil_clumps = 1.0;        // 0 skips the clump/cavity detail (low quality tiers)

varying vec3 world_pos;
varying vec3 relief_wn;      // world-space normal including the fine relief bump
varying float relief_mix;    // how much of the mesh normal to replace with relief_wn (0 off soil)
varying float relief_h;      // relief height (-0.5..0.5) for cavity shading

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

// Dual animated wave interference network for submerged bed caustics
float caustic_pattern(vec2 pos, float t) {
    vec2 p1 = pos * 14.0 + vec2(t * 0.70, t * 0.45);
    p1 += vec2(sin(p1.y * 1.35 + t * 0.85), cos(p1.x * 1.35 - t * 0.75)) * 0.45;
    float w1 = sin(p1.x * 2.2 + p1.y * 1.2);
    float w2 = cos(p1.y * 2.2 - p1.x * 1.1);
    float c1 = pow(clamp(1.0 - abs(w1 * w2), 0.0, 1.0), 3.5);

    vec2 p2 = vec2(pos.x * 0.707 - pos.y * 0.707, pos.x * 0.707 + pos.y * 0.707) * 17.0 + vec2(-t * 0.60, t * 0.65);
    p2 += vec2(cos(p2.y * 1.25 - t * 0.80), sin(p2.x * 1.25 + t * 0.90)) * 0.45;
    float w3 = sin(p2.x * 2.0 - p2.y * 1.3);
    float w4 = cos(p2.y * 2.1 + p2.x * 1.2);
    float c2 = pow(clamp(1.0 - abs(w3 * w4), 0.0, 1.0), 3.5);

    return clamp((c1 + c2) * 0.45 + (c1 * c2) * 1.4, 0.0, 2.0);
}

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;

    // Microrelief belongs in shading. The visible bed must stay on the authoritative terrain
    // triangles: decorative displacement can bury a shallow stream or conceal a small eroded channel.
    vec2 fuv = (world_pos.xz - field_rect.xy) / field_rect.zw;
    vec4 sub_samp = texture(sub_tex, fuv);
    float rock_weight = sub_samp.r;
    float contact = sub_samp.a;
    float soilMask = clamp(1.0 - rock_weight, 0.0, 1.0);
    float eps = 0.3;
    float h0 = mix(vnoise(world_pos.xz * soil_relief_freq), vnoise(world_pos.xz * soil_relief_freq * 2.03 + 4.7), 0.28) - 0.5;
    float h1 = mix(vnoise((world_pos.xz + vec2(eps, 0.0)) * soil_relief_freq), vnoise((world_pos.xz + vec2(eps, 0.0)) * soil_relief_freq * 2.03 + 4.7), 0.28) - 0.5;
    float h2 = mix(vnoise((world_pos.xz + vec2(0.0, eps)) * soil_relief_freq), vnoise((world_pos.xz + vec2(0.0, eps)) * soil_relief_freq * 2.03 + 4.7), 0.28) - 0.5;
    float amp = soil_relief_amp * soilMask;

    vec3 grad = vec3(eps, (h1 - h0) * amp, 0.0);
    vec3 grad2 = vec3(0.0, (h2 - h0) * amp, eps);
    vec3 n2 = normalize(cross(grad2, grad));
    relief_wn = normalize((MODEL_MATRIX * vec4(normalize(NORMAL + vec3(n2.x, 0.0, n2.z) * soilMask), 0.0)).xyz);
    relief_mix = soilMask;
    relief_h = h0;
}

void fragment() {
    vec2 fuv = (world_pos.xz - field_rect.xy) / field_rect.zw;
    vec4 f = smooth_field(field_tex, fuv);
    // Warp the visual sampling of the authoritative cell mask in metres. A straight
    // lookup exposes the square cell lattice around gravel; continuous world-space
    // noise bends its edge without changing any substrate or tool behavior.
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
    // Submerged waterway bed (gw_sub). IslandRenderer writes it from the simulation's standing-water depth, not
    // from moisture, so a merely damp cell keeps its moss and only a real waterway bed turns to silt. The mask
    // arrives C1-smoothed over the 25 cm sub grid and is broken up with the same world-space noise the gravel
    // edge uses, so the shoreline is a soft irregular gradient rather than a step at a cell boundary.
    float silt_edge = (vnoise(world_pos.xz * 4.7 + vec2(21.3, 5.9)) - 0.5) * 0.34;
    float wSilt = (gw_sub > 0.0) ? smoothstep(0.10, 0.74, gw_sub + silt_edge) : 0.0;
    // A shallow waterline retains the bank's substrate. Accumulated alluvial
    // sediment takes over gradually with actual standing depth, not a binary plane cut.
    wSilt *= smoothstep(0.002, 0.030, water_depth);
    f.g = texture(field_tex, fuv).g;              // the substrate code (overlay only) must not be smoothed
    float moisture = f.r, light = f.a;
    float code = floor(f.g * 4.0 + 0.5);         // overlay only: 0 soil, 1 gravel, 2 rock, 3 wood, 4 water
    float macro = fbm(world_pos.xz * 0.35);
    // Smooth world-space distortion keeps the albedo, normal and roughness of every scan registered.
    // The broad field changes scale subtly rather than repeating one 56 cm square across the island.
    vec2 drift = vec2(vnoise(world_pos.xz * 0.72 + vec2(6.2, 1.7)),
                      vnoise(world_pos.xz * 0.72 + vec2(23.7, 14.2))) - 0.5;
    float tile_scale = tile_metres * mix(0.84, 1.24, macro);
    vec2 uv = world_pos.xz / tile_scale + drift * 0.36;
    vec3 wn = normalize(mix(normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz), relief_wn, relief_mix));

    // Slope-adaptive triplanar blending across slopes:
    // Flattish terrain (wn.y close to 1) evaluates top projection; steep banks/cliffs blend side projections.
    vec3 tri_blend = pow(abs(wn), vec3(4.0));
    tri_blend /= max(tri_blend.x + tri_blend.y + tri_blend.z, 0.0001);
    vec2 uv_x = vec2(world_pos.z, world_pos.y) / tile_scale + drift * 0.36;
    vec2 uv_z = vec2(world_pos.x, world_pos.y) / tile_scale + drift * 0.36;

    // weights first, then sample only the layers that actually show here (big saving on an iGPU)
    // Silt is deposited sediment, not soaked forest earth: the damp and moss layers are cut back under water so
    // a saturated channel cannot drift back to wet green. The mask alone decides this — moisture would do it
    // everywhere that is merely damp.
    float wDamp = smoothstep(0.38, 0.82, moisture + (macro - 0.5) * 0.18) * (1.0 - wSilt);
    float stored_litter = smooth_field(litter_tex, fuv).r;
    float ambient_cover = smooth_field(ambient_tex, fuv).r;
    float colony = smooth_field(cover_tex, fuv).r;
    // Litter collects preferentially in sheltered contact zones (root collars, logs, rocks)
    float wLeaf = (stored_litter * (1.0 + contact_mask * 0.45)) * smoothstep(0.31, 0.64, fbm(world_pos.xz * 0.83 + 7.2));
    float wMoss = smoothstep(0.48, 0.83, moisture) * smoothstep(0.20, 0.53, light)
        * smoothstep(0.32, 0.69, terrain_fbm(world_pos.xz * 1.14 + 2.8)) * (1.0 - wSilt)
        * (1.0 - 0.75 * smoothstep(0.04, 0.45, colony));
    float gravel_edge = (vnoise(world_pos.xz * 5.3 + vec2(8.7, 13.2)) - 0.5) * 0.16;
    float wGrav = smoothstep(0.16, 0.84, sub.r + gravel_edge);
    wMoss *= photo_look_moss_scale;

    vec3 tn;
    vec4 soil = sample_untiled(soil_col, soil_nrm, soil_rgh, uv, tn);
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

    // the scan is a warm red-brown: pull it toward a darker umber forest soil (a cool multiplier here reads mauve)
    vec3 col = mix(vec3(dot(soil.rgb, vec3(0.3, 0.59, 0.11))), soil.rgb, 0.7) * vec3(0.84, 0.79, 0.72);
    float rough = soil.a;
    if (soil_clumps > 0.5)
    // topsoil structure that survives distance: crumb clumps and darker hollows between them, as albedo and
    // a mid-scale normal (the texture detail mips away, the mesh relief is too coarse to catch light alone)
    {
        // 2-octave noise, and the slope from single-octave samples: three full 4-octave fbm per pixel of ground
        // was a real cost on the iGPU for detail the eye only reads as broad clumping
        vec2 q = world_pos.xz * 11.0;
        float s1 = vnoise(q), s2 = vnoise(q * 2.03 + 3.7);
        float s0 = s1 * 0.67 + s2 * 0.33;
        float sx = vnoise(q + vec2(0.132, 0.0)) * 0.67 + s2 * 0.33;
        float sz = vnoise(q + vec2(0.0, 0.132)) * 0.67 + s2 * 0.33;
        float clump = smoothstep(0.35, 0.7, s0);
        float cav = clamp(0.5 - relief_h * 1.6, 0.0, 1.0);          // hollows of the displaced relief
        col *= mix(0.90, 1.06, clump) * mix(1.0, 0.92, cav * relief_mix);
        col = mix(col, col * vec3(1.06, 0.98, 0.9), smoothstep(0.55, 0.8, vnoise(world_pos.xz * 3.1 + 5.0)) * 0.6);   // drier crusted patches
        vec3 cn = normalize(vec3(-(sx - s0) * 0.9, 0.012, -(sz - s0) * 0.9));   // world normal of the clump field
        world_pert = mix(world_pert, vec3(cn.x, 0.0, -cn.z), 0.28 * relief_mix);
        rough = mix(rough, 0.95, clump * 0.3);
    }
    if (wDamp > 0.01) {
        vec2 du = uv * 1.15 + 3.1;
        vec3 n2 = unpack_normal(texture(damp_nrm, du).rgb);
        vec4 d = vec4(texture(damp_col, du).rgb, texture(damp_rgh, du).r);
        if (tri_blend.y < 0.95) {
            vec2 du_x = uv_x * 1.15 + 3.1;
            vec2 du_z = uv_z * 1.15 + 3.1;
            vec4 d_x = vec4(texture(damp_col, du_x).rgb, texture(damp_rgh, du_x).r);
            vec4 d_z = vec4(texture(damp_col, du_z).rgb, texture(damp_rgh, du_z).r);
            d = d * tri_blend.y + d_x * tri_blend.x + d_z * tri_blend.z;
            vec3 n2_x = unpack_normal(texture(damp_nrm, du_x).rgb);
            vec3 n2_z = unpack_normal(texture(damp_nrm, du_z).rgb);
            n2 = normalize(n2 * tri_blend.y + n2_x * tri_blend.x + n2_z * tri_blend.z);
        }
        float w = mix(wDamp, hblend(wDamp, dot(d.rgb, vec3(0.33)) * 2.0), 0.35);   // soft: moisture creeps, it has no edge
        d.rgb *= photo_look_damp_tint;
        col = mix(col, d.rgb, w); rough = mix(rough, d.a, w);
        world_pert = mix(world_pert, vec3(n2.x, 0.0, -n2.y), w);
    }
    if (wLeaf > 0.01) {
        // Larger, rotated scan fragments read as fallen leaves rather than a tiny repeated pebble print.
        vec2 lu = uv * 0.72 + 1.7;
        vec3 ln;
        vec4 l = sample_untiled(leaf_col, leaf_nrm, leaf_rgh, lu, ln);
        // ScatteredLeaves007 is isolated litter on black. Black gaps reveal the actual
        // substrate; treating them as opaque albedo printed a black-backed carpet.
        float leaf_presence = smoothstep(0.007, 0.045, dot(l.rgb, vec3(0.333)));
        float pockets = smoothstep(0.25, 0.72, vnoise(world_pos.xz * 2.1 + vec2(19.7, 8.3)));
        float w = hblend(wLeaf * mix(0.32, 1.0, pockets), dot(l.rgb, vec3(0.33)) * 2.0) * leaf_presence;
        // A decomposing forest floor has a muted umber base with occasional intact warm leaves.
        float leaf_luminance = dot(l.rgb, vec3(0.2126, 0.7152, 0.0722));
        l.rgb = mix(vec3(leaf_luminance) * vec3(0.90, 0.82, 0.66), l.rgb, 0.56);
        // Damp litter loses the dry scan's bright orange/yellow response.
        l.rgb *= mix(vec3(1.0), vec3(0.50, 0.58, 0.48), smoothstep(0.24, 0.78, moisture));
        col = mix(col, l.rgb, w); rough = mix(rough, l.a, w);
        world_pert = mix(world_pert, vec3(ln.x, 0.0, -ln.y), w);
    }
    if (wMoss > 0.01) {
        vec2 mu = uv * 2.4 + 7.3;
        vec3 mn = unpack_normal(texture(moss_nrm, mu).rgb);
        vec4 m = vec4(texture(moss_col, mu).rgb, texture(moss_rgh, mu).r);
        // Ground the bright moss texture with earthy forest tones and rhizoid boundary
        vec3 natural_moss = mix(m.rgb, m.rgb * vec3(0.74, 0.88, 0.58), 0.5);
        natural_moss *= photo_look_moss_tint;
        float h = dot(m.rgb, vec3(0.333)) * 1.2;
        float w = hblend(wMoss, h);
        vec3 rhizoid_col = mix(col * 0.78, natural_moss, smoothstep(0.08, 0.45, w));
        col = mix(col, rhizoid_col, w * 0.75);
        rough = mix(rough, m.a, w * 0.7);
        world_pert = mix(world_pert, vec3(mn.x, 0.0, -mn.y), w);
    }
    if (wGrav > 0.01) {
        vec2 gu = uv * 2.4;
        vec3 gn = unpack_normal(texture(gravel_nrm, gu).rgb);
        vec4 g = vec4(texture(gravel_col, gu).rgb, texture(gravel_rgh, gu).r);
        if (tri_blend.y < 0.95) {
            vec2 gu_x = uv_x * 2.4;
            vec2 gu_z = uv_z * 2.4;
            vec4 g_x = vec4(texture(gravel_col, gu_x).rgb, texture(gravel_rgh, gu_x).r);
            vec4 g_z = vec4(texture(gravel_col, gu_z).rgb, texture(gravel_rgh, gu_z).r);
            g = g * tri_blend.y + g_x * tri_blend.x + g_z * tri_blend.z;
            vec3 gn_x = unpack_normal(texture(gravel_nrm, gu_x).rgb);
            vec3 gn_z = unpack_normal(texture(gravel_nrm, gu_z).rgb);
            gn = normalize(gn * tri_blend.y + gn_x * tri_blend.x + gn_z * tri_blend.z);
        }
        g.rgb = mix(vec3(dot(g.rgb, vec3(0.3, 0.59, 0.11))), g.rgb, 0.45) * vec3(1.03, 1.0, 0.95);   // desaturate, warm slightly
        // Height-aware displacement blend for rock/soil transition per Section 6: pebbles emerge from soil, soil in hollows
        float stone_h = dot(g.rgb, vec3(0.33)) * 1.4;
        float soil_h = dot(soil.rgb, vec3(0.33));
        float w = clamp(((wGrav * 1.4 + stone_h * 0.35 - soil_h * 0.25) - 0.15) / 0.70, 0.0, 1.0);
        col = mix(col, g.rgb, w); rough = mix(rough, g.a, w);
        world_pert = mix(world_pert, vec3(gn.x, 0.0, -gn.y), w);
    }
    if (wSilt > 0.01) {
        // Riparian alluvial silt: fine-grained mineral sediment with gentle flow micro-ripples and sleek wet sheen.
        // Distinct from porous crumbly organic damp earth (which has coarse aggregate relief and clods).
        float silt_grain = fbm(world_pos.xz * 5.3 + vec2(5.1, 31.7));
        float silt_ripple = sin(world_pos.x * 24.0 + world_pos.z * 12.0 + silt_grain * 2.5) * 0.5 + 0.5;

        // Pale alluvial silt when dry/shallow bank, darkening to sleek alluvial mud when submerged
        vec3 silt_bank = vec3(0.088, 0.072, 0.054) * mix(0.85, 1.15, silt_grain);
        vec3 silt_mud = vec3(0.048, 0.038, 0.029) * mix(0.82, 1.18, silt_grain + silt_ripple * 0.15);
        vec3 silt_col = mix(silt_bank, silt_mud, smoothstep(0.0, 0.02, water_depth));

        // The air/water interface owns the mirror reflection. Keep a wet sheen
        // on the thin shoreline film, then roughen sediment beneath the water
        // so the bed does not become a second silver reflection surface.
        float silt_rough = mix(0.72, 0.22, smoothstep(0.0, 0.004, water_depth));
        silt_rough = mix(silt_rough, 0.78, smoothstep(0.006, 0.025, water_depth));

        col = mix(col, silt_col, wSilt);
        rough = mix(rough, silt_rough, wSilt);

        // Smooth flow micro-ripple normal (replaces coarse soil crumbs with alluvial ripple)
        vec3 ripple_pert = vec3(cos(world_pos.x * 24.0) * 0.06, 0.0, sin(world_pos.z * 12.0) * 0.06);
        world_pert = mix(world_pert, ripple_pert, wSilt * 0.85);
    }

    // world-space normal: geometry normal perturbed by the blended detail (GL green = -z)
    vec3 detail = normalize(wn + world_pert * 0.9);


    // Anonymous low vegetation reads as a soft green underlayer at distance. Close-up silhouette comes from
    // AmbientGroundCoverRenderer; this prevents the same field from turning back into brown dirt when geometry culls.
    float ambient_micro = 0.76 + 0.24 * fbm(world_pos.xz * 7.5 + vec2(19.1, 4.7));
    float ambient_weight = smoothstep(0.035, 0.72, ambient_cover) * 0.72 * ambient_micro
        * (1.0 - wGrav) * (1.0 - wLeaf * 0.55) * (1.0 - wSilt);
    vec3 ambient_dry = srgb_to_linear(vec3(0.22, 0.35, 0.09));
    vec3 ambient_mesic = srgb_to_linear(vec3(0.075, 0.29, 0.065));
    vec3 ambient_shade = srgb_to_linear(vec3(0.045, 0.205, 0.105));
    vec3 ambient_col = mix(ambient_dry, ambient_mesic, smoothstep(0.12, 0.58, moisture));
    ambient_col = mix(ambient_col, ambient_shade, smoothstep(0.62, 0.18, light));
    // Anonymous creepers grow over humus, not an opaque flat green paint layer.
    // Keep the scanned soil's organic grain beneath sparse moss pigmentation.
    ambient_col = mix(soil.rgb * vec3(0.72, 0.69, 0.55), ambient_col, 0.32);
    ambient_col *= mix(0.74, 1.05, ambient_micro);
    col = mix(col, ambient_col, ambient_weight);
    rough = mix(rough, 0.9, ambient_weight * 0.45);

    // Ground cushion moss on bare gravel/soil: creeps in where it is moist, shaded or already vegetated, and
    // hugs gravel margins; dry exposed gravel stays clear. Same cushion logic as the log moss (surface.gdshaderinc).
    float bare = max(wGrav, (1.0 - wMoss) * (1.0 - ambient_weight) * (1.0 - wLeaf * 0.7));
    float gedge = wGrav * (1.0 - wGrav) * 4.0;
    float favour = moisture * 0.7 + (1.0 - light) * 0.55 + ambient_cover * 0.5 + gedge * 0.6 - 0.40;
    float patchn = terrain_noise(world_pos.xz * 1.7 + vec2(3.3, 9.1)) * 0.65 + terrain_noise(world_pos.xz * 4.9 + 1.2) * 0.35;
    float gm = smoothstep(0.0, 0.35, favour + (patchn - 0.5) * 0.6) * bare * (1.0 - wSilt)
        * (1.0 - smoothstep(0.04, 0.45, colony));   // bare ground around real colonies so they read as distinct patches
    // Decorative moss grows in broken islands. Moisture alone must not paint the
    // entire floor; the explicit colony mask still owns occupied moss/lichen patches.
    gm *= smoothstep(0.28, 0.64, patchn + contact_mask * 0.06);
    gm *= photo_look_ground_moss_scale;
    if (gm > 0.02) {
        float speck;
        vec4 cl = moss_clumps(world_pos.xz * 38.0, speck);
        float cover = clamp(gm + (cl.x - 0.5) * 0.8 * (1.0 - gm * gm), 0.0, 1.0);
        float fib = vnoise(world_pos.xz * vec2(160.0, 60.0));
        // Fine fuzz: two high-frequency octaves (~2-5 mm grain) so a close view reads as countless shoots,
        // not smooth cobbles; it also breaks the cell seams so neighbouring cushions merge.
        float fz1 = vnoise(world_pos.xz * 420.0 + vec2(cl.w * 17.0));
        float fz2 = vnoise(world_pos.xz * 190.0 + 4.1);
        float footprint = max(length(dFdx(world_pos.xz)), length(dFdy(world_pos.xz)));
        float fine_detail = 1.0 - smoothstep(0.0015, 0.006, footprint);
        float fuzz = mix(0.5, fz1 * 0.55 + fz2 * 0.45, fine_detail);
        float h = clamp(cl.x + (fuzz - 0.5) * 0.45, 0.0, 1.0);
        // Artist-facing sRGB greens: dark humid cushions, with chartreuse limited to
        // growing tips. The previous linear constants produced a pale olive wash.
        vec3 base = mix(srgb_to_linear(vec3(0.19, 0.30, 0.09)),
            srgb_to_linear(vec3(0.28, 0.40, 0.12)), cl.w) * mix(0.85, 1.10, patchn);
        vec3 mc = mix(base, srgb_to_linear(vec3(0.48, 0.66, 0.18)),
            smoothstep(0.60, 0.96, h) * (0.25 + 0.35 * fib));
        mc *= mix(0.56, 1.0, smoothstep(0.0, 0.5, h)) * (0.75 + 0.4 * fuzz);
        mc = mix(mc, srgb_to_linear(vec3(0.30, 0.28, 0.12)), speck * 0.22 * fine_detail);
        float a = smoothstep(0.20, 0.68, cover) * smoothstep(0.05, 0.60, gm);
        col = mix(col, mc, a);
        rough = mix(rough, 0.95, a);
        vec2 fg = vec2(vnoise(world_pos.xz * 300.0 + 2.7), vnoise(world_pos.xz * 300.0 + 8.3)) - 0.5;
        vec3 dn = normalize(vec3(cl.y * 0.5 + fg.x * 0.6 * fine_detail, 0.6 * max(cl.x, 0.25), cl.z * 0.5 + fg.y * 0.6 * fine_detail));
        detail = normalize(mix(detail, normalize(wn + vec3(dn.x, 0.0, dn.z) * 0.7), a));
    }

    // moisture ramp: dry ground is paler and dustier, damp ground darkens gradually, saturated ground glosses
    float dryness = smoothstep(0.42, 0.18, moisture);
    col = mix(col, mix(vec3(dot(col, vec3(0.3, 0.59, 0.11))), col, 0.7) * 1.18, dryness);
    // Silt is matte. The bed under real water has already been repainted, so the soak darkening and the wet
    // gloss are faded out there and survive only at the shoreline, where they still darken the wet margin.
    float silt_clear = 1.0 - wSilt;
    col *= mix(1.0, 0.72, smoothstep(0.3, 0.75, moisture) * (1.0 - wMoss * 0.5) * silt_clear);
    float soil_wet = smoothstep(0.7, 1.0, moisture) * silt_clear;

    // Enhance shoreline transition using standing water depth:
    float gw_shore_wet = (sub.g > 0.0 && world_pos.y < water_table + 0.02) ? smoothstep(0.001, 0.02, water_table + 0.02 - world_pos.y) : 0.0;
    float surf_shore_wet = (sub.b > 0.0 && world_pos.y < surf_y + 0.02) ? smoothstep(0.001, 0.02, surf_y + 0.02 - world_pos.y) : 0.0;
    float shore_wet = max(gw_shore_wet, surf_shore_wet) * silt_clear;
    float total_wet = clamp(max(soil_wet, shore_wet), 0.0, 1.0);

    col *= mix(1.0, 0.72, total_wet * (1.0 - wMoss * 0.6));
    // Damp organic pores retain rough relief. The liquid interface, when present,
    // supplies the sharp canopy reflection rather than a silver mirror on the bed.
    float wet_pores = vnoise(world_pos.xz * 36.0 + vec2(11.7, 3.1));
    rough = mix(rough, mix(0.40, 0.52, wet_pores), total_wet * 0.85);
    // The water interface supplies specular reflection; submerged humus must not
    // become another silver mirror as the silt weight fades at the bank.
    float submerged_bed = smoothstep(0.004, 0.018, water_depth);
    rough = mix(rough, max(rough, 0.78), submerged_bed);
    detail = normalize(mix(detail, wn, total_wet * 0.12));

    // Animated procedural caustics on submerged waterway bed:
    float caustic_mask = 0.0;
    vec3 caustic_light = vec3(0.0);
    EMISSION = vec3(0.0);
    if ((world_pos.y < water_surface) && (water_depth > 0.005)) {
        float submerged = smoothstep(0.005, 0.06, water_depth);
        float caustic_depth = exp(-water_depth * 1.5);
        caustic_mask = submerged * caustic_depth;
        float caustics = caustic_pattern(world_pos.xz, review_time >= 0.0 ? review_time : TIME);
        caustic_light = vec3(0.012, 0.011, 0.008) * min(caustics, 1.0) * caustic_mask;
        col += caustic_light * 0.65;
        EMISSION = vec3(0.0); // Bed contrast receives scene illumination instead of glowing in shadow.
    }
    if (world_pos.y >= water_surface) {
        caustic_mask = 0.0;
        EMISSION = vec3(0.0);
    }

    // Local ground contact mask (root collars, nurse logs, boulders):
    // Organic humus enrichment and cavity occlusion WITHOUT dark spray-paint halos
    if (contact_mask > 0.01) {
        // Natural rich organic humus accumulation: slight warmth and subtle deepening, never black paint
        vec3 humus_enrich = vec3(0.88, 0.85, 0.80);
        col = mix(col, col * humus_enrich, contact_mask * 0.35);
        // Compacted damp contact under root flare / logs
        rough = mix(rough, 0.76, contact_mask * 0.28);
    }

    if (overlay_mode == 1) col = mix(col, ramp(moisture), overlay_strength);
    else if (overlay_mode == 2) col = mix(col, ramp(f.b), overlay_strength);
    else if (overlay_mode == 3) col = mix(col, ramp(light), overlay_strength);
    else if (overlay_mode == 4) col = mix(col, vec3(code == 0.0 ? 0.55 : 0.0, code == 1.0 ? 0.8 : (code == 0.0 ? 0.35 : 0.0), code >= 2.0 ? 0.3 + 0.2 * code : 0.1), overlay_strength);
    // Preserve pore shadows even when the wet surface catches the sky. Contact mask deepens cavity occlusion.
    float base_ao = mix(0.82, 1.0, smoothstep(0.12, 0.6, dot(soil.rgb, vec3(0.333))));
    float contact_ao = mix(1.0, 0.58, contact_mask);
    AO = min(base_ao, contact_ao);
    AO_LIGHT_AFFECT = 0.25;
    float photo_look_film_mask = clamp(total_wet * (1.0 - wMoss) * (1.0 - wGrav) * (1.0 - wLeaf) * (1.0 - step(0.5, code)) * (1.0 - wSilt) * (1.0 - submerged_bed), 0.0, 1.0);
    CLEARCOAT = photo_look_wet_film * photo_look_film_mask;
    CLEARCOAT_ROUGHNESS = photo_look_wet_roughness;
    ALBEDO = col;
    ROUGHNESS = rough;
    SPECULAR = mix(0.35, 0.32, total_wet);
    SPECULAR = mix(SPECULAR, 0.12, smoothstep(0.006, 0.025, water_depth));
    NORMAL = normalize((VIEW_MATRIX * vec4(detail, 0.0)).xyz);
}
