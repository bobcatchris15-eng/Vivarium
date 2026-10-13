"""Reproducible Y-up water-monitor authoring entry point."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from monitor_cinematic_authoring import generate
if __name__ == '__main__':
    generate('monitor')
