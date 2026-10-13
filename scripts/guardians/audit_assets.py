"""Read-only GLB integrity and material evidence audit; not a visual acceptance gate."""
import argparse
import json
import math
from pathlib import Path
import struct

COMPONENTS = {5120: ('b', 1), 5121: ('B', 1), 5122: ('h', 2), 5123: ('H', 2), 5125: ('I', 4), 5126: ('f', 4)}
WIDTHS = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4, 'MAT4': 16}

def load(path):
    data = path.read_bytes()
    magic, version, size = struct.unpack_from('<4sII', data)
    if magic != b'glTF' or version != 2 or size != len(data): raise ValueError('Invalid GLB header')
    chunks = {}; offset = 12
    while offset < size:
        length, kind = struct.unpack_from('<II', data, offset)
        chunks[kind] = data[offset+8:offset+8+length]; offset += length+8
    return json.loads(chunks[0x4E4F534A]), chunks.get(0x004E4942, b'')

def accessor(doc, binary, index):
    a = doc['accessors'][index]
    if 'sparse' in a: raise ValueError('Sparse accessor requires independent validator')
    view = doc['bufferViews'][a['bufferView']]
    code, size = COMPONENTS[a['componentType']]; width = WIDTHS[a['type']]
    stride = view.get('byteStride', size*width)
    start = view.get('byteOffset', 0)+a.get('byteOffset', 0)
    values = [struct.unpack_from('<'+code*width, binary, start+i*stride) for i in range(a['count'])]
    if a.get('normalized'):
        maximum = {5120:127, 5121:255, 5122:32767, 5123:65535}[a['componentType']]
        values = [tuple(max(-1, x/maximum) for x in v) for v in values]
    return values

def audit(path):
    doc, binary = load(path)
    failures = []; warnings = []; primitives = []; total_triangles = 0
    for mesh in doc.get('meshes', []):
        for primitive in mesh['primitives']:
            attrs = primitive['attributes']; positions = accessor(doc, binary, attrs['POSITION'])
            indices = [x[0] for x in accessor(doc, binary, primitive['indices'])] if 'indices' in primitive else list(range(len(positions)))
            bad_positions = sum(not all(math.isfinite(c) for c in p) for p in positions)
            bad_indices = sum(i >= len(positions) for i in indices)
            if bad_positions or bad_indices: failures.append(mesh.get('name', '')+' invalid vertices/indices')
            zero_weights = 0; invalid_weight_sums = 0
            if 'WEIGHTS_0' in attrs:
                weights = accessor(doc, binary, attrs['WEIGHTS_0'])
                for weight in weights:
                    if sum(weight) <= 1e-6: zero_weights += 1
                    if not all(math.isfinite(v) and v >= 0 for v in weight) or abs(sum(weight)-1) > 0.025: invalid_weight_sums += 1
                if zero_weights or invalid_weight_sums: failures.append(mesh.get('name', '')+' invalid skin weights')
            degenerate = 0
            if primitive.get('mode', 4) == 4 and not bad_indices:
                for i in range(0, len(indices)-2, 3):
                    a, b, c = (positions[indices[i+j]] for j in range(3))
                    u = [b[j]-a[j] for j in range(3)]; v = [c[j]-a[j] for j in range(3)]
                    cross = (u[1]*v[2]-u[2]*v[1], u[2]*v[0]-u[0]*v[2], u[0]*v[1]-u[1]*v[0])
                    if sum(x*x for x in cross) < 1e-18: degenerate += 1
                total_triangles += len(indices)//3
            if degenerate: warnings.append(mesh.get('name', '')+f' has {degenerate} degenerate triangles')
            if 'TEXCOORD_0' not in attrs: warnings.append(mesh.get('name', '')+' has no UV0')
            primitives.append({'mesh':mesh.get('name'), 'vertices':len(positions), 'triangles':len(indices)//3,
                'attributes':list(attrs), 'degenerate_triangles':degenerate, 'invalid_weight_sums':invalid_weight_sums})
    materials = []
    for material in doc.get('materials', []):
        pbr = material.get('pbrMetallicRoughness', {})
        materials.append({'name':material.get('name'), 'roughness':pbr.get('roughnessFactor', 1),
            'albedo_texture':'baseColorTexture' in pbr, 'roughness_texture':'metallicRoughnessTexture' in pbr,
            'normal_texture':'normalTexture' in material, 'extensions':list(material.get('extensions', {}))})
    clips = []
    for animation in doc.get('animations', []):
        times = [t[0] for sampler in animation['samplers'] for t in accessor(doc, binary, sampler['input'])]
        clips.append({'name':animation.get('name'), 'duration':max(times)-min(times), 'channels':len(animation['channels'])})
    names = {clip['name'] for clip in clips}
    if not {'idle', 'feed', 'rest'}.issubset(names) or not names.intersection({'walk','slither'}): failures.append('Missing core behavior clips')
    return {'asset':str(path.resolve()), 'bytes':path.stat().st_size, 'triangles':total_triangles,
        'skin_joint_counts':[len(s['joints']) for s in doc.get('skins', [])], 'primitives':primitives,
        'materials':materials, 'animations':clips, 'image_count':len(doc.get('images', [])),
        'failures':failures, 'warnings':warnings, 'integrity_passed':not failures,
        'visual_acceptance':'Requires rendered anatomy, material, contact and motion review'}

if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--assets', type=Path, default=Path('game/assets/guardians'))
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    reports = [audit(path) for path in sorted(args.assets.glob('*/guardian.glb'))]
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(reports, indent=2))
    for row in reports: print(Path(row['asset']).parent.name, row['triangles'], 'triangles', len(row['failures']), 'failures', len(row['warnings']), 'warnings')
    raise SystemExit(0 if reports and all(r['integrity_passed'] for r in reports) else 1)
