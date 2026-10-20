"""Blender-only local muscle sleeve union; patch only the body primitive.
Run: blender --background --factory-startup --python this.py -- in.glb out.glb
"""
import sys, math, json
from pathlib import Path
import bpy
import bmesh
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree
sys.path.insert(0,str(Path(__file__).parent))
from gltf_mesh_patch import Asset

source,output=map(Path,sys.argv[sys.argv.index('--')+1:])
asset=Asset(source);j=asset.doc;primitive=next(m for m in j['meshes'] if m['name']=='coati_body')['primitives'][0]
attrs={k:asset.read(v) for k,v in primitive['attributes'].items()};p=attrs['POSITION'];tri=asset.read(primitive['indices']).reshape(-1,3)
skin=j['skins'][0];names=[j['nodes'][i]['name'] for i in skin['joints']]
binds=asset.read(skin['inverseBindMatrices']).reshape(-1,4,4).transpose(0,2,1);centers={name:np.linalg.inv(m)[:3,3] for name,m in zip(names,binds)}
dense=np.zeros((len(p),len(names)),dtype=np.float32)
for slot in range(4):dense[np.arange(len(p)),attrs['JOINTS_0'][:,slot]]+=attrs['WEIGHTS_0'][:,slot]
colors=attrs['COLOR_0'].astype(float)/65535
tree=KDTree(len(p))
for i,point in enumerate(p):tree.insert(point,i)
tree.balance()
source_pos=[x.copy() for x in p];source_tri=[tuple(map(int,t)) for t in tri]
source_weights=list(dense);source_colors=list(colors);source_uv=list(attrs['TEXCOORD_0'])
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
def object_mesh(name,vertices,faces):
    mesh=bpy.data.meshes.new(name);mesh.from_pydata(vertices,[],faces);mesh.update()
    ob=bpy.data.objects.new(name,mesh);bpy.context.collection.objects.link(ob);return ob
# Material consolidation also put disconnected ears/lids/muzzle details in this
# primitive. Keep them out of the body Boolean and append their exact attributes.
_,weld=np.unique(np.round(p,6),axis=0,return_inverse=True)
parents=np.arange(weld.max()+1)
def root(i):
    while parents[i]!=i:parents[i]=parents[parents[i]];i=parents[i]
    return i
for face in weld[tri]:
    r=root(face[0])
    for i in face[1:]:parents[root(i)]=r
labels=np.array([root(i) for i in weld]);unique,counts=np.unique(labels,return_counts=True);core=unique[np.argmax(counts)]
core_faces=tri[labels[tri[:,0]]==core];extra_faces=tri[labels[tri[:,0]]!=core]
body=object_mesh('CoatiConnectedBody',p.tolist(),core_faces.tolist())
bm=bmesh.new();bm.from_mesh(body.data);bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=1e-6)
loose=[v for v in bm.verts if not v.link_faces]
if loose:bmesh.ops.delete(bm,geom=loose,context='VERTS')
bm.to_mesh(body.data);bm.free();body.data.update()
print('CORE_TRIANGLES',len(core_faces),'PROTECTED_DETAIL_TRIANGLES',len(extra_faces),flush=True)
for side in ['L','R']:
    for front in [True,False]:
        a,b,c=[x+'_'+side for x in (['UpperArm','Forearm','Hand'] if front else ['Thigh','Shin','Foot'])]
        h,k,f=[centers[x] for x in [a,b,c]];vertices=[];faces=[];ws=[];cs=[];uv=[]
        inner=h.copy();inner[0]*=.25;inner[2]-=.045
        end=f+np.array([0,-.025,.020])
        rings=33;segments=32
        # Two smoothly joined tapered muscle sections. The extra shoulder and
        # thigh volume swallows the old piecewise-joint surface inside the union.
        for ring in range(rings):
            t=ring/(rings-1);u=np.clip((t-.18)/.37,0,1);v=np.clip((t-.55)/.45,0,1)
            if t<.18:
                fraction=t/.18;center=inner*(1-fraction)+h*fraction;axis=h-inner
                radius=.012*(1-fraction)+(.080 if front else .105)*fraction
            elif t<=.55:
                center=h*(1-u)+k*u;axis=k-h
                radius=(.080 if front else .105)*(1-u)+(.054 if front else .060)*u
            else:
                center=k*(1-v)+end*v;axis=end-k
                radius=(.054 if front else .060)*(1-v)+.009*v
            axis=axis/np.linalg.norm(axis);lateral=np.cross(axis,[1,0,0]);lateral/=np.linalg.norm(lateral);other=np.cross(axis,lateral)
            blend=np.zeros(len(names),dtype=np.float32)
            if t<=.5:blend[names.index(a)]=1-u*.5;blend[names.index(b)]=u*.5
            else:blend[names.index(a)]=(1-v)*.5;blend[names.index(b)]=.5+v*.5
            if t>.88:
                foot=(t-.88)/.12;blend*=1-foot;blend[names.index(c)]+=foot
            if t<.15:
                trunk=(1-t/.15)*.5;blend*=1-trunk;blend[names.index('Chest' if front else 'Pelvis')]+=trunk
            for segment in range(segments):
                angle=math.tau*segment/segments
                point=center+radius*(math.cos(angle)*lateral+math.sin(angle)*other*.90)
                vertices.append(tuple(point));ws.append(blend.copy())
                _,idx,_=tree.find(Vector(point));cs.append(colors[idx].copy());uv.append((segment/segments,t))
        for ring in range(rings-1):
            for seg in range(segments):
                a0=ring*segments+seg;a1=ring*segments+(seg+1)%segments;b0=a0+segments;b1=a1+segments
                faces.extend([(a0,a1,b1),(a0,b1,b0)])
        faces.extend([(0,i+1,i) for i in range(1,segments-1)])
        base=(rings-1)*segments;faces.extend([(base,base+i,base+i+1) for i in range(1,segments-1)])
        test=np.array(vertices);ff=np.array(faces);volume=np.einsum('ij,ij->i',test[ff[:,0]],np.cross(test[ff[:,1]],test[ff[:,2]])).sum()
        if volume<0:faces=[tuple(reversed(face)) for face in faces]
        offset=len(source_pos);source_pos.extend(np.array(vertices));source_tri.extend(tuple(offset+i for i in face) for face in faces)
        source_weights.extend(ws);source_colors.extend(cs);source_uv.extend(uv)
        sleeve=object_mesh('MuscleSleeve',vertices,faces)
        bpy.context.view_layer.objects.active=body;body.select_set(True)
        sleeve.select_set(True)
        bpy.ops.object.join()
        print('LIMB_UNION',side,front,flush=True)
body.data.remesh_voxel_size=.003
body.data.remesh_voxel_adaptivity=0
bpy.context.view_layer.objects.active=body
bpy.ops.object.voxel_remesh()
modifier=body.modifiers.new('Organic muscle transitions','SMOOTH');modifier.factor=.6;modifier.iterations=4
bpy.ops.object.modifier_apply(modifier=modifier.name)
modifier=body.modifiers.new('Controlled body topology','DECIMATE');modifier.ratio=.35
bpy.ops.object.modifier_apply(modifier=modifier.name)
bpy.ops.object.mode_set(mode='EDIT');bpy.ops.mesh.select_all(action='SELECT')
bpy.ops.uv.smart_project(angle_limit=1.15,island_margin=.004)
bpy.ops.object.mode_set(mode='OBJECT')
me=body.data;me.calc_loop_triangles();positions=np.array([v.co[:] for v in me.vertices],dtype='<f4')
faces=np.array([t.vertices[:] for t in me.loop_triangles],dtype='<u4')
corner_uv=np.array([me.uv_layers.active.data[loop].uv[:] for t in me.loop_triangles for loop in t.loops],dtype='<f4')
# Restore the source surface under protected facial details, paws and tail.
original_bvh=BVHTree.FromPolygons(p.tolist(),core_faces.tolist(),all_triangles=True)
for index,point in enumerate(positions):
    if point[1]<-.28 or point[1]>.55 or point[2]>-.095:
        location,_,_,_=original_bvh.find_nearest(Vector(point))
        positions[index]=location
# Transfer all surface attributes barycentrically from exact input surfaces.
bvh=BVHTree.FromPolygons(source_pos,source_tri,all_triangles=True)
sp=np.array(source_pos);st=np.array(source_tri);sw=np.array(source_weights);sc=np.array(source_colors);su=np.array(source_uv)
weights=[];out_colors=[];out_uv=[]
for point in positions:
    loc,normal,index,distance=bvh.find_nearest(Vector(point));ids=st[index];v0,v1,v2=sp[ids];e0=v1-v0;e1=v2-v0;v=np.array(loc)-v0
    d00=e0@e0;d01=e0@e1;d11=e1@e1;den=d00*d11-d01*d01
    if abs(den)<1e-18:bary=np.array([1,0,0])
    else:
        u=(d11*(v@e0)-d01*(v@e1))/den;w=(d00*(v@e1)-d01*(v@e0))/den;bary=np.clip([1-u-w,u,w],0,1);bary/=bary.sum()
    weights.append(bary@sw[ids]);out_colors.append(bary@sc[ids]);out_uv.append(bary@su[ids])
weights=np.array(weights);bones=np.argsort(weights,axis=1)[:,-4:][:,::-1];weights=np.take_along_axis(weights,bones,axis=1);weights/=weights.sum(axis=1,keepdims=True)
normal=np.zeros_like(positions);fn=np.cross(positions[faces[:,1]]-positions[faces[:,0]],positions[faces[:,2]]-positions[faces[:,0]])
for slot in range(3):np.add.at(normal,faces[:,slot],fn)
normal/=np.maximum(np.linalg.norm(normal,axis=1)[:,None],1e-9)
data={'POSITION':positions,'NORMAL':normal,'COLOR_0':np.uint16(np.clip(out_colors,0,1)*65535),'TEXCOORD_0':np.array(out_uv,dtype='<f4'),'TEXCOORD_1':np.array(out_uv,dtype='<f4'),'JOINTS_0':bones.astype('u1'),'WEIGHTS_0':weights.astype('<f4')}
keys=np.column_stack([faces.ravel(),np.round(corner_uv*1e7).astype(np.int64)])
_,first,inverse=np.unique(keys,axis=0,return_index=True,return_inverse=True)
source_indices=faces.ravel()[first]
for key in data:data[key]=data[key][source_indices]
data['TEXCOORD_0']=corner_uv[first];data['TEXCOORD_1']=corner_uv[first]
faces=inverse.reshape(-1,3).astype('<u4')
extra_vertices=np.unique(extra_faces);remap=np.full(len(p),-1,dtype=int);remap[extra_vertices]=np.arange(len(extra_vertices))+len(positions)
remap[extra_vertices]=np.arange(len(extra_vertices))+len(data['POSITION'])
faces=np.concatenate([faces,remap[extra_faces].astype('<u4')])
for key in data:data[key]=np.concatenate([data[key],attrs[key][extra_vertices]])
for key,value in data.items():primitive['attributes'][key]=asset.replace(primitive['attributes'][key],value)
primitive['indices']=asset.replace(primitive['indices'],faces.reshape(-1,1),True)
asset.write(output)
output.with_suffix('.union.json').write_text(json.dumps({'source':str(source),'body_vertices':len(positions),'body_triangles':len(faces),'operation':'Watertight3mm torso/muscle-sleeve voxel union, protected source-surface projection, exact disconnected facial detail retention, barycentric attribute transfer','status':'native geometry candidate; not accepted'},indent=2))
print('COATI_LIMB_UNION_OK',output,flush=True)
