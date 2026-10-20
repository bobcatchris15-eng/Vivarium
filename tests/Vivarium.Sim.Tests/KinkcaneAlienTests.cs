using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

/// <summary>Species contract for accepted Kinkcane geometry, independent of GLB assets.</summary>
public sealed class KinkcaneAlienTests
{
    private static Vec3 Center(MeshData m,int start,int ring)
    {
        var centre=Vec3.Zero;
        for(int j=0;j<7;j++)centre+=m.Position(start+ring*8+j)/7;
        return centre;
    }

    private static (Vec3 Base,Vec3 Top,bool Straight)[] Canes(MeshData m)
    {
        var tubes=m.Structural.Where(s=>s.VertexCount==56&&s.Sides==7&&s.Radius>.015).ToArray();
        return tubes.Select(t=>
        {
            var points=Enumerable.Range(0,7).Select(r=>Center(m,t.FirstVertex,r)).ToArray();
            bool straight=points.All(p=>
                Math.Abs(p.X-points[0].X)<1e-5 &&
                Math.Abs(p.Z-points[0].Z)<1e-5);
            return (points[0],points[^1],straight);
        }).ToArray();
    }

    [Theory]
    [InlineData(14UL)]
    [InlineData(754UL)]
    [InlineData(9821UL)]
    public void ExactlyOneFifthOfCulmsAreCollinear(ulong seed)
    {
        var sp=TestUtil.Content.FloraOrThrow("kinkcane");
        var m=OrganismMeshes.Flora(sp,seed,visualDetail:0);
        var canes=Canes(m);
        Assert.Contains(canes.Length,new[]{15,20});
        Assert.Equal(canes.Length/5,canes.Count(c=>c.Straight));
        Assert.True(canes.All(c=>c.Top.Y>c.Base.Y+.5));
        Assert.Equal(m.DigestHex(),OrganismMeshes.Flora(sp,seed,visualDetail:0).DigestHex());
        Assert.All(m.Positions,p=>Assert.True(float.IsFinite(p)));
        Assert.InRange(m.TriangleCount,1,OrganismMeshes.FloraTopTriangleCap);
    }

    [Theory]
    [InlineData(14UL)]
    [InlineData(754UL)]
    public void LeafAttachmentSitesStayWithinUpperFifthOfTheirCane(ulong seed)
    {
        var sp=TestUtil.Content.FloraOrThrow("kinkcane");
        var m=OrganismMeshes.Flora(sp,seed,visualDetail:0);
        var canes=Canes(m);
        int cursor=0;
        for(int i=0;i<canes.Length;i++)
        {
            bool straight=canes[i].Straight;
            int blades=straight?40:5;
            Assert.True(cursor+blades<=m.Leaves.Count);
            double bottom=canes[i].Base.Y+(.80*(canes[i].Top.Y-canes[i].Base.Y));
            for(int n=cursor;n<cursor+blades;n++)
                Assert.True(m.Leaves[n].Attachment.Y>=bottom-1e-4,
                    $"Seed {seed}, cane {i}, leaf {n-cursor} at {m.Leaves[n].Attachment.Y:F3} below 80% threshold {bottom:F3}");
            cursor+=blades;
        }
        Assert.Equal(cursor,m.Leaves.Count);
        Assert.True(canes.Count(c=>c.Straight)*40>=m.Leaves.Count/2,
            "Broad palmate terminal crowns must be genuinely prominent.");
    }

    [Fact]
    public void CanesAreThinnerAndDarkerThanOriginalSpeciesPalette()
    {
        var sp=TestUtil.Content.FloraOrThrow("kinkcane");
        var m=OrganismMeshes.Flora(sp,129UL,visualDetail:0);
        var first=m.Structural.First();
        Assert.InRange(first.Radius,.014,.018);
        Assert.True(m.Colors[first.FirstVertex*4+1]<sp.Color[1]*.40);
        Assert.True(m.LeafSpans.Count>=150);
    }

    [Fact]
    public void AllDetailTiersPreserveBudget()
    {
        var sp=TestUtil.Content.FloraOrThrow("kinkcane");
        var high=OrganismMeshes.FloraTier(sp,8391UL,0);
        var mid=OrganismMeshes.FloraTier(sp,8391UL,1);
        var far=OrganismMeshes.FloraTier(sp,8391UL,2);
        Assert.InRange(high.TriangleCount,1,OrganismMeshes.FloraTopTriangleCap);
        Assert.InRange(mid.TriangleCount,1,OrganismMeshes.FloraTierBudget(high.TriangleCount,1));
        Assert.InRange(far.TriangleCount,1,OrganismMeshes.FloraTierBudget(high.TriangleCount,2));
    }
}
