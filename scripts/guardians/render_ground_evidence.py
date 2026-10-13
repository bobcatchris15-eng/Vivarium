"""Refresh evidence without rebuilding a stable GLB: Blender --python this -- species."""
import bpy,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parent))
from ground_art import ROOT,TASK,render_evidence
species=sys.argv[sys.argv.index('--')+1]
asset=ROOT/'game/assets/guardians'/('woodland_omnivore' if species=='coati' else 'great_ground')
bpy.ops.wm.open_mainfile(filepath=str(asset/'guardian.blend'))
rig=next(o for o in bpy.data.objects if o.type=='ARMATURE')
acts={a.name:a for a in bpy.data.actions}
render_evidence(species,rig,acts,TASK/species)
print('GROUND_EVIDENCE_COMPLETE '+species,flush=True)
