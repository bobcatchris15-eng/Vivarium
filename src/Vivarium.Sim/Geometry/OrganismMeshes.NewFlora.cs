using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Shared builders for the 2026-10-21 new-flora set (Glasslily, Silverweed, Coalspike, Marrowbloom,
/// Blackneedle, Starbulb, Bonecup, Embercup, Hollowcrown, Tankbloom).
///
/// The house pattern for a new species is: author the plant in real metres, convert it into the engine's
/// unit frame, and keep every helper private to the species' own partial so two sessions cannot collide
/// on a member name. This file is the shared exception: it holds the parts every one of the ten species
/// needs, and every member in it is prefixed <c>NewFlora</c> so it cannot clash with an existing
/// generator. The conversion itself is shared too - <c>NewFloraAppendWorld</c> - so all ten species are
/// scaled the same way.
///
/// Conventions held here:
///   * lengths are metres; the caller converts at the end;
///   * every blade is a closed lofted section with one shared vertex grid, so a fleshy leaf shades as a
///     continuous surface instead of a stack of folded-paper quads;
///   * every blade is recorded with <see cref="MeshData.RecordLeaf"/>, so the LOD tierer can drop whole
///     leaves and never half of one;
///   * <see cref="MeshData.FloraDetailLevel"/> (0 full, 1 medium, 2 low) and
///     <see cref="MeshData.Lod"/> are both respected.
/// </summary>
public static partial class OrganismMeshes
{
    // ---------------------------------------------------------------------------------------------
    // Organ markers
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Marks a vertex range as an organ whose authored pigment the shader must keep.
    ///
    /// The vascular branch of the flora shader mixes every leaf towards the species' own green
    /// leaf pigment, because that is right for a leaf and wrong for a petal, a bract, a fungus cup
    /// or a glass spine. An organ that has to keep the colour it was authored in is flagged through
    /// UV2, and the shader then uses the vertex colour as written.
    ///
    /// Two bands are available:
    ///   * <paramref name="glass"/> false - UV2.x = 1.35. The pigment is used as authored. This band
    ///     is also the shader's marked-nodule band, so a species that asks for night emission will
    ///     light these organs as well.
    ///   * <paramref name="glass"/> true - UV2.x = 1.28. The pigment is used as authored AND picks
    ///     up the view-angle prismatic shift, which is what makes cut glass read as cut glass. This
    ///     band never glows.
    ///
    /// UV2.y must be 1: the override only applies to vertices the shader treats as leaf surface.
    /// </summary>
    private static void NewFloraMarkOrgan(MeshData m, int firstVertex, int count, bool glass = false)
    {
        float x = glass ? 1.28f : 1.35f;
        int last = Math.Min(m.VertexCount, firstVertex + count);
        for (int v = Math.Max(0, firstVertex); v < last; v++)
        {
            m.UV2[v * 2] = x;
            m.UV2[v * 2 + 1] = 1f;
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Frame conversion
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Converts a part authored in world metres into the engine's unit frame. The renderer scales
    /// instances by (radius, height, radius), so this inverse scale makes the plant appear at the size
    /// and proportion it was authored at. Normals need the inverse transpose of that scale.
    /// </summary>
    private static void NewFloraAppendWorld(MeshData target, MeshData part, double radius, double height)
    {
        double sx = 1.0 / Math.Max(0.01, radius);
        double sy = 1.0 / Math.Max(0.01, height);
        int baseVertex = target.VertexCount;
        int structuralBefore = target.Structural.Count;
        target.Append(part, p => new Vec3(p.X * sx, p.Y * sy, p.Z * sx));

        for (int v = baseVertex; v < target.VertexCount; v++)
        {
            var n = target.NormalAt(v);
            var scaled = new Vec3(n.X * radius, n.Y * height, n.Z * radius);
            double len = scaled.Length;
            if (len > 1e-9)
            {
                scaled = scaled / len;
                target.Normals[v * 3] = (float)scaled.X;
                target.Normals[v * 3 + 1] = (float)scaled.Y;
                target.Normals[v * 3 + 2] = (float)scaled.Z;
            }
        }

        // Tube radii are recorded in world metres; restate them in mesh units so the LOD tiers keep
        // classifying petioles as petioles rather than as structural wood.
        for (int i = structuralBefore; i < target.Structural.Count; i++)
        {
            var st = target.Structural[i];
            target.Structural[i] = (st.FirstVertex, st.VertexCount, st.Sides, st.Radius * sx);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Detail budget
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// Applies the render-only detail budget to a mesh the builder is about to author. Detail level 0 is
    /// the authored plant, 1 halves the tessellation, 2 halves it again. The parametric LOD tiers scale
    /// the same counts at runtime, so the two budgets compose.
    /// </summary>
    private static void NewFloraBudget(MeshData m, ref int rows, ref int sides, int minRows = 3, int minSides = 5)
    {
        int detail = m.FloraDetailLevel ?? 0;
        if (detail >= 1)
        {
            rows = Math.Max(minRows, rows - 2);
            sides = Math.Max(minSides, sides - 2);
        }
        if (detail >= 2)
        {
            rows = Math.Max(minRows, rows - 2);
            sides = Math.Max(minSides, sides - 2);
        }
        if (m.Lod is { } lod)
        {
            rows = Math.Max(2, lod.Segments(rows));
            sides = Math.Max(5, lod.Sides(sides));
        }
    }

    /// <summary>
    /// Scales a repeat count (leaf count, floret count, scale count) for the current detail level.
    /// </summary>
    private static int NewFloraCount(MeshData m, int full)
    {
        int detail = m.FloraDetailLevel ?? 0;
        int n = detail switch { 0 => full, 1 => Math.Max(2, (int)Math.Round(full * 0.7)), _ => Math.Max(2, (int)Math.Round(full * 0.45)) };
        if (m.Lod is { } lod) n = Math.Max(2, (int)Math.Round(n * lod.LeafFraction));
        return n;
    }

    // ---------------------------------------------------------------------------------------------
    // The blade
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A lofted organic blade: one shared vertex grid, closed section, smooth-shaded.
    ///
    /// The section is a superellipse whose fullness and edge sharpness are separate, so a blade can be
    /// a round fleshy lobe, a flat strap, or a keeled grass blade without a second builder. On top of the
    /// section sit the three modifiers that carry most of a plant's anatomy:
    ///
    ///   * <c>keel</c> - positive raises a ridge along the dorsal midline (a midrib standing proud),
    ///     negative sinks the midline into a trough with the keel underneath, which is how a grass blade
    ///     folds and how a bromeliad leaf makes its tank;
    ///   * <c>cup</c> - lifts both margins toward the dorsal side, so the blade becomes a spoon;
    ///   * <c>camber</c> - arcs the blade along its own length, and <c>tipCurl</c> rolls the last third.
    ///
    /// Returns the tip position, so a caller can hang the next organ off the end of the blade.
    /// </summary>
    private static Vec3 NewFloraBlade(
        MeshData m,
        Vec3 root,
        Vec3 along,
        Vec3 dorsal,
        double length,
        double halfWidth,
        Func<double, double> width,
        Func<double, double[]> colour,
        int rows = 7,
        int sides = 8,
        double flatness = 0.35,
        double keel = 0.0,
        double cup = 0.0,
        double camber = 0.0,
        double tipCurl = 0.0,
        double twist = 0.0,
        double bow = 0.0,
        double serration = 0.0,
        double teeth = 14.0,
        double[]? midrib = null,
        double midribWidth = 0.22,
        bool volumetric = false)
    {
        if (length <= 1e-7 || halfWidth <= 0) return root;

        int leafVertex = m.VertexCount, leafIndex = m.Indices.Count;
        NewFloraBudget(m, ref rows, ref sides, minRows: 3, minSides: 5);
        rows = Math.Max(2, rows);

        var a0 = along.LengthSq > 1e-12 ? along.Normalized() : Vec3.Up;
        var d0 = dorsal - a0 * dorsal.Dot(a0);
        if (d0.LengthSq < 1e-10) d0 = a0.Cross(new Vec3(1, 0, 0));
        if (d0.LengthSq < 1e-10) d0 = a0.Cross(new Vec3(0, 0, 1));
        d0 = d0.Normalized();
        var side0 = a0.Cross(d0).Normalized();

        Vec3 Centre(double t)
        {
            double c = camber * length * Math.Sin(Math.PI * Math.Min(1.0, Math.Max(0.0, t)));
            double b = bow * length * Math.Sin(Math.PI * t);
            double curl = tipCurl * length * Math.Pow(Math.Max(0.0, t - 0.68) / 0.32, 2.0);
            return root + a0 * (length * t) + d0 * (c + curl) + side0 * b;
        }

        var grid = new int[rows + 1, sides + 1];
        var normals = new Vec3[rows + 1, sides + 1];
        var side = side0;
        var prevTangent = a0;

        for (int i = 0; i <= rows; i++)
        {
            double t = i / (double)rows;
            var centre = Centre(t);

            // The local frame follows the blade as it bends: the tangent is taken from the centreline
            // and the side vector is parallel-transported from ring to ring, so a drooping blade does
            // not corkscrew its section.
            var tangent = i == 0 ? Centre(Math.Min(1.0, t + 0.02)) - centre
                       : i == rows ? centre - Centre(Math.Max(0.0, t - 0.02))
                       : Centre(t + 0.02) - Centre(t - 0.02);
            if (tangent.LengthSq < 1e-16) tangent = prevTangent;
            else tangent = tangent.Normalized();
            side = side - tangent * side.Dot(tangent);
            if (side.LengthSq < 1e-12) side = tangent.Cross(d0);
            if (side.LengthSq < 1e-12) side = tangent.Cross(new Vec3(1, 0, 0));
            side = side.Normalized();
            var up = side.Cross(tangent).Normalized();
            prevTangent = tangent;

            double w = halfWidth * Math.Max(0.0, width(t));
            var ringColour = colour(t);
            double tw = twist * t;
            var sideTw = side * Math.Cos(tw) + up * Math.Sin(tw);
            var upTw = up * Math.Cos(tw) - side * Math.Sin(tw);

            for (int j = 0; j <= sides; j++)
            {
                double th = 2 * Math.PI * j / sides;
                double cs = Math.Cos(th), sn = Math.Sin(th);
                // Superellipse: the exponent across controls fullness, the one through controls how
                // sharply the margin closes. A round fleshy lobe wants both near 1; a grass strap wants
                // a fat across and a sharp edge.
                double sx = Math.Sign(cs) * Math.Pow(Math.Abs(cs), 0.70);
                double sy = Math.Sign(sn) * Math.Pow(Math.Abs(sn), 1.55);

                // The margin carries the teeth; the midline never does.
                double margin = Math.Pow(Math.Abs(cs), 1.6);
                double tooth = serration > 1e-6 ? (t * teeth - Math.Floor(t * teeth)) : 0.0;

                double px = sx * w * (1.0 - serration * margin * tooth);
                double ridge = keel * Math.Pow(Math.Max(0.0, sn), 5.0);
                double lift = cup * Math.Pow(Math.Abs(cs), 2.0);
                double py = sy * w * flatness + (ridge + lift) * w;

                var p = centre + sideTw * px + upTw * py;

                var col = ringColour;
                if (midrib != null && sn > 0.0)
                {
                    double across = Math.Abs(cs);
                    float k = (float)(MathD.SmoothStep(midribWidth, 0.0, across) * Math.Min(1.0, sn * 1.6));
                    col = Primitives.Mix(col, midrib, k);
                }
                grid[i, j] = m.AddVertex(p, upTw, col, 1, t, j / (double)sides, 0, 1);
            }
        }

        for (int i = 0; i < rows; i++)
            for (int j = 0; j < sides; j++)
            {
                var a = m.Position(grid[i, j]);
                var b = m.Position(grid[i, j + 1]);
                var c = m.Position(grid[i + 1, j]);
                var d = m.Position(grid[i + 1, j + 1]);
                var n = (b - a).Cross(c - a);
                if (n.LengthSq < 1e-18) n = (d - a).Cross(c - a);
                if (n.LengthSq < 1e-18) continue;
                n = n.Normalized();
                normals[i, j] += n;
                normals[i, j + 1] += n;
                normals[i + 1, j] += n;
                normals[i + 1, j + 1] += n;
                Primitives.TriangleFacing(m, grid[i, j], grid[i, j + 1], grid[i + 1, j], n);
                Primitives.TriangleFacing(m, grid[i, j + 1], grid[i + 1, j + 1], grid[i + 1, j], n);
            }

        for (int i = 0; i <= rows; i++)
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

        // The base is closed with a small fan set inside the sheath, so the join never shows.
        var capCentre = Centre(0.0);
        var capColour = colour(0.0);
        int capIdx = m.AddVertex(capCentre, -a0, capColour, 1, 0.0, 0.5, 0, 1);
        for (int j = 0; j < sides; j++)
        {
            var n0 = (m.Position(grid[0, j + 1]) - capCentre).Cross(m.Position(grid[0, j]) - capCentre);
            if (n0.LengthSq < 1e-16) continue;
            n0 = n0.Normalized();
            int a = m.AddVertex(m.Position(grid[0, j]), n0, capColour, 1, 0.0, j / (double)sides, 0, 1);
            int b = m.AddVertex(m.Position(grid[0, j + 1]), n0, capColour, 1, 0.0, (j + 1) / (double)sides, 0, 1);
            Primitives.TriangleFacing(m, capIdx, a, b, n0);
        }

        // The tip is closed the same way, so a blade whose width profile does not reach zero cannot
        // leave a hole at its end. When the profile does reach zero the fan is a point and costs
        // almost nothing.
        var tipCentre = Centre(1.0);
        var tipColour = colour(1.0);
        int tipIdx = m.AddVertex(tipCentre, a0, tipColour, 1, 1.0, 0.5, 0, 1);
        for (int j = 0; j < sides; j++)
        {
            var n0 = (m.Position(grid[rows, j]) - tipCentre).Cross(m.Position(grid[rows, j + 1]) - tipCentre);
            if (n0.LengthSq < 1e-16) continue;
            n0 = n0.Normalized();
            int a = m.AddVertex(m.Position(grid[rows, j]), n0, tipColour, 1, 1.0, j / (double)sides, 0, 1);
            int b = m.AddVertex(m.Position(grid[rows, j + 1]), n0, tipColour, 1, 1.0, (j + 1) / (double)sides, 0, 1);
            Primitives.TriangleFacing(m, tipIdx, b, a, n0);
        }

        m.RecordLeaf(leafVertex, leafIndex, root, length, volumetric);
        return Centre(1.0);
    }

    // ---------------------------------------------------------------------------------------------
    // The petal
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A petal: a cupped, double-sided surface with a raised vein network.
    ///
    /// A petal is not a blade. It is a broad, shallow spoon, it is thin enough to be lit from
    /// behind, and its veins fan out from the base rather than running down a midrib. The colour
    /// callback therefore receives the position along the petal, the position across it, and the
    /// vein mask, so a species can bake its own leading into the pigment.
    /// </summary>
    private static Vec3 NewFloraPetal(
        MeshData m,
        Vec3 root,
        Vec3 along,
        Vec3 dorsal,
        double length,
        double halfWidth,
        Func<double, double> width,
        Func<double, double, double, double[]> colour,
        int rows = 7,
        int across = 5,
        double cup = 0.55,
        double camber = 0.0,
        double tipCurl = 0.0,
        double veinCount = 0.0,
        double veinDepth = 0.0,
        double veinSharp = 12.0,
        Func<double, double, double, double[]>? backColour = null)
    {
        if (length <= 1e-7 || halfWidth <= 0) return root;

        int leafVertex = m.VertexCount, leafIndex = m.Indices.Count;
        NewFloraBudget(m, ref rows, ref across, minRows: 3, minSides: 3);
        rows = Math.Max(2, rows);
        across = Math.Max(2, across);

        var a0 = along.LengthSq > 1e-12 ? along.Normalized() : Vec3.Up;
        var d0 = dorsal - a0 * dorsal.Dot(a0);
        if (d0.LengthSq < 1e-10) d0 = a0.Cross(new Vec3(1, 0, 0));
        if (d0.LengthSq < 1e-10) d0 = a0.Cross(new Vec3(0, 0, 1));
        d0 = d0.Normalized();
        var side0 = a0.Cross(d0).Normalized();

        Vec3 Centre(double t)
        {
            double c = camber * length * Math.Sin(Math.PI * Math.Min(1.0, Math.Max(0.0, t)));
            double curl = tipCurl * length * Math.Pow(Math.Max(0.0, t - 0.70) / 0.30, 2.0);
            return root + a0 * (length * t) + d0 * (c + curl);
        }

        // The vein mask fans out from the base: the veins are furthest apart at the tip.
        double VeinMask(double t, double x)
        {
            if (veinCount < 0.5) return 0.0;
            double phase = Math.Cos(veinCount * Math.PI * x);
            return Math.Pow(Math.Max(0.0, phase), veinSharp) * Math.Min(1.0, 0.35 + 0.85 * t);
        }

        // The petal is a thin sheet, so it is built as two coincident shells with opposite
        // winding, each with its own smooth normals - the way the engine's curved leaf does it.
        // The shader tells the two apart by which side is facing the viewer, so the back of a
        // petal is pigmented as a back and not as a second front.
        var frontGrid = new int[rows + 1, across + 1];
        var backGrid = new int[rows + 1, across + 1];
        var normals = new Vec3[rows + 1, across + 1];

        for (int face = 0; face < 2; face++)
        {
            var faceN = face == 0 ? d0 : -d0;
            for (int i = 0; i <= rows; i++)
            {
                double t = i / (double)rows;
                var centre = Centre(t);
                double w = halfWidth * Math.Max(0.0, width(t));
                for (int j = 0; j <= across; j++)
                {
                    double x = j / (double)across * 2.0 - 1.0;
                    double vein = VeinMask(t, x);
                    // The spoon: the margins lift toward the dorsal side, and the veins stand proud
                    // on the front face.
                    double lift = cup * w * x * x + veinDepth * w * vein;
                    var p = centre + side0 * (x * w) + d0 * lift;
                    // The back of a blade can be a different material from the front - a thistle's
                    // is woolly white, a lily pad's is wine-coloured - so the two shells may carry
                    // their own pigment.
                    var col = face == 1 && backColour != null ? backColour(t, x, vein) : colour(t, x, vein);
                    int v = m.AddVertex(p, faceN, col, 1, t, (x + 1) * 0.5, 0, 1);
                    if (face == 0) frontGrid[i, j] = v; else backGrid[i, j] = v;
                }
            }
            for (int i = 0; i < rows; i++)
                for (int j = 0; j < across; j++)
                {
                    int a = frontGrid[i, j], b = frontGrid[i, j + 1], c = frontGrid[i + 1, j], d = frontGrid[i + 1, j + 1];
                    var n = (m.Position(b) - m.Position(a)).Cross(m.Position(c) - m.Position(a));
                    if (n.LengthSq < 1e-18) n = (m.Position(d) - m.Position(a)).Cross(m.Position(c) - m.Position(a));
                    if (n.LengthSq < 1e-18) continue;
                    n = n.Normalized();
                    if (n.Dot(faceN) < 0) n = -n;
                    if (face == 0)
                    {
                        normals[i, j] += n;
                        normals[i, j + 1] += n;
                        normals[i + 1, j] += n;
                        normals[i + 1, j + 1] += n;
                    }
                    Primitives.TriangleFacing(m, a, b, c, n);
                    Primitives.TriangleFacing(m, b, d, c, n);
                }
        }

        // The smoothed normals go on the front shell; the back shell gets them mirrored, so the
        // back of the petal is lit as the back of the petal.
        for (int i = 0; i <= rows; i++)
            for (int j = 0; j <= across; j++)
            {
                var n = normals[i, j];
                if (n.LengthSq < 1e-18) n = d0;
                n = n.Normalized();
                int fv = frontGrid[i, j], bv = backGrid[i, j];
                m.Normals[fv * 3] = (float)n.X;
                m.Normals[fv * 3 + 1] = (float)n.Y;
                m.Normals[fv * 3 + 2] = (float)n.Z;
                m.Normals[bv * 3] = (float)-n.X;
                m.Normals[bv * 3 + 1] = (float)-n.Y;
                m.Normals[bv * 3 + 2] = (float)-n.Z;
            }

        m.RecordLeaf(leafVertex, leafIndex, root, length);
        return Centre(1.0);
    }

    // ---------------------------------------------------------------------------------------------
    // Stalks, filaments and scale columns
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A tapering stalk that arcs from a base to a tip. Petioles, scapes and pedicels are all this shape:
    /// a tube that leaves the plant, bows, and holds its end up. Returns the tip.
    /// </summary>
    private static Vec3 NewFloraStalk(MeshData m, Vec3 root, Vec3 tip, Vec3 bulge, double bulgeAmount,
        double baseRadius, double tipRadius, double[] rootCol, double[] tipCol,
        int segments = 6, int sides = 6, double taper = 1.0)
    {
        var path = new Vec3[segments + 1];
        var radii = new double[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            double t = i / (double)segments;
            path[i] = Vec3.Lerp(root, tip, t) + bulge * (bulgeAmount * Math.Sin(Math.PI * t));
            radii[i] = baseRadius + (tipRadius - baseRadius) * Math.Pow(t, taper);
        }
        Primitives.Tube(m, path, radii, sides,
            (i, v) => (Primitives.Mix(rootCol, tipCol, (double)i / segments), 1, (double)i / segments, v, 0, 0));
        return tip;
    }

    /// <summary>
    /// A fine tapered filament: a hair, a thread, a spine. It is a tube rather than a ribbon so it still
    /// catches light from every side, which matters for the glass needles.
    /// </summary>
    private static void NewFloraFilament(MeshData m, Vec3 from, Vec3 to, double radius, double[] col, int sides = 4)
    {
        var axis = to - from;
        if (axis.LengthSq < 1e-14) return;
        axis = axis.Normalized();
        var mid = Vec3.Lerp(from, to, 0.5) + axis * (radius * 0.6);
        Primitives.Tube(m, new[] { from, mid, to }, new[] { radius, radius * 0.62, radius * 0.08 }, sides,
            (i, v) => (col, 1, i * 0.5, v, 0, 0));
    }

    /// <summary>
    /// A column of overlapping scales wound around an axis: the armour of a cattail spike, the involucre
    /// of a thistle head, the bracts of a bromeliad spike. Each scale is a small lanceolate plate that
    /// leaves the axis pointing outward and up, so the column reads as shingled rather than as a texture.
    ///
    /// The scales spiral, as they do on a real spike, and the golden angle keeps the seams from lining up.
    /// </summary>
    private static void NewFloraScaleColumn(MeshData m, Vec3 basePoint, Vec3 axis, double length, double radius,
        int whorls, int perWhorl, double scaleLength, double scaleWidth,
        Func<double, double, double[]> colour, Rng rng, double droop = 0.6, double spiralTurns = 1.0, bool organ = true, bool glass = false)
    {
        var up = axis.Normalized();
        var side = up.Cross(new Vec3(1, 0, 0));
        if (side.LengthSq < 1e-10) side = up.Cross(new Vec3(0, 0, 1));
        side = side.Normalized();
        var forward = side.Cross(up).Normalized();

        for (int w = 0; w < whorls; w++)
        {
            double wt = (w + 0.5) / whorls;
            var at = basePoint + up * (length * wt);
            // The column swells through the middle and closes at the tip.
            double r = radius * (0.72 + 0.38 * Math.Sin(Math.PI * Math.Min(1.0, wt * 1.04)));
            for (int k = 0; k < perWhorl; k++)
            {
                double a = 2 * Math.PI * k / perWhorl + w * 2 * Math.PI * spiralTurns / whorls;
                var outward = (side * Math.Cos(a) + forward * Math.Sin(a)).Normalized();
                var root = at + outward * (r * 0.55);
                // Each scale lifts a little as it goes and then falls away, shingling over the one below.
                var dir = (outward + up * (0.55 - droop * 0.35)).Normalized();
                int mark = m.VertexCount;
                NewFloraBlade(m, root, dir, up, scaleLength * rng.Range(0.82, 1.18),
                    scaleWidth * rng.Range(0.85, 1.15),
                    t => 1.0 - 0.72 * Math.Pow(t, 1.6),
                    t => colour(wt, t),
                    rows: 3, sides: 5, flatness: 0.30, keel: 0.16, camber: 0.10, volumetric: true);
                // A scale is armour, not foliage: it keeps the pigment it was authored in.
                if (organ) NewFloraMarkOrgan(m, mark, m.VertexCount - mark, glass);
            }
        }
    }
}
