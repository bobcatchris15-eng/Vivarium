import os

file_path = "src/Vivarium.Sim/Geometry/OrganismMeshes.cs"
recovery_path = "build/recovery/OrganismMeshes-reviewed.cs"

with open(file_path, "r", encoding="utf-8", newline="") as f:
    content = f.read()

# 1. New FloraFruiting
old_fruiting = """    public static MeshData? FloraFruiting(FloraSpeciesDef sp, ulong seed = 1)
    {
        if (sp.Shape != "plasmodium") return null;
        var rng = Rng.Keyed(seed, "flora.fruit." + sp.Id, 0);
        var m = new MeshData();
        var stalk = new[] { 0.35, 0.28, 0.16 };
        var head = new[] { 0.18, 0.14, 0.1 };
        // a faded network left behind, crowded with tiny stalked spore cases (height axis is stretched ×8 below)
        Plasmodium(m, rng, Primitives.Scale(sp.Color, 0.55), Primitives.Scale(sp.Color2, 0.5));
        for (int k = 0; k < 40; k++)
        {
            double ang = rng.Range(0, 2 * Math.PI), r = Math.Sqrt(rng.NextDouble()) * 0.8;
            var b = new Vec3(Math.Cos(ang) * r, 0.2, Math.Sin(ang) * r);
            double h = rng.Range(4, 7);
            Primitives.Tube(m, new[] { b, b + new Vec3(0, h, 0) }, new[] { 0.012, 0.008 }, 3, (i, v) => (stalk, 1, i, v, 0, 0));
            Primitives.Ellipsoid(m, b + new Vec3(0, h + 0.5, 0), new Vec3(0.04, 0.6, 0.04), 4, 6, (u, v) => (head, 1, u, v, 0, 0));
        }
        return m;
    }"""

new_fruiting = """    public static MeshData? FloraFruiting(FloraSpeciesDef sp, ulong seed = 1)
    {
        if (sp.Shape != "plasmodium") return null;
        var rng = Rng.Keyed(seed, "flora.fruit." + sp.Id, 0);
        var m = new MeshData();

        // 1. Withered vein residue tracks on the substrate
        // The protoplasm has drained into the sporangia, leaving dry, flattened silvery-buff residue
        var residueCol = new[] { 0.42, 0.38, 0.30 };
        var residueEdgeCol = new[] { 0.32, 0.28, 0.22 };
        var stipeCol = new[] { 0.10, 0.07, 0.04 };          // dark shiny obsidian/bronze wire
        var sporangiumCol = new[] { 0.16, 0.11, 0.07 };     // rich chocolate bronze / amber-black capsule
        var peridiumCapCol = new[] { 0.68, 0.62, 0.48 };    // pale powdery cap / dehiscent apex

        // Draw flat residue network
        void ResidueTrack(Vec3 a, Vec3 b, double width)
        {
            var dir = b - a;
            if (dir.Length < 1e-4) return;
            var perp = new Vec3(-dir.Z, 0, dir.X).Normalized() * (width * 0.5);
            var p0 = a - perp; var p1 = a + perp;
            var p2 = b + perp; var p3 = b - perp;
            int idx = m.VertexCount;
            m.Positions.AddRange(new[] {
                (float)p0.X, (float)p0.Y, (float)p0.Z,
                (float)p1.X, (float)p1.Y, (float)p1.Z,
                (float)p2.X, (float)p2.Y, (float)p2.Z,
                (float)p3.X, (float)p3.Y, (float)p3.Z,
            });
            m.Normals.AddRange(new[] { 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f, 0f, 0f, 1f, 0f });
            for (int i = 0; i < 4; i++)
            {
                var c = i == 0 || i == 3 ? residueEdgeCol : residueCol;
                m.Colors.AddRange(new[] { (float)c[0], (float)c[1], (float)c[2], 1.0f });
                m.UV.AddRange(new[] { 0f, 0f });
                m.UV2.AddRange(new[] { 0f, 0f });
            }
            m.Indices.AddRange(new[] { idx, idx + 1, idx + 2, idx, idx + 2, idx + 3 });
        }

        // Generate residue track paths radiating and looping
        var hubPositions = new List<Vec3>();
        int trackRays = 7;
        for (int r = 0; r < trackRays; r++)
        {
            double ang = r * Math.PI * 2.0 / trackRays + rng.Range(-0.15, 0.15);
            var prev = new Vec3(rng.Range(-0.05, 0.05), 0.03, rng.Range(-0.05, 0.05));
            int segments = 4 + rng.NextInt(3);
            for (int s = 1; s <= segments; s++)
            {
                double dist = (s / (double)segments) * rng.Range(0.65, 0.95);
                double wAng = ang + rng.Range(-0.25, 0.25);
                var next = new Vec3(Math.Cos(wAng) * dist, 0.03, Math.Sin(wAng) * dist);
                ResidueTrack(prev, next, 0.05 * (1.0 - dist * 0.4));
                if (s >= 2) hubPositions.Add((prev + next) * 0.5);
                prev = next;
            }
        }

        // 2. Forest of delicate erect sporangia stipes crowned with spore capsules
        // ~65-85 delicate fruiting bodies rising along the tracks and hubs
        int sporangiaCount = 65 + rng.NextInt(20);
        for (int i = 0; i < sporangiaCount; i++)
        {
            Vec3 baseP;
            if (hubPositions.Count > 0 && rng.NextDouble() < 0.75)
            {
                var hub = hubPositions[rng.NextInt(hubPositions.Count)];
                baseP = hub + new Vec3(rng.Range(-0.04, 0.04), 0, rng.Range(-0.04, 0.04));
            }
            else
            {
                double ang = rng.Range(0, Math.PI * 2);
                double dist = Math.Sqrt(rng.NextDouble()) * 0.75;
                baseP = new Vec3(Math.Cos(ang) * dist, 0.03, Math.Sin(ang) * dist);
            }

            // Stipe height in mesh space: 1.2 to 2.2 (which scales to 2.4 - 4.4 cm in world)
            double stipeH = rng.Range(1.2, 2.2);
            // Slight natural nod / lean
            var lean = new Vec3(rng.Range(-0.08, 0.08), 0, rng.Range(-0.08, 0.08));
            var pBottom = baseP;
            var pMid = baseP + Vec3.Up * (stipeH * 0.6) + lean * 0.5;
            var pTop = baseP + Vec3.Up * stipeH + lean;

            // Hair-like slender dark stipe
            Primitives.Tube(m, new[] { pBottom, pMid, pTop }, new[] { 0.015, 0.010, 0.007 }, 3,
                (step, u) => (stipeCol, 1.0, step, u, 0, 0));

            // Nodding ovoid/ellipsoidal spore capsule (sporangium)
            var nodDir = (lean.Normalized() * 0.6 + Vec3.Up * 0.8).Normalized();
            var capCenter = pTop + nodDir * 0.08;
            Primitives.Ellipsoid(m, capCenter, new Vec3(0.042, 0.085, 0.042), 5, 8,
                (u, v) =>
                {
                    // Slightly lighter powdery apical cap
                    var c = v > 0.75 ? Primitives.Mix(sporangiumCol, peridiumCapCol, (v - 0.75) * 3.0) : sporangiumCol;
                    return (c, 1.0, u, v, 0, 0);
                });
        }

        return m;
    }"""

# 2. New Plasmodium
old_plasmodium = """    /// <summary>Slime-mold plasmodium: a branching fan of flattened yellow veins with a thin advancing front.</summary>
    private static void Plasmodium(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        void Branch(Vec3 from, double ang, double len, double rad, int depth)
        {
            var path = new List<Vec3> { from }; var rs = new List<double> { rad };
            var p = from;
            for (int i = 1; i <= 5; i++)
            {
                ang += rng.Range(-0.35, 0.35);
                p += new Vec3(Math.Cos(ang), 0, Math.Sin(ang)) * (len / 5);
                path.Add(new Vec3(p.X, 0.3 + 0.2 * rng.NextDouble(), p.Z)); rs.Add(rad * (1 - i / 7.0));
            }
            Primitives.Tube(m, path, rs, 4, (i, v) => (Primitives.Mix(c1, c2, i / 5.0), 1, i, v, 0, 0));
            if (depth <= 0) return;
            for (int b = 0; b < 2; b++) Branch(path[3 + b], ang + (b == 0 ? -0.6 : 0.6) + rng.Range(-0.2, 0.2), len * 0.6, rad * 0.6, depth - 1);
        }
        double baseAng = rng.Range(0, 2 * Math.PI);
        for (int k = 0; k < 5; k++) Branch(new Vec3(0, 0.3, 0), baseAng + k * 0.5 - 1.0, rng.Range(0.6, 0.85), 0.06, 2);
        // advancing front: a thin fan sheet on the leading side
        var rim = new List<Vec3>();
        for (int s = 0; s <= 14; s++)
        {
            double th = baseAng - 1.3 + 2.6 * s / 14;
            double rr = 0.9 + 0.1 * Math.Sin(s * 1.7);
            rim.Add(new Vec3(Math.Cos(th) * rr, 0.2, Math.Sin(th) * rr));
        }
        Primitives.Fan(m, new Vec3(Math.Cos(baseAng) * 0.55, 0.2, Math.Sin(baseAng) * 0.55), rim, Vec3.Up, Primitives.Scale(c2, 0.95), c1, 1, 2);
    }"""

new_plasmodium = """    /// <summary>
    /// Slime-mold plasmodium (Physarum polycephalum):
    /// Low, ground-hugging planar anastomosing network with interconnected closed-loop veins,
    /// fleshy triangular junction webbing, protoplasmic capillary reticulations, and a broad
    /// advancing foraging fan with lobate crenulated pseudopodial fingers.
    /// </summary>
    private static void Plasmodium(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        // Colors: c1 = rich golden amber core, c2 = bright canary yellow front/capillaries
        var coreCol = c1;
        var fanCol = c2;
        var capCol = Primitives.Mix(c1, c2, 0.65);
        var junctionCol = Primitives.Mix(c1, c2, 0.35);

        double baseAngle = rng.Range(0, Math.PI * 2);
        double cs = Math.Cos(baseAngle), sn = Math.Sin(baseAngle);
        Vec3 LocalToWorld(double lx, double ly, double lz)
        {
            return new Vec3(lx * cs - lz * sn, ly, lx * sn + lz * cs);
        }

        // Helper: draw low flattened ground tube
        void VeinTube(IReadOnlyList<Vec3> pts, IReadOnlyList<double> radii, double[] col, int sides = 5)
        {
            if (pts.Count < 2) return;
            Primitives.Tube(m, pts, radii, sides, (step, u) =>
            {
                double t = step / (double)(pts.Count - 1);
                var c = Primitives.Mix(col, fanCol, t * 0.35);
                return (c, 1.0, step, u, 0, 0);
            });
        }

        // 1. Primary Trunk Cords (anastomosing major conduits)
        // A network of nodes that loop back into each other
        var nodePositions = new Dictionary<int, Vec3>();
        // Node 0: trailing confluence / origin
        nodePositions[0] = LocalToWorld(-0.65, 0.07, 0.0);
        // Primary loop nodes
        nodePositions[1] = LocalToWorld(-0.35, 0.09, 0.28);
        nodePositions[2] = LocalToWorld(-0.38, 0.08, -0.26);
        nodePositions[3] = LocalToWorld(-0.05, 0.11, 0.42);
        nodePositions[4] = LocalToWorld(-0.08, 0.10, -0.38);
        nodePositions[5] = LocalToWorld(0.02, 0.12, 0.05);   // central major hub
        nodePositions[6] = LocalToWorld(0.32, 0.11, 0.32);
        nodePositions[7] = LocalToWorld(0.35, 0.10, -0.22);
        nodePositions[8] = LocalToWorld(0.30, 0.12, 0.06);   // pre-fan hub
        // Front margin feeder nodes
        nodePositions[9] = LocalToWorld(0.58, 0.09, 0.45);
        nodePositions[10] = LocalToWorld(0.65, 0.09, 0.20);
        nodePositions[11] = LocalToWorld(0.68, 0.09, -0.05);
        nodePositions[12] = LocalToWorld(0.62, 0.08, -0.35);

        // Add small deterministic jitter to nodes
        var nodeKeys = nodePositions.Keys.ToList();
        foreach (var k in nodeKeys)
        {
            var p = nodePositions[k];
            nodePositions[k] = p + new Vec3(rng.Range(-0.03, 0.03), rng.Range(-0.01, 0.01), rng.Range(-0.03, 0.03));
        }

        // Closed-loop vein edges (Anastomosis):
        (int from, int to, double rStart, double rEnd, double sagZ)[] primaryEdges =
        {
            (0, 1, 0.065, 0.055, 0.05),
            (0, 2, 0.060, 0.050, -0.04),
            (1, 2, 0.045, 0.045, -0.02), // closed loop 1!
            (1, 3, 0.055, 0.050, 0.06),
            (1, 5, 0.050, 0.055, -0.04), // cross-bridge to central hub!
            (2, 4, 0.050, 0.045, -0.05),
            (2, 5, 0.048, 0.055, 0.03),  // cross-bridge to central hub!
            (3, 5, 0.048, 0.055, -0.04), // closed loop 2!
            (4, 5, 0.045, 0.055, 0.04),  // closed loop 3!
            (3, 6, 0.050, 0.045, 0.05),
            (4, 7, 0.045, 0.040, -0.05),
            (5, 8, 0.060, 0.058, 0.02),  // main arterial vein to front!
            (6, 8, 0.045, 0.055, -0.03), // closed loop 4!
            (7, 8, 0.042, 0.055, 0.03),  // closed loop 5!
            (6, 9, 0.045, 0.035, 0.04),
            (6, 10, 0.042, 0.038, -0.03),
            (8, 10, 0.055, 0.042, 0.02),
            (8, 11, 0.052, 0.040, -0.02),
            (7, 11, 0.040, 0.038, 0.03),
            (7, 12, 0.040, 0.032, -0.04),
            (10, 11, 0.035, 0.035, 0.01), // transverse front vein!
            (9, 10, 0.032, 0.035, 0.02),  // transverse front vein!
            (11, 12, 0.032, 0.030, -0.02),// transverse front vein!
        };

        foreach (var (from, to, rStart, rEnd, sag) in primaryEdges)
        {
            var pA = nodePositions[from];
            var pB = nodePositions[to];
            var mid = (pA + pB) * 0.5;
            var dir = (pB - pA).Normalized();
            var perp = new Vec3(-dir.Z, 0, dir.X).Normalized();
            var pMid = mid + perp * sag + new Vec3(rng.Range(-0.02, 0.02), rng.Range(-0.005, 0.01), rng.Range(-0.02, 0.02));
            var pts = new[] { pA, pMid, pB };
            var radii = new[] { rStart, (rStart + rEnd) * 0.52, rEnd };
            VeinTube(pts, radii, coreCol, 5);
        }

        // 2. Fleshy Junction Webbing (Delta triangular membranes at hubs)
        int[] hubs = { 1, 2, 5, 8, 6, 7 };
        foreach (int h in hubs)
        {
            var center = nodePositions[h];
            double jRad = (h == 5 || h == 8) ? 0.09 : 0.065;
            var rimPts = new List<Vec3>();
            for (int a = 0; a <= 12; a++)
            {
                double ang = a * Math.PI * 2.0 / 12.0;
                double rWobble = jRad * (1.0 + 0.22 * Math.Sin(ang * 3.0 + h));
                rimPts.Add(center + new Vec3(Math.Cos(ang) * rWobble, rng.Range(-0.005, 0.005), Math.Sin(ang) * rWobble));
            }
            Primitives.Fan(m, center + Vec3.Up * 0.015, rimPts, Vec3.Up, junctionCol, coreCol, 1, 2);
        }

        // 3. Fine Tertiary Capillary Web inside the interior bays
        (Vec3 a, Vec3 b)[] capillaries =
        {
            (nodePositions[1] * 0.6 + nodePositions[5] * 0.4, nodePositions[2] * 0.6 + nodePositions[5] * 0.4),
            (nodePositions[3] * 0.5 + nodePositions[6] * 0.5, nodePositions[5] * 0.5 + nodePositions[8] * 0.5),
            (nodePositions[4] * 0.5 + nodePositions[7] * 0.5, nodePositions[5] * 0.5 + nodePositions[8] * 0.5),
            (nodePositions[0] * 0.4 + nodePositions[1] * 0.6, nodePositions[0] * 0.4 + nodePositions[2] * 0.6),
            (nodePositions[6] * 0.5 + nodePositions[10] * 0.5, nodePositions[8] * 0.5 + nodePositions[10] * 0.5),
            (nodePositions[7] * 0.5 + nodePositions[11] * 0.5, nodePositions[8] * 0.5 + nodePositions[11] * 0.5),
        };
        foreach (var (cA, cB) in capillaries)
        {
            var cMid = (cA + cB) * 0.5 + new Vec3(rng.Range(-0.02, 0.02), rng.Range(-0.004, 0.004), rng.Range(-0.02, 0.02));
            VeinTube(new[] { cA, cMid, cB }, new[] { 0.020, 0.018, 0.020 }, capCol, 4);
            // Small protoplasmic bead at the capillary midpoint
            Primitives.Ellipsoid(m, cMid + Vec3.Up * 0.008, new Vec3(0.025, 0.016, 0.025), 4, 6,
                (u, v) => (junctionCol, 1.0, u, v, 0, 0));
        }

        // 4. Advancing Foraging Fan (Leading Front Sheet with Crenulated Pseudopodial Lobes)
        int lobeCount = 9;
        double fanSpanStart = -1.15;
        double fanSpanEnd = 1.15;

        for (int l = 0; l < lobeCount; l++)
        {
            double t0 = l / (double)lobeCount;
            double t1 = (l + 1) / (double)lobeCount;
            double a0 = fanSpanStart + t0 * (fanSpanEnd - fanSpanStart);
            double a1 = fanSpanStart + t1 * (fanSpanEnd - fanSpanStart);
            double aMid = (a0 + a1) * 0.5;

            // Anchor point from the transverse front
            double tx = 0.50 + 0.15 * Math.Cos(aMid);
            double tz = 0.50 * Math.Sin(aMid);
            var anchor = LocalToWorld(tx, 0.08, tz);

            // Lobate finger reaches forward with rounded crenulated tip
            double reach = 0.90 + 0.14 * Math.Sin(l * 1.9 + 0.7) + rng.Range(-0.03, 0.03);

            var fanRim = new List<Vec3>();
            int rimSubdivs = 8;
            for (int s = 0; s <= rimSubdivs; s++)
            {
                double st = s / (double)rimSubdivs;
                double curAng = a0 + st * (a1 - a0);
                double profile = Math.Sin(st * Math.PI);
                double rCur = (reach - 0.08) + profile * 0.12;
                double rCrenulation = 0.025 * Math.Sin(st * Math.PI * 4.0);
                double rFinal = rCur + rCrenulation;
                var edgePos = LocalToWorld(rFinal * Math.Cos(curAng), 0.04 + profile * 0.02, rFinal * Math.Sin(curAng));
                fanRim.Add(edgePos);
            }

            Primitives.Fan(m, anchor, fanRim, Vec3.Up, fanCol, junctionCol, 1, 2);

            // Fine pseudopodial tip fingers extending beyond the rim
            if (l % 2 == 0)
            {
                var tipCenter = fanRim[rimSubdivs / 2];
                var fwdDir = (tipCenter - anchor).Normalized();
                var fingerMid = tipCenter + fwdDir * rng.Range(0.04, 0.08) + new Vec3(rng.Range(-0.015, 0.015), 0, rng.Range(-0.015, 0.015));
                var fingerEnd = fingerMid + fwdDir * rng.Range(0.03, 0.06);
                VeinTube(new[] { tipCenter, fingerMid, fingerEnd }, new[] { 0.018, 0.012, 0.004 }, fanCol, 4);
            }
        }
    }"""

# Normalise newlines to match content
crlf = "\r\n" in content
nl = "\r\n" if crlf else "\n"

old_fruiting_norm = old_fruiting.replace("\r\n", "\n").replace("\n", nl)
new_fruiting_norm = new_fruiting.replace("\r\n", "\n").replace("\n", nl)
old_plasmodium_norm = old_plasmodium.replace("\r\n", "\n").replace("\n", nl)
new_plasmodium_norm = new_plasmodium.replace("\r\n", "\n").replace("\n", nl)

assert old_fruiting_norm in content, "old_fruiting not found"
content = content.replace(old_fruiting_norm, new_fruiting_norm, 1)

assert old_plasmodium_norm in content, "old_plasmodium not found"
content = content.replace(old_plasmodium_norm, new_plasmodium_norm, 1)

with open(file_path, "w", encoding="utf-8", newline="") as f:
    f.write(content)

print(f"Updated {file_path}")

# Also update recovery path if it exists
if os.path.exists(recovery_path):
    with open(recovery_path, "r", encoding="utf-8", newline="") as f:
        r_content = f.read()
    r_crlf = "\r\n" in r_content
    r_nl = "\r\n" if r_crlf else "\n"
    r_old_fruiting = old_fruiting.replace("\r\n", "\n").replace("\n", r_nl)
    r_new_fruiting = new_fruiting.replace("\r\n", "\n").replace("\n", r_nl)
    r_old_plasmodium = old_plasmodium.replace("\r\n", "\n").replace("\n", r_nl)
    r_new_plasmodium = new_plasmodium.replace("\r\n", "\n").replace("\n", r_nl)
    if r_old_fruiting in r_content and r_old_plasmodium in r_content:
        r_content = r_content.replace(r_old_fruiting, r_new_fruiting, 1)
        r_content = r_content.replace(r_old_plasmodium, r_new_plasmodium, 1)
        with open(recovery_path, "w", encoding="utf-8", newline="") as f:
            f.write(r_content)
        print(f"Updated {recovery_path}")
