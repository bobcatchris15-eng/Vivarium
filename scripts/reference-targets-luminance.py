"""Compute luminance metrics for reference photos."""

import json
from pathlib import Path
import numpy as np
from PIL import Image


def compute_metrics(image_path: Path) -> dict:
    with Image.open(image_path) as img:
        rgb = img.convert("RGB")
    arr = np.asarray(rgb, dtype=np.float64) / 255.0
    lum = 0.2126 * arr[:, :, 0] + 0.7152 * arr[:, :, 1] + 0.0722 * arr[:, :, 2]

    mean = float(np.mean(lum))
    p05 = float(np.percentile(lum, 5))
    p95 = float(np.percentile(lum, 95))
    clip_black = float(np.mean(lum < 0.01) * 100.0)
    clip_white = float(np.mean(lum > 0.99) * 100.0)

    return {
        "lum_mean": round(mean, 4),
        "lum_p05": round(p05, 4),
        "lum_p95": round(p95, 4),
        "lum_clip_black": round(clip_black, 4),
        "lum_clip_white": round(clip_white, 4),
    }


def main():
    repo_root = Path(__file__).resolve().parent.parent
    ref_dir = repo_root / "docs" / "reference-targets"
    target_files = ["real-01.png", "real-02.webp", "real-03.webp"]

    results = {}
    for filename in target_files:
        path = ref_dir / filename
        if not path.exists():
            raise FileNotFoundError(f"Reference image not found: {path}")
        results[filename] = compute_metrics(path)

    out_path = ref_dir / "luminance_targets.json"
    with open(out_path, "w", encoding="utf-8") as f:
        json.dump(results, f, indent=2)
        f.write("\n")

    for filename, m in results.items():
        print(f"{filename}: mean={m['lum_mean']} p05={m['lum_p05']} p95={m['lum_p95']} black={m['lum_clip_black']}% white={m['lum_clip_white']}%")
    print(f"Wrote targets to {out_path}")


if __name__ == "__main__":
    main()
