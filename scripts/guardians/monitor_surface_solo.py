"""Portable monitor scale maps and inset orbital/aural soft tissue."""
import bpy,math
from mathutils import Vector
def images(out):
    import numpy as np
    size=2048; u,v=np.meshgrid(np.arange(size)/size,np.arange(size)/size); rng=np.random.default_rng(902)
    # Staggered scales vary in width and center; fine texture is independent of pigmentation.
    x=u*150+np.floor(v*180)%2*.5+.12*np.sin(v*94); y=v*180+.10*np.sin(u*65)
    dx=x%1-.5; dy=y%1-.5
    h=np.maximum(0,1-4*dx*dx-4*dy*dy)**.55
    cell=rng.random((181,152)); variation=cell[np.minimum(180,y.astype(int)),np.minimum(151,x.astype(int))]
    value=.70+.20*h+.08*variation
    rgb=np.stack((value*.90,value*.92,value*.80),axis=-1)
    def save(name,data,noncolor=False):
        im=bpy.data.images.new('monitor_'+name,width=size,height=size)
        if noncolor: im.colorspace_settings.name='Non-Color'
        im.pixels.foreach_set(np.concatenate((data,np.ones((size,size,1))),axis=-1).astype('float32').ravel())
        im.filepath_raw=str(out/(name+'.png')); im.file_format='PNG'; im.save(); im.pack(); return im
    color=save('surface_albedo',rgb)
    gy,gx=np.gradient(h); n=np.stack((-gx*1.6,-gy*1.6,np.ones_like(h)),axis=-1); n/=np.linalg.norm(n,axis=-1)[...,None]
    normal=save('surface_normal',n*.5+.5,True)
    rough=.49+.15*(1-h)+.045*variation
    rough=save('surface_roughness',np.stack((rough,rough,rough),axis=-1),True)
    return color,normal,rough
def roughness(mat,image):
    for node in mat.node_tree.nodes:
        if node.type=='NORMAL_MAP':node.inputs['Strength'].default_value=.25
    tex=mat.node_tree.nodes.new('ShaderNodeTexImage'); tex.image=image
    mat.node_tree.links.new(tex.outputs['Color'],mat.node_tree.nodes['Principled BSDF'].inputs['Roughness'])
def face(Builder,rig,body,mat,mats):
    # Open rings bridge actual surface to an almond aperture; no solid socket balls.
    for s in [-1,1]:
        b=Builder('monitor_eyelid_'+str(s),rig,mat); seg=48
        for r in range(5):
            t=r/4
            for j in range(seg):
                a=math.tau*j/seg; oy=.326+math.sin(a)*.019; oz=-1.006+math.cos(a)*.030
                hit,p,n,idx=body.ray_cast(Vector((s*.3,oy,oz)),Vector((-s,0,0)),distance=1)
                ox=abs(p.x)+.0003 if hit else .108
                y=oy*(1-t)+(.326+math.sin(a)*.0085)*t; z=oz*(1-t)+(-1.006+math.cos(a)*.014)*t
                # Fit every ring to the original skull ellipse, rather than
                # stretching a high fixed aperture above the sloping brow.
                profile=[(-1.09,.295,.085,.047),(-1.0,.307,.105,.060),(-.91,.303,.109,.069)]
                for left,right in zip(profile,profile[1:]):
                    if left[0]<=z<=right[0]:
                        q=(z-left[0])/(right[0]-left[0]);cy=left[1]*(1-q)+right[1]*q;rx=left[2]*(1-q)+right[2]*q;ry=left[3]*(1-q)+right[3]*q;break
                fitted=rx*math.sqrt(max(.01,1-((y-cy)/ry)**2))
                x=s*(fitted-.004*t+.0003)
                b.v.append((x,y,z)); b.uv.append((j/seg,t)); b.w.append({'Head':1})
                b.col.append((.095*(1-t)+.028*t,.10*(1-t)+.025*t,.060*(1-t)+.013*t,1))
        for r in range(4):
            for j in range(seg):
                a=r*seg+j; q=r*seg+(j+1)%seg; b.f.append((a,q,q+seg,a+seg))
        b.build()
        # Small recessed auditory cup with a scale-colored outer rim and dark floor.
        e=Builder('monitor_aural_cup_'+str(s),rig,mat)
        for r in range(4):
            t=r/3
            for j in range(32):
                a=math.tau*j/32
                y=.300+math.sin(a)*(.011*(1-t)+.001);z=-.88+math.cos(a)*(.009*(1-t)+.001)
                hit,loc,normal,face=body.ray_cast(Vector((s*.3,y,z)),Vector((-s,0,0)),distance=1)
                x=abs(loc.x)+.0003 if hit else .103
                e.v.append((s*(x-.004*t),y,z))
                e.uv.append((j/32,t)); e.w.append({'Head':1}); e.col.append((.047*(1-t)+.008*t,.060*(1-t)+.007*t,.031*(1-t)+.003*t,1))
        for r in range(3):
            for j in range(32):
                a=r*32+j;q=r*32+(j+1)%32;e.f.append((a,q,q+32,a+32))
        e.build()
