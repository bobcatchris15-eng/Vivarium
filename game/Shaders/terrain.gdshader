// Island top surface from photo-scanned ground materials (CC0, ambientCG): soil, damp forest earth, moss,
// leaf litter, rock and gravel. Live habitat data arrives as a small field texture (R = moisture,
// G = substrate code / 4, B = nutrients, A = light) sampled by world XZ, and decides where each material shows.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

#include "res://Shaders/surface.gdshaderinc"

uniform sampler2D field_tex : filter_linear, repeat_disable;
uniform sampler2D sub_tex : filter_linear, repeat_disable;
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
uniform sampler2D rock_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float tile_metres = 0.9;    // real-world size of one texture repeat
uniform float soil_relief_amp = 0.045;   // metres of fine geometric relief on soil (lumps, furrows)
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

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;

    // fine geometric relief: real displacement (not just a normal map) so the soil reads as lumpy ground
    // rather than a flat painted plane. Gated off gravel/rock/wood/water by the substrate code.
    vec2 fuv = (world_pos.xz - field_rect.xy) / field_rect.zw;
    float code = floor(texture(field_tex, fuv).g * 4.0 + 0.5);
    float soilMask = 1.0 - step(0.5, code);   // 1 on soil (code 0), 0 elsewhere
    float eps = 0.3;
    float h0 = fbm(world_pos.xz * soil_relief_freq) - 0.5;
    float h1 = fbm((world_pos.xz + vec2(eps, 0.0)) * soil_relief_freq) - 0.5;
    float h2 = fbm((world_pos.xz + vec2(0.0, eps)) * soil_relief_freq) - 0.5;
    float amp = soil_relief_amp * soilMask;
    VERTEX += NORMAL * (h0 * amp);
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;

    vec3 grad = vec3(eps, (h1 - h0) * amp, 0.0);
    vec3 grad2 = vec3(0.0, (h2 - h0) * amp, eps);
    vec3 n2 = normalize(cross(grad2, grad));
    relief_wn = normalize((MODEL_MATRIX * vec4(mix(NORMAL, n2, soilMask), 0.0)).xyz);
    relief_mix = soilMask;
    relief_h = h0;
}

void fragment() {
    vec2 fuv = (world_pos.xz - field_rect.xy) / field_rect.zw;
    vec4 f = smooth_field(field_tex, fuv);
    vec2 sub = smooth_field(sub_tex, fuv).rg;     // smooth gravel / rock weights (water ignored)
    f.g = texture(field_tex, fuv).g;              // the substrate code (overlay only) must not be smoothed
    float moisture = f.r, light = f.a;
    float code = floor(f.g * 4.0 + 0.5);         // overlay only: 0 soil, 1 gravel, 2 rock, 3 wood, 4 water
    vec2 uv = world_pos.xz / tile_metres;
    float macro = fbm(world_pos.xz * 0.35);
    vec3 wn = normalize(mix(normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz), relief_wn, relief_mix));

    // weights first, then sample only the layers that actually show here (big saving on an iGPU)
    float wDamp = smoothstep(0.5, 0.92, moisture + (macro - 0.5) * 0.12);
    float wLeaf = smoothstep(0.55, 0.25, light) * smoothstep(0.3, 0.55, moisture) * smoothstep(0.35, 0.7, macro);
    float wMoss = smoothstep(0.55, 0.9, moisture) * smoothstep(0.25, 0.6, light) * smoothstep(0.3, 0.6, fbm(world_pos.xz * 1.7));
    float wGrav = smoothstep(0.2, 0.7, sub.r);
    float wRock = max(smoothstep(0.3, 0.8, sub.g), smoothstep(0.55, 0.8, 1.0 - wn.y));  // steep banks show bare rock

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
        col *= mix(0.78, 1.12, clump) * mix(1.0, 0.8, cav * relief_mix);
        col = mix(col, col * vec3(1.06, 0.98, 0.9), smoothstep(0.55, 0.8, vnoise(world_pos.xz * 3.1 + 5.0)) * 0.6);   // drier crusted patches
        vec3 cn = normalize(vec3(-(sx - s0) * 0.9, 0.012, -(sz - s0) * 0.9));   // world normal of the clump field
        float far = smoothstep(1.5, 6.0, length(world_pos - CAMERA_POSITION_WORLD));
        tn = normalize(mix(tn, vec3(cn.x, -cn.z, cn.y), mix(0.35, 0.7, far) * relief_mix));   // stronger far away, where the texture normal has faded
        rough = mix(rough, 0.95, clump * 0.3);
    }
    if (wDamp > 0.01) {
        vec3 n2 = unpack_normal(texture(damp_nrm, uv * 1.15 + 3.1).rgb);
        vec4 d = vec4(texture(damp_col, uv * 1.15 + 3.1).rgb, texture(damp_rgh, uv * 1.15 + 3.1).r);
        float w = mix(wDamp, hblend(wDamp, dot(d.rgb, vec3(0.33)) * 2.0), 0.35);   // soft: moisture creeps, it has no edge
        col = mix(col, d.rgb, w); rough = mix(rough, d.a, w); tn = normalize(mix(tn, n2, w));
    }
    if (wLeaf > 0.01) {
        vec2 lu = uv * 1.3 + 1.7;
        vec4 l = vec4(texture(leaf_col, lu).rgb, texture(leaf_rgh, lu).r);
        float w = hblend(wLeaf, dot(l.rgb, vec3(0.33)) * 2.0);
        col = mix(col, l.rgb, w); rough = mix(rough, l.a, w); tn = normalize(mix(tn, unpack_normal(texture(leaf_nrm, lu).rgb), w));
    }
    if (wMoss > 0.01) {
        vec2 mu = uv * 2.2 + 7.3;
        vec4 m = vec4(texture(moss_col, mu).rgb, texture(moss_rgh, mu).r);
        float w = hblend(wMoss, dot(m.rgb, vec3(0.33)) * 2.5);
        col = mix(col, m.rgb, w * 0.85); rough = mix(rough, m.a, w); tn = normalize(mix(tn, unpack_normal(texture(moss_nrm, mu).rgb), w));
    }
    if (wGrav > 0.01) {
        vec2 gu = uv * 2.4;
        vec4 g = vec4(texture(gravel_col, gu).rgb, texture(gravel_rgh, gu).r);
        g.rgb = mix(vec3(dot(g.rgb, vec3(0.3, 0.59, 0.11))), g.rgb, 0.45) * vec3(1.03, 1.0, 0.95);   // desaturate, warm slightly
        float w = hblend(wGrav, dot(g.rgb, vec3(0.33)) * 2.0);
        col = mix(col, g.rgb, w); rough = mix(rough, g.a, w); tn = normalize(mix(tn, unpack_normal(texture(gravel_nrm, gu).rgb), w));
    }

    // world-space normal: geometry normal perturbed by the blended detail (uv = world xz; GL green = -z)
    vec3 detail = normalize(wn + vec3(tn.x, 0.0, -tn.y) * 0.9);
    if (wRock > 0.01) {
        vec3 n_rock;
        vec4 rock = triplanar(rock_col, rock_nrm, rock_rgh, world_pos / (tile_metres * 0.9), wn, 4.0, n_rock);
        float w = hblend(wRock, dot(rock.rgb, vec3(0.33)) * 2.0);
        col = mix(col, rock.rgb, w); rough = mix(rough, rock.a, w); detail = normalize(mix(detail, n_rock, w));
    }

    // moisture ramp: dry ground is paler and dustier, damp ground darkens gradually, saturated ground glosses
    float dryness = smoothstep(0.42, 0.18, moisture);
    col = mix(col, mix(vec3(dot(col, vec3(0.3, 0.59, 0.11))), col, 0.7) * 1.18, dryness);
    col *= mix(1.0, 0.72, smoothstep(0.3, 0.75, moisture) * (1.0 - wMoss * 0.5));
    float wet = smoothstep(0.7, 1.0, moisture);
    col *= mix(1.0, 0.8, wet * (1.0 - wMoss * 0.6));
    rough = mix(rough, 0.28, wet * 0.8);
    detail = normalize(mix(detail, wn, wet * 0.35));

    if (overlay_mode == 1) col = mix(col, ramp(moisture), overlay_strength);
    else if (overlay_mode == 2) col = mix(col, ramp(f.b), overlay_strength);
    else if (overlay_mode == 3) col = mix(col, ramp(light), overlay_strength);
    else if (overlay_mode == 4) col = mix(col, vec3(code == 0.0 ? 0.55 : 0.0, code == 1.0 ? 0.8 : (code == 0.0 ? 0.35 : 0.0), code >= 2.0 ? 0.3 + 0.2 * code : 0.1), overlay_strength);
    ALBEDO = col;
    ROUGHNESS = rough;
    SPECULAR = mix(0.35, 0.6, wet);
    NORMAL = normalize((VIEW_MATRIX * vec4(detail, 0.0)).xyz);
}
