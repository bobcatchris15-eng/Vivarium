// Shared helpers for the photo-scanned (CC0, ambientCG) surface materials.
// Normal maps are OpenGL convention (+Y up) and imported as normal maps, so the sampled .xy is the
// tangent-space normal in [0,1]; unpack_normal returns it in [-1,1] with z rebuilt.

vec3 srgb_to_linear(vec3 c) { return mix(c / 12.92, pow((c + 0.055) / 1.055, vec3(2.4)), step(0.04045, c)); }

float hash21(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }
float vnoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash21(i), hash21(i + vec2(1, 0)), u.x), mix(hash21(i + vec2(0, 1)), hash21(i + vec2(1, 1)), u.x), u.y);
}
float fbm(vec2 p) { float s = 0.0, a = 0.5; for (int i = 0; i < 4; i++) { s += a * vnoise(p); p *= 2.03; a *= 0.5; } return s; }

vec3 unpack_normal(vec3 t) { vec2 xy = t.xy * 2.0 - 1.0; return vec3(xy, sqrt(max(0.0, 1.0 - dot(xy, xy)))); }

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
    return normalize(geom_normal * detail.z + (t * detail.x - b * detail.y) * inv);
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
    n_world = normalize(nx * w.x + ny * w.y + nz * w.z + wn * 0.35);
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
