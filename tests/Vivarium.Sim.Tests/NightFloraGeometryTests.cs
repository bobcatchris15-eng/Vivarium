using Vivarium.Sim.Content;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Tests;

public sealed class NightFloraGeometryTests
{
    private static FloraSpeciesDef Pampas() => new()
    {
        Id = "plumegrass", Name = "Plumegrass", Archetype = "plant", Shape = "pampas",
        Color = new[] { 0.30, 0.42, 0.18 }, Color2 = new[] { 0.68, 0.60, 0.40 },
        Height = 0.5, RadiusAtMax = 0.2,
    };

    [Fact]
    public void PampasHasStableRootedSilhouetteAndBoundedLodBudgets()
    {
        var sp = Pampas();
        var full = OrganismMeshes.Flora(sp, 441, visualDetail: 0);
        var medium = OrganismMeshes.Flora(sp, 441, visualDetail: 1);
        var far = OrganismMeshes.Flora(sp, 441, visualDetail: 2);
        Assert.Equal(full.DigestHex(), OrganismMeshes.Flora(sp, 441, visualDetail: 0).DigestHex());
        Assert.True(full.TriangleCount > medium.TriangleCount);
        Assert.True(medium.TriangleCount > far.TriangleCount);
        Assert.InRange(full.TriangleCount, 500, 12000);
        Assert.All(full.Positions, v => Assert.True(float.IsFinite(v)));
        Assert.All(full.Normals, v => Assert.True(float.IsFinite(v)));
        var ys = Enumerable.Range(0, full.VertexCount).Select(i => full.Position(i).Y).ToArray();
        Assert.InRange(ys.Min(), -0.02, 0.08); // basal crown stays planted at the local origin
        Assert.True(ys.Max() > 0.7); // plume tufts rise along the stalks
    }

    [Fact]
    public void PampasJuvenileKeepsBasalLeavesAndShortensPlumes()
    {
        var sp = Pampas();
        var adult = OrganismMeshes.Flora(sp, 982, visualDetail: 1);
        var juvenile = OrganismMeshes.Flora(sp, 982, juvenile: true, visualDetail: 1);
        Assert.True(juvenile.TriangleCount <= adult.TriangleCount);
        var adultMaxY = Enumerable.Range(0, adult.VertexCount).Max(i => adult.Position(i).Y);
        var youngMaxY = Enumerable.Range(0, juvenile.VertexCount).Max(i => juvenile.Position(i).Y);
        Assert.True(youngMaxY < adultMaxY);
        Assert.True(youngMaxY > 0.4);
    }

    [Fact]
    public void SiltstarsAreRootedLowCarpetLeavesRatherThanWaterSurfaceGeometry()
    {
        var sp = new FloraSpeciesDef
        {
            Id = "siltstars", Name = "Siltstars", Archetype = "plant", Shape = "carpet",
            Color = new[] { 0.24, 0.48, 0.34 }, Color2 = new[] { 0.52, 0.72, 0.57 },
            Height = 0.08, RadiusAtMax = 0.3,
        };
        var mesh = OrganismMeshes.Flora(sp, 553, visualDetail: 0);
        var ys = Enumerable.Range(0, mesh.VertexCount).Select(i => mesh.Position(i).Y).ToArray();
        Assert.InRange(ys.Min(), -0.02, 0.04);
        Assert.InRange(ys.Max(), 0.04, 0.14);
        Assert.True(mesh.UV2.Where((_, index) => index % 2 == 1).Count(v => v > 0.5f) > mesh.VertexCount / 2);
        Assert.True(mesh.TriangleCount > 200);
    }

    [Fact]
    public void GlowbriarHasItsOwnLowCanedFormDistinctFromLanternbrush()
    {
        var briar = new FloraSpeciesDef
        {
            Id = "glowbriar", Name = "Glowbriar", Archetype = "plant", Shape = "shrub_glowbriar",
            Color = new[] { 0.12, 0.28, 0.27 }, Color2 = new[] { 0.32, 0.58, 0.47 },
            Height = 1.12, RadiusAtMax = 0.72,
        };
        var lantern = new FloraSpeciesDef
        {
            Id = "lanternbrush", Name = "Lanternbrush", Archetype = "plant", Shape = "shrub_lanternbrush",
            Color = briar.Color, Color2 = briar.Color2, Height = briar.Height, RadiusAtMax = briar.RadiusAtMax,
        };
        var mesh = OrganismMeshes.Flora(briar, 9162);
        var comparison = OrganismMeshes.Flora(lantern, 9162);
        Assert.NotEqual(mesh.DigestHex(), comparison.DigestHex());
        var ys = Enumerable.Range(0, mesh.VertexCount).Select(i => mesh.Position(i).Y).ToArray();
        Assert.InRange(ys.Min(), -0.02, 0.02);
        Assert.InRange(ys.Max(), 0.30, 0.65);
        Assert.True(mesh.TriangleCount is > 300 and < 9000);
        Assert.True(Enumerable.Range(0, mesh.VertexCount).Count(i => mesh.UV2[i * 2] is > 1.30f and < 1.50f) > 20,
            "Underside glow nodules must be explicitly marked for organ-only emission.");
    }

    [Fact]
    public void LanternsedgeMarksSeedHeadsForOrganOnlyEmission()
    {
        var sp = new FloraSpeciesDef
        {
            Id = "lanternsedge", Name = "Lanternsedge", Archetype = "plant", Shape = "reed",
            Color = new[] { 0.25, 0.40, 0.22 }, Color2 = new[] { 0.58, 0.52, 0.32 },
            Height = 0.90, RadiusAtMax = 0.35,
        };
        var mesh = OrganismMeshes.Flora(sp, 7721, juvenile: false);
        int markedPods = Enumerable.Range(0, mesh.VertexCount).Count(i => mesh.UV2[i * 2] is > 1.30f and < 1.50f);
        Assert.True(markedPods > 20, "Seed pod cylinder vertices must carry organ marker UV2.x in 1.3..1.5.");
        int stems = Enumerable.Range(0, mesh.VertexCount).Count(i => mesh.UV2[i * 2] == 0f);
        Assert.True(stems > markedPods, "Stem and sheath vertices must remain unmarked.");
    }

    [Fact]
    public void GhostbonnetMarksUndersideGillsDistinctFromCapAndStems()
    {
        var sp = new FloraSpeciesDef
        {
            Id = "ghostbonnet", Name = "Ghostbonnet", Archetype = "fungus", Shape = "mushroom_cluster",
            Color = new[] { 0.34, 0.39, 0.48 }, Color2 = new[] { 0.67, 0.74, 0.82 },
            Height = 0.22, RadiusAtMax = 0.10,
        };
        var mesh = OrganismMeshes.Flora(sp, 3141);
        int gillVertices = Enumerable.Range(0, mesh.VertexCount).Count(i => Math.Abs(mesh.UV2[i * 2] - 1.0f) < 1e-4f);
        int capVertices = Enumerable.Range(0, mesh.VertexCount).Count(i => Math.Abs(mesh.UV2[i * 2]) < 1e-4f);
        Assert.True(gillVertices > 50, "Gills must be tagged with UV2.x = 1.0.");
        Assert.True(capVertices > gillVertices, "Cap and stipe vertices must have UV2.x = 0.0.");
    }

    [Fact]
    public void GhostbonnetDetailTiersKeepWholeBonnetsAndTheirGillMarkers()
    {
        var sp=TestUtil.Content.FloraOrThrow("ghostbonnet");int top=0;
        for(int tier=0;tier<3;tier++)
        {
            var mesh=OrganismMeshes.FloraTier(sp,3141,tier);
            if(tier==0)top=mesh.TriangleCount;
            Assert.InRange(mesh.TriangleCount,1,OrganismMeshes.FloraTierBudget(top,tier));
            Assert.True(mesh.EssentialSpans.Count>=3);
            foreach(var span in mesh.EssentialSpans)
            {
                var vertices=Enumerable.Range(span.FirstVertex,span.VertexCount);
                Assert.Contains(vertices,v=>mesh.UV2[v*2]==1f&&mesh.NormalAt(v).Y<-.5);
                Assert.Contains(vertices,v=>mesh.UV2[v*2]==0f&&mesh.NormalAt(v).Y>.5);
            }
        }
    }

    [Fact]
    public void TidebellLeavesHaveEdgePerimeterVerticesDistinctFromCenter()
    {
        var sp = new FloraSpeciesDef
        {
            Id = "tidebell", Name = "Tidebell", Archetype = "plant", Shape = "pairedleaf",
            Color = new[] { 0.15, 0.35, 0.28 }, Color2 = new[] { 0.45, 0.70, 0.55 },
            Height = 0.40, RadiusAtMax = 0.35,
        };
        var mesh = OrganismMeshes.Flora(sp, 4242);
        int leafVerts = Enumerable.Range(0, mesh.VertexCount).Count(i => mesh.UV2[i * 2 + 1] > 0.5f);
        Assert.True(leafVerts > 50, "Foliage blades must carry UV2.y = 1.0.");
        int edgeVerts = Enumerable.Range(0, mesh.VertexCount)
            .Count(i => mesh.UV2[i * 2 + 1] > 0.5f && Math.Abs(mesh.UV[i * 2 + 1] - 0.5f) > 0.45f);
        Assert.True(edgeVerts > 20, "Perimeter rim vertices must have |UV.y - 0.5| > 0.45.");
    }
}
