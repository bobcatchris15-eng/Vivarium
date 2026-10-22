import sys, os
sys.path.insert(0, '.')
from scratch.solidcraft import SolidCraftClient

client = SolidCraftClient()
# Let's inspect the occurrence tree
occ = client.call("ui.inspect")
print("Hover:", occ.get("hover"))

# Let's find handles or points
handles = client.call("ui.handles")
print("Handles count:", len(handles))
for h in handles[:20]:
    print(h)

client.close()
