// Dedicated thin/volumetric foliage. CUSTOM0=(seed,variant,age,flex), CUSTOM1=(attachment,length).
shader_type spatial;
render_mode diffuse_burley, cull_disabled;
#include "res://Shaders/surface.gdshaderinc"
#include "res://Shaders/living_leaf_optics.gdshaderinc"
#include "res://Shaders/leaf_direct_light.gdshaderinc"
uniform sampler2DArray tissue_color : source_color, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray tissue_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2DArray tissue_physical : filter_linear_mipmap_anisotropic, repeat_enable;
uniform sampler2D vein_normal : hint_normal, filter_linear_mipmap_anisotropic, repeat_disable;
uniform sampler2D vein_mask : filter_linear_mipmap_anisotropic, repeat_disable;
uniform vec3 leaf_top = vec3(.045,.14,.045);
uniform vec3 leaf_back = vec3(.14,.20,.09);
uniform vec3 top_pigment = vec3(-1.0);
uniform vec3 back_pigment = vec3(-1.0);
uniform float tissue_albedo_mode = 0.0;
uniform float tissue_gain = 1.0;
uniform float leaf_roughness = .45;
uniform float back_roughness = .84;
uniform float leaf_transmission = .28;
uniform float transmission = -1.0;
uniform float cuticle_sheen = .35;
uniform float normal_strength = .45;
// Production lamina curvature, selected after 0x/2x/6x/12x stress captures.
uniform float lamina_curl_strength = 1.5;
uniform float vein_strength = .4;
uniform float wind_strength = 0.0;
uniform float plant_stiffness = 7.;
uniform float tissue_scale = 1.6;
uniform int tissue_layers = 4;
uniform float canopy_occlusion = .55;
// Shoot-level chlorophyll mottling; separate from unrelated UV texture noise.
uniform float leaf_pigment_mottle = 0.0;
uniform bool volumetric = false;
uniform int leaf_variegation = 0;
uniform float review_time = -1.;
uniform vec3 bioluminescence_color = vec3(0.0);
uniform float bioluminescence_strength = 0.0;
uniform float night_factor = 0.0;
uniform int emission_mask_mode = 0;
uniform bool clip_to_hex = false;
uniform float hex_apothem = 6.92820323;
varying vec4 plant;
varying vec4 blade;
varying vec3 position_world;
varying vec3 position_local;
varying vec3 v_obj_pos;

vec3 turn(vec3 p, vec3 axis, float angle) {
    return p*cos(angle) + cross(axis,p)*sin(angle) + axis*dot(axis,p)*(1.-cos(angle));
}
void vertex() {
    bool has_custom = (abs(INSTANCE_CUSTOM.x) + abs(INSTANCE_CUSTOM.y) + abs(INSTANCE_CUSTOM.z) + abs(INSTANCE_CUSTOM.w)) > 1e-4;
    plant = has_custom ? INSTANCE_CUSTOM : vec4(1.0, 1.0, 0.5, 0.35);
    blade = CUSTOM0;
    v_obj_pos = VERTEX;
    position_local = VERTEX;
    // Review captures freeze review_time; live play uses the real clock.
    float clock = review_time >= 0.0 ? review_time : TIME;
    // Blade UV.x runs root→tip, so it is the natural bend/tip weight.
    float tip = clamp(UV.x, 0.0, 1.0);
    tip = tip * tip * (3.0 - 2.0 * tip);
    float leaf_len = clamp(CUSTOM1.w, 0.01, 0.5);
    float amp = clamp(leaf_len * 0.045, 0.0006, 0.0026);
    float wind_gain = mix(0.50, 1.20, clamp(wind_strength, 0.0, 1.0));
    vec3 wp0 = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
    float phase = plant.w * 6.2831853 + blade.x * 2.4 + wp0.x * 0.9 + wp0.z * 0.7;
    // Slow primary air current (1.5–3 mm at the tip) plus a tiny normal flutter.
    // Displacement scales with tip weight, so the petiole stays anchored.
    VERTEX.x += sin(clock * 0.62 + phase) * amp * wind_gain * tip;
    VERTEX.z += cos(clock * 0.51 + phase * 1.13) * amp * 0.78 * wind_gain * tip;
    VERTEX += NORMAL * sin(clock * 2.70 + phase * 2.9) * 0.00055 * wind_gain * tip;
    position_world = (MODEL_MATRIX*vec4(VERTEX,1.)).xyz;

}
vec3 detail_normal(vec3 base, vec2 uv, vec3 detail) {
    return cotangent_normal(base, position_world, uv, detail);
}
void fragment() {
    if (clip_to_hex) {
        float d = max(abs(position_world.z), 0.8660254037844386 * abs(position_world.x) + 0.5 * abs(position_world.z)) - hex_apothem;
        if (d > 0.001) {
            discard;
        }
    }
    tissue_phase_mask = UV2.y > 0.5 ? 1.0 : 0.0;
    tissue_nonleaf_mask = (UV2.y < 0.5 && COLOR.a > 0.5) ? 1.0 : 0.0;
    // Invert backface normal so undersides do not render dark or inverted.
    if (!FRONT_FACING) {
        NORMAL = -NORMAL;
    }
    float cam_dist = length(CAMERA_POSITION_WORLD - position_world);
    float micro_fade = leaf_micro_fade(CAMERA_POSITION_WORLD, position_world);
    bool back = !FRONT_FACING && !volumetric;
    float side = back ? 1. : 0.;
    float seed = fract(blade.x+plant.w*.618);
    float tl = float(max(tissue_layers,1));
    float layer = mod(blade.y+floor(plant.w*4.),tl);
    vec2 tissue_uv = UV*vec2(tissue_scale,tissue_scale*.7)+vec2(seed*3.13,seed*7.31);
    float blend = .12 + .18*(.5+.5*sin(UV.x*3.14159+seed*6.28));
    float second = mod(layer+1.,tl);
    // Sampled early: the baked physical channels (thickness/striation) steer the
    // structural venation before the pigment is composed.
    vec4 physical = mix(texture(tissue_physical,vec3(tissue_uv,layer)),
        texture(tissue_physical,vec3(tissue_uv,second)),blend);
    vec3 photograph = mix(texture(tissue_color,vec3(tissue_uv,layer)).rgb,
        texture(tissue_color,vec3(tissue_uv,second)).rgb,blend);
    // Masters are normalized offline: luminance carries pigment variation, not height.
    photograph *= 2.; // Offline masters use a linear .5 neutral reference.
    vec3 actual_top = top_pigment.r >= 0.0 ? top_pigment : leaf_top;
    vec3 actual_back = back_pigment.r >= 0.0 ? back_pigment : leaf_back;
    float actual_trans = transmission >= 0.0 ? transmission : leaf_transmission;
    vec3 pigment = mix(actual_top, actual_back, side);
    pigment = leaf_abaxial_tint(pigment, side);
    // Photo variation plus stable organ-specific pigment clouding: avoid paint-flat laminae.
    vec3 color = pigment*mix(vec3(1.),photograph,.92);
    if (tissue_albedo_mode > 0.5) {
        // Own-pool species: the baked texture IS the albedo (linear). Underside = same tissue
        // scaled by the species back/top pigment ratio, pulled toward its own luminance.
        vec3 own = photograph * .5 * tissue_gain;
        vec3 ratio = clamp(actual_back / max(actual_top, vec3(.004)), vec3(.6), vec3(1.8));
        vec3 under = own * mix(vec3(1.), ratio, .6);
        under = mix(under, vec3(dot(under, vec3(.2126,.7152,.0722))), .25) * 1.08;
        color = mix(own, under, side);
    }
    // High-frequency epidermal variation is only resolved close to camera; fade
    // it to its neutral value instead of paying for subpixel noise everywhere.
    if (micro_fade > 0.01) {
        float meso = leaf_mesophyll_pattern(UV, seed);
        color *= mix(1.0, mix(0.95, 1.05, clamp((meso - 0.94) / 0.12, 0.0, 1.0)), micro_fade);
    }
    // Stable organ and plant identity shift chlorophyll against warmer pigments.
    // CUSTOM0 carries a fixed per-leaf pseudo-age, not simulation age; the tip shift is subtle.
    float pigment_age = clamp(blade.z, 0.0, 1.0);
    float pigment_shift = (seed - .5) * .18 + (pigment_age - .5) * .12;
    color *= vec3(1.0 + pigment_shift, 1.0 - pigment_shift * .22,
                  1.0 - pigment_shift * .65);
    float tip_age = smoothstep(.72, 1.0, UV.x) * pigment_age * .10;
    color *= mix(vec3(1.0), vec3(1.20, 1.02, .72), tip_age);
    // Two spectral bands covary spatially, as they do under varying chlorophyll
    // density. Coherent over the blade, seed-stable, no screen-space flicker.
    if (leaf_pigment_mottle > 0.001 && micro_fade > 0.01) {
        float chlorophyll = vnoise(UV * vec2(7.1, 10.8) +
            vec2(seed * 16.31, seed * 25.77));
        float mottle = (chlorophyll - .5) * leaf_pigment_mottle * micro_fade;
        color *= clamp(vec3(1.0 - .42*mottle,
                            1.0 + .32*mottle,
                            1.0 - .58*mottle), vec3(.10), vec3(2.30));
    }
    color *= mix(1.0,.94,smoothstep(.36,.50,abs(UV.y-.5)));
    vec4 anatomy = texture(vein_mask,UV);
    // Structural vascular architecture. The baked mask only carries a uniform
    // fine reticulum, so the midrib and arched pinnate secondaries are rebuilt
    // from the blade's own UV frame and stay readable at any leaf size.
    float along = clamp(UV.x, 0.0, 1.0);
    float across = UV.y - 0.5;
    float abs_across = abs(across) * 2.0;
    // Thick/succulent laminae (baked physical B) suppress pinnate arching: their
    // veins are longitudinal and internal rather than proud of the surface.
    float striation = smoothstep(0.55, 0.95, physical.b);
    float pinnate = 1.0 - striation * 0.7;
    // Tapering primary midrib, widest at the petiole.
    float midrib_halfw = mix(0.042, 0.008, pow(along, 0.75));
    float midrib = 1.0 - smoothstep(0.0, midrib_halfw, abs_across);
    float midrib_profile = cos(clamp(across / max(midrib_halfw, 0.001), -1.0, 1.0) * 1.5707963);
    // Pinnate secondaries leave the midrib and arch forward toward the margin.
    float sec_coord = along * 7.5 - pow(abs_across, 1.35) * 1.65 + seed * 1.7;
    float sec_dist = abs(fract(sec_coord) - 0.5);
    // Widen the band by the local pixel footprint so distant arches fade instead of moiring.
    float sec_aa = max(fwidth(sec_coord), 0.0);
    float sec_vein = (1.0 - smoothstep(0.02, 0.085 + sec_aa, sec_dist)) * smoothstep(0.02, 0.14, abs_across) * (1.0 - smoothstep(0.78, 0.98, abs_across)) * (1.0 - smoothstep(0.88, 0.99, along)) * pinnate;
    // Inter-vein bullate pillows: the mesophyll domes between successive arches.
    float bullate = sin(fract(sec_coord) * 3.14159265) * smoothstep(0.04, 0.22, abs_across) * (1.0 - smoothstep(0.75, 0.96, abs_across)) * pinnate;
    float struct_vein = clamp(max(midrib, sec_vein * 0.72), 0.0, 1.0);
    color *= 1.+(blade.z-.5)*.08+(plant.x-.5)*.05;
    // Basal petiole collar: the lamina is inserted into the stalk, so its first
    // centimetres sit in the petiole's shadow.
    color *= mix(0.74, 1.0, smoothstep(0.0, 0.09, along));
    // The thinner margin dries and reddens slightly ahead of the lamina.
    color = mix(color, color * vec3(1.12, 0.94, 0.76), smoothstep(0.78, 0.98, abs_across) * 0.28);
    // Inter-vein mesophyll is richer and slightly deeper green than the raised vascular bundles,
    // making the primary midrib and arched pinnate secondaries read clearly at close range.
    color *= mix(1.0, 0.92 + 0.10 * bullate, (1.0 - struct_vein) * 0.55);
    // Pale yellow-green/amber vascular bundle tone; the abaxial rib is paler still.
    color = mix(color, color * vec3(1.10, 1.12, 0.98) + vec3(0.005, 0.007, 0.002), struct_vein * (0.35 + 0.15 * side));
    color = mix(color, color * vec3(1.06, 1.08, 1.00), anatomy.r * vein_strength * 0.40);
    float topf = 1. - side;
    // actual_back already supplies the species' abaxial pigment; apply only a restrained
    // underside matte shift instead of double-brightening the back face.
    color *= mix(vec3(1.0), vec3(0.94, 0.96, 0.92), side);
    // Pale midrib/veins and young-leaf freshness; lift only genuinely crushed blacks.
    color = mix(color, color*vec3(1.12,1.15,1.04)+vec3(.002,.003,.001), anatomy.r*topf*.05);
    color *= mix(1.15, 1., smoothstep(.010,.045,dot(color,vec3(.2126,.7152,.0722))));
    if (leaf_variegation == 1) color = mix(color, vec3(dot(color,vec3(.33)))*vec3(2.5,2.8,2.2)+.03, anatomy.r*topf*.6);
    else if (leaf_variegation == 2) color = mix(color, vec3(.30,.34,.32), smoothstep(.45,.65,texture(tissue_physical,vec3(tissue_uv*.5,layer)).b)*(1.-anatomy.r)*topf*.55);
    else if (leaf_variegation == 3) color = mix(color, vec3(.20,.025,.07)*(.8+.4*photograph.g), side*.85);
    if (volumetric) color = mix(color,srgb_to_linear(COLOR.rgb),smoothstep(.64,.99,UV.x)*.72);
    if (leaf_variegation == 5) {
        // Azure crystal frost (glassfinger). The family's epidermal bladder cells are grown
        // here as a saturated blue crystalline veneer over the distal blade, densest towards
        // the tip and strongest along the dorsal ridge, with the anthocyanin flush of the
        // real ice plant still burning red at the very point.
        float ridge = 1.0 - smoothstep(0.03, 0.42, abs(across));
        // The vesicles are discrete, so the frost is cellular and patchy, not a smooth wash.
        // Their cell noise is microscopic: fade to the neutral wash when subpixel.
        float vesicle = 0.5;
        if (micro_fade > 0.01 && cam_dist < 4.0) {
            float cellA = vnoise(vec2(along * 20.0, UV.y * 16.0) + seed * 23.7);
            float cellB = vnoise(vec2(along * 47.0, UV.y * 38.0) + seed * 51.3);
            vesicle = mix(0.5, smoothstep(0.54, 0.86, cellA * 0.68 + cellB * 0.32), micro_fade);
        }
        float reach = smoothstep(0.42, 0.92, along);
        // A continuous wash carries the blue, with the vesicles burning brighter inside it.
        float frost = (0.30 + 0.70 * vesicle) * reach * (0.34 + 0.66 * ridge);
        // Deep cobalt, not a pale wash: the blue has to carry the eye at plant scale.
        vec3 azure = vec3(0.004, 0.026, 0.22);
        vec3 azureHot = vec3(0.012, 0.145, 0.90);
        color = mix(color, mix(azure, azureHot, vesicle), clamp(frost, 0.0, 1.0) * 0.90);
        // The tip keeps the species' red flush, burning through the frost.
        float flush = smoothstep(0.84, 1.0, along);
        color = mix(color, vec3(0.42, 0.030, 0.050), flush * 0.72);
    }
    vec3 micro = unpack_normal(mix(texture(tissue_normal,vec3(tissue_uv,layer)).rgb,texture(tissue_normal,vec3(tissue_uv,second)).rgb,blend));
    vec3 veins = unpack_normal(texture(vein_normal,UV).rgb);
    // Add slopes, retaining both tissue relief and structural veins.
    // Physically directional blade curl: midrib-to-margin roll and longitudinal
    // pitch, coherent across each blade. Exaggeration is intentional at >1.
    float transverse = UV.y - .5;
    float edge_roll = sign(transverse) * smoothstep(.07,.46,abs(transverse));
    float leaf_span = smoothstep(.03,.23,UV.x) * (1.0 - smoothstep(.87,.99,UV.x));
    float individual_roll = mix(-1.,1.,step(.5,fract(seed*13.71)));
    vec2 lamina_slope = vec2(
        cos(UV.x * 3.14159265) * .34 * leaf_span,
        edge_roll * sin(UV.x*3.14159265) * leaf_span * individual_roll * .85);
    // Analytical relief of the vascular hierarchy itself.
    // Adaxial: a narrow channeled rib with convex bullate mesophyll cushions
    // between the secondaries. Abaxial: rib and veins stand proud as a keel.
    float rib_polarity = mix(-0.60, 1.0, side);
    float sec_polarity = mix(1.0, -1.0, side);
    float sec_arc_d = cos(fract(sec_coord) * 3.14159265) * sec_polarity;
    float midrib_d = -sin(clamp(across / max(midrib_halfw, 0.001), -1.0, 1.0) * 1.5707963);
    vec2 struct_slope = vec2(
        midrib_profile * rib_polarity * 0.055 + sec_arc_d * pinnate * 0.10,
        midrib_d * rib_polarity * 0.085 + sec_arc_d * pinnate * (0.20 + 0.06 * bullate));
    vec3 slopes = normalize(vec3(
        micro.xy*normal_strength + veins.xy*vein_strength*mix(-0.32, 1.05, side)
        + lamina_curl_strength * lamina_slope
        + struct_slope * mix(0.70, 1.15, clamp(vein_strength, 0.0, 1.0)), 1.));
    vec3 wn = finite_surface_normal((INV_VIEW_MATRIX*vec4(NORMAL,0.)).xyz, vec3(0.0,1.0,0.0));
    vec3 mapped=detail_normal(wn,UV,slopes);
    NORMAL=finite_surface_normal((VIEW_MATRIX*vec4(mapped,0.)).xyz, vec3(0.0,0.0,1.0));
    // Reconstruct plant-fiber orientation from true mesh UV derivatives,
    // not from the camera plane; rotate into the custom light's view frame.
    vec2 ux = dFdx(UV), uy = dFdy(UV);
    vec3 px = dFdx(position_world), py = dFdy(position_world);
    float det = ux.x * uy.y - uy.x * ux.y;
    vec3 fiber_world = (px * uy.y - py * ux.y) /
        (abs(det) > 1e-7 ? det : 1.0);
    vec3 fiber_view = (VIEW_MATRIX * vec4(fiber_world, 0.0)).xyz;
    float fiber_len = length(fiber_view);
    tissue_fiber_view = fiber_len > 1e-5 ? fiber_view / fiber_len : vec3(1.0, 0.0, 0.0);
    tissue_fiber_mask = fiber_len > 1e-5 && abs(det) > 1e-7 ? (1.0 - side) : 0.0;
    float ground_contact = smoothstep(0.0, 0.14, max(v_obj_pos.y, 0.0));
    color *= mix(vec3(0.55, 0.50, 0.42), vec3(1.0), ground_contact);
    color *= mix(.93,1.07,fract(seed*5.3+plant.x*2.1))*vec3(1.,1.,mix(.95,1.03,fract(seed*3.7)));
    ALBEDO=mix(vec3(dot(color,vec3(.333)))*vec3(1.25,1.05,.55),color,.3+.7*smoothstep(0.,.45,plant.y));
    // Species-specific cuticular gloss on adaxial face; rougher, paler abaxial face
    float wax = cuticle_sheen * topf;
    ROUGHNESS=clamp(mix(leaf_roughness - wax * 0.08, back_roughness + 0.08, side) + (physical.r - 0.5) * 0.12 - anatomy.r * 0.02, 0.20, 0.96);
    // Filter unresolved normal detail into the specular lobe instead of allowing
    // subpixel veins/folds to flash white as the camera or blade moves.
    vec3 normal_dx = dFdx(NORMAL), normal_dy = dFdy(NORMAL);
    float normal_variance = dot(normal_dx, normal_dx) + dot(normal_dy, normal_dy);
    if (!(normal_variance >= 0.0 && normal_variance < 1e12)) normal_variance = 0.0;
    ROUGHNESS = clamp(sqrt(ROUGHNESS * ROUGHNESS + min(2.0 * normal_variance, .22)), .24, 1.0);
    SPECULAR=volumetric ? .45 : mix(0.22, mix(0.40, 0.54, cuticle_sheen), topf);
    // Structural veins are optically thick too, so the rib and laterals read
    // dark in silhouette while the thin mesophyll between them glows.
    float vein_thickness = clamp(max(anatomy.g * 0.75 + anatomy.r * 0.25, struct_vein * 0.88), 0.0, 1.0);
    RIM = mix(0.18, 0.48, cuticle_sheen);
    tissue_rim = RIM;
    float base_tau = clamp(mix(0.35, 2.2, physical.b) + anatomy.r * 0.85, 0.15, 3.0);
    float tau = leaf_vein_tau(base_tau, vein_thickness, side);
    tissue_tau = tau;
    vec3 through=leaf_through(color, actual_trans, vein_thickness,
        mix(.62, 1.35, physical.b), side) * mix(.65, 1., physical.g);
    // Direct transmission is owned by light() (leaf_direct_light.gdshaderinc);
    // built-in BACKLIGHT/SSS_TRANSMITTANCE_* would duplicate it, so none are written.
    float basal_ao = mix(0.76, 1.0, smoothstep(0.0, 0.22, along));
    float furrow_ao = 1.0 - 0.14 * (1.0 - bullate) * pinnate * (1.0 - struct_vein);
        float is_pilot_crown = step(1.6, v_obj_pos.y);
    float norm_h = mix(
        clamp(v_obj_pos.y, 0.0, 1.0),
        clamp((v_obj_pos.y - 4.2) / 13.5, 0.0, 1.0),
        is_pilot_crown
    );
    float radial_shell = clamp(length(v_obj_pos.xz) / 4.8, 0.0, 1.0);
    float pilot_interior_ao = mix(0.38, 1.0, 0.52 * norm_h + 0.48 * radial_shell)
        * (0.76 + 0.24 * smoothstep(-0.25, 0.65, wn.y));
    float inst_depth = 1.0 - canopy_occlusion * (1.0 - norm_h);
    float canopy_depth = mix(inst_depth, pilot_interior_ao, is_pilot_crown);
    float ao = basal_ao * furrow_ao * mix(0.55, 1.0, ground_contact);
    AO = clamp(ao * canopy_depth, 0.16, 1.0);
    AO_LIGHT_AFFECT = mix(0.55, 0.78, is_pilot_crown);
    if (UV2.y < .5 && COLOR.a > .5) {
        ALBEDO=srgb_to_linear(COLOR.rgb);
        NORMAL=finite_surface_normal((VIEW_MATRIX*vec4(wn,0.)).xyz, vec3(0.0,0.0,1.0));
        ROUGHNESS=.72;
        SPECULAR=.35;
        tissue_rim=0.0;
    }
    ALBEDO *= mix(1.0, mix(0.52, 1.06, canopy_depth), is_pilot_crown);
    float marked_nodule = step(1.30, UV2.x) * (1.0 - step(1.50, UV2.x)) * step(0.5, UV2.y);
    float gill_mask = step(0.5, UV2.x) * (1.0 - step(1.5, UV2.x));
    float seed_head_mask = step(1.30, UV2.x) * (1.0 - step(1.50, UV2.x));
    float center_stripe = (1.0 - smoothstep(0.16, 0.25, abs(UV.y - 0.5))) * step(0.5, UV2.y);
    float edge_distance = min(min(UV.x, 1.0 - UV.x), 0.5 - abs(UV.y - 0.5));
    float edge_strip = (1.0 - smoothstep(0.025, 0.075, edge_distance)) * step(0.5, UV2.y);
    float night_mask = 0.0;
    if (emission_mask_mode == 1) night_mask = 1.0;
    else if (emission_mask_mode == 2) night_mask = marked_nodule;
    else if (emission_mask_mode == 3) night_mask = gill_mask * float(FRONT_FACING);
    else if (emission_mask_mode == 4) night_mask = seed_head_mask;
    else if (emission_mask_mode == 5) night_mask = center_stripe;
    else if (emission_mask_mode == 6) night_mask = edge_strip;
    else night_mask = smoothstep(0.45, 0.60, UV2.y);
    EMISSION = bioluminescence_color * bioluminescence_strength * night_factor * night_mask;
    // Emission-only instrument readouts; never used in the production mode.
    if (tissue_material_diagnostic != 0) {
        vec3 world_normal = normalize((INV_VIEW_MATRIX * vec4(NORMAL, 0.0)).xyz);
        vec3 display = tissue_material_diagnostic == 1 ? ALBEDO :
            tissue_material_diagnostic == 2 ? world_normal * .5 + .5 :
            tissue_material_diagnostic == 3 ? vec3(ROUGHNESS) :
            tissue_material_diagnostic == 4 ? through :
            vec3(anatomy.r);
        ALBEDO = vec3(0.0);
        BACKLIGHT = vec3(0.0);
        SPECULAR = 0.0;
        EMISSION = display;
    }
    ALBEDO = clamp(ALBEDO, vec3(0.0), vec3(0.98));
    if (tissue_light_diagnostic == 5) {
        SSS_STRENGTH = 0.0;
        SSS_TRANSMITTANCE_COLOR = vec4(0.0);
        SSS_TRANSMITTANCE_DEPTH = 0.1;
        SSS_TRANSMITTANCE_BOOST = 0.0;
        BACKLIGHT = vec3(0.0);
    }
}
