import os
import sys
import json
import time
import subprocess
from datetime import datetime

repo = os.path.abspath('.')
godot_exe = os.path.join(repo, r'tools\godot\Godot_v4.7.1-stable_mono_win64\Godot_v4.7.1-stable_mono_win64_console.exe')
save_path = os.path.join(repo, r'build\hitches-20261025\inputs\player-autosave.vivsave')
out_dir = os.path.join(repo, r'build\investigation-20261025\effect-sweep')
os.makedirs(out_dir, exist_ok=True)
gpu_lease_file = open(os.path.join(repo, '.clanker/visual-lab-gpu.lease'), 'r+')

provenance = {
    "run_id": "effect-sweep-" + datetime.utcnow().strftime("%Y%m%dT%H%M%SZ"),
    "source_revision": "head",
    "source_tree_sha256": "0" * 64,
    "build_game_sha256": "0" * 64,
    "source_root": repo,
    "loaded_world_sha256": "0" * 64,
    "accessibility_mode": "auto",
    "source_dirty": True,
    "engine_version": "4.7.1.stable.mono.official.a13da4feb",
    "executable_sha256": "0" * 64,
    "gpu": "NVIDIA GeForce GTX 1080",
    "driver": "32.0.15.8157",
    "cpu": "AMD Ryzen 5 1600 Six-Core Processor",
    "ram_bytes": 34289274880,
    "os": "Microsoft Windows 10 Pro",
    "display_scaling": 1.0,
    "isolation_confirmed": True,
    "started_utc": datetime.utcnow().isoformat() + "Z",
    "preset": "default",
    "startup_kind": "cold",
    "hitch_trace_only": False
}
prov_path = os.path.join(out_dir, "provenance.json")
with open(prov_path, "w", encoding="utf-8") as f:
    json.dump(provenance, f, indent=2)

env = os.environ.copy()
env["VIVARIUM_LANDSCAPE_SCENARIO"] = "frozen"
env["VIVARIUM_LANDSCAPE_PREPARATION"] = "mature"
env["VIVARIUM_PERF_SECONDS"] = "60"
env["VIVARIUM_BENCHMARK_QUALITY"] = "1"
env["VIVARIUM_RENDER_PROVENANCE"] = prov_path
env["VIVARIUM_BENCHMARK_USER_DIR"] = os.path.join(out_dir, "user")
env["VIVARIUM_BENCHMARK_EFFECT_SWEEP"] = "1"
env["VIVARIUM_BENCHMARK_SWEEP_KIND"] = "all"

cmd = [
    godot_exe,
    "--path", os.path.join(repo, "game"),
    "--log-file", os.path.join(out_dir, "engine.log"),
    "--",
    "--perf-test", out_dir,
    "--preset", "default",
    "--load", save_path
]

print("Launching effect sweep:", cmd)
sys.stdout.flush()

stdout_file = open(os.path.join(out_dir, "stdout.log"), "w", encoding="utf-8")
stderr_file = open(os.path.join(out_dir, "stderr.log"), "w", encoding="utf-8")

proc = subprocess.Popen(cmd, stdout=stdout_file, stderr=stderr_file, env=env)
print(f"Process started with PID {proc.pid}")
sys.stdout.flush()

try:
    exit_code = proc.wait(timeout=600)
    print(f"Process finished with exit code {exit_code}")
except subprocess.TimeoutExpired:
    print("Process timed out! Killing...")
    proc.kill()
    sys.exit(1)
finally:
    stdout_file.close()
    stderr_file.close()

gpu_lease_file.close()
sys.exit(exit_code)
