import json, struct, numpy as np

path = 'game/assets/guardians/great_ground/guardian.glb'
with open(path, 'rb') as f:
    data = f.read()

chunk_len, chunk_type = struct.unpack_from('<II', data, 12)
doc = json.loads(data[20:20+chunk_len])
bin_data = data[28+chunk_len:]

# Find the tongue and eye positions
for m in doc['meshes']:
    if 'tongue' in m.get('name', ''):
        p = m['primitives'][0]
        pos_acc = doc['accessors'][p['attributes']['POSITION']]
        bview = doc['bufferViews'][pos_acc['bufferView']]
        offset = bview.get('byteOffset', 0) + pos_acc.get('byteOffset', 0)
        count = pos_acc['count']
        coords = np.frombuffer(bin_data[offset:offset+count*12], dtype=np.float32).reshape(-1, 3)
        print("Tongue coords in GLTF:", coords.mean(axis=0))
        # Note: In GLTF, Y up, -Z forward or whatever
        # When SolidCraft imports with up='y', it rotates: (x, y, z) -> (x, -z, y) or (x, z, -y)
        # Let's check how SolidCraft maps it
