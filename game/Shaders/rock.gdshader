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
uniform float dust = 0.4;   // pale dust settling in low spots / interstices between gravel pebbles

varying vec3 world_pos;
varying vec3 obj_pos;
varying vec3 inst_origin;   // per-instance origin (MultiMesh transform), for per-stone variation

void vertex() {
    world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    obj_pos = VERTEX;
    inst_origin = (MODEL_MATRIX * vec4(0.0, 0.0, 0.0, 1.0)).xyz;
}

float lum(vec3 c) { return dot(c, vec3(0.2126, 0.7152, 0.0722)); }

void fragment() {
    vec3 wn = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
    vec3 n;
    vec4 t = triplanar(rock_col, rock_nrm, rock_rgh, world_pos / tile_metres, wn, 4.0, n);
    float avg = max(lum(textureLod(rock_col, vec2(0.5), 10.0).rgb), 0.02);
    vec3 tint = srgb_to_linear(COLOR.rgb);
    // the scan carries a cool blue cast: keep its luminance detail, drop most of its hue so the variant tint rules
    vec3 scan = mix(vec3(lum(t.rgb)), t.rgb, 0.3) / avg;
    // per-stone hue/value jitter (pebbles share 6 meshes; world position breaks the repetition)
    vec2 cell = inst_origin.xz * 97.0;
    float j = vnoise(cell + 2.1), k = vnoise(cell * 1.7 + 8.7);
    tint *= mix(vec3(1.04, 1.0, 0.94), vec3(0.95, 0.97, 1.0), j) * (0.85 + 0.3 * k);
    vec3 col = tint * scan * 0.9;
    // lichen: pale grey-green and ochre crusts on the tops, a few rust-orange spots
    float up = smoothstep(0.25, 0.85, wn.y);
    float patchN = fbm(obj_pos.xz * 7.0 + obj_pos.y * 3.0);
    float crust = smoothstep(0.52, 0.62, patchN) * up * lichen;
    vec3 lichenCol = mix(vec3(0.42, 0.47, 0.33), vec3(0.62, 0.55, 0.30), vnoise(obj_pos.xz * 18.0));
    col = mix(col, lichenCol * (0.8 + 0.4 * vnoise(world_pos.xz * 90.0)), crust * 0.85);
    col = mix(col, vec3(0.55, 0.22, 0.05), smoothstep(0.8, 0.84, vnoise(obj_pos.xz * 25.0 + 4.0)) * up * lichen * 0.8);
    // fine pale dust gathers in low spots and the interstices between neighbouring pebbles
    float low = 1.0 - smoothstep(-0.2, 0.35, wn.y);
    vec3 dustCol = vec3(0.62, 0.58, 0.50);
    col = mix(col, dustCol * (0.75 + 0.3 * vnoise(obj_pos.xz * 30.0 + 11.0)), low * dust * 0.5);
    ALBEDO = col;
    ROUGHNESS = mix(clamp(t.a, 0.45, 1.0), 0.95, crust);
    SPECULAR = 0.3;   // lower sky reflection: the glossy blue sheen read as cartoony on gravel
    NORMAL = normalize((VIEW_MATRIX * vec4(normalize(mix(n, wn, crust * 0.6)), 0.0)).xyz);
}
