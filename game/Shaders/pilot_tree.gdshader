shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;

#include "res://Shaders/surface.gdshaderinc"
#include "res://Shaders/bark_fissure.gdshaderinc"
uniform sampler2D bark_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D bark_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D bark_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float hex_apothem = 6.92820323;
uniform float u_bark_fissures = 0.65;
// Gains are exposed for restrained/full material comparison captures.
uniform float u_leaf_vein_strength = 0.34;
uniform float u_leaf_relief = 0.06;
uniform float u_cutwood_structure = 0.78;

varying vec3 object_position;
varying vec3 world_position;
varying float planar_bark_uv;

void vertex() {
    object_position = VERTEX;
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    // Roots, armor and plates use the generic world X+Z / height face UVs.
    // Cylinders and swept branches author their own circumference / length UVs.
    float planar_error = length(UV - vec2(VERTEX.x + VERTEX.z, VERTEX.y));
    planar_bark_uv = 1.0 - smoothstep(0.001, 0.02, planar_error);
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
        // The mesh authors UV.x as distance from the tree's pith and UV.y as
        // height. Use that radial coordinate rather than object-space distance:
        // the latter is measured from the island corner, away from the tree.
        float radius = max(UV.x, 0.0);
        float broad_figure = fbm(vec2(radius * 2.4 + 1.7, UV.y * 0.32 + 8.2));
        float ring_warp = (vnoise(vec2(UV.y * 0.55 + 1.9, radius * 1.3)) - 0.5) * 0.16;
        float wood_noise = fbm(vec2(radius * 3.8 + 2.3, UV.y * 0.52 + 5.1));
        float ring_phase = fract(radius * 4.2 + ring_warp + (wood_noise - 0.5) * 0.14);
        float ring_line = 1.0 - smoothstep(0.045, 0.24, abs(ring_phase - 0.5));
        float fibre_warp = (fbm(vec2(UV.y * 0.38 + 4.0, radius * 2.7)) - 0.5) * 0.15;
        float fibres = fbm(vec2(radius * 9.0 + fibre_warp, UV.y * 0.75));
        float fine_fibre = vnoise(vec2(radius * 21.0 + fibre_warp * 1.7, UV.y * 1.25));
        float pith = 1.0 - smoothstep(0.018, 0.095, radius);
        float rays = smoothstep(0.76, 0.94,
            vnoise(vec2(radius * 4.5 + UV.y * 0.16, UV.y * 0.52 + 9.0)));
        vec3 heartwood = tissue * vec3(0.84, 0.75, 0.65);
        vec3 sapwood = tissue * vec3(1.02, 0.97, 0.84);
        // Broad, gently wandering heartwood/sapwood boundary adds recognizable
        // volume; its low-frequency displacement avoids a repeated contour.
        float sap_edge = 0.48 + (broad_figure - 0.5) * 0.30
            + (vnoise(vec2(UV.y * 0.8 + 13.0, radius * 1.1)) - 0.5) * 0.08;
        float sap = smoothstep(sap_edge - 0.12, sap_edge + 0.12, radius);
        vec3 wood_col = mix(heartwood, sapwood, sap);
        float structure = clamp(u_cutwood_structure, 0.0, 1.0);
        wood_col *= 0.97 + (fibres - 0.5) * 0.13 * structure
            + (fine_fibre - 0.5) * 0.035 * structure
            + (broad_figure - 0.5) * 0.16 * structure;
        wood_col *= 1.0 - ring_line * (0.065 + wood_noise * 0.025) * structure;
        wood_col *= 1.0 - rays * 0.055 * structure;
        wood_col = mix(wood_col, wood_col * vec3(0.64, 0.52, 0.39), pith * 0.43 * structure);
        ALBEDO = wood_col;
        ROUGHNESS = clamp(0.82 + (fibres - 0.5) * 0.08 * structure + ring_line * 0.035, 0.75, 0.94);
        SPECULAR = 0.10;
        BACKLIGHT = vec3(0.0);
        // Clean planar normal without bark bump map
    } else if (COLOR.a > 0.2 && COLOR.a < 0.4) {
        float porous = smoothstep(0.72, 0.88, vnoise(object_position.xz * 95.0 + object_position.y * 17.0));
        float mottling = fbm(object_position.xz * 13.0 + vec2(object_position.y * 2.0, -object_position.y));
        ALBEDO = tissue * (0.88 + mottling * 0.22) * (1.0 - porous * 0.10);
        ROUGHNESS = clamp(0.82 + porous * 0.10, 0.80, 0.96);
        SPECULAR = 0.10;
        BACKLIGHT = vec3(0.0);
    } else if (COLOR.a < 0.2) {
        // Blade UVs are normalized across the lamina (U) and base-to-tip (V).
        // A soft midrib and broken oblique laterals give direction without
        // turning every species into a bright, glossy leaf card.
        // Cull-disabled blades can have coincident front/back copies; orient
        // the rasterized back face toward the viewer to avoid inward lighting.
        if (!FRONT_FACING) NORMAL = -NORMAL;
        vec2 leaf_uv = UV;
        float across = abs(leaf_uv.x - 0.5) * 2.0;
        float taper = smoothstep(0.025, 0.20, leaf_uv.y) * (1.0 - smoothstep(0.84, 1.0, leaf_uv.y));
        float midrib = 1.0 - smoothstep(0.018, 0.11, abs(leaf_uv.x - 0.5));
        // Continuous soft ribs: sine-based distance stays differentiable at
        // every repeat, unlike a fract-threshold pattern that made dashed marks.
        float rib_phase = leaf_uv.y * 7.0 + across * 0.52
            + sin(leaf_uv.y * 5.0 + across * 1.8) * 0.055;
        float rib_sine = sin(rib_phase * 6.2831853);
        float lateral = exp(-rib_sine * rib_sine * 7.0);
        lateral *= smoothstep(0.22, 0.62, across) * (1.0 - smoothstep(0.84, 1.0, across));
        float leaf_noise = fbm(vec2(leaf_uv.x * 4.0 + 4.6, leaf_uv.y * 2.6 + 0.7));
        float edge = smoothstep(0.82, 1.0, across);
        float vein_strength = clamp(u_leaf_vein_strength, 0.0, 1.0);
        vec3 vein_tint = vec3(1.0) + vec3(0.055, 0.062, 0.028) * midrib
            - vec3(0.018, 0.015, 0.014) * lateral * vein_strength;
        vec3 leaf_col = tissue * (0.88 + leaf_noise * 0.20);
        leaf_col *= mix(vec3(1.0), vein_tint, taper * vein_strength);
        leaf_col *= 1.0 - edge * 0.045;
        ALBEDO = leaf_col;
        // Broad sheen and a shallow midrib/lateral relief add grazing-light
        // cues without relying on a brightness shift or narrow plastic glint.
        float relief_height = (midrib * 0.045 + lateral * 0.009) * taper * vein_strength;
        vec3 leaf_n = normalize(NORMAL);
        vec3 leaf_dpdx = dFdx(VERTEX);
        vec3 leaf_dpdy = dFdy(VERTEX);
        vec3 relief_grad = dFdx(relief_height) * cross(leaf_dpdy, leaf_n)
            + dFdy(relief_height) * cross(leaf_n, leaf_dpdx);
        float relief_area = max(length(cross(leaf_dpdx, leaf_dpdy)), 0.0001);
        vec3 relief_offset = relief_grad * (u_leaf_relief / relief_area);
        relief_offset *= min(1.0, 0.18 / max(length(relief_offset), 0.0001));
        NORMAL = normalize(leaf_n - relief_offset);
        ROUGHNESS = clamp(0.72 + leaf_noise * 0.08 + edge * 0.07, 0.70, 0.92);
        SPECULAR = mix(0.16, 0.23, taper * (1.0 - edge));
        BACKLIGHT = tissue * 0.075;
    } else {
        if (planar_bark_uv > 0.5 && wood > 0.5) {
            // Generic face UVs collapse vertical texture detail on horizontal roots
            // and make small armor plates look like woven bark. Triplanar sampling
            // keeps the scan and shallow relief at a consistent world scale.
            vec3 normal_world = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
            vec3 bark_normal_world;
            vec4 scan = triplanar(bark_col, bark_nrm, bark_rgh,
                world_position, normal_world, 4.0, bark_normal_world);
            float average = max(dot(textureLod(bark_col, vec2(0.5), 10.0).rgb,
                vec3(0.2126, 0.7152, 0.0722)), 0.025);
            vec3 scan_tint = clamp(scan.rgb / average, vec3(0.62), vec3(1.42));
            ALBEDO = tissue * mix(vec3(1.0), scan_tint, 0.72);
            ROUGHNESS = clamp(scan.a + 0.09, 0.78, 0.97);
            SPECULAR = 0.13;
            NORMAL = normalize((VIEW_MATRIX * vec4(
                normalize(mix(normal_world, bark_normal_world, 0.30)), 0.0)).xyz);
            BACKLIGHT = vec3(0.0);
        } else {
            vec2 grain = UV / 0.65;
            vec3 scan = texture(bark_col, grain).rgb;
            float average = max(dot(textureLod(bark_col, vec2(0.5), 10.0).rgb, vec3(0.2126, 0.7152, 0.0722)), 0.025);
            float micro = fbm(object_position.xz * 9.0 + vec2(object_position.y * 2.7, object_position.y * 1.3));
            vec3 bark = tissue * clamp(scan / average, vec3(0.36), vec3(1.65));
            vec3 bn = unpack_normal(texture(bark_nrm, grain).rgb);
            float raw_rgh = texture(bark_rgh, grain).r;
            vec3 cork, cork_normal;
            float cork_ao, cork_groove, cork_rough;
            bark_fissure_pbr(bark, bn, raw_rgh, grain, u_bark_fissures, 0.35, micro,
                cork, cork_normal, cork_rough, cork_ao, cork_groove);
            float bark_value = dot(bark, vec3(0.2126, 0.7152, 0.0722));
            float bark_tone = clamp(0.94 + (bark_value - 0.35) * 0.20 + micro * 0.10, 0.84, 1.08);
            ALBEDO = mix(tissue * (0.90 + micro * 0.12), cork * cork_ao * bark_tone, wood);
            ROUGHNESS = mix(0.76, clamp(cork_rough + 0.045, 0.62, 0.97), wood);
            float tree_bark_spec = mix(0.42, 0.14, smoothstep(0.55, 0.92, cork_rough));
            SPECULAR = mix(0.26, tree_bark_spec, wood);
            BACKLIGHT = tissue * (1.0 - wood) * 0.12;

            // Derivative tangent frame follows the cylindrical mesh UVs on trunk and branches.
            vec3 dpdx = dFdx(VERTEX), dpdy = dFdy(VERTEX);
            vec2 duvdx = dFdx(grain), duvdy = dFdy(grain);
            vec3 tangent = dpdx * duvdy.y - dpdy * duvdx.y;
            if (length(tangent) > 0.00001 && wood > 0.5) {
                tangent = normalize(tangent - NORMAL * dot(NORMAL, tangent));
                vec3 bitangent = normalize(cross(NORMAL, tangent));
                NORMAL = normalize(tangent * cork_normal.x +
                    bitangent * cork_normal.y +
                    NORMAL * cork_normal.z);
            }
        }
    }
}
