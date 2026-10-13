"""Reproducible Y-up tree boa authoring. Run from repository root."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from boa_cinematic_authoring import build_serpent
if __name__ == '__main__': build_serpent()
