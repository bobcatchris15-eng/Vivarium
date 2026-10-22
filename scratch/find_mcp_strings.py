with open(r'C:\Users\Chris\Downloads\SolidCraft-0.18.4-windows-x64.exe', 'rb') as f:
    data = f.read()

import re

pos = data.find(b'crates/mcp/src/server.rs')
print("pos:", pos)
if pos != -1:
    start = max(0, pos - 5000)
    end = min(len(data), pos + 5000)
    chunk = data[start:end]
    strs = re.findall(rb'[A-Za-z0-9_\-\.\:\/]{3,}', chunk)
    print("Found strings around mcp/src/server.rs:")
    for s in strs:
        t = s.decode('latin1')
        if any(k in t.lower() for k in ['tool', 'mcp', 'call', 'schema', 'method', 'command', 'execute', 'run', 'eval', 'cadcraft', 'solidcraft']):
            print(" ", t)
