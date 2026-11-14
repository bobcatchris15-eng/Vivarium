// Specimen cut faces. Each band uses a photo-scanned material that suits its horizon (soil, gravelly clay,
// bedrock), tinted by the layer colour from content so the horizons stay distinct. POM, real normal
// perturbation and a groundwater clearcoat let the displaced 3D wall read as damp geology rather than paint.
// UV = (distance along perimeter, depth below surface); UV2 = (layer index, depth within layer).
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx, ambient_light_disabled;

#include "res://Shaders/surface.gdshaderinc"
#include "res://Shaders/repr_skyvis.gdshaderinc"

uniform sampler2D soil_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_hgt : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D soil_ao : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;

uniform sampler2D gravel_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_hgt : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D gravel_ao : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;

uniform sampler2D rock_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_hgt : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_ao : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;

uniform float banding_strength = 0.22;
uniform vec3 rim_color : source_color = vec3(0.16, 0.20, 0.08);
uniform float tile_metres = 1.1;
uniform sampler2D layer_palette : source_color, filter_nearest, repeat_disable;
uniform int layer_count = 4;

// Full hex perimeter in metres. IslandRenderer binds 6 * radius. The U coordinate is normalised against
// this and rounded to an integer repeat count, so the first/last wall vertex share a texel phase.
uniform float perimeter_metres = 24.0;
uniform float water_table = -1000.0;
uniform float pom_height = 0.022;
uniform int pom_steps = 20;

varying vec3 world_pos;
varying vec3 ts_view;
varying vec3 ts_light;

float lum(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

vec3 geologic_tint(float i) {
    float count = float(max(layer_count, 1));
    return textureLod(layer_palette, vec2((clamp(i, 0.0, count - 1.0) + 0.5) / count, 0.5), 0.0).rgb;
}

// World-space bump from a scalar height field. Screen derivatives of world position and height give the
// surface gradient; subtracting it tilts the normal without relying on tangent-space UVs.
vec3 bump_normal(vec3 n, vec3 p, float h) {
    vec3 px = dFdx(p), py = dFdy(p);
    vec3 tx = px - n * dot(n, px);
    vec3 ty = py - n * dot(n, py);
    float a = dot(tx, tx), b = dot(tx, ty), c = dot(ty, ty);
    float det = a * c - b * b;
    float hx = dFdx(h), hy = dFdy(h);
    float ga = 0.0, gb = 0.0;
    if (abs(det) > 1e-12) {
        ga = (hx * c - hy * b) / det;
        gb = (hy * a - hx * b) / det;
    }
    vec3 g = tx * ga + ty * gb;
    return normalize(n - g);
}

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    vec3 view_dir_view = normalize((inverse(MODEL_MATRIX) * vec4(CAMERA_POSITION_WORLD, 1.0)).xyz - VERTEX);
    ts_view = vec3(dot(view_dir_view, TANGENT), dot(view_dir_view, BINORMAL), dot(view_dir_view, NORMAL));
    vec3 sun_world = normalize(vec3(0.35, 0.75, 0.45));
    vec3 light_dir_view = normalize((inverse(MODEL_MATRIX) * vec4(sun_world, 0.0)).xyz);
    ts_light = vec3(dot(light_dir_view, TANGENT), dot(light_dir_view, BINORMAL), dot(light_dir_view, NORMAL));
}

void fragment() {
    float along = UV.x, depth = UV.y, layer = UV2.x, inLayer = UV2.y;
    float cam_dist = distance(CAMERA_POSITION_WORLD, world_pos);
    // This parameter is shared by both vertices at each horizon: layer i at depth 1
    // and layer i+1 at depth 0 both resolve to the same material state.
    vec2 face = vec2(along, depth);
    // Horizon boundaries are warped in the shader as well (function of face position only, so both vertex
    // copies at a seam agree): uneven, never straight.
    float hWarp = (vnoise(face * vec2(0.55, 1.9) + 3.1) - 0.5) * 0.46
                + (vnoise(face * vec2(1.7, 4.3) + 9.7) - 0.5) * 0.16;
    float layerPosition = layer + inLayer + hWarp * smoothstep(0.02, 0.2, depth);
    // Soil profile palette: dark organic O/A, brown mineral subsoil, tan gravelly C, grey-brown rock.
    vec3 hzO = vec3(0.045, 0.030, 0.020);
    vec3 hzB = vec3(0.26, 0.170, 0.105);
    vec3 hzC = vec3(0.34, 0.27, 0.19);
    vec3 hzR = vec3(0.30, 0.285, 0.265);
    vec3 layerCol = mix(hzO, hzB, smoothstep(0.62, 1.25, layerPosition));
    layerCol = mix(layerCol, hzC, smoothstep(1.55, 2.30, layerPosition));
    layerCol = mix(layerCol, hzR, smoothstep(2.75, 3.35, layerPosition));
    layerCol *= mix(vec3(1.0), geologic_tint(layer) / max(lum(geologic_tint(layer)), 0.02), 0.12);
    vec3 wn = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);

    // Material weights: topsoil/subsoil share the soil scan, the C horizon uses gravel, and the
    // bedrock uses fractured rock. The transitions sit on the contacts, at the same layerPosition
    // value for both duplicated horizon vertices, so materials never pop across a seam.
    float wRock = smoothstep(2.75, 3.35, layerPosition);
    float wGravel = smoothstep(1.78, 2.22, layerPosition) * (1.0 - wRock);
    float wSoil = clamp(1.0 - wGravel - wRock, 0.0, 1.0);

    // Seamless UVs. U wraps an integer number of times around the hex perimeter; V follows depth.
    float perimeter = max(perimeter_metres, 0.001);
    float soilTile = max(tile_metres, 0.2);
    float gravelTile = max(tile_metres / 1.35, 0.2);
    float rockTile = max(tile_metres / 0.85, 0.2);
    float soilRepeat = max(1.0, round(perimeter / soilTile));
    float gravelRepeat = max(1.0, round(perimeter / gravelTile));
    float rockRepeat = max(1.0, round(perimeter / rockTile));
    vec2 uvSoil = vec2(along / perimeter * soilRepeat, depth / soilTile);
    vec2 uvGravel = vec2(along / perimeter * gravelRepeat, depth / gravelTile);
    vec2 uvRock = vec2(along / perimeter * rockRepeat, depth / rockTile);

    // Contact-refining POM on each active horizon material.
    vec2 pomSoil = uvSoil, pomGravel = uvGravel, pomRock = uvRock;
    float pomShadow = 1.0;
    float shSoil = 1.0, shGravel = 1.0, shRock = 1.0;
    if (pom_height > 0.0001 && cam_dist < 5.0) {
        if (wSoil > 0.02) pomSoil = pom_raymarch(soil_hgt, uvSoil, ts_view, pom_height, pom_steps, shSoil, ts_light);
        if (wGravel > 0.02) pomGravel = pom_raymarch(gravel_hgt, uvGravel, ts_view, pom_height * 0.85, pom_steps, shGravel, ts_light);
        if (wRock > 0.02) pomRock = pom_raymarch(rock_hgt, uvRock, ts_view, pom_height * 0.70, pom_steps, shRock, ts_light);
        pomShadow = shSoil * wSoil + shGravel * wGravel + shRock * wRock;
    }

    vec4 soil = vec4(texture(soil_col, pomSoil).rgb, texture(soil_rgh, pomSoil).r);
    vec4 gravel = vec4(texture(gravel_col, pomGravel).rgb, texture(gravel_rgh, pomGravel).r);
    // Rock/gravel: second lookup at another scale and rotation, blended by low-frequency noise (no visible tile).
    float untileM = smoothstep(0.35, 0.65, vnoise(face * 0.8 + 41.0));
    vec2 pomRock2 = rot2(pomRock * 0.77 + vec2(0.37, 0.61), 1.1);
    vec2 pomGravel2 = rot2(pomGravel * 0.83 + vec2(0.21, 0.52), 0.7);
    gravel = mix(gravel, vec4(texture(gravel_col, pomGravel2).rgb, texture(gravel_rgh, pomGravel2).r), untileM);
    vec4 rock = vec4(mix(texture(rock_col, pomRock).rgb, texture(rock_col, pomRock2).rgb, untileM),
                     mix(texture(rock_rgh, pomRock).r, texture(rock_rgh, pomRock2).r, untileM));
    vec4 t = soil * wSoil + gravel * wGravel + rock * wRock;
    float matAo = texture(soil_ao, pomSoil).r * wSoil
                + texture(gravel_ao, pomGravel).r * wGravel
                + texture(rock_ao, pomRock).r * wRock;

    // Keep the scan's detail and a little of its hue, but take the overall colour from the layer.
    float avg = max(lum(textureLod(soil_col, vec2(0.5), 10.0).rgb) * wSoil
                  + lum(textureLod(gravel_col, vec2(0.5), 10.0).rgb) * wGravel
                  + lum(textureLod(rock_col, vec2(0.5), 10.0).rgb) * wRock, 0.02);
    vec3 detail = t.rgb / avg;
    vec3 col = layerCol * mix(vec3(lum(detail)), detail, 0.12);

    // Fine sediment changes: warped, diffuse laminae add variation without drawing parallel stripes.
    vec2 broadWarp = vec2(vnoise(face * vec2(0.11, 0.28) + 2.3),
                          vnoise(face * vec2(0.13, 0.31) + 8.1)) - 0.5;
    float laminaA = vnoise(face * vec2(0.19, 1.35) + broadWarp * 1.7 + layerPosition * 3.7);
    float laminaB = vnoise(face * vec2(0.34, 2.8) - broadWarp * 0.8 + 14.2);
    float lamina = (laminaA - 0.5) * 1.15 + (laminaB - 0.5) * 0.28;
    float broadDeposit = vnoise(face * vec2(0.31, 0.78) + broadWarp * 1.8 + layerPosition * 6.3);
    float mineralPatch = vnoise(face * vec2(0.62, 1.16) - broadWarp * 1.1 + 23.0);
    col *= 1.0 + banding_strength * 0.16 * lamina * min(layerPosition + 0.5, 2.0);
    col *= mix(0.86, 1.12, smoothstep(0.20, 0.82, broadDeposit));
    col *= mix(0.92, 1.08, mineralPatch);

    // Gravelly clasts and sediment lenses are sparse, rounded inclusions rather than a noisy overlay.
    vec2 lensP = face * vec2(0.46, 0.68);
    float lensShape = vnoise(lensP + vec2(vnoise(lensP * 0.31 + 4.0),
                                             vnoise(lensP * 0.31 + 17.0)) * 0.72);
    float lens = smoothstep(0.57, 0.77, lensShape) * smoothstep(0.18, 0.42, depth);
    lens *= smoothstep(0.8, 1.8, layerPosition + 0.15);
    vec4 pebbleCells = moss_cells(face * vec2(4.2, 5.2), 0.16, 0.14);
    float pebbleEdgeNoise = vnoise(pebbleCells.yz * 3.4 + floor(lensP) * 1.73 + 8.0);
    float pebble = smoothstep(0.45, 0.62, pebbleCells.x + (pebbleEdgeNoise - 0.5) * 0.25)
                 * step(0.55, fract(pebbleCells.w * 53.1))
                 * smoothstep(0.10, 0.30, depth)
                 * smoothstep(0.35, 0.9, layerPosition);
    vec3 clastTone = mix(vec3(0.13, 0.105, 0.08), vec3(0.30, 0.24, 0.17),
                         vnoise(lensP * 0.42 + 31.0));
    col = mix(col, clastTone * layerCol * 1.7, lens * 0.68);
    col = mix(col, clastTone * (0.6 + 0.9 * fract(pebbleCells.w * 7.3)) * mix(0.55, 1.15, smoothstep(0.45, 0.95, pebbleCells.x) * (0.8 + 0.4 * pebbleCells.y)) * mix(0.8, 1.2, vnoise(face * 55.0)), pebble * 0.9);
    // Whimsy: faint mineral glints in the stones.
    float glint = step(0.93, hash21(floor(face * 90.0) + 3.0)) * pebble * smoothstep(0.5, 0.9, vnoise(face * 14.0));
    col += vec3(0.55, 0.62, 0.7) * glint * 0.6;

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
    col = mix(col, mix(col * 0.5, vec3(0.30, 0.20, 0.11), 0.6), rootMask * 0.75);

    // A few narrow, branching weathering seams show through the deeper clay and rock.
    vec2 crackP = face * vec2(0.37, 1.62);
    vec2 crackWarp = vec2(vnoise(crackP * 0.42 + 7.2), vnoise(crackP * 0.46 + 21.8)) - 0.5;
    float crackRidge = abs(vnoise(crackP * vec2(2.4, 2.9) + crackWarp * 0.9 + 44.0) - 0.5);
    float crack = (1.0 - smoothstep(0.010, 0.028, crackRidge))
                * smoothstep(1.2, 2.2, layerPosition + 0.2)
                * (0.45 + 0.55 * vnoise(crackP * 0.24 + 51.0));
    col = mix(col, col * vec3(0.54, 0.52, 0.47), crack * 0.48);

    // Real geometric normal perturbation: laminae, clast lenses, root channels and weathering seams
    // are shallow height fields rather than albedo-only paint.
    float detailH = lamina * 0.0012
                  + (broadDeposit - 0.5) * 0.0015
                  + lens * 0.0025
                  - rootMask * 0.0045
                  - crack * 0.0070;
    // Bedding planes in the bedrock read as shallow horizontal steps in the lighting as well.
    detailH += wRock * sin(depth * 38.0 + vnoise(vec2(along * 0.7, 5.1)) * 6.0) * 0.0025;

    vec3 nSoil = cotangent_normal(wn, world_pos, uvSoil, unpack_normal(texture(soil_nrm, pomSoil).rgb));
    vec3 nGravel = cotangent_normal(wn, world_pos, uvGravel, unpack_normal(texture(gravel_nrm, pomGravel).rgb));
    vec3 nRock = cotangent_normal(wn, world_pos, uvRock, unpack_normal(texture(rock_nrm, pomRock).rgb));
    vec3 n = normalize(nSoil * wSoil + nGravel * wGravel + nRock * wRock);
    n = bump_normal(n, world_pos, detailH);

    // Horizon contacts stay readable but are no longer inked as perfect dark diagram lines. The wall geometry
    // itself now meanders these boundaries coherently through the specimen.
    col *= mix(0.90, 1.0, smoothstep(0.0, 0.018, inLayer + (layer < 0.5 ? 1.0 : 0.0)));

    // A restrained organic-rich skin just below the living surface; low-frequency variation avoids a noisy trim.
    float rootOrganic = 0.88 + 0.12 * vnoise(vec2(along * 0.8, depth * 4.0) + 9.0);
    vec3 litter = mix(vec3(0.20, 0.10, 0.04), vec3(0.09, 0.07, 0.03), vnoise(vec2(along * 9.0, depth * 60.0) + 5.0));
    float litterEdge = 0.020 + 0.016 * vnoise(vec2(along * 3.1, 2.2));
    col = mix(litter * rootOrganic, col, smoothstep(litterEdge * 0.6, litterEdge * 1.5, depth));
    col = mix(rim_color * 0.6 * rootOrganic, col, smoothstep(0.0, 0.006, depth));

    // Turf overhang: soft contact shadow under the living crown along the top
    // cut rim, broken up so it never reads as an inked border.
    float rim_shadow = (1.0 - smoothstep(0.0, 0.035, depth))
        * (0.70 + 0.30 * vnoise(vec2(along * 1.3, 4.7)));
    col *= mix(1.0, 0.78, rim_shadow);

    // Groundwater seepage: a noisy water line plus vertical damp streaks below the table. Peak clearcoat
    // is 0.65 with a much tighter lobe than the dry soil, and the albedo darkens as the pores saturate.
    float tableLine = water_table + (vnoise(vec2(along * 1.7, 7.3)) - 0.5) * 0.08;
    float seep = 1.0 - smoothstep(tableLine - 0.12, tableLine + 0.02, world_pos.y);
    float streaks = vnoise(vec2(along * 6.5, max(water_table - world_pos.y, 0.0) * 0.9 + 3.0));
    seep *= mix(0.45, 1.0, smoothstep(0.22, 0.82, streaks));
    col *= mix(vec3(1.0), vec3(0.48, 0.47, 0.44), seep * 0.5);

    float rough = clamp(mix(t.a, 0.94, max(lens * 0.42, rootMask * 0.14)), 0.55, 1.0);
    rough = mix(rough, 0.16, seep * 0.9);

    ALBEDO = col;
    EMISSION = col * vec3(0.30, 0.31, 0.33) * mix(0.6, 1.0, matAo); // wall ambient supplied here: probe/ambient left a hard black box on this face
    SPECULAR = 0.5;
    CLEARCOAT = 0.3 * seep;
    CLEARCOAT_ROUGHNESS = mix(0.28, 0.05, seep);
    AO = clamp(matAo * mix(0.55, 1.0, pomShadow) * mix(0.78, 1.0, rim_shadow), 0.0, 1.0);
    AO_LIGHT_AFFECT = 0.30;
    if (repr_skyvis_strength > 0.0) {
        float repr_ao = repr_sky_ao(world_pos, n, false);
        AO *= repr_ao;
        AO_LIGHT_AFFECT = min(AO_LIGHT_AFFECT, REPR_SKYVIS_LIGHT_AFFECT);
        EMISSION = EMISSION * mix(0.65, 1.0, repr_ao) + repr_sky_bounce(ALBEDO, n, repr_ao, false);
    }
    NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);

    // Specular anti-aliasing: horizon detail is high-frequency, so widen the
    // lobe where the normal varies faster than a pixel.
    float n_var = clamp(dot(dFdx(NORMAL), dFdx(NORMAL)) + dot(dFdy(NORMAL), dFdy(NORMAL)), 0.0, 0.25);
    ROUGHNESS = clamp(sqrt(rough * rough + n_var * 0.5), 0.04, 1.0);
}
