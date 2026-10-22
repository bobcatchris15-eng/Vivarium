import subprocess
import threading
import sys

proc = subprocess.Popen(
    [r'C:\Users\Chris\Downloads\SolidCraft-0.18.4-windows-x64.exe', '--control', '7878'],
    stdout=subprocess.PIPE,
    stderr=subprocess.PIPE,
    text=True,
    bufsize=1
)

def read_stream(stream, name):
    for line in stream:
        print(f"[{name}] {line.strip()}", flush=True)

t1 = threading.Thread(target=read_stream, args=(proc.stdout, "OUT"))
t2 = threading.Thread(target=read_stream, args=(proc.stderr, "ERR"))
t1.daemon = True
t2.daemon = True
t1.start()
t2.start()

print(f"Started PID {proc.pid}")
try:
    ret = proc.wait(timeout=10)
    print("Process exited with return code:", ret)
except subprocess.TimeoutExpired:
    print("Process is still running after 10s!")
