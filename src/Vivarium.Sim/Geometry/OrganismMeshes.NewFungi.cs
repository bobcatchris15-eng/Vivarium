using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Seven small fungal forms for the 2026-11-06 expansion: duskpuff, starsplit, reekhorn,
/// cradlecup, candlecoral, ashcomb and barrowbole.
///
/// Duskpuff bruises violet, Starsplit splits into thick tan rays, Reekhorn hangs a tattered
/// lace skirt, Cradlecup holds glossy spore-eggs on coiled cords, Candlecoral blushes rose
/// at the tips, Ashcomb pits its cap like a drowned comb, and Barrowbole pores cream
/// beneath its heavy dome.
///
/// All builders author in REAL METRES into a scratch part and convert with
/// NewFloraAppendWorld, following the Hollowcrown/Bonecup house pattern. Flesh is built
/// from Shef-free lathe grids, Primitives tubes/ellipsoids and Form.FoliageBlade blades,
/// wound with Primitives.TriangleFacing and recorded so the LOD tierer can drop whole
/// organs. Pigment is carried only by c1/c2 through Primitives.Mix/Scale; structural
/// tubes keep UV2 0,0, while leaf-recorded organs use the 1.35 marked-organ band so the
/// flora shader keeps the authored fungal pigment instead of leaf green.
/// </summary>
public static partial class OrganismMeshes
{
    // ---------------------------------------------------------------------------------------------
    // Shared lathe helpers (prefixed NewFungi so every partial file stays collision-free)
    // ---------------------------------------------------------------------------------------------

    /// <summary>Linear lookup into a small (t, value) profile table; t is clamped to the table.</summary>
    private static double NewFungiProfile(double[] ts, double[] vs, double t)
    {
        if (t <= ts[0]) return vs[0];
        for (int k = 1; k < ts.Length; k++)
        {
            if (t <= ts[k])
            {
                double f = (t - ts[k - 1]) / Math.Max(1e-9, ts[k] - ts[k - 1]);
                return vs[k - 1] + (vs[k] - vs[k - 1]) * f;
            }
        }
        return vs[^1];
    }

    /// <summary>
    /// The duskpuff's torn apical pore: a small flared funnel seated on the pericarp.
    /// Two rings plus a recessed centre open downward into the gleba like a torn ostiole.
    /// Bruised violet near the pore echoes the Duskpuff bruise.
    /// </summary>
    private static void NewFungiDuskpuffCrest(MeshData part, Vec3 top, double R, double[] poreCol, int sides)
    {
        sides = Math.Max(5, sides);
        // Wave 9: small sunken apical ostiole only — no lid, no fin.
        double rIn = R * 0.14;
        double rOut = R * 0.28;
        int centre = part.AddVertex(top + Vec3.Up * (-R * 0.030),
            Vec3.Up, Primitives.Scale(poreCol, 0.55), 1, 0.5, 0.5, 1.35, 1.0);
        var inner = new int[sides + 1];
        var outer = new int[sides + 1];
        var poreDark = Primitives.Scale(poreCol, 0.66);
        for (int j = 0; j <= sides; j++)
        {
            double th = 2.0 * Math.PI * j / sides;
            double tear = 1.0 + 0.14 * Math.Sin(3.0 * th + 1.2) + 0.07 * Math.Sin(5.0 * th + 0.3);
            inner[j] = part.AddVertex(top + new Vec3(Math.Cos(th) * rIn * tear, -R * 0.010, Math.Sin(th) * rIn * tear),
                Vec3.Up, poreCol, 1, (double)j / sides, 0.0, 1.35, 1.0);
            outer[j] = part.AddVertex(top + new Vec3(Math.Cos(th) * rOut * tear, Math.Min(-0.0004, -R * 0.004), Math.Sin(th) * rOut * tear),
                Vec3.Up, Primitives.Mix(poreCol, poreDark, 0.5), 1, (double)j / sides, 1.0, 1.35, 1.0);
        }
        for (int j = 0; j < sides; j++)
        {
            Primitives.TriangleFacing(part, centre, inner[j], inner[j + 1], Vec3.Up);
            var a = part.Position(inner[j]);
            var b = part.Position(inner[j + 1]);
            var c = part.Position(outer[j]);
            var fn = (b - a).Cross(c - a);
            if (fn.LengthSq < 1e-18) fn = (part.Position(outer[j + 1]) - a).Cross(c - a);
            if (fn.LengthSq < 1e-18) continue;
            fn = fn.Normalized();
            if (fn.Dot(Vec3.Up) < 0) fn = -fn;
            Primitives.TriangleFacing(part, inner[j], outer[j], inner[j + 1], fn);
            Primitives.TriangleFacing(part, inner[j + 1], outer[j], outer[j + 1], fn);
        }
    }

    /// <summary>
    /// Reekhorn gleba cap: a domed lathe whose pitted lower half is dented inward and
    /// painted dark so the pits read as the foul, spore-slicked honeycomb of Phallus.
    /// Fine bump noise breaks the smooth-helmet read across the whole olive cap.
    /// </summary>
    private static void NewFungiReekhornCap(MeshData part, Vec3 baseCentre, double Rcap, double capHeight, double[] c2, int rows, int sides)
    {
        rows = Math.Max(4, rows);
        sides = Math.Max(5, sides);
        var grid = new int[rows + 1, sides + 1];
        var normals = new Vec3[rows + 1, sides + 1];
        // gleba olive darkened for Wave 8: pit interiors ~0.30x hi so the
        // glistening dark jelly reads against the ivory horn, knot glisten
        // kept so pits stay slimy, not matte.
        var glebaHi = Primitives.Scale(c2, 0.82);
        var glebaPit = Primitives.Scale(glebaHi, 0.30);
        var glebaMid = Primitives.Scale(glebaHi, 0.55);
        for (int i = 0; i <= rows; i++)
        {
            double v = (double)i / rows;
            double theta = v * Math.PI * 0.5;
            double rBase = Rcap * Math.Pow(Math.Max(0.0, Math.Cos(theta)), 1.05);
            double y = capHeight * Math.Sin(theta);
            for (int j = 0; j <= sides; j++)
            {
                double u = (double)j / sides;
                double th = 2.0 * Math.PI * u;
                double r = rBase;
                var col = glebaHi;
                if (v < 0.72 && rBase > 1e-6)
                {
                    double pitZone = 1.0 - MathD.SmoothStep(0.18, 0.72, v);
                    double pit = Math.Abs(Math.Sin(u * 2.0 * Math.PI * 5.0) * Math.Sin(v * 2.0 * Math.PI * 3.6 + 0.4));
                    // deeper slime pits with matte irregular pitting, not a glazed helmet
                    double bump = Math.Sin(u * 2.0 * Math.PI * 11.0 + Math.Sin(v * 7.0) * 1.3)
                                * Math.Sin(v * 2.0 * Math.PI * 6.5 + 0.7);
                    r *= 1.0 - 0.24 * pit * pitZone + 0.022 * bump * (0.35 + 0.65 * pitZone);
                    col = Primitives.Mix(glebaPit, glebaMid, Math.Pow(pit, 0.7));
                    // glistening dark jelly: matte mottle breaks any plastic read,
                    // gloss knots between pits give the wet gleba sheen
                    double mottle = Math.Sin(th * 9.0 + v * 13.0) * Math.Sin(th * 17.0 - v * 7.0);
                    col = Primitives.Mix(col, glebaPit, Math.Max(0.0, mottle) * 0.20 * pitZone);
                    col = Primitives.Mix(col, glebaHi, Math.Max(0.0, -mottle) * 0.10 * pitZone);
                    // faint gloss knots between pits
                    col = Primitives.Mix(col, Primitives.Scale(glebaHi, 1.18), Math.Max(0.0, bump) * 0.16 * pitZone);
                }
                else
                {
                    double bump = Math.Sin(th * 4.0 + v * 6.0) * Math.Sin(v * 5.0);
                    r *= 1.0 + 0.018 * bump;
                    col = Primitives.Mix(glebaHi, glebaMid, 0.10 * Math.Sin(th * 2.0) + 0.05 * bump);
                }
                r = Math.Max(0.0004, r);
                var p = baseCentre + new Vec3(Math.Cos(th) * r, y, Math.Sin(th) * r);
                grid[i, j] = part.AddVertex(p, Vec3.Up, col, 1, u, v, 0.0, 0.0);
            }
        }
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < sides; j++)
            {
                var a = part.Position(grid[i, j]);
                var b = part.Position(grid[i, j + 1]);
                var c = part.Position(grid[i + 1, j]);
                var d = part.Position(grid[i + 1, j + 1]);
                var fn = (b - a).Cross(c - a);
                if (fn.LengthSq < 1e-18) fn = (d - a).Cross(c - a);
                if (fn.LengthSq < 1e-18) continue;
                fn = fn.Normalized();
                var centroid = (a + b + c + d) * 0.25 - baseCentre;
                var ref0 = new Vec3(centroid.X, 0, centroid.Z).Normalized() * 0.6 + Vec3.Up * 0.8;
                if (ref0.LengthSq > 1e-8 && fn.Dot(ref0.Normalized()) < 0) fn = -fn;
                normals[i, j] += fn;
                normals[i, j + 1] += fn;
                normals[i + 1, j] += fn;
                normals[i + 1, j + 1] += fn;
                Primitives.TriangleFacing(part, grid[i, j], grid[i, j + 1], grid[i + 1, j], fn);
                Primitives.TriangleFacing(part, grid[i, j + 1], grid[i + 1, j + 1], grid[i + 1, j], fn);
            }
        for (int i = 0; i <= rows; i++)
            for (int j = 0; j <= sides; j++)
            {
                var n = normals[i, j];
                if (n.LengthSq < 1e-18) n = Vec3.Up;
                n = n.Normalized();
                int vtx = grid[i, j];
                part.Normals[vtx * 3] = (float)n.X;
                part.Normals[vtx * 3 + 1] = (float)n.Y;
                part.Normals[vtx * 3 + 2] = (float)n.Z;
            }
        // Underside fan closing the cap over the stalk, facing down.
        int bottomCentre = part.AddVertex(baseCentre + Vec3.Up * 0.002,
            Vec3.Down, Primitives.Scale(c2, 0.62), 1, 0.5, 0.0, 1.35, 1.0);
        for (int j = 0; j < sides; j++)
            Primitives.TriangleFacing(part, bottomCentre, grid[0, j], grid[0, j + 1], Vec3.Down);
        // Top pole cap.
        int pole = part.AddVertex(baseCentre + Vec3.Up * capHeight,
            Vec3.Up, glebaHi, 1, 0.5, 1.0, 1.35, 1.0);
        for (int j = 0; j < sides; j++)
            Primitives.TriangleFacing(part, grid[rows, j], grid[rows, j + 1], pole, Vec3.Up);
    }

    /// <summary>
    /// One cradlecup cup: a single-surface revolved wall over a curved profile with a
    /// flared rim. Ochre outside deepens toward the base while the rim pales toward
    /// the inner face so the cup reads as an ochre nest with a pale mouth.
    /// The leaf shader renders both sides, so no second wall is needed.
    /// </summary>
    private static void NewFungiCradlecupCup(MeshData part, Vec3 centre, double R, double H, double[] c1, double[] c2, int rows, int sides)
    {
        rows = Math.Max(4, rows);
        sides = Math.Max(5, sides);
        double[] ts = { 0.0, 0.18, 0.42, 0.68, 1.0 };
        double[] rs = { 0.30, 0.52, 0.66, 0.76, 0.92 }; // SP11: broad seated foot
        double[] ys = { 0.0, 0.20, 0.50, 0.78, 1.0 };
        // Wave 9: warm woody cinnamon-ochre outer, pale buff inner. Base hues
        // as specified, brightened ~35% only to survive shader darkening.
        var outerBase = new[] { 0.68, 0.44, 0.20 };
        var innerBase = new[] { 0.88, 0.78, 0.58 };
        var c1b = Primitives.Scale(outerBase, 1.35);
        var c2b = Primitives.Scale(innerBase, 1.30);
        var paleInside = Primitives.Mix(c1b, c2b, 0.45);
        var grid = new int[rows + 1, sides + 1];
        var normals = new Vec3[rows + 1, sides + 1];
        for (int i = 0; i <= rows; i++)
        {
            double t = (double)i / rows;
            double r = NewFungiProfile(ts, rs, t) * R;
            double y = NewFungiProfile(ts, ys, t) * H;
            for (int j = 0; j <= sides; j++)
            {
                double u = (double)j / sides;
                double th = 2.0 * Math.PI * u;
                // fluted wall: 9 vertical grooves scallop the cup so it reads
                // as a bird's nest, not a smooth funnel (Wave 8).
                double flute = 1.0 + (0.055 * Math.Sin(th * 7.0 + 0.4) + 0.025 * Math.Sin(th * 11.0 + 1.7)) * (0.35 + 0.65 * t);
                double rr = r * flute;
                // ochre foot darkened, pale inner mouth brightened toward the rim;
                // matte mottling keeps the ochre paper-dull, not plastic
                var col = Primitives.Mix(Primitives.Scale(c1b, 0.86), c1b, t);
                double inner = MathD.SmoothStep(0.55, 1.0, t);
                col = Primitives.Mix(col, paleInside, inner * 0.60);
                col = Primitives.Mix(col, Primitives.Scale(c1b, 1.06), inner * inner * 0.25);
                double mottle = Math.Sin(th * 7.0 + t * 9.0) * Math.Sin(th * 13.0 - t * 5.0);
                col = Primitives.Mix(col, Primitives.Scale(c1b, 0.80), Math.Max(0.0, mottle) * 0.22);
                col = Primitives.Mix(col, Primitives.Scale(c1b, 1.05), Math.Max(0.0, -mottle) * 0.10);
                var p = centre + new Vec3(Math.Cos(th) * Math.Max(0.0, rr), y, Math.Sin(th) * Math.Max(0.0, rr));
                grid[i, j] = part.AddVertex(p, Vec3.Up, col, 1, u, t, 1.35, 1.0);
            }
        }
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < sides; j++)
            {
                var a = part.Position(grid[i, j]);
                var b = part.Position(grid[i, j + 1]);
                var c = part.Position(grid[i + 1, j]);
                var d = part.Position(grid[i + 1, j + 1]);
                var fn = (b - a).Cross(c - a);
                if (fn.LengthSq < 1e-18) fn = (d - a).Cross(c - a);
                if (fn.LengthSq < 1e-18) continue;
                fn = fn.Normalized();
                var centroid = (a + b + c + d) * 0.25 - centre;
                var ref0 = new Vec3(centroid.X, 0, centroid.Z).Normalized() * 0.75 + Vec3.Up * 0.45;
                if (ref0.LengthSq > 1e-8 && fn.Dot(ref0.Normalized()) < 0) fn = -fn;
                normals[i, j] += fn;
                normals[i, j + 1] += fn;
                normals[i + 1, j] += fn;
                normals[i + 1, j + 1] += fn;
                Primitives.TriangleFacing(part, grid[i, j], grid[i, j + 1], grid[i + 1, j], fn);
                Primitives.TriangleFacing(part, grid[i, j + 1], grid[i + 1, j + 1], grid[i + 1, j], fn);
            }
        for (int i = 0; i <= rows; i++)
            for (int j = 0; j <= sides; j++)
            {
                var n = normals[i, j];
                if (n.LengthSq < 1e-18) n = Vec3.Up;
                n = n.Normalized();
                int vtx = grid[i, j];
                part.Normals[vtx * 3] = (float)n.X;
                part.Normals[vtx * 3 + 1] = (float)n.Y;
                part.Normals[vtx * 3 + 2] = (float)n.Z;
            }
        // Bottom centre closes the cup floor.
        int bc = part.AddVertex(centre, Vec3.Up, Primitives.Scale(c1b, 0.88), 1, 0.5, 0.0, 1.35, 1.0);
        for (int j = 0; j < sides; j++)
            Primitives.TriangleFacing(part, bc, grid[0, j], grid[0, j + 1], Vec3.Up);
        // Wave 9: distinct rolled rim tube around the mouth (pale buff).
        {
            int rimSteps = Math.Max(10, sides);
            var rimPath = new List<Vec3>();
            var rimRadii = new List<double>();
            for (int j = 0; j <= rimSteps; j++)
            {
                double th = 2.0 * Math.PI * j / rimSteps;
                double flute = 1.0 + 0.055 * Math.Sin(th * 7.0 + 0.4) + 0.025 * Math.Sin(th * 11.0 + 1.7);
                rimPath.Add(centre + new Vec3(Math.Cos(th) * R * 0.92 * flute, H, Math.Sin(th) * R * 0.92 * flute));
                rimRadii.Add(Math.Max(0.0005, R * 0.075)); // SP11: thicker rim
            }
            var rimCol = Primitives.Scale(c2b, 1.02);
            Primitives.Tube(part, rimPath, rimRadii, 6,
                (i, v) => (rimCol, 1.0, (double)i / Math.Max(1, rimSteps), v, 0.0, 0.0));
        }
    }

    /// <summary>
    /// Ashcomb cap: tall oval honeycomb. Radius is modulated by the ridge field
    /// |sin|sin|| so ridges swell outward and pits sink inward with a deep comb profile;
    /// pigment follows the same field on an ash-grey ground so pits read dark-rimmed
    /// and ridges stay grey, never white. Profile is oval with a rounded top, not a sharp cone.
    /// </summary>
    private static void NewFungiAshcombCap(MeshData part, Vec3 baseCentre, double radius, double height,
        double[] ash, double[] cream, int rows, int columns, ulong seed)
    {
        const int rimSamples = 8;
        var rimColor = Primitives.Mix(ash, cream, 0.29);
        var innerColor = Primitives.Scale(Primitives.Mix(ash, cream, 0.16), 0.80);
        var pitColor = Primitives.Scale(ash, 0.68);
        var grid = new Vec2[rows + 1, columns + 1];
        for (int row = 0; row <= rows; row++)
        {
            for (int col = 0; col < columns; col++)
            {
                ulong node = (ulong)(row * columns + col);
                double jitterU = Rng.HashUnit(seed, 0x41534843UL, node) - 0.5;
                double jitterV = Rng.HashUnit(seed, 0x41534844UL, node) - 0.5;
                double bend = Math.Sin(Math.Tau * col / columns + row * 1.79) * 0.17;
                grid[row, col] = new Vec2(
                    (col + 0.26 * Math.Sin(row * 2.12) + jitterU * 0.62) / columns,
                    (row + (row == 0 || row == rows ? 0.0 : jitterV * 0.66 + bend)) / rows);
            }
            grid[row, columns] = grid[row, 0] + new Vec2(1, 0);
        }

        Vec3 Position(Vec2 uv, double depth)
        {
            double theta = uv.X * Math.Tau;
            double h = uv.Z;
            double shoulder = (0.79 + 0.28 * Math.Sin(Math.PI * h * 0.92)) * (1.0 - 0.52 * h * h);
            double shape = shoulder * MathD.Lerp(1.0, 0.46, MathD.SmoothStep(0.78, 1.0, h));
            double side = 1.0 + 0.045 * Math.Sin(3 * theta + h * 1.3) + 0.025 * Math.Sin(7 * theta - h * 2.1);
            double r = Math.Max(radius * 0.08, radius * shape * side - depth);
            return baseCentre + new Vec3(Math.Cos(theta) * r, h * height, Math.Sin(theta) * r);
        }

        for (int row = 0; row < rows; row++)
        for (int col = 0; col < columns; col++)
        {
            var a = grid[row, col];
            var b = grid[row, col + 1];
            var c = grid[row + 1, col + 1];
            var d = grid[row + 1, col];
            Vec2[] perimeter = { a, (a + b) * 0.5, b, (b + c) * 0.5, c, (c + d) * 0.5, d, (d + a) * 0.5 };
            ulong identity = (ulong)(row * columns + col);
            var centre = (a + b + c + d) * 0.25 + new Vec2(
                (Rng.HashUnit(seed, 0x41534845UL, identity) - 0.5) * 0.20 / columns,
                (Rng.HashUnit(seed, 0x41534846UL, identity) - 0.5) * 0.20 / rows);
            double depth = radius * (0.075 + 0.125 * Rng.HashUnit(seed, 0x41534847UL, identity));
            double theta = centre.X * Math.Tau;
            var outward = new Vec3(Math.Cos(theta), 0.0, Math.Sin(theta));
            var innerCentre = Position(centre, depth);
            int start = part.VertexCount;
            for (int ring = 0; ring < 3; ring++)
            for (int i = 0; i < rimSamples; i++)
            {
                double contraction = ring == 0 ? 1.0 : ring == 1 ? 0.75 + 0.05 * Math.Sin(identity * 1.41) : 0.36;
                double recess = ring == 0 ? 0.0 : ring == 1 ? depth * 0.22 : depth * 0.88;
                var outline = perimeter[i];
                if (ring != 0)
                {
                    var left = perimeter[(i + rimSamples - 1) % rimSamples];
                    var right = perimeter[(i + 1) % rimSamples];
                    outline = outline * 0.60 + (left + right) * 0.20;
                }
                var uv = Vec2.Lerp(centre, outline, contraction);
                var pos = Position(uv, recess);
                double angle = uv.X * Math.Tau;
                var radial = new Vec3(Math.Cos(angle), 0.20 * uv.Z, Math.Sin(angle));
                var intoPit = (innerCentre - pos);
                var tangent = intoPit - outward * intoPit.Dot(outward);
                var normal = (radial + tangent.Normalized() * (ring == 1 ? 0.46 : ring == 2 ? 0.25 : 0.0)).Normalized();
                double grain = Math.Sin(22 * angle + 41 * uv.Z) * Math.Sin(13 * angle - 67 * uv.Z);
                var tissue = ring == 0 ? rimColor : ring == 1 ? innerColor : pitColor;
                var color = Primitives.Scale(tissue, 1.0 + grain * 0.06);
                part.AddVertex(pos, normal, color, 1, uv.X, uv.Z, 0, 0);
            }
            int floor = part.AddVertex(innerCentre, outward, pitColor, 1, centre.X, centre.Z, 0, 0);
            for (int i = 0; i < rimSamples; i++)
            {
                int next = (i + 1) % rimSamples;
                for (int ring = 0; ring < 2; ring++)
                {
                    int k = start + ring * rimSamples;
                    Primitives.TriangleFacing(part, k + i, k + next, k + rimSamples + i, outward);
                    Primitives.TriangleFacing(part, k + next, k + rimSamples + next, k + rimSamples + i, outward);
                }
                Primitives.TriangleFacing(part, start + 2 * rimSamples + i, start + 2 * rimSamples + next, floor, outward);
            }
        }

        for (int end = 0; end < 2; end++)
        {
            int row = end == 0 ? 0 : rows;
            var normal = end == 0 ? Vec3.Down : Vec3.Up;
            var pole = baseCentre + Vec3.Up * (end == 0 ? -height * 0.045 : height * 1.052);
            int centre = part.AddVertex(pole, normal, rimColor, 1);
            for (int i = 0; i < columns; i++)
            {
                var a = grid[row, i];
                var b = grid[row, i + 1];
                int v0 = part.AddVertex(Position(a, 0), normal, rimColor, 1);
                int mid = part.AddVertex(Position((a + b) * 0.5, 0), normal, rimColor, 1);
                int v1 = part.AddVertex(Position(b, 0), normal, rimColor, 1);
                Primitives.TriangleFacing(part, centre, v0, mid, normal);
                Primitives.TriangleFacing(part, centre, mid, v1, normal);
            }
        }
    }

    /// <summary>
    /// Barrowbole cap + pore face: hemispherical dome with an inrolled margin over an annular
    /// pore disc whose radial striations are tinted c2 cream.
    /// </summary>
    private static void NewFungiBarrowboleCapAndPore(MeshData part, Vec3 rimCentre, double Rcap, double capHeight, double stemTopR, double[] c1, double[] c2, int rows, int sides)
    {
        rows = Math.Max(4, rows);
        sides = Math.Max(5, sides);
        var grid = new int[rows + 1, sides + 1];
        var normals = new Vec3[rows + 1, sides + 1];
        // brightened only for shader darkening: heavy brown cap stays brown,
        // cream pores stay cream (not white halo)
        var c1b = Primitives.Scale(c1, 1.20);
        var c2b = Primitives.Scale(c2, 1.08);
        var capTopCol = Primitives.Mix(c1b, c2b, 0.06);
        for (int i = 0; i <= rows; i++)
        {
            double v = (double)i / rows;
            double theta = v * Math.PI * 0.5;
            double rBase = Rcap * Math.Pow(Math.Max(0.0, Math.Cos(theta)), 0.80);
            double y = capHeight * Math.Sin(theta);
            // Inrolled margin: the rim curls down and inward.
            if (v < 0.16)
            {
                double k = 1.0 - v / 0.16;
                y -= capHeight * 0.05 * k;
                rBase *= 1.0 - 0.08 * k;
            }
            for (int j = 0; j <= sides; j++)
            {
                double u = (double)j / sides;
                double th = 2.0 * Math.PI * u;
                // irregular domed cap: low-frequency lumps and a lopsided lean break the perfect dome
                double scab = Math.Sin(th * 3.0 + v * 5.0) * 0.055 + Math.Sin(th * 5.0 - v * 7.0) * 0.038
                    + Math.Sin(th * 9.0 + v * 11.0) * 0.026 + Math.Sin(th * 13.0 - v * 9.0) * 0.015;
                double lean = 1.0 + 0.06 * Math.Sin(th - 0.9) * v;
                double r = Math.Max(0.0004, rBase * (1.0 + scab) * lean);
                y = capHeight * Math.Sin(theta) * (1.0 + 0.10 * Math.Sin(th + 1.1) * v);
                if (v < 0.16)
                {
                    double k = 1.0 - v / 0.16;
                    y -= capHeight * 0.05 * k;
                }
                var col = Primitives.Mix(c1b, capTopCol, v);
                double patch = Math.Sin(th * 5.0 + v * 7.0) * Math.Sin(th * 9.0 - v * 4.0);
                // matte scabrous mottling, no specular shine
                col = Primitives.Mix(col, Primitives.Scale(c1b, 0.78), Math.Max(0.0, patch) * 0.34);
                col = Primitives.Mix(col, Primitives.Scale(c1b, 1.05), Math.Max(0.0, -patch) * 0.16);
                double fine = Math.Sin(th * 17.0 + v * 19.0) * Math.Sin(th * 23.0 - v * 13.0);
                col = Primitives.Mix(col, Primitives.Scale(c1b, 0.86), Math.Max(0.0, fine) * 0.14);
                var p = rimCentre + new Vec3(Math.Cos(th) * r, y, Math.Sin(th) * r);
                grid[i, j] = part.AddVertex(p, Vec3.Up, col, 1, u, v, 0.0, 0.0);
            }
        }
        for (int i = 0; i < rows; i++)
            for (int j = 0; j < sides; j++)
            {
                var a = part.Position(grid[i, j]);
                var b = part.Position(grid[i, j + 1]);
                var c = part.Position(grid[i + 1, j]);
                var d = part.Position(grid[i + 1, j + 1]);
                var fn = (b - a).Cross(c - a);
                if (fn.LengthSq < 1e-18) fn = (d - a).Cross(c - a);
                if (fn.LengthSq < 1e-18) continue;
                fn = fn.Normalized();
                var centroid = (a + b + c + d) * 0.25 - rimCentre;
                var ref0 = new Vec3(centroid.X, 0, centroid.Z).Normalized() * 0.65 + Vec3.Up * 0.75;
                if (ref0.LengthSq > 1e-8 && fn.Dot(ref0.Normalized()) < 0) fn = -fn;
                normals[i, j] += fn;
                normals[i, j + 1] += fn;
                normals[i + 1, j] += fn;
                normals[i + 1, j + 1] += fn;
                Primitives.TriangleFacing(part, grid[i, j], grid[i, j + 1], grid[i + 1, j], fn);
                Primitives.TriangleFacing(part, grid[i, j + 1], grid[i + 1, j + 1], grid[i + 1, j], fn);
            }
        for (int i = 0; i <= rows; i++)
            for (int j = 0; j <= sides; j++)
            {
                var n = normals[i, j];
                if (n.LengthSq < 1e-18) n = Vec3.Up;
                n = n.Normalized();
                int vtx = grid[i, j];
                part.Normals[vtx * 3] = (float)n.X;
                part.Normals[vtx * 3 + 1] = (float)n.Y;
                part.Normals[vtx * 3 + 2] = (float)n.Z;
            }
        int pole = part.AddVertex(rimCentre + Vec3.Up * capHeight,
            Vec3.Up, capTopCol, 1, 0.5, 1.0, 0.0, 0.0);
        for (int j = 0; j < sides; j++)
            Primitives.TriangleFacing(part, grid[rows, j], grid[rows, j + 1], pole, Vec3.Up);

        // Pore disc: annular grid facing down, meeting the cap rim and stem
        // with no gap (no floating halo). Cream face with gentle striations.
        int pr = 5;
        double rIn = Math.Max(0.002, stemTopR * 0.98);
        // Wave 8 polish: pore face opened to 0.95Rcap so the cream face reads
        double rOut = Rcap * 0.95;
        var pgrid = new int[pr + 1, sides + 1];
        var pnormals = new Vec3[pr + 1, sides + 1];
        for (int k = 0; k <= pr; k++)
        {
            double s = (double)k / pr;
            double r = rIn + (rOut - rIn) * s;
            // Wave 9 fix: LOCAL offset below capBase (rimCentre). Previously this
            // used rimCentre.Y as the local y and then added rimCentre again,
            // double-counting the height and floating the cream ring in mid-air
            // above the cap. The pore annulus now sits at the underside of the
            // cap, between stemTopR and Rcap, never above the cap.
            double y = -0.0012 - 0.0020 * Math.Sin(Math.PI * s);
            for (int j = 0; j <= sides; j++)
            {
                double u = (double)j / sides;
                double th = 2.0 * Math.PI * u;
                double stri = Math.Pow(Math.Abs(Math.Sin(th * 52.0)), 7.0);
                var col = Primitives.Mix(Primitives.Scale(c2b, 1.06), Primitives.Scale(c2b, 0.52), stri * 0.55);
                var p = rimCentre + new Vec3(Math.Cos(th) * r, y, Math.Sin(th) * r);
                pgrid[k, j] = part.AddVertex(p, Vec3.Down, col, 1, u, s, 1.35, 1.0);
            }
        }
        for (int k = 0; k < pr; k++)
            for (int j = 0; j < sides; j++)
            {
                var a = part.Position(pgrid[k, j]);
                var b = part.Position(pgrid[k, j + 1]);
                var c = part.Position(pgrid[k + 1, j]);
                var d = part.Position(pgrid[k + 1, j + 1]);
                var fn = (b - a).Cross(c - a);
                if (fn.LengthSq < 1e-18) fn = (d - a).Cross(c - a);
                if (fn.LengthSq < 1e-18) continue;
                fn = fn.Normalized();
                if (fn.Dot(Vec3.Down) < 0) fn = -fn;
                pnormals[k, j] += fn;
                pnormals[k, j + 1] += fn;
                pnormals[k + 1, j] += fn;
                pnormals[k + 1, j + 1] += fn;
                Primitives.TriangleFacing(part, pgrid[k, j], pgrid[k, j + 1], pgrid[k + 1, j], fn);
                Primitives.TriangleFacing(part, pgrid[k, j + 1], pgrid[k + 1, j + 1], pgrid[k + 1, j], fn);
            }
        for (int k = 0; k <= pr; k++)
            for (int j = 0; j <= sides; j++)
            {
                var n = pnormals[k, j];
                if (n.LengthSq < 1e-18) n = Vec3.Down;
                n = n.Normalized();
                int vtx = pgrid[k, j];
                part.Normals[vtx * 3] = (float)n.X;
                part.Normals[vtx * 3 + 1] = (float)n.Y;
                part.Normals[vtx * 3 + 2] = (float)n.Z;
            }
    }

    /// <summary>One candlecoral branch: a club that swells at the warm-rose tip and forks once or twice as depth allows.</summary>
    private static void NewFungiCandlecoralBranch(MeshData part, Vec3 root, Vec3 dir, double len, double baseR,
        double[] baseCol, double[] tipCol, Rng rng, int sides, int depth)
    {
        var p0 = root;
        var p1 = root + dir * (len * 0.5) + new Vec3(rng.Range(-0.003, 0.003), len * 0.03, rng.Range(-0.003, 0.003));
        var p2 = root + dir * len;
        var warmTip = Primitives.Scale(Primitives.Mix(tipCol, baseCol, 0.28), 1.02);
        // fleshy candle club: narrows at the waist then swells into a blunt rounded tip
        Primitives.Tube(part, new[] { p0, p1, p2 }, new[] { baseR, baseR * 0.68, baseR * 0.42 }, sides,
            (i, v) =>
            {
                double t = (double)i / 2.0;
                // bleach to cream: the whole club stays cream and the rose blush is
                // confined to the last few percent before the tip cap, so the tips
                // read as distinctly rose against cream, not a pink wash.
                var bodyCol = Primitives.Mix(baseCol, tipCol, MathD.SmoothStep(0.72, 0.98, t) * 0.06);
                var col = Primitives.Mix(bodyCol, warmTip, MathD.SmoothStep(0.78, 1.0, t) * 0.90);
                // waxy translucency streaks: pale lengthwise variation, matte not plastic
                double wax = Math.Sin(v * 2.0 * Math.PI * 3.0 + t * 4.0);
                col = Primitives.Mix(col, Primitives.Scale(baseCol, 1.08), Math.Max(0.0, wax) * 0.16);
                col = Primitives.Mix(col, Primitives.Scale(baseCol, 0.90), Math.Max(0.0, -wax) * 0.14);
                double fine = Math.Sin(v * 2.0 * Math.PI * 9.0 + t * 11.0) * Math.Sin(v * 2.0 * Math.PI * 15.0 - t * 7.0);
                col = Primitives.Mix(col, Primitives.Scale(baseCol, 0.94), Math.Max(0.0, fine) * 0.12);
                return (col, 1.0, t, v, 0.0, 0.0);
            });
        // candle-like rounded tip cap: plump blunt end so each club reads as a wax candle
        {
            int tipFirst = part.VertexCount;
            Primitives.Ellipsoid(part, p2 + dir * (baseR * 0.18), new Vec3(baseR * 0.44, baseR * 0.60, baseR * 0.44), 3, sides,
                (u, v) => (warmTip, 1.0, u, v, 0.0, 0.0));
            part.Structural.Add((tipFirst, part.VertexCount - tipFirst, sides, baseR * 0.80));
        }
        // short prickles along the club shaft so each club reads as a branching candle
        if (depth == 0)
        {
            int prickles = 1 + (rng.NextDouble() < 0.5 ? 1 : 0);
            var side0 = new Vec3(-dir.Z, 0, dir.X);
            if (side0.LengthSq < 1e-8) side0 = new Vec3(1, 0, 0);
            side0 = side0.Normalized();
            for (int p = 0; p < prickles; p++)
            {
                double pt = rng.Range(0.35, 0.58);
                var attach = Vec3.Lerp(p0, p2, pt);
                double psign = rng.NextDouble() < 0.5 ? -1.0 : 1.0;
                var nd = (dir * 0.55 + side0 * (psign * rng.Range(0.55, 0.85)) + Vec3.Up * 0.35).Normalized();
                if (nd.LengthSq < 1e-8) nd = Vec3.Up;
                double nLen = len * rng.Range(0.10, 0.14);
                var prickleBase = Primitives.Mix(baseCol, tipCol, pt * 0.10);
                NewFungiCandlecoralBranch(part, attach, nd, nLen, baseR * 0.38, prickleBase, tipCol, rng, sides, 2);
            }
        }
        if (depth >= 2) return;
        if (depth == 1 && rng.NextDouble() > 0.15) return;
        double forkT = rng.Range(0.40, 0.75);
        var forkPt = Vec3.Lerp(p0, p2, forkT);
        var side = new Vec3(-dir.Z, 0, dir.X);
        if (side.LengthSq < 1e-8) side = new Vec3(1, 0, 0);
        side = side.Normalized();
        var forkBase = Primitives.Mix(baseCol, tipCol, forkT * 0.15);
        for (int s = -1; s <= 1; s += 2)
        {
            var nd = (dir + side * (s * rng.Range(0.50, 0.70)) + Vec3.Up * rng.Range(0.05, 0.15)).Normalized();
            if (nd.LengthSq < 1e-8) nd = Vec3.Up;
            double nLen = len * rng.Range(0.38, 0.56);
            NewFungiCandlecoralBranch(part, forkPt, nd, nLen, baseR * 0.62, forkBase, tipCol, rng, sides, depth + 1);
        }
    }

    // ---------------------------------------------------------------------------------------------
    // Duskpuff: squat papery sphere on a short base, torn pore at the apex; bruises violet.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Duskpuff: a squat papery puffball on humus; matte body, violet-bruised pore.</summary>
    private static void Duskpuff(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        int detail = m.FloraDetailLevel ?? 0;
        radius = Math.Max(0.01, radius);
        height = Math.Max(0.01, height);
        double ju = juvenile ? 0.72 : 1.0;
        double h = height * ju;
        double R = Math.Max(0.004, height * 0.55 * ju);
        R = Math.Min(R, Math.Max(0.004, radius * 0.58));
        double Ry = R * 0.62;
        double centreY = h * 0.48;
        // papery off-white kept warm: only a modest brightening so the tan hue
        // survives instead of clamping to chalk-white; violet is held saturated
        // but confined to the apical pore only (see ellipsoid colour below).
        var c1b = Primitives.Scale(c1, 1.10);
        var c2b = Primitives.Scale(c2, 1.20);
        Rng.HashUnit(seed, 0x4E657746756E6769UL, 1); // deterministic stream tag; rng carries the draws

        int rings = detail == 0 ? 8 : detail == 1 ? 6 : 5;
        int segs = detail == 0 ? 16 : detail == 1 ? 12 : 9;
        if (m.Lod is { } lod)
        {
            rings = Math.Max(4, lod.Segments(rings));
            segs = Math.Max(5, lod.Sides(segs));
        }

        int leafV = part.VertexCount;
        int leafI = part.Indices.Count;
        int bodyFirst = part.VertexCount;
        Primitives.Ellipsoid(part, new Vec3(0, centreY, 0), new Vec3(R, Ry, R), rings, segs,
            (u, v) =>
            {
                double ySign = Math.Sin(Math.PI * u) * Math.Cos(2.0 * Math.PI * v);
                double under = MathD.SmoothStep(0.15, -0.55, ySign);
                // matte off-white paper body: brightened c1 dominates, violet
                // confined to the apical pore ring; base and upper skin stay
                // off-white so the puff reads as paper, not a lilac mushroom.
                var col = Primitives.Mix(c1b, Primitives.Scale(c1b, 0.86), under * 0.65);
                col = Primitives.Mix(col, Primitives.Scale(c1b, 0.92), 0.07 * Math.Sin(v * 2.0 * Math.PI * 3.0 + u * 4.0));
                // violet only at the torn pore itself, not a broad bruise wash
                // (Wave 8 polish: pore ring slightly deepened for macro read)
                double poreRing = MathD.SmoothStep(0.72, 0.99, ySign) * 0.78;
                col = Primitives.Mix(col, Primitives.Scale(c2b, 0.92), poreRing);
                // papery mottling: strong fine + blotchy matte variation kills the grape-gloss read
                double rough = Math.Sin(v * 2.0 * Math.PI * 5.0 + u * 7.0) * Math.Sin(v * 2.0 * Math.PI * 11.0 + u * 13.0)
                    + 0.55 * Math.Sin(v * 2.0 * Math.PI * 17.0 + u * 21.0);
                col = Primitives.Mix(col, Primitives.Scale(c1b, 0.80), Math.Max(0.0, rough) * 0.26);
                col = Primitives.Mix(col, Primitives.Scale(c1b, 1.05), Math.Max(0.0, -rough) * 0.16);
                double blotch = Math.Sin(v * 2.0 * Math.PI * 3.0 + u * 2.0) * Math.Sin(v * 2.0 * Math.PI * 7.0 - u * 9.0);
                col = Primitives.Mix(col, Primitives.Scale(c1b, 0.86), Math.Max(0.0, blotch) * 0.22);
                col = Primitives.Mix(col, Primitives.Scale(c1b, 1.04), Math.Max(0.0, -blotch) * 0.12);
                // fine pore pitting: sparse dark flecks so the skin reads papery-torn, not smooth
                double pore = Math.Pow(Math.Abs(Math.Sin(v * 2.0 * Math.PI * 23.0 + u * 11.0) * Math.Sin(v * 2.0 * Math.PI * 29.0 - u * 17.0)), 10.0);
                col = Primitives.Mix(col, Primitives.Scale(c1b, 0.70), pore * 0.40);
                return (col, 1.0, u, v, 0.0, 0.0);
            });
        // slight radial irregularity: keep the squat papery form but avoid a perfect sphere.
        // A finer papery corrugation is creased into the skin so it dents like thin paper.
        int bodyLast = part.VertexCount;
        for (int vi = bodyFirst; vi < bodyLast; vi++)
        {
            var p = part.Position(vi);
            double dx = p.X;
            double dz = p.Z;
            double dy = p.Y - centreY;
            double az = Math.Atan2(dz, dx);
            double irr = 1.0 + 0.075 * Math.Sin(3.0 * az + 1.7) + 0.050 * Math.Sin(5.0 * az + 0.6) + 0.030 * Math.Sin(2.0 * az + 3.1)
                + 0.018 * Math.Sin(9.0 * az + 2.2) * Math.Sin(6.0 * (dy / Math.Max(0.004, Ry)) + 1.0)
                + 0.012 * Math.Sin(13.0 * az + 0.4) * Math.Sin(9.0 * (dy / Math.Max(0.004, Ry)) + 0.3);
            double yIrr = 1.0 + 0.04 * Math.Sin(2.0 * az + 0.9) + 0.014 * Math.Sin(7.0 * az + 0.2);
            var np = new Vec3(dx * irr, centreY + dy * yIrr, dz * irr);
            part.Positions[vi * 3] = (float)np.X;
            part.Positions[vi * 3 + 1] = (float)np.Y;
            part.Positions[vi * 3 + 2] = (float)np.Z;
        }
        part.Structural.Add((bodyFirst, part.VertexCount - bodyFirst, segs, R));

        // Torn pore: hollow funnel seated in the pericarp; detail 2 keeps a simple ring.
        var poreCol = Primitives.Scale(c2b, 0.85);
        NewFungiDuskpuffCrest(part, new Vec3(0, centreY + Ry, 0), R, poreCol, segs);
        // Wave 9: lid deleted — no shark-fin flap. Small raised papery warts
        // (2-3 mm) across the upper hemisphere instead.
        {
            // SP11: areolate/warty skin - many raised warts scaled to the body so they read at macro range.
            int warts = detail == 0 ? 46 + rng.NextInt(10) : detail == 1 ? 24 : 12;
            var wartCol = Primitives.Scale(c1b, 0.80);
            for (int w = 0; w < warts; w++)
            {
                double wa = rng.Range(0, 2.0 * Math.PI);
                double wElev = rng.Range(-0.35, 0.88); // upper hemisphere + shoulders
                double wh = Math.Sqrt(Math.Max(0.0, 1.0 - wElev * wElev));
                var wc = new Vec3(Math.Cos(wa) * R * wh * 1.0, centreY + Ry * wElev * 1.0, Math.Sin(wa) * R * wh * 1.0);
                double wr = R * rng.Range(0.07, 0.115);
                int wartFirst = part.VertexCount;
                Primitives.Ellipsoid(part, wc, new Vec3(wr, wr * 0.75, wr), 3, 6,
                    (u, v) => (wartCol, 1.0, u, v, 0.0, 0.0));
                part.Structural.Add((wartFirst, part.VertexCount - wartFirst, 6, wr));
            }
        }
        part.RecordLeaf(leafV, leafI, new Vec3(0, centreY, 0), h * 0.7, volumetric: true);
        part.EssentialSpans.Add((leafI, part.Indices.Count - leafI));

        // Short thick stem holding the sphere just clear of the litter.
        double tubeTop = centreY - Ry * 0.30;
        var p0 = new Vec3(0, -0.002, 0);
        var p1 = new Vec3(rng.Range(-0.002, 0.002), tubeTop * 0.5, rng.Range(-0.002, 0.002));
        var p2 = new Vec3(0, tubeTop, 0);
        var baseCol = Primitives.Scale(c1b, 0.85);
        int tubeSides = Math.Max(6, segs / 2);
        Primitives.Tube(part, new[] { p0, p1, p2 }, new[] { R * 0.62, R * 0.48, R * 0.55 }, tubeSides,
            (i, v) =>
            {
                var col = Primitives.Mix(baseCol, c1b, (double)i / 2.0);
                double mottle = Math.Sin(v * 2.0 * Math.PI * 5.0 + i * 1.7) * Math.Sin(v * 2.0 * Math.PI * 9.0 + i * 2.9);
                col = Primitives.Mix(col, Primitives.Scale(col, 0.82), Math.Max(0.0, mottle) * 0.24);
                col = Primitives.Mix(col, Primitives.Scale(col, 1.06), Math.Max(0.0, -mottle) * 0.12);
                return (col, 1.0, (double)i / 2.0, v, 0.0, 0.0);
            });

        NewFloraAppendWorld(m, part, radius, height);
    }

    // ---------------------------------------------------------------------------------------------
    // Starsplit: central sac joined to broad, curled split-skin rays.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Starsplit: a puffball with a continuous folded outer skin splitting into uneven rays.</summary>
    private static void Starsplit(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        int detail = m.FloraDetailLevel ?? 0;
        radius = Math.Max(0.01, radius);
        height = Math.Max(0.01, height);
        double ju = juvenile ? 0.72 : 1.0;
        double Rb = Math.Max(0.004, height * 0.48 * ju);
        Rb = Math.Min(Rb, Math.Max(0.004, radius * 0.34));
        double bulbY = height * 0.50 * ju;
        double phase = Rng.HashUnit(seed, 0x4E657746756E6769UL, 2) * 2.0 * Math.PI;
        // warm tan held warm: only a modest brightening so the tan does not wash
        // chalk-pale; the bulb (c2) stays warm-cream under its wart mottling.
        var c1b = Primitives.Scale(c1, 1.12);
        var c2b = Primitives.Scale(c2, 1.08);

        int rings = detail == 0 ? 7 : detail == 1 ? 6 : 5;
        int segs = detail == 0 ? 14 : detail == 1 ? 11 : 8;
        if (m.Lod is { } lod)
        {
            rings = Math.Max(4, lod.Segments(rings));
            segs = Math.Max(5, lod.Sides(segs));
        }

        int leafV = part.VertexCount;
        int leafI = part.Indices.Count;
        int bodyFirst = part.VertexCount;
        // domed warm-cream bulb: rough exoperidium mottling breaks the chalky smooth read
        Primitives.Ellipsoid(part, new Vec3(0, bulbY, 0), new Vec3(Rb, Rb * 1.02, Rb), rings, segs,
            (u, v) =>
            {
                double ySign = Math.Sin(Math.PI * u) * Math.Cos(2.0 * Math.PI * v);
                double under = MathD.SmoothStep(0.15, -0.55, ySign);
                var col = Primitives.Mix(c2b, Primitives.Scale(c2b, 0.72), under);
                // top hemisphere warmed and held below chalk-white so the puff stays warm-cream
                double topHi = MathD.SmoothStep(0.35, 0.95, ySign);
                col = Primitives.Mix(col, Primitives.Scale(c2b, 1.00), topHi * 0.28);
                // papery porous exoperidium: wart mottling + dark pores, no plastic sheen
                double wart = Math.Sin(v * 2.0 * Math.PI * 9.0 + u * 6.0) * Math.Sin(v * 2.0 * Math.PI * 13.0 - u * 11.0);
                col = Primitives.Mix(col, Primitives.Scale(c1b, 0.96), Math.Max(0.0, wart) * 0.32);
                col = Primitives.Mix(col, Primitives.Scale(c2b, 0.80), Math.Max(0.0, -wart) * 0.22);
                double pore = Math.Pow(Math.Abs(Math.Sin(v * 2.0 * Math.PI * 17.0 + u * 9.0) * Math.Sin(v * 2.0 * Math.PI * 23.0 - u * 13.0)), 6.0);
                col = Primitives.Mix(col, Primitives.Scale(c1b, 0.72), pore * 0.52);
                return (col, 1.0, u, v, 0.0, 0.0);
            });
        // slight radial irregularity so the puff is not a perfect ball
        {
            int bodyLast = part.VertexCount;
            for (int vi = bodyFirst; vi < bodyLast; vi++)
            {
                var p = part.Position(vi);
                double dx = p.X;
                double dz = p.Z;
                double dy = p.Y - bulbY;
                double az = Math.Atan2(dz, dx);
                double irr = 1.0 + 0.070 * Math.Sin(4.0 * az + 2.1) + 0.045 * Math.Sin(7.0 * az + 0.7) + 0.030 * Math.Sin(2.0 * az + 3.4);
                double yIrr = 1.0 + 0.04 * Math.Sin(3.0 * az + 1.2);
                var np = new Vec3(dx * irr, bulbY + dy * yIrr, dz * irr);
                part.Positions[vi * 3] = (float)np.X;
                part.Positions[vi * 3 + 1] = (float)np.Y;
                part.Positions[vi * 3 + 2] = (float)np.Z;
            }
        }
        part.Structural.Add((bodyFirst, part.VertexCount - bodyFirst, segs, Rb));
        part.RecordLeaf(leafV, leafI, new Vec3(0, bulbY, 0), height * 0.5, volumetric: true);
        part.EssentialSpans.Add((leafI, part.Indices.Count - leafI));

        // Six broad, irregular rays emerge from within the sac's lower shell so
        // the split skin stays physically joined instead of reading as detached petals.
        int rayBase = 6;
        int rays = NewFloraCount(part, rayBase);
        for (int k = 0; k < rays; k++)
        {
            double az = 2.0 * Math.PI * k / Math.Max(1, rays) + phase + rng.Range(-0.22, 0.22);
            var outward = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var side = new Vec3(-outward.Z, 0, outward.X);
            double splitReach = (k % 2 == 0) ? rng.Range(0.78, 1.0) : rng.Range(0.58, 0.84);
            double reach = radius * splitReach * ju;
            reach = Math.Max(reach, Rb * 1.6);
            // Root lies slightly inside the sac at its lower fold; its exposed
            // surface emerges from the bulb rather than beginning beyond its edge.
            double rootY = Math.Max(0.002, bulbY - Rb * rng.Range(0.82, 0.90));
            var root = outward * (Rb * rng.Range(0.54, 0.66)) + Vec3.Up * rootY;
            var tip = outward * reach + Vec3.Up * rng.Range(0.002, 0.008);
            double len = (tip - root).Length;
            if (len < 1e-5) continue;
            // Wave 8 polish: rays thickened (diagnostic earthstar detail)
            double hw = len * rng.Range(0.25, 0.32);
            double rayCamber = rng.Range(0.22, 0.32);
            double rayCurl = rng.Range(0.030, 0.055);
            // per-ray matte variation: each ray dries a slightly different tan, never a uniform wash
            var rayBaseCol = Primitives.Scale(c1b, rng.Range(0.94, 1.04));
            var tipCol = Primitives.Scale(c1b, rng.Range(0.82, 0.95));
            int mark = part.VertexCount;
            Form.FoliageBlade.Build(part, root, tip, side, hw, rayBaseCol, tipCol, camber: rayCamber, curl: rayCurl, shoulder: 1.15);
            // underside skin layer: gives the ray thickness at its edges and seats it on the receptacle
            double skin = Math.Max(0.0012, Rb * 0.07);
            Form.FoliageBlade.Build(part, root - Vec3.Up * skin, tip - Vec3.Up * skin * 0.6, side, hw * 0.94,
                Primitives.Scale(rayBaseCol, 0.84), Primitives.Scale(tipCol, 0.84), camber: rayCamber * 0.9, curl: rayCurl, shoulder: 1.15);
            NewFloraMarkOrgan(part, mark, part.VertexCount - mark);
        }

        NewFloraAppendWorld(m, part, radius, height);
    }

    // ---------------------------------------------------------------------------------------------
    // Reekhorn: hollow pale stalk, pitted olive cap, drooping lace skirt.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Reekhorn: tall slime-capped spike with a lace skirt; the vertical decomposer.</summary>
    private static void Reekhorn(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        int detail = m.FloraDetailLevel ?? 0;
        radius = Math.Max(0.01, radius);
        height = Math.Max(0.01, height);
        double ju = juvenile ? 0.72 : 1.0;
        double h = height * ju;
        double phase = Rng.HashUnit(seed, 0x4E657746756E6769UL, 3) * 2.0 * Math.PI;

        // Cream horn: tapering stalk in c1; dark gleba occupies the top
        // quarter only (cap 0.75h-1.0h). Wave 8: cap seated at 0.75h so the
        // darkest olive jelly crowns the top quarter, not a helmet tip.
        double capBase = h * 0.75;
        double stalkTop = capBase + h * 0.02;
        double stalkR = Math.Max(0.003, h * 0.095);
        var s0 = new Vec3(0, -0.002, 0);
        var s1 = new Vec3(rng.Range(-0.002, 0.002), stalkTop * 0.5, rng.Range(-0.002, 0.002));
        var s2 = new Vec3(0, stalkTop, 0);
        int stalkSides = detail == 0 ? 9 : detail == 1 ? 7 : 6;
        if (m.Lod is { } lodSt) stalkSides = Math.Max(5, lodSt.Sides(stalkSides));
        Primitives.Tube(part, new[] { s0, s1, s2 },
            new[] { stalkR * 1.45, stalkR * 0.88, stalkR * 0.52 }, stalkSides,
            (i, v) =>
            {
                var col = Primitives.Mix(Primitives.Scale(c1, 0.98), Primitives.Scale(c1, 1.08), (double)i / 2.0);
                // slime-smearred stalk: matte olive-grey smears break the smooth white ghost
                double smear = Math.Sin(v * 2.0 * Math.PI * 4.0 + i * 1.3) * Math.Sin(v * 2.0 * Math.PI * 7.0 - i * 2.1 + 0.7);
                col = Primitives.Mix(col, Primitives.Scale(col, 0.78), Math.Max(0.0, smear) * 0.28);
                col = Primitives.Mix(col, Primitives.Scale(col, 1.05), Math.Max(0.0, -smear) * 0.10);
                double fine = Math.Sin(v * 2.0 * Math.PI * 13.0 + i * 3.7) * Math.Sin(v * 2.0 * Math.PI * 21.0 - i * 4.3);
                col = Primitives.Mix(col, Primitives.Scale(col, 0.86), Math.Max(0.0, fine) * 0.14);
                return (col, 1.0, (double)i / 2.0, v, 0.0, 0.0);
            });

        // Olive gleba cap at the apex only: a bold olive dome, not a helmet spot.
        double Rcap = Math.Max(0.004, stalkR * 2.90);
        Rcap = Math.Min(Rcap, Math.Max(0.004, radius * 0.42));
        double capHeight = Math.Max(0.012, h * 0.27);
        int rows = detail == 0 ? 8 : detail == 1 ? 7 : 5;
        int sides = detail == 0 ? 16 : detail == 1 ? 12 : 9;
        if (m.Lod is { } lod)
        {
            rows = Math.Max(4, lod.Segments(rows));
            sides = Math.Max(5, lod.Sides(sides));
        }
        int capV = part.VertexCount;
        int capI = part.Indices.Count;
        int capFirst = part.VertexCount;
        NewFungiReekhornCap(part, new Vec3(0, capBase, 0), Rcap, capHeight, c2, rows, sides);
        part.Structural.Add((capFirst, part.VertexCount - capFirst, sides, Rcap));
        part.RecordLeaf(capV, capI, new Vec3(0, capBase, 0), capHeight, volumetric: true);
        part.EssentialSpans.Add((capV, part.VertexCount - capV));

        // Joined strands form a short net below the wet cap rim.
        int columns = detail == 0 ? 16 : detail == 1 ? 12 : 8;
        int tiers = detail == 0 ? 5 : 4;
        double laceTop = capBase + capHeight * 0.30;
        double drop = h * 0.285;
        var lace = Primitives.Mix(c1, new[] { 0.92, 0.84, 0.66 }, 0.52);
        int firstLace = part.VertexCount;
        var junctions = new Vec3[tiers + 1, columns];
        for (int row = 0; row <= tiers; row++)
        for (int col = 0; col < columns; col++)
        {
            double t = row / (double)tiers;
            double angle = phase + Math.Tau * (col + 0.5 * (row % 2)) / columns;
            angle += rng.Range(-0.026, 0.026) * t;
            double r = Rcap * (0.88 + Math.Sin(t * 1.82) * 0.49);
            r *= 1.0 + 0.05 * Math.Sin(angle * 3.0 + phase);
            double y = laceTop - drop * t + rng.Range(-0.16, 0.12) * drop * t * t;
            junctions[row, col] = new Vec3(Math.Cos(angle) * r, y, Math.Sin(angle) * r);
        }
        void Thread(Vec3 from, Vec3 to, double thickness)
        {
            double sag = 0.0015 + rng.Range(0.0, 0.0010);
            var mid = (from + to) * 0.5 - Vec3.Up * sag;
            Primitives.Tube(part, new[] { from, mid, to }, new[] { thickness, thickness * 0.84, thickness }, 6,
                (station, v) => (lace, 1, station * 0.5, v, 1.35, 1.0));
        }
        for (int col = 0; col < columns; col++)
            Thread(junctions[0, col], junctions[0, (col + 1) % columns], 0.00075);
        for (int row = 0; row < tiers; row++)
        for (int col = 0; col < columns; col++)
        {
            var from = junctions[row, col];
            double thickness = MathD.Lerp(0.00065, 0.00043, row / (double)(tiers - 1));
            int neighbor = (col + ((row % 2 == 0) ? columns - 1 : 1)) % columns;
            Thread(from, junctions[row + 1, col], thickness);
            if (row < tiers - 1 || (col + (int)(seed & 3)) % 4 != 0)
                Thread(from, junctions[row + 1, neighbor], thickness);
        }
        part.EssentialSpans.Add((firstLace, part.VertexCount - firstLace));

        NewFloraAppendWorld(m, part, radius, height);
    }

    // ---------------------------------------------------------------------------------------------
    // Cradlecup: clustered bird's-nest cups holding bright spore-eggs, on 1-2 cm stalks.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Cradlecup: clustered ochre cups sitting on the litter cradling glossy egg-like peridioles on coiled cords (Wave 8: 2-4 mm base, 2.2-3.0 cm cups).</summary>
    private static void Cradlecup(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        int detail = m.FloraDetailLevel ?? 0;
        radius = Math.Max(0.01, radius);
        height = Math.Max(0.01, height);
        double ju = juvenile ? 0.72 : 1.0;
        double phase = Rng.HashUnit(seed, 0x4E657746756E6769UL, 4) * 2.0 * Math.PI;

        int rows = detail == 0 ? 6 : detail == 1 ? 5 : 4;
        int sides = detail == 0 ? 12 : detail == 1 ? 10 : 8;
        if (m.Lod is { } lod)
        {
            rows = Math.Max(4, lod.Segments(rows));
            sides = Math.Max(5, lod.Sides(sides));
        }

        int cupBase = 4 + rng.NextInt(2); // 4-5 cups, leave open litter between nests
        int cups = NewFloraCount(part, cupBase);
        double hScale = (height / 0.03) * ju;
        for (int k = 0; k < cups; k++)
        {
            double az = phase + 2.0 * Math.PI * k / Math.Max(1, cups) + rng.Range(-0.30, 0.30);
            var dir = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            // Wave 9: tight cluster within 0.10-0.32 * radius
            double dist = cups == 1 ? 0.0 : radius * rng.Range(0.30, 0.62) * ju;
            // sitting directly on the ground, thick-walled cups on the litter
            double stalkH = -0.0058; // SP11: base sunk into the litter, no floating stalk
            var centre = dir * dist + Vec3.Up * stalkH;
            // Each cup 2.0-2.6 cm wide (radius 10-13 mm), Hc = 0.75 * Rc tall.
            double Rc = rng.Range(0.015, 0.019) * (juvenile ? 0.8 : 1.0); // SP11: larger cups
            double Hc = 0.75 * Rc;

            int cupV = part.VertexCount;
            int cupI = part.Indices.Count;
            NewFungiCradlecupCup(part, centre, Rc, Hc, c1, c2, rows, sides);
            part.RecordLeaf(cupV, cupI, centre, Hc, volumetric: true);
            part.EssentialSpans.Add((cupI, part.Indices.Count - cupI));

            // 4-5 glossy pale-grey/white peridioles nestled in the open bowl
            int eggs = detail == 2 ? 3 : rng.NextInt(2) + 3; // 3-4 eggs, visibly separated
            // Wave 9: bright glossy ivory-white lentil eggs, clearly visible
            // against the cinnamon-ochre bowl.
            var eggBase = new[] { 0.95, 0.94, 0.88 };
            var eggDark = Primitives.Scale(eggBase, 0.82);
            var eggHi = Primitives.Scale(eggBase, 1.05);
            var eggSheen = new[] { 1.0, 1.0, 1.0 };
            for (int e = 0; e < eggs; e++)
            {
                double ea = rng.Range(0, 2.0 * Math.PI);
                double ed = Rc * rng.Range(0.06, 0.22);
                double er = Rc * rng.Range(0.22, 0.27);
                var ec = centre + new Vec3(Math.Cos(ea) * ed, er * 0.70, Math.Sin(ea) * ed);
                // radius assigned above so the base sits against the cup floor
                int eggFirst = part.VertexCount;
                Primitives.Ellipsoid(part, ec, new Vec3(er, er * 0.92, er), 4, 8,
                    (u, v) =>
                    {
                        // glossy dome: dark flank, bright crown on +Y, plus a small sheen spot
                        double topness = (Math.Sin(Math.PI * u) * Math.Cos(2.0 * Math.PI * v) + 1.0) * 0.5;
                        topness = MathD.SmoothStep(0.45, 0.98, topness);
                        var col = Primitives.Mix(eggDark, eggHi, topness);
                        double sheen = Math.Pow(Math.Abs(Math.Sin(Math.PI * u) * Math.Cos(2.0 * Math.PI * v - 0.8)), 3.0);
                        col = Primitives.Mix(col, eggSheen, sheen * 0.35);
                        return (col, 1.0, u, v, 0.0, 0.0);
                    });
                part.Structural.Add((eggFirst, part.VertexCount - eggFirst, 8, er));
                // coiled cord: a stout curled funiculus spiralling up beside each egg
                {
                    var cordBase = ec + new Vec3(Math.Cos(ea + 1.2) * er * 1.30, er * 0.25, Math.Sin(ea + 1.2) * er * 1.30);
                    double curlR = er * 0.55;
                    var cp0 = cordBase;
                    var cp1 = cordBase + new Vec3(Math.Cos(ea + 2.0) * curlR, er * 0.20, Math.Sin(ea + 2.0) * curlR);
                    var cp2 = cordBase + new Vec3(Math.Cos(ea + 3.2) * curlR * 0.85, er * 0.35, Math.Sin(ea + 3.2) * curlR * 0.85);
                    var cp3 = cordBase + new Vec3(Math.Cos(ea + 4.4) * curlR * 0.55, er * 0.48, Math.Sin(ea + 4.4) * curlR * 0.55);
                    var cp4 = cordBase + new Vec3(Math.Cos(ea + 5.6) * curlR * 0.28, er * 0.58, Math.Sin(ea + 5.6) * curlR * 0.28);
                    var cordCol = Primitives.Scale(c1, 0.38);
                    int cordSides = detail == 0 ? 5 : detail == 1 ? 5 : 5;
                    Primitives.Tube(part, new[] { cp0, cp1, cp2, cp3, cp4 },
                        new[] { er * 0.30, er * 0.27, er * 0.24, er * 0.20, er * 0.16 }, Math.Max(5, cordSides),
                        (i, v) =>
                        {
                            var col = Primitives.Mix(cordCol, Primitives.Scale(cordCol, 1.25), (double)i / 4.0);
                            double mottle = Math.Sin(v * 2.0 * Math.PI * 3.0 + i * 1.6);
                            col = Primitives.Mix(col, Primitives.Scale(col, 0.78), Math.Max(0.0, mottle) * 0.22);
                            return (col, 1.0, (double)i / 4.0, v, 0.0, 0.0);
                        });
                }
            }
        }

        NewFloraAppendWorld(m, part, radius, height);
    }

    // ---------------------------------------------------------------------------------------------
    // Candlecoral: 4-8 forking clubs from a basal knot, cream to rose tips.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Candlecoral: upright cream-and-rose candle branching from buried wood.</summary>
    private static void Candlecoral(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        int detail = m.FloraDetailLevel ?? 0;
        radius = Math.Max(0.01, radius);
        height = Math.Max(0.01, height);
        double ju = juvenile ? 0.72 : 1.0;
        double h = height * ju;
        double phase = Rng.HashUnit(seed, 0x4E657746756E6769UL, 5) * 2.0 * Math.PI;

        // Basal knot anchoring the tuft.
        double knotR = Math.Max(0.004, h * 0.12);
        int kRows = detail == 0 ? 6 : 5;
        int kSegs = detail == 0 ? 12 : detail == 1 ? 10 : 8;
        if (m.Lod is { } lodK)
        {
            kRows = Math.Max(4, lodK.Segments(kRows));
            kSegs = Math.Max(5, lodK.Sides(kSegs));
        }
        int knotFirst = part.VertexCount;
        var knotCol = Primitives.Scale(c1, 0.85);
        Primitives.Ellipsoid(part, new Vec3(0, h * 0.04, 0), new Vec3(knotR, knotR * 0.85, knotR), kRows, kSegs,
            (u, v) =>
            {
                var col = knotCol;
                double mottle = Math.Sin(v * 2.0 * Math.PI * 5.0 + u * 6.0) * Math.Sin(v * 2.0 * Math.PI * 9.0 - u * 7.0);
                col = Primitives.Mix(col, Primitives.Scale(col, 0.80), Math.Max(0.0, mottle) * 0.26);
                col = Primitives.Mix(col, Primitives.Scale(col, 1.08), Math.Max(0.0, -mottle) * 0.12);
                return (col, 1.0, u, v, 0.0, 0.0);
            });
        part.Structural.Add((knotFirst, part.VertexCount - knotFirst, kSegs, knotR));

        int clubBase = 7 + rng.NextInt(5); // 7-11 clubs, denser tuft
        int clubs = NewFloraCount(part, clubBase);
        int sides = detail == 0 ? 8 : detail == 1 ? 7 : 6;
        if (m.Lod is { } lod)
        {
            sides = Math.Max(5, lod.Sides(sides));
        }
        double baseR = Math.Max(0.0014, h * 0.058);
        for (int k = 0; k < clubs; k++)
        {
            double az = phase + 2.0 * Math.PI * k / Math.Max(1, clubs) + rng.Range(-0.25, 0.25);
            var outward = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var root = outward * (knotR * rng.Range(0.20, 0.55)) + Vec3.Up * (h * 0.07);
            double tilt = rng.Range(0.22, 0.42);
            var dir = (outward * Math.Sin(tilt) + Vec3.Up * Math.Cos(tilt)).Normalized();
            // wide height variation for a natural candle unevenness
            double len = h * rng.Range(0.40, 1.05);
            len = Math.Max(0.015, len);
            NewFungiCandlecoralBranch(part, root, dir, len, baseR * rng.Range(0.85, 1.15), c1, c2, rng, sides, 0);
        }

        NewFloraAppendWorld(m, part, radius, height);
    }

    // ---------------------------------------------------------------------------------------------
    // Ashcomb: cream stipe under a tall honeycombed ash-grey cap.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Ashcomb: honeycombed ash-grey cap on a short thick cream stipe.</summary>
    private static void Ashcomb(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        int detail = m.FloraDetailLevel ?? 0;
        radius = Math.Max(0.01, radius);
        height = Math.Max(0.01, height);
        double ju = juvenile ? 0.72 : 1.0;
        double h = height * ju;
        Rng.HashUnit(seed, 0x4E657746756E6769UL, 6); // deterministic tag; rng carries the draws

        // Short thick cream stipe in pale c2, clearly visible below the morel cap.
        // Wave 8: cap seated higher on a distinct waist (cap 0.52h, stipe top
        // 0.50h, base girth 0.60Rb) so the cream stem reads separately.
        double stipeTop = h * 0.50;
        var stipeCol = Primitives.Scale(c2, 1.18);
        double stipeR = Math.Max(0.003, h * 0.095);
        var s0 = new Vec3(0, -0.002, 0);
        var s1 = new Vec3(rng.Range(-0.0015, 0.0015), stipeTop * 0.55, rng.Range(-0.0015, 0.0015));
        var s2 = new Vec3(0, stipeTop, 0);
        int stipeSides = detail == 0 ? 9 : detail == 1 ? 7 : 6;
        if (m.Lod is { } lodSt) stipeSides = Math.Max(5, lodSt.Sides(stipeSides));
        Primitives.Tube(part, new[] { s0, s1, s2 },
            new[] { stipeR * 1.20, stipeR * 1.00, stipeR * 0.85 }, stipeSides,
            (i, v) =>
            {
                var col = Primitives.Mix(Primitives.Scale(stipeCol, 0.92), stipeCol, (double)i / 2.0);
                double mottle = Math.Sin(v * 2.0 * Math.PI * 5.0 + i * 1.8) * Math.Sin(v * 2.0 * Math.PI * 9.0 - i * 2.4);
                col = Primitives.Mix(col, Primitives.Scale(col, 0.80), Math.Max(0.0, mottle) * 0.26);
                col = Primitives.Mix(col, Primitives.Scale(col, 1.06), Math.Max(0.0, -mottle) * 0.12);
                return (col, 1.0, (double)i / 2.0, v, 0.0, 0.0);
            });

        // Honeycomb morel cap merging into the stipe.
        double Rb = Math.Max(0.004, h * 0.22);
        Rb = Math.Min(Rb, Math.Max(0.004, radius * 0.52));
        double capH = Math.Max(0.01, h * 0.62);
        int rows = detail == 0 ? 7 : detail == 1 ? 6 : 4;
        int columns = detail == 0 ? 12 : detail == 1 ? 9 : 7;
        int capV = part.VertexCount;
        int capI = part.Indices.Count;
        var capBase = new Vec3(0, h * 0.52, 0);
        NewFungiAshcombCap(part, capBase, Rb, capH, c1, c2, rows, columns, seed);
        part.RecordLeaf(capV, capI, capBase, capH, volumetric: true);
        part.EssentialSpans.Add((capV, part.VertexCount - capV));

        NewFloraAppendWorld(m, part, radius, height);
    }

    // ---------------------------------------------------------------------------------------------
    // Barrowbole: stout stem, brown hemispherical cap, cream pore disc beneath.
    // ---------------------------------------------------------------------------------------------

    /// <summary>Barrowbole: stout brown-capped bolete with a cream striated pore face.</summary>
    private static void Barrowbole(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        int detail = m.FloraDetailLevel ?? 0;
        radius = Math.Max(0.01, radius);
        height = Math.Max(0.01, height);
        double ju = juvenile ? 0.72 : 1.0;
        double h = height * ju;
        Rng.HashUnit(seed, 0x4E657746756E6769UL, 7); // deterministic tag; rng carries the draws

        // Stout cream stem flaring at the foot, reticulate-darkened near the top.
        double capBase = h * 0.48;
        double stemR = Math.Max(0.004, h * 0.26);
        var s0 = new Vec3(0, -0.002, 0);
        var s1 = new Vec3(rng.Range(-0.002, 0.002), capBase * 0.5, rng.Range(-0.002, 0.002));
        var s2 = new Vec3(0, capBase, 0);
        int stemSides = detail == 0 ? 10 : detail == 1 ? 8 : 6;
        if (m.Lod is { } lodSt) stemSides = Math.Max(5, lodSt.Sides(stemSides));
        Primitives.Tube(part, new[] { s0, s1, s2 },
            new[] { stemR * 1.35, stemR * 1.00, stemR * 0.92 }, stemSides,
            (i, v) =>
            {
                var baseCol = Primitives.Mix(Primitives.Scale(c2, 0.92), Primitives.Scale(c2, 1.02), (double)i / 2.0);
                double topness = (double)i / 2.0;
                // reticulate net: intersecting diagonals darken near the apex, kept light
                // enough that the stem stays cream, never near-black
                double net = Math.Abs(Math.Sin(v * 2.0 * Math.PI * 5.0 + i * 0.9) * Math.Sin(v * 2.0 * Math.PI * 3.0 - i * 0.7));
                double retic = MathD.SmoothStep(0.35, 1.0, topness) * (1.0 - net * 0.80);
                var col = Primitives.Mix(Primitives.Scale(c2, 0.68), baseCol, retic);
                double mottle = Math.Sin(v * 2.0 * Math.PI * 7.0 + i * 2.1) * Math.Sin(v * 2.0 * Math.PI * 11.0 - i * 1.6);
                col = Primitives.Mix(col, Primitives.Scale(col, 0.86), Math.Max(0.0, mottle) * 0.18);
                return (col, 1.0, (double)i / 2.0, v, 0.0, 0.0);
            });

        // Brown hemispherical cap with an inrolled margin; cream pores underneath.
        double Rcap = Math.Max(0.005, h * 0.52);
        Rcap = Math.Min(Rcap, Math.Max(0.005, radius * 0.75));
        double capHeight = Math.Max(0.008, Rcap * 0.55);
        int rows = detail == 0 ? 9 : detail == 1 ? 7 : 5;
        int sides = detail == 0 ? 22 : detail == 1 ? 18 : 13;
        if (m.Lod is { } lod)
        {
            rows = Math.Max(4, lod.Segments(rows));
            sides = Math.Max(5, lod.Sides(sides));
        }
        int capV = part.VertexCount;
        int capI = part.Indices.Count;
        int capFirst = part.VertexCount;
        NewFungiBarrowboleCapAndPore(part, new Vec3(0, capBase, 0), Rcap, capHeight, stemR, c1, c2, rows, sides);
        part.Structural.Add((capFirst, part.VertexCount - capFirst, sides, Rcap));
        part.RecordLeaf(capV, capI, new Vec3(0, capBase, 0), capHeight, volumetric: true);
        part.EssentialSpans.Add((capI, part.Indices.Count - capI));

        NewFloraAppendWorld(m, part, radius, height);
    }
}
