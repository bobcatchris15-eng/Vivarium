using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

public class TerrainPresetRevisionTests
{
    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(20)]
    public void CascadesHaveASteepPhysicalLedgeAndFlatShelves(double diameter)
    {
        var d = TestUtil.Content.PresetOrThrow("cascading_falls");
        d.ResizeFootprint(diameter);
        var h = Heightfield.Generate(d, new HexDomain(diameter));
        double x = -.25 * diameter / 8, z = -1.5 * diameter / 8;
        // Locate the single mesh cell containing the shelf boundary.
        int i = (int)Math.Floor((x-h.OriginX)/h.Step)+1;
        double right = h.OriginX + i*h.Step, left = right-h.Step;
        var a = new Vec2(left,z); var b = new Vec2(right,z);
        Assert.InRange(h.Height(a)-h.Height(b), .60, .70);
        Assert.True(h.Normal((a+b)*.5).Y < .25);
        Assert.True(Math.Abs(h.Height(new(left-.3,z))-h.Height(new(left-.6,z))) < .04);
        foreach (var p in new[] {a,b,(a+b)*.5})
            Assert.InRange(Math.Abs(TerrainMesh.RenderHeight(h,p)-h.Height(p)),0,.000501);
        Assert.Empty(WorldDescriptor.FromJson(d.ToJson()).Validate());
    }

    [Fact]
    public void SpringbraidHasOneSourceTwoPerchedPoolsAndSplitRejoiningRoutes()
    {
        var d = TestUtil.Content.PresetOrThrow("braided_springs");
        Assert.Single(d.Water.Springs);
        var fs = d.Terrain.Features.Where(f=>f.Type=="channel").ToArray();
        var pools = d.Terrain.Features.Where(f=>f.Type=="basin").ToArray();
        Assert.Equal(3,pools.Length);
        var spring = d.Water.Springs[0];
        Assert.Equal(2,fs.Count(f=>Math.Abs(f.X-spring.X)<1e-6 && Math.Abs(f.Z-spring.Z)<1e-6));
        Assert.True(fs.GroupBy(f=>(f.ToX,f.ToZ)).Count(g=>g.Count()>1)>=2);
        var h=Heightfield.Generate(d,new HexDomain(d.Diameter));
        Assert.True(h.Height(new(0,3.05))<d.Water.WaterTable-.05);
        Assert.True(h.Height(new(-2,0))>d.Water.WaterTable+.2);
    }

    [Theory]
    [InlineData("creek")]
    [InlineData("default")]
    [InlineData("peat_fen")]
    [InlineData("woodland_vernal_pools")]
    public void GentleCoursesHaveBroadBendsRatherThanManyAlmostCollinearSegments(string id)
    {
        var d=TestUtil.Content.PresetOrThrow(id);
        var fs=d.Terrain.Features.Where(f=>f.Type=="channel" && f.BedHeight.HasValue).ToArray();
        var a=new Vec2(fs[0].X,fs[0].Z); var b=new Vec2(fs[^1].ToX,fs[^1].ToZ);
        double length=fs.Sum(f=>Vec2.Distance(new(f.X,f.Z),new(f.ToX,f.ToZ)));
        Assert.True(length/Vec2.Distance(a,b)>1.20,$"{id}: sinuosity {length/Vec2.Distance(a,b)}");
    }

    [Fact]
    public void DegenerateLedgeIsRejected()
    {
        var d=TestUtil.FlatDescriptor();d.Terrain.Features.Add(new(){Type="ledge"});
        Assert.Contains(d.Validate(),e=>e.Contains("ledge"));
    }
}
