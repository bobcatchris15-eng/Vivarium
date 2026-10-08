using System.Diagnostics;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Xunit.Abstractions;

namespace Vivarium.Sim.Tests;

/// <summary>Times the per-species flora mesh builds the renderer runs (full/juvenile/fruit per LOD tier).</summary>
[Trait("Suite", "Geometry")]
[Trait("Speed", "Slow")]
public class FloraBuildTimingTests
{
    private readonly ITestOutputHelper _out;
    public FloraBuildTimingTests(ITestOutputHelper output) => _out = output;

    [Theory]
    [InlineData("ringreed")]
    [InlineData("rain_jelly")]
    [InlineData("snaptrap")]
    [InlineData("shadebell")]
    [InlineData("mooncoin")]
    [InlineData("pitcher_plant")]
    [InlineData("prismstar")]
    public void TimeSpeciesTiers(string id)
    {
        var sp = TestUtil.Content.Flora.First(f => f.Id == id);
        ulong seed = Rng.Mix(Hash.Fnv1a64("flora.visual." + sp.Id), 0x9E3779B97F4A7C15UL);
        for (int lod = 0; lod < OrganismMeshes.FloraLodTiers; lod++)
        {
            var sw = Stopwatch.StartNew();
            var full = sp.Climber != null ? OrganismMeshes.ClimberNodeTier(sp, seed, true, lod) : OrganismMeshes.FloraTier(sp, seed, lod, tieredSource: true);
            long tFull = sw.ElapsedMilliseconds; sw.Restart();
            var fruit = OrganismMeshes.FloraFruitingTier(sp, seed, lod, tieredSource: true);
            long tFruit = sw.ElapsedMilliseconds; sw.Restart();
            var young = sp.Climber != null ? OrganismMeshes.ClimberNodeTier(sp, seed, false, lod) : OrganismMeshes.FloraTier(sp, seed, lod, juvenile: true, tieredSource: true);
            _out.WriteLine($"{id} lod{lod} juvenile {sw.ElapsedMilliseconds}ms tris={young.TriangleCount}");
            _out.WriteLine($"{id} lod{lod} full {tFull}ms tris={full.TriangleCount} digest={full.DigestHex()} | fruit {tFruit}ms tris={fruit?.TriangleCount} digest={fruit?.DigestHex()}");
        }
    }
}

