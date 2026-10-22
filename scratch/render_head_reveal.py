import sys, os
sys.path.insert(0, '.')
from scratch.solidcraft import SolidCraftClient

client = SolidCraftClient()

# Render head from front and three-quarters
client.call("ui.camera", {
    "target": [0.0, -100.0, -850.0],
    "distance": 650.0,
    "pitch": 0.35,
    "yaw": -0.6
})
client.render("scratch/renders/head_reveal.png", 1280, 720)

client.call("ui.camera", {
    "target": [0.0, -100.0, -950.0],
    "distance": 450.0,
    "pitch": 0.25,
    "yaw": 0.0
})
client.render("scratch/renders/head_snout.png", 1280, 720)

client.close()
