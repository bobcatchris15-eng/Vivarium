with open(r'C:\Users\Chris\Downloads\SolidCraft-0.18.4-windows-x64.exe', 'rb') as f:
    data = f.read()

import re

matches = [m.start() for m in re.finditer(re.escape(b'control_server.rs'), data)]
print("Matches for control_server.rs:", len(matches))
for pos in matches:
    start = max(0, pos - 1500)
    end = min(len(data), pos + 1500)
    chunk = data[start:end]
    strs = re.findall(rb'[A-Za-z0-9_\-\.\:\/]{3,}', chunk)
    print("Pos:", pos)
    print([s.decode('latin1') for s in strs if any(c in s.decode('latin1').lower() for c in ['token', 'port', 'method', 'json', 'client', 'server', 'auth'])])
