using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

public class MapDrainageTests
{
    [Fact]
    public void GradedChannelIntersectionsDoNotAddCutDepths()
    {
        var d = TestUtil.FlatDescriptor();
        d.Terrain.Features.Add(new() { Type="channel",X=-2,Z=0,ToX=2,ToZ=0,Width=.3,Sharp=true,BedHeight=.35,ToBedHeight=.15 });
        var domain = new HexDomain(d.Diameter);
        var single = Heightfield.Generate(d,domain);
        d.Terrain.Features.Add(new() { Type="channel",X=0,Z=-2,ToX=0,ToZ=2,Width=.3,Sharp=true,BedHeight=.35,ToBedHeight=.15 });
        var joined = Heightfield.Generate(d,domain);
        Assert.Equal(single.Height(Vec2.Zero),joined.Height(Vec2.Zero),10);
        Assert.InRange(joined.Height(Vec2.Zero),.249,.251);
    }

    [Fact]
    public void EverySpringHasAGradedRouteAndGentleSurroundingTerrainAtEveryPlayableSize()
    {
        foreach (var preset in TestUtil.Content.Presets.Values)
        foreach (double diameter in new[] { WorldDescriptor.MinDiameter,preset.Diameter,WorldDescriptor.MaxDiameter })
        {
            var d = preset.Clone(); d.ResizeFootprint(diameter);
            Assert.Empty(d.Validate());
            var domain = new HexDomain(d.Diameter); var terrain = Heightfield.Generate(d,domain);
            var channels = d.Terrain.Features.Where(f=>f.Type=="channel"&&f.BedHeight.HasValue).ToArray();
            Assert.NotEmpty(channels);
            foreach (var source in d.Water.Springs)
            {
                Assert.True(domain.ContainsDisc(new(source.X,source.Z),.05));
                Assert.Contains(channels,f=>Vec2.Distance(new(source.X,source.Z),new(f.X,f.Z))<1e-8);
            }
            foreach (var f in channels)
            {
                Assert.True(f.Sharp);
                Assert.True(f.ToBedHeight < f.BedHeight);
                var a = new Vec2(f.X,f.Z); var ab = new Vec2(f.ToX,f.ToZ)-a;
                int count = (int)Math.Ceiling(ab.Length/.025); double prior=terrain.Height(a);
                for (int i=1;i<=count;i++)
                {
                    var p=a+ab*((double)i/count); if (!domain.Contains(p)) break;
                    double h=terrain.Height(p);
                    // Perched runs cannot acquire a dam. Submerged bed variation remains below the water table.
                    Assert.True(h <= d.Water.WaterTable || h-prior<.001,$"{d.PresetId} at {diameter} m: uphill step {h-prior} at {p}");
                    prior=h;
                }
                var end = new Vec2(f.ToX,f.ToZ);
                Assert.True(!domain.Contains(end)||terrain.Height(end)<d.Water.WaterTable||
                    channels.Any(next=>next!=f&&Vec2.Distance(end,new(next.X,next.Z))<1e-8),
                    $"{d.PresetId}: channel ends without a receiver at {end}");
            }
        }
    }

    [Fact]
    public void ResizingPreservesBanksAndBedElevationAndDescriptorRoundTrips()
    {
        var d=TestUtil.Content.PresetOrThrow("creek"); var original=d.Clone();
        d.ResizeFootprint(10);
        double scale=10/original.Diameter;
        Assert.Equal(original.Terrain.SlopeZ/scale,d.Terrain.SlopeZ,12);
        for(int i=0;i<d.Terrain.Features.Count;i++)
        {
            var before=original.Terrain.Features[i]; var after=d.Terrain.Features[i];
            Assert.Equal(before.Width*scale,after.Width,12);
            Assert.Equal(before.BedHeight,after.BedHeight);
            Assert.Equal(before.ToBedHeight,after.ToBedHeight);
        }
        var copy=WorldDescriptor.FromJson(d.ToJson());
        Assert.Empty(copy.Validate());
        var domain=new HexDomain(d.Diameter);
        Assert.Equal(Heightfield.Generate(d,domain).DigestHex(),Heightfield.Generate(copy,domain).DigestHex());
    }

    [Theory]
    [InlineData("bayou_marsh",.35,.55,.015,.060)]
    [InlineData("shoreline",.30,.48,.30,.60)]
    [InlineData("deep_inlet",.30,.48,.35,.75)]
    public void WetlandAndLakeshoresHaveTheirAuthoredStandingWater(string id,double minArea,double maxArea,double minMedian,double maxMedian)
    {
        // Include actual tree placement and its soil shoulders, rather than just the raw preset height.
        var w=VivariumWorld.CreateBaseline(TestUtil.Content,TestUtil.Content.PresetOrThrow(id));
        var depths=new List<double>();int total=0;
        for(int z=0;z<w.Terrain.Nz;z++)for(int x=0;x<w.Terrain.Nx;x++)
        {
            var p=w.Terrain.VertexPos(x,z);if(!w.Domain.Contains(p))continue;total++;
            double depth=w.Water.WaterTable-w.GroundHeight(p);if(depth>0)depths.Add(depth);
        }
        depths.Sort();
        Assert.InRange((double)depths.Count/total,minArea,maxArea);
        Assert.InRange(depths[depths.Count/2],minMedian,maxMedian);
    }

    [Fact]
    public void ReliefControlKeepsEveryPresetValidIncludingDeepLakeFloors()
    {
        foreach(var original in TestUtil.Content.Presets.Values)
        foreach(double multiplier in new[]{.25,1.0,2.5})
        {
            var d=original.Clone(); d.ResizeFootprint(WorldDescriptor.MaxDiameter); d.Terrain.ScaleRelief(multiplier);
            Assert.Empty(d.Validate());
            var h=Heightfield.Generate(d,new HexDomain(d.Diameter));
            Assert.All(h.H,y=>Assert.True(double.IsFinite(y)&&y>=d.Terrain.MinHeight&&y<=d.Terrain.MaxHeight));
        }
    }

    [Fact]
    public void WaterTableMeanderSeparatesTwinMeadowsIntoTwoLargeLandAreas()
    {
        var w=VivariumWorld.CreateBaseline(TestUtil.Content,TestUtil.Content.PresetOrThrow("oxbow_creek"));
        var land=new HashSet<(int X,int Z)>();int total=0;
        for(int z=0;z<w.Terrain.Nz;z++)for(int x=0;x<w.Terrain.Nx;x++)
        {
            var p=w.Terrain.VertexPos(x,z);if(!w.Domain.Contains(p))continue;total++;
            if(w.GroundHeight(p)>w.Water.WaterTable)land.Add((x,z));
        }
        var areas=new List<int>();
        while(land.Count>0)
        {
            var start=land.First();land.Remove(start);var pending=new Stack<(int X,int Z)>();pending.Push(start);int count=0;
            while(pending.TryPop(out var p))
            {
                count++;
                foreach(var q in new[]{(p.X+1,p.Z),(p.X-1,p.Z),(p.X,p.Z+1),(p.X,p.Z-1)})
                    if(land.Remove(q))pending.Push(q);
            }
            areas.Add(count);
        }
        var large=areas.Where(a=>a>total*.15).OrderDescending().ToArray();
        Assert.Equal(2,large.Length);
        Assert.All(large,a=>Assert.InRange((double)a/total,.25,.60));
    }
}
