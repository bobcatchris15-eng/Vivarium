"""Deterministic canopy guardian authoring. Authored +Y up/-Z forward.

All geometry and textures are real exported mesh/PBR data. No Blender-only
procedural shading, automatic bone proximity weights, or shared polish pass.
Run either species entry point with Blender 5.2 --background --python.
"""
import bpy, math, random, json, struct, sys
from pathlib import Path
from mathutils import Vector, Matrix

ROOT=Path(__file__).resolve().parents[2]
REPORT=ROOT/'.clanker/tasks/guardian-hoatzin-solo/v8-authoring'
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
    def feather(self,base,tip,width,mat,bone,normal=(0,1,0),rows=8,arch=.001):
        # Thin closed vane with irregular asymmetric margins and a true tapered
        # tip. Raised rounded camber made v1 read as overlapping scales.
        rows=min(rows,12) # Spend geometry on coverage rather than over-tessellated vanes.
        b=Vector(base); d=Vector(tip)-b; n=Vector(normal).normalized()
        cross=d.cross(n).normalized(); n=cross.cross(d).normalized(); grid=[]
        rng=random.Random(int(abs(b.x*91823+b.y*37121+b.z*19477)*10))
        asymmetry=rng.uniform(.76,.95); phase=rng.uniform(0,TAU)
        profile=[(0,.10),(.12,.45),(.28,.82),(.48,1),(.72,.99),(.88,.87),(.97,.48),(1,.15)]
        def vane_width(t):
            for a,c in zip(profile,profile[1:]):
                if a[0]<=t<=c[0]:
                    q=(t-a[0])/(c[0]-a[0]); return a[1]*(1-q)+c[1]*q
            return .012
        for side in (1,-1):
            layer=[]
            for j in range(rows+1):
                t=j/rows
                center=b+d*t+n*math.sin(t*math.pi)*(width*.023+arch)+cross*math.sin(t*math.pi)*math.sin(phase)*width*.11
                w=width*vane_width(t)
                row=[]
                for k in range(3):
                    u=k/2; x=(u-.5)*2
                    edge=(.95+.05*math.sin(j*2.17+phase)) if abs(x)>.9 else 1
                    blade=x*w*edge*(asymmetry if x<0 else 1)
                    ridge=(1-abs(x))*width*.009
                    thickness=min(.000075,width*.004)*(1-.8*t)
                    row.append(self.vertex(center+cross*blade+n*(ridge+side*thickness),(u,t),feather_weights(bone,max(0,min(1,(b.z+d.z*t-.31)/.51)))))
                layer.append(row)
            grid.append(layer)
            for j in range(rows):
                for k in range(2):
                    q=(layer[j][k],layer[j][k+1],layer[j+1][k+1],layer[j+1][k])
                    self.face(q if side==1 else tuple(reversed(q)),mat)
        for j in range(rows):
            for k in (0,2): self.face((grid[0][j][k],grid[1][j][k],grid[1][j+1][k],grid[0][j+1][k]),mat)
        for j in (0,rows):
            for k in range(2): self.face((grid[0][j][k],grid[0][j][k+1],grid[1][j][k+1],grid[1][j][k]),mat)
        if rows>=6:
            shaft=[]; radii=[]
            for j in range(6):
                t=j/5
                shaft.append(b+d*t+n*(width*.013+math.sin(t*math.pi)*(width*.023+arch))+cross*math.sin(t*math.pi)*math.sin(phase)*width*.11)
                radii.append(max(.000025,width*.012*(1-t)))
            self.tube(shaft,radii,mat,[feather_weights(bone,max(0,min(1,(b.z+d.z*j/5-.31)/.51))) for j in range(6)],5)
    def crest_feather(self,base,tip,width,bend,roll,seed):
        # Curved rachis with attached narrow vanes and individual edge barbs.
        # This is exported mesh, with roots blended into Head and tips Crest.
        b=Vector(base); d=Vector(tip)-b; bend=Vector(bend)
        rng=random.Random(seed)
        axis=Vector((math.cos(roll),0,math.sin(roll)))
        def center(t): return b+d*t+bend*(4*t*(1-t))+Vector((.002*math.sin(t*math.pi*1.3+roll)*t,0,0))
        def basis(t):
            tangent=(center(min(1,t+.005))-center(max(0,t-.005))).normalized()
            cross=(axis-tangent*axis.dot(tangent)).normalized()
            return cross,tangent.cross(cross).normalized()
        def shape(t):
            return math.sin(math.pi*max(0,(t-.09)/.91))**.85 if t>.09 else .04
        def weight(t):
            c=min(1,t/.18);return {'Head':1-c,'Crest':c}
        shaft=[center(j/14) for j in range(15)]
        self.tube(shaft,[.00040*(1-j/14)**1.25+.000035 for j in range(15)],2,[weight(j/14) for j in range(15)],5)
        layers=[]
        for side in (1,-1):
            rows=[]
            for j in range(15):
                t=j/14; cross,n=basis(t); w=width*shape(t)*.55; row=[]
                for k in range(3):
                    u=k/2; asym=.86 if k==0 else 1
                    pt=center(t)+cross*((u-.5)*2*w*asym)+n*(side*.000035+.00007*(1-abs((u-.5)*2)))
                    row.append(self.vertex(pt,(u,t),weight(t)))
                rows.append(row)
            layers.append(rows)
            for j in range(14):
                for k in range(2):
                    q=(rows[j][k],rows[j][k+1],rows[j+1][k+1],rows[j+1][k])
                    self.face(q if side==1 else tuple(reversed(q)),2)
        for j in range(14):
            for k in (0,2): self.face((layers[0][j][k],layers[1][j][k],layers[1][j+1][k],layers[0][j+1][k]),2)
        # Narrow irregular ribbons attach to rachis and extend beyond the vane
        # margin. Small staggered gaps soften the silhouette at game scale.
        for j in range(42):
            t=.14+j*.020+rng.uniform(-.008,.008)
            for side in (-1,1):
                end_t=min(.986,t+rng.uniform(.035,.064)); cross,n=basis(t)
                start=center(t)+n*.00012
                end=center(end_t)+cross*(side*width*shape(t)*rng.uniform(1.05,1.48))+n*.00016
                delta=end-start; edge=delta.cross(n).normalized(); half=rng.uniform(.00014,.00032)*(1-.55*t)
                q=[self.vertex(pt,uv,weight(t if index<2 else end_t)) for index,(pt,uv) in enumerate([(start-edge*half,(.48,t)),(start+edge*half,(.52,t)),(end+edge*half*.16,(.52,end_t)),(end-edge*half*.16,(.48,end_t))])]
                self.face(q,2);self.face(tuple(reversed(q)),2)

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
                    edge=max(math.exp(-((u-.03)/.026)**2),math.exp(-((u-.97)/.026)**2),math.exp(-((t-.96)/.032)**2))*.32
                    rgb=[v*(1-edge)+c*edge for v,c in zip(rgb,(.39,.33,.22))]
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

def material(name,color,rough=.65,image=None,normal=None,normal_strength=.30):
    m=bpy.data.materials.new(name); m.use_nodes=True; p=m.node_tree.nodes.get('Principled BSDF')
    p.inputs['Base Color'].default_value=(*color,1); p.inputs['Roughness'].default_value=rough
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
        for k in range(seg): surface.face((rows[j][k],rows[j+1][k],rows[j+1][k+1],rows[j][k+1]),mat)
    surface.face(rows[0][:-1],mat); surface.face(tuple(reversed(rows[-1][:-1])),mat)
    return samples

def eyes(s,x,y,z,r,iris,black,skin,avian):
    for side in (-1,1):
        # Orbital socket, iris dome, black pupil: smooth wet-eye geometry.
        s.ellipsoid((side*x,y,z),(r*.42,r*1.17,r*1.1),skin,'Head',24,12)
        s.ellipsoid((side*(x+r*.24),y,z),(r*.38,r,r),iris,'Head',24,12)
        s.ellipsoid((side*(x+r*.54),y,z-.002),(r*.10,r*.54,r*.55),black,'Head',20,10)

def limbs_mesh(s,limbs,bodymat,skinmat,clawmat,avian):
    for names,h,k,a,toe,side in limbs:
        upper,lower,foot=names
        s.tube([h,h.lerp(k,.5),k],[.038 if avian else .057,.031 if avian else .049,.022 if avian else .033],bodymat,[upper,upper,{upper:.5,lower:.5}],16)
        s.tube([k,k.lerp(a,.45),a],[.022 if avian else .033,.014 if avian else .025,.012 if avian else .022],skinmat,[lower,lower,{lower:.8,foot:.2}],14)
        s.ellipsoid(k,(.023 if avian else .036,.024 if avian else .034,.025 if avian else .037),bodymat,{upper:.5,lower:.5},18,10)
        s.ellipsoid(toe.lerp(a,.48),(.027 if avian else .046,.015,.033),skinmat,foot,20,10)
        count=3 if avian else 4
        for d in range(count):
            x=(d-(count-1)/2)*(.021 if avian else .024)
            base=Vector((a.x+x*.3,.026,a.z-.015)); mid=Vector((a.x+x,.021,a.z-.050)); end=Vector((a.x+x*1.3,.020,a.z-.082+abs(x)*.4))
            s.tube([base,mid,end],[.010,.008,.005],skinmat,foot,10)
            s.tube([end,end+Vector((0,.008,-.012)),end+Vector((0,-.001,-.025))],[.005,.004,.0006],clawmat,foot,8)
        if avian:
            b=Vector((a.x,.027,a.z)); e=b+Vector((-side*.014,-.003,.07))
            s.tube([b,b.lerp(e,.55),e],[.010,.008,.0045],skinmat,foot,10)
            s.tube([e,e+Vector((0,.006,.012)),e+Vector((0,-.001,.024))],[.005,.004,.0005],clawmat,foot,8)
            # Tarsal scutes modelled as narrow overlapping bands.
            for j in range(7):
                p=k.lerp(a,.30+j*.085); s.ellipsoid(p,(.015,.004,.015),clawmat,lower,12,6)

def bird(s,m,limbs):
    profile=[(-.613,.525,.017,.014),(-.590,.526,.025,.022),(-.545,.533,.043,.041),(-.485,.523,.049,.044),(-.430,.489,.047,.041),(-.39,.455,.046,.053),(-.30,.37,.055,.067),(-.19,.30,.10,.117),(-.07,.275,.145,.143),(.10,.285,.153,.141),(.25,.285,.13,.113),(.36,.28,.05,.052)]
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
    # Distinct buff crop and lower breast remain visible beneath coverts.
    s.ellipsoid((0,.248,-.09),(.126,.106,.165),9,{'Body':.45,'Chest':.55},32,18)
    rnd=random.Random(713)
    # Short rounded contour feathers follow the sampled outer body rather than
    # a constant radius. Endpoints and arch sit outside the convex loft.
    def surface_at(z,a,offset=.001):
        cy,rx,ry=body_at(z); return Vector((math.sin(a)*rx,cy+math.cos(a)*ry,z))+Vector((math.sin(a),math.cos(a),0))*offset
    for row in range(23):
        z=-.41+row*.032
        cy,rx,ry=body_at(z)
        count=30 if z<-.25 else 32
        for k in range(count):
            angle=TAU*(k+rnd.uniform(-.20,.20)+(row%2)*.5)/count
            normal=Vector((math.sin(angle),math.cos(angle),0)); b=surface_at(z,angle,.002)
            if z>-.19 and b.y>.25 and abs(b.x)>.095: continue
            length=rnd.uniform(.042,.058) if z>-.25 else rnd.uniform(.026,.038)
            tip=surface_at(min(.36,z+length),angle+rnd.uniform(-.045,.045),.004)
            bone='Neck' if z<-.26 else ('Chest' if z<-.08 else 'Body')
            mat=1 if b.y<.265 else (10 if rnd.random()<.45 else 0)
            s.feather(b,tip,rnd.uniform(.0045,.007) if z<-.25 else rnd.uniform(.010,.016),mat,bone,normal=normal,rows=4,arch=.0012)
    # Breast feathers follow the outer union of crop and torso. A crop-only
    # ellipse sinks into the wider torso near its rear and exposes jagged tips.
    def breast_surface(z,angle,offset):
        cy,rx,ry=body_at(z); sx=math.sin(angle); sy=math.cos(angle)
        a=sx*sx/(rx*rx)+sy*sy/(ry*ry)
        b=2*sy*(.248-cy)/(ry*ry); c=(.248-cy)**2/(ry*ry)-1
        torso=max(0,(-b+math.sqrt(max(0,b*b-4*a*c)))/(2*a))
        r=math.sqrt(max(0,1-((z+.09)/.165)**2))
        crop=r/math.sqrt(sx*sx/(.126*.126)+sy*sy/(.106*.106))
        radius=max(torso,crop)+offset
        return Vector((sx*radius,.248+sy*radius,z))
    for row in range(12):
        z=-.244+row*.027
        for k in range(26):
            angle=math.pi*.42+math.pi*1.16*(k+.45*(row%2))/26
            normal=Vector((math.sin(angle),math.cos(angle),0))
            b=breast_surface(z,angle,.005)
            end_z=min(.10,z+rnd.uniform(.039,.056))
            tip=breast_surface(end_z,angle,.008)
            s.feather(b,tip,rnd.uniform(.010,.016),1,{'Body':.45,'Chest':.55},normal=normal,rows=4,arch=.0015)
    # Bare orbital skin is a conforming 0.35mm overlay on the actual skull,
    # with an eye opening. It has no separate ellipsoid/disc volume.
    eye_y=.540; eye_z=-.542
    for side in (-1,1):
        grid=[]; count=48
        for j in range(7):
            r=.34+j*(1-.34)/6; ring=[]
            for k in range(count+1):
                a=TAU*k/count
                # Anatomical exposed face boundary, extending toward bill/cere;
                # inner contour is an independent eye aperture, not scaled disc.
                boundary=[(.003,.025),(.013,.012),(.015,-.015),(.011,-.043),(-.003,-.061),(-.014,-.040),(-.020,-.012),(-.014,.017)]
                sector=(a/TAU*8)%8; index=int(sector); q=sector-index
                p0=boundary[(index-1)%8]; p1=boundary[index]; p2=boundary[(index+1)%8]; p3=boundary[(index+2)%8]
                def margin(axis):
                    return .5*((2*p1[axis])+(-p0[axis]+p2[axis])*q+(2*p0[axis]-5*p1[axis]+4*p2[axis]-p3[axis])*q*q+(-p0[axis]+3*p1[axis]-3*p2[axis]+p3[axis])*q*q*q)
                outer_y=margin(0); outer_z=margin(1)
                blend=j/6
                y=eye_y+math.sin(a)*.008*(1-blend)+outer_y*blend
                z=eye_z+math.cos(a)*.0086*(1-blend)+outer_z*blend
                x=side*(face_x(y,z)+.0018)
                ring.append(s.vertex((x,y,z),((z+.61)/.10,(y-.505)/.060),'Head'))
            grid.append(ring)
        for j in range(6):
            for k in range(count):
                q=(grid[j][k],grid[j+1][k],grid[j+1][k+1],grid[j][k+1])
                s.face(tuple(reversed(q)) if side==1 else q,4)
        x=face_x(eye_y,eye_z)
        # The iris/pupil sit only 1-2mm above the skull, under the lids.
        s.ellipsoid((side*(x+.0008),eye_y,eye_z),(.0013,.0066,.0070),6,'Head',28,16)
        s.ellipsoid((side*(x+.0019),eye_y,eye_z-.0004),(.0005,.0034,.0035),7,'Head',24,14)
        for upper in (True,False):
            pts=[]
            for k in range(17):
                a=(0 if upper else math.pi)+math.pi*k/16
                y=eye_y+math.sin(a)*(.008 if upper else .0076)
                z=eye_z+math.cos(a)*.0086
                pts.append((side*(face_x(y,z)+.0022),y,z))
            s.tube(pts,[.00115 if upper else .0008]*len(pts),4,'Head',8)
    # Short, flattened bill: broad at attachment, tapering to a restrained hook.
    s.tube([(0,.529,-.575),(0,.525,-.601),(0,.519,-.628),(0,.512,-.653)],[(.026,.013),(.023,.011),(.016,.007),(.0028,.0025)],5,'Head',28)
    s.tube([(0,.512,-.579),(0,.511,-.603),(0,.509,-.636),(0,.509,-.648)],[(.023,.0045),(.020,.004),(.010,.0024),(.002,.0012)],5,'Jaw',24)
    s.tube([(0,.516,-.579),(0,.515,-.610),(0,.510,-.646)],[(.021,.0008),(.017,.0008),(.002,.0004)],7,'Head',16)
    for side in (-1,1): s.ellipsoid((side*.017,.534,-.603),(.0018,.0017,.0045),7,'Head',16,10)
    rnd=random.Random(19)
    # Splayed layered crest: varied curved shafts, narrow vanes and fine barbs.
    crest_rng=random.Random(1919)
    for i in range(21):
        t=i/20; x=crest_rng.uniform(-.024,.024)
        z=-.531+t*.068+crest_rng.uniform(-.006,.006); cy,rx,ry=body_at(z)
        y=cy+ry*math.sqrt(max(.1,1-(x/rx)**2))-.0006
        height=.074+.047*math.sin(t*math.pi)+crest_rng.uniform(-.015,.011)
        spread=x*1.5+crest_rng.uniform(-.022,.022)
        backsweep=.047+.052*t+crest_rng.uniform(-.011,.018)
        tip=(spread,y+height,z+backsweep)
        bend=(crest_rng.uniform(-.014,.014),crest_rng.uniform(.002,.011),crest_rng.uniform(-.021,-.007))
        s.crest_feather((x,y,z),tip,crest_rng.uniform(.0028,.0048),bend,crest_rng.uniform(-1.3,1.3),401+i)
    # Preserve exact legacy RNG state for all non-crest geometry below.
    for _ in range(15*8): rnd.random()
    for side,n in ((1,'L'),(-1,'R')):
        # Shoulder coverts, overlapping scapulars, secondaries and long primaries.
        s.ellipsoid((side*.133,.30,.10),(.064,.10,.20),8,'Wing_'+n,24,16)
        for row in range(6):
            count=(13,13,12,12,11,10)[row]
            for j in range(count):
                z=-.069+j*.027+row*.009+rnd.uniform(-.007,.007)
                y=.375-row*.025+rnd.uniform(-.004,.004)
                shape=max(.03,1-((y-.30)/.10)**2-((z-.10)/.20)**2)
                x=side*(.133+.064*math.sqrt(shape)+.0015)
                length=rnd.uniform(.082,.128); width=rnd.uniform(.013,.018)
                rnd.random(); rnd.random(); mat=0
                s.feather((x,y,z),(x+side*.033,y-rnd.uniform(.017,.032),z+length),width,mat,'Wing_'+n,normal=(side,.25,0),rows=10)
        # Leading shoulder contour coverts cover the exposed front cap.
        for row in range(6):
            z=-.099+row*.014
            r=math.sqrt(max(0,1-((z-.10)/.20)**2))
            for j in range(16):
                angle=-1.45+2.9*(j+.5*(row%2))/16
                tip_z=z+rnd.uniform(.044,.068)
                tr=math.sqrt(max(0,1-((tip_z-.10)/.20)**2))
                b=(side*(.133+(.064*r+.004)*math.cos(angle)),.30+(.10*r+.004)*math.sin(angle),z)
                tip=(side*(.133+(.064*tr+.006)*math.cos(angle)),.30+(.10*tr+.006)*math.sin(angle),tip_z)
                s.feather(b,tip,rnd.uniform(.010,.014),0,'Wing_'+n,normal=(side*math.cos(angle),math.sin(angle),0),rows=6,arch=.001)
        for rz,z in enumerate((-.084,-.052,-.018)):
            for j in range(6):
                y=.365-j*.021
                shape=1-((y-.30)/.10)**2-((z-.10)/.20)**2
                if shape<=.03: continue
                x=side*(.133+.064*math.sqrt(shape)+.003)
                s.feather((x,y,z),(side*.210,y-.015,z+rnd.uniform(.069,.097)),rnd.uniform(.011,.015),0,'Wing_'+n,normal=(side,.2,0),rows=5,arch=.002)
        for j in range(14):
            z=-.069+j*.024+rnd.uniform(-.006,.006)
            s.feather((side*.151,.387,z),(side*.170,.358,z+rnd.uniform(.14,.21)),rnd.uniform(.012,.017),0,'Wing_'+n,normal=(side*.7,.7,0),rows=10)
        for j in range(14):
            b=(side*(.165+j*.002),.30-j*.004,.105+j*.011)
            tip=(side*(.172+j*.003),.179+j*.002,.46+j*.011+rnd.uniform(-.018,.018))
            s.feather(b,tip,rnd.uniform(.017,.022),0,'Flight_'+n,normal=(side,.2,0),rows=14)
    # Fine forehead/nape contour plumage breaks the smooth head silhouette,
    # leaving the anatomical exposed facial patch unobstructed.
    for row in range(9):
        z=-.57+row*.020
        for k in range(32):
            angle=TAU*(k+.5*(row%2)+rnd.uniform(-.15,.15))/32
            b=surface_at(z,angle,.0015)
            tip=surface_at(z+rnd.uniform(.021,.031),angle,.003)
            # Reserve a conservative exposed-face clearance envelope for roots,
            # tips and vane widths, not only the root center. Matched Head
            # weighting plus 1.8mm skin offset avoids native depth contention.
            if abs(b.x)>.006 and min(b.z,tip.z)-.006<-.505 and max(b.z,tip.z)+.006>-.620 and min(b.y,tip.y)-.006<.567 and max(b.y,tip.y)+.006>.506: continue
            normal=(math.sin(angle),math.cos(angle),0)
            s.feather(b,tip,rnd.uniform(.0035,.0055),10 if rnd.random()<.28 else 0,'Head' if z<-.43 else 'Neck',normal=normal,rows=4,arch=.0008)
    for i in range(11):
        f=(i-5)/5; b=(f*.035,.286,.31); tip=(f*.14,.21-abs(f)*.01,.82-abs(f)*.08)
        s.feather(b,tip,.026,0,'Tail',normal=(0,1,0),rows=12)
        # Physical pale terminal band; mapped feather material gives barbs.
        s.feather(Vector(b).lerp(Vector(tip),.87),tip,.024,3,'Tail',normal=(0,1,0),rows=3)
    limbs_mesh(s,limbs,0,5,5,True)

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
    normal=texture('MicroSurface',texdir/'surface-normal.png','feather_normal' if avian else 'fur_normal',(.5,.5,1))
    if avian:
        colors=[(.12,.104,.085),(.30,.215,.115),(.27,.115,.033),(.48,.392,.255)]
        imgs=[texture('Feather'+str(i),texdir/('feather-'+str(i)+'.png'),'feather',c) for i,c in enumerate(colors)]
        down_normal=texture('UnderplumageNormal',texdir/'underplumage-normal.png','down_normal',(.5,.5,1))
        dark_down=texture('DarkUnderplumage',texdir/'underplumage.png','down',(.073,.061,.051))
        buff_down=texture('BuffUnderplumage',texdir/'breast-underplumage.png','down',(.255,.176,.09))
        streak=texture('StreakedContour',texdir/'streaked-contour.png','streak',colors[0])
        skin=texture('OrbitalSkin',texdir/'orbital-skin.png','skin',(.065,.265,.34))
        skin_normal=texture('OrbitalPores',texdir/'orbital-normal.png','skin_normal',(.5,.5,1))
        mats=[material('FlightChestnut',colors[0],.73,imgs[0],normal,.22),material('BuffCrop',colors[1],.88,imgs[1],normal,.22),material('RufousCrest',colors[2],.83,imgs[2],normal,.22),material('BuffStreak',colors[3],.86,imgs[3],normal,.22),material('BlueOrbitalSkin',(.020,.145,.222),.74,skin,skin_normal,.18),material('HornBillAndScutes',(.085,.071,.050),.49),material('RedIris',(.32,.035,.018),.14),material('WetPupilAndMouth',(.004,.006,.005),.085),material('DarkUnderplumage',(.073,.061,.051),.92,dark_down,down_normal,.13),material('BuffUnderplumage',(.255,.176,.09),.92,buff_down,down_normal,.13),material('StreakedContour',colors[0],.86,streak,normal,.18)]
    else:
        fur=texture('Underfur',texdir/'underfur.png','fur',(.14,.10,.063)); quill=texture('BandedQuill',texdir/'quill.png','quill',(.7,.7,.5))
        mats=[material('DenseUnderfur',(.14,.10,.063),.88,fur,normal),material('BandedQuills',(.7,.6,.3),.42,quill),material('NoseEarsAndGripSkin',(.13,.090,.063),.65,normal=normal,normal_strength=.18),material('WetEyePupil',(.005,.006,.006),.075),material('ClawsNostrilsAndMouth',(.015,.012,.009),.46),material('WhiskersAndIncisors',(.68,.57,.37),.46),material('WetChestnutIris',(.025,.018,.012),.10)]
    rig,limbs=rig_for(species); s=Surface()
    bird(s,mats,limbs) if avian else porcupine(s,mats,limbs)
    ob=s.object('HoatzinAnatomy' if avian else 'PorcupineAnatomy',mats,rig)
    animations(rig,limbs,species)
    bpy.context.scene.render.fps=24; bpy.context.scene.frame_start=1; bpy.context.scene.frame_end=49
    bpy.ops.object.select_all(action='DESELECT'); ob.select_set(True); rig.select_set(True); bpy.context.view_layer.objects.active=rig
    bpy.ops.export_scene.gltf(filepath=str(out/'guardian.glb'),export_format='GLB',use_selection=True,export_animations=True,export_animation_mode='ACTIONS',export_skins=True,export_apply=False,export_yup=True,export_cameras=False,export_lights=False)
    audit(out/'guardian.glb',species,ob,rig)
    bpy.ops.wm.save_as_mainfile(filepath=str(REPORT/(species+'-authoring.blend')))
    if '--render' in sys.argv: studio(species,rig,ob)
    print('CANOPY_AUTHORING_COMPLETE '+species)
