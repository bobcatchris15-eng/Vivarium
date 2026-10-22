#!/usr/bin/env python3
"""Analyze relative exposure response in the bounded physical-camera fixture."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
from pathlib import Path
import tempfile

try:
    from PIL import Image, ImageChops, ImageStat
except ImportError as exc:
    raise SystemExit("Pillow is required: python -m pip install Pillow") from exc

EXPECTED = {"baseline", "half-lux", "double-lux", "shutter-120", "iso-200", "aperture-f5.657", "compensated-double-lux-shutter-120", "baseline-repeat"}
EXPECTED_FILES = {name + ".png" for name in EXPECTED}
BASELINE = "baseline"
REPEAT = "baseline-repeat"
NOMINAL_RATIOS = {"half-lux": .5, "double-lux": 2.0, "shutter-120": .5, "iso-200": 2.0,
                  "aperture-f5.657": (4.0 / 5.657) ** 2,
                  "compensated-double-lux-shutter-120": 1.0, "baseline-repeat": 1.0}


class EvidenceError(ValueError):
    pass


def digest(path):
    h = hashlib.sha256()
    with path.open("rb") as f:
        for chunk in iter(lambda: f.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def number(obj, key, where):
    v = obj.get(key) if isinstance(obj, dict) else None
    try:
        finite = type(v) in (int, float) and math.isfinite(float(v))
    except (OverflowError, ValueError):
        finite = False
    if not finite:
        raise EvidenceError(f"{where}.{key} must be a finite number")
    return float(v)


def decode_srgb(c):
    c /= 255.0
    return c / 12.92 if c <= .04045 else ((c + .055) / 1.055) ** 2.4


def load_png(path):
    with Image.open(path) as im:
        if im.format != "PNG":
            raise EvidenceError(f"expected PNG: {path.name}")
        rgba = im.convert("RGBA")
        try:
            alpha = rgba.getchannel("A")
            try:
                if alpha.getextrema() != (255, 255):
                    raise EvidenceError(f"PNG must be fully opaque: {path.name}")
            finally:
                alpha.close()
        finally:
            rgba.close()
        return im.convert("RGB")


def exposure_factor(record):
    controls = record.get("exposure")
    if not isinstance(controls, dict):
        raise EvidenceError(f"{record.get('condition')}: exposure must be an object")
    fstop = number(controls, "f_stop", "exposure")
    shutter = number(controls, "shutter_denominator", "exposure")
    iso = number(controls, "iso", "exposure")
    multiplier = number(controls, "multiplier", "exposure")
    if min(fstop, shutter, iso, multiplier) <= 0:
        raise EvidenceError(f"{record.get('condition')}: exposure controls must be positive")
    lux = number(record, "directional_lux", str(record.get("condition")))
    if lux <= 0:
        raise EvidenceError(f"{record.get('condition')}: directional_lux must be positive")
    # Exposure factor expected by a linear camera pipeline: lux * ISO * multiplier / (N^2 * shutter denominator).
    ev100 = math.log2(fstop * fstop * shutter) - math.log2(iso / 100.0)
    declared_ev = number(controls, "ev100", "exposure")
    if abs(ev100 - declared_ev) > 0.02:
        raise EvidenceError(f"{record.get('condition')}: declared EV100 disagrees with f-stop/shutter/ISO ({declared_ev:g} vs {ev100:g})")
    return lux * iso * multiplier / (fstop * fstop * shutter), ev100


def roi_metrics(image, roi):
    if not isinstance(roi, dict):
        raise EvidenceError("each capture must declare ROI coordinates")
    vals = [roi.get("x"), roi.get("y"), roi.get("width"), roi.get("height")]
    if any(type(v) is not int for v in vals):
        raise EvidenceError("ROI x/y/width/height must be integers")
    x, y, w, h = vals
    if x < 0 or y < 0 or w <= 0 or h <= 0 or x + w > image.width or y + h > image.height:
        raise EvidenceError(f"ROI {vals} falls outside {image.width}x{image.height}")
    crop = image.crop((x, y, x + w, y + h))
    try:
        channels = crop.split()
        try:
            hist = [ch.histogram() for ch in channels]
            pixels = w * h
            linear = [sum(decode_srgb(i) * count for i, count in enumerate(ch)) / pixels for ch in hist]
            luma = .2126 * linear[0] + .7152 * linear[1] + .0722 * linear[2]
            clip_pixels = sum(1 for px in crop.getdata() if max(px) == 255)
            black_pixels = sum(1 for px in crop.getdata() if px == (0, 0, 0))
            channel_clip_samples = sum(ch[255] for ch in hist)
            return {"roi": {"x": x, "y": y, "width": w, "height": h}, "pixel_count": pixels,
                    "encoded_srgb_mean_rgb": [sum(i * count for i, count in enumerate(ch)) / pixels for ch in hist],
                    "decoded_display_linear_rgb_mean": linear,
                    "decoded_display_linear_rec709_mean": luma,
                    "any_channel_clipped_pixels": clip_pixels,
                    "channel_clip_sample_fraction": channel_clip_samples / (pixels * 3),
                    "exact_black_fraction": black_pixels / pixels}
        finally:
            for ch in channels: ch.close()
    finally:
        crop.close()


def encoded_mae(a, b):
    diff = ImageChops.difference(a, b)
    try:
        return sum(ImageStat.Stat(diff).mean) / 3.0
    finally:
        diff.close()


def analyze(input_dir: Path, max_relative_error=.05):
    meta_path = input_dir / "metadata.json"
    if not meta_path.is_file():
        raise EvidenceError("metadata.json is required for physical exposure eligibility")
    try:
        metadata = json.loads(meta_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, UnicodeError) as exc:
        raise EvidenceError(f"invalid metadata.json: {exc}") from exc
    captures = metadata.get("captures") if isinstance(metadata, dict) else None
    if not isinstance(captures, list) or len(captures) != len(EXPECTED):
        raise EvidenceError("metadata must contain exactly eight capture records")
    by_condition = {}
    for rec in captures:
        if not isinstance(rec, dict) or not isinstance(rec.get("condition"), str) or not isinstance(rec.get("file"), str):
            raise EvidenceError("each capture needs string condition description and file fields")
        filename = rec["file"]
        if filename not in EXPECTED_FILES:
            raise EvidenceError(f"unexpected physical fixture filename {filename!r}")
        # The stable file stem is the machine condition key. Preserve the human condition description separately.
        canonical = Path(filename).stem
        if canonical in by_condition:
            raise EvidenceError(f"duplicate physical fixture file for {canonical}")
        by_condition[canonical] = rec
    if set(by_condition) != EXPECTED:
        raise EvidenceError(f"conditions must be exactly {sorted(EXPECTED)}")
    if metadata.get("process_setting_actual") is not True:
        raise EvidenceError("metadata must confirm actual project setting enables physical units")
    restore_path = input_dir / "restore-status.json"
    if not restore_path.is_file():
        raise EvidenceError("restore-status.json is required to verify the project setting was restored")
    try:
        restore_status = json.loads(restore_path.read_text(encoding="utf-8"))
    except (json.JSONDecodeError, UnicodeError) as exc:
        raise EvidenceError(f"invalid restore-status.json: {exc}") from exc
    if not isinstance(restore_status, dict) or restore_status.get("restored") is not True:
        raise EvidenceError("restore-status.json must confirm restored=true")
    frames = {}
    records = {}
    try:
        for condition, rec in by_condition.items():
            filename = Path(rec["file"])
            if filename.name != rec["file"] or filename.suffix.lower() != ".png":
                raise EvidenceError(f"unsafe or non-PNG capture filename: {rec['file']}")
            path = input_dir / filename
            if not path.is_file():
                raise EvidenceError(f"missing capture {rec['file']}")
            image = load_png(path)
            frames[condition] = image
            actual_hash = digest(path)
            declared_hash = rec.get("sha256")
            if declared_hash is not None and (type(declared_hash) is not str or declared_hash.lower() != actual_hash):
                raise EvidenceError(f"metadata SHA256 mismatch for {rec['file']}")
            if rec.get("physical_light_units") is not True:
                raise EvidenceError(f"{condition}: physical light units are not declared enabled")
            if "linear" not in str(rec.get("tonemap", "")).lower():
                raise EvidenceError(f"{condition}: metadata must declare linear tonemapping")
            if len(image.getbands()) != 3:
                raise EvidenceError(f"expected RGB PNG: {rec['file']}")
            albedo = number(rec, "neutral_albedo", condition)
            if abs(albedo - .18) > 1e-6:
                raise EvidenceError(f"{condition}: neutral Lambert albedo must be literal linear 0.18")
            factor, ev = exposure_factor(rec)
            control = roi_metrics(image, rec.get("roi"))
            records[condition] = {"condition": condition, "condition_description": rec["condition"], "file": rec["file"], "sha256": actual_hash, "dimensions": [image.width, image.height], "exposure_factor_from_declared_controls": factor,
                                 "EV100_from_controls": ev, "metrics": control,
                                 "metadata_controls": {k: rec.get(k) for k in ("directional_lux", "exposure", "neutral_albedo", "physical_light_units", "auto_exposure", "tonemap")}}
        dims = {tuple(r["dimensions"]) for r in records.values()}
        rois = {json.dumps(r["metrics"]["roi"], sort_keys=True) for r in records.values()}
        if len(dims) != 1 or len(rois) != 1:
            raise EvidenceError("all captures must have identical dimensions and ROI coordinates")
        if any(by_condition[c].get("auto_exposure") is not False for c in EXPECTED):
            raise EvidenceError("every capture must declare auto_exposure=false")
        baseline = records[BASELINE]
        if baseline["metrics"]["decoded_display_linear_rec709_mean"] <= 0 or baseline["metrics"]["exact_black_fraction"] >= 1:
            raise EvidenceError("baseline ROI is black; exposure comparisons are ineligible")
        baseline_factor = baseline["exposure_factor_from_declared_controls"]
        baseline_luma = baseline["metrics"]["decoded_display_linear_rec709_mean"]
        checks = {}
        for condition, rec in records.items():
            if condition == BASELINE:
                continue
            expected_ratio = rec["exposure_factor_from_declared_controls"] / baseline_factor
            observed_ratio = rec["metrics"]["decoded_display_linear_rec709_mean"] / baseline_luma
            nominal_ratio = NOMINAL_RATIOS[condition]
            controls_match = abs(expected_ratio - nominal_ratio) / nominal_ratio <= .02
            clipped = rec["metrics"]["any_channel_clipped_pixels"] > 0 or baseline["metrics"]["any_channel_clipped_pixels"] > 0
            relative_error = abs(observed_ratio - expected_ratio) / max(abs(expected_ratio), 1e-12)
            eligible = controls_match and not clipped and relative_error <= max_relative_error
            checks[condition] = {"nominal_control_ratio": nominal_ratio, "expected_ratio_from_metadata": expected_ratio,
                                 "metadata_controls_match_nominal": controls_match,
                                 "observed_decoded_display_linear_rec709_ratio": observed_ratio,
                                 "relative_error": relative_error, "clipped": clipped, "within_tolerance": relative_error <= max_relative_error,
                                 "eligible_for_relative_response_comparison": eligible}
        repeat = records[REPEAT]
        repeat_mae = encoded_mae(frames[BASELINE], frames[REPEAT])
        repeat_exact = baseline["sha256"] == repeat["sha256"]
        repeat_pass = repeat_mae == 0.0
        clipping_any = any(r["metrics"]["any_channel_clipped_pixels"] > 0 for r in records.values())
        return {"schema_version": 1, "fixture": "physical-camera-relative-exposure-response", "capture_count": len(records),
                "frames": records, "relative_response_checks_vs_baseline": checks,
                "baseline_repeat": {"sha256_equal": repeat_exact, "encoded_rgb_mae": repeat_mae, "pass": repeat_pass},
                "claim_eligibility": {"unclipped_all_frames": not clipping_any, "baseline_repeat_stable": repeat_pass,
                                      "physical_units_declared": all(by_condition[c].get("physical_light_units") is True for c in EXPECTED),
                                      "linear_tonemap_declared": all("linear" in str(by_condition[c].get("tonemap", "")).lower() for c in EXPECTED),
                                      "process_setting_restored": restore_status["restored"],
                                      "relative_camera_exposure_response": not clipping_any and repeat_pass and restore_status["restored"] and all(x["eligible_for_relative_response_comparison"] for x in checks.values())},
                "interpretation": "Relative exposure response only. Inverse-sRGB PNG measurements are decoded display-linear values after rendering and are not proof of absolute photometric accuracy, calibrated radiance, or universal camera response.",
                "fixture_limitations": ["No depth-of-field enable control is exposed by the installed physical camera API; the measured planar target is at the configured focus distance."],
                "restore_status": restore_status, "metadata": metadata}
    finally:
        for image in frames.values(): image.close()


def make_record(condition, file, lux, fstop=4, shutter=60, iso=100, multiplier=1):
    ev = math.log2(fstop*fstop*shutter) - math.log2(iso/100)
    descriptions = {"baseline": "baseline", "half-lux": "half light illuminance", "double-lux": "double light illuminance", "shutter-120": "half exposure time", "iso-200": "double ISO sensitivity", "aperture-f5.657": "one-stop smaller aperture", "compensated-double-lux-shutter-120": "double lux compensated by half exposure time", "baseline-repeat": "restored baseline repeat"}
    return {"condition": descriptions[condition], "file": file, "roi": {"x": 0, "y": 0, "width": 8, "height": 8}, "physical_light_units": True, "neutral_albedo": .18,
            "auto_exposure": False, "tonemap": "Linear", "directional_lux": lux,
            "exposure": {"f_stop": fstop, "shutter_denominator": shutter, "shutter_seconds": 1/shutter, "iso": iso, "multiplier": multiplier, "ev100": ev}}


def self_check():
    with tempfile.TemporaryDirectory(prefix="physical-metrics-check-") as td:
        root = Path(td)
        settings = {"baseline": (100,4,60,100), "half-lux": (50,4,60,100), "double-lux": (200,4,60,100),
                    "shutter-120": (100,4,120,100), "iso-200": (100,4,60,200), "aperture-f5.657": (100,5.657,60,100),
                    "compensated-double-lux-shutter-120": (200,4,120,100), "baseline-repeat": (100,4,60,100)}
        target = .20
        captures=[]
        for condition, (lux,fstop,shutter,iso) in settings.items():
            rec=make_record(condition, condition+".png", lux, fstop, shutter, iso)
            factor,_=exposure_factor(rec)
            value=min(.9, target*factor/(100*100/(4*4*60)))
            encoded=round(255*(1.055*value**(1/2.4)-.055))
            Image.new("RGB",(8,8),(encoded,encoded,encoded)).save(root/rec["file"])
            rec["sha256"]=digest(root/rec["file"])
            captures.append(rec)
        metadata={"process_setting_actual":True,"process_setting_restored":True,"captures":captures}
        (root/"metadata.json").write_text(json.dumps(metadata),encoding="utf-8")
        (root/"restore-status.json").write_text(json.dumps({"restored":True,"key":"rendering/limits/fixed_fps"}),encoding="utf-8")
        before={p.name:digest(p) for p in root.iterdir() if p.is_file()}
        report=analyze(root)
        assert report["relative_response_checks_vs_baseline"]["half-lux"]["eligible_for_relative_response_comparison"]
        assert report["relative_response_checks_vs_baseline"]["double-lux"]["eligible_for_relative_response_comparison"]
        assert report["relative_response_checks_vs_baseline"]["aperture-f5.657"]["metadata_controls_match_nominal"]
        assert report["frames"]["half-lux"]["condition"] == "half-lux"
        assert report["frames"]["half-lux"]["condition_description"] == "half light illuminance"
        assert report["baseline_repeat"]["pass"]
        assert before=={p.name:digest(p) for p in root.iterdir() if p.is_file()}
        # Declared controls must match the named sweep and a bad ratio must suppress evidence.
        metadata["captures"][1]["directional_lux"] = 40
        (root/"metadata.json").write_text(json.dumps(metadata),encoding="utf-8")
        mismatch=analyze(root)
        assert not mismatch["relative_response_checks_vs_baseline"]["half-lux"]["metadata_controls_match_nominal"]
        assert not mismatch["claim_eligibility"]["relative_camera_exposure_response"]
        metadata["captures"][1]["directional_lux"] = 50
        # Clipping must suppress a response claim.
        Image.new("RGB",(8,8),(255,255,255)).save(root/"double-lux.png")
        metadata["captures"][2]["sha256"]=digest(root/"double-lux.png")
        (root/"metadata.json").write_text(json.dumps(metadata),encoding="utf-8")
        clipped=analyze(root)
        assert not clipped["relative_response_checks_vs_baseline"]["double-lux"]["eligible_for_relative_response_comparison"]
        metadata["captures"][0]["tonemap"]="AgX"
        (root/"metadata.json").write_text(json.dumps(metadata),encoding="utf-8")
        try: analyze(root)
        except EvidenceError: pass
        else: raise AssertionError("unsupported tonemap metadata should fail closed")
        metadata["captures"][0]["tonemap"]="Linear"
        (root/"metadata.json").write_text(json.dumps(metadata),encoding="utf-8")
        (root/"restore-status.json").write_text(json.dumps({"restored":False}),encoding="utf-8")
        try: analyze(root)
        except EvidenceError: pass
        else: raise AssertionError("unrestored project setting should fail closed")
    print("self-check passed: expected relative ratios, exact repeat, bad-control/clipping suppression, metadata gate, input nonmutation")


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input",type=Path)
    parser.add_argument("--output",type=Path)
    parser.add_argument("--max-relative-error",type=float,default=.05)
    parser.add_argument("--self-check",action="store_true")
    args=parser.parse_args()
    if args.self_check: self_check(); return
    if not args.input or not args.output: parser.error("--input and --output are required")
    if not 0 < args.max_relative_error <= .25: parser.error("--max-relative-error must be in (0, .25]")
    if args.output.exists(): parser.error(f"output already exists: {args.output}")
    try:
        report=analyze(args.input,args.max_relative_error)
        args.output.mkdir(parents=True)
        (args.output/"physical-analysis.json").write_text(json.dumps(report,indent=2,sort_keys=True),encoding="utf-8")
    except (OSError,EvidenceError,ValueError) as exc: parser.error(str(exc))
    print(f"wrote {args.output/'physical-analysis.json'}")
    if not report["claim_eligibility"]["relative_camera_exposure_response"]:
        print("relative exposure checks failed or were ineligible; see report", file=__import__("sys").stderr)
        raise SystemExit(2)


if __name__ == "__main__": main()
