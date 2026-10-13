"""Independent Blender evaluation of authored clips and contact surfaces.
Run Blender --background --python this_file -- coati|monitor.
This tests evaluated meshes at subframes, not only analytic IK targets.
"""
import bpy, sys, json, math, os
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
species=sys.argv[sys.argv.index('--')+1]
folder=ROOT/'.clanker/tasks'/os.environ.get('GROUND_ART_TASK_ID','guardian-ground-art')/species
asset=ROOT/'game/assets/guardians'/('woodland_omnivore' if species=='coati' else 'great_ground')
bpy.ops.wm.open_mainfile(filepath=str(asset/'guardian.blend'))
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
meshobjects=[o for o in bpy.data.objects if o.type=='MESH']
solegroups={}
weight_error=0
for ob in meshobjects:
    names={g.index:g.name for g in ob.vertex_groups}
    soles={n:[] for n in ['Hand_L','Hand_R','Foot_L','Foot_R']}
    for v in ob.data.vertices:
        total=sum(g.weight for g in v.groups); weight_error=max(weight_error,abs(total-1))
        for g in v.groups:
            n=names[g.group]
            if n in soles and g.weight>.995: soles[n].append(v.index)
    solegroups[ob.name]=soles
report={'maximum_vertex_weight_sum_error':weight_error,'clips':{}}
for action in bpy.data.actions:
    if action.name not in ['idle','walk','feed','rest']: continue
    rig.animation_data.action=action
    start,end=action.frame_range; min_y=1e9; max_foot_delta=0; max_mesh_span=0; finite=True
    snapshots=[]
    # Include half-frame interpolation in contact validation.
    for f in [start+(end-start)*i/16+.5 for i in range(16)]+[end]:
        bpy.context.scene.frame_set(int(f),subframe=f-int(f)); deps=bpy.context.evaluated_depsgraph_get()
        feet={n:1e9 for n in ['Hand_L','Hand_R','Foot_L','Foot_R']}
        for ob in meshobjects:
            ev=ob.evaluated_get(deps); me=ev.to_mesh()
            for v in me.vertices:
                finite=finite and all(math.isfinite(x) for x in v.co)
                min_y=min(min_y,v.co.y)
            for n,indices in solegroups[ob.name].items():
                if indices: feet[n]=min(feet[n],min(me.vertices[i].co.y for i in indices))
            ev.to_mesh_clear()
        snapshots.append({'frame':round(f,4),'feet_min_y_m':feet})
    report['clips'][action.name]={'all_vertices_finite':finite,'lowest_vertex_y_m':min_y,'snapshots':snapshots}
report['limitations']=['Subframe checks use editable source with Blender skinning; Godot and terrain alignment remain root integration gates.','Foot minima include curved digits/claws, not only ground-facing sole vertices.']
(folder/'deformation-audit.json').write_text(json.dumps(report,indent=2))
print('DEFORMATION_AUDIT '+json.dumps(report),flush=True)
