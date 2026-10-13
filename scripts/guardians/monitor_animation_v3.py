"""Paired curved skin lids translate over unchanged rigid eye globes."""
import math
from mathutils import Vector
def lids(Builder,rig,mat):
    for side,suffix in [(-1,'R'),(1,'L')]:
        for upper,name in [(True,'Blink_'+suffix),(False,'BlinkLower_'+suffix)]:
            b=Builder('monitor_mobile_lid_'+name,rig,mat); seg=48
            for ring in range(10):
                theta=.015+(math.pi/2-.015)*ring/9
                for j in range(seg):
                    a=math.tau*j/seg
                    x=side*.087+.0093*math.sin(theta)*math.cos(a)
                    y=.326+(1 if upper else -1)*.0135*math.cos(theta)
                    z=-1.006+.0135*math.sin(theta)*math.sin(a)
                    b.v.append((x,y,z));b.uv.append((j/seg,ring/9));b.w.append({name:1})
                    b.col.append((.042,.051,.025,1) if upper else (.037,.042,.02,1))
            for ring in range(9):
                for j in range(seg):
                    a=ring*seg+j;q=ring*seg+(j+1)%seg;b.f.append((a,q,q+seg,a+seg))
            pole=len(b.v);b.v.append((side*.087,.326+(1 if upper else -1)*.0135,-1.006));b.uv.append((.5,0));b.w.append({name:1});b.col.append((.042,.051,.025,1))
            for j in range(seg):b.f.append((pole,j,(j+1)%seg))
            b.build()
        seam=Builder('monitor_closed_lid_seam_'+suffix,rig,mat)
        nodes=[]
        for j in range(25):
            zoff=-.0125+j*.025/24
            x=side*(.087+.0093*math.sqrt(max(0,1-(zoff/.0135)**2))+.00012)
            nodes.append(((x,.326,-1.006+zoff),.00016,.00016,{'Blink_'+suffix:1}))
        seam.ringmesh(nodes,8,(.008,.007,.004,1));seam.build()
def pose(rig,amount):
    for suffix in ['L','R']:
        for sign,name in [(1,'Blink_'+suffix),(-1,'BlinkLower_'+suffix)]:
            side=1 if suffix=='L' else -1
            offset=Vector((-side*.017*(1-amount),sign*.004*(1-amount),0))
            rig.pose.bones[name].location=rig.data.bones[name].matrix_local.to_3x3().inverted()@offset
def closure(name,t):
    if name=='rest': return 1.0
    # Closed hold spans two frames, with faster closure than reopening.
    center={'idle':.64,'feed':.78,'walk':.84}[name]
    d=t-center
    if -.055<d<0: return (d+.055)/.055
    if 0<=d<.035:return 1
    if .035<=d<.11:return 1-(d-.035)/.075
    return 0








def curved_eye(Builder,rig,mats,side):
    # Thin pigment follows the corneal ellipsoid; flat iris discs protruded
    # through curved closed lids at their edges.
    for kind,radius,depth in [('iris',.007,.00015),('pupil',.004,.00028)]:
        b=Builder('monitor_'+kind+'_curved_'+str(side),rig,mats['iris' if kind=='iris' else 'eye']);seg=48
        for ring in range(9):
            r=radius*max(.04,ring/8)
            for j in range(seg):
                angle=math.tau*j/seg;y=r*math.sin(angle);z=r*math.cos(angle)
                x=side*(.086+.00845*math.sqrt(max(0,1-(y/.013)**2-(z/.013)**2))+depth)
                b.v.append((x,.326+y,-1.006+z));b.uv.append((.5+y/(2*radius),.5+z/(2*radius)));b.w.append({'Head':1});b.col.append((1,1,1,1))
        pole=len(b.v);b.v.append((side*(.086+.00845+depth),.326,-1.006));b.uv.append((.5,.5));b.w.append({'Head':1});b.col.append((1,1,1,1))
        for j in range(seg):b.f.append((pole,j,(j+1)%seg))
        for ring in range(8):
            for j in range(seg):
                a=ring*seg+j;q=ring*seg+(j+1)%seg;b.f.append((a,q,q+seg,a+seg))
        b.build()
