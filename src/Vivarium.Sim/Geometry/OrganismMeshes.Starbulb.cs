using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Starbulb - the bulb plant of dry open ground.
/// Analogue: Allium, the ornamental onion.
///
/// The plant is authored in REAL METRES and converted into the engine's unit frame by
/// <see cref="NewFloraAppendWorld"/>, so what is built here is what the player sees: a 0.62 m
/// plant, most of which is the leafless scape and the globe it carries.
///
/// Anatomy follows Allium rather than any generic bulb flower:
///
///   * a TUNICATED BULB, half sunk in the grit: five overlapping papery tunics and a sixth that
///     has split down its side and is peeling away from the shoulder of the bulb;
///   * two or three BULBLETS at the foot of the main bulb, each with its own small tunics;
///   * a ROSETTE of glaucous strap leaves out of the bulb's neck, each keeled and channelled with
///     a pale midrib, sheathing the neck at its foot and arching over to droop at the tip;
///   * a LEAFLESS SCAPE, smooth and tapering, that rises clear of the rosette and bends over
///     near the top;
///   * an UMBEL that is a true GLOBE rather than a flat disc: fifty-odd six-tepalled star flowers
///     on pedicels of graded length, so the inflorescence closes into a ball;
///   * a few CLOSED BUDS still held in the middle of the globe, where the youngest flowers are.
///
/// The fantasy layer is colour, and only colour. The tepals are the colour of COLD BLUE GLASS,
/// and the tunic is a dull gold where a real bulb would be papery brown. Nothing else is invented:
/// the bulb, the rosette, the scape and the globe grow exactly as an Allium grows them, so the
/// eye reads a real plant first and a strange one second.
/// </summary>
public static partial class OrganismMeshes
{
    // ---------------------------------------------------------------------------------------------
    // Palette
    // ---------------------------------------------------------------------------------------------

    /// <summary>The tunic's dull gold. A real Allium tunic is papery brown; this one is not.</summary>
    private static readonly double[] StarbulbTunicGold = { 0.60, 0.46, 0.20 };
    /// <summary>The shaded, grit-stained foot of a tunic, where the paper has lain in the soil.</summary>
    private static readonly double[] StarbulbTunicShade = { 0.34, 0.25, 0.10 };
    /// <summary>The pale, dry, fibrous rim where a tunic has torn.</summary>
    private static readonly double[] StarbulbTunicRim = { 0.78, 0.68, 0.40 };
    /// <summary>The pale flesh of the bulb under its tunics, and of the neck above them.</summary>
    private static readonly double[] StarbulbFlesh = { 0.76, 0.67, 0.44 };
    /// <summary>The papery remains of last year's leaf bases, dried at the bulb's foot.</summary>
    private static readonly double[] StarbulbDeadSheath = { 0.46, 0.35, 0.16 };
    /// <summary>The glaucous blue-green of a healthy Allium leaf.</summary>
    private static readonly double[] StarbulbGlaucous = { 0.32, 0.42, 0.35 };
    /// <summary>The paler, greyer green of the leaf tip, where the wax bloom is thickest.</summary>
    private static readonly double[] StarbulbGlaucousPale = { 0.48, 0.56, 0.47 };
    /// <summary>The pale midrib stripe that runs down the middle of a strap leaf.</summary>
    private static readonly double[] StarbulbMidrib = { 0.62, 0.68, 0.57 };
    /// <summary>The waxy, glaucous green of the scape.</summary>
    private static readonly double[] StarbulbScapeCol = { 0.40, 0.51, 0.39 };
    /// <summary>The tepal's cold blue glass, deepest where it leaves the ovary.</summary>
    private static readonly double[] StarbulbTepalBase = { 0.48, 0.65, 0.86 };
    /// <summary>The tepal's tip: almost white, but still cold.</summary>
    private static readonly double[] StarbulbTepalTip = { 0.90, 0.95, 1.00 };
    /// <summary>The darker midvein running down the middle of each tepal.</summary>
    private static readonly double[] StarbulbTepalVein = { 0.18, 0.36, 0.66 };
    /// <summary>The anthers: a pale, cold gold.</summary>
    private static readonly double[] StarbulbAnther = { 0.93, 0.88, 0.62 };
    /// <summary>The green ovary in the middle of a star.</summary>
    private static readonly double[] StarbulbOvary = { 0.58, 0.68, 0.46 };
    /// <summary>A closed bud: the tepals are still folded, so the blue has not come out yet.</summary>
    private static readonly double[] StarbulbBudCol = { 0.46, 0.62, 0.68 };
    /// <summary>The papery spathe that splits and falls from under the umbel.</summary>
    private static readonly double[] StarbulbSpathe = { 0.85, 0.80, 0.60 };

    /// <summary>
    /// One papery tunic: a curved plate that wraps the bulb, with a torn upper edge, the vertical
    /// fibre folds of a real tunic, and an optional peel where the plate has split away from the
    /// shoulder and curled outward.
    /// </summary>
    private readonly record struct StarbulbTunicSpec(
        Vec3 Origin,
        double Radius,
        double CoreBase,
        double CoreTop,
        double TunicBase,
        double TunicTop,
        double Azimuth,
        double Span,
        double Thickness,
        double Gap,
        int Ribs,
        double Peel,
        int Layer);

    // ---------------------------------------------------------------------------------------------
    // The bulb
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The radius of the bulb's flesh at a given height. A tunicated Allium bulb is not a sphere:
    /// it swells quickly off a narrow flat basal plate, is widest low down, and then draws in to a
    /// short neck at the top - a squat ovoid, not a ball and not a cone.
    /// </summary>
    private static double StarbulbBulbRadius(double y, double radius, double baseY, double topY)
    {
        double t = Math.Clamp((y - baseY) / Math.Max(1e-6, topY - baseY), 0.0, 1.0);
        if (t < 0.42) return radius * (0.32 + 0.68 * MathD.SmoothStep(0.0, 0.42, t));
        // The shoulder: an ellipse that is stopped short of zero, so the bulb ends in a neck stub
        // rather than in a point.
        double u = (t - 0.42) / 0.58;
        return radius * Math.Sqrt(Math.Max(0.0, 1.0 - u * u * 0.85));
    }

    /// <summary>
    /// The radius of a tunic's outer face at a given height. Below the shoulder the tunic is
    /// pressed flat against the flesh; above it the tunic is a loose bag whose mouth stands a
    /// little away from the neck, which is what lets the eye see the pale flesh inside.
    /// </summary>
    private static double StarbulbTunicRadius(StarbulbTunicSpec spec, double y)
    {
        double core = StarbulbBulbRadius(y, spec.Radius, spec.CoreBase, spec.CoreTop);
        double t = Math.Clamp((y - spec.TunicBase) / Math.Max(1e-6, spec.TunicTop - spec.TunicBase), 0.0, 1.0);
        double k = MathD.SmoothStep(0.66, 0.95, t);
        double mouth = spec.Radius * 0.66;
        // The layer term is what makes a stack of tunics read as a stack. Every plate stands one
        // paper thickness outside the one under it, so the free edge of each one is a visible
        // step instead of a seam buried in the same surface. The step dies away towards the
        // mouth, because up there the plates are pressed together around the neck.
        double layer = spec.Layer * spec.Thickness * (1.0 - 0.75 * k);
        return core * (1.0 - k) + Math.Max(mouth, core) * k + spec.Gap + layer;
    }

    /// <summary>
    /// The bulb's flesh: the pale, ribbed core that the tunics are wrapped around. It is mostly
    /// hidden, so it is modelled cheaply, but it still has to carry the longitudinal ribs that
    /// show in the gaps between the tunics.
    /// </summary>
    private static void StarbulbBulbCore(MeshData m, Vec3 origin, double radius, double baseY, double topY,
        int rows, int sides, Rng rng)
    {
        NewFloraBudget(m, ref rows, ref sides, minRows: 4, minSides: 6);
        double phase = rng.Range(0, Math.PI * 2);
        var ids = new int[rows + 1, sides + 1];
        for (int i = 0; i <= rows; i++)
        {
            double t = i / (double)rows;
            double y = baseY + (topY - baseY) * t;
            for (int j = 0; j <= sides; j++)
            {
                double th = 2 * Math.PI * j / sides;
                // Six shallow ribs, the faint vertical seams the flesh shows between the tunics.
                double r = StarbulbBulbRadius(y, radius, baseY, topY)
                    * (1.0 + 0.022 * Math.Cos(6.0 * th + phase));
                var p = new Vec3(origin.X + Math.Cos(th) * r, y, origin.Z + Math.Sin(th) * r);
                var n = new Vec3(Math.Cos(th), 0.15, Math.Sin(th)).Normalized();
                // The flesh is cream where the light reaches it and greener down at the basal
                // plate, where it is still sitting in the ground.
                var col = Primitives.Mix(StarbulbFlesh, new[] { 0.72, 0.72, 0.50 }, 0.25 * (1.0 - t));
                ids[i, j] = m.AddVertex(p, n, col, 1, t, j / (double)sides, 0, 0);
            }
        }
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < sides; j++)
            {
                int a = ids[i, j], b = ids[i, j + 1], c = ids[i + 1, j], d = ids[i + 1, j + 1];
                var p0 = m.Position(a);
                var n = (m.Position(b) - p0).Cross(m.Position(c) - p0);
                if (n.LengthSq < 1e-20) n = (m.Position(d) - p0).Cross(m.Position(c) - p0);
                if (n.LengthSq < 1e-20) continue;
                n = n.Normalized();
                Primitives.TriangleFacing(m, a, b, c, n);
                Primitives.TriangleFacing(m, b, d, c, n);
            }
        // The basal plate: a flat disc, because a bulb really does stand on one.
        var plate = new Vec3(origin.X, baseY, origin.Z);
        int centre = m.AddVertex(plate, -Vec3.Up, StarbulbFlesh, 1, 0, 0.5, 0, 0);
        for (int j = 0; j < sides; j++)
        {
            int a = ids[0, j], b = ids[0, j + 1];
            Primitives.TriangleFacing(m, centre, b, a, -Vec3.Up);
        }
        // The shoulder is closed too, so the neck sits on a solid bulb instead of on a shell.
        int crown = m.AddVertex(new Vec3(origin.X, topY, origin.Z), Vec3.Up, StarbulbFlesh, 1, 1, 0.5, 0, 0);
        for (int j = 0; j < sides; j++)
        {
            int a = ids[rows, j], b = ids[rows, j + 1];
            Primitives.TriangleFacing(m, crown, a, b, Vec3.Up);
        }
    }

    /// <summary>
    /// One papery tunic, built as a shell rather than a card: an outer face, an inner face a paper
    /// thickness behind it, and the torn rim stitched between them. A single sheet would read as
    /// cardboard; the shell gives the rim a visible edge, which is the whole point of a tunic.
    /// </summary>
    private static void StarbulbTunicPlate(MeshData m, StarbulbTunicSpec spec, Rng rng, int rows, int cols)
    {
        NewFloraBudget(m, ref rows, ref cols, minRows: 4, minSides: 5);
        rows = Math.Max(3, rows);
        cols = Math.Max(4, cols);

        // The torn edge. A tunic does not end in a clean ring: it tears into a few big lobes with
        // a finer ragged edge between them, and those lobes are what the eye reads as "papery".
        var tear = new double[cols + 1];
        double phase = rng.Range(0, Math.PI * 2);
        double height = spec.TunicTop - spec.TunicBase;
        for (int j = 0; j <= cols; j++)
        {
            double u = j / (double)cols;
            double lobe = 0.5 + 0.5 * Math.Sin(u * Math.PI * 3.3 + phase);
            double fine = 0.5 + 0.5 * Math.Sin(u * Math.PI * 13.0 + phase * 2.1);
            tear[j] = (0.17 * lobe + 0.07 * fine) * height;
        }

        // Per-column fibre value. A tunic is a bundle of vertical fibres and the light breaks
        // differently on each one; without this the plate reads as painted card.
        var fibre = new double[cols + 1];
        for (int j = 0; j <= cols; j++) fibre[j] = rng.Range(0.86, 1.12);

        var outer = new int[rows + 1, cols + 1];
        var inner = new int[rows + 1, cols + 1];
        var outerN = new Vec3[rows + 1, cols + 1];
        var innerN = new Vec3[rows + 1, cols + 1];

        for (int i = 0; i <= rows; i++)
        {
            double v = i / (double)rows;
            for (int j = 0; j <= cols; j++)
            {
                double u = j / (double)cols;
                double az = spec.Azimuth + (u - 0.5) * spec.Span;
                double topY = spec.TunicTop - tear[j];
                double y = spec.TunicBase + (topY - spec.TunicBase) * v;
                double r = StarbulbTunicRadius(spec, y);
                // The vertical folds: a tunic is made of fibres that bulge a little as they run up.
                r *= 1.0 + 0.020 * Math.Sin(u * spec.Ribs * Math.PI * 2.0 + spec.Azimuth * 3.0);
                // The peel: where the plate has split, its edge lifts off the bulb and curls away.
                if (spec.Peel > 1e-6)
                    r += spec.Peel * Math.Exp(-Math.Pow(u / 0.28, 2.0)) * v;
                else if (spec.Peel < -1e-6)
                    r += -spec.Peel * Math.Exp(-Math.Pow((1.0 - u) / 0.28, 2.0)) * v;

                var radial = new Vec3(Math.Cos(az), 0, Math.Sin(az));
                var p = new Vec3(spec.Origin.X + radial.X * r, y, spec.Origin.Z + radial.Z * r);
                // The colour: gold, dirtier at the foot, paler and drier at the torn rim.
                var col = Primitives.Mix(StarbulbTunicGold, StarbulbTunicShade, Math.Pow(1.0 - v, 2.0));
                col = Primitives.Mix(col, StarbulbTunicRim, MathD.SmoothStep(0.80, 1.0, v));
                col = Primitives.Scale(col, fibre[j]);
                outer[i, j] = m.AddVertex(p, radial, col, 1, v, u, 0, 0);

                // The inner face is the pale flesh side of the paper, sitting a thickness behind.
                double ri = r - spec.Thickness;
                var pi = new Vec3(spec.Origin.X + radial.X * ri, y, spec.Origin.Z + radial.Z * ri);
                inner[i, j] = m.AddVertex(pi, -radial, Primitives.Mix(StarbulbFlesh, col, 0.25), 1, v, u, 0, 0);
            }
        }

        void Quad(int[,] sheet, Vec3[,] acc, int i, int j, Vec3 refDir)
        {
            int a = sheet[i, j], b = sheet[i, j + 1], c = sheet[i + 1, j], d = sheet[i + 1, j + 1];
            var p0 = m.Position(a);
            var n = (m.Position(b) - p0).Cross(m.Position(c) - p0);
            if (n.LengthSq < 1e-20) n = (m.Position(d) - p0).Cross(m.Position(c) - p0);
            if (n.LengthSq < 1e-20) return;
            n = n.Normalized();
            if (n.Dot(refDir) < 0) n = -n;
            acc[i, j] += n; acc[i, j + 1] += n; acc[i + 1, j] += n; acc[i + 1, j + 1] += n;
            Primitives.TriangleFacing(m, a, b, c, refDir);
            Primitives.TriangleFacing(m, b, d, c, refDir);
        }

        for (int i = 0; i < rows; i++)
            for (int j = 0; j < cols; j++)
            {
                double uMid = (j + 0.5) / cols;
                double azMid = spec.Azimuth + (uMid - 0.5) * spec.Span;
                var outDir = new Vec3(Math.Cos(azMid), 0, Math.Sin(azMid));
                Quad(outer, outerN, i, j, outDir);
                Quad(inner, innerN, i, j, -outDir);
            }

        // The torn rim. It faces up and slightly out, which is what gives the edge of a torn
        // tunic its bright line of light.
        for (int j = 0; j < cols; j++)
        {
            int a = outer[rows, j], b = outer[rows, j + 1], c = inner[rows, j + 1], d = inner[rows, j];
            Primitives.TriangleFacing(m, a, b, c, Vec3.Up);
            Primitives.TriangleFacing(m, a, c, d, Vec3.Up);
            outerN[rows, j] += Vec3.Up * 0.7; outerN[rows, j + 1] += Vec3.Up * 0.7;
            innerN[rows, j] += Vec3.Up * 0.3; innerN[rows, j + 1] += Vec3.Up * 0.3;
        }
        // The two side edges of the plate.
        for (int i = 0; i < rows; i++)
        {
            int a = outer[i, 0], b = outer[i + 1, 0], c = inner[i + 1, 0], d = inner[i, 0];
            var sideDir = new Vec3(Math.Cos(spec.Azimuth - spec.Span * 0.5), 0, Math.Sin(spec.Azimuth - spec.Span * 0.5));
            Primitives.TriangleFacing(m, a, b, c, sideDir);
            Primitives.TriangleFacing(m, a, c, d, sideDir);
            outerN[i, 0] += sideDir * 0.5; outerN[i + 1, 0] += sideDir * 0.5;
            innerN[i, 0] += sideDir * 0.3; innerN[i + 1, 0] += sideDir * 0.3;

            int a2 = outer[i, cols], b2 = outer[i + 1, cols], c2 = inner[i + 1, cols], d2 = inner[i, cols];
            var sideDir2 = new Vec3(Math.Cos(spec.Azimuth + spec.Span * 0.5), 0, Math.Sin(spec.Azimuth + spec.Span * 0.5));
            Primitives.TriangleFacing(m, a2, b2, c2, sideDir2);
            Primitives.TriangleFacing(m, a2, c2, d2, sideDir2);
            outerN[i, cols] += sideDir2 * 0.5; outerN[i + 1, cols] += sideDir2 * 0.5;
            innerN[i, cols] += sideDir2 * 0.3; innerN[i + 1, cols] += sideDir2 * 0.3;
        }

        for (int i = 0; i <= rows; i++)
            for (int j = 0; j <= cols; j++)
            {
                var n = outerN[i, j];
                if (n.LengthSq < 1e-18) n = Vec3.Up;
                n = n.Normalized();
                int v = outer[i, j];
                m.Normals[v * 3] = (float)n.X; m.Normals[v * 3 + 1] = (float)n.Y; m.Normals[v * 3 + 2] = (float)n.Z;
                var ni = innerN[i, j];
                if (ni.LengthSq < 1e-18) ni = Vec3.Down;
                ni = ni.Normalized();
                int w = inner[i, j];
                m.Normals[w * 3] = (float)ni.X; m.Normals[w * 3 + 1] = (float)ni.Y; m.Normals[w * 3 + 2] = (float)ni.Z;
            }
    }

    /// <summary>
    /// A small offset bulb at the foot of the main one. A real Allium pushes its offsets out of
    /// the basal plate, and each offset is a miniature of its mother: a ribbed core inside two
    /// small tunics.
    /// </summary>
    private static void StarbulbBulblet(MeshData m, Vec3 origin, double radius, double baseY, double topY,
        Rng rng, int rows, int cols)
    {
        StarbulbBulbCore(m, origin, radius, baseY, topY, rows, cols, rng);
        for (int k = 0; k < 2; k++)
        {
            double az = rng.Range(0, Math.PI * 2);
            var spec = new StarbulbTunicSpec(
                Origin: origin,
                Radius: radius,
                CoreBase: baseY,
                CoreTop: topY,
                TunicBase: baseY + 0.0015,
                TunicTop: topY + 0.0025,
                Azimuth: az,
                Span: Math.PI * 1.25,
                Thickness: 0.0007,
                Gap: 0.0005,
                Ribs: 2,
                Peel: 0.0,
                Layer: k);
            StarbulbTunicPlate(m, spec, rng, 4, 6);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The rosette
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The width profile of a strap leaf. An Allium leaf is a flat strap: it swells where it
    /// leaves the sheath, holds its width for most of its length, and then tapers away to a point.
    /// </summary>
    private static double StarbulbStrapWidth(double t)
    {
        double swell = 0.62 + 0.38 * MathD.SmoothStep(0.0, 0.16, t);
        // The strap holds its width well past the middle before it tapers, or the leaf reads as
        // a wire with a tuft at one end instead of a ribbon of leaf.
        double taper = Math.Max(0.04, 1.0 - Math.Pow(Math.Max(0.0, (t - 0.45) / 0.55), 2.2));
        return swell * taper;
    }

    /// <summary>
    /// The width profile of a tepal: a narrow claw leaving the ovary, widest just past the base,
    /// then a long lanceolate taper to a point.
    /// </summary>
    private static double StarbulbLanceWidth(double t)
    {
        double foot = 0.20 + 0.80 * MathD.SmoothStep(0.0, 0.16, t);
        double taper = Math.Max(0.02, 1.0 - Math.Pow(Math.Max(0.0, (t - 0.30) / 0.70), 1.7));
        return foot * taper;
    }

    // ---------------------------------------------------------------------------------------------
    // The scape
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The scape: a smooth, tapering, leafless stalk that rises clear of the rosette and bends
    /// over near the top. The bend is built as a real arc rather than a lean, because the whole
    /// silhouette of the plant depends on the scape leaving vertical and going over in one smooth
    /// curve; a tilted straight stalk reads as a stick.
    /// </summary>
    private static Vec3 StarbulbScape(MeshData m, Vec3 basePoint, double length, double azimuth,
        double bendAngle, double[] rootCol, double[] tipCol, int segments, int sides)
    {
        NewFloraBudget(m, ref segments, ref sides, minRows: 4, minSides: 6);
        segments = Math.Max(5, segments);

        var lean = new Vec3(Math.Cos(azimuth), 0, Math.Sin(azimuth));
        double straight = length * 0.66;
        double arc = length - straight;

        var path = new Vec3[segments + 1];
        var radii = new double[segments + 1];
        int straightRows = Math.Max(2, (int)Math.Round(segments * 0.66));
        var p = basePoint;
        for (int i = 0; i <= segments; i++)
        {
            double t = i / (double)segments;
            if (i <= straightRows)
            {
                // The straight rise. It drifts a hair off vertical so the scape is not a mast.
                double drift = 0.010 * length * t * t;
                p = basePoint + Vec3.Up * (straight * (i / (double)straightRows))
                    + lean * (drift * (i / (double)straightRows));
            }
            else
            {
                // The bend: the tangent rolls from vertical towards the lean direction.
                double s = (i - straightRows) / (double)(segments - straightRows);
                double ang = bendAngle * s;
                var tangent = (Vec3.Up * Math.Cos(ang) + lean * Math.Sin(ang)).Normalized();
                p = p + tangent * (arc / (segments - straightRows));
            }
            path[i] = p;
            // A scape swells where it leaves the bulb and then tapers evenly to the umbel.
            radii[i] = 0.0036 * (1.0 - 0.66 * Math.Pow(t, 1.05)) * (1.0 + 0.30 * Math.Exp(-t * 14.0));
        }
        Primitives.Tube(m, path, radii, sides,
            (i, v) => (Primitives.Mix(rootCol, tipCol, (double)i / segments), 1, (double)i / segments, v, 0, 0));
        return path[segments];
    }

    // ---------------------------------------------------------------------------------------------
    // The umbel
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// One anther: a tiny faceted grain of pale gold. It is a grain and not a modelled anther
    /// because at a third of a millimetre it is a fleck, and a fleck costs eight triangles where a
    /// smooth ellipsoid would spend two dozen on its own degenerate poles.
    /// </summary>
    private static void StarbulbAntherGrain(MeshData m, Vec3 centre, Vec3 axis, double size, double[] colour)
    {
        var n = axis.Normalized();
        var t1 = n.Cross(new Vec3(1, 0, 0));
        if (t1.LengthSq < 1e-8) t1 = n.Cross(new Vec3(0, 0, 1));
        t1 = t1.Normalized();
        var t2 = n.Cross(t1).Normalized();
        int apex = m.AddVertex(centre + n * size, n, colour, 1, 0, 0.5, 0, 0);
        int foot = m.AddVertex(centre - n * (size * 0.85), -n, colour, 1, 1, 0.5, 0, 0);
        var girdle = new int[4];
        for (int i = 0; i < 4; i++)
        {
            double a = i * Math.PI * 0.5;
            var p = centre + t1 * (Math.Cos(a) * size * 0.55) + t2 * (Math.Sin(a) * size * 0.55);
            girdle[i] = m.AddVertex(p, (p - centre).Normalized(), colour, 1, 0.5, 0.5, 0, 0);
        }
        for (int i = 0; i < 4; i++)
        {
            int j = (i + 1) % 4;
            Primitives.TriangleFacing(m, apex, girdle[i], girdle[j], n);
            Primitives.TriangleFacing(m, foot, girdle[j], girdle[i], -n);
        }
    }

    /// <summary>
    /// One star flower: six narrow lanceolate tepals around a green ovary, with six anthers.
    /// The flower's axis points out of the umbel, so from outside the globe each flower is seen
    /// face-on and reads as a flat six-pointed star.
    /// </summary>
    private static void StarbulbFlower(MeshData m, Vec3 centre, Vec3 axis, double size, Rng rng)
    {
        int flowerFirst = m.VertexCount;
        var n = axis.Normalized();
        var t1 = n.Cross(new Vec3(0, 0, 1));
        if (t1.LengthSq < 1e-8) t1 = n.Cross(new Vec3(1, 0, 0));
        t1 = t1.Normalized();
        var t2 = n.Cross(t1).Normalized();
        double phase = rng.Range(0, Math.PI);
        double value = rng.Range(0.92, 1.07);

        for (int p = 0; p < 6; p++)
        {
            double a = phase + p * Math.PI / 3.0;
            var along = (t1 * Math.Cos(a) + t2 * Math.Sin(a)).Normalized();
            double len = size * rng.Range(0.92, 1.08);
            double halfWidth = len * rng.Range(0.115, 0.145);
            int first = m.VertexCount;
            NewFloraBlade(m, centre - n * (size * 0.05), along, n, len, halfWidth,
                StarbulbLanceWidth,
                t => Primitives.Scale(Primitives.Mix(StarbulbTepalBase, StarbulbTepalTip, t * t * 0.85 + t * 0.15), value),
                rows: 3, sides: 7, flatness: 0.30, keel: 0.14, cup: 0.12, camber: 0.10,
                midrib: StarbulbTepalVein, midribWidth: 0.45);
            // The tepals are the fantasy layer, so they are flagged as their own organ: the shader
            // then takes the colour straight off the vertex instead of pulling it towards the
            // species green, and gives the blade a glassy, semi-polished finish.
            for (int v = first; v < m.VertexCount; v++) m.UV2[v * 2] = 1.35f;
        }

        // The ovary: a small green centre that the six tepals spring from.
        Primitives.Ellipsoid(m, centre, new Vec3(size * 0.15, size * 0.15, size * 0.15), 2, 6,
            (u, v) => (StarbulbOvary, 1, u, v, 0, 0));

        // Six anthers standing clear of the ovary.
        for (int s = 0; s < 6; s++)
        {
            double a = phase + 0.5 + s * Math.PI / 3.0;
            var radial = (t1 * Math.Cos(a) + t2 * Math.Sin(a)).Normalized();
            var at = centre + radial * (size * 0.17) + n * (size * 0.05);
            StarbulbAntherGrain(m, at, n, size * 0.075, StarbulbAnther);
        }

        // The whole flower is one essential organ. The LOD tierer is allowed to thin the rosette
        // and the dried collar, but it must never leave a globe of bare pedicels behind, and it
        // must never take the six tepals off one flower and leave the other five.
        m.EssentialSpans.Add((flowerFirst, m.VertexCount - flowerFirst));
    }

    /// <summary>
    /// The umbel: a globe, not a disc. The flowers are placed on a sphere around a centre that
    /// sits above the scape's tip, and the pedicels run from the receptacle out to them, so their
    /// lengths grade from short at the sides to long over the top. That grading is what closes the
    /// inflorescence into a ball.
    /// </summary>
    private static void StarbulbUmbel(MeshData m, Vec3 scapeTip, Rng rng, int flowerCount, double[] pedicelCol)
    {
        // The receptacle: the swollen top of the scape where every pedicel springs from.
        Primitives.Ellipsoid(m, scapeTip, new Vec3(0.0042, 0.0038, 0.0042), 4, 8,
            (u, v) => (pedicelCol, 1, u, v, 0, 0));

        // The globe is a ball and not a disc. Its centre rides well above the receptacle, so the
        // pedicels that reach the flowers underneath are short and the ones that reach over the
        // top are long, and that grading is what closes the inflorescence into a sphere.
        double ballRadius = 0.0440;
        var centre = scapeTip + Vec3.Up * (ballRadius * 0.55);

        // The spathe: the papery bract that splits and falls from under the umbel. It is the last
        // thing left of the flower's own bud, and it hangs from the scape's tip like a torn flag.
        for (int k = 0; k < 3; k++)
        {
            double az = 2 * Math.PI * k / 3.0 + rng.Range(-0.3, 0.3);
            var outward = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var along = (outward * 0.55 + Vec3.Down * 0.84).Normalized();
            int first = m.VertexCount;
            NewFloraBlade(m, scapeTip + Vec3.Up * 0.002, along, outward, 0.0125, 0.0042,
                t => Math.Max(0.03, Math.Sin(Math.PI * Math.Min(1.0, t * 0.9 + 0.1))),
                t => Primitives.Mix(StarbulbSpathe, StarbulbTunicRim, 0.35 * t),
                rows: 4, sides: 5, flatness: 0.22, camber: 0.30, volumetric: true);
            // The spathe is dead tissue, not a leaf, so it is kept out of the leaf branch: a dry
            // parchment bract has no chlorophyll to be pulled towards the species green, and it
            // wants the matte finish of a shed husk.
            for (int v = first; v < m.VertexCount; v++)
            {
                m.UV2[v * 2] = 0.0f;
                m.UV2[v * 2 + 1] = 0.0f;
            }
        }

        // The flowers. A golden-angle spiral spread evenly over the cap of the sphere that the
        // umbel occupies, jittered so the globe is even but never mechanical. The cap reaches
        // well below the equator, because an Allium globe is a full ball with only the scape
        // passing into it.
        const double golden = 2.39996323;
        const double zFloor = -0.78;
        for (int i = 0; i < flowerCount; i++)
        {
            // Even in area, which means even in spacing: a plain Fibonacci sphere would crowd
            // the flowers at the top of the globe and leave the underside bare.
            double u = (i + 0.5) / flowerCount;
            double cz = 1.0 - u * (1.0 - zFloor);
            double sr = Math.Sqrt(Math.Max(0.0, 1.0 - cz * cz));
            double th = i * golden + rng.Range(-0.16, 0.16);
            var dir = new Vec3(sr * Math.Cos(th), cz, sr * Math.Sin(th)).Normalized();
            double dist = ballRadius * rng.Range(0.90, 1.08);
            var at = centre + dir * dist;

            // The pedicel: graded in length because the receptacle is below the globe's centre.
            var bulge = (at - centre).Normalized() * (ballRadius * 0.09);
            NewFloraStalk(m, scapeTip, at, bulge, 1.0, 0.00095, 0.00055,
                pedicelCol, pedicelCol, segments: 3, sides: 5, taper: 1.0);

            StarbulbFlower(m, at, dir, 0.0110 * rng.Range(0.92, 1.10), rng);
        }

        // The closed buds in the middle of the globe. An Allium opens from the outside in, so the
        // youngest flowers are still shut and still in the centre.
        int buds = Math.Max(3, flowerCount / 9);
        for (int b = 0; b < buds; b++)
        {
            double az = 2 * Math.PI * b / buds + rng.Range(-0.4, 0.4);
            double lift = rng.Range(0.25, 0.95);
            var dir = (new Vec3(Math.Cos(az), 0, Math.Sin(az)) * Math.Sqrt(Math.Max(0.0, 1.0 - lift * lift))
                + Vec3.Up * lift).Normalized();
            var at = centre + dir * rng.Range(0.0030, 0.0090);
            Primitives.Ellipsoid(m, at, new Vec3(0.0013, 0.0034, 0.0013), 3, 6,
                (u, v) => (Primitives.Mix(StarbulbBudCol, StarbulbGlaucous, 0.35), 1, u, v, 0, 0));
        }
    }

    // ---------------------------------------------------------------------------------------------
    // The plant
    // ---------------------------------------------------------------------------------------------

    /// <summary>
    /// The starbulb: a gold-papery bulb half sunk in the grit, a rosette of glaucous strap leaves,
    /// and a bare scape that bends over to carry a globe of cold blue stars.
    /// </summary>
    private static void Starbulb(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };

        // The species pair is a plain green. An Allium's own colour is a glaucous blue-green, so
        // the leaf colour is the species colour pulled towards grey-blue, not the other way round.
        var leafCol = Primitives.Mix(StarbulbGlaucous, c1, 0.20);
        var leafTip = Primitives.Mix(StarbulbGlaucousPale, c2, 0.14);
        var scapeCol = Primitives.Mix(StarbulbScapeCol, c1, 0.14);

        // ---- the bulb -----------------------------------------------------------------------
        // The bulb is the plant's anchor. A dry-ground Allium is mostly a bulb with a rosette on
        // top, and it is the one organ that has to survive the season, so it gets the most care.
        // It is planted in the grit: the basal plate is well down, so only the shoulder of the
        // bulb stands above the soil line, and the player sees a squat gold dome with a torn rim
        // and the neck standing out of its middle.
        double bulbRadius = 0.0300;
        double bulbBase = -0.0105;      // the basal plate, sunk below the grit line
        double bulbTop = 0.0340;        // the shoulder, where the neck begins

        int bulbFirst = part.VertexCount;
        StarbulbBulbCore(part, Vec3.Zero, bulbRadius, bulbBase, bulbTop, 9, 14, rng);

        // Five overlapping wraps. Each one covers a little more than half the bulb, starts under
        // its neighbour, and stands one paper thickness outside the plate beneath it, so the stack
        // reads as a spiral of paper rather than as a smooth golden egg.
        const int wraps = 5;
        for (int i = 0; i < wraps; i++)
        {
            double az = 2 * Math.PI * i / wraps + rng.Range(-0.10, 0.10);
            var spec = new StarbulbTunicSpec(
                Origin: Vec3.Zero,
                Radius: bulbRadius,
                CoreBase: bulbBase,
                CoreTop: bulbTop,
                TunicBase: bulbBase + 0.0020,
                TunicTop: bulbTop - rng.Range(0.0010, 0.0035),
                Azimuth: az,
                Span: Math.PI * 0.88,
                Thickness: 0.0013,
                Gap: 0.0008,
                Ribs: 3 + (i % 2),
                Peel: 0.0,
                Layer: i);
            StarbulbTunicPlate(part, spec, rng, 8, 11);
        }

        // The outermost tunic has split down one side and is peeling off the shoulder. It is the
        // one piece of the bulb that is not doing its job any more, and it is what stops the
        // bulb reading as a smooth golden egg.
        double splitAz = rng.Range(0, Math.PI * 2);
        for (int half = 0; half < 2; half++)
        {
            double az = splitAz + (half == 0 ? 0.18 : 3.32);
            var spec = new StarbulbTunicSpec(
                Origin: Vec3.Zero,
                Radius: bulbRadius,
                CoreBase: bulbBase,
                CoreTop: bulbTop,
                TunicBase: bulbBase + 0.0020,
                TunicTop: bulbTop - rng.Range(0.0005, 0.0030),
                Azimuth: az,
                Span: Math.PI * 0.62,
                Thickness: 0.0013,
                Gap: 0.0011,
                Ribs: 4,
                Peel: half == 0 ? -0.0060 : 0.0060,
                Layer: wraps);
            StarbulbTunicPlate(part, spec, rng, 8, 9);
        }

        // The offsets: two or three small bulbs pushed out of the basal plate.
        int bulblets = 2 + rng.NextInt(2);
        for (int b = 0; b < bulblets; b++)
        {
            double az = rng.Range(0, Math.PI * 2);
            double dist = bulbRadius * rng.Range(0.85, 1.15);
            var at = new Vec3(Math.Cos(az) * dist, 0, Math.Sin(az) * dist);
            double r = bulbRadius * rng.Range(0.30, 0.42);
            StarbulbBulblet(part, at, r, bulbBase + 0.0020, bulbTop - 0.0120, rng, 5, 9);
        }

        // The bulb is the plant's name. It is a loose shell of paper rather than a solid organ, so
        // the tierer would treat it as cullable scenery and drop it first, leaving the plant
        // standing on nothing. It is marked essential so that never happens.
        part.EssentialSpans.Add((bulbFirst, part.VertexCount - bulbFirst));

        // The ring of last year's leaf bases, dried and shingled round the foot of the bulb. It
        // is the oldest thing on the plant and it is what a real bulb wears in the dry season.
        NewFloraScaleColumn(part, new Vec3(0, bulbBase + 0.002, 0), Vec3.Up, 0.012, bulbRadius * 1.05,
            2, 9, 0.0130, 0.0050,
            (w, t) => Primitives.Mix(StarbulbDeadSheath, StarbulbTunicShade, 0.35 * t),
            rng, droop: 0.45, spiralTurns: 0.55);

        // ---- the neck and the rosette -------------------------------------------------------
        // The neck: the short cone between the bulb's shoulder and the leaves. Everything above
        // the ground comes out of this one point.
        Primitives.Tube(part, new[] { new Vec3(0, 0.028, 0), new Vec3(0, 0.040, 0), new Vec3(0, 0.046, 0) },
            new[] { 0.0110, 0.0058, 0.0042 }, 7,
            (i, v) => (Primitives.Mix(StarbulbFlesh, scapeCol, 0.45), 1, i * 0.5, v, 0, 0));

        int leaves = NewFloraCount(m, 7);
        for (int i = 0; i < leaves; i++)
        {
            double az = 2 * Math.PI * i / leaves + rng.Range(-0.30, 0.30);
            double len = rng.Range(0.170, 0.260);
            double halfWidth = len * rng.Range(0.030, 0.040);
            // The chord of the leaf. The camber does the arch: the leaf leaves the sheath steeply,
            // swings over, and the tip finishes hanging. The chord has to be a good deal steeper
            // than the leaf's own arc, or the rosette collapses into a flat spider on the grit.
            double chord = rng.Range(0.75, 1.02);
            var dir = new Vec3(Math.Cos(az) * Math.Cos(chord), Math.Sin(chord), Math.Sin(az) * Math.Cos(chord));
            var root = new Vec3(Math.Cos(az) * 0.0068, 0.0390, Math.Sin(az) * 0.0068);

            // The sheath: a short papery tube that clasps the neck below the blade's foot. It has
            // to stand just clear of the neck, or it is swallowed by it and the leaf looks as if it
            // simply starts out of the bulb with nothing holding it.
            Primitives.Tube(part, new[] { root - Vec3.Up * 0.0140, root - Vec3.Up * 0.0010 },
                new[] { 0.0050, 0.0042 }, 6,
                (i2, v) => (Primitives.Mix(StarbulbFlesh, leafCol, 0.55), 1, i2 * 0.5, v, 0, 0));

            NewFloraBlade(part, root, dir, Vec3.Up, len, halfWidth,
                StarbulbStrapWidth,
                t => Primitives.Mix(leafCol, leafTip, MathD.SmoothStep(0.10, 0.95, t)),
                rows: 8, sides: 7, flatness: 0.24, keel: 0.09, cup: 0.15,
                camber: rng.Range(0.18, 0.26),
                // A negative tip curl is a droop: the last third of the blade falls away from the
                // arch instead of flattening out, which is what an Allium leaf does as it ages.
                tipCurl: rng.Range(-0.14, -0.05),
                midrib: StarbulbMidrib, midribWidth: 0.32, volumetric: true);
        }

        // ---- the scape ----------------------------------------------------------------------
        // The scape is the plant's height: everything above the rosette is one bare stalk and
        // the globe it carries. It is long enough to be the dominant silhouette and short
        // enough that the plant still fits the height it is given.
        double scapeLength = rng.Range(0.495, 0.535);
        double scapeAz = rng.Range(0, Math.PI * 2);
        var scapeTip = StarbulbScape(part, new Vec3(0, 0.044, 0), scapeLength, scapeAz,
            rng.Range(0.42, 0.78), scapeCol, Primitives.Mix(scapeCol, leafTip, 0.35), 13, 8);

        // ---- the globe ----------------------------------------------------------------------
        int flowers = NewFloraCount(m, 60);
        StarbulbUmbel(part, scapeTip, rng, flowers, scapeCol);

        // MeshData.Append does not carry essential spans across, so the flower heads are moved
        // over by hand, with the same base offset the append is about to use.
        int baseVertex = m.VertexCount;
        foreach (var span in part.EssentialSpans)
            m.EssentialSpans.Add((baseVertex + span.FirstVertex, span.VertexCount));

        NewFloraAppendWorld(m, part, radius, height);
    }
}
