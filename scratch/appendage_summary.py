"""Print the guardian appendage cost summary from a perf report."""
import json
import sys

for path in sys.argv[1:]:
    d = json.load(open(path))
    f = d["facts"]
    c = f.get("guardian_constraint_cache", {})
    r = f.get("guardian_registry_cache", {})
    cal = f.get("calibration", {})
    s = f.get("summary", {})
    print(f"== {path}")
    print(f"   steps={c.get('solve_steps')} iters={c.get('solve_iterations')} "
          f"veg_queries={c.get('vegetation_queries')} veg_ms={c.get('vegetation_ms')} "
          f"tree_ms={c.get('tree_ms')} ground_ms={c.get('ground_ms')}")
    q = r.get("spatial_queries", 0)
    cand = r.get("spatial_candidates", 0)
    print(f"   queries={q} cand/query={cand/max(1,q):.1f} "
          f"narrow_ns/cand={r.get('spatial_narrow_ms',0)*1e6/max(1,cand):.0f} "
          f"broad_ns/query={r.get('spatial_broadphase_ms',0)*1e6/max(1,q):.0f}")
    print(f"   calibration: ts_ns={cal.get('timestamp_ns')} sqrt_ns={cal.get('sqrt_loop_ns')}")
    print(f"   summary: fps={s.get('fps_mean')} p50={s.get('frame_ms_p50')} p95={s.get('frame_ms_p95')} "
          f"flora={s.get('flora')} fauna={s.get('fauna')}")
    fr = f.get("flora_renderer", {})
    if fr:
        print(f"   flora: lights={fr.get('luminousLights')} visible={fr.get('floraVisible')} tris={fr.get('trianglesDrawn')}")
