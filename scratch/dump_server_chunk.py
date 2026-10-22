with open(r'C:\Users\Chris\Downloads\SolidCraft-0.18.4-windows-x64.exe', 'rb') as f:
    data = f.read()

pos = 90884676
start = pos - 800
end = pos + 800
chunk = data[start:end]
print("".join(chr(b) if 32 <= b <= 126 else f"\\x{b:02x}" for b in chunk))
