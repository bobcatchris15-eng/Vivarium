shader_type spatial;
render_mode cull_disabled, depth_draw_opaque;
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
        tint_depth+=v.y*w*smoothstep(-.006,.001,column_head-bed_height);
    }
    if(depth<=0.0||weight<=.000001)return vec4(bed_height,0.0,0.0,0.0);
    vec2 velocity=momentum/depth;
    // Preserve the hydrostatic solution exactly, including the shoreline, rather
    // than draping a resting pool over its bed.
    ivec2 owner=ivec2(floor(grid+vec2(.5)));
    bool owner_wet=read_state(owner).y>.00000001;
    if(owner_wet&&highest-lowest<=.000001&&max_slope<=.000001&&max_speed<=.0001)depth=max(0.0,head_sum/weight-bed_height);
    return vec4(bed_height+depth,depth,velocity);
}
void vertex(){
    local_uv=UV;float unused_tint;vec4 water=fluid(UV,unused_tint);vec4 bed=ground(UV);
    VERTEX.xz*=tile_size;VERTEX.y=max(water.x,bed.x+.00005);
    world_position=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
}
float scene_depth(vec2 uv,mat4 inverse_projection){vec4 p=inverse_projection*vec4(uv*2.0-1.0,texture(depth_tex,uv).r,1.0);return -p.z/p.w;}
void fragment(){
    float tint_depth;vec4 water=fluid(local_uv,tint_depth);vec4 bed=ground(local_uv);
    float tint_visibility=clamp(tint_depth/max(water.y,.00000001),0.0,1.0);
    // Subdivided support geometry must stay within 2 mm of the reconstructed
    // surface; otherwise a stretched triangle can masquerade as a vertical flap.
    if(abs(world_position.y-water.x)>.002)discard;
    if(water.y<=0.00000001||water.x<=bed.x+.000003||bed.z<.5)discard;
    // The fixed-head reservoir exclusively presents liquid at its own surface.
    if(bed.x<pond_level)discard;
    // Shade the reconstructed surface itself. Averaging the old cell-plane
    // normals makes a terrain-following film reflect like disconnected flat plates.
    vec3 wn=normalize(cross(dFdx(world_position),dFdy(world_position)));
    if(wn.y<0.0)wn=-wn;
    float speed=length(water.zw);float activity=smoothstep(.003,.12,speed);
    vec2 position=tile_origin+local_uv*tile_size;
    // Two overlapping advection intervals keep coordinates bounded. Multiplying
    // a changing velocity/direction by total world age stretches the highlights
    // into stationary contour bands and eventually aliases at wet/dry fronts.
    float phase0=fract(fluid_time/.6),phase1=fract(phase0+.5);
    vec2 q0=position-water.zw*(phase0*.6),q1=position-water.zw*(phase1*.6);
    vec2 r0=vec2(cos(q0.x*95.0+sin(q0.y*65.0)),sin(q0.y*85.0+sin(q0.x*55.0)));
    vec2 r1=vec2(cos(q1.x*95.0+sin(q1.y*65.0)),sin(q1.y*85.0+sin(q1.x*55.0)));
    vec2 ripple=mix(r1,r0,1.0-abs(phase0*2.0-1.0))*.045;
    wn=normalize(wn+vec3(ripple.x,0.0,ripple.y)*activity);
    vec3 n=normalize((VIEW_MATRIX*vec4(wn,0.0)).xyz),view=normalize(-VERTEX);
    NORMAL=n;float cosine=clamp(dot(n,view),0.0,1.0);
    float fresnel=.0204+.9796*pow(1.0-cosine,5.0);
    float gap=max(scene_depth(SCREEN_UV,INV_PROJECTION_MATRIX)+VERTEX.z,0.0);
    float thickness=min(gap,max(.0003,water.x-bed.y)/max(cosine,.2));
    vec2 uv=clamp(SCREEN_UV+n.xy*.0035*smoothstep(.0001,.006,gap),vec2(.002),vec2(.998));
    if(scene_depth(uv,INV_PROJECTION_MATRIX)<-VERTEX.z)uv=SCREEN_UV;
    vec3 transmission=mobile_transmission(textureLod(screen_tex,uv,0.0).rgb,thickness,tint_visibility);
    ALBEDO=mobile_water_tint*.025*tint_visibility;METALLIC=0.0;ROUGHNESS=mobile_roughness;SPECULAR=.5;
    vec3 reflected=reflect(-normalize((INV_VIEW_MATRIX*vec4(view,0.0)).xyz),wn);
    EMISSION=mobile_surface_color(transmission,reflected,fresnel,tint_visibility);
    if(debug_surface){ALBEDO=vec3(0.0);EMISSION=vec3(.0,.5,.9);}
    ALPHA=smoothstep(.000001,.000008,water.x-bed.x);
}
