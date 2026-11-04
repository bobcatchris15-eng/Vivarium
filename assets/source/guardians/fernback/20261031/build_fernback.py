"""Editable Fernback study. Blender 5.2, Y-up / -Z nose, CPU renders.
Run blender -b -t 8 --python build_fernback.py -- --revision 1
Outputs remain beside this script until explicitly promoted.
"""
import bpy, bmesh, math, os, sys, json, argparse, random
from mathutils import Vector, Matrix
from pathlib import Path
P=argparse.ArgumentParser(); P.add_argument('--revision',type=int,default=1)
A=P.parse_args(sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else [])
REV=A.revision; ROOT=Path(__file__).resolve().parent; OUT=ROOT/f'revision-{REV:02d}'; OUT.mkdir(exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)
parts=[]; details=[]
def mat(name,color,rough=.7):
 m=bpy.data.materials.new(name); m.diffuse_color=(*color,1); m.use_nodes=True
 p=m.node_tree.nodes.get('Principled BSDF'); p.inputs['Base Color'].default_value=(*color,1); p.inputs['Roughness'].default_value=rough
 return m
coat=mat('Fernback short coat',(.32,.23,.145),.87)
n=coat.node_tree.nodes; l=coat.node_tree.links; bs=n.get('Principled BSDF')
vc=n.new('ShaderNodeVertexColor'); vc.layer_name='Coat'; l.new(vc.outputs['Color'],bs.inputs['Base Color'])
noise=n.new('ShaderNodeTexNoise'); noise.inputs['Scale'].default_value=720; noise.inputs['Detail'].default_value=2
bump=n.new('ShaderNodeBump'); bump.inputs['Strength'].default_value=.20; bump.inputs['Distance'].default_value=.00065
l.new(noise.outputs['Fac'],bump.inputs['Height']); l.new(bump.outputs['Normal'],bs.inputs['Normal']); bs.inputs['Sheen Weight'].default_value=.2
nosemat=mat('Rhinarium charcoal',(.018,.012,.009),.38)
eye=mat('Eye deep umber',(.013,.009,.005),.17)
iris=mat('Iris amber brown',(.065,.035,.014) if REV>=6 else (.12,.065,.021),.25)
earmat=mat('Ear muted rose',(.135,.075,.052) if REV>=7 else (.24,.12,.092),.89)
clawmat=mat('Horn claws',(.085,.062,.043),.58)
mouthmat=mat('Lip and nostril',(.025,.016,.011),.76)
lipmat=mat('Lip skin',(.075,.043,.025),.82) if REV>=8 else mouthmat
lidmat=mat('Orbital skin',(.08,.055,.035),.85) if REV>=3 else mouthmat
def objmesh(name,verts,faces,material=coat):
 me=bpy.data.meshes.new(name); me.from_pydata(verts,[],faces); me.update()
 ob=bpy.data.objects.new(name,me); bpy.context.collection.objects.link(ob); me.materials.append(material)
 bm=bmesh.new(); bm.from_mesh(me); bmesh.ops.recalc_face_normals(bm,faces=bm.faces); bm.to_mesh(me); bm.free()
 for p in me.polygons:p.use_smooth=True
 return ob
def ell(name,c,r,material=coat,detail=False):
 bpy.ops.mesh.primitive_uv_sphere_add(segments=32,ring_count=20,location=c)
 ob=bpy.context.object; ob.name=name; ob.scale=r
 bpy.ops.object.transform_apply(location=False,rotation=False,scale=True); ob.data.materials.append(material)
 for p in ob.data.polygons:p.use_smooth=True
 (details if detail else parts).append(ob);return ob
def tube(name,points,radii,material=coat,detail=False,radial=24):
 pts=[Vector(p) for p in points]; verts=[]; faces=[]
 if REV>=2 and not detail and len(pts)>3:
  rr=[Vector(r if isinstance(r,tuple) else (r,r)) for r in radii]; pp=[]; rs=[]
  def cat(a,b,c,d,t):return .5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t)
  for i in range(len(pts)-1):
   for k in range(5):
    t=k/5;pp.append(cat(pts[max(0,i-1)],pts[i],pts[i+1],pts[min(i+2,len(pts)-1)],t));v=cat(rr[max(0,i-1)],rr[i],rr[i+1],rr[min(i+2,len(rr)-1)],t);rs.append((max(.001,v.x),max(.001,v.y)))
  pp.append(pts[-1]);rs.append(tuple(rr[-1]));pts=pp;radii=rs
 for i,p in enumerate(pts):
  tangent=(pts[min(i+1,len(pts)-1)]-pts[max(0,i-1)]).normalized()
  ref=Vector((1,0,0)); u=(ref-tangent*ref.dot(tangent)).normalized(); v=tangent.cross(u)
  rr=radii[i]; rx,ry=rr if isinstance(rr,tuple) else (rr,rr)
  for j in range(radial):
   a=j*math.tau/radial; verts.append(p+u*rx*math.cos(a)+v*ry*math.sin(a))
 for i in range(len(pts)-1):
  for j in range(radial):
   k=i*radial+j; q=i*radial+(j+1)%radial; faces.append((k,q,q+radial,k+radial))
 faces.extend([tuple(reversed(range(radial))),tuple((len(pts)-1)*radial+j for j in range(radial))])
 ob=objmesh(name,verts,faces,material);(details if detail else parts).append(ob);return ob
def loft(name,rows):
 if REV>=3:
  # Axial cross-sections stay vertical. A tilted tube frame can fold back on
  # itself at the neck/withers, making an artificial shoulder shelf.
  rr=[Vector(r) for r in rows];sample=[]
  for i in range(len(rr)-1):
   a,b,c,d=rr[max(0,i-1)],rr[i],rr[i+1],rr[min(i+2,len(rr)-1)]
   for k in range(6):
    t=k/6;sample.append(.5*(2*b+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t))
  sample.append(rr[-1]);verts=[];faces=[];radial=64
  for z,y,rx,ry in sample:
   for j in range(radial):
    a=j*math.tau/radial;verts.append((max(.001,rx)*math.cos(a),y+max(.001,ry)*math.sin(a),z))
  for i in range(len(sample)-1):
   for j in range(radial):
    q=i*radial+j;r=i*radial+(j+1)%radial;faces.append((q,r,r+radial,q+radial))
  faces.extend([tuple(reversed(range(radial))),tuple((len(sample)-1)*radial+j for j in range(radial))])
  ob=objmesh(name,verts,faces);parts.append(ob);return ob
 return tube(name,[(0,y,z) for z,y,rx,ry in rows],[(rx,ry) for z,y,rx,ry in rows],radial=48)
# Primary forms: deep thorax, long lumbar region, sloping sacrum, muscular tail root.
body_rows=[
 (-.58,.565,.044,.059),(-.52,.55,.062,.084),(-.44,.50,.087,.134),
 (-.36,.445,.126,.156),(-.25,.428,.136,.16),(-.10,.423,.126,.15),
 (.06,.435,.106,.129),(.20,.445,.09,.105),(.34,.438,.103,.112),
 (.45,.421,.109,.12),(.53,.403,.083,.09),(.60,.376,.057,.064),
 (.72,.328,.044,.049),(.85,.269,.033,.037),(.98,.205,.023,.027),
 (1.10,.15,.012,.016),(1.18,.132,.003,.004)]
if REV>=2:
 body_rows[2:15]=[(-.44,.50,.084,.12),(-.36,.445,.12,.15),(-.25,.437,.13,.164),(-.10,.445,.12,.145),(.06,.455,.099,.11),(.20,.463,.085,.085),(.34,.442,.104,.104),(.45,.416,.107,.114),(.53,.394,.09,.10),(.60,.365,.071,.075),(.72,.318,.055,.058),(.85,.263,.04,.045),(.98,.205,.026,.030)]
if REV>=5:
 body_rows[12]=(.72,.318,.060,.063);body_rows[13]=(.85,.265,.044,.049)
if REV>=7:
 body_rows[0:10]=[(-.58,.555,.037,.047),(-.52,.535,.051,.066),(-.44,.492,.074,.099),(-.36,.454,.103,.127),(-.25,.447,.119,.137),(-.10,.444,.116,.130),(.06,.452,.103,.112),(.20,.456,.089,.095),(.34,.437,.098,.102),(.45,.416,.101,.108)]
if REV>=9:
 body_rows[10:15]=[(.53,.414,.058,.075),(.60,.384,.047,.060),(.72,.335,.035,.045),(.85,.274,.025,.034),(.98,.210,.016,.022)]
if REV>=11:
 body_rows[6:10]=[(.06,.460,.099,.102),(.20,.465,.076,.078),(.34,.443,.104,.096),(.45,.425,.108,.102)]
if REV>=12:
 body_rows[0:2]=[(-.56,.580,.026,.026),(-.49,.540,.047,.056)]
 body_rows[8:10]=[(.34,.416,.104,.090),(.45,.395,.108,.095)]
body=loft('Continuous axial volume',body_rows)
# Head follows skull and soft-tissue envelope rather than a pointed canine snout.
head_rows=[
 (-.85,.535,.019,.023),(-.825,.54,.025,.026),(-.77,.549,.03,.031),
 (-.71,.565,.037,.037),(-.66,.584,.052,.051),(-.61,.59,.060,.060),
 (-.56,.59,.05,.057),(-.53,.579,.04,.045)]
if REV>=7:
 head_rows=[(-.85,.535,.016,.020),(-.825,.54,.022,.024),(-.77,.549,.026,.027),(-.71,.565,.032,.031),(-.66,.584,.043,.041),(-.61,.59,.044,.049),(-.56,.59,.037,.047),(-.53,.579,.033,.038)]
loft('Head and long rostrum',head_rows)
if REV>=7:
 loft('Mandibular ramus',[(-.843,.517,.014,.009),(-.80,.516,.022,.013),(-.74,.518,.026,.015),(-.68,.525,.032,.023),(-.63,.544,.035,.031),(-.60,.554,.023,.022)])
else:ell('Mandible',(0,.518,-.72),(.031,.025,.115))
if REV>=3:
 if REV<10:
  ell('Nasal bridge',(0,.573,-.70),(.032,.030,.064))
  for s in [-1,1]:ell('Cheek soft tissue '+str(s),(s*(.028 if REV>=7 else .033),.561,-.651),(.014,.025,.043) if REV>=7 else (.026,.033,.05))
 elif REV==10:
  for s in [-1,1]:tube('Pinna root '+str(s),[(s*.027,.608,-.58),(s*.039,.617,-.582),(s*.046,.624,-.582)],[.016,.018,.016])
if REV<2:ell('Throat',(0,.51,-.51),(.06,.083,.104))
# Forelimb and hindlimb chains; stifle points forward, calcaneal joint backward.
chains={}
for s,side in [(1,'L'),(-1,'R')]:
 x=s*.092
 front=[(x,.50,-.32),(s*.102,.335,-.265),(s*.103,.112,-.325),(s*.104,.045,-.342),(s*.104,.030,-.372)]
 hind=[(s*.087,.422,.405),(s*.108,.29,.31),(s*.10,.125,.49),(s*.10,.043,.44),(s*.10,.03,.407)]
 if REV>=7:
  front=[(s*.084,.47,-.315),(s*.102,.303,-.258),(s*.103,.086,-.326),(s*.104,.043,-.350),(s*.104,.025,-.367)]
  hind=[(s*.087,.430,.405),(s*.108,.286,.275),(s*.10,.140,.425),(s*.10,.047,.390),(s*.10,.026,.355)]
 chains['F'+side]=front;chains['H'+side]=hind
 if REV>=7:
  # Cross-sections explicitly describe deltoid/triceps, elbow, forearm belly,
  # tapering distal radius/ulna, carpus and narrow pastern. They are not a
  # single constant-radius tube with an arbitrary bend in it.
  tube('Forelimb anatomical '+side,[(s*.038,.475,-.315),(s*(.075 if REV>=11 else .084),.419,-.292),(s*.101,.351,-.251),front[1],(s*.103,.273,-.267),(s*.103,.220,-.291),(s*.103,.151,-.313),(s*.103,.108,-.323),front[2],(s*.104,.064,-.338),front[3],front[4]],[(.030,.037),(.033,.046) if REV>=11 else (.044,.052),(.026,.034) if REV>=11 else (.030,.040),(.022,.027),(.027,.034),(.020,.027),(.013,.018),(.012,.015),(.019,.018),(.012,.014),(.012,.014),(.018,.021)])
  ell('Olecranon '+side,(s*.102,.306,-.237),(.017,.022,.014))
  ell('Elbow medial epicondyle '+side,(s*.115,.305,-.258) if REV>=11 else (s*.117,.305,-.258),(.007,.014,.010) if REV>=11 else (.010,.014,.015))
  tube('Fore extensor tendon '+side,[(s*.104,.202,-.313),(s*.104,.151,-.332),(s*.104,.107,-.339),(s*.104,.082,-.341)],[.0033,.0027,.0024,.0030])
  tube('Hindlimb anatomical '+side,[(s*.038,.434,.405),(s*(.078 if REV>=11 else .085),.385,.365),(s*(.099 if REV>=11 else .103),.327,.304),hind[1],(s*.106,.250,.314),(s*.103,.205,.365),(s*.101,.164,.407),hind[2],(s*.10,.116,.416),(s*.10,.078,.402),hind[3],hind[4]],[(.037,.045),(.041,.062) if REV>=11 else (.052,.066),(.031,.041) if REV>=11 else (.038,.044),(.025,.030),(.029,.038),(.020,.029),(.012,.019),(.019,.020),(.012,.014),(.010,.012),(.013,.014),(.019,.022)])
  ell('Patella '+side,(s*.108,.288,.259) if REV>=11 else (s*.108,.286,.252),(.009,.018,.008) if REV>=11 else (.014,.017,.013))
  ell('Calcaneal tuber '+side,(s*.10,.150,.434) if REV>=9 else (s*.10,.154,.443),(.008,.018,.010) if REV>=9 else (.014,.015,.014))
  tube('Achilles tendon '+side,[(s*.103,.237,.345),(s*.101,.197,.394),(s*.10,.159,.433) if REV>=9 else (s*.10,.161,.441)],[.005,.0035,.004])
 elif REV>=4:
  tube('Forelimb '+side,[(s*.04,.48,-.32),(s*.095,.425,-.30)]+front[1:],[.034,.052,.047,.027,.021,.026])
  tube('Hindlimb '+side,[(s*.04,.425,.405),(s*.10,.36,.365)]+hind[1:],[.041,.072,.058,.024,.019,.027])
 else:
  tube('Forelimb '+side,front,[.062,.047,.027,.021,.026] if REV>=2 else [.061,.043,.023,.018,.025])
  tube('Hindlimb '+side,hind,[.082,.058,.024,.019,.027])
 if REV<3:ell('Scapular muscle '+side,(s*.082,.454,-.31),(.042 if REV>=2 else .058,.115,.095))
 if REV<3:ell('Haunch '+side,(s*.077,.379,.403),(.062 if REV>=2 else .073,.105,.09))
 for label,chain in [('F',front),('H',hind)]:
  paw=Vector(chain[-1]);ell('Paw '+label+side,paw,(.032,.023,.042) if REV>=8 else (.028,.022,.034) if REV>=7 else (.037,.026,.057))
  for j in range(4):
   toe=paw+Vector(((j-1.5)*(.013 if REV>=7 else .017),-.005,(-.032+abs(j-1.5)*.005) if REV>=8 else (-.025+abs(j-1.5)*.005) if REV>=7 else -.038+abs(j-1.5)*.007))
   ell('Toe '+label+side+str(j),toe,(.009,.015,.022) if REV>=8 else (.008,.016,.018) if REV>=7 else (.010,.018,.025))
   ob=tube('Claw '+label+side+str(j),[toe+Vector((0,.003,-.014 if REV>=8 else -.018)),toe+Vector((0,-.004 if REV>=8 else -.006,-.026 if REV>=8 else -.035))],[.0025,.0005] if REV>=8 else [.004,.0008],clawmat,True,12);ob['bind']='Hand_'+side if label=='F' else 'Foot_'+side
  if label=='F':
   if REV>=9:
    tube('Attached dew digit '+side,[(s*.113,.095,-.322),(s*.124,.080,-.319),(s*.125,.066,-.321)],[.006,.005,.003])
   else:ell('Dewclaw '+side,(s*.125,.095,-.312),(.011,.015,.013))
# Fuse intersecting primary volumes into a continuous watertight skin.
bpy.ops.object.select_all(action='DESELECT')
for ob in parts:ob.select_set(True)
bpy.context.view_layer.objects.active=body;bpy.ops.object.join();skin=bpy.context.object;skin.name='Fernback_Skin'
rem=skin.modifiers.new('Continuous sculpt volume','REMESH');rem.mode='VOXEL';rem.voxel_size=.0025 if REV>=7 else .0032;rem.use_smooth_shade=True
bpy.ops.object.modifier_apply(modifier=rem.name)
sm=skin.modifiers.new('Relax sculpt transitions','SMOOTH');sm.factor=.35 if REV>=7 else 1.35;sm.iterations=2 if REV>=7 else 6;bpy.ops.object.modifier_apply(modifier=sm.name)
if REV>=11:
 # Blend only fleshy shoulder/pelvic attachments; preserve distal joint relief.
 neighbours=[[] for v in skin.data.vertices]
 for e in skin.data.edges:
  a,b=e.vertices;neighbours[a].append(b);neighbours[b].append(a)
 region=[]
 for v in skin.data.vertices:
  x,y,z=v.co
  if .33<y<.49 and abs(x)>.055 and (-.41<z<-.19 or .23<z<.49):
   w=min(1,(y-.33)/.065)*min(1,(.49-y)/.055)*min(1,(abs(x)-.055)/.035)
   region.append((v.index,w))
 for iteration in range(4):
  old=[v.co.copy() for v in skin.data.vertices]
  for i,w in region:
   if neighbours[i]:skin.data.vertices[i].co=old[i].lerp(sum((old[j] for j in neighbours[i]),Vector())/len(neighbours[i]),.35*w)
 skin.data.update()
dec=skin.modifiers.new('Surface economy','DECIMATE');dec.ratio=.48;bpy.ops.object.modifier_apply(modifier=dec.name)
if REV>=5:
 bm=bmesh.new();bm.from_mesh(skin.data)
 bmesh.ops.remove_doubles(bm,verts=list(bm.verts),dist=.000001)
 boundary=[e for e in bm.edges if e.is_boundary]
 if boundary:bmesh.ops.holes_fill(bm,edges=boundary,sides=0)
 bmesh.ops.recalc_face_normals(bm,faces=bm.faces);bm.to_mesh(skin.data);bm.free();skin.data.update()
for p in skin.data.polygons:p.use_smooth=True
if REV>=6:
 for v in skin.data.vertices:
  x,y,z=v.co
  if abs(x)>.025 and -.70<z<-.61:
   inset=.003*math.exp(-((y-.592)/.018)**2-((z+.658)/.023)**2)
   v.co.x-=math.copysign(inset,x)
 skin.data.update()
def smooth(a,b,x):
 t=max(0,min(1,(x-a)/(b-a)));return t*t*(3-2*t)
def mix(a,b,t):return tuple(x*(1-t)+y*t for x,y in zip(a,b))
def coatcolor(p):
 x,y,z=p; flank=(.22,.151,.089) if REV>=2 else (.36,.255,.159); dorsal=(.095,.069,.043) if REV>=2 else (.205,.151,.101); belly=(.43,.36,.26) if REV>=2 else (.64,.547,.405)
 # Pale underside, throat and inner limbs; lightly grizzled dorsal saddle.
 col=mix(flank,dorsal,smooth(.43,.58,y)*.8)
 col=mix(col,belly,(1-smooth(.29,.41,y))*.66 if -.4<z<.35 else 0)
 if z<-.35:col=mix(col,belly,(1-smooth(.50,.565,y))*.88*(1-smooth(-.48,-.35,z)))
 if y<.17:col=mix(col,(.24,.177,.116),.18)
 # Six branching stripes on rear half, each tapering and asymmetrically feathered.
 stripe=0
 if -.075<z<.58 and y>.29:
  for i in range(6):
   centre=([-.035,.060,.177,.278,.384,.486][i] if REV>=3 else -.025+i*.098); low=.32+.026*math.sin(i*1.3)
   height=smooth(low,low+(.027 if REV>=5 else .08),y); curve=centre+(.012 if REV>=3 else .035)*math.sin((y-.35)*(6 if REV>=3 else 9)+i*.7)
   if REV>=5:curve+=.007*(i%3-1)*(y-.43)/.15
   width=((.015+.012*smooth(.33,.53,y)) if REV>=2 else (.010+.010*smooth(.33,.53,y)))*(1+.12*math.sin(i*2))
   feather=(.0004 if REV>=2 else .0015)*math.sin(y*430+x*700)
   stripe=max(stripe,(1-smooth(width-.003,width+.003,abs(z-curve)+feather))*height)
   if REV>=3 and abs(x)>.05:
    for k in range(3):
     yy=low+.024+k*.033
     for side in [-1,1]:
      # Tapered leaflets visibly grow from a common stem.
      az,ay=curve,yy+(.008 if REV>=5 and side>0 else 0);bz,by=curve+side*(.036-k*.003),ay+.023
      dz,dy=bz-az,by-ay;t=max(0,min(1,((z-az)*dz+(y-ay)*dy)/(dz*dz+dy*dy)))
      dist=math.hypot(z-(az+dz*t),y-(ay+dy*t));rad=.008*(1-.75*t)
      stripe=max(stripe,1-smooth(rad,rad+.002,dist))
   # pinnules branch from the main stem only low on the flank.
   for k in range(0 if REV>=3 else 2 if REV>=2 else 3):
    yy=low+.035+k*.036
    if yy<y<yy+.052 and abs(x)>.055:
     d=y-yy; off=1.05*d if REV>=2 else .72*d
     stripe=max(stripe,(1-smooth(.006 if REV>=2 else .003,.009 if REV>=2 else .006,abs(abs(z-curve)-off)))*(1-smooth(.028,.052,d))*.9)
 grain=(.006 if REV>=2 else .012)*math.sin(z*630+y*120)*math.sin(x*970+y*25)
 return tuple(max(0,c+grain) for c in mix(col,(.035,.026,.018),stripe))+(1,)
attr=skin.data.color_attributes.new(name='Coat',type='FLOAT_COLOR',domain='POINT')
for v in skin.data.vertices:attr.data[v.index].color=coatcolor(v.co)
if REV>=2:
 # Short tapered guard hairs follow the skin normal and longitudinal fur flow.
 rng=random.Random(96131);vs=[];fs=[];cols=[]
 candidates=list(skin.data.vertices)
 if REV>=5:
  hair_samples=[];polys=list(skin.data.polygons)
  for poly in rng.choices(polys,weights=[p.area for p in polys],k=45000):
   aa,bb,cc=[skin.data.vertices[i] for i in poly.vertices[:3]];u=math.sqrt(rng.random());v=rng.random();wa,wb,wc=1-u,u*(1-v),u*v
   hair_samples.append((aa.co*wa+bb.co*wb+cc.co*wc,(aa.normal*wa+bb.normal*wb+cc.normal*wc).normalized()))
 else:hair_samples=[(v.co.copy(),v.normal.normalized()) for v in rng.sample(candidates,min(42000 if REV>=3 else 18500,len(candidates)))]
 for p,normal in hair_samples:
  if p.y<.13 or p.z<(-.48 if REV>=3 else -.77):continue
  if REV>=5:p-=normal*.001
  length=rng.uniform(.004,.008) if REV>=3 and p.z>-.48 else rng.uniform(.0025,.0055) if p.z>-.48 else rng.uniform(.001,.0025)
  flow=Vector((0,-.14,.9));flow=(flow-normal*flow.dot(normal)).normalized()
  direction=(normal*.45+flow*.9).normalized();width=.0004 if REV>=3 else .00065
  u=normal.cross(direction).normalized()*width
  base=len(vs);vs.extend([p-u,p+u,p+direction*length]);fs.append((base,base+1,base+2));c=coatcolor(p);cols.extend([c,c,tuple(min(1,k*1.1) for k in c[:3])+(1,)])
 fur=objmesh('Short directional guard fur',vs,fs,coat);details.append(fur)
 ca=fur.data.color_attributes.new(name='Coat',type='FLOAT_COLOR',domain='POINT')
 for i,c in enumerate(cols):ca.data[i].color=c
if REV>=3:
 # UVs and a packed tangent normal keep the close coat grain in exported glTF.
 import numpy as np
 size=512; yy,xx=np.mgrid[0:size,0:size]/size
 grain=np.sin(xx*math.tau*137+np.sin(yy*math.tau*11)*1.5)
 if REV>=4:
  grain=np.random.default_rng(61031).normal(size=(size,size))
  for k in range(6):grain=(grain*2+np.roll(grain,1,0)+np.roll(grain,-1,0))/4
  grain=(np.roll(grain,1,1)-np.roll(grain,-1,1));grain/=max(.001,float(grain.std()));grain=np.clip(grain,-2,2)*.45
 rgba=np.ones((size,size,4),dtype=np.float32);rgba[:,:,0]=.5+.13*grain;rgba[:,:,1]=.5+.04*np.cos(yy*math.tau*57+xx*15);rgba[:,:,2]=np.sqrt(np.maximum(0,1-(rgba[:,:,0]*2-1)**2-(rgba[:,:,1]*2-1)**2))*.5+.5
 if REV>=4:
  rgba[:,:,1]=.5+.015*np.roll(grain,3,0);rgba[:,:,2]=np.sqrt(np.maximum(0,1-(rgba[:,:,0]*2-1)**2-(rgba[:,:,1]*2-1)**2))*.5+.5
 im=bpy.data.images.new('Fernback directional fur normal',width=size,height=size);im.colorspace_settings.name='Non-Color';im.pixels.foreach_set(rgba.ravel());im.filepath_raw=str(OUT/'fernback_fur_normal.png');im.file_format='PNG';im.save();im.pack()
 tx=n.new('ShaderNodeTexImage');tx.image=im;nm=n.new('ShaderNodeNormalMap');nm.inputs['Strength'].default_value=.22 if REV>=4 else .45;l.new(tx.outputs['Color'],nm.inputs['Color']);l.new(nm.outputs['Normal'],bs.inputs['Normal'])
 for ob in [skin,fur]:
  uv=ob.data.uv_layers.new(name='Coat flow')
  for loop in ob.data.loops:
   p=ob.data.vertices[loop.vertex_index].co;uv.data[loop.index].uv=(math.atan2(p.x,p.y-.44)/math.tau+.5,(p.z+.85)/2.03)
# Face detail uses small lips, almond lids, recessed eyes and cupped rounded pinnae.
if REV>=6:
 from mathutils.bvhtree import BVHTree
 skull_bvh=BVHTree.FromPolygons([v.co for v in skin.data.vertices],[list(p.vertices) for p in skin.data.polygons])
 def skull_x(side,y,z):
  hit=skull_bvh.ray_cast(Vector((side*.25,y,z)),Vector((-side,0,0)),.25)[0]
  return abs(hit.x) if hit else .051
for s,side in [(1,'L'),(-1,'R')]:
 surf_x=skull_x(s,.592,-.658) if REV>=6 else .051
 ob=ell('Eye '+side,(s*(surf_x-.003 if REV>=6 else .051),.592,-.658),(.0065,.007,.011) if REV>=6 else (.007,.008,.012) if REV>=2 else (.011,.011,.014),eye,True);ob['bind']='Head'
 ob=ell('Iris '+side,(s*(surf_x+.0032 if REV>=6 else .0575 if REV>=2 else .0605),.592,-.66),(.0006,.0045,.0055) if REV>=6 else (.001,.005,.006) if REV>=2 else (.002,.007,.008),iris,True);ob['bind']='Head'
 ob=ell('Pupil '+side,(s*(surf_x+.0037 if REV>=6 else .0585 if REV>=2 else .062),.592,-.661),(.0005,.0035,.0038) if REV>=6 else (.001,.0037,.004) if REV>=2 else (.0014,.0048,.005),eye,True);ob['bind']='Head'
 pts=[]
 for i in range(49):
  a=i*math.tau/48;pts.append((s*(.057 if REV>=2 else .054+.006*abs(math.cos(a))),.592+(.0065 if REV>=2 else .009)*math.sin(a),-.658+(.012 if REV>=2 else .017)*math.cos(a)))
 if REV>=5:
  verts=[];faces=[];colors=[];sections=48
  for row in range(7):
   t=row/6
   for j in range(sections):
    a=j*math.tau/sections
    p=Vector((s*(.056*(1-t)+.043*t+.003*math.sin(math.pi*t)),.592+(.0065*(1-t)+.018*t)*math.sin(a),-.658+(.012*(1-t)+.025*t)*math.cos(a)))
    if REV>=6:
     p.y=.592+(.0055*(1-t)+.017*t)*math.sin(a);p.z=-.658+(.009*(1-t)+.024*t)*math.cos(a)
     p.x=s*(skull_x(s,p.y,p.z)+(.0012*(1-t)**2-.0003*t if REV>=11 else .0002+.001*(1-t)**2))
    verts.append(p);colors.append(mix(coatcolor(p)[:3],(.045,.029,.018),(1-t)**3*.65)+(1,))
  for row in range(6):
   for j in range(sections):faces.append((row*sections+j,row*sections+(j+1)%sections,(row+1)*sections+(j+1)%sections,(row+1)*sections+j))
  ob=objmesh('Sculpted eyelids '+side,verts,faces,coat);details.append(ob);ob['bind']='Head'
  ca=ob.data.color_attributes.new(name='Coat',type='FLOAT_COLOR',domain='POINT')
  for i,c in enumerate(colors):ca.data[i].color=c
 else:
  ob=tube('Eyelid '+side,pts,[.0015 if REV>=3 else .0024]*len(pts),lidmat,True,10);ob['bind']='Head'
 pts=[(s*.023,.519,-.837),(s*.03,.513,-.79),(s*.033,.515,-.73),(s*.041,.531,-.677)]
 if REV>=3:
  control=[Vector(p) for p in pts];pts=[]
  for j in range(25):
   t=j/24;pts.append((1-t)**3*control[0]+3*(1-t)**2*t*control[1]+3*(1-t)*t*t*control[2]+t**3*control[3])
 if REV>=8:
  for p in pts:p.x=s*(skull_x(s,p.y,p.z)+.0002)
 ob=tube('Mouth seam '+side,pts,[.00035 if REV>=11 else .0005 if REV>=8 else .0008]*len(pts) if REV>=3 else [.0015,.0016,.0018,.001],lipmat,True,8);ob['bind']='Head'
 # bowl with rolled rim, front facing -Z
 verts=[];faces=[]
 for r in range(13):
  t=r/12
  for j in range(48):
   a=j*math.tau/48
   xx=math.cos(a)*(.024 if REV>=11 else .030 if REV>=3 else .034 if REV>=2 else .041)*t+(s*.004*t if REV>=11 else s*.014*t);zz=(.007 if REV>=10 else .014)*(1-t*t)
   if REV>=10:
    xx*=.72+.28*max(0,min(1,1+math.sin(a)))
    if REV>=11:xx*=1-.25*max(0,math.sin(a))
    xx+=.0012*math.sin(a*3)*t;zz+=.001*math.sin(a*5)*t*t
   angle=s*.15 if REV>=5 else s*.48 if REV>=2 else 0
   verts.append((s*(.036 if REV>=11 else .052)+xx*math.cos(angle)-zz*math.sin(angle),(.635 if REV>=11 else .639)+math.sin(a)*(.029 if REV>=11 else .036 if REV>=3 else .043 if REV>=2 else .052)*t,-.582+xx*math.sin(angle)+zz*math.cos(angle)))
 for r in range(12):
  for j in range(48):faces.append((r*48+j,r*48+(j+1)%48,(r+1)*48+(j+1)%48,(r+1)*48+j))
 ob=objmesh('Cupped pinna '+side,verts,faces,earmat);details.append(ob);ob['bind']='Ear_'+side
 solid=ob.modifiers.new('Pinna thickness','SOLIDIFY');solid.thickness=.003
 bpy.context.view_layer.objects.active=ob;bpy.ops.object.modifier_apply(modifier=solid.name)
 pts=[verts[12*48+j] for j in range(48)]+[verts[12*48]]
 ob=tube('Pinna fur rim '+side,pts,[.0013 if REV>=7 else .0022 if REV>=3 else .004]*49,coat,True,10);ob['bind']='Ear_'+side
 ca=ob.data.color_attributes.new(name='Coat',type='FLOAT_COLOR',domain='POINT')
 for d in ca.data:d.color=(.23,.15,.095,1) if REV>=7 else (.39,.28,.18,1)
 ob=ell('Nostril '+side,(s*.010,.541,-.855) if REV>=3 else (s*.013,.539,-.856),(.003,.002,.0007) if REV>=3 else (.0055,.004,.0018),mouthmat,True);ob['bind']='Head'
 # sparse whiskers with anatomical roots
 for k in range(3 if REV>=3 else 5):
  p=Vector((s*.026,.537+k*.003,-.79+k*.009))
  if REV>=3:
   pts=[p+Vector((s*(.028+k*.005)*t,.006*(k-1)*t+.003*t*t,(-.02+.015*k)*t)) for t in [j/8 for j in range(9)]]
   ob=tube('Whisker '+side+str(k),pts,[.00018*(1-j/9) for j in range(9)],clawmat,True,5)
  else:ob=tube('Whisker '+side+str(k),[p,p+Vector((s*.022,.004,-.015)),p+Vector((s*(.045+k*.003),.006+k*.002,-.02))],[.00055,.00035,.00007],clawmat,True,5)
  ob['bind']='Head'
ob=ell('Nasal pad',(0,.539,-.85),(.020,.011,.006) if REV>=8 else (.017,.012,.007) if REV>=2 else (.022,.017,.01),nosemat,True);ob['bind']='Head'
if REV>=3:
 for ob in details:
  if coat in list(ob.data.materials) and not ob.data.uv_layers:
   uv=ob.data.uv_layers.new(name='Coat flow')
   for loop in ob.data.loops:
    p=ob.data.vertices[loop.vertex_index].co;uv.data[loop.index].uv=(math.atan2(p.x,p.y-.44)/math.tau+.5,(p.z+.85)/2.03)
# Armature, using the existing runtime bone naming convention.
arm=bpy.data.armatures.new('Fernback skeleton');rig=bpy.data.objects.new('FernbackRig',arm);bpy.context.collection.objects.link(rig)
bpy.context.view_layer.objects.active=rig;rig.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
segments={}
def bone(name,a,b,parent=None):
 e=arm.edit_bones.new(name);e.head=a;e.tail=b
 if parent:e.parent=arm.edit_bones[parent]
 segments[name]=(Vector(a),Vector(b));return e
bone('Root',(0,0,0),(0,.1,0))
bone('Body3',(0,.42,.43),(0,.44,.18),'Root');bone('Body2',(0,.44,.18),(0,.44,-.08),'Body3')
bone('Body1',(0,.44,-.08),(0,.46,-.3),'Body2');bone('Body0',(0,.46,-.3),(0,.51,-.45),'Body1')
bone('Neck',(0,.51,-.45),(0,.58,-.57),'Body0');bone('Head',(0,.58,-.57),(0,.54,-.84),'Neck')
bone('Jaw',(0,.535,-.64),(0,.515,-.82),'Head');bone('Snout',(0,.55,-.70),(0,.54,-.85),'Head')
tail=[(0,.41,.46),(0,.36,.62),(0,.29,.81),(0,.20,1.0),(0,.135,1.18)]
for i in range(4):bone('Tail'+str(i),tail[i],tail[i+1],'Body3' if i==0 else 'Tail'+str(i-1))
for label,pts in chains.items():
 side=label[-1];names=([f'UpperArm_{side}',f'Forearm_{side}',f'Carpus_{side}',f'Hand_{side}'] if label[0]=='F' else [f'Thigh_{side}',f'Shin_{side}',f'Hock_{side}',f'Foot_{side}'])
 for i in range(4):bone(names[i],pts[i],pts[i+1],('Body0' if label[0]=='F' else 'Body3') if i==0 else names[i-1])
for s,side in [(1,'L'),(-1,'R')]:bone('Ear_'+side,(s*.05,.62,-.58),(s*.06,.69,-.58),'Head')
bpy.ops.object.mode_set(mode='OBJECT')
def distance(p,a,b):
 d=b-a;t=max(0,min(1,(p-a).dot(d)/d.length_squared));return (p-(a+d*t)).length
for ob in [skin]+details:
 groups={n:ob.vertex_groups.new(name=n) for n in segments}
 for v in ob.data.vertices:
  p=ob.matrix_world@v.co
  if 'bind' in ob:weights=[(ob['bind'],1)]
  else:
   if p.z<-.54:eligible=['Head','Neck']
   elif p.z>.57:eligible=['Tail'+str(i) for i in range(4)]
   elif p.y<.32:
    side='L' if p.x>0 else 'R';prefix=['UpperArm','Forearm','Carpus','Hand'] if p.z<0 else ['Thigh','Shin','Hock','Foot'];eligible=[n+'_'+side for n in prefix]
   else:eligible=[n for n in segments if n.startswith('Body') or n=='Neck' or ((n.startswith('UpperArm') or n.startswith('Thigh')) and ((p.x>0)==n.endswith('L')))]
   ds=sorted((distance(p,*segments[n]),n) for n in eligible)[:3]
   weights=[(n,math.exp(-((d-ds[0][0])/.035)**2*2)) for d,n in ds]
  total=sum(w for n,w in weights)
  if REV>=2 and 'bind' not in ob:
   axial=[n for n in segments if n.startswith('Body') or n in ['Neck','Head'] or n.startswith('Tail')]
   def nearest(names):
    ds=sorted((distance(p,*segments[n]),n) for n in names)[:3];ww=[(n,math.exp(-((d-ds[0][0])/.045)**2*2)) for d,n in ds];tt=sum(w for n,w in ww);return [(n,w/tt) for n,w in ww]
   weights=nearest(axial);side='L' if p.x>0 else 'R'
   front=p.z<0;zcentre=-.31 if front else .40
   blend=(1-smooth(.27,.44,p.y))*(1-smooth(.085,.20,abs(p.z-zcentre)))
   if p.y<.15:blend=1 if abs(p.z-zcentre)<.20 else blend
   names=[n+'_'+side for n in (['UpperArm','Forearm','Carpus','Hand'] if front else ['Thigh','Shin','Hock','Foot'])]
   weights=[(n,w*(1-blend)) for n,w in weights]+[(n,w*blend) for n,w in nearest(names)];total=sum(w for n,w in weights)
  if REV>=5:
   weights=[(n,w) for n,w in weights if w/total>.0001];total=sum(w for n,w in weights)
   for n,w in weights:groups[n].add([v.index],w/total,'REPLACE')
  else:
   for n,w in weights:
    if w/total>.001:groups[n].add([v.index],w/total,'REPLACE')
 mod=ob.modifiers.new('Fernback deform','ARMATURE');mod.object=rig
 ob.parent=rig;ob.matrix_parent_inverse=rig.matrix_world.inverted()
# Keep skin, detail meshes and joint frames under the same proportion change.
if REV>=7:
 def anatomical_proportion(p):
  q=Vector(p);w=max(0,min(1,(-q.z-.42)/.13))
  if REV>=8:
   # Shorten the neck independently; preserve a long, substantial skull.
   # Reduce the elongated trunk, translating the whole tail rather than
   # shortening its taper or misaligning the hindlimb skeleton.
   z=q.z
   if z<-.53:q.z=-.47+(z+.53)*(1.19 if REV>=10 else 1.035)
   elif z<-.32:q.z=z+.06*((-.32-z)/.21)
   elif z<.45:q.z=-.32+(z+.32)*(.76 if REV>=12 else .85)
   else:q.z=z-(.1848 if REV>=12 else .1155)
   q.x*=1+.10*w;q.y=.565+(q.y-.565)*(1+.10*w)-.008*w
   return q
  if q.z<-.42:q.z=-.42+(q.z+.42)*.76
  q.x*=1-.05*w;q.y=.57+(q.y-.57)*(1-.13*w)
  return q
 for ob in [skin]+details:
  inv=ob.matrix_world.inverted()
  for v in ob.data.vertices:v.co=inv@anatomical_proportion(ob.matrix_world@v.co)
  ob.data.update()
 bpy.context.view_layer.objects.active=rig;bpy.ops.object.mode_set(mode='EDIT')
 for b in arm.edit_bones:b.head=anatomical_proportion(b.head);b.tail=anatomical_proportion(b.tail)
 bpy.ops.object.mode_set(mode='OBJECT')
 for label,pts in chains.items():chains[label]=[tuple(anatomical_proportion(p)) for p in pts]
# Every pose is driven by the same skin and skeleton.
rig.animation_data_create()
def pose(kind,t):
 for b in rig.pose.bones:b.rotation_mode='XYZ';b.rotation_euler=(0,0,0);b.location=(0,0,0)
 def rot(n,v):rig.pose.bones[n].rotation_euler=v
 if kind=='idle':
  rot('Neck',(.025*math.sin(t*math.tau),0,.015*math.sin(t*math.tau)))
  rot('Ear_L',(0,0,.07*math.sin(t*math.tau)));rot('Ear_R',(0,0,-.05*math.sin(t*math.tau+.8)))
 elif kind=='walk':
  for side,offset in [('L',0),('R',math.pi)]:
   q=math.sin(t*math.tau+offset)
   rot('UpperArm_'+side,(.19*q,0,0));rot('Forearm_'+side,(-.12*max(0,-q),0,0))
   rot('Thigh_'+side,(-.18*q,0,0));rot('Shin_'+side,(.13*max(0,q),0,0))
  if REV>=3:
   # Solve the elbow/stifle in the limb plane, holding the distal foot's
   # orientation while the stance target stays on the ground.
   for label,pts in chains.items():
    side=label[-1];front=label[0]=='F';phase=t*math.tau+(math.pi/2 if front else 0)+(0 if side=='L' else math.pi)
    a,b,c=[Vector(p) for p in pts[:3]];target=c+Vector((0,.032*max(0,math.cos(phase)),.045*math.sin(phase)))
    L1=(b-a).length;L2=(c-b).length;delta=target-a;D=min(delta.length,L1+L2-.0001);direction=delta.normalized();target=a+direction*D
    along=(L1*L1-L2*L2+D*D)/(2*D);h=math.sqrt(max(0,L1*L1-along*along));perp=Vector((0,-direction.z,direction.y))
    choices=[a+direction*along+perp*h,a+direction*along-perp*h];joint=min(choices,key=lambda p:(p-b).length)
    def ang(v):return math.atan2(v.z,v.y)
    upper=ang(joint-a)-ang(b-a);lower=ang(target-joint)-ang(c-b)-upper
    prefix=['UpperArm','Forearm','Carpus'] if front else ['Thigh','Shin','Hock']
    rot(prefix[0]+'_'+side,(upper,0,0));rot(prefix[1]+'_'+side,(lower,0,0));rot(prefix[2]+'_'+side,(-upper-lower,0,0))
  rot('Head',(.025,0,.035*math.sin(t*math.tau)))
 elif kind=='feed':
  rot('Neck',(-.38,0,0));rot('Head',(-.28,0,0));rot('Ear_L',(0,0,-.13))
 elif kind=='rest':
  rot('Neck',(.08,0,.15));rot('Head',(.06,0,.13));rot('Ear_L',(0,0,.16));rot('Ear_R',(0,0,-.12))
 rot('Tail2',(0,.035*math.sin(t*math.tau),0))
for kind in ['idle','walk','feed','rest']:
 action=bpy.data.actions.new(kind);rig.animation_data.action=action
 for f in range(1,50,4):
  pose(kind,(f-1)/48)
  for b in rig.pose.bones:b.keyframe_insert('rotation_euler',frame=f,group=b.name)
 action.use_fake_user=True
rig.animation_data.action=bpy.data.actions['idle'];bpy.context.scene.frame_set(1);pose('idle',0)
bpy.ops.object.select_all(action='DESELECT')
for ob in [skin,rig]+details:ob.select_set(True)
bpy.context.view_layer.objects.active=rig
bpy.ops.export_scene.gltf(filepath=str(OUT/'guardian.glb'),export_format='GLB',use_selection=True,export_animations=True,export_animation_mode='ACTIONS',export_skins=True,export_yup=True,export_vertex_color='ACTIVE',export_extras=True)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'fernback.blend'))
# CPU Cycles avoids competing with the running Godot sessions for GPU ownership.
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.device='CPU';scene.cycles.samples=24;scene.cycles.use_denoising=True
scene.render.resolution_x=1200;scene.render.resolution_y=900;scene.render.resolution_percentage=100
if REV>=2:scene.view_settings.exposure=-.4
scene.world=bpy.data.worlds.new('Soft studio world');scene.world.color=(.18,.18,.18);scene.view_settings.view_transform='AgX'
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,min(v.co.y for v in skin.data.vertices)-.0001 if REV>=5 else -.004,0));floor=bpy.context.object;floor.rotation_euler=(math.pi/2,0,0);floor.data.materials.append(mat('Studio floor',(.105,.123,.13),.96))
def aim(ob,target):
 f=(Vector(target)-ob.location).normalized();r=f.cross(Vector((0,1,0))).normalized();u=r.cross(f);ob.rotation_euler=Matrix((r,u,-f)).transposed().to_euler()
for name,loc,power,size,color in [('Key',(1.7,2.8,-1.8),300,2.5,(1,.88,.74)),('Fill',(-2,1.4,-.2),200,2.8,(.74,.85,1)),('Rim',(.3,2.3,2),360,2,(1,.93,.79))]:
 d=bpy.data.lights.new(name,'AREA');d.energy=power;d.shape='DISK';d.size=size;d.color=color;ob=bpy.data.objects.new(name,d);bpy.context.collection.objects.link(ob);ob.location=loc;aim(ob,(0,.3,0))
d=bpy.data.cameras.new('Review camera');cam=bpy.data.objects.new('Review camera',d);bpy.context.collection.objects.link(cam);scene.camera=cam;d.type='ORTHO';d.ortho_scale=2.35
views=[('alert_side','idle',1,(2.8,.72,.16),(0,.34,.16),2.35),('alert_threequarter','idle',1,(1.8,1.05,-1.9),(0,.35,.12),2.3),('walking','walk',13,(2.6,.85,-.85),(0,.34,.12),2.3),('sniffing','feed',1,(2.4,.85,-1),(0,.32,.12),2.3),('watchful','rest',1,(1.8,1,-1.6),(0,.35,.12),2.3),('face','idle',1,(1,.78,-1.6),(0,.57,-.64),.65),('rear','idle',1,(1.9,.9,1.7),(0,.34,.2),2.3)]
if REV>=7:
 views.extend([('forelimb','idle',1,(1.1,.40,-.52),(.10,.22,-.30),.55),('hindlimb','idle',1,(1.1,.40,.68),(.10,.22,.25 if REV>=8 else .36),.55)])
if REV>=10:views=[next(v for v in views if v[0]=='face')]+[v for v in views if v[0]!='face']
cam.location=views[1][3];aim(cam,views[1][4]);d.ortho_scale=2.3
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'fernback-studio.blend'))
for name,kind,frame,loc,target,scale in views:
 rig.animation_data.action=bpy.data.actions[kind];scene.frame_set(frame);cam.location=loc;aim(cam,target);d.ortho_scale=scale
 scene.render.filepath=str(OUT/(name+'.png'));bpy.ops.render.render(write_still=True)
if REV>=7:
 clay=mat('Anatomy neutral clay',(.26,.28,.29),.9)
 if REV>=9:
  for ob in details:
   if 'guard fur' in ob.name.lower() or 'whisker' in ob.name.lower():ob.hide_render=True
 for ob in [skin]+details:
  for slot in ob.material_slots:slot.material=clay
 for name,kind,frame,loc,target,scale in [next(v for v in views if v[0]=='alert_side'),views[-2],views[-1]]:
  rig.animation_data.action=bpy.data.actions[kind];scene.frame_set(frame);cam.location=loc;aim(cam,target);d.ortho_scale=scale
  scene.render.filepath=str(OUT/(name+'-clay.png'));bpy.ops.render.render(write_still=True)
audit={'revision':REV,'skin_vertices':len(skin.data.vertices),'skin_triangles':sum(len(p.vertices)-2 for p in skin.data.polygons),'bones':len(arm.bones),'clips':['idle','walk','feed','rest'],'renders':[v[0] for v in views],'renderer':'Cycles CPU','source_axes':'Y up, -Z nose','runtime_verified':False}
(OUT/'audit.json').write_text(json.dumps(audit,indent=2));print('FERNBACK_COMPLETE',json.dumps(audit))
