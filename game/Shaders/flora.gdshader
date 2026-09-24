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
    float ma = inst.x - 0.5, mb = inst.w - 0.5;
    VERTEX.xz *= vec2(1.0 + ma * 0.10, 1.0 - ma * 0.07);
    float grow = smoothstep(0.0, 1.0, h);
    VERTEX.x += grow * grow * mb * 0.055;
    VERTEX.z += grow * grow * ma * 0.045;
    float phase = TIME * 1.3 + inst.w * 6.2831;
    float amount = (0.012 * sway + inst.z * 0.15) / stiffness;
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
        float lobes = mix(vnoise(p), vnoise(p * 1.87 + 4.3), 0.35);
        float rim = smoothstep(0.42, 0.55, lobes) - smoothstep(0.55, 0.7, lobes);
        c *= 0.88 + 0.22 * lobes + 0.10 * rim;
        c = mix(c, vec3(lum(c)) * 1.1, smoothstep(0.66, 0.84, vnoise(p * 2.6 + 7.1)) * 0.22);
        float e = 0.02;
        float dx = fbm(p + vec2(e, 0.0)) - lobes, dz = fbm(p + vec2(0.0, e)) - lobes;
        n = normalize(wn - vec3(dx, 0.0, dz) / e * 0.08);
        rough = 0.95;
    } else if (surface_mode == 3) {
        // fungi: matte, faintly translucent flesh; radial gill lines on undersides (UV2.x = 1) and fine
        // concentric growth zones on caps and shelves (UV.x runs from centre to rim)
        float warp = (vnoise(obj_pos.xz * 17.0 + inst.w * 9.0) - 0.5) * 0.12;
        float gills = UV2.x > 0.5 ? 0.90 + 0.10 * smoothstep(0.35, 0.65, fract((UV.x + warp) * 31.0)) : 1.0;
        float zones = 0.96 + 0.04 * sin(UV2.y * 37.0 + UV.x * 23.0 + warp * 12.0);
        c *= gills * zones * (0.94 + 0.12 * vnoise(obj_pos.xz * 70.0));
        rough = 0.85;
    } else if (surface_mode == 4) {
        // slime mold: glossy, translucent yellow protoplasm with a slow streaming pulse along the veins
        float pulse = 0.5 + 0.5 * sin(TIME * 1.6 - length(obj_pos.xz) * 9.0 + inst.w * 20.0);
        c *= 0.85 + 0.3 * pulse;
        rough = 0.3;
    } else {
        // vascular plants: midrib + lateral veins from the leaf uv, slight waxy sheen
        float along = UV.x, across = UV.y;
        float veinWarp = (vnoise(vec2(along * 5.0, across * 3.0) + inst.w * 5.0) - 0.5) * 0.06;
        float mid = 1.0 - smoothstep(0.0, 0.035, abs(fract(across + veinWarp) - 0.5));
        float lat = 1.0 - smoothstep(0.0, 0.07, abs(fract(along * 5.3 + abs(fract(across) - 0.5) * 2.1 + veinWarp) - 0.5));
        c *= 1.0 + 0.12 * mid + 0.045 * lat - 0.035 * vnoise(world_pos.xz * 110.0);
        rough = 0.55;
    }
    float health = clamp(inst.y, 0.0, 1.0);
    vec3 faded = mix(vec3(dot(c, vec3(0.33))), vec3(0.62, 0.55, 0.32), 0.5);
    ALBEDO = mix(faded, c, 0.35 + 0.65 * health);
    ROUGHNESS = rough;
    SPECULAR = surface_mode == 2 ? 0.45 : surface_mode == 4 ? 0.6 : 0.25;
    BACKLIGHT = c * (surface_mode == 2 ? 0.45 : surface_mode >= 3 ? 0.35 : 0.2);
    NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
}
