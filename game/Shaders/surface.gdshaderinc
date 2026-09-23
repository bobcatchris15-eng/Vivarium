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
