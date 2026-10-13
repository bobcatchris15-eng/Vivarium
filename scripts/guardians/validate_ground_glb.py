"""Stdlib GLB contract check independent of Blender scene state."""
import json,struct,math,hashlib,os
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
TASK=ROOT/'.clanker/tasks'/os.environ.get('GROUND_ART_TASK_ID','guardian-ground-art')
def verify(path):
    raw=path.read_bytes(); assert raw[:4]==b'glTF'; assert struct.unpack_from('<I',raw,4)[0]==2
    assert struct.unpack_from('<I',raw,8)[0]==len(raw)
    n=struct.unpack_from('<I',raw,12)[0]; doc=json.loads(raw[20:20+n]); off=20+n
    bn=struct.unpack_from('<I',raw,off)[0]; binary=raw[off+8:off+8+bn]
    def values(index):
        a=doc['accessors'][index]; view=doc['bufferViews'][a['bufferView']]
        fmt={5120:'b',5121:'B',5122:'h',5123:'H',5125:'I',5126:'f'}[a['componentType']]
        dim={'SCALAR':1,'VEC2':2,'VEC3':3,'VEC4':4,'MAT4':16}[a['type']]
        size=struct.calcsize(fmt)*dim; stride=view.get('byteStride',size); offset=view.get('byteOffset',0)+a.get('byteOffset',0)
        result=[struct.unpack_from('<'+fmt*dim,binary,offset+i*stride) for i in range(a['count'])]
        if a.get('normalized'):
            maxvalue={5121:255,5123:65535,5120:127,5122:32767}[a['componentType']]
            result=[tuple(x/maxvalue for x in r) for r in result]
        return result
    allfinite=True; weight_error=0; jointmax=0; degenerate=0; tris=0; primitives=0
    for mesh in doc['meshes']:
        for p in mesh['primitives']:
            primitives+=1; attrs=p['attributes']; assert all(k in attrs for k in ['POSITION','NORMAL','TEXCOORD_0','JOINTS_0','WEIGHTS_0'])
            pos=values(attrs['POSITION']); normal=values(attrs['NORMAL']); uv=values(attrs['TEXCOORD_0'])
            allfinite=allfinite and all(math.isfinite(x) for rows in [pos,normal,uv] for row in rows for x in row)
            assert allfinite
            weights=values(attrs['WEIGHTS_0']); joints=values(attrs['JOINTS_0'])
            weight_error=max(weight_error,max(abs(sum(w)-1) for w in weights)); jointmax=max(jointmax,max(max(j) for j in joints))
            indices=[r[0] for r in values(p['indices'])]; assert max(indices)<len(pos); assert len(indices)%3==0
            for i in range(0,len(indices),3):
                a,b,c=[pos[indices[i+j]] for j in range(3)]
                u=[b[j]-a[j] for j in range(3)]; v=[c[j]-a[j] for j in range(3)]
                cross=[u[1]*v[2]-u[2]*v[1],u[2]*v[0]-u[0]*v[2],u[0]*v[1]-u[1]*v[0]]
                if sum(x*x for x in cross)<1e-18: degenerate+=1
                tris+=1
    jointcount=max(len(s['joints']) for s in doc['skins']); assert jointmax<jointcount; assert weight_error<1e-4
    animations={a['name']:a for a in doc['animations']}; assert set(animations)=={'idle','walk','feed','rest'}
    clips={}
    for name,a in animations.items():
        durations=[]
        for s in a['samplers']:
            times=values(s['input']); assert all(times[i][0]<times[i+1][0] for i in range(len(times)-1))
            durations.append(times[-1][0]-times[0][0])
            output=values(s['output']); assert all(math.isfinite(x) for r in output for x in r)
        clips[name]={'channels':len(a['channels']),'duration_s':max(durations),'interpolations':sorted(set(s.get('interpolation','LINEAR') for s in a['samplers']))}
    assert any('normalTexture' in m and 'baseColorTexture' in m.get('pbrMetallicRoughness',{}) for m in doc['materials'])
    return {'passed':True,'path':str(path),'sha256':hashlib.sha256(raw).hexdigest(),'bytes':len(raw),'triangles':tris,'degenerate_triangles_threshold_1e-18':degenerate,'mesh_count':len(doc['meshes']),'primitive_count':primitives,'joint_count':jointcount,'max_skin_weight_error':weight_error,'all_vertex_data_finite':allfinite,'clips':clips,'embedded_images':len(doc.get('images',[]))}
if __name__=='__main__':
    reports={}
    for species,folder in [('coati','woodland_omnivore'),('monitor','great_ground')]: reports[species]=verify(ROOT/'game/assets/guardians'/folder/'guardian.glb')
    (TASK/'glb-contract-validation.json').write_text(json.dumps(reports,indent=2)); print(json.dumps(reports,indent=2))
