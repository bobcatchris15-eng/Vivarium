// Ruffle has physical upper/lower shells; reject each shell backside.
// Authored raised moss and lichen structures. Deliberately texture-free: all
// species shape and color come from their geometry and authored vertex colors.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx, cull_back;

uniform vec4 primary_tint : source_color = vec4(.12,.30,.16,1.);
uniform vec4 secondary_tint : source_color = vec4(.20,.38,.19,1.);
uniform float form = 0.0;
uniform float organ = 0.0;
varying vec3 world_position;

vec3 finite_normal(vec3 value, vec3 fallback) {
    float m2=dot(value,value);
    if(m2>1e-12 && m2<1e12) return value*inversesqrt(m2);
    float f2=dot(fallback,fallback);
    return f2>1e-12 && f2<1e12 ? fallback*inversesqrt(f2) : vec3(0.0,1.0,0.0);
}

vec3 safe_srgb_to_linear(vec3 c) {
    c=max(c,vec3(0.0));
    return mix(c/12.92,pow((c+0.055)/1.055,vec3(2.4)),step(0.04045,c));
}

float hash21(vec2 p) { return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453); }
float value_noise(vec2 p) {
    vec2 i=floor(p),f=fract(p);
    vec2 u=f*f*(3.0-2.0*f);
    float a=hash21(i),b=hash21(i+vec2(1.0,0.0));
    float c=hash21(i+vec2(0.0,1.0)),d=hash21(i+vec2(1.0,1.0));
    return mix(mix(a,b,u.x),mix(c,d,u.x),u.y);
}

void vertex() { world_position=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz; }

void fragment() {
    if(!FRONT_FACING) NORMAL=-NORMAL;
    vec3 n=finite_normal((INV_VIEW_MATRIX*vec4(NORMAL,0.0)).xyz,vec3(0.0,1.0,0.0));
    vec3 authored=safe_srgb_to_linear(COLOR.rgb);
    float growth=clamp(COLOR.a,0.0,1.0);
    float grain=value_noise(world_position.xz*vec2(17.0,53.0));
    float tip=smoothstep(0.58,0.91,grain);
    float fine=value_noise(world_position.xz*143.0);
    vec3 color=authored;
    color=mix(color,color*secondary_tint.rgb/max(primary_tint.rgb,vec3(.05)),.10+.12*tip);
    color*=mix(vec3(.76,.80,.61),vec3(1.06,1.08,.83),tip);
    float cell=value_noise(world_position.xz*680.0);
    color*=.91+.07*fine+.04*cell;
    color*=form<3.5 ? vec3(.65,.76,.57) : vec3(1.0);
    color*=mix(.86,1.08,growth);
    color=mix(vec3(.055,.038,.018),color,clamp(UV.y,0.0,1.0));
    color*=mix(.78,1.0,clamp(UV.x,0.0,1.0));
    ALBEDO=clamp(color,vec3(0.0),vec3(.82));
    ROUGHNESS=clamp(mix(.94,.78,tip)*mix(.96,1.0,fine),.70,.98);
    SPECULAR=.16;
    AO=clamp(mix(.72,1.0,tip)*mix(.92,1.0,growth),.55,1.0);
    NORMAL=finite_normal((VIEW_MATRIX*vec4(n,0.0)).xyz,NORMAL);
}
