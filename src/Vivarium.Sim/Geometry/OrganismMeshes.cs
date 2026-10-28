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
public static partial class OrganismMeshes
{
    public static MeshData Flora(FloraSpeciesDef sp, ulong seed = 1, bool juvenile = false, int? visualDetail = null, FloraLodParams? lod = null)
    {
        var rng = Rng.Keyed(seed, "flora.mesh." + sp.Id, 0);
        var m = new MeshData { FloraDetailLevel = visualDetail, FloraVisualSeed = seed, Lod = lod };
        var c1 = sp.Color; var c2 = sp.Color2;
        switch (sp.Shape)
        {
            case "carpet":
                if (sp.Id == "siltstars") Siltstars(m, rng, seed, c1, c2);
                else if (sp.Id == "floodlace_moss") Floodlace(m, rng, seed, c1, c2);
                else if (sp.Id == "velvetweave_moss") Velvetweave(m, rng, seed, c1, c2);
                else if (sp.Id == "starmoss") Starmoss(m, rng, seed, c1, c2);
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
            case "creeper": Coinrunner(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height); break;
            case "reed":
                if (sp.Id == "ringreed") Ringreed(m, rng, seed, c1, c2);
                else if (sp.Id == "lanternsedge") LanternsedgeGeometry.Build(m, rng, juvenile);
                else Glassrush(m, rng, seed, c1, c2, juvenile);
                break;
            case "herb":
            {
                // irregular basal crown of kernel-built lanceolate leaves (cambered, midrib-folded, drooping tips,
                // random overlapping angles/lengths, always GREEN regardless of species tint) around several
                // leaning flower stalks, each carrying a kernel-staged cambered cup coloured with the species
                // tint (lilac/violet) so the plant no longer reads as a flat lilac star.
                var leafBase = new[] { 0.14, 0.42, 0.10 };
                var leafTip = new[] { 0.32, 0.62, 0.22 };
                int nLeaves = 6 + rng.NextInt(3); // 6..8 irregular leaves; low detail retains 72 triangles per blade
                for (int k = 0; k < nLeaves; k++)
                {
                    double ang = rng.Range(0, 2 * Math.PI); // fully irregular, not evenly spaced -> leaves overlap
                    double len = Math.Min(1.0, 0.66 * rng.Range(0.6, 1.5)); // per-leaf size jitter ~0.6..1.5x
                    var leafAxis = new AxisParams(Length: len, BaseAngle: rng.Range(0.5, 0.95), BaseAzimuth: ang,
                        Droop: rng.Range(0.7, 1.4), WobbleAmplitude: 0.015, WobbleFrequency: 1.1, Segments: 5);
                    ulong lSeed = Rng.Mix(seed, (ulong)(k * 401 + 3));
                    var bp = new LeafBladeParams(
                        Midrib: leafAxis, Profile: BladeProfile.Lanceolate, HalfWidth: rng.Range(0.08, 0.13),
                        Camber: rng.Range(0.08, 0.14), MidribFold: rng.Range(0.08, 0.15),
                        Cup: rng.Range(0.04, 0.09), TipCurl: rng.Range(0.05, 0.14),
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
                if (sp.Id == "mooncoin") Mooncoin(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height);
                else if (sp.Id == "brooklace") Brooklace(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height);
                else RoundLeaf(m, rng, seed, c1, c2);
                break;
            case "pairedleaf":
                if (sp.Id == "fenbead") Fenbead(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height);
                else PairedLeaf(m, rng, seed, c1, c2);
                break;
            case "floatleaf": FloatLeaf(m, rng, seed, c1, c2); break;
            case "capitula": BogglassMoss(m, rng, seed, c1, c2); break;
            case "trifoliate":
                // The clover is authored in real metres, so it needs the species' own footprint and
                // height. Fall back to the published values when a caller builds a bare definition.
                TrifoldClover(m, rng, seed, c1, c2,
                    sp.RadiusAtMax > 0.001 ? sp.RadiusAtMax : 0.22,
                    sp.Height > 0.001 ? sp.Height : 0.08);
                break;
            case "iceplant": Glassfinger(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height); break;
            case "fern": case "veilfern":
                // The veilfern is an ostrich fern and carries its own anatomy; other ferns, including
                // the moonfern variant, keep the shared form kernel.
                if (sp.Id == "veilfern")
                    Veilfern(m, rng, seed, c1, c2, juvenile,
                        sp.RadiusAtMax > 0.001 ? sp.RadiusAtMax : 0.28,
                        sp.Height > 0.001 ? sp.Height : 0.30);
                else Fern(m, rng, seed, c1, c2, juvenile, sp.Id == "moonfern");
                break;
            case "vine":
                if (sp.Id == "spiralvine") Spiralvine(m, rng, seed, c1, c2, juvenile);
                else if (sp.Id == "fenhook") Fenhook(m, rng, seed, c1, c2, juvenile);
                else Clinglace(m, rng, seed, c1, c2, juvenile);
                break;
            case "vine_clinglace": case "clinglace": Clinglace(m, rng, seed, c1, c2, juvenile); break;
            case "vine_spiralvine": case "spiralvine": Spiralvine(m, rng, seed, c1, c2, juvenile); break;
            case "vine_fenhook": case "fenhook": Fenhook(m, rng, seed, c1, c2, juvenile); break;
            case "mushroom_cluster":
                if (sp.Id == "ghostbonnet") GhostbonnetGeometry.Build(m, rng, c1, c2, juvenile);
                else Mushrooms(m, rng, seed, c1, c2, juvenile);
                break;
            case "bracket": Bracket(m, rng, c1, c2); break;
            case "epiphytic_rosette":
            case "epiphyte":
            case "rosette":
            case "bromeliad":
            case "tillandsia":
            case "succulent_perch":
            case "perched_rosette":
            case "perched_epiphyte":
                EpiphyticRosetteMesh(m, rng, seed, c1, c2, juvenile);
                break;
            case "succulent":
                if (sp.Id.Contains("epiphyte") || sp.Id.Contains("perch"))
                    EpiphyticRosetteMesh(m, rng, seed, c1, c2, juvenile);
                else
                    Succulent(m, rng, seed, c1, c2, juvenile);
                break;
            case "tussock": Tussock(m, rng, seed, c1, c2, juvenile); break;
            case "pampas": Plumegrass(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height, juvenile); break;
            case "kinkcane_brake": KinkcaneAlienGeometry.Build(m, rng, c1, c2, sp.Height / Math.Max(0.01, sp.RadiusAtMax)); break;
            case "veilblade_curtain": Veilblade(m, rng, seed, c1, c2); break;
            case "hookthicket_brake":
                // The brake is a bramble thicket and carries its own anatomy; other brakes keep the old form.
                HookthicketBramble(m, rng, seed, c1, c2, juvenile,
                    sp.RadiusAtMax > 0.001 ? sp.RadiusAtMax : 0.9,
                    sp.Height > 0.001 ? sp.Height : 1.15);
                break;
            case "sundew_mat": BlueSundewRosette(m, rng, sp.RadiusAtMax, sp.Height); break;
            case "pitcher_rosette": PitcherPlant(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height); break;
            case "snaptrap_rosette":
                // The latchjaw is a column of jaw-leaves under one large fleshy flower.
                LatchJawColumn(m, rng, seed, c1, c2, juvenile,
                    sp.RadiusAtMax > 0.001 ? sp.RadiusAtMax : 0.27,
                    sp.Height > 0.001 ? sp.Height : 0.20);
                break;
            case "rain_jelly": RainJelly(m, rng, c1, c2); break;
            case "carrion_bell": CarrionBell(m, rng, c1, c2); break;
            case "glass_antlers": GlassAntlers(m, rng, c1, c2); break;
            case "fruticose":
                if (sp.Id == "antlerlace_lichen") Antlerlace(m, rng, seed, c1, c2);
                else Fruticose(m, rng, c1, c2);
                break;
            case "shrub_embercrown": ShrubEmbercrown(m, rng, seed, c1, c2, juvenile); break;
            case "shrub_lanternbrush": ShrubLanternbrush(m, rng, c1, c2); break;
            case "shrub_glowbriar": ShrubGlowbriar(m, rng, seed, c1, c2, juvenile); break;
            case "shrub_shadebell": ShrubShadebell(m, rng, seed, c1, c2, juvenile); break;
            case "microcanopy_splitcrown": Splitcrown(m, rng, seed, c1, c2, juvenile); break;
            case "microcanopy_vaultfern": Vaultfern(m, rng, seed, c1, c2, juvenile); break;
            case "microcanopy_rainshield": MidRainshield(m, rng, seed, c1, c2, juvenile); break;
            case "microcanopy_lacemantle": Lacemantle(m, rng, seed, c1, c2, juvenile); break;
            case "microcanopy_cragparasol": Cragparasol(m, rng, seed, c1, c2, juvenile); break;
            case "ribbonweed": case "streamribbon": Streamribbon(m, rng, seed, c1, c2, juvenile); break;
            case "milfoil": case "fencomb": Fencomb(m, rng, seed, c1, c2, juvenile); break;
            // The 2026-10-21 new-flora set. Each carries its own anatomy; see docs/flora-new-set.md.
            case "glasslily_pad": Glasslily(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height); break;
            case "submerged_whorl": Silverweed(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height); break;
            case "coalspike_cattail": Coalspike(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height); break;
            case "marrowbloom_stem": Marrowbloom(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height); break;
            case "blackneedle_thistle": Blackneedle(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height); break;
            case "starbulb_umbel": Starbulb(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height); break;
            case "bonecup_fungus": Bonecup(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height); break;
            case "embercup_moss": Embercup(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height); break;
            case "hollowcrown_tree": Hollowcrown(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height, juvenile); break;
            case "tankbloom_bromeliad": Tankbloom(m, rng, seed, c1, c2, sp.RadiusAtMax, sp.Height); break;
            case "tree":
            case "tree_split":
            case "ironlace":
            case "broadleaf_tree":
                TreeSplitTrunk(m, rng, seed, c1, c2, juvenile);
                break;
            default:
                if (sp.IsTree)
                    TreeSplitTrunk(m, rng, seed, c1, c2, juvenile);
                else
                    Primitives.Ellipsoid(m, Vec3.Zero, new Vec3(1, 1, 1), 6, 8, (a, b) => (c1, 1, a, b, 0, 0));
                break;
        }
        return m;
    }

    /// <summary>Alternate pose for organisms with a visible reproductive stage (slime mold sporangia); null otherwise.</summary>
    /// <summary>
    /// The growing tip of a shoot: a thin tendril reaching ahead of the stem with the smallest leaves it will
    /// ever carry. The node's local +Y is along the stem and +Z is the upper side, so the tendril reaches forward
    /// and curls up on the same side as the foliage.
    /// </summary>
    public static MeshData ClimberNodeTip(FloraSpeciesDef sp, ulong seed, FloraLodParams? lod = null)
    {
        var m = new MeshData { Lod = lod };
        var rng = Rng.Keyed(seed, "climber.tip", 0);
        var c1 = sp.Color;
        var c2 = sp.Color2;
        var stemCol = Primitives.Mix(WoodyBarkLight, c1, 0.32);
        var leafCol = Primitives.Mix(c1, c2, 0.45);

        // A slender tendril reaching forward along the stem and curling up on its upper side. It is short enough
        // to stay against the surface it is growing on, which is where a tendril belongs.
        var p0 = new Vec3(0, 0, 0);
        var p1 = new Vec3(0, 0.016, 0.002);
        var p2 = new Vec3(0, 0.030, 0.006);
        var p3 = new Vec3(0, 0.040, 0.013);
        Primitives.Tube(m, new[] { p0, p1, p2, p3 }, new[] { 0.0017, 0.0013, 0.0009, 0.0004 }, 5,
            (i, v) => (stemCol, 1.0, i / 3.0, v, 0, 0));

        // Two small leaves at the base of the tendril, on the upper side of the stem.
        foreach (double sign in new[] { -1.0, 1.0 })
        {
            var lDir = new Vec3(sign * 0.40, 0.26, 0.88).Normalized();
            double len = rng.Range(0.028, 0.040);
            var lSide = lDir.Cross(new Vec3(0, 0, 1)).Normalized();
            int bladeStart = m.VertexCount;
            FoliageBlade.Build(m, lDir * 0.006, lDir * 0.006 + lDir * len, lSide, len * 0.26,
                Primitives.Scale(leafCol, 0.80), leafCol, camber: 0.14, curl: 0.16, shoulder: 0.78, segments: 3);
            for (int v = bladeStart; v < m.VertexCount; v++)
                if (m.UV2[v * 2] > 0.5f) m.SetColor(v, c2[0], c2[1], c2[2], 1.0);
        }
        return m;
    }

    /// <summary>Generates a single modular node foliage cluster for a climber segment (attached or ground runner).</summary>
    public static MeshData ClimberNode(FloraSpeciesDef sp, ulong seed, bool attached, FloraLodParams? lod = null)
    {
        var m = new MeshData { Lod = lod };
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

    public static MeshData? FloraFruiting(FloraSpeciesDef sp, ulong seed = 1, int? visualDetail = null, FloraLodParams? lod = null)
    {
        if (sp.Reproduction is not { } rp) return null;
        var rngFruit = Rng.Keyed(seed, "flora.reproduction." + sp.Id, 0);
        var plant = Flora(sp, seed, visualDetail: visualDetail, lod: lod);
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

        // Determine authentic branch anchor positions across the plant's branches/stems.
        double maxY = 0;
        for (int v = 0; v < plant.VertexCount; v++)
        {
            double y = plant.Positions[v * 3 + 1];
            if (y > maxY) maxY = y;
        }
        double minYForFruit = Math.Max(0.12, maxY * (sp.IsTree ? 0.38 : 0.25));

        var anchors = new List<(Vec3 Pos, Vec3 Outward, Vec3 BranchDir)>();

        // 1. Sample structural tubes (stems, canes, limbs, branches)
        foreach (var st in plant.Structural)
        {
            int sides = st.Sides;
            int vCount = st.VertexCount;
            if (sides < 3 || vCount < (sides + 1) * 2) continue;
            int numRings = vCount / (sides + 1);
            int startRing = Math.Max(1, (int)(numRings * 0.28));
            for (int r = startRing; r < numRings; r++)
            {
                Vec3 rc = Vec3.Zero;
                int ringStart = st.FirstVertex + r * (sides + 1);
                for (int s = 0; s < sides; s++) rc += plant.Position(ringStart + s);
                rc *= (1.0 / sides);
                if (rc.Y < minYForFruit) continue;

                Vec3 bDir = r < numRings - 1
                    ? (plant.Position(st.FirstVertex + (r + 1) * (sides + 1)) - rc)
                    : (r > 0 ? rc - plant.Position(st.FirstVertex + (r - 1) * (sides + 1)) : Vec3.Up);
                if (bDir.LengthSq > 1e-8) bDir = bDir.Normalized();
                else bDir = Vec3.Up;

                int step = Math.Max(1, sides / 3);
                for (int s = 0; s < sides; s += step)
                {
                    var p = plant.Position(ringStart + s);
                    var outw = p - rc;
                    if (outw.LengthSq > 1e-8) outw = outw.Normalized();
                    else outw = plant.NormalAt(ringStart + s);
                    anchors.Add((p, outw, bDir));
                }
            }
        }

        // 2. Leaf attachment nodes (axils where leaves join branches)
        foreach (var p in plant.AttachmentNodes)
        {
            if (p.Y < minYForFruit) continue;
            var outw = new Vec3(p.X, 0, p.Z);
            if (outw.LengthSq > 1e-6) outw = outw.Normalized();
            else outw = Vec3.Up;
            anchors.Add((p, outw, Vec3.Up));
        }

        // 3. Fallback: sample surface vertices from upper plant geometry if no tubes/nodes were found
        if (anchors.Count == 0 && plant.VertexCount > 0)
        {
            int stride = Math.Max(1, plant.VertexCount / 100);
            for (int v = 0; v < plant.VertexCount; v += stride)
            {
                var p = plant.Position(v);
                if (p.Y >= minYForFruit)
                {
                    var n = plant.NormalAt(v);
                    anchors.Add((p, n.LengthSq > 1e-6 ? n.Normalized() : Vec3.Up, Vec3.Up));
                }
            }
        }

        // 4. Ultimate fallback if empty mesh
        if (anchors.Count == 0)
        {
            anchors.Add((new Vec3(0, Math.Max(0.5, maxY * 0.7), 0), Vec3.Up, Vec3.Up));
        }

        var pedicelCol = Primitives.Mix(new[] { 0.24, 0.19, 0.14 }, sp.Color, 0.35);
        double pedR = Math.Max(0.0016, rx * 0.10);

        for (int i = 0; i < count; i++)
        {
            int anchorIdx = (int)(((i + rngFruit.Range(0.05, 0.95)) / count) * anchors.Count) % anchors.Count;
            var (anchor, outward, bDir) = anchors[anchorIdx];

            var col = rngFruit.NextDouble() < 0.35 ? ripe2 : fruitCol;
            double pedLenX = rx * rngFruit.Range(1.3, 2.2);
            double pedLenY = ry * rngFruit.Range(1.5, 2.6);

            var side = bDir.Cross(outward);
            if (side.LengthSq < 1e-8) side = new Vec3(-outward.Z, 0, outward.X);
            if (side.LengthSq > 1e-8) side = side.Normalized(); else side = new Vec3(1, 0, 0);
            double lateralJitter = rngFruit.Range(-0.35, 0.35);
            var hangOut = (outward * 0.50 + side * lateralJitter).Normalized();

            switch (rp.Form)
            {
                case "pod":
                {
                    var midP = anchor + hangOut * (pedLenX * 0.30) - Vec3.Up * (pedLenY * 0.30);
                    var fruitTop = anchor + hangOut * (pedLenX * 0.25) - Vec3.Up * (pedLenY * 0.85);
                    var c = fruitTop - Vec3.Up * (ry * 2.2);
                    Primitives.Tube(plant, new[] { anchor, midP, fruitTop },
                        new[] { pedR * 1.3, pedR, pedR * 0.75 }, 3,
                        (u, v) => (pedicelCol, 1, u, v, 2.0, 0));
                    Primitives.Ellipsoid(plant, c, new Vec3(rx * 0.55, ry * 2.5, rx * 0.55), 5, 8,
                        (u, v) => (col, 1, u, v, 0, 0), pitch: rngFruit.Range(-0.25, 0.25));
                    break;
                }
                case "cone":
                {
                    var coneDir = (outward * 0.75 + (rngFruit.NextDouble() < 0.5 ? Vec3.Up : -Vec3.Up) * 0.55).Normalized();
                    var stalkLen = pedLenX * 0.65;
                    var coneBase = anchor + coneDir * stalkLen;
                    var coneCenter = coneBase + coneDir * (ry * 1.5);
                    Primitives.Tube(plant, new[] { anchor, coneBase }, new[] { pedR * 1.8, pedR * 1.3 }, 3,
                        (u, v) => (pedicelCol, 1, u, v, 2.0, 0));
                    Primitives.Ellipsoid(plant, coneCenter, new Vec3(rx * 0.9, ry * 1.8, rx * 0.9), 6, 9,
                        (u, v) => (Primitives.Mix(col, ripe2, v), 1, u, v, 0, 0), pitch: rngFruit.Range(-0.35, 0.35));
                    break;
                }
                case "capsule":
                {
                    var midP = anchor + hangOut * (pedLenX * 0.45) - Vec3.Up * (pedLenY * 0.25);
                    var fruitTop = anchor + hangOut * (pedLenX * 0.40) - Vec3.Up * (pedLenY * 0.85);
                    var c = fruitTop - Vec3.Up * (ry * 1.3);
                    Primitives.Tube(plant, new[] { anchor, midP, fruitTop },
                        new[] { pedR * 1.3, pedR, pedR * 0.75 }, 3,
                        (u, v) => (pedicelCol, 1, u, v, 2.0, 0));
                    Primitives.Ellipsoid(plant, c, new Vec3(rx * 0.7, ry * 1.45, rx * 0.7), 5, 8,
                        (u, v) => (col, 1, u, v, 0, 0));
                    break;
                }
                case "wind_seed":
                case "achene":
                {
                    var midP = anchor + hangOut * (pedLenX * 0.35) + Vec3.Up * (pedLenY * 0.20);
                    var c = anchor + hangOut * (pedLenX * 0.70) + Vec3.Up * (pedLenY * 0.50);
                    Primitives.Tube(plant, new[] { anchor, midP, c },
                        new[] { pedR * 1.1, pedR * 0.8, pedR * 0.5 }, 3,
                        (u, v) => (pedicelCol, 1, u, v, 2.0, 0));
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
                default: // "berry", "drupe", "nut"
                {
                    var midP = anchor + hangOut * (pedLenX * 0.45) - Vec3.Up * (pedLenY * 0.25);
                    var fruitTop = anchor + hangOut * (pedLenX * 0.40) - Vec3.Up * (pedLenY * 0.85);
                    var c = fruitTop - Vec3.Up * ry;
                    Primitives.Tube(plant, new[] { anchor, midP, fruitTop },
                        new[] { pedR * 1.3, pedR, pedR * 0.75 }, 3,
                        (u, v) => (pedicelCol, 1, u, v, 2.0, 0));
                    Primitives.Ellipsoid(plant, c, new Vec3(rx, ry, rx), 5, 8,
                        (u, v) => (col, 1, u, v, 0, 0));
                    break;
                }
            }
        }
        return plant;
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
        foreach (var st in src.Structural) dst.Structural.Add((st.FirstVertex + baseIndex, st.VertexCount, st.Sides, st.Radius));
        foreach (var an in src.AttachmentNodes) dst.AttachmentNodes.Add(Rot(an) + translate);
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
        foreach (var st in src.Structural) dst.Structural.Add((st.FirstVertex + baseIndex, st.VertexCount, st.Sides, st.Radius));
        foreach (var an in src.AttachmentNodes) dst.AttachmentNodes.Add(an + translate);
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
        var tmp = new MeshData { Lod = m.Lod };
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
    // Glassfinger lives in its own file: OrganismMeshes.Glassfinger.cs.


    // Ringreed lives in its own file: OrganismMeshes.Ringreed.cs.

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

            var tmp = new MeshData { Lod = m.Lod };
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
            var bladeTmp = new MeshData { Lod = m.Lod };
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

            var tmp = new MeshData { Lod = m.Lod };
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

    /// <summary>Cheap double-sided cupped ovate leaflet oriented by an explicit (fwd, side, up) basis ÃƒÂ¢Ã¢â€šÂ¬Ã¢â‚¬Â the
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
    /// <summary>
    /// Tidebell (PairedLeaf): bioluminescent emergent herb of shallow pond shelves.
    /// - Sprawling clusters of succulent decussate stems with pairs of waxy, thick jade-green leaves.
    /// - Nodding, pendulous campanulate bell flowers held on arched pedicels above the waterline.
    /// - Deep glowing amber bioluminescent interior cavity (UV2.x = 1.8, UV2.y = 1.0).
    /// </summary>
    /// <summary>
    /// Tidebell (PairedLeaf): bioluminescent emergent herb of shallow pond shelves.
    /// - Sprawling clusters of succulent decussate stems with pairs of waxy, thick jade-green leaves.
    /// - Nodding, pendulous campanulate bell flowers held on arched pedicels above the waterline.
    /// - Deep glowing amber bioluminescent interior cavity (UV2.x = 1.8, UV2.y = 1.0).
    /// </summary>
    private static void PairedLeaf(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var leafBaseCol = Primitives.Mix(c1, new[] { 0.14, 0.28, 0.22 }, 0.45);
        var leafRimCol = Primitives.Mix(c1, new[] { 0.35, 0.58, 0.45 }, 0.35);
        var stemCol = Primitives.Mix(c1, new[] { 0.30, 0.44, 0.25 }, 0.5);
        var calyxCol = Primitives.Mix(c1, new[] { 0.20, 0.36, 0.22 }, 0.6);
        var bellOuterCol = Primitives.Mix(c2, new[] { 0.82, 0.74, 0.45 }, 0.55);
        var bellInnerGlow = new[] { 1.0, 0.62, 0.25 }; // Warm amber bioluminescent glow

        int stems = 5 + rng.NextInt(4); // 5..8 succulent stems
        for (int k = 0; k < stems; k++)
        {
            double ang = rng.Range(0, Math.Tau);
            double stemR = Math.Sqrt(rng.NextDouble()) * 0.65;
            var basePos = new Vec3(Math.Cos(ang) * stemR, 0, Math.Sin(ang) * stemR);
            double stemLen = rng.Range(0.42, 0.70);
            int pairs = 4 + rng.NextInt(4); // 4..7 pairs

            var stemAxis = new AxisParams(
                Length: stemLen,
                BaseAngle: rng.Range(1.10, 1.40),
                BaseAzimuth: 0,
                Droop: rng.Range(-0.10, 0.25),
                PhototropicBend: rng.Range(0.12, 0.28),
                WobbleAmplitude: 0.012,
                WobbleFrequency: 1.5,
                Segments: 8);

            ulong sSeed = Rng.Mix(seed, (ulong)(k * 211 + 11));
            var stemFrames = Axis.Build(stemAxis, sSeed);

            var tmp = new MeshData { Lod = m.Lod };
            SoftTube.Build(tmp, new SoftTubeParams(stemAxis, BaseRadius: 0.014, TipRadius: 0.007, Segments: 6), sSeed,
                (i, v) => (stemCol, 1, i, v, 0, 0));

            // Pairs of succulent waxy opposite leaves along nodes
            for (int p = 1; p <= pairs; p++)
            {
                double t = (double)p / (pairs + 1);
                var f = Axis.Sample(stemFrames, t);
                double taper = 1.0 - 0.45 * (double)p / pairs;
                double leafLen = rng.Range(0.14, 0.20) * taper;
                double leafWid = leafLen * 0.52;
                double leafThk = leafLen * 0.16;

                // Decussate arrangement: alternating 90 degrees each node
                double pairYaw = (p % 2 == 0) ? Math.PI * 0.5 : 0.0;
                var rotSide = Axis.RotateAround(f.Side, f.Tangent, pairYaw);

                foreach (double sgn in new[] { -1.0, 1.0 })
                {
                    var fwd = (rotSide * sgn + f.Tangent * 0.35).Normalized();
                    var up = fwd.Cross(f.Tangent).Normalized() * sgn;
                    if (up.LengthSq < 1e-8) up = Vec3.Up;
                    var side = fwd.Cross(up).Normalized();
                    var leafBase = f.Point + fwd * 0.012;

                    // Volumetric fleshy ovate-lanceolate leaf
                    int vStart = tmp.VertexCount;
                    const int leafSegs = 6;
                    var topIds = new List<int>();
                    var botIds = new List<int>();

                    for (int s = 0; s <= leafSegs; s++)
                    {
                        double lt = (double)s / leafSegs;
                        double w = leafWid * Math.Sin(Math.PI * Math.Pow(lt, 0.7));
                        double hCurve = -leafLen * 0.08 * lt * lt;
                        var pMid = leafBase + fwd * (leafLen * lt) + up * hCurve;

                        var col = Primitives.Mix(leafBaseCol, leafRimCol, lt * 0.7);
                        topIds.Add(tmp.AddVertex(pMid + up * (leafThk * 0.5) - side * (w * 0.5), up, col, 1.0, lt, 0.0, 0, 1.0));
                        topIds.Add(tmp.AddVertex(pMid + up * (leafThk * 0.5) + side * (w * 0.5), up, col, 1.0, lt, 1.0, 0, 1.0));

                        botIds.Add(tmp.AddVertex(pMid - up * (leafThk * 0.5) - side * (w * 0.5), -up, col, 1.0, lt, 0.0, 0, 1.0));
                        botIds.Add(tmp.AddVertex(pMid - up * (leafThk * 0.5) + side * (w * 0.5), -up, col, 1.0, lt, 1.0, 0, 1.0));
                    }

                    // Triangulate leaf top, bottom, and rim quads
                    for (int s = 0; s < leafSegs; s++)
                    {
                        int t0 = s * 2, t1 = t0 + 1, t2 = t0 + 2, t3 = t0 + 3;
                        // Top
                        Primitives.TriangleFacing(tmp, topIds[t0], topIds[t2], topIds[t1], up);
                        Primitives.TriangleFacing(tmp, topIds[t1], topIds[t2], topIds[t3], up);

                        // Bottom
                        Primitives.TriangleFacing(tmp, botIds[t0], botIds[t1], botIds[t2], -up);
                        Primitives.TriangleFacing(tmp, botIds[t1], botIds[t3], botIds[t2], -up);

                        // Left rim
                        Primitives.TriangleFacing(tmp, topIds[t0], botIds[t0], topIds[t2], -side);
                        Primitives.TriangleFacing(tmp, topIds[t2], botIds[t0], botIds[t2], -side);

                        // Right rim
                        Primitives.TriangleFacing(tmp, topIds[t1], topIds[t3], botIds[t1], side);
                        Primitives.TriangleFacing(tmp, topIds[t3], botIds[t3], botIds[t1], side);
                    }

                    tmp.RecordLeaf(vStart, 0, leafBase, leafLen, volumetric: true);
                }
            }

            // Terminal nodding bioluminescent campanulate bell flower (on 75% of stems)
            if (rng.NextDouble() < 0.75)
            {
                var tipF = stemFrames[^1];
                var archDir = (tipF.Tangent + new Vec3(rng.Range(-0.3, 0.3), 0, rng.Range(-0.3, 0.3))).Normalized();
                var pedSide = archDir.Cross(Vec3.Up).Normalized();
                if (pedSide.LengthSq < 1e-6) pedSide = new Vec3(1, 0, 0);

                // Arched nodding pedicel drooping downward
                double pedLen = rng.Range(0.06, 0.10);
                var p0 = tipF.Point;
                var p1 = p0 + archDir * (pedLen * 0.6) + Vec3.Up * (pedLen * 0.2);
                var p2 = p0 + archDir * pedLen - Vec3.Up * (pedLen * 0.35);

                Primitives.Tube(tmp, new[] { p0, p1, p2 }, new[] { 0.005, 0.004, 0.003 }, 4,
                    (st, u) => (stemCol, 1.0, st, u, 0, 0));

                // Nodding Campanulate Bell
                var bellAxis = (p2 - p1).Normalized();
                var bellSide = pedSide;
                var bellUp = bellSide.Cross(bellAxis).Normalized();
                double bellR = 0.038;
                double bellLen = 0.055;

                // Calyx sepals at base
                for (int c = 0; c < 5; c++)
                {
                    double cAz = c * (Math.Tau / 5);
                    var cDir = (bellSide * Math.Cos(cAz) + bellUp * Math.Sin(cAz)).Normalized();
                    var cP0 = p2;
                    var cP1 = p2 + bellAxis * (bellLen * 0.25) + cDir * (bellR * 0.55);
                    Primitives.Tube(tmp, new[] { cP0, cP1 }, new[] { 0.0035, 0.0015 }, 3,
                        (st, u) => (calyxCol, 1.0, st, u, 0, 0));
                }

                // Bell Corolla (Outer campanulate trumpet + glowing inner throat)
                const int bellRings = 8;
                const int bellSides = 15;
                int bOuterStart = tmp.VertexCount;
                int bInnerStart = bOuterStart + (bellRings + 1) * (bellSides + 1);

                for (int br = 0; br <= bellRings; br++)
                {
                    double t = (double)br / bellRings;
                    double flare = bellR * (0.28 + 0.72 * Math.Pow(t, 1.8));
                    double recurve = t > 0.8 ? (t - 0.8) * bellR * 0.3 : 0.0;
                    double z = bellLen * t;

                    for (int s = 0; s <= bellSides; s++)
                    {
                        double th = s * Math.Tau / bellSides;
                        double lobe = 1.0 + 0.15 * Math.Cos(5.0 * th) * Math.Pow(t, 2.0);
                        var radDir = (bellSide * Math.Cos(th) + bellUp * Math.Sin(th)).Normalized();

                        double rO = flare * lobe + recurve;
                        var pO = p2 + bellAxis * z + radDir * rO;
                        var colO = Primitives.Mix(bellOuterCol, new[] { 0.95, 0.88, 0.60 }, t * 0.45);

                        tmp.AddVertex(pO, radDir, colO, 1.0, t, s / (double)bellSides, 0, 1.0);
                    }
                }

                // Inner vertices
                for (int br = 0; br <= bellRings; br++)
                {
                    double t = (double)br / bellRings;
                    double flare = bellR * (0.28 + 0.72 * Math.Pow(t, 1.8));
                    double recurve = t > 0.8 ? (t - 0.8) * bellR * 0.3 : 0.0;
                    double z = bellLen * t;

                    for (int s = 0; s <= bellSides; s++)
                    {
                        double th = s * Math.Tau / bellSides;
                        double lobe = 1.0 + 0.15 * Math.Cos(5.0 * th) * Math.Pow(t, 2.0);
                        var radDir = (bellSide * Math.Cos(th) + bellUp * Math.Sin(th)).Normalized();

                        double rO = flare * lobe + recurve;
                        double rI = Math.Max(0.004, rO - 0.003);
                        var pI = p2 + bellAxis * z + radDir * rI;

                        var colI = Primitives.Mix(bellOuterCol, bellInnerGlow, MathD.SmoothStep(0.9, 0.1, t));
                        double glow = 1.8 * MathD.SmoothStep(0.85, 0.05, t);

                        tmp.AddVertex(pI, -radDir, colI, 1.0, t, s / (double)bellSides, glow, 1.0);
                    }
                }

                // Triangles
                for (int br = 0; br < bellRings; br++)
                {
                    for (int s = 0; s < bellSides; s++)
                    {
                        // Outer
                        int o0 = bOuterStart + br * (bellSides + 1) + s;
                        int o1 = o0 + bellSides + 1;
                        Primitives.TriangleFacing(tmp, o0, o1, o0 + 1, tmp.NormalAt(o0));
                        Primitives.TriangleFacing(tmp, o0 + 1, o1, o1 + 1, tmp.NormalAt(o0 + 1));

                        // Inner
                        int i0 = bInnerStart + br * (bellSides + 1) + s;
                        int i1 = i0 + bellSides + 1;
                        Primitives.TriangleFacing(tmp, i0, i0 + 1, i1, tmp.NormalAt(i0));
                        Primitives.TriangleFacing(tmp, i0 + 1, i1 + 1, i1, tmp.NormalAt(i0 + 1));
                    }
                }

                // Rim lip connecting outer and inner corolla
                int lastO = bOuterStart + bellRings * (bellSides + 1);
                int lastI = bInnerStart + bellRings * (bellSides + 1);
                for (int s = 0; s < bellSides; s++)
                {
                    Primitives.TriangleFacing(tmp, lastO + s, lastO + s + 1, lastI + s, bellAxis);
                    Primitives.TriangleFacing(tmp, lastO + s + 1, lastI + s + 1, lastI + s, bellAxis);
                }

                // Central hanging style and golden stamen anthers inside bell
                var styleTip = p2 + bellAxis * (bellLen * 0.70);
                Primitives.Tube(tmp, new[] { p2, styleTip }, new[] { 0.0025, 0.0015 }, 3,
                    (st, u) => (new[] { 0.95, 0.92, 0.75 }, 1.0, st, u, 0.5, 1.0));
                double antherR = 0.006;
                Primitives.Ellipsoid(tmp, styleTip, new Vec3(antherR, antherR * 1.4, antherR), 3, 5,
                    (u, v) => (new[] { 0.98, 0.82, 0.28 }, 1.0, u, v, 1.2, 1.0));
            }

            double leafYawWhole = rng.Range(0, Math.Tau);
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
        var yellow = new[] { 0.88, 0.82, 0.35 };
        var youngCol = new[] { 0.62, 0.58, 0.20 };
        var underCol = new[] { 0.38, 0.14, 0.20 }; // Rich purple-bronze underside

        // 1. Rhizome crown on pond floor
        {
            var rim = new List<Vec3>();
            for (int s = 0; s <= 8; s++)
            {
                double th = 2 * Math.PI * s / 8;
                rim.Add(new Vec3(Math.Cos(th) * 0.035, 0, Math.Sin(th) * 0.035));
            }
            Primitives.Fan(m, new Vec3(0, 0.010, 0), rim, Vec3.Up, Primitives.Scale(c1, 0.45), Primitives.Scale(c1, 0.30));
        }

        int nLeaves = 5 + rng.NextInt(4); // 5..8 leaves
        int nYoung = 1 + rng.NextInt(2);  // 1..2 rolled scroll leaves
        int nOld = rng.NextInt(2);        // 0..1 older weathered pads

        for (int k = 0; k < nLeaves; k++)
        {
            double ang = rng.Range(0, 2 * Math.PI);
            double reach = rng.Range(0.11, 0.22); // Pad centre offset from rhizome (metres, well within 0.40m limit)
            bool isYoung = k < nYoung;
            bool isOld = !isYoung && k >= nLeaves - nOld;
            ulong lSeed = Rng.Mix(seed, (ulong)(k * 401 + 7));

            if (isYoung)
            {
                // Young rolled scroll: emergent shoot unfurling near water surface
                double riseY = 1.0 + rng.Range(0.04, 0.09);
                var dir = new Vec3(Math.Cos(ang), 0, Math.Sin(ang));
                var path = new List<Vec3> {
                    Vec3.Zero,
                    new Vec3(dir.X * reach * 0.35, riseY * 0.35, dir.Z * reach * 0.35),
                    new Vec3(dir.X * reach * 0.70, riseY * 0.75, dir.Z * reach * 0.70),
                    dir * reach + new Vec3(0, riseY, 0)
                };
                var rad = new List<double> { 0.0035, 0.0030, 0.0024, 0.0018 };
                Primitives.Tube(m, path, rad, 5, (i, v) => (Primitives.Scale(c1, 0.65), 1, i, v, 0, 0));

                var tipP = path[^1];
                var side = new Vec3(-Math.Sin(ang), 0, Math.Cos(ang));
                var coil = new List<Vec3>();
                var crad = new List<double>();
                for (int i = 0; i <= 8; i++)
                {
                    double t = i / 8.0, th = t * Math.PI * 2.4, rr = 0.024 * (1.0 - t * 0.55);
                    var p = tipP + new Vec3(0, 0.006 + 0.03 * t, 0) + side * (Math.Cos(th) * rr) + new Vec3(0, Math.Sin(th) * rr * 0.35, 0);
                    coil.Add(p);
                    crad.Add(0.0028 * (1.0 - t * 0.45));
                }
                int coilCount = coil.Count;
                Primitives.Tube(m, coil, crad, 4, (i, v) => (Primitives.Mix(c1, youngCol, i / (double)(coilCount - 1)), 1, i, v, 0, 0));
                continue;
            }

            // Mature floating pad lying flat at water surface (unit Y = 1.0)
            double padY = 1.0;
            var padCentre = new Vec3(Math.Cos(ang) * reach, padY, Math.Sin(ang) * reach);
            const double gap = 0.008;

            var petPath = new List<Vec3> {
                Vec3.Zero,
                new Vec3(padCentre.X * 0.30, padY * 0.35, padCentre.Z * 0.30),
                new Vec3(padCentre.X * 0.65, padY * 0.70, padCentre.Z * 0.65),
                new Vec3(padCentre.X * 0.88, padY - gap * 2.5, padCentre.Z * 0.88),
                padCentre with { Y = padY - gap }
            };
            var petRad = new List<double> { 0.0038, 0.0032, 0.0026, 0.0020, 0.0016 };
            Primitives.Tube(m, petPath, petRad, 5, (i, v) => (Primitives.Mix(underCol, c1, 0.4), 1, i, v, 0, 0));

            double padRadius = rng.Range(0.048, 0.074) * (isOld ? rng.Range(0.88, 1.05) : 1.0);
            var topCentreCol = isOld ? Primitives.Mix(c1, yellow, rng.Range(0.35, 0.65)) : Primitives.Scale(c1, 0.98);
            var topRimCol = isOld ? Primitives.Mix(c2, yellow, rng.Range(0.45, 0.75)) : Primitives.Mix(c1, c2, rng.Range(0.2, 0.4));
            var bottomCol = isOld ? Primitives.Mix(underCol, yellow, 0.25) : underCol;

            var tmp = new MeshData { Lod = m.Lod };
            OrbicularPad(tmp, lSeed, padRadius, thickness: 0.004, topCentreCol, topRimCol, bottomCol,
                torn: isOld, tearAmp: isOld ? rng.Range(0.06, 0.12) : 0);
            AppendRotatedY(m, tmp, padCentre, ang);
        }

        // 2. Emergent water-shield cup flowers held just above the water line (Y ~ 1.08)
        int nFlowers = 1 + rng.NextInt(2); // 1..2
        for (int f = 0; f < nFlowers; f++)
        {
            double fAng = rng.Range(0, 2 * Math.PI);
            double fReach = rng.Range(0.05, 0.12);
            double fTopY = rng.Range(1.05, 1.10);
            var fTop = new Vec3(Math.Cos(fAng) * fReach, fTopY, Math.Sin(fAng) * fReach);

            var scapePath = new List<Vec3> {
                Vec3.Zero,
                new Vec3(fTop.X * 0.35, fTopY * 0.35, fTop.Z * 0.35),
                new Vec3(fTop.X * 0.70, fTopY * 0.75, fTop.Z * 0.70),
                fTop - Vec3.Up * 0.015
            };
            var scapeRad = new List<double> { 0.0035, 0.0028, 0.0022, 0.0016 };
            Primitives.Tube(m, scapePath, scapeRad, 5, (i, v) => (Primitives.Mix(underCol, c1, 0.5), 1, i, v, 0, 0));

            // Sculpted 5-petaled emergent water poppy goblet
            double flR = 0.030;
            const int pCount = 5;
            for (int p = 0; p < pCount; p++)
            {
                double pAz = p * (Math.Tau / pCount) + fAng;
                var pRad = new Vec3(Math.Cos(pAz), 0, Math.Sin(pAz));
                var pSide = new Vec3(-Math.Sin(pAz), 0, Math.Cos(pAz));
                var pBase = fTop;
                var pMid = fTop + pRad * (flR * 0.6) + Vec3.Up * (flR * 0.45);
                var pTip = fTop + pRad * flR + Vec3.Up * (flR * 0.55);

                int v0 = m.AddVertex(pBase, Vec3.Up, yellow, 1, 0, 0, 0, 1.0);
                int v1 = m.AddVertex(pMid - pSide * (flR * 0.35), pRad, yellow, 1, 0.5, 0, 0, 1.0);
                int v2 = m.AddVertex(pMid + pSide * (flR * 0.35), pRad, yellow, 1, 0.5, 1, 0, 1.0);
                int v3 = m.AddVertex(pTip, (pRad + Vec3.Up).Normalized(), yellow, 1, 1, 0.5, 0, 1.0);

                Primitives.TriangleFacing(m, v0, v1, v2, pRad);
                Primitives.TriangleFacing(m, v1, v3, v2, pRad);
                // Dual face for petal
                Primitives.TriangleFacing(m, v0, v2, v1, -pRad);
                Primitives.TriangleFacing(m, v1, v2, v3, -pRad);
            }
            // Golden stamen boss at center
            double antherR = 0.010;
            Primitives.Ellipsoid(m, fTop + Vec3.Up * 0.01, new Vec3(antherR, antherR * 1.2, antherR), 4, 6,
                (u, v) => (new[] { 0.96, 0.84, 0.22 }, 1.0, u, v, 0, 1.0));
        }
    }

    /// <summary>
    /// Volumetric floating pad: dished orbicular disc with a V-shaped basal sinus cleft,
    /// a smooth rolled rim connecting top and bottom faces, raised radiating veins, and aerenchyma ribs.
    /// </summary>
    private static void OrbicularPad(MeshData m, ulong seed, double radius, double thickness,
        double[] topCentreCol, double[] topRimCol, double[] bottomCol, bool torn, double tearAmp)
    {
        var rng = Rng.Keyed(seed, "flora.mesh.orbicularpad", 0);
        const int seg = 28;
        double notchHalf = rng.Range(0.20, 0.30);
        double notchShift = rng.Range(-0.05, 0.05);
        double start = Math.PI + notchShift + notchHalf;
        double sweep = 2 * Math.PI - 2 * notchHalf;
        double phase = rng.Range(0, 2 * Math.PI);
        double phase2 = rng.Range(0, 2 * Math.PI);
        double ellipse = rng.Range(-0.10, 0.10);
        double broadWave = rng.Range(0.04, 0.08);
        double fineWave = rng.Range(0.015, 0.035);
        double upturn = radius * rng.Range(0.08, 0.14);
        double tearAngle = start + sweep * rng.Range(0.18, 0.83);

        Vec3 Rim(double th, double faceY)
        {
            double wave = broadWave * Math.Sin(3 * th + phase)
                + fineWave * Math.Sin(7 * th + phase2);
            double bite = torn ? tearAmp * Math.Pow(Math.Max(0, Math.Cos(th - tearAngle)), 18) : 0;
            double rr = radius * (1.0 + wave - bite);
            return new Vec3(Math.Cos(th) * rr * (1.0 + ellipse),
                faceY + upturn * (0.85 + 0.15 * Math.Sin(2 * th + phase)),
                Math.Sin(th) * rr * (1.0 - ellipse));
        }

        var topIds = new List<int>();
        var bottomIds = new List<int>();

        // Top face
        int cTop = m.AddVertex(new Vec3(0, thickness * 0.5, 0), Vec3.Up, topCentreCol, 1, 0.5, 0.5, 0, 1.0);
        for (int s = 0; s <= seg; s++)
        {
            double th = start + sweep * s / seg;
            topIds.Add(m.AddVertex(Rim(th, thickness * 0.5), Vec3.Up, topRimCol, 1, (double)s / seg, 1.0, 0, 1.0));
        }
        for (int i = 0; i < topIds.Count - 1; i++)
            Primitives.TriangleFacing(m, cTop, topIds[i], topIds[i + 1], Vec3.Up);

        // Bottom face
        int cBottom = m.AddVertex(new Vec3(0, -thickness * 0.5, 0), -Vec3.Up, bottomCol, 1, 0.5, 0.5, 1, 1.0);
        for (int s = 0; s <= seg; s++)
        {
            double th = start + sweep * s / seg;
            bottomIds.Add(m.AddVertex(Rim(th, -thickness * 0.5), -Vec3.Up, bottomCol, 1, (double)s / seg, 1.0, 1, 1.0));
        }
        for (int i = 0; i < bottomIds.Count - 1; i++)
            Primitives.TriangleFacing(m, cBottom, bottomIds[i + 1], bottomIds[i], -Vec3.Up);

        // Rolled rim connecting top and bottom faces along the perimeter
        for (int s = 0; s < seg; s++)
        {
            int t0 = topIds[s], t1 = topIds[s + 1];
            int b0 = bottomIds[s], b1 = bottomIds[s + 1];
            var rNorm = (m.Position(t0) - new Vec3(0, m.Position(t0).Y, 0)).Normalized();
            Primitives.TriangleFacing(m, t0, t1, b0, rNorm);
            Primitives.TriangleFacing(m, t1, b1, b0, rNorm);
        }

        // Notch cleft side walls
        var n0Norm = (m.Position(topIds[0]) - m.Position(cTop)).Cross(Vec3.Up).Normalized();
        Primitives.TriangleFacing(m, cTop, topIds[0], cBottom, n0Norm);
        Primitives.TriangleFacing(m, topIds[0], bottomIds[0], cBottom, n0Norm);

        var n1Norm = (m.Position(cTop) - m.Position(topIds[^1])).Cross(Vec3.Up).Normalized();
        Primitives.TriangleFacing(m, cTop, cBottom, topIds[^1], n1Norm);
        Primitives.TriangleFacing(m, topIds[^1], cBottom, bottomIds[^1], n1Norm);

        // Raised structural veins on top
        var veinCol = Primitives.Scale(Primitives.Mix(topCentreCol, topRimCol, 0.5), 1.15);
        for (int v = 0; v < 5; v++)
        {
            double th = start + sweep * (v + 0.5) / 5;
            double edgeY = thickness * 0.5;
            var edge = Rim(th, edgeY);
            var centre = new Vec3(0, edgeY + 0.001, 0);
            var middle = Vec3.Lerp(centre, edge, 0.50) + new Vec3(0, 0.001, 0);
            var end = Vec3.Lerp(centre, edge, 0.90) + new Vec3(0, 0.001, 0);
            Primitives.Tube(m, new[] { centre, middle, end },
                new[] { 0.0008, 0.0005, 0.00018 }, 3,
                (i, u) => (veinCol, 1, i, u, 0, 0));
        }
    }
    private static int LodCount(MeshData m, int authored, int min, bool radial) =>
        m.Lod is { } lod ? Math.Max(Math.Min(min, authored), (int)Math.Round(authored * (radial ? lod.Radial : lod.Length))) : authored;

    private static void PearlCushion(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var matBaseCol = Primitives.Scale(c1, 0.65);
        var matMidCol = c1;
        var matCrownCol = Primitives.Mix(c1, c2, 0.60);
        var pearlCol = new[] { 0.88, 0.94, 0.96 };
        var setaCol = new[] { 0.58, 0.30, 0.14 };
        var capsuleCol = new[] { 0.74, 0.48, 0.16 };
        var calyptraCol = new[] { 0.45, 0.22, 0.10 };

        // Establishment points grow and merge into a compound cushion; the
        // old hand-arranged five-lobe dome is available only for A/B testing.
        bool clonal = Environment.GetEnvironmentVariable("VIVARIUM_MOSS_CLONAL_SURFACE") != "0";
        var lobeCenters = new (double x, double z, double r, double h)[]
        {
            (0.0, 0.0, 0.85, 0.68),
            (0.35, 0.20, 0.55, 0.58),
            (-0.30, 0.25, 0.52, 0.55),
            (0.15, -0.38, 0.50, 0.52),
            (-0.35, -0.22, 0.48, 0.50)
        };
        if (clonal)
            MossClonalMat(m, seed, matBaseCol, matCrownCol, heightScale: .68);
        else
        {
        int rings = LodCount(m, 18, 5, radial: false);
        int segs = LodCount(m, 36, 8, radial: true);
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

        }

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
            if (clonal) h = MossClonalHeight(px, pz, seed, .68);
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
            if (clonal) h = MossClonalHeight(px, pz, seed, .68);
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

        bool clonal = Environment.GetEnvironmentVariable("VIVARIUM_MOSS_CLONAL_SURFACE") != "0";
        if (clonal)
            MossClonalMat(m, seed, matCol, c1, heightScale: .055);
        else
        {
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

        }

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

        bool clonal = Environment.GetEnvironmentVariable("VIVARIUM_MOSS_CLONAL_SURFACE") != "0";
        // 1. Replace the full smooth disk with the true clonal union when enabled.
        if (!clonal)
        {
        int rings = LodCount(m, 16, 5, radial: false);
        int segs = LodCount(m, 32, 8, radial: true);
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

                double domeH = clonal ? MossClonalHeight(px, pz, seed) :
                    0.32 * Math.Cos(f * Math.PI * 0.5);
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
        }
        else
        {
            MossClonalMat(m, seed, turfBaseCol, turfCrownCol);
            if (Environment.GetEnvironmentVariable("VIVARIUM_MOSS_CLONAL_SHOOTS") == "1")
                MossClonalLeaflets(m, seed, turfBaseCol, turfCrownCol);
        }

        // 2. Micro-turf erect shoot needle tips (giving dense velvety texture)
        int shootCount = 150 + rng.NextInt(35);
        for (int i = 0; i < shootCount; i++)
        {
            double ang = rng.Range(0, 2 * Math.PI);
            double dist = Math.Sqrt(rng.NextDouble()) * 0.88;
            double px = Math.Cos(ang) * dist;
            double pz = Math.Sin(ang) * dist;
            double coverage = clonal ? MossClonalCoverage(px,pz,seed) : 1.0;
            if (clonal && coverage < .07) continue;
            double domeH = clonal ? MossClonalHeight(px, pz, seed) :
                0.32 * Math.Cos(dist / 0.94 * Math.PI * 0.5);
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
            double coverage = clonal ? MossClonalCoverage(px,pz,seed) : 1.0;
            if (clonal && coverage < .17) continue;
            double domeH = clonal ? MossClonalHeight(px, pz, seed) :
                0.32 * Math.Cos(dist / 0.94 * Math.PI * 0.5);
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
    /// <summary>
    /// Starmoss (Syntrichia ruralis / star moss & Schistostega pennata luminous moss habit):
    /// - Dense emerald moss hummock adhering to moist soil, scree, and gravel.
    /// - Crowded stellate rosettes (tiny 5-6 ray star gametophore shoots) with waxy leaflets.
    /// - Central spherical protonemal lens cells with warm emerald-gold bioluminescence (UV2.x = 1.1, UV2.y = 1.0).
    /// - Graceful emergent wiry setae carrying nodding glossy amber spore capsules with pointed calyptra hoods.
    /// </summary>
    private static void Starmoss(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var hummockCol = Primitives.Scale(c1, 0.65);
        var starBaseCol = c1;
        var starTipCol = Primitives.Mix(c1, c2, 0.70);
        var bioLensCol = new[] { 0.64, 1.0, 0.38 };
        var setaCol = new[] { 0.52, 0.28, 0.14 };
        var capsuleCol = new[] { 0.72, 0.44, 0.16 };

        // 1. Spreading velvety moss hummock mound
        const int hummockRings = 5;
        const int hummockSides = 20;
        int hStart = m.VertexCount;
        for (int r = 0; r <= hummockRings; r++)
        {
            double rf = r / (double)hummockRings;
            double baseRad = 0.55 * rf;
            double h = 0.045 * Math.Pow(1.0 - rf, 1.6) + 0.004;

            for (int s = 0; s <= hummockSides; s++)
            {
                double th = s * Math.Tau / hummockSides;
                double edgeWobble = 1.0 + 0.14 * Math.Sin(4.0 * th) + 0.08 * Math.Cos(7.0 * th);
                double rad = baseRad * edgeWobble;
                var p = new Vec3(Math.Cos(th) * rad, h, Math.Sin(th) * rad);
                var norm = (new Vec3(Math.Cos(th) * 0.25, 0.95, Math.Sin(th) * 0.25)).Normalized();
                var col = Primitives.Mix(hummockCol, starBaseCol, rf * 0.6);
                m.AddVertex(p, norm, col, 1.0, rf, s / (double)hummockSides, 0, 1.0);
            }
        }
        for (int r = 0; r < hummockRings; r++)
        {
            for (int s = 0; s < hummockSides; s++)
            {
                int aIdx = hStart + r * (hummockSides + 1) + s;
                int bIdx = aIdx + hummockSides + 1;
                Primitives.TriangleFacing(m, aIdx, bIdx, aIdx + 1, Vec3.Up);
                Primitives.TriangleFacing(m, aIdx + 1, bIdx, bIdx + 1, Vec3.Up);
            }
        }

        // 2. Crowded Stellate Gametophore Star Shoots
        int starCount = 45 + rng.NextInt(20);
        for (int st = 0; st < starCount; st++)
        {
            double a = rng.Range(0, Math.Tau);
            double dist = Math.Sqrt(rng.NextDouble()) * 0.48;
            double moundH = 0.045 * Math.Pow(Math.Max(0.0, 1.0 - dist / 0.55), 1.6) + 0.005;
            var center = new Vec3(Math.Cos(a) * dist, moundH, Math.Sin(a) * dist);

            double starR = rng.Range(0.016, 0.032);
            int rays = 5 + rng.NextInt(2);

            // Center bioluminescent protonemal lens bead
            double lensR = starR * 0.28;
            Primitives.Ellipsoid(m, center + Vec3.Up * (lensR * 0.6), new Vec3(lensR, lensR * 0.8, lensR), 3, 5,
                (u, v) => (bioLensCol, 1.0, u, v, 1.1, 1.0));

            // Stellate ray leaflets
            int cV = m.AddVertex(center + Vec3.Up * 0.002, Vec3.Up, starBaseCol, 1.0, 0, 0, 0.4, 1.0);
            for (int ray = 0; ray < rays; ray++)
            {
                double rayAng = ray * (Math.Tau / rays) + rng.Range(-0.15, 0.15);
                var rayDir = new Vec3(Math.Cos(rayAng), 0.15, Math.Sin(rayAng)).Normalized();
                var raySide = new Vec3(-rayDir.Z, 0, rayDir.X);
                double rayLen = starR * rng.Range(0.85, 1.15);
                double rayWid = starR * 0.32;

                var tip = center + rayDir * rayLen;
                var pL = center + rayDir * (rayLen * 0.45) - raySide * rayWid;
                var pR = center + rayDir * (rayLen * 0.45) + raySide * rayWid;

                int iTip = m.AddVertex(tip, (rayDir + Vec3.Up * 0.5).Normalized(), starTipCol, 1.0, 1, 0.5, 0.2, 1.0);
                int iL = m.AddVertex(pL, Vec3.Up, starBaseCol, 1.0, 0.5, 0, 0.1, 1.0);
                int iR = m.AddVertex(pR, Vec3.Up, starBaseCol, 1.0, 0.5, 1, 0.1, 1.0);

                Primitives.TriangleFacing(m, cV, iL, iTip, Vec3.Up);
                Primitives.TriangleFacing(m, cV, iTip, iR, Vec3.Up);
                Primitives.TriangleFacing(m, cV, iTip, iL, -Vec3.Up);
                Primitives.TriangleFacing(m, cV, iR, iTip, -Vec3.Up);
            }
        }

        // 3. Emergent wiry setae & spore capsules
        int setaeCount = 12 + rng.NextInt(8);
        for (int sc = 0; sc < setaeCount; sc++)
        {
            double a = rng.Range(0, Math.Tau);
            double dist = Math.Sqrt(rng.NextDouble()) * 0.40;
            double moundH = 0.045 * Math.Pow(Math.Max(0.0, 1.0 - dist / 0.55), 1.6) + 0.005;
            var sBase = new Vec3(Math.Cos(a) * dist, moundH, Math.Sin(a) * dist);

            double setaH = rng.Range(0.045, 0.085);
            var arch = new Vec3(rng.Range(-0.02, 0.02), 0, rng.Range(-0.02, 0.02));
            var sMid = sBase + arch * 0.6 + Vec3.Up * (setaH * 0.55);
            var sTip = sBase + arch + Vec3.Up * setaH;

            Primitives.Tube(m, new[] { sBase, sMid, sTip }, new[] { 0.0022, 0.0016, 0.0010 }, 3,
                (st, u) => (setaCol, 1.0, st, u, 0, 0));

            var capDir = (sTip - sMid).Normalized();
            var nodDir = (capDir * 0.6 - Vec3.Up * 0.8).Normalized();
            var capBase = sTip;
            var capTip = sTip + nodDir * 0.012;

            double capR = 0.0045;
            Primitives.Ellipsoid(m, Vec3.Lerp(capBase, capTip, 0.5), new Vec3(capR, capR * 1.6, capR), 3, 5,
                (u, v) => (capsuleCol, 1.0, u, v, 0, 0));
        }
    }

    private static void CarpetDefault(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var matCol = Primitives.Scale(c1, 0.7);
        if (Environment.GetEnvironmentVariable("VIVARIUM_MOSS_CLONAL_SURFACE") != "0")
            MossClonalMat(m, seed, matCol, c1, heightScale: .10);
        else
        {
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
        }
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

        bool clonal = Environment.GetEnvironmentVariable("VIVARIUM_MOSS_CLONAL_SURFACE") != "0";
        if (clonal)
            MossClonalMat(m, seed, hummockCol, branchSpreadCol, heightScale: .115);
        else
        {
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

        }

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
    /// Antlerlace lichen (Cladonia furcata / Cladonia rangiferina / fruticose coral-antler lichen):
    /// - Densely imbricated basal primary squamules forming an intricate, curled micro-foliose crust on mineral substrate.
    /// - Elegant upright hollow podetia rising in a domed cushion habit, branching repeatedly into smooth dichotomous antler tines.
    /// - Flattened and grooved axils with granulose pruinose surface texture.
    /// - Tips crowned with rounded rufous/chestnut apothecial button discs with distinct sterile margins.
    /// </summary>
    private static void Antlerlace(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2)
    {
        var squamuleTopCol = Primitives.Scale(c1, 0.90);
        var squamuleUnderCol = Primitives.Mix(c1, new[] { 0.88, 0.90, 0.82 }, 0.55);
        var podetiaCol = Primitives.Mix(c1, c2, 0.35);
        var tineTipCol = Primitives.Mix(c1, c2, 0.75);
        var buttonCol = new[] { 0.36, 0.16, 0.10 };
        var buttonRimCol = Primitives.Mix(tineTipCol, buttonCol, 0.35);

        // 1. Basal primary squamules: layered, curled micro-foliose thallus scales
        int squamuleCount = 36 + rng.NextInt(12);
        for (int i = 0; i < squamuleCount; i++)
        {
            double a = rng.Range(0, 2 * Math.PI);
            double dist = Math.Pow(rng.NextDouble(), 0.6) * 0.65;
            var center = new Vec3(Math.Cos(a) * dist, 0.008 + rng.Range(0, 0.006), Math.Sin(a) * dist);
            var fwd = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var right = new Vec3(-fwd.Z, 0, fwd.X);
            double scale = rng.Range(0.035, 0.065);

            // 6-point crenulate arched fan with curled margin
            int fanStart = m.VertexCount;
            m.AddVertex(center, Vec3.Up, squamuleUnderCol, 1.0, 0, 0.5, 0, 0);

            const int lobes = 6;
            for (int l = 0; l <= lobes; l++)
            {
                double u = (double)l / lobes * Math.PI;
                double lobeR = scale * Math.Sin(u) * (1.0 + 0.25 * Math.Cos(l * 3.0));
                double lift = 0.003 + 0.008 * Math.Sin(u);
                var pos = center + fwd * (Math.Cos(u) * scale * 0.8) + right * lobeR + new Vec3(0, lift, 0);
                var norm = (Vec3.Up + (pos - center) * 0.5).Normalized();
                var col = Primitives.Mix(squamuleTopCol, squamuleUnderCol, (double)l / lobes * 0.3);
                m.AddVertex(pos, norm, col, 1.0, 1.0, (double)l / lobes, 0, 0);
            }

            for (int l = 0; l < lobes; l++)
            {
                Primitives.TriangleFacing(m, fanStart, fanStart + 1 + l, fanStart + 2 + l, Vec3.Up);
            }
        }

        // 2. Upright antler podetia tufts rising in a natural domed cushion
        int tuftCount = 18 + rng.NextInt(6);
        for (int t = 0; t < tuftCount; t++)
        {
            double a = rng.Range(0, 2 * Math.PI);
            double distNorm = Math.Sqrt(rng.NextDouble());
            double dist = distNorm * 0.52; // Strictly confined within r <= 0.52 at base
            var root = new Vec3(Math.Cos(a) * dist, 0.012, Math.Sin(a) * dist);

            // Dome envelope: tall at center (up to 0.90), compact toward margin (down to 0.45)
            double domeProfile = Math.Max(0.35, 1.0 - Math.Pow(dist / 0.55, 1.8));
            double totalH = rng.Range(0.60, 0.88) * domeProfile;

            // Inward lean for outer tufts to form a cohesive clump
            double leanIn = distNorm * 0.18;
            var tiltDir = new Vec3(-Math.Cos(a) * leanIn + rng.Range(-0.04, 0.04), 1.0, -Math.Sin(a) * leanIn + rng.Range(-0.04, 0.04)).Normalized();

            var p0 = root;
            var p1 = root + tiltDir * (totalH * 0.40);
            Primitives.Tube(m, new[] { p0, p1 }, new[] { 0.022, 0.016 }, 6,
                (step, u) => (podetiaCol, 1.0, step * 0.3, u, 0, 0));

            // Dichotomous primary fork
            double forkAng = rng.Range(0, 2 * Math.PI);
            for (int f = -1; f <= 1; f += 2)
            {
                var sideFork = new Vec3(Math.Cos(forkAng + f * 0.55), 0, Math.Sin(forkAng + f * 0.55));
                var dir1 = (tiltDir * 0.85 + sideFork * 0.30).Normalized();
                var p2 = p1 + dir1 * (totalH * 0.30);
                Primitives.Tube(m, new[] { p1, p2 }, new[] { 0.015, 0.010 }, 5,
                    (step, u) => (Primitives.Mix(podetiaCol, tineTipCol, 0.35), 1.0, 0.3 + step * 0.3, u, 0, 0));

                // Secondary antler tines
                int tineCount = 2 + (rng.NextDouble() < 0.40 ? 1 : 0);
                for (int tc = 0; tc < tineCount; tc++)
                {
                    double tineSpread = (tc - (tineCount - 1) * 0.5) * 0.42;
                    var sideDir = dir1.Cross(Vec3.Up).Normalized();
                    if (sideDir.LengthSq < 1e-4) sideDir = dir1.Cross(Vec3.Right).Normalized();
                    var tineDir = (dir1 * 0.80 + sideDir * tineSpread + new Vec3(0, rng.Range(0.12, 0.28), 0)).Normalized();
                    double tineLen = totalH * rng.Range(0.20, 0.30);
                    var pTine = p2 + tineDir * tineLen;

                    // Smooth cylindrical antler prong
                    Primitives.Tube(m, new[] { p2, pTine }, new[] { 0.009, 0.0055 }, 4,
                        (step, u) => (Primitives.Mix(podetiaCol, tineTipCol, 0.65 + step * 0.35), 1.0, 0.6 + step * 0.4, u, 0, 0));

                    // Apothecial button disc with rim
                    var btnCenter = pTine + tineDir * 0.006;
                    Primitives.Ellipsoid(m, btnCenter, new Vec3(0.010, 0.005, 0.010), 3, 6,
                        (u, v) => (buttonCol, 1.0, u, v, 0, 0));
                    Primitives.Ellipsoid(m, btnCenter - tineDir * 0.002, new Vec3(0.011, 0.003, 0.011), 3, 6,
                        (u, v) => (buttonRimCol, 1.0, u, v, 0, 0));
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


    /// <summary>Arching pinnate fronds with natural fountain arch, paired pinnae, and unrolling fiddleheads.</summary>
    private static void Fern(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile, bool moonfern = false)
    {
        // Moonfern's compact, refined Boston-fern-like fountain is an anatomical
        // variant, not a global change to the other fern species.
        int crownCount = moonfern ? (juvenile ? 7 + rng.NextInt(3) : 17 + rng.NextInt(3))
            : (juvenile ? 4 + rng.NextInt(2) : 8 + rng.NextInt(3));
        int pairs = moonfern ? (juvenile ? 14 + rng.NextInt(4) : 26 + rng.NextInt(4))
            : (juvenile ? 8 + rng.NextInt(3) : 12 + rng.NextInt(3));
        double frondLen = moonfern ? (juvenile ? rng.Range(0.44, 0.56) : rng.Range(0.93, 1.11))
            : (juvenile ? rng.Range(0.50, 0.65) : rng.Range(1.05, 1.30));
        double arch = moonfern ? rng.Range(0.88, 1.07) : rng.Range(1.05, 1.25);
        double taper = moonfern ? rng.Range(0.88, 1.05) : rng.Range(0.70, 0.85);
        double pinnaLen = moonfern ? rng.Range(0.124, 0.153) : rng.Range(0.20, 0.25);
        double pinnaWid = moonfern ? rng.Range(0.019, 0.027) : rng.Range(0.038, 0.046);
        double camber = moonfern ? rng.Range(0.07, 0.12) : rng.Range(0.10, 0.16);
        double tilt = moonfern ? rng.Range(0.10, 0.19) : rng.Range(0.18, 0.28);
        double subOpposite = moonfern ? rng.Range(0.15, 0.31) : rng.Range(0.25, 0.45);
        double fiddlehead = juvenile ? rng.Range(0.70, 0.90) : 0.0;

        var p = new PinnateFrondParams(
            CrownCount: crownCount,
            PinnaePairs: pairs,
            FrondLength: frondLen,
            RachisRadius: moonfern ? (juvenile ? 0.0052 : 0.0071) : (juvenile ? 0.008 : 0.011),
            ArchCurve: arch,
            Taper: taper,
            PinnaLength: pinnaLen,
            PinnaWidth: pinnaWid,
            Camber: camber,
            TiltAngle: tilt,
            SubOpposite: subOpposite,
            FiddleheadProgress: fiddlehead,
            DetailLevel: m.FloraDetailLevel ?? 0,
            StipeFlex: 0.85,
            Compound: true,
            PetioluleLength: moonfern ? 0.0028 : 0.005,
            FineFountain: moonfern
        );

        var topGreen = Primitives.Mix(c1, new[] { 0.20, 0.46, 0.24 }, 0.55);
        var underJade = Primitives.Mix(c2, new[] { 0.32, 0.60, 0.38 }, 0.55);
        PinnateFrond.Build(m, p, seed, topGreen, underJade);
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

            // Alternate 3-lobed ivy leaf pair, carried on the upper side of the stem and angled up and away
            foreach (double sign in new[] { -1.0, 1.0 })
            {
                var lDir = new Vec3(sign * 0.42, 0.20, 0.88).Normalized();
                var petEnd = lDir * 0.024;
                Primitives.Tube(m, new[] { Vec3.Zero, petEnd }, new[] { 0.0028, 0.0020 }, 6,
                    (i, v) => (petioleCol, 1.0, i, v, 0, 0));
                IvyLeaf(petEnd, lDir, new Vec3(0, 0, 1), 1.05);
            }

            // A small switch of side growth where the shoot leaves the node. The stem itself is continuous now
            // and the side shoots that really hang are part of the grown stem, so this stays close to the bark
            // rather than trailing off into the air.
            var p0 = new Vec3(0, 0, 0.005);
            var p1 = p0 + new Vec3(rng.Range(-0.006, 0.006), -0.013, 0.004);
            var p2 = p1 + new Vec3(rng.Range(-0.007, 0.007), -0.014, 0.003);

            Primitives.Tube(m, new[] { p0, p1, p2 }, new[] { 0.0016, 0.0011, 0.0006 }, 5,
                (i, v) => (stemCol, 1.0, i / 2.0, v, 0, 0));

            // Small distally-spaced leaves and holdfasts along the trailing shoot
            Primitives.Tube(m, new[] { p1, p1 - new Vec3(0, 0.004, 0.008) }, new[] { 0.0013, 0.0007 }, 4,
                (i, v) => (rootletCol, 1.0, i, v, 0, 0));
            var pet1 = p1 + new Vec3(0.010, -0.006, 0.004);
            Primitives.Tube(m, new[] { p1, pet1 }, new[] { 0.0015, 0.0010 }, 4, (i, v) => (petioleCol, 1.0, i, v, 0, 0));
            IvyLeaf(pet1, new Vec3(0.35, -0.60, 0.72).Normalized(), new Vec3(0, 0, 1), 0.52);

            var pet2 = p2 + new Vec3(-0.008, -0.007, 0.003);
            Primitives.Tube(m, new[] { p2, pet2 }, new[] { 0.0014, 0.0009 }, 4, (i, v) => (petioleCol, 1.0, i, v, 0, 0));
            IvyLeaf(pet2, new Vec3(-0.30, -0.62, 0.72).Normalized(), new Vec3(0, 0, 1), 0.40);
        }
        else
        {
            // Ground runner node: leaf reaching upward into sunlight. The node's local +Z is the upper side of
            // the stem, so the leaf reaches up off the top of the runner.
            var lDir = (new Vec3(rng.Range(-0.35, 0.35), rng.Range(-0.15, 0.15), 0.88)).Normalized();
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

            // The leaves are carried on the upper side of the stem: the node's local +Z is up, +Y is along the
            // stem and +X is across it, so the pair splays sideways while leaning up and forward.
            foreach (double sign in new[] { -1.0, 1.0 })
            {
                var lDir = (side * (sign * 0.42) + norm * 0.84 + fwd * 0.30).Normalized();
                var lSide = lDir.Cross(norm).Normalized();
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
        Primitives.Ellipsoid(m, Vec3.Zero, new Vec3(0.0075, 0.0075, 0.0075), 4, 6,
            (u, v) => (collarCol, 1.0, u, v, 0, 0));

        // Opposite pair of lanceolate leaves, carried on the upper side of the stem in both forms.
        PairedLeaves(Vec3.Zero, new Vec3(0, 1, 0), new Vec3(0, 0, 1), attached ? 1.05 : 0.85);

        if (attached)
        {
            // A small switch of side growth where the shoot leaves the node. The stem itself is continuous now
            // and the side shoots that really hang are part of the grown stem, so this stays close to the bark
            // rather than trailing off into the air.
            var p0 = new Vec3(0, 0, 0.005);
            var p1 = p0 + new Vec3(0.010 * Math.Sin(1.6), -0.013, 0.005);
            var p2 = p1 + new Vec3(0.012 * Math.Sin(3.8), -0.014, 0.003);

            Primitives.Tube(m, new[] { p0, p1, p2 }, new[] { 0.0016, 0.0011, 0.0006 }, 5,
                (i, v) => (stemCol, 1.0, i / 2.0, v, 0, 0));

            // Small distally-spaced opposite leaf pairs along the trailing shoot
            PairedLeaves(p1, (p2 - p0).Normalized(), new Vec3(0, 0, 1), 0.46);
        }
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

        // Starry whorl of 6 oblanceolate leaves
        int leaves = 6;
        var bladeCol = Primitives.Mix(c1, c2, 0.40);
        double bladeLen = (attached ? 1.0 : 0.80) * rng.Range(0.085, 0.115);
        double halfW = bladeLen * 0.20;

        for (int w = 0; w < leaves; w++)
        {
            double a = w * (Math.PI * 2 / leaves) + rng.Range(-0.05, 0.05);
            // The whorl is carried on the upper side of the stem: the leaves fan about the stem's axis but lean
            // up, so the mass of the foliage sits on top of the stem rather than all around it.
            var lDir = new Vec3(Math.Cos(a) * 0.56, -0.10, Math.Sin(a) * 0.56 + 0.82).Normalized();
            var lSide = lDir.Cross(Vec3.Up).Normalized();

            int bladeStart = m.VertexCount;
            FoliageBlade.Build(m, Vec3.Zero, lDir * bladeLen, lSide, halfW,
                Primitives.Scale(bladeCol, 0.78), bladeCol,
                camber: 0.12, curl: 0.14, shoulder: 0.82, segments: 3);

            for (int v = bladeStart; v < m.VertexCount; v++)
                if (m.UV2[v * 2] > 0.5f) m.SetColor(v, leafUnderCol[0], leafUnderCol[1], leafUnderCol[2], 1.0);
        }

        if (attached)
        {
            // A small switch of side growth where the shoot leaves the node. The stem itself is continuous now
            // and the side shoots that really hang are part of the grown stem, so this stays close to the bark
            // rather than trailing off into the air.
            var p0 = new Vec3(0, 0, 0.005);
            var p1 = p0 + new Vec3(rng.Range(-0.006, 0.006), -0.014, 0.004);
            var p2 = p1 + new Vec3(rng.Range(-0.007, 0.007), -0.015, 0.003);

            Primitives.Tube(m, new[] { p0, p1, p2 }, new[] { 0.0016, 0.0011, 0.0006 }, 5,
                (i, v) => (stemCol, 1.0, i / 2.0, v, 0.0, 0.0));
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

                    // Trailing pendulous streamers draping downward under gravity off host bark
                    if (order == 0 && (node == 3 || node == 5))
                    {
                        var hangStart = p1;
                        double hangDrop = rng.Range(0.18, 0.28);
                        var hDir = (norm * 0.50 + side * (node == 3 ? 0.35 : -0.35)).Normalized();
                        var hp0 = hangStart;
                        var hp1 = hangStart + hDir * 0.025 - Vec3.Up * (hangDrop * 0.33);
                        var hp2 = hp1 + hDir * 0.018 - Vec3.Up * (hangDrop * 0.38) + side * rng.Range(-0.012, 0.012);
                        var hp3 = hp2 + hDir * 0.010 - Vec3.Up * (hangDrop * 0.29) + side * rng.Range(-0.008, 0.008);

                        Wood(m, new[] { hp0, hp1, hp2, hp3 }, new[] { curR * 0.70, curR * 0.55, curR * 0.42, curR * 0.30 }, 5);

                        // Holdfast rootlets on hanging shoot
                        Primitives.Tube(m, new[] { hp1, hp1 - norm * 0.010 }, new[] { 0.0016, 0.0008 }, 4,
                            (ri, rv) => (rootletCol, 1.0, ri, rv, 0, 0));

                        // Small distally-spaced ivy leaves along downward-trailing streamer
                        var hlDir1 = (side * 0.65 - Vec3.Up * 0.75).Normalized();
                        IvyLeaf(hp1 + hlDir1 * 0.014, hlDir1, norm, 0.68);

                        var hlDir2 = (-side * 0.60 - Vec3.Up * 0.80).Normalized();
                        IvyLeaf(hp2 + hlDir2 * 0.012, hlDir2, norm, 0.54);

                        var hlDir3 = (norm * 0.20 - Vec3.Up * 0.95).Normalized();
                        IvyLeaf(hp3 + hlDir3 * 0.010, hlDir3, norm, 0.42);
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

                    // Trailing pendulous twining cord draping downward under gravity off host
                    if (i == 4 || i == 6)
                    {
                        var hangStart = p1;
                        double hangDrop = rng.Range(0.20, 0.28);
                        var hNorm = n1;
                        var hSide = (p1 - p0).Cross(n1).Normalized();
                        var sp0 = hangStart;
                        var sp1 = hangStart + hNorm * 0.026 - Vec3.Up * (hangDrop * 0.35) + hSide * 0.016;
                        var sp2 = sp1 + hNorm * 0.020 - Vec3.Up * (hangDrop * 0.38) - hSide * 0.014;
                        var sp3 = sp2 + hNorm * 0.012 - Vec3.Up * (hangDrop * 0.27) + hSide * 0.010;

                        var hPts = new[] { sp0, sp1, sp2, sp3 };
                        var hRads = new[] { arcRadii[3] * 0.65, arcRadii[3] * 0.50, arcRadii[3] * 0.38, arcRadii[3] * 0.25 };
                        Wood(m, hPts, hRads, 5);

                        // Small distally-spaced opposite leaf pairs along hanging twiner
                        PairedLeaves(sp1, (sp2 - sp0).Normalized(), hNorm, 0.68, (double)i / nodes);
                        PairedLeaves(sp2, (sp3 - sp1).Normalized(), hNorm, 0.52, (double)(i + 1) / nodes);

                        // Terminal holdfast tendril curl at tip of hanging cord
                        var curlEnd = sp3 + (hNorm * 0.28 - Vec3.Up * 0.72).Normalized() * 0.016;
                        Primitives.Tube(m, new[] { sp3, curlEnd }, new[] { 0.0012, 0.0006 }, 4,
                            (cu, cv) => (collarCol, 1.0, cu, cv, 0, 0));
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
    private static void AddLamellarGills(MeshData m, Func<double,double,Vec3> underside,
        double capRadius, double[] color, int count)
    {
        for(int g=0;g<count;g++)
        {
            double angle=g*Math.Tau/count;
            double inner=g%2==0?.12:.53;
            var side=new Vec3(-Math.Sin(angle),0,Math.Cos(angle));
            int start=m.VertexCount;
            int stations=m.Lod?.GillStations??9;
            for(int row=0;row<=stations;row++)
            {
                double u=(double)row/stations, fraction=inner+(1-inner)*u;
                var top=underside(fraction,angle);
                double depth=capRadius*(.055+.13*Math.Sin(Math.PI*u));
                var lower=top-Vec3.Up*depth;
                for(int face=0;face<2;face++)
                {
                    var n=side*(face==0?1:-1);
                    var offset=n*(capRadius*.0025);
                    m.AddVertex(top+offset,n,color,1,g/(double)count,fraction,1,0);
                    m.AddVertex(lower+offset,n,Primitives.Scale(color,.82),1,g/(double)count,fraction,1,0);
                }
            }
            for(int row=0;row<stations;row++)
            {
                for(int face=0;face<2;face++)
                {
                    int q=start+row*4+face*2,next=q+4;
                    var n=side*(face==0?1:-1);
                    Primitives.TriangleFacing(m,q,next,q+1,n);
                    Primitives.TriangleFacing(m,q+1,next,next+1,n);
                }
                int bottom=start+row*4+1;
                Primitives.TriangleFacing(m,bottom,bottom+4,bottom+2,-Vec3.Up);
                Primitives.TriangleFacing(m,bottom+2,bottom+4,bottom+6,-Vec3.Up);
            }
        }
    }

    private static void Mushrooms(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile, bool wholeBonnets=false)
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
        // Luminous bonnets keep complete cap/gill/stipe groups. Reduce the troop and radial
        // detail together; loose-part culling otherwise leaves glowing stalks with no caps.
        if(wholeBonnets)n=Math.Max(3,(int)Math.Round(n*(m.Lod?.LeafFraction??1)));
        for (int k = 0; k < n; k++)
        {
            int bonnetStart=m.VertexCount;
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

            var stipePath=new Vec3[13];var stipeRadii=new double[13];
            for(int row=0;row<=12;row++)
            {
                double t=row/12.0;
                var pStipe=t<.18?Vec3.Lerp(b,lower,t/.18):t<.52?Vec3.Lerp(lower,mid,(t-.18)/.34):Vec3.Lerp(mid,top,(t-.52)/.48);
                double curl=Math.Sin(Math.PI*t)*baseRad*.14;
                stipePath[row]=pStipe+new Vec3(Math.Cos(t*Math.Tau*1.7)*curl,0,Math.Sin(t*Math.Tau*1.7)*curl);
                stipeRadii[row]=MathD.Lerp(baseRad,topRad,t)*(1+.025*Math.Sin(t*13+k));
            }
            Primitives.Tube(m,stipePath,stipeRadii,16,
                (i,v)=>(Primitives.Mix(stemBaseCol,stemCol,i/12.0),1,i/12.0,v,0,0));

            // Cap geometry with conical umbo and radial sulcate fluting
            int start = m.VertexCount;
            int around=wholeBonnets?Math.Max(8,(int)Math.Round(32*(m.Lod?.Radial??1))):32;
            int rings=wholeBonnets?Math.Max(3,(int)Math.Round(16*(m.Lod?.Radial??1))):16;
            double phase = rng.Range(0, 2 * Math.PI);
            double asym = rng.Range(0.04, 0.09);

            Vec3 CapPoint(double f, double s)
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
                    var slope=(CapPoint(Math.Min(1,f+.002),s)-CapPoint(Math.Max(.001,f-.002),s));
                    var circum=CapPoint(f,s+.01)-CapPoint(f,s-.01);
                    var normal=circum.Cross(slope).Normalized();
                    if(normal.Y<0)normal=-normal;
                    m.AddVertex(pos, normal, col, 1.0, (double)s / around, t, 0, 0);
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
            const int underRings=16;
            for (int row = 0; row <= underRings; row++)
            {
                double f = .12 + .88 * row / underRings;
                for (int s = 0; s <= around; s++)
                {
                    var gPos = CapPoint(f, s) + new Vec3(0, -capR * 0.030, 0);
                    var gillCol = Primitives.Mix(c1, c2, row == 0 ? 0.55 : (s % 2 == 0 ? 0.15 : 0.40));
                    m.AddVertex(gPos, -Vec3.Up, gillCol, 1.0, (double)s / around, row, 1.0, 0.0);
                }
            }

            for(int row=0;row<underRings;row++)
            for (int s = 0; s < around; s++)
            {
                int a = under + row*(around+1) + s;
                int c = a + around + 1;
                Primitives.TriangleFacing(m, a, c, a + 1, -Vec3.Up);
                Primitives.TriangleFacing(m, a + 1, c, c + 1, -Vec3.Up);
            }
            // True alternating primary gills and half-length lamellulae follow the pileus.
            AddLamellarGills(m,(fraction,angle)=>CapPoint(fraction,angle*around/Math.Tau)-Vec3.Up*(capR*.035),
                capR,Primitives.Mix(c1,c2,.48),36);
            // Close the thin striated margin between upper and lower cap shells.
            for(int j=0;j<around;j++)
            {
                int outer=start+rings*(around+1)+j, lowerEdge=under+underRings*(around+1)+j;
                var outDir=new Vec3(Math.Cos(j*Math.Tau/around),0,Math.Sin(j*Math.Tau/around));
                Primitives.TriangleFacing(m,outer,lowerEdge,outer+1,outDir);
                Primitives.TriangleFacing(m,outer+1,lowerEdge,lowerEdge+1,outDir);
            }
            // Membranous annulus, anchored around the stipe beneath the pileus.
            int skirt=m.VertexCount;
            for(int row=0;row<3;row++)for(int j=0;j<=32;j++)
            {
                double angle=j*Math.Tau/32;
                double rr=topRad*(1+row*1.8);
                var pAnn=Vec3.Lerp(mid,top,.72)+new Vec3(Math.Cos(angle)*rr,
                    -row*capR*.045+Math.Sin(angle*9+phase)*capR*.015*row,Math.Sin(angle)*rr);
                m.AddVertex(pAnn,Vec3.Up,stemCol,1,j/32.0,row/2.0,0,0);
            }
            for(int row=0;row<2;row++)for(int j=0;j<32;j++)
            {
                int q=skirt+row*33+j,nq=q+33;
                Primitives.TriangleFacing(m,q,nq,q+1,Vec3.Up);
                Primitives.TriangleFacing(m,q+1,nq,nq+1,Vec3.Up);
            }
            // Fine basal hyphal cords retain an irregular, open felt silhouette.
            for(int hypha=0;hypha<5;hypha++)
            {
                double angle=hypha*2.399+phase;
                var tip=b+new Vec3(Math.Cos(angle)*baseRad*2.4,.002,Math.Sin(angle)*baseRad*2.4);
                Primitives.Tube(m,new[]{b,Vec3.Lerp(b,tip,.52)+Vec3.Up*.007,tip},
                    new[]{.002*scale,.0012*scale,.0004*scale},5,
                    (i,v)=>(myceliumCol,1,i/2.0,v,0,0));
            }
            if(wholeBonnets)m.EssentialSpans.Add((bonnetStart,m.VertexCount-bonnetStart));
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
        const int rings = 24, around = 48;

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

                    double growthRidge=.006*Math.Sin(u*24*Math.Tau+phaseRuffle)*(1-u*.6);
                    double yTop = y + cupDroop + ruffle + growthRidge;
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

                    double growthRidge=.006*Math.Sin(u*24*Math.Tau+phaseRuffle)*(1-u*.6);
                    double yTop = y + cupDroop + ruffle + growthRidge;
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
            for(int j=0;j<around;j++)
            {
                int top=topStart+rings*(around+1)+j,bot=underStart+rings*(around+1)+j;
                var outDir=new Vec3(1,0,0);
                Primitives.TriangleFacing(m,top,bot,top+1,outDir);
                Primitives.TriangleFacing(m,top+1,bot,bot+1,outDir);
            }
            // Raised tube mouths and recessed dark pores on the polypore hymenium.
            for(int pore=0;pore<144;pore++)
            {
                int ri=2+pore%21,sj=2+(pore*17)%44;
                var surface=m.Position(underStart+ri*(around+1)+sj);
                int first=m.VertexCount;
                for(int ring=0;ring<2;ring++)for(int j=0;j<=6;j++)
                {
                    double angle=j*Math.Tau/6,rr=ring==0?.0035:.0015;
                    m.AddVertex(surface+new Vec3(Math.Cos(angle)*rr,ring==0?-.0015:.001,Math.Sin(angle)*rr),
                        -Vec3.Up,Primitives.Scale(poreBase,ring==0?1:.44),1,j/6.0,ring,1,0);
                }
                for(int j=0;j<6;j++)
                {
                    Primitives.TriangleFacing(m,first+j,first+j+7,first+j+1,-Vec3.Up);
                    Primitives.TriangleFacing(m,first+j+1,first+j+7,first+j+8,-Vec3.Up);
                }
            }
        }
        // Clockwise Godot faces require the negative geometric cross for visible normals.
        var totals=new Vec3[m.VertexCount];
        for(int t=0;t<m.Indices.Count;t+=3)
        {
            int a=m.Indices[t],b=m.Indices[t+1],c=m.Indices[t+2];
            var n=(m.Position(c)-m.Position(a)).Cross(m.Position(b)-m.Position(a));
            totals[a]+=n;totals[b]+=n;totals[c]+=n;
        }
        for(int i=0;i<m.VertexCount;i++)if(totals[i].LengthSq>1e-20)
        {
            var n=totals[i].Normalized();m.Normals[i*3]=(float)n.X;m.Normals[i*3+1]=(float)n.Y;m.Normals[i*3+2]=(float)n.Z;
        }
    }

    /// <summary>
    /// A xerophytic succulent rosette colony (Sempervivum tectorum / Houseleek & Echeveria):
    /// - Dominant mother rosette encircled by radiating daughter offsets connected via short fleshy stolons.
    /// - Fibonacci phyllotaxis (137.5Ãƒâ€šÃ‚Â° golden angle) of thick, faceted succulent leaves with concave adaxial face,
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
            double stalkH = rng.Range(1.45, 2.00); // reaches ~11-14 cm in world space
            double leanX = rng.Range(-0.18, 0.18);
            double leanZ = rng.Range(-0.18, 0.18);
            var stalkMid = stalkBase + new Vec3(leanX * 0.4, stalkH * 0.52, leanZ * 0.4);
            var stalkTop = stalkBase + new Vec3(leanX, stalkH, leanZ);

            // Muted wine/red-brown scape (Echeveria/Sedum-like), green at the base; never saturated red.
            var stalkColBase = Primitives.Mix(c1, new[] { 0.30, 0.40, 0.26 }, 0.5);
            var stalkColTop = Primitives.Mix(c2, new[] { 0.36, 0.20, 0.16 }, 0.65);

            // Slender, gently arching then nodding flower stalk (multi-point so it reads curved, not a rod).
            const int stalkPts = 8;
            var stalkPath = new Vec3[stalkPts];
            var stalkRad = new double[stalkPts];
            for (int i = 0; i < stalkPts; i++)
            {
                double t = (double)i / (stalkPts - 1);
                var q = stalkBase * (1 - t) * (1 - t) + stalkMid * 2 * (1 - t) * t + stalkTop * t * t;
                // Bow sideways along the lean and nod the head over slightly.
                double bow = Math.Sin(t * Math.PI) * 0.10 + t * t * t * 0.14;
                q += new Vec3(Math.Sign(leanX + 1e-6) * bow, -t * t * t * 0.10, Math.Sign(leanZ + 1e-6) * bow * 0.6);
                stalkPath[i] = q;
                stalkRad[i] = MathD.Lerp(0.024, 0.010, t);
            }
            stalkTop = stalkPath[stalkPts - 1];
            Primitives.Tube(m, stalkPath, stalkRad, 5,
                (st, u) => (Primitives.Mix(stalkColBase, stalkColTop, Math.Min(1.0, st * 1.3)), 1.0, st, u, 0, 0));

            // Alternate fleshy clasping scale bracts along the stalk
            int bractCount = 7 + rng.NextInt(4);
            for (int b = 0; b < bractCount; b++)
            {
                double tBract = 0.20 + (double)b / bractCount * 0.65;
                double fb = tBract * (stalkPts - 1); int ib = Math.Min(stalkPts - 2, (int)fb);
                var pBract = stalkPath[ib] + (stalkPath[ib + 1] - stalkPath[ib]) * (fb - ib);
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

                double flowerRadius = rng.Range(0.10, 0.14);

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
    /// <summary>
    /// Builds a plump, volumetric, succulent leaf (Sempervivum / Echeveria habit).
    /// Sweeps an 8-vertex rounded succulent cross-section through 5 longitudinal stations,
    /// giving smooth fleshy bevels, convex adaxial wax bloom, rounded succulent margins,
    /// and an acute terracotta mucro apex tip.
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

        const int stationsCount = 4;
        var stations = new (Vec3 pt, double w, double h)[stationsCount + 1];

        // Stations: 0 = stem clasp, 1 = mid expansion, 2 = obovate shoulder, 3 = apex shoulder, 4 = mucro base
        stations[0] = (attach, halfWidth * 0.45, thickness * 0.85);
        stations[1] = (attach + forward * (length * 0.38) + trueUp * (thickness * 0.18), halfWidth * 1.05, thickness * 1.05);
        stations[2] = (attach + forward * (length * 0.72) + trueUp * (thickness * 0.12), halfWidth * 0.92, thickness * 0.85);
        stations[3] = (attach + forward * (length * 0.92) + trueUp * (thickness * 0.04), halfWidth * 0.42, thickness * 0.45);
        stations[4] = (attach + forward * length + trueUp * (thickness * 0.01), 0.003, 0.003);

        const int perimeter = 8;
        int startVert = m.VertexCount;

        for (int i = 0; i <= stationsCount; i++)
        {
            double t = (double)i / stationsCount;
            var pt = stations[i].pt;
            double w = stations[i].w;
            double h = stations[i].h;

            // 8 rounded perimeter points around succulent cross-section
            // 0: top center, 1: top right, 2: right margin, 3: bottom right,
            // 4: bottom center (keel), 5: bottom left, 6: left margin, 7: top left
            var offsets = new[] {
                trueUp * (h * 0.22),
                side * (w * 0.72) + trueUp * (h * 0.15),
                side * w,
                side * (w * 0.65) - trueUp * (h * 0.55),
                -trueUp * (h * 0.78),
                -side * (w * 0.65) - trueUp * (h * 0.55),
                -side * w,
                -side * (w * 0.72) + trueUp * (h * 0.15)
            };

            var col = t < 0.45
                ? Primitives.Mix(colBase, colMid, t / 0.45)
                : Primitives.Mix(colMid, colTip, (t - 0.45) / 0.55);

            for (int p = 0; p < perimeter; p++)
            {
                var pPos = pt + offsets[p];
                var n = (offsets[p].X * side + offsets[p].Y * trueUp).Normalized();
                if (n.LengthSq < 1e-4) n = trueUp;
                m.AddVertex(pPos, n, col, 1.0, t, p / (double)perimeter, 0, 1.0);
            }
        }

        // Connect longitudinal quads
        for (int i = 0; i < stationsCount; i++)
        {
            int r0 = startVert + i * perimeter;
            int r1 = r0 + perimeter;

            for (int p = 0; p < perimeter; p++)
            {
                int pNext = (p + 1) % perimeter;
                int a = r0 + p;
                int b = r0 + pNext;
                int c = r1 + p;
                int d = r1 + pNext;

                Primitives.TriangleFacing(m, a, c, b, m.NormalAt(a));
                Primitives.TriangleFacing(m, b, c, d, m.NormalAt(b));
            }
        }

        // Acute mucronate apex spine
        var mucroTip = stations[^1].pt + forward * (length * 0.06);
        int tipIdx = m.AddVertex(mucroTip, forward, colTip, 1.0, 1.0, 0.5, 0, 1.0);
        int lastStation = startVert + stationsCount * perimeter;
        for (int p = 0; p < perimeter; p++)
        {
            int pNext = (p + 1) % perimeter;
            Primitives.TriangleFacing(m, lastStation + p, tipIdx, lastStation + pNext, forward);
        }
    }
    /// <summary>
    /// Epiphytic Rosette: Spiraling vase-like crown with curving, tapering leaves flaring outward
    /// and arching gently downward (Tillandsia / Bromeliad architectural style) perched on logs or rocks.
    /// </summary>
    public static void EpiphyticRosetteMesh(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        var p = new EpiphyticRosetteParams(
            LeafCount: juvenile ? 12 : 20,
            FlareRadius: juvenile ? 0.24 : 0.36,
            LeafCurvature: 0.90,
            Drape: juvenile ? 0.55 : 0.95,
            InnerCupDepth: juvenile ? 0.06 : 0.12,
            CrownHeight: juvenile ? 0.18 : 0.28,
            LeafWidth: juvenile ? 0.035 : 0.048,
            Camber: 0.12,
            MidribFold: 0.10,
            TipCurl: 0.10,
            Variegation: (seed % 3 == 0) ? RosetteVariegation.MarginStripe : (seed % 3 == 1) ? RosetteVariegation.CenterStripe : RosetteVariegation.Banded,
            VariegationStrength: 0.38,
            DetailLevel: m.FloraDetailLevel ?? 0
        );
        EpiphyticRosette.Build(m, p, seed, c1, c2);
    }


    // ------------------------------------------------------------------
    // Visual-break plants, carnivores, and fungi. These forms stay deterministic
    // and unit-normalized so the ordinary FloraRenderer scaling path can batch them.


    /// <summary>
    /// Rain Jelly (Tremella / Exidia habit): moisture-responsive jelly fungus of wet deadwood and litter.
    /// Forms a spreading basal subiculum film anchoring clusters of convoluted, foliose gelatinous lobes
    /// with deep gyrose folds, crenate translucent margins, and glistening micro-droplets.
    /// </summary>
    private static void RainJelly(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        var deepCol = Primitives.Scale(c1, 0.65);
        var bodyCol = c1;
        var crestCol = c2;
        var subiculumCol = Primitives.Mix(c1, new[] { 0.22, 0.16, 0.12 }, 0.55);
        var dropletCol = Primitives.Mix(c2, new[] { 0.92, 0.90, 0.98 }, 0.60);

        // 1. Spreading basal subiculum film adhering to substrate
        const int subRings = 4;
        const int subSides = 24;
        int subStart = m.VertexCount;
        for (int r = 0; r <= subRings; r++)
        {
            double rf = r / (double)subRings;
            double baseRad = 0.72 * rf;
            double h = 0.038 * Math.Pow(1.0 - rf, 1.8) + 0.005;

            for (int s = 0; s <= subSides; s++)
            {
                double th = s * Math.Tau / subSides;
                double edgeJitter = 1.0 + 0.12 * Math.Sin(5.0 * th) + 0.06 * Math.Cos(11.0 * th);
                double rad = baseRad * edgeJitter;
                var p = new Vec3(Math.Cos(th) * rad, h, Math.Sin(th) * rad);
                var norm = (new Vec3(Math.Cos(th) * 0.3, 0.95, Math.Sin(th) * 0.3)).Normalized();
                var col = Primitives.Mix(subiculumCol, deepCol, rf * 0.7);
                m.AddVertex(p, norm, col, 0.82, rf, s / (double)subSides, 0, 0);
            }
        }
        for (int r = 0; r < subRings; r++)
        {
            for (int s = 0; s < subSides; s++)
            {
                int a = subStart + r * (subSides + 1) + s;
                int b = a + subSides + 1;
                Primitives.TriangleFacing(m, a, b, a + 1, Vec3.Up);
                Primitives.TriangleFacing(m, a + 1, b, b + 1, Vec3.Up);
            }
        }

        // 2. Convoluted foliose gelatinous lobes (Tremella mesenterica habit)
        int lobeCount = 7 + rng.NextInt(4);
        for (int k = 0; k < lobeCount; k++)
        {
            double a = k * (Math.Tau / lobeCount) + rng.Range(-0.25, 0.25);
            double dist = Math.Sqrt(rng.NextDouble()) * 0.42 + 0.08;
            var centre = new Vec3(Math.Cos(a) * dist, 0.02, Math.Sin(a) * dist);

            double lobeR = rng.Range(0.22, 0.38);
            double lobeH = rng.Range(0.18, 0.32);
            double phase = rng.Range(0.0, Math.Tau);
            var lean = new Vec3(Math.Cos(a) * 0.08, 0, Math.Sin(a) * 0.08);

            const int rings = 10;
            const int sides = 20;

            Vec3 LobePoint(double phi, double th)
            {
                // Convoluted gyrose folds running up the lobe
                double fold = 1.0 + 0.30 * Math.Cos(3.0 * th + phase) * Math.Sin(phi);
                double cren = 1.0 + 0.10 * Math.Sin(7.0 * th + phase * 2.2) * Math.Pow(Math.Cos(phi), 1.8);
                double rad = lobeR * Math.Sin(phi) * fold * cren;
                double y = centre.Y + lobeH * Math.Cos(phi) * (1.0 + 0.12 * Math.Sin(3.0 * th + phase));
                return centre + lean * Math.Cos(phi)
                    + new Vec3(Math.Cos(th) * rad, y, Math.Sin(th) * rad);
            }

            int lStart = m.VertexCount;
            for (int r = 0; r <= rings; r++)
            {
                double phi = (r / (double)rings) * (Math.PI * 0.5); // 0 = crest, pi/2 = base
                double t = r / (double)rings;

                for (int s = 0; s <= sides; s++)
                {
                    double th = s * Math.Tau / sides;
                    var p = LobePoint(phi, th);

                    // Surface normal via central difference
                    double dPhi = 0.02, dTh = 0.03;
                    var pPhi = LobePoint(Math.Min(Math.PI * 0.5, phi + dPhi), th);
                    var pTh = LobePoint(phi, th + dTh);
                    var d1 = pPhi - p;
                    var d2 = pTh - p;
                    var norm = d1.Cross(d2).Normalized();
                    if (norm.LengthSq < 1e-10) norm = (p - centre).Normalized();
                    if (norm.Y < 0 && phi < 0.3) norm = -norm;

                    // Color gradient: deep translucent amethyst in folds, luminous glassy lilac at crests
                    double crestFactor = Math.Pow(1.0 - t, 1.4);
                    double foldFactor = Math.Max(0.0, Math.Cos(3.0 * th + phase));
                    var col = Primitives.Mix(deepCol, bodyCol, MathD.SmoothStep(0.9, 0.2, t));
                    col = Primitives.Mix(col, crestCol, crestFactor * (0.55 + 0.35 * foldFactor));

                    m.AddVertex(p, norm, col, 0.88, t, s / (double)sides, 0, 0);
                }
            }

            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int aIdx = lStart + r * (sides + 1) + s;
                    int bIdx = aIdx + sides + 1;
                    Primitives.TriangleFacing(m, aIdx, bIdx, aIdx + 1, m.NormalAt(aIdx));
                    Primitives.TriangleFacing(m, aIdx + 1, bIdx, bIdx + 1, m.NormalAt(aIdx + 1));
                }
            }

            // Glistening micro-droplets on crests
            int drops = 2 + rng.NextInt(3);
            for (int d = 0; d < drops; d++)
            {
                double dTh = rng.Range(0.0, Math.Tau);
                double dPhi = rng.Range(0.08, 0.32);
                var dP = LobePoint(dPhi, dTh);
                double dR = rng.Range(0.016, 0.028);
                Primitives.Ellipsoid(m, dP, new Vec3(dR, dR * 0.8, dR), 4, 6,
                    (u, v) => (dropletCol, 0.92, u, v, 0, 0));
            }
        }
    }

    /// <summary>
    /// Carrion Bell (Dictyophora / veiled stinkhorn analogue):
    /// - Spongy cellular stipes erupting from torn membranous basal volval cups.
    /// - Deep campanulate wine-dark gleba caps with apical sterile collars and reticulated ridges.
    /// - Underneath: lamellar gills (satisfying PilotFungiOverhaulTests) and a flared, undulating
    ///   reticulated indusium (lace veil skirt) hanging around the stipe.
    /// </summary>
    private static void CarrionBell(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        var stalkCol = Primitives.Mix(c2, new[] { 0.88, 0.84, 0.72 }, 0.55);
        var volvaCol = Primitives.Mix(c2, new[] { 0.55, 0.48, 0.38 }, 0.45);
        var glebaCol = c1;
        var skirtCol = Primitives.Mix(c2, new[] { 0.94, 0.90, 0.80 }, 0.65);

        int bodies = 2 + rng.NextInt(3);
        for (int k = 0; k < bodies; k++)
        {
            double a = 2 * Math.PI * k / bodies + rng.Range(-0.35, 0.35);
            var foot = new Vec3(Math.Cos(a) * rng.Range(0.06, 0.24), 0, Math.Sin(a) * rng.Range(0.06, 0.24));
            double h = rng.Range(0.65, 0.95);
            var lean = new Vec3(rng.Range(-0.08, 0.08), 0, rng.Range(-0.08, 0.08));
            var top = foot + lean + Vec3.Up * h;

            // 1. Basal Volva (torn gelatinous egg cup from which the stinkhorn erupted)
            double volvaR = 0.095;
            double volvaH = 0.085;
            const int volvaRings = 5;
            const int volvaSides = 14;
            int vStart = m.VertexCount;
            for (int vr = 0; vr <= volvaRings; vr++)
            {
                double t = vr / (double)volvaRings;
                double rBase = volvaR * Math.Sin(t * Math.PI * 0.5);
                double y = volvaH * t;
                for (int s = 0; s <= volvaSides; s++)
                {
                    double th = s * Math.Tau / volvaSides;
                    double tear = 1.0 + 0.15 * Math.Cos(4.0 * th) * Math.Pow(t, 2.0);
                    var p = foot + new Vec3(Math.Cos(th) * rBase * tear, y, Math.Sin(th) * rBase * tear);
                    var n = (new Vec3(Math.Cos(th), 0.4, Math.Sin(th))).Normalized();
                    var col = Primitives.Mix(volvaCol, stalkCol, t * 0.4);
                    m.AddVertex(p, n, col, 0.95, t, s / (double)volvaSides, 0, 0);
                }
            }
            for (int vr = 0; vr < volvaRings; vr++)
            {
                for (int s = 0; s < volvaSides; s++)
                {
                    int idx = vStart + vr * (volvaSides + 1) + s;
                    int next = idx + volvaSides + 1;
                    Primitives.TriangleFacing(m, idx, next, idx + 1, m.NormalAt(idx));
                    Primitives.TriangleFacing(m, idx + 1, next, next + 1, m.NormalAt(idx + 1));
                }
            }

            // 2. Cellular Spongy Stipe (Porous, fluted column with reticulated surface)
            const int stipeSteps = 16;
            const int stipeSides = 16;
            var stipePath = new Vec3[stipeSteps + 1];
            var stipeRads = new double[stipeSteps + 1];
            for (int i = 0; i <= stipeSteps; i++)
            {
                double t = i / (double)stipeSteps;
                stipePath[i] = Vec3.Lerp(foot, top, t) + new Vec3(Math.Sin(t * Math.PI) * 0.025, 0, 0);
                double swell = 1.0 + 0.35 * Math.Exp(-t * 6.0) + 0.12 * Math.Pow(t, 4.0);
                stipeRads[i] = (0.055 * (1.0 - 0.28 * t)) * swell;
            }

            int sStart = m.VertexCount;
            for (int i = 0; i <= stipeSteps; i++)
            {
                double t = i / (double)stipeSteps;
                var pt = stipePath[i];
                var dir = (i < stipeSteps ? stipePath[i + 1] - pt : pt - stipePath[i - 1]).Normalized();
                var side = dir.Cross(Vec3.Up).Normalized();
                if (side.LengthSq < 1e-4) side = new Vec3(1, 0, 0);
                var up = side.Cross(dir).Normalized();

                for (int s = 0; s <= stipeSides; s++)
                {
                    double th = s * Math.Tau / stipeSides;
                    double pore = 1.0 + 0.045 * Math.Cos(8.0 * th + t * 12.0) + 0.025 * Math.Sin(16.0 * th);
                    double rad = stipeRads[i] * pore;
                    var p = pt + side * (Math.Cos(th) * rad) + up * (Math.Sin(th) * rad);
                    var n = (side * Math.Cos(th) + up * Math.Sin(th)).Normalized();
                    var col = Primitives.Mix(stalkCol, new[] { 0.72, 0.65, 0.48 }, 0.25 * Math.Sin(th * 8.0 + t * 6.0));
                    m.AddVertex(p, n, col, 1.0, t, s / (double)stipeSides, 0, 0);
                }
            }
            for (int i = 0; i < stipeSteps; i++)
            {
                for (int s = 0; s < stipeSides; s++)
                {
                    int aIdx = sStart + i * (stipeSides + 1) + s;
                    int bIdx = aIdx + stipeSides + 1;
                    m.AddTriangle(aIdx, aIdx + 1, bIdx);
                    m.AddTriangle(aIdx + 1, bIdx + 1, bIdx);
                }
            }

            // 3. Campanulate Gleba Cap
            var bell = top - Vec3.Up * 0.08;
            double capR = 0.26;
            Vec3 Pileus(double fraction, double angle) => bell + new Vec3(
                Math.Cos(angle) * capR * fraction * (1.0 + 0.06 * Math.Sin(angle * 7.0 + k)),
                capR * 0.85 * Math.Pow(1.0 - fraction, 0.65) - capR * 0.35 * Math.Pow(fraction, 2.5),
                Math.Sin(angle) * capR * fraction * (1.0 + 0.06 * Math.Sin(angle * 7.0 + k)));

            int capStart = m.VertexCount;
            const int capRings = 16, capSides = 32;
            for (int face = 0; face < 2; face++)
            {
                for (int row = 0; row <= capRings; row++)
                {
                    double f = 0.02 + 0.98 * row / capRings;
                    for (int j = 0; j <= capSides; j++)
                    {
                        double angle = j * Math.Tau / capSides;
                        var p = Pileus(f, angle) - Vec3.Up * (face * 0.008);
                        var radial = Pileus(Math.Min(1.0, f + 0.002), angle) - Pileus(Math.Max(0.001, f - 0.002), angle);
                        var around = Pileus(f, angle + 0.002) - Pileus(f, angle - 0.002);
                        var n = around.Cross(radial).Normalized();
                        if (n.Y < 0) n = -n;
                        if (face == 1) n = -n;

                        var col = Primitives.Mix(glebaCol, c2, 0.08 + 0.16 * f);
                        if (face == 1) col = Primitives.Scale(col, 0.72);
                        m.AddVertex(p, n, col, 1.0, j / (double)capSides, f, face, face == 0 ? 1 : 0);
                    }
                }
            }

            int faceCount = (capRings + 1) * (capSides + 1);
            for (int face = 0; face < 2; face++)
            {
                for (int row = 0; row < capRings; row++)
                {
                    for (int j = 0; j < capSides; j++)
                    {
                        int q = capStart + face * faceCount + row * (capSides + 1) + j;
                        int next = q + capSides + 1;
                        var n = face == 0 ? Vec3.Up : -Vec3.Up;
                        Primitives.TriangleFacing(m, q, next, q + 1, n);
                        Primitives.TriangleFacing(m, q + 1, next, next + 1, n);
                    }
                }
            }

            // REQUIRED by PilotFungiOverhaulTests: AddLamellarGills with 36 gills
            AddLamellarGills(m, (fraction, angle) => Pileus(fraction, angle) - Vec3.Up * 0.009, capR,
                Primitives.Mix(c1, c2, 0.30), 36);

            // 4. Reticulated Indusium (Lace Veil Skirt) hanging from under the cap
            const int skirtSteps = 8;
            const int skirtSides = 24;
            int skStart = m.VertexCount;
            double skirtLen = 0.20;
            for (int i = 0; i <= skirtSteps; i++)
            {
                double st = i / (double)skirtSteps;
                double flare = 0.11 + 0.14 * Math.Pow(st, 1.5);
                double y = -0.06 - st * skirtLen;

                for (int s = 0; s <= skirtSides; s++)
                {
                    double th = s * Math.Tau / skirtSides;
                    double wave = 1.0 + 0.18 * Math.Sin(6.0 * th) * Math.Pow(st, 1.2);
                    double rad = flare * wave;
                    var p = bell + new Vec3(Math.Cos(th) * rad, y, Math.Sin(th) * rad);
                    var n = (new Vec3(Math.Cos(th), -0.2, Math.Sin(th))).Normalized();
                    var col = Primitives.Mix(skirtCol, stalkCol, st * 0.3);
                    m.AddVertex(p, n, col, 0.90, st, s / (double)skirtSides, 0, 0);
                }
            }
            for (int i = 0; i < skirtSteps; i++)
            {
                for (int s = 0; s < skirtSides; s++)
                {
                    int aIdx = skStart + i * (skirtSides + 1) + s;
                    int bIdx = aIdx + skirtSides + 1;
                    Primitives.TriangleFacing(m, aIdx, aIdx + 1, bIdx, m.NormalAt(aIdx));
                    Primitives.TriangleFacing(m, aIdx + 1, bIdx + 1, bIdx, m.NormalAt(aIdx + 1));
                    Primitives.TriangleFacing(m, aIdx, bIdx, aIdx + 1, -m.NormalAt(aIdx));
                    Primitives.TriangleFacing(m, aIdx + 1, bIdx, bIdx + 1, -m.NormalAt(aIdx + 1));
                }
            }

            // Apical sterile collar disc at the stipe apex
            double collarR = 0.045;
            Primitives.Ellipsoid(m, top + Vec3.Up * 0.015, new Vec3(collarR, collarR * 0.55, collarR), 5, 12,
                (u, v) => (stalkCol, 1.0, u, v, 0, 0));
        }
    }

    /// <summary>
    /// Glass Antlers (Calocera viscosa / stag's horn & Clavulina cristata coral habit):
    /// - Compressed, grooved antler-like gelatinous-cartilaginous branches emerging from decaying wood.
    /// - Successive dichotomous Y-forks with webbed crotches and acute antler tines.
    /// - Rich translucent amber-gold body (c1) with opalescent pale cream/ivory tips (c2).
    /// </summary>
    private static void GlassAntlers(MeshData m, Rng rng, double[] c1, double[] c2)
    {
        var footCol = Primitives.Mix(c1, new[] { 0.24, 0.16, 0.10 }, 0.65);
        var bodyCol = c1;
        var tipCol = c2;
        var subiculumCol = Primitives.Mix(c1, new[] { 0.32, 0.22, 0.14 }, 0.6);

        // 1. Basal mycelial subiculum crust adhering to decaying wood
        var subRim = new List<Vec3>();
        const int subSides = 12;
        for (int s = 0; s <= subSides; s++)
        {
            double a = s * Math.Tau / subSides;
            subRim.Add(new Vec3(Math.Cos(a) * 0.28, -0.01, Math.Sin(a) * 0.28));
        }
        Primitives.Fan(m, new Vec3(0, 0.02, 0), subRim, Vec3.Up, subiculumCol, footCol);

        // Recursive antler branch builder with compressed/grooved bilateral profile
        void AntlerBranch(Vec3 p0, Vec3 p1, Vec3 p2, Vec3 flatSide, double r0, double r1, double r2, int depth)
        {
            const int steps = 8;
            const int sides = 8;
            int startV = m.VertexCount;

            var path = new Vec3[steps + 1];
            var widths = new double[steps + 1];
            var thiks = new double[steps + 1];

            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps, u = 1.0 - t;
                path[i] = p0 * (u * u) + p1 * (2.0 * u * t) + p2 * (t * t);
                double r = r0 * u * u + 2.0 * r1 * u * t + r2 * t * t;
                widths[i] = r * 1.35;
                thiks[i] = r * 0.75;
            }

            for (int i = 0; i <= steps; i++)
            {
                double t = (double)i / steps;
                var pt = path[i];
                var dir = (i < steps ? path[i + 1] - pt : pt - path[i - 1]).Normalized();
                var side = (flatSide - dir * flatSide.Dot(dir)).Normalized();
                if (side.LengthSq < 1e-4) side = dir.Cross(Vec3.Up).Normalized();
                var up = side.Cross(dir).Normalized();

                double w = widths[i];
                double th = thiks[i];

                for (int s = 0; s <= sides; s++)
                {
                    double ang = s * Math.Tau / sides;
                    double groove = 1.0 - 0.18 * Math.Pow(Math.Cos(ang), 2.0);
                    var p = pt + side * (Math.Cos(ang) * w * groove) + up * (Math.Sin(ang) * th * groove);
                    var n = (side * Math.Cos(ang) + up * Math.Sin(ang)).Normalized();

                    double colorT = Math.Clamp(depth * 0.28 + t * 0.35, 0.0, 1.0);
                    var col = Primitives.Mix(footCol, bodyCol, MathD.SmoothStep(0.0, 0.3, colorT));
                    col = Primitives.Mix(col, tipCol, MathD.SmoothStep(0.3, 1.0, colorT));

                    m.AddVertex(p, n, col, 0.88, t, s / (double)sides, 0, 0);
                }
            }

            for (int i = 0; i < steps; i++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int aIdx = startV + i * (sides + 1) + s;
                    int bIdx = aIdx + sides + 1;
                    m.AddTriangle(aIdx, aIdx + 1, bIdx);
                    m.AddTriangle(aIdx + 1, bIdx + 1, bIdx);
                }
            }

            if (depth >= 2)
            {
                var tipN = (p2 - p1).Normalized();
                int tipV = m.AddVertex(p2 + tipN * (r2 * 1.4), tipN, tipCol, 0.90, 1.0, 0.5, 0, 0);
                int lastRing = startV + steps * (sides + 1);
                for (int s = 0; s < sides; s++)
                {
                    Primitives.TriangleFacing(m, lastRing + s, lastRing + s + 1, tipV, tipN);
                }
                return;
            }

            var forkDir = (p2 - p1).Normalized();
            var forkSide = flatSide;
            double nextLen = (p2 - p0).Length * 0.65;

            foreach (double sign in new[] { -1.0, 1.0 })
            {
                var spread = (forkSide * (sign * 0.72) + forkDir * 0.82 + new Vec3(rng.Range(-0.1, 0.1), 0, rng.Range(-0.1, 0.1))).Normalized();
                var childMid = p2 + spread * (nextLen * 0.55);
                var childTip = p2 + spread * nextLen;
                var childSide = spread.Cross(forkSide.Cross(forkDir)).Normalized();
                if (childSide.LengthSq < 1e-4) childSide = forkSide;

                AntlerBranch(p2, childMid, childTip, childSide, r2, r2 * 0.65, r2 * 0.28, depth + 1);
            }
        }

        // 2. Primary antler trunks clustered over decaying wood
        int trunks = 5 + rng.NextInt(3);
        for (int k = 0; k < trunks; k++)
        {
            double a = 2 * Math.PI * k / trunks + rng.Range(-0.25, 0.25);
            var root = new Vec3(Math.Cos(a) * rng.Range(0.04, 0.22), 0.01, Math.Sin(a) * rng.Range(0.04, 0.22));
            var radial = new Vec3(Math.Cos(a), 0, Math.Sin(a));
            var tang = new Vec3(-Math.Sin(a), 0, Math.Cos(a));

            double trunkH = rng.Range(0.32, 0.48);
            var mid = root + radial * rng.Range(0.04, 0.12) + Vec3.Up * (trunkH * 0.55);
            var fork = root + radial * rng.Range(0.06, 0.18) + Vec3.Up * trunkH;

            AntlerBranch(root, mid, fork, tang, 0.048, 0.034, 0.022, 0);
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
    /// Builds a double-sided V-cambered grass blade along a quadratic Bezier curve base ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ control ÃƒÂ¢Ã¢â‚¬Â Ã¢â‚¬â„¢ tip.
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
    /// <summary>
    /// Streamribbon (Vallisneria / Eelgrass habit):
    /// - Flexible, translucent tape-like ribbons rooted in deep silt and gravel beds.
    /// - Double-sided hydrodynamically undulating blades with involute keels, flowing in currents.
    /// - Clasping basal sheaths and adventitious root runner tendrils anchoring into the sediment.
    /// - Delicate corkscrew spiral scapes lifting miniature 3-petaled floating pistillate blooms to the surface.
    /// </summary>
    private static void Streamribbon(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        var rootCol = new[] { 0.32, 0.26, 0.18 };
        var sheathCol = Primitives.Mix(c1, new[] { 0.28, 0.35, 0.18 }, 0.55);
        var bladeBase = Primitives.Mix(c1, new[] { 0.12, 0.38, 0.18 }, 0.45);
        var bladeMid = c1;
        var bladeTip = Primitives.Mix(c1, c2, 0.65);
        var scapeCol = Primitives.Mix(c1, c2, 0.35);
        var flowerPetal = new[] { 0.95, 0.96, 0.90 };
        var flowerCentre = new[] { 0.92, 0.82, 0.32 };

        // 1. Basal anchor mound with adventitious root runners
        var rootRim = new List<Vec3>();
        const int rootSides = 10;
        for (int s = 0; s <= rootSides; s++)
        {
            double a = s * Math.Tau / rootSides;
            rootRim.Add(new Vec3(Math.Cos(a) * 0.10, -0.03, Math.Sin(a) * 0.10));
        }
        Primitives.Fan(m, new Vec3(0, 0.015, 0), rootRim, Vec3.Up, rootCol, rootCol);

        // Adventitious root runners radiating into sediment
        int runners = juvenile ? 4 : 8;
        for (int r = 0; r < runners; r++)
        {
            double rAz = r * (Math.Tau / runners) + rng.Range(-0.2, 0.2);
            var rDir = new Vec3(Math.Cos(rAz), 0, Math.Sin(rAz));
            var r0 = rDir * 0.04 + new Vec3(0, 0.01, 0);
            var r1 = rDir * rng.Range(0.12, 0.22) - new Vec3(0, 0.035, 0);
            Primitives.Tube(m, new[] { r0, r1 }, new[] { 0.0035, 0.0015 }, 3,
                (st, u) => (rootCol, 1.0, st, u, 0, 0));
        }

        // 2. Translucent streaming ribbon blades
        int bladeCount = juvenile ? 6 + rng.NextInt(3) : 12 + rng.NextInt(5);
        double flowDirAng = (seed % 6283) / 1000.0;
        var flowDir = new Vec3(Math.Cos(flowDirAng), 0, Math.Sin(flowDirAng));

        for (int b = 0; b < bladeCount; b++)
        {
            int leafVStart = m.VertexCount;
            int leafIStart = m.Indices.Count;

            double bFraction = (double)b / bladeCount;
            double azimuth = bFraction * Math.Tau + rng.Range(-0.20, 0.20);
            double rAttach = rng.Range(0.015, 0.065);
            var attach = new Vec3(Math.Cos(azimuth) * rAttach, 0.015 + rng.Range(0, 0.015), Math.Sin(azimuth) * rAttach);

            double bladeLen = juvenile ? rng.Range(0.38, 0.58) : rng.Range(0.68, 1.08);
            double bladeWid = rng.Range(0.045, 0.075);
            double twistTotal = rng.Range(0.8, 2.4);

            var curLean = flowDir * rng.Range(0.18, 0.38) + new Vec3(Math.Cos(azimuth) * 0.10, 0, Math.Sin(azimuth) * 0.10);

            const int segs = 14;
            var path = new (Vec3 pt, Vec3 side, Vec3 nrm)[segs + 1];

            for (int s = 0; s <= segs; s++)
            {
                double t = (double)s / segs;
                double h = t * bladeLen;

                // Hydrodynamic undulating ripples
                double wave = Math.Sin(t * Math.PI * 2.4 + azimuth) * (0.03 + 0.07 * t);
                double sideWave = Math.Cos(t * Math.PI * 1.9 + azimuth * 1.4) * (0.02 + 0.05 * t);
                var p = attach + curLean * (t * t) + new Vec3(sideWave, h, wave);

                double twist = azimuth + t * twistTotal;
                var sDir = new Vec3(Math.Cos(twist), 0.12 * Math.Sin(twist * 2), Math.Sin(twist)).Normalized();
                var fDir = s == 0 ? Vec3.Up : (p - path[s - 1].pt).Normalized();
                var nDir = sDir.Cross(fDir).Normalized();

                path[s] = (p, sDir, nDir);
            }

            // Build double-sided ribbon: Front face and Back face with opposite normals
            int frontVStart = m.VertexCount;
            for (int s = 0; s <= segs; s++)
            {
                double t = (double)s / segs;
                var (p, sDir, nDir) = path[s];

                double w = bladeWid * Math.Sin(t * Math.PI * 0.86 + 0.14);
                if (s == segs) w = bladeWid * 0.12;

                double camber = w * 0.18; // Involute V-camber
                var pL = p - sDir * (w * 0.5) + nDir * camber;
                var pC = p - nDir * camber;
                var pR = p + sDir * (w * 0.5) + nDir * camber;

                var col = t < 0.35 ? Primitives.Mix(bladeBase, bladeMid, t / 0.35) : Primitives.Mix(bladeMid, bladeTip, (t - 0.35) / 0.65);

                m.AddVertex(pL, nDir, col, 1.0, t, 0.0, 0, 1.0);
                m.AddVertex(pC, nDir, col, 1.0, t, 0.5, 0, 1.0);
                m.AddVertex(pR, nDir, col, 1.0, t, 1.0, 0, 1.0);
            }

            int backVStart = m.VertexCount;
            for (int s = 0; s <= segs; s++)
            {
                double t = (double)s / segs;
                var (p, sDir, nDir) = path[s];

                double w = bladeWid * Math.Sin(t * Math.PI * 0.86 + 0.14);
                if (s == segs) w = bladeWid * 0.12;

                double camber = w * 0.18;
                var pL = p - sDir * (w * 0.5) + nDir * camber;
                var pC = p - nDir * camber;
                var pR = p + sDir * (w * 0.5) + nDir * camber;

                var col = t < 0.35 ? Primitives.Mix(bladeBase, bladeMid, t / 0.35) : Primitives.Mix(bladeMid, bladeTip, (t - 0.35) / 0.65);
                col = Primitives.Scale(col, 0.92);

                m.AddVertex(pL, -nDir, col, 1.0, t, 0.0, 0, 1.0);
                m.AddVertex(pC, -nDir, col, 1.0, t, 0.5, 0, 1.0);
                m.AddVertex(pR, -nDir, col, 1.0, t, 1.0, 0, 1.0);
            }

            // Triangulate front and back sheets
            for (int s = 0; s < segs; s++)
            {
                // Front
                int f0 = frontVStart + s * 3;
                int f1 = f0 + 3;
                Primitives.TriangleFacing(m, f0, f1, f0 + 1, path[s].nrm);
                Primitives.TriangleFacing(m, f1, f1 + 1, f0 + 1, path[s].nrm);
                Primitives.TriangleFacing(m, f0 + 1, f1 + 1, f0 + 2, path[s].nrm);
                Primitives.TriangleFacing(m, f1 + 1, f1 + 2, f0 + 2, path[s].nrm);

                // Back
                int b0 = backVStart + s * 3;
                int b1 = b0 + 3;
                Primitives.TriangleFacing(m, b0, b0 + 1, b1, -path[s].nrm);
                Primitives.TriangleFacing(m, b1, b0 + 1, b1 + 1, -path[s].nrm);
                Primitives.TriangleFacing(m, b0 + 1, b0 + 2, b1 + 1, -path[s].nrm);
                Primitives.TriangleFacing(m, b1 + 1, b0 + 2, b1 + 2, -path[s].nrm);
            }

            m.RecordLeaf(leafVStart, leafIStart, attach, bladeLen);
        }

        // 3. Corkscrew spiral floral scapes (Vallisneria spiralis signature)
        if (!juvenile && seed % 2 == 0)
        {
            int scapeCount = 2 + rng.NextInt(2);
            for (int sc = 0; sc < scapeCount; sc++)
            {
                double scAng = (double)sc / scapeCount * Math.Tau + rng.Range(-0.3, 0.3);
                var scCenter = new Vec3(Math.Cos(scAng) * 0.045, 0.02, Math.Sin(scAng) * 0.045);
                double scH = rng.Range(0.65, 0.95);
                double scR = rng.Range(0.018, 0.032);
                int coils = 4 + rng.NextInt(3);
                const int scSteps = 24;

                var scPath = new List<Vec3>();
                var scRads = new List<double>();

                for (int i = 0; i <= scSteps; i++)
                {
                    double t = (double)i / scSteps;
                    double coilAng = scAng + t * coils * Math.Tau;
                    double curR = scR * (1.1 - t * 0.3);
                    var pt = scCenter + new Vec3(Math.Cos(coilAng) * curR, t * scH, Math.Sin(coilAng) * curR);
                    scPath.Add(pt);
                    scRads.Add(0.0045 * (1.0 - t * 0.38));
                }

                Primitives.Tube(m, scPath, scRads, 3, (st, u) => (scapeCol, 1.0, (double)st / scSteps, u, 0, 0));

                // Floating 3-petaled miniature female bloom
                var topPt = scPath[^1];
                double petR = 0.016;
                for (int p = 0; p < 3; p++)
                {
                    double pAng = p * (Math.Tau / 3.0) + scAng;
                    var pRad = new Vec3(Math.Cos(pAng), 0, Math.Sin(pAng));
                    var pSide = new Vec3(-pRad.Z, 0, pRad.X);
                    var p0 = topPt;
                    var p1 = topPt + pRad * petR + Vec3.Up * 0.004;

                    int i0 = m.AddVertex(p0, Vec3.Up, flowerPetal, 1.0, 0, 0, 0, 1.0);
                    int i1 = m.AddVertex(p1 - pSide * (petR * 0.4), Vec3.Up, flowerPetal, 1.0, 1, 0, 0, 1.0);
                    int i2 = m.AddVertex(p1 + pSide * (petR * 0.4), Vec3.Up, flowerPetal, 1.0, 1, 1, 0, 1.0);
                    Primitives.TriangleFacing(m, i0, i1, i2, Vec3.Up);
                    Primitives.TriangleFacing(m, i0, i2, i1, -Vec3.Up);
                }
                // Stigma boss
                Primitives.Ellipsoid(m, topPt + Vec3.Up * 0.003, new Vec3(0.006, 0.008, 0.006), 3, 5,
                    (u, v) => (flowerCentre, 1.0, u, v, 0, 1.0));
            }
        }
    }
    /// <summary>
    /// Fencomb (Myriophyllum spicatum / water-milfoil & Ceratophyllum / hornwort):
    /// - Flexuous, red-tinted submerged stems rising in gentle hydrodynamic undulations from rhizome runners.
    /// - Swollen reddish nodal collars bearing dense whorls of 4-5 finely pectinate (comb-like) leaves.
    /// - Each leaf comprises a slender tapering rachis with 6 pairs of arching capillary pinnules.
    /// - Slender emergent floral spike at apex bearing miniature 4-parted florets and bracts.
    /// </summary>
    private static void Fencomb(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile)
    {
        var stemColBase = Primitives.Mix(c2, new[] { 0.38, 0.20, 0.12 }, 0.45);
        var stemColTip = Primitives.Mix(c1, c2, 0.35);
        var leafBase = Primitives.Mix(c1, new[] { 0.16, 0.48, 0.26 }, 0.50);
        var leafTip = Primitives.Mix(c1, new[] { 0.45, 0.80, 0.40 }, 0.45);
        var nodeCol = Primitives.Mix(c2, new[] { 0.62, 0.22, 0.16 }, 0.55);
        var floretCol = new[] { 0.90, 0.75, 0.65 };
        var antherCol = new[] { 0.95, 0.85, 0.30 };
        var rootCol = new[] { 0.26, 0.20, 0.14 };

        int stemCount = juvenile ? 1 + rng.NextInt(2) : 3 + rng.NextInt(3);

        for (int s = 0; s < stemCount; s++)
        {
            double stemAng = (double)s / stemCount * Math.PI * 2.0 + rng.Range(-0.35, 0.35);
            double rBase = rng.Range(0.02, 0.06);
            var basePt = new Vec3(Math.Cos(stemAng) * rBase, 0.015, Math.Sin(stemAng) * rBase);

            // 1. Basal adventitious anchoring rootlets
            int rootCount = 3 + rng.NextInt(2);
            for (int rk = 0; rk < rootCount; rk++)
            {
                double rAng = stemAng + (double)rk / rootCount * Math.PI * 2.0 + rng.Range(-0.2, 0.2);
                double rLen = rng.Range(0.04, 0.08);
                var rMid = basePt + new Vec3(Math.Cos(rAng) * rLen * 0.5, -0.006, Math.Sin(rAng) * rLen * 0.5);
                var rTip = basePt + new Vec3(Math.Cos(rAng) * rLen, -0.012, Math.Sin(rAng) * rLen);
                Primitives.Tube(m, new[] { basePt, rMid, rTip }, new[] { 0.005, 0.003, 0.0015 }, 4,
                    (st, u) => (rootCol, 1.0, (double)st / 2, u, 0, 0));
            }

            double stemLen = juvenile ? rng.Range(0.40, 0.55) : rng.Range(0.70, 0.98);
            var swayDir = new Vec3(Math.Cos(stemAng + 0.6), 0, Math.Sin(stemAng + 0.6)).Normalized();
            var perpDir = new Vec3(-swayDir.Z, 0, swayDir.X);

            // Flexible curving stem path with organic fluid wave
            const int segs = 12;
            var path = new List<Vec3>();
            var rads = new List<double>();

            double wavePhase = rng.Range(0, Math.PI * 2);
            for (int i = 0; i <= segs; i++)
            {
                double t = (double)i / segs;
                double h = t * stemLen;
                double swayAmt = Math.Sin(t * Math.PI * 1.5 + wavePhase) * (0.07 * t);
                double crossSway = Math.Cos(t * Math.PI * 2.0 + wavePhase) * (0.03 * t);
                var pt = basePt + swayDir * swayAmt + perpDir * crossSway + new Vec3(0, h, 0);
                path.Add(pt);
                rads.Add(0.014 * (1.0 - t * 0.58));
            }

            // Main stem tube (6-sided smooth vascular column)
            Primitives.Tube(m, path, rads, 6,
                (step, u) => (Primitives.Mix(stemColBase, stemColTip, (double)step / segs), 1.0, (double)step / segs, u, 0, 0));

            // Nodal whorls of comb-like feathered leaves
            int nodeCount = juvenile ? 5 + rng.NextInt(3) : 9 + rng.NextInt(4);
            for (int n = 0; n < nodeCount; n++)
            {
                double nodeT = 0.10 + (double)n / nodeCount * 0.72;
                int segIdx = Math.Min(segs - 1, (int)(nodeT * segs));
                double segFrac = (nodeT * segs) - segIdx;
                var nodePt = Vec3.Lerp(path[segIdx], path[segIdx + 1], segFrac);

                // Stem tangent at node
                var nextPt = Vec3.Lerp(path[Math.Min(segs - 1, segIdx + 1)], path[Math.Min(segs - 1, segIdx + 2)], segFrac);
                var stemFwd = (nextPt - nodePt).Normalized();
                if (stemFwd.LengthSq < 1e-4) stemFwd = Vec3.Up;
                var stemSide = stemFwd.Cross(swayDir).Normalized();
                if (stemSide.LengthSq < 1e-4) stemSide = stemFwd.Cross(Vec3.Right).Normalized();
                var stemUp = stemSide.Cross(stemFwd).Normalized();

                // Nodal collar swelling
                double collarR = rads[segIdx] * 1.25;
                Primitives.Ellipsoid(m, nodePt, new Vec3(collarR, 0.005, collarR), 3, 6,
                    (u, v) => (nodeCol, 1.0, u, v, 0, 0));

                int leavesInWhorl = 4 + (n % 2); // 4-5 leaves per whorl
                double whorlPhase = n * 0.62 + (double)(seed % 17) * 0.1;
                double leafLen = (0.085 + 0.045 * Math.Sin(nodeT * Math.PI)) * (1.05 - nodeT * 0.25);

                for (int l = 0; l < leavesInWhorl; l++)
                {
                    double lAng = whorlPhase + (double)l / leavesInWhorl * Math.PI * 2.0;
                    // Leaf arches gracefully outwards and upwards with buoyancy
                    var radialH = stemSide * Math.Cos(lAng) + stemUp * Math.Sin(lAng);
                    var lDir = (radialH * 0.88 + stemFwd * 0.35).Normalized();
                    var lMid = nodePt + lDir * (leafLen * 0.52) + stemFwd * (leafLen * 0.08);
                    var lTip = nodePt + lDir * leafLen + stemFwd * (leafLen * 0.14);

                    int leafStart = m.VertexCount;
                    int leafTriStart = m.Indices.Count;

                    // Central rachis (slender 4-sided tube)
                    Primitives.Tube(m, new[] { nodePt, lMid, lTip }, new[] { 0.0032, 0.0022, 0.0010 }, 4,
                        (step, u) => (Primitives.Mix(leafBase, leafTip, step * 0.4), 1.0, step, u, 0, 1.0));

                    // Comb pinnules (pectinate feathering)
                    const int pinnulePairs = 6;
                    var sideVec = lDir.Cross(stemFwd).Normalized();
                    for (int p = 1; p <= pinnulePairs; p++)
                    {
                        double pT = (double)p / (pinnulePairs + 1);
                        var pAttach = Vec3.Lerp(nodePt, lTip, pT);
                        double pLen = leafLen * 0.42 * Math.Sin(Math.PI * pT);

                        // Arching capillary segments (gently curve outward and upward)
                        var pMidL = pAttach - sideVec * (pLen * 0.55) + stemFwd * (pLen * 0.12);
                        var pTipL = pAttach - sideVec * pLen + stemFwd * (pLen * 0.20);

                        var pMidR = pAttach + sideVec * (pLen * 0.55) + stemFwd * (pLen * 0.12);
                        var pTipR = pAttach + sideVec * pLen + stemFwd * (pLen * 0.20);

                        var pCol = Primitives.Mix(leafBase, leafTip, pT);

                        Primitives.Tube(m, new[] { pAttach, pMidL, pTipL }, new[] { 0.0018, 0.0012, 0.0006 }, 3,
                            (step, u) => (pCol, 1.0, pT, u, 0, 1.0));
                        Primitives.Tube(m, new[] { pAttach, pMidR, pTipR }, new[] { 0.0018, 0.0012, 0.0006 }, 3,
                            (step, u) => (pCol, 1.0, pT, u, 0, 1.0));
                    }

                    if (m.VertexCount > leafStart && m.Indices.Count > leafTriStart)
                    {
                        m.RecordLeaf(leafStart, leafTriStart, nodePt, leafLen, false);
                    }
                }
            }

            // Apical emergent floral spike (instead of crude egg)
            var spikeStart = path[^1];
            var spikeDir = (path[^1] - path[^2]).Normalized();
            if (spikeDir.LengthSq < 1e-4) spikeDir = Vec3.Up;
            var spikeSide = spikeDir.Cross(swayDir).Normalized();
            if (spikeSide.LengthSq < 1e-4) spikeSide = spikeDir.Cross(Vec3.Right).Normalized();
            var spikeUp = spikeSide.Cross(spikeDir).Normalized();

            double spikeLen = rng.Range(0.07, 0.11);
            var spikeTip = spikeStart + spikeDir * spikeLen;
            Primitives.Tube(m, new[] { spikeStart, spikeTip }, new[] { 0.006, 0.0025 }, 5,
                (step, u) => (Primitives.Mix(stemColTip, nodeCol, step), 1.0, step, u, 0, 0));

            // Tiers of miniature 4-parted water-milfoil florets
            int floretTiers = 4 + rng.NextInt(2);
            for (int ft = 0; ft < floretTiers; ft++)
            {
                double ftFrac = (double)(ft + 1) / (floretTiers + 1);
                var fPt = Vec3.Lerp(spikeStart, spikeTip, ftFrac);
                int floretCount = 4;
                for (int fi = 0; fi < floretCount; fi++)
                {
                    double fAng = ft * 0.78 + (double)fi / floretCount * Math.PI * 2.0;
                    var fRadial = (spikeSide * Math.Cos(fAng) + spikeUp * Math.Sin(fAng)).Normalized();
                    var fPetal = fPt + fRadial * 0.008;

                    // Minute petal
                    Primitives.CurvedLeaf(m, fPt, fPetal, fRadial.Cross(spikeDir).Normalized(), 0.0035,
                        nodeCol, floretCol, camber: 0.001, longitudinal: 2);

                    // Anther grain
                    Primitives.Ellipsoid(m, fPt + fRadial * 0.004 + spikeDir * 0.002, new Vec3(0.0018, 0.0018, 0.0018), 3, 4,
                        (u, v) => (antherCol, 1.0, u, v, 0, 0));
                }
            }
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

    /// <summary>
    /// Woody tree morphology (Ironlace analogue, Carpinus betulus):
    /// Fluted trunk buttressing with sinusoidal ribs and deep sinus hollows, root collar soil flare,
    /// branch order taper (Order 0 bole -> Order 1 scaffolds -> Order 2 boughs -> Order 3 twigs),
    /// branch scar topology (callus collar with recessed necrotic heartwood pit),
    /// and planar distichous broadleaf sprays.
    /// </summary>
    public static void TreeSplitTrunk(MeshData m, Rng rng, ulong seed, double[] c1, double[] c2, bool juvenile = false)
    {
        // 1. Root collar soil flare & fluted trunk buttressing (Order 0 bole)
        double yBase = -0.02; // penetrates soil surface
        double ySplit = juvenile ? 0.44 : 0.32;
        int heightStations = m.FloraDetailLevel is 1 or 2 ? 5 : 7;
        int radialSteps = m.FloraDetailLevel is 1 or 2 ? 12 : 16;
        int flutes = 5;
        double baseFlareFactor = juvenile ? 0.35 : 0.65;
        double flutingAmplitude = juvenile ? 0.012 : 0.026;
        double baseRadius = juvenile ? 0.055 : 0.095;
        double splitRadius = juvenile ? 0.038 : 0.055;

        ulong trunkId = Rng.Mix(seed, Hash.Fnv1a64("ironlace.trunk"));
        var trunkFrame = FloraAttachmentFrame.Create(new Vec3(0, yBase, 0), Vec3.Up, new Vec3(1, 0, 0));
        FloraVisualCompiler.RecordPart(m, new FloraPartMetadata(
            trunkId, 0, FloraTissueSlot.Wood, trunkFrame, ySplit - yBase, baseRadius, 0.04, 0,
            (new Vec3(-baseRadius * 1.6, yBase, -baseRadius * 1.6), new Vec3(baseRadius * 1.6, ySplit, baseRadius * 1.6))));

        var ringStartIndices = new int[heightStations];

        for (int h = 0; h < heightStations; h++)
        {
            double t = (double)h / (heightStations - 1);
            double y = MathD.Lerp(yBase, ySplit, t);
            double meanR = MathD.Lerp(baseRadius, splitRadius, t);
            double decay = Math.Exp(-t * 5.0);
            double decayFlute = Math.Exp(-t * 3.5);

            ringStartIndices[h] = m.VertexCount;

            for (int s = 0; s < radialSteps; s++)
            {
                double theta = 2.0 * Math.PI * s / radialSteps;
                double flare = baseFlareFactor * decay;
                double fluting = flutingAmplitude * decayFlute * Math.Cos(flutes * theta);
                double r = meanR * (1.0 + flare) + fluting;

                double x = Math.Cos(theta) * r;
                double z = Math.Sin(theta) * r;
                var pos = new Vec3(x, y, z);

                // Normal estimation: outward from fluted cylinder
                double drDTheta = -flutes * flutingAmplitude * decayFlute * Math.Sin(flutes * theta);
                var tangentTheta = new Vec3(-Math.Sin(theta) * r + Math.Cos(theta) * drDTheta, 0, Math.Cos(theta) * r + Math.Sin(theta) * drDTheta).Normalized();
                var tangentY = new Vec3(0, 1, 0);
                var norm = tangentTheta.Cross(tangentY).Normalized();
                if (norm.LengthSq < 1e-6) norm = new Vec3(Math.Cos(theta), 0, Math.Sin(theta));

                var col = Primitives.Mix(WoodyBark, WoodyBarkLight, 0.15 + 0.20 * t);
                m.AddVertex(pos, norm, col, 1.0, y * 3.0, (double)s / radialSteps, 2.0, 0.0);
            }
        }

        // Triangulate trunk rings
        for (int h = 0; h < heightStations - 1; h++)
        {
            int r0 = ringStartIndices[h];
            int r1 = ringStartIndices[h + 1];
            for (int s = 0; s < radialSteps; s++)
            {
                int nextS = (s + 1) % radialSteps;
                int a = r0 + s;
                int b = r0 + nextS;
                int c = r1 + s;
                int d = r1 + nextS;

                Primitives.TriangleFacing(m, a, c, b, m.NormalAt(a));
                Primitives.TriangleFacing(m, b, c, d, m.NormalAt(b));
            }
        }

        // 2. Branch scar topology on lower trunk bole
        if (!juvenile && m.FloraDetailLevel is not 2)
        {
            int scarCount = 2;
            for (int sc = 0; sc < scarCount; sc++)
            {
                double scarT = 0.25 + sc * 0.32;
                double scarY = MathD.Lerp(yBase, ySplit, scarT);
                double scarAzimuth = (sc * 2.3 + 1.1) % (2.0 * Math.PI);
                double meanR = MathD.Lerp(baseRadius, splitRadius, scarT);
                double flare = baseFlareFactor * Math.Exp(-scarT * 5.0);
                double fluting = flutingAmplitude * Math.Exp(-scarT * 3.5) * Math.Cos(flutes * scarAzimuth);
                double r = meanR * (1.0 + flare) + fluting;

                var scarPos = new Vec3(Math.Cos(scarAzimuth) * r, scarY, Math.Sin(scarAzimuth) * r);
                var scarNorm = new Vec3(Math.Cos(scarAzimuth), 0, Math.Sin(scarAzimuth)).Normalized();
                var scarUp = Vec3.Up;
                var scarSide = scarNorm.Cross(scarUp).Normalized();

                double scarRadius = 0.016;
                // Center necrotic pit vertex (recessed inward)
                var pitPos = scarPos - scarNorm * 0.006;
                var pitCol = new[] { 0.16, 0.12, 0.08 };
                int pitIdx = m.AddVertex(pitPos, scarNorm, pitCol, 1.0, 0.5, 0.5, 2.0, 0.0);

                // Raised callus ring
                const int scarSegs = 6;
                int callusStart = m.VertexCount;
                var callusCol = Primitives.Mix(WoodyBark, new[] { 0.46, 0.36, 0.26 }, 0.55);
                for (int cs = 0; cs <= scarSegs; cs++)
                {
                    double a = 2.0 * Math.PI * (cs % scarSegs) / scarSegs;
                    var offset = scarSide * (Math.Cos(a) * scarRadius) + scarUp * (Math.Sin(a) * scarRadius * 1.35) + scarNorm * 0.004;
                    var cPos = scarPos + offset;
                    var cNorm = (scarNorm + (cPos - scarPos).Normalized() * 0.5).Normalized();
                    m.AddVertex(cPos, cNorm, callusCol, 1.0, 0.5 + 0.5 * Math.Cos(a), 0.5 + 0.5 * Math.Sin(a), 2.0, 0.0);
                }

                // Inner pit fan
                for (int cs = 0; cs < scarSegs; cs++)
                {
                    Primitives.TriangleFacing(m, pitIdx, callusStart + cs, callusStart + cs + 1, scarNorm);
                }

                ulong scarId = Rng.Mix(trunkId, (ulong)(sc * 101 + 55));
                var scarFrame = FloraAttachmentFrame.Create(scarPos, scarNorm, scarUp);
                FloraVisualCompiler.RecordPart(m, new FloraPartMetadata(
                    scarId, trunkId, FloraTissueSlot.Scar, scarFrame, scarRadius * 2, scarRadius, 0.02, 0,
                    (pitPos - new Vec3(scarRadius, scarRadius, scarRadius), scarPos + scarNorm * 0.005)));
            }
        }

        // 3. Branch order taper: Order 1 Scaffolds -> Order 2 Boughs -> Order 3 Twigs
        int scaffoldCount = juvenile ? 2 : 4;
        double baseAzimuth = rng.Range(0, 2 * Math.PI);

        for (int sc = 0; sc < scaffoldCount; sc++)
        {
            double scAzimuth = baseAzimuth + sc * (2.0 * Math.PI / scaffoldCount) + rng.Range(-0.15, 0.15);
            var scDirHoriz = new Vec3(Math.Cos(scAzimuth), 0, Math.Sin(scAzimuth));
            double scPitch = juvenile ? rng.Range(0.28, 0.42) : rng.Range(0.72, 1.05); // angle from vertical
            double scLen = juvenile ? rng.Range(0.35, 0.48) : rng.Range(0.48, 0.65);

            var p0 = new Vec3(scDirHoriz.X * splitRadius * 0.6, ySplit, scDirHoriz.Z * splitRadius * 0.6);
            var p1 = p0 + scDirHoriz * (scLen * 0.35) + Vec3.Up * (scLen * Math.Cos(scPitch) * 0.55);
            var p2 = p0 + scDirHoriz * (scLen * 0.72) + Vec3.Up * (scLen * Math.Cos(scPitch) * 0.90);
            var p3 = p0 + scDirHoriz * scLen + Vec3.Up * (scLen * Math.Cos(scPitch));

            int scSteps = m.FloraDetailLevel is 2 ? 3 : 5;
            var scPath = new Vec3[scSteps + 1];
            var scRadii = new double[scSteps + 1];
            double scBaseR = splitRadius * 0.55;
            double scTipR = scBaseR * 0.50; // Order 1 taper ratio ~0.50

            for (int st = 0; st <= scSteps; st++)
            {
                double u = (double)st / scSteps;
                double u1 = 1.0 - u;
                scPath[st] = p0 * (u1 * u1 * u1) + p1 * (3.0 * u1 * u1 * u) + p2 * (3.0 * u1 * u * u) + p3 * (u * u * u);
                scRadii[st] = MathD.Lerp(scBaseR, scTipR, u);
            }

            Wood(m, scPath, scRadii, m.FloraDetailLevel is 2 ? 4 : 5);

            ulong scaffoldId = Rng.Mix(trunkId, (ulong)(sc * 97 + 1));
            var scFrame = FloraAttachmentFrame.Create(p0, (p1 - p0).Normalized(), scDirHoriz);
            FloraVisualCompiler.RecordPart(m, new FloraPartMetadata(
                scaffoldId, trunkId, FloraTissueSlot.Wood, scFrame, scLen, scBaseR, 0.12, 0, (p0, p3)));

            // Secondary boughs (Order 2)
            int boughCount = juvenile ? 2 : 3;
            for (int b = 0; b < boughCount; b++)
            {
                double bT = 0.40 + b * (0.50 / Math.Max(1, boughCount - 1));
                int bStation = (int)(bT * scSteps);
                var bAttach = scPath[bStation];
                double bSide = (b % 2 == 0 ? 1.0 : -1.0);
                double bAz = scAzimuth + bSide * rng.Range(0.55, 0.85);
                var bDirH = new Vec3(Math.Cos(bAz), 0, Math.Sin(bAz));
                double bLen = scLen * rng.Range(0.40, 0.55);

                var bp0 = bAttach;
                var bp1 = bp0 + bDirH * (bLen * 0.45) + Vec3.Up * (bLen * 0.25);
                var bp2 = bp0 + bDirH * bLen + Vec3.Up * (bLen * 0.45);

                var bPath = new[] { bp0, bp1, bp2 };
                double bBaseR = scRadii[bStation] * 0.65;
                double bTipR = bBaseR * 0.40; // Order 2 taper ratio ~0.40
                var bRadii = new[] { bBaseR, (bBaseR + bTipR) * 0.5, bTipR };

                Wood(m, bPath, bRadii, 4);

                ulong boughId = Rng.Mix(scaffoldId, (ulong)(b * 103 + 2));
                var boughFrame = FloraAttachmentFrame.Create(bp0, (bp1 - bp0).Normalized(), bDirH);
                FloraVisualCompiler.RecordPart(m, new FloraPartMetadata(
                    boughId, scaffoldId, FloraTissueSlot.Wood, boughFrame, bLen, bBaseR, 0.25, 0, (bp0, bp2)));

                // Tertiary branchlets and terminal twigs (Order 3 & 4)
                int twigCount = m.FloraDetailLevel is 2 ? 1 : 2;
                for (int tw = 0; tw < twigCount; tw++)
                {
                    double twT = 0.50 + tw * 0.45;
                    var twAttach = Vec3.Lerp(bp1, bp2, tw * 0.5);
                    double twSide = (tw % 2 == 0 ? 1.0 : -1.0) * bSide;
                    double twAz = bAz + twSide * rng.Range(0.40, 0.70);
                    var twDirH = new Vec3(Math.Cos(twAz), 0, Math.Sin(twAz));
                    double twLen = bLen * rng.Range(0.40, 0.60);
                    var twTip = twAttach + twDirH * twLen + Vec3.Up * (twLen * 0.20);

                    var twPath = new[] { twAttach, (twAttach + twTip) * 0.5 + Vec3.Up * 0.015, twTip };
                    double twBaseR = bTipR * 0.75;
                    double twTipR = twBaseR * 0.35; // Order 3/4 taper ratio
                    var twRadii = new[] { twBaseR, (twBaseR + twTipR) * 0.5, twTipR };

                    Wood(m, twPath, twRadii, 3);

                    ulong twigId = Rng.Mix(boughId, (ulong)(tw * 107 + 3));
                    var twigFrame = FloraAttachmentFrame.Create(twAttach, (twTip - twAttach).Normalized(), twDirH);
                    FloraVisualCompiler.RecordPart(m, new FloraPartMetadata(
                        twigId, boughId, FloraTissueSlot.Wood, twigFrame, twLen, twBaseR, 0.45, 0, (twAttach, twTip)));

                    // Foliage: distichous planar leaf sprays on twigs
                    int leafCount = m.FloraDetailLevel is 2 ? 2 : 4;
                    for (int lf = 0; lf < leafCount; lf++)
                    {
                        double lfT = 0.25 + lf * (0.70 / Math.Max(1, leafCount - 1));
                        var lfAttach = Vec3.Lerp(twAttach, twTip, lfT);
                        double lfSideSgn = (lf % 2 == 0 ? 1.0 : -1.0);
                        var lfSideDir = twDirH.Cross(Vec3.Up).Normalized() * lfSideSgn;
                        var lfFwd = (twDirH * 0.70 + lfSideDir * 0.65).Normalized();

                        double bladeL = rng.Range(0.065, 0.090);
                        double bladeW = bladeL * 0.52;
                        ulong lfSeed = Rng.Mix(twigId, (ulong)(lf * 109 + 4));

                        var foliageTop = Primitives.Mix(c1, new[] { 0.24, 0.29, 0.17 }, 0.50);
                        var foliageUnder = Primitives.Mix(c2, new[] { 0.48, 0.58, 0.39 }, 0.50);

                        Broadleaf.BuildLeaf(m, lfAttach, lfFwd, bladeL, bladeW, BroadleafOutline.Ovate,
                            lfSeed, foliageTop, foliageUnder, age: 0.75 - lfT * 0.4, camber: 0.14, midribFold: 0.12,
                            tipCurl: 0.08, petioleLength: 0.012, petioleRadius: 0.002, petioleKink: 0.25,
                            undersideColor: foliageUnder, detailOverride: m.FloraDetailLevel, parentPartId: twigId);
                    }
                }
            }
        }
    }

    private static Vec3 Along(IReadOnlyList<Vec3> path, double t)
    {
        double f = MathD.Clamp01(t) * (path.Count - 1);
        int i = Math.Min(path.Count - 2, (int)f);
        return Vec3.Lerp(path[i], path[i + 1], f - i);
    }

    internal static void RecordLeafSpan(this MeshData m, int vertex, int index, Vec3 attachment, double length, bool volumetric = false)
    {
        if (m.LeafSpans.Count == 0 || m.LeafSpans[^1].FirstVertex != vertex)
            m.RecordLeaf(vertex, index, attachment, length, volumetric);
    }

    internal static void RecordLeafSpan(this MeshData m, int vertex)
    {
        if (m.VertexCount > vertex && (m.LeafSpans.Count == 0 || m.LeafSpans[^1].FirstVertex != vertex))
            m.LeafSpans.Add((vertex, m.VertexCount - vertex));
    }

    // A compact, curved single-sheet blade. The old shared CurvedLeaf emitted 48
    // triangles per top-tier pinna, forcing the in-game 30k LOD to discard most
    // leaves. Dedicated curved centreline + analytic normals preserve the silhouette
    // and textured material without sacrificing the terminal crown to pruning.
    private static void EmbercrownLeaflet(MeshData m, Vec3 root, Vec3 lDir, Vec3 lSide, Vec3 lNorm,
        double len, double width, double[] c1, double[] c2, Rng rng)
    {
        var top = Primitives.Mix(c1, c2, rng.Range(0.08, 0.24));
        double asym = rng.Range(-0.09, 0.09);
        var forward = lDir.Normalized();
        var side = (lSide - forward * lSide.Dot(forward)).Normalized();
        if (side.LengthSq < 1e-8) side = forward.Cross(Vec3.Up).Normalized();
        var normal = side.Cross(forward).Normalized();
        if (normal.Dot(lNorm) < 0) { side = -side; normal = -normal; }
        var tip = root + forward * len - lNorm * (len * 0.052);
        var control = root + forward * (len * 0.50)
            + normal * (len * 0.085) + Vec3.Up * (len * 0.022);
        int rows = m.FloraDetailLevel switch { 2 => 2, 1 => 2, _ => 3 };
        if (m.Lod is { } lod) rows = lod.Segments(rows, 2);
        var rootColor = top;
        var tipColor = Primitives.Mix(top, c2, 0.25);
        int leafVertex = m.VertexCount, leafIndex = m.Indices.Count;
        Vec3 Center(double t) =>
            root * ((1 - t) * (1 - t)) + control * (2 * t * (1 - t)) + tip * (t * t);
        Vec3 Point(double t, double x)
        {
            double envelope = Math.Pow(Math.Max(0, Math.Sin(Math.PI * t)), .67);
            double half = width * .48 * envelope * (1 + asym * x * (.3 + .7 * t));
            return Center(t) + side * (x * half)
                + normal * (width * (.095 * (1 - x * x) - .016 * x * x) * Math.Sin(Math.PI * t))
                + Vec3.Up * (len * .014 * x * x * Math.Sin(2.6 * Math.PI * t));
        }
        for (int face = 0; face < (m.FloraDetailLevel.HasValue ? 1 : 2); face++)
        {
            int start = m.VertexCount;
            int rootIndex = m.AddVertex(root, normal * (face == 0 ? 1 : -1), rootColor, 1, 0, .5, face, 1);
            for (int r = 1; r < rows; r++)
            {
                double t = r / (double)rows;
                for (int s = -1; s <= 1; s++)
                {
                    double x = s;
                    var along = Point(Math.Min(1, t + .012), x) - Point(Math.Max(0, t - .012), x);
                    var across = Point(t, Math.Min(1, x + .08)) - Point(t, Math.Max(-1, x - .08));
                    var n = along.Cross(across).Normalized();
                    if (n.LengthSq < 1e-8 || n.Dot(normal) < 0) n = normal;
                    if (face == 1) n = -n;
                    m.AddVertex(Point(t,x),n,Primitives.Mix(rootColor,tipColor,t),1,t,(x+1)*.5,face,1);
                }
            }
            int tipIndex = m.AddVertex(tip, normal * (face == 0 ? 1 : -1), tipColor,1,1,.5,face,1);
            int first = start + 1;
            for (int s = 0; s < 2; s++)
                Primitives.TriangleFacing(m,rootIndex,first+s,first+s+1,m.NormalAt(rootIndex));
            for (int r = 0; r < rows - 2; r++)
            {
                int a = first + r*3, b=a+3;
                for (int s = 0; s < 2; s++)
                {
                    Primitives.TriangleFacing(m,a+s,b+s,a+s+1,m.NormalAt(a+s));
                    Primitives.TriangleFacing(m,a+s+1,b+s,b+s+1,m.NormalAt(a+s+1));
                }
            }
            int last = first + (rows-2)*3;
            for (int s = 0; s < 2; s++)
                Primitives.TriangleFacing(m,last+s,tipIndex,last+s+1,m.NormalAt(last+s));
        }
        m.RecordLeaf(leafVertex,leafIndex,root,len);
    }

    private static void EmbercrownCompoundLeaf(MeshData m, Vec3 root, Vec3 dir, double length,
        double[] c1, double[] c2, Rng rng, bool apex = false)
    {
        // 1. Rachis curve: arches horizontally with a graceful vault, leveling off and weeping at tip
        var p0 = root;
        var lateral = dir.Cross(Vec3.Up).Normalized();
        if (lateral.LengthSq < 1e-8) lateral = new Vec3(1, 0, 0);
        double wander = rng.Range(-0.035, 0.035) * length;
        double lift = rng.Range(0.033, 0.057);
        double droop = rng.Range(0.055, 0.085);
        var p1 = root + dir * (length * 0.29) + Vec3.Up * lift + lateral * wander;
        var p2 = root + dir * (length * 0.68) - Vec3.Up * (droop * .18) - lateral * wander * .35;
        var p3 = root + dir * length - Vec3.Up * droop + lateral * wander * .55;

        // The tube and its leaflet attachment sites follow THE SAME cubic.
        // Previously the tube was only four straight stations while leaflets
        // occupied the separate BÃƒÂ©zier centreline, causing visible detached tips.
        Vec3 Rachis(double t)
        {
            double u = 1 - t;
            return p0 * (u*u*u) + p1 * (3*u*u*t) + p2 * (3*u*t*t) + p3 * (t*t*t);
        }
        Vec3 RachisTangent(double t)
        {
            double u=1-t;
            return ((p1-p0)*(3*u*u)+(p2-p1)*(6*u*t)+(p3-p2)*(3*t*t)).Normalized();
        }
        int stations = m.FloraDetailLevel switch {2=>4,1=>5,_=>5};
        var rachisPath = Enumerable.Range(0,stations+1).Select(i=>Rachis(i/(double)stations)).ToArray();
        var rachisRadii = Enumerable.Range(0,stations+1).Select(i=>
            MathD.Lerp(.0042,.0014,i/(double)stations)).ToArray();
        var petioleCol = Primitives.Mix(WoodyBarkLight, c1, 0.45);
        Primitives.Tube(m, rachisPath, rachisRadii, 6,
            (i, v) => (petioleCol, 1, i / (double)stations, v, 0, 0));

        // 2. Dense leaflet pairs along rachis: 11 pairs closely spaced along the stem
        int pairs = 11;
        for (int j = 0; j < pairs; j++)
        {
            double t = 0.12 + 0.82 * j / (pairs - 1);
            var pos = Rachis(t);
            var fwd = RachisTangent(t);
            var up = Vec3.Up;
            var side = fwd.Cross(up).Normalized();
            var rachisNorm = side.Cross(fwd).Normalized();

            // Broad, dense leaflets that overlap their neighbors
            double span = length * (0.28 + 0.18 * Math.Sin(Math.PI * (t - 0.06) / 0.90));
            double width = span * 0.48;

            foreach (double sgn in new[] { -1.0, 1.0 })
            {
                double sideComp = (0.84 + rng.Range(-0.07, 0.07)) * sgn;
                double fwdComp = 0.48 + rng.Range(-0.08, 0.08);
                double normComp = 0.18 + rng.Range(-0.09, 0.09);
                var lDir = (side * sideComp + fwd * fwdComp + rachisNorm * normComp).Normalized();
                var baseSide = lDir.Cross(rachisNorm).Normalized();
                double roll = rng.Range(-0.15, 0.15);
                var lSide = Axis.RotateAround(baseSide, lDir, roll);
                var lNorm = lSide.Cross(lDir).Normalized();

                EmbercrownLeaflet(m, pos, lDir, lSide, lNorm, span, width, c1, c2, rng);
            }
        }

        // Terminal leaflet at tip
        var termD0 = RachisTangent(1);
        var termSide0 = termD0.Cross(Vec3.Up).Normalized();
        if (termSide0.LengthSq < 1e-6) termSide0 = new Vec3(1, 0, 0);
        var termNorm0 = termSide0.Cross(termD0).Normalized();
        var termD = (termD0 + termSide0 * rng.Range(-0.07, 0.07) + termNorm0 * rng.Range(-0.07, 0.07)).Normalized();
        var termSide = Axis.RotateAround(termSide0, termD, rng.Range(-0.12, 0.12)).Normalized();
        var termNorm = termSide.Cross(termD).Normalized();
        double termSpan = length * 0.28;
        double termWidth = termSpan * 0.48;
        EmbercrownLeaflet(m, p3, termD, termSide, termNorm, termSpan, termWidth, c1, c2, rng);
        // A single terminal blade on each top-tier umbrella frond remains an
        // indispensable silhouette organ even if distant-budget pruning removes
        // most small lateral pinnae. This is an LOD guarantee, not extra leaves.
        if (apex && m.LeafSpans.Count > 0)
        {
            var terminal = m.LeafSpans[^1];
            m.EssentialSpans.Add(terminal);
        }
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

        int tiers = 7;
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

                // These hair-thin pedicels do not merit a six-sided tube:
                // a shaded double-sided ribbon saves thousands of triangles
                // while retaining the visible connection from spike to fruit.
                var stemDir = (bPos - tierCenter).Normalized();
                var offset = stemDir.Cross(Vec3.Up);
                if (offset.LengthSq < 1e-8) offset = stemDir.Cross(new Vec3(1,0,0));
                offset = offset.Normalized() * .0016;
                var face = stemDir.Cross(offset).Normalized();
                int pv = m.AddVertex(tierCenter - offset,face,spindleCol,1,0,0,0,0);
                m.AddVertex(tierCenter + offset,face,spindleCol,1,0,1,0,0);
                m.AddVertex(bPos - offset*.7,face,spindleCol,1,1,0,0,0);
                m.AddVertex(bPos + offset*.7,face,spindleCol,1,1,1,0,0);
                Primitives.TriangleFacing(m,pv,pv+2,pv+1,face);
                Primitives.TriangleFacing(m,pv+1,pv+2,pv+3,face);

                EmbercrownBerry(m, bPos, bR, rng);
            }
        }

        EmbercrownBerry(m, topPos + axis * 0.008, 0.010, rng);
    }

    // Interpolate woody canes through their anatomical landmarks, keeping each
    // original station (and its branch/leaf attachment) on the resulting curve.
    // Gentle nonuniform Hermite curvature replaces hard polyline elbows.
    private static void EmbercrownSmoothCane(MeshData m, IReadOnlyList<Vec3> anchors,
        IReadOnlyList<double> radii, double[] bark, int segments = 7)
    {
        const int subdivisions = 4;
        var path = new List<Vec3>();
        var smoothRadii = new List<double>();
        for (int i = 0; i < anchors.Count - 1; i++)
        {
            var p0 = anchors[Math.Max(0, i - 1)];
            var p1 = anchors[i];
            var p2 = anchors[i + 1];
            var p3 = anchors[Math.Min(anchors.Count - 1, i + 2)];
            var tangent0 = (p2 - p0) * .5;
            var tangent1 = (p3 - p1) * .5;
            for (int j = 0; j < subdivisions; j++)
            {
                double t = j/(double)subdivisions, t2=t*t, t3=t2*t;
                path.Add(p1 * (2*t3-3*t2+1) + tangent0*(t3-2*t2+t)
                    + p2*(-2*t3+3*t2)+tangent1*(t3-t2));
                smoothRadii.Add(MathD.Lerp(radii[i],radii[i+1],t));
            }
        }
        path.Add(anchors[^1]); smoothRadii.Add(radii[^1]);
        Primitives.Tube(m, path, smoothRadii, segments,
            (i,v)=>(bark,1,i/(double)(path.Count-1),v,2,0));
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
        m.PreserveUpperCanopyOnLod = true;
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
                EmbercrownSmoothCane(m, new[] {root, mid, top},
                    new[] {.016,.012,.008}, barkCol, 6);

                // 5 dense juvenile compound leaves
                for (int l = 0; l < 5; l++)
                {
                    double la = a + (l - 2.0) * 0.75 + rng.Range(-0.12, 0.12);
                    var lDir = (new Vec3(Math.Cos(la), 0.12, Math.Sin(la))).Normalized();
                    EmbercrownCompoundLeaf(m, top, lDir, rng.Range(0.26, 0.34), c1, c2, rng);
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
                EmbercrownCompoundLeaf(m, caneTop - Vec3.Up * 0.012, lDir, rng.Range(0.42, 0.52), c1, c2, rng, apex: true);
            }

            // Lower tier: 5 slightly shorter descending/weeping compound leaves staggered between upper leaves
            int lowerCount = 5;
            for (int l = 0; l < lowerCount; l++)
            {
                double la = baseAngle + (l + 0.5) * (Math.PI * 2.0 / lowerCount) + rng.Range(-0.12, 0.12);
                var lDir = (new Vec3(Math.Cos(la) * 0.85, -0.22, Math.Sin(la) * 0.85)).Normalized();
                EmbercrownCompoundLeaf(m, caneTop - Vec3.Up * 0.035, lDir, rng.Range(0.35, 0.44), c1, c2, rng, apex: true);
            }
        }

        // Central cane (taller, upright leader)
        {
            double cHeight = rng.Range(0.96, 1.08);
            var cRoot = new Vec3(rng.Range(-0.015, 0.015), 0.02, rng.Range(-0.015, 0.015));
            var cMid1 = cRoot + new Vec3(rng.Range(-0.03, 0.03), cHeight * 0.35, rng.Range(-0.03, 0.03));
            var cMid2 = cRoot + new Vec3(rng.Range(-0.04, 0.04), cHeight * 0.70, rng.Range(-0.04, 0.04));
            var cTop = cRoot + new Vec3(rng.Range(-0.02, 0.02), cHeight, rng.Range(-0.02, 0.02));
            EmbercrownSmoothCane(m, new[] {cRoot,cMid1,cMid2,cTop},
                new[] {.032,.025,.018,.012},barkCol,7);

            // Mid-cane leaves on central cane
            for (int mLeaf = 0; mLeaf < 3; mLeaf++)
            {
                double mt = 0.42 + mLeaf * 0.18;
                var mPos = Vec3.Lerp(cMid1, cMid2, (mt - 0.35) / 0.35);
                double ma = phase + mLeaf * 2.1;
                var mDir = (new Vec3(Math.Cos(ma), rng.Range(-0.06, 0.04), Math.Sin(ma))).Normalized();
                EmbercrownCompoundLeaf(m, mPos, mDir, rng.Range(0.32, 0.42), c1, c2, rng);
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

            EmbercrownSmoothCane(m,new[]{s0,s1,s2,s3,s4},
                new[]{.028,.024,.020,.016,.011},barkCol,7);

            // Mid-cane foliage: 4 alternate compound leaves along upright cane section (clothes the stem in lush green)
            for (int mid = 0; mid < 3; mid++)
            {
                double midT = 0.35 + mid * 0.195;
                var midPos = midT < 0.50
                    ? Vec3.Lerp(s2, s3, (midT - 0.32) / 0.18)
                    : Vec3.Lerp(s3, s4, (midT - 0.50) / 0.32);
                double midAngle = ang + (mid % 2 == 0 ? 0.70 : -0.70) + rng.Range(-0.10, 0.10);
                var midDir = (new Vec3(Math.Cos(midAngle), rng.Range(-0.08, 0.04), Math.Sin(midAngle))).Normalized();
                EmbercrownCompoundLeaf(m, midPos, midDir, rng.Range(0.32, 0.42), c1, c2, rng);
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

            // The outer section of each shoot becomes a long compound leaf carrying close-set opposing leaflets
            int leafletPairs = 20;
            for (int j = 0; j < leafletPairs; j++)
            {
                double t = Math.Clamp(0.45 + (double)j / (leafletPairs - 1) * 0.53 + rng.Range(-0.003, 0.003), 0.45, 0.98);
                double segment = t * (path.Length - 1);
                int index = Math.Min(path.Length - 2, (int)segment);
                var p = path[index] + (path[index + 1] - path[index]) * (segment - index);
                var stemDir = (path[index + 1] - path[index]).Normalized();
                for (int n = 0; n < 2; n++)
                {
                    double sign = n == 0 ? -1.0 : 1.0;
                    var outDir = (tangent * sign * rng.Range(0.82, 0.96)
                        + stemDir * rng.Range(0.28, 0.45)
                        + new Vec3(0, rng.Range(0.04, 0.14), 0)).Normalized();
                    double petioleLength = rng.Range(0.014, 0.028);
                    var bladeRoot = p + outDir * petioleLength;
                    double fullness = 0.60 + 0.40 * Math.Sin(Math.PI * (j + 0.5) / leafletPairs);
                    double len = rng.Range(0.32, 0.44) * fullness;
                    var sideFlat = Vec3.Up.Cross(outDir).Normalized();
                    var bladeTip = bladeRoot + outDir * len + sideFlat * (len * rng.Range(-0.06, 0.06)) + new Vec3(0, len * rng.Range(-0.05, 0.06), 0);
                    var bladeAxis = (bladeTip - bladeRoot).Normalized();
                    var side = Axis.RotateAround(sideFlat, bladeAxis, rng.Range(-0.18, 0.18)).Normalized();
                    Primitives.Tube(m, new[] { p, bladeRoot }, new[] { 0.0028, 0.0015 }, 6,
                        (i, v) => (youngBark, 1, i, v, 2, 0));
                    int leafVertex = m.VertexCount, leafIndex = m.Indices.Count;
                    Primitives.CurvedLeaf(m, bladeRoot, bladeTip, side, len * rng.Range(0.18, 0.25),
                        Primitives.Mix(c1, c2, 0.18), Primitives.Mix(c1, c2, rng.Range(0.48, 0.72)),
                        camber: len * rng.Range(0.045, 0.085), longitudinal: 4, asymmetry: rng.Range(-0.12, 0.12));
                    m.RecordLeafSpan(leafVertex, leafIndex, bladeRoot, len);
                    if (!m.FloraDetailLevel.HasValue)
                        Primitives.Tube(m, new[] { bladeRoot + new Vec3(0, 0.001, 0), bladeRoot + (bladeTip - bladeRoot) * 0.55 + new Vec3(0, len * 0.025, 0) },
                            new[] { 0.0018, 0.0006 }, 6, (i, v) => (vein, 1, i, v, 0, 0));
                }
            }
            var terminalDir = ((path[6] - path[5]).Normalized() + radial * rng.Range(-0.06, 0.06) + tangent * rng.Range(-0.06, 0.06)).Normalized();
            var termSide = Axis.RotateAround(Vec3.Up.Cross(terminalDir).Normalized(), terminalDir, rng.Range(-0.15, 0.15));
            double termLen = 0.26;
            var termTip = path[6] + terminalDir * termLen;
            int termVertex = m.VertexCount, termIndex = m.Indices.Count;
            Primitives.CurvedLeaf(m, path[6], termTip, termSide, 0.045,
                Primitives.Mix(c1, c2, 0.18), Primitives.Mix(c1, c2, 0.60), camber: 0.015, longitudinal: 4);
            m.RecordLeafSpan(termVertex, termIndex, path[6], termLen);

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
    /// - Terminal shoots carry broad, near-orbicular cordate leaves held nearly horizontal,
    ///   like rhubarb-scaled canopy shelves with a small younger interior tier.
    /// - Original waxy two-tone shader and deep dark shade-green pigment retained.
    /// - Dense pendant campanulate flower racemes (11 mature bells per cluster) hanging underneath
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

        int detail = m.FloraDetailLevel ?? 0;

        // Short terminal shoots carry a loose spiral of mature and emerging leaves.
        // Keyed variation stays independent of the structural RNG and of the detail tier.
        int whorlOrdinal = 0;
        void ShadebellLeafWhorl(Vec3 tipPos, Vec3 outwardDir, double whorlRadius, bool hasFlowerCluster)
        {
            var fwd = outwardDir.Normalized();
            var up = Vec3.Up;
            var side = fwd.Cross(up).Normalized();
            if (side.LengthSq < 1e-6) side = new Vec3(1, 0, 0);

            ulong whorlSeed = Rng.Mix(seed, 0x53484F4F54UL + (ulong)whorlOrdinal++);
            double Variation(int leaf, ulong channel) => Rng.HashUnit(whorlSeed, channel, (ulong)leaf);
            double phase = Variation(0, 11) * Math.PI * 2;
            var shootAxis = (fwd * 0.38 + up).Normalized();
            var shootEnd = tipPos + shootAxis * 0.085;
            Primitives.Tube(m, new[] { tipPos, shootEnd }, new[] { 0.006, 0.0025 },
                detail == 0 ? 5 : 3, (i, v) => (petioleCol, 1, i, v, 0, 0));

            // Broad, nearly horizontal, near-orbicular leaves create rhubarb-like shade shelves.
            // Fewer laminae than the former lanceolate rosette; each carries considerably more area.
            // Preserve the same attachments and RNG stream across material detail tiers.
            int outerCount = juvenile ? 4 : 5;
            for (int li = 0; li < outerCount; li++)
            {
                double leafAngle = li * 2.399963 + phase + (Variation(li, 13) - 0.5) * 0.18;
                double pitchVar = (Variation(li, 17) - 0.5) * 0.20;
                var lDir = (side * Math.Cos(leafAngle) + fwd * Math.Sin(leafAngle) * 0.95
                    + up * (0.15 + pitchVar)).Normalized();
                double lLen = whorlRadius * (0.93 + Variation(li, 19) * 0.35);
                double lWidth = lLen * (1.13 + Variation(li, 23) * 0.24);
                ulong leafSeed = Rng.Mix(whorlSeed, (ulong)(li * 317 + 5));
                var leafAttach = tipPos + shootAxis * (0.050 * li / Math.Max(1, outerCount - 1));
                double roll = (Variation(li, 67) - 0.5) * 0.16;

                Broadleaf.BuildLeaf(m, leafAttach, lDir, lLen, lWidth, BroadleafOutline.OrbicularCordate, leafSeed,
                    c1, c2, age: 0.88 + Variation(li, 29) * 0.11, camber: 0.045 + Variation(li, 31) * 0.055,
                    midribFold: 0.021, wavyMargin: 0.017, wavyFrequency: 2.2 + Variation(li, 37) * 0.9,
                    tipCurl: 0.014 + Variation(li, 41) * 0.047, petioleLength: lLen * 0.23, petioleRadius: 0.0055,
                    petioleKink: 0.15 + Variation(li, 43) * 0.085, asymmetry: (Variation(li, 47) - 0.5) * 0.12,
                    roll: roll, undersideColor: colUnder, detailOverride: Math.Max(1, detail));
            }

            // Tier 2: Inner overlapping rosette leaves (5 leaves) filling the center with younger foliage
            // Pruned for distant LOD tiers to reduce build time and polygon density
            if (!juvenile)
            {
                int innerCount = 2;
                for (int li = 0; li < innerCount; li++)
                {
                    double leafAngle = li * 2.399963 + phase + 0.65 + (Variation(li, 51) - 0.5) * 0.18;
                    double pitchVar = (Variation(li, 53) - 0.5) * 0.16;
                    var lDir = (side * Math.Cos(leafAngle) + fwd * Math.Sin(leafAngle) * 0.94 + up * (0.19 + pitchVar)).Normalized();
                    double lLen = whorlRadius * (0.66 + Variation(li, 57) * 0.19);
                    double lWidth = lLen * (1.06 + Variation(li, 59) * 0.20);
                    ulong leafSeed = Rng.Mix(whorlSeed, (ulong)(li * 523 + 47));
                    double roll = (Variation(li, 71) - 0.5) * 0.14;

                    Broadleaf.BuildLeaf(m, tipPos + shootAxis * (0.055 + 0.0075 * li), lDir, lLen, lWidth, BroadleafOutline.OrbicularCordate, leafSeed,
                        c1, c2, age: 0.81, camber: 0.075, midribFold: 0.023, wavyMargin: 0.015, wavyFrequency: 2.8,
                        tipCurl: 0.025 + Variation(li, 61) * 0.025, petioleLength: lLen * 0.23, petioleRadius: 0.0040, petioleKink: 0.23,
                        roll: roll, undersideColor: colUnder, detailOverride: Math.Max(1, detail));
                }
            }

            // Hanging nodding campanulate bell flower raceme beneath the foliage whorl
            // Pruned completely at LOD 2; simplified at LOD 1 (fewer bells, low-poly cups, simplified pedicel tubes)
            if (hasFlowerCluster && detail < 2)
            {
                var racemeBase = tipPos - Vec3.Up * 0.045;
                int bellCount = detail == 0 ? (juvenile ? 5 : 11) : (juvenile ? 3 : 6);
                double racemeLen = 0.175 * (juvenile ? 0.65 : 1.0);

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
                int rSides = detail == 0 ? 3 : 3;
                Primitives.Tube(m, rPts, rRadii, rSides, (i, v) => (pedicelCol, 1.0, i / (double)bellCount, v, 0, 0));

                for (int bi = 1; bi <= bellCount; bi++)
                {
                    var pedStart = rPts[bi];
                    double bAng = bi * 2.39996;
                    var bSide = new Vec3(Math.Cos(bAng), 0, Math.Sin(bAng));
                    var pedEnd = pedStart + bSide * (0.050 + (bi % 3) * 0.006) - Vec3.Up * (0.027 + (bi % 2) * 0.009);

                    // Delicate curved pedicel
                    if (detail == 0)
                    {
                        // Pedicels are slender enough to read as smoothly bent lines
                        // without two full circumferential spans per blossom.
                        Primitives.Tube(m, new[] { pedStart, pedEnd },
                            new[] { 0.0019, 0.0012 }, 4,
                            (i, v) => (pedicelCol, 1.0, i, v, 0, 0));
                    }
                    else
                    {
                        Primitives.Tube(m, new[] { pedStart, pedEnd },
                            new[] { 0.0020, 0.0014 }, 3,
                            (i, v) => (pedicelCol, 1.0, i / 1.0, v, 0, 0));
                    }

                    // Nodding campanulate bell blossom (hollow cup with flared scalloped rim)
                    int bellStart = m.VertexCount;
                    int bRings = detail == 0 ? 2 : 2;
                    int bSides = detail == 0 ? 7 : 5;
                    double bellH = 0.039;
                    double bellR = 0.021;

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

            var cTop = radial * caneReach + new Vec3(0, caneH, 0);
            var tangent = new Vec3(-radial.Z, 0, radial.X);
            ulong bendSeed = Rng.Mix(seed, 0x53484144454UL + (ulong)k);
            Vec3 CanePoint(double t)
            {
                double bend = Noise.Gradient(bendSeed, t * 2.3, 1.7)
                    * Math.Sin(Math.PI * t) * 0.09;
                return root + radial * (caneReach * Math.Pow(t, 1.18))
                    + tangent * bend + Vec3.Up * (caneH * t);
            }
            // Include every branch attachment as an actual spine station. The same
            // centreline serves all detail tiers, so leaves stay attached during LOD changes.
            double[] stations = { 0, 0.14, 0.28, 0.36, 0.50, 0.62, 0.68, 0.82, 0.88, 1 };
            var canePath = stations.Select(CanePoint).ToArray();
            var caneRadii = stations.Select(t => 0.009 + 0.029 * Math.Pow(1 - t, 1.15)
                + 0.012 * Math.Exp(-t * 14)).ToArray();
            Wood(m, canePath, caneRadii, detail == 0 ? 7 : (detail == 1 ? 5 : 4));

            // 3 horizontal tiered branch shelves per cane
            int tiers = juvenile ? 2 : 3;
            for (int tr = 0; tr < tiers; tr++)
            {
                double tt = 0.36 + 0.52 * tr / Math.Max(1, tiers - 1);
                var tierAttach = CanePoint(tt);
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
                WoodyCurve(m, new Vec3(0, 0, 0), cMid, cTop, 0.022, detail == 0 ? 4 : 3);
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
                CinematicIsopod(m);
                break;
            case "beetle": CinematicBeetle(m); break;
            case "silverfish": CinematicSilverfish(m); break;
            case "slug": CinematicSlug(m); break;
            case "millipede": Millipede(m); break;
            case "moth": Moth(m); break;
            case "toad": Toad(m, visualSeed); break;
            case "salamander": CinematicSalamander(m); break;
            case "worm": CinematicWorm(m); break;
            case "harvestman": CinematicHarvestman(m); break;
            case "aquatic_larva": AquaticLarva(m); break;
            case "snail": Snail(m); break;
            case "midge": Midge(m); break;
            default: Primitives.Ellipsoid(m, new Vec3(0, 0.2, 0), new Vec3(0.5, 0.2, 0.2), 8, 10, (a, b) => (Body, 0, a, b, 1, 0)); break;
        }
        if (visualSeed != 0) ApplyFaunaMorph(m, model, visualSeed);
        FinalizeFaunaArticulation(m);
        return m;
    }

    /// <summary>Rolled-up pose for conglobating species (pill bug ball); null for species that never curl.</summary>
    public static MeshData? FaunaCurled(FaunaSpeciesDef sp, ulong visualSeed = 0)
    {
        var model = !string.IsNullOrEmpty(sp.Model) ? sp.Model : sp.Id;
        if (model is not ("isopod" or "pill_bug")) return null;
        var m = new MeshData();
        const double cy = 0.22, ringR = 0.175;

        if (model == "isopod")
        {
            CinematicIsopodCurled(m);
            if (visualSeed != 0) ApplyFaunaMorph(m, model, visualSeed);
            return m;
        }

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
        if (model is "isopod" or "silverfish" or "salamander" or "slug" or "worm" or "harvestman")
        {
            // Inherited arch/leg proportions can lower the terminal claws
            // beneath the neutral support plane. Translate the whole mesh;
            // root-relative offsets remain valid for every attached joint.
            double lift = .002 - m.Bounds().Min.Y;
            if (Math.Abs(lift) > 1e-8)
                for (int i = 0; i < m.VertexCount; i++) m.Positions[i * 3 + 1] += (float)lift;
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
        // Fractional role-1 data is root-to-toe weight. A planted toe gets the full calibrated
        // stroke, while the socket stays attached regardless of limb length/genetics.
        Primitives.Tube(m, p, r, segs, (i, v) => (Body, 1, 0.5, v, region,
            animationRole == 1 ? 1 + 0.25 * i / (p.Count - 1) : animationRole));
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
        Appendage(m, new[] { manuAttach, manuMid, manuFork }, new[] { 0.016, 0.013, 0.010 }, 6, 4, 5);

        // Paired dentes with hooked mucro tips extending forward under belly
        foreach (double z in new[] { -1.0, 1.0 })
        {
            var densAttach = new Vec3(-0.17, 0.038, 0.008 * z);
            var densMid = new Vec3(-0.10, 0.035, 0.016 * z);
            var densApex = new Vec3(-0.04, 0.032, 0.016 * z);
            var mucroTip = new Vec3(-0.015, 0.038, 0.010 * z); // hooked tip
            Appendage(m, new[] { densAttach, densMid, densApex, mucroTip }, new[] { 0.009, 0.007, 0.005, 0.003 }, 6, 4, 5);
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
            Appendage(m, new[] { new Vec3(-0.37, 0.07, 0.01 * z), new Vec3(-0.6, 0.075, 0.1 * z), new Vec3(-0.8, 0.07, 0.16 * z) }, new[] { 0.008, 0.005, 0.003 }, 3, 4, 3);
        }
        Appendage(m, new[] { new Vec3(-0.37, 0.07, 0), new Vec3(-0.62, 0.08, 0), new Vec3(-0.85, 0.075, 0) }, new[] { 0.008, 0.005, 0.003 }, 3, 4, 3);
    }







    private static void Toad(MeshData m, ulong visualSeed)
    {
        // +X is forward. A low pear-shaped trunk, broad jaw and folded muscular legs.
        BodySegment(m, new Vec3(-0.09, 0.19, 0), new Vec3(0.32, 0.18, 0.255), 18, 28,
            (a, b) => Region(a, b, Math.Cos(b * Math.Tau) < -0.45 ? 3 : 1));
        BodySegment(m, new Vec3(0.22, 0.225, 0), new Vec3(0.225, 0.125, 0.235), 16, 28,
            (a, b) => Region(a, b, Math.Cos(b * Math.Tau) < -0.55 ? 3 : 1));
        BodySegment(m, new Vec3(0.26, 0.15, 0), new Vec3(0.184, 0.043, 0.199), 12, 24,
            (a, b) => Region(a, b, 3));
        BodySegment(m, new Vec3(0.19, 0.088, 0), new Vec3(0.125, 0.055, 0.125), 12, 20,
            (a, b) => Region(a, b, 8)); // soft throat, independent respiration

        // A continuous thin mouth line follows the leading edge of the broad jaw.
        var mouth = new List<Vec3>();
        for (int i = 0; i <= 24; i++)
        {
            double t = -1.32 + 2.64 * i / 24;
            mouth.Add(new Vec3(0.235 + 0.194 * Math.Cos(t), 0.158 - 0.012 * Math.Cos(t), 0.211 * Math.Sin(t)));
        }
        Primitives.Tube(m, mouth, mouth.Select(_ => 0.0035).ToArray(), 6, (i, v) => Region(i / 24.0, v, 7));

        foreach (double side in new[] { -1.0, 1.0 })
        {
            // Parotoid glands, brow ridges and inset bronze eyes with horizontal pupils.
            BodySegment(m, new Vec3(0.075, 0.279, 0.185 * side), new Vec3(0.115, 0.029, 0.050), 10, 16,
                (a, b) => Region(a, b, 1));
            BodySegment(m, new Vec3(0.285, 0.313, 0.155 * side), new Vec3(0.086, 0.073, 0.072), 12, 20,
                (a, b) => Region(a, b, 1));
            BodySegment(m, new Vec3(0.305, 0.332, 0.193 * side), new Vec3(0.056, 0.044, 0.042), 12, 24,
                (a, b) => Region(a, b, 2));
            BodySegment(m, new Vec3(0.312, 0.336, 0.223 * side), new Vec3(0.046, 0.034, 0.018), 12, 24,
                (a, b) => Region(a, b, 5));
            BodySegment(m, new Vec3(0.312, 0.336, 0.240 * side), new Vec3(0.034, 0.007, 0.007), 10, 20,
                (a, b) => Region(a, b, 6));
            BodySegment(m, new Vec3(0.405, 0.249, 0.071 * side), new Vec3(0.008, 0.005, 0.009), 6, 10,
                (a, b) => Region(a, b, 7)); // nostril

            var hip = new Vec3(-0.20, 0.18, 0.18 * side);
            var ankle = new Vec3(-0.17, 0.032, 0.39 * side);
            var wrist = new Vec3(0.31, 0.024, 0.285 * side);
            var shoulder = new Vec3(0.145, 0.17, 0.177 * side);
            Limb(hip, new[] { hip, new Vec3(-0.34, 0.14, 0.31 * side), new Vec3(-0.35, 0.075, 0.37 * side), ankle },
                new[] { 0.090, 0.105, 0.057, 0.026 });
            Limb(shoulder, new[] { shoulder, new Vec3(0.16, 0.082, 0.255 * side), wrist },
                new[] { 0.049, 0.042, 0.024 });
            // Every digit shares its parent leg's attachment, so toes cannot detach in flight.
            for (int digit = 0; digit < 5; digit++)
            {
                double spread = (digit - 2) * 0.023;
                double len = 0.105 - Math.Abs(digit - 2) * 0.020;
                Limb(hip, new[] { ankle, ankle + new Vec3(0.045, -0.012, spread * side),
                    ankle + new Vec3(len, -0.019, spread * 1.8 * side) }, new[] { 0.014, 0.010, 0.0045 });
            }
            for (int digit = 0; digit < 4; digit++)
            {
                double spread = (digit - 1.5) * 0.025;
                double len = 0.082 - Math.Abs(digit - 1.5) * 0.018;
                Limb(shoulder, new[] { wrist, wrist + new Vec3(0.033, -0.010, spread * side),
                    wrist + new Vec3(len, -0.016, spread * 1.5 * side) }, new[] { 0.012, 0.008, 0.0035 });
            }
        }
        // Low, irregular dermal bumps supply silhouette detail as well as shader microrelief.
        var rng = Rng.Keyed(visualSeed, "fauna.toad.warts", 0);
        for (int i = 0; i < 78; i++)
        {
            double x = rng.Range(-0.35, 0.16), angle = rng.Range(-1.18, 1.18);
            double ring = Math.Sqrt(Math.Max(0, 1 - Math.Pow((x + 0.09) / 0.32, 2)));
            var c = new Vec3(x, 0.19 + 0.178 * ring * Math.Cos(angle), 0.253 * ring * Math.Sin(angle));
            double r = rng.Range(0.008, 0.017);
            BodySegment(m, c, new Vec3(r, r * 0.55, r), 5, 8, (a, b) => Region(a, b, 1));
        }

        void Limb(Vec3 root, Vec3[] path, double[] radii)
        {
            var curve = new List<Vec3>();
            var thickness = new List<double>();
            for (int k = 0; k < path.Length - 1; k++)
            {
                var a = path[Math.Max(0, k - 1)]; var b = path[k];
                var c = path[k + 1]; var d = path[Math.Min(path.Length - 1, k + 2)];
                for (int j = 0; j < 4; j++)
                {
                    double t = j / 4.0, tt = t * t, ttt = tt * t;
                    curve.Add((b * 2 + (c - a) * t + (a * 2 - b * 5 + c * 4 - d) * tt
                        + (-a + b * 3 - c * 3 + d) * ttt) * 0.5);
                    thickness.Add(MathD.Lerp(radii[k], radii[k + 1], t));
                }
            }
            curve.Add(path[^1]); thickness.Add(radii[^1]);
            int first = m.VertexCount;
            Appendage(m, curve, thickness, 12);
            for (int i = first; i < m.VertexCount; i++)
            {
                var off = m.Position(i) - root;
                m.SetColor(i, off.X, off.Y, off.Z, 1);
                // Digits move with the wrist/ankle at the full planted-foot stroke.
                // Main limbs keep the tube's root-to-tip weights (including anchored sockets).
                if (path[0] != root) m.UV2[i * 2 + 1] = 1.25f;
            }
        }
    }










    private static void Midge(MeshData m)
    {
        BodySegment(m,new Vec3(0.06,0.12,0),new Vec3(0.12,0.055,0.05),4,8,(a,b)=>Region(0.55,b,1));
        BodySegment(m,new Vec3(0.20,0.13,0),new Vec3(0.07,0.06,0.065),4,8,(a,b)=>Region(0.9,b,2));
        BodySegment(m,new Vec3(-0.14,0.115,0),new Vec3(0.15,0.035,0.035),4,8,(a,b)=>Region(0.25,b,a > 0.45 ? 9 : 1));
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
