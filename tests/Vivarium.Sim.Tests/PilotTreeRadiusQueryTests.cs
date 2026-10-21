using System.Reflection;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Coverage")]
public class PilotTreeRadiusQueryTests
{
    private readonly record struct Segment(PilotRootSample A,PilotRootSample B,Vec2 Delta,double LengthSq,
        double MinX,double MaxX,double MinZ,double MaxZ,double MaxWidth);
    private static Segment[] Segments(PilotTreeState tree)=>tree.Roots.SelectMany(path=>
        Enumerable.Range(1,path.Samples.Count-1).Select(i=>
        {
            var a=path.Samples[i-1];var b=path.Samples[i];var d=b.Position-a.Position;
            return new Segment(a,b,d,d.LengthSq,Math.Min(a.Position.X,b.Position.X),Math.Max(a.Position.X,b.Position.X),
                Math.Min(a.Position.Z,b.Position.Z),Math.Max(a.Position.Z,b.Position.Z),Math.Max(a.Width,b.Width));
        })).ToArray();

    // Independent original full scan, with original bounds/arithmetic/order.
    private static double ReferenceRise(Segment[] segments,Vec2 p,double radius)
    {
        double top=0;
        foreach(var segment in segments)
        {
            double limit=segment.MaxWidth+radius;
            double x=Math.Max(Math.Max(segment.MinX-p.X,p.X-segment.MaxX),0);
            double z=Math.Max(Math.Max(segment.MinZ-p.Z,p.Z-segment.MaxZ),0);
            if(x*x+z*z>=limit*limit)continue;
            var a=segment.A;var b=segment.B;var ab=segment.Delta;
            double t=segment.LengthSq>1e-12?MathD.Clamp01((p-a.Position).Dot(ab)/segment.LengthSq):0;
            double width=MathD.Lerp(a.Width,b.Width,t)+radius;
            double d=Vec2.Distance(p,a.Position+ab*t);
            if(d>=width)continue;
            double height=MathD.Lerp(a.Height,b.Height,t);
            double section=Math.Sqrt(Math.Max(0,1-d*d/(width*width)));
            top=Math.Max(top,height*section);
        }
        return top;
    }
    private static void Exact(double expected,double actual)=>
        Assert.Equal(BitConverter.DoubleToInt64Bits(expected),BitConverter.DoubleToInt64Bits(actual));

    [Theory]
    [InlineData("gloomspire",0)] [InlineData("needlevault",1)]
    [InlineData("crowncoil",2)] [InlineData("emberpillar",3)]
    [InlineData("basinwarden",4)] [InlineData("palehollow",5)]
    public void RadiusMaskMatchesFullScanAtRootsCellBoundariesAndFallbacks(string id,int corner)
    {
        var world=TestUtil.FlatWorld(711,d=>{d.PilotTreeId=id;d.PilotTreeCorner=corner;});
        var tree=world.PilotTree!;var segments=Segments(tree);
        var rise=typeof(PilotTreeState).GetMethod("RootRise",BindingFlags.Instance|BindingFlags.NonPublic)!
            .CreateDelegate<Func<Vec2,double,double>>(tree);
        var points=new List<Vec2>();
        foreach(var segment in segments)
        {
            points.Add(segment.A.Position);points.Add(segment.B.Position);
            points.Add(segment.A.Position+new Vec2(segment.MaxWidth,.058));
        }
        for(int i=-10;i<=10;i++)for(int j=-10;j<=10;j++)
            points.Add(new Vec2(i*.5+(i%3)*1e-12,j*.5+(j%3)*1e-12));
        points.Add(new Vec2(1e30,-1e30));points.Add(new Vec2(double.NaN,0));points.Add(new Vec2(double.PositiveInfinity,0));
        foreach(var p in points)foreach(double radius in new[]{0,.001,.009,.058,.12,.7,3,-.05,1e30,double.NaN,double.PositiveInfinity})
            // Preserve the pre-existing zero-radius cell lookup's empty result
            // for nonfinite positions; only positive radii use the new mask.
            Exact(radius==0 && (!double.IsFinite(p.X)||!double.IsFinite(p.Z)) ? 0 : ReferenceRise(segments,p,radius),rise(p,radius));

        // Future larger root forms must bypass the 64-bit index entirely.
        var field=typeof(PilotTreeState).GetField("_segments",BindingFlags.Instance|BindingFlags.NonPublic)!;
        var original=(Array)field.GetValue(tree)!;
        var larger=Array.CreateInstance(original.GetType().GetElementType()!,original.Length*2);
        Array.Copy(original,0,larger,0,original.Length);Array.Copy(original,0,larger,original.Length,original.Length);
        try
        {
            field.SetValue(tree,larger);
            foreach(var p in points.Take(60))Exact(ReferenceRise(segments,p,.058),rise(p,.058));
        }
        finally{field.SetValue(tree,original);}
    }

    private static (Vec3 Normal,double Depth) ReferencePenetration(PilotTreeState tree,Heightfield terrain,Segment[] segments,Vec3 p,double radius)
    {
        double trunkDistance=Vec2.Distance(p.XZ,tree.Anchor);
        if(p.Y>=tree.BaseHeight-radius&&p.Y<=tree.BaseHeight+tree.Def.TrunkHeight+radius&&trunkDistance<tree.Def.TrunkRadius*.91+radius)
        {
            var radial=(p.XZ-tree.Anchor).Normalized();if(radial.LengthSq<1e-12)radial=tree.CornerPoint.Normalized();
            double side=tree.Def.TrunkRadius*.91+radius-trunkDistance;
            double top=tree.BaseHeight+tree.Def.TrunkHeight+radius-p.Y;
            if(top<side)return(Vec3.Up,top);
            return(Vec3.FromXZ(radial,0),side);
        }
        if(p.Y>terrain.MaxHeight+tree.Def.RootHeight*1.8+radius)return(Vec3.Up,0);
        if(p.Y<terrain.Height(p.XZ)-.04-radius)return(Vec3.Up,0);
        double rise=ReferenceRise(segments,p.XZ,radius);
        if(!(rise>.01&&p.Y<terrain.Height(p.XZ)+rise+radius))return(Vec3.Up,0);
        double Surface(Vec2 q)
        {
            if(tree.BlocksTrunk(q))return tree.BaseHeight+tree.Def.TrunkHeight;
            double h=ReferenceRise(segments,q,0);
            return h<=0?double.NaN:terrain.Height(q)+h;
        }
        double h=Surface(p.XZ);if(double.IsNaN(h))h=terrain.Height(p.XZ);
        const double epsilon=.015;
        double Top(Vec2 q){double t=Surface(q);return double.IsNaN(t)?terrain.Height(q):t;}
        double dx=(Top(p.XZ+new Vec2(epsilon,0))-Top(p.XZ-new Vec2(epsilon,0)))/(2*epsilon);
        double dz=(Top(p.XZ+new Vec2(0,epsilon))-Top(p.XZ-new Vec2(0,epsilon)))/(2*epsilon);
        return(new Vec3(-dx,1,-dz).Normalized(),Math.Max(.001,h+radius-p.Y));
    }

    [Fact]
    public void PenetrationMatchesOriginalFullScanAfterTerrainEdits()
    {
        var world=TestUtil.FlatWorld(712,d=>d.PilotTreeId="basinwarden");var tree=world.PilotTree!;
        for(int edit=0;edit<2;edit++)
        {
            if(edit>0)for(int i=0;i<world.Terrain.H.Length;i++)world.Terrain.H[i]+=(i%7-3)*.013;
            var segments=Segments(tree);
            foreach(var s in segments)foreach(double radius in new[]{0,.009,.058,.2,3})
            foreach(double y in new[]{-.01,.03,.2,.6,10})
            {
                var p=Vec3.FromXZ((s.A.Position+s.B.Position)*.5,world.Terrain.Height(s.A.Position)+y);
                var expected=ReferencePenetration(tree,world.Terrain,segments,p,radius);var actual=tree.Penetration(p,radius);
                Exact(expected.Depth,actual.Depth);Exact(expected.Normal.X,actual.Normal.X);
                Exact(expected.Normal.Y,actual.Normal.Y);Exact(expected.Normal.Z,actual.Normal.Z);
            }
        }
    }
}
