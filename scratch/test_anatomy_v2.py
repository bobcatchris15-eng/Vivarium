import math
import numpy as np
from mathutils import Vector

def test_profiles():
    print("Testing anatomical profiles v2...")

    # 1. Test Snout / Muzzle Square profile
    def box_square(q, roundness=0.35):
        s4 = math.sin(q)**4 + math.cos(q)**4
        return (s4)**(-0.22 * (1.0 - roundness))

    for angle in [0, math.pi/4, math.pi/2, 3*math.pi/4, math.pi]:
        f = box_square(angle)
        print(f"Angle {math.degrees(angle):.0f} deg -> box factor: {f:.3f}")

    # 2. Test Tail Junction continuity
    pelvis_rx = 0.155
    pelvis_ry = 0.130

    def tail_rx_v2(angle, prog):
        # At prog = 0 (pelvis junction), tail is broad side-to-side (caudofemoral base)
        # As prog increases, it tapers and becomes laterally compressed
        base_w = pelvis_rx * (1.0 - 0.75 * (prog**0.85))
        return base_w

    def tail_ry_v2(angle, prog):
        # At prog = 0, ry matches pelvis_ry
        base_h = pelvis_ry * (1.0 - 0.65 * (prog**1.1))
        # Double swimming crest emerges downstream (prog > 0.18)
        if 0.18 < prog < 0.88:
            keel = 0.022 * math.sin(math.pi * (prog - 0.18) / 0.70) * math.exp(-((abs(math.sin(angle)) - 0.22) / 0.12)**2) * (1.0 if math.cos(angle) > 0 else 0.0)
            return base_h + keel
        return base_h

    print(f"Tail junction at prog=0: rx={tail_rx_v2(0, 0):.4f} (matches {pelvis_rx}), ry={tail_ry_v2(0, 0):.4f} (matches {pelvis_ry})")
    print(f"Tail mid at prog=0.5: rx={tail_rx_v2(0, 0.5):.4f}, ry={tail_ry_v2(0, 0.5):.4f} (laterally compressed!)")

    # 3. Test Head profiles
    profile_data_v2 = [
        # (z, center_y, rx, ry, section_type)
        (-1.25, 0.270, 0.062, 0.040, 'blunt_snout_apex'),
        (-1.20, 0.274, 0.076, 0.048, 'rostrum_muzzle'),
        (-1.12, 0.282, 0.098, 0.058, 'mid_snout_canthus'),
        (-1.03, 0.294, 0.126, 0.068, 'suborbital_cheek'),
        (-0.95, 0.304, 0.146, 0.076, 'cheekbone_orbit'),
        (-0.88, 0.306, 0.144, 0.084, 'temporal_adductor'),
        (-0.84, 0.300, 0.136, 0.098, 'occipital_crest'),
        (-0.75, 0.288, 0.122, 0.098, 'nuchal_neck_transition'),
        (-0.62, 0.286, 0.128, 0.106, 'cervical_column'),
        (-0.49, 0.280, 0.185, 0.150, 'pectoral_shoulder'),
        (-0.32, 0.280, 0.230, 0.162, 'anterior_thorax'),
        (-0.12, 0.266, 0.232, 0.156, 'mid_torso_flank'),
        ( 0.10, 0.256, 0.208, 0.138, 'posterior_torso'),
        ( 0.28, 0.262, 0.215, 0.148, 'lumbar_flank'),
        ( 0.42, 0.255, 0.190, 0.138, 'pelvic_girdle'),
        ( 0.49, 0.245, 0.155, 0.130, 'caudofemoral_base')
    ]
    print(f"Total profile nodes: {len(profile_data_v2)}")
    print("Snout width:", profile_data_v2[0][2]*2, "m (broad blunt square!)")
    print("Cheekbone width:", profile_data_v2[4][2]*2, "m (broad trapezoid!)")
    print("Occipital crest height:", profile_data_v2[6][3]*2, "m (pronounced occipital bone!)")

if __name__ == "__main__":
    test_profiles()
