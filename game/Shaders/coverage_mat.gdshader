// Continuous organic moss/lichen mats (docs/overhaul/growth_models.md §11, §4.5/§5).
// COLOR.rgb = species colour already modulated by biomass/dormancy/rim/boundary in C#.
// COLOR.a = coverage occupancy fraction from the corner grid (0.25/0.5/0.75/1.0), used to feather
// the rim into an irregular, noise-perturbed edge instead of a hard cell-aligned staircase.
// UV = world-space (x, z) in metres, stable across tiles, used for seamless micro-detail noise.
// u_pattern selects the sub-cell detail: 0 = moss (fibrous/stippled), 1 = lichen (cracked/areolate).
shader_type spatial;
render_mode diffuse_burley, cull_disabled;

#include "res://Shaders/surface.gdshaderinc"
#include "res://Shaders/moss_aggregate.gdshaderinc"

uniform float u_pattern = 0.0;
uniform float u_moss_colony_nuclei = 0.0;
uniform float u_moss_aggregate = 0.75;
uniform sampler2D moss_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D lichen_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;

varying vec3 world_pos;
varying float sundew_surface;
varying float sundew_dew;

float voronoi_cracks(vec2 p) {
    vec2 n = floor(p);
    vec2 f = fract(p);
    float f1 = 8.0;
    float f2 = 8.0;
    for (int j = -1; j <= 1; j++) {
        for (int i = -1; i <= 1; i++) {
            vec2 g = vec2(float(i), float(j));
            vec2 o = vec2(hash21(n + g), hash21(n + g + vec2(37.0, 17.0)));
            vec2 r = g + o - f;
            float d = dot(r, r);
            if (d < f1) { f2 = f1; f1 = d; }
            else if (d < f2) { f2 = d; }
        }
    }
    float crack = f2 - f1;
    return 1.0 - smoothstep(0.0, 0.06, crack);
}

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    sundew_surface = float(UV2.y < -4.5);
    sundew_dew = UV2.x;
}

void fragment() {
    vec3 base = srgb_to_linear(clamp(COLOR.rgb, vec3(0.0), vec3(1.0)));
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
    float isShoot = 0.0;
    if (!FRONT_FACING) NORMAL = -NORMAL;
    vec3 wn = finite_surface_normal((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz, vec3(0.0, 1.0, 0.0));

    // Per-patch grain direction so fibre/lobe streaking is not axis-aligned everywhere; derived from
    // a coarse hash of world position so it stays stable per clump instead of jittering per fragment.
    float grainAngle = hash21(floor(wp * 0.35)) * 6.2831853;
    vec2 grainDir = vec2(cos(grainAngle), sin(grainAngle));
    vec2 grainOrth = vec2(-grainDir.y, grainDir.x);

    if (sundew_surface > 0.5) {
        // Living spoon leaves, tentacles and dew retain their own smooth tissue surface.
        rough = mix(0.48, 0.08, sundew_dew);
    } else if (u_pattern < 0.5) {
        // Photo-scanned moss supplies irregular leaf clusters and contact shadows across the
        // geometric pillows. World-space sampling keeps one patch continuous over tile seams.
        vec3 scan_n;
        vec4 scan = triplanar(moss_col, moss_nrm, moss_rgh,
            world_pos / 0.24, wn, 5.0, scan_n);
        float scan_avg = max(dot(textureLod(moss_col, vec2(0.5), 10.0).rgb,
            vec3(0.2126, 0.7152, 0.0722)), 0.05);
        c *= mix(vec3(1.0), clamp(vec3(dot(scan.rgb, vec3(0.2126, 0.7152, 0.0722)) / scan_avg), vec3(0.25), vec3(1.7)), 0.78);
        // Moss: layered fibrous/leaflet micro-detail at ~1-3mm scale with directional grain per clump,
        // plus tip brightening so individual shoots read instead of a flat stippled paint.
        vec2 pg = vec2(dot(wp, grainDir), dot(wp, grainOrth));
        float fibers = fbm(pg * vec2(3.0, 26.0));
        float leaflets = fbm(pg * vec2(9.0, 70.0) + 11.3);
        float stipple = hash21(floor(wp * 140.0));
        float tips = smoothstep(0.62, 0.92, fbm(pg * vec2(6.0, 50.0) + 5.1));
        c *= 0.82 + 0.16 * fibers + 0.14 * leaflets + 0.08 * (stipple - 0.5);
        // Recessed shoots stay olive; thin growing tips catch chartreuse light without
        // an additive pigment floor that makes shaded moss look self-lit.
        c *= mix(vec3(0.91, 0.94, 0.77), vec3(1.08, 1.12, 0.88), tips);
        // Centimetre-scale tuft cavities break up the continuous carpet's diffuse field.
        float tuft_speck;
        vec4 tuft = moss_clumps(wp * 45.0, tuft_speck);
        float tuft_height = clamp(tuft.x, 0.0, 1.0);
        c *= mix(vec3(0.56, 0.58, 0.43), vec3(1.04, 1.02, 0.88), smoothstep(0.05, 0.85, tuft_height));

        float e = 0.01;
        float h = fibers + leaflets * 0.6;
        vec2 pgx = vec2(dot(wp + vec2(e, 0.0), grainDir), dot(wp + vec2(e, 0.0), grainOrth));
        vec2 pgz = vec2(dot(wp + vec2(0.0, e), grainDir), dot(wp + vec2(0.0, e), grainOrth));
        float dx = (fbm(pgx * vec2(3.0, 26.0)) + fbm(pgx * vec2(9.0, 70.0) + 11.3) * 0.6) - h;
        float dz = (fbm(pgz * vec2(3.0, 26.0)) + fbm(pgz * vec2(9.0, 70.0) + 11.3) * 0.6) - h;
        bump = vec3(-dx, 0.0, -dz) * 1.2 + (scan_n - wn) * 0.65 + vec3(tuft.y, 0.0, tuft.z) * 0.32;

        // A current-state kernel field: isolated growing points expand into
        // neighboring points while dark protonema persists in the valleys.
        if (u_moss_colony_nuclei > 0.01) {
            float crown = UV2.y > 0.05 ? 1.0 : clamp(UV2.x, 0.0, 1.0);
            float strength = u_moss_colony_nuclei >= 4.0 ? 1.0 : 0.45;
            c *= mix(vec3(1.0),
                mix(vec3(0.32, 0.36, 0.31), vec3(1.13, 1.12, 0.96),
                    smoothstep(0.09, 0.88, crown)), strength);
        }

        // Aggregate a broad distribution of microscopic gametophore normals.
        // This keeps the same subpixel appearance without sparkling geometry.
        vec3 porous, porous_n;
        float porous_occ;
        moss_aggregate(c, wn, CAMERA_POSITION_WORLD - world_pos, wp,
            smoothstep(.20, .78, COLOR.a), u_moss_aggregate,
            porous, porous_n, porous_occ);
        c = porous;
        bump += (porous_n - wn) * 0.38;
        // Crevice AO: darken low-fibre troughs between shoot clusters.
        ao = mix(0.72, 1.0, smoothstep(-0.1, 0.5, fibers)) * mix(0.65, 1.0, smoothstep(0.0, 0.65, tuft_height));
        ao *= porous_occ;
        rough = clamp(mix(0.96, scan.a, 0.55), 0.80, 0.97);
        // Light-dependent fibre scattering and thin-phyllid gametophore transmittance.
        // Basal mat uses subtle fibre scattering; elevated 3D gametophores catch
        // translucent golden-green backlight and velvety grazing sheen.
        float shootT = clamp((UV2.y - 0.05) / 0.95, 0.0, 1.0);
        isShoot = smoothstep(0.05, 0.20, UV2.y);
        vec3 matBacklight = base * vec3(0.075, 0.11, 0.025) * (0.35 + 0.65 * tips) * coverMask;
        vec3 shootBacklight = base * vec3(0.28, 0.48, 0.08) * (0.35 + 0.65 * shootT);
        BACKLIGHT = mix(matBacklight, shootBacklight, isShoot);
        RIM = mix(0.065 * tips * coverMask, mix(0.15, 0.38, shootT), isShoot);
        RIM_TINT = mix(0.75, 0.65, isShoot);
    } else {
        // Lichen: scanned thallus colour + high-frequency tangent-space normal and cavity AO
        // from LichenThallus.png, plus procedural Voronoi areole cracks.
        vec3 thallus = texture(lichen_col, wp / 0.22).rgb;
        c *= clamp(vec3(0.42) + thallus * 1.25, vec3(0.3), vec3(1.55));

        float thallus_lum = dot(thallus, vec3(0.299, 0.587, 0.114));
        float e = 0.004;
        float hx = dot(texture(lichen_col, (wp + vec2(e, 0.0)) / 0.22).rgb, vec3(0.299, 0.587, 0.114));
        float hz = dot(texture(lichen_col, (wp + vec2(0.0, e)) / 0.22).rgb, vec3(0.299, 0.587, 0.114));
        bump = vec3((thallus_lum - hx) * 6.0, 0.0, (thallus_lum - hz) * 6.0);

        float cracks = voronoi_cracks(wp * 65.0);
        float cx = voronoi_cracks((wp + vec2(e, 0.0)) * 65.0);
        float cz = voronoi_cracks((wp + vec2(0.0, e)) * 65.0);
        bump += vec3((cracks - cx) * 1.6, 0.0, (cracks - cz) * 1.6);

        // Dusty soredia / cortex micro-roughness; waxy apothecia discs stay smooth.
        float soredia_mask = fbm(wp * 80.0);
        rough = mix(0.68, 0.92, soredia_mask);
        float apothecia_mask = smoothstep(0.45, 0.75, thallus.r - thallus.g * 0.6);
        rough = mix(rough, 0.28, apothecia_mask);

        // Cavity AO: thallus height + crack crevices.
        float thallus_ao = mix(0.45, 1.0, thallus_lum);
        thallus_ao *= mix(1.0, 0.35, cracks);
        ao = clamp(thallus_ao * COLOR.a, 0.15, 1.0);
        AO = ao;
        AO_LIGHT_AFFECT = 0.75;
    }

    // Rim/interior relief: brighten and lift shading slightly where coverage is thin (rim), consistent
    // with the C#-side rim/interior colour split, and darken slightly toward full interior density.
    c *= mix(1.05, 0.96, coverMask);
    c *= ao;

    vec3 n = finite_surface_normal(wn + bump * 0.5, wn);

    // Colony identity under the dark grow-light rig: lift and saturate slightly so moss/lichen patches stand out
    // from the procedural ground/log moss (which is also suppressed under colonies via cover_tex).
    if (sundew_surface < 0.5) {
        float cl = dot(c, vec3(0.2126, 0.7152, 0.0722));
        c = u_pattern < 0.5
            ? clamp(mix(vec3(cl), c, mix(0.88, 1.0, isShoot)), vec3(0.0), vec3(1.0))
            : clamp(mix(vec3(cl), c, 1.18) * 1.30, vec3(0.0), vec3(1.0));
    }
    ALBEDO = c;
    ROUGHNESS = sundew_surface > 0.5 || u_pattern < 0.5 ? rough : rough * 0.88;
    SPECULAR = sundew_surface > 0.5 ? mix(0.35, 0.85, sundew_dew) : (u_pattern < 0.5 ? 0.22 : 0.28);
    NORMAL = finite_surface_normal((VIEW_MATRIX * vec4(n, 0.0)).xyz, NORMAL);
}
