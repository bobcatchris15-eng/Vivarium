using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Species meshes. Flora meshes are built at unit scale (radius 1 in XZ, height 1 in Y) and scaled by the
/// individual's radius/height; fauna meshes are unit body length along +X (head forward), origin at the
/// underside centre.
/// Fauna vertex encoding (read by the fauna shader):
///   COLOR.rgb = offset from the appendage's attachment point (0 for body), COLOR.a = 1 on appendages;
///   UV = (position along body 0..1, around 0..1) for markings; UV2.x = material/visual region
///   (0 body, 1 marking-eligible body, 2 eye, 3 belly/light, 4 fin/limb); UV2.y = render-only
///   animation role (0 none/body, 1 locomotor limb, 2 wing/fin, 3 sensory appendage).
/// </summary>
public static class OrganismMeshes
{
    public static MeshData Flora(FloraSpeciesDef sp, ulong seed = 1, bool juvenile = false)
    {
        var rng = Rng.Keyed(seed, "flora.mesh." + sp.Id, 0);
        var m = new MeshData();
        var c1 = sp.Color; var c2 = sp.Color2;
        switch (sp.Shape)
        {
            case "carpet":
                if (sp.Id == "floodlace_moss") Floodlace(m, rng, seed, c1, c2);
                else if (sp.Id == "velvetweave_moss") Velvetweave(m, rng, seed, c1, c2);
                else CarpetDefault(m, rng, seed, c1, c2);
                break;
            case "cushion":
                PearlCushion(m, rng, seed, c1, c2);
                break;
            case "crust":
                Embercrust(m, rng, seed, c1, c2);
                break;
            case "foliose":
                RuffleLichen(m, rng, seed, c1, c2);
                break;
            case "creeper": Creeper(m, rng, seed, c1, c2); break;
            case "reed":
                if (sp.Id == "ringreed") Ringreed(m, rng, c1, c2);
                else Glassrush(m, rng, c1, c2, juvenile);
                break;
            case "herb":
            {
                // irregular basal crown of kernel-built lanceolate leaves (cambered, midrib-folded, drooping tips,
                // random overlapping angles/lengths, always GREEN regardless of species tint) around several
                // leaning flower stalks, each carrying a kernel-staged cambered cup coloured with the species
                // tint (lilac/violet) so the plant no longer reads as a flat lilac star.
                var leafBase = new[] { 0.14, 0.42, 0.10 };
                var leafTip = new[] { 0.32, 0.62, 0.22 };
                int nLeaves = 5 + rng.NextInt(3); // 5..7 irregular leaves; low detail retains 72 triangles per blade
                for (int k = 0; k < nLeaves; k++)
                {
                    double ang = rng.Range(0, 2 * Math.PI); // fully irregular, not evenly spaced -> leaves overlap
                    double len = rng.Range(0.55, 1.0);       // unequal lengths
                    var leafAxis = new AxisParams(Length: len, BaseAngle: rng.Range(0.5, 0.95), BaseAzimuth: ang,
                        Droop: rng.Range(0.7, 1.4), WobbleAmplitude: 0.015, WobbleFrequency: 1.1, Segments: 5);
                    ulong lSeed = Rng.Mix(seed, (ulong)(k * 401 + 3));
                    var bp = new LeafBladeParams(
                        Midrib: leafAxis, Profile: BladeProfile.Lanceolate, HalfWidth: rng.Range(0.08, 0.13),
                        Camber: rng.Range(0.05, 0.1), MidribFold: rng.Range(0.04, 0.09),
                        Asymmetry: rng.Range(-0.12, 0.12), MidribThickness: 0.003, DetailLevel: 0);
                    LeafBlade.Build(m, bp, lSeed, leafBase, leafTip);
                }
                int nFlowers = 2 + rng.NextInt(2); // 2..3 (kept off the requested 2..5 ceiling for the same reason)
                for (int f = 0; f < nFlowers; f++)
                {
                    double fang = rng.Range(0, 2 * Math.PI), fh = rng.Range(0.55, 1.05); // different heights
                    var stalkAxis = new AxisParams(Length: fh, BaseAngle: rng.Range(0.2, 0.55), BaseAzimuth: fang,
                        Droop: rng.Range(-0.05, 0.25), WobbleAmplitude: 0.012, Segments: 4);
                    ulong sSeed = Rng.Mix(seed, (ulong)(f * 613 + 71));
                    SoftTube.Build(m, new SoftTubeParams(stalkAxis, BaseRadius: 0.016, TipRadius: 0.01, Segments: 4), sSeed,
                        (i, v) => (leafBase, 1, i, v, 0, 0));
                    var top = Axis.Build(stalkAxis, sSeed)[^1].Point;
                    double stageT = (double)((seed + (ulong)f * 2) % 5) / 4.0; // guarantees a bud and a spent variant
                    FlowerHead(m, rng, Rng.Mix(seed, (ulong)(f * 97 + 5)), top, 0.12, stageT,
                        c1, c2, new[] { 0.95, 0.85, 0.35 });
                }
                break;
            }
            case "roundleaf":
                if (sp.Id == "mooncoin") Mooncoin(m, rng, seed, c1, c2);
                else if (sp.Id == "brooklace") Brooklace(m, rng, seed, c1, c2);
                else RoundLeaf(m, rng, seed, c1, c2);
                break;
            case "pairedleaf":
                if (sp.Id == "fenbead") Fenbead(m, rng, seed, c1, c2);
                else PairedLeaf(m, rng, seed, c1, c2);
                break;
            case "floatleaf": FloatLeaf(m, rng, seed, c1, c2); break;
            case "capitula": BogglassMoss(m, rng, seed, c1, c2); break;
            case "trifoliate": Trifold(m, rng, seed, c1, c2); break;
            case "iceplant": Glassfinger(m, rng, c1, c2); break;
            case "fern": case "veilfern": Fern(m, rng, seed, c1, c2, juvenile); break;
            case "vine":
                if (sp.Id == "spiralvine") Spiralvine(m, rng, seed, c1, c2, juvenile);
                else if (sp.Id == "fenhook") Fenhook(m, rng, seed, c1, c2, juvenile);
                else Clinglace(m, rng, seed, c1, c2, juvenile);
                break;
            case "vine_clinglace": case "clinglace": Clinglace(m, rng, seed, c1, c2, juvenile); break;
            case "vine_spiralvine": case "spiralvine": Spiralvine(m, rng, seed, c1, c2, juvenile); break;
            case "vine_fenhook": case "fenhook": Fenhook(m, rng, seed, c1, c2, juvenile); break;
            case "mushroom_cluster": Mushrooms(m, rng, seed, c1, c2, juvenile); break;
            case "bracket": Bracket(m, rng, c1, c2); break;
            case "plasmodium": Plasmodium(m, rng, c1, c2); break;
            case "succulent": Succulent(m, rng, seed, c1, c2, juvenile); break;
            case "tussock": Tussock(m, rng, seed, c1, c2, juvenile); break;
            case "kinkcane_brake": Kinkcane(m, rng, c1, c2); break;
            case "veilblade_curtain": Veilblade(m, rng, c1, c2); break;
            case "hookthicket_brake": Hookthicket(m, rng, c1, c2); break;
            case "sundew_mat": BlueSundewRosette(m, rng, c1, c2); break;
            case "pitcher_rosette": PitcherPlant(m, rng, c1, c2); break;
            case "snaptrap_rosette": SnapTrap(m, rng, c1, c2); break;
            case "rain_jelly": RainJelly(m, rng, c1, c2); break;
            case "carrion_bell": CarrionBell(m, rng, c1, c2); break;
            case "glass_antlers": GlassAntlers(m, rng, c1, c2); break;
            case "fruticose":
                if (sp.Id == "antlerlace_lichen") Antlerlace(m, rng, seed, c1, c2);
                else Fruticose(m, rng, c1, c2);
                break;
            case "tree_ironlace": TreeIronlace(m, rng, seed, c1, c2, juvenile); break;
            case "tree_umbraheart": TreeUmbraheart(m, rng, seed, c1, c2, juvenile); break;
            case "tree_fenneedle": TreeFenneedle(m, rng, seed, c1, c2, juvenile); break;
            case "tree_kiteleaf": TreeKiteleaf(m, rng, c1, c2); break;
            case "shrub_embercrown": ShrubEmbercrown(m, rng, seed, c1, c2, juvenile); break;
            case "shrub_lanternbrush": ShrubLanternbrush(m, rng, c1, c2); break;
            case "shrub_shadebell": ShrubShadebell(m, rng, seed, c1, c2, juvenile); break;
            case "ribbonweed": case "streamribbon": Streamribbon(m, rng, seed, c1, c2, juvenile); break;
            case "milfoil": case "fencomb": Fencomb(m, rng, seed, c1, c2, juvenile); break;
            default:
                Primitives.Ellipsoid(m, Vec3.Zero, new Vec3(1, 1, 1), 6, 8, (a, b) => (c1, 1, a, b, 0, 0));
                break;
        }
        return m;
    }

    /// <summary>Alternate pose for organisms with a visible reproductive stage (slime mold sporangia); null otherwise.</summary>
    /// <summary>Generates a single modular node foliage cluster for a climber segment (attached or ground runner).</summary>
    public static MeshData ClimberNode(FloraSpeciesDef sp, ulong seed, bool attached)
    {
        var m = new MeshData();
        var rng = Rng.Keyed(seed, "climber.node", 0);
        var c1 = sp.Color;
        var c2 = sp.Color2;
        switch (sp.Id)
        {
            case "clinglace": ClinglaceNode(m, rng, seed, c1, c2, attached); break;
            case "spiralvine": SpiralvineNode(m, rng, seed, c1, c2, attached); break;
            case "fenhook": FenhookNode(m, rng, seed, c1, c2, attached); break;
            default: ClinglaceNode(m, rng, seed, c1, c2, attached); break;
        }
        return m;
    }

    public static MeshData? FloraFruiting(FloraSpeciesDef sp, ulong seed = 1)
    {
        if (sp.Shape != "plasmodium")
        {
            if (sp.Reproduction is not { } rp) return null;
            var rngFruit = Rng.Keyed(seed, "flora.reproduction." + sp.Id, 0);
            var plant = Flora(sp, seed);
            var fruitCol = rp.FruitColor;
            var ripe2 = Primitives.Scale(fruitCol, 0.72);
            double rx = MathD.Clamp(rp.DisplaySize / Math.Max(0.02, sp.RadiusAtMax), 0.012, 0.18);
            double ry = MathD.Clamp(rp.DisplaySize / Math.Max(0.02, sp.Height), 0.008, 0.14);
            int count = rp.Form switch
            {
                "berry" => 18,
                "drupe" => 12,
                "nut" => 10,
                "pod" => 12,
                "cone" => 10,
                "capsule" => 14,
                "wind_seed" => 24,
                "achene" => 22,
                _ => 12,
            };

            for (int i = 0; i < count; i++)
            {
                double ang = rngFruit.Range(0, Math.PI * 2);
                double radial = rngFruit.Range(0.22, 0.88);
                double y = rngFruit.Range(sp.IsTree ? 0.58 : 0.42, 0.96);
                var c = new Vec3(Math.Cos(ang) * radial, y, Math.Sin(ang) * radial);
                var col = rngFruit.NextDouble() < 0.35 ? ripe2 : fruitCol;
                switch (rp.Form)
                {
                    case "pod":
                        Primitives.Ellipsoid(plant, c, new Vec3(rx * 0.55, ry * 2.5, rx * 0.55), 5, 8,
                            (u, v) => (col, 1, u, v, 0, 0), pitch: rngFruit.Range(-0.75, 0.75));
                        break;
                    case "cone":
                        Primitives.Ellipsoid(plant, c, new Vec3(rx * 0.9, ry * 1.8, rx * 0.9), 6, 9,
                            (u, v) => (Primitives.Mix(col, ripe2, v), 1, u, v, 0, 0), pitch: rngFruit.Range(-0.35, 0.35));
                        break;
                    case "capsule":
                        Primitives.Ellipsoid(plant, c, new Vec3(rx * 0.7, ry * 1.45, rx * 0.7), 5, 8,
                            (u, v) => (col, 1, u, v, 0, 0));
                        break;
                    case "wind_seed":
                    case "achene":
                    {
                        Primitives.Ellipsoid(plant, c, new Vec3(rx * 0.28, ry * 0.9, rx * 0.28), 4, 6,
                            (u, v) => (col, 1, u, v, 0, 0));
                        var pale = Primitives.Mix(col, new[] { 0.90, 0.86, 0.72 }, 0.72);
                        for (int k = 0; k < 4; k++)
                        {
                            double a = k * Math.PI * 0.5 + rngFruit.Range(-0.18, 0.18);
                            var tip = c + new Vec3(Math.Cos(a) * rx * 2.2, ry * 1.8, Math.Sin(a) * rx * 2.2);
                            Primitives.Tube(plant, new[] { c, tip }, new[] { rx * 0.055, rx * 0.018 }, 3,
                                (u, v) => (pale, 1, u, v, 0, 0));
                        }
                        break;
                    }
                    default:
                        Primitives.Ellipsoid(plant, c, new Vec3(rx, ry, rx), 5, 8,
                            (u, v) => (col, 1, u, v, 0, 0));
                        break;
                }
            }
            return plant;
        }

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
    }

    /// <summary>Appends <paramref name="src"/> into <paramref name="dst"/>, rotating positions and normals about
    /// world Y by <paramref name="yawRad"/>, then translating the result by <paramref name="translate"/>.</summary>
    private static void AppendRotatedY(MeshData dst, MeshData src, Vec3 translate, double yawRad)
    {
        double cs = Math.Cos(yawRad), sn = Math.Sin(yawRad);
        Vec3 Rot(Vec3 v) => new Vec3(v.X * cs - v.Z * sn, v.Y, v.X * sn + v.Z * cs);
        int baseIndex = dst.VertexCount;
        for (int i = 0; i < src.VertexCount; i++)
        {
            var p = Rot(src.Position(i)) + translate;
            dst.Positions.Add((float)p.X); dst.Positions.Add((float)p.Y); dst.Positions.Add((float)p.Z);
            var n = Rot(src.NormalAt(i));
            dst.Normals.Add((float)n.X); dst.Normals.Add((float)n.Y); dst.Normals.Add((float)n.Z);
        }
        dst.Colors.AddRange(src.Colors); dst.UV.AddRange(src.UV); dst.UV2.AddRange(src.UV2);
        foreach (var i in src.Indices) dst.Indices.Add(baseIndex + i);
    }

    /// <summary>Appends <paramref name="src"/> into <paramref name="dst"/> translated by <paramref name="translate"/>
    /// with no rotation (positions and normals both left in the source's orientation).</summary>
    private static void AppendTranslated(MeshData dst, MeshData src, Vec3 translate)
    {
        int baseIndex = dst.VertexCount;
        for (int i = 0; i < src.VertexCount; i++)
        {
            var p = src.Position(i) + translate;
            dst.Positions.Add((float)p.X); dst.Positions.Add((float)p.Y); dst.Positions.Add((float)p.Z);
        }
        dst.Normals.AddRange(src.Normals); dst.Colors.AddRange(src.Colors); dst.UV.AddRange(src.UV); dst.UV2.AddRange(src.UV2);
        foreach (var i in src.Indices) dst.Indices.Add(baseIndex + i);
    }

    private static void MarkBladeVertices(MeshData mesh, int start)
    {
        for (int i = start; i < mesh.VertexCount; i++) mesh.UV2[i * 2 + 1] = 1;
    }

    private static void MarkOvateBladeVertices(MeshData mesh, int start, Vec3 centre, Vec3 axis, double length)
    {
        for (int i = start; i < mesh.VertexCount; i++)
        {
            mesh.UV[i * 2] = (float)MathD.Clamp01(0.5 + (mesh.Position(i) - centre).Dot(axis) / length);
            mesh.UV2[i * 2 + 1] = 1;
        }
    }

    /// <summary>Shallow cup of cambered, thick petals (cheap ThickOvateLeaflet blades) around a small centre disc, staged by
    /// <paramref name="stageT"/> in [0,1]: 0 is a near-closed upright bud, ~0.5 is fully open and splayed, and 1 is
    /// spent (petals drooping past horizontal, discoloured, some dropped). Built at the origin and translated to
    /// <paramref name="topPos"/> so callers can place it atop any stalk/pedicel.</summary>
    private static void FlowerHead(MeshData m, Rng rng, ulong seed, Vec3 topPos, double size, double stageT,
        double[] petalCol1, double[] petalCol2, double[] centerCol, int petalCountMax = 7)
    {
        // petalCountMax caps the upper end of the 4..7 range: each petal is a full LeafBlade (a tri-cost floor
        // around 128 tris), so callers with a tight budget (e.g. one flower head per plant) can cap the count.
        int petalCount = 4 + rng.NextInt(Math.Max(1, petalCountMax - 3)); // 4..petalCountMax, unequal size/angle below
        double t = MathD.Clamp01(stageT);
        // pitch from vertical: bud stays near-upright/closed, opens outward, then droops past horizontal when spent
        double basePitch = t <= 0.5 ? MathD.Lerp(0.22, 1.2, t / 0.5) : MathD.Lerp(1.2, 2.5, (t - 0.5) / 0.5);
        double openness = t <= 0.5 ? MathD.Lerp(0.4, 1.0, t / 0.5) : 1.0;
        int visiblePetals = t > 0.7 ? Math.Max(2, petalCount - 1 - rng.NextInt(2)) : petalCount;
        double wiltAmt = t > 0.55 ? MathD.Clamp01((t - 0.55) / 0.45) : 0.0;
        var wilt = new[] { 0.5, 0.4, 0.2 };
        var col1 = wiltAmt > 0 ? Primitives.Mix(petalCol1, wilt, wiltAmt * 0.75) : petalCol1;
        var col2 = wiltAmt > 0 ? Primitives.Mix(petalCol2, wilt, wiltAmt * 0.75) : petalCol2;

        // Petals are cheap cambered ThickOvateLeaflet blades (same double-sided crown/camber technique the
        // pairedleaf leaves use) spread around a spherical (pitch, azimuth) direction, not full LeafBlade calls
        // per petal: a full LeafBlade has a ~128-tri floor in this kernel, which would blow the plant's tri
        // budget once multiplied by several flower heads.
        var tmp = new MeshData();
        for (int i = 0; i < visiblePetals; i++)
        {
            double ang = 2 * Math.PI * i / petalCount + rng.Range(-0.18, 0.18);
            double pitch = MathD.Clamp(basePitch + rng.Range(-0.12, 0.12), 0, Math.PI);
            double petalLen = size * openness * rng.Range(0.85, 1.3); // unequal petal size
            var azimuthDir = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
            var fwd = (Vec3.Up * Math.Cos(pitch) + azimuthDir * Math.Sin(pitch)).Normalized();
            var side = fwd.Cross(Vec3.Up);
            if (side.LengthSq < 1e-8) side = fwd.Cross(new Vec3(1, 0, 0));
            side = side.Normalized();
            var up = side.Cross(fwd).Normalized();
            var center = fwd * (petalLen * 0.55);
            ulong pSeed = Rng.Mix(seed, (ulong)(i * 331 + 13));
            ThickOvateLeaflet(tmp, center, fwd, side, up, petalLen * 1.3, petalLen * 0.55,
                petalLen * rng.Range(0.18, 0.32), pSeed, Primitives.Scale(col1, 0.9), col2);
        }
        double centerR = size * 0.22;
        var centerRim = new List<Vec3>();
        for (int s = 0; s <= 8; s++)
        {
            double th = 2 * Math.PI * s / 8;
            centerRim.Add(new Vec3(Math.Cos(th) * centerR, 0, Math.Sin(th) * centerR));
        }
        Primitives.Fan(tmp, new Vec3(0, centerR * 0.6, 0), centerRim, Vec3.Up, centerCol, Primitives.Scale(centerCol, 0.85));
        AppendTranslated(m, tmp, topPos);
    }

    /// <summary>Dense mat of cupped reniform/cordate leaflets on short arching petioles (dichondra, watercress),
    /// scattered across the full mat footprint like the old scattered-leaflet coverage. A handful of larger
    /// "hero" leaves (via the LeafBlade kernel) sit among many cheaper cupped-fan leaflets so the mat reads as
    /// dense and covers the whole footprint within the triangle budget. Youngest leaves are smaller and folded.</summary>
    // Low creeping ice-plant shoots with opposite, angular water-storing leaves.
    private static void Glassfinger(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        int shoots = 9 + rng.NextInt(3);
        for (int k = 0; k < shoots; k++)
        {
            double angle = 2 * Math.PI * k / shoots + rng.Range(-0.18, 0.18);
            var axis = new Vec3(Math.Cos(angle), 0, Math.Sin(angle));
            var side = new Vec3(-axis.Z, 0, axis.X);
            var origin = side * rng.Range(-0.10, 0.10);
            double reach = rng.Range(0.65, 0.83);
            var shoot = new[]
            {
                origin + new Vec3(0, 0.025, 0),
                origin + axis * (reach * 0.32) + side * rng.Range(-0.035, 0.035) + new Vec3(0, 0.035, 0),
                origin + axis * (reach * 0.65) + side * rng.Range(-0.055, 0.055) + new Vec3(0, 0.042, 0),
                origin + axis * reach + new Vec3(0, 0.055, 0),
            };
            Primitives.Tube(m, shoot, new[] { 0.018, 0.016, 0.013, 0.009 }, 6,
                (i, v) => (Primitives.Mix(c1, c2, 0.08), 1, i / 3.0, v, 0, 0));
            for (int node = 1; node <= 3; node++)
            {
                for (int pair = -1; pair <= 1; pair += 2)
                {
                    double turn = angle + pair * rng.Range(0.65, 1.12);
                    var direction = new Vec3(Math.Cos(turn), 0, Math.Sin(turn));
                    double length = rng.Range(0.24, 0.38) * (node == 3 ? 0.78 : 1);
                    double width = rng.Range(0.055, 0.077);
                    GlassfingerLeaf(m, rng, shoot[node], direction, length, width, c1, c2);
                }
            }
            if (k == 1 || k == 6)
                GlassfingerFlower(m, rng, shoot[2] + new Vec3(0, 0.21, 0), c1);
        }
    }

    private static void GlassfingerLeaf(MeshData m, Rng rng, Vec3 root, Vec3 direction,
        double length, double width, double[] green, double[] red)
    {
        var side = new Vec3(-direction.Z, 0, direction.X);
        double[] stations = { 0.0, 0.25, 0.57, 0.82, 1.0 };
        double[] girth = { 0.24, 0.83, 1.0, 0.68, 0.07 };
        var rings = new Vec3[stations.Length][];
        for (int i = 0; i < stations.Length; i++)
        {
            double t = stations[i];
            var center = root + direction * (length * t)
                + Vec3.Up * (0.035 + 0.055 * Math.Sin(t * Math.PI * 0.85));
            double w = width * girth[i];
            double h = w * 0.78;
            rings[i] = new[]
            {
                center + Vec3.Up * h,
                center - side * w - Vec3.Up * (h * 0.42),
                center + side * w - Vec3.Up * (h * 0.42),
            };
        }
        for (int face = 0; face < 3; face++)
        {
            int next = (face + 1) % 3;
            var normal = (rings[0][next] - rings[0][face])
                .Cross(rings[1][face] - rings[0][face]).Normalized();
            var ids = new int[stations.Length, 2];
            for (int i = 0; i < stations.Length; i++)
            {
                double blush = i switch { 4 => 0.88, 3 => 0.36, 2 => 0.08, _ => 0.02 };
                var col = Primitives.Mix(green, red, blush);
                if (face < 2) col = Primitives.Mix(col, new[] { 0.82, 0.91, 0.79 }, 0.16);
                ids[i, 0] = m.AddVertex(rings[i][face], normal, col, 1, stations[i], 0, 0, 1);
                ids[i, 1] = m.AddVertex(rings[i][next], normal, col, 1, stations[i], 1, 0, 1);
            }
            for (int i = 0; i < stations.Length - 1; i++)
            {
                Primitives.TriangleFacing(m, ids[i, 0], ids[i, 1], ids[i + 1, 0], normal);
                Primitives.TriangleFacing(m, ids[i, 1], ids[i + 1, 1], ids[i + 1, 0], normal);
            }
        }
        // Sparse pale epidermal flecks sit on the sloping facets, like ice-plant bladder cells.
        for (int i = 0; i < 3; i++)
        {
            double t = i switch { 0 => 0.36, 1 => 0.53, _ => 0.72 };
            int section = t < stations[2] ? 1 : 2;
            double blend = (t - stations[section]) / (stations[section + 1] - stations[section]);
            int edge = i % 2 == 0 ? 1 : 2;
            var ridge = Vec3.Lerp(rings[section][0], rings[section + 1][0], blend);
            var shoulder = Vec3.Lerp(rings[section][edge], rings[section + 1][edge], blend);
            var normal = (rings[section][edge] - rings[section][0])
                .Cross(rings[section + 1][0] - rings[section][0]).Normalized();
            var p = Vec3.Lerp(ridge, shoulder, rng.Range(0.31, 0.56)) + normal * 0.004;
            var fleck = new[] { 0.86, 0.96, 0.84 };
            int a = m.AddVertex(p - direction * 0.011, normal, fleck);
            int b = m.AddVertex(p + direction * 0.011, normal, fleck);
            int c = m.AddVertex(p + side * (edge == 1 ? -0.010 : 0.010), normal, fleck);
            Primitives.TriangleFacing(m, a, b, c, normal);
        }
    }

    private static void GlassfingerFlower(MeshData m, Rng rng, Vec3 center, double[] green)
    {
        var foot = center - Vec3.Up * 0.15;
        Primitives.Tube(m, new[] { foot, center }, new[] { 0.008, 0.006 }, 6,
            (i, v) => (green, 1, i, v, 0, 0));
        var pink = new[] { 0.81, 0.22, 0.47 };
        var pale = new[] { 0.97, 0.56, 0.69 };
        for (int petal = 0; petal < 16; petal++)
        {
            double a = 2 * Math.PI * petal / 16 + rng.Range(-0.035, 0.035);
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var side = new Vec3(-dir.Z, 0, dir.X);
            double len = rng.Range(0.10, 0.14);
            var rim = new[]
            {
                center + side * 0.006,
                center + dir * (len * 0.65) + side * 0.013 + Vec3.Up * 0.012,
                center + dir * len + Vec3.Up * 0.009,
                center + dir * (len * 0.65) - side * 0.013 + Vec3.Up * 0.012,
                center - side * 0.006,
            };
            Primitives.Fan(m, center + Vec3.Up * 0.01, rim, Vec3.Up, pink, pale);
        }
        Primitives.Ellipsoid(m, center + Vec3.Up * 0.012,
            new Vec3(0.025, 0.012, 0.025), 4, 8,
            (u, v) => (new[] { 0.96, 0.76, 0.32 }, 1, u, v, 0, 0));
    }

    // Glassrush: a compact, branchless fan of glassy jointed stems. A few mature
    // stalks carry narrow cattail-like pods, distinct from Ringreed's whorls.
    private static void Glassrush(MeshData m, Rng rng, double[] c1, double[] c2, bool juvenile)
    {
        int count = 17 + rng.NextInt(5);
        for (int k = 0; k < count; k++)
        {
            double angle = rng.Range(0, Math.PI * 2);
            double radius = Math.Sqrt(rng.NextDouble()) * 0.19;
            var foot = new Vec3(Math.Cos(angle) * radius, -0.025, Math.Sin(angle) * radius);
            var sway = new Vec3(Math.Cos(angle), 0, Math.Sin(angle)) * rng.Range(0.07, 0.28);
            bool podded = !juvenile && k % 5 == 0;
            double height = rng.Range(podded ? 0.75 : 0.51, podded ? 0.95 : 1.0);
            var joints = new Vec3[6];
            var radii = new double[6];
            for (int j = 0; j < joints.Length; j++)
            {
                double t = j / 5.0;
                joints[j] = foot + Vec3.Up * (height * t) + sway * (t * t * 0.85);
                radii[j] = 0.023 * (1 - t * 0.42);
            }
            var green = Primitives.Mix(c1, c2, rng.Range(0.15, 0.48));
            Primitives.Tube(m, joints, radii, 6,
                (i, v) => (Primitives.Mix(Primitives.Scale(green, 0.72), green, i / 5.0), 1, i, v, 0, 0));
            for (int j = 1; j < 5; j++)
            {
                var node = joints[j];
                // Short pale sheaths show each joint without Ringreed's dark collars.
                Primitives.Tube(m, new[] { node - Vec3.Up * 0.008, node + Vec3.Up * 0.008 },
                    new[] { radii[j] * 1.10, radii[j] * 1.08 }, 6,
                    (i, v) => (Primitives.Mix(green, c2, 0.52), 1, i, v, 0, 0));
            }
            if (!podded) continue;

            // A velvety seed cylinder with uneven shoulders and a pale, tapered crown.
            // Each pod inherits its stem's lean and has a slightly different length.
            var direction = (joints[5] - joints[4]).Normalized();
            double length = rng.Range(0.15, 0.21);
            double width = rng.Range(0.032, 0.044);
            var baseP = joints[5] + direction * 0.025;
            Primitives.Tube(m, new[] { joints[5], baseP }, new[] { radii[5] * 0.7, 0.009 }, 6,
                (i, v) => (green, 1, i, v, 0, 0));
            var podPath = new Vec3[7];
            var podRadii = new double[7];
            double[] profile = { 0.28, 0.79, 0.98, 1.0, 0.91, 0.68, 0.12 };
            for (int j = 0; j < podPath.Length; j++)
            {
                double t = j / 6.0;
                podPath[j] = baseP + direction * (length * t);
                podRadii[j] = width * profile[j];
            }
            var bronze = new[] { 0.55, 0.44, 0.27 };
            var dusk = new[] { 0.55, 0.50, 0.48 };
            Primitives.Tube(m, podPath, podRadii, 9,
                (i, v) => (Primitives.Mix(bronze, dusk, i >= 5 ? 0.62 : 0.18 + 0.08 * Math.Sin(v * Math.PI * 10)), 1, i / 6.0, v, 0, 0));
        }
    }

    // Ringreed: Great Horsetail (Equisetum telmateia) morphology with fluted segmented
    // culms, dark-toothed nodal sheaths, tiered drooping 4-angled branchlet whorls,
    // and terminal fertile strobilus spore cones.
    private static void Ringreed(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        var culmBaseCol = c1;
        var culmLightCol = Primitives.Mix(c1, c2, 0.40);
        var sheathCol = Primitives.Scale(c1, 0.28);
        var sheathTeethCol = Primitives.Mix(sheathCol, new[] { 0.08, 0.07, 0.06 }, 0.75);
        var branchCol = Primitives.Mix(c1, c2, 0.32);
        var strobilusCol = Primitives.Mix(c2, new[] { 0.82, 0.72, 0.42 }, 0.65);
        var strobilusScaleCol = Primitives.Scale(strobilusCol, 0.72);

        int culmCount = 11 + rng.NextInt(4);
        for (int k = 0; k < culmCount; k++)
        {
            double angle = rng.Range(0, Math.PI * 2);
            double dist = rng.Range(0.02, 0.28);
            var foot = new Vec3(Math.Cos(angle) * dist, 0, Math.Sin(angle) * dist);
            var swayDir = new Vec3(Math.Cos(angle + rng.Range(-0.5, 0.5)), 0, Math.Sin(angle + rng.Range(-0.5, 0.5)));
            double height = rng.Range(0.68, 1.0);
            bool isFertile = k < 3 || (k == 3 && rng.NextDouble() < 0.5);

            const int nodeCount = 8;
            var joints = new Vec3[nodeCount + 1];
            var radii = new double[nodeCount + 1];

            for (int j = 0; j <= nodeCount; j++)
            {
                double t = (double)j / nodeCount;
                joints[j] = foot + swayDir * (0.08 * t * t) + Vec3.Up * (height * t);
                radii[j] = 0.028 * (1.0 - t * 0.62);
            }

            // 1. Fluted segmented main culm (8 sides for vertical ribbing)
            Primitives.Tube(m, joints, radii, 8,
                (i, v) => (Primitives.Mix(culmBaseCol, culmLightCol, (i % 2 == 0 ? 0.2 : 0.6) + 0.3 * Math.Sin(v * 16)),
                           1.0, i / (double)nodeCount, v, 0, 0));

            // 2. Toothed nodal sheaths & tiered drooping branchlet whorls
            for (int j = 1; j < nodeCount; j++)
            {
                var collar = joints[j];
                double r = radii[j];
                double sheathH = 0.022 * (1.0 - (double)j / nodeCount * 0.4);

                // Flared sheath collar with dark tooth rim
                int sStart = m.VertexCount;
                const int sSides = 10;
                for (int si = 0; si <= sSides; si++)
                {
                    double th = 2 * Math.PI * si / sSides;
                    var nrm = new Vec3(Math.Cos(th), 0, Math.Sin(th));
                    var pBase = collar - Vec3.Up * (sheathH * 0.5) + nrm * (r * 1.04);
                    var pMid = collar + nrm * (r * 1.12);
                    // Pointed appressed black teeth
                    bool isTooth = (si % 2 == 1);
                    var pTop = collar + Vec3.Up * (sheathH * (isTooth ? 0.65 : 0.40)) + nrm * (r * (isTooth ? 1.08 : 1.18));

                    m.AddVertex(pBase, nrm, sheathCol, 1.0, (double)si / sSides, 0.0, 0, 0);
                    m.AddVertex(pMid, nrm, sheathCol, 1.0, (double)si / sSides, 0.5, 0, 0);
                    m.AddVertex(pTop, nrm, isTooth ? sheathTeethCol : sheathCol, 1.0, (double)si / sSides, 1.0, 0, 0);
                }
                for (int si = 0; si < sSides; si++)
                {
                    int a = sStart + si * 3;
                    int b = a + 3;
                    Primitives.TriangleFacing(m, a, b, a + 1, Vec3.Up);
                    Primitives.TriangleFacing(m, b, b + 1, a + 1, Vec3.Up);
                    Primitives.TriangleFacing(m, a + 1, b + 1, a + 2, Vec3.Up);
                    Primitives.TriangleFacing(m, b + 1, b + 2, a + 2, Vec3.Up);
                }

                // Drooping branchlet whorls from sheath base (tiers 2 through 6)
                if (j >= 2 && j <= 6)
                {
                    int whorlCount = 8 + rng.NextInt(5);
                    double tierFraction = (double)(j - 2) / 4.0;
                    double reach = rng.Range(0.24, 0.36) * (1.1 - tierFraction * 0.45);

                    for (int b = 0; b < whorlCount; b++)
                    {
                        double bAng = b * (2 * Math.PI / whorlCount) + angle + j * 0.42 + rng.Range(-0.08, 0.08);
                        var radial = new Vec3(Math.Cos(bAng), 0, Math.Sin(bAng));

                        // 3-point drooping branchlet: horizontal start, graceful arch, downward sweeping tip
                        var p0 = collar - Vec3.Up * (sheathH * 0.4) + radial * (r * 1.06);
                        var p1 = p0 + radial * (reach * 0.48) + Vec3.Up * 0.018;
                        var p2 = p0 + radial * (reach * 0.85) - Vec3.Up * (reach * 0.22);
                        var p3 = p0 + radial * reach - Vec3.Up * (reach * 0.52);

                        Primitives.Tube(m, new[] { p0, p1, p2, p3 }, new[] { 0.0055, 0.0042, 0.0028, 0.0014 }, 4,
                            (idx, v) => (branchCol, 1.0, idx / 3.0, v, 0, 0));
                    }
                }
            }

            // 3. Fertile terminal strobilus (spore cone) atop mature culms
            if (isFertile)
            {
                var apex = joints[nodeCount];
                double coneH = rng.Range(0.09, 0.13);
                double coneR = 0.024 * (1.0 + rng.Range(-0.15, 0.15));

                int coneStart = m.VertexCount;
                const int cRings = 7, cSides = 10;
                for (int cr = 0; cr <= cRings; cr++)
                {
                    double ct = (double)cr / cRings;
                    double y = coneH * ct;
                    // Oval-cylindrical envelope with rounded apex
                    double envelope = Math.Sin(Math.PI * (0.15 + 0.78 * ct));
                    double rad = coneR * Math.Max(0.15, envelope);
                    var ringCol = (cr % 2 == 0) ? strobilusCol : strobilusScaleCol;

                    for (int cs = 0; cs <= cSides; cs++)
                    {
                        double th = 2 * Math.PI * cs / cSides;
                        // Hexagonal scale faceting
                        double scaleFacet = 0.003 * Math.Cos(cs * 5.0 + cr * 3.0);
                        var nrm = new Vec3(Math.Cos(th), 0.2 * (ct - 0.5), Math.Sin(th)).Normalized();
                        var p = apex + new Vec3(Math.Cos(th) * (rad + scaleFacet), y, Math.Sin(th) * (rad + scaleFacet));
                        m.AddVertex(p, nrm, ringCol, 1.0, (double)cs / cSides, ct, 0, 0);
                    }
                }
                for (int cr = 0; cr < cRings; cr++)
                {
                    for (int cs = 0; cs < cSides; cs++)
                    {
                        int a = coneStart + cr * (cSides + 1) + cs;
                        int c = a + cSides + 1;
                        Primitives.TriangleFacing(m, a, c, a + 1, Vec3.Up);
                        Primitives.TriangleFacing(m, a + 1, c, c + 1, Vec3.Up);
                    }
                }
            }
        }
    }

    /// <summary>Brooklace: Trailing aquatic runner (Callitriche / Veronica beccabunga) with rooting nodes,
    /// opposite glossy narrow leaves, and floating apical star-rosettes.</summary>
    private static void Brooklace(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var stemCol = Primitives.Mix(c1, c2, 0.42);
        var leafBase = c1;
        var leafTip = Primitives.Mix(c1, c2, 0.82);
        var rootCol = new[] { 0.88, 0.90, 0.82 };
        var petalCol = new[] { 0.65, 0.74, 0.92 };
        var eyeCol = new[] { 0.96, 0.88, 0.32 };

        void BrooklaceFlower(Vec3 centre, Vec3 normal, double radius, double[] pCol, double[] eCol)
        {
            var side = normal.Cross(Math.Abs(normal.Y) > 0.9 ? new Vec3(1, 0, 0) : Vec3.Up).Normalized();
            var up = side.Cross(normal).Normalized();
            var rim = new List<Vec3>();
            const int petals = 4;
            const int steps = 16;
            for (int i = 0; i <= steps; i++)
            {
                double th = 2 * Math.PI * i / steps;
                double petalWave = 0.60 + 0.40 * Math.Cos(th * petals);
                double r = radius * petalWave;
                rim.Add(centre + side * (Math.Cos(th) * r) + up * (Math.Sin(th) * r));
            }
            Primitives.Fan(m, centre, rim, normal, eCol, pCol);
        }

        int runnerCount = 5 + rng.NextInt(3);
        for (int k = 0; k < runnerCount; k++)
        {
            double baseAng = (double)k / runnerCount * (2 * Math.PI) + rng.Range(-0.25, 0.25);
            double totalReach = rng.Range(0.68, 0.95);
            const int segs = 5;

            var path = new List<Vec3>();
            var radii = new List<double>();

            double curAng = baseAng;
            var curPos = new Vec3(0, 0.02, 0);
            path.Add(curPos);
            radii.Add(0.013);

            for (int s = 1; s <= segs; s++)
            {
                double st = (double)s / segs;
                curAng += rng.Range(-0.22, 0.22);
                double stepLen = (totalReach / segs) * rng.Range(0.9, 1.1);
                // Stems creep horizontally, rising toward water surface near apex
                double y = 0.02 + 0.08 * (st * st);
                curPos = new Vec3(curPos.X + Math.Cos(curAng) * stepLen, y, curPos.Z + Math.Sin(curAng) * stepLen);
                path.Add(curPos);
                radii.Add(0.013 * (1.0 - 0.55 * st));
            }

            Primitives.Tube(m, path, radii, 6, (i, v) => (stemCol, 1.0, i / (double)segs, v, 0, 0));

            // Nodes along runner: adventitious roots & opposite leaf pairs
            for (int j = 1; j < segs; j++)
            {
                var nodePos = path[j];
                var fwd = (path[j + 1] - path[j - 1]).Normalized();
                var side = fwd.Cross(Vec3.Up).Normalized();

                // Adventitious rootlets anchoring into substrate
                for (int r = 0; r < 2; r++)
                {
                    double rSign = (r == 0) ? -1.0 : 1.0;
                    var rTip = nodePos - Vec3.Up * rng.Range(0.035, 0.065) + side * (rSign * rng.Range(0.01, 0.025));
                    var rMid = (nodePos + rTip) * 0.5 + new Vec3(rng.Range(-0.008, 0.008), 0, rng.Range(-0.008, 0.008));
                    Primitives.Tube(m, new[] { nodePos, rMid, rTip }, new[] { 0.0032, 0.0022, 0.0010 }, 4,
                        (idx, v) => (rootCol, 0.85, idx / 2.0, v, 0, 0));
                }

                // Opposite pair of glossy narrow-elliptic leaves
                for (int s = -1; s <= 1; s += 2)
                {
                    var lDir = (side * s * 0.90 + fwd * 0.22 + Vec3.Up * rng.Range(0.06, 0.16)).Normalized();
                    double lLen = rng.Range(0.14, 0.20);
                    var lTip = nodePos + lDir * lLen;
                    var lSide = lDir.Cross(Vec3.Up);
                    Primitives.CurvedLeaf(m, nodePos, lTip, lSide, lLen * 0.30, leafBase, leafTip,
                        camber: lLen * 0.08, longitudinal: 4, asymmetry: rng.Range(-0.05, 0.05));
                }

                // Tiny axillary star blossoms in upper nodes
                if (j >= 2 && rng.NextDouble() < 0.45)
                {
                    var flPos = nodePos + Vec3.Up * 0.022 + side * rng.Range(-0.015, 0.015);
                    BrooklaceFlower(flPos, Vec3.Up, 0.024, petalCol, eyeCol);
                }
            }

            // Apical floating star-rosette at runner tip
            var tipPos = path[^1];
            var tipFwd = (path[^1] - path[^2]).Normalized();
            int rosetteCount = 8 + rng.NextInt(3);
            for (int r = 0; r < rosetteCount; r++)
            {
                double rAng = r * (2 * Math.PI / rosetteCount) + rng.Range(-0.12, 0.12);
                var rDir = (new Vec3(Math.Cos(rAng), 0, Math.Sin(rAng)) * 0.85 + tipFwd * 0.35 + Vec3.Up * 0.05).Normalized();
                double rLen = rng.Range(0.11, 0.17) * (0.85 + 0.25 * Math.Cos(rAng));
                var rTip = tipPos + rDir * rLen;
                var rSide = rDir.Cross(Vec3.Up);
                Primitives.CurvedLeaf(m, tipPos, rTip, rSide, rLen * 0.36, leafBase, leafTip,
                    camber: rLen * 0.06, longitudinal: 4);
            }
        }
    }

    /// <summary>Fenbead: Lush clump of decumbent-to-ascending fleshy succulent stems with opposite
    /// bead-like succulent leaves and terminal starry pearl blossoms.</summary>
    private static void Fenbead(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var stemCol = Primitives.Mix(c1, c2, 0.22);
        var beadBase = c1;
        var beadTip = Primitives.Mix(c1, c2, 0.72);
        var flowerCol = c2;
        var flowerEye = new[] { 0.96, 0.88, 0.30 };

        void SucculentBead(Vec3 attach, Vec3 dir, Vec3 up, double len, double wid)
        {
            var fwd = dir.Normalized();
            var side = fwd.Cross(up).Normalized();
            var top = side.Cross(fwd).Normalized();

            int bStart = m.VertexCount;
            const int rings = 3, around = 6;
            for (int ri = 0; ri <= rings; ri++)
            {
                double t = (double)ri / rings;
                double dist = len * t;
                double rad = wid * Math.Sin(Math.PI * t * 0.85) * (1.0 + 0.35 * Math.Sin(Math.PI * t));
                var col = Primitives.Mix(beadBase, beadTip, Math.Pow(t, 1.2));

                for (int s = 0; s <= around; s++)
                {
                    double th = 2 * Math.PI * s / around;
                    var offset = side * (Math.Cos(th) * rad) + top * (Math.Sin(th) * rad * 0.85);
                    var pos = attach + fwd * dist + offset;
                    var nrm = (offset.Normalized() * 0.85 + fwd * 0.3).Normalized();
                    m.AddVertex(pos, nrm, col, 1.0, (double)s / around, t, 0, 1);
                }
            }
            for (int ri = 0; ri < rings; ri++)
            {
                for (int s = 0; s < around; s++)
                {
                    int a = bStart + ri * (around + 1) + s;
                    int c = a + around + 1;
                    Primitives.TriangleFacing(m, a, c, a + 1, top);
                    Primitives.TriangleFacing(m, a + 1, c, c + 1, top);
                }
            }
        }

        void FenbeadFlower(Vec3 centre, Vec3 normal, double radius, double[] pCol, double[] eCol)
        {
            var side = normal.Cross(Math.Abs(normal.Y) > 0.9 ? new Vec3(1, 0, 0) : Vec3.Up).Normalized();
            var up = side.Cross(normal).Normalized();
            var rim = new List<Vec3>();
            const int petals = 5;
            const int steps = 20;
            for (int i = 0; i <= steps; i++)
            {
                double th = 2 * Math.PI * i / steps;
                double petalWave = 0.55 + 0.45 * Math.Cos(th * petals);
                double r = radius * petalWave;
                rim.Add(centre + side * (Math.Cos(th) * r) + up * (Math.Sin(th) * r));
            }
            Primitives.Fan(m, centre, rim, normal, eCol, pCol);
        }

        int stemCount = 12 + rng.NextInt(4);
        for (int k = 0; k < stemCount; k++)
        {
            double sAng = k * (2 * Math.PI / stemCount) + rng.Range(-0.22, 0.22);
            var radial = new Vec3(Math.Cos(sAng), 0, Math.Sin(sAng));
            double reach = rng.Range(0.48, 0.85);
            double height = rng.Range(0.42, 0.78);

            var p0 = radial * rng.Range(0.01, 0.04);
            var p1 = radial * (reach * 0.50) + Vec3.Up * (height * 0.38);
            var p2 = radial * reach + Vec3.Up * height;

            const int sSegs = 5;
            var sPts = new Vec3[sSegs + 1];
            var sRads = new double[sSegs + 1];
            for (int si = 0; si <= sSegs; si++)
            {
                double t = (double)si / sSegs;
                sPts[si] = Curve(p0, p1, p2, t);
                sRads[si] = 0.016 * (1.0 - 0.48 * t);
            }
            Primitives.Tube(m, sPts, sRads, 6, (i, v) => (stemCol, 1.0, i / (double)sSegs, v, 0, 0));

            // Opposite decussate succulent leaf beads along each stem
            for (int si = 1; si <= sSegs; si++)
            {
                var attach = sPts[si];
                var fwd = (si == sSegs ? sPts[si] - sPts[si - 1] : sPts[si + 1] - sPts[si - 1]).Normalized();
                var refUp = Vec3.Up;
                var side0 = fwd.Cross(refUp).Normalized();
                var up0 = side0.Cross(fwd).Normalized();

                // Rotate pair by 90 degrees each station (decussate)
                double pairRot = (si % 2 == 0) ? 0.0 : Math.PI * 0.5;
                var pairAxis = side0 * Math.Cos(pairRot) + up0 * Math.Sin(pairRot);

                double bLen = rng.Range(0.065, 0.095);
                double bWid = bLen * 0.38;

                SucculentBead(attach, pairAxis, fwd, bLen, bWid);
                SucculentBead(attach, -pairAxis, fwd, bLen, bWid);
            }

            // Starry pearl blossom at tip of mature upright stems
            if (height > 0.55 && rng.NextDouble() < 0.65)
            {
                var tipPos = sPts[^1];
                var tipDir = (sPts[^1] - sPts[^2]).Normalized();
                FenbeadFlower(tipPos + tipDir * 0.015, tipDir, 0.040, flowerCol, flowerEye);
            }
        }
    }
    private static void RoundLeaf(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        int nHero = 3 + rng.NextInt(4);   // 3..6 larger kernel-built leaves for close-up detail
        int nMat = 16 + rng.NextInt(16);  // 16..31 cheap cupped leaflets filling out the mat, like the old coverage

        for (int k = 0; k < nHero; k++)
        {
            double ageT = nHero <= 1 ? 1.0 : (double)k / (nHero - 1);
            double ang = rng.Range(0, 2 * Math.PI);
            double clumpR = rng.Range(0, 0.5) * 0.9;
            var basePos = new Vec3(Math.Cos(ang) * clumpR, MathD.Lerp(-0.01, 0.03, 1 - ageT), Math.Sin(ang) * clumpR);

            double sizeScale = MathD.Lerp(0.2, 0.12, ageT) * rng.Range(0.85, 1.15);
            double petioleLen = MathD.Lerp(0.5, 0.22, ageT) * rng.Range(0.85, 1.15);
            bool folded = ageT > 0.72;

            var petioleAxis = new AxisParams(Length: petioleLen, BaseAngle: rng.Range(0.35, 0.75), BaseAzimuth: 0,
                Droop: rng.Range(0.9, 1.5), WobbleAmplitude: 0.01, WobbleFrequency: 1.3, Segments: 5);
            ulong pSeed = Rng.Mix(seed, (ulong)(k * 131 + 7));
            var petioleFrames = Axis.Build(petioleAxis, pSeed);
            var tipFrame = petioleFrames[^1];

            var tmp = new MeshData();
            SoftTube.Build(tmp, new SoftTubeParams(petioleAxis, BaseRadius: 0.007 * (sizeScale / 0.13), TipRadius: 0.004 * (sizeScale / 0.13), Segments: 4),
                pSeed, (i, v) => (Primitives.Scale(c1, 0.72), 1, i, v, 0, 0));

            double tipPitch = Math.Acos(MathD.Clamp(tipFrame.Tangent.Y, -1.0, 1.0));
            double tipAz = Math.Atan2(tipFrame.Tangent.Z, tipFrame.Tangent.X);
            var profile = rng.NextDouble() < 0.5 ? BladeProfile.Reniform : BladeProfile.Cordate;
            var bladeAxis = new AxisParams(Length: sizeScale * 0.5, BaseAngle: tipPitch, BaseAzimuth: tipAz,
                Droop: rng.Range(-0.2, 0.2), Segments: 5);
            var bladeParams = new LeafBladeParams(
                Midrib: bladeAxis, Profile: profile, HalfWidth: sizeScale,
                Camber: rng.Range(0.14, 0.22) + (folded ? 0.16 : 0),
                Cup: rng.Range(0.06, 0.14), MidribFold: folded ? 0.10 : 0.02,
                Asymmetry: rng.Range(-0.08, 0.08), MidribThickness: 0.002, DetailLevel: 1);
            var bladeTmp = new MeshData();
            LeafBlade.Build(bladeTmp, bladeParams, Rng.Mix(seed, (ulong)(k * 977 + 3)),
                Primitives.Scale(c1, 0.85), Primitives.Mix(c1, c2, rng.Range(0.1, 0.4)));
            AppendTranslated(tmp, bladeTmp, tipFrame.Point);

            double leafYaw = ang + rng.Range(-0.35, 0.35); // no two leaves share orientation
            AppendRotatedY(m, tmp, basePos, leafYaw);
        }

        for (int k = 0; k < nMat; k++)
        {
            double ang = rng.Range(0, 2 * Math.PI), r = Math.Sqrt(rng.NextDouble()) * 0.88;
            var basePos = new Vec3(Math.Cos(ang) * r, 0, Math.Sin(ang) * r);
            double stalkH = rng.Range(0.05, 0.14);
            double leafR = rng.Range(0.05, 0.11);
            bool folded = rng.NextDouble() < 0.18;
            var profile = rng.NextDouble() < 0.5 ? BladeProfile.Reniform : BladeProfile.Cordate;
            double leafYaw = ang + rng.Range(-0.5, 0.5);
            ulong lSeed = Rng.Mix(seed, (ulong)(k * 733 + 91));

            var tmp = new MeshData();
            var top = new Vec3(0, stalkH, 0);
            Primitives.Tube(tmp, new[] { Vec3.Zero, top }, new[] { 0.008 * (leafR / 0.08), 0.005 * (leafR / 0.08) }, 4,
                (i, v) => (Primitives.Scale(c1, 0.72), 1, i, v, 0, 0));
            CuppedLeaflet(tmp, top, leafR, profile, folded, lSeed, Primitives.Mix(c1, c2, rng.Range(0.1, 0.45)), c2);
            AppendRotatedY(m, tmp, basePos, leafYaw);
        }
    }

    /// <summary>Cheap double-sided cupped/notched leaflet (a raised-centre fan): the mat-filler counterpart to the
    /// full LeafBlade kernel, used where many instances are needed within the triangle budget.</summary>
    private static void CuppedLeaflet(MeshData m, Vec3 top, double leafR, BladeProfile profile, bool folded, ulong seed,
        double[] rimCol, double[] tipCol)
    {
        var rng = Rng.Keyed(seed, "flora.mesh.leaflet", 0);
        double notchAng = rng.Range(0, 2 * Math.PI);
        double notchDepth = profile == BladeProfile.Reniform ? 0.4 : 0.3;
        double cup = leafR * rng.Range(0.22, 0.4);
        double foldAng = rng.Range(0, 2 * Math.PI);

        var rim = new List<Vec3>();
        for (int s = 0; s <= 10; s++)
        {
            double th = 2 * Math.PI * s / 10;
            double rr = leafR * (1 - notchDepth * Math.Max(0, Math.Cos(th - notchAng)));
            double lift = -0.08 * leafR; // rim curls down slightly relative to the cupped centre
            if (folded)
            {
                double fold = Math.Max(0, Math.Cos(th - foldAng));
                lift += fold * fold * leafR * 0.9; // one half folded up against the other
                rr *= 1 - 0.25 * fold;
            }
            rim.Add(top + new Vec3(Math.Cos(th) * rr, lift, Math.Sin(th) * rr));
        }
        var apex = top + new Vec3(0, cup, 0);
        Primitives.Fan(m, apex, rim, Vec3.Up, tipCol, rimCol);
    }

    /// <summary>Cheap double-sided cupped ovate leaflet oriented by an explicit (fwd, side, up) basis — the
    /// pairedleaf counterpart to <see cref="CuppedLeaflet"/>, elongated along <paramref name="fwd"/> and cambered
    /// toward <paramref name="up"/> to read as a thick fleshy leaf rather than a flat card.</summary>
    private static void ThickOvateLeaflet(MeshData m, Vec3 center, Vec3 fwd, Vec3 side, Vec3 up,
        double length, double halfWidth, double cupAmt, ulong seed, double[] baseCol, double[] tipCol, int segs = 8)
    {
        var rng = Rng.Keyed(seed, "flora.mesh.ovateleaflet", 0);
        double asym = rng.Range(-0.15, 0.15);
        var rim = new List<Vec3>();
        for (int s = 0; s <= segs; s++)
        {
            double th = 2 * Math.PI * s / segs;
            double envelope = Math.Pow(Math.Abs(Math.Sin(th)), 0.7);
            double along = Math.Cos(th) * length * 0.5;
            double across = Math.Sin(th) * halfWidth * envelope * (1 + asym * Math.Sign(Math.Sin(th)));
            double lift = -0.06 * halfWidth;
            rim.Add(center + fwd * along + side * across + up * lift);
        }
        var apex = center + up * cupAmt;
        Primitives.Fan(m, apex, rim, up, tipCol, baseCol);
    }

    /// <summary>Decumbent curved stems with opposite pairs of thick ovate leaves, decussate and tapering to the
    /// tip (bacopa). Occasional tiny white flower near the growing tip.</summary>
    private static void PairedLeaf(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        int stems = 6 + rng.NextInt(7); // 6..12 stems, a visible sprawl rather than a few sprigs
        for (int k = 0; k < stems; k++)
        {
            double ang = rng.Range(0, 2 * Math.PI), r = Math.Sqrt(rng.NextDouble()) * 0.75;
            var basePos = new Vec3(Math.Cos(ang) * r, 0, Math.Sin(ang) * r);
            double stemLen = rng.Range(0.4, 0.75);
            int pairs = 4 + rng.NextInt(4); // 4..7 pairs, tapering toward tip

            var stemAxis = new AxisParams(Length: stemLen, BaseAngle: rng.Range(1.15, 1.45), BaseAzimuth: 0,
                Droop: rng.Range(-0.15, 0.35), PhototropicBend: rng.Range(0.1, 0.3),
                WobbleAmplitude: 0.015, WobbleFrequency: 1.6, Segments: 8);
            ulong sSeed = Rng.Mix(seed, (ulong)(k * 211 + 11));
            var stemFrames = Axis.Build(stemAxis, sSeed);

            var tmp = new MeshData();
            SoftTube.Build(tmp, new SoftTubeParams(stemAxis, BaseRadius: 0.012, TipRadius: 0.006, Segments: 6), sSeed,
                (i, v) => (Primitives.Scale(c1, 0.78), 1, i, v, 0, 0));

            for (int p = 1; p <= pairs; p++)
            {
                double t = (double)p / (pairs + 1);
                var f = Axis.Sample(stemFrames, t);
                double taper = 1.0 - 0.5 * (double)p / pairs;
                double leafSize = rng.Range(0.12, 0.18) * taper;
                double pairYaw = (p % 2 == 0) ? Math.PI / 2 : 0.0; // decussate: successive pairs rotated 90 degrees

                var rotSide = Axis.RotateAround(f.Side, f.Tangent, pairYaw);
                foreach (double sgn in new[] { -1.0, 1.0 })
                {
                    var fwd = rotSide * sgn;
                    var up = fwd.Cross(f.Tangent).Normalized();
                    if (up.LengthSq < 1e-8) up = Vec3.Up;
                    var center = f.Point + fwd * (leafSize * 0.65);
                    ulong lSeed = Rng.Mix(seed, (ulong)(k * 4111 + p * 17 + (sgn > 0 ? 1u : 2u)));
                    int bladeStart = tmp.VertexCount;
                    double bladeLength = leafSize * 1.3;
                    ThickOvateLeaflet(tmp, center, fwd, f.Tangent, up, bladeLength, leafSize * 0.55,
                        leafSize * rng.Range(0.12, 0.22), lSeed,
                        Primitives.Scale(c1, 0.82), Primitives.Mix(c1, c2, rng.Range(0.15, 0.4)));
                    MarkOvateBladeVertices(tmp, bladeStart, center, fwd, bladeLength);
                }
            }
            if (rng.NextDouble() < 0.3)
            {
                var tipF = stemFrames[^1];
                var star = new List<Vec3>();
                for (int s = 0; s <= 8; s++) { double th = 2 * Math.PI * s / 8, rr = s % 2 == 0 ? 0.045 : 0.018; star.Add(tipF.Point + new Vec3(Math.Cos(th) * rr, 0.01, Math.Sin(th) * rr)); }
                Primitives.Fan(tmp, tipF.Point + new Vec3(0, 0.015, 0), star, Vec3.Up, new[] { 1.0, 1.0, 1.0 }, c2);
            }
            double leafYawWhole = rng.Range(0, 2 * Math.PI);
            AppendRotatedY(m, tmp, basePos, leafYawWhole);
        }
    }

    /// <summary>Rooted lily-pad plant: flat, notched, orbicular leaf pads lying at the water surface, each fed by
    /// a petiole that bends up from a small central rhizome and stays UNDER the pad (submerged, never visible
    /// above water). Unlike every other flora shape, this one is NOT built at unit XZ radius 1: the renderer
    /// fixes this species' horizontal scale to 1 (see FloraRenderer.Rebuild) so pad size is authored here
    /// directly in metres and stays constant regardless of plant growth radius or local water depth. Only
    /// vertical position is unit-normalised (surface at Y = 1) so the renderer's depth-driven Y scale lands
    /// everything at the true water height. Includes 1-2 young rolled scroll leaves that DO stand a little
    /// above the water, older pads with torn/yellowed margins, and 0-2 staged cup flowers held above the surface.</summary>
    private static void FloatLeaf(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var yellow = new[] { 0.62, 0.56, 0.22 };
        var youngCol = new[] { 0.62, 0.58, 0.2 };
        var underCol = Primitives.Scale(c1, 0.55); // paler, matte underside

        // Small rhizome crown the petioles emerge from (metres, on the pond floor).
        {
            var rim = new List<Vec3>();
            for (int s = 0; s <= 6; s++) { double th = 2 * Math.PI * s / 6; rim.Add(new Vec3(Math.Cos(th) * 0.02, 0, Math.Sin(th) * 0.02)); }
            Primitives.Fan(m, new Vec3(0, 0.006, 0), rim, Vec3.Up, Primitives.Scale(c1, 0.55), Primitives.Scale(c1, 0.4));
        }

        int nLeaves = 4 + rng.NextInt(9); // 4..12
        int nYoung = 1 + rng.NextInt(2);  // 1..2 rolled scroll leaves
        int nOld = rng.NextInt(3);        // 0..2 older torn/yellowed pads

        for (int k = 0; k < nLeaves; k++)
        {
            double ang = rng.Range(0, 2 * Math.PI);
            double reach = rng.Range(0.03, 0.11); // pad/scroll centre offset from the rhizome (metres)
            bool isYoung = k < nYoung;
            bool isOld = !isYoung && k >= nLeaves - nOld;
            ulong lSeed = Rng.Mix(seed, (ulong)(k * 401 + 7));

            if (isYoung)
            {
                // Young rolled scroll: the one leaf stage allowed above water. Its petiole rises past unit
                // Y = 1 (the surface) carrying a tight, not-yet-unfurled coil.
                double riseY = 1.0 + rng.Range(0.05, 0.14);
                var dir = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
                var path = new List<Vec3> { Vec3.Zero, dir * (reach * 0.4) + new Vec3(0, 0.55, 0),
                    dir * (reach * 0.85) + new Vec3(0, 0.92, 0), dir * reach + new Vec3(0, riseY, 0) };
                var rad = new List<double> { 0.003, 0.0026, 0.002, 0.0016 };
                Primitives.Tube(m, path, rad, 5, (i, v) => (Primitives.Scale(c1, 0.65), 1, i, v, 0, 0));
                var tipP = path[^1];
                var side = new Vec3(-Math.Sin(ang), 0, Math.Cos(ang));
                var coil = new List<Vec3>(); var crad = new List<double>();
                for (int i = 0; i <= 10; i++)
                {
                    double t = i / 10.0, th = t * Math.PI * 2.6, rr = 0.018 * (1 - t * 0.6);
                    var p = tipP + new Vec3(0, 0.008 + 0.05 * t, 0) + side * (Math.Cos(th) * rr) + new Vec3(0, Math.Sin(th) * rr * 0.4, 0);
                    coil.Add(p); crad.Add(0.0022 * (1 - t * 0.5));
                }
                int coilCount = coil.Count;
                Primitives.Tube(m, coil, crad, 4, (i, v) => (Primitives.Mix(c1, youngCol, i / (double)(coilCount - 1)), 1, i, v, 0, 0));
                continue;
            }

            // Mature/old pads lie flat at the surface (unit Y = 1); the petiole bends up from the rhizome to
            // meet the underside of the pad from below and stops a hair short of it, so no stalk shows above
            // the water for these leaves — only the pad itself does.
            double padY = 1.0;
            var padCentre = new Vec3(Math.Cos(ang) * reach, padY, Math.Sin(ang) * reach);
            const double gap = 0.012;
            var petPath = new List<Vec3> { Vec3.Zero,
                (padCentre * 0.35) with { Y = padY * 0.45 },
                (padCentre * 0.75) with { Y = padY - gap * 3 },
                padCentre with { Y = padY - gap } };
            var petRad = new List<double> { 0.0035, 0.003, 0.0024, 0.0016 };
            Primitives.Tube(m, petPath, petRad, 5, (i, v) => (Primitives.Scale(c1, 0.6), 1, i, v, 0, 0));

            double radius = rng.Range(0.015, 0.04) * (isOld ? rng.Range(0.85, 1.05) : 1.0); // 3-8 cm diameter
            var topCentreCol = isOld ? Primitives.Mix(c1, yellow, rng.Range(0.4, 0.75)) : Primitives.Scale(c1, 0.95);
            var topRimCol = isOld ? Primitives.Mix(c2, yellow, rng.Range(0.5, 0.85)) : Primitives.Mix(c1, c2, rng.Range(0.1, 0.3));
            var bottomCol = isOld ? Primitives.Mix(underCol, yellow, 0.3) : underCol;
            var tmp = new MeshData();
            OrbicularPad(tmp, lSeed, radius, thickness: 0.02, topCentreCol, topRimCol, bottomCol,
                torn: isOld, tearAmp: isOld ? rng.Range(0.06, 0.12) : 0);
            AppendRotatedY(m, tmp, padCentre, ang);
        }

        int nFlowers = rng.NextInt(3); // 0..2
        for (int f = 0; f < nFlowers; f++)
        {
            double ang = rng.Range(0, 2 * Math.PI), petAngle = rng.Range(0.04, 0.14);
            double petLen = 1.02 / Math.Cos(petAngle);
            var stalkAxis = new AxisParams(Length: petLen, BaseAngle: petAngle, BaseAzimuth: ang,
                Droop: rng.Range(-0.02, 0.03), WobbleAmplitude: 0.006, Segments: 5);
            ulong sSeed = Rng.Mix(seed, (ulong)(f * 613 + 71));
            SoftTube.Build(m, new SoftTubeParams(stalkAxis, BaseRadius: 0.004, TipRadius: 0.002, Segments: 5), sSeed,
                (i, v) => (Primitives.Scale(c1, 0.6), 1, i, v, 0, 0));
            var top = Axis.Build(stalkAxis, sSeed)[^1].Point;
            double stageT = (double)((seed + (ulong)f * 3) % 5) / 4.0; // guarantees bud/open/spent variety
            FlowerHead(m, rng, Rng.Mix(seed, (ulong)(f * 97 + 5)), top, 0.05, stageT,
                c1, c2, new[] { 0.95, 0.9, 0.55 }, petalCountMax: 6);
        }
    }

    /// <summary>Flat, roughly circular double-sided pad in the local XZ plane (Y ~ 0, width ~= length) with a
    /// V-shaped basal sinus notch cut from the rim all the way to the centre — the peltate attachment point
    /// where a petiole meets it from below — and a gently upturned rim. Top and bottom faces carry distinct
    /// colour and UV2.x (0 top / 1 bottom, matching the LeafBlade convention) so a shader can tell the leaf's
    /// topside from its pale underside; both faces' normals are exactly (0, ±1, 0). The notch/attachment point
    /// faces local -X; callers rotate the whole pad about Y to aim the notch back toward the plant's rhizome and
    /// translate it into place.</summary>
    private static void OrbicularPad(MeshData m, ulong seed, double radius, double thickness,
        double[] topCentreCol, double[] topRimCol, double[] bottomCol, bool torn, double tearAmp)
    {
        var rng = Rng.Keyed(seed, "flora.mesh.orbicularpad", 0);
        const int seg = 30;
        double notchHalf = rng.Range(0.19, 0.31);
        double notchShift = rng.Range(-0.065, 0.065);
        double start = Math.PI + notchShift + notchHalf;
        double sweep = 2 * Math.PI - 2 * notchHalf;
        double phase = rng.Range(0, 2 * Math.PI);
        double phase2 = rng.Range(0, 2 * Math.PI);
        double ellipse = rng.Range(-0.14, 0.14);
        double broadWave = rng.Range(0.055, 0.105);
        double fineWave = rng.Range(0.018, 0.045);
        double upturn = radius * rng.Range(0.14, 0.25);
        double tearAngle = start + sweep * rng.Range(0.18, 0.83);

        // Each leaf has a distinct broad outline plus small, smooth margin movement.
        // Damage on old pads removes one localized bite rather than making a
        // regular zigzag all the way around the leaf.
        Vec3 Rim(double th, double faceY)
        {
            double wave = broadWave * Math.Sin(3 * th + phase)
                + fineWave * Math.Sin(7 * th + phase2)
                + 0.024 * Noise.Gradient(seed, Math.Cos(th) * 2.4 + 3, Math.Sin(th) * 2.4 + 5);
            double bite = torn ? tearAmp * Math.Pow(Math.Max(0, Math.Cos(th - tearAngle)), 18) : 0;
            double rr = radius * (1 + wave - bite);
            return new Vec3(Math.Cos(th) * rr * (1 + ellipse),
                faceY + upturn * (0.88 + 0.12 * Math.Sin(2 * th + phase)),
                Math.Sin(th) * rr * (1 - ellipse));
        }

        for (int face = 0; face < 2; face++)
        {
            double sign = face == 0 ? 1.0 : -1.0;
            var n = new Vec3(0, sign, 0);
            double faceY = sign * thickness * 0.5;
            var centreCol = face == 0 ? topCentreCol : bottomCol;
            var rimCol = face == 0 ? topRimCol : bottomCol;
            int c = m.AddVertex(new Vec3(0, faceY, 0), n, centreCol, 1, 0.5, 0.5, face, 1);
            var ids = new List<int>();
            for (int s = 0; s <= seg; s++)
            {
                double th = start + sweep * s / seg;
                ids.Add(m.AddVertex(Rim(th, faceY), n, rimCol, 1, (double)s / seg, 1, face, 1));
            }
            for (int i = 0; i < ids.Count - 1; i++)
                Primitives.TriangleFacing(m, c, ids[i], ids[i + 1], n);
        }

        // Fine veins radiate from the petiole attachment and stop before the
        // uneven rim. They follow the same height profile as the leaf surface.
        var veinCol = Primitives.Scale(Primitives.Mix(topCentreCol, topRimCol, 0.5), 1.14);
        for (int v = 0; v < 5; v++)
        {
            double th = start + sweep * (v + 0.5) / 5;
            var edge = Rim(th, thickness * 0.5);
            var centre = new Vec3(0, thickness * 0.5 + 0.0012, 0);
            var middle = Vec3.Lerp(centre, edge, 0.48) + new Vec3(0, 0.0012, 0);
            var end = Vec3.Lerp(centre, edge, 0.91) + new Vec3(0, 0.0012, 0);
            Primitives.Tube(m, new[] { centre, middle, end },
                new[] { 0.00055, 0.00042, 0.00012 }, 3,
                (i, u) => (veinCol, 1, i, u, 0, 0));
        }
    }
    /// <summary>Double-sided scalloped reniform/coin leaf blade with concave saucer dish, crenate margins,
    /// and basal cleft notch at the petiole attachment.</summary>
    private static void ScallopedCoinLeaf(MeshData m, Vec3 center, Vec3 normal, Vec3 fwd, double radius, double cup,
        int scallops, double sinusDepth, double[] colUpperCenter, double[] colUpperRim, double[] colUnder)
    {
        normal = normal.Normalized();
        var side = normal.Cross(fwd).Normalized();
        if (side.LengthSq < 1e-8)
        {
            side = normal.Cross(new Vec3(1, 0, 0)).Normalized();
            if (side.LengthSq < 1e-8) side = normal.Cross(new Vec3(0, 0, 1)).Normalized();
        }
        fwd = side.Cross(normal).Normalized();

        const int N = 14;
        var rimUpper = new int[N + 1];
        var rimLower = new int[N + 1];

        var apexUpper = center + normal * cup;
        int cUpper = m.AddVertex(apexUpper, normal, colUpperCenter, 1.0, 0.5, 0.5, 0.0, 1.0);

        var apexLower = center - normal * (cup * 0.4);
        int cLower = m.AddVertex(apexLower, -normal, colUnder, 1.0, 0.5, 0.5, 1.0, 1.0);

        for (int s = 0; s <= N; s++)
        {
            double th = 2 * Math.PI * s / N;
            double cosTh = Math.Cos(th);
            double sinTh = Math.Sin(th);
            var pDir = fwd * cosTh + side * sinTh;

            // Basal notch (sinus) where petiole attaches at th = Math.PI (rear)
            double sinus = Math.Max(0, -cosTh);
            double rSinus = 1.0 - sinusDepth * sinus * sinus;

            // Scalloped crenations around the margin
            double rScallop = 1.0 + 0.06 * Math.Cos(scallops * th);
            double r = radius * rSinus * rScallop;

            double rimLift = -cup * 0.35;
            var rimPos = center + pDir * r + normal * rimLift;

            var upperNormal = (normal * 0.85 + pDir * 0.35).Normalized();
            var lowerNormal = (-normal * 0.85 + pDir * 0.35).Normalized();

            double u = 0.5 + 0.5 * cosTh;
            double v = 0.5 + 0.5 * sinTh;

            rimUpper[s] = m.AddVertex(rimPos, upperNormal, colUpperRim, 1.0, u, v, 0.0, 1.0);
            rimLower[s] = m.AddVertex(rimPos, lowerNormal, colUnder, 1.0, u, v, 1.0, 1.0);
        }

        for (int s = 0; s < N; s++)
        {
            Primitives.TriangleFacing(m, cUpper, rimUpper[s], rimUpper[s + 1], normal);
            Primitives.TriangleFacing(m, cLower, rimLower[s + 1], rimLower[s], -normal);
        }
    }

    /// <summary>
    /// Pearl-cushion moss (Grimmia / Leucobryum habit):
    /// Multi-lobed rounded cushion mounds studded with glistening silvery-pearl hyaline hair points (awns)
    /// and wiry curved setae stalks carrying glossy amber spore capsules with beaked calyptra hoods.
    /// </summary>
    private static void PearlCushion(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var matBaseCol = Primitives.Scale(c1, 0.65);
        var matMidCol = c1;
        var matCrownCol = Primitives.Mix(c1, c2, 0.60);
        var pearlCol = new[] { 0.88, 0.94, 0.96 };
        var setaCol = new[] { 0.58, 0.30, 0.14 };
        var capsuleCol = new[] { 0.74, 0.48, 0.16 };
        var calyptraCol = new[] { 0.45, 0.22, 0.10 };

        // 1. Multi-lobed compound dome: 5 overlapping pillow lobes
        var lobeCenters = new (double x, double z, double r, double h)[]
        {
            (0.0, 0.0, 0.85, 0.68),
            (0.35, 0.20, 0.55, 0.58),
            (-0.30, 0.25, 0.52, 0.55),
            (0.15, -0.38, 0.50, 0.52),
            (-0.35, -0.22, 0.48, 0.50)
        };

        const int rings = 18;
        const int segs = 36;
        int startVert = m.VertexCount;
        ulong ns = Rng.Mix(seed, 0xA491);

        for (int r = 0; r <= rings; r++)
        {
            double f = (double)r / rings;
            double rad = f * 0.95;
            for (int s = 0; s <= segs; s++)
            {
                double th = 2 * Math.PI * s / segs;
                double cosTh = Math.Cos(th);
                double sinTh = Math.Sin(th);
                double px = cosTh * rad;
                double pz = sinTh * rad;

                // Evaluate height from overlapping lobes
                double h = 0.0;
                for (int l = 0; l < lobeCenters.Length; l++)
                {
                    var (lx, lz, lr, lh) = lobeCenters[l];
                    double dx = px - lx;
                    double dz = pz - lz;
                    double dist = Math.Sqrt(dx * dx + dz * dz);
                    if (dist < lr)
                    {
                        double dome = Math.Cos(dist / lr * Math.PI * 0.5);
                        h = Math.Max(h, lh * dome);
                    }
                }

                // Add granular organic moss-turf noise
                double turfNoise = 0.04 * Noise.Gradient(ns, px * 6.0, pz * 6.0);
                h += turfNoise * (1.0 - f * 0.5);

                // Margin tuck into ground
                if (r == rings) h = 0.005;
                else if (r == rings - 1) h = Math.Min(h, 0.04);

                var pos = new Vec3(px, Math.Max(0.005, h), pz);
                double tHeight = MathD.Clamp01(h / 0.65);
                var col = tHeight < 0.4
                    ? Primitives.Mix(matBaseCol, matMidCol, tHeight / 0.4)
                    : Primitives.Mix(matMidCol, matCrownCol, (tHeight - 0.4) / 0.6);

                m.AddVertex(pos, Vec3.Up, col, 1.0, f, (double)s / segs, 0.0, 0.0);
            }
        }

        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s < segs; s++)
            {
                int a = startVert + r * (segs + 1) + s;
                int b = a + segs + 1;
                Primitives.TriangleFacing(m, a, a + 1, b, Vec3.Up);
                Primitives.TriangleFacing(m, a + 1, b + 1, b, Vec3.Up);
            }
        }
        m.RecomputeNormals();

        // 2. Silvery hyaline awns (pearly hair points) bristling across the crown
        int awnCount = 130 + rng.NextInt(30);
        for (int i = 0; i < awnCount; i++)
        {
            double ang = rng.Range(0, 2 * Math.PI);
            double dist = Math.Sqrt(rng.NextDouble()) * 0.78;
            double px = Math.Cos(ang) * dist;
            double pz = Math.Sin(ang) * dist;

            double h = 0.0;
            for (int l = 0; l < lobeCenters.Length; l++)
            {
                var (lx, lz, lr, lh) = lobeCenters[l];
                double dx = px - lx;
                double dz = pz - lz;
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d < lr) h = Math.Max(h, lh * Math.Cos(d / lr * Math.PI * 0.5));
            }
            if (h < 0.20) continue;

            var baseP = new Vec3(px, h, pz);
            var outward = new Vec3(px * 0.8 + rng.Range(-0.15, 0.15), 0.6 + rng.Range(0.0, 0.4), pz * 0.8 + rng.Range(-0.15, 0.15)).Normalized();
            double awnLen = rng.Range(0.045, 0.08);
            var tipP = baseP + outward * awnLen;
            Primitives.Tube(m, new[] { baseP, baseP + outward * (awnLen * 0.5), tipP },
                new[] { 0.0032, 0.0020, 0.0006 }, 4, (step, u) => (pearlCol, 1.0, step / 2.0, u, 0, 0));
        }

        // 3. Setae stalks carrying glossy amber nodding spore capsules
        int sporophyteCount = 22 + rng.NextInt(8);
        for (int i = 0; i < sporophyteCount; i++)
        {
            double ang = rng.Range(0, 2 * Math.PI);
            double dist = Math.Sqrt(rng.NextDouble()) * 0.65;
            double px = Math.Cos(ang) * dist;
            double pz = Math.Sin(ang) * dist;

            double h = 0.0;
            for (int l = 0; l < lobeCenters.Length; l++)
            {
                var (lx, lz, lr, lh) = lobeCenters[l];
                double dx = px - lx;
                double dz = pz - lz;
                double d = Math.Sqrt(dx * dx + dz * dz);
                if (d < lr) h = Math.Max(h, lh * Math.Cos(d / lr * Math.PI * 0.5));
            }
            if (h < 0.25) continue;

            var baseP = new Vec3(px, h, pz);
            double setaH = rng.Range(0.30, 0.48);
            var archDir = new Vec3(rng.Range(-1.0, 1.0), 0, rng.Range(-1.0, 1.0)).Normalized();

            var p1 = baseP + Vec3.Up * (setaH * 0.50) + archDir * 0.03;
            var p2 = baseP + Vec3.Up * setaH + archDir * 0.07;
            var p3 = p2 + new Vec3(0, -0.045, 0) + archDir * 0.04;

            Primitives.Tube(m, new[] { baseP, p1, p2, p3 },
                new[] { 0.0040, 0.0032, 0.0026, 0.0022 }, 4, (step, u) => (setaCol, 1.0, step / 3.0, u, 0, 0));

            var capsuleAxis = (p3 - p2).Normalized();
            var capsuleCenter = p3 + capsuleAxis * 0.025;
            Primitives.Ellipsoid(m, capsuleCenter, new Vec3(0.018, 0.030, 0.018), 5, 8,
                (u, v) => (capsuleCol, 1.0, u, v, 0, 0));

            var calyptraTip = capsuleCenter + capsuleAxis * 0.038;
            Primitives.Tube(m, new[] { capsuleCenter + capsuleAxis * 0.018, calyptraTip },
                new[] { 0.012, 0.002 }, 4, (step, u) => (calyptraCol, 1.0, step, u, 0, 0));
        }
    }

    /// <summary>
    /// Floodlace moss (pleurocarpous aquatic / riparian feather moss):
    /// Sprawling prostrate runner stems branching pinnately with two-ranked feathery leaflet sprays,
    /// and lateral nodding capsules arising from the sides of creeping stems on wiry setae.
    /// </summary>
    private static void Floodlace(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var matCol = Primitives.Scale(c1, 0.55);
        var stemCol = new[] { 0.22, 0.36, 0.18 };
        var leafBase = c1;
        var leafTip = Primitives.Mix(c1, c2, 0.85);
        var setaCol = new[] { 0.48, 0.22, 0.14 };
        var capsuleCol = new[] { 0.40, 0.32, 0.16 };

        // 1. Thin undulating base mat hugging the floor
        int matStart = m.VertexCount;
        Primitives.Ellipsoid(m, new Vec3(0, 0.02, 0), new Vec3(0.96, 0.08, 0.96), 6, 28,
            (a, b) => (matCol, 1.0, a, b, 0, 0));
        ulong rim = Rng.Mix(seed, 0xC4A9);
        for (int i = matStart; i < m.VertexCount; i++)
        {
            var p = m.Position(i);
            double ang = Math.Atan2(p.Z, p.X);
            double wob = 1.0 + 0.24 * (Noise.Value3(rim, Math.Cos(ang) * 2.5, Math.Sin(ang) * 2.5, 0.5) - 0.3);
            m.Positions[i * 3] = (float)(p.X * wob);
            m.Positions[i * 3 + 2] = (float)(p.Z * wob);
            m.Positions[i * 3 + 1] = (float)Math.Max(0.002, p.Y * 0.4);
        }
        m.RecomputeNormals();

        // 2. Creeping runner stems (pleurocarpous prostrate axes)
        int runnerCount = 8 + rng.NextInt(3);
        var capsuleNodes = new List<(Vec3 pos, Vec3 side)>();

        for (int r = 0; r < runnerCount; r++)
        {
            double baseAng = (2 * Math.PI * r) / runnerCount + rng.Range(-0.18, 0.18);
            double runnerLen = rng.Range(0.68, 0.96);
            int segs = 10;
            var path = new List<Vec3>();
            var radii = new List<double>();

            var cur = new Vec3(rng.Range(-0.04, 0.04), 0.015, rng.Range(-0.04, 0.04));
            path.Add(cur);
            radii.Add(0.010);

            double curAng = baseAng;
            for (int s = 1; s <= segs; s++)
            {
                double stepLen = runnerLen / segs;
                curAng += rng.Range(-0.20, 0.20);
                double prog = (double)s / segs;
                cur += new Vec3(Math.Cos(curAng) * stepLen, rng.Range(-0.003, 0.005), Math.Sin(curAng) * stepLen);
                cur = new Vec3(cur.X, Math.Max(0.008, cur.Y), cur.Z);
                path.Add(cur);
                radii.Add(Math.Max(0.003, 0.010 * (1.0 - prog * 0.7)));

                var fwd = new Vec3(Math.Cos(curAng), 0, Math.Sin(curAng)).Normalized();
                var lat = new Vec3(-fwd.Z, 0, fwd.X);

                for (int sideSign = -1; sideSign <= 1; sideSign += 2)
                {
                    double pinnuleAng = curAng + sideSign * rng.Range(0.9, 1.25);
                    var pDir = new Vec3(Math.Cos(pinnuleAng), 0, Math.Sin(pinnuleAng));
                    double pLen = rng.Range(0.08, 0.16) * (1.05 - prog * 0.4);
                    var pRoot = cur + lat * (sideSign * 0.004);
                    var pTip = pRoot + pDir * pLen + new Vec3(0, rng.Range(0.008, 0.022), 0);

                    Primitives.CurvedLeaf(m, pRoot, pTip, lat * sideSign, 0.022 * (1.0 - prog * 0.3),
                        leafBase, leafTip, camber: 0.006, longitudinal: 3);

                    if (s % 2 == 0 && s < segs - 1)
                    {
                        var subRoot = Vec3.Lerp(pRoot, pTip, 0.45);
                        var subDir = (pDir + fwd * (sideSign * 0.3)).Normalized();
                        var subTip = subRoot + subDir * (pLen * 0.55) + new Vec3(0, 0.008, 0);
                        Primitives.CurvedLeaf(m, subRoot, subTip, lat * sideSign, 0.014,
                            leafBase, leafTip, camber: 0.004, longitudinal: 3);
                    }
                }

                if (s >= 2 && s <= segs - 3 && rng.NextDouble() < 0.45)
                {
                    capsuleNodes.Add((cur, lat * (rng.NextDouble() < 0.5 ? 1 : -1)));
                }
            }

            Primitives.Tube(m, path, radii, 4, (step, u) => (stemCol, 1.0, (double)step / segs, u, 0, 0));
        }

        // 3. Lateral pleurocarpous capsules arising from stem sides on wiry setae
        int capsCount = Math.Min(16, capsuleNodes.Count);
        for (int i = 0; i < capsCount; i++)
        {
            var (nodePos, lat) = capsuleNodes[i];
            double setaH = rng.Range(0.16, 0.28);
            var archDir = (lat * 0.6 + new Vec3(rng.Range(-0.3, 0.3), 0, rng.Range(-0.3, 0.3))).Normalized();

            var b = nodePos + new Vec3(0, 0.005, 0);
            var p1 = b + Vec3.Up * (setaH * 0.55) + archDir * 0.02;
            var p2 = b + Vec3.Up * setaH + archDir * 0.05;
            var p3 = p2 + new Vec3(0, -0.035, 0) + archDir * 0.035;

            Primitives.Tube(m, new[] { b, p1, p2, p3 },
                new[] { 0.0035, 0.0028, 0.0022, 0.0018 }, 4, (step, u) => (setaCol, 1.0, step / 3.0, u, 0, 0));

            var cDir = (p3 - p2).Normalized();
            var cCenter = p3 + cDir * 0.018;
            Primitives.Ellipsoid(m, cCenter, new Vec3(0.014, 0.026, 0.014), 4, 8,
                (u, v) => (capsuleCol, 1.0, u, v, 0, 0));
        }
    }

    /// <summary>
    /// Velvetweave moss (Polytrichum / Dicranum dense acrocarpous micro-turf):
    /// Ultra-dense velvety micro-turf lawn with fine erect needle-like shoot tips,
    /// punctuated by tall, erect wiry setae bearing distinct 4-angled boxy urn capsules with basal apophysis.
    /// </summary>
    private static void Velvetweave(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var turfBaseCol = Primitives.Scale(c1, 0.65);
        var turfMidCol = c1;
        var turfCrownCol = Primitives.Mix(c1, c2, 0.70);
        var needleCol = c2;
        var setaCol = new[] { 0.58, 0.24, 0.12 };
        var apophysisCol = new[] { 0.34, 0.22, 0.10 };
        var urnCol = new[] { 0.52, 0.44, 0.18 };
        var lidCol = new[] { 0.62, 0.28, 0.12 };

        // 1. Gently undulating micro-turf dome
        const int rings = 16;
        const int segs = 32;
        int startVert = m.VertexCount;
        ulong ns = Rng.Mix(seed, 0x58F3);

        for (int r = 0; r <= rings; r++)
        {
            double f = (double)r / rings;
            double rad = f * 0.94;
            for (int s = 0; s <= segs; s++)
            {
                double th = 2 * Math.PI * s / segs;
                double px = Math.Cos(th) * rad;
                double pz = Math.Sin(th) * rad;

                double domeH = 0.32 * Math.Cos(f * Math.PI * 0.5);
                double microWobble = 0.025 * Noise.Gradient(ns, px * 8.0, pz * 8.0);
                double h = Math.Max(0.005, domeH + microWobble);
                if (r == rings) h = 0.005;

                var col = f < 0.5
                    ? Primitives.Mix(turfCrownCol, turfMidCol, f * 2.0)
                    : Primitives.Mix(turfMidCol, turfBaseCol, (f - 0.5) * 2.0);

                m.AddVertex(new Vec3(px, h, pz), Vec3.Up, col, 1.0, f, (double)s / segs, 0.0, 0.0);
            }
        }

        for (int r = 0; r < rings; r++)
        {
            for (int s = 0; s < segs; s++)
            {
                int a = startVert + r * (segs + 1) + s;
                int b = a + segs + 1;
                Primitives.TriangleFacing(m, a, a + 1, b, Vec3.Up);
                Primitives.TriangleFacing(m, a + 1, b + 1, b, Vec3.Up);
            }
        }
        m.RecomputeNormals();

        // 2. Micro-turf erect shoot needle tips (giving dense velvety texture)
        int shootCount = 150 + rng.NextInt(35);
        for (int i = 0; i < shootCount; i++)
        {
            double ang = rng.Range(0, 2 * Math.PI);
            double dist = Math.Sqrt(rng.NextDouble()) * 0.88;
            double px = Math.Cos(ang) * dist;
            double pz = Math.Sin(ang) * dist;
            double domeH = 0.32 * Math.Cos(dist / 0.94 * Math.PI * 0.5);
            if (domeH < 0.05) continue;

            var b = new Vec3(px, domeH, pz);
            double needleH = rng.Range(0.035, 0.065);
            var tip = b + new Vec3(rng.Range(-0.01, 0.01), needleH, rng.Range(-0.01, 0.01));
            Primitives.Tube(m, new[] { b, tip }, new[] { 0.004, 0.0008 }, 4, (step, u) => (needleCol, 1.0, step, u, 0, 0));
        }

        // 3. Tall erect wire setae with 4-angled boxy urn capsules (Polytrichum sporophytes)
        int setaeCount = 28 + rng.NextInt(8);
        for (int i = 0; i < setaeCount; i++)
        {
            double ang = rng.Range(0, 2 * Math.PI);
            double dist = Math.Sqrt(rng.NextDouble()) * 0.72;
            double px = Math.Cos(ang) * dist;
            double pz = Math.Sin(ang) * dist;
            double domeH = 0.32 * Math.Cos(dist / 0.94 * Math.PI * 0.5);
            if (domeH < 0.10) continue;

            var baseP = new Vec3(px, domeH, pz);
            double tallH = rng.Range(0.55, 0.92);
            var lean = new Vec3(rng.Range(-0.06, 0.06), 0, rng.Range(-0.06, 0.06));

            var p1 = baseP + Vec3.Up * (tallH * 0.55) + lean * 0.4;
            var p2 = baseP + Vec3.Up * (tallH * 0.90) + lean;
            var nodDir = (lean.Normalized() * 0.5 + new Vec3(rng.Range(-0.5, 0.5), 0, rng.Range(-0.5, 0.5))).Normalized();
            var pApex = p2 + Vec3.Up * (tallH * 0.10) + nodDir * 0.035;

            Primitives.Tube(m, new[] { baseP, p1, p2, pApex },
                new[] { 0.0032, 0.0025, 0.0020, 0.0018 }, 4, (step, u) => (setaCol, 1.0, step / 3.0, u, 0, 0));

            var cDir = (nodDir * 0.7 + Vec3.Up * 0.3).Normalized();
            var apoPos = pApex + cDir * 0.008;
            Primitives.Tube(m, new[] { pApex, apoPos }, new[] { 0.004, 0.009 }, 4, (step, u) => (apophysisCol, 1.0, step, u, 0, 0));

            var urnStart = apoPos;
            var urnEnd = urnStart + cDir * 0.042;
            Primitives.Tube(m, new[] { urnStart, urnEnd }, new[] { 0.0095, 0.0085 }, 4, (step, u) => (urnCol, 1.0, step, u, 0, 0));

            var lidTip = urnEnd + cDir * 0.016;
            Primitives.Tube(m, new[] { urnEnd, lidTip }, new[] { 0.008, 0.001 }, 4, (step, u) => (lidCol, 1.0, step, u, 0, 0));
        }
    }

    /// <summary>Fallback generic carpet moss.</summary>
    private static void CarpetDefault(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var matCol = Primitives.Scale(c1, 0.7);
        int matStart = m.VertexCount;
        Primitives.Ellipsoid(m, new Vec3(0, 0.05, 0), new Vec3(1.0, 0.25, 1.0), 6, 28, (a, b) => (matCol, 1, a, b, 0, 0));
        ulong rim = Rng.Mix(seed, 0xC4A9);
        for (int i = matStart; i < m.VertexCount; i++)
        {
            var p = m.Position(i);
            double ang = Math.Atan2(p.Z, p.X);
            double wob = 1 + 0.22 * (Noise.Value3(rim, Math.Cos(ang) * 2.2, Math.Sin(ang) * 2.2, 0.5) - 0.3);
            double edge = Math.Sqrt(p.X * p.X + p.Z * p.Z);
            m.Positions[i * 3] = (float)(p.X * wob);
            m.Positions[i * 3 + 2] = (float)(p.Z * wob);
            m.Positions[i * 3 + 1] = (float)(p.Y - 0.08 * edge * edge);
        }
        m.RecomputeNormals();
        for (int k = 0; k < 70; k++)
        {
            double ang = rng.Range(0, 2 * Math.PI), r = Math.Sqrt(rng.NextDouble()) * 0.92;
            var baseP = new Vec3(Math.Cos(ang) * r, 0.1, Math.Sin(ang) * r);
            double h = rng.Range(0.5, 1.0) * (1.05 - r * 0.35);
            var lean = new Vec3(rng.Range(-0.12, 0.12), 0, rng.Range(-0.12, 0.12));
            var tipCol = Primitives.Mix(c1, c2, rng.Range(0.4, 1.0));
            Primitives.Tube(m, new[] { baseP, baseP + lean * 0.5 + new Vec3(0, h * 0.55, 0), baseP + lean + new Vec3(0, h, 0) },
                new[] { 0.05, 0.035, 0.008 }, 4, (i, v) => (Primitives.Mix(Primitives.Scale(c1, 0.75), tipCol, i / 2.0), 1, i, v, 0, 0));
        }
    }

    /// <summary>
    /// Bogglass moss (Sphagnum peat moss):
    /// Peat hummock base with upright stems carrying fascicles of spreading and pendent branches,
    /// 5-part stellate capitulum heads, and raised dark spherical capsules on elevated pseudopodia.
    /// </summary>
    private static void BogglassMoss(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var hummockCol = Primitives.Scale(c1, 0.60);
        var stemCol = Primitives.Mix(c1, c2, 0.25);
        var branchSpreadCol = Primitives.Mix(c1, c2, 0.40);
        var branchPendentCol = Primitives.Scale(c1, 0.75);
        var capitulaCol = Primitives.Mix(c1, c2, 0.75);
        var pseudopodiumCol = new[] { 0.82, 0.85, 0.65 };
        var capsuleCol = new[] { 0.16, 0.10, 0.08 };

        // 1. Peat hummock base
        int hummockStart = m.VertexCount;
        Primitives.Ellipsoid(m, new Vec3(0, 0.04, 0), new Vec3(0.92, 0.20, 0.92), 6, 28,
            (a, b) => (hummockCol, 1.0, a, b, 0, 0));
        ulong hummockNs = Rng.Mix(seed, 0x82B1);
        for (int i = hummockStart; i < m.VertexCount; i++)
        {
            var p = m.Position(i);
            double ang = Math.Atan2(p.Z, p.X);
            double wob = 1.0 + 0.18 * (Noise.Value3(hummockNs, Math.Cos(ang) * 3.0, Math.Sin(ang) * 3.0, 0.5) - 0.3);
            m.Positions[i * 3] = (float)(p.X * wob);
            m.Positions[i * 3 + 2] = (float)(p.Z * wob);
            m.Positions[i * 3 + 1] = (float)Math.Max(0.005, p.Y * 0.7);
        }
        m.RecomputeNormals();

        // 2. Upright Sphagnum shoots with fascicles and capitula
        int shootCount = 42 + rng.NextInt(8);
        var sporophyteShoots = new List<Vec3>();

        for (int k = 0; k < shootCount; k++)
        {
            double ang = rng.Range(0, 2 * Math.PI);
            double r = Math.Sqrt(rng.NextDouble()) * 0.86;
            var b = new Vec3(Math.Cos(ang) * r, 0.06, Math.Sin(ang) * r);
            double h = rng.Range(0.65, 0.98) * (1.05 - r * 0.25);
            var top = b + new Vec3(rng.Range(-0.06, 0.06), h, rng.Range(-0.06, 0.06));

            var stem = new[] { b, Vec3.Lerp(b, top, 0.35), Vec3.Lerp(b, top, 0.70), top };
            Primitives.Tube(m, stem, new[] { 0.014, 0.012, 0.010, 0.008 }, 5,
                (i, v) => (stemCol, 1.0, i / 3.0, v, 0, 0));

            double phase = rng.Range(0, Math.PI * 2);
            for (int whorl = 0; whorl < 3; whorl++)
            {
                double t = whorl == 0 ? 0.35 : whorl == 1 ? 0.62 : 0.85;
                var node = Vec3.Lerp(b, top, t);

                int spreadCount = 3;
                for (int sb = 0; sb < spreadCount; sb++)
                {
                    double a = phase + sb * (Math.PI * 2 / spreadCount) + whorl * 0.4;
                    var radDir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                    double sLen = rng.Range(0.08, 0.13);
                    var sTip = node + radDir * sLen + new Vec3(0, rng.Range(-0.01, 0.02), 0);
                    Primitives.Tube(m, new[] { node, (node + sTip) * 0.5 + new Vec3(0, 0.006, 0), sTip },
                        new[] { 0.007, 0.005, 0.002 }, 3, (step, u) => (branchSpreadCol, 1.0, step / 2.0, u, 0, 0));
                }

                int pendentCount = 2;
                for (int pb = 0; pb < pendentCount; pb++)
                {
                    double a = phase + pb * Math.PI + whorl * 0.4 + 0.5;
                    var radDir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                    double pLen = rng.Range(0.10, 0.16);
                    var pTip = node + radDir * 0.02 - Vec3.Up * pLen;
                    Primitives.Tube(m, new[] { node, node + radDir * 0.025 - Vec3.Up * (pLen * 0.5), pTip },
                        new[] { 0.006, 0.004, 0.0015 }, 3, (step, u) => (branchPendentCol, 1.0, step / 2.0, u, 0, 0));
                }
            }

            int crownBranches = 5;
            for (int c = 0; c < crownBranches; c++)
            {
                double a = phase + c * (Math.PI * 2 / crownBranches);
                var cDir = new Vec3(Math.Cos(a), 0.35, Math.Sin(a)).Normalized();
                double cLen = rng.Range(0.05, 0.08);
                var cTip = top + cDir * cLen;
                Primitives.CurvedLeaf(m, top, cTip, new Vec3(-cDir.Z, 0, cDir.X), 0.025,
                    capitulaCol, capitulaCol, camber: 0.010, longitudinal: 3);
            }
            Primitives.Ellipsoid(m, top + new Vec3(0, 0.015, 0), new Vec3(0.035, 0.025, 0.035), 4, 6,
                (u, v) => (capitulaCol, 1.0, u, v, 0, 0));

            if (k < 14) sporophyteShoots.Add(top);
        }

        // 3. Elevated spherical spore capsules on pseudopodia
        for (int i = 0; i < sporophyteShoots.Count; i++)
        {
            var top = sporophyteShoots[i];
            double stalkH = rng.Range(0.12, 0.20);
            var stalkTop = top + new Vec3(rng.Range(-0.015, 0.015), stalkH, rng.Range(-0.015, 0.015));

            Primitives.Tube(m, new[] { top, (top + stalkTop) * 0.5, stalkTop },
                new[] { 0.0035, 0.0028, 0.0022 }, 4, (step, u) => (pseudopodiumCol, 1.0, step / 2.0, u, 0, 0));

            Primitives.Ellipsoid(m, stalkTop + new Vec3(0, 0.012, 0), new Vec3(0.016, 0.018, 0.016), 5, 8,
                (u, v) => (capsuleCol, 1.0, u, v, 0, 0));
        }
    }

    /// <summary>
    /// Antlerlace lichen (Cladonia / fruticose coral-antler lichen):
    /// Basal squamule crust supporting upright hollow podetia that fork into flattened palmate antler tines,
    /// tipped with dark chestnut/rufous apothecial button discs.
    /// </summary>
    private static void Antlerlace(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var squamuleCol = Primitives.Scale(c1, 0.85);
        var podetiaCol = Primitives.Mix(c1, c2, 0.35);
        var tineTipCol = c2;
        var buttonCol = new[] { 0.36, 0.16, 0.10 };

        // 1. Basal squamules: small scalloped leafy scales covering substrate
        int squamuleCount = 28 + rng.NextInt(8);
        for (int i = 0; i < squamuleCount; i++)
        {
            double a = rng.Range(0, 2 * Math.PI);
            double dist = Math.Sqrt(rng.NextDouble()) * 0.82;
            var center = new Vec3(Math.Cos(a) * dist, 0.01, Math.Sin(a) * dist);
            var fwd = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            double scale = rng.Range(0.04, 0.075);
            var rim = new List<Vec3>();
            for (int s = 0; s <= 8; s++)
            {
                double u = (double)s / 8 * Math.PI;
                double r = scale * Math.Sin(u) * (1.0 + 0.2 * Math.Cos(s * 3));
                rim.Add(center + fwd * (Math.Cos(u) * scale) + new Vec3(-fwd.Z, 0, fwd.X) * r + new Vec3(0, rng.Range(0.002, 0.012), 0));
            }
            Primitives.Fan(m, center, rim, Vec3.Up, squamuleCol, squamuleCol);
        }

        // 2. Upright antler podetia tufts
        int tuftCount = 18 + rng.NextInt(6);
        for (int t = 0; t < tuftCount; t++)
        {
            double a = rng.Range(0, 2 * Math.PI);
            double dist = Math.Sqrt(rng.NextDouble()) * 0.70;
            var root = new Vec3(Math.Cos(a) * dist, 0.015, Math.Sin(a) * dist);
            double totalH = rng.Range(0.55, 0.95) * (1.05 - dist * 0.3);

            var p0 = root;
            var p1 = root + new Vec3(rng.Range(-0.03, 0.03), totalH * 0.38, rng.Range(-0.03, 0.03));
            Primitives.Tube(m, new[] { p0, p1 }, new[] { 0.024, 0.018 }, 5,
                (step, u) => (podetiaCol, 1.0, step * 0.3, u, 0, 0));

            double forkAng = rng.Range(0, 2 * Math.PI);
            for (int f = -1; f <= 1; f += 2)
            {
                var dir1 = (Vec3.Up * 0.75 + new Vec3(Math.Cos(forkAng + f * 0.5), 0, Math.Sin(forkAng + f * 0.5)) * 0.45).Normalized();
                var p2 = p1 + dir1 * (totalH * 0.32);
                Primitives.Tube(m, new[] { p1, p2 }, new[] { 0.017, 0.012 }, 4,
                    (step, u) => (Primitives.Mix(podetiaCol, tineTipCol, 0.4), 1.0, 0.3 + step * 0.3, u, 0, 0));

                int tineCount = 2 + (rng.NextDouble() < 0.45 ? 1 : 0);
                for (int tc = 0; tc < tineCount; tc++)
                {
                    double tineSpread = (tc - (tineCount - 1) * 0.5) * 0.55;
                    var sideDir = new Vec3(-dir1.Z, 0, dir1.X).Normalized();
                    var tineDir = (dir1 * 0.7 + sideDir * tineSpread + new Vec3(0, rng.Range(0.1, 0.3), 0)).Normalized();
                    double tineLen = totalH * rng.Range(0.22, 0.34);
                    var pTine = p2 + tineDir * tineLen;

                    Primitives.CurvedLeaf(m, p2, pTine, sideDir, 0.016,
                        Primitives.Mix(podetiaCol, tineTipCol, 0.6), tineTipCol, camber: 0.003, longitudinal: 2);

                    var btnCenter = pTine + tineDir * 0.008;
                    Primitives.Ellipsoid(m, btnCenter, new Vec3(0.012, 0.008, 0.012), 4, 6,
                        (u, v) => (buttonCol, 1.0, u, v, 0, 0));
                }
            }
        }
    }

    /// <summary>
    /// Ruffle lichen (Parmelia / Platismatia foliose shield lichen):
    /// Broad undulating thallus lobes with margins curling upwards to reveal pale whitish/chalky undersides,
    /// centered with raised saucer/cup-shaped lecanorine apothecia with chestnut discs.
    /// </summary>
    private static void RuffleLichen(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var upperBaseCol = c1;
        var upperRimCol = c2;
        var underCol = new[] { 0.86, 0.86, 0.80 };
        var apotheciaRimCol = c1;
        var apotheciaDiscCol = new[] { 0.42, 0.20, 0.10 };

        // 1. Overlapping foliose lobes with curling, ruffled edges
        int lobeCount = 18 + rng.NextInt(6);
        var apotheciaSpots = new List<Vec3>();

        for (int k = 0; k < lobeCount; k++)
        {
            double baseAng = (2 * Math.PI * k) / lobeCount + rng.Range(-0.20, 0.20);
            double lobeLen = rng.Range(0.55, 0.95);
            var dir = new Vec3(Math.Cos(baseAng), 0, Math.Sin(baseAng));
            var side = new Vec3(-dir.Z, 0, dir.X);

            var lobeCenter = dir * (lobeLen * 0.45) + new Vec3(0, 0.03, 0);
            if (k % 2 == 0) apotheciaSpots.Add(lobeCenter);

            const int segU = 7;
            const int segV = 5;
            int startVert = m.VertexCount;
            ulong lobeNs = Rng.Mix(seed, (ulong)(k * 137 + 19));

            var grid = new Vec3[segU + 1, segV + 1];
            for (int u = 0; u <= segU; u++)
            {
                double fu = (double)u / segU;
                double widthEnvelope = Math.Sin(fu * Math.PI * 0.85);
                double width = widthEnvelope * 0.28 * lobeLen;
                double lift = 0.01 + fu * fu * 0.16 + (fu > 0.65 ? Math.Pow((fu - 0.65) / 0.35, 2.0) * 0.18 : 0.0);
                double curlBack = fu > 0.80 ? (fu - 0.80) * 0.08 : 0.0;

                for (int v = 0; v <= segV; v++)
                {
                    double fv = (double)v / segV;
                    double lat = (fv - 0.5) * 2.0;

                    double marginRuffle = Math.Abs(lat) * 0.06 * Math.Sin(fu * 10.0 + k);
                    double wNoise = 0.02 * Noise.Gradient(lobeNs, fu * 3.0, lat * 2.0);

                    var p = dir * (fu * lobeLen - curlBack)
                          + side * (lat * width + wNoise)
                          + Vec3.Up * (lift + marginRuffle + (lat * lat * 0.06));

                    grid[u, v] = p;
                }
            }

            // Upper surface
            for (int u = 0; u <= segU; u++)
            {
                double fu = (double)u / segU;
                for (int v = 0; v <= segV; v++)
                {
                    var col = Primitives.Mix(upperBaseCol, upperRimCol, fu * 0.8 + 0.1);
                    m.AddVertex(grid[u, v], Vec3.Up, col, 1.0, fu, (double)v / segV, 0.0, 0.0);
                }
            }
            int rowSize = segV + 1;
            for (int u = 0; u < segU; u++)
            {
                for (int v = 0; v < segV; v++)
                {
                    int a = startVert + u * rowSize + v;
                    int b = a + rowSize;
                    Primitives.TriangleFacing(m, a, a + 1, b, Vec3.Up);
                    Primitives.TriangleFacing(m, a + 1, b + 1, b, Vec3.Up);
                }
            }

            // Lower surface (pale whitish/chalky underside revealed by curled rims)
            int lowerStart = m.VertexCount;
            for (int u = 0; u <= segU; u++)
            {
                double fu = (double)u / segU;
                for (int v = 0; v <= segV; v++)
                {
                    var p = grid[u, v] - Vec3.Up * 0.003;
                    m.AddVertex(p, -Vec3.Up, underCol, 1.0, fu, (double)v / segV, 1.0, 0.0);
                }
            }
            for (int u = 0; u < segU; u++)
            {
                for (int v = 0; v < segV; v++)
                {
                    int a = lowerStart + u * rowSize + v;
                    int b = a + rowSize;
                    Primitives.TriangleFacing(m, a, b, a + 1, -Vec3.Up);
                    Primitives.TriangleFacing(m, a + 1, b, b + 1, -Vec3.Up);
                }
            }
        }
        m.RecomputeNormals();

        // 2. Lecanorine apothecia saucers with raised rims and sunken chestnut discs
        int cupCount = Math.Min(18, apotheciaSpots.Count);
        for (int i = 0; i < cupCount; i++)
        {
            var spot = apotheciaSpots[i] + new Vec3(rng.Range(-0.04, 0.04), 0.005, rng.Range(-0.04, 0.04));
            double cupR = rng.Range(0.035, 0.070);
            const int seg = 10;
            int rimStart = m.VertexCount;

            for (int s = 0; s <= seg; s++)
            {
                double th = 2 * Math.PI * s / seg;
                var rp = spot + new Vec3(Math.Cos(th) * cupR, 0.018, Math.Sin(th) * cupR);
                m.AddVertex(rp, Vec3.Up, apotheciaRimCol, 1.0, 0.5, (double)s / seg, 0.0, 0.0);
            }
            int discCenter = m.AddVertex(spot + new Vec3(0, 0.008, 0), Vec3.Up, apotheciaDiscCol, 1.0, 0.5, 0.5, 0.0, 0.0);
            for (int s = 0; s < seg; s++)
            {
                Primitives.TriangleFacing(m, discCenter, rimStart + s, rimStart + s + 1, Vec3.Up);
            }
            int baseStart = m.VertexCount;
            for (int s = 0; s <= seg; s++)
            {
                double th = 2 * Math.PI * s / seg;
                var bp = spot + new Vec3(Math.Cos(th) * (cupR * 1.15), 0.0, Math.Sin(th) * (cupR * 1.15));
                m.AddVertex(bp, Vec3.Up, apotheciaRimCol, 1.0, 0.0, (double)s / seg, 0.0, 0.0);
            }
            for (int s = 0; s < seg; s++)
            {
                Primitives.TriangleFacing(m, rimStart + s, baseStart + s, rimStart + s + 1, Vec3.Up);
                Primitives.TriangleFacing(m, rimStart + s + 1, baseStart + s, baseStart + s + 1, Vec3.Up);
            }
        }
    }

    /// <summary>
    /// Embercrust lichen (Caloplaca / Xanthoria areolate crustose lichen):
    /// Polygonal mosaic areole tiles separated by dark fissures with a faint prothallus margin,
    /// densely studded with raised fiery ember-orange apothecial discs.
    /// </summary>
    private static void Embercrust(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var areoleCol = c1;
        var rimCol = c2;
        var discCol = new[] { 0.88, 0.28, 0.05 };
        var fissureCol = new[] { 0.18, 0.16, 0.15 };

        // 1. Base substrate fissure plate
        int plateStart = m.VertexCount;
        Primitives.Ellipsoid(m, new Vec3(0, 0.002, 0), new Vec3(0.96, 0.015, 0.96), 4, 24,
            (u, v) => (fissureCol, 1.0, u, v, 0, 0));
        ulong plateNs = Rng.Mix(seed, 0x93C2);
        for (int i = plateStart; i < m.VertexCount; i++)
        {
            var p = m.Position(i);
            double ang = Math.Atan2(p.Z, p.X);
            double wob = 1.0 + 0.15 * (Noise.Value3(plateNs, Math.Cos(ang) * 2.5, Math.Sin(ang) * 2.5, 0.5) - 0.3);
            m.Positions[i * 3] = (float)(p.X * wob);
            m.Positions[i * 3 + 2] = (float)(p.Z * wob);
            m.Positions[i * 3 + 1] = (float)Math.Max(0.001, p.Y * 0.2);
        }
        m.RecomputeNormals();

        // 2. Polygonal areole mosaic tiles
        int areoleRings = 4;
        var areoleCenters = new List<Vec3>();

        for (int r = 1; r <= areoleRings; r++)
        {
            double f = (double)r / areoleRings;
            double ringR = f * 0.82;
            int countInRing = 6 + r * 6;
            for (int s = 0; s < countInRing; s++)
            {
                double th = 2 * Math.PI * s / countInRing + (r * 0.35) + rng.Range(-0.08, 0.08);
                double tileR = ringR + rng.Range(-0.03, 0.03);
                var c = new Vec3(Math.Cos(th) * tileR, 0.015 + (1.0 - f) * 0.015, Math.Sin(th) * tileR);
                areoleCenters.Add(c);

                int sides = 5 + rng.NextInt(2);
                double aRadius = (0.75 / areoleRings) * rng.Range(0.68, 0.88);
                var centerVert = m.AddVertex(c + new Vec3(0, 0.012, 0), Vec3.Up, areoleCol, 1.0, 0.5, 0.5, 0.0, 0.0);
                int rimStart = m.VertexCount;

                for (int sd = 0; sd <= sides; sd++)
                {
                    double sideTh = 2 * Math.PI * sd / sides + (th * 2.0);
                    var vPos = c + new Vec3(Math.Cos(sideTh) * aRadius, -0.008, Math.Sin(sideTh) * aRadius);
                    m.AddVertex(vPos, Vec3.Up, Primitives.Scale(areoleCol, 0.85), 1.0, 0.0, (double)sd / sides, 0.0, 0.0);
                }

                for (int sd = 0; sd < sides; sd++)
                {
                    Primitives.TriangleFacing(m, centerVert, rimStart + sd, rimStart + sd + 1, Vec3.Up);
                }
            }
        }

        // Center tile
        var centerPos = new Vec3(0, 0.028, 0);
        areoleCenters.Add(centerPos);
        int cCenter = m.AddVertex(centerPos + new Vec3(0, 0.012, 0), Vec3.Up, areoleCol, 1.0, 0.5, 0.5, 0.0, 0.0);
        int cRimStart = m.VertexCount;
        for (int sd = 0; sd <= 6; sd++)
        {
            double sideTh = 2 * Math.PI * sd / 6;
            var vPos = centerPos + new Vec3(Math.Cos(sideTh) * 0.12, -0.008, Math.Sin(sideTh) * 0.12);
            m.AddVertex(vPos, Vec3.Up, Primitives.Scale(areoleCol, 0.85), 1.0, 0.0, (double)sd / 6, 0.0, 0.0);
        }
        for (int sd = 0; sd < 6; sd++)
        {
            Primitives.TriangleFacing(m, cCenter, cRimStart + sd, cRimStart + sd + 1, Vec3.Up);
        }

        // 3. Raised fiery ember-orange apothecial discs nestled on the areoles
        int apotheciaCount = 28 + rng.NextInt(8);
        for (int i = 0; i < apotheciaCount && i < areoleCenters.Count; i++)
        {
            var tileC = areoleCenters[rng.NextInt(areoleCenters.Count)];
            var discPos = tileC + new Vec3(rng.Range(-0.03, 0.03), 0.016, rng.Range(-0.03, 0.03));
            double discR = rng.Range(0.022, 0.045);
            const int seg = 8;
            int aRimStart = m.VertexCount;

            for (int s = 0; s <= seg; s++)
            {
                double th = 2 * Math.PI * s / seg;
                var rp = discPos + new Vec3(Math.Cos(th) * discR, 0.010, Math.Sin(th) * discR);
                m.AddVertex(rp, Vec3.Up, rimCol, 1.0, 0.5, (double)s / seg, 0.0, 0.0);
            }
            int dCenter = m.AddVertex(discPos + new Vec3(0, 0.006, 0), Vec3.Up, discCol, 1.0, 0.5, 0.5, 0.0, 0.0);
            for (int s = 0; s < seg; s++)
            {
                Primitives.TriangleFacing(m, dCenter, aRimStart + s, aRimStart + s + 1, Vec3.Up);
            }
            int slopeStart = m.VertexCount;
            for (int s = 0; s <= seg; s++)
            {
                double th = 2 * Math.PI * s / seg;
                var sp = discPos + new Vec3(Math.Cos(th) * (discR * 1.25), -0.004, Math.Sin(th) * (discR * 1.25));
                m.AddVertex(sp, Vec3.Up, areoleCol, 1.0, 0.0, (double)s / seg, 0.0, 0.0);
            }
            for (int s = 0; s < seg; s++)
            {
                Primitives.TriangleFacing(m, aRimStart + s, slopeStart + s, aRimStart + s + 1, Vec3.Up);
                Primitives.TriangleFacing(m, aRimStart + s + 1, slopeStart + s, slopeStart + s + 1, Vec3.Up);
            }
        }
        m.RecomputeNormals();
    }

    /// <summary>
    /// Prostrate creeping groundcover (Coinrunner) with an interlocking network of branching stolons,
    /// adventitious rooting nodes anchoring into the soil, and a dense, shingled carpet of scalloped
    /// coin/reniform leaves completely occluding the ground.
    /// </summary>
    private static void Creeper(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var stemCol = new[] { 0.32, 0.52, 0.22 };
        var stemTip = new[] { 0.44, 0.68, 0.28 };
        var rootCol = new[] { 0.28, 0.24, 0.18 };
        var leafCenter = Primitives.Mix(c1, c2, 0.25);
        var leafRim = Primitives.Mix(c1, c2, 0.65);
        var leafUnder = Primitives.Scale(c1, 0.72);
        var youngLeafCol = Primitives.Mix(c2, new[] { 0.85, 0.95, 0.35 }, 0.4);

        // 1. Central Crown: dense rosette of coin leaves on short arching petioles covering the center
        int crownLeaves = 18 + rng.NextInt(6);
        for (int c = 0; c < crownLeaves; c++)
        {
            double az = 2 * Math.PI * c / crownLeaves + rng.Range(-0.15, 0.15);
            double dist = rng.Range(0.04, 0.22);
            double h = rng.Range(0.18, 0.45);
            var dir = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var baseP = dir * (dist * 0.25);
            var midP = dir * (dist * 0.65) + new Vec3(0, h * 0.60, 0);
            var tipP = dir * dist + new Vec3(0, h, 0);

            Primitives.Tube(m, new[] { baseP, midP, tipP }, new[] { 0.008, 0.006, 0.0045 }, 4, (i, v) => (stemCol, 1, i, v, 0, 0));

            var leafNorm = (Vec3.Up * 0.90 + dir * rng.Range(0.12, 0.30)).Normalized();
            double leafR = rng.Range(0.14, 0.19);
            ScallopedCoinLeaf(m, tipP, leafNorm, dir, leafR, leafR * 0.18, 9, 0.35, leafCenter, leafRim, leafUnder);
        }

        // 2. Primary and Secondary Stolon Network
        int nPrimary = 9 + rng.NextInt(3); // 9..11 primary stolons radiating in all directions
        for (int k = 0; k < nPrimary; k++)
        {
            double baseAz = 2 * Math.PI * k / nPrimary + rng.Range(-0.15, 0.15);
            double reach = rng.Range(0.88, 1.06);

            // Build primary stolon path
            const int segs = 6;
            var pPath = new List<Vec3>();
            var pRadii = new List<double>();
            for (int s = 0; s <= segs; s++)
            {
                double t = (double)s / segs;
                double r = t * reach;
                double wander = 0.12 * Math.Sin(t * Math.PI * 1.8 + k * 1.3);
                double az = baseAz + wander;
                double y = Math.Max(0.015, 0.035 - 0.015 * t);
                pPath.Add(new Vec3(Math.Cos(az) * r, y, Math.Sin(az) * r));
                pRadii.Add(MathD.Lerp(0.014, 0.005, t));
            }
            Primitives.Tube(m, pPath, pRadii, 6, (i, v) => (Primitives.Mix(stemCol, stemTip, (double)i / segs), 1, i, v, 0, 0));

            // Nodes along primary runner
            for (int s = 1; s <= segs; s++)
            {
                double t = (double)s / segs;
                var nodePos = pPath[s];

                // Rootlet peg into soil
                if (s % 2 == 0)
                {
                    var rTip = nodePos + new Vec3(rng.Range(-0.01, 0.01), -0.06, rng.Range(-0.01, 0.01));
                    Primitives.Tube(m, new[] { nodePos, rTip }, new[] { 0.006, 0.002 }, 4, (i, v) => (rootCol, 1, i, v, 0, 0));
                }

                // 1..2 petioles rising in organic azimuth directions from node
                int nPetioles = (s == segs) ? 2 : rng.NextDouble() < 0.70 ? 2 : 1;
                for (int p = 0; p < nPetioles; p++)
                {
                    double petAz = rng.Range(0, 2 * Math.PI);
                    var lat = new Vec3(Math.Cos(petAz), 0, Math.Sin(petAz));
                    double petH = MathD.Lerp(0.55, 0.20, t) * rng.Range(0.85, 1.15);
                    double spread = rng.Range(0.04, 0.10);
                    var pMid = nodePos + lat * (spread * 0.55) + new Vec3(0, petH * 0.65, 0);
                    var pTip = nodePos + lat * spread + new Vec3(0, petH, 0);

                    Primitives.Tube(m, new[] { nodePos, pMid, pTip }, new[] { 0.007, 0.0055, 0.004 }, 4, (i, v) => (Primitives.Mix(stemCol, stemTip, t), 1, i, v, 0, 0));

                    double leafR = MathD.Lerp(0.18, 0.09, t) * rng.Range(0.9, 1.1);
                    var leafNorm = (Vec3.Up * 0.88 + lat * rng.Range(0.12, 0.32)).Normalized();
                    bool young = t > 0.85;
                    var upperCol = young ? youngLeafCol : leafCenter;
                    var rimCol = young ? Primitives.Mix(youngLeafCol, leafRim, 0.5) : leafRim;
                    ScallopedCoinLeaf(m, pTip, leafNorm, lat, leafR, leafR * 0.18, 9, young ? 0.45 : 0.32, upperCol, rimCol, leafUnder);
                }
            }

            // Two secondary lateral branches per primary runner (alternating sides at staggered distances)
            for (int branch = 0; branch < 2; branch++)
            {
                double forkT = branch == 0 ? rng.Range(0.32, 0.45) : rng.Range(0.60, 0.75);
                int forkIdx = (int)(forkT * segs);
                var forkPos = pPath[forkIdx];
                double forkSide = (branch == 0) ? (k % 2 == 0 ? 1.0 : -1.0) : (k % 2 == 0 ? -1.0 : 1.0);
                double forkAz = baseAz + forkSide * rng.Range(0.55, 0.85);
                double forkReach = rng.Range(0.35, 0.52);

                const int bSegs = 3;
                var bPath = new List<Vec3>();
                var bRadii = new List<double>();
                for (int bs = 0; bs <= bSegs; bs++)
                {
                    double bt = (double)bs / bSegs;
                    double br = bt * forkReach;
                    double bx = forkPos.X + Math.Cos(forkAz) * br;
                    double bz = forkPos.Z + Math.Sin(forkAz) * br;
                    double by = Math.Max(0.015, forkPos.Y - 0.005 * bt);
                    bPath.Add(new Vec3(bx, by, bz));
                    bRadii.Add(MathD.Lerp(0.010, 0.004, bt));
                }
                Primitives.Tube(m, bPath, bRadii, 6, (i, v) => (Primitives.Mix(stemCol, stemTip, forkT), 1, i, v, 0, 0));

                // Nodes along branch
                for (int bs = 1; bs <= bSegs; bs++)
                {
                    double bt = (double)bs / bSegs;
                    var bNode = bPath[bs];

                    if (bs == 2)
                    {
                        var rTip = bNode + new Vec3(0, -0.05, 0);
                        Primitives.Tube(m, new[] { bNode, rTip }, new[] { 0.005, 0.002 }, 4, (i, v) => (rootCol, 1, i, v, 0, 0));
                    }

                    double petAz = rng.Range(0, 2 * Math.PI);
                    var lat = new Vec3(Math.Cos(petAz), 0, Math.Sin(petAz));
                    double petH = MathD.Lerp(0.48, 0.18, bt) * rng.Range(0.85, 1.15);
                    double spread = rng.Range(0.04, 0.08);
                    var pMid = bNode + lat * (spread * 0.55) + new Vec3(0, petH * 0.65, 0);
                    var pTip = bNode + lat * spread + new Vec3(0, petH, 0);

                    Primitives.Tube(m, new[] { bNode, pMid, pTip }, new[] { 0.006, 0.0045, 0.0035 }, 4, (i, v) => (stemCol, 1, i, v, 0, 0));

                    double leafR = MathD.Lerp(0.15, 0.085, bt) * rng.Range(0.9, 1.1);
                    var leafNorm = (Vec3.Up * 0.88 + lat * 0.25).Normalized();
                    ScallopedCoinLeaf(m, pTip, leafNorm, lat, leafR, leafR * 0.18, 9, 0.35, leafCenter, leafRim, leafUnder);
                }
            }
        }
    }

    /// <summary>Double-sided palmately trifoliate clover leaf with 3 obcordate (heart-shaped) leaflets,
    /// cambered midrib crease, and realistic pale chevron watermark bands.</summary>
    private static void TrifoliateLeaf(MeshData m, Vec3 apex, Vec3 normal, Vec3 fwd, double leafSpan,
        double[] c1, double[] c2, ulong seed)
    {
        normal = normal.Normalized();
        var side = normal.Cross(fwd).Normalized();
        if (side.LengthSq < 1e-8) side = normal.Cross(new Vec3(1, 0, 0)).Normalized();
        fwd = side.Cross(normal).Normalized();

        var colBase = Primitives.Scale(c1, 0.78);
        var colWatermark = Primitives.Mix(c2, new[] { 1.0, 1.0, 0.92 }, 0.72);
        var colTip = c2;
        var colUnder = Primitives.Scale(c1, 0.65);

        double leafletLen = leafSpan * 0.55;
        double leafletHalfW = leafletLen * 0.48;
        double camber = leafletHalfW * 0.35;

        // 3 obcordate leaflets at 120-degree intervals
        for (int l = 0; l < 3; l++)
        {
            double la = 2 * Math.PI * l / 3;
            var lDir = (fwd * Math.Cos(la) + side * Math.Sin(la) + normal * 0.08).Normalized();
            var lSide = normal.Cross(lDir).Normalized();

            // Build obcordate grid (5 longitudinal rows, 3 points across)
            const int L_SEGS = 4;
            var upGrid = new int[L_SEGS + 1, 3];
            var lowGrid = new int[L_SEGS + 1, 3];

            for (int i = 0; i <= L_SEGS; i++)
            {
                double t = (double)i / L_SEGS;
                double w = t switch
                {
                    0.0 => 0.005,
                    0.25 => leafletHalfW * 0.55,
                    0.50 => leafletHalfW * 0.88,
                    0.75 => leafletHalfW * 1.0,
                    _ => leafletHalfW * 0.75, // apical lobe indentation
                };

                // Color selection for chevron band
                double[] rowCol = t switch
                {
                    <= 0.20 => colBase,
                    <= 0.60 => colWatermark, // pale chevron watermark band
                    _ => colTip,
                };

                for (int j = 0; j <= 2; j++)
                {
                    double x = j - 1.0; // -1 (left), 0 (midrib), 1 (right)
                    double along = (j == 1 && i == L_SEGS) ? leafletLen * 0.86 : leafletLen * t;
                    double lift = (j == 1) ? -camber * 0.6 : camber * 0.4;
                    var pos = apex + lDir * along + lSide * (w * x) + normal * lift;

                    var nUp = (normal * 0.85 + lDir * 0.25 + lSide * (x * 0.45)).Normalized();
                    var nLow = (-normal * 0.85 + lDir * 0.25 + lSide * (x * 0.45)).Normalized();

                    double u = t;
                    double v = (x + 1.0) * 0.5;

                    upGrid[i, j] = m.AddVertex(pos, nUp, rowCol, 1.0, u, v, 0.0, 1.0);
                    lowGrid[i, j] = m.AddVertex(pos, nLow, colUnder, 1.0, u, v, 1.0, 1.0);
                }
            }

            // Connect grid triangles
            for (int i = 0; i < L_SEGS; i++)
            {
                for (int j = 0; j < 2; j++)
                {
                    int u00 = upGrid[i, j], u01 = upGrid[i, j + 1];
                    int u10 = upGrid[i + 1, j], u11 = upGrid[i + 1, j + 1];
                    Primitives.TriangleFacing(m, u00, u10, u01, normal);
                    Primitives.TriangleFacing(m, u01, u10, u11, normal);

                    int l00 = lowGrid[i, j], l01 = lowGrid[i, j + 1];
                    int l10 = lowGrid[i + 1, j], l11 = lowGrid[i + 1, j + 1];
                    Primitives.TriangleFacing(m, l00, l01, l10, -normal);
                    Primitives.TriangleFacing(m, l01, l11, l10, -normal);
                }
            }
        }
    }

    /// <summary>Spherical clover pompom flower head of creamy-white tubular florets with pink-blush tips.</summary>
    private static void CloverPompom(MeshData m, Vec3 top, double headR, ulong seed)
    {
        var rng = Rng.Keyed(seed, "clover.pompom", 0);
        var sepalCol = new[] { 0.25, 0.48, 0.20 };
        var floretBase = new[] { 0.98, 0.98, 0.94 };
        var floretTip = new[] { 0.95, 0.82, 0.86 }; // soft clover pink blush

        // Calyx collar
        int sepals = 6;
        for (int s = 0; s < sepals; s++)
        {
            double sa = 2 * Math.PI * s / sepals;
            var sTip = top + new Vec3(Math.Cos(sa) * headR * 0.6, -headR * 0.35, Math.Sin(sa) * headR * 0.6);
            Primitives.Tube(m, new[] { top, sTip }, new[] { 0.008, 0.002 }, 3, (i, v) => (sepalCol, 1, i, v, 0, 0));
        }

        // Dense hemisphere of florets
        int florets = 18 + rng.NextInt(5);
        for (int f = 0; f < florets; f++)
        {
            double fa = 2 * Math.PI * f / florets + rng.Range(-0.15, 0.15);
            double pitch = rng.Range(0.2, 1.35); // hemisphere spread
            var fDir = new Vec3(Math.Sin(pitch) * Math.Cos(fa), Math.Cos(pitch), Math.Sin(pitch) * Math.Sin(fa)).Normalized();
            var fSide = Vec3.Up.Cross(fDir).Normalized();
            if (fSide.LengthSq < 1e-8) fSide = new Vec3(1, 0, 0);

            double flen = headR * rng.Range(0.85, 1.15);
            double fhw = headR * 0.18;
            var b = top + fDir * (headR * 0.2);
            var t = top + fDir * flen;

            int start = m.VertexCount;
            Primitives.CurvedLeaf(m, b, t, fSide, fhw, floretBase, floretTip, camber: 0.005, longitudinal: 3);
            for (int i = start; i < m.VertexCount; i++) m.UV2[i * 2] = 1; // mark as fruit/floret sheen
        }
    }

    /// <summary>
    /// Clonal meadow clover groundcover (Trifold) spreading via prostrate surface stolons with adventitious rooting
    /// nodes and a dense, interlocking carpet of trifoliate (3-lobed obcordate) leaves with pale chevron
    /// watermark bands, accented by spherical pompom flower heads nestled in the foliage.
    /// </summary>
    private static void Trifold(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var stemCol = new[] { 0.28, 0.50, 0.22 };
        var stemTip = new[] { 0.40, 0.65, 0.28 };
        var rootCol = new[] { 0.26, 0.22, 0.16 };

        // 1. Central Crown: 14..18 trifoliate leaves filling the center
        int crownLeaves = 14 + rng.NextInt(5);
        for (int c = 0; c < crownLeaves; c++)
        {
            double az = 2 * Math.PI * c / crownLeaves + rng.Range(-0.18, 0.18);
            double dist = rng.Range(0.04, 0.22);
            double h = rng.Range(0.22, 0.45);
            var dir = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var baseP = dir * (dist * 0.25);
            var midP = dir * (dist * 0.65) + new Vec3(0, h * 0.60, 0);
            var tipP = dir * dist + new Vec3(0, h, 0);

            Primitives.Tube(m, new[] { baseP, midP, tipP }, new[] { 0.007, 0.0055, 0.004 }, 4, (i, v) => (stemCol, 1, i, v, 0, 0));

            var leafNorm = (Vec3.Up * 0.90 + dir * rng.Range(0.12, 0.28)).Normalized();
            double leafSpan = rng.Range(0.26, 0.34);
            ulong lSeed = Rng.Mix(seed, (ulong)(c * 179 + 7));
            TrifoliateLeaf(m, tipP, leafNorm, dir, leafSpan, c1, c2, lSeed);
        }

        // 2. Creeping Stolons (Runners)
        int nPrimary = 8 + rng.NextInt(3); // 8..10 primary stolons radiating outward
        for (int k = 0; k < nPrimary; k++)
        {
            double baseAz = 2 * Math.PI * k / nPrimary + rng.Range(-0.15, 0.15);
            double reach = rng.Range(0.85, 1.05);

            const int segs = 5;
            var pPath = new List<Vec3>();
            var pRadii = new List<double>();
            for (int s = 0; s <= segs; s++)
            {
                double t = (double)s / segs;
                double r = t * reach;
                double wander = 0.10 * Math.Sin(t * Math.PI * 2.0 + k * 1.5);
                double az = baseAz + wander;
                double y = Math.Max(0.015, 0.035 - 0.015 * t);
                pPath.Add(new Vec3(Math.Cos(az) * r, y, Math.Sin(az) * r));
                pRadii.Add(MathD.Lerp(0.012, 0.004, t));
            }
            Primitives.Tube(m, pPath, pRadii, 6, (i, v) => (Primitives.Mix(stemCol, stemTip, (double)i / segs), 1, i, v, 0, 0));

            // Nodes along primary runner
            for (int s = 1; s <= segs; s++)
            {
                double t = (double)s / segs;
                var nodePos = pPath[s];

                // Rootlet peg into soil
                if (s % 2 == 0)
                {
                    var rTip = nodePos + new Vec3(rng.Range(-0.01, 0.01), -0.06, rng.Range(-0.01, 0.01));
                    Primitives.Tube(m, new[] { nodePos, rTip }, new[] { 0.005, 0.002 }, 4, (i, v) => (rootCol, 1, i, v, 0, 0));
                }

                // Petioles rising from node
                int nPetioles = (s == segs) ? 2 : rng.NextDouble() < 0.65 ? 2 : 1;
                for (int p = 0; p < nPetioles; p++)
                {
                    double petAz = rng.Range(0, 2 * Math.PI);
                    var lat = new Vec3(Math.Cos(petAz), 0, Math.Sin(petAz));
                    double petH = MathD.Lerp(0.48, 0.20, t) * rng.Range(0.85, 1.15);
                    double spread = rng.Range(0.04, 0.10);
                    var pMid = nodePos + lat * (spread * 0.55) + new Vec3(0, petH * 0.65, 0);
                    var pTip = nodePos + lat * spread + new Vec3(0, petH, 0);

                    Primitives.Tube(m, new[] { nodePos, pMid, pTip }, new[] { 0.0065, 0.005, 0.0035 }, 4, (i, v) => (Primitives.Mix(stemCol, stemTip, t), 1, i, v, 0, 0));

                    double leafSpan = MathD.Lerp(0.32, 0.18, t) * rng.Range(0.9, 1.1);
                    var leafNorm = (Vec3.Up * 0.90 + lat * rng.Range(0.10, 0.28)).Normalized();
                    ulong lSeed = Rng.Mix(seed, (ulong)(k * 547 + s * 43 + p * 13 + 3));
                    TrifoliateLeaf(m, pTip, leafNorm, lat, leafSpan, c1, c2, lSeed);
                }
            }

            // Two secondary lateral branches per primary runner
            for (int branch = 0; branch < 2; branch++)
            {
                double forkT = branch == 0 ? rng.Range(0.35, 0.48) : rng.Range(0.62, 0.76);
                int forkIdx = (int)(forkT * segs);
                var forkPos = pPath[forkIdx];
                double forkSide = (branch == 0) ? (k % 2 == 0 ? 1.0 : -1.0) : (k % 2 == 0 ? -1.0 : 1.0);
                double forkAz = baseAz + forkSide * rng.Range(0.55, 0.85);
                double forkReach = rng.Range(0.32, 0.50);

                const int bSegs = 3;
                var bPath = new List<Vec3>();
                var bRadii = new List<double>();
                for (int bs = 0; bs <= bSegs; bs++)
                {
                    double bt = (double)bs / bSegs;
                    double br = bt * forkReach;
                    double bx = forkPos.X + Math.Cos(forkAz) * br;
                    double bz = forkPos.Z + Math.Sin(forkAz) * br;
                    double by = Math.Max(0.015, forkPos.Y - 0.005 * bt);
                    bPath.Add(new Vec3(bx, by, bz));
                    bRadii.Add(MathD.Lerp(0.009, 0.0035, bt));
                }
                Primitives.Tube(m, bPath, bRadii, 6, (i, v) => (Primitives.Mix(stemCol, stemTip, forkT), 1, i, v, 0, 0));

                // Nodes along branch
                for (int bs = 1; bs <= bSegs; bs++)
                {
                    double bt = (double)bs / bSegs;
                    var bNode = bPath[bs];

                    double petAz = rng.Range(0, 2 * Math.PI);
                    var lat = new Vec3(Math.Cos(petAz), 0, Math.Sin(petAz));
                    double petH = MathD.Lerp(0.42, 0.18, bt) * rng.Range(0.85, 1.15);
                    double spread = rng.Range(0.03, 0.08);
                    var pMid = bNode + lat * (spread * 0.55) + new Vec3(0, petH * 0.65, 0);
                    var pTip = bNode + lat * spread + new Vec3(0, petH, 0);

                    Primitives.Tube(m, new[] { bNode, pMid, pTip }, new[] { 0.0055, 0.004, 0.003 }, 4, (i, v) => (stemCol, 1, i, v, 0, 0));

                    double leafSpan = MathD.Lerp(0.26, 0.16, bt) * rng.Range(0.9, 1.1);
                    var leafNorm = (Vec3.Up * 0.90 + lat * 0.20).Normalized();
                    ulong blSeed = Rng.Mix(seed, (ulong)(k * 823 + bs * 31 + 19));
                    TrifoliateLeaf(m, pTip, leafNorm, lat, leafSpan, c1, c2, blSeed);
                }
            }
        }

        // 3. Clover Pompom Flower Heads: 7..10 globular heads nestled in the foliage
        int nPompoms = 7 + rng.NextInt(4);
        for (int fl = 0; fl < nPompoms; fl++)
        {
            double fa = 2 * Math.PI * fl / nPompoms + rng.Range(-0.25, 0.25);
            double fr = rng.Range(0.18, 0.70);
            double fh = rng.Range(0.45, 0.68); // nestled just above the leaf canopy
            var fBase = new Vec3(Math.Cos(fa) * fr * 0.7, 0.02, Math.Sin(fa) * fr * 0.7);
            var fMid = new Vec3(Math.Cos(fa) * fr * 0.88, fh * 0.55, Math.Sin(fa) * fr * 0.88);
            var fTop = new Vec3(Math.Cos(fa) * fr, fh, Math.Sin(fa) * fr);

            Primitives.Tube(m, new[] { fBase, fMid, fTop }, new[] { 0.008, 0.006, 0.004 }, 4, (i, v) => (stemTip, 1, i, v, 0, 0));
            CloverPompom(m, fTop, rng.Range(0.055, 0.075), Rng.Mix(seed, (ulong)(fl * 433 + 71)));
        }
    }

    /// <summary>Double-sided circular peltate saucer leaf blade with dish-like concave curvature
    /// and a distinct pale central pip where the petiole connects.</summary>
    private static void PeltateSaucerLeaf(MeshData m, Vec3 center, Vec3 normal, double radius, double dishCup,
        double[] c1, double[] c2, ulong seed)
    {
        normal = normal.Normalized();
        var side1 = normal.Cross(new Vec3(1, 0, 0)).Normalized();
        if (side1.LengthSq < 1e-8) side1 = normal.Cross(new Vec3(0, 0, 1)).Normalized();
        var side2 = normal.Cross(side1).Normalized();

        var colPip = new[] { 0.85, 0.88, 0.78 }; // pale waxy central pip
        var colBody = Primitives.Mix(c1, c2, 0.35);
        var colRim = Primitives.Mix(c2, new[] { 0.88, 0.92, 0.82 }, 0.35);
        var colUnder = Primitives.Scale(c1, 0.75);

        const int N = 12;
        var rimUpper = new int[N + 1];
        var rimLower = new int[N + 1];

        // Upper center vertex (pip)
        int cUpper = m.AddVertex(center, normal, colPip, 1.0, 0.5, 0.5, 0.0, 1.0);

        // Lower center vertex
        var apexLower = center - normal * (radius * 0.08);
        int cLower = m.AddVertex(apexLower, -normal, colUnder, 1.0, 0.5, 0.5, 1.0, 1.0);

        for (int s = 0; s <= N; s++)
        {
            double th = 2 * Math.PI * s / N;
            double cosTh = Math.Cos(th);
            double sinTh = Math.Sin(th);
            var pDir = side1 * cosTh + side2 * sinTh;

            // Saucer rim curls up slightly relative to the center
            var rimPos = center + pDir * radius + normal * dishCup;

            var upperNormal = (normal * 0.85 + pDir * 0.35).Normalized();
            var lowerNormal = (-normal * 0.85 + pDir * 0.35).Normalized();

            double u = 0.5 + 0.5 * cosTh;
            double v = 0.5 + 0.5 * sinTh;

            rimUpper[s] = m.AddVertex(rimPos, upperNormal, colRim, 1.0, u, v, 0.0, 1.0);
            rimLower[s] = m.AddVertex(rimPos, lowerNormal, colUnder, 1.0, u, v, 1.0, 1.0);
        }

        for (int s = 0; s < N; s++)
        {
            Primitives.TriangleFacing(m, cUpper, rimUpper[s], rimUpper[s + 1], normal);
            Primitives.TriangleFacing(m, cLower, rimLower[s + 1], rimLower[s], -normal);
        }
    }

    /// <summary>
    /// Xerophytic silver-green peltate coin groundcover (Mooncoin) spreading via fine wiry prostrate runners with
    /// rooting nodes, forming a dense, shingled mosaic carpet of cupped saucer leaves that completely
    /// occludes warm open soil.
    /// </summary>
    private static void Mooncoin(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var wiryStemCol = new[] { 0.40, 0.36, 0.28 }; // warm bronze-olive wiry runner
        var stemTip = new[] { 0.48, 0.52, 0.36 };
        var rootCol = new[] { 0.30, 0.26, 0.20 };

        // 1. Central Crown: dense cluster of peltate coin leaves covering the center
        int crownLeaves = 20 + rng.NextInt(6);
        for (int c = 0; c < crownLeaves; c++)
        {
            double az = 2 * Math.PI * c / crownLeaves + rng.Range(-0.15, 0.15);
            double dist = rng.Range(0.03, 0.22);
            double h = rng.Range(0.15, 0.42);
            var dir = new Vec3(Math.Cos(az), 0, Math.Sin(az));
            var baseP = dir * (dist * 0.25);
            var midP = dir * (dist * 0.65) + new Vec3(0, h * 0.60, 0);
            var tipP = dir * dist + new Vec3(0, h, 0);

            Primitives.Tube(m, new[] { baseP, midP, tipP }, new[] { 0.007, 0.0055, 0.004 }, 4, (i, v) => (wiryStemCol, 1, i, v, 0, 0));

            var leafNorm = (Vec3.Up * 0.92 + dir * rng.Range(0.10, 0.28)).Normalized();
            double leafR = rng.Range(0.13, 0.18);
            ulong lSeed = Rng.Mix(seed, (ulong)(c * 211 + 19));
            PeltateSaucerLeaf(m, tipP, leafNorm, leafR, leafR * 0.16, c1, c2, lSeed);
        }

        // 2. Wiry Stolon Network
        int nPrimary = 9 + rng.NextInt(3); // 9..11 primary runners
        for (int k = 0; k < nPrimary; k++)
        {
            double baseAz = 2 * Math.PI * k / nPrimary + rng.Range(-0.15, 0.15);
            double reach = rng.Range(0.88, 1.06);

            const int segs = 6;
            var pPath = new List<Vec3>();
            var pRadii = new List<double>();
            for (int s = 0; s <= segs; s++)
            {
                double t = (double)s / segs;
                double r = t * reach;
                double wander = 0.12 * Math.Sin(t * Math.PI * 1.8 + k * 1.4);
                double az = baseAz + wander;
                double y = Math.Max(0.012, 0.030 - 0.015 * t);
                pPath.Add(new Vec3(Math.Cos(az) * r, y, Math.Sin(az) * r));
                pRadii.Add(MathD.Lerp(0.010, 0.0035, t));
            }
            Primitives.Tube(m, pPath, pRadii, 6, (i, v) => (Primitives.Mix(wiryStemCol, stemTip, (double)i / segs), 1, i, v, 0, 0));

            // Nodes along primary runner
            for (int s = 1; s <= segs; s++)
            {
                double t = (double)s / segs;
                var nodePos = pPath[s];

                // Rootlet peg into soil
                if (s % 2 == 0)
                {
                    var rTip = nodePos + new Vec3(rng.Range(-0.01, 0.01), -0.05, rng.Range(-0.01, 0.01));
                    Primitives.Tube(m, new[] { nodePos, rTip }, new[] { 0.004, 0.002 }, 4, (i, v) => (rootCol, 1, i, v, 0, 0));
                }

                // Petioles rising from node
                int nPetioles = (s == segs) ? 2 : rng.NextDouble() < 0.70 ? 2 : 1;
                for (int p = 0; p < nPetioles; p++)
                {
                    double petAz = rng.Range(0, 2 * Math.PI);
                    var lat = new Vec3(Math.Cos(petAz), 0, Math.Sin(petAz));
                    double petH = MathD.Lerp(0.52, 0.16, t) * rng.Range(0.85, 1.15);
                    double spread = rng.Range(0.03, 0.09);
                    var pMid = nodePos + lat * (spread * 0.55) + new Vec3(0, petH * 0.65, 0);
                    var pTip = nodePos + lat * spread + new Vec3(0, petH, 0);

                    Primitives.Tube(m, new[] { nodePos, pMid, pTip }, new[] { 0.006, 0.0045, 0.0035 }, 4, (i, v) => (Primitives.Mix(wiryStemCol, stemTip, t), 1, i, v, 0, 0));

                    double leafR = MathD.Lerp(0.17, 0.085, t) * rng.Range(0.9, 1.1);
                    var leafNorm = (Vec3.Up * 0.92 + lat * rng.Range(0.08, 0.28)).Normalized();
                    ulong lSeed = Rng.Mix(seed, (ulong)(k * 701 + s * 41 + p * 11 + 7));
                    PeltateSaucerLeaf(m, pTip, leafNorm, leafR, leafR * 0.16, c1, c2, lSeed);
                }
            }

            // Two secondary lateral branches per primary runner
            for (int branch = 0; branch < 2; branch++)
            {
                double forkT = branch == 0 ? rng.Range(0.32, 0.45) : rng.Range(0.60, 0.75);
                int forkIdx = (int)(forkT * segs);
                var forkPos = pPath[forkIdx];
                double forkSide = (branch == 0) ? (k % 2 == 0 ? 1.0 : -1.0) : (k % 2 == 0 ? -1.0 : 1.0);
                double forkAz = baseAz + forkSide * rng.Range(0.55, 0.85);
                double forkReach = rng.Range(0.32, 0.50);

                const int bSegs = 3;
                var bPath = new List<Vec3>();
                var bRadii = new List<double>();
                for (int bs = 0; bs <= bSegs; bs++)
                {
                    double bt = (double)bs / bSegs;
                    double br = bt * forkReach;
                    double bx = forkPos.X + Math.Cos(forkAz) * br;
                    double bz = forkPos.Z + Math.Sin(forkAz) * br;
                    double by = Math.Max(0.012, forkPos.Y - 0.005 * bt);
                    bPath.Add(new Vec3(bx, by, bz));
                    bRadii.Add(MathD.Lerp(0.008, 0.003, bt));
                }
                Primitives.Tube(m, bPath, bRadii, 6, (i, v) => (Primitives.Mix(wiryStemCol, stemTip, forkT), 1, i, v, 0, 0));

                // Nodes along branch
                for (int bs = 1; bs <= bSegs; bs++)
                {
                    double bt = (double)bs / bSegs;
                    var bNode = bPath[bs];

                    double petAz = rng.Range(0, 2 * Math.PI);
                    var lat = new Vec3(Math.Cos(petAz), 0, Math.Sin(petAz));
                    double petH = MathD.Lerp(0.42, 0.16, bt) * rng.Range(0.85, 1.15);
                    double spread = rng.Range(0.03, 0.07);
                    var pMid = bNode + lat * (spread * 0.55) + new Vec3(0, petH * 0.65, 0);
                    var pTip = bNode + lat * spread + new Vec3(0, petH, 0);

                    Primitives.Tube(m, new[] { bNode, pMid, pTip }, new[] { 0.005, 0.004, 0.003 }, 4, (i, v) => (wiryStemCol, 1, i, v, 0, 0));

                    double leafR = MathD.Lerp(0.15, 0.08, bt) * rng.Range(0.9, 1.1);
                    var leafNorm = (Vec3.Up * 0.92 + lat * 0.20).Normalized();
                    ulong blSeed = Rng.Mix(seed, (ulong)(k * 983 + bs * 29 + 13));
                    PeltateSaucerLeaf(m, pTip, leafNorm, leafR, leafR * 0.16, c1, c2, blSeed);
                }
            }
        }
    }

    /// <summary>Arching pinnate fronds with fan leaflets, plus one unrolling fiddlehead.</summary>
    private static void Fern(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        var stalk = new[] { 0.22, 0.27, 0.15 };
        var sorus = new[] { 0.32, 0.19, 0.08 };

        // Lady-fern-like crown: each arching frond has paired pinnae, and every pinna
        // carries smaller pinnules. The repeated subdivisions read as a fern at game scale.
        if (!juvenile)
        {
            int fronds = 6 + rng.NextInt(3);
            for (int k = 0; k < fronds; k++)
            {
                double ang = 2 * Math.PI * k / fronds + rng.Range(-0.19, 0.19);
                var rachis = new AxisParams(Length: rng.Range(1.27, 1.48),
                    BaseAngle: rng.Range(0.18, 0.32), BaseAzimuth: ang,
                    Droop: rng.Range(-1.35, -1.12), WobbleAmplitude: 0.01,
                    WobbleFrequency: 1.1, Segments: 16);
                ulong rachisSeed = Rng.Mix(seed, (ulong)(k * 301 + 13));
                var frames = Axis.Build(rachis, rachisSeed);
                SoftTube.Build(m, new SoftTubeParams(rachis, BaseRadius: 0.011, TipRadius: 0.002, Segments: 4),
                    rachisSeed, (i, v) => (stalk, 1, i, v, 0, 0));
                int pairs = 11 + rng.NextInt(3);
                for (int i = 0; i < pairs; i++)
                {
                    foreach (double sgn in new[] { -1.0, 1.0 })
                    {
                        double t = 0.14 + (i + (sgn > 0 ? 0.24 : 0.0)) * 0.066;
                        if (t > 0.94) continue;
                        var frame = Axis.Sample(frames, t);
                        double envelope = Math.Pow(Math.Sin(Math.PI * t), 0.75);
                        double length = rng.Range(0.25, 0.31) * envelope;
                        var outward = (frame.Side * sgn + frame.Tangent * 0.17).Normalized();
                        var root = frame.Point;
                        var tip = root + outward * length + new Vec3(0, -0.016 * t, 0);
                        var mid = Vec3.Lerp(root, tip, 0.53) + new Vec3(0, 0.008, 0);
                        var green = Primitives.Mix(c1, c2, 0.18 + 0.45 * t);
                        Primitives.Tube(m, new[] { root, mid, tip },
                            new[] { 0.004, 0.003, 0.0007 }, 3,
                            (j, v) => (Primitives.Scale(green, 0.78), 1, j, v, 0, 0));

                        // Secondary division: narrow, staggered pinnules on both sides
                        // of the pinna rachis, with a smaller terminal pinnule.
                        int divisions = 4;
                        for (int j = 0; j < divisions; j++)
                        {
                            double u = 0.17 + j * 0.19;
                            double leafLength = length * (0.31 - j * 0.028);
                            foreach (double side in new[] { -1.0, 1.0 })
                            {
                                var attach = Vec3.Lerp(root, tip, u + (side > 0 ? 0.035 : 0));
                                var along = (frame.Tangent * side + outward * 0.24).Normalized();
                                var leafTip = attach + along * leafLength;
                                var leafCol = Primitives.Mix(green, c2, rng.Range(0.0, 0.25));
                                double halfWidth = leafLength * rng.Range(0.23, 0.29);
                                var bladeNormal = along.Cross(outward).Normalized();
                                var bladeAxis = leafTip - attach;
                                var rim = new List<Vec3>
                                {
                                    attach,
                                    attach + bladeAxis * 0.36 + outward * halfWidth,
                                    leafTip,
                                    attach + bladeAxis * 0.36 - outward * halfWidth,
                                    attach
                                };
                                Primitives.Fan(m, attach + bladeAxis * 0.48 + bladeNormal * 0.003,
                                    rim, bladeNormal, Primitives.Scale(leafCol, 0.83), leafCol);

                                // Sori are the rust-brown spore-bearing clusters on the
                                // underside of developed pinnules, paired along the midrib.
                                if (i > 2 && j < 3 && (k + i + j) % 2 == 0)
                                {
                                    var normal = along.Cross(outward).Normalized();
                                    if (normal.Y > 0) normal = -normal;
                                    foreach (double row in new[] { 0.42, 0.67 })
                                    {
                                        var dot = Vec3.Lerp(attach, leafTip, row) + normal * 0.004;
                                        // A flat four-triangle sorus sits on the underside of the blade.
                                        // Its paired spots remain visible in a close underside view.
                                        int center = m.AddVertex(dot, normal, sorus, 1, 0.5, 0.5);
                                        int a = m.AddVertex(dot + along * 0.008, normal, sorus, 1, 1, 0.5);
                                        int b = m.AddVertex(dot + outward * 0.006, normal, sorus, 1, 0.5, 1);
                                        int c = m.AddVertex(dot - along * 0.008, normal, sorus, 1, 0, 0.5);
                                        int d = m.AddVertex(dot - outward * 0.006, normal, sorus, 1, 0.5, 0);
                                        Primitives.TriangleFacing(m, center, a, b, normal);
                                        Primitives.TriangleFacing(m, center, b, c, normal);
                                        Primitives.TriangleFacing(m, center, c, d, normal);
                                        Primitives.TriangleFacing(m, center, d, a, normal);
                                    }
                                }
                            }
                        }
                        var terminal = tip + outward * (length * 0.08);
                        Primitives.CurvedLeaf(m, tip - outward * (length * 0.16), terminal,
                            frame.Tangent, length * 0.045, Primitives.Scale(green, 0.8), green,
                            camber: 0.003, longitudinal: 3);
                    }
                }
            }
        }

        // Croziers emerge first as compact curled fists. A mature crown still has
        // a few newly unfurling fronds among its open ones.
        int fists = juvenile ? 3 + rng.NextInt(2) : 1 + rng.NextInt(2);
        for (int k = 0; k < fists; k++)
        {
            double ang = 2 * Math.PI * k / fists + rng.Range(-0.2, 0.2);
            double radius = juvenile ? rng.Range(0.035, 0.09) : rng.Range(0.02, 0.06);
            double height = juvenile ? rng.Range(0.42, 0.75) : rng.Range(0.52, 0.76);
            var baseP = new Vec3(Math.Cos(ang) * radius, 0, Math.Sin(ang) * radius);
            var radial = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
            var center = baseP + new Vec3(0, height, 0);
            var path = new List<Vec3>
            {
                baseP,
                baseP + new Vec3(0, height * 0.64, 0),
                center + new Vec3(0, -0.075, 0)
            };
            var widths = new List<double> { 0.012, 0.015, 0.019 };
            for (int j = 0; j <= 28; j++)
            {
                double u = j / 28.0;
                double theta = -Math.PI / 2 + u * Math.PI * 2.25;
                double coilRadius = 0.075 * (1 - u * 0.67);
                path.Add(center + radial * (Math.Cos(theta) * coilRadius)
                    + new Vec3(0, Math.Sin(theta) * coilRadius, 0));
                widths.Add(0.018 * (1 - u * 0.48));
            }
            Primitives.Tube(m, path, widths, 5,
                (j, v) => (Primitives.Mix(stalk, c2, j / (double)(path.Count - 1)), 1, j, v, 0, 0));

        }
    }
    private static void ClinglaceNode(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool attached)
    {
        var stemCol = Primitives.Mix(WoodyBark, new[] { 0.22, 0.32, 0.16 }, 0.45);
        var rootletCol = new[] { 0.18, 0.14, 0.10 };
        var leafUnderCol = Primitives.Mix(Primitives.Scale(c2, 0.85), new[] { 0.42, 0.55, 0.35 }, 0.50);
        var petioleCol = Primitives.Mix(stemCol, c1, 0.40);

        void IvyLeaf(Vec3 petioleEnd, Vec3 fwd, Vec3 norm, double scale)
        {
            var side = fwd.Cross(norm).Normalized();
            if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);
            double mainLen = scale * rng.Range(0.070, 0.095);
            double mainHalfW = mainLen * 0.38;
            int bladeStart = m.VertexCount;
            FoliageBlade.Build(m, petioleEnd, petioleEnd + fwd * mainLen, side, mainHalfW,
                Primitives.Scale(c1, 0.82), c1, camber: 0.14, curl: 0.10, shoulder: 0.72, segments: 3);

            foreach (double sign in new[] { -1.0, 1.0 })
            {
                double latAng = sign * 0.72 + rng.Range(-0.06, 0.06);
                var latDir = Axis.RotateAround(fwd, norm, latAng);
                var latSide = latDir.Cross(norm).Normalized();
                double latLen = mainLen * 0.78;
                FoliageBlade.Build(m, petioleEnd, petioleEnd + latDir * latLen, latSide, mainHalfW * 0.80,
                    Primitives.Scale(c1, 0.80), c1, camber: 0.12, curl: 0.08, shoulder: 0.70, segments: 3);
            }

            for (int v = bladeStart; v < m.VertexCount; v++)
                if (m.UV2[v * 2] > 0.5f) m.SetColor(v, leafUnderCol[0], leafUnderCol[1], leafUnderCol[2], 1.0);
        }

        if (attached)
        {
            // Anchoring adventitious rootlets into host bark (-Z into host)
            for (int r = 0; r < 4; r++)
            {
                var rOff = new Vec3(rng.Range(-0.008, 0.008), rng.Range(-0.006, 0.006), 0);
                Primitives.Tube(m, new[] { rOff, rOff - new Vec3(0, 0, 0.014) },
                    new[] { 0.0020, 0.0010 }, 6, (i, v) => (rootletCol, 1.0, i, v, 0, 0));
            }

            // Alternate 3-lobed ivy leaf pair plated flat against host bark
            foreach (double sign in new[] { -1.0, 1.0 })
            {
                var lDir = new Vec3(sign * 0.85, 0.45, 0.15).Normalized();
                var petEnd = lDir * 0.024;
                Primitives.Tube(m, new[] { Vec3.Zero, petEnd }, new[] { 0.0028, 0.0020 }, 6,
                    (i, v) => (petioleCol, 1.0, i, v, 0, 0));
                IvyLeaf(petEnd, (new Vec3(0, 0.65, 0) + lDir * 0.35).Normalized(), new Vec3(0, 0, 1), 1.05);
            }
        }
        else
        {
            // Ground runner node: leaf reaching upward into sunlight
            var lDir = (new Vec3(rng.Range(-0.7, 0.7), 0.85, rng.Range(-0.3, 0.3))).Normalized();
            var petEnd = lDir * 0.020;
            Primitives.Tube(m, new[] { Vec3.Zero, petEnd }, new[] { 0.0024, 0.0018 }, 6,
                (i, v) => (petioleCol, 1.0, i, v, 0, 0));
            IvyLeaf(petEnd, lDir, Vec3.Up, 0.85);

            // Anchoring rootlet down into soil
            Primitives.Tube(m, new[] { Vec3.Zero, -Vec3.Up * 0.015 }, new[] { 0.0016, 0.0008 }, 6,
                (i, v) => (rootletCol, 1.0, i, v, 0, 0));
        }
    }

    private static void SpiralvineNode(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool attached)
    {
        var stemCol = Primitives.Mix(WoodyBarkLight, c1, 0.32);
        var collarCol = Primitives.Scale(stemCol, 0.85);
        var petioleCol = Primitives.Mix(stemCol, c2, 0.40);
        var leafUnderCol = Primitives.Mix(Primitives.Scale(c2, 0.88), new[] { 0.45, 0.62, 0.38 }, 0.45);

        void PairedLeaves(Vec3 stemNode, Vec3 fwd, Vec3 norm, double scale)
        {
            var side = fwd.Cross(norm).Normalized();
            if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);
            var bladeCol = Primitives.Mix(c1, c2, 0.45);
            double bladeLen = scale * rng.Range(0.095, 0.125);
            double halfW = bladeLen * 0.28;

            foreach (double sign in new[] { -1.0, 1.0 })
            {
                var lDir = (side * (sign * 0.82) + norm * 0.48 + Vec3.Up * 0.26).Normalized();
                var lSide = lDir.Cross(Vec3.Up).Normalized();
                var petEnd = stemNode + lDir * (bladeLen * 0.20);
                Primitives.Tube(m, new[] { stemNode, petEnd }, new[] { 0.0028, 0.0020 }, 6,
                    (i, v) => (petioleCol, 1.0, i, v, 0, 0));

                int bladeStart = m.VertexCount;
                FoliageBlade.Build(m, petEnd, petEnd + lDir * bladeLen, lSide, halfW,
                    Primitives.Scale(bladeCol, 0.80), bladeCol,
                    camber: 0.16, curl: 0.18, shoulder: 0.78, segments: 4);

                for (int v = bladeStart; v < m.VertexCount; v++)
                    if (m.UV2[v * 2] > 0.5f) m.SetColor(v, leafUnderCol[0], leafUnderCol[1], leafUnderCol[2], 1.0);
            }
        }

        // Swollen annular collar
        Primitives.Ellipsoid(m, Vec3.Zero, new Vec3(0.014, 0.010, 0.014), 4, 6,
            (u, v) => (collarCol, 1.0, u, v, 0, 0));

        // Opposite pair of lanceolate leaves
        PairedLeaves(Vec3.Zero, new Vec3(0, 1, 0), attached ? new Vec3(0, 0, 1) : Vec3.Up, attached ? 1.05 : 0.85);
    }

    private static void FenhookNode(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool attached)
    {
        var stemCol = Primitives.Mix(c1, c2, 0.42);
        var hookCol = Primitives.Mix(stemCol, new[] { 0.85, 0.88, 0.72 }, 0.50);
        var leafUnderCol = Primitives.Mix(Primitives.Scale(c2, 0.85), new[] { 0.50, 0.65, 0.40 }, 0.40);

        // 4-angled stem collar with hooks
        for (int h = 0; h < 4; h++)
        {
            double a = h * (Math.PI / 2.0) + Math.PI / 4.0;
            var hDir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            Primitives.Tube(m, new[] { hDir * 0.005, hDir * 0.009 - new Vec3(0, 0.004, 0) },
                new[] { 0.0016, 0.0006 }, 6, (i, v) => (hookCol, 1.0, i, v, 0, 0));
        }

        // Starry whorl of 8 oblanceolate leaves
        int leaves = 8;
        var bladeCol = Primitives.Mix(c1, c2, 0.40);
        double bladeLen = (attached ? 1.0 : 0.80) * rng.Range(0.085, 0.115);
        double halfW = bladeLen * 0.20;

        for (int w = 0; w < leaves; w++)
        {
            double a = w * (Math.PI * 2 / leaves) + rng.Range(-0.05, 0.05);
            var lDir = new Vec3(Math.Cos(a), -0.15, Math.Sin(a)).Normalized();
            var lSide = lDir.Cross(Vec3.Up).Normalized();

            int bladeStart = m.VertexCount;
            FoliageBlade.Build(m, Vec3.Zero, lDir * bladeLen, lSide, halfW,
                Primitives.Scale(bladeCol, 0.78), bladeCol,
                camber: 0.12, curl: 0.14, shoulder: 0.82, segments: 3);

            for (int v = bladeStart; v < m.VertexCount; v++)
                if (m.UV2[v * 2] > 0.5f) m.SetColor(v, leafUnderCol[0], leafUnderCol[1], leafUnderCol[2], 1.0);
        }
    }

    /// <summary>
    /// Clinglace: Shade-tolerant root-climber (Hedera helix / English Ivy &amp; Ficus pumila / Creeping Fig).
    /// - Shared base mechanic: stem up to growth node, leaves along stem, from growth node send out new stem.
    /// - Climbing form:
    ///   - Stems hug host surface tightly with adventitious rootlet pads at every internode.
    ///   - Leaves: alternate 3-to-5 lobed ivy blades (acute central lobe + 2 lateral spreading lobes)
    ///     shingled flat against the bark/rock surface, overlapping like tiles.
    ///   - Shoot dispatch: primary leader ascends vertically; lateral shoots bifurcate around the
    ///     host circumference, blanketing the host surface with a dense evergreen veil.
    /// - Ground runner form:
    ///   - Prostrate creeping stolons in litter with rooting pads and shingled juvenile leaves.
    /// - Authored in unit-normalized bounds [0..1] for height and radius.
    /// </summary>
    private static void Clinglace(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        var stemCol = Primitives.Mix(WoodyBark, new[] { 0.22, 0.32, 0.16 }, 0.45);
        var rootletCol = new[] { 0.18, 0.14, 0.10 };
        var leafUnderCol = Primitives.Mix(Primitives.Scale(c2, 0.85), new[] { 0.42, 0.55, 0.35 }, 0.50);
        var petioleCol = Primitives.Mix(stemCol, c1, 0.40);

        // Helper to build a 3-lobed ivy leaf (central acute lobe + 2 lateral spreading lobes)
        void IvyLeaf(Vec3 petioleEnd, Vec3 fwd, Vec3 norm, double scale)
        {
            var side = fwd.Cross(norm).Normalized();
            if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);

            double mainLen = scale * rng.Range(0.070, 0.095);
            double mainHalfW = mainLen * 0.38;

            int bladeStart = m.VertexCount;
            // Central acute lobe
            FoliageBlade.Build(m, petioleEnd, petioleEnd + fwd * mainLen, side, mainHalfW,
                Primitives.Scale(c1, 0.82), c1, camber: 0.14, curl: 0.10, shoulder: 0.72, segments: 3);

            // Two spreading lateral acute lobes (Hedera signature)
            foreach (double sign in new[] { -1.0, 1.0 })
            {
                double latAng = sign * 0.72 + rng.Range(-0.06, 0.06);
                var latDir = Axis.RotateAround(fwd, norm, latAng);
                var latSide = latDir.Cross(norm).Normalized();
                double latLen = mainLen * 0.78;
                FoliageBlade.Build(m, petioleEnd, petioleEnd + latDir * latLen, latSide, mainHalfW * 0.80,
                    Primitives.Scale(c1, 0.80), c1, camber: 0.12, curl: 0.08, shoulder: 0.70, segments: 3);
            }

            // Recolor leaf undersides
            for (int v = bladeStart; v < m.VertexCount; v++)
            {
                if (m.UV2[v * 2] > 0.5f)
                    m.SetColor(v, leafUnderCol[0], leafUnderCol[1], leafUnderCol[2], 1.0);
            }
        }

        if (juvenile)
        {
            // Ground runner form: 5 prostrate creeping stolons running along litter
            int stolons = 5;
            for (int s = 0; s < stolons; s++)
            {
                double sAng = s * (Math.PI * 2 / stolons) + rng.Range(-0.2, 0.2);
                var sDir = new Vec3(Math.Cos(sAng), 0, Math.Sin(sAng));
                var sSide = sDir.Cross(Vec3.Up);

                int nodes = 5;
                var currentPos = new Vec3(0, 0.012, 0);
                double stemR = 0.007;

                for (int n = 0; n < nodes; n++)
                {
                    double dist = rng.Range(0.06, 0.09);
                    double sway = (n % 2 == 0 ? 1.0 : -1.0) * 0.015;
                    var nextPos = currentPos + sDir * dist + sSide * sway + new Vec3(0, rng.Range(-0.003, 0.003), 0);
                    nextPos = new Vec3(nextPos.X, Math.Max(0.006, nextPos.Y), nextPos.Z);

                    // Stem up to growth node
                    Wood(m, new[] { currentPos, nextPos }, new[] { stemR, stemR * 0.90 }, 6);

                    // Anchoring adventitious rootlets into soil
                    for (int r = 0; r < 4; r++)
                    {
                        var rOff = sSide * rng.Range(-0.006, 0.006);
                        Primitives.Tube(m, new[] { nextPos + rOff, nextPos + rOff - Vec3.Up * 0.014 },
                            new[] { 0.0016, 0.0008 }, 6, (i, v) => (rootletCol, 1.0, i, v, 0, 0));
                    }

                    // Leaves along stem
                    double leafSign = n % 2 == 0 ? 1.0 : -1.0;
                    var lDir = (sSide * (leafSign * 0.80) + sDir * 0.45 + Vec3.Up * 0.12).Normalized();
                    var petEnd = nextPos + lDir * 0.018;
                    Primitives.Tube(m, new[] { nextPos, petEnd }, new[] { 0.0024, 0.0018 }, 6, (i, v) => (petioleCol, 1.0, i, v, 0, 0));
                    IvyLeaf(petEnd, lDir, Vec3.Up, 0.70);

                    currentPos = nextPos;
                    stemR *= 0.90;
                }
            }
        }
        else
        {
            // Mature Climbing Form: Vertical bark-hugging ascent with shingled ivy tapestry
            double HostR(double y) => 0.205 - 0.020 * (y / 1.15);
            Vec3 HostSurface(double y, double th, double clearance = 0.008)
            {
                double r = HostR(y) + clearance;
                return new Vec3(r * Math.Sin(th), y, r * Math.Cos(th) - HostR(0));
            }
            Vec3 HostNormal(double th) => new Vec3(Math.Sin(th), 0, Math.Cos(th)).Normalized();

            // Function to grow a shoot from start to a sequence of growth nodes
            void GrowShoot(double startY, double startTh, int maxNodes, int order, double startRadius)
            {
                double y = startY;
                double th = startTh;
                double curR = startRadius;

                for (int node = 0; node < maxNodes; node++)
                {
                    double stepY = rng.Range(0.07, 0.11);
                    double stepTh = (order == 0 ? rng.Range(-0.06, 0.06) : rng.Range(0.10, 0.20) * (startTh >= 0 ? 1.0 : -1.0));
                    double nextY = Math.Min(1.02, y + stepY);
                    double nextTh = th + stepTh;

                    var p0 = HostSurface(y, th, 0.007);
                    var p1 = HostSurface(nextY, nextTh, 0.007);
                    var mid = HostSurface((y + nextY) * 0.5, (th + nextTh) * 0.5, 0.007);
                    var norm = HostNormal((th + nextTh) * 0.5);
                    var fwd = (p1 - p0).Normalized();
                    var side = fwd.Cross(norm).Normalized();

                    // 1. Stem up to growth node (hugging host contour)
                    Wood(m, new[] { p0, mid, p1 }, new[] { curR, curR * 0.94, curR * 0.88 }, 6);

                    // Under-stem adventitious rootlet pads anchoring into host
                    for (int r = 0; r < 5; r++)
                    {
                        double rt = 0.2 + 0.6 * r / 4.0;
                        var rAttach = Curve(p0, mid, p1, rt);
                        var rTip = rAttach - norm * 0.014 + side * rng.Range(-0.008, 0.008);
                        Primitives.Tube(m, new[] { rAttach, rTip }, new[] { 0.0020, 0.0010 }, 6,
                            (i, v) => (rootletCol, 1.0, i, v, 0, 0));
                    }

                    // 2. Leaves along stem (alternate distichous, shingled flat against host)
                    for (int l = 0; l < 2; l++)
                    {
                        double lt = 0.30 + l * 0.42;
                        var lAttach = Curve(p0, mid, p1, lt);
                        double lSign = (node * 2 + l) % 2 == 0 ? 1.0 : -1.0;

                        var lDir = (side * (lSign * 0.78) + fwd * 0.55 + norm * 0.12).Normalized();
                        var petEnd = lAttach + lDir * 0.024 + norm * 0.004;
                        Primitives.Tube(m, new[] { lAttach, petEnd }, new[] { 0.0028, 0.0020 }, 6,
                            (i, v) => (petioleCol, 1.0, i, v, 0, 0));

                        IvyLeaf(petEnd, (fwd * 0.65 + lDir * 0.35).Normalized(), norm, 1.05);
                    }

                    // Leaf directly at growth node
                    {
                        double lSign = (node % 2 == 0 ? -1.0 : 1.0);
                        var lDir = (side * (lSign * 0.85) + fwd * 0.40 + norm * 0.15).Normalized();
                        var petEnd = p1 + lDir * 0.022 + norm * 0.004;
                        Primitives.Tube(m, new[] { p1, petEnd }, new[] { 0.0026, 0.0018 }, 6,
                            (i, v) => (petioleCol, 1.0, i, v, 0, 0));
                        IvyLeaf(petEnd, (fwd * 0.60 + lDir * 0.40).Normalized(), norm, 0.95);
                    }

                    // 3. From growth node, send out new lateral shoot
                    if (order == 0 && (node == 1 || node == 2 || node == 4 || node == 6))
                    {
                        double branchSide = node == 1 ? 1.0 : node == 2 ? -1.0 : node == 4 ? 1.0 : -1.0;
                        GrowShoot(nextY, nextTh + branchSide * 0.22, 4, 1, curR * 0.75);
                    }

                    y = nextY;
                    th = nextTh;
                    curR *= 0.90;
                    if (y >= 1.0) break;
                }
            }

            // Primary ascending leader (central)
            GrowShoot(0.02, 0.0, 10, 0, 0.015);
            // Flanking secondary leaders
            GrowShoot(0.03, -0.32, 9, 1, 0.012);
            GrowShoot(0.03, 0.32, 9, 1, 0.012);
            // Outer wrapping runners
            GrowShoot(0.05, -0.65, 7, 1, 0.010);
            GrowShoot(0.05, 0.65, 7, 1, 0.010);
        }
    }

    private static void Spiralvine(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        var stemCol = Primitives.Mix(WoodyBarkLight, c1, 0.32);
        var collarCol = Primitives.Scale(stemCol, 0.85);
        var petioleCol = Primitives.Mix(stemCol, c2, 0.40);
        var leafUnderCol = Primitives.Mix(Primitives.Scale(c2, 0.88), new[] { 0.45, 0.62, 0.38 }, 0.45);

        // Helper to build an opposite pair of lanceolate leaves
        void PairedLeaves(Vec3 stemNode, Vec3 fwd, Vec3 norm, double scale, double ageT)
        {
            var side = fwd.Cross(norm).Normalized();
            if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);

            // Mature dark green at bottom to luminous fresh lime-green at top
            var bladeCol = Primitives.Mix(c1, c2, 0.25 + 0.65 * ageT);
            double bladeLen = scale * rng.Range(0.095, 0.125);
            double halfW = bladeLen * 0.28;

            foreach (double sign in new[] { -1.0, 1.0 })
            {
                var lDir = (side * (sign * 0.82) + norm * 0.48 + Vec3.Up * 0.26).Normalized();
                var lSide = lDir.Cross(Vec3.Up).Normalized();
                var petEnd = stemNode + lDir * (bladeLen * 0.20);

                Primitives.Tube(m, new[] { stemNode, petEnd }, new[] { 0.0028, 0.0020 }, 6,
                    (i, v) => (petioleCol, 1.0, i, v, 0, 0));

                int bladeStart = m.VertexCount;
                FoliageBlade.Build(m, petEnd, petEnd + lDir * bladeLen, lSide, halfW,
                    Primitives.Scale(bladeCol, 0.80), bladeCol,
                    camber: 0.16, curl: 0.18, shoulder: 0.78, segments: 4);

                for (int v = bladeStart; v < m.VertexCount; v++)
                {
                    if (m.UV2[v * 2] > 0.5f)
                        m.SetColor(v, leafUnderCol[0], leafUnderCol[1], leafUnderCol[2], 1.0);
                }
            }
        }

        if (juvenile)
        {
            // Ground runner form: broad searching undulating S-curves across open soil
            const int segs = 10;
            var path = new Vec3[segs + 1];
            var radii = new double[segs + 1];
            double sAng = rng.Range(0, Math.PI * 2);
            var fwd = new Vec3(Math.Cos(sAng), 0, Math.Sin(sAng));
            var side = fwd.Cross(Vec3.Up);

            for (int i = 0; i <= segs; i++)
            {
                double t = (double)i / segs;
                double reach = t * 0.48;
                double wave = Math.Sin(t * Math.PI * 2.0) * 0.06;
                path[i] = fwd * reach + side * wave + new Vec3(0, 0.015 + 0.008 * Math.Sin(t * Math.PI), 0);
                radii[i] = 0.008 * (1.0 - 0.55 * t);
            }
            Wood(m, path, radii, 6);

            for (int i = 1; i < segs; i++)
            {
                var dir = (path[i + 1] - path[i - 1]).Normalized();
                PairedLeaves(path[i], dir, Vec3.Up, 0.75, (double)i / segs);
            }
        }
        else
        {
            // Mature Climbing Form: Twin Intertwined 3D Helical Lianas
            double HostR(double y) => 0.205 - 0.020 * (y / 1.15);
            Vec3 HostSurface(double y, double th, double clearance = 0.009)
            {
                double r = HostR(y) + clearance;
                return new Vec3(r * Math.Sin(th), y, r * Math.Cos(th) - HostR(0));
            }
            Vec3 HostNormal(double th) => new Vec3(Math.Sin(th), 0, Math.Cos(th)).Normalized();

            // Grow a helical vine strand
            void GrowHelix(double startTh, double totalTurns, double startY, double endY, int nodes, double stemR, bool allowWhips)
            {
                var nodePositions = new Vec3[nodes + 1];
                var nodeNormals = new Vec3[nodes + 1];

                for (int i = 0; i <= nodes; i++)
                {
                    double t = (double)i / nodes;
                    double y = startY + t * (endY - startY);
                    double th = startTh + t * (totalTurns * Math.PI * 2);
                    nodePositions[i] = HostSurface(y, th);
                    nodeNormals[i] = HostNormal(th);
                }

                for (int i = 0; i < nodes; i++)
                {
                    var p0 = nodePositions[i];
                    var p1 = nodePositions[i + 1];
                    var n1 = nodeNormals[i + 1];

                    // 4-point helical arc between nodes
                    var arcPts = new Vec3[4];
                    var arcRadii = new double[4];
                    for (int pt = 0; pt < 4; pt++)
                    {
                        double lt = (double)pt / 3.0;
                        double gt = (i + lt) / nodes;
                        double y = startY + gt * (endY - startY);
                        double th = startTh + gt * (totalTurns * Math.PI * 2);
                        arcPts[pt] = HostSurface(y, th);
                        arcRadii[pt] = stemR * (1.0 - gt * 0.45);
                    }

                    // 1. Stem up to growth node (helical cord)
                    Wood(m, arcPts, arcRadii, 6);

                    // Swollen annular collar at growth node
                    Primitives.Ellipsoid(m, p1, new Vec3(arcRadii[3] * 1.55, 0.012, arcRadii[3] * 1.55), 4, 6,
                        (u, v) => (collarCol, 1.0, u, v, 0, 0));

                    // 2. Leaves along stem: 2 pairs of opposite leaves per segment!
                    for (int lp = 0; lp < 2; lp++)
                    {
                        int ptIdx = lp == 0 ? 1 : 2;
                        var fwd = (arcPts[ptIdx + 1] - arcPts[ptIdx - 1]).Normalized();
                        double segT = (i + (lp + 1) / 3.0) / nodes;
                        var midNorm = HostNormal(startTh + segT * (totalTurns * Math.PI * 2));
                        PairedLeaves(arcPts[ptIdx], fwd, midNorm, 1.05, segT);
                    }

                    // Paired leaves directly at growth node
                    {
                        var fwd1 = (p1 - p0).Normalized();
                        PairedLeaves(p1, fwd1, n1, 0.95, (double)(i + 1) / nodes);
                    }

                    // 3. From growth node, send out new shoots:
                    // Mid-height secondary twining loop
                    if (i == 3 || i == 5)
                    {
                        var secTh = startTh + (i / (double)nodes) * (totalTurns * Math.PI * 2);
                        var secPath = new Vec3[4];
                        var secRadii = new double[4];
                        for (int sp = 0; sp < 4; sp++)
                        {
                            double st = sp / 3.0;
                            double sy = p1.Y + st * 0.16;
                            double sth = secTh + st * 0.95;
                            secPath[sp] = HostSurface(sy, sth);
                            secRadii[sp] = arcRadii[3] * (0.75 - st * 0.30);
                        }
                        Wood(m, secPath, secRadii, 6);
                        PairedLeaves(secPath[1], (secPath[2] - secPath[0]).Normalized(), HostNormal(secTh + 0.3), 0.90, 0.6);
                        PairedLeaves(secPath[2], (secPath[3] - secPath[1]).Normalized(), HostNormal(secTh + 0.6), 0.85, 0.7);
                    }

                    // Upper crown searching tendril whips
                    if (allowWhips && i >= nodes - 2)
                    {
                        var fwd = (p1 - p0).Normalized();
                        var whipStart = p1;
                        var whipDir = (n1 * 0.65 + Vec3.Up * 0.72 + fwd * 0.25).Normalized();
                        var whipTip = whipStart + whipDir * 0.22 + new Vec3(rng.Range(-0.04, 0.04), 0, rng.Range(-0.04, 0.04));
                        var whipCtrl = (whipStart + whipTip) * 0.5 + Vec3.Up * 0.04;
                        WoodyCurve(m, whipStart, whipCtrl, whipTip, 0.005, 3);
                        PairedLeaves(whipTip, whipDir, Vec3.Up, 0.80, 1.0);
                    }
                }
            }

            // Twin intertwined helical lianas
            double baseTh = rng.Range(-0.4, 0.4);
            // Strand 1: Primary helix
            GrowHelix(baseTh, 1.55, 0.02, 0.98, 9, 0.015, true);
            // Strand 2: Companion intertwined helix, offset by ~75 degrees (0.42 rad) and twining in tandem
            GrowHelix(baseTh + 0.75, 1.45, 0.05, 0.95, 8, 0.012, true);
        }
    }

    private static void Fenhook(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        var stemCol = Primitives.Mix(c1, c2, 0.42);
        var hookCol = Primitives.Mix(stemCol, new[] { 0.85, 0.88, 0.72 }, 0.50);
        var leafUnderCol = Primitives.Mix(Primitives.Scale(c2, 0.85), new[] { 0.50, 0.65, 0.40 }, 0.40);

        // Herbaceous 4-angled stem tube (UV2.x = 0 so it stays natural succulent green, not bark)
        void FenTube(IReadOnlyList<Vec3> path, IReadOnlyList<double> radii)
        {
            Primitives.Tube(m, path, radii, 6, (i, v) => (stemCol, 1.0, i / (double)(path.Count - 1), v, 0.0, 0.0));
        }

        // Helper to build a starry whorl of 7-8 narrow oblanceolate leaves collaring a node
        void StarryWhorl(Vec3 nodePos, Vec3 stemDir, Vec3 upHint, double scale, double ageT)
        {
            var fwd = stemDir.Normalized();
            var side = fwd.Cross(upHint).Normalized();
            if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);
            var norm = side.Cross(fwd).Normalized();

            int leaves = 8;
            var bladeCol = Primitives.Mix(c1, c2, 0.30 + 0.50 * ageT);
            double bladeLen = scale * rng.Range(0.085, 0.115);
            double halfW = bladeLen * 0.20;

            for (int w = 0; w < leaves; w++)
            {
                double a = w * (Math.PI * 2 / leaves) + rng.Range(-0.05, 0.05);
                var lDir = (side * Math.Cos(a) + norm * Math.Sin(a) * 0.82 + fwd * 0.18).Normalized();
                var lSide = lDir.Cross(fwd).Normalized();

                int bladeStart = m.VertexCount;
                FoliageBlade.Build(m, nodePos, nodePos + lDir * bladeLen, lSide, halfW,
                    Primitives.Scale(bladeCol, 0.78), bladeCol,
                    camber: 0.12, curl: 0.14, shoulder: 0.82, segments: 3);

                for (int v = bladeStart; v < m.VertexCount; v++)
                {
                    if (m.UV2[v * 2] > 0.5f)
                        m.SetColor(v, leafUnderCol[0], leafUnderCol[1], leafUnderCol[2], 1.0);
                }
            }
        }

        // Helper to add recurved hooked prickles along stem
        void StemHooks(Vec3 p0, Vec3 p1, Vec3 fwd, Vec3 norm, double r)
        {
            var side = fwd.Cross(norm).Normalized();
            for (int h = 1; h <= 4; h++)
            {
                double ht = h / 5.0;
                var hPos = Vec3.Lerp(p0, p1, ht);
                double sign = (h % 2 == 0 ? 1.0 : -1.0);
                var hookDir = (side * sign * 0.70 - fwd * 0.70).Normalized();
                Primitives.Tube(m, new[] { hPos, hPos + hookDir * 0.005 },
                    new[] { 0.0016, 0.0006 }, 6, (i, v) => (hookCol, 1.0, i, v, 0, 0));
            }
        }

        if (juvenile)
        {
            // Ground runner form: sprawling loose tangled mat of 4-angled stems and starry whorls
            int stems = 5;
            for (int s = 0; s < stems; s++)
            {
                double a = s * (Math.PI * 2 / stems) + rng.Range(-0.25, 0.25);
                var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                var side = dir.Cross(Vec3.Up);

                var cur = new Vec3(0, 0.015, 0);
                double r = 0.007;

                for (int n = 0; n < 4; n++)
                {
                    double len = rng.Range(0.07, 0.10);
                    var next = cur + dir * len + side * rng.Range(-0.02, 0.02) + new Vec3(0, rng.Range(-0.003, 0.003), 0);
                    next = new Vec3(next.X, Math.Max(0.008, next.Y), next.Z);

                    var fwd = (next - cur).Normalized();
                    FenTube(new[] { cur, next }, new[] { r, r * 0.88 });
                    StemHooks(cur, next, fwd, Vec3.Up, r);
                    StarryWhorl(next, fwd, Vec3.Up, 0.75, (double)n / 4);

                    cur = next;
                    r *= 0.88;
                }
            }
        }
        else
        {
            // Mature Climbing & Scrambling Form: Clambering framework with cascading hanging curtains
            double HostR(double y) => 0.205 - 0.020 * (y / 1.15);
            Vec3 HostSurface(double y, double th, double clearance = 0.010)
            {
                double r = HostR(y) + clearance;
                return new Vec3(r * Math.Sin(th), y, r * Math.Cos(th) - HostR(0));
            }
            Vec3 HostNormal(double th) => new Vec3(Math.Sin(th), 0, Math.Cos(th)).Normalized();

            // Function to grow a scrambling shoot with clambering spans and pendulous swags
            void GrowScrambler(double startY, double startTh, int spanCount, double baseReach, double curR, double angSign)
            {
                var cur = HostSurface(startY, startTh);
                double th = startTh;
                double y = startY;

                for (int i = 0; i < spanCount; i++)
                {
                    double stepY = rng.Range(0.12, 0.16);
                    double stepTh = angSign * rng.Range(0.28, 0.45);
                    double nextY = Math.Min(1.00, y + stepY);
                    double nextTh = th + stepTh;

                    var next = HostSurface(nextY, nextTh);
                    var midSurf = HostSurface((y + nextY) * 0.5, (th + nextTh) * 0.5);
                    var norm = HostNormal((th + nextTh) * 0.5);

                    // Gravity sag between contact points
                    var sag = midSurf - Vec3.Up * rng.Range(0.016, 0.028) + norm * 0.005;

                    var fwd = (next - cur).Normalized();

                    // 1. Stem up to growth node (sagging span)
                    FenTube(new[] { cur, sag, next }, new[] { curR, curR * 0.94, curR * 0.88 });
                    StemHooks(cur, sag, (sag - cur).Normalized(), norm, curR);
                    StemHooks(sag, next, (next - sag).Normalized(), norm, curR * 0.9);

                    // 2. Leaves along stem: starry whorl at the midpoint sag and at the node
                    StarryWhorl(sag, (next - cur).Normalized(), norm, 0.95, (double)i / spanCount);
                    StarryWhorl(next, fwd, norm, 1.0, (double)i / spanCount);

                    // 3. From growth node, send out cascading hanging curtains (pendulous swags)
                    if (i >= 1)
                    {
                        // Shoot arches over limb/bark and falls downward under gravity!
                        var loopStart = next;
                        var outDir = (norm * 0.70 + (nextTh > th ? 1.0 : -1.0) * fwd.Cross(norm) * 0.50).Normalized();
                        double loopDrop = rng.Range(0.22, 0.32);

                        // Looped swag with 5 points
                        var pTop = loopStart + outDir * 0.035 + Vec3.Up * 0.015;
                        var pMid = loopStart + outDir * 0.055 - Vec3.Up * (loopDrop * 0.50);
                        var pBottom = loopStart + outDir * 0.030 - Vec3.Up * loopDrop;
                        var pCurl = pBottom + new Vec3(rng.Range(-0.02, 0.02), rng.Range(0.04, 0.08), rng.Range(-0.02, 0.02));

                        var drapePts = new[] { loopStart, pTop, pMid, pBottom, pCurl };
                        var drapeRadii = new[] { curR * 0.75, curR * 0.65, curR * 0.55, curR * 0.45, curR * 0.35 };
                        FenTube(drapePts, drapeRadii);

                        // Hooks along draped curtain
                        StemHooks(pTop, pMid, (pMid - pTop).Normalized(), outDir, curR * 0.6);
                        StemHooks(pMid, pBottom, (pBottom - pMid).Normalized(), outDir, curR * 0.5);

                        // Starry whorls along hanging curtain
                        StarryWhorl(pTop, (pMid - loopStart).Normalized(), outDir, 0.90, 0.7);
                        StarryWhorl(pMid, -Vec3.Up, outDir, 0.85, 0.8);
                        StarryWhorl(pBottom, (pCurl - pBottom).Normalized(), outDir, 0.80, 0.9);
                        StarryWhorl(pCurl, Vec3.Up, outDir, 0.70, 1.0);
                    }

                    cur = next;
                    y = nextY;
                    th = nextTh;
                    curR *= 0.90;
                    if (y >= 0.98) break;
                }
            }

            // Two main clambering framework shoots ascending both sides
            GrowScrambler(0.02, -0.25, 7, 0.16, 0.014, 1.0);  // Climbs winding right
            GrowScrambler(0.04, 0.35, 7, 0.16, 0.013, -1.0);  // Climbs winding left
        }
    }
    /// <summary>
    /// Dewbonnet: Decomposer troop (Mycena galericulata &amp; Coprinellus disseminatus) of humid forest litter.
    /// - Multi-age fairy troop: young ovate bullet buttons, prime campanulate bonnets with acute central umbos,
    ///   and flared mature parasols with scalloped brims.
    /// - Radial sulcate striations (fluted ribs) running from umbo to cap margins.
    /// - Dense underside gills with alternating lamellae (UV2.x = 1.0 triggering shader gill pattern).
    /// - Fine flexuous translucent fibrous stipes with slight basal swellings.
    /// - Substrate mycelial felt pad and radiating hyphal cords anchoring the colony into organic litter.
    /// - Authored in unit-normalized bounds [0..1] for height and radius.
    /// </summary>
    private static void Mushrooms(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        var stemCol = new[] { 0.88, 0.85, 0.79 };
        var stemBaseCol = new[] { 0.65, 0.58, 0.50 };
        var myceliumCol = new[] { 0.92, 0.90, 0.84 };
        double scale = juvenile ? 0.60 : 1.0;

        // 1. Basal mycelial felt pad &amp; radiating hyphal cords anchoring the troop into substrate
        int padPoints = 16;
        for (int cluster = 0; cluster < (juvenile ? 2 : 3); cluster++)
        {
            double clAng = cluster * (Math.PI * 2 / 3) + rng.Range(-0.35, 0.35);
            double clDist = rng.Range(0.10, 0.25) * scale;
            var clCenter = new Vec3(Math.Cos(clAng) * clDist, 0.015, Math.Sin(clAng) * clDist);
            double padR = rng.Range(0.38, 0.60) * scale;

            var rim = new List<Vec3>(padPoints + 1);
            for (int s = 0; s <= padPoints; s++)
            {
                double th = 2 * Math.PI * s / padPoints;
                double r = padR * (0.75 + 0.35 * Math.Sin(th * 3 + cluster) + 0.15 * Math.Cos(th * 5));
                rim.Add(clCenter + new Vec3(Math.Cos(th) * r, 0, Math.Sin(th) * r));
            }
            Primitives.Fan(m, clCenter, rim, Vec3.Up, myceliumCol, Primitives.Scale(c1, 0.75), 1.0, 0.0);
        }

        // 2. Troop of multi-age fruiting bodies (densely packed 34-42 bonnets)
        int n = juvenile ? (16 + rng.NextInt(6)) : (36 + rng.NextInt(8));
        for (int k = 0; k < n; k++)
        {
            int clusterIdx = k % 4;
            double clAng = clusterIdx * (Math.PI * 2 / 4);
            var clCenter = new Vec3(Math.Cos(clAng) * (0.22 * scale), 0, Math.Sin(clAng) * (0.22 * scale));
            double rLocal = Math.Sqrt(rng.NextDouble()) * (0.45 * scale);
            double aLocal = rng.Range(0, 2 * Math.PI);
            var b = clCenter + new Vec3(Math.Cos(aLocal) * rLocal, 0, Math.Sin(aLocal) * rLocal);

            // Age stage: 0 = bullet button (~22%), 1 = prime campanulate bonnet (~53%), 2 = flared parasol (~25%)
            int stage;
            double p = rng.NextDouble();
            if (p < 0.22) stage = 0;
            else if (p < 0.75) stage = 1;
            else stage = 2;

            double h = (stage == 0 ? rng.Range(0.25, 0.42) :
                        stage == 1 ? rng.Range(0.52, 0.92) :
                                     rng.Range(0.60, 1.00)) * scale;

            double capR = (stage == 0 ? rng.Range(0.08, 0.13) :
                           stage == 1 ? rng.Range(0.18, 0.27) :
                                        rng.Range(0.26, 0.38)) * scale;

            var outward = b.LengthSq > 1e-4 ? b.Normalized() : new Vec3(1, 0, 0);
            var lean = outward * rng.Range(0.06, 0.18) + new Vec3(rng.Range(-0.06, 0.06), 0, rng.Range(-0.06, 0.06));
            var top = b + lean + new Vec3(0, h, 0);
            var mid = b + lean * 0.45 + new Vec3(rng.Range(-0.03, 0.03), h * 0.52, rng.Range(-0.03, 0.03));
            var lower = b + lean * 0.15 + new Vec3(0, h * 0.18, 0);

            double baseRad = rng.Range(0.028, 0.040) * scale;
            double midRad = rng.Range(0.018, 0.025) * scale;
            double topRad = rng.Range(0.014, 0.018) * scale;

            Primitives.Tube(m, new[] { b, lower, mid, top },
                new[] { baseRad, midRad * 1.15, midRad, topRad }, 6,
                (i, v) => (Primitives.Mix(stemBaseCol, stemCol, Math.Min(1.0, i / 2.0)), 1.0, i / 3.0, v, 0, 0));

            // Cap geometry with conical umbo and radial sulcate fluting
            int start = m.VertexCount;
            const int around = 24, rings = 5;
            double phase = rng.Range(0, 2 * Math.PI);
            double asym = rng.Range(0.04, 0.09);

            Vec3 CapPoint(double f, int s)
            {
                double th = 2 * Math.PI * s / around;
                double wave = Math.Cos(th * 12 + phase) * (0.055 * f * f);
                double width = capR * f * (1.0 + asym * Math.Cos(th - phase) + wave);

                double umbo = (stage == 1 ? 0.32 : stage == 2 ? 0.22 : 0.14) * capR * Math.Exp(-18.0 * f * f);
                double y;
                if (stage == 0)
                {
                    y = capR * 1.45 * (1.0 - f * f * 0.85) + umbo;
                }
                else if (stage == 1)
                {
                    y = capR * (0.82 * Math.Pow(1.0 - f, 0.72) - 0.12 * Math.Pow(f, 3.5)) + umbo;
                }
                else
                {
                    double brimWave = Math.Sin(th * 6 + phase) * (capR * 0.08 * Math.Pow(f, 3.0));
                    y = capR * (0.38 * (1.0 - f) + 0.16 * Math.Pow(f, 4.0)) + umbo + brimWave;
                }
                return top + new Vec3(Math.Cos(th) * width, y, Math.Sin(th) * width);
            }

            for (int ri = 0; ri <= rings; ri++)
            {
                double t = (double)ri / rings;
                double f = 0.04 + t * 0.96;
                var col = Primitives.Mix(c2, c1, Math.Min(1.0, Math.Pow(t, 0.75)));
                for (int s = 0; s <= around; s++)
                {
                    var pos = CapPoint(f, s);
                    m.AddVertex(pos, Vec3.Up, col, 1.0, (double)s / around, t, 0, 0);
                }
            }

            for (int ri = 0; ri < rings; ri++)
            {
                for (int s = 0; s < around; s++)
                {
                    int a = start + ri * (around + 1) + s;
                    int c = a + around + 1;
                    Primitives.TriangleFacing(m, a, c, a + 1, Vec3.Up);
                    Primitives.TriangleFacing(m, a + 1, c, c + 1, Vec3.Up);
                }
            }

            // Underside gills
            int under = m.VertexCount;
            for (int row = 0; row < 2; row++)
            {
                double f = row == 0 ? 0.12 : 1.0;
                for (int s = 0; s <= around; s++)
                {
                    var gPos = CapPoint(f, s) + new Vec3(0, -capR * 0.030, 0);
                    var gillCol = Primitives.Mix(c1, c2, row == 0 ? 0.55 : (s % 2 == 0 ? 0.15 : 0.40));
                    m.AddVertex(gPos, -Vec3.Up, gillCol, 1.0, (double)s / around, row, 1.0, 0.0);
                }
            }

            for (int s = 0; s < around; s++)
            {
                int a = under + s;
                int c = a + around + 1;
                Primitives.TriangleFacing(m, a, c, a + 1, -Vec3.Up);
                Primitives.TriangleFacing(m, a + 1, c, c + 1, -Vec3.Up);
            }
        }
    }

    /// <summary>Tiers of leathery bracket shelves with rich ember-rust concentric zonation (turkey tail / cinnabar polypore),
    /// flared foot adhering to host bark at X=0, and pale cream microporous underside.</summary>
    private static void Bracket(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        // Ember-rust concentric zonation palette
        var charredUmber = new[] { 0.22, 0.12, 0.06 };
        var emberRust = c1;
        var fieryTerracotta = Primitives.Mix(c1, new[] { 0.88, 0.28, 0.08 }, 0.65);
        var warmOchre = Primitives.Mix(c1, c2, 0.45);
        var siennaVelvet = Primitives.Scale(c1, 0.70);
        var brightEmber = Primitives.Mix(c1, c2, 0.78);
        var creamMargin = c2;
        var ivoryRim = Primitives.Scale(c2, 1.06);

        double[][] bands = [charredUmber, emberRust, fieryTerracotta, warmOchre, siennaVelvet, brightEmber, creamMargin, ivoryRim];

        var poreBase = Primitives.Mix(c2, new[] { 0.94, 0.90, 0.82 }, 0.40);
        var poreCenter = Primitives.Scale(poreBase, 0.88);

        int tiers = 4 + rng.NextInt(3);
        const int rings = 8, around = 22;

        for (int k = 0; k < tiers; k++)
        {
            double tTier = tiers > 1 ? (double)k / (tiers - 1) : 0.5;
            double y = 0.16 + tTier * 0.64 + rng.Range(-0.025, 0.025);
            double reach = rng.Range(0.68, 0.94) * (0.86 + 0.14 * Math.Sin(Math.PI * (k + 0.5) / tiers));
            double spread = reach * rng.Range(0.95, 1.25);
            double off = (k % 2 == 0 ? -1.0 : 1.0) * rng.Range(0.04, 0.18) + rng.Range(-0.03, 0.03);
            double tilt = rng.Range(-0.03, 0.03);
            double cup = rng.Range(0.025, 0.055);
            double baseThick = rng.Range(0.032, 0.044);
            double edgeThick = rng.Range(0.005, 0.008);

            double phaseRuffle = rng.Range(0, 2 * Math.PI);
            double phaseCren = rng.Range(0, 2 * Math.PI);

            int topStart = m.VertexCount;

            // 1. Top shelf surface with ruffled crenulated margins and concentric ember zonation
            for (int ri = 0; ri <= rings; ri++)
            {
                double u = (double)ri / rings;
                for (int s = 0; s <= around; s++)
                {
                    double angle = (double)s / around * Math.PI;
                    double sinA = Math.Sin(angle);
                    double cosA = -Math.Cos(angle);

                    double edgeFactor = Math.Pow(u, 1.6) * sinA;
                    double cren = (0.070 * Math.Sin(8.0 * angle + phaseCren) + 0.035 * Math.Cos(14.0 * angle - phaseCren) + 0.018 * Math.Sin(22.0 * angle)) * edgeFactor;
                    double wav = 1.0 + cren;
                    double wavZ = 1.0 + cren * 0.75;

                    double x = reach * u * sinA * wav;
                    if (s == 0 || s == around || ri == 0) x = 0.0;

                    double currentSpread = spread * (0.60 + 0.40 * u);
                    double z = off + currentSpread * cosA * wavZ;

                    // Flared bracket foot at host attachment (u close to 0)
                    if (u < 0.25)
                    {
                        double footFlare = (1.0 - u / 0.25);
                        z += Math.Sign(cosA) * footFlare * 0.06;
                    }

                    double cupDroop = cup * u * u - 0.045 * u * Math.Pow(sinA, 1.4) + tilt * cosA * u;
                    double ruffleWave = 0.045 * Math.Sin(9.0 * angle + phaseRuffle) + 0.025 * Math.Sin(17.0 * angle - phaseRuffle);
                    double ruffle = ruffleWave * edgeFactor;

                    double yTop = y + cupDroop + ruffle;
                    var posTop = new Vec3(x, yTop, z);

                    var normal = new Vec3(
                        -0.25 * sinA,
                        1.0 - 0.35 * Math.Pow(u, 2),
                        -0.20 * cosA
                    ).Normalized();

                    // Concentric band tinting with subtle velvety micro-shading
                    double bandT = (u * (bands.Length - 1));
                    int b0 = Math.Min(bands.Length - 1, (int)bandT);
                    int b1 = Math.Min(bands.Length - 1, b0 + 1);
                    double bf = bandT - b0;
                    var col = Primitives.Mix(bands[b0], bands[b1], bf);
                    if (u > 0.94) col = ivoryRim;

                    m.AddVertex(posTop, normal, col, 1.0, u, (double)s / around, 0, 0);
                }
            }

            int underStart = m.VertexCount;

            // 2. Underside surface: cream microporous hymenium with sterile pale margin
            for (int ri = 0; ri <= rings; ri++)
            {
                double u = (double)ri / rings;
                double thk = MathD.Lerp(baseThick, edgeThick, Math.Pow(u, 0.75));
                var underCol = (u > 0.92) ? creamMargin : (u < 0.25) ? poreCenter : poreBase;

                for (int s = 0; s <= around; s++)
                {
                    double angle = (double)s / around * Math.PI;
                    double sinA = Math.Sin(angle);
                    double cosA = -Math.Cos(angle);

                    double edgeFactor = Math.Pow(u, 1.6) * sinA;
                    double cren = (0.070 * Math.Sin(8.0 * angle + phaseCren) + 0.035 * Math.Cos(14.0 * angle - phaseCren) + 0.018 * Math.Sin(22.0 * angle)) * edgeFactor;
                    double wav = 1.0 + cren;
                    double wavZ = 1.0 + cren * 0.75;

                    double x = reach * u * sinA * wav;
                    if (s == 0 || s == around || ri == 0) x = 0.0;

                    double currentSpread = spread * (0.60 + 0.40 * u);
                    double z = off + currentSpread * cosA * wavZ;

                    if (u < 0.25)
                    {
                        double footFlare = (1.0 - u / 0.25);
                        z += Math.Sign(cosA) * footFlare * 0.06;
                    }

                    double cupDroop = cup * u * u - 0.045 * u * Math.Pow(sinA, 1.4) + tilt * cosA * u;
                    double ruffleWave = 0.045 * Math.Sin(9.0 * angle + phaseRuffle) + 0.025 * Math.Sin(17.0 * angle - phaseRuffle);
                    double ruffle = ruffleWave * edgeFactor;

                    double yTop = y + cupDroop + ruffle;
                    var posBottom = new Vec3(x, yTop - thk, z);

                    m.AddVertex(posBottom, -Vec3.Up, underCol, 1.0, u, (double)s / around, 1, 0);
                }
            }

            // Top surface triangulation
            for (int ri = 0; ri < rings; ri++)
                for (int s = 0; s < around; s++)
                {
                    int a = topStart + ri * (around + 1) + s, c = a + around + 1;
                    Primitives.TriangleFacing(m, a, c, a + 1, Vec3.Up);
                    Primitives.TriangleFacing(m, a + 1, c, c + 1, Vec3.Up);
                }

            // Underside triangulation
            for (int ri = 0; ri < rings; ri++)
                for (int s = 0; s < around; s++)
                {
                    int a = underStart + ri * (around + 1) + s, c = a + around + 1;
                    Primitives.TriangleFacing(m, a, c, a + 1, -Vec3.Up);
                    Primitives.TriangleFacing(m, a + 1, c, c + 1, -Vec3.Up);
                }
        }
        m.RecomputeNormals();
    }

    /// <summary>
    /// A xerophytic succulent rosette colony (Sempervivum tectorum / Houseleek & Echeveria):
    /// - Dominant mother rosette encircled by radiating daughter offsets connected via short fleshy stolons.
    /// - Fibonacci phyllotaxis (137.5° golden angle) of thick, faceted succulent leaves with concave adaxial face,
    ///   sharp lateral margins, and pronounced dorsal keel tapering to an acute mucronate apex.
    /// - Color zoning: glaucous jade-green base with intense sun-stressed terracotta-crimson and bronze tips.
    /// - Mature specimens send up a fleshy bract-covered flowering thyrsus with starry coral-pink blossoms.
    /// </summary>
    private static void Succulent(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        var stolonColBase = new[] { 0.42, 0.36, 0.28 };
        var stolonColTip = Primitives.Mix(c1, new[] { 0.48, 0.60, 0.45 }, 0.3);

        if (juvenile)
        {
            // Juvenile form: single compact mother rosette with 1-2 small budding offsets
            double motherR = rng.Range(0.40, 0.52);
            BuildRosette(m, rng, seed, new Vec3(0, 0.10, 0), motherR, 20, c1, c2);

            int buds = 1 + rng.NextInt(2);
            for (int b = 0; b < buds; b++)
            {
                double bAng = b * Math.PI + rng.Range(-0.3, 0.3);
                double bDist = motherR * rng.Range(0.65, 0.85);
                var bCenter = new Vec3(Math.Cos(bAng) * bDist, 0.08, Math.Sin(bAng) * bDist);
                BuildRosette(m, rng, seed + (ulong)(b * 73 + 11), bCenter, motherR * 0.38, 12, c1, c2);
            }
            return;
        }

        // Mature colony: Mother rosette + 5..7 daughter offsets on radiating stolons
        var motherCenter = new Vec3(rng.Range(-0.05, 0.05), 0.12, rng.Range(-0.05, 0.05));
        double motherRadius = rng.Range(0.44, 0.54);
        BuildRosette(m, rng, seed, motherCenter, motherRadius, 28, c1, c2);

        int daughterCount = 5 + rng.NextInt(3); // 5..7 offsets
        for (int d = 0; d < daughterCount; d++)
        {
            double dAng = (double)d / daughterCount * Math.PI * 2.0 + rng.Range(-0.25, 0.25);
            double dDist = rng.Range(0.55, 0.88);
            var daughterCenter = motherCenter + new Vec3(Math.Cos(dAng) * dDist, 0.02, Math.Sin(dAng) * dDist);
            double daughterRadius = motherRadius * rng.Range(0.42, 0.65);

            // Stolon runner cord
            var stolonMid = (motherCenter + daughterCenter) * 0.5 + new Vec3(0, 0.03, 0);
            Primitives.Tube(m, new[] { motherCenter + new Vec3(0, 0.02, 0), stolonMid, daughterCenter + new Vec3(0, 0.015, 0) },
                new[] { 0.022, 0.018, 0.014 }, 3,
                (st, u) => (Primitives.Mix(stolonColBase, stolonColTip, st), 1.0, st, u, 0, 0));

            // Daughter rosette
            ulong dSeed = seed + (ulong)(d * 127 + 31);
            int dLeaves = 16 + rng.NextInt(5);
            BuildRosette(m, rng, dSeed, daughterCenter, daughterRadius, dLeaves, c1, c2);
        }

        // Mature flowering thyrsus / cyme (~65% chance on mature colony)
        if (seed % 3 != 0)
        {
            var stalkBase = motherCenter + new Vec3(rng.Range(-0.04, 0.04), 0.06, rng.Range(-0.04, 0.04));
            double stalkH = rng.Range(1.85, 2.50); // reaches ~14-17 cm in world space
            double leanX = rng.Range(-0.18, 0.18);
            double leanZ = rng.Range(-0.18, 0.18);
            var stalkMid = stalkBase + new Vec3(leanX * 0.4, stalkH * 0.52, leanZ * 0.4);
            var stalkTop = stalkBase + new Vec3(leanX, stalkH, leanZ);

            var stalkColBase = Primitives.Mix(c1, new[] { 0.35, 0.52, 0.40 }, 0.4);
            var stalkColTop = Primitives.Mix(c2, new[] { 0.82, 0.45, 0.38 }, 0.35);

            // Fleshy arching flower stalk
            Primitives.Tube(m, new[] { stalkBase, stalkMid, stalkTop }, new[] { 0.038, 0.026, 0.018 }, 4,
                (st, u) => (Primitives.Mix(stalkColBase, stalkColTop, st), 1.0, st, u, 0, 0));

            // Alternate fleshy clasping scale bracts along the stalk
            int bractCount = 7 + rng.NextInt(4);
            for (int b = 0; b < bractCount; b++)
            {
                double tBract = 0.20 + (double)b / bractCount * 0.65;
                var pBract = stalkBase * (1 - tBract) * (1 - tBract) + stalkMid * 2 * (1 - tBract) * tBract + stalkTop * tBract * tBract;
                double bAzimuth = b * 2.4 + seed * 0.1;
                var bDir = (new Vec3(Math.Cos(bAzimuth), 0.3, Math.Sin(bAzimuth))).Normalized();
                var bUp = new Vec3(0, 1, 0);
                double bLen = rng.Range(0.08, 0.13) * (1.1 - tBract * 0.35);
                SucculentLeaf(m, pBract, bDir, bUp, bLen, bLen * 0.22, bLen * 0.18,
                    stalkColBase, stalkColTop, stalkColTop);
            }

            // Terminal cyme head with 5..8 starry succulent flowers
            int flowerCount = 5 + rng.NextInt(4);
            var flowerPink = Primitives.Mix(c2, new[] { 0.95, 0.42, 0.50 }, 0.45);
            var flowerCream = new[] { 0.96, 0.94, 0.82 };
            var stamenGold = new[] { 0.95, 0.85, 0.25 };

            for (int f = 0; f < flowerCount; f++)
            {
                double fAng = (double)f / flowerCount * Math.PI * 2.0 + rng.Range(-0.25, 0.25);
                var fDir = (new Vec3(Math.Cos(fAng) * 0.85, 0.55, Math.Sin(fAng) * 0.85)).Normalized();
                var pedicelBase = stalkTop - new Vec3(0, (double)f / flowerCount * 0.18, 0);
                var flowerPos = pedicelBase + fDir * rng.Range(0.12, 0.22);

                // Pedicel wire
                Primitives.Tube(m, new[] { pedicelBase, flowerPos }, new[] { 0.008, 0.006 }, 3,
                    (st, u) => (stalkColTop, 1.0, st, u, 0, 0));

                // Starry flower (8-10 petals)
                int petals = 8 + rng.NextInt(3);
                var fUp = fDir.Cross(new Vec3(Math.Cos(fAng + 1.5), 0, Math.Sin(fAng + 1.5))).Normalized();
                if (fUp.LengthSq < 1e-4) fUp = Vec3.Up;
                var fSide = fDir.Cross(fUp).Normalized();

                double flowerRadius = rng.Range(0.07, 0.10);

                // Central stamen disc
                Primitives.Ellipsoid(m, flowerPos, new Vec3(0.016, 0.016, 0.016), 4, 6,
                    (u, v) => (stamenGold, 1.0, u, v, 0, 0));

                // Petals
                for (int p = 0; p < petals; p++)
                {
                    double pAng = (double)p / petals * Math.PI * 2.0;
                    var pRadial = (fSide * Math.Cos(pAng) + fUp * Math.Sin(pAng)).Normalized();
                    var pDir = (pRadial * 0.92 + fDir * 0.38).Normalized();
                    var pTip = flowerPos + pDir * flowerRadius;
                    var pMid = flowerPos + pDir * (flowerRadius * 0.50);
                    var pCross = pRadial.Cross(fDir).Normalized();

                    double pWid = flowerRadius * 0.24;
                    var vL = pMid - pCross * pWid;
                    var vR = pMid + pCross * pWid;
                    var nPetal = fDir;

                    int iBase = m.AddVertex(flowerPos, nPetal, flowerCream, 1.0, 0, 0.5, 0, 1);
                    int iL = m.AddVertex(vL, nPetal, flowerPink, 1.0, 0.5, 0, 0, 1);
                    int iR = m.AddVertex(vR, nPetal, flowerPink, 1.0, 0.5, 1, 0, 1);
                    int iTip = m.AddVertex(pTip, nPetal, flowerPink, 1.0, 1.0, 0.5, 0, 1);

                    Primitives.TriangleFacing(m, iBase, iL, iR, nPetal);
                    Primitives.TriangleFacing(m, iL, iTip, iR, nPetal);
                }
            }
        }
    }

    private static void BuildRosette(MeshData m, Rng rng, ulong seed, Vec3 center,
        double rosetteRadius, int leafCount, double[] c1, double[] c2)
    {
        double goldenAngle = 2.39996323; // 137.508 degrees
        double baseHue = rng.Range(-0.05, 0.05);
        var jadeBase = Primitives.Mix(c1, new[] { 0.32, 0.48, 0.38 }, 0.38 + baseHue);
        var jadeMid = Primitives.Mix(c1, new[] { 0.50, 0.68, 0.58 }, 0.28 + baseHue);
        var blushTip = Primitives.Mix(c2, new[] { 0.85, 0.28, 0.22 }, 0.35);
        var witheredBase = new[] { 0.45, 0.36, 0.24 };

        // Basal anchor cushion beneath rosette
        var rootRim = new List<Vec3>();
        for (int s = 0; s <= 8; s++)
        {
            double a = s * Math.PI * 2.0 / 8;
            rootRim.Add(center + new Vec3(Math.Cos(a) * rosetteRadius * 0.45, -0.06, Math.Sin(a) * rosetteRadius * 0.45));
        }
        Primitives.Fan(m, center + new Vec3(0, 0.01, 0), rootRim, Vec3.Up, witheredBase, witheredBase);

        for (int k = 0; k < leafCount; k++)
        {
            double rankT = (double)k / Math.Max(1, leafCount - 1); // 0 = outermost (oldest), 1 = innermost (youngest)
            double azimuth = k * goldenAngle + (seed % 6283) / 1000.0;

            // Outer leaves are longest and spread nearly flat; inner leaves are small and erect
            double leafLen = rosetteRadius * (0.45 + 0.55 * (1.0 - rankT * 0.68));
            double leafWid = leafLen * (0.35 + 0.10 * Math.Sin(rankT * Math.PI));
            double leafThick = leafLen * (0.16 + 0.10 * rankT);

            // Angle from horizontal: outer leaves ~10-18 deg, mid ~35-50 deg, inner ~70-85 deg
            double pitchRad = 0.14 + 1.22 * Math.Pow(rankT, 1.45);
            double cosP = Math.Cos(pitchRad), sinP = Math.Sin(pitchRad);

            var dir = new Vec3(Math.Cos(azimuth) * cosP, sinP, Math.Sin(azimuth) * cosP).Normalized();
            var up = new Vec3(-Math.Cos(azimuth) * sinP, cosP, -Math.Sin(azimuth) * sinP).Normalized();

            double rAttach = rosetteRadius * (0.06 + 0.20 * (1.0 - rankT));
            var attach = center + new Vec3(Math.Cos(azimuth) * rAttach, rankT * 0.14 * rosetteRadius, Math.Sin(azimuth) * rAttach);

            var colB = rankT < 0.22 ? Primitives.Mix(witheredBase, jadeBase, rankT * 4.5) : jadeBase;
            var colM = jadeMid;
            var colT = Primitives.Mix(jadeMid, blushTip, 0.42 + 0.55 * (1.0 - rankT * 0.65));

            SucculentLeaf(m, attach, dir, up, leafLen, leafWid * 0.5, leafThick, colB, colM, colT);
        }
    }

    /// <summary>
    /// Builds a single 3D faceted succulent leaf for a rosette (Sempervivum / Echeveria).
    /// Cross-section has 4 vertices (Left edge, Upper center trough, Right edge, Lower dorsal keel),
    /// swept through 3 longitudinal segments with an obovate expansion and acute mucronate apex tip.
    /// UV2.y=1 flags succulent PBR wax bloom in flora.gdshader.
    /// </summary>
    private static void SucculentLeaf(MeshData m, Vec3 attach, Vec3 dir, Vec3 up,
        double length, double halfWidth, double thickness,
        double[] colBase, double[] colMid, double[] colTip)
    {
        var forward = dir.Normalized();
        var side = forward.Cross(up).Normalized();
        if (side.LengthSq < 1e-4) side = forward.Cross(new Vec3(1, 0, 0)).Normalized();
        var trueUp = side.Cross(forward).Normalized();

        const int n = 3;
        var stations = new (Vec3 pt, double w, double h)[n + 1];

        // Stations: 0 = base, 1 = mid expansion, 2 = shoulder, 3 = acute apex
        stations[0] = (attach, halfWidth * 0.45, thickness * 0.85);
        stations[1] = (attach + forward * (length * 0.45) + trueUp * (thickness * 0.15), halfWidth * 1.05, thickness * 1.0);
        stations[2] = (attach + forward * (length * 0.80) + trueUp * (thickness * 0.05), halfWidth * 0.82, thickness * 0.70);
        stations[3] = (attach + forward * length + trueUp * (thickness * 0.02), 0.002, 0.002);

        int startVert = m.VertexCount;

        for (int i = 0; i <= n; i++)
        {
            double t = (double)i / n;
            var pt = stations[i].pt;
            double w = stations[i].w;
            double h = stations[i].h;

            var pL = pt - side * w;
            var pC = pt + trueUp * (h * 0.18);
            var pR = pt + side * w;
            var pK = pt - trueUp * (h * 0.82);

            var nTopL = (trueUp * 0.75 - side * 0.65).Normalized();
            var nTopR = (trueUp * 0.75 + side * 0.65).Normalized();
            var nBotL = (-trueUp * 0.70 - side * 0.70).Normalized();
            var nBotR = (-trueUp * 0.70 + side * 0.70).Normalized();

            var col = t < 0.5 ? Primitives.Mix(colBase, colMid, t * 2.0) : Primitives.Mix(colMid, colTip, (t - 0.5) * 2.0);

            m.AddVertex(pL, nTopL, col, 1.0, t, 0.0, 0, 1);
            m.AddVertex(pC, trueUp, col, 1.0, t, 0.5, 0, 1);
            m.AddVertex(pR, nTopR, col, 1.0, t, 1.0, 0, 1);
            m.AddVertex(pK, (nBotL + nBotR).Normalized(), col, 1.0, t, 0.5, 1, 1);
        }

        for (int i = 0; i < n; i++)
        {
            int r0 = startVert + i * 4;
            int r1 = r0 + 4;

            int l0 = r0, c0 = r0 + 1, rt0 = r0 + 2, k0 = r0 + 3;
            int l1 = r1, c1_v = r1 + 1, rt1 = r1 + 2, k1 = r1 + 3;

            var nTopL = (m.NormalAt(l0) + m.NormalAt(c0)).Normalized();
            Primitives.TriangleFacing(m, l0, l1, c0, nTopL);
            Primitives.TriangleFacing(m, l1, c1_v, c0, nTopL);

            var nTopR = (m.NormalAt(c0) + m.NormalAt(rt0)).Normalized();
            Primitives.TriangleFacing(m, c0, c1_v, rt0, nTopR);
            Primitives.TriangleFacing(m, c1_v, rt1, rt0, nTopR);

            var nBotR = (m.NormalAt(rt0) + m.NormalAt(k0)).Normalized();
            Primitives.TriangleFacing(m, rt0, rt1, k0, nBotR);
            Primitives.TriangleFacing(m, rt1, k1, k0, nBotR);

            var nBotL = (m.NormalAt(k0) + m.NormalAt(l0)).Normalized();
            Primitives.TriangleFacing(m, k0, k1, l0, nBotL);
            Primitives.TriangleFacing(m, k1, l1, l0, nBotL);
        }
    }


    // ------------------------------------------------------------------
    // Visual-break plants, carnivores, and fungi. These forms stay deterministic
    // and unit-normalized so the ordinary FloraRenderer scaling path can batch them.

    private static void AddSimpleBlade(MeshData m, Vec3 root, Vec3 mid, Vec3 tip, double width, double[] baseCol, double[] tipCol)
    {
        var axis = tip - root;
        var side = new Vec3(-axis.Z, 0, axis.X);
        if (side.LengthSq < 1e-8) side = new Vec3(1, 0, 0);
        side = side.Normalized();
        var n = axis.Cross(side).Normalized();
        if (n.LengthSq < 1e-8) n = Vec3.Up;
        var l0 = root - side * (width * 0.34);
        var r0 = root + side * (width * 0.34);
        var lm = mid - side * width;
        var rm = mid + side * width;
        int a = m.AddVertex(l0, n, baseCol, 1, 0, 0, 0, 0);
        int b = m.AddVertex(r0, n, baseCol, 1, 1, 0, 0, 0);
        int c = m.AddVertex(lm, n, Primitives.Mix(baseCol, tipCol, 0.45), 1, 0, 0.55, 0, 0);
        int d = m.AddVertex(rm, n, Primitives.Mix(baseCol, tipCol, 0.45), 1, 1, 0.55, 0, 0);
        int e = m.AddVertex(tip, n, tipCol, 1, 0.5, 1, 0, 0);
        Primitives.TriangleFacing(m, a, b, d, n);
        Primitives.TriangleFacing(m, a, d, c, n);
        Primitives.TriangleFacing(m, c, d, e, n);
        Primitives.TriangleFacing(m, b, a, d, -n);
        Primitives.TriangleFacing(m, d, a, c, -n);
        Primitives.TriangleFacing(m, d, c, e, -n);
    }

    /// <summary>Dense rhizomatous cane brake. Crooked segmented culms form a real 1.5-3 m visual wall.</summary>
    private static void Kinkcane(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        var nodeCol = Primitives.Scale(c1, 0.58);
        var youngCol = Primitives.Mix(c1, c2, 0.42);
        int canes = 14 + rng.NextInt(7);
        for (int k = 0; k < canes; k++)
        {
            double a = rng.Range(0, Math.PI * 2);
            double rr = Math.Sqrt(rng.NextDouble()) * 0.72;
            var foot = new Vec3(Math.Cos(a) * rr, 0, Math.Sin(a) * rr);
            var drift = new Vec3(Math.Cos(a + rng.Range(-0.9, 0.9)), 0, Math.Sin(a + rng.Range(-0.9, 0.9)));
            const int joints = 7;
            var path = new Vec3[joints];
            var rad = new double[joints];
            for (int j = 0; j < joints; j++)
            {
                double t = j / (double)(joints - 1);
                double kink = (j % 2 == 0 ? -1 : 1) * rng.Range(0.025, 0.075) * t;
                var side = new Vec3(-drift.Z, 0, drift.X);
                path[j] = foot + Vec3.Up * t + drift * (0.10 * t * t) + side * kink;
                rad[j] = MathD.Lerp(0.040, 0.016, t);
            }
            Primitives.Tube(m, path, rad, 7,
                (i, v) => (Primitives.Mix(Primitives.Scale(c1, 0.68), youngCol, i / 6.0), 1, i / 6.0, v, 0, 0));
            for (int j = 1; j < joints - 1; j++)
            {
                var p = path[j];
                Primitives.Ellipsoid(m, p, new Vec3(rad[j] * 1.24, 0.014, rad[j] * 1.24), 4, 7,
                    (u, v) => (nodeCol, 1, u, v, 0, 0));
                if ((j + k) % 2 != 0) continue;
                var seg = (path[j + 1] - path[j - 1]).Normalized();
                var outward = new Vec3(-seg.Z, 0.15, seg.X).Normalized() * (j % 4 == 0 ? -1 : 1);
                var root = p + outward * 0.015;
                var mid = root + outward * rng.Range(0.14, 0.22) + Vec3.Up * 0.035;
                var tip = root + outward * rng.Range(0.28, 0.42) - Vec3.Up * rng.Range(0.01, 0.06);
                AddSimpleBlade(m, root, mid, tip, rng.Range(0.035, 0.055), c1, c2);
            }
        }
        // A few low horizontal rhizome glimpses stitch the clump into a brake.
        for (int r = 0; r < 5; r++)
        {
            double a = rng.Range(0, Math.PI * 2);
            var p0 = new Vec3(Math.Cos(a) * 0.12, 0.018, Math.Sin(a) * 0.12);
            var p2 = new Vec3(Math.Cos(a) * 0.92, 0.014, Math.Sin(a) * 0.92);
            var p1 = Vec3.Lerp(p0, p2, 0.52) + new Vec3(rng.Range(-0.08,0.08), 0.012, rng.Range(-0.08,0.08));
            Primitives.Tube(m, new[] { p0, p1, p2 }, new[] { 0.017, 0.013, 0.008 }, 5,
                (i, v) => (nodeCol, 1, i, v, 0, 0));
        }
    }

    /// <summary>Wet-ground curtain of tall ribbon leaves, deliberately dense enough to break sightlines.</summary>
    private static void Veilblade(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        int leaves = 28 + rng.NextInt(9);
        for (int k = 0; k < leaves; k++)
        {
            double a = rng.Range(0, Math.PI * 2);
            double rr = Math.Sqrt(rng.NextDouble()) * 0.55;
            var root = new Vec3(Math.Cos(a) * rr, 0, Math.Sin(a) * rr);
            var outDir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            double lean = rng.Range(0.18, 0.58);
            double h = rng.Range(0.78, 1.04);
            var mid = root + outDir * (lean * 0.30) + Vec3.Up * (h * 0.62);
            var tip = root + outDir * lean + Vec3.Up * (h * rng.Range(0.58, 0.82));
            var col = Primitives.Mix(c1, c2, rng.Range(0.04, 0.48));
            AddSimpleBlade(m, root, mid, tip, rng.Range(0.045, 0.075), Primitives.Scale(col, 0.78), col);
        }
        // Sparse taller fertile stalks give the wall a vertical comb above the leaf curtain.
        for (int k = 0; k < 5; k++)
        {
            double a = rng.Range(0, Math.PI * 2);
            var foot = new Vec3(Math.Cos(a) * rng.Range(0.05,0.36), 0, Math.Sin(a) * rng.Range(0.05,0.36));
            var top = foot + new Vec3(rng.Range(-0.08,0.08), rng.Range(0.88,1.04), rng.Range(-0.08,0.08));
            Primitives.Tube(m, new[] { foot, Vec3.Lerp(foot, top, 0.6), top }, new[] { 0.010, 0.007, 0.004 }, 5,
                (i,v)=>(Primitives.Scale(c1,0.76),1,i,v,0,0));
            Primitives.Ellipsoid(m, top - Vec3.Up * 0.035, new Vec3(0.018,0.075,0.018), 5, 8,
                (u,v)=>(Primitives.Mix(c2,new[]{0.55,0.42,0.22},0.55),1,u,v,0,0));
        }
    }

    /// <summary>Interlaced arching thorn canes that read as a dense, mechanically difficult shrub wall.</summary>
    private static void Hookthicket(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        var wood = Primitives.Mix(c1, new[] { 0.25, 0.16, 0.10 }, 0.66);
        var thorn = new[] { 0.42, 0.31, 0.18 };
        int canes = 11 + rng.NextInt(5);
        for (int k = 0; k < canes; k++)
        {
            double a = 2 * Math.PI * k / canes + rng.Range(-0.28,0.28);
            var dir = new Vec3(Math.Cos(a),0,Math.Sin(a));
            var side = new Vec3(-dir.Z,0,dir.X);
            var root = dir * rng.Range(0.05,0.34);
            var p1 = root + dir * rng.Range(0.18,0.38) + Vec3.Up * rng.Range(0.28,0.48);
            var p2 = root + dir * rng.Range(0.08,0.30) + side * rng.Range(-0.30,0.30) + Vec3.Up * rng.Range(0.66,0.88);
            var p3 = root - dir * rng.Range(0.05,0.28) + side * rng.Range(-0.34,0.34) + Vec3.Up * rng.Range(0.78,1.02);
            var path = new[] { root, p1, p2, p3 };
            Primitives.Tube(m, path, new[] { 0.036,0.029,0.021,0.012 }, 6,
                (i,v)=>(Primitives.Mix(wood,c1,i/3.0*0.35),1,i/3.0,v,0,0));
            for (int j=1;j<3;j++)
            {
                var p=path[j];
                var outv=(side*(j%2==0?-1:1)+Vec3.Up*0.12).Normalized();
                var thornTip=p+outv*rng.Range(0.055,0.085)-Vec3.Up*0.018;
                Primitives.Tube(m,new[]{p,thornTip},new[]{0.010,0.0015},4,(i,v)=>(thorn,1,i,v,0,0));
                if ((k+j)%2==0)
                {
                    var leafTip=p+outv*rng.Range(0.16,0.24)+Vec3.Up*0.03;
                    AddSimpleBlade(m,p,Vec3.Lerp(p,leafTip,0.55)+Vec3.Up*0.018,leafTip,0.040,c1,c2);
                }
            }
        }
        // Cross-braces make neighbouring canes visibly knit together rather than reading as separate shrubs.
        for(int b=0;b<6;b++)
        {
            double a=rng.Range(0,Math.PI*2);
            var p0=new Vec3(Math.Cos(a)*0.55,rng.Range(0.25,0.68),Math.Sin(a)*0.55);
            var p1=new Vec3(Math.Cos(a+Math.PI+rng.Range(-0.5,0.5))*0.55,rng.Range(0.35,0.82),Math.Sin(a+Math.PI+rng.Range(-0.5,0.5))*0.55);
            Primitives.Tube(m,new[]{p0,Vec3.Lerp(p0,p1,0.5)+Vec3.Up*0.10,p1},new[]{0.018,0.013,0.008},5,(i,v)=>(wood,1,i,v,0,0));
        }
    }

    /// <summary>One sundew rosette; spatial colonies render many of these from the coverage layer.</summary>
    private static void BlueSundewRosette(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        int leaves=9+rng.NextInt(4);
        for(int k=0;k<leaves;k++)
        {
            double a=2*Math.PI*k/leaves+rng.Range(-0.16,0.16);
            var dir=new Vec3(Math.Cos(a),0,Math.Sin(a));
            var root=new Vec3(0,0.018,0);
            var pad=root+dir*rng.Range(0.55,0.92)+Vec3.Up*rng.Range(0.01,0.08);
            Primitives.Tube(m,new[]{root,Vec3.Lerp(root,pad,0.55)+Vec3.Up*0.035,pad},new[]{0.025,0.017,0.010},5,
                (i,v)=>(Primitives.Mix(c1,c2,0.18+i*0.18),1,i,v,0,0));
            var padCol=Primitives.Mix(c1,c2,rng.Range(0.25,0.62));
            Primitives.Ellipsoid(m,pad,new Vec3(0.12,0.020,0.18),4,10,(u,v)=>(padCol,1,u,v,0,0),yaw:a);
            for(int d=0;d<6;d++)
            {
                double da=a+(d-2.5)*0.20;
                var baseP=pad+new Vec3(Math.Cos(da)*0.08,0.012,Math.Sin(da)*0.08);
                var tip=baseP+new Vec3(Math.Cos(da)*0.025,0.045,Math.Sin(da)*0.025);
                Primitives.Tube(m,new[]{baseP,tip},new[]{0.004,0.0015},3,(i,v)=>(new[]{0.64,0.14,0.26},1,i,v,0,0));
                Primitives.Ellipsoid(m,tip,new Vec3(0.010,0.010,0.010),3,4,(u,v)=>(new[]{0.52,0.78,0.98},0.92,u,v,0,0));
            }
        }
        if(rng.NextDouble()<0.7)
        {
            var top=new Vec3(rng.Range(-0.08,0.08),1.0,rng.Range(-0.08,0.08));
            Primitives.Tube(m,new[]{new Vec3(0,0.02,0),new Vec3(0,0.55,0),top},new[]{0.018,0.010,0.005},5,(i,v)=>(c1,1,i,v,0,0));
            for(int i=0;i<3;i++)
            {
                var p=Vec3.Lerp(new Vec3(0,0.55,0),top,(i+1)/4.0)+new Vec3((i-1)*0.055,0,0);
                Primitives.Ellipsoid(m,p,new Vec3(0.055,0.026,0.055),4,8,(u,v)=>(new[]{0.18,0.46,0.96},1,u,v,0,0));
            }
        }
    }

    private static void PitcherPlant(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        int count=3+rng.NextInt(6);
        for(int k=0;k<count;k++)
        {
            double a=2*Math.PI*k/count+rng.Range(-0.25,0.25);
            var dir=new Vec3(Math.Cos(a),0,Math.Sin(a));
            double d=rng.Range(0.20,0.56);
            var foot=dir*d;
            double h=rng.Range(0.58,0.96);
            var p1=foot+dir*0.04+Vec3.Up*(h*0.34);
            var p2=foot-dir*0.03+Vec3.Up*(h*0.78);
            var mouth=foot+dir*rng.Range(-0.04,0.05)+Vec3.Up*h;
            var bodyCol=Primitives.Mix(c1,c2,rng.Range(0.10,0.55));
            Primitives.Tube(m,new[]{foot,p1,p2,mouth},new[]{0.055,0.12,0.095,0.16},10,
                (i,v)=>(Primitives.Mix(Primitives.Scale(bodyCol,0.72),bodyCol,i/3.0),1,i/3.0,v,0,0));
            // dark liquid plane and thick lip make the mouth visibly hollow/rain-filled.
            Primitives.Ellipsoid(m,mouth+Vec3.Up*0.003,new Vec3(0.135,0.010,0.135),3,10,
                (u,v)=>(new[]{0.10,0.12,0.12},0.92,u,v,0,0));
            Primitives.Ellipsoid(m,mouth+Vec3.Up*0.015,new Vec3(0.165,0.020,0.165),3,10,
                (u,v)=>(Primitives.Mix(c2,new[]{0.58,0.18,0.18},0.35),1,u,v,0,0));
            // lid held above and outward from the mouth.
            var lidRoot=mouth+Vec3.Up*0.025-dir*0.02;
            var lidMid=lidRoot-dir*0.07+Vec3.Up*0.09;
            var lidTip=lidRoot-dir*0.20+Vec3.Up*0.10;
            AddSimpleBlade(m,lidRoot,lidMid,lidTip,0.12,c1,c2);
        }
    }

    private static void SnapTrap(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        int traps=6+rng.NextInt(4);
        for(int k=0;k<traps;k++)
        {
            double a=2*Math.PI*k/traps+rng.Range(-0.18,0.18);
            var dir=new Vec3(Math.Cos(a),0,Math.Sin(a));
            var side=new Vec3(-dir.Z,0,dir.X);
            var root=dir*rng.Range(0.02,0.12)+Vec3.Up*0.025;
            var hinge=root+dir*rng.Range(0.42,0.72)+Vec3.Up*rng.Range(0.10,0.24);
            Primitives.Tube(m,new[]{root,Vec3.Lerp(root,hinge,0.55)+Vec3.Up*0.06,hinge},new[]{0.025,0.018,0.010},5,
                (i,v)=>(c1,1,i,v,0,0));
            var lobeCol=Primitives.Mix(c1,c2,0.55);
            for(int sgn=-1;sgn<=1;sgn+=2)
            {
                var center=hinge+side*(sgn*0.075)+dir*0.08+Vec3.Up*0.018;
                var tip=center+dir*0.19+side*(sgn*0.045);
                AddSimpleBlade(m,hinge,center,tip,0.105,lobeCol,c2);
                for(int t=0;t<5;t++)
                {
                    var toothBase=Vec3.Lerp(hinge,tip,0.18+t*0.16)+side*(sgn*0.08);
                    var toothTip=toothBase+side*(sgn*0.07)+Vec3.Up*0.025;
                    Primitives.Tube(m,new[]{toothBase,toothTip},new[]{0.005,0.001},3,(i,v)=>(Primitives.Scale(c2,0.82),1,i,v,0,0));
                }
            }
        }
    }

    private static void RainJelly(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        int lobes=7+rng.NextInt(5);
        for(int i=0;i<lobes;i++)
        {
            double a=rng.Range(0,Math.PI*2), rr=Math.Sqrt(rng.NextDouble())*0.62;
            var p=new Vec3(Math.Cos(a)*rr,rng.Range(0.05,0.20),Math.Sin(a)*rr);
            var col=Primitives.Mix(c1,c2,rng.Range(0.05,0.62));
            Primitives.Ellipsoid(m,p,new Vec3(rng.Range(0.20,0.36),rng.Range(0.12,0.28),rng.Range(0.20,0.38)),6,10,
                (u,v)=>(col,0.88,u,v,0,0),yaw:rng.Range(0,Math.PI*2));
        }
        // low basal smear visually joins the swollen lobes.
        Primitives.Ellipsoid(m,new Vec3(0,0.035,0),new Vec3(0.76,0.055,0.70),4,12,
            (u,v)=>(Primitives.Scale(c1,0.72),0.75,u,v,0,0));
    }

    private static void CarrionBell(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        var stalkCol=Primitives.Mix(c2,new[]{0.78,0.72,0.55},0.48);
        int bodies=2+rng.NextInt(3);
        for(int k=0;k<bodies;k++)
        {
            double a=2*Math.PI*k/bodies+rng.Range(-0.35,0.35);
            var foot=new Vec3(Math.Cos(a)*rng.Range(0.05,0.28),0,Math.Sin(a)*rng.Range(0.05,0.28));
            double h=rng.Range(0.68,1.0);
            var top=foot+new Vec3(rng.Range(-0.08,0.08),h,rng.Range(-0.08,0.08));
            Primitives.Tube(m,new[]{foot,Vec3.Lerp(foot,top,0.55),top},new[]{0.075,0.055,0.040},7,(i,v)=>(stalkCol,1,i,v,0,0));
            var bell=top-Vec3.Up*0.10;
            Primitives.Ellipsoid(m,bell,new Vec3(0.22,0.22,0.22),7,12,(u,v)=>(Primitives.Mix(c1,c2,v*0.28),1,u,v,0,0));
            // ragged flared skirt under the carrion-scent chamber
            for(int s=0;s<8;s++)
            {
                double th=s*Math.PI*2/8;
                var dir=new Vec3(Math.Cos(th),0,Math.Sin(th));
                var r0=bell+dir*0.10-Vec3.Up*0.08;
                var tip=r0+dir*0.13-Vec3.Up*rng.Range(0.06,0.15);
                AddSimpleBlade(m,r0,Vec3.Lerp(r0,tip,0.55),tip,0.045,c1,Primitives.Scale(c1,0.62));
            }
            Primitives.Ellipsoid(m,bell+Vec3.Up*0.035,new Vec3(0.13,0.11,0.13),5,9,
                (u,v)=>(new[]{0.32,0.12,0.10},1,u,v,0,0));
        }
    }

    private static void GlassAntlers(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        var baseCol=Primitives.Mix(c1,new[]{0.78,0.84,0.86},0.45);
        var tipCol=Primitives.Mix(c2,new[]{0.92,0.94,0.98},0.62);
        int trunks=4+rng.NextInt(3);
        for(int k=0;k<trunks;k++)
        {
            double a=2*Math.PI*k/trunks+rng.Range(-0.25,0.25);
            var root=new Vec3(Math.Cos(a)*rng.Range(0.04,0.28),0,Math.Sin(a)*rng.Range(0.04,0.28));
            var mid=root+new Vec3(rng.Range(-0.10,0.10),rng.Range(0.36,0.52),rng.Range(-0.10,0.10));
            var fork=mid+new Vec3(rng.Range(-0.08,0.08),rng.Range(0.20,0.30),rng.Range(-0.08,0.08));
            Primitives.Tube(m,new[]{root,mid,fork},new[]{0.065,0.045,0.030},6,(i,v)=>(Primitives.Mix(baseCol,tipCol,i/2.0),0.82,i,v,0,0));
            for(int b=-1;b<=1;b+=2)
            {
                var radial=new Vec3(Math.Cos(a+b*0.85),0,Math.Sin(a+b*0.85));
                var b1=fork+radial*rng.Range(0.12,0.22)+Vec3.Up*rng.Range(0.18,0.28);
                var b2=b1+radial*rng.Range(0.08,0.16)+Vec3.Up*rng.Range(0.12,0.22);
                Primitives.Tube(m,new[]{fork,b1,b2},new[]{0.028,0.018,0.006},5,(i,v)=>(Primitives.Mix(baseCol,tipCol,0.55+i*0.22),0.78,i,v,0,0));
                if((k+b+3)%2==0)
                {
                    var tine=b1+new Vec3(-radial.Z,0,radial.X)*b*0.12+Vec3.Up*0.14;
                    Primitives.Tube(m,new[]{b1,tine},new[]{0.016,0.004},4,(i,v)=>(tipCol,0.75,i,v,0,0));
                }
            }
        }
    }

    /// <summary>
    /// A tufted xerophytic tussock grass (Festuca glauca / Blue Fescue & Stipa tenuissima):
    /// - Fibrous basal thatch mound / root pedestal anchoring into dry soil or scree.
    /// - Multi-tiered tillering:
    ///   * Outer senescent skirt: arching, recurved straw-buff and dry-gold blades cascading to the soil.
    ///   * Mid-tier fountain: dense arching glaucous blue-silver blades with genuine V-camber cross sections
    ///     (involute vernation) reflecting sky highlights along their dorsal keels.
    ///   * Inner heart: stiff upright young shoots crowned at the center.
    /// - Flowering culms & branched panicles: slender wiry culms extending above the leaf canopy with delicate
    ///   branchlets bearing luminous golden-amber seed spikelets.
    /// </summary>
    private static void Tussock(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        double scale = juvenile ? 0.55 : 1.0;
        var thatchBaseCol = new[] { 0.18, 0.14, 0.11 };
        var thatchMidCol = new[] { 0.36, 0.30, 0.22 };
        var thatchTopCol = new[] { 0.52, 0.46, 0.32 };
        var glaucousBaseCol = Primitives.Mix(c1, new[] { 0.22, 0.35, 0.28 }, 0.45);
        var glaucousMidCol = Primitives.Mix(c1, new[] { 0.55, 0.70, 0.72 }, 0.35); // chalky blue bloom
        var strawCol = Primitives.Mix(c2, new[] { 0.85, 0.78, 0.52 }, 0.35);
        var weatheredBuffCol = new[] { 0.62, 0.56, 0.40 };

        // 1. Basal Thatch Mound (Root Pedestal in unit space)
        double rBase = (juvenile ? 0.22 : 0.38) * rng.Range(0.92, 1.08);
        double rTop = (juvenile ? 0.14 : 0.24) * rng.Range(0.92, 1.08);
        double hCrown = (juvenile ? 0.08 : 0.14) * rng.Range(0.90, 1.10);
        int thatchSectors = 12;
        int thatchStart = m.VertexCount;
        var crownTopVerts = new int[thatchSectors];

        for (int s = 0; s < thatchSectors; s++)
        {
            double th = 2 * Math.PI * s / thatchSectors;
            double cos = Math.Cos(th), sin = Math.Sin(th);
            double jit = 0.88 + 0.24 * Math.Sin(th * 3 + seed * 0.01) + 0.12 * Math.Cos(th * 5);
            var pBot = new Vec3(cos * rBase * jit, 0.0, sin * rBase * jit);
            var pMid = new Vec3(cos * (rBase * 0.75) * jit, hCrown * 0.50, sin * (rBase * 0.75) * jit);
            var pTop = new Vec3(cos * rTop * jit, hCrown, sin * rTop * jit);

            var nrmBot = new Vec3(cos, 0.1, sin).Normalized();
            var nrmMid = new Vec3(cos, 0.4, sin).Normalized();
            var nrmTop = new Vec3(cos * 0.5, 0.85, sin * 0.5).Normalized();

            m.AddVertex(pBot, nrmBot, thatchBaseCol, 1.0, 0.0, (double)s / thatchSectors, 0, 0);
            m.AddVertex(pMid, nrmMid, thatchMidCol, 1.0, 0.5, (double)s / thatchSectors, 0, 0);
            int v2 = m.AddVertex(pTop, nrmTop, thatchTopCol, 1.0, 1.0, (double)s / thatchSectors, 0, 0);
            crownTopVerts[s] = v2;
        }

        for (int s = 0; s < thatchSectors; s++)
        {
            int sNext = (s + 1) % thatchSectors;
            int b0 = thatchStart + s * 3, m0 = b0 + 1, t0 = b0 + 2;
            int b1 = thatchStart + sNext * 3, m1 = b1 + 1, t1 = b1 + 2;
            m.AddTriangle(b0, b1, m1);
            m.AddTriangle(b0, m1, m0);
            m.AddTriangle(m0, m1, t1);
            m.AddTriangle(m0, t1, t0);
        }

        // Center dome cap
        var centerPos = new Vec3(0, hCrown * 1.15, 0);
        int vCenter = m.AddVertex(centerPos, Vec3.Up, thatchTopCol, 1.0, 1.0, 0.5, 0, 0);
        for (int s = 0; s < thatchSectors; s++)
        {
            int sNext = (s + 1) % thatchSectors;
            m.AddTriangle(vCenter, crownTopVerts[s], crownTopVerts[sNext]);
        }

        // 2. Multi-tiered Tillering (V-Camber Grass Blades in unit space)
        // Tier 1: Outer Senescent Skirt (dry gold / weathered buff)
        int skirtCount = juvenile ? 20 : 42;
        for (int i = 0; i < skirtCount; i++)
        {
            double ang = rng.Range(0, 2 * Math.PI);
            double r0 = rTop * rng.Range(0.80, 1.25);
            var dir = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
            var side = new Vec3(-dir.Z, 0, dir.X);
            var b = dir * r0 + new Vec3(0, hCrown * rng.Range(0.35, 0.85), 0);

            double spread = rng.Range(0.60, 0.98) * scale;
            double hArc = rng.Range(0.18, 0.35) * scale;
            var mid = b + dir * (spread * 0.45) + side * rng.Range(-0.08, 0.08) + new Vec3(0, hArc, 0);
            var tip = b + dir * spread + side * rng.Range(-0.16, 0.16) + new Vec3(0, rng.Range(-0.02, 0.05), 0);

            double w = rng.Range(0.035, 0.055) * scale;
            double camber = rng.Range(0.35, 0.55);
            var cBase = Primitives.Mix(thatchTopCol, weatheredBuffCol, 0.5);
            var cMid = rng.NextDouble() < 0.6 ? strawCol : weatheredBuffCol;
            var cTip = strawCol;
            GrassBlade(m, b, mid, tip, side, w, camber, cBase, cMid, cTip);
        }

        // Tier 2: Mid-Tier Fountain (Glaucous Blue-Silver Blades)
        int fountainCount = juvenile ? 48 : 96;
        for (int i = 0; i < fountainCount; i++)
        {
            double ang = rng.Range(0, 2 * Math.PI);
            double r0 = rTop * rng.Range(0.20, 0.85);
            var dir = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
            var side = new Vec3(-dir.Z, 0, dir.X);
            var b = dir * r0 + new Vec3(0, hCrown * rng.Range(0.75, 1.10), 0);

            double spread = rng.Range(0.48, 0.88) * scale;
            double h = rng.Range(0.72, 1.05) * scale;
            var mid = b + dir * (spread * rng.Range(0.26, 0.46)) + side * rng.Range(-0.10, 0.10) + new Vec3(0, h * rng.Range(0.85, 1.08), 0);
            var tip = b + dir * spread + side * rng.Range(-0.18, 0.18) + new Vec3(0, h * rng.Range(0.35, 0.65), 0);

            double w = rng.Range(0.028, 0.048) * scale;
            double camber = rng.Range(0.40, 0.65);
            var blueTint = Primitives.Mix(glaucousMidCol, new[] { 0.42, 0.62, 0.68 }, rng.Range(-0.15, 0.20));
            var cTip = rng.NextDouble() < 0.28 ? strawCol : blueTint;
            GrassBlade(m, b, mid, tip, side, w, camber, glaucousBaseCol, blueTint, cTip);
        }

        // Tier 3: Inner Heart (Stiff Erect Young Tillers)
        int heartCount = juvenile ? 22 : 44;
        for (int i = 0; i < heartCount; i++)
        {
            double ang = rng.Range(0, 2 * Math.PI);
            double r0 = rTop * rng.Range(0.02, 0.35);
            var dir = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
            var side = new Vec3(-dir.Z, 0, dir.X);
            var b = dir * r0 + new Vec3(0, hCrown * rng.Range(0.95, 1.18), 0);

            double spread = rng.Range(0.14, 0.36) * scale;
            double h = rng.Range(0.65, 0.96) * scale;
            var mid = b + dir * (spread * 0.30) + side * rng.Range(-0.06, 0.06) + new Vec3(0, h * 0.65, 0);
            var tip = b + dir * spread + side * rng.Range(-0.10, 0.10) + new Vec3(0, h * rng.Range(0.85, 1.02), 0);

            double w = rng.Range(0.022, 0.038) * scale;
            double camber = rng.Range(0.45, 0.70);
            var youngBlue = Primitives.Mix(glaucousMidCol, new[] { 0.32, 0.58, 0.52 }, 0.25);
            GrassBlade(m, b, mid, tip, side, w, camber, glaucousBaseCol, youngBlue, youngBlue);
        }

        // 3. Flowering Culms & Branched Panicles with Seed Spikelets (Mature only)
        if (!juvenile)
        {
            int culmCount = 10 + rng.NextInt(5); // 10..14 wiry flowering culms
            var culmBaseCol = glaucousBaseCol;
            var culmTopCol = Primitives.Mix(strawCol, glaucousMidCol, 0.3);
            var spikeletAmber = new[] { 0.88, 0.76, 0.44 };
            var spikeletBronze = new[] { 0.74, 0.55, 0.30 };

            for (int c = 0; c < culmCount; c++)
            {
                double cAng = rng.Range(0, 2 * Math.PI);
                double cR0 = rTop * rng.Range(0.15, 0.65);
                var cDir = new Vec3(Math.Cos(cAng), 0, Math.Sin(cAng));
                var cSide = new Vec3(-cDir.Z, 0, cDir.X);
                var cBase = cDir * cR0 + new Vec3(0, hCrown, 0);

                double culmH = rng.Range(1.30, 1.62); // reaches well above 1.0 foliage canopy
                double leanSpread = rng.Range(0.28, 0.55);
                var cMid = cBase + cDir * (leanSpread * 0.4) + cSide * rng.Range(-0.08, 0.08) + new Vec3(0, culmH * 0.55, 0);
                var cTop = cBase + cDir * leanSpread + cSide * rng.Range(-0.14, 0.14) + new Vec3(0, culmH, 0);

                // Slender wiry culm stem
                Primitives.Tube(m, new[] { cBase, cMid, cTop }, new[] { 0.012, 0.008, 0.005 }, 3,
                    (st, u) => (Primitives.Mix(culmBaseCol, culmTopCol, st), 1.0, st, u, 0, 0));

                // Branched Panicle along the upper 35% of the culm
                int branches = 4 + rng.NextInt(3);
                for (int b = 0; b < branches; b++)
                {
                    double tBranch = 0.65 + (double)b / branches * 0.32;
                    var pBranch = cBase * (1 - tBranch) * (1 - tBranch) + cMid * 2 * (1 - tBranch) * tBranch + cTop * tBranch * tBranch;
                    double bAng = cAng + (b % 2 == 0 ? 1 : -1) * rng.Range(0.5, 1.1) + rng.Range(-0.2, 0.2);
                    var bDir = (new Vec3(Math.Cos(bAng), 0, Math.Sin(bAng)) * 0.7 + Vec3.Up * rng.Range(0.6, 1.0)).Normalized();
                    double bLen = rng.Range(0.09, 0.17) * (1.1 - b * 0.10);
                    var pSpike = pBranch + bDir * bLen;

                    // Pedicel thread
                    Primitives.Tube(m, new[] { pBranch, pSpike }, new[] { 0.004, 0.003 }, 3,
                        (st, u) => (culmTopCol, 1.0, st, u, 0, 0));

                    // Compressed golden-amber spikelet (spindle shape)
                    var spSide = bDir.Cross(Vec3.Up).Normalized();
                    if (spSide.LengthSq < 1e-4) spSide = new Vec3(1, 0, 0);
                    var spUp = spSide.Cross(bDir).Normalized();

                    double spLen = rng.Range(0.055, 0.082);
                    double spWid = 0.016;
                    double spThick = 0.010;

                    var pTip = pSpike + bDir * spLen;
                    var pSpMid = pSpike + bDir * (spLen * 0.45);

                    var vSpBase = pSpike;
                    var vSpTip = pTip;
                    var vSpL = pSpMid - spSide * spWid;
                    var vSpR = pSpMid + spSide * spWid;
                    var vSpTop = pSpMid + spUp * spThick;
                    var vSpBot = pSpMid - spUp * spThick;

                    void AddSpikeTri(Vec3 p1, Vec3 p2, Vec3 p3, double[] col)
                    {
                        var outN = ((p1 + p2 + p3) * (1.0 / 3.0) - pSpMid).Normalized();
                        int i1 = m.AddVertex(p1, outN, col, 1.0, 0, 0, 0, 1);
                        int i2 = m.AddVertex(p2, outN, col, 1.0, 0.5, 0.5, 0, 1);
                        int i3 = m.AddVertex(p3, outN, col, 1.0, 1, 1, 0, 1);
                        Primitives.TriangleFacing(m, i1, i2, i3, outN);
                    }

                    var spCol = Primitives.Mix(spikeletAmber, spikeletBronze, rng.Range(0.0, 0.4));
                    AddSpikeTri(vSpBase, vSpTop, vSpL, spCol);
                    AddSpikeTri(vSpBase, vSpR, vSpTop, spCol);
                    AddSpikeTri(vSpBase, vSpL, vSpBot, spCol);
                    AddSpikeTri(vSpBase, vSpBot, vSpR, spCol);

                    AddSpikeTri(vSpTop, vSpTip, vSpL, spCol);
                    AddSpikeTri(vSpTop, vSpR, vSpTip, spCol);
                    AddSpikeTri(vSpBot, vSpL, vSpTip, spCol);
                    AddSpikeTri(vSpBot, vSpTip, vSpR, spCol);
                }
            }
        }
    }

    /// <summary>
    /// Builds a double-sided V-cambered grass blade along a quadratic Bezier curve base → control → tip.
    /// The involute V-fold (keel) gives authentic 3D stiffness, cross-sectional depth, and longitudinal
    /// specular highlights down the midrib. UV2.y=1 flags the blade for grass striation PBR shading.
    /// </summary>
    private static void GrassBlade(MeshData m, Vec3 b, Vec3 control, Vec3 tip, Vec3 sideHint,
        double halfWidth, double camber, double[] colBase, double[] colMid, double[] colTip)
    {
        const int n = 4;
        var pts = new Vec3[n + 1];
        for (int i = 0; i <= n; i++)
        {
            double t = (double)i / n, u = 1 - t;
            pts[i] = b * (u * u) + control * (2 * u * t) + tip * (t * t);
        }

        var alongs = new Vec3[n + 1];
        var sides = new Vec3[n + 1];
        var ups = new Vec3[n + 1];

        for (int i = 0; i <= n; i++)
        {
            var along = (i < n ? pts[i + 1] - pts[i] : pts[i] - pts[i - 1]).Normalized();
            var side = (sideHint - along * sideHint.Dot(along)).Normalized();
            if (side.LengthSq < 1e-4) side = along.Cross(Vec3.Up).Normalized();
            if (side.LengthSq < 1e-4) side = along.Cross(new Vec3(1, 0, 0)).Normalized();
            var up = side.Cross(along).Normalized();

            alongs[i] = along;
            sides[i] = side;
            ups[i] = up;
        }

        for (int face = 0; face < 2; face++)
        {
            double sign = face == 0 ? 1.0 : -1.0;
            int startVert = m.VertexCount;

            for (int i = 0; i <= n; i++)
            {
                double t = (double)i / n;
                double w = halfWidth * (0.35 + 0.65 * Math.Sin(Math.PI * Math.Pow(t, 0.6))) * (1.0 - 0.94 * t);
                double d = w * camber * (1.0 - t * 0.25);

                var pLeft = pts[i] - sides[i] * w;
                var pKeel = pts[i] - ups[i] * d;
                var pRight = pts[i] + sides[i] * w;

                var nLeft = ((-sides[i] + ups[i] * (camber * 1.5)).Cross(alongs[i])).Normalized() * sign;
                var nKeel = (ups[i] * sign).Normalized();
                var nRight = ((sides[i] + ups[i] * (camber * 1.5)).Cross(alongs[i])).Normalized() * sign;

                var col = t < 0.5 ? Primitives.Mix(colBase, colMid, t * 2.0) : Primitives.Mix(colMid, colTip, (t - 0.5) * 2.0);

                m.AddVertex(pLeft, nLeft, col, 1.0, t, 0.0, face, 1);
                m.AddVertex(pKeel, nKeel, col, 1.0, t, 0.5, face, 1);
                m.AddVertex(pRight, nRight, col, 1.0, t, 1.0, face, 1);
            }

            for (int i = 0; i < n; i++)
            {
                int r0 = startVert + i * 3;
                int r1 = r0 + 3;

                int l0 = r0, k0 = r0 + 1, rt0 = r0 + 2;
                int l1 = r1, k1 = r1 + 1, rt1 = r1 + 2;

                var nrmL = m.NormalAt(l0);
                var nrmR = m.NormalAt(rt0);

                Primitives.TriangleFacing(m, l0, l1, k0, nrmL);
                Primitives.TriangleFacing(m, l1, k1, k0, nrmL);
                Primitives.TriangleFacing(m, k0, k1, rt0, nrmR);
                Primitives.TriangleFacing(m, k1, rt1, rt0, nrmR);
            }
        }
    }

    /// <summary>
    /// Streamribbon (Vallisneria americana / freshwater tape grass / eelgrass):
    /// - Anchored deep in submerged silt/gravel with a basal runner crown and fibrous rootlets.
    /// - Arching rosette tuft of long, flexible, translucent ribbon blades that stream upward through
    ///   the water column with gentle V-camber, spiral twisting, and current-driven undulation.
    /// - Mature specimens produce iconic coiled corkscrew floral scapes rising toward the water surface.
    /// </summary>
    private static void Streamribbon(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        var rootCol = new[] { 0.34, 0.28, 0.20 };
        var bladeBase = Primitives.Mix(c1, new[] { 0.10, 0.35, 0.16 }, 0.45);
        var bladeMid = c1;
        var bladeTip = Primitives.Mix(c1, c2, 0.65);
        var scapeCol = Primitives.Mix(c1, c2, 0.35);
        var flowerCol = new[] { 0.94, 0.95, 0.88 };

        // 1. Basal anchor mound and root runner tufts
        var rootRim = new List<Vec3>();
        for (int s = 0; s <= 8; s++)
        {
            double a = s * Math.PI * 2.0 / 8;
            rootRim.Add(new Vec3(Math.Cos(a) * 0.12, -0.04, Math.Sin(a) * 0.12));
        }
        Primitives.Fan(m, new Vec3(0, 0.02, 0), rootRim, Vec3.Up, rootCol, rootCol);

        // 2. Translucent streaming ribbon blades
        int bladeCount = juvenile ? 6 + rng.NextInt(3) : 12 + rng.NextInt(5);
        double flowDirAng = (seed % 6283) / 1000.0;
        var flowDir = new Vec3(Math.Cos(flowDirAng), 0, Math.Sin(flowDirAng));

        for (int b = 0; b < bladeCount; b++)
        {
            double bFraction = (double)b / bladeCount;
            double azimuth = bFraction * Math.PI * 2.0 + rng.Range(-0.25, 0.25);
            double rAttach = rng.Range(0.02, 0.08);
            var attach = new Vec3(Math.Cos(azimuth) * rAttach, 0.02 + rng.Range(0, 0.02), Math.Sin(azimuth) * rAttach);

            double bladeLen = juvenile ? rng.Range(0.35, 0.55) : rng.Range(0.65, 1.05);
            double bladeWid = rng.Range(0.045, 0.075);
            double twistTotal = rng.Range(0.8, 2.5); // graceful spiral twist along blade

            // Current sway bias: blades bend towards prevailing flow
            var curLean = flowDir * rng.Range(0.15, 0.35) + new Vec3(Math.Cos(azimuth) * 0.12, 0, Math.Sin(azimuth) * 0.12);

            const int segs = 7;
            var path = new (Vec3 pt, Vec3 side, Vec3 nrm)[segs + 1];

            for (int s = 0; s <= segs; s++)
            {
                double t = (double)s / segs;
                double h = t * bladeLen;

                // Sinusoidal wave undulation simulating water current ripples
                double wave = Math.Sin(t * Math.PI * 2.2 + azimuth) * (0.04 + 0.08 * t);
                double sideWave = Math.Cos(t * Math.PI * 1.8 + azimuth * 1.5) * (0.03 + 0.06 * t);

                var p = attach + curLean * (t * t) + new Vec3(sideWave, h, wave);

                // Twisted ribbon orientation
                double twist = azimuth + t * twistTotal;
                var sDir = new Vec3(Math.Cos(twist), 0.15 * Math.Sin(twist * 2), Math.Sin(twist)).Normalized();
                var fDir = s == 0 ? Vec3.Up : (p - path[s - 1].pt).Normalized();
                var nDir = sDir.Cross(fDir).Normalized();

                path[s] = (p, sDir, nDir);
            }

            // Build ribbon mesh: 3 vertices per station (Left, Center keel, Right)
            int vStart = m.VertexCount;
            for (int s = 0; s <= segs; s++)
            {
                double t = (double)s / segs;
                var (p, sDir, nDir) = path[s];

                // Width profile: narrow base, wide ribbon mid, rounded tip
                double w = bladeWid * Math.Sin(t * Math.PI * 0.85 + 0.15);
                if (s == segs) w = bladeWid * 0.15;

                double camber = w * 0.20; // gentle involute V-camber
                var pL = p - sDir * (w * 0.5) + nDir * camber;
                var pC = p - nDir * camber;
                var pR = p + sDir * (w * 0.5) + nDir * camber;

                var col = t < 0.4 ? Primitives.Mix(bladeBase, bladeMid, t / 0.4) : Primitives.Mix(bladeMid, bladeTip, (t - 0.4) / 0.6);

                m.AddVertex(pL, nDir, col, 1.0, t, 0.0, 0, 0);
                m.AddVertex(pC, nDir, col, 1.0, t, 0.5, 0, 0);
                m.AddVertex(pR, nDir, col, 1.0, t, 1.0, 0, 0);
            }

            for (int s = 0; s < segs; s++)
            {
                int i0 = vStart + s * 3;
                int i1 = i0 + 3;

                // Left quad
                Primitives.TriangleFacing(m, i0, i1, i0 + 1, path[s].nrm);
                Primitives.TriangleFacing(m, i1, i1 + 1, i0 + 1, path[s].nrm);

                // Right quad
                Primitives.TriangleFacing(m, i0 + 1, i1 + 1, i0 + 2, path[s].nrm);
                Primitives.TriangleFacing(m, i1 + 1, i1 + 2, i0 + 2, path[s].nrm);
            }
        }

        // 3. Corkscrew spiral floral scapes (Vallisneria spiralis characteristic)
        if (!juvenile && seed % 2 == 0)
        {
            int scapeCount = 2 + rng.NextInt(2);
            for (int sc = 0; sc < scapeCount; sc++)
            {
                double scAng = (double)sc / scapeCount * Math.PI * 2.0 + rng.Range(-0.3, 0.3);
                var scCenter = new Vec3(Math.Cos(scAng) * 0.05, 0.03, Math.Sin(scAng) * 0.05);
                double scH = rng.Range(0.65, 0.95);
                double scR = rng.Range(0.02, 0.035);
                int coils = 4 + rng.NextInt(3);
                const int scSteps = 24;

                var scPath = new List<Vec3>();
                var scRads = new List<double>();

                for (int i = 0; i <= scSteps; i++)
                {
                    double t = (double)i / scSteps;
                    double coilAng = scAng + t * coils * Math.PI * 2.0;
                    double curR = scR * (1.1 - t * 0.3);
                    var pt = scCenter + new Vec3(Math.Cos(coilAng) * curR, t * scH, Math.Sin(coilAng) * curR);
                    scPath.Add(pt);
                    scRads.Add(0.005 * (1.0 - t * 0.4));
                }

                Primitives.Tube(m, scPath, scRads, 3, (st, u) => (scapeCol, 1.0, (double)st / scSteps, u, 0, 0));

                // Terminal emergent spathe / miniature pearl blossom
                var topPt = scPath[^1];
                Primitives.Ellipsoid(m, topPt + new Vec3(0, 0.015, 0), new Vec3(0.016, 0.024, 0.016), 4, 6,
                    (u, v) => (flowerCol, 1.0, u, v, 0, 0));
            }
        }
    }

    /// <summary>
    /// Fencomb (Myriophyllum spicatum / water-milfoil & Ceratophyllum / hornwort):
    /// - Flexible, slender branching stems rising from bottom sediments.
    /// - Multi-tiered nodes carrying whorls of 4-5 fine comb-like pinnatisect leaves.
    /// - Feathery underwater plumes creating dense micro-shelters for aquatic fauna.
    /// </summary>
    private static void Fencomb(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        var stemColBase = Primitives.Mix(c2, new[] { 0.42, 0.22, 0.15 }, 0.4);
        var stemColTip = Primitives.Mix(c1, c2, 0.28);
        var leafBase = c1;
        var leafTip = Primitives.Mix(c1, new[] { 0.45, 0.75, 0.35 }, 0.4);
        var budCol = Primitives.Mix(c2, new[] { 0.65, 0.25, 0.20 }, 0.5);

        int stemCount = juvenile ? 1 + rng.NextInt(2) : 3 + rng.NextInt(3);

        for (int s = 0; s < stemCount; s++)
        {
            double stemAng = (double)s / stemCount * Math.PI * 2.0 + rng.Range(-0.35, 0.35);
            double rBase = rng.Range(0.02, 0.07);
            var basePt = new Vec3(Math.Cos(stemAng) * rBase, 0.02, Math.Sin(stemAng) * rBase);

            double stemLen = juvenile ? rng.Range(0.35, 0.52) : rng.Range(0.65, 0.98);
            var swayDir = new Vec3(Math.Cos(stemAng + 0.5), 0, Math.Sin(stemAng + 0.5)).Normalized();

            // Flexible curving stem path
            const int segs = 6;
            var path = new List<Vec3>();
            var rads = new List<double>();

            for (int i = 0; i <= segs; i++)
            {
                double t = (double)i / segs;
                double h = t * stemLen;
                double swayAmt = Math.Sin(t * Math.PI * 1.4) * (0.08 * t);
                var pt = basePt + swayDir * swayAmt + new Vec3(0, h, 0);
                path.Add(pt);
                rads.Add(0.012 * (1.0 - t * 0.55));
            }

            Primitives.Tube(m, path, rads, 4,
                (step, u) => (Primitives.Mix(stemColBase, stemColTip, (double)step / segs), 1.0, (double)step / segs, u, 0, 0));

            // Nodal whorls of comb-like feathered leaves
            int nodeCount = juvenile ? 4 + rng.NextInt(3) : 8 + rng.NextInt(4);
            for (int n = 0; n < nodeCount; n++)
            {
                double nodeT = 0.12 + (double)n / nodeCount * 0.82;
                int segIdx = Math.Min(segs - 1, (int)(nodeT * segs));
                double segFrac = (nodeT * segs) - segIdx;
                var nodePt = Vec3.Lerp(path[segIdx], path[segIdx + 1], segFrac);

                int leavesInWhorl = 4 + (n % 2); // 4-5 leaves per whorl
                double whorlPhase = n * 0.78 + seed * 0.1;

                double leafLen = (0.09 + 0.05 * Math.Sin(nodeT * Math.PI)) * (1.1 - nodeT * 0.3);

                for (int l = 0; l < leavesInWhorl; l++)
                {
                    double lAng = whorlPhase + (double)l / leavesInWhorl * Math.PI * 2.0;
                    // Leaf ascends at ~25-45 degrees from stem
                    var lDir = new Vec3(Math.Cos(lAng), 0.45, Math.Sin(lAng)).Normalized();
                    var lTip = nodePt + lDir * leafLen;

                    // Central rachis wire
                    Primitives.Tube(m, new[] { nodePt, lTip }, new[] { 0.0035, 0.0015 }, 3,
                        (step, u) => (leafBase, 1.0, step, u, 0, 0));

                    // Comb pinnules (pectinate feathering)
                    const int pinnulePairs = 4;
                    var sideVec = lDir.Cross(Vec3.Up).Normalized();
                    for (int p = 1; p <= pinnulePairs; p++)
                    {
                        double pT = (double)p / (pinnulePairs + 1);
                        var pAttach = Vec3.Lerp(nodePt, lTip, pT);
                        double pLen = leafLen * 0.42 * Math.Sin(pT * Math.PI);

                        var pTipL = pAttach - sideVec * pLen + new Vec3(0, 0.008, 0);
                        var pTipR = pAttach + sideVec * pLen + new Vec3(0, 0.008, 0);

                        Primitives.Tube(m, new[] { pAttach, pTipL }, new[] { 0.002, 0.0008 }, 3,
                            (step, u) => (leafTip, 1.0, step, u, 0, 0));
                        Primitives.Tube(m, new[] { pAttach, pTipR }, new[] { 0.002, 0.0008 }, 3,
                            (step, u) => (leafTip, 1.0, step, u, 0, 0));
                    }
                }
            }

            // Apical feathery crown bud
            var top = path[^1];
            Primitives.Ellipsoid(m, top + new Vec3(0, 0.015, 0), new Vec3(0.022, 0.035, 0.022), 4, 6,
                (u, v) => (budCol, 1.0, u, v, 0, 0));
        }
    }

    /// <summary>Fruticose lichen: a spongy mound of finely forking hollow stalks with pale, nodding tips.</summary>
    private static void Fruticose(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        void Branch(Vec3 from, Vec3 dir, double len, double rad, int depth)
        {
            var to = from + dir * len;
            Primitives.Tube(m, new[] { from, (from + to) * 0.5 + new Vec3(rng.Range(-0.02, 0.02), 0, rng.Range(-0.02, 0.02)), to }, new[] { rad, rad * 0.85, rad * 0.7 }, 4,
                (i, v) => (Primitives.Mix(c1, c2, (3 - depth) / 3.0 + i * 0.1), 1, i, v, 0, 0));
            if (depth <= 0) { Primitives.Ellipsoid(m, to, new Vec3(rad, rad, rad), 3, 5, (a, b) => (c2, 1, a, b, 0, 0)); return; }
            for (int k = 0; k < 2; k++)
            {
                var nd = (dir + new Vec3(rng.Range(-0.6, 0.6), rng.Range(-0.1, 0.3), rng.Range(-0.6, 0.6))).Normalized();
                Branch(to, nd, len * 0.72, rad * 0.72, depth - 1);
            }
        }
        for (int k = 0; k < 9; k++)
        {
            double ang = rng.Range(0, 2 * Math.PI), r = Math.Sqrt(rng.NextDouble()) * 0.65;
            var b = new Vec3(Math.Cos(ang) * r, 0, Math.Sin(ang) * r);
            var dir = (new Vec3(Math.Cos(ang) * 0.35, 1, Math.Sin(ang) * 0.35)).Normalized();
            Branch(b, dir, rng.Range(0.3, 0.42), 0.06, 3);
        }
    }

    /// <summary>
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
    }

    private static readonly double[] WoodyBark = { 0.30, 0.24, 0.18 };
    private static readonly double[] WoodyBarkLight = { 0.42, 0.34, 0.24 };

    // Bark carries UV2.x=2; blade UV2.y=1 is intentionally disjoint from woody tissue.
    private static void Wood(MeshData m, IReadOnlyList<Vec3> path, IReadOnlyList<double> radii, int sides = 6)
    {
        double length = 0;
        var distance = new double[path.Count];
        for (int i = 1; i < path.Count; i++) distance[i] = length += (path[i] - path[i - 1]).Length;
        Primitives.Tube(m, path, radii, sides, (i, v) =>
            (Primitives.Mix(WoodyBark, WoodyBarkLight, 0.22 + 0.23 * i / (path.Count - 1.0)), 1,
             distance[i], v, 2, 0));
    }

    private static Vec3 Curve(Vec3 root, Vec3 control, Vec3 tip, double t)
    {
        double u = 1 - t;
        return root * (u * u) + control * (2 * t * u) + tip * (t * t);
    }

    private static void WoodyCurve(MeshData m, Vec3 root, Vec3 control, Vec3 tip, double radius, int segments = 4)
    {
        var path = new Vec3[segments + 1];
        var radii = new double[segments + 1];
        for (int i = 0; i <= segments; i++)
        {
            double t = i / (double)segments;
            path[i] = Curve(root, control, tip, t);
            radii[i] = radius * (0.08 + 0.92 * Math.Pow(1 - t, 1.15));
        }
        Wood(m, path, radii);
    }

    private static Vec3[] TreeBole(MeshData m, Rng rng, double radius, double height, double lean)
    {
        double angle = rng.Range(0, Math.PI * 2);
        var drift = new Vec3(Math.Cos(angle), 0, Math.Sin(angle)) * lean;
        var path = new Vec3[7];
        var radii = new double[7];
        double[] heights = { -0.006, 0.05, 0.20, 0.40, 0.61, 0.80, 1.0 };
        for (int i = 0; i < path.Length; i++)
        {
            double t = heights[i];
            path[i] = new Vec3(0, height * t, 0) + drift * (t * t)
                + new Vec3(Math.Sin(t * 5) * lean * 0.23, 0, Math.Sin(t * 3.6) * lean * 0.18);
            radii[i] = radius * (0.075 + 0.925 * Math.Pow(1 - Math.Max(0, t), 0.8)) * (i == 0 ? 1.35 : 1);
        }
        Wood(m, path, radii, 9);
        // Buttressed root collars dive below the soil instead of ending as a hovering flat tube.
        for (int r = 0; r < 5; r++)
        {
            double a = angle + r * 1.2566 + rng.Range(-0.24, 0.24);
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            double reach = radius * rng.Range(2.1, 3.0);
            Wood(m, new[] { dir * (radius * 0.28) + new Vec3(0, 0.07, 0),
                dir * (reach * 0.55) + new Vec3(0, 0.017, 0), dir * reach + new Vec3(0, -0.01, 0) },
                new[] { radius * 0.48, radius * 0.21, 0.002 });
        }
        return path;
    }

    /// <summary>Raised, irregular longitudinal bark ridges on Kiteleaf's mature cottonwood-like bole.</summary>
    private static void CottonwoodBark(MeshData m, IReadOnlyList<Vec3> path, double baseRadius)
    {
        const int sides = 48;
        int start = m.VertexCount;
        double[] levels = { -0.006, 0.05, 0.20, 0.40, 0.61, 0.80, 1.0 };
        for (int i = 0; i < path.Count; i++)
        {
            double t = levels[i];
            double core = baseRadius * (0.075 + 0.925 * Math.Pow(1 - Math.Max(0, t), 0.8)) * (i == 0 ? 1.35 : 1);
            double relief = 0.14 * (1 - 0.68 * t);
            for (int s = 0; s <= sides; s++)
            {
                double around = (double)s / sides;
                double angle = around * Math.PI * 2;
                double phase = angle * 12 + t * 1.4 + 0.23 * Math.Sin(angle * 5 + t * 3);
                double ridge = Math.Cos(phase);
                double radius = core * (1.18 + relief * ridge);
                var radial = new Vec3(Math.Cos(angle), 0, Math.Sin(angle));
                var circumferential = new Vec3(-Math.Sin(angle), 0, Math.Cos(angle));
                double slope = -core * relief * 12 * Math.Sin(phase);
                var normal = (radial - circumferential * (slope / radius)).Normalized();
                double shade = 0.69 + 0.31 * (ridge + 1) * 0.5;
                m.AddVertex(path[i] + radial * radius, normal,
                    new[] { 0.38 * shade, 0.39 * shade, 0.38 * shade }, 1, t, around, 3, 0);
            }
        }
        for (int i = 0; i < path.Count - 1; i++)
            for (int s = 0; s < sides; s++)
            {
                int a = start + i * (sides + 1) + s, b = a + sides + 1;
                m.AddTriangle(a, a + 1, b);
                m.AddTriangle(a + 1, b + 1, b);
            }
    }

    private static Vec3 Along(IReadOnlyList<Vec3> path, double t)
    {
        double f = MathD.Clamp01(t) * (path.Count - 1);
        int i = Math.Min(path.Count - 2, (int)f);
        return Vec3.Lerp(path[i], path[i + 1], f - i);
    }

    /// <summary>Small softly oval Kiteleaf blade with a rounded perimeter and a pale reverse.</summary>
    private static void KiteleafOvateBlade(MeshData m, Rng rng, Vec3 root, Vec3 tip,
        Vec3 sideHint, double halfWidth, double[] topColor)
    {
        var axis = tip - root;
        double length = axis.Length;
        if (length < 1e-8) return;
        var forward = axis / length;
        var side = sideHint - forward * sideHint.Dot(forward);
        if (side.LengthSq < 1e-8) side = forward.Cross(Vec3.Up);
        side = Axis.RotateAround(side.Normalized(), forward, rng.Range(-0.42, 0.42));
        var up = side.Cross(forward).Normalized();
        double phase = rng.Range(0, Math.PI * 2);
        double asymmetry = rng.Range(-0.08, 0.08);
        const int rimCount = 12;
        for (int face = 0; face < 2; face++)
        {
            var visibleNormal = face == 0 ? up : -up;
            var color = face == 0 ? topColor : new[] { 0.46, 0.59, 0.54 };
            int center = m.AddVertex(root + axis * 0.5 + up * (length * 0.055), visibleNormal,
                color, 1, 0.5, 0.5, face, 1);
            int first = m.VertexCount;
            for (int s = 0; s < rimCount; s++)
            {
                double angle = s * Math.PI * 2 / rimCount;
                double t = (1 + Math.Cos(angle)) * 0.5;
                double across = Math.Sin(angle);
                double edge = halfWidth * across * (1 + asymmetry * Math.Sign(across)
                    + 0.045 * Math.Sin(angle * 3 + phase));
                var p = root + axis * t + side * edge
                    + up * (length * 0.008 * Math.Sin(Math.PI * t));
                m.AddVertex(p, visibleNormal, color, 1, t, (across + 1) * 0.5, face, 1);
            }
            for (int s = 0; s < rimCount; s++)
                Primitives.TriangleFacing(m, center, first + s, first + (s + 1) % rimCount,
                    visibleNormal);
        }
    }

    private static void LeafSpray(MeshData m, Rng rng, Vec3 root, Vec3 control, Vec3 tip,
        double size, double widthRatio, int leaves, double[] c1, double[] c2,
        bool broad = false, bool silverBack = false, bool tipCluster = false)
    {
        WoodyCurve(m, root, control, tip, broad ? 0.011 : 0.007, 2);
        var direction = (tip - root).Normalized();
        var side = direction.Cross(Vec3.Up).Normalized();
        for (int j = 0; j < leaves; j++)
        {
            double t = 0.22 + 0.72 * j / Math.Max(1, leaves - 1);
            var attach = Curve(root, control, tip, t);
            double sign = j % 2 == 0 ? 1 : -1;
            var outward = (side * sign * rng.Range(0.7, 1.0) + direction * rng.Range(0.42, 0.9)
                + Vec3.Up * rng.Range(-0.20, 0.24)).Normalized();
            double len = size * rng.Range(0.76, 1.18) * (1 - 0.22 * t);
            var petiole = attach + outward * (len * (silverBack ? 0.27 : broad ? 0.20 : 0.12));
            // Short green petioles join each blade visibly to its supporting twig.
            Primitives.Tube(m, new[] { attach, petiole }, new[] { 0.0023, 0.0012 }, 6,
                (i, v) => (Primitives.Scale(c1, 0.75), 1, i, v, 0, 0));
            var col = Primitives.Mix(c1, c2, rng.Range(0.08, 0.55));
            if (silverBack)
                KiteleafOvateBlade(m, rng, petiole, petiole + outward * len,
                    outward.Cross(Vec3.Up), len * widthRatio, col);
            else
                FoliageBlade.Build(m, petiole, petiole + outward * len, outward.Cross(Vec3.Up), len * widthRatio,
                    Primitives.Scale(col, 0.82), Primitives.Scale(col, rng.Range(0.97, 1.12)),
                    camber: rng.Range(0.12, 0.24), curl: rng.Range(0.06, 0.20),
                    roll: rng.Range(-0.65, 0.65), asymmetry: rng.Range(-0.15, 0.15),
                    shoulder: broad ? 0.66 : 0.91, segments: broad ? 5 : 4);
        }
        if (tipCluster)
        {
            // A small fan finishes each outer twig, filling the crown edge without
            // stuffing extra leaves along the visible supporting branches.
            for (int j = 0; j < 5; j++)
            {
                double angle = j * Math.PI * 0.4 + rng.Range(-0.22, 0.22);
                var outward = (direction * rng.Range(0.45, 0.65)
                    + side * Math.Cos(angle) * 0.72
                    + Vec3.Up * Math.Sin(angle) * 0.32).Normalized();
                double len = size * rng.Range(0.83, 1.13);
                var petiole = tip + outward * (len * 0.19);
                Primitives.Tube(m, new[] { tip, petiole }, new[] { 0.0019, 0.0009 }, 6,
                    (i, v) => (Primitives.Scale(c1, 0.75), 1, i, v, 0, 0));
                var col = Primitives.Mix(c1, c2, rng.Range(0.15, 0.58));
                KiteleafOvateBlade(m, rng, petiole, petiole + outward * len,
                    outward.Cross(Vec3.Up), len * widthRatio, col);
            }
        }
    }

    /// <summary>Hierarchical crowns: crooked trunk, ascending scaffolds, fine alternate leafy shoots.
    /// The three broadleaf species retain their open, umbrella, and high-pioneer growth habits.</summary>
    private static void BroadleafTree(MeshData m, Rng rng, double[] c1, double[] c2, int kind)
    {
        bool broad = kind == 1;
        int branches = kind == 2 ? 12 : broad ? 6 : 8;
        // Only fenneedle keeps an uninterrupted central leader. Broadleaf boles
        // end at a fork; ascending scaffolds carry the height of the crown.
        double forkHeight = broad ? 0.23 : kind == 2 ? 0.38 : 0.34;
        double boleRadius = broad ? 0.11 : kind == 2 ? 0.16 : 0.075;
        var trunk = TreeBole(m, rng, boleRadius, forkHeight, broad ? 0.025 : 0.045);
        if (kind == 2) CottonwoodBark(m, trunk, boleRadius);
        double phase = rng.Range(0, Math.PI * 2);
        int leaders = broad ? 4 : kind == 2 ? 3 : 2;
        var leaderTips = new Vec3[leaders];
        var leaderRoots = new Vec3[leaders];
        var leaderControls = new Vec3[leaders];
        for (int leader = 0; leader < leaders; leader++)
        {
            double a = phase + leader * Math.PI * 2 / leaders + rng.Range(-0.24, 0.24);
            var radial = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var root = Along(trunk, broad ? 0.70 + leader * 0.07 : 0.81 + leader * 0.05);
            double spread = broad ? rng.Range(0.36, 0.55) : kind == 2
                ? rng.Range(0.34, 0.50) : rng.Range(0.28, 0.49);
            var tip = root + radial * spread + Vec3.Up *
                (broad ? rng.Range(0.43, 0.55) : kind == 2 ? rng.Range(0.32, 0.42) : rng.Range(0.53, 0.68));
            var control = root + radial * (spread * (kind == 2 ? 0.56 : 0.30))
                + Vec3.Up * (tip.Y - root.Y) * (kind == 2 ? 0.68 : 0.53);
            var woodenLeaderTip = kind == 2 ? Curve(root, control, tip, 0.88) : tip;
            var woodenLeaderControl = kind == 2 ? Curve(root, control, tip, 0.45) : control;
            WoodyCurve(m, root, woodenLeaderControl, woodenLeaderTip,
                boleRadius * (broad ? 0.53 : kind == 2 ? 0.30 : 0.45), 5);
            leaderRoots[leader] = root;
            leaderControls[leader] = control;
            leaderTips[leader] = tip;
        }
        for (int k = 0; k < branches; k++)
        {
            double fraction = k / (double)(branches - 1);
            double angle = phase + k * 2.39996 + rng.Range(-0.3, 0.3);
            var radial = new Vec3(Math.Cos(angle), 0, Math.Sin(angle));
            var sideways = radial.Cross(Vec3.Up);
            int parent = k % leaders;
            double stemT = kind == 2 ? 0.11 + fraction * 0.68 : 0.31 + fraction * 0.56;
            var root = Curve(leaderRoots[parent], leaderControls[parent], leaderTips[parent], stemT);
            double length = (kind == 2 ? rng.Range(0.72, 0.94) : rng.Range(0.29, broad ? 0.47 : 0.42))
                * (1 - fraction * (kind == 2 ? 0.48 : 0.18));
            var tip = root + radial * length + Vec3.Up *
                (kind == 2 ? rng.Range(-0.085, 0.035) : broad ? rng.Range(0.02, 0.10) : rng.Range(0.08, 0.19));
            var control = root + radial * (length * 0.48) + Vec3.Up *
                (kind == 2 ? rng.Range(-0.025, 0.045) : broad ? 0.20 : 0.18)
                + sideways * rng.Range(kind == 2 ? -0.10 : -0.06, kind == 2 ? 0.10 : 0.06);
            // Upper Kiteleaf limbs are fine crown wood; only the low outward scaffolds
            // retain substantial girth, so no heavy bars cross the canopy summit.
            var woodenTip = kind == 2 ? Curve(root, control, tip, 0.92) : tip;
            var woodenControl = kind == 2 ? Curve(root, control, tip, 0.46) : control;
            WoodyCurve(m, root, woodenControl, woodenTip,
                kind == 2 ? 0.004 + 0.037 * Math.Pow(1 - fraction, 1.7) : broad ? 0.039 : 0.026, 5);
            int sprigs = kind == 2 ? 12 : 3;
            for (int sprig = 0; sprig < sprigs; sprig++)
            {
                double t = kind == 2 ? 0.12 + sprig * 0.073 : 0.45 + sprig * 0.23;
                var junction = Curve(root, control, tip, t);
                double sign = sprig % 2 == 0 ? -1 : 1;
                var sprigDirection = (radial * 0.70 + sideways * sign * rng.Range(0.40, 0.85)).Normalized();
                var end = junction + sprigDirection * rng.Range(kind == 2 ? 0.23 : 0.17, kind == 2 ? 0.34 : 0.26)
                    + Vec3.Up * rng.Range(kind == 2 ? -0.105 : -0.03, kind == 2 ? 0.025 : broad ? 0.10 : 0.13);
                var bend = Vec3.Lerp(junction, end, 0.5) + Vec3.Up * 0.035;
                LeafSpray(m, rng, junction, bend, end,
                    broad ? 0.26 : kind == 2 ? 0.075 : 0.18,
                    broad ? 0.47 : kind == 2 ? 0.32 : 0.26, kind == 2 ? 9 : broad ? 4 : 5,
                    c1, c2, broad, kind == 2, kind == 2);
            }
        }
        // Leader foliage closes the top without an opaque canopy volume.
        for (int j = 0; j < leaders; j++)
        {
            double a = phase + j * Math.PI * 2 / leaders;
            var crown = leaderTips[j];
            var end = crown + new Vec3(Math.Cos(a) * (kind == 2 ? 0.20 : 0.15),
                kind == 2 ? -0.035 : broad ? -0.035 : 0.045, Math.Sin(a) * (kind == 2 ? 0.20 : 0.15));
            LeafSpray(m, rng, leaderControls[j], crown, end, broad ? 0.26 : kind == 2 ? 0.075 : 0.19,
                broad ? 0.47 : kind == 2 ? 0.32 : 0.30, kind == 2 ? 11 : 4, c1, c2, broad, kind == 2, kind == 2);
            if (kind == 2)
            {
                for (int shoot = 0; shoot < 3; shoot++)
                {
                    double t = 0.48 + shoot * 0.17;
                    var junction = Curve(leaderRoots[j], leaderControls[j], crown, t);
                    double side = (shoot - 1) * 0.22;
                    var innerTip = junction + new Vec3(-Math.Cos(a) * 0.18 - Math.Sin(a) * side,
                        rng.Range(0.09, 0.16), -Math.Sin(a) * 0.18 + Math.Cos(a) * side);
                    LeafSpray(m, rng, junction, Vec3.Lerp(junction, innerTip, 0.52) + Vec3.Up * 0.025,
                        innerTip, 0.075, 0.32, 9, c1, c2, false, true, true);
                }
            }
        }
    }

    /// <summary>
    /// Ironlace: Pioneer hardwood (Carpinus caroliniana / Musclewood &amp; Ostrya virginiana / Ironwood).
    /// - Muscular, fluted sinewy trunk dividing low into 2-3 crooked ascending codominant leaders.
    /// - Wide, spreading crown canopy with an intricate zig-zagging 3D lattice of fine, tough branchlets ("iron lace").
    /// - Full, lush canopy:
    ///   - Alternate, 2-ranked (distichous) ovate Hornbeam leaves along every zig-zag branchlet.
    ///   - Inner centripetal leafy shoots radiating back inward to crown the canopy summit without gaps.
    ///   - Prominent midrib trough, acute tips, rich forest green upper face (c1) and pale satin lime underside.
    ///   - Dense terminal leaf fans crowning every twig to ensure a rich, full crown mass.
    /// - Botanical signature: pendulous, hop-like papery fruiting strobiles (Ostrya-style inflated bracts)
    ///   hanging gracefully below the foliage planes.
    /// - Authored in unit-normalized bounds [0..1] for height and radius.
    /// </summary>
    private static void TreeIronlace(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        double totalH = 1.0;
        double boleRadius = juvenile ? 0.038 : 0.075;
        double forkH = totalH * (juvenile ? 0.20 : 0.26);
        var colUnder = Primitives.Mix(Primitives.Scale(c2, 0.85), new[] { 0.38, 0.58, 0.32 }, 0.45);
        var petioleCol = Primitives.Mix(WoodyBarkLight, c1, 0.50);
        var strobileCol = Primitives.Mix(c2, new[] { 0.75, 0.82, 0.48 }, 0.65);

        // 1. Muscular fluted trunk dividing low into codominant leaders
        var trunkPath = new Vec3[5];
        var trunkRadii = new double[5];
        double trunkAngle = rng.Range(0, Math.PI * 2);
        var trunkDrift = new Vec3(Math.Cos(trunkAngle), 0, Math.Sin(trunkAngle)) * (forkH * 0.08);

        for (int i = 0; i < 5; i++)
        {
            double t = i / 4.0;
            trunkPath[i] = new Vec3(0, forkH * t, 0) + trunkDrift * (t * t);
            trunkRadii[i] = boleRadius * (0.42 + 0.58 * Math.Pow(1.0 - t * 0.4, 0.8)) * (i == 0 ? 1.35 : 1.0);
        }
        Wood(m, trunkPath, trunkRadii, 8);

        // Basal muscular root sinews diving into soil
        for (int r = 0; r < 5; r++)
        {
            double a = trunkAngle + r * (Math.PI * 2 / 5) + rng.Range(-0.2, 0.2);
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            double reach = boleRadius * rng.Range(2.0, 2.8);
            Wood(m, new[] { dir * (boleRadius * 0.6) + new Vec3(0, forkH * 0.20, 0),
                            dir * (reach * 0.55) + new Vec3(0, 0.02, 0),
                            dir * reach + new Vec3(0, -0.012, 0) },
                    new[] { boleRadius * 0.42, boleRadius * 0.20, 0.003 }, 6);
        }

        // 2. Ascending crooked codominant leaders spreading widely outward (Musclewood habit)
        int leaderCount = juvenile ? 2 : 3;
        var forkPoint = trunkPath[4];
        var leaderTips = new List<Vec3>();
        var leaderControls = new List<Vec3>();

        for (int l = 0; l < leaderCount; l++)
        {
            double lAng = trunkAngle + l * (Math.PI * 2 / leaderCount) + rng.Range(-0.25, 0.25);
            var lDir = new Vec3(Math.Cos(lAng), 0, Math.Sin(lAng));
            double targetH = totalH * (l == 0 ? 1.0 : l == 1 ? 0.94 : 0.88);
            // Splay leaders outward much further into the crown
            double reach = (totalH * 0.52) * rng.Range(0.90, 1.15);

            var lMid = forkPoint + lDir * (reach * 0.50) + new Vec3(0, (targetH - forkH) * 0.44, 0)
                       + new Vec3(rng.Range(-0.04, 0.04), 0, rng.Range(-0.04, 0.04));
            var lTip = forkPoint + lDir * reach + new Vec3(0, targetH - forkH, 0);

            WoodyCurve(m, forkPoint, lMid, lTip, boleRadius * 0.48, 5);
            leaderControls.Add(lMid);
            leaderTips.Add(lTip);
        }

        // Helper for building a dense planar leaf spray along a twig
        void IronlaceLeafSpray(Vec3 start, Vec3 dir, double length, double leafScale, bool hasStrobile)
        {
            var fwd = dir.Normalized();
            var side = fwd.Cross(Vec3.Up).Normalized();
            if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);
            var norm = side.Cross(fwd).Normalized();

            // Zig-zag twig backbone
            int nodes = juvenile ? 6 : 9;
            var twigPts = new Vec3[nodes + 1];
            twigPts[0] = start;
            for (int n = 1; n <= nodes; n++)
            {
                double nt = (double)n / nodes;
                double zig = (n % 2 == 0 ? 1.0 : -1.0) * (length * 0.055);
                twigPts[n] = start + fwd * (length * nt) + side * zig - Vec3.Up * (length * 0.035 * nt);
            }
            for (int n = 0; n < nodes; n++)
            {
                Primitives.Tube(m, new[] { twigPts[n], twigPts[n + 1] },
                    new[] { 0.009 * (1.0 - n * 0.07), 0.007 * (1.0 - n * 0.07) }, 6,
                    (i, v) => (WoodyBarkLight, 1, i, v, 2, 0));
            }

            // Distichous alternate ovate leaves along the twig
            for (int n = 1; n <= nodes; n++)
            {
                double nt = (double)n / nodes;
                var nodePos = twigPts[n];
                double leafSign = n % 2 == 0 ? 1.0 : -1.0;
                var leafDir = (side * leafSign * 0.82 + fwd * 0.52 + norm * 0.15).Normalized();
                var leafSide = leafDir.Cross(norm).Normalized();

                double lLen = leafScale * rng.Range(0.15, 0.20);
                double lWidth = lLen * 0.56;

                // Petiole
                var petioleEnd = nodePos + leafDir * (lLen * 0.15);
                Primitives.Tube(m, new[] { nodePos, petioleEnd }, new[] { 0.0038, 0.0028 }, 6,
                    (i, v) => (petioleCol, 1.0, i, v, 0, 0));

                // Ovate Hornbeam blade with midrib trough
                int bladeStart = m.VertexCount;
                FoliageBlade.Build(m, petioleEnd, petioleEnd + leafDir * lLen, leafSide, lWidth * 0.5,
                    Primitives.Scale(c1, 0.85), c1, camber: 0.18, curl: 0.12, shoulder: 0.72, segments: 3);

                // Recolor underside to pale satin lime
                for (int v = bladeStart; v < m.VertexCount; v++)
                {
                    if (m.UV2[v * 2] > 0.5f)
                    {
                        m.SetColor(v, colUnder[0], colUnder[1], colUnder[2], 1.0);
                    }
                }
            }

            // Terminal leaf fan crowning the twig (4 leaves)
            for (int tf = 0; tf < 4; tf++)
            {
                double fanAng = (tf - 1.5) * 0.42 + rng.Range(-0.08, 0.08);
                var fDir = Axis.RotateAround(fwd, norm, fanAng);
                var fSide = fDir.Cross(norm).Normalized();
                double fLen = leafScale * rng.Range(0.15, 0.20);
                double fWidth = fLen * 0.56;
                var pEnd = twigPts[nodes] + fDir * (fLen * 0.14);

                int bladeStart = m.VertexCount;
                FoliageBlade.Build(m, pEnd, pEnd + fDir * fLen, fSide, fWidth * 0.5,
                    Primitives.Scale(c1, 0.82), c1, camber: 0.16, curl: 0.10, shoulder: 0.70, segments: 3);
                for (int v = bladeStart; v < m.VertexCount; v++)
                {
                    if (m.UV2[v * 2] > 0.5f)
                        m.SetColor(v, colUnder[0], colUnder[1], colUnder[2], 1.0);
                }
            }

            // Botanical Signature: Pendulous hop-like papery strobile (Ostrya-style)
            if (hasStrobile)
            {
                var strobileAttach = twigPts[nodes / 2];
                var strobileStem = strobileAttach - Vec3.Up * 0.035;
                Primitives.Tube(m, new[] { strobileAttach, strobileStem }, new[] { 0.0028, 0.0022 }, 6,
                    (i, v) => (petioleCol, 1.0, i, v, 0, 0));

                double coneLen = 0.11 * leafScale;
                int bracts = 7;
                for (int bi = 0; bi < bracts; bi++)
                {
                    double bt = (double)bi / (bracts - 1);
                    var bCenter = strobileStem - Vec3.Up * (coneLen * bt);
                    double bRad = (0.028 * Math.Sin(Math.PI * bt) + 0.012) * leafScale;
                    Primitives.Ellipsoid(m, bCenter, new Vec3(bRad, coneLen * 0.22, bRad), 4, 6,
                        (u, v) => (strobileCol, 1.0, u, v, 0.8, 0.0));
                }
            }
        }

        // 3. Wide spreading 3D lace branching lattice from each leader
        int scaffoldCount = juvenile ? 5 : 9;
        for (int l = 0; l < leaderCount; l++)
        {
            var lMid = leaderControls[l];
            var lTip = leaderTips[l];

            for (int s = 0; s < scaffoldCount; s++)
            {
                double st = 0.18 + 0.78 * s / (scaffoldCount - 1);
                var scaffoldBase = Curve(forkPoint, lMid, lTip, st);
                double ang = s * 2.39996 + l * 1.8 + rng.Range(-0.25, 0.25);
                // Spread scaffold branches widely outward into the crown
                var scafDir = (new Vec3(Math.Cos(ang), 0, Math.Sin(ang)) * 0.94 + Vec3.Up * rng.Range(0.04, 0.28)).Normalized();
                double scafLen = (totalH * 0.56 * (1.15 - st * 0.35)) * rng.Range(0.88, 1.15);

                var scafTip = scaffoldBase + scafDir * scafLen;
                var scafCtrl = (scaffoldBase + scafTip) * 0.5 + Vec3.Up * (scafLen * 0.10);
                WoodyCurve(m, scaffoldBase, scafCtrl, scafTip, boleRadius * 0.24 * (1.0 - st * 0.45), 3);

                // Subdivide into fine zig-zag twigs with dense planar leaf sprays
                int twigs = juvenile ? 3 : 4;
                for (int tw = 0; tw < twigs; tw++)
                {
                    double twt = 0.28 + 0.68 * tw / Math.Max(1, twigs - 1);
                    var twStart = Curve(scaffoldBase, scafCtrl, scafTip, twt);
                    double twAng = ang + (tw % 2 == 0 ? 0.70 : -0.70) + rng.Range(-0.15, 0.15);
                    var twDir = (new Vec3(Math.Cos(twAng), 0, Math.Sin(twAng)) * 0.90 + Vec3.Up * rng.Range(-0.1, 0.2)).Normalized();
                    double twLen = scafLen * 0.65 * rng.Range(0.85, 1.15);

                    bool addStrobile = !juvenile && (s % 3 == 0) && (tw == 0);
                    IronlaceLeafSpray(twStart, twDir, twLen, 1.0, addStrobile);
                }
            }

            // Inward-reaching branchlets to fill inner crown summit
            if (!juvenile)
            {
                var inBase = Curve(forkPoint, lMid, lTip, 0.68);
                var inDir = (-lMid.Normalized() * 0.7 + Vec3.Up * 0.7).Normalized();
                var inTip = inBase + inDir * 0.30;
                WoodyCurve(m, inBase, (inBase + inTip) * 0.5 + Vec3.Up * 0.05, inTip, 0.012, 3);
                IronlaceLeafSpray(inTip, inDir, 0.28, 0.95, false);
            }
        }
    }

    /// <summary>
    /// Large cordate (Catalpa-style) shield leaf:
    /// - Indented basal sinus where flexible green petiole joins
    /// - Distinct backward-reaching rounded basal lobes
    /// - Broad shoulders at u ~ 0.32, smoothly tapering to an acute drip tip
    /// - 3D longitudinal droop and transverse camber (shallow trough)
    /// - Two-tone: rich deep green upper surface; dusky, paler warm-olive underside
    /// - Individually large blade spanning 24-32cm in scale
    /// </summary>
    private static void CatalpaLeaf(MeshData m, Vec3 attach, Vec3 petioleDir,
        double petioleLen, double leafLen, double widthRatio,
        double[] colTop, double[] colUnder, double[] petioleCol,
        double camber = 0.20, double droop = 0.16, double roll = 0.0)
    {
        // 1. Flexible green petiole with subtle sag under heavy leaf weight
        var sinus = attach + petioleDir * petioleLen - Vec3.Up * (petioleLen * 0.20);
        var petioleMid = Vec3.Lerp(attach, sinus, 0.5) - Vec3.Up * (petioleLen * 0.08);
        Primitives.Tube(m, new[] { attach, petioleMid, sinus }, new[] { 0.0055, 0.0042, 0.0030 }, 5,
            (i, v) => (petioleCol, 1.0, i, v, 0, 0));

        // 2. Blade orientation: mostly horizontal/outward spread with slight droop
        var forward = (petioleDir - Vec3.Up * (droop * 0.4)).Normalized();
        var up = Vec3.Up;
        var side = forward.Cross(up).Normalized();
        if (side.LengthSq < 1e-6) side = forward.Cross(new Vec3(1, 0, 0)).Normalized();
        up = side.Cross(forward).Normalized();
        if (Math.Abs(roll) > 1e-4)
        {
            side = Axis.RotateAround(side, forward, roll);
            up = side.Cross(forward).Normalized();
        }

        double halfWidth = leafLen * widthRatio * 0.5;

        Vec3 Pos(double u, double v)
        {
            double absV = Math.Abs(v);
            // Heart-shaped basal lobes reach back past the sinus
            double lobeBack = Math.Pow(1.0 - u, 2.0) * Math.Sin(Math.PI * absV) * (leafLen * 0.22);
            double zLocal = u * leafLen - lobeBack;
            // Catalpa width profile: broad shoulder at u ~ 0.32
            double env = Math.Pow(Math.Max(0.0, Math.Sin(Math.PI * Math.Pow(u, 0.60))), 0.74) * (1.0 + 0.20 * (1.0 - u));
            double xLocal = v * halfWidth * env;
            // Natural 3D camber (trough along midrib) and gentle droop
            double midribSag = -leafLen * droop * Math.Pow(u, 1.5);
            double transverseCamber = halfWidth * camber * (1.0 - v * v) - 0.0025 * absV;
            double tipCurl = leafLen * 0.028 * Math.Sin(Math.PI * u) * (1.0 - u);
            double yLocal = midribSag + transverseCamber + tipCurl;
            return sinus + forward * zLocal + side * xLocal + up * yLocal;
        }

        Vec3 Normal(double u, double v)
        {
            const double d = 0.005;
            var du = Pos(Math.Min(1.0, u + d), v) - Pos(Math.Max(0.0, u - d), v);
            var dv = Pos(u, Math.Min(1.0, v + d * 2)) - Pos(u, Math.Max(-1.0, v - d * 2));
            var norm = du.Cross(dv).Normalized();
            return norm.LengthSq < 1e-6 ? up : norm;
        }

        const int Nu = 5;
        const int Nv = 4;
        int stride = Nv + 1;

        for (int face = 0; face < 2; face++)
        {
            double sign = face == 0 ? 1.0 : -1.0;
            var faceNormTarget = face == 0 ? up : -up;
            var faceCol = face == 0 ? colTop : colUnder;
            int start = m.VertexCount;

            for (int i = 0; i < Nu; i++)
            {
                double u = (double)i / Nu;
                for (int j = 0; j <= Nv; j++)
                {
                    double v = (double)j / Nv * 2.0 - 1.0;
                    var p = Pos(u, v);
                    var n = Normal(u, v) * sign;
                    m.AddVertex(p, n, faceCol, 1.0, u, (v + 1.0) * 0.5, face, 1);
                }
            }
            var tipPos = Pos(1.0, 0.0);
            var tipNorm = Normal(0.98, 0.0) * sign;
            int tipIdx = m.AddVertex(tipPos, tipNorm, faceCol, 1.0, 1.0, 0.5, face, 1);

            for (int i = 0; i < Nu - 1; i++)
            {
                for (int j = 0; j < Nv; j++)
                {
                    int a = start + i * stride + j;
                    int b = a + 1;
                    int c = a + stride;
                    int d = c + 1;
                    Primitives.TriangleFacing(m, a, c, b, faceNormTarget);
                    Primitives.TriangleFacing(m, b, c, d, faceNormTarget);
                }
            }
            int lastRowStart = start + (Nu - 1) * stride;
            for (int j = 0; j < Nv; j++)
            {
                int a = lastRowStart + j;
                int b = a + 1;
                Primitives.TriangleFacing(m, a, tipIdx, b, faceNormTarget);
            }
        }
    }

    /// <summary>
    /// Umbraheart: Climax canopy giant of moist fertile ground.
    /// - Muscular tabular buttress root flare diving into soil (Ficus macrophylla / Banyan)
    /// - Low, stout sympodial trunk breaking at ~0.22 height into 4-5 heavy weight-bearing limbs (Samanea saman umbrella)
    /// - Secondary spreading tiers with gentle gravity sag
    /// - Individually large, cambered cordate shield leaves (Catalpa-style) on flexible green petioles
    /// - Two-tone tissue: deep forest-green upper surface, dusky paler warm-olive underside
    /// - Sapling form with single erect stem and light-seeking oversized tender leaves
    /// </summary>
    private static void TreeUmbraheart(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        var colUnder = new[] {
            MathD.Clamp01(c2[0] * 0.86 + 0.06),
            MathD.Clamp01(c2[1] * 0.82 + 0.04),
            MathD.Clamp01(c2[2] * 0.65 + 0.03)
        };
        var petioleCol = Primitives.Mix(c1, WoodyBark, 0.35);

        if (juvenile)
        {
            // Juvenile sapling: single upright leader with large light-seeking cordate leaves
            var saplingTrunk = TreeBole(m, rng, 0.024, 0.44, 0.025);
            int leaves = 7 + rng.NextInt(3);
            for (int i = 0; i < leaves; i++)
            {
                double t = 0.30 + 0.66 * i / Math.Max(1, leaves - 1);
                var attach = Along(saplingTrunk, t);
                double ang = i * 2.39996 + rng.Range(-0.2, 0.2);
                var petioleDir = (new Vec3(Math.Cos(ang), 0.25 + rng.Range(-0.08, 0.12), Math.Sin(ang))).Normalized();
                var colTop = Primitives.Mix(c1, c2, rng.Range(0.15, 0.38));
                CatalpaLeaf(m, attach, petioleDir,
                    petioleLen: rng.Range(0.055, 0.075),
                    leafLen: rng.Range(0.20, 0.25),
                    widthRatio: rng.Range(0.78, 0.86),
                    colTop: colTop, colUnder: colUnder, petioleCol: petioleCol,
                    camber: rng.Range(0.16, 0.24), droop: rng.Range(0.12, 0.20),
                    roll: rng.Range(-0.25, 0.25));
            }
            var top = saplingTrunk[^1];
            for (int a = 0; a < 3; a++)
            {
                double ang = a * Math.PI * 2.0 / 3.0 + rng.Range(-0.2, 0.2);
                var petioleDir = (new Vec3(Math.Cos(ang) * 0.6, 0.65, Math.Sin(ang) * 0.6)).Normalized();
                var colTop = Primitives.Mix(c1, c2, 0.45);
                CatalpaLeaf(m, top, petioleDir,
                    petioleLen: 0.045, leafLen: 0.16, widthRatio: 0.80,
                    colTop: colTop, colUnder: colUnder, petioleCol: petioleCol,
                    camber: 0.18, droop: 0.12, roll: 0.0);
            }
            return;
        }

        // --- Mature Climax Tree ---
        double boleRadius = 0.135;
        double forkHeight = 0.42;
        var trunk = TreeBole(m, rng, boleRadius, forkHeight, 0.02);

        // 1. Muscular tabular buttress roots flaring out from the base into the soil
        double phase = rng.Range(0, Math.PI * 2);
        int buttresses = 5;
        for (int r = 0; r < buttresses; r++)
        {
            double a = phase + r * (Math.PI * 2.0 / buttresses) + rng.Range(-0.18, 0.18);
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            double reach = boleRadius * rng.Range(2.4, 3.2);
            Wood(m, new[] {
                dir * (boleRadius * 0.35) + new Vec3(0, 0.095, 0),
                dir * (reach * 0.48) + new Vec3(0, 0.022, 0),
                dir * reach + new Vec3(0, -0.015, 0)
            }, new[] { boleRadius * 0.52, boleRadius * 0.24, 0.003 }, 6);
        }

        // 2. Primary Scaffolds: 4 to 5 massive, weight-bearing limbs (Samanea saman umbrella)
        int leaders = 4 + (rng.NextDouble() < 0.4 ? 1 : 0);
        var leaderTips = new Vec3[leaders];
        var leaderRoots = new Vec3[leaders];
        var leaderControls = new Vec3[leaders];
        var leaderRadials = new Vec3[leaders];

        for (int leader = 0; leader < leaders; leader++)
        {
            double a = phase + leader * Math.PI * 2.0 / leaders + rng.Range(-0.20, 0.20);
            var radial = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            leaderRadials[leader] = radial;
            var root = Along(trunk, 0.72 + leader * (0.24 / Math.Max(1, leaders - 1)));
            double spread = rng.Range(0.68, 0.82);
            // The high fork lets the hanging canopy clear the shrub layer.
            var tip = root + radial * spread + Vec3.Up * rng.Range(0.30, 0.38);
            var control = root + radial * (spread * 0.42) + Vec3.Up * rng.Range(0.38, 0.46);
            WoodyCurve(m, root, control, tip, boleRadius * 0.58, 5);

            leaderRoots[leader] = root;
            leaderControls[leader] = control;
            leaderTips[leader] = tip;

            // Swollen branch collar knot on lower scaffold
            var knotPos = Curve(root, control, tip, 0.25) - radial * 0.015 - Vec3.Up * 0.02;
            Wood(m, new[] { knotPos, knotPos + radial * 0.025 - Vec3.Up * 0.01 }, new[] { 0.024, 0.012 }, 5);
        }

        // 3. Central canopy fill: major interior branches that grow back INTO the center of the crown
        // Broad-crowned trees send centripetal limbs back into the central light gap to capture zenith sunlight.
        int centerLimbs = Math.Min(3, leaders);
        for (int ci = 0; ci < centerLimbs; ci++)
        {
            int pLeader = ci * (leaders / centerLimbs);
            var pRadial = leaderRadials[pLeader];
            var pSide = pRadial.Cross(Vec3.Up);
            double sign = ci % 2 == 0 ? 1.0 : -1.0;

            // Arises from the scaffold shoulder (~0.40 along limb) and arches back inward & upward into the crown summit
            var branchRoot = Curve(leaderRoots[pLeader], leaderControls[pLeader], leaderTips[pLeader], 0.40);
            double hubY = 0.76 + ci * 0.03 + rng.Range(-0.02, 0.02);
            var hubPos = new Vec3(pRadial.X * 0.03 + pSide.X * (sign * 0.05), hubY, pRadial.Z * 0.03 + pSide.Z * (sign * 0.05));
            var branchCtrl = Vec3.Lerp(branchRoot, hubPos, 0.48) + Vec3.Up * 0.04;
            WoodyCurve(m, branchRoot, branchCtrl, hubPos, 0.028, 4);

            // From this central branch hub, twigs radiate horizontally across the interior light cone
            for (int r = 0; r < 4; r++)
            {
                double rAngle = phase + ci * 1.9 + r * (Math.PI * 2.0 / 4.0) + rng.Range(-0.12, 0.12);
                var rDir = new Vec3(Math.Cos(rAngle), rng.Range(-0.02, 0.03), Math.Sin(rAngle)).Normalized();
                double rLen = rng.Range(0.16, 0.24);
                var rTip = hubPos + rDir * rLen;
                var rMid = (hubPos + rTip) * 0.5 + Vec3.Up * 0.010;
                WoodyCurve(m, hubPos, rMid, rTip, 0.011, 3);

                // Leaf node along radiating twig: broad, horizontal, facing zenith to capture sunlight
                var midNode = Curve(hubPos, rMid, rTip, 0.52);
                foreach (double sm in new[] { -1.0, 1.0 })
                {
                    var leafDir = (rDir * 0.45 + rDir.Cross(Vec3.Up) * sm * 0.88 + Vec3.Up * rng.Range(0.01, 0.05)).Normalized();
                    var colTop = Primitives.Scale(c1, rng.Range(0.88, 1.06));
                    CatalpaLeaf(m, midNode, leafDir,
                        petioleLen: rng.Range(0.050, 0.070),
                        leafLen: rng.Range(0.24, 0.30),
                        widthRatio: rng.Range(0.80, 0.88),
                        colTop: colTop, colUnder: colUnder, petioleCol: petioleCol,
                        camber: 0.16, droop: 0.05, roll: rng.Range(-0.05, 0.05));
                }

                // Terminal leaf at twig tip radiating outward horizontally
                var termLeafDir = (rDir + Vec3.Up * rng.Range(0.01, 0.05)).Normalized();
                var termCol = Primitives.Scale(c1, rng.Range(0.90, 1.08));
                CatalpaLeaf(m, rTip, termLeafDir,
                    petioleLen: rng.Range(0.046, 0.064),
                    leafLen: rng.Range(0.24, 0.30),
                    widthRatio: rng.Range(0.80, 0.88),
                    colTop: termCol, colUnder: colUnder, petioleCol: petioleCol,
                    camber: 0.16, droop: 0.05, roll: 0.0);
            }
        }

        // 4. Secondary spreading lateral limbs & outer weeping twigs with large Catalpa leaves
        int totalBranches = leaders * 2;
        for (int k = 0; k < totalBranches; k++)
        {
            int parent = k % leaders;
            double fraction = (k / leaders) == 0 ? 0.30 : 0.70;
            double stemT = 0.36 + fraction * 0.44 + rng.Range(-0.04, 0.04);
            var root = Curve(leaderRoots[parent], leaderControls[parent], leaderTips[parent], stemT);

            var parentRadial = leaderRadials[parent];
            var parentSideways = parentRadial.Cross(Vec3.Up);
            double sign = (k / leaders) % 2 == 0 ? -1 : 1;
            double branchAngle = rng.Range(0.38, 0.72) * sign;
            var branchDir = (parentRadial * Math.Cos(branchAngle) + parentSideways * Math.Sin(branchAngle)).Normalized();
            var branchSide = branchDir.Cross(Vec3.Up);

            double length = rng.Range(0.36, 0.50);
            // Outer limbs dip under their foliage, but leave room for shrubs below.
            var tip = root + branchDir * length - Vec3.Up * rng.Range(0.03, 0.10);
            var control = root + branchDir * (length * 0.46) + Vec3.Up * rng.Range(0.01, 0.04) + branchSide * rng.Range(-0.03, 0.03);
            WoodyCurve(m, root, control, tip, 0.034, 4);

            for (int tw = 0; tw < 2; tw++)
            {
                double twT = 0.50 + tw * 0.40;
                var twJunction = Curve(root, control, tip, twT);
                double twSign = tw % 2 == 0 ? -1 : 1;
                // Outer twigs cascade downward to bring the canopy edge down around the outside
                var twDir = (branchDir * 0.60 + branchSide * twSign * rng.Range(0.40, 0.75) - Vec3.Up * rng.Range(0.18, 0.32)).Normalized();
                double twLen = rng.Range(0.20, 0.28);
                var twTip = twJunction + twDir * twLen;
                var twMid = (twJunction + twTip) * 0.5;
                WoodyCurve(m, twJunction, twMid, twTip, 0.009, 3);

                // 1 node with opposite pair of large Catalpa leaves
                var nodePos = Curve(twJunction, twMid, twTip, 0.45);
                foreach (double sideMult in new[] { -1.0, 1.0 })
                {
                    var leafDir = (twDir * 0.55 + twDir.Cross(Vec3.Up) * sideMult * rng.Range(0.65, 0.90)
                        - Vec3.Up * rng.Range(0.18, 0.35)).Normalized();
                    var colTop = Primitives.Scale(c1, rng.Range(0.88, 1.08));
                    CatalpaLeaf(m, nodePos, leafDir,
                        petioleLen: rng.Range(0.055, 0.075),
                        leafLen: rng.Range(0.23, 0.29),
                        widthRatio: rng.Range(0.80, 0.88),
                        colTop: colTop, colUnder: colUnder, petioleCol: petioleCol,
                        camber: rng.Range(0.18, 0.24),
                        droop: rng.Range(0.20, 0.30),
                        roll: rng.Range(-0.25, 0.25));
                }

                // Terminal fan of 2 large Catalpa leaves cascading at twig tip
                for (int tf = 0; tf < 2; tf++)
                {
                    double tfAngle = (tf == 0 ? -1 : 1) * 0.45 + rng.Range(-0.1, 0.1);
                    var tfSide = twDir.Cross(Vec3.Up);
                    var leafDir = (twDir * Math.Cos(tfAngle) + tfSide * Math.Sin(tfAngle)
                        - Vec3.Up * rng.Range(0.20, 0.40)).Normalized();
                    var colTop = Primitives.Scale(c1, rng.Range(0.90, 1.10));
                    CatalpaLeaf(m, twTip, leafDir,
                        petioleLen: rng.Range(0.050, 0.068),
                        leafLen: rng.Range(0.23, 0.28),
                        widthRatio: rng.Range(0.80, 0.88),
                        colTop: colTop, colUnder: colUnder, petioleCol: petioleCol,
                        camber: rng.Range(0.18, 0.24),
                        droop: rng.Range(0.20, 0.32),
                        roll: rng.Range(-0.25, 0.25));
                }
            }
        }

        // 5. Outer weeping skirt branches on primary scaffolds: reaches low to complete the sheltering umbrella perimeter
        for (int leader = 0; leader < leaders; leader++)
        {
            var radial = leaderRadials[leader];
            var sideways = radial.Cross(Vec3.Up);
            var skirtRoot = Curve(leaderRoots[leader], leaderControls[leader], leaderTips[leader], 0.70);
            var skirtDir = (radial * 0.50 + sideways * (leader % 2 == 0 ? 0.45 : -0.45) - Vec3.Up * 0.48).Normalized();
            double skirtLen = rng.Range(0.20, 0.28);
            var skirtTip = skirtRoot + skirtDir * skirtLen;
            WoodyCurve(m, skirtRoot, (skirtRoot + skirtTip) * 0.5, skirtTip, 0.012, 3);

            // Pair of hanging perimeter leaves on skirt branch
            foreach (double sm in new[] { -1.0, 1.0 })
            {
                var leafDir = (skirtDir * 0.60 + skirtDir.Cross(Vec3.Up) * sm * 0.65 - Vec3.Up * 0.35).Normalized();
                var colTop = Primitives.Scale(c1, rng.Range(0.88, 1.06));
                CatalpaLeaf(m, skirtTip, leafDir,
                    petioleLen: rng.Range(0.050, 0.070),
                    leafLen: rng.Range(0.24, 0.30),
                    widthRatio: rng.Range(0.80, 0.88),
                    colTop: colTop, colUnder: colUnder, petioleCol: petioleCol,
                    camber: 0.20, droop: 0.30, roll: rng.Range(-0.3, 0.3));
            }
        }

        // 6. Scaffold crown terminals: horizontal spreading fans completing the mid/outer umbrella dome
        for (int j = 0; j < leaders; j++)
        {
            var crown = leaderTips[j];
            var radial = leaderRadials[j];
            var side = radial.Cross(Vec3.Up);
            for (int cl = -1; cl <= 1; cl++)
            {
                double clAngle = cl * 0.60 + rng.Range(-0.10, 0.10);
                var fDir = (radial * Math.Cos(clAngle) + side * Math.Sin(clAngle) - Vec3.Up * rng.Range(0.08, 0.18)).Normalized();
                double fLen = rng.Range(0.14, 0.20);
                var fTip = crown + fDir * fLen;
                WoodyCurve(m, crown, (crown + fTip) * 0.5, fTip, 0.010, 3);

                // Leaf pair at node
                var fNode = (crown + fTip) * 0.5;
                foreach (double sm in new[] { -1.0, 1.0 })
                {
                    var lDir = (fDir * 0.55 + fDir.Cross(Vec3.Up) * sm * 0.80 - Vec3.Up * rng.Range(0.10, 0.22)).Normalized();
                    CatalpaLeaf(m, fNode, lDir,
                        petioleLen: rng.Range(0.048, 0.068),
                        leafLen: rng.Range(0.23, 0.29),
                        widthRatio: rng.Range(0.80, 0.88),
                        colTop: Primitives.Scale(c1, rng.Range(0.88, 1.08)), colUnder: colUnder, petioleCol: petioleCol,
                        camber: 0.18, droop: 0.16, roll: rng.Range(-0.15, 0.15));
                }

                // Terminal tip leaf
                CatalpaLeaf(m, fTip, (fDir - Vec3.Up * 0.12).Normalized(),
                    petioleLen: rng.Range(0.045, 0.062),
                    leafLen: rng.Range(0.23, 0.29),
                    widthRatio: rng.Range(0.80, 0.88),
                    colTop: Primitives.Scale(c1, rng.Range(0.90, 1.10)), colUnder: colUnder, petioleCol: petioleCol,
                    camber: 0.18, droop: 0.16, roll: 0.0);
            }
        }
    }
    private static void TreeKiteleaf(MeshData m, Rng rng, double[] c1, double[] c2) => BroadleafTree(m, rng, c1, c2, 2);

    /// <summary>
    /// Fenneedle: Wetland conifer (Larix laricina / Tamarack &amp; Taxodium distichum / Bald Cypress).
    /// - Continuous straight excurrent central leader trunk from ground to terminal spike.
    /// - Fluted basal trunk buttress flanges diving into wetland soil.
    /// - 8 to 9 horizontal scaffold whorls with smooth, irregular organic curves (no kinks).
    /// - Dedicated terminal drooping twig weeping at the end of each branch.
    /// - Foliage:
    ///   - Long, wide, smoothly curved and overlapping blade-shaped needles.
    ///   - Feathery distichous sprays along weeping lateral branchlets and terminal weeping tips.
    ///   - Dense short-spur rosettes (brachyblasts) packed along older scaffold wood.
    ///   - Ascending needle plume clothing the terminal leader spike.
    /// - Rich two-tone color: vibrant fresh spring-green highlights (c2) and deep blue-green interior (c1).
    /// - Authored in unit-normalized bounds [0..1] for height and radius.
    /// </summary>
    private static void TreeFenneedle(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        double totalH = 1.0;
        double boleRadius = juvenile ? 0.035 : 0.070;
        double spreadScale = juvenile ? 0.50 : 1.0;

        // 1. Continuous straight excurrent central bole
        const int trunkSegs = 8;
        var trunkPath = new Vec3[trunkSegs + 1];
        var trunkRadii = new double[trunkSegs + 1];
        double driftAng = rng.Range(0, Math.PI * 2);
        var driftDir = new Vec3(Math.Cos(driftAng), 0, Math.Sin(driftAng));

        for (int i = 0; i <= trunkSegs; i++)
        {
            double t = (double)i / trunkSegs;
            var drift = driftDir * (t * t * 0.035);
            trunkPath[i] = new Vec3(drift.X, totalH * t, drift.Z);
            trunkRadii[i] = boleRadius * (0.09 + 0.91 * Math.Pow(1.0 - t, 0.82)) * (i == 0 ? 1.45 : 1.0);
        }
        Wood(m, trunkPath, trunkRadii, 8);

        // Fluted basal buttresses diving into wetland peat
        int buttressCount = juvenile ? 4 : 6;
        for (int b = 0; b < buttressCount; b++)
        {
            double a = driftAng + b * (Math.PI * 2 / buttressCount) + rng.Range(-0.18, 0.18);
            var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            double reach = boleRadius * rng.Range(2.2, 3.2);
            var b0 = dir * (boleRadius * 0.75) + new Vec3(0, totalH * 0.045, 0);
            var b1 = dir * (reach * 0.55) + new Vec3(0, 0.012, 0);
            var b2 = dir * reach + new Vec3(0, -0.010, 0);
            Wood(m, new[] { b0, b1, b2 }, new[] { boleRadius * 0.45, boleRadius * 0.22, 0.003 }, 6);
        }

        // Helper for building the drooping twig at the branch tip
        void DroopingTwig(Vec3 start, Vec3 dir, double length)
        {
            var fwd = dir.Normalized();
            var side = fwd.Cross(Vec3.Up).Normalized();
            if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);

            // 4 curved segments gracefully drooping under gravity
            const int segs = 4;
            var twigPath = new Vec3[segs + 1];
            var twigRadii = new double[segs + 1];
            for (int i = 0; i <= segs; i++)
            {
                double t = (double)i / segs;
                // Arch outward slightly, then weep downward
                double fwdReach = length * Math.Sin(t * Math.PI * 0.5) * 0.55;
                double drop = length * Math.Pow(t, 1.4) * 0.72;
                twigPath[i] = start + fwd * fwdReach - Vec3.Up * drop;
                twigRadii[i] = 0.006 * (1.0 - 0.65 * t);
            }
            Wood(m, twigPath, twigRadii, 6);

            // Wide, long, smoothly curved and overlapping blade-shapes along the drooping twig
            int bladeCount = juvenile ? 8 : 12;
            for (int i = 0; i < bladeCount; i++)
            {
                double t = 0.10 + 0.85 * i / (bladeCount - 1);
                int segIdx = Math.Min(segs - 1, (int)(t * segs));
                double localT = (t * segs) - segIdx;
                var node = Vec3.Lerp(twigPath[segIdx], twigPath[segIdx + 1], localT);

                double sign = i % 2 == 0 ? -1.0 : 1.0;
                var bladeDir = (side * (sign * 0.85) + fwd * 0.35 - Vec3.Up * 0.38).Normalized();
                var bladeSide = bladeDir.Cross(Vec3.Up).Normalized();

                double bLen = rng.Range(0.12, 0.16) * (1.0 - 0.25 * t) * spreadScale;
                double bHalfWidth = bLen * 0.24; // wide blade shape

                var bladeCol = Primitives.Mix(c1, c2, rng.Range(0.25, 0.65));

                FoliageBlade.Build(m, node, node + bladeDir * bLen, bladeSide, bHalfWidth,
                    Primitives.Scale(bladeCol, 0.78), bladeCol,
                    camber: 0.18, curl: 0.22, shoulder: 0.80, segments: 4);
            }
        }

        // 2. Horizontal scaffold tiers with irregular organic curves
        int numTiers = juvenile ? 5 : 8;
        double phase = rng.Range(0, Math.PI * 2);

        for (int tier = 0; tier < numTiers; tier++)
        {
            double tierRel = (double)tier / (numTiers - 1);
            // Elevation of tier along the bole (from 0.20 to 0.84)
            double tierH = totalH * (0.20 + tierRel * 0.64);
            int branches = tier < 3 ? 6 : tier < 6 ? 5 : 4;
            // Spreading branch reach (wider at base, tapering towards crown top)
            double reach = (0.75 - tierRel * 0.50) * spreadScale * rng.Range(0.92, 1.08);

            for (int b = 0; b < branches; b++)
            {
                double angle = phase + b * Math.PI * 2 / branches + tier * 0.75 + rng.Range(-0.16, 0.16);
                var radial = new Vec3(Math.Cos(angle), 0, Math.Sin(angle));
                var side = radial.Cross(Vec3.Up).Normalized();

                var root = new Vec3(0, tierH, 0);
                double branchR = boleRadius * 0.36 * (1.0 - tierRel * 0.50);

                // Smooth, irregular organic curve (5 segments, NO 30-degree kink)
                const int bSegs = 5;
                var branchPath = new Vec3[bSegs + 1];
                var branchRadii = new double[bSegs + 1];
                for (int pt = 0; pt <= bSegs; pt++)
                {
                    double t = (double)pt / bSegs;
                    // Gentle collar vault, then natural smooth organic waver and slight sag
                    double yLift = (reach * 0.08) * Math.Sin(Math.PI * Math.Pow(t, 0.70))
                                   - (reach * 0.04) * (t * t)
                                   + rng.Range(-0.005, 0.005);
                    double sCurve = (reach * 0.04) * Math.Sin(t * Math.PI * 1.5) * (b % 2 == 0 ? 1.0 : -1.0);
                    branchPath[pt] = root + radial * (reach * t) + side * sCurve + Vec3.Up * yLift;
                    branchRadii[pt] = branchR * (0.16 + 0.84 * Math.Pow(1.0 - t, 0.75));
                }
                Wood(m, branchPath, branchRadii, 6);

                // Distichous overlapping blade pairs along the scaffold branch wood
                int leafNodes = juvenile ? 5 : 8;
                for (int n = 1; n < leafNodes; n++)
                {
                    double nt = (double)n / leafNodes;
                    int ptIdx = Math.Min(bSegs - 1, (int)(nt * bSegs));
                    double localT = (nt * bSegs) - ptIdx;
                    var node = Vec3.Lerp(branchPath[ptIdx], branchPath[ptIdx + 1], localT);

                    // A pair of wide curved blades spreading laterally on each side of the branch
                    foreach (double sign in new[] { -1.0, 1.0 })
                    {
                        var bDir = (side * sign * 0.85 + radial * 0.45 + Vec3.Up * rng.Range(0.05, 0.20)).Normalized();
                        var bSide = bDir.Cross(Vec3.Up).Normalized();
                        double bLen = rng.Range(0.12, 0.16) * (1.1 - nt * 0.25) * spreadScale;
                        double bHalfWidth = bLen * 0.24;
                        var bCol = Primitives.Mix(c1, c2, rng.Range(0.20, 0.50));

                        FoliageBlade.Build(m, node, node + bDir * bLen, bSide, bHalfWidth,
                            Primitives.Scale(bCol, 0.78), bCol,
                            camber: 0.16, curl: 0.18, shoulder: 0.80, segments: 4);
                    }
                }

                // Short-spur rosettes (brachyblasts) along older scaffold wood near the trunk
                int spurCount = juvenile ? 2 : 3;
                for (int spur = 0; spur < spurCount; spur++)
                {
                    double st = 0.18 + spur * 0.16;
                    int ptIdx = Math.Min(bSegs - 1, (int)(st * bSegs));
                    double localT = (st * bSegs) - ptIdx;
                    var spurNode = Vec3.Lerp(branchPath[ptIdx], branchPath[ptIdx + 1], localT);

                    var spurColor = Primitives.Mix(c1, c2, rng.Range(0.18, 0.42));
                    int needlesInRosette = juvenile ? 6 : 9;

                    for (int rn = 0; rn < needlesInRosette; rn++)
                    {
                        double a = rn * Math.PI * 2 / needlesInRosette + spur * 0.6;
                        var rDir = (side * Math.Cos(a) + Vec3.Up * (Math.Sin(a) * 0.65 + 0.25) + radial * 0.25).Normalized();
                        double rLen = rng.Range(0.08, 0.12) * spreadScale;
                        FoliageBlade.Build(m, spurNode, spurNode + rDir * rLen, rDir.Cross(radial),
                            rLen * 0.22, Primitives.Scale(spurColor, 0.76), spurColor,
                            camber: 0.14, curl: 0.16, shoulder: 0.85, segments: 4);
                    }
                }

                // ONE dedicated drooping twig at the end of each branch!
                var branchTip = branchPath[bSegs];
                var termDir = (radial * 0.75 - Vec3.Up * 0.15 + side * rng.Range(-0.15, 0.15)).Normalized();
                double termLen = (0.20 - tierRel * 0.06) * spreadScale * rng.Range(0.92, 1.10);
                DroopingTwig(branchTip, termDir, termLen);
            }
        }

        // 3. Terminal leader plume clothing the top spike
        double topStart = totalH * 0.82;
        int crownNeedles = juvenile ? 16 : 26;
        for (int i = 0; i < crownNeedles; i++)
        {
            double t = (double)i / crownNeedles;
            double y = topStart + t * (totalH - topStart);
            double a = i * 2.39996 + phase;
            var dir = (new Vec3(Math.Cos(a), 0, Math.Sin(a)) * 0.58 + Vec3.Up * 0.82).Normalized();
            var node = new Vec3(0, y, 0);
            double len = rng.Range(0.10, 0.15) * (1.0 - t * 0.30) * spreadScale;
            var tipCol = Primitives.Mix(c1, c2, 0.65 + 0.35 * t);
            FoliageBlade.Build(m, node, node + dir * len, dir.Cross(Vec3.Up),
                len * 0.22, Primitives.Scale(tipCol, 0.78), tipCol,
                camber: 0.12, curl: 0.15, shoulder: 0.85, segments: 4);
        }
    }

    private static void EmbercrownLeaflet(MeshData m, Vec3 root, Vec3 lDir, Vec3 lSide, Vec3 lNorm,
        double len, double width, double[] c1, double[] c2, Rng rng)
    {
        var v0 = root;
        var v1 = root + lDir * (len * 0.36) - lSide * (width * 0.5) + lNorm * (len * 0.04);
        var v2 = root + lDir * (len * 0.48) - lNorm * (len * 0.015);
        var v3 = root + lDir * (len * 0.36) + lSide * (width * 0.5) + lNorm * (len * 0.04);
        var v4 = root + lDir * len - lNorm * (len * 0.035);

        var colTop = Primitives.Mix(c1, c2, rng.Range(0.08, 0.24));
        var colUnder = Primitives.Mix(Primitives.Scale(c2, 0.82), new[] { 0.42, 0.52, 0.32 }, 0.45);

        void LeafFace(Vec3 a, Vec3 b, Vec3 c, double[] col, Vec3 upTarget, int faceIdx)
        {
            var norm = (b - a).Cross(c - a).Normalized();
            if (norm.Dot(upTarget) < 0)
            {
                var tmp = b; b = c; c = tmp;
                norm = -norm;
            }
            int ia = m.AddVertex(a, norm, col, 1.0, 0.2, 0.5, faceIdx, 1.0);
            int ib = m.AddVertex(b, norm, col, 1.0, 0.5, 0.2, faceIdx, 1.0);
            int ic = m.AddVertex(c, norm, col, 1.0, 0.8, 0.5, faceIdx, 1.0);
            m.AddTriangle(ia, ib, ic);
        }

        // Upper face (UV2.y = 1.0, normal towards +lNorm)
        LeafFace(v0, v1, v2, colTop, lNorm, 0);
        LeafFace(v0, v2, v3, colTop, lNorm, 0);
        LeafFace(v2, v1, v4, colTop, lNorm, 0);
        LeafFace(v2, v4, v3, colTop, lNorm, 0);

        // Lower face (UV2.y = 1.0, normal towards -lNorm)
        LeafFace(v0, v1, v2, colUnder, -lNorm, 1);
        LeafFace(v0, v2, v3, colUnder, -lNorm, 1);
        LeafFace(v2, v1, v4, colUnder, -lNorm, 1);
        LeafFace(v2, v4, v3, colUnder, -lNorm, 1);
    }

    private static void EmbercrownCompoundLeaf(MeshData m, Vec3 root, Vec3 dir, double length,
        double[] c1, double[] c2, Rng rng)
    {
        // 1. Rachis curve: arches horizontally with a graceful vault, leveling off and weeping at tip
        var p0 = root;
        var p1 = root + dir * (length * 0.30) + Vec3.Up * 0.038;
        var p2 = root + dir * (length * 0.65) - Vec3.Up * 0.018;
        var p3 = root + dir * length - Vec3.Up * 0.080;

        var rachisPath = new[] { p0, p1, p2, p3 };
        var rachisRadii = new[] { 0.0042, 0.0034, 0.0024, 0.0014 };
        var petioleCol = Primitives.Mix(WoodyBarkLight, c1, 0.45);
        Primitives.Tube(m, rachisPath, rachisRadii, 5, (i, v) => (petioleCol, 1, i / 3.0, v, 0, 0));

        // 2. Dense leaflet pairs along rachis: 12 pairs closely spaced along the stem
        int pairs = 12;
        for (int j = 0; j < pairs; j++)
        {
            double t = 0.12 + 0.82 * j / (pairs - 1);
            double t1 = 1.0 - t;
            var pos = p0 * (t1 * t1 * t1) + p1 * (3 * t1 * t1 * t) + p2 * (3 * t1 * t * t) + p3 * (t * t * t);
            var d = (p1 - p0) * (3 * t1 * t1) + (p2 - p1) * (6 * t1 * t) + (p3 - p2) * (3 * t * t);
            var fwd = d.Normalized();
            var up = Vec3.Up;
            var side = fwd.Cross(up).Normalized();
            var rachisNorm = side.Cross(fwd).Normalized();

            // Broad, dense leaflets that overlap their neighbors
            double span = length * (0.24 + 0.15 * Math.Sin(Math.PI * (t - 0.08) / 0.88));
            double width = span * 0.44;

            foreach (double sgn in new[] { -1.0, 1.0 })
            {
                var lDir = (side * sgn * 0.82 + fwd * 0.52 + rachisNorm * 0.18).Normalized();
                var lSide = lDir.Cross(rachisNorm).Normalized();
                var lNorm = lSide.Cross(lDir).Normalized();

                EmbercrownLeaflet(m, pos, lDir, lSide, lNorm, span, width, c1, c2, rng);
            }
        }

        // Terminal leaflet at tip
        var termD = (p3 - p2).Normalized();
        var termSide = termD.Cross(Vec3.Up).Normalized();
        var termNorm = termSide.Cross(termD).Normalized();
        double termSpan = length * 0.25;
        EmbercrownLeaflet(m, p3, termD, termSide, termNorm, termSpan, termSpan * 0.44, c1, c2, rng);
    }

    private static void EmbercrownBerry(MeshData m, Vec3 center, double r, Rng rng)
    {
        var pTop = center + Vec3.Up * r;
        var pBot = center - Vec3.Up * r;
        var pE = center + new Vec3(r, 0, 0);
        var pW = center + new Vec3(-r, 0, 0);
        var pN = center + new Vec3(0, 0, r);
        var pS = center + new Vec3(0, 0, -r);

        var colTop = new[] { 0.98, rng.Range(0.20, 0.28), 0.05 };
        var colMid = new[] { 0.88, rng.Range(0.06, 0.10), 0.04 };
        var colBot = new[] { 0.48, 0.03, 0.02 };

        void Tri(Vec3 a, Vec3 b, Vec3 c, double[] ca, double[] cb, double[] cc)
        {
            var norm = (b - a).Cross(c - a).Normalized();
            if (norm.Dot(a - center) < 0)
            {
                var tmp = b; b = c; c = tmp;
                var tc = cb; cb = cc; cc = tc;
                norm = -norm;
            }
            int ia = m.AddVertex(a, norm, ca, 1.0, 0.5, 0.5, 1.0, 0.0);
            int ib = m.AddVertex(b, norm, cb, 1.0, 0.5, 0.5, 1.0, 0.0);
            int ic = m.AddVertex(c, norm, cc, 1.0, 0.5, 0.5, 1.0, 0.0);
            m.AddTriangle(ia, ib, ic);
        }

        Tri(pTop, pN, pE, colTop, colMid, colMid);
        Tri(pTop, pW, pN, colTop, colMid, colMid);
        Tri(pTop, pS, pW, colTop, colMid, colMid);
        Tri(pTop, pE, pS, colTop, colMid, colMid);

        Tri(pBot, pE, pN, colBot, colMid, colMid);
        Tri(pBot, pN, pW, colBot, colMid, colMid);
        Tri(pBot, pW, pS, colBot, colMid, colMid);
        Tri(pBot, pS, pE, colBot, colMid, colMid);
    }

    private static void EmbercrownBerrySpike(MeshData m, Vec3 root, double spikeHeight, Rng rng)
    {
        var axis = Vec3.Up;
        double baseRadius = 0.034;
        var topPos = root + axis * spikeHeight;

        var spindleCol = new[] { 0.42, 0.07, 0.05 };
        Primitives.Tube(m, new[] { root, (root + topPos) * 0.5, topPos },
            new[] { 0.007, 0.0045, 0.002 }, 5, (i, v) => (spindleCol, 1, i / 2.0, v, 0, 0));

        int tiers = 8;
        double phase = rng.Range(0, Math.PI * 2);
        for (int tier = 0; tier < tiers; tier++)
        {
            double t = 0.06 + 0.88 * tier / (tiers - 1);
            var tierCenter = root + axis * (spikeHeight * t);
            double tierR = baseRadius * (1.0 - 0.70 * t) * (0.90 + 0.24 * Math.Sin(Math.PI * t));
            int berryCount = Math.Max(4, (int)Math.Round(9 - 5 * t));
            double bR = 0.0135 * (1.0 - 0.28 * t);

            for (int b = 0; b < berryCount; b++)
            {
                double ang = phase + tier * 1.9 + b * (Math.PI * 2.0 / berryCount) + rng.Range(-0.08, 0.08);
                var bDir = new Vec3(Math.Cos(ang), rng.Range(-0.04, 0.04), Math.Sin(ang)).Normalized();
                var bPos = tierCenter + bDir * (tierR * rng.Range(0.92, 1.05));

                Primitives.Tube(m, new[] { tierCenter, bPos }, new[] { 0.0016, 0.0016 }, 4,
                    (i, v) => (spindleCol, 1, i, v, 0, 0));

                EmbercrownBerry(m, bPos, bR, rng);
            }
        }

        EmbercrownBerry(m, topPos + axis * 0.008, 0.010, rng);
    }

    /// <summary>
    /// Embercrown: Fast clonal thicket-forming shrub with splaying canes, dense multi-tier umbrella leaf whorls,
    /// and upright torches of glossy ember-red berries (Rhus typhina / Sorbus aucuparia).
    /// - Outer canes splay outward near the ground before elbowing upright
    /// - Each cane tip bears a dense two-tier umbrella of long pinnately compound leaves (10-11 leaves per cane)
    /// - Along the canes, multiple alternate compound leaves furnish the middle volume
    /// - Above each leaf umbrella, an erect bright-red berry spire points straight up
    /// </summary>
    private static void ShrubEmbercrown(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        var barkCol = Primitives.Mix(WoodyBark, new[] { 0.44, 0.22, 0.16 }, 0.45);

        if (juvenile)
        {
            int shoots = 2 + rng.NextInt(2);
            for (int s = 0; s < shoots; s++)
            {
                double a = s * Math.PI * 2.0 / shoots + rng.Range(-0.25, 0.25);
                var dir = new Vec3(Math.Cos(a), 0, Math.Sin(a));
                double reach = rng.Range(0.20, 0.30);
                double h = rng.Range(0.38, 0.52);
                var root = dir * 0.02;
                var mid = dir * (reach * 0.7) + Vec3.Up * (h * 0.45);
                var top = dir * reach + Vec3.Up * h;
                Primitives.Tube(m, new[] { root, mid, top }, new[] { 0.016, 0.012, 0.008 }, 5,
                    (i, v) => (barkCol, 1, i / 2.0, v, 2, 0));

                // 5 dense juvenile leaves
                for (int l = 0; l < 5; l++)
                {
                    double la = a + (l - 2.0) * 0.75 + rng.Range(-0.12, 0.12);
                    var lDir = (new Vec3(Math.Cos(la), 0.12, Math.Sin(la))).Normalized();
                    EmbercrownCompoundLeaf(m, top, lDir, rng.Range(0.24, 0.30), c1, c2, rng);
                }
                EmbercrownBerrySpike(m, top, 0.09, rng);
            }
            return;
        }

        // --- Mature Shrub Stool ---
        // 1. Central woody stool & root flare gripping the ground
        var stoolCenter = new Vec3(0, 0.02, 0);
        Primitives.Tube(m, new[] { stoolCenter - Vec3.Up * 0.02, stoolCenter + Vec3.Up * 0.025 },
            new[] { 0.075, 0.055 }, 6, (i, v) => (barkCol, 1, i, v, 2, 0));

        int rootFlangeCount = 5;
        double rPhase = rng.Range(0, Math.PI * 2);
        for (int rf = 0; rf < rootFlangeCount; rf++)
        {
            double rAng = rPhase + rf * Math.PI * 2.0 / rootFlangeCount + rng.Range(-0.2, 0.2);
            var rDir = new Vec3(Math.Cos(rAng), 0, Math.Sin(rAng));
            double rReach = rng.Range(0.09, 0.15);
            Wood(m, new[] {
                rDir * 0.04 + Vec3.Up * 0.03,
                rDir * (rReach * 0.6) + Vec3.Up * 0.01,
                rDir * rReach - Vec3.Up * 0.015
            }, new[] { 0.022, 0.012, 0.003 }, 5);
        }

        // 2. Canes: 1 upright central cane + 5 to 6 outer canes that splay radially outward before rising erect
        int outerCount = 6;
        double phase = rng.Range(0, Math.PI * 2);

        // Helper to add a rich, dense two-tier umbrella of compound leaves at cane top
        void AddApexUmbrella(Vec3 caneTop, double baseAngle)
        {
            // Upper tier: 6 spreading horizontal compound leaves
            int upperCount = 6;
            for (int l = 0; l < upperCount; l++)
            {
                double la = baseAngle + l * (Math.PI * 2.0 / upperCount) + rng.Range(-0.10, 0.10);
                var lDir = new Vec3(Math.Cos(la), rng.Range(-0.04, 0.03), Math.Sin(la)).Normalized();
                EmbercrownCompoundLeaf(m, caneTop - Vec3.Up * 0.012, lDir, rng.Range(0.38, 0.46), c1, c2, rng);
            }

            // Lower tier: 5 slightly shorter descending/weeping compound leaves staggered between upper leaves
            int lowerCount = 5;
            for (int l = 0; l < lowerCount; l++)
            {
                double la = baseAngle + (l + 0.5) * (Math.PI * 2.0 / lowerCount) + rng.Range(-0.12, 0.12);
                var lDir = (new Vec3(Math.Cos(la) * 0.85, -0.22, Math.Sin(la) * 0.85)).Normalized();
                EmbercrownCompoundLeaf(m, caneTop - Vec3.Up * 0.035, lDir, rng.Range(0.30, 0.38), c1, c2, rng);
            }
        }

        // Central cane (taller, upright leader)
        {
            double cHeight = rng.Range(0.96, 1.08);
            var cRoot = new Vec3(rng.Range(-0.015, 0.015), 0.02, rng.Range(-0.015, 0.015));
            var cMid1 = cRoot + new Vec3(rng.Range(-0.03, 0.03), cHeight * 0.35, rng.Range(-0.03, 0.03));
            var cMid2 = cRoot + new Vec3(rng.Range(-0.04, 0.04), cHeight * 0.70, rng.Range(-0.04, 0.04));
            var cTop = cRoot + new Vec3(rng.Range(-0.02, 0.02), cHeight, rng.Range(-0.02, 0.02));
            Primitives.Tube(m, new[] { cRoot, cMid1, cMid2, cTop }, new[] { 0.032, 0.025, 0.018, 0.012 }, 6,
                (i, v) => (barkCol, 1, i / 3.0, v, 2, 0));

            // Mid-cane leaves on central cane
            for (int mLeaf = 0; mLeaf < 3; mLeaf++)
            {
                double mt = 0.42 + mLeaf * 0.18;
                var mPos = Vec3.Lerp(cMid1, cMid2, (mt - 0.35) / 0.35);
                double ma = phase + mLeaf * 2.1;
                var mDir = (new Vec3(Math.Cos(ma), rng.Range(-0.06, 0.04), Math.Sin(ma))).Normalized();
                EmbercrownCompoundLeaf(m, mPos, mDir, rng.Range(0.28, 0.36), c1, c2, rng);
            }

            // Dense two-tier umbrella whorl at cane top
            AddApexUmbrella(cTop, phase);

            // Upright bright red berry spike
            EmbercrownBerrySpike(m, cTop, rng.Range(0.20, 0.25), rng);
        }

        // Outer canes (splay out radially near ground before elbowing upward)
        for (int k = 0; k < outerCount; k++)
        {
            double ang = phase + k * (Math.PI * 2.0 / outerCount) + rng.Range(-0.16, 0.16);
            var rad = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
            var tan = new Vec3(-Math.Sin(ang), 0, Math.Cos(ang));

            double splayDist = rng.Range(0.32, 0.44);
            double tipSpread = splayDist + rng.Range(0.10, 0.18);
            double tipHeight = rng.Range(0.74, 0.88);

            // 5-station spline: splaying outward near the ground, elbowing upward, standing erect
            var s0 = rad * 0.035 + Vec3.Up * 0.015;
            var s1 = rad * (splayDist * 0.65) + tan * rng.Range(-0.025, 0.025) + Vec3.Up * rng.Range(0.05, 0.09);
            var s2 = rad * splayDist + tan * rng.Range(-0.035, 0.035) + Vec3.Up * rng.Range(0.20, 0.30);
            var s3 = rad * (splayDist * 0.90 + tipSpread * 0.10) + Vec3.Up * rng.Range(0.48, 0.60);
            var s4 = rad * tipSpread + Vec3.Up * tipHeight;

            Primitives.Tube(m, new[] { s0, s1, s2, s3, s4 },
                new[] { 0.028, 0.024, 0.020, 0.016, 0.011 }, 6,
                (i, v) => (barkCol, 1, i / 4.0, v, 2, 0));

            // Mid-cane foliage: 4 alternate compound leaves along upright cane section (clothes the stem in lush green)
            for (int mid = 0; mid < 4; mid++)
            {
                double midT = 0.32 + mid * 0.16;
                var midPos = midT < 0.50
                    ? Vec3.Lerp(s2, s3, (midT - 0.32) / 0.18)
                    : Vec3.Lerp(s3, s4, (midT - 0.50) / 0.32);
                double midAngle = ang + (mid % 2 == 0 ? 0.70 : -0.70) + rng.Range(-0.10, 0.10);
                var midDir = (new Vec3(Math.Cos(midAngle), rng.Range(-0.08, 0.04), Math.Sin(midAngle))).Normalized();
                EmbercrownCompoundLeaf(m, midPos, midDir, rng.Range(0.28, 0.36), c1, c2, rng);
            }

            // Top of cane: dense two-tier umbrella of long compound leaves underneath
            AddApexUmbrella(s4, ang);

            // Top of cane: bright red berry cluster spike going straight up
            EmbercrownBerrySpike(m, s4, rng.Range(0.18, 0.23), rng);
        }
    }
    private static void ShrubLanternbrush(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        var oldBark = new[] { 0.19, 0.075, 0.11 };
        var youngBark = new[] { 0.35, 0.16, 0.22 };
        var vein = Primitives.Mix(c2, new[] { 0.63, 0.75, 0.61 }, 0.45);
        int stems = 6 + rng.NextInt(3);
        for (int k = 0; k < stems; k++)
        {
            bool leader = k == 0;
            double ang = leader ? rng.Range(0, 2 * Math.PI) : 2 * Math.PI * (k - 1) / (stems - 1) + rng.Range(-0.22, 0.22);
            var radial = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
            var tangent = new Vec3(-Math.Sin(ang), 0, Math.Cos(ang));
            double reach = leader ? 0.15 : rng.Range(0.72, 0.98);
            double height = leader ? 0.99 : rng.Range(0.73, 0.91);
            double bend = leader ? rng.Range(-0.04, 0.04) : rng.Range(-0.17, 0.17);
            // Outer shoots leave the shaded middle low, wander sideways, then turn up into open light.
            // Separate sideways phases make each silhouette a swoop rather than a straight radial spoke.
            var path = new Vec3[7];
            for (int i = 0; i < path.Length; i++)
            {
                double t = (double)i / (path.Length - 1);
                double r = leader ? reach * t : reach * (0.25 * t + 0.75 * t * t);
                double y = leader ? height * t : height * (0.78 * Math.Sin(t * Math.PI * 0.5) + 0.22 * t);
                double wander = bend * Math.Sin(t * Math.PI * 1.4) + (leader ? 0.025 : 0.11) * Math.Sin(t * Math.PI * 2.2 + ang);
                path[i] = radial * r + tangent * wander + new Vec3(0, y, 0);
            }
            Primitives.Tube(m, path, new[] { 0.044, 0.041, 0.034, 0.027, 0.015, 0.009, 0.0035 }, 7,
                (i, v) => (Primitives.Mix(oldBark, youngBark, i / 6.0), 1, i / 6.0, v, 2, 0));

            // The outer third of each shoot becomes one long compound leaf: a slender rachis
            // carrying close-set, opposing leaflets that grow outward and slightly forward.
            for (int j = 0; j < 16; j++)
            {
                double t = Math.Clamp(0.655 + j * 0.021 + rng.Range(-0.004, 0.004), 0.65, 0.98);
                double segment = t * (path.Length - 1);
                int index = Math.Min(path.Length - 2, (int)segment);
                var p = path[index] + (path[index + 1] - path[index]) * (segment - index);
                var stemDir = (path[index + 1] - path[index]).Normalized();
                for (int n = 0; n < 2; n++)
                {
                    double sign = n == 0 ? -1 : 1;
                    var outDir = (tangent * sign * rng.Range(0.82, 0.98)
                        + stemDir * rng.Range(0.30, 0.45)
                        + new Vec3(0, rng.Range(0.04, 0.13), 0)).Normalized();
                    double petioleLength = rng.Range(0.012, 0.025);
                    var bladeRoot = p + outDir * petioleLength;
                    double fullness = 0.62 + 0.38 * Math.Sin(Math.PI * (j + 0.5) / 16);
                    double len = rng.Range(0.28, 0.35) * fullness;
                    var bladeTip = bladeRoot + outDir * len + new Vec3(0, rng.Range(-0.009, 0.015), 0);
                    var side = Vec3.Up.Cross(outDir);
                    Primitives.Tube(m, new[] { p, bladeRoot }, new[] { 0.0028, 0.0015 }, 6,
                        (i, v) => (youngBark, 1, i, v, 2, 0));
                    Primitives.CurvedLeaf(m, bladeRoot, bladeTip, side, len * rng.Range(0.12, 0.16),
                        Primitives.Mix(c1, c2, 0.18), Primitives.Mix(c1, c2, rng.Range(0.48, 0.72)),
                        camber: len * rng.Range(0.045, 0.085), longitudinal: 4, asymmetry: rng.Range(-0.12, 0.12));
                    Primitives.Tube(m, new[] { bladeRoot + new Vec3(0, 0.001, 0), bladeRoot + (bladeTip - bladeRoot) * 0.55 + new Vec3(0, len * 0.025, 0) },
                        new[] { 0.0018, 0.0006 }, 6, (i, v) => (vein, 1, i, v, 0, 0));
                }
            }
            var terminalDir = (path[6] - path[5]).Normalized();
            Primitives.CurvedLeaf(m, path[6], path[6] + terminalDir * 0.20, Vec3.Up.Cross(terminalDir), 0.028,
                Primitives.Mix(c1, c2, 0.18), Primitives.Mix(c1, c2, 0.6), camber: 0.012, longitudinal: 4);

            if (k > 0 && k % 2 == 0)
            {
                var anchor = path[5] + radial * 0.045;
                var fruit = anchor + radial * 0.025 + new Vec3(0, -0.12, 0);
                Primitives.Tube(m, new[] { anchor, anchor + radial * 0.025 + new Vec3(0, -0.055, 0), fruit },
                    new[] { 0.004, 0.003, 0.002 }, 6, (i, v) => (youngBark, 1, i, v, 0, 0));
                // Bladdernut-like three-part paper capsule: visible lobes and dark seams, not a berry.
                double podAng = rng.Range(0, 2 * Math.PI);
                for (int lobe = 0; lobe < 3; lobe++)
                {
                    double a = podAng + lobe * 2 * Math.PI / 3;
                    var offset = new Vec3(Math.Cos(a) * 0.038, 0, Math.Sin(a) * 0.038);
                    var podCol = new[] { rng.Range(0.65, 0.75), rng.Range(0.55, 0.65), rng.Range(0.36, 0.44) };
                    Primitives.Ellipsoid(m, fruit + offset + new Vec3(0, -0.018, 0),
                        new Vec3(0.047, rng.Range(0.053, 0.068), 0.047), 7, 10,
                        (u, v) => (podCol, 0.83, u, v, 0, 0));
                    Primitives.Tube(m, new[] { fruit + offset + new Vec3(0, 0.04, 0), fruit + offset * 1.35 + new Vec3(0, -0.02, 0), fruit + offset + new Vec3(0, -0.075, 0) },
                        new[] { 0.0018, 0.0024, 0.001 }, 6,
                        (i, v) => (new[] { 0.43, 0.25, 0.18 }, 1, i, v, 0, 0));
                }
            }
        }
    }

    /// <summary>
    /// Shadebell: Understory shade shrub (Enkianthus campanulatus &amp; Pieris japonica).
    /// - Multi-stemmed shrub with 6 arching outer woody canes and 2 central rising stems forming tiered "pagoda" foliage shelves.
    /// - Dense terminal leaf whorls / rosettes at every branchlet tip (14 broad obovate leaves in two overlapping tiers per whorl),
    ///   forming solid, continuous, overlapping, lustrous dark-green canopy shelves with no central void.
    /// - Two-tone leaves: deep lustrous dark shade-green upper surface (c1), pale waxy sage underside.
    /// - Pendulous nodding campanulate bell flower racemes (6-10 bells per cluster) hanging underneath
    ///   the foliage shelves on delicate curved pedicels:
    ///   - Hollow bell cups with flared scalloped rims.
    ///   - Creamy ivory base with delicate rose-coral longitudinal striations.
    /// - Authored in unit-normalized bounds [0..1] for height and radius.
    /// </summary>
    private static void ShrubShadebell(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        double totalH = 1.0;
        double radius = juvenile ? 0.52 : 0.92;
        var barkCol = new[] { 0.28, 0.24, 0.20 };
        var colUnder = Primitives.Mix(Primitives.Scale(c2, 0.80), new[] { 0.32, 0.48, 0.30 }, 0.50);
        var petioleCol = Primitives.Mix(barkCol, c1, 0.45);
        var bellBaseCol = new[] { 0.92, 0.89, 0.80 };
        var bellStripeCol = Primitives.Mix(c2, new[] { 0.85, 0.48, 0.44 }, 0.65);
        var pedicelCol = new[] { 0.58, 0.24, 0.22 };

        // Helper for building a dense terminal whorl / rosette of broad obovate leaves (two tiers of leaves)
        void ShadebellLeafWhorl(Vec3 tipPos, Vec3 outwardDir, double whorlRadius, bool hasFlowerCluster)
        {
            var fwd = outwardDir.Normalized();
            var up = Vec3.Up;
            var side = fwd.Cross(up).Normalized();
            if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);

            // Tier 1: Outer large leaves (10 leaves)
            int outerCount = juvenile ? 8 : 10;
            for (int li = 0; li < outerCount; li++)
            {
                double leafAngle = li * (Math.PI * 2 / outerCount) + rng.Range(-0.08, 0.08);
                var lDir = (side * Math.Cos(leafAngle) + fwd * Math.Sin(leafAngle) * 0.80 - up * 0.08).Normalized();
                var lSide = lDir.Cross(up).Normalized();

                double lLen = whorlRadius * rng.Range(0.92, 1.18);
                double lWidth = lLen * 0.55;

                var pStart = tipPos;
                var pEnd = tipPos + lDir * (lLen * 0.14);
                Primitives.Tube(m, new[] { pStart, pEnd }, new[] { 0.0055, 0.0040 }, 6,
                    (i, v) => (petioleCol, 1.0, i, v, 0, 0));

                int bladeStart = m.VertexCount;
                FoliageBlade.Build(m, pEnd, pEnd + lDir * lLen, lSide, lWidth * 0.5,
                    Primitives.Scale(c1, 0.82), c1, camber: 0.18, curl: 0.14, shoulder: 0.65, segments: 3);

                for (int v = bladeStart; v < m.VertexCount; v++)
                {
                    if (m.UV2[v * 2] > 0.5f)
                        m.SetColor(v, colUnder[0], colUnder[1], colUnder[2], 1.0);
                }
            }

            // Tier 2: Inner overlapping rosette leaves (5-6 leaves) filling the center
            if (!juvenile)
            {
                int innerCount = 5;
                for (int li = 0; li < innerCount; li++)
                {
                    double leafAngle = li * (Math.PI * 2 / innerCount) + 0.35 + rng.Range(-0.08, 0.08);
                    var lDir = (side * Math.Cos(leafAngle) + fwd * Math.Sin(leafAngle) * 0.75 + up * 0.05).Normalized();
                    var lSide = lDir.Cross(up).Normalized();

                    double lLen = whorlRadius * 0.65 * rng.Range(0.90, 1.10);
                    double lWidth = lLen * 0.58;

                    var pStart = tipPos + up * 0.012;
                    var pEnd = pStart + lDir * (lLen * 0.14);

                    int bladeStart = m.VertexCount;
                    FoliageBlade.Build(m, pEnd, pEnd + lDir * lLen, lSide, lWidth * 0.5,
                        Primitives.Scale(c1, 0.88), c1, camber: 0.15, curl: 0.10, shoulder: 0.65, segments: 3);

                    for (int v = bladeStart; v < m.VertexCount; v++)
                    {
                        if (m.UV2[v * 2] > 0.5f)
                            m.SetColor(v, colUnder[0], colUnder[1], colUnder[2], 1.0);
                    }
                }
            }

            // Hanging nodding campanulate bell flower raceme beneath the foliage whorl
            if (hasFlowerCluster)
            {
                var racemeBase = tipPos - Vec3.Up * 0.02;
                int bellCount = juvenile ? 4 : 7;
                double racemeLen = 0.13 * (juvenile ? 0.6 : 1.0);

                var rPts = new Vec3[bellCount + 1];
                var rRadii = new double[bellCount + 1];
                rPts[0] = racemeBase;
                rRadii[0] = 0.0040;
                for (int bi = 1; bi <= bellCount; bi++)
                {
                    double bt = (double)bi / bellCount;
                    rPts[bi] = racemeBase - Vec3.Up * (racemeLen * bt) + fwd * (racemeLen * 0.22 * bt);
                    rRadii[bi] = 0.0035 * (1.0 - 0.60 * bt);
                }
                Primitives.Tube(m, rPts, rRadii, 6, (i, v) => (pedicelCol, 1.0, i / (double)bellCount, v, 0, 0));

                for (int bi = 1; bi <= bellCount; bi++)
                {
                    var pedStart = rPts[bi];
                    double bAng = bi * 2.39996;
                    var bSide = new Vec3(Math.Cos(bAng), 0, Math.Sin(bAng));
                    var pedEnd = pedStart + bSide * 0.038 - Vec3.Up * 0.028;

                    // Delicate curved pedicel
                    Primitives.Tube(m, new[] { pedStart, (pedStart + pedEnd) * 0.5 + bSide * 0.008, pedEnd },
                        new[] { 0.0022, 0.0018, 0.0014 }, 6,
                        (i, v) => (pedicelCol, 1.0, i / 2.0, v, 0, 0));

                    // Nodding campanulate bell blossom (hollow cup with flared scalloped rim)
                    int bellStart = m.VertexCount;
                    const int bRings = 4, bSides = 10;
                    double bellH = 0.044;
                    double bellR = 0.024;

                    for (int ri = 0; ri <= bRings; ri++)
                    {
                        double rt = (double)ri / bRings;
                        double y = -bellH * rt;
                        double r = bellR * (0.35 + 0.65 * Math.Sin(Math.PI * 0.5 * rt) + (rt > 0.7 ? 0.35 * (rt - 0.7) / 0.3 : 0.0));
                        var col = Primitives.Mix(bellBaseCol, bellStripeCol, Math.Pow(rt, 1.2));

                        for (int s = 0; s <= bSides; s++)
                        {
                            double th = 2 * Math.PI * s / bSides;
                            double lobeWave = rt > 0.7 ? Math.Cos(th * 5) * 0.15 * bellR : 0.0;
                            var pos = pedEnd + new Vec3(Math.Cos(th) * (r + lobeWave), y, Math.Sin(th) * (r + lobeWave));
                            var norm = new Vec3(Math.Cos(th), -0.3, Math.Sin(th)).Normalized();
                            m.AddVertex(pos, norm, col, 1.0, (double)s / bSides, rt, 0.0, 0.0);
                        }
                    }

                    for (int ri = 0; ri < bRings; ri++)
                    {
                        for (int s = 0; s < bSides; s++)
                        {
                            int a = bellStart + ri * (bSides + 1) + s;
                            int c = a + bSides + 1;
                            Primitives.TriangleFacing(m, a, c, a + 1, Vec3.Up);
                            Primitives.TriangleFacing(m, a + 1, c, c + 1, Vec3.Up);
                        }
                    }
                }
            }
        }

        // Multi-stemmed shrub: 6 outer splaying canes + 2 central upright canes filling the summit
        int caneCount = juvenile ? 4 : 6;
        for (int k = 0; k < caneCount; k++)
        {
            double ang = k * (Math.PI * 2 / caneCount) + rng.Range(-0.25, 0.25);
            var radial = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
            var root = new Vec3(0, 0, 0);

            double caneReach = radius * rng.Range(0.68, 0.88);
            double caneH = totalH * rng.Range(0.85, 0.98);

            var c0 = root;
            var c1pt = radial * (caneReach * 0.35) + new Vec3(0, caneH * 0.28, 0);
            var c2pt = radial * (caneReach * 0.75) + new Vec3(0, caneH * 0.68, 0);
            var cTop = radial * caneReach + new Vec3(0, caneH, 0);

            Wood(m, new[] { c0, c1pt, c2pt, cTop }, new[] { 0.038, 0.026, 0.016, 0.009 }, 6);

            // 3 horizontal tiered branch shelves per cane
            int tiers = juvenile ? 2 : 3;
            for (int tr = 0; tr < tiers; tr++)
            {
                double tt = 0.36 + 0.52 * tr / Math.Max(1, tiers - 1);
                var tierAttach = Curve(c0, c1pt, cTop, tt);
                double tAng = ang + (tr % 2 == 0 ? 0.65 : -0.65) + rng.Range(-0.15, 0.15);
                var tDir = new Vec3(Math.Cos(tAng), 0, Math.Sin(tAng));
                double tReach = (radius * 0.48 * (1.1 - tt * 0.35)) * rng.Range(0.88, 1.12);

                var tTip = tierAttach + tDir * tReach + new Vec3(0, rng.Range(-0.02, 0.04), 0);
                var tCtrl = (tierAttach + tTip) * 0.5 + Vec3.Up * 0.025;
                WoodyCurve(m, tierAttach, tCtrl, tTip, 0.012, 3);

                bool flowerOnTier = !juvenile && (tr == 1 || tr == 2);
                ShadebellLeafWhorl(tTip, tDir, 0.28, flowerOnTier);
            }

            // Crown terminal whorl atop the cane
            bool flowerOnTop = !juvenile && (k % 2 == 0);
            ShadebellLeafWhorl(cTop, radial, 0.30, flowerOnTop);
        }

        // Central summit canes crowning the interior (preventing open center void)
        if (!juvenile)
        {
            for (int ci = 0; ci < 2; ci++)
            {
                double cAng = ci * Math.PI + 0.5 + rng.Range(-0.2, 0.2);
                var cRadial = new Vec3(Math.Cos(cAng), 0, Math.Sin(cAng)) * 0.18;
                var cTop = cRadial + new Vec3(0, totalH * 1.0, 0);
                var cMid = cRadial * 0.5 + new Vec3(0, totalH * 0.55, 0);
                WoodyCurve(m, new Vec3(0, 0, 0), cMid, cTop, 0.022, 4);
                ShadebellLeafWhorl(cTop, new Vec3(Math.Cos(cAng), 0, Math.Sin(cAng)), 0.30, true);
            }
        }
    }

    // ------------------------------------------------------------------ fauna

    private static readonly double[] White = { 1, 1, 1 };

    private static void BodySegment(MeshData m, Vec3 centre, Vec3 radii, int rings, int segments,
        Func<double, double, (double[] Col, double A, double U, double V, double U2, double V2)> attr, double pitch = 0)
    {
        int start = m.VertexCount;
        double cp = Math.Cos(pitch), sp = Math.Sin(pitch);
        for (int r = 0; r <= rings; r++)
        {
            double phi = Math.PI * r / rings;
            for (int s = 0; s <= segments; s++)
            {
                double th = 2 * Math.PI * s / segments;
                var local = new Vec3(Math.Cos(phi) * radii.X, Math.Sin(phi) * Math.Cos(th) * radii.Y, Math.Sin(phi) * Math.Sin(th) * radii.Z);
                var n = new Vec3(Math.Cos(phi) / radii.X, Math.Sin(phi) * Math.Cos(th) / radii.Y, Math.Sin(phi) * Math.Sin(th) / radii.Z).Normalized();
                var rp = new Vec3(local.X * cp - local.Y * sp, local.X * sp + local.Y * cp, local.Z);
                var rn = new Vec3(n.X * cp - n.Y * sp, n.X * sp + n.Y * cp, n.Z);
                var a = attr((double)r / rings, (double)s / segments);
                m.AddVertex(centre + rp, rn, a.Col, a.A, a.U, a.V, a.U2, a.V2);
            }
        }
        for (int r = 0; r < rings; r++)
            for (int s = 0; s < segments; s++)
            {
                int a = start + r * (segments + 1) + s, b = a + segments + 1;
                if (r > 0) m.AddTriangle(a, a + 1, b);
                if (r < rings - 1) m.AddTriangle(a + 1, b + 1, b);
            }
    }

    public static MeshData Fauna(FaunaSpeciesDef sp, ulong visualSeed = 0)
    {
        var m = new MeshData();
        var model = !string.IsNullOrEmpty(sp.Model) ? sp.Model : sp.Id;
        switch (model)
        {
            case "springtail": Springtail(m); break;
            case "shrimp": Shrimp(m); break;
            case "triops": Triops(m); break;
            case "minnow": Minnow(m); break;
            case "isopod":
            case "pill_bug":
                Isopod(m);
                break;
            case "beetle": Beetle(m); break;
            case "silverfish": Silverfish(m); break;
            case "slug": Slug(m); break;
            case "millipede": Millipede(m); break;
            case "moth": Moth(m); break;
            case "toad": Toad(m); break;
            case "salamander": Salamander(m); break;
            case "worm": Worm(m); break;
            case "harvestman": Harvestman(m); break;
            case "aquatic_larva": AquaticLarva(m); break;
            case "snail": Snail(m); break;
            case "midge": Midge(m); break;
            default: Primitives.Ellipsoid(m, new Vec3(0, 0.2, 0), new Vec3(0.5, 0.2, 0.2), 8, 10, (a, b) => (Body, 0, a, b, 1, 0)); break;
        }
        if (visualSeed != 0) ApplyFaunaMorph(m, model, visualSeed);
        return m;
    }

    /// <summary>Rolled-up pose for conglobating species (pill bug ball); null for species that never curl.</summary>
    public static MeshData? FaunaCurled(FaunaSpeciesDef sp, ulong visualSeed = 0)
    {
        var model = !string.IsNullOrEmpty(sp.Model) ? sp.Model : sp.Id;
        if (model is not ("isopod" or "pill_bug")) return null;
        var m = new MeshData();
        const double cy = 0.22, ringR = 0.175;

        // Ventral sphere: inner sphere representing the tightly curled belly
        BodySegment(m, new Vec3(0, cy, 0), new Vec3(0.185, 0.185, 0.195), 4, 7, (a, b) => Region(0.5, b, 3));

        // Exactly 7 arched overlapping armor plates (Pereonites 1 to 7) wrapping tightly over the sphere
        const int plates = 7;
        for (int k = 0; k < plates; k++)
        {
            double t = (double)k / (plates - 1);
            // Plates run across the dorsal circumference from anterior to posterior
            double ang = Math.PI * 0.72 - k * (Math.PI * 1.44 / (plates - 1));
            var c = new Vec3(Math.Cos(ang) * ringR, cy + Math.Sin(ang) * ringR, 0);
            double w = 0.22 + 0.02 * Math.Sin(Math.PI * t);

            // Arched pereonite armor plate tightly fitted over sphere
            BodySegment(m, c, new Vec3(0.075, 0.040, w), 2, 6, (a, b) => Region(0.85 - t * 0.6, b, 1), pitch: ang + Math.PI / 2);

            // Lateral epimera flanges on each plate
            foreach (double z in new[] { -1.0, 1.0 })
            {
                var epiC = c + new Vec3(0, 0, (w - 0.01) * z);
                BodySegment(m, epiC, new Vec3(0.055, 0.018, 0.028), 2, 3, (a, b) => Region(0.85 - t * 0.6, b, 1), pitch: ang + Math.PI / 2);
            }
        }

        // Tucked cephalon at anterior end
        double headAng = Math.PI * 0.72 + 0.32;
        var headC = new Vec3(Math.Cos(headAng) * ringR, cy + Math.Sin(headAng) * ringR, 0);
        BodySegment(m, headC, new Vec3(0.055, 0.038, 0.15), 2, 5, (a, b) => Region(0.95, b, 1), pitch: headAng + Math.PI / 2);

        // Lateral compound eyes
        foreach (double z in new[] { -1.0, 1.0 })
        {
            var eyeC = headC + new Vec3(0, 0.008, 0.12 * z);
            BodySegment(m, eyeC, new Vec3(0.016, 0.014, 0.012), 2, 4, (a, b) => Region(0.97, b, 2));
        }

        // Tucked pleotelson at posterior end meeting cephalon
        double tailAng = Math.PI * 0.72 - 1.44 - 0.28;
        var tailC = new Vec3(Math.Cos(tailAng) * ringR, cy + Math.Sin(tailAng) * ringR, 0);
        BodySegment(m, tailC, new Vec3(0.050, 0.035, 0.11), 2, 5, (a, b) => Region(0.05, b, 1), pitch: tailAng + Math.PI / 2);

        // Tucked appendages (antennae / legs sealed in ventral seam)
        foreach (double z in new[] { -1.0, 1.0 })
        {
            var attach = headC + new Vec3(0, -0.018, 0.04 * z);
            Appendage(m, new[] { attach, attach + new Vec3(0.015, -0.025, 0.025 * z), attach + new Vec3(0.01, -0.04, 0.04 * z) },
                new[] { 0.012, 0.008, 0.004 }, 6, 4);
        }

        if (visualSeed != 0) ApplyFaunaMorph(m, model, visualSeed);
        return m;
    }

    private static void ApplyFaunaMorph(MeshData m, string model, ulong seed)
    {
        var rng = Rng.Keyed(seed, "fauna.visual.morph", 0);
        double length = rng.Range(0.94, 1.07);
        double height = rng.Range(0.92, 1.08);
        double width = rng.Range(0.92, 1.09);
        double appendageLong = rng.Range(0.91, 1.10);
        double appendageVert = rng.Range(0.94, 1.07);
        double appendageWide = rng.Range(0.91, 1.11);
        double asym = rng.Range(-0.055, 0.055);
        double arch = rng.Range(-0.018, 0.022);

        // Aquatic bodies benefit from slightly more shape diversity; plated terrestrial animals stay tighter so
        // their joints continue to overlap plausibly.
        if (model is "minnow" or "shrimp" or "triops" or "aquatic_larva" or "snail")
        {
            length *= rng.Range(0.96, 1.05);
            height *= rng.Range(0.96, 1.05);
            width *= rng.Range(0.95, 1.06);
        }
        else if (model is "isopod" or "beetle" or "pill_bug")
        {
            length = MathD.Lerp(1, length, 0.7);
            height = MathD.Lerp(1, height, 0.7);
            width = MathD.Lerp(1, width, 0.75);
        }

        Vec3 MorphBody(Vec3 p)
        {
            double envelope = Math.Max(0, 1.0 - Math.Abs(p.X) / 0.65);
            return new Vec3(
                p.X * length,
                p.Y * height + arch * envelope,
                p.Z * width + asym * envelope * 0.035);
        }

        for (int i = 0; i < m.VertexCount; i++)
        {
            var p = m.Position(i);
            bool appendage = m.Colors[i * 4 + 3] > 0.5f;
            Vec3 outP;
            if (appendage)
            {
                var off = new Vec3(m.Colors[i * 4], m.Colors[i * 4 + 1], m.Colors[i * 4 + 2]);
                var attach = p - off;
                var a2 = MorphBody(attach);
                double side = off.Z < 0 ? -1 : off.Z > 0 ? 1 : 0;
                var off2 = new Vec3(
                    off.X * appendageLong,
                    off.Y * appendageVert,
                    off.Z * appendageWide * (1.0 + asym * side));
                // Tiny inherited sweep differences keep antennae/legs/fins from sharing one exact outline.
                off2 = new Vec3(off2.X + asym * off2.Z * 0.32, off2.Y, off2.Z - asym * off2.X * 0.22);
                outP = a2 + off2;
                m.Colors[i * 4] = (float)off2.X;
                m.Colors[i * 4 + 1] = (float)off2.Y;
                m.Colors[i * 4 + 2] = (float)off2.Z;
            }
            else outP = MorphBody(p);

            m.Positions[i * 3] = (float)outP.X;
            m.Positions[i * 3 + 1] = (float)outP.Y;
            m.Positions[i * 3 + 2] = (float)outP.Z;
        }
        m.RecomputeNormals();
    }

    private static readonly double[] Body = { 0, 0, 0 };

    private static (double[] Col, double A, double U, double V, double U2, double V2) Region(double u, double v, int region) => (Body, 0, u, v, region, 0);

    /// <summary>Appendage vertex attributes: colour carries the offset from the attachment point.</summary>
    private static void Appendage(MeshData m, IReadOnlyList<Vec3> path, IReadOnlyList<double> radius, int segs, int region = 4, double animationRole = 1)
    {
        if (path.Count < 2 || radius.Count != path.Count) return;
        var attach = path[0];

        // Build a short flared root into the first segment instead of starting a constant-radius tube abruptly at
        // the body surface. It reads as a joint/socket at macro distance and removes the "tube glued to ellipsoid"
        // construction tell without needing a separate overlapping primitive.
        var p = new List<Vec3>(path.Count + 1) { attach, Vec3.Lerp(path[0], path[1], 0.18) };
        var r = new List<double>(radius.Count + 1) { radius[0] * 1.28, radius[0] * 1.08 };
        for (int k = 1; k < path.Count; k++) { p.Add(path[k]); r.Add(radius[k]); }

        int start = m.VertexCount;
        Primitives.Tube(m, p, r, segs, (i, v) => (Body, 1, 0.5, v, region, animationRole));
        for (int i = start; i < m.VertexCount; i++)
        {
            var off = m.Position(i) - attach;
            m.SetColor(i, off.X, off.Y, off.Z, 1);
        }
    }

    private static void AppendageFan(MeshData m, Vec3 attach, Vec3 centre, IReadOnlyList<Vec3> rim, Vec3 normal, double animationRole = 2)
    {
        int start = m.VertexCount;
        Primitives.Fan(m, centre, rim, normal, White, White, 1, 4, v2: animationRole);
        for (int i = start; i < m.VertexCount; i++)
        {
            var off = m.Position(i) - attach;
            m.SetColor(i, off.X, off.Y, off.Z, 1);
        }
    }

    private static void Springtail(MeshData m)
    {
        // Entomobryid collembolan: length 1 along +X (head forward), origin at underside center.
        // Articulated arthropod anatomy:
        // 1. Globular head with clypeus/mouthparts and lateral ocellar compound eye patches
        // 2. 3 thoracic segments (pro-, meso-, metathorax)
        // 3. 6 abdominal segments (A1..A6) with smooth tapering posterior
        // 4. Ventral sternum (belly) and ventral collophore tube
        // 5. 4-segmented elbowed antennae
        // 6. 3 pairs of jointed legs (coxa, femur, tibia, tarsus)
        // 7. Folded jumping fork (furcula: manubrium, paired dentes, mucro tips) tucked underneath

        // --- 1. Globular Head & Clypeus / Mouthparts ---
        // Globular head capsule
        BodySegment(m, new Vec3(0.36, 0.165, 0), new Vec3(0.095, 0.085, 0.080), 4, 7, (a, b) => Region(0.90, b, 1), pitch: -0.22);
        // Clypeus / mouthparts (anterior-ventral projection)
        BodySegment(m, new Vec3(0.43, 0.105, 0), new Vec3(0.040, 0.032, 0.032), 3, 5, (a, b) => Region(0.96, b, 1), pitch: -0.45);

        // Lateral eye patches (clusters of ocelli)
        foreach (double z in new[] { -1.0, 1.0 })
        {
            BodySegment(m, new Vec3(0.38, 0.195, 0.062 * z), new Vec3(0.020, 0.016, 0.014), 2, 4, (a, b) => Region(0.98, b, 2));
        }

        // --- 2. Three Thoracic Segments ---
        // Prothorax (T1)
        BodySegment(m, new Vec3(0.24, 0.165, 0), new Vec3(0.042, 0.082, 0.085), 3, 6, (a, b) => Region(0.78, b, 1));
        // Mesothorax (T2)
        BodySegment(m, new Vec3(0.15, 0.172, 0), new Vec3(0.048, 0.090, 0.092), 3, 6, (a, b) => Region(0.70, b, 1));
        // Metathorax (T3)
        BodySegment(m, new Vec3(0.05, 0.170, 0), new Vec3(0.050, 0.090, 0.092), 3, 6, (a, b) => Region(0.62, b, 1));

        // --- 3. Six Abdominal Segments (A1..A6) ---
        // A1
        BodySegment(m, new Vec3(-0.05, 0.165, 0), new Vec3(0.048, 0.086, 0.088), 3, 6, (a, b) => Region(0.54, b, 1));
        // A2
        BodySegment(m, new Vec3(-0.14, 0.160, 0), new Vec3(0.046, 0.082, 0.082), 3, 6, (a, b) => Region(0.46, b, 1));
        // A3
        BodySegment(m, new Vec3(-0.22, 0.152, 0), new Vec3(0.044, 0.076, 0.075), 3, 6, (a, b) => Region(0.38, b, 1));
        // A4
        BodySegment(m, new Vec3(-0.29, 0.142, 0), new Vec3(0.042, 0.068, 0.065), 3, 6, (a, b) => Region(0.30, b, 1));
        // A5
        BodySegment(m, new Vec3(-0.36, 0.130, 0), new Vec3(0.038, 0.058, 0.052), 3, 6, (a, b) => Region(0.22, b, 1));
        // A6 (tapering terminal segment)
        BodySegment(m, new Vec3(-0.42, 0.115, 0), new Vec3(0.032, 0.042, 0.038), 3, 6, (a, b) => Region(0.14, b, 1));

        // --- 4. Ventral Sternum (Belly) and Collophore Tube ---
        // Ventral belly floor connecting sternites (Region 3)
        BodySegment(m, new Vec3(-0.06, 0.082, 0), new Vec3(0.36, 0.030, 0.062), 3, 6, (a, b) => Region(0.50, b, 3));
        // Ventral tube (collophore) projecting downward from A1 sternite
        BodySegment(m, new Vec3(-0.03, 0.045, 0), new Vec3(0.022, 0.028, 0.018), 3, 5, (a, b) => Region(0.50, b, 3));

        // --- 5. Appendages: Antennae and Legs ---
        foreach (double z in new[] { -1.0, 1.0 })
        {
            // 4-segmented elbowed antennae:
            // Ant I (scape) -> Ant II (pedicel) -> elbow bend -> Ant III -> Ant IV (flagellum tip)
            var antSocket = new Vec3(0.42, 0.18, 0.045 * z);
            var ant1 = antSocket + new Vec3(0.07, 0.025, 0.040 * z);
            var ant2 = ant1 + new Vec3(0.08, 0.010, 0.060 * z); // elbow
            var ant3 = ant2 + new Vec3(0.09, -0.018, 0.035 * z);
            var ant4 = ant3 + new Vec3(0.09, -0.035, 0.015 * z); // tip
            Appendage(m, new[] { antSocket, ant1, ant2, ant3, ant4 }, new[] { 0.016, 0.013, 0.010, 0.007, 0.004 }, 6, 4, 3);

            // 3 pairs of jointed legs (coxa / femur / tibia / tarsus) under T1, T2, T3
            for (int leg = 0; leg < 3; leg++)
            {
                double legX = 0.24 - leg * 0.095;
                double sweep = (leg - 1) * -0.018;
                var pCoxa = new Vec3(legX, 0.088, 0.052 * z);
                var pFemur = new Vec3(legX + sweep * 0.3, 0.078, 0.082 * z);
                var pTibia = new Vec3(legX + sweep * 0.7, 0.055, 0.120 * z);
                var pTarsus = new Vec3(legX + sweep * 1.1, 0.025, 0.145 * z);
                var pClaw = new Vec3(legX + sweep * 1.5, 0.000, 0.160 * z);
                Appendage(m, new[] { pCoxa, pFemur, pTibia, pTarsus, pClaw }, new[] { 0.015, 0.013, 0.010, 0.007, 0.004 }, 6, 4);
            }
        }

        // --- 6. Folded Jumping Fork (Furcula: Manubrium, Paired Dentes, Mucro Tips) ---
        // Basal piece (manubrium) attached at ventral A4/A5 and extending forward along midline
        var manuAttach = new Vec3(-0.31, 0.068, 0);
        var manuMid = new Vec3(-0.24, 0.050, 0);
        var manuFork = new Vec3(-0.17, 0.038, 0);
        Appendage(m, new[] { manuAttach, manuMid, manuFork }, new[] { 0.016, 0.013, 0.010 }, 6, 4);

        // Paired dentes with hooked mucro tips extending forward under belly
        foreach (double z in new[] { -1.0, 1.0 })
        {
            var densAttach = new Vec3(-0.17, 0.038, 0.008 * z);
            var densMid = new Vec3(-0.10, 0.035, 0.016 * z);
            var densApex = new Vec3(-0.04, 0.032, 0.016 * z);
            var mucroTip = new Vec3(-0.015, 0.038, 0.010 * z); // hooked tip
            Appendage(m, new[] { densAttach, densMid, densApex, mucroTip }, new[] { 0.009, 0.007, 0.005, 0.003 }, 6, 4);
        }
    }

    private static void Shrimp(MeshData m)
    {
        // curved, tapering segmented body: carapace + 6 abdominal segments
        Primitives.Ellipsoid(m, new Vec3(0.18, 0.2, 0), new Vec3(0.24, 0.12, 0.1), 10, 12, (a, b) => Region(0.8 - a * 0.25, b, 1));
        for (int s = 0; s < 6; s++)
        {
            double t = s / 5.0;
            double x = -0.05 - t * 0.38, y = 0.18 - t * t * 0.08;
            double r = 0.1 * (1 - t * 0.55);
            Primitives.Ellipsoid(m, new Vec3(x, y, 0), new Vec3(0.075, r, r * 0.85), 6, 10, (a, b) => Region(0.5 - t * 0.45, b, b > 0.6 ? 3 : 1), pitch: -t * 0.5);
        }
        // tail fan
        var tailAttach = new Vec3(-0.46, 0.1, 0);
        AppendageFan(m, tailAttach, tailAttach, new List<Vec3> { tailAttach + new Vec3(-0.02, 0.0, -0.1), tailAttach + new Vec3(-0.14, -0.02, -0.08), tailAttach + new Vec3(-0.16, -0.02, 0), tailAttach + new Vec3(-0.14, -0.02, 0.08), tailAttach + new Vec3(-0.02, 0.0, 0.1) }, Vec3.Up);
        // rostrum, eyes, antennae, legs
        Appendage(m, new[] { new Vec3(0.4, 0.24, 0), new Vec3(0.5, 0.26, 0) }, new[] { 0.015, 0.004 }, 4, 1, 0);
        foreach (double z in new[] { -1.0, 1.0 })
        {
            Primitives.Ellipsoid(m, new Vec3(0.38, 0.25, 0.06 * z), new Vec3(0.03, 0.03, 0.03), 4, 6, (a, b) => Region(0.95, b, 2));
            Appendage(m, new[] { new Vec3(0.4, 0.22, 0.04 * z), new Vec3(0.7, 0.3, 0.18 * z), new Vec3(1.0, 0.25, 0.32 * z), new Vec3(1.25, 0.15, 0.4 * z) }, new[] { 0.01, 0.007, 0.005, 0.003 }, 4, 4, 3);
            for (int leg = 0; leg < 5; leg++)
            {
                double x = 0.28 - leg * 0.08;
                Appendage(m, new[] { new Vec3(x, 0.1, 0.05 * z), new Vec3(x + 0.03, 0.04, 0.12 * z), new Vec3(x + 0.02, 0.0, 0.16 * z) }, new[] { 0.01, 0.008, 0.005 }, 3);
            }
        }
    }

    private static void Triops(MeshData m)
    {
        // broad shield carapace (flattened dome) over the front, segmented abdomen behind
        Primitives.Ellipsoid(m, new Vec3(0.15, 0.08, 0), new Vec3(0.34, 0.08, 0.3), 12, 16, (a, b) => Region(0.9 - a * 0.4, b, b > 0.55 ? 3 : 1));
        for (int s = 0; s < 7; s++)
        {
            double t = s / 6.0;
            double r = 0.07 * (1 - t * 0.5);
            Primitives.Ellipsoid(m, new Vec3(-0.12 - t * 0.28, 0.06, 0), new Vec3(0.05, r * 0.6, r), 5, 8, (a, b) => Region(0.4 - t * 0.35, b, 1));
        }
        foreach (double z in new[] { -1.0, 1.0 })
        {
            Primitives.Ellipsoid(m, new Vec3(0.34, 0.15, 0.04 * z), new Vec3(0.022, 0.015, 0.022), 4, 6, (a, b) => Region(0.95, b, 2));
            Appendage(m, new[] { new Vec3(-0.42, 0.06, 0.02 * z), new Vec3(-0.62, 0.07, 0.07 * z), new Vec3(-0.85, 0.06, 0.12 * z) }, new[] { 0.012, 0.008, 0.004 }, 4);
            for (int leg = 0; leg < 6; leg++)
            {
                double x = 0.2 - leg * 0.06;
                Appendage(m, new[] { new Vec3(x, 0.03, 0.12 * z), new Vec3(x - 0.02, 0.0, 0.24 * z) }, new[] { 0.01, 0.005 }, 3);
            }
        }
    }

    private static void Isopod(MeshData m)
    {
        // Oniscidean isopod (woodlouse / pill bug): length 1 along +X, origin at underside center.
        // Articulated arthropod anatomy:
        // 1. Distinct cephalon with lateral compound eyes
        // 2. Elbowed antennae (antenna 2)
        // 3. 7 overlapping arched pereonite plates with articulated lateral epimera flanges
        // 4. 5 pleonites + shield-like pleotelson
        // 5. Ventral belly (sternites)
        // 6. 7 pairs of jointed walking pereopods
        // 7. Sensory uropods at rear

        // --- 1. Distinct Cephalon & Lateral Compound Eyes ---
        // Cephalon: broad, rounded anterior shield
        BodySegment(m, new Vec3(0.36, 0.075, 0), new Vec3(0.065, 0.055, 0.150), 3, 6, (a, b) => Region(0.92, b, 1));

        // Lateral compound eyes
        foreach (double z in new[] { -1.0, 1.0 })
        {
            BodySegment(m, new Vec3(0.37, 0.095, 0.125 * z), new Vec3(0.018, 0.016, 0.014), 2, 4, (a, b) => Region(0.97, b, 2));
        }

        // --- 2. Elbowed Antennae ---
        foreach (double z in new[] { -1.0, 1.0 })
        {
            var ant0 = new Vec3(0.40, 0.065, 0.055 * z); // socket
            var ant1 = new Vec3(0.47, 0.082, 0.110 * z); // pedicel
            var ant2 = new Vec3(0.55, 0.060, 0.170 * z); // elbow
            var ant3 = new Vec3(0.62, 0.020, 0.200 * z); // flagellum tip
            Appendage(m, new[] { ant0, ant1, ant2, ant3 }, new[] { 0.015, 0.012, 0.009, 0.005 }, 6, 4, 3);
        }

        // --- 3. Seven Overlapping Arched Pereonite Plates with Lateral Epimera Flanges ---
        for (int s = 0; s < 7; s++)
        {
            double t = s / 6.0;
            double x = 0.26 - s * 0.078;
            double dome = Math.Sin(Math.PI * (0.15 + t * 0.70));
            double w = 0.22 + 0.04 * dome;
            double h = 0.085 + 0.065 * dome;

            // Arched central tergite plate (tilted with pitch so trailing edge overlaps next plate)
            BodySegment(m, new Vec3(x, 0.065, 0), new Vec3(0.048, h, w), 2, 6, (a, b) => Region(0.85 - t * 0.55, b, 1), pitch: -0.16);

            // Articulated lateral epimera flanges on left and right
            double sweep = (s >= 3 ? (s - 2) * 0.012 : 0);
            foreach (double z in new[] { -1.0, 1.0 })
            {
                var epiC = new Vec3(x - sweep * 0.5, 0.038 + 0.015 * dome, (w + 0.015) * z);
                var epiR = new Vec3(0.040 + sweep * 0.5, 0.016, 0.030);
                BodySegment(m, epiC, epiR, 2, 3, (a, b) => Region(0.85 - t * 0.55, b, 1), pitch: -0.14);
            }
        }

        // --- 4. Five Pleonites and Shield-like Pleotelson ---
        for (int s = 0; s < 5; s++)
        {
            double t = s / 4.0;
            double px = -0.26 - s * 0.028;
            double pw = 0.17 - s * 0.018;
            double ph = 0.070 - s * 0.010;
            BodySegment(m, new Vec3(px, 0.062, 0), new Vec3(0.020, ph, pw), 2, 4, (a, b) => Region(0.26 - t * 0.15, b, 1), pitch: -0.20);
        }

        // Shield-like pleotelson
        BodySegment(m, new Vec3(-0.42, 0.050, 0), new Vec3(0.042, 0.028, 0.065), 2, 5, (a, b) => Region(0.05, b, 1), pitch: -0.22);

        // --- 5. Ventral Belly (Sternites) ---
        BodySegment(m, new Vec3(0.0, 0.042, 0), new Vec3(0.42, 0.030, 0.18), 2, 6, (a, b) => Region(0.50, b, 3));

        // --- 6. Seven Pairs of Jointed Walking Pereopods ---
        for (int s = 0; s < 7; s++)
        {
            double legX = 0.26 - s * 0.078;
            double sweep = (s - 3) * -0.012;
            foreach (double z in new[] { -1.0, 1.0 })
            {
                var p0 = new Vec3(legX, 0.042, 0.13 * z);
                var p1 = new Vec3(legX + sweep * 0.5, 0.048, 0.19 * z);
                var p2 = new Vec3(legX + sweep, 0.024, 0.25 * z);
                var p3 = new Vec3(legX + sweep * 1.4, 0.000, 0.28 * z);
                Appendage(m, new[] { p0, p1, p2, p3 }, new[] { 0.014, 0.011, 0.008, 0.005 }, 6, 4);
            }
        }

        // --- 7. Sensory Uropods at Rear ---
        foreach (double z in new[] { -1.0, 1.0 })
        {
            var uro0 = new Vec3(-0.41, 0.042, 0.045 * z);
            var uro1 = new Vec3(-0.46, 0.034, 0.075 * z);
            var uro2 = new Vec3(-0.51, 0.024, 0.095 * z);
            Appendage(m, new[] { uro0, uro1, uro2 }, new[] { 0.012, 0.008, 0.004 }, 6, 4);
        }
    }

    private static void Beetle(MeshData m)
    {
        // darkling beetle, length 1 along +X: domed fused wing cases with a centre seam, a wide pronotum, a small
        // head with beaded antennae, and six long legs
        Primitives.Ellipsoid(m, new Vec3(-0.1, 0.17, 0), new Vec3(0.34, 0.17, 0.2), 10, 16, (a, b) => Region(0.55 - a * 0.5, b, b > 0.3 && b < 0.7 ? 3 : 1));  // elytra
        Primitives.Ellipsoid(m, new Vec3(-0.1, 0.335, 0), new Vec3(0.3, 0.008, 0.006), 3, 4, (a, b) => Region(0.5, b, 2));                                      // seam
        Primitives.Ellipsoid(m, new Vec3(0.25, 0.15, 0), new Vec3(0.11, 0.1, 0.17), 8, 12, (a, b) => Region(0.8, b, 1));                                        // pronotum
        Primitives.Ellipsoid(m, new Vec3(0.39, 0.12, 0), new Vec3(0.07, 0.06, 0.09), 6, 10, (a, b) => Region(0.95, b, 1));                                      // head
        foreach (double z in new[] { -1.0, 1.0 })
        {
            Primitives.Ellipsoid(m, new Vec3(0.43, 0.14, 0.06 * z), new Vec3(0.018, 0.016, 0.015), 4, 6, (a, b) => Region(0.97, b, 2));
            var ant = new List<Vec3> { new Vec3(0.45, 0.13, 0.05 * z) };
            for (int k = 1; k <= 5; k++) ant.Add(ant[^1] + new Vec3(0.045, 0.012, 0.03 * z));
            Appendage(m, ant, new[] { 0.013, 0.012, 0.013, 0.014, 0.015, 0.017 }, 4, 4, 3);
            for (int leg = 0; leg < 3; leg++)
            {
                double x = 0.24 - leg * 0.16, sweep = (leg - 1) * 0.12;
                Appendage(m, new[] { new Vec3(x, 0.07, 0.1 * z), new Vec3(x + sweep, 0.12, 0.26 * z), new Vec3(x + sweep * 1.8, 0.0, 0.34 * z) }, new[] { 0.02, 0.016, 0.01 }, 4);
            }
        }
    }

    private static void Silverfish(MeshData m)
    {
        // silverfish, length 1 along +X: a flattened, tapering carrot of a body, long antennae forward and three long
        // tail bristles behind, short legs tucked under the thorax
        var path = new List<Vec3>(); var radius = new List<double>();
        for (int i = 0; i <= 14; i++)
        {
            double t = i / 14.0;
            path.Add(new Vec3(0.36 - t * 0.74, 0.07, 0)); radius.Add(0.09 * Math.Pow(1 - t * 0.85, 0.9) * Math.Min(1, (t + 0.08) * 6) + 0.006);
        }
        int start = m.VertexCount;
        Primitives.Tube(m, path, radius, 12, (i, v) => (Body, 0, 0.75 - i / 14.0 * 0.7, v, v > 0.62 && v < 0.88 ? 3 : 1, 0), new Vec3(0, 0, 1));
        for (int i = start; i < m.VertexCount; i++)   // flatten top to bottom
        {
            var q = m.Position(i);
            m.Positions[i * 3 + 1] = (float)(0.07 + (q.Y - 0.07) * 0.45);
        }
        foreach (double z in new[] { -1.0, 1.0 })
        {
            Primitives.Ellipsoid(m, new Vec3(0.37, 0.085, 0.04 * z), new Vec3(0.015, 0.012, 0.012), 4, 6, (a, b) => Region(0.97, b, 2));
            Appendage(m, new[] { new Vec3(0.4, 0.08, 0.02 * z), new Vec3(0.6, 0.1, 0.12 * z), new Vec3(0.85, 0.08, 0.2 * z) }, new[] { 0.009, 0.006, 0.003 }, 3);
            for (int leg = 0; leg < 3; leg++)
            {
                double x = 0.26 - leg * 0.08;
                Appendage(m, new[] { new Vec3(x, 0.04, 0.06 * z), new Vec3(x - 0.03, 0.02, 0.12 * z), new Vec3(x - 0.06, 0.0, 0.14 * z) }, new[] { 0.012, 0.009, 0.006 }, 3);
            }
            Appendage(m, new[] { new Vec3(-0.37, 0.07, 0.01 * z), new Vec3(-0.6, 0.075, 0.1 * z), new Vec3(-0.8, 0.07, 0.16 * z) }, new[] { 0.008, 0.005, 0.003 }, 3);
        }
        Appendage(m, new[] { new Vec3(-0.37, 0.07, 0), new Vec3(-0.62, 0.08, 0), new Vec3(-0.85, 0.075, 0) }, new[] { 0.008, 0.005, 0.003 }, 3);
    }

    private static void Slug(MeshData m)
    {
        BodySegment(m, new Vec3(-0.05, 0.08, 0), new Vec3(0.43, 0.075, 0.13), 6, 12, (a,b) => Region(0.45, b, 1), pitch: -0.04);
        BodySegment(m, new Vec3(0.12, 0.145, 0), new Vec3(0.19, 0.065, 0.11), 5, 10, (a,b) => Region(0.72, b, 2));
        foreach (double z in new[] {-1.0, 1.0})
        {
            Appendage(m, new[] { new Vec3(0.31,0.13,0.055*z), new Vec3(0.43,0.21,0.11*z), new Vec3(0.50,0.24,0.14*z) }, new[] {0.015,0.009,0.005}, 5, 4, 3);
            BodySegment(m, new Vec3(0.505,0.242,0.142*z), new Vec3(0.012,0.012,0.012), 3, 5, (a,b)=>Region(0.98,b,3));
        }
    }

    private static void Millipede(MeshData m)
    {
        const int n = 12;
        for (int k=0;k<n;k++)
        {
            double t=k/(double)(n-1), x=0.44-t*0.88, r=0.065*(0.65+0.35*Math.Sin(Math.PI*t));
            BodySegment(m,new Vec3(x,0.085,0),new Vec3(0.047,r,r*1.15),3,8,(a,b)=>Region(1-t,b,1));
            foreach(double z in new[]{-1.0,1.0})
                Appendage(m,new[]{new Vec3(x,0.055,r*0.75*z),new Vec3(x-0.015,0.025,(r+0.055)*z),new Vec3(x+0.01,0,(r+0.09)*z)},new[]{0.009,0.006,0.003},3);
        }
        BodySegment(m,new Vec3(0.50,0.09,0),new Vec3(0.065,0.06,0.07),4,8,(a,b)=>Region(0.98,b,2));
    }

    private static void Moth(MeshData m)
    {
        BodySegment(m,new Vec3(0.08,0.13,0),new Vec3(0.18,0.08,0.08),5,9,(a,b)=>Region(0.55,b,1));
        BodySegment(m,new Vec3(0.29,0.14,0),new Vec3(0.09,0.08,0.09),4,8,(a,b)=>Region(0.9,b,2));
        BodySegment(m,new Vec3(-0.18,0.12,0),new Vec3(0.18,0.055,0.055),4,8,(a,b)=>Region(0.28,b,1));
        foreach(double z in new[]{-1.0,1.0})
        {
            var a=new Vec3(0.05,0.16,0.04*z);
            AppendageFan(m,a,a,new[]{a+new Vec3(0.12,0.02,0.38*z),a+new Vec3(-0.12,0.02,0.48*z),a+new Vec3(-0.28,-0.01,0.24*z)},Vec3.Up);
            var ant=new Vec3(0.34,0.17,0.035*z);
            Appendage(m,new[]{ant,ant+new Vec3(0.11,0.07,0.07*z),ant+new Vec3(0.22,0.09,0.13*z)},new[]{0.009,0.005,0.003},3,4,3);
        }
    }

    private static void Toad(MeshData m)
    {
        BodySegment(m,new Vec3(-0.06,0.20,0),new Vec3(0.30,0.19,0.25),7,12,(a,b)=>Region(0.45,b,1));
        BodySegment(m,new Vec3(0.25,0.23,0),new Vec3(0.18,0.15,0.23),6,11,(a,b)=>Region(0.82,b,2));
        foreach(double z in new[]{-1.0,1.0})
        {
            BodySegment(m,new Vec3(0.31,0.35,0.14*z),new Vec3(0.05,0.055,0.05),4,7,(a,b)=>Region(0.96,b,3));
            Appendage(m,new[]{new Vec3(-0.20,0.14,0.16*z),new Vec3(-0.34,0.08,0.33*z),new Vec3(-0.08,0.02,0.43*z)},new[]{0.05,0.04,0.025},5);
            Appendage(m,new[]{new Vec3(0.19,0.13,0.16*z),new Vec3(0.31,0.06,0.28*z),new Vec3(0.39,0.01,0.34*z)},new[]{0.04,0.03,0.018},5);
        }
    }

    private static void Salamander(MeshData m)
    {
        BodySegment(m,new Vec3(0.02,0.11,0),new Vec3(0.34,0.09,0.10),6,10,(a,b)=>Region(0.52,b,1));
        BodySegment(m,new Vec3(0.37,0.12,0),new Vec3(0.12,0.09,0.11),5,9,(a,b)=>Region(0.92,b,2));
        var path=new List<Vec3>(); var rad=new List<double>();
        for(int i=0;i<=8;i++){double t=i/8.0; path.Add(new Vec3(-0.28-t*0.42,0.10+0.02*Math.Sin(t*Math.PI),0)); rad.Add(0.07*(1-t)+0.008);}
        Primitives.Tube(m,path,rad,8,(i,v)=>(Body,0,0.2,v,1,0));
        foreach(double z in new[]{-1.0,1.0})
            foreach(double x in new[]{0.22,-0.14})
                Appendage(m,new[]{new Vec3(x,0.08,0.07*z),new Vec3(x+0.02,0.035,0.17*z),new Vec3(x+0.08,0.0,0.23*z)},new[]{0.025,0.017,0.009},4);
    }

    private static void Worm(MeshData m)
    {
        var path=new List<Vec3>(); var rad=new List<double>();
        for(int i=0;i<=16;i++){double t=i/16.0; path.Add(new Vec3(0.48-t*0.96,0.055+0.012*Math.Sin(t*Math.PI*3),0.025*Math.Sin(t*Math.PI*2))); rad.Add(0.045*(0.55+0.45*Math.Sin(Math.PI*t))+0.006);}
        Primitives.Tube(m,path,rad,9,(i,v)=>(Body,0,1-i/16.0,v,1,0),new Vec3(0,0,1));
    }

    private static void Harvestman(MeshData m)
    {
        BodySegment(m,new Vec3(0,0.13,0),new Vec3(0.13,0.09,0.12),5,9,(a,b)=>Region(0.55,b,1));
        BodySegment(m,new Vec3(0.14,0.14,0),new Vec3(0.07,0.06,0.07),4,8,(a,b)=>Region(0.85,b,2));
        for(int leg=0;leg<4;leg++) foreach(double z in new[]{-1.0,1.0})
        {
            double x=0.13-leg*0.085;
            Appendage(m,new[]{new Vec3(x,0.11,0.08*z),new Vec3(x+(leg-1.5)*0.035,0.13,0.34*z),new Vec3(x+(leg-1.5)*0.10,0.01,0.55*z)},new[]{0.014,0.008,0.004},4);
        }
    }

    private static void AquaticLarva(MeshData m)
    {
        for(int k=0;k<8;k++){double t=k/7.0,x=0.25-t*0.58,r=0.075*(1-0.45*t); BodySegment(m,new Vec3(x,0.11,0),new Vec3(0.05,r,r),3,8,(a,b)=>Region(0.75-t*0.55,b,1));}
        BodySegment(m,new Vec3(0.36,0.12,0),new Vec3(0.13,0.085,0.11),5,9,(a,b)=>Region(0.94,b,2));
        foreach(double z in new[]{-1.0,1.0})
        {
            for(int leg=0;leg<3;leg++){double x=0.25-leg*0.10; Appendage(m,new[]{new Vec3(x,0.08,0.06*z),new Vec3(x+0.02,0.02,0.15*z),new Vec3(x+0.08,0.0,0.20*z)},new[]{0.014,0.009,0.004},4);}
            var tail=new Vec3(-0.34,0.11,0.02*z); AppendageFan(m,tail,tail,new[]{tail+new Vec3(-0.16,0.08,0.09*z),tail+new Vec3(-0.22,0,0.12*z),tail+new Vec3(-0.16,-0.07,0.09*z)},new Vec3(0,0,z));
        }
    }

    private static void Snail(MeshData m)
    {
        BodySegment(m,new Vec3(0.05,0.07,0),new Vec3(0.42,0.055,0.12),5,11,(a,b)=>Region(0.45,b,1));
        BodySegment(m,new Vec3(-0.08,0.22,0),new Vec3(0.22,0.22,0.10),8,14,(a,b)=>Region(0.35,b,2));
        BodySegment(m,new Vec3(0.36,0.10,0),new Vec3(0.11,0.07,0.09),4,8,(a,b)=>Region(0.9,b,1));
        foreach(double z in new[]{-1.0,1.0})
            Appendage(m,new[]{new Vec3(0.40,0.13,0.04*z),new Vec3(0.49,0.21,0.08*z),new Vec3(0.54,0.23,0.10*z)},new[]{0.012,0.007,0.004},4,4,3);
    }

    private static void Midge(MeshData m)
    {
        BodySegment(m,new Vec3(0.06,0.12,0),new Vec3(0.12,0.055,0.05),4,8,(a,b)=>Region(0.55,b,1));
        BodySegment(m,new Vec3(0.20,0.13,0),new Vec3(0.07,0.06,0.065),4,8,(a,b)=>Region(0.9,b,2));
        BodySegment(m,new Vec3(-0.14,0.115,0),new Vec3(0.15,0.035,0.035),4,8,(a,b)=>Region(0.25,b,1));
        foreach(double z in new[]{-1.0,1.0})
        {
            var a=new Vec3(0.04,0.15,0.025*z);
            AppendageFan(m,a,a,new[]{a+new Vec3(0.02,0.015,0.25*z),a+new Vec3(-0.20,0.01,0.32*z),a+new Vec3(-0.16,-0.01,0.08*z)},Vec3.Up);
            for(int leg=0;leg<3;leg++){double x=0.12-leg*0.10; Appendage(m,new[]{new Vec3(x,0.10,0.035*z),new Vec3(x,0.04,0.17*z),new Vec3(x-0.08,0.0,0.27*z)},new[]{0.008,0.004,0.002},3);}
        }
    }

    private static void Minnow(MeshData m)
    {
        // spindle body via lathe: profile radius along length
        var path = new List<Vec3>(); var radius = new List<double>();
        for (int i = 0; i <= 14; i++)
        {
            double t = i / 14.0;
            double x = 0.42 - t * 0.78;
            double r = 0.105 * Math.Pow(Math.Sin(Math.PI * Math.Pow(t, 0.8)), 0.75) + 0.004;
            path.Add(new Vec3(x, 0.14, 0)); radius.Add(r);
        }
        int start = m.VertexCount;
        Primitives.Tube(m, path, radius, 14, (i, v) => (Body, 0, 1 - i / 14.0, v, v > 0.35 && v < 0.65 ? 3 : 1, 0), new Vec3(0, 0, 1));
        // flatten laterally a little (fish are compressed side to side)
        for (int i = start; i < m.VertexCount; i++)
        {
            var p = m.Position(i);
            m.Positions[i * 3 + 2] = (float)(p.Z * 0.72);
        }
        foreach (double z in new[] { -1.0, 1.0 })
            Primitives.Ellipsoid(m, new Vec3(0.33, 0.16, 0.055 * z), new Vec3(0.022, 0.022, 0.012), 4, 6, (a, b) => Region(0.95, b, 2));
        // forked caudal fin
        var ta = new Vec3(-0.36, 0.14, 0);
        AppendageFan(m, ta, ta, new List<Vec3> { ta + new Vec3(0, 0.02, 0), ta + new Vec3(-0.2, 0.13, 0), ta + new Vec3(-0.12, 0.0, 0), ta + new Vec3(-0.2, -0.13, 0), ta + new Vec3(0, -0.02, 0) }, new Vec3(0, 0, 1));
        // dorsal + anal fins
        var da = new Vec3(-0.05, 0.24, 0);
        AppendageFan(m, da, da, new List<Vec3> { da + new Vec3(0.06, 0, 0), da + new Vec3(-0.02, 0.08, 0), da + new Vec3(-0.1, 0.0, 0) }, new Vec3(0, 0, 1));
        var aa = new Vec3(-0.12, 0.06, 0);
        AppendageFan(m, aa, aa, new List<Vec3> { aa + new Vec3(0.05, 0, 0), aa + new Vec3(-0.03, -0.06, 0), aa + new Vec3(-0.09, 0, 0) }, new Vec3(0, 0, 1));
        foreach (double z in new[] { -1.0, 1.0 })
        {
            var pa = new Vec3(0.2, 0.1, 0.06 * z);
            AppendageFan(m, pa, pa, new List<Vec3> { pa, pa + new Vec3(-0.08, -0.03, 0.06 * z), pa + new Vec3(-0.03, -0.05, 0.02 * z) }, new Vec3(0, 1, 0));
        }
    }
}
