using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

public static partial class OrganismMeshes
{
    // Continuous quadratic midrib, tapered margins and a shallow fold. Unlike the old three-triangle
    // blades, these ribbons can bow, twist and roll without producing a sharp elbow in the silhouette.
    internal static void CurvedBlade(MeshData m, Vec3 root, Vec3 control, Vec3 tip, Vec3 sideHint,
        double width, double[] baseCol, double[] tipCol, int segments = 18,
        double twist = 0, double shoulder = 1, double serration = 0, double foliage = 1)
    {
        int rows = m.FloraDetailLevel switch { 2 => 8, 1 => 12, _ => Math.Max(segments, 18) };
        if (m.Lod is { } lod) rows = lod.Segments(rows, 4);
        int cols = m.FloraDetailLevel switch { 2 => 4, 1 => 6, _ => 8 };
        if (m.Lod is { } lodC) cols = Math.Max(3, (int)Math.Round(cols * lodC.Blade));

        Vec3 At(double t, double x, int face) => CurvedBladePoint(root, control, tip, sideHint, width, t, x, twist, shoulder, serration, face);
        Vec3 Normal(double t, double x, int face)
        {
            const double e = .002;
            var along = At(Math.Min(1, t + e), x, face) - At(Math.Max(0, t - e), x, face);
            var across = At(t, Math.Min(1, x + e), face) - At(t, Math.Max(-1, x - e), face);
            var normal = across.Cross(along).Normalized();
            return normal.LengthSq > 1e-8 ? normal : Vec3.Up;
        }

        int vertex = m.VertexCount, index = m.Indices.Count;
        int[] rootIdx = new int[2];
        int[] tipIdx = new int[2];
        int[,,] gridIdx = new int[2, rows - 1, cols + 1];

        for (int face = 0; face < 2; face++)
        {
            double sign = face == 0 ? 1.0 : -1.0;
            var underCol = Primitives.Mix(baseCol, new[] { 0.35, 0.48, 0.28 }, 0.42);
            var bCol = face == 0 ? baseCol : underCol;
            var tCol = face == 0 ? tipCol : underCol;

            rootIdx[face] = m.AddVertex(At(0, 0, face), Normal(0, 0, face) * sign, bCol, 1.0, 0.0, 0.5, face, foliage);

            for (int r = 1; r < rows; r++)
            {
                double t = (double)r / rows;
                var rowCol = Primitives.Mix(bCol, tCol, t);
                for (int c = 0; c <= cols; c++)
                {
                    double x = (double)c / cols * 2.0 - 1.0;
                    gridIdx[face, r - 1, c] = m.AddVertex(At(t, x, face), Normal(t, x, face) * sign, rowCol, 1.0, t, (x + 1.0) * 0.5, face, foliage);
                }
            }

            tipIdx[face] = m.AddVertex(At(1, 0, face), Normal(1, 0, face) * sign, tCol, 1.0, 1.0, 0.5, face, foliage);

            // Triangulation: root fan
            for (int c = 0; c < cols; c++)
            {
                int va = gridIdx[face, 0, c];
                int vb = gridIdx[face, 0, c + 1];
                Primitives.TriangleFacing(m, rootIdx[face], va, vb, m.NormalAt(rootIdx[face]));
            }

            // Triangulation: middle quad strips
            for (int r = 0; r < rows - 2; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int a = gridIdx[face, r, c];
                    int b = gridIdx[face, r, c + 1];
                    int d = gridIdx[face, r + 1, c];
                    int e = gridIdx[face, r + 1, c + 1];
                    Primitives.TriangleFacing(m, a, d, b, m.NormalAt(a));
                    Primitives.TriangleFacing(m, b, d, e, m.NormalAt(b));
                }
            }

            // Triangulation: tip fan
            for (int c = 0; c < cols; c++)
            {
                int va = gridIdx[face, rows - 2, c];
                int vb = gridIdx[face, rows - 2, c + 1];
                Primitives.TriangleFacing(m, va, tipIdx[face], vb, m.NormalAt(va));
            }
        }

        // Stitched boundary rim connecting upper and lower surfaces
        var sampleAcross = sideHint.Normalized();
        // Left rim (c = 0)
        Primitives.TriangleFacing(m, rootIdx[0], rootIdx[1], gridIdx[0, 0, 0], -sampleAcross);
        Primitives.TriangleFacing(m, rootIdx[1], gridIdx[1, 0, 0], gridIdx[0, 0, 0], -sampleAcross);
        for (int r = 0; r < rows - 2; r++)
        {
            Primitives.TriangleFacing(m, gridIdx[0, r, 0], gridIdx[1, r, 0], gridIdx[0, r + 1, 0], -sampleAcross);
            Primitives.TriangleFacing(m, gridIdx[1, r, 0], gridIdx[1, r + 1, 0], gridIdx[0, r + 1, 0], -sampleAcross);
        }
        Primitives.TriangleFacing(m, gridIdx[0, rows - 2, 0], gridIdx[1, rows - 2, 0], tipIdx[0], -sampleAcross);
        Primitives.TriangleFacing(m, gridIdx[1, rows - 2, 0], tipIdx[1], tipIdx[0], -sampleAcross);

        // Right rim (c = cols)
        Primitives.TriangleFacing(m, rootIdx[0], gridIdx[0, 0, cols], rootIdx[1], sampleAcross);
        Primitives.TriangleFacing(m, rootIdx[1], gridIdx[0, 0, cols], gridIdx[1, 0, cols], sampleAcross);
        for (int r = 0; r < rows - 2; r++)
        {
            Primitives.TriangleFacing(m, gridIdx[0, r, cols], gridIdx[0, r + 1, cols], gridIdx[1, r, cols], sampleAcross);
            Primitives.TriangleFacing(m, gridIdx[1, r, cols], gridIdx[0, r + 1, cols], gridIdx[1, r + 1, cols], sampleAcross);
        }
        Primitives.TriangleFacing(m, gridIdx[0, rows - 2, cols], tipIdx[0], gridIdx[1, rows - 2, cols], sampleAcross);
        Primitives.TriangleFacing(m, gridIdx[1, rows - 2, cols], tipIdx[0], tipIdx[1], sampleAcross);

        if (foliage > .5) m.RecordLeaf(vertex, index, root, (tip - root).Length);
    }

    private static Vec3 CurvedBladePoint(Vec3 root, Vec3 control, Vec3 tip, Vec3 sideHint,
        double width, double t, double x, double twist, double shoulder, double serration, int face = 0)
    {
        var side = sideHint.Normalized();
        var centre = root * ((1-t)*(1-t)) + control * (2*t*(1-t)) + tip * (t*t);
        var tangent = ((control-root)*(1-t) + (tip-control)*t).Normalized();
        var across = side - tangent * side.Dot(tangent);
        if (across.LengthSq < 1e-8) across = tangent.Cross(new Vec3(1, 0, 0));
        across = Axis.RotateAround(across.Normalized(), tangent, twist*t);
        var up = across.Cross(tangent).Normalized();
        // Sheathed culm base wrapping around axis
        if (t < 0.18)
        {
            double sw = (1.0 - t / 0.18);
            across = Axis.RotateAround(across, tangent, sw * 0.35 * x);
            up = across.Cross(tangent).Normalized();
        }
        double envelope = Math.Pow(Math.Max(0, Math.Sin(Math.PI*Math.Pow(t, shoulder))), .75);
        double teeth = 1 + serration * (2*Math.Abs(t*9-Math.Floor(t*9+.5))-.5);
        double half = width * envelope * (1 + .07*x*Math.Sin(t*5));
        double absX = Math.Abs(x);
        double zThick = face == 0 ? 0.00018 : -0.00018;
        double zProfile = face == 0
            ? (0.16 * (1.0 - x * x) - 0.08 * absX)
            : (0.16 * (1.0 - x * x) - 0.22 * (1.0 - absX));
        return centre + across*(x*half*teeth) + up*(zThick + half * zProfile);
    }


    private static void BlueSundewRosette(MeshData m, Rng rng, double radius, double height)
    {
        var clump = new MeshData();
        BlueSundewGeometry.AppendClump(clump, Vec3.Zero, Vec3.Up, (uint)rng.NextULong());
        radius = Math.Max(.001, radius); height = Math.Max(.001, height);
        for(int i=0; i<clump.VertexCount; i++)
        {
            var n=clump.NormalAt(i);
            n=new Vec3(n.X*radius,n.Y*height,n.Z*radius).Normalized();
            clump.Normals[i*3]=(float)n.X; clump.Normals[i*3+1]=(float)n.Y; clump.Normals[i*3+2]=(float)n.Z;
        }
        m.Append(clump,p=>new Vec3(p.X/radius,p.Y/height,p.Z/radius));
    }

}





/// <summary>
/// Render-only parametric detail tiers for flora. Every tier is the same generator run with the same seed and
/// individual form, only with coarser <see cref="FloraLodParams"/>: fewer radial sides and length rings on
/// tubes (never below 5 sides), fewer blade segments (never below 2), and a deterministic fraction of leaf
/// blades kept (innermost/smallest dropped first). Nothing is clustered or collapsed, so trunks and stems stay
/// closed solid tubes at every tier. Nothing here is simulation state.
/// </summary>
public static partial class OrganismMeshes
{
    public const int FloraLodTiers = 3;
    /// <summary>Hard cap on the top tier of every species.</summary>
    public const int FloraTopTriangleCap = 160000;
    public const double FloraMidRatio = 0.85, FloraLowRatio = 0.60;

    /// <summary>
    /// Top-tier triangle cap for one species. The default holds for every form, but a species whose
    /// near-field read depends on dense, fine foliage may ask for a richer closest mesh: the extra
    /// triangles are only paid for the instances the camera is close to.
    /// </summary>
    public static int FloraTopTriangleCapFor(string id) => id switch
    {
        // A clover mat is hundreds of small overlapping leaflets; below its budget the near tier
        // thins the mat into scattered pads instead of a continuous sward.
        "trifold" => 160000,
        // Dewbonnet's radial lamellae are part of the cap silhouette and underside read. Its
        // authored full mesh measures about 200k triangles; preserve the requested dense cap.
        "dewbonnet" => 220000,
        // A fern's near read is its pinnae: dozens of finely divided blades per frond, nine fronds
        // deep. Below its budget the near tier drops pinnae and the fern reads as a handful of
        // broad leaves instead of a feather.
        "veilfern" => 160000,
        // A bramble's near read is its prickles and its palmate leaves: without them the brake is a
        // tangle of bare wires.
        "hookthicket" => 160000,
        // The coinrunner mat is the same argument: a wide carpet of large reniform coins, each one
        // a modelled blade with its own venation, a coin-border bead and a rolled edge. Below its
        // budget the near tier drops coins and the carpet opens into separate rafts.
        "coinrunner" => 160000,
        // Fine dense-leaf shells on climbers are authored as full closed surfaces. Keep their
        // close tier intact; thinning them to the general cap opens the foliage into sparse strips.
        "fenhook" => 400000,
        "spiralvine" => 120000,
        "clinglace" => 360000,
        // A Sarracenia trap is a modelled organ: trumpet wall, throat, rolled lip, erect hood with
        // its areolate net and a fringe of crystalline needles. Below its budget the tierer drops
        // the hood fringe and the net, and the trap stops reading as a pitcher plant.
        "pitcher_plant" => 160000,
        _ => FloraTopTriangleCap,
    };

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string, ulong, int, bool, int), double> LodScaleCache = new();

    /// <summary>Triangle target for a tier, given the top-tier triangle count.</summary>
    public static int FloraTierBudget(int topTris, int tier) => tier switch
    {
        <= 0 => Math.Min(topTris, FloraTopTriangleCap),
        1 => Math.Max(1, (int)(topTris * FloraMidRatio)),
        _ => Math.Max(1, (int)(topTris * FloraLowRatio)),
    };

    /// <summary>Flora mesh for one detail tier. <paramref name="tieredSource"/> also selects the dedicated leaf
    /// system's own tessellation tier (visual-profile species).</summary>
    public static MeshData FloraTier(FloraSpeciesDef sp, ulong seed, int tier, bool juvenile = false, bool tieredSource = false, int farGillStations = 9, int farEmberMicroSides = 5) =>
        BuildTier(sp.Id, seed, juvenile ? 1 : 0, tieredSource, tier,
            (t, lod) => Flora(sp, seed, juvenile, tieredSource ? t : null, lod), farGillStations, farEmberMicroSides)!;

    public static MeshData? FloraFruitingTier(FloraSpeciesDef sp, ulong seed, int tier, bool tieredSource = false) =>
        BuildTier(sp.Id, seed, 2, tieredSource, tier, (t, lod) => FloraFruiting(sp, seed, tieredSource ? t : null, lod));

    public static MeshData ClimberNodeTier(FloraSpeciesDef sp, ulong seed, bool attached, int tier) =>
        BuildTier(sp.Id, seed, attached ? 3 : 4, false, tier, (_, lod) => ClimberNode(sp, seed, attached, lod))!;

    /// <summary>The growing tip of a shoot, at the given detail tier.</summary>
    public static MeshData ClimberNodeTipTier(FloraSpeciesDef sp, ulong seed, int tier) =>
        BuildTier(sp.Id, seed, 5, false, tier, (_, lod) => ClimberNodeTip(sp, seed, lod))!;

    private static MeshData? BuildTier(string id, ulong seed, int kind, bool tiered, int tier, Func<int, FloraLodParams, MeshData?> gen, int farGillStations = 9, int farEmberMicroSides = 5)
    {
        tier = Math.Clamp(tier, 0, FloraLodTiers - 1);
        MeshData? Make(int t, double s) => gen(t, FloraLodParams.FromScale(s)) is { } raw ? FinishTier(raw) : null;
        double S(int t)
        {
            var key = (id, seed, kind, tiered, t);
            if (LodScaleCache.TryGetValue(key, out double cached)) return cached;
            int target;
            double hiS;
            if (t == 0) { target = FloraTopTriangleCapFor(id); hiS = 1; }
            else
            {
                double sTop = S(0);
                var top = Make(0, sTop);
                if (top == null) return 0;
                target = FloraTierBudget(top.TriangleCount, t);
                hiS = t == 1 ? sTop : S(1);
            }
            double s = SearchScale(x => Make(t, x)?.TriangleCount ?? 0, target, hiS);
            LodScaleCache[key] = s;
            return s;
        }
        double scale = S(tier);
        // Keep the existing calibration and all non-gill geometry unchanged.
        if (tier == 2 && ((id == "dewbonnet" && farGillStations != 9) || (id == "embercup" && farEmberMicroSides != 5)))
        {
            var lod = FloraLodParams.FromScale(scale) with
            {
                GillStations = id == "dewbonnet" ? Math.Clamp(farGillStations, 3, 9) : 9,
                EmberMicroSides = id == "embercup" ? Math.Clamp(farEmberMicroSides, 3, 5) : 5
            };
            return gen(tier, lod) is { } raw ? FinishTier(raw) : null;
        }
        return Make(tier, scale);
    }

    /// <summary>Largest scale in [0, hi] whose triangle count is within <paramref name="target"/> (0 if none).</summary>
    private static double SearchScale(Func<double, int> tris, int target, double hi)
    {
        if (tris(hi) <= target) return hi;
        double lo = 0;
        if (tris(lo) > target) return 0;
        for (int it = 0; it < 9; it++)
        {
            double mid = (lo + hi) * 0.5;
            if (tris(mid) <= target) lo = mid; else hi = mid;
        }
        return lo;
    }

    /// <summary>Twice the triangle area below which a triangle counts as degenerate.</summary>
    public const double DegenerateCross = 1e-12;
    /// <summary>Unit-scale tube radius below which a tube is foliage-like (petiole, filament, needle).</summary>
    public const double MinorTubeRadius = 0.006;

    public static bool IsDegenerate(MeshData m, int tri)
    {
        var a = m.Position(m.Indices[tri * 3]);
        var cross = (m.Position(m.Indices[tri * 3 + 1]) - a).Cross(m.Position(m.Indices[tri * 3 + 2]) - a);
        return cross.Length < DegenerateCross;
    }

    /// <summary>Drops leaf blades beyond <see cref="FloraLodParams.LeafFraction"/> (innermost, then smallest,
    /// first) and zero-area triangles, then compacts the mesh. Tubes are never dropped.</summary>
    private static MeshData FinishTier(MeshData src)
    {
        double keepFraction = src.Lod?.LeafFraction ?? 1;
        int nv = src.VertexCount;
        var idx = src.Indices;
        // owner: -2 structural tube, >= 0 candidate part, -1 unassigned
        var owner = new int[nv];
        var essential = new bool[nv];
        Array.Fill(owner, -1);
        foreach (var st in src.Structural)
            for (int v = st.FirstVertex; v < st.FirstVertex + st.VertexCount && v < nv; v++) owner[v] = -2;
        foreach (var span in src.EssentialSpans)
            for (int v = span.FirstVertex; v < span.FirstVertex + span.VertexCount && v < nv; v++) {owner[v] = -2;essential[v]=true;}
        int parts = 0;
        foreach (var ls in src.LeafSpans)
        {
            bool any = false;
            for (int v = ls.FirstVertex; v < ls.FirstVertex + ls.VertexCount && v < nv; v++)
                if (owner[v] == -1) { owner[v] = parts; any = true; }
            if (any) parts++;
        }
        // Unrecorded small loose pieces (lobes, petals, beads) are candidates too; big ones are structure.
        var parent = new int[nv];
        for (int i = 0; i < nv; i++) parent[i] = i;
        int Find(int x) { while (parent[x] != x) { parent[x] = parent[parent[x]]; x = parent[x]; } return x; }
        for (int t = 0; t < idx.Count; t += 3)
        {
            int a = idx[t];
            for (int j = 1; j < 3; j++)
            {
                int b = idx[t + j];
                if (owner[a] != -1 || owner[b] != -1) continue;
                int ra = Find(a), rb = Find(b);
                if (ra != rb) { if (ra < rb) parent[rb] = ra; else parent[ra] = rb; }
            }
        }
        var compTris = new SortedDictionary<int, int>();
        for (int t = 0; t < idx.Count; t += 3)
            if (owner[idx[t]] == -1) { int r = Find(idx[t]); compTris[r] = compTris.GetValueOrDefault(r) + 1; }
        var compPart = new Dictionary<int, int>();
        foreach (var (root, count) in compTris)
            if (count <= 240) compPart[root] = parts++;
        for (int v = 0; v < nv; v++)
            if (owner[v] == -1 && compPart.TryGetValue(Find(v), out int pp)) owner[v] = pp;

        // Fine tubes are foliage, not wood: a petiole goes with the blade it carries; loose filaments, hairs
        // and needles are candidates of their own (dropped after blades). Trunks and limbs always stay.
        var tubePart = new HashSet<int>();
        double maxTube = src.Structural.Count > 0 ? src.Structural.Max(s => s.Radius) : 0;
        double minor = Math.Max(MinorTubeRadius, maxTube * 0.12);
        // Petioles are judged before the trunkless bump, or a reed's main culm rides along with the collar after it.
        double petiole = minor * 2;
        // Without a real trunk every tube is a stem or shoot; the finer side branches go before the main stems.
        if (maxTube < 0.03) minor = Math.Max(minor, maxTube * 0.6);
        foreach (var st in src.Structural)
        {
            if(essential[st.FirstVertex])continue;
            int end = st.FirstVertex + st.VertexCount;
            int p;
            if (st.Radius < petiole && end < nv && owner[end] >= 0) p = owner[end];
            else if (st.Radius < minor) { p = parts++; tubePart.Add(p); }
            else continue;
            for (int v = st.FirstVertex; v < end && v < nv; v++) owner[v] = p;
        }
        var drop = new bool[Math.Max(1, parts)];
        if (keepFraction < 1 && parts > 1)
        {
            var mn = new Vec3[parts]; var mx = new Vec3[parts]; var size = new int[parts];
            for (int p = 0; p < parts; p++)
            {
                mn[p] = new Vec3(double.MaxValue, double.MaxValue, double.MaxValue);
                mx[p] = new Vec3(double.MinValue, double.MinValue, double.MinValue);
            }
            for (int t = 0; t < idx.Count; t += 3)
            {
                int p = owner[idx[t]];
                if (p < 0) continue;
                size[p]++;
                for (int j = 0; j < 3; j++)
                {
                    var q = src.Position(idx[t + j]);
                    mn[p] = new Vec3(Math.Min(mn[p].X, q.X), Math.Min(mn[p].Y, q.Y), Math.Min(mn[p].Z, q.Z));
                    mx[p] = new Vec3(Math.Max(mx[p].X, q.X), Math.Max(mx[p].Y, q.Y), Math.Max(mx[p].Z, q.Z));
                }
            }
            var (bmin, bmax) = src.Bounds();
            var c = (bmin + bmax) * 0.5; var ext = bmax - bmin;
            double sx = Math.Max(1e-4, ext.X * 0.5), sy = Math.Max(1e-4, ext.Y * 0.5), sz = Math.Max(1e-4, ext.Z * 0.5);
            double maxSpan = 1e-9;
            for (int p = 0; p < parts; p++) if (size[p] > 0) maxSpan = Math.Max(maxSpan, (mx[p] - mn[p]).Length);
            var order = Enumerable.Range(0, parts).Where(p => size[p] > 0).Select(p =>
            {
                var q = (mn[p] + mx[p]) * 0.5 - c;
                double radialExposure = Math.Sqrt(q.X / sx * (q.X / sx) + q.Z / sz * (q.Z / sz));
                double height = Math.Clamp(q.Y / sy + 1, 0, 2);
                double shape = (mx[p] - mn[p]).Length / maxSpan;
                // Only Embercrown currently requests the crown-first rule.
                // Other species retain their existing radial selection policy.
                double importance = src.PreserveUpperCanopyOnLod
                    ? .70 * radialExposure + 1.25 * height + .45 * shape
                    : radialExposure + .30 * height + .50 * shape;
                return (p, score: importance + (tubePart.Contains(p) ? 4 : 0));
            }).OrderBy(x => x.score).ThenBy(x => x.p).ToList();
            int dropCount = Math.Min(order.Count - 1, (int)Math.Round(order.Count * (1 - keepFraction)));
            // WP-19: Plants never lose leaves or minor stems at distance; LOD is handled via tessellation reduction.
            // for (int k = 0; k < dropCount; k++) drop[order[k].p] = true;
        }

        var map = new int[nv];
        var dst = new MeshData { FloraDetailLevel = src.FloraDetailLevel, FloraVisualSeed = src.FloraVisualSeed, Lod = src.Lod,
            PreserveUpperCanopyOnLod = src.PreserveUpperCanopyOnLod };
        bool c0 = nv > 0 && src.Custom0.Count >= nv * 4, c1 = nv > 0 && src.Custom1.Count >= nv * 4;
        for (int v = 0; v < nv; v++)
        {
            if (owner[v] >= 0 && drop[owner[v]]) { map[v] = -1; continue; }
            map[v] = dst.VertexCount;
            for (int j = 0; j < 3; j++) { dst.Positions.Add(src.Positions[v * 3 + j]); dst.Normals.Add(src.Normals[v * 3 + j]); }
            for (int j = 0; j < 4; j++) dst.Colors.Add(src.Colors[v * 4 + j]);
            for (int j = 0; j < 2; j++) { dst.UV.Add(src.UV[v * 2 + j]); dst.UV2.Add(src.UV2[v * 2 + j]); }
            if (c0) for (int j = 0; j < 4; j++) dst.Custom0.Add(src.Custom0[v * 4 + j]);
            if (c1) for (int j = 0; j < 4; j++) dst.Custom1.Add(src.Custom1[v * 4 + j]);
        }
        int triCount = idx.Count / 3;
        var triStart = new int[triCount + 1];
        for (int t = 0; t < triCount; t++)
        {
            triStart[t] = dst.Indices.Count;
            int a = map[idx[t * 3]], b = map[idx[t * 3 + 1]], cc = map[idx[t * 3 + 2]];
            if (a < 0 || b < 0 || cc < 0 || IsDegenerate(src, t)) continue;
            dst.Indices.Add(a); dst.Indices.Add(b); dst.Indices.Add(cc);
        }
        triStart[triCount] = dst.Indices.Count;
        foreach (var leaf in src.Leaves)
        {
            if (leaf.FirstVertex >= nv || map[leaf.FirstVertex] < 0) continue;
            int t0 = Math.Min(triCount, leaf.FirstIndex / 3), t1 = Math.Min(triCount, (leaf.FirstIndex + leaf.IndexCount) / 3);
            dst.Leaves.Add(leaf with { FirstVertex = map[leaf.FirstVertex], FirstIndex = triStart[t0], IndexCount = triStart[t1] - triStart[t0] });
        }
        foreach (var st in src.Structural)
            if (st.FirstVertex < nv && map[st.FirstVertex] >= 0) dst.Structural.Add((map[st.FirstVertex], st.VertexCount, st.Sides, st.Radius));
        foreach (var ls in src.LeafSpans)
            if (ls.FirstVertex < nv && map[ls.FirstVertex] >= 0) dst.LeafSpans.Add((map[ls.FirstVertex], ls.VertexCount));
        foreach (var span in src.EssentialSpans)
            if (span.FirstVertex < nv && map[span.FirstVertex] >= 0) dst.EssentialSpans.Add((map[span.FirstVertex], span.VertexCount));
        return dst;
    }
}
