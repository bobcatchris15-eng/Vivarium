"""Continuous anatomical skin weights across reconstructed limb/torso seams."""
import sys,json
from pathlib import Path
import numpy as np
from gltf_mesh_patch import Asset

def smooth(a,b,x):
    t=np.clip((x-a)/(b-a),0,1);return t*t*(3-2*t)

asset=Asset(sys.argv[1]);j=asset.doc;out=Path(sys.argv[2]);prim=next(m for m in j['meshes'] if m['name']=='coati_body')['primitives'][0]
p=asset.read(prim['attributes']['POSITION']);bones=asset.read(prim['attributes']['JOINTS_0']);raw=asset.read(prim['attributes']['WEIGHTS_0']);skin=j['skins'][0]
names=[j['nodes'][i]['name'] for i in skin['joints']];m=asset.read(skin['inverseBindMatrices']).reshape(-1,4,4).transpose(0,2,1)
centers={name:np.linalg.inv(mat)[:3,3] for name,mat in zip(names,m)};weights=np.zeros((len(p),len(names)),dtype=np.float32)
for slot in range(4):weights[np.arange(len(p)),bones[:,slot]]+=raw[:,slot]
limb_ids=[i for i,name in enumerate(names) if name.startswith(('UpperArm_','Forearm_','Hand_','Thigh_','Shin_','Foot_'))]
score=[];profiles=[]
for side in ['L','R']:
    for front in [True,False]:
        a,b,c=[x+'_'+side for x in (['UpperArm','Forearm','Hand'] if front else ['Thigh','Shin','Foot'])];h,k,f=[centers[x] for x in [a,b,c]]
        A=np.linalg.norm(k-h);B=np.linalg.norm(f-k)
        def section(start,end):
            d=end-start;t=np.clip(((p-start)*d).sum(axis=1)/(d@d),0,1);r=np.linalg.norm(p-start-t[:,None]*d,axis=1);return t,r
        u,du=section(h,k);v,dv=section(k,f);upper=du<=dv;s=np.where(upper,u*A,A+v*B)
        radius=np.where(upper,(.10 if front else .125)*(1-u)+.06*u,.06*(1-v)+.038*v)
        ratio=np.where(upper,du,dv)/radius
        root=smooth(0,A*.7,s);blend=root*(1-smooth(1.1,1.65,ratio))
        joint=smooth(A*.55,A*1.25,s);foot=smooth(A+B*.75,A+B,s)
        target=np.zeros_like(weights);target[:,names.index(a)]=(1-joint)*(1-foot);target[:,names.index(b)]=joint*(1-foot);target[:,names.index(c)]=foot
        score.append(ratio);profiles.append((blend,target,names.index('Chest' if front else 'Pelvis')))
selection=np.argmin(score,axis=0);old_limb=weights[:,limb_ids].sum(axis=1);result=weights.copy();changed=np.zeros(len(p),dtype=bool)
for i,(blend,target,trunk) in enumerate(profiles):
    active=(selection==i)&(p[:,2]<-.095)&(p[:,1]>-.28)&((old_limb>.005)|(blend>.01))
    base=weights.copy();base[:,limb_ids]=0;total=base.sum(axis=1)
    empty=total<1e-6;base[empty,trunk]=1;total[empty]=1;base/=total[:,None]
    mixed=base*(1-blend[:,None])+target*blend[:,None]
    result[active]=mixed[active];changed|=active
top=np.argsort(result,axis=1)[:,-4:][:,::-1];value=np.take_along_axis(result,top,axis=1);value/=value.sum(axis=1,keepdims=True)
prim['attributes']['JOINTS_0']=asset.replace(prim['attributes']['JOINTS_0'],top.astype('u1'))
prim['attributes']['WEIGHTS_0']=asset.replace(prim['attributes']['WEIGHTS_0'],value.astype('<f4'))
asset.write(out);out.with_suffix('.weights.json').write_text(json.dumps({'body_vertices_reweighted':int(changed.sum()),'contract':'continuous torso-upper-lower-foot fields; preserve face/tail/paws; max4 normalized influences','status':'native review required'},indent=2));print('ANATOMICAL_WEIGHT_FIELD',changed.sum())
