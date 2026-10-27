// Opaque volumetric coverage tissues. Moss forms use scanned, world-space surface detail;
// basal mat is clipped with stable noise so its cell grid does not read as a tile.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx, cull_disabled;

#include "res://Shaders/surface.gdshaderinc"

uniform vec4 primary_tint : source_color = vec4(.12,.30,.16,1.);
uniform vec4 secondary_tint : source_color = vec4(.20,.38,.19,1.);
uniform vec4 accent_tint : source_color = vec4(.68,.25,.06,1.);
uniform float form = 0.;
uniform float organ = 0.;
uniform sampler2D moss_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_nrm : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D moss_rgh : hint_roughness_r, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D lichen_col : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
varying vec3 position_world;
varying float basal;
varying float basal_density;
varying float growth_tip;

void vertex() {
    position_world=(MODEL_MATRIX*vec4(VERTEX,1.)).xyz;
    growth_tip=clamp(COLOR.a,0.0,1.0);
    basal=float(UV2.x < 0.0);
    basal_density=clamp(-UV2.x-1.0,0.0,1.0);
}

void fragment() {
    float health=clamp(UV.x,0.,1.);
    float living=clamp(UV.y,0.,1.);
    // Density is interpolated from a warped occupancy field, never vertex pigment.
    // Fully occupied interiors survive; only the millimetre fringe is clipped.
    float edge=fbm(position_world.xz*23.0)*.52+vnoise(position_world.xz*91.0)*.48;
    if(basal>.5 && basal_density < .30+(edge-.5)*.48) discard;
    if(!FRONT_FACING) NORMAL=-NORMAL;
    vec3 wn=finite_surface_normal((INV_VIEW_MATRIX*vec4(NORMAL,0.0)).xyz,vec3(0.0,1.0,0.0));
    vec3 pigment=mix(primary_tint.rgb,secondary_tint.rgb,0.34);
    if(organ>.5) pigment=accent_tint.rgb;
    vec3 scan_n=wn;
    float rough=0.88;
    float ao=1.0;
    vec3 bump=vec3(0.0);

    if(form<3.5) {
        // Palette is kept in the field's deep olive/forest range. Scanned luminance
        // modulates it after normalization, preserving pigment rather than painting over it.
        vec3 palette=mix(vec3(.12,.19,.055),vec3(.27,.32,.10),clamp(.22+basal_density*.28,0.0,1.0));
        if(form>0.5) palette=mix(vec3(.10,.16,.07),vec3(.23,.30,.13),.5);
        if(form>1.5) palette=mix(vec3(.13,.20,.08),vec3(.31,.34,.12),.5);
        if(form>2.5) palette=mix(vec3(.09,.17,.07),vec3(.24,.31,.14),.5);
        // Palette constants are sRGB swatches; decode them just like authored colors.
        palette=srgb_to_linear(palette*vec3(1.0,1.32,1.06));
        pigment=mix(palette,pigment,.06);
        vec4 scan=triplanar(moss_col,moss_nrm,moss_rgh,position_world/.24,wn,5.0,scan_n);
        float avg=max(dot(textureLod(moss_col,vec2(.5),10.0).rgb,vec3(.2126,.7152,.0722)),.05);
        float lum=clamp(dot(scan.rgb,vec3(.2126,.7152,.0722))/avg,.30,1.65);
        float broad=fbm(position_world.xz*3.2);
        float grain=fbm(position_world.xz*vec2(8.0,35.0));
        vec2 tuft_p=mat2(vec2(.82,.57),vec2(-.57,.82))*position_world.xz;
        tuft_p+=vec2(vnoise(tuft_p*31.0),vnoise(tuft_p*29.0+7.0))*.013;
        float tuft_speck;
        vec4 tuft_shape=moss_clumps(tuft_p*45.0,tuft_speck);
        float tuft=clamp(tuft_shape.x,0.0,1.0);
        float cavities=smoothstep(.22,.72,tuft);
        float tips=smoothstep(.61,.91,grain);
        pigment*=mix(1.0,lum,basal>.5 ? .85 : .28)*(.79+.18*broad+.13*grain);
        pigment*=mix(vec3(.52,.55,.38),vec3(1.02,1.0,.82),cavities);
        pigment*=mix(vec3(.93,.95,.78),vec3(1.08,1.09,.83),tips);
        if(organ>.5) {
            // Leaflets retain the species accent, with restrained fine veins and warm tips.
            float vein=1.0-smoothstep(.0,.055,abs(fract(position_world.x*95.0+position_world.z*13.0)-.5));
            pigment=mix(pigment,accent_tint.rgb*lum,mix(.18,.34,tips));
            pigment*=.94+.06*vein;
        }
        rough=clamp(mix(.94,scan.a,.55),.78,.98);
        ao=mix(.66,1.0,cavities)*mix(.79,1.0,broad);
                if(basal<.5) {
            pigment*=mix(vec3(.63,.78,.44),vec3(1.14,1.16,.84),growth_tip);
            // Small leaf blades read as tissue, with restrained scan relief.
            ao=mix(ao,1.0,.55);
        }
        bump=(scan_n-wn)*.56+vec3(tuft_shape.y,0.0,tuft_shape.z)*.22;

    } else {
        vec3 scan=texture(lichen_col,position_world.xz/.22).rgb;
        float lum=max(dot(scan,vec3(.299,.587,.114)),.04);
        pigment*=clamp(vec3(.42)+scan*1.15,vec3(.45),vec3(1.35));
        rough=.91;
        ao=mix(.55,1.0,clamp(lum*1.5,0.0,1.0));
    }

    pigment=mix(vec3(.075,.054,.027),pigment,living);
    ALBEDO=clamp(pigment*mix(.72,1.0,health)*ao,vec3(0.0),vec3(.78));
    ROUGHNESS=rough;
    SPECULAR=.18;
    AO=clamp(ao,.45,1.0);
    NORMAL=finite_surface_normal((VIEW_MATRIX*vec4(finite_surface_normal(wn+bump*.5,wn),0.0)).xyz,NORMAL);
}
