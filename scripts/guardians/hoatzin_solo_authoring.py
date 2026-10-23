"""Deterministic canopy guardian authoring. Authored +Y up/-Z forward.

All geometry and textures are real exported mesh/PBR data. No Blender-only
procedural shading, automatic bone proximity weights, or shared polish pass.
Run either species entry point with Blender 5.2 --background --python.
"""
import bpy, math, random, json, struct, sys
from pathlib import Path
from mathutils import Vector, Matrix

ROOT=Path(__file__).resolve().parents[2]
REPORT=ROOT/'.clanker/tasks/guardian-hoatzin-solo/v14-authoring'
if '--crest-v4' in sys.argv: REPORT=REPORT/'crest-v4'
elif '--bird-face-fix' in sys.argv: REPORT=REPORT/'v3-face-fix'
elif '--porcupine-v2' in sys.argv: REPORT=REPORT/'porcupine-v2'
elif '--v3' in sys.argv: REPORT=REPORT/'v3'
elif '--v2' in sys.argv: REPORT=REPORT/'v2'
TAU=math.tau

def feather_weights(bone,t):
    if bone != 'Tail': return bone
    station=max(0,min(3,t*3)); i=min(2,int(station)); q=station-i; q=q*q*(3-2*q)
    return {'Tail'+str(i):1-q,'Tail'+str(i+1):q}

class Surface:
    def __init__(self): self.v=[]; self.f=[]; self.uv=[]; self.w=[]; self.mi=[]
    def vertex(self,p,uv=(0,0),weights='Body'):
        i=len(self.v); self.v.append(tuple(p)); self.uv.append(uv)
        self.w.append({weights:1.0} if isinstance(weights,str) else weights); return i
    def face(self,indices,mat): self.f.append(tuple(indices)); self.mi.append(mat)
    def ellipsoid(self,c,r,mat,bone,seg=24,rings=14):
        start=len(self.v); c=Vector(c)
        for j in range(rings+1):
            t=math.pi*j/rings
            for k in range(seg+1):
                a=TAU*k/seg
                self.vertex(c+Vector((r[0]*math.sin(t)*math.cos(a),r[1]*math.cos(t),r[2]*math.sin(t)*math.sin(a))), (k/seg,j/rings),bone)
        for j in range(rings):
            for k in range(seg):
                a=start+j*(seg+1)+k; self.face((a,a+1,a+seg+2,a+seg+1),mat)
    def tube(self,points,radii,mat,weights,seg=12):
        pts=list(map(Vector,points)); rows=[]
        for j,p in enumerate(pts):
            tangent=(pts[min(j+1,len(pts)-1)]-pts[max(j-1,0)]).normalized()
            axis=Vector((1,0,0)); axis=(axis-tangent*axis.dot(tangent)).normalized()
            if axis.length<.1: axis=Vector((0,0,1)).cross(tangent).normalized()
            other=tangent.cross(axis).normalized(); row=[]
            rr=radii[j]; rx,ry=(rr,rr) if isinstance(rr,(int,float)) else rr
            for k in range(seg+1):
                a=TAU*k/seg
                row.append(self.vertex(p+axis*(math.cos(a)*rx)+other*(math.sin(a)*ry),(k/seg,j/(len(pts)-1)),weights[j] if isinstance(weights,list) else weights))
            rows.append(row)
        for j in range(len(rows)-1):
            for k in range(seg): self.face((rows[j][k],rows[j][k+1],rows[j+1][k+1],rows[j+1][k]),mat)
        self.face(tuple(reversed(rows[0][:-1])),mat); self.face(rows[-1][:-1],mat)
    def feather(self,base,tip,width,mat,bone,normal=(0,1,0),rows=8,arch=.0015,project=None,uv_rect=(0.0,1.0,0.0,1.0)):
        # 7-column cambered avian feather vane with integrated 3D central rachis ridge
        # and unshared dorsal/ventral perimeter vertices so smooth shading never wraps 180 deg across the razor margin.
        rows=min(rows,12)
        stations=sorted(set([j/rows for j in range(rows+1)]+[.93,.98]))
        rows=len(stations)-1
        b=Vector(base); d=Vector(tip)-b; n=Vector(normal).normalized()
        cross=d.cross(n)
        if cross.length < 1e-6:
            cross=d.cross(Vector((1,0,0)) if abs(d.normalized().x)<.8 else Vector((0,0,1)))
        cross.normalize(); n=cross.cross(d).normalized()
        rng=random.Random(int(abs(b.x*91823+b.y*37121+b.z*19477)*10))
        asymmetry=rng.uniform(.82,.96); phase=rng.uniform(0,TAU)
        profile=[(0,.14),(.12,.52),(.28,.86),(.50,1.0),(.74,.98),(.88,.84),(.96,.48),(1,.04)]
        def vane_width(t):
            for a,c in zip(profile,profile[1:]):
                if a[0]<=t<=c[0]:
                    q=(t-a[0])/(c[0]-a[0]); return a[1]*(1-q)+c[1]*q
            return .02
        u0,u1,v0,v1=uv_rect
        x_cols=(-1.0,-0.60,-0.075,0.0,0.075,0.60,1.0)
        u_cols=(0.0,0.20,0.462,0.50,0.538,0.80,1.0)
        for side in (1,-1):
            layer=[]
            for j in range(rows+1):
                t=stations[j]
                center=b+d*t+n*math.sin(t*math.pi)*(width*.028+arch)+cross*math.sin(t*math.pi)*math.sin(phase)*width*.06
                w=width*vane_width(t)
                w_vtx=feather_weights(bone,max(0,min(1,(b.z+d.z*t-.31)/.51)))
                row=[]
                for k in range(7):
                    x=x_cols[k]; u_loc=u_cols[k]
                    edge=(.96+.04*math.sin(j*1.9+phase)) if abs(x)>.9 else 1.0
                    blade=x*w*edge*(asymmetry if x<0 else 1.0)
                    # Transverse vane camber + raised central rachis shaft at k == 3
                    camber=width*.028*(1.0-x*x)*math.sin(max(0.05,t)*math.pi*.92)
                    rachis_h=(width*.048*(1.0-.70*t) if k==3 else (width*.014*(1.0-.70*t) if k in (2,4) else 0.0))
                    is_rim=(k in (0,6) or j in (0,rows))
                    thick=0.0 if is_rim else max(.00010,width*.008)*(1.0-.75*t)*(1.0-x*x)
                    lift=camber + (rachis_h if side==1 else -rachis_h*.35) + side*thick
                    position=center+cross*blade+n*lift
                    if project: position=project(position,t,side)
                    uv=(u0+u_loc*(u1-u0), v0+t*(v1-v0))
                    row.append(self.vertex(position,uv,w_vtx))
                layer.append(row)
            for j in range(rows):
                for k in range(6):
                    q=(layer[j][k],layer[j][k+1],layer[j+1][k+1],layer[j+1][k])
                    self.face(q if side==1 else tuple(reversed(q)),mat)
    def crest_feather(self,base,tip,width,bend,roll,seed):
        # Curved rachis with attached narrow lanceolate vanes and delicate barb ribbons.
        b=Vector(base); d=Vector(tip)-b; bend=Vector(bend)
        rng=random.Random(seed)
        axis=Vector((math.cos(roll),0,math.sin(roll)))
        def center(t): return b+d*t+bend*(4*t*(1-t))+Vector((.0018*math.sin(t*math.pi*1.3+roll)*t,0,0))
        def basis(t):
            tangent=(center(min(1,t+.005))-center(max(0,t-.005))).normalized()
            cross=(axis-tangent*axis.dot(tangent)).normalized()
            return cross,tangent.cross(cross).normalized()
        def shape(t):
            return math.sin(math.pi*max(0,(t-.06)/.94))**.78 if t>.06 else .08
        def weight(t):
            c=min(1,t/.18);return {'Head':1-c,'Crest':c}
        x_cols=(-1.0,-0.45,0.0,0.45,1.0)
        u_cols=(0.0,0.275,0.50,0.725,1.0)
        for side in (1,-1):
            rows=[]
            for j in range(15):
                t=j/14; cross,n=basis(t); w=width*shape(t)*.68; row=[]
                for k in range(5):
                    x=x_cols[k]; u=u_cols[k]; asym=.88 if x<0 else 1.0
                    rachis_h=(width*.055*(1.0-.65*t) if k==2 else 0.0)
                    is_rim=(k in (0,4) or j in (0,14))
                    thick=0.0 if is_rim else .00009*(1.0-.7*t)*(1.0-x*x)
                    lift=width*.022*(1.0-x*x) + (rachis_h if side==1 else -rachis_h*.3) + side*thick
                    pt=center(t)+cross*(x*w*asym)+n*lift
                    row.append(self.vertex(pt,(u,t),weight(t)))
                rows.append(row)
            for j in range(14):
                for k in range(4):
                    q=(rows[j][k],rows[j][k+1],rows[j+1][k+1],rows[j+1][k])
                    self.face(q if side==1 else tuple(reversed(q)),2)
        # Delicate separated barb ribbons radiating along the plume margins
        for j in range(36):
            t=.14+j*.023+rng.uniform(-.006,.006)
            for side in (-1,1):
                end_t=min(.988,t+rng.uniform(.038,.066)); cross,n=basis(t)
                start=center(t)+n*.00012
                end=center(end_t)+cross*(side*width*shape(t)*rng.uniform(1.02,1.38))+n*.00015
                delta=end-start; edge=delta.cross(n).normalized(); half=rng.uniform(.00015,.00030)*(1-.50*t)
                u_in = 0.50 + side * 0.05; u_out = 0.50 + side * 0.44
                q_top=[self.vertex(pt+n*.00003,uv,weight(t if index<2 else end_t)) for index,(pt,uv) in enumerate([(start-edge*half,(u_in,t)),(start+edge*half,(u_in,t)),(end+edge*half*.18,(u_out,end_t)),(end-edge*half*.18,(u_out,end_t))])]
                q_bot=[self.vertex(pt-n*.00003,uv,weight(t if index<2 else end_t)) for index,(pt,uv) in enumerate([(start-edge*half,(u_in,t)),(start+edge*half,(u_in,t)),(end+edge*half*.18,(u_out,end_t)),(end-edge*half*.18,(u_out,end_t))])]
                self.face(q_top,2); self.face(tuple(reversed(q_bot)),2)

    def object(self,name,mats,rig):
        me=bpy.data.meshes.new(name); me.from_pydata(self.v,[],self.f); me.update()
        # Remove collapsed sphere poles and sub-micron vane-tip triangles before
        # export while retaining per-vertex weights and each triangle's material.
        me.calc_loop_triangles(); faces=[]; indices=[]
        for tri in me.loop_triangles:
            a,b,c=[Vector(self.v[i]) for i in tri.vertices]
            if (b-a).cross(c-a).length_squared <= 1.1e-18: continue
            faces.append(tuple(tri.vertices)); indices.append(self.mi[tri.polygon_index])
        me.clear_geometry(); me.from_pydata(self.v,[],faces); me.update(); self.mi=indices
        ob=bpy.data.objects.new(name,me); bpy.context.collection.objects.link(ob)
        for mat in mats: me.materials.append(mat)
        uv=me.uv_layers.new(name='SurfaceUV')
        for p,mat in zip(me.polygons,self.mi):
            p.material_index=mat; p.use_smooth=True
            for li in p.loop_indices: uv.data[li].uv=self.uv[me.loops[li].vertex_index]
        for b in rig.data.bones: ob.vertex_groups.new(name=b.name)
        for i,w in enumerate(self.w):
            for name,val in w.items(): ob.vertex_groups[name].add([i],val,'REPLACE')
        ob.parent=rig; mod=ob.modifiers.new('AnatomicalSkin','ARMATURE'); mod.object=rig
        return ob

def texture(name,path,kind,color):
    size=512 if kind in ('feather','feather_normal','down','down_normal') else 256
    im=bpy.data.images.new(name,width=size,height=size,alpha=True)
    if kind.endswith('normal'): im.colorspace_settings.name='Non-Color'
    pixels=[]
    for y in range(size):
        t=y/(size-1)
        for x in range(size):
            u=x/(size-1); noise=math.sin(x*12.9898+y*78.233)*43758.5453; noise-=math.floor(noise)
            if kind in ('feather','streak'):
                # Oblique barbs share a rachis; variable spacing/phase and weak
                # contrast avoid the v1 repeated woven sinusoid.
                coordinate=t-abs(u-.5)*.47+.005*math.sin(t*31+u*17)
                line=math.exp(-math.sin(coordinate*math.pi*92)**2/.035)
                shaft=math.exp(-((u-.5)/.010)**2)
                mottling=.97+.025*math.sin(t*21+u*7)+.015*math.sin(t*67-u*19)
                rgb=[min(1,c*(mottling-.08*line+.09*shaft)+.004*(noise-.5)) for c in color]
                if kind=='feather' and name=='Feather0':
                    # Narrow worn buff margins around dark, broad rounded coverts.
                    edge=max(math.exp(-((u-.03)/.026)**2),math.exp(-((u-.97)/.026)**2))*.065
                    edge=max(edge,math.exp(-((t-.96)/.032)**2)*.14)
                    edge*=.78+.22*math.sin(u*13+t*31)**2
                    rgb=[v*(1-edge)+c*edge for v,c in zip(rgb,(.28,.255,.225))]
                if kind=='streak':
                    streak=math.exp(-((u-.5)/.045)**2)*min(1,max(0,(t-.12)*4))*.72
                    rgb=[v*(1-streak)+p*streak for v,p in zip(rgb,(.50,.38,.21))]
            elif kind=='feather_normal':
                coordinate=t-abs(u-.5)*.47+.005*math.sin(t*31+u*17)
                a=.012*math.sin(coordinate*math.pi*184)*(-1 if u<.5 else 1)
                b=.006*math.sin(coordinate*math.pi*184)+.0015*(noise-.5)
                rgb=(.5+a,.5+b,math.sqrt(max(0,1-(a*2)**2-(b*2)**2))*.5+.5)
            elif kind=='skin':
                tone=.91+.065*math.sin(u*7+t*11)+.035*math.sin(u*37-t*23)+.025*(noise-.5)
                rgb=[c*tone for c in color]
            elif kind=='skin_normal':
                a=.004*math.sin(u*127+t*211)+.002*(noise-.5); b=.004*math.sin(u*177-t*137)
                rgb=(.5+a,.5+b,1)
            elif kind=='down':
                tone=.94+.035*math.sin(u*19+t*13)+.022*math.sin(u*61-t*27)+.018*(noise-.5)
                rgb=[c*tone for c in color]
            elif kind=='down_normal':
                a=.003*math.sin(u*97+t*43)+.002*(noise-.5)
                b=.003*math.sin(u*53-t*113)+.002*(noise-.5)
                rgb=(.5+a,.5+b,math.sqrt(max(0,1-(a*2)**2-(b*2)**2))*.5+.5)
            elif kind=='fur':
                strand=math.sin(u*217+t*33+.20*math.sin(t*61))
                tone=.91+.04*strand+.035*math.sin(u*17+t*13)+.025*(noise-.5)
                rgb=[c*tone for c in color]
            elif kind=='fur_normal':
                a=.010*math.sin(u*217+t*33+.20*math.sin(t*61))+.003*(noise-.5)
                b=.003*math.sin(u*91-t*47)
                rgb=(.5+a,.5+b,1)
            elif kind=='quill':
                c=(.13,.10,.065) if t<.18 else ((.68,.57,.33) if t<.60 else ((.055,.035,.02) if t<.77 else (.87,.81,.61)))
                rgb=[v*(.85+.15*math.sin(u*math.pi)) for v in c]
            elif kind=='normal':
                a=.09*math.sin((u*1.3+t*.23)*610); b=.025*(noise-.5)
                rgb=(.5+a,.5+b,math.sqrt(max(0,1-(a*2)**2-(b*2)**2))*.5+.5)
            else:
                strand=.83+.17*math.sin((u+t*.11)*680); rgb=[c*(strand*.9+noise*.13) for c in color]
            pixels.extend((*rgb,1))
    im.pixels.foreach_set(pixels); im.filepath_raw=str(path); im.file_format='PNG'; im.save(); im.pack(); return im

def material(name,color,rough=.65,image=None,normal=None,normal_strength=.30,spec=.50):
    m=bpy.data.materials.new(name); m.use_nodes=True; p=m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=(*color,1); p.inputs['Roughness'].default_value=rough
    if 'Specular IOR Level' in p.inputs: p.inputs['Specular IOR Level'].default_value=spec
    if image:
        node=m.node_tree.nodes.new('ShaderNodeTexImage'); node.image=image
        m.node_tree.links.new(node.outputs['Color'],p.inputs['Base Color'])
    if normal:
        tex=m.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=normal; normal.colorspace_settings.name='Non-Color'
        n=m.node_tree.nodes.new('ShaderNodeNormalMap'); n.inputs['Strength'].default_value=normal_strength
        m.node_tree.links.new(tex.outputs['Color'],n.inputs['Color']); m.node_tree.links.new(n.outputs['Normal'],p.inputs['Normal'])
    return m

def rig_for(species):
    avian=species=='crested_canopy'; defs={}
    def add(name,h,t,parent='Root'): defs[name]=(h,t,parent)
    add('Root',(0,0,0),(0,.1,0),None)
    add('Body',(0,.27,.18),(0,.30,-.12))
    add('Chest',(0,.285,-.07),(0,.30,-.19),'Body')
    add('Neck',(0,.30,-.19),(0,.45,-.37),'Chest')
    add('Head',(0,.45,-.37),(0,.53,-.56),'Neck') if avian else add('Head',(0,.29,-.31),(0,.30,-.48),'Body')
    add('Jaw',(0,.49,-.53),(0,.49,-.66),'Head') if avian else add('Jaw',(0,.22,-.45),(0,.23,-.55),'Head')
    if avian:
        add('Crest',(0,.57,-.48),(0,.71,-.39),'Head')
        for side,n in ((1,'L'),(-1,'R')):
            for upper,label in ((True,'Upper'),(False,'Lower')):
                y=.5484 if upper else .5316
                add('Lid'+label+'_'+n,(side*.043,y,-.542),(side*.043,y,-.522),'Head')
        tailpts=[(0,.27,.30),(0,.258,.43),(0,.242,.56),(0,.225,.69),(0,.21,.82)]
        for i in range(4): add('Tail'+str(i),tailpts[i],tailpts[i+1],'Body' if i==0 else 'Tail'+str(i-1))
        for s,n in ((1,'L'),(-1,'R')):
            add('Wing_'+n,(s*.13,.29,-.08),(s*.18,.30,.22),'Body')
            add('Flight_'+n,(s*.18,.30,.22),(s*.19,.23,.56),'Wing_'+n)
    else:
        pts=[(0,.26,.33),(0,.23,.45),(0,.21,.57),(0,.21,.69),(0,.25,.80),(0,.33,.85),(0,.40,.81),(0,.42,.73)]
        for i in range(7): add('Tail'+str(i+1),pts[i],pts[i+1],'Body' if i==0 else 'Tail'+str(i))
    limbs=[]
    for s,n in ((1,'L'),(-1,'R')):
        specs=[('Hind', (s*.095,.25,.14),(s*.11,.12,.08),(s*.11,.027,.15),(s*.11,.025,.07))] if avian else [
            ('Front',(s*.145,.29,-.15),(s*.19,.16,-.12),(s*.18,.036,-.23),(s*.18,.027,-.29)),
            ('Hind',(s*.14,.26,.22),(s*.19,.14,.16),(s*.18,.036,.27),(s*.18,.027,.21))]
        for typ,h,k,a,toe in specs:
            names=(typ+'Upper_'+n,typ+'Lower_'+n,typ+'Foot_'+n)
            add(names[0],h,k); add(names[1],k,a,names[0]); add(names[2],a,toe,names[1])
            limbs.append((names,Vector(h),Vector(k),Vector(a),Vector(toe),s))
    ar=bpy.data.armatures.new('CanopySkeleton'); ob=bpy.data.objects.new('GuardianRig',ar); bpy.context.collection.objects.link(ob)
    bpy.context.view_layer.objects.active=ob; ob.select_set(True); bpy.ops.object.mode_set(mode='EDIT')
    for name,(h,t,parent) in defs.items():
        b=ar.edit_bones.new(name); b.head=h; b.tail=t
        if parent: b.parent=ar.edit_bones[parent]
    bpy.ops.object.mode_set(mode='OBJECT')
    for b in ob.pose.bones: b.rotation_mode='QUATERNION'
    return ob,limbs

def body_loft(surface,profile,mat,avian=False):
    # Cubic profile interpolation gives continuous neck/chest/abdomen masses.
    rows=[]; seg=40; samples=[]
    for j in range(len(profile)-1):
        p0=profile[max(0,j-1)]; p1=profile[j]; p2=profile[j+1]; p3=profile[min(len(profile)-1,j+2)]
        for k in range(5):
            t=k/5
            sample=[.5*((2*p1[d])+(-p0[d]+p2[d])*t+(2*p0[d]-5*p1[d]+4*p2[d]-p3[d])*t*t+(-p0[d]+3*p1[d]-3*p2[d]+p3[d])*t*t*t) for d in range(4)]
            samples.append(sample)
    samples.append(profile[-1])
    for j,(z,y,rx,ry) in enumerate(samples):
        if avian:
            head=max(0,min(1,(-z-.37)/.09)); neck=max(0,min(1,(-z-.17)/.13))*(1-head)
            chest=max(0,min(1,(-z+.03)/.17))*(1-head-neck)
            weights={'Body':1-head-neck-chest,'Chest':chest,'Neck':neck,'Head':head}
        else:
            head=max(0,min(1,(-z-.25)/.12)); weights={'Body':1-head,'Head':head}
        row=[]
        for k in range(seg+1):
            a=TAU*k/seg; x=math.sin(a)*max(.004,rx); yy=y+math.cos(a)*max(.004,ry)
            # Sternum/belly kept round; shoulders broaden only above midline.
            row.append(surface.vertex((x,yy,z),(k/seg,j/(len(samples)-1)),weights))
        rows.append(row)
    for j in range(len(rows)-1):
        for k in range(seg):
            face_mat = mat
            if avian:
                z_mid = samples[j][0]
                a_mid = TAU * (k + 0.5) / seg
                ca = math.cos(a_mid)
                sa = abs(math.sin(a_mid))
                if z_mid < -0.505:
                    # Head & face: bare cobalt-turquoise skin strictly on lateral orbital zone; buff on chin; dark bronze-sepia under crown/occiput
                    if z_mid > -0.568 and sa > 0.52 and -0.48 < ca < 0.72: face_mat = 4
                    elif ca < -0.32: face_mat = 1
                    else: face_mat = 10
                elif z_mid < -0.22:
                    # Neck: ventral throat is golden-buff bib, dorsal/lateral nape is streaked dark bronze-sepia
                    face_mat = 1 if ca < -0.28 else 10
                elif z_mid < -0.04:
                    # Upper breast/crop: ventral crop is rich rufous-chestnut; dorsal mantle is streaked bronze-sepia
                    face_mat = 8 if ca < 0.15 else 10
                else:
                    # Lower breast, belly, flanks, and lower torso: deep rufous-chestnut
                    face_mat = 8
            surface.face((rows[j][k],rows[j+1][k],rows[j+1][k+1],rows[j][k+1]),face_mat)
    front_cap_mat = 5 if avian else mat
    surface.face(rows[0][:-1],front_cap_mat); surface.face(tuple(reversed(rows[-1][:-1])),mat)
    return samples

def eyes(s,x,y,z,r,iris,black,skin,avian):
    for side in (-1,1):
        # Orbital socket, iris dome, black pupil: smooth wet-eye geometry.
        s.ellipsoid((side*x,y,z),(r*.42,r*1.17,r*1.1),skin,'Head',24,12)
        s.ellipsoid((side*(x+r*.24),y,z),(r*.38,r,r),iris,'Head',24,12)
        s.ellipsoid((side*(x+r*.54),y,z-.002),(r*.10,r*.54,r*.55),black,'Head',20,10)

def _avian_toe_and_talon(s, p_start, p_end, r_base, n_phalanges, mat_skin, mat_claw, bone, side):
    """Build an articulated avian digit with fleshy plantar pads, dorsal phalangeal scutes, and a curved keratinous talon."""
    d_vec = p_end - p_start
    length = d_vec.length
    fwd = d_vec.normalized()
    up = Vector((0.0, 1.0, 0.0))
    right = fwd.cross(up)
    if right.length < 1e-5:
        right = Vector((1.0, 0.0, 0.0))
    right.normalize()
    up = right.cross(fwd).normalized()

    stations = 10
    seg = 12
    rows = []
    for j in range(stations + 1):
        t = j / stations
        # Subtle arch along toe and phalangeal pad undulations
        arch_y = math.sin(t * math.pi) * 0.0035
        pad_bulge = abs(math.sin(t * math.pi * n_phalanges)) * 0.0016 * (1.0 - 0.35 * t)
        center = p_start.lerp(p_end, t) + Vector((0.0, arch_y, 0.0))
        rx = r_base * (1.0 - 0.46 * (t ** 1.15)) + pad_bulge * 0.45
        ry = r_base * (0.92 - 0.42 * (t ** 1.15)) + pad_bulge
        v_uv = 0.04 + 0.42 * t  # Lower half of horn-bill.png = scutellate/reticulate leg scales
        row = []
        for k in range(seg + 1):
            a = TAU * k / seg
            ca = math.cos(a)
            sa = math.sin(a)
            # Flatten ventral sole slightly so digital pads rest naturally on perch/ground
            y_off = sa * ry * (0.72 if sa < 0 else 1.08)
            pt = center + right * (ca * rx) + up * y_off
            if pt.y < 0.0015:
                pt.y = 0.0015
            row.append(s.vertex(pt, (k / seg, v_uv), bone))
        rows.append(row)
    for j in range(stations):
        for k in range(seg):
            s.face((rows[j][k], rows[j][k + 1], rows[j + 1][k + 1], rows[j + 1][k]), mat_skin)
    s.face(tuple(reversed(rows[0][:-1])), mat_skin)
    s.face(rows[-1][:-1], mat_skin)

    # Dorsal transverse digital scute plates along the toe
    n_scutes = n_phalanges * 3 + 1
    for sc in range(n_scutes):
        t0 = 0.06 + sc * (0.84 / n_scutes)
        t1 = t0 + (0.92 / n_scutes)
        c0 = p_start.lerp(p_end, t0) + Vector((0.0, math.sin(t0 * math.pi) * 0.0035, 0.0))
        c1 = p_start.lerp(p_end, t1) + Vector((0.0, math.sin(t1 * math.pi) * 0.0035, 0.0))
        rx0 = r_base * (1.0 - 0.46 * (t0 ** 1.15)) * 1.08
        ry0 = r_base * (0.92 - 0.42 * (t0 ** 1.15)) * 1.14
        rx1 = r_base * (1.0 - 0.46 * (t1 ** 1.15)) * 1.12
        ry1 = r_base * (0.92 - 0.42 * (t1 ** 1.15)) * 1.18
        r_a = []
        r_b = []
        for k in range(7):
            ang = -1.15 + 2.30 * k / 6.0
            u_sc = 0.28 + 0.44 * (k / 6.0)
            p0 = c0 + right * (math.sin(ang) * rx0) + up * (math.cos(ang) * ry0)
            p1 = c1 + right * (math.sin(ang) * rx1) + up * (math.cos(ang) * ry1)
            r_a.append(s.vertex(p0, (u_sc, 0.12), bone))
            r_b.append(s.vertex(p1, (u_sc, 0.28), bone))
        for k in range(6):
            s.face((r_a[k], r_b[k], r_b[k + 1], r_a[k + 1]), mat_skin)

    # Curved, laterally compressed, dorsally keeled avian talon (ungual claw)
    claw_len = 0.022 if n_phalanges >= 3 else 0.020
    claw_rows = []
    c_seg = 10
    base_c = p_end + Vector((0.0, 0.001, 0.0))
    for j in range(7):
        tc = j / 6.0
        # Arch forward and downward into a sharp gripping hook
        reach = claw_len * (tc - 0.08 * (tc ** 2))
        drop = 0.0035 * math.sin(tc * math.pi * 0.75) - 0.0145 * (tc ** 1.85)
        c_pt = base_c + fwd * reach + Vector((0.0, drop, 0.0))
        if c_pt.y < 0.0012:
            c_pt.y = 0.0012
        taper = (1.0 - tc) ** 1.15
        crx = max(0.00035, r_base * 0.42 * taper)
        cry = max(0.00045, r_base * 0.66 * taper)
        v_claw = 0.56 + 0.40 * tc  # Upper half of horn-bill.png = smooth dark horn keratin with tip wear
        row = []
        for k in range(c_seg + 1):
            a = TAU * k / c_seg
            ca = math.cos(a)
            sa = math.sin(a)
            # Dorsal longitudinal keel on talon (sa > 0)
            keel = 1.18 if sa > 0.5 else (0.78 if sa < -0.3 else 1.0)
            pt = c_pt + right * (ca * crx) + up * (sa * cry * keel)
            if pt.y < 0.0010:
                pt.y = 0.0010
            row.append(s.vertex(pt, (k / c_seg, v_claw), bone))
        claw_rows.append(row)
    for j in range(6):
        for k in range(c_seg):
            s.face((claw_rows[j][k], claw_rows[j][k + 1], claw_rows[j + 1][k + 1], claw_rows[j + 1][k]), mat_claw)
    s.face(tuple(reversed(claw_rows[0][:-1])), mat_claw)
    s.face(claw_rows[-1][:-1], mat_claw)


def limbs_mesh(s,limbs,bodymat,skinmat,clawmat,avian):
    for names,h,k,a,toe,side in limbs:
        upper,lower,foot=names
        if not avian:
            s.tube([h,h.lerp(k,.5),k],[.057,.049,.033],bodymat,[upper,upper,{upper:.5,lower:.5}],16)
            s.tube([k,k.lerp(a,.45),a],[.033,.025,.022],skinmat,[lower,lower,{lower:.8,foot:.2}],14)
            s.ellipsoid(k,(.036,.034,.037),bodymat,{upper:.5,lower:.5},18,10)
            s.ellipsoid(toe.lerp(a,.48),(.046,.015,.033),skinmat,foot,20,10)
            for d in range(4):
                x=(d-1.5)*.024
                base=Vector((a.x+x*.3,.026,a.z-.015)); mid=Vector((a.x+x,.021,a.z-.050)); end=Vector((a.x+x*1.3,.020,a.z-.082+abs(x)*.4))
                s.tube([base,mid,end],[.010,.008,.005],skinmat,foot,10)
                s.tube([end,end+Vector((0,.008,-.012)),end+Vector((0,-.001,-.025))],[.005,.004,.0006],clawmat,foot,8)
            continue

        # --- AVIAN LEG, CRURAL PLUMAGE, TARSOMETATARSUS & ANISODACTYL PERCHING FOOT ---
        thigh_mat = 8
        s.tube([h, h.lerp(k, .5), k], [.038, .030, .020], thigh_mat, [upper, upper, {upper: .5, lower: .5}], 16)
        s.ellipsoid(k, (.020, .021, .022), thigh_mat, {upper: .5, lower: .5}, 16, 10)

        # Shingled downward-sweeping rufous-chestnut crural feathers clothing the tibiotarsus (thigh)
        crural_rng = random.Random(441 if side > 0 else 442)
        leg_axis = (k - h).normalized()
        for r_idx in range(7):
            t_bone = 0.06 + r_idx * 0.132
            p_mid = h.lerp(k, t_bone)
            r_rad = 0.036 * (1.0 - 0.42 * t_bone)
            w_bone = upper if t_bone < 0.72 else {upper: 0.65, lower: 0.35}
            n_feathers = 14
            for f_idx in range(n_feathers):
                f_ang = TAU * (f_idx + 0.5 * (r_idx % 2)) / n_feathers + crural_rng.uniform(-0.08, 0.08)
                radial = Vector((math.sin(f_ang), 0.0, math.cos(f_ang)))
                radial = (radial - leg_axis * radial.dot(leg_axis)).normalized()
                f_base = p_mid + radial * (r_rad + 0.001)
                f_len = crural_rng.uniform(0.030, 0.040) * (1.0 - 0.18 * t_bone)
                # Sweep downward along the leg axis with subtle outward shingle overlap
                f_dir = (leg_axis * 0.92 + radial * 0.24).normalized()
                f_tip = f_base + f_dir * f_len
                f_width = crural_rng.uniform(0.011, 0.015)
                s.feather(f_base, f_tip, f_width, 8, w_bone, normal=radial, rows=5, arch=0.0015)

        # Sculpted Avian Tarsometatarsus (k -> a) mapped to scutellate/reticulate scale region (v in [0.04, 0.46])
        tarsus_pts = [k.lerp(a, j / 8.0) for j in range(9)]
        tarsus_r = [(.018, .020), (.015, .017), (.013, .015), (.0115, .0135), (.0105, .0125), (.010, .012), (.010, .012), (.011, .013), (.013, .015)]
        tarsus_rows = []
        t_seg = 16
        t_tangent = (a - k).normalized()
        t_right = Vector((1.0, 0.0, 0.0))
        t_right = (t_right - t_tangent * t_right.dot(t_tangent)).normalized()
        t_fwd = t_right.cross(t_tangent).normalized()  # Points roughly -Z (anterior)
        for j, p_t in enumerate(tarsus_pts):
            rx, rz = tarsus_r[j]
            v_uv = 0.04 + 0.42 * (j / 8.0)
            w_t = {upper: 0.3, lower: 0.7} if j == 0 else (lower if j < 7 else {lower: 0.75, foot: 0.25})
            row = []
            for k_idx in range(t_seg + 1):
                ang = TAU * k_idx / t_seg
                pt = p_t + t_right * (math.sin(ang) * rx) + t_fwd * (math.cos(ang) * rz)
                row.append(s.vertex(pt, (k_idx / t_seg, v_uv), w_t))
            tarsus_rows.append(row)
        for j in range(8):
            for k_idx in range(t_seg):
                s.face((tarsus_rows[j][k_idx], tarsus_rows[j][k_idx + 1], tarsus_rows[j + 1][k_idx + 1], tarsus_rows[j + 1][k_idx]), skinmat)

        # 10 Contoured Anterior Acrotarsial Scute Plates hugging the front of the tarsometatarsus
        for sc in range(10):
            t0 = 0.12 + sc * 0.078
            t1 = t0 + 0.088
            p0 = k.lerp(a, t0)
            p1 = k.lerp(a, t1)
            rx0 = 0.0115 + 0.004 * (1.0 - t0)
            rz0 = 0.0135 + 0.004 * (1.0 - t0)
            r_top = []
            r_bot = []
            for k_idx in range(7):
                ang = -1.10 + 2.20 * (k_idx / 6.0)
                u_sc = 0.28 + 0.44 * (k_idx / 6.0)
                pt0 = p0 + t_right * (math.sin(ang) * rx0 * 1.05) + t_fwd * (math.cos(ang) * rz0 * 1.07)
                pt1 = p1 + t_right * (math.sin(ang) * rx0 * 1.09) + t_fwd * (math.cos(ang) * rz0 * 1.11)
                r_top.append(s.vertex(pt0, (u_sc, 0.10), lower))
                r_bot.append(s.vertex(pt1, (u_sc, 0.26), lower))
            for k_idx in range(6):
                s.face((r_top[k_idx], r_bot[k_idx], r_bot[k_idx + 1], r_top[k_idx + 1]), clawmat)

        # Compact Tarso-Metatarsal Trochlear Pad at ankle `a` (replaces the giant flat hamburger bun!)
        s.ellipsoid(a + Vector((0.0, -0.004, -0.006)), (.015, .011, .016), skinmat, foot, 16, 10)

        # Articulated Anisodactyl Perching Digits:
        # Digit II (Inner Anterior), Digit III (Middle Anterior, longest), Digit IV (Outer Anterior), Digit I (Posterior Hallux)
        toe_specs = [
            # (start_offset, end_offset, base_radius, n_phalanges)
            (Vector((-side * 0.006, -0.005, -0.010)), Vector((-side * 0.025, -0.013, -0.072)), 0.0078, 3),
            (Vector((0.000, -0.004, -0.012)), Vector((0.000, -0.013, -0.088)), 0.0086, 4),
            (Vector((side * 0.006, -0.005, -0.010)), Vector((side * 0.027, -0.013, -0.075)), 0.0078, 4),
            (Vector((-side * 0.003, -0.005, 0.006)), Vector((-side * 0.012, -0.013, 0.066)), 0.0076, 2),
        ]
        for start_off, end_off, r_toe, n_phal in toe_specs:
            _avian_toe_and_talon(s, a + start_off, a + end_off, r_toe, n_phal, skinmat, clawmat, foot, side)


def _build_hoatzin_bill_and_nares(s):
    """Sculpted 3D folivorous/gallinaceous Hoatzin maxilla, pervious nostrils (nares), palatal roof, and mandible."""
    # Upper Maxilla (Head bone): stout, deep, laterally compressed, decurved culmen ridge and sharp tomia
    # Stations: (z, culmen_y, tomium_y, half_width_tomium, culmen_half_width)
    max_stations = [
        (-0.558, 0.550, 0.522, 0.0195, 0.0065),
        (-0.568, 0.552, 0.522, 0.0185, 0.0055),
        (-0.579, 0.551, 0.523, 0.0168, 0.0045),
        (-0.590, 0.547, 0.522, 0.0145, 0.0036),
        (-0.600, 0.541, 0.520, 0.0115, 0.0028),
        (-0.609, 0.533, 0.518, 0.0082, 0.0020),
        (-0.616, 0.524, 0.515, 0.0048, 0.0013),
        (-0.622, 0.514, 0.511, 0.0016, 0.0006),
    ]
    # Cross-section around maxilla: 12 points from dorsal culmen -> right wall -> right tomium -> palate -> left tomium -> left wall
    max_rows = []
    for j, (z, cy_top, ty_bot, wx, cx) in enumerate(max_stations):
        t = j / (len(max_stations) - 1)
        v_uv = 0.54 + 0.44 * t  # Upper half of horn-bill.png = bill keratin with culmen/tomium wear
        mid_y = 0.5 * (cy_top + ty_bot)
        palate_y = ty_bot + 0.0035 * (1.0 - t)
        pts_sec = [
            (0.0, cy_top, 0.50),                              # 0: Culmen apex
            (cx, cy_top - 0.0015 * (1.0 - 0.6 * t), 0.56),    # 1: Right culmen shoulder
            (wx * 0.72, mid_y + 0.002, 0.70),                 # 2: Right upper lore/maxillary wall
            (wx * 0.96, ty_bot + 0.004 * (1.0 - t), 0.84),    # 3: Right lower maxillary wall
            (wx, ty_bot, 0.96),                               # 4: Right tomium cutting edge
            (wx * 0.55, palate_y, 0.80),                      # 5: Right palatal roof
            (0.0, palate_y + 0.001, 0.50),                    # 6: Median palatal suture
            (-wx * 0.55, palate_y, 0.20),                     # 7: Left palatal roof
            (-wx, ty_bot, 0.04),                              # 8: Left tomium cutting edge
            (-wx * 0.96, ty_bot + 0.004 * (1.0 - t), 0.16),   # 9: Left lower maxillary wall
            (-wx * 0.72, mid_y + 0.002, 0.30),                # 10: Left upper maxillary wall
            (-cx, cy_top - 0.0015 * (1.0 - 0.6 * t), 0.44),   # 11: Left culmen shoulder
            (0.0, cy_top, 0.50),                              # 12: Wrap to culmen apex
        ]
        row = [s.vertex((px, py, z), (u_sec, v_uv), 'Head') for (px, py, u_sec) in pts_sec]
        max_rows.append(row)
    for j in range(len(max_rows) - 1):
        for k in range(12):
            f_mat = 7 if k in (5, 6) else 5
            s.face((max_rows[j][k], max_rows[j][k + 1], max_rows[j + 1][k + 1], max_rows[j + 1][k]), f_mat)
    s.face(tuple(reversed(max_rows[0][:-1])), 5)
    s.face(max_rows[-1][:-1], 5)

    # Sculpted Pervious Nostrils (Nares) with raised opercular keratin rim and recessed dark cavity
    for side in (-1, 1):
        nx_c, ny_c, nz_c = side * 0.0136, 0.536, -0.579
        rim_outer = []
        rim_inner = []
        cavity_deep = []
        for k in range(17):
            a = TAU * k / 16.0
            dy = math.sin(a) * 0.0024
            dz = math.cos(a) * 0.0046
            p_out = (nx_c + side * 0.0006, ny_c + dy * 1.35, nz_c + dz * 1.28)
            p_in = (nx_c + side * 0.0014, ny_c + dy * 0.85, nz_c + dz * 0.85)
            p_deep = (nx_c - side * 0.0022, ny_c + dy * 0.45, nz_c + dz * 0.45)
            rim_outer.append(s.vertex(p_out, (0.5, 0.68), 'Head'))
            rim_inner.append(s.vertex(p_in, (0.5, 0.74), 'Head'))
            cavity_deep.append(s.vertex(p_deep, (0.5, 0.80), 'Head'))
        for k in range(16):
            q1 = (rim_outer[k], rim_outer[k + 1], rim_inner[k + 1], rim_inner[k])
            q2 = (rim_inner[k], rim_inner[k + 1], cavity_deep[k + 1], cavity_deep[k])
            s.face(q1 if side == 1 else tuple(reversed(q1)), 5)
            s.face(q2 if side == 1 else tuple(reversed(q2)), 7)
        s.face(cavity_deep[:-1] if side == 1 else tuple(reversed(cavity_deep[:-1])), 7)

    # Lower Mandible (Jaw bone): U-shaped mandibular rami and keeled gonys symphysis
    # Stations: (z, tomium_y, gonys_y, half_width)
    mand_stations = [
        (-0.560, 0.522, 0.508, 0.0172),
        (-0.572, 0.522, 0.507, 0.0155),
        (-0.584, 0.521, 0.506, 0.0132),
        (-0.595, 0.520, 0.506, 0.0102),
        (-0.605, 0.518, 0.508, 0.0068),
        (-0.612, 0.515, 0.510, 0.0036),
        (-0.617, 0.513, 0.511, 0.0012),
    ]
    mand_rows = []
    for j, (z, ty_top, gy_bot, wx) in enumerate(mand_stations):
        t = j / (len(mand_stations) - 1)
        v_uv = 0.54 + 0.42 * t
        floor_y = ty_top - 0.0032 * (1.0 - t)
        pts_sec = [
            (0.0, gy_bot, 0.50),                           # 0: Ventral gonys keel
            (wx * 0.62, gy_bot + 0.002, 0.68),             # 1: Right lower ramus
            (wx, (ty_top + gy_bot) * 0.5, 0.84),           # 2: Right outer ramus wall
            (wx * 0.95, ty_top, 0.96),                     # 3: Right mandibular tomium
            (wx * 0.50, floor_y, 0.75),                    # 4: Right oral floor
            (0.0, floor_y - 0.001, 0.50),                  # 5: Median tongue/oral trough
            (-wx * 0.50, floor_y, 0.25),                   # 6: Left oral floor
            (-wx * 0.95, ty_top, 0.04),                    # 7: Left mandibular tomium
            (-wx, (ty_top + gy_bot) * 0.5, 0.16),          # 8: Left outer ramus wall
            (-wx * 0.62, gy_bot + 0.002, 0.32),            # 9: Left lower ramus
            (0.0, gy_bot, 0.50),                           # 10: Wrap to ventral gonys keel
        ]
        row = [s.vertex((px, py, z), (u_sec, v_uv), 'Jaw') for (px, py, u_sec) in pts_sec]
        mand_rows.append(row)
    for j in range(len(mand_rows) - 1):
        for k in range(10):
            f_mat = 7 if k in (4, 5) else 5
            s.face((mand_rows[j][k], mand_rows[j + 1][k], mand_rows[j + 1][k + 1], mand_rows[j][k + 1]), f_mat)
    s.face(mand_rows[0][:-1], 5)
    s.face(tuple(reversed(mand_rows[-1][:-1])), 5)


def bird(s,m,limbs):
    # UV sub-rectangles for our 2-column 2048x2048 Wing & Mantle Atlas (feather-0.png):
    # Column 0: Iridescent dark bronzy-olive wing coverts & tertials with creamy-buff terminal wing-bar crescent
    # Column 1: Glowing deep rufous-chestnut primary & secondary remiges with dark bronzy-sepia distal tips
    UV_COV = (0.03, 0.47, 0.0, 1.0)
    UV_PRI = (0.53, 0.97, 0.0, 1.0)
    UV_FULL = (0.0, 1.0, 0.0, 1.0)

    # Cranial & torso loft starting cleanly at z = -0.572 inside the bill base (no protruding blue snout tube!)
    profile=[(-.572,.533,.016,.016),(-.558,.535,.030,.029),(-.534,.535,.043,.041),(-.485,.524,.049,.044),(-.430,.490,.047,.042),(-.39,.452,.038,.048),(-.30,.395,.046,.064),(-.19,.286,.109,.129),(-.07,.275,.146,.144),(.10,.285,.154,.142),(.25,.285,.131,.114),(.36,.280,.052,.052)]
    samples=body_loft(s,profile,8,True)
    def body_at(z):
        if z<=samples[0][0]: return samples[0][1:]
        for a,b in zip(samples,samples[1:]):
            if a[0]<=z<=b[0]:
                t=(z-a[0])/(b[0]-a[0]); return tuple(a[i]*(1-t)+b[i]*t for i in range(1,4))
        return samples[-1][1:]
    def face_x(y,z):
        cy,rx,ry=body_at(z)
        return rx*math.sqrt(max(.02,1-((y-cy)/ry)**2))
    rnd=random.Random(713)
    def surface_at(z,a,offset=.001):
        cy,rx,ry=body_at(z); return Vector((math.sin(a)*rx,cy+math.cos(a)*ry,z))+Vector((math.sin(a),math.cos(a),0))*offset

    # 1. Complete Shingled Neck, Mantle, Scapulars, Back, Flanks & Belly Plumage (z = -0.45 to +0.35)
    for row in range(28):
        z=-.44+row*.0285
        cy,rx,ry=body_at(z)
        count=32 if z<-.24 else 38
        for k in range(count):
            angle=TAU*(k+rnd.uniform(-.16,.16)+(row%2)*.5)/count
            normal=Vector((math.sin(angle),math.cos(angle),0)); b=surface_at(z,angle,.002)
            # Only skip feathers strictly inside the small buried wing hinge socket
            if -.06 < z < .18 and .24 < b.y < .33 and abs(b.x) > .122: continue
            length=rnd.uniform(.048,.066) if z>-.22 else rnd.uniform(.028,.040)
            tip=surface_at(min(.38,z+length),angle+rnd.uniform(-.035,.035),.0045)
            bone='Head' if z<-.41 else ('Neck' if z<-.26 else ('Chest' if z<-.08 else 'Body'))
            cos_a = math.cos(angle)
            uv_r = UV_FULL
            if z < -.22:
                # Neck: ventral throat (cos_a < -0.30) is warm ochraceous-buff; sides and nape are cream-streaked dark bronze-sepia
                mat = 1 if cos_a < -.30 else 10
            elif z < -.14:
                # Clavicle & upper mantle transition: throat bib blends into rufous-chestnut crop; dorsal mantle is cream-streaked
                if cos_a < -.25:
                    mat = 1 if rnd.random() < (-z - .14) / .08 else 8
                else:
                    mat = 10 if cos_a > .05 else 8
            elif z < .16:
                # Mid-back & scapulars vs lower crop, breast, and flanks
                if cos_a > .42:
                    # Dorsal mantle & scapulars: blend of cream-streaked mantle feathers (10) and buff-edged bronze coverts (0)
                    if z < .04 or abs(math.sin(angle)) > .48:
                        mat = 10
                    else:
                        mat = 0; uv_r = UV_COV
                elif cos_a > .18:
                    mat = 0; uv_r = UV_COV
                else:
                    mat = 8
            else:
                # Posterior back/rump (z >= 0.16): rich rufous-chestnut lower back/rump & flanks with bronze-olive upper covert transition
                if cos_a > .55 and z < .24:
                    mat = 0; uv_r = UV_COV
                else:
                    mat = 8
            w_f = rnd.uniform(.0055,.0085) if z<-.25 else rnd.uniform(.012,.018)
            s.feather(b,tip,w_f,mat,bone,normal=normal,rows=5,arch=.0015,uv_rect=uv_r)

    # 2. Hypertrophied Folivorous Crop & Breast Plumage (smooth transition from golden-buff bib to deep rufous-chestnut)
    def breast_surface(z,angle,offset):
        cy,rx,ry=body_at(z)
        sy=math.cos(angle)
        crop_bulge=max(0.0,math.sin(max(0.0,min(math.pi,(z+0.28)/0.24*math.pi))))*max(0.0,-sy)*0.014
        return surface_at(z,angle,offset+crop_bulge)
    def crop_project(p,t,layer):
        cy,rx,ry=body_at(p.z)
        angle=math.atan2(p.x,p.y-cy)
        return breast_surface(p.z,angle,.005+.0018*t+layer*.00008)
    for row in range(16):
        z=-.275+row*.024
        for k in range(28):
            angle=math.pi*.40+math.pi*1.20*(k+.48*(row%2))/28
            normal=Vector((math.sin(angle),math.cos(angle),0))
            b=breast_surface(z,angle,.005)
            end_z=min(.14,z+rnd.uniform(.042,.058))
            tip=breast_surface(end_z,angle,.0085)
            if z < -.22: mat = 1
            elif z < -.15: mat = 1 if rnd.random() < (-z - .15)/.07 else 8
            else: mat = 8
            s.feather(b,tip,rnd.uniform(.011,.017),mat,{'Body':.45,'Chest':.55},normal=normal,rows=5,arch=.0016,project=crop_project)

    # 3. Scapular & Shoulder Bridge Coverts (cream-streaked scapulars + buff-tipped bronze coverts)
    for side,n in ((1,'L'),(-1,'R')):
        def shoulder_project(p,t,layer):
            cy,rx,ry=body_at(p.z)
            return surface_at(p.z,math.atan2(p.x,p.y-cy),.006+.0022*t+layer*.00008)
        for row in range(10):
            z=-.17+row*.042
            for k in range(10):
                angle=side*(.52+k*.10+rnd.uniform(-.02,.02))
                b=surface_at(z,angle,.006)
                tip=surface_at(min(.36,z+rnd.uniform(.052,.075)),angle,.009)
                is_streak = (k < 4 or row < 4)
                s.feather(b,tip,rnd.uniform(.013,.019),10 if is_streak else 0,{'Body':.65,'Wing_'+n:.35},
                    normal=(math.sin(angle),math.cos(angle),0),rows=6,arch=.0014,project=shoulder_project,
                    uv_rect=UV_FULL if is_streak else UV_COV)

    # 4. Bare Cobalt-Turquoise Facial Patch, Superciliary Brow, Planar-UV Ruby Eye & Black Eyelashes (Cilia)
    eye_y=.539; eye_z=-.536
    for side in (-1,1):
        grid=[]; count=48
        boundary=[
            (.001,.029),   # Posterior temple (a=0)
            (.012,.021),   # Upper-posterior supercilium
            (.016,-.001),  # Dorsal supraorbital brow ridge
            (.012,-.021),  # Anterior forehead/lore border
            (-.001,-.026), # Lore / bill base junction
            (-.014,-.023), # Rictus / gape corner
            (-.023,.003),  # Suborbital malar cheek
            (-.011,.023)   # Auricular / ear covert border
        ]
        for j in range(8):
            r=.32+j*(1-.32)/7; ring=[]
            for k in range(count+1):
                a=TAU*k/count
                sector=(a/TAU*8)%8; index=int(sector); q=sector-index
                p0=boundary[(index-1)%8]; p1=boundary[index]; p2=boundary[(index+1)%8]; p3=boundary[(index+2)%8]
                def margin(axis):
                    return .5*((2*p1[axis])+(-p0[axis]+p2[axis])*q+(2*p0[axis]-5*p1[axis]+4*p2[axis]-p3[axis])*q*q+(-p0[axis]+3*p1[axis]-3*p2[axis]+p3[axis])*q*q*q)
                outer_y=margin(0); outer_z=margin(1)
                blend=j/7
                y=eye_y+math.sin(a)*.0088*(1-blend)+outer_y*blend
                z=eye_z+math.cos(a)*.0092*(1-blend)+outer_z*blend
                # Sculpted superciliary brow ridge above the eye + palpebral rim cushion
                brow_arch = max(0.0, math.sin(a)) * math.exp(-((blend - 0.45) / 0.28) ** 2) * 0.0022
                rim_cushion = math.exp(-((blend - 0.12) / 0.14) ** 2) * 0.0012
                x=side*(face_x(y,z)+.0020+brow_arch+rim_cushion)
                u=0.50+(z-eye_z)/0.060
                v=0.50+(y-eye_y)/0.050
                ring.append(s.vertex((x,y,z),(u,v),'Head'))
            grid.append(ring)
        for j in range(7):
            for k in range(count):
                q=(grid[j][k],grid[j+1][k],grid[j+1][k+1],grid[j][k+1])
                s.face(tuple(reversed(q)) if side==1 else q,4)

        x_eye = face_x(eye_y, eye_z)
        # Dark scleral/limbal base ring
        s.ellipsoid((side*(x_eye+.0010),eye_y,eye_z),(.0016,.0098,.0102),7,'Head',28,16)

        # Custom Lateral-Facing Planar-UV Ruby Iris Dome (fixes the equirectangular pole-wrap bug!)
        iris_rings = []
        iris_seg = 32
        r_iy, r_iz, r_ix = 0.0091, 0.0094, 0.0024
        apex_x = side * (x_eye + 0.0014 + r_ix)
        apex_idx = s.vertex((apex_x, eye_y, eye_z), (0.50, 0.50), 'Head')
        for j in range(1, 9):
            rj = j / 8.0
            ring = []
            for k in range(iris_seg + 1):
                a = TAU * k / iris_seg
                dy = math.sin(a) * r_iy * rj
                dz = math.cos(a) * r_iz * rj
                dx = r_ix * math.sqrt(max(0.0, 1.0 - rj * rj))
                px = side * (x_eye + 0.0014 + dx)
                py = eye_y + dy
                pz = eye_z + dz
                u_i = 0.50 + (dz / (2.0 * r_iz))
                v_i = 0.50 + (dy / (2.0 * r_iy))
                ring.append(s.vertex((px, py, pz), (u_i, v_i), 'Head'))
            iris_rings.append(ring)
        for k in range(iris_seg):
            tri = (apex_idx, iris_rings[0][k], iris_rings[0][k + 1])
            s.face(tri if side == 1 else tuple(reversed(tri)), 6)
        for j in range(7):
            for k in range(iris_seg):
                q = (iris_rings[j][k], iris_rings[j + 1][k], iris_rings[j + 1][k + 1], iris_rings[j][k + 1])
                s.face(q if side == 1 else tuple(reversed(q)), 6)

        # Glossy corneal/pupil center dome for crisp studio eye catchlight
        s.ellipsoid((side*(x_eye+.0032),eye_y,eye_z),(.0009,.0034,.0035),7,'Head',24,14)

        # Palpebral eyelid folds
        for upper in (True,False):
            pts=[]
            for k in range(19):
                a=(0 if upper else math.pi)+math.pi*k/18
                y=eye_y+math.sin(a)*(.0094 if upper else .0090)
                z=eye_z+math.cos(a)*.0098
                pts.append((side*(face_x(y,z)+.0028),y,z))
            s.tube(pts,[.0013 if upper else .0009]*len(pts),4,'Head',8)

    # Articulated Eyelids + Iconic Black Avian Eyelashes (Cilia) on LidUpper_L/R and LidLower_L/R
    for side,n in ((1,'L'),(-1,'R')):
        for upper,label in ((True,'Upper'),(False,'Lower')):
            bone='Lid'+label+'_'+n; sign=1 if upper else -1; rings=[]
            center=s.vertex((side*(face_x(eye_y,eye_z)+.0038),eye_y,eye_z),(0.5,0.5),bone)
            for j in range(1,9):
                r=j/8;ring=[]
                for k in range(33):
                    a=math.pi*k/32;y=eye_y+sign*math.sin(a)*.0096*r;z=eye_z+math.cos(a)*.0102*r
                    x=side*(face_x(y,z)+.0036+.00015*(1-r*r))
                    ring.append(s.vertex((x,y,z),(0.50+(z-eye_z)/0.054,0.50+(y-eye_y)/0.038),bone))
                rings.append(ring)
            def lid_face(q): s.face(tuple(reversed(q)) if side*sign>0 else q,4)
            for k in range(32): lid_face((center,rings[0][k],rings[0][k+1]))
            for a,b in zip(rings,rings[1:]):
                for k in range(32): lid_face((a[k],b[k],b[k+1],a[k+1]))

            # Prominent black bristle eyelashes (cilia) radiating from the palpebral margin
            n_lashes = 13 if upper else 9
            for l_idx in range(n_lashes):
                la = math.pi * (0.12 + 0.76 * (l_idx / (n_lashes - 1)))
                ly = eye_y + sign * math.sin(la) * 0.0096
                lz = eye_z + math.cos(la) * 0.0102
                lx = side * (face_x(ly, lz) + 0.0032)
                p0 = Vector((lx, ly, lz))
                lash_len = (0.0068 if upper else 0.0042) * (0.75 + 0.35 * math.sin(la))
                out_dir = Vector((side * 0.65, sign * math.sin(la) * 0.85, math.cos(la) * 0.35 + 0.18)).normalized()
                p1 = p0 + out_dir * (lash_len * 0.55) + Vector((0.0, sign * 0.0008, 0.0))
                p2 = p0 + out_dir * lash_len + Vector((0.0, sign * 0.0018, 0.0008))
                s.tube([p0, p1, p2], [0.00038, 0.00024, 0.00006], 7, bone, 5)

    # 5. Sculpted 3D Folivorous Hoatzin Bill & Pervious Nares
    _build_hoatzin_bill_and_nares(s)

    rnd=random.Random(19)
    # 6. Magnificent Radiating Multi-Layered Sagittal Crest of 96 Lanceolate Plumes + Shingled Crown Bed
    crest_rng=random.Random(1919)
    for i in range(96):
        t=i/95.0
        z_base=-.556+t*.102+crest_rng.uniform(-.003,.003); cy,rx,ry=body_at(z_base)
        x_norm=crest_rng.uniform(-0.82,0.82)
        x=x_norm*rx*0.46
        y_rad=ry*math.sqrt(max(0.05,1.0-(x/max(0.01,rx))**2))
        y_base=cy+y_rad-0.0010
        crown_factor=math.sin(t*math.pi)**0.68
        length=0.096+0.088*crown_factor+crest_rng.uniform(-0.008,0.014)
        elev_angle=math.radians(74.0-50.0*t+crest_rng.uniform(-3.0,3.0))
        dy=length*math.sin(elev_angle)
        dz=length*math.cos(elev_angle)
        splay=x*0.62+crest_rng.uniform(-0.004,0.004)
        tip=(splay,y_base+dy,z_base+dz)
        bend=(x*0.18+crest_rng.uniform(-0.003,0.003),crest_rng.uniform(0.014,0.028),crest_rng.uniform(-0.015,0.003))
        width=crest_rng.uniform(0.0068,0.0098)*(0.86+0.36*crown_factor)
        roll=crest_rng.uniform(-0.65,0.65)
        s.crest_feather((x,y_base,z_base),tip,width,bend,roll,500+i)

    # Shingled rufous-and-streaked crown base feathers concealing all crest shaft roots (no bald crown potato!)
    for cr_row in range(12):
        z_cr = -.564 + cr_row * .0115
        cy_cr, rx_cr, ry_cr = body_at(z_cr)
        for k_cr in range(13):
            a_cr = -0.80 + 1.60 * (k_cr + 0.5 * (cr_row % 2)) / 13.0 + crest_rng.uniform(-0.03, 0.03)
            p_cr = Vector((math.sin(a_cr) * rx_cr * 0.86, cy_cr + math.cos(a_cr) * ry_cr + 0.0012, z_cr))
            # Skip strictly if inside bare orbital patch
            if abs(p_cr.x) > 0.008 and -0.568 < z_cr < -0.506 and p_cr.y < 0.556:
                continue
            tip_cr = p_cr + Vector((math.sin(a_cr) * 0.007, 0.012 + 0.006 * math.sin(cr_row / 11.0 * math.pi), 0.024 + crest_rng.uniform(0.0, 0.008)))
            mat_cr = 2 if (abs(a_cr) < 0.52 and cr_row < 9) else 10
            s.feather(p_cr, tip_cr, crest_rng.uniform(0.0068, 0.0098), mat_cr, 'Head', normal=(math.sin(a_cr) * 0.45, 0.88, 0.12), rows=5, arch=0.0016)

    for _ in range(15*8): rnd.random()

    # 7. Complete 5-Tier Ornithological Folded Wing Architecture (ZERO exposed wing ellipsoid!)
    for side,n in ((1,'L'),(-1,'R')):
        # Compact internal structural wing core tucked well inside the outer feather surface
        s.ellipsoid((side*.118,.288,.085),(.028,.052,.115),10,'Wing_'+n,18,10)

        def wing_surf(y_w, z_w, lift=0.002):
            dy = (y_w - 0.295) / 0.088
            dz = (z_w - 0.095) / 0.195
            sh = max(0.02, 1.0 - 0.72 * dy * dy - 0.75 * dz * dz)
            xw = side * (0.126 + 0.054 * math.sqrt(sh) + lift)
            return Vector((xw, y_w, z_w))

        # Tier A: Dorsal & Anterior Marginal / Lesser Wing Coverts (completely roofs the top and upper-middle wing!)
        for row in range(8):
            z_start = -0.100 + row * 0.025
            for j in range(16):
                u_f = (j + 0.5 * (row % 2)) / 16.0
                # Sweep from dorsal-inner wing margin over the crown of the wing down to mid-wing
                y_b = 0.376 - u_f * 0.078 + rnd.uniform(-0.0025, 0.0025)
                z_b = z_start + u_f * 0.068 + rnd.uniform(-0.0035, 0.0035)
                b_pt = wing_surf(y_b, z_b, lift=0.002 + row * 0.0005)
                f_len = rnd.uniform(0.062, 0.084)
                t_pt = wing_surf(y_b - rnd.uniform(0.014, 0.026), z_b + f_len, lift=0.0045 + row * 0.0005)
                n_vec = Vector((side * 0.78, 0.58 - u_f * 0.35, 0.12)).normalized()
                is_scap = (u_f < 0.26 and row < 5)
                s.feather(b_pt, t_pt, rnd.uniform(0.015, 0.020), 10 if is_scap else 0, 'Wing_' + n,
                          normal=n_vec, rows=7, arch=0.0028, uv_rect=UV_FULL if is_scap else UV_COV)

        # Anterior Shoulder / Patagial Wrap Coverts (seals the leading wrist/shoulder edge)
        for row in range(6):
            z_pat = -0.105 + row * 0.016
            for j in range(14):
                ang = -1.35 + 2.70 * (j + 0.5 * (row % 2)) / 14.0
                r_p = math.sqrt(max(0.08, 1.0 - ((z_pat - 0.08) / 0.20) ** 2))
                b_pt = Vector((side * (0.126 + (0.050 * r_p + 0.003) * math.cos(ang)),
                               0.295 + (0.082 * r_p + 0.003) * math.sin(ang), z_pat))
                tip_z = z_pat + rnd.uniform(0.052, 0.072)
                r_t = math.sqrt(max(0.08, 1.0 - ((tip_z - 0.08) / 0.20) ** 2))
                t_pt = Vector((side * (0.126 + (0.054 * r_t + 0.005) * math.cos(ang)),
                               0.295 + (0.085 * r_t + 0.005) * math.sin(ang), tip_z))
                n_vec = Vector((side * math.cos(ang), math.sin(ang), 0.08)).normalized()
                s.feather(b_pt, t_pt, rnd.uniform(0.013, 0.017), 0, 'Wing_' + n,
                          normal=n_vec, rows=6, arch=0.0022, uv_rect=UV_COV)

        # Tier B: Alula (Bastard Wing — 4 sleek thumb feathers along the anterior wrist edge)
        for al in range(4):
            y_al = 0.285 - al * 0.014
            z_al = -0.055 + al * 0.020
            b_al = wing_surf(y_al, z_al, lift=0.006)
            t_al = wing_surf(y_al - 0.018, z_al + 0.078 + al * 0.010, lift=0.009)
            s.feather(b_al, t_al, 0.014, 0, 'Wing_' + n, normal=(side * 0.92, 0.22, 0.10), rows=7, arch=0.0022, uv_rect=UV_COV)

        # Tier C0: Upper-Middle Lesser/Median Covert Shingle (bridges Lesser Coverts into the Upper Wing Bar)
        for j in range(15):
            u_c0 = j / 14.0
            y_c0 = 0.356 - u_c0 * 0.038
            z_c0 = -0.068 + u_c0 * 0.195
            b_c0 = wing_surf(y_c0, z_c0, lift=0.0045)
            t_c0 = wing_surf(y_c0 - 0.022, z_c0 + 0.082 + u_c0 * 0.016, lift=0.0075)
            s.feather(b_c0, t_c0, 0.0175, 0, 'Wing_' + n, normal=(side * 0.84, 0.48, 0.10), rows=8, arch=0.0030, uv_rect=UV_COV)

        # Tier C: Median Secondary Coverts (15 broad imbricated coverts forming the Upper Creamy-Buff Wing Bar)
        for j in range(15):
            u_m = j / 14.0
            y_m = 0.338 - u_m * 0.038
            z_m = -0.055 + u_m * 0.198
            b_m = wing_surf(y_m, z_m, lift=0.0055)
            t_m = wing_surf(y_m - 0.024, z_m + 0.092 + u_m * 0.018, lift=0.0085)
            s.feather(b_m, t_m, 0.018, 0, 'Wing_' + n, normal=(side * 0.88, 0.42, 0.10), rows=8, arch=0.0032, uv_rect=UV_COV)

        # Tier D: Greater Secondary & Primary Coverts (16 longer coverts forming the Lower Creamy-Buff Wing Bar)
        for j in range(16):
            u_g = j / 15.0
            y_g = 0.312 - u_g * 0.042
            z_g = -0.038 + u_g * 0.215
            b_g = wing_surf(y_g, z_g, lift=0.0065)
            t_g = wing_surf(y_g - 0.026, z_g + 0.106 + u_g * 0.022, lift=0.0095)
            s.feather(b_g, t_g, 0.019, 0, 'Wing_' + n, normal=(side * 0.90, 0.32, 0.10), rows=9, arch=0.0034, uv_rect=UV_COV)

        # Tier E: Secondaries & Inner Tertials (draping over the upper-posterior wing and rump edge)
        for j in range(12):
            u_s = j / 11.0
            y_s = 0.348 - u_s * 0.054
            z_s = 0.020 + u_s * 0.198
            b_s = wing_surf(y_s, z_s, lift=0.0045)
            t_s = Vector((side * (0.148 - u_s * 0.022), y_s - 0.032, z_s + 0.148 + (1.0 - abs(u_s - 0.4)) * 0.035))
            s.feather(b_s, t_s, 0.021, 0, {'Wing_' + n: 0.65, 'Flight_' + n: 0.35},
                      normal=(side * 0.78, 0.56, 0.14), rows=10, arch=0.0034, uv_rect=UV_COV if j < 7 else UV_PRI)

        # Tier F: Long Primary Remiges (12 sleek overlapping primaries in glowing Rufous-Chestnut with dark bronzy-sepia tips!)
        for j in range(12):
            u_p = j / 11.0
            b_p = Vector((side * (0.156 + u_p * 0.016), 0.282 - u_p * 0.036, 0.065 + u_p * 0.145))
            t_p = Vector((side * (0.148 + u_p * 0.018), 0.188 - u_p * 0.012, 0.365 + u_p * 0.155))
            s.feather(b_p, t_p, 0.0215, 0, 'Flight_' + n,
                      normal=(side * 0.92, 0.24, 0.12), rows=12, arch=0.0032, uv_rect=UV_PRI)

    # 8. Fine Facial Margin, Forehead, Cheek & Nape Contour Plumage framing the Cobalt Facial Patch
    for row in range(11):
        z=-.568+row*.016
        for k in range(34):
            angle=TAU*(k+.5*(row%2)+rnd.uniform(-.12,.12))/34
            b=surface_at(z,angle,.0016)
            tip=surface_at(z+rnd.uniform(.022,.032),angle,.0032)
            # Clean clearance around the bare cobalt orbital skin patch
            if abs(b.x)>.006 and min(b.z,tip.z)-.003<-.505 and max(b.z,tip.z)+.003>-.570 and min(b.y,tip.y)-.003<.556 and max(b.y,tip.y)+.003>.514: continue
            normal=(math.sin(angle),math.cos(angle),0)
            mat = 1 if math.cos(angle) < -.28 else (2 if (math.cos(angle) > .48 and z < -.48) else 10)
            s.feather(b,tip,rnd.uniform(.0040,.0065),mat,'Head' if z<-.43 else 'Neck',normal=normal,rows=4,arch=.0010)

    # 9. Shingled Rump, Uppertail Coverts & Undertail Coverts (Crissum) concealing the tail base!
    for row in range(5):
        z_utc = 0.21 + row * 0.038
        n_utc = 11 - row
        for k in range(n_utc):
            f_u = (k - (n_utc - 1) * 0.5) / max(1.0, (n_utc - 1) * 0.5)
            x_b = f_u * (0.088 - row * 0.010)
            y_b = 0.318 - row * 0.008 - (f_u * f_u) * 0.018
            b_pt = Vector((x_b, y_b, z_utc))
            t_pt = Vector((x_b * 1.15, y_b - 0.016, z_utc + 0.072 + row * 0.010))
            n_vec = Vector((f_u * 0.35, 0.92, 0.12)).normalized()
            w_utc = 'Body' if row < 2 else {'Body': 0.45, 'Tail0': 0.55}
            mat_utc = 0 if row >= 2 else 8
            s.feather(b_pt, t_pt, 0.020, mat_utc, w_utc, normal=n_vec, rows=6, arch=0.0018,
                      uv_rect=UV_COV if mat_utc == 0 else UV_FULL)

    # Undertail coverts (crissum) clothing the ventral tail base in rich rufous-chestnut
    for row in range(4):
        z_vtc = 0.22 + row * 0.040
        n_vtc = 9 - row
        for k in range(n_vtc):
            f_v = (k - (n_vtc - 1) * 0.5) / max(1.0, (n_vtc - 1) * 0.5)
            x_b = f_v * (0.075 - row * 0.009)
            y_b = 0.232 + row * 0.009 + (f_v * f_v) * 0.012
            b_pt = Vector((x_b, y_b, z_vtc))
            t_pt = Vector((x_b * 1.18, y_b + 0.004, z_vtc + 0.068 + row * 0.008))
            n_vec = Vector((f_v * 0.35, -0.92, 0.12)).normalized()
            w_vtc = 'Body' if row < 2 else {'Body': 0.45, 'Tail0': 0.55}
            s.feather(b_pt, t_pt, 0.019, 8, w_vtc, normal=n_vec, rows=6, arch=0.0015)

    # 10. Broad Graduated Tail Fan of 12 Long Vaulted Rectrices with Crisp Creamy-Buff Terminal Band
    for i in range(12):
        f=(i-5.5)/5.5
        # Central rectrices (abs(f) small) shingle cleanly on top of outer rectrices
        y_stack = 0.292 - abs(f) * 0.012
        b=(f*.034, y_stack, .30)
        tail_len = .85 - (abs(f) ** 1.35) * .11
        tip=(f*.185, .208 - abs(f)*.022, tail_len)
        n_tail = Vector((f * 0.18, 0.98, 0.0)).normalized()
        s.feather(b,tip,.042,3,'Tail',normal=n_tail,rows=12,arch=.0025)

    limbs_mesh(s,limbs,8,5,5,True)

def porcupine(s,m,limbs):
    samples=body_loft(s,[(-.596,.273,.020,.012),(-.577,.270,.030,.021),(-.545,.267,.043,.036),(-.525,.270,.055,.052),(-.49,.274,.072,.067),(-.42,.292,.104,.098),(-.31,.32,.124,.126),(-.18,.33,.174,.168),(.00,.345,.194,.183),(.16,.323,.185,.165),(.29,.278,.139,.129),(.38,.25,.063,.062)],0)
    def body_at(z):
        if z<=samples[0][0]: return samples[0][1:]
        for a,b in zip(samples,samples[1:]):
            if a[0]<=z<=b[0]:
                t=(z-a[0])/(b[0]-a[0]); return tuple(a[i]*(1-t)+b[i]*t for i in range(1,4))
        return samples[-1][1:]
    def face_x(y,z):
        cy,rx,ry=body_at(z)
        return rx*math.sqrt(max(.025,1-((y-cy)/ry)**2))
    # Continuous furry muzzle profile ends in a shallow dark rhinarium. The
    # nostrils are actual openings in the front patch with dark recessed bowls.
    rows=[]; nx=40; ny=22
    for j in range(ny+1):
        row=[]
        for k in range(nx+1):
            x=-.027+.054*k/nx; y=.261+.031*j/ny
            rr=(x/.027)**2+((y-.277)/.0155)**2
            if rr>1:
                scale=1/math.sqrt(rr); x*=scale; y=.277+(y-.277)*scale; rr=1
            z=-.604+.009*rr
            row.append(s.vertex((x,y,z),(k/nx,j/ny),'Head'))
        rows.append(row)
    def allowed(x,y):
        if (x/.027)**2+((y-.277)/.0155)**2>1: return False
        return all(((x-side*.012)/.0048)**2+((y-.280)/.0033)**2>1 for side in (-1,1))
    for j in range(ny):
        for k in range(nx):
            x=-.027+.054*(k+.5)/nx; y=.261+.031*(j+.5)/ny
            if allowed(x,y): s.face((rows[j][k],rows[j+1][k],rows[j+1][k+1],rows[j][k+1]),4)
    for side in (-1,1):
        s.ellipsoid((side*.012,.280,-.597),(.0054,.0038,.002),4,'Head',16,10)
        pts=[]
        for k in range(25):
            a=TAU*k/24; x=side*.012+math.cos(a)*.0048; y=.280+math.sin(a)*.0033
            rr=(x/.027)**2+((y-.277)/.0155)**2
            pts.append((x,y,-.604+.009*rr+.00025))
        s.tube(pts,[.00055]*len(pts),4,'Head',5)
    # Closed upper/lower lip conceals fixed upper incisors at rest. Feeding
    # lowers Jaw/lower lip, exposing the tooth tips without an idle toy grin.
    s.tube([(-.023,.253,-.577),(0,.252,-.584),(.023,.253,-.577)],[.004,.006,.004],0,'Jaw',12)
    s.ellipsoid((0,.237,-.548),(.030,.014,.032),0,'Jaw',24,12)
    for side in (-1,1): s.ellipsoid((side*.005,.257,-.579),(.004,.008,.0025),5,'Head',12,8)
    # Small inset dark eyes, with upper and lower lids conforming to skull.
    eye_y=.316; eye_z=-.435
    for side in (-1,1):
        x=face_x(eye_y,eye_z)
        s.ellipsoid((side*(x-.001),eye_y,eye_z),(.0024,.0074,.008),6,'Head',24,14)
        s.ellipsoid((side*(x+.0007),eye_y,eye_z-.0003),(.001,.0045,.0048),3,'Head',24,12)
        for upper in (True,False):
            pts=[]
            for k in range(17):
                a=(0 if upper else math.pi)+math.pi*k/16
                y=eye_y+math.sin(a)*(.0088 if upper else .0081); z=eye_z+math.cos(a)*.0093
                pts.append((side*(face_x(y,z)+.001),y,z))
            s.tube(pts,[.0017 if upper else .0011]*len(pts),0,'Head',7)
    # Pinnae are closed thin cupped surfaces: recessed inner bowl, folded rim
    # and a furred back. The rim is not a flat projecting disk.
    for side in (-1,1):
        grids=[]
        for front in (True,False):
            grid=[]
            for j in range(8):
                r=j/7; ring=[]
                for k in range(33):
                    a=TAU*k/32
                    x=side*(.112+.015*r*r-(0 if front else .003))
                    y=.356+math.cos(a)*.026*r; z=-.367+math.sin(a)*.021*r+.006*r*math.cos(a)
                    ring.append(s.vertex((x,y,z),(k/32,r),'Head'))
                grid.append(ring)
            grids.append(grid)
            for j in range(7):
                for k in range(32):
                    q=(grid[j][k],grid[j+1][k],grid[j+1][k+1],grid[j][k+1])
                    s.face(q if (side==1)==front else tuple(reversed(q)),2 if front else 0)
        rim=[]
        for k in range(33):
            a=TAU*k/32; rim.append((side*.127,.356+math.cos(a)*.026,-.367+math.sin(a)*.021+.006*math.cos(a)))
            if k<32: s.face((grids[0][-1][k],grids[1][-1][k],grids[1][-1][k+1],grids[0][-1][k+1]),2)
        s.tube(rim,[.002]*len(rim),0,'Head',7)
        s.ellipsoid((side*.121,.343,-.383),(.004,.008,.005),0,'Head',16,10)
    # Sparse short face undercoat breaks smooth silhouette without replacing
    # or moving the existing quills. Eye aperture and nose remain unobscured.
    fur_rng=random.Random(919)
    for j in range(170):
        z=fur_rng.uniform(-.566,-.34); a=fur_rng.uniform(-math.pi,math.pi)
        cy,rx,ry=body_at(z); x=math.sin(a)*rx; y=cy+math.cos(a)*ry
        if abs(abs(x)-face_x(eye_y,eye_z))<.014 and abs(y-eye_y)<.016 and abs(z-eye_z)<.02: continue
        if z<-.53 and y>.263: continue
        n=Vector((math.sin(a),math.cos(a),0)); p=Vector((x,y,z))+n*.0005
        end=p+Vector((n.x*.002,n.y*.002,fur_rng.uniform(.005,.010)))
        s.tube([p,p.lerp(end,.5)+n*.001,end],[.0003,.0002,.000025],0,'Head',4)
    # Continuous prehensile tail through seven deform joints. Distal grasping
    # surface is naked and bears no quills, per Smithsonian zoo reference.
    points=[(0,.25,.34),(0,.23,.45),(0,.21,.57),(0,.21,.69),(0,.25,.80),(0,.33,.85),(0,.40,.81),(0,.42,.73)]
    pts=[]; rr=[]; ww=[]
    for j in range(7):
        for k in range(4):
            t=k/4; pts.append(Vector(points[j]).lerp(Vector(points[j+1]),t)); rr.append(.055*(1-(j+t)/8)**1.3+.004)
            w={'Tail'+str(j+1):1}
            if j<6 and t>.5: w={'Tail'+str(j+1):1-(t-.5),'Tail'+str(j+2):t-.5}
            ww.append(w)
    pts.append(points[-1]); rr.append(.009); ww.append('Tail7')
    s.tube(pts,rr,2,ww,20)
    limbs_mesh(s,limbs,0,2,4,False)
    rnd=random.Random(83)
    # Quills root on the real dorsal/body surface; normals sweep rearwards.
    # Dense staggered rows avoid the detached radial sea-urchin silhouette.
    for j in range(38):
        z=-.32+j*.018; t=(z+.32)/.67
        for k in range(31):
            angle=-1.72+k*3.44/30+rnd.uniform(-.03,.03)
            pz=z+rnd.uniform(-.005,.005); cy,rx,ry=body_at(pz)
            normal=Vector((math.sin(angle)*.63,math.cos(angle)*.76,.53)).normalized()
            p=Vector((math.sin(angle)*rx,cy+math.cos(angle)*ry,pz))-normal*.002
            length=rnd.uniform(.065,.135)*(1-.25*abs(math.sin(angle)))
            end=p+normal*length
            head=max(0,min(1,(-pz-.25)/.12)); weights={'Body':1-head,'Head':head}
            s.tube([p,p.lerp(end,.62),end],[.0035,.0021,.00025],1,weights,5)
    # Short forehead quills/fur follow crown and cheek contours, leaving eyes
    # and nose unobscured. Whiskers are fine curved mesh, never shader curves.
    for j in range(180):
        a=rnd.uniform(-1.65,1.65); z=rnd.uniform(-.46,-.31); cy,rx,ry=body_at(z)
        p=Vector((math.sin(a)*rx,cy+math.cos(a)*ry,z))-Vector((math.sin(a),math.cos(a),0))*.002
        end=p+Vector((math.sin(a)*.017,math.cos(a)*.016,.04))
        s.tube([p,p.lerp(end,.65),end],[.0018,.001,.0002],1,'Head',5)
    for side in (-1,1):
        for j in range(9):
            b=Vector((side*.034,.267,-.562+j*.002)); e=b+Vector((side*(.07+j*.003),.008*(j-4),-.018))
            s.tube([b,b.lerp(e,.45)+Vector((0,.006,0)),e],[.0007,.0005,.0001],5,'Head',5)

def aim_bone(rig,name,h,t):
    pb=rig.pose.bones[name]; rest=pb.bone
    q=(rest.tail_local-rest.head_local).rotation_difference(Vector(t)-Vector(h))
    pb.matrix=Matrix.Translation(Vector(h)) @ q.to_matrix().to_4x4() @ rest.matrix_local.to_3x3().to_4x4()

def solve_limb(rig,limb,phase,walking,stride,feeding=False):
    names,h,k,a,toe,side=limb; upper,lower,foot=names
    # 65% supported stance followed by a lifted return. This is a authored
    # treadmill stride: engine root translation supplies travel distance.
    p=phase%1; target=a.copy()
    if feeding and upper.startswith('Front'):
        target=Vector((side*.10,.20+.005*math.sin(phase*TAU*3),-.38))
    if walking:
        if p<.65: target.z+=stride*(p/.65-.5); target.y=a.y
        else:
            q=(p-.65)/.35; target.z+=stride*(.5-q); target.y+=.034*math.sin(q*math.pi)
    l1=(k-h).length; l2=(a-k).length; delta=target-h; dist=min(delta.length,l1+l2-.00001); direction=delta.normalized()
    target=h+direction*dist
    along=(l1*l1-l2*l2+dist*dist)/(2*dist); height=math.sqrt(max(0,l1*l1-along*along))
    pole=k-h; pole=(pole-direction*pole.dot(direction)).normalized(); knee=h+direction*along+pole*height
    aim_bone(rig,upper,h,knee); bpy.context.view_layer.update()
    aim_bone(rig,lower,knee,target); bpy.context.view_layer.update()
    aim_bone(rig,foot,target,target+(toe-a)); bpy.context.view_layer.update()

def animations(rig,limbs,species):
    from mathutils import Quaternion
    rig.animation_data_create()
    def ease(x): return max(0,min(1,x))**2*(3-2*max(0,min(1,x)))
    def look(t):
        stops=[(0,0),(.13,0),(.22,-.13),(.38,-.13),(.47,.085),(.66,.085),(.78,0),(1,0)]
        for a,b in zip(stops,stops[1:]):
            if a[0]<=t<=b[0]: return a[1]+(b[1]-a[1])*ease((t-a[0])/(b[0]-a[0]))
        return 0
    for name in ('idle','walk','feed','rest'):
        act=bpy.data.actions.new(name);act.use_fake_user=True;rig.animation_data.action=act
        end=25 if name=='walk' else 97
        for frame in range(1,end+1):
            phase=(frame-1)/(end-1);wave=math.sin(phase*TAU)
            for pb in rig.pose.bones: pb.location=(0,0,0);pb.rotation_quaternion=(1,0,0,0);pb.scale=(1,1,1)
            bpy.context.view_layer.update()
            def rotate(bone,axis,angle):
                pb=rig.pose.bones[bone];local=pb.bone.matrix_local.to_3x3().inverted()@Vector(axis);pb.rotation_quaternion=Quaternion(local,angle)
            rotate('Chest',(1,0,0),.009*math.sin(phase*TAU*2))
            rotate('Head',(0,1,0),look(phase) if name=='idle' else .018*wave)
            rotate('Neck',(0,1,0),.30*look(phase) if name=='idle' else .008*wave)
            if name=='walk':
                rotate('Body',(0,0,1),.021*math.sin(phase*TAU))
                rotate('Chest',(1,0,0),.018*math.sin(phase*TAU*2))
                rotate('Head',(1,0,0),-.025*math.sin(phase*TAU*2))
            if name=='feed':
                reach=math.sin(math.pi*ease((phase-.12)/.76))**2 if .12<phase<.88 else 0
                rotate('Chest',(1,0,0),-.09*reach)
                rotate('Neck',(1,0,0),-.20*reach)
                rotate('Head',(1,0,0),.09*reach)
                bite=max(0,math.sin((phase-.32)*TAU*5))**3*reach
                rotate('Jaw',(1,0,0),.105*bite)
            if name=='rest':
                rotate('Neck',(1,0,0),-.15)
                rotate('Head',(1,0,0),.15+.005*wave)
                rotate('Chest',(1,0,0),.005*wave)

            # Artist-authored blink envelope, not measured species timing.
            closure=1 if name=='rest' else max(0,1-abs(phase-.42)/.025) if name=='idle' else 0
            closure=ease(closure)
            for n,side in (('L',1),('R',-1)):
                rotate('LidUpper_'+n,(0,0,1),-side*1.22*(1-closure))
                rotate('LidLower_'+n,(0,0,1),side*1.22*(1-closure))
            rotate('Crest',(1,0,0),-.02*look((phase-.05)%1)+.012*wave)
            for j in range(4):
                rotate('Tail'+str(j),(0,1,0),.012*math.sin(TAU*phase-j*.38))
                pb=rig.pose.bones['Tail'+str(j)];local=pb.bone.matrix_local.to_3x3().inverted()@Vector((1,0,0));pb.rotation_quaternion=pb.rotation_quaternion@Quaternion(local,.004*math.sin(TAU*phase-j*.38))
            for n,side in (('L',1),('R',-1)):
                rotate('Wing_'+n,(0,0,1),side*(.008*wave if name!='walk' else .013*math.sin(TAU*phase)))
            for i,limb in enumerate(limbs): solve_limb(rig,limb,phase+i*.5,name=='walk',.13)
            for pb in rig.pose.bones:
                for channel in ('location','rotation_quaternion','scale'):pb.keyframe_insert(channel,frame=frame)
        track=rig.animation_data.nla_tracks.new();track.name=name
        strip=track.strips.new(name,1,act);strip.action_frame_start=1;strip.action_frame_end=end;track.mute=True
    rig.animation_data.action=None
    for pb in rig.pose.bones:pb.location=(0,0,0);pb.rotation_quaternion=(1,0,0,0);pb.scale=(1,1,1)
    bpy.context.scene.frame_set(1)


def audit(path,species,ob,rig):
    raw=path.read_bytes(); length=struct.unpack_from('<I',raw,12)[0]; data=json.loads(raw[20:20+length])
    primitives=[p for m in data.get('meshes',[]) for p in m['primitives']]
    report={'species':species,'axis_contract':'Authored Y-up in Blender; standard glTF conversion; runtime +90X retained',
        'blender_vertices':len(ob.data.vertices),'blender_triangles':sum(len(p.vertices)-2 for p in ob.data.polygons),
        'glb_vertices':sum(data['accessors'][p['attributes']['POSITION']]['count'] for p in primitives),
        'glb_triangles':sum(data['accessors'][p['indices']]['count']//3 for p in primitives),
        'joints':[len(s['joints']) for s in data.get('skins',[])],
        'clips':[a.get('name') for a in data.get('animations',[])],
        'images':len(data.get('images',[])),'materials':len(data.get('materials',[])),
        'bytes':len(raw),'uv_primitives':sum('TEXCOORD_0' in p['attributes'] for p in primitives),
        'primitive_count':len(primitives),'extensions_used':data.get('extensionsUsed',[])}
    report['nominal_walk_speed_m_s']=.20 if species=='crested_canopy' else .12
    report['walk_cycle_seconds']=1.0
    report['walk_stance_fraction']=.65
    (REPORT/(species+'-audit.json')).write_text(json.dumps(report,indent=2)); print(json.dumps(report))

def studio(species,rig,ob):
    sc=bpy.context.scene; sc.render.engine='CYCLES'; sc.cycles.samples=12
    sc.render.threads_mode='FIXED'; sc.render.threads=4
    sc.render.resolution_x=600; sc.render.resolution_y=510; sc.render.resolution_percentage=100
    sc.world=bpy.data.worlds.new('StudioWorld'); sc.world.use_nodes=True
    sc.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.22,.22,.22,1)
    sc.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.45
    sc.view_settings.view_transform='AgX'
    # Floor is +Y plane. Render apparatus is added AFTER export.
    bpy.ops.mesh.primitive_plane_add(size=200,location=(0,.009,0),rotation=(math.pi/2,0,0))
    floor=bpy.context.object; floor.name='ReviewFloor'; floor.data.materials.append(material('StudioFloor',(.16,.17,.18),.8))
    for name,pos,power,size in [('Key',(-2,3,-2),450,3),('Fill',(2,1.4,-1),200,2),('Rim',(1,2,3),550,2)]:
        data=bpy.data.lights.new(name,'AREA'); data.energy=power; data.shape='DISK'; data.size=size
        light=bpy.data.objects.new(name,data); bpy.context.collection.objects.link(light); light.location=pos
        light.rotation_euler=(Vector((0,.3,0))-light.location).to_track_quat('-Z','Y').to_euler()
    camdata=bpy.data.cameras.new('ReviewCamera'); cam=bpy.data.objects.new('ReviewCamera',camdata); bpy.context.collection.objects.link(cam); sc.camera=cam
    camdata.type='ORTHO'; camdata.ortho_scale=1.60 if species=='crested_canopy' else 1.65
    target=Vector((0,.36,.08)); views={'front':(0,.65,-3),'side':(3,.55,.08),'threequarter':(2,.95,-2.3)}
    mats=list(ob.data.materials); clay=material('ReviewNeutralClay',(.43,.43,.43),.75)
    def orient_camera():
        direction=(target-cam.location).normalized(); right=direction.cross(Vector((0,1,0))).normalized()
        up=right.cross(direction).normalized()
        cam.rotation_euler=Matrix((right,up,-direction)).transposed().to_euler()
    for label,pos in views.items():
        cam.location=pos; direction=target-cam.location
        # Preserve anatomical vertical +Y in the authored scene.
        orient_camera()
        for i in range(len(mats)): ob.data.materials[i]=clay
        sc.render.filepath=str(REPORT/(species+'-clay-'+label+'.png')); bpy.ops.render.render(write_still=True)
    for i,m in enumerate(mats): ob.data.materials[i]=m
    sc.render.filepath=str(REPORT/(species+'-surface-threequarter.png')); bpy.ops.render.render(write_still=True)
    if '--views-only' in sys.argv: return
    cam.location=(3,.62,.04); orient_camera()
    for name in ('idle','walk','feed','rest'):
        rig.animation_data.action=bpy.data.actions[name]
        frames=(1,4,7,10) if species=='crested_canopy' and name=='walk' else ((1,5,10,14) if name=='walk' else (1,13,25,37))
        for f in frames if name=='walk' else (1,25):
            sc.frame_set(f); sc.render.filepath=str(REPORT/(species+'-'+name+'-'+str(f)+'.png')); bpy.ops.render.render(write_still=True)
    rig.animation_data.action=None; sc.frame_set(1)

def generate(species):
    if species != 'crested_canopy': raise ValueError('Hoatzin-only authoring module')
    bpy.ops.wm.read_factory_settings(use_empty=True); REPORT.mkdir(parents=True,exist_ok=True)
    out=ROOT/'game/assets/guardians'/species; out.mkdir(parents=True,exist_ok=True)
    avian=species=='crested_canopy'; texdir=out/'textures'; texdir.mkdir(exist_ok=True)
    from hoatzin_textures import generate_all_textures
    generate_all_textures(texdir)

    def load_tex(fname, non_color=False):
        p = texdir / fname
        im = bpy.data.images.load(str(p.resolve()), check_existing=False)
        im.name = p.stem
        if non_color:
            im.colorspace_settings.name = 'Non-Color'
        im.pack()
        return im

    img_flight = load_tex('feather-0.png')
    img_buff = load_tex('feather-1.png')
    img_crest = load_tex('feather-2.png')
    img_tail = load_tex('feather-3.png')
    img_skin = load_tex('orbital-skin.png')
    img_skin_normal = load_tex('orbital-normal.png', non_color=True)
    img_streak = load_tex('streaked-contour.png')
    img_dark_down = load_tex('underplumage.png')
    img_dark_down_normal = load_tex('underplumage-normal.png', non_color=True)
    img_buff_down = load_tex('breast-underplumage.png')
    img_surface_normal = load_tex('surface-normal.png', non_color=True)
    img_horn = load_tex('horn-bill.png')
    img_horn_normal = load_tex('horn-normal.png', non_color=True)
    img_iris = load_tex('red-iris.png')

    mats = [
        material('FlightChestnut', (.545, .185, .052), .68, img_flight, img_surface_normal, .45, spec=.16),
        material('BuffCrop', (.835, .565, .255), .72, img_buff, img_surface_normal, .35, spec=.16),
        material('RufousCrest', (.875, .485, .085), .62, img_crest, img_surface_normal, .45, spec=.18),
        material('BuffStreak', (.910, .859, .749), .68, img_tail, img_surface_normal, .40, spec=.16),
        material('BlueOrbitalSkin', (.085, .520, .800), .42, img_skin, img_skin_normal, .55, spec=.42),
        material('HornBillAndScutes', (.125, .150, .175), .38, img_horn, img_horn_normal, .50, spec=.45),
        material('RedIris', (.82, .05, .05), .04, img_iris, None, 0, spec=.85),
        material('WetPupilAndMouth', (.005, .005, .006), .02, spec=.90),
        material('DarkUnderplumage', (.545, .155, .035), .72, img_dark_down, img_dark_down_normal, .38, spec=.15),
        material('BuffUnderplumage', (.835, .565, .255), .74, img_buff_down, img_dark_down_normal, .32, spec=.15),
        material('StreakedContour', (.065, .055, .036), .68, img_streak, img_surface_normal, .45, spec=.16)
    ]
    rig,limbs=rig_for(species); s=Surface()
    bird(s,mats,limbs) if avian else porcupine(s,mats,limbs)
    ob=s.object('HoatzinAnatomy' if avian else 'PorcupineAnatomy',mats,rig)
    animations(rig,limbs,species)
    bpy.context.scene.render.fps=24; bpy.context.scene.frame_start=1; bpy.context.scene.frame_end=49
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); rig.select_set(True); bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.gltf(filepath=str(out/'guardian.glb'),export_format='GLB',use_selection=True,export_animations=True,export_animation_mode='ACTIONS',export_skins=True,export_apply=False,export_yup=True,export_cameras=False,export_lights=False)
    audit(out/'guardian.glb',species,ob,rig)
    bpy.ops.wm.save_as_mainfile(filepath=str(REPORT/(species+'-authoring.blend')))
    import os
    if '--render' in sys.argv and os.environ.get('GUARDIAN_SKIP_EVIDENCE') != '1':
        studio(species,rig,ob)
    print('CANOPY_AUTHORING_COMPLETE '+species)
