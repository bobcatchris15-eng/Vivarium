import sys, os
sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
from scratch.solidcraft import SolidCraftClient

client = SolidCraftClient()
cmds = client.commands()

interest = [
    'file.insert_mesh', 'file.export', 'file.save_mesh',
    'solid.loft', 'solid.sweep', 'solid.extrude', 'solid.revolve',
    'solid.combine', 'solid.fillet', 'solid.chamfer', 'mesh.convert',
    'sketch.create', 'sketch.spline', 'sketch.line', 'solid.box', 'solid.sphere'
]

for c in cmds:
    if c['id'] in interest:
        print(f"=== {c['id']} ===")
        print("Label:", c.get('label'))
        print("Params doc / schema:", c.get('params'))

client.close()
