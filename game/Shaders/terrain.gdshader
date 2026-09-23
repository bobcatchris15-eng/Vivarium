// Island top surface. Colours come from content substrates (uniforms); live habitat data arrives as a
// small field texture (R = moisture, G = substrate code / 4, B = nutrients, A = light) sampled by world XZ.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

uniform sampler2D field_tex : filter_linear, repeat_disable;
uniform vec4 field_rect;            // origin.x, origin.z, size.x, size.z
uniform vec3 soil_color : source_color = vec3(0.36, 0.24, 0.14);
uniform vec3 rock_color : source_color = vec3(0.55, 0.55, 0.57);
uniform vec3 gravel_color : source_color = vec3(0.62, 0.60, 0.56);
uniform vec3 wood_color : source_color = vec3(0.45, 0.30, 0.18);
uniform vec3 moss_tint : source_color = vec3(0.34, 0.52, 0.20);
uniform int overlay_mode = 0;       // 0 none, 1 moisture, 2 nutrients, 3 light, 4 substrate
uniform float overlay_strength = 0.85;

varying vec3 world_pos;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }
float vnoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}
float fbm(vec2 p) { float s = 0.0, a = 0.5; for (int i = 0; i < 4; i++) { s += a * vnoise(p); p *= 2.03; a *= 0.5; } return s; }

vec3 ramp(float t) {
    t = clamp(t, 0.0, 1.0);
    return mix(mix(vec3(0.15, 0.2, 0.75), vec3(0.1, 0.8, 0.5), smoothstep(0.0, 0.5, t)), vec3(1.0, 0.85, 0.1), smoothstep(0.5, 1.0, t));
}

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    vec2 fuv = (world_pos.xz - field_rect.xy) / field_rect.zw;
    vec4 f = texture(field_tex, fuv);
    float moisture = f.r;
    float code = floor(f.g * 4.0 + 0.5);         // 0 soil, 1 gravel, 2 rock, 3 wood, 4 water
    float fine = fbm(world_pos.xz * 9.0);
    float coarse = fbm(world_pos.xz * 1.3);
    vec3 base = soil_color * (0.82 + 0.35 * fine) * (0.9 + 0.2 * coarse);
    // soft green bloom where the ground is moist and well lit: reads as a living floor without blades
    base = mix(base, moss_tint * (0.8 + 0.4 * fine), smoothstep(0.55, 0.9, moisture) * 0.35 * f.a);
    vec3 rock = rock_color * (0.8 + 0.4 * fbm(world_pos.xz * 5.0));
    vec3 gravel = gravel_color * (0.7 + 0.6 * hash(floor(world_pos.xz * 60.0)));
    vec3 col = base;
    col = mix(col, rock, smoothstep(1.5, 2.0, code) * (1.0 - smoothstep(2.5, 3.0, code)));
    col = mix(col, gravel, (1.0 - abs(code - 1.0)) * step(0.5, code) * step(code, 1.5));
    // wetness darkens and adds sheen
    float wet = smoothstep(0.45, 1.0, moisture);
    col *= mix(1.0, 0.62, wet);
    if (overlay_mode == 1) col = mix(col, ramp(moisture), overlay_strength);
    else if (overlay_mode == 2) col = mix(col, ramp(f.b), overlay_strength);
    else if (overlay_mode == 3) col = mix(col, ramp(f.a), overlay_strength);
    else if (overlay_mode == 4) col = mix(col, vec3(code == 0.0 ? 0.55 : 0.0, code == 1.0 ? 0.8 : (code == 0.0 ? 0.35 : 0.0), code >= 2.0 ? 0.3 + 0.2 * code : 0.1), overlay_strength);
    ALBEDO = col;
    ROUGHNESS = mix(0.92, 0.35, wet);
    SPECULAR = mix(0.3, 0.6, wet);
}
