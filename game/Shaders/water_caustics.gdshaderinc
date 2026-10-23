// Shared 3D chromatic refractive caustics for submerged beds (terrain, rocks, logs).
// Include after surface.gdshaderinc in the host shader. The host must provide TIME
// (or pass an explicit clock) and the water_table uniform where applicable.

vec2 wc_hash22(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * vec3(443.897, 441.423, 437.195));
    p3 += dot(p3, p3.yzx + 19.19);
    return fract((p3.xx + p3.yz) * p3.zy);
}

// Distance from p to the nearest Voronoi cell edge (small near folds -> focusing).
float wc_voronoi_edge(vec2 p) {
    vec2 ip = floor(p);
    float d1 = 1e9, d2 = 1e9;
    for (int j = -1; j <= 1; j++) {
        for (int i = -1; i <= 1; i++) {
            vec2 g = ip + vec2(float(i), float(j));
            vec2 o = wc_hash22(g);
            vec2 r = g + o - p;
            float d = dot(r, r);
            if (d < d1) { d2 = d1; d1 = d; }
            else if (d < d2) { d2 = d; }
        }
    }
    return sqrt(max(d2, 0.0)) - sqrt(max(d1, 0.0));
}

// Dual-advected wave displacement field for the wavefront Jacobian term.
vec2 wc_wave_offset(vec2 p, float t) {
    vec2 a = p + vec2(t * 0.37, -t * 0.29);
    vec2 b = vec2(cos(0.7) * p.x - sin(0.7) * p.y, sin(0.7) * p.x + cos(0.7) * p.y)
           + vec2(-t * 0.23, t * 0.31);
    return vec2(
        sin(a.x * 2.6 + a.y * 1.4) + sin(b.x * 3.1 - b.y * 1.9),
        cos(a.y * 2.3 - a.x * 1.1) + cos(b.y * 2.8 + b.x * 1.6)
    ) * 0.18;
}

// Wavefront Jacobian determinant focusing: 1 / |det(d(bed)/d(surface))|.
float wc_jacobian_caustic(vec2 p, float t) {
    float e = 0.004;
    vec2 dx = (wc_wave_offset(p + vec2(e, 0.0), t) - wc_wave_offset(p - vec2(e, 0.0), t)) / (2.0 * e);
    vec2 dy = (wc_wave_offset(p + vec2(0.0, e), t) - wc_wave_offset(p - vec2(0.0, e), t)) / (2.0 * e);
    float det = (1.0 + dx.x) * (1.0 + dy.y) - dx.y * dy.x;
    float c = 1.0 / max(abs(det), 0.08);
    return clamp(c, 0.0, 3.0);
}

float wc_caustic_layer(vec2 uv, float t, float scale) {
    vec2 p = uv * scale;
    // Dual advection: two independent time-offset Voronoi fields.
    vec2 adv1 = p + vec2(t * 0.45, -t * 0.33);
    vec2 adv2 = p * 1.37 + vec2(-t * 0.37, t * 0.41);
    float e1 = wc_voronoi_edge(adv1);
    float e2 = wc_voronoi_edge(adv2);
    float focusing = 1.0 / (e1 * 0.6 + 0.05) * 1.0 / (e2 * 0.9 + 0.08);
    focusing = clamp(focusing, 0.0, 8.0);
    float jac = wc_jacobian_caustic(p, t);
    return clamp(focusing * 0.06 * jac, 0.0, 2.0);
}

// Evaluate chromatic refractive caustics on a submerged surface.
// world_pos: surface world position; world_normal: surface world normal;
// water_surface_y: water surface height; light_dir: world-space direction to sun;
// time: animation clock.
vec3 evaluate_submerged_caustics(vec3 world_pos, vec3 world_normal, float water_surface_y, vec3 light_dir, float time) {
    vec3 refracted = refract(-light_dir, vec3(0.0, 1.0, 0.0), 1.0 / 1.333);
    if (abs(refracted.y) < 1e-4) {
        return vec3(0.0);
    }
    float depth = water_surface_y - world_pos.y;
    if (depth <= 0.0) {
        return vec3(0.0);
    }
    float denom = max(0.15, -refracted.y);
    vec2 uv_surf = world_pos.xz + refracted.xz * (water_surface_y - world_pos.y) / denom;

    // Chromatic dispersion: three slightly offset UV samples for R, G, B.
    vec2 disp = vec2(0.018, -0.012);
    float cr = wc_caustic_layer(uv_surf - disp, time, 1.0);
    float cg = wc_caustic_layer(uv_surf, time, 1.05);
    float cb = wc_caustic_layer(uv_surf + disp, time, 0.95);

    float normal_align = max(0.0, dot(world_normal, -refracted));
    float depth_env = smoothstep(0.003, 0.035, depth) * exp(-depth * 1.8);

    vec3 c = vec3(cr, cg, cb) * normal_align * depth_env;
    c *= vec3(0.95, 1.0, 1.05);
    return clamp(c * 0.35, vec3(0.0), vec3(2.0));
}