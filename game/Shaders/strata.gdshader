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
uniform float banding_strength = 0.22;
uniform vec3 rim_color : source_color = vec3(0.16, 0.20, 0.08);
uniform float tile_metres = 1.1;
uniform sampler2D layer_palette : source_color, filter_nearest, repeat_disable;
uniform int layer_count = 4;

varying vec3 world_pos;

void vertex() { world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }

float lum(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

vec3 geologic_tint(float i) {
    float count = float(max(layer_count, 1));
    return textureLod(layer_palette, vec2((clamp(i, 0.0, count - 1.0) + 0.5) / count, 0.5), 0.0).rgb;
}

void fragment() {
    float along = UV.x, depth = UV.y, layer = UV2.x, inLayer = UV2.y;
    // This parameter is shared by both vertices at each horizon: layer i at depth 1
    // and layer i+1 at depth 0 both resolve to the same material state.
    float layerPosition = layer + inLayer;
    vec3 layerCol = geologic_tint(layer);
    float layerCountMax = float(max(layer_count - 1, 0));
    float contactNoise = vnoise(vec2(along * 0.18, depth * 0.34) + 12.4);
    float contactWidth = mix(0.18, 0.30, contactNoise);
    if (layer > 0.5) {
        vec3 upperContact = mix(geologic_tint(layer - 1.0), layerCol, 0.5);
        layerCol = mix(upperContact, layerCol, smoothstep(0.0, contactWidth, inLayer));
    }
    if (layer < layerCountMax) {
        vec3 lowerContact = mix(layerCol, geologic_tint(layer + 1.0), 0.5);
        layerCol = mix(layerCol, lowerContact, smoothstep(1.0 - contactWidth, 1.0, inLayer));
    }
    vec3 wn = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
    vec3 p = world_pos / tile_metres;

    // pick the material for this horizon: 0-1 soil, 2 gravelly clay, 3+ bedrock (soft blend at the seams)
    vec3 n_a, n_b;
    // layer + normalized depth is continuous at every shared horizon: the lower vertex of i
    // and upper vertex of i+1 both evaluate to the same material position.
    float wSoil = 1.0 - smoothstep(1.8, 2.35, layerPosition);
    vec4 a = triplanar(soil_col, soil_nrm, soil_rgh, p, wn, 6.0, n_a);
    vec4 b = triplanar(gravel_col, gravel_nrm, gravel_rgh, p * 1.6, wn, 6.0, n_b);
    float wGravel = mix(0.14, 1.0, 1.0 - wSoil);            // gradual grit increase through the clay horizon
    vec4 t = mix(a, b, wGravel);
    vec3 n = normalize(mix(n_a, n_b, wGravel));

    // keep the scan's detail and a little of its hue, but take the overall colour from the layer
    float avg = max(lum(textureLod(soil_col, vec2(0.5), 10.0).rgb), 0.02);
    vec3 detail = t.rgb / avg;
    layerCol = mix(vec3(lum(layerCol)), layerCol, 0.6);             // content colours are illustrative; tone them down
    vec3 col = layerCol * mix(vec3(lum(detail)), detail, 0.45);

    // Fine sediment changes: warped, diffuse laminae add variation without drawing parallel stripes.
    vec2 face = vec2(along, depth);
    vec2 broadWarp = vec2(vnoise(face * vec2(0.11, 0.28) + 2.3),
                          vnoise(face * vec2(0.13, 0.31) + 8.1)) - 0.5;
    float laminaA = vnoise(face * vec2(0.19, 1.35) + broadWarp * 1.7 + layerPosition * 3.7);
    float laminaB = vnoise(face * vec2(0.34, 2.8) - broadWarp * 0.8 + 14.2);
    float lamina = (laminaA - 0.5) * 1.15 + (laminaB - 0.5) * 0.28;
    float broadDeposit = vnoise(face * vec2(0.31, 0.78) + broadWarp * 1.8 + layerPosition * 6.3);
    float mineralPatch = vnoise(face * vec2(0.62, 1.16) - broadWarp * 1.1 + 23.0);
    col *= 1.0 + banding_strength * 0.16 * lamina * min(layerPosition + 0.5, 2.0);
    col *= mix(0.78, 1.18, smoothstep(0.20, 0.82, broadDeposit));
    col *= mix(0.92, 1.08, mineralPatch);

    // Gravelly clasts and sediment lenses are sparse, rounded inclusions rather than a noisy overlay.
    vec2 lensP = face * vec2(0.46, 0.68);
    float lensShape = vnoise(lensP + vec2(vnoise(lensP * 0.31 + 4.0),
                                             vnoise(lensP * 0.31 + 17.0)) * 0.72);
    float lens = smoothstep(0.57, 0.77, lensShape) * smoothstep(0.18, 0.42, depth);
    lens *= smoothstep(0.8, 1.8, layerPosition + 0.15);
    vec4 pebbleCells = moss_cells(face * vec2(1.55, 2.0), 0.18, 0.16);
    float pebbleEdgeNoise = vnoise(pebbleCells.yz * 3.4 + floor(lensP) * 1.73 + 8.0);
    float pebble = smoothstep(0.23, 0.80, pebbleCells.x + (pebbleEdgeNoise - 0.5) * 0.42)
                 * step(0.30, fract(pebbleCells.w * 53.1))
                 * smoothstep(0.24, 0.66, depth)
                 * smoothstep(0.7, 1.5, layerPosition + 0.15);
    vec3 clastTone = mix(vec3(0.16, 0.15, 0.13), vec3(0.34, 0.30, 0.24),
                         vnoise(lensP * 0.42 + 31.0));
    col = mix(col, clastTone * layerCol * 1.7, lens * 0.68);
    col = mix(col, clastTone, pebble * 0.52);

    // Rootlets intrude from the surface in irregular branching-looking filaments; their coverage fades
    // quickly with depth and varies by face position instead of repeating as a decorative stripe.
    float rootMask = 0.0;
    float rootSlot = floor(along / 7.0);
    for (int i = -1; i <= 1; i++) {
        float slot = rootSlot + float(i);
        float seed = hash21(vec2(slot, 83.2));
        float origin = slot * 7.0 + mix(0.7, 6.3, seed);
        float sway = (vnoise(vec2(slot * 1.73, depth * 0.62 + 2.0)) - 0.5) * 1.0;
        float trunkCenter = origin + sway + depth * 0.20;
        float trunk = (1.0 - smoothstep(0.030, 0.098, abs(along - trunkCenter)))
                    * (1.0 - smoothstep(0.92, 1.38, depth));
        float branchStart = 0.16 + seed * 0.36;
        float branchDepth = max(depth - branchStart, 0.0);
        float branchCenter = trunkCenter + branchDepth * mix(-0.46, 0.46, fract(seed * 13.7));
        float branch = (1.0 - smoothstep(0.022, 0.064, abs(along - branchCenter)))
                     * smoothstep(branchStart, branchStart + 0.10, depth)
                     * (1.0 - smoothstep(0.72, 1.06, depth));
        rootMask = max(rootMask, max(trunk, branch));
    }
    rootMask *= 0.55 + 0.45 * vnoise(face * vec2(0.34, 0.72) + 27.0);
    col = mix(col, col * vec3(0.36, 0.37, 0.27), rootMask * 0.82);

    // A few narrow, branching weathering seams show through the deeper clay and rock.
    vec2 crackP = face * vec2(0.37, 1.62);
    vec2 crackWarp = vec2(vnoise(crackP * 0.42 + 7.2), vnoise(crackP * 0.46 + 21.8)) - 0.5;
    float crackRidge = abs(vnoise(crackP * vec2(2.4, 2.9) + crackWarp * 0.9 + 44.0) - 0.5);
    float crack = (1.0 - smoothstep(0.010, 0.028, crackRidge))
                * smoothstep(1.2, 2.2, layerPosition + 0.2)
                * (0.45 + 0.55 * vnoise(crackP * 0.24 + 51.0));
    col = mix(col, col * vec3(0.54, 0.52, 0.47), crack * 0.48);

    // Horizon contacts stay readable but are no longer inked as perfect dark diagram lines. The wall geometry
    // itself now meanders these boundaries coherently through the specimen.
    col *= mix(0.90, 1.0, smoothstep(0.0, 0.018, inLayer + (layer < 0.5 ? 1.0 : 0.0)));

    // A restrained organic-rich skin just below the living surface; low-frequency variation avoids a noisy trim.
    float rootOrganic = 0.88 + 0.12 * vnoise(vec2(along * 0.8, depth * 4.0) + 9.0);
    col = mix(srgb_to_linear(rim_color) * rootOrganic, col, smoothstep(0.0, 0.038, depth));
    ALBEDO = col;
    EMISSION = vec3(0.0);
    ROUGHNESS = clamp(mix(t.a, 0.94, max(lens * 0.42, rootMask * 0.14)), 0.6, 1.0);
    SPECULAR = 0.25;
    NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
}
