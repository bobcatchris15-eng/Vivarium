// Shared helpers for the photo-scanned (CC0, ambientCG) surface materials.
// Normal maps are OpenGL convention (+Y up) and imported as normal maps, so the sampled .xy is the
// tangent-space normal in [0,1]; unpack_normal returns it in [-1,1] with z rebuilt.

vec3 srgb_to_linear(vec3 c) {
    // MSAA interpolation may extrapolate vertex attributes outside their source
    // range. mix() does not protect the unused fractional-pow branch from NaN.
    c = max(c, vec3(0.0));
    return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c));
}

float hash21(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }
float vnoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash21(i), hash21(i + vec2(1, 0)), u.x), mix(hash21(i + vec2(0, 1)), hash21(i + vec2(1, 1)), u.x), u.y);
}
float fbm(vec2 p) { float s = 0.0, a = 0.5; for (int i = 0; i < 4; i++) { s += a * vnoise(p); p *= 2.03; a *= 0.5; } return s; }

vec3 unpack_normal(vec3 t) { vec2 xy = t.xy * 2.0 - 1.0; return vec3(xy, sqrt(max(0.0, 1.0 - dot(xy, xy)))); }

// Interpolated/derivative frames may collapse on caps or subpixel triangles.
// Both comparisons reject non-finite values as well as zero-length vectors.
vec3 finite_surface_normal(vec3 value, vec3 fallback) {
    float magnitude_squared = dot(value, value);
    if (magnitude_squared > 1e-12 && magnitude_squared < 1e12)
        return value * inversesqrt(magnitude_squared);
    float fallback_squared = dot(fallback, fallback);
    return fallback_squared > 1e-12 && fallback_squared < 1e12
        ? fallback * inversesqrt(fallback_squared) : vec3(0.0, 1.0, 0.0);
}

// Tangent-space normal back into the [0,1] encoding NORMAL_MAP expects.
vec3 pack_normal(vec3 n) { n = normalize(n); return vec3(n.xy * 0.5 + 0.5, 1.0); }

// Rotates a uv by an angle (used to break up texture repetition).
vec2 rot2(vec2 uv, float a) { float c = cos(a), s = sin(a); return vec2(c * uv.x - s * uv.y, s * uv.x + c * uv.y); }

// Screen-space cotangent frame normal mapping (OpenGL/Godot convention: +Y up in texture -> -V in UV).
// detail is the tangent-space normal (detail.z pointing outward along geom_normal).
vec3 cotangent_normal(vec3 geom_normal, vec3 p_world, vec2 uv, vec3 detail) {
    vec3 px = dFdx(p_world), py = dFdy(p_world);
    vec2 ux = dFdx(uv), uy = dFdy(uv);
    vec3 r1 = cross(py, geom_normal), r2 = cross(geom_normal, px);
    vec3 t = r1 * ux.x + r2 * uy.x;
    vec3 b = r1 * ux.y + r2 * uy.y;
    float inv = inversesqrt(max(max(dot(t, t), dot(b, b)), 1e-12));
    return finite_surface_normal(geom_normal * detail.z + (t * detail.x - b * detail.y) * inv, geom_normal);
}

// Orthonormal tangent frame for cylindrical stems and branches.
// Compensates for non-isotropic UV aspect ratios (circumference vs length),
// wraps derivatives seamlessly across the 0..1 tube seam, and aligns
// the bitangent with the growth axis (+V along stem).
vec3 stem_surface_normal(vec3 geom_normal, vec3 p_world, vec2 uv, vec3 detail) {
    vec3 px = dFdx(p_world), py = dFdy(p_world);
    vec2 ux = dFdx(uv), uy = dFdy(uv);
    ux.x -= round(ux.x);
    uy.x -= round(uy.x);
    vec3 r1 = cross(py, geom_normal);
    vec3 r2 = cross(geom_normal, px);
    vec3 t = r1 * ux.x + r2 * uy.x;
    vec3 b = r1 * ux.y + r2 * uy.y;
    // Caps and subpixel stems can have singular UV derivatives. Construct a
    // valid fallback before Gram-Schmidt; never normalize a zero projection.
    vec3 reference = abs(geom_normal.y) < 0.9 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
    vec3 fallback_t = normalize(cross(geom_normal, reference));
    vec3 projected_t = t - geom_normal * dot(geom_normal, t);
    float lt = length(projected_t);
    vec3 T = lt > 1e-7 ? projected_t / lt : fallback_t;
    vec3 projected_b = b - geom_normal * dot(geom_normal, b) - T * dot(T, b);
    float lb = length(projected_b);
    vec3 B = lb > 1e-7 ? projected_b / lb : cross(geom_normal, T);
    float det = ux.x * uy.y - ux.y * uy.x;
    if (det < 0.0) T = -T;
    vec3 mapped = geom_normal * detail.z + T * detail.x + B * detail.y;
    float mapped_length = length(mapped);
    return mapped_length > 1e-7 ? mapped / mapped_length : geom_normal;
}


vec2 hash22(vec2 p) {
    vec3 p3 = fract(vec3(p.xyx) * vec3(443.897, 441.423, 437.195));
    p3 += dot(p3, p3.yzx + 19.19);
    return fract((p3.xx + p3.yz) * p3.zy);
}

// Variance-preserving hexagonal stochastic texture bombing.
// Samples 3 triangle grid cells with height-weighted contrast-preserving blend:
// W_i = pow(w_i, 4.0) * exp(6.0 * h_i).
// Returns albedo (rgb) and roughness in .a, with tangent normal in n_out, height in h_out, and AO in ao_out.
vec4 sample_stochastic_pbr(sampler2D col, sampler2D nrm, sampler2D rgh, sampler2D hgt, sampler2D ao, vec2 uv, out vec3 n_out, out float h_out, out float ao_out) {
    // Transform to equilateral triangular (skewed) coordinates
    // Skew factors: 1/sqrt(3) ~ 0.577350269, 2/sqrt(3) ~ 1.154700538
    vec2 skewed = vec2(uv.x - 0.577350269 * uv.y, 1.154700538 * uv.y);
    vec2 ip = floor(skewed);
    vec2 fp = fract(skewed);

    vec2 v0, v1, v2;
    vec3 w;
    if (fp.x + fp.y < 1.0) {
        v0 = ip;
        v1 = ip + vec2(1.0, 0.0);
        v2 = ip + vec2(0.0, 1.0);
        w = vec3(1.0 - fp.x - fp.y, fp.x, fp.y);
    } else {
        v0 = ip + vec2(1.0, 1.0);
        v1 = ip + vec2(1.0, 0.0);
        v2 = ip + vec2(0.0, 1.0);
        w = vec3(fp.x + fp.y - 1.0, 1.0 - fp.y, 1.0 - fp.x);
    }

    vec2 h0 = hash22(v0);
    vec2 h1 = hash22(v1);
    vec2 h2 = hash22(v2);

    float a0 = h0.x * 6.2831853;
    float a1 = h1.x * 6.2831853;
    float a2 = h2.x * 6.2831853;

    vec2 uv0 = rot2(uv, a0) + h0 * 17.13;
    vec2 uv1 = rot2(uv, a1) + h1 * 17.13;
    vec2 uv2 = rot2(uv, a2) + h2 * 17.13;

    // Base UV derivatives for anisotropic filtering across grid boundaries
    vec2 duv_dx = dFdx(uv);
    vec2 duv_dy = dFdy(uv);

    vec4 c0 = textureGrad(col, uv0, duv_dx, duv_dy);
    vec4 c1 = textureGrad(col, uv1, duv_dx, duv_dy);
    vec4 c2 = textureGrad(col, uv2, duv_dx, duv_dy);

    float r0 = textureGrad(rgh, uv0, duv_dx, duv_dy).r;
    float r1 = textureGrad(rgh, uv1, duv_dx, duv_dy).r;
    float r2 = textureGrad(rgh, uv2, duv_dx, duv_dy).r;

    float h0_val = textureGrad(hgt, uv0, duv_dx, duv_dy).r;
    float h1_val = textureGrad(hgt, uv1, duv_dx, duv_dy).r;
    float h2_val = textureGrad(hgt, uv2, duv_dx, duv_dy).r;

    float ao0 = textureGrad(ao, uv0, duv_dx, duv_dy).r;
    float ao1 = textureGrad(ao, uv1, duv_dx, duv_dy).r;
    float ao2 = textureGrad(ao, uv2, duv_dx, duv_dy).r;

    vec3 n0 = unpack_normal(textureGrad(nrm, uv0, duv_dx, duv_dy).rgb);
    vec3 n1 = unpack_normal(textureGrad(nrm, uv1, duv_dx, duv_dy).rgb);
    vec3 n2 = unpack_normal(textureGrad(nrm, uv2, duv_dx, duv_dy).rgb);
    n0.xy = rot2(n0.xy, -a0);
    n1.xy = rot2(n1.xy, -a1);
    n2.xy = rot2(n2.xy, -a2);

    // Height-weighted contrast-preserving blend: W_i = pow(w_i, 4.0) * exp(6.0 * h_i)
    float W0 = pow(max(0.0, w.x), 4.0) * exp(clamp(6.0 * h0_val, -10.0, 10.0));
    float W1 = pow(max(0.0, w.y), 4.0) * exp(clamp(6.0 * h1_val, -10.0, 10.0));
    float W2 = pow(max(0.0, w.z), 4.0) * exp(clamp(6.0 * h2_val, -10.0, 10.0));
    float W_sum = max(1e-6, W0 + W1 + W2);
    vec3 bw = vec3(W0, W1, W2) / W_sum;

    vec3 final_col = c0.rgb * bw.x + c1.rgb * bw.y + c2.rgb * bw.z;
    float final_rgh = r0 * bw.x + r1 * bw.y + r2 * bw.z;
    n_out = normalize(n0 * bw.x + n1 * bw.y + n2 * bw.z);
    h_out = h0_val * bw.x + h1_val * bw.y + h2_val * bw.z;
    ao_out = ao0 * bw.x + ao1 * bw.y + ao2 * bw.z;

    return vec4(final_col, final_rgh);
}

// Contact-Refining Parallax Occlusion Mapping (POM) + soft self-shadowing helper.
// Uses linear stepping (24 steps) + 4 binary/secant refinement steps,
// and computes soft self-shadow factor pom_shadow along light_ts (8 steps).
vec2 pom_raymarch(sampler2D hgt_map, vec2 uv, vec3 view_ts, float height_scale, int steps, out float pom_shadow, vec3 light_ts) {
    pom_shadow = 1.0;
    if (height_scale <= 0.0) return uv;

    vec3 v = view_ts.z < 0.0 ? -view_ts : view_ts;
    v = normalize(v);
    float vz = max(v.z, 0.05);
    vec2 max_offset = -v.xy / vz * height_scale;

    int num_steps = steps > 0 ? steps : 24;
    float step_size = 1.0 / float(num_steps);

    float curr_layer_depth = 0.0;
    vec2 curr_uv = uv;
    float curr_map_h = texture(hgt_map, curr_uv).r;
    float curr_ray_h = 1.0;

    vec2 prev_uv = uv;
    float prev_ray_h = 1.0;
    float prev_map_h = curr_map_h;

    for (int i = 0; i < num_steps; i++) {
        curr_layer_depth += step_size;
        curr_uv = uv + max_offset * curr_layer_depth;
        curr_ray_h = 1.0 - curr_layer_depth;
        curr_map_h = texture(hgt_map, curr_uv).r;

        if (curr_ray_h <= curr_map_h) {
            break;
        }

        prev_uv = curr_uv;
        prev_ray_h = curr_ray_h;
        prev_map_h = curr_map_h;
    }

    // 4 refinement steps (secant + interval contraction)
    vec2 uv_min = prev_uv;
    vec2 uv_max = curr_uv;
    float ray_min = prev_ray_h;
    float ray_max = curr_ray_h;
    float map_min = prev_map_h;
    float map_max = curr_map_h;

    for (int j = 0; j < 4; j++) {
        float denom = (map_min - ray_min) + (ray_max - map_max);
        float t = denom > 1e-5 ? clamp((map_min - ray_min) / denom, 0.0, 1.0) : 0.5;
        vec2 mid_uv = mix(uv_min, uv_max, t);
        float mid_ray_h = mix(ray_min, ray_max, t);
        float mid_map_h = texture(hgt_map, mid_uv).r;

        if (mid_ray_h > mid_map_h) {
            uv_min = mid_uv;
            ray_min = mid_ray_h;
            map_min = mid_map_h;
        } else {
            uv_max = mid_uv;
            ray_max = mid_ray_h;
            map_max = mid_map_h;
        }
    }

    vec2 final_uv = mix(uv_min, uv_max, 0.5);
    float final_h = mix(ray_min, ray_max, 0.5);

    // Soft self-shadowing along light_ts (8 steps)
    vec3 l = light_ts;
    if (l.z > 0.02) {
        l = normalize(l);
        vec2 light_dir = l.xy / max(l.z, 0.05) * height_scale;
        float shadow_occlusion = 0.0;
        float remaining_h = 1.0 - final_h;

        if (remaining_h > 0.01) {
            for (int s = 1; s <= 8; s++) {
                float step_frac = float(s) / 8.0;
                float shadow_ray_h = final_h + 0.01 + step_frac * remaining_h;
                vec2 shadow_uv = final_uv + light_dir * (step_frac * remaining_h);
                float shadow_map_h = texture(hgt_map, shadow_uv).r;

                if (shadow_map_h > shadow_ray_h) {
                    float diff = (shadow_map_h - shadow_ray_h) * (1.0 - step_frac * 0.5) * 3.5;
                    shadow_occlusion = max(shadow_occlusion, diff);
                }
            }
        }
        pom_shadow = clamp(1.0 - shadow_occlusion, 0.0, 1.0);
    } else {
        pom_shadow = 0.0;
    }

    return final_uv;
}

// Anti-tiling: two lookups at different scales/rotations mixed by low-frequency noise.
// Returns albedo (linear), roughness in .a; the matching normal is written to n_out (tangent space).
vec4 sample_untiled(sampler2D col, sampler2D nrm, sampler2D rgh, vec2 uv, out vec3 n_out) {
    vec2 uv2 = rot2(uv * 0.77 + vec2(0.37, 0.61), 1.1);
    float m = smoothstep(0.35, 0.65, vnoise(uv * 0.23));
    vec3 c = mix(texture(col, uv).rgb, texture(col, uv2).rgb, m);
    float r = mix(texture(rgh, uv).r, texture(rgh, uv2).r, m);
    vec3 n1 = unpack_normal(texture(nrm, uv).rgb);
    vec3 n2 = unpack_normal(texture(nrm, uv2).rgb);
    n2.xy = rot2(n2.xy, -1.1);   // undo the uv rotation so lighting direction stays consistent
    n_out = normalize(mix(n1, n2, m));
    return vec4(c, r);
}

// World-space triplanar: albedo (rgb) + roughness (a); world-space perturbed normal in n_world.
vec4 triplanar(sampler2D col, sampler2D nrm, sampler2D rgh, vec3 p, vec3 wn, float sharp, out vec3 n_world) {
    vec3 w = pow(abs(wn), vec3(sharp));
    w /= (w.x + w.y + w.z);
    vec2 ux = p.zy, uy = p.xz, uz = p.xy;
    vec4 cx = vec4(texture(col, ux).rgb, texture(rgh, ux).r);
    vec4 cy = vec4(texture(col, uy).rgb, texture(rgh, uy).r);
    vec4 cz = vec4(texture(col, uz).rgb, texture(rgh, uz).r);
    // whiteout-style blend of each projection's tangent normal into world space
    // GL normal maps: +green points up the image, i.e. toward decreasing v (Godot uvs start top-left)
    vec3 tx = unpack_normal(texture(nrm, ux).rgb) * vec3(1, -1, 1);
    vec3 ty = unpack_normal(texture(nrm, uy).rgb) * vec3(1, -1, 1);
    vec3 tz = unpack_normal(texture(nrm, uz).rgb) * vec3(1, -1, 1);
    vec3 nx = vec3(tx.z * sign(wn.x), tx.y, tx.x);
    vec3 ny = vec3(ty.x, ty.z * sign(wn.y), ty.y);
    vec3 nz = vec3(tz.x, tz.y, tz.z * sign(wn.z));
    n_world = finite_surface_normal(nx * w.x + ny * w.y + nz * w.z + wn * 0.35, wn);
    return cx * w.x + cy * w.y + cz * w.z;
}

// Cushion-moss clumps: 3x3 cellular search. Returns (dome height 0..1, offset-to-centre xy, cell id).
vec4 moss_cells(vec2 p, float rmin, float rspan) {
    vec2 ip = floor(p), fp = fract(p);
    float best = 8.0; vec2 bo = vec2(0.0); float id = 0.0;
    for (int j = -1; j <= 1; j++) for (int i = -1; i <= 1; i++) {
        vec2 g = vec2(float(i), float(j));
        float h = hash21(ip + g);
        vec2 o = g + vec2(h, fract(h * 37.13)) * 0.8 + 0.1 - fp;
        o *= vec2(1.0, 0.75 + 0.5 * fract(h * 13.7));             // slightly oval, not perfect discs
        float r = rmin + rspan * fract(h * 91.7);
        float d = length(o) / r;
        if (d < best) { best = d; bo = o / r; id = h; }
    }
    return vec4(sqrt(max(0.0, 1.0 - best * best)), bo, id);
}

// Multi-size, domain-warped cushions: a few large mounds over a field of small ones.
// Returns (height 0..1, slope xy, id); speck (0..1) marks bright strand / sporophyte tips.
vec4 moss_clumps(vec2 p, out float speck) {
    vec2 w = vec2(vnoise(p * 0.23 + 3.7), vnoise(p * 0.23 + 11.9)) - 0.5;
    p += w * 2.2 + (vec2(vnoise(p * 0.9), vnoise(p * 0.9 + 5.3)) - 0.5) * 0.45;
    vec4 big = moss_cells(p * 0.32 + 7.1, 0.35, 0.55);
    big.x *= step(0.62, fract(big.w * 53.1)) * 1.0;               // only ~40% of big cells become cushions
    vec4 sm = moss_cells(p, 0.45, 0.45);
    vec4 r = sm;
    float bh = big.x * 0.85 + sm.x * 0.25;
    if (bh > sm.x) r = vec4(bh, big.yz * 0.6 + sm.yz * 0.4, big.w);
    // speckles: sparse hashed points on a fine lattice, only on clump tops
    vec2 sp = p * 3.3;
    float sh = hash21(floor(sp));
    float sd = length(fract(sp) - 0.5 - (vec2(sh, fract(sh * 7.7)) - 0.5) * 0.6);
    speck = step(0.86, sh) * smoothstep(0.16, 0.05, sd) * smoothstep(0.3, 0.7, r.x);
    return r;
}
