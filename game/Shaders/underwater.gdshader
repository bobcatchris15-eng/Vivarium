// Screen-space underwater treatment: gentle teal grade, soft caustic shimmer and vignette.
// Kept subtle on purpose so small aquatic fauna stay clearly visible.
shader_type canvas_item;

uniform float strength : hint_range(0.0, 1.0) = 0.0;
uniform vec3 tint : source_color = vec3(0.35, 0.75, 0.8);
uniform sampler2D screen_tex : hint_screen_texture, filter_linear;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }
float vnoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}

void fragment() {
    vec2 uv = SCREEN_UV;
    vec2 wob = vec2(vnoise(uv * 8.0 + TIME * 0.4), vnoise(uv * 8.0 - TIME * 0.35)) - 0.5;
    vec3 scene = texture(screen_tex, uv + wob * 0.004 * strength).rgb;
    float caustic = pow(vnoise(uv * vec2(22.0, 14.0) + TIME * 0.6) * vnoise(uv * vec2(17.0, 26.0) - TIME * 0.5), 2.0);
    vec3 graded = mix(scene, scene * tint * 1.25 + caustic * 0.12, 0.45);
    float vig = smoothstep(0.95, 0.35, distance(uv, vec2(0.5)));
    graded *= mix(0.82, 1.0, vig);
    COLOR = vec4(mix(scene, graded, strength), 1.0);
}
