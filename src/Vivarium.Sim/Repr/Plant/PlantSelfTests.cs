using System;
using System.Collections.Generic;
using System.Linq;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>Shared helpers for the plant self tests.</summary>
internal static class PlantTestKit
{
    public static PlanGenerator NewGenerator(int i) => i switch
    {
        0 => new GlowbriarGenerator(),
        1 => new MoonfernGenerator(),
        _ => new ShadebellGenerator(),
    };

    public static readonly string[] SpeciesNames = { "glowbriar", "moonfern", "shadebell" };

    public static List<double> Sig(Organ[] organs)
    {
        var s = new List<double>(organs.Length * 24);
        foreach (var o in organs)
        {
            s.Add(o.Parent); s.Add((double)o.Type); s.Add((double)o.Tissue); s.Add((double)o.Flags);
            s.Add(o.ShapeId); s.Add(o.Order); s.Add(o.AttachT);
            foreach (var v in new[] { o.Start, o.End, o.StartTangent, o.EndTangent, o.Up })
            { s.Add(v.X); s.Add(v.Y); s.Add(v.Z); }
            s.Add(o.RadiusStart); s.Add(o.RadiusEnd); s.Add(o.Maturity); s.Add(o.Health); s.Add(o.Seed);
        }
        return s;
    }

    public static int FlatCount(PlantDescriptor d) =>
        d.Organs.Count(o => o.Type is OrganType.Leaf or OrganType.Leaflet);

    public static bool Finite(Vec3 v) => v.IsFinite;
}

public sealed class PlantDeterminismSelfTest : IReprSelfTest
{
    public string Name => "plant.determinism";

    public string? Run()
    {
        for (int i = 0; i < 3; i++)
        {
            var g1 = PlantTestKit.NewGenerator(i);
            var g2 = PlantTestKit.NewGenerator(i);
            var a = g1.Create(7, 1);
            var b = g2.Create(7, 1);
            if (!PlantTestKit.Sig(a.Organs).SequenceEqual(PlantTestKit.Sig(b.Organs)))
                return $"{PlantTestKit.SpeciesNames[i]}: same seed gives different seedling";
            var c = g1.Create(7, 2);
            if (PlantTestKit.Sig(a.Organs).SequenceEqual(PlantTestKit.Sig(c.Organs)))
                return $"{PlantTestKit.SpeciesNames[i]}: different plantId gives identical seedling";
            g1.Grow(a, 1.0, 0.5, null);
            g2.Grow(b, 1.0, 0.5, null);
            if (!PlantTestKit.Sig(a.Organs).SequenceEqual(PlantTestKit.Sig(b.Organs)))
                return $"{PlantTestKit.SpeciesNames[i]}: grown plants differ for same seed";
        }
        return null;
    }
}

public sealed class PlantMonotoneGrowthSelfTest : IReprSelfTest
{
    public string Name => "plant.monotone";

    public string? Run()
    {
        for (int i = 0; i < 3; i++)
        {
            string sp = PlantTestKit.SpeciesNames[i];
            var stepped = PlantTestKit.NewGenerator(i);
            var a = stepped.Create(7, 3);
            for (int k = 1; k <= 10; k++) stepped.Grow(a, k / 10.0, 0.5, null);

            var jump = PlantTestKit.NewGenerator(i);
            var bb = jump.Create(7, 3);
            jump.Grow(bb, 1.0, 0.5, null);
            if (!PlantTestKit.Sig(a.Organs).SequenceEqual(PlantTestKit.Sig(bb.Organs)))
                return $"{sp}: ten steps to 1.0 differ from one jump to 1.0";

            var midGen = PlantTestKit.NewGenerator(i);
            var mid = midGen.Create(7, 3);
            midGen.Grow(mid, 0.5, 0.5, null);
            if (mid.Organs.Length == 0 || mid.Organs.Length > a.Organs.Length)
                return $"{sp}: organ count at 0.5 is {mid.Organs.Length} vs {a.Organs.Length} at 1.0";
            for (int j = 0; j < mid.Organs.Length; j++)
            {
                var m = mid.Organs[j];
                var f = a.Organs[j];
                if (m.Parent != f.Parent || m.AttachT != f.AttachT || m.Seed != f.Seed || m.Type != f.Type)
                    return $"{sp}: organ {j} identity changed between 0.5 and 1.0";
                if ((m.Start - f.Start).Length > 1e-3)
                    return $"{sp}: organ {j} Start moved {(m.Start - f.Start).Length * 1000:0.00} mm between 0.5 and 1.0";
                if (f.Maturity + 1e-6f < m.Maturity)
                    return $"{sp}: organ {j} Maturity decreased";
            }
        }
        return null;
    }
}

public sealed class PlantAttachmentSelfTest : IReprSelfTest
{
    public string Name => "plant.attachment";

    public string? Run()
    {
        for (int i = 0; i < 3; i++)
        {
            string sp = PlantTestKit.SpeciesNames[i];
            var g = PlantTestKit.NewGenerator(i);
            var d = g.Create(11, 5);
            g.Grow(d, 1.0, 0.5, null);
            for (int j = 0; j < d.Organs.Length; j++)
            {
                var o = d.Organs[j];
                if (o.Parent < 0) continue;
                var q = d.Organs[o.Parent];
                if (!q.Type.Equals(OrganType.Axis) && !q.Type.Equals(OrganType.Petiole))
                    return $"{sp}: organ {j} parent is not an axis";
                if (q.Maturity < o.AttachT - 1e-3f)
                    return $"{sp}: organ {j} emerged before its attachment point was exposed";
                double u = Math.Clamp(o.AttachT / Math.Max(q.Maturity, 1e-6f), 0, 1);
                var c = PlantMath.Hermite(q.Start, q.StartTangent, q.End, q.EndTangent, u);
                double r = q.RadiusStart + (q.RadiusEnd - q.RadiusStart) * u;
                double dist = (o.Start - c).Length;
                if (Math.Abs(dist - r) > 1e-3)
                    return $"{sp}: organ {j} Start is {dist * 1000:0.0} mm from parent centreline, radius {r * 1000:0.0} mm";
                if (o.Type is OrganType.Axis or OrganType.Petiole)
                {
                    if (o.RadiusStart > 0.85 * r + 1e-6)
                        return $"{sp}: axis {j} radius {o.RadiusStart * 1000:0.00} mm exceeds 0.85 of parent";
                }
            }
        }
        return null;
    }
}

public sealed class PlantSanitySelfTest : IReprSelfTest
{
    public string Name => "plant.sanity";

    public string? Run()
    {
        var (lo, hi) = (new[] { 150, 50, 120 }, new[] { 1200, 260, 800 });
        for (int i = 0; i < 3; i++)
        {
            string sp = PlantTestKit.SpeciesNames[i];
            var g = PlantTestKit.NewGenerator(i);
            var d = g.Create(3, 9);
            g.Grow(d, 1.0, 0.5, null);
            if (d.Organs.Length < lo[i] || d.Organs.Length > hi[i])
                return $"{sp}: organ count {d.Organs.Length} outside [{lo[i]}, {hi[i]}]";
            foreach (var o in d.Organs)
            {
                if (!PlantTestKit.Finite(o.Start) || !PlantTestKit.Finite(o.End) || !PlantTestKit.Finite(o.Up)
                    || !PlantTestKit.Finite(o.StartTangent) || !PlantTestKit.Finite(o.EndTangent))
                    return $"{sp}: non-finite organ vector";
                if (!float.IsFinite(o.RadiusStart) || !float.IsFinite(o.RadiusEnd) || o.RadiusStart < 0 || o.RadiusEnd < 0)
                    return $"{sp}: invalid radius";
                if (o.Maturity < 0 || o.Maturity > 1.0001f) return $"{sp}: Maturity out of range";
                foreach (var p in new[] { o.Start, o.End })
                {
                    var b = d.LocalBounds;
                    if (p.X < b.Min.X - 1e-9 || p.Y < b.Min.Y - 1e-9 || p.Z < b.Min.Z - 1e-9
                        || p.X > b.Max.X + 1e-9 || p.Y > b.Max.Y + 1e-9 || p.Z > b.Max.Z + 1e-9)
                        return $"{sp}: organ endpoint outside LocalBounds";
                }
            }
            if (!(d.LeafArea > 0)) return $"{sp}: LeafArea not positive";
            if (!(d.Height > 0.2) || !(d.CrownRadius > 0.05)) return $"{sp}: implausible height/crown {d.Height:0.00}/{d.CrownRadius:0.00}";
            if (!(d.WoodVolume > 0)) return $"{sp}: WoodVolume not positive";
        }
        return null;
    }
}

public sealed class PlantCompilerSelfTest : IReprSelfTest
{
    public string Name => "plant.compiler";

    public string? Run()
    {
        for (int i = 0; i < 3; i++)
        {
            string sp = PlantTestKit.SpeciesNames[i];
            var g = PlantTestKit.NewGenerator(i);
            var d = g.Create(21, 4);
            g.Grow(d, 1.0, 0.5, null);
            int lastFlat = -1;
            for (int detail = 0; detail <= 2; detail++)
            {
                var a = PlantCompiler.Compile(d, detail);
                if (a.WoodTriangles != a.Wood.TriangleCount) return $"{sp}: WoodTriangles mismatch";
                if (a.Wood.TriangleCount == 0) return $"{sp}: no wood at detail {detail}";
                var shapes = d.Shapes.Select(s => PlantCompiler.BuildShapeMesh(s, detail)).ToArray();
                var flat = PlantCompiler.Flatten(d, a, shapes);
                if (flat.Indices.Count % 3 != 0) return $"{sp}: index count not a multiple of 3";
                foreach (int ix in flat.Indices)
                    if (ix < 0 || ix >= flat.VertexCount) return $"{sp}: index {ix} out of range";
                int degenerate = 0;
                for (int v = 0; v < flat.VertexCount; v++)
                {
                    var n = flat.NormalAt(v);
                    if (!n.IsFinite) return $"{sp}: non-finite normal";
                    if (n.LengthSq < 0.25) degenerate++;
                }
                if (degenerate > 0) return $"{sp}: {degenerate} degenerate normals at detail {detail}";
                if (detail == 0 && d.Aggregates.Length == 0) return $"{sp}: no foliage aggregates";
                if (detail > 0 && flat.TriangleCount >= lastFlat) return $"{sp}: detail {detail} not lighter than {detail - 1}";
                lastFlat = flat.TriangleCount;
            }
        }
        return null;
    }
}

public sealed class PlantSpeciesDistinctnessSelfTest : IReprSelfTest
{
    public string Name => "plant.distinct";

    public string? Run()
    {
        var ratio = new double[3];
        var leaves = new int[3];
        for (int i = 0; i < 3; i++)
        {
            var g = PlantTestKit.NewGenerator(i);
            var d = g.Create(5, 2);
            g.Grow(d, 1.0, 0.5, null);
            ratio[i] = d.Height / Math.Max(d.CrownRadius, 1e-6);
            leaves[i] = PlantTestKit.FlatCount(d);
        }
        for (int a = 0; a < 3; a++)
            for (int b = a + 1; b < 3; b++)
            {
                if (leaves[a] == leaves[b])
                    return $"{PlantTestKit.SpeciesNames[a]} and {PlantTestKit.SpeciesNames[b]} have the same leaf count";
                if (Math.Abs(ratio[a] - ratio[b]) < 0.05)
                    return $"{PlantTestKit.SpeciesNames[a]} and {PlantTestKit.SpeciesNames[b]} have the same crown ratio";
            }
        return null;
    }
}

/// <summary>Always passes. Name carries the measured footprint of one species.</summary>
public sealed class PlantFootprintGlowbriarSelfTest : IReprSelfTest
{
    private string _name = "plant.footprint glowbriar (not run)";
    public string Name => _name;
    public string? Run() { _name = PlantFootprint.Measure(new GlowbriarGenerator(), "glowbriar"); return null; }
}

public sealed class PlantFootprintMoonfernSelfTest : IReprSelfTest
{
    private string _name = "plant.footprint moonfern (not run)";
    public string Name => _name;
    public string? Run() { _name = PlantFootprint.Measure(new MoonfernGenerator(), "moonfern"); return null; }
}

public sealed class PlantFootprintShadebellSelfTest : IReprSelfTest
{
    private string _name = "plant.footprint shadebell (not run)";
    public string Name => _name;
    public string? Run() { _name = PlantFootprint.Measure(new ShadebellGenerator(), "shadebell"); return null; }
}

internal static class PlantFootprint
{
    public static string Measure(PlanGenerator g, string species)
    {
        var d = g.Create(1, 1);
        g.Grow(d, 1.0, 0.5, null);
        var a0 = PlantCompiler.Compile(d, 0);
        var shapes0 = d.Shapes.Select(s => PlantCompiler.BuildShapeMesh(s, 0)).ToArray();
        int shapeTris = shapes0.Sum(m => m.TriangleCount);
        int flat0 = PlantCompiler.Flatten(d, a0, shapes0).TriangleCount;
        var a1 = PlantCompiler.Compile(d, 1);
        int flat1 = PlantCompiler.Flatten(d, a1, d.Shapes.Select(s => PlantCompiler.BuildShapeMesh(s, 1)).ToArray()).TriangleCount;
        var a2 = PlantCompiler.Compile(d, 2);
        int flat2 = PlantCompiler.Flatten(d, a2, d.Shapes.Select(s => PlantCompiler.BuildShapeMesh(s, 2)).ToArray()).TriangleCount;
        string K(int n) => n >= 1000 ? $"{n / 1000.0:0.0}k" : n.ToString();
        return $"plant.footprint {species} organs={d.Organs.Length} descBytes={PlantCompiler.DescriptorBytes(d) / 1024.0:0.0}kB " +
               $"woodTris={K(a0.WoodTriangles)} leafInst={a0.Instances.Length} shapeTris={shapeTris} " +
               $"flatTris={K(flat0)} (d1={K(flat1)} d2={K(flat2)}) aggregates={d.Aggregates.Length}";
    }
}

public sealed class PlantExportSelfTest : IReprSelfTest
{
    public string Name => "plant.export";

    public string? Run()
    {
        const string dir = "C:/Misc/Vivarium/build/overhaul/P1";
        for (int i = 0; i < 3; i++)
        {
            string sp = PlantTestKit.SpeciesNames[i];
            var g = PlantTestKit.NewGenerator(i);
            var d = g.Create(1, 1);
            var shapes = d.Shapes.Select(s => PlantCompiler.BuildShapeMesh(s, 0)).ToArray();
            g.Grow(d, 1.0, 0.5, null);
            var a = PlantCompiler.Compile(d, 0);
            PlantObjExport.WriteObj(PlantCompiler.Flatten(d, a, shapes), $"{dir}/{sp}_age100.obj");

            var g2 = PlantTestKit.NewGenerator(i);
            var d2 = g2.Create(1, 1);
            g2.Grow(d2, 0.4, 0.5, null);
            var a2 = PlantCompiler.Compile(d2, 0);
            PlantObjExport.WriteObj(PlantCompiler.Flatten(d2, a2, shapes), $"{dir}/{sp}_age040.obj");
        }
        foreach (var sp in PlantTestKit.SpeciesNames)
        {
            foreach (var f in new[] { $"{dir}/{sp}_age100.obj", $"{dir}/{sp}_age040.obj" })
            {
                if (!System.IO.File.Exists(f) || new System.IO.FileInfo(f).Length == 0)
                    return $"missing export {f}";
            }
        }
        return null;
    }
}
