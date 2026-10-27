"""Acquire original ambientCG CC0 4K PNG masters; never upscale 1K inputs.

Archives stay in build/asset-cache. The manifest records source URLs and hashes.
"""
from pathlib import Path
import concurrent.futures, hashlib, json, urllib.request, zipfile, time

ROOT = Path(__file__).resolve().parents[2]
ASSETS = ['Ground048', 'Ground037', 'Moss002', 'Rock058', 'Gravel022', 'Bark001', 'Bark013', 'Bark014']
MAPS = ['Color', 'NormalGL', 'Roughness', 'Displacement', 'AmbientOcclusion']

def acquire(asset):
    cache = ROOT / 'build/asset-cache'
    cache.mkdir(parents=True, exist_ok=True)
    archive = cache / f'{asset}_4K-PNG.zip'
    url = f'https://ambientcg.com/get?file={archive.name}'
    if not archive.exists():
        partial = archive.with_suffix('.part')
        for attempt in range(3):
            try:
                req = urllib.request.Request(url, headers={'User-Agent': 'Vivarium asset acquisition'})
                with urllib.request.urlopen(req, timeout=90) as src, partial.open('wb') as dst:
                    while data := src.read(1024 * 1024):
                        dst.write(data)
                with zipfile.ZipFile(partial) as z:
                    if z.testzip():
                        raise ValueError('Archive CRC validation failed')
                partial.replace(archive)
                break
            except Exception:
                if attempt == 2:
                    raise
                time.sleep(2)
    entries = []
    with zipfile.ZipFile(archive) as z:
        for channel in MAPS:
            name = f'{asset}_4K-PNG_{channel}.png'
            candidates = [n for n in z.namelist() if Path(n).name == name]
            if not candidates:
                if channel == 'AmbientOcclusion':
                    continue  # derive cavity AO from original displacement below
                raise ValueError(f'{name} missing in original archive')
            data = z.read(candidates[0])
            (ROOT / 'game/Textures' / name).write_bytes(data)
            entries.append({'path': f'game/Textures/{name}', 'sha256': hashlib.sha256(data).hexdigest()})
    # Some original scan sets omit AO. Derive it from the measured master height.
    ao = ROOT / 'game/Textures' / f'{asset}_4K-PNG_AmbientOcclusion.png'
    if not ao.exists():
        import numpy as np
        from PIL import Image
        import importlib.util
        spec = importlib.util.spec_from_file_location('surface', Path(__file__).with_name('GenerateSurfaceMaps.py'))
        module = importlib.util.module_from_spec(spec)
        spec.loader.exec_module(module)
        height = np.array(Image.open(ROOT / 'game/Textures' / f'{asset}_4K-PNG_Displacement.png'), dtype=np.float32) / 65535.0
        Image.fromarray((module.compute_multiscale_hbao(height) * 255).clip(0, 255).astype('uint8')).save(ao)
        entries.append({'path': str(ao.relative_to(ROOT)), 'sha256': hashlib.sha256(ao.read_bytes()).hexdigest(), 'derived': 'horizon AO from original 4K displacement'})
    print(f'{asset}: acquired {len(entries)} original/derived channels', flush=True)
    return {'asset': asset, 'url': url, 'license': 'CC0-1.0', 'files': entries}

if __name__ == '__main__':
    with concurrent.futures.ThreadPoolExecutor(max_workers=3) as pool:
        results = list(pool.map(acquire, ASSETS))
    (ROOT / 'game/Textures/SURFACE_MASTERS.json').write_text(json.dumps({'license_url': 'https://docs.ambientcg.com/license/', 'sets': results}, indent=2))
