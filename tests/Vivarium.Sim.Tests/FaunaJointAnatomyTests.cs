using Vivarium.Sim.Geometry;
using Vivarium.Sim.Core;
namespace Vivarium.Sim.Tests;
public class FaunaJointAnatomyTests
{
    [Theory]
    [InlineData("coalback_beetle",6)]
    [InlineData("marbleback",14)]
    public void JointChannelsSurviveMorphAndAttachEveryLimb(string species,int expected)
    {
        var mesh=OrganismMeshes.Fauna(TestUtil.Content.FaunaOrThrow(species),1234);
        Assert.Equal(mesh.VertexCount*4,mesh.Custom0.Count);Assert.Equal(mesh.VertexCount*4,mesh.Custom1.Count);
        var joints=new HashSet<(double,double,double)>();
        int socket=0,lower=0,toe=0;
        for(int i=0;i<mesh.VertexCount;i++)
        {
            Assert.True(mesh.Position(i).IsFinite);Assert.True(mesh.NormalAt(i).IsFinite);
            if(mesh.Custom0[i*4+3]<.5)continue;
            var off=new Vec3(mesh.Colors[i*4],mesh.Colors[i*4+1],mesh.Colors[i*4+2]);
            var root=mesh.Position(i)-off;
            joints.Add((Math.Round(root.X,4),Math.Round(root.Y,4),Math.Round(root.Z,4)));
            var knee=new Vec3(mesh.Custom1[i*4],mesh.Custom1[i*4+1],mesh.Custom1[i*4+2]);
            var ankle=new Vec3(mesh.Custom0[i*4],mesh.Custom0[i*4+1],mesh.Custom0[i*4+2]);
            Assert.True(knee.IsFinite&&ankle.IsFinite);Assert.True(knee.Length>.01&&(ankle-knee).Length>.01);
            double chain=mesh.Custom1[i*4+3];Assert.InRange(chain,0,2.99);
            if(chain<.2)socket++;if(chain>1&&chain<1.8)lower++;if(chain>=2)toe++;
            Assert.InRange(mesh.UV2[i*2+1],1,1.25);
        }
        Assert.Equal(expected,joints.Count);Assert.True(socket>0&&lower>0&&toe>0);
    }
    [Fact]
    public void SoftBodiesRemainFiniteAndContactPlaneSurvivesMorph()
    {
        foreach(var species in TestUtil.Content.Fauna.Where(s=>s.Model is "salamander" or "slug" or "worm"))
        foreach(ulong seed in new ulong[]{0,1234,5678})
        {
            var m=OrganismMeshes.Fauna(species,seed);
            Assert.True(m.VertexCount>=1500);Assert.True(m.Bounds().Min.Y>=-.00001);
            foreach(int i in Enumerable.Range(0,m.VertexCount)){Assert.True(m.Position(i).IsFinite);Assert.True(m.NormalAt(i).IsFinite);}
            foreach(int i in m.Indices)Assert.InRange(i,0,m.VertexCount-1);
        }
    }
}
