"""Contact audit and 10-second geometry transition sequence for canopy assets.

Run with Blender --background --threads 4 --python canopy_validate.py -- SPECIES.
The sequence is deliberately a neutral workbench geometry diagnostic, not a
claim of texture/runtime visual parity. Frames sample an authored treadmill
timeline idle 0-2s, walk 2-4s, feed 4-6s, rest 6-8s, walk 8-10s.
"""
import bpy,sys,json,math,array
from pathlib import Path
from mathutils import Vector,Matrix
sys.path.insert(0,str(Path(__file__).resolve().parent))
from canopy_art import REPORT,material

species=sys.argv[sys.argv.index('--')+1]; avian=species=='crested_canopy'
bpy.ops.wm.open_mainfile(filepath=str(REPORT/(species+'-authoring.blend')))
rig=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
ob=next(o for o in bpy.context.scene.objects if o.type=='MESH')
scene=bpy.context.scene
measurements=[]; failures=[]; floor=.009
body_top_normal=list(ob.data.polygons[0].normal)
if body_top_normal[1]<=0: failures.append({'kind':'inward_body_loft_winding','normal':body_top_normal})
feet=[b for b in rig.pose.bones if 'Foot' in b.name]
foot_indices={}
for bone in feet:
    group=ob.vertex_groups[bone.name].index
    foot_indices[bone.name]=[v.index for v in ob.data.vertices if any(g.group==group and g.weight>.9 for g in v.groups)]
coords=array.array('f',[0.0])*(len(ob.data.vertices)*3)
for name in ('idle','walk','feed','rest'):
    action=bpy.data.actions[name]; rig.animation_data.action=action
    end=13 if avian and name=='walk' else 19 if name=='walk' else 49
    for frame in range(1,end+1):
        scene.frame_set(frame); bpy.context.view_layer.update()
        deps=bpy.context.evaluated_depsgraph_get(); evaluated=ob.evaluated_get(deps); mesh=evaluated.to_mesh()
        mesh.vertices.foreach_get('co',coords)
        record={'clip':name,'frame':frame,'whole_mesh_min_y':min(coords[1::3]),'feet':{}}
        for bone in feet:
            miny=min(coords[i*3+1] for i in foot_indices[bone.name])
            record['feet'][bone.name]={'min_y':miny,'head':list(bone.head)}
        evaluated.to_mesh_clear(); measurements.append(record)
        if record['whole_mesh_min_y']<floor-.012: failures.append({'clip':name,'frame':frame,'kind':'floor_penetration','min_y':record['whole_mesh_min_y']})
walk=[r for r in measurements if r['clip']=='walk']
stance=[]
for i,bone in enumerate([b for b in rig.pose.bones if 'Foot' in b.name]):
    # Rig iteration order: left front/hind, right front/hind.
    offset=(i%2)*.5 if avian else [0,.5,.75,.25][i]
    cycle=.5 if avian else .75; speed=.2 if avian else .12
    samples=[]
    for r in walk[:-1]:
        p=((r['frame']-1)/(12 if avian else 18)+offset)%1
        if p<.65:
            head=r['feet'][bone.name]['head']; actor_z=-speed*(r['frame']-1)/24
            samples.append({'frame':r['frame'],'head_y':head[1],'world_foot_z_with_nominal_travel':head[2]+actor_z,'min_y':r['feet'][bone.name]['min_y']})
    # A wrap can span a new plant; measure only consecutive stance runs.
    drifts=[]
    for a,b in zip(samples,samples[1:]):
        if b['frame']==a['frame']+1: drifts.append(abs(b['world_foot_z_with_nominal_travel']-a['world_foot_z_with_nominal_travel']))
    stance.append({'bone':bone.name,'max_consecutive_stance_drift_m':max(drifts,default=0),'head_y_range_m':max(s['head_y'] for s in samples)-min(s['head_y'] for s in samples),'min_foot_y':min(s['min_y'] for s in samples),'samples':samples})
    if max(drifts,default=0)>.004: failures.append({'bone':bone.name,'kind':'stance_drift'})
report={'species':species,'floor_y':floor,'body_top_normal':body_top_normal,'nominal_speed_m_s':.2 if avian else .12,'cycle_s':.5 if avian else .75,'stance':stance,'failures':failures,'frames':measurements,
        'limitations':['Authored treadmill clips require runtime locomotion speed to match nominal speed times instance scale.','No terrain adaptation or branch grasp IK is supplied.','Feed porcupine front feet intentionally rise; rear feet remain support.','Rest is a supported repose rather than a defensive full-body curl.','Workbench sequence visualizes geometry and transitions, not exported surface shading.']}
(REPORT/(species+'-contact-audit.json')).write_text(json.dumps(report,indent=2))
print('CONTACT_AUDIT '+json.dumps({'species':species,'failures':failures,'stance':[{k:v for k,v in s.items() if k!='samples'} for s in stance]}))
if '--audit-only' in sys.argv:
    print('CANOPY_VALIDATION_COMPLETE '+species); raise SystemExit

scene.render.engine='BLENDER_WORKBENCH'; scene.render.resolution_x=480; scene.render.resolution_y=340; scene.render.resolution_percentage=100
scene.display.shading.light='STUDIO'; scene.display.shading.color_type='SINGLE'; scene.display.shading.single_color=(.46,.46,.46)
scene.display.shading.show_shadows=True; scene.display.shading.show_cavity=True; scene.display.shading.cavity_type='BOTH'
scene.display.shading.background_type='WORLD'; scene.world=bpy.data.worlds.new('DiagnosticWorld'); scene.world.color=(.09,.09,.09)
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,floor,0),rotation=(math.pi/2,0,0))
camdata=bpy.data.cameras.new('DiagnosticCamera'); cam=bpy.data.objects.new('DiagnosticCamera',camdata); bpy.context.collection.objects.link(cam); scene.camera=cam
camdata.type='ORTHO'; camdata.ortho_scale=1.7; cam.location=(2,.9,-2)
target=Vector((0,.35,.08)); d=(target-cam.location).normalized(); right=d.cross(Vector((0,1,0))).normalized(); up=right.cross(d).normalized(); cam.rotation_euler=Matrix((right,up,-d)).transposed().to_euler()
framesdir=REPORT/(species+'-sequence'); framesdir.mkdir(exist_ok=True)
for frame in range(80):
    t=frame/8; clip=('idle','walk','feed','rest','walk')[min(4,int(t//2))]
    local=t%2; action=bpy.data.actions[clip]; rig.animation_data.action=action
    length=12 if avian and clip=='walk' else 18 if clip=='walk' else 48
    scene.frame_set(1+int(local*24)%length)
    scene.render.filepath=str(framesdir/(str(frame).zfill(3)+'.png')); bpy.ops.render.render(write_still=True)
print('CANOPY_VALIDATION_COMPLETE '+species)
