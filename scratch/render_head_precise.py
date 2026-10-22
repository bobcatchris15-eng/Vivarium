import sys, os
sys.path.insert(0, '.')
from scratch.solidcraft import SolidCraftClient

client = SolidCraftClient()

# Let's target the head
client.call("ui.camera", {
    "target": [0.0, -1300.0, 0.0],
    "distance": 500.0,
    "pitch": 0.35,
    "yaw": 0.0
})
client.render("scratch/renders/head_true_1.png", 1280, 720)

client.call("ui.camera", {
    "target": [0.0, -1300.0, 0.0],
    "distance": 400.0,
    "pitch": 0.2,
    "yaw": -0.8
})
client.render("scratch/renders/head_true_angle.png", 1280, 720)

client.close()
