// Decomposing leaf litter; per-instance pigment follows stored litter and habitat moisture.
shader_type spatial;
render_mode cull_disabled, diffuse_burley, specular_schlick_ggx;
#include "res://Shaders/surface.gdshaderinc"
varying vec3 pigment;
void vertex() { pigment = srgb_to_linear(COLOR.rgb); }
void fragment() {
    if (!FRONT_FACING) NORMAL = -NORMAL;
    float across = abs(UV.y - 0.5);
    float midrib = 1.0 - smoothstep(0.012, 0.048, across);
    float vein_phase = UV.x * 7.0 - across * 3.2;
    float vein_width = max(fwidth(vein_phase), 0.035);
    float veins = (1.0 - smoothstep(0.025, 0.025 + vein_width, abs(fract(vein_phase) - 0.5)))
        * smoothstep(0.025, 0.09, across) * (1.0 - smoothstep(0.36, 0.5, across));
    float wear = vnoise(UV * vec2(21.0, 13.0) + vec2(5.7, 13.4));
    float freckles = smoothstep(0.56, 0.76, wear);
    float edge = smoothstep(0.30, 0.5, across);
    ALBEDO = pigment * mix(0.74, 1.10, wear) * mix(1.0, 0.62, freckles * 0.55);
    ALBEDO *= 1.0 + midrib * 0.10 + veins * 0.07 + edge * 0.08;
    ROUGHNESS = clamp(0.89 + wear * 0.09 - veins * 0.05, 0.78, 1.0);
    SPECULAR = 0.22;
    BACKLIGHT = pigment * vec3(0.8, 0.55, 0.22) * 0.07 * (1.0 - midrib);
    AO = 0.86 + across * 0.20;
    AO_LIGHT_AFFECT = 0.15;
}
