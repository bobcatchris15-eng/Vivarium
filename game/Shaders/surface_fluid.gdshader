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
varying vec2 local_uv;
varying vec3 world_position;
vec4 read_state(ivec2 p){
    vec4 a=texelFetch(previous_state,p,0),b=texelFetch(current_state,p,0);
    if(a.y<=0.0)a.x=b.x;if(b.y<=0.0)b.x=a.x;
    return mix(a,b,state_blend);
}
vec4 fluid(vec2 uv){
    vec2 grid=uv*16.0+vec2(.5);ivec2 base=ivec2(floor(grid));vec2 f=fract(grid);
    // Do not interpolate the supported surface across an unsupported ledge.
    // The ballistic state owns liquid between the two separated support levels.
    ivec2 anchor_key=ivec2(floor(grid+vec2(.5)));
    vec4 anchor=read_state(anchor_key);
    vec2 anchor_slope=mix(texelFetch(previous_support,anchor_key,0).xy,texelFetch(current_support,anchor_key,0).xy,state_blend);
    float anchor_head=anchor.x+dot(anchor_slope,(grid-vec2(anchor_key))*tile_size/16.0);
    vec4 s=vec4(0);float weight=0.0;float coverage=0.0;
    for(int z=0;z<2;z++)for(int x=0;x<2;x++){
        ivec2 key=base+ivec2(x,z);vec4 v=read_state(key);vec2 slope=mix(texelFetch(previous_support,key,0).xy,texelFetch(current_support,key,0).xy,state_blend);v.x+=dot(slope,(grid-vec2(key))*tile_size/16.0);float w=(x==0?1.0-f.x:f.x)*(z==0?1.0-f.y:f.y);
        float wet=v.y>0.00000001?1.0:0.0;if(anchor.y>.00000001&&abs(v.x-anchor_head)>tile_size/16.0)wet=0.0;coverage+=w*wet;w*=clamp(v.y/.001,.001,1.0);s+=vec4(v.x,v.y,v.z,v.w)*w*wet;weight+=w*wet;
    }
    if(weight<=.000001)return vec4(-1000.0,0.0,0.0,0.0);
    s/=weight;s.y=coverage;return s;
}
vec2 surface_slope(vec2 uv){
    vec2 grid=uv*16.0+vec2(.5);ivec2 base=ivec2(floor(grid));vec2 f=fract(grid);vec2 sum=vec2(0);float weight=0.0;
    for(int z=0;z<2;z++)for(int x=0;x<2;x++){
        ivec2 key=base+ivec2(x,z);vec4 v=read_state(key);float w=(x==0?1.0-f.x:f.x)*(z==0?1.0-f.y:f.y)*clamp(v.y/.001,.001,1.0)*step(.00000001,v.y);
        sum+=mix(texelFetch(previous_support,key,0).xy,texelFetch(current_support,key,0).xy,state_blend)*w;weight+=w;
    }
    return weight>0.000001?sum/weight:vec2(0);
}
vec4 ground(vec2 uv){float n=float(textureSize(solid,0).x);return texture(solid,(uv*(n-1.0)+.5)/n);}
void vertex(){
    local_uv=UV;vec4 water=fluid(UV);vec4 bed=ground(UV);
    VERTEX.xz*=tile_size;water.x=mix(bed.x,water.x,water.y);VERTEX.y=max(water.x,bed.x+.00005);
    world_position=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
}
float scene_depth(vec2 uv,mat4 inverse_projection){vec4 p=inverse_projection*vec4(uv*2.0-1.0,texture(depth_tex,uv).r,1.0);return -p.z/p.w;}
void fragment(){
    vec4 water=fluid(local_uv);vec4 bed=ground(local_uv);water.x=mix(bed.x,water.x,water.y);
    if(abs(world_position.y-water.x)>.002)discard;
    if(water.y<=0.00000001||water.x<=bed.x+.000003||bed.z<.5)discard;
    // The fixed-head reservoir exclusively presents liquid at its own surface.
    if(bed.x<pond_level)discard;
    vec2 gradient=surface_slope(local_uv);vec3 wn=normalize(vec3(-gradient.x,1.0,-gradient.y));
    float speed=length(water.zw);float activity=smoothstep(.015,.3,speed);
    vec2 position=tile_origin+local_uv*tile_size;
    vec2 direction=water.zw/max(speed,.001);
    float phase=dot(position,direction)*160.0-fluid_time*min(speed,1.0)*160.0;
    // Submillimetre travelling capillary normals; no displacement or added volume.
    vec2 cross_direction=vec2(-direction.y,direction.x);
    float cross_phase=dot(position,cross_direction)*230.0+sin(phase*.5);
    vec2 ripple=direction*cos(phase)*.075+cross_direction*sin(cross_phase)*.025;
    wn=normalize(wn+vec3(ripple.x,0.0,ripple.y)*activity);
    vec3 n=normalize((VIEW_MATRIX*vec4(wn,0.0)).xyz),view=normalize(-VERTEX);
    NORMAL=n;float cosine=clamp(dot(n,view),0.0,1.0);
    float fresnel=.0204+.9796*pow(1.0-cosine,5.0);
    float gap=max(scene_depth(SCREEN_UV,INV_PROJECTION_MATRIX)+VERTEX.z,0.0);
    float thickness=min(gap,max(.0003,water.x-bed.y)/max(cosine,.2));
    vec2 uv=clamp(SCREEN_UV+n.xy*.004*smoothstep(.001,.012,gap),vec2(.002),vec2(.998));
    if(scene_depth(uv,INV_PROJECTION_MATRIX)<-VERTEX.z)uv=SCREEN_UV;
    vec3 transmission=textureLod(screen_tex,uv,0.0).rgb*exp(-vec3(.8,.22,.45)*thickness);
    vec3 reflection=mix(vec3(.020,.022,.018),vec3(.070,.075,.065),smoothstep(-.1,.8,reflect(-normalize((INV_VIEW_MATRIX*vec4(view,0.0)).xyz),wn).y));
    ALBEDO=vec3(.015,.022,.019);METALLIC=0.0;ROUGHNESS=.06;SPECULAR=.5;
    vec3 reflected=reflect(-normalize((INV_VIEW_MATRIX*vec4(view,0.0)).xyz),wn);
    float panel=exp(-pow(reflected.z*12.0,2.0))*smoothstep(.40,.85,reflected.y);
    float edge=(1.0-smoothstep(.0005,.003,gap))*smoothstep(.00005,.0005,water.x-bed.x);
    // Match the preserved pond material's dark enclosure and overhead grow-light response.
    EMISSION=transmission*(1.0-fresnel*.65)+reflection*fresnel*.4+vec3(1.4)*panel*fresnel+vec3(.012)*edge;
    if(debug_surface){ALBEDO=vec3(0.0);EMISSION=vec3(.0,.5,.9);}
    ALPHA=smoothstep(.000005,.00005,water.x-bed.x);
}
