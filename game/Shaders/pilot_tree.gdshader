shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

#include "res://Shaders/surface.gdshaderinc"
uniform sampler2D bark_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D bark_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D bark_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float hex_apothem = 6.92820323;

varying float underside;
varying vec3 object_position;
varying vec3 world_position;

void vertex() {
    underside = max(-NORMAL.y, 0.0);
    object_position = VERTEX;
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    // Island slice plane: any part of the tree outside the regular hexagon gets sliced clean and flat
    float d = max(abs(world_position.z), 0.8660254037844386 * abs(world_position.x) + 0.5 * abs(world_position.z)) - hex_apothem;
    if (d > 0.001) {
        discard;
    }

    vec3 tissue = srgb_to_linear(COLOR.rgb);
    float wood = step(0.5, COLOR.a);
    bool is_cut_plane = (COLOR.a > 0.92 && COLOR.a < 0.98);

    if (is_cut_plane) {
        // Wood interior / cross-section revealed along slice plane:
        // Solid flat plane with growth rings and vertical grain.
        float dist = length(object_position.xz);
        float ring = sin(dist * 28.0 + vnoise(vec2(dist * 5.0, object_position.y * 1.5)) * 2.0);
        float grain_y = vnoise(vec2(dist * 40.0, object_position.y * 3.5));
        vec3 heartwood = mix(tissue * 1.25, vec3(0.52, 0.38, 0.25), 0.35);
        vec3 sapwood = mix(tissue * 1.45, vec3(0.65, 0.52, 0.36), 0.30);
        vec3 wood_col = mix(heartwood, sapwood, clamp(dist / 1.8, 0.0, 1.0));
        wood_col = mix(wood_col * 0.92, wood_col * 1.08, ring * 0.5 + 0.5);
        ALBEDO = wood_col * (0.94 + grain_y * 0.12);
        ROUGHNESS = 0.62;
        SPECULAR = 0.16;
        BACKLIGHT = vec3(0.0);
        // Clean planar normal without bark bump map
    } else if (COLOR.a > 0.2 && COLOR.a < 0.4) {
        float pores = smoothstep(0.68, 0.85, vnoise(object_position.xz * 260.0));
        float fibrous = vnoise(object_position.xz * 75.0 + object_position.y * 30.0);
        ALBEDO = tissue * (0.92 + fibrous * 0.13) * (1.0 - pores * underside * 0.18);
        ROUGHNESS = 0.88;
        SPECULAR = 0.12;
        BACKLIGHT = vec3(0.0);
    } else {
        vec2 grain = UV / 0.65;
        vec3 scan = texture(bark_col, grain).rgb;
        float average = max(dot(textureLod(bark_col, vec2(0.5), 10.0).rgb, vec3(0.2126, 0.7152, 0.0722)), 0.025);
        float micro = vnoise(object_position.xz * 31.0 + object_position.y * 7.0);
        vec3 bark = tissue * clamp(scan / average, vec3(0.36), vec3(1.65));
        ALBEDO = mix(tissue * (0.92 + micro * 0.14), bark, wood);
        ROUGHNESS = mix(0.72, max(0.78, texture(bark_rgh, grain).r), wood);
        SPECULAR = mix(0.26, 0.18, wood);
        BACKLIGHT = tissue * (1.0 - wood) * 0.12;

        // Derivative tangent frame follows the cylindrical mesh UVs on trunk, branches and buttresses.
        vec3 dpdx = dFdx(VERTEX), dpdy = dFdy(VERTEX);
        vec2 duvdx = dFdx(grain), duvdy = dFdy(grain);
        vec3 tangent = dpdx * duvdy.y - dpdy * duvdx.y;
        if (length(tangent) > 0.00001 && wood > 0.5) {
            tangent = normalize(tangent - NORMAL * dot(NORMAL, tangent));
            vec3 bitangent = normalize(cross(NORMAL, tangent));
            vec3 bump = unpack_normal(texture(bark_nrm, grain).rgb);
            NORMAL = normalize(tangent * bump.x * 0.65 + bitangent * bump.y * 0.65 + NORMAL * bump.z);
        }
    }
}
