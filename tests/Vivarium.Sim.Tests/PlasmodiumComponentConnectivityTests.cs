using Vivarium.Sim.Coverage.Plasmodium;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "GrowthLab")]
public class PlasmodiumComponentConnectivityTests
{
    // Independent union-find reference for the former canonical component rule.
    private static List<List<(int,int)>> Reference(IEnumerable<(int,int)> cells)
    {
        var set=new HashSet<(int,int)>(cells);
        var parents=set.ToDictionary(x=>x,x=>x);
        (int,int) Find((int,int) x)
        {
            while(parents[x]!=x)
            {
                parents[x]=parents[parents[x]];
                x=parents[x];
            }
            return x;
        }
        foreach(var (x,z) in set)
            for(int dx=-1;dx<=1;dx++)
                for(int dz=-1;dz<=1;dz++)
                {
                    if(dx==0 && dz==0)continue;
                    var n=(x+dx,z+dz);
                    if(set.Contains(n))
                    {
                        var a=Find((x,z));
                        var b=Find(n);
                        if(a!=b)parents[a]=b;
                    }
                }
        var groups=new Dictionary<(int,int),List<(int,int)>>();
        foreach(var c in set)
        {
            var root=Find(c);
            if(!groups.TryGetValue(root,out var group))
                groups[root]=group=new List<(int,int)>();
            group.Add(c);
        }
        var result=groups.Values.ToList();
        foreach(var group in result)group.Sort();
        result.Sort((a,b)=>a[0].CompareTo(b[0]));
        return result;
    }

    [Fact]
    public void FloodFillExactlyMatchesUnionFindForSparseDenseAndDisconnectedSheets()
    {
        var rng=new Random(380113);
        for(int scenario=0;scenario<24;scenario++)
        {
            var cells=new List<(int,int)>();
            int count=100+scenario*180;
            for(int i=0;i<count;i++)
            {
                // Mixed islands, narrow corridors, independent specks,
                // negative coordinates, duplicates, and sparse diagonals.
                int group=rng.Next(0,7);
                int x=rng.Next(-28,29)+group*55;
                int z=rng.Next(-28,29)+(group%3)*66;
                cells.Add((x,z));
                if(i%17==0) cells.Add((x,z));
                if(i%9==0) cells.Add((x+1,z+1));
            }
            cells.AddRange(Enumerable.Range(0,100).Select(i=>(i-50, i-50)));
            var expected=Reference(cells);
            var actual=ComponentLabeler.Label(cells);
            Assert.Equal(expected.Count,actual.Count);
            for(int i=0;i<expected.Count;i++)
                Assert.Equal(expected[i],actual[i]);
            cells.Reverse();
            var reversed=ComponentLabeler.Label(cells);
            Assert.Equal(actual.Count,reversed.Count);
            for(int i=0;i<actual.Count;i++)
                Assert.Equal(actual[i],reversed[i]);
        }
    }
}
