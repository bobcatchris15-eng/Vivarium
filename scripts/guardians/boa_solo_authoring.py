"""Boa solo v8, selective incoming anatomy on the detailed v6 rig; source Y-up/-Z-forward."""

import bpy,bmesh,math,json,os,csv
from pathlib import Path
from mathutils import Vector,Matrix,kdtree
import numpy as np
TAU=math.tau
REPORT=Path(".clanker/tasks/guardian-boa-solo/v8-authoring")

# Selectively retain the incoming constricted cervical silhouette and
# taller/narrower arboreal trunk. Keep the original length, 33 skin stations,
# grounded ventrum, facial detail, embedded PBR maps and deformation rig.
BOA_PROFILE=[(-.98,.136,.038,.040),(-.90,.124,.041,.046),(-.83,.103,.046,.062),(-.60,.071,.061,.077),(-.30,.083,.073,.091),(0,.087,.079,.097),(.35,.085,.078,.094),(.66,.076,.066,.083),(.92,.061,.054,.066),(1.13,.047,.042,.050),(1.35,.031,.027,.031),(1.53,.017,.014,.015),(1.69,.006,.002,.003)]

def ventral_factor(z):
    u=max(0,min(1,(z+.94)/.13));u=u*u*(3-2*u)
    return .30+.56*u

def clean():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    REPORT.mkdir(parents=True, exist_ok=True)
    bpy.context.scene.render.fps = 30

def image(name, pixels, directory, noncolor=False, encode_linear_color=False):
    h,w,_=pixels.shape
    if encode_linear_color:
        pixels=pixels.copy(); rgb=pixels[:,:,:3]
        pixels[:,:,:3]=np.where(rgb<=.0031308,12.92*rgb,1.055*np.maximum(rgb,0)**(1/2.4)-.055)
    im=bpy.data.images.new(name, width=w, height=h, alpha=True)
    if noncolor: im.colorspace_settings.name='Non-Color'
    im.pixels.foreach_set(pixels.astype(np.float32).ravel())
    im.filepath_raw=str((directory/(name+'.png')).resolve())
    im.file_format='PNG'; im.save()
    if encode_linear_color:
        # Reload the encoded byte image so Blender and glTF consume the same
        # sRGB source, rather than retaining a generated float buffer.
        path=im.filepath_raw; bpy.data.images.remove(im)
        im=bpy.data.images.load(path,check_existing=False); im.name=name
        im.colorspace_settings.name='sRGB'
    im.pack()
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
        # One whole-body atlas contains60circumferential x216longitudinal cells.
        row=np.floor(y*216); shift=row*.5+.08*np.sin(row*2.31)+.04*np.sin(row*7.13)
        col=np.floor(x*60+shift); cx=(x*60+shift)%1; cy=(y*216)%1
        width=.48+.025*np.sin(col*3.47+row*1.23)
        length=.55+.025*np.sin(col*2.30-row*.70)
        center=.50+.020*np.sin(col*.70+row*1.23)
        d=((cx-.5)/width)**2+((cy-center)/length)**2
        dome=np.clip(1-d,0,1)
        edge=np.clip((1-d)*12,0,1)
        grain=np.sin(x*TAU*479+y*TAU*373)*np.sin(y*TAU*433)
        # Low-relief micro-scales across one whole-body UV atlas. Broad
        # pigment/roughness variation breaks the uniform shiny toy response.
        mottle=.5*np.sin(y*TAU*7+np.sin(x*TAU*3))+.3*np.sin(y*TAU*19-x*TAU*4)
        ridge=np.exp(-((d-.86)/.14)**2)
        # Restrained convex keratin relief, with irregular growth striae.
        striae=np.sin(cx*TAU*8+cy*2.4+row*.73)*dome
        height=.0030*dome+.00024*ridge+.00007*striae+.00006*grain
        pigment=.018*np.sin(col*2.47+row*.719)+.012*np.sin(col*1.19-row*2.77)
        modulation=.86+.045*edge+.025*dome+.045*mottle+pigment+.006*grain
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
        blend=np.clip(marking*.72,0,1)[:,:,None]
        rgba[:,:,:3]=rgba[:,:,:3]*(1-blend)+np.array((.24,.29,.17))[None,None,:]*blend
    color=image(name+'_basecolor',rgba,directory,encode_linear_color=kind in ('snake','ventral'))
    r=np.ones_like(rgba); val=np.clip(rough+.10*(1-modulation),.15,.95)
    if kind in ('snake','ventral'): val=np.clip(val+.035*mottle+.012*grain if kind=='snake' else val+.025*mottle,.35,.80)
    if kind=='snake': val=np.clip(val+.075*(1-edge)-.020*dome,.48,.78)
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
        if name=='Tongue': b.parent=data.edit_bones['Head']
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
            yc=max(c,-ventral_factor(z)) if snake else c
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

def boa_face_point(profile,z,a,depress=True):
    cy,rx,ry=interp(profile,z); s=math.sin(a); c=math.cos(a)
    # Broad roof and nearly vertical muzzle flanks; lower skull seals to jaw.
    x=rx*math.copysign(abs(s)**(.55 if c>=0 else .35),s)
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
            floor=.117+.013*math.exp(-((z+1.33)/.080)**2)+.002*math.exp(-((z+.94)/.085)**2)
            y=cy-(cy-.133)*c**.30 if c>=0 else cy-(cy-floor)*abs(c)**.72
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

def keyall(rig,frame):
    for b in rig.pose.bones:
        for channel in ('location','rotation_quaternion','scale'): b.keyframe_insert(channel,frame=frame)

def animate(rig,callback,names=('idle','walk','feed','rest')):
    for name in names:
        act=bpy.data.actions.new(name); rig.animation_data.action=act
        for f in range(0,61):
            bpy.context.scene.frame_set(f)
            for b in rig.pose.bones: b.matrix_basis=Matrix.Identity(4)
            bpy.context.view_layer.update()
            callback(name,f/60,rig)
            keyall(rig,f)
        for layer in act.layers:
            for action_strip in layer.strips:
                for bag in action_strip.channelbags:
                    for curve in bag.fcurves:
                        for point in curve.keyframe_points: point.interpolation='LINEAR'
        track=rig.animation_data.nla_tracks.new(); track.name=name; strip=track.strips.new(name,0,act); strip.name=name
        track.mute=True
    rig.animation_data.action=None
    for b in rig.pose.bones: b.matrix_basis=Matrix.Identity(4)
    bpy.context.scene.frame_set(0)

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
    bpy.context.view_layer.update()
    # A restrained neck lift blends through the same continuous skin, with
    # positive authored support offset for runtime terrain fitting to retain.
    for i in range(33):
        pb=rig.pose.bones[f'Body{i:02d}'];m=pb.matrix.copy()
        lift=(.004*math.exp(-i/2.4) if clip in ('idle','feed') else .001*math.exp(-i/2.4))*math.sin(phase)**2
        m.translation.y+=lift;pb.matrix=m
    bpy.context.view_layer.update()
    first=rig.pose.bones['Body00'];delta=first.matrix@first.bone.matrix_local.inverted()
    # Same deformation maps Head and Jaw; the neck cannot be left behind.
    pitch=.010*math.sin(phase) if clip=='idle' else 0
    hp=rig.data.bones['Head'].head_local
    head_delta=delta@Matrix.Translation(hp)@Matrix.Rotation(pitch,4,'X')@Matrix.Translation(-hp)
    rig.pose.bones['Head'].matrix=head_delta@rig.data.bones['Head'].matrix_local
    opening=.12*math.sin(math.pi*t)**2 if clip=='feed' else 0
    jp=rig.data.bones['Jaw'].head_local
    rig.pose.bones['Jaw'].matrix=head_delta@Matrix.Translation(jp)@Matrix.Rotation(-opening,4,'X')@Matrix.Translation(-jp)@rig.data.bones['Jaw'].matrix_local
    bpy.context.view_layer.update()
    taste=math.sin(math.pi*(t-.18)/.52)**2 if clip=='feed' and .18<t<.70 else 0
    b=rig.data.bones['Tongue'];rig.pose.bones['Tongue'].location=b.matrix_local.to_3x3().inverted()@Vector((0,0,-.115*taste))

def build_serpent():
    clean(); out=Path('game/assets/guardians/canopy_serpent'); out.mkdir(parents=True,exist_ok=True)
    mats=[pbr('BoaDorsalScales',out,'snake',(.040,.115,.038),.62),pbr('BoaVentralScutes',out,'ventral',(.22,.20,.13),.66),solid('BoaEyeGold',(.14,.10,.035),.18),solid('BoaPupil',(.003,.008,.005),.16),solid('BoaLabialPit',(.002,.004,.002),.95),solid('BoaMouth',(.075,.040,.036),.78),solid('IvoryTeeth',(.62,.55,.40),.44),solid('BoaForkedTongue',(.13,.026,.031),.38)]
    centers=[(f'Body{i:02d}',-.94+i*.082) for i in range(33)]
    seg=[(n,(0,interp(BOA_PROFILE,z)[0],z),(0,interp(BOA_PROFILE,min(z+.082,1.69))[0],min(z+.082,1.69))) for n,z in centers]
    seg.extend([('Head',(0,.145,-.94),(0,.145,-1.27)),('Jaw',(0,.125,-.955),(0,.125,-1.27))])
    seg.append(('Tongue',(0,.139,-1.265),(0,.139,-1.295)))
    rig=rig_create('CanopyBoaRig',seg); body=Geometry(); head=Geometry(); details=Geometry()
    # A muscular, modestly flattened body with long progressive taper.
    profile=BOA_PROFILE
    loft(body,profile,centers,step=.012,radial=48,snake=True)
    # Diamond-shaped head has independent jaw and supraocular / temporal masses.
    hp=[(-1.330,.148,.027,.020),(-1.318,.149,.037,.022),(-1.285,.151,.058,.027),(-1.235,.154,.071,.033),(-1.185,.157,.083,.037),(-1.10,.160,.098,.042),(-1.015,.148,.069,.038),(-.94,.135,.038,.039)]
    boa_skull(head,hp)
    boa_jaw(head,hp)
    for side in (-1,1):
        angle=1.28 if side==1 else TAU-1.28
        p,n=boa_face_point(hp,-1.177,angle,False)
        # Globe embedded behind the socket, with an iris on the exposed cap.
        ellipsoid(details,p-n*.0075,(.008,.010,.012),'Head',2,32,20)
        cavity_disc(details,p+n*.0010,n,.0065,.0012,'Head',3)
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
        tube(details,lip,[.0004]*len(lip),'Head',5,8)
        # Fine curved dentition becomes visible in feed jaw opening.
        for k in range(7):
            z=-1.25+k*.032; cy,rx,ry=interp(hp,z); px=rx*.66*side
            tube(details,[(px,.147,z),(px,.136,z+.004),(px*.98,.126,z+.011)],[.0018,.0013,.0001],'Head',6,8)
    # Forked tongue rests inside the shut mouth and extends only while tasting.
    tube(details,[(0,.139,-1.265),(0,.139,-1.287)],[.0017,.0013],'Tongue',7,10)
    for side in (-1,1):
        tube(details,[(0,.139,-1.287),(.003*side,.139,-1.295),(.005*side,.139,-1.302)],[.0013,.0008,.0003],'Tongue',7,8)
    # Main skull has dedicated facial UV spacing; map jaw onto ventral atlas.
    # Additional thin socket/scute rims share restrained facial plate normals.
    bodies=[body.object('BoaMuscularBody',mats,rig),head.object('BoaHeadAndJaw',mats,rig),details.object('BoaEyesPitsTeethMarkings',mats,rig)]
    animate(rig,serpent_motion,('idle','walk','slither','feed','rest'))
    finish('canopy_serpent',out,rig,bodies)

def finish(species,out,rig,bodies):
    scene=bpy.context.scene; scene.frame_start=0; scene.frame_end=60
    # Remove duplicate-pole triangles and verify normalized weights before export.
    for ob in bodies:
        bm=bmesh.new();bm.from_mesh(ob.data)
        bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.0000002)
        bad=[face for face in bm.faces if face.calc_area()<1e-12]
        bmesh.ops.delete(bm,geom=bad,context='FACES');bm.to_mesh(ob.data);bm.free();ob.data.update()
    bpy.ops.export_scene.gltf(filepath=str((out/'guardian.glb').resolve()),export_format='GLB',export_animations=True,export_skins=True,export_animation_mode='NLA_TRACKS',export_force_sampling=True,export_optimize_animation_size=False,export_apply=False)
    # Exact .blend enables reproducible neutral / animation validation.
    bpy.ops.wm.save_as_mainfile(filepath=str((REPORT/(species+'-source.blend')).resolve()))
    audit={'species':species,'source_axis':'Y up, -Z forward; preserve runtime root +90X','vertices':sum(len(o.data.vertices) for o in bodies),'triangles':sum(sum(len(p.vertices)-2 for p in o.data.polygons) for o in bodies),'joints':len(rig.data.bones),'clips':[a.name for a in bpy.data.actions],'glb_bytes':(out/'guardian.glb').stat().st_size,'materials':[m.name for m in bpy.data.materials],'textures':[im.name for im in bpy.data.images if im.type=='IMAGE'],'animation_bounds':{},'nominal_source_speed_mps':.20 if species=='armored_forest' else math.pi/7.85,'clip_duration_seconds':2,'limits':['Procedural anatomically grounded analogue, not scan-derived photorealism.','Pangolin rest is supported neutral rest, not defensive ball.','Boa rest is a safe loose planar coil, not an arboreal saddle drape.','Walk is in-place; match nominal authored speed at runtime.']}
    # Sample every frame of every action; finite coordinates, measured lower
    # bounds and bounding ranges become evidence rather than pose assertions.
    deps=bpy.context.evaluated_depsgraph_get()
    for action in bpy.data.actions:
        rig.animation_data.action=action; low=1e9; high=-1e9
        for frame in range(0,61):
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
    scene.frame_set(0)
    if species=='armored_forest':
        audit['planted_ik']=audit_planted_ik(rig,bodies)
        rig.animation_data.action=None
        for b in rig.pose.bones: b.matrix_basis=Matrix.Identity(4)
        scene.frame_set(1)
    (REPORT/(species+'-audit.json')).write_text(json.dumps(audit,indent=2))
    detailed_audit(rig,bodies)
    # This task is export-only. Retained renderer is unused legacy evidence
    # code; root owns native Godot review and all art acceptance.
    print('ANATOMICAL_GUARDIAN_EXPORT_COMPLETE',species)

def detailed_audit(rig,bodies):
    scene=bpy.context.scene;deps=bpy.context.evaluated_depsgraph_get()
    report={'source_axis':'Y up/-Z forward inside Blender; runtime+90X preserved','body_profile':BOA_PROFILE,'body_stations':[],'tip':{'source_center':[0,.006,1.69],'lateral_radius_m':.002,'ground_radius_m':.86*.003},'joint_count':len(rig.data.bones),'clips':{},'mesh_structure':{}}
    for i in range(33):
        b=rig.data.bones[f'Body{i:02d}'];cy,rx,ry=interp(BOA_PROFILE,b.head_local.z)
        report['body_stations'].append({'name':b.name,'parent':b.parent.name,'head':list(b.head_local),'leaf':list(b.tail_local),'bone_length_m':b.length,'mesh_center_y':cy,'nominal_lateral_radius_m':rx,'maximum_lateral_radius_m':rx*(1+.045+.018),'vertical_ground_radius_m':ventral_factor(b.head_local.z)*ry,'ventral_flatten_factor':ventral_factor(b.head_local.z),'dorsal_radius_m':1.035*ry})
    report['head_jaw_tongue']={name:{'parent':rig.data.bones[name].parent.name,'head':list(rig.data.bones[name].head_local),'leaf':list(rig.data.bones[name].tail_local),'length':rig.data.bones[name].length} for name in ('Head','Jaw','Tongue')}
    for ob in bodies:
        weights=[sum(g.weight for g in v.groups) for v in ob.data.vertices]
        report['mesh_structure'][ob.name]={'vertices':len(ob.data.vertices),'triangles':sum(len(p.vertices)-2 for p in ob.data.polygons),'minimum_rest_y':min(v.co.y for v in ob.data.vertices),'maximum_weight_sum_error':max(abs(w-1) for w in weights),'degenerate_polygons':sum(p.area<1e-12 for p in ob.data.polygons),'nonfinite_normals':sum(not all(math.isfinite(x) for x in v.normal) for v in ob.data.vertices)}
    for action in bpy.data.actions:
        rig.animation_data.action=action;lower=1e6;headlower=1e6;bonelower=1e6;maximum_centerline_strain=0;minimum_nonlocal_gap=1e6;firstcoords=None;lastcoords=None;maxheadattachment=0;headlift=[1e6,-1e6]
        for half in range(121):
            frame=half*.5;scene.frame_set(int(frame),subframe=frame-int(frame));deps.update()
            joints=[rig.pose.bones[f'Body{i:02d}'].head.copy() for i in range(33)]
            bonelower=min(bonelower,min(p.y for p in joints));headlift[0]=min(headlift[0],joints[0].y-rig.data.bones['Body00'].head_local.y);headlift[1]=max(headlift[1],joints[0].y-rig.data.bones['Body00'].head_local.y)
            for i in range(32):
                rest=(rig.data.bones[f'Body{i+1:02d}'].head_local-rig.data.bones[f'Body{i:02d}'].head_local).length
                maximum_centerline_strain=max(maximum_centerline_strain,abs((joints[i+1]-joints[i]).length/rest-1))
            for i in range(33):
                for j in range(i+5,33):
                    ri=report['body_stations'][i]['maximum_lateral_radius_m'];rj=report['body_stations'][j]['maximum_lateral_radius_m']
                    minimum_nonlocal_gap=min(minimum_nonlocal_gap,(joints[i]-joints[j]).length-ri-rj)
            coordinates=[]
            for ob in bodies:
                ev=ob.evaluated_get(deps);mesh=ev.to_mesh();coords=np.empty(len(mesh.vertices)*3,np.float32);mesh.vertices.foreach_get('co',coords);coords=coords.reshape(-1,3)
                if not np.isfinite(coords).all():raise RuntimeError('Nonfinite dense pose '+action.name)
                lower=min(lower,float(coords[:,1].min()))
                if ob.name=='BoaHeadAndJaw':headlower=min(headlower,float(coords[:,1].min()))
                coordinates.append(coords.copy());ev.to_mesh_clear()
            if half==0:firstcoords=coordinates
            if half==120:lastcoords=coordinates
        loop=max(float(np.linalg.norm(a-b,axis=1).max()) for a,b in zip(firstcoords,lastcoords))
        report['clips'][action.name]={'dense_samples':121,'minimum_mesh_y':lower,'minimum_head_jaw_y':headlower,'minimum_bone_origin_y':bonelower,'maximum_centerline_relative_strain':maximum_centerline_strain,'minimum_nonlocal_centerline_capsule_gap':minimum_nonlocal_gap,'self_overlap_test_limit':'Nonlocal centerline spheres with5joint exclusion; not exhaustive triangle/cavity self-collision.','loop_endpoint_maximum_mesh_difference_m':loop,'first_body_authored_lift_range_m':headlift}
    rig.animation_data.action=None
    for pb in rig.pose.bones:pb.matrix_basis=Matrix.Identity(4)
    scene.frame_set(0)
    report['neutral_mesh_bounds']={ob.name:{'min':[min(v.co[k] for v in ob.data.vertices) for k in range(3)],'max':[max(v.co[k] for v in ob.data.vertices) for k in range(3)]} for ob in bodies}
    report['nominal_speed_mps']=math.pi/7.85;report['cycle_seconds']=2;report['retreat_playback_multiplier']=1.8
    (REPORT/'rig-contact-deformation-audit.json').write_text(json.dumps(report,indent=2))
    assert all(c['minimum_mesh_y']>0 and c['loop_endpoint_maximum_mesh_difference_m']<1e-5 for c in report['clips'].values())

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
    if species=='canopy_serpent': camdata.ortho_scale=3.75
    target=Vector((0,.22,.15 if species=='canopy_serpent' else .35))
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
