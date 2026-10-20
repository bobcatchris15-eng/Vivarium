using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

/// <summary>Moonfern-specific fine fountain constraints for original procedural rendering.</summary>
public sealed class MoonfernFountainTests
{
    [Theory]
    [InlineData(177UL)]
    [InlineData(8993UL)]
    [InlineData(15511UL)]
    public void MatureMoonfernBuildsManyNarrowDenselyPinnateFronds(ulong seed)
    {
        var sp=TestUtil.Content.FloraOrThrow("moonfern");
        var m=OrganismMeshes.Flora(sp,seed);
        // One substantial rachis tube per mature or emerging frond; exclude
        // short basal ramenta and tiny capillary pinna petiolules.
        int fronds=m.Structural.Count(s=>s.VertexCount>=45 && s.Radius>.004);
        Assert.InRange(fronds,17,19);
        Assert.True(m.LeafSpans.Count>=650,
            $"Expected fine, heavily pinnate foliage: {m.LeafSpans.Count}");
        Assert.All(m.Positions,v=>Assert.True(float.IsFinite(v)));
        Assert.All(m.Normals,v=>Assert.True(float.IsFinite(v)));
        Assert.Equal(m.DigestHex(),OrganismMeshes.Flora(sp,seed).DigestHex());
    }

    [Fact]
    public void MediumAndFarMoonfernTiersRetainFeatheryFoliage()
    {
        var sp=TestUtil.Content.FloraOrThrow("moonfern");
        var near=OrganismMeshes.FloraTier(sp,177UL,0);
        var middle=OrganismMeshes.FloraTier(sp,177UL,1);
        var far=OrganismMeshes.FloraTier(sp,177UL,2);
        Assert.InRange(near.TriangleCount,1,OrganismMeshes.FloraTopTriangleCap);
        Assert.InRange(middle.TriangleCount,1,OrganismMeshes.FloraTierBudget(near.TriangleCount,1));
        Assert.InRange(far.TriangleCount,1,OrganismMeshes.FloraTierBudget(near.TriangleCount,2));
        Assert.True(middle.LeafSpans.Count>=near.LeafSpans.Count*.7,
            $"Middle tier lost too many fine pinnae: {middle.LeafSpans.Count}/{near.LeafSpans.Count}");
        Assert.True(far.LeafSpans.Count>=near.LeafSpans.Count*.7,
            $"Far tier lost feathery texture: {far.LeafSpans.Count}/{near.LeafSpans.Count}");
        Assert.True(near.TriangleCount>middle.TriangleCount*2);
    }

    [Fact]
    public void YoungMoonfernKeepsFiddleheadsWithoutAdultTriangleCost()
    {
        var sp=TestUtil.Content.FloraOrThrow("moonfern");
        var young=OrganismMeshes.Flora(sp,991UL,juvenile:true);
        var adult=OrganismMeshes.Flora(sp,991UL);
        Assert.True(young.Structural.Count<adult.Structural.Count);
        Assert.True(young.TriangleCount<adult.TriangleCount);
        Assert.True(young.Bounds().Max.Y<adult.Bounds().Max.Y);
    }
}
