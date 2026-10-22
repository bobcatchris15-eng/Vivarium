import sys, os
sys.path.insert(0, '.')
from scratch.solidcraft import SolidCraftClient

client = SolidCraftClient()

# Get current camera
ui = client.ui_inspect()
cam = ui['camera']
print("Starting camera:", cam)

# Zoom in 5x (factor 0.2)
res = client.execute("view.navigate", {
    "camera": cam,
    "motion": "zoom",
    "factor": 0.25
})
print("Zoomed camera:", res.get("camera"))
client.render("scratch/renders/zoomed_1.png", 1280, 720)

# Let's pan towards the head
res2 = client.execute("view.navigate", {
    "camera": res['camera'],
    "motion": "pan",
    "delta": [0, -180],
    "viewport": [1280, 720]
})
client.render("scratch/renders/head_panned.png", 1280, 720)

client.close()
