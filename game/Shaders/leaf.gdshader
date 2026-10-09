// Dedicated thin/volumetric foliage. CUSTOM0=(seed,variant,age,flex), CUSTOM1=(attachment,length).
shader_type spatial;
render_mode diffuse_burley, cull_disabled;
#include "res://Shaders/surface.gdshaderinc"
uniform sampler2DArray tissue_color : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray tissue_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray tissue_physical : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D vein_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D vein_mask : filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec3 leaf_top = vec3(.045,.14,.045);
uniform vec3 leaf_back = vec3(.14,.20,.09);
uniform vec3 top_pigment = vec3(-1.0);
uniform vec3 back_pigment = vec3(-1.0);
uniform float leaf_roughness = .45;
uniform float back_roughness = .84;
uniform float leaf_transmission = .28;
uniform float transmission = -1.0;
uniform float cuticle_sheen = .35;
uniform float normal_strength = .45;
uniform float vein_strength = .4;
uniform float wind_strength = .018;
uniform float plant_stiffness = 7.;
uniform float tissue_scale = 1.6;
uniform float canopy_occlusion = .15;
uniform bool volumetric = false;
uniform int leaf_variegation = 0;
uniform float review_time = -1.;
varying vec4 plant;
varying vec4 blade;
varying vec3 position_world;
varying vec3 position_local;

vec3 turn(vec3 p, vec3 axis, float angle) {
    return p*cos(angle) + cross(axis,p)*sin(angle) + axis*dot(axis,p)*(1.-cos(angle));
}
void vertex() {
    plant = INSTANCE_CUSTOM; blade = CUSTOM0;
    float clock = review_time >= 0. ? review_time : TIME;
    if (wind_strength > 0.) {
        // Hierarchical wind: branch cantilever sway with bounded displacement preserving shadow bounds
        float phase_branch = clock * 1.25 + plant.w * 6.2831;
        float h = max(VERTEX.y, 0.0);
        float stem_flex = (h * h) / (1.0 + 0.35 * h);
        float stem_amount = clamp((.012 + plant.z * .15) / max(plant_stiffness, 0.5), 0.0, 0.08);
        vec2 branch_disp = clamp(vec2(sin(phase_branch) * 2.2, cos(phase_branch * 0.85) * 1.6) * stem_amount * stem_flex, vec2(-0.35), vec2(0.35));

        // Attachment anchor CUSTOM1.xyz moves with branch sway
        vec3 anchor = CUSTOM1.xyz;
        float ah = max(anchor.y, 0.0);
        float a_flex = (ah * ah) / (1.0 + 0.35 * ah);
        vec2 a_disp = clamp(vec2(sin(phase_branch) * 2.2, cos(phase_branch * 0.85) * 1.6) * stem_amount * a_flex, vec2(-0.35), vec2(0.35));
        vec3 animated_anchor = anchor + vec3(a_disp.x, 0.0, a_disp.y);

        // Leaf blade flutter rotating around its moving attachment anchor
        vec3 leaf_local = VERTEX - anchor;
        float tip = smoothstep(0.02, 0.95, UV.x);
        float leaf_phase = plant.w * 6.2831 + blade.x * 17.3;
        vec3 wind_axis = normalize(vec3(cos(leaf_phase), 0.0, sin(leaf_phase)));
        float leaf_wave = sin(clock * 2.6 + leaf_phase) * 0.6 + sin(clock * 5.2 + blade.x * 37.0) * 0.4;
        float leaf_angle = leaf_wave * wind_strength * blade.w * tip;
        leaf_local = turn(leaf_local, wind_axis, leaf_angle);
        VERTEX = animated_anchor + leaf_local;
        NORMAL = normalize(turn(NORMAL, wind_axis, leaf_angle));

        // Rotate normal with branch deflection
        float d_stem = stem_amount * 2.0 * h / (1.0 + 0.35 * h);
        vec2 dw = vec2(sin(phase_branch) * 2.2, cos(phase_branch * 0.85) * 1.6) * d_stem;
        NORMAL = normalize(vec3(NORMAL.x - dw.x * NORMAL.y, NORMAL.y + dw.x * NORMAL.x + dw.y * NORMAL.z, NORMAL.z - dw.y * NORMAL.y));
    }
    position_local = VERTEX;
    position_world = (MODEL_MATRIX*vec4(VERTEX,1.)).xyz;

}
vec3 detail_normal(vec3 base, vec2 uv, vec3 detail) {
    return cotangent_normal(base, position_world, uv, detail);
}
void fragment() {
    // Invert backface normal so undersides do not render dark or inverted.
    if (!FRONT_FACING) {
        NORMAL = -NORMAL;
    }
    bool back = !FRONT_FACING && !volumetric;
    float side = back ? 1. : 0.;
    float seed = fract(blade.x+plant.w*.618);
    float layer = mod(blade.y+floor(plant.w*4.),4.);
    vec2 tissue_uv = UV*vec2(tissue_scale,tissue_scale*.7)+vec2(seed*3.13,seed*7.31);
    float blend = .12 + .18*(.5+.5*sin(UV.x*3.14159+seed*6.28));
    float second = mod(layer+1.,4.);
    vec3 photograph = mix(texture(tissue_color,vec3(tissue_uv,layer)).rgb,
        texture(tissue_color,vec3(tissue_uv,second)).rgb,blend);
    // Masters are normalized offline: luminance carries pigment variation, not height.
    photograph *= 2.; // Offline masters use a linear .5 neutral reference.
    vec3 actual_top = top_pigment.r >= 0.0 ? top_pigment : leaf_top;
    vec3 actual_back = back_pigment.r >= 0.0 ? back_pigment : leaf_back;
    float actual_trans = transmission >= 0.0 ? transmission : leaf_transmission;
    vec3 pigment = mix(actual_top, actual_back, side);
    vec3 color = pigment*mix(vec3(1.),photograph,.72);
    vec4 anatomy = texture(vein_mask,UV);
    color *= 1.+(blade.z-.5)*.08+(plant.x-.5)*.05;
    color = mix(color,color*vec3(1.15,1.18,1.03),anatomy.r*vein_strength);
    float topf = 1. - side;
    // Pale midrib/veins and young-leaf freshness; lift very dark tissue so it reads as leaf.
    color = mix(color, color*vec3(1.5,1.55,1.15)+vec3(.008,.012,.005), anatomy.r*topf*.55);
    color *= mix(1.3, 1., smoothstep(.015,.07,dot(color,vec3(.2126,.7152,.0722))));
    if (leaf_variegation == 1) color = mix(color, vec3(dot(color,vec3(.33)))*vec3(2.5,2.8,2.2)+.03, anatomy.r*topf*.6);
    else if (leaf_variegation == 2) color = mix(color, vec3(.30,.34,.32), smoothstep(.45,.65,texture(tissue_physical,vec3(tissue_uv*.5,layer)).b)*(1.-anatomy.r)*topf*.55);
    else if (leaf_variegation == 3) color = mix(color, vec3(.20,.025,.07)*(.8+.4*photograph.g), side*.85);
    if (volumetric) color = mix(color,srgb_to_linear(COLOR.rgb),smoothstep(.64,.99,UV.x)*.72);
    vec4 physical = mix(texture(tissue_physical,vec3(tissue_uv,layer)),texture(tissue_physical,vec3(tissue_uv,second)),blend);
    vec3 micro = unpack_normal(mix(texture(tissue_normal,vec3(tissue_uv,layer)).rgb,texture(tissue_normal,vec3(tissue_uv,second)).rgb,blend));
    vec3 veins = unpack_normal(texture(vein_normal,UV).rgb);
    // Add slopes, retaining both tissue relief and structural veins.
    vec3 slopes = normalize(vec3(micro.xy*normal_strength + veins.xy*vein_strength,1.));
    vec3 wn = normalize((INV_VIEW_MATRIX*vec4(NORMAL,0.)).xyz);
    vec3 mapped=detail_normal(wn,UV,slopes);
    NORMAL=normalize((VIEW_MATRIX*vec4(mapped,0.)).xyz);
    color*=mix(.8,1.35,fract(seed*5.3+plant.x*2.1))*vec3(1.,1.,mix(1.,.8,fract(seed*5.3)));
    ALBEDO=mix(vec3(dot(color,vec3(.333)))*vec3(1.25,1.05,.55),color,.3+.7*smoothstep(0.,.45,plant.y));
    // Species-specific cuticular gloss on adaxial face; rougher, paler abaxial face
    float wax = cuticle_sheen * topf;
    ROUGHNESS=clamp(mix(leaf_roughness - wax * 0.16, back_roughness, side) + (physical.r - 0.5) * 0.12 - anatomy.r * 0.02, 0.20, 0.96);
    SPECULAR=volumetric ? .45 : mix(0.35, mix(0.50, 0.68, cuticle_sheen), topf);
    // Physically bounded transmission through chlorophyll, attenuated by vein thickness
    vec3 transmit_filter = vec3(0.92, 1.30, 0.50);
    float vein_thickness = clamp(anatomy.g * 0.75 + anatomy.r * 0.25, 0.0, 1.0);
    float trans_amount = clamp(actual_trans, 0.0, 0.5);
    BACKLIGHT=color * transmit_filter * trans_amount * physical.g * (1.0 - vein_thickness * 0.65) * (1.2 - 0.4 * physical.b);
    AO=1.-canopy_occlusion*(1.-clamp(position_local.y,0.,1.));
    AO_LIGHT_AFFECT=.2;
    if (UV2.y < .5) { ALBEDO=srgb_to_linear(COLOR.rgb); BACKLIGHT=vec3(0.); }
}
