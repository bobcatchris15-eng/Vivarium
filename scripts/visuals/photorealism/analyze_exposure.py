#!/usr/bin/env python3
"""Summarize exposure-calibration PNG frames without assigning a verdict."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import subprocess
import sys
import tempfile

from PIL import Image, ImageChops, ImageStat


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def require_new_output(path: Path) -> None:
    if path.exists():
        raise FileExistsError(f"Output already exists; choose a new path: {path}")


def load_rgb(path: Path) -> Image.Image:
    with Image.open(path) as opened:
        if "A" in opened.getbands() or "transparency" in opened.info:
            alpha = opened.convert("RGBA").getchannel("A")
            try:
                if alpha.getextrema() != (255, 255):
                    raise ValueError(f"Non-opaque PNG is unsupported: {path}")
            finally:
                alpha.close()
        return opened.convert("RGB")


def quantile(histogram: list[int], samples: int, q: float) -> int:
    if samples == 0:
        return 0
    target = max(1, math.ceil(q * samples))
    cumulative = 0
    for value, count in enumerate(histogram):
        cumulative += count
        if cumulative >= target:
            return value
    return 255


def whole_image_stats(image: Image.Image) -> dict:
    red, green, blue = image.split()
    try:
        black = ImageChops.add(ImageChops.add(red, green), blue)
        max_channel = ImageChops.lighter(ImageChops.lighter(red, green), blue)
        luma = image.convert("L")  # Pillow's 8-bit grayscale transform; not radiometric luminance.
        try:
            black_count = black.histogram()[0]
            clipped_pixels = max_channel.histogram()[255]
            clipped_luma = luma.histogram()[255]
            channel_histograms = [red.histogram(), green.histogram(), blue.histogram()]
        finally:
            black.close()
            max_channel.close()
            luma.close()
    finally:
        red.close()
        green.close()
        blue.close()
    width, height = image.size
    pixels = width * height
    clipped_samples = sum(hist[255] for hist in channel_histograms)
    return {
        "width": width,
        "height": height,
        "pixel_count": pixels,
        "exact_black_pixels": black_count,
        "exact_black_fraction": black_count / pixels,
        "pixels_with_any_channel_at_255": clipped_pixels,
        "pixels_with_any_channel_at_255_fraction": clipped_pixels / pixels,
        "channel_samples_at_255": clipped_samples,
        "channel_samples_at_255_fraction": clipped_samples / (pixels * 3),
        "grayscale_luma_at_255_pixels": clipped_luma,
        "grayscale_luma_at_255_fraction": clipped_luma / pixels,
        "luma_definition": "Pillow 8-bit RGB-to-L conversion; encoded-image diagnostic, not radiometric luminance"
    }


def region_histograms(image: Image.Image, rect: tuple[int, int, int, int]) -> dict:
    crop = image.crop(rect)
    channels = crop.split()
    try:
        stat = ImageStat.Stat(crop)
        means = [float(x) for x in stat.mean]
        histograms = [channel.histogram() for channel in channels]
    finally:
        crop.close()
        for channel in channels:
            channel.close()
    count = (rect[2] - rect[0]) * (rect[3] - rect[1])
    names = ("R", "G", "B")
    quantiles = {name: {f"q{int(q * 100)}": quantile(hist, count, q)
                        for q in (.50, .95, .99)}
                 for name, hist in zip(names, histograms)}
    return {"pixel_count": count,
            "encoded_srgb_channel_mean_0_255": dict(zip(names, means)),
            "encoded_srgb_channel_quantiles_0_255": quantiles}


def parse_roi(values: list[int], size: tuple[int, int], index: int) -> dict:
    x, y, width, height = values
    if x < 0 or y < 0 or width <= 0 or height <= 0 or x + width > size[0] or y + height > size[1]:
        raise ValueError(f"ROI {index} ({values}) is outside image dimensions {size}")
    return {"name": f"roi-{index}", "xywh": values, "xyxy": [x, y, x + width, y + height]}


def roi_mae(left: Path, right: Path, rect: tuple[int, int, int, int]) -> float:
    a, b = load_rgb(left), load_rgb(right)
    try:
        if a.size != b.size:
            raise ValueError(f"Capture dimensions differ: {left.name}={a.size}, {right.name}={b.size}")
        ac, bc = a.crop(rect), b.crop(rect)
        try:
            diff = ImageChops.difference(ac, bc)
            try:
                return float(sum(ImageStat.Stat(diff).mean) / 3.0)
            finally:
                diff.close()
        finally:
            ac.close()
            bc.close()
    finally:
        a.close()
        b.close()


def match_metadata(metadata, filename: str):
    """Return raw dict records containing a filename-like value matching this frame."""
    found = []
    expected = filename.casefold()
    keys = {"file", "filename", "path", "image", "frame"}

    def walk(value):
        if isinstance(value, dict):
            matched = False
            for key, child in value.items():
                if str(key).casefold() in keys and isinstance(child, str):
                    if Path(child.replace("\\", "/")).name.casefold() == expected:
                        matched = True
            if matched:
                found.append(value)
            for child in value.values():
                walk(child)
        elif isinstance(value, list):
            for child in value:
                walk(child)

    walk(metadata)
    return found


def analyze(input_dir: Path, rois: list[list[int]], reference: str | None) -> dict:
    frames = sorted(input_dir.glob("*.png"), key=lambda path: path.name.casefold())
    if not frames:
        raise ValueError(f"No PNG frames found in {input_dir}")
    images = [load_rgb(path) for path in frames]
    try:
        size = images[0].size
        if any(image.size != size for image in images):
            sizes = {path.name: image.size for path, image in zip(frames, images)}
            raise ValueError("PNG frame dimensions differ: " + json.dumps(sizes))
        roi_specs = [parse_roi(values, size, index + 1) for index, values in enumerate(rois)]
        regions = [{"name": "whole-image", "xywh": [0, 0, size[0], size[1]], "xyxy": [0, 0, size[0], size[1]]}, *roi_specs]
        ref_path = None
        if reference is not None:
            matches = [path for path in frames if path.name.casefold() == Path(reference).name.casefold()]
            if len(matches) != 1:
                raise ValueError(f"--reference must identify one PNG filename in the input directory: {reference}")
            ref_path = matches[0]
        metadata_path = input_dir / "metadata.json"
        metadata = json.loads(metadata_path.read_text(encoding="utf-8")) if metadata_path.is_file() else None
        report_frames = []
        for path, image in zip(frames, images):
            rois_report = []
            for region in regions:
                x, y, w, h = region["xywh"]
                rect = (x, y, x + w, y + h)
                entry = {**region, **region_histograms(image, rect)}
                if ref_path is not None:
                    entry["mae_rgb_vs_reference"] = roi_mae(ref_path, path, rect)
                else:
                    entry["mae_rgb_vs_reference"] = None
                rois_report.append(entry)
            report_frames.append({
                "filename": path.name,
                "sha256": sha256(path),
                "whole_image": whole_image_stats(image),
                "regions": rois_report,
                "metadata_matches": match_metadata(metadata, path.name) if metadata is not None else []
            })
        sequential = []
        for index in range(1, len(frames)):
            left, right = frames[index - 1], frames[index]
            deltas = []
            for region in regions:
                x, y, w, h = region["xywh"]
                deltas.append({"region": region["name"],
                               "mae_rgb": roi_mae(left, right, (x, y, x + w, y + h))})
            sequential.append({"from": left.name, "to": right.name, "regions": deltas})
        return {
            "schema_version": 1,
            "title": "Exposure diagnostic frame metrics",
            "frame_order": "case-insensitive lexical filename order; metadata is preserved but not used to reorder frames",
            "input_directory": str(input_dir),
            "frame_count": len(frames),
            "dimensions": {"width": size[0], "height": size[1]},
            "reference_filename": ref_path.name if ref_path else None,
            "reference_meaning": "per-region RGB MAE is against this explicitly selected frame" if ref_path else "no fixed-frame reference was selected",
            "metadata": {"path": str(metadata_path), "sha256": sha256(metadata_path), "document": metadata} if metadata is not None else None,
            "regions": regions,
            "frames": report_frames,
            "sequential_stability": sequential,
            "interpretation_limits": [
                "These are encoded-image measurements; no exposure cause, adaptation response, or production-quality verdict is inferred.",
                "Exact black is RGB=(0,0,0). Whole-image black fractions include the original black background.",
                "Channel clipping means a channel sample equals 255; any-channel pixel fraction and total channel-sample fraction are both reported.",
                "Luma clipping uses Pillow's 8-bit RGB-to-L conversion and is not radiometric luminance.",
                "ROI statistics are channel-wise encoded sRGB values; ROIs require stable alignment across frames.",
                "Metadata is retained wholesale and filename-matched dictionary records are attached where recognizable; unknown metadata schema is not interpreted."
            ]
        }
    finally:
        for image in images:
            image.close()


def self_check():
    with tempfile.TemporaryDirectory(prefix="exposure-self-check-") as temp:
        root = Path(temp)
        frames_dir = root / "frames"
        frames_dir.mkdir()
        first = Image.new("RGB", (4, 4), (0, 0, 0))
        second = Image.new("RGB", (4, 4), (0, 0, 0))
        for image in (first, second):
            for y in (1, 2):
                for x in (1, 2):
                    image.putpixel((x, y), (32, 32, 32))
        first.putpixel((3, 0), (40, 40, 40))
        second.putpixel((3, 0), (120, 120, 120))
        first.save(frames_dir / "early.png")
        second.save(frames_dir / "late.png")
        first.close()
        second.close()
        metadata_document = {"unknown_schema_note": "retain as supplied",
                             "frames": [{"filename": "early.png", "phase": 0.15},
                                        {"path": "captures/late.png", "phase": 0.30}]}
        (frames_dir / "metadata.json").write_text(json.dumps(metadata_document), encoding="utf-8")
        report = analyze(frames_dir, [[1, 1, 2, 2]], "early.png")
        early, late = report["frames"]
        if early["whole_image"]["exact_black_fraction"] != late["whole_image"]["exact_black_fraction"]:
            raise AssertionError("whole-image black fraction changed when only card luminance changed")
        if report["sequential_stability"][0]["regions"][0]["mae_rgb"] <= 0:
            raise AssertionError("outside-ROI card change was not detected in whole-image stability")
        if early["regions"][1]["encoded_srgb_channel_mean_0_255"] != late["regions"][1]["encoded_srgb_channel_mean_0_255"]:
            raise AssertionError("stable ROI changed while only outside-ROI card changed")
        if report["sequential_stability"][0]["regions"][1]["mae_rgb"] != 0:
            raise AssertionError("sequential ROI stability should be zero for unchanged ROI")
        if late["regions"][1]["mae_rgb_vs_reference"] != 0:
            raise AssertionError("ROI MAE versus reference should be zero for unchanged ROI")
        if report["metadata"]["document"] != metadata_document or len(early["metadata_matches"]) != 1 or len(late["metadata_matches"]) != 1:
            raise AssertionError("metadata preservation or flexible filename matching failed")
        cli_output = root / "analysis"
        run = subprocess.run([sys.executable, str(Path(__file__).resolve()), "--input", str(frames_dir),
                              "--roi", "1", "1", "2", "2", "--reference", "early.png",
                              "--output", str(cli_output)], text=True, capture_output=True)
        if run.returncode != 0 or "EXPOSURE_METRICS_OK" not in run.stdout:
            raise AssertionError("CLI end-to-end check failed: " + run.stderr)
        written = json.loads((cli_output / "exposure-analysis.json").read_text(encoding="utf-8"))
        if written["frame_count"] != 2 or written["metadata"]["document"] != metadata_document:
            raise AssertionError("CLI report content mismatch")
        output = root / "occupied"
        output.mkdir()
        sentinel = output / "keep.txt"
        sentinel.write_text("keep", encoding="utf-8")
        try:
            require_new_output(output)
        except FileExistsError:
            pass
        else:
            raise AssertionError("existing output accepted")
        if sentinel.read_text(encoding="utf-8") != "keep":
            raise AssertionError("existing output changed")
    print("EXPOSURE_METRICS_SELF_CHECK_OK black_fraction_constant=1 roi_constant=1 fixed_reference=1 sequential_roi=1 occupied_output=preserved")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, help="directory containing PNG frames and optional metadata.json")
    parser.add_argument("--roi", type=int, nargs=4, action="append", metavar=("X", "Y", "W", "H"),
                        help="optional region in pixels; repeat to add regions")
    parser.add_argument("--reference", help="optional fixed-frame PNG filename for per-region MAE")
    parser.add_argument("--output", type=Path, help="new analysis directory; existing path is refused")
    parser.add_argument("--self-check", action="store_true", help="run small synthetic black/ROI/output check")
    args = parser.parse_args()
    if args.self_check:
        self_check()
        return 0
    if not args.input or not args.output:
        parser.error("--input and --output are required")
    input_dir = args.input.resolve()
    if not input_dir.is_dir():
        parser.error("Input directory not found: " + str(input_dir))
    output = args.output.resolve()
    try:
        require_new_output(output)
    except FileExistsError as exc:
        parser.error(str(exc))
    try:
        report = analyze(input_dir, args.roi or [], args.reference)
    except (ValueError, OSError, json.JSONDecodeError) as exc:
        parser.error(str(exc))
    output.mkdir(parents=True)
    (output / "exposure-analysis.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"EXPOSURE_METRICS_OK {output} frames={report['frame_count']} reference={report['reference_filename'] or 'none'}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
