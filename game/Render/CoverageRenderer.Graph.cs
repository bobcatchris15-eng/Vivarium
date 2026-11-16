using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Repr;
using Vivarium.Sim.Repr.Plant;

namespace Vivarium.Game.Render;

public partial class CoverageRenderer
{
    // Immutable prototypes, compiled once per content definition. A prototype is a single
    // shoot subtree, not an entire colony. Lazy serializes concurrent tile builds.
    private readonly ConcurrentDictionary<string, Lazy<MeshData?>> _coverageGraphPrototypes = new(StringComparer.Ordinal);
    private const int CoverageGraphSiteBudget = 48;
    private const int CoverageGraphTriangleBudget = 12000;

    private MeshData? CoverageGraphPrototype(string id)
    {
        return _coverageGraphPrototypes.GetOrAdd(id, key => new Lazy<MeshData?>(() => {
            var sp = _w.Content.FloraById(key);
            if (sp == null || !PlantGraphCatalog.Supports(key)) return null;
            var generator = PlantGraphCatalog.Create(sp);
            var plant = generator.Create(0xC07EUL, 1);
            generator.Grow(plant, 1, 1, null);
            var all = plant.Organs;
            // Cryptogam graphs have runner -> order-2 shoot. Herb graphs can have
            // another hierarchy, so use the first non-root axis as a fallback.
            int shoot = Array.FindIndex(all, o => o.Type == OrganType.Axis && o.Order == 2);
            if (shoot < 0) shoot = Array.FindIndex(all, o => o.Type == OrganType.Axis && o.Parent >= 0);
            if (shoot < 0) shoot = Array.FindIndex(all, o => o.Type == OrganType.Axis);
            if (shoot < 0) return null;
            var keep = new bool[all.Length]; keep[shoot] = true;
            for (int i = shoot + 1; i < all.Length; i++)
                if (all[i].Parent >= 0 && keep[all[i].Parent]) keep[i] = true;
            var origin = all[shoot].Start;
            // Progressively downsample complete prefix subtrees if a prototype is too
            // expensive; remap retained parents, never leave a dangling index.
            foreach (int limit in new[] { 32, 16, 8, 4, 2, 1 })
            {
                var organs = new List<Organ>();
                var remap = new Dictionary<int, int>();
                for (int i = 0; i < all.Length && organs.Count < limit; i++) if (keep[i])
                {
                    var o = all[i]; o.Start -= origin; o.End -= origin;
                    int oldParent = o.Parent;
                    o.Parent = remap.TryGetValue(oldParent, out int mapped) ? mapped : -1;
                    remap[i] = organs.Count; organs.Add(o);
                }
                plant.Organs = organs.ToArray();
                var assembly = PlantCompiler.Compile(plant, 2);
                var shapes = plant.Shapes.Select(shape => PlantCompiler.BuildShapeMesh(shape, 2)).ToArray();
                var mesh = PlantCompiler.Flatten(plant, assembly, shapes);
                if (mesh.TriangleCount > 0 && mesh.TriangleCount <= 2000) return mesh;
            }
            return null;
        }, true)).Value;
    }

    /// <summary>Compiles or retrieves a coverage prototype for native diagnostics.</summary>
    public (int Vertices, int Triangles) CoverageGraphPrototypeCounts(string speciesId)
    {
        var mesh = CoverageGraphPrototype(speciesId);
        return mesh == null ? (0, 0) : (mesh.VertexCount, mesh.TriangleCount);
    }

    // Returns true for graph-owned species even when a site is skipped, suppressing
    // the legacy per-cell decorative geometry while leaving the coverage sheet intact.
    private bool AppendCoverageGraph(MeshData target, SpeciesRenderInfo sp, Vec3 foot, Vec3 normal,
        uint hash, float biomass, ref int sites, ref int triangles)
    {
        if (!PlantGraphCatalog.Supports(sp.SpeciesId)) return false;
        if (biomass < .12f || (hash >> 24) >= 48 || sites >= CoverageGraphSiteBudget) return true;
        var proto = CoverageGraphPrototype(sp.SpeciesId);
        if (proto == null) return false; // retain legacy structures on prototype failure
        if (triangles + proto.TriangleCount > CoverageGraphTriangleBudget) return true;
        Vec3 up = normal.Normalized();
        Vec3 u = Math.Abs(up.Y) < .99 ? Vec3.Up.Cross(up).Normalized() : Vec3.Right;
        Vec3 v = u.Cross(up).Normalized();
        double angle = (hash & 1023) * (Math.Tau / 1024.0);
        Vec3 x = u*Math.Cos(angle) + v*Math.Sin(angle);
        Vec3 z = x.Cross(up).Normalized();
        double scale = .28 * (.55 + .45*Math.Sqrt(Math.Clamp(biomass, 0f, 1f)));
        Vec3 Transform(Vec3 p) => x*p.X + up*p.Y + z*p.Z;
        int start = target.VertexCount;
        for (int i = 0; i < proto.VertexCount; i++)
        {
            var p = foot + Transform(proto.Position(i))*scale;
            var n = Transform(proto.NormalAt(i)).Normalized();
            target.AddVertex(p,n,proto.Colors[i*4],proto.Colors[i*4+1],proto.Colors[i*4+2],proto.Colors[i*4+3],
                proto.UV[i*2],proto.UV[i*2+1],proto.UV2[i*2],proto.UV2[i*2+1]);
        }
        foreach (int index in proto.Indices) target.Indices.Add(start + index);
        sites++; triangles += proto.TriangleCount;
        return true;
    }
}
