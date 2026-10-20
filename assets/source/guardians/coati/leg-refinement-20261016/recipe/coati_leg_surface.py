"""Coati limb-volume correction and baked directional fur normal detail.

Patch the detailed source GLB; retain skeleton, clips, face and ground-contact
vertices. Remove coat fibers while retaining the sixteen authored whiskers.
"""
import argparse
import hashlib
import io
import json
import math
from pathlib import Path
import numpy as np
from PIL import Image
from scipy.ndimage import distance_transform_edt
from scipy.spatial import cKDTree
from scipy.sparse import coo_matrix
from gltf_mesh_patch import Asset
from coati_limb_retarget import retarget


def unit(v):
    return v/np.maximum(np.linalg.norm(v,axis=-1,keepdims=True),1e-9)


def bake_normal(p,n,uv,tri,flow,size=2048):
    image=np.zeros((size,size,3),dtype=np.float32);image[:,:,2]=1
    covered=np.zeros((size,size),dtype=bool)
    for face in tri:
        q=uv[face]*size-.5
        xmin,ymin=np.maximum(np.floor(q.min(axis=0)).astype(int),0)
        xmax,ymax=np.minimum(np.ceil(q.max(axis=0)).astype(int),size-1)
        if xmax<xmin or ymax<ymin:continue
        e1=q[1]-q[0];e2=q[2]-q[0];det=e1[0]*e2[1]-e1[1]*e2[0]
        if abs(det)<1e-7:continue
        xx,yy=np.meshgrid(np.arange(xmin,xmax+1),np.arange(ymin,ymax+1));dx=xx-q[0,0];dy=yy-q[0,1]
        b1=(dx*e2[1]-dy*e2[0])/det;b2=(dy*e1[0]-dx*e1[1])/det;b0=1-b1-b2
        mask=(b0>=-1e-5)&(b1>=-1e-5)&(b2>=-1e-5)
        if not mask.any():continue
        bary=np.stack([b0[mask],b1[mask],b2[mask]],axis=-1)
        xyz=bary@p[face];normal=unit(bary@n[face]);groom=bary@flow[face]
        groom=unit(groom-normal*(groom*normal).sum(axis=1)[:,None]);across=unit(np.cross(normal,groom))
        c=(xyz*across).sum(axis=1)/.0018;l=(xyz*groom).sum(axis=1)/.008
        seed=np.sin(np.floor(c)*127.1+np.floor(l)*311.7)*43758.5453;seed-=np.floor(seed)
        phase=(c+.3*seed)%1-.5;along=(l+.45*seed)%1
        ridge=np.exp(-(phase/.20)**2);envelope=np.sin(np.pi*along)**2
        dc=.000075*ridge*(-2*phase/.20**2)/.0018*envelope
        dl=.000075*ridge*np.sin(2*np.pi*along)*np.pi/.008
        fur_mask=np.where(xyz[:,1]<-.32,.3,1.0)
        fur_mask[(xyz[:,2]>-.08)&(normal[:,2]>.4)]=0
        dc*=fur_mask;dl*=fur_mask
        modified=unit(normal-across*dc[:,None]-groom*dl[:,None])
        duv1=uv[face[1]]-uv[face[0]];duv2=uv[face[2]]-uv[face[0]]
        edge1=p[face[1]]-p[face[0]];edge2=p[face[2]]-p[face[0]]
        d=duv1[0]*duv2[1]-duv1[1]*duv2[0]
        tangent=(edge1*duv2[1]-edge2*duv1[1])/d
        tangent=unit(tangent-normal*(normal*tangent).sum(axis=1)[:,None])
        raw_b=(-edge1*duv2[0]+edge2*duv1[0])/d
        bitangent=unit(np.cross(normal,tangent));bitangent*=np.where((bitangent*raw_b).sum(axis=1)>=0,1,-1)[:,None]
        encoded=np.stack([(modified*tangent).sum(axis=1),(modified*bitangent).sum(axis=1),(modified*normal).sum(axis=1)],axis=1)
        image[yy[mask],xx[mask]]=encoded;covered[yy[mask],xx[mask]]=True
    distance,nearest=distance_transform_edt(~covered,return_indices=True)
    pad=(~covered)&(distance<=5);image[pad]=image[nearest[0][pad],nearest[1][pad]]
    rgb=np.uint8(np.clip(image*.5+.5,0,1)*255)
    out=io.BytesIO();Image.fromarray(rgb).save(out,format='PNG')
    return out.getvalue(),float(covered.mean())


def main():
    ap=argparse.ArgumentParser();ap.add_argument('source',type=Path);ap.add_argument('output',type=Path)
    ap.add_argument('--geometry-only',action='store_true');ap.add_argument('--volume',type=float,default=1)
    ap.add_argument('--retarget-stance',action='store_true')
    args=ap.parse_args();asset=Asset(args.source);j=asset.doc
    retarget_audit=retarget(asset) if args.retarget_stance else None
    body=next(m for m in j['meshes'] if m['name']=='coati_body')['primitives'][0]
    p=asset.read(body['attributes']['POSITION']);old=p.copy();n=asset.read(body['attributes']['NORMAL'])
    joints=asset.read(body['attributes']['JOINTS_0']);weights=asset.read(body['attributes']['WEIGHTS_0'])
    skin=j['skins'][0];names=[j['nodes'][i]['name'] for i in skin['joints']]
    mats=asset.read(skin['inverseBindMatrices']).reshape(-1,4,4).transpose(0,2,1)
    centers={name:np.linalg.inv(mat)[:3,3] for name,mat in zip(names,mats)}
    influence={name:np.sum(np.where(joints==i,weights,0),axis=1) for i,name in enumerate(names)}
    displacement=np.zeros_like(p);flow=np.tile(np.array([0,1,0],dtype=np.float32),(len(p),1))
    for side in ['L','R']:
        for front in [True,False]:
            a,b,c=[x+'_'+side for x in (['UpperArm','Forearm','Hand'] if front else ['Thigh','Shin','Foot'])]
            h,k,f=[centers[x] for x in [a,b,c]]
            limb=influence[a]+influence[b]
            # Protect the sole, claws, toes and complete head. Sculpt the sleeve
            # around existing joints, keeping the rig and IK reach unchanged.
            protect=np.clip((-.105-p[:,2])/.045,0,1)
            leg_mask=limb*protect
            delta=p-k;axis=unit(f-h)
            along=np.clip(np.sum((p-h)*axis,axis=1)/np.linalg.norm(f-h),0,1)
            # Nearest centerline of the two anatomical bone segments.
            def radial(start,end):
                d=end-start;t=np.clip(((p-start)*d).sum(axis=1)/(d@d),0,1)
                center=start+t[:,None]*d;return p-center,t
            upper,tu=radial(h,k);lower,tl=radial(k,f)
            use_upper=np.linalg.norm(upper,axis=1)<=np.linalg.norm(lower,axis=1)
            rad=np.where(use_upper[:,None],upper,lower)
            # Developed scapular/triceps sleeve and haunch, tapering into a
            # broad lower limb instead of cylindrical sticks and joint beads.
            upper_gain=(.46 if front else .40)*(0.65+0.35*np.sin(np.pi*tu))
            lower_gain=(.52 if front else .38)*(1-.40*tl)
            gain=np.where(use_upper,upper_gain,lower_gain)*args.volume
            change=rad*gain[:,None]*leg_mask[:,None]
            # Greater sagittal depth, moderated lateral width: avoid spherical joints.
            change[:,0]*=.78
            displacement+=change
            flow=flow*(1-leg_mask[:,None])+unit(f-h)[None,:]*leg_mask[:,None]
    # The old union transferred skin weights from nearest source pieces. Those
    # are suitable for deformation but too discontinuous to drive a sculpt.
    # Smooth the sculpt field in rest space rather than growing each weighted
    # vertex independently into a lumpy shoulder/hip junction.
    distances,neighbors=cKDTree(old).query(old,k=48)
    kernel=np.exp(-(distances/.018)**2);kernel[distances>.045]=0
    kernel/=np.maximum(kernel.sum(axis=1,keepdims=True),1e-9)
    protected=(old[:,2]>=-.105)
    for name in ['Head','Jaw','Snout','Neck']:
        if name in influence:protected|=influence[name]>.05
    for _ in range(5):
        displacement=np.sum(displacement[neighbors]*kernel[:,:,None],axis=1)
        displacement[protected]=0
    p+=displacement
    tri=asset.read(body['indices']).reshape(-1,3).astype(int)
    if args.retarget_stance:
        # Smooth the actual connected surface at the union/hinge transitions.
        # Weld UV duplicates for topology operations; retain all original UVs.
        _,weld=np.unique(np.round(p,6),axis=0,return_inverse=True)
        count=weld.max()+1;pos=np.zeros((count,3));np.add.at(pos,weld,p)
        multiplicity=np.bincount(weld);pos/=multiplicity[:,None]
        edges=np.concatenate([weld[tri[:,[0,1]]],weld[tri[:,[1,2]]],weld[tri[:,[2,0]]]])
        edges=np.unique(np.sort(edges,axis=1),axis=0);edges=edges[edges[:,0]!=edges[:,1]]
        rows=np.r_[edges[:,0],edges[:,1]];cols=np.r_[edges[:,1],edges[:,0]]
        adj=coo_matrix((np.ones(len(rows)),(rows,cols)),shape=(count,count)).tocsr()
        degree=np.asarray(adj.sum(axis=1)).ravel();degree=np.maximum(degree,1)
        movable=np.zeros(count);np.maximum.at(movable,weld,np.clip(np.linalg.norm(displacement,axis=1)/.004,0,1))
        pin=np.zeros(count,dtype=bool);np.logical_or.at(pin,weld,protected);movable[pin]=0
        for _ in range(18):
            for strength in [.42,-.43]:
                average=(adj@pos)/degree[:,None]
                pos+=(average-pos)*(movable*strength)[:,None]
        p=pos[weld].astype('<f4')
    face_normals=np.cross(p[tri[:,1]]-p[tri[:,0]],p[tri[:,2]]-p[tri[:,0]])
    accumulated=np.zeros_like(p)
    for i in range(3):np.add.at(accumulated,tri[:,i],face_normals)
    # Weld UV-seam normal averages, while retaining all UV/color/skin topology.
    _,inverse=np.unique(np.round(p,6),axis=0,return_inverse=True)
    welded=np.zeros((inverse.max()+1,3),dtype=np.float32);np.add.at(welded,inverse,accumulated)
    rebuilt=unit(welded[inverse]);changed=np.linalg.norm(displacement,axis=1)>1e-7
    changed=np.linalg.norm(p-old,axis=1)>1e-7
    affected=changed.copy();affected[tri[np.any(changed[tri],axis=1)].ravel()]=True
    n[affected]=rebuilt[affected]
    body['attributes']['POSITION']=asset.replace(body['attributes']['POSITION'],p.astype('<f4'))
    body['attributes']['NORMAL']=asset.replace(body['attributes']['NORMAL'],n.astype('<f4'))
    fur=next(m for m in j['meshes'] if m['name']=='coati_fine_skinned_fur')['primitives'][0]
    assert asset.read(fur['attributes']['POSITION']).shape[0]==360160,'Use protected original detailed GLB'
    for key,index in list(fur['attributes'].items()):fur['attributes'][key]=asset.replace(index,asset.read(index)[-160:])
    # Preserve source whisker vertex/skin attributes; source quads have five vertices each.
    pattern=np.array([0,1,2,1,3,2,2,3,4,5,6,7,6,8,7,7,8,9],dtype='<u4')
    indices=(np.arange(16,dtype='<u4')[:,None]*10+pattern).reshape(-1,1)
    fur['indices']=asset.replace(fur['indices'],indices,True)
    j['materials'][fur['material']]['name']='CoatiVibrissae'
    coverage=None
    if not args.geometry_only:
        png,coverage=bake_normal(p,n,asset.read(body['attributes']['TEXCOORD_0']),tri,unit(flow))
        normal=j['materials'][body['material']]['normalTexture'];image=j['textures'][normal['index']]['source']
        j['images'][image].update(name='CoatiDirectionalFurNormal',bufferView=asset.view(png),mimeType='image/png')
        normal['scale']=.85
        args.output.parent.mkdir(parents=True,exist_ok=True);args.output.with_suffix('.normal.png').write_bytes(png)
    asset.write(args.output)
    report={'source_sha256':hashlib.sha256(args.source.read_bytes()).hexdigest(),'output_sha256':hashlib.sha256(args.output.read_bytes()).hexdigest(),'body_vertices_changed':int(changed.sum()),'maximum_vertex_shift_source_m':float(np.linalg.norm(displacement,axis=1).max()),'sole_vertices_unchanged':bool(np.all(p[old[:,2]>=-.105]==old[old[:,2]>=-.105])),'coat_fibers_removed':36000,'whiskers_retained':16,'rig_and_animation_untouched':not args.retarget_stance,'retarget_audit':retarget_audit,'normal_map_coverage':coverage,'geometry_only':args.geometry_only,'volume':args.volume,'acceptance':'candidate; native clay, animation, skin-contact and material review required'}
    args.output.with_suffix('.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report))


if __name__=='__main__':main()
