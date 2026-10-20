using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Five genuinely raised understory architectures. All points are in unit radius/height space.
/// Unlike brakes and curtains these forms retain a mostly open basal volume beneath living foliage.
/// Seeded variation is structural: forks, asymmetrical crowns, missing shelves and leaf orientations.
/// </summary>
public static partial class OrganismMeshes
{
    private static Vec3 MidRadial(double az) => new(Math.Cos(az), 0, Math.Sin(az));
    private static readonly double[] MidBark = { 0.24, 0.21, 0.16 };
    private static void MidStem(MeshData m, double[] bark, Vec3[] points, double baseRadius)
    {
        double[] r = new double[points.Length];
        for (int i = 0; i < r.Length; i++)
            r[i] = Math.Max(.0015, baseRadius * Math.Pow(1 - i / (double)points.Length, .95));
        Primitives.Tube(m, points, r, m.FloraDetailLevel == 2 ? 4 : 6,
            (i, v) => (bark, 1, i / (double)Math.Max(1, points.Length - 1), v, 0, 0));
    }

    private static void MidBlade(MeshData m, Vec3 attach, Vec3 direction, double length, double width,
        double[] c1, double[] c2, ulong leafSeed, bool round = false, double drop = .035)
    {
        var dir = direction.Normalized();
        if (dir.LengthSq < 1e-7) dir = new Vec3(1, 0, 0);
        var side = dir.Cross(Vec3.Up).Normalized();
        if (side.LengthSq < 1e-7) side = new Vec3(0, 0, 1);
        var end = attach + dir * length - Vec3.Up * drop;
        var ctrl = attach + dir * (length * .56) + Vec3.Up * (length * (round ? .06 : .09));
        CurvedBlade(m, attach, ctrl, end, side, width, c1, c2,
            segments: m.FloraDetailLevel == 2 ? 4 : 7,
            twist: (Rng.HashUnit(leafSeed, 0x901UL, 0) - .5) * .24,
            shoulder: round ? .72 : 1.10, serration: round ? 0 : .035);
    }

    // Small forked tree, compound terminal foliage on genuinely clear lower stems.
    private static void MidSplitcrown(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        int trunks = juvenile ? 1 : 2 + rng.NextInt(2);
        var bark = Primitives.Mix(MidBark, new[] { .27, .32, .22 }, .25);
        var under = Primitives.Scale(c2, .77);
        int detail = m.FloraDetailLevel ?? 0;
        for (int tr = 0; tr < trunks; tr++)
        {
            double az = tr * 2.399963 + rng.Range(-.20, .20);
            var radial = MidRadial(az);
            var basePos = radial * (tr == 0 ? 0 : rng.Range(.04, .14));
            double trunkTop = juvenile ? rng.Range(.50, .66) : rng.Range(.64, .78);
            var p1 = basePos * .50 + Vec3.Up * (trunkTop * .38);
            var fork = radial * rng.Range(.05, .13) + Vec3.Up * trunkTop;
            MidStem(m, bark, new[] { basePos, p1, fork }, juvenile ? .034 : .046);
            int branches = juvenile ? 3 : 4 + rng.NextInt(2);
            for (int b = 0; b < branches; b++)
            {
                double ba = az + 2 * Math.PI * b / branches + rng.Range(-.27, .27);
                var outward = MidRadial(ba);
                double reach = rng.Range(.46, .77);
                var join = fork + outward * (reach * .43) + Vec3.Up * rng.Range(.08, .17);
                var tip = fork + outward * reach + Vec3.Up * rng.Range(.09, .22);
                tip = new Vec3(tip.X, Math.Min(.99, tip.Y), tip.Z);
                MidStem(m, bark, new[] { fork, join, tip }, .016);
                int branchlets = detail == 2 ? 2 : (juvenile ? 2 : 3);
                for (int n = 0; n < branchlets; n++)
                {
                    var origin = join * (1 - n * .26) + tip * (n * .26);
                    var spray = (outward * rng.Range(.72, 1.0) + MidRadial(ba + Math.PI * .5) * rng.Range(-.44, .44)
                        + Vec3.Up * rng.Range(-.12, .12)).Normalized();
                    var rachis = origin + spray * rng.Range(.20, .32);
                    MidStem(m, bark, new[] { origin, rachis }, .005);
                    int pairs = detail == 2 ? 3 : 5;
                    var tangent = new Vec3(-spray.Z, 0, spray.X);
                    for (int p = 0; p < pairs; p++)
                    {
                        double t = (p + .7) / (pairs + .4);
                        var node = origin * (1 - t) + rachis * t;
                        for (int sign = -1; sign <= 1; sign += 2)
                        {
                            var ld = (spray * .28 + tangent * sign + Vec3.Up * rng.Range(-.13, .17)).Normalized();
                            double len = rng.Range(.095, .16) * (1 - .24 * t);
                            MidBlade(m, node, ld, len, len * .19, c1, under,
                                Rng.Mix(seed, (ulong)(tr * 100000 + b * 10000 + n * 1000 + p * 10 + sign + 3)));
                        }
                    }
                    MidBlade(m, rachis, spray, .12, .023, c1, under,
                        Rng.Mix(seed, (ulong)(tr * 811 + b * 47 + n * 7 + 999)));
                }
            }
        }
    }

    // Tree-fern analogue: exposed fibrous stipe under umbrella of compound drooping fronds.
    private static void MidVaultfern(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        int detail = m.FloraDetailLevel ?? 0;
        var bark = new[] { .25, .28, .19 };
        double height = juvenile ? .48 : rng.Range(.66, .78);
        var trunk = new[] { Vec3.Zero, new Vec3(.025, .24, -.02),
            new Vec3(-.025, height * .75, .022), new Vec3(0, height, 0) };
        MidStem(m, bark, trunk, juvenile ? .047 : .085);
        int fronds = juvenile ? 5 : (detail == 2 ? 7 : 10 + rng.NextInt(3));
        var low = Primitives.Scale(c1, .76);
        for (int f = 0; f < fronds; f++)
        {
            double angle = 2 * Math.PI * f / fronds + rng.Range(-.15, .15);
            var radial = MidRadial(angle);
            var ortho = MidRadial(angle + Math.PI * .5);
            double reach = (juvenile ? .49 : rng.Range(.69, .98));
            var root = trunk[^1] + Vec3.Up * .012;
            int stations = 9;
            var path = new Vec3[stations];
            var radii = new double[stations];
            for (int s = 0; s < stations; s++)
            {
                double t = s / (double)(stations - 1);
                path[s] = root + radial * (reach * t) + ortho * (.045 * Math.Sin(t * Math.PI * 1.3))
                    + Vec3.Up * (.18 * Math.Sin(Math.PI * t) - .23 * t * t);
                radii[s] = .012 * (1 - .85 * t);
            }
            Primitives.Tube(m, path, radii, detail == 2 ? 3 : 5,
                (i, v) => (bark, 1, i, v, 0, 0));
            int pairs = detail == 2 ? 7 : 13;
            for (int p = 1; p <= pairs; p++)
            {
                double t = p / (double)(pairs + 1);
                double a = t * (stations - 1);
                int segment = Math.Min(stations - 2, (int)a);
                var node = path[segment] * (1 - (a - segment)) + path[segment + 1] * (a - segment);
                double extent = (.17 + .20 * Math.Sin(Math.PI * t)) * Math.Pow(1 - t, .25);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var end = node + ortho * (sign * extent) + radial * extent * .11
                        - Vec3.Up * (extent * rng.Range(.12, .31));
                    var control = (node + end) * .5 + Vec3.Up * .025;
                    MidBlade(m, node, end - node, (end - node).Length, extent * .21,
                        c1, low, Rng.Mix(seed, (ulong)(f * 1000 + p * 10 + sign + 3)), drop: .018);
                    if (detail == 0 && p % 2 == 0)
                    {
                        // Sparse secondary pinnae create the characteristic fern fractal edge.
                        var subRoot = node + (end - node) * .62;
                        var subDir = ((end - node).Normalized() * .35 + radial * (sign * .45)
                            + Vec3.Up * .1).Normalized();
                        MidBlade(m, subRoot, subDir, extent * .31, extent * .035,
                            c1, low, Rng.Mix(seed, (ulong)(f * 600 + p * 9 + sign + 50)), drop: .004);
                    }
                }
            }
        }
    }

    // Rhubarb/Darmera-like umbrella: individual petioles, overlapping nearly horizontal leaves.
    private static void MidRainshield(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        int detail = m.FloraDetailLevel ?? 0;
        int leaves = juvenile ? 7 : (detail == 2 ? 11 : 17 + rng.NextInt(5));
        var stalk = Primitives.Mix(c1, new[] { .29, .32, .23 }, .60);
        var under = Primitives.Mix(c2, new[] { .48, .63, .47 }, .45);
        for (int k = 0; k < leaves; k++)
        {
            double a = k * 2.399963 + rng.Range(-.29, .29);
            double r = Math.Sqrt((k + .5) / (double)leaves) * rng.Range(.29, .53);
            var outward = MidRadial(a);
            var foot = outward * (r * .65);
            double h = juvenile ? rng.Range(.35, .63) : rng.Range(.67, .99);
            var mid = foot + outward * (r * .40) + Vec3.Up * (h * .60);
            var crown = foot + outward * (r + .11) + Vec3.Up * h;
            MidStem(m, stalk, new[] { foot, mid, crown }, juvenile ? .008 : .013);
            var dir = (outward * .96 + Vec3.Up * rng.Range(-.10, .09)).Normalized();
            double len = (juvenile ? .23 : .31) * rng.Range(.81, 1.16);
            Broadleaf.BuildLeaf(m, crown, dir, len, len * rng.Range(.97, 1.20),
                BroadleafOutline.OrbicularCordate, Rng.Mix(seed, (ulong)(k * 439 + 13)),
                c1, c2, age: rng.Range(.71, .98), camber: .07, midribFold: .045,
                wavyMargin: .025, wavyFrequency: 3.2, tipCurl: .043,
                petioleLength: .07, petioleRadius: .006, petioleKink: .05,
                undersideColor: under, detailOverride: Math.Max(1, detail));
        }
    }

    // Fine-grained horizontal foliage shelves; dark spaces remain visible between tiers.
    private static void MidLacemantle(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        int detail = m.FloraDetailLevel ?? 0;
        var bark = Primitives.Mix(MidBark, new[] { .27, .22, .24 }, .28);
        int stems = juvenile ? 2 : 3 + rng.NextInt(2);
        for (int s = 0; s < stems; s++)
        {
            double phase = rng.Range(0, Math.PI * 2);
            var rootDir = MidRadial(phase);
            var root = rootDir * rng.Range(0, .10);
            double height = rng.Range(.83, .99);
            var mid = root + rootDir * .10 + Vec3.Up * (height * .51);
            var top = root + rootDir * .15 + Vec3.Up * height;
            MidStem(m, bark, new[] { root, mid, top }, juvenile ? .025 : .040);
            int tiers = juvenile ? 2 : (detail == 2 ? 3 : 4);
            for (int t = 0; t < tiers; t++)
            {
                double ht = .38 + t * (.55 / (tiers - 1));
                var junction = root * (1 - ht) + top * ht;
                int arms = detail == 2 ? 2 : 3;
                for (int a = 0; a < arms; a++)
                {
                    double az = phase + (a * 2 * Math.PI / arms) + (t % 2) * .7 + rng.Range(-.24, .24);
                    var outward = MidRadial(az);
                    double reach = rng.Range(.43, .79) * (1 - .22 * t / (double)tiers);
                    var bend = junction + outward * reach * .54 + Vec3.Up * .025;
                    var end = junction + outward * reach + Vec3.Up * rng.Range(-.07, .05);
                    MidStem(m, bark, new[] { junction, bend, end }, .009);
                    int twigs = detail == 2 ? 3 : 5;
                    for (int q = 0; q < twigs; q++)
                    {
                        var anchor = bend * (1 - q / (double)(twigs + 1)) + end * (q / (double)(twigs + 1));
                        double twigAz = az + rng.Range(-.95, .95);
                        var dir = MidRadial(twigAz);
                        var twigTip = anchor + dir * rng.Range(.12, .23) + Vec3.Up * rng.Range(-.06, .07);
                        MidStem(m, bark, new[] { anchor, twigTip }, .0036);
                        int leafCount = detail == 2 ? 3 : 5;
                        for (int l = 0; l < leafCount; l++)
                        {
                            var attach = anchor * (1 - l / (double)leafCount) + twigTip * (l / (double)leafCount);
                            var leafDir = (dir * .35 + MidRadial(twigAz + (l % 2 == 0 ? 1.10 : -1.10))
                                + Vec3.Up * rng.Range(-.08, .10)).Normalized();
                            double len = rng.Range(.085, .15);
                            MidBlade(m, attach, leafDir, len, len * .27, c1, c2,
                                Rng.Mix(seed, (ulong)(s * 100000 + t * 10000 + a * 1000 + q * 100 + l)));
                        }
                    }
                }
            }
        }
    }

    // Twisted dry-land stem skeleton with distinct terminal leather-leaf rosettes.
    private static void MidCragparasol(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        int detail = m.FloraDetailLevel ?? 0;
        var bark = new[] { .31, .27, .21 };
        double az = rng.Range(0, Math.PI * 2);
        var lean = MidRadial(az);
        var foot = Vec3.Zero;
        var elbow = lean * .13 + Vec3.Up * .27;
        var fork = lean * .24 + Vec3.Up * (juvenile ? .39 : .57);
        MidStem(m, bark, new[] { foot, elbow, fork }, .057);
        int crowns = juvenile ? 3 : 5 + rng.NextInt(3);
        for (int k = 0; k < crowns; k++)
        {
            double a = az + 2 * Math.PI * k / crowns + rng.Range(-.24, .24);
            var outward = MidRadial(a);
            double reach = rng.Range(.38, .70);
            var mid = fork + outward * (reach * .56) + Vec3.Up * rng.Range(.06, .18);
            var end = fork + outward * reach + Vec3.Up * rng.Range(.13, .32);
            end = new Vec3(end.X, Math.Min(.98, end.Y), end.Z);
            MidStem(m, bark, new[] { fork, mid, end }, .020);
            int rosettes = detail == 2 ? 2 : 3;
            for (int r = 0; r < rosettes; r++)
            {
                var anchor = r == 0 ? mid : end + outward * (r == 2 ? .06 : 0);
                int leaves = detail == 2 ? 5 : (juvenile ? 6 : 9);
                for (int l = 0; l < leaves; l++)
                {
                    double leafAngle = a + 2 * Math.PI * l / leaves + rng.Range(-.19, .19);
                    var direction = (MidRadial(leafAngle) + Vec3.Up * rng.Range(-.04, .19)).Normalized();
                    double len = rng.Range(.14, .24);
                    Broadleaf.BuildLeaf(m, anchor, direction, len, len * .24, BroadleafOutline.Lanceolate,
                        Rng.Mix(seed, (ulong)(k * 10101 + r * 117 + l * 3)),
                        c1, c2, age: rng.Range(.72, 1), camber: .08, midribFold: .10,
                        wavyMargin: .012, wavyFrequency: 3, tipCurl: .05,
                        petioleLength: .021, petioleRadius: .002, petioleKink: .09,
                        detailOverride: Math.Max(1, detail));
                }
            }
        }
    }
}
