"""Ground guardian authoring. Explicit UV topology, anatomical part weights, baked IK.

Coordinates deliberately remain X right, Y up, -Z forward in the Blender scene.
glTF export converts Blender axes; Vivarium's existing +90X instance rotation restores
the authored convention. Do not apply a second axis conversion here.
"""
import bpy, math, json, struct, random, os
from pathlib import Path
from mathutils import Vector, Matrix

ROOT = Path(__file__).resolve().parents[2]
TASK = ROOT / '.clanker/tasks' / os.environ.get('GROUND_ART_TASK_ID','guardian-ground-art')
TAU = math.tau

def active(obj):
    bpy.ops.object.select_all(action='DESELECT'); obj.select_set(True)
    bpy.context.view_layer.objects.active = obj

class Builder:
    def __init__(self, name, rig, mat):
        self.name=name; self.rig=rig; self.mat=mat
        self.v=[]; self.f=[]; self.uv=[]; self.w=[]; self.col=[]
    def ringmesh(self, nodes, radial=32, color=(1,1,1,1), uvscale=1):
        # Nodes: center, ellipse radii, blend weights. Adaptive tangent frames
        # prevent the collapsed diagonal limb sections in the original assets.
        start=len(self.v); n=len(nodes)
        for i,(c,rx,ry,weights) in enumerate(nodes):
            c=Vector(c)
            d=Vector(nodes[min(i+1,n-1)][0])-Vector(nodes[max(i-1,0)][0]); d.normalize()
            ref=Vector((0,1,0)) if abs(d.y)<.92 else Vector((0,0,1))
            a=d.cross(ref).normalized(); b=a.cross(d).normalized()
            for j in range(radial+1):
                q=TAU*j/radial
                self.v.append(tuple(c+a*(math.sin(q)*rx)+b*(math.cos(q)*ry)))
                self.uv.append((j/radial,i/max(1,n-1)*uvscale)); self.w.append(weights)
                self.col.append(color(c,q,i) if callable(color) else color)
        for i in range(n-1):
            for j in range(radial):
                k=start+i*(radial+1)+j
                self.f.append((k,k+radial+1,k+radial+2,k+1))
        for i,reverse in [(0,True),(n-1,False)]:
            k=len(self.v); c=nodes[i][0]
            self.v.append(c); self.uv.append((.5,i/max(1,n-1)*uvscale)); self.w.append(nodes[i][3])
            self.col.append(color(Vector(c),0,i) if callable(color) else color)
            r=start+i*(radial+1)
            for j in range(radial):
                self.f.append((r+j+1,r+j,k) if reverse else (r+j,r+j+1,k))
    def ellipsoid(self,c,r,weights,color=(1,1,1,1),segments=24):
        # ring representation has clean latitude UVs and no operator transforms.
        nodes=[]
        for i in range(17):
            t=-math.pi/2+math.pi*i/16
            nodes.append(((c[0],c[1],c[2]+r[2]*math.sin(t)),max(.0003,r[0]*math.cos(t)),max(.0003,r[1]*math.cos(t)),weights))
        self.ringmesh(nodes,segments,color)
    def build(self):
        me=bpy.data.meshes.new(self.name); me.from_pydata(self.v,[],self.f); me.update()
        ob=bpy.data.objects.new(self.name,me); bpy.context.collection.objects.link(ob)
        me.materials.append(self.mat)
        uv=me.uv_layers.new(name='SurfaceUV')
        for p in me.polygons:
            p.use_smooth=True
            for li in p.loop_indices: uv.data[li].uv=self.uv[me.loops[li].vertex_index]
        col=me.color_attributes.new(name='Color',type='FLOAT_COLOR',domain='POINT')
        for i,c in enumerate(self.col): col.data[i].color=c
        for name in self.rig.data.bones.keys(): ob.vertex_groups.new(name=name)
        for i,ws in enumerate(self.w):
            items=sorted([(k,v) for k,v in ws.items() if v>0],key=lambda p:-p[1])[:4]
            total_w=sum(v for _,v in items) or 1.0
            for name,weight in items:
                ob.vertex_groups[name].add([i],weight/total_w,'REPLACE')
        mod=ob.modifiers.new('AnatomicalSkin','ARMATURE'); mod.object=self.rig
        ob.parent=self.rig
        # Normals use actual consistent face orientation, not double-sided masks.
        active(ob); bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
        bpy.ops.mesh.normals_make_consistent(inside=False); bpy.ops.object.mode_set(mode='OBJECT')
        return ob

def material(name,color,rough=.7,texture=None,normal=None):
    m=bpy.data.materials.new(name); m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=(*color,1)
    p.inputs['Roughness'].default_value=rough
    if texture:
        tex=m.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=texture
        # Vertex colors carry the designed regional coat/mask/bands; exported
        # image carries skin microvariation. glTF multiplies COLOR_0 with texture.
        col=m.node_tree.nodes.new('ShaderNodeVertexColor'); col.layer_name='Color'
        mul=m.node_tree.nodes.new('ShaderNodeMixRGB'); mul.blend_type='MULTIPLY'; mul.inputs[0].default_value=1
        m.node_tree.links.new(tex.outputs['Color'],mul.inputs[1]); m.node_tree.links.new(col.outputs['Color'],mul.inputs[2])
        m.node_tree.links.new(mul.outputs[0],p.inputs['Base Color'])
    if normal:
        tex=m.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=normal
        nm=m.node_tree.nodes.new('ShaderNodeNormalMap'); nm.inputs['Strength'].default_value=.06 if 'coati' in name else .65
        m.node_tree.links.new(tex.outputs['Color'],nm.inputs['Color']); m.node_tree.links.new(nm.outputs[0],p.inputs['Normal'])
    return m

def images(species,out):
    # Actual image textures, packed and saved, not unsupported procedural nodes.
    import numpy as np
    size=1024; u,v=np.meshgrid(np.arange(size)/size,np.arange(size)/size)
    rng=np.random.default_rng(723 if species=='coati' else 901)
    if species=='coati':
        h=.45*np.sin(TAU*(u*121+v*5))+.3*np.sin(TAU*(u*79-v*8))+.1*rng.random((size,size))
        value=.93+.025*h; rgb=np.stack((value,value*.96,value*.90),axis=-1)
    else:
        # Staggered scale cells, raised centers and shallow seams.
        x=u*128+(np.floor(v*180)%2)*.5; y=v*180
        dx=(x%1)-.5; dy=(y%1)-.5
        h=np.maximum(0,1-(dx*dx*4+dy*dy*4))**.45
        spot=np.sin(TAU*u*7)*np.sin(TAU*v*9)>.55
        value=.80+.10*h; rgb=np.stack((value,value*.96,value*.78),axis=-1)
    rgba=np.concatenate((rgb,np.ones((size,size,1))),axis=-1).astype('float32')
    color=bpy.data.images.new(species+'_albedo',width=size,height=size)
    color.pixels.foreach_set(rgba.ravel()); color.filepath_raw=str(out/'surface_albedo.png'); color.file_format='PNG'; color.save(); color.pack()
    gy,gx=np.gradient(h); n=np.stack((-gx*2,-gy*2,np.ones_like(h)),axis=-1); n/=np.linalg.norm(n,axis=-1)[...,None]
    rgba=np.concatenate((n*.5+.5,np.ones((size,size,1))),axis=-1).astype('float32')
    normal=bpy.data.images.new(species+'_normal',width=size,height=size)
    normal.colorspace_settings.name='Non-Color'; normal.pixels.foreach_set(rgba.ravel())
    normal.filepath_raw=str(out/'surface_normal.png'); normal.file_format='PNG'; normal.save(); normal.pack()
    return color,normal

def config(species):
    if species=='coati':
        # Broad thorax, abdominal tuck, developed haunches; skull separated by
        # a narrow neck and deep cheek rather than one uniform fusiform tube.
        profile=[(-.83,.365,.008,.009),(-.80,.367,.026,.025),(-.75,.375,.035,.028),(-.68,.39,.045,.035),(-.61,.405,.057,.045),(-.56,.425,.075,.061),(-.50,.44,.099,.082),(-.43,.445,.108,.095),(-.36,.43,.095,.09),(-.29,.415,.080,.077),(-.22,.398,.088,.089),(-.14,.383,.116,.118),(-.05,.377,.137,.133),(.06,.378,.142,.143),(.17,.387,.133,.136),(.26,.41,.137,.14),(.35,.421,.129,.128),(.41,.414,.09,.087),(.44,.407,.025,.034)]
        chain=[('Pelvis',(0,.41,.35),(0,.40,.20),'Root'),('Spine1',(0,.40,.20),(0,.38,.06),'Pelvis'),('Chest',(0,.38,.06),(0,.39,-.18),'Spine1'),('Neck',(0,.39,-.18),(0,.43,-.36),'Chest'),('Head',(0,.43,-.36),(0,.42,-.58),'Neck'),('Snout',(0,.42,-.58),(0,.37,-.82),'Head'),('Jaw',(0,.405,-.47),(0,.345,-.74),'Head')]
        limbs=[]
        for side,s in [('L',1),('R',-1)]:
            limbs.append((side,True,Vector((s*.105,.365,-.10)),Vector((s*.13,.235,.045)),Vector((s*.125,.055,-.115))))
            limbs.append((side,False,Vector((s*.105,.395,.30)),Vector((s*.15,.245,.18)),Vector((s*.14,.055,.33))))
        tail=[(0,.44,.38),(.025,.58,.45),(.04,.74,.55),(.02,.87,.68),(-.02,.92,.84),(-.06,.85,.99),(-.08,.70,1.10),(-.06,.58,1.16)]
        return profile,chain,limbs,tail
    profile=[(-1.28,.27,.007,.01),(-1.25,.272,.033,.022),(-1.18,.282,.059,.032),(-1.09,.295,.085,.047),(-1.0,.307,.105,.060),(-.91,.303,.109,.069),(-.83,.291,.096,.081),(-.72,.29,.096,.092),(-.61,.29,.102,.094),(-.49,.282,.16,.138),(-.34,.283,.205,.15),(-.15,.269,.208,.145),(.07,.258,.183,.119),(.25,.264,.191,.134),(.40,.257,.168,.125),(.51,.253,.115,.10),(.57,.245,.055,.064)]
    chain=[('Pelvis',(0,.25,.42),(0,.26,.15),'Root'),('Spine1',(0,.26,.15),(0,.27,-.20),'Pelvis'),('Chest',(0,.27,-.20),(0,.29,-.58),'Spine1'),('Neck',(0,.29,-.58),(0,.30,-.84),'Chest'),('Head',(0,.30,-.84),(0,.28,-1.22),'Neck'),('Jaw',(0,.275,-.90),(0,.235,-1.24),'Head')]
    limbs=[]
    for side,s in [('L',1),('R',-1)]:
        limbs.append((side,True,Vector((s*.145,.26,-.46)),Vector((s*.30,.22,-.36)),Vector((s*.39,.05,-.57))))
        limbs.append((side,False,Vector((s*.145,.245,.39)),Vector((s*.32,.205,.29)),Vector((s*.40,.05,.49))))
    tail=[(0.0, 0.245, 0.49), (0.0146875, 0.24562499999999998, 0.5787499999999999), (0.035, 0.245, 0.7), (0.0578125, 0.24093749999999997, 0.825), (0.08, 0.235, 0.96), (0.10062499999999999, 0.228125, 1.101875), (0.11, 0.22, 1.25), (0.095625, 0.2109375, 1.4031250000000002), (0.065, 0.2, 1.56), (0.016874999999999998, 0.18625, 1.7206249999999998), (-0.035, 0.17, 1.88), (-0.0775, 0.1509375, 2.0337500000000004), (-0.11, 0.13, 2.18), (-0.1271875, 0.106875, 2.3156250000000003), (-0.13, 0.085, 2.44), (-0.11125, 0.0671875, 2.5612500000000002), (-0.09, 0.055, 2.65)]
    return profile,chain,limbs,tail

def rigging(species,chain,limbs,tail):
    ar=bpy.data.armatures.new(species+'_skeleton'); ob=bpy.data.objects.new(species+'_rig',ar); bpy.context.collection.objects.link(ob)
    active(ob); bpy.ops.object.mode_set(mode='EDIT')
    def bone(name,h,t,parent):
        b=ar.edit_bones.new(name); b.head=h; b.tail=t
        if parent: b.parent=ar.edit_bones[parent]
        return b
    bone('Root',(0,0,0),(0,0,-.15),None)
    for name,h,t,parent in chain: bone(name,h,t,parent)
    if species=='coati':
        for side,suffix in [(-1,'R'),(1,'L')]:
            for name in ['Blink_'+suffix,'BlinkLower_'+suffix]: bone(name,(side*.083,.470,-.499),(side*.083,.450,-.499),'Head')
    if species=='monitor':
        for side,suffix in [(-1,'R'),(1,'L')]:
            for name in ['Blink_'+suffix,'BlinkLower_'+suffix]: bone(name,(side*.087,.326,-1.006),(side*.087,.306,-1.006),'Head')
        bone('Tongue',(0,.268,-1.10),(0,.268,-1.22),'Head')
    if species=='coati':
        for side,suffix in [(-1,'R'),(1,'L')]: bone('Ear_'+suffix,(side*.087,.510,-.385),(side*.087,.565,-.385),'Head')
    for i in range(len(tail)-1): bone('Tail'+str(i+1),tail[i],tail[i+1],'Pelvis' if i==0 else 'Tail'+str(i))
    for side,front,h,k,f in limbs:
        names=('UpperArm','Forearm','Hand') if front else ('Thigh','Shin','Foot')
        a,b,c=[n+'_'+side for n in names]
        bone(a,h,k,'Chest' if front else 'Pelvis'); bone(b,k,f,a); bone(c,f,f+Vector((0,0,-.09)),b)
    bpy.ops.object.mode_set(mode='OBJECT')
    for pb in ob.pose.bones: pb.rotation_mode='QUATERNION'
    return ob

def body_weight(z,species):
    centers=[(-.72,'Snout'),(-.47,'Head'),(-.28,'Neck'),(-.05,'Chest'),(.16,'Spine1'),(.36,'Pelvis')] if species=='coati' else [(-1.02,'Head'),(-.70,'Neck'),(-.35,'Chest'),(.08,'Spine1'),(.42,'Pelvis')]
    if z<=centers[0][0]: return {centers[0][1]:1}
    if z>=centers[-1][0]: return {centers[-1][1]:1}
    for (a,an),(b,bn) in zip(centers,centers[1:]):
        if a<=z<=b:
            t=(z-a)/(b-a); t=t*t*(3-2*t)
            return {an:1-t,bn:t}

def surface_color(species):
    def color(c,q,i):
        # q=0 is dorsal in the longitudinal ring frame.
        belly=math.cos(q)<-.42
        if species=='monitor':
            if belly: return (.16,.145,.10,1)
            row=math.floor(c.z*24); cell=math.floor((q%TAU)*6+((row%2)*.5))
            seed=(row*19349663 ^ cell*73856093)&65535
            du=((q%TAU)*6+((row%2)*.5))%1-.5-((seed%31)/31-.5)*.24
            dv=(c.z*24)%1-.5-(((seed//31)%31)/31-.5)*.20
            spotted=(du*du/.12+dv*dv/.13)<(.62+.26*((seed//961)%17)/17)
            return (.19,.155,.077,1) if spotted and seed%5<2 else (.069,.061,.040,1)
        from coati_surface_v2 import coarse_color
        return coarse_color(c,q)
    return color

def interpolate_profiles(profile):
    # Catmull-Rom provides shaped cross sections with many deformation loops.
    result=[]
    for i in range(len(profile)-1):
        a=profile[max(0,i-1)]; b=profile[i]; c=profile[i+1]; d=profile[min(i+2,len(profile)-1)]
        for j in range(4):
            t=j/4
            vals=[]
            for k in range(4): vals.append(.5*((2*b[k])+(-a[k]+c[k])*t+(2*a[k]-5*b[k]+4*c[k]-d[k])*t*t+(-a[k]+3*b[k]-3*c[k]+d[k])*t*t*t))
            result.append(vals)
    result.append(profile[-1]); return result

def build_monitor_anatomical_trunk(skin, profile, coat):
    """Build varanid skull, cervical folds, broad ribcage with lateral flank folds, and pelvis."""
    from monitor_surface_solo import mouth_y, upper_jaw_halfwidth
    prof = interpolate_profiles(profile)
    radial = 48
    start = len(skin.v)
    n = len(prof)
    for i, (z, cy, rx, ry) in enumerate(prof):
        rx = max(rx, 0.003)
        ry = max(ry, 0.003)
        ws = body_weight(z, 'monitor')
        c_vec = Vector((0.0, cy, z))

        # Smooth regional masks
        w_head = max(0.0, min(1.0, (-0.80 - z) / 0.09))
        w_snout = max(0.0, min(1.0, (-0.86 - z) / 0.06))
        w_torso = max(0.0, min(1.0, (z + 0.48) / 0.14)) * max(0.0, min(1.0, (0.38 - z) / 0.14))

        # Gular / antehumeral fold constrictions on neck
        neck_pinch = 1.0 - 0.065 * math.exp(-((z + 0.79) / 0.032) ** 2) - 0.055 * math.exp(-((z + 0.57) / 0.035) ** 2)
        rx_eff = rx * neck_pinch
        ry_eff = ry * neck_pinch

        ym = mouth_y(z)
        jaw_hw = upper_jaw_halfwidth(z)

        for j in range(radial + 1):
            q = TAU * j / radial
            sq = math.sin(q)
            cq = math.cos(q)

            # Standard elliptical base point (note: -sq is +X in the ringmesh frame)
            lat = -sq
            px = lat * rx_eff
            py = cy + cq * ry_eff

            # 1. Varanid cranial shaping (flat skull table, canthus rostralis, brow shelf, flat maxillary mouth floor)
            if w_head > 0.0:
                # Match upper jaw halfwidth contour
                h_rx = (1.0 - w_snout) * rx_eff + w_snout * jaw_hw
                # Trapezoidal skull cross-section: wider at jugal/temporal cheeks (cq ~ -0.1..0.3), narrower at dorsal table
                trap = 0.88 + 0.14 * (1.0 - max(0.0, cq) ** 1.35)
                # Overhanging supraorbital brow shelf & canthus rostralis flare
                brow_env = math.exp(-((z + 0.995) / 0.072) ** 2) * math.exp(-((cq - 0.58) / 0.24) ** 2)
                canthus_env = math.exp(-((z + 1.12) / 0.065) ** 2) * math.exp(-((cq - 0.52) / 0.24) ** 2)
                px_head = lat * (h_rx * trap + 0.011 * brow_env + 0.005 * canthus_env)

                # Flattened dorsal skull table
                if cq > 0.42:
                    flat_top = 1.0 - 0.20 * ((cq - 0.42) / 0.58) ** 1.4
                    py_head = cy + cq * ry_eff * flat_top + 0.003 * brow_env
                else:
                    py_head = cy + cq * ry_eff

                # Flat horizontal upper jaw labial floor for z < -0.85 so upper skull meets sculpted mandible cleanly
                if w_snout > 0.0 and cq < 0.05:
                    floor_y = ym + 0.0015 * max(0.0, 1.0 + cq)
                    clamp_t = min(1.0, max(0.0, (0.05 - cq) / 0.35)) * w_snout
                    py_head = py_head * (1.0 - clamp_t) + max(floor_y, py_head * 0.3 + floor_y * 0.7) * clamp_t

                px = (1.0 - w_head) * px + w_head * px_head
                py = (1.0 - w_head) * py + w_head * py_head

            # 2. Torso shaping: dorsoventral basking compression, lateral dermal flank fold, flat ventral plastron
            if w_torso > 0.0:
                flank_fold = math.exp(-((cq + 0.14) / 0.24) ** 2) * (abs(lat) ** 1.5)
                px += lat * (0.009 * flank_fold * w_torso)
                py -= 0.005 * flank_fold * w_torso
                if cq < -0.45:
                    belly_flat = ((abs(cq) - 0.45) / 0.55) ** 1.5
                    py += 0.012 * belly_flat * w_torso

            skin.v.append((px, py, z))
            skin.uv.append((j / radial, i / max(1, n - 1)))
            skin.w.append(ws)
            skin.col.append(coat(c_vec, q, i))

    for i in range(n - 1):
        for j in range(radial):
            k = start + i * (radial + 1) + j
            skin.f.append((k, k + radial + 1, k + radial + 2, k + 1))
    for i, reverse in [(0, True), (n - 1, False)]:
        k = len(skin.v)
        z, cy, _, _ = prof[i]
        c_pt = (0.0, cy, z)
        skin.v.append(c_pt)
        skin.uv.append((0.5, i / max(1, n - 1)))
        skin.w.append(body_weight(z, 'monitor'))
        skin.col.append(coat(Vector(c_pt), 0, i))
        r = start + i * (radial + 1)
        for j in range(radial):
            skin.f.append((r + j + 1, r + j, k) if reverse else (r + j, r + j + 1, k))

def unify_anatomy(obj, species):
    """Union body, limb masses and paws, then recover regional skin ownership.

    The resulting skin has continuous shoulder/hip junctions and elbow surfaces.
    UVs are repacked after retopology rather than relying on destroyed ring UVs.
    """
    from mathutils.kdtree import KDTree
    kd=KDTree(len(obj.data.vertices)); samples=[]
    colors=obj.data.color_attributes['Color']
    groupnames={g.index:g.name for g in obj.vertex_groups}
    for v in obj.data.vertices:
        kd.insert(v.co,v.index)
        samples.append(({groupnames[g.group]:g.weight for g in v.groups},tuple(colors.data[v.index].color)))
    kd.balance(); active(obj)
    remesh=obj.modifiers.new('ContinuousAnatomyUnion','REMESH'); remesh.mode='VOXEL'; remesh.voxel_size=.006 if species=='coati' else .0075
    remesh.use_smooth_shade=True; bpy.ops.object.modifier_apply(modifier=remesh.name)
    smooth=obj.modifiers.new('OrganicSurfaceRelax','SMOOTH'); smooth.factor=.65 if species=='coati' else .52; smooth.iterations=5 if species=='coati' else 4
    bpy.ops.object.modifier_apply(modifier=smooth.name)
    dec=obj.modifiers.new('GameRetopology','DECIMATE'); dec.ratio=.46 if species=='coati' else .54
    bpy.ops.object.modifier_apply(modifier=dec.name)
    if species=='monitor':
        post_smooth=obj.modifiers.new('PostDecimateRelax','SMOOTH'); post_smooth.factor=.35; post_smooth.iterations=2
        bpy.ops.object.modifier_apply(modifier=post_smooth.name)
    if species=='coati':
        # Actual skin recession around each orbit, before weight/UV recovery.
        # A smaller globe alone still sat outside the smooth skull in Godot.
        for v in obj.data.vertices:
            p=v.co
            if abs(p.x)>.055 and -.56<p.z<-.44 and .425<p.y<.510:
                falloff=math.exp(-((p.z+.499)/.028)**2-((p.y-.470)/.023)**2)
                p.x-=math.copysign(.014*falloff,p.x)
        obj.data.update()
        # Carve a real shallow orbital cavity. A displaced closed skull surface
        # could still occlude the cornea and turn a lid patch into a pale disc.
        for side in [-1,1]:
            bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=16,location=(side*.083,.470,-.499))
            cutter=bpy.context.object; cutter.name='TemporaryOrbitCutter'; cutter.scale=(.020,.012,.018)
            active(cutter); bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
            active(obj); mod=obj.modifiers.new('InsetOrbit','BOOLEAN'); mod.operation='DIFFERENCE'; mod.solver='EXACT'; mod.object=cutter
            bpy.ops.object.modifier_apply(modifier=mod.name); bpy.data.objects.remove(cutter,do_unlink=True)
    if species=='monitor':
        for side in [-1,1]:
            bpy.ops.mesh.primitive_uv_sphere_add(segments=24,ring_count=16,location=(side*.094,.326,-1.006))
            cutter=bpy.context.object; cutter.scale=(.018,.014,.022)
            active(cutter); bpy.ops.object.transform_apply(location=False,rotation=False,scale=True)
            active(obj); mod=obj.modifiers.new('MonitorInsetOrbit','BOOLEAN'); mod.operation='DIFFERENCE'; mod.solver='EXACT'; mod.object=cutter
            bpy.ops.object.modifier_apply(modifier=mod.name); bpy.data.objects.remove(cutter,do_unlink=True)
    obj.vertex_groups.clear()
    for name in obj.parent.data.bones.keys(): obj.vertex_groups.new(name=name)
    for attr in list(obj.data.color_attributes): obj.data.color_attributes.remove(attr)
    color=obj.data.color_attributes.new(name='Color',type='FLOAT_COLOR',domain='POINT')
    for v in obj.data.vertices:
        nearest=kd.find_n(v.co,4); weights={}; col=[0,0,0,0]; total=0
        for co,idx,dist in nearest:
            w=1/max(.0002,dist)**2; total+=w
            ws,c=samples[idx]
            for n,value in ws.items(): weights[n]=weights.get(n,0)+value*w
            for j in range(4): col[j]+=c[j]*w
        # Keep at most four influences: standard mobile/Godot skinning contract.
        ws=sorted(weights.items(),key=lambda p:-p[1])[:4]; norm=sum(w for n,w in ws)
        for n,w in ws: obj.vertex_groups[n].add([v.index],w/norm,'REPLACE')
        color.data[v.index].color=[c/total for c in col]
        if species=='coati' and abs(v.co.x)>.055:
            orbit=((v.co.z+.499)/.023)**2+((v.co.y-.470)/.016)**2
            if orbit<1.5:
                blend=min(1,max(0,(1.5-orbit)/.65))
                old=color.data[v.index].color
                color.data[v.index].color=tuple(old[k]*(1-blend)+(.018,.010,.005)[k]*blend for k in range(3))+(1,)
    for p in obj.data.polygons: p.use_smooth=True
    if not obj.data.uv_layers:
        obj.data.uv_layers.new(name='SurfaceUV')
    else:
        obj.data.uv_layers.active.name='SurfaceUV'
    active(obj); bpy.ops.object.mode_set(mode='EDIT'); bpy.ops.mesh.select_all(action='SELECT')
    bpy.ops.uv.smart_project(angle_limit=1.15,island_margin=.008)
    bpy.ops.object.mode_set(mode='OBJECT')

def geometry(species,rig,mat,profile,limbs,tail):
    skin=Builder(species+'_body',rig,mat); coat=surface_color(species)
    if species=='monitor':
        build_monitor_anatomical_trunk(skin, profile, coat)
    else:
        nodes=[((0,y,z),max(rx,.003),max(ry,.003),body_weight(z,species)) for z,y,rx,ry in interpolate_profiles(profile)]
        skin.ringmesh(nodes,48,coat)
    # Chain tail uses elongated transverse sections; monitor tail has a heavy
    # caudofemoralis base tapering into a laterally compressed swimming blade.
    tn=[]
    for i in range(len(tail)-1):
        for j in range(8):
            t=j/8; u=(i+t)/(len(tail)-1)
            if species in ['coati','monitor']:
                p0=Vector(tail[max(0,i-1)]); p1=Vector(tail[i]); p2=Vector(tail[i+1]); p3=Vector(tail[min(len(tail)-1,i+2)])
                c=(2*p1+(-p0+p2)*t+(2*p0-5*p1+4*p2-p3)*t*t+(-p0+3*p1-3*p2+p3)*t*t*t)*.5
                weights=[(1-t)**3/6,(3*t**3-6*t*t+4)/6,(-3*t**3+3*t*t+3*t+1)/6,t**3/6]
                ws={}
                for k,w in zip([i-1,i,i+1,i+2],weights):
                    name='Tail'+str(max(1,min(len(tail)-1,k+1))); ws[name]=ws.get(name,0)+w
            else:
                c=Vector(tail[i]).lerp(Vector(tail[i+1]),t);ws={'Tail'+str(i+1):1}
                if i>0 and t<.4: ws={'Tail'+str(i):.5*(1-t/.4),'Tail'+str(i+1):.5+.5*t/.4}
            if species=='monitor':
                r=.114*(1-u)**1.05+.004
                sacral_blend=min(1.0,u/0.16)
                sacral_blend=sacral_blend*sacral_blend*(3.0-2.0*sacral_blend)
                caudofemoral=max(0.0,1.0-u/0.26)
                rx_tail=r*(0.72+0.30*caudofemoral)
                ry_tail=r*(0.88*(1.0-sacral_blend)+1.26*sacral_blend)
                tn.append((tuple(c),rx_tail,ry_tail,ws))
            else:
                r=.052*(1-u)**1.1+.004
                tn.append((tuple(c),r,r,ws))
    tn.append((tail[-1],.001,.001,{'Tail'+str(len(tail)-1):1}))
    def tailcol(c,q,i):
        if species=='coati':
            return (.065,.026,.009,1) if (i//7)%2 else (.24,.135,.055,1)
        band=(.5+.5*math.sin(i*1.12))**3
        return tuple(a+(b-a)*band for a,b in zip((.067,.059,.040),(.17,.143,.082)))+(1,)
    skin.ringmesh(tn,32,tailcol)
    for side,front,h,k,f in limbs:
        names=('UpperArm','Forearm','Hand') if front else ('Thigh','Shin','Foot')
        a,b,c=[n+'_'+side for n in names]
        # Strong upper masses, muscular forearm/calf bellies, tapering wrists/ankles.
        radii=((.052,.032,.022) if front else (.072,.036,.025)) if species=='coati' else ((.076,.046,.028) if front else (.094,.055,.032))
        ln=[]
        for seg,(p0,p1,r0,r1) in enumerate([(h,k,radii[0],radii[1]),(k,f,radii[1],radii[2])]):
            for j in range(9):
                if seg==1 and j==0: continue
                t=j/8; p=p0.lerp(p1,t); r=r0*(1-t)+r1*t
                if seg==0:
                    r*=1+(.20 if species=='coati' else .25)*math.sin(math.pi*t)
                elif species=='monitor':
                    r*=1+.16*math.sin(math.pi*(t**0.75))
                if seg==0: ws={a:1-max(0,(t-.65)/.35)*.5,b:max(0,(t-.65)/.35)*.5}
                else: ws={a:max(0,(.35-t)/.35)*.5,b:1-max(0,(.35-t)/.35)*.5}
                ln.append((tuple(p),r,r*(.88 if species=='coati' else .92),ws))
        skin.ringmesh(ln,24,(.29,.14,.06,1) if species=='coati' else (.072,.064,.043,1))
        limbcolor=(.29,.14,.06,1) if species=='coati' else (.072,.064,.043,1)
        joint_r=.040 if species=='coati' else (.044 if front else .050)
        skin.ellipsoid(tuple(k),(joint_r,joint_r*.92,joint_r),{a:.5,b:.5},limbcolor)
        shoulder_r=(.069 if front else .088) if species=='coati' else (.082 if front else .096)
        skin.ellipsoid(tuple(h),(shoulder_r*.88,shoulder_r,shoulder_r*1.18),{a:.75,('Chest' if front else 'Pelvis'):.25},limbcolor)
        if species=='coati':
            skin.ellipsoid(tuple(f+Vector((0,-.027,-.025))),(.039,.028,.065),{c:1},(.105,.07,.035,1))
            s=1 if side=='L' else -1
            for digit in range(5):
                spread=(digit-2)*.014
                length=.05*(1-.14*abs(digit-2))
                p=f+Vector((spread,.0,-.065)); end=p+Vector((spread*.40,-.008,-length))
                skin.ringmesh([(tuple(p),.011,.012,{c:1}),(tuple(p.lerp(end,.5)),.010,.010,{c:1}),(tuple(end),.006,.006,{c:1})],12,(.16,.10,.052,1))
                claws=Builder(species+'_claw_'+c+str(digit),rig,MATS['claw'])
                claws.ringmesh([(tuple(end),.006,.006,{c:1}),(tuple(end+Vector((0,-.006,-.02))),.004,.004,{c:1}),(tuple(end+Vector((0,-.040,-.028))),.0005,.0005,{c:1})],10)
                claws.build()
        else:
            # Broad plantigrade metapodial palm/sole pad; the 5 distinct articulated digits
            # are built after unify_anatomy so voxel remeshing never melts toes together.
            skin.ellipsoid(tuple(f+Vector((0,-.022,-.018))),(.050,.020,.054),{c:1},(.075,.068,.047,1))
    if species=='coati':
        for side in [-1,1]:
            skin.ellipsoid((side*.037,.382,-.665),(.017,.011,.055),{'Head':.35,'Snout':.65},(.39,.34,.26,1))
    body=skin.build(); unify_anatomy(body,species)
    if species=='monitor':
        from monitor_pigment_solo import apply_3d_dermal_relief, bake_pigment
        from monitor_surface_solo import build_digits_and_claws, face
        from monitor_animation_v3 import lids
        from monitor_scutes_solo import scutes

        apply_3d_dermal_relief(body, amplitude=0.00015)
        # Scale body's clean smart_project UVs into [0.002, 0.998] x [0.002, 0.745] so the trunk/head/limbs get 75% of the 2048x2048 map
        for loop_uv in body.data.uv_layers.active.data:
            loop_uv.uv = (0.002 + loop_uv.uv[0] * 0.996, 0.002 + loop_uv.uv[1] * 0.743)

        digits_ob = build_digits_and_claws(Builder, rig, mat, MATS, limbs)
        skin_face_parts = face(Builder, rig, body, mat, MATS)
        blink_lids = lids(Builder, rig, mat)
        osteoderms_ob = scutes(Builder, rig, body, mat, tail)

        # Unwrap only the small orbital/naris/aural rings into [0.650, 0.716] x [0.760, 0.990],
        # preserving the huge un-fragmented parametric UV grids of digits_ob and skin_face_parts[0] (mandible).
        small_rings = [p for p in [*skin_face_parts[1:], *blink_lids] if p is not None]
        if small_rings:
            active(small_rings[0])
            for part in small_rings[1:]:
                part.select_set(True)
            if len(small_rings) > 1:
                bpy.ops.object.join()
            rings_ob = small_rings[0]
            active(rings_ob)
            bpy.ops.object.mode_set(mode='EDIT')
            bpy.ops.mesh.select_all(action='SELECT')
            bpy.ops.uv.smart_project(angle_limit=1.15, island_margin=0.004)
            bpy.ops.object.mode_set(mode='OBJECT')
            for loop_uv in rings_ob.data.uv_layers.active.data:
                loop_uv.uv = (0.650 + loop_uv.uv[0] * 0.066, 0.760 + loop_uv.uv[1] * 0.230)
        else:
            rings_ob = None

        # Join digits, sculpted mandible, small facial rings, and grid-mapped osteoderms into body
        active(body)
        digits_ob.select_set(True)
        if skin_face_parts and skin_face_parts[0] is not None:
            skin_face_parts[0].select_set(True)
        if rings_ob is not None:
            rings_ob.select_set(True)
        if osteoderms_ob is not None:
            osteoderms_ob.select_set(True)
        bpy.ops.object.join()

        bake_pigment(body, mat, ROOT / 'game/assets/guardians/great_ground', profile)
    # Face includes actual eyeballs, eyelid/socket shapes, nostrils and lips.
    eye_z=-.493 if species=='coati' else -1.00
    eye_y=.47 if species=='coati' else .326
    eye_x=.082 if species=='coati' else .087
    eye_r=.011 if species=='coati' else .013
    for s in [-1,1]:
        eye=Builder(species+'_eye_'+str(s),rig,MATS['eye'])
        eye.ellipsoid((s*(eye_x+(.001 if species=='coati' else -.001)),eye_y,eye_z-.006),(eye_r*.65,eye_r,eye_r),{'Head':1},(.035,.025,.014,1) if species=='coati' else (.014,.010,.006,1))
        eye.build()
        if species=='monitor':
            from monitor_animation_v3 import curved_eye
            curved_eye(Builder,rig,MATS,s)
        else:
            iris=Builder(species+'_iris_'+str(s),rig,MATS['iris'])
            iris.ellipsoid((s*(eye_x+.0072),eye_y,eye_z-.009),(.0008,eye_r*.64,eye_r*.65),{'Head':1})
            iris.build()
            pupil=Builder(species+'_pupil_'+str(s),rig,MATS['eye'])
            pupil.ellipsoid((s*(eye_x+.0078),eye_y,eye_z-.009),(.0006,eye_r*.40,eye_r*.39),{'Head':1})
            pupil.build()
            nostril=Builder(species+'_nostril_'+str(s),rig,MATS['eye'])
            nz=-.825; ny=.376; nx=.026
            nostril.ellipsoid((s*nx,ny,nz),(.002,.006,.009),{'Snout':1})
            nostril.build()
            lip=Builder(species+'_mouth_'+str(s),rig,MATS['mouth'])
            lp=[]
            for j in range(21):
                z=-.788+j*(.288/20)
                nearby=[v.co.y for v in body.data.vertices if abs(v.co.z-z)<.008 and abs(v.co.x)<.1]
                y=(min(nearby)+.012) if nearby else .355
                hit,loc,norm,index=body.ray_cast(Vector((s*.25,y,z)),Vector((-s,0,0)),distance=1)
                lp.append((loc.x+s*.0004,y,z) if hit else (s*.021,y,z))
            lipradius=.0013
            lip.ringmesh([(p,lipradius,lipradius,{'Jaw':1}) for p in lp],8); lip.build()
    if species=='coati':
        jaw=Builder(species+'_mandible',rig,mat)
        jaw.ellipsoid((0,.366,-.645),(.035,.014,.140),{'Jaw':1},(.34,.28,.20,1))
        jaw.build()
        from coati_surface_v2 import cup_ears,lids_and_muzzle,surface_fur
        ears=cup_ears(Builder,rig,mat); lids_and_muzzle(Builder,rig,mat,MATS,body)
        pads=[o for o in bpy.data.objects if o.type=='MESH' and o.name.startswith('coati_muzzle_pad_')]
        surface_fur(Builder,rig,body,ears+pads,TASK)
    return body

MATS={}

def consolidate_material_meshes(species):
    groups={}
    for ob in list(bpy.data.objects):
        if ob.type=='MESH': groups.setdefault(ob.data.materials[0].name,[]).append(ob)
    for name,objects in groups.items():
        active(objects[0])
        for ob in objects: ob.select_set(True)
        if len(objects)>1: bpy.ops.object.join()
        bpy.context.object.name=species+'_'+name

def normalize_all_skin_weights():
    for ob in bpy.data.objects:
        if ob.type!='MESH' or not ob.vertex_groups: continue
        for v in ob.data.vertices:
            pairs=[(g.group,g.weight) for g in v.groups if g.weight>1e-6]
            if not pairs:
                ob.vertex_groups[0].add([v.index],1.0,'REPLACE')
                continue
            pairs.sort(key=lambda p:-p[1])
            keep=pairs[:4]
            keep_ids={g_id for g_id,_ in keep}
            for g in list(v.groups):
                if g.group not in keep_ids:
                    ob.vertex_groups[g.group].remove([v.index])
            total=sum(w for _,w in keep) or 1.0
            for g_id,w in keep:
                ob.vertex_groups[g_id].add([v.index],w/total,'REPLACE')

def set_bone_segment(rig,name,h,t):
    pb=rig.pose.bones[name]; rest=rig.data.bones[name]
    rd=rest.tail_local-rest.head_local; nd=Vector(t)-Vector(h)
    rot=rd.normalized().rotation_difference(nd.normalized()).to_matrix().to_4x4()
    mat=rot @ rest.matrix_local; mat.translation=Vector(h)
    pb.matrix=mat
    bpy.context.view_layer.update()

def ik_limb(rig,side,front,h,k,f,target):
    names=('UpperArm','Forearm','Hand') if front else ('Thigh','Shin','Foot')
    a,b,c=[n+'_'+side for n in names]
    # Shoulder follows its animated parent transform; foot follows contact track.
    parent=rig.pose.bones['Chest' if front else 'Pelvis']
    parent_rest=rig.data.bones[parent.name].matrix_local
    h=(parent.matrix @ parent_rest.inverted()) @ h
    A=(k-Vector(rig.data.bones[a].head_local)).length; B=(f-k).length
    target=Vector(target); d=target-h; dist=min(d.length,A+B-.00001); axis=d.normalized()
    midpoint=(A*A-B*B+dist*dist)/(2*dist)
    bend=k-h; bend-=axis*bend.dot(axis)
    if bend.length<1e-6: bend=Vector((1 if side=='L' else -1,0,0))
    knee=h+axis*midpoint+bend.normalized()*math.sqrt(max(0,A*A-midpoint*midpoint))
    reached=h+axis*dist
    set_bone_segment(rig,a,h,knee); set_bone_segment(rig,b,knee,reached)
    # Horizontal feet retain the authored orientation (Y-up).
    pb=rig.pose.bones[c]; mat=rig.data.bones[c].matrix_local.copy(); mat.translation=reached; pb.matrix=mat
    bpy.context.view_layer.update()
    return (reached-target).length

def reset(rig):
    for b in rig.pose.bones:
        b.location=(0,0,0); b.rotation_quaternion=(1,0,0,0); b.scale=(1,1,1)
    bpy.context.view_layer.update()

def animations(species,rig,limbs,tail):
    from mathutils import Quaternion
    # Bone solving only needs armature evaluation. Avoid deforming every mesh
    # during each intermediate solve on the shared authoring machine.
    modifiers=[m for o in bpy.data.objects if o.type=='MESH' for m in o.modifiers if m.type=='ARMATURE']
    for mod in modifiers: mod.show_viewport=False
    def world_rotation(name,axis,angle):
        return Quaternion(rig.data.bones[name].matrix_local.to_3x3().inverted()@Vector(axis),angle)
    rig.animation_data_create(); report={}; acts={}
    for name,length in [('idle',60),('walk',24 if species=='coati' else 32),('feed',72),('rest',60)]:
        act=bpy.data.actions.new(name); rig.animation_data.action=act; acts[name]=act
        errors=[]; contacts=[]
        for frame in range(1,length+2):
            reset(rig); t=(frame-1)/length; phase=t*TAU
            if name=='idle':
                rig.pose.bones['Chest'].scale=(1+.012*math.sin(phase),1+.007*math.sin(phase),1)
                rig.pose.bones['Head'].rotation_quaternion=Quaternion(Vector((0,0,1)),.06*math.sin(phase))
            if species=='coati':
                from coati_animation_v3 import closure,pose
                blink=closure(name,t)
                pose(rig,blink)
                if name=='idle':
                    rig.pose.bones['Neck'].rotation_quaternion=Quaternion(Vector((1,0,0)),.035*math.sin(phase))
                    rig.pose.bones['Head'].rotation_quaternion=Quaternion(Vector((0,0,1)),.075*math.sin(phase))*Quaternion(Vector((1,0,0)),.025*math.sin(phase*2))
                    rig.pose.bones['Snout'].rotation_quaternion=Quaternion(Vector((0,1,0)),.045*math.sin(phase*2))
                if name=='rest':
                    rig.pose.bones['Head'].rotation_quaternion=Quaternion(Vector((0,0,1)),.02*math.sin(phase))
                for suffix,offset in [('L',.17),('R',.61)]:
                    twitch=math.exp(-(((t-offset+.5)%1-.5)/.055)**2) if name=='idle' else 0
                    rig.pose.bones['Ear_'+suffix].rotation_quaternion=Quaternion(Vector((1,0,0)),.11*twitch)

            if species=='monitor':
                from monitor_animation_v3 import closure as monitor_closure,pose as monitor_lid_pose
                monitor_lid_pose(rig,monitor_closure(name,t))
                if name=='idle':
                    look=.04*math.tanh(3*math.sin(phase))
                    rig.pose.bones['Head'].rotation_quaternion=world_rotation('Head',(0,1,0),look)*world_rotation('Head',(1,0,0),.009*math.sin(phase))
                    rig.pose.bones['Neck'].rotation_quaternion=world_rotation('Neck',(0,1,0),-.012*math.sin(phase))
                flick=(max(0,min(1,(t-.265)/.018))*max(0,min(1,(.365-t)/.082))) if name=='idle' else 0
                rig.pose.bones['Tongue'].location=(0,0.145*flick,0)
                rig.pose.bones['Tongue'].scale=(1,1.0+0.35*flick,1)
                if flick>0:rig.pose.bones['Jaw'].rotation_quaternion=world_rotation('Jaw',(1,0,0),-.025*flick)
            if name=='feed':
                dip=(1-math.cos(phase))*.5
                rig.pose.bones['Neck'].rotation_quaternion=world_rotation('Neck',(1,0,0),-.22*dip)
                rig.pose.bones['Head'].rotation_quaternion=world_rotation('Head',(1,0,0),-.16*dip)
                rig.pose.bones['Jaw'].rotation_quaternion=world_rotation('Jaw',(1,0,0),-.13*dip*(.5+.5*math.sin(phase*4)))
            if name=='rest':
                # A sustained crouched/resting breath cycle; feet solved against
                # unchanged contact positions rather than translated into earth.
                delta=Vector((0,-(.09 if species=='coati' else .07),0))
                pb=rig.pose.bones['Pelvis']; pb.location=rig.data.bones['Pelvis'].matrix_local.to_3x3().inverted()@delta
                rig.pose.bones['Chest'].scale=(1+.009*math.sin(phase),1,1)
            if name=='walk' and species=='monitor':
                rig.pose.bones['Spine1'].rotation_quaternion=world_rotation('Spine1',(0,1,0),.030*math.sin(phase))
                rig.pose.bones['Chest'].rotation_quaternion=world_rotation('Chest',(0,1,0),-.025*math.sin(phase))
            for i in range(1,len(tail)):
                amp=.018 if species=='coati' else .025
                rig.pose.bones['Tail'+str(i)].rotation_quaternion=world_rotation('Tail'+str(i),(0,1,0),.010*math.sin(phase-i*.21))*world_rotation('Tail'+str(i),(1,0,0),.002*math.sin(phase-i*.29))
            bpy.context.view_layer.update()
            if name=='rest' and species=='monitor':
                # A resting torso must not drag the tail tip below the floor.
                for i in range(1,len(tail)):
                    h=Vector(tail[i-1]); e=Vector(tail[i])
                    h.y-=.07*(1-(i-1)/(len(tail)-1)); e.y-=.07*(1-i/(len(tail)-1))
                    set_bone_segment(rig,'Tail'+str(i),h,e)
            for side,front,h,k,f in limbs:
                target=f.copy(); contact=True
                if name=='walk':
                    # Four-beat walking pattern, 65% stance. In-place authored
                    # stride moves stance feet backward at constant speed while
                    # runtime movement carries the animal forward.
                    offset={('L',True):0,('R',False):.25,('R',True):.5,('L',False):.75}[(side,front)]
                    p=(t+offset)%1; step=.32 if species=='coati' else .40
                    if p<.65: target.z+=-step*.5+step*p/.65
                    else:
                        swing=(p-.65)/.35; ease=swing*swing*(3-2*swing)
                        target.z+=step*.5-step*ease; target.y+=(.055 if species=='coati' else .065)*math.sin(math.pi*swing)
                        contact=False
                err=ik_limb(rig,side,front,h,k,f,target); errors.append(err)
                if contact: contacts.append(float(rig.pose.bones[('Hand' if front else 'Foot')+'_'+side].matrix.translation.y-f.y))
            for pb in rig.pose.bones:
                pb.keyframe_insert('location',frame=frame); pb.keyframe_insert('rotation_quaternion',frame=frame); pb.keyframe_insert('scale',frame=frame)
        report[name]={'frames':[1,length+1],'duration_seconds':length/24,'max_IK_target_error_m':max(errors),'max_stance_height_error_m':max(abs(c) for c in contacts),'stride_m':(.32 if species=='coati' else .40) if name=='walk' else 0}
        for layer in act.layers:
            for stripdata in layer.strips:
                for bag in stripdata.channelbags:
                    for curve in bag.fcurves:
                        for key in curve.keyframe_points: key.interpolation='LINEAR'
        track=rig.animation_data.nla_tracks.new(); track.name=name; strip=track.strips.new(name,1,act); track.mute=True
    rig.animation_data.action=None; reset(rig); bpy.context.scene.frame_set(1)
    for mod in modifiers: mod.show_viewport=True
    bpy.context.view_layer.update()
    return acts,report

def glb_audit(path):
    data=path.read_bytes(); length,kind=struct.unpack_from('<II',data,12); doc=json.loads(data[20:20+length])
    access=doc['accessors']; verts=tris=0
    for mesh in doc.get('meshes',[]):
        for p in mesh['primitives']:
            verts+=access[p['attributes']['POSITION']]['count']; tris+=access[p['indices']]['count']//3
    return {'bytes':len(data),'vertices_exported':verts,'triangles':tris,'mesh_count':len(doc.get('meshes',[])),'skin_count':len(doc.get('skins',[])),'joints':max([len(s['joints']) for s in doc.get('skins',[])],default=0),'animations':[{'name':a.get('name'),'channels':len(a['channels'])} for a in doc.get('animations',[])],'materials':len(doc.get('materials',[])),'embedded_images':len(doc.get('images',[])),'all_meshes_have_uv':all('TEXCOORD_0' in p['attributes'] for m in doc['meshes'] for p in m['primitives']),'normal_texture_materials':sum('normalTexture' in m for m in doc.get('materials',[]))}

def render_evidence(species,rig,acts,folder):
    scene=bpy.context.scene; scene.render.engine='BLENDER_EEVEE'; scene.eevee.taa_render_samples=16
    scene.render.resolution_x=1000; scene.render.resolution_y=800; scene.render.resolution_percentage=100
    scene.world=bpy.data.worlds.new('StudioWorld'); scene.world.use_nodes=True; scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.18,.18,.18,1)
    scene.world.node_tree.nodes['Background'].inputs[1].default_value=.7
    # Rendering works directly in authored Y-up world, with a Y-normal floor.
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,-.005,0),rotation=(math.pi/2,0,0)); floor=bpy.context.object
    floor.name='EvidenceFloor'; floor.data.materials.append(material('StudioFloor',(.23,.245,.26),.8))
    def aim(ob,p):
        forward=(Vector(p)-ob.location).normalized()
        right=forward.cross(Vector((0,1,0))).normalized(); up=right.cross(forward).normalized()
        ob.rotation_euler=Matrix((right,up,-forward)).transposed().to_euler()
    for name,p,power,size in [('Key',(2,3,-3),550,4),('Fill',(-3,1,-2),350,3),('Rim',(0,3,3),650,3)]:
        ld=bpy.data.lights.new(name,'AREA'); ld.energy=power; ld.shape='DISK'; ld.size=size
        lo=bpy.data.objects.new(name,ld); bpy.context.collection.objects.link(lo); lo.location=p; aim(lo,(0,.4,0))
    camd=bpy.data.cameras.new('EvidenceCamera'); cam=bpy.data.objects.new('EvidenceCamera',camd); bpy.context.collection.objects.link(cam); scene.camera=cam
    camd.type='ORTHO'; camd.ortho_scale=2.10 if species=='coati' else 4.35
    center=Vector((0,.63,-.10)) if species=='coati' else Vector((0,.25,.67))
    views={'side':(4,.7,0),'front':(0,.7,-5),'three_quarter':(3,1.6,-4)}
    clay=material('NeutralClay',(.48,.48,.48),.77)
    original={o.name:list(o.data.materials) for o in bpy.data.objects if o.type=='MESH' and o!=floor}
    rig.animation_data.action=None; reset(rig)
    for mode in ['clay','surface']:
        for o in bpy.data.objects:
            if o.type=='MESH' and o!=floor:
                o.data.materials.clear()
                for m in ([clay] if mode=='clay' else original[o.name]): o.data.materials.append(m)
        for view,offset in views.items():
            cam.location=center+Vector(offset); aim(cam,center)
            scene.render.filepath=str(folder/(mode+'_'+view+'.png')); bpy.ops.render.render(write_still=True)
    cam.location=center+Vector(views['three_quarter']); aim(cam,center)
    for name in ['walk','feed','rest']:
        rig.animation_data.action=acts[name]
        for frame in ([1,7,13,19] if name=='walk' else [19,37]):
            scene.frame_set(frame); scene.render.filepath=str(folder/(name+'_'+str(frame)+'.png')); bpy.ops.render.render(write_still=True)
    rig.animation_data.action=None; reset(rig)

def lift_monitor_anatomy(rig):
    def lift(z):
        t=max(0,min(1,(-.49-z)/.35));return .045*t*t*(3-2*t)
    for ob in bpy.data.objects:
        if ob.type=='MESH':
            for v in ob.data.vertices:v.co.y+=lift(v.co.z)
    active(rig);bpy.ops.object.mode_set(mode='EDIT')
    for bone in rig.data.edit_bones:
        bone.head.y+=lift(bone.head.z);bone.tail.y+=lift(bone.tail.z)
    bpy.ops.object.mode_set(mode='OBJECT');bpy.context.view_layer.update()

def clean_monitor_triangles():
    import bmesh
    for ob in bpy.data.objects:
        if ob.type!='MESH':continue
        bm=bmesh.new();bm.from_mesh(ob.data)
        bmesh.ops.triangulate(bm,faces=list(bm.faces))
        tiny=[f for f in bm.faces if f.calc_area()<5.5e-10]
        if tiny:bmesh.ops.delete(bm,geom=tiny,context='FACES')
        bm.to_mesh(ob.data);bm.free();ob.data.update()

def generate(species):
    print('GROUND_ART_START '+species,flush=True)
    bpy.ops.wm.read_factory_settings(use_empty=True); TASK.mkdir(parents=True,exist_ok=True)
    bpy.context.preferences.filepaths.save_version=0
    out=ROOT/'game/assets/guardians'/('woodland_omnivore' if species=='coati' else 'great_ground'); out.mkdir(parents=True,exist_ok=True)
    folder=TASK/species; folder.mkdir(exist_ok=True)
    if species=='coati':
        from coati_surface_v2 import coati_images,nose_material
        color,normal,noseimage=coati_images(out)
    else:
        from monitor_surface_solo import images as monitor_images
        color,normal,rough=monitor_images(out)
    print('GROUND_ART_TEXTURES '+species,flush=True)
    mat=material(species+'_PBR',(.3,.2,.1),.76 if species=='coati' else .54,color,normal)
    if species=='monitor':
        from monitor_surface_solo import roughness
        roughness(mat,rough)
    MATS.update({'eye':material('WetCornea',(0.008,.006,.004),.12),'iris':material('AmberIris',(.22,.11,.025),.22),'claw':material('Keratin',(.12,.105,.08),.4),'mouth':material('LipCrease',(.032,.023,.016),.68),'ear':material('EarInner',(.36,.245,.165),.80)})
    if species=='coati':
        MATS['nose']=nose_material(noseimage)
        MATS['iris'].node_tree.nodes.get('Principled BSDF').inputs['Base Color'].default_value=(.07,.033,.006,1)
    if species=='monitor': MATS['tongue']=material('MonitorTongue',(.055,.015,.032),.36)
    profile,chain,limbs,tail=config(species); rig=rigging(species,chain,limbs,tail)
    body=geometry(species,rig,mat,profile,limbs,tail)
    if species=='coati':
        from coati_animation_v3 import lids
        lids(Builder,rig,mat)
    if species=='monitor':
        # Retracted-in-mouth rest tongue so rest/bind pose has a clean closed snout,
        # darting forward out of the parted jaws during the idle tongue-flick.
        tongue=Builder('monitor_forked_tongue',rig,MATS['tongue'])
        tongue.ringmesh([((0,.268,-1.095),.0035,.0015,{'Tongue':1}),((0,.268,-1.155),.003,.0013,{'Tongue':1}),((0,.267,-1.200),.0024,.001,{'Tongue':1})],10)
        for side in [-1,1]: tongue.ringmesh([((side*.0014,.267,-1.196),.0016,.0008,{'Tongue':1}),((side*.011,.265,-1.232),.0004,.0004,{'Tongue':1})],8)
        tongue.build()
    if species=='monitor':
        lift_monitor_anatomy(rig)
        clean_monitor_triangles()
    consolidate_material_meshes(species)
    normalize_all_skin_weights()
    print('GROUND_ART_MESH '+species,flush=True)
    acts,animreport=animations(species,rig,limbs,tail)
    rig['authored_axes']='Y up; -Z forward; runtime +90X'; rig['walk_stride_m']=.32 if species=='coati' else .40
    rig['walk_cycle_seconds']=(24 if species=='coati' else 32)/24; rig['art_revision']='monitor-solo-v16'
    # Save editable source before the studio objects are created.
    for obj in bpy.data.objects:
        if obj.type=='MESH': obj.data.validate(verbose=True,clean_customdata=False)
    bpy.ops.wm.save_as_mainfile(filepath=str(out/'guardian.blend'))
    bpy.ops.object.select_all(action='SELECT')
    tmp_glb=out/'guardian_tmp.glb'; final_glb=out/'guardian.glb'
    if tmp_glb.exists(): tmp_glb.unlink()
    bpy.ops.export_scene.gltf(filepath=str(tmp_glb),export_format='GLB',use_selection=True,export_animations=True,export_skins=True,export_animation_mode='ACTIONS',export_force_sampling=True,export_frame_range=False,export_apply=False,export_extras=True,export_materials='EXPORT',export_vertex_color='ACTIVE')
    import time,shutil
    for _ in range(10):
        try:
            os.replace(tmp_glb,final_glb); break
        except OSError:
            try:
                shutil.copyfile(tmp_glb,final_glb); tmp_glb.unlink(); break
            except OSError:
                time.sleep(0.25)
    audit=glb_audit(final_glb); audit['animation_contact_audit']=animreport
    (folder/'audit.json').write_text(json.dumps(audit,indent=2))
    print('GROUND_ART_AUDIT '+json.dumps(audit),flush=True)
    if os.environ.get('GROUND_ART_SKIP_EVIDENCE')!='1': render_evidence(species,rig,acts,folder)
    print('GROUND_ART_COMPLETE '+species,flush=True)
