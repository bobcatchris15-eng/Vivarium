import json
import os
import subprocess

# Let's inspect the exact banks along each preset using a C# script run via dotnet
# Let's write a C# helper to calculate and output the exact bank values at arbitrary points
cs_helper = """
using System;
using System.IO;
using System.Text.Json;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

var content = ContentLoader.Load(new DirectoryContentSource("game/content"));
string presetId = args[0];
var d = content.PresetOrThrow(presetId);
var domain = new HexDomain(d.Diameter);
var banksDescriptor = d.Clone();
banksDescriptor.Terrain.Features.RemoveAll(f => f.BedHeight.HasValue);
var banks = Heightfield.Generate(banksDescriptor, domain);

for (int i = 1; i < args.Length; i += 2)
{
    double x = double.Parse(args[i]);
    double z = double.Parse(args[i+1]);
    Console.WriteLine($"{x},{z}:{banks.Height(new Vec2(x, z)):0.000}");
}
"""

with open("scratch/probe_bank.cs", "w") as f:
    f.write(cs_helper)

print("Created scratch/probe_bank.cs")
