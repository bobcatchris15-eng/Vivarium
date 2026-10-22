import sys, os
sys.path.insert(0, '.')
from scratch.solidcraft import SolidCraftClient

client = SolidCraftClient()
client.execute("file.new")

realistic_glb = os.path.abspath("scratch/monitor_realism/models/guardian_realistic.glb").replace('\\', '/')
print("Inserting realistic guardian:", realistic_glb)
res = client.execute("file.insert_mesh", {"path": realistic_glb, "up": "y"})
print("Insert result:", res)

# Multi-angle renders
client.call("ui.view", {"view": "fit"})
client.render("scratch/monitor_realism/renders/realistic_fit_iso.png", 1280, 720)

client.call("ui.view", {"view": "right"})
client.render("scratch/monitor_realism/renders/realistic_side.png", 1280, 720)

client.call("ui.view", {"view": "top"})
client.render("scratch/monitor_realism/renders/realistic_top.png", 1280, 720)

# Closeup of the head
client.call("ui.camera", {
    "target": [-307.4, -69.37, 739.46],
    "distance": 1800.0,
    "pitch": 0.45,
    "yaw": -0.6
})
client.render("scratch/monitor_realism/renders/realistic_body_detailed.png", 1280, 720)

# Window screenshot
client.screenshot("scratch/monitor_realism/renders/solidcraft_realistic_gui.png")

print("Saved SolidCraft realistic renders!")
client.close()
