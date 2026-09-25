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
uniform sampler2D moss_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D lichen_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;

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
    vec3 wn = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);

    // Per-patch grain direction so fibre/lobe streaking is not axis-aligned everywhere; derived from
    // a coarse hash of world position so it stays stable per clump instead of jittering per fragment.
    float grainAngle = hash21(floor(wp * 0.35)) * 6.2831853;
    vec2 grainDir = vec2(cos(grainAngle), sin(grainAngle));
    vec2 grainOrth = vec2(-grainDir.y, grainDir.x);

    if (u_pattern < 0.5) {
        // Photo-scanned moss supplies irregular leaf clusters and contact shadows across the
        // geometric pillows. World-space sampling keeps one patch continuous over tile seams.
        vec3 scan_n;
        vec4 scan = triplanar(moss_col, moss_nrm, moss_rgh,
            world_pos / 0.24, wn, 5.0, scan_n);
        float scan_avg = max(dot(textureLod(moss_col, vec2(0.5), 10.0).rgb,
            vec3(0.2126, 0.7152, 0.0722)), 0.05);
        c *= mix(vec3(1.0), clamp(scan.rgb / scan_avg, vec3(0.25), vec3(1.7)), 0.78);
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
        bump = vec3(-dx, 0.0, -dz) * 1.2 + (scan_n - wn) * 0.65;

        // Crevice AO: darken low-fibre troughs between shoot clusters.
        ao = mix(0.72, 1.0, smoothstep(-0.1, 0.5, fibers));
        rough = mix(0.96, scan.a, 0.55);
    } else {
        // A scanned-style lichen thallus replaces the synthetic dark squiggles. Its light curled
        // lobes and granular troughs are recoloured by the species, over the folded sheet mesh.
        vec3 thallus = texture(lichen_col, wp / 0.22).rgb;
        c *= clamp(vec3(0.42) + thallus * 1.25, vec3(0.3), vec3(1.55));
        rough = 0.91;
    }

    // Rim/interior relief: brighten and lift shading slightly where coverage is thin (rim), consistent
    // with the C#-side rim/interior colour split, and darken slightly toward full interior density.
    c *= mix(1.05, 0.96, coverMask);
    c *= ao;

    vec3 n = normalize(wn + bump * 0.5);

    ALBEDO = c;
    ROUGHNESS = rough;
    SPECULAR = 0.15;
    NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
}
