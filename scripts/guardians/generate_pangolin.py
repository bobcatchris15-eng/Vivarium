"""Reproducible Y-up pangolin authoring. Run from repository root."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from pangolin_solo_authoring import build_pangolin
if __name__ == '__main__': build_pangolin()

