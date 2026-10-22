import sys, os
sys.path.insert(0, '.')
from scratch.solidcraft import SolidCraftClient

client = SolidCraftClient()

client.call("ui.view", {"view": "fit"})
ui = client.ui_inspect()
cam = ui['camera']
print("Fit camera:", cam)

# Take render of the fit camera
client.render("scratch/renders/fit_clean.png", 1280, 720)

client.close()
