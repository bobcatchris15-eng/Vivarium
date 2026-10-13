using Vivarium.Sim.Geometry;
namespace Vivarium.Sim.Tests;
public class SilverfishAnatomyTests
{
    [Fact]
    public void GhostbristleKeepsSixLegsAndFiveSensoryRootsAcrossInheritedMorphs()
    {
        var sp=TestUtil.Content.FaunaOrThrow("ghostbristle");
        foreach(ulong seed in new ulong[]{0,42,1234,81733})
        {
            var mesh=OrganismMeshes.Fauna(sp,seed);
            var legs=new HashSet<(double,double,double)>();var sensory=new HashSet<(double,double,double)>();
            for(int i=0;i<mesh.VertexCount;i++)
            {
                var p=mesh.Position(i);Assert.True(double.IsFinite(p.LengthSq));
                if(mesh.Colors[i*4+3]<=.5)continue;
                var root=(Math.Round(p.X-mesh.Colors[i*4],5),Math.Round(p.Y-mesh.Colors[i*4+1],5),Math.Round(p.Z-mesh.Colors[i*4+2],5));
                double role=mesh.UV2[i*2+1];if(role>=1&&role<2)legs.Add(root);if(role==3)sensory.Add(root);
            }
            Assert.Equal(6,legs.Count);Assert.Equal(5,sensory.Count);
            Assert.True(mesh.Bounds().Min.Y>=.0019,"Neutral tarsi penetrate support after morph");
            foreach(int index in mesh.Indices)Assert.InRange(index,0,mesh.VertexCount-1);
            for(int i=0;i<mesh.Indices.Count;i+=3)
            {
                var a=mesh.Position(mesh.Indices[i]);var b=mesh.Position(mesh.Indices[i+1]);var c=mesh.Position(mesh.Indices[i+2]);
                Assert.True((b-a).Cross(c-a).LengthSq>1e-20,"Collapsed cuticle triangle");
            }
        }
    }
}
