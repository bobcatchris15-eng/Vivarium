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

        Vec3 At(double t, double x) => CurvedBladePoint(root, control, tip, sideHint, width, t, x, twist, shoulder, serration);
        Vec3 Normal(double t, double x)
        {
            const double e = .002;
            var along = At(Math.Min(1, t + e), x) - At(Math.Max(0, t - e), x);
            var across = At(t, Math.Min(1, x + e)) - At(t, Math.Max(-1, x - e));
            var normal = across.Cross(along).Normalized();
            return normal.LengthSq > 1e-8 ? normal : Vec3.Up;
        }

        int vertex = m.VertexCount, index = m.Indices.Count;

        int start = m.VertexCount;
        for (int r = 0; r <= rows; r++)
        {
            double t = (double)r / rows;
            var col = Primitives.Mix(baseCol, tipCol, t);
            for (int c = 0; c <= cols; c++)
            {
                double x = (double)c / cols * 2.0 - 1.0;
                var mid = At(t, x);
                var n = Normal(t, x);
                m.AddVertex(mid, n, col, 1.0, t, (x + 1.0) * 0.5, 0.5, foliage);
            }
        }

        int rowStride = cols + 1;
        for (int r = 0; r < rows; r++)
        {
            for (int c = 0; c < cols; c++)
            {
                int a = start + r * rowStride + c;
                int b = a + rowStride;
                var n0 = m.NormalAt(a);
                Primitives.TriangleFacing(m, a, b, a + 1, n0.LengthSq > 1e-8 ? n0 : Vec3.Up);
                Primitives.TriangleFacing(m, a + 1, b, b + 1, n0.LengthSq > 1e-8 ? n0 : Vec3.Up);
            }
        }

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
        double zProfile = 0.16 * (1.0 - x * x) - 0.08 * absX;
        return centre + across*(x*half*teeth) + up*(half * zProfile);
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
/// blades kept (innermost/smallest dropped first). Main stems and marked essential features survive;
/// secondary foliage and filaments are thinned on middle/far tiers. Nothing here is simulation state.
/// </summary>
public static partial class OrganismMeshes
{
    public const int FloraLodTiers = 3;
    /// <summary>Hard cap on the top tier of every species.</summary>
    public const int FloraTopTriangleCap = 96000;
    public const double FloraMidRatio = 0.40, FloraLowRatio = 0.14;

    /// <summary>
    /// Top-tier triangle cap for one species. The default holds for every form, but a species whose
    /// near-field read depends on dense, fine foliage may ask for a richer closest mesh: the extra
    /// triangles are only paid for the instances the camera is close to.
    /// </summary>
    public static int FloraTopTriangleCapFor(string id) => id switch
    {
        // A clover mat is hundreds of small overlapping leaflets; below its budget the near tier
        // thins the mat into scattered pads instead of a continuous sward.
        "trifold" => 105000,
        // Keep authored cap/stipe density; underside lamellae now use a normal map.
        "dewbonnet" => 130000,
        // A fern's near read is its pinnae: dozens of finely divided blades per frond, nine fronds
        // deep. Below its budget the near tier drops pinnae and the fern reads as a handful of
        // broad leaves instead of a feather.
        "veilfern" => 105000,
        // A bramble's near read is its prickles and its palmate leaves: without them the brake is a
        // tangle of bare wires.
        "hookthicket" => 105000,
        // The coinrunner mat is the same argument: a wide carpet of large reniform coins, each one
        // a modelled blade with its own venation, a coin-border bead and a rolled edge. Below its
        // budget the near tier drops coins and the carpet opens into separate rafts.
        "coinrunner" => 105000,
        // Fine dense-leaf shells on climbers are authored as full closed surfaces. Keep their
        // close tier intact; thinning them to the general cap opens the foliage into sparse strips.
        "fenhook" => 220000,
        "spiralvine" => 80000,
        "clinglace" => 200000,
        // A Sarracenia trap is a modelled organ: trumpet wall, throat, rolled lip, erect hood with
        // its areolate net and a fringe of crystalline needles. Below its budget the tierer drops
        // the hood fringe and the net, and the trap stops reading as a pitcher plant.
        "pitcher_plant" => 105000,
        _ => FloraTopTriangleCap,
    };

    /// <summary>Calibrated tier scale per species/form/tier. Deliberately keyed without the instance seed:
    /// the calibration mesh is always generated with one canonical seed, so a single binary search serves
    /// every morph of the species.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<(string Id, int Kind, bool Tiered, int Tier, bool Thin), double> LodScaleCache = new();

    /// <summary>Triangle target for a tier, given the top-tier triangle count.</summary>
    public static int FloraTierBudget(int topTris, int tier) => tier switch
    {
        <= 0 => Math.Min(topTris, FloraTopTriangleCap),
        1 => Math.Max(1, (int)(topTris * FloraMidRatio)),
        _ => Math.Max(1, (int)(topTris * FloraLowRatio)),
    };

    /// <summary>Flora mesh for one detail tier. <paramref name="tieredSource"/> also selects the dedicated leaf
    /// system's own tessellation tier (visual-profile species). Geometry audits can disable
    /// <paramref name="thinMinorDetails"/> to isolate tessellation from organ selection.</summary>
    public static MeshData FloraTier(FloraSpeciesDef sp, ulong seed, int tier, bool juvenile = false, bool tieredSource = false, int farGillStations = 9, int farEmberMicroSides = 5, int farDewbonnetCapRings = 16, bool thinMinorDetails = true) =>
        BuildTier(sp.Id, seed, juvenile ? 1 : 0, tieredSource, tier,
            (sd, t, lod) => Flora(sp, sd, juvenile, tieredSource ? t : null, lod), farGillStations, farEmberMicroSides, farDewbonnetCapRings, thinMinorDetails)!;

    public static MeshData? FloraFruitingTier(FloraSpeciesDef sp, ulong seed, int tier, bool tieredSource = false) =>
        BuildTier(sp.Id, seed, 2, tieredSource, tier, (sd, t, lod) => FloraFruiting(sp, sd, tieredSource ? t : null, lod));

    public static MeshData ClimberNodeTier(FloraSpeciesDef sp, ulong seed, bool attached, int tier) =>
        BuildTier(sp.Id, seed, attached ? 3 : 4, false, tier, (sd, _, lod) => ClimberNode(sp, sd, attached, lod))!;

    /// <summary>The growing tip of a shoot, at the given detail tier.</summary>
    public static MeshData ClimberNodeTipTier(FloraSpeciesDef sp, ulong seed, int tier) =>
        BuildTier(sp.Id, seed, 5, false, tier, (sd, _, lod) => ClimberNodeTip(sp, sd, lod))!;

    private static MeshData? BuildTier(string id, ulong seed, int kind, bool tiered, int tier, Func<ulong, int, FloraLodParams, MeshData?> gen, int farGillStations = 9, int farEmberMicroSides = 5, int farDewbonnetCapRings = 16, bool thinMinorDetails = true)
    {
        tier = Math.Clamp(tier, 0, FloraLodTiers - 1);
        FloraLodParams Detail(int t, double s) => FloraLodParams.FromScale(s) with { CullMinorDetails = t > 0 && thinMinorDetails };
        MeshData? Make(ulong sd, int t, double s) => gen(sd, t, Detail(t, s)) is { } raw ? FinishTier(raw) : null;
        // One canonical seed per species/tier: every morph measures the same mesh, so the search is shared.
        ulong calSeed = Hash.Fnv1a64("flora.lod.cal." + id);
        double S(int t)
        {
            var key = (id, kind, tiered, t, thinMinorDetails);
            if (LodScaleCache.TryGetValue(key, out double cached)) return cached;
            int target;
            double hiS;
            if (t == 0) { target = FloraTopTriangleCapFor(id); hiS = 1; }
            else
            {
                double sTop = S(0);
                var top = Make(calSeed, 0, sTop);
                if (top == null) return 0;
                target = FloraTierBudget(top.TriangleCount, t);
                // An authored floor that is already above the top-tier cap returns sTop = 0. Lower tiers can
                // still thin organs, so they search their full range instead of collapsing onto that floor.
                hiS = t == 1 ? (sTop > 0 ? sTop : 1) : S(1);
            }
            double s = SearchScale(x => Make(calSeed, t, x)?.TriangleCount ?? 0, target, hiS);
            LodScaleCache[key] = s;
            return s;
        }
        double scale = S(tier);
        // Keep the existing calibration; override only the selected far-tier detail.
        if (tier == 2 && ((id == "dewbonnet" && (farGillStations != 9 || farDewbonnetCapRings != 16)) || (id == "embercup" && farEmberMicroSides != 5)))
        {
            var lod = Detail(tier, scale) with
            {
                GillStations = id == "dewbonnet" ? Math.Clamp(farGillStations, 3, 9) : 9,
                EmberMicroSides = id == "embercup" ? Math.Clamp(farEmberMicroSides, 3, 5) : 5,
                DewbonnetCapRings = id == "dewbonnet" ? Math.Clamp(farDewbonnetCapRings, 4, 16) : 16
            };
            return gen(seed, tier, lod) is { } raw ? FinishTier(raw) : null;
        }
        return Make(seed, tier, scale);
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
    /// first) and zero-area triangles, then compacts the mesh. Main structural tubes are retained.</summary>
    private static MeshData FinishTier(MeshData src)
    {
        double keepFraction = src.Lod?.LeafFraction ?? 1;
        int nv = src.VertexCount;
        var idx = src.Indices;
        var owner = Array.Empty<int>();
        var drop = Array.Empty<bool>();
        if (src.Lod?.CullMinorDetails == true && keepFraction < 1)
        {
            // owner: -2 structural tube, >= 0 candidate part, -1 unassigned
            owner = new int[nv];
            var essential = new bool[nv];
            Array.Fill(owner, -1);
            double maxTube = src.Structural.Count > 0 ? src.Structural.Max(s => s.Radius) : 0;
            foreach (var st in src.Structural)
                for (int v = st.FirstVertex; v < st.FirstVertex + st.VertexCount && v < nv; v++)
                {
                    owner[v] = -2;
                    bool substantialWood = st.Radius >= Math.Max(MinorTubeRadius, maxTube * .12)
                        && src.UV2.Count > st.FirstVertex * 2 && src.UV2[st.FirstVertex * 2] == 2;
                    if (st.Radius >= maxTube * .9 || substantialWood) essential[v] = true;
                }
            foreach (var span in src.EssentialSpans)
                for (int v = span.FirstVertex; v < span.FirstVertex + span.VertexCount && v < nv; v++) {owner[v] = -2;essential[v]=true;}
            int parts = 0;
            foreach (var ls in src.LeafSpans)
            {
                bool any = false;
                for (int v = ls.FirstVertex; v < ls.FirstVertex + ls.VertexCount && v < nv; v++)
                    if (owner[v] < 0 && !essential[v]) { owner[v] = parts; any = true; }
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
            // and needles are candidates of their own. Prefer foliage over disconnected fine wires.
            var tubePart = new HashSet<int>();
            double minor = Math.Max(MinorTubeRadius, maxTube * 0.12);
            // Petioles are judged before the trunkless bump, or a reed's main culm rides along with the collar after it.
            double petiole = minor * 2;
            // Without a real trunk every tube is a stem or shoot; the finer side branches go before the main stems.
            if (maxTube < 0.03) minor = Math.Max(minor, maxTube * 0.6);
            foreach (var st in src.Structural)
            {
                int end = st.FirstVertex + st.VertexCount;
                if (owner[st.FirstVertex] >= 0 || st.Radius >= maxTube * .9 || Array.IndexOf(essential, true, st.FirstVertex, Math.Min(end, nv) - st.FirstVertex) >= 0) continue;
                int p;
                if (st.Radius < petiole && end < nv && owner[end] >= 0) p = owner[end];
                else if (st.Radius < minor) { p = parts++; tubePart.Add(p); }
                else continue;
                for (int v = st.FirstVertex; v < end && v < nv; v++) owner[v] = p;
            }
            drop = new bool[Math.Max(1, parts)];
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
                    return (p, score: importance - (tubePart.Contains(p) ? .5 : 0));
                }).OrderBy(x => x.score).ThenBy(x => x.p).ToList();
                int dropCount = Math.Min(order.Count - 1, (int)Math.Round(order.Count * (1 - keepFraction)));
                // Retain exposed/large organs first; main stems and essential spans are never candidates.
                for (int k = 0; k < dropCount; k++) drop[order[k].p] = true;
            }

        }

        var map = new int[nv];
        var dst = new MeshData { FloraDetailLevel = src.FloraDetailLevel, FloraVisualSeed = src.FloraVisualSeed, Lod = src.Lod,
            PreserveUpperCanopyOnLod = src.PreserveUpperCanopyOnLod };
        bool c0 = nv > 0 && src.Custom0.Count >= nv * 4, c1 = nv > 0 && src.Custom1.Count >= nv * 4;
        for (int v = 0; v < nv; v++)
        {
            if (owner.Length > 0 && owner[v] >= 0 && drop[owner[v]]) { map[v] = -1; continue; }
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
        var kept = new int[nv + 1];
        for (int i = 0; i < nv; i++) kept[i + 1] = kept[i] + (map[i] >= 0 ? 1 : 0);
        (int First, int Count) RemapSpan(int first, int count)
        {
            int begin = Math.Clamp(first, 0, nv), end = (int)Math.Min(nv, (long)begin + count);
            return (kept[begin], kept[end] - kept[begin]);
        }
        foreach (var leaf in src.Leaves)
        {
            var span = RemapSpan(leaf.FirstVertex, leaf.VertexCount);
            if (span.Count == 0) continue;
            int t0 = Math.Min(triCount, leaf.FirstIndex / 3), t1 = Math.Min(triCount, (leaf.FirstIndex + leaf.IndexCount) / 3);
            dst.Leaves.Add(leaf with { FirstVertex = span.First, VertexCount = span.Count, FirstIndex = triStart[t0], IndexCount = triStart[t1] - triStart[t0] });
        }
        foreach (var st in src.Structural)
        {
            var span = RemapSpan(st.FirstVertex, st.VertexCount);
            if (span.Count > 0) dst.Structural.Add((span.First, span.Count, st.Sides, st.Radius));
        }
        foreach (var ls in src.LeafSpans)
        {
            var span = RemapSpan(ls.FirstVertex, ls.VertexCount);
            if (span.Count > 0) dst.LeafSpans.Add((span.First, span.Count));
        }
        foreach (var span in src.EssentialSpans)
        {
            var mapped = RemapSpan(span.FirstVertex, span.VertexCount);
            if (mapped.Count > 0) dst.EssentialSpans.Add((mapped.First, mapped.Count));
        }
        return dst;
    }
}
