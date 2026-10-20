using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

public sealed class LanternsedgeDetailTests
{
    [Fact]
    public void LanternsedgeHasUniqueCompoundLeavesAndAnatomicalCulms()
    {
        var species=TestUtil.Content.FloraOrThrow("lanternsedge");
        var mesh=OrganismMeshes.Flora(species,0xBAD124UL);
        var glass=OrganismMeshes.Flora(TestUtil.Content.FloraOrThrow("glassrush"),0xBAD124UL);
        Assert.NotEqual(glass.DigestHex(),mesh.DigestHex());
        Assert.True(mesh.LeafSpans.Count>=95,
            $"Expected many toothed angelica-style leaflets, got {mesh.LeafSpans.Count}");
        Assert.True(mesh.Structural.Count>=50,
            $"Expected main culms, clasping sheaths and hundreds of branchlets: {mesh.Structural.Count}");
        Assert.InRange(mesh.TriangleCount,10000,30000);
        Assert.All(mesh.Positions,v=>Assert.True(float.IsFinite(v)));
        Assert.Equal(mesh.DigestHex(),OrganismMeshes.Flora(species,0xBAD124UL).DigestHex());
    }

    [Fact]
    public void DarkPurpleCulmsAndGlowingLampsKeepDifferentMaterialChannels()
    {
        var m=OrganismMeshes.Flora(TestUtil.Content.FloraOrThrow("lanternsedge"),7124);
        Assert.NotEmpty(m.Structural);
        var st=m.Structural[0];
        Assert.All(Enumerable.Range(st.FirstVertex,st.VertexCount),i=>
        {
            Assert.Equal(0f,m.UV2[i*2]);
            // Reddish-purple stem is visible instead of inherited glassrush green.
            Assert.True(m.Colors[i*4+2]>m.Colors[i*4+1]*1.3);
        });
        int heads=Enumerable.Range(0,m.VertexCount).Count(i=>
            m.UV2[i*2]>1.3f && m.UV2[i*2]<1.5f && m.UV2[i*2+1]>.5f);
        Assert.True(heads>700,$"No distinct luminous capsules: {heads}");
        Assert.True(heads<m.VertexCount/3);
    }

    [Fact]
    public void JuvenileRetainsFoliageButHasNoMatureLuminousHeads()
    {
        var sp=TestUtil.Content.FloraOrThrow("lanternsedge");
        var adult=OrganismMeshes.Flora(sp,1173);
        var young=OrganismMeshes.Flora(sp,1173,juvenile:true);
        Assert.True(young.LeafSpans.Count>5);
        Assert.True(young.LeafSpans.Count<adult.LeafSpans.Count);
        Assert.DoesNotContain(Enumerable.Range(0,young.VertexCount),
            i=>young.UV2[i*2]>1.3f && young.UV2[i*2]<1.5f);
    }

    [Fact]
    public void DedicatedModelFitsLegacyTierBudgets()
    {
        var sp=TestUtil.Content.FloraOrThrow("lanternsedge");
        var top=OrganismMeshes.FloraTier(sp,51722,0);
        var mid=OrganismMeshes.FloraTier(sp,51722,1);
        var low=OrganismMeshes.FloraTier(sp,51722,2);
        Assert.InRange(top.TriangleCount,1,OrganismMeshes.FloraTopTriangleCap);
        Assert.InRange(mid.TriangleCount,1,OrganismMeshes.FloraTierBudget(top.TriangleCount,1));
        Assert.InRange(low.TriangleCount,1,OrganismMeshes.FloraTierBudget(top.TriangleCount,2));
    }
}
