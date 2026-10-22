with open(r'C:\Users\Chris\Downloads\SolidCraft-0.18.4-windows-x64.exe', 'rb') as f:
    data = f.read()

import re

# Find occurrences of crates\ui-egui\src\control.rs
matches = [m.start() for m in re.finditer(re.escape(b'crates\\ui-egui\\src\\control.rs'), data)]
print("Matches for control.rs:", len(matches))

for pos in matches:
    start = max(0, pos - 1000)
    end = min(len(data), pos + 1000)
    chunk = data[start:end]
    # Extract strings of length >= 3
    strs = re.findall(rb'[A-Za-z0-9_\.\-\:\/\\]{3,}', chunk)
    print("Pos:", pos)
    print("Strings:", [s.decode('latin1') for s in strs if '.' in s.decode('latin1') or '_' in s.decode('latin1')])
