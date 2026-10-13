"""Paired curved skin lids translate over unchanged rigid eye globes."""
import math
from mathutils import Vector
def lids(Builder,rig,mat):
    for side,suffix in [(-1,'R'),(1,'L')]:
        for upper,name in [(True,'Blink_'+suffix),(False,'BlinkLower_'+suffix)]:
            b=Builder('coati_mobile_lid_'+name,rig,mat); seg=48
            for ring in range(10):
                theta=.015+(math.pi/2-.015)*ring/9
                for j in range(seg):
                    a=math.tau*j/seg
                    x=side*.083+.0082*math.sin(theta)*math.cos(a)
                    y=.470+(1 if upper else -1)*.0115*math.cos(theta)
                    z=-.499+.0115*math.sin(theta)*math.sin(a)
                    b.v.append((x,y,z));b.uv.append((j/seg,ring/9));b.w.append({name:1})
                    b.col.append((.085,.043,.018,1) if upper else (.078,.037,.014,1))
            for ring in range(9):
                for j in range(seg):
                    a=ring*seg+j;q=ring*seg+(j+1)%seg;b.f.append((a,q,q+seg,a+seg))
            pole=len(b.v);b.v.append((side*.083,.470+(1 if upper else -1)*.0115,-.499));b.uv.append((.5,0));b.w.append({name:1});b.col.append((.085,.043,.018,1))
            for j in range(seg):b.f.append((pole,j,(j+1)%seg))
            b.build()
        seam=Builder('coati_closed_lid_seam_'+suffix,rig,mat)
        nodes=[]
        for j in range(25):
            zoff=-.0105+j*.021/24
            x=side*(.083+.0082*math.sqrt(max(0,1-(zoff/.0115)**2))+.00012)
            nodes.append(((x,.470,-.499+zoff),.00016,.00016,{'Blink_'+suffix:1}))
        seam.ringmesh(nodes,8,(.024,.012,.005,1));seam.build()
def pose(rig,amount):
    for suffix in ['L','R']:
        for sign,name in [(1,'Blink_'+suffix),(-1,'BlinkLower_'+suffix)]:
            side=1 if suffix=='L' else -1
            offset=Vector((-side*.017*(1-amount),sign*.004*(1-amount),0))
            rig.pose.bones[name].location=rig.data.bones[name].matrix_local.to_3x3().inverted()@offset
def closure(name,t):
    if name=='rest': return 1.0
    # Closed hold spans two frames, with faster closure than reopening.
    center={'idle':.40,'feed':.72,'walk':.72}[name]
    d=t-center
    if -.055<d<0: return (d+.055)/.055
    if 0<=d<.035:return 1
    if .035<=d<.11:return 1-(d-.035)/.075
    return 0







