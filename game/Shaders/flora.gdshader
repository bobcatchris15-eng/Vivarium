// Flora instances: baked vertex colours per species, INSTANCE_CUSTOM = (tint variation, health, wobble, seed).
// surface_mode picks the micro-detail: 0 moss (photo-scanned moss, CC0 ambientCG, tinted to the species),
// 1 lichen (lobed, crusty procedural relief), 2 vascular plants (veined, waxy leaves with translucency).
// Sway and poke wobble are purely cosmetic.
shader_type spatial;
render_mode diffuse_burley, cull_disabled;

#include "res://Shaders/surface.gdshaderinc"

uniform float sway = 1.0;
uniform float stiffness = 1.0;
uniform int surface_mode = 2;
uniform sampler2D moss_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;

varying vec4 inst;
varying vec3 world_pos;
varying vec3 obj_pos;

void vertex() {
    inst = INSTANCE_CUSTOM;
    obj_pos = VERTEX;
    float h = max(VERTEX.y, 0.0);
    float phase = TIME * 1.3 + inst.w * 6.2831;
    float amount = (0.015 * sway + inst.z * 0.15) / stiffness;
    VERTEX.x += sin(phase) * h * h * amount * 3.0 + sin(TIME * 18.0 + inst.w * 40.0) * inst.z * h * 0.2;
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
        c = mix(m.rgb * (lum(c) / avg), c * (m.rgb / avg), 0.45) * 0.9;
        n = normalize(wn + vec3(tn.x, 0.0, -tn.y) * 1.2);
        rough = m.a;
        // mats thin out and darken toward their rim so they settle into the soil instead of reading as a plate
        float rimd = length(obj_pos.xz);
        c *= mix(0.45, 1.0, smoothstep(1.05, 0.55, rimd));
    } else if (surface_mode == 1) {
        // lichen: lobed rosettes with raised rims and powdery centres
        vec2 p = obj_pos.xz * 9.0 + inst.w * 17.0;
        float lobes = fbm(p);
        float rim = smoothstep(0.45, 0.5, lobes) - smoothstep(0.5, 0.56, lobes);
        c *= 0.8 + 0.35 * lobes + 0.25 * rim;
        c = mix(c, vec3(lum(c)) * 1.15, smoothstep(0.6, 0.8, vnoise(p * 3.0)) * 0.35);   // powdery soredia
        float e = 0.02;
        float dx = fbm(p + vec2(e, 0.0)) - lobes, dz = fbm(p + vec2(0.0, e)) - lobes;
        n = normalize(wn - vec3(dx, 0.0, dz) / e * 0.08);
        rough = 0.95;
    } else {
        // vascular plants: midrib + lateral veins from the leaf uv, slight waxy sheen
        float along = UV.x, across = UV.y;
        float mid = 1.0 - smoothstep(0.0, 0.04, abs(fract(across) - 0.5));
        float lat = 1.0 - smoothstep(0.0, 0.08, abs(fract(along * 7.0 + abs(fract(across) - 0.5) * 3.0) - 0.5));
        c *= 1.0 + 0.18 * mid + 0.08 * lat - 0.06 * vnoise(world_pos.xz * 140.0);
        rough = 0.55;
    }
    float health = clamp(inst.y, 0.0, 1.0);
    vec3 faded = mix(vec3(dot(c, vec3(0.33))), vec3(0.62, 0.55, 0.32), 0.5);
    ALBEDO = mix(faded, c, 0.35 + 0.65 * health);
    ROUGHNESS = rough;
    SPECULAR = surface_mode == 2 ? 0.45 : 0.25;
    BACKLIGHT = c * (surface_mode == 2 ? 0.45 : 0.2);
    NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
}
