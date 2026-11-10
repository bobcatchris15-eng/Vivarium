using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Procedural builders for the four new ferns in the 2026-11-06 flora fiction catalog expansion.
/// All geometry is authored in real metres into a scratch part, then converted with
/// <see cref="NewFloraAppendWorld"/>. Pinnate fronds come from <see cref="PinnateFrond"/>;
/// mirrorstrap straps come from <see cref="Sheet"/> over an arching <see cref="Axis"/>.
/// </summary>
public static partial class OrganismMeshes
{
    private static void ClampNewFernsToGround(MeshData part)
    {
        // Frond bases sit in the litter; hold any sub-surface vertex at the surface.
        for (int i = 0; i < part.VertexCount; i++)
            if (part.Positions[i * 3 + 1] < 0.0f) part.Positions[i * 3 + 1] = 0.0f;
    }

    private static int NewFernsPairs(MeshData m, int full)
    {
        // Pinnae per frond: far FloraDetailLevels drop pairs first (silhouette carried by
        // frond length and arch, not by fine pinna count), then the LOD tier scales further.
        int detail = m.FloraDetailLevel ?? 0;
        int pairs = detail == 0 ? full : Math.Max(6, full - 2 * detail);
        if (m.Lod is { } lod) pairs = Math.Max(5, (int)Math.Round(pairs * lod.Length));
        return pairs;
    }

    /// <summary>
    /// Wave 9 true pinnate frond: a smooth 7-station curved rachis (Tube) with
    /// crisp, narrow, cambered pinnae (CurvedLeaf) leaving clear daylight gaps
    /// between every pair. Returns the rachis stations so callers can add
    /// fertile tips or fork branches.
    /// </summary>
    private static Vec3[] BuildTruePinnateFrond(MeshData part, Rng rng, Vec3 origin, double azimuth, double length, double baseAngleFromVertical, double archDroop, double rachisBaseR, int pairs, double maxPinnaLen, double pinnaWidthRatio, double subOpposite, double tilt, double[] stemCol, double[] leafBaseCol, double[] leafTipCol, int detail, bool triangular = false)
    {
        length = Math.Max(0.02, length);
        pairs = Math.Max(2, pairs);
        var azDir = new Vec3(Math.Cos(azimuth), 0, Math.Sin(azimuth));
        var sideVec = new Vec3(-Math.Sin(azimuth), 0, Math.Cos(azimuth));
        // 7-station curved rachis: rises at baseAngleFromVertical, arches outward/down by archDroop.
        const int stations = 7;
        var pts = new Vec3[stations];
        pts[0] = origin;
        var pos = origin;
        double step = length / (stations - 1);
        for (int i = 1; i < stations; i++)
        {
            double tm = (i - 0.5) / (stations - 1);
            double ang = baseAngleFromVertical + archDroop * tm * tm;
            var dir = (azDir * Math.Sin(ang) + Vec3.Up * Math.Cos(ang)).Normalized();
            pos = pos + dir * step;
            pts[i] = pos;
        }
        var radii = new double[stations];
        for (int i = 0; i < stations; i++)
        {
            double tm = (double)i / (stations - 1);
            radii[i] = Math.Max(0.0006, rachisBaseR * (1.0 - 0.62 * tm));
        }
        Primitives.Tube(part, pts, radii, 6,
            (i, v) =>
            {
                double tm = (double)i / Math.Max(1, stations - 1);
                var col = Primitives.Mix(stemCol, leafBaseCol, tm * 0.35);
                return (col, 1.0, tm, v, 0.0, 0.0);
            });

        Vec3 At(double t, out Vec3 tangent)
        {
            double x = Math.Clamp(t, 0, 1) * (stations - 1);
            int i = Math.Min(stations - 2, (int)Math.Floor(x));
            double f = x - i;
            tangent = (pts[i + 1] - pts[i]).Normalized();
            return Vec3.Lerp(pts[i], pts[i + 1], f);
        }

        double dt = (0.96 - 0.14) / Math.Max(1, pairs - 1);
        for (int p = 0; p < pairs; p++)
        {
            double t = 0.14 + (0.96 - 0.14) * (pairs == 1 ? 0 : (double)p / (pairs - 1));
            // Envelope: sin bell, or triangular (widest at base) for Emberbracken.
            double env = triangular
                ? (1.0 - 0.78 * t)
                : Math.Sin(Math.PI * Math.Pow(t, 0.65)) * (1.0 - 0.18 * t);
            env = Math.Max(0.08, env);
            double pinnaVariation = 0.84 + 0.28 * rng.NextDouble();
            double pinnaLen = Math.Max(0.004, maxPinnaLen * env * pinnaVariation);
            for (int sIdx = 0; sIdx < 2; sIdx++)
            {
                double sign = sIdx == 0 ? -1.0 : 1.0;
                double tt = Math.Clamp(t + sign * subOpposite * 0.5 * dt, 0.05, 0.99);
                var attach = At(tt, out var tangent);
                // Pinna direction: sideways, angled toward the tip, lifted by tilt.
                var pinnaDir = (sideVec * (sign * 0.82) + tangent * 0.55 + Vec3.Up * tilt).Normalized();
                var pinnaUp = (Vec3.Up - pinnaDir * pinnaDir.Dot(Vec3.Up)).Normalized();
                if (pinnaUp.LengthSq < 1e-8) pinnaUp = sideVec * sign;
                var tip = attach + pinnaDir * pinnaLen;
                double halfW = pinnaLen * pinnaWidthRatio;
                int lv = part.VertexCount;
                int li = part.Indices.Count;
                Primitives.CurvedLeaf(part, attach, tip, pinnaUp, halfW, leafBaseCol, leafTipCol, camber: 0.08 * pinnaLen, longitudinal: detail == 2 ? 3 : 4);
                part.RecordLeaf(lv, li, attach, pinnaLen);
                // Small side-lobes / pinnules on the lower pinnae for bipinnate texture.
                if (tt < 0.52 && detail < 2)
                {
                    int lobes = 4 + rng.NextInt(3); // 4-6
                    for (int lb = 0; lb < lobes; lb++)
                    {
                        double f = 0.30 + 0.45 * (lb / Math.Max(1, lobes - 1));
                        double lsign = (lb % 2 == 0) ? 1.0 : -1.0;
                        var lat = (pinnaUp * lsign * 0.55 + pinnaDir * 0.45).Normalized();
                        var lAttach = attach + pinnaDir * (pinnaLen * f);
                        double lLen = pinnaLen * 0.26;
                        var lTip = lAttach + lat * lLen;
                        Primitives.CurvedLeaf(part, lAttach, lTip, pinnaDir, Math.Max(0.0012, lLen * pinnaWidthRatio), leafBaseCol, leafTipCol, camber: 0.08 * lLen, longitudinal: 3);
                    }
                }
            }
        }
        // Single terminal pinna continuing the rachis at t = 1.0.
        {
            var attach = At(1.0, out var tangent);
            double tLen = Math.Max(0.004, maxPinnaLen * 0.55);
            var tip = attach + tangent * tLen;
            var pinnaUp = (Vec3.Up - tangent * tangent.Dot(Vec3.Up)).Normalized();
            if (pinnaUp.LengthSq < 1e-8) pinnaUp = sideVec;
            int lv = part.VertexCount;
            int li = part.Indices.Count;
            Primitives.CurvedLeaf(part, attach, tip, pinnaUp, tLen * pinnaWidthRatio, leafBaseCol, leafTipCol, camber: 0.08 * tLen, longitudinal: detail == 2 ? 3 : 4);
            part.RecordLeaf(lv, li, attach, tLen);
        }
        return pts;
    }

    /// <summary>Coiled green fiddlehead crozier: a spiral tube curling over its tip.</summary>
    private static void BuildFiddlehead(MeshData part, Vec3 origin, double azimuth, double len, double[] baseCol, double[] tipCol)
    {
        var azDir = new Vec3(Math.Cos(azimuth), 0, Math.Sin(azimuth));
        var sideVec = new Vec3(-Math.Sin(azimuth), 0, Math.Cos(azimuth));
        int n = 12;
        var path = new Vec3[n];
        var radii = new double[n];
        for (int i = 0; i < n; i++)
        {
            double tm = (double)i / (n - 1);
            double rise = len * tm * 0.85;
            double coilR = 0.022 * (1.0 - tm) + 0.002;
            double ang = tm * 4.6; // ~2.6 turns
            var centre = origin + Vec3.Up * rise + azDir * (len * 0.12 * tm);
            path[i] = centre + sideVec * (Math.Cos(ang) * coilR) + azDir * (Math.Sin(ang) * coilR * 0.6);
            radii[i] = 0.0032 * (1.0 - 0.55 * tm);
        }
        Primitives.Tube(part, path, radii, 6,
            (i, v) =>
            {
                double tm = (double)i / Math.Max(1, n - 1);
                return (Primitives.Mix(baseCol, tipCol, tm * 0.4), 1.0, tm, v, 0.0, 0.0);
            });
    }

    /// <summary>
    /// Swanfrond: a soft bipinnate shuttlecock of rich moist shade. Ten
    /// crown fronds arch outward and down and overlap into one dense
    /// layered mass; the pinnae are the densest of the four ferns
    /// (15-16 pairs, near-opposite) but finely divided: long, narrow,
    /// pointed leaflets (PinnaLength 0.30 m, PinnaWidth 0.085 m absolute
    /// metres) with a clear gap between neighbours, so the bipinnate feather —
    /// not weed blobs — reads at capture distance. The youngest fronds
    /// stand half-open: a visible coil with short frondlings already
    /// tucked under it. The broadest, softest arch of the set (ArchCurve
    /// 1.14, weeping tips, softest stipe) keeps the fountain broad without
    /// a naked tip; leaf colour is lifted toward c2 and scaled 1.48x so
    /// the mass reads pale-green, not dark under the shader's leaf
    /// pigment mix. Wave 8: PinnaLength/PinnaWidth are ABSOLUTE metres
    /// (PinnateFrond multiplies by env and length/0.88); widened so the
    /// fronds read lush, not twiggy.
    /// </summary>
    private static void Swanfrond(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        int detail = m.FloraDetailLevel ?? 0;
        double h = Math.Max(0.05, height);
        double scale = juvenile ? 0.65 : 1.0;

        // Visible stipe base: a short rhizome tube the shuttlecock mass sits on.
        double rhizH = 0.08 * h * scale;
        var rhizCol = Primitives.Scale(c1, 0.75);
        Primitives.Tube(part,
            new[] { Vec3.Zero, new Vec3(0, rhizH * 0.5, 0), new Vec3(0, rhizH, 0) },
            new[] { 0.018, 0.016, 0.014 }, 8,
            (i, v) => (rhizCol, 1, (double)i / 2, v, 0, 0));

        // Lifted leaf pigment: c1 is mixed toward the pale c2 and the
        // whole gradient is scaled 1.48x so it survives the flora shader's
        // mix with the dark leaf_top pigment and reads pale-green.
        var leafC1 = Primitives.Scale(Primitives.Mix(c1, c2, 0.55), 1.48);
        var leafC2 = Primitives.Scale(c2, 1.48);

        // Fronds overshoot the nominal height so the arching mass fills its
        // (radius, height) footprint instead of shrinking dwarfed inside it.
        double frondLen = Math.Max(0.05, h * scale * 1.18 * rng.Range(0.95, 1.05) - rhizH);

        // Wave 9: true pinnate fronds — 11-13 arching shuttlecock fronds.
        int fronds = NewFloraCount(m, 11 + rng.NextInt(3)); // 11-13
        var crown = new Vec3(0, rhizH, 0);
        for (int k = 0; k < fronds; k++)
        {
            double az = 2.0 * Math.PI * k / Math.Max(1, fronds) + rng.Range(-0.18, 0.18);
            BuildTruePinnateFrond(part, rng, crown, az,
                frondLen * rng.Range(0.92, 1.05),
                rng.Range(0.82, 1.08), rng.Range(0.28, 0.48),
                0.0035, NewFernsPairs(m, 20 + rng.NextInt(5)), // 20-24 pairs
                0.11 * scale, 0.15, 0.10, 0.18,
                Primitives.Scale(c1, 0.75), leafC1, leafC2, detail);
        }
        // 3 coiled green fiddlehead crozier spirals in the centre.
        for (int k = 0; k < 3; k++)
        {
            double az = rng.Range(0, 2.0 * Math.PI);
            BuildFiddlehead(part, crown, az, frondLen * rng.Range(0.30, 0.45), leafC1, leafC2);
        }

        ClampNewFernsToGround(part);
        NewFloraAppendWorld(m, part, radius, height);
    }

    /// <summary>
    /// Sceptrefern: the sceptre — a tall, upright, rigid crown of wet woodland. Fronds
    /// hold themselves stiff and near-vertical (ArchCurve 0.10, stout rachis, stiffest
    /// stipe) with eight pairs of widely spaced, large, broad oblong pinnae
    /// standing along each stem like teeth: alternate attachment (SubOpposite 0.5) on a
    /// long petiolule, so the bare rachis shows between teeth instead of a regular
    /// close feather and instead of the tip-clustered crown the review caught. Frond
    /// length overshoots the nominal height (1.20x) so the stems clear 50 cm and fill
    /// the crown tall; the narrow upright arch (0.10) differentiates from swanfrond's
    /// broad weep (1.14) and emberbracken's flat fan (0.42). Leaf colour is lifted
    /// toward c2 and scaled 1.4x: this is the darkest c1 of the set and rendered
    /// near-black before the lift. Wave 8: PinnaLength/PinnaWidth in ABSOLUTE metres;
    /// pinnae widened to broad oblong teeth; rusty-brown fertile spikes raise the
    /// sceptre apex above the green crown.
    /// </summary>
    private static void Sceptrefern(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        int detail = m.FloraDetailLevel ?? 0;
        double h = Math.Max(0.05, height);
        double scale = juvenile ? 0.65 : 1.0;

        // Short slim stipe base carrying the stiff crown clear of the wet soil.
        // Was a stout stump (0.032 radius) that read as a sapling trunk; now thin
        // so the crown reads as fern, not tree.
        double rhizH = 0.14 * h * scale;
        var rhizCol = Primitives.Scale(c1, 0.7);
        Primitives.Tube(part,
            new[] { Vec3.Zero, new Vec3(0, rhizH * 0.5, 0), new Vec3(0, rhizH, 0) },
            new[] { 0.020, 0.017, 0.014 }, 8,
            (i, v) => (rhizCol, 1, (double)i / 2, v, 0, 0));

        // Lifted leaf pigment (darkest c1 of the set): mixed to mid-bright
        // and scaled 1.4x, not near-black under the shader.
        var leafC1 = Primitives.Scale(Primitives.Mix(c1, c2, 0.50), 1.40);
        var leafC2 = Primitives.Scale(c2, 1.40);

        // Stems overshoot the nominal height so even the 0.85 m crown fills
        // tall (was 1.08x: the crown read as a small spiky bush).
        double frondLen = h * scale * 1.20 - rhizH;

        // Wave 9: true pinnate fronds — 8-10 tall stiffly ascending fronds.
        int fronds = NewFloraCount(m, 8 + rng.NextInt(3)); // 8-10
        var crown = new Vec3(0, rhizH, 0);
        var rachises = new List<Vec3[]>();
        var lens = new List<double>();
        for (int k = 0; k < fronds; k++)
        {
            double az = 2.0 * Math.PI * k / Math.Max(1, fronds) + rng.Range(-0.15, 0.15);
            double thisLen = frondLen * rng.Range(0.90, 1.05);
            var pts = BuildTruePinnateFrond(part, rng, crown, az,
                thisLen,
                rng.Range(0.18, 0.38), rng.Range(0.18, 0.28),
                0.006, NewFernsPairs(m, 12 + rng.NextInt(3)), // 12-14 pairs
                0.15 * scale, 0.20, 0.25, 0.05,
                Primitives.Scale(c1, 0.75), leafC1, leafC2, detail);
            rachises.Add(pts);
            lens.Add(thisLen);
        }
        // Royal-fern sceptre: on the 4 tallest central fronds, top the upper
        // 25% of the rachis with rusty-cinnamon fertile spore-beads.
        {
            var fertBase = new[] { 0.62, 0.32, 0.12 };
            var fertCol = Primitives.Scale(fertBase, 1.25);
            var order = new List<int>();
            for (int k = 0; k < fronds; k++) order.Add(k);
            order.Sort((a, b) => lens[b].CompareTo(lens[a]));
            int fertile = Math.Min(3, fronds);
            for (int fi = 0; fi < fertile; fi++)
            {
                var pts = rachises[order[fi]];
                // short fertile tube along the upper rachis
                var seg = new List<Vec3>();
                var segR = new List<double>();
                for (int i = pts.Length - 2; i < pts.Length; i++) { seg.Add(pts[i]); segR.Add(0.0035); }
                if (seg.Count >= 2)
                    Primitives.Tube(part, seg, segR, 5, (i, v) => (fertCol, 1, (double)i, v, 0, 0));
                int beads = detail == 2 ? 6 : 10;
                for (int b = 0; b < beads; b++)
                {
                    double tm = 0.72 + 0.28 * (b / Math.Max(1, beads - 1));
                    double x = Math.Clamp(tm, 0, 1) * (pts.Length - 1);
                    int i = Math.Min(pts.Length - 2, (int)Math.Floor(x));
                    var bp = Vec3.Lerp(pts[i], pts[i + 1], x - i);
                    double br = rng.Range(0.0035, 0.0055);
                    int beadFirst = part.VertexCount;
                    Primitives.Ellipsoid(part, bp, new Vec3(br, br * 1.25, br), 3, 6,
                        (u, v) => (fertCol, 1, u, v, 0, 0));
                    part.Structural.Add((beadFirst, part.VertexCount - beadFirst, 6, br));
                }
            }
        }

        ClampNewFernsToGround(part);
        NewFloraAppendWorld(m, part, radius, height);
    }

    /// <summary>
    /// Emberbracken: the TRIANGULAR fan — a stiff pioneer of dry open slopes. Each
    /// frond is widest at the base and tapers to a point: low Taper (0.32) peaks the
    /// pinna envelope near the base and the large flat pinnae close it to a triangle.
    /// Tilt and Camber are near-zero (flat fan), ArchCurve 0.42 so the triangle is
    /// held out flatter than swanfrond's weep (1.14) and wider than sceptrefern's
    /// narrow upright (0.10), not wept. Basal green is mixed well toward bronze,
    /// lifted 1.50x, and 3-4 older fronds are full bronze (c2 outright, scaled
    /// 1.55x) so the bronze fan — not just a green weed with one brown stem — reads
    /// at capture distance; the thicker ascending rachis (0.013) carries the
    /// triangular outline. A thick near-black creeping rhizome marches behind with
    /// 2-3 visible buds. Green and bronze are both lifted so they read mid-bright,
    /// never near-black under the shader.
    /// </summary>
    private static void Emberbracken(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        int detail = m.FloraDetailLevel ?? 0;
        double h = Math.Max(0.05, height);
        double scale = juvenile ? 0.65 : 1.0;
        double frondLen = h * scale * rng.Range(0.95, 1.05);

        // Wave 9: 6-8 wide deltoid (3-part triangular) bracken fronds.
        // Each frond forks at 35% height into 1 terminal + 2 lateral pinnate
        // branches at +-55 degrees, each branch pinnate in russet/olive-gold.
        var russetBase = new[] { 0.72, 0.42, 0.18 };
        var oliveBase = new[] { 0.48, 0.58, 0.22 };
        var russet = Primitives.Scale(russetBase, 1.30);
        var olive = Primitives.Scale(oliveBase, 1.30);
        var stemCol = Primitives.Scale(c1, 0.60);
        int fronds = NewFloraCount(m, 6 + rng.NextInt(3)); // 6-8
        var crown = new Vec3(0, 0.025, 0);
        for (int k = 0; k < fronds; k++)
        {
            double az = 2.0 * Math.PI * k / Math.Max(1, fronds) + rng.Range(-0.20, 0.20);
            double thisLen = frondLen * rng.Range(0.90, 1.05);
            double baseAng = rng.Range(0.28, 0.45);
            // Fork at 35% height along the initial rachis direction.
            var azDir = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var initDir = (azDir * Math.Sin(baseAng) + Vec3.Up * Math.Cos(baseAng)).Normalized();
            var fork = crown + initDir * (thisLen * 0.35);
            // Main stalk to the fork.
            Primitives.Tube(part, new[] { crown, Vec3.Lerp(crown, fork, 0.5), fork },
                new[] { 0.007, 0.0055, 0.004 }, 6,
                (i, v) => (stemCol, 1, (double)i / 2, v, 0, 0));
            // Terminal branch.
            BuildTruePinnateFrond(part, rng, fork, az, thisLen * 0.68, baseAng, 0.30,
                0.004, NewFernsPairs(m, 12 + rng.NextInt(5)), 0.11 * scale, 0.18, 0.10, 0.08,
                stemCol, russet, olive, detail, triangular: true);
            // Two wide lateral branches at +-55 degrees.
            BuildTruePinnateFrond(part, rng, fork, az + 0.96, thisLen * 0.62, baseAng + 0.68, 0.48,
                0.004, NewFernsPairs(m, 12 + rng.NextInt(5)), 0.10 * scale, 0.18, 0.10, 0.05,
                stemCol, olive, russet, detail, triangular: true);
            BuildTruePinnateFrond(part, rng, fork, az - 0.96, thisLen * 0.62, baseAng + 0.68, 0.48,
                0.004, NewFernsPairs(m, 12 + rng.NextInt(5)), 0.10 * scale, 0.18, 0.10, 0.05,
                stemCol, russet, olive, detail, triangular: true);
        }

        // Creeping rhizome: a thick near-black horizontal tube trailing behind
        // the crown, with 2-3 buds: the march a little further uphill each year.
        // Thick, long and raised so it clears the litter instead of sinking into
        // it invisibly under the fronds.
        double rhizA = rng.Range(0, 2 * Math.PI);
        var trailDir = new Vec3(Math.Cos(rhizA), 0, Math.Sin(rhizA));
        double rhizLen = Math.Clamp(radius * 0.38, 0.12, 0.32) * scale;
        var rhizCol = Primitives.Mix(new[] { 0.08, 0.06, 0.04 }, c1, 0.12);
        var rhizBase = new Vec3(0, 0.012, 0);
        var rhizTip = rhizBase - trailDir * rhizLen;
        Primitives.Tube(part,
            new[] { rhizTip, Vec3.Lerp(rhizTip, rhizBase, 0.5), rhizBase },
            new[] { 0.007, 0.009, 0.012 }, 6,
            (i, v) => (rhizCol, 1, (double)i / 2, v, 0, 0));

        int buds = 2 + rng.NextInt(2); // 2-3 buds along the rhizome
        for (int k = 0; k < buds; k++)
        {
            double t = 0.30 + 0.30 * k;
            var budPos = Vec3.Lerp(rhizTip, rhizBase, t) + new Vec3(0, 0.002, 0);
            // Small buds, not marbles: the olive-marble read must not return (was 0.036).
            Primitives.Ellipsoid(part, budPos, new Vec3(0.014, 0.010, 0.014), 3, 6,
                (u, v) => (rhizCol, 1, u, v, 0, 0));
        }

        ClampNewFernsToGround(part);
        NewFloraAppendWorld(m, part, radius, height);
    }

    /// <summary>
    /// Mirrorstrap fern: glossy UNDIVIDED straps of shaded crevices — hartstongue, not
    /// an agave rosette. Eight to nine broad, long, flat straps (no pinnae, smooth
    /// margins: LobeCount 0) leave the base nearly horizontal then arch firmly over
    /// (strong Droop), so each blade reads as a varnished tongue lying flat from the
    /// crevice, not a stiff upright V. Thin lamina (0.7 mm) removes the succulent
    /// thickness impression; a strong dark midrib tube (0.0032 radius, 0.38x c1)
    /// holds the two halves like a hinge, baked into the vertex colours along the
    /// blade centre so the stripe reads as a continuous dark hinge from above.
    /// Straps are flagged as leaf tissue (UV2.y = 1) so the shader applies the waxy,
    /// low-roughness varnish optics instead of matte epidermis. Evergreen deep
    /// green, lifted and scaled 1.4x; the tip is only gently lightened, never
    /// white-hot plastic. Polish pass only: undivided form and footprint kept.
    /// </summary>
    private static void Mirrorstrap(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, double radius, double height, bool juvenile)
    {
        var part = new MeshData { Lod = m.Lod, FloraDetailLevel = m.FloraDetailLevel, FloraVisualSeed = m.FloraVisualSeed };
        int detail = m.FloraDetailLevel ?? 0;
        double h = Math.Max(0.05, height);
        double scale = juvenile ? 0.65 : 1.0;

        int straps = NewFloraCount(m, 8 + rng.NextInt(2)); // 8-9: a fuller tongue fan for the wide read, not a dense rosette
        // Sheet DetailLevel is a tessellation knob: 4 = 20x10 grid, falling with detail/LOD.
        int sheetDetail = detail switch { 0 => 4, 1 => 3, _ => 2 };
        if (m.Lod is { Blade: < 0.5 }) sheetDetail = Math.Max(1, sheetDetail - 1);
        int axisSegs = Math.Max(6, (int)Math.Round(12 * (m.Lod?.Length ?? 1)));
        // Dark hinge midrib, ~3.2 mm radius: intentional darkness, kept from the
        // leaf lift below so the stripe still reads near-black against the strap.
        var midribCol = Primitives.Scale(c1, 0.38);
        // Lifted strap pigment: mid-bright deep green base (1.4x), with a
        // gently lightened tip (1.15x, never white-hot) for the varnish glint.
        var leafC1 = Primitives.Scale(Primitives.Mix(c1, c2, 0.40), 1.40);
        var strapTipCol = Primitives.Scale(
            Primitives.Mix(Primitives.Mix(c1, c2, 0.55), new[] { 1.0, 1.0, 0.92 }, 0.15), 1.15);

        for (int k = 0; k < straps; k++)
        {
            double az = 2 * Math.PI * k / straps + rng.Range(-0.2, 0.2);
            double len = h * 1.30 * scale * rng.Range(0.82, 1.14); // individual strap lengths break the uniform rosette read
            var axisParams = new AxisParams(
                Length: len,
                BaseAngle: rng.Range(1.30, 1.55), // spread straps at varied angles across the crevice...
                BaseAzimuth: az,
                Droop: rng.Range(1.25, 1.45), // ...then arch over (bend back across the crown)
                PhototropicBend: 0,
                TwistPerLength: 0,
                WobbleAmplitude: 0.001, // smooth strap
                WobbleFrequency: 2.0,
                Segments: axisSegs);
            ulong s = Rng.Mix(seed, (ulong)(k * 701 + 13));
            var sheetParams = new SheetParams(
                axisParams,
                HalfWidth: h * 0.195 * scale, // broad tongue strap: ~3.3:1, far wider than any grass blade (was 0.160)
                LobeCount: 0, // undivided: smooth margins, no lobing, no pinnae
                LobeDepth: 0.0,
                Thickness: 0.0007, // flatter lamina: no succulent read
                Curl: rng.Range(0.035, 0.075), // modest individual curl softens the uniform strap fan
                DetailLevel: sheetDetail);

            int lv = part.VertexCount;
            int li = part.Indices.Count;
            Sheet.Build(part, sheetParams, s, leafC1, strapTipCol);
            // Flag the strap as leaf tissue so the flora shader applies the waxy
            // low-roughness varnish optics (Sheet writes UV2.y = 0, matte
            // epidermis). Then bake the dark hinge into the vertex colours along
            // the blade centre (UV.y across the strap, centre 0.5), so the
            // midrib reads from above even where the tube sits under the lamina.
            // Polished band: slightly wider and stronger so the hinge is a
            // continuous dark stripe, not a pale seam.
            for (int v = lv; v < part.VertexCount; v++)
            {
                part.UV2[v * 2 + 1] = 1f;
                double across = part.UV[v * 2 + 1] - 0.5;
                double d = Math.Abs(across);
                if (d < 0.19)
                {
                    double shadeK = (1.0 - d / 0.19) * 0.85;
                    var cur = new[] { (double)part.Colors[v * 4], (double)part.Colors[v * 4 + 1], (double)part.Colors[v * 4 + 2] };
                    var dark = Primitives.Mix(cur, midribCol, shadeK);
                    part.SetColor(v, dark[0], dark[1], dark[2], 1.0);
                }
            }
            part.RecordLeaf(lv, li, Vec3.Zero, len);

            // Strong dark midrib tube under the lamina, along the same axis.
            var frames = Axis.Build(axisParams, s);
            var path = new Vec3[frames.Count];
            var radii = new double[frames.Count];
            for (int i = 0; i < frames.Count; i++)
            {
                path[i] = frames[i].Point + new Vec3(0, -0.0012, 0);
                radii[i] = 0.0032 * (1.0 - 0.4 * (double)i / Math.Max(1, frames.Count - 1));
            }
            Primitives.Tube(part, path, radii, 6,
                (i, v) => (midribCol, 1, (double)i / Math.Max(1, frames.Count - 1), v, 0, 0));
        }

        ClampNewFernsToGround(part);
        NewFloraAppendWorld(m, part, radius, height);
    }
}
