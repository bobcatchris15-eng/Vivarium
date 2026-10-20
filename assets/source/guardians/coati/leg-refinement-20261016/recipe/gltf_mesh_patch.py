"""Small GLB patch writer. Unedited live payloads survive compaction exactly."""
import copy
import json
import struct
from pathlib import Path
import numpy as np


class Asset:
    def __init__(self, path):
        raw = Path(path).read_bytes()
        n, tag = struct.unpack_from('<II', raw, 12)
        assert tag == 0x4e4f534a
        self.doc = json.loads(raw[20:20+n])
        size, tag = struct.unpack_from('<II', raw, 20+n)
        assert tag == 0x004e4942
        self.blob = bytearray(raw[28+n:28+n+size])

    def read(self, index):
        a = self.doc['accessors'][index]
        v = self.doc['bufferViews'][a['bufferView']]
        assert 'byteStride' not in v and 'sparse' not in a
        dtype = {5121:'u1',5123:'<u2',5125:'<u4',5126:'<f4'}[a['componentType']]
        width = {'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']]
        return np.frombuffer(self.blob, dtype=dtype, count=a['count']*width,
                             offset=v.get('byteOffset',0)+a.get('byteOffset',0)).reshape(a['count'],width).copy()

    def view(self, payload, target=None):
        while len(self.blob)%4: self.blob.append(0)
        v = {'buffer':0,'byteOffset':len(self.blob),'byteLength':len(payload)}
        if target: v['target']=target
        self.blob.extend(payload)
        self.doc['bufferViews'].append(v)
        return len(self.doc['bufferViews'])-1

    def replace(self, index, values, indices=False):
        a = copy.deepcopy(self.doc['accessors'][index])
        if indices: a['componentType']=5125; values=values.astype('<u4')
        a.update(bufferView=self.view(values.tobytes(),34963 if indices else 34962),count=len(values))
        a.pop('byteOffset',None)
        if 'min' in a or 'max' in a: a.update(min=values.min(axis=0).tolist(),max=values.max(axis=0).tolist())
        self.doc['accessors'].append(a)
        return len(self.doc['accessors'])-1

    def write(self, path):
        j=self.doc; refs=[]
        for m in j['meshes']:
            for p in m['primitives']:
                refs.extend((p['attributes'],k) for k in p['attributes'])
                if 'indices' in p: refs.append((p,'indices'))
                for t in p.get('targets',[]): refs.extend((t,k) for k in t)
        refs.extend((s,'inverseBindMatrices') for s in j.get('skins',[]) if 'inverseBindMatrices' in s)
        for a in j.get('animations',[]):
            for s in a['samplers']: refs.extend([(s,'input'),(s,'output')])
        live=sorted({o[k] for o,k in refs}); ids={x:i for i,x in enumerate(live)}
        for o,k in refs: o[k]=ids[o[k]]
        j['accessors']=[j['accessors'][x] for x in live]
        vr=[(a,'bufferView') for a in j['accessors']]+[(im,'bufferView') for im in j.get('images',[]) if 'bufferView' in im]
        live=sorted({o[k] for o,k in vr}); ids={x:i for i,x in enumerate(live)}; blob=bytearray();views=[]
        for x in live:
            v=dict(j['bufferViews'][x]); old=v.get('byteOffset',0)
            while len(blob)%4: blob.append(0)
            v['byteOffset']=len(blob);blob.extend(self.blob[old:old+v['byteLength']]);views.append(v)
        for o,k in vr:o[k]=ids[o[k]]
        j['bufferViews']=views;j['buffers'][0]['byteLength']=len(blob)
        text=json.dumps(j,separators=(',',':')).encode();text+=b' '*(-len(text)%4);blob+=b'\0'*(-len(blob)%4)
        raw=struct.pack('<III',0x46546c67,2,28+len(text)+len(blob))+struct.pack('<II',len(text),0x4e4f534a)+text+struct.pack('<II',len(blob),0x004e4942)+blob
        Path(path).parent.mkdir(parents=True,exist_ok=True);Path(path).write_bytes(raw)
