using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

public class BlueSundewGeometryTests
{
    [Theory]
    [InlineData(1u)]
    [InlineData(31u)]
    [InlineData(173u)]
    [InlineData(9041u)]
    public void UprightClumpsHaveFiniteBoundedSeparatedOrgans(uint seed)
    {
        var m=new MeshData(); BlueSundewGeometry.AppendClump(m,Vec3.Zero,Vec3.Up,seed);
        Assert.InRange(m.TriangleCount,2000,10000);
        Assert.All(m.Positions, x=>Assert.True(float.IsFinite(x)));
        Assert.All(m.Normals,x=>Assert.True(float.IsFinite(x)));
        var bounds=m.Bounds();
        Assert.InRange(bounds.Max.Y,.045,.09);
        Assert.InRange(bounds.Max.X-bounds.Min.X,.02,.09);
        var roles=new HashSet<int>();
        for(int i=0;i<m.VertexCount;i++)
        {
            Assert.Equal(-8f,m.UV2[i*2+1]); roles.Add((int)m.UV2[i*2]);
        }
        Assert.Equal(new[]{0,1,2},roles.OrderBy(x=>x));
        for(int t=0;t<m.Indices.Count;t+=3)
        {
            int a=m.Indices[t],b=m.Indices[t+1],c=m.Indices[t+2];
            Assert.Equal(m.UV2[a*2],m.UV2[b*2]); Assert.Equal(m.UV2[a*2],m.UV2[c*2]);
            Assert.True((m.Position(b)-m.Position(a)).Cross(m.Position(c)-m.Position(a)).LengthSq>1e-24);
        }
        var again=new MeshData(); BlueSundewGeometry.AppendClump(again,Vec3.Zero,Vec3.Up,seed);
        Assert.Equal(m.Positions,again.Positions); Assert.Equal(m.Indices,again.Indices);
    }
}
