using Vivarium.Sim.Core;
using Vivarium.Sim.World;
namespace Vivarium.Sim.Tests;
public class RootQueryAccelerationTests
{
    [Fact]
    public void AcceleratedQueriesMatchOrderedFullSegmentSearch()
    {
        foreach (var def in PilotTreeCatalog.All)
        {
            var descriptor=TestUtil.FlatDescriptor(424242); descriptor.PilotTreeId=def.Id;
            var world=VivariumWorld.CreateBaseline(TestUtil.Content,descriptor);
            var tree=world.PilotTree!;
            for(int z=-15;z<=15;z++) for(int x=-15;x<=15;x++)
            {
                var p=new Vec2(x*.37+.013,z*.39-.021);
                double ratio=double.PositiveInfinity,distance=0,width=0,rise=0;
                foreach(var path in tree.Roots) for(int k=1;k<path.Samples.Count;k++)
                {
                    var a=path.Samples[k-1]; var b=path.Samples[k]; var ab=b.Position-a.Position;
                    double t=ab.LengthSq>1e-12?MathD.Clamp01((p-a.Position).Dot(ab)/ab.LengthSq):0;
                    double d=Vec2.Distance(p,a.Position+ab*t), w=MathD.Lerp(a.Width,b.Width,t), h=MathD.Lerp(a.Height,b.Height,t);
                    if(d/Math.Max(w,.001)<ratio){ratio=d/Math.Max(w,.001);distance=d;width=w;}
                    if(d<w)rise=Math.Max(rise,h*Math.Sqrt(Math.Max(0,1-d*d/(w*w))));
                }
                Assert.Equal(Math.Exp(-distance*distance/Math.Max(.12,width*width*4)),tree.RootDensity(p),12);
                if(!tree.BlocksTrunk(p))
                {
                    double height=tree.RootSurfaceHeight(p);
                    if(rise==0)Assert.True(double.IsNaN(height));
                    else Assert.Equal(world.Terrain.Height(p)+rise,height,12);
                }
            }
        }
    }
}
