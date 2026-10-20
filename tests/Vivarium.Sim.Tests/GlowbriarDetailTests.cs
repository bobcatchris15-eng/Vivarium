using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

/// <summary>Contracts for the accepted legacy Glowbriar, not the experimental GLB overhaul.</summary>
public sealed class GlowbriarDetailTests
{
    [Fact]
    public void SpiralCaneHierarchySupportsHundredsOfDistinctSpearLeaves()
    {
        var species=TestUtil.Content.FloraOrThrow("glowbriar");
        var mature=OrganismMeshes.Flora(species,0xB12AUL);
        var young=OrganismMeshes.Flora(species,0xB12AUL,juvenile:true);
        Assert.True(mature.LeafSpans.Count>=300,
            $"Expected a densely foliated tertiary canopy: {mature.LeafSpans.Count}");
        Assert.True(mature.LeafSpans.Count>young.LeafSpans.Count*2);
        Assert.True(mature.Structural.Count>=40);
        Assert.Equal(mature.DigestHex(),OrganismMeshes.Flora(species,0xB12AUL).DigestHex());
        Assert.InRange(mature.TriangleCount,10000,35000);
        Assert.All(mature.Positions,value=>Assert.True(float.IsFinite(value)));
    }

    [Fact]
    public void StemSectionIsSubtlyFlutedAndOnlyPendantNodulesHaveGlowChannel()
    {
        var mesh=OrganismMeshes.Flora(TestUtil.Content.FloraOrThrow("glowbriar"),5278);
        var woody=mesh.Structural[0];
        int segments=woody.Sides;
        var centre=new Vivarium.Sim.Core.Vec3(0,0,0);
        for(int i=0;i<segments;i++)centre+=mesh.Position(woody.FirstVertex+i);
        centre/=segments;
        var width=Enumerable.Range(0,segments).Select(i=>
            (mesh.Position(woody.FirstVertex+i)-centre).Length).ToArray();
        Assert.True(width.Max()/width.Min()>1.045,
            "A stem should have an oval/fluted section, not a perfect manufactured cylinder.");
        Assert.True(width.Max()/width.Min()<1.20,
            "Honeysuckle-like woody shafts should remain predominantly rounded.");
        Assert.All(Enumerable.Range(woody.FirstVertex,woody.VertexCount),i=>
            Assert.Equal(2f,mesh.UV2[i*2]));
        int emitters=Enumerable.Range(0,mesh.VertexCount).Count(i=>
            mesh.UV2[i*2] is >1.30f and <1.50f);
        Assert.True(emitters>100);
        Assert.True(emitters<mesh.VertexCount/4);
    }

    [Fact]
    public void ExistingFloraTierBudgetsStillBoundTheDenseShrub()
    {
        var species=TestUtil.Content.FloraOrThrow("glowbriar");
        var high=OrganismMeshes.FloraTier(species,8854,0);
        var medium=OrganismMeshes.FloraTier(species,8854,1);
        var far=OrganismMeshes.FloraTier(species,8854,2);
        Assert.True(high.LeafSpans.Count>medium.LeafSpans.Count);
        Assert.True(medium.LeafSpans.Count>far.LeafSpans.Count);
        Assert.InRange(high.TriangleCount,1,OrganismMeshes.FloraTopTriangleCap);
        Assert.InRange(medium.TriangleCount,1,OrganismMeshes.FloraTierBudget(high.TriangleCount,1));
        Assert.InRange(far.TriangleCount,1,OrganismMeshes.FloraTierBudget(high.TriangleCount,2));
    }
}
