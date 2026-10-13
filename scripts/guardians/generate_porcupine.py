import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parent))
from porcupine_solo_authoring import generate
if __name__ == '__main__':
    generate('quilled_arboreal')
