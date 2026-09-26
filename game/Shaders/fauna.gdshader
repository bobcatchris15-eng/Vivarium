// Fauna instances. Species colours are material uniforms; per-individual genetics arrive in
// INSTANCE_CUSTOM = (hue shift, marking density, pattern contrast, packed appendage scale + speed + phase).
// The CPU accumulates phase from species-authored body-relative cadence. Vertex COLOR.rgb is the appendage
// offset from its attachment and COLOR.a flags appendages. UV2.x is the visual region; UV2.y is animation role:
// 0 body, 1 locomotor limb, 2 wing/fin, 3 sensory appendage.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx, cull_disabled;

uniform vec3 base_color : source_color = vec3(0.9, 0.3, 0.2);
uniform vec3 ornament_color : source_color = vec3(1.0);
uniform int anim_family = 0;
uniform float anim_amplitude = 1.0;
uniform float anim_idle_motion = 0.0;
uniform float anim_body_wave = 0.2;
uniform float anim_limb_sweep = 0.5;
uniform float anim_limb_lift = 0.3;
uniform float anim_bob = 0.05;
uniform float anim_phase_spread = 0.5;
uniform float anim_duty_factor = 0.6;
uniform float translucency = 0.0;
uniform float carapace = 0.0;   // clearcoat for hard, glossy exoskeletons
uniform float scales = 0.0;     // 1 = fish scales + silvery sheen
uniform float segment_rings = 0.0;   // body segments to mark as faint grooves along UV.x (0 = none)
uniform float bloom = 0.5;      // waxy/dusty surface bloom: 1 = matte chalky cuticle, 0 = clean
uniform float wet = 0.0;        // aquatic animals: a thin wet film keeps a soft sheen

varying vec4 genes;
varying float region;
varying vec2 body_uv;
varying float seed;
varying float anim_role;
varying float motion_amount;

vec3 hue_rotate(vec3 c, float shift) {
    // rotate hue in YIQ space
    const mat3 toYIQ = mat3(vec3(0.299, 0.596, 0.211), vec3(0.587, -0.274, -0.523), vec3(0.114, -0.322, 0.312));
    const mat3 toRGB = mat3(vec3(1.0, 1.0, 1.0), vec3(0.956, -0.272, -1.106), vec3(0.621, -0.647, 1.703));
    vec3 yiq = toYIQ * c;
    float a = shift * 6.2831853;
    float cs = cos(a), sn = sin(a);
    yiq.yz = vec2(yiq.y * cs - yiq.z * sn, yiq.y * sn + yiq.z * cs);
    return clamp(toRGB * yiq, 0.0, 1.0);
}
float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453123); }
float vnoise(vec2 p) {
    vec2 i = floor(p), f = fract(p);
    vec2 u = f * f * (3.0 - 2.0 * f);
    return mix(mix(hash(i), hash(i + vec2(1, 0)), u.x), mix(hash(i + vec2(0, 1)), hash(i + vec2(1, 1)), u.x), u.y);
}

vec3 rotate_x(vec3 p, float a) {
    float c = cos(a), s = sin(a);
    return vec3(p.x, p.y * c - p.z * s, p.y * s + p.z * c);
}

vec3 rotate_y(vec3 p, float a) {
    float c = cos(a), s = sin(a);
    return vec3(p.x * c + p.z * s, p.y, -p.x * s + p.z * c);
}

// Foot trajectory with an authored stance fraction: sweep backward while planted, then recover forward
// through a lifted swing. This is the core contact-preserving primitive shared by walking families.
float gait_sweep(float phase01, float duty) {
    float p = fract(phase01);
    return p < duty
        ? mix(1.0, -1.0, p / max(duty, 0.001))
        : mix(-1.0, 1.0, (p - duty) / max(1.0 - duty, 0.001));
}

float gait_lift(float phase01, float duty) {
    float p = fract(phase01);
    if (p < duty) return 0.0;
    float swing = (p - duty) / max(1.0 - duty, 0.001);
    return sin(3.14159265359 * swing);
}

void vertex() {
    genes = INSTANCE_CUSTOM;
    float packed = genes.w;
    float cycle = fract(packed);
    float code = floor(packed);
    float speed01 = mod(code, 16.0) / 15.0;
    genes.w = floor(code / 16.0) / 1000.0;

    region = UV2.x;
    anim_role = UV2.y;
    body_uv = UV;
    seed = float(INSTANCE_ID);
    motion_amount = anim_amplitude * mix(anim_idle_motion, 1.0, speed01);

    float body_mask = 1.0 - COLOR.a;
    VERTEX.y *= 1.0 + (genes.y - 0.5) * 0.08 * body_mask;
    VERTEX.z *= 1.0 + (genes.z - 0.5) * 0.07 * body_mask;

    vec3 attach = VERTEX - COLOR.rgb;
    vec3 off = COLOR.rgb * genes.w;
    if (COLOR.a > 0.5) VERTEX = attach + off;

    const float TAU_ = 6.28318530718;
    const float PI_ = 3.14159265359;
    float theta = cycle * TAU_;
    float side = off.z < 0.0 ? -1.0 : 1.0;
    float reach = max(length(off), 0.001);
    float longitudinal = clamp(attach.x + 0.5, 0.0, 1.0);
    float pose = motion_amount;

    // 1: axial swimmer. Tail amplitude grows toward the rear while fins only make small stabilizing motions.
    if (anim_family == 1) {
        float along = clamp(0.58 - VERTEX.x, 0.0, 1.25);
        float wave = sin(theta - VERTEX.x * TAU_ * anim_phase_spread);
        VERTEX.z += wave * anim_body_wave * 0.065 * pose * along;
        if (COLOR.a > 0.5 && anim_role > 1.5) VERTEX.y += sin(theta + longitudinal * PI_) * anim_limb_lift * 0.02 * pose;
        VERTEX.y += sin(theta * 2.0) * anim_bob * 0.012 * pose;
    }
    // 2: paddler. Modest trunk wave, paired appendages sweep through the water continuously.
    else if (anim_family == 2) {
        VERTEX.z += sin(theta - VERTEX.x * TAU_ * anim_phase_spread) * anim_body_wave * 0.025 * pose;
        if (COLOR.a > 0.5 && anim_role > 0.5 && anim_role < 2.5) {
            float lp = theta + longitudinal * TAU_ * anim_phase_spread + (side > 0.0 ? PI_ : 0.0);
            off.x += sin(lp) * anim_limb_sweep * reach * 0.38 * pose;
            off.y += cos(lp) * anim_limb_lift * reach * 0.18 * pose;
            VERTEX = attach + off;
        }
    }
    // 3: ordinary walking/scuttling. Contralateral phase and a real swing/stance lift instead of universal wriggle.
    else if (anim_family == 3) {
        if (COLOR.a > 0.5 && anim_role > 0.5 && anim_role < 1.5) {
            float lp = cycle + longitudinal * anim_phase_spread + (side > 0.0 ? 0.5 : 0.0);
            float sweep = gait_sweep(lp, anim_duty_factor);
            float lift = gait_lift(lp, anim_duty_factor);
            off.x += sweep * anim_limb_sweep * reach * 0.42 * pose;
            off.y += lift * anim_limb_lift * reach * 0.32 * pose;
            VERTEX = attach + off;
        }
        VERTEX.z += sin(theta - VERTEX.x * TAU_) * anim_body_wave * 0.010 * pose * body_mask;
        VERTEX.y += abs(sin(theta * 2.0)) * anim_bob * 0.015 * pose;
    }
    // 4: many-legged metachronal walking. Longitudinal phase spread produces the visible leg wave.
    else if (anim_family == 4) {
        if (COLOR.a > 0.5 && anim_role > 0.5 && anim_role < 1.5) {
            float lp = cycle + longitudinal * anim_phase_spread + (side > 0.0 ? 0.30 : 0.0);
            off.x += gait_sweep(lp, anim_duty_factor) * anim_limb_sweep * reach * 0.30 * pose;
            off.y += gait_lift(lp, anim_duty_factor) * anim_limb_lift * reach * 0.22 * pose;
            VERTEX = attach + off;
        }
        VERTEX.z += sin(theta - VERTEX.x * TAU_) * anim_body_wave * 0.010 * pose;
    }
    // 5: slug/snail style muscular foot. No snake-like lateral motion; the body subtly advances a compression wave.
    else if (anim_family == 5) {
        float wave = sin(theta - (VERTEX.x + 0.5) * TAU_ * anim_phase_spread);
        VERTEX.y += wave * anim_body_wave * 0.008 * pose * body_mask;
        VERTEX.z *= 1.0 + wave * anim_body_wave * 0.015 * pose * body_mask;
    }
    // 6: annelid peristalsis: alternating longitudinal shortening and radial expansion.
    else if (anim_family == 6) {
        float wave = sin(theta - (VERTEX.x + 0.5) * TAU_ * anim_phase_spread);
        VERTEX.x += cos(theta - (VERTEX.x + 0.5) * TAU_ * anim_phase_spread) * anim_body_wave * 0.012 * pose;
        VERTEX.y *= 1.0 + wave * anim_body_wave * 0.075 * pose;
        VERTEX.z *= 1.0 + wave * anim_body_wave * 0.075 * pose;
    }
    // 7: saltatory/hopping gait. The stance portion crouches; the remainder describes a clean airborne arc.
    else if (anim_family == 7) {
        float airborne = step(anim_duty_factor, cycle);
        float air_t = clamp((cycle - anim_duty_factor) / max(1.0 - anim_duty_factor, 0.01), 0.0, 1.0);
        float hop = sin(PI_ * air_t) * airborne;
        float crouch_t = clamp(cycle / max(anim_duty_factor, 0.01), 0.0, 1.0);
        float crouch = sin(PI_ * crouch_t) * (1.0 - airborne);
        VERTEX.y += hop * anim_bob * 0.20 * pose;
        if (COLOR.a > 0.5 && anim_role > 0.5 && anim_role < 1.5) {
            float hind = 1.0 - smoothstep(-0.18, 0.18, attach.x);
            off.x -= hind * (crouch - hop * 0.35) * anim_limb_sweep * reach * 0.50 * pose;
            off.y += (hop - crouch * 0.35) * anim_limb_lift * reach * 0.30 * pose;
            VERTEX = attach + off + vec3(0.0, hop * anim_bob * 0.20 * pose, 0.0);
        }
    }
    // 8: sprawled tetrapod gait. Diagonal limb phasing is coupled to a small trunk/tail counter-wave.
    else if (anim_family == 8) {
        if (COLOR.a > 0.5 && anim_role > 0.5 && anim_role < 1.5) {
            float fore = smoothstep(-0.05, 0.22, attach.x);
            float diagonal = side > 0.0 ? 0.5 : 0.0;
            float lp = cycle + diagonal + fore * 0.5 + longitudinal * anim_phase_spread;
            off.x += gait_sweep(lp, anim_duty_factor) * anim_limb_sweep * reach * 0.46 * pose;
            off.y += gait_lift(lp, anim_duty_factor) * anim_limb_lift * reach * 0.28 * pose;
            VERTEX = attach + off;
        }
        VERTEX.z += sin(theta - VERTEX.x * TAU_ * 0.8) * anim_body_wave * 0.020 * pose * body_mask;
        VERTEX.y += abs(sin(theta * 2.0)) * anim_bob * 0.012 * pose;
    }
    // 9: flight. Only role-2 geometry hinges as a wing; legs and sensory structures are not forced to flap.
    else if (anim_family == 9) {
        if (COLOR.a > 0.5 && anim_role > 1.5 && anim_role < 2.5) {
            float flap = sin(theta);
            off = rotate_x(off, side * flap * anim_limb_sweep * 1.15 * pose);
            VERTEX = attach + off;
        }
        VERTEX.y += -cos(theta * 2.0) * anim_bob * 0.030 * pose;
    }

    // Sensory structures get a slow independent searching motion regardless of gait family.
    if (COLOR.a > 0.5 && anim_role > 2.5) {
        float search = sin(theta * 0.37 + seed * 0.73 + longitudinal * 1.9);
        off = COLOR.rgb * genes.w;
        off = rotate_y(off, search * anim_limb_sweep * 0.12 * max(pose, anim_idle_motion));
        off.y += cos(theta * 0.29 + seed) * anim_limb_lift * reach * 0.05 * max(pose, anim_idle_motion);
        VERTEX = attach + off;
    }
}

void fragment() {
    vec3 base = hue_rotate(base_color, genes.x);
    float density = genes.y, contrast = genes.z;
    // markings: spots whose count follows density; contrast follows pattern strength
    float scale = mix(3.0, 14.0, density);
    vec2 warped = body_uv * vec2(scale, scale * 0.62);
    warped += vec2(vnoise(body_uv * 4.1 + seed * 0.17), vnoise(body_uv.yx * 3.7 + seed * 0.31)) * 0.85;
    float spots = smoothstep(0.64, 0.78, vnoise(warped + seed * 0.13));
    float streaks = smoothstep(0.68, 0.82, vnoise(body_uv * vec2(mix(2.2, 5.2, density), 1.7) + seed * 0.47));
    float mark = clamp(max(spots, streaks * density * 0.65), 0.0, 1.0) * contrast;
    vec3 col = mix(base, hue_rotate(ornament_color, genes.x * 0.5), mark * step(region, 1.5));
    if (region > 1.5 && region < 2.5) col = vec3(0.03);                    // eyes
    else if (region > 2.5 && region < 3.5) col = mix(col, col * 1.4 + vec3(0.04), 0.35); // slightly lighter belly, same hue (pure white read as a toy)
    else if (region > 3.5) col = base * 1.15 + vec3(0.02);                // limbs & fins
    // fine surface grain (chitin pits / skin) so bodies don't read as smooth plastic
    float micro = vnoise(body_uv * vec2(90.0, 60.0) + seed * 0.37);
    col *= 0.92 + 0.16 * micro;
    if (segment_rings > 0.5 && region < 1.5) {
        float sw = (vnoise(body_uv * vec2(3.0, 1.4) + seed * 0.21) - 0.5) * 0.18;
        col *= 1.0 - 0.10 * smoothstep(0.43, 0.5, abs(fract((body_uv.x + sw) * segment_rings) - 0.5));
    }
    float fres = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
    if (scales > 0.5 && region < 3.5 && !(region > 1.5 && region < 2.5)) {
        vec2 sc = body_uv * vec2(27.0, 18.0);
        sc += vec2(vnoise(body_uv * 5.0 + seed * 0.23), vnoise(body_uv.yx * 4.0 + seed * 0.41)) * 0.42;
        sc.x += floor(sc.y) * 0.5;
        float edge = smoothstep(0.34, 0.5, length(fract(sc) - 0.5));
        col *= 1.0 - 0.09 * edge;
        col = mix(col, vec3(0.85, 0.9, 0.95), fres * 0.35 + (region > 2.5 ? 0.35 : 0.0));   // silvery flank and belly
    }
    // Real cuticle is mostly matte: waxy bloom, dust and soil grime break up whatever gloss there is, and it
    // varies over the body (worn shiny on the back, dull in the joints and underside). A uniform low roughness
    // everywhere is what made every critter read as moulded plastic.
    bool eye = region > 1.5 && region < 2.5;
    float grime = vnoise(body_uv * vec2(9.0, 6.0) + seed * 0.71);
    float dullPatch = smoothstep(0.35, 0.75, grime) * bloom;
    float wear = smoothstep(0.55, 0.9, vnoise(body_uv * vec2(4.0, 3.0) + seed * 1.9)) * step(region, 1.5);   // polished high spots
    col = mix(col, col * 0.85 + vec3(0.05, 0.045, 0.035), dullPatch * 0.5);   // dusty, slightly desaturated
    float rough = mix(0.62, 0.42, carapace) + 0.18 * micro + 0.25 * dullPatch - 0.15 * wear * carapace;
    rough = mix(rough, 0.3, wet * 0.6);
    if (region > 2.5) rough += 0.1;          // bellies, legs and joints are duller
    ALBEDO = col;
    ROUGHNESS = eye ? 0.08 : clamp(rough, 0.25, 0.95);
    SPECULAR = eye ? 0.5 : mix(0.3, 0.45, max(carapace, wet));
    CLEARCOAT = eye ? 0.0 : carapace * (1.0 - dullPatch) * 0.6;
    CLEARCOAT_ROUGHNESS = mix(0.35, 0.7, micro);
    RIM = mix(0.08, 0.2, wet);
    RIM_TINT = 0.6;
    BACKLIGHT = vec3(translucency) * col;
}
