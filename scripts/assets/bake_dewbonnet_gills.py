"""Bake Dewbonnet radial lamellae into a seamless OpenGL tangent normal map.
U wraps around the cap, V runs centre to rim. Eighteen primary gills alternate
with eighteen shorter lamellulae. Python standard library only; deterministic.
"""
import math
from pathlib import Path
import struct
import zlib

WIDTH, HEIGHT = 2048, 256

def height(u, v):
    wave = math.cos(u * math.tau * 18)
    t = max(0, min(1, (v - .50) / .08))
    short = t * t * (3 - 2 * t)
    return max(0, wave) ** 6 + max(0, -wave) ** 6 * short

def chunk(tag, data):
    return struct.pack('>I', len(data)) + tag + data + struct.pack('>I', zlib.crc32(tag + data))

def bake():
    pixels = bytearray()
    for y in range(HEIGHT):
        pixels.append(0)
        v = y / (HEIGHT - 1)
        for x in range(WIDTH):
            u = x / WIDTH
            du, dv = 1 / WIDTH, 1 / HEIGHT
            # Relief height relative to cap radius. U's physical length is 2*pi*r.
            nx = -.035 * (height(u + du, v) - height(u - du, v)) / (2 * du * math.tau * max(v, .08))
            ny = -.035 * (height(u, v + dv) - height(u, v - dv)) / (2 * dv)
            length = math.sqrt(nx * nx + ny * ny + 1)
            pixels.extend(round((n / length * .5 + .5) * 255) for n in (nx, ny, 1))
    png = b'\x89PNG\r\n\x1a\n' + chunk(b'IHDR', struct.pack('>IIBBBBB', WIDTH, HEIGHT, 8, 2, 0, 0, 0))
    png += chunk(b'IDAT', zlib.compress(pixels, 9)) + chunk(b'IEND', b'')
    target = Path(__file__).resolve().parents[2] / 'game/Textures/Fungi/dewbonnet_gills_normal.png'
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_bytes(png)
    print(f'{target}: {WIDTH}x{HEIGHT}, {len(png)} bytes')

if __name__ == '__main__':
    bake()
