import json, math

# Implement exact C# RawHeight and Noise logic to simulate Vivarium terrain
class Vec2:
    def __init__(self, x, z):
        self.x = float(x)
        self.z = float(z)
    def __add__(self, o): return Vec2(self.x + o.x, self.z + o.z)
    def __sub__(self, o): return Vec2(self.x - o.x, self.z - o.z)
    def __mul__(self, s): return Vec2(self.x * s, self.z * s)
    @property
    def length_sq(self): return self.x * self.x + self.z * self.z
    @property
    def length(self): return math.sqrt(self.length_sq)
    def dot(self, o): return self.x * o.x + self.z * o.z
    def dist(self, o): return (self - o).length

def clamp01(v): return max(0.0, min(1.0, v))
def clamp(v, lo, hi): return max(lo, min(hi, v))

# We can call dotnet test to verify exactly, but this script allows fast iteration on cascading_falls!
