import math
from mathutils import Vector

def test_claw():
    # Foot position: e.g. front left foot at (0.39, 0.05, -0.57)
    f = Vector((0.39, 0.05, -0.57))
    s = 1.0 # Left side
    d_idx = 2 # Digit III (middle)
    d_spread = 0.0 # Straight forward
    d_len = 0.090

    # In Blender:
    # Forward is -Z
    # Up is +Y
    # Right is +X (for left side s=1, right is outward from body)
    f_forward = Vector((0, 0, -1))
    f_right = Vector((s * 1.0, 0, 0))
    d_dir = (f_forward * math.cos(d_spread) + f_right * math.sin(d_spread)).normalized()
    print(f"d_dir: {d_dir} (should be along -Z)")

    d_base = f + Vector((s * (d_idx - 2) * 0.016, -0.015, -0.025))
    ph1 = d_base + d_dir * (d_len * 0.38) + Vector((0, -0.004, 0))
    ph2 = ph1 + d_dir * (d_len * 0.34) + Vector((0, -0.006, 0))
    d_end = ph2 + d_dir * (d_len * 0.28) + Vector((0, -0.005, 0))
    print(f"d_end: {d_end} (z should be more negative than d_base: {d_base.z} -> {d_end.z})")

    # Now claw arc:
    c_len = 0.035
    claw_arc_steps = 6
    claw_pts = []
    for step in range(claw_arc_steps + 1):
        prog = step / claw_arc_steps
        # Angle from horizontal (0 rad = along d_dir, pi/3 rad = curling down towards -Y)
        angle = prog * 1.10 # ~63 degrees downward curl
        # Vector along the arc:
        # horizontal forward component: cos(angle) along d_dir
        # vertical downward component: -sin(angle) along Y
        # Since angle starts at 0 and grows, the position integral / parametric curve is:
        forward_dist = math.sin(angle) / 1.10 * c_len # integral of cos
        down_dist = (1.0 - math.cos(angle)) / 1.10 * c_len # integral of sin

        c_pos = d_end + d_dir * forward_dist + Vector((0, -down_dist, 0))
        claw_pts.append(c_pos)
        print(f"step {step} (prog={prog:.2f}): forward_dist={forward_dist:.4f}, down_dist={down_dist:.4f}, pos={c_pos}")

    # Check: does z move further forward (more negative)?
    assert claw_pts[-1].z < claw_pts[0].z, "Claw tip must be further forward than claw base!"
    # Check: does y move downward (lower)?
    assert claw_pts[-1].y < claw_pts[0].y, "Claw tip must be lower than claw base!"
    print("Claw geometry verification PASSED!")

if __name__ == "__main__":
    test_claw()
