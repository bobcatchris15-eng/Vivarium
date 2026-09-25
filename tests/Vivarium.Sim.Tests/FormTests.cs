using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Geometry")]
public class FormTests
{
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
}
