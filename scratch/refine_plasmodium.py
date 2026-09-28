import os

file_path = "src/Vivarium.Sim/Geometry/OrganismMeshes.cs"
recovery_path = "build/recovery/OrganismMeshes-reviewed.cs"

with open(file_path, "r", encoding="utf-8", newline="") as f:
    content = f.read()

new_plasmodium = """    /// <summary>
    /// Slime-mold plasmodium (Physarum polycephalum):
    /// Ground-hugging planar anastomosing network with sinuous closed-loop multi-tiered veins,
    /// fleshy triangular delta junction webbing, fine capillary reticulations, and a broad
    /// advancing foraging fan with scalloped pseudopodial lobes.
    /// </summary>
    private static void Plasmodium(MeshData m, Rng rng, double[] c1, double[] c2)
    {
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

        void SinuousTube(Vec3 pA, Vec3 pB, double rStart, double rEnd, double curveFactor, double[] col, int sides = 5)
        {
            var dir = pB - pA;
            double len = dir.Length;
            if (len < 1e-4) return;
            var normDir = dir.Normalized();
            var perp = new Vec3(-normDir.Z, 0, normDir.X).Normalized();

            int segs = 4;
            var pts = new Vec3[segs + 1];
            var radii = new double[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                double t = i / (double)segs;
                double wave = Math.Sin(t * Math.PI) * curveFactor;
                double jY = Math.Sin(t * Math.PI) * 0.008;
                pts[i] = pA + dir * t + perp * wave + Vec3.Up * jY;
                radii[i] = (rStart * (1 - t) + rEnd * t) * (1.0 + 0.12 * Math.Sin(t * Math.PI * 2.0));
            }
            Primitives.Tube(m, pts, radii, sides, (step, u) =>
            {
                double t = step / (double)segs;
                var c = Primitives.Mix(col, fanCol, t * 0.35);
                return (c, 1.0, step, u, 0, 0);
            });
        }

        // Broad spreading fan layout
        var origin = LocalToWorld(-0.55, 0.08, 0.0);
        var nodes = new List<Vec3>();

        int AddNode(double lx, double lz, double ly)
        {
            int idx = nodes.Count;
            double jx = rng.Range(-0.02, 0.02);
            double jz = rng.Range(-0.02, 0.02);
            nodes.Add(LocalToWorld(lx + jx, ly, lz + jz));
            return idx;
        }

        int nOrig = AddNode(-0.55, 0.0, 0.08);

        // Tier 1: 3 nodes (R ≈ 0.28 from origin)
        int n1_0 = AddNode(-0.35, -0.25, 0.09);
        int n1_1 = AddNode(-0.28,  0.00, 0.10);
        int n1_2 = AddNode(-0.35,  0.25, 0.09);

        // Tier 2: 5 nodes (R ≈ 0.55 from origin, broad fan)
        int n2_0 = AddNode(-0.08, -0.46, 0.09);
        int n2_1 = AddNode(-0.02, -0.22, 0.11);
        int n2_2 = AddNode( 0.04,  0.00, 0.12); // main central confluence hub
        int n2_3 = AddNode(-0.02,  0.22, 0.11);
        int n2_4 = AddNode(-0.08,  0.46, 0.09);

        // Tier 3: 6 nodes (R ≈ 0.82 from origin)
        int n3_0 = AddNode(0.24, -0.58, 0.08);
        int n3_1 = AddNode(0.32, -0.32, 0.10);
        int n3_2 = AddNode(0.38, -0.11, 0.11);
        int n3_3 = AddNode(0.38,  0.11, 0.11);
        int n3_4 = AddNode(0.32,  0.32, 0.10);
        int n3_5 = AddNode(0.24,  0.58, 0.08);

        // Tier 4: 8 feeder nodes along the inner fan margin (R ≈ 1.02)
        int n4_0 = AddNode(0.50, -0.62, 0.07);
        int n4_1 = AddNode(0.60, -0.42, 0.08);
        int n4_2 = AddNode(0.68, -0.24, 0.09);
        int n4_3 = AddNode(0.72, -0.08, 0.09);
        int n4_4 = AddNode(0.72,  0.08, 0.09);
        int n4_5 = AddNode(0.68,  0.24, 0.09);
        int n4_6 = AddNode(0.60,  0.42, 0.08);
        int n4_7 = AddNode(0.50,  0.62, 0.07);

        // Closed-loop anastomosing network with sinuous curves
        (int from, int to, double rA, double rB, double curve)[] edges =
        {
            // Tier 0 -> Tier 1
            (nOrig, n1_0, 0.065, 0.055, -0.035),
            (nOrig, n1_1, 0.072, 0.062,  0.015),
            (nOrig, n1_2, 0.065, 0.055,  0.035),
            // Tier 1 transverse loop
            (n1_0, n1_1, 0.046, 0.052, 0.025),
            (n1_1, n1_2, 0.052, 0.046, -0.025),

            // Tier 1 -> Tier 2
            (n1_0, n2_0, 0.052, 0.044, -0.03),
            (n1_0, n2_1, 0.048, 0.050,  0.025),
            (n1_1, n2_1, 0.055, 0.050, -0.025),
            (n1_1, n2_2, 0.065, 0.058,  0.015), // central arterial cord
            (n1_1, n2_3, 0.055, 0.050,  0.025),
            (n1_2, n2_3, 0.048, 0.050, -0.025),
            (n1_2, n2_4, 0.052, 0.044,  0.03),
            // Tier 2 transverse loops
            (n2_0, n2_1, 0.042, 0.046, 0.02),
            (n2_1, n2_2, 0.048, 0.054, -0.02),
            (n2_2, n2_3, 0.054, 0.048, 0.02),
            (n2_3, n2_4, 0.046, 0.042, -0.02),

            // Tier 2 -> Tier 3
            (n2_0, n3_0, 0.042, 0.036, -0.025),
            (n2_0, n3_1, 0.038, 0.042,  0.025),
            (n2_1, n3_1, 0.048, 0.044, -0.02),
            (n2_1, n3_2, 0.044, 0.048,  0.025),
            (n2_2, n3_2, 0.058, 0.050, -0.02),
            (n2_2, n3_3, 0.058, 0.050,  0.02),
            (n2_3, n3_3, 0.044, 0.048, -0.025),
            (n2_3, n3_4, 0.048, 0.044,  0.02),
            (n2_4, n3_4, 0.038, 0.042, -0.025),
            (n2_4, n3_5, 0.042, 0.036,  0.025),
            // Tier 3 transverse loops
            (n3_0, n3_1, 0.034, 0.040, 0.02),
            (n3_1, n3_2, 0.040, 0.045, -0.02),
            (n3_2, n3_3, 0.048, 0.048, 0.015),
            (n3_3, n3_4, 0.045, 0.040, 0.02),
            (n3_4, n3_5, 0.040, 0.034, -0.02),

            // Tier 3 -> Tier 4
            (n3_0, n4_0, 0.034, 0.028, -0.02),
            (n3_1, n4_1, 0.040, 0.034, -0.015),
            (n3_1, n4_2, 0.036, 0.036,  0.02),
            (n3_2, n4_2, 0.044, 0.040, -0.02),
            (n3_2, n4_3, 0.046, 0.042,  0.015),
            (n3_3, n4_4, 0.046, 0.042, -0.015),
            (n3_3, n4_5, 0.044, 0.040,  0.02),
            (n3_4, n4_5, 0.036, 0.036, -0.02),
            (n3_4, n4_6, 0.040, 0.034,  0.015),
            (n3_5, n4_7, 0.034, 0.028,  0.02),
            // Tier 4 marginal transverse loop
            (n4_0, n4_1, 0.028, 0.032, 0.01),
            (n4_1, n4_2, 0.032, 0.035, 0.01),
            (n4_2, n4_3, 0.035, 0.038, 0.01),
            (n4_3, n4_4, 0.038, 0.038, 0.01),
            (n4_4, n4_5, 0.038, 0.035, 0.01),
            (n4_5, n4_6, 0.035, 0.032, 0.01),
            (n4_6, n4_7, 0.032, 0.028, 0.01),
        };

        foreach (var (from, to, rA, rB, curve) in edges)
        {
            SinuousTube(nodes[from], nodes[to], rA, rB, curve, coreCol, 5);
        }

        // Fleshy delta junction webbing at hubs
        int[] hubs = { nOrig, n1_1, n2_1, n2_2, n2_3, n3_2, n3_3 };
        foreach (int h in hubs)
        {
            var center = nodes[h];
            double jRad = h == n2_2 ? 0.09 : h == n1_1 ? 0.078 : 0.062;
            var rimPts = new List<Vec3>();
            for (int a = 0; a <= 12; a++)
            {
                double ang = a * Math.PI * 2.0 / 12.0;
                double rWobble = jRad * (1.0 + 0.20 * Math.Sin(ang * 3.0 + h));
                rimPts.Add(center + new Vec3(Math.Cos(ang) * rWobble, rng.Range(-0.003, 0.003), Math.Sin(ang) * rWobble));
            }
            Primitives.Fan(m, center + Vec3.Up * 0.012, rimPts, Vec3.Up, junctionCol, coreCol, 1, 2);
        }

        // Protoplasmic droplets / nodes inside bays
        (int, int)[] dropletPairs = { (n1_0, n2_1), (n1_2, n2_3), (n2_0, n3_1), (n2_4, n3_4), (n2_1, n3_2), (n2_3, n3_3) };
        foreach (var (a, b) in dropletPairs)
        {
            var pos = (nodes[a] + nodes[b]) * 0.5 + new Vec3(rng.Range(-0.015, 0.015), 0.005, rng.Range(-0.015, 0.015));
            Primitives.Ellipsoid(m, pos, new Vec3(0.026, 0.016, 0.026), 4, 6,
                (u, v) => (junctionCol, 1.0, u, v, 0, 0));
        }

        // Advancing Foraging Fan (Leading Protoplasmic Apron with Scalloped Lobes)
        int[] t4Nodes = { n4_0, n4_1, n4_2, n4_3, n4_4, n4_5, n4_6, n4_7 };
        for (int i = 0; i < t4Nodes.Length - 1; i++)
        {
            var anchorA = nodes[t4Nodes[i]];
            var anchorB = nodes[t4Nodes[i + 1]];
            var anchorMid = (anchorA + anchorB) * 0.5;

            var outDir = (anchorMid - origin).Normalized();
            var sideDir = new Vec3(-outDir.Z, 0, outDir.X).Normalized();

            double lobeReach = rng.Range(0.24, 0.32);
            var fanRim = new List<Vec3>();
            int rimSteps = 8;
            for (int s = 0; s <= rimSteps; s++)
            {
                double st = s / (double)rimSteps;
                double sideOffset = (st - 0.5) * 0.22;
                double forwardReach = lobeReach * Math.Sin(st * Math.PI);
                double crenulation = 0.022 * Math.Sin(st * Math.PI * 4.0);
                var edgePt = anchorMid + sideDir * sideOffset + outDir * (forwardReach + crenulation);
                edgePt += new Vec3(0, -0.025 * (1.0 - Math.Sin(st * Math.PI)), 0);
                fanRim.Add(edgePt);
            }

            Primitives.Fan(m, anchorMid, fanRim, Vec3.Up, fanCol, junctionCol, 1, 2);

            // Small exploratory pseudopodial finger tips branching from lobe apex
            if (i % 2 == 1 || i == 3)
            {
                var tipP = fanRim[rimSteps / 2];
                var fMid = tipP + outDir * rng.Range(0.04, 0.07) + new Vec3(rng.Range(-0.015, 0.015), 0, rng.Range(-0.015, 0.015));
                var fEnd = fMid + outDir * rng.Range(0.03, 0.05);
                Primitives.Tube(m, new[] { tipP, fMid, fEnd }, new[] { 0.016, 0.010, 0.003 }, 4,
                    (st, u) => (fanCol, 1.0, st, u, 0, 0));
            }
        }
    }"""

# Normalise newlines
crlf = "\r\n" in content
nl = "\r\n" if crlf else "\n"

start_marker = "    /// <summary>\r\n    /// Slime-mold plasmodium (Physarum polycephalum):"
if start_marker not in content:
    start_marker = "    /// <summary>\n    /// Slime-mold plasmodium (Physarum polycephalum):"

end_marker = "    private static readonly double[] WoodyBark = { 0.30, 0.24, 0.18 };"

start_idx = content.find(start_marker)
end_idx = content.find(end_marker)

assert start_idx != -1, "start_marker not found"
assert end_idx != -1, "end_marker not found"

new_plasmodium_norm = new_plasmodium.replace("\r\n", "\n").replace("\n", nl) + nl + nl
content = content[:start_idx] + new_plasmodium_norm + content[end_idx:]

with open(file_path, "w", encoding="utf-8", newline="") as f:
    f.write(content)

print(f"Updated {file_path}")

if os.path.exists(recovery_path):
    with open(recovery_path, "r", encoding="utf-8", newline="") as f:
        r_content = f.read()
    r_start_idx = r_content.find(start_marker)
    r_end_idx = r_content.find(end_marker)
    if r_start_idx != -1 and r_end_idx != -1:
        r_content = r_content[:r_start_idx] + new_plasmodium_norm + r_content[r_end_idx:]
        with open(recovery_path, "w", encoding="utf-8", newline="") as f:
            f.write(r_content)
        print(f"Updated {recovery_path}")
