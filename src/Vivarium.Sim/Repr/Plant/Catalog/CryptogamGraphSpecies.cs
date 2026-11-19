using System;
using System.Collections.Generic;
using System.Linq;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>Individually authored colony forms. Positions and growth gates are deterministic per plant.</summary>
public static class CryptogamGraphSpecies
{
    // Fungi: dry caps .6-.75, moist/translucent/slimy low; moss .85-.95; lichen .88-.97. Stems/branches slightly drier than caps.
    private static float CryptoRough(string id, string part)
    {
        float cap = id switch
        {
            "ashcomb" => .68f, "barrowbole" => .56f, "bonecup" => .52f, "candlecoral" => .72f, "cradlecup" => .74f, "dewbonnet" => .56f,
            "duskpuff" => .90f, "emberfan_fungus" => .76f, "ghostbonnet" => .38f, "glass_antlers" => .40f, "rain_jelly" => .28f,
            "reekhorn" => .34f, "carrion_bell" => .36f, "starsplit" => .72f,
            "bogglass_moss" => .86f, "capstar_moss" => .88f, "embercup" => .90f, "floodlace_moss" => .86f, "pearl_cushion_moss" => .94f,
            "pinequilt_moss" => .92f, "quillfeather_moss" => .90f, "rustbellow_moss" => .87f, "silkgleam_moss" => .85f, "slickmantle" => .62f,
            "starmoss" => .90f, "stoneknot_moss" => .95f, "velvetweave_moss" => .91f, "wispthread_moss" => .87f,
            "antlerlace_lichen" => .94f, "ashfingers_lichen" => .92f, "hagshair_lichen" => .90f, "moonshell_lichen" => .93f,
            "ruffle_lichen" => .95f, "embercrust_lichen" => .97f, "sunscald_lichen" => .96f, _ => .72f
        };
        bool fungus = id is "ashcomb" or "barrowbole" or "bonecup" or "candlecoral" or "cradlecup" or "dewbonnet" or "duskpuff" or "emberfan_fungus"
            or "ghostbonnet" or "glass_antlers" or "rain_jelly" or "reekhorn" or "carrion_bell" or "starsplit";
        return fungus && part != "_cap" && id != "rain_jelly" ? cap + .06f : cap;
    }
    private enum Form { Cap, Bell, Cup, Shelf, Puff, Coral, Jelly, Star, Feather, Foliose, Crust, Pendant }
    private sealed record Entry(string Id, string Counterpart, string Family, string Silhouette, string Habit, bool Attached,
        Form Form, int Count, double Spread, double Height, double Radius);
    private static readonly Entry[] Entries = {
        new("antlerlace_lichen", "Cladonia rangiferina", "lichen", "fruticose", "Dry branching lichen of open lean ground and stable mineral surfaces.", true, Form.Coral, 17, 0.45, 0.07, 0.13),
        new("ashcomb", "Morchella esculenta", "fungus", "ashcomb", "Spring honeycomb of hardwood shade.", false, Form.Cap, 4, 0.84, 0.16, 0.09),
        new("ashfingers_lichen", "Thamnolia vermicularis", "lichen", "ashfingers_lichen", "Pale branching lichen of dry heath.", true, Form.Coral, 22, 0.31, 0.04, 0.13),
        new("barrowbole", "Boletus edulis", "fungus", "barrowbole", "Stout bolete of rich soil.", false, Form.Cap, 3, 1.05, 0.14, 0.14),
        new("bogglass_moss", "Sphagnum capillifolium", "moss", "capitula", "Water-holding bog moss of saturated nutrient-poor ground.", true, Form.Star, 48, 0.8, 0.05, 0.2),
        new("bonecup", "Peziza vesiculosa", "fungus", "bonecup_fungus", "Wood-decaying cup fungus of damp, shaded litter.", true, Form.Cup, 12, 1.08, 0.075, 0.16),
        new("candlecoral", "Ramaria formosa", "fungus", "candlecoral", "Upright coral fungus of buried wood.", false, Form.Coral, 10, 0.6, 0.11, 0.12),
        new("capstar_moss", "Polytrichum commune", "moss", "capstar_moss", "Star-leaved tall moss ripening gold spore-capsules.", true, Form.Star, 38, 1.0, 0.15, 0.19),
        new("carrion_bell", "Phallus impudicus", "fungus", "carrion_bell", "Short-lived dramatic stinkhorn analogue that erupts from rich litter and rapidly returns to detritus.", false, Form.Bell, 3, 0.65, 0.28, 0.19),
        new("cradlecup", "Crucibulum laeve", "fungus", "cradlecup", "Tiny nest fungus of wet wood.", true, Form.Cup, 9, 0.88, 0.025, 0.06),
        new("dewbonnet", "Mycena galericulata", "fungus", "mushroom_cluster", "Small litter decomposer of humid nutrient-rich ground.", false, Form.Cap, 10, 0.78, 0.07, 0.1),
        new("duskpuff", "Lycoperdon perlatum", "fungus", "duskpuff", "Squat papery spore-puff of rich humus.", false, Form.Puff, 5, 1.05, 0.09, 0.08),
        new("embercrust_lichen", "Rhizocarpon geographicum", "lichen", "crust", "Slow crust-forming pioneer of stable bare mineral surfaces.", true, Form.Crust, 46, 0.5, 0.004, 0.16),
        new("embercup", "Atrichum undulatum", "moss", "embercup_moss", "Cushion moss of damp stone and rotten wood, ripening lit spore cups.", true, Form.Star, 55, 0.7, 0.12, 0.19),
        new("emberfan_fungus", "Trametes versicolor", "fungus", "bracket", "Persistent bracket decomposer of dead wood.", true, Form.Shelf, 7, 0.9, 0.12, 0.14),
        new("floodlace_moss", "Plagiomnium undulatum", "moss", "carpet", "Wet-bank moss of saturated soil just above standing water.", true, Form.Feather, 24, 0.85, 0.03, 0.28),
        new("ghostbonnet", "Mycena chlorophos", "fungus", "mushroom_cluster", "Bioluminescent detritus-feeding fungus of damp, shaded litter.", false, Form.Cap, 14, 0.72, 0.12, 0.1),
        new("glass_antlers", "Xylaria hypoxylon", "fungus", "glass_antlers", "Persistent translucent branching wood fungus.", true, Form.Coral, 7, 0.9, 0.14, 0.24),
        new("hagshair_lichen", "Usnea barbata", "lichen", "hagshair_lichen", "Hanging fruticose lichen of humid air.", true, Form.Pendant, 15, 0.65, 0.10, 0.13),
        new("moonshell_lichen", "Parmelia sulcata", "lichen", "moonshell_lichen", "Pale foliose lichen of shaded bark.", true, Form.Foliose, 18, 0.65, 0.012, 0.13),
        new("pearl_cushion_moss", "Leucobryum glaucum", "moss", "cushion", "Compact mound-forming moss of bright mineral surfaces.", true, Form.Star, 64, 0.65, 0.06, 0.14),
        new("pinequilt_moss", "Pleurozium schreberi", "moss", "pinequilt_moss", "Tufted acid-litter moss of dry needle-fall shade.", true, Form.Feather, 34, 0.45, 0.035, 0.34),
        new("quillfeather_moss", "Hylocomium splendens", "moss", "quillfeather_moss", "Prostrate shade moss whose fronds lie like fallen quills.", true, Form.Feather, 22, 1.0, 0.04, 0.34),
        new("rain_jelly", "Tremella mesenterica", "fungus", "rain_jelly", "Moisture-responsive jelly fungus that swells dramatically after rain and shrinks in dry spells.", true, Form.Jelly, 13, 0.8, 0.08, 0.24),
        new("reekhorn", "Phallus indusiatus", "fungus", "reekhorn", "Tall carrion-scented spike of wet litter.", false, Form.Bell, 4, 0.78, 0.22, 0.09),
        new("ruffle_lichen", "Flavoparmelia caperata", "lichen", "foliose", "Leafy lichen of weathered bark, rock and older surfaces.", true, Form.Foliose, 25, 0.85, 0.012, 0.13),
        new("rustbellow_moss", "Sphagnum magellanicum", "moss", "rustbellow_moss", "Acid-hummock moss of saturated peat.", true, Form.Star, 48, 0.9, 0.06, 0.2),
        new("silkgleam_moss", "Brachythecium rutabulum", "moss", "silkgleam_moss", "Sunlit bank moss with a silken sheen.", true, Form.Feather, 30, 0.65, 0.04, 0.28),
        new("slickmantle", "Pellia epiphylla", "moss", "slickmantle", "Thallose mat of dripping rock and bark.", true, Form.Foliose, 32, 0.75, 0.022, 0.28),
        new("starmoss", "Schistostega pennata", "moss", "carpet", "Bioluminescent low moss cushion for cool, moist ground.", true, Form.Star, 62, 0.65, 0.05, 0.16),
        new("starsplit", "Geastrum triplex", "fungus", "starsplit", "Star-crowned puffball of open soil.", false, Form.Puff, 2, 0.7, 0.06, 0.1),
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
            if (e.Id == "duskpuff")
            {
                green = PlantShapes.Srgb(.24, .27, .30);
                accent = PlantShapes.Srgb(.16, .18, .21);
            }
            Tissue colonyTissue = moss || lichen ? Tissue.Green : e.Id == "ghostbonnet" ? Tissue.PaleFungal : Tissue.Fungal;
            ushort Shape(string name, OrganSurfaceForm form, int lobes = 0) => b.AddShape(new OrganShape {
                Name = e.Id + name, Type = OrganType.Flower, SurfaceForm = form,
                WidthProfile = PlantShapes.Profile(t => e.Id == "ghostbonnet" && name == "_cap" ? .5 * Math.Pow(Math.Max(0, 1 - Math.Pow(t, 3.2)), .5) :
                    e.Id == "ashcomb" && name == "_cap" ? .5 * Math.Pow(Math.Max(0, 1-t), .38) * (.78 + .22*Math.Sin(Math.PI*t)) :
                    name == "_cap" && e.Id is "reekhorn" or "carrion_bell" ? .5 * Math.Pow(Math.Max(0, 1-t), .55) * (.72 + .28*Math.Sin(Math.PI*t)) :
                    name == "_needle" && e.Id is "bogglass_moss" or "rustbellow_moss" ? .5 * Math.Pow(Math.Max(0, Math.Sin(Math.PI*Math.Pow(t,.75))), .8) :
                    name == "_blade" && e.Id is "slickmantle" or "moonshell_lichen" or "ruffle_lichen" ? .5 * Math.Pow(Math.Max(0, Math.Sin(Math.PI*Math.Pow(t,1.35))), .55) : form switch {
                    OrganSurfaceForm.MushroomCap or OrganSurfaceForm.Shelf => .5 * Math.Sqrt(Math.Max(0, 1 - t*t)),
                    OrganSurfaceForm.Cup when e.Id is "bonecup" or "cradlecup" => .09 + .41*Math.Sin(.5*Math.PI*Math.Pow(t,.72)),
                    OrganSurfaceForm.Cup or OrganSurfaceForm.Trumpet => .12 + .38 * Math.Pow(t, .7),
                    OrganSurfaceForm.Needle => .5 * Math.Pow(Math.Max(0, Math.Sin(Math.PI*t)), .4),
                    OrganSurfaceForm.Disc or OrganSurfaceForm.Star => .5 * Math.Sin(Math.PI*t),
                    _ => .5 * Math.Pow(Math.Max(0, Math.Sin(Math.PI*t)), .65)
                }),
                RadialPitDepth = e.Id == "ashcomb" && name == "_cap" ? .30f : 0,
                // Gentle uneven outlines replace the identical five-point rim across fungi.
                LobeCount = e.Id == "ghostbonnet" && name == "_cap" ? 7 : e.Id == "carrion_bell" && name == "_cap" ? 10 : e.Id == "bonecup" && name == "_cap" ? 8 : name == "_cap" && e.Form is Form.Cap or Form.Bell or Form.Cup or Form.Shelf ? 0 : lobes,
                // Bonecup walls undulate toward the rim; the cup stays circular at its foot.
                LobeDepth = e.Id == "ghostbonnet" && name == "_cap" ? .14f : e.Id == "carrion_bell" && name == "_cap" ? .14f : e.Id == "bonecup" && name == "_cap" ? .09f : .10f, RimCurl = e.Form == Form.Cup ? .22f : .10f,
                EdgeWave = e.Form == Form.Foliose ? .18f : .08f, EdgeWaveFreq = 7,
                RadialAsymmetry = name == "_cap" ? e.Form switch {
                    Form.Cup => .12f, Form.Shelf => .14f, Form.Cap => .08f, Form.Bell => .07f,
                    Form.Puff or Form.Jelly => .10f, _ => 0f } : 0f,
                ColorAdaxial = e.Id == "reekhorn" && name == "_cap" ? PlantShapes.Srgb(0.32,0.147,0.107) : green, ColorAbaxial = e.Id == "reekhorn" && name == "_cap" ? PlantShapes.Srgb(0.22,0.106,0.074) : accent, Roughness = CryptoRough(e.Id, name), Transmission = e.Id == "rain_jelly" ? .65f : .3f
            });
            // The veil hangs from the cap as a flared net; its hem rolls unevenly.
            ushort veil = e.Id == "reekhorn" ? b.AddShape(new OrganShape {
                Name = e.Id + "_veil", Type = OrganType.Flower, SurfaceForm = OrganSurfaceForm.Cup,
                WidthProfile = PlantShapes.Profile(t => .13 + .47 * Math.Pow(t, .55)),
                LobeCount = 9, LobeDepth = .12f, RimCurl = .10f,
                RadialLattice = true, ColorAdaxial = accent, ColorAbaxial = accent,
                Roughness = .8f, Transmission = .3f
            }) : (ushort)0;
            // Two skirt shells of different lobe counts give a beat pattern, so the
            // hem folds at unequal lengths instead of one regular sawtooth.
            ushort skirt = e.Id == "carrion_bell" ? b.AddShape(new OrganShape {
                Name = "carrion_bell_skirt", Type = OrganType.Flower, SurfaceForm = OrganSurfaceForm.Cup,
                WidthProfile = PlantShapes.Profile(t => .16 + .36 * Math.Pow(t, .62)),
                LobeCount = 9, LobeDepth = .14f, RimCurl = .16f, ColorAdaxial = accent, ColorAbaxial = accent,
                Roughness = .8f, Transmission = .3f
            }) : (ushort)0;
            ushort skirtFold = e.Id == "carrion_bell" ? b.AddShape(new OrganShape {
                Name = "carrion_bell_skirt_fold", Type = OrganType.Flower, SurfaceForm = OrganSurfaceForm.Cup,
                WidthProfile = PlantShapes.Profile(t => .14 + .30 * Math.Pow(t, .50)),
                LobeCount = 7, LobeDepth = .22f, RimCurl = .24f, ColorAdaxial = accent, ColorAbaxial = accent,
                Roughness = .8f, Transmission = .3f
            }) : (ushort)0;
            ushort blade = Shape("_blade", OrganSurfaceForm.Lamina);
            ushort needle = Shape("_needle", OrganSurfaceForm.Needle);
            ushort egg = Shape("_egg", OrganSurfaceForm.Revolved);
            ushort cap = Shape("_cap", e.Form switch {
                Form.Cap => e.Id == "ashcomb" ? OrganSurfaceForm.Revolved : OrganSurfaceForm.MushroomCap, Form.Bell => OrganSurfaceForm.MushroomCap,
                Form.Cup => OrganSurfaceForm.Cup, Form.Shelf => OrganSurfaceForm.Shelf,
                Form.Puff or Form.Jelly => OrganSurfaceForm.Revolved, Form.Crust => OrganSurfaceForm.Disc,
                _ => OrganSurfaceForm.Lamina }, e.Form == Form.Jelly ? 7 : 5);
            // Age-dependent outlines share the colony but not the mature cap profile:
            // a young bolete stays convex, and older bonnets open and lift their rims.
            ushort capYoung = e.Id == "barrowbole" ? b.AddShape(new OrganShape {
                Name = "barrowbole_cap_young", Type = OrganType.Flower, SurfaceForm = OrganSurfaceForm.MushroomCap,
                WidthProfile = PlantShapes.Profile(t => .5 * Math.Pow(Math.Max(0, 1 - Math.Pow(t, 1.55)), .58)),
                RadialAsymmetry = .07f, RimCurl = .06f, Roughness = .56f, Transmission = .3f,
                ColorAdaxial = green, ColorAbaxial = accent
            }) : cap;
            ushort capOpen = e.Id == "dewbonnet" ? b.AddShape(new OrganShape {
                Name = "dewbonnet_cap_open", Type = OrganType.Flower, SurfaceForm = OrganSurfaceForm.MushroomCap,
                WidthProfile = PlantShapes.Profile(t => .5 * Math.Sqrt(Math.Max(0, 1 - Math.Pow(t, 2.6))) * (.94 + .12 * Math.Pow(t, 3))),
                LobeCount = 9, LobeDepth = .07f, RadialAsymmetry = .10f, RimCurl = .16f,
                Roughness = .56f, Transmission = .3f, ColorAdaxial = green, ColorAbaxial = accent
            }) : cap;
            // A short basal mycelium/rhizoid network is the parent for all colony shoots.
            int root = b.AddAxis(-1, 0, Vec3.Up, Math.Max(.001, h*.025), Vec3.Zero,
                Math.Max(.0003,r*.018), Math.Max(.0002,r*.014), 0, colonyTissue, 0, e.Family == "fungus" ? .04 : .1, OrganFlags.Hidden);
            double phase = b.Range(0, Math.Tau);
            if (e.Id == "duskpuff")
            {
                // A staggered family of uneven puffs, not copies of one lathed bead.
                // Each individual keeps its own shoulders, taper, tilt and pigment.
                var placedPuffs = new List<(Vec3 Pos, double Rad)>(7);
                for (int i = 0; i < 7; i++)
                {
                    double a = phase + i * 2.399963229728653 + b.Range(-.22, .22);
                    var radial = PlantMath.Horizontal(a);
                    double distance = i == 0 ? 0 : r * (i % 3 == 0 ? b.Range(.52, .72) : b.Range(.82, 1.16));
                    double fullness = b.Range(2.02, 2.34), shoulder = b.Range(-.07, .07), shade = b.Range(.84, 1.14);
                    float asym = (float)b.Range(.12, .27);
                    double width = r * b.Range(.22, .32), height = h * b.Range(.44, .78);
                    var pos = radial * distance;
                    for (int iter = 0; iter < 16; iter++)
                    {
                        bool moved = false;
                        for (int prev = 0; prev < placedPuffs.Count; prev++)
                        {
                            var delta = pos - placedPuffs[prev].Pos;
                            double minDist = (width + placedPuffs[prev].Rad) * 1.14;
                            double dist = delta.Length;
                            if (dist < minDist)
                            {
                                var pushDir = dist > 1e-6 ? delta / dist : PlantMath.Horizontal(a + prev * 1.1);
                                pos += pushDir * (minDist - dist);
                                moved = true;
                            }
                        }
                        if (!moved) break;
                    }
                    placedPuffs.Add((pos, width));
                    distance = pos.Length;
                    if (distance > 1e-6) radial = pos / distance;
                    int anchor = b.AddAxis(root, .2, radial, Math.Max(.001, distance), Vec3.Zero,
                        .0002, .0001, 1, Tissue.Fungal, .01, .1, OrganFlags.Hidden);
                    ushort puff = b.AddShape(new OrganShape {
                        Name = "duskpuff_body_" + i, Type = OrganType.Flower, SurfaceForm = OrganSurfaceForm.Revolved,
                        WidthProfile = PlantShapes.Profile(t => .5 * Math.Sqrt(Math.Max(0, 1-Math.Pow(Math.Abs(2*t-1), fullness)))
                            * (1+shoulder*Math.Sin(Math.PI*t)*Math.Sin(3*Math.PI*t))),
                        RadialAsymmetry = asym, LobeCount = 0,
                        RimCurl = 0, Roughness = .9f, Transmission = .04f,
                        ColorAdaxial = green * shade, ColorAbaxial = accent
                    });
                    var bodyDir = Vec3.Up + radial * b.Range(-.14, .14);
                    int bodyIdx = b.AddFlat(anchor, .95, bodyDir, height, width,
                        .0003, OrganType.Flower, puff, 2, .12 + i * .018, .32);
                    b[bodyIdx].P0 = new Vec3(pos.X, 0, pos.Z);
                    b[bodyIdx].P1 = b[bodyIdx].P0 + b[bodyIdx].T0 * height;
                }
                return;
            }
            if (e.Id == "emberfan_fungus")
            {
                // A vertical bark-contact patch carries sessile, overlapping shelves.
                // +Z is outward from the wood; the shelf tops stay level in the world.
                for (int i = 0; i < 7; i++)
                {
                    double y = h * (.02 + .075 * i + b.Range(-.018,.025)), x = r * b.Range(-.18, .18);
                    int rise = b.AddAxis(root, 0, Vec3.Up, Math.Max(.001, y), Vec3.Zero,
                        .0002, .0001, 1, Tissue.Fungal, .02, .12, OrganFlags.Hidden);
                    int anchor = b.AddAxis(rise, 1, x < 0 ? -Vec3.Right : Vec3.Right,
                        Math.Max(.001, Math.Abs(x)), Vec3.Zero, .0001, .00008, 2, Tissue.Fungal,
                        .03, .1, OrganFlags.Hidden);
                    ushort fan = b.AddShape(new OrganShape {
                        Name = "emberfan_bracket_" + i, Type = OrganType.Flower, SurfaceForm = OrganSurfaceForm.Shelf,
                        WidthProfile = PlantShapes.Profile(t => .5 * Math.Pow(Math.Max(0, Math.Sin(Math.PI * t)), .42)),
                        LobeCount = 0, RadialAsymmetry = (float)b.Range(.12, .26),
                        ColorAdaxial = green * b.Range(.87, 1.10), ColorAbaxial = accent,
                        Roughness = .76f, Transmission = .03f, ShellThickness = .022f
                    });
                    double bearing=b.Range(-.38,.38);
                    b.AddFlat(anchor, .98, Vec3.Forward*Math.Cos(bearing)+Vec3.Right*Math.Sin(bearing), r * b.Range(.48, 1.10),
                        r * b.Range(.34, .86), .0004, OrganType.Flower, fan, 3, .12 + i * .035, .27);
                }
                return;
            }
            if (e.Id == "starsplit")
            {
                // A single squat puff with a deeply open star mouth. The cup is a
                // closed-bottom shell: its white outside and orange inside share a rim.
                ushort body = b.AddShape(new OrganShape {
                    Name = "starsplit_open_body", Type = OrganType.Flower, SurfaceForm = OrganSurfaceForm.Cup,
                    // The mouth widens so the split occupies the whole crown rather than
                    // sitting as a small inset star on a closed pebble.
                    WidthProfile = PlantShapes.Profile(t => .5 * Math.Sqrt(Math.Max(0, 1 - Math.Pow(2*t-1, 2))) * (1-.40*Math.Pow(t, 6)) + .30*Math.Pow(t,5.2) + .002, 33),
                    RadialAsymmetry = .22f, RimCurl=.6f, LobeCount = 5, LobeDepth = .56f, ShellThickness = .055f,
                    ColorAdaxial = PlantShapes.Srgb(.98,.98,.94), ColorAbaxial = PlantShapes.Srgb(0.57,0.259,0.132),
                    Roughness = .72f, Transmission = .03f
                });
                // Seated into the soil with thick fleshy lips and subtle lean.
                int bodyFlat = b.AddFlat(root, 0, Vec3.Up + PlantMath.Horizontal(phase) * .02, h*1.45, r*.88, .0003, OrganType.Flower, body, 1, .03, .30);
                b[bodyFlat].P0 = new Vec3(0, .002, 0);
                b[bodyFlat].P1 = b[bodyFlat].P0 + b[bodyFlat].T0 * (h * 1.45);
                return;
            }
            var placedFungi = new List<(Vec3 Pos, double Rad)>(e.Count);
            for (int i = 0; i < e.Count; i++)
            {
                // A loose colony follows the existing golden-angle distribution, with
                // individual shoots occupying slightly different distances and bearings.
                double az = phase + i * 2.399963229728653 + b.Range(-.22, .22) + .22*Math.Sin(i*1.71+phase);
                var radial = PlantMath.Horizontal(az);
                double distance = r * e.Spread * Math.Sqrt((i+.5)/e.Count) * b.Range(.78, 1.08) * (1+.19*Math.Sin(i*2.37+phase*.6));
                if (e.Id == "barrowbole" && i > 0) distance = Math.Max(distance, r * .46);
                if (moss || lichen)
                    distance *= .78 + .27*Math.Cos(2*(az-phase)) + .18*Math.Sin(3*(az-phase)+.8);
                double variation = e.Id == "ghostbonnet" && i%5==0 ? b.Range(.38,.58) : b.Range(.72,1.15);
                variation *= 1 + .18*Math.Sin(i*1.91+phase);
                if (e.Id == "barrowbole") variation *= i == 0 ? .68 : i == 1 ? .90 : 1.12;
                if (e.Id == "ashcomb") variation *= .68 + .13 * i;
                else if (e.Id == "reekhorn") variation *= .66 + .14 * i;
                else if (e.Id == "dewbonnet") variation *= .58 + .11 * (i % 5);
                if (e.Id is "ashcomb" or "bonecup" or "cradlecup" or "dewbonnet" or "duskpuff" or "reekhorn")
                {
                    double myRad = e.Id switch
                    {
                        "ashcomb" => r * .25 * variation,
                        "bonecup" => r * .185 * variation,
                        "cradlecup" => r * .165 * variation,
                        "dewbonnet" => r * .195 * variation,
                        "duskpuff" => r * .36 * variation,
                        "reekhorn" => r * .26 * variation,
                        _ => 0,
                    };
                    var pos = radial * distance;
                    for (int iter = 0; iter < 12; iter++)
                    {
                        bool moved = false;
                        for (int prev = 0; prev < placedFungi.Count; prev++)
                        {
                            var delta = pos - placedFungi[prev].Pos;
                            double minDist = (myRad + placedFungi[prev].Rad) * 1.12;
                            double dist = delta.Length;
                            if (dist < minDist)
                            {
                                var pushDir = dist > 1e-6 ? delta / dist : PlantMath.Horizontal(az + prev * 1.1);
                                pos += pushDir * (minDist - dist);
                                moved = true;
                            }
                        }
                        if (!moved) break;
                    }
                    distance = Math.Max(.001, pos.Length);
                    if (distance > 1e-6)
                    {
                        radial = pos / distance;
                        az = Math.Atan2(radial.Z, radial.X);
                    }
                    placedFungi.Add((pos, myRad));
                }
                int runner = b.AddAxis(root, .8, radial + Vec3.Up*.025, Math.Max(.001,distance), Vec3.Zero,
                    Math.Max(.00015,r*.009), Math.Max(.0001,r*.006), 1, colonyTissue, .03, e.Family == "fungus" ? .08 : .24, OrganFlags.Hidden);
                if (moss) variation *= 1.28 - .55*Math.Clamp(distance/Math.Max(.001,r*e.Spread),0,1);
                var tangent = PlantMath.Horizontal(az + Math.PI/2);
                double lean = b.Range(-.22, .22);
                var dir = Vec3.Up + radial * (e.Form == Form.Feather ? 1.8 : e.Form == Form.Pendant ? .75 : .18) + tangent * lean;
                if (e.Form == Form.Pendant) dir = radial + Vec3.Up * -.6;
                double shootHeight = h * variation * (e.Form switch { Form.Star when e.Id == "embercup" => .36, Form.Feather => .65, Form.Cup => e.Id == "cradlecup" ? .03 : e.Id == "bonecup" ? .05 : .12, Form.Shelf => .22, Form.Puff => .025, Form.Jelly => .025, Form.Cap when e.Id == "barrowbole" => .42, Form.Bell when e.Id == "carrion_bell" => .48, _ => .78 });
                // Fruiting bodies emerge from a much thinner mycelial runner; their stipes
                // are independently thick rather than clamped to the runner's radius.
                double shootRadius = e.Family == "fungus" ? r * (e.Form switch {
                    Form.Cap => e.Id == "barrowbole" ? .23 : e.Id == "ashcomb" ? .065 : .024,
                    Form.Bell => e.Id == "reekhorn" ? .12 : .14,
                    Form.Cup => e.Id == "cradlecup" ? .070 : e.Id == "bonecup" ? .034 : .028, Form.Shelf => .05, Form.Puff => .085,
                    Form.Coral => .046, Form.Jelly => .045, _ => .02
                }) : Math.Max(.00012, r*.006);
                // New coral fruiting branches share a low fleshy collar before forking.
                if (e.Id is "glass_antlers" or "candlecoral")
                {
                    int collar = b.AddAxis(runner,.94,Vec3.Up+radial*.28,shootRadius*.9,radial*.30,
                        shootRadius*1.8,shootRadius,1,Tissue.Fungal,.06,.09,OrganFlags.Basal);
                    runner = collar;
                }
                int shoot;
                if (e.Id == "ghostbonnet")
                {
                    // Sinuous stipe: four welded segments whose bends alternate in a
                    // randomly oriented plane, so the stem snakes rather than leaning.
                    const int segs = 4;
                    double segLen = Math.Max(.001, shootHeight) / segs;
                    double wavePhase = b.Range(0, Math.Tau);
                    var swayA = PlantMath.Horizontal(az + b.Range(-1.2, 1.2));
                    var swayB = PlantMath.Horizontal(az + Math.PI/2 + b.Range(-.8, .8));
                    shoot = runner;
                    for (int k = 0; k < segs; k++)
                    {
                        double w0 = k + wavePhase;
                        var bend = swayA * (1.25 * Math.Sin(w0 * 2.1)) + swayB * (.8 * Math.Cos(w0 * 2.1 + .6)) + Vec3.Up * .15;
                        double ra = shootRadius * (1 - .38 * k / segs), rb2 = shootRadius * (1 - .38 * (k + 1) / segs);
                        int seg = b.AddAxis(shoot, k == 0 ? .96 : 1, k == 0 ? dir : b.TangentAt(shoot, 1), segLen,
                            bend, ra, rb2, 2, colonyTissue, .1 + k * .03, .04,
                            k == 0 ? OrganFlags.Basal : OrganFlags.Basal | OrganFlags.Continuous);
                        if (k > 0)
                        {
                            var prev = b[shoot]; var o = b[seg];
                            double len = o.T0.Length;
                            o.P0 = prev.P1; o.R0 = prev.R1;
                            o.T0 = PlantMath.SafeNormal(prev.T1, Vec3.Up) * len;
                            o.P1 = o.P0 + (o.T0 + o.T1) * .5;
                        }
                        shoot = seg;
                    }
                }
                else
                shoot = b.AddAxis(runner, .96, dir, Math.Max(.001,shootHeight),
                    e.Form == Form.Pendant ? Vec3.Up * -.7 + tangent * lean :
                    e.Form == Form.Feather ? tangent * b.Range(-.32, .32) + Vec3.Up * .13 :
                    radial * .15 + tangent * lean,
                    shootRadius, shootRadius*(e.Id == "cradlecup" ? .90 : .62), 2, colonyTissue, .1, e.Family == "fungus" ? .12 : .3,
                    e.Family == "fungus" ? OrganFlags.Basal : OrganFlags.None);
                double delay = .15 + .1 * i/e.Count;
                if (e.Form is Form.Star or Form.Feather)
                {
                    int levels = e.Form == Form.Star ? (e.Id is "bogglass_moss" or "rustbellow_moss" ? 2 : 3) : 5;
                    for (int j=0;j<levels;j++)
                    {
                        double t = e.Id is "bogglass_moss" or "rustbellow_moss" ? .68 + .28*j/(levels-1.0) : .22 + .72*j/(levels-1.0);
                        t = Math.Clamp(t + .035*Math.Sin(i*1.7+j*2.3+phase),.12,.98);
                        int sides = e.Form == Form.Star ? (e.Id is "bogglass_moss" or "rustbellow_moss" ? 7 : 5) : 2;
                        for (int q=0;q<sides;q++)
                        {
                            double a = az + (e.Form == Form.Star ? q*Math.Tau/sides+j*.6 : q*Math.PI + Math.PI/2) + b.Range(-.16,.16);
                            var leafdir = PlantMath.Horizontal(a)*.85 + Vec3.Up*(e.Form == Form.Star ? .4 : .13);
                            double len = Math.Max(.001, h*(e.Form == Form.Star ? (e.Id is "bogglass_moss" or "rustbellow_moss" ? .48 : .34) : .65)*(1-.35*t)*b.Range(.82,1.15));
                            b.AddFlat(shoot,t,leafdir,len,len*.13,.00012,OrganType.Flower,needle,3,delay,.18);
                        }
                    }
                    if ((e.Id == "capstar_moss" || e.Id == "embercup") && i%5 == 0)
                    {
                        int seta = b.AddAxis(shoot,.95,Vec3.Up,h*.7,radial*.1,.00048,.00024,3,Tissue.Green,.35,.25);
                        b.AddFlat(seta,.95,Vec3.Up,h*.12,h*.035,.0001,OrganType.Flower,cap,4,.5,.15);
                    }
                }
                else if (e.Form is Form.Coral or Form.Pendant)
                {
                    for (int j=0;j<3;j++)
                    {
                        double branchAngle = az+j*2.1+b.Range(-.25,.25);
                        // Candlecoral grows upright clubs; antler lichens spread more widely.
                        double branchSpread = e.Id == "candlecoral" ? b.Range(.24,.48) : b.Range(.5,.8);
                        var branch = PlantMath.Horizontal(branchAngle)*branchSpread + (e.Form == Form.Pendant ? Vec3.Up*-.5 : Vec3.Up*.8);
                        int fork = b.AddAxis(shoot,Math.Clamp(.45+j*.19+.07*Math.Sin(i*1.9+j*1.7+phase),.25,.94),branch,Math.Max(.001,h*(.32-j*.04)*b.Range(.8,1.2)),PlantMath.Horizontal(branchAngle+Math.PI/2)*b.Range(-.28,.28),
                            e.Family=="fungus" ? shootRadius*.55 : Math.Max(.0001,r*.003),
                            e.Family=="fungus" ? shootRadius*.28 : .00008,3,colonyTissue,delay,.24);
                        for (int q=0;q<2;q++)
                            b.AddAxis(fork,.8,branch+PlantMath.Horizontal(az+q*Math.PI)*.4,Math.Max(.001,h*.15*b.Range(.8,1.2)),PlantMath.Horizontal(branchAngle+Math.PI/2)*b.Range(-.25,.25),
                                e.Family=="fungus" ? shootRadius*.25 : .00012,
                                e.Family=="fungus" ? shootRadius*.10 : .00006,4,colonyTissue,.3,.18);
                    }
                }
                else if (e.Form is Form.Foliose or Form.Crust)
                {
                    int lobes = e.Form == Form.Crust ? 3 : 5;
                    for(int j=0;j<lobes;j++)
                    {
                        var d = PlantMath.Horizontal(az + j*Math.Tau/lobes + b.Range(-.22,.22)) + Vec3.Up*(e.Form == Form.Crust ? b.Range(.03,.14) : b.Range(.12,.48));
                        double len = r * (e.Form == Form.Crust ? .065 : e.Id == "slickmantle" ? .28 : .18) * b.Range(.78,1.18);
                        b.AddFlat(shoot,.45,d,len,len*(e.Id == "slickmantle" ? .7 : .38),.00015,OrganType.Flower,blade,3,delay,.25);
                    }
                    if (e.Form == Form.Crust && i%3 == 0)
                        b.AddFlat(shoot,.98,Vec3.Up,Math.Max(.001,h*.3),r*.018,.0001,OrganType.Flower,cap,3,.3,.18);
                }
                else
                {
                    double width = r * (e.Form switch { Form.Cap => e.Id == "ashcomb" ? .21 : e.Id == "barrowbole" ? .65 : e.Id == "dewbonnet" ? .18 : .23, Form.Bell => .18, Form.Cup => e.Id == "cradlecup" ? .145 : .165,
                        Form.Shelf => .32, Form.Puff => e.Id == "duskpuff" ? .34 : .48, Form.Jelly => .14, _ => .2 });
                    double axial = h * (e.Form switch { Form.Cap => e.Id == "ashcomb" ? .58 : e.Id == "ghostbonnet" ? .085 : .22, Form.Bell => .28, Form.Cup => e.Id == "cradlecup" ? .60 : .52,
                        Form.Shelf => .12, Form.Puff => e.Id == "duskpuff" ? .36 : .48, Form.Jelly => .45, _ => .3 });
                    var d = e.Id == "reekhorn" ? b.TangentAt(shoot,.94) :
                        e.Id == "ghostbonnet" ? PlantMath.SafeNormal(b.TangentAt(shoot,.94)*.35 + Vec3.Up, Vec3.Up) : e.Form == Form.Shelf ? radial + Vec3.Up*b.Range(.18,.42) :
                        e.Form == Form.Cap && e.Id == "barrowbole" ? Vec3.Up + radial*b.Range(-.26,.26) + tangent*b.Range(-.20,.20) :
                        e.Form == Form.Cup ? Vec3.Up + radial*(.13*Math.Sin(i*1.9+phase)) + tangent*(.12*Math.Cos(i*1.3+phase)) :
                        e.Id == "carrion_bell" ? b.TangentAt(shoot,.94) :
                        Vec3.Up + radial*b.Range(-.16,.16) + tangent*b.Range(-.12,.12);
                    int headHost=shoot; double headAt = e.Id == "dewbonnet" ? .88 : .94;
                    if(e.Id=="reekhorn") {
                        headHost=b.AddAxis(shoot,.94,d,h*.004,Vec3.Zero,.0001,.00008,3,Tissue.Fungal,delay,.05,OrganFlags.Hidden);
                        // Re-centre the head axis on the stalk; AddAxis starts on the parent
                        // surface, which would skew the cap and net veil to one side.
                        var ro=b[shoot]; var rh=b[headHost];
                        rh.P0=PlantMath.Hermite(ro.P0,ro.T0,ro.P1,ro.T1,.94);
                        rh.P1=rh.P0+(rh.T0+rh.T1)*.5;
                        headAt=1;
                    }
                    if(e.Id=="carrion_bell") {
                        headHost=b.AddAxis(shoot,.94,d,h*.004,Vec3.Zero,.0001,.00008,3,Tissue.Fungal,delay,.05,OrganFlags.Hidden);
                        var co=b[shoot]; var ch=b[headHost];
                        ch.P0=PlantMath.Hermite(co.P0,co.T0,co.P1,co.T1,.94);
                        ch.P1=ch.P0+(ch.T0+ch.T1)*.5;
                        headAt=0;
                    }
                    axial *= 1 + .13*Math.Sin(i*2.11+phase);
                    ushort headCap = e.Id == "barrowbole" && i == 0 ? capYoung : e.Id == "dewbonnet" && i % 3 == 1 ? capOpen : cap;
                    bool openBonnet = e.Id == "dewbonnet" && headCap == capOpen;
                    int headIdx = b.AddFlat(headHost,headAt,d,Math.Max(.001, axial * (openBonnet ? .78 : 1.0)),
                        width*variation*(openBonnet ? 1.14 : 1.0),.0003,OrganType.Flower,headCap,3,delay,.28);
                    if (e.Form is Form.Cap or Form.Cup && e.Id != "reekhorn" && e.Id != "carrion_bell")
                    {
                        var shootOrg = b[headHost];
                        var stemCenter = PlantMath.Hermite(shootOrg.P0, shootOrg.T0, shootOrg.P1, shootOrg.T1, headAt);
                        double capLen = (b[headIdx].P1 - b[headIdx].P0).Length;
                        var offset = e.Id == "barrowbole" ? radial * (shootRadius * .14) : Vec3.Zero;
                        b[headIdx].P0 = stemCenter + offset;
                        b[headIdx].P1 = b[headIdx].P0 + b[headIdx].T0 * capLen;
                    }
                    if (e.Id == "carrion_bell")
                    {
                        // Both skirt shells hang from one hidden host centred on the shaft
                        // (AddAxis otherwise starts on the stipe surface), so their folds
                        // emerge from the flesh without a separate collar seam.
                        int skirtHost = b.AddAxis(shoot,.92,b.TangentAt(shoot,.92),h*.004,Vec3.Zero,
                            .0001,.00006,3,Tissue.Fungal,delay,.05,OrganFlags.Hidden);
                        var so = b[shoot]; var ho = b[skirtHost];
                        ho.P0 = PlantMath.Hermite(so.P0, so.T0, so.P1, so.T1, .92);
                        ho.P1 = ho.P0 + (ho.T0 + ho.T1) * .5;
                        var hang = PlantMath.SafeNormal(-b.TangentAt(shoot,.94) + radial*.06, -Vec3.Up);
                        b.AddFlat(skirtHost,0,hang,h*.22,r*.28*variation,.00015,OrganType.Flower,skirt,3,delay,.28);
                        b.AddFlat(skirtHost,0,PlantMath.SafeNormal(hang + tangent*.14, -Vec3.Up),
                            h*.15,r*.22*variation,.00015,OrganType.Flower,skirtFold,3,delay,.30);
                    }
                    if (e.Id == "reekhorn")
                        b.AddFlat(headHost,headAt,PlantMath.SafeNormal(-b.TangentAt(shoot,.94) + radial*.10*Math.Sin(i*2.7+phase), -Vec3.Up),
                            h*.32,r*.24*variation*(1+.18*Math.Sin(i*1.83+phase)),.00015,OrganType.Flower,veil,3,delay,.28);
                    if (e.Id == "rain_jelly")
                        for(int q=0;q<5;q++)
                        {
                            var fold = PlantMath.Horizontal(az+q*Math.Tau/5+b.Range(-.2,.2))*b.Range(.5,.8) + Vec3.Up*b.Range(.55,.9);
                            b.AddFlat(shoot,.85,fold,h*.65,r*.13,.0002,OrganType.Flower,blade,3,.22,.25);
                        }
                    if (e.Id == "starsplit")
                        for(int q=0;q<6;q++) b.AddFlat(shoot,.05,PlantMath.Horizontal(az+q*Math.Tau/6) + Vec3.Up*.04,r*.56,r*.20,.0003,OrganType.Flower,blade,3,.24,.2);
                    if (e.Id == "cradlecup")
                        for(int q=0;q<3;q++) b.AddFlat(shoot,.98,PlantMath.Horizontal(az+q*Math.Tau/3)*.12+Vec3.Up,r*.045,r*.035,.0002,OrganType.Flower,egg,4,.4,.15);
                }
            }
            if (moss || lichen)
                AddCryptogamColonyUnderstory(b, root, colonyTissue, phase, green, accent);
        }

        // Catalog specimens need the low colony around their shoots. In the world,
        // CoverageRenderer builds the continuous terrain-following sheet separately and
        // extracts only an order-2 shoot subtree, so these root siblings are not doubled.
        private void AddCryptogamColonyUnderstory(PlanBuilder b, int root, Tissue tissue, double phase, Vec3 green, Vec3 accent)
        {
            bool moss = e.Family == "moss";
            bool emberMat = e.Id is "embercrust_lichen" or "embercup" or "floodlace_moss" or "moonshell_lichen" or "pearl_cushion_moss" or "pinequilt_moss" or "quillfeather_moss" or "ruffle_lichen" or "rustbellow_moss" or "starmoss" or "stoneknot_moss" or "velvetweave_moss" or "wispthread_moss";
            // Hanging beard lichen has a small attachment colony, rather than a ground carpet.
            double extent = r * e.Spread * (e.Form == Form.Pendant ? .30 : .9);
            ushort mat = b.AddShape(new OrganShape {
                Name = e.Id + "_colony_understory", Type = OrganType.Flower,
                SurfaceForm = OrganSurfaceForm.Lamina,
                WidthProfile = PlantShapes.Profile(t => .5 * Math.Pow(Math.Max(0, Math.Sin(Math.PI*t)), emberMat ? .28 : .55)),
                EdgeWave = emberMat ? .23f : .16f, EdgeWaveFreq = 7, RimCurl = emberMat ? .055f : .10f,
                ColorAdaxial = green, ColorAbaxial = e.Id is "embercup" or "floodlace_moss" or "pearl_cushion_moss" or "pinequilt_moss" or "quillfeather_moss" or "rustbellow_moss" or "starmoss" or "stoneknot_moss" or "velvetweave_moss" or "wispthread_moss" ? green*.78 : accent, Roughness = CryptoRough(e.Id, "_colony"), Transmission = .12f
            });
            // Broad interleaved radial blades share a common ground-level origin and
            // overlap across their length. Irregular shorter fringe lobes break up the
            // perimeter; neither layer uses suspended stems or isolated coins.
            int count = emberMat ? 42 : e.Form == Form.Pendant ? 20 : 32;
            for (int i = 0; i < count; i++)
            {
                double a = phase + i * Math.Tau / count + .10*Math.Sin(i*1.71+phase);
                var radial = PlantMath.Horizontal(a);
                // Project the colony in its growth direction, with two advancing lobes and a recessed side.
                double frontage = .78 + .27*Math.Cos(2*(a-phase)) + .18*Math.Sin(3*(a-phase)+.8);
                double offset = extent * frontage * (.10 + .09 * Math.Sin(i * 1.37));
                int anchor = b.AddAxis(root,0,radial,Math.Max(.0001,offset),Vec3.Zero,.00008,.00008,1,tissue,.01,.04,OrganFlags.Hidden);
                double length = extent * frontage * (1 + .16*Math.Sin(i*2.17) + .15*Math.Sin(i*.73+phase));
                b.AddFlat(anchor, 1, radial + Vec3.Up * (emberMat ? .003 : .012), Math.Max(.001, length), extent * (emberMat ? .51 : .43),
                    .00008, OrganType.Flower, mat, 3, emberMat ? .08 : .76, .12);
                int fringeAnchor = b.AddAxis(root, 0, radial, Math.Max(.001, extent * frontage * .68), Vec3.Zero,
                    .00008, .00008, 1, tissue, e.Id is "pearl_cushion_moss" or "pinequilt_moss" or "quillfeather_moss" or "ruffle_lichen" or "rustbellow_moss" or "stoneknot_moss" or "velvetweave_moss" or "wispthread_moss" ? .08 : .65, .05, OrganFlags.Hidden);
                double fringeAngle = a + (i % 2 == 0 ? .19 : -.19);
                b.AddFlat(fringeAnchor, .95, PlantMath.Horizontal(fringeAngle) + Vec3.Up * .035,
                    Math.Max(.001, extent * frontage * .40), extent * (moss ? .14 : .18), .00008,
                    OrganType.Flower, mat, 3, emberMat ? .15 : .78, .12);
            }
            // Interior lobes overlap at the origin and close the pinhole left where the
            // radiating first row departs, without restoring a central disc or spokes.
            for (int i = 0; i < 6; i++)
            {
                double a = phase + .7 + i * (Math.Tau / 6) + .2 * Math.Sin(i * 2.3 + phase);
                var radial = PlantMath.Horizontal(a);
                int centerAnchor = b.AddAxis(root, 0, -radial, Math.Max(.0001, extent * .22), Vec3.Zero, .00008, .00008, 1, tissue, .01, .03, OrganFlags.Hidden);
                b.AddFlat(centerAnchor, 1, radial + Vec3.Up * .012, Math.Max(.001, extent * (.58 + .10 * Math.Sin(i * 2.7 + phase))),
                    extent * (.44 + .05 * Math.Cos(i * 1.9 + phase)), .00008,
                    OrganType.Flower, mat, 3, emberMat ? .06 : .76, .12);
            }
            // Wispthread trails fine late threads past the patch edge so neighbouring
            // colonies bridge instead of meeting at a cut boundary.
            if (e.Id == "wispthread_moss")
                for (int i = 0; i < 6; i++)
                {
                    double a = phase + .35 + i * 1.93 + .32*Math.Sin(i*2.1+phase);
                    var radial = PlantMath.Horizontal(a);
                    b.AddFlat(root, 0, radial + Vec3.Up*.006, Math.Max(.001, extent * (1.22 + .26*Math.Sin(i*1.7+phase))),
                        extent * .028, .00005, OrganType.Flower, mat, 4, .8, .16);
                }
        }
    }
}
