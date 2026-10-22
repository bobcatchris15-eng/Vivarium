import json, struct

def audit(path):
    with open(path, 'rb') as f:
        data = f.read()
    magic, ver, length = struct.unpack_from('<4sII', data, 0)
    chunk_len, chunk_type = struct.unpack_from('<II', data, 12)
    doc = json.loads(data[20:20+chunk_len])
    accessors = doc['accessors']
    total_v = 0
    total_t = 0
    meshes_info = []
    for m in doc.get('meshes', []):
        for p in m.get('primitives', []):
            pos_acc = accessors[p['attributes']['POSITION']]
            ind_acc = accessors[p['indices']]
            v_count = pos_acc['count']
            t_count = ind_acc['count'] // 3
            total_v += v_count
            total_t += t_count
            meshes_info.append((m.get('name'), v_count, t_count))
    return {
        'path': path,
        'size_bytes': len(data),
        'total_verts': total_v,
        'total_tris': total_t,
        'meshes': meshes_info,
        'animations': [a.get('name') for a in doc.get('animations', [])],
        'skins': len(doc.get('skins', [])),
        'joints': len(doc['skins'][0]['joints']) if doc.get('skins') else 0
    }

base = audit('game/assets/guardians/great_ground/guardian.glb')
cand = audit('scratch/monitor_realism/models/guardian_realistic.glb')

print("=== COMPARISON AUDIT ===")
print(f"File Size:  Baseline = {base['size_bytes']/1024/1024:.2f} MB  |  Realistic = {cand['size_bytes']/1024/1024:.2f} MB")
print(f"Total Verts: Baseline = {base['total_verts']:,}  |  Realistic = {cand['total_verts']:,}")
print(f"Total Tris:  Baseline = {base['total_tris']:,}  |  Realistic = {cand['total_tris']:,}")
print(f"Joints:      Baseline = {base['joints']}  |  Realistic = {cand['joints']}")
print(f"Animations:  Baseline = {base['animations']}  |  Realistic = {cand['animations']}")
print("\nRealistic Meshes:")
for name, v, t in cand['meshes']:
    print(f"  {name}: verts={v:,}, tris={t:,}")
