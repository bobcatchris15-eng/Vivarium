using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

public class ShallowPresetTests
{
    [Theory]
    [InlineData("braided_springs",5)]
    [InlineData("braided_springs",8)]
    [InlineData("braided_springs",20)]
    [InlineData("winding_pools",5)]
    [InlineData("winding_pools",8)]
    [InlineData("winding_pools",20)]
    public void ActualChannelAndPoolDepthsFollowTheSlopingSurface(string id,double size)
    {
        foreach (ulong seed in new ulong[]{1,731921,837129})
        {
            var d=TestUtil.Content.PresetOrThrow(id);d.Seed=seed;d.ResizeFootprint(size);
            var domain=new HexDomain(size);var cut=Heightfield.Generate(d,domain);
            var baseline=d.Clone();baseline.Terrain.Features.Clear();var land=Heightfield.Generate(baseline,domain);
            var pools=d.Terrain.Features.Where(f=>f.Type=="basin").ToArray();int samples=0;
            foreach(var f in d.Terrain.Features.Where(f=>f.Type=="channel"))
            {
                var a=new Vec2(f.X,f.Z);var ab=new Vec2(f.ToX,f.ToZ)-a;
                int n=Math.Max(2,(int)Math.Ceiling(ab.Length/.025));
                for(int k=0;k<=n;k++)
                {
                    var p=a+ab*((double)k/n);if(!domain.Contains(p))continue;
                    if(pools.Any(b=>Vec2.Distance(p,new(b.X,b.Z))<b.Radius*1.4))continue;
                    double depth=land.Height(p)-cut.Height(p);
                    Assert.InRange(depth,.05,.100001);samples++;
                    if(k<n)
                    {
                        var next=a+ab*((double)(k+1)/n);
                        Assert.True(land.Height(next)-land.Height(p)<.001,
                            $"{id}: surrounding ground rises along the course at {p}");
                    }
                }
            }
            Assert.True(samples>20);
            foreach(var pool in pools)
            {
                var p=new Vec2(pool.X,pool.Z);
                Assert.InRange(land.Height(p)-cut.Height(p),.10,.15);
            }
            double scale=size/8;
            Assert.True(land.Height(new(0,-2*scale))-land.Height(new(0,2*scale))>.24);
            var copy=WorldDescriptor.FromJson(d.ToJson());
            Assert.Empty(copy.Validate());
            Assert.Equal(cut.DigestHex(),Heightfield.Generate(copy,domain).DigestHex());
        }
    }

    [Fact]
    public void WindingPoolsHasFivePoolsOnOneLongConnectedCourseToTheBoundary()
    {
        var d=TestUtil.Content.PresetOrThrow("winding_pools");
        Assert.Single(d.Water.Springs);
        var fs=d.Terrain.Features.Where(f=>f.Type=="channel").ToArray();
        var pools=d.Terrain.Features.Where(f=>f.Type=="basin").ToArray();Assert.Equal(5,pools.Length);
        for(int i=1;i<fs.Length;i++)
            Assert.True(Vec2.Distance(new(fs[i-1].ToX,fs[i-1].ToZ),new(fs[i].X,fs[i].Z))<1e-8);
        var a=new Vec2(fs[0].X,fs[0].Z);var b=new Vec2(fs[^1].ToX,fs[^1].ToZ);
        Assert.True(fs.Sum(f=>Vec2.Distance(new(f.X,f.Z),new(f.ToX,f.ToZ)))/Vec2.Distance(a,b)>3);
        Assert.False(new HexDomain(d.Diameter).Contains(b));
        foreach(var pool in pools)
            Assert.Contains(fs,f=>Vec2.Distance(new(f.X,f.Z),new(pool.X,pool.Z))<.1);
    }

    [Fact]
    public void RelativeCutsDoNotDoubleExcavateOrScaleTheirDepthWithTheFootprint()
    {
        var d=TestUtil.FlatDescriptor();d.Terrain.MicroRelief=0;
        d.Terrain.Features.Add(new(){Type="channel",X=-2,ToX=2,Width=.4,CutDepth=.085,Sharp=true});
        d.Terrain.Features.Add(new(){Type="channel",Z=-2,ToZ=2,Width=.4,CutDepth=.085,Sharp=true});
        var h=Heightfield.Generate(d,new HexDomain(d.Diameter));
        Assert.Equal(.415,h.Height(Vec2.Zero),8);
        d.ResizeFootprint(20);d.Terrain.ScaleRelief(2.5);
        Assert.All(d.Terrain.Features,f=>Assert.Equal(.085,f.CutDepth));
        d.Terrain.Features[0].BedHeight=.2;
        Assert.Contains(d.Validate(),s=>s.Contains("cutDepth"));
    }

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    [InlineData(20)]
    public void MarshHasPermanentShallowChannelsAndThreeDeeperEdgePools(double size)
    {
        var d=TestUtil.Content.PresetOrThrow("permanent_marsh");d.ResizeFootprint(size);
        Assert.Empty(d.Water.Springs);
        var domain=new HexDomain(size);var h=Heightfield.Generate(d,domain);
        int total=0,near=0;var wet=new HashSet<(int X,int Z)>();
        for(int j=0;j<h.Nz;j++)for(int i=0;i<h.Nx;i++)
        {
            var p=h.VertexPos(i,j);if(!domain.Contains(p))continue;total++;
            double y=h.Vertex(i,j)-d.Water.WaterTable;
            if(Math.Abs(y)<.08)near++;
            if(y<-.002)wet.Add((i,j));
        }
        Assert.InRange((double)near/total,.85,1);
        Assert.InRange((double)wet.Count/total,.25,.85);
        int wetCount=wet.Count,largest=0;
        while(wet.Count>0)
        {
            var stack=new Stack<(int X,int Z)>();var start=wet.First();wet.Remove(start);stack.Push(start);int count=0;
            while(stack.TryPop(out var p))
            {
                count++;
                foreach(var q in new[]{(p.X+1,p.Z),(p.X-1,p.Z),(p.X,p.Z+1),(p.X,p.Z-1)})
                    if(wet.Remove(q))stack.Push(q);
            }
            largest=Math.Max(largest,count);
        }
        Assert.True(largest>wetCount*.8);
        var pools=d.Terrain.Features.Where(f=>f.Type=="basin").ToArray();Assert.Equal(3,pools.Length);
        foreach(var pool in pools)
        {
            var p=new Vec2(pool.X,pool.Z);
            Assert.InRange(d.Water.WaterTable-h.Height(p),.20,.40);
            Assert.True(p.Length>size*.3);
        }
    }
}
