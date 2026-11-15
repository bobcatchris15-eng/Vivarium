"""Summarize fixed production SurfaceAt probes; same points file required for pairs."""
import json
import math
import pathlib
import statistics
import sys


def percentile(values, q):
    values = sorted(values)
    if not values:
        return None
    p = (len(values) - 1) * q
    lo = int(p)
    return values[lo] + (values[min(lo + 1, len(values) - 1)] - values[lo]) * (p - lo)


def analyze(directory):
    directory = pathlib.Path(directory)
    report = json.loads((directory / "report.json").read_text(encoding="utf-8-sig"))
    points = json.loads((directory / "query-points.json").read_text(encoding="utf-8-sig"))
    output = {"directory": str(directory), "points": len(points), "shots": []}
    for shot in report["reports"]:
        samples = shot["querySamples"]
        heights = [[float(v) for v in s["heights"]] for s in samples]
        temporal = []
        missing = 0
        for i in range(len(points)):
            values = [row[i] for row in heights if math.isfinite(row[i])]
            missing += len(heights) - len(values)
            if len(values) >= 2:
                temporal.append(percentile(values, .95) - percentile(values, .05))
        spreads = []
        for row in heights:
            valid = [v for v in row if math.isfinite(v)]
            if valid:
                spreads.append(percentile(valid, .95) - percentile(valid, .05))
        output["shots"].append({"name": shot["name"], "physical_time": shot["physical_time"],
            "versions": len(set(s["version"] for s in samples)), "missing_queries": missing,
            "temporal_p95_minus_p05_mm_median": 1000 * statistics.median(temporal) if temporal else None,
            "temporal_p95_minus_p05_mm_p95": 1000 * percentile(temporal, .95) if temporal else None,
            "spatial_p95_minus_p05_mm_median": 1000 * statistics.median(spreads) if spreads else None,
            "spatial_p95_minus_p05_mm_max": 1000 * max(spreads) if spreads else None,
            "pool_speed_mean": shot["pool_speed_mean"], "pool_speed_max": shot["pool_speed_max"],
            "frame_p95_ms": shot["frame_p95_ms"], "gpu_mean_ms": shot["gpu_mean_ms"]})
    return output


if __name__ == "__main__":
    print(json.dumps([analyze(d) for d in sys.argv[1:]], indent=2))
