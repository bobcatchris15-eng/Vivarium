import math
from mathutils import Vector

def add_lids(Geometry,rig,g):
    for side,suffix in [(1,'L'),(-1,'R')]:
        for upper,name in [(True,'Blink_'+suffix),(False,'BlinkLower_'+suffix)]:
            rows=[];sign=1 if upper else -1
            pole=g.vertex((side*.0575,.266+sign*.0134,-.607),(.5,0),{name:1})
            for ring in range(1,11):
                theta=math.pi/2*ring/10;row=[]
                for j in range(49):
                    a=math.tau*j/48;x=side*.0575+.0125*math.sin(theta)*math.cos(a);y=.266+sign*.0134*math.cos(theta);z=-.607+.015*math.sin(theta)*math.sin(a)
                    row.append(g.vertex((x,y,z),(j/48,ring/10),{name:1}))
                rows.append(row)
            for j in range(48):g.face((pole,rows[0][j],rows[0][j+1]),1)
            for left,right in zip(rows,rows[1:]):
                for j in range(48):g.face((left[j],left[j+1],right[j+1],right[j]),1)

def lid_pose(rig,clip,t):
    if clip=='rest':closed=1
    else:
        center={'idle':.43,'feed':.72,'walk':.81}[clip];d=t-center
        closed=max(0,min(1,(d+.045)/.045)) if d<0 else (1 if d<.03 else max(0,1-(d-.03)/.065))
    for side,suffix in [(1,'L'),(-1,'R')]:
        for sign,name in [(1,'Blink_'+suffix),(-1,'BlinkLower_'+suffix)]:
            offset=Vector((-side*.020*(1-closed),sign*.004*(1-closed),0))
            rig.pose.bones[name].location=rig.data.bones[name].matrix_local.to_3x3().inverted()@offset
