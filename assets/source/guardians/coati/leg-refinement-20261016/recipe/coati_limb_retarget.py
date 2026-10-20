"""Retarget the existing four clips to a less angular coati limb rest stance.

No clip is regenerated or removed. Preserve hip and foot world trajectories,
solve each intermediate joint with the revised rest lengths and old bend pole.
"""
import numpy as np
from scipy.spatial.transform import Rotation, Slerp
from scipy.spatial import cKDTree


def smooth_limb_weights(asset):
    j=asset.doc;p=next(m for m in j['meshes'] if m['name']=='coati_body')['primitives'][0]
    xyz=asset.read(p['attributes']['POSITION']);bones=asset.read(p['attributes']['JOINTS_0']);weights=asset.read(p['attributes']['WEIGHTS_0'])
    names=[j['nodes'][i]['name'] for i in j['skins'][0]['joints']]
    dense=np.zeros((len(xyz),len(names)),dtype=np.float32)
    for slot in range(4):dense[np.arange(len(xyz)),bones[:,slot]]+=weights[:,slot]
    limb=[i for i,name in enumerate(names) if name.startswith(('UpperArm_','Forearm_','Thigh_','Shin_'))]
    head=[i for i,name in enumerate(names) if name in ['Head','Jaw','Snout','Neck']]
    region=(xyz[:,2]<-.105)&(dense[:,head].sum(axis=1)<.05)&(dense[:,limb].sum(axis=1)>.001)
    distance,neighbors=cKDTree(xyz).query(xyz,k=48)
    kernel=np.exp(-(distance/.018)**2).astype(np.float32);kernel[distance>.045]=0;kernel/=kernel.sum(axis=1,keepdims=True)
    for _ in range(4):
        averaged=np.sum(dense[neighbors]*kernel[:,:,None],axis=1)
        dense[region]=averaged[region]
    top=np.argsort(dense,axis=1)[:,-4:][:,::-1]
    w=np.take_along_axis(dense,top,axis=1);w/=w.sum(axis=1,keepdims=True)
    bones[region]=top[region];weights[region]=w[region]
    p['attributes']['JOINTS_0']=asset.replace(p['attributes']['JOINTS_0'],bones)
    p['attributes']['WEIGHTS_0']=asset.replace(p['attributes']['WEIGHTS_0'],weights)
    return int(region.sum())


def align(a,b):
    a=a/np.linalg.norm(a);b=b/np.linalg.norm(b);v=np.cross(a,b);c=np.clip(a@b,-1,1)
    if c>.999999:return np.eye(3)
    if c<-.999999:raise ValueError('Unexpected reversed limb')
    k=np.array([[0,-v[2],v[1]],[v[2],0,-v[0]],[-v[1],v[0],0]])
    return np.eye(3)+k+k@k/(1+c)


def retarget(asset):
    smoothed_vertices=smooth_limb_weights(asset)
    j=asset.doc;nodes=j['nodes'];parent={c:i for i,n in enumerate(nodes) for c in n.get('children',[])}
    ids={n.get('name'):i for i,n in enumerate(nodes)}
    def globals_for(changes):
        cache={}
        def get(i):
            if i not in cache:
                n=nodes[i];t,q,s=[changes.get((i,k),n.get(k,d)) for k,d in [('translation',[0,0,0]),('rotation',[0,0,0,1]),('scale',[1,1,1])]]
                m=np.eye(4);m[:3,:3]=Rotation.from_quat(q).as_matrix()@np.diag(s);m[:3,3]=t
                cache[i]=(get(parent[i])@m) if i in parent else m
            return cache[i]
        return {i:get(i) for i in range(len(nodes))}
    old_rest=globals_for({});new_rest={i:m.copy() for i,m in old_rest.items()};legs=[];changed_nodes=set()
    for side in ['L','R']:
        for front in [True,False]:
            a,b,c=[ids[x+'_'+side] for x in (['UpperArm','Forearm','Hand'] if front else ['Thigh','Shin','Foot'])]
            h,k,f=[old_rest[i][:3,3].copy() for i in [a,b,c]];new_k=k.copy();new_k[1]+=-.060 if front else .040
            new_rest[a][:3,:3]=align(k-h,new_k-h)@old_rest[a][:3,:3]
            new_rest[b][:3,3]=new_k;new_rest[b][:3,:3]=align(f-k,f-new_k)@old_rest[b][:3,:3]
            legs.append((a,b,c,np.linalg.norm(new_k-h),np.linalg.norm(f-new_k)))
            changed_nodes.update([a,b,c])
    # Deform the rest mesh into the new rest frame, then replace inverse binds.
    skin=j['skins'][0];bind_index=skin['inverseBindMatrices'];old_binds=asset.read(bind_index).reshape(-1,4,4).transpose(0,2,1)
    transforms=np.array([new_rest[node]@old_binds[i] for i,node in enumerate(skin['joints'])])
    for mesh in j['meshes']:
        if mesh['name']!='coati_body':continue
        p=mesh['primitives'][0];vertices=asset.read(p['attributes']['POSITION']);weights=asset.read(p['attributes']['WEIGHTS_0']);bones=asset.read(p['attributes']['JOINTS_0'])
        output=np.zeros_like(vertices)
        for slot in range(4):
            m=transforms[bones[:,slot]];output+=(np.einsum('nij,nj->ni',m[:,:3,:3],vertices)+m[:,:3,3])*weights[:,slot,None]
        p['attributes']['POSITION']=asset.replace(p['attributes']['POSITION'],output.astype('<f4'))
    new_binds=np.array([np.linalg.inv(new_rest[node]).T.reshape(-1) for node in skin['joints']],dtype='<f4')
    skin['inverseBindMatrices']=asset.replace(bind_index,new_binds)
    # Keep a numerical foot/hip trajectory audit at every original clip key time.
    audits={}
    for animation in j['animations']:
        tracks={};samplers={}
        for channel in animation['channels']:
            key=(channel['target']['node'],channel['target']['path']);s=animation['samplers'][channel['sampler']]
            assert s.get('interpolation','LINEAR') in ['LINEAR','STEP']
            tracks[key]=(asset.read(s['input']).ravel(),asset.read(s['output']),s.get('interpolation','LINEAR'));samplers[key]=s
        times=np.unique(np.concatenate([t for t,v,mode in tracks.values()]));outputs={(node,path):[] for node in changed_nodes for path in ['translation','rotation']}
        minimum_margin=float('inf');maximum_foot_error=0
        for time in times:
            changes={}
            for key,(t,v,mode) in tracks.items():
                if len(t)==1:value=v[0]
                elif mode=='STEP':value=v[max(0,np.searchsorted(t,time,side='right')-1)]
                elif key[1]=='rotation':value=Slerp(t,Rotation.from_quat(v))([np.clip(time,t[0],t[-1])]).as_quat()[0]
                else:value=np.array([np.interp(time,t,v[:,k]) for k in range(v.shape[1])])
                changes[key]=value
            old=globals_for(changes);new={i:m.copy() for i,m in old.items()}
            for a,b,c,A,B in legs:
                h,k,f=[old[i][:3,3] for i in [a,b,c]];axis=f-h;distance=np.linalg.norm(axis);axis/=distance
                minimum_margin=min(minimum_margin,A+B-distance)
                if distance>A+B-1e-5:raise ValueError(f'New limb cannot preserve foot path: {animation["name"]} node{a} at{time}')
                mid=(A*A-B*B+distance*distance)/(2*distance)
                pole=k-h-axis*((k-h)@axis);pole/=np.linalg.norm(pole)
                knee=h+axis*mid+pole*np.sqrt(max(0,A*A-mid*mid))
                new[a][:3,:3]=align(k-h,knee-h)@old[a][:3,:3]
                new[b][:3,3]=knee;new[b][:3,:3]=align(f-k,f-knee)@old[b][:3,:3]
                maximum_foot_error=max(maximum_foot_error,float(np.linalg.norm(new[c][:3,3]-f)))
            for node in changed_nodes:
                local=np.linalg.inv(new[parent[node]])@new[node] if node in parent else new[node]
                scale=np.linalg.norm(local[:3,:3],axis=0)
                outputs[node,'translation'].append(local[:3,3])
                outputs[node,'rotation'].append(Rotation.from_matrix(local[:3,:3]/scale).as_quat())
        for key,values in outputs.items():
            s=samplers[key];values=np.array(values,dtype='<f4')
            if key[1]=='rotation':
                for k in range(1,len(values)):
                    if values[k]@values[k-1]<0:values[k]*=-1
            s['input']=asset.replace(s['input'],times.astype('<f4').reshape(-1,1))
            s['output']=asset.replace(s['output'],values)
            s['interpolation']='LINEAR'
        audits[animation['name']]={'key_times':len(times),'maximum_foot_world_error_m':maximum_foot_error,'minimum_reach_margin_m':minimum_margin}
    for node in changed_nodes:
        local=np.linalg.inv(new_rest[parent[node]])@new_rest[node] if node in parent else new_rest[node]
        scale=np.linalg.norm(local[:3,:3],axis=0)
        nodes[node]['translation']=local[:3,3].tolist();nodes[node]['rotation']=Rotation.from_matrix(local[:3,:3]/scale).as_quat().tolist()
    return {'clips':audits,'limb_weight_vertices_smoothed':smoothed_vertices}
