import subprocess
import threading
import sys
import time

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

print(f"SolidCraft running with PID {proc.pid}", flush=True)

# Keep parent alive as long as child is running
while proc.poll() is None:
    time.sleep(1)

print(f"SolidCraft exited with code {proc.returncode}", flush=True)
