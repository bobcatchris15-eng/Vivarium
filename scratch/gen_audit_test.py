import os, json, glob, subprocess

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

public class PresetAuditTests
{
    private readonly ITestOutputHelper _output;
    public PresetAuditTests(ITestOutputHelper output) { _output = output; }

    [Fact]
    public void AuditAllPresets()
    {
        var content = ContentLoader.Load(TestUtil.ContentSource);
        foreach (var p in content.Presets.Values.OrderBy(x => x.PresetId))
        {
            var domain = new HexDomain(p.Diameter);
            var terrain = Heightfield.Generate(p, domain);
            var banksDesc = p.Clone();
            banksDesc.Terrain.Features.RemoveAll(f => f.BedHeight.HasValue);
            var banks = Heightfield.Generate(banksDesc, domain);

            // Edge wet
            int edgeCount = 0, wetEdgeCount = 0;
            for (int k = 0; k < 6; k++)
            {
                var v1 = domain.Vertices[k];
                var v2 = domain.Vertices[(k + 1) % 6];
                for (int i = 0; i <= 50; i++)
                {
                    var pt = v1 + (v2 - v1) * ((double)i / 50);
                    edgeCount++;
                    if (terrain.Height(pt) <= p.Water.WaterTable) wetEdgeCount++;
                }
            }
            double edgeWet = 100.0 * wetEdgeCount / edgeCount;

            // Channel cuts and uphill
            double minCut = 999, maxCut = 0;
            string uphill = "NONE";
            var channels = p.Terrain.Features.Where(f => f.Type == "channel" && f.BedHeight.HasValue).ToList();
            foreach (var ch in channels)
            {
                var a = new Vec2(ch.X, ch.Z);
                var b = new Vec2(ch.ToX, ch.ToZ);
                var ab = b - a;
                int count = (int)Math.Ceiling(ab.Length / 0.025);
                double prior = terrain.Height(a);
                for (int i = 1; i <= count; i++)
                {
                    var pt = a + ab * ((double)i / count);
                    if (!domain.Contains(pt)) break;
                    double h = terrain.Height(pt);
                    double cut = banks.Height(pt) - h;
                    if (cut < minCut) minCut = cut;
                    if (cut > maxCut) maxCut = cut;
                    if (h > p.Water.WaterTable && h - prior > 0.001) uphill = $"UPHILL {h - prior:0.000} at {pt}";
                    prior = h;
                }
            }

            // Potholes: basins with bedHeight
            var potholes = p.Terrain.Features.Where(f => f.Type == "basin" && f.BedHeight.HasValue).ToList();
            string potholeReport = string.Join("; ", potholes.Select(b => 
            {
                double bCut = banks.Height(new Vec2(b.X, b.Z)) - terrain.Height(new Vec2(b.X, b.Z));
                return $"R={b.Radius:0.0}m cut={bCut:0.00}m";
            }));

            _output.WriteLine($"{p.PresetId,-22}: EdgeWet={edgeWet,5:F1}% | Chs={channels.Count,2} Cuts=[{minCut:0.00}m..{maxCut:0.00}m] {uphill} | Potholes({potholes.Count}): {potholeReport}");
        }
    }
}
""";

with open("tests/Vivarium.Sim.Tests/PresetAuditTests.cs", "w") as f:
    f.write(cs_code)
print("PresetAuditTests.cs created")
