// Continuous organic moss/lichen mats (docs/overhaul/growth_models.md §11, §4.5/§5).
// COLOR.rgb = species colour already modulated by biomass/dormancy/rim/boundary in C#.
// COLOR.a = coverage occupancy fraction from the corner grid (0.25/0.5/0.75/1.0), used to feather
// the rim into an irregular, noise-perturbed edge instead of a hard cell-aligned staircase.
// UV = world-space (x, z) in metres, stable across tiles, used for seamless micro-detail noise.
// u_pattern selects the sub-cell detail: 0 = moss (fibrous/stippled), 1 = lichen (cracked/areolate).
shader_type spatial;
render_mode diffuse_burley, cull_disabled;

#include "res://Shaders/surface.gdshaderinc"

uniform float u_pattern = 0.0;

varying vec3 world_pos;

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    vec3 base = srgb_to_linear(COLOR.rgb);
    vec2 wp = world_pos.xz;

    // Irregular, noise-jittered edge feather: interior (alpha==1) never discards; the ~1-cell
    // rim band (alpha 0.25..0.75) breaks up raggedly instead of following the cell grid.
    float edgeNoise = fbm(wp * 4.0) * 0.25 + fbm(wp * 16.0) * 0.30 + fbm(wp * 70.0) * 0.45;
    float threshold = clamp(0.5 + (edgeNoise - 0.5) * 2.2, 0.05, 0.95);
    if (COLOR.a < threshold) discard;
    float coverMask = smoothstep(threshold - 0.15, threshold + 0.15, COLOR.a);

    vec3 c = base;
    float rough = 0.92;
    vec3 bump = vec3(0.0);

    if (u_pattern < 0.5) {
        // Moss: fine fibrous/stippled micro-detail, sub-cell scale (well above cell frequency).
        vec2 p = wp * 60.0;
        float fibers = fbm(p * vec2(1.0, 3.2));
        float stipple = hash21(floor(wp * 140.0));
        c *= 0.86 + 0.20 * fibers + 0.10 * (stipple - 0.5);
        float e = 0.01;
        float h = fibers;
        float dx = fbm((wp + vec2(e, 0.0)) * vec2(60.0, 192.0)) - h;
        float dz = fbm((wp + vec2(0.0, e)) * vec2(60.0, 192.0)) - h;
        bump = vec3(-dx, 0.0, -dz) * 1.4;
        rough = 0.95;
    } else {
        // Crust/foliose lichen: cracked, areolate patches with lobed tint variation.
        vec2 p = wp * 22.0;
        float cracks = vnoise(p) ;
        float areole = smoothstep(0.46, 0.5, cracks) - smoothstep(0.5, 0.56, cracks);
        c *= 0.90 + 0.14 * cracks - 0.35 * areole;
        float lobes = fbm(wp * 5.0 + 3.7);
        c = mix(c, c * 1.12 + vec3(0.02, 0.02, 0.0), smoothstep(0.55, 0.8, lobes));
        float e = 0.02;
        float dx = vnoise(p + vec2(e, 0.0)) - cracks;
        float dz = vnoise(p + vec2(0.0, e)) - cracks;
        bump = vec3(-dx, 0.0, -dz) * 2.0;
        rough = 0.88;
    }

    // Rim/interior relief: brighten and lift shading slightly where coverage is thin (rim), consistent
    // with the C#-side rim/interior colour split, and darken slightly toward full interior density.
    c *= mix(1.05, 0.96, coverMask);

    vec3 wn = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
    vec3 n = normalize(wn + bump * 0.5);

    ALBEDO = c;
    ROUGHNESS = rough;
    SPECULAR = 0.15;
    NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
}
