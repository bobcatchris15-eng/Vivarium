"""Shallow anatomical dermal scutes; ordinary skinned triangles in glTF."""
import bpy,math,random,bisect
from mathutils import Vector

def scutes(Builder,rig,body,mat):
    rng=random.Random(49613)
    mesh=body.data;mesh.calc_loop_triangles()
    triangles=[];areas=[];area=0
    for triangle in mesh.loop_triangles:
        if triangle.area<=1e-10:continue
        triangles.append(triangle);area+=triangle.area;areas.append(area)
    coat=bpy.data.materials.new('MonitorDermalScutes');coat.use_nodes=True
    shader=coat.node_tree.nodes.get('Principled BSDF');shader.inputs['Base Color'].default_value=(.73,.75,.65,1);shader.inputs['Roughness'].default_value=.74
    b=Builder('monitor_shallow_dermal_scutes',rig,coat)
    colors=mesh.color_attributes['Color'];names={group.index:group.name for group in body.vertex_groups}
    for count in range(14000):
        tri=triangles[bisect.bisect_left(areas,rng.random()*area)]
        r=math.sqrt(rng.random());t=rng.random();bary=[1-r,r*(1-t),r*t]
        verts=[mesh.vertices[index] for index in tri.vertices]
        p=sum((vertex.co*weight for vertex,weight in zip(verts,bary)),Vector())
        n=sum((vertex.normal*weight for vertex,weight in zip(verts,bary)),Vector()).normalized()
        # Leave orbital and auditory apertures unobstructed, and the soles soft.
        if p.y<.085:continue
        if abs(p.x)>.062 and ((p.z+1.006)/.050)**2+((p.y-.333)/.030)**2<1:continue
        if abs(p.x)>.075 and ((p.z+.88)/.025)**2+((p.y-.300)/.025)**2<1:continue
        ws={};color=[0,0,0,0]
        for vertex,weight in zip(verts,bary):
            for group in vertex.groups:ws[names[group.group]]=ws.get(names[group.group],0)+group.weight*weight
            for k in range(4):color[k]+=colors.data[vertex.index].color[k]*weight
        ws=dict(sorted(ws.items(),key=lambda item:-item[1])[:4]);total=sum(ws.values());ws={k:v/total for k,v in ws.items()}
        flow=Vector((0,0,1));flow-=n*flow.dot(n)
        if flow.length<.01:flow=Vector((1,0,0));flow-=n*flow.dot(n)
        flow.normalize();across=flow.cross(n).normalized()
        size=rng.uniform(.0020,.0041)*( .8 if p.z<-.8 else 1)
        start=len(b.v);variation=rng.uniform(.97,1.03)
        for ring in range(2):
            radius=1 if ring==0 else .58;height=.00018 if ring==0 else rng.uniform(.00035,.00052)
            for j in range(6):
                angle=math.tau*j/6
                q=p+n*height+(across*math.cos(angle)+flow*math.sin(angle)*1.3)*size*radius
                b.v.append(tuple(q));b.w.append(ws);b.col.append(tuple(color[k]*variation for k in range(3))+(1,));b.uv.append((.5+.49*radius*math.cos(angle),.5+.49*radius*math.sin(angle)))
        for j in range(6):b.f.append((start+j,start+(j+1)%6,start+6+(j+1)%6,start+6+j))
        b.f.append(tuple(start+6+j for j in range(6)))
    return b.build()
