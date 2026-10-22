import sys, os
sys.path.insert(0, '.')
from scratch.solidcraft import SolidCraftClient

client = SolidCraftClient()

# Target center of body with 1800mm distance
client.call("ui.camera", {
    "target": [-307.4, -69.37, 739.46],
    "distance": 1800.0,
    "pitch": 0.45,
    "yaw": -0.6
})
client.render("scratch/renders/body_detailed.png", 1280, 720)

# Target the head
# Since the whole animal spans along Z: target Z was 739.46.
# If Z goes from -432 to +2644 mm (tail is high Z, head is low Z):
# Head should be at Z ~ -350 mm, X ~ 0, Y ~ -1250 mm!
client.call("ui.camera", {
    "target": [0.0, -50.0, -300.0],
    "distance": 800.0,
    "pitch": 0.4,
    "yaw": -0.5
})
client.render("scratch/renders/head_attempt2.png", 1280, 720)

client.close()
