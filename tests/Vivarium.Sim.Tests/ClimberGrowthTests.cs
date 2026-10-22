using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;
using Vivarium.Sim.World;
using Xunit;

namespace Vivarium.Sim.Tests;

/// <summary>
/// The surface-bound vine growth law: a shoot grows from its tip, lays stem down on whatever surface it is
/// touching, and the surface's geometry decides where the stem ends up.
/// </summary>
public class ClimberGrowthTests
{
    private static FloraSpeciesDef Sp(string id) => TestUtil.Content.FloraOrThrow(id);

    private static void Run(VivariumWorld w, double days, Action? everyDay = null)
    {
        int steps = (int)(days * 86400 / 600);
        for (int i = 0; i < steps; i++)
        {
            if (i % 144 == 0) everyDay?.Invoke();
            w.FloraSystem.Step(600);
        }
    }

    // ------------------------------------------------------------------ the law itself

    [Fact]
    public void TransportedReferenceIsIdentityOnAFlatSurface()
    {
        var lateral = new Vec3(1, 0, 0);
        var moved = ClimberGrowth.Transport(lateral, Vec3.Up, Vec3.Up);
        Assert.True(Math.Abs(moved.X - 1) < 1e-9 && Math.Abs(moved.Y) < 1e-9 && Math.Abs(moved.Z) < 1e-9,
            $"a flat surface must not turn the shoot's frame, got {moved}");
    }

    [Fact]
    public void TransportedReferenceTurnsWithTheSurfaceAroundAHost()
    {
        var lateral = new Vec3(0, 0, 1);          // along the trunk
        var normal = new Vec3(1, 0, 0);           // facing out of it
        var turned = new Vec3(Math.Cos(0.5), 0, Math.Sin(0.5));
        var moved = ClimberGrowth.Transport(lateral, normal, turned);
        // The frame turns by exactly the angle the surface normal turned by.
        double ang = Math.Atan2(moved.Z, moved.X);
        Assert.True(Math.Abs(ang - (Math.PI / 2 + 0.5)) < 1e-6,
            $"the frame should follow the surface, got {ang:0.0000} rad, expected {Math.PI / 2 + 0.5:0.0000}");
    }

    /// <summary>
    /// Marches a shoot along a round host with nothing but the growth law, and reports how far around the host
    /// it wound. A flat host gives no winding; the same law on a round host does.
    /// </summary>
    private static (double Turns, double Rise) WindAroundHost(double hostRadius, double climbWeight, double driftWeight)
    {
        const double radius = 0.25;
        const double ds = 0.02;
        var p = new Vec3(radius, 0, 0);
        var normal = new Vec3(1, 0, 0);
        var lateral = new Vec3(0, 0, 1);
        double arc = 0, turns = 0, prev = 0;
        for (int i = 0; i < 600; i++)
        {
            var heading = ClimberGrowth.Heading(normal, lateral, arc, Vec3.Up, climbWeight, Vec3.Up, 0, driftWeight, 0, 1);
            var raw = p + heading * ds;
            // Project back onto the cylinder: the closest point at the same height.
            double h = Math.Sqrt(raw.X * raw.X + raw.Z * raw.Z);
            var next = new Vec3(raw.X / h * radius, raw.Y, raw.Z / h * radius);
            var nextNormal = new Vec3(next.X / radius, 0, next.Z / radius);
            double th = Math.Atan2(next.Z, next.X);
            double d = th - prev;
            while (d > Math.PI) d -= 2 * Math.PI;
            while (d < -Math.PI) d += 2 * Math.PI;
            turns += d;
            prev = th;
            lateral = ClimberGrowth.Transport(lateral, normal, nextNormal);
            normal = nextNormal;
            p = next;
            arc += ds;
        }
        return (turns / (2 * Math.PI), p.Y);
    }

    [Fact]
    public void TheSameLawWindsAroundARoundHost()
    {
        var (turns, rise) = WindAroundHost(0.25, 1.0, 1.2);
        Assert.True(turns > 0.5, $"a shoot with a lateral weight should wind around a round host, got {turns:0.00} turns");
        Assert.True(rise > 1.0, $"and it should still climb while it winds, rose {rise:0.00} m");
    }

    [Fact]
    public void WithoutALateralWeightAShootClimbsStraightUp()
    {
        var (turns, rise) = WindAroundHost(0.25, 1.0, 0.0);
        Assert.True(Math.Abs(turns) < 0.05, $"a shoot with no lateral weight should not wind, got {turns:0.00} turns");
        Assert.True(rise > 5.0, $"it should climb instead, rose {rise:0.00} m");
    }

    // ------------------------------------------------------------------ the simulation

    [Fact]
    public void ARunnerWithoutSupportStaysOnTheGroundAndStops()
    {
        var w = TestUtil.FlatWorld(61);
        TestUtil.Condition(w, 0.60, 0.9, 0.65);
        var sp = Sp("clinglace");
        var founder = w.FloraSystem.Establish(sp, Vec2.Zero, "climber test", sp.MaxBiomass * 0.65);

        Run(w, 14, () => TestUtil.Condition(w, 0.60, 0.9, 0.65));

        var f = w.Flora.Get(founder.Id);
        Assert.NotNull(f);
        var segs = f!.ClimberSegments;
        Assert.NotNull(segs);
        Assert.True(segs!.Count > 10, $"a runner should lay down a stem, got {segs.Count} nodes");

        // Every node of an unsupported runner lies on the ground.
        foreach (var s in segs)
        {
            Assert.True(Math.Abs(s.Position.Y - w.GroundHeight(s.Position.XZ)) < 0.02,
                $"a ground runner must stay on the ground, node at y={s.Position.Y:0.000} but ground is {w.GroundHeight(s.Position.XZ):0.000}");
        }
        Assert.False(f.ClimberAttached, "a runner with nothing to climb must not report itself attached");
    }

    [Fact]
    public void ARunnerCannotGrowForeverWithoutSupport()
    {
        var w = TestUtil.FlatWorld(63);
        TestUtil.Condition(w, 0.60, 0.9, 0.65);
        var sp = Sp("clinglace");
        var founder = w.FloraSystem.Establish(sp, Vec2.Zero, "climber test", sp.MaxBiomass * 0.65);

        Run(w, 28, () => TestUtil.Condition(w, 0.60, 0.9, 0.65));

        var f = w.Flora.Get(founder.Id);
        var segs = f!.ClimberSegments!;
        double reach = segs.Max(s => Vec2.Distance(s.Position.XZ, founder.Position));
        Assert.True(reach <= sp.Climber!.MaxUnsupportedLength * 2.0,
            $"a runner without support must give up, it reached {reach:0.00} m (limit {sp.Climber.MaxUnsupportedLength:0.00} m)");
    }

    [Fact]
    public void AVineReachesAHostAndClimbsIt()
    {
        var w = TestUtil.FlatWorld(62);
        TestUtil.Condition(w, 0.62, 0.9, 0.75);
        var supportSp = Sp("shadebell");
        var support = w.FloraSystem.Establish(supportSp, new Vec2(1.8, 0), "support", supportSp.MaxBiomass);
        support.Age = supportSp.MaturityAge;
        var vineSp = Sp("clinglace");
        var founder = w.FloraSystem.Establish(vineSp, Vec2.Zero, "climber test", vineSp.MaxBiomass * 0.7);
        Assert.False(founder.ClimberAttached);

        Run(w, 34, () => TestUtil.Condition(w, 0.62, 0.9, 0.75));

        var f = w.Flora.Get(founder.Id);
        var segs = f!.ClimberSegments!;
        var climbing = segs.Where(s => s.Attached).ToList();
        Assert.True(climbing.Count > 8, $"the vine should climb the host, {climbing.Count} attached nodes");

        // It must actually go up the host, not just touch it.
        double top = climbing.Max(s => s.Position.Y);
        Assert.True(top > support.GroundHeight + 0.4,
            $"the vine should climb the trunk, it only reached {top:0.00} m (base {support.GroundHeight:0.00} m)");

        // Every climbing node lies on the bark.
        foreach (var s in climbing)
        {
            var (r, _, _) = w.FloraSystem.TreeTrunkProfile(support, supportSp, s.Position.Y);
            double dist = Math.Sqrt(Math.Pow(s.Position.X - support.X, 2) + Math.Pow(s.Position.Z - support.Z, 2));
            Assert.True(Math.Abs(dist - r) < 0.05,
                $"the stem must lie on the bark: node is {dist:0.000} m from the axis but the trunk is {r:0.000} m");
        }
        Assert.True(f.ClimberAttached, "a vine that has reached a host should report itself attached");
    }

    [Fact]
    public void AClimbingStemWindsAroundItsHost()
    {
        var w = TestUtil.FlatWorld(64);
        TestUtil.Condition(w, 0.62, 0.9, 0.75);
        var supportSp = Sp("shadebell");
        var support = w.FloraSystem.Establish(supportSp, new Vec2(1.8, 0), "support", supportSp.MaxBiomass);
        support.Age = supportSp.MaturityAge;
        var vineSp = Sp("spiralvine");
        var founder = w.FloraSystem.Establish(vineSp, Vec2.Zero, "climber test", vineSp.MaxBiomass * 0.7);

        Run(w, 34, () => TestUtil.Condition(w, 0.62, 0.9, 0.75));

        var f = w.Flora.Get(founder.Id);
        var segs = f!.ClimberSegments!;
        var climbing = segs.Where(s => s.Attached).OrderBy(s => s.Arc).ToList();
        Assert.True(climbing.Count > 8, "the vine should climb the host");

        double turns = 0;
        double prev = Math.Atan2(climbing[0].Position.Z, climbing[0].Position.X - support.X);
        for (int i = 1; i < climbing.Count; i++)
        {
            double th = Math.Atan2(climbing[i].Position.Z, climbing[i].Position.X - support.X);
            double d = th - prev;
            while (d > Math.PI) d -= 2 * Math.PI;
            while (d < -Math.PI) d += 2 * Math.PI;
            turns += d;
            prev = th;
        }
        Assert.True(Math.Abs(turns / (2 * Math.PI)) > 1.2,
            $"a twining vine should wind around a round host, it made {turns / (2 * Math.PI):0.00} turns");
    }

    [Fact]
    public void TheStemBranchesFromWhereItIs()
    {
        var w = TestUtil.FlatWorld(65);
        TestUtil.Condition(w, 0.62, 0.9, 0.75);
        var supportSp = Sp("shadebell");
        var support = w.FloraSystem.Establish(supportSp, new Vec2(1.8, 0), "support", supportSp.MaxBiomass);
        support.Age = supportSp.MaturityAge;
        var vineSp = Sp("clinglace");
        w.FloraSystem.Establish(vineSp, Vec2.Zero, "climber test", vineSp.MaxBiomass * 0.7);

        Run(w, 34, () => TestUtil.Condition(w, 0.62, 0.9, 0.75));

        var vine = w.Flora.Items.First(x => x.SpeciesId == vineSp.Id && x.ClimberSegments is { Count: > 10 });
        var segs = vine.ClimberSegments!;
        var children = segs.GroupBy(s => s.ParentIndex).ToDictionary(g => g.Key, g => g.Count());
        Assert.True(children.Values.Any(c => c > 1),
            "the stem should branch from the node it grew from, not sprout unrelated strands");
        // A lateral shoot leaves the stem at an angle, so it is not a continuation of the leader.
        var branches = segs.Where(s => s.ShootOrder > 0).ToList();
        Assert.True(branches.Count > 0, "the vine should throw side shoots");
    }

    [Fact]
    public void TheStemIsThickerAtItsBaseThanAtItsTip()
    {
        var w = TestUtil.FlatWorld(66);
        TestUtil.Condition(w, 0.62, 0.9, 0.75);
        var supportSp = Sp("shadebell");
        var support = w.FloraSystem.Establish(supportSp, new Vec2(1.8, 0), "support", supportSp.MaxBiomass);
        support.Age = supportSp.MaturityAge;
        var vineSp = Sp("clinglace");
        w.FloraSystem.Establish(vineSp, Vec2.Zero, "climber test", vineSp.MaxBiomass * 0.7);

        Run(w, 34, () => TestUtil.Condition(w, 0.62, 0.9, 0.75));

        var vine = w.Flora.Items.First(x => x.SpeciesId == vineSp.Id && x.ClimberSegments is { Count: > 10 });
        var segs = vine.ClimberSegments!;
        var base0 = segs[0].Radius;
        var tip = segs.OrderByDescending(s => s.Depth).First().Radius;
        Assert.True(base0 > tip, $"the stem must taper from base to tip, base {base0:0.0000} m, tip {tip:0.0000} m");
        Assert.True(base0 > 0.002, $"a grown stem should have some thickness, got {base0:0.0000} m");
    }

    [Fact]
    public void TheStemIsCappedAtTheSpeciesBaseRadiusAndTapersToItsTip()
    {
        foreach (var species in new[] { "clinglace", "spiralvine", "fenhook" })
        {
            var w = TestUtil.FlatWorld(68);
            TestUtil.Condition(w, 0.62, 0.9, 0.75);
            var vineSp = Sp(species);
            var cd = vineSp.Climber!;
            w.FloraSystem.Establish(vineSp, Vec2.Zero, "climber test", vineSp.MaxBiomass * 0.7);

            Run(w, 30, () => TestUtil.Condition(w, 0.62, 0.9, 0.75));

            var vine = w.Flora.Items.First(x => x.SpeciesId == species && x.ClimberSegments is { Count: > 10 });
            var segs = vine.ClimberSegments!;

            double baseR = segs[0].Radius;
            Assert.True(baseR <= cd.BaseRadius + 1e-9,
                $"{species}: the stem must never be thicker than the species cap, {baseR:0.0000} > {cd.BaseRadius:0.0000}");
            Assert.True(baseR >= cd.BaseRadius * 0.8,
                $"{species}: a grown stem should reach its species cap, it is only {baseR:0.0000} of {cd.BaseRadius:0.0000}");

            foreach (var s in segs)
                Assert.True(s.Radius >= cd.TipRadius - 1e-9,
                    $"{species}: the stem must never thin below the species tip radius, {s.Radius:0.0000} < {cd.TipRadius:0.0000}");

            // The stem tapers from the base to the tip.
            var byDepth = segs.OrderBy(s => s.Depth).ToList();
            for (int i = 1; i < byDepth.Count; i++)
                Assert.True(byDepth[i].Radius <= byDepth[i - 1].Radius + 1e-9,
                    $"{species}: the stem must taper with depth, depth {byDepth[i].Depth} is thicker ({byDepth[i].Radius:0.0000}) than depth {byDepth[i - 1].Depth} ({byDepth[i - 1].Radius:0.0000})");

            // Consecutive nodes keep the species' taper ratio, so the drawn stem has no steps in it.
            for (int i = 1; i < byDepth.Count; i++)
            {
                if (byDepth[i].Depth != byDepth[i - 1].Depth + 1) continue;
                double ratio = byDepth[i].Radius / Math.Max(byDepth[i - 1].Radius, 1e-9);
                double expected = Math.Pow(cd.StemTaper, cd.SegmentLength);
                Assert.True(Math.Abs(ratio - expected) < 1e-6,
                    $"{species}: adjacent nodes must keep the taper ratio, got {ratio:0.0000} want {expected:0.0000}");
            }
        }
    }

    [Fact]
    public void TheGrownStemAndItsSideShootsLieOnTheHostSurface()
    {
        var w = TestUtil.FlatWorld(69);
        TestUtil.Condition(w, 0.62, 0.9, 0.75);
        var supportSp = Sp("shadebell");
        var support = w.FloraSystem.Establish(supportSp, new Vec2(1.8, 0), "support", supportSp.MaxBiomass);
        support.Age = supportSp.MaturityAge;
        var vineSp = Sp("clinglace");
        w.FloraSystem.Establish(vineSp, Vec2.Zero, "climber test", vineSp.MaxBiomass * 0.7);

        Run(w, 34, () => TestUtil.Condition(w, 0.62, 0.9, 0.75));

        var vine = w.Flora.Items.First(x => x.SpeciesId == vineSp.Id && x.ClimberSegments is { Count: > 10 });
        var segs = vine.ClimberSegments!;
        var onHost = segs.Where(s => s.Support == ClimberSurfaceKind.Woody).ToList();
        Assert.True(onHost.Count > 20, $"the vine should carry a good length of stem on the host, {onHost.Count} nodes");

        // Every node that claims to lie on the host really does: the stem and its side shoots alike.
        foreach (var s in onHost)
        {
            var (r, _, _) = w.FloraSystem.TreeTrunkProfile(support, supportSp, s.Position.Y);
            double dist = Math.Sqrt(Math.Pow(s.Position.X - support.X, 2) + Math.Pow(s.Position.Z - support.Z, 2));
            Assert.True(Math.Abs(dist - r) < 0.05,
                $"stem on the host must lie on the bark: node is {dist:0.000} m from the axis, the trunk is {r:0.000} m");
            // And it points away from the trunk, not into it.
            double outward = (s.Position.X - support.X) * s.Normal.X + (s.Position.Z - support.Z) * s.Normal.Z;
            Assert.True(outward > 0, "a node on the host must face away from the trunk");
        }

        // The side shoots are part of the stem, so they lie on the bark too.
        var laterals = onHost.Where(s => s.ShootOrder > 0).ToList();
        Assert.True(laterals.Count > 0, "the vine should throw side shoots on the host");
        foreach (var s in laterals)
        {
            var (r, _, _) = w.FloraSystem.TreeTrunkProfile(support, supportSp, s.Position.Y);
            double dist = Math.Sqrt(Math.Pow(s.Position.X - support.X, 2) + Math.Pow(s.Position.Z - support.Z, 2));
            Assert.True(Math.Abs(dist - r) < 0.07,
                $"a side shoot must lie on the bark as well: {dist:0.000} m from the axis against a {r:0.000} m trunk");
        }
    }

    [Fact]
    public void VineGrowthIsDeterministic()
    {
        static string StemDigest(ulong seed)
        {
            var w = TestUtil.FlatWorld(seed);
            TestUtil.Condition(w, 0.62, 0.9, 0.75);
            var supportSp = TestUtil.Content.FloraOrThrow("shadebell");
            var support = w.FloraSystem.Establish(supportSp, new Vec2(1.8, 0), "support", supportSp.MaxBiomass);
            support.Age = supportSp.MaturityAge;
            var vineSp = TestUtil.Content.FloraOrThrow("clinglace");
            w.FloraSystem.Establish(vineSp, Vec2.Zero, "climber test", vineSp.MaxBiomass * 0.7);
            for (int i = 0; i < 4080; i++) w.FloraSystem.Step(600);
            var vine = w.Flora.Items.First(x => x.SpeciesId == vineSp.Id && x.ClimberSegments is { Count: > 10 });
            var sb = new System.Text.StringBuilder();
            foreach (var s in vine.ClimberSegments!)
                sb.Append($"{s.Position.X:0.0000},{s.Position.Y:0.0000},{s.Position.Z:0.0000};");
            return sb.ToString();
        }

        Assert.Equal(StemDigest(62), StemDigest(62));
    }

    [Fact]
    public void AVineWithoutSupportStopsGrowingRatherThanRunningAway()
    {
        var w = TestUtil.FlatWorld(67);
        TestUtil.Condition(w, 0.60, 0.9, 0.65);
        var sp = Sp("spiralvine");
        var founder = w.FloraSystem.Establish(sp, Vec2.Zero, "climber test", sp.MaxBiomass * 0.65);

        Run(w, 28, () => TestUtil.Condition(w, 0.60, 0.9, 0.65));

        var f = w.Flora.Get(founder.Id);
        var segs = f!.ClimberSegments!;
        // Every shoot must have stopped: the node budget bounds the stem, and no shoot may run further than the
        // species allows without something to hold it.
        Assert.True(segs.Count <= sp.Climber!.NodeCap, "the stem must respect the node budget");
        foreach (var s in segs)
            Assert.True(s.UnsupportedLength <= sp.Climber.MaxUnsupportedLength + 1e-6,
                $"a runner with nothing to climb must give up, one shoot ran {s.UnsupportedLength:0.00} m (limit {sp.Climber.MaxUnsupportedLength:0.00} m)");
    }
}
