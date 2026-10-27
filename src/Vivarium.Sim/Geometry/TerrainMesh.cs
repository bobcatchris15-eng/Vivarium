using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Builds the island's render geometry from authoritative terrain: the top surface clipped exactly to the
/// regular hexagon, the six vertical strata walls, and the flat underside.
/// </summary>
public static class TerrainMesh
{
    /// <summary>
    /// Subdivided terrain triangles. In physical mode (renderSmoothing = false), geometry matches
    /// fluid support exactly (< 2 µm). In render mode (renderSmoothing = true), bicubic Catmull-Rom
    /// smoothing plus micro-relief hummocks (±3.5 mm) eliminate polygonal facet lines.

    /// Smooth normals soften lighting without moving the contact surface.
    /// </summary>
    public static TopMesh BuildTop(VivariumWorld w, bool renderSmoothing = true)
    {
        var hf = w.Terrain;
        var dom = w.Domain;
        var mesh = new MeshData();
        var xz = new List<Vec2>();
        var shared = new Dictionary<int, int>();

        const int sub = 6;
        double step = hf.Step / sub;
        int nx = (hf.Nx - 1) * sub + 1;
        int nz = (hf.Nz - 1) * sub + 1;

        Vec2 Pos(int i, int j) => new(hf.OriginX + i * step, hf.OriginZ + j * step);
        int Vertex(Vec2 p, int key = -1)
        {
            if (key >= 0 && shared.TryGetValue(key, out int existing)) return existing;
            double h = renderSmoothing ? RenderHeight(hf, p) : hf.Height(p);
            var n = renderSmoothing ? RenderNormal(hf, p) : hf.Normal(p);
            int idx = mesh.AddVertex(new Vec3(p.X, h, p.Z), n, 0.4, 0.3, 0.2, 1, p.X, p.Z);
            xz.Add(p);
            if (key >= 0) shared[key] = idx;
            return idx;
        }

        for (int j = 0; j < nz - 1; j++)
            for (int i = 0; i < nx - 1; i++)
            {
                var p00 = Pos(i, j); var p10 = Pos(i + 1, j);
                var p01 = Pos(i, j + 1); var p11 = Pos(i + 1, j + 1);
                int k00 = j * nx + i, k10 = k00 + 1, k01 = k00 + nx, k11 = k01 + 1;

                // Preserve the parent's (00,11) diagonal, including in every subdivision.
                AddClipped(dom, mesh, Vertex, new[] { p00, p10, p11 }, new[] { k00, k10, k11 });
                AddClipped(dom, mesh, Vertex, new[] { p00, p11, p01 }, new[] { k00, k11, k01 });
            }
        return new TopMesh(mesh, xz);
    }

    /// <summary>The visible bed and fluid/collision support share one elevation.</summary>
    private static double CatmullRom(double p0, double p1, double p2, double p3, double t)
    {
        double t2 = t * t;
        double t3 = t2 * t;
        return 0.5 * ((2.0 * p1) +
                      (-p0 + p2) * t +
                      (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2 +
                      (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3);
    }

    /// <summary>The visible bed evaluates C1-continuous bicubic height with subtle micro-relief hummocks.</summary>
    public static double RenderHeight(Heightfield hf, Vec2 p, bool smooth = true)
    {
        if (!smooth) return hf.Height(p);
        double fx = (p.X - hf.OriginX) / hf.Step;
        double fz = (p.Z - hf.OriginZ) / hf.Step;
        int i = (int)Math.Floor(fx);
        int j = (int)Math.Floor(fz);
        double tx = MathD.Clamp01(fx - i);
        double tz = MathD.Clamp01(fz - j);

        double r0 = CatmullRom(hf.Vertex(i - 1, j - 1), hf.Vertex(i, j - 1), hf.Vertex(i + 1, j - 1), hf.Vertex(i + 2, j - 1), tx);
        double r1 = CatmullRom(hf.Vertex(i - 1, j), hf.Vertex(i, j), hf.Vertex(i + 1, j), hf.Vertex(i + 2, j), tx);
        double r2 = CatmullRom(hf.Vertex(i - 1, j + 1), hf.Vertex(i, j + 1), hf.Vertex(i + 1, j + 1), hf.Vertex(i + 2, j + 1), tx);
        double r3 = CatmullRom(hf.Vertex(i - 1, j + 2), hf.Vertex(i, j + 2), hf.Vertex(i + 1, j + 2), hf.Vertex(i + 2, j + 2), tx);

        double smoothH = CatmullRom(r0, r1, r2, r3, tz);

        // Subtle deterministic micro-relief hummocks (within +/- 3.5 mm)
        double hummock = 0.0035 * Math.Clamp(
            0.7 * Noise.Gradient(0xC0FFEE_F00DUL, p.X * 5.5, p.Z * 5.5) +
            0.3 * Noise.Gradient(0xDEAD_BEEFUL, p.X * 12.0, p.Z * 12.0),
            -1.0, 1.0);

        return smoothH + hummock;
    }

    public static Vec3 RenderNormal(Heightfield hf, Vec2 p)
    {
        double e = Math.Min(hf.Step * 0.25, 0.03);
        double dx = (RenderHeight(hf, p + new Vec2(e, 0)) - RenderHeight(hf, p - new Vec2(e, 0))) / (2 * e);
        double dz = (RenderHeight(hf, p + new Vec2(0, e)) - RenderHeight(hf, p - new Vec2(0, e))) / (2 * e);
        return new Vec3(-dx, 1, -dz).Normalized();
    }

    private static void AddClipped(HexDomain dom, MeshData mesh, Func<Vec2, int, int> vertex, Vec2[] tri, int[] keys)
    {
        bool allIn = true, allOut = true;
        foreach (var p in tri) { double sd = dom.SignedDistance(p); if (sd > 0) allIn = false; else allOut = false; }
        if (allIn) { mesh.AddTriangle(vertex(tri[0], keys[0]), vertex(tri[1], keys[1]), vertex(tri[2], keys[2])); return; }
        if (allOut)
        {
            // a triangle can still overlap the hexagon near a corner even if all vertices are outside
            var probe = dom.ClipPolygon(tri);
            if (probe.Count < 3) return;
        }
        var poly = dom.ClipPolygon(tri);
        if (poly.Count < 3) return;
        var ids = new int[poly.Count];
        for (int k = 0; k < poly.Count; k++)
        {
            int key = -1;
            for (int t = 0; t < 3; t++) if (Vec2.DistanceSq(poly[k], tri[t]) < 1e-20) key = keys[t];
            ids[k] = vertex(poly[k], key);
        }
        for (int k = 1; k + 1 < poly.Count; k++)
        {
            // skip degenerate slivers
            var a = poly[0]; var b = poly[k]; var c = poly[k + 1];
            if (Math.Abs((b - a).Cross(c - a)) < 1e-12) continue;
            mesh.AddTriangle(ids[0], ids[k], ids[k + 1]);
        }
    }

    public static Vec3 SmoothNormal(Heightfield hf, Vec2 p) => RenderNormal(hf, p);

    /// <summary>
    /// Six vertical cut faces from the terrain edge down to the island bottom, banded by strata. Walls are
    /// tessellated at ~3 cm horizontally and eight rows per stratum vertically, then displaced outward with
    /// layer-specific relief (overhanging turf lip, blocky peds, cobbles, bedding ledges). Interior vertices
    /// ramp from zero at the living surface and island base so the rim and underside remain watertight.
    /// UV = (distance along perimeter, depth below surface); UV2 = (layer index, normalized depth within
    /// that layer). Colours come from content data. Woody flora near the rim also grow real 3D rootlets.
    /// </summary>
    public static MeshData BuildWalls(VivariumWorld w, double sampleStep = 0)
    {
        var hf = w.Terrain; var dom = w.Domain; var strata = w.Strata;
        double step = sampleStep > 0 ? sampleStep : hf.Step * 0.25;
        double bottom = hf.Bottom;
        var mesh = new MeshData();
        double perimeter = 0;
        int layers = Math.Max(1, strata.Layers.Count);
        const int rows = 8;

        // Blend layer relief across a narrow contact band. Both copies of a shared horizon vertex sample the
        // same (depth, world position), so the two bands resolve to the same displacement and never crack.
        double thinnest = strata.Layers[0].Thickness;
        for (int i = 1; i < layers; i++) thinnest = Math.Min(thinnest, strata.Layers[i].Thickness);
        double blend = Math.Min(0.012, Math.Max(0.002, thinnest * 0.12));

        // One deterministic relief seed per stratum; deeper custom layers reuse the bedrock field.
        var reliefSeeds = new ulong[Math.Max(layers, 4)];
        for (int i = 0; i < reliefSeeds.Length; i++)
            reliefSeeds[i] = Rng.Mix(w.Seed, Hash.Fnv1a64("strata.relief." + i));

        // Keep horizons continuous across all six faces by sampling in world XZ, not per-face UV.
        // Large, slowly changing folds give the cut an exposed geological profile while a strict
        // fraction-of-layer bound keeps adjacent horizons from crossing.
        double HorizonDepth(int boundary, Vec2 p)
        {
            if (boundary <= 0) return 0;
            double baseDepth = strata.LayerTop(boundary);
            ulong seed = Rng.Mix(w.Seed, Hash.Fnv1a64("strata.horizon." + boundary));
            double neighboringThickness = Math.Min(strata.Layers[boundary - 1].Thickness, strata.Layers[boundary].Thickness);
            // The living-soil contact can cut deeply into a thick A horizon; lower contacts retain
            // a wider clearance so even opposing folds leave each mineral layer intact.
            double fraction = boundary == 1 ? 0.72 : 0.42;
            double amp = Math.Min(neighboringThickness * fraction, 0.22 + baseDepth * 0.08);
            double broad = Noise.Fbm(seed, p.X * 0.19, p.Z * 0.19, 3);
            double secondary = Noise.Gradient(Rng.Mix(seed, 29), p.X * 0.47, p.Z * 0.47);
            double local = Noise.Gradient(Rng.Mix(seed, 71), p.X * 0.91, p.Z * 0.91);
            return Math.Max(0, baseDepth + amp * (broad * 0.72 + secondary * 0.22 + local * 0.06));
        }

        // Monotonic local contacts. Guarding each contact against the one above (rather than independently
        // per layer) guarantees the upper layer's bottom and the lower layer's top share one world Y.
        var bounds = new double[layers + 1];
        void FillBounds(Vec2 p, double surf, double[] dst)
        {
            dst[0] = 0;
            for (int b = 1; b < layers; b++)
                dst[b] = Math.Max(HorizonDepth(b, p), dst[b - 1] + 0.025);
            dst[layers] = Math.Max(surf - bottom, dst[layers - 1] + 0.025);
        }

        double Relief(int layer, double u, double depth, ulong seed) => layer switch
        {
            0 => TopsoilRelief(u, depth, seed),
            1 => SubsoilRelief(u, depth, seed),
            2 => GravelRelief(u, depth, seed),
            _ => BedrockRelief(u, depth, seed),
        };

        // Continuous outward displacement field. The relief is parameterised by (world-stable wall
        // coordinate, depth): a shared horizon vertex has identical inputs in both adjacent layers, so the
        // duplicated vertices land on exactly the same point.
        double Displace(Vec2 p, double depth, double y, double[] contacts)
        {
            double u = p.X * 0.5 + p.Z * 0.8660254037844386;
            double sum = 0, weightSum = 0;
            for (int i = 0; i < layers; i++)
            {
                double top = contacts[i], bot = contacts[i + 1];
                double wt = 1;
                if (i > 0) wt *= MathD.SmoothStep(top - blend, top + blend, depth);
                if (i < layers - 1) wt *= 1 - MathD.SmoothStep(bot - blend, bot + blend, depth);
                if (wt <= 1e-5) continue;
                sum += wt * Relief(i, u, depth, reliefSeeds[Math.Min(i, reliefSeeds.Length - 1)]);
                weightSum += wt;
            }
            if (weightSum <= 1e-9)
            {
                // Island thinner than the authored strata: fall back to the nominal layer at this depth.
                int li = Math.Clamp(strata.LayerAtDepth(depth), 0, layers - 1);
                sum = Relief(li, u, depth, reliefSeeds[Math.Min(li, reliefSeeds.Length - 1)]);
                weightSum = 1;
            }
            // Zero at the exact living surface and at the flat island base; ramp in over 1.5 cm.
            double taper = MathD.SmoothStep(0.0, 0.015, depth) * MathD.SmoothStep(0.0, 0.015, y - bottom);
            return taper * sum / weightSum;
        }

        var edgeStarts = new double[7];
        for (int k = 0; k < 6; k++)
        {
            edgeStarts[k] = perimeter;
            Vec2 a = dom.Vertices[k], b = dom.Vertices[(k + 1) % 6];
            var outward2 = dom.EdgeNormals[k];
            var outward = new Vec3(outward2.X, 0, outward2.Z);
            double len = Vec2.Distance(a, b);
            int segs = Math.Max(1, (int)Math.Ceiling(len / step));

            var ids = new int[layers][][];
            for (int l = 0; l < layers; l++)
            {
                ids[l] = new int[rows + 1][];
                for (int r = 0; r <= rows; r++) ids[l][r] = new int[segs + 1];
            }

            var colNormals = new Vec2[segs + 1];
            for (int s = 0; s <= segs; s++)
            {
                double t = (double)s / segs;
                var p = Vec2.Lerp(a, b, t);
                double surf = RenderHeight(hf, p);
                FillBounds(p, surf, bounds);
                var dispNormal = dom.BoundaryNormal(p);
                colNormals[s] = dispNormal;
                double u = perimeter + t * len;
                for (int layer = 0; layer < layers; layer++)
                {
                    var col = strata.Layers[layer].Color;
                    double yTop = Math.Max(surf - bounds[layer], bottom);
                    double yBot = Math.Max(surf - bounds[layer + 1], bottom);
                    for (int r = 0; r <= rows; r++)
                    {
                        double v = (double)r / rows;
                        double y = MathD.Lerp(yTop, yBot, v);
                        double depth = surf - y;
                        double d = Displace(p, depth, y, bounds);
                        var pos = new Vec3(p.X + dispNormal.X * d, y, p.Z + dispNormal.Z * d);
                        ids[layer][r][s] = mesh.AddVertex(pos, outward, col, 1, u, depth, layer, v);
                    }
                }
            }

            // Clockwise front faces for Godot: the right-hand normal of each triangle points into the island.
            for (int layer = 0; layer < layers; layer++)
                for (int r = 0; r < rows; r++)
                    for (int s = 0; s < segs; s++)
                    {
                        int v00 = ids[layer][r][s], v10 = ids[layer][r][s + 1];
                        int v01 = ids[layer][r + 1][s], v11 = ids[layer][r + 1][s + 1];
                        mesh.AddTriangle(v00, v01, v11);
                        mesh.AddTriangle(v00, v11, v10);
                    }

            // Area-weighted normals within each layer grid, then one gentle smoothing pass. Layer and
            // corner boundaries are not cross-smoothed (their vertices are unique), so contacts and the
            // six cut corners keep a crease while the displaced surface itself reads smooth.
            for (int layer = 0; layer < layers; layer++)
            {
                int cols = segs + 1;
                var acc = new Vec3[(rows + 1) * cols];
                for (int r = 0; r < rows; r++)
                    for (int s = 0; s < segs; s++)
                    {
                        int v00 = ids[layer][r][s], v10 = ids[layer][r][s + 1];
                        int v01 = ids[layer][r + 1][s], v11 = ids[layer][r + 1][s + 1];
                        var p00 = mesh.Position(v00); var p10 = mesh.Position(v10);
                        var p01 = mesh.Position(v01); var p11 = mesh.Position(v11);
                        var c1 = (p01 - p00).Cross(p11 - p00); // inward-facing right-hand normal
                        var c2 = (p11 - p00).Cross(p10 - p00);
                        int i00 = r * cols + s, i10 = i00 + 1, i01 = i00 + cols, i11 = i01 + 1;
                        acc[i00] -= c1; acc[i01] -= c1; acc[i11] -= c1;
                        acc[i00] -= c2; acc[i11] -= c2; acc[i10] -= c2;
                    }
                var n0 = new Vec3[(rows + 1) * cols];
                for (int i = 0; i < n0.Length; i++)
                {
                    n0[i] = acc[i].Normalized();
                    if (n0[i].LengthSq < 1e-8) n0[i] = outward;
                }
                for (int r = 0; r <= rows; r++)
                    for (int s = 0; s <= segs; s++)
                    {
                        int i = r * cols + s;
                        var sum = n0[i] * 2;
                        int count = 2;
                        if (r > 0) { sum += n0[i - cols]; count++; }
                        if (r < rows) { sum += n0[i + cols]; count++; }
                        if (s > 0) { sum += n0[i - 1]; count++; }
                        if (s < segs) { sum += n0[i + 1]; count++; }
                        var nn = (sum / count).Normalized();
                        if (nn.Dot(outward) < 0.05) nn = outward;
                        int vi = ids[layer][r][s];
                        mesh.Normals[vi * 3] = (float)nn.X;
                        mesh.Normals[vi * 3 + 1] = (float)nn.Y;
                        mesh.Normals[vi * 3 + 2] = (float)nn.Z;
                    }
            }
            perimeter += len;
        }
        edgeStarts[6] = perimeter;

        double PerimeterCoord(Vec2 p)
        {
            var nb = dom.NearestBoundary(p);
            Vec2 a = dom.Vertices[nb.Edge], b = dom.Vertices[(nb.Edge + 1) % 6];
            Vec2 ab = b - a;
            double t = MathD.Clamp01((nb.Point - a).Dot(ab) / Math.Max(ab.LengthSq, 1e-12));
            return edgeStarts[nb.Edge] + t * Math.Sqrt(ab.LengthSq);
        }

        // Real dangling rootlets: 12-25 tapered tubes clustered under woody flora within 0.8 m of the rim,
        // topped up deterministically along the topsoil/subsoil horizon where no woody plant is close.
        var rootRng = Rng.Stream(w.Seed, "strata.rootlets");
        int rootTarget = 12 + rootRng.NextInt(14);
        var anchors = new List<Vec2>(rootTarget);
        foreach (var flora in w.Flora.Items)
        {
            if (anchors.Count >= rootTarget) break;
            var species = w.Content.FloraOrThrow(flora.SpeciesId);
            bool woody = species.Woody != null || species.Tags.Contains("woody")
                || species.Tags.Contains("tree") || species.Tags.Contains("shrub");
            if (!woody) continue;
            if (Math.Abs(dom.SignedDistance(flora.Position)) > 0.8) continue;
            var nb = dom.NearestBoundary(flora.Position);
            Vec2 a = dom.Vertices[nb.Edge], b = dom.Vertices[(nb.Edge + 1) % 6];
            Vec2 ab = b - a;
            double len = Math.Max(Math.Sqrt(ab.LengthSq), 1e-6);
            double t0 = MathD.Clamp01((nb.Point - a).Dot(ab) / Math.Max(ab.LengthSq, 1e-12));
            int cluster = 2 + rootRng.NextInt(3);
            for (int c = 0; c < cluster && anchors.Count < rootTarget; c++)
            {
                double t = Math.Clamp(t0 + rootRng.Range(-0.07, 0.07) / len, 0.02, 0.98);
                anchors.Add(Vec2.Lerp(a, b, t));
            }
        }
        int guard = 0;
        while (anchors.Count < rootTarget && guard++ < rootTarget * 12)
        {
            int e = rootRng.NextInt(6);
            double t = rootRng.Range(0.04, 0.96);
            var q = Vec2.Lerp(dom.Vertices[e], dom.Vertices[(e + 1) % 6], t);
            bool tooClose = false;
            foreach (var other in anchors)
                if (Vec2.Distance(other, q) < 0.045) { tooClose = true; break; }
            if (!tooClose) anchors.Add(q);
        }
        while (anchors.Count < rootTarget)
        {
            int e = rootRng.NextInt(6);
            double t = rootRng.Range(0.04, 0.96);
            anchors.Add(Vec2.Lerp(dom.Vertices[e], dom.Vertices[(e + 1) % 6], t));
        }

        var rootColour = strata.Layers[0].Color;
        var rootTint = new[]
        {
            MathD.Clamp(rootColour[0] * 0.62 + 0.020, 0, 1),
            MathD.Clamp(rootColour[1] * 0.56 + 0.016, 0, 1),
            MathD.Clamp(rootColour[2] * 0.48 + 0.012, 0, 1),
        };
        var rootBounds = new double[layers + 1];
        var rootPath = new List<Vec3>(7);
        var rootRadii = new List<double>(7);
        double[] rootT = { 0, 0.12, 0.28, 0.48, 0.68, 0.86, 1.0 };
        double[] rootDrop = { 0.010, -0.002, -0.22, -0.44, -0.66, -0.86, -1.0 };

        foreach (var anchor in anchors)
        {
            var nb = dom.NearestBoundary(anchor);
            Vec2 n2 = nb.Normal;
            var tangent2 = new Vec2(-n2.Z, n2.X);
            double surf = RenderHeight(hf, anchor);
            double maxDepth = Math.Max(0.018, Math.Min(strata.Layers[0].Thickness + 0.14, surf - bottom - 0.03));
            double depth0 = rootRng.Range(0.018, maxDepth);
            double y0 = Math.Max(bottom + 0.02, surf - depth0);
            FillBounds(anchor, surf, rootBounds);
            double wallDepth = surf - y0;
            double disp = Displace(anchor, wallDepth, y0, rootBounds);
            double r0 = rootRng.Range(0.0015, 0.0045);
            double len = rootRng.Range(0.06, 0.22);
            double sideways = rootRng.Range(-0.022, 0.022);
            double bow = rootRng.Range(0.006, 0.016);
            var N = new Vec3(n2.X, 0, n2.Z);
            var T = new Vec3(tangent2.X, 0, tangent2.Z);

            rootPath.Clear(); rootRadii.Clear();
            for (int i = 0; i < rootT.Length; i++)
            {
                double t = rootT[i];
                double outOffset = i switch
                {
                    0 => disp - 0.035,
                    1 => disp + 0.001,
                    2 => disp + bow,
                    3 => disp + bow * 1.25,
                    4 => disp + bow * 1.25,
                    5 => disp + bow,
                    _ => disp + bow * 0.75,
                };
                var point = new Vec3(anchor.X, y0, anchor.Z)
                    + N * outOffset + T * (sideways * t) + Vec3.Up * (rootDrop[i] * len);
                rootPath.Add(point);
                double radius = r0 * (1 - t) + r0 * 0.28 * t;
                radius *= 1 + 0.08 * Math.Sin(i * 2.1 + r0 * 1.0e5);
                rootRadii.Add(Math.Max(0.0003, radius));
            }

            double perimeterAt = PerimeterCoord(anchor);
            double inLayer = MathD.Clamp01(depth0 / Math.Max(strata.Layers[0].Thickness, 0.05));
            Primitives.Tube(mesh, rootPath, rootRadii, 5, (i, around) =>
            {
                double radius = rootRadii[i];
                return (rootTint, 1.0,
                    perimeterAt + around * radius * 6.283185307179586,
                    depth0 + rootT[i] * len, 0.0, inLayer);
            }, Vec3.Up);
        }

        // underside: flat hexagon facing down
        var baseCol = strata.Layers[^1].Color;
        var dark = new[] { baseCol[0] * 0.7, baseCol[1] * 0.7, baseCol[2] * 0.7 };
        int c0 = mesh.AddVertex(new Vec3(0, bottom, 0), new Vec3(0, -1, 0), dark, 1, 0, 0, strata.Layers.Count - 1, 0);
        var ring = new int[6];
        for (int k = 0; k < 6; k++) ring[k] = mesh.AddVertex(new Vec3(dom.Vertices[k].X, bottom, dom.Vertices[k].Z), new Vec3(0, -1, 0), dark, 1, dom.Vertices[k].X, dom.Vertices[k].Z, strata.Layers.Count - 1, 0);
        for (int k = 0; k < 6; k++) mesh.AddTriangle(c0, ring[(k + 1) % 6], ring[k]);
        return mesh;
    }

    private static double Unit(ulong hash) => (hash >> 11) * (1.0 / (1UL << 53));

    private static double Fract(double v) => v - Math.Floor(v);

    /// <summary>Jittered-lattice Voronoi: nearest/second-nearest feature distances and the nearest cell id.</summary>
    private static (double F1, double F2, ulong Id) Voronoi(ulong seed, double x, double y)
    {
        long cx = (long)Math.Floor(x), cy = (long)Math.Floor(y);
        double f1 = double.MaxValue, f2 = double.MaxValue;
        ulong best = 0;
        for (int j = -1; j <= 1; j++)
            for (int i = -1; i <= 1; i++)
            {
                long gx = cx + i, gy = cy + j;
                ulong h = Rng.Mix(Rng.Mix(seed, (ulong)gx), (ulong)gy);
                ulong h2 = Rng.Mix(h, seed ^ 0x9E3779B97F4A7C15UL);
                double px = gx + 0.15 + 0.70 * Unit(h);
                double py = gy + 0.15 + 0.70 * Unit(h2);
                double dx = px - x, dy = py - y;
                double d2 = dx * dx + dy * dy;
                if (d2 < f1) { f2 = f1; f1 = d2; best = h; }
                else if (d2 < f2) f2 = d2;
            }
        return (Math.Sqrt(f1), Math.Sqrt(f2), best);
    }

    /// <summary>Overhanging organic turf lip just below the rim, with vertical root-channel flutes.</summary>
    private static double TopsoilRelief(double u, double depth, ulong seed)
    {
        double lip = MathD.SmoothStep(0.006, 0.026, depth) * (1 - MathD.SmoothStep(0.070, 0.150, depth));
        double lumps = 0.72 + 0.28 * Noise.Fbm(Rng.Mix(seed, 11), u * 8.5, depth * 13.0, 2);
        double bulge = 0.012 * lip * lumps;
        double sway = Noise.Gradient(Rng.Mix(seed, 13), u * 6.2, depth * 9.0) * 0.010;
        double flute = Math.Pow(0.5 + 0.5 * Math.Cos((u + sway) * 2 * Math.PI * 9.0), 10);
        double fluteDepth = 0.0065 * flute * MathD.SmoothStep(0.004, 0.020, depth) * (1 - MathD.SmoothStep(0.10, 0.20, depth));
        double grain = 0.0012 * Noise.Gradient(Rng.Mix(seed, 17), u * 65.0, depth * 65.0);
        return bulge - fluteDepth + grain;
    }

    /// <summary>Blocky ped structure: rounded Voronoi blocks separated by recessed fracture seams (+/-1.5 cm).</summary>
    private static double SubsoilRelief(double u, double depth, ulong seed)
    {
        var (f1, f2, id) = Voronoi(Rng.Mix(seed, 23), u * 34.0, depth * 34.0);
        double dome = 1 - MathD.SmoothStep(0.05, 0.95, f1);
        double crack = 1 - MathD.SmoothStep(0.03, 0.20, f2 - f1);
        double grain = (Unit(id) - 0.5) * 0.002;
        double relief = 0.030 * (dome - 0.5) - 0.006 * crack + grain;
        return Math.Clamp(relief, -0.015, 0.015);
    }

    /// <summary>Rounded embedded cobbles in the gravel/clay horizon; matrix recedes between the domes (+/-2.2 cm).</summary>
    private static double GravelRelief(double u, double depth, ulong seed)
    {
        var (f1, _, id) = Voronoi(Rng.Mix(seed, 29), u * 42.0, depth * 42.0);
        double radius = 0.60 + 0.35 * Unit(Rng.Mix(id, 3));
        double q = f1 / radius;
        double dome = q < 1 ? Math.Sqrt(1 - q * q) : 0;
        double stone = 0.72 + 0.58 * Unit(Rng.Mix(id, 5));
        double grain = 0.0015 * Noise.Gradient(Rng.Mix(seed, 31), u * 90.0, depth * 90.0);
        double relief = 0.040 * (dome * stone - 0.62) + grain;
        return Math.Clamp(relief, -0.022, 0.022);
    }

    /// <summary>Stepped horizontal sedimentary beds with angular cleavage ledges (+/-2.8 cm).</summary>
    private static double BedrockRelief(double u, double depth, ulong seed)
    {
        double warp = Noise.Fbm(Rng.Mix(seed, 37), u * 1.9, depth * 0.8, 2) * 0.045;
        double bedding = (depth + warp) / 0.11;
        long band = (long)Math.Floor(bedding);
        ulong hash = Rng.Mix(Rng.Mix(seed, (ulong)band), 0xBED5UL);
        double bed = (Unit(hash) * 2 - 1) * (0.62 + 0.38 * (Fract(bedding) - 0.5));
        double ridge = 1 - Math.Abs(Noise.Gradient(Rng.Mix(seed, 41), (u + depth * 0.55) * 4.5, depth * 5.5));
        double cleave = MathD.SmoothStep(0.74, 0.97, ridge);
        double relief = 0.028 * bed - 0.010 * cleave;
        return Math.Clamp(relief, -0.028, 0.028);
    }
}

public sealed class TopMesh
{
    public MeshData Mesh { get; }
    /// <summary>World XZ of each vertex (for recolouring by substrate/moisture without rebuilding).</summary>
    public IReadOnlyList<Vec2> VertexXZ { get; }
    public TopMesh(MeshData mesh, IReadOnlyList<Vec2> xz) { Mesh = mesh; VertexXZ = xz; }
}
