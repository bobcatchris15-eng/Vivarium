// Fallen logs: photo-scanned bark (CC0, ambientCG) wrapped around the trunk (UV = metres along, metres around),
// with decay tint and moss from the mesh's vertex colour, and procedural growth rings on the cut/broken ends
// (UV2.y = 1 on the end caps, UV.x = radius fraction there).
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

#include "res://Shaders/surface.gdshaderinc"

uniform sampler2D bark_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D bark_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D bark_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float bark_tile_metres = 0.5;

varying vec3 obj_pos;

void vertex() { obj_pos = VERTEX; }

float lum(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

void fragment() {
    vec3 vcol = srgb_to_linear(COLOR.rgb);
    float cap = UV2.y;
    vec3 wn = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
    if (cap > 0.5) {
        // end grain: concentric rings with a little wobble, radial checks, darker bark rim
        float r = UV.x;
        float rings = sin((r * 26.0 + vnoise(vec2(UV.y * 3.0, r * 8.0)) * 1.5) * 3.14159);
        vec3 col = vcol * (0.82 + 0.16 * rings);
        col *= 1.0 - 0.35 * smoothstep(0.55, 0.9, vnoise(vec2(UV.y * 20.0, r * 3.0))) * step(0.2, r);
        ALBEDO = col;
        ROUGHNESS = 0.8;
        SPECULAR = 0.3;
    } else {
        vec2 uv = vec2(UV.y, UV.x) / bark_tile_metres;   // bark grain runs along the trunk
        // plain lookups (no anti-tiling rotation): the grain must stay aligned with the trunk
        vec4 bark = vec4(texture(bark_col, uv).rgb, texture(bark_rgh, uv).r);
        vec3 tn = unpack_normal(texture(bark_nrm, uv).rgb);
        float avg = max(lum(textureLod(bark_col, vec2(0.5), 10.0).rgb), 0.02);
        // decay tint from the vertex colour (fresh / weathered / rotting), keeping the scan's detail
        float mossy = clamp((vcol.g - vcol.r) * 6.0, 0.0, 1.0);
        vec3 barkTint = mix(vcol, vec3(0.2, 0.14, 0.09), mossy);
        vec3 col = barkTint * (bark.rgb / avg) * 0.95;
        vec3 mn;
        vec4 moss = sample_untiled(moss_col, moss_nrm, moss_rgh, uv * 2.0 + 5.0, mn);
        col = mix(col, moss.rgb, mossy);
        float rough = mix(bark.a, moss.a, mossy);
        tn = normalize(mix(tn, mn, mossy));
        ALBEDO = col;
        ROUGHNESS = rough;
        SPECULAR = 0.3;
        // bark mesh has no tangents: build them from the log axis (+X in object space)
        vec3 axis = normalize((MODEL_MATRIX * vec4(1.0, 0.0, 0.0, 0.0)).xyz);
        vec3 around = normalize(cross(axis, wn));   // direction of increasing uv.x (around the trunk)
        vec3 n = normalize(wn * tn.z + around * tn.x - axis * tn.y);
        NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
    }
}
