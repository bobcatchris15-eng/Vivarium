// Render-only analytical two-link knee solve with an ankle-relative planted tarsus.
// CUSTOM0: existing signed offset/flag; CUSTOM1: knee.xyz + chain coordinate;
// CUSTOM2: rest ankle.xyz + active marker. UVs and animation roles stay intact.
vec3 fauna_joint_rotate(vec3 p, vec3 from_axis, vec3 to_axis) {
    float len_a = dot(from_axis, from_axis);
    float len_b = dot(to_axis, to_axis);
    if (len_a < 1e-10 || len_b < 1e-10) return p;
    vec3 a = from_axis * inversesqrt(len_a);
    vec3 b = to_axis * inversesqrt(len_b);
    float c = clamp(dot(a, b), -1.0, 1.0);
    vec3 v = cross(a, b);
    if (c < -0.9999) {
        vec3 perp = abs(a.y) < 0.9 ? vec3(0.0, 1.0, 0.0) : vec3(1.0, 0.0, 0.0);
        vec3 ax = cross(a, perp);
        float len_ax = dot(ax, ax);
        if (len_ax < 1e-10) return -p;
        vec3 axis = ax * inversesqrt(len_ax);
        return -p + 2.0 * axis * dot(axis, p);
    }
    return p + cross(v, p) + cross(v, cross(v, p)) / max(1.0 + c, 0.0001);
}
void fauna_joint_ik(inout vec3 vertex, inout vec3 normal, vec3 attach, vec3 rest_offset,
    vec4 knee_data, vec4 ankle_data, float appendage_scale, float phase,
    float duty, float stride, float lift_height) {
    if (ankle_data.w < 0.5) return;
    vec3 rest_knee = knee_data.xyz * appendage_scale;
    vec3 rest_ankle = ankle_data.xyz * appendage_scale;
    vec3 p = rest_offset * appendage_scale;
    float stance = clamp(duty, 0.05, 0.95);
    float t = fract(phase);
    float sweep = t < stance ? 1.0 - 2.0 * t / stance : -cos((t - stance) / (1.0 - stance) * 3.14159265);
    float lift = t < stance ? 0.0 : sin((t - stance) / (1.0 - stance) * 3.14159265);
    vec3 target = rest_ankle + vec3(sweep * stride, lift * lift_height, 0.0);
    float upper_len = length(rest_knee);
    float lower_len = length(rest_ankle - rest_knee);
    if (upper_len < 1e-5 || lower_len < 1e-5) return;
    float dist = length(target);
    if (dist < 1e-5) return;
    float clamped_dist = clamp(dist, abs(upper_len - lower_len) + 0.0001, upper_len + lower_len - 0.0001);
    vec3 direction = target / dist;
    vec3 pole = rest_knee - direction * dot(rest_knee, direction);
    if (dot(pole, pole) < 1e-10) pole = cross(direction, vec3(1.0, 0.0, 0.0));
    if (dot(pole, pole) < 1e-10) pole = cross(direction, vec3(0.0, 0.0, 1.0));
    float pole_len = dot(pole, pole);
    if (pole_len < 1e-10) return;
    pole = pole * inversesqrt(pole_len);
    float projection = (upper_len * upper_len - lower_len * lower_len + clamped_dist * clamped_dist) / (2.0 * clamped_dist);
    vec3 knee = direction * projection + pole * sqrt(max(upper_len * upper_len - projection * projection, 0.0));
    target = direction * clamped_dist;
    vec3 upper = fauna_joint_rotate(p, rest_knee, knee);
    vec3 lower = knee + fauna_joint_rotate(p - rest_knee, rest_ankle - rest_knee, target - knee);
    vec3 upper_n = fauna_joint_rotate(normal, rest_knee, knee);
    vec3 lower_n = fauna_joint_rotate(normal, rest_ankle - rest_knee, target - knee);
    float coordinate = knee_data.w;
    float knee_blend = smoothstep(0.88, 1.12, coordinate);
    vec3 solved = mix(upper, lower, knee_blend);
    vec3 solved_n = mix(upper_n, lower_n, knee_blend);
    float sn_len = dot(solved_n, solved_n);
    solved_n = sn_len > 1e-10 ? solved_n * inversesqrt(sn_len) : normal;
    float ankle_blend = smoothstep(1.88, 2.04, coordinate);
    solved = mix(solved, p + target - rest_ankle, ankle_blend);
    vec3 mid_n = mix(solved_n, normal, ankle_blend);
    float mn_len = dot(mid_n, mid_n);
    solved_n = mn_len > 1e-10 ? mid_n * inversesqrt(mn_len) : normal;
    float socket = smoothstep(0.0, 0.16, coordinate);
    vertex = attach + mix(p, solved, socket);
    vec3 final_n = mix(normal, solved_n, socket);
    float fn_len = dot(final_n, final_n);
    normal = fn_len > 1e-10 ? final_n * inversesqrt(fn_len) : normal;
}
