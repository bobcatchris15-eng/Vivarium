// Reconstructed three dimensional liquid. Normals and shape come from the simulated fluid.
// No fake wave displacement, turbulent foam, ground-depth field, or wetness texture.
shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_back, diffuse_burley, specular_schlick_ggx;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
uniform float interpolation_seconds;
uniform bool debug_surface=false;
uniform sampler2D rings : filter_nearest,repeat_disable;
uniform sampler2D widths : filter_nearest,repeat_disable;
uniform sampler2D velocities : filter_nearest,repeat_disable;
uniform sampler2D normals : filter_nearest,repeat_disable;
varying float local_thickness;
void vertex(){
    int id=int((UV.x<.5?INSTANCE_CUSTOM.r:INSTANCE_CUSTOM.g)+.5);ivec2 key=ivec2(id%128,id/128);
    vec4 p=texelFetch(rings,key,0),w=texelFetch(widths,key,0),n=texelFetch(normals,key,0);
    float angle=UV.y*6.28318530718;
    // A rounded rectangular face section meets neighboring falling faces without
    // the strongly pinched edges of independent elliptical tubes. Its area is
    // 3.7081493546 * width_radius * thickness_radius, used by reconstruction.
    vec2 section=sign(vec2(cos(angle),sin(angle)))*sqrt(abs(vec2(cos(angle),sin(angle))));
    VERTEX=p.xyz+w.xyz*section.x*p.w+n.xyz*section.y*w.w;
    VERTEX+=texelFetch(velocities,key,0).xyz*interpolation_seconds;
    VERTEX.y-=4.905*interpolation_seconds*interpolation_seconds;
    NORMAL=normalize(w.xyz*pow(abs(section.x),3.0)*sign(section.x)/max(p.w,.0000001)+n.xyz*pow(abs(section.y),3.0)*sign(section.y)/max(w.w,.0000001));
    local_thickness=w.w*2.0;
}

float scene_depth(vec2 uv,mat4 inverse_projection){vec4 v=inverse_projection*vec4(uv*2.0-1.0,texture(depth_tex,uv).r,1.0);return -v.z/v.w;}
void fragment(){
    vec3 n=normalize(NORMAL),view=normalize(-VERTEX);
    if(dot(n,view)<0.0)n=-n;
    float cosine=clamp(dot(n,view),0.0,1.0);
    float fresnel=.0204+.9796*pow(1.0-cosine,5.0);
    float gap=max(scene_depth(SCREEN_UV,INV_PROJECTION_MATRIX)+VERTEX.z,0.0);
    float thickness=min(gap,local_thickness/max(cosine,.25));
    vec2 offset=n.xy*.004*smoothstep(.001,.012,gap);
    vec2 uv=clamp(SCREEN_UV+offset,vec2(.002),vec2(.998));
    if(scene_depth(uv,INV_PROJECTION_MATRIX)<-VERTEX.z)uv=SCREEN_UV;
    vec3 transmitted=textureLod(screen_tex,uv,0.0).rgb*exp(-vec3(.8,.22,.45)*thickness);
    vec3 wn=(INV_VIEW_MATRIX*vec4(n,0.0)).xyz;
    vec3 wv=(INV_VIEW_MATRIX*vec4(view,0.0)).xyz;
    vec3 reflected=reflect(-wv,wn);
    float panel=exp(-pow(reflected.z*12.0,2.0))*smoothstep(.45,.85,reflected.y);
    vec3 reflection=mix(vec3(.020,.022,.018),vec3(.070,.075,.065),smoothstep(-.1,.8,reflected.y))+vec3(.025)*panel;
    ALBEDO=vec3(.012,.020,.016);
    METALLIC=0.0;ROUGHNESS=.055;SPECULAR=.5;
    EMISSION=mix(transmitted,reflection,fresnel);
    ALPHA=1.0;
    if(debug_surface){ALBEDO=vec3(0.0);EMISSION=vec3(.9,.05,.0);}
}
