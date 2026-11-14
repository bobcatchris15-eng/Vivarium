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
#include "res://Shaders/repr_skyvis.gdshaderinc"

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
// SP3: per-species surface style for the sheet (1 floodlace, 2 pearl cushion, 3 velvetweave, 4 bogglass),
// encoded by the C# tile builder as UV2.y = -0.5 * style. 0 = legacy generic moss.
varying flat float mat_style;

// Voronoi helper: x = distance to nearest feature, y = per-cell hash, zw = vector point -> feature.
vec4 vor_cell(vec2 p) {
    vec2 n = floor(p);
    vec2 f = fract(p);
    float f1 = 8.0;
    vec2 br = vec2(0.0);
    float bh = 0.0;
    for (int j = -1; j <= 1; j++) {
        for (int i = -1; i <= 1; i++) {
            vec2 g = vec2(float(i), float(j));
            vec2 o = vec2(hash21(n + g), hash21(n + g + vec2(37.0, 17.0)));
            vec2 r = g + o - f;
            float d = dot(r, r);
            if (d < f1) { f1 = d; br = r; bh = hash21(n + g + vec2(11.0, 5.0)); }
        }
    }
    return vec4(sqrt(f1), bh, br);
}

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
    sundew_surface = float(UV2.y < -4.9 && UV2.y > -5.1);
    sundew_dew = UV2.x;
    mat_style = ((UV2.y < -0.25 && UV2.y > -4.75) || (UV2.y < -5.75 && UV2.y > -8.0)) ? floor(-UV2.y * 2.0 + 0.5) : 0.0;
}

void fragment() {
    vec3 base = srgb_to_linear(clamp(COLOR.rgb, vec3(0.0), vec3(1.0)));
    vec2 wp = world_pos.xz;
    float cam_dist = length(CAMERA_POSITION_WORLD - world_pos);
    // Micro-leaflets, clump Voronoi and derivative noise are subpixel past ~5 m.
    float detail_lod = 1.0 - smoothstep(2.0, 5.0, cam_dist);
    vec3 to_cam = CAMERA_POSITION_WORLD - world_pos;

    // Irregular, noise-jittered edge feather: interior (alpha==1) never discards; the ~1-cell
    // rim band (alpha 0.25..0.75) breaks up raggedly instead of following the cell grid.
    float edgeNoise = fbm(wp * 4.0) * 0.25 + fbm(wp * 16.0) * 0.30 + fbm(wp * 70.0) * 0.45;
    float threshold = clamp(0.5 + (edgeNoise - 0.5) * 2.2, 0.05, 0.95);
    float style = mat_style;
    bool styled = style > 0.5;
    float coverMask = smoothstep(threshold - 0.15, threshold + 0.15, COLOR.a);
    float edge_fade = 1.0;
    if (styled) {
        // Soft, dithered mat edge: the sheet thins into the ground over several centimetres instead of
        // ending on a hard cut. Hashed alpha stays in the opaque pass (no sorting, no blend cost).
        float en = fbm(wp * 7.0) * 0.5 + fbm(wp * 31.0) * 0.5;
        edge_fade = smoothstep(0.10, 0.92, COLOR.a + (en - 0.5) * 0.7);
        if (edge_fade < 0.03) discard;
        coverMask = edge_fade;
        ALPHA = edge_fade;
        ALPHA_HASH_SCALE = 1.0;
    } else if (COLOR.a < threshold) discard;

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
        rough = clamp(mix(0.96, scan.a, 0.55), 0.80, 0.97);
        bump = (scan_n - wn) * 0.65;

        vec2 pg = vec2(dot(wp, grainDir), dot(wp, grainOrth));
        float fibers = 0.5;
        float leaflets = 0.5;
        float tips = 0.0;
        float tuft_height = 0.5;
        float tuft_speck = 0.0;
        float porous_occ = 1.0;

        // ------------------------------------------------------------------
        // Close-range bryophyte micro-canopy: layered leaflet fibres, gametophore
        // clumps, optical porosity and damp shoot cavities. All of it is subpixel
        // beyond ~5 m, so it is evaluated only while detail_lod > 0.01.
        // ------------------------------------------------------------------
        if (detail_lod > 0.01 && !styled) {
            float d = detail_lod;
            fibers = fbm(pg * vec2(3.0, 26.0));
            leaflets = fbm(pg * vec2(9.0, 70.0) + 11.3);
            float stipple = hash21(floor(wp * 140.0));
            tips = smoothstep(0.62, 0.92, fbm(pg * vec2(6.0, 50.0) + 5.1));
            c *= mix(vec3(1.0),
                vec3(0.82 + 0.16 * fibers + 0.14 * leaflets + 0.08 * (stipple - 0.5)), d);
            // Recessed shoots stay olive; thin growing tips catch chartreuse light.
            c *= mix(vec3(1.0),
                mix(vec3(0.91, 0.94, 0.77), vec3(1.08, 1.12, 0.88), tips), d);
            // Centimetre-scale tuft cavities break up the continuous carpet's diffuse field.
            vec4 tuft = moss_clumps(wp * 45.0, tuft_speck);
            tuft_height = clamp(tuft.x, 0.0, 1.0);
            c *= mix(vec3(1.0),
                mix(vec3(0.56, 0.58, 0.43), vec3(1.04, 1.02, 0.88),
                    smoothstep(0.05, 0.85, tuft_height)), d);

            float e = 0.01;
            float h = fibers + leaflets * 0.6;
            vec2 pgx = vec2(dot(wp + vec2(e, 0.0), grainDir), dot(wp + vec2(e, 0.0), grainOrth));
            vec2 pgz = vec2(dot(wp + vec2(0.0, e), grainDir), dot(wp + vec2(0.0, e), grainOrth));
            float dx = (fbm(pgx * vec2(3.0, 26.0)) + fbm(pgx * vec2(9.0, 70.0) + 11.3) * 0.6) - h;
            float dz = (fbm(pgz * vec2(3.0, 26.0)) + fbm(pgz * vec2(9.0, 70.0) + 11.3) * 0.6) - h;
            bump += (vec3(-dx, 0.0, -dz) * 1.2 + vec3(tuft.y, 0.0, tuft.z) * 0.32) * d;

            // Optical porosity: interstitial pore cavities between gametophore
            // clumps absorb light, so the mat reads as deep velvety shoots rather
            // than a painted green plate.
            float pore = smoothstep(0.58, 0.88, 1.0 - tuft_height)
                * smoothstep(0.18, 0.62, fibers);
            c *= mix(1.0, 0.42, pore * 0.85);
            ao *= mix(1.0, 0.55, pore);

            // Damp water film sits in the lower shoot cavities while exposed tips stay crisp.
            float cavity_moisture = (1.0 - smoothstep(0.30, 0.85, tuft_height)) * d;
            c *= mix(1.0, 0.86, cavity_moisture * 0.55);
            rough = mix(rough, 0.42, cavity_moisture * 0.55);

            // Aggregate a broad distribution of microscopic gametophore normals.
            // This keeps the same subpixel appearance without sparkling geometry.
            vec3 porous, porous_n;
            moss_aggregate(c, wn, to_cam, wp,
                smoothstep(.20, .78, COLOR.a), u_moss_aggregate * d,
                porous, porous_n, porous_occ);
            c = porous;
            bump += (porous_n - wn) * 0.38 * d;
        }

        // Macro surface beyond 5 m: one low-frequency variation keeps the mat
        // from flattening into a single albedo without paying for micro noise.
        float macro = vnoise(wp * 3.2 + grainDir * 2.7);
        c *= mix(0.93, 1.07, macro);

        // A current-state kernel field: isolated growing points expand into
        // neighboring points while dark protonema persists in the valleys.
        if (u_moss_colony_nuclei > 0.01 && !styled) {
            float crown = UV2.y > 0.05 ? 1.0 : clamp(UV2.x, 0.0, 1.0);
            float strength = u_moss_colony_nuclei >= 4.0 ? 1.0 : 0.45;
            c *= mix(vec3(1.0),
                mix(vec3(0.32, 0.36, 0.31), vec3(1.13, 1.12, 0.96),
                    smoothstep(0.09, 0.88, crown)), strength);
        }

        // Crevice AO: darken low-fibre troughs between shoot clusters.
        ao = mix(0.72, 1.0, smoothstep(-0.1, 0.5, fibers))
            * mix(0.65, 1.0, smoothstep(0.0, 0.65, tuft_height));
        ao *= mix(1.0, porous_occ, detail_lod);
        // Light-dependent fibre scattering and thin-phyllid gametophore transmittance.
        // Basal mat uses subtle fibre scattering; elevated 3D gametophores catch
        // translucent golden-green backlight and velvety grazing sheen.
        float shootT = clamp((UV2.y - 0.05) / 0.95, 0.0, 1.0);
        isShoot = smoothstep(0.05, 0.20, UV2.y);
        vec3 matBacklight = base * vec3(0.075, 0.11, 0.025) * (0.35 + 0.65 * tips) * coverMask;
        vec3 shootBacklight = base * vec3(0.28, 0.48, 0.08) * (0.35 + 0.65 * shootT);
        BACKLIGHT = mix(matBacklight, shootBacklight, isShoot);
        // Grazing-angle phyllid fuzz / retroreflective velvet rim.
        float velvet = pow(1.0 - clamp(dot(wn, normalize(to_cam)), 0.0, 1.0), 3.0) * detail_lod;
        c *= 1.0 + velvet * 0.10;
        RIM = mix(0.065 * tips * coverMask, mix(0.15, 0.38, shootT), isShoot) + velvet * 0.18;
        RIM_TINT = mix(0.75, 0.65, isShoot);

        if (styled) {
            float d = detail_lod;
            float lum_base = dot(base, vec3(0.2126, 0.7152, 0.0722));
            ao = 0.9;
            if (style > 4.5 && style < 5.5) {
                // Plain tissue for blades, domes and setae: soft fine grain, translucent warm backlight.
                c *= 0.88 + 0.24 * vnoise(wp * 140.0);
                ao = 1.0;
                rough = 0.62;
                RIM = 0.16; RIM_TINT = 0.5;
                BACKLIGHT = base * vec3(0.22, 0.30, 0.06);
            } else if (style > 5.5) {
                // SP6 mosses: subtle low-frequency shading only; the shoots and cushions are real geometry.
                float lo = vnoise(wp * 6.0 + 1.7);
                float mid = vnoise(wp * 23.0 + 5.3);
                float fine = vnoise(wp * 95.0);
                float stream = fbm(vec2(dot(wp, grainDir) * 12.0, dot(wp, grainOrth) * 110.0));
                float ndv = clamp(dot(wn, normalize(to_cam)), 0.0, 1.0);
                float sheen = pow(1.0 - ndv, 2.5);
                if (style < 6.5) {
                    // Pinequilt: dry needle-litter quilt. Soft 3 cm pillows, warm brown seams, olive-khaki nap.
                    vec4 vc = vor_cell(wp / 0.032 + 2.3);
                    float pillow = 1.0 - smoothstep(0.0, 0.8, vc.x);
                    float seam = smoothstep(0.55, 1.0, vc.x);
                    c *= (0.82 + 0.30 * lo) * mix(0.86, 1.06, pillow) * (0.90 + 0.18 * fine);
                    c = mix(c, c * vec3(0.70, 0.52, 0.38), seam * 0.50 * max(d, 0.35));
                    c = mix(c, c * vec3(1.15, 0.98, 0.70), smoothstep(0.55, 0.8, mid) * 0.35);
                    bump += vec3(-vc.z, 0.0, -vc.w) * 0.45 * pillow * d;
                    ao = mix(0.70, 1.0, 1.0 - seam * 0.8);
                    rough = 0.90;
                    RIM = 0.12; RIM_TINT = 0.6;
                    BACKLIGHT = base * vec3(0.12, 0.12, 0.03);
                } else if (style < 7.5) {
                    // Quillfeather: sage feather felt. Fine combed streaks, cool/warm green drift.
                    c *= (0.78 + 0.40 * stream) * (0.88 + 0.22 * lo);
                    c = mix(c, c * vec3(0.80, 1.00, 0.92), smoothstep(0.35, 0.7, mid) * 0.5);
                    c = mix(c, c * vec3(1.15, 1.05, 0.70), smoothstep(0.6, 0.85, lo) * 0.45);
                    ao = 0.90;
                    rough = 0.86;
                    RIM = 0.18 + 0.15 * sheen; RIM_TINT = 0.7;
                    BACKLIGHT = base * vec3(0.10, 0.20, 0.06);
                    bump += vec3(stream - 0.5, 0.0, vnoise(wp * 80.0) - 0.5) * 0.25 * d;
                } else if (style < 8.5) {
                    // Rustbellow: billowing rust-brown turf, umber hollows, olive-green living fringe.
                    float billow = vnoise(wp * 11.0 + 9.0);
                    float hollow = smoothstep(0.30, 0.70, vnoise(wp * 5.0 + 3.0));
                    c *= (0.70 + 0.50 * billow) * (0.88 + 0.24 * mid);
                    c = mix(c, c * vec3(0.55, 0.40, 0.32), hollow * 0.55);
                    c = mix(c, vec3(0.20, 0.27, 0.10) * (0.8 + 0.4 * mid), smoothstep(0.62, 0.82, lo) * 0.55);
                    c *= 0.90 + 0.20 * fine;
                    bump += vec3(vnoise(wp * 11.0 + 9.4) - billow, 0.0, vnoise(wp * 11.0 + 9.8) - billow) * 1.4 * d;
                    ao = mix(0.62, 1.0, billow);
                    rough = 0.80;
                    RIM = 0.14; RIM_TINT = 0.45;
                    BACKLIGHT = base * vec3(0.30, 0.12, 0.03);
                } else if (style < 9.5) {
                    // Silkgleam: silky gold-olive, fine combed lustre with strong grazing sheen.
                    float comb = fbm(vec2(dot(wp, grainDir) * 4.0, dot(wp, grainOrth) * 220.0));
                    c *= (0.74 + 0.44 * comb) * (0.90 + 0.20 * lo);
                    c = mix(c, c * vec3(0.72, 0.85, 0.50), smoothstep(0.55, 0.85, mid) * 0.45);
                    c += base * vec3(0.50, 0.38, 0.10) * sheen * (0.4 + 0.6 * comb);
                    ao = 0.92;
                    rough = 0.42;
                    RIM = 0.30 + 0.30 * sheen; RIM_TINT = 0.8;
                    BACKLIGHT = base * vec3(0.22, 0.18, 0.04);
                    bump += vec3(comb - 0.5, 0.0, vnoise(wp * 140.0) - 0.5) * 0.20 * d;
                } else if (style < 12.5) {
                    // Stoneknot: tight grey-green knots on stone; irregular warped cells, stone showing in seams.
                    vec2 wq = wp + 0.006 * vec2(vnoise(wp * 40.0), vnoise(wp * 40.0 + 4.0));
                    vec4 vc = vor_cell(wq / 0.014 + 6.1);
                    float knot = 1.0 - smoothstep(0.0, 0.8, vc.x);
                    float seam = smoothstep(0.55, 0.98, vc.x);
                    c *= (0.80 + 0.30 * lo) * mix(0.72, 1.10, knot) * (0.88 + 0.24 * vc.y);
                    c = mix(c, vec3(0.28, 0.29, 0.26), seam * 0.65 * max(d, 0.3));
                    c = mix(c, c * vec3(1.10, 1.12, 0.95) + 0.03, smoothstep(0.6, 0.85, mid) * 0.35);
                    bump += vec3(-vc.z, 0.0, -vc.w) * 0.8 * knot * d;
                    ao = mix(0.55, 1.0, 1.0 - seam * 0.9);
                    rough = 0.92;
                    RIM = 0.10; RIM_TINT = 0.3;
                    BACKLIGHT = base * vec3(0.04, 0.08, 0.03);
                } else {
                    // Slickmantle: wet olive mantle. Smooth glossy film, gentle mottling, lobes carry the relief.
                    c *= (0.80 + 0.32 * lo) * (0.92 + 0.14 * mid);
                    c = mix(c, c * vec3(0.78, 0.92, 0.70), smoothstep(0.5, 0.8, vnoise(wp * 17.0 + 2.0)) * 0.5);
                    ao = 0.94;
                    rough = 0.28 + 0.2 * mid;
                    RIM = 0.16; RIM_TINT = 0.4;
                    BACKLIGHT = base * vec3(0.16, 0.22, 0.05);
                    bump += vec3(vnoise(wp * 30.0) - 0.5, 0.0, vnoise(wp * 30.0 + 7.0) - 0.5) * 0.4 * d;
                }
            } else if (style < 1.5) {
                // Floodlace: lacy wet sheet. Flow-aligned warped streams, fine holes that read as dark water
                // between strands, glossy film.
                vec2 wq = wp + 0.012 * vec2(fbm(wp * 14.0), fbm(wp * 14.0 + 7.0));
                float stream = fbm(vec2(dot(wq, grainDir) * 9.0, dot(wq, grainOrth) * 70.0));
                float lace = smoothstep(0.50, 0.68, fbm(wq * 38.0 + 3.0)) * d;
                float vein = smoothstep(0.35, 0.5, abs(fbm(wq * 20.0) - 0.5) + 0.0) * d;
                float sheetTone = 0.75 + 0.5 * vnoise(wp * 9.0 + 1.0);
                c *= sheetTone * mix(1.0, 0.55 + 0.9 * stream, d);
                c = mix(c, c * vec3(0.25, 0.42, 0.40), lace * 0.85);
                c *= 1.0 - 0.18 * vein;
                ao = mix(0.92, 0.55, lace);
                float e2 = 0.006;
                vec2 gx = vec2(dot(wq + vec2(e2, 0.0), grainDir), dot(wq + vec2(e2, 0.0), grainOrth));
                vec2 gz = vec2(dot(wq + vec2(0.0, e2), grainDir), dot(wq + vec2(0.0, e2), grainOrth));
                float h0 = stream;
                float hx = fbm(vec2(gx.x * 9.0, gx.y * 70.0));
                float hz = fbm(vec2(gz.x * 9.0, gz.y * 70.0));
                bump += vec3(h0 - hx, 0.0, h0 - hz) * 1.4 * d;
                rough = mix(0.55, 0.28, 0.5 + 0.5 * stream);
                RIM = 0.10; RIM_TINT = 0.5;
                BACKLIGHT = base * vec3(0.05, 0.22, 0.14);
            } else if (style < 2.5) {
                // Pearl cushion: tight 1.6 cm domes, dark seams, frosted pearl crowns.
                vec4 vc = vor_cell(wp / 0.016 + 3.7);
                float dome = 1.0 - smoothstep(0.0, 0.75, vc.x);
                float seam = smoothstep(0.50, 0.95, vc.x);
                float crown = 1.0 - smoothstep(0.0, 0.30, vc.x);
                float cellTone = 0.85 + 0.3 * vc.y;
                c *= mix(1.0, cellTone * mix(0.68, 1.12, dome), max(d, 0.25));
                c = mix(c, vec3(0.50, 0.55, 0.40), crown * 0.45 * max(d, 0.25));
                c *= 1.0 - 0.45 * seam * d;
                bump += vec3(-vc.z, 0.0, -vc.w) * 0.9 * dome * d;
                ao = mix(0.62, 1.0, 1.0 - seam * d);
                rough = mix(0.82, 0.55, crown);
                RIM = 0.30 * crown; RIM_TINT = 0.1;
                BACKLIGHT = base * vec3(0.05, 0.08, 0.03);
            } else if (style < 3.5) {
                // Velvetweave: low combed nap, soft woven sheen, warm gold glow on grazing light.
                float nap = fbm(vec2(dot(wp, grainDir) * 6.0, dot(wp, grainOrth) * 160.0));
                float patch_g = vnoise(wp * 7.0 + 2.0);
                float weave = 0.5 + 0.5 * sin(dot(wp, grainOrth) * 520.0) * sin(dot(wp, grainDir) * 160.0 + nap * 3.0);
                c *= mix(1.0, 0.80 + 0.35 * nap + 0.06 * weave, d);
                c = mix(c, c * vec3(1.30, 1.15, 0.65), smoothstep(0.52, 0.80, patch_g) * 0.8);
                float ndv = clamp(dot(wn, normalize(to_cam)), 0.0, 1.0);
                float sheen = pow(1.0 - ndv, 2.2);
                c += base * vec3(0.55, 0.45, 0.14) * sheen * (0.4 + 0.6 * nap) * 0.7;
                ao = 0.94;
                rough = 0.97;
                RIM = 0.25 + 0.3 * sheen; RIM_TINT = 0.7;
                BACKLIGHT = base * vec3(0.18, 0.14, 0.03);
                bump += vec3(vnoise(wp * 90.0) - 0.5, 0.0, vnoise(wp * 90.0 + 9.0) - 0.5) * 0.25 * d;
            } else {
                // Bogglass: wet glassy sphagnum. 2.2 cm capitula with dimpled centres, russet hollows, gloss.
                vec4 vc = vor_cell(wp / 0.022 + 8.1);
                float dome = 1.0 - smoothstep(0.0, 0.85, vc.x);
                float dimple = 1.0 - smoothstep(0.0, 0.16, vc.x);
                float gap = smoothstep(0.55, 0.95, vc.x);
                float hollow = smoothstep(0.45, 0.72, vnoise(wp * 5.0 + 4.0));
                c *= mix(1.0, (0.78 + 0.35 * vc.y) * mix(0.75, 1.12, dome), max(d, 0.3));
                c = mix(c, c * vec3(1.12, 0.70, 0.55), hollow * 0.40);
                c *= 1.0 - 0.35 * dimple * d - 0.30 * gap * d;
                bump += vec3(-vc.z, 0.0, -vc.w) * 0.8 * dome * d;
                ao = mix(0.7, 1.0, 1.0 - gap * d);
                rough = mix(0.30, 0.16, dome);
                RIM = 0.18; RIM_TINT = 0.3;
                BACKLIGHT = base * vec3(0.30, 0.34, 0.06);
            }
            // Thin, wet-looking edge: fade colour toward damp ground as the sheet thins.
            c = mix(c * 0.8, c, smoothstep(0.15, 0.85, edge_fade));
        }
    } else {
        // Lichen: scanned thallus colour + high-frequency tangent-space normal and cavity AO
        // from LichenThallus.png, plus procedural Voronoi areole cracks.
        vec3 thallus = texture(lichen_col, wp / 0.22).rgb;
        c *= clamp(vec3(0.42) + thallus * 1.25, vec3(0.3), vec3(1.55));

        float thallus_lum = dot(thallus, vec3(0.299, 0.587, 0.114));
        float cracks = 0.0;
        bump = vec3(0.0);
        if (detail_lod > 0.01) {
            float d = detail_lod;
            float e = 0.004;
            float hx = dot(texture(lichen_col, (wp + vec2(e, 0.0)) / 0.22).rgb, vec3(0.299, 0.587, 0.114));
            float hz = dot(texture(lichen_col, (wp + vec2(0.0, e)) / 0.22).rgb, vec3(0.299, 0.587, 0.114));
            bump = vec3((thallus_lum - hx) * 6.0, 0.0, (thallus_lum - hz) * 6.0) * d;

            cracks = voronoi_cracks(wp * 65.0);
            float cx = voronoi_cracks((wp + vec2(e, 0.0)) * 65.0);
            float cz = voronoi_cracks((wp + vec2(0.0, e)) * 65.0);
            bump += vec3((cracks - cx) * 1.6, 0.0, (cracks - cz) * 1.6) * d;

            // Dusty soredia / cortex micro-roughness; waxy apothecia discs stay smooth.
            float soredia_mask = fbm(wp * 80.0);
            rough = mix(rough, mix(0.68, 0.92, soredia_mask), d);
            float apothecia_mask = smoothstep(0.45, 0.75, thallus.r - thallus.g * 0.6);
            rough = mix(rough, 0.28, apothecia_mask * d);
        }

        // Cavity AO: thallus height + crack crevices. Past ~5 m only the broad
        // thallus luminance survives, with no Voronoi derivative samples.
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
    if (repr_skyvis_strength > 0.0) {
        float repr_ao = repr_sky_ao(world_pos, n, isShoot > 0.5);
        AO *= repr_ao;
        AO_LIGHT_AFFECT = min(AO_LIGHT_AFFECT, REPR_SKYVIS_LIGHT_AFFECT);
        EMISSION = repr_sky_bounce(ALBEDO, n, repr_ao, isShoot > 0.5);
    }
}
