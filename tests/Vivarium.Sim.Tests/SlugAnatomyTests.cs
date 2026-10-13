using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

public class SlugAnatomyTests
{
    [Fact]
    public void DewmantleHasContinuousSupportedSkinAndFourAttachedSensoryRoots()
    {
        var species=TestUtil.Content.FaunaOrThrow("dewmantle");
        foreach(ulong seed in new ulong[]{0,42,1234,81733})
        {
            var mesh=OrganismMeshes.Fauna(species,seed);
            var roots=new HashSet<(double,double,double)>();
            int sole=0,mantle=0;
            for(int i=0;i<mesh.VertexCount;i++)
            {
                var p=mesh.Position(i);Assert.True(double.IsFinite(p.LengthSq));
                if(mesh.Colors[i*4+3]>.5)
                {
                    Assert.Equal(3f,mesh.UV2[i*2+1]);
                    roots.Add((Math.Round(p.X-mesh.Colors[i*4],5),Math.Round(p.Y-mesh.Colors[i*4+1],5),Math.Round(p.Z-mesh.Colors[i*4+2],5)));
                }
                else { if(mesh.UV2[i*2]==3)sole++;if(mesh.UV2[i*2]==5)mantle++; }
            }
            Assert.Equal(4,roots.Count);Assert.True(sole>100&&mantle>100);
            Assert.InRange(mesh.Bounds().Min.Y,.0019,.0021);
            foreach(int i in mesh.Indices)Assert.InRange(i,0,mesh.VertexCount-1);
            for(int i=0;i<mesh.Indices.Count;i+=3)
            {
                var a=mesh.Position(mesh.Indices[i]);var b=mesh.Position(mesh.Indices[i+1]);var c=mesh.Position(mesh.Indices[i+2]);
                Assert.True((b-a).Cross(c-a).LengthSq>1e-20,"Collapsed slug surface");
            }
            int dorsal=Enumerable.Range(0,mesh.VertexCount).Where(i=>mesh.Colors[i*4+3]<.5).OrderByDescending(i=>mesh.Position(i).Y).First();
            Assert.True(mesh.Normals[dorsal*3+1]>0,"Mantle normal faces inward");
        }
    }
}
