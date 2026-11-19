using System;
using System.Collections.Generic;
using System.Linq;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>Authored woody architectures and support-aware climbing foliage.</summary>
public static class WoodyGraphSpecies
{
    // Each record controls topology as well as proportions: trunks, crown tiers, branching,
    // foliage placement, blade dimensions, and reproductive structures.
    private sealed record Form(string Id, string Counterpart, string Habit, int Trunks, int Tiers, int Branches, int Nodes,
        double CrownBase, double Spread, double Rise, double Droop, double LeafLength,
        double LeafWidth, int Arrangement, int Ornament, bool Needle = false, bool Climber = false, double Girth = 1.0);
    private static readonly Form[] Forms = {
        new("banksorrow","Salix babylonica","leaning riverside curtain",1,5,4,8,.30,1.35,.20,.92,.14,.11,0,0,Girth:1.1),
        new("bitterbine","Humulus lupulus","coarse twining cone vine",2,3,3,7,.10,.75,.80,.25,.12,.42,1,2,false,true),
        new("clinglace","Ficus pumila","overlapping clinging plates",1,3,2,9,.05,.80,.35,.10,.06,.35,0,0,false,true),
        new("cragparasol","Rhododendron catawbiense","crooked terminal umbrellas",1,3,3,5,.58,1.30,.22,.18,.24,.48,3,0),
        new("embercrown","Rhus typhina","ascending clonal lantern thicket",7,3,3,6,.22,.72,.82,.15,.16,.16,2,2,Girth:0.9),
        new("fenforge","Alnus glutinosa","wide leaning bank tree",4,5,4,6,.28,1.05,.45,.32,.13,.48,0,2,Girth:1.15),
        new("fenhook","Solanum dulcamara","hooked wetland scramble",3,3,2,6,.05,1.10,.35,.65,.09,.28,0,0,false,true),
        new("glowbriar","Rosa multiflora","arching luminous canes",6,4,2,5,.15,.90,.70,.50,.15,.30,1,1),
        new("greywatch","Juniperus communis","ascending needle sprays",3,5,4,8,.18,.80,.80,.12,.12,.07,2,2,true),
        new("hollowcrown","Quercus robur","pollarded veteran with compact shoots",1,3,5,7,.62,.90,.55,.12,.24,.55,0,0,Girth:1.8),
        new("hookthicket","Rubus fruticosus","interlaced rooting canes",8,3,3,7,.08,1.15,.60,.88,.09,.28,0,0,Girth:0.8),
        new("kinkcane","Phyllostachys nigra","tall bare canes with palmate tops",9,1,5,4,.85,.95,.90,.12,.22,.22,3,0,Girth:0.55),
        new("lacemantle","Cornus alternifolia","horizontal rotating branch shelves",1,5,3,7,.25,1.32,.10,.08,.09,.22,0,0),
        new("lanternbrush","Staphylea trifolia","bowed wetland lanterns",6,4,3,5,.16,.90,.70,.55,.14,.35,1,2),
        new("miremyrtle","Myrica gale","fine aromatic catkin shrub",7,3,3,8,.12,.85,.55,.30,.05,.16,2,2),
        new("mirespire","Picea mariana","narrow tiered conifer",1,8,5,8,.16,.55,.22,.15,.12,.25,2,2,true,Girth:0.9),
        new("moonbark","Betula pendula","airy high pioneer crown",3,5,4,5,.45,.95,.60,.28,.105,.48,0,0,Girth:0.8),
        new("nutglade","Corylus avellana","coppice with broad scalloped leaves",7,4,3,5,.20,.92,.65,.18,.11,.44,0,2),
        new("rainshield","Darmera peltata","open stems with broad leaf platforms",11,1,1,1,.98,1.05,.95,.04,.31,.48,3,0,Girth:0.75),
        new("shadebell","Asimina triloba","tiered broad leaves and pendant bells",5,4,3,5,.20,.90,.70,.25,.17,.40,1,1),
        new("shadewrap","Hedera helix","layered lobed trunk wrap",1,3,2,8,.05,.75,.50,.12,.10,.46,0,0,false,true),
        new("shiverleaf","Populus tremula","slender trembling round crown",3,5,4,5,.48,.85,.72,.18,.075,.68,0,0,Girth:0.85),
        new("silverfelt","Salix lanata","low dense felted mound",8,3,3,7,.08,1.00,.40,.42,.055,.32,0,2),
        new("spiralvine","Celastrus orbiculatus","narrow-leaved searching spiral",1,4,2,7,.05,1.25,.70,.15,.12,.14,0,0,false,true),
        new("splitcrown","Albizia julibrissin","separate high forked crowns",3,3,4,6,.58,1.25,.45,.15,.09,.18,0,0),
        new("thornmantle","Crataegus monogyna","spiny blossom thicket",6,4,3,6,.14,1.00,.58,.28,.065,.34,0,1),
        new("trumpetbind","Lonicera periclymenum","opposite leaves and hanging trumpets",2,4,2,6,.05,.80,.65,.20,.085,.34,1,1,false,true),
        new("vaultfern","Dicksonia antarctica","elevated arching divided fronds",1,1,14,10,.96,1.25,.22,.68,.095,.16,1,0,Girth:1.8),
        new("witchberry","Sorbus aucuparia","feathered spreading berry crown",1,5,4,7,.38,.95,.58,.22,.095,.19,0,2,Girth:1.8),
    };
    public static IReadOnlyList<GraphSpeciesInfo> Species { get; } = Array.AsReadOnly(Forms.Select(f =>
        new GraphSpeciesInfo(f.Id, f.Climber ? "climber" : "woody", f.Habit,
            f.Climber ? "Searches and follows available support with foliage along stems" : f.Id switch {
                "fenforge" => "Raised root cage and staggered, leafy branch shelves",
                "mirespire" => "Tapered leader with overlapping three-dimensional needle sprays",
                "moonbark" => "Clustered pale crooked stems, arching limbs and pendant leafy shoots",
                "hollowcrown" => "Hollow veteran bole supporting a broad scaffold crown",
                "witchberry" => "Thick low fork with pinnate leaf clusters and fruiting tips",
                "cragparasol" => "Single crooked trunk and a compact terminal leaf parasol",
                "greywatch" => "Low spreading limbs clothed in pale narrow needle whorls",
                "glowbriar" => "Low stool and runners, arching thorny canes and decussate foliage",
                "shadebell" => "Crooked stems, lateral leaf decks and pendant bell racemes",
                "vaultfern" => "Radial divided fronds and central coiling croziers above a caudex",
                _ => f.Habit }, f.Climber)).ToArray());
    public static PlanGenerator? Create(FloraSpeciesDef species)
    {
        var f = Array.Find(Forms, x => x.Id == species.Id);
        if (f == null) return null;
        if (f.Id == "glowbriar") return new GlowbriarGenerator();
        if (f.Id == "shadebell") return new ShadebellGenerator();
        return new WoodyGenerator(f, species);
    }
    // Perceptual roughness per woody species leaf: felted/hairy high, leathery/glossy low, ordinary broadleaf mid.
    private static float LeafRough(string id) => id switch
    {
        "silverfelt" => .86f, "bitterbine" => .78f, "embercrown" => .74f, "nutglade" => .72f, "hookthicket" => .70f,
        "miremyrtle" => .66f, "splitcrown" => .66f, "vaultfern" => .66f, "lacemantle" => .64f, "banksorrow" => .60f,
        "hollowcrown" => .62f, "lanternbrush" => .62f, "fenhook" => .62f, "witchberry" => .62f, "greywatch" => .60f,
        "trumpetbind" => .60f, "mirespire" => .58f, "moonbark" => .58f, "kinkcane" => .56f, "shiverleaf" => .56f,
        "spiralvine" => .54f, "thornmantle" => .54f, "clinglace" => .52f, "fenforge" => .52f, "cragparasol" => .45f,
        "rainshield" => .45f, "shadewrap" => .44f, _ => .62f
    };
    // Flowers/fruit: berries and drupes glossy, cones/catkins/felted heads dry.
    private static float OrnamentRough(string id) => id switch
    {
        "fenhook" => .22f, "thornmantle" => .24f, "lacemantle" => .26f, "hookthicket" => .26f, "shadewrap" => .28f, "spiralvine" => .30f,
        "greywatch" => .40f, "cragparasol" => .60f, "trumpetbind" => .60f, "hollowcrown" => .60f, "lanternbrush" => .68f,
        "kinkcane" => .70f, "nutglade" => .70f, "rainshield" => .70f, "splitcrown" => .72f, "bitterbine" => .74f, "miremyrtle" => .76f,
        "fenforge" => .76f, "embercrown" => .78f, "vaultfern" => .78f, "moonbark" => .78f, "shiverleaf" => .78f, "banksorrow" => .78f,
        "mirespire" => .80f, "silverfelt" => .80f, _ => .62f
    };
    private static ushort Leaf(PlanBuilder b, Form f)
    {
        bool wide = f.LeafWidth >= .35;
        bool serrated = f.Id is "hookthicket" or "thornmantle" or "nutglade" or "embercrown" or "lanternbrush" or "glowbriar";
        bool shield = f.Id == "rainshield";
        float midribCurl = f.Id switch {
            "rainshield" => .08f,
            "vaultfern" => .18f,
            "kinkcane" => .28f,
            "cragparasol" => .22f,
            _ => (float)Math.Clamp(.16 + f.Droop * .22, .16, .38)
        };
        float crossCurl = shield ? -.05f : wide ? .18f : .14f;
        float twist = shield ? .02f : f.Needle ? 0f : .10f;
        float edgeWave = shield ? .028f : wide ? .018f : .008f;
        return b.AddShape(new OrganShape {
            Name = f.Id + "_leaf", Type = OrganType.Leaf,
            SurfaceForm = f.Needle ? OrganSurfaceForm.Needle : OrganSurfaceForm.Lamina,
            // Position of maximum width, tip taper and margin are authored per identity.
            // The compiler normalizes peak width; f.LeafWidth supplies the aspect ratio.
            WidthProfile = PlantShapes.Profile(t => LeafOutline(f.Id, t)),
            MidribCurl = midribCurl, CrossCurl = crossCurl, Twist = twist,
            EdgeWave = edgeWave, EdgeWaveFreq = shield ? 6f : 4f,
            Serration = serrated ? (f.Id is "thornmantle" or "nutglade" ? .20f : .12f) : 0f,
            SerrationFreq = f.Id is "thornmantle" or "hollowcrown" ? 5f : 9f,
            ColorAdaxial = f.Id switch { "mirespire" => PlantShapes.Srgb(0.222,0.56,0.53), "greywatch" => PlantShapes.Srgb(.56,.68,.57), "cragparasol" => PlantShapes.Srgb(.91,.86,.60), _ => PlantShapes.Srgb(.13,.32,.14) },
            ColorAbaxial = f.Id switch { "mirespire" => PlantShapes.Srgb(.40,.70,.66), "greywatch" => PlantShapes.Srgb(.70,.76,.65), "cragparasol" => PlantShapes.Srgb(.98,.93,.72), _ => PlantShapes.Srgb(.28,.46,.24) },
            Roughness = LeafRough(f.Id), Transmission = .32f
        });
    }
    private static double LeafOutline(string id, double t)
    {
        // Profiles run from attachment to tip. Lobed climbing blades are assembled from
        // individual overlapping lobes in BuildClimber, as in their legacy geometry.
        double sine = Math.Max(0, Math.Sin(Math.PI * t));
        double shape = id switch {
            "banksorrow" => Math.Pow(sine, .8),
            "bitterbine" => Math.Pow(sine, .72) * (1.18 - .36 * t),
            "clinglace" => Math.Pow(sine, .78) * (1.20 - .40 * t),
            "cragparasol" => Math.Pow(sine, .68) * (.48 + 1.04 * t),
            "embercrown" => Math.Pow(sine, .95) * (1.23 - .46 * t),
            "fenforge" => Math.Pow(sine, .65) * (.72 + .56 * t),
            "fenhook" => Math.Pow(sine, .42) * (1.30 - .60 * t),
            "glowbriar" => Math.Pow(sine, .80) * (1.15 - .30 * t),
            "greywatch" => Math.Pow(1 - t, .70),
            "hollowcrown" => Math.Pow(sine, .66) * (.72 + .56 * t) * (1 - .70 * Math.Pow(Math.Sin(4 * Math.PI * t), 2)),
            "hookthicket" => Math.Pow(sine, .78) * (1.15 - .30 * t),
            "kinkcane" => Math.Pow(sine, 1.30) * (1.35 - .70 * t),
            "lacemantle" => Math.Pow(sine, .83) * (1.13 - .26 * t),
            "lanternbrush" => Math.Pow(sine, .82) * (1.22 - .44 * t),
            "miremyrtle" => Math.Pow(sine, .75) * (.66 + .68 * t),
            // Near-constant width needle with a short blunt base ramp and a rounded tip.
            "mirespire" => Math.Min(1, .55 + t * 6) * (t > .80 ? Math.Sqrt(Math.Max(0, 1 - Math.Pow((t - .80) / .20, 2))) : 1),
            "moonbark" => Math.Pow(sine, .90) * (1.40 - .80 * t),
            "nutglade" => Math.Pow(sine, .60) * (1.24 - .48 * t),
            "rainshield" => Math.Pow(sine, .38) * (1.12 - .24 * t),
            "shadebell" => Math.Pow(sine, .72) * (.67 + .66 * t),
            "shadewrap" => Math.Pow(sine, 1.08) * (1.22 - .44 * t),
            "shiverleaf" => Math.Pow(sine, .48) * (1.34 - .68 * t),
            "silverfelt" => Math.Pow(sine, .72) * (.60 + .80 * t),
            "spiralvine" => Math.Pow(sine, .90) * (1.32 - .64 * t),
            "splitcrown" => Math.Pow(sine, .72) * (.82 + .36 * t),
            "thornmantle" => Math.Pow(sine, .72) * (.68 + .64 * t) * (1 - .25 * Math.Pow(Math.Sin(3 * Math.PI * t), 2)),
            "trumpetbind" => Math.Pow(sine, .70) * (1.12 - .24 * t),
            "vaultfern" => Math.Pow(sine, .85) * (1.25 - .50 * t),
            "witchberry" => Math.Pow(sine, .90) * (1.20 - .40 * t),
            _ => sine
        };
        return .5 * Math.Max(0, shape);
    }
    private static ushort Ornament(PlanBuilder b, Form f) => b.AddShape(new OrganShape {
        Name = f.Id + "_fruit_flower", Type = f.Ornament == 1 ? OrganType.Flower : OrganType.Fruit,
        SurfaceForm = f.Ornament == 1 ? OrganSurfaceForm.Trumpet : OrganSurfaceForm.Revolved,
        WidthProfile = PlantShapes.Profile(t => f.Ornament == 1 ? .12 + .38 * Math.Pow(t, .7) : .5 * Math.Sin(Math.PI * t)),
        LobeCount = f.Ornament == 1 ? 5 : 0, LobeDepth = .18f,
        ColorAdaxial = PlantShapes.Srgb(0.83,0.544,0.286), ColorAbaxial = PlantShapes.Srgb(0.65,0.287,0.215), Roughness=OrnamentRough(f.Id)
    });
    private sealed class WoodyGenerator : PlanGenerator
    {
        private readonly Form f; private readonly double height, radius;
        public WoodyGenerator(Form form, FloraSpeciesDef s) { f=form; height=s.Height > 0 ? s.Height : 1; radius=s.RadiusAtMax > 0 ? s.RadiusAtMax : height*.5; }
        public override string SpeciesId => f.Id;
        /// <summary>Unit horizontal side vector of an axis direction (falls back to any perpendicular).</summary>
        private static Vec3 SideOf(Vec3 tan) => PlantMath.SafeNormal(tan.Cross(Vec3.Up), PlantMath.Perpendicular(tan));

        /// <summary>
        /// Places leaf nodes along part of an axis, oriented relative to the local tangent so foliage follows
        /// the curve of its shoot. Optionally adds short curved petioles so blades sit naturally off the twig.
        /// </summary>
        private void Foliage(PlanBuilder b, ushort leaf, int axis, int nodes, double t0, double t1, double emerge,
            ref double phyl, double petioleLen = 0.0, double leafScale = 1.0, int perOverride = 0)
        {
            int per = perOverride > 0 ? perOverride : f.Arrangement == 1 ? 2 : f.Arrangement == 3 ? 3 : 1;
            for (int n = 0; n < nodes; n++)
            {
                double nt = Math.Clamp(t0 + (t1 - t0) * (n + .5) / nodes + b.Range(-.025, .025), .04, .995);
                var tan = b.TangentAt(axis, nt);
                var dorsal = PlantMath.SafeNormal(Vec3.Up - tan * Vec3.Up.Dot(tan), PlantMath.Perpendicular(tan));
                var lateral = tan.Cross(dorsal);
                phyl += f.Arrangement switch { 1 => Math.PI / 2, 2 => 2.39996, 3 => 2.39996, _ => Math.PI } + b.Range(-.25, .25);
                double tipScale = 1 - .25 * (nt - t0) / Math.Max(.05, t1 - t0);
                for (int q = 0; q < per; q++)
                {
                    double ang = phyl + q * Math.Tau / per;
                    var radial = lateral * Math.Cos(ang) + dorsal * (Math.Sin(ang) * (f.Arrangement == 3 ? .28 : .55));
                    double forward = f.Arrangement == 3 ? .25 : f.Needle ? .65 : .45;
                    var ld = radial + tan * forward + Vec3.Up * (.22 - f.Droop * .22)
                        + new Vec3(b.Range(-.18, .18), b.Range(-.12, .12), b.Range(-.18, .18));
                    double ll = Math.Min(height * .24, f.LeafLength * leafScale) * tipScale * b.Range(.82, 1.18);
                    int host = axis;
                    double hostT = nt;
                    if (petioleLen > 1e-4 && !f.Needle)
                    {
                        var pdir = radial * .85 + tan * .35 + Vec3.Up * .35;
                        host = b.AddAxis(axis, nt, pdir, petioleLen * b.Range(.8, 1.2), Vec3.Down * .28,
                            .0014, .0008, 3, Tissue.Green, emerge + nt * .04, .10, OrganFlags.None, OrganType.Petiole);
                        hostT = 1.0;
                    }
                    b.AddFlat(host, hostT, ld, ll, ll * f.LeafWidth, .0004, OrganType.Leaf, leaf,
                        (ushort)(petioleLen > 1e-4 ? 4 : 3), emerge + nt * .05, .16);
                }
            }
        }

        /// <summary>Builds a curved compound leaf (pinnate or trifoliate) on a green rachis/petiole.</summary>
        private static void AddCompoundLeaf(PlanBuilder b, ushort leaf, int parent, double t, Vec3 dir,
            double rachisLen, Vec3 bend, int pairs, double leafletLen, double leafletWidthRatio, ushort order, double emerge)
        {
            int rachis = b.AddAxis(parent, t, dir, rachisLen, bend, .0022, .0007, order, Tissue.Green,
                emerge, .14, OrganFlags.None, OrganType.Petiole);
            for (int p = 0; p < pairs; p++)
            {
                double pt = pairs == 1 ? .75 : .28 + .64 * p / (pairs - 1.0);
                double env = pairs == 1 ? 1.0 : .70 + .40 * Math.Sin(Math.PI * (.15 + .75 * p / Math.Max(1, pairs - 1)));
                var rt = b.TangentAt(rachis, pt);
                var lat = SideOf(rt);
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    var ld = lat * (sign * .95) + rt * .40 + Vec3.Up * b.Range(-.08, .22);
                    double ll = leafletLen * env * b.Range(.86, 1.14);
                    b.AddFlat(rachis, pt, ld, ll, ll * leafletWidthRatio, .0004, OrganType.Leaflet, leaf,
                        (ushort)(order + 1), emerge + pt * .05, .14);
                }
            }
            var tipT = b.TangentAt(rachis, 1.0);
            b.AddFlat(rachis, 1.0, tipT + Vec3.Up * .1, leafletLen * b.Range(.9, 1.15), leafletLen * leafletWidthRatio,
                .0004, OrganType.Leaflet, leaf, (ushort)(order + 1), emerge + .06, .14);
        }

        /// <summary>Builds a smooth 2-segment curved stem/trunk so no axis reads as a straight stick.</summary>
        private static (int Base, int Upper) CurvedStem(PlanBuilder b, Vec3 baseDir, double totalLen, Vec3 bend1, Vec3 bend2,
            double rBase, double rTip, double splitFrac = 0.54, double emerge = 0.0, double dur = 0.28, Vec3? rootOrigin = null)
        {
            double l1 = totalLen * splitFrac;
            double l2 = totalLen * (1 - splitFrac);
            double rMid = rBase + (rTip - rBase) * splitFrac;
            int s0 = b.AddAxis(-1, 0, baseDir, l1, bend1, rBase, rMid, 0, Tissue.Wood, emerge, dur * .6, rootOrigin: rootOrigin);
            var stem0 = b[s0];
            var axis = PlantMath.SafeNormal(baseDir, Vec3.Up);
            var side = SideOf(axis);
            // The control tangents create a broad S sweep without moving the ends
            // together, and the second section inherits the first section's tangent.
            double sweep = b.Range(.38, .68) * (b.Draw() < .5 ? -1 : 1);
            var chord0 = stem0.P1 - stem0.P0;
            stem0.T0 = chord0 + side * (l1 * sweep);
            stem0.T1 = chord0 - side * (l1 * sweep);
            var t1 = b.TangentAt(s0, 1.0);
            var dir2 = t1 + bend2 * .45;
            int s1 = b.AddAxis(s0, 1.0, dir2, l2, bend2, rMid * .84, rTip, 1, Tissue.Wood, emerge + .04, dur * .55);
            var stem1 = b[s1];
            var chord1 = stem1.P1 - stem1.P0;
            stem1.T0 = PlantMath.SafeNormal(stem0.T1, axis) * chord1.Length;
            stem1.T1 = chord1 + side * (l2 * sweep);
            return (s0, s1);
        }

        // Bend around an explicit chord: AddAxis' averaged tangents can shorten a
        // strongly curved limb. Keeping its endpoint preserves the intended crown reach.
        private static void Sweep(PlanBuilder b, int axis, Vec3 side, double amount)
        {
            var organ = b[axis];
            var chord = organ.P1 - organ.P0;
            organ.T0 = chord + side * (chord.Length * amount);
            organ.T1 = chord - side * (chord.Length * amount);
        }

        protected override void Design(PlanBuilder b)
        {
            ushort leaf = Leaf(b, f), ornament = Ornament(b, f);
            double phyl = b.Range(0, Math.Tau);
            if (f.Climber) { DesignClimbingFallback(b, leaf, ornament); return; }
            switch (f.Id)
            {
                case "banksorrow": DesignBanksorrow(b, leaf, ref phyl); return;
                case "cragparasol": DesignCragparasol(b, leaf, ref phyl); return;
                case "embercrown": DesignEmbercrown(b, leaf, ornament, ref phyl); return;
                case "fenforge": DesignFenforge(b, leaf, ornament, ref phyl); return;
                case "mirespire": DesignMirespire(b, leaf, ornament); return;
                case "moonbark": DesignMoonbark(b, leaf, ref phyl); return;
                case "greywatch": DesignGreywatch(b, leaf, ornament, ref phyl); return;
                case "hookthicket": DesignHookthicket(b, leaf, ref phyl); return;
                case "hollowcrown": DesignHollowcrown(b, leaf, ref phyl); return;
                case "witchberry": DesignWitchberry(b, leaf, ornament); return;
                case "kinkcane": DesignKinkcane(b, leaf, ref phyl); return;
                case "lacemantle": DesignLacemantle(b, leaf, ref phyl); return;
                case "lanternbrush": DesignLanternbrush(b, leaf, ornament, ref phyl); return;
                case "miremyrtle": DesignMiremyrtle(b, leaf, ornament, ref phyl); return;
                case "nutglade": DesignNutglade(b, leaf, ornament, ref phyl); return;
                case "rainshield": DesignRainshield(b, leaf); return;
                case "silverfelt": DesignSilverfelt(b, leaf, ornament, ref phyl); return;
                case "splitcrown": DesignSplitcrown(b, leaf, ref phyl); return;
                case "thornmantle": DesignThornmantle(b, leaf, ornament, ref phyl); return;
                case "vaultfern": DesignVaultfern(b, leaf); return;
                default: DesignGeneric(b, leaf, ornament, ref phyl); return;
            }
        }

        private void DesignFenforge(PlanBuilder b, ushort leaf, ushort ornament, ref double phyl)
        {
            // The bole changes direction as it rises, with substantial asymmetric
            // leaders instead of a single straight pole under repeated shelves.
            double rot = b.Range(0, Math.Tau);
            double tr = Math.Max(.012, height * .032);
            var lean = PlantMath.Horizontal(rot);
            var cross = PlantMath.Horizontal(rot + Math.PI / 2);
            int trunk = b.AddAxis(-1, 0, Vec3.Up + lean * .12, height * .31,
                cross * .38 + lean * .15, tr, tr * .78, 0, Tissue.Wood, 0, .30, OrganFlags.Continuous);
            int middle = b.AddAxis(trunk, 1, b.TangentAt(trunk, 1), height * .30,
                -cross * .75 + lean * .28, tr * .78, tr * .48, 1, Tissue.Wood, .02, .25, OrganFlags.Continuous | OrganFlags.Basal);
            int upper = b.AddAxis(middle, 1, b.TangentAt(middle, 1), height * .30,
                cross * .60 - lean * .25, tr * .48, tr * .15, 2, Tissue.Wood, .04, .24, OrganFlags.Continuous | OrganFlags.Basal);
            for (int r = 0; r < 8; r++)
            {
                double a = rot + r * Math.Tau / 8 + b.Range(-.20, .20);
                var outward = PlantMath.Horizontal(a);
                var side = PlantMath.Horizontal(a + Math.PI / 2);
                // Roots leave the lowest part of the bole, rather than hoisting the crown
                // onto stilts. Their tips enter the soil even on the shorter variants.
                int root = b.AddAxis(trunk, .10 + .04 * (r % 2), outward * .90 + Vec3.Down,
                    height * b.Range(.26, .38), side * b.Range(-.45, .45) + Vec3.Down * .24,
                    tr * b.Range(.48, .67), tr * .19, 1, Tissue.RootBark, .01, .22);
                var rootPlan = b[root];
                rootPlan.P1 = new Vec3(rootPlan.P1.X, -.012, rootPlan.P1.Z);
                b.AddAxis(root, .94, outward + Vec3.Down * .40, height * .08,
                    side * (r % 2 == 0 ? -.35 : .35), tr * .18, .002, 2, Tissue.RootBark, .04, .16);
                // Irregular knobby knees: clustered, uneven in count and height, rising from the roots.
                int knees = r % 3 == 0 ? 1 : r % 3 == 1 ? 3 : 2;
                for (int kn = 0; kn < knees; kn++)
                {
                    double kt = Math.Clamp(.42 + .50 * (kn + b.Draw()) / knees, .40, .97);
                    // Leaning, curving knees read as knuckled roots rather than a
                    // picket of vertical pegs.
                    double kneeLen = height * b.Range(.05, .15);
                    int knee = b.AddAxis(root, kt,
                        Vec3.Up * b.Range(.70, 1.0) + outward * b.Range(.10, .38) + side * b.Range(-.30, .30),
                        kneeLen,
                        outward * b.Range(.25, .55) + side * b.Range(-.30, .30) + Vec3.Down * .05,
                        tr * b.Range(.22, .34), tr * b.Range(.10, .18), 2, Tissue.RootBark, .03, .18);
                    var kneePlan = b[knee];
                    // Start along the root's own tangent so the step out of the root
                    // is a continuous knuckle rather than a right-angled peg.
                    kneePlan.T0 = PlantMath.SafeNormal(b.TangentAt(root, kt) * .55 + Vec3.Up, Vec3.Up) * kneeLen;
                    kneePlan.AxisGnarling = b.Range(.35, .75);
                    kneePlan.AxisWaist = b.Range(.15, .40);
                }
            }
            // A few arched prop roots swing out from the lower bole and re-enter the soil.
            for (int p = 0; p < 4; p++)
            {
                double a = rot + p * 1.7 + b.Range(-.5, .5);
                var outward = PlantMath.Horizontal(a);
                var side = PlantMath.Horizontal(a + Math.PI / 2);
                int prop = b.AddAxis(trunk, b.Range(.14, .26), outward * .9 + Vec3.Up * .75,
                    height * b.Range(.10, .17), Vec3.Down * 1.1 + side * b.Range(-.3, .3),
                    tr * b.Range(.30, .42), tr * .15, 2, Tissue.RootBark, .02, .22);
                var pr = b[prop];
                pr.P1 = new Vec3(pr.P1.X, -.012, pr.P1.Z);
                pr.AxisGnarling = b.Range(.25, .5);
            }
            for (int k = 0; k < 11; k++)
            {
                double u = k / 10.0, a = rot + k * 2.39996 + b.Range(-.32, .32);
                var outward = PlantMath.Horizontal(a);
                var side = PlantMath.Horizontal(a + Math.PI / 2);
                double reach = radius * (1.0 - .20 * u) * b.Range(.68, 1.20);
                int parent = k < 3 ? trunk : k < 7 ? middle : upper;
                double attachment = k < 3 ? .62 + k * .14 : k < 7 ? .12 + (k-3) * .25 : .12 + (k-7) * .24;
                int branch = b.AddAxis(parent, Math.Clamp(attachment + b.Range(-.06, .06), .10, .95),
                    outward + Vec3.Up * b.Range(.16, .58), reach,
                    Vec3.Up * b.Range(-.12, .30) + side * b.Range(-.35, .35), tr * (k < 5 ? .48 : .30), .002, 3, Tissue.Wood, .06, .20);
                Sweep(b, branch, side, b.Range(-.65, .65));
                for (int j = 0; j < 7; j++)
                {
                    double t = Math.Clamp(.14 + .12 * j + b.Range(-.035, .035), .10, .91);
                    int sign = j % 2 == 0 ? 1 : -1;
                    var tan = b.TangentAt(branch, t);
                    var dir = tan * .75 + SideOf(tan) * sign * .8 + Vec3.Up * .12;
                    int twig = b.AddAxis(branch, t, dir, reach * b.Range(.38, .52), Vec3.Up * b.Range(-.28, .52),
                        .003, .0008, 2, Tissue.Wood, .10, .16);
                    Foliage(b, leaf, twig, 6, .16, .98, .13, ref phyl, .012, 1.50, 2);
                }
                Foliage(b, leaf, branch, 5, .48, .99, .12, ref phyl, .012, 1.45, 2);
                if (k % 2 == 0) b.AddFlat(branch, .82, Vec3.Down, .05, .02, .001, OrganType.Fruit, ornament, 2, .2, .18);
            }
        }

        private void DesignMirespire(PlanBuilder b, ushort leaf, ushort ornament)
        {
            // Narrow conical leader with dense, tapered horizontal needle sprays at every tier.
            double rot = b.Range(0, Math.Tau), tr = Math.Max(.01, height * .025);
            int trunk = b.AddAxis(-1, 0, Vec3.Up, height * .98, PlantMath.Horizontal(rot) * .04,
                tr, tr * .12, 0, Tissue.Wood, 0, .28);
            for (int r = 0; r < 7; r++)
            {
                var d = PlantMath.Horizontal(rot + r * Math.Tau / 7);
                int root = b.AddAxis(trunk, .06, d + Vec3.Down * .2, radius * .32, Vec3.Down * .30,
                    tr * .45, tr * .16, 1, Tissue.RootBark, 0, .20);
                b.AddAxis(root, .9, Vec3.Up + d * .15, height * .06, -d * .2,
                    tr * .15, .002, 2, Tissue.RootBark, .03, .15);
            }
            for (int tier = 0; tier < 12; tier++)
            {
                double u = tier / 11.0, reach = radius * (.95 - .75 * u);
                // Uneven attachment heights and reaches open irregular windows between
                // major limbs while the needle sprays themselves stay unchanged.
                double tierAt = Math.Clamp(.18 + .78 * u + b.Range(-.05, .05), .13, .98);
                for (int j = 0; j < 4; j++)
                {
                    double a = rot + tier * 1.15 + j * Math.Tau / 4 + b.Range(-.34, .34);
                    var outward = PlantMath.Horizontal(a);
                    var branchSide = PlantMath.Horizontal(a + Math.PI / 2);
                    int branch = b.AddAxis(trunk, Math.Clamp(tierAt + b.Range(-.025, .025), .13, .98),
                        outward + Vec3.Up * b.Range(.04, .25), reach * b.Range(.70, 1.16),
                        Vec3.Down * b.Range(.04, .24), tr * .22 * (1 - .5 * u), .001, 1, Tissue.Wood, .05, .20);
                    Sweep(b, branch, branchSide, b.Range(-.45, .45));
                    for (int q = 0; q < 3; q++)
                    {
                        double t = Math.Clamp(.22 + q * .28 + b.Range(-.04, .04), .12, .94);
                        var tan = b.TangentAt(branch, t);
                        var side = SideOf(tan);
                        for (int sign = -1; sign <= 1; sign += 2)
                        {
                            double sprayLen = reach * (.55 - .22 * t) * b.Range(.78, 1.18);
                            double lift = (q + j + tier) % 2 == 0 ? .85 : -.65;
                            int spray = b.AddAxis(branch, t, tan * .55 + side * sign * .65 + Vec3.Up * lift,
                                sprayLen, Vec3.Up * (lift > 0 ? .22 : -.24), .0015, .0005, 2, Tissue.Green, .09, .16);
                            // Long, thin main needles (thin green rods of near-constant width)
                            // each carry a pinnate fringe of short sub-needles on both sides.
                            for (int n = 0; n < 4; n++)
                            {
                                double nt = .12 + n * .24;
                                var st = b.TangentAt(spray, nt);
                                var lateral = SideOf(st);
                                for (int pair = -1; pair <= 1; pair += 2)
                                {
                                    double len = Math.Min(.30, height * .15) * (1 - .22 * nt) * b.Range(.85, 1.15);
                                    double incline = ((n + q + j) % 3 - 1) * .85;
                                    var ndir = lateral * pair + st * .35 + Vec3.Up * incline;
                                    int needle = b.AddAxis(spray, nt, ndir, len, Vec3.Down * .06,
                                        .0009, .0007, 3, Tissue.Green, .11, .15);
                                    // Each needle supports one opposed pair of longer fringe leaves.
                                    {
                                        double snt = .49;
                                        var nTan = b.TangentAt(needle, snt);
                                        var nSide = SideOf(nTan);
                                        double subLen = len * .46;
                                        for (int fs = -1; fs <= 1; fs += 2)
                                            b.AddFlat(needle, snt, nSide * fs + nTan * .55,
                                                subLen, subLen * .09, .0003, OrganType.Leaf, leaf, 4, .13, .15);
                                    }
                                    b.AddFlat(needle, 1.0, b.TangentAt(needle, 1.0), len * .22, len * .02,
                                        .0003, OrganType.Leaf, leaf, 4, .13, .15);
                                }
                            }
                        }
                    }
                    if (j == 0 && tier % 2 == 0)
                        b.AddFlat(branch, .7, Vec3.Down, .04, .014, .001, OrganType.Fruit, ornament, 2, .20, .16);
                }
            }
        }

        private void DesignMoonbark(PlanBuilder b, ushort leaf, ref double phyl)
        {
            // Pale, crooked clustered stems divide into high arching limbs and pendant leafy shoots.
            double rot = b.Range(0, Math.Tau), tr = Math.Max(.008, height * .023);
            for (int c = 0; c < 3; c++)
            {
                double a = rot + c * Math.Tau / 3;
                var outwards = PlantMath.Horizontal(a);
                var side = PlantMath.Horizontal(a + Math.PI / 2);
                var (lower, upper) = CurvedStem(b, Vec3.Up + outwards * b.Range(.28, .44), height * b.Range(.72, 1.02),
                    side * .35 + outwards * .18, -side * .50 + outwards * .30,
                    tr, tr * .35, .47, .01 * c, .28);
                // The upper trunk continues through the same ring, rather than starting
                // on the outside of the lower trunk with a visible offset and collar.
                // Sink the trunk feet below soil so leaning stems flare cleanly out of ground.
                var lowerStem = b[lower];
                var upperStem = b[upper];
                lowerStem.P0 += Vec3.Down * (tr * .30);
                lowerStem.T0 = PlantMath.SafeNormal(lowerStem.T0, Vec3.Up) * (lowerStem.P1 - lowerStem.P0).Length;
                var correction = lowerStem.P1 - upperStem.P0;
                upperStem.P0 += correction;
                upperStem.P1 += correction;
                upperStem.R0 = lowerStem.R1;
                lowerStem.Flags |= OrganFlags.Continuous;
                upperStem.Flags |= OrganFlags.Continuous;
                for (int k = 0; k < 4; k++)
                {
                    double az = a + (k - 1.5) * .65;
                    var dir = PlantMath.Horizontal(az);
                    var limbDir = dir * .9 + Vec3.Up * b.Range(.45, .72);
                    double atT = .25 + .22 * k;
                    // A short collar leaves the trunk exactly along its tangent, sunk into
                    // the trunk axis, and the limb continues from the collar's end ring.
                    int collar = b.AddAxis(upper, atT, b.TangentAt(upper, atT) * .9 + limbDir.Normalized() * .55, tr * 5.5, Vec3.Zero,
                        tr * .5, tr * .30, 2, Tissue.Wood, .06, .12, OrganFlags.Continuous);
                    var col = b[collar];
                    double at = col.AttachT;
                    var center = PlantMath.Hermite(upperStem.P0, upperStem.T0, upperStem.P1, upperStem.T1, at);
                    var axis = b.TangentAt(upper, at);
                    double chord = tr * 5.5;
                    var exit = PlantMath.SafeNormal(axis * .35 + limbDir.Normalized(), axis);
                    col.P0 = center;
                    col.P1 = center + PlantMath.SafeNormal(axis * .9 + limbDir.Normalized() * .55, axis) * chord;
                    col.T0 = axis * chord;
                    col.T1 = exit * chord;
                    int limb = b.AddAxis(collar, 1, limbDir, radius * b.Range(.72, 1.0),
                        Vec3.Down * 1.45 + side * b.Range(-.2, .2), tr * .30, .0018, 2, Tissue.Wood, .07, .20,
                        OrganFlags.Continuous);
                    var branch = b[limb];
                    branch.P0 = col.P1;
                    branch.R0 = col.R1;
                    branch.T0 = exit * branch.T0.Length;
                    for (int j = 0; j < 8; j++)
                    {
                        double t = .18 + .105 * j;
                        var tan = b.TangentAt(limb, t);
                        var lateral = SideOf(tan);
                        int twig = b.AddAxis(limb, t, Vec3.Down * 1.25 + tan * .18 + lateral * (j % 2 == 0 ? .18 : -.18),
                            radius * b.Range(.48, .72), Vec3.Down * .85, .0022, .0006, 3, Tissue.Wood, .11, .16);
                        Foliage(b, leaf, twig, 7, .12, .98, .13, ref phyl, .010, 1.30, 2);
                    }
                }
            }
        }

        private void DesignCragparasol(PlanBuilder b, ushort leaf, ref double phyl)
        {
            // One continuous Hermite trunk avoids the side-offset at an axis junction.
            double az=b.Range(0,Math.Tau), tr=Math.Max(.025,height*.045);
            var lean=PlantMath.Horizontal(az);
            int trunk=b.AddAxis(-1,0,Vec3.Up+lean*.12,height*.72,lean*.12,tr,tr*.38,0,Tissue.Wood,0,.34,OrganFlags.Continuous);
            // Shallow surface roots carry the flared foot into the soil.
            for(int r=0;r<5;r++)
            {
                double ra=az+.7+r*Math.Tau/5+b.Range(-.35,.35);
                var ro=PlantMath.Horizontal(ra);
                var rs=PlantMath.Horizontal(ra+Math.PI/2);
                int root=b.AddAxis(trunk,b.Range(.02,.09),ro*.6+Vec3.Down,height*b.Range(.07,.13),
                    rs*b.Range(-.35,.35)+Vec3.Down*.2,tr*b.Range(.30,.44),tr*b.Range(.07,.12),1,
                    Tissue.RootBark,0,.18);
                var foot=b[root];
                foot.P1=ro*(radius*b.Range(.13,.26))+rs*(radius*b.Range(-.05,.05))
                    + Vec3.Down*(height*b.Range(.006,.016));
                double chordLen=(foot.P1-foot.P0).Length;
                foot.T0=PlantMath.SafeNormal(foot.T0,Vec3.Down)*chordLen;
                foot.T1=PlantMath.SafeNormal(foot.T1,ro)*chordLen;
                foot.AxisGnarling=b.Range(.14,.32);
            }
            ushort tuftLeaf=b.AddShape(new OrganShape { Name="cragparasol_upcurled_leaf", Type=OrganType.Leaf,
                SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Max(0,Math.Sin(Math.PI*t)),.8)),
                MidribCurl=.75f, CrossCurl=.008f, ColorAdaxial=PlantShapes.Srgb(.91,.86,.60),
                ColorAbaxial=PlantShapes.Srgb(.98,.93,.72), Roughness=.45f, Transmission=.32f });
            ushort flower=b.AddShape(new OrganShape { Name="cragparasol_blue_fuzz", Type=OrganType.Flower,
                SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(1-t,.7)),
                ColorAdaxial=PlantShapes.Srgb(0.09,0.13,0.44), ColorAbaxial=PlantShapes.Srgb(0.13,0.19,0.58), Thickness=.0003f, Roughness=.78f });
            for(int j=0;j<7;j++)
            {
                double a=az+j*Math.Tau/7+b.Range(-.34,.34); var outwards=PlantMath.Horizontal(a);
                double limbLen=radius*b.Range(.52,.70);
                double limbAttach=j==0 ? 1.0 : b.Range(.82,.97);
                int limb=b.AddAxis(trunk,limbAttach,outwards*.8+Vec3.Up*b.Range(.52,1.02),limbLen,Vec3.Up*.22,
                    j==0 ? tr*.38 : tr*.32,.003,1,Tissue.Wood,.10,.22,OrganFlags.Continuous);
                var lp=b[limb];
                if(j==0)
                {
                    lp.P0=b[trunk].P1;
                    lp.R0=b[trunk].R1;
                    lp.T0=PlantMath.SafeNormal(b[trunk].T1*.55+outwards*.75+Vec3.Up*.75,Vec3.Up)*limbLen;
                    lp.P1=lp.P0+(lp.T0+lp.T1)*.5;
                }
                else
                {
                    lp.P0=PlantMath.Hermite(b[trunk].P0,b[trunk].T0,b[trunk].P1,b[trunk].T1,lp.AttachT);
                    lp.P1=lp.P0+(lp.T0+lp.T1)*.5;
                }
                for(int k=0;k<3;k++)
                {
                    double sa=a+(k-1)*.48; var ray=PlantMath.Horizontal(sa);
                    double subLen=radius*b.Range(.16,.23);
                    double subAt=k==2 ? 1.0 : .68+k*.14;
                    int sub=b.AddAxis(limb,subAt,ray*.65+Vec3.Up*.8,subLen,Vec3.Up*.18,
                        k==2 ? lp.R1 : .003,.0015,2,Tissue.Wood,.18,.16,OrganFlags.Continuous);
                    var sp=b[sub];
                    if(k==2)
                    {
                        sp.P0=lp.P1;
                        sp.R0=lp.R1;
                        sp.T0=PlantMath.SafeNormal(lp.T1*.55+ray*.55+Vec3.Up*.65,Vec3.Up)*subLen;
                        sp.P1=sp.P0+(sp.T0+sp.T1)*.5;
                    }
                    else
                    {
                        sp.P0=PlantMath.Hermite(lp.P0,lp.T0,lp.P1,lp.T1,sp.AttachT);
                        sp.P1=sp.P0+(sp.T0+sp.T1)*.5;
                    }
                    // Unequal tuft masses, so the parasol is a ring of distinct
                    // rosettes rather than seven copies of one pad.
                    double tuftScale=b.Range(.72,1.30);
                    // Each terminal rosette has its own upright blue fuzzy spike.
                    for(int n=0;n<20;n++)
                    {
                        double la=sa+n*2.399963229728653; var ld=PlantMath.Horizontal(la);
                        double length=Math.Min(height*.24,f.LeafLength)*tuftScale*b.Range(.85,1.2);
                        b.AddFlat(sub,.93,ld*.65+Vec3.Up*b.Range(.45,.85),length,
                            length*f.LeafWidth*.65,.0005,OrganType.Leaf,tuftLeaf,3,.20,.20);
                    }
                    int spike=b.AddAxis(sub,1,Vec3.Up+ray*.18,height*.13,Vec3.Zero,.002,.001,3,Tissue.Green,.35,.15);
                    for(int n=0;n<36;n++)
                    {
                        double t=(n+.5)/36.0, fa=n*2.399963229728653;
                        b.AddFlat(spike,t,PlantMath.Horizontal(fa)+Vec3.Up*.2,height*.040,
                            height*.0042,.0002,OrganType.Flower,flower,4,.45,.15);
                    }
                }
            }
        }

        private void DesignBanksorrow(PlanBuilder b, ushort leaf, ref double phyl)
        {
            // Low leaning fork, fountain boughs, and long leafy willow withes.
            double rot = b.Range(0, Math.Tau), tr = Math.Max(.012, height * .045);
            var lean = PlantMath.Horizontal(rot);
            int trunk = b.AddAxis(-1, 0, Vec3.Up + lean * .22, height * .55,
                lean * .22, tr, tr * .58, 0, Tissue.Wood, 0, .18, OrganFlags.Continuous);
            // The bole ends in tapered buttresses that run out along the bank instead
            // of stopping as a cut cylinder at the soil.
            for (int r = 0; r < 5; r++)
            {
                double a = rot + .4 + (r + b.Range(-.30, .30)) * Math.Tau / 5;
                var outward = PlantMath.Horizontal(a);
                var side = PlantMath.Horizontal(a + Math.PI / 2);
                double rootLen = height * b.Range(.10, .19);
                int root = b.AddAxis(trunk, b.Range(.02, .12), outward * .55 + Vec3.Down,
                    rootLen, side * b.Range(-.40, .40) + Vec3.Down * .20,
                    tr * b.Range(.34, .48), tr * b.Range(.09, .15), 1, Tissue.RootBark, 0, .18);
                var foot = b[root];
                foot.P0 = Vec3.Up * (height * b.Range(.02, .06)) + outward * (tr * .35);
                foot.P1 = outward * (radius * b.Range(.16, .34))
                    + side * (radius * b.Range(-.06, .06))
                    + Vec3.Down * (height * b.Range(.008, .020));
                double chordLen = (foot.P1 - foot.P0).Length;
                foot.T0 = PlantMath.SafeNormal(foot.T0, Vec3.Down) * chordLen;
                foot.T1 = PlantMath.SafeNormal(foot.T1, outward) * chordLen;
                foot.AxisGnarling = b.Range(.16, .36);
            }
            for (int i = 0; i < 7; i++)
            {
                double a = rot + i * Math.Tau / 7 + b.Range(-.18, .18);
                var outward = PlantMath.Horizontal(a);
                var side = PlantMath.Horizontal(a + Math.PI / 2);
                double reach = radius * b.Range(.57, .79);
                // Uneven primary scaffold heights keep the curtain crown from
                // resolving into an even dome.
                double boughLift = .62 + .36 * ((i * 3) % 5) / 4.0 + b.Range(-.08, .08);
                double boughAt = i == 0 ? 1.0 : .74 + .09 * (i % 3) + b.Range(-.03, .03);
                int bough = b.AddAxis(trunk, boughAt,
                    outward * .75 + Vec3.Up * boughLift, reach, Vec3.Down * .85 + side * b.Range(-.12, .12),
                    i == 0 ? tr * .58 : tr * .48, .004, 1, Tissue.Wood, .04, .14, OrganFlags.Continuous);
                var boughPlan = b[bough];
                if (i == 0)
                {
                    boughPlan.P0 = b[trunk].P1;
                    boughPlan.R0 = b[trunk].R1;
                    boughPlan.T0 = PlantMath.SafeNormal(b[trunk].T1 * .60 + outward * .65 + Vec3.Up * boughLift, outward) * reach;
                }
                else
                {
                    boughPlan.P0 = PlantMath.Hermite(b[trunk].P0, b[trunk].T0, b[trunk].P1, b[trunk].T1, boughPlan.AttachT);
                    boughPlan.T0 = PlantMath.SafeNormal(b.TangentAt(trunk, boughPlan.AttachT) * .55 + outward * .65 + Vec3.Up * boughLift, outward) * reach;
                }
                boughPlan.P1 = boughPlan.P0 + (boughPlan.T0 + boughPlan.T1) * .5;
                // Each paired lateral first grows outward as a tapering branch; its
                // own tip then continues into the long, leafy hanging shoot.
                for (int j = 0; j < 4; j++)
                {
                    double t = .27 + .19 * j + b.Range(-.025, .025);
                    var tan = b.TangentAt(bough, t);
                    for (int sign = -1; sign <= 1; sign += 2)
                    {
                        double branchLen = reach * b.Range(.28, .48) * (1.12 - .28 * t);
                        double branchR = (b[bough].R0 * (1 - t) + b[bough].R1 * t) * .38;
                        var branchDir = tan * .85 + outward * .35 + side * (sign * b.Range(.38, .68))
                            + Vec3.Up * b.Range(.05, .20);
                        int lateral = b.AddAxis(bough, t, branchDir, branchLen,
                            Vec3.Down * b.Range(.28, .55) + side * b.Range(-.12, .12),
                            branchR, branchR * .42, 2, Tissue.Wood, .08, .10, OrganFlags.Continuous);
                        var latPlan = b[lateral];
                        latPlan.P0 = PlantMath.Hermite(boughPlan.P0, boughPlan.T0, boughPlan.P1, boughPlan.T1, latPlan.AttachT);
                        latPlan.P1 = latPlan.P0 + (latPlan.T0 + latPlan.T1) * .5;
                        // Fine twigs branch from each lateral before continuing into
                        // their own drooping leafy tips, never directly from a bough.
                        for (int k = 0; k < 3; k++)
                        {
                            double tt = .34 + k * .30 + b.Range(-.035, .035);
                            var twigDir = b.TangentAt(lateral, tt) * .8
                                + side * ((k % 2 == 0 ? -1 : 1) * b.Range(.25, .48)) + outward * .15;
                            double twigR = branchR * (.40 - .18 * tt);
                            int twig = b.AddAxis(lateral, tt, twigDir, reach * b.Range(.14, .23),
                                Vec3.Down * b.Range(.20, .40), twigR, twigR * .48, 3, Tissue.Wood, .09, .12, OrganFlags.Continuous);
                            var branch = b[twig];
                            branch.P0 = PlantMath.Hermite(latPlan.P0, latPlan.T0, latPlan.P1, latPlan.T1, branch.AttachT);
                            branch.P1 = branch.P0 + (branch.T0 + branch.T1) * .5;
                            double drape = height * b.Range(.40, .65);
                            var startDir = PlantMath.SafeNormal(branch.T1, outward);
                            var endDir = PlantMath.SafeNormal(Vec3.Down * b.Range(1.1, 1.6)
                                + outward * b.Range(.10, .30) + side * b.Range(-.18, .18), Vec3.Down);
                            int withe = b.AddAxis(twig, 1, startDir, drape, Vec3.Down,
                                branch.R1, branch.R1 * .12, 4, Tissue.Wood, .10, .14, OrganFlags.Terminal | OrganFlags.Continuous);
                            var shoot = b[withe];
                            shoot.P0 = branch.P1;
                            shoot.T0 = startDir * drape;
                            shoot.T1 = endDir * drape;
                            shoot.P1 = shoot.P0 + startDir * (drape * .18) + endDir * (drape * .80);
                            shoot.P1 = new Vec3(shoot.P1.X, Math.Max(height * b.Range(.015, .065), shoot.P1.Y), shoot.P1.Z);
                            shoot.R0 = branch.R1;
                            shoot.R1 = branch.R1 * .12;
                            Foliage(b, leaf, withe, 18, .04, .98, .12, ref phyl, leafScale: 1.08, perOverride: 1);
                        }
                        double latDrape = height * b.Range(.34, .54);
                        var latStart = PlantMath.SafeNormal(latPlan.T1, outward);
                        var latEnd = PlantMath.SafeNormal(Vec3.Down * b.Range(1.1, 1.5) + outward * .2, Vec3.Down);
                        int latWithe = b.AddAxis(lateral, 1, latStart, latDrape, Vec3.Down,
                            latPlan.R1, latPlan.R1 * .14, 3, Tissue.Wood, .10, .14, OrganFlags.Terminal | OrganFlags.Continuous);
                        var lShoot = b[latWithe];
                        lShoot.P0 = latPlan.P1;
                        lShoot.T0 = latStart * latDrape;
                        lShoot.T1 = latEnd * latDrape;
                        lShoot.P1 = lShoot.P0 + latStart * (latDrape * .18) + latEnd * (latDrape * .80);
                        lShoot.P1 = new Vec3(lShoot.P1.X, Math.Max(height * b.Range(.02, .08), lShoot.P1.Y), lShoot.P1.Z);
                        lShoot.R0 = latPlan.R1;
                        lShoot.R1 = latPlan.R1 * .14;
                        Foliage(b, leaf, latWithe, 4, .06, .98, .12, ref phyl, leafScale: 1.05, perOverride: 1);
                    }
                }
                double bDrape = height * b.Range(.32, .50);
                var bStart = PlantMath.SafeNormal(boughPlan.T1, outward);
                var bEnd = PlantMath.SafeNormal(Vec3.Down * b.Range(1.1, 1.5) + outward * .25, Vec3.Down);
                int bWithe = b.AddAxis(bough, 1, bStart, bDrape, Vec3.Down,
                    boughPlan.R1, boughPlan.R1 * .16, 2, Tissue.Wood, .10, .14, OrganFlags.Terminal | OrganFlags.Continuous);
                var bShoot = b[bWithe];
                bShoot.P0 = boughPlan.P1;
                bShoot.T0 = bStart * bDrape;
                bShoot.T1 = bEnd * bDrape;
                bShoot.P1 = bShoot.P0 + bStart * (bDrape * .18) + bEnd * (bDrape * .80);
                bShoot.P1 = new Vec3(bShoot.P1.X, Math.Max(height * b.Range(.03, .09), bShoot.P1.Y), bShoot.P1.Z);
                bShoot.R0 = boughPlan.R1;
                bShoot.R1 = boughPlan.R1 * .16;
                Foliage(b, leaf, bWithe, 4, .06, .98, .12, ref phyl, leafScale: 1.02, perOverride: 1);
            }
        }

        private void DesignEmbercrown(PlanBuilder b, ushort leaf, ushort ornament, ref double phyl)
        {
            // Clonal lantern thicket: sinuous ascending stems, curved limbs, arching pinnate compound leaves, upright fruit panicles.
            int stems = 7;
            double stemR = Math.Max(.008, height * .022 / Math.Sqrt(stems));
            double baseRot = b.Range(0, Math.Tau);
            for (int c = 0; c < stems; c++)
            {
                double az = baseRot + c * Math.Tau / stems + b.Range(-.40, .40);
                var h = PlantMath.Horizontal(az);
                var s = PlantMath.Horizontal(az + Math.PI / 2);
                double len = height * (.52 + .075 * c) * b.Range(.95, 1.05);
                double turn = c % 2 == 0 ? 1 : -1;
                var d0 = Vec3.Up + h * .24 + s * (.70 * turn);
                int s0 = b.AddAxis(-1, 0, d0, len * .34, Vec3.Zero,
                    stemR, stemR * .78, 0, Tissue.Wood, .01 * c, .16, rootOrigin: h * radius * b.Range(.10,.30));
                b[s0].P1 = b[s0].P0 + Vec3.Up * (len * .33) + h * (len * .09) + s * (len * .12 * turn);
                b[s0].T0 = (Vec3.Up + s * (.70 * turn)) * (len * .34);
                b[s0].T1 = (Vec3.Up - s * (.70 * turn)) * (len * .34);
                int mid = b.AddAxis(s0, .98, b.TangentAt(s0, 1), len * .33,
                    Vec3.Zero, stemR * .75, stemR * .55, 1, Tissue.Wood, .04, .14);
                b[mid].P1 = b[mid].P0 + Vec3.Up * (len * .33) + h * (len * .09) - s * (len * .12 * turn);
                b[mid].T0 = b[s0].T1;
                b[mid].T1 = (Vec3.Up + s * (.70 * turn)) * (len * .34);
                int s1 = b.AddAxis(mid, .98, b.TangentAt(mid, 1), len * .33,
                    Vec3.Zero, stemR * .52, stemR * .35, 1, Tissue.Wood, .07, .14);
                b[s1].P1 = b[s1].P0 + Vec3.Up * (len * .33) + h * (len * .09) + s * (len * .12 * turn);
                b[s1].T0 = b[mid].T1;
                b[s1].T1 = (Vec3.Up - s * (.70 * turn)) * (len * .34);

                // Curved limbs on upper stem + crown tip.
                for (int k = 0; k < 4; k++)
                {
                    int host = k < 2 ? mid : s1;
                    double bt = k < 2 ? .52 + .36 * k : .28 + .58 * (k - 2);
                    double baz = az + (k % 2 == 0 ? 1 : -1) * b.Range(.55, 1.15) + b.Range(-.3, .3);
                    var bo = PlantMath.Horizontal(baz);
                    var bs = PlantMath.Horizontal(baz + Math.PI / 2);
                    double reach = radius * b.Range(.58, .94);
                    double sweep = (k % 2 == 0 ? 1 : -1) * turn;
                    int br0 = b.AddAxis(host, bt, bo * .75 + Vec3.Up * .65 + bs * (1.10 * sweep), reach * .55,
                        Vec3.Zero, stemR * .32, stemR * .19, 2, Tissue.Wood, .07, .12);
                    b[br0].P1 = b[br0].P0 + bo * (reach * .42) + Vec3.Up * (reach * .22) + bs * (reach * .22 * sweep);
                    b[br0].T0 = (bo * .8 + Vec3.Up * .5 + bs * (1.16 * sweep)) * (reach * .55);
                    b[br0].T1 = (bo * .8 + Vec3.Up * .5 - bs * (1.16 * sweep)) * (reach * .55);
                    int br = b.AddAxis(br0, .98, b.TangentAt(br0, 1), reach * .45,
                        Vec3.Zero, stemR * .18, .0025, 3, Tissue.Wood, .10, .12);
                    b[br].P1 = b[br].P0 + bo * (reach * .34) + Vec3.Up * (reach * .18) - bs * (reach * .20 * sweep);
                    b[br].T0 = b[br0].T1;
                    b[br].T1 = (bo * .8 + Vec3.Up * .5 + bs * (1.16 * sweep)) * (reach * .45);
                    // Arching pinnate compound leaf sprays radiating from the branch.
                    for (int lf = 0; lf < 4; lf++)
                    {
                        int leafHost = lf < 2 ? br0 : br;
                        double lt = lf < 2 ? .58 + .36 * lf : .35 + .60 * (lf - 2);
                        double laz = baz + (lf - 1.5) * .9 + b.Range(-.25, .25);
                        var ld = PlantMath.Horizontal(laz) * .9 + Vec3.Up * b.Range(.25, .65);
                        AddCompoundLeaf(b, leaf, leafHost, lt, ld, b.Range(.22, .34), Vec3.Down * .55,
                            6, b.Range(.065, .095), .22, 3, .11);
                    }
                    // Upright ember fruit lantern panicle at branch tip.
                    b.AddFlat(br, .96, Vec3.Up + bo * .15, Math.Min(.09, height * .05), Math.Min(.032, height * .02),
                        .001, OrganType.Fruit, ornament, 3, .18, .18);
                }
                // Crown tip compound rosette + upright fruit.
                for (int lf = 0; lf < 3; lf++)
                {
                    var ld = PlantMath.Horizontal(az + lf * 2.1) * .85 + Vec3.Up * .55;
                    AddCompoundLeaf(b, leaf, s1, .92, ld, b.Range(.24, .34), Vec3.Down * .50, 6, .08, .22, 2, .12);
                }
                b.AddFlat(s1, .98, Vec3.Up, .09, .035, .001, OrganType.Fruit, ornament, 2, .20, .16);
            }
        }

        private void DesignGreywatch(PlanBuilder b, ushort leaf, ushort ornament, ref double phyl)
        {
            // Uneven limbs rise from a leaning bole, then sweep sharply down at their tips.
            double rot = b.Range(0, Math.Tau), tr = Math.Max(.012, height * .045);
            int bole = b.AddAxis(-1, 0, Vec3.Up + PlantMath.Horizontal(rot) * .18, height * .25,
                PlantMath.Horizontal(rot + 1) * .12, tr, tr * .65, 0, Tissue.Wood, 0, .2);
            for (int i = 0; i < 6; i++)
            {
                double a = rot + i * Math.Tau / 6 + b.Range(-.62, .62);
                var outward = PlantMath.Horizontal(a);
                var side = PlantMath.Horizontal(a + Math.PI / 2);
                int limb = b.AddAxis(bole, b.Range(.30,.92), outward*.55 + Vec3.Up*b.Range(.8,1.3),
                    radius*b.Range(.55,.85), outward*.65 + Vec3.Down*.55 + side*b.Range(-.3,.3), tr*.42, tr*.18, 1, Tissue.Wood, .04, .2);
                var tangent = b.TangentAt(limb,1);
                int droop = b.AddAxis(limb, 1, tangent, radius*b.Range(.35,.65),
                    Vec3.Down*b.Range(2.5,4.0) + side*b.Range(-.35,.35), tr*.18, .0015, 2, Tissue.Wood, .06, .2, OrganFlags.Basal);
                for (int j = 0; j < 7; j++)
                {
                    int host = j < 3 ? limb : droop;
                    double t = j < 3 ? b.Range(.3,.92) : b.Range(.12,.95);
                    double sa = a + b.Range(-1.2,1.2);
                    var d = PlantMath.Horizontal(sa);
                    int shoot = b.AddAxis(host,t,d+Vec3.Up*b.Range(.05,.55),radius*b.Range(.12,.42),
                        Vec3.Down*b.Range(.15,.65)+side*b.Range(-.3,.3),.003,.0008,3,Tissue.Green,.09,.16);
                    Foliage(b,leaf,shoot,9,.10,.99,.13,ref phyl,leafScale:1.25,perOverride:3);
                }
                Foliage(b,leaf,droop,8,.15,.99,.13,ref phyl,leafScale:1.25,perOverride:3);
            }
        }

        private void DesignHookthicket(PlanBuilder b, ushort leaf, ref double phyl)
        {
            // Rubus fruticosus: multi-segment parabolic arching canes that rise to ~1.1m then bow back to ground, clothed in compound bramble leaves & prickles.
            int canes = 8;
            double caneR = Math.Max(.006, height * .024 / Math.Sqrt(canes));
            double baseRot = b.Range(0, Math.Tau);
            for (int c = 0; c < canes; c++)
            {
                double az = baseRot + c * Math.Tau / canes + b.Range(-.38, .38);
                var h = PlantMath.Horizontal(az);
                var s = PlantMath.Horizontal(az + Math.PI / 2);
                double segLen = height * b.Range(.70, 1.18);
                // Segment 1: climbs steeply and arcs outward to crest near 0.95-1.15m.
                int asc = b.AddAxis(-1, 0, Vec3.Up * b.Range(1.05, 1.45) + h * b.Range(.40, .68), segLen,
                    h * b.Range(.40, .68) + Vec3.Down * .48 + s * b.Range(-.35, .35), caneR, caneR * .62, 0, Tissue.Wood, .01 * c, .22);
                // Segment 2: bows over and sweeps back toward the ground.
                var t1 = b.TangentAt(asc, 1.0);
                int bow = b.AddAxis(asc, .96, t1 + h * .45 + Vec3.Down * .55, segLen * b.Range(.75, 1.02),
                    Vec3.Down * b.Range(.85, 1.25) + s * b.Range(-.4, .4), caneR * .52, .0020, 1, Tissue.Wood, .05, .22);

                // Recurved thorns along ascending & bowing cane.
                for (int th = 0; th < 14; th++)
                {
                    int host = th < 7 ? asc : bow;
                    double tt = .08 + .84 * (th % 7) / 6.0;
                    var td = s * (th % 2 == 0 ? .8 : -.8) + h * .4 + Vec3.Up * .2;
                    b.AddAxis(host, tt, td, .038, Vec3.Down * 2.4 - h*.6, .0032, .00025, 2, Tissue.Bark, .08, .10, OrganFlags.GlossyBlack);
                }

                // Curved leafy lateral shoots along both ascending and bowing segments.
                for (int k = 0; k < 10; k++)
                {
                    int host = k < 5 ? asc : bow;
                    double bt = Math.Clamp((k < 5 ? .16 + .19 * k : .10 + .20 * (k - 5)) + b.Range(-.055,.055), .08,.97);
                    double baz = az + (k % 2 == 0 ? 1 : -1) * b.Range(.55, 1.05);
                    var bo = PlantMath.Horizontal(baz);
                    var bs = PlantMath.Horizontal(baz + Math.PI / 2);
                    int lat = b.AddAxis(host, bt, bo * .75 + Vec3.Up * b.Range(.45, .85), radius * b.Range(.30, .46),
                        bs * b.Range(-.4, .4) + Vec3.Down * b.Range(.15, .45), caneR * .34, .0014, 2, Tissue.Green, .08, .16);
                    for (int lf = 0; lf < 4; lf++)
                    {
                        double lt = .22 + .72 * lf / 3.0;
                        var ldir = PlantMath.Horizontal(baz + (lf - 1.5) * .9) * .85 + Vec3.Up * b.Range(.2, .55);
                        AddCompoundLeaf(b, leaf, lat, lt, ldir, b.Range(.06, .09), Vec3.Down * .25,
                            1 + (lf % 2 == 1 ? 1 : 0), b.Range(.080, .115), .34, 3, .12);
                    }
                }
            }
        }

        private void DesignHollowcrown(PlanBuilder b, ushort leaf, ref double phyl)
        {
            // Veteran pollard after the previous form: a tall, broad, gently leaning
            // hollow bole with a flared base, buttress roots that sink down out of the
            // trunk flare before running along the soil, and a swollen bolling head
            // from which a ring of heavy elbowed scaffold limbs spreads into a wide crown.
            double boleHeight = height * .56, r = Math.Max(.11, Math.Max(height * .11, radius * .30));
            double opening = Math.Atan2(Vec3.Forward.Z, Vec3.Forward.X);
            int bole = b.AddAxis(-1, 0, Vec3.Up, boleHeight,
                PlantMath.Horizontal(opening + 2.2) * .05, r * 1.06, r * .92, 0, Tissue.Bark, 0, .25,
                OrganFlags.Hollow | OrganFlags.HollowWindow);
            b[bole].AxisWaist = .14f;
            b[bole].Up = Vec3.Forward;
            b[bole].HollowRimRoll = .85f;

            // Six irregular buttresses, none directly under the opening.
            double[] rootOffsets = { .78, Math.PI * .52, Math.PI * .94, Math.PI * 1.36, Math.PI * 1.66, Math.Tau - .78 };
            foreach (double off in rootOffsets)
            {
                double az = opening + off + b.Range(-.22, .22);
                var radial = PlantMath.Horizontal(az);
                var ortho = PlantMath.Horizontal(az + Math.PI / 2);
                double length = r * b.Range(1.4, 2.6);
                int root = b.AddAxis(bole, b.Range(.16, .24), radial * .5 + Vec3.Down, length,
                    Vec3.Zero, r * b.Range(.26, .32), r * b.Range(.05, .09), 1, Tissue.RootBark, 0, .16);
                var o = b[root];
                // Emerge from inside the lower bole flare instead of jutting off the
                // outside as a flat shelf, then sink below the soil line.
                var c0 = PlantMath.Hermite(b[bole].P0, b[bole].T0, b[bole].P1, b[bole].T1, o.AttachT);
                o.P0 = c0 + radial * (r * .72);
                o.P1 = radial * (r * b.Range(1.9, 3.1)) + ortho * (r * b.Range(-.40, .40))
                    + Vec3.Down * (r * b.Range(.10, .40));
                o.T0 = (Vec3.Down * .92 + radial * .38) * length;
                o.T1 = (radial * .80 + Vec3.Down * b.Range(.14, .34) + ortho * b.Range(-.30, .30)) * length;
                o.AxisGnarling = b.Range(.14, .34);
                o.AxisWaist = b.Range(.10, .26);
            }

            const int limbs = 6;
            double baseAz = opening + Math.PI * .20;
            for (int k = 0; k < limbs; k++)
            {
                double az = baseAz + k * Math.Tau / limbs + b.Range(-.38, .38);
                var outward = PlantMath.Horizontal(az);
                var ortho = PlantMath.Horizontal(az + Math.PI / 2);
                double limbR = r * b.Range(.28, .33);
                double reach = radius * b.Range(.30, .40), rise = height * b.Range(.10, .15);

                // Heavy knurled primary limb seated inside the bolling wall annulus so
                // it rises out of the knuckles without a step collar or sealing the mouth.
                int limb = b.AddAxis(bole, Math.Clamp(.85 + .024 * k + b.Range(-.015, .015), .84, .97),
                    outward * .55 + Vec3.Up * .8, reach,
                    Vec3.Zero, limbR, limbR * .66, 1, Tissue.Wood, .04, .17);
                var l = b[limb];
                var cLimb = PlantMath.Hermite(b[bole].P0, b[bole].T0, b[bole].P1, b[bole].T1, l.AttachT);
                l.P0 = cLimb + outward * (r * .74);
                l.P1 = l.P0 + outward * reach + ortho * (reach * b.Range(-.12, .12)) + Vec3.Up * rise;
                l.T0 = (Vec3.Up * .85 + outward * .52) * (reach + rise);
                l.T1 = (outward * .80 + Vec3.Up * .50 + ortho * b.Range(-.15, .15)) * (reach + rise);
                l.AxisWaist = b.Range(.10, .22);
                l.Flags |= OrganFlags.Continuous;

                // Outer spreading bough continues from the elbow ring.
                double outAz = az + b.Range(-.25, .25);
                var outDir = PlantMath.Horizontal(outAz);
                double outReach = radius * b.Range(.40, .52);
                int outer = b.AddAxis(limb, 1, l.T1, outReach, Vec3.Zero, limbR * .66, limbR * .16,
                    2, Tissue.Wood, .07, .16, OrganFlags.Continuous);
                var ob = b[outer];
                ob.P0 = l.P1; ob.R0 = l.R1; ob.T0 = l.T1;
                ob.P1 = ob.P0 + outDir * outReach + Vec3.Up * (height * b.Range(.05, .10))
                    + PlantMath.Horizontal(outAz + Math.PI / 2) * (outReach * b.Range(-.12, .12));
                ob.T1 = (outDir * .85 + Vec3.Up * .22) * outReach;

                // Ascending inner bough curving up over the bowl.
                double inRise = height * b.Range(.20, .27);
                int inner = b.AddAxis(limb, .92, Vec3.Up * .8 + outward * .35, inRise,
                    -outward * .3, limbR * .52, limbR * .14, 2, Tissue.Wood, .08, .16);
                Sweep(b, inner, ortho, b.Range(-.35, .35));

                // Low skirt bough spreading over the knuckles.
                double lowReach = radius * b.Range(.38, .50);
                var lowDir = PlantMath.Horizontal(az + b.Range(-.45, .45));
                int low = b.AddAxis(limb, .26, lowDir * .85 + Vec3.Up * .22, lowReach,
                    Vec3.Down * .12, limbR * .44, limbR * .12, 2, Tissue.Wood, .06, .16);
                Sweep(b, low, PlantMath.Horizontal(az + Math.PI / 2), b.Range(-.40, .40));

                // Crooked side branchlets carry the lobed oak sprays.
                foreach (var (host, n, t0) in new[] { (outer, 4, .30), (inner, 3, .35), (low, 2, .45) })
                {
                    for (int j = 0; j < n; j++)
                    {
                        double t = Math.Clamp(t0 + (1 - t0) * (j + .5) / n + b.Range(-.05, .05), .2, .97);
                        var tan = b.TangentAt(host, t); var side = SideOf(tan);
                        var d = tan * .55 + side * ((j % 2 == 0 ? 1 : -1) * b.Range(.55, .95)) + Vec3.Up * b.Range(.25, .7);
                        int twig = b.AddAxis(host, t, d, radius * b.Range(.18, .28),
                            side * b.Range(-.3, .3) + Vec3.Up * b.Range(-.1, .25), b[host].R1 * 1.4, .0012, 3, Tissue.Wood, .12, .16);
                        Foliage(b, leaf, twig, 7, .10, .99, .15, ref phyl, petioleLen: .012, perOverride: 2);
                    }
                    Foliage(b, leaf, host, 5, .72, .99, .15, ref phyl, petioleLen: .012, perOverride: 2);
                }
            }
        }

        private void DesignWitchberry(PlanBuilder b, ushort leaf, ushort ornament)
        {
            // Consecutive bent sections form one continuous, uneven veteran trunk.
            // Short, thick wooden nodes are real branch parents rather than surface decals.
            double rot = b.Range(0, Math.Tau), tr = Math.Max(.025, height * .065);
            var h = PlantMath.Horizontal(rot);
            var side = PlantMath.Horizontal(rot + Math.PI / 2);
            int[] trunk = new int[7];
            // An irregular spatial path and shared endpoint tangents make the bole a
            // single continuous piece of wood, with swellings built into its profile.
            var points = new[] {
                -Vec3.Up * .035, Vec3.Up * .075 + h * .095,
                Vec3.Up * .15 + h * .19 + side * .10,
                Vec3.Up * .205 + h * .03 + side * .24,
                Vec3.Up * .175 - h * .02 + side * .26,
                Vec3.Up * .26 - h * .07 + side * .06,
                Vec3.Up * .345 - h * .03 - side * .16,
                Vec3.Up * .32 + h * .145 - side * .05
            };
            var tangents = new[] {
                Vec3.Up * .11, Vec3.Up * .08 + h * .15,
                Vec3.Up * .06 + side * .15,
                -h * .06 + side * .08,
                -h * .05 + side * .05,
                Vec3.Up * .12 - side * .15,
                Vec3.Up * .04 + h * .19,
                -Vec3.Up * .035 + h * .10 + side * .12
            };
            double[] radii = { tr * 1.25, tr * .94, tr * 1.09, tr * .82,
                tr * 1.00, tr * .72, tr * .84, tr * .59 };
            for (int n = 0; n < trunk.Length; n++)
            {
                trunk[n] = b.AddAxis(n == 0 ? -1 : trunk[n - 1], n == 0 ? 0 : 1,
                    Vec3.Up, height * .08, Vec3.Zero, radii[n], radii[n + 1],
                    (ushort)n, Tissue.Wood, .025 * n, .16,OrganFlags.Continuous);
                var section = b[trunk[n]];
                section.P0 = n == 0 ? points[0] * height : b[trunk[n - 1]].P1;
                section.P1 = points[n + 1] * height;
                section.T0 = n == 0 ? tangents[n] * height : b[trunk[n - 1]].T1;
                section.T1 = tangents[n + 1] * height;
                section.R0 = radii[n];
                section.R1 = radii[n + 1];
                section.AxisGnarling = n == 2 || n == 4 ? .48 : .28 + .04 * (n % 3);
            }
            for (int i = 0; i < 4; i++)
            {
                double a = rot + i * Math.Tau / 4 + b.Range(-.45, .45);
                var d = PlantMath.Horizontal(a);
                int root = b.AddAxis(trunk[0], .18, d + Vec3.Down * .7, tr * b.Range(.95,1.50),
                    Vec3.Down * .8, tr * b.Range(.34, .46), tr * .18, 1, Tissue.RootBark, 0, .2);
                var r = b[root];
                r.P0 = Vec3.Up * (tr * .28) + d * (tr * .35);
                r.P1 = d * (tr * b.Range(1.3, 1.9)) + Vec3.Down * (tr * b.Range(.25, .45));
                double rootChord = (r.P1 - r.P0).Length;
                r.T0 = PlantMath.SafeNormal(Vec3.Down * .65 + d * .75, Vec3.Down) * rootChord;
                r.T1 = PlantMath.SafeNormal(d * .85 + Vec3.Down * .35, d) * rootChord;
                r.AxisGnarling = b.Range(.16, .30);
            }

            ushort berry = b.AddShape(new OrganShape {
                Name = "witchberry_red_berry", Type = OrganType.Fruit,
                SurfaceForm = OrganSurfaceForm.Revolved,
                WidthProfile = PlantShapes.Profile(t => .5 * Math.Sqrt(Math.Max(0, 1 - (2 * t - 1) * (2 * t - 1)))),
                ColorAdaxial = PlantShapes.Srgb(0.34,0.072,0.08),
                ColorAbaxial = PlantShapes.Srgb(0.26,0.055,0.06), Roughness = .18f
            });
            ushort fanLeaf = b.AddShape(new OrganShape {
                Name = "witchberry_ginkgo_fan", Type = OrganType.Leaf,
                SurfaceForm = OrganSurfaceForm.Lamina,
                WidthProfile = PlantShapes.Profile(t => .5 * Math.Sin(Math.PI * .5 * t)
                    * (t > .78 ? 1 - .42 * Math.Pow((t - .78) / .22, 2) : 1), 17),
                ColorAdaxial = PlantShapes.Srgb(.32, .48, .22),
                ColorAbaxial = PlantShapes.Srgb(.28, .42, .20), Roughness = .65f, Transmission = .32f,
                TipNotchDepth = .16f, TipNotchWidth = .14f
            });
            for (int i = 0; i < 10; i++)
            {
                double a = rot + i * 2.3999632297 + b.Range(-.32, .32);
                var d = PlantMath.Horizontal(a);
                var s = PlantMath.Horizontal(a + Math.PI / 2);
                int host = trunk[1 + i % 6];
                double at = .25 + .19 * (i % 4);
                // A short stout swelling grows out of the trunk centerline; the limb continues from it.
                double burlRadius = tr * (i == 0 ? b.Range(.52, .60) : i == 1 ? b.Range(.34, .42) : b.Range(.22, .32));
                double burlLen = tr * b.Range(.58, .82);
                int burl = b.AddAxis(host, at, d * .55 + Vec3.Up * .50, burlLen,
                    s * b.Range(-.25, .25), burlRadius, burlRadius * .54, 1, Tissue.Wood, .04, .2);
                var hostOrg = b[host];
                b[burl].P0 = PlantMath.Hermite(hostOrg.P0, hostOrg.T0, hostOrg.P1, hostOrg.T1, at);
                b[burl].P1 = b[burl].P0 + (b[burl].T0 + b[burl].T1) * .5;
                b[burl].R0 = burlRadius;
                b[burl].R1 = burlRadius * .54;
                b[burl].Flags |= OrganFlags.Continuous;
                // Pinch the short branch collar unevenly along its length. The two
                // larger collars remain distinct from the smaller branch origins.
                b[burl].AxisWaist = i < 2 ? .28 : .38;
                b[burl].AxisGnarling = i < 2 ? .9 : (i % 3 == 0 ? .24 : 0);
                int limb = b.AddAxis(burl, 1, d * b.Range(.65, 1.0) + Vec3.Up * b.Range(-.10, .55),
                    radius * b.Range(.32, .56), -d * b.Range(.25, .75) + s * b.Range(-.8, .8),
                    burlRadius * .54, .004, 1, Tissue.Wood, .05, .2);
                b[limb].R0 = b[burl].R1;
                b[limb].T0 = b[burl].T1;
                b[limb].AxisWaist = b.Range(.18, .48);
                b[limb].AxisGnarling = i % 3 == 1 ? .30 : .16;
                Sweep(b, limb, s, b.Range(-1.0, 1.0));
                // The successor shares the exact end ring and tangent; Attach() normally
                // places a lateral branch on the parent surface instead.
                b[limb].Flags |= OrganFlags.Continuous;
                b[limb].P0 = b[burl].P1;
                b[limb].R0 = b[burl].R1;
                b[limb].T0 = PlantMath.SafeNormal(b[burl].T1, d) * (b[limb].P1 - b[limb].P0).Length;
                for (int j = 0; j < 6; j++)
                {
                    double t = Math.Clamp(.20 + j * .14 + b.Range(-.045, .045), .16, .97);
                    var outward = PlantMath.Horizontal(a + (j % 2 == 0 ? .65 : -.65));
                    var cross = PlantMath.Horizontal(a + (j % 2 == 0 ? 2.1 : -2.1));
                    int twig = b.AddAxis(limb, t, outward + Vec3.Up * b.Range(.15, .55), radius * b.Range(.18,.32),
                        Vec3.Up * b.Range(-.20, .35) + cross * b.Range(-.65, .65), .005, .001, 2, Tissue.Wood, .09, .17);
                    Sweep(b, twig, cross, b.Range(-.65, .65));
                    for (int q = 0; q < 4; q++)
                    {
                        double qt = .22 + q * .25;
                        var tan = b.TangentAt(twig, qt);
                        var direction = tan + SideOf(tan) * (q % 2 == 0 ? .8 : -.8) + Vec3.Up * .2;
                        for (int k = -1; k <= 1; k++)
                        {
                            var fanDir = direction + SideOf(tan) * (k * .55) + tan * (k == 0 ? .4 : 0);
                            int petiole = b.AddAxis(twig, qt, fanDir, .022, Vec3.Down * .15,
                                .0012, .0006, 3, Tissue.Green, .14, .12, OrganFlags.None, OrganType.Petiole);
                            double size = b.Range(.072, .098);
                            b.AddFlat(petiole, 1, fanDir, size, size * 1.15, .0004,
                                OrganType.Leaf, fanLeaf, 4, .15, .14);
                        }
                    }
                    // One branched stalk per limb carries a distinct cluster of round fruit.
                    if (j != 4 || i % 2 != 0) continue;
                    int stem = b.AddAxis(twig, .86, Vec3.Down + outward * .3, .035,
                        outward * .15, .0015, .0008, 3, Tissue.Wood, .1, .12);
                    int count = 5 + i % 3;
                    for (int q = 0; q < count; q++)
                    {
                        double ba = q * Math.Tau / count + b.Range(-.15, .15);
                        var radial = PlantMath.Horizontal(ba);
                        int pedicel = b.AddAxis(stem, .28 + .65 * (q % 3) / 2.0,
                            radial * .75 + Vec3.Down * .55, .016 + .003 * (q % 2), Vec3.Down * .15,
                            .0006, .0003, 4, Tissue.Wood, .1, .1);
                        double size = b.Range(.012, .017);
                        b.AddFlat(pedicel, 1, Vec3.Down, size * 2, size, .001,
                            OrganType.Fruit, berry, 4, .2, .18);
                    }
                }
            }
        }

        private void DesignKinkcane(PlanBuilder b, ushort leaf, ref double phyl)
        {
            // Straight sentinel canes contrast with angular, alternating bamboo-like internodes.
            int canes = 10;
            double caneR = Math.Max(.005, height * .028 * f.Girth / Math.Sqrt(canes));
            double baseRot = b.Range(0, Math.Tau);
            for (int c = 0; c < canes; c++)
            {
                bool palmTop = c % 4 == 0;
                double az = baseRot + c * Math.Tau / canes + b.Range(-.35, .35);
                var h = PlantMath.Horizontal(az);
                var s = PlantMath.Horizontal(az + Math.PI / 2);
                double len = height * (palmTop ? b.Range(.95, 1.12) : b.Range(.68, .96));
                // Separate straight axes preserve sharp elbows; bending a single axis smooths them away.
                int[] joints = new int[6];
                double jointRadius = caneR;
                for (int j = 0; j < joints.Length; j++)
                {
                    double sign = j % 2 == 0 ? 1 : -1;
                    var direction = palmTop
                        ? Vec3.Up + h * (.06 + sign * .10) + s * (sign * .05)
                        : Vec3.Up + h * (sign * b.Range(.30,.70) + b.Range(.04,.22))
                            + s * (sign * b.Range(-.32,.32));
                    double tipRadius = caneR * (1 - .055 * (j + 1));
                    joints[j] = b.AddAxis(j == 0 ? -1 : joints[j - 1], 1, direction,
                        len / joints.Length, Vec3.Zero, jointRadius, tipRadius,
                        (ushort)j, Tissue.Wood, .01 * c + .015 * j, .16, rootOrigin: j == 0 ? h * (radius * b.Range(.10,.26)) : Vec3.Zero);
                    // These axes are consecutive internodes, not lateral branches: bypass
                    // AddAxis's branch-radius clamp so the culm tapers gently through joints.
                    b[joints[j]].R0 = jointRadius;
                    b[joints[j]].R1 = tipRadius;
                    jointRadius = tipRadius;
                }
                int seg1 = joints[4], seg2 = joints[5];

                if (palmTop)
                {
                    // Lush multi-tiered palmate crown of broad radiating leaves on curved petioles.
                    for (int k = 0; k < 6; k++)
                    {
                        double baz = az + k * Math.Tau / 6 + b.Range(-.25, .25);
                        var bo = PlantMath.Horizontal(baz);
                        int pet = b.AddAxis(seg2, b.Range(.85, .98), bo * .95 + Vec3.Up * b.Range(.35, .75),
                            radius * b.Range(.26, .42), Vec3.Down * .45, .0026, .0012, 3, Tissue.Green, .12, .14);
                        Foliage(b, leaf, pet, 3, .65, 1.0, .14, ref phyl, leafScale: 1.05, perOverride: 2);
                    }
                }
                else
                {
                    // Dense feathery nodal branchlet sprays on upper 35% of the bowing cane.
                    for (int n = 0; n < 5; n++)
                    {
                        int host = n == 0 ? joints[3] : n < 3 ? seg1 : seg2;
                        double nt = n == 0 ? .68 : n < 3 ? .28+.30*(n-1) : .25+.36*(n-3);
                        for (int sideSign = -1; sideSign <= 1; sideSign += 2)
                        {
                            double baz = az + sideSign * b.Range(.65, 1.25);
                            var bo = PlantMath.Horizontal(baz);
                            int spr = b.AddAxis(host, nt, bo * .85 + Vec3.Up * b.Range(.35, .75),
                                radius * b.Range(.22, .36), Vec3.Down * .32 + s * (sideSign * .2),
                                .0022, .0008, 3, Tissue.Green, .11, .14);
                            Foliage(b, leaf, spr, 4, .25, 1.0, .13, ref phyl, petioleLen: .012, leafScale: .72, perOverride: 2);
                        }
                    }
                    Foliage(b, leaf, seg2, 4, .45, 1.0, .13, ref phyl, petioleLen: .012, leafScale: .75, perOverride: 2);
                }
            }
        }

        // Each fan shares one origin, preserving the distinctive palmate silhouette.
        private void LaceFan(PlanBuilder b, ushort leaf, int axis, double t)
        {
            var forward = b.TangentAt(axis, t);
            var side = SideOf(forward);
            for (int l = -2; l <= 2; l++)
            {
                double angle = l * .32;
                var dir = forward * Math.Cos(angle) + side * Math.Sin(angle) + Vec3.Down * (.05 + .025 * Math.Abs(l));
                double len = Math.Min(height * .24, .105) * (1 - .07 * Math.Abs(l)) * b.Range(.92, 1.08);
                b.AddFlat(axis, t, dir, len, len * .34, .0004, OrganType.Leaf, leaf, 4, .14, .16);
            }
        }

        private void DesignLacemantle(PlanBuilder b, ushort leaf, ref double phyl)
        {
            // Cornus alternifolia: sinuous pagoda trunk carrying 5-6 horizontal 120-deg tiers of upturned sweeping limbs packed with dense lacy horizontal foliage shelves + upright apex spray.
            double trunkR = Math.Max(.010, height * .028);
            double leanAz = b.Range(0, Math.Tau);
            var hL = PlantMath.Horizontal(leanAz);
            var sL = PlantMath.Horizontal(leanAz + Math.PI / 2);
            int s0 = b.AddAxis(-1, 0, Vec3.Up + hL * .25, height * .46, hL * -.55 + sL * .45, trunkR, trunkR * .65, 0, Tissue.Wood, 0, .20);
            b[s0].AxisGnarling = .38;
            // A few shallow roots widen the thin bole into a gradual foot.
            for (int r = 0; r < 4; r++)
            {
                double ra = leanAz + .5 + r * Math.Tau / 4 + b.Range(-.35, .35);
                var ro = PlantMath.Horizontal(ra);
                var rsv = PlantMath.Horizontal(ra + Math.PI / 2);
                int root = b.AddAxis(s0, b.Range(.02, .09), ro * .62 + Vec3.Down, height * b.Range(.05, .10),
                    rsv * b.Range(-.30, .30) + Vec3.Down * .2,
                    trunkR * b.Range(.38, .55), trunkR * b.Range(.10, .16), 1, Tissue.RootBark, 0, .18);
                var foot = b[root];
                foot.P1 = ro * (radius * b.Range(.10, .20)) + rsv * (radius * b.Range(-.04, .04))
                    + Vec3.Down * (height * b.Range(.006, .014));
                double chordLen = (foot.P1 - foot.P0).Length;
                foot.T0 = PlantMath.SafeNormal(foot.T0, Vec3.Down) * chordLen;
                foot.T1 = PlantMath.SafeNormal(foot.T1, ro) * chordLen;
                foot.AxisGnarling = b.Range(.12, .28);
            }
            int s1 = b.AddAxis(s0, .96, b.TangentAt(s0, 1.0) + sL * -.42, height * .42, hL * .48 + sL * -.35,
                trunkR * .54, trunkR * .24, 1, Tissue.Wood, .04, .20);
            b[s1].AxisGnarling = .28;
            ushort pollen = b.AddShape(new OrganShape {
                Name = "lacemantle_pollen", Type = OrganType.Flower,
                SurfaceForm = OrganSurfaceForm.Revolved,
                WidthProfile = PlantShapes.Profile(t => .5 * Math.Sin(Math.PI * t)),
                ColorAdaxial = PlantShapes.Srgb(.57, .34, .66), ColorAbaxial = PlantShapes.Srgb(.39, .22, .51),
                Roughness = .8f
            });

            // Upright lush apex spray.
            for (int a = 0; a < 3; a++)
            {
                double aaz = leanAz + a * Math.Tau / 3 + b.Range(-.3, .3);
                int topBr = b.AddAxis(s1, .88 + .05 * (a % 2), PlantMath.Horizontal(aaz) * .55 + Vec3.Up,
                    radius * .28, PlantMath.Horizontal(aaz) * .35, .0035, .0014, 2, Tissue.Wood, .10, .15);
                for (int n = 0; n < 3; n++) LaceFan(b, leaf, topBr, .4 + n * .28);
            }

            // 6 rotating 120-degree pagoda branch tiers.
            double tierRot = b.Range(0, Math.Tau);
            for (int tier = 0; tier < 6; tier++)
            {
                int host = tier < 3 ? s0 : s1;
                double tt = Math.Clamp((tier < 3 ? .38 + .25 * tier : .16 + .30 * (tier - 3)) + b.Range(-.08,.08),.08,.96);
                double u = tier / 5.0;
                double tierRadius = radius * (1.05 - .48 * u);
                for (int bIdx = 0; bIdx < 3; bIdx++)
                {
                    double az = tierRot + tier * (Math.Tau / 3 * .5) + bIdx * (Math.Tau / 3) + b.Range(-.48,.48);
                    var bo = PlantMath.Horizontal(az);
                    var bs = PlantMath.Horizontal(az + Math.PI / 2);
                    // Horizontal sweeping limb that gently upturns toward the tip like Cornus alternifolia.
                    int limb = b.AddAxis(host, tt, bo * 1.0 + Vec3.Up * b.Range(.06, .22) + bs * b.Range(-.22, .22), tierRadius * b.Range(.76, 1.12),
                        Vec3.Up * b.Range(.12, .48) + bs * b.Range(-.65, .65), Math.Max(.003, trunkR * .38 * (1 - .35 * u)), .0018, 2, Tissue.Wood, .07, .18);
                    b[limb].AxisGnarling = b.Range(.12, .30);

                    // Horizontal flat fan of secondary twigs carrying dense lacy terminal foliage.
                    for (int tw = 0; tw < 5; tw++)
                    {
                        double twT = .22 + .72 * tw / 4.0;
                        double twAz = az + (tw % 2 == 0 ? 1 : -1) * b.Range(.65, 1.05);
                        var twO = PlantMath.Horizontal(twAz);
                        int twig = b.AddAxis(limb, twT, twO * .95 + Vec3.Up * .12, tierRadius * b.Range(.22,.50) * (1 - .3 * twT),
                            Vec3.Up * .20 + PlantMath.Horizontal(twAz + 1.5) * b.Range(-.2, .2), .0025, .0010, 3, Tissue.Green, .11, .14);
                        for (int n = 0; n < 4; n++) LaceFan(b, leaf, twig, .22 + n * .25);
                        // A subset of twig tips bears a hanging, branching pollen raceme.
                        if ((tier + bIdx + tw) % 3 == 0)
                        {
                            int stalk = b.AddAxis(twig, .97, Vec3.Down + twO * .15, height * b.Range(.09, .13),
                                Vec3.Down * .4, .0012, .00065, 4, Tissue.Green, .16, .14);
                            for (int n = 0; n < 5; n++)
                            {
                                double pt = .22 + n * .18;
                                var side = PlantMath.Horizontal(twAz + (n % 2 == 0 ? 1 : -1) * 1.2);
                                int ped = b.AddAxis(stalk, pt, Vec3.Down + side * .55, height * .018,
                                    Vec3.Down * .3, .0006, .00035, 5, Tissue.Green, .18, .10);
                                b.AddFlat(ped, 1, Vec3.Down, height * .025, height * .009, .0004,
                                    OrganType.Flower, pollen, 6, .20, .12);
                            }
                        }
                    }
                    LaceFan(b, leaf, limb, .96);
                }
            }
        }

        private void DesignLanternbrush(PlanBuilder b, ushort leaf, ushort ornament, ref double phyl)
        {
            // Staphylea trifolia: tall wine-dark fountain-arching stems (2-segment rise then graceful bow) with opposite trifoliate leaves & hanging bladder-lanterns.
            int stems = 6;
            double stemR = Math.Max(.008, height * .026 / Math.Sqrt(stems));
            double baseRot = b.Range(0, Math.Tau);
            for (int c = 0; c < stems; c++)
            {
                double az = baseRot + c * Math.Tau / stems + b.Range(-.36, .36);
                var h = PlantMath.Horizontal(az);
                var s = PlantMath.Horizontal(az + Math.PI / 2);
                double len = height * b.Range(.62,1.12);
                int s0 = b.AddAxis(-1, 0, Vec3.Up + h * b.Range(.10,.30), len * .55,
                    h * b.Range(.05, .16) + s * b.Range(-.16, .16), stemR, stemR * .65, 0, Tissue.Wood, .01 * c, .20);
                // Independent shoots emerge across a broad stool rather than one shared point.
                var baseOffset = h * (radius * b.Range(.16, .30));
                b[s0].P0 += baseOffset;
                b[s0].P1 += baseOffset;
                int s1 = b.AddAxis(s0, .96, b.TangentAt(s0, 1.0) + h * .12, len * .45,
                    Vec3.Down * b.Range(.75, 1.15) + h * .3 + s * b.Range(-.3, .3), stemR * .54, .0025, 1, Tissue.Wood, .05, .20);

                // Opposite curved lateral branches along the arching stem.
                for (int k = 0; k < 4; k++)
                {
                    int host = k < 2 ? s0 : s1;
                    double bt = Math.Clamp((k < 2 ? .52+.36*k : .25+.50*(k-2))+b.Range(-.08,.08),.12,.95);
                    for (int sideSign = -1; sideSign <= 1; sideSign += 2)
                    {
                        double baz = az + sideSign * b.Range(.75, 1.15);
                        var bo = PlantMath.Horizontal(baz);
                        var bs = PlantMath.Horizontal(baz + Math.PI / 2);
                        int br = b.AddAxis(host, bt, bo * .85 + Vec3.Up * b.Range(.35, .75), radius * b.Range(.36, .58),
                            Vec3.Down * b.Range(.35, .65) + bs * b.Range(-.25, .25), stemR * .32, .0014, 2, Tissue.Wood, .09, .16);
                        // Opposite trifoliate compound leaves along the lateral branch.
                        for (int nd = 0; nd < 3; nd++)
                        {
                            double nt = .28 + .64 * nd / 2.0;
                            for (int pSign = -1; pSign <= 1; pSign += 2)
                            {
                                var ldir = PlantMath.Horizontal(baz + pSign * 1.1) * .9 + Vec3.Up * .25;
                                AddCompoundLeaf(b, leaf, br, nt, ldir, b.Range(.045, .07), Vec3.Down * .25, 1,
                                    b.Range(.09, .14), .36, 3, .13);
                            }
                        }
                        // Pendant bladder-lanterns hanging from slender curved pedicels.
                        if ((k + (sideSign > 0 ? 1 : 0)) % 2 == 0)
                        {
                            int ped = b.AddAxis(br, .72, Vec3.Down + bo * .25, .055, bo * .2, .0012, .0008, 3, Tissue.Green, .15, .14);
                            b.AddFlat(ped, 1.0, Vec3.Down * .95 + bo * .15, .065, .028, .001, OrganType.Fruit, ornament, 4, .18, .16);
                        }
                    }
                }
                AddCompoundLeaf(b, leaf, s1, .96, h * .8 + Vec3.Up * .2, .06, Vec3.Down * .25, 1, .12, .36, 2, .13);
            }
        }

        private void DesignMiremyrtle(PlanBuilder b, ushort leaf, ushort ornament, ref double phyl)
        {
            // Myrica gale: dense twiggy bog-myrtle bush with curving ascending stems, fine twigs packed with spiral oblanceolate leaves, and upright catkins.
            int stems = 8;
            double stemR = Math.Max(.005, height * .028 / Math.Sqrt(stems));
            double baseRot = b.Range(0, Math.Tau);
            for (int c = 0; c < stems; c++)
            {
                double az = baseRot + c * Math.Tau / stems + b.Range(-.38, .38);
                var h = PlantMath.Horizontal(az);
                var s = PlantMath.Horizontal(az + Math.PI / 2);
                double len = height * b.Range(.75, 1.05);
                var (s0, s1) = CurvedStem(b, Vec3.Up + h * b.Range(.22, .45), len,
                    h * b.Range(.10, .28) + s * b.Range(-.32, .32),
                    h * b.Range(-.25, .15) + s * b.Range(-.32, .32), stemR, stemR * .32, 0.48, .01 * c, .20, h * radius * b.Range(.10,.30));

                for (int k = 0; k < 4; k++)
                {
                    int host = k < 2 ? s0 : s1;
                    double bt = k < 2 ? .40 + .42 * k : .20 + .55 * (k - 2);
                    double baz = az + (k % 2 == 0 ? 1 : -1) * b.Range(.55, 1.05);
                    var bo = PlantMath.Horizontal(baz);
                    var bs = PlantMath.Horizontal(baz + Math.PI / 2);
                    int br = b.AddAxis(host, bt, bo * .75 + Vec3.Up * b.Range(.55, .95), radius * b.Range(.40, .65),
                        bs * b.Range(-.32, .32) + Vec3.Up * .15, stemR * .34, .0014, 2, Tissue.Wood, .07, .16);
                    for (int tw = 0; tw < 2; tw++)
                    {
                        double twAz = baz + (tw == 0 ? .85 : -.85);
                        int twig = b.AddAxis(br, .35 + .48 * tw, PlantMath.Horizontal(twAz) * .8 + Vec3.Up * .65,
                            radius * b.Range(.18,.42), PlantMath.Horizontal(twAz + 1.5) * b.Range(-.3, .3),
                            .0018, .0009, 3, Tissue.Green, .11, .14);
                        Foliage(b, leaf, twig, 6, .15, .99, .13, ref phyl, leafScale: 1.08, perOverride: 2);
                    }
                    Foliage(b, leaf, br, 5, .30, .99, .12, ref phyl, leafScale: 1.05, perOverride: 2);
                    if (k >= 2)
                        b.AddFlat(br, .88, Vec3.Up + bo * .15, .042, .014, .001, OrganType.Fruit, ornament, 3, .17, .15);
                }
                Foliage(b, leaf, s1, 4, .45, 1.0, .12, ref phyl, leafScale: 1.05, perOverride: 2);
            }
        }

        private void DesignNutglade(PlanBuilder b, ushort leaf, ushort ornament, ref double phyl)
        {
            // Corylus avellana: multi-stemmed coppice stool with sinuous stems, zig-zagging hazel branches, large broad scalloped leaves on petioles, and dangling catkins/nuts.
            int stems = 7;
            double stemR = Math.Max(.008, height * .028 / Math.Sqrt(stems));
            double baseRot = b.Range(0, Math.Tau);
            for (int c = 0; c < stems; c++)
            {
                double az = baseRot + c * Math.Tau / stems + b.Range(-.35, .35);
                var h = PlantMath.Horizontal(az);
                var s = PlantMath.Horizontal(az + Math.PI / 2);
                double len = height * b.Range(.76, 1.05);
                var (s0, s1) = CurvedStem(b, Vec3.Up + h * b.Range(.04,.12), len,
                    s * b.Range(-.32, .32) + h * b.Range(-.15, .20),
                    h * b.Range(-.28, .15) + s * b.Range(-.32, .32), stemR, stemR * .35, 0.50, .01 * c, .24);
                // Move both sections together; their attached branches follow the
                // translated upper section when they are subsequently authored.
                var baseOffset = h * (radius * b.Range(.16, .30));
                b[s0].P0 += baseOffset; b[s0].P1 += baseOffset;
                b[s1].P0 += baseOffset; b[s1].P1 += baseOffset;

                for (int k = 0; k < 4; k++)
                {
                    int host = k < 2 ? s0 : s1;
                    double bt = k < 2 ? .42 + .42 * k : .22 + .56 * (k - 2);
                    double baz = az + (k % 2 == 0 ? 1 : -1) * b.Range(.60, 1.15);
                    var bo = PlantMath.Horizontal(baz);
                    var bs = PlantMath.Horizontal(baz + Math.PI / 2);
                    int br = b.AddAxis(host, bt, bo * .85 + Vec3.Up * b.Range(.45, .85), radius * b.Range(.45, .72),
                        bs * b.Range(-.40, .40) + Vec3.Down * b.Range(.08, .28), stemR * .35, .0016, 2, Tissue.Wood, .08, .18);
                    for (int tw = 0; tw < 3; tw++)
                    {
                        double twT = .28 + .62 * tw / 2.0;
                        double twAz = baz + (tw % 2 == 0 ? 1 : -1) * b.Range(.7, 1.1);
                        int twig = b.AddAxis(br, twT, PlantMath.Horizontal(twAz) * .85 + Vec3.Up * b.Range(.15, .55),
                            radius * b.Range(.24, .38), PlantMath.Horizontal(twAz + 1.5) * b.Range(-.35, .35) + Vec3.Down * .18,
                            .0024, .0010, 3, Tissue.Green, .12, .14);
                        Foliage(b, leaf, twig, 4, .20, 1.0, .14, ref phyl, petioleLen: .018, leafScale: 1.18, perOverride: 2);
                    }
                    Foliage(b, leaf, br, 4, .22, 1.0, .13, ref phyl, petioleLen: .018, leafScale: 1.18, perOverride: 2);
                    if (k >= 2)
                        b.AddFlat(br, .75, Vec3.Down + bo * .25, .065, .016, .001, OrganType.Fruit, ornament, 3, .18, .16);
                }
                Foliage(b, leaf, s1, 3, .55, 1.0, .13, ref phyl, petioleLen: .018, leafScale: 1.15, perOverride: 2);
            }
        }

        private void DesignRainshield(PlanBuilder b, ushort leaf)
        {
            // Darmera peltata: rhizomatous colony of 26 gracefully curving fleshy petioles rising to staggered heights, each crowned with a broad cupped horizontal umbrella leaf.
            ushort ridge=b.AddShape(new OrganShape { Name="rainshield_ribbed_stem", Type=OrganType.Flower,
                SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t => .5),
                ColorAdaxial=PlantShapes.Srgb(.22,.38,.18), ColorAbaxial=PlantShapes.Srgb(.14,.27,.11), Roughness=.52f });
            int count = 26;
            double petR = Math.Max(.006, height * .014);
            double baseRot = b.Range(0, Math.Tau);
            int rhizome = b.AddAxis(-1, 0, Vec3.Up, .035, Vec3.Zero, petR * 1.4, petR * 1.2, 0, Tissue.Green, 0, .10, OrganFlags.Hidden);
            for (int i = 0; i < count; i++)
            {
                double az = baseRot + i * 2.399963 + b.Range(-.22, .22);
                var h = PlantMath.Horizontal(az);
                var s = PlantMath.Horizontal(az + Math.PI / 2);
                double frac = (i + .5) / count;
                double reach = radius * (.30 + .48*Math.Sqrt(frac));
                int runner = b.AddAxis(rhizome, .8, h + Vec3.Up * .05, Math.Max(.02, reach), Vec3.Zero,
                    petR * .9, petR * .75, 1, Tissue.Green, .01, .12, OrganFlags.Hidden);
                double stalkH = height * b.Range(.30,1.05);
                var sDir = Vec3.Up + h * b.Range(.14, .42) + s * b.Range(-.14, .14);
                var sBend = h * b.Range(-.35, .45) + s * b.Range(-.55, .55);
                int petiole = b.AddAxis(runner, .95, sDir, stalkH, sBend, petR * .62, petR * .42, 2, Tissue.Green, .02 + .004 * i, .24);
                Sweep(b, petiole, s, b.Range(-.65, .65));
                // Short ribbed sections follow the curved petiole instead of drawing a straight rod beside it.
                for (int part=0;part<4;part++)
                {
                    double t=part*.25;
                    var stalk=b[petiole];
                    var span=PlantMath.Hermite(stalk.P0,stalk.T0,stalk.P1,stalk.T1,t+.25)
                        -PlantMath.Hermite(stalk.P0,stalk.T0,stalk.P1,stalk.T1,t);
                    b.AddFlat(petiole,t,span,span.Length,petR*(.65-.12*t),.0004,OrganType.Flower,ridge,3,.03,.24);
                }
                // A narrow extension carries the blade into the petiole tip.
                int joint=b.AddAxis(petiole,1,b.TangentAt(petiole,1),petR*.7,Vec3.Zero,petR*.38,petR*.08,3,Tissue.Green,.03,.08,OrganFlags.Basal);
                // Broad overlapping shields form a cohesive, staggered canopy.
                double bladeLen = b.Range(.68, .88);
                var tilt = h * b.Range(.75, 1.0) + s * b.Range(-.3, .3) + Vec3.Up * b.Range(-.25,.38);
                b.AddFlat(joint, 1.0, tilt, bladeLen, bladeLen * .58, .0007, OrganType.Leaf, leaf, 3, .14 + .004 * i, .20);
            }
        }

        private void DesignSilverfelt(PlanBuilder b, ushort leaf, ushort ornament, ref double phyl)
        {
            // Separate ascending shoots occupy a broad cushion instead of fanning
            // out from one central point.
            int stems = 9;
            double stemR = Math.Max(.005, height * .030 / Math.Sqrt(stems));
            double baseRot = b.Range(0, Math.Tau);
            for (int c = 0; c < stems; c++)
            {
                double az = baseRot + c * Math.Tau / stems + b.Range(-.45,.45);
                var h = PlantMath.Horizontal(az);
                var s = PlantMath.Horizontal(az + Math.PI / 2);
                double len = height * b.Range(.48,1.02);
                var origin = h * (radius * b.Range(.25, .55));
                int s0 = b.AddAxis(-1, 0, Vec3.Up + h * b.Range(.10,.42), len * .52,
                    h * .08 + s * b.Range(-.15, .15), stemR, stemR * .65, 0, Tissue.Wood, .01 * c, .20,
                    rootOrigin: origin);
                int s1 = b.AddAxis(s0, .95, b.TangentAt(s0, 1.0) + Vec3.Up * .45 + s * b.Range(-.3, .3),
                    len * .46, s * b.Range(-.35, .35) + h * -.25, stemR * .54, .0016, 1, Tissue.Wood, .05, .18);

                for (int k = 0; k < 3; k++)
                {
                    int host = k == 0 ? s0 : s1;
                    double bt = k == 0 ? .60 : .30 + .45 * (k - 1);
                    double baz = az + (k % 2 == 0 ? 1 : -1) * b.Range(.55, 1.05);
                    var bo = PlantMath.Horizontal(baz);
                    var bs = PlantMath.Horizontal(baz + Math.PI / 2);
                    int br = b.AddAxis(host, bt, bo * .75 + Vec3.Up * b.Range(.65, 1.1), radius * b.Range(.28, .44),
                        bs * b.Range(-.35, .35) + Vec3.Up * .2, stemR * .32, .0012, 2, Tissue.Wood, .08, .15);
                    Foliage(b, leaf, br, 6, .18, .99, .12, ref phyl, petioleLen: .007, leafScale: 1.10, perOverride: 2);
                    if (k == 2)
                        b.AddFlat(br, .92, Vec3.Up + bo * .1, .042, .016, .001, OrganType.Fruit, ornament, 3, .17, .15);
                }
                Foliage(b, leaf, s1, 6, .25, 1.0, .12, ref phyl, petioleLen: .007, leafScale: 1.10, perOverride: 2);
            }
        }

        private void DesignSplitcrown(PlanBuilder b, ushort leaf, ref double phyl)
        {
            // Albizia julibrissin: 3 slender sinuous trunks keeping lower space open, forking into asymmetric flat-topped umbrella crowns of feathery compound-leaf sprays.
            int trunks = 3;
            double trunkR = Math.Max(.009, height * .047 / Math.Sqrt(trunks));
            double baseRot = b.Range(0, Math.Tau);
            for (int c = 0; c < trunks; c++)
            {
                double az = baseRot + c * Math.Tau / trunks + b.Range(-.45, .45);
                var h = PlantMath.Horizontal(az);
                var s = PlantMath.Horizontal(az + Math.PI / 2);
                double len = height * b.Range(.72, .96);
                var (s0, s1) = CurvedStem(b, Vec3.Up + h * b.Range(.28, .50), len,
                    s * b.Range(-.30, .30) + h * b.Range(-.1, .2),
                    h * b.Range(.15, .35) + s * b.Range(-.30, .30), trunkR, trunkR * .38, 0.56, .02 * c, .24);

                // Asymmetric high umbrella scaffold limbs from upper trunk.
                for (int k = 0; k < 4; k++)
                {
                    double baz = az + (k - 1.5) * .95 + b.Range(-.3, .3);
                    var bo = PlantMath.Horizontal(baz);
                    var bs = PlantMath.Horizontal(baz + Math.PI / 2);
                    double bt = Math.Clamp(.30+.65*k/3.0+b.Range(-.10,.10),.16,.96);
                    int limb = b.AddAxis(s1, bt, bo * .95 + Vec3.Up * b.Range(.12,.75), radius * b.Range(.46, .74),
                        Vec3.Up * .15 + bs * b.Range(-.32, .32) + Vec3.Down * .22, trunkR * .30, .0020, 2, Tissue.Wood, .08, .18);
                    for (int tw = 0; tw < 5; tw++)
                    {
                        double twT = .25 + .70 * tw / 4.0;
                        double twAz = baz + (tw - 2) * .48 + b.Range(-.32,.32);
                        var twO = PlantMath.Horizontal(twAz);
                        int twig = b.AddAxis(limb, twT, twO * .95 + Vec3.Up * .18, radius * b.Range(.22, .34),
                            Vec3.Down * .15 + PlantMath.Horizontal(twAz + 1.5) * b.Range(-.25, .25), .0018, .0009, 3, Tissue.Green, .12, .14);
                        // Pair of feathery pinnate compound leaves at each twig fan.
                        // Broad, overlapping leaflets and an extra leaf per fan fill the canopy.
                        for (int fIdx = 0; fIdx < 4; fIdx++)
                        {
                            double ft = .30 + .65 * fIdx / 3.0;
                            var fdir = PlantMath.Horizontal(twAz + (fIdx - 1.5) * .7) * .95 + Vec3.Up * .12;
                            AddCompoundLeaf(b, leaf, twig, ft, fdir, b.Range(.17, .25), Vec3.Down * .32, 5, b.Range(.058, .076), .62, 4, .15);
                        }
                    }
                    AddCompoundLeaf(b, leaf, limb, .96, bo * .95 + Vec3.Up * .1, .22, Vec3.Down * .30, 5, .070, .62, 3, .14);
                }
            }
        }

        private void DesignThornmantle(PlanBuilder b, ushort leaf, ushort ornament, ref double phyl)
        {
            // Crataegus monogyna: crooked multi-stemmed thorny thicket with zig-zagging limbs, sharp nodal spines, dense spur leaf clusters, and blossom/haw corymbs.
            int stems = 6;
            double stemR = Math.Max(.008, height * .028 / Math.Sqrt(stems));
            double baseRot = b.Range(0, Math.Tau);
            for (int c = 0; c < stems; c++)
            {
                double az = baseRot + c * Math.Tau / stems + b.Range(-.38, .38);
                var h = PlantMath.Horizontal(az);
                var s = PlantMath.Horizontal(az + Math.PI / 2);
                double len = height * b.Range(.64,1.12);
                var (s0, s1) = CurvedStem(b, Vec3.Up + h * b.Range(.08,.30), len,
                    s * b.Range(-.18, .18) + h * .08,
                    s * b.Range(-.22, .22) - h * .08, stemR, stemR * .34, 0.48, .01 * c, .24,
                    h * radius * b.Range(.16, .42));

                for (int k = 0; k < 4; k++)
                {
                    int host = k < 2 ? s0 : s1;
                    double bt = k < 2 ? .38 + .42 * k : .20 + .55 * (k - 2);
                    double baz = az + (k % 2 == 0 ? 1 : -1) * b.Range(.55, 1.15);
                    var bo = PlantMath.Horizontal(baz);
                    var bs = PlantMath.Horizontal(baz + Math.PI / 2);
                    int br = b.AddAxis(host, bt, bo * .85 + Vec3.Up * b.Range(.45, .85), radius * b.Range(.44, .70),
                        bs * b.Range(-.42, .42) + Vec3.Down * b.Range(.10, .32), stemR * .35, .0016, 2, Tissue.Wood, .07, .18);
                    // Sharp hawthorn spines at branch nodes.
                    for (int sp = 0; sp < 2; sp++)
                        b.AddAxis(br, .3 + .45 * sp, bs * (sp == 0 ? .8 : -.8) + bo * .5 + Vec3.Up * .3,
                            .022, Vec3.Zero, .0020, .0002, 3, Tissue.Bark, .10, .10);

                    for (int tw = 0; tw < 3; tw++)
                    {
                        double twT = .28 + .62 * tw / 2.0;
                        double twAz = baz + (tw % 2 == 0 ? 1 : -1) * b.Range(.75, 1.15);
                        int twig = b.AddAxis(br, twT, PlantMath.Horizontal(twAz) * .85 + Vec3.Up * b.Range(.25, .65),
                            radius * b.Range(.22, .34), PlantMath.Horizontal(twAz + 1.5) * b.Range(-.35, .35),
                            .0022, .0010, 3, Tissue.Green, .11, .14);
                        Foliage(b, leaf, twig, 5, .20, 1.0, .13, ref phyl, petioleLen: .012, leafScale: 1.10, perOverride: 2);
                    }
                    Foliage(b, leaf, br, 4, .40, 1.0, .12, ref phyl, petioleLen: .012, leafScale: 1.15, perOverride: 2);
                    // Spreading wide compound leaves crown the end of each limb.
                    for (int cl = 0; cl < 3; cl++)
                        AddCompoundLeaf(b, leaf, br, 1.0, bo * .95 + bs * ((cl - 1) * .8) + Vec3.Up * .22,
                            b.Range(.22, .30), Vec3.Down * .18, 4, b.Range(.085, .105), .58, 3, .14);
                    if (k >= 1)
                        b.AddFlat(br, .82, Vec3.Up * .5 + bo * .6, .048, .022, .001, OrganType.Flower, ornament, 3, .17, .15);
                }
                Foliage(b, leaf, s1, 4, .55, 1.0, .12, ref phyl, petioleLen: .012, leafScale: 1.10, perOverride: 2);
                for (int cl = 0; cl < 3; cl++)
                    AddCompoundLeaf(b, leaf, s1, 1.0, PlantMath.Horizontal(az + (cl - 1) * 1.1) * .95 + Vec3.Up * .25,
                        b.Range(.24, .32), Vec3.Down * .18, 4, b.Range(.09, .11), .58, 3, .14);
            }
        }

        private void DesignVaultfern(PlanBuilder b, ushort leaf)
        {
            // Dicksonia antarctica: stout subtly curved fibrous trunk crowned by a lush 2-whorl shuttlecock of 20 arching fronds with dense overlapping pinnae.
            double trunkR = Math.Max(.035, height * .055 * f.Girth);
            double leanAz = b.Range(0, Math.Tau);
            var hL = PlantMath.Horizontal(leanAz);
            var sL = PlantMath.Horizontal(leanAz + Math.PI / 2);
            int trunk = b.AddAxis(-1, 0, Vec3.Up + hL * b.Range(.06, .14), height * .68,
                hL * b.Range(-.08, .05) + sL * b.Range(-.10, .10), trunkR * 1.35, trunkR * .62, 0, Tissue.Wood, 0, .28);
            // Rising curled croziers occupy the centre of the old tree-fern crown.
            for (int c = 0; c < 5; c++)
            {
                double a = c * Math.Tau / 5 + b.Range(-.15, .15);
                var outwards = PlantMath.Horizontal(a);
                int crozier = b.AddAxis(trunk, .97, Vec3.Up + outwards * .2,
                    height * .15, outwards * .45, .004, .0025, 1, Tissue.Green, .09, .17);
                int curl = b.AddAxis(crozier, .98, outwards + Vec3.Up * .35,
                    height * .055, Vec3.Down * .8 - outwards * .3, .0025, .0014, 2, Tissue.Green, .12, .15);
                b.AddAxis(curl, .98, Vec3.Down - outwards * .6, height * .035,
                    -outwards * .65 + Vec3.Up * .4, .0014, .0007, 3, Tissue.Green, .15, .12);
            }
            int fronds = 20;
            double rot = b.Range(0, Math.Tau);
            for (int j = 0; j < fronds; j++)
            {
                bool inner = j >= 12;
                double a = rot + j * 2.399963 + b.Range(-.32,.32);
                var outward = PlantMath.Horizontal(a);
                var side = PlantMath.Horizontal(a + Math.PI / 2);
                double rise = inner ? b.Range(.40,1.18) : b.Range(-.02,.60);
                double reach = radius * (inner ? b.Range(.62,1.05) : b.Range(.78,1.32));
                double droop = inner ? b.Range(1.0, 1.5) : b.Range(1.45, 2.15);
                int rachis = b.AddAxis(trunk, .92 + .06 * (j / (double)fronds), outward + Vec3.Up * rise + side * b.Range(-.12, .12),
                    reach, Vec3.Down * droop + side * b.Range(-.15, .15), .009, .0012, 1, Tissue.Green, .05 + .004 * j, .22);
                int pairs = 18;
                for (int n = 0; n < pairs; n++)
                {
                    double nt = .10 + .86 * n / (pairs - 1.0);
                    double env = Math.Pow(Math.Sin(Math.PI * nt), .65);
                    double pinna = Math.Min(.46, radius * .28) * env;
                    var rt = b.TangentAt(rachis, nt);
                    var lat = SideOf(rt);
                    for (int sign = -1; sign <= 1; sign += 2)
                        b.AddFlat(rachis, nt, lat * sign + rt * .38 + Vec3.Down * .10, pinna, pinna * .24, .0004,
                            OrganType.Leaflet, leaf, 2, .12 + nt * .10, .18);
                }
            }
        }

        // Without simulated support, vines spread and search near their origin; no
        // imaginary trunk or trellis is introduced into the catalog representation.
        private void DesignClimbingFallback(PlanBuilder b, ushort leaf, ushort ornament)
        {
            int runners = f.Id is "bitterbine" or "trumpetbind" ? 3 : 2;
            double rotation = b.Range(0, Math.Tau);
            for (int r = 0; r < runners; r++)
            {
                double az = rotation + r * Math.Tau / runners + b.Range(-.38,.38);
                var outward = PlantMath.Horizontal(az);
                var lateral = PlantMath.Horizontal(az + Math.PI / 2);
                int parent = -1;
                int pieces = f.Id == "spiralvine" ? 9 : 7;
                double pieceLength = Math.Max(.09, Math.Min(.30, radius * .30 + height * .035));
                for (int k = 0; k < pieces; k++)
                {
                    double phase = k * (f.Id == "spiralvine" ? 1.1 : .75) + r;
                    // Low, sinuous connected runners, with a free searching tip.
                    double climb = f.Id == "clinglace"
                        ? (k == pieces - 1 ? .08 : 0.0)
                        : (k == pieces - 1 ? .48 : .08 + .12 * Math.Sin(phase));
                    var direction = outward + lateral * (Math.Sin(phase) * .42) + Vec3.Up * climb;
                    int axis = b.AddAxis(parent, parent < 0 ? 0 : 1, direction, pieceLength,
                        lateral * (.40 * Math.Cos(phase)), .0035 * (1 - .55 * k / pieces),
                        .0015, (ushort)k, Tissue.Green, .015 * k, .12, OrganFlags.Continuous);
                    // Consecutive runner sections are a continuation, not a lateral
                    // branch: AddAxis pinches a child to 0.85 of its parent's attach
                    // radius, which would thin the runner away toward its searching tip.
                    double segR = .0035 * (1 - .55 * k / (double)pieces);
                    b[axis].R0 = segR;
                    b[axis].R1 = Math.Max(.0012, segR * .93);
                    if (f.Id == "clinglace" && parent < 0)
                    {
                        b[axis].P0 = Vec3.Up * .004;
                        b[axis].P1 = b[axis].P0 + (b[axis].T0 + b[axis].T1) * .5;
                    }
                    else if (parent >= 0)
                    {
                        b[axis].P0 = b[parent].P1;
                        b[axis].R0 = b[parent].R1;
                        b[axis].P1 = b[axis].P0 + (b[axis].T0 + b[axis].T1) * .5;
                    }
                    var tangent = b.TangentAt(axis, .5);
                    var side = SideOf(tangent);
                    AddClimberNode(b, f, leaf, ornament, axis, b.Range(.34,.68), tangent, Vec3.Up, side, k);
                    parent = axis;
                }
            }
        }

        private void DesignGeneric(PlanBuilder b, ushort leaf, ushort ornament, ref double phyl)
        {
            bool arching = !f.Climber && f.Trunks > 1 && f.Droop >= .4;
            bool clearBole = !f.Climber && f.CrownBase >= .4;
            double trunkR = Math.Max(.003, height * .028 * f.Girth / Math.Sqrt(f.Trunks));
            for (int c = 0; c < f.Trunks; c++)
            {
                double az = c * Math.Tau / f.Trunks + b.Range(-.35, .35);
                var h = PlantMath.Horizontal(az);
                var hs = PlantMath.Horizontal(az + Math.PI / 2);
                double lean = f.Climber ? .06 : f.Trunks > 1 ? b.Range(.18, .42) * (arching ? 1.5 : 1) : b.Range(.06, .16);
                if (f.Habit.Contains("leaning")) lean += .35;
                if (f.Habit.Contains("crooked")) lean += .22;
                double trunkLen = height * (f.Climber ? .7 : f.CrownBase + (1 - f.CrownBase) * .55) * b.Range(.85, 1.08)
                    * (f.Trunks > 1 ? b.Range(.82, 1.08) : 1);
                if (f.Id == "shiverleaf") trunkLen *= c == 0 ? 1.12 : c == 1 ? .88 : .77;
                var dir = Vec3.Up * (f.Climber ? .45 : 1) + h * lean;
                var trunkBend = arching ? h * .55 + Vec3.Down * f.Droop * .95
                    : h * (-lean * .75) + hs * b.Range(-.24, .24) + (f.Habit.Contains("crooked") ? h * b.Range(.2, .45) : Vec3.Zero);
                int trunk = b.AddAxis(-1, 0, dir, trunkLen, trunkBend, trunkR, trunkR * .3, 0, Tissue.Wood, 0, .28);
                if (f.Id == "shiverleaf")
                {
                    // The legacy clonal trio has a dominant leader and two shorter,
                    // sinuously leaning companions rather than identical poles.
                    var stem = b[trunk];
                    stem.Flags |= OrganFlags.Continuous;
                    var chord = stem.P1 - stem.P0;
                    double sweep = (c == 1 ? -.48 : .48) * trunkLen;
                    stem.T0 = chord + hs * sweep;
                    stem.T1 = chord - hs * sweep;
                    // Each stem leaves the soil through short exposed roots instead of
                    // a flat cut end at the ground plane.
                    for (int rt = 0; rt < 3; rt++)
                    {
                        double ra = az + 1.9 + rt * Math.Tau / 3 + b.Range(-.50, .50);
                        var ro = PlantMath.Horizontal(ra);
                        var rsv = PlantMath.Horizontal(ra + Math.PI / 2);
                        double rootLen = height * b.Range(.030, .058);
                        int root = b.AddAxis(trunk, b.Range(.02, .08), ro * .60 + Vec3.Down,
                            rootLen, rsv * b.Range(-.25, .25) + Vec3.Down * .20,
                            trunkR * b.Range(.40, .58), trunkR * b.Range(.10, .16), 1,
                            Tissue.RootBark, 0, .18);
                        var foot = b[root];
                        foot.P0 = stem.P0 + Vec3.Up * (trunkR * b.Range(.30, .55)) + ro * (trunkR * .30);
                        foot.P1 = stem.P0 + ro * (trunkR * b.Range(1.8, 2.6)) + rsv * (trunkR * b.Range(-.25, .25))
                            + Vec3.Down * (trunkR * .35);
                        double chordLen = (foot.P1 - foot.P0).Length;
                        foot.T0 = PlantMath.SafeNormal(Vec3.Down * .65 + ro * .75, Vec3.Down) * chordLen;
                        foot.T1 = PlantMath.SafeNormal(ro * .85 + Vec3.Down * .30, ro) * chordLen;
                        foot.AxisGnarling = b.Range(.14, .28);
                    }
                }
                if (f.Id is "fenforge" or "mirespire")
                {
                    // Exposed buttresses and knee-like upturned roots at the waterside base.
                    int rootCount = f.Id == "mirespire" ? 7 : 4;
                    for (int r = 0; r < rootCount; r++)
                    {
                        var rd = PlantMath.Horizontal(az + r * Math.Tau / rootCount);
                        double rootLength = Math.Max(.08, radius * (f.Id == "mirespire" ? .32 : .24));
                        int root = b.AddAxis(trunk, .08, rd + Vec3.Down * .2, rootLength,
                            Vec3.Down * .35, trunkR * .45, trunkR * .16, 1, Tissue.RootBark, 0, .20);
                        if (f.Id == "mirespire")
                            b.AddAxis(root, .9, Vec3.Up + rd * .15, height * .06,
                                -rd * .2, trunkR * .15, .002, 2, Tissue.RootBark, .03, .15);
                    }
                }
                if (!f.Climber && f.Arrangement != 3) Foliage(b, leaf, trunk, Math.Max(3, f.Nodes / 2), .88, 1, .15, ref phyl, petioleLen: .012);
                for (int k = 0; k < f.Tiers; k++) for (int j = 0; j < f.Branches; j++)
                {
                    bool shiverLeader = f.Id == "shiverleaf" && k == f.Tiers - 1 && j == 0;
                    double skip = b.Draw();
                    if (f.Branches * f.Tiers >= 6 && !shiverLeader && skip < .12) continue;
                    double a = az + j * Math.Tau / f.Branches + k * 2.39996 + b.Range(-.4, .4);
                    var outward = PlantMath.Horizontal(a);
                    var side = PlantMath.Horizontal(a + Math.PI / 2);
                    double u = (k + .5) / f.Tiers;
                    double t = shiverLeader ? 1.0 : f.CrownBase + (1 - f.CrownBase) * u + b.Range(-.4, .4) * (1 - f.CrownBase) / f.Tiers;
                    double envelope = f.Needle ? 1.05 - .8 * u
                        : clearBole ? .45 + .6 * Math.Sin(Math.PI * (.2 + .7 * u))
                        : .55 + .45 * (1 - k / (double)Math.Max(1, f.Tiers));
                    double reach = radius * f.Spread * envelope * b.Range(.72, 1.18);
                    double rise = f.Rise * (.55 + .9 * u) + b.Range(-.12, .12);
                    if (clearBole && !f.Needle) rise += .55 * (1 - .5 * u);
                    var bdir = outward + Vec3.Up * rise + side * b.Range(-.24, .24);
                    double weep = f.Droop >= .5 ? 2.2 : 1.3;
                    var bbend = Vec3.Down * (f.Droop * weep * (1.3 - .6 * u))
                        + (clearBole && !f.Needle ? Vec3.Down * rise * .7 : Vec3.Zero)
                        + Vec3.Up * (.32 * (1 - f.Droop) * (f.Needle ? .5 : 1))
                        + side * b.Range(-.32, .32);
                    double br0 = Math.Max(.0025, trunkR * (f.Needle ? .28 : .4) * (1 - .4 * u));
                    int branch = b.AddAxis(trunk, Math.Clamp(t, .03, 1.0), bdir, reach, bbend,
                        shiverLeader ? b[trunk].R1 : br0, .0012, 1, Tissue.Wood, .05, .22,
                        shiverLeader ? OrganFlags.Continuous : OrganFlags.None);
                    if (shiverLeader)
                    {
                        var st = b[trunk];
                        var br = b[branch];
                        br.P0 = st.P1;
                        br.R0 = st.R1;
                        br.T0 = PlantMath.SafeNormal(st.T1, Vec3.Up) * reach;
                        br.P1 = br.P0 + (br.T0 + br.T1) * .5;
                    }
                    else if (f.Id == "shiverleaf")
                    {
                        var st = b[trunk];
                        var br = b[branch];
                        br.P0 = PlantMath.Hermite(st.P0, st.T0, st.P1, st.T1, br.AttachT) + outward * (trunkR * .15);
                        br.P1 = br.P0 + (br.T0 + br.T1) * .5;
                    }
                    if (f.Arrangement == 3)
                    {
                        Foliage(b, leaf, branch, f.Nodes, .84, 1, .1, ref phyl);
                    }
                    else
                    {
                        int twigs = reach < .18 ? 1 : reach > .45 ? 4 : f.Nodes >= 7 ? 3 : 2;
                        bool readableCrown = f.Id is "mirespire" or "fenforge" or "moonbark";
                        if (readableCrown) twigs = Math.Min(twigs, 3);
                        int perShoot = readableCrown ? 5 : Math.Max(3, (int)Math.Round(f.Nodes / (twigs * .65 + .8)));
                        int sign = b.Draw() < .5 ? -1 : 1;
                        int lastShoot = branch;
                        for (int w = 0; w < twigs; w++)
                        {
                            double tw = Math.Clamp(.3 + .5 * (w + .5) / twigs + b.Range(-.06, .06), .2, .9);
                            var tan = b.TangentAt(branch, tw);
                            var sideV = SideOf(tan);
                            var tdir = tan * .85 + sideV * (sign * b.Range(.55, 1.0)) + Vec3.Up * b.Range(.05, .35);
                            sign = -sign;
                            double tlen = reach * b.Range(.32, .5) * (1 - .4 * tw);
                            var tbend = Vec3.Down * (f.Droop * .7) + Vec3.Up * (.22 * (1 - f.Droop)) + sideV * b.Range(-.25, .25);
                            int twig = b.AddAxis(branch, tw, tdir, tlen, tbend, br0 * .5, .0008, 2, Tissue.Wood, .08, .18);
                            if (f.Id == "witchberry")
                            {
                                for (int q = 0; q < 3; q++)
                                {
                                    double qt = .35 + q * .28;
                                    var td = b.TangentAt(twig, qt);
                                    var ld = td + SideOf(td) * (q % 2 == 0 ? .8 : -.8) + Vec3.Up * .2;
                                    AddCompoundLeaf(b, leaf, twig, qt, ld, .18, Vec3.Down * .25,
                                        4, .10, .45, 3, .14);
                                }
                            }
                            else Foliage(b, leaf, twig, perShoot, .22, 1, .12, ref phyl, petioleLen: f.Needle ? 0 : .012, perOverride: readableCrown ? 2 : 0);
                            lastShoot = twig;
                        }
                        Foliage(b, leaf, branch, perShoot, .50, 1, .1, ref phyl, petioleLen: f.Needle ? 0 : .012, perOverride: readableCrown ? 2 : 0);
                        if (f.Ornament > 0)
                        {
                            var od = Vec3.Down + outward * .3 + new Vec3(b.Range(-.2, .2), 0, b.Range(-.2, .2));
                            b.AddFlat(lastShoot, .8, od, Math.Min(.07, height * .05), Math.Min(.025, height * .02), .001,
                                f.Ornament == 1 ? OrganType.Flower : OrganType.Fruit, ornament, 3, .2, .18);
                        }
                        continue;
                    }
                    if (f.Ornament > 0) b.AddFlat(branch, .82, Vec3.Down + outward * .3, Math.Min(.07, height * .05), Math.Min(.025, height * .02), .001,
                        f.Ornament == 1 ? OrganType.Flower : OrganType.Fruit, ornament, 2, .2, .18);
                }
            }
        }
    }
    // Shared node vocabulary keeps unsupported runners and recorded support paths
    // recognizably the same species. Added details never alter recorded axes.
    private static void AddClimberNode(PlanBuilder b, Form f, ushort leaf, ushort ornament,
        int axis, double t, Vec3 tangent, Vec3 normal, Vec3 side, int n)
    {
        int leaves = f.Id is "bitterbine" or "trumpetbind" ? 2 : 1;
        for (int q = 0; q < leaves; q++)
        {
            double sign = (n + q) % 2 == 0 ? 1 : -1;
            var d = PlantMath.SafeNormal(normal * .35 + side * sign + tangent * .38, side * sign);
            if (f.Id == "fenhook")
            {
                for (int k = -2; k <= 2; k++)
                    b.AddFlat(axis, t, side * (k * .42) + normal * (.45 + .1 * (2 - Math.Abs(k))) + tangent * .35,
                        .072, .011, .0004, OrganType.Leaf, leaf, 1, 0, .12);
            }
            else
            {
                int lobes = f.Id == "shadewrap" ? 5 : f.Id is "clinglace" or "bitterbine" ? 3 : 1;
                for (int l = 0; l < lobes; l++)
                {
                    double offset = l - (lobes - 1) * .5, angle = offset * (lobes == 5 ? .55 : .85);
                    var lateral = PlantMath.SafeNormal(normal.Cross(d), tangent);
                    var ld = d * Math.Cos(angle) + lateral * Math.Sin(angle);
                    double scale = 1 - .20 * Math.Abs(offset);
                    double width = f.Id is "shadewrap" or "clinglace" or "bitterbine" ? .25 : f.LeafWidth;
                    b.AddFlat(axis, t, ld, f.LeafLength * scale, f.LeafLength * scale * width,
                        .0004, OrganType.Leaf, leaf, 1, 0, .12);
                }
            }
        }
        if (f.Ornament > 0 && n % 3 == 1)
            b.AddFlat(axis, t, Vec3.Down + normal * .3, .04, .015, .001,
                f.Ornament == 1 ? OrganType.Flower : OrganType.Fruit, ornament, 1, .05, .12);
        if (f.Id == "spiralvine" && n % 3 == 1)
        {
            int curl = b.AddAxis(axis, t, side + normal * .35, .026, tangent * .5, .0015, .0008, 1, Tissue.Green, .05, .1);
            b.AddAxis(curl, 1, tangent + normal * .6, .025, -side * 1.4, .0008, .0002, 2, Tissue.Green, .08, .1);
        }
        if (f.Id == "fenhook" && n % 3 == 1)
        {
            int hook = b.AddAxis(axis, t, side + normal * .3, .012, -tangent * .5, .0012, .0007, 1, Tissue.Green, .05, .1);
            b.AddAxis(hook, 1, -tangent + side * .3, .009, -side * .7, .0007, .0002, 2, Tissue.Green, .06, .1);
        }
        if (f.Id == "clinglace")
            b.AddAxis(axis, t, -normal + side * .25, .018, -tangent * .2, .0008, .0002, 1, Tissue.RootBark, .04, .1);
    }

    /// <summary>Builds a graph directly from copied plant-local support segments.</summary>
    public static PlantDescriptor BuildClimber(FloraSpeciesDef species, ulong seed, ulong id,
        IReadOnlyList<GraphAxisSegment> segments, double age, double health)
    {
        var f=Array.Find(Forms,x=>x.Id==species.Id && x.Climber)
            ?? throw new ArgumentException("Species is not an assigned climber", nameof(species));
        var b=new PlanBuilder(seed,id); ushort leaf=Leaf(b,f), ornament=Ornament(b,f);
        var indices=new int[segments.Count];
        for(int i=0;i<segments.Count;i++) {
            var s=segments[i]; var delta=s.End-s.Start; double len=delta.Length;
            if(len<1e-7) { indices[i]=-1; continue; }
            int parent=s.Parent>=0 && s.Parent<i ? indices[s.Parent] : -1;
            int axis=b.AddAxis(parent,1,delta,len,Vec3.Zero,.004,.002,0,Tissue.Green,0,.1);
            // Preserve the simulation's segment endpoints exactly; these are already plant-local.
            var o=b[axis]; o.P0=s.Start; o.P1=s.End; o.T0=delta; o.T1=delta; o.Up=PlantMath.SafeNormal(s.Normal,Vec3.Up);
            // Support segments continue one another. Inheriting the parent's end
            // radius keeps the supported runner from being clamped thinner at every
            // attachment (AddAxis constrains laterals to 0.85 of the parent radius).
            double segR=parent>=0?Math.Max(.0008,b[parent].R1):.004;
            o.R0=segR; o.R1=Math.Max(.0007,segR*.96);
            indices[i]=axis;
            int count=Math.Clamp((int)Math.Ceiling(len/.11),1,12);
            for(int n=0;n<count;n++) {
                double t=(n+.5)/count; var normal=PlantMath.SafeNormal(s.Normal,PlantMath.Perpendicular(delta.Normalized()));
                var side=PlantMath.SafeNormal(delta.Cross(normal),Vec3.Right);
                var tangent=delta.Normalized();
                AddClimberNode(b,f,leaf,ornament,axis,t,tangent,normal,side,n);

            }
        }
        var plan=b.Finish(species.Id);
        var dsc=new PlantDescriptor { PlantId=id,WorldSeed=seed,SpeciesId=species.Id,StructureVersion=1,
            Shapes=plan.Shapes,Organs=PlantGrowth.Materialize(plan,Math.Clamp(age,0,1),.5) };
        for(int i=0;i<dsc.Organs.Length;i++) { var o=dsc.Organs[i]; o.Health=(float)Math.Clamp(health,0,1); dsc.Organs[i]=o; }
        PlanGenerator.Summarize(dsc); return dsc;
    }
}
