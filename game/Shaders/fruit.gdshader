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

void vertex() {
    instance_tint = srgb_to_linear(COLOR.rgb);
    instance_moisture = clamp(COLOR.a, 0.0, 1.0);
    local_coord = VERTEX;
}

void fragment() {
    // 3D coordinate-based procedural cellular noise for skin mottling & lenticels
    vec3 p_local = local_coord * 140.0;
    float cell_noise = vnoise(p_local.xy + p_local.zz * 0.7);
    float fine_noise = vnoise(p_local.zx * 2.3 + vec2(7.1, 13.5));
    float freckle_mask = smoothstep(0.68, 0.85, fine_noise) * skin_freckles;

    // Fleshy pulp gradient: skin variation around instance tint
    vec3 skin_color = instance_tint * mix(0.88, 1.08, cell_noise);
    // Subtle lenticels / freckles on the berry cuticle
    skin_color = mix(skin_color, skin_color * vec3(1.25, 0.85, 0.45), freckle_mask);

    // Moisture-driven wetting: water film darkens diffuse reflection and deepens saturation
    float wet = instance_moisture;
    vec3 wet_color = skin_color * mix(1.0, 0.72, wet);
    ALBEDO = wet_color;

    // Dual-lobe specular:
    // Lobe 1 (Base Epidermis/Pulp): softer, organic roughness
    ROUGHNESS = clamp(mix(base_roughness, base_roughness * 0.75, wet) + (cell_noise - 0.5) * 0.06, 0.15, 0.95);
    SPECULAR = base_specular;

    // Lobe 2 (Waxy / Wet Epicuticular Cuticle Clearcoat):
    // Satiny wax bloom when dry, transitioning to a sharp glistening water glaze when wet
    CLEARCOAT = mix(cuticle_wax, 0.95, wet);
    CLEARCOAT_ROUGHNESS = mix(cuticle_roughness, 0.04, wet);

    // Subsurface Scattering (SSS) for fleshy berry pulp
    SSS_STRENGTH = clamp(sss_strength * mix(0.85, 1.0, 1.0 - wet * 0.15), 0.0, 1.0);

    // Rich subsurface transmittance: warm internal scattering modulated by fruit tint
    vec3 warm_scatter = normalize(instance_tint + vec3(0.18, 0.06, 0.02)) * length(instance_tint) * 1.5;
    vec3 sss_color = mix(warm_scatter, pulp_tint.rgb, 0.35);
    SSS_TRANSMITTANCE_COLOR = vec4(clamp(sss_color, vec3(0.05), vec3(1.0)), 1.0);
    SSS_TRANSMITTANCE_DEPTH = sss_depth;

    // Backlight transmission through fleshy berry pulp
    BACKLIGHT = sss_color * 0.32;

    // Subtle epidermal micro-relief (peel texture)
    vec2 bump_uv = local_coord.xy * 200.0 + local_coord.yz * 160.0;
    float n1 = vnoise(bump_uv);
    float n2 = vnoise(bump_uv + vec2(0.15, 0.0));
    float n3 = vnoise(bump_uv + vec2(0.0, 0.15));
    vec2 dndxy = vec2(n2 - n1, n3 - n1) * peel_micro_relief * (1.0 - wet * 0.5);
    NORMAL = normalize(NORMAL + vec3(dndxy, 0.0));

    // Ambient occlusion
    AO = 0.92;
    AO_LIGHT_AFFECT = 0.35;
}
