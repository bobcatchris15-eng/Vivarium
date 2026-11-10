// Dedicated photorealistic fleshy fallen fruit / berry / seed shader.
// Supports dual-lobe specular (Burley base + waxy/wet cuticle clearcoat),
// rich subsurface scattering, back-transmittance, and instance tint/moisture modulation.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx, cull_back;

#include "res://Shaders/surface.gdshaderinc"

uniform float sss_strength : hint_range(0.0, 1.0) = 0.65;
uniform float sss_depth : hint_range(0.0001, 0.05) = 0.006;
uniform vec4 pulp_tint : source_color = vec4(0.92, 0.22, 0.15, 1.0);
uniform float base_roughness : hint_range(0.0, 1.0) = 0.38;
uniform float base_specular : hint_range(0.0, 1.0) = 0.35;
uniform float cuticle_wax : hint_range(0.0, 1.0) = 0.45;
uniform float cuticle_roughness : hint_range(0.0, 1.0) = 0.16;
uniform float skin_freckles : hint_range(0.0, 1.0) = 0.35;
uniform float peel_micro_relief : hint_range(0.0, 1.0) = 0.18;

varying vec3 instance_tint;
varying float instance_moisture;
varying vec3 local_coord;
varying vec3 world_position;

void vertex() {
    instance_tint = srgb_to_linear(COLOR.rgb);
    instance_moisture = clamp(COLOR.a, 0.0, 1.0);
    local_coord = VERTEX;
    world_position = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
    float cam_dist = length(CAMERA_POSITION_WORLD - world_position);
    float micro_fade = 1.0 - smoothstep(1.2, 4.5, cam_dist);
    // Nyquist limit the 140/m skin frequency so berries stop aliasing at range.
    vec3 extent = fwidth(local_coord);
    float pixel_extent = max(max(extent.x, extent.y), max(extent.z, 1e-5));
    float freq = min(140.0, 0.30 / pixel_extent);
    freq = mix(min(freq, 22.0), freq, micro_fade);
    vec3 p_local = local_coord * freq;
    float cell_noise = vnoise(p_local.xy + p_local.zz * 0.7);
    float fine_noise = vnoise(p_local.zx * 2.3 + vec2(7.1, 13.5));
    float freckle_mask = smoothstep(0.68, 0.85, fine_noise) * skin_freckles * micro_fade;

    float wet = instance_moisture;
    // Fleshy pulp gradient: skin variation around instance tint
    vec3 skin_color = instance_tint * mix(0.88, 1.08, cell_noise);
    // Subtle lenticels / freckles on the berry cuticle
    skin_color = mix(skin_color, skin_color * vec3(1.25, 0.85, 0.45), freckle_mask);
    // Waxy bloom: patchy dry farina lifts the skin and scatters the cuticle highlight.
    float bloom = smoothstep(0.42, 0.78, cell_noise) * (1.0 - wet) * cuticle_wax * micro_fade;
    skin_color = mix(skin_color, skin_color * vec3(1.05, 1.09, 1.12), bloom * 0.35);

    // Moisture-driven wetting: water film darkens diffuse reflection and deepens saturation
    vec3 wet_color = skin_color * mix(1.0, 0.72, wet);
    ALBEDO = wet_color;

    // Dual-lobe specular:
    // Lobe 1 (Base Epidermis/Pulp): softer, organic roughness
    ROUGHNESS = clamp(mix(base_roughness, base_roughness * 0.75, wet)
        + (cell_noise - 0.5) * 0.06 * micro_fade, 0.15, 0.95);
    SPECULAR = base_specular;

    // Lobe 2 (Waxy / Wet Epicuticular Cuticle Clearcoat):
    // Satiny wax bloom when dry, transitioning to a sharp glistening water glaze when wet
    CLEARCOAT = mix(cuticle_wax, 0.95, wet) * (1.0 - bloom * 0.25);
    CLEARCOAT_ROUGHNESS = clamp(mix(cuticle_roughness, 0.04, wet)
        + (cell_noise - 0.5) * 0.05 * micro_fade, 0.03, 0.90);

    // Subsurface Scattering (SSS) for fleshy berry pulp
    SSS_STRENGTH = clamp(sss_strength * mix(0.85, 1.0, 1.0 - wet * 0.15), 0.0, 1.0);

    // Rich subsurface transmittance: warm internal scattering modulated by fruit tint
    vec3 warm_scatter = normalize(instance_tint + vec3(0.18, 0.06, 0.02)) * length(instance_tint) * 1.5;
    vec3 sss_color = mix(warm_scatter, pulp_tint.rgb, 0.35);
    SSS_TRANSMITTANCE_COLOR = vec4(clamp(sss_color, vec3(0.05), vec3(1.0)), 1.0);
    SSS_TRANSMITTANCE_DEPTH = sss_depth;

    // Backlight transmission: warm pulp glow behind the thin, wet skin.
    float pulp_gain = 0.34 + 0.20 * clamp(dot(normalize(instance_tint + vec3(0.001)),
        vec3(0.3333)), 0.0, 1.0);
    BACKLIGHT = sss_color * mix(pulp_gain, pulp_gain * 1.15, wet);

    // Subtle epidermal micro-relief (peel texture), faded below the pixel footprint.
    vec2 bump_uv = local_coord.xy * freq * 1.42 + local_coord.yz * freq * 1.14;
    float n1 = vnoise(bump_uv);
    float n2 = vnoise(bump_uv + vec2(0.15, 0.0));
    float n3 = vnoise(bump_uv + vec2(0.0, 0.15));
    float relief = peel_micro_relief * mix(0.30, 1.0, micro_fade) * (1.0 - wet * 0.5);
    vec2 dndxy = vec2(n2 - n1, n3 - n1) * relief;
    NORMAL = normalize(NORMAL + vec3(dndxy, 0.0));

    // Specular AA: unresolved relief widens the highlight instead of aliasing.
    vec3 normal_dx = dFdx(NORMAL), normal_dy = dFdy(NORMAL);
    float normal_variance = dot(normal_dx, normal_dx) + dot(normal_dy, normal_dy);
    if (!(normal_variance >= 0.0 && normal_variance < 1e12)) normal_variance = 0.0;
    ROUGHNESS = clamp(sqrt(ROUGHNESS * ROUGHNESS + min(2.0 * normal_variance, .25)), 0.15, 1.0);

    // Ambient occlusion
    AO = 0.92 - bloom * 0.02;
    AO_LIGHT_AFFECT = 0.35;
}
