using System.Text.Json;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

public class RootcloisterCanopyTests
{
    [Theory]
    [InlineData(5,0,76UL)]
    [InlineData(10,2,424242UL)]
    [InlineData(20,5,731921UL)]
    public void RootcloisterHasIslandScaleLeafCover(double diameter,int corner,ulong seed)
    {
        var d=TestUtil.FlatDescriptor(seed);d.ResizeFootprint(diameter);
        d.PilotTreeId="rootcloister";d.PilotTreeCorner=corner;
        var w=VivariumWorld.CreateBaseline(TestUtil.Content,d);
        var mesh=PilotTreeMeshes.BuildTrunkAndCrown(w);
        double coverage=ProjectedCoverage(mesh,w.Domain);
        string dir=Path.Combine(TestUtil.RepoRoot,"build/rootcloister-open-floor-20261101");Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir,$"coverage-{diameter}.json"),JsonSerializer.Serialize(new {
            diameter,corner,seed,leaf_coverage=coverage,leaves=mesh.Leaves.Count,triangles=mesh.TriangleCount,
            canopy_radius=w.PilotTree!.CanopyRadius
        },new JsonSerializerOptions{WriteIndented=true}));
        Assert.InRange(mesh.TriangleCount,1000,2600000);
        Assert.InRange(coverage,.64,.75);
    }

    // Orthographic union of actual leaf triangles, clipped to island samples.
    // This measures leaf cover, rather than a bounding box or summed leaf area.
    public static double ProjectedCoverage(MeshData mesh,HexDomain domain)
    {
        const int n=160;double step=domain.Diameter/n;var covered=new bool[n*n];
        foreach(var leaf in mesh.Leaves)
        for(int k=leaf.FirstIndex;k<leaf.FirstIndex+leaf.IndexCount;k+=3)
        {
            var a=mesh.Position(mesh.Indices[k]).XZ;var b=mesh.Position(mesh.Indices[k+1]).XZ;var c=mesh.Position(mesh.Indices[k+2]).XZ;
            double area=(b-a).Cross(c-a);if(Math.Abs(area)<1e-12)continue;
            int x0=Math.Clamp((int)Math.Floor((Math.Min(a.X,Math.Min(b.X,c.X))+domain.Radius)/step),0,n-1);
            int x1=Math.Clamp((int)Math.Floor((Math.Max(a.X,Math.Max(b.X,c.X))+domain.Radius)/step),0,n-1);
            int z0=Math.Clamp((int)Math.Floor((Math.Min(a.Z,Math.Min(b.Z,c.Z))+domain.Radius)/step),0,n-1);
            int z1=Math.Clamp((int)Math.Floor((Math.Max(a.Z,Math.Max(b.Z,c.Z))+domain.Radius)/step),0,n-1);
            for(int z=z0;z<=z1;z++)for(int x=x0;x<=x1;x++)
            {
                var p=new Vec2(-domain.Radius+(x+.5)*step,-domain.Radius+(z+.5)*step);
                double u=(b-a).Cross(p-a)/area,v=(p-a).Cross(c-a)/area;
                if(u>=0&&v>=0&&u+v<=1)covered[z*n+x]=true;
            }
        }
        int total=0,hit=0;
        for(int z=0;z<n;z++)for(int x=0;x<n;x++)
        {
            if(!domain.Contains(new(-domain.Radius+(x+.5)*step,-domain.Radius+(z+.5)*step)))continue;
            total++;if(covered[z*n+x])hit++;
        }
        return hit/(double)total;
    }
}
