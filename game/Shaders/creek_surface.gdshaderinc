render_mode cull_disabled, depth_draw_opaque, unshaded, fog_disabled;
uniform sampler2D previous_state : filter_nearest, repeat_disable;
uniform sampler2D current_state : filter_nearest, repeat_disable;
// State RGBA: effective bed (head - volume/area), mean depth, signed velocity X/Z.
uniform sampler2D u_ripple_normal : repeat_enable, filter_linear_mipmap;
uniform sampler2D solid : filter_linear, repeat_disable;
#ifdef CREEK_CHEAP_REFRACTION
uniform sampler2D u_substrate_albedo : source_color, repeat_enable, filter_linear_mipmap;
#else
uniform sampler2D screen_tex : hint_screen_texture, filter_linear_mipmap, repeat_disable;
uniform sampler2D depth_tex : hint_depth_texture, filter_nearest, repeat_disable;
#endif
uniform vec2 tile_origin;
uniform float tile_size;
uniform float pond_level;
uniform float state_blend=1.0;
uniform float fluid_time;
// Pin only optical advection for matched captures; conserved state stays intact.
uniform float review_time=-1.0;
uniform bool debug_surface=false;
// Clear creek optics: no procedural environment reflection.
uniform float sky_energy = 1.0;
uniform vec3 light_direction = vec3(.4,.8,-.3);
uniform int water_debug = 0;
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
    vec4 state=mix(a,b,state_blend);
    state.x+=state.y; // reconstruct hydraulic head in metres
    return state;
}
float column_kernel(float distance){
    float a=abs(distance);return a<.5?.75-a*a:(a<1.5?.5*(1.5-a)*(1.5-a):0.0);
}
float kernel_derivative(float distance){
    float a=abs(distance);
    return a<.5?-2.0*distance:(a<1.5?-sign(distance)*(1.5-a):0.0);
}
vec4 fluid(vec2 uv,out float tint_depth,out vec2 head_slope){
    tint_depth=0.0;head_slope=vec2(0.0);
    vec2 head_gradient=vec2(0.0),weight_gradient=vec2(0.0);
    vec2 grid=uv*16.0+vec2(.5);ivec2 base=ivec2(floor(grid+vec2(.5)));
    float bed_height=ground(uv).x,depth=0.0,head_sum=0.0,weight=0.0;
    float lowest=1e20,highest=-1e20,max_speed=0.0;vec2 momentum=vec2(0.0);
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
        vec2 offset=grid-vec2(key);
        vec2 dw=vec2(kernel_derivative(offset.x)*column_kernel(offset.y),column_kernel(offset.x)*kernel_derivative(offset.y));
        head_gradient+=v.x*dw;weight_gradient+=dw;
        lowest=min(lowest,v.x);highest=max(highest,v.x);
        max_speed=max(max_speed,length(v.zw));
        tint_depth+=v.y*w*smoothstep(-.0002,.0004,v.x-bed_height);

    }
    if(depth<=0.0||weight<=.000001)return vec4(bed_height,0.0,0.0,0.0);
    head_slope=(head_gradient*weight-head_sum*weight_gradient)/(weight*weight*tile_size/16.0);
    vec2 velocity=momentum/depth;
    // Preserve the hydrostatic solution exactly, including the shoreline, rather
    // than draping a resting pool over its bed.
    ivec2 owner=ivec2(floor(grid+vec2(.5)));
    bool owner_wet=read_state(owner).y>.00000001;
    if(owner_wet&&depth>.004&&highest-lowest<=.000001&&max_speed<=.0001)depth=max(0.0,head_sum/weight-bed_height);
    // Thick connected water interpolates head, so a basin stays level over its bed.
    // The blend uses continuous kernel depth, never nearest-cell ownership.
    // Thin films retain the positive volume kernel; no isolated parcel becomes a plate.
    float pool_blend=smoothstep(.004,.012,depth);
    depth=mix(depth,max(0.0,head_sum/weight-bed_height),pool_blend);
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
    local_uv=UV;float unused_tint;vec2 unused_slope;vec4 water=fluid(UV,unused_tint,unused_slope);vec4 bed=ground(UV);
    VERTEX.xz*=tile_size;VERTEX.y=max(water.x,bed.x+.0008);
    world_position=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
}
#ifndef CREEK_CHEAP_REFRACTION
float scene_depth(vec2 uv,mat4 inverse_projection){vec4 p=inverse_projection*vec4(uv*2.0-1.0,texture(depth_tex,uv).r,1.0);return -p.z/p.w;}
#endif
void fragment(){
    float tint_depth;vec2 head_slope;vec4 water=fluid(local_uv,tint_depth,head_slope);vec4 bed=ground(local_uv);
    float tint_visibility=bed.x<pond_level?1.0:clamp(tint_depth/max(water.y,.00000001),0.0,1.0);
    if(water.y<=.0000001)discard;
    tint_visibility=max(tint_visibility,.35);
    // Subdivided support geometry must stay within 2 mm of the reconstructed
    // surface; otherwise a stretched triangle can masquerade as a vertical flap.
    if(abs(world_position.y-max(water.x,bed.x+.0008))>.003)discard;
    if(water.y<=0.00000001||water.x<=bed.x+.000003||bed.z<.5)discard;
    // The fixed-head reservoir exclusively presents liquid at its own surface.
    if(bed.x<pond_level&&water.x<=pond_level+.000015)discard;
    // Shade the reconstructed surface itself. Averaging the old cell-plane
    // normals makes a terrain-following film reflect like disconnected flat plates.
    // Analytic gradient of the same normalized head kernel: continuous across
    // cell/tile boundaries, flat for equal heads, with no extra texture fetches.
    vec2 step_uv=vec2(1.0/16.0,0.0);
    vec2 bed_slope=vec2(ground(local_uv+step_uv).x-ground(local_uv-step_uv).x,
        ground(local_uv+step_uv.yx).x-ground(local_uv-step_uv.yx).x)/(2.0*tile_size/16.0);
    // Sub-centimetre films follow support; pools follow hydraulic head. This
    // matches the geometry transition and suppresses grid-scale specular stairs.
    vec2 macro_slope=mix(bed_slope,head_slope,smoothstep(.004,.012,water.y));
    // Optical interface follows the actual rasterized free surface, including
    // the derivative of the depth-dependent bed/head blend.
    vec3 wn=normalize(cross(dFdx(world_position),dFdy(world_position)));
    if(wn.y<0.0)wn=-wn;
    float speed=length(water.zw);
    vec2 position=tile_origin+local_uv*tile_size;
    float clock=review_time>=0.0?review_time:fluid_time;
    // World-space periodic normal map, two phases. Each reset has zero weight.
    // Smooth spatial phase variation avoids the per-pixel random phase seams in the draft.
    float phase=clock/1.4 + .12*sin(dot(position,vec2(1.7,2.3)));
    float p0=fract(phase),p1=fract(phase+.5);
    float w0=1.0-abs(2.0*p0-1.0);
    vec2 velocity=water.zw;
    velocity*=min(1.0,.8/max(length(velocity),.00001));
    vec2 uv0=(position-velocity*(p0*1.4))*4.0;
    vec2 uv1=(position-velocity*(p1*1.4))*4.0;
    // Fixed gradients keep a changing flow field from selecting unstable mip levels.
    vec2 dx=dFdx(position)*4.0,dy=dFdy(position)*4.0;
    vec2 r0=textureGrad(u_ripple_normal,uv0,dx,dy).xy*2.0-1.0;
    vec2 r1=textureGrad(u_ripple_normal,uv1,dx,dy).xy*2.0-1.0;
    float flow=smoothstep(.003,.30,speed);
    float pool_calm=mix(1.0,.45,smoothstep(.07,.18,water.y));
    vec2 ripple=mix(r1,r0,w0)*mix(.018,.19,flow)*pool_calm;
    // No constant drift in still pools: motion follows the solved discharge.
    wn=normalize(wn+vec3(ripple.x,0.0,ripple.y));
    // This surface is two-sided: orient the optical normal toward the viewer.
    // A back-facing slope must not clamp to grazing Fresnel and become a silver sheet.
    vec3 world_view=normalize(CAMERA_POSITION_WORLD-world_position);
    if(dot(wn,world_view)<0.0)wn=-wn;
    vec3 n=normalize((VIEW_MATRIX*vec4(wn,0.0)).xyz),view=normalize(VIEW);
    NORMAL=n;float cosine=clamp(dot(n,view),0.0,1.0);
    float fresnel=.0204+.9796*pow(1.0-cosine,5.0);
#ifdef CREEK_CHEAP_REFRACTION
    vec3 ray=refract(-normalize(CAMERA_POSITION_WORLD-world_position),wn,1.0/1.333);
    float thickness=water.y/max(abs(ray.y),.25);
    vec2 bed_position=position+ray.xz*thickness;
    // Optional approximation: only the substrate, not submerged objects or shadows.
    vec3 behind=texture(u_substrate_albedo,bed_position*3.0).rgb;
    behind*=clamp(sky_energy*.55,.15,1.0);
#else
    float gap=max(scene_depth(SCREEN_UV,INV_PROJECTION_MATRIX)+VERTEX.z,0.0);
    float thickness=min(gap,max(.0003,water.x-bed.y)/max(cosine,.2));
    vec3 flat_view=normalize((VIEW_MATRIX*vec4(0.0,1.0,0.0,0.0)).xyz);
    // Refraction vanishes with optical thickness. The old fixed offset could
    // move the bed several pixels even beneath the minimum visible slope film.
    float refraction_weight=smoothstep(.001,.05,thickness);
    vec2 uv=clamp(SCREEN_UV+clamp((n.xy-flat_view.xy)*.045,vec2(-.012),vec2(.012))*refraction_weight,vec2(.002),vec2(.998));
    if(scene_depth(uv,INV_PROJECTION_MATRIX)<-VERTEX.z)uv=SCREEN_UV;
    vec3 behind=textureLod(screen_tex,uv,0.0).rgb;
#endif
    // Restrained wet-bed contrast and centimetre-scale absorption, not opaque paint.
    // The transmitted scene already contains bed lighting; do not darken it again.
    vec3 absorb=exp(-vec3(.45,.12,.08)*thickness);
    vec3 transmission=behind*absorb+vec3(.035,.085,.065)*(vec3(1.0)-absorb);
    // A compact sun glint moves with the actual surface/ripple normal.
    // Keep transmission intact outside the highlight instead of reflecting a canopy.
    vec3 light_dir=normalize(light_direction);
    vec3 half_vector=normalize(light_dir+world_view);
    float highlight=pow(max(dot(wn,half_vector),0.0),384.0)
        * max(dot(wn,light_dir),0.0);
    vec3 glint=vec3(1.0,.97,.91)*highlight*mix(.35,1.0,fresnel)
        * max(sky_energy,0.0)*2.0;
    // An unpatterned, restrained interface sheen keeps clear water readable
    // between sun glints. Angle dependence uses the moving surface normal;
    // cap the contribution so grazing films never become opaque silver sheets.
    float sheen_weight=.025+.095*pow(1.0-cosine,2.0);
    vec3 sheen_color=vec3(.24,.36,.38)*max(sky_energy,0.0);
    EMISSION=mix(transmission,sheen_color,sheen_weight)+glint;
    if(water_debug==1){ALBEDO=vec3(0.0);EMISSION=wn*.5+.5;}
    if(water_debug==2){ALBEDO=vec3(0.0);EMISSION=transmission;}
    if(water_debug==3){ALBEDO=vec3(0.0);EMISSION=glint;}
    if(water_debug==4){ALBEDO=vec3(0.0);EMISSION=vec3(fresnel);}
    if(debug_surface){ALBEDO=vec3(0.0);EMISSION=vec3(.0,.5,.9);}
    ALBEDO=EMISSION;EMISSION=vec3(0.0);
    // A sub-mm display film remains visible only where conserved water exists.
    ALPHA=smoothstep(.0000001,.00002,water.y)*smoothstep(.015,.25,tint_visibility);
}
