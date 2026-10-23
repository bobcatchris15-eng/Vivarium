shader_type spatial;
render_mode blend_mix, depth_draw_always, cull_disabled, diffuse_burley, specular_schlick_ggx;
uniform sampler2D previous_state : filter_nearest, repeat_disable;
uniform sampler2D current_state : filter_nearest, repeat_disable;
uniform sampler2D previous_support : filter_nearest, repeat_disable;
uniform sampler2D current_support : filter_nearest, repeat_disable;
uniform sampler2D solid : filter_linear, repeat_disable;
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
uniform vec2 tile_origin;
uniform float tile_size;
uniform float pond_level;
uniform float state_blend=1.0;
uniform float fluid_time;
// Pin only optical advection for matched captures; conserved state stays intact.
uniform float review_time=-1.0;
uniform bool debug_surface=false;
#include "res://Shaders/mobile_water_optics.gdshaderinc"
varying vec2 local_uv;
varying vec3 world_position;
vec4 ground(vec2 uv){
    int n=textureSize(solid,0).x;
    vec2 p=(uv*16.0+vec2(1.0))*float(n-1)/18.0;
    ivec2 i=clamp(ivec2(floor(p)),ivec2(0),ivec2(n-2));vec2 f=clamp(p-vec2(i),vec2(0.0),vec2(1.0));
    vec4 a=texelFetch(solid,i,0),b=texelFetch(solid,i+ivec2(1,0),0);
    vec4 c=texelFetch(solid,i+ivec2(0,1),0),d=texelFetch(solid,i+ivec2(1,1),0);
    // Use the visible mesh's diagonal. Bilinear height filtering creates a
    // different bed inside every quad and cuts triangular holes through films.
    return f.x>=f.y?a+f.x*(b-a)+f.y*(d-b):a+f.y*(c-a)+f.x*(d-c);
}
vec4 read_state(ivec2 p){
    vec4 a=texelFetch(previous_state,p,0),b=texelFetch(current_state,p,0);
    if(a.y<=0.0)a.x=b.x;if(b.y<=0.0)b.x=a.x;
    return mix(a,b,state_blend);
}
float column_kernel(float distance){
    float a=abs(distance);return a<.5?.75-a*a:(a<1.5?.5*(1.5-a)*(1.5-a):0.0);
}
vec4 fluid(vec2 uv,out float tint_depth){
    tint_depth=0.0;
    vec2 grid=uv*16.0+vec2(.5);ivec2 base=ivec2(floor(grid+vec2(.5)));
    float bed_height=ground(uv).x,depth=0.0,head_sum=0.0,weight=0.0;
    float lowest=1e20,highest=-1e20,max_slope=0.0,max_speed=0.0;vec2 momentum=vec2(0.0);
    for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++){
        ivec2 key=base+ivec2(x,z);
        float w=column_kernel(grid.x-float(key.x))*column_kernel(grid.y-float(key.y));
        if(w<=0.0||any(lessThan(key,ivec2(0)))||any(greaterThanEqual(key,textureSize(current_state,0))))continue;
        vec4 v=read_state(key);
        if(v.y<=.00000001)continue;
        vec2 sample_uv=(vec2(key)-vec2(.5))/16.0;
        float support=ground(sample_uv).x;
        // Never spread a high lip's supported volume onto the floor below a drop.
        if(support-bed_height>tile_size/16.0*1.2&&v.x-bed_height>tile_size/16.0*1.2){
            float quarter_bed=ground(mix(sample_uv,uv,.25)).x;
            if(support+4.0*(quarter_bed-support)-bed_height>tile_size/16.0*1.2)continue;
        }
        // A quadratic B-spline kernel on finite-volume column depths is nonnegative and its
        // integral is the stored volume. Extrapolated planes create paper wedges.
        depth+=v.y*w;momentum+=v.zw*v.y*w;head_sum+=v.x*w;weight+=w;
        lowest=min(lowest,v.x);highest=max(highest,v.x);
        max_speed=max(max_speed,length(v.zw));
        vec2 slope=mix(texelFetch(previous_support,key,0).xy,texelFetch(current_support,key,0).xy,state_blend);
        max_slope=max(max_slope,length(slope));
        // Color follows local liquid contact, rather than painting the dry uphill
        // side of a deep pool when the continuous depth kernel crosses a ledge.
        float column_head=v.x+dot(slope,(grid-vec2(key))*tile_size/16.0);
        tint_depth+=v.y*w*smoothstep(-.0002,.0004,column_head-bed_height);
    }
    if(depth<=0.0||weight<=.000001)return vec4(bed_height,0.0,0.0,0.0);
    vec2 velocity=momentum/depth;
    // Preserve the hydrostatic solution exactly, including the shoreline, rather
    // than draping a resting pool over its bed.
    ivec2 owner=ivec2(floor(grid+vec2(.5)));
    bool owner_wet=read_state(owner).y>.00000001;
    if(owner_wet&&highest-lowest<=.000001&&max_slope<=.000001&&max_speed<=.0001)depth=max(0.0,head_sum/weight-bed_height);
    float head=bed_height+depth;
    // Continue the final film a short distance over the receiver, settling exactly
    // onto its fixed plane instead of cutting it off at the terrain contour.
    if(bed_height<pond_level){
        float settle=smoothstep(0.0,.012,pond_level-bed_height);
        head=mix(max(pond_level,head),pond_level,settle);
        depth=max(0.0,head-bed_height);
        velocity*=1.0-settle;
    }
    return vec4(head,depth,velocity);
}
void vertex(){
    local_uv=UV;float unused_tint;vec4 water=fluid(UV,unused_tint);vec4 bed=ground(UV);
    VERTEX.xz*=tile_size;VERTEX.y=max(water.x,bed.x+.00005);
    world_position=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
}
float scene_depth(vec2 uv,mat4 inverse_projection){vec4 p=inverse_projection*vec4(uv*2.0-1.0,texture(depth_tex,uv).r,1.0);return -p.z/p.w;}
vec3 refracted_screen(vec2 uv,vec2 bend_dir,float path,float roughness){
    float mip=water_refraction_mip(path,roughness);
    float spread=water_dispersion_amount(path,roughness);
    vec2 lo=clamp(uv-bend_dir*spread,vec2(.002),vec2(.998));
    vec2 hi=clamp(uv+bend_dir*spread,vec2(.002),vec2(.998));
    return vec3(textureLod(screen_tex,lo,mip).r,
        textureLod(screen_tex,uv,mip).g,
        textureLod(screen_tex,hi,mip).b);
}
void fragment(){
    float tint_depth;vec4 water=fluid(local_uv,tint_depth);vec4 bed=ground(local_uv);
    float tint_visibility=bed.x<pond_level?1.0:clamp(tint_depth/max(water.y,.00000001),0.0,1.0);
    if(tint_visibility<.015)discard;
    // Subdivided support geometry must stay within 2 mm of the reconstructed
    // surface; otherwise a stretched triangle can masquerade as a vertical flap.
    if(abs(world_position.y-water.x)>.002)discard;
    if(water.y<=0.00000001||water.x<=bed.x+.000003||bed.z<.5)discard;
    // The fixed-head reservoir exclusively presents liquid at its own surface.
    if(bed.x<pond_level&&water.x<=pond_level+.000015)discard;
    float clock=review_time>=0.0?review_time:fluid_time;
    // Analytic central-difference gradient of the quadratic B-spline surface, taken
    // at the mesh's own sample spacing (one 16-cell tile is 96 samples wide). Screen
    // derivatives of the displaced position gave every triangle one flat normal;
    // this gradient is C1 across every triangle and tile boundary, so the surface is
    // completely facet-free.
    float eps=1.0/(16.0*6.0);
    float td_xp,td_xm,td_zp,td_zm;
    float h_xp=fluid(local_uv+vec2(eps,0.0),td_xp).x;
    float h_xm=fluid(local_uv-vec2(eps,0.0),td_xm).x;
    float h_zp=fluid(local_uv+vec2(0.0,eps),td_zp).x;
    float h_zm=fluid(local_uv-vec2(0.0,eps),td_zm).x;
    float span=2.0*eps*tile_size;
    // Guard the wet/dry front, where the reconstruction legitimately steps onto the
    // bed; beyond 80 degrees the slope is no longer a water surface.
    vec2 gradient=clamp(vec2(h_xm-h_xp,h_zm-h_zp)/span,vec2(-6.0),vec2(6.0));
    // Dispersion-correct gravity-capillary cascade, forced by the simulated flow and
    // damped towards the shoreline where the conserved column thins out.
    float speed=length(water.zw);
    vec2 flow_dir=speed>1e-4?water.zw/speed:water_wind_direction;
    float wave_wind=water_wind_speed+speed*2.2;
    float slope_variance=0.0;
    vec2 wave=water_wave_slope(world_position.xz,clock,water.y,wave_wind,flow_dir,slope_variance);
    vec3 wn=normalize(vec3(gradient.x-wave.x,1.0,gradient.y-wave.y));
    if(wn.y<0.0)wn=-wn;
    vec3 n=normalize((VIEW_MATRIX*vec4(wn,0.0)).xyz),view=normalize(VIEW);
    NORMAL=n;float cosine=clamp(dot(n,view),0.0,1.0);
    float fresnel=water_fresnel(cosine);
    float roughness=clamp(0.018+slope_variance*0.25,0.018,0.14);
    float gap=max(scene_depth(SCREEN_UV,INV_PROJECTION_MATRIX)+VERTEX.z,0.0);
    // True optical path along the visible ray. The old 15 cm clamp made an entire pond
    // read as one opaque olive slab; only a pixel with no bed behind it falls back to
    // the conserved column depth.
    float path=gap;
    if(!(gap>0.0&&gap<20.0))path=max(water.x-bed.y,0.0003)/max(cosine,0.2);
    vec3 flat_view=normalize((VIEW_MATRIX*vec4(0.0,1.0,0.0,0.0)).xyz);
    vec2 uv=clamp(SCREEN_UV+clamp((n.xy-flat_view.xy)*.022,vec2(-.006),vec2(.006))*smoothstep(.0002,.008,gap),vec2(.002),vec2(.998));
    if(scene_depth(uv,INV_PROJECTION_MATRIX)<-VERTEX.z)uv=SCREEN_UV;
    vec2 bend=uv-SCREEN_UV;
    vec2 bend_dir=length(bend)>1e-6?normalize(bend):vec2(0.0);
    vec3 behind=refracted_screen(uv,bend_dir,path,roughness);
    vec3 transmission=mobile_transmission(behind,path,tint_visibility);
    vec3 reflected=reflect(-normalize(CAMERA_POSITION_WORLD-world_position),wn);
    // (1 - F) is the energy left for the refracted body; the pipeline's GGX lobe
    // supplies the complementary F-weighted reflection, so nothing is counted twice.
    vec3 emission=transmission*(1.0-fresnel);
    if(water_debug==1)emission=wn*.5+.5;
    if(water_debug==2)emission=transmission;
    if(water_debug==3)emission=mobile_reflection(reflected);
    if(water_debug==4)emission=vec3(fresnel);
    if(debug_surface)emission=vec3(.0,.5,.9);
    // ALBEDO is the near-black volumetric albedo of the body, not a tint: the
    // refracted bed is radiance, so it lives in EMISSION where the surface lighting
    // cannot modulate it a second time. SPECULAR 0.255 is F0 = 0.0204 for n = 1.333.
    ALBEDO=(water_debug>0||debug_surface)?vec3(0.0):vec3(0.012,0.024,0.018);
    METALLIC=0.0;
    SPECULAR=(water_debug>0||debug_surface)?0.0:WATER_SPECULAR;
    ROUGHNESS=(water_debug>0||debug_surface)?1.0:roughness;
    EMISSION=emission;
    ALPHA=smoothstep(.000001,.000008,water.x-bed.x)*smoothstep(.015,.25,tint_visibility);
}
