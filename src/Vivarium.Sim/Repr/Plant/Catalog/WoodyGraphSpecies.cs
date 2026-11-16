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
            ColorAdaxial = f.Id switch { "mirespire" => PlantShapes.Srgb(.22,.56,.53), "greywatch" => PlantShapes.Srgb(.56,.68,.57), "cragparasol" => PlantShapes.Srgb(.91,.86,.60), _ => PlantShapes.Srgb(.13,.32,.14) },
            ColorAbaxial = f.Id switch { "mirespire" => PlantShapes.Srgb(.40,.70,.66), "greywatch" => PlantShapes.Srgb(.70,.76,.65), "cragparasol" => PlantShapes.Srgb(.98,.93,.72), _ => PlantShapes.Srgb(.28,.46,.24) },
            Roughness = f.Id is "cragparasol" or "rainshield" ? .45f : .65f, Transmission = .32f
        });
    }
    private static double LeafOutline(string id, double t)
    {
        // Profiles run from attachment to tip. Lobed climbing blades are assembled from
        // individual overlapping lobes in BuildClimber, as in their legacy geometry.
        double sine = Math.Max(0, Math.Sin(Math.PI * t));
        double shape = id switch {
            "banksorrow" => Math.Pow(sine, 1.15) * (1.25 - .50 * t),
            "bitterbine" => Math.Pow(sine, .72) * (1.18 - .36 * t),
            "clinglace" => Math.Pow(sine, .78) * (1.20 - .40 * t),
            "cragparasol" => Math.Pow(sine, .68) * (.48 + 1.04 * t),
            "embercrown" => Math.Pow(sine, .95) * (1.23 - .46 * t),
            "fenforge" => Math.Pow(sine, .65) * (.72 + .56 * t),
            "fenhook" => Math.Pow(sine, .42) * (1.30 - .60 * t),
            "glowbriar" => Math.Pow(sine, .80) * (1.15 - .30 * t),
            "greywatch" => Math.Pow(1 - t, .70),
            "hollowcrown" => Math.Pow(sine, .66) * (.62 + .76 * t) * (1 - .22 * Math.Pow(Math.Sin(4 * Math.PI * t), 2)),
            "hookthicket" => Math.Pow(sine, .78) * (1.15 - .30 * t),
            "kinkcane" => Math.Pow(sine, 1.30) * (1.35 - .70 * t),
            "lacemantle" => Math.Pow(sine, .83) * (1.13 - .26 * t),
            "lanternbrush" => Math.Pow(sine, .82) * (1.22 - .44 * t),
            "miremyrtle" => Math.Pow(sine, .75) * (.66 + .68 * t),
            "mirespire" => Math.Pow(1 - t, .85),
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
        ColorAdaxial = PlantShapes.Srgb(.83,.52,.24), ColorAbaxial = PlantShapes.Srgb(.65,.25,.17)
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
            double rBase, double rTip, double splitFrac = 0.54, double emerge = 0.0, double dur = 0.28)
        {
            double l1 = totalLen * splitFrac;
            double l2 = totalLen * (1 - splitFrac);
            double rMid = rBase + (rTip - rBase) * splitFrac;
            int s0 = b.AddAxis(-1, 0, baseDir, l1, bend1, rBase, rMid, 0, Tissue.Wood, emerge, dur * .6);
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
            // Low spreading buttress roots carry one crooked leader and staggered foliage shelves.
            double rot = b.Range(0, Math.Tau);
            double tr = Math.Max(.012, height * .032);
            int trunk = b.AddAxis(-1, 0, Vec3.Up + PlantMath.Horizontal(rot) * .10, height * .88,
                PlantMath.Horizontal(rot + 1.5) * .16, tr, tr * .26, 0, Tissue.Wood, 0, .30);
            for (int r = 0; r < 8; r++)
            {
                double a = rot + r * Math.Tau / 8;
                var outward = PlantMath.Horizontal(a);
                var side = PlantMath.Horizontal(a + Math.PI / 2);
                // Roots leave the lowest part of the bole, rather than hoisting the crown
                // onto stilts. Their tips enter the soil even on the shorter variants.
                int root = b.AddAxis(trunk, .10 + .04 * (r % 2), outward * .90 + Vec3.Down,
                    height * .19, side * (r % 2 == 0 ? .35 : -.35) + Vec3.Down * .24,
                    tr * .42, tr * .19, 1, Tissue.RootBark, .01, .22);
                var rootPlan = b[root];
                rootPlan.P1 = new Vec3(rootPlan.P1.X, -.012, rootPlan.P1.Z);
                b.AddAxis(root, .94, outward + Vec3.Down * .40, height * .08,
                    side * (r % 2 == 0 ? -.35 : .35), tr * .18, .002, 2, Tissue.RootBark, .04, .16);
            }
            for (int k = 0; k < 11; k++)
            {
                double u = k / 10.0, a = rot + k * 2.39996 + b.Range(-.32, .32);
                var outward = PlantMath.Horizontal(a);
                var side = PlantMath.Horizontal(a + Math.PI / 2);
                double reach = radius * (1.0 - .20 * u) * b.Range(.68, 1.20);
                int branch = b.AddAxis(trunk, Math.Clamp(.37 + .58 * u + b.Range(-.045, .045), .34, .97),
                    outward + Vec3.Up * b.Range(.16, .58), reach,
                    Vec3.Up * b.Range(-.12, .30) + side * b.Range(-.20, .20), tr * .28, .002, 1, Tissue.Wood, .06, .20);
                Sweep(b, branch, side, b.Range(-.65, .65));
                for (int j = 0; j < 7; j++)
                {
                    double t = Math.Clamp(.14 + .12 * j + b.Range(-.035, .035), .10, .91);
                    int sign = j % 2 == 0 ? 1 : -1;
                    var tan = b.TangentAt(branch, t);
                    var dir = tan * .75 + SideOf(tan) * sign * .8 + Vec3.Up * .12;
                    int twig = b.AddAxis(branch, t, dir, reach * b.Range(.38, .52), Vec3.Up * (j % 3 == 0 ? .35 : -.18),
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
                for (int j = 0; j < 4; j++)
                {
                    double a = rot + tier * 1.15 + j * Math.Tau / 4 + b.Range(-.34, .34);
                    var outward = PlantMath.Horizontal(a);
                    var branchSide = PlantMath.Horizontal(a + Math.PI / 2);
                    int branch = b.AddAxis(trunk, Math.Clamp(.18 + .78 * u + b.Range(-.025, .025), .15, .98),
                        outward + Vec3.Up * b.Range(.04, .25), reach * b.Range(.82, 1.12),
                        Vec3.Down * b.Range(.04, .24), tr * .22 * (1 - .5 * u), .001, 1, Tissue.Wood, .05, .20);
                    Sweep(b, branch, branchSide, b.Range(-.45, .45));
                    for (int q = 0; q < 6; q++)
                    {
                        double t = Math.Clamp(.18 + q * .14 + b.Range(-.035, .035), .12, .94);
                        var tan = b.TangentAt(branch, t);
                        var side = SideOf(tan);
                        for (int sign = -1; sign <= 1; sign += 2)
                        {
                            double sprayLen = reach * (.55 - .22 * t) * b.Range(.78, 1.18);
                            double lift = (q + j + tier) % 2 == 0 ? .85 : -.65;
                            int spray = b.AddAxis(branch, t, tan * .55 + side * sign * .65 + Vec3.Up * lift,
                                sprayLen, Vec3.Up * (lift > 0 ? .22 : -.24), .0015, .0005, 2, Tissue.Green, .09, .16);
                            for (int n = 0; n < 6; n++)
                            {
                                double nt = .13 + n * .14;
                                var st = b.TangentAt(spray, nt);
                                var lateral = SideOf(st);
                                for (int pair = -1; pair <= 1; pair += 2)
                                {
                                    double len = Math.Min(.13, height * .065) * (1 - .25 * nt);
                                    double incline = ((n + q + j) % 3 - 1) * .85;
                                    b.AddFlat(spray, nt, lateral * pair + st * .35 + Vec3.Up * incline,
                                        len, len * .10, .0004, OrganType.Leaf, leaf, 3, .12, .15);
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
                for (int k = 0; k < 4; k++)
                {
                    double az = a + (k - 1.5) * .65;
                    var dir = PlantMath.Horizontal(az);
                    int limb = b.AddAxis(upper, .25 + .22 * k, dir * .9 + Vec3.Up * b.Range(.45, .72),
                        radius * b.Range(.72, 1.0), Vec3.Down * 1.45 + side * b.Range(-.2, .2),
                        tr * .32, .0018, 2, Tissue.Wood, .07, .20);
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
            // Gnarled crooked krummholz trunks -> dichotomous twisting limbs -> dense terminal umbrella rosettes.
            int trunks = 1;
            double trunkR = Math.Max(.025, height * .045);
            double baseRot = b.Range(0, Math.Tau);
            for (int c = 0; c < trunks; c++)
            {
                double az = baseRot + c * Math.Tau / trunks + b.Range(-.45, .45);
                var h = PlantMath.Horizontal(az);
                var s = PlantMath.Horizontal(az + Math.PI / 2);
                double len = height * b.Range(.68, .86);
                var d0 = Vec3.Up + h * b.Range(.26, .48) + s * b.Range(-.22, .22);
                var b1 = h * b.Range(.28, .55) + s * b.Range(-.45, .45) + Vec3.Down * .10;
                int s0 = b.AddAxis(-1, 0, d0, len * .45, b1, trunkR, trunkR * .72, 0, Tissue.Wood, 0, .2);
                var d1 = b.TangentAt(s0, 1.0) + s * b.Range(-.45, .45) + Vec3.Up * .35;
                int s1 = b.AddAxis(s0, .95, d1, len * .38, h * b.Range(-.35, .25) + s * b.Range(-.45, .45),
                    trunkR * .60, trunkR * .38, 1, Tissue.Wood, .04, .18);

                // Dichotomous twisting scaffold limbs forming wide asymmetric umbrellas.
                for (int j = 0; j < 7; j++)
                {
                    double laz = az + j * Math.Tau / 7 + b.Range(-.52, .52);
                    var lo = PlantMath.Horizontal(laz);
                    var ls = PlantMath.Horizontal(laz + Math.PI / 2);
                    double lt = b.Range(.60, .96);
                    int host = s1;
                    double lLen = radius * b.Range(.38, .76);
                    var lDir = lo * .95 + Vec3.Up * b.Range(.22, .52) + ls * b.Range(-.25, .25);
                    var lBend = ls * b.Range(-.45, .45) + Vec3.Up * b.Range(.10, .35) + Vec3.Down * b.Range(0, .22);
                    int limb = b.AddAxis(host, Math.Clamp(lt, .45, .96), lDir, lLen, lBend, trunkR * .30, .0045, 2, Tissue.Wood, .07, .18);
                    Sweep(b, limb, ls, b.Range(-.65, .65));

                    for (int k = 0; k < 3; k++)
                    {
                        double saz = laz + (k - 1) * .95 + b.Range(-.3, .3);
                        var so = PlantMath.Horizontal(saz);
                        var ss = PlantMath.Horizontal(saz + Math.PI / 2);
                        double st = .42 + .52 * k / 2.0;
                        var sDir = so * .85 + Vec3.Up * b.Range(.35, .65) + ss * b.Range(-.2, .2);
                        int sub = b.AddAxis(limb, st, sDir, radius * b.Range(.24, .36), ss * b.Range(-.35, .35) + Vec3.Up * .25,
                            .0038, .0020, 3, Tissue.Wood, .10, .14);
                        // Dense terminal umbrella rosette + subterminal collar.
                        Foliage(b, leaf, sub, 5, .45, .99, .14, ref phyl, petioleLen: .018, leafScale: 1.35, perOverride: 3);
                        Foliage(b, leaf, sub, 2, .35, .62, .12, ref phyl, petioleLen: .016, leafScale: .95, perOverride: 2);
                    }
                }
            }
        }

        private void DesignBanksorrow(PlanBuilder b, ushort leaf, ref double phyl)
        {
            // Low leaning fork, fountain boughs, and long leafy willow withes.
            double rot = b.Range(0, Math.Tau), tr = Math.Max(.012, height * .045);
            var lean = PlantMath.Horizontal(rot);
            int trunk = b.AddAxis(-1, 0, Vec3.Up + lean * .22, height * .43,
                lean * .22, tr, tr * .58, 0, Tissue.Wood, 0, .28);
            for (int i = 0; i < 7; i++)
            {
                double a = rot + i * Math.Tau / 7 + b.Range(-.18, .18);
                var outward = PlantMath.Horizontal(a);
                var side = PlantMath.Horizontal(a + Math.PI / 2);
                double reach = radius * b.Range(.72, .98);
                int bough = b.AddAxis(trunk, .83 + .15 * (i % 2), outward * .75 + Vec3.Up * .8,
                    reach, Vec3.Down * .85 + side * b.Range(-.12, .12), tr * .48, .004, 1, Tissue.Wood, .04, .22);
                for (int j = 0; j < 8; j++)
                {
                    double t = .22 + .105 * j;
                    var tan = b.TangentAt(bough, t);
                    double drape = height * b.Range(.36, .56);
                    int withe = b.AddAxis(bough, t, tan * .45 + Vec3.Down * .65,
                        drape, Vec3.Down * 1.3 + side * b.Range(-.10, .10),
                        .004, .001, 2, Tissue.Wood, .09, .18);
                    Foliage(b, leaf, withe, 16, .08, .98, .12, ref phyl, leafScale: 1.05, perOverride: 2);
                }
            }
        }

        private void DesignEmbercrown(PlanBuilder b, ushort leaf, ushort ornament, ref double phyl)
        {
            // Staghorn sumac clonal thicket: sinuous ascending stems, curved limbs, arching pinnate compound leaves, upright fruit panicles.
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
                var d0 = Vec3.Up + h * .24 + s * (.9 * turn);
                int s0 = b.AddAxis(-1, 0, d0, len * .34, Vec3.Zero,
                    stemR, stemR * .78, 0, Tissue.Wood, .01 * c, .16);
                b[s0].P1 = b[s0].P0 + Vec3.Up * (len * .33) + h * (len * .06) + s * (len * .16 * turn);
                b[s0].T0 = (Vec3.Up + s * (.9 * turn)) * (len * .34);
                b[s0].T1 = (Vec3.Up - s * (.9 * turn)) * (len * .34);
                int mid = b.AddAxis(s0, .98, b.TangentAt(s0, 1), len * .33,
                    Vec3.Zero, stemR * .75, stemR * .55, 1, Tissue.Wood, .04, .14);
                b[mid].P1 = b[mid].P0 + Vec3.Up * (len * .33) + h * (len * .06) - s * (len * .16 * turn);
                b[mid].T0 = b[s0].T1;
                b[mid].T1 = (Vec3.Up + s * (.9 * turn)) * (len * .34);
                int s1 = b.AddAxis(mid, .98, b.TangentAt(mid, 1), len * .33,
                    Vec3.Zero, stemR * .52, stemR * .35, 1, Tissue.Wood, .07, .14);
                b[s1].P1 = b[s1].P0 + Vec3.Up * (len * .33) + h * (len * .06) + s * (len * .16 * turn);
                b[s1].T0 = b[mid].T1;
                b[s1].T1 = (Vec3.Up - s * (.9 * turn)) * (len * .34);

                // Curved limbs on upper stem + crown tip.
                for (int k = 0; k < 4; k++)
                {
                    int host = k < 2 ? mid : s1;
                    double bt = k < 2 ? .52 + .36 * k : .28 + .58 * (k - 2);
                    double baz = az + (k % 2 == 0 ? 1 : -1) * b.Range(.55, 1.15) + b.Range(-.3, .3);
                    var bo = PlantMath.Horizontal(baz);
                    var bs = PlantMath.Horizontal(baz + Math.PI / 2);
                    double reach = radius * b.Range(.45, .75);
                    double sweep = (k % 2 == 0 ? 1 : -1) * turn;
                    int br0 = b.AddAxis(host, bt, bo * .75 + Vec3.Up * .65 + bs * (.85 * sweep), reach * .55,
                        Vec3.Zero, stemR * .32, stemR * .19, 2, Tissue.Wood, .07, .12);
                    b[br0].P1 = b[br0].P0 + bo * (reach * .42) + Vec3.Up * (reach * .22) + bs * (reach * .16 * sweep);
                    b[br0].T0 = (bo * .8 + Vec3.Up * .5 + bs * (.9 * sweep)) * (reach * .55);
                    b[br0].T1 = (bo * .8 + Vec3.Up * .5 - bs * (.9 * sweep)) * (reach * .55);
                    int br = b.AddAxis(br0, .98, b.TangentAt(br0, 1), reach * .45,
                        Vec3.Zero, stemR * .18, .0025, 3, Tissue.Wood, .10, .12);
                    b[br].P1 = b[br].P0 + bo * (reach * .34) + Vec3.Up * (reach * .18) - bs * (reach * .14 * sweep);
                    b[br].T0 = b[br0].T1;
                    b[br].T1 = (bo * .8 + Vec3.Up * .5 + bs * (.9 * sweep)) * (reach * .45);
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
            // Short leaning bole and low, outward-reaching limbs carry pale, airy pads.
            double rot = b.Range(0, Math.Tau), tr = Math.Max(.012, height * .045);
            int bole = b.AddAxis(-1, 0, Vec3.Up + PlantMath.Horizontal(rot) * .18, height * .25,
                PlantMath.Horizontal(rot + 1) * .12, tr, tr * .65, 0, Tissue.Wood, 0, .2);
            for (int i = 0; i < 6; i++)
            {
                double a = rot + i * Math.Tau / 6 + b.Range(-.2, .2);
                var outwards = PlantMath.Horizontal(a);
                int limb = b.AddAxis(bole, .45 + .09 * (i % 5), outwards + Vec3.Up * .27,
                    radius * b.Range(.70, 1.0), Vec3.Up * .16, tr * .42, .002, 1, Tissue.Wood, .04, .2);
                for (int j = 0; j < 4; j++)
                {
                    double t = .30 + j * .21;
                    var d = PlantMath.Horizontal(a + (j % 2 == 0 ? .65 : -.65));
                    int shoot = b.AddAxis(limb, t, d + Vec3.Up * .22, radius * .30,
                        Vec3.Up * .08, .003, .001, 2, Tissue.Green, .09, .16);
                    Foliage(b, leaf, shoot, 9, .12, .98, .13, ref phyl, leafScale: 1.25, perOverride: 3);
                }
                Foliage(b, leaf, limb, 8, .3, .99, .13, ref phyl, leafScale: 1.25, perOverride: 3);
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
                double segLen = height * b.Range(.84, 1.06);
                // Segment 1: climbs steeply and arcs outward to crest near 0.95-1.15m.
                int asc = b.AddAxis(-1, 0, Vec3.Up * b.Range(1.05, 1.45) + h * b.Range(.40, .68), segLen,
                    h * b.Range(.40, .68) + Vec3.Down * .48 + s * b.Range(-.35, .35), caneR, caneR * .62, 0, Tissue.Wood, .01 * c, .22);
                // Segment 2: bows over and sweeps back toward the ground.
                var t1 = b.TangentAt(asc, 1.0);
                int bow = b.AddAxis(asc, .96, t1 + h * .45 + Vec3.Down * .55, segLen * b.Range(.75, 1.02),
                    Vec3.Down * b.Range(.85, 1.25) + s * b.Range(-.4, .4), caneR * .52, .0020, 1, Tissue.Wood, .05, .22);

                // Recurved thorns along ascending & bowing cane.
                for (int th = 0; th < 6; th++)
                {
                    int host = th < 3 ? asc : bow;
                    double tt = .20 + .72 * (th % 3) / 2.0;
                    var td = s * (th % 2 == 0 ? .8 : -.8) + h * .4 + Vec3.Up * .2;
                    b.AddAxis(host, tt, td, .014, Vec3.Down * .45, .0025, .0003, 2, Tissue.Bark, .08, .10);
                }

                // Curved leafy lateral shoots along both ascending and bowing segments.
                for (int k = 0; k < 6; k++)
                {
                    int host = k < 3 ? asc : bow;
                    double bt = k < 3 ? .32 + .28 * k : .16 + .32 * (k - 3);
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
                            1 + (lf % 2 == 1 ? 1 : 0), b.Range(.070, .100), .34, 3, .12);
                    }
                }
            }
        }

        private void DesignHollowcrown(PlanBuilder b, ushort leaf, ref double phyl)
        {
            // One hollow veteran bole supports radial roots and a full crown.
            double boleHeight = height * .49, r = Math.Max(.10, height * .105);
            double opening = b.Range(0, Math.Tau);
            int bole = b.AddAxis(-1, 0, Vec3.Up, boleHeight,
                Vec3.Up * .03, r, r * .85, 0, Tissue.Bark, 0, .25, OrganFlags.Hollow | OrganFlags.HollowWindow);
            b[bole].Up = Vec3.Forward;
            for (int i = 0; i < 11; i++)
            {
                double az = opening + i * Math.Tau / 11 + b.Range(-.22, .22);
                var radial = PlantMath.Horizontal(az);
                b.AddAxis(bole, .06, radial + Vec3.Down * .12, r * 1.65,
                    Vec3.Down * .25, r * .33, .008, 1, Tissue.RootBark, 0, .2);
                for (int k = 0; k < 2; k++)
                {
                    double a = az + (k == 0 ? -.18 : .18) + b.Range(-.30, .30);
                    int limb = b.AddAxis(bole, b.Range(.68, .98), PlantMath.Horizontal(a) + Vec3.Up * b.Range(.22, .65),
                        radius * b.Range(.43, .84), Vec3.Up * .18, r * .23, .006, 1, Tissue.Wood, .07, .2);
                    Sweep(b, limb, PlantMath.Horizontal(a + Math.PI / 2), b.Range(-.60, .60));
                    for (int j = 0; j < 3; j++)
                    {
                        var d = PlantMath.Horizontal(a + (j - 1) * .5) + Vec3.Up * .60;
                        int twig = b.AddAxis(limb, Math.Clamp(.35 + .29 * j + b.Range(-.08, .08), .22, .98), d, radius * b.Range(.24, .36),
                            Vec3.Up * .15, .006, .001, 2, Tissue.Wood, .12, .16);
                        Foliage(b, leaf, twig, 7, .12, .98, .15, ref phyl, petioleLen: .012, perOverride: 2);
                    }
                }
            }
        }

        private void DesignWitchberry(PlanBuilder b, ushort leaf, ushort ornament)
        {
            // Root-flared short trunk splits into a wide, upward-splayed crown.
            double rot = b.Range(0, Math.Tau), tr = Math.Max(.025, height * .065);
            int bole = b.AddAxis(-1, 0, Vec3.Up, height * .48, PlantMath.Horizontal(rot) * .12,
                tr, tr * .65, 0, Tissue.Wood, 0, .23);
            for (int i = 0; i < 5; i++)
            {
                double a = rot + i * Math.Tau / 5 + b.Range(-.45, .45);
                var d = PlantMath.Horizontal(a);
                b.AddAxis(bole, .05, d + Vec3.Down * .15, tr * 2.3, Vec3.Down * .25,
                    tr * .38, .006, 1, Tissue.RootBark, 0, .2);
                int limb = b.AddAxis(bole, b.Range(.64, .96), d * b.Range(.65, 1.0) + Vec3.Up * b.Range(.75, 1.65),
                    radius * b.Range(.75, 1.30), Vec3.Up * b.Range(.15, .55), tr * .45, .004, 1, Tissue.Wood, .05, .2);
                Sweep(b, limb, PlantMath.Horizontal(a + Math.PI / 2), b.Range(-.55, .55));
                for (int j = 0; j < 5; j++)
                {
                    double t = Math.Clamp(.25 + j * .17 + b.Range(-.055, .055), .18, .96);
                    var outward = PlantMath.Horizontal(a + (j % 2 == 0 ? .65 : -.65));
                    int twig = b.AddAxis(limb, t, outward + Vec3.Up * b.Range(.25, .85), radius * b.Range(.32, .48),
                        Vec3.Up * b.Range(.1, .35), .005, .001, 2, Tissue.Wood, .09, .17);
                    for (int q = 0; q < 4; q++)
                    {
                        double qt = .20 + q * .23;
                        var tan = b.TangentAt(twig, qt);
                        var direction = tan + SideOf(tan) * (q % 2 == 0 ? .8 : -.8) + Vec3.Up * .2;
                        AddCompoundLeaf(b, leaf, twig, qt, direction, .23, Vec3.Down * .25,
                            4, .10, .45, 3, .14);
                    }
                    b.AddFlat(twig, .85, Vec3.Down + outward * .3, .06, .035, .001,
                        OrganType.Fruit, ornament, 3, .2, .18);
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
                        : Vec3.Up + h * (sign * b.Range(.48, .66) + .12)
                            + s * (sign * (j % 3 == 0 ? .25 : -.25));
                    double tipRadius = caneR * (1 - .055 * (j + 1));
                    joints[j] = b.AddAxis(j == 0 ? -1 : joints[j - 1], 1, direction,
                        len / joints.Length, Vec3.Zero, jointRadius, tipRadius,
                        (ushort)j, Tissue.Wood, .01 * c + .015 * j, .16);
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
                        int host = n < 2 ? seg1 : seg2;
                        double nt = n < 2 ? .68 + .26 * n : .20 + .35 * (n - 2);
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
            int s0 = b.AddAxis(-1, 0, Vec3.Up + hL * .14, height * .46, hL * -.18 + sL * .22, trunkR, trunkR * .65, 0, Tissue.Wood, 0, .20);
            int s1 = b.AddAxis(s0, .96, b.TangentAt(s0, 1.0) + sL * -.25, height * .42, hL * .18 + sL * -.18,
                trunkR * .54, trunkR * .24, 1, Tissue.Wood, .04, .20);

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
                double tt = tier < 3 ? .38 + .25 * tier : .16 + .30 * (tier - 3);
                double u = tier / 5.0;
                double tierRadius = radius * (1.05 - .48 * u);
                for (int bIdx = 0; bIdx < 3; bIdx++)
                {
                    double az = tierRot + tier * (Math.Tau / 3 * .5) + bIdx * (Math.Tau / 3) + b.Range(-.22, .22);
                    var bo = PlantMath.Horizontal(az);
                    var bs = PlantMath.Horizontal(az + Math.PI / 2);
                    // Horizontal sweeping limb that gently upturns toward the tip like Cornus alternifolia.
                    int limb = b.AddAxis(host, tt, bo * 1.0 + Vec3.Up * b.Range(.06, .18), tierRadius * b.Range(.80, 1.08),
                        Vec3.Up * .32 + bs * b.Range(-.25, .25), Math.Max(.003, trunkR * .38 * (1 - .35 * u)), .0018, 2, Tissue.Wood, .07, .18);

                    // Horizontal flat fan of secondary twigs carrying dense lacy terminal foliage.
                    for (int tw = 0; tw < 4; tw++)
                    {
                        double twT = .30 + .64 * tw / 3.0;
                        double twAz = az + (tw % 2 == 0 ? 1 : -1) * b.Range(.65, 1.05);
                        var twO = PlantMath.Horizontal(twAz);
                        int twig = b.AddAxis(limb, twT, twO * .95 + Vec3.Up * .12, tierRadius * b.Range(.28, .42) * (1 - .3 * twT),
                            Vec3.Up * .20 + PlantMath.Horizontal(twAz + 1.5) * b.Range(-.2, .2), .0025, .0010, 3, Tissue.Green, .11, .14);
                        for (int n = 0; n < 3; n++) LaceFan(b, leaf, twig, .35 + n * .30);
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
                double len = height * b.Range(.78, 1.04);
                int s0 = b.AddAxis(-1, 0, Vec3.Up + h * b.Range(.18, .36), len * .55,
                    h * b.Range(.22, .42) + s * b.Range(-.25, .25), stemR, stemR * .65, 0, Tissue.Wood, .01 * c, .20);
                int s1 = b.AddAxis(s0, .96, b.TangentAt(s0, 1.0) + h * .25, len * .45,
                    Vec3.Down * b.Range(.75, 1.15) + h * .3 + s * b.Range(-.3, .3), stemR * .54, .0025, 1, Tissue.Wood, .05, .20);

                // Opposite curved lateral branches along the arching stem.
                for (int k = 0; k < 4; k++)
                {
                    int host = k < 2 ? s0 : s1;
                    double bt = k < 2 ? .52 + .36 * k : .25 + .50 * (k - 2);
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
                    h * b.Range(-.25, .15) + s * b.Range(-.32, .32), stemR, stemR * .32, 0.48, .01 * c, .20);

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
                            radius * b.Range(.22, .35), PlantMath.Horizontal(twAz + 1.5) * b.Range(-.3, .3),
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
                var (s0, s1) = CurvedStem(b, Vec3.Up + h * b.Range(.24, .44), len,
                    s * b.Range(-.32, .32) + h * b.Range(-.15, .20),
                    h * b.Range(-.28, .15) + s * b.Range(-.32, .32), stemR, stemR * .35, 0.50, .01 * c, .24);

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
                    Foliage(b, leaf, br, 3, .42, 1.0, .13, ref phyl, petioleLen: .018, leafScale: 1.18, perOverride: 2);
                    if (k >= 2)
                        b.AddFlat(br, .75, Vec3.Down + bo * .25, .065, .016, .001, OrganType.Fruit, ornament, 3, .18, .16);
                }
                Foliage(b, leaf, s1, 3, .55, 1.0, .13, ref phyl, petioleLen: .018, leafScale: 1.15, perOverride: 2);
            }
        }

        private void DesignRainshield(PlanBuilder b, ushort leaf)
        {
            // Darmera peltata: rhizomatous colony of 26 gracefully curving fleshy petioles rising to staggered heights, each crowned with a broad cupped horizontal umbrella leaf.
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
                double reach = radius * .32 * Math.Sqrt(frac);
                int runner = b.AddAxis(rhizome, .8, h + Vec3.Up * .05, Math.Max(.02, reach), Vec3.Zero,
                    petR * .9, petR * .75, 1, Tissue.Green, .01, .12, OrganFlags.Hidden);
                double stalkH = height * b.Range(.42, .92);
                var sDir = Vec3.Up + h * b.Range(.14, .42) + s * b.Range(-.14, .14);
                var sBend = h * b.Range(-.35, .45) + s * b.Range(-.55, .55);
                int petiole = b.AddAxis(runner, .95, sDir, stalkH, sBend, petR * .62, petR * .42, 2, Tissue.Green, .02 + .004 * i, .24);
                Sweep(b, petiole, s, b.Range(-.65, .65));
                // Broad overlapping shields form a cohesive, staggered canopy.
                double bladeLen = b.Range(.48, .66);
                var tilt = h * b.Range(.75, 1.0) + s * b.Range(-.3, .3) + Vec3.Up * b.Range(-.06, .16);
                b.AddFlat(petiole, 1.0, tilt, bladeLen, bladeLen * .50, .0007, OrganType.Leaf, leaf, 3, .14 + .004 * i, .20);
            }
        }

        private void DesignSilverfelt(PlanBuilder b, ushort leaf, ushort ornament, ref double phyl)
        {
            // Salix lanata: low wind-hugged gnarled cushion of 9 procumbent-ascending multi-segment stems & twisted twigs packed with broad ovate leaves + erect furry catkins.
            int stems = 9;
            double stemR = Math.Max(.005, height * .030 / Math.Sqrt(stems));
            double baseRot = b.Range(0, Math.Tau);
            for (int c = 0; c < stems; c++)
            {
                double az = baseRot + c * Math.Tau / stems + b.Range(-.38, .38);
                var h = PlantMath.Horizontal(az);
                var s = PlantMath.Horizontal(az + Math.PI / 2);
                double len = radius * b.Range(.86, 1.20);
                // Hugs low outward first, then curves upward.
                int s0 = b.AddAxis(-1, 0, h * 1.1 + Vec3.Up * b.Range(.35, .65), len * .52,
                    Vec3.Up * .55 + s * b.Range(-.35, .35), stemR, stemR * .65, 0, Tissue.Wood, .01 * c, .20);
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
            double trunkR = Math.Max(.009, height * .026 / Math.Sqrt(trunks));
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
                    double bt = .38 + .58 * k / 3.0;
                    int limb = b.AddAxis(s1, bt, bo * .95 + Vec3.Up * b.Range(.28, .55), radius * b.Range(.46, .74),
                        Vec3.Up * .15 + bs * b.Range(-.32, .32) + Vec3.Down * .22, trunkR * .30, .0020, 2, Tissue.Wood, .08, .18);
                    for (int tw = 0; tw < 3; tw++)
                    {
                        double twT = .35 + .58 * tw / 2.0;
                        double twAz = baz + (tw - 1) * .85;
                        var twO = PlantMath.Horizontal(twAz);
                        int twig = b.AddAxis(limb, twT, twO * .95 + Vec3.Up * .18, radius * b.Range(.22, .34),
                            Vec3.Down * .15 + PlantMath.Horizontal(twAz + 1.5) * b.Range(-.25, .25), .0018, .0009, 3, Tissue.Green, .12, .14);
                        // Pair of feathery pinnate compound leaves at each twig fan.
                        for (int fIdx = 0; fIdx < 3; fIdx++)
                        {
                            double ft = .35 + .60 * fIdx / 2.0;
                            var fdir = PlantMath.Horizontal(twAz + (fIdx - 1) * .9) * .95 + Vec3.Up * .12;
                            AddCompoundLeaf(b, leaf, twig, ft, fdir, b.Range(.16, .24), Vec3.Down * .32, 6, b.Range(.038, .052), .26, 4, .15);
                        }
                    }
                    AddCompoundLeaf(b, leaf, limb, .96, bo * .95 + Vec3.Up * .1, .20, Vec3.Down * .30, 6, .048, .26, 3, .14);
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
                double len = height * b.Range(.78, 1.05);
                var (s0, s1) = CurvedStem(b, Vec3.Up + h * b.Range(.20, .42), len,
                    s * b.Range(-.36, .36) + h * .18,
                    s * b.Range(-.36, .36) + h * -.24, stemR, stemR * .34, 0.48, .01 * c, .24);

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
                    if (k >= 1)
                        b.AddFlat(br, .82, Vec3.Up * .5 + bo * .6, .048, .022, .001, OrganType.Flower, ornament, 3, .17, .15);
                }
                Foliage(b, leaf, s1, 4, .55, 1.0, .12, ref phyl, petioleLen: .012, leafScale: 1.10, perOverride: 2);
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
                double a = rot + j * 2.399963 + b.Range(-.15, .15);
                var outward = PlantMath.Horizontal(a);
                var side = PlantMath.Horizontal(a + Math.PI / 2);
                double rise = inner ? b.Range(.55, 1.05) : b.Range(.10, .48);
                double reach = radius * (inner ? b.Range(.72, .95) : b.Range(.95, 1.25));
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
                    var chord = stem.P1 - stem.P0;
                    double sweep = (c == 1 ? -.48 : .48) * trunkLen;
                    stem.T0 = chord + hs * sweep;
                    stem.T1 = chord - hs * sweep;
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
                    double skip = b.Draw();
                    if (f.Branches * f.Tiers >= 6 && skip < .12) continue;
                    double a = az + j * Math.Tau / f.Branches + k * 2.39996 + b.Range(-.4, .4);
                    var outward = PlantMath.Horizontal(a);
                    var side = PlantMath.Horizontal(a + Math.PI / 2);
                    double u = (k + .5) / f.Tiers;
                    double t = f.CrownBase + (1 - f.CrownBase) * u + b.Range(-.4, .4) * (1 - f.CrownBase) / f.Tiers;
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
                    int branch = b.AddAxis(trunk, Math.Clamp(t, .03, .99), bdir, reach, bbend, br0, .0012, 1, Tissue.Wood, .05, .22);
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
            indices[i]=axis;
            int count=Math.Clamp((int)Math.Ceiling(len/.11),1,12);
            for(int n=0;n<count;n++) {
                double t=(n+.5)/count; var normal=PlantMath.SafeNormal(s.Normal,PlantMath.Perpendicular(delta.Normalized()));
                var side=PlantMath.SafeNormal(delta.Cross(normal),Vec3.Right);
                var tangent=delta.Normalized();
                int pairs=f.Id is "bitterbine" or "spiralvine" or "trumpetbind" ? 2 : 1;
                for(int q=0;q<pairs;q++) {
                    double sign=(n+q)%2==0?1:-1;
                    var d=PlantMath.SafeNormal(normal*.35+side*sign+tangent*.38, side*sign);
                    if(f.Id=="fenhook") {
                        // A half-whorl of narrow straps faces away from the support.
                        for(int k=-2;k<=2;k++) {
                            var wd=side*(k*.42)+normal*(.45+.1*(2-Math.Abs(k)))+tangent*.35;
                            b.AddFlat(axis,t,wd,.072,.011,.0004,OrganType.Leaf,leaf,1,0,.12);
                        }
                    } else {
                        int lobes=f.Id=="shadewrap"?5:f.Id is "clinglace" or "bitterbine"?3:1;
                        for(int l=0;l<lobes;l++) {
                            double offset=l-(lobes-1)*.5, angle=offset*(lobes==5?.55:.85);
                            var lateral=PlantMath.SafeNormal(normal.Cross(d),tangent);
                            var ld=d*Math.Cos(angle)+lateral*Math.Sin(angle);
                            double scale=1-.20*Math.Abs(offset);
                            double width=f.Id is "shadewrap" or "clinglace" or "bitterbine" ? .25 : f.LeafWidth;
                            b.AddFlat(axis,t,ld,f.LeafLength*scale,f.LeafLength*scale*width,.0004,OrganType.Leaf,leaf,1,0,.12);
                        }
                    }
                }
                if(f.Ornament>0 && n%3==1) b.AddFlat(axis,t,Vec3.Down+normal*.3,.04,.015,.001,
                    f.Ornament==1?OrganType.Flower:OrganType.Fruit,ornament,1,.05,.12);
                if(f.Id=="spiralvine" && n%3==1) {
                    // A short searching curl, kept local to this support station.
                    int curl=b.AddAxis(axis,t,side+normal*.35,.026,tangent*.5,.0015,.0008,1,Tissue.Green,.05,.1);
                    b.AddAxis(curl,1,tangent+normal*.6,.025,-side*1.4,.0008,.0002,2,Tissue.Green,.08,.1);
                }
                if(f.Id=="fenhook" && n%3==1) {
                    int hook=b.AddAxis(axis,t,side+normal*.3,.012,-tangent*.5,.0012,.0007,1,Tissue.Green,.05,.1);
                    b.AddAxis(hook,1,-tangent+side*.3,.009,-side*.7,.0007,.0002,2,Tissue.Green,.06,.1);
                }
            }
        }
        var plan=b.Finish(species.Id);
        var dsc=new PlantDescriptor { PlantId=id,WorldSeed=seed,SpeciesId=species.Id,StructureVersion=1,
            Shapes=plan.Shapes,Organs=PlantGrowth.Materialize(plan,Math.Clamp(age,0,1),.5) };
        for(int i=0;i<dsc.Organs.Length;i++) { var o=dsc.Organs[i]; o.Health=(float)Math.Clamp(health,0,1); dsc.Organs[i]=o; }
        PlanGenerator.Summarize(dsc); return dsc;
    }
}
