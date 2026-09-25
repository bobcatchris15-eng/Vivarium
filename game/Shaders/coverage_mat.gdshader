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
    float ao = 1.0;

    // Per-patch grain direction so fibre/lobe streaking is not axis-aligned everywhere; derived from
    // a coarse hash of world position so it stays stable per clump instead of jittering per fragment.
    float grainAngle = hash21(floor(wp * 0.35)) * 6.2831853;
    vec2 grainDir = vec2(cos(grainAngle), sin(grainAngle));
    vec2 grainOrth = vec2(-grainDir.y, grainDir.x);

    if (u_pattern < 0.5) {
        // Moss: layered fibrous/leaflet micro-detail at ~1-3mm scale with directional grain per clump,
        // plus tip brightening so individual shoots read instead of a flat stippled paint.
        vec2 pg = vec2(dot(wp, grainDir), dot(wp, grainOrth));
        float fibers = fbm(pg * vec2(3.0, 26.0));
        float leaflets = fbm(pg * vec2(9.0, 70.0) + 11.3);
        float stipple = hash21(floor(wp * 140.0));
        float tips = smoothstep(0.62, 0.92, fbm(pg * vec2(6.0, 50.0) + 5.1));
        c *= 0.82 + 0.16 * fibers + 0.14 * leaflets + 0.08 * (stipple - 0.5);
        c = mix(c, c * 1.35 + vec3(0.03, 0.05, 0.0), tips * 0.5);

        float e = 0.01;
        float h = fibers + leaflets * 0.6;
        vec2 pgx = vec2(dot(wp + vec2(e, 0.0), grainDir), dot(wp + vec2(e, 0.0), grainOrth));
        vec2 pgz = vec2(dot(wp + vec2(0.0, e), grainDir), dot(wp + vec2(0.0, e), grainOrth));
        float dx = (fbm(pgx * vec2(3.0, 26.0)) + fbm(pgx * vec2(9.0, 70.0) + 11.3) * 0.6) - h;
        float dz = (fbm(pgz * vec2(3.0, 26.0)) + fbm(pgz * vec2(9.0, 70.0) + 11.3) * 0.6) - h;
        bump = vec3(-dx, 0.0, -dz) * 3.2;

        // Crevice AO: darken low-fibre troughs between shoot clusters.
        ao = mix(0.72, 1.0, smoothstep(-0.1, 0.5, fibers));
        rough = 0.96;
    } else {
        // Crust: cracked, raised-rim areolate tiles. Foliose species (looser tint spread, sp.Color2
        // farther from sp.Color1) read lobed with lighter edges; reindeer-lichen footprint (fine
        // branching) shows via the same noise at a finer scale plus pale branch tips.
        vec2 p = wp * 22.0;
        float cracks = vnoise(p);
        float crackLine = smoothstep(0.46, 0.5, cracks) - smoothstep(0.5, 0.56, cracks);
        float raisedRim = smoothstep(0.5, 0.56, cracks) - smoothstep(0.56, 0.62, cracks);
        c *= 0.90 + 0.14 * cracks - 0.40 * crackLine;
        c = mix(c, c * 1.10, raisedRim);
        ao = mix(1.0, 0.55, crackLine);

        float lobes = fbm(wp * 5.0 + 3.7);
        vec3 lobeEdgeTint = c * 1.18 + vec3(0.03, 0.03, 0.01);
        vec3 lobeCenterTint = c * 0.90;
        c = mix(c, lobeEdgeTint, smoothstep(0.55, 0.72, lobes) * (1.0 - smoothstep(0.72, 0.9, lobes)));
        c = mix(c, lobeCenterTint, smoothstep(0.3, 0.1, lobes));

        // Fine branching tips (reindeer-lichen character): pale, high-frequency filaments.
        float branch = fbm(wp * 35.0 + 8.2);
        float branchTip = smoothstep(0.6, 0.85, branch);
        c = mix(c, c * 1.4 + vec3(0.08), branchTip * 0.35);

        float e = 0.02;
        float dx = vnoise(p + vec2(e, 0.0)) - cracks;
        float dz = vnoise(p + vec2(0.0, e)) - cracks;
        bump = vec3(-dx, 0.0, -dz) * 3.0;
        rough = 0.86;
    }

    // Rim/interior relief: brighten and lift shading slightly where coverage is thin (rim), consistent
    // with the C#-side rim/interior colour split, and darken slightly toward full interior density.
    c *= mix(1.05, 0.96, coverMask);
    c *= ao;

    vec3 wn = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
    vec3 n = normalize(wn + bump * 0.5);

    ALBEDO = c;
    ROUGHNESS = rough;
    SPECULAR = 0.15;
    NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
}
