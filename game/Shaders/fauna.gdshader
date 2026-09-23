// Fauna instances. Species colours are material uniforms; per-individual genetics arrive in
// INSTANCE_CUSTOM = (hue shift, marking density, pattern contrast, appendage scale ×1000 + walk-cycle phase).
// The phase is accumulated on the CPU from each animal's real speed, so legs and tails move with the body.
// Vertex COLOR.rgb = offset from the appendage attachment, COLOR.a = appendage flag (see OrganismMeshes).
// UV = body coordinates for markings, UV2.x = region (1 body, 2 eye, 3 belly, 4 fin/limb).
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx, cull_disabled;

uniform vec3 base_color : source_color = vec3(0.9, 0.3, 0.2);
uniform vec3 ornament_color : source_color = vec3(1.0);
uniform float wiggle = 1.0;
uniform float wiggle_speed = 9.0;
uniform float translucency = 0.0;
uniform float carapace = 0.0;   // clearcoat for hard, glossy exoskeletons
uniform float scales = 0.0;     // 1 = fish scales + silvery sheen
uniform float segment_rings = 0.0;   // body segments to mark as faint grooves along UV.x (0 = none)

varying vec4 genes;
varying float region;
varying vec2 body_uv;
varying float seed;

vec3 hue_rotate(vec3 c, float shift) {
    // rotate hue in YIQ space
    const mat3 toYIQ = mat3(vec3(0.299, 0.596, 0.211), vec3(0.587, -0.274, -0.523), vec3(0.114, -0.322, 0.312));
    const mat3 toRGB = mat3(vec3(1.0, 1.0, 1.0), vec3(0.956, -0.272, -1.106), vec3(0.621, -0.647, 1.703));
    vec3 yiq = toYIQ * c;
    float a = shift * 6.2831853;
    float cs = cos(a), sn = sin(a);
    yiq.yz = vec2(yiq.y * cs - yiq.z * sn, yiq.y * sn + yiq.z * cs);
    return clamp(toRGB * yiq, 0.0, 1.0);
}
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }
float vnoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}

void vertex() {
    genes = INSTANCE_CUSTOM;
    float cycle = fract(genes.w);
    genes.w = floor(genes.w) / 1000.0;
    region = UV2.x;
    body_uv = UV;
    seed = float(INSTANCE_ID);
    // genetic appendage length: slide appendage vertices along their attachment offset
    VERTEX += COLOR.rgb * (genes.w - 1.0) * COLOR.a;
    // cosmetic locomotion: tail-to-head travelling wave (render-only)
    float phase = cycle * 6.2831853 + seed * 1.37;
    float along = clamp(0.5 - VERTEX.x, 0.0, 1.5);
    VERTEX.z += sin(phase - VERTEX.x * 7.0) * 0.035 * wiggle * along;
    VERTEX.y += COLOR.a * sin(phase * 2.0 + VERTEX.x * 11.0) * 0.012 * max(wiggle, 0.3);
}

void fragment() {
    vec3 base = hue_rotate(base_color, genes.x);
    float density = genes.y, contrast = genes.z;
    // markings: spots whose count follows density; contrast follows pattern strength
    float scale = mix(3.0, 14.0, density);
    float spots = smoothstep(0.62, 0.72, vnoise(body_uv * vec2(scale, scale * 0.6) + seed * 0.13));
    float bands = smoothstep(0.35, 0.5, abs(fract(body_uv.x * mix(2.0, 7.0, density)) - 0.5)) * 0.4;
    float mark = clamp(max(spots, bands * density), 0.0, 1.0) * contrast;
    vec3 col = mix(base, hue_rotate(ornament_color, genes.x * 0.5), mark * step(region, 1.5));
    if (region > 1.5 && region < 2.5) col = vec3(0.03);                    // eyes
    else if (region > 2.5 && region < 3.5) col = mix(col, vec3(1.0), 0.35); // lighter belly
    else if (region > 3.5) col = mix(base * 1.1, vec3(1.0), 0.15);        // limbs & fins
    // fine surface grain (chitin pits / skin) so bodies don't read as smooth plastic
    float micro = vnoise(body_uv * vec2(90.0, 60.0) + seed * 0.37);
    col *= 0.92 + 0.16 * micro;
    if (segment_rings > 0.5 && region < 1.5) col *= 1.0 - 0.22 * smoothstep(0.42, 0.5, abs(fract(body_uv.x * segment_rings) - 0.5));
    float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
    if (scales > 0.5 && region < 3.5 && !(region > 1.5 && region < 2.5)) {
        vec2 sc = body_uv * vec2(34.0, 22.0);
        sc.x += floor(sc.y) * 0.5;
        float edge = smoothstep(0.32, 0.5, length(fract(sc) - 0.5));
        col *= 1.0 - 0.14 * edge;
        col = mix(col, vec3(0.85, 0.9, 0.95), fres * 0.35 + (region > 2.5 ? 0.35 : 0.0));   // silvery flank and belly
    }
    ALBEDO = col;
    ROUGHNESS = region > 1.5 && region < 2.5 ? 0.1 : mix(0.5, 0.3, carapace) + 0.1 * micro;
    SPECULAR = 0.5;
    CLEARCOAT = carapace;
    CLEARCOAT_ROUGHNESS = 0.25;
    RIM = 0.25;
    RIM_TINT = 0.6;
    BACKLIGHT = vec3(translucency) * col;
}
