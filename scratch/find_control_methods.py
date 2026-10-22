with open(r'C:\Users\Chris\Downloads\SolidCraft-0.18.4-windows-x64.exe', 'rb') as f:
    data = f.read()

import re

# Search around crates/ui-egui/src/control.rs
matches = [m.start() for m in re.finditer(re.escape(b'control.rs'), data)]
for pos in matches:
    start = max(0, pos - 3000)
    end = min(len(data), pos + 3000)
    chunk = data[start:end]
    # find all ascii strings >= 4 chars
    strs = re.findall(rb'[A-Za-z0-9_\.]{4,}', chunk)
    decoded = [s.decode('latin1') for s in strs]
    filtered = [s for s in decoded if any(k in s for k in ['control', 'method', 'token', 'request', 'response', 'params', 'result', 'error', 'dispatch', 'eval', 'script', 'command', 'view', 'screenshot'])]
    if filtered:
        print(f"=== Match at {pos} ===")
        print(filtered[:30])
