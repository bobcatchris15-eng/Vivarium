using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// New succulents set: Brightknot, Cragset and the individual moss CapstarMoss (2026-11-06 expansion,
/// fictional renames per docs/2026-11-06-flora-fiction-rename.md).
/// Each builder authors in real metres into a scratch part and converts with
/// <see cref="NewFloraAppendWorld"/>. Organ counts honour <c>FloraDetailLevel</c> and <c>Lod</c>
/// through <c>NewFloraCount</c>; <c>Form.FoliageBlade</c>, <c>Form.Sheet</c>, tubes and ellipsoids
/// carry their own internal detail budgets. Foliage blades record their own leaf spans and tubes
/// record structural spans, so only sheet petals (which do not self-record) are recorded manually.
/// </summary>
public static partial class OrganismMeshes
{
    // -----------------------------------------------------------------
    // Shared helpers (NSuc prefix: private to this partial)
    // -----------------------------------------------------------------

    /// <summary>One fleshy lanceolate leaf as a cambered FoliageBlade, base to tip in c1-derived greens.</summary>
    private static void NSucLeaf(MeshData part, Vec3 root, double azimuth, double elevation,
        double length, double widthRatio, double camber, double[] baseCol, double[] tipCol, Rng rng,
        double curlOverride = -1.0)
    {
        if (length < 1e-5) return;
        double ce = Math.Cos(elevation), se = Math.Sin(elevation);
        double ca = Math.Cos(azimuth), sa = Math.Sin(azimuth);
        var dir = new Vec3(ca * ce, se, sa * ce);
        if (dir.LengthSq < 1e-12) dir = Vec3.Up;
        dir = dir.Normalized();
        var tip = root + dir * length;
        var side = new Vec3(-sa, 0, ca);
        double curl = curlOverride >= 0 ? curlOverride : rng.Range(0.04, 0.12);
        FoliageBlade.Build(part, root, tip, side, length * widthRatio, baseCol, tipCol,
            camber: camber, curl: curl, roll: rng.Range(-0.15, 0.15),
            asymmetry: 0.0, shoulder: 0.82, segments: 8);
    }

    /// <summary>
    /// One thin moss microphyll (star leaf) as a single-sheet CurvedLeaf, base to tip in c1-derived greens.
    /// Unlike the fleshy succulent leaves above, a haircap microphyll is a stiff single lamina rather than a
    /// thick shell, so a CurvedLeaf keeps the star leaf cheap and exactly length-capped while still
    /// recording its own leaf span. NB CurvedLeaf camber is an absolute metre arch (unlike FoliageBlade's
    /// ratio), so callers pass ~1 mm values; callers keep the leaf under 18 mm end to end.
    /// </summary>
    private static void NSucMossLeaf(MeshData part, Vec3 root, double azimuth, double elevation,
        double length, double widthRatio, double camber, double[] baseCol, double[] tipCol, Rng rng)
    {
        if (length < 1e-5) return;
        double ce = Math.Cos(elevation), se = Math.Sin(elevation);
        double ca = Math.Cos(azimuth), sa = Math.Sin(azimuth);
        var dir = new Vec3(ca * ce, se, sa * ce);
        if (dir.LengthSq < 1e-12) dir = Vec3.Up;
        dir = dir.Normalized();
        var tip = root + dir * length;
        var side = new Vec3(-sa, 0, ca);
        Primitives.CurvedLeaf(part, root, tip, side, length * widthRatio, baseCol, tipCol,
            camber: camber, longitudinal: 6, asymmetry: rng.Range(-0.10, 0.10));
    }

    /// <summary>
    /// A small yellow star flower at the origin of a scratch mesh: five Sheet petals around a
    /// tiny centre disc. Built in a scratch mesh so the petals read as a rosette, then the caller
    /// appends it at the flower position. Flower organs keep their authored gold via NewFloraMarkOrgan.
    /// </summary>
    private static void NSucStarFlower(MeshData part, Vec3 at, Vec3 tiltAxis, double tilt, ulong fseed, double petalLen, double petalWid,
        double[] petalBase, double[] petalTip, double[] centreCol)
    {
        var tmp = new MeshData { Lod = part.Lod, FloraDetailLevel = part.FloraDetailLevel, FloraVisualSeed = part.FloraVisualSeed };
        double azBase = Rng.HashUnit(fseed, Hash.Fnv1a64("newsuc.flower"), 0) * Math.PI * 2.0;
        for (int k = 0; k < 5; k++)
        {
            double az = azBase + k * (Math.PI * 2.0 / 5.0) + (Rng.HashUnit(fseed, Hash.Fnv1a64("newsuc.petal"), (ulong)k) - 0.5) * 0.12;
            double len = petalLen * (0.92 + 0.16 * Rng.HashUnit(fseed, Hash.Fnv1a64("newsuc.len"), (ulong)k));
            var axis = new AxisParams(Length: len, BaseAngle: 1.32, BaseAzimuth: az, Droop: 0.10,
                WobbleAmplitude: len * 0.06, WobbleFrequency: 1.6, Segments: 4);
            int lv = tmp.VertexCount;
            int li = tmp.Indices.Count;
            Sheet.Build(tmp, new SheetParams(axis, HalfWidth: petalWid, Thickness: 0.0003, Curl: 0.18, DetailLevel: 1, FaceAxisUp: true),
                Rng.Mix(fseed, (ulong)(k * 17 + 3)), petalBase, petalTip);
            tmp.RecordLeaf(lv, li, Vec3.Zero, len);
        }
        // Tiny centre: receptacle disc the five petals are seated on.
        Primitives.Ellipsoid(tmp, new Vec3(0, 0.0006, 0), new Vec3(0.0022, 0.0008, 0.0022), 2, 6,
            (u, v) => (centreCol, 1.0, u, v, 0.0, 0.0));
        NewFloraMarkOrgan(tmp, 0, tmp.VertexCount);
        int first = part.VertexCount;
        part.Append(tmp, p => at + Axis.RotateAround(p, tiltAxis, tilt));
        for (int i = first; i < part.VertexCount; i++)
        {
            var n = Axis.RotateAround(part.NormalAt(i), tiltAxis, tilt);
            part.Normals[i * 3] = (float)n.X;
            part.Normals[i * 3 + 1] = (float)n.Y;
            part.Normals[i * 3 + 2] = (float)n.Z;
        }
    }

    /// <summary>A short fleshy caudex disc closing the base under one rosette so it sits as a body, not a fan over bare ground.</summary>
    private static void NSucCaudex(MeshData part, Vec3 centre, double rad, double[] col)
    {
        int v0 = part.VertexCount;
        int i0 = part.Indices.Count;
        Primitives.Ellipsoid(part, centre + Vec3.Up * 0.0004,
            new Vec3(Math.Max(1e-4, rad), Math.Max(0.0004, rad * 0.18), Math.Max(1e-4, rad)), 2, 6,
            (u, v) => (col, 1.0, u, v, 0.0, 0.0));
        part.RecordLeaf(v0, i0, centre, rad, volumetric: true);
    }

    /// <summary>
    /// A 4-sided (square-section) tapered tube — the Polytrichum capsule is
    /// 4-angled, and Primitives.Tube promotes every request to 6+ sides, so
    /// the capsule needs this dedicated builder to read as 4-angled.
    /// </summary>
    private static void NSucQuadTube(MeshData part, IReadOnlyList<Vec3> path, IReadOnlyList<double> radii,
        double[] baseCol, double[] tipCol)
    {
        if (path.Count < 2) return;
        int start = part.VertexCount;
        var tangent = (path[path.Count - 1] - path[0]).Normalized();
        var side = tangent.Cross(Vec3.Up);
        if (side.LengthSq < 1e-10) side = tangent.Cross(new Vec3(1, 0, 0));
        side = side.Normalized();
        var up = tangent.Cross(side).Normalized();
        // Align the square so one flat faces up: corners at 45 deg offsets.
        for (int i = 0; i < path.Count; i++)
        {
            double t = i / (double)(path.Count - 1);
            var col = Primitives.Mix(baseCol, tipCol, t);
            for (int s = 0; s <= 4; s++)
            {
                double th = Math.PI / 4.0 + s * (Math.PI / 2.0);
                var radial = (side * Math.Cos(th) + up * Math.Sin(th)).Normalized();
                part.AddVertex(path[i] + radial * radii[i], radial, col, 1, t, (double)s / 4.0, 0, 0);
            }
        }
        for (int i = 0; i < path.Count - 1; i++)
            for (int s = 0; s < 4; s++)
            {
                int a = start + i * 5 + s, b = a + 5;
                part.AddTriangle(a, a + 1, b);
                part.AddTriangle(a + 1, b + 1, b);
            }
        part.Structural.Add((start, part.VertexCount - start, 4, Math.Max(radii[0], radii[radii.Count - 1])));
    }

    // -----------------------------------------------------------------
    // Brightknot (Sedum analogue) - low mat of tiny rosettes with gold stars + pink margins
    // -----------------------------------------------------------------

    /// <summary>
    /// Brightknot: a low mat of 24 tiny rosettes packed edge-to-edge into a knot, each 5-6 plump bead-like
    /// (short, narrow, strongly cambered) leaves radiating upward-outward with the crown standing
    /// 6-8 mm proud of the ground, with 8-10 near-sessile stems (1.2-1.8 cm, the whole plant under 3 cm)
    /// carrying clusters of gold star flowers (five petals plus a tiny centre, ~1.6-2.2 cm across) held
    /// clear above/between the rosettes. Rosettes are ~2-3 cm across (2-4 cm per wave-6: tight beads,
    /// not lettuce). Outer-mat leaves are tinted toward a sun-pink (vertex colour toward
    /// pink from the outer 70% of the mat, near-full at the margin in full sun). C1 green (glaucous), C2 golden. Vertex colours are
    /// brightened ~40% to offset the leaf shader's 62% mix toward the dark species pigment; gold/red
    /// scales capped at 1.05 so bright c2 does not clamp to white.
    /// </summary>
    private static void Brightknot(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        double r = radius > 0.01 ? radius : 0.14;
        double h = height > 0.01 ? height : 0.08;
        double hScale = juvenile ? 0.60 : 1.0;

        // Vertex colours are authored ~40% brighter than they should read: the leaf
        // shader mixes every leaf vertex 62% toward the dark species pigment
        // (leaf_pigment in living_leaf_optics.gdshaderinc), so an uncompensated
        // vertex renders near-black olive. Primitives.Scale clamps at 1.0.
        var leafBase = Primitives.Scale(Primitives.Mix(c1, new[] { 0.55, 0.65, 0.50 }, 0.22), 1.40);
        var leafTip = Primitives.Scale(Primitives.Mix(c1, c2, 0.40), 1.40);      // bright yellow-green tip, mostly c1
        var sunPink = Primitives.Scale(Primitives.Mix(Primitives.Mix(c1, c2, 0.40), new[] { 0.90, 0.22, 0.38 }, 0.72), 1.05);
        var caudexCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.10), 1.00);    // 0.72 x 1.4 pre-brightened
        var stemCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.15), 1.19);      // 0.85 x 1.4 pre-brightened
        var petalBase = new[] { 0.98, 0.82, 0.10 };
        var petalTip = new[] { 0.98, 0.82, 0.10 };
        var centreCol = Primitives.Scale(Primitives.Mix(new[] { 0.80, 0.62, 0.15 }, c2, 0.40), 1.05);

        // Wave-9: dense continuous circular cushion of 26-32 plump jelly-bean
        // rosettes covering 0.10-0.80*radius on a Vogel spiral.
        int rosettes = NewFloraCount(part, juvenile ? 8 + rng.NextInt(3) : 26 + rng.NextInt(7)); // 26-32 adult
        const double golden = 2.399963229728653;
        var centres = new List<Vec3>();
        for (int i = 0; i < rosettes; i++)
        {
            double f = (i + 0.5) / rosettes;
            double th = i * golden + rng.Range(-0.20, 0.20);
            double rr = r * (0.10 + 0.70 * Math.Sqrt(f)) * rng.Range(0.92, 1.00); // covers 0.10-0.80*radius
            var centre = new Vec3(Math.Cos(th) * rr, 0.0018 + h * 0.012 * (1.0 - f) * rng.Range(0.5, 1.0), Math.Sin(th) * rr);
            var parent = Vec3.Zero;
            double nearest = centre.LengthSq;
            foreach (var other in centres)
            {
                double distance = (centre - other).LengthSq;
                if (distance >= nearest) continue;
                nearest = distance;
                parent = other;
            }
            var elbow = Vec3.Lerp(parent, centre, 0.54) - Vec3.Up * 0.0012;
            Primitives.Tube(part, new[] { parent, elbow, centre }, new[] { 0.0016, 0.0012, 0.0010 }, 6,
                (station, v) => (Primitives.Mix(caudexCol, stemCol, station * 0.35), 1, station * 0.5, v, 0, 0));
            centres.Add(centre);
            NSucCaudex(part, centre, rng.Range(0.0038, 0.0055), caudexCol);

            // Outer-mat margin in full sun: tint the leaf tips toward sun-pink, ramping in over the
            // outer 70% of the mat so the pink reads as a margin rather than a single leaf edge.
            double pink = MathD.Clamp01((f - 0.30) / 0.70) * (0.58 + 0.38 * Math.Sin(th + 0.7));
            var thisTip = Primitives.Mix(leafTip, sunPink, pink);

            int leaves = NewFloraCount(part, 5 + rng.NextInt(2)); // 5-6 per rosette (budget: dense rosettes, fewer shell blades each)
            double leafAz0 = rng.Range(0, Math.PI * 2.0);
            for (int k = 0; k < leaves; k++)
            {
                double az = leafAz0 + k * golden + rng.Range(-0.22, 0.22);
                double len = rng.Range(0.010, 0.014) * hScale; // plump jelly-bean beads (~1.0-1.4 cm), crown 6-8 mm proud
                double elev = rng.Range(0.65, 1.15); // tighter, more erect bead (~37-66 deg), not flat lettuce
                var root = centre + new Vec3(Math.Cos(az) * 0.0015, rng.Range(0.0, 0.003), Math.Sin(az) * 0.0015);
                // Plump jelly-bean: widthRatio 0.42-0.55 and strongly cambered
                // (0.60-0.75) for a fat bead; near-zero curl keeps it domed.
                NSucLeaf(part, root, az, elev, len, rng.Range(0.42, 0.55), rng.Range(0.60, 0.75),
                    leafBase, thisTip, rng, curlOverride: rng.Range(0.02, 0.05));
            }
        }

        // Wave-9: 12-16 crisp flat-open 5-pointed golden-yellow star flowers
        // (2.0-2.6 cm across, bright gold (0.98,0.82,0.10)) distributed evenly
        // across the cushion, one per stem, sitting just above the foliage.
        if (!juvenile)
        {
            int stems = NewFloraCount(part, 9 + rng.NextInt(4));
            for (int s = 0; s < stems && centres.Count > 0; s++)
            {
                var basePt = centres[(s * centres.Count / stems) % centres.Count];
                double stemLen = rng.Range(0.014, 0.025);
                double leanAz = rng.Range(0, Math.PI * 2.0);
                var outward = new Vec3(Math.Cos(leanAz), 0, Math.Sin(leanAz));
                var apex = basePt + Vec3.Up * stemLen + outward * rng.Range(0.004, 0.010);
                var mid = basePt + Vec3.Up * (stemLen * 0.55) + outward * rng.Range(0.000, 0.002);
                Primitives.Tube(part, new[] { basePt, mid, apex }, new[] { 0.0008, 0.0006, 0.0004 }, 6,
                    (idx, v) => (Primitives.Mix(stemCol, leafTip, (double)idx / 2), 1, (double)idx / 2, v, 0, 0));

                NSucStarFlower(part, apex, Vec3.Up.Cross(outward), rng.Range(0.20, 0.48),
                    Rng.Mix(seed, (ulong)(s * 397 + 7)),
                    rng.Range(0.0100, 0.0130), rng.Range(0.0032, 0.0040), petalBase, petalTip, centreCol);
            }
        }

        NewFloraAppendWorld(m, part, radius, height);
    }

    // -----------------------------------------------------------------
    // Cragset (Sempervivum analogue) - tight blue rosette with pups
    // -----------------------------------------------------------------

    /// <summary>
    /// Cragset: one tight geometric mother rosette of 32-44 overlapping thick
    /// pointed leaves spiralling in a Fibonacci spiral outward (outer longest
    /// and most sprawling, inner short and erect), glaucous slate-blue blade
    /// centres with dark crimson-purple mucronate tips, outer leaves strongly
    /// recurved (tips bending back). 5-6 small offset pups (ever-smaller
    /// copies of the mother) ring the mother widely in the crack; the mother
    /// is raised proud of the crack (outer leaves 48-68 mm, crown raised
    /// clear of the stone); vertex colours are brightened ~40% to offset
    /// the leaf shader's 62% mix toward the grey pigment.
    /// </summary>
    private static void Cragset(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        double r = radius > 0.01 ? radius : 0.12;
        double h = height > 0.01 ? height : 0.10;
        double hScale = juvenile ? 0.62 : 1.0;

        // Deep blue-grey bloom (saturated, not icy-white): FoliageBlade grades root->tip,
        // so a deep bloom-blue base stands for the bloom on the leaf centres with the
        // tip kept mostly blue-grey — only a faint c2 edge, so the plant stays sculpted
        // blue-grey instead of reading pink-fleshy.
        // Brightened ~40% for the same reason as Brightknot: the leaf shader mixes
        // 62% of every leaf vertex toward the grey species pigment, washing the
        // blue bloom out before it ever reaches the screen.
        var leafBase = Primitives.Scale(Primitives.Mix(c1, new[] { 0.24, 0.36, 0.58 }, 0.68), 1.40);
        // Dark crimson-purple mucronate tip (Wave-8): the FoliageBlade grades
        // root->tip, so the pointed tip alone carries the crimson-purple.
        var leafTip = Primitives.Scale(Primitives.Mix(new[] { 0.45, 0.10, 0.32 }, c2, 0.15), 1.05);
        var caudexCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.10), 0.84); // 0.60 x 1.4
        var stolonCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.12), 0.98); // 0.70 x 1.4

        // One tight mother rosette in the crack; the ring of small pups below carries the
        // "children ringing the mother" identity. Leaf/pup counts still honour detail via
        // NewFloraCount, so low LOD drops whole blades, not the mother.
        int mainRosettes = NewFloraCount(part, 1);
        var mains = new List<Vec3>();
        for (int mIdx = 0; mIdx < mainRosettes; mIdx++)
        {
            double ma = rng.Range(0, Math.PI * 2.0);
            double mr = r * rng.Range(0.0, 0.12);
            var centre = new Vec3(Math.Cos(ma) * mr, 0.005 + h * 0.035, Math.Sin(ma) * mr);
            mains.Add(centre);
            NSucCaudex(part, centre, rng.Range(0.012, 0.017) * hScale, caudexCol);

            // Wave-8: 32-44 mother leaves in a Fibonacci spiral (golden-angle),
            // overlapping thick glaucous pointed blades with crimson tips.
            int leaves = NewFloraCount(part, 32 + rng.NextInt(13)); // 32-44 overlapping, spiralling
            const double golden = 2.399963229728653;
            double az0 = rng.Range(0, Math.PI * 2.0);
            for (int k = 0; k < leaves; k++)
            {
                double rankT = leaves <= 1 ? 0.5 : (double)k / (leaves - 1); // 0 outer (oldest) .. 1 inner (youngest)
                double az = az0 + k * golden + rng.Range(-0.12, 0.12);
                double len = (rng.Range(0.048, 0.068) * (1.0 - rankT * 0.50)) * hScale; // wedged proud: outer long, inner short
                double elev = 0.14 + 1.06 * Math.Pow(rankT, 1.3); // outer sprawling, inner erect, crown domed clear of the crack
                var root = centre + new Vec3(Math.Cos(az) * (0.003 + rankT * 0.006), rankT * 0.012 * hScale + rng.Range(0, 0.003), Math.Sin(az) * (0.003 + rankT * 0.006));
                // Outer leaves recurved: curl 0.50 at the rim falling to 0.08 at the centre,
                // so the tips bend back and down like a sempervivum rim.
                double curl = 0.08 + 0.42 * (1.0 - rankT);
                // Thick (0.28-0.38) and cambered (0.35-0.55): overlapping thick
                // pointed lanceolate blade, not a spatulate spoon.
                NSucLeaf(part, root, az, elev, len, rng.Range(0.28, 0.38), rng.Range(0.35, 0.55), leafBase, leafTip, rng, curlOverride: curl);
            }

            // 4-6 small pups ring the mother widely, each an ever-smaller copy of her.
            int pups = NewFloraCount(part, juvenile ? 2 : 4 + rng.NextInt(3)); // 4-6
            if (mIdx == 0)
            {
                for (int p = 0; p < pups; p++)
                {
                    double pa = (p / (double)pups) * Math.PI * 2.0 + rng.Range(-0.25, 0.25);
                    double pd = rng.Range(0.045, 0.076) * hScale; // wide ring, clear of the mother rim so daughters read at distance
                    var pupC = centre + new Vec3(Math.Cos(pa) * pd, -0.001, Math.Sin(pa) * pd);
                    // Short stolon from the mother caudex to the pup.
                    var stolonMid = (centre + pupC) * 0.5 + Vec3.Up * 0.0012;
                    Primitives.Tube(part, new[] { centre + Vec3.Up * 0.0025, stolonMid, pupC + Vec3.Up * 0.0035 },
                        new[] { 0.0028, 0.0023, 0.0019 }, 6,
                        (idx, v) => (stolonCol, 1, (double)idx / 2, v, 0, 0));
                    double pupAge = rng.Range(0.65, 1.14);
                    NSucCaudex(part, pupC, rng.Range(0.006, 0.010) * pupAge * hScale, caudexCol);
                    int pupLeaves = NewFloraCount(part, Math.Max(5, (int)Math.Round((9 + rng.NextInt(3)) * pupAge))); // 9-11 so each daughter reads as a small rosette
                    double paz0 = rng.Range(0, Math.PI * 2.0);
                    for (int k = 0; k < pupLeaves; k++)
                    {
                        double rankT = pupLeaves <= 1 ? 0.5 : (double)k / (pupLeaves - 1);
                        double az = paz0 + k * golden + rng.Range(-0.14, 0.14);
                        double len = rng.Range(0.026, 0.038) * (1.0 - rankT * 0.45) * pupAge * hScale; // still ~2/3 of the mother
                        double elev = 0.30 + 0.92 * Math.Pow(rankT, 1.4);
                        var root = pupC + new Vec3(Math.Cos(az) * 0.0018, rankT * 0.0060 * hScale, Math.Sin(az) * 0.0018);
                        double pCurl = 0.06 + 0.24 * (1.0 - rankT);
                        NSucLeaf(part, root, az, elev, len, rng.Range(0.34, 0.44), rng.Range(0.35, 0.52), leafBase, leafTip, rng, curlOverride: pCurl);
                    }
                }
            }
        }

        NewFloraAppendWorld(m, part, radius, height);
    }

    // -----------------------------------------------------------------
    // CapstarMoss (haircap analogue) - upright star shoots with gold capsules
    // -----------------------------------------------------------------

    /// <summary>
    /// Capstar moss: 14-18 upright shoots (4-5 cm), each a slender stem
    /// topped by 2-3 whorls of spreading 5-6-pointed star-shaped microphylls
    /// so each shoot top looks like a star. 8-10 slender reddish-gold setae
    /// (3.5-4.5 cm above the shoots) each ending in a distinct nodding
    /// 4-angled golden-amber spore capsule with a pointed pale calyptra hood.
    /// C1 deep green, C2 gold. Vertex colours are brightened extra (~75% for
    /// fresh leaves, ~40% for already-saturated gold) to offset the leaf
    /// shader's 62% mix toward the dark species pigment plus the moss surface
    /// mode's second multiply.
    /// </summary>
    private static void CapstarMoss(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        double r = radius > 0.01 ? radius : 0.19;
        double h = height > 0.01 ? height : 0.12;
        double hScale = juvenile ? 0.60 : 1.0;

        // Brightened extra for the moss double-darken: leaf_pigment mixes 62% of each leaf
        // vertex toward the dark species pigment, then surface_mode 0 multiplies again by the
        // scanned-moss luminance/aggregate. Fresh greens get the full 0.72 x 1.75 compensation;
        // the gold capsule is already saturated so it keeps 1.05 to stay orange-gold rather than
        // clamping to pale yellow-white.
        var stemCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.08), 1.26); // 0.72 x 1.75
        var leafBase = Primitives.Scale(Primitives.Mix(c1, c2, 0.05), 1.75);  // deep green, lifted past the double-darken
        var leafTip = Primitives.Scale(Primitives.Mix(c1, c2, 0.25), 1.75);   // lightly sun-touched tip
        var matCol = Primitives.Scale(Primitives.Mix(c1, c2, 0.06), 1.09);     // 0.62 x 1.75, cushion still subtle
        var setaCol = Primitives.Scale(Primitives.Mix(c2, new[] { 0.45, 0.30, 0.10 }, 0.30), 1.05);
        var capsuleCol = Primitives.Scale(Primitives.Mix(c2, new[] { 0.88, 0.62, 0.15 }, 0.42), 1.05); // bright orange-gold
        var hoodCol = Primitives.Scale(Primitives.Mix(c2, new[] { 0.92, 0.86, 0.68 }, 0.65), 1.05); // pale pointed calyptra hood

        // No artificial basal disk: overlapping rooted microphylls form the cushion.

        int shoots = NewFloraCount(part, juvenile ? 5 + rng.NextInt(2) : 20 + rng.NextInt(7)); // 20-26 staggered adult shoots
        // 14-18 shoots carry a sporophyte (seta + capsule), evenly spread.
        int fertile = juvenile ? 0 : Math.Min(shoots, 9 + rng.NextInt(4)); // 9-12 fertile
        for (int s = 0; s < shoots; s++)
        {
            double baseAz = rng.Range(0, Math.PI * 2.0);
            double baseR = r * (0.12 + 0.63 * Math.Sqrt(rng.NextDouble()));
            var basePt = new Vec3(Math.Cos(baseAz) * baseR, 0.0025, Math.Sin(baseAz) * baseR);
            // Small per-shoot pad so each stem has its own footing instead of one shared disc.
            int padV0 = part.VertexCount;
            double padRad = rng.Range(0.0035, 0.0055);
            Primitives.Ellipsoid(part, basePt + Vec3.Up * 0.0004,
                new Vec3(padRad, 0.0012, padRad), 2, 6,
                (u, v) => (matCol, 1.0, u, v, 0.0, 0.0));
            part.RecordLeaf(padV0, part.Indices.Count, basePt, padRad, volumetric: true);
            int basalLeaves = 3 + rng.NextInt(2);
            double basalPhase = rng.Range(0.0, Math.PI * 2.0);
            for (int bl = 0; bl < basalLeaves; bl++)
            {
                double az = basalPhase + bl * Math.PI * 2.0 / basalLeaves + rng.Range(-0.22, 0.22);
                var leafRoot = basePt + new Vec3(Math.Cos(az) * padRad * 0.35, 0.0008, Math.Sin(az) * padRad * 0.35);
                NSucMossLeaf(part, leafRoot, az, rng.Range(0.06, 0.22), rng.Range(0.010, 0.017),
                    rng.Range(0.24, 0.34), rng.Range(0.0004, 0.0011), leafBase, leafTip, rng);
            }

            double shootH = Math.Max(0.030, rng.Range(0.040, 0.050)) * hScale; // upright Polytrichum shoot (4-5 cm)
            double leanAz = baseAz + rng.Range(-0.6, 0.6);
            var outward = new Vec3(Math.Cos(leanAz), 0, Math.Sin(leanAz));
            double lean = rng.Range(0.03, 0.13);
            var tipPt = basePt + Vec3.Up * (shootH * Math.Cos(lean)) + outward * (shootH * Math.Sin(lean));
            var midPt = basePt + Vec3.Up * (shootH * 0.52) + outward * (shootH * lean * 0.30);
            Primitives.Tube(part, new[] { basePt, midPt, tipPt }, new[] { 0.0020, 0.0016, 0.0011 }, 6,
                (idx, v) => (Primitives.Mix(stemCol, leafBase, (double)idx / 2), 1, (double)idx / 2, v, 0, 0));

            // Star head (Wave-9): 2-3 whorls of 5-6 spreading microphylls so
            // each shoot top looks like a star 18-22 mm across on a full cushion.
            int whorls = 2 + rng.NextInt(2); // 2-3 whorls
            for (int w = 0; w < whorls; w++)
            {
                int pts = NewFloraCount(part, 5 + rng.NextInt(2)); // 5-6 points per whorl
                double leafAz0 = rng.Range(0, Math.PI * 2.0) + w * 0.52;
                var whorlBase = tipPt + Vec3.Up * (-w * rng.Range(0.005, 0.008)) + outward * (-w * 0.001);
                for (int k = 0; k < pts; k++)
                {
                    double az = leafAz0 + k * (Math.PI * 2.0 / pts) + rng.Range(-0.12, 0.12);
                    double len = Math.Min(0.013, rng.Range(0.010, 0.013) * hScale); // longer radial points make each staggered star legible
                    double elev = w == 0 ? rng.Range(0.25, 0.50) : rng.Range(0.50, 0.90); // top whorl widest open
                    var root = whorlBase + new Vec3(Math.Cos(az) * 0.0008, rng.Range(0.0, 0.0015), Math.Sin(az) * 0.0008);
                    NSucMossLeaf(part, root, az, elev, len, rng.Range(0.22, 0.30), rng.Range(0.0006, 0.0012), leafBase, leafTip, rng);
                }
            }

            // Sporophyte (Wave-8): slender reddish-gold seta 3.5-4.5 cm above
            // the shoot with a nodding 4-angled golden-amber capsule + pointed
            // pale calyptra hood, lifted clear of the star tops.
            bool isFertile = fertile > 0 && (shoots <= 1 || (s * fertile / Math.Max(1, shoots)) != ((s + 1) * fertile / Math.Max(1, shoots)));
            if (isFertile && !juvenile)
            {
                double setaLen = rng.Range(0.026, 0.034) * hScale; // shorter tethered capsules stay visibly tied to the star shoots
                double nodAz = rng.Range(0, Math.PI * 2.0);
                var nodOut = new Vec3(Math.Cos(nodAz), 0, Math.Sin(nodAz));
                var setaMid = tipPt + Vec3.Up * (setaLen * 0.55) + nodOut * rng.Range(0.006, 0.012);
                var setaTop = tipPt + Vec3.Up * setaLen + nodOut * rng.Range(0.010, 0.016);
                Primitives.Tube(part, new[] { tipPt, setaMid, setaTop }, new[] { 0.00070, 0.00060, 0.00045 }, 6,
                    (idx, v) => (setaCol, 1, (double)idx / 2, v, 0, 0));
                // Capsule nodding clear of the seta tip: a 4-angled body (11 mm
                // long, ~7 mm wide) built as a square-section tube along the nod
                // direction, with a pointed pale calyptra hood continuing it —
                // not an ellipsoid button/toadstool. The axis hangs
                // sideways-down so the capsule reads as a moss capsule.
                var capDir = (nodOut * 0.86 + Vec3.Up * -0.52).Normalized();
                var capBase = setaTop + Vec3.Up * 0.0010;
                var capMid = capBase + capDir * 0.0060 + Vec3.Up * -0.0005;
                var capTip = capBase + capDir * 0.0125 + Vec3.Up * -0.0010;
                int capV0 = part.VertexCount;
                NSucQuadTube(part, new[] { capBase, capMid, capTip }, new[] { 0.0032, 0.0038, 0.0021 }, capsuleCol, hoodCol);
                NewFloraMarkOrgan(part, capV0, part.VertexCount - capV0);
            }
        }

        NewFloraAppendWorld(m, part, radius, height);
    }
}
