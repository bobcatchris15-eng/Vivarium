import sys, os
sys.path.insert(0, '.')
from scratch.solidcraft import SolidCraftClient

client = SolidCraftClient()

# Start a new document
client.execute("file.new")

# Let's create a sketch on XY plane
client.execute("sketch.create", {"plane": "XY", "name": "HeadBase"})
# Create an anatomical monitor skull outline profile
# A varanid skull: narrow tapered snout tip, flaring back at temporal lobes
client.execute("sketch.spline", {
    "points": [
        [0.0, 120.0],    # Snout tip
        [18.0, 90.0],    # Anterior snout
        [32.0, 40.0],    # Pre-orbital constriction
        [48.0, 0.0],     # Orbital / temporal bulge
        [52.0, -30.0],   # Post-orbital skull roof
        [40.0, -70.0],   # Occipital corner
        [0.0, -80.0],    # Posterior skull base
        [-40.0, -70.0],
        [-52.0, -30.0],
        [-48.0, 0.0],
        [-32.0, 40.0],
        [-18.0, 90.0]
    ],
    "closed": True
})

# Extrude the skull base
client.execute("solid.extrude", {"distance": 45.0, "taper": -15.0})

# Render the result in SolidCraft
client.call("ui.view", {"view": "fit"})
client.render("scratch/renders/solidcraft_skull_test.png", 1280, 720)
print("Rendered SolidCraft skull test!")

client.close()
