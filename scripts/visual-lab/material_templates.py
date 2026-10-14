"""Derive editable MIT Material Maker graph recipes; validate exported appearance in game separately."""
import argparse,json,hashlib
from pathlib import Path
parser=argparse.ArgumentParser();parser.add_argument('--source',required=True);parser.add_argument('--out',required=True);a=parser.parse_args()
source=Path(a.source);out=Path(a.out);out.mkdir(parents=True,exist_ok=True)
examples = source/'material_maker/examples' if (source/'material_maker/examples').is_dir() else source/'examples'
recipes={'bark':('wood',(0.55,0.40,0.24),.85),'leaves':('dry_earth',(.16,.38,.075),.55),
 'soil':('dry_earth',(.55,.40,.28),.90),'moss':('dry_earth',(.18,.36,.09),.92),
 'fungi':('dry_earth',(.65,.56,.40),.82),'stones':('rock',(.55,.54,.50),.88),
 'chitin':('crocodile_skin',(.28,.18,.09),.48),'decaying_wood':('wood',(.32,.27,.20),.96),
 'wet_surfaces':('dry_earth',(.30,.25,.20),.22)}
manifest=[]
for name,(example,tint,roughness) in recipes.items():
 p=examples/(example+'.ptex');graph=json.loads(p.read_text());found=False
 for node in graph.get('nodes',[]):
  if node.get('type')=='material':
   params=node.setdefault('parameters',{});params['metallic']=0.0;params['roughness']=roughness
   params['albedo_color']={'type':'Color','r':tint[0],'g':tint[1],'b':tint[2],'a':1};found=True
 if not found:raise ValueError('No material output in '+str(p))
 dest=out/(name+'.ptex');dest.write_text(json.dumps(graph,indent=2))
 manifest.append({'recipe':name,'source_example':str(p),'source_sha256':hashlib.sha256(p.read_bytes()).hexdigest(),
  'recipe_sha256':hashlib.sha256(dest.read_bytes()).hexdigest(),'license':'MIT Material Maker example graph; original modifications',
  'status':'editable starting recipe; anatomy/material identity/appearance unaccepted'})
(out/'manifest.json').write_text(json.dumps(manifest,indent=2));print('Generated',len(manifest),'editable starting recipes')
