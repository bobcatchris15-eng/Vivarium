import os, subprocess, json

# We can run a dotnet test that evaluates the candidate cascading_falls configuration
cs_code = """
using System;
using System.Collections.Generic;
using System.Linq;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;
using Xunit;
using Xunit.Abstractions;

namespace Vivarium.Sim.Tests;

public class CascadesDesignerTests
{
    private readonly ITestOutputHelper _output;
    public CascadesDesignerTests(ITestOutputHelper output) { _output = output; }

    [Fact]
    public void SolveAndVerifyCascadingFalls()
    {
        var content = ContentLoader.Load(TestUtil.ContentSource);
        var p = content.PresetOrThrow("cascading_falls").Clone();
        
        p.Diameter = 8.0;
        p.Terrain.BaseHeight = 0.20;
        p.Terrain.Relief = 0.04;
        p.Terrain.NoiseScale = 0.16;
        p.Terrain.Octaves = 3;
        p.Terrain.SlopeX = -0.045;
        p.Terrain.SlopeZ = -0.015;
        p.Terrain.RockExposure = 0.35;
        p.Terrain.MinHeight = -1.2;
        p.Terrain.MaxHeight = 2.5;
        p.Terrain.Bottom = -3.5;
        
        // Clear features and add escarpment hills and ridges
        p.Terrain.Features.Clear();
        p.Terrain.Features.Add(new() { Type = "hill", X = -2.2, Z = -2.2, Radius = 2.5, Amount = 0.36 });
        p.Terrain.Features.Add(new() { Type = "hill", X = -1.8, Z =  0.0, Radius = 2.5, Amount = 0.36 });
        p.Terrain.Features.Add(new() { Type = "hill", X = -2.2, Z =  2.2, Radius = 2.5, Amount = 0.36 });

        // Escarpment ledge lip ridges flanking the waterfall plunge
        p.Terrain.Features.Add(new() { Type = "ridge", X = -0.25, Z = -3.2, ToX = -0.30, ToZ = -0.70, Width = 0.40, Amount = 0.20 });
        p.Terrain.Features.Add(new() { Type = "ridge", X = -0.30, Z = -0.10, ToX = -0.25, ToZ =  3.2, Width = 0.40, Amount = 0.20 });

        // Edge pond
        p.Terrain.Features.Add(new() { Type = "basin", X = 2.70, Z = 1.60, Radius = 1.80, Amount = 0.15, BedHeight = -0.12 });

        // Pothole 1 (at escarpment edge)
        var pothole1 = new TerrainFeature { Type = "basin", X = -0.45, Z = -0.40, Radius = 0.55, Amount = 0.12 };
        p.Terrain.Features.Add(pothole1);

        // Pothole 2 (at base of waterfall plunge)
        var pothole2 = new TerrainFeature { Type = "basin", X = 0.25, Z = -0.25, Radius = 0.60, Amount = 0.12 };
        p.Terrain.Features.Add(pothole2);

        // Channel waypoints
        var pts = new List<Vec2>
        {
            new(-2.60, -1.60), // Spring
            new(-2.10, -1.25),
            new(-1.60, -0.90),
            new(-1.05, -0.65),
            new(-0.45, -0.40), // Pothole 1 (edge of escarpment)
            new( 0.25, -0.25), // Pothole 2 (plunge pool at cliff base)
            new( 0.80, -0.10),
            new( 1.35,  0.15),
            new( 1.85,  0.50),
            new( 2.35,  0.95),
            new( 2.85,  1.45),
            new( 3.35,  1.95)  // In edge pond
        };

        p.Water.Springs.Clear();
        p.Water.Springs.Add(new() { X = pts[0].X, Z = pts[0].Z, DischargePerHour = 0.45 });

        // Uncut bank generator
        var domain = new HexDomain(p.Diameter);
        var uncutHf = Heightfield.Generate(p, domain);

        // Sample banks along waypoints
        var bankHeights = pts.Select(pt => uncutHf.Height(pt)).ToList();
        _output.WriteLine("Uncut bank heights along path:");
        for (int i = 0; i < pts.Count; i++)
        {
            _output.WriteLine($"  pt {i,2} ({pts[i].X,5:0.00}, {pts[i].Z,5:0.00}): bank = {bankHeights[i]:0.000}m");
        }

        // Set pothole bed heights to ~12cm cut
        pothole1.BedHeight = bankHeights[4] - 0.12;
        pothole2.BedHeight = bankHeights[5] - 0.12;

        // Channel cuts: target 0.065m to 0.08m
        // Ensure strictly decreasing downstream bed heights
        var beds = new double[pts.Count];
        beds[4] = pothole1.BedHeight.Value;
        beds[5] = pothole2.BedHeight.Value;
        beds[pts.Count - 1] = -0.12; // In edge pond

        // Upper reach: 0 to 4
        for (int i = 0; i < 4; i++)
        {
            // Bank minus ~0.07m
            beds[i] = bankHeights[i] - 0.075;
        }
        // Lower reach: 6 to 10
        for (int i = 6; i < pts.Count - 1; i++)
        {
            beds[i] = bankHeights[i] - 0.075;
        }

        // Ensure strictly decreasing bed heights with safety margin
        for (int i = pts.Count - 2; i >= 0; i--)
        {
            if (beds[i] <= beds[i + 1] + 0.015)
                beds[i] = beds[i + 1] + 0.015;
        }

        // Check cuts
        _output.WriteLine("Computed Bed Heights & Cuts:");
        for (int i = 0; i < pts.Count; i++)
        {
            double cut = bankHeights[i] - beds[i];
            _output.WriteLine($"  pt {i,2}: bank={bankHeights[i]:0.000}m, bed={beds[i]:0.000}m, cut={cut:0.000}m");
        }

        // Build channel segments
        for (int i = 0; i < pts.Count - 1; i++)
        {
            p.Terrain.Features.Add(new TerrainFeature
            {
                Type = "channel",
                X = pts[i].X,
                Z = pts[i].Z,
                ToX = pts[i + 1].X,
                ToZ = pts[i + 1].Z,
                Width = 0.20,
                Amount = 0.18,
                Sharp = true,
                BedHeight = beds[i],
                ToBedHeight = beds[i + 1]
            });
        }

        // Now generate full terrain and test
        var fullTerrain = Heightfield.Generate(p, domain);
        double maxChannelCut = 0;
        double minChannelCut = 999;
        string uphill = "NONE";

        foreach (var ch in p.Terrain.Features.Where(f => f.Type == "channel" && f.BedHeight.HasValue))
        {
            var a = new Vec2(ch.X, ch.Z);
            var b = new Vec2(ch.ToX, ch.ToZ);
            var ab = b - a;
            int count = (int)Math.Ceiling(ab.Length / 0.025);
            double prior = fullTerrain.Height(a);
            for (int i = 1; i <= count; i++)
            {
                var pt = a + ab * ((double)i / count);
                if (!domain.Contains(pt)) break;
                double h = fullTerrain.Height(pt);
                double uncut = uncutHf.Height(pt);
                double cut = uncut - h;
                if (cut > maxChannelCut) maxChannelCut = cut;
                if (cut < minChannelCut) minChannelCut = cut;
                if (h > p.Water.WaterTable && h - prior > 0.001)
                {
                    uphill = $"UPHILL {h - prior:0.000}m at ({pt.X:0.00},{pt.Z:0.00})";
                }
                prior = h;
            }
        }

        _output.WriteLine($"Result: minCut={minChannelCut:0.000}m, maxCut={maxChannelCut:0.000}m, uphill={uphill}");

        // Check edge wet percentage
        int edgeCount = 0, wetEdgeCount = 0;
        for (int k = 0; k < 6; k++)
        {
            var v1 = domain.Vertices[k];
            var v2 = domain.Vertices[(k + 1) % 6];
            for (int i = 0; i <= 50; i++)
            {
                var pt = v1 + (v2 - v1) * ((double)i / 50);
                edgeCount++;
                if (fullTerrain.Height(pt) <= p.Water.WaterTable) wetEdgeCount++;
            }
        }
        _output.WriteLine($"EdgeWet = {100.0 * wetEdgeCount / edgeCount:F1}%");

        // Check substrates along cliff
        var world = VivariumWorld.CreateBaseline(content, p);
        int cliffCells = 0, rockCells = 0;
        foreach (int idx in world.Grid.DomainCells)
        {
            var pt = world.Grid.CellCenter(idx);
            if (pt.X >= -0.5 && pt.X <= 0.2 && Math.Abs(pt.Z) <= 3.0)
            {
                cliffCells++;
                var sub = world.SubstrateAt(pt);
                if (sub == Substrate.Rock || sub == Substrate.Gravel) rockCells++;
            }
        }
        _output.WriteLine($"Cliff Rock/Gravel: {rockCells}/{cliffCells} ({100.0 * rockCells / cliffCells:F1}%)");
    }
}
""";

with open("tests/Vivarium.Sim.Tests/CascadesDesignerTests.cs", "w") as f:
    f.write(cs_code)
