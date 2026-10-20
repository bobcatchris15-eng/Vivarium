using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;

namespace Vivarium.Sim.Tests;

public class AmbientCoverSpatialIndexTests
{
    [Fact]
    public void BinnedExclusionsMatchExactLinearSearchIncludingNegativeBorders()
    {
        var r=new Random(177);
        var index=new AmbientCoverSpatialIndex(0.75);
        var discs=new List<(Vec2 Pos,double Radius)>();
        for(int i=0;i<600;i++)
        {
            var p=new Vec2(r.NextDouble()*21-10.5,r.NextDouble()*21-10.5);
            double radius=0.01+r.NextDouble()*1.5;
            discs.Add((p,radius));
            index.Add(p,radius);
        }
        Assert.Equal(discs.Count,index.Count);
        long checks=0;
        for(int i=0;i<2800;i++)
        {
            var p=new Vec2(r.NextDouble()*27-13.5,r.NextDouble()*27-13.5);
            bool expected=discs.Any(d=>Vec2.DistanceSq(p,d.Pos)<d.Radius*d.Radius);
            Assert.Equal(expected,index.Contains(p));
            checks+=index.LastCandidates;
        }
        // Broad-phase must do less than 1/6 of the O(N) work here.
        Assert.True(checks<2800L*discs.Count/6,
            $"Too many candidates: {checks}");
        index.Clear();
        Assert.False(index.Contains(new Vec2(0,0)));
    }

    [Fact]
    public void VisualInfillingFollowsLivingParentBiomassWithoutCreatingEntities()
    {
        var index=new AmbientCoverSpatialIndex();
        var root=new Vec2(-.21,.43);
        index.Add(root,.55,.22);
        var near=new Vec2(-.17,.4);
        double young=index.Influence(near);
        Assert.True(young>0 && young<.25);
        Assert.Equal(0,index.Influence(new Vec2(2,2)));
        index.Clear();
        index.Add(root,.55,.86);
        double mature=index.Influence(near);
        Assert.True(mature>young*3,
            $"Mature={mature:0.000}, young={young:0.000}");
        Assert.Equal(1,index.Count); // exactly the same one parent
        index.Add(root,.55,.86);
        Assert.Equal(1,index.Influence(near),precision:12); // saturation
    }
}
