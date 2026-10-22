with open(r'C:\Users\Chris\Downloads\SolidCraft-0.18.4-windows-x64.exe', 'rb') as f:
    data = f.read()

import re

# Find occurrences of 'mcp' or JSON-RPC methods
matches = re.findall(rb'\"([a-zA-Z0-9_\-\.\/]+)\"', data)
unique = set()
for m in matches:
    try:
        s = m.decode('ascii')
        if any(s.startswith(p) for p in ['engine.', 'ui.', 'view.', 'file.', 'doc.', 'document.', 'sketch.', 'solid.', 'mesh.', 'construct.', 'control.']):
            unique.add(s)
    except:
        pass

for u in sorted(unique):
    print(u)
