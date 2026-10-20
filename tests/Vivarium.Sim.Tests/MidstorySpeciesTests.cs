using System.Text.Json;
using Vivarium.Sim.Content;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

/// <summary>Protects the five raised middle-canopy architectures from being flattened into generic shrubs.</summary>
public sealed class MidstorySpeciesTests
{
    public static readonly string[] Names =
        { "splitcrown", "vaultfern", "rainshield", "lacemantle", "cragparasol" };

    [Fact]
    public void EveryMidstoryPlantHasAnEcologicalNicheAndIndependentRaisedCrown()
    {
        var content = ContentLoader.Load(TestUtil.ContentSource);
        foreach (var name in Names)
        {
            var sp = content.FloraOrThrow(name);
            Assert.Equal(FloraPlacementGroup.Woody, sp.PlacementGroup);
            Assert.NotNull(sp.Woody);
            Assert.True(sp.Woody!.CanopyRadius >= sp.RadiusAtMax);
            Assert.InRange(sp.Height, 1.1, 3);
            Assert.Contains("microcanopy", sp.Tags);
            var mesh = OrganismMeshes.Flora(sp, 48290);
            Assert.True(mesh.TriangleCount > 250, $"{name} has no meaningful geometry");
            Assert.True(mesh.TriangleCount < 120000, $"{name} has excessive triangle budget {mesh.TriangleCount}");
            Assert.True(mesh.LeafSpans.Count >= 8, $"{name} lacks separate leaves");
            Assert.All(mesh.Positions, n => Assert.True(float.IsFinite(n), $"{name} has nonfinite coordinate"));
            Assert.All(mesh.Normals, n => Assert.True(float.IsFinite(n), $"{name} has nonfinite normal"));
            var y = Enumerable.Range(0,mesh.VertexCount).Select(i=>mesh.Position(i).Y).ToArray();
            Assert.InRange(y.Min(), -.06, .08);
            Assert.True(y.Max() > .69, $"{name} does not raise foliage overhead (max y {y.Max():0.00})");
            double upperFraction=y.Count(v=>v>.45) / (double)y.Length;
            Assert.True(upperFraction>.4, $"{name} lacks elevated crown volume: {upperFraction:P1}");
            double maxReach=Enumerable.Range(0,mesh.VertexCount).Max(i=>
            {
                var p=mesh.Position(i);
                return Math.Sqrt(p.X*p.X+p.Z*p.Z);
            });
            Assert.True(maxReach>.60, $"{name} lacks lateral microcanopy reach");
            Assert.Equal(mesh.DigestHex(),OrganismMeshes.Flora(sp,48290).DigestHex());
        }
    }

    [Fact]
    public void MicrocanopiesHaveDifferentFormsAndCompactDistanceMeshes()
    {
        var content = ContentLoader.Load(TestUtil.ContentSource);
        var digests=new HashSet<string>(StringComparer.Ordinal);
        foreach(var name in Names)
        {
            var sp=content.FloraOrThrow(name);
            var adult=OrganismMeshes.Flora(sp,111,visualDetail:0);
            var far=OrganismMeshes.Flora(sp,111,visualDetail:2);
            var juvenile=OrganismMeshes.Flora(sp,111,juvenile:true,visualDetail:0);
            Assert.True(adult.TriangleCount>far.TriangleCount,$"{name} has no simplified distance mesh");
            Assert.True(juvenile.TriangleCount<adult.TriangleCount,$"{name} juvenile is not simpler");
            Assert.True(digests.Add(adult.DigestHex()),$"{name} reused another species mesh");
        }
    }

    [Fact]
    public void EveryPresetPlantsSuitableMidstorySpecies()
    {
        var root=Path.Combine(TestUtil.ContentDir,"presets");
        foreach(var path in Directory.EnumerateFiles(root,"*.json"))
        {
            using var document=JsonDocument.Parse(File.ReadAllText(path));
            var starters=document.RootElement.GetProperty("starterFlora").EnumerateArray();
            Assert.Contains(starters,x=> Names.Contains(x.GetProperty("species").GetString())
                && x.GetProperty("count").GetInt32()>=2);
        }
    }
}
