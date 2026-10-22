import sys, os
sys.path.insert(0, '.')
from scratch.solidcraft import SolidCraftClient

client = SolidCraftClient()
ui = client.ui_inspect()
print("Camera:", ui.get("camera"))

# Let's frame the head specifically
# The head is around Z = -1.0 to -1.3 m (in GLTF metres, wait! In SolidCraft, GLTF metres become millimetres!)
# Wait! Let's check how SolidCraft scaled the GLTF:
# "and its metres millimetres" -> 1 metre in GLTF becomes 1 mm or 1000 mm?
# Let's check body bounding boxes in SolidCraft!
insp = client.inspect()
for b in insp.get("bodies", []):
    print("Body:", b.get("name"), "type:", b.get("type"))

client.close()
