#!/usr/bin/env python3
"""Validate and summarize relative-engine family diagnostic profiles."""
from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import tempfile
from pathlib import Path

ROOT = Path(__file__).with_name("family-profiles.json")
TOP_KEYS = {"schema_version", "units", "profiles"}
PROFILE_KEYS = {"id", "light_mode", "material_mode", "exposure", "sun_energy", "ambient_energy", "f0", "specular_gain", "transmission_gain", "phase_gain", "diffuse_wrap", "anisotropy"}
LIGHT_MODES = {"canopy_only", "canopy_key", "key_only"}
MATERIAL_MODES = {"production", "reflection_only"}
LIMITS = {
    "exposure": (0.0, None, True), "sun_energy": (0.0, 10.0, False),
    "ambient_energy": (0.0, 10.0, False), "f0": (0.0, 0.16, False),
    "specular_gain": (0.0, 2.0, False), "transmission_gain": (0.0, 4.0, False),
    "phase_gain": (0.0, 4.0, False), "diffuse_wrap": (0.0, 1.0, False),
    "anisotropy": (0.0, 0.96, False),
}
ID_RE = re.compile(r"^(canopy-only|canopy-key|key-only)-(production|reflection-only)$")
MODE_PREFIX = {"canopy_only": "canopy-only", "canopy_key": "canopy-key", "key_only": "key-only"}


class ProfileError(ValueError):
    pass


def reject_constant(value: str):
    raise ProfileError(f"non-finite JSON constant {value!r} is forbidden")


def reject_duplicate_keys(pairs):
    result = {}
    for key, value in pairs:
        if key in result:
            raise ProfileError(f"duplicate JSON object key {key!r}")
        result[key] = value
    return result


def finite_number(value):
    if type(value) not in (int, float):
        return False
    try:
        return math.isfinite(float(value))
    except (OverflowError, ValueError):
        return False


def validate(data):
    if type(data) is not dict or set(data) != TOP_KEYS:
        raise ProfileError(f"top-level fields must be exactly {sorted(TOP_KEYS)}")
    if type(data["schema_version"]) is not int or data["schema_version"] != 1:
        raise ProfileError("schema_version must be integer 1")
    if data["units"] != "relative_engine":
        raise ProfileError("units must be exactly 'relative_engine'")
    profiles = data["profiles"]
    if type(profiles) is not list or len(profiles) != 6:
        raise ProfileError("profiles must contain exactly six entries")
    seen = set()
    by_mode = {}
    normalized = []
    for index, p in enumerate(profiles):
        where = f"profiles[{index}]"
        if type(p) is not dict or set(p) != PROFILE_KEYS:
            raise ProfileError(f"{where} fields must be exactly {sorted(PROFILE_KEYS)}")
        if type(p["id"]) is not str or not ID_RE.fullmatch(p["id"]):
            raise ProfileError(f"{where}.id is malformed or unsafe")
        if p["id"] in seen:
            raise ProfileError(f"duplicate profile id {p['id']!r}")
        seen.add(p["id"])
        if (type(p["light_mode"]) is not str or p["light_mode"] not in LIGHT_MODES
                or type(p["material_mode"]) is not str or p["material_mode"] not in MATERIAL_MODES):
            raise ProfileError(f"{where} has an unknown light/material mode")
        expected_id = MODE_PREFIX[p["light_mode"]] + "-" + ("production" if p["material_mode"] == "production" else "reflection-only")
        if p["id"] != expected_id:
            raise ProfileError(f"{where}.id does not match its declared modes")
        q = {"id": p["id"], "light_mode": p["light_mode"], "material_mode": p["material_mode"]}
        for field, (low, high, exclusive_low) in LIMITS.items():
            value = p[field]
            if not finite_number(value):
                raise ProfileError(f"{where}.{field} must be a finite number (booleans are not numbers here)")
            value = float(value)
            if (value <= low if exclusive_low else value < low) or (high is not None and value > high):
                left = ">" if exclusive_low else ">="
                raise ProfileError(f"{where}.{field} must be {left} {low} and <= {high}")
            q[field] = value
        if p["light_mode"] == "key_only" and q["ambient_energy"] != 0.0:
            raise ProfileError(f"{where}: key_only requires ambient_energy=0")
        if p["light_mode"] == "canopy_only" and q["sun_energy"] != 0.0:
            raise ProfileError(f"{where}: canopy_only requires sun_energy=0")
        if p["material_mode"] == "reflection_only" and any(q[k] != 0.0 for k in ("transmission_gain", "phase_gain", "diffuse_wrap")):
            raise ProfileError(f"{where}: reflection_only requires transmission_gain, phase_gain, and diffuse_wrap to be zero")
        by_mode[(p["light_mode"], p["material_mode"])] = q
        normalized.append(q)
    expected = {(light, material) for light in LIGHT_MODES for material in MATERIAL_MODES}
    if set(by_mode) != expected:
        raise ProfileError("profiles must cover each of the six light/material combinations exactly once")
    # Enforce isolation: the paired material profiles vary only the three named controls.
    material_controls = {"transmission_gain", "phase_gain", "diffuse_wrap"}
    for light in LIGHT_MODES:
        a = by_mode[(light, "production")]
        b = by_mode[(light, "reflection_only")]
        if any(a[k] != b[k] for k in PROFILE_KEYS - {"id", "material_mode"} - material_controls):
            raise ProfileError(f"{light}: material pair must hold all non-transmission controls fixed")
    # Lighting pairs are fixed per material mode; only the sun/ambient controls may change.
    for material in MATERIAL_MODES:
        pairs = [by_mode[(light, material)] for light in LIGHT_MODES]
        for other in pairs[1:]:
            if any(pairs[0][k] != other[k] for k in PROFILE_KEYS - {"id", "light_mode", "sun_energy", "ambient_energy"}):
                raise ProfileError(f"{material}: light pair must hold material controls fixed")
    normalized.sort(key=lambda p: p["id"])
    return normalized


def load(path: Path):
    raw = path.read_text(encoding="utf-8")
    try:
        data = json.loads(raw, parse_constant=reject_constant, object_pairs_hook=reject_duplicate_keys)
    except (json.JSONDecodeError, ProfileError, RecursionError, ValueError) as exc:
        raise ProfileError(f"invalid JSON: {exc}") from exc
    return validate(data)


def canonical_hash(profiles):
    payload = json.dumps({"schema_version": 1, "units": "relative_engine", "profiles": profiles}, sort_keys=True, separators=(",", ":"), allow_nan=False).encode("utf-8")
    return hashlib.sha256(payload).hexdigest()


def self_check():
    base = json.loads(ROOT.read_text(encoding="utf-8"))
    validate(base)
    cases = []
    def altered(label, mutate):
        sample = json.loads(json.dumps(base))
        mutate(sample)
        cases.append((label, sample))
    altered("negative gain", lambda d: d["profiles"][0].__setitem__("specular_gain", -0.1))
    altered("non-finite", lambda d: d["profiles"][0].__setitem__("f0", float("nan")))
    altered("boolean numeric", lambda d: d["profiles"][0].__setitem__("exposure", True))
    altered("unknown field", lambda d: d["profiles"][0].__setitem__("unmodeled", 1))
    altered("duplicate id", lambda d: d["profiles"][1].__setitem__("id", d["profiles"][0]["id"]))
    altered("unsupported units", lambda d: d.__setitem__("units", "lux"))
    altered("unknown mode", lambda d: d["profiles"][0].__setitem__("light_mode", "studio"))
    altered("list mode", lambda d: d["profiles"][0].__setitem__("light_mode", []))
    altered("object material mode", lambda d: d["profiles"][0].__setitem__("material_mode", {}))
    altered("overflowing integer", lambda d: d["profiles"][0].__setitem__("specular_gain", 10**10000))
    altered("false key-only claim", lambda d: d["profiles"][4].__setitem__("ambient_energy", 0.1))
    altered("false reflection-only claim", lambda d: d["profiles"][1].__setitem__("transmission_gain", 0.2))
    altered("false canopy-only claim", lambda d: d["profiles"][0].__setitem__("sun_energy", 0.2))
    for label, sample in cases:
        try:
            validate(sample)
        except ProfileError:
            pass
        else:
            raise AssertionError(f"self-check failed to reject {label}")
    # Exercise on-disk loading and prove the validator does not rewrite an input.
    with tempfile.TemporaryDirectory(prefix="family-profile-check-") as tmp:
        p = Path(tmp) / "input.json"
        content = ROOT.read_bytes()
        p.write_bytes(content)
        before = hashlib.sha256(p.read_bytes()).digest()
        load(p)
        after = hashlib.sha256(p.read_bytes()).digest()
        if before != after or p.read_bytes() != content:
            raise AssertionError("validator modified its input")
        duplicate_key_bytes = content.replace(b'"schema_version": 1', b'"schema_version": 1, "schema_version": 1', 1)
        p.write_bytes(duplicate_key_bytes)
        try:
            load(p)
        except ProfileError:
            pass
        else:
            raise AssertionError("self-check failed to reject duplicate JSON key")
        if p.read_bytes() != duplicate_key_bytes:
            raise AssertionError("duplicate-key check modified its input")
    print(f"self-check passed: {len(cases) + 1} malformed cases rejected; input preserved")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, default=ROOT, help="manifest JSON (read-only; defaults to family-profiles.json)")
    parser.add_argument("--self-check", action="store_true", help="check malformed cases and input preservation")
    args = parser.parse_args()
    try:
        if args.self_check:
            self_check()
        profiles = load(args.input)
    except (OSError, ProfileError, AssertionError) as exc:
        parser.error(str(exc))
    summary = {"schema_version": 1, "units": "relative_engine", "profile_count": len(profiles), "profiles": profiles, "normalized_sha256": canonical_hash(profiles), "interpretation": "relative engine diagnostics; not physically calibrated values or measured photorealism"}
    print(json.dumps(summary, indent=2, sort_keys=True))


if __name__ == "__main__":
    main()
