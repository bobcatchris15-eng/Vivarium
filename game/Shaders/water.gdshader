// Clear water. The scene behind the water is refracted from the screen texture and attenuated by the true
// thickness of water it passes through (Beer–Lambert, per channel, from the depth buffer), so shallow water is
// nearly invisible and only deep pools pick up a faint green-blue. Lighting adds the reflections and glints.
// The cut face at the island edge is treated as aquarium glass: no tint, a thin meniscus line at the top.
// COLOR.a = depth factor, UV = world xz (surface) or (along, depth) on the cut face,
// UV2 = flow velocity from the authoritative hydrology (visual only: nothing flows back).
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, diffuse_burley, specular_schlick_ggx;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
uniform vec3 absorption = vec3(0.45, 0.16, 0.12);   // per metre of water; small = crystal clear
uniform vec3 deep_tint : source_color = vec3(0.10, 0.34, 0.36);
uniform float refraction = 0.004;
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

float linear_depth(vec2 uv, mat4 inv_proj) {
    float d = texture(depth_tex, uv).r;
    vec4 v = inv_proj * vec4(uv * 2.0 - 1.0, d, 1.0);
    return -v.z / v.w;
}

void fragment() {
    float h = 0.0, hx = 0.0, hz = 0.0;
    vec2 f = clamp(flow * flow_visual_scale, vec2(-3.0), vec2(3.0));
    if (cutface < 0.5) {
        float t = TIME * 0.35;
        float ph0 = fract(t), ph1 = fract(t + 0.5);
        float w = abs(ph0 - 0.5) * 2.0;
        vec2 p = UV * ripple_scale;
        float e = 0.02;
        h = mix(waves(p - f * ph0 + vec2(t * 0.2, 0.0)), waves(p - f * ph1 + vec2(0.0, t * 0.2) + 3.7), w);
        hx = mix(waves(p + vec2(e, 0) - f * ph0 + vec2(t * 0.2, 0.0)), waves(p + vec2(e, 0) - f * ph1 + vec2(0.0, t * 0.2) + 3.7), w);
        hz = mix(waves(p + vec2(0, e) - f * ph0 + vec2(t * 0.2, 0.0)), waves(p + vec2(0, e) - f * ph1 + vec2(0.0, t * 0.2) + 3.7), w);
        float amp = 0.25 + 0.6 * clamp(length(f), 0.0, 1.0);
        NORMAL_MAP = normalize(vec3((h - hx) / 0.02 * amp * 0.08 + 0.5, (h - hz) / 0.02 * amp * 0.08 + 0.5, 1.0));
        NORMAL_MAP_DEPTH = 0.5;
    }

    // refract: offset the screen lookup by the ripple slope, but never pick up things in front of the water
    float surf_depth = -VERTEX.z;
    vec2 offset = vec2(h - hx, h - hz) / 0.02 * refraction * (1.0 - cutface);
    vec2 ruv = SCREEN_UV + offset;
    if (linear_depth(ruv, INV_PROJECTION_MATRIX) < surf_depth) ruv = SCREEN_UV;
    float thickness = max(linear_depth(ruv, INV_PROJECTION_MATRIX) - surf_depth, 0.0);
    if (cutface > 0.5) thickness *= 0.25;     // looking in through the glass: judge the column mostly by what's behind
    if (underwater > 0.5) thickness = 0.0;    // from below, the screen-space underwater grade does the tinting
    vec3 behind = textureLod(screen_tex, ruv, 0.0).rgb;
    vec3 transmit = exp(-absorption * thickness);
    vec3 scene = behind * transmit + deep_tint * (1.0 - transmit) * 0.35;
    // faint current streaks on streams
    scene += vec3(0.05) * smoothstep(0.78, 1.0, h) * clamp(length(f), 0.0, 1.0) * (1.0 - cutface);

    if (cutface > 0.5) {
        // aquarium glass: meniscus line where the water meets the air, otherwise perfectly clear
        float meniscus = 1.0 - smoothstep(0.0, 0.006, UV.y);
        scene = mix(scene, vec3(0.85, 0.95, 1.0), meniscus * 0.55);
    }

    ALBEDO = vec3(0.0);           // the refracted scene is already lit; the surface only adds reflections
    EMISSION = scene;
    ALPHA = 1.0;
    ROUGHNESS = cutface > 0.5 ? 0.0 : 0.03;
    METALLIC = 0.0;
    SPECULAR = cutface > 0.5 ? 0.15 : 0.5;
}
