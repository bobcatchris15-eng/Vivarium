using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Horsetail/skirting-cabbage hybrid for the 'ringreed' species.
///
/// The plant is authored in the engine's unit space (radius 1 in XZ, height 1 in Y), like the
/// other mid-story forms.
///
/// The species used to be a straight horsetail: a fluted, jointed culm with tiered whorls of
/// branchlets and a terminal spore cone. It now reads as a horsetail that has been crossed
/// with a skunk cabbage (Symplocarpus foetidus), and the two parents' silhouettes are both
/// still recognisable:
///
///   * the HORSETAIL gives the skeleton - thick, hollow, jointed stalks divided by dark
///     ringed collars with black-toothed rims, and tiered whorls of drooping branchlets;
///   * the SKUNK CABBAGE gives the flesh - a crown of broad, heart-shaped, strongly veined
///     leaves at the base, and the big mottled hood (the spathe) that in the real plant
///     encloses the flowering spadix;
///   * a FERN's fiddlehead sits where the horsetail's spore cone used to be: the new frond is
///     still coiled on its stalk, a tight crozier, wrapped in the cabbage's hood.
///
/// The result is a plant that is neither quite a horsetail nor quite a skunk cabbage, and
/// reads as a wetland curiosity rather than a copy of either.
///
/// Reference renders: scripts/visuals/ringreed/skunkcabbage_reference.py.
/// </summary>
public static partial class OrganismMeshes
{
    /// <summary>The spathe: mottled plum over olive, the skunk cabbage's hood.</summary>
    private static readonly double[] ReedSpathePlum = { 0.255, 0.105, 0.135 };
    private static readonly double[] ReedSpatheOlive = { 0.185, 0.200, 0.085 };
    /// <summary>The crozier: a pale, buttery green with darker scales.</summary>
    private static readonly double[] ReedFiddle = { 0.300, 0.355, 0.130 };
    private static readonly double[] ReedFiddleScale = { 0.175, 0.225, 0.080 };
    /// <summary>The basal leaves: a deep, glossy green with paler veins.</summary>
    private static readonly double[] ReedLeaf = { 0.038, 0.115, 0.042 };
    private static readonly double[] ReedLeafPale = { 0.082, 0.168, 0.068 };

    /// <summary>
    /// One broad, heart-shaped leaf of the cabbage parent: a cordate blade with two basal
    /// lobes and an open sinus at the petiole, paler along the midrib and the veins that
    /// radiate from it.
    /// </summary>
    private static void ReedCabbageLeaf(MeshData m, Vec3 attach, Vec3 along, double length,
        double halfWidth, double notch, Rng rng)
    {
        int leafVertex = m.VertexCount, leafIndex = m.Indices.Count;
        // The blade lies out and faces the sky, the way a skunk cabbage leaf does, and then
        // falls away at the tip. Its plane is close to horizontal, so it is read from above
        // rather than edge-on.
        var across = Vec3.Up.Cross(along);
        if (across.LengthSq < 1e-10) across = new Vec3(1, 0, 0);
        across = across.Normalized();
        var up = along.Cross(across).Normalized();
        if (up.Y < 0) { up = -up; across = -across; }

        // The cordate outline: the apex, the two margins bulging out, the two basal lobes
        // reaching back past the petiole, and the sinus between them.
        var ox = new List<double>();
        var oy = new List<double>();
        void Bez(double x0, double y0, double cx, double cy, double x1, double y1, int n)
        {
            for (int i = 0; i < n; i++)
            {
                double t = i / (double)n;
                double mt = 1 - t;
                ox.Add(mt * mt * x0 + 2 * mt * t * cx + t * t * x1);
                oy.Add(mt * mt * y0 + 2 * mt * t * cy + t * t * y1);
            }
        }
        const int M = 10, K = 6;
        // A broad, rounded apex, two margins bulging out to the widest point just above the
        // lobes, then two rounded basal lobes that reach back past the petiole, and the open
        // sinus between them. That cleft is the whole reason the blade reads as a heart.
        Bez(0.30 * halfWidth, 0.92 * length, halfWidth * 1.06, 0.42 * length, halfWidth, -notch * 0.70, M);
        Bez(halfWidth, -notch * 0.70, halfWidth * 0.82, -notch * 1.20, 0.0, 0.0, K);
        Bez(0.0, 0.0, -halfWidth * 0.82, -notch * 1.20, -halfWidth, -notch * 0.70, K);
        Bez(-halfWidth, -notch * 0.70, -halfWidth * 1.06, 0.42 * length, -0.30 * halfWidth, 0.92 * length, M);
        // The apex itself, a broad arc closing the top.
        Bez(-0.30 * halfWidth, 0.92 * length, 0.0, 1.04 * length, 0.30 * halfWidth, 0.92 * length, 6);

        int n = ox.Count;
        double droop = rng.Range(0.45, 0.85);
        double dish = rng.Range(0.10, 0.20);

        // The fan is dished and falls away from the petiole, so the blade curves over.
        var positions = new Vec3[n];
        var normals = new Vec3[n];
        for (int i = 0; i < n; i++)
        {
            double x = ox[i], y = oy[i];
            // The blade is rotated about its own width axis as it runs out, so it droops
            // away from the petiole instead of staying flat.
            double theta = droop * Math.Pow(Math.Abs(y) / Math.Max(1e-6, length), 1.5);
            double ct = Math.Cos(theta), st = Math.Sin(theta);
            var fwd = along * ct - up * st;
            var lift = along * st + up * ct;
            // The blade cups slightly, deepest along the midrib.
            double cup = dish * (1.0 - Math.Min(1.0, Math.Abs(x) / Math.Max(1e-6, halfWidth)));
            positions[i] = attach + across * x + fwd * y + lift * (length * cup * 0.35);
        }

        int centre = m.AddVertex(attach, up, Primitives.Mix(ReedLeaf, ReedLeafPale, 0.30), 1, 0, 0.5, 0, 1);
        var ids = new int[n];
        for (int i = 0; i < n; i++)
        {
            // The veins of a skunk cabbage leaf radiate from the petiole, so the blade is
            // paler along the midrib and where the palmate veins run out.
            double vein = 0.12 + 0.34 * Math.Exp(-Math.Abs(ox[i]) / Math.Max(1e-6, halfWidth * 0.42));
            var col = Primitives.Mix(ReedLeaf, ReedLeafPale, vein);
            ids[i] = m.AddVertex(positions[i], up, col, 1, 1, 0.5 + ox[i] / Math.Max(1e-6, halfWidth) * 0.5, 0, 1);
        }

        // Smooth normals over the dished blade.
        for (int i = 0; i < n; i++)
        {
            var a = positions[i];
            var b = positions[(i + 1) % n];
            var c = positions[(i - 1 + n) % n];
            var nrm = (b - attach).Cross(c - attach);
            if (nrm.LengthSq < 1e-18) nrm = up;
            normals[i] = nrm.Normalized();
        }
        for (int i = 0; i < n; i++)
        {
            int v = ids[i];
            m.Normals[v * 3] = (float)normals[i].X;
            m.Normals[v * 3 + 1] = (float)normals[i].Y;
            m.Normals[v * 3 + 2] = (float)normals[i].Z;
        }

        for (int i = 0; i < n; i++)
        {
            int a = ids[i], b = ids[(i + 1) % n];
            var normal = (positions[i] - attach).Cross(positions[(i + 1) % n] - attach);
            if (normal.LengthSq < 1e-18) continue;
            normal = normal.Normalized();
            Primitives.TriangleFacing(m, centre, a, b, normal);
        }

        m.RecordLeaf(leafVertex, leafIndex, attach, length);
    }

    /// <summary>
    /// The crozier: a new frond still coiled on its stalk, a tight logarithmic spiral with
    /// dark scales down its outside.
    /// </summary>
    private static void ReedFiddlehead(MeshData m, Vec3 stalkTip, Vec3 axis, Vec3 forward,
        double coilRadius, double hoodHeight, Rng rng)
    {
        var up = axis.Normalized();
        var lean = forward - up * forward.Dot(up);
        if (lean.LengthSq < 1e-10) lean = new Vec3(1, 0, 0);
        lean = lean.Normalized();
        var side = up.Cross(lean).Normalized();

        // The fiddlehead stipe emerges directly from the top joint of the stalk,
        // rises through the hollow interior of the spathe hood, emerges at its lip,
        // and coils into a tight logarithmic crozier.
        const int stipeSteps = 8;
        const int coilSteps = 24;
        const int steps = stipeSteps + coilSteps;
        var path = new Vec3[steps + 1];
        var radii = new double[steps + 1];

        // 1. Rising stipe inside the hood up to the rim
        double rimH = hoodHeight * 0.85;
        double rimLean = hoodHeight * 0.42;
        var rimPos = stalkTip + up * rimH + lean * rimLean;

        for (int i = 0; i <= stipeSteps; i++)
        {
            double t = (double)i / stipeSteps;
            path[i] = stalkTip + up * (rimH * t) + lean * (rimLean * t * t);
            radii[i] = coilRadius * (0.42 - 0.12 * t);
        }

        // 2. Coiling crozier beyond the rim
        double turns = rng.Range(1.25, 1.55);
        for (int i = 1; i <= coilSteps; i++)
        {
            double s = (double)i / coilSteps;
            double r = coilRadius * Math.Exp(-1.45 * s);
            double phi = s * turns * Math.PI * 2.0;
            var offset = lean * (r * Math.Sin(phi)) + up * (r * (1.0 - Math.Cos(phi)) * 0.75) + side * (0.003 * Math.Sin(s * 3.0));
            path[stipeSteps + i] = rimPos + offset + up * (0.015 * s);
            radii[stipeSteps + i] = coilRadius * (0.30 - 0.18 * s) + 0.0015;
        }

        Primitives.Tube(m, path, radii, 7,
            (i, v) => (Primitives.Mix(ReedFiddle, ReedFiddleScale, 0.35 + 0.4 * Math.Sin(v * 7.0)), 1, i / (double)steps, v, 0, 0));

        // The scales: the small overlapping plates that cover the crozier.
        int scales = 16;
        for (int s = 0; s < scales; s++)
        {
            double t = 0.25 + 0.70 * (s / (double)(scales - 1));
            int i = Math.Min(steps, (int)(t * steps));
            var at = path[i];
            var tangent = (path[Math.Min(steps, i + 1)] - path[Math.Max(0, i - 1)]);
            if (tangent.LengthSq < 1e-12) tangent = up;
            tangent = tangent.Normalized();
            var outward = tangent.Cross(side);
            if (outward.LengthSq < 1e-10) outward = lean;
            outward = outward.Normalized();
            double a = s * 2.399963;
            var dir = (side * Math.Cos(a) + outward * Math.Sin(a)).Normalized();
            double sz = radii[i] * 1.35;
            int c0 = m.AddVertex(at + dir * (sz * 0.7) - tangent * (sz * 0.7), dir, ReedFiddleScale, 1, t, 0, 0, 0);
            int c1 = m.AddVertex(at + dir * (sz * 0.7) + tangent * (sz * 0.7), dir, ReedFiddleScale, 1, t, 1, 0, 0);
            int c2 = m.AddVertex(at + dir * (sz * 1.45), dir, ReedFiddle, 1, t + 0.04, 0.5, 0, 0);
            Primitives.TriangleFacing(m, c0, c1, c2, dir);
            Primitives.TriangleFacing(m, c1, c0, c2, -dir);
        }
    }

    /// <summary>
    /// A fluted culm.
    ///
    /// A horsetail's stem is ribbed, not round: a ring of longitudinal ridges separated by
    /// deep grooves, and the flutes carry the shading the whole way up the stalk. The ribs
    /// here are real geometry - the grooves are cut into the section - so the stalk catches
    /// the light along its length instead of reading as a painted stripe.
    /// </summary>
    private static void ReedFlutedTube(MeshData m, IReadOnlyList<Vec3> path, IReadOnlyList<double> radii,
        int sides, int ribs, double ribDepth, double[] baseCol, double[] ridgeCol, double[] grooveCol,
        Func<int, double, (double[] Col, double A, double U, double V, double U2, double V2)> attr)
    {
        if (path.Count < 2) return;
        int start = m.VertexCount;
        var tangents = new Vec3[path.Count];
        for (int i = 0; i < path.Count; i++)
        {
            Vec3 d;
            if (i == 0) d = path[1] - path[0];
            else if (i == path.Count - 1) d = path[^1] - path[^2];
            else d = path[i + 1] - path[i - 1];
            if (d.LengthSq < 1e-14) d = Vec3.Up;
            tangents[i] = d.Normalized();
        }

        var side = tangents[0].Cross(Vec3.Up);
        if (side.LengthSq < 1e-10) side = tangents[0].Cross(new Vec3(1, 0, 0));
        side = side.Normalized();

        for (int i = 0; i < path.Count; i++)
        {
            var dir = tangents[i];
            if (i > 0)
            {
                side = side - dir * side.Dot(dir);
                if (side.LengthSq < 1e-10)
                {
                    side = dir.Cross(Vec3.Up);
                    if (side.LengthSq < 1e-10) side = dir.Cross(new Vec3(1, 0, 0));
                }
                side = side.Normalized();
            }
            var up2 = side.Cross(dir).Normalized();

            for (int s = 0; s <= sides; s++)
            {
                double th = 2 * Math.PI * s / sides;
                // 1 on a ridge crest, 0 at the bottom of a groove.
                double rib = 0.5 + 0.5 * Math.Cos(ribs * th);
                double r = radii[i] * (1.0 - ribDepth * (1.0 - rib));
                var radial = (side * Math.Cos(th) + up2 * Math.Sin(th)).Normalized();
                // The groove is a deep shadow and the ridge crest catches the light, and a
                // fine darker line runs down the crest the way a horsetail's carinal ridge does.
                var col = Primitives.Mix(grooveCol, baseCol, Math.Min(1.0, rib * 2.0));
                col = Primitives.Mix(col, ridgeCol, Math.Max(0.0, rib * 2.0 - 1.0));
                var a = attr(i, (double)s / sides);
                m.AddVertex(path[i] + radial * r, radial, col, a.A, a.U, a.V, a.U2, a.V2);
            }
        }
        for (int i = 0; i < path.Count - 1; i++)
            for (int s = 0; s < sides; s++)
            {
                int a = start + i * (sides + 1) + s, b = a + sides + 1;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
    }

    /// <summary>
    /// The spathe: the skunk cabbage's hood, a big pleated and mottled bract that wraps the
    /// crozier and peels back at its rim.
    /// </summary>
    private static void ReedSpathe(MeshData m, Vec3 basePoint, Vec3 axis, Vec3 forward,
        double baseRadius, double height, Rng rng, int rings, int sides)
    {
        var up = axis.Normalized();
        var lean = forward - up * forward.Dot(up);
        if (lean.LengthSq < 1e-10) lean = new Vec3(1, 0, 0);
        lean = lean.Normalized();
        var side = up.Cross(lean).Normalized();

        int start = m.VertexCount;
        for (int r = 0; r <= rings; r++)
        {
            double v = r / (double)rings;
            // The hood swells out from the stalk into a broad belly and then snaps inward at
            // the very top, so it closes over the crozier the way a real spathe does.
            double rad = baseRadius * (1.0 + 2.3 * Math.Sin(v * Math.PI * 0.62));
            rad *= 1.0 - 0.72 * Math.Pow(v, 6.0);
            // Only the upper part of the hood leans over.
            double leanOut = height * 0.55 * Math.Pow(v, 2.4);
            var centre = basePoint + up * (height * v) + lean * leanOut;
            for (int s = 0; s <= sides; s++)
            {
                double u = s / (double)sides;
                double ang = 2 * Math.PI * u;
                // The spathe is strongly pleated, in vertical ribs.
                double pleat = 1.0 + 0.13 * Math.Cos(ang * 7.0) * (0.35 + 0.65 * v);
                // Its rim is scalloped and cut back unevenly.
                double rim = 1.0 - 0.10 * Math.Cos(ang * 7.0);
                var dir = (side * Math.Cos(ang) + lean * Math.Sin(ang)).Normalized();
                var p = centre + dir * (rad * pleat * rim);
                // The hood is mottled: plum and olive in patches, not one flat colour.
                double patch = 0.5 + 0.5 * Math.Sin(ang * 3.0 + v * 7.0 + 1.1);
                var col = Primitives.Mix(ReedSpatheOlive, ReedSpathePlum, patch);
                col = Primitives.Scale(col, 0.82 + 0.28 * v);
                m.AddVertex(p, dir, col, 1, v, u, 0, 0);
            }
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < sides; s++)
            {
                int a = start + r * (sides + 1) + s;
                int b = a + sides + 1;
                Primitives.TriangleFacing(m, a, b, a + 1, Vec3.Up);
                Primitives.TriangleFacing(m, b, b + 1, a + 1, Vec3.Up);
            }
        m.EssentialSpans.Add((start, m.VertexCount - start));
    }

    /// <summary>
    /// The ringreed: a horsetail's jointed skeleton wearing a skunk cabbage's leaves and hood,
    /// with a fern's fiddlehead where the spore cone used to be.
    /// </summary>
    private static void Ringreed(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var culmBaseCol = c1;
        var culmLightCol = Primitives.Mix(c1, c2, 0.40);
        var sheathCol = Primitives.Scale(c1, 0.28);
        var sheathTeethCol = Primitives.Mix(sheathCol, new[] { 0.08, 0.07, 0.06 }, 0.75);
        var branchCol = Primitives.Mix(c1, c2, 0.32);

        // 1. The crown of broad cabbage leaves, out of the wet ground at the base.
        int leaves = 7 + rng.NextInt(3);
        for (int i = 0; i < leaves; i++)
        {
            double ang = 2 * Math.PI * i / leaves + rng.Range(-0.22, 0.22);
            var radial = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
            // The oldest leaves lie out flat; the inner ones still stand.
            double age = i / (double)(leaves - 1);
            double lift = 0.48 - 0.30 * age;
            var along = (radial * Math.Sqrt(1.0 - lift * lift) + Vec3.Up * lift).Normalized();
            var attach = radial * rng.Range(0.015, 0.09) + Vec3.Up * 0.012;
            double len = rng.Range(0.40, 0.56) * (1.0 - 0.24 * age);
            ReedCabbageLeaf(m, attach, along, len, len * rng.Range(0.64, 0.82),
                len * rng.Range(0.24, 0.34), rng);
        }

        // 2. The thick, jointed stalks.
        int stalks = 5 + rng.NextInt(3);
        for (int k = 0; k < stalks; k++)
        {
            double angle = rng.Range(0, Math.PI * 2);
            double dist = rng.Range(0.02, 0.24);
            var foot = new Vec3(Math.Cos(angle) * dist, 0, Math.Sin(angle) * dist);
            var swayDir = new Vec3(Math.Cos(angle + rng.Range(-0.5, 0.5)), 0, Math.Sin(angle + rng.Range(-0.5, 0.5)));
            double height = rng.Range(0.68, 1.0);
            bool hooded = true;

            const int nodeCount = 7;
            var joints = new Vec3[nodeCount + 1];
            var radii = new double[nodeCount + 1];
            for (int j = 0; j <= nodeCount; j++)
            {
                double t = (double)j / nodeCount;
                joints[j] = foot + swayDir * (0.09 * t * t) + Vec3.Up * (height * t);
                // The stalks are thick: this is the cabbage parent's stock, not a grass's.
                radii[j] = 0.052 * (1.0 - t * 0.52);
            }

            // The stalk itself: ribbed, the way a horsetail's is, with the grooves cut into
            // the section so the flutes catch the light all the way up.
            ReedFlutedTube(m, joints, radii, 24, 8, 0.13,
                culmBaseCol, culmLightCol, Primitives.Scale(culmBaseCol, 0.52),
                (i, v) => (culmBaseCol, 1.0, i / (double)nodeCount, v, 0, 0));

            // 3. The dark ringed joints with black-toothed rims.
            for (int j = 1; j < nodeCount; j++)
            {
                var collar = joints[j];
                double r = radii[j];
                double sheathH = 0.030 * (1.0 - (double)j / nodeCount * 0.35);

                int sStart = m.VertexCount;
                int sSides = m.Lod?.Sides(10) ?? 10;
                int sheathStep = m.Lod == null ? 1 : m.Lod.LeafFraction < 0.08 ? 4 : m.Lod.LeafFraction < 0.30 ? 2 : 1;
                if (j % sheathStep == 0)
                {
                    for (int si = 0; si <= sSides; si++)
                    {
                        double th = 2 * Math.PI * si / sSides;
                        var nrm = new Vec3(Math.Cos(th), 0, Math.Sin(th));
                        var pBase = collar - Vec3.Up * (sheathH * 0.5) + nrm * (r * 1.02);
                        var pMid = collar + nrm * (r * 1.10);
                        bool isTooth = ((si % sSides) % 2 == 1);
                        var pTop = collar + Vec3.Up * (sheathH * (isTooth ? 0.70 : 0.42)) + nrm * (r * (isTooth ? 1.06 : 1.18));

                        m.AddVertex(pBase, nrm, sheathCol, 1.0, (double)si / sSides, 0.0, 0, 0);
                        m.AddVertex(pMid, nrm, sheathCol, 1.0, (double)si / sSides, 0.5, 0, 0);
                        m.AddVertex(pTop, nrm, isTooth ? sheathTeethCol : sheathCol, 1.0, (double)si / sSides, 1.0, 0, 0);
                    }
                    for (int si = 0; si < sSides; si++)
                    {
                        int a = sStart + si * 3;
                        int b = a + 3;
                        Primitives.TriangleFacing(m, a, b, a + 1, Vec3.Up);
                        Primitives.TriangleFacing(m, b, b + 1, a + 1, Vec3.Up);
                        Primitives.TriangleFacing(m, a + 1, b + 1, a + 2, Vec3.Up);
                        Primitives.TriangleFacing(m, b + 1, b + 2, a + 2, Vec3.Up);
                    }
                    m.EssentialSpans.Add((sStart, m.VertexCount - sStart));
                }

                // 4. Tiered whorls of drooping branchlets, on the lower nodes only, so the
                // crozier and the hood at the top stay clear.
                if (j >= 2 && j <= 4)
                {
                    int whorlCount = 7 + rng.NextInt(4);
                    double tierFraction = (double)(j - 2) / 2.0;
                    double reach = rng.Range(0.26, 0.38) * (1.1 - tierFraction * 0.45);
                    for (int b = 0; b < whorlCount; b++)
                    {
                        double bAng = b * (2 * Math.PI / whorlCount) + angle + j * 0.42 + rng.Range(-0.08, 0.08);
                        var radial = new Vec3(Math.Cos(bAng), 0, Math.Sin(bAng));
                        var p0 = collar - Vec3.Up * (sheathH * 0.42) + radial * (r * 1.04);
                        var p1 = p0 + radial * (reach * 0.48) + Vec3.Up * 0.018;
                        var p2 = p0 + radial * (reach * 0.85) - Vec3.Up * (reach * 0.22);
                        var p3 = p0 + radial * reach - Vec3.Up * (reach * 0.52);
                        Primitives.Tube(m, new[] { p0, p1, p2, p3 }, new[] { 0.0058, 0.0044, 0.0029, 0.0014 }, 4,
                            (i, v) => (branchCol, 1, i / 3.0, v, 0, 0));
                    }
                }
            }

            // 5. The crozier and the hood, at the top of the stalk.
            var tip = joints[nodeCount];
            var axis = (joints[nodeCount] - joints[nodeCount - 1]);
            if (axis.LengthSq < 1e-12) axis = Vec3.Up;
            axis = axis.Normalized();
            var lean = swayDir - axis * swayDir.Dot(axis);
            if (lean.LengthSq < 1e-10) lean = new Vec3(1, 0, 0);
            lean = lean.Normalized();

            if (hooded)
            {
                // The hood wraps the crozier and peels back at its rim.
                double hoodH = rng.Range(0.12, 0.16);
                ReedSpathe(m, tip - axis * 0.014, axis, lean, radii[nodeCount] * 0.90,
                    hoodH, rng, 6, 18);
                // The crozier emerges from the top joint, rises through the hood, and coils at the rim.
                ReedFiddlehead(m, tip, axis, lean,
                    rng.Range(0.036, 0.050), hoodH, rng);
            }
        }
    }
}
