using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

public class FloraVisualTests
{
    [Theory]
    [InlineData("umbraheart")]
    [InlineData("kiteleaf")]
    [InlineData("glassfinger")]
    [InlineData("shadebell")]
    [InlineData("ironlace")]
    [InlineData("fenneedle")]
    [InlineData("lanternbrush")]
    [InlineData("embercrown")]
    public void DetailTiersRetainLeavesAndStableMaterialIdentity(string id)
    {
        var sp=TestUtil.Content.FloraOrThrow(id);
        var near=OrganismMeshes.Flora(sp,173,visualDetail:0);
        Assert.NotEmpty(near.Leaves);
        Assert.True(near.Leaves.Select(x=>x.MaterialVariant).Distinct().Count()>1);
        var medium=OrganismMeshes.Flora(sp,173,visualDetail:1);
        var far=OrganismMeshes.Flora(sp,173,visualDetail:2);
        Assert.True(medium.TriangleCount<near.TriangleCount);
        Assert.True(far.TriangleCount<=medium.TriangleCount); // Embercrown's boundary is already two triangles.
        foreach(var tier in new[]{medium,far})
        {
            Assert.Equal(near.Leaves.Count,tier.Leaves.Count);
            for(int i=0;i<near.Leaves.Count;i++)
            {
                var a=near.Leaves[i];var b=tier.Leaves[i];
                Assert.Equal(a.Identity,b.Identity);Assert.Equal(a.MaterialVariant,b.MaterialVariant);
                Assert.Equal(a.Attachment,b.Attachment);Assert.Equal(a.Length,b.Length);
            }
        }
        var again=OrganismMeshes.Flora(sp,173,visualDetail:0);
        Assert.Equal(near.DigestHex(),again.DigestHex());
    }

    [Theory]
    [InlineData("umbraheart")]
    [InlineData("kiteleaf")]
    [InlineData("glassfinger")]
    [InlineData("shadebell")]
    [InlineData("ironlace")]
    [InlineData("fenneedle")]
    [InlineData("lanternbrush")]
    [InlineData("embercrown")]
    public void SplitPreservesEveryTriangleAndAllLeafMetadata(string id)
    {
        var m=OrganismMeshes.Flora(TestUtil.Content.FloraOrThrow(id),89,visualDetail:0);
        var (stem,leaf)=FloraVisualCompiler.Split(m);
        Assert.Equal(m.TriangleCount,stem.TriangleCount+leaf.TriangleCount);
        Assert.Equal(leaf.VertexCount*4,leaf.Custom0.Count);Assert.Equal(leaf.VertexCount*4,leaf.Custom1.Count);
        Assert.Empty(stem.Custom0);
        Assert.All(leaf.Indices,i=>Assert.InRange(i,0,leaf.VertexCount-1));
        Assert.All(leaf.Custom1,f=>Assert.True(float.IsFinite(f)));
        Assert.All(m.Leaves,l=>Assert.Equal(id=="glassfinger",l.Volumetric));
    }

    [Theory]
    [InlineData("umbraheart")]
    [InlineData("kiteleaf")]
    [InlineData("shadebell")]
    [InlineData("ironlace")]
    [InlineData("fenneedle")]
    [InlineData("lanternbrush")]
    [InlineData("embercrown")]
    public void ThinBladesHaveOnlyOneAuthoredSide(string id)
    {
        var m=OrganismMeshes.Flora(TestUtil.Content.FloraOrThrow(id),89,visualDetail:0);
        foreach(var leaf in m.Leaves)
            for(int i=leaf.FirstVertex;i<leaf.FirstVertex+leaf.VertexCount;i++)
            { Assert.Equal(0,m.UV2[i*2]);Assert.Equal(1,m.UV2[i*2+1]); }
    }

    [Fact]
    public void EmbercrownTissueCoordinatesAreContinuousAcrossFacets()
    {
        var m=OrganismMeshes.Flora(TestUtil.Content.FloraOrThrow("embercrown"),173,visualDetail:0);
        foreach(var leaf in m.Leaves)
        {
            for(int t=leaf.FirstIndex;t<leaf.FirstIndex+leaf.IndexCount;t+=3)
            {
                int a=m.Indices[t],b=m.Indices[t+1],c=m.Indices[t+2];
                var winding=(m.Position(b)-m.Position(a)).Cross(m.Position(c)-m.Position(a));
                Assert.True(winding.Dot(m.NormalAt(a))<0); // Godot's authored upper face is clockwise.
            }
            foreach(var group in Enumerable.Range(leaf.FirstVertex,leaf.VertexCount).GroupBy(m.Position))
            {
                int first=group.First();
                foreach(int vertex in group)
                {
                    Assert.Equal(m.UV[first*2],m.UV[vertex*2]);
                    Assert.Equal(m.UV[first*2+1],m.UV[vertex*2+1]);
                }
            }
        }
    }

    [Fact]
    public void LegacyGeometryRemainsUnmigratedAndTierHysteresisIsStable()
    {
        Assert.Empty(OrganismMeshes.Flora(TestUtil.Content.FloraOrThrow("veilfern"),173).Leaves);
        Assert.Equal(0,FloraDetail.Select(330));Assert.Equal(1,FloraDetail.Select(200));Assert.Equal(2,FloraDetail.Select(100));
        Assert.Equal(0,FloraDetail.Select(300,0));Assert.Equal(1,FloraDetail.Select(330,1));
        Assert.Equal(2,FloraDetail.Select(130,2));Assert.Equal(1,FloraDetail.Select(150,2));
        Assert.Equal(1,FloraDetail.Select(110,1));Assert.Equal(2,FloraDetail.Select(90,1));
    }
}
