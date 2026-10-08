// Island top surface from photo-scanned ground materials (CC0, ambientCG): soil, damp forest earth, moss,
// leaf litter and gravel. Live habitat data arrives as a small field texture (R = moisture,
// G = substrate code / 4, B = nutrients, A = light) sampled by world XZ, and decides where each material shows.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

#include "res://Shaders/surface.gdshaderinc"

uniform sampler2D field_tex : filter_linear, repeat_disable;
uniform sampler2D sub_tex : filter_linear, repeat_disable;
uniform sampler2D litter_tex : filter_linear, repeat_disable;
uniform sampler2D ambient_tex : filter_linear, repeat_disable;
uniform sampler2D cover_tex : filter_linear, repeat_disable;   // moss/lichen colony occupancy (IslandRenderer)
uniform vec4 field_rect;            // origin.x, origin.z, size.x, size.z
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

    // fine geometric relief: real displacement (not just a normal map) so the soil reads as lumpy ground
    // rather than a flat painted plane. Gated off gravel/rock/wood/water by the substrate code.
    vec2 fuv = (world_pos.xz - field_rect.xy) / field_rect.zw;
    float code = floor(texture(field_tex, fuv).g * 4.0 + 0.5);
    float soilMask = 1.0 - step(0.5, code);   // 1 on soil (code 0), 0 elsewhere
    float eps = 0.3;
    float h0 = mix(vnoise(world_pos.xz * soil_relief_freq), vnoise(world_pos.xz * soil_relief_freq * 2.03 + 4.7), 0.28) - 0.5;
    float h1 = mix(vnoise((world_pos.xz + vec2(eps, 0.0)) * soil_relief_freq), vnoise((world_pos.xz + vec2(eps, 0.0)) * soil_relief_freq * 2.03 + 4.7), 0.28) - 0.5;
    float h2 = mix(vnoise((world_pos.xz + vec2(0.0, eps)) * soil_relief_freq), vnoise((world_pos.xz + vec2(0.0, eps)) * soil_relief_freq * 2.03 + 4.7), 0.28) - 0.5;
    float amp = soil_relief_amp * soilMask;
    VERTEX += NORMAL * (h0 * amp);
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;

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
    vec2 sub = vec2(texture(sub_tex, gravel_uv).r, smooth_field(sub_tex, fuv).g);
    // Submerged waterway bed (sub.g). IslandRenderer writes it from the simulation's standing-water depth, not
    // from moisture, so a merely damp cell keeps its moss and only a real waterway bed turns to silt. The mask
    // arrives C1-smoothed over the 25 cm sub grid and is broken up with the same world-space noise the gravel
    // edge uses, so the shoreline is a soft irregular gradient rather than a step at a cell boundary.
    float silt_edge = (vnoise(world_pos.xz * 4.7 + vec2(21.3, 5.9)) - 0.5) * 0.34;
    float wSilt = smoothstep(0.10, 0.74, sub.g + silt_edge);
    f.g = texture(field_tex, fuv).g;              // the substrate code (overlay only) must not be smoothed
    float moisture = f.r, light = f.a;
    float code = floor(f.g * 4.0 + 0.5);         // overlay only: 0 soil, 1 gravel, 2 rock, 3 wood, 4 water
    float macro = fbm(world_pos.xz * 0.35);
    // Smooth world-space distortion keeps the albedo, normal and roughness of every scan registered.
    // The broad field changes scale subtly rather than repeating one 56 cm square across the island.
    vec2 drift = vec2(vnoise(world_pos.xz * 0.72 + vec2(6.2, 1.7)),
                      vnoise(world_pos.xz * 0.72 + vec2(23.7, 14.2))) - 0.5;
    vec2 uv = world_pos.xz / (tile_metres * mix(0.84, 1.24, macro)) + drift * 0.36;
    vec3 wn = normalize(mix(normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz), relief_wn, relief_mix));

    // weights first, then sample only the layers that actually show here (big saving on an iGPU)
    // Silt is deposited sediment, not soaked forest earth: the damp and moss layers are cut back under water so
    // a saturated channel cannot drift back to wet green. The mask alone decides this — moisture would do it
    // everywhere that is merely damp.
    float wDamp = smoothstep(0.38, 0.82, moisture + (macro - 0.5) * 0.18) * (1.0 - wSilt);
    float stored_litter = smooth_field(litter_tex, fuv).r;
    float ambient_cover = smooth_field(ambient_tex, fuv).r;
    float colony = smooth_field(cover_tex, fuv).r;
    float wLeaf = stored_litter * smoothstep(0.31, 0.64, fbm(world_pos.xz * 0.83 + 7.2));
    float wMoss = smoothstep(0.48, 0.83, moisture) * smoothstep(0.20, 0.53, light)
        * smoothstep(0.32, 0.69, fbm(world_pos.xz * 1.14 + 2.8)) * (1.0 - wSilt)
        * (1.0 - 0.75 * smoothstep(0.04, 0.45, colony));
    float gravel_edge = (vnoise(world_pos.xz * 5.3 + vec2(8.7, 13.2)) - 0.5) * 0.16;
    float wGrav = smoothstep(0.16, 0.84, sub.r + gravel_edge);

    vec3 tn;
    vec4 soil = sample_untiled(soil_col, soil_nrm, soil_rgh, uv, tn);
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
        tn = normalize(mix(tn, vec3(cn.x, -cn.z, cn.y), 0.28 * relief_mix));
        rough = mix(rough, 0.95, clump * 0.3);
    }
    if (wDamp > 0.01) {
        vec2 du = uv * 1.15 + 3.1;
        vec3 n2 = unpack_normal(texture(damp_nrm, du).rgb);
        vec4 d = vec4(texture(damp_col, du).rgb, texture(damp_rgh, du).r);
        float w = mix(wDamp, hblend(wDamp, dot(d.rgb, vec3(0.33)) * 2.0), 0.35);   // soft: moisture creeps, it has no edge
        col = mix(col, d.rgb, w); rough = mix(rough, d.a, w); tn = normalize(mix(tn, n2, w));
    }
    if (wLeaf > 0.01) {
        vec2 lu = uv * 1.3 + 1.7;
        vec3 ln = unpack_normal(texture(leaf_nrm, lu).rgb);
        vec4 l = vec4(texture(leaf_col, lu).rgb, texture(leaf_rgh, lu).r);
        float w = hblend(wLeaf, dot(l.rgb, vec3(0.33)) * 2.0);
        col = mix(col, l.rgb, w); rough = mix(rough, l.a, w); tn = normalize(mix(tn, ln, w));
    }
    if (wMoss > 0.01) {
        vec2 mu = uv * 2.4 + 7.3;
        vec3 mn = unpack_normal(texture(moss_nrm, mu).rgb);
        vec4 m = vec4(texture(moss_col, mu).rgb, texture(moss_rgh, mu).r);
        // Ground the bright moss texture with earthy forest tones and rhizoid boundary
        vec3 natural_moss = mix(m.rgb, m.rgb * vec3(0.74, 0.88, 0.58), 0.5);
        float h = dot(m.rgb, vec3(0.333)) * 1.2;
        float w = hblend(wMoss, h);
        vec3 rhizoid_col = mix(col * 0.78, natural_moss, smoothstep(0.08, 0.45, w));
        col = mix(col, rhizoid_col, w * 0.75);
        rough = mix(rough, m.a, w * 0.7);
        tn = normalize(mix(tn, mn, w));
    }
    if (wGrav > 0.01) {
        vec2 gu = uv * 2.4;
        vec3 gn = unpack_normal(texture(gravel_nrm, gu).rgb);
        vec4 g = vec4(texture(gravel_col, gu).rgb, texture(gravel_rgh, gu).r);
        g.rgb = mix(vec3(dot(g.rgb, vec3(0.3, 0.59, 0.11))), g.rgb, 0.45) * vec3(1.03, 1.0, 0.95);   // desaturate, warm slightly
        float w = hblend(wGrav, dot(g.rgb, vec3(0.33)) * 2.0);
        col = mix(col, g.rgb, w); rough = mix(rough, g.a, w); tn = normalize(mix(tn, gn, w));
    }
    if (wSilt > 0.01) {
        // Silt: fine pale sediment settled out of the water. None of the scans (soil, damp earth, moss, leaf
        // litter) is what a stream bed actually looks like, so the bed is repainted rather than tinted. A little
        // of its own broad value variation is kept so the channel still reads as a surface, and the fine normal
        // is flattened: settled silt is smooth and matte, not the pebbled relief of a soil scan.
        float silt_grain = fbm(world_pos.xz * 5.3 + vec2(5.1, 31.7));
        // Dark, muddy brown: wet organic sediment. These are LINEAR albedo values under an Agx tonemapper, which
        // lifts shadows, so they sit far lower than the sRGB brown they read as (roughly sRGB 0.30/0.25/0.21).
        // Warm-biased so it still separates from the surrounding damp soil.
        vec3 silt_col = vec3(0.070, 0.049, 0.034) * mix(0.78, 1.22, silt_grain);
        col = mix(col, silt_col, wSilt);
        rough = mix(rough, 0.96, wSilt);
        tn = normalize(mix(tn, vec3(0.0, 0.0, 1.0), wSilt * 0.6));
    }

    // world-space normal: geometry normal perturbed by the blended detail (uv = world xz; GL green = -z)
    vec3 detail = normalize(wn + vec3(tn.x, 0.0, -tn.y) * 0.9);


    // Anonymous low vegetation reads as a soft green underlayer at distance. Close-up silhouette comes from
    // AmbientGroundCoverRenderer; this prevents the same field from turning back into brown dirt when geometry culls.
    float ambient_micro = 0.76 + 0.24 * fbm(world_pos.xz * 7.5 + vec2(19.1, 4.7));
    float ambient_weight = smoothstep(0.035, 0.72, ambient_cover) * 0.72 * ambient_micro
        * (1.0 - wGrav) * (1.0 - wLeaf * 0.55) * (1.0 - wSilt);
    vec3 ambient_dry = vec3(0.22, 0.35, 0.09);
    vec3 ambient_mesic = vec3(0.075, 0.29, 0.065);
    vec3 ambient_shade = vec3(0.045, 0.205, 0.105);
    vec3 ambient_col = mix(ambient_dry, ambient_mesic, smoothstep(0.12, 0.58, moisture));
    ambient_col = mix(ambient_col, ambient_shade, smoothstep(0.62, 0.18, light));
    ambient_col *= mix(0.88, 1.08, ambient_micro);
    col = mix(col, ambient_col, ambient_weight);
    rough = mix(rough, 0.9, ambient_weight * 0.45);

    // Ground cushion moss on bare gravel/soil: creeps in where it is moist, shaded or already vegetated, and
    // hugs gravel margins; dry exposed gravel stays clear. Same cushion logic as the log moss (surface.gdshaderinc).
    float bare = max(wGrav, (1.0 - wMoss) * (1.0 - ambient_weight) * (1.0 - wLeaf * 0.7));
    float gedge = wGrav * (1.0 - wGrav) * 4.0;
    float favour = moisture * 0.7 + (1.0 - light) * 0.55 + ambient_cover * 0.5 + gedge * 0.6 - 0.40;
    float patchn = vnoise(world_pos.xz * 1.7 + vec2(3.3, 9.1)) * 0.65 + vnoise(world_pos.xz * 4.9 + 1.2) * 0.35;
    float gm = smoothstep(0.0, 0.35, favour + (patchn - 0.5) * 0.6) * bare * (1.0 - wSilt)
        * (1.0 - smoothstep(0.04, 0.45, colony));   // bare ground around real colonies so they read as distinct patches
    if (gm > 0.02) {
        float speck;
        vec4 cl = moss_clumps(world_pos.xz * 38.0, speck);
        float cover = clamp(gm + (cl.x - 0.5) * 0.8 * (1.0 - gm * gm), 0.0, 1.0);
        float fib = vnoise(world_pos.xz * vec2(160.0, 60.0));
        // Fine fuzz: two high-frequency octaves (~2-5 mm grain) so a close view reads as countless shoots,
        // not smooth cobbles; it also breaks the cell seams so neighbouring cushions merge.
        float fz1 = vnoise(world_pos.xz * 420.0 + vec2(cl.w * 17.0));
        float fz2 = vnoise(world_pos.xz * 190.0 + 4.1);
        float fuzz = fz1 * 0.55 + fz2 * 0.45;
        float h = clamp(cl.x + (fuzz - 0.5) * 0.45, 0.0, 1.0);
        vec3 base = mix(vec3(0.07, 0.135, 0.035), vec3(0.12, 0.20, 0.05), cl.w) * mix(0.85, 1.12, patchn);
        vec3 mc = mix(base, vec3(0.24, 0.33, 0.09), smoothstep(0.45, 0.95, h) * (0.35 + 0.5 * fib));
        mc *= mix(0.55, 1.0, smoothstep(0.0, 0.5, h)) * (0.62 + 0.6 * fuzz);
        mc = mix(mc, vec3(0.42, 0.36, 0.13), speck * 0.6);
        float a = smoothstep(0.12, 0.55, cover);
        col = mix(col, mc, a);
        rough = mix(rough, 0.95, a);
        vec2 fg = vec2(vnoise(world_pos.xz * 300.0 + 2.7), vnoise(world_pos.xz * 300.0 + 8.3)) - 0.5;
        vec3 dn = normalize(vec3(cl.y * 0.5 + fg.x * 0.9, 0.6 * max(cl.x, 0.25), cl.z * 0.5 + fg.y * 0.9));
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

    // Enhance shoreline transition using standing water depth (sub.g):
    // Within a few cm of the water margin (sub.g > 0.001 or transition edge),
    // apply wet darkening to ALBEDO and increase gloss (lower ROUGHNESS, higher SPECULAR).
    float shore_wet = smoothstep(0.001, 0.06, sub.g + silt_edge * 0.1) * silt_clear;
    float total_wet = clamp(max(soil_wet, shore_wet), 0.0, 1.0);

    col *= mix(1.0, 0.72, total_wet * (1.0 - wMoss * 0.6));
    rough = mix(rough, 0.20, total_wet * 0.85);
    detail = normalize(mix(detail, wn, total_wet * 0.45));

    // Animated procedural caustics on submerged waterway bed (sub.g > 0.005)
    float submerged = smoothstep(0.005, 0.06, sub.g);
    float caustic_depth = exp(-sub.g * 1.5);
    float caustic_mask = submerged * caustic_depth;
    float caustics = caustic_pattern(world_pos.xz, TIME);
    vec3 caustic_light = vec3(0.24, 0.32, 0.26) * caustics * caustic_mask;
    col += caustic_light * 0.65;

    if (overlay_mode == 1) col = mix(col, ramp(moisture), overlay_strength);
    else if (overlay_mode == 2) col = mix(col, ramp(f.b), overlay_strength);
    else if (overlay_mode == 3) col = mix(col, ramp(light), overlay_strength);
    else if (overlay_mode == 4) col = mix(col, vec3(code == 0.0 ? 0.55 : 0.0, code == 1.0 ? 0.8 : (code == 0.0 ? 0.35 : 0.0), code >= 2.0 ? 0.3 + 0.2 * code : 0.1), overlay_strength);
    // Preserve pore shadows even when the wet surface catches the sky.
    AO = mix(0.82, 1.0, smoothstep(0.12, 0.6, dot(soil.rgb, vec3(0.333))));
    AO_LIGHT_AFFECT = 0.25;
    ALBEDO = col;
    ROUGHNESS = rough;
    SPECULAR = mix(0.35, 0.75, total_wet);
    EMISSION = caustic_light * 0.35;
    NORMAL = normalize((VIEW_MATRIX * vec4(detail, 0.0)).xyz);
}
