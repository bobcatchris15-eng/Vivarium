// Rocks and gravel pebbles: photo-scanned rock (CC0, ambientCG) projected in world space, tinted per variant by
// the mesh's vertex colour (granite, slate, sandstone...). Lichen crusts settle on the upward-facing surfaces.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;

#include "res://Shaders/surface.gdshaderinc"

uniform sampler2D rock_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D rock_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform float tile_metres = 0.9;
uniform float lichen = 1.0;

varying vec3 world_pos;
varying vec3 obj_pos;

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    obj_pos = VERTEX;
}

float lum(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

void fragment() {
    vec3 wn = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
    vec3 n;
    vec4 t = triplanar(rock_col, rock_nrm, rock_rgh, world_pos / tile_metres, wn, 4.0, n);
    float avg = max(lum(textureLod(rock_col, vec2(0.5), 10.0).rgb), 0.02);
    vec3 tint = srgb_to_linear(COLOR.rgb);
    vec3 col = tint * (t.rgb / avg) * 0.9;
    // lichen: pale grey-green and ochre crusts on the tops, a few rust-orange spots
    float up = smoothstep(0.25, 0.85, wn.y);
    float patchN = fbm(obj_pos.xz * 7.0 + obj_pos.y * 3.0);
    float crust = smoothstep(0.52, 0.62, patchN) * up * lichen;
    vec3 lichenCol = mix(vec3(0.42, 0.47, 0.33), vec3(0.62, 0.55, 0.30), vnoise(obj_pos.xz * 18.0));
    col = mix(col, lichenCol * (0.8 + 0.4 * vnoise(world_pos.xz * 90.0)), crust * 0.85);
    col = mix(col, vec3(0.55, 0.22, 0.05), smoothstep(0.8, 0.84, vnoise(obj_pos.xz * 25.0 + 4.0)) * up * lichen * 0.8);
    ALBEDO = col;
    ROUGHNESS = mix(clamp(t.a, 0.45, 1.0), 0.95, crust);
    SPECULAR = 0.4;
    NORMAL = normalize((VIEW_MATRIX * vec4(normalize(mix(n, wn, crust * 0.6)), 0.0)).xyz);
}
