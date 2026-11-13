// L1: cached sky-visibility (geometry occlusion) volume. Baked on the CPU by Vivarium.Sim.Repr.Light.OcclusionVolume,
// uploaded only after a bake, bound as global shader parameters. strength 0 = disabled (returns 1.0, no texture read).
// Texture layout: width = x, height = z, depth = y; rgb = bent normal * 0.5 + 0.5, a = sky visibility.
global uniform sampler3D repr_skyvis_tex;
global uniform vec3 repr_skyvis_origin;
global uniform vec3 repr_skyvis_inv_size;   // 1 / extent per axis
global uniform float repr_skyvis_strength;  // 0 disabled, 1 as baked, >1 exaggerated (visibility ^ strength)
global uniform float repr_skyvis_voxel;     // voxel edge in metres

// Direct-light share of the shader's AO that stays when the field is on: the field is an ambient/indirect term.
const float REPR_SKYVIS_LIGHT_AFFECT = 0.15;

bool repr_skyvis_lookup(vec3 world_pos, vec3 world_normal, out vec4 s) {
    s = vec4(0.0, 1.0, 0.0, 1.0);
    // Offset along the normal so a surface does not occlude itself (it lies inside its own voxel).
    vec3 p = world_pos + world_normal * (repr_skyvis_voxel * 1.5);
    vec3 uvw = (p - repr_skyvis_origin) * repr_skyvis_inv_size;
    if (uvw.x < 0.0 || uvw.y < 0.0 || uvw.z < 0.0 || uvw.x > 1.0 || uvw.y > 1.0 || uvw.z > 1.0) return false;
    s = texture(repr_skyvis_tex, uvw.xzy);
    return true;
}

// Sky visibility 0..1 (1 = open sky, also when disabled or outside the volume).
float repr_sky_visibility(vec3 world_pos, vec3 world_normal) {
    if (repr_skyvis_strength <= 0.0) return 1.0;
    vec4 s;
    if (!repr_skyvis_lookup(world_pos, world_normal, s)) return 1.0;
    return pow(clamp(s.a, 0.0, 1.0), repr_skyvis_strength);
}

// Bent-normal directional factor: how much of the normal's hemisphere points toward the open sky, relative to an
// unoccluded point (1 in the open, lower when the open sky lies off to the side of the surface normal).
float repr_sky_directional(vec3 world_pos, vec3 world_normal) {
    if (repr_skyvis_strength <= 0.0) return 1.0;
    vec4 s;
    if (!repr_skyvis_lookup(world_pos, world_normal, s)) return 1.0;
    vec3 bent = normalize(s.rgb * 2.0 - 1.0);
    float got = clamp(0.5 + 0.5 * dot(world_normal, bent), 0.0, 1.0);
    float ref = max(clamp(0.5 + 0.5 * world_normal.y, 0.0, 1.0), 0.05);
    return clamp(got / ref, 0.0, 1.0);
}
