using Vivarium.Sim.Geometry;
namespace Vivarium.Sim.Tests;
public class SalamanderAnatomyTests
{
    [Fact]
    public void RainspineKeepsFourLimbRootsAndClearSupportAcrossInheritedMorphs()
    {
        var sp=TestUtil.Content.FaunaOrThrow("rainspine");
        foreach(ulong seed in new ulong[]{0,42,1234,81733})
        {
            var mesh=OrganismMeshes.Fauna(sp,seed);var roots=new HashSet<(double,double,double)>();
            for(int i=0;i<mesh.VertexCount;i++)
            {
                var p=mesh.Position(i);Assert.True(double.IsFinite(p.LengthSq));
                if(mesh.Colors[i*4+3]>.5)Assert.InRange(mesh.UV2[i*2+1],1f,1.25f);
                if(mesh.Colors[i*4+3]>.5&&mesh.UV2[i*2+1]>=1&&mesh.UV2[i*2+1]<2)
                    roots.Add((Math.Round(p.X-mesh.Colors[i*4],5),Math.Round(p.Y-mesh.Colors[i*4+1],5),Math.Round(p.Z-mesh.Colors[i*4+2],5)));
            }
            Assert.Equal(4,roots.Count);Assert.True(mesh.Bounds().Min.Y>=.0019);
            foreach(int i in mesh.Indices)Assert.InRange(i,0,mesh.VertexCount-1);
            for(int i=0;i<mesh.Indices.Count;i+=3)
            {
                var a=mesh.Position(mesh.Indices[i]);var b=mesh.Position(mesh.Indices[i+1]);var c=mesh.Position(mesh.Indices[i+2]);
                Assert.True((b-a).Cross(c-a).LengthSq>1e-20,"Collapsed soft-skin triangle");
            }
            var vertex=Enumerable.Range(0,mesh.VertexCount).Where(i=>mesh.Colors[i*4+3]<.5&&mesh.Position(i).X<.1&&mesh.Position(i).X>-.1).OrderByDescending(i=>mesh.Position(i).Y).First();
            Assert.True(mesh.Normals[vertex*3+1]>0,"Dorsal normal faces inward");
        }
    }
}
