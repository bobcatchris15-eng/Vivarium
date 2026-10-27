// Sundew organs use separate draw surfaces: no interpolated pigment/organ flags,
// moss textures, custom light function, wind, or detached micro-droplet geometry.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx;
uniform int tissue = 0; // 0 round basal leaf; 1 scaled stalk; 2 dewy blue club

void fragment() {
    float n2=dot(NORMAL,NORMAL);
    NORMAL=n2>1e-10 && n2<1e6 ? NORMAL*inversesqrt(n2) : vec3(0.,0.,1.);
    vec2 uv=clamp(UV,vec2(0.),vec2(1.));
    SPECULAR=.35;
    if(tissue==0) {
        ALBEDO=vec3(.018,.068,.027)*mix(.88,1.06,smoothstep(.05,.85,uv.y));
        ROUGHNESS=.65;
    } else if(tissue==1) {
        float scale_band=.5+.5*cos(uv.y*37.699+sin(uv.x*25.133)*.65);
        ALBEDO=mix(vec3(.033,.066,.027),vec3(.075,.11,.043),scale_band*.55);
        ROUGHNESS=.72;
    } else {
        ALBEDO=mix(vec3(.012,.105,.38),vec3(.022,.30,.78),smoothstep(.1,.85,uv.y));
        ROUGHNESS=.20;
        SPECULAR=.5;
        CLEARCOAT=.65;
        CLEARCOAT_ROUGHNESS=.18;
    }
}
