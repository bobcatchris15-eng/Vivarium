// Specimen cut faces. Each band uses a photo-scanned material that suits its horizon (soil, gravelly clay,
// bedrock), tinted by the layer colour from content so the horizons stay distinct. A dark line marks each
// layer's top and a root mat sits just under the surface, so the section reads as a deliberate cut.
// UV = (distance along perimeter, depth below surface); UV2 = (layer index, depth within layer).
shader_type spatial;
render_mode diffuse_burley;

#include "res://Shaders/surface.gdshaderinc"

uniform sampler2D soil_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float banding_strength = 0.22;
uniform vec3 rim_color : source_color = vec3(0.16, 0.20, 0.08);
uniform float tile_metres = 1.1;

varying vec3 world_pos;

void vertex() { world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }

float lum(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

void fragment() {
    float along = UV.x, depth = UV.y, layer = UV2.x, inLayer = UV2.y;
    vec3 layerCol = srgb_to_linear(COLOR.rgb);   // content colours are sRGB
    vec3 wn = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
    vec3 p = world_pos / tile_metres;

    // pick the material for this horizon: 0-1 soil, 2 gravelly clay, 3+ bedrock (soft blend at the seams)
    vec3 n_a, n_b;
    float wSoil = 1.0 - smoothstep(1.5, 2.0, layer), wRock = smoothstep(2.5, 3.0, layer);
    vec4 a = wRock > 0.5 ? triplanar(rock_col, rock_nrm, rock_rgh, p * 0.8, wn, 6.0, n_a)
                         : triplanar(soil_col, soil_nrm, soil_rgh, p, wn, 6.0, n_a);
    vec4 b = triplanar(gravel_col, gravel_nrm, gravel_rgh, p * 1.6, wn, 6.0, n_b);
    float wGravel = (1.0 - wSoil) * (1.0 - wRock) + wSoil * 0.18 * layer;   // subsoil gets a little grit
    vec4 t = mix(a, b, wGravel);
    vec3 n = normalize(mix(n_a, n_b, wGravel));

    // keep the scan's detail and a little of its hue, but take the overall colour from the layer
    float avg = max(lum(wRock > 0.5 ? textureLod(rock_col, vec2(0.5), 10.0).rgb : textureLod(soil_col, vec2(0.5), 10.0).rgb), 0.02);
    vec3 detail = t.rgb / avg;
    layerCol = mix(vec3(lum(layerCol)), layerCol, 0.6);             // content colours are illustrative; tone them down
    vec3 col = layerCol * mix(vec3(lum(detail)), detail, 0.45);
    col = mix(col, t.rgb * 0.85, wRock * 0.6);                        // bedrock mostly shows the real stone

    // Subtle anisotropic laminae: coherent, irregular deposition rather than a periodic sine ruler.
    float warp = (vnoise(vec2(along * 0.43, depth * 0.9) + layer * 2.7) - 0.5) * 0.16;
    float laminaA = vnoise(vec2(along * 0.32, (depth + warp) * 8.2) + layer * 5.1);
    float laminaB = vnoise(vec2(along * 0.17 + 4.2, (depth - warp * 0.4) * 17.3));
    float lamina = (laminaA - 0.5) * 1.35 + (laminaB - 0.5) * 0.35;
    col *= 1.0 + banding_strength * 0.10 * lamina * min(layer + 0.5, 2.0);

    // Horizon contacts stay readable but are no longer inked as perfect dark diagram lines. The wall geometry
    // itself now meanders these boundaries coherently through the specimen.
    col *= mix(0.90, 1.0, smoothstep(0.0, 0.018, inLayer + (layer < 0.5 ? 1.0 : 0.0)));

    // A restrained organic-rich skin just below the living surface; low-frequency variation avoids a noisy trim.
    float rootOrganic = 0.88 + 0.12 * vnoise(vec2(along * 0.8, depth * 4.0) + 9.0);
    col = mix(srgb_to_linear(rim_color) * rootOrganic, col, smoothstep(0.0, 0.038, depth));
    ALBEDO = col;
    EMISSION = vec3(0.0);
    ROUGHNESS = clamp(t.a, 0.6, 1.0);
    SPECULAR = 0.25;
    NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
}
