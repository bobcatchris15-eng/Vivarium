import json
import struct

path = 'game/assets/guardians/great_ground/guardian.glb'
with open(path, 'rb') as f:
    data = f.read()

chunk_len, chunk_type = struct.unpack_from('<II', data, 12)
doc = json.loads(data[20:20+chunk_len])
accessors = doc['accessors']
total_v = 0
total_t = 0
for m in doc['meshes']:
    for p in m['primitives']:
        pos_acc = accessors[p['attributes']['POSITION']]
        ind_acc = accessors[p['indices']]
        v_count = pos_acc['count']
        t_count = ind_acc['count'] // 3
        total_v += v_count
        total_t += t_count
        min_v = pos_acc.get('min')
        max_v = pos_acc.get('max')
        print(f"{m.get('name')}: verts={v_count}, tris={t_count}, bbox=({min_v} to {max_v})")
print(f"TOTAL: verts={total_v}, tris={total_t}")
