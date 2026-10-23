// Hydrostatic water table: a lit Forward+ surface over the same optical body as the
// dynamic fluid. Sun glints, canopy IBL, cast shadows and the reflection probe are
// evaluated by the engine on the surface; the fragment only supplies the refracted
// radiance below it.
shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_disabled, diffuse_burley, specular_schlick_ggx;

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

// Refracted screen sample. The three channels bend by slightly different angles and
// the mip level grows with the optical path, so a deep column is defocused by
// scattering rather than covered by a tint.
vec3 refracted_screen(vec2 uv, vec2 bend_dir, float path, float roughness) {
    float mip = water_refraction_mip(path, roughness);
    float spread = water_dispersion_amount(path, roughness);
    vec2 lo = clamp(uv - bend_dir * spread, vec2(.002), vec2(.998));
    vec2 hi = clamp(uv + bend_dir * spread, vec2(.002), vec2(.998));
    return vec3(textureLod(screen_tex, lo, mip).r,
        textureLod(screen_tex, uv, mip).g,
        textureLod(screen_tex, hi, mip).b);
}

void fragment() {
    float clock = review_time >= 0.0 ? review_time : TIME;

    // Un-clamped screen-space optical path from the surface to the opaque bed.
    // It is the real distance along the view ray, not a 15 cm estimate: a deep pond
    // absorbs like a deep pond and its colour grades with depth instead of saturating.
    float surface_depth = -VERTEX.z;
    float scene = linear_depth(SCREEN_UV, INV_PROJECTION_MATRIX);
    float gap = max(scene - surface_depth, 0.0);
    bool bed_visible = scene > surface_depth && scene < surface_depth + 20.0;
    // The mesh carries the hydrostatic column in COLOR.a, quantised to 15 cm. It is
    // only a fallback for pixels with no opaque bed behind the surface.
    float column = max(depthf * 0.15, 0.0001);

    // Analytic normal: a flat table plus the gravity-capillary slope. It never comes
    // from screen derivatives, so no triangle or vertex normal can show through.
    vec3 base_normal = normalize(NORMAL) * (underwater > .5 ? -1.0 : 1.0);
    float slope_variance = 0.0;
    if (cutface < .5) {
        vec2 slope = water_wave_slope(water_world_position.xz, clock, gap,
            water_wind_speed, water_wind_direction, slope_variance);
        base_normal = normalize(base_normal + (VIEW_MATRIX * vec4(-slope.x, 0.0, -slope.y, 0.0)).xyz);
    }
    vec3 normal = normalize(base_normal);
    vec3 view = normalize(VIEW);
    float cosine = clamp(dot(normal, view), 0.0, 1.0);
    float fresnel = water_fresnel(cosine);
    float roughness = clamp(0.018 + slope_variance * 0.25, 0.018, 0.14);

    vec3 flat_view = normalize((VIEW_MATRIX * vec4(0.0, 1.0, 0.0, 0.0)).xyz);
    vec2 offset = clamp((normal.xy - flat_view.xy) * .024
        * smoothstep(.0002, .008, gap) * (1.0 - cutface), vec2(-.012), vec2(.012));
    vec2 uv = clamp(SCREEN_UV + offset, vec2(.002), vec2(.998));
    float refracted_depth = linear_depth(uv, INV_PROJECTION_MATRIX);
    if (refracted_depth < surface_depth || refracted_depth > surface_depth + 20.0)
        uv = SCREEN_UV;
    else
        uv = mix(uv, SCREEN_UV, smoothstep(.03, .12,
            abs(refracted_depth - scene)));

    float path = bed_visible ? gap : column / max(cosine, .2);
    vec2 bend = uv - SCREEN_UV;
    vec2 bend_dir = length(bend) > 1e-6 ? normalize(bend) : vec2(0.0);
    vec3 behind = refracted_screen(uv, bend_dir, path, roughness);
    vec3 transmitted = mobile_transmission(behind, path, 1.0);
    // (1 - F) keeps the transmitted radiance and the pipeline's GGX specular a single
    // energy split: the surface reflectance is never counted twice.
    float body = 1.0 - fresnel;
    vec3 emission = transmitted * body;
    float alpha = smoothstep(.00002, .0005, gap);

    if (cutface > .5) {
        float meniscus = depthf <= .005 ? 0.0 : 1.0 - smoothstep(0.0, .006, UV.y);
        // A thin vertical sheet of water: the pipeline lights the glassy meniscus and
        // the bank still shows through it. The sheet has no thick column to absorb, so
        // the body weight of the horizontal surface does not apply here.
        emission = mix(behind, vec3(.75, .85, .90), meniscus * .35);
        roughness = max(roughness, .05);
        alpha = 1.0;
    } else if (underwater > .5) {
        emission = behind;
        alpha = 1.0;
    }

    vec3 world_normal = normalize((INV_VIEW_MATRIX * vec4(normal, 0.0)).xyz);
    if (water_debug == 1) emission = world_normal * .5 + .5;
    if (water_debug == 2) emission = transmitted;
    if (water_debug == 3) emission = mobile_reflection(reflect(
        -normalize(CAMERA_POSITION_WORLD - water_world_position), world_normal));
    if (water_debug == 4) emission = vec3(fresnel);

    ALBEDO = water_debug > 0 ? vec3(0.0) : vec3(0.012, 0.024, 0.018);
    METALLIC = 0.0;
    SPECULAR = water_debug > 0 ? 0.0 : WATER_SPECULAR;
    ROUGHNESS = water_debug > 0 ? 1.0 : roughness;
    EMISSION = emission;
    ALPHA = alpha;
}
