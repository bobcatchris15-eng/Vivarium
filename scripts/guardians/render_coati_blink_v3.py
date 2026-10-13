"""Four decisive coati views; preserves stable export bytes and limits GPU load."""
import bpy,sys,math
from pathlib import Path
from mathutils import Vector,Matrix
sys.path.insert(0,str(Path(__file__).resolve().parent))
from ground_art import ROOT,TASK,material,reset
asset=ROOT/'game/assets/guardians/woodland_omnivore'
bpy.ops.wm.open_mainfile(filepath=str(asset/'guardian.blend'))
scene=bpy.context.scene; scene.render.engine='BLENDER_EEVEE'; scene.eevee.taa_render_samples=16
scene.render.resolution_x=640; scene.render.resolution_y=480; scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('CoatiStudio'); scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs[0].default_value=(.17,.18,.20,1); scene.world.node_tree.nodes['Background'].inputs[1].default_value=.55
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE'); rig.animation_data.action=None; reset(rig)
objects=[o for o in bpy.data.objects if o.type=='MESH']; originals={o.name:list(o.data.materials) for o in objects}
bpy.ops.mesh.primitive_plane_add(size=200,location=(0,-.004,0),rotation=(math.pi/2,0,0)); floor=bpy.context.object
floor.data.materials.append(material('NeutralFloor',(.17,.18,.20),.85))
def aim(ob,target):
    f=(Vector(target)-ob.location).normalized(); r=f.cross(Vector((0,1,0))).normalized(); u=r.cross(f).normalized()
    ob.rotation_euler=Matrix((r,u,-f)).transposed().to_euler()
for name,p,energy,size in [('Key',(2,2.8,-3),420,3),('Fill',(-3,1,-1),240,3),('Rim',(0,2.5,2),580,2.5)]:
    d=bpy.data.lights.new(name,'AREA'); d.energy=energy; d.shape='DISK'; d.size=size
    o=bpy.data.objects.new(name,d); bpy.context.collection.objects.link(o); o.location=p; aim(o,(0,.5,-.1))
d=bpy.data.cameras.new('CoatiV2Camera'); cam=bpy.data.objects.new('CoatiV2Camera',d); bpy.context.collection.objects.link(cam); scene.camera=cam; d.type='ORTHO'
folder=TASK/'coati'; folder.mkdir(parents=True,exist_ok=True)
def shot(name,target,offset,scale):
    d.ortho_scale=scale; cam.location=Vector(target)+Vector(offset); aim(cam,target)
    scene.render.filepath=str(folder/(name+'.png')); bpy.ops.render.render(write_still=True)
rig.animation_data.action=bpy.data.actions['idle']
for i,frame in enumerate([1,7,13,18,21,23,25,27,29,32,36,42,48,54,60,61]):
    scene.frame_set(frame)
    shot('blink_%02d'%i,(0,.445,-.535),(2,.7,-3),.69)
rig.animation_data.action=bpy.data.actions['rest'];scene.frame_set(31)
shot('rest_closed',(0,.355,-.535),(2,.7,-3),.69)
print('COATI_BLINK_EVIDENCE_COMPLETE')
