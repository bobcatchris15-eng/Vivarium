// Frosted dark glass for UI panels: blurred copy of what is behind, dark tint, rounded corners,
// hairline top highlight and faint grain. rect_size / corner are set by GlassPanel on resize.
shader_type canvas_item;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap;
uniform vec2 rect_size = vec2(100.0, 40.0);
uniform float corner = 12.0;
uniform float blur_lod = 3.5;
uniform vec4 tint : source_color = vec4(0.04, 0.06, 0.08, 0.58);
uniform float circle = 0.0;   // 1 = circular disc instead of rounded rect

float rounded_rect(vec2 p, vec2 half_size, float r) {
    vec2 q = abs(p) - half_size + r;
    return length(max(q, 0.0)) + min(max(q.x, q.y), 0.0) - r;
}

float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

void fragment() {
    vec2 p = (UV - 0.5) * rect_size;
    float d = circle > 0.5 ? length(p) - min(rect_size.x, rect_size.y) * 0.5 : rounded_rect(p, rect_size * 0.5, corner);
    float alpha = clamp(0.5 - d, 0.0, 1.0);
    if (alpha <= 0.0) discard;
    // soft multi-tap blur on top of a mip lookup
    vec2 px = SCREEN_PIXEL_SIZE * 6.0;
    vec3 c = textureLod(screen_tex, SCREEN_UV, blur_lod).rgb * 0.4;
    c += textureLod(screen_tex, SCREEN_UV + vec2(px.x, 0.0), blur_lod).rgb * 0.15;
    c += textureLod(screen_tex, SCREEN_UV - vec2(px.x, 0.0), blur_lod).rgb * 0.15;
    c += textureLod(screen_tex, SCREEN_UV + vec2(0.0, px.y), blur_lod).rgb * 0.15;
    c += textureLod(screen_tex, SCREEN_UV - vec2(0.0, px.y), blur_lod).rgb * 0.15;
    vec3 col = mix(c, tint.rgb, tint.a);
    col += (hash(FRAGCOORD.xy) - 0.5) * 0.02;                       // grain
    float edge = smoothstep(1.5, 0.0, abs(d + 0.75));                // hairline rim
    float top = smoothstep(0.0, 1.0, 1.0 - UV.y);                    // brighter toward the top
    col += edge * (0.10 + 0.12 * top);
    COLOR = vec4(col, alpha * 0.97);
}
