import os, json, glob, subprocess

# Let's write a C# one-liner or test method to inspect water table intersection at the edge
script = """
using System;
using System.Linq;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

var content = ContentLoader.Load("game/content");
foreach (var p in content.Presets.Values.OrderBy(x => x.PresetId))
{
    var domain = new HexDomain(p.Diameter);
    var hf = Heightfield.Generate(p, domain);
    int edgeCount = 0;
    int wetEdgeCount = 0;
    
    // Sample along the 6 edges
    for (int k = 0; k < 6; k++)
    {
        var v1 = domain.Vertices[k];
        var v2 = domain.Vertices[(k + 1) % 6];
        int steps = 50;
        for (int i = 0; i <= steps; i++)
        {
            var pt = v1 + (v2 - v1) * ((double)i / steps);
            double h = hf.Height(pt);
            edgeCount++;
            if (h <= p.Water.WaterTable) wetEdgeCount++;
        }
    }
    
    // Also total wet area on the domain
    int totalCells = 0;
    int wetCells = 0;
    for (int j = 0; j < hf.Nz; j++)
    for (int i = 0; i < hf.Nx; i++)
    {
        var pt = hf.VertexPos(i, j);
        if (!domain.Contains(pt)) continue;
        totalCells++;
        if (hf.Vertex(i, j) <= p.Water.WaterTable) wetCells++;
    }
    
    Console.WriteLine($"{p.PresetId,-22}: EdgeWet={100.0*wetEdgeCount/edgeCount:5.1f}% TotalWet={100.0*wetCells/totalCells:5.1f}% wt={p.Water.WaterTable:0.00}");
}
"""

with open("scratch/inspect_water.cs", "w") as f:
    f.write(script)
