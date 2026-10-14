// Reconstructed three dimensional liquid. Normals and shape come from the simulated fluid.
// No fake wave displacement, turbulent foam, ground-depth field, or wetness texture.
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back, diffuse_burley, specular_schlick_ggx;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
varying float local_thickness;
void vertex(){local_thickness=COLOR.a;}
float scene_depth(vec2 uv,mat4 inverse_projection){vec4 v=inverse_projection*vec4(uv*2.0-1.0,texture(depth_tex,uv).r,1.0);return -v.z/v.w;}
void fragment(){
    vec3 n=normalize(NORMAL),view=normalize(-VERTEX);
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
    vec3 reflection=mix(vec3(.035,.04,.037),vec3(.22,.25,.24),smoothstep(-.1,.8,reflected.y))+vec3(.65)*panel;
    ALBEDO=vec3(.012,.020,.016);
    METALLIC=0.0;ROUGHNESS=.055;SPECULAR=.5;
    EMISSION=mix(transmitted,reflection,fresnel);
    ALPHA=1.0;
}
