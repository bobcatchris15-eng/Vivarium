"""Author linear 4K EXR canopy/enclosure lighting. Procedural, not a photographed HDRI."""
from pathlib import Path
import hashlib, json
import numpy as np
import OpenEXR

root = Path(__file__).resolve().parents[2]
out = root / 'game/Textures/Environment'
out.mkdir(parents=True, exist_ok=True)
w, h = 4096, 2048
lon = (np.arange(w, dtype=np.float32)[None, :] / w - .5) * (2 * np.pi)
lat = np.arange(h, dtype=np.float32)[:, None] / (h - 1) * np.pi
x = np.sin(lat) * np.sin(lon)
y = np.broadcast_to(np.cos(lat), (h, w))
z = np.sin(lat) * np.cos(lon)
def smooth(a, b, q):
    t = np.clip((q - a) / (b - a), 0, 1)
    return t*t*(3-2*t)
foliage = .5 + .22*np.sin(x*19 + np.sin(z*11))*np.cos(z*23+y*9) + .15*np.sin(x*51-z*37)
panels = smooth(.88,.96,y) + smooth(.985,.997,z*.25+y*.97)*.6
frames = .6 + .4*smooth(.03,.08,np.abs(np.sin(lon*5)))
records=[]
for name, sky, foliage_col, energy in [
    ('day', [.30,.44,.57], [.10,.23,.055], 1.),
    ('dusk', [.32,.15,.11], [.08,.085,.025], .40),
    ('night', [.006,.012,.032], [.003,.008,.004], .025)]:
    base = np.array(sky, np.float32)[None,None,:]*(.35+.65*np.maximum(y,0))[...,None]
    canopy = np.array(foliage_col,np.float32)[None,None,:]*(.4+foliage)[...,None]
    rgb = base*(.35+.65*smooth(.2,.85,foliage))[...,None] + canopy*(1-smooth(.35,.90,y))[...,None]
    rgb += (panels*frames*18*energy)[...,None]*np.array([1.,.94,.82],np.float32)
    rgb *= np.where(y<0,.18,1)[...,None]
    rgb = np.ascontiguousarray(rgb,dtype=np.float32)
    path=out/f'vivarium_canopy_{name}_4k.exr'
    OpenEXR.File({'compression':OpenEXR.ZIP_COMPRESSION,'type':OpenEXR.scanlineimage}, {'RGB':rgb}).write(str(path))
    records.append({'path':path.name,'sha256':hashlib.sha256(path.read_bytes()).hexdigest(),'min_linear':float(rgb.min()),'max_linear':float(rgb.max())})
    print(path.name, rgb.shape, records[-1]['max_linear'],flush=True)
(out/'radiance-manifest.json').write_text(json.dumps({'source':'authored deterministic linear radiance, not measured photography','size':[w,h],'files':records},indent=2))
