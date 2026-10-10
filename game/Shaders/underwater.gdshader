// Screen-space underwater treatment: gentle teal grade, soft caustic shimmer and vignette.
// Kept subtle on purpose so small aquatic fauna stay clearly visible.
shader_type canvas_item;

uniform float strength : hint_range(0.0, 1.0) = 0.0;
uniform vec3 tint : source_color = vec3(0.42, 0.70, 0.50);
uniform sampler2D screen_tex : hint_screen_texture, filter_linear;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }
float vnoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}

// Dual animated wave interference network for shimmering caustics
float caustic_pattern(vec2 pos, float t) {
    vec2 p1 = pos * 12.0 + vec2(t * 0.55, t * 0.35);
    p1 += vec2(sin(p1.y * 1.35 + t * 0.75), cos(p1.x * 1.35 - t * 0.65)) * 0.45;
    float w1 = sin(p1.x * 2.2 + p1.y * 1.2);
    float w2 = cos(p1.y * 2.2 - p1.x * 1.1);
    float c1 = pow(clamp(1.0 - abs(w1 * w2), 0.0, 1.0), 3.5);

    vec2 p2 = vec2(pos.x * 0.707 - pos.y * 0.707, pos.x * 0.707 + pos.y * 0.707) * 15.0 + vec2(-t * 0.45, t * 0.55);
    p2 += vec2(cos(p2.y * 1.25 - t * 0.70), sin(p2.x * 1.25 + t * 0.80)) * 0.45;
    float w3 = sin(p2.x * 2.0 - p2.y * 1.3);
    float w4 = cos(p2.y * 2.1 + p2.x * 1.2);
    float c2 = pow(clamp(1.0 - abs(w3 * w4), 0.0, 1.0), 3.5);

    return clamp((c1 + c2) * 0.45 + (c1 * c2) * 1.4, 0.0, 2.0);
}

void fragment() {
    vec2 uv = SCREEN_UV;
    vec2 wob = vec2(vnoise(uv * 8.0 + TIME * 0.4), vnoise(uv * 8.0 - TIME * 0.35)) - 0.5;
    vec3 scene = texture(screen_tex, uv + wob * 0.004 * strength).rgb;
    
    // Shimmering caustic light bands
    float caustic = caustic_pattern(uv + wob * 0.015, TIME);
    vec3 caustic_color = vec3(0.70, 0.95, 0.82);
    vec3 caustic_light = caustic_color * caustic * 0.32;

    vec3 graded = mix(scene, scene * tint * 1.15, 0.22) + caustic_light * strength;
    float vig = smoothstep(0.95, 0.35, distance(uv, vec2(0.5)));
    graded *= mix(0.82, 1.0, vig);
    COLOR = vec4(mix(scene, graded, strength), 1.0);
}
