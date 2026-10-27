using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Geometry;

/// <summary>Seed-driven procedural prop meshes. Any seed yields a distinct, deterministic variant.</summary>
public static class PropMeshes
{
    // Toned toward natural, weathered stone: low saturation greys, tans and slate, no saturated hues.
    private static readonly double[][] RockPalette =
    {
        new[] { 0.56, 0.54, 0.52 }, // warm grey granite
        new[] { 0.49, 0.51, 0.53 }, // slate grey
        new[] { 0.60, 0.53, 0.44 }, // tan sandstone
        new[] { 0.51, 0.50, 0.47 }, // umber basalt
        new[] { 0.66, 0.63, 0.58 }, // pale limestone
        new[] { 0.53, 0.46, 0.42 }, // muted umber-red
    };

    // Mineral tints for river pebbles (dark basalt, warm quartzite, slate grey, ochre chert, speckled granite).
    private static readonly double[][] PebblePalette =
    {
        new[] { 0.22, 0.22, 0.24 }, // dark basalt
        new[] { 0.62, 0.55, 0.42 }, // warm quartzite
        new[] { 0.46, 0.48, 0.50 }, // slate grey
        new[] { 0.58, 0.46, 0.30 }, // ochre chert
        new[] { 0.50, 0.48, 0.44 }, // speckled granite
    };

    /// <summary>High-tessellation Voronoi planar-cleaved stratified boulder with beveled edge transitions.</summary>
    public static MeshData Rock(ulong variantSeed, int detail = 3)
    {
        if (detail == 1) return LegacyRock(variantSeed, detail);
        var profile = RockSurfaceProfile.Create(variantSeed);
        var palette = RockPalette[profile.Palette];
        var mesh = new MeshData();
        const int divisions = 9; // 81 triangles per authoritative fracture face.

        // Deterministic Voronoi 3D cleavage planes for this rock
        var rng = Rng.Keyed(variantSeed, "rock.cleavage", 0);
        const int planeCount = 14;
        var planeNormals = new Vec3[planeCount];
        var planeDists = new double[planeCount];
        var center = new Vec3(0, 0.15, 0);

        // 8 steep lateral fracture joints (forming faceted polygonal silhouette)
        for (int k = 0; k < 8; k++)
        {
            double phi = 2 * Math.PI * (k + rng.Range(-0.25, 0.25)) / 8.0;
            double tilt = rng.Range(-0.22, 0.22);
            double ny = Math.Sin(tilt);
            double cosTilt = Math.Cos(tilt);
            double nx = Math.Cos(phi) * cosTilt;
            double nz = Math.Sin(phi) * cosTilt;
            var n = new Vec3(nx, ny, nz).Normalized();
            planeNormals[k] = n;
            planeDists[k] = center.Dot(n) + rng.Range(0.62, 0.78);
        }

        // 4 oblique corner fracture joints (creating dramatic diagonal cleavage facets)
        for (int k = 0; k < 4; k++)
        {
            double phi = 2 * Math.PI * (k + 0.5 + rng.Range(-0.2, 0.2)) / 4.0;
            double pitch = rng.Range(0.55, 0.90);
            double ny = Math.Sin(pitch);
            double cosPitch = Math.Cos(pitch);
            double nx = Math.Cos(phi) * cosPitch;
            double nz = Math.Sin(phi) * cosPitch;
            var n = new Vec3(nx, ny, nz).Normalized();
            planeNormals[8 + k] = n;
            double d = center.Dot(n) + rng.Range(0.66, 0.84);
            // Apex at (0, 0.75, 0) must never be clipped
            if (n.Y > 0) d = Math.Max(d, 0.75 * n.Y + 0.005);
            planeDists[8 + k] = d;
        }

        // 2 sub-horizontal bedding-parallel cleavage planes
        for (int k = 0; k < 2; k++)
        {
            double phi = rng.Range(0, 2 * Math.PI);
            double dip = rng.Range(0.04, 0.12);
            double ny = Math.Cos(dip);
            double sinDip = Math.Sin(dip);
            double nx = Math.Cos(phi) * sinDip;
            double nz = Math.Sin(phi) * sinDip;
            var n = new Vec3(nx, ny, nz).Normalized();
            planeNormals[12 + k] = n;
            planeDists[12 + k] = Math.Max(0.75 * n.Y + 0.005, rng.Range(0.76, 0.88));
        }

        Vec3 Deform(Vec3 p, bool isBottom, bool isShelf)
        {
            if (isBottom) return p;
            double groundWeight = Math.Clamp((p.Y - (-0.38)) / 0.10, 0.0, 1.0);
            if (groundWeight <= 1e-6) return p;

            const double bevelRadius = 0.045; // 4.5 cm beveled transition
            double maxH = double.MinValue;
            for (int k = 0; k < planeCount; k++)
            {
                double h = p.Dot(planeNormals[k]) - planeDists[k];
                if (h > maxH) maxH = h;
            }

            if (maxH <= -3.0 * bevelRadius) return p;

            double sumW = 0.0;
            double sumNx = 0.0, sumNy = 0.0, sumNz = 0.0;
            for (int k = 0; k < planeCount; k++)
            {
                double h = p.Dot(planeNormals[k]) - planeDists[k];
                double w = Math.Exp((h - maxH) / bevelRadius);
                sumW += w;
                sumNx += w * planeNormals[k].X;
                sumNy += w * planeNormals[k].Y;
                sumNz += w * planeNormals[k].Z;
            }

            double smoothH = maxH + bevelRadius * Math.Log(sumW);
            double push = (smoothH + Math.Sqrt(smoothH * smoothH + bevelRadius * bevelRadius)) * 0.5 - bevelRadius * 0.5;
            if (push <= 1e-6) return p;

            var effN = new Vec3(sumNx, sumNy, sumNz).Normalized();
            if (isShelf)
            {
                // Preserve horizontal shelf orientation while cleaving lateral boundary
                effN = new Vec3(effN.X, 0, effN.Z).Normalized();
            }

            var result = p - effN * (push * groundWeight);
            if (result.Y > 0.75) result = new Vec3(result.X, 0.75, result.Z);
            return result;
        }

        int faceIndex = 0;
        foreach (var face in profile.Triangles)
        {
            var a = face.A; var b = face.B; var c = face.C;
            var geometric = (b - a).Cross(c - a).Normalized();
            var outward = geometric.Dot((a + b + c) / 3) > 0 ? geometric : geometric * -1;
            bool isShelf = !face.Bottom && outward.Y >= 0.82;

            int[,] row = new int[divisions + 1, divisions + 1];
            for (int i = 0; i <= divisions; i++)
                for (int j = 0; j <= divisions - i; j++)
                {
                    var p = (a * (divisions - i - j) + b * i + c * j) / divisions;
                    var defP = Deform(p, face.Bottom, isShelf);
                    row[i, j] = mesh.AddVertex(defP, outward, palette[0], palette[1], palette[2], 1.0, defP.X, defP.Z, profile.Family, 0);
                }
            for (int i = 0; i < divisions; i++)
                for (int j = 0; j < divisions - i; j++)
                {
                    Primitives.TriangleFacing(mesh, row[i, j], row[i + 1, j], row[i, j + 1], outward);
                    if (i + j + 1 < divisions)
                        Primitives.TriangleFacing(mesh, row[i + 1, j], row[i + 1, j + 1], row[i, j + 1], outward);
                }
            faceIndex++;
        }

        // Cache initial outward normal per vertex
        var origOutward = new Vec3[mesh.VertexCount];
        for (int i = 0; i < mesh.VertexCount; i++) origOutward[i] = mesh.NormalAt(i);

        // Recompute smooth area-weighted vertex normals across beveled edges (preserving crease angle 55 deg)
        int triCount = mesh.Indices.Count / 3;
        var triOutwardNormals = new Vec3[triCount];
        var vertexTriangles = new List<int>[mesh.VertexCount];
        for (int i = 0; i < mesh.VertexCount; i++) vertexTriangles[i] = new List<int>(4);

        for (int t = 0; t < triCount; t++)
        {
            int i0 = mesh.Indices[t * 3];
            int i1 = mesh.Indices[t * 3 + 1];
            int i2 = mesh.Indices[t * 3 + 2];
            vertexTriangles[i0].Add(t);
            vertexTriangles[i1].Add(t);
            vertexTriangles[i2].Add(t);

            var p0 = mesh.Position(i0);
            var p1 = mesh.Position(i1);
            var p2 = mesh.Position(i2);
            // Godot winding is clockwise, so outward area-weighted normal is (p2 - p0) x (p1 - p0)
            triOutwardNormals[t] = (p2 - p0).Cross(p1 - p0);
        }

        // Spatial grouping for coincident vertices along subdivided face seams
        var posGroups = new Dictionary<(int, int, int), List<int>>();
        for (int i = 0; i < mesh.VertexCount; i++)
        {
            var p = mesh.Position(i);
            var key = ((int)Math.Round(p.X * 1000.0), (int)Math.Round(p.Y * 1000.0), (int)Math.Round(p.Z * 1000.0));
            if (!posGroups.TryGetValue(key, out var list))
            {
                list = new List<int>(4);
                posGroups[key] = list;
            }
            list.Add(i);
        }

        const double cosCrease = 0.573576436; // cos(55 deg)

        for (int i = 0; i < mesh.VertexCount; i++)
        {
            var p = mesh.Position(i);
            var key = ((int)Math.Round(p.X * 1000.0), (int)Math.Round(p.Y * 1000.0), (int)Math.Round(p.Z * 1000.0));

            var baseN = Vec3.Zero;
            foreach (int t in vertexTriangles[i]) baseN += triOutwardNormals[t];
            var unitBaseN = baseN.LengthSq > 1e-12 ? baseN.Normalized() : origOutward[i];

            var smoothN = Vec3.Zero;
            if (posGroups.TryGetValue(key, out var coincident))
            {
                foreach (int vCoincident in coincident)
                {
                    if ((mesh.Position(vCoincident) - p).LengthSq > 1e-6) continue;
                    foreach (int t in vertexTriangles[vCoincident])
                    {
                        var tn = triOutwardNormals[t];
                        double len = tn.Length;
                        if (len < 1e-12) continue;
                        var unitTn = tn / len;
                        if (unitBaseN.Dot(unitTn) >= cosCrease)
                        {
                            smoothN += tn;
                        }
                    }
                }
            }

            if (smoothN.LengthSq < 1e-12) smoothN = unitBaseN;
            var finalNormal = smoothN.Normalized();

            // Strict outward normal guarantee: normal must have positive dot with every incident triangle outward normal
            bool allPositive = true;
            foreach (int t in vertexTriangles[i])
            {
                if (triOutwardNormals[t].Dot(finalNormal) <= 1e-6)
                {
                    allPositive = false;
                    break;
                }
            }
            if (!allPositive)
            {
                finalNormal = unitBaseN;
                bool baseOk = true;
                foreach (int t in vertexTriangles[i])
                {
                    if (triOutwardNormals[t].Dot(finalNormal) <= 1e-6)
                    {
                        baseOk = false;
                        break;
                    }
                }
                if (!baseOk) finalNormal = origOutward[i];
            }

            mesh.Normals[i * 3] = (float)finalNormal.X;
            mesh.Normals[i * 3 + 1] = (float)finalNormal.Y;
            mesh.Normals[i * 3 + 2] = (float)finalNormal.Z;
        }

        // Bake local mean curvature / concavity into vertex COLOR.r (cavity AO) and COLOR.g (ridge exposure)
        var neighbors = new HashSet<int>[mesh.VertexCount];
        for (int i = 0; i < mesh.VertexCount; i++) neighbors[i] = new HashSet<int>();
        for (int t = 0; t < triCount; t++)
        {
            int i0 = mesh.Indices[t * 3];
            int i1 = mesh.Indices[t * 3 + 1];
            int i2 = mesh.Indices[t * 3 + 2];
            neighbors[i0].Add(i1); neighbors[i0].Add(i2);
            neighbors[i1].Add(i0); neighbors[i1].Add(i2);
            neighbors[i2].Add(i0); neighbors[i2].Add(i1);
        }

        for (int i = 0; i < mesh.VertexCount; i++)
        {
            var p = mesh.Position(i);
            var n = mesh.NormalAt(i);
            double curvSum = 0;
            int count = 0;
            foreach (int adj in neighbors[i])
            {
                var diff = mesh.Position(adj) - p;
                double len = diff.Length;
                if (len > 1e-6)
                {
                    curvSum += diff.Dot(n) / len;
                    count++;
                }
            }

            double kappa = count > 0 ? curvSum / count : 0.0;
            // kappa < 0 -> convex (ridge/edge), kappa > 0 -> concave (crevice/cavity)
            double ridge = kappa < -0.01 ? Math.Clamp(-kappa * 2.8, 0.0, 1.0) : 0.0;
            double cavity = kappa > 0.01 ? Math.Clamp(1.0 - kappa * 2.2, 0.45, 1.0) : 1.0;

            double groundDist = p.Y - (-0.38);
            if (groundDist < 0.15)
            {
                cavity *= Math.Clamp(0.55 + 0.45 * (groundDist / 0.15), 0.55, 1.0);
            }

            double layer = 0.88 + 0.12 * Math.Sin(p.Y * 19.0 + (double)(variantSeed % 11));
            mesh.SetColor(i, cavity, ridge, layer, 1.0);
        }

        return mesh;
    }

    private static MeshData LegacyRock(ulong variantSeed, int detail = 3)
    {
        var rng = Rng.Keyed(variantSeed, "rock.mesh", 0);
        var basePal = RockPalette[rng.NextInt(RockPalette.Length)];
        int family = rng.NextInt(3); // fractured block, layered slab, upright shard
        const int sides = 10;
        double phase = rng.Range(0, Math.PI * 2);
        double shearX = rng.Range(-0.12, 0.12), shearZ = rng.Range(-0.12, 0.12);
        double[] heights = family switch
        {
            0 => new[] { -0.38, -0.16, 0.22, 0.56 },
            1 => new[] { -0.38, -0.12, 0.29, 0.60 },
            _ => new[] { -0.38, -0.20, 0.34, 0.65 },
        };
        double[] radii = family switch
        {
            0 => new[] { 0.78, 1.0, 0.76, 0.38 },
            1 => new[] { 0.84, 1.0, 0.79, 0.44 },
            _ => new[] { 0.78, 0.97, 0.66, 0.23 },
        };
        var sector = new double[sides];
        var angular = new double[sides];
        for (int s = 0; s < sides; s++)
        {
            sector[s] = rng.Range(0.85, 1.07);
            angular[s] = phase + 2 * Math.PI * (s + rng.Range(-0.10, 0.10)) / sides;
        }
        var rings = new Vec3[heights.Length, sides];
        for (int r = 0; r < heights.Length; r++)
            for (int s = 0; s < sides; s++)
            {
                double y = heights[r] + rng.Range(-0.06, 0.06);
                // Whole-sector offsets make long fracture edges; slab bands step outward at each layer.
                double radius = radii[r] * sector[s] * rng.Range(0.96, 1.04);
                double a = angular[s];
                rings[r, s] = new Vec3(Math.Cos(a) * radius + y * shearX, y,
                    Math.Sin(a) * radius + y * shearZ);
            }
        var m = new MeshData();
        void Face(Vec3 a, Vec3 b, Vec3 c, Vec3 outward, double shade)
        {
            var rawCross = (b - a).Cross(c - a).Normalized();
            var n = rawCross.Dot(outward) >= 0 ? rawCross : rawCross * -1;
            var col = Primitives.Scale(basePal, shade);
            int start = m.AddVertex(a, n, col, 1, a.X, a.Z, family, 0);
            m.AddVertex(b, n, col, 1, b.X, b.Z, family, 0);
            m.AddVertex(c, n, col, 1, c.X, c.Z, family, 0);
            Primitives.TriangleFacing(m, start, start + 1, start + 2, outward);
        }
        for (int r = 0; r < heights.Length - 1; r++)
            for (int s = 0; s < sides; s++)
            {
                int next = (s + 1) % sides;
                var a = rings[r, s]; var b = rings[r, next];
                var c = rings[r + 1, next]; var d = rings[r + 1, s];
                var outwards = new Vec3(a.X + b.X + c.X + d.X, 0, a.Z + b.Z + c.Z);
                double shade = rng.Range(0.86, 1.10);
                Face(a, b, c, outwards, shade);
                Face(a, c, d, outwards, shade * rng.Range(0.96, 1.04));
            }
        var bottom = new Vec3(0, heights[0], 0);
        // Shared crest height is the placement and surface-query contract for every family.
        var top = new Vec3(0.75 * shearX, 0.75, 0.75 * shearZ);
        for (int s = 0; s < sides; s++)
        {
            int next = (s + 1) % sides;
            Face(bottom, rings[0, next], rings[0, s], new Vec3(0, -1, 0), 0.76);
            Face(top, rings[heights.Length - 1, s], rings[heights.Length - 1, next], new Vec3(0, 1, 0), rng.Range(0.98, 1.12));
        }
        return m;
    }

    /// <summary>Dense water-worn cobble or broad-cleaved mineral chip for gravel scatter.</summary>
    public static MeshData Pebble(ulong seed)
    {
        var rng = Rng.Keyed(seed, "pebble.mesh", 0);
        bool chip = rng.NextInt(4) != 0;
        var pigment = PebblePalette[rng.NextInt(PebblePalette.Length)];
        double rx = rng.Range(0.78, 1.05), rz = rng.Range(0.78, 1.05);
        const int sides = 32, bands = 11;
        double[] heights = { .75, .63, .47, .28, .08, -.10, -.22, -.31, -.35, -.375, -.38 };
        double[] radii = { .44, .66, .84, 1.0, 1.04, 1.0, .88, .70, .53, .31, .12 };
        var sector = new double[sides + 1];
        for (int j = 0; j < sides; j++) sector[j] = rng.Range(chip ? .88 : .96, chip ? 1.08 : 1.04);
        sector[sides] = sector[0];
        var mesh = new MeshData();
        int[,] ring = new int[bands, sides + 1];
        for (int i = 0; i < bands; i++)
            for (int j = 0; j <= sides; j++)
            {
                double angle = 2 * Math.PI * j / sides;
                double radius = radii[i] * sector[j];
                var p = new Vec3(Math.Cos(angle) * rx * radius, heights[i], Math.Sin(angle) * rz * radius);
                var n = new Vec3(p.X / (rx * rx), (i < 5 ? .45 : -.45) / .7, p.Z / (rz * rz)).Normalized();
                ring[i, j] = mesh.AddVertex(p, n, pigment, 1, (double)j / sides, (double)i / (bands - 1));
            }
        for (int i = 0; i < bands - 1; i++)
            for (int j = 0; j < sides; j++)
            {
                int a = ring[i, j], b = ring[i, j + 1], c = ring[i + 1, j + 1], d = ring[i + 1, j];
                var outward = new Vec3(mesh.Position(a).X + mesh.Position(b).X, 0,
                    mesh.Position(a).Z + mesh.Position(b).Z);
                Primitives.TriangleFacing(mesh, a, b, c, outward);
                Primitives.TriangleFacing(mesh, a, c, d, outward);
            }
        // Duplicate the rim for the chip cap so the entire cleavage plane has one upward normal.
        int topCenter = mesh.AddVertex(new Vec3(0, .75, 0), Vec3.Up, pigment, 1, 0, 0, 0, chip ? 1 : 0);
        int bottomCenter = mesh.AddVertex(new Vec3(0, -.38, 0), Vec3.Up * -1, pigment, 1);
        for (int j = 0; j < sides; j++)
        {
            int next = j + 1;
            var ta = mesh.Position(ring[0, j]); var tb = mesh.Position(ring[0, next]);
            int ia = mesh.AddVertex(ta, Vec3.Up, pigment, 1, ta.X, ta.Z, 0, chip ? 1 : 0);
            int ib = mesh.AddVertex(tb, Vec3.Up, pigment, 1, tb.X, tb.Z, 0, chip ? 1 : 0);
            Primitives.TriangleFacing(mesh, topCenter, ia, ib, Vec3.Up);
            var ba = mesh.Position(ring[bands - 1, j]); var bb = mesh.Position(ring[bands - 1, next]);
            int ja = mesh.AddVertex(ba, Vec3.Up * -1, pigment, 1);
            int jb = mesh.AddVertex(bb, Vec3.Up * -1, pigment, 1);
            Primitives.TriangleFacing(mesh, bottomCenter, jb, ja, Vec3.Up * -1);
        }
        return mesh;
    }

    private readonly record struct LogKnot(double T, double Th, double Radius, double Height, Vec3 Dir, bool HasStub);

    private static double WrapAngle(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    private static double FracturedEndSplinter(ulong seed, int end, double rFrac, double th, double length, double radius, int decay)
    {
        ulong endSeed = Rng.Mix(seed, (ulong)end * 0x9E3779B97F4A7C15UL + 0x454E44UL);
        if (decay >= 3)
        {
            const double hollowRadius = 0.62;
            if (rFrac < hollowRadius)
            {
                double cavityNorm = 1.0 - (rFrac / hollowRadius);
                double rotDepth = 0.065 * length + 0.020 * length * Noise.Gradient(endSeed, Math.Cos(th) * 2.5, Math.Sin(th) * 2.5);
                return -rotDepth * Math.Sqrt(cavityNorm);
            }
            double shellFrac = (rFrac - hollowRadius) / (1.0 - hollowRadius);
            double shellSplinter = 0.018 * length * (1.0 + Noise.Gradient(Rng.Mix(endSeed, 29), Math.Cos(th) * 4.0, Math.Sin(th) * 4.0));
            return Math.Min(shellSplinter * shellFrac, 0.055 * length);
        }

        double shearPhase = th + (end == 0 ? 0.8 : 2.4);
        double stepShear = Math.Sin(shearPhase) > 0.0 ? 0.020 * length : -0.005 * length;
        double grainA = Noise.Gradient(Rng.Mix(endSeed, 11), Math.Cos(th) * 5.0, Math.Sin(th) * 5.0);
        double grainB = Noise.Gradient(Rng.Mix(endSeed, 37), rFrac * 8.0 + Math.Cos(th) * 4.0, Math.Sin(th) * 4.0);
        double fiberNoise = (0.015 * length) * (grainA * 0.65 + grainB * 0.35);

        double spurProtrusion = 0;
        for (int sp = 0; sp < 3; sp++)
        {
            ulong spKey = Rng.Mix(endSeed, (ulong)(sp * 101 + 7));
            double spTh = ((spKey % 1000) / 1000.0) * 2 * Math.PI;
            double spR = 0.25 + (((spKey / 1000) % 1000) / 1000.0) * 0.55;
            double spLen = radius * (0.28 + (((spKey / 1000000) % 1000) / 1000.0) * 0.35);
            double dTh = WrapAngle(th - spTh);
            double dR = rFrac - spR;
            double d = Math.Sqrt(dTh * dTh * spR * spR + dR * dR);
            if (d < 0.25)
            {
                double h = spLen * (1.0 - d / 0.25);
                if (h > spurProtrusion) spurProtrusion = h;
            }
        }

        double total = 0.012 * length + stepShear + fiberNoise + spurProtrusion;
        return Math.Clamp(total, 0.002 * length, 0.055 * length);
    }

    /// <summary>
    /// Fallen log along local +X, centred at the origin (its axis at y = 0), built at real size.
    /// Bark ridges, knots/branch stubs, broken ends, decay sag and colour all derive from the seed + decay class.
    /// </summary>
    public static MeshData Log(LogProp log) => Log(log.VariantSeed, log.Length, log.Radius, log.DecayClass);

    public static MeshData Log(ulong seed, double length, double radius, int decay)
    {
        var rng = Rng.Keyed(seed, "log.mesh", 0);
        ulong ns = Rng.Mix(seed, 0x106);
        var m = new MeshData();
        const int around = 72;
        int along = Math.Max(12, (int)Math.Ceiling(length / 0.015));
        double barkDepth = radius * rng.Range(0.045, 0.075) * (decay >= 3 ? 0.55 : 1);
        double taper = rng.Range(0.04, 0.14), sag = decay * 0.010 * length;
        double flatten = decay >= 3 ? 0.80 : (decay == 2 ? 0.86 : 1.0);
        double bendY = rng.Range(-0.025, 0.025) * length, bendZ = rng.Range(-0.035, 0.035) * length;
        double phaseY = rng.Range(0, Math.PI * 2), phaseZ = rng.Range(0, Math.PI * 2);

        double[] bark = decay switch
        {
            0 => new[] { 0.40, 0.29, 0.19 },
            1 => new[] { 0.46, 0.42, 0.36 },
            2 => new[] { 0.44, 0.30, 0.20 },
            _ => new[] { 0.28, 0.18, 0.11 },
        };
        bark = Primitives.Scale(bark, rng.Range(0.92, 1.08));
        var heart = decay switch
        {
            0 => new[] { 0.82, 0.70, 0.48 },
            1 => new[] { 0.70, 0.63, 0.48 },
            2 => new[] { 0.58, 0.48, 0.32 },
            _ => new[] { 0.32, 0.22, 0.14 },
        };
        var sapwood = new[] { 0.72, 0.60, 0.44 };
        var weatheredWood = new[] { 0.62, 0.54, 0.42 };
        var punkWood = new[] { 0.22, 0.14, 0.08 };
        var cavityWood = new[] { 0.10, 0.07, 0.04 };
        var moss = new[] { 0.26, 0.58, 0.18 };

        // Knots: branch junctions with swelling collars and optional broken stubs
        var knotRng = Rng.Keyed(seed, "log.knots", 0);
        int knotCount = 2 + knotRng.NextInt(3);
        var knots = new List<LogKnot>(knotCount);
        for (int k = 0; k < knotCount; k++)
        {
            double kt = knotRng.Range(0.20, 0.80);
            double kth = knotRng.Range(0, 2 * Math.PI);
            double kr = radius * knotRng.Range(0.25, 0.40);
            double kh = radius * knotRng.Range(0.18, 0.32);
            var kdir = new Vec3(knotRng.Range(-0.2, 0.2), Math.Cos(kth) * flatten, Math.Sin(kth)).Normalized();
            bool hasStub = (decay <= 1 && knotRng.Chance(0.70)) || (decay == 2 && knotRng.Chance(0.35));
            knots.Add(new LogKnot(kt, kth, kr, kh, kdir, hasStub));
        }

        Vec3 AxisPoint(double t) => new(
            (t - 0.5) * length,
            -sag * Math.Sin(Math.PI * t) + bendY * Math.Sin(Math.PI * t) * Math.Sin(t * Math.PI * 1.7 + phaseY),
            bendZ * Math.Sin(Math.PI * t) * Math.Sin(t * Math.PI * 1.35 + phaseZ));

        double RadiusAt(double t) => radius * (1.0 - taper * t) * (0.97 + 0.05 * Noise.Value3(Rng.Mix(ns, 91), t * 3.2, 0.4, 0.2));

        int ringStart = m.VertexCount;
        for (int i = 0; i <= along; i++)
        {
            double t = (double)i / along;
            var c = AxisPoint(t);
            double rBase = RadiusAt(t);

            for (int s = 0; s <= around; s++)
            {
                double th = s == around ? 0 : 2 * Math.PI * s / around;

                // Knot swelling and grain deflection around knots
                double knotSwell = 0;
                double thDeflect = 0;
                for (int k = 0; k < knots.Count; k++)
                {
                    var knot = knots[k];
                    double dt = (t - knot.T) * length;
                    double dTh = WrapAngle(th - knot.Th);
                    double dyz = dTh * rBase;
                    double dist = Math.Sqrt(dt * dt + dyz * dyz);
                    double collarRadius = knot.Radius * 2.5;
                    if (dist < collarRadius)
                    {
                        double u = dist / collarRadius;
                        double w = (1.0 - u * u) * (1.0 - u);
                        knotSwell += knot.Height * w;

                        if (dist > 1e-4)
                        {
                            double angleToKnot = Math.Atan2(dyz, dt);
                            thDeflect += 0.30 * (1.0 - u) * Math.Sin(angleToKnot);
                        }
                    }
                }

                // Longitudinal bark furrowing (ridges and grooves running along length)
                double thGrain = th + thDeflect;
                const int furrowCount = 14;
                double furrowPhase = thGrain * (furrowCount / (2 * Math.PI)) + Noise.Gradient(ns, t * length * 1.2, thGrain * 0.8) * 0.40;
                double furrowDist = Math.Abs(Math.Sin(furrowPhase * Math.PI));
                double furrowProfile = Math.Pow(furrowDist, 0.55) * 1.8 - 0.9;
                double fineGrain = Noise.Gradient(Rng.Mix(ns, 47), t * length * 6.0, thGrain * 3.0) * 0.20;
                double barkRidge = (furrowProfile + fineGrain) * barkDepth;

                // Progressive decay class features
                bool isPeelingFissure = false;
                if (decay == 2)
                {
                    double fissureNoise = Noise.Gradient(Rng.Mix(ns, 113), t * length * 1.8, thGrain * 1.5);
                    if (fissureNoise > 0.12 && furrowDist < 0.42)
                    {
                        isPeelingFissure = true;
                        barkRidge -= barkDepth * 1.35 * MathD.SmoothStep(0.12, 0.40, fissureNoise);
                    }
                    else if (fissureNoise > 0.04 && furrowDist >= 0.42 && furrowDist < 0.62)
                    {
                        barkRidge += barkDepth * 0.45 * MathD.SmoothStep(0.04, 0.22, fissureNoise);
                    }
                }

                double rotTrough = 0;
                bool isPunkWood = false;
                if (decay >= 3)
                {
                    double slough = Noise.Gradient(Rng.Mix(ns, 151), t * length * 1.5, thGrain * 1.2);
                    if (slough > -0.22)
                    {
                        isPunkWood = true;
                        barkRidge = -barkDepth * 0.65;
                    }
                    if (Math.Cos(th) > 0.30)
                    {
                        double rotNoise = Noise.Gradient(Rng.Mix(ns, 199), t * length * 1.3, 0.5);
                        rotTrough = MathD.SmoothStep(0.30, 0.85, Math.Cos(th)) * (radius * 0.32) * MathD.SmoothStep(-0.2, 0.5, rotNoise);
                    }
                }

                double rr = Math.Max(radius * 0.15, rBase + knotSwell + barkRidge - rotTrough);
                var dir = new Vec3(0, Math.Cos(th) * flatten, Math.Sin(th));

                double xPos = c.X;
                if (i == 0)
                {
                    double rimSplinter = FracturedEndSplinter(seed, 0, 1.0, th, length, radius, decay);
                    xPos = -0.5 * length - rimSplinter;
                }
                else if (i == along)
                {
                    double rimSplinter = FracturedEndSplinter(seed, 1, 1.0, th, length, radius, decay);
                    xPos = 0.5 * length + rimSplinter;
                }
                var p = new Vec3(xPos, c.Y + dir.Y * rr, c.Z + dir.Z * rr);

                // Vertex shading and coloring
                double shade = 0.82 + 0.28 * furrowDist;
                var col = Primitives.Scale(bark, shade);
                if (isPeelingFissure)
                {
                    col = Primitives.Mix(sapwood, weatheredWood, 0.35);
                }
                else if (isPunkWood || rotTrough > 0)
                {
                    col = Primitives.Scale(punkWood, 0.85 + 0.25 * Noise.Gradient(ns, t * 5.0, th * 3.5));
                }

                if (decay >= 2 && Math.Cos(th) > 0.30)
                {
                    double patch = MathD.SmoothStep(0.08, 0.48, Noise.Gradient(Rng.Mix(ns, 3), t * length * 2.5, th * 2.0));
                    col = Primitives.Mix(col, moss, patch * (decay >= 3 ? 0.75 : 0.50));
                }

                m.AddVertex(p, dir, col, 1, t * length, (2 * Math.PI * s / around) * rBase, decay, 0);
            }
        }

        // Trunk cylinder quads
        for (int i = 0; i < along; i++)
        {
            for (int s = 0; s < around; s++)
            {
                int a = ringStart + i * (around + 1) + s, b = a + around + 1;
                m.AddTriangle(a, b, a + 1);
                m.AddTriangle(a + 1, b, b + 1);
            }
        }

        // The cavity runs through the full axis; end annuli join the actual displaced bark edge.
        // Material IDs: 0 bark, 1 end grain, 2 cavity (COLOR.r = AO), 3 splinter.
        const int innerSides = 48;
        double InnerFraction(double th) => (decay >= 3 ? 0.48 : 0.39)
            + 0.025 * Math.Sin(th * 9 + (seed % 37));
        Vec3 InnerPoint(double t, double th)
        {
            double r = RadiusAt(t) * InnerFraction(th);
            // Long continuous heartwood ribs, with subtle axial rot waviness.
            r *= 1 + 0.045 * Math.Sin(th * 17 + t * 2.2);
            var c = AxisPoint(t);
            double x = c.X;
            if (t == 0 || t == 1)
                x += (t == 0 ? -1 : 1) * Math.Abs(FracturedEndSplinter(seed, (int)t, InnerFraction(th), th, length, radius, decay));
            return new Vec3(x, c.Y + Math.Cos(th) * r * flatten, c.Z + Math.Sin(th) * r);
        }
        int cavityStart = m.VertexCount;
        if (decay >= 2)
        {
            for (int i = 0; i <= along; i++)
            {
                double t = (double)i / along;
                double depth = Math.Min(t, 1 - t) * length;
                double ao = 0.08 + 0.92 * Math.Exp(-depth / Math.Max(radius * 0.45, 0.01));
                for (int j = 0; j <= innerSides; j++)
                {
                    double th = j * 2 * Math.PI / innerSides;
                    m.AddVertex(InnerPoint(t, th), new Vec3(0, -Math.Cos(th), -Math.Sin(th)),
                        ao, ao, ao, 1, InnerFraction(th), th, decay, 2);
                }
            }
            for (int i = 0; i < along; i++)
                for (int j = 0; j < innerSides; j++)
                {
                    int a = cavityStart + i * (innerSides + 1) + j, b = a + innerSides + 1;
                    m.AddTriangle(a, a + 1, b);
                    m.AddTriangle(a + 1, b + 1, b);
                }
        }

        for (int end = 0; end < 2; end++)
        {
            var endNorm = new Vec3(end == 0 ? -1 : 1, 0, 0);
            var cEnd = AxisPoint(end);
            double rEnd = RadiusAt(end);
            int outerStart = ringStart + end * along * (around + 1);
            int prev = -1;
            const int rimRings = 6;
            for (int r = 0; r <= (decay >= 2 ? rimRings - 1 : rimRings); r++)
            {
                int row = m.VertexCount;
                for (int j = 0; j <= around; j++)
                {
                    double th = j * 2 * Math.PI / around;
                    double f = (double)r / rimRings;
                    var outside = m.Position(outerStart + j);
                    var inside = decay >= 2 ? InnerPoint(end, th) : cEnd;
                    // Splintered annulus, with continuous boundaries at bark and tunnel.
                    var p = outside * (1 - f) + inside * f;
                    p += endNorm * (Math.Sin(Math.PI * f) * radius * 0.075 * Math.Sin(th * 19 + seed % 101));
                    double rFrac = 1 - f * (1 - (decay >= 2 ? InnerFraction(th) : 0));
                    m.AddVertex(p, endNorm, heart, 1, rFrac, th, decay, 1);
                }
                if (prev >= 0)
                    for (int j = 0; j < around; j++)
                    {
                        // Solid logs finish in a fan, avoiding collapsed centre triangles.
                        if (decay < 2 && r == rimRings)
                            Primitives.TriangleFacing(m, prev + j, row + j, prev + j + 1, endNorm);
                        else
                        {
                            Primitives.TriangleFacing(m, prev + j, row + j, row + j + 1, endNorm);
                            Primitives.TriangleFacing(m, prev + j, row + j + 1, prev + j + 1, endNorm);
                        }
                    }
                prev = row;
            }
            if (decay >= 2)
            {
                // Zipper triangulation joins the 72-sided annulus to the 48-sided tunnel.
                // Use the tunnel vertices themselves for the last rim row, so no radial cracks remain.
                int cavityRow = cavityStart + end * along * (innerSides + 1);
                int innerRow = m.VertexCount;
                for (int q = 0; q <= innerSides; q++)
                    m.AddVertex(m.Position(cavityRow + q), endNorm, heart, 1,
                        InnerFraction(q * 2 * Math.PI / innerSides), q * 2 * Math.PI / innerSides, decay, 1);
                int beforeLast = prev;
                int j = 0, k = 0;
                while (j < around || k < innerSides)
                {
                    if (j < around && (k == innerSides || (double)(j + 1) / around <= (double)(k + 1) / innerSides))
                    {
                        Primitives.TriangleFacing(m, beforeLast + j, innerRow + k, beforeLast + j + 1, endNorm);
                        j++;
                    }
                    else
                    {
                        Primitives.TriangleFacing(m, beforeLast + j, innerRow + k, innerRow + k + 1, endNorm);
                        k++;
                    }
                }
            }
            // Six-sided tapered wood fibers; their UVs follow length and circumference.
            int tineCount = 12 + (int)(Rng.Mix(seed, (ulong)end + 71) % 13);
            for (int tine = 0; tine < tineCount; tine++)
            {
                ulong key = Rng.Mix(seed, (ulong)(end * 101 + tine + 1));
                double th = 2 * Math.PI * tine / tineCount + (key % 1000) * 0.0003;
                double frac = decay >= 2 ? 0.69 + (key % 100) * 0.0015 : 0.28 + (key % 100) * 0.006;
                var baseP = cEnd + new Vec3(0, Math.Cos(th) * rEnd * frac * flatten, Math.Sin(th) * rEnd * frac);
                baseP += endNorm * FracturedEndSplinter(seed, end, frac, th, length, radius, decay);
                double shardLength = radius * (0.18 + (key % 1000) * 0.00035);
                double width = radius * (0.012 + (key % 97) * 0.00022);
                var sideA = new Vec3(0, -Math.Sin(th), Math.Cos(th));
                var sideB = new Vec3(0, Math.Cos(th), Math.Sin(th));
                int start = m.VertexCount;
                for (int row = 0; row < 3; row++)
                    for (int j = 0; j <= 6; j++)
                    {
                        double angle = j * 2 * Math.PI / 6;
                        var n = sideA * Math.Cos(angle) + sideB * Math.Sin(angle);
                        double taperShard = row == 0 ? 1 : row == 1 ? 0.52 : 0.03;
                        var p = baseP + endNorm * (shardLength * row / 2) + n * (width * taperShard)
                            + sideA * (row * row * width * 0.13);
                        m.AddVertex(p, n, heart, 1, row * shardLength / 2, angle * width, decay, 3);
                    }
                for (int row = 0; row < 2; row++)
                    for (int j = 0; j < 6; j++)
                    {
                        int a = start + row * 7 + j, b = a + 7;
                        var outward = m.NormalAt(a);
                        Primitives.TriangleFacing(m, a, b, a + 1, outward);
                        Primitives.TriangleFacing(m, a + 1, b, b + 1, outward);
                    }
                int tip = m.AddVertex(baseP + endNorm * shardLength + sideA * (4 * width * 0.13), endNorm,
                    heart, 1, shardLength, 0, decay, 3);
                for (int j = 0; j < 6; j++)
                    Primitives.TriangleFacing(m, tip, start + 14 + j, start + 15 + j, endNorm);
            }
        }

        // Branch stubs and knot collars
        for (int k = 0; k < knotCount; k++)
        {
            var knot = knots[k];
            if (knot.HasStub)
            {
                double t = knot.T;
                var baseP = AxisPoint(t);
                double rAtT = RadiusAt(t);
                double collarR = rAtT + knot.Height * 0.85;
                double stubLen = radius * rng.Range(0.35, 0.65);
                double stubRad = knot.Radius * rng.Range(0.35, 0.55);

                var dir = knot.Dir;
                var p0 = baseP + dir * (collarR * 0.65);
                var p1 = baseP + dir * (collarR * 0.95);
                var p2 = baseP + dir * (collarR + stubLen * 0.50);
                var p3 = baseP + dir * (collarR + stubLen);
                var path = new List<Vec3> { p0, p1, p2, p3 };
                var radii = new[] { stubRad * 1.4, stubRad * 1.15, stubRad * 0.8, stubRad * 0.4 };
                Primitives.Tube(m, path, radii, 8, (idx, v) => (Primitives.Scale(bark, 0.94), 1, idx, v, decay, 0));

                // Broken splintered branch tip
                var tipCenter = p3;
                var tipNorm = dir;
                int tipStart = m.VertexCount;
                // Tagged 3: broken branch-stub tip, not a log-end cap face (see tine comment above).
                m.AddVertex(tipCenter + tipNorm * (stubRad * 0.35), tipNorm, Primitives.Scale(heart, 0.85), 1, 0, 0, decay, 3);
                for (int s = 0; s <= 8; s++)
                {
                    double a = 2 * Math.PI * s / 8;
                    var sideDir = (new Vec3(-dir.Z, 0, dir.X).Normalized() * Math.Cos(a) + new Vec3(0, 1, 0) * Math.Sin(a)).Normalized();
                    double splinter = stubRad * 0.30 * Math.Abs(Math.Sin(a * 2.5 + k));
                    var pt = tipCenter + sideDir * (stubRad * 0.40) + tipNorm * splinter;
                    m.AddVertex(pt, tipNorm, Primitives.Scale(heart, 0.80), 1, Math.Cos(a), Math.Sin(a), decay, 3);
                }
                for (int s = 0; s < 8; s++)
                {
                    Primitives.TriangleFacing(m, tipStart, tipStart + 1 + s, tipStart + 1 + s + 1, tipNorm);
                }
            }
        }

        // Area weighting after all displacement. Weld duplicated UV seams within a material only.
        var sums = new Dictionary<(long X, long Y, long Z, int Material), Vec3>();
        (long, long, long, int) NormalKey(int i)
        {
            var p = m.Position(i);
            return ((long)Math.Round(p.X * 1e6), (long)Math.Round(p.Y * 1e6),
                (long)Math.Round(p.Z * 1e6), (int)m.UV2[i * 2 + 1]);
        }
        for (int i = 0; i < m.Indices.Count; i += 3)
        {
            int a = m.Indices[i], b = m.Indices[i + 1], c = m.Indices[i + 2];
            // Godot front faces are clockwise: visible normal is the negative cross product.
            var n = (m.Position(c) - m.Position(a)).Cross(m.Position(b) - m.Position(a));
            foreach (int v in new[] { a, b, c })
            {
                var key = NormalKey(v);
                sums.TryGetValue(key, out var total);
                sums[key] = total + n;
            }
        }
        for (int i = 0; i < m.VertexCount; i++)
        {
            var n = sums.TryGetValue(NormalKey(i), out var total) && total.LengthSq > 1e-20
                ? total.Normalized() : m.NormalAt(i).Normalized();
            m.Normals[i * 3] = (float)n.X;
            m.Normals[i * 3 + 1] = (float)n.Y;
            m.Normals[i * 3 + 2] = (float)n.Z;
        }
        return m;
    }
    public readonly record struct PebbleInstance(Vec3 Position, Vec3 Scale, double RotationY, double TiltX, double TiltZ, int Variant);

    /// <summary>Render-only clustered gravel with voids, burial and full 3-D orientation.</summary>
    public static List<PebbleInstance> GravelScatter(VivariumWorld w, GravelPatch g, double density = 650, int variants = 32)
    {
        var rng = Rng.Keyed(g.VariantSeed, "gravel.scatter", 0);
        int count = (int)(Math.PI * g.Radius * g.Radius * density);
        var list = new List<PebbleInstance>(count);
        var clusters = new List<Vec2>();
        for (int c = 0; c < 5; c++)
            for (int tries = 0; tries < 12; tries++)
            {
                double a = rng.Range(0, Math.PI * 2), r = Math.Sqrt(rng.NextDouble()) * g.Radius * 0.82;
                var p = g.Position + Vec2.FromAngle(a) * r;
                if (g.Covers(p)) { clusters.Add(p); break; }
            }
        if (clusters.Count == 0) clusters.Add(g.Position);

        for (int i = 0; i < count * 5 && list.Count < count; i++)
        {
            Vec2 p;
            if (rng.Chance(0.76))
            {
                var c = clusters[rng.NextInt(clusters.Count)];
                double a = rng.Range(0, Math.PI * 2);
                double spread = g.Radius * (0.08 + 0.28 * Math.Pow(rng.NextDouble(), 1.8));
                p = c + Vec2.FromAngle(a) * spread;
            }
            else
            {
                double a = rng.Range(0, Math.PI * 2), r = Math.Sqrt(rng.NextDouble()) * g.Radius;
                p = g.Position + Vec2.FromAngle(a) * r;
            }
            if (!g.Covers(p) || !w.Domain.ContainsDisc(p, 0.02)) continue;
            double s;
            if (rng.Chance(0.10))
                s = rng.Range(0.025, 0.045); // large cobble 2.5-4.5 cm
            else
                s = rng.Range(0.004, 0.012); // interstitial grain 4-12 mm
            double sx = s * rng.Range(0.78, 1.25), sy = s * rng.Range(0.25, 0.40), sz = s * rng.Range(0.8, 1.3);
            double burial = rng.Range(0.12, 0.42);
            var pos = new Vec3(p.X, w.Terrain.Height(p) + sy * (0.38 - burial), p.Z);
            list.Add(new PebbleInstance(pos, new Vec3(sx, sy, sz), rng.Range(0, 2 * Math.PI),
                rng.Range(-0.20, 0.20), rng.Range(-0.20, 0.20), rng.NextInt(variants)));
        }
        return list;
    }

    /// <summary>
    /// Dark rocky spring orifice aperture: a ring/collar of dark stone facets around a sunken dark center aperture.
    /// Outer radius ~0.08m to 0.12m, with a sunken conduit center.
    /// </summary>
    public static MeshData SpringVent(ulong variantSeed = 0)
    {
        var rng = Rng.Keyed(variantSeed, "prop.spring_vent", 0);
        var m = new MeshData();

        // Dark basalt/slate palette
        var stonePalette = new[] { 0.20, 0.20, 0.21 };
        var sunkenPalette = new[] { 0.04, 0.04, 0.05 };

        const int sides = 10;
        double phase = rng.Range(0, Math.PI * 2);

        // Ring dimensions (m)
        // Outer collar radius ~0.08m to 0.12m
        double[] outerRadii = new double[sides];
        double[] crestRadii = new double[sides];
        double[] innerRadii = new double[sides];
        double[] angles = new double[sides];

        for (int s = 0; s < sides; s++)
        {
            angles[s] = phase + 2 * Math.PI * (s + rng.Range(-0.08, 0.08)) / sides;
            outerRadii[s] = rng.Range(0.088, 0.112);
            crestRadii[s] = rng.Range(0.050, 0.065);
            innerRadii[s] = rng.Range(0.028, 0.038);
        }

        var outerRing = new Vec3[sides];
        var crestRing = new Vec3[sides];
        var innerRing = new Vec3[sides];

        for (int s = 0; s < sides; s++)
        {
            double a = angles[s];
            double ca = Math.Cos(a);
            double sa = Math.Sin(a);

            // Outer base sits slightly in terrain
            double yOuter = rng.Range(-0.008, 0.002);
            outerRing[s] = new Vec3(ca * outerRadii[s], yOuter, sa * outerRadii[s]);

            // Raised collar crest lip
            double yCrest = rng.Range(0.012, 0.022);
            crestRing[s] = new Vec3(ca * crestRadii[s], yCrest, sa * crestRadii[s]);

            // Inner rim starts dropping into hole
            double yInner = rng.Range(-0.010, -0.004);
            innerRing[s] = new Vec3(ca * innerRadii[s], yInner, sa * innerRadii[s]);
        }

        // Sunken dark center aperture point
        double yCenter = rng.Range(-0.040, -0.032);
        var centerPos = new Vec3(0, yCenter, 0);

        void Face(Vec3 a, Vec3 b, Vec3 c, Vec3 outward, double[] baseCol, double shade)
        {
            var rawCross = (b - a).Cross(c - a).Normalized();
            var n = rawCross.Dot(outward) >= 0 ? rawCross : rawCross * -1;
            var col = Primitives.Scale(baseCol, shade);
            int start = m.AddVertex(a, n, col, 1, a.X, a.Z, 0, 0);
            m.AddVertex(b, n, col, 1, b.X, b.Z, 0, 0);
            m.AddVertex(c, n, col, 1, c.Z, c.Z, 0, 0);
            Primitives.TriangleFacing(m, start, start + 1, start + 2, outward);
        }

        for (int s = 0; s < sides; s++)
        {
            int next = (s + 1) % sides;

            // 1. Outer collar facets (outer to crest)
            var o0 = outerRing[s]; var o1 = outerRing[next];
            var c0 = crestRing[s]; var c1 = crestRing[next];
            var outwardCollar = new Vec3(o0.X + o1.X + c0.X + c1.X, 1.0, o0.Z + o1.Z + c0.Z + c1.Z);
            double shadeCollar = rng.Range(0.85, 1.15);
            Face(o0, o1, c1, outwardCollar, stonePalette, shadeCollar);
            Face(o0, c1, c0, outwardCollar, stonePalette, shadeCollar * rng.Range(0.95, 1.05));

            // 2. Collar crest to inner rim (crest to inner)
            var i0 = innerRing[s]; var i1 = innerRing[next];
            var inwardSlope = new Vec3(0, 1.0, 0);
            double shadeInnerSlope = rng.Range(0.65, 0.85);
            Face(c0, c1, i1, inwardSlope, stonePalette, shadeInnerSlope);
            Face(c0, i1, i0, inwardSlope, stonePalette, shadeInnerSlope * rng.Range(0.95, 1.05));

            // 3. Sunken dark center aperture cone (inner rim to center point)
            var inwardCenter = new Vec3(0, 1.0, 0);
            double shadeAperture = rng.Range(0.80, 1.00);
            Face(i0, i1, centerPos, inwardCenter, sunkenPalette, shadeAperture);

            // 4. Base skirt underneath facing downward
            var bottomCenter = new Vec3(0, -0.015, 0);
            Face(bottomCenter, o1, o0, new Vec3(0, -1.0, 0), stonePalette, 0.50);
        }

        return m;
    }

    /// <summary>Build procedural geometry for a prop kind.</summary>
    public static MeshData Build(PropKind kind, ulong seed = 0) => kind switch
    {
        PropKind.SpringVent => SpringVent(seed),
        _ => new MeshData()
    };

    /// <summary>Build procedural geometry for a prop instance.</summary>
    public static MeshData Build(Prop prop) => Build(prop.Kind, prop.VariantSeed);
}
