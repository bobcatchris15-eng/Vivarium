import sys, os
sys.path.insert(0, '.')
from scratch.solidcraft import SolidCraftClient

client = SolidCraftClient()

# Let's test camera views focused on the head and front legs
# The head is towards one end of the lizard
# Let's frame monitor_aural_cup_-1 or monitor_eye_-1
client.call("ui.camera", {"fit": ["monitor_eye_-1"], "pitch": 0.35, "yaw": -0.6})
client.render("scratch/renders/head_closeup.png", 1280, 720)

client.call("ui.camera", {"fit": ["monitor_claw_Foot_L0"], "pitch": 0.45, "yaw": -0.4})
client.render("scratch/renders/claw_closeup.png", 1280, 720)

client.call("ui.camera", {"fit": ["monitor_body"], "pitch": 0.5, "yaw": -0.7})
client.render("scratch/renders/body_closeup.png", 1280, 720)

print("Rendered closeups!")
client.close()
