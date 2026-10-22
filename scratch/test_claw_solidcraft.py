import sys, os
sys.path.insert(0, '.')
from scratch.solidcraft import SolidCraftClient

client = SolidCraftClient()
client.execute("file.new")

# Create a claw base sketch on YZ plane
client.execute("sketch.create", {"plane": "YZ", "name": "ClawProfile"})

# An egg/teardrop shaped cross section: sharp top ridge, rounded bottom
# In YZ plane, Y is forward/back, Z is up/down (or X/Y of sketch)
# Sketch coordinates: X is width, Y is height
pts = [
    [0.0, 10.0],    # Sharp dorsal ridge apex
    [2.2, 5.0],     # Upper lateral flank
    [3.0, 0.0],     # Mid lateral width
    [2.0, -4.0],    # Lower lateral flank
    [0.0, -5.0],    # Ventral groove
    [-2.0, -4.0],
    [-3.0, 0.0],
    [-2.2, 5.0]
]
client.execute("sketch.spline.fit_point", {"points": pts, "periodic": True})

# Extrude with taper to form a curved claw tip
# Or extrude along X
ext = client.execute("solid.extrude", {"distance": 15.0, "taper": -2.0})
print("Claw body:", ext)

client.call("ui.view", {"view": "iso"})
client.render("scratch/monitor_realism/renders/solidcraft_claw_raw.png", 800, 600)

# Export the claw as OBJ / STL
stl_path = os.path.abspath("scratch/monitor_realism/models/claw.obj").replace('\\', '/')
client.execute("file.export", {"path": stl_path, "format": "obj"})
print("Exported claw to", stl_path)

client.close()
