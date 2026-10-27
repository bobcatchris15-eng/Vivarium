"""Freeze render-test source while independent work continues in the main checkout.

Source/configuration files are copied. Large immutable image/model/import-cache inputs
are hardlinked and still covered by the capture's before/after hash manifest.
"""
import argparse
import os
import shutil
from pathlib import Path

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('destination',type=Path)
args=parser.parse_args()
root=Path(__file__).resolve().parents[3]
dest=args.destination.resolve()
dest.mkdir(parents=True,exist_ok=False)
copies=links=0
for folder in ('game','src','tests'):
    for directory,dirs,files in os.walk(root/folder):
        dirs[:]=[d for d in dirs if d not in ('obj','bin','mono','editor','shader_cache')]
        for name in files:
            source=Path(directory)/name
            target=dest/source.relative_to(root)
            target.parent.mkdir(parents=True,exist_ok=True)
            if source.stat().st_size>1_000_000 and source.suffix.lower() in ('.png','.jpg','.exr','.glb','.ctex','.scn','.bin'):
                os.link(source,target);links+=1
            else:
                shutil.copy2(source,target);copies+=1
for source in root.iterdir():
    if source.is_file() and source.suffix.lower() in ('.sln','.props','.targets','.json','.config'):
        shutil.copy2(source,dest/source.name)
print(f'Frozen source: {dest}; copied {copies} files, hardlinked {links} immutable asset/cache files.')
