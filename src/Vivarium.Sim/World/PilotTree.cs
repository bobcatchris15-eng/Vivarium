using Vivarium.Sim.Core;
using Vivarium.Sim.Content;
using Vivarium.Sim.Time;

namespace Vivarium.Sim.World;

/// <summary>Ecological and architectural identity of an old-growth corner tree, independent of rendering.</summary>
public sealed record PilotTreeDef(
    string Id, string Name, string Niche,
    double TrunkRadius, double TrunkHeight, double CanopyRadius,
    double RootWidth, double RootHeight, double ShadeOpacity,
    double MoistureRetention, double RootCompetition, double LitterPerDay,
    double CoarseFraction, double LitterDecayMultiplier, double FungalAffinity,
    string FruitType, Vec3 BarkColor, Vec3 FoliageColor);

public static class PilotTreeCatalog
{
    public static IReadOnlyList<PilotTreeDef> All { get; } = Array.AsReadOnly(new[]
    {
        new PilotTreeDef("gloomspire", "Gloomspire", "Cool humid hemlock canopy and mossy root shelves", 1.5, 12, 5.8, 0.42, 0.55, 0.82, 0.34, 0.14, 0.09, 0.12, 0.65, 0.95, "small_cone", new Vec3(0.30,0.25,0.20), new Vec3(0.14,0.28,0.18)),
        new PilotTreeDef("needlevault", "Needlevault", "Spruce needle beds, dry roots and wet crotches", 1.25, 13, 5.0, 0.34, 0.62, 0.70, 0.23, 0.22, 0.13, 0.10, 0.72, 0.72, "cone", new Vec3(0.36,0.27,0.20), new Vec3(0.17,0.30,0.24)),
        new PilotTreeDef("crowncoil", "Crowncoil", "Armored cycad column beneath open dappled fronds", 1.3, 8, 5.2, 0.40, 0.42, 0.43, 0.12, 0.30, 0.055, 0.25, 0.90, 0.35, "fleshy_cone", new Vec3(0.38,0.34,0.23), new Vec3(0.27,0.40,0.16)),
        new PilotTreeDef("emberpillar", "Emberpillar", "High redwood crown, fibrous bark and massive persistent wood", 2.0, 15, 6.2, 0.60, 0.90, 0.46, 0.19, 0.18, 0.08, 0.45, 0.65, 0.68, "small_cone", new Vec3(0.48,0.24,0.14), new Vec3(0.19,0.32,0.22)),
        new PilotTreeDef("basinwarden", "Basinwarden", "Broadleaf buttress walls, sheltered basins and rich leaf beds", 1.75, 11, 6.5, 0.24, 1.12, 0.68, 0.31, 0.12, 0.16, 0.10, 1.55, 0.72, "nut", new Vec3(0.39,0.38,0.27), new Vec3(0.26,0.39,0.16)),
        new PilotTreeDef("palehollow", "Palehollow", "Broken conifer crown, plated pale bark and fungal deadwood", 1.55, 10, 5.2, 0.47, 0.67, 0.56, 0.28, 0.08, 0.095, 0.55, 1.20, 1.0, "cone", new Vec3(0.62,0.59,0.49), new Vec3(0.26,0.34,0.26)),
    });

    public static PilotTreeDef? Find(string id) => All.FirstOrDefault(d => d.Id == id);
}

/// <param name="Width">Half-width of the woody root footprint.</param>
/// <param name="Height">Root crest height above GroundY.</param>
public readonly record struct PilotRootSample(Vec2 Position, double GroundY, double Width, double Height);

public sealed class PilotRootPath
{
    public IReadOnlyList<PilotRootSample> Samples { get; }
    public bool AlongSide { get; }
    public PilotRootPath(IEnumerable<PilotRootSample> samples, bool alongSide)
    { Samples = Array.AsReadOnly(samples.ToArray()); AlongSide = alongSide; }
}

/// <summary>
/// Immutable authoritative tree form. The seed and resolved descriptor reconstruct it on load;
/// roots share one footprint and height query with meshes, placement, habitat and traversal.
/// </summary>
public sealed class PilotTreeState
{
    public PilotTreeDef Def { get; }
    public int Corner { get; }
    public Vec2 Anchor { get; }
    public Vec2 CornerPoint { get; }
    public Vec2[] SideDirections { get; }
    public Vec2[] CutNormals { get; }
    public ulong Seed { get; }
    public double BaseHeight { get; }
    public HexDomain Domain => _domain;
    public IReadOnlyList<PilotRootPath> Roots { get; private set; }
    private readonly HexDomain _domain;
    private readonly Heightfield _terrain;

    private PilotTreeState(PilotTreeDef def, int corner, ulong seed, HexDomain domain, Heightfield terrain)
    {
        Def = def; Corner = corner; Seed = seed; _domain = domain; _terrain = terrain;
        CornerPoint = domain.Vertices[corner];
        var outward = CornerPoint.Normalized();
        Anchor = CornerPoint;
        BaseHeight = terrain.Height(CornerPoint);
        SideDirections = new[] { (domain.Vertices[(corner + 5) % 6] - CornerPoint).Normalized(), (domain.Vertices[(corner + 1) % 6] - CornerPoint).Normalized() };
        CutNormals = new[] { domain.EdgeNormals[(corner + 5) % 6], domain.EdgeNormals[corner] };
        var paths = new List<PilotRootPath>();
        var rng = Rng.Stream(seed, "pilot.roots");
        for (int side = 0; side < 2; side++)
        {
            var samples = new List<PilotRootSample>();
            double length = domain.Radius;
            var dir = SideDirections[side];
            var inward = -domain.EdgeNormals[side == 0 ? (corner + 5) % 6 : corner];
            samples.Add(new(Anchor, BaseHeight, def.TrunkRadius * 0.72, def.RootHeight * 1.8));
            for (int k = 0; k <= 16; k++)
            {
                double t = k / 16.0;
                var p = CornerPoint + dir * (length * t);
                double taper = 0.24 + 0.76 * Math.Pow(1 - t, 0.70);
                samples.Add(new(p, terrain.Height(p), def.RootWidth * (0.18 + taper), def.RootHeight * taper));
            }
            paths.Add(new(samples, true));
            // Unequal inward spurs make root pockets and routes, rather than a symmetric spoke wheel.
            var branch = new List<PilotRootSample>();
            var start = samples[6 + side * 2].Position;
            var branchDir = (dir * 0.32 - outward * 0.9).Normalized();
            double branchLength = domain.Radius * rng.Range(0.26, 0.38);
            for (int k = 0; k <= 8; k++)
            {
                double t = k / 8.0;
                var p = domain.ClampInside(start + branchDir * (branchLength * t), 0.04);
                branch.Add(new(p, terrain.Height(p), def.RootWidth * 0.57 * (1 - t) + 0.025, def.RootHeight * 0.48 * (1 - t)));
            }
            paths.Add(new(branch, false));
        }
        Roots = Array.AsReadOnly(paths.ToArray());
    }

    public static PilotTreeState? Generate(WorldDescriptor descriptor, HexDomain domain, Heightfield terrain)
    {
        if (descriptor.PilotTreeId == "none") return null;
        var rng = Rng.Stream(descriptor.Seed, "world.pilot");
        int seededForm = rng.NextInt(PilotTreeCatalog.All.Count), seededCorner = rng.NextInt(6);
        var def = descriptor.PilotTreeId == "random" ? PilotTreeCatalog.All[seededForm]
            : PilotTreeCatalog.Find(descriptor.PilotTreeId) ?? throw new ArgumentException("Unknown Pilot Tree: " + descriptor.PilotTreeId);
        int corner = descriptor.PilotTreeCorner < 0 ? seededCorner : descriptor.PilotTreeCorner;
        var state = new PilotTreeState(def, corner, rng.NextULong(), domain, terrain);
        descriptor.PilotTreeId = def.Id;
        descriptor.PilotTreeCorner = corner;
        return state;
    }

    private (double Distance, double Width, double Height) NearestRoot(Vec2 p)
    {
        double bestRatio = double.PositiveInfinity, bestD = double.PositiveInfinity, bestW = 0, bestH = 0;
        foreach (var path in Roots)
            for (int k = 1; k < path.Samples.Count; k++)
            {
                var a = path.Samples[k - 1]; var b = path.Samples[k];
                var ab = b.Position - a.Position;
                double t = ab.LengthSq > 1e-12 ? MathD.Clamp01((p - a.Position).Dot(ab) / ab.LengthSq) : 0;
                double d = Vec2.Distance(p, a.Position + ab * t);
                double width = MathD.Lerp(a.Width, b.Width, t), height = MathD.Lerp(a.Height, b.Height, t);
                double ratio = d / Math.Max(width, 0.001);
                if (ratio < bestRatio) { bestRatio = ratio; bestD = d; bestW = width; bestH = height; }
            }
        return (bestD, bestW, bestH);
    }

    public bool BlocksTrunk(Vec2 p, double radius = 0) => Vec2.Distance(p, Anchor) < Def.TrunkRadius * 0.91 + radius;

    /// <summary>Living ancient wood is the same attachment material as a placed log.</summary>
    public bool TryLogSurface(Vec3 p, double reach, out Vec3 surface, out Vec3 normal, out double top)
    {
        surface = default; normal = Vec3.Up; top = BaseHeight + Def.TrunkHeight;
        double best = reach;
        if (p.Y >= BaseHeight && p.Y <= top)
        {
            double t = Math.Clamp((p.Y - BaseHeight) / Def.TrunkHeight, 0, 1);
            double radius = Def.Id == "crowncoil" ? Def.TrunkRadius * (.82 + .18 * (1-t)) * (1 + .13*Math.Exp(-t*16))
                : Def.TrunkRadius * (.18 + .82*Math.Pow(1-t,.75)) * (1+.30*Math.Exp(-t*16));
            var radial = (p.XZ - Anchor).Normalized();
            var q = Anchor + radial * radius;
            double distance = Math.Abs((p.XZ-Anchor).Length-radius);
            if (_domain.Contains(q) && distance < best)
            { best = distance; normal=Vec3.FromXZ(radial,0); surface=Vec3.FromXZ(q,p.Y)+normal*.005; }
        }
        double root = RootSurfaceHeight(p.XZ);
        if (!double.IsNaN(root) && Math.Abs(p.Y-root) < best && !BlocksTrunk(p.XZ))
        { best=Math.Abs(p.Y-root);surface=Vec3.FromXZ(p.XZ,root+.005);normal=Vec3.Up;top=root; }
        return best < reach;
    }

    public bool BlocksDisc(Vec2 p, double radius = 0)
    {
        if (BlocksTrunk(p, radius)) return true;
        var root = NearestRoot(p);
        return root.Height > 0.04 && root.Distance < root.Width + radius;
    }

    /// <summary>Absolute woody top; NaN outside roots/trunk. GroundY follows terrain edits without stacked offsets.</summary>
    public double RootSurfaceHeight(Vec2 p)
    {
        if (BlocksTrunk(p)) return BaseHeight + Def.TrunkHeight;
        double rise = RootRise(p);
        return rise <= 0 ? double.NaN : _terrain.Height(p) + rise;
    }

    private double RootRise(Vec2 p, double radius = 0)
    {
        double top = 0;
        foreach (var path in Roots)
            for (int k = 1; k < path.Samples.Count; k++)
            {
                var a = path.Samples[k - 1]; var b = path.Samples[k];
                var ab = b.Position - a.Position;
                double t = ab.LengthSq > 1e-12 ? MathD.Clamp01((p - a.Position).Dot(ab) / ab.LengthSq) : 0;
                double width = MathD.Lerp(a.Width, b.Width, t) + radius;
                double d = Vec2.Distance(p, a.Position + ab * t);
                if (d >= width) continue;
                double height = MathD.Lerp(a.Height, b.Height, t);
                double section = Math.Sqrt(Math.Max(0, 1 - d * d / (width * width)));
                top = Math.Max(top, height * section);
            }
        return top;
    }

    public double RootDensity(Vec2 p)
    {
        var root = NearestRoot(p);
        return Math.Exp(-root.Distance * root.Distance / Math.Max(0.12, root.Width * root.Width * 4));
    }

    public double CanopyInfluence(Vec2 p)
    {
        double d = Vec2.Distance(p, Anchor) / Def.CanopyRadius;
        if (d >= 1.3) return 0;
        double falloff = MathD.Clamp01(1 - d / 1.3);
        // Spatially fixed sunflecks, not a uniform island modifier or per-tick randomness.
        double fleck = 0.82 + 0.18 * Noise.Fbm(Seed, p.X * 1.1, p.Z * 1.1, 2);
        return MathD.Clamp01(falloff * fleck);
    }

    public double MoistureInfluence(Vec2 p) => CanopyInfluence(p) * Def.MoistureRetention
        - RootDensity(p) * Def.RootCompetition * 0.45;

    /// <summary>Finite woody body test shared by camera movement and opaque ray picking.</summary>
    public bool ContainsVolume(Vec3 p, double radius = 0)
    {
        if (p.Y >= BaseHeight - radius && p.Y <= BaseHeight + Def.TrunkHeight + radius && BlocksTrunk(p.XZ, radius)) return true;
        if (p.Y > _terrain.MaxHeight + Def.RootHeight * 1.8 + radius) return false;
        if (p.Y < _terrain.Height(p.XZ) - 0.04 - radius) return false;
        double rise = RootRise(p.XZ, radius);
        return rise > 0.01 && p.Y < _terrain.Height(p.XZ) + rise + radius;
    }

    public (Vec3 Normal, double Depth) Penetration(Vec3 p, double radius)
    {
        double trunkDistance = Vec2.Distance(p.XZ, Anchor);
        if (p.Y >= BaseHeight - radius && p.Y <= BaseHeight + Def.TrunkHeight + radius && trunkDistance < Def.TrunkRadius * 0.91 + radius)
        {
            var radial = (p.XZ - Anchor).Normalized();
            if (radial.LengthSq < 1e-12) radial = CornerPoint.Normalized();
            double side = Def.TrunkRadius * 0.91 + radius - trunkDistance;
            double top = BaseHeight + Def.TrunkHeight + radius - p.Y;
            if (top < side) return (Vec3.Up, top);
            return (Vec3.FromXZ(radial, 0), side);
        }
        if (!ContainsVolume(p, radius)) return (Vec3.Up, 0);
        double h = RootSurfaceHeight(p.XZ);
        if (double.IsNaN(h)) h = _terrain.Height(p.XZ);
        const double epsilon = 0.015;
        double Top(Vec2 q) { double t = RootSurfaceHeight(q); return double.IsNaN(t) ? _terrain.Height(q) : t; }
        double dx = (Top(p.XZ + new Vec2(epsilon, 0)) - Top(p.XZ - new Vec2(epsilon, 0))) / (2 * epsilon);
        double dz = (Top(p.XZ + new Vec2(0, epsilon)) - Top(p.XZ - new Vec2(0, epsilon))) / (2 * epsilon);
        return (new Vec3(-dx, 1, -dz).Normalized(), Math.Max(0.001, h + radius - p.Y));
    }

    /// <summary>Baseline soil shoulders under roots deflect hydrology; wood volume remains distinct.</summary>
    public void ShapeTerrain(HexDomain domain)
    {
        for (int j = 0; j < _terrain.Nz; j++)
            for (int i = 0; i < _terrain.Nx; i++)
            {
                var p = _terrain.VertexPos(i, j);
                if (!domain.Contains(p)) continue;
                var root = NearestRoot(p);
                double q = root.Distance / Math.Max(0.08, root.Width * 1.7);
                double ridge = q < 1 ? root.Height * 0.24 * Math.Pow(1 - q * q, 2) : 0;
                _terrain.H[j * _terrain.Nx + i] = MathD.Clamp(_terrain.H[j * _terrain.Nx + i] + ridge, _terrain.MinHeight, _terrain.MaxHeight);
            }
        // Root mesh samples sit on the resulting authoritative bed, which also becomes the sculpt-save baseline.
        Roots = Array.AsReadOnly(Roots.Select(path => new PilotRootPath(path.Samples.Select(s => s with { GroundY = _terrain.Height(s.Position) }), path.AlongSide)).ToArray());
    }

    public void CoupleHabitat(VivariumWorld world, double dt)
    {
        double relax = 1 - Math.Exp(-dt / (3 * SimUnits.Hour));
        foreach (int idx in world.Grid.DomainCells)
        {
            var p = world.Grid.CellCenter(idx);
            double influence = MoistureInfluence(p);
            double baseline = world.Content.Ecology.MoistureDryBaseline;
            double target = MathD.Clamp01(baseline + influence);
            // Only retention raises dry cells; competition lowers raised woody ridges, never standing water.
            if (!world.Water.IsWet(idx))
            {
                double m = world.Fields.Moisture[idx];
                if ((influence > 0 && m < target) || (influence < 0 && m > target))
                    world.Fields.Moisture[idx] = MathD.Lerp(m, target, relax);
            }
        }
    }

    public void DepositLitter(VivariumWorld world, double biologicalSeconds)
    {
        foreach (int idx in world.Grid.DomainCells)
        {
            var p = world.Grid.CellCenter(idx);
            double bias = CanopyInfluence(p) * (0.75 + 0.25 * RootDensity(p));
            double mass = Def.LitterPerDay * bias * world.Grid.CellSize * world.Grid.CellSize * biologicalSeconds / SimUnits.Day;
            // Bounded source reservoir prevents immortality becoming unbounded litter accumulation.
            double capacity = world.Content.Ecology.DetritusMax * 1.5;
            double current = world.Litter.DetritusAt(idx);
            mass *= MathD.Clamp01(1 - current / Math.Max(capacity, 1e-6));
            mass = Math.Min(mass, Math.Max(0, capacity - current));
            if (mass > 0) world.Litter.Deposit(p, mass * (1 - Def.CoarseFraction), mass * Def.CoarseFraction);
        }
    }
}
