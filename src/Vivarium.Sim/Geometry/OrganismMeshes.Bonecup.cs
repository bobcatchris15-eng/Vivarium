using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Bonecup - the wood-decaying cup fungus of damp, shaded litter.
///
/// Analogue: Sarcoscypha, the scarlet elf cup: a decaying nurse wood knot on the forest floor,
/// draped in an organic mycelial felt subiculum and lush emerald moss cushions, from which a crowd of
/// alabaster goblets rise. Each cup is a volumetric chalice lined with velvety blood-vermilion hymenium
/// holding a still pool of obsidian-black fluid.
///
/// The fantasy layer is the material: the cups are sculpted from ivory alabaster, cool and faintly
/// translucent, with a fine crystalline fuzz standing off the outer wall. The vermilion hymenium is
/// the fiery heart of the fungus, and the fluid inside is an ink-black meniscus that never spills.
///
/// Authored in REAL METRES and converted with NewFloraAppendWorld.
/// </summary>
public static partial class OrganismMeshes
{
    // ---------------------------------------------------------------------------------------------
    // Palette
    // ---------------------------------------------------------------------------------------------

    /// <summary>Decaying nurse branch heartwood.</summary>
    private static readonly double[] BonecupWoodDark = { 0.16, 0.12, 0.09 };
    /// <summary>Weathered bark with rich damp brown tones.</summary>
    private static readonly double[] BonecupWoodBark = { 0.28, 0.20, 0.14 };
    /// <summary>Exposed wood grain in bark fissures.</summary>
    private static readonly double[] BonecupWoodGrain = { 0.38, 0.30, 0.22 };

    /// <summary>Deep shaded moss turf in crevices.</summary>
    private static readonly double[] BonecupMossDark = { 0.08, 0.24, 0.10 };
    /// <summary>Velvety emerald moss cushions nestled along bark crevices.</summary>
    private static readonly double[] BonecupMossMid = { 0.16, 0.46, 0.18 };
    /// <summary>Sunlit chartreuse moss frond tips.</summary>
    private static readonly double[] BonecupMossTip = { 0.28, 0.62, 0.24 };

    /// <summary>The spreading mycelial subiculum felt: buff-ivory with faint ochre stains.</summary>
    private static readonly double[] BonecupSubiculum = { 0.86, 0.82, 0.74 };
    /// <summary>Shaded underside of the mycelial felt.</summary>
    private static readonly double[] BonecupSubiculumShade = { 0.58, 0.50, 0.42 };

    /// <summary>The cup's outer wall: cool, pristine alabaster bone.</summary>
    private static readonly double[] BonecupAlabaster = { 0.93, 0.90, 0.85 };
    /// <summary>The thin, translucent rolled lip of the cup.</summary>
    private static readonly double[] BonecupAlabasterLip = { 0.98, 0.96, 0.92 };

    /// <summary>The hymenium lining in deep shadow: oxblood/ruby vermilion.</summary>
    private static readonly double[] BonecupHymeniumDeep = { 0.45, 0.04, 0.03 };
    /// <summary>The velvety vermilion hymenium body.</summary>
    private static readonly double[] BonecupHymeniumMid = { 0.75, 0.10, 0.06 };
    /// <summary>The hymenium where light catches it: bright glowing scarlet.</summary>
    private static readonly double[] BonecupHymeniumLip = { 0.88, 0.18, 0.08 };

    /// <summary>The motionless black fluid pool.</summary>
    private static readonly double[] BonecupFluidBlack = { 0.010, 0.012, 0.016 };
    /// <summary>The fluid meniscus sheen climbing the inner hymenium wall.</summary>
    private static readonly double[] BonecupFluidSheen = { 0.20, 0.24, 0.32 };

    /// <summary>Fine mycelial rhizomorph hyphae.</summary>
    private static readonly double[] BonecupHyphae = { 0.78, 0.72, 0.62 };

    // ---------------------------------------------------------------------------------------------
    // Substrate & Ecology: Nurse Wood, Subiculum Felt, Moss & Hyphae
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds a solid, weathered decaying nurse branch fragment that sits naturally half-buried in the soil.
    /// The ends taper down and terminate under the ground plane to form a seamless, watertight solid manifold.
    /// </summary>
    private static void BonecupNurseWood(MeshData m, Vec3 origin, double length, double woodRadius, Rng rng)
    {
        const int stations = 18;
        const int sides = 16;
        var path = new Vec3[stations + 1];
        var radii = new double[stations + 1];

        // S-curved centerline lying half-submerged in the ground plane (y = 0)
        double curveAngle = rng.Range(-0.20, 0.20);
        var dir = new Vec3(Math.Cos(curveAngle), 0, Math.Sin(curveAngle)).Normalized();
        var perp = new Vec3(-dir.Z, 0, dir.X).Normalized();

        for (int i = 0; i <= stations; i++)
        {
            double t = i / (double)stations;
            double u = t * 2.0 - 1.0; // -1 to +1

            // S-curve lateral displacement
            double sCurve = Math.Sin(Math.PI * t) * 0.010 - Math.Sin(Math.PI * 2.0 * t) * 0.004;
            // Centerline height: sunken into ground at ends (below y=0), crowned in center
            double yCenter = woodRadius * (0.28 - 0.36 * Math.Abs(u));
            path[i] = origin + dir * (length * 0.5 * u) + perp * sCurve + Vec3.Up * yCenter;

            // Radius tapers down to zero at both ends for a watertight closed cap
            double capTaper = Math.Sin(Math.PI * t);
            double knotSwell = 1.0 + 0.14 * Math.Sin(t * Math.PI * 3.5);
            radii[i] = woodRadius * Math.Pow(capTaper, 0.40) * knotSwell;
        }

        // Build lofted solid branch with bark fissures
        var grid = new int[stations + 1, sides + 1];
        var normals = new Vec3[stations + 1, sides + 1];

        for (int i = 0; i <= stations; i++)
        {
            var p = path[i];
            double r = radii[i];
            bool isEndPole = (i == 0 || i == stations);

            for (int j = 0; j <= sides; j++)
            {
                double th = 2.0 * Math.PI * j / sides;
                // Longitudinal bark ridges and fissures
                double barkBumpy = 1.0 + 0.10 * Math.Sin(j * 4.0 + i * 0.4) + 0.05 * Math.Cos(j * 8.0);
                double radialR = isEndPole ? 0.0 : r * barkBumpy;

                // Flatten bottom where branch sits in soil
                double yMult = Math.Sin(th) < 0 ? 0.70 : 1.0;
                var offset = (perp * Math.Cos(th) + Vec3.Up * (Math.Sin(th) * yMult)) * radialR;
                var pt = p + offset;

                // Color: weathered bark with fissures showing lighter wood grain
                double stripe = 0.5 + 0.5 * Math.Sin(j * 4.0);
                var col = Primitives.Mix(BonecupWoodDark, BonecupWoodBark, stripe);
                col = Primitives.Mix(col, BonecupWoodGrain, 0.22 * Math.Max(0.0, Math.Sin(th)));

                grid[i, j] = m.AddVertex(pt, Vec3.Up, col, 1, i / (double)stations, (double)j / sides, 2.0, 0.0);
            }
        }

        // Generate branch faces
        for (int i = 0; i < stations; i++)
        {
            for (int j = 0; j < sides; j++)
            {
                int a = grid[i, j], b = grid[i, j + 1], c = grid[i + 1, j], d = grid[i + 1, j + 1];
                var pa = m.Position(a);
                var fn = (m.Position(b) - pa).Cross(m.Position(c) - pa);
                if (fn.LengthSq < 1e-18) fn = (m.Position(d) - pa).Cross(m.Position(c) - pa);
                if (fn.LengthSq < 1e-18) continue;
                fn = fn.Normalized();

                normals[i, j] += fn;
                normals[i, j + 1] += fn;
                normals[i + 1, j] += fn;
                normals[i + 1, j + 1] += fn;

                if (i == 0)
                {
                    Primitives.TriangleFacing(m, a, b, d, fn);
                }
                else if (i == stations - 1)
                {
                    Primitives.TriangleFacing(m, a, b, c, fn);
                }
                else
                {
                    Primitives.TriangleFacing(m, a, b, c, fn);
                    Primitives.TriangleFacing(m, b, d, c, fn);
                }
            }
        }

        // Apply smoothed normals
        for (int i = 0; i <= stations; i++)
            for (int j = 0; j <= sides; j++)
            {
                var n = normals[i, j];
                if (n.LengthSq < 1e-18) n = Vec3.Up;
                n = n.Normalized();
                int v = grid[i, j];
                m.Normals[v * 3] = (float)n.X;
                m.Normals[v * 3 + 1] = (float)n.Y;
                m.Normals[v * 3 + 2] = (float)n.Z;
            }
    }

    /// <summary>
    /// Builds the living mycelial subiculum cushion: an organic, raised stroma pad swelling
    /// atop the nurse wood right beneath the goblet colony, with softly rounded lobed margins.
    /// </summary>
    private static void BonecupSubiculumCushion(MeshData m, Vec3 origin, double length, double woodRadius, Rng rng)
    {
        const int rings = 10;
        const int sectors = 32;
        var grid = new int[rings + 1, sectors + 1];
        var normals = new Vec3[rings + 1, sectors + 1];

        double padLength = length * 0.62;
        double padWidth = woodRadius * 1.35;
        double padHeight = woodRadius * 0.28;

        for (int i = 0; i <= rings; i++)
        {
            double u = i / (double)rings; // 0 (center) to 1 (margin)
            for (int j = 0; j <= sectors; j++)
            {
                double th = 2.0 * Math.PI * j / sectors;
                // Multi-harmonic scalloped lobes
                double lobe = 1.0 + 0.14 * Math.Cos(3.0 * th + 0.5) + 0.08 * Math.Sin(5.0 * th);
                double rx = (padLength * 0.5) * Math.Cos(th) * u * lobe;
                double rz = (padWidth * 0.5) * Math.Sin(th) * u * lobe;

                // Smooth organic dome elevation above the wood surface
                double falloff = 1.0 - Math.Pow(u, 2.2);
                double h = padHeight * falloff;
                h += 0.0015 * Math.Sin(u * 12.0 + th * 2.0) * falloff;

                // Center sits securely atop the wood crown
                var p = origin + Vec3.Up * (woodRadius * 0.90 + h) + new Vec3(rx, 0, rz);

                // Creamy ivory felt, shading darker where it meets the wood margin
                var col = Primitives.Mix(BonecupSubiculumShade, BonecupSubiculum, MathD.SmoothStep(0.15, 0.85, 1.0 - u));
                col = Primitives.Mix(col, BonecupAlabaster, 0.18 * Math.Sin(u * 8.0 + th * 3.0));

                grid[i, j] = m.AddVertex(p, Vec3.Up, col, 1, u, (double)j / sectors, 1.35, 1.0);
            }
        }

        // Quads for subiculum pad
        for (int i = 0; i < rings; i++)
        {
            for (int j = 0; j < sectors; j++)
            {
                int a = grid[i, j], b = grid[i, j + 1], c = grid[i + 1, j], d = grid[i + 1, j + 1];
                var pa = m.Position(a);
                var fn = (m.Position(b) - pa).Cross(m.Position(c) - pa);
                if (fn.LengthSq < 1e-18) fn = (m.Position(d) - pa).Cross(m.Position(c) - pa);
                if (fn.LengthSq < 1e-18) continue;
                fn = fn.Normalized();

                normals[i, j] += fn;
                normals[i, j + 1] += fn;
                normals[i + 1, j] += fn;
                normals[i + 1, j + 1] += fn;

                Primitives.TriangleFacing(m, a, b, c, fn);
                Primitives.TriangleFacing(m, b, d, c, fn);
            }
        }

        // Apply smoothed normals
        for (int i = 0; i <= rings; i++)
            for (int j = 0; j <= sectors; j++)
            {
                var n = normals[i, j];
                if (n.LengthSq < 1e-18) n = Vec3.Up;
                n = n.Normalized();
                int v = grid[i, j];
                m.Normals[v * 3] = (float)n.X;
                m.Normals[v * 3 + 1] = (float)n.Y;
                m.Normals[v * 3 + 2] = (float)n.Z;
            }
    }

    /// <summary>
    /// Builds lush, velvety emerald moss cushions (Dicranum / pincushion moss) with soft micro-tufted
    /// surface domes and radiating leafy fronds nestled along the wood flanks and ground.
    /// </summary>
    private static void BonecupMossTurf(MeshData m, Vec3 origin, double length, double woodRadius, Rng rng)
    {
        const int clumpCount = 9;
        for (int k = 0; k < clumpCount; k++)
        {
            double a = 2.0 * Math.PI * k / clumpCount + rng.Range(-0.25, 0.25);
            double dist = rng.Range(woodRadius * 0.85, woodRadius * 1.55);
            double mx = Math.Cos(a) * (length * 0.44);
            double mz = Math.Sin(a) * dist;
            double my = Math.Max(0.002, woodRadius * 0.45 * Math.Max(0.0, 1.0 - Math.Abs(Math.Sin(a))));
            var clumpCenter = origin + new Vec3(mx, my, mz);

            double mRad = rng.Range(0.015, 0.025);
            double mH = rng.Range(0.009, 0.016);

            // 1. Organic pillowed moss dome
            const int domeRings = 6;
            const int domeSectors = 16;
            var domeGrid = new int[domeRings + 1, domeSectors + 1];

            for (int r = 0; r <= domeRings; r++)
            {
                double u = r / (double)domeRings; // 0 (peak) to 1 (base)
                double yFrac = Math.Cos(Math.PI * 0.5 * u);
                double rFrac = Math.Sin(Math.PI * 0.5 * u);

                for (int s = 0; s <= domeSectors; s++)
                {
                    double th = 2.0 * Math.PI * s / domeSectors;
                    double bump = 1.0 + 0.12 * Math.Sin(3.0 * th + r * 2.0) + 0.06 * Math.Cos(5.0 * th);
                    var p = clumpCenter + new Vec3(
                        Math.Cos(th) * mRad * rFrac * bump,
                        mH * yFrac,
                        Math.Sin(th) * mRad * rFrac * bump);

                    var col = Primitives.Mix(BonecupMossDark, BonecupMossMid, yFrac);
                    col = Primitives.Mix(col, BonecupMossTip, Math.Pow(yFrac, 2.0) * 0.55);

                    domeGrid[r, s] = m.AddVertex(p, Vec3.Up, col, 1, u, (double)s / domeSectors, 1.35, 1.0);
                }
            }

            for (int r = 0; r < domeRings; r++)
            {
                for (int s = 0; s < domeSectors; s++)
                {
                    int a_idx = domeGrid[r, s], b_idx = domeGrid[r, s + 1], c_idx = domeGrid[r + 1, s], d_idx = domeGrid[r + 1, s + 1];
                    var pa = m.Position(a_idx);
                    var fn = (m.Position(b_idx) - pa).Cross(m.Position(c_idx) - pa);
                    if (fn.LengthSq < 1e-18) fn = Vec3.Up;
                    fn = fn.Normalized();
                    Primitives.TriangleFacing(m, a_idx, b_idx, c_idx, fn);
                    Primitives.TriangleFacing(m, b_idx, d_idx, c_idx, fn);
                }
            }

            // 2. Micro-tufted frond starlets radiating around the base of the cushion
            const int fronds = 8;
            for (int f = 0; f < fronds; f++)
            {
                double fth = 2.0 * Math.PI * f / fronds + rng.Range(-0.2, 0.2);
                var fDir = new Vec3(Math.Cos(fth), 0.25, Math.Sin(fth)).Normalized();
                var fBase = clumpCenter + new Vec3(Math.Cos(fth), 0, Math.Sin(fth)) * (mRad * 0.85);
                double fLen = rng.Range(0.004, 0.007);
                var fTip = fBase + fDir * fLen;
                double fWid = rng.Range(0.0018, 0.0028);

                Primitives.CurvedLeaf(m, fBase, fTip, Vec3.Up, fWid * 0.5,
                    BonecupMossMid, BonecupMossTip, camber: 0.02, longitudinal: 3);
            }
        }
    }

    /// <summary>
    /// Builds branching rhizomorphic mycelial cords snaking across the wood bark and soil.
    /// </summary>
    private static void BonecupRhizomorphs(MeshData m, Vec3 origin, double radius, Rng rng)
    {
        const int cordCount = 14;
        for (int k = 0; k < cordCount; k++)
        {
            double a = 2.0 * Math.PI * k / cordCount + rng.Range(-0.2, 0.2);
            var cordDir = new Vec3(Math.Cos(a), 0, Math.Sin(a)).Normalized();
            var cordRoot = origin + cordDir * (radius * 0.42) + Vec3.Up * 0.005;
            double cordLen = rng.Range(0.035, 0.070);

            var p0 = cordRoot;
            var p1 = cordRoot + cordDir * (cordLen * 0.5) + new Vec3(rng.Range(-0.005, 0.005), 0.002, rng.Range(-0.005, 0.005));
            var p2 = cordRoot + cordDir * cordLen + new Vec3(rng.Range(-0.008, 0.008), -0.003, rng.Range(-0.008, 0.008));

            Primitives.Tube(m, new[] { p0, p1, p2 }, new[] { 0.0009, 0.0005, 0.00015 }, 4,
                (i, v) => (BonecupHyphae, 1, i * 0.5, v, 1.35, 0.0));

            // Secondary branchlet
            if (rng.NextDouble() < 0.65)
            {
                var bPerp = new Vec3(-cordDir.Z, 0, cordDir.X) * (rng.NextDouble() < 0.5 ? 1.0 : -1.0);
                var bTip = p1 + (cordDir * 0.7 + bPerp * 0.7).Normalized() * (cordLen * 0.45);
                Primitives.Tube(m, new[] { p1, bTip }, new[] { 0.00045, 0.00010 }, 3,
                    (i, v) => (BonecupHyphae, 1, i, v, 1.35, 0.0));
            }
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The Alabaster Chalices: Goblets, Hymenium & Black Fluid
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Builds one high-fidelity alabaster elf-cup goblet:
    /// - Continuous cubic Bézier stipe fluted longitudinally.
    /// - Outer bowl: Ivory alabaster bone flaring out into an undulating wavy rim.
    /// - Rolled rim: Thick, smooth semicircular rollover from outer wall to inner wall.
    /// - Inner cavity: Strictly interior concave bowl lined with velvety blood-vermilion hymenium.
    /// - Guaranteed wall thickness: Rin(y) = max(0, Rout(y) - T(y)) eliminates any possibility of self-intersection.
    /// - Mirror-black motionless fluid pool with high-specular meniscus wetting the red wall.
    /// - Crystalline outer tomentum micro-hairs catching rim light.
    /// </summary>
    private static void BonecupGoblet(MeshData m, Vec3 basePoint, Vec3 axis, double rimRadius, double cupDepth,
        double stipeLength, double stipeRadius, double fluidFraction, int lobes, double lobeAmp, Rng rng)
    {
        int leafVertex = m.VertexCount;
        int leafIndex = m.Indices.Count;

        const int stipeStations = 6;
        const int bowlStations = 10;
        const int rimRollSteps = 4;
        const int sectors = 36;

        var up = axis.Normalized();
        var right = up.Cross(Vec3.Forward);
        if (right.LengthSq < 1e-10) right = up.Cross(Vec3.Right);
        right = right.Normalized();
        var fwd = right.Cross(up).Normalized();

        double wallThickRim = rimRadius * 0.16; // 16% rim thickness
        double wallThickBase = stipeRadius * 0.85; // Solid floor thickness at bottom of cup
        double rimTopY = stipeLength + cupDepth;
        double cavityFloorY = stipeLength + wallThickBase * 0.65;

        // 1. Stipe: continuous cubic Bézier stalk that arches organically
        var stipeP0 = basePoint;
        var stipeC0 = basePoint + Vec3.Up * (stipeLength * 0.40);
        var stipeC1 = basePoint + up * (stipeLength * 0.70) + new Vec3(rng.Range(-0.002, 0.002), 0, rng.Range(-0.002, 0.002));
        var stipeP1 = basePoint + up * stipeLength;

        var stipePoints = new Vec3[stipeStations + 1];
        var stipeRadii = new double[stipeStations + 1];
        for (int i = 0; i <= stipeStations; i++)
        {
            double t = i / (double)stipeStations;
            double u = 1.0 - t;
            stipePoints[i] = stipeP0 * (u * u * u) + stipeC0 * (3.0 * u * u * t) + stipeC1 * (3.0 * u * t * t) + stipeP1 * (t * t * t);
            // Flared foot where it roots into the subiculum, tapering middle, widening neck
            double flare = 1.0 + 0.40 * Math.Pow(1.0 - t, 2.0) + 0.30 * Math.Pow(t, 2.0);
            stipeRadii[i] = stipeRadius * flare;
        }

        // Generate stipe tube with longitudinal micro-fluting
        var stipeGrid = new int[stipeStations + 1, sectors + 1];
        var stipeNormals = new Vec3[stipeStations + 1, sectors + 1];

        for (int i = 0; i <= stipeStations; i++)
        {
            double t = i / (double)stipeStations;
            var pt = stipePoints[i];
            double r = stipeRadii[i];

            for (int j = 0; j <= sectors; j++)
            {
                double th = 2.0 * Math.PI * j / sectors;
                double fluting = 1.0 + 0.07 * Math.Sin(j * 4.0);
                var offset = (right * Math.Cos(th) + fwd * Math.Sin(th)) * (r * fluting);
                var p = pt + offset;

                var col = Primitives.Mix(BonecupSubiculum, BonecupAlabaster, Math.Pow(t, 0.75));
                stipeGrid[i, j] = m.AddVertex(p, offset.Normalized(), col, 1, t, (double)j / sectors, 1.30, 0.0);
            }
        }

        for (int i = 0; i < stipeStations; i++)
        {
            for (int j = 0; j < sectors; j++)
            {
                int a = stipeGrid[i, j], b = stipeGrid[i, j + 1], c = stipeGrid[i + 1, j], d = stipeGrid[i + 1, j + 1];
                var pa = m.Position(a);
                var fn = (m.Position(b) - pa).Cross(m.Position(c) - pa);
                if (fn.LengthSq < 1e-18) fn = (m.Position(d) - pa).Cross(m.Position(c) - pa);
                if (fn.LengthSq < 1e-18) continue;
                fn = fn.Normalized();

                stipeNormals[i, j] += fn;
                stipeNormals[i, j + 1] += fn;
                stipeNormals[i + 1, j] += fn;
                stipeNormals[i + 1, j + 1] += fn;

                Primitives.TriangleFacing(m, a, b, c, fn);
                Primitives.TriangleFacing(m, b, d, c, fn);
            }
        }

        for (int i = 0; i <= stipeStations; i++)
            for (int j = 0; j <= sectors; j++)
            {
                var n = stipeNormals[i, j];
                if (n.LengthSq < 1e-18) n = up;
                n = n.Normalized();
                int v = stipeGrid[i, j];
                m.Normals[v * 3] = (float)n.X;
                m.Normals[v * 3 + 1] = (float)n.Y;
                m.Normals[v * 3 + 2] = (float)n.Z;
            }

        // 2. The Chalice Bowl: Outer Excipulum Wall, Rolled Lip, and Inner Hymenium Cavity
        int totalProfileSteps = bowlStations + 1 + rimRollSteps + bowlStations + 1;
        var profR = new double[totalProfileSteps];
        var profY = new double[totalProfileSteps];
        var profCol = new double[totalProfileSteps][];
        var profIsInner = new bool[totalProfileSteps];

        double rNeck = stipeRadii[stipeStations];

        // Analytic bowl outer radius function
        double OuterRadiusAtY(double y)
        {
            double t = Math.Clamp((y - stipeLength) / cupDepth, 0.0, 1.0);
            double curve = (1.0 - Math.Cos(Math.PI * 0.5 * t)) * 0.50 + Math.Pow(t, 2.0) * 0.50;
            return rNeck + (rimRadius - rNeck) * curve;
        }

        // Analytic wall thickness function
        double WallThickAtY(double y)
        {
            double t = Math.Clamp((y - cavityFloorY) / Math.Max(1e-6, rimTopY - cavityFloorY), 0.0, 1.0);
            return wallThickBase + (wallThickRim - wallThickBase) * Math.Pow(t, 0.85);
        }

        int pIdx = 0;

        // A) Outer Excipulum Wall: from stipe neck (y = stipeLength) to rim crest (y = rimTopY)
        for (int s = 0; s <= bowlStations; s++)
        {
            double t = s / (double)bowlStations;
            double y = stipeLength + cupDepth * t;
            double r = OuterRadiusAtY(y);

            profY[pIdx] = y;
            profR[pIdx] = r;
            profCol[pIdx] = Primitives.Mix(BonecupAlabaster, BonecupAlabasterLip, Math.Pow(t, 1.5));
            profIsInner[pIdx] = false;
            pIdx++;
        }

        // B) Rolled Rim Torus: 180 degree smooth semicircular roll from outer crest to inner crest
        for (int s = 1; s <= rimRollSteps; s++)
        {
            double alpha = Math.PI * s / (double)rimRollSteps; // pi/4, pi/2, 3pi/4, pi
            double rollR = (wallThickRim * 0.5) * (1.0 - Math.Cos(alpha));
            double rollH = (wallThickRim * 0.5) * Math.Sin(alpha);
            double y = rimTopY + rollH;
            double r = rimRadius - rollR;

            profY[pIdx] = y;
            profR[pIdx] = r;
            double blend = (double)s / rimRollSteps;
            profCol[pIdx] = Primitives.Mix(BonecupAlabasterLip, BonecupHymeniumLip, blend);
            profIsInner[pIdx] = (s >= rimRollSteps / 2);
            pIdx++;
        }

        // C) Inner Hymenium Wall: from inner rim (y = rimTopY) down to cavity floor (y = cavityFloorY)
        for (int s = bowlStations; s >= 0; s--)
        {
            double t = s / (double)bowlStations;
            double y = cavityFloorY + (rimTopY - cavityFloorY) * t;

            // Guaranteed strictly inside outer wall:
            double rOut = OuterRadiusAtY(y);
            double thick = WallThickAtY(y);
            double rIn = Math.Max(0.0, rOut - thick);

            profY[pIdx] = y;
            profR[pIdx] = rIn;
            // Glowing scarlet near rim, shading to velvety deep vermilion/oxblood at cavity floor
            profCol[pIdx] = Primitives.Mix(BonecupHymeniumDeep, BonecupHymeniumLip, Math.Pow(t, 0.70));
            profIsInner[pIdx] = true;
            pIdx++;
        }

        int actualProfileSteps = pIdx;

        // Build vertex grid with organic undulating ruffled lip
        var cupGrid = new int[actualProfileSteps, sectors + 1];
        var cupNormals = new Vec3[actualProfileSteps, sectors + 1];
        double wavePhase = rng.Range(0, 2.0 * Math.PI);

        Vec3 LathePoint(int i, double th)
        {
            double r = profR[i];
            double y = profY[i];

            // Ruffled multi-harmonic wave modulation near rim
            double heightWeight = Math.Clamp((profY[i] - cavityFloorY) / Math.Max(1e-6, rimTopY - cavityFloorY), 0.0, 1.0);
            if (heightWeight > 0.40)
            {
                double waveWeight = Math.Pow((heightWeight - 0.40) / 0.60, 1.35);
                double wave = (Math.Sin(lobes * th + wavePhase) * 0.75 + Math.Sin((lobes + 2) * th + 1.2) * 0.25) * (lobeAmp * waveWeight);
                y += wave * 0.65;
                r += wave * 0.45;
            }

            return basePoint + up * y + (right * Math.Cos(th) + fwd * Math.Sin(th)) * Math.Max(0.0, r);
        }

        for (int i = 0; i < actualProfileSteps; i++)
        {
            for (int j = 0; j <= sectors; j++)
            {
                double th = 2.0 * Math.PI * j / sectors;
                var p = LathePoint(i, th);
                var col = profCol[i];

                // Hymenium carries UV2.x = 1.35, UV2.y = 1.0 to preserve pure saturated pigment
                float uv2x = profIsInner[i] ? 1.35f : 1.30f;
                cupGrid[i, j] = m.AddVertex(p, up, col, 1, i / (double)actualProfileSteps, (double)j / sectors, uv2x, 1.0);
            }
        }

        // Generate faces with proper inward/outward winding
        for (int i = 0; i < actualProfileSteps - 1; i++)
        {
            bool stepToCenter = (profR[i + 1] <= 1e-7);
            for (int j = 0; j < sectors; j++)
            {
                int a = cupGrid[i, j], b = cupGrid[i, j + 1], c = cupGrid[i + 1, j], d = cupGrid[i + 1, j + 1];
                var pa = m.Position(a);
                var fn = profIsInner[i]
                    ? (m.Position(c) - pa).Cross(m.Position(b) - pa)  // invert winding for inner cavity
                    : (m.Position(b) - pa).Cross(m.Position(c) - pa);

                if (fn.LengthSq < 1e-18) fn = (m.Position(d) - pa).Cross(m.Position(c) - pa);
                if (fn.LengthSq < 1e-18) continue;
                fn = fn.Normalized();

                cupNormals[i, j] += fn;
                cupNormals[i, j + 1] += fn;
                cupNormals[i + 1, j] += fn;
                cupNormals[i + 1, j + 1] += fn;

                if (stepToCenter)
                {
                    if (profIsInner[i])
                        Primitives.TriangleFacing(m, a, c, b, fn);
                    else
                        Primitives.TriangleFacing(m, a, b, c, fn);
                }
                else
                {
                    if (profIsInner[i])
                    {
                        Primitives.TriangleFacing(m, a, c, b, fn);
                        Primitives.TriangleFacing(m, b, c, d, fn);
                    }
                    else
                    {
                        Primitives.TriangleFacing(m, a, b, c, fn);
                        Primitives.TriangleFacing(m, b, d, c, fn);
                    }
                }
            }
        }

        // Apply smoothed normals
        for (int i = 0; i < actualProfileSteps; i++)
            for (int j = 0; j <= sectors; j++)
            {
                var n = cupNormals[i, j];
                if (n.LengthSq < 1e-18) n = profIsInner[i] ? -up : up;
                n = n.Normalized();
                int v = cupGrid[i, j];
                m.Normals[v * 3] = (float)n.X;
                m.Normals[v * 3 + 1] = (float)n.Y;
                m.Normals[v * 3 + 2] = (float)n.Z;
            }

        // 3. Motionless Obsidian Fluid Pool
        if (fluidFraction > 0.15)
        {
            double fY = cavityFloorY + (rimTopY - cavityFloorY) * fluidFraction;
            double baseFR = Math.Max(0.002, OuterRadiusAtY(fY) - WallThickAtY(fY));

            // Fluid sits horizontally inside the cavity
            var fluidCenter = basePoint + up * fY;
            double meniscusH = baseFR * 0.04;

            int fluidFirst = m.Indices.Count;
            int fMid = m.AddVertex(fluidCenter, up, BonecupFluidBlack, 1, 0.5, 0.5, 1.35, 1.0);
            const int fSegs = 36;
            var fRim = new int[fSegs + 1];

            double heightWeight = Math.Clamp((fY - cavityFloorY) / Math.Max(1e-6, rimTopY - cavityFloorY), 0.0, 1.0);
            double waveWeight = heightWeight > 0.40 ? Math.Pow((heightWeight - 0.40) / 0.60, 1.35) : 0.0;

            for (int j = 0; j <= fSegs; j++)
            {
                double th = 2.0 * Math.PI * j / fSegs;
                double wave = (Math.Sin(lobes * th + wavePhase) * 0.75 + Math.Sin((lobes + 2) * th + 1.2) * 0.25) * (lobeAmp * waveWeight);
                // 1.02 multiplier guarantees a tight seal against the inner hymenium wall
                double rAtTheta = (baseFR + wave * 0.45) * 1.02;
                var p = fluidCenter + (right * Math.Cos(th) + fwd * Math.Sin(th)) * Math.Max(0.001, rAtTheta) + up * (meniscusH + wave * 0.65);
                fRim[j] = m.AddVertex(p, up, BonecupFluidSheen, 1, 0.5 + 0.5 * Math.Cos(th), 0.5 + 0.5 * Math.Sin(th), 1.35, 1.0);
            }

            for (int j = 0; j < fSegs; j++)
            {
                Primitives.TriangleFacing(m, fMid, fRim[j], fRim[j + 1], up);
            }

            m.EssentialSpans.Add((fluidFirst, m.Indices.Count - fluidFirst));
        }

        // 4. Crystalline Tomentum Micro-Hairs on Outer Excipulum Wall
        const int hairCount = 36;
        for (int h = 0; h < hairCount; h++)
        {
            double ht = rng.Range(0.15, 0.82);
            int hi = (int)(ht * bowlStations);
            double ha = rng.Range(0, 2.0 * Math.PI);
            var at = LathePoint(hi, ha);
            var radial = (right * Math.Cos(ha) + fwd * Math.Sin(ha)).Normalized();
            var hDir = (radial * 0.85 + up * 0.45).Normalized();
            double hLen = rng.Range(0.0008, 0.0016);

            Primitives.Tube(m, new[] { at, at + hDir * hLen }, new[] { 0.00014, 0.00003 }, 3,
                (k, v) => (BonecupAlabasterLip, 1, k, v, 1.30, 0.0));
        }

        m.RecordLeaf(leafVertex, leafIndex, basePoint, rimTopY, volumetric: true);
        m.EssentialSpans.Add((leafIndex, m.Indices.Count - leafIndex));
    }

    // ---------------------------------------------------------------------------------------------
    // Entry Point
    // ---------------------------------------------------------------------------------------------

    /// <summary>The bonecup: wood-decaying elf-cup fungus of damp forest litter.</summary>
    private static void Bonecup(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };

        // 1. Decaying nurse wood knot and spreading mycelial subiculum cushion
        double branchLen = radius * 1.65;
        double woodRad = height * 0.38;
        var woodOrigin = new Vec3(0, 0, 0);

        BonecupNurseWood(part, woodOrigin, branchLen, woodRad, rng);
        BonecupSubiculumCushion(part, woodOrigin, branchLen, woodRad, rng);
        BonecupMossTurf(part, woodOrigin, branchLen, woodRad, rng);
        BonecupRhizomorphs(part, woodOrigin, radius, rng);

        // 2. Colony of Alabaster Elf Cups at diverse, interlocking life stages
        // Cluster layout: cups sprout caespitose along the top and flanks of the nurse wood

        // A) Grand Sovereign Goblet (1 dominant mature chalice with deep fluid mirror)
        {
            var basePt = woodOrigin + new Vec3(rng.Range(-0.010, 0.010), woodRad * 0.88, rng.Range(-0.010, 0.010));
            double cupR = rng.Range(0.028, 0.035);
            double depth = cupR * rng.Range(1.10, 1.30);
            double stipeL = rng.Range(0.018, 0.026);
            double stipeR = cupR * rng.Range(0.24, 0.28);
            double fluidLvl = rng.Range(0.45, 0.55);
            int lobes = 4 + rng.NextInt(2); // 4 to 5 ruffled lobes
            double lobeAmp = cupR * rng.Range(0.08, 0.14);

            var lean = new Vec3(rng.Range(-0.12, 0.12), 1.0, rng.Range(-0.12, 0.12)).Normalized();
            BonecupGoblet(part, basePt, lean, cupR, depth, stipeL, stipeR, fluidLvl, lobes, lobeAmp, rng);
        }

        // B) Mature Companion Goblets (2-3 large cups clustered beside the sovereign)
        int companionCount = 2 + rng.NextInt(2);
        for (int i = 0; i < companionCount; i++)
        {
            double a = 2.0 * Math.PI * i / companionCount + rng.Range(-0.35, 0.35);
            double dist = rng.Range(0.032, 0.055);
            double x = Math.Cos(a) * (dist * 1.15) + rng.Range(-0.01, 0.01);
            double z = Math.Sin(a) * (dist * 0.70) + rng.Range(-0.01, 0.01);
            var basePt = woodOrigin + new Vec3(x, woodRad * 0.80 + rng.Range(0.001, 0.005), z);

            double cupR = rng.Range(0.020, 0.026);
            double depth = cupR * rng.Range(1.05, 1.25);
            double stipeL = rng.Range(0.014, 0.020);
            double stipeR = cupR * rng.Range(0.23, 0.28);
            double fluidLvl = rng.Range(0.38, 0.48);
            int lobes = 3 + rng.NextInt(3);
            double lobeAmp = cupR * rng.Range(0.06, 0.12);

            var lean = (new Vec3(Math.Cos(a) * 0.22, 1.0, Math.Sin(a) * 0.22)).Normalized();
            BonecupGoblet(part, basePt, lean, cupR, depth, stipeL, stipeR, fluidLvl, lobes, lobeAmp, rng);
        }

        // C) Adolescent Goblets (3-4 medium cups expanding outward)
        int adolCount = 3 + rng.NextInt(2);
        for (int i = 0; i < adolCount; i++)
        {
            double a = 2.0 * Math.PI * (i + 0.5) / adolCount + rng.Range(-0.3, 0.3);
            double dist = rng.Range(0.045, 0.078);
            double x = Math.Cos(a) * (dist * 1.2);
            double z = Math.Sin(a) * (dist * 0.65);
            var basePt = woodOrigin + new Vec3(x, woodRad * 0.70 + rng.Range(0.0, 0.004), z);

            double cupR = rng.Range(0.013, 0.018);
            double depth = cupR * rng.Range(0.95, 1.15);
            double stipeL = rng.Range(0.010, 0.016);
            double stipeR = cupR * rng.Range(0.22, 0.27);
            double fluidLvl = rng.Range(0.28, 0.38);
            int lobes = 3 + rng.NextInt(2);
            double lobeAmp = cupR * rng.Range(0.05, 0.09);

            var lean = (new Vec3(Math.Cos(a) * 0.30, 1.0, Math.Sin(a) * 0.30)).Normalized();
            BonecupGoblet(part, basePt, lean, cupR, depth, stipeL, stipeR, fluidLvl, lobes, lobeAmp, rng);
        }

        // D) Immature Button Primordia (6-8 miniature chalices nestled tightly into crevices)
        int buttonCount = 6 + rng.NextInt(3);
        for (int i = 0; i < buttonCount; i++)
        {
            double a = rng.Range(0, 2.0 * Math.PI);
            double dist = rng.Range(0.025, 0.082);
            double x = Math.Cos(a) * (dist * 1.15);
            double z = Math.Sin(a) * (dist * 0.65);
            var basePt = woodOrigin + new Vec3(x, woodRad * 0.65 + rng.Range(0.0, 0.004), z);

            double cupR = rng.Range(0.006, 0.010);
            double depth = cupR * rng.Range(1.00, 1.25);
            double stipeL = rng.Range(0.006, 0.010);
            double stipeR = cupR * rng.Range(0.25, 0.30);
            var lean = (new Vec3(Math.Cos(a) * 0.35, 1.0, Math.Sin(a) * 0.35)).Normalized();

            BonecupGoblet(part, basePt, lean, cupR, depth, stipeL, stipeR, fluidFraction: 0.0, lobes: 3, lobeAmp: cupR * 0.04, rng);
        }

        NewFloraAppendWorld(m, part, radius, height);
    }
}
