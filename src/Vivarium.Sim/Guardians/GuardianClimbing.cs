using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Guardians;

public sealed record GuardianClimbStep(Vec3 Position, Vec3 Normal);

/// <summary>Routes sampled from the same woody triangles used by rendering.
/// No canopy bounds, leaf cards, or approximate trunk cylinders are supports.</summary>
public sealed class GuardianClimbing
{
    private readonly VivariumWorld _world;
    private Surface? _tree;
    private readonly Dictionary<(string, int), MeshData> _shrubs = new();
    public GuardianClimbing(VivariumWorld world) => _world = world;

    private sealed class Surface
    {
        private readonly record struct Triangle(Vec3 A, Vec3 B, Vec3 C);
        private readonly Dictionary<int, List<Triangle>> _heights = new();
        public Surface(MeshData mesh, Func<Vec3, Vec3>? transform = null, bool pilot = false)
        {
            var leaves = new bool[mesh.VertexCount];
            foreach (var span in mesh.LeafSpans)
                for (int i = span.FirstVertex; i < span.FirstVertex + span.VertexCount; i++) leaves[i] = true;
            for (int i = 0; i + 2 < mesh.Indices.Count; i += 3)
            {
                int ia = mesh.Indices[i], ib = mesh.Indices[i + 1], ic = mesh.Indices[i + 2];
                if (leaves[ia] || leaves[ib] || leaves[ic]) continue;
                if (pilot && (mesh.Colors[ia * 4 + 3] < .9 || mesh.Colors[ib * 4 + 3] < .9 || mesh.Colors[ic * 4 + 3] < .9)) continue;
                var a = mesh.Position(ia); var b = mesh.Position(ib); var c = mesh.Position(ic);
                if (transform != null) { a = transform(a); b = transform(b); c = transform(c); }
                var t = new Triangle(a, b, c);
                int lo = (int)Math.Floor(Math.Min(a.Y, Math.Min(b.Y, c.Y)) * 4);
                int hi = (int)Math.Floor(Math.Max(a.Y, Math.Max(b.Y, c.Y)) * 4);
                for (int y = lo; y <= hi; y++)
                {
                    if (!_heights.TryGetValue(y, out var list)) _heights[y] = list = new();
                    list.Add(t);
                }
            }
        }
        public bool Ray(Vec3 origin, Vec3 direction, double reach, out GuardianClimbStep hit)
        {
            hit = new(Vec3.Zero, Vec3.Up); bool found = false;
            if (!_heights.TryGetValue((int)Math.Floor(origin.Y * 4), out var triangles)) return false;
            foreach (var t in triangles)
            {
                var ab = t.B - t.A; var ac = t.C - t.A;
                var p = direction.Cross(ac); double det = ab.Dot(p);
                if (Math.Abs(det) < 1e-12) continue;
                var offset = origin - t.A; double u = offset.Dot(p) / det;
                if (u < 0 || u > 1) continue;
                var q = offset.Cross(ab); double v = direction.Dot(q) / det;
                if (v < 0 || u + v > 1) continue;
                double distance = ac.Dot(q) / det;
                if (distance < 0 || distance >= reach) continue;
                var normal = ab.Cross(ac).Normalized();
                if (normal.Dot(direction) > 0) normal = -normal;
                hit = new(origin + direction * distance + normal * .006, normal);
                reach = distance; found = true;
            }
            return found;
        }
    }

    public List<GuardianClimbStep>? FindRoute(GuardianIndividual guardian, out ulong hostId)
    {
        hostId = 0;
        if (GuardianCatalog.Find(guardian.ArchetypeId)?.BehaviorProfile?.CanClimbTrunk != true) return null;
        var candidates = _world.Flora.ItemsNear(guardian.PositionXZ, 4)
            .Where(f => { var s = _world.Content.FloraOrThrow(f.SpeciesId); return (s.IsShrub || s.Woody != null) && s.Climber == null && f.Health > .5; })
            .OrderBy(f => Vec2.DistanceSq(f.Position, guardian.PositionXZ)).ThenBy(f => f.Id.Value).Take(4);
        // Alternate tree and shrub opportunities deterministically.
        bool treeFirst = (guardian.RngCounter & 1) == 0;
        if (treeFirst && TreeRoute(guardian) is { } treeRoute) return treeRoute;
        foreach (var f in candidates)
        {
            var species = _world.Content.FloraOrThrow(f.SpeciesId);
            double height = species.Height * (.45 + .55 * Math.Sqrt(f.BiomassFraction(species)));
            var growth = FloraGeometryPlacement.Upright(_world, f.Id.Value, f.Position, f.Health, f.Radius(species), height);
            if (growth.Height < .8 || growth.Radius < .2) continue;
            ulong hash = Rng.Mix(f.Id.Value, 0xF10);
            int morph = (int)((hash >> 8) % (ulong)FloraGeometryPlacement.MorphCount(species.Shape));
            var key = (species.Id, morph);
            if (!_shrubs.TryGetValue(key, out var mesh))
            {
                if (_shrubs.Count >= 12) _shrubs.Clear();
                ulong seed = Rng.Mix(Hash.Fnv1a64("flora.visual." + species.Id), (ulong)(morph + 1) * 0x9E3779B97F4A7C15UL);
                _shrubs[key] = mesh = OrganismMeshes.FloraTier(species, seed, 0, tieredSource: true);
            }
            var origin = Vec3.FromXZ(f.Position, _world.GroundHeight(f.Position));
            var surface = new Surface(mesh, p => FloraGeometryPlacement.Transform(p, origin, growth.Up, (hash % 6283) / 1000f, growth.Radius, growth.Height));
            var outward = (guardian.PositionXZ - f.Position).Normalized();
            if (outward.LengthSq < .1) outward = new Vec2(1, 0);
            var route = Trace(surface, origin, outward, Math.Max(.5, growth.Radius * 1.5), Math.Min(2.5, growth.Height * .8));
            if (route != null) { hostId = f.Id.Value; return route; }
        }
        return treeFirst ? null : TreeRoute(guardian);
    }

    private List<GuardianClimbStep>? TreeRoute(GuardianIndividual guardian)
    {
        if (_world.PilotTree is not { } tree) return null;
        _tree ??= new Surface(PilotTreeMeshes.BuildTrunkAndCrown(_world), pilot: true);
        var outward = -tree.Anchor.Normalized();
        return Trace(_tree, Vec3.FromXZ(tree.Anchor, tree.BaseHeight), outward, tree.Def.TrunkRadius * 2 + 1, Math.Min(3.5, tree.Def.TrunkHeight * .4));
    }

    private List<GuardianClimbStep>? Trace(Surface surface, Vec3 origin, Vec2 outward, double radius, double height)
    {
        var n = Vec3.FromXZ(outward, 0); var side = new Vec3(-n.Z, 0, n.X);
        var result = new List<GuardianClimbStep>();
        for (double y = .12; y <= height; y += .12)
        {
            var start = origin + n * radius + Vec3.Up * y;
            if (!surface.Ray(start, -n, radius * 2, out var hit)) { if (result.Count > 0) break; else continue; }
            if (!_world.Domain.ContainsDisc(hit.Position.XZ, .08) || hit.Normal.Dot(n) < .25) break;
            // Two additional contact points require real wood wide enough for paws.
            if (!surface.Ray(start + side * .025, -n, radius * 2, out var left) ||
                !surface.Ray(start - side * .025, -n, radius * 2, out var right) ||
                (left.Position - hit.Position).Length > .12 || (right.Position - hit.Position).Length > .12) break;
            if (result.Count == 0)
            {
                if (hit.Position.Y < _world.SurfaceHeight(hit.Position.XZ) + .03) continue;
                var entry = hit.Position + hit.Normal * .45;
                entry = Vec3.FromXZ(entry.XZ, _world.GroundHeight(entry.XZ));
                if (!_world.Domain.Contains(entry.XZ) || _world.PilotTree?.BlocksTrunk(entry.XZ, .35) == true ||
                    Math.Abs(entry.Y - hit.Position.Y) > .65) continue;
                result.Add(new(entry, Vec3.Up));
            }
            else if ((hit.Position - result[^1].Position).Length > .28) break;
            result.Add(hit);
        }
        return result.Count >= 7 && result[^1].Position.Y - result[0].Position.Y > .5 ? result : null;
    }
}
