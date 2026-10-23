using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

/// <summary>
/// Species-specific contracts for the accepted procedural Shadebell, not the
/// superseded authored botanical leaf assets.
/// </summary>
public sealed class ShadebellRhubarbCanopyTests
{
    [Theory]
    [InlineData(441UL)]
    [InlineData(8854UL)]
    [InlineData(19991UL)]
    public void BroadHorizontalWaxyCanopyFitsTopTierBudget(ulong seed)
    {
        var species=TestUtil.Content.FloraOrThrow("shadebell");
        var mesh=OrganismMeshes.Flora(species,seed);
        Assert.InRange(mesh.TriangleCount,18000,OrganismMeshes.FloraTopTriangleCap);
        Assert.InRange(mesh.LeafSpans.Count,145,195);
        Assert.Equal(mesh.DigestHex(),OrganismMeshes.Flora(species,seed).DigestHex());
        Assert.All(mesh.Positions,x=>Assert.True(float.IsFinite(x)));

        int horizontal=0;
        int broad=0;
        foreach(var (start,count) in mesh.LeafSpans)
        {
            var indices=Enumerable.Range(start,count).Where(i=>
                mesh.UV2[2*i]<.5f && mesh.UV2[2*i+1]>.5f).ToArray();
            if(indices.Length==0)continue;
            var upright=indices.Count(i=>mesh.NormalAt(i).Y>.57);
            if(upright>=indices.Length*.63)horizontal++;
            var minX=indices.Min(i=>mesh.Position(i).X);
            var maxX=indices.Max(i=>mesh.Position(i).X);
            var minZ=indices.Min(i=>mesh.Position(i).Z);
            var maxZ=indices.Max(i=>mesh.Position(i).Z);
            var minY=indices.Min(i=>mesh.Position(i).Y);
            var maxY=indices.Max(i=>mesh.Position(i).Y);
            if(Math.Max(maxX-minX,maxZ-minZ)>(maxY-minY)*1.7)
                broad++;
        }
        Assert.True(horizontal>=mesh.LeafSpans.Count*.68,
            $"Shadebell laminae should face upward as shade shelves: {horizontal}/{mesh.LeafSpans.Count}");
        Assert.True(broad>=mesh.LeafSpans.Count*.76,
            $"Canopy leaves should spread laterally, not hang as spears: {broad}/{mesh.LeafSpans.Count}");
    }

    [Fact]
    public void UnderstoryBellRacemesAreDenseAndPendent()
    {
        var species=TestUtil.Content.FloraOrThrow("shadebell");
        var mesh=OrganismMeshes.Flora(species,8854UL);
        int paleCupVertices=Enumerable.Range(0,mesh.VertexCount).Count(i=>
            mesh.Colors[i*4]>.77f &&
            mesh.Colors[i*4+1]>.68f &&
            mesh.Colors[i*4+2]>.58f);
        Assert.True(paleCupVertices>=2700,
            $"Expected 11 pendulous pale blossoms per fertile shelf, got {paleCupVertices} pale cup vertices.");
        Assert.True(paleCupVertices<mesh.VertexCount/2);
        Assert.True(mesh.Bounds().Min.Y>=-.03);
        Assert.True(mesh.Bounds().Max.Y<=1.20);
    }

    [Fact]
    public void JuvenileStillHasHorizontalBroadleavesAndNoAdultFlowerRacemes()
    {
        var species=TestUtil.Content.FloraOrThrow("shadebell");
        var mature=OrganismMeshes.Flora(species,9000UL);
        var young=OrganismMeshes.Flora(species,9000UL,juvenile:true);
        Assert.True(young.LeafSpans.Count>25);
        Assert.True(young.LeafSpans.Count<mature.LeafSpans.Count);
        Assert.True(young.TriangleCount<mature.TriangleCount);
        Assert.True(young.TriangleCount<=OrganismMeshes.FloraTopTriangleCap);
    }

    [Fact]
    public void MediumAndFarBotanicalTierBudgetsRemainBounded()
    {
        var species=TestUtil.Content.FloraOrThrow("shadebell");
        var high=OrganismMeshes.FloraTier(species,5881UL,0,tieredSource:true);
        var mid=OrganismMeshes.FloraTier(species,5881UL,1,tieredSource:true);
        var far=OrganismMeshes.FloraTier(species,5881UL,2,tieredSource:true);
        Assert.InRange(high.TriangleCount,1,OrganismMeshes.FloraTopTriangleCap);
        Assert.InRange(mid.TriangleCount,1,OrganismMeshes.FloraTierBudget(high.TriangleCount,1));
        Assert.InRange(far.TriangleCount,1,OrganismMeshes.FloraTierBudget(high.TriangleCount,2));
        // WP-19: Non-destructive LOD preserves all leaf organs without dropping
        Assert.Equal(high.LeafSpans.Count, mid.LeafSpans.Count);
        Assert.Equal(mid.LeafSpans.Count, far.LeafSpans.Count);
    }
}
