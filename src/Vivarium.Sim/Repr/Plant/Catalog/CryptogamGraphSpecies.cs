using System;
using System.Collections.Generic;
using System.Linq;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>Individually authored colony forms. Positions and growth gates are deterministic per plant.</summary>
public static class CryptogamGraphSpecies
{
    private enum Form { Cap, Bell, Cup, Shelf, Puff, Coral, Jelly, Star, Feather, Foliose, Crust, Pendant }
    private sealed record Entry(string Id, string Counterpart, string Family, string Silhouette, string Habit, bool Attached,
        Form Form, int Count, double Spread, double Height, double Radius);
    private static readonly Entry[] Entries = {
        new("antlerlace_lichen", "Cladonia rangiferina", "lichen", "fruticose", "Dry branching lichen of open lean ground and stable mineral surfaces.", true, Form.Coral, 17, 0.45, 0.07, 0.13),
        new("ashcomb", "Morchella esculenta", "fungus", "ashcomb", "Spring honeycomb of hardwood shade.", false, Form.Cap, 4, 0.64, 0.16, 0.09),
        new("ashfingers_lichen", "Thamnolia vermicularis", "lichen", "ashfingers_lichen", "Pale branching lichen of dry heath.", true, Form.Coral, 22, 0.31, 0.04, 0.13),
        new("barrowbole", "Boletus edulis", "fungus", "barrowbole", "Stout bolete of rich soil.", false, Form.Cap, 3, 1.05, 0.14, 0.14),
        new("bogglass_moss", "Sphagnum capillifolium", "moss", "capitula", "Water-holding bog moss of saturated nutrient-poor ground.", true, Form.Star, 48, 0.8, 0.05, 0.2),
        new("bonecup", "Peziza vesiculosa", "fungus", "bonecup_fungus", "Wood-decaying cup fungus of damp, shaded litter.", true, Form.Cup, 12, 0.9, 0.075, 0.16),
        new("candlecoral", "Ramaria formosa", "fungus", "candlecoral", "Upright coral fungus of buried wood.", false, Form.Coral, 10, 0.6, 0.11, 0.12),
        new("capstar_moss", "Polytrichum commune", "moss", "capstar_moss", "Star-leaved tall moss ripening gold spore-capsules.", true, Form.Star, 38, 1.0, 0.15, 0.19),
        new("carrion_bell", "Phallus impudicus", "fungus", "carrion_bell", "Short-lived dramatic stinkhorn analogue that erupts from rich litter and rapidly returns to detritus.", false, Form.Bell, 3, 0.65, 0.28, 0.19),
        new("cradlecup", "Crucibulum laeve", "fungus", "cradlecup", "Tiny nest fungus of wet wood.", true, Form.Cup, 9, 0.55, 0.025, 0.06),
        new("dewbonnet", "Mycena galericulata", "fungus", "mushroom_cluster", "Small litter decomposer of humid nutrient-rich ground.", false, Form.Cap, 10, 0.52, 0.07, 0.1),
        new("duskpuff", "Lycoperdon perlatum", "fungus", "duskpuff", "Squat papery spore-puff of rich humus.", false, Form.Puff, 5, 0.85, 0.09, 0.08),
        new("embercrust_lichen", "Rhizocarpon geographicum", "lichen", "crust", "Slow crust-forming pioneer of stable bare mineral surfaces.", true, Form.Crust, 46, 0.5, 0.004, 0.16),
        new("embercup", "Atrichum undulatum", "moss", "embercup_moss", "Cushion moss of damp stone and rotten wood, ripening lit spore cups.", true, Form.Star, 55, 0.7, 0.12, 0.19),
        new("emberfan_fungus", "Trametes versicolor", "fungus", "bracket", "Persistent bracket decomposer of dead wood.", true, Form.Shelf, 7, 0.9, 0.12, 0.14),
        new("floodlace_moss", "Plagiomnium undulatum", "moss", "carpet", "Wet-bank moss of saturated soil just above standing water.", true, Form.Feather, 24, 0.85, 0.03, 0.28),
        new("ghostbonnet", "Mycena chlorophos", "fungus", "mushroom_cluster", "Bioluminescent detritus-feeding fungus of damp, shaded litter.", false, Form.Cap, 8, 0.38, 0.12, 0.1),
        new("glass_antlers", "Xylaria hypoxylon", "fungus", "glass_antlers", "Persistent translucent branching wood fungus.", true, Form.Coral, 7, 0.9, 0.14, 0.24),
        new("hagshair_lichen", "Usnea barbata", "lichen", "hagshair_lichen", "Hanging fruticose lichen of humid air.", true, Form.Pendant, 15, 0.65, 0.10, 0.13),
        new("moonshell_lichen", "Parmelia sulcata", "lichen", "moonshell_lichen", "Pale foliose lichen of shaded bark.", true, Form.Foliose, 18, 0.65, 0.012, 0.13),
        new("pearl_cushion_moss", "Leucobryum glaucum", "moss", "cushion", "Compact mound-forming moss of bright mineral surfaces.", true, Form.Star, 64, 0.65, 0.06, 0.14),
        new("pinequilt_moss", "Pleurozium schreberi", "moss", "pinequilt_moss", "Tufted acid-litter moss of dry needle-fall shade.", true, Form.Feather, 34, 0.45, 0.035, 0.34),
        new("quillfeather_moss", "Hylocomium splendens", "moss", "quillfeather_moss", "Prostrate shade moss whose fronds lie like fallen quills.", true, Form.Feather, 22, 1.0, 0.04, 0.34),
        new("rain_jelly", "Tremella mesenterica", "fungus", "rain_jelly", "Moisture-responsive jelly fungus that swells dramatically after rain and shrinks in dry spells.", true, Form.Jelly, 13, 0.8, 0.08, 0.24),
        new("reekhorn", "Phallus indusiatus", "fungus", "reekhorn", "Tall carrion-scented spike of wet litter.", false, Form.Bell, 4, 0.38, 0.22, 0.09),
        new("ruffle_lichen", "Flavoparmelia caperata", "lichen", "foliose", "Leafy lichen of weathered bark, rock and older surfaces.", true, Form.Foliose, 25, 0.85, 0.012, 0.13),
        new("rustbellow_moss", "Sphagnum magellanicum", "moss", "rustbellow_moss", "Acid-hummock moss of saturated peat.", true, Form.Star, 48, 0.9, 0.06, 0.2),
        new("silkgleam_moss", "Brachythecium rutabulum", "moss", "silkgleam_moss", "Sunlit bank moss with a silken sheen.", true, Form.Feather, 30, 0.65, 0.04, 0.28),
        new("slickmantle", "Pellia epiphylla", "moss", "slickmantle", "Thallose mat of dripping rock and bark.", true, Form.Foliose, 32, 0.75, 0.022, 0.28),
        new("starmoss", "Schistostega pennata", "moss", "carpet", "Bioluminescent low moss cushion for cool, moist ground.", true, Form.Star, 62, 0.65, 0.05, 0.16),
        new("starsplit", "Geastrum triplex", "fungus", "starsplit", "Star-crowned puffball of open soil.", false, Form.Puff, 4, 0.7, 0.06, 0.1),
        new("stoneknot_moss", "Grimmia pulvinata", "moss", "stoneknot_moss", "Drought-hardened pincushion of bare stone.", true, Form.Star, 58, 0.5, 0.045, 0.14),
        new("sunscald_lichen", "Xanthoria parietina", "lichen", "sunscald_lichen", "Radiant crust of sun-baked stone.", true, Form.Crust, 35, 0.8, 0.006, 0.16),
        new("velvetweave_moss", "Hypnum cupressiforme", "moss", "carpet", "Low mat-forming moss of humid soil and old wood.", true, Form.Feather, 34, 0.52, 0.025, 0.34),
        new("wispthread_moss", "Amblystegium serpens", "moss", "wispthread_moss", "Wispy thread moss of wet shaded timber.", true, Form.Feather, 20, 0.4, 0.025, 0.34),
    };
    public static IReadOnlyList<GraphSpeciesInfo> Species { get; } = Array.AsReadOnly(
        Entries.Select(e => new GraphSpeciesInfo(e.Id, e.Family, e.Silhouette, e.Habit, e.Attached)).ToArray());
    public static PlanGenerator? Create(FloraSpeciesDef species)
    {
        var e = Array.Find(Entries, x => x.Id == species.Id);
        return e == null ? null : new Colony(e, species);
    }
    private sealed class Colony : PlanGenerator
    {
        private readonly Entry e;
        private readonly double h, r;
        private readonly double[] primary, secondary;
        public Colony(Entry entry, FloraSpeciesDef species)
        {
            e = entry;
            primary = species.Color; secondary = species.Color2;
            h = species.Height > 0 ? species.Height : e.Height;
            r = species.RadiusAtMax > 0 ? species.RadiusAtMax : e.Radius;
        }
        public override string SpeciesId => e.Id;
        protected override void Design(PlanBuilder b)
        {
            bool moss = e.Family == "moss", lichen = e.Family == "lichen";
            // Missing content fixtures use the plant-green default; fungi need a neutral fallback.
            bool defaultFungus = e.Family == "fungus" && primary[0] == .3 && primary[1] == .6 && primary[2] == .2;
            var green = defaultFungus ? PlantShapes.Srgb(.66,.49,.34) : PlantShapes.Srgb(primary[0], primary[1], primary[2]);
            var accent = defaultFungus ? PlantShapes.Srgb(.78,.68,.55) : PlantShapes.Srgb(secondary[0], secondary[1], secondary[2]);
            // The legacy ghostbonnet reads as icy pale in daylight; this is reflected
            // pigment only, independent of any species emission settings.
            if (e.Id == "ghostbonnet")
            {
                green = PlantShapes.Srgb(.82, .90, .94);
                accent = PlantShapes.Srgb(.71, .82, .88);
            }
            Tissue colonyTissue = moss || lichen ? Tissue.Green : e.Id == "ghostbonnet" ? Tissue.PaleFungal : Tissue.Fungal;
            ushort Shape(string name, OrganSurfaceForm form, int lobes = 0) => b.AddShape(new OrganShape {
                Name = e.Id + name, Type = OrganType.Flower, SurfaceForm = form,
                WidthProfile = PlantShapes.Profile(t => e.Id == "ashcomb" && name == "_cap" ? .5 * Math.Pow(Math.Max(0, 1-t), .38) * (.78 + .22*Math.Sin(Math.PI*t)) : form switch {
                    OrganSurfaceForm.MushroomCap or OrganSurfaceForm.Shelf => .5 * Math.Sqrt(Math.Max(0, 1 - t*t)),
                    OrganSurfaceForm.Cup or OrganSurfaceForm.Trumpet => .12 + .38 * Math.Pow(t, .7),
                    OrganSurfaceForm.Needle => .5 * Math.Pow(Math.Max(0, Math.Sin(Math.PI*t)), .4),
                    OrganSurfaceForm.Disc or OrganSurfaceForm.Star => .5 * Math.Sin(Math.PI*t),
                    _ => .5 * Math.Pow(Math.Max(0, Math.Sin(Math.PI*t)), .65)
                }),
                RadialPitDepth = e.Id == "ashcomb" && name == "_cap" ? .30f : 0,
                LobeCount = lobes, LobeDepth = .16f, RimCurl = .18f, EdgeWave = .12f, EdgeWaveFreq = 7,
                ColorAdaxial = green, ColorAbaxial = accent, Roughness = e.Id == "rain_jelly" ? .28f : .72f, Transmission = e.Id == "rain_jelly" ? .65f : .3f
            });
            ushort blade = Shape("_blade", OrganSurfaceForm.Lamina);
            ushort needle = Shape("_needle", OrganSurfaceForm.Needle);
            ushort egg = Shape("_egg", OrganSurfaceForm.Revolved);
            ushort cap = Shape("_cap", e.Form switch {
                Form.Cap => e.Id == "ashcomb" ? OrganSurfaceForm.Revolved : OrganSurfaceForm.MushroomCap, Form.Bell => OrganSurfaceForm.MushroomCap,
                Form.Cup => OrganSurfaceForm.Cup, Form.Shelf => OrganSurfaceForm.Shelf,
                Form.Puff or Form.Jelly => OrganSurfaceForm.Revolved, Form.Crust => OrganSurfaceForm.Disc,
                _ => OrganSurfaceForm.Lamina }, e.Form == Form.Jelly ? 7 : 5);
            // A short basal mycelium/rhizoid network is the parent for all colony shoots.
            int root = b.AddAxis(-1, 0, Vec3.Up, Math.Max(.001, h*.025), Vec3.Zero,
                Math.Max(.0003,r*.018), Math.Max(.0002,r*.014), 0, colonyTissue, 0, e.Family == "fungus" ? .04 : .1, e.Family == "fungus" ? OrganFlags.Hidden : OrganFlags.None);
            double phase = b.Range(0, Math.Tau);
            for (int i = 0; i < e.Count; i++)
            {
                double az = phase + i * 2.399963229728653;
                var radial = PlantMath.Horizontal(az);
                double distance = r * e.Spread * Math.Sqrt((i+.5)/e.Count);
                int runner = b.AddAxis(root, .8, radial + Vec3.Up*.025, Math.Max(.001,distance), Vec3.Zero,
                    Math.Max(.00015,r*.009), Math.Max(.0001,r*.006), 1, colonyTissue, .03, e.Family == "fungus" ? .08 : .24, e.Family == "fungus" ? OrganFlags.Hidden : OrganFlags.None);
                double variation = b.Range(.72,1.15);
                var dir = Vec3.Up + radial * (e.Form == Form.Feather ? 1.8 : e.Form == Form.Pendant ? .75 : .18);
                if (e.Form == Form.Pendant) dir = radial + Vec3.Up * -.6;
                double shootHeight = h * variation * (e.Form switch { Form.Feather => .65, Form.Cup => .12, Form.Shelf => .22, Form.Puff => .18, Form.Jelly => .10, _ => .78 });
                // Fruiting bodies emerge from a much thinner mycelial runner; their stipes
                // are independently thick rather than clamped to the runner's radius.
                double shootRadius = e.Family == "fungus" ? r * (e.Form switch {
                    Form.Cap => e.Id == "barrowbole" ? .12 : e.Id == "ashcomb" ? .065 : .024,
                    Form.Bell => e.Id == "reekhorn" ? .07 : .055,
                    Form.Cup => .028, Form.Shelf => .05, Form.Puff => .085,
                    Form.Coral => .036, Form.Jelly => .045, _ => .02
                }) : Math.Max(.00012, r*.006);
                int shoot = b.AddAxis(runner, .96, dir, Math.Max(.001,shootHeight),
                    e.Form == Form.Pendant ? Vec3.Up * -.7 : radial*.15,
                    shootRadius, shootRadius*.62, 2, colonyTissue, .1, e.Family == "fungus" ? .12 : .3,
                    e.Family == "fungus" ? OrganFlags.Basal : OrganFlags.None);
                double delay = .15 + .1 * i/e.Count;
                if (e.Form is Form.Star or Form.Feather)
                {
                    int levels = e.Form == Form.Star ? (e.Id is "bogglass_moss" or "rustbellow_moss" ? 2 : 3) : 5;
                    for (int j=0;j<levels;j++)
                    {
                        double t = e.Id is "bogglass_moss" or "rustbellow_moss" ? .68 + .28*j/(levels-1.0) : .22 + .72*j/(levels-1.0);
                        int sides = e.Form == Form.Star ? (e.Id is "bogglass_moss" or "rustbellow_moss" ? 7 : 5) : 2;
                        for (int q=0;q<sides;q++)
                        {
                            double a = az + (e.Form == Form.Star ? q*Math.Tau/sides+j*.6 : q*Math.PI + Math.PI/2);
                            var leafdir = PlantMath.Horizontal(a)*.85 + Vec3.Up*(e.Form == Form.Star ? .4 : .13);
                            double len = Math.Max(.001, h*(e.Form == Form.Star ? (e.Id is "bogglass_moss" or "rustbellow_moss" ? .48 : .34) : .65)*(1-.35*t));
                            b.AddFlat(shoot,t,leafdir,len,len*.13,.00012,OrganType.Flower,needle,3,delay,.18);
                        }
                    }
                    if ((e.Id == "capstar_moss" || e.Id == "embercup") && i%5 == 0)
                    {
                        int seta = b.AddAxis(shoot,.95,Vec3.Up,h*.7,radial*.1,.0003,.00016,3,Tissue.Green,.35,.25);
                        b.AddFlat(seta,.95,Vec3.Up,h*.12,h*.035,.0001,OrganType.Flower,cap,4,.5,.15);
                    }
                }
                else if (e.Form is Form.Coral or Form.Pendant)
                {
                    for (int j=0;j<3;j++)
                    {
                        var branch = PlantMath.Horizontal(az+j*2.1)*.65 + (e.Form == Form.Pendant ? Vec3.Up*-.5 : Vec3.Up*.8);
                        int fork = b.AddAxis(shoot,.45+j*.19,branch,Math.Max(.001,h*(.32-j*.04)),Vec3.Zero,
                            e.Family=="fungus" ? shootRadius*.55 : Math.Max(.0001,r*.003),
                            e.Family=="fungus" ? shootRadius*.28 : .00008,3,colonyTissue,delay,.24);
                        for (int q=0;q<2;q++)
                            b.AddAxis(fork,.8,branch+PlantMath.Horizontal(az+q*Math.PI)*.4,Math.Max(.001,h*.15),Vec3.Zero,
                                e.Family=="fungus" ? shootRadius*.25 : .00012,
                                e.Family=="fungus" ? shootRadius*.10 : .00006,4,colonyTissue,.3,.18);
                    }
                }
                else if (e.Form is Form.Foliose or Form.Crust)
                {
                    int lobes = e.Form == Form.Crust ? 3 : 5;
                    for(int j=0;j<lobes;j++)
                    {
                        var d = PlantMath.Horizontal(az + j*Math.Tau/lobes) + Vec3.Up*(e.Form == Form.Crust ? .08 : .28);
                        double len = r * (e.Form == Form.Crust ? .065 : e.Id == "slickmantle" ? .28 : .18);
                        b.AddFlat(shoot,.45,d,len,len*(e.Id == "slickmantle" ? .7 : .38),.00015,OrganType.Flower,blade,3,delay,.25);
                    }
                    if (e.Form == Form.Crust && i%3 == 0)
                        b.AddFlat(shoot,.98,Vec3.Up,Math.Max(.001,h*.3),r*.018,.0001,OrganType.Flower,cap,3,.3,.18);
                }
                else
                {
                    double width = r * (e.Form switch { Form.Cap => e.Id == "ashcomb" ? .27 : .23, Form.Bell => .18, Form.Cup => .25,
                        Form.Shelf => .32, Form.Puff => .25, Form.Jelly => .14, _ => .2 });
                    double axial = h * (e.Form switch { Form.Cap => e.Id == "ashcomb" ? .58 : e.Id == "ghostbonnet" ? .34 : .22, Form.Bell => .28, Form.Cup => .52,
                        Form.Shelf => .12, Form.Puff => .55, Form.Jelly => .45, _ => .3 });
                    var d = e.Form == Form.Shelf ? radial + Vec3.Up*.3 : Vec3.Up;
                    b.AddFlat(shoot,.94,d,Math.Max(.001,axial),width*variation,.0003,OrganType.Flower,cap,3,delay,.28);
                    if (e.Id is "reekhorn" or "carrion_bell")
                        for(int q=0;q<7;q++)
                        {
                            var veilDir = PlantMath.Horizontal(az+q*Math.Tau/7)*.65 + Vec3.Up*-.75;
                            b.AddFlat(shoot,.94,veilDir,h*.42,r*.055,.00015,OrganType.Flower,blade,3,.32,.22);
                        }
                    if (e.Id == "rain_jelly")
                        for(int q=0;q<5;q++)
                        {
                            var fold = PlantMath.Horizontal(az+q*Math.Tau/5)*.65 + Vec3.Up*.75;
                            b.AddFlat(shoot,.85,fold,h*.65,r*.13,.0002,OrganType.Flower,blade,3,.22,.25);
                        }
                    if (e.Id == "starsplit")
                        for(int q=0;q<7;q++) b.AddFlat(shoot,.05,PlantMath.Horizontal(az+q*Math.Tau/7),r*.24,r*.065,.0003,OrganType.Flower,blade,3,.24,.2);
                    if (e.Id == "cradlecup")
                        for(int q=0;q<3;q++) b.AddFlat(shoot,.98,PlantMath.Horizontal(az+q*Math.Tau/3)*.12+Vec3.Up,r*.045,r*.035,.0002,OrganType.Flower,egg,4,.4,.15);
                }
            }
        }
    }
}
