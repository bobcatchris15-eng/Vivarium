import socket
import json
import os
import sys

token_file = os.path.expandvars(r'%LOCALAPPDATA%\SolidCraft\control\7878.token')
with open(token_file, 'r') as f:
    token = f.read().strip()

print("Using token:", token[:8] + "...")

sock = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
sock.settimeout(10.0)
sock.connect(('127.0.0.1', 7878))
print("Connected to SolidCraft control server!")

reader = sock.makefile('r', encoding='utf-8')

# First message must carry token
req1 = {
    "id": 1,
    "method": "ui.inspect",
    "params": {},
    "token": token
}
sock.sendall((json.dumps(req1) + "\n").encode('utf-8'))
reply1 = reader.readline()
print("Reply 1:", reply1[:300])

# Second message: document.inspect
req2 = {
    "id": 2,
    "method": "document.inspect",
    "params": {}
}
sock.sendall((json.dumps(req2) + "\n").encode('utf-8'))
reply2 = reader.readline()
print("Reply 2:", reply2[:300])

sock.close()
