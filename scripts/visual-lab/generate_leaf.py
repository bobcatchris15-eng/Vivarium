"""Deterministic organic leaf smoke. Blender --background --python this.py -- [--render]."""
import bpy, math, random, hashlib, json, struct, sys
from pathlib import Path
from mathutils import Vector
args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
OUT = Path(args[args.index('--output') + 1]).resolve() if '--output' in args else Path(__file__).resolve().parents[2] / 'build/visual-lab/tool-tests/blender'
OUT.mkdir(parents=True, exist_ok=True)
SEED=7319
random.seed(SEED)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
verts=[]; faces=[]; uvs=[]
rows,cols=32,12
phase=random.uniform(-.15,.15)
for i in range(rows+1):
    t=i/rows
    width=.005+.32*math.sin(math.pi*t)**.85*(1-.25*t)
    for j in range(cols+1):
        s=2*j/cols-1
        verts.append((s*width,t*1.2,.10*math.sin(math.pi*t)+.05*s*s*math.sin(math.pi*t)+.018*s*math.sin(4*math.pi*t+phase)))
        uvs.append((j/cols,t))
for i in range(rows):
    for j in range(cols):
        a=i*(cols+1)+j; faces.append((a,a+1,a+cols+2,a+cols+1))
me=bpy.data.meshes.new('OrganicLeafMesh'); me.from_pydata(verts,[],faces); me.update()
ob=bpy.data.objects.new('OrganicLeaf',me); bpy.context.collection.objects.link(ob)
bpy.context.view_layer.objects.active=ob; ob.select_set(True)
uv=me.uv_layers.new(name='LeafUV')
for loop in me.loops: uv.data[loop.index].uv=uvs[loop.vertex_index]
for poly in me.polygons: poly.use_smooth=True
solid=ob.modifiers.new('LeafThickness','SOLIDIFY'); solid.thickness=.004
bpy.ops.object.modifier_apply(modifier=solid.name)
mat=bpy.data.materials.new('LeafPBR'); mat.use_nodes=True
bs=mat.node_tree.nodes.get('Principled BSDF')
bs.inputs['Base Color'].default_value=(.045,.24,.018,1)
bs.inputs['Roughness'].default_value=.48; bs.inputs['Metallic'].default_value=0
ob.data.materials.append(mat)
ob.shape_key_add(name='Basis'); bend=ob.shape_key_add(name='WindBend')
for v in bend.data:
    t=max(0,min(1,v.co.y/1.2)); v.co.z+=.20*t*t; v.co.x+=.055*t*t
for frame,value in [(1,0),(13,1),(25,0)]:
    bend.value=value; bend.keyframe_insert(data_path='value',frame=frame)
bend.value=0
scene=bpy.context.scene; scene.frame_start=1; scene.frame_end=25; scene.render.fps=24; scene.frame_set(1)
digest=hashlib.sha256()
for v in ob.data.vertices: digest.update(struct.pack('<3f',*v.co))
for p in ob.data.polygons:
    for v in p.vertices: digest.update(struct.pack('<I',v))
geometry_hash=digest.hexdigest()
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'organic_leaf.blend'))
bpy.ops.export_scene.gltf(filepath=str(OUT/'organic_leaf.glb'),export_format='GLB',use_selection=True,export_animations=True,export_animation_mode='ACTIONS',export_morph=True,export_apply=False,export_yup=True,export_cameras=False,export_lights=False)
receipt={'seed':SEED,'geometry_sha256':geometry_hash,'blender':bpy.app.version_string,'vertices':len(ob.data.vertices),'polygons':len(ob.data.polygons),'uv_layers':[x.name for x in ob.data.uv_layers],'thickness_m':.004,'animation':'WindBend morph 0 to 1 to 0, frames 1/13/25 at 24 fps','rendered':False}
if '--render' in sys.argv:
    scene.render.engine='CYCLES'; scene.cycles.device='CPU'; scene.cycles.samples=16
    scene.render.resolution_x=384; scene.render.resolution_y=384; scene.render.resolution_percentage=100
    bpy.ops.object.camera_add(location=(1,-1,1.65)); cam=bpy.context.object
    cam.rotation_euler=(Vector((0,.6,.05))-cam.location).to_track_quat('-Z','Y').to_euler(); scene.camera=cam
    bpy.ops.object.light_add(type='AREA',location=(0,-.2,2)); bpy.context.object.data.energy=120; bpy.context.object.data.shape='DISK'; bpy.context.object.data.size=2
    scene.world.color=(.2,.2,.2); scene.render.filepath=str(OUT/'reference_cpu.png'); bpy.ops.render.render(write_still=True); receipt['rendered']=True
(OUT/'generation.json').write_text(json.dumps(receipt,indent=2))
