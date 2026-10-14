using Vivarium.Sim.Core;
using Vivarium.Sim.Content;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Geometry.Form;
namespace Vivarium.Sim.Tests;
public class OrganicVisualTests
{
    [Fact]
    public void GlasscoilStaysOnTheSubmergedSurfaceAcrossWaterColumnFractionsAndMovement()
    {
        var w=TestUtil.FlatWorld(73,d=>d.Water.WaterTable=.95);
        var snail=TestUtil.Content.FaunaOrThrow("glasscoil");
        double bed=w.Terrain.Height(Vec2.Zero);
        foreach(double fraction in new[]{.05,.5,.95})Assert.Equal(bed,w.FaunaSystem.RestingY(snail,Vec2.Zero,fraction),8);
        var swimmer=TestUtil.Content.Fauna.First(s=>s.Medium==Medium.Aquatic&&s.Model!="snail");
        Assert.True(w.FaunaSystem.RestingY(swimmer,Vec2.Zero,.8)>bed+.01);
        var a=w.FaunaSystem.CreateFounder(snail,Vec2.Zero);
        for(int i=0;i<120;i++)
        {
            a.Energy=snail.MaxEnergy;w.FaunaSystem.StepBehaviour(1);
            Assert.Equal(w.FaunaSystem.RestingY(snail,new Vec2(a.X,a.Z)),a.Y,8);
        }
    }
    [Fact]
    public void RoundedBroadleafHasCurvedSurfaceAndDeterministicOrganicContour()
    {
        MeshData Make(ulong seed)
        {
            var m=new MeshData {FloraDetailLevel=0};
            Broadleaf.BuildLeaf(m,Vec3.Zero,new Vec3(1,0,0),1,.55,BroadleafOutline.Ovate,seed,
                new[]{.1,.3,.08},new[]{.2,.4,.1});return m;
        }
        var m=Make(37);var leaf=Assert.Single(m.Leaves);
        Assert.True(leaf.VertexCount>30);
        Assert.True(Enumerable.Range(leaf.FirstVertex,leaf.VertexCount).Select(m.NormalAt).Distinct().Count()>12);
        Assert.Equal(m.DigestHex(),Make(37).DigestHex());Assert.NotEqual(m.DigestHex(),Make(38).DigestHex());
    }
    [Theory]
    [InlineData("glassfinger")]
    [InlineData("shadebell")]
    [InlineData("lanternbrush")]
    [InlineData("embercrown")]
    public void PhotographedLeafTiersRespectLiveGeometryBudgets(string id)
    {
        var sp=TestUtil.Content.FloraOrThrow(id);
        var near=OrganismMeshes.FloraTier(sp,173,0,tieredSource:true);
        Assert.InRange(near.TriangleCount,1,OrganismMeshes.FloraTopTriangleCap);
        for(int tier=1;tier<3;tier++)
        {
            var m=OrganismMeshes.FloraTier(sp,173,tier,tieredSource:true);
            Assert.True(m.TriangleCount<=OrganismMeshes.FloraTierBudget(near.TriangleCount,tier));
        }
    }
    [Fact]
    public void EverySpeciesHasFiniteNearLeafGeometry()
    {
        foreach(var sp in TestUtil.Content.Flora)
        {
            var m=OrganismMeshes.Flora(sp,39);
            Assert.All(m.Positions,p=>Assert.True(float.IsFinite(p),sp.Id));
            Assert.All(m.Normals,n=>Assert.True(float.IsFinite(n),sp.Id));
        }
    }
}
