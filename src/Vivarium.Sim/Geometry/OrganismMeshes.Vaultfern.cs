using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Vaultfern (microcanopy_vaultfern) high-fidelity procedural geometry.
///
/// Distinct ecological role:
/// A monumental prehistoric tree-fern / cycad microcanopy inspired by ancient Dicksonia antarctica
/// and Cyathea tree-ferns, forming an architectural ribbed vault of arching bipinnate fronds
/// above a shaggy, fibrous caudex armoured in persistent old stipe-stubs.
///
/// Visual Architecture:
///   1. Shaggy Fibrous Caudex with Flared Root Skirt: A stout, muscular caudex with a widely
///      flared dark-peat fibrous root skirt at ground level, clad in rusty-gold ramenta scales and
///      28-36 persistent broken diamond-shaped frond stipe-stubs spiraling up the trunk (UV2 = (2, 0)).
///   2. Uncurling Spiral Fiddlehead Croziers Filling Crown Apex Center: Rising majestically from the
///      central apical pith, a dense, sculptural heart of 6-8 uncurling logarithmic spiral fiddlehead croziers
///      and young crosier buds (UV2 = (1.25, 0.86)), shifting from bronze-furred bases to glowing lime-chartreuse
///      and amber-gold spiral heads, completely banishing the hollow center hole.
///   3. 3-Tier Concentric Frond Arcade: 22-26 arching fronds springing in three concentric tiers
///      (inner elevated crown framing the fiddleheads, middle sweeping vault, and outer cascading skirt)
///      to form a continuous, unbroken foliage arcade from center to rim. Pinnae carry raised mineral midribs,
///      +Up normals for lush canopy illumination, and warm golden-orange sori spore-clusters along lower pinnae.
/// </summary>
public static partial class OrganismMeshes
{
    // Shaggy caudex & ramenta palette (UV2 = (2, 0))
    private static readonly double[] VaultPeatDark     = { 0.12, 0.09, 0.07 };   // Shaded dark peat & fissures
    private static readonly double[] VaultFibrousBark  = { 0.24, 0.17, 0.13 };   // Fibrous caudex body
    private static readonly double[] VaultRamentaGold  = { 0.50, 0.32, 0.14 };   // Rusty-gold ramenta hair scales
    private static readonly double[] VaultStubPith     = { 0.44, 0.36, 0.26 };   // Exposed broken stipe-stub pith
    private static readonly double[] VaultCaudexMoss   = { 0.18, 0.28, 0.12 };   // Damp basal skirt moss

    // Frond & pinnae palette (UV2 = (0, 1))
    private static readonly double[] VaultPinnaEmerald = { 0.18, 0.44, 0.20 };   // Deep emerald core
    private static readonly double[] VaultPinnaChartreuse = { 0.38, 0.64, 0.22 };// Vibrant sunlit upper surface
    private static readonly double[] VaultPinnaSage    = { 0.44, 0.56, 0.38 };   // Pale mineral underside
    private static readonly double[] VaultMidrib       = { 0.58, 0.65, 0.44 };   // Pale reinforced mineral midrib
    private static readonly double[] VaultSoriGolden   = { 0.90, 0.58, 0.14 };   // Golden-orange spore sori (UV2 = 1.35, 0.88)

    // Whimsical Fiddlehead Crozier palette (UV2 = 1.25, 0.86)
    private static readonly double[] VaultSheath       = { 0.82, 0.76, 0.62 };   // Basal parchment spathe
    private static readonly double[] VaultCrozierBronze= { 0.44, 0.26, 0.14 };   // Bronze ramenta fur at coil base
    private static readonly double[] VaultCrozierLime  = { 0.58, 0.92, 0.24 };   // Luminous lime-chartreuse coil
    private static readonly double[] VaultCrozierAmber = { 0.98, 0.78, 0.18 };   // Glowing amber-gold coil apex

    /// <summary>
    /// Builds the shaggy fibrous caudex with flared root skirt and spiraling persistent stipe-stubs (UV2 = (2, 0)).
    /// </summary>
    private static (Vec3 Crown, double CrownRadius) VaultShaggyCaudex(
        MeshData m,
        Rng rng,
        double height,
        double rBase,
        int detail)
    {
        int stations = detail == 2 ? 10 : 16;
        int segments = detail == 2 ? 8 : 12;
        int start = m.VertexCount;

        double leanAz = rng.Range(0, Math.PI * 2);
        var leanDir = new Vec3(Math.Cos(leanAz), 0, Math.Sin(leanAz));

        var path = new Vec3[stations];
        var radii = new double[stations];

        for (int i = 0; i < stations; i++)
        {
            double v = i / (double)(stations - 1);
            double sway = Math.Sin(Math.PI * v) * rng.Range(0.006, 0.016);
            path[i] = leanDir * sway + Vec3.Up * (height * v);

            // Flared root skirt at base, tapering slightly to shoulder, rounded crown
            if (v < 0.25)
            {
                double flare = Math.Pow(1.0 - v / 0.25, 2.0);
                radii[i] = rBase * (0.95 + 0.55 * flare);
            }
            else if (v < 0.80)
            {
                radii[i] = rBase * (0.88 + 0.08 * Math.Sin((v - 0.25) / 0.55 * Math.PI));
            }
            else
            {
                radii[i] = rBase * (0.94 - 0.40 * Math.Pow((v - 0.80) / 0.20, 1.4));
            }
        }

        var dir0 = (path[1] - path[0]).Normalized();
        var side = dir0.Cross(Vec3.Up);
        if (side.LengthSq < 1e-8) side = dir0.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();

        for (int i = 0; i < stations; i++)
        {
            var dir = i == 0 ? (path[1] - path[0]).Normalized()
                    : i == stations - 1 ? (path[^1] - path[^2]).Normalized()
                    : (path[i + 1] - path[i - 1]).Normalized();
            side = (side - dir * side.Dot(dir)).Normalized();
            var up2 = side.Cross(dir).Normalized();

            double v = i / (double)(stations - 1);
            double stationR = radii[i];

            for (int s = 0; s <= segments; s++)
            {
                double u = s / (double)segments;
                double th = 2.0 * Math.PI * u;

                // Fibrous root ribs and longitudinal ramenta furrows
                double flute = Math.Cos(6.0 * th);
                double rMod = 1.0 + 0.10 * flute;
                if (v < 0.25)
                {
                    rMod += 0.12 * Math.Max(0.0, Math.Cos(4.0 * th)) * (1.0 - v / 0.25);
                }

                double r = stationR * rMod;
                var radial = (side * Math.Cos(th) + up2 * Math.Sin(th)).Normalized();
                var pos = path[i] + radial * r;
                var normal = radial;

                var col = Primitives.Mix(VaultFibrousBark, VaultPeatDark, Math.Clamp(-flute, 0.0, 1.0) * 0.6);
                if (v < 0.22)
                {
                    col = Primitives.Mix(col, VaultCaudexMoss, (1.0 - v / 0.22) * 0.55);
                }
                else if (v > 0.50)
                {
                    col = Primitives.Mix(col, VaultRamentaGold, (v - 0.50) * 0.45);
                }

                m.AddVertex(pos, normal, col, 1.0, height * v * 3.0, u, 2.0, 0.0);
            }
        }

        for (int i = 0; i < stations - 1; i++)
        {
            for (int s = 0; s < segments; s++)
            {
                int a = start + i * (segments + 1) + s;
                int b = a + segments + 1;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
        }

        // Domed crown cap
        double apexR = radii[^1];
        var apexCenter = path[^1] + Vec3.Up * (apexR * 0.50);
        int apexIdx = m.AddVertex(apexCenter, Vec3.Up, VaultRamentaGold, 1.0, height * 3.0, 0.5, 2.0, 0.0);
        int topRow = start + (stations - 1) * (segments + 1);
        for (int s = 0; s < segments; s++)
        {
            m.AddTriangle(topRow + s, topRow + s + 1, apexIdx);
        }

        m.Structural.Add((start, m.VertexCount - start, segments, rBase));

        // ---------------------------------------------------------------------
        // Persistent Old Frond Stipe-Stubs (28-36 angled diamond stubs spiraling up trunk)
        // ---------------------------------------------------------------------
        int numStubs = detail == 2 ? 14 : 32;
        double goldenAngle = 2.39996323; // ~137.5 degrees

        for (int st = 0; st < numStubs; st++)
        {
            double vStub = 0.30 + 0.62 * (st / (double)numStubs);
            double thStub = st * goldenAngle;

            int pathIdx = Math.Clamp((int)(vStub * (stations - 1)), 0, stations - 2);
            double frac = (vStub * (stations - 1)) - pathIdx;
            var center = Vec3.Lerp(path[pathIdx], path[pathIdx + 1], frac);
            double rad = MathD.Lerp(radii[pathIdx], radii[pathIdx + 1], frac);

            var stubOut = new Vec3(Math.Cos(thStub), 0, Math.Sin(thStub)).Normalized();
            var stubBasePos = center + stubOut * rad;

            var stubDir = (stubOut * 0.85 + Vec3.Up * 0.52).Normalized();
            var stubSide = stubDir.Cross(Vec3.Up).Normalized();
            var stubUp = stubSide.Cross(stubDir).Normalized();

            double stubLen = rng.Range(0.025, 0.045);
            double stubWid = rng.Range(0.014, 0.022);
            double stubH = stubWid * 0.70;

            var pEnd = stubBasePos + stubDir * stubLen;

            int stubStart = m.VertexCount;
            // 4 base vertices
            m.AddVertex(stubBasePos + stubSide * (stubWid * 0.5), stubSide, VaultRamentaGold, 1.0, 0, 0, 2.0, 0.0);
            m.AddVertex(stubBasePos + stubUp * (stubH * 0.5), stubUp, VaultRamentaGold, 1.0, 0, 0.25, 2.0, 0.0);
            m.AddVertex(stubBasePos - stubSide * (stubWid * 0.5), -stubSide, VaultRamentaGold, 1.0, 0, 0.50, 2.0, 0.0);
            m.AddVertex(stubBasePos - stubUp * (stubH * 0.5), -stubUp, VaultRamentaGold, 1.0, 0, 0.75, 2.0, 0.0);

            // 4 tip vertices (broken end)
            m.AddVertex(pEnd + stubSide * (stubWid * 0.35), stubSide, VaultStubPith, 1.0, 1, 0, 2.0, 0.0);
            m.AddVertex(pEnd + stubUp * (stubH * 0.35), stubUp, VaultStubPith, 1.0, 1, 0.25, 2.0, 0.0);
            m.AddVertex(pEnd - stubSide * (stubWid * 0.35), -stubSide, VaultStubPith, 1.0, 1, 0.50, 2.0, 0.0);
            m.AddVertex(pEnd - stubUp * (stubH * 0.35), -stubUp, VaultStubPith, 1.0, 1, 0.75, 2.0, 0.0);

            int tipIdx = m.AddVertex(pEnd, stubDir, VaultStubPith, 1.0, 1, 0.5, 2.0, 0.0);

            for (int k = 0; k < 4; k++)
            {
                int kNext = (k + 1) % 4;
                int a = stubStart + k;
                int b = stubStart + kNext;
                int c = stubStart + 4 + k;
                int d = stubStart + 4 + kNext;

                m.AddTriangle(a, d, c);
                m.AddTriangle(a, b, d);
                m.AddTriangle(c, d, tipIdx);
            }
        }

        return (apexCenter, apexR);
    }

    /// <summary>
    /// One pinna blade: entire, leathery cycad/tree-fern pinna with raised midrib,
    /// blended +Up normals, and golden-orange sori spore clusters along the lower surface.
    /// </summary>
    private static void VaultPinnaBlade(
        MeshData m,
        Vec3 basePoint,
        Vec3 along,
        Vec3 across,
        Vec3 dorsal,
        double length,
        double halfWidth,
        ulong seed,
        double[] c1,
        double[] c2,
        int rows,
        bool hasSori)
    {
        var rng = Rng.Keyed(seed, "vaultfern.pinna", 0);
        int leafVertex = m.VertexCount, leafIndex = m.Indices.Count;
        double sweep = rng.Range(0.06, 0.16);
        double curve = rng.Range(-0.05, 0.05);

        double[] sx = { -1.0, -0.45, 0.0, 0.45, 1.0 };
        double[] sy = { 0.10, 0.28, 0.46, 0.28, 0.10 };

        var ids = new int[rows + 1, 5];

        for (int i = 0; i <= rows; i++)
        {
            double t = i / (double)rows;
            double w = halfWidth * (0.65 + 0.35 * Math.Sin(Math.PI * Math.Min(1.0, 0.16 + 0.84 * t)))
                * Math.Max(0.05, 1.0 - Math.Pow(t, 2.2));

            var centre = basePoint + along * (length * t) + across * (length * sweep * t * t)
                + dorsal * (length * curve * Math.Sin(Math.PI * t));

            for (int k = 0; k < 5; k++)
            {
                var at = centre + across * (sx[k] * w) + dorsal * (sy[k] * w);
                double ridge = k == 2 ? 1.0 : 0.0;

                var col = Primitives.Mix(c1, c2, t);
                if (ridge > 0.5) col = Primitives.Mix(col, VaultMidrib, 0.65);

                // Blend normal heavily towards +Up for lush canopy illumination
                var norm = (dorsal * 0.35 + Vec3.Up * 0.65).Normalized();
                ids[i, k] = m.AddVertex(at, norm, col, 1.0, t, 0.5 + sx[k] * 0.5, 0.0, 1.0);
            }
        }

        for (int i = 0; i < rows; i++)
        {
            for (int k = 0; k < 4; k++)
            {
                int a0 = ids[i, k];
                int a1 = ids[i, k + 1];
                int b0 = ids[i + 1, k];
                int b1 = ids[i + 1, k + 1];

                m.AddTriangle(a0, b0, a1);
                m.AddTriangle(a1, b0, b1);
            }
        }

        // Golden-orange sori spore dots along lower surface
        if (hasSori && rows >= 4)
        {
            for (int s = 1; s <= 2; s++)
            {
                int rowIdx = (int)(rows * (0.30 + 0.35 * s));
                for (int sideSign = -1; sideSign <= 1; sideSign += 2)
                {
                    int kIdx = sideSign > 0 ? 3 : 1;
                    var soriPos = m.Position(ids[rowIdx, kIdx]) - dorsal * 0.002;
                    int sv = m.VertexCount;
                    double sR = halfWidth * 0.22;
                    m.AddVertex(soriPos, -dorsal, VaultSoriGolden, 1.0, 0, 0, 1.35, 0.88);
                    m.AddVertex(soriPos + across * (sideSign * sR), -dorsal, VaultSoriGolden, 1.0, 1, 0, 1.35, 0.88);
                    m.AddVertex(soriPos + along * sR, -dorsal, VaultSoriGolden, 1.0, 0, 1, 1.35, 0.88);
                    m.AddTriangle(sv, sv + 1, sv + 2);
                }
            }
        }

        m.RecordLeaf(leafVertex, leafIndex, basePoint, length);
    }

    /// <summary>
    /// One frond: an architectural rib of the arching vault.
    /// </summary>
    private static void VaultFrond(
        MeshData m,
        Rng rng,
        ulong seed,
        Vec3 crown,
        double az,
        double reach,
        double lift,
        int pairs,
        int detail,
        double[] c1,
        double[] c2)
    {
        var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
        var across = new Vec3(-Math.Sin(az), 0, Math.Cos(az));
        int stations = detail == 2 ? 6 : 9;
        var path = new Vec3[stations];
        var radii = new double[stations];

        for (int s = 0; s < stations; s++)
        {
            double t = s / (double)(stations - 1);
            path[s] = crown + radial * (reach * Math.Pow(t, 0.86))
                + Vec3.Up * (lift * Math.Sin(t * Math.PI * 0.66) - 0.26 * t * t);
            radii[s] = 0.0065 * (1.0 - 0.66 * t) + 0.0016;
        }

        Primitives.Tube(m, path, radii, detail == 2 ? 4 : 6,
            (i, v) => (Primitives.Mix(VaultPinnaEmerald, VaultPinnaChartreuse, 0.42), 1.0, i / (double)(stations - 1), v, 0.0, 1.0));

        // Spiny basal petiole
        int spines = detail == 2 ? 3 : 5;
        for (int k = 0; k < spines; k++)
        {
            double t = 0.04 + 0.14 * (k / (double)Math.Max(1, spines - 1));
            int a = Math.Min(stations - 2, (int)(t * stations));
            var node = Vec3.Lerp(path[a], path[a + 1], t * stations - a);
            double sign = (k % 2 == 0) ? 1.0 : -1.0;
            var tip = node + across * (sign * 0.015) - radial * 0.010 - Vec3.Up * 0.004;
            Primitives.Tube(m, new[] { node, tip }, new[] { 0.0030, 0.0005 }, 4,
                (i, v) => (VaultPinnaChartreuse, 1.0, i, v, 0.0, 0.0));
        }

        // Pairs of pinnae
        int pinnaRows = detail == 2 ? 3 : (detail == 1 ? 4 : 5);
        const double pinnaStart = 0.12;
        const double pinnaEnd = 0.99;

        for (int p = 1; p <= pairs; p++)
        {
            double t = pairs <= 1 ? pinnaEnd
                : pinnaStart + (pinnaEnd - pinnaStart) * (p - 1) / (pairs - 1);
            double a = t * (stations - 1);
            int seg = Math.Min(stations - 2, (int)a);
            var node = Vec3.Lerp(path[seg], path[seg + 1], a - seg);

            double len = (0.160 + 0.110 * Math.Sin(Math.PI * Math.Min(1.0, 0.22 + 0.78 * t)))
                * Math.Pow(1.0 - t, 0.10) * (detail == 2 ? 1.15 : 1.0);

            bool hasSori = (detail == 0) && (t > 0.30 && t < 0.75 && p % 2 == 0);

            for (int sign = -1; sign <= 1; sign += 2)
            {
                var dir = (across * sign + radial * rng.Range(0.30, 0.50) - Vec3.Up * rng.Range(0.02, 0.10)).Normalized();
                var pinnaAcross = dir.Cross(Vec3.Up);
                if (pinnaAcross.LengthSq < 1e-10) pinnaAcross = radial;
                pinnaAcross = pinnaAcross.Normalized();
                var pinnaUp = pinnaAcross.Cross(dir).Normalized();
                if (pinnaUp.Y < 0) { pinnaUp = -pinnaUp; pinnaAcross = -pinnaAcross; }

                double halfW = len * rng.Range(0.055, 0.070);
                VaultPinnaBlade(m, node, dir, pinnaAcross, pinnaUp, len, halfW,
                    Rng.Mix(seed, (ulong)(p * 733 + sign * 13 + 5)), c1, c2, pinnaRows, hasSori);
            }
        }

        // Terminal pinna
        var tipDir = (path[stations - 1] - path[stations - 2]).Normalized();
        var tipAcross = tipDir.Cross(Vec3.Up).Normalized();
        if (tipAcross.LengthSq < 1e-10) tipAcross = across;
        var tipUp = tipAcross.Cross(tipDir).Normalized();
        if (tipUp.Y < 0) { tipUp = -tipUp; tipAcross = -tipAcross; }

        double tipLen = 0.115 * (detail == 2 ? 1.15 : 1.0);
        var tipLeafDir = (tipDir + tipUp * rng.Range(0.20, 0.40)).Normalized();
        var tipLeafAcross = tipLeafDir.Cross(Vec3.Up).Normalized();
        if (tipLeafAcross.LengthSq < 1e-10) tipLeafAcross = tipAcross;
        var tipLeafUp = tipLeafAcross.Cross(tipLeafDir).Normalized();
        if (tipLeafUp.Y < 0) { tipLeafUp = -tipLeafUp; tipLeafAcross = -tipLeafAcross; }

        VaultPinnaBlade(m, path[stations - 1] - tipDir * (tipLen * 0.28), tipLeafDir, tipLeafAcross, tipLeafUp,
            tipLen, tipLen * rng.Range(0.052, 0.066),
            Rng.Mix(seed, (ulong)(pairs * 733 + 997)), c1, c2, pinnaRows, false);
    }

    /// <summary>
    /// Whimsical Signature: Luminous Spiraling Fiddlehead Crozier with 2.5-turn logarithmic coil
    /// shifting from bronze-furred base to glowing lime-chartreuse & amber-gold at the spiral head (UV2 = (1.25, 0.86)).
    /// Sculptural, substantive presence filling the central apical crown.
    /// </summary>
    private static void VaultFiddleheadCrozier(
        MeshData m,
        Vec3 basePoint,
        Rng rng,
        double height,
        double r0,
        double turns,
        double headAz,
        ulong seed,
        int detail)
    {
        int crozierStart = m.VertexCount;
        int steps = detail == 2 ? 14 : 26;

        var points = new Vec3[steps + 1];
        var radii = new double[steps + 1];

        var headFwd = new Vec3(Math.Cos(headAz), 0, Math.Sin(headAz));
        var headSide = headFwd.Cross(Vec3.Up).Normalized();

        for (int i = 0; i <= steps; i++)
        {
            double t = i / (double)steps;
            double a = t * turns * 2.0 * Math.PI;
            double r = r0 * Math.Exp(-1.75 * t);

            // Upward stipe ascending smoothly into logarithmic planar coil
            double y = height * (0.20 + 0.80 * Math.Pow(t, 0.70)) + Math.Sin(a) * r * 0.45;
            var pSpiral = basePoint + headFwd * (Math.Cos(a) * r) + headSide * (0.003 * Math.Sin(a)) + Vec3.Up * y;

            points[i] = pSpiral;
            radii[i] = Math.Max(0.0024, r * 0.28 + height * 0.015 * (1.0 - 0.70 * t));
        }

        // Tube with color gradient: bronze base -> lime-chartreuse -> glowing amber head (UV2 = 1.25, 0.86)
        Primitives.Tube(m, points, radii, detail == 2 ? 4 : 6, (i, v) =>
        {
            double t = i / (double)steps;
            var col = t < 0.35
                ? Primitives.Mix(VaultCrozierBronze, VaultCrozierLime, t / 0.35)
                : Primitives.Mix(VaultCrozierLime, VaultCrozierAmber, (t - 0.35) / 0.65);
            return (col, 1.0, t, v, 1.25, 0.86);
        });

        // Basal parchment spathe / furred bud sheath
        int rings = 3, sides = detail == 2 ? 6 : 8;
        int sStart = m.VertexCount;
        for (int r = 0; r <= rings; r++)
        {
            double phi = (Math.PI * 0.60) * r / rings;
            for (int s = 0; s <= sides; s++)
            {
                double th = 2.0 * Math.PI * s / sides;
                double rr = r0 * 0.90 * Math.Cos(phi);
                var p = basePoint + new Vec3(Math.Cos(th) * rr, Math.Sin(phi) * height * 0.35, Math.Sin(th) * rr);
                m.AddVertex(p, Vec3.Up, VaultSheath, 1.0, 0, 0.5, 1.25, 0.86);
            }
        }
        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s < sides; s++)
            {
                int a = sStart + r * (sides + 1) + s;
                int b = a + sides + 1;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
        }

        if (m.VertexCount > crozierStart)
        {
            m.EssentialSpans.Add((crozierStart, m.VertexCount - crozierStart));
        }
    }

    /// <summary>
    /// Dense central heart cluster of uncurling fiddlehead croziers and furred crosier buds
    /// completely filling the crown apex center so no hollow hole exists.
    /// </summary>
    private static void VaultCentralCrozierHeart(
        MeshData m,
        Vec3 crownCenter,
        double crownR,
        Rng rng,
        ulong seed,
        bool juvenile,
        int detail)
    {
        int croziers = juvenile ? 4 : (detail == 2 ? 4 : 7);

        for (int i = 0; i < croziers; i++)
        {
            // Golden-angle spiral distribution from the very center outward to crownR * 0.32
            double tFrac = (i + 0.5) / croziers;
            double rPos = Math.Sqrt(tFrac) * (crownR * 0.28);
            double azPos = i * 2.39996323 + rng.Range(-0.15, 0.15);

            var at = crownCenter + new Vec3(Math.Cos(azPos), 0.015, Math.Sin(azPos)) * rPos;

            // Varied heights and stages: tall majestic center coils vs arching flanking coils
            double crozierH;
            double coilR;
            if (i == 0 || i == 1)
            {
                // Tallest majestic central croziers rising above the crown
                crozierH = rng.Range(0.24, 0.32) * (juvenile ? 0.70 : 1.05);
                coilR = crozierH * 0.30;
            }
            else if (i < 4)
            {
                // Mid-height uncurling croziers
                crozierH = rng.Range(0.18, 0.24) * (juvenile ? 0.70 : 1.00);
                coilR = crozierH * 0.32;
            }
            else
            {
                // Younger tight crozier coils nestled around the base
                crozierH = rng.Range(0.12, 0.17) * (juvenile ? 0.70 : 0.95);
                coilR = crozierH * 0.34;
            }

            double turns = rng.Range(2.2, 2.6);
            double headAz = azPos + rng.Range(0.2, 0.8);
            ulong headSeed = Rng.Mix(seed, (ulong)(i * 733 + 77));

            VaultFiddleheadCrozier(m, at, rng, crozierH, coilR, turns, headAz, headSeed, detail);
        }
    }

    /// <summary>
    /// Vaultfern procedural generator.
    /// Builds a monumental prehistoric tree-fern / cycad microcanopy with shaggy fibrous caudex,
    /// persistent broken diamond stipe-stubs, sculptural uncurling spiral fiddlehead croziers
    /// completely filling the crown apex center, and a 3-tier concentric arching frond arcade
    /// with golden sori clusters.
    /// </summary>
    private static void Vaultfern(
        MeshData m,
        Rng rng,
        ulong seed,
        double[] c1,
        double[] c2,
        bool juvenile)
    {
        m.PreserveUpperCanopyOnLod = true;
        int detail = m.FloraDetailLevel ?? 0;

        var pinnaBase = Primitives.Mix(VaultPinnaEmerald, c1, 0.25);
        var pinnaTip = Primitives.Mix(VaultPinnaChartreuse, c2, 0.30);

        // -------------------------------------------------------------------------
        // 1. Shaggy Fibrous Caudex with Flared Root Skirt & Persistent Stipe-Stubs
        // -------------------------------------------------------------------------
        double height = juvenile ? 0.28 : rng.Range(0.42, 0.52);
        double rBase = juvenile ? 0.085 : rng.Range(0.125, 0.155);
        var (crown, crownR) = VaultShaggyCaudex(m, rng, height, rBase, detail);

        // -------------------------------------------------------------------------
        // 2. Uncurling Spiral Fiddlehead Croziers Filling Crown Apex Center (UV2 = (1.25, 0.86))
        // -------------------------------------------------------------------------
        VaultCentralCrozierHeart(m, crown, crownR, rng, seed, juvenile, detail);

        // -------------------------------------------------------------------------
        // 3. 3-Tier Concentric Frond Arcade (Continuous Foliage Vault from Center to Rim)
        // -------------------------------------------------------------------------
        // Tier 3: Inner Elevated Crown (emerges close to center, framing the croziers)
        // Tier 2: Middle Canopy Vault (sweeping horizontal gothic arcade)
        // Tier 1: Outer Cascading Skirt (sweeping wide and downward)
        int numTier3Fronds = juvenile ? 0 : (detail == 2 ? 4 : 8);
        int numTier2Fronds = juvenile ? 4 : (detail == 2 ? 6 : 10);
        int numTier1Fronds = juvenile ? 6 : (detail == 2 ? 8 : 12);

        int pairsTier1 = juvenile ? 14 : (detail == 2 ? 16 : 26);
        int pairsTier2 = juvenile ? 12 : (detail == 2 ? 14 : 22);
        int pairsTier3 = juvenile ? 10 : (detail == 2 ? 12 : 18);

        double baseAz = rng.Range(0, Math.PI * 2);

        // --- Tier 1: Outer Cascading Skirt ---
        for (int f = 0; f < numTier1Fronds; f++)
        {
            double az = baseAz + 2.0 * Math.PI * f / numTier1Fronds + rng.Range(-0.06, 0.06);
            double reach = juvenile ? rng.Range(0.50, 0.60) : rng.Range(0.85, 0.98);
            double lift = juvenile ? rng.Range(0.18, 0.25) : rng.Range(0.24, 0.32);

            var root = crown - Vec3.Up * (crownR * 0.22) + new Vec3(Math.Cos(az), 0, Math.Sin(az)) * (crownR * 0.88);
            ulong frondSeed = Rng.Mix(seed, (ulong)(f * 911 + 17));

            VaultFrond(m, rng, frondSeed, root, az, reach, lift, pairsTier1, detail, pinnaBase, pinnaTip);
        }

        // --- Tier 2: Middle Canopy Vault ---
        double azTier2 = baseAz + Math.PI / numTier1Fronds;
        for (int f = 0; f < numTier2Fronds; f++)
        {
            double az = azTier2 + 2.0 * Math.PI * f / numTier2Fronds + rng.Range(-0.06, 0.06);
            double reach = juvenile ? rng.Range(0.42, 0.52) : rng.Range(0.70, 0.82);
            double lift = juvenile ? rng.Range(0.25, 0.32) : rng.Range(0.38, 0.48);

            var root = crown + Vec3.Up * (crownR * 0.05) + new Vec3(Math.Cos(az), 0, Math.Sin(az)) * (crownR * 0.60);
            ulong frondSeed = Rng.Mix(seed, (ulong)(f * 911 + 555));

            VaultFrond(m, rng, frondSeed, root, az, reach, lift, pairsTier2, detail, pinnaBase, pinnaTip);
        }

        // --- Tier 3: Inner Elevated Crown (Framing the Fiddlehead Center) ---
        if (numTier3Fronds > 0)
        {
            double azTier3 = baseAz + 0.5 * Math.PI / numTier1Fronds;
            for (int f = 0; f < numTier3Fronds; f++)
            {
                double az = azTier3 + 2.0 * Math.PI * f / numTier3Fronds + rng.Range(-0.06, 0.06);
                double reach = rng.Range(0.52, 0.64);
                double lift = rng.Range(0.48, 0.58); // high lift framing the fiddleheads

                var root = crown + Vec3.Up * (crownR * 0.18) + new Vec3(Math.Cos(az), 0, Math.Sin(az)) * (crownR * 0.32);
                ulong frondSeed = Rng.Mix(seed, (ulong)(f * 911 + 888));

                VaultFrond(m, rng, frondSeed, root, az, reach, lift, pairsTier3, detail, pinnaBase, pinnaTip);
            }
        }
    }
}
