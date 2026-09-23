// Clear, inspectable water. COLOR.a = depth factor, UV = world xz (surface) or (along, depth) on the cut
// face, UV2 = flow velocity from the authoritative hydrology (visual only: nothing flows back).
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform vec3 shallow_color : source_color = vec3(0.35, 0.78, 0.82);
uniform vec3 deep_color : source_color = vec3(0.10, 0.42, 0.62);
uniform float shallow_alpha = 0.18;
uniform float deep_alpha = 0.52;
uniform float flow_visual_scale = 400.0;
uniform float ripple_scale = 7.0;
uniform float underwater = 0.0;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }
float vnoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}
float waves(vec2 p) { return vnoise(p) * 0.6 + vnoise(p * 2.3 + 7.1) * 0.3 + vnoise(p * 5.1 - 3.3) * 0.1; }

varying float depthf;
varying vec2 flow;
varying float cutface;

void vertex() {
    depthf = COLOR.a;
    flow = UV2;
    cutface = step(abs(NORMAL.y), 0.5);
}

void fragment() {
    vec2 f = clamp(flow * flow_visual_scale, vec2(-3.0), vec2(3.0));
    float t = TIME * 0.35;
    float ph0 = fract(t), ph1 = fract(t + 0.5);
    float w = abs(ph0 - 0.5) * 2.0;
    vec2 p = UV * ripple_scale;
    float h0 = waves(p - f * ph0 + vec2(t * 0.2, 0.0));
    float h1 = waves(p - f * ph1 + vec2(0.0, t * 0.2) + 3.7);
    float h = mix(h0, h1, w);
    float e = 0.02;
    float hx = mix(waves(p + vec2(e, 0) - f * ph0 + vec2(t * 0.2, 0.0)), waves(p + vec2(e, 0) - f * ph1 + vec2(0.0, t * 0.2) + 3.7), w);
    float hz = mix(waves(p + vec2(0, e) - f * ph0 + vec2(t * 0.2, 0.0)), waves(p + vec2(0, e) - f * ph1 + vec2(0.0, t * 0.2) + 3.7), w);
    float amp = 0.25 + 0.6 * clamp(length(f), 0.0, 1.0);
    NORMAL_MAP = normalize(vec3((h - hx) / e * amp * 0.08 + 0.5, (h - hz) / e * amp * 0.08 + 0.5, 1.0));
    NORMAL_MAP_DEPTH = 0.6 * (1.0 - cutface);
    float d = clamp(depthf, 0.0, 1.0);
    vec3 col = mix(shallow_color, deep_color, d);
    // faint streaks show the current direction on streams
    col += vec3(0.12) * smoothstep(0.75, 1.0, h) * clamp(length(f), 0.0, 1.0);
    ALBEDO = col;
    ALPHA = mix(shallow_alpha, deep_alpha, d);
    if (cutface > 0.5) { ALPHA = 0.42; ALBEDO = mix(shallow_color, deep_color, clamp(UV.y * 2.5, 0.0, 1.0)); }
    if (underwater > 0.5) ALPHA *= 0.6;
    ROUGHNESS = 0.04;
    METALLIC = 0.0;
    SPECULAR = 0.7;
}
