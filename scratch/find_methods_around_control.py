with open(r'C:\Users\Chris\Downloads\SolidCraft-0.18.4-windows-x64.exe', 'rb') as f:
    data = f.read()

import re

# Look for method string matches in crates\ui-egui\src\control.rs
pos = data.find(b'crates\\ui-egui\\src\\control.rs')
start = max(0, pos - 15000)
end = min(len(data), pos + 15000)
chunk = data[start:end]

# Find string literals
strs = re.findall(rb'([a-zA-Z0-9_\.]{3,})', chunk)
print("Unique strings around control.rs:")
found = set()
for s in strs:
    try:
        t = s.decode('ascii')
        if any(w in t for w in ['control', 'command', 'screenshot', 'view', 'exec', 'run', 'eval', 'script', 'get', 'set', 'list', 'ping', 'status', 'pending', 'cancel']):
            found.add(t)
    except:
        pass

for f in sorted(found):
    print(" ", f)
