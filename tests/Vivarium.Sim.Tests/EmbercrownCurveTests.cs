using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

public sealed class EmbercrownCurveTests
{
    [Theory]
    [InlineData(173UL)]
    [InlineData(1777UL)]
    [InlineData(9917UL)]
    public void TerminalUmbrellasSurviveAllGameTiers(ulong seed)
    {
        var species=TestUtil.Content.FloraOrThrow("embercrown");
        var top=OrganismMeshes.FloraTier(species,seed,0,tieredSource:true);
        var middle=OrganismMeshes.FloraTier(species,seed,1,tieredSource:true);
        var far=OrganismMeshes.FloraTier(species,seed,2,tieredSource:true);

        Assert.InRange(top.TriangleCount,1,OrganismMeshes.FloraTopTriangleCap);
        Assert.InRange(middle.TriangleCount,1,OrganismMeshes.FloraTierBudget(top.TriangleCount,1));
        Assert.InRange(far.TriangleCount,1,OrganismMeshes.FloraTierBudget(top.TriangleCount,2));
        // One guaranteed true terminal blade for each of eleven apex rachises
        // on seven canes, regardless of leaf-budget decimation.
        foreach(var tier in new[]{top,middle,far})
        {
            Assert.True(tier.EssentialSpans.Count>=77,
                $"Terminal crowns were culled: {tier.EssentialSpans.Count}/77 at {tier.TriangleCount} triangles.");
            Assert.All(tier.EssentialSpans,span=>
                Assert.True(span.FirstVertex>=0 && span.FirstVertex+span.VertexCount<=tier.VertexCount));
        }
        Assert.Equal(top.DigestHex(),
            OrganismMeshes.FloraTier(species,seed,0,tieredSource:true).DigestHex());
    }

    [Fact]
    public void FruitingShrubAlsoPreservesTerminalCrownsInGame()
    {
        var sp=TestUtil.Content.FloraOrThrow("embercrown");
        foreach(int tier in new[]{0,1,2})
        {
            var fruit=OrganismMeshes.FloraFruitingTier(sp,173UL,tier,tieredSource:true);
            Assert.NotNull(fruit);
            Assert.True(fruit!.EssentialSpans.Count>=77);
            Assert.InRange(fruit.TriangleCount,1,OrganismMeshes.FloraTopTriangleCap);
        }
    }

    [Fact]
    public void OrganicRachisesRetainAttachedLeafletsAndMatureDensity()
    {
        var sp=TestUtil.Content.FloraOrThrow("embercrown");
        var leafMesh=OrganismMeshes.Flora(sp,173,visualDetail:0);
        Assert.True(leafMesh.LeafSpans.Count>=2100);
        Assert.True(leafMesh.EssentialSpans.Count>=77);
        Assert.All(leafMesh.Positions,x=>Assert.True(float.IsFinite(x)));
        Assert.All(leafMesh.Normals,x=>Assert.True(float.IsFinite(x)));
        // Source fits the normal in-game geometry target; otherwise top crowns
        // would have to fight with central foliage for triangle allocation.
        Assert.InRange(leafMesh.TriangleCount,1,OrganismMeshes.FloraTopTriangleCap);
    }
}
