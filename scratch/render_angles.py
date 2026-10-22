import sys, os
sys.path.insert(0, '.')
from scratch.solidcraft import SolidCraftClient

client = SolidCraftClient()

# Fit view
client.call("ui.view", {"view": "fit"})
client.render("scratch/renders/monitor_fit_iso.png", 1280, 720)

client.call("ui.view", {"view": "right"})
client.render("scratch/renders/monitor_side.png", 1280, 720)

client.call("ui.view", {"view": "top"})
client.render("scratch/renders/monitor_top.png", 1280, 720)

client.call("ui.view", {"view": "front"})
client.render("scratch/renders/monitor_front.png", 1280, 720)

# Window screenshot
client.screenshot("scratch/renders/solidcraft_gui.png")

print("Saved multi-angle renders and GUI screenshot!")
client.close()
