using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;
using Xunit;
namespace Vivarium.Sim.Tests;
public sealed class TerrainSurfaceMaterialTests
{
    [Fact]
    public void TerrainStaysSoilEvenAtMaximumRockExposure()
    {
        var w=TestUtil.FlatWorld(edit:d=>{d.Terrain.RockExposure=1;d.Terrain.Relief=1;});
        Assert.All(w.Grid.DomainCells,c=>Assert.Equal(Substrate.Soil,(Substrate)w.Fields.BaseSubstrate[c]));
    }
    [Fact]
    public void WaterTableRendersStandingWaterWithoutAnySpring()
    {
        var w=TestUtil.FlatWorld(edit:d=>d.Water.WaterTable=.7);
        var mesh=WaterMesh.BuildWaterTable(w,w.Terrain);
        Assert.True(mesh.TriangleCount>0);
        Assert.True(w.Water.IsWet(Vec2.Zero));
        Assert.Equal(.7,w.Water.SurfaceAt(Vec2.Zero),8);
        for(int i=0;i<mesh.VertexCount;i++)
        {
            var p=mesh.Position(i);
            Assert.True(w.Domain.SignedDistance(p.XZ)<1e-5);
            Assert.True(p.Y<=.700001);
        }
    }
    [Fact]
    public void WaterTableDoesNotProduceSurfaceAboveDryTerrain()
    {
        var w=TestUtil.FlatWorld();
        Assert.Equal(0,WaterMesh.BuildWaterTable(w,w.Terrain).TriangleCount);
        Assert.False(w.Water.IsWet(Vec2.Zero));
    }
}
