using Vivarium.Sim.Core;
using Vivarium.Sim.Water;
namespace Vivarium.Sim.Geometry;

/// <summary>A small hollow spring lip gives the downward liquid jet a visible physical origin.</summary>
public static class SpringOutletMesh
{
    public static MeshData Build()
    {
        var m=PropMeshes.SpringVent(0x51A);
        Vec3[] path=[new(-.065,-.01,0),new(-.065,.08,0),new(-.059,.112,0),new(-.04,.132,0),new(-.02,.132,0),new(-.003,.12,0),new(0,LiquidDynamics.OutletHeight,0)];
        double[] radii=Enumerable.Repeat(.026,path.Length).ToArray();
        Primitives.Tube(m,path,radii,16,(i,t)=>(new[]{.18,.185,.17},1,t,i/(double)(path.Length-1),0,0));
        int start=m.VertexCount;double[] inner=Enumerable.Repeat(.020,path.Length).ToArray();
        Primitives.Tube(m,path,inner,16,(i,t)=>(new[]{.055,.06,.05},1,t,i/(double)(path.Length-1),0,0));
        // Inside the hollow lip, the normals face into the passage.
        for(int i=start;i<m.VertexCount;i++)for(int j=0;j<3;j++)m.Normals[i*3+j]*=-1;
        return m;
    }
}
