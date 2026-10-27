// Fallen logs: photo-scanned bark (CC0, ambientCG) wrapped around the trunk (UV = metres along, metres around),
// with decay tint and moss from the mesh's vertex colour, and procedural growth rings on the cut/broken ends
// (UV2.y = 1 on the end caps, UV.x = radius fraction there).
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

#include "res://Shaders/surface.gdshaderinc"
#include "res://Shaders/bark_fissure.gdshaderinc"
#include "res://Shaders/water_caustics.gdshaderinc"

uniform sampler2D bark_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D bark_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D bark_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D bark_hgt : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D bark_ao : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_hgt : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_ao : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rotten_wood_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rotten_wood_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rotten_wood_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rotten_wood_hgt : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rotten_wood_ao : filter_linear_mipmap_anisotropic, repeat_enable;
uniform float bark_height_scale = 0.025;
uniform float bark_tile_metres = 0.5;
uniform float water_table = 0.0;

varying vec3 obj_pos;
varying vec3 wld_pos;
// Coverage-colony mask from IslandRenderer: procedural moss steps aside where a real moss/lichen colony lives.
uniform sampler2D sub_tex : filter_linear, repeat_disable;
uniform sampler2D cover_tex : filter_linear, repeat_disable;
uniform vec4 field_rect = vec4(0.0, 0.0, 1.0, 1.0);

varying float material_specular;
void vertex() { obj_pos = VERTEX; wld_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }

float lum(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

// Surface-gradient bump works on caps, bent cavity walls and shards without mesh tangents.
vec3 wood_bump(vec3 n, vec3 p, float height) {
    vec3 dx = dFdx(p), dy = dFdy(p);
    vec3 rx = cross(dy, n), ry = cross(n, dx);
    float determinant = dot(dx, rx);
    vec3 gradient = (rx * dFdx(height) + ry * dFdy(height)) / max(abs(determinant), 1e-10) * sign(determinant);
    return finite_surface_normal(n - gradient, n);
}
float endgrain_height(vec2 uv) {
    float r = uv.x, angle = uv.y;
    float warp = (vnoise(vec2(cos(angle), sin(angle)) * 4.7 + r * 3.7) - 0.5) * 1.7;
    float rings = sin((r * 21.0 + warp) * PI);
    float checks = pow(max(0.0, cos(angle * 13.0 + vnoise(vec2(r * 3.0, angle)) * 0.6)), 36.0);
    float rays = sin(angle * 93.0 + r * 2.0);
    return rings * 0.00065 + rays * 0.00012 - checks * smoothstep(0.2, 0.9, r) * 0.002;
}
void fragment() {
    vec3 vcol = srgb_to_linear(COLOR.rgb);
    float cap = UV2.y;
    vec3 wn = finite_surface_normal((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz, vec3(0.0, 1.0, 0.0));
    if (cap > 0.5) {
        float wood_height = endgrain_height(UV);
        vec3 col = vcol * (0.88 + 0.10 * sin(UV.x * 21.0 * PI));
        float cavity = step(1.5, cap) * (1.0 - step(2.5, cap));
        float splinter = step(2.5, cap);
        if (cavity > 0.5) {
            // Decayed hollow log interior / cavity: 5-channel PBR rotten wood
            vec2 rot_uv = vec2(obj_pos.x, UV.y * 0.25) / bark_tile_metres;
            vec4 rot_samp = texture(rotten_wood_col, rot_uv);
            vec3 rot_n = unpack_normal(texture(rotten_wood_nrm, rot_uv).rgb);
            float rot_r = texture(rotten_wood_rgh, rot_uv).r;
            float rot_h = texture(rotten_wood_hgt, rot_uv).r;
            float rot_ao = texture(rotten_wood_ao, rot_uv).r;

            // Decayed punk wood interior: rich crumbly dark organic humus tones
            col = rot_samp.rgb * vec3(0.55, 0.42, 0.32);
            wood_height = (rot_h - 0.5) * 0.008 + sin(UV.y * 17.0 + obj_pos.x * 2.2) * 0.0007;
            vec3 rot_bump = wood_bump(wn, wld_pos, wood_height);
            vec3 wood_normal = finite_surface_normal(rot_bump + (rot_n.x * cross(vec3(1.0, 0.0, 0.0), wn) + rot_n.y * vec3(1.0, 0.0, 0.0)) * 0.35, wn);
            NORMAL = finite_surface_normal((VIEW_MATRIX * vec4(wood_normal, 0.0)).xyz, NORMAL);
            ALBEDO = col;
            ROUGHNESS = clamp(rot_r * 1.05, 0.75, 0.98);
            SPECULAR = 0.2;
            AO = COLOR.r * rot_ao;
        } else if (splinter > 0.5) {
            // Decayed splintered ends / fractured interior shards
            vec2 rot_uv = vec2(obj_pos.x, UV.y * 0.5) / bark_tile_metres;
            vec4 rot_samp = texture(rotten_wood_col, rot_uv);
            float rot_h = texture(rotten_wood_hgt, rot_uv).r;
            float rot_ao = texture(rotten_wood_ao, rot_uv).r;
            float rot_r = texture(rotten_wood_rgh, rot_uv).r;

            col = mix(col, rot_samp.rgb * vec3(0.68, 0.56, 0.44), 0.65);
            wood_height = (rot_h - 0.5) * 0.004 + sin(UV.y * 90.0 + UV.x * 8.0) * 0.00032;
            vec3 wood_normal = wood_bump(wn, wld_pos, wood_height);
            NORMAL = finite_surface_normal((VIEW_MATRIX * vec4(wood_normal, 0.0)).xyz, NORMAL);
            ALBEDO = col;
            ROUGHNESS = clamp(rot_r, 0.8, 0.96);
            SPECULAR = 0.25;
            AO = COLOR.r * rot_ao;
        } else {
            // Cut / broken end caps: blend rotten wood into decaying ends (decay >= 2)
            if (UV2.x >= 1.5) {
                vec2 rot_uv = vec2(UV.x * cos(UV.y), UV.x * sin(UV.y)) * 1.5;
                vec4 rot_samp = texture(rotten_wood_col, rot_uv);
                float decay_blend = smoothstep(1.5, 3.0, UV2.x) * 0.6;
                col = mix(col, rot_samp.rgb * vec3(0.60, 0.48, 0.36), decay_blend);
            }
            vec3 wood_normal = wood_bump(wn, wld_pos, wood_height);
            NORMAL = finite_surface_normal((VIEW_MATRIX * vec4(wood_normal, 0.0)).xyz, NORMAL);
            ALBEDO = col;
            ROUGHNESS = 0.8;
            SPECULAR = 0.3;
            AO = 1.0;
        }
    } else {
        vec2 uv = vec2(UV.y, UV.x) / bark_tile_metres;   // bark grain runs along the trunk
        vec3 axis_world = finite_surface_normal((MODEL_MATRIX * vec4(1.0, 0.0, 0.0, 0.0)).xyz, vec3(1.0, 0.0, 0.0));
        vec3 around_world = finite_surface_normal(cross(axis_world, wn),
            finite_surface_normal(cross(axis_world, vec3(0.0, 1.0, 0.0)), vec3(0.0, 0.0, 1.0)));
        vec3 along_world = finite_surface_normal(cross(wn, around_world), axis_world);
        vec3 view_world = finite_surface_normal((INV_VIEW_MATRIX * vec4(VIEW, 0.0)).xyz, wn);
        vec3 view_ts = vec3(dot(view_world, around_world), dot(view_world, along_world), dot(view_world, wn));
        float pom_shadow;
        uv = pom_raymarch(bark_hgt, uv, view_ts, bark_height_scale, 16, pom_shadow, vec3(0.0, 0.0, 1.0));
        // plain lookups (no anti-tiling rotation): the grain must stay aligned with the trunk
        vec4 bark = vec4(texture(bark_col, uv).rgb, texture(bark_rgh, uv).r);
        vec3 tn = unpack_normal(texture(bark_nrm, uv).rgb);
        vec3 fissure_color;
        vec3 fissure_normal;
        float fissure_ao;
        bark_fissure(bark.rgb, tn, uv, 1.0, fissure_color, fissure_normal, fissure_ao);
        tn = fissure_normal;
        bark.rgb = fissure_color;
        float avg = max(lum(textureLod(bark_col, vec2(0.5), 10.0).rgb), 0.02);
        // decay tint from the vertex colour (fresh / weathered / rotting), keeping the scan's detail
        float vmoss = clamp((vcol.g - vcol.r) * 6.0, 0.0, 1.0);
        // sheltered damp-forest moss: upper faces, broken up by two noise octaves, creeping down the sides
        float up = wn.y;
        float n1 = vnoise(obj_pos.xz * 3.1 + obj_pos.y * 1.7);
        float n2 = vnoise(obj_pos.xz * 11.0 - obj_pos.y * 5.0);
        float cover = smoothstep(-0.25 + n1 * 0.5, 0.45 + n1 * 0.3, up + (n2 - 0.5) * 0.35);
        cover *= smoothstep(0.15, 0.45, n1 * 0.7 + 0.35);
        // cushion clumps (~2 cm) with a finer sub-clump layer; uv is in half-metres
        vec2 cp = uv * 26.0 + vec2(n1 * 1.3, n2 * 0.9);
        float speck;
        vec4 cl = moss_clumps(cp, speck);
        vec4 cl2 = moss_cells(cp * 2.7 + 3.1, 0.55, 0.35);
        float h = cl.x * (0.75 + 0.25 * cl2.x);
        // ragged edge: coverage breaks at clump granularity instead of a smooth felt boundary
        float colony = texture(cover_tex, (wld_pos.xz - field_rect.xy) / field_rect.zw).r;
        float mossy = clamp(max(vmoss, cover * 0.95) * (1.0 - smoothstep(0.05, 0.5, colony)), 0.0, 1.0);
        mossy = clamp(mossy + (h - 0.55) * 0.7 * (1.0 - mossy * mossy) - (1.0 - h) * 0.25 * mossy * (1.0 - mossy), 0.0, 1.0);
        // damp, darker bark where moss does not reach
        vec3 barkTint = mix(vcol, vec3(0.2, 0.14, 0.09), 0.55 + 0.45 * vmoss) * 0.72;
        vec3 col = barkTint * (bark.rgb / avg) * 0.95;
        if (UV2.x >= 1.5) {
            float rot_mix = smoothstep(1.5, 3.0, UV2.x) * (0.35 + 0.35 * vnoise(uv * 4.0));
            vec4 rot_samp = texture(rotten_wood_col, uv);
            vec3 rot_n = unpack_normal(texture(rotten_wood_nrm, uv).rgb);
            col = mix(col, rot_samp.rgb * barkTint * 0.85, rot_mix);
            tn = finite_surface_normal(mix(tn, rot_n, rot_mix * 0.5), vec3(0.0, 0.0, 1.0));
        }
        vec3 mn;
        vec4 moss = sample_untiled(moss_col, moss_nrm, moss_rgh, uv * 2.0 + 5.0, mn);
        // per-clump hue drift (olive to deep green), yellowed light-catching tips, near-black crevices
        float fib = vnoise(uv * vec2(55.0, 14.0));
        vec3 base = mix(vec3(0.34, 0.46, 0.20), vec3(0.50, 0.58, 0.24), cl.w) * mix(0.85, 1.1, n2);
        vec3 tipc = vec3(0.62, 0.77, 0.29);
        vec3 mtint = mix(base, tipc, smoothstep(0.55, 0.98, h) * (0.35 + 0.4 * fib));
        float ao = mix(0.38, 1.0, smoothstep(0.0, 0.72, h)) * (0.85 + 0.15 * cl2.x);
        vec3 mcol = moss.rgb * mtint * ao * (0.8 + 0.35 * fib);
        mcol = mix(vec3(lum(mcol)), mcol, 0.78);
        mcol = mix(mcol, vec3(0.62, 0.55, 0.22) * (0.8 + 0.4 * fib), speck * 0.22);   // sporophyte / strand tips                      // muted, not felt-green
        mcol = mix(mcol * 0.5, mcol, smoothstep(0.0, 0.6, mossy));   // thin edges read as dark damp fuzz
        col = mix(col, mcol, smoothstep(0.08, 0.5, mossy));
        float r_plate = smoothstep(0.18, 0.88, bark.a);
        float bark_rgh_mapped = mix(0.38, 0.90, r_plate);
        float rough = mix(bark_rgh_mapped, max(moss.a, 0.85), mossy);
        // dome normal per clump (tangent space: x around, y along)
        vec3 dn = finite_surface_normal(vec3(cl.y, cl.z, 0.0) * 0.24 + vec3(cl2.y, cl2.z, 0.0) * 0.10 + vec3(0.0, 0.0, 0.85 + 0.15 * h), vec3(0.0, 0.0, 1.0));
        float moss_relief = texture(moss_hgt, uv * 2.0 + 5.0).r;
        mn = finite_surface_normal(vec3(mn.xy * 0.24 + dn.xy, dn.z + (moss_relief - 0.5) * 0.15), vec3(0.0, 0.0, 1.0));
        tn = finite_surface_normal(mix(tn, mn, mossy), vec3(0.0, 0.0, 1.0));
        ALBEDO = col;
        ROUGHNESS = rough;
        SPECULAR = 0.3;
        // bark mesh has no tangents: build them from the log axis (+X in object space)
        vec3 n = finite_surface_normal(wn * tn.z + around_world * tn.x + along_world * tn.y, wn);
        NORMAL = finite_surface_normal((VIEW_MATRIX * vec4(n, 0.0)).xyz, NORMAL);
        // Soft translucent tuft tips; no emission into the black enclosure void.
        float tips = smoothstep(0.35, 0.9, h) * mossy;
        BACKLIGHT = vec3(0.28, 0.43, 0.08) * tips * 0.35;
        AO = mix(texture(bark_ao, uv).r * fissure_ao * pom_shadow,
            ao * texture(moss_ao, uv * 2.0 + 5.0).r, mossy);
        vec3 dx = dFdx(NORMAL);
        vec3 dy = dFdy(NORMAL);
        float variance = dot(dx, dx) + dot(dy, dy);
        ROUGHNESS = clamp(sqrt(rough * rough + min(variance * 2.0, 0.25)), 0.35, 1.0);
        float log_bark_spec = mix(0.42, 0.14, smoothstep(0.55, 0.90, bark_rgh_mapped));
        SPECULAR = mix(log_bark_spec, 0.16, mossy);
    }
    AO_LIGHT_AFFECT = 0.85;
    material_specular = SPECULAR;
}

void light() {
    vec3 wn = finite_surface_normal((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz, vec3(0.0, 1.0, 0.0));
    vec3 wl = finite_surface_normal((INV_VIEW_MATRIX * vec4(LIGHT, 0.0)).xyz, wn);
    vec3 vv = finite_surface_normal((INV_VIEW_MATRIX * vec4(VIEW, 0.0)).xyz, wn);

    float ndotl = max(dot(wn, wl), 0.0);
    DIFFUSE_LIGHT += LIGHT_COLOR * ATTENUATION * ndotl * (1.0 / PI);

    if (ndotl > 0.0) {
        vec3 h = finite_surface_normal(wl + vv, wn);
        float ndoth = max(dot(wn, h), 0.0);
        float ndotv = max(dot(wn, vv), 1e-4);
        float a = max(ROUGHNESS * ROUGHNESS, 0.001);
        float a2 = a * a;
        float denom = ndoth * ndoth * (a2 - 1.0) + 1.0;
        float D = a2 / max(PI * denom * denom, 1e-4);
        float F0 = material_specular * 0.08;
        float vh = clamp(dot(vv, h), 0.0, 1.0);
        float F = F0 + (1.0 - F0) * pow(1.0 - vh, 5.0);
        float k = (ROUGHNESS + 1.0) * (ROUGHNESS + 1.0) * 0.125;
        float G = (ndotv / max(ndotv * (1.0 - k) + k, 1e-4)) *
                 (ndotl / max(ndotl * (1.0 - k) + k, 1e-4));
        float spec = D * G * F / max(4.0 * ndotv * ndotl, 1e-4);
        SPECULAR_LIGHT += LIGHT_COLOR * ATTENUATION * spec * ndotl;
    }

    vec2 field_uv = (wld_pos.xz - field_rect.xy) / max(field_rect.zw, vec2(0.001));
    float stream_depth = texture(sub_tex, clamp(field_uv, vec2(0.001), vec2(0.999))).b;
    float water_surface_y = water_table + max(0.0, stream_depth * 2.55 - 0.01);
    vec3 caustic = evaluate_submerged_caustics(wld_pos, wn, water_surface_y, wl, TIME);
    DIFFUSE_LIGHT += caustic * ATTENUATION * LIGHT_COLOR;
}
