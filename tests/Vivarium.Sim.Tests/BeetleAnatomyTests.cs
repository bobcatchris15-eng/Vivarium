using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

public class BeetleAnatomyTests
{
    [Fact]
    public void CoalbackHasSixAttachedLegsAndTwoSensoryRootsWithValidGeometry()
    {
        var mesh=OrganismMeshes.Fauna(TestUtil.Content.FaunaOrThrow("coalback_beetle"),1234);
        var roots=new Dictionary<int,HashSet<(double,double,double)>>();
        foreach(int i in Enumerable.Range(0,mesh.VertexCount))
        {
            Assert.True(double.IsFinite(mesh.Position(i).LengthSq));
            Assert.True(float.IsFinite(mesh.Colors[i*4]) && float.IsFinite(mesh.Colors[i*4+1]) && float.IsFinite(mesh.Colors[i*4+2]));
            if(mesh.Colors[i*4+3]<=.5)continue;
            int role=(int)mesh.UV2[i*2+1];
            if(!roots.TryGetValue(role,out var group))roots[role]=group=new();
            var p=mesh.Position(i);
            group.Add((Math.Round(p.X-mesh.Colors[i*4],5),Math.Round(p.Y-mesh.Colors[i*4+1],5),Math.Round(p.Z-mesh.Colors[i*4+2],5)));
        }
        Assert.Equal(6,roots[1].Count);Assert.Equal(2,roots[3].Count);
        foreach(int i in mesh.Indices)Assert.InRange(i,0,mesh.VertexCount-1);
        for(int t=0;t<mesh.Indices.Count;t+=3)
        {
            var a=mesh.Position(mesh.Indices[t]);var b=mesh.Position(mesh.Indices[t+1]);var c=mesh.Position(mesh.Indices[t+2]);
            Assert.True((b-a).Cross(c-a).LengthSq>1e-20,"Collapsed beetle triangle");
        }
        // The terminal foot geometry cannot extend through the neutral support
        // plane even after inherited appendage/body morphs have been applied.
        Assert.True(mesh.Bounds().Min.Y>=-.002,"Neutral beetle geometry crosses its ground plane");
    }
}
