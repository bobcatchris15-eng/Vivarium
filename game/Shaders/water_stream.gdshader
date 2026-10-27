// Hydrostatic water table: the same optical composite as the moving surface.
// The camera sees the black void; water reflects the canopy used for ambient light.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_disabled, unshaded, fog_disabled;

uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
uniform float underwater = 0.0;
uniform float review_time = -1.0;
#include "res://Shaders/mobile_water_optics.gdshaderinc"

varying vec3 water_world_position;
varying float depthf;
varying float cutface;

void vertex() {
    water_world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    depthf = COLOR.a;
    cutface = step(0.5, COLOR.r);
}

float linear_depth(vec2 uv, mat4 inv_proj) {
    vec4 p = inv_proj * vec4(uv * 2.0 - 1.0, texture(depth_tex, uv).r, 1.0);
    return -p.z / p.w;
}

float capillary_height(vec2 uv, float t) {
    float along = dot(uv, vec2(.8, .6));
    float across = dot(uv, vec2(-.6, .8));
    return .5 + .35 * sin(along * 24.0 - t * 5.5 + sin(across * 8.0) * .7)
        + .20 * sin(along * 48.0 - t * 11.0 + across * 16.0)
        + .12 * sin(across * 18.0 + sin(along * 12.0 - t * 3.0));
}

void fragment() {
    float clock = review_time >= 0.0 ? review_time : TIME;
    float t = clock * .12;
    float e = .015;
    float amplitude = .10 * mix(.28, 1.0, depthf) * .32;
    float h = capillary_height(UV, t);
    vec2 slope = (vec2(h) - vec2(capillary_height(UV + vec2(e, 0.0), t),
        capillary_height(UV + vec2(0.0, e), t))) / e * amplitude * .045;
    vec3 base_normal = normalize(NORMAL) * (underwater > .5 ? -1.0 : 1.0);
    if (cutface < .5)
        NORMAL = normalize(base_normal + (VIEW_MATRIX * vec4(slope.x, 0.0, slope.y, 0.0)).xyz);

    vec3 normal = normalize(NORMAL);
    vec3 view = normalize(VIEW);
    float cosine = max(abs(dot(normal, view)), .25);
    float surface_depth = -VERTEX.z;
    float gap = max(linear_depth(SCREEN_UV, INV_PROJECTION_MATRIX) - surface_depth, 0.0);
    vec3 flat_view = normalize((VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
    vec2 offset = clamp((normal.xy - flat_view.xy) * .024
        * smoothstep(.0002, .008, gap) * (1.0 - cutface), vec2(-.012), vec2(.012));
    vec2 uv = clamp(SCREEN_UV + offset, vec2(.002), vec2(.998));
    float refracted_depth = linear_depth(uv, INV_PROJECTION_MATRIX);
    if (refracted_depth < surface_depth || refracted_depth > surface_depth + 20.0)
        uv = SCREEN_UV;
    else
        uv = mix(uv, SCREEN_UV, smoothstep(.03, .12,
            abs(refracted_depth - linear_depth(SCREEN_UV, INV_PROJECTION_MATRIX))));
    vec3 behind = textureLod(screen_tex, uv, 0.0).rgb;
    vec3 world_normal = normalize((INV_VIEW_MATRIX * vec4(normal, 0.0)).xyz);
    vec3 world_view = normalize(CAMERA_POSITION_WORLD - water_world_position);
    vec3 reflected = reflect(-world_view, world_normal);
    float fresnel = .0204 + .9796 * pow(1.0 - clamp(dot(normal, view), 0.0, 1.0), 5.0);
    float path = min(gap, max(.0001, depthf * .15) / cosine);
    vec3 transmitted = mobile_transmission(behind, path, 1.0);
    vec3 color = mobile_surface_color(transmitted, reflected, fresnel, 1.0);
    ALPHA = smoothstep(.00002, .0005, gap);
    if (cutface > .5) {
        float meniscus = depthf <= .005 ? 0.0 : 1.0 - smoothstep(0.0, .006, UV.y);
        color = mix(behind, vec3(.90, .96, 1.0), meniscus * .5);
        ALPHA = 1.0;
    } else if (underwater > .5) {
        color = behind;
        ALPHA = 1.0;
    } else {
        if (water_debug == 1) color = world_normal * .5 + .5;
        if (water_debug == 2) color = transmitted;
        if (water_debug == 3) color = mobile_reflection(reflected);
        if (water_debug == 4) color = vec3(fresnel);
    }
    // Screen transmission and canopy reflection are already composed. Lighting
    // this result again adds a second, mismatched radiance response at the join.
    ALBEDO = color;
}
