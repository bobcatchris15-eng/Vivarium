#!/usr/bin/env python3
"""Compare matched PNG captures with explicit masks and image metrics.

Reports pixel differences and luminance distribution summaries. It does not
compute a photorealism score or infer semantic material regions.
"""
from __future__ import annotations

import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import tempfile

try:
    from PIL import Image
except ImportError as exc:  # pragma: no cover - depends on bundled runtime
    raise SystemExit("Pillow is required; use the bundled Python runtime with Pillow installed") from exc


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def require_new_output(path: Path) -> None:
    if path.exists():
        raise FileExistsError(f"Output already exists; choose a new directory: {path}")


def load_rgb(path: Path):
    image = Image.open(path)
    try:
        if "A" in image.getbands() or "transparency" in image.info:
            alpha = image.convert("RGBA").getchannel("A")
            try:
                if alpha.getextrema() != (255, 255):
                    raise ValueError(f"Non-opaque PNG is unsupported for exact-black masking: {path}")
            finally:
                alpha.close()
        return image.convert("RGB")
    finally:
        image.close()


def validate_roi(roi, size):
    if roi is None:
        return (0, 0, size[0], size[1])
    x, y, w, h = roi
    if x < 0 or y < 0 or w <= 0 or h <= 0 or x + w > size[0] or y + h > size[1]:
        raise ValueError(f"ROI {roi} must be a positive rectangle inside image size {size}")
    return (x, y, x + w, y + h)


def image_stats(pixels, width, height, rect):
    x0, y0, x1, y1 = rect
    low_x, low_y, high_x, high_y = width, height, -1, -1
    hist = [0] * 256
    nonblack = 0
    black = 0
    for byte_index in range(0, len(pixels), 3):
        index = byte_index // 3
        y, x = divmod(index, width)
        if x < x0 or x >= x1 or y < y0 or y >= y1:
            continue
        r, g, b = pixels[byte_index], pixels[byte_index + 1], pixels[byte_index + 2]
        if r == 0 and g == 0 and b == 0:
            black += 1
        else:
            nonblack += 1
            low_x, low_y = min(low_x, x), min(low_y, y)
            high_x, high_y = max(high_x, x), max(high_y, y)
            value = max(0, min(255, round(.2126 * r + .7152 * g + .0722 * b)))
            hist[value] += 1
    area = (x1 - x0) * (y1 - y0)
    quantiles = {"sample_count": nonblack, "encoding": "Rec.709 luma of encoded 8-bit sRGB, rounded to 8-bit"}
    for q in (.90, .95, .99):
        target = max(1, int(q * nonblack + .999999))
        cumulative = 0
        for value, n in enumerate(hist):
            cumulative += n
            if cumulative >= target:
                quantiles[f"q{int(q * 100)}"] = value
                break
    bounds = [low_x, low_y, high_x + 1, high_y + 1] if nonblack else None
    return {"exact_black_pixels": black, "exact_black_fraction": black / area,
            "nonblack_bbox_xyxy": bounds, "nonblack_pixels": nonblack,
            "nonblack_luminance": quantiles}


def compare_pair(base_path: Path, other_path: Path, label: str, output: Path, roi):
    base, other = load_rgb(base_path), load_rgb(other_path)
    try:
        if base.size != other.size:
            raise ValueError(f"Capture dimensions differ: baseline={base.size}, {label}={other.size}")
        width, height = base.size
        rect = validate_roi(roi, base.size)
        x0, y0, x1, y1 = rect
        diff_data, black_data, mask_data = bytearray(), bytearray(), bytearray()
        diffs, n = 0, 0
        low_x, low_y, high_x, high_y = width, height, -1, -1
        base_bytes, other_bytes = base.tobytes(), other.tobytes()
        for index in range(width * height):
            y, x = divmod(index, width)
            in_roi = x0 <= x < x1 and y0 <= y < y1
            i = index * 3
            a = (base_bytes[i], base_bytes[i + 1], base_bytes[i + 2])
            b = (other_bytes[i], other_bytes[i + 1], other_bytes[i + 2])
            common_black = a == (0, 0, 0) and b == (0, 0, 0)
            union_nonblack = a != (0, 0, 0) or b != (0, 0, 0)
            black_data.append(255 if in_roi and common_black else 0)
            mask_data.append(255 if in_roi and union_nonblack else 0)
            if in_roi and union_nonblack:
                low_x, low_y = min(low_x, x), min(low_y, y)
                high_x, high_y = max(high_x, x), max(high_y, y)
                diffs += abs(a[0] - b[0]) + abs(a[1] - b[1]) + abs(a[2] - b[2])
                n += 3
            for channel in range(3):
                diff_data.append(abs(a[channel] - b[channel]) if in_roi else 0)
        safe_label = "".join(c if c.isalnum() or c in "-_" else "_" for c in label)
        diff_name, black_name, mask_name = (f"diff-{safe_label}.png", f"exact-black-{safe_label}.png", f"mask-{safe_label}.png")
        for name, img in ((diff_name, Image.frombytes("RGB", base.size, bytes(diff_data))),
                          (black_name, Image.frombytes("L", base.size, bytes(black_data))),
                          (mask_name, Image.frombytes("L", base.size, bytes(mask_data)))):
            img.save(output / name)
            img.close()
        base_stats = image_stats(base_bytes, width, height, rect)
        other_stats = image_stats(other_bytes, width, height, rect)
        union_bbox = [low_x, low_y, high_x + 1, high_y + 1] if high_x >= 0 else None
        return {"label": label, "baseline": {"path": str(base_path), "sha256": sha256(base_path), **base_stats},
                "compared": {"path": str(other_path), "sha256": sha256(other_path), **other_stats},
                "dimensions": {"width": width, "height": height}, "roi_xywh": list(roi) if roi else None,
                "foreground_mask_rule": "union of pixels nonblack in either image, within ROI; exact black means RGB=(0,0,0)",
                "foreground_union_bbox_xyxy": union_bbox,
                "masked_rgb_mae": (diffs / n) if n else 0.0, "masked_rgb_channel_samples": n,
                "common_exact_black_pixels": sum(1 for value in black_data if value == 255),
                "diff_image": diff_name, "exact_black_mask_image": black_name, "foreground_mask_image": mask_name}
    finally:
        base.close()
        other.close()


def self_check():
    with tempfile.TemporaryDirectory(prefix="m02-self-check-") as temp:
        root = Path(temp)
        baseline = root / "baseline.png"
        repeat = root / "repeat.png"
        candidate = root / "candidate.png"
        mismatch = root / "mismatch.png"
        pixels = [(0, 0, 0), (20, 30, 40), (0, 0, 0), (80, 80, 80)]
        for path, size, data in ((baseline, (2, 2), pixels), (repeat, (2, 2), pixels),
                                 (candidate, (2, 2), [(0, 0, 0), (40, 30, 40), (0, 0, 0), (80, 80, 80)]),
                                 (mismatch, (1, 4), pixels)):
            img = Image.new("RGB", size)
            img.putdata(data)
            img.save(path)
            img.close()
        out = root / "out"
        out.mkdir()
        identical = compare_pair(baseline, repeat, "baseline-repeat", out, None)
        if identical["masked_rgb_mae"] != 0 or identical["common_exact_black_pixels"] != 2:
            raise AssertionError("identical/black mask metric mismatch")
        changed = compare_pair(baseline, candidate, "candidate-F0", out, None)
        if changed["masked_rgb_mae"] <= 0 or changed["compared"]["exact_black_pixels"] != 2:
            raise AssertionError("candidate difference or exact-black count not detected")
        try:
            compare_pair(baseline, mismatch, "mismatch", out, None)
        except ValueError as exc:
            if "dimensions differ" not in str(exc):
                raise
        else:
            raise AssertionError("dimension mismatch accepted")
        cli_out = root / "cli-output"
        run = subprocess.run([sys.executable, str(Path(__file__).resolve()), "--baseline", str(baseline),
                             "--repeat", str(repeat), "--candidate", str(candidate), "--output", str(cli_out)],
                            text=True, capture_output=True)
        if run.returncode != 0 or "M02_PNG_COMPARE_OK" not in run.stdout:
            raise AssertionError("CLI end-to-end self-check failed: " + run.stderr)
        report = json.loads((cli_out / "comparison.json").read_text(encoding="utf-8"))
        if len(report["comparisons"]) != 2 or report["photorealism_score"] is not None:
            raise AssertionError("CLI report structure or score limitation failed")
        occupied = root / "occupied"
        occupied.mkdir()
        sentinel = occupied / "sentinel.txt"
        sentinel.write_text("preserve", encoding="utf-8")
        try:
            require_new_output(occupied)
        except FileExistsError:
            pass
        else:
            raise AssertionError("existing output accepted")
        if sentinel.read_text(encoding="utf-8") != "preserve":
            raise AssertionError("existing output modified")
    print("M02_PNG_SELF_CHECK_OK identical=zero candidate_delta=detected size_mismatch=rejected occupied_output=preserved")


def main():
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--baseline", type=Path, help="F0 baseline PNG")
    ap.add_argument("--repeat", type=Path, help="independent F0 repeat PNG")
    ap.add_argument("--candidate", type=Path, action="append", help="candidate PNG; repeat for multiple candidates")
    ap.add_argument("--output", type=Path, help="new output directory; existing directory is refused")
    ap.add_argument("--roi", type=int, nargs=4, metavar=("X", "Y", "W", "H"), help="optional stable ROI in pixels")
    ap.add_argument("--self-check", action="store_true", help="run tiny synthetic metric/mask/mismatch checks")
    args = ap.parse_args()
    if args.self_check:
        self_check()
        return 0
    if not args.baseline or not args.repeat or not args.candidate or not args.output:
        ap.error("--baseline, --repeat, at least one --candidate, and --output are required")
    paths = [args.baseline, args.repeat, *args.candidate]
    for path in paths:
        if path.suffix.lower() != ".png":
            ap.error("Input must be a PNG: " + str(path))
        if not path.is_file():
            ap.error("PNG input not found: " + str(path))
    output = args.output.resolve()
    try:
        require_new_output(output)
    except FileExistsError as exc:
        ap.error(str(exc))
    # Read and validate all image dimensions before creating output files.
    dimensions = []
    for path in paths:
        im = load_rgb(path)
        dimensions.append(im.size)
        im.close()
    if any(size != dimensions[0] for size in dimensions[1:]):
        ap.error("All baseline, repeat, and candidate captures must have identical dimensions: " + repr(dimensions))
    validate_roi(args.roi, dimensions[0])
    output.mkdir(parents=True)
    comparisons = [compare_pair(args.baseline, args.repeat, "baseline-repeat", output, args.roi)]
    for index, candidate in enumerate(args.candidate):
        label = f"candidate-{index + 1}-{candidate.stem}"
        comparisons.append(compare_pair(args.baseline, candidate, label, output, args.roi))
    report = {"schema_version": 1, "title": "M02 PNG capture comparison metrics",
              "photorealism_score": None,
              "interpretation_limits": ["Pixel metrics describe image differences only; they do not rate photorealism.",
                  "Exact-black masking is not semantic segmentation; nonblack subjects, standards, shadows, and backgrounds all remain in the mask.",
                  "No diffuse-only material separation is inferred unless the input captures were explicitly authored as diagnostic diffuse captures.",
                  "Luminance quantiles use encoded 8-bit sRGB Rec.709 luma and are descriptive, not radiometric measurements."],
              "mask_scope": "all pixels in images unless an ROI is supplied; differences use the union nonblack mask",
              "comparisons": comparisons}
    (output / "comparison.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print("M02_PNG_COMPARE_OK " + str(output) + f" comparisons={len(comparisons)} score=none")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
