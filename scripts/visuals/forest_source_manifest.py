"""Authenticate the existing visual lab's eligible game/source/assets for forest reviews."""
import hashlib
import json
import subprocess
import sys
from pathlib import Path

repo = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(repo / 'scripts' / 'visual-lab'))
from visual_lab import atomic, digest, source_paths

paths = dict(source_paths(repo))
# The general visual-lab filter skips directories named "tools". Sim/Tools is
# runtime C# (camera collision, selection and interaction), so include it here.
for path in (repo / 'src/Vivarium.Sim/Tools').glob('*.cs'):
    paths[path.relative_to(repo).as_posix()] = path
files = [{'path': rel, 'sha256': digest(path)} for rel, path in sorted(paths.items())]
tree = hashlib.sha256(json.dumps(files, sort_keys=True).encode()).hexdigest()
atomic(Path(sys.argv[1]), {'scope_version': 2, 'revision': subprocess.check_output(['git', '-C', str(repo), 'rev-parse', 'HEAD']).decode().strip(), 'tree_sha256': tree, 'files': files})
