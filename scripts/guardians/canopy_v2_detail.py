"""Close hoatzin geometry/surface reviews with anatomical Y camera up."""
import bpy,sys
from pathlib import Path
from mathutils import Vector,Matrix
sys.path.insert(0,str(Path(__file__).resolve().parent))
from canopy_art import REPORT,material
bpy.ops.wm.open_mainfile(filepath=str(REPORT/'crested_canopy-authoring.blend'))
scene=bpy.context.scene; ob=next(o for o in scene.objects if o.type=='MESH')
scene.render.engine='CYCLES'; scene.cycles.samples=16
scene.render.threads_mode='FIXED'; scene.render.threads=4
scene.render.resolution_x=780; scene.render.resolution_y=640; scene.render.resolution_percentage=100
scene.world=bpy.data.worlds.new('FaceReviewWorld'); scene.world.use_nodes=True
scene.world.node_tree.nodes['Background'].inputs['Color'].default_value=(.18,.20,.17,1)
scene.world.node_tree.nodes['Background'].inputs['Strength'].default_value=.55
scene.view_settings.view_transform='AgX'
for name,power,pos,size in [('Key',75,(-1,1.4,-1.2),1.1),('Fill',30,(.9,.7,-1),1.1),('Rim',60,(.2,1,.8),1)]:
    data=bpy.data.lights.new(name,'AREA'); data.energy=power; data.size=size
    light=bpy.data.objects.new(name,data); bpy.context.collection.objects.link(light); light.location=pos
    light.rotation_euler=(Vector((0,.53,-.51))-light.location).to_track_quat('-Z','Y').to_euler()
camdata=bpy.data.cameras.new('FaceReviewCamera'); cam=bpy.data.objects.new('FaceReviewCamera',camdata); bpy.context.collection.objects.link(cam); scene.camera=cam
camdata.type='ORTHO'; camdata.ortho_scale=.40
def aim(pos,target):
    cam.location=pos; d=(Vector(target)-cam.location).normalized()
    right=d.cross(Vector((0,1,0))).normalized(); up=right.cross(d).normalized()
    cam.rotation_euler=Matrix((right,up,-d)).transposed().to_euler()
mats=list(ob.data.materials); clay=material('FaceReviewClay',(.43,.43,.43),.8)
views=[('threequarter',(.5,.66,-1)),('side',(1,.59,-.54)),('front',(0,.60,-1.1))]
if '--targeted' in sys.argv: views=views[:2]
for label,pos in views:
    aim(pos,(0,.565,-.51))
    for mode in (('surface',) if '--targeted' in sys.argv else ('surface','clay')):
        for i,m in enumerate(mats): ob.data.materials[i]=m if mode=='surface' else clay
        scene.render.filepath=str(REPORT/(('final-' if '--targeted' in sys.argv else '')+'face-'+mode+'-'+label+'.png')); bpy.ops.render.render(write_still=True)
for i,m in enumerate(mats): ob.data.materials[i]=m
camdata.ortho_scale=.50; aim((.8,.57,.0),(0,.31,.12))
scene.render.filepath=str(REPORT/('final-wing-vane-detail.png' if '--targeted' in sys.argv else 'wing-vane-detail.png')); bpy.ops.render.render(write_still=True)
print('CANOPY_V2_DETAIL_COMPLETE')
