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
varying vec3 wld_pos;
// Coverage-colony mask from IslandRenderer: procedural moss steps aside where a real moss/lichen colony lives.
uniform sampler2D cover_tex : filter_linear, repeat_disable;
uniform vec4 field_rect = vec4(0.0, 0.0, 1.0, 1.0);

void vertex() { obj_pos = VERTEX; wld_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz; }

float lum(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

void fragment() {
    vec3 vcol = srgb_to_linear(COLOR.rgb);
    float cap = UV2.y;
    vec3 wn = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
    if (cap > 0.5) {
        // end grain: concentric rings with a little wobble, radial checks, darker bark rim
        float r = UV.x;
        float warp = (vnoise(vec2(UV.y * 4.7, r * 7.3)) - 0.5) * (1.2 + r * 1.8);
        float rings = sin((r * 21.0 + warp + vnoise(vec2(r * 13.0, UV.y * 2.1)) * 0.9) * 3.14159);
        vec3 col = vcol * (0.88 + 0.10 * rings);
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
        vec3 mn;
        vec4 moss = sample_untiled(moss_col, moss_nrm, moss_rgh, uv * 2.0 + 5.0, mn);
        // per-clump hue drift (olive to deep green), yellowed light-catching tips, near-black crevices
        float fib = vnoise(uv * vec2(55.0, 14.0));
        vec3 base = mix(vec3(0.34, 0.46, 0.20), vec3(0.50, 0.58, 0.24), cl.w) * mix(0.85, 1.1, n2);
        vec3 tipc = vec3(0.78, 0.82, 0.38);
        vec3 mtint = mix(base, tipc, smoothstep(0.55, 0.98, h) * (0.35 + 0.4 * fib));
        float ao = mix(0.22, 1.0, smoothstep(0.0, 0.6, h)) * (0.85 + 0.15 * cl2.x);
        vec3 mcol = moss.rgb * mtint * ao * (0.8 + 0.35 * fib);
        mcol = mix(vec3(lum(mcol)), mcol, 0.78);
        mcol = mix(mcol, vec3(0.62, 0.55, 0.22) * (0.8 + 0.4 * fib), speck * 0.8);   // sporophyte / strand tips                      // muted, not felt-green
        mcol = mix(mcol * 0.5, mcol, smoothstep(0.0, 0.6, mossy));   // thin edges read as dark damp fuzz
        col = mix(col, mcol, smoothstep(0.08, 0.5, mossy));
        float rough = mix(bark.a, max(moss.a, 0.85), mossy);
        // dome normal per clump (tangent space: x around, y along)
        vec3 dn = normalize(vec3(cl.y, cl.z, 0.0) * 0.9 + vec3(cl2.y, cl2.z, 0.0) * 0.35 + vec3(0.0, 0.0, max(cl.x, 0.25)));
        mn = normalize(vec3(mn.xy * 0.6 + dn.xy, dn.z));
        tn = normalize(mix(tn, mn, mossy));
        ALBEDO = col;
        EMISSION = col * 0.025;   // small ambient floor so shaded log undersides (sparse_dry) do not crush to black
        ROUGHNESS = rough;
        SPECULAR = 0.3;
        // bark mesh has no tangents: build them from the log axis (+X in object space)
        vec3 axis = normalize((MODEL_MATRIX * vec4(1.0, 0.0, 0.0, 0.0)).xyz);
        vec3 around = normalize(cross(axis, wn));   // direction of increasing uv.x (around the trunk)
        vec3 n = normalize(wn * tn.z + around * tn.x - axis * tn.y);
        NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);
    }
}
