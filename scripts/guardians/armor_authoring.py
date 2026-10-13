"""Bounded anatomical guardian authoring, image PBR, explicit skinning and audits.

Coordinates intentionally remain X-right / Y-up / -Z-forward in Blender.
The runtime's +90X import correction is therefore still required. Bones use
root-parented axial segments and connected two-link limb chains. Analytic
poses avoid cumulative Euler curls; planted IK preserves limb lengths.
"""
import bpy, bmesh, math, json, os, csv
from pathlib import Path
from mathutils import Vector, Matrix, kdtree
import numpy as np

TAU = math.tau
REPORT = Path('.clanker/tasks/guardian-armor-art')

def clean():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    REPORT.mkdir(parents=True, exist_ok=True)
    bpy.context.scene.render.fps = 30

def image(name, pixels, directory, noncolor=False):
    h,w,_=pixels.shape
    im=bpy.data.images.new(name, width=w, height=h, alpha=True)
    if noncolor: im.colorspace_settings.name='Non-Color'
    im.pixels.foreach_set(pixels.astype(np.float32).ravel())
    im.filepath_raw=str((directory/(name+'.png')).resolve())
    im.file_format='PNG'; im.save(); im.pack()
    return im

def pbr(name, directory, kind, base, rough=.5):
    """1K/2K PNGs feed glTF-supported image/normal/Principled nodes."""
    n=2048 if kind in ('snake','ventral') else 1024
    y,x=np.mgrid[0:n,0:n]/n
    if kind=='keratin':
        grooves=np.sin(x*TAU*36 + 2*np.sin(y*TAU*3))*.5+.5
        grain=np.sin(x*TAU*179+y*TAU*81)*np.sin(y*TAU*133+x*TAU*31)
        height=.012*grooves+.003*grain+.020*np.sin(y*math.pi)
        modulation=.76+.18*grooves+.10*y+.035*grain
    elif kind=='snake':
        # Staggered imbricated dorsal scales. UV body spans 5 circumferential
        # repeats and 18 longitudinal repeats, giving 60 x 216 scale cells.
        row=np.floor(y*216); cx=(x*60+row*.5)%1; cy=(y*216)%1
        d=((cx-.5)/.50)**2+((cy-.50)/.56)**2
        dome=np.clip(1-d,0,1)
        edge=np.clip((1-d)*12,0,1)
        grain=np.sin(x*TAU*479+y*TAU*373)*np.sin(y*TAU*433)
        # Low-relief micro-scales across one whole-body UV atlas. Broad
        # pigment/roughness variation breaks the uniform shiny toy response.
        mottle=.5*np.sin(y*TAU*7+np.sin(x*TAU*3))+.3*np.sin(y*TAU*19-x*TAU*4)
        height=.0016*dome+.00010*grain
        modulation=.78+.095*edge+.055*dome+.045*mottle+.008*grain
    elif kind=='ventral':
        scutes=np.cos(y*TAU*216)
        mottle=.4*np.sin(y*TAU*7)+.25*np.sin(x*TAU*3+y*TAU*11)
        height=.0006*scutes+.00015*np.sin(x*TAU*17+y*TAU*179)
        modulation=.87+.035*scutes+.045*mottle
    else:
        # Irregular isotropic pores, not a periodic woven sine grid. The latter
        # produced obvious cloth-like moire in the verified face closeup.
        grain=np.random.default_rng(1437).normal(0,1,(n,n))
        for _ in range(5): grain=(grain*4+np.roll(grain,1,0)+np.roll(grain,-1,0)+np.roll(grain,1,1)+np.roll(grain,-1,1))/8
        grain/=max(float(grain.std()),1e-8)
        height=.0018*grain
        modulation=.90+.022*grain
    rgba=np.ones((n,n,4)); rgba[:,:,:3]=np.clip(np.array(base)[None,None,:]*modulation[:,:,None],0,1)
    if kind=='snake':
        dorsal=np.minimum(x,1-x); marking=np.zeros((n,n))
        for k in range(14):
            v=(-.69+k*.145+.98)/2.67
            center=v+.018*np.sin(dorsal*23+k*.71)+.008*dorsal
            width=.0045+.0012*math.sin(k*1.37)
            ragged=1+.16*np.sin(x*TAU*37+y*TAU*51)
            marking=np.maximum(marking,np.exp(-((y-center)/(width*ragged))**2)*np.clip(1-dorsal/.145,0,1))
        blend=np.clip(marking*.83,0,1)[:,:,None]
        rgba[:,:,:3]=rgba[:,:,:3]*(1-blend)+np.array((.40,.45,.30))[None,None,:]*blend
    color=image(name+'_basecolor',rgba,directory)
    r=np.ones_like(rgba); val=np.clip(rough+.10*(1-modulation),.15,.95)
    if kind in ('snake','ventral'): val=np.clip(val+.035*mottle+.012*grain if kind=='snake' else val+.025*mottle,.35,.80)
    r[:,:,:3]=val[:,:,None]
    roughim=image(name+'_roughness',r,directory,True)
    # Derivatives in tangent-space, positive green OpenGL normal convention.
    dy,dx=np.gradient(height)
    normals=np.stack((-dx*n*.07,-dy*n*.07,np.ones_like(dx)),axis=-1)
    normals/=np.linalg.norm(normals,axis=-1)[:,:,None]
    nm=np.ones_like(rgba); nm[:,:,:3]=normals*.5+.5
    normalim=image(name+'_normal',nm,directory,True)
    mat=bpy.data.materials.new(name); mat.use_nodes=True
    nodes=mat.node_tree.nodes; links=mat.node_tree.links; bsdf=nodes.get('Principled BSDF')
    for im,target in ((color,'Base Color'),(roughim,'Roughness')):
        tex=nodes.new('ShaderNodeTexImage'); tex.image=im; links.new(tex.outputs['Color'],bsdf.inputs[target])
    tex=nodes.new('ShaderNodeTexImage'); tex.image=normalim
    norm=nodes.new('ShaderNodeNormalMap'); links.new(tex.outputs['Color'],norm.inputs['Color']); links.new(norm.outputs['Normal'],bsdf.inputs['Normal'])
    bsdf.inputs['Specular IOR Level'].default_value=.25 if kind in ('snake','ventral') else .35
    return mat

def solid(name, color, rough=.5):
    m=bpy.data.materials.new(name); m.use_nodes=True
    b=m.node_tree.nodes.get('Principled BSDF'); b.inputs['Base Color'].default_value=(*color,1); b.inputs['Roughness'].default_value=rough
    if 'Pit' in name: b.inputs['Specular IOR Level'].default_value=0
    return m

class Geometry:
    def __init__(self): self.v=[]; self.f=[]; self.uv=[]; self.weights=[]; self.mi=[]
    def vertex(self,p,uv=(0,0),weights=None):
        self.v.append(tuple(p)); self.uv.append(uv); self.weights.append(weights or {'Root':1}); return len(self.v)-1
    def face(self,indices,mat=0): self.f.append(tuple(indices)); self.mi.append(mat)
    def object(self,name,mats,rig):
        mesh=bpy.data.meshes.new(name); mesh.from_pydata(self.v,[],self.f); mesh.update()
        ob=bpy.data.objects.new(name,mesh); bpy.context.collection.objects.link(ob)
        for m in mats: mesh.materials.append(m)
        for p,mi in zip(mesh.polygons,self.mi): p.material_index=mi; p.use_smooth=True
        uv=mesh.uv_layers.new(name='UVMap')
        for p in mesh.polygons:
            for li in p.loop_indices: uv.data[li].uv=self.uv[mesh.loops[li].vertex_index]
        bm=bmesh.new(); bm.from_mesh(mesh); bmesh.ops.recalc_face_normals(bm,faces=bm.faces); bm.to_mesh(mesh); bm.free()
        groups={b.name:ob.vertex_groups.new(name=b.name) for b in rig.data.bones}
        for i,weights in enumerate(self.weights):
            for name,w in weights.items():
                if w>1e-5: groups[name].add([i],w,'REPLACE')
        ob.parent=rig; mod=ob.modifiers.new('AnatomicalSkin','ARMATURE'); mod.object=rig
        return ob

def rig_create(name,segments):
    data=bpy.data.armatures.new(name); ob=bpy.data.objects.new(name,data); bpy.context.collection.objects.link(ob)
    bpy.context.view_layer.objects.active=ob; ob.select_set(True); bpy.ops.object.mode_set(mode='EDIT')
    root=data.edit_bones.new('Root'); root.head=(0,0,0); root.tail=(0,.10,0)
    for name,p,q in segments:
        b=data.edit_bones.new(name); b.head=p; b.tail=q; b.parent=root
        if name.endswith('Lower'):
            b.parent=data.edit_bones[name[:-5]+'Upper']; b.use_connect=True
        elif name.endswith('Foot'):
            b.parent=data.edit_bones[name[:-4]+'Lower']; b.use_connect=True
    bpy.ops.object.mode_set(mode='OBJECT'); ob.animation_data_create()
    for b in ob.pose.bones: b.rotation_mode='QUATERNION'
    return ob

def longitudinal(z,centers):
    if z<=centers[0][1]: return {centers[0][0]:1}
    if z>=centers[-1][1]: return {centers[-1][0]:1}
    for (a,za),(b,zb) in zip(centers,centers[1:]):
        if za<=z<=zb:
            if a==b: return {a:1}
            t=(z-za)/(zb-za); t=t*t*(3-2*t)
            return {a:1-t,b:t}

def boa_longitudinal(z,centers):
    """C2 cubic B-spline influence envelope; at most four exported joints."""
    h=centers[1][1]-centers[0][1]; u=(z-centers[0][1])/h; i=math.floor(u); f=u-i
    values=((1-f)**3/6,(3*f**3-6*f*f+4)/6,(-3*f**3+3*f*f+3*f+1)/6,f**3/6)
    weights={}
    for index,value in zip((i-1,i,i+1,i+2),values):
        name=centers[max(0,min(len(centers)-1,index))][0]; weights[name]=weights.get(name,0)+value
    return {name:value for name,value in weights.items() if value>1e-8}

def ellipsoid(g,center,radius,bone,mat=0,segments=24,rings=14):
    center=Vector(center); radius=Vector(radius); start=len(g.v)
    for j in range(rings+1):
        v=j/rings; phi=math.pi*v
        for i in range(segments+1):
            u=i/segments; a=TAU*u
            p=center+Vector((math.sin(phi)*math.cos(a)*radius.x,math.cos(phi)*radius.y,math.sin(phi)*math.sin(a)*radius.z))
            g.vertex(p,(u,v),{bone:1})
    for j in range(rings):
        for i in range(segments):
            a=start+j*(segments+1)+i; g.face((a,a+1,a+segments+2,a+segments+1),mat)

def tube(g,points,radii,bone,mat=0,segments=12):
    rows=[]
    for k,p in enumerate(points):
        p=Vector(p); tangent=Vector(points[min(k+1,len(points)-1)])-Vector(points[max(0,k-1)])
        tangent.normalize(); ref=Vector((0,1,0))
        if abs(tangent.dot(ref))>.95: ref=Vector((1,0,0))
        axis=tangent.cross(ref).normalized(); up=axis.cross(tangent).normalized()
        row=[]
        for i in range(segments+1):
            u=i/segments; a=u*TAU
            row.append(g.vertex(p+radii[k]*(math.cos(a)*axis+math.sin(a)*up),(u,k/(len(points)-1)),{bone:1}))
        rows.append(row)
    for ra,rb in zip(rows,rows[1:]):
        for i in range(segments): g.face((ra[i],ra[i+1],rb[i+1],rb[i]),mat)
    g.face(tuple(reversed(rows[0][:-1])),mat); g.face(rows[-1][:-1],mat)

def cavity_disc(g,center,normal,ry,rz,bone,mat):
    normal=Vector(normal).normalized(); along=Vector((0,0,1)); upright=normal.cross(along).normalized()
    center=Vector(center); ci=g.vertex(center,(.5,.5),{bone:1}); ring=[]
    for i in range(20):
        a=TAU*i/20
        ring.append(g.vertex(center+along*(rz*math.cos(a))+upright*(ry*math.sin(a)),(.5+.5*math.cos(a),.5+.5*math.sin(a)),{bone:1}))
    for i in range(20): g.face((ci,ring[i],ring[(i+1)%20]),mat)

def interp(profile,z):
    for a,b in zip(profile,profile[1:]):
        if a[0]<=z<=b[0]:
            t=(z-a[0])/(b[0]-a[0]); t=t*t*(3-2*t)
            return [a[k]*(1-t)+b[k]*t for k in range(1,4)]
    return profile[0][1:] if z<profile[0][0] else profile[-1][1:]

def loft(g,profile,centers,step=.018,radial=40,mat=0,snake=False,labial=False):
    zs=np.linspace(profile[0][0],profile[-1][0],int((profile[-1][0]-profile[0][0])/step)+1); rows=[]
    for z in zs:
        y,rx,ry=interp(profile,z); row=[]
        for j in range(radial+1):
            a=TAU*j/radial; c=math.cos(a)
            # Flat broad ventrum supports ground contact, round dorsal muscle.
            yc=max(c,-.86) if snake else c
            if snake:
                # Broad paired epaxial masses and a restrained dorsal ridge,
                # with low-frequency muscle variation along the trunk.
                muscle=1+.045*math.sin(a*2)**2+.018*math.sin(z*11)*math.sin(a)**2
                yc=yc*(1+.035*max(0,c)**5)
            else: muscle=1
            p=Vector((rx*math.sin(a)*muscle,y+ry*yc,z))
            if labial:
                depth=0
                for side in (-1,1):
                    theta=1.90 if side==1 else TAU-1.90
                    delta=(a-theta+math.pi)%TAU-math.pi
                    for k in range(7):
                        pz=-1.28+k*.037
                        depth+=.0045*math.exp(-((z-pz)/.008)**2-(delta/.14)**2)
                    theta=1.10 if side==1 else TAU-1.10
                    delta=(a-theta+math.pi)%TAU-math.pi
                    depth+=.0025*math.exp(-((z+1.287)/.008)**2-(delta/.16)**2)
                normal=Vector((math.sin(a)/max(rx,.001),math.cos(a)/max(ry,.001),0)).normalized()
                p-=normal*depth
            uv=(j/radial,(z-zs[0])/(zs[-1]-zs[0])*(1 if snake else 3))
            row.append(g.vertex(p,uv,boa_longitudinal(z,centers) if snake else longitudinal(z,centers)))
        rows.append(row)
    for ra,rb in zip(rows,rows[1:]):
        for j in range(radial):
            idx=1 if snake and math.cos(TAU*(j+.5)/radial)<-.62 else mat
            g.face((ra[j],rb[j],rb[j+1],ra[j+1]),idx)
    g.face(tuple(reversed(rows[0][:-1])),mat); g.face(rows[-1][:-1],mat)

def plate(g,anchor,across,along,normal,width,length,bone,mat=0):
    """Closed, curved imbricated keratin shield with embedded root and free rim.
    Five across samples x nine longitudinal samples, upper/lower shell, rim.
    Rounded pentagonal outline; grooves are a reusable tangent normal image.
    """
    anchor=Vector(anchor); across=Vector(across); along=Vector(along); normal=Vector(normal)
    rows=[]; bottoms=[]; nx=4; ny=5
    for j in range(ny+1):
        t=j/ny; taper=math.sqrt(max(.015,1-t**4))
        ra=[]; rb=[]
        for i in range(nx+1):
            u=i/nx; s=(u-.5)*2
            # Root embedded just under skin, convex shoulder and tapered tip.
            rise=.001+.004*math.sin(math.pi*t*.82)+.003*(1-s*s)*math.sin(math.pi*t)
            p=anchor+across*(s*width*.5*taper)+along*(t*length)+normal*rise
            ra.append(g.vertex(p,(u,t),{bone:1})); rb.append(g.vertex(p-normal*.003,(u,t),{bone:1}))
        rows.append(ra); bottoms.append(rb)
    for j in range(ny):
        for i in range(nx):
            g.face((rows[j][i],rows[j][i+1],rows[j+1][i+1],rows[j+1][i]),mat)
            g.face((bottoms[j][i],bottoms[j+1][i],bottoms[j+1][i+1],bottoms[j][i+1]),mat)
    perimeter=rows[0]+[r[-1] for r in rows[1:]]+list(reversed(rows[-1][:-1]))+[r[0] for r in reversed(rows[1:-1])]
    bottom=bottoms[0]+[r[-1] for r in bottoms[1:]]+list(reversed(bottoms[-1][:-1]))+[r[0] for r in reversed(bottoms[1:-1])]
    for i in range(len(perimeter)): g.face((perimeter[i],bottom[i],bottom[(i+1)%len(bottom)],perimeter[(i+1)%len(perimeter)]),mat)

def boa_face_point(profile,z,a,depress=True):
    cy,rx,ry=interp(profile,z); s=math.sin(a); c=math.cos(a)
    # Broad roof and nearly vertical muzzle flanks; lower skull seals to jaw.
    x=rx*math.copysign(abs(s)**(.65 if c>=0 else .35),s)
    y=cy+ry*c**.65 if c>=0 else cy-(cy-.133)*abs(c)**.30
    p=Vector((x,y,z)); normal=Vector((s/max(rx,.001),c/max(ry,.001),0)).normalized()
    if depress:
        for side in (-1,1):
            for theta,pz,depth,aw,zw in [(1.28,-1.177,.008,.26,.022),(1.08,-1.291,.0045,.18,.009)]+[(1.50,-1.272+k*.037,.004,.12,.008) for k in range(7)]:
                angle=theta if side==1 else TAU-theta; delta=(a-angle+math.pi)%TAU-math.pi
                # Socket depression stops at the upper lip instead of pulling
                # the nearly flat skull underside into a vertical eye streak.
                if theta==1.28 and c<.10: continue
                p-=normal*(depth*math.exp(-((z-pz)/zw)**2-(delta/aw)**2))
    return p,normal

def boa_skull(g,profile):
    rows=[]; radial=80
    for z in np.linspace(profile[0][0],profile[-1][0],100):
        row=[]
        for j in range(radial+1):
            p,n=boa_face_point(profile,z,TAU*j/radial)
            uv=(j/radial*.28,(z-profile[0][0])/(profile[-1][0]-profile[0][0])*.075)
            if z==profile[0][0]: uv=(.12+(p.x/profile[0][2]*.5+.5)*.045,.005+(p.y-.133)/.05*.012)
            row.append(g.vertex(p,uv,{'Head':1}))
        rows.append(row)
    for ra,rb in zip(rows,rows[1:]):
        for j in range(radial):
            # The green skull ends at its lip edge. A second green sheet
            # under the cream mandible caused animated triangle interference.
            if math.cos(TAU*(j+.5)/radial)>0: g.face((ra[j],rb[j],rb[j+1],ra[j+1]),0)
    # A restrained convex rostral shield closes the wedge, without a ball tip.
    center=g.vertex((0,.149,profile[0][0]-.003),(.1425,.009),{'Head':1})
    for j in range(radial):
        if math.cos(TAU*(j+.5)/radial)>0: g.face((center,rows[0][j+1],rows[0][j]),0)
    g.face(rows[-1][:radial//4+1]+rows[-1][radial*3//4:-1],0)

def boa_jaw(g,profile):
    # Upper mandibular surface follows the skull's underside exactly. The
    # small seam offset closes the mouth without an exposed tooth or slit.
    rows=[]; radial=80
    for z in np.linspace(profile[0][0],profile[-1][0],100):
        cy,rx,ry=interp(profile,z); row=[]
        for j in range(radial+1):
            a=TAU*j/radial; s=math.sin(a); c=math.cos(a)
            x=rx*math.copysign(abs(s)**(.35 if c>=0 else .75),s)
            y=cy-(cy-.133)*c**.30 if c>=0 else cy-(cy-.118)*abs(c)**.50
            row.append(g.vertex((x,y,z),(j/radial*.28,(z-profile[0][0])/(profile[-1][0]-profile[0][0])*.075),{'Jaw':1}))
        rows.append(row)
    for ra,rb in zip(rows,rows[1:]):
        for j in range(radial):
            # Only the external lower mandibular shell reaches the lip. No
            # near-coplanar internal roof remains behind the visible seam.
            if math.cos(TAU*(j+.5)/radial)<0: g.face((ra[j],rb[j],rb[j+1],ra[j+1]),1)
    g.face(tuple(reversed(rows[0][radial//4:radial*3//4+1])),1); g.face(rows[-1][radial//4:radial*3//4+1],1)

def cavity_rim(g,center,normal,ry,rz,bone,mat=0):
    normal=Vector(normal).normalized(); along=Vector((0,0,1)); up=normal.cross(along).normalized(); center=Vector(center)
    rings=[]
    for factor,depth in ((1.6,.0004),(1.12,.0005),(1,-.0015)):
        ring=[]
        for j in range(32):
            a=TAU*j/32; p=center+along*(rz*factor*math.cos(a))+up*(ry*factor*math.sin(a))+normal*depth
            ring.append(g.vertex(p,(.12+j/32*.08,.025+factor*.005),{bone:1}))
        rings.append(ring)
    for ra,rb in zip(rings,rings[1:]):
        for j in range(32): g.face((ra[j],rb[j],rb[(j+1)%32],ra[(j+1)%32]),mat)

def pose_matrix(rig,name,offset=(0,0,0),angle=0,axis='Y'):
    b=rig.data.bones[name]; p=b.head_local
    transform=Matrix.Translation(Vector(offset))@Matrix.Translation(p)@Matrix.Rotation(angle,4,axis)@Matrix.Translation(-p)
    rig.pose.bones[name].matrix=transform@b.matrix_local

def segment_pose(rig,name,head,tail,parent_matrix=None):
    bone=rig.data.bones[name]
    rotation=(bone.tail_local-bone.head_local).rotation_difference(Vector(tail)-Vector(head)).to_matrix().to_4x4()
    transform=Matrix.Translation(Vector(head))@rotation@Matrix.Translation(-bone.head_local)
    desired=transform@bone.matrix_local
    if bone.parent:
        parent_matrix=parent_matrix if parent_matrix is not None else rig.pose.bones[bone.parent.name].matrix
        rig.pose.bones[name].matrix_basis=bone.convert_local_to_pose(desired,bone.matrix_local,parent_matrix=parent_matrix,parent_matrix_local=bone.parent.matrix_local,invert=True)
    else: rig.pose.bones[name].matrix=desired
    return desired

def solve_leg(rig,label,target):
    """Exact two-link inverse kinematics with rest-knee bend-plane selection.
    Paws retain their rest orientation; only upper/lower segments articulate.
    Lengths are never scaled and their shared knee is the same solved point.
    """
    upper=rig.data.bones[label+'Upper']; lower=rig.data.bones[label+'Lower']
    hip=upper.head_local.copy(); rest_knee=upper.tail_local.copy(); target=Vector(target)
    a=(upper.tail_local-upper.head_local).length; b=(lower.tail_local-lower.head_local).length
    delta=target-hip; distance=delta.length
    if distance>=a+b-.0001 or distance<=abs(a-b)+.0001: raise RuntimeError('Unreachable planted paw '+label)
    direction=delta/distance; bend=rest_knee-hip-direction*(rest_knee-hip).dot(direction)
    bend.normalize(); along=(a*a-b*b+distance*distance)/(2*distance)
    height=math.sqrt(max(0,a*a-along*along)); knee=hip+direction*along+bend*height
    upper_matrix=segment_pose(rig,label+'Upper',hip,knee)
    lower_matrix=segment_pose(rig,label+'Lower',knee,target,upper_matrix)
    foot=rig.data.bones[label+'Foot']; offset=target-foot.head_local
    desired=Matrix.Translation(offset)@foot.matrix_local
    rig.pose.bones[label+'Foot'].matrix_basis=foot.convert_local_to_pose(desired,foot.matrix_local,parent_matrix=lower_matrix,parent_matrix_local=lower.matrix_local,invert=True)
    return hip,knee,target

def limb_definition(side,front,z):
    h=Vector((.115*side,.325,z))
    k=Vector((.215*side,.190 if front else .170,z+(.105 if front else -.105)))
    w=Vector((.235*side,.053,z-.035))
    return h,k,w

def limb_loft(g,label,h,k,w):
    """Tapered muscle/tendon exterior with no discrete oval joint masses."""
    side=1 if h.x>0 else -1
    points=[Vector((.075*side,.335,h.z)),h,h.lerp(k,.27),h.lerp(k,.58),k,k.lerp(w,.32),k.lerp(w,.70),w]
    radii=[.071,.073,.065,.053,.046,.038,.030,.025]
    rows=[]; frames=[]; radial=24
    for j,(p,radius) in enumerate(zip(points,radii)):
        tangent=(points[min(j+1,len(points)-1)]-points[max(0,j-1)]).normalized()
        across=tangent.cross(Vector((0,0,1))).normalized(); out=across.cross(tangent).normalized()
        rows.append([]); frames.append((p,across,out,radius))
        if j<=3: weights={label+'Upper':1}
        elif j==4: weights={label+'Upper':.35,label+'Lower':.65}
        elif j==7: weights={label+'Lower':.35,label+'Foot':.65}
        else: weights={label+'Lower':1}
        for i in range(radial+1):
            u=i/radial; a=u*TAU
            # Slightly compressed cross-section makes a broad forearm with
            # a continuous taper toward the narrow tendinous wrist.
            pos=p+radius*(math.cos(a)*across+math.sin(a)*out*.86)
            rows[-1].append(g.vertex(pos,(u,j/7),weights))
    for ra,rb in zip(rows,rows[1:]):
        for i in range(radial): g.face((ra[i],ra[i+1],rb[i+1],rb[i]),1)
    g.face(tuple(reversed(rows[0][:-1])),1); g.face(rows[-1][:-1],1)
    return frames

def continuous_skin(g,name,mats,rig):
    """Voxel-union anatomical volumes, then recover explicit nearby weights.

    The trunk, shoulder, elbow, wrist, and five digits become one continuous
    exterior. It removes intersecting capsule seams while retaining modeled
    muscle masses. UVs and bone weights are recovered after final topology;
    textures remain image-based and the exporter sees ordinary polygons.
    """
    ob=g.object(name,mats,rig)
    ob.modifiers.clear()
    bpy.context.view_layer.objects.active=ob
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True)
    mod=ob.modifiers.new('ContinuousAnatomicalUnion','REMESH'); mod.mode='VOXEL'; mod.voxel_size=.0065; mod.use_smooth_shade=True
    bpy.ops.object.modifier_apply(modifier=mod.name)
    smooth=ob.modifiers.new('SoftTissueTransitions','SMOOTH'); smooth.factor=.7; smooth.iterations=3
    bpy.ops.object.modifier_apply(modifier=smooth.name)
    dec=ob.modifiers.new('SurfaceRetopologyBudget','DECIMATE'); dec.ratio=.24
    bpy.ops.object.modifier_apply(modifier=dec.name)
    tree=kdtree.KDTree(len(g.v))
    for i,p in enumerate(g.v): tree.insert(Vector(p),i)
    tree.balance()
    ob.vertex_groups.clear(); groups={b.name:ob.vertex_groups.new(name=b.name) for b in rig.data.bones}
    uv=ob.data.uv_layers.new(name='UVMap') if not ob.data.uv_layers else ob.data.uv_layers.active
    coords=[]
    for v in ob.data.vertices:
        neighbors=tree.find_n(v.co,4); weights={}; total=0
        for _,index,distance in neighbors:
            influence=1/max(distance,.002)**2; total+=influence
            for bone,w in g.weights[index].items(): weights[bone]=weights.get(bone,0)+w*influence
        # Use stable world-projected skin UVs; the pore map is isotropic.
        coords.append((v.co.x*3,v.co.z*3))
        for bone,w in weights.items(): groups[bone].add([v.index],w/total,'REPLACE')
    for p in ob.data.polygons:
        p.use_smooth=True; p.material_index=1
        for li in p.loop_indices: uv.data[li].uv=coords[ob.data.loops[li].vertex_index]
    mod=ob.modifiers.new('AnatomicalSkin','ARMATURE'); mod.object=rig
    return ob

def keyall(rig,frame):
    for b in rig.pose.bones:
        for channel in ('location','rotation_quaternion','scale'): b.keyframe_insert(channel,frame=frame)

def animate(rig,callback,names=('idle','walk','feed','rest')):
    for name in names:
        act=bpy.data.actions.new(name); rig.animation_data.action=act
        for f in range(1,62):
            for b in rig.pose.bones: b.matrix_basis=Matrix.Identity(4)
            callback(name,(f-1)/60,rig)
            keyall(rig,f)
        for layer in act.layers:
            for action_strip in layer.strips:
                for bag in action_strip.channelbags:
                    for curve in bag.fcurves:
                        for point in curve.keyframe_points: point.interpolation='LINEAR'
        track=rig.animation_data.nla_tracks.new(); track.name=name; strip=track.strips.new(name,1,act); strip.name=name
        track.mute=True
    rig.animation_data.action=None
    for b in rig.pose.bones: b.matrix_basis=Matrix.Identity(4)
    bpy.context.scene.frame_set(1)

def pangolin_motion(clip,t,rig):
    phase=t*TAU
    for name in ('Body0','Body1','Body2','Body3'):
        pose_matrix(rig,name,angle=.004*math.sin(phase))
    pose_matrix(rig,'Head',angle=(.07*math.sin(phase)**2 if clip=='feed' else .015*math.sin(phase)),axis='X')
    for i in range(8): pose_matrix(rig,f'Tail{i}',angle=(.025*math.sin(phase-i*.5) if clip=='walk' else .010*math.sin(phase-i*.4)))
    if clip=='walk':
        for label,sign in (('FL',1),('FR',-1),('HL',-1),('HR',1)):
            u=(t+(0 if sign==1 else .5))%1
            if u<.5:
                # Forward is -Z: planted feet move +Z in model space so
                # runtime translation at -.20m/s cancels during stance.
                lift=0; dz=-.10+.40*u
            else:
                swing=(u-.5)*2; lift=.040*math.sin(math.pi*swing); dz=.10-.20*swing
            target=rig.data.bones[label+'Foot'].head_local+Vector((0,lift,dz))
            solve_leg(rig,label,target)
    if clip=='feed':
        pose_matrix(rig,'Tongue',offset=(0,0,-.085*math.sin(math.pi*t)**2))
    if clip=='rest':
        # Supported neutral rest; no destructive defensive ball approximation.
        pose_matrix(rig,'Head',angle=.025*math.sin(phase),axis='Y')

def build_pangolin():
    clean(); out=Path('game/assets/guardians/armored_forest'); out.mkdir(parents=True,exist_ok=True)
    mats=[pbr('AmberKeratin',out,'keratin',(.32,.215,.11),.36),pbr('PangolinSkin',out,'skin',(.23,.16,.105),.63),solid('DarkClaw',(.09,.060,.035),.34),solid('GlossEye',(.007,.010,.008),.12),solid('WarmIris',(.24,.105,.022),.25),solid('Mouth',(.065,.032,.025),.72),solid('Tongue',(.37,.105,.10),.5)]
    centers=[('Head',-.66),('Neck',-.43),('Body0',-.23),('Body1',0),('Body2',.22),('Body3',.42)]+[(f'Tail{i}',.56+i*.16) for i in range(8)]
    seg=[(n,(0,.25,z),(0,.25,z+.10)) for n,z in centers]
    for side,label in ((1,'L'),(-1,'R')):
        for front,prefix,z in ((True,'F',-.23),(False,'H',.34)):
            label=prefix+('L' if side==1 else 'R')
            h,k,w=limb_definition(side,front,z)
            seg.extend([(label+'Upper',h,k),(label+'Lower',k,w),(label+'Foot',w,(.245*side,.033,z-.09))])
    seg.append(('Tongue',(0,.171,-.79),(0,.171,-.84)))
    rig=rig_create('PangolinRig',seg)
    skin=Geometry(); scales=Geometry(); detail=Geometry()
    profile=[(-.84,.185,.013,.012),(-.78,.190,.026,.020),(-.69,.214,.044,.031),(-.60,.244,.076,.060),(-.50,.280,.103,.082),(-.38,.325,.135,.103),(-.23,.355,.18,.145),(0,.380,.213,.165),(.20,.365,.204,.163),(.37,.330,.181,.140),(.53,.258,.137,.092),(.74,.173,.114,.073),(.96,.114,.082,.055),(1.18,.086,.061,.042),(1.40,.066,.040,.030),(1.65,.043,.013,.015),(1.73,.033,.002,.003)]
    loft(skin,profile,centers,mat=1,radial=48)
    # Closely spaced staggered scales cover back/flanks, narrowing over tail.
    z=-.63; row=0
    while z<1.65:
        y,rx,ry=interp(profile,z)
        count=max(5,int(rx*TAU/.070)); step=.035 if z<-.50 else (.047 if z<.55 else .045)
        arc=2.20 if z<-.50 else 3.90
        for j in range(count):
            a=-arc*.5+(j+.5*(row%2))/max(1,count-1)*arc
            normal=Vector((math.sin(a),math.cos(a),0)).normalized(); tangent=Vector((math.cos(a),-math.sin(a),0))
            anchor=Vector((rx*math.sin(a),y+ry*math.cos(a),z))-normal*.0015
            width=(rx*arc/count)*1.19; length=step*1.62
            ny,nrx,nry=interp(profile,min(z+length,profile[-1][0]))
            delta=Vector(((nrx-rx)*math.sin(a),ny+nry*math.cos(a)-y-ry*math.cos(a),length))
            bone=max(longitudinal(z+.025,centers),key=longitudinal(z+.025,centers).get)
            plate(scales,anchor,tangent,delta.normalized(),normal,width,delta.length,bone)
        z+=step; row+=1
    for side in (-1,1):
        for front,prefix,z in ((True,'F',-.23),(False,'H',.34)):
            label=prefix+('L' if side==1 else 'R'); upper=label+'Upper'; lower=label+'Lower'; foot=label+'Foot'
            h,k,w=limb_definition(side,front,z); frames=limb_loft(skin,label,h,k,w)
            ellipsoid(skin,(.245*side,.029,z-.077),(.047,.024,.064),foot,1)
            # Five digits; three dominant foreclaws plus reduced outer claws.
            for k in range(5):
                dx=(k-2)*.020; length=([.031,.055,.074,.055,.031] if front else [.024,.032,.038,.032,.024])[k]
                px=.245*side+dx
                ellipsoid(skin,(px,.025,z-.111),(.013,.020,.034),foot,1,16,10)
                if front:
                    tube(detail,[(px,.034,z-.13),(px,.047,z-.13-length*.35),(px,.025,z-.13-length*.85),(px,.012,z-.13-length*.94),(px,.021,z-.13-length*.55)], [.011,.010,.006,.003,.0008],foot,2)
                else:
                    tube(detail,[(px,.032,z-.13),(px,.042,z-.13-length*.35),(px,.026,z-.13-length*.8),(px,.009,z-.13-length)], [.010,.009,.006,.0008],foot,2)
            for row in range(1,7):
                p,across,radial_out,radius=frames[row]; nextp=frames[row+1][0]
                for column in range(4):
                    a=-.92+column*.61
                    # Outboard arc selected from the loft's actual cross frame.
                    normal=(across*math.cos(a)+radial_out*math.sin(a)).normalized()
                    if normal.x*side<0: normal=-normal
                    anchor=p+normal*(radius-.003)
                    tangent=(nextp-p).normalized()
                    lateral=tangent.cross(normal).normalized()
                    plate(scales,anchor,lateral,tangent,normal,.027,.044,upper if row<4 else lower)
        # Lid rims + inset black eye, warm iris, tiny nasal chamber.
        ellipsoid(skin,(.076*side,.266,-.598),(.014,.019,.022),'Head',1)
        ellipsoid(detail,(.082*side,.267,-.605),(.008,.013,.014),'Head',4)
        ellipsoid(detail,(.087*side,.267,-.608),(.006,.010,.010),'Head',3)
        ellipsoid(detail,(.022*side,.20,-.784),(.004,.003,.007),'Head',3,16,10)
        tube(detail,[(.014*side,.178,-.828),(.029*side,.185,-.754),(.052*side,.209,-.676)], [.0018,.0017,.001], 'Head',5,8)
        # Small external ear inset behind eye, not a horn.
        ellipsoid(skin,(.092*side,.291,-.507),(.011,.022,.016),'Neck',1)
    tube(detail,[(0,.178,-.831),(0,.178,-.844),(0,.178,-.857)],[.003,.0028,.002],'Tongue',6,8)
    bodies=[continuous_skin(skin,'PangolinContinuousSkin',mats,rig),scales.object('ClosedOverlappingArmor',mats,rig),detail.object('FaceDigitsClaws',mats,rig)]
    animate(rig,pangolin_motion)
    finish('armored_forest',out,rig,bodies)

def serpent_motion(clip,t,rig):
    phase=t*TAU
    # Individual absolute deformation matrices share a sampled continuous
    # centerline; no accumulated local rotations or pitching below ground.
    for i in range(33):
        z=-.94+i*.082; name=f'Body{i:02d}'
        if clip in ('walk','slither'):
            # Integrate a unit tangent instead of stretching a z-fixed sine
            # centerline. Lower angular amplitude avoids tight inner folds.
            s=z+.94; k=7.85; angle=.24*math.cos(k*s-phase)
            count=max(1,math.ceil(s/.006)); ds=s/count
            samples=[.24*math.cos(k*((j+.5)*ds)-phase) for j in range(count)]
            x=.030*math.sin(-phase)+sum(math.sin(a)*ds for a in samples)
            new_z=-.94+sum(math.cos(a)*ds for a in samples)
            pose_matrix(rig,name,(x,0,new_z-z),angle,'Y')
            continue
        elif clip=='rest':
            # Loose 270-degree planar resting coil. Arc length is preserved
            # at every segment sample; the free head points out of the coil.
            s=z+.94; theta=1.8*s
            x=(1-math.cos(theta))/1.8
            new_z=-.94+math.sin(theta)/1.8
            pose_matrix(rig,name,(x,0,new_z-z),theta,'Y')
            continue
        else:
            amp=.035; k=3.2; x=amp*math.sin(k*(z+.94)+phase)*(.8 if clip=='feed' else .4)
            derivative=amp*k*math.cos(k*(z+.94)+phase)*(.8 if clip=='feed' else .4)
        pose_matrix(rig,name,(x,0,0),math.atan(derivative),'Y')
    # Head follows the anterior body sample exactly, with no downward curl.
    z=-.94
    if clip in ('walk','slither'): x=.030*math.sin(-phase); ang=.24*math.cos(-phase)
    elif clip=='rest': x=0; ang=0
    else: x=.035*math.sin(phase)*(.8 if clip=='feed' else .4); ang=math.atan(.035*3.2*math.cos(phase)*(.8 if clip=='feed' else .4))
    pose_matrix(rig,'Head',(x,0,0),ang,'Y')
    pose_matrix(rig,'Jaw',(x,0,0),ang,'Y')
    if clip=='feed':
        # Small tasting/open-jaw feeding cue. Full swallowing requires prey
        # interaction and is deliberately not represented as a strike curl.
        b=rig.data.bones['Jaw']; p=b.head_local
        transform=Matrix.Translation((x,0,0))@Matrix.Translation(Vector((0,.175,-.94)))@Matrix.Rotation(ang,4,'Y')@Matrix.Translation(Vector((0,-.175,.94)))@Matrix.Translation(p)@Matrix.Rotation(-.18*math.sin(math.pi*t)**2,4,'X')@Matrix.Translation(-p)
        rig.pose.bones['Jaw'].matrix=transform@b.matrix_local

def build_serpent():
    clean(); out=Path('game/assets/guardians/canopy_serpent'); out.mkdir(parents=True,exist_ok=True)
    mats=[pbr('BoaDorsalScales',out,'snake',(.095,.205,.083),.59),pbr('BoaVentralScutes',out,'ventral',(.42,.39,.235),.64),solid('BoaEyeGold',(.29,.155,.034),.34),solid('BoaPupil',(.003,.008,.005),.20),solid('BoaLabialPit',(.002,.004,.002),.95),solid('BoaMouth',(.11,.055,.045),.70),solid('IvoryTeeth',(.62,.55,.40),.44)]
    centers=[(f'Body{i:02d}',-.94+i*.082) for i in range(33)]
    seg=[(n,(0,.116,z),(0,.116,z+.082)) for n,z in centers]
    seg.extend([('Head',(0,.145,-.94),(0,.145,-1.27)),('Jaw',(0,.125,-.955),(0,.125,-1.27))])
    rig=rig_create('CanopyBoaRig',seg); body=Geometry(); head=Geometry(); details=Geometry()
    # A muscular, modestly flattened body with long progressive taper.
    profile=[(-.98,.118,.044,.058),(-.83,.103,.055,.062),(-.60,.071,.073,.077),(-.30,.083,.087,.091),(0,.087,.095,.097),(.35,.085,.093,.094),(.66,.076,.079,.083),(.92,.061,.064,.066),(1.13,.047,.048,.050),(1.35,.031,.029,.031),(1.53,.017,.014,.015),(1.69,.006,.002,.003)]
    loft(body,profile,centers,step=.012,radial=48,snake=True)
    # Diamond-shaped head has independent jaw and supraocular / temporal masses.
    hp=[(-1.330,.148,.027,.020),(-1.318,.149,.037,.022),(-1.285,.151,.050,.025),(-1.235,.154,.064,.031),(-1.185,.157,.083,.037),(-1.10,.160,.098,.042),(-1.015,.148,.077,.038),(-.94,.135,.042,.039)]
    boa_skull(head,hp)
    boa_jaw(head,hp)
    for side in (-1,1):
        angle=1.28 if side==1 else TAU-1.28
        p,n=boa_face_point(hp,-1.177,angle,False)
        # Globe embedded behind the socket, with an iris on the exposed cap.
        ellipsoid(details,p-n*.006,(.008,.010,.012),'Head',2,32,20)
        cavity_disc(details,p+n*.00215,n,.0065,.0012,'Head',3)
        cavity_rim(head,p+n*.0005,n,.0088,.0118,'Head',0)
        angle=1.08 if side==1 else TAU-1.08
        p,n=boa_face_point(hp,-1.291,angle,False)
        cavity_rim(head,p-n*.001,n,.0024,.0035,'Head',0)
        cavity_disc(details,p-n*.0025,n,.0024,.0035,'Head',4)
        # Labial openings sit in thin scale rims, above the sealed mouth.
        for k in range(7):
            z=-1.272+k*.037; a=1.50 if side==1 else TAU-1.50
            p,n=boa_face_point(hp,z,a,False)
            cavity_rim(head,p-n*.001,n,.0024,.0042,'Head',0)
            cavity_disc(details,p-n*.002,n,.0024,.0042,'Head',4)
        lip=[]
        for z in np.linspace(-1.330,-.99,45):
            cy,rx,ry=interp(hp,z); lip.append((rx*side,cy-.0002,z))
        tube(details,lip,[.0007]*len(lip),'Head',5,8)
        # Fine curved dentition becomes visible in feed jaw opening.
        for k in range(7):
            z=-1.25+k*.032; cy,rx,ry=interp(hp,z); px=rx*.66*side
            tube(details,[(px,.147,z),(px,.136,z+.004),(px*.98,.126,z+.011)],[.0018,.0013,.0001],'Head',6,8)
    # Main skull has dedicated facial UV spacing; map jaw onto ventral atlas.
    # Additional thin socket/scute rims share restrained facial plate normals.
    bodies=[body.object('BoaMuscularBody',mats,rig),head.object('BoaHeadAndJaw',mats,rig),details.object('BoaEyesPitsTeethMarkings',mats,rig)]
    animate(rig,serpent_motion,('idle','walk','slither','feed','rest'))
    finish('canopy_serpent',out,rig,bodies)

def finish(species,out,rig,bodies):
    scene=bpy.context.scene; scene.frame_start=1; scene.frame_end=61
    bpy.ops.export_scene.gltf(filepath=str((out/'guardian.glb').resolve()),export_format='GLB',export_animations=True,export_skins=True,export_animation_mode='NLA_TRACKS',export_force_sampling=True,export_apply=False)
    # Exact .blend enables reproducible neutral / animation validation.
    bpy.ops.wm.save_as_mainfile(filepath=str((REPORT/(species+'-source.blend')).resolve()))
    audit={'species':species,'source_axis':'Y up, -Z forward; preserve runtime root +90X','vertices':sum(len(o.data.vertices) for o in bodies),'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in bodies),'joints':len(rig.data.bones),'clips':[a.name for a in bpy.data.actions],'glb_bytes':(out/'guardian.glb').stat().st_size,'materials':[m.name for m in bpy.data.materials],'textures':[im.name for im in bpy.data.images if im.type=='IMAGE'],'animation_bounds':{},'nominal_source_speed_mps':.20 if species=='armored_forest' else math.pi/7.85,'clip_duration_seconds':2,'limits':['Procedural anatomically grounded analogue, not scan-derived photorealism.','Pangolin rest is supported neutral rest, not defensive ball.','Boa rest is a safe loose planar coil, not an arboreal saddle drape.','Walk is in-place; match nominal authored speed at runtime.']}
    # Sample every frame of every action; finite coordinates, measured lower
    # bounds and bounding ranges become evidence rather than pose assertions.
    deps=bpy.context.evaluated_depsgraph_get()
    for action in bpy.data.actions:
        rig.animation_data.action=action; low=1e9; high=-1e9
        for frame in range(1,62):
            scene.frame_set(frame); deps.update()
            for ob in bodies:
                ev=ob.evaluated_get(deps); mesh=ev.to_mesh()
                coords=np.empty(len(mesh.vertices)*3,dtype=np.float32)
                mesh.vertices.foreach_get('co',coords); coords=coords.reshape((-1,3))
                if not np.isfinite(coords).all(): raise RuntimeError('Non-finite deformation')
                low=min(low,float(coords[:,1].min())); high=max(high,float(coords[:,1].max()))
                ev.to_mesh_clear()
        audit['animation_bounds'][action.name]={'min_y':round(low,6),'max_y':round(high,6),'sampled_frames':61}
    rig.animation_data.action=None
    for b in rig.pose.bones: b.matrix_basis=Matrix.Identity(4)
    scene.frame_set(1)
    if species=='armored_forest':
        audit['planted_ik']=audit_planted_ik(rig,bodies)
        rig.animation_data.action=None
        for b in rig.pose.bones: b.matrix_basis=Matrix.Identity(4)
        scene.frame_set(1)
    (REPORT/(species+'-audit.json')).write_text(json.dumps(audit,indent=2))
    render_views(species,bodies)

def audit_planted_ik(rig,bodies):
    scene=bpy.context.scene; rig.animation_data.action=bpy.data.actions['walk']
    groups={}; sole_groups={}; gap=0; length_error=0; flat_degrees=0; sole_y=[]; traces=[]
    skin=bodies[0]; representative={}
    for label in ('FL','FR','HL','HR'):
        group_index=skin.vertex_groups[label+'Foot'].index
        candidates=[v for v in skin.data.vertices if v.co.y<.02 and any(g.group==group_index and g.weight>.995 for g in v.groups)]
        representative[label]=min(candidates,key=lambda v:v.co.y).index
    deps=bpy.context.evaluated_depsgraph_get()
    for half_frame in range(121):
        frame=1+half_frame*.5; scene.frame_set(int(frame),subframe=frame-int(frame))
        t=(frame-1)/60; seconds=t*2
        evaluated=skin.evaluated_get(deps); mesh=evaluated.to_mesh()
        for label,phase_offset in (('FL',0),('FR',.5),('HL',.5),('HR',0)):
            u=(t+phase_offset)%1; up=rig.pose.bones[label+'Upper']; low=rig.pose.bones[label+'Lower']; paw=rig.pose.bones[label+'Foot']
            gap=max(gap,(up.tail-low.head).length,(low.tail-paw.head).length)
            for pb in (up,low): length_error=max(length_error,abs((pb.tail-pb.head).length-pb.bone.length))
            relative=paw.matrix.to_3x3()@paw.bone.matrix_local.to_3x3().inverted()
            flat_degrees=max(flat_degrees,math.degrees(relative.to_quaternion().angle))
            if u<.5:
                group=(label,math.floor(t+phase_offset)); p=paw.head.copy(); p.z-=.2*seconds
                groups.setdefault(group,[]).append(p); sole_y.append(paw.head.y)
                sole=mesh.vertices[representative[label]].co.copy(); sole.z-=.2*seconds
                sole_groups.setdefault(group,[]).append(sole)
            sole=mesh.vertices[representative[label]].co
            traces.append([round(frame,3),round(seconds,6),label,u<.5,float(paw.head.x),float(paw.head.y),float(paw.head.z),float(sole.x),float(sole.y),float(sole.z),float(sole.z-.2*seconds)])
        evaluated.to_mesh_clear()
    slip=max((p-group[0]).length for group in groups.values() for p in group)
    sole_slip=max((p-group[0]).length for group in sole_groups.values() for p in group)
    planted_soles=[p.y for group in sole_groups.values() for p in group]
    with (REPORT/'pangolin-v2-contact-trace.csv').open('w',newline='') as handle:
        writer=csv.writer(handle); writer.writerow(['frame','seconds','foot','stance','bone_x','bone_y','bone_z','sole_x','sole_y','sole_z','virtual_world_sole_z']); writer.writerows(traces)
    result={'sampled_half_frames':121,'max_joint_gap_m':gap,'max_segment_length_error_m':length_error,'max_virtual_world_planted_paw_drift_m':slip,'max_virtual_world_planted_sole_drift_m':sole_slip,'planted_sole_y_range_m':[min(planted_soles),max(planted_soles)],'max_paw_orientation_error_degrees':flat_degrees,'planted_paw_origin_y_range_m':[min(sole_y),max(sole_y)],'source_forward_speed_mps':.2,'retreat_required_playback_multiplier':1.8,'hierarchy':'Root -> Upper -> Lower -> Foot, connected joints','pass':gap<1e-5 and length_error<1e-5 and slip<.001 and sole_slip<.001 and min(planted_soles)>0 and flat_degrees<.1}
    if not result['pass']: raise RuntimeError('Planted IK contact audit failed '+json.dumps(result))
    return result

def render_views(species,bodies):
    scene=bpy.context.scene; scene.render.engine='CYCLES'; scene.cycles.samples=12
    scene.cycles.use_denoising=True
    # Use GPU raster evidence for bounded iteration; all exported material
    # inputs remain the same image PBR nodes used in the Cycles path.
    scene.render.engine='CYCLES' if os.environ.get('GUARDIAN_CYCLES')=='1' else 'BLENDER_EEVEE'
    scene.render.resolution_x=900; scene.render.resolution_y=650; scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new('NeutralWorld'); scene.world.use_nodes=True
    scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.11,.13,.15,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.6
    scene.view_settings.view_transform='AgX'
    # Ground plane is XZ because source up remains Y.
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,-.006,0),rotation=(math.pi/2,0,0)); plane=bpy.context.object
    plane.data.materials.append(solid('StudioGround',(.16,.18,.20),.75))
    for name,pos,power,size in [('Key',(2.5,4,-2),500,4),('Fill',(-3,2,-.5),300,3),('Rim',(1,3,3),650,3)]:
        data=bpy.data.lights.new(name,'AREA'); data.energy=power; data.shape='DISK'; data.size=size
        ob=bpy.data.objects.new(name,data); scene.collection.objects.link(ob); ob.location=pos; ob.rotation_euler=(Vector((0,.2,.3))-ob.location).to_track_quat('-Z','Y').to_euler()
    camdata=bpy.data.cameras.new('EvidenceCamera'); cam=bpy.data.objects.new('EvidenceCamera',camdata); scene.collection.objects.link(cam); scene.camera=cam; camdata.type='ORTHO'; camdata.ortho_scale=3.35
    target=Vector((0,.22,.35))
    views={'side':(4,1,.35),'front':(0,1,-4),'three_quarter':(3,2,-3)}
    clay=solid('NeutralClay',(.38,.40,.42),.6)
    original=[[m for m in ob.data.materials] for ob in bodies]
    for mode in (() if os.environ.get('GUARDIAN_FACE_ONLY')=='1' else ('pbr','clay')):
        if mode=='clay':
            for ob in bodies:
                for i in range(len(ob.data.materials)): ob.data.materials[i]=clay
        for view,position in views.items():
            cam.location=position
            forward=(target-cam.location).normalized(); right=forward.cross(Vector((0,1,0))).normalized(); up=right.cross(forward)
            cam.rotation_euler=Matrix((right,up,-forward)).transposed().to_euler()
            scene.render.filepath=str((REPORT/(species+'-'+mode+'-'+view+'.png')).resolve()); bpy.ops.render.render(write_still=True)
    for ob,mats in zip(bodies,original):
        for i,m in enumerate(mats): ob.data.materials[i]=m
    camdata.ortho_scale=.57 if species=='armored_forest' else .60
    aim=Vector((0,.24,-.66)) if species=='armored_forest' else Vector((0,.16,-1.14))
    cam.location=(.8,.55,-1.5 if species=='armored_forest' else -1.9)
    forward=(aim-cam.location).normalized(); right=forward.cross(Vector((0,1,0))).normalized(); up=right.cross(forward)
    cam.rotation_euler=Matrix((right,up,-forward)).transposed().to_euler()
    scene.render.filepath=str((REPORT/(species+'-face-closeup.png')).resolve()); bpy.ops.render.render(write_still=True)
    if os.environ.get('GUARDIAN_FACE_ONLY')=='1':
        print('ANATOMICAL_GUARDIAN_COMPLETE',species); return
    camdata.ortho_scale=3.35
    rig=bodies[0].parent
    for clip,frame in (('walk',16),('feed',31),('rest',1)):
        rig.animation_data.action=bpy.data.actions[clip]; scene.frame_set(frame)
        aim=Vector((.45,.17,-.80)) if species=='canopy_serpent' and clip=='rest' else target
        cam.location=(3,2,-3)
        forward=(aim-cam.location).normalized(); right=forward.cross(Vector((0,1,0))).normalized(); up=right.cross(forward)
        cam.rotation_euler=Matrix((right,up,-forward)).transposed().to_euler()
        scene.render.filepath=str((REPORT/(species+'-pose-'+clip+'.png')).resolve()); bpy.ops.render.render(write_still=True)
    if species=='armored_forest':
        rig.animation_data.action=bpy.data.actions['walk']
        camdata.ortho_scale=1.55
        aim=Vector((0,.22,-.01)); cam.location=(4,.7,.05)
        forward=(aim-cam.location).normalized(); right=forward.cross(Vector((0,1,0))).normalized(); up=right.cross(forward)
        cam.rotation_euler=Matrix((right,up,-forward)).transposed().to_euler()
        for frame in (1,16,31,46):
            scene.frame_set(frame)
            scene.render.filepath=str((REPORT/f'pangolin-v2-walk-contact-f{frame:03}.png').resolve()); bpy.ops.render.render(write_still=True)
    rig.animation_data.action=None
    for b in rig.pose.bones: b.matrix_basis=Matrix.Identity(4)
    print('ANATOMICAL_GUARDIAN_COMPLETE',species)
