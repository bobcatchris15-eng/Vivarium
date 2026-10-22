#!/usr/bin/env python3
"""Summarize PNG output from a bounded backlight sweep; reports display values only."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import tempfile
from pathlib import Path

try:
    from PIL import Image, ImageChops, ImageStat
except ImportError as exc:
    raise SystemExit("Pillow is required: python -m pip install Pillow") from exc


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def rect_arg(values):
    if len(values) != 4:
        raise argparse.ArgumentTypeError("ROI requires X Y W H")
    try:
        x, y, w, h = (int(v) for v in values)
    except ValueError as exc:
        raise argparse.ArgumentTypeError("ROI coordinates must be integers") from exc
    if x < 0 or y < 0 or w <= 0 or h <= 0:
        raise argparse.ArgumentTypeError("ROI requires nonnegative origin and positive width/height")
    return x, y, w, h


def load_rgb(path: Path):
    with Image.open(path) as im:
        if im.format != "PNG":
            raise ValueError(f"expected PNG: {path}")
        if "A" in im.getbands() or "transparency" in im.info:
            rgba = im.convert("RGBA")
            try:
                alpha = rgba.getchannel("A")
                try:
                    opaque = alpha.getextrema() == (255, 255)
                finally:
                    alpha.close()
                if not opaque:
                    raise ValueError(f"non-opaque PNG unsupported: {path}")
            finally:
                rgba.close()
        return im.convert("RGB")


def decode_srgb(value: int) -> float:
    c = value / 255.0
    return c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4


def quantile(hist, count, q):
    target = max(1, math.ceil(count * q))
    acc = 0
    for v, n in enumerate(hist):
        acc += n
        if acc >= target:
            return v
    return 255


def stats(image, rect=None):
    crop = image.crop((rect[0], rect[1], rect[0] + rect[2], rect[1] + rect[3])) if rect else image.copy()
    try:
        chans = crop.split()
        try:
            hists = [c.histogram() for c in chans]
            n = crop.width * crop.height
            encoded_means = [sum(i * count for i, count in enumerate(hist)) / n for hist in hists]
            linear_means = [sum(decode_srgb(i) * count for i, count in enumerate(hist)) / n for hist in hists]
            q = {str(int(v * 100)): [quantile(h, n, v) for h in hists] for v in (.50, .95, .99)}
            clip = sum(hist[255] for hist in hists)
            # Rec.709 weighted mean and quantiles after per-channel sRGB decoding.
            luma_hist = [0] * 65536
            for pixel in crop.getdata():
                y = .2126 * decode_srgb(pixel[0]) + .7152 * decode_srgb(pixel[1]) + .0722 * decode_srgb(pixel[2])
                luma_hist[min(65535, round(y * 65535))] += 1
            mean_luma = sum(i * count for i, count in enumerate(luma_hist)) / (n * 65535)
            def lq(frac):
                target = max(1, math.ceil(n * frac)); acc = 0
                for i, count in enumerate(luma_hist):
                    acc += count
                    if acc >= target:
                        return i / 65535
                return 1.0
            luma_q = {str(int(v * 100)): lq(v) for v in (.50, .95, .99)}
            return {"width": crop.width, "height": crop.height, "pixel_count": n,
                    "encoded_srgb_mean_rgb": encoded_means, "decoded_display_linear_rgb_mean": linear_means,
                    "encoded_srgb_channel_quantiles": q, "decoded_display_linear_rec709_mean": mean_luma,
                    "decoded_display_linear_rec709_quantiles": luma_q,
                    "channel_samples_at_255": clip, "channel_clipping_fraction": clip / (n * 3)}
        finally:
            for c in chans: c.close()
    finally:
        crop.close()


def mae(a, b, rect=None):
    aa = a.crop((rect[0], rect[1], rect[0]+rect[2], rect[1]+rect[3])) if rect else a
    bb = b.crop((rect[0], rect[1], rect[0]+rect[2], rect[1]+rect[3])) if rect else b
    try:
        return float(sum(ImageStat.Stat(ImageChops.difference(aa, bb)).mean) / 3)
    finally:
        if rect:
            aa.close(); bb.close()


def analyze(input_dir: Path, rois):
    files = sorted((p for p in input_dir.iterdir() if p.is_file() and p.suffix.lower() == ".png"), key=lambda p: p.name.casefold())
    if not files:
        raise ValueError(f"no PNG captures found in {input_dir}")
    metadata_path = input_dir / "metadata.json"
    metadata = json.loads(metadata_path.read_text(encoding="utf-8")) if metadata_path.exists() else None
    records = {}
    if isinstance(metadata, dict) and isinstance(metadata.get("captures"), list):
        records = {r["file"]: r for r in metadata["captures"] if isinstance(r, dict) and isinstance(r.get("file"), str)}
    images = {}
    try:
        for p in files:
            image = load_rgb(p)
            for roi in rois:
                if roi[0] + roi[2] > image.width or roi[1] + roi[3] > image.height:
                    image.close()
                    raise ValueError(f"ROI {roi} exceeds image bounds for {p.name} ({image.width}x{image.height})")
            images[p.name] = image
        baseline_name = next((p.name for p in files if p.stem == "baseline-a"), None)
        repeat_name = next((p.name for p in files if p.stem == "baseline-repeat"), None)
        dimensions = {im.size for im in images.values()}
        if len(dimensions) > 1:
            raise ValueError("all PNG captures must have identical dimensions")
        baseline = images.get(baseline_name) if baseline_name else None
        labels = [("full_image", None)] + [(f"roi_{i+1}", r) for i, r in enumerate(rois)]
        frames = []
        for p in files:
            im = images[p.name]
            regions = {}
            for label, rect in labels:
                item = stats(im, rect)
                if baseline is not None and im is not baseline:
                    item["mae_encoded_rgb_vs_baseline_a"] = mae(im, baseline, rect)
                regions[label] = item
            frames.append({"file": p.name, "sha256": sha256(p), "metadata": records.get(p.name), "regions": regions})
        repeat_metrics = None
        if baseline is not None and repeat_name in images:
            rep = images[repeat_name]
            repeat_metrics = {label: {"mae_encoded_rgb": mae(baseline, rep, rect)} for label, rect in labels}
        return {"schema_version": 1, "input_directory": str(input_dir.resolve()), "capture_count": len(files),
                "image_size": list(next(iter(dimensions))), "frames": frames,
                "roi_definitions": [{"label": f"roi_{index + 1}", "x": roi[0], "y": roi[1], "width": roi[2], "height": roi[3]}
                                    for index, roi in enumerate(rois)],
                "baseline_repeat_encoded_rgb_mae": repeat_metrics,
                "image_encoding_limit": "Decoded display-linear values are inverse-sRGB transforms of PNG display codes. They are not scene radiance, calibrated luminance, photon flux, backlight conservation, or a realism score; tonemapping (including AgX) and display encoding intervene.",
                "metadata": metadata}
    finally:
        for im in images.values(): im.close()


def self_check():
    with tempfile.TemporaryDirectory(prefix="backlight-self-check-") as td:
        root = Path(td); out = root / "out"; out.mkdir()
        black = Image.new("RGB", (8, 6), (0, 0, 0))
        black.save(root / "baseline-a.png")
        same = Image.new("RGB", (8, 6), (0, 0, 0)); same.save(root / "baseline-repeat.png")
        bright = Image.new("RGB", (8, 6), (0, 0, 0))
        for x in range(2, 6):
            for y in range(1, 4): bright.putpixel((x, y), (188, 188, 188))
        bright.save(root / "candidate.png")
        before = {p.name: sha256(p) for p in root.glob("*.png")}
        result = analyze(root, [(2, 1, 4, 3)])
        assert result["roi_definitions"] == [{"label": "roi_1", "x": 2, "y": 1, "width": 4, "height": 3}]
        assert result["frames"][2]["regions"]["full_image"]["mae_encoded_rgb_vs_baseline_a"] > 0
        assert result["baseline_repeat_encoded_rgb_mae"]["full_image"]["mae_encoded_rgb"] == 0
        candidate = result["frames"][2]["regions"]["roi_1"]
        assert candidate["decoded_display_linear_rec709_mean"] > 0.49
        assert abs(decode_srgb(188) - 0.503) < 0.003
        assert before == {p.name: sha256(p) for p in root.glob("*.png")}
        try:
            analyze(root, [(7, 0, 2, 2)])
        except ValueError:
            pass
        else:
            raise AssertionError("out-of-bounds ROI was accepted")
    print("self-check passed: sRGB decode, unchanged repeat, brighter ROI, bounds rejection, input preservation")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, help="directory containing PNG captures and optional metadata.json")
    parser.add_argument("--roi", nargs=4, action="append", type=int, metavar=("X", "Y", "W", "H"), help="region of interest; repeatable")
    parser.add_argument("--output", type=Path, help="new output directory (refuses existing paths)")
    parser.add_argument("--self-check", action="store_true")
    args = parser.parse_args()
    if args.self_check:
        self_check(); return
    if not args.input or not args.output:
        parser.error("--input and --output are required unless --self-check is used")
    rois = []
    for vals in args.roi or []:
        try: rois.append(rect_arg(vals))
        except argparse.ArgumentTypeError as exc: parser.error(str(exc))
    if args.output.exists(): parser.error(f"output already exists: {args.output}")
    try:
        report = analyze(args.input, rois)
        args.output.mkdir(parents=True)
        (args.output / "backlight-analysis.json").write_text(json.dumps(report, indent=2, sort_keys=True), encoding="utf-8")
    except (OSError, ValueError, json.JSONDecodeError) as exc:
        parser.error(str(exc))
    print(f"wrote {args.output / 'backlight-analysis.json'} ({report['capture_count']} PNGs)")


if __name__ == "__main__":
    main()
