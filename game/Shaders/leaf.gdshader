// Dedicated thin/volumetric foliage. CUSTOM0=(seed,variant,age,flex), CUSTOM1=(attachment,length).
shader_type spatial;
render_mode diffuse_burley, cull_disabled;
#include "res://Shaders/surface.gdshaderinc"
uniform sampler2DArray tissue_color : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray tissue_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray tissue_physical : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D vein_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D vein_mask : filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec3 top_pigment = vec3(.045,.14,.045);
uniform vec3 back_pigment = vec3(.14,.20,.09);
uniform float leaf_roughness = .45;
uniform float back_roughness = .84;
uniform float transmission = .28;
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
        float phase = plant.w*6.2831;
        vec3 wind_axis = normalize(vec3(cos(phase),0.,sin(phase)));
        float tip = smoothstep(.02,.95,UV.x);
        float wave = sin(clock*1.3+phase) * .55 + sin(clock*2.7+blade.x*37.+phase)*.45;
        float angle = wave*wind_strength*blade.w*tip;
        VERTEX = CUSTOM1.xyz + turn(VERTEX-CUSTOM1.xyz, wind_axis, angle);
        NORMAL = normalize(turn(NORMAL,wind_axis,angle));
        float h = max(VERTEX.y,0.);
        float amount = (.012 + plant.z*.15)/plant_stiffness;
        float wx = sin(clock*1.3+phase)*amount*3.;
        float wz = cos((clock*1.3+phase)*.8)*amount*2.;
        VERTEX.x += wx*h*h + sin(clock*18.+plant.w*40.)*plant.z*h*.2;
        VERTEX.z += wz*h*h;
        NORMAL = normalize(vec3(NORMAL.x,NORMAL.y-2.*h*(wx*NORMAL.x+wz*NORMAL.z),NORMAL.z));
    }
    position_local = VERTEX;
    position_world = (MODEL_MATRIX*vec4(VERTEX,1.)).xyz;
}
vec3 detail_normal(vec3 base, vec2 uv, vec3 detail) {
    vec3 p = position_world;
    vec3 px=dFdx(p), py=dFdy(p);
    vec2 ux=dFdx(uv), uy=dFdy(uv);
    vec3 r1=cross(py,base),r2=cross(base,px);
    vec3 t=r1*ux.x+r2*uy.x, b=r1*ux.y+r2*uy.y;
    float inv=inversesqrt(max(max(dot(t,t),dot(b,b)),1e-12));
    return normalize(base*detail.z+(t*detail.x-b*detail.y)*inv);
}
void fragment() {
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
    vec3 pigment = mix(top_pigment,back_pigment,side);
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
    vec3 micro = mix(texture(tissue_normal,vec3(tissue_uv,layer)),texture(tissue_normal,vec3(tissue_uv,second)),blend).xyz*2.-1.;
    vec3 veins = texture(vein_normal,UV).xyz*2.-1.;
    // Add slopes, retaining both tissue relief and structural veins.
    vec3 slopes = normalize(vec3(micro.xy*normal_strength + veins.xy*vein_strength,1.));
    vec3 wn = normalize((INV_VIEW_MATRIX*vec4(NORMAL,0.)).xyz);
    // Godot's cull_disabled shader already flips the interpolated normal on back faces.
    vec3 mapped=detail_normal(wn,UV,slopes);
    NORMAL=normalize((VIEW_MATRIX*vec4(mapped,0.)).xyz);
    color*=mix(.8,1.35,fract(seed*5.3+plant.x*2.1))*vec3(1.,1.,mix(1.,.8,fract(seed*5.3)));
    ALBEDO=mix(vec3(dot(color,vec3(.333)))*vec3(1.25,1.05,.55),color,.3+.7*smoothstep(0.,.45,plant.y));
    ROUGHNESS=clamp(mix(leaf_roughness-.1,back_roughness,side)+(physical.r-.5)*.13-anatomy.r*.025,.2,.98);
    float ndv=clamp(abs(dot(wn,normalize(CAMERA_POSITION_WORLD-position_world))),0.,1.);
    ALBEDO+=ALBEDO*.6*pow(1.-ndv,3.)*topf;
    SPECULAR=volumetric ? .5 : .55;
    // Thicker midrib/veins transmit less; never bake the direction of illumination into color.
    BACKLIGHT=color*vec3(1.2,1.3,.6)*transmission*1.3*physical.g*(1.-anatomy.g*.65)*(1.25-.5*physical.b);
    AO=1.-canopy_occlusion*(1.-clamp(position_local.y,0.,1.));
    AO_LIGHT_AFFECT=.2;
    if (UV2.y < .5) { ALBEDO=srgb_to_linear(COLOR.rgb); BACKLIGHT=vec3(0.); }
}
