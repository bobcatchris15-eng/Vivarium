"""Idempotent batch-only repairs for the pinned official MIT source; no installed binaries changed."""
import argparse,hashlib,json
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('source');args=p.parse_args();root=Path(args.source).resolve();records=[]
patches=[
 ('material_maker/widgets/float_edit/float_edit.gd','static var sensitivity : float = mm_globals.get_config("ui_field_sensitivity")','static var sensitivity : float = 1.0 if "--export-material" in OS.get_cmdline_args() else mm_globals.get_config("ui_field_sensitivity")'),
 ('material_maker/steam.gd','func _ready():\n','func _ready():\n\tif "--export-material" in OS.get_cmdline_args():\n\t\treturn\n'),
 ('parse_args.gd','\t\tprint("Exporting...")','\t\tfor frame in range(120):\n\t\t\tif mm_renderer.rendering_device != null:\n\t\t\t\tbreak\n\t\t\tawait get_tree().process_frame\n\t\tif mm_renderer.rendering_device == null:\n\t\t\tpush_error("Batch rendering device did not initialize")\n\t\t\tget_tree().quit(3)\n\t\t\treturn\n\t\tprint("Exporting...")'),
 ('parse_args.gd','\tget_tree().quit()','\tawait mm_renderer.stop_rendering_thread()\n\tget_tree().quit()'),
 ('parse_args.gd','\t\tawait export_files(expanded_files, output_dir, target, output_file, image_size)','\t\tif texture_size > 0:\n\t\t\timage_size = texture_size\n\t\tawait export_files(expanded_files, output_dir, target, output_file, image_size)')]
for rel,old,new in patches:
 path=root/rel;s=path.read_text();before=hashlib.sha256(path.read_bytes()).hexdigest()
 if new in s:status='already-applied'
 elif old in s:path.write_text(s.replace(old,new,1));status='applied'
 else:raise SystemExit('Unexpected source: reconcile '+rel+' without resetting it')
 records.append({'file':rel,'status':status,'before_sha256':before,'after_sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
(root.parent/'batch-repair-receipt.json').write_text(json.dumps({'scope':'batch-only pinned Material Maker source startup, size and shutdown; MIT license retained','patches':records},indent=2));print(json.dumps(records,indent=2))
