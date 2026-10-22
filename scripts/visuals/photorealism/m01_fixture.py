#!/usr/bin/env python3
"""Plan/run bounded M01 native captures and seal machine-readable provenance.

This orchestrates existing Godot review modes. It does not add runtime controls or
claim that separate native captures are a same-frame multi-material fixture.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
from datetime import datetime, timezone


SCHEMA = 1
MODES = ("material-port-review", "world-review")


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def git(repo: Path, *args: str) -> str:
    return subprocess.check_output(["git", "-C", str(repo), *args], text=True).strip()


def source_inventory(repo: Path) -> dict:
    """Hash relevant code/docs as present, including local and untracked edits."""
    paths = git(repo, "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--",
                "game", "src", "scripts/visuals/photorealism", "docs/2026-10-21-vivarium-photorealism-research.md",
                "docs/visual-lab/RUNNER.md").split("\0")
    entries = []
    tree = hashlib.sha256()
    for rel in sorted(p for p in paths if p):
        p = repo / rel
        if not p.is_file():
            continue
        digest = sha256(p)
        entries.append({"path": rel.replace("\\", "/"), "sha256": digest})
        tree.update(rel.replace("\\", "/").encode("utf-8") + b"\0" + digest.encode("ascii") + b"\n")
    return {"revision": git(repo, "rev-parse", "HEAD"), "dirty": bool(git(repo, "status", "--porcelain")),
            "relevant_source_file_count": len(entries), "relevant_source_sha256": tree.hexdigest(), "files": entries}


def atomic_json(path: Path, data: dict) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + ".tmp")
    temp.write_text(json.dumps(data, indent=2) + "\n", encoding="utf-8")
    os.replace(temp, path)


def require_new_output(path: Path) -> None:
    if path.exists():
        raise FileExistsError("output already exists; choose a new path to preserve prior captures: " + str(path))


def commands(repo: Path, godot: Path, baseline: Path, root: Path, modes: list[str]) -> list[list[str]]:
    out = []
    for mode in modes:
        dest = root / mode
        args = [str(godot), "--path", str(repo / "game"), "--", f"--{mode}", str(dest)]
        if mode == "material-port-review":
            args += ["--material-baseline", str(baseline)]
        out.append(args)
    return out


def validate_manifest(data: dict, repo: Path) -> None:
    required = {"schema_version", "fixture_id", "world", "camera", "exposure", "light", "readiness", "requested_modes"}
    missing = sorted(required - data.keys())
    if missing:
        raise ValueError("manifest missing: " + ", ".join(missing))
    if data["schema_version"] != SCHEMA:
        raise ValueError("unsupported schema_version")
    if not isinstance(data["fixture_id"], str) or not data["fixture_id"].strip():
        raise ValueError("fixture_id must be non-empty")
    modes = data["requested_modes"]
    if not isinstance(modes, list) or not modes or any(m not in MODES for m in modes) or len(set(modes)) != len(modes):
        raise ValueError("requested_modes must be a unique non-empty subset of " + ", ".join(MODES))
    if not isinstance(data["camera"], dict) or not isinstance(data["exposure"], dict) or not isinstance(data["light"], dict):
        raise ValueError("camera, exposure and light must be objects")
    # Do not let a user-supplied manifest imply controls that current source does not expose.
    unsupported = data.get("claimed_runtime_overrides", [])
    if unsupported:
        raise ValueError("runtime override claims unsupported by current native interfaces: " + ", ".join(unsupported))


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument("--repo", type=Path, default=Path(__file__).resolve().parents[3])
    ap.add_argument("--manifest", type=Path, help="fixture JSON manifest")
    ap.add_argument("--godot", type=Path, help="Godot 4.7.1 Mono console executable")
    ap.add_argument("--baseline", type=Path, help="protected baseline/game/Shaders directory")
    ap.add_argument("--output", type=Path, help="new run directory; existing path is refused")
    ap.add_argument("--dry-run", action="store_true", help="validate and print native commands without launching")
    ap.add_argument("--self-check", action="store_true", help="focused manifest and no-overwrite checks")
    ap.add_argument("--material-port-review", action="store_true", help="include existing material-port native mode")
    ap.add_argument("--world-review", action="store_true", help="include existing world-review native mode")
    a = ap.parse_args()
    repo = a.repo.resolve()
    if a.self_check:
        sample = {"schema_version": 1, "fixture_id": "m01-self-check", "world": {"source": "saved or disposable"},
                  "camera": {"control": "native mode specific"}, "exposure": {"control": "native mode specific"},
                  "light": {"control": "native mode specific"}, "readiness": {"required": ["capture files present"]},
                  "requested_modes": ["world-review"]}
        validate_manifest(sample, repo)
        for bad in (dict(sample, requested_modes=["invented-mode"]), dict(sample, claimed_runtime_overrides=["fixed exposure in world-review"])):
            try:
                validate_manifest(bad, repo)
            except ValueError:
                pass
            else:
                raise AssertionError("invalid manifest accepted")
        with tempfile.TemporaryDirectory(prefix="m01-self-check-") as temp:
            occupied = Path(temp) / "occupied"
            occupied.mkdir()
            sentinel = occupied / "preserve.txt"
            sentinel.write_text("preserve", encoding="utf-8")
            try:
                require_new_output(occupied)
            except FileExistsError:
                pass
            else:
                raise AssertionError("existing output accepted")
            if sentinel.read_text(encoding="utf-8") != "preserve":
                raise AssertionError("existing output contents changed")
        print("M01_MANIFEST_SELF_CHECK_OK schema=1 invalid_modes=rejected unsupported_claims=rejected existing_output=preserved")
        return 0
    if not a.manifest or not a.output or not a.godot or not a.baseline:
        ap.error("--manifest, --output, --godot, and --baseline are required outside --self-check")
    manifest_path = a.manifest.resolve()
    manifest = json.loads(manifest_path.read_text(encoding="utf-8"))
    validate_manifest(manifest, repo)
    modes = [m for m, enabled in ((MODES[0], a.material_port_review), (MODES[1], a.world_review)) if enabled]
    if not modes:
        modes = manifest["requested_modes"]
    if set(modes) != set(manifest["requested_modes"]):
        ap.error("CLI-selected modes must match manifest requested_modes")
    out_root = a.output.resolve()
    try:
        require_new_output(out_root)
    except FileExistsError as ex:
        ap.error(str(ex))
    godot, baseline = a.godot.resolve(), a.baseline.resolve()
    if not a.dry_run:
        if not godot.is_file():
            ap.error("Godot executable not found: " + str(godot))
        if not baseline.is_dir():
            ap.error("baseline shader directory not found: " + str(baseline))
    cmds = commands(repo, godot, baseline, out_root, modes)
    for cmd in cmds:
        print(subprocess.list2cmdline(cmd))
    if a.dry_run:
        print("DRY_RUN_ONLY native_capture=not_executed runtime_controls=not_claimed")
        return 0
    if out_root.exists():
        ap.error("output appeared before run; refusing overwrite")
    out_root.mkdir(parents=True)
    start = datetime.now(timezone.utc).isoformat()
    assembly_paths = [repo / "game/.godot/mono/temp/bin/Debug/Vivarium.dll",
                      repo / "game/.godot/mono/temp/bin/Debug/Vivarium.Sim.dll"]
    provenance = {"schema_version": SCHEMA, "fixture_id": manifest["fixture_id"], "started_utc": start,
                  "source_scope": ["game/", "src/", "scripts/visuals/photorealism/",
                                   "docs/2026-10-21-vivarium-photorealism-research.md", "docs/visual-lab/RUNNER.md"],
                  "source": source_inventory(repo), "manifest_sha256": sha256(manifest_path),
                  "godot_executable": str(godot), "godot_executable_sha256": sha256(godot),
                  "runtime_assemblies": [{"path": str(p), "sha256": sha256(p) if p.is_file() else None,
                                          "present": p.is_file()} for p in assembly_paths],
                  "baseline_directory": str(baseline), "baseline_files": {p.name: sha256(p) for p in sorted(baseline.glob("*.gdshader"))},
                  "world": manifest["world"], "declared_camera": manifest["camera"],
                  "declared_exposure": manifest["exposure"], "declared_light": manifest["light"],
                  "readiness_policy": manifest["readiness"], "requested_modes": modes,
                  "control_limitations": ["world-review uses native fixed light phase 420 and 1600x900; shader TIME is not pinned",
                      "world-review uses disposable initial world, not a mature ecology", "material-port-review uses its own fixed views/light/exposure and synthetic material fixture",
                      "the two modes are separate processes and do not show all requested materials under one identical light/camera",
                      "native modes do not expose arbitrary camera/exposure/light/material override values through CLI"]}
    atomic_json(out_root / "m01-provenance.json", provenance)
    results = []
    for cmd, mode in zip(cmds, modes):
        p = subprocess.run(cmd, cwd=repo, text=True, capture_output=True, check=False)
        marker = "MATERIAL_PORT_REVIEW_OK" if mode == "material-port-review" else "VIVARIUM_WORLDREVIEW_OK"
        log_path = out_root / (mode + ".log")
        log_path.write_text(p.stdout + "\n--- STDERR ---\n" + p.stderr, encoding="utf-8")
        result = {"mode": mode, "command": cmd, "exit_code": p.returncode,
                  "log_path": str(log_path.relative_to(out_root)).replace("\\", "/"),
                  "success_marker": marker, "success_marker_seen": marker in p.stdout}
        capdir = out_root / mode
        files = sorted(f for f in capdir.rglob("*") if f.is_file()) if capdir.exists() else []
        result["outputs"] = [{"path": str(f.relative_to(out_root)).replace("\\", "/"), "sha256": sha256(f)} for f in files]
        result["readiness"] = {"output_directory_exists": capdir.is_dir(), "files_written": len(files),
                               "native_success_marker": marker in p.stdout}
        results.append(result)
        if p.returncode:
            break
    receipt = {"schema_version": SCHEMA, "fixture_id": manifest["fixture_id"], "started_utc": start,
               "finished_utc": datetime.now(timezone.utc).isoformat(), "provenance": provenance,
               "captures": results, "complete": len(results) == len(modes) and all(r["exit_code"] == 0 and r["outputs"] and r["success_marker_seen"] for r in results),
               "claims": {"same_frame_all_materials": False, "calibrated_photographic_match": False,
                          "performance_evidence": False, "aesthetic_acceptance": False}}
    atomic_json(out_root / "receipt.json", receipt)
    return 0 if receipt["complete"] else 2


if __name__ == "__main__":
    raise SystemExit(main())
