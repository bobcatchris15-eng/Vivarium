"""Reversible short stiff coat candidate; preserve every non-fur GLB payload.

Input is the latest detailed GLB, never a regeneration from an older blend.
Only a named fur primitive gets new accessors. Original binary stays a prefix.
"""
import argparse
import copy
import hashlib
import json
import struct
from pathlib import Path

import numpy as np
from scipy.spatial import cKDTree


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('source', type=Path)
    ap.add_argument('output', type=Path)
    args = ap.parse_args()
    original = args.source.read_bytes()
    size, kind = struct.unpack_from('<II', original, 12)
    assert kind == 0x4e4f534a
    doc = json.loads(original[20:20 + size])
    start = 20 + size
    binary_size, kind = struct.unpack_from('<II', original, start)
    assert kind == 0x004e4942
    binary = original[start + 8:start + 8 + binary_size]
    before = copy.deepcopy(doc)
    dtypes = {5121: 'u1', 5123: '<u2', 5125: '<u4', 5126: '<f4'}
    widths = {'SCALAR': 1, 'VEC2': 2, 'VEC3': 3, 'VEC4': 4}

    def read(index):
        a = doc['accessors'][index]
        v = doc['bufferViews'][a['bufferView']]
        assert 'byteStride' not in v and 'sparse' not in a
        return np.frombuffer(binary, dtype=dtypes[a['componentType']],
                             count=a['count'] * widths[a['type']],
                             offset=v.get('byteOffset', 0) + a.get('byteOffset', 0)).reshape(a['count'], -1).copy()

    mesh_index = next(i for i, m in enumerate(doc['meshes']) if m['name'] == 'coati_fine_skinned_fur')
    primitive = doc['meshes'][mesh_index]['primitives'][0]
    body = next(m for m in doc['meshes'] if m['name'] == 'coati_body')['primitives'][0]
    attrs = {k: read(v) for k, v in primitive['attributes'].items()}
    p = attrs['POSITION'].reshape(-1, 10, 3)
    assert len(p) == 36016, 'Unexpected coat revision; audit before adapting'
    assert np.max(np.abs((p[:, 0] + p[:, 1] - p[:, 5] - p[:, 6]) * .5)) < 5e-5
    assert np.max(np.abs(p[:, 4] - p[:, 9])) < 1e-6
    count = len(p) - 16  # retain authored vibrissae byte-for-byte
    roots = (p[:count, 0] + p[:count, 1]) * .5
    axes = p[:count, 4] - roots
    lengths = np.linalg.norm(axes, axis=1)
    axes /= lengths[:, None]
    nearest = cKDTree(read(body['attributes']['POSITION'])).query(roots)[1]
    normals = read(body['attributes']['NORMAL'])[nearest]
    normals /= np.maximum(np.linalg.norm(normals, axis=1)[:, None], 1e-8)
    tangent = axes - normals * (axes * normals).sum(axis=1)[:, None]
    tangent /= np.maximum(np.linalg.norm(tangent, axis=1)[:, None], 1e-8)
    direction = tangent * .89 + normals * .45
    direction /= np.linalg.norm(direction, axis=1)[:, None]
    side = np.cross(direction, normals)
    side /= np.maximum(np.linalg.norm(side, axis=1)[:, None], 1e-8)
    other = np.cross(direction, side)
    old_width = np.linalg.norm(p[:count, 1] - p[:count, 0], axis=1) * .5
    rng = np.random.default_rng(730219)
    positions, normal_sets, colors = [], [], []
    base_colors = attrs['COLOR_0'].reshape(-1, 10, 3)[:count, 2]
    for layer in range(3):
        # Sub-millimeter root spread forms a tight pelt. Keep the interpolated
        # source weights: the displacement is below the original fiber spacing.
        jitter = rng.uniform(-.00065, .00065, (count, 2)) if layer else np.zeros((count, 2))
        root = roots + side * jitter[:, :1] + tangent * jitter[:, 1:]
        length = lengths * rng.uniform(.60, .76, count)
        width = old_width * rng.uniform(.72, .92, count)
        result = np.zeros((count, 10, 3), dtype=np.float32)
        ns = np.zeros_like(result)
        for ribbon, lateral in enumerate((side, other)):
            k = ribbon * 5
            result[:, k] = root - lateral * width[:, None]
            result[:, k + 1] = root + lateral * width[:, None]
            result[:, k + 2] = root + direction * (length * .55)[:, None] - lateral * (width * .48)[:, None]
            result[:, k + 3] = root + direction * (length * .55)[:, None] + lateral * (width * .48)[:, None]
            result[:, k + 4] = root + direction * length[:, None]
            # Thin crossed ribbons must shade as a close pelt, not as two flat
            # intersecting cards that alternate black scratches and white dots.
            # Use the underlying smooth skin normal, with a small grain response.
            coat_normal = normals + tangent * .10
            coat_normal /= np.linalg.norm(coat_normal, axis=1)[:, None]
            ns[:, k:k + 5] = coat_normal[:, None, :]
        tint = np.array([.94, .94, 1, 1, 1.035] * 2)
        colors.append((base_colors[:, None, :] * tint[None, :, None] * rng.uniform(.94, 1.04, (count, 1, 1))).astype(np.float32))
        positions.append(result)
        normal_sets.append(ns)
    output = {}
    for key, data in attrs.items():
        grouped = data.reshape(len(p), 10, -1)
        parts = positions if key == 'POSITION' else normal_sets if key == 'NORMAL' else colors if key == 'COLOR_0' else [grouped[:count]] * 3
        output[key] = np.concatenate(parts + [grouped[count:]], axis=0).reshape(-1, data.shape[1]).astype(data.dtype)
    fiber_count = count * 3 + 16
    pattern = np.array([0, 1, 3, 0, 3, 2, 2, 3, 4, 5, 6, 8, 5, 8, 7, 7, 8, 9], dtype='<u4')
    indices = (np.arange(fiber_count, dtype='<u4')[:, None] * 10 + pattern).reshape(-1, 1)
    assert indices.max() < len(output['POSITION'])
    assert np.allclose(output['WEIGHTS_0'].sum(axis=1), 1, atol=.001)
    blob = bytearray(binary)

    def append(data, old_index, index_buffer=False):
        while len(blob) % 4:
            blob.append(0)
        offset = len(blob)
        payload = data.tobytes()
        blob.extend(payload)
        view = len(doc['bufferViews'])
        doc['bufferViews'].append({'buffer': 0, 'byteOffset': offset, 'byteLength': len(payload), 'target': 34963 if index_buffer else 34962})
        accessor = copy.deepcopy(doc['accessors'][old_index])
        accessor.update(bufferView=view, count=len(data))
        accessor.pop('byteOffset', None)
        if 'min' in accessor or 'max' in accessor:
            accessor.update(min=data.min(axis=0).tolist(), max=data.max(axis=0).tolist())
        if index_buffer:
            accessor['componentType'] = 5125
        doc['accessors'].append(accessor)
        return len(doc['accessors']) - 1

    for key, data in output.items():
        primitive['attributes'][key] = append(data, primitive['attributes'][key])
    primitive['indices'] = append(indices, primitive['indices'], True)
    doc['buffers'][0]['byteLength'] = len(blob)
    # Only the fur mesh, appended accessors/views and buffer length may change.
    for key in before.keys() - {'meshes', 'accessors', 'bufferViews', 'buffers'}:
        assert before[key] == doc[key], key
    for i in range(len(before['meshes'])):
        if i != mesh_index:
            assert before['meshes'][i] == doc['meshes'][i]
    assert blob[:len(binary)] == binary
    js = json.dumps(doc, separators=(',', ':')).encode()
    js += b' ' * (-len(js) % 4)
    blob += b'\0' * (-len(blob) % 4)
    result = struct.pack('<III', 0x46546c67, 2, 28 + len(js) + len(blob)) + struct.pack('<II', len(js), 0x4e4f534a) + js + struct.pack('<II', len(blob), 0x004e4942) + blob
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_bytes(result)
    report = {'source': str(args.source.resolve()), 'source_sha256': hashlib.sha256(original).hexdigest(), 'candidate_sha256': hashlib.sha256(result).hexdigest(), 'source_fibers': len(p), 'candidate_fibers': fiber_count, 'whiskers_preserved': 16, 'source_binary_prefix_preserved': True, 'all_non_fur_meshes_and_rig_animations_materials_unchanged': True, 'length_factor': [.60, .76], 'outward_component': .45, 'status': 'unreviewed candidate; no performance acceptance'}
    args.output.with_suffix('.json').write_text(json.dumps(report, indent=2))
    print(json.dumps(report))


if __name__ == '__main__':
    main()
