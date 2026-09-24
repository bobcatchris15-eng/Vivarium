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
float waves(vec2 p) { return vnoise(p) * 0.58 + vnoise(p * 1.93 + 7.1) * 0.29 + vnoise(p * 4.37 - 3.3) * 0.13; }

float water_height(vec2 uv, vec2 f, float t) {
    float speed = clamp(length(f), 0.0, 1.0);
    vec2 fd = speed > 0.01 ? normalize(f) : normalize(vec2(0.83, 0.56));
    vec2 side = vec2(-fd.y, fd.x);
    vec2 q = vec2(dot(uv, fd), dot(uv, side));

    // Broad capillary texture drifts continuously rather than cycling between two noise fields. Flow introduces
    // elongated detail along the current, but still leaves quiet water with subtle non-directional structure.
    float broad = waves(uv * ripple_scale * 0.62 - f * t * 0.08 + vec2(t * 0.021, -t * 0.016));
    float streak = vnoise(q * vec2(ripple_scale * 0.72, ripple_scale * 1.55)
                          - vec2(t * (0.08 + speed * 0.20), t * 0.019));
    float cross = vnoise(q.yx * vec2(ripple_scale * 0.41, ripple_scale * 0.88)
                         + vec2(-t * 0.013, t * 0.031));
    return mix(broad, streak * 0.72 + cross * 0.28, speed * 0.42);
}

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
        float t = TIME;
        float e = 0.018;
        h = water_height(UV, f, t);
        hx = water_height(UV + vec2(e, 0.0), f, t);
        hz = water_height(UV + vec2(0.0, e), f, t);
        // ripple normal built in world space (the water mesh has no tangents, so NORMAL_MAP would be undefined
        // per triangle and show up as a brick pattern of mismatched reflections)
        float amp = 0.18 + 0.48 * clamp(length(f), 0.0, 1.0);
        vec2 slope = vec2(h - hx, h - hz) / e * amp * 0.030;
        vec3 wn = normalize(vec3(slope.x, 1.0, slope.y));
        if (!FRONT_FACING) wn = -wn;   // seen from below the surface
        NORMAL = normalize((VIEW_MATRIX * vec4(wn, 0.0)).xyz);
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
    // Fast current reads through the moving normal/refraction instead of a bright procedural streak overlay.

    if (cutface > 0.5) {
        // aquarium glass: meniscus line where the water meets the air, otherwise perfectly clear
        float meniscus = 1.0 - smoothstep(0.0, 0.006, UV.y);
        scene = mix(scene, vec3(0.85, 0.95, 1.0), meniscus * 0.55);
    }

    ALBEDO = vec3(0.0);           // the refracted scene is already lit; the surface only adds reflections
    EMISSION = scene;
    // soft waterline: the surface fades out where almost no water lies over the ground
    ALPHA = cutface > 0.5 ? 1.0 : smoothstep(0.0, 0.012, max(linear_depth(SCREEN_UV, INV_PROJECTION_MATRIX) - surf_depth, 0.0));
    float flow_rough = smoothstep(0.15, 1.0, clamp(length(f), 0.0, 1.0));
    ROUGHNESS = cutface > 0.5 ? 0.0 : mix(0.025, 0.06, flow_rough);
    METALLIC = 0.0;
    SPECULAR = cutface > 0.5 ? 0.15 : 0.5;
}
