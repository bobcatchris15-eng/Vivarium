using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

/// <summary>The ghost is intentionally an airy, luminous membrane organism;
/// Dewbonnet retains its grounded, compact, conventional fungus anatomy.</summary>
public sealed class GhostbonnetContrastTests
{
    [Fact]
    public void GhostbonnetHasDistinctFewerAndMoreOpenWholeBonnets()
    {
        var ghost=TestUtil.Content.FloraOrThrow("ghostbonnet");
        var dew=TestUtil.Content.FloraOrThrow("dewbonnet");
        var g=OrganismMeshes.Flora(ghost,9112);
        var d=OrganismMeshes.Flora(dew,9112);
        Assert.NotEqual(g.DigestHex(),d.DigestHex());
        Assert.InRange(g.EssentialSpans.Count,12,16);
        Assert.True(d.Structural.Count>g.EssentialSpans.Count*2,
            $"Ordinary Dewbonnet must have many more fruiting bodies: dew tubes {d.Structural.Count}, ghost bonnets {g.EssentialSpans.Count}");
        Assert.InRange(g.TriangleCount,5000,OrganismMeshes.FloraTopTriangleCap);
        // Visual species dimensions, not only normalized mesh coordinates.
        Assert.True(g.Bounds().Max.Y*ghost.Height > d.Bounds().Max.Y*dew.Height*1.7,
            "Ghostbonnet must rise well above Dewbonnet in the actual world.");
        Assert.Equal(g.DigestHex(),OrganismMeshes.Flora(ghost,9112).DigestHex());
        Assert.Equal(d.DigestHex(),OrganismMeshes.Flora(dew,9112).DigestHex());
    }

    [Fact]
    public void EverySpectralCapRetainsThinMembraneAndPhysicallyExposedGills()
    {
        var g=OrganismMeshes.Flora(TestUtil.Content.FloraOrThrow("ghostbonnet"),14561);
        Assert.InRange(g.EssentialSpans.Count,12,16);
        foreach(var span in g.EssentialSpans)
        {
            var v=Enumerable.Range(span.FirstVertex,span.VertexCount);
            Assert.Contains(v,i=>g.UV2[i*2]==0f&&g.UV2[i*2+1]>.5f&&g.NormalAt(i).Y>.5);
            Assert.Contains(v,i=>g.UV2[i*2]==1f&&g.NormalAt(i).Y<-.5);
            Assert.Contains(v,i=>g.UV2[i*2]==1f&&Math.Abs(g.NormalAt(i).Y)<.4);
        }
        Assert.All(g.Positions,f=>Assert.True(float.IsFinite(f)));
        Assert.All(g.Normals,f=>Assert.True(float.IsFinite(f)));
    }

    [Fact]
    public void YoungSpectralColonyIsShorterAndHasWholeBonnets()
    {
        var sp=TestUtil.Content.FloraOrThrow("ghostbonnet");
        var mature=OrganismMeshes.Flora(sp,8000);
        var juvenile=OrganismMeshes.Flora(sp,8000,juvenile:true);
        Assert.True(juvenile.EssentialSpans.Count<mature.EssentialSpans.Count);
        Assert.True(juvenile.Bounds().Max.Y<mature.Bounds().Max.Y);
        Assert.All(juvenile.EssentialSpans,span=>
            Assert.Contains(Enumerable.Range(span.FirstVertex,span.VertexCount),
                i=>juvenile.UV2[i*2]==1f&&juvenile.NormalAt(i).Y<-.5));
    }
}
