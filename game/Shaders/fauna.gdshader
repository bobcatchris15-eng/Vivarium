// Fauna instances. Species colours are material uniforms; per-individual genetics arrive in
// INSTANCE_CUSTOM = (hue shift, marking density, pattern contrast, packed appendage scale + speed + phase).
// The CPU accumulates phase from species-authored body-relative cadence. Vertex CUSTOM0.rgb is the signed
// appendage offset from its attachment and CUSTOM0.a flags appendages. UV2.x is the visual region; UV2.y is animation role:
// 0 body, 1 locomotor limb, 2 wing/fin, 3 sensory appendage.
shader_type spatial;
render_mode diffuse_burley, specular_schlick_ggx, cull_disabled;

uniform vec3 base_color : source_color = vec3(0.9, 0.3, 0.2);
uniform vec3 ornament_color : source_color = vec3(1.0);
uniform int anim_family = 0;
uniform int anim_body_style = 0; // 1 salamander, 2 toad, 3 harvestman, 4 slug, 5 worm
uniform float anim_half_stroke = 0.1;
uniform float toad_crawl_stroke = 0.15909;
uniform float toad_crawl_duty = 0.7;
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
uniform float preview_time = -1.0; // deterministic respiration in capture tools; live uses TIME
uniform float wet = 0.0;        // aquatic animals: a thin wet film keeps a soft sheen

varying vec4 genes;
varying float region;
varying vec2 body_uv;
varying float seed;
varying float anim_role;
varying float motion_amount;
varying vec3 skin_position;

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

vec3 rotate_z(vec3 p, float a) {
    float c = cos(a), s = sin(a);
    return vec3(p.x * c - p.y * s, p.x * s + p.y * c, p.z);
}

float spine_wave(float x, float theta, float pose) {
    if (anim_family == 4) return sin(theta - x * 6.2831853 * 0.85) * anim_body_wave * 0.085 * pose;
    if (anim_body_style == 1) {
        float tail = 1.0 - smoothstep(-0.65, -0.20, x);
        float trunk = 1.0 - smoothstep(0.18, 0.40, x);
        return sin(theta - x * 6.2831853 * 0.65) * anim_body_wave * 0.036 * pose * (trunk + tail * 2.4);
    }
    if (anim_body_style == 7) return sin(theta - x * 4.0) * anim_body_wave * 0.045 * pose;
    if (anim_body_style == 8) return sin(theta - x * 5.0) * anim_body_wave * 0.018 * pose;
    if (anim_body_style == 11) {
        float tail = clamp(0.42 - x, 0.0, 1.2);
        return sin(theta - x * 5.0) * anim_body_wave * 0.11 * pose * tail * tail;
    }
    if (anim_body_style == 9 || anim_body_style == 10 || anim_body_style == 16) {
        float tail = 1.0 - smoothstep(-0.4, 0.15, x);
        return sin(theta - x * 5.0) * anim_body_wave * 0.065 * pose * tail;
    }
    return 0.0;
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
    bool toad_crawl = anim_body_style == 2 && genes.x > 2.0;
    if (toad_crawl) genes.x -= 4.0;
    int locomotion = toad_crawl ? 8 : anim_family;
    float turn_bend = 0.0;
    bool bends = anim_family == 4 || anim_body_style == 1 || anim_body_style == 4 || anim_body_style == 5
        || anim_body_style == 7 || anim_body_style == 8 || anim_body_style == 9 || anim_body_style == 10
        || anim_body_style == 11 || anim_body_style == 12 || anim_body_style == 16;
    if (bends && genes.x > 0.5) {
        float bend_code = floor(genes.x * 0.5 + 0.25);
        genes.x -= bend_code * 2.0;
        turn_bend = ((bend_code - 1.0) / 15.0 - 1.0) * 1.25;
    }
    turn_bend *= anim_amplitude;
    if (anim_body_style == 8 || anim_body_style == 10 || anim_body_style == 12) turn_bend *= 0.45;
    float packed = genes.w;
    float cycle = fract(packed);
    float code = floor(packed);
    float speed01 = mod(code, 16.0) / 15.0;
    genes.w = floor(code / 16.0) / 1000.0;

    region = UV2.x;
    anim_role = floor(UV2.y + 0.001);
    float foot_weight = clamp((UV2.y - 1.0) * 4.0, 0.0, 1.0);
    body_uv = UV;
    seed = float(INSTANCE_ID);
    // Genetic identity is stable when MultiMesh slots are reordered by visibility/death.
    if (anim_body_style == 2) seed = hash(genes.xy * 213.7 + genes.z * 91.3) * 97.0;
    // y/z have no packed steering bits: pattern identity survives every turn bucket.
    if (anim_body_style == 1 || anim_body_style == 3 || anim_body_style == 4 || anim_body_style == 5
        || anim_body_style == 10 || anim_body_style == 12 || anim_body_style == 13 || anim_body_style == 16
        || anim_family == 4) seed = hash(genes.yz * 289.0) * 97.0;
    motion_amount = anim_amplitude * mix(anim_idle_motion, 1.0, speed01);
    // Distance-clock walkers retain their last support pose when stopping. Reducing
    // the wave amplitude at zero speed would pull planted toes sideways at rest.
    if (locomotion == 3 || locomotion == 4 || locomotion == 8 || anim_body_style == 5) motion_amount = anim_amplitude;

    float body_mask = 1.0 - CUSTOM0.a;
    VERTEX.y *= 1.0 + (genes.y - 0.5) * 0.08 * body_mask;
    VERTEX.z *= 1.0 + (genes.z - 0.5) * 0.07 * body_mask;

    skin_position = VERTEX;
    vec3 attach = VERTEX - CUSTOM0.rgb;
    vec3 off = CUSTOM0.rgb * genes.w;
    if (CUSTOM0.a > 0.5) VERTEX = attach + off;

    const float TAU_ = 6.28318530718;
    const float PI_ = 3.14159265359;
    float theta = cycle * TAU_;
    float side = off.z < 0.0 ? -1.0 : 1.0;
    float reach = max(length(off), 0.001);
    float longitudinal = clamp(attach.x + 0.5, 0.0, 1.0);
    float pose = motion_amount;
    float stride = (toad_crawl ? toad_crawl_stroke : anim_half_stroke) * step(0.001, anim_amplitude);
    float hop_height = 0.0;
    float hop_pitch = 0.0;
    float hop_squash = 1.0;
    float hop_width = 1.0;
    float flight_pitch = 0.0;
    float flight_bob = 0.0;

    // 1: axial swimmer. Tail amplitude grows toward the rear while fins only make small stabilizing motions.
    if (locomotion == 1) {
        float along = clamp(0.58 - VERTEX.x, 0.0, 1.25);
        float wave = sin(theta - VERTEX.x * TAU_ * anim_phase_spread);
        if (anim_body_style != 11) VERTEX.z += wave * anim_body_wave * 0.065 * pose * along;
        if (CUSTOM0.a > 0.5 && anim_role > 1.5 && anim_role < 2.5) {
            float fin_angle = sin(theta + longitudinal * PI_) * anim_limb_lift * 0.25 * pose;
            off = rotate_x(off, side * fin_angle);
            NORMAL = rotate_x(NORMAL, side * fin_angle);
            VERTEX = attach + off;
        }
        VERTEX.y += sin(theta * 2.0) * anim_bob * 0.012 * pose;
    }
    // 2: paddler. Modest trunk wave, paired appendages sweep through the water continuously.
    else if (locomotion == 2) {
        if (anim_body_style == 0) VERTEX.z += sin(theta - VERTEX.x * TAU_ * anim_phase_spread) * anim_body_wave * 0.025 * pose;
        if (CUSTOM0.a > 0.5 && anim_role > 0.5 && anim_role < 2.5) {
            float lp = theta + longitudinal * TAU_ * anim_phase_spread + (side > 0.0 ? PI_ : 0.0);
            off.x += sin(lp) * anim_limb_sweep * reach * 0.38 * pose;
            off.y += cos(lp) * anim_limb_lift * reach * 0.18 * pose;
            VERTEX = attach + off;
        }
    }
    // 3: ordinary walking/scuttling. Contralateral phase and a real swing/stance lift instead of universal wriggle.
    else if (locomotion == 3) {
        if (CUSTOM0.a > 0.5 && anim_role > 0.5 && anim_role < 1.5) {
            float lp = cycle + longitudinal * anim_phase_spread + (side > 0.0 ? 0.5 : 0.0);
            if (anim_body_style == 6 || anim_body_style == 7) {
                float pair = floor(clamp((0.26 - attach.x) / (anim_body_style == 6 ? 0.16 : 0.08), 0.0, 2.0) + 0.25);
                lp = cycle + mod(pair, 2.0) * 0.5 + (side > 0.0 ? 0.5 : 0.0);
            } else if (anim_body_style == 3) {
                float pair = floor(clamp((0.14 - attach.x) / 0.085, 0.0, 3.0) + 0.25);
                lp = cycle + mod(pair, 2.0) * 0.5 + (side > 0.0 ? 0.5 : 0.0);
            }
            float sweep = gait_sweep(lp, anim_duty_factor);
            float lift = gait_lift(lp, anim_duty_factor);
            off.x += sweep * stride * foot_weight;
            off.y += lift * anim_limb_lift * reach * 0.32 * pose;
            VERTEX = attach + off;
        }
        if (anim_body_style == 0) VERTEX.z += sin(theta - VERTEX.x * TAU_) * anim_body_wave * 0.010 * pose * body_mask;
        VERTEX.y += abs(sin(theta * 2.0)) * anim_bob * 0.015 * pose * (1.0 - CUSTOM0.a * foot_weight);
    }
    // 4: many-legged metachronal walking. Longitudinal phase spread produces the visible leg wave.
    else if (locomotion == 4) {
        if (CUSTOM0.a > 0.5 && anim_role > 0.5 && anim_role < 1.5) {
            float lp = cycle + longitudinal * anim_phase_spread + (side > 0.0 ? 0.30 : 0.0);
            off.x += gait_sweep(lp, anim_duty_factor) * stride * foot_weight;
            off.y += gait_lift(lp, anim_duty_factor) * anim_limb_lift * reach * 0.50 * pose;
            VERTEX = attach + off;
        }
    }
    // 5: slug/snail style muscular foot. No snake-like lateral motion; the body subtly advances a compression wave.
    else if (locomotion == 5) {
        float wave = sin(theta - (VERTEX.x + 0.5) * TAU_ * anim_phase_spread);
        if (anim_body_style == 4 || anim_body_style == 12) {
            // Applied after sensory articulation so the crowns follow their moving roots.
        } else {
            VERTEX.y += wave * anim_body_wave * 0.008 * pose * body_mask;
            VERTEX.z *= 1.0 + wave * anim_body_wave * 0.015 * pose * body_mask;
        }
    }
    // 6: annelid peristalsis: alternating longitudinal shortening and radial expansion.
    else if (locomotion == 6) {
        float local_phase = cycle - (VERTEX.x + 0.5) * anim_phase_spread;
        float wave = sin(local_phase * TAU_);
        float axial = 1.0;
        if (anim_body_style == 5) {
            // Supporting rings move backward by precisely the travelled distance;
            // recovering rings bunch forward into a travelling compression wave.
            VERTEX.x += gait_sweep(local_phase, anim_duty_factor) * anim_half_stroke * anim_amplitude;
            axial += (fract(local_phase) < anim_duty_factor ? 2.0 / anim_duty_factor
                : -2.0 / (1.0 - anim_duty_factor)) * anim_half_stroke * anim_phase_spread * anim_amplitude;
        } else VERTEX.x += cos(local_phase * TAU_) * anim_body_wave * 0.012 * pose;
        float radial = 1.0 + wave * anim_body_wave * (anim_body_style == 5 ? 0.12 : 0.075) * pose;
        VERTEX.y *= radial;
        VERTEX.z *= radial;
        NORMAL = normalize(NORMAL / vec3(max(axial, 0.10), radial, radial));
    }
    // 7: saltatory/hopping gait. The stance portion crouches; the remainder describes a clean airborne arc.
    else if (locomotion == 7) {
        float airborne = step(anim_duty_factor, cycle);
        float air_t = clamp((cycle - anim_duty_factor) / max(1.0 - anim_duty_factor, 0.01), 0.0, 1.0);
        float hop = sin(PI_ * air_t) * airborne;
        float crouch_t = clamp(cycle / max(anim_duty_factor, 0.01), 0.0, 1.0);
        float crouch = sin(PI_ * crouch_t) * (1.0 - airborne);
        if (anim_body_style == 2 || anim_body_style == 15) {
            // Load the hindquarters, extend at launch, then tuck and reach into landing.
            crouch = (smoothstep(0.12, 0.95, crouch_t) + 0.35 * (1.0 - smoothstep(0.0, 0.18, crouch_t))) * (1.0 - airborne)
                + ((1.0 - smoothstep(0.0, 0.18, air_t)) + 0.35 * smoothstep(0.8, 1.0, air_t)) * airborne;
            if (CUSTOM0.a > 0.5 && anim_role > 0.5 && anim_role < 1.5) {
                float hind = 1.0 - smoothstep(-0.18, 0.18, attach.x);
                float tuck = hop * hind;
                float flex = clamp(reach / 0.32, 0.0, 1.0);
                off.x += (-crouch * hind * 0.12 - tuck * 0.10 + hop * (1.0 - hind) * 0.06) * pose * flex;
                off.y += tuck * 0.09 * pose * flex;
                off.z *= 1.0 - crouch * hind * 0.12 * pose;
                if (anim_body_style == 2) {
                    // Extend the folded hind limb at take-off; gather it under the body
                    // at the apex, then spread the toes and reach the forelegs to land.
                    float launch = smoothstep(0.0, 0.09, air_t) * (1.0 - smoothstep(0.12, 0.40, air_t)) * airborne;
                    float landing = smoothstep(0.55, 0.98, air_t) * airborne;
                    off = CUSTOM0.rgb * genes.w;
                    float hinge = hind * (-0.20 * crouch + 0.65 * launch - 0.32 * hop)
                        + (1.0 - hind) * (0.28 * hop - 0.24 * landing);
                    off = rotate_z(off, hinge * pose);
                    NORMAL = rotate_z(NORMAL, hinge * pose);
                    off.x += (-0.10 * launch * hind + 0.055 * landing * (1.0 - hind)) * foot_weight * pose;
                    off.y += (0.09 * hop * hind + 0.03 * hop * (1.0 - hind)) * foot_weight * pose;
                    off.z *= 1.0 - 0.16 * hop * hind * pose;
                }
                VERTEX = attach + off;
            }
            hop_squash = 1.0 - crouch * 0.18 * pose;
            hop_width = 1.0 + crouch * 0.07 * pose;
            hop_pitch = sin(air_t * TAU_) * airborne * (anim_body_style == 15 ? 0.28 : 0.12) * pose;
            hop_height = hop * anim_bob * (anim_body_style == 15 ? 0.55 : 0.38) * pose;
            if (CUSTOM0.a > 0.5 && anim_role > 4.5) {
                float release = smoothstep(0.0, 0.12, air_t) * (1.0 - smoothstep(0.35, 0.85, air_t)) * airborne;
                VERTEX = attach + rotate_z(off, -release * 1.25 * pose);
                NORMAL = rotate_z(NORMAL, -release * 1.25 * pose);
            }
        } else {
            VERTEX.y += hop * anim_bob * 0.20 * pose;
            if (CUSTOM0.a > 0.5 && anim_role > 0.5 && anim_role < 1.5) {
                float hind = 1.0 - smoothstep(-0.18, 0.18, attach.x);
                off.x -= hind * (crouch - hop * 0.35) * anim_limb_sweep * reach * 0.50 * pose;
                off.y += (hop - crouch * 0.35) * anim_limb_lift * reach * 0.30 * pose;
                VERTEX = attach + off + vec3(0.0, hop * anim_bob * 0.20 * pose, 0.0);
            }
        }
    }
    // 8: sprawled tetrapod gait. Diagonal limb phasing is coupled to a small trunk/tail counter-wave.
    else if (locomotion == 8) {
        if (CUSTOM0.a > 0.5 && anim_role > 0.5 && anim_role < 1.5) {
            float fore = (toad_crawl || anim_body_style == 1) ? step(0.0, attach.x) : smoothstep(-0.05, 0.22, attach.x);
            float diagonal = side > 0.0 ? 0.5 : 0.0;
            float lp = cycle + diagonal + fore * 0.5 + longitudinal * anim_phase_spread;
            if (anim_body_style == 1 || anim_body_style == 2) lp = cycle + diagonal + fore * 0.5;
            float duty = toad_crawl ? toad_crawl_duty : anim_duty_factor;
            off.x += gait_sweep(lp, duty) * stride * foot_weight;
            off.y += gait_lift(lp, duty) * anim_limb_lift * reach * (toad_crawl ? 0.12 : 0.28) * pose;
            VERTEX = attach + off;
        }
        if (anim_body_style != 1 && anim_body_style != 2) VERTEX.z += sin(theta - VERTEX.x * TAU_ * 0.8) * anim_body_wave * 0.020 * pose * body_mask;
        float support = anim_body_style == 1 ? 1.0 - CUSTOM0.a * foot_weight : 1.0;
        VERTEX.y += abs(sin(theta * 2.0)) * anim_bob * 0.012 * pose * support;
    }
    // 9: flight. Only role-2 geometry hinges as a wing; legs and sensory structures are not forced to flap.
    else if (locomotion == 9) {
        if (CUSTOM0.a > 0.5 && anim_role > 1.5 && anim_role < 2.5) {
            float flap = sin(theta);
            // Hindwings lag slightly, without the pair separating at their hinges.
            if (anim_body_style == 13 && region > 7.5) flap = sin(theta - 0.14);
            off = rotate_x(off, side * flap * anim_limb_sweep * 1.15 * pose);
            NORMAL = rotate_x(NORMAL, side * flap * anim_limb_sweep * 1.15 * pose);
            VERTEX = attach + off;
        }
        flight_bob = -cos(theta * 2.0) * anim_bob * 0.030 * pose;
        flight_pitch = -speed01 * (anim_body_style == 14 ? 0.18 : 0.10);
    }

    // Sensory structures get a slow independent searching motion regardless of gait family.
    if (CUSTOM0.a > 0.5 && anim_role > 2.5 && anim_role < 3.5) {
        float search = sin(theta * 0.37 + seed * 0.73 + longitudinal * 1.9);
        off = CUSTOM0.rgb * genes.w;
        off = rotate_y(off, search * anim_limb_sweep * 0.12 * max(pose, anim_idle_motion));
        off.y += cos(theta * 0.29 + seed) * anim_limb_lift * reach * 0.05 * max(pose, anim_idle_motion);
        VERTEX = attach + off;
    }

    if (anim_body_style == 1) {
        float rest_time = preview_time >= 0.0 ? preview_time : TIME;
        float breath = sin(rest_time * 2.3 + seed);
        float trunk = (1.0 - smoothstep(0.16, 0.32, VERTEX.x)) * smoothstep(-0.32, -0.20, VERTEX.x) * body_mask;
        VERTEX.y += breath * 0.0015 * trunk * (1.0 - speed01) * anim_amplitude;
    }
    if (anim_body_style == 2) {
        float rest_time = preview_time >= 0.0 ? preview_time : TIME;
        float breath = sin(rest_time * 2.7 + seed * 1.3);
        float resting = (1.0 - speed01) * anim_amplitude;
        float trunk = (1.0 - smoothstep(0.08, 0.28, VERTEX.x)) * body_mask;
        VERTEX.y += breath * 0.0025 * trunk * resting;
        VERTEX.z *= 1.0 + breath * 0.012 * trunk * resting;
        if (region > 7.5 && region < 8.5) {
            float pulse = sin(rest_time * 4.1 + seed);
            VERTEX.y -= (0.5 + 0.5 * pulse) * 0.007 * resting;
            VERTEX.z *= 1.0 + (0.5 + 0.5 * pulse) * 0.06 * resting;
        }
    }

    // Apply whole-animal poses after sensory articulation so antennae cannot be left behind in a jump/flight.
    if (locomotion == 7 && (anim_body_style == 2 || anim_body_style == 15)) {
        VERTEX.y *= hop_squash;
        VERTEX.z *= hop_width;
        NORMAL = normalize(NORMAL / vec3(1.0, hop_squash, hop_width));
        VERTEX = rotate_z(VERTEX - vec3(0.0, 0.15, 0.0), hop_pitch) + vec3(0.0, 0.15 + hop_height, 0.0);
        NORMAL = rotate_z(NORMAL, hop_pitch);
    }
    if (anim_family == 9) {
        VERTEX.y += flight_bob;
        VERTEX = rotate_z(VERTEX - vec3(0.0, 0.12, 0.0), flight_pitch) + vec3(0.0, 0.12, 0.0);
        NORMAL = rotate_z(NORMAL, flight_pitch);
    }

    if (anim_body_style == 4 || (anim_body_style == 12 && anim_role < 3.5)) {
        // Compression travels through the muscular foot; scale about ground level to hold the sole down.
        float x = CUSTOM0.a > 0.5 ? attach.x : VERTEX.x;
        float p = theta - (x + 0.5) * TAU_ * anim_phase_spread;
        float radial = 1.0 + sin(p) * anim_body_wave * 0.12 * pose;
        VERTEX.x += cos(p) * anim_body_wave * 0.025 * pose;
        VERTEX.y *= radial;
        VERTEX.z *= radial;
        NORMAL = normalize(NORMAL / vec3(1.0, radial, radial));
    }

    if ((anim_body_style == 9 || anim_body_style == 10 || anim_body_style == 16) && anim_role < 3.5) {
        float x = CUSTOM0.a > 0.5 ? attach.x : VERTEX.x;
        float tail = 1.0 - smoothstep(-0.45, 0.10, x);
        float curl = sin(theta - x * 4.0) * anim_body_wave * 0.035 * pose * tail;
        VERTEX.y += curl;
        NORMAL = rotate_z(NORMAL, -cos(theta - x * 4.0) * anim_body_wave * 0.10 * pose * tail);
    }

    // Bend the complete animal after appendage articulation. Arc length is preserved along
    // the spine, and the local cross-section/normal rotates with it, keeping feet attached.
    // The front leads the turn; the rear retains the previous heading rather than pivoting.
    if (bends && !((anim_body_style == 12 || anim_body_style == 10) && anim_role > 3.5)) {
        float x = CUSTOM0.a > 0.5 ? attach.x : VERTEX.x;
        float z = VERTEX.z;
        float wave = spine_wave(x, theta, pose);
        // A supporting millipede toe retains its touchdown wave position, while the
        // torso and upper leg continue their metachronal motion around it.
        if (anim_family == 4 && CUSTOM0.a > 0.5 && anim_role > 0.5 && anim_role < 1.5) {
            float leg_phase = fract(cycle + longitudinal * anim_phase_spread + (side > 0.0 ? 0.30 : 0.0));
            if (leg_phase < anim_duty_factor)
                wave = mix(wave, spine_wave(x, theta - leg_phase * TAU_, pose), foot_weight);
        }
        if (anim_body_style == 1 && CUSTOM0.a > 0.5 && anim_role > 0.5 && anim_role < 1.5) {
            float leg_phase = fract(cycle + (off.z > 0.0 ? 0.5 : 0.0) + step(0.0, attach.x) * 0.5);
            if (leg_phase < anim_duty_factor) {
                // While supporting the trunk, the toe keeps the lateral position it had
                // at touchdown; the body wave bends the upper limb around that planted foot.
                float planted_wave = spine_wave(x, theta - leg_phase * TAU_, pose);
                wave = mix(wave, planted_wave, foot_weight);
            }
        }
        float slope = (spine_wave(x + 0.001, theta, pose) - spine_wave(x - 0.001, theta, pose)) / 0.002;
        if ((anim_body_style == 1 || anim_family == 4) && CUSTOM0.a > 0.5 && anim_role > 0.5 && anim_role < 1.5)
            slope *= 1.0 - foot_weight; // planted toes do not roll with the trunk's axial wave
        float a = turn_bend * (x - 0.5);
        float spine_x = x;
        float spine_z = 0.0;
        if (abs(turn_bend) > 0.001) {
            spine_x = 0.5 + sin(a) / turn_bend;
            spine_z = (cos(a) - 1.0) / turn_bend;
        }
        float tangent = a - atan(slope);
        vec3 cross_section = rotate_y(vec3(VERTEX.x - x, VERTEX.y, z), tangent);
        VERTEX = vec3(spine_x, 0.0, spine_z + wave) + cross_section;
        NORMAL = rotate_y(NORMAL, tangent);
    }
    if (anim_body_style == 3 || anim_body_style == 6) {
        // The rigid body rides between support groups. Leave planted toe tips down.
        float roll = sin(theta) * (anim_body_style == 6 ? 0.035 : 0.065) * pose;
        float weight = smoothstep(0.0, 0.11, VERTEX.y) * (1.0 - CUSTOM0.a * foot_weight);
        vec3 rocked = rotate_x(VERTEX - vec3(0.0, 0.10, 0.0), roll) + vec3(0.0, 0.10, 0.0);
        rocked.y += abs(sin(theta * 2.0)) * anim_bob * 0.12 * pose;
        VERTEX = mix(VERTEX, rocked, weight);
        NORMAL = rotate_x(NORMAL, roll * weight);
    }
    if (anim_body_style == 12 && anim_role > 3.5) {
        // Shell rides as a rigid mass above the rippling foot, without squash/stretch.
        VERTEX.y += sin(theta) * anim_bob * 0.12 * pose;
    }
}

// Object-space noise follows amphibian skin across separately authored head/limb UV islands.
float skin_noise(vec3 p) {
    return (vnoise(p.xy) + vnoise(p.yz + 17.3) + vnoise(p.zx + 31.7)) / 3.0;
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
    if (anim_body_style == 2) {
        vec3 p = skin_position;
        float identity = hash(genes.xy * 39.1 + genes.z * 117.3);
        vec3 olive = vec3(0.16, 0.19, 0.085);
        vec3 umber = vec3(0.23, 0.15, 0.075);
        vec3 stone = vec3(0.21, 0.205, 0.16);
        vec3 skin = mix(olive, umber, smoothstep(0.05, 0.65, identity));
        skin = mix(skin, stone, smoothstep(0.65, 1.0, identity));
        skin *= mix(0.78, 1.25, clamp(0.5 + genes.x * 3.0, 0.0, 1.0));
        vec3 offset = vec3(seed * 0.73, seed * 0.39, seed * 0.57);
        float blotches = skin_noise(p * mix(18.0, 32.0, density) + offset);
        float mottling = skin_noise(p * 72.0 + offset);
        float pores = skin_noise(p * 230.0 + offset);
        float patch = smoothstep(0.48, 0.63, blotches);
        float rim = smoothstep(0.40, 0.49, blotches) - patch;
        col = skin * mix(0.91, 1.16, mottling);
        col = mix(col, skin * 0.32, patch * mix(0.4, 0.92, contrast));
        col += rim * vec3(0.040, 0.032, 0.015);
        // Faint broken dorsal stripe; only some individuals carry it.
        float stripe = (1.0 - smoothstep(0.007, 0.020, abs(p.z)))
            * smoothstep(0.25, 0.33, p.y) * smoothstep(0.58, 0.85, identity);
        col = mix(col, vec3(0.36, 0.30, 0.15), stripe * 0.45);
        float ventral = 1.0 - smoothstep(0.065, 0.15, p.y);
        if (region > 7.5) ventral = 1.0;
        col = mix(col, vec3(0.29, 0.25, 0.18) * mix(0.8, 1.12, mottling), ventral * 0.88);
        rough = mix(0.68, 0.87, pores) - patch * 0.045;
        eye = (region > 1.5 && region < 2.5) || (region > 4.5 && region < 6.5);
        if (eye) {
            col = region < 2.5 ? vec3(0.13, 0.09, 0.025) : vec3(0.008, 0.010, 0.005);
            if (region > 4.5 && region < 5.5) {
                float fibers = vnoise(body_uv * vec2(100.0, 8.0) + seed);
                col = mix(vec3(0.16, 0.095, 0.026), vec3(0.49, 0.33, 0.09), fibers);
            }
        } else if (region > 6.5 && region < 7.5) {
            col = vec3(0.045, 0.030, 0.017);
            rough = 0.76;
        } else {
            // Screen derivatives turn attached object-space relief into a view-space
            // surface gradient, without UV seams or texture tangents on procedural tubes.
            float relief = mottling * 0.00085 + pores * 0.00025;
            vec3 dx = dFdx(VERTEX), dy = dFdy(VERTEX);
            vec3 rx = cross(dy, NORMAL), ry = cross(NORMAL, dx);
            float det = dot(dx, rx);
            vec3 gradient = (rx * dFdx(relief) + ry * dFdy(relief))
                * sign(det) / max(abs(det), 0.0000001);
            NORMAL = normalize(NORMAL - gradient * (1.0 - ventral * 0.65));
        }
    }
    if (anim_body_style == 1) {
        vec3 p = skin_position;
        float identity = hash(genes.yz * 47.9 + vec2(8.3, 21.7));
        float palette = floor(identity * 8.0);
        vec3 skin = vec3(0.014, 0.021, 0.018), marking = vec3(0.88, 0.49, 0.008);
        if (palette == 1.0) { skin = vec3(0.022, 0.042, 0.085); marking = vec3(0.10, 0.59, 0.77); }
        else if (palette == 2.0) { skin = vec3(0.49, 0.055, 0.027); marking = vec3(0.035, 0.020, 0.016); }
        else if (palette == 3.0) { skin = vec3(0.025, 0.018, 0.030); marking = vec3(0.81, 0.12, 0.020); }
        else if (palette == 4.0) { skin = vec3(0.055, 0.16, 0.11); marking = vec3(0.45, 0.63, 0.21); }
        else if (palette == 5.0) { skin = vec3(0.12, 0.034, 0.20); marking = vec3(0.47, 0.20, 0.57); }
        else if (palette == 6.0) { skin = vec3(0.48, 0.41, 0.25); marking = vec3(0.055, 0.036, 0.020); }
        else if (palette == 7.0) { skin = vec3(0.016, 0.017, 0.020); marking = vec3(0.65, 0.66, 0.52); }
        skin = hue_rotate(skin, genes.x * 0.6);
        marking = hue_rotate(marking, genes.x * 0.3);
        vec3 offset = vec3(seed * 0.17, seed * 0.41, seed * 0.63);
        float coarse = skin_noise(p * 17.0 + offset);
        float grain = skin_noise(p * 130.0 + offset);
        float pores = skin_noise(p * 300.0 + offset);
        float warp = skin_noise(p * 8.0 + offset) - 0.5;
        float pattern = floor(hash(genes.zy * 99.7 + 3.4) * 5.0);
        float mask = 0.0;
        if (pattern == 0.0) mask = smoothstep(0.54, 0.64, skin_noise(p * mix(28.0, 65.0, density) + offset));
        else if (pattern == 1.0) {
            float stripe = abs(p.z + warp * 0.017);
            mask = 1.0 - smoothstep(0.023, 0.043, stripe);
        } else if (pattern == 2.0) {
            float bands = abs(sin((p.x + warp * 0.10) * 42.0 + seed));
            mask = smoothstep(0.62, 0.82, bands);
        } else if (pattern == 3.0) mask = smoothstep(0.43, 0.56, coarse);
        else mask = smoothstep(0.63, 0.72, skin_noise(p * 75.0 + offset));
        float tail_level = clamp((-0.22 - p.x) / 0.55, 0.0, 1.0) * 0.065;
        float dorsal = smoothstep(0.040 - tail_level, 0.085 - tail_level, p.y);
        col = mix(skin, marking, mask * mix(0.6, 1.0, contrast) * dorsal);
        col *= mix(0.94, 1.05, grain);
        vec3 belly = mix(skin * 0.65 + vec3(0.07, 0.055, 0.032), marking * 0.38, 0.2);
        col = mix(belly, col, dorsal);
        // Restrained costal folds along the trunk; smooth head and tail remain uninterrupted.
        float folds = pow(abs(sin(p.x * 140.0)), 14.0) * smoothstep(0.047, 0.086, abs(p.z))
            * smoothstep(-0.28, -0.20, p.x) * (1.0 - smoothstep(0.15, 0.23, p.x));
        col *= 1.0 - folds * 0.09;
        rough = mix(0.30, 0.46, grain);
        eye = (region > 1.5 && region < 2.5) || (region > 4.5 && region < 6.5);
        if (eye) {
            col = vec3(0.007, 0.011, 0.010);
            if (region > 4.5 && region < 5.5) col = vec3(0.15, 0.10, 0.035) * mix(0.8, 1.2, grain);
        } else if (region > 6.5 && region < 7.5) {
            col = mix(skin, vec3(0.015, 0.012, 0.01), 0.85);
            rough = 0.48;
        } else {
            float relief = grain * 0.00020 + pores * 0.000075 - folds * 0.00030;
            vec3 dx = dFdx(VERTEX), dy = dFdy(VERTEX);
            vec3 rx = cross(dy, NORMAL), ry = cross(NORMAL, dx);
            float det = dot(dx, rx);
            vec3 gradient = (rx * dFdx(relief) + ry * dFdy(relief)) * sign(det) / max(abs(det), 0.0000001);
            NORMAL = normalize(NORMAL - gradient);
        }
    }
    bool detailed = anim_body_style == 3 || anim_body_style == 4 || anim_body_style == 5
        || anim_body_style == 10 || anim_body_style == 12 || anim_body_style == 13
        || anim_body_style == 16 || (anim_family == 4 && anim_body_style == 0);
    if (detailed) {
        vec3 p = skin_position;
        vec3 offset = vec3(seed * 0.17, seed * 0.41, seed * 0.63);
        float identity = hash(genes.yz * 47.9 + vec2(8.3, 21.7));
        float choice = floor(identity * 6.0);
        float variant = floor(hash(genes.zy * 99.7 + 3.4) * 4.0);
        float coarse = skin_noise(p * 19.0 + offset);
        float grain = skin_noise(p * 140.0 + offset);
        float pores = skin_noise(p * 330.0 + offset);
        vec3 skin = base, marking = ornament_color;
        float relief = 0.00012 * grain + 0.00006 * pores;
        float mask = smoothstep(0.49, 0.64, coarse);
        if (anim_body_style == 3) {
            // Bronze, bark, charcoal, pale straw, russet and moss; dorsal saddles and fine reticulation.
            skin = vec3(0.21, 0.12, 0.054); marking = vec3(0.045, 0.028, 0.018);
            if (choice == 1.0) skin = vec3(0.34, 0.25, 0.13);
            else if (choice == 2.0) { skin = vec3(0.048, 0.046, 0.038); marking = vec3(0.21, 0.18, 0.10); }
            else if (choice == 3.0) skin = vec3(0.52, 0.41, 0.23);
            else if (choice == 4.0) skin = vec3(0.33, 0.080, 0.028);
            else if (choice == 5.0) skin = vec3(0.16, 0.20, 0.085);
            float saddle = 1.0 - smoothstep(0.04, 0.08, abs(p.z + (coarse - 0.5) * 0.08));
            mask = variant == 0.0 ? saddle : variant == 1.0 ? mask
                : variant == 2.0 ? smoothstep(0.56, 0.66, skin_noise(p * 65.0 + offset)) : saddle * mask;
            col = mix(skin, marking, mask * mix(0.45, 0.9, contrast));
            if (region > 3.5 && region < 4.5) {
                float bands = smoothstep(0.78, 0.96, abs(sin(body_uv.x * 38.0 + seed)));
                col = mix(skin * 0.75, marking, bands * 0.45);
                rough = 0.65;
            } else rough = mix(0.58, 0.78, grain);
            float sutures = pow(abs(sin(p.x * 95.0)), 28.0) * (1.0 - smoothstep(-0.025, 0.07, p.x));
            col *= 1.0 - sutures * 0.12;
            relief -= sutures * 0.00020;
        } else if (anim_body_style == 4) {
            // Mantle granules and elongated flank folds, with natural leopard/banded/plain morphs.
            skin = vec3(0.32, 0.34, 0.12); marking = vec3(0.045, 0.041, 0.023);
            if (choice == 1.0) skin = vec3(0.52, 0.36, 0.11);
            else if (choice == 2.0) skin = vec3(0.24, 0.25, 0.24);
            else if (choice == 3.0) { skin = vec3(0.070, 0.065, 0.046); marking = vec3(0.37, 0.31, 0.17); }
            else if (choice == 4.0) skin = vec3(0.39, 0.11, 0.045);
            else if (choice == 5.0) skin = vec3(0.54, 0.46, 0.33);
            float leopard = smoothstep(0.53, 0.65, skin_noise(p * mix(28.0, 60.0, density) + offset));
            float stripe = smoothstep(0.80, 0.95, abs(sin(p.z * 75.0 + coarse * 1.7)));
            mask = variant == 0.0 ? leopard : variant == 1.0 ? stripe : variant == 2.0 ? mask : leopard * 0.20;
            col = mix(skin, marking, mask * mix(0.50, 0.95, contrast));
            float sole = 1.0 - smoothstep(0.025, 0.060, p.y);
            col = mix(col, skin * 0.7 + vec3(0.08, 0.07, 0.045), sole);
            float folds = pow(abs(sin(p.z * 230.0 + coarse * 4.0)), 10.0) * (region < 1.5 ? 1.0 : 0.3);
            col *= 0.92 + grain * 0.16 - folds * 0.075;
            rough = mix(0.30, 0.46, grain);
            relief = grain * 0.00055 + pores * 0.00008 - folds * 0.0003;
        } else if (anim_body_style == 5) {
            skin = mix(vec3(0.22, 0.055, 0.035), vec3(0.52, 0.23, 0.16), identity);
            if (choice == 0.0) skin = vec3(0.35, 0.20, 0.12);
            else if (choice == 4.0) skin = vec3(0.27, 0.14, 0.22);
            else if (choice == 5.0) skin = vec3(0.55, 0.36, 0.25);
            float annulus = pow(0.5 + 0.5 * cos((0.51 - p.x) / 1.02 * 6.2831853 * 60.0), 6.0);
            float dorsal = smoothstep(0.035, 0.070, p.y);
            col = skin * mix(1.12, 0.72, dorsal) * (0.93 + 0.14 * grain - annulus * 0.19);
            if (region > 4.5 && region < 5.5) col = mix(skin * 1.28, vec3(0.48, 0.32, 0.21), 0.35);
            rough = mix(0.28, 0.40, grain);
            relief = grain * 0.0001 - annulus * 0.00015;
        } else if (anim_family == 4) {
            skin = vec3(0.17, 0.055, 0.020); marking = vec3(0.56, 0.19, 0.035);
            if (choice == 1.0) { skin = vec3(0.040, 0.036, 0.029); marking = vec3(0.48, 0.33, 0.090); }
            else if (choice == 2.0) { skin = vec3(0.22, 0.10, 0.040); marking = vec3(0.65, 0.45, 0.15); }
            else if (choice == 3.0) { skin = vec3(0.045, 0.070, 0.10); marking = vec3(0.21, 0.35, 0.41); }
            else if (choice == 4.0) { skin = vec3(0.15, 0.13, 0.08); marking = vec3(0.44, 0.49, 0.21); }
            else if (choice == 5.0) { skin = vec3(0.055, 0.020, 0.023); marking = vec3(0.49, 0.064, 0.039); }
            float edge = smoothstep(0.53, 0.82, body_uv.x);
            float stripe = 1.0 - smoothstep(0.014, 0.027, abs(p.z));
            mask = variant == 0.0 ? edge : variant == 1.0 ? stripe : variant == 2.0 ? max(edge, stripe * 0.6) : mask * 0.55;
            col = mix(skin, marking, mask * mix(0.55, 1.0, contrast));
            if (region > 3.5 && region < 4.5) col = marking * 0.72;
            rough = mix(0.42, 0.62, grain);
        } else if (anim_body_style == 10 || anim_body_style == 16) {
            skin = vec3(0.29, 0.32, 0.12); marking = vec3(0.10, 0.12, 0.037);
            if (choice == 1.0) { skin = vec3(0.46, 0.27, 0.074); marking = vec3(0.17, 0.068, 0.015); }
            else if (choice == 2.0) { skin = vec3(0.11, 0.18, 0.20); marking = vec3(0.035, 0.065, 0.067); }
            else if (choice == 3.0) { skin = vec3(0.40, 0.39, 0.26); marking = vec3(0.12, 0.12, 0.080); }
            else if (choice == 4.0) { skin = vec3(0.32, 0.095, 0.040); marking = vec3(0.085, 0.039, 0.022); }
            else if (choice == 5.0) { skin = vec3(0.10, 0.090, 0.060); marking = vec3(0.33, 0.28, 0.12); }
            float line = 1.0 - smoothstep(0.006, 0.018, abs(p.z + (coarse - 0.5) * 0.008));
            float flecks = smoothstep(0.57, 0.66, skin_noise(p * 80.0 + offset));
            mask = variant == 0.0 ? mask : variant == 1.0 ? flecks : variant == 2.0 ? max(line, mask * 0.4) : mask * 0.25;
            col = mix(skin, marking, mask * mix(0.55, 0.95, contrast));
            if (anim_body_style == 10 && region > 4.5) {
                float growth = pow(abs(sin(length((p.xz - vec2(0.135, 0.0)) / vec2(0.34, 0.28)) * 100.0 + coarse)), 12.0);
                col *= 1.0 - growth * 0.08;
                relief -= growth * 0.0001;
            }
            if (region > 3.5 && region < 4.5) col = mix(skin, vec3(0.47, 0.38, 0.21), 0.5) * (0.8 + grain * 0.2);
            rough = mix(0.35, 0.55, grain);
        } else if (anim_body_style == 12) {
            skin = mix(vec3(0.075, 0.057, 0.035), vec3(0.31, 0.25, 0.15), identity);
            col = skin * (0.9 + grain * 0.18);
            rough = mix(0.28, 0.43, grain);
            if (region > 4.5 && region < 5.5) {
                vec3 shell = vec3(0.40, 0.23, 0.063), band = vec3(0.075, 0.032, 0.012);
                if (choice == 1.0) shell = vec3(0.62, 0.48, 0.24);
                else if (choice == 2.0) { shell = vec3(0.15, 0.19, 0.14); band = vec3(0.044, 0.075, 0.043); }
                else if (choice == 3.0) shell = vec3(0.32, 0.082, 0.032);
                else if (choice == 4.0) { shell = vec3(0.16, 0.13, 0.098); band = vec3(0.45, 0.36, 0.17); }
                else if (choice == 5.0) { shell = vec3(0.49, 0.43, 0.34); band = vec3(0.21, 0.16, 0.12); }
                float bands = smoothstep(0.60, 0.84, abs(sin(body_uv.y * 6.2831853 * 3.0 + coarse * 0.4)));
                float growth = pow(abs(sin(body_uv.x * 740.0 + coarse)), 16.0);
                mask = variant == 0.0 ? bands : variant == 1.0 ? mask : variant == 2.0 ? max(bands, mask * 0.5) : mask * 0.25;
                col = mix(shell, band, mask * mix(0.5, 1.0, contrast)) * (0.93 + grain * 0.14 - growth * 0.10);
                rough = mix(0.34, 0.53, grain);
                relief = grain * 0.00012 - growth * 0.00012;
            }
        } else if (anim_body_style == 13) {
            skin = vec3(0.26, 0.19, 0.10); marking = vec3(0.64, 0.49, 0.23);
            if (choice == 1.0) { skin = vec3(0.42, 0.43, 0.35); marking = vec3(0.070, 0.079, 0.050); }
            else if (choice == 2.0) { skin = vec3(0.30, 0.058, 0.032); marking = vec3(0.62, 0.29, 0.074); }
            else if (choice == 3.0) { skin = vec3(0.050, 0.067, 0.079); marking = vec3(0.28, 0.42, 0.45); }
            else if (choice == 4.0) { skin = vec3(0.28, 0.21, 0.31); marking = vec3(0.59, 0.47, 0.51); }
            else if (choice == 5.0) { skin = vec3(0.47, 0.40, 0.22); marking = vec3(0.11, 0.083, 0.042); }
            col = skin * (0.91 + grain * 0.20);
            rough = mix(0.76, 0.91, grain);
            if (region > 4.5 && (region < 5.5 || region > 7.5)) {
                float bands = smoothstep(0.54, 0.78, abs(sin(p.x * 28.0 + abs(p.z) * 17.0 + coarse * 2.0 + seed)));
                float fringe = smoothstep(0.85, 0.99, body_uv.x);
                vec2 spot = (vec2(p.x, abs(p.z)) - vec2(region < 5.5 ? 0.19 : -0.20, region < 5.5 ? 0.42 : 0.31)) / vec2(0.054, 0.065);
                float eye_radius = length(spot);
                float ocellus = (1.0 - smoothstep(0.85, 1.15, eye_radius));
                mask = variant == 0.0 ? bands : variant == 1.0 ? mask : variant == 2.0 ? max(bands * 0.45, ocellus) : bands * 0.3;
                col = mix(skin, marking, mask * mix(0.6, 1.0, contrast));
                if (variant == 2.0) {
                    col = mix(col, marking * 1.2, ocellus);
                    col = mix(col, vec3(0.020, 0.016, 0.013), 1.0 - smoothstep(0.43, 0.68, eye_radius));
                }
                // Radiating veins and overlapping scales are attached to the wing membrane.
                float veins = pow(abs(sin(body_uv.y * 6.2831853 * 12.0)), 30.0) * smoothstep(0.08, 0.23, body_uv.x);
                float scalelets = pow(abs(sin(p.x * 780.0 + floor(abs(p.z) * 650.0) * 0.9)), 8.0);
                col *= 0.95 + grain * 0.10 - veins * 0.20 - fringe * 0.19 - scalelets * 0.045;
                relief = grain * 0.00004 + veins * 0.00018 + scalelets * 0.00002;
            }
        }
        col = hue_rotate(col, genes.x * 0.35);
        eye = region > 1.5 && region < 2.5;
        if (eye) col = vec3(0.008, 0.011, 0.008);
        else if (region > 6.5 && region < 7.5) { col *= 0.25; rough = 0.53; }
        else {
            vec3 dx = dFdx(VERTEX), dy = dFdy(VERTEX);
            vec3 rx = cross(dy, NORMAL), ry = cross(NORMAL, dx);
            float det = dot(dx, rx);
            vec3 gradient = (rx * dFdx(relief) + ry * dFdy(relief)) * sign(det) / max(abs(det), 0.0000001);
            NORMAL = normalize(NORMAL - gradient);
        }
    }
    ALBEDO = col;
    ROUGHNESS = eye ? 0.08 : clamp(rough, 0.25, 0.95);
    SPECULAR = eye ? 0.5 : anim_body_style == 1 ? 0.42 : mix(0.3, 0.45, max(carapace, wet));
    CLEARCOAT = eye ? 0.0 : carapace * (1.0 - dullPatch) * 0.6;
    CLEARCOAT_ROUGHNESS = mix(0.35, 0.7, micro);
    RIM = anim_body_style == 1 || anim_body_style == 2 || detailed ? 0.025 : mix(0.08, 0.2, wet);
    RIM_TINT = 0.6;
    BACKLIGHT = vec3(translucency) * col;
}
