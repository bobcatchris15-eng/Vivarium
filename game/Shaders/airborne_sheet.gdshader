// Reconstructed three dimensional liquid. Normals and shape come from the simulated fluid.
// No fake wave displacement, turbulent foam, ground-depth field, or wetness texture.
shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_back, diffuse_burley, specular_schlick_ggx;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
uniform float interpolation_seconds;
#include "res://Shaders/mobile_water_optics.gdshaderinc"
uniform float fluid_time;
uniform bool debug_surface=false;
uniform sampler2D rings : filter_nearest,repeat_disable;
uniform sampler2D widths : filter_nearest,repeat_disable;
uniform sampler2D velocities : filter_nearest,repeat_disable;
uniform sampler2D normals : filter_nearest,repeat_disable;
varying float local_thickness;
varying vec3 liquid_position;
varying vec3 liquid_velocity;
void vertex(){
    int id=int((UV.x<.5?INSTANCE_CUSTOM.r:INSTANCE_CUSTOM.g)+.5);ivec2 key=ivec2(id%128,id/128);
    vec4 p=texelFetch(rings,key,0),w=texelFetch(widths,key,0),n=texelFetch(normals,key,0);
    float angle=UV.y*6.28318530718;
    // A rounded rectangular face section meets neighboring falling faces without
    // the strongly pinched edges of independent elliptical tubes. Its area is
    // 3.7081493546 * width_radius * thickness_radius, used by reconstruction.
    vec2 section=sign(vec2(cos(angle),sin(angle)))*pow(abs(vec2(cos(angle),sin(angle))),vec2(n.w));
    VERTEX=p.xyz+w.xyz*section.x*p.w+n.xyz*section.y*w.w;
    VERTEX+=texelFetch(velocities,key,0).xyz*interpolation_seconds;
    VERTEX.y-=4.905*interpolation_seconds*interpolation_seconds;
    float normal_power=2.0/max(n.w,.5)-1.0;
    NORMAL=normalize(w.xyz*pow(abs(section.x),normal_power)*sign(section.x)/max(p.w,.0000001)+n.xyz*pow(abs(section.y),normal_power)*sign(section.y)/max(w.w,.0000001));
    local_thickness=w.w*2.0;
    liquid_position=VERTEX;
    liquid_velocity=texelFetch(velocities,key,0).xyz+vec3(0,-9.81*interpolation_seconds,0);
}

float scene_depth(vec2 uv,mat4 inverse_projection){vec4 v=inverse_projection*vec4(uv*2.0-1.0,texture(depth_tex,uv).r,1.0);return -v.z/v.w;}
void fragment(){
    vec3 n=normalize(NORMAL),view=normalize(-VERTEX);
    // Travelling optical corrugation follows the actual falling velocity. A smooth,
    // steady sheet otherwise copies a static background and looks like a glass panel.
    vec3 flow=normalize((VIEW_MATRIX*vec4(liquid_velocity,0)).xyz);
    vec3 tangent=flow-n*dot(flow,n);tangent/=max(length(tangent),.001);
    float phase0=fract(fluid_time/.6),phase1=fract(phase0+.5);
    vec3 q0=liquid_position-liquid_velocity*(phase0*.6),q1=liquid_position-liquid_velocity*(phase1*.6);
    float ripple=mix(cos(q1.y*110.0+sin(q1.x*90.0)),cos(q0.y*110.0+sin(q0.x*90.0)),1.0-abs(phase0*2.0-1.0));
    n=normalize(n+tangent*ripple*.045*smoothstep(.02,.4,length(liquid_velocity)));
    if(dot(n,view)<0.0)n=-n;
    float cosine=clamp(dot(n,view),0.0,1.0);
    float fresnel=.0204+.9796*pow(1.0-cosine,5.0);
    float gap=max(scene_depth(SCREEN_UV,INV_PROJECTION_MATRIX)+VERTEX.z,0.0);
    float thickness=min(gap,local_thickness/max(cosine,.25));
    vec2 offset=n.xy*.0035*smoothstep(.0001,.006,gap);
    vec2 uv=clamp(SCREEN_UV+offset,vec2(.002),vec2(.998));
    if(scene_depth(uv,INV_PROJECTION_MATRIX)<-VERTEX.z)uv=SCREEN_UV;
    vec3 transmitted=mobile_transmission(textureLod(screen_tex,uv,0.0).rgb,thickness,1.0);
    vec3 wn=(INV_VIEW_MATRIX*vec4(n,0.0)).xyz;
    vec3 wv=(INV_VIEW_MATRIX*vec4(view,0.0)).xyz;
    vec3 reflected=reflect(-wv,wn);
    ALBEDO=mobile_water_tint*.025;
    METALLIC=0.0;ROUGHNESS=mobile_roughness;SPECULAR=.5;
    EMISSION=mobile_surface_color(transmitted,reflected,fresnel,1.0);
    ALPHA=1.0;
    if(debug_surface){ALBEDO=vec3(0.0);EMISSION=vec3(.9,.05,.0);}
}
