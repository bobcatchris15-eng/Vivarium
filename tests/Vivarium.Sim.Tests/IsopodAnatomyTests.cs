using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

public class IsopodAnatomyTests
{
    [Fact]
    public void MarblebackKeepsFourteenLegRootsAndContinuousFiniteWalkAndCurledGeometry()
    {
        var sp=TestUtil.Content.FaunaOrThrow("marbleback");
        foreach(ulong seed in new ulong[]{0,42,1234,81733})
        {
            var walk=OrganismMeshes.Fauna(sp,seed);
            var roots=new HashSet<(double,double,double)>();
            for(int i=0;i<walk.VertexCount;i++)
                if(walk.Colors[i*4+3]>.5 && walk.UV2[i*2+1]>=1 && walk.UV2[i*2+1]<2)
                {
                    var p=walk.Position(i);
                    roots.Add((Math.Round(p.X-walk.Colors[i*4],5),Math.Round(p.Y-walk.Colors[i*4+1],5),Math.Round(p.Z-walk.Colors[i*4+2],5)));
                }
            Assert.Equal(14,roots.Count);
            foreach(var mesh in new[]{walk,OrganismMeshes.FaunaCurled(sp,seed)!})
            {
                Assert.NotNull(mesh);Assert.True(mesh.Bounds().Min.Y>=-.002,"Neutral shell/foot penetrates support");
                for(int i=0;i<mesh.VertexCount;i++)Assert.True(double.IsFinite(mesh.Position(i).LengthSq));
                foreach(int index in mesh.Indices)Assert.InRange(index,0,mesh.VertexCount-1);
                for(int i=0;i<mesh.Indices.Count;i+=3)
                {
                    var a=mesh.Position(mesh.Indices[i]);var b=mesh.Position(mesh.Indices[i+1]);var c=mesh.Position(mesh.Indices[i+2]);
                    Assert.True((b-a).Cross(c-a).LengthSq>1e-20,"Collapsed cuticle triangle");
                }
            }
        }
    }
}
