#!/usr/bin/env python3
"""Analyze fixed-camera custom direct-light specular diagnostic captures."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import subprocess
import sys
import tempfile

from PIL import Image, ImageChops, ImageStat


SPECULARS = (0.0, 0.25, 0.5, 1.0)
LEGACY_RE = re.compile(r"^custom-legacy-light-specular-(0\.00|0\.25|0\.50|1\.00)$", re.I)
EXPLICIT_RE = re.compile(r"^custom-explicit-f0-0\.04-light-specular-(0\.00|0\.25|0\.50|1\.00)$", re.I)
STANDARD_RE = re.compile(r"^standard-reference-light-specular-(0\.00|0\.25|0\.50|1\.00)$", re.I)


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def require_new_output(path: Path) -> None:
    if path.exists():
        raise FileExistsError(f"Output already exists; choose a new directory: {path}")


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


def quantile(hist: list[int], count: int, q: float) -> int:
    if count <= 0:
        return 0
    target = max(1, math.ceil(q * count))
    seen = 0
    for value, frequency in enumerate(hist):
        seen += frequency
        if seen >= target:
            return value
    return 255


def srgb_decode(value: int) -> float:
    encoded = value / 255.0
    return encoded / 12.92 if encoded <= 0.04045 else ((encoded + 0.055) / 1.055) ** 2.4


def luminance_mean_from_hist(hist: list[int], pixels: int) -> float:
    if pixels == 0:
        return 0.0
    return sum(srgb_decode(value) * count for value, count in enumerate(hist)) / pixels


def metadata_records(metadata, filename: str) -> list:
    found = []
    expected = filename.casefold()
    keys = {"file", "filename", "path", "image"}

    def visit(value):
        if isinstance(value, dict):
            if any(str(key).casefold() in keys and isinstance(child, str)
                   and Path(child.replace("\\", "/")).name.casefold() == expected
                   for key, child in value.items()):
                found.append(value)
            for child in value.values():
                visit(child)
        elif isinstance(value, list):
            for child in value:
                visit(child)

    visit(metadata)
    return found


def image_summary(path: Path, roi: list[int]) -> dict:
    image = load_rgb(path)
    try:
        width, height = image.size
        x, y, w, h = roi
        if x < 0 or y < 0 or w <= 0 or h <= 0 or x + w > width or y + h > height:
            raise ValueError(f"ROI {roi} lies outside {path.name} dimensions {(width, height)}")
        red, green, blue = image.split()
        try:
            red_green_max = ImageChops.lighter(red, green)
            try:
                max_channel = ImageChops.lighter(red_green_max, blue)
                try:
                    clipped = max_channel.histogram()[255]
                finally:
                    max_channel.close()
            finally:
                red_green_max.close()
            channel_histograms = [red.histogram(), green.histogram(), blue.histogram()]
        finally:
            red.close()
            green.close()
            blue.close()
        total_pixels = width * height
        crop = image.crop((x, y, x + w, y + h))
        try:
            channels = crop.split()
            try:
                encoded_means = [float(value) for value in ImageStat.Stat(crop).mean]
                roi_histograms = [channel.histogram() for channel in channels]
            finally:
                for channel in channels:
                    channel.close()
        finally:
            crop.close()
        roi_pixels = w * h
        channel_names = ("R", "G", "B")
        encoded = dict(zip(channel_names, encoded_means))
        linear = {name: luminance_mean_from_hist(hist, roi_pixels)
                  for name, hist in zip(channel_names, roi_histograms)}
        quantiles = {name: {f"q{int(q * 100)}": quantile(hist, roi_pixels, q)
                            for q in (.50, .95, .99)}
                     for name, hist in zip(channel_names, roi_histograms)}
        channel_clips = [hist[255] for hist in channel_histograms]
        roi_bytes = crop_bytes(image, roi)
        black_pixels = sum(1 for index in range(0, len(roi_bytes), 3)
                           if roi_bytes[index] == 0 and roi_bytes[index + 1] == 0 and roi_bytes[index + 2] == 0)
        return {
            "filename": path.name,
            "sha256": sha256(path),
            "dimensions": {"width": width, "height": height},
            "any_channel_clipped_pixels": clipped,
            "any_channel_clipped_fraction": clipped / total_pixels,
            "channel_samples_at_255": sum(channel_clips),
            "channel_samples_at_255_fraction": sum(channel_clips) / (total_pixels * 3),
            "roi_xywh": roi,
            "roi_exact_black_fraction": black_pixels / roi_pixels,
            "roi_encoded_srgb_mean_0_255": encoded,
            "roi_srgb_decoded_linear_mean_0_1": linear,
            "roi_encoded_srgb_channel_quantiles_0_255": quantiles
        }
    finally:
        image.close()


def crop_bytes(image: Image.Image, roi: list[int]) -> bytes:
    x, y, w, h = roi
    cropped = image.crop((x, y, x + w, y + h))
    try:
        return cropped.tobytes()
    finally:
        cropped.close()


def mode_for(filename: str):
    stem = Path(filename).stem
    for regex, name in ((LEGACY_RE, "custom-legacy"), (EXPLICIT_RE, "custom-explicit-f0-0.04"),
                        (STANDARD_RE, "standard-reference")):
        match = regex.fullmatch(stem)
        if match:
            specular = float(match.group(1))
            return {"name": name, "light_specular": specular}
    if stem.startswith("diagnostic-"):
        return {"name": "emissive-diagnostic", "light_specular": None}
    return {"name": "unclassified", "light_specular": None}


def linear_mae(left: dict, right: dict) -> float:
    return sum(abs(left["roi_srgb_decoded_linear_mean_0_1"][channel] -
                   right["roi_srgb_decoded_linear_mean_0_1"][channel]) for channel in "RGB") / 3.0


def analyze(input_dir: Path, roi: list[int]) -> dict:
    pngs = sorted(input_dir.glob("*.png"), key=lambda path: path.name.casefold())
    if not pngs:
        raise ValueError("No PNG frames found in " + str(input_dir))
    metadata_path = input_dir / "metadata.json"
    metadata = json.loads(metadata_path.read_text(encoding="utf-8")) if metadata_path.is_file() else None
    frames = []
    size = None
    for path in pngs:
        summary = image_summary(path, roi)
        dims = (summary["dimensions"]["width"], summary["dimensions"]["height"])
        if size is None:
            size = dims
        elif dims != size:
            raise ValueError(f"PNG dimensions differ: {path.name} has {dims}, expected {size}")
        mode = mode_for(path.name)
        summary["mode"] = mode
        if mode["name"] in ("custom-legacy", "custom-explicit-f0-0.04"):
            at_zero = mode["light_specular"] == 0.0
            observed_black = summary["roi_exact_black_fraction"] == 1.0
            expected_control = observed_black if at_zero else not observed_black
            summary["measurement_quality"] = {
                "expected_black_control": at_zero,
                "observed_roi_fully_black": observed_black,
                "control_state_matches_expectation": expected_control,
                "whole_image_unclipped": summary["any_channel_clipped_pixels"] == 0,
                "eligible_as_clean_measurement": expected_control and summary["any_channel_clipped_pixels"] == 0
            }
        else:
            summary["measurement_quality"] = {"eligible_as_clean_measurement": summary["any_channel_clipped_pixels"] == 0,
                                                "whole_image_unclipped": summary["any_channel_clipped_pixels"] == 0}
        summary["metadata_matches"] = metadata_records(metadata, path.name) if metadata is not None else []
        frames.append(summary)
    if metadata is not None:
        fixed_exposure = metadata.get("exposure") == 1.0 and metadata.get("autoExposure") is False
        linear_tonemap = str(metadata.get("tonemap", "")).casefold() == "linear"
    else:
        fixed_exposure = linear_tonemap = False
    metadata_assumptions = {"fixed_exposure_1": fixed_exposure,
                            "auto_exposure_disabled": fixed_exposure,
                            "linear_tonemap_declared": linear_tonemap,
                            "linear_png_decode_interpretation_eligible": fixed_exposure and linear_tonemap}
    explicit = {frame["mode"]["light_specular"]: frame for frame in frames
                if frame["mode"]["name"] == "custom-explicit-f0-0.04"}
    explicit_zero = explicit.get(0.0)
    explicit_scaling = []
    for light_specular in (0.25, 0.5, 1.0):
        frame = explicit.get(light_specular)
        base_signal = explicit_zero["roi_srgb_decoded_linear_mean_0_1"] if explicit_zero else None
        signal = frame["roi_srgb_decoded_linear_mean_0_1"] if frame else None
        zero_ok = bool(explicit_zero and explicit_zero["measurement_quality"]["eligible_as_clean_measurement"] and
                       explicit_zero["roi_exact_black_fraction"] == 1.0)
        positive_ok = bool(frame and frame["measurement_quality"]["eligible_as_clean_measurement"] and
                           frame["roi_exact_black_fraction"] < 1.0)
        unclipped = bool(frame and explicit_zero and
                         frame["any_channel_clipped_pixels"] == 0 and explicit_zero["any_channel_clipped_pixels"] == 0)
        eligible = bool(frame and explicit_zero and zero_ok and positive_ok and unclipped and
                        metadata_assumptions["linear_png_decode_interpretation_eligible"])
        response = None
        if frame and explicit_zero and light_specular > 0:
            response = {channel: max(0.0, signal[channel] - base_signal[channel]) / light_specular
                        for channel in "RGB"}
        explicit_scaling.append({
            "light_specular": light_specular,
            "frame_present": frame is not None,
            "zero_control_black_expected_and_observed": zero_ok,
            "positive_control_nonblack_expected_and_observed": positive_ok,
            "pair_unclipped": unclipped,
            "linear_response_above_zero_normalized_by_light_specular": response if eligible else None,
            "claim_eligible_under_declared_linear_fixed_exposure_assumptions": eligible
        })
    normalized = [item["linear_response_above_zero_normalized_by_light_specular"] for item in explicit_scaling]
    reference_response = next((item for item in normalized if item is not None), None)
    for item in explicit_scaling:
        response = item["linear_response_above_zero_normalized_by_light_specular"]
        item["relative_to_first_eligible_normalized_response"] = (
            {channel: response[channel] / reference_response[channel] if reference_response[channel] else None
             for channel in "RGB"} if response is not None and reference_response is not None else None)
    legacy = []
    for light_specular in SPECULARS:
        frame = next((f for f in frames if f["mode"]["name"] == "custom-legacy" and
                      f["mode"]["light_specular"] == light_specular), None)
        amount = min(1.0, max(0.0, 2.0 * light_specular))
        legacy_f0 = 0.018 + (0.055 - 0.018) * amount
        legacy.append({"light_specular": light_specular, "frame_present": frame is not None,
                       "expected_specular_amount": 2.0 * light_specular,
                       "clamped_legacy_f0_from_include": legacy_f0,
                       "roi_encoded_srgb_mean_0_255": frame["roi_encoded_srgb_mean_0_255"] if frame else None,
                       "any_channel_clipped_fraction": frame["any_channel_clipped_fraction"] if frame else None,
                       "interpretation": "observed PNG response is descriptive; no raw-radiance or exact BRDF claim"})
    return {
        "schema_version": 1,
        "title": "Custom direct-light specular diagnostic metrics",
        "input_directory": str(input_dir),
        "frame_order": "case-insensitive lexical filename order",
        "roi_xywh": roi,
        "metadata": {"path": str(metadata_path), "sha256": sha256(metadata_path), "document": metadata} if metadata is not None else None,
        "declared_assumptions": metadata_assumptions,
        "frames": frames,
        "custom_explicit_f0_scaling": explicit_scaling,
        "custom_legacy_f0_coupling": legacy,
        "standard_material_note": "StandardMaterial reference contains diffuse response and is not BRDF-equivalent to the custom specular-only path.",
        "interpretation_limits": [
            "PNG values are quantized and sRGB encoded; sRGB decoding estimates display-linear values but does not prove raw incident radiance.",
            "Response scaling is only marked eligible when metadata declares fixed exposure 1.0, auto exposure disabled, linear tonemap, both pair frames unclipped, expected black zero control, and nonblack positive control.",
            "Unexpected clipping or black controls suppress eligible scaling fields; measurements remain visible for diagnosis.",
            "Legacy F0 values use mix(0.018,0.055,clamp(2*light_specular,0,1)); expected .0365 at .25 and .055 at .5 and 1.0.",
            "StandardMaterial references include diffuse and are not an equivalent BRDF comparison.",
            "No automatic causal verdict, visual quality verdict, or exact raw-radiance claim is produced."
        ]
    }


def self_check():
    with tempfile.TemporaryDirectory(prefix="light-metrics-self-check-") as temp:
        root = Path(temp)
        frames_dir = root / "frames"
        frames_dir.mkdir()
        roi = [0, 0, 2, 2]
        linear_signals = {0.0: 0.0, 0.25: 0.01, 0.5: 0.02, 1.0: 0.04}
        for spec, value in linear_signals.items():
            encoded_float = 12.92 * value if value <= 0.0031308 else 1.055 * (value ** (1 / 2.4)) - 0.055
            encoded = round(encoded_float * 255)
            image = Image.new("RGB", (2, 2), (encoded, encoded, encoded))
            name = f"custom-explicit-f0-0.04-light-specular-{spec:.2f}.png"
            image.save(frames_dir / name)
            image.close()
        metadata = {"exposure": 1.0, "autoExposure": False, "tonemap": "Linear",
                    "captures": [{"file": f"custom-explicit-f0-0.04-light-specular-{spec:.2f}.png"}
                                 for spec in SPECULARS]}
        (frames_dir / "metadata.json").write_text(json.dumps(metadata), encoding="utf-8")
        report = analyze(frames_dir, roi)
        results = report["custom_explicit_f0_scaling"]
        normalized = [item["linear_response_above_zero_normalized_by_light_specular"]["R"] for item in results]
        if not all(item["claim_eligible_under_declared_linear_fixed_exposure_assumptions"] for item in results):
            raise AssertionError("synthetic linear controls should be eligible")
        if max(normalized) - min(normalized) > 0.006:
            raise AssertionError(f"encoded/decoded linear scale exceeds quantization tolerance: {normalized}")
        if not all(math.isclose(item["relative_to_first_eligible_normalized_response"]["R"], 1.0, abs_tol=.06)
                   for item in results):
            raise AssertionError("normalized explicit signal ratios did not remain near one")
        # A clipped frame must suppress scaling eligibility.
        clipped = Image.new("RGB", (2, 2), (255, 255, 255))
        clipped.save(frames_dir / "custom-explicit-f0-0.04-light-specular-1.00.png")
        clipped.close()
        gated = analyze(frames_dir, roi)["custom_explicit_f0_scaling"][-1]
        if gated["claim_eligible_under_declared_linear_fixed_exposure_assumptions"] or gated["linear_response_above_zero_normalized_by_light_specular"] is not None:
            raise AssertionError("clipped signal did not suppress evidence claim")
        black_positive = Image.new("RGB", (2, 2), (0, 0, 0))
        black_positive.save(frames_dir / "custom-explicit-f0-0.04-light-specular-0.50.png")
        black_positive.close()
        unexpected = analyze(frames_dir, roi)["custom_explicit_f0_scaling"][1]
        if unexpected["claim_eligible_under_declared_linear_fixed_exposure_assumptions"] or unexpected["positive_control_nonblack_expected_and_observed"]:
            raise AssertionError("unexpected black positive control did not suppress evidence claim")
        cli_output = root / "analysis"
        run = subprocess.run([sys.executable, str(Path(__file__).resolve()), "--input", str(frames_dir),
                              "--roi", "0", "0", "2", "2", "--output", str(cli_output)],
                            text=True, capture_output=True)
        if run.returncode != 0 or "LIGHT_METRICS_OK" not in run.stdout or not (cli_output / "light-analysis.json").is_file():
            raise AssertionError("CLI end-to-end check failed: " + run.stderr)
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
    print("LIGHT_METRICS_SELF_CHECK_OK linear_1x2x4=within_quantization clipped_and_black_claims=suppressed cli_report=written output_preserved=1")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, help="directory containing PNG captures and optional metadata.json")
    parser.add_argument("--output", type=Path, help="new output directory; existing path is refused")
    parser.add_argument("--roi", type=int, nargs=4, default=[700, 350, 200, 200], metavar=("X", "Y", "W", "H"),
                        help="center measurement ROI (default: 700 350 200 200)")
    parser.add_argument("--self-check", action="store_true", help="run tiny synthetic linear-scaling and clipping checks")
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
        report = analyze(input_dir, args.roi)
    except (FileExistsError, ValueError, OSError, json.JSONDecodeError) as exc:
        parser.error(str(exc))
    output.mkdir(parents=True)
    (output / "light-analysis.json").write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(f"LIGHT_METRICS_OK {output} frames={len(report['frames'])}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
