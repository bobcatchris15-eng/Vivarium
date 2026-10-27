using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;
using Xunit.Abstractions;
namespace Vivarium.Sim.Tests;

public class PilotFungiOverhaulTests
{
    private readonly ITestOutputHelper output;
    public PilotFungiOverhaulTests(ITestOutputHelper output) => this.output=output;
    [Theory]
    [InlineData("basinwarden")]
    [InlineData("crowncoil")]
    [InlineData("gloomspire")]
    public void CrownLeavesCompileSeparatelyWithTipUvAndPreserveWorld(string id)
    {
        var descriptor=TestUtil.FlatDescriptor(76);descriptor.PilotTreeId=id;
        var world=VivariumWorld.CreateBaseline(TestUtil.Content,descriptor);
        var before=TestUtil.Digest(world);
        var mesh=PilotTreeMeshes.BuildTrunkAndCrown(world);
        var split=FloraVisualCompiler.Split(mesh);
        Assert.NotEmpty(mesh.Leaves);
        Assert.True(split.Leaf.TriangleCount>0);
        Assert.Equal(mesh.TriangleCount,split.Stem.TriangleCount+split.Leaf.TriangleCount);
        Assert.All(Enumerable.Range(0,split.Leaf.VertexCount),i=>Assert.Equal(0,split.Leaf.Colors[i*4+3]));
        Assert.All(Enumerable.Range(0,split.Stem.VertexCount),i=>Assert.True(split.Stem.Colors[i*4+3]>.2));
        Assert.Equal(split.Leaf.VertexCount*4,split.Leaf.Custom0.Count);
        Assert.Equal(split.Leaf.VertexCount*4,split.Leaf.Custom1.Count);
        if(id!="gloomspire")Assert.All(mesh.Leaves,leaf=>Assert.True(leaf.VertexCount>=490));
        foreach(var leaf in mesh.Leaves.Take(32))
        {
            var indices=Enumerable.Range(leaf.FirstVertex,leaf.VertexCount);
            var basePoints=indices.Where(i=>mesh.UV[i*2]<.01).ToArray();
            var tipPoints=indices.Where(i=>mesh.UV[i*2]>.99).ToArray();
            Assert.NotEmpty(basePoints);Assert.NotEmpty(tipPoints);
            Assert.True((mesh.Position(basePoints[0])-mesh.Position(tipPoints[0])).Length>leaf.Length*.5);
        }
        Assert.Equal(before,TestUtil.Digest(world));
        output.WriteLine($"{id}: {mesh.TriangleCount} triangles; {mesh.Leaves.Count} leaves");
    }

    [Theory]
    [InlineData("dewbonnet")]
    [InlineData("carrion_bell")]
    [InlineData("ghostbonnet")]
    public void LamellarFungiHaveActualVerticalGillsAndFullPileus(string id)
    {
        var mesh=OrganismMeshes.Flora(TestUtil.Content.FloraOrThrow(id),9112);
        var gills=Enumerable.Range(0,mesh.VertexCount).Where(i=>mesh.UV2[i*2]==1 && Math.Abs(mesh.NormalAt(i).Y)<.1).ToArray();
        Assert.True(gills.Length>=36*20);
        var fractions=gills.Select(i=>mesh.UV[i*2+1]).ToArray();
        Assert.Contains(fractions,f=>Math.Abs(f-.12)<.002);
        Assert.Contains(fractions,f=>Math.Abs(f-.53)<.002);
        Assert.All(mesh.Positions,f=>Assert.True(float.IsFinite(f)));
        Assert.Equal(mesh.DigestHex(),OrganismMeshes.Flora(TestUtil.Content.FloraOrThrow(id),9112).DigestHex());
        output.WriteLine($"{id}: {mesh.TriangleCount} triangles; {gills.Length} gill vertices");
    }

    [Theory]
    [InlineData("glass_antlers")]
    [InlineData("emberfan_fungus")]
    public void CurvedCoralAndPorousBracketsRemainDeterministicAndFinite(string id)
    {
        var mesh=OrganismMeshes.Flora(TestUtil.Content.FloraOrThrow(id),9112);
        Assert.True(mesh.TriangleCount>3000);
        Assert.All(mesh.Normals,f=>Assert.True(float.IsFinite(f)));
        Assert.Equal(mesh.DigestHex(),OrganismMeshes.Flora(TestUtil.Content.FloraOrThrow(id),9112).DigestHex());
        output.WriteLine($"{id}: {mesh.TriangleCount} triangles");
    }
}
