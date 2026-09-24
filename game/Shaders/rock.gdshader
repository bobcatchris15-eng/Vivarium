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
    // per-stone hue/value jitter (pebbles share a variant bank; world position breaks residual repetition)
    vec2 cell = inst_origin.xz * 97.0;
    float j = vnoise(cell + 2.1), k = vnoise(cell * 1.7 + 8.7);
    tint *= mix(vec3(1.04, 1.0, 0.94), vec3(0.95, 0.97, 1.0), j) * (0.85 + 0.3 * k);
    vec3 col = tint * scan * 0.9;
    // Weathering lives in broad, broken patches rather than an FBM threshold that traces the same scale all
    // over the stone. Upward faces carry restrained pale biological crust; the buried foot picks up soil staining.
    float up = smoothstep(0.30, 0.88, wn.y);
    float broad = vnoise(obj_pos.xz * 2.35 + obj_pos.y * 0.8 + 3.2);
    float broken = vnoise(obj_pos.zx * 4.91 + obj_pos.y * 1.7 + 11.6);
    float crust = smoothstep(0.58, 0.76, broad * 0.72 + broken * 0.28) * up * lichen;
    vec3 lichenCol = mix(vec3(0.43, 0.46, 0.34), vec3(0.56, 0.52, 0.32), vnoise(obj_pos.xz * 6.7 + 5.0));
    col = mix(col, lichenCol * (0.92 + 0.14 * vnoise(world_pos.xz * 24.0)), crust * 0.58);

    float foot = 1.0 - smoothstep(-0.30, 0.08, obj_pos.y);
    float stainNoise = 0.78 + 0.22 * vnoise(obj_pos.xz * 5.2 + 17.0);
    vec3 soilStain = vec3(0.24, 0.19, 0.14) * stainNoise;
    col = mix(col, soilStain, foot * dust * 0.34);
    ALBEDO = col;
    ROUGHNESS = mix(clamp(t.a, 0.48, 1.0), 0.92, crust);
    SPECULAR = 0.3;   // lower sky reflection: the glossy blue sheen read as cartoony on gravel
    NORMAL = normalize((VIEW_MATRIX * vec4(normalize(mix(n, wn, crust * 0.6)), 0.0)).xyz);
}
