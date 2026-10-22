import sys, os
sys.path.insert(0, '.')
from scratch.solidcraft import SolidCraftClient

client = SolidCraftClient()

# Target the head
# Head is at roughly (0, 300, -1100) or (0, -1100, 300) in mm
# Let's try distance 800 mm, pitch 0.3, yaw -0.5
client.call("ui.camera", {
    "target": [0.0, -1100.0, 300.0],
    "distance": 600.0,
    "pitch": 0.4,
    "yaw": -0.5
})
client.render("scratch/renders/head_closeup_1.png", 1280, 720)

# Target the limbs / torso
client.call("ui.camera", {
    "target": [0.0, -300.0, 250.0],
    "distance": 1200.0,
    "pitch": 0.6,
    "yaw": -0.8
})
client.render("scratch/renders/torso_closeup_1.png", 1280, 720)

# Target the front feet / claws
client.call("ui.camera", {
    "target": [200.0, -500.0, 100.0],
    "distance": 500.0,
    "pitch": 0.5,
    "yaw": -0.4
})
client.render("scratch/renders/foot_closeup_1.png", 1280, 720)

client.close()
