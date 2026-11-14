shader_type spatial;
render_mode cull_disabled, depth_prepass_alpha;
uniform sampler2D moss_color : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_roughness : hint_default_white, filter_linear_mipmap_anisotropic, repeat_enable;
varying vec4 colony_state;
varying vec4 colony_shape;
varying vec2 colony_kind;
varying vec3 colony_world;

float hash2(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }
float value_noise(vec2 p) {
    vec2 i=floor(p), f=fract(p); f=f*f*(3.0-2.0*f);
    return mix(mix(hash2(i),hash2(i+vec2(1.0,0.0)),f.x),
               mix(hash2(i+vec2(0.0,1.0)),hash2(i+vec2(1.0)),f.x),f.y);
}
void vertex() {
    colony_state=CUSTOM0; colony_shape=CUSTOM1; colony_kind=UV2;
    colony_world=(MODEL_MATRIX*vec4(VERTEX,1.0)).xyz;
}
void fragment() {
    if (!FRONT_FACING) NORMAL = -NORMAL;
    float n=value_noise(colony_world.xz*140.0);
    float fine=value_noise(colony_world.xz*420.0);
    bool moss=colony_kind.x<0.5;
    bool base=colony_kind.y<0.5;
    vec3 color=COLOR.rgb;
    if (moss) {
        color *= mix(vec3(0.48,0.62,0.42), vec3(0.78,0.90,0.66), n);
        if (base) {
            vec3 scanned = texture(moss_color, colony_world.xz * 4.0).rgb;
            color = mix(color, scanned * mix(vec3(0.72,0.64,0.48), vec3(0.70,0.82,0.60), colony_state.x), 0.65);
        } else color *= mix(0.72,1.05,colony_kind.y-1.0);
    } else {
        color *= mix(0.50,0.78,n);
    }
    ALBEDO=color * mix(0.88,1.08,fine);
    ROUGHNESS=moss && base ? mix(0.92,texture(moss_roughness,colony_world.xz*4.0).r,0.45) : mix(0.98,0.82,clamp(colony_state.x,0.0,1.0));
    AO=clamp(colony_shape.z,0.5,1.0);
    // Coverage is a field, not uniform transparency: break sparse rims and lichen crust into
    // stable world-space islands, leaving the substrate visible between them.
    float pattern=mix(n,fine,0.30);
    float occupancy=clamp(COLOR.a,0.0,1.0);
    float mask=base ? smoothstep(pattern-0.12,pattern+0.12,occupancy) : 1.0;
    ALPHA=mask;
    ALPHA_SCISSOR_THRESHOLD=0.5;
}
