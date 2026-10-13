"""Coati-only surface/face pass. Solid skinned fibers; no hair-engine dependency.

Small tapered crossed ribbons are ordinary glTF triangles with opaque core PBR.
Every fiber copies its root's interpolated anatomical skin weights and colors.
The deterministic stochastic coat has no sinusoidal repeating wood grain.
"""
import bpy,math,random,json
from mathutils import Vector

def coarse_color(c,q):
    belly=math.cos(q)<-.42
    if c.z<-.77: return (.035,.022,.014,1)
    if c.z<-.61:
        return (.31,.28,.22,1) if math.cos(q)<.30 else (.09,.048,.021,1)
    if c.z<-.37:
        # Separate pale above/below-eye markings and dark orbital strip rather
        # than one broad white forehead sheet.
        lateral=abs(math.sin(q))
        up=math.cos(q)
        light=((.48<up<.80 or -.20<up<.17) and lateral>.65 and abs(c.z+.499)<.060) or (belly and lateral>.40)
        return (.32,.29,.23,1) if light else (.085,.043,.018,1)
    return (.24,.18,.12,1) if belly else (.14,.084,.046,1)

def coati_images(out):
    import numpy as np
    size=1024; rng=np.random.default_rng(37291)
    # Nonperiodic correlated strands of short irregular lengths. Each row uses
    # a different noise seed; there is no frequency that repeats across islands.
    noise=rng.random((size,size)).astype('float32')-.5
    directional=noise.copy()
    for offset in range(1,7): directional+=np.roll(noise,offset,axis=0)*(1-offset/8)
    directional/=3.7
    soft=noise.copy()
    for off in [1,2,4]: soft=(soft+np.roll(soft,off,axis=0)+np.roll(soft,off,axis=1))/3
    h=directional*.30+soft*.25
    value=.88+directional*.09+soft*.13
    rgb=np.stack((value,value*.95,value*.87),axis=-1)
    rgba=np.concatenate((rgb,np.ones((size,size,1))),axis=-1).astype('float32')
    color=bpy.data.images.new('coati_stochastic_albedo',width=size,height=size)
    color.pixels.foreach_set(rgba.ravel()); color.filepath_raw=str(out/'surface_albedo.png'); color.file_format='PNG'; color.save(); color.pack()
    gy,gx=np.gradient(h); n=np.stack((-gx,-gy,np.ones_like(h)),axis=-1); n/=np.linalg.norm(n,axis=-1)[...,None]
    rgba=np.concatenate((n*.5+.5,np.ones((size,size,1))),axis=-1).astype('float32')
    normal=bpy.data.images.new('coati_stochastic_normal',width=size,height=size); normal.colorspace_settings.name='Non-Color'
    normal.pixels.foreach_set(rgba.ravel()); normal.filepath_raw=str(out/'surface_normal.png'); normal.file_format='PNG'; normal.save(); normal.pack()
    # Glabrous nasal pad gets a separate tiny pore image, exported as a normal.
    size=256; raw=rng.random((size,size)); h=raw.copy()
    for dy,dx in [(1,0),(-1,0),(0,1),(0,-1)]: h+=np.roll(raw,(dy,dx),(0,1))
    h/=5; gy,gx=np.gradient(h)
    n=np.stack((-gx*2,-gy*2,np.ones_like(h)),axis=-1); n/=np.linalg.norm(n,axis=-1)[...,None]
    rgba=np.concatenate((n*.5+.5,np.ones((size,size,1))),axis=-1).astype('float32')
    nose=bpy.data.images.new('coati_nose_pores',width=size,height=size); nose.colorspace_settings.name='Non-Color'; nose.pixels.foreach_set(rgba.ravel())
    nose.filepath_raw=str(out/'nose_normal.png'); nose.file_format='PNG'; nose.save(); nose.pack()
    return color,normal,nose

def nose_material(noseimage):
    m=bpy.data.materials.new('CoatiWetNasalPad'); m.use_nodes=True
    p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=(.018,.011,.008,1); p.inputs['Roughness'].default_value=.37
    tex=m.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=noseimage
    nm=m.node_tree.nodes.new('ShaderNodeNormalMap'); nm.inputs['Strength'].default_value=.20
    m.node_tree.links.new(tex.outputs['Color'],nm.inputs['Color']); m.node_tree.links.new(nm.outputs[0],p.inputs['Normal'])
    return m

def cup_ears(Builder,rig,mat):
    ears=[]
    for side in [-1,1]:
        b=Builder('coati_cupped_ear_'+str(side),rig,mat)
        cx=side*.087; cy=.530; cz=-.385; rx=.039; ry=.034; seg=32
        # Thin concave front skin and a rounded rear surface share their rim.
        for face in [0,1]:
            for r in [.02,.30,.62,1.0]:
                for j in range(seg):
                    a=math.tau*j/seg
                    z=cz-.014*r*r if face==0 else cz+.010*(1-r*r)-.012*r*r
                    x=cx+side*rx*r*math.cos(a)+side*.006*r*math.sin(a)
                    y=cy+ry*r*math.sin(a)
                    b.v.append((x,y,z)); b.uv.append((.5+.48*r*math.cos(a),.5+.48*r*math.sin(a))); b.w.append({'Head':1-r*.8,('Ear_L' if side>0 else 'Ear_R'):r*.8})
                    inner=(.19,.122,.075,1) if face==0 else (.11,.052,.021,1)
                    rim=(.12,.060,.027,1)
                    b.col.append(tuple(inner[k]*(1-r)+rim[k]*r for k in range(4)))
        for face in [0,1]:
            start=face*4*seg
            for ring in range(3):
                for j in range(seg):
                    a=start+ring*seg+j; an=start+ring*seg+(j+1)%seg; c=a+seg; cn=an+seg
                    b.f.append((a,an,cn,c) if face==0 else (a,c,cn,an))
            b.f.append(tuple(start+j for j in range(seg)))
        for j in range(seg):
            a=3*seg+j; an=3*seg+(j+1)%seg; c=7*seg+j; cn=7*seg+(j+1)%seg
            b.f.append((a,c,cn,an))
        ears.append(b.build())
    return ears

def lids_and_muzzle(Builder,rig,mat,mats,body):
    for side in [-1,1]:
        # Annular skin bridge joins the actual recessed socket to a small almond
        # aperture. A line around a protruding sphere was still toy-like.
        lid=Builder('coati_eyelids_'+str(side),rig,mat)
        seg=48
        for ring in range(5):
            t=ring/4
            for j in range(seg):
                a=math.tau*j/seg
                outer_y=.470+math.sin(a)*.025; outer_z=-.499+math.cos(a)*.040
                hit,loc,normal,face=body.ray_cast(Vector((side*.25,outer_y,outer_z)),Vector((-side,0,0)),distance=1)
                outer_x=abs(loc.x)+.0003 if hit else .087
                y=outer_y*(1-t)+(.470+math.sin(a)*.0075)*t
                z=outer_z*(1-t)+(-.499+math.cos(a)*.0128)*t
                x=side*(outer_x*(1-t)+(.0907+.0006*abs(math.sin(a)))*t+.0002*math.sin(math.pi*t))
                lid.v.append((x,y,z)); lid.uv.append((j/seg,t)); lid.w.append({'Head':1})
                if hit:
                    indices=list(body.data.polygons[face].vertices); attr=body.data.color_attributes['Color']
                    outer=tuple(sum(attr.data[i].color[k] for i in indices)/len(indices) for k in range(3))
                else: outer=(.085,.043,.018)
                inner=(.075,.037,.014); dark=math.sqrt(t)
                lid.col.append(tuple(outer[k]*(1-dark)+inner[k]*dark for k in range(3))+(1,))
        for ring in range(4):
            for j in range(seg):
                a=ring*seg+j; an=ring*seg+(j+1)%seg
                lid.f.append((a,an,an+seg,a+seg))
        lid.build()
    nose=Builder('coati_nasal_pad',rig,mats['nose'])
    profile=[(-.799,.373,.015,.010),(-.806,.374,.025,.017),(-.818,.374,.028,.020),(-.830,.375,.025,.017),(-.839,.375,.016,.011),(-.843,.375,.003,.002)]
    nodes=[]
    for i in range(len(profile)-1):
        for j in range(4):
            t=j/4; z,y,rx,ry=[profile[i][k]*(1-t)+profile[i+1][k]*t for k in range(4)]
            nodes.append(((0,y,z),rx,ry,{'Snout':1}))
    z,y,rx,ry=profile[-1]; nodes.append(((0,y,z),rx,ry,{'Snout':1}))
    nose.ringmesh(nodes,40); nose.build()

def fiber_material():
    m=bpy.data.materials.new('CoatiFineFur'); m.use_nodes=True; m.use_backface_culling=False
    p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=(1,1,1,1); p.inputs['Roughness'].default_value=.91
    c=m.node_tree.nodes.new('ShaderNodeVertexColor'); c.layer_name='Color'; m.node_tree.links.new(c.outputs['Color'],p.inputs['Base Color'])
    return m

def add_fiber(b,root,direction,length,width,weights,color):
    axis=direction.normalized(); ref=Vector((0,1,0)) if abs(axis.y)<.9 else Vector((1,0,0))
    a=axis.cross(ref).normalized(); other=axis.cross(a).normalized()
    # Two crossed opaque ribbons give a stable silhouette from all azimuths.
    for lateral in [a,other]:
        start=len(b.v)
        locations=[root-lateral*width,root+lateral*width,root+axis*(length*.55)-lateral*(width*.48),root+axis*(length*.55)+lateral*(width*.48),root+axis*length]
        for j,p in enumerate(locations):
            b.v.append(tuple(p)); b.uv.append(((j%2),[0,0,.55,.55,1][j])); b.w.append(weights)
            tint=.80 if j<2 else (1 if j<4 else 1.10)
            b.col.append(tuple(min(1,color[k]*tint) for k in range(3))+(1,))
        b.f.extend([(start,start+1,start+3,start+2),(start+2,start+3,start+4)])

def surface_fur(Builder,rig,body,extra_surfaces,task):
    import bisect
    rng=random.Random(710291); mat=fiber_material(); b=Builder('coati_fine_skinned_fur',rig,mat)
    triangles=[]; cumulative=[]; total=0
    for ob in [body]+extra_surfaces:
        me=ob.data; me.calc_loop_triangles(); cols=me.color_attributes.get('Color')
        names={g.index:g.name for g in ob.vertex_groups}
        for tri in me.loop_triangles:
            indices=tuple(tri.vertices); vs=[me.vertices[i] for i in indices]
            center=sum((v.co for v in vs),Vector())/3
            if center.y<.095 or center.z<-.705: continue
            if abs(center.x)>.055 and abs(center.z+.499)<.026 and abs(center.y-.470)<.018: continue
            # Inner ear cup stays mostly glabrous; fringe is included later.
            if 'ear' in ob.name and center.z>-.393: continue
            area=tri.area
            if area<1e-9: continue
            total+=area; cumulative.append(total)
            data=[]
            for v in vs:
                data.append((v.co.copy(),v.normal.copy(),{names[g.group]:g.weight for g in v.groups},tuple(cols.data[v.index].color) if cols else (.14,.07,.025,1)))
            triangles.append((ob.name,data))
    count=36000
    lengths=[]; zones={}; sample_roots=[]
    for i in range(count):
        idx=bisect.bisect_left(cumulative,rng.random()*total); name,vertices=triangles[idx]
        u=math.sqrt(rng.random()); v=rng.random(); bary=[1-u,u*(1-v),u*v]
        p=sum((vertices[j][0]*bary[j] for j in range(3)),Vector())
        n=sum((vertices[j][1]*bary[j] for j in range(3)),Vector()).normalized()
        color=[sum(vertices[j][3][k]*bary[j] for j in range(3)) for k in range(4)]
        weights={}
        for j in range(3):
            for key,value in vertices[j][2].items(): weights[key]=weights.get(key,0)+value*bary[j]
        ws=sorted(weights.items(),key=lambda p:-p[1])[:4]; norm=sum(w for _,w in ws); weights={key:w/norm for key,w in ws}
        if p.y>.57 and p.z>.22:
            zone='tail'; flow=Vector((0,1,-.12)); length=rng.uniform(.006,.014)
        elif p.z<-.36:
            zone='face'; flow=Vector((0,-.20,1)); length=rng.uniform(.004,.009)
        elif p.y<.27:
            zone='limbs'; flow=Vector((0,-1,.35)); length=rng.uniform(.004,.009)
        else:
            zone='torso'; flow=Vector((0,-.15,1)); length=rng.uniform(.009,.018)
        tangent=flow-n*flow.dot(n)
        if tangent.length<.001: tangent=n.cross(Vector((1,0,0)))
        tangent.normalize(); direction=(n*.30+tangent*.95).normalized()
        direction=(direction+Vector((rng.uniform(-.08,.08),rng.uniform(-.08,.08),rng.uniform(-.08,.08)))).normalized()
        root=p-n*.00015
        variation=rng.uniform(.88,1.08)
        # A scattering of darker guard hairs creates nonuniform real coat color.
        if rng.random()<.12: variation*=.78
        color=[c*variation for c in color[:3]]+[1]
        add_fiber(b,root,direction,length,rng.uniform(.00020,.00038),weights,color)
        lengths.append(length); zones[zone]=zones.get(zone,0)+1
        if i<12: sample_roots.append({'position':list(p),'weights':weights})
    # Fine mobile vibrissae, rooted in the muzzle and kept above its lower lip.
    for side in [-1,1]:
        for i in range(8):
            root=Vector((side*(.040+.003*(i%2)),.382+.004*(i//2),-.69+.014*(i%4)))
            direction=Vector((side*1,.20,.32+(i%3)*.13)).normalized()
            add_fiber(b,root,direction,.038+.004*i,.00015,{'Snout':.75,'Head':.25},(.29,.26,.20,1))
    fur=b.build(); fur['solid_tapered_fiber_count']=count+16; fur['root_weight_contract']='interpolated source skin weights; max4'
    report={'fiber_count':count+16,'fiber_vertices':len(b.v),'fiber_triangles':sum(len(f)-2 for f in b.f),'fur_length_m':[min(lengths),max(lengths)],'zones':zones,'material':'CoatiFineFur','alpha_mode':'OPAQUE','same_skin_as_body':True,'sample_root_weights':sample_roots,'limitations':['Fine opaque fibers are a bounded silhouette approximation, not a strand groom or photographed coat.','No new run/trot or terrain-adaptive IK is authored.']}
    (task/'coati'/'fur-audit.json').write_text(json.dumps(report,indent=2))
    return fur










def coat_roughness(mat,out):
    import numpy as np
    size=512;rng=np.random.default_rng(81733);n=rng.random((size,size))
    for off in [1,2,4,8]:n=(n+np.roll(n,off,axis=0)+np.roll(n,off,axis=1))/3
    value=np.clip(.80+.14*n,.80,.94)
    rgba=np.stack((value,value,value,np.ones_like(value)),axis=-1).astype('float32')
    im=bpy.data.images.new('CoatiCoatRoughness',width=size,height=size);im.colorspace_settings.name='Non-Color';im.pixels.foreach_set(rgba.ravel())
    im.filepath_raw=str(out/'coat-roughness.png');im.file_format='PNG';im.save();im.pack()
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage');tex.image=im
    separate=mat.node_tree.nodes.new('ShaderNodeSeparateColor')
    mat.node_tree.links.new(tex.outputs['Color'],separate.inputs['Color']);mat.node_tree.links.new(separate.outputs['Red'],mat.node_tree.nodes.get('Principled BSDF').inputs['Roughness'])
