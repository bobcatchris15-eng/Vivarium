"""Bake short directional coat normal detail on the corrected coati atlas."""
import copy,json,sys
from pathlib import Path
import numpy as np
from gltf_mesh_patch import Asset
from coati_leg_surface import bake_normal,unit

asset=Asset(sys.argv[1]);out=Path(sys.argv[2]);j=asset.doc
body=next(m for m in j['meshes'] if m['name']=='coati_body')['primitives'][0]
p=asset.read(body['attributes']['POSITION']);n=asset.read(body['attributes']['NORMAL']);uv=asset.read(body['attributes']['TEXCOORD_0']);tri=asset.read(body['indices']).reshape(-1,3)
bones=asset.read(body['attributes']['JOINTS_0']);weights=asset.read(body['attributes']['WEIGHTS_0']);skin=j['skins'][0];names=[j['nodes'][i]['name'] for i in skin['joints']]
binds=asset.read(skin['inverseBindMatrices']).reshape(-1,4,4).transpose(0,2,1);centers={name:np.linalg.inv(m)[:3,3] for name,m in zip(names,binds)}
flow=np.tile(np.array([0,1,0],dtype=np.float32),(len(p),1))
for side in ['L','R']:
    for a,b in [('UpperArm','Forearm'),('Forearm','Hand'),('Thigh','Shin'),('Shin','Foot')]:
        name=a+'_'+side;end=b+'_'+side;influence=np.sum(np.where(bones==names.index(name),weights,0),axis=1)
        direction=unit(centers[end]-centers[name]);flow=flow*(1-influence[:,None])+direction*influence[:,None]
# Last7048 triangles are the exact retained disconnected facial detail pieces.
core_tri=tri[:-7048];detail_tri=tri[-7048:]
png,coverage=bake_normal(p,n,uv,core_tri,unit(flow),2048)
original=copy.deepcopy(j['materials'][body['material']]);old_image=copy.deepcopy(j['images'][j['textures'][original['normalTexture']['index']]['source']])
j['images'].append(old_image);old_texture=copy.deepcopy(j['textures'][original['normalTexture']['index']]);old_texture['source']=len(j['images'])-1;j['textures'].append(old_texture)
original['name']='CoatiFacialDetailPreserved';original['normalTexture']['index']=len(j['textures'])-1;j['materials'].append(original)
detail=copy.deepcopy(body);detail['material']=len(j['materials'])-1;detail['indices']=asset.replace(body['indices'],detail_tri.reshape(-1,1),True)
body['indices']=asset.replace(body['indices'],core_tri.reshape(-1,1),True)
next(m for m in j['meshes'] if m['name']=='coati_body')['primitives'].append(detail)
normal=j['materials'][body['material']]['normalTexture'];image=j['textures'][normal['index']]['source']
j['images'][image].update(name='CoatiDirectionalFurNormal',bufferView=asset.view(png),mimeType='image/png');normal['scale']=.55
out.parent.mkdir(parents=True,exist_ok=True);out.with_suffix('.normal.png').write_bytes(png);asset.write(out)
out.with_suffix('.surface.json').write_text(json.dumps({'texture_size':[2048,2048],'atlas_coverage':coverage,'normal_strength':.55,'facial_detail_triangles_preserved':len(detail_tri),'coat_geometry_fibers':0,'whiskers':16,'status':'native material and motion validation required'},indent=2));print('COATI_FUR_NORMAL_BAKED',coverage,flush=True)
