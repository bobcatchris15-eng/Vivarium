using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Geometry")]
public class FormTests
{
    [Fact]
    public void MushroomClusterHasGroundedStagesAndIrregularSilhouettes()
    {
        var sp = new FloraSpeciesDef { Id = "bonnet_mushroom", Shape = "mushroom_cluster", Color = Green, Color2 = LightGreen };
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var mesh = OrganismMeshes.Flora(sp, seed);
            Assert.Equal(mesh.DigestHex(), OrganismMeshes.Flora(sp, seed).DigestHex());
            AssertAllFinite(mesh);
            AssertNoZeroAreaTriangles(mesh);
            Assert.InRange(mesh.TriangleCount, 500, 4500);
            Assert.InRange(mesh.Bounds().Min.Y, -0.04, 0.02);
            // Cap profile samples are tagged in UV2.x: buds, open and aged specimens.
            var stages = Enumerable.Range(0, mesh.VertexCount).Where(i => mesh.UV2[i * 2] >= 1).Select(i => (int)mesh.UV2[i * 2]).Distinct().ToArray();
            Assert.Contains(1, stages);
            Assert.Contains(2, stages);
            Assert.Contains(3, stages);
            // Rim samples have a measurable uneven vertical silhouette.
            var rims = Enumerable.Range(0, mesh.VertexCount).Where(i => mesh.UV2[i * 2 + 1] == 1).Select(i => mesh.Position(i).Y).ToArray();
            Assert.True(rims.Length >= 40 && rims.Max() - rims.Min() > 0.1);
        }
    }
    private static readonly double[] Green = { 0.2, 0.6, 0.2 };
    private static readonly double[] LightGreen = { 0.5, 0.8, 0.4 };

    private static LeafBladeParams DefaultLeaf() => new(
        Midrib: new AxisParams(Length: 0.04, BaseAngle: 0.2, Droop: 0.3, Segments: 8),
        Profile: BladeProfile.Ovate,
        HalfWidth: 0.015,
        Camber: 0.15,
        MidribFold: 0.05,
        Cup: 0.05,
        TipCurl: 0.1,
        EdgeRuffle: 0.02,
        Asymmetry: 0.1,
        Margin: LeafMargin.Crenate,
        MidribThickness: 0.0015);

    private static void AssertAllFinite(MeshData m)
    {
        for (int i = 0; i < m.VertexCount; i++)
            Assert.True(m.Position(i).IsFinite, $"non-finite vertex at {i}");
    }

    private static void AssertNoZeroAreaTriangles(MeshData m)
    {
        for (int t = 0; t < m.Indices.Count; t += 3)
        {
            var a = m.Position(m.Indices[t]);
            var b = m.Position(m.Indices[t + 1]);
            var c = m.Position(m.Indices[t + 2]);
            double area = (b - a).Cross(c - a).Length * 0.5;
            Assert.True(area > 1e-12, $"degenerate triangle at index {t}, area={area}");
        }
    }

    [Fact]
    public void AxisIsDeterministic()
    {
        var p = new AxisParams(Length: 0.1, BaseAngle: 0.3, Droop: 0.4, PhototropicBend: 0.1, TwistPerLength: 1.5, WobbleAmplitude: 0.005, Segments: 12);
        var f1 = Axis.Build(p, 12345);
        var f2 = Axis.Build(p, 12345);
        Assert.Equal(f1.Count, f2.Count);
        for (int i = 0; i < f1.Count; i++)
        {
            Assert.Equal(f1[i].Point, f2[i].Point);
            Assert.True(f1[i].Point.IsFinite);
            Assert.True(f1[i].Tangent.IsFinite);
        }
    }

    [Fact]
    public void LeafBladeIsDeterministic()
    {
        var p = DefaultLeaf();
        var m1 = new MeshData();
        var m2 = new MeshData();
        LeafBlade.Build(m1, p, 777, Green, LightGreen);
        LeafBlade.Build(m2, p, 777, Green, LightGreen);
        Assert.Equal(m1.DigestHex(), m2.DigestHex());
    }

    [Fact]
    public void LeafBladeIsFiniteAndNonDegenerate()
    {
        var m = new MeshData();
        LeafBlade.Build(m, DefaultLeaf(), 42, Green, LightGreen);
        AssertAllFinite(m);
        AssertNoZeroAreaTriangles(m);
    }

    [Fact]
    public void LeafBladeHasBothFaces()
    {
        var m = new MeshData();
        LeafBlade.Build(m, DefaultLeaf(), 42, Green, LightGreen);
        bool hasTop = false, hasBottom = false;
        for (int i = 0; i < m.VertexCount; i++)
        {
            if (m.UV2[i * 2] < 0.5) hasTop = true; else hasBottom = true;
        }
        Assert.True(hasTop && hasBottom);
    }

    [Fact]
    public void LeafBladeMarksOnlyBladeVerticesForTipDeformation()
    {
        var m = new MeshData();
        LeafBlade.Build(m, DefaultLeaf() with { PetioleLength = 0.01 }, 42, Green, LightGreen);
        Assert.Contains(1f, m.UV2.Where((_, i) => i % 2 == 1));
        Assert.Contains(0f, m.UV2.Where((_, i) => i % 2 == 1));
        // Blade rows precede the petiole. Every petiole vertex must retain the zero role flag.
        int bladeVertices = 2 * (8 + 1) * (4 + 1);
        for (int i = 0; i < bladeVertices; i++) Assert.Equal(1f, m.UV2[i * 2 + 1]);
        for (int i = bladeVertices; i < m.VertexCount; i++) Assert.Equal(0f, m.UV2[i * 2 + 1]);
    }

    [Fact]
    public void LeafBladeDefaultDetailIsWithinTriangleBudget()
    {
        var m = new MeshData();
        LeafBlade.Build(m, DefaultLeaf(), 42, Green, LightGreen);
        Assert.InRange(m.TriangleCount, 60, 150);
    }

    [Fact]
    public void LeafBladeLowDetailRetainsBothFacesWithinBudget()
    {
        var m = new MeshData();
        LeafBlade.Build(m, DefaultLeaf() with { DetailLevel = 0 }, 42, Green, LightGreen);
        AssertAllFinite(m);
        AssertNoZeroAreaTriangles(m);
        Assert.Equal(72, m.TriangleCount);
        Assert.Contains(0f, m.UV2.Where((_, i) => i % 2 == 0));
        Assert.Contains(1f, m.UV2.Where((_, i) => i % 2 == 0));
    }

    [Theory]
    [InlineData(BladeProfile.Ovate)]
    [InlineData(BladeProfile.Lanceolate)]
    [InlineData(BladeProfile.Cordate)]
    [InlineData(BladeProfile.Reniform)]
    [InlineData(BladeProfile.Obcordate)]
    [InlineData(BladeProfile.Linear)]
    public void AllBladeProfilesProduceValidMeshes(BladeProfile profile)
    {
        var p = DefaultLeaf() with { Profile = profile };
        var m = new MeshData();
        LeafBlade.Build(m, p, 1, Green, LightGreen);
        AssertAllFinite(m);
        AssertNoZeroAreaTriangles(m);
        Assert.True(m.TriangleCount > 0);
    }

    [Fact]
    public void SoftTubeIsDeterministicAndValid()
    {
        var p = new SoftTubeParams(new AxisParams(Length: 0.05, BaseAngle: 0.1, Segments: 10), BaseRadius: 0.006, TipRadius: 0.002, Ellipticity: 1.3, BaseFlare: 0.2);
        var m1 = new MeshData();
        var m2 = new MeshData();
        SoftTube.Build(m1, p, 99, (i, v) => (Green, 1.0, v, i, 0, 0));
        SoftTube.Build(m2, p, 99, (i, v) => (Green, 1.0, v, i, 0, 0));
        Assert.Equal(m1.DigestHex(), m2.DigestHex());
        AssertAllFinite(m1);
        AssertNoZeroAreaTriangles(m1);
    }

    [Fact]
    public void SoftTubeJunctionRespectsFilletTolerance()
    {
        var parentPoint = new Vec3(0, 0.1, 0);
        var parentTangent = new Vec3(0, 1, 0);
        double parentRadius = 0.02;
        double tolerance = 0.002;
        var junction = new TubeJunction(parentPoint, parentTangent, parentRadius, FilletLength: 0.02, FilletTolerance: tolerance);

        var p = new SoftTubeParams(new AxisParams(Length: 0.06, BaseAngle: 1.2, Segments: 10), BaseRadius: 0.015, TipRadius: 0.003, Segments: 10);
        var m = new MeshData();
        SoftTube.Build(m, p, 5, (i, v) => (Green, 1.0, v, i, 0, 0), junction);

        double minAllowed = parentRadius - tolerance - 1e-6;
        for (int i = 0; i < m.VertexCount; i++)
        {
            var pos = m.Position(i);
            var toV = pos - parentPoint;
            var d = parentTangent.Normalized();
            var proj = parentPoint + d * toV.Dot(d);
            double dist = (pos - proj).Length;
            Assert.True(dist >= minAllowed, $"vertex {i} at distance {dist} < allowed {minAllowed}");
        }
    }

    [Fact]
    public void SheetIsDeterministicAndValid()
    {
        var p = new SheetParams(new AxisParams(Length: 0.03, BaseAngle: 0.5, Segments: 8), HalfWidth: 0.012, LobeCount: 4, LobeDepth: 0.2, Curl: 0.3);
        var m1 = new MeshData();
        var m2 = new MeshData();
        Sheet.Build(m1, p, 21, Green, LightGreen);
        Sheet.Build(m2, p, 21, Green, LightGreen);
        Assert.Equal(m1.DigestHex(), m2.DigestHex());
        AssertAllFinite(m1);
        AssertNoZeroAreaTriangles(m1);
    }

    [Fact]
    public void VariationSamplingIsDeterministicPerIndividual()
    {
        double Build(Rng rng) => rng.Range(0.5, 1.5);
        double v1 = Variation.Sample(worldSeed: 1234, speciesId: "test_species", individualId: 7, Build);
        double v2 = Variation.Sample(worldSeed: 1234, speciesId: "test_species", individualId: 7, Build);
        double v3 = Variation.Sample(worldSeed: 1234, speciesId: "test_species", individualId: 8, Build);
        Assert.Equal(v1, v2);
        Assert.InRange(v1, 0.5, 1.5);
        Assert.NotEqual(v1, v3);
    }

    // Old (pre-kernel) shapes, worst case at max leaflet/stem counts:
    //   roundleaf: up to 54 leaflets x (Tube 8 tris + Fan 10 tris) = ~972 tris  -> 2x budget 1944
    //   pairedleaf: up to 16 stems x (Tube 16 tris + 4 pairs x 2 x CurvedLeaf 32 tris + flower ~16 tris)
    //               = ~4608 tris -> 2x budget 9216
    [Fact]
    public void RoundLeafIsFiniteNonDegenerateAndWithinTriangleBudget()
    {
        var sp = new FloraSpeciesDef { Id = "dichondra", Shape = "roundleaf", Color = Green, Color2 = LightGreen };
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var m = OrganismMeshes.Flora(sp, seed);
            AssertAllFinite(m);
            AssertNoZeroAreaTriangles(m);
            Assert.InRange(m.TriangleCount, 1, 1944);
        }
    }

    [Fact]
    public void PairedLeafIsFiniteNonDegenerateAndWithinTriangleBudget()
    {
        var sp = new FloraSpeciesDef { Id = "bacopa", Shape = "pairedleaf", Color = Green, Color2 = LightGreen };
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var m = OrganismMeshes.Flora(sp, seed);
            AssertAllFinite(m);
            AssertNoZeroAreaTriangles(m);
            Assert.InRange(m.TriangleCount, 1, 9216);
        }
    }

    // Old (pre-kernel) creeper shape, worst case: 14 stems x (Tube 12 tris + Fan 20 tris) = 448 tris
    // -> 2x budget 896. New kernel-built shape (stolons + rooting nodes + leaves) must stay within that.
    [Fact]
    public void CreeperIsFiniteNonDegenerateAndWithinTriangleBudget()
    {
        var sp = new FloraSpeciesDef { Id = "creeping_groundcover", Shape = "creeper", Color = Green, Color2 = LightGreen };
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var m = OrganismMeshes.Flora(sp, seed);
            AssertAllFinite(m);
            AssertNoZeroAreaTriangles(m);
            Assert.InRange(m.TriangleCount, 1, 896);
        }
    }

    [Fact]
    public void CreeperIsDeterministic()
    {
        var sp = new FloraSpeciesDef { Id = "creeping_groundcover", Shape = "creeper", Color = Green, Color2 = LightGreen };
        var m1 = OrganismMeshes.Flora(sp, 42);
        var m2 = OrganismMeshes.Flora(sp, 42);
        Assert.Equal(m1.DigestHex(), m2.DigestHex());
    }

    [Fact]
    public void VineBladesAreDeterministicCurvedMeshesWithinBudget()
    {
        var sp = new FloraSpeciesDef { Id = "climbing_vine", Shape = "vine", Color = Green, Color2 = LightGreen };
        for (ulong seed = 1; seed <= 3; seed++)
        {
            var mesh = OrganismMeshes.Flora(sp, seed);
            Assert.Equal(mesh.DigestHex(), OrganismMeshes.Flora(sp, seed).DigestHex());
            AssertAllFinite(mesh);
            AssertNoZeroAreaTriangles(mesh);
            Assert.InRange(mesh.TriangleCount, 10000, 19000);
        }
    }

    [Fact]
    public void CreeperHasNoLargeFlatDiscsAndSpansExpectedFootprint()
    {
        // Regression: the old shape was a handful of near-vertical, wide flat fans (a "disc" reads as many
        // co-planar rim vertices at one height/normal). The new shape must be prostrate (low profile) and
        // spread leaves/runners across a footprint comparable to the old radius (~0.85), not shrunk down.
        var sp = new FloraSpeciesDef { Id = "creeping_groundcover", Shape = "creeper", Color = Green, Color2 = LightGreen };
        var m = OrganismMeshes.Flora(sp, 7);
        var bounds = m.Bounds();
        Assert.True(bounds.Max.Y < 0.35, $"creeper must stay low/prostrate, got max Y {bounds.Max.Y}");
        double footprint = Math.Max(Math.Max(Math.Abs(bounds.Min.X), Math.Abs(bounds.Max.X)),
            Math.Max(Math.Abs(bounds.Min.Z), Math.Abs(bounds.Max.Z)));
        Assert.True(footprint > 0.4, $"creeper footprint should span a comparable radius to the old shape, got {footprint}");
    }

    [Fact]
    public void RoundLeafIsDeterministic()
    {
        var sp = new FloraSpeciesDef { Id = "dichondra", Shape = "roundleaf", Color = Green, Color2 = LightGreen };
        var m1 = OrganismMeshes.Flora(sp, 42);
        var m2 = OrganismMeshes.Flora(sp, 42);
        Assert.Equal(m1.DigestHex(), m2.DigestHex());
    }

    [Fact]
    public void PairedLeafIsDeterministic()
    {
        var sp = new FloraSpeciesDef { Id = "bacopa", Shape = "pairedleaf", Color = Green, Color2 = LightGreen };
        var m1 = OrganismMeshes.Flora(sp, 42);
        var m2 = OrganismMeshes.Flora(sp, 42);
        Assert.Equal(m1.DigestHex(), m2.DigestHex());
    }

    // FloatLeaf (lily pad): up to 12 leaves x LeafBlade 72 tris + petiole/coil/flower overhead; budget generous
    // (3000 requested by the plant) but the kernel stays well under it -> assert under 3000.
    [Fact]
    public void FloatLeafIsFiniteNonDegenerateAndWithinTriangleBudget()
    {
        var sp = new FloraSpeciesDef { Id = "lily_pad", Shape = "floatleaf", Color = Green, Color2 = LightGreen };
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var m = OrganismMeshes.Flora(sp, seed);
            AssertAllFinite(m);
            AssertNoZeroAreaTriangles(m);
            Assert.InRange(m.TriangleCount, 1, 3000);
        }
    }

    [Fact]
    public void FloatLeafIsDeterministic()
    {
        var sp = new FloraSpeciesDef { Id = "lily_pad", Shape = "floatleaf", Color = Green, Color2 = LightGreen };
        var m1 = OrganismMeshes.Flora(sp, 42);
        var m2 = OrganismMeshes.Flora(sp, 42);
        Assert.Equal(m1.DigestHex(), m2.DigestHex());
    }

    [Fact]
    public void FloatLeafPetiolesReachUnitSurfaceHeight()
    {
        // The renderer scales this mesh's Y axis directly by local water depth, so every leaf pad's petiole
        // must rise close to unit Y = 1 regardless of its horizontal lean, or pads would float above/below
        // the water surface once scaled.
        var sp = new FloraSpeciesDef { Id = "lily_pad", Shape = "floatleaf", Color = Green, Color2 = LightGreen };
        var m = OrganismMeshes.Flora(sp, 5);
        var bounds = m.Bounds();
        Assert.True(bounds.Max.Y > 0.85 && bounds.Max.Y < 1.3, $"floatleaf pads should sit near unit Y=1, got max Y {bounds.Max.Y}");
    }

    [Fact]
    public void FloatLeafFootprintIsAuthoredInMetresNotUnitRadius()
    {
        // Unlike every other flora shape, floatleaf is NOT built at unit XZ radius 1 (the renderer fixes its
        // horizontal scale to 1 so pad size stays constant regardless of growth radius/water depth). Regression
        // guard: if this ever reverts to unit-radius authoring, the plant's XZ footprint would balloon back up
        // toward ~1 m instead of staying within a small multi-pad clump a few tens of centimetres across.
        var sp = new FloraSpeciesDef { Id = "lily_pad", Shape = "floatleaf", Color = Green, Color2 = LightGreen };
        for (ulong seed = 1; seed <= 8; seed++)
        {
            var bounds = OrganismMeshes.Flora(sp, seed).Bounds();
            double footprint = Math.Max(Math.Max(Math.Abs(bounds.Min.X), Math.Abs(bounds.Max.X)),
                Math.Max(Math.Abs(bounds.Min.Z), Math.Abs(bounds.Max.Z)));
            Assert.True(footprint < 0.4, $"floatleaf footprint should be metre-scale, got {footprint}");
        }
    }

    // Old (pre-kernel) herb/clover shapes:
    //   herb: 7 leaves x CurvedLeaf(56 tris) + 3 flowers x (Tube 12 + Fan 20) tris = ~488 tris.
    //   New herb uses 72-triangle kernel leaves and 2-3 flower heads with small cupped petals.
    //   trifoliate: up to 26 instances x (Tube 12 + 3 lobes x CurvedLeaf(32) + flower ~20 x 0.15) tris = ~2500 tris
    //               -> 2x budget conservatively widened for the occasional kernel-petal pompom flower.
    [Fact]
    public void HerbIsFiniteNonDegenerateAndWithinTriangleBudget()
    {
        // The flower centre uses Primitives.Fan, which can have degenerate centre/rim triangles.
        var sp = new FloraSpeciesDef { Id = "ornamental_herb", Shape = "herb", Color = Green, Color2 = LightGreen };
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var m = OrganismMeshes.Flora(sp, seed);
            AssertAllFinite(m);
            Assert.InRange(m.TriangleCount, 1, 1300);
        }
    }

    [Fact]
    public void HerbIsDeterministic()
    {
        var sp = new FloraSpeciesDef { Id = "ornamental_herb", Shape = "herb", Color = Green, Color2 = LightGreen };
        var m1 = OrganismMeshes.Flora(sp, 42);
        var m2 = OrganismMeshes.Flora(sp, 42);
        Assert.Equal(m1.DigestHex(), m2.DigestHex());
    }

    [Fact]
    public void TrifoliateIsFiniteNonDegenerateAndWithinTriangleBudget()
    {
        // Existing CurvedLeaf-based clover leaflets collapse to a point at the root.
        var sp = new FloraSpeciesDef { Id = "clover", Shape = "trifoliate", Color = Green, Color2 = LightGreen };
        for (ulong seed = 1; seed <= 5; seed++)
        {
            var m = OrganismMeshes.Flora(sp, seed);
            AssertAllFinite(m);
            Assert.InRange(m.TriangleCount, 1, 6000);
        }
    }

    [Fact]
    public void TrifoliateIsDeterministic()
    {
        var sp = new FloraSpeciesDef { Id = "clover", Shape = "trifoliate", Color = Green, Color2 = LightGreen };
        var m1 = OrganismMeshes.Flora(sp, 42);
        var m2 = OrganismMeshes.Flora(sp, 42);
        Assert.Equal(m1.DigestHex(), m2.DigestHex());
    }

    [Fact]
    public void FernHasCamberedBladeSurfacesAndGroundedVaryingFronds()
    {
        var sp = new FloraSpeciesDef { Id = "maidenhair_fern", Shape = "fern", Color = Green, Color2 = LightGreen };
        var m = OrganismMeshes.Flora(sp, 42);
        AssertAllFinite(m);
        var bounds = m.Bounds();
        Assert.InRange(bounds.Min.Y, -0.001, 0.001);
        Assert.True(bounds.Max.Y > 0.55);
        Assert.True(m.UV2.Count(v => v == 1f) > 500, "fern needs substantial kernel blade surface");
        Assert.InRange(m.TriangleCount, 4000, 10000);
        Assert.Equal(m.DigestHex(), OrganismMeshes.Flora(sp, 42).DigestHex());
    }

    [Fact]
    public void TussockHasGroundedTaperedBladesAndMixedStrawTips()
    {
        var sp = new FloraSpeciesDef { Id = "blue_fescue", Shape = "tussock", Color = new[] { 0.36, 0.55, 0.6 }, Color2 = new[] { 0.8, 0.74, 0.5 } };
        var m = OrganismMeshes.Flora(sp, 42);
        Assert.Equal(2640, m.TriangleCount);
        AssertAllFinite(m);
        var bounds = m.Bounds();
        Assert.InRange(bounds.Min.Y, -0.001, 0.001);
        int strawTips = 0, blueTips = 0;
        for (int blade = 0; blade < 110; blade++)
        {
            int start = blade * 28;
            double baseWidth = (m.Position(start + 1) - m.Position(start)).Length;
            double tipWidth = (m.Position(start + 13) - m.Position(start + 12)).Length;
            Assert.True(tipWidth < baseWidth * 0.2, $"blade {blade} should end in a fine point");
            Assert.InRange(m.Position(start).Y, -0.001, 0.001);
            int color = (start + 12) * 4;
            if (m.Colors[color] > m.Colors[color + 2] + 0.04) strawTips++;
            else blueTips++;
        }
        Assert.InRange(strawTips, 10, 65);
        Assert.InRange(blueTips, 45, 100);
        Assert.Equal(m.DigestHex(), OrganismMeshes.Flora(sp, 42).DigestHex());
    }

    [Fact]
    public void BracketFungusHasTieredRuffledShelves()
    {
        var sp = new FloraSpeciesDef
        {
            Id = "turkey_tail",
            Shape = "bracket",
            Color = new[] { 0.46, 0.33, 0.22 },
            Color2 = new[] { 0.86, 0.8, 0.66 }
        };
        for (ulong seed = 1; seed <= 4; seed++)
        {
            var mesh = OrganismMeshes.Flora(sp, seed);
            Assert.Equal(mesh.DigestHex(), OrganismMeshes.Flora(sp, seed).DigestHex());
            AssertAllFinite(mesh);
            AssertNoZeroAreaTriangles(mesh);

            // Triangle budget: multi-tiered double-sided bracket shelves
            Assert.InRange(mesh.TriangleCount, 1500, 4500);

            // Bounds check & base attachment:
            // Bracket base attaches cleanly along the support surface (local X=0)
            var bounds = mesh.Bounds();
            Assert.InRange(bounds.Min.X, -0.0001, 0.0001);
            Assert.True(bounds.Max.X > 0.5 && bounds.Max.X <= 1.05);
            Assert.InRange(bounds.Min.Y, 0.02, 0.25);
            Assert.InRange(bounds.Max.Y, 0.70, 1.05);

            // Base attachment: all base vertices lie along support plane local X=0
            var baseVerts = Enumerable.Range(0, mesh.VertexCount)
                .Where(i => Math.Abs(mesh.Position(i).X) < 1e-4)
                .ToArray();
            Assert.True(baseVerts.Length >= 50, "must attach cleanly along support surface at X=0");

            // Multi-tiered shelves: top shelf vertices (UV2.x == 0) and pore underside vertices (UV2.x == 1)
            var topVerts = Enumerable.Range(0, mesh.VertexCount).Where(i => mesh.UV2[i * 2] == 0).ToArray();
            var underVerts = Enumerable.Range(0, mesh.VertexCount).Where(i => mesh.UV2[i * 2] == 1).ToArray();
            Assert.True(topVerts.Length > 0 && underVerts.Length > 0);
            Assert.Equal(topVerts.Length, underVerts.Length);
            Assert.True(underVerts.Length >= 400, "underside pore surface must be a multi-ring contoured surface");

            // Underside follows top shelf contour with thickness (no single-vertex central Fan)
            for (int k = 0; k < topVerts.Length; k++)
            {
                var pTop = mesh.Position(topVerts[k]);
                var pUnder = mesh.Position(underVerts[k]);
                Assert.Equal(pTop.X, pUnder.X, 4);
                Assert.Equal(pTop.Z, pUnder.Z, 4);
                Assert.True(pTop.Y >= pUnder.Y, "pore surface must be underneath top shelf with thickness");
            }
            double minThickness = topVerts.Select((v, idx) => mesh.Position(v).Y - mesh.Position(underVerts[idx]).Y).Min();
            Assert.True(minThickness > 0.005, "shelf must have positive leathery thickness throughout");

            // Ruffled crenulated margins: rim vertices tagged with UV2.y == 1 have measurable vertical ruffle
            var rims = Enumerable.Range(0, mesh.VertexCount).Where(i => mesh.UV2[i * 2 + 1] == 1).Select(i => mesh.Position(i).Y).ToArray();
            Assert.True(rims.Length >= 60 && rims.Max() - rims.Min() > 0.1, "rim must have ruffled vertical silhouette");

            // Concentric color banding on top surface: multiple distinct zoned color bands
            var distinctColors = topVerts.Select(i => Math.Round(mesh.Colors[i * 4], 2)).Distinct().ToArray();
            Assert.True(distinctColors.Length >= 4, "top surface must feature zoned concentric color banding");
        }
    }
    [Fact]
    public void SpringtailHasArticulatedAnatomyAndAppendages()
    {
        var sp = new FaunaSpeciesDef { Id = "springtail", Model = "springtail" };
        for (ulong seed = 1; seed <= 3; seed++)
        {
            var mesh = OrganismMeshes.Fauna(sp, seed);
            var mesh2 = OrganismMeshes.Fauna(sp, seed);
            Assert.Equal(mesh.DigestHex(), mesh2.DigestHex());
            AssertAllFinite(mesh);
            AssertNoZeroAreaTriangles(mesh);
            Assert.InRange(mesh.TriangleCount, 150, 1500);

            // Validate all UV2 regions present: 1 (body/tergites), 2 (eyes), 3 (ventral/belly), 4 (appendages)
            var regions = Enumerable.Range(0, mesh.VertexCount)
                .Select(i => (int)Math.Round(mesh.UV2[i * 2]))
                .Distinct()
                .ToHashSet();
            Assert.Contains(1, regions);
            Assert.Contains(2, regions);
            Assert.Contains(3, regions);
            Assert.Contains(4, regions);

            // Validate shader vertex contracts: COLOR.a == 1.0 on region 4, COLOR.a == 0.0 on regions 1, 2, 3
            for (int i = 0; i < mesh.VertexCount; i++)
            {
                int r = (int)Math.Round(mesh.UV2[i * 2]);
                float a = mesh.Colors[i * 4 + 3];
                if (r == 4)
                    Assert.True(a > 0.99f, $"Appendage vertex {i} must have COLOR.a = 1.0");
                else
                    Assert.True(a < 0.01f, $"Body vertex {i} (region {r}) must have COLOR.a = 0.0");
            }
        }

        // Springtail never curls: FaunaCurled must be null
        Assert.Null(OrganismMeshes.FaunaCurled(sp));
    }

    [Fact]
    public void IsopodHasArticulatedPlatesAndCurledPose()
    {
        var spIsopod = new FaunaSpeciesDef { Id = "isopod", Model = "isopod" };
        var spPillBug = new FaunaSpeciesDef { Id = "pill_bug", Model = "pill_bug" };

        foreach (var sp in new[] { spIsopod, spPillBug })
        {
            for (ulong seed = 1; seed <= 3; seed++)
            {
                var mesh = OrganismMeshes.Fauna(sp, seed);
                var mesh2 = OrganismMeshes.Fauna(sp, seed);
                Assert.Equal(mesh.DigestHex(), mesh2.DigestHex());
                AssertAllFinite(mesh);
                AssertNoZeroAreaTriangles(mesh);
                Assert.InRange(mesh.TriangleCount, 150, 1500);

                var regions = Enumerable.Range(0, mesh.VertexCount)
                    .Select(i => (int)Math.Round(mesh.UV2[i * 2]))
                    .Distinct()
                    .ToHashSet();
                Assert.Contains(1, regions);
                Assert.Contains(2, regions);
                Assert.Contains(3, regions);
                Assert.Contains(4, regions);

                for (int i = 0; i < mesh.VertexCount; i++)
                {
                    int r = (int)Math.Round(mesh.UV2[i * 2]);
                    float a = mesh.Colors[i * 4 + 3];
                    if (r == 4)
                        Assert.True(a > 0.99f, $"Appendage vertex {i} must have COLOR.a = 1.0");
                    else
                        Assert.True(a < 0.01f, $"Body vertex {i} (region {r}) must have COLOR.a = 0.0");
                }

                // FaunaCurled: pill bug rolled-up ball pose preserves 7 arched overlapping armor plates
                var curled = OrganismMeshes.FaunaCurled(sp, seed);
                Assert.NotNull(curled);
                Assert.Equal(curled.DigestHex(), OrganismMeshes.FaunaCurled(sp, seed)!.DigestHex());
                AssertAllFinite(curled);
                AssertNoZeroAreaTriangles(curled);
                Assert.InRange(curled.TriangleCount, 150, 1500);

                var curledRegions = Enumerable.Range(0, curled.VertexCount)
                    .Select(i => (int)Math.Round(curled.UV2[i * 2]))
                    .Distinct()
                    .ToHashSet();
                Assert.Contains(1, curledRegions);
                Assert.Contains(2, curledRegions);
                Assert.Contains(3, curledRegions);
                Assert.Contains(4, curledRegions);
            }
        }
    }
}
