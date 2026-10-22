import sys, os
sys.path.insert(0, '.')
from scratch.solidcraft import SolidCraftClient

client = SolidCraftClient()
client.execute("file.new")
client.execute("sketch.create", {"plane": "XY", "name": "SkullBase"})

# Fit point spline for realistic monitor skull
points = [
    [0.0, 140.0],    # Snout apex
    [22.0, 100.0],   # Anterior rostrum
    [36.0, 40.0],    # Canthal narrowing
    [54.0, -10.0],   # Jugal / postorbital width
    [56.0, -50.0],   # Temporal muscle ridge
    [38.0, -80.0],   # Squamosal corner
    [0.0, -90.0],    # Occipital condyle base
    [-38.0, -80.0],
    [-56.0, -50.0],
    [-54.0, -10.0],
    [-36.0, 40.0],
    [-22.0, 100.0]
]

res = client.execute("sketch.spline.fit_point", {"points": points, "periodic": True})
print("Spline created:", res)

# Extrude skull base
ext = client.execute("solid.extrude", {"distance": 45.0, "taper": -4.0})
print("Extrude result:", ext)

client.call("ui.view", {"view": "iso"})
client.render("scratch/renders/skull_spline.png", 1280, 720)
print("Rendered skull_spline.png!")

client.close()
