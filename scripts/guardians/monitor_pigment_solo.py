"""Bake anatomical small pigment spots into the body's unique UV chart.
Source coordinates are metres, Y up, -Z forward. Spots are about8-18mm,
independent of2-5mm dermal relief. Ordinary embedded glTF bitmap, no runtime shader.
"""
import bpy,math
import numpy as np

def bake_pigment(body,mat,out,profile):
    size=2048;mesh=body.data;mesh.calc_loop_triangles()
    coords=np.array([tuple(v.co) for v in mesh.vertices],dtype=np.float32)
    profile=np.array(profile)
    def pigment(p):
        x,y,z=p[...,0],p[...,1],p[...,2]
        cy=np.interp(z,profile[:,0],profile[:,1]);rx=np.interp(z,profile[:,0],profile[:,2]);ry=np.interp(z,profile[:,0],profile[:,3])
        q=np.mod(np.arctan2(x/np.maximum(rx,.02),(y-cy)/np.maximum(ry,.02)),math.tau)
        yy=z*26;row=np.floor(yy);xx=q*7+(row%2)*.5;cell=np.floor(xx)
        seed=np.mod(np.sin(row*127.1+cell*311.7)*43758.5453,1)
        du=xx%1-.5+(seed-.5)*.19;dv=yy%1-.5+np.mod(seed*13.3,1)*.13
        distance=du*du/.073+dv*dv/.080
        spot=np.clip((.66-distance)/.24,0,1)*(seed>.55)
        rgb=np.array([.058,.052,.036])+spot[...,None]*np.array([.125,.093,.041])
        belly=np.clip((-np.cos(q)-.35)/.45,0,1)
        rgb=rgb*(1-belly[...,None])+np.array([.17,.15,.096])*belly[...,None]
        limbs=(np.abs(x)>rx*1.13)|(y<.12)
        rgb=np.where(limbs[...,None],np.array([.073,.065,.044]),rgb)
        band=(.5+.5*np.sin((z-.55)*21))**3
        tail=np.array([.057,.049,.033])+band[...,None]*np.array([.092,.070,.032])
        rgb=np.where((z>.57)[...,None],tail,rgb)
        grain=1+.025*np.sin(x*513+z*467)*np.sin(y*389-z*327)
        return rgb*grain[...,None]
    # Retain pigment for scutes, while bitmap carries body color without double multiplication.
    attribute=mesh.color_attributes.new(name='Pigment',type='FLOAT_COLOR',domain='POINT')
    vertex_rgb=pigment(coords)
    for i,c in enumerate(vertex_rgb):attribute.data[i].color=(*c,1);mesh.color_attributes['Color'].data[i].color=(1,1,1,1)
    mesh.color_attributes.active_color=mesh.color_attributes['Color']
    rgba=np.zeros((size,size,4),dtype=np.float32);mask=np.zeros((size,size),dtype=bool)
    uv=mesh.uv_layers.active.data
    for tri in mesh.loop_triangles:
        u=np.array([tuple(uv[i].uv) for i in tri.loops])*size
        low=np.maximum(0,np.floor(u.min(axis=0)).astype(int));high=np.minimum(size-1,np.ceil(u.max(axis=0)).astype(int))
        if np.any(high<low):continue
        a,b,c=u;den=(b[1]-c[1])*(a[0]-c[0])+(c[0]-b[0])*(a[1]-c[1])
        if abs(den)<1e-8:continue
        xx,yy=np.meshgrid(np.arange(low[0],high[0]+1)+.5,np.arange(low[1],high[1]+1)+.5)
        w0=((b[1]-c[1])*(xx-c[0])+(c[0]-b[0])*(yy-c[1]))/den
        w1=((c[1]-a[1])*(xx-c[0])+(a[0]-c[0])*(yy-c[1]))/den;w2=1-w0-w1
        inside=(w0>=-.0001)&(w1>=-.0001)&(w2>=-.0001)
        if not inside.any():continue
        positions=coords[list(tri.vertices)]
        points=w0[inside,None]*positions[0]+w1[inside,None]*positions[1]+w2[inside,None]*positions[2]
        area=rgba[low[1]:high[1]+1,low[0]:high[0]+1];area[inside,:3]=pigment(points);area[inside,3]=1
        mask[low[1]:high[1]+1,low[0]:high[0]+1]|=inside
    for padding in range(8):
        previous=mask.copy()
        for dy,dx in [(0,1),(0,-1),(1,0),(-1,0)]:
            shifted=np.roll(np.roll(previous,dy,axis=0),dx,axis=1);take=shifted&~mask
            shifted_rgb=np.roll(np.roll(rgba,dy,axis=0),dx,axis=1);rgba[take]=shifted_rgb[take];mask|=take
    rgba[~mask]=(.058,.052,.036,1)
    image=bpy.data.images.new('monitor_anatomical_pigment',width=size,height=size)
    image.pixels.foreach_set(rgba.ravel());image.filepath_raw=str(out/'body_pigment.png');image.file_format='PNG';image.save();image.pack()
    own=mat.copy();own.name='MonitorAnatomicalPigmentPBR'
    for node in own.node_tree.nodes:
        if node.type=='TEX_IMAGE' and node.image and 'albedo' in node.image.name:node.image=image
    body.data.materials.clear();body.data.materials.append(own)
    print('MONITOR_PIGMENT_BAKED 2048x2048,8pixel padding,8-18mm pigment',flush=True)
