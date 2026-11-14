"""Compare the retained coverage runs; pass the build/coverage_cost directory."""
import json
import sys
from pathlib import Path

root = Path(sys.argv[1] if len(sys.argv) > 1 else "build/coverage_cost").resolve()
metrics = ("fps_mean", "frame_ms_p50", "frame_ms_p95", "frame_ms_p99",
           "frame_ms_worst", "gpu_frame_ms_mean", "allocated_bytes_per_frame",
           "draw_calls", "primitives", "video_mem_bytes", "node_count")
result = {"notes": [
    "One observation per variant, 180 seconds total and 120 seconds warmup, cap 30.",
    "Warm/render-ready young ecosystem, about three biological days; no mature-world claim.",
    "CPU scopes overlap; worker wall time is not main-thread or GPU time.",
    "Initial control camera drifted; initial runs are observational, not strict view/GPU controls.",
    "The fixed pair uses the same Game/Sim binaries, camera/input lock and a soak-only tangent switch."
]}
for label, folder in (("control", "control"), ("age_only", "candidate"),
                      ("prepared_tangents", "tangent-candidate"),
                      ("control_2", "control2"), ("fixed_control", "fixed-control"),
                      ("fixed_prepared_tangents", "fixed-tangents")):
    path = root / folder / "diagnostics/perf_summary.json"
    summary = json.loads(path.read_text(encoding="utf-8-sig"))
    start, end = summary["coverage_cache_start"], summary["coverage_cache_end"]
    row = {key: summary.get(key) for key in metrics}
    row.update({"window_seconds": summary["window_seconds"],
                "frames": summary["frames"], "coverage_start": start, "coverage_end": end,
                "triangles_committed": end["triangles_built"] - start["triangles_built"],
                "coverage_scopes": {key: value for key, value in summary["scopes_cpu_ms"].items()
                                    if key.startswith("Coverage")}})
    if "prepared_tangent_uploads" in start:
        row["prepared_tangent_uploads"] = end["prepared_tangent_uploads"] - start["prepared_tangent_uploads"]
    result[label] = row
    reference = "fixed_control" if label == "fixed_prepared_tangents" else "control"
    if label not in ("control", "fixed_control"):
        row["percent_change_from_control"] = {
            key: 100 * (row[key] / result[reference][key] - 1)
            for key in metrics if result[reference][key] not in (None, 0)}
result["fixed_camera_matches"] = all(
    result["fixed_control"][boundary][key] == result["fixed_prepared_tangents"][boundary][key]
    for boundary in ("coverage_start", "coverage_end")
    for key in ("camera_position", "camera_rotation", "camera_fov", "fixed_camera"))
result["fixed_pair_has_expected_tangent_paths"] = (
    result["fixed_control"]["prepared_tangent_uploads"] == 0
    and result["fixed_prepared_tangents"]["prepared_tangent_uploads"] > 0)
(root / "comparison.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
print(json.dumps(result, indent=2))
