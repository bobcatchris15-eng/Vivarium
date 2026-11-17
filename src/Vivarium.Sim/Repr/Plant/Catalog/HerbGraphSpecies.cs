using System;
using System.Collections.Generic;
using System.Linq;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>Individually authored herbaceous architecture. Dimensions scale with content ecology.</summary>
public static partial class HerbGraphSpecies
{
    // form, number of primary shoots, leaves/pairs per shoot, spread, leaf width, inflorescence character
    private sealed record Spec(string Id, string Counterpart, string Form, int Shoots, int Leaves, double Spread, double Width, int Bloom, string Habit);
    private static readonly Spec[] Table = {
        new("ashwire","fictional identity: airy pale upright tuft","grass",30,1,.85,.016,2,"fine dry bristle turf"),
        new("bannerreed","Phragmites australis","grass",11,3,.42,.075,5,"broad marsh reed with hanging panicles"),
        new("barbarrow","Sagittaria sagittifolia","forb",7,3,.70,.22,3,"emergent arrow leaf whorls"),
        new("blackneedle","Cirsium vulgare","rosette",12,2,.94,.17,5,"spiny basal leaves and bracted crowns"),
        new("blue_sundew","Drosera rotundifolia","trap",15,1,.95,.10,1,"low sundew colony and raised clubs"),
        new("bogfringe","Menyanthes trifoliata","pad",9,3,.95,.23,5,"floating trifoliate raft"),
        new("brightknot","Sedum acre","rosette",23,1,.98,.14,5,"many tight succulent stars"),
        new("brooklace","Callitriche stagnalis","runner",7,8,.96,.07,0,"creeping aquatic laced stems"),
        new("burlmantle","Arctium lappa","rosette",9,2,.95,.30,3,"huge felted basal leaves and burs"),
        new("burrflag","Sparganium emersum","grass",9,1,.78,.09,2,"ribbon leaves and burr stalks"),
        new("butterglint","Ranunculus acris","forb",6,3,.65,.18,5,"glossy meadow cups"),
        new("cinderberry","Fragaria vesca","runner",5,5,.95,.14,3,"trifoliate runners and fruit"),
        new("cloudtuft","Eriophorum angustifolium","grass",17,1,.32,.018,4,"narrow stems with cotton heads"),
        new("coalspike","Typha latifolia","grass",15,2,.30,.075,3,"dense emergent straps with spikes"),
        new("coinrunner","Lysimachia nummularia","runner",6,7,.98,.20,0,"round leaves along stolons"),
        new("cragset","Sempervivum tectorum","rosette",15,1,.72,.19,1,"compact sculptural stone rosette"),
        new("deepquill","Ceratophyllum demersum","whorl",7,7,.30,.018,0,"upright forked aquatic quills"),
        new("duskbristle","Luzula sylvatica","grass",24,1,.85,.035,1,"shaggy woodland fountain"),
        new("emberbracken","Pteridium aquilinum","fern",6,10,.95,.13,0,"triangular stiff fronds"),
        new("fenbead","fictional identity: delicate branching fen herb","whorl",10,5,.52,.14,0,"delicate branching stems and spaced small leaves"),
        new("fencomb","Myriophyllum spicatum","whorl",8,11,.62,.025,0,"fine submerged combs"),
        new("fenfoam","Filipendula ulmaria","forb",8,7,.72,.10,7,"branching froth over pinnate leaves"),
        new("frosttussock","fictional identity: airy pale upright tuft","grass",30,1,.77,.025,1,"arching fine blue tussock"),
        new("glassfinger","Carpobrotus edulis","runner",7,5,.88,.11,0,"fleshy angled gravel runners"),
        new("glasslily","Nymphaea alba","pad",8,1,.95,.32,1,"round pads and tall glass cup"),
        new("glassrush","Juncus articulatus","grass",19,1,.35,.025,0,"compact jointed vertical fans"),
        new("hallowsting","Urtica dioica","forb",8,6,.54,.17,3,"opposite serrated leaves and tassels"),
        new("heddlelace","Anthriscus sylvestris","forb",6,6,.78,.07,9,"hollow stalks and lace umbels"),
        new("hushclover","Oxalis acetosella","pad",8,3,.75,.20,5,"shamrock leaves under hovering flowers"),
        new("lanternsedge","Cyperus alternifolius","grass",13,2,.45,.055,7,"sedge fans and radiating lantern heads"),
        new("marrowbloom","Monotropa uniflora","forb",6,0,.27,.04,1,"leafless nodding pale bells"),
        new("marshkettle","Caltha palustris","pad",9,1,.85,.24,5,"glossy round leaves and cups"),
        new("mirepocket","Utricularia vulgaris","whorl",8,8,.83,.025,2,"feathery branches with little bladders"),
        new("mirrorleaf","Nuphar lutea","pad",11,1,.98,.29,2,"broad pond pads and pale cups"),
        new("mirrorstrap_fern","Asplenium scolopendrium","rosette",11,1,.65,.15,0,"undivided upright glossy fern straps"),
        new("mistviolet","Viola palustris","pad",7,1,.72,.20,5,"round basal leaves and nodding stars"),
        new("mooncoin","Dichondra argentea","runner",8,6,.96,.16,0,"wiry silver coin mat"),
        new("moonfern","Nephrolepis exaltata","fern",6,14,.75,.18,0,"luminous arching divided fronds"),
        new("paddlerib","Alisma plantago-aquatica","forb",7,3,.68,.22,6,"ribbed paddles and airy whorls"),
        new("pitcher_plant","fictional identity: curved red and cream vessels","pitcher",7,1,.75,.20,1,"upright hollow pitchers with hoods"),
        new("plumegrass","Calamagrostis acutiflora","grass",10,1,.36,.035,6,"layered feathery plume stalks"),
        new("pondstars","Callitriche palustris","rosette",18,1,.99,.14,0,"small floating star colonies"),
        new("prismstar","Trientalis borealis","rosette",10,2,.80,.15,7,"star rosettes with tiny blooms"),
        new("quillsedge","Carex comans","grass",25,1,.94,.025,2,"fine arching fountain sedge"),
        new("ringreed","Equisetum fluviatile","whorl",12,6,.36,.018,0,"jointed shafts and radial collars"),
        new("sceptrefern","Osmunda regalis","fern",7,8,.68,.11,0,"tall separated pinnae crown"),
        new("siltstars","Littorella uniflora","rosette",22,1,.99,.10,0,"submerged ground-hugging star carpet"),
        new("silverweed","Elodea canadensis","whorl",8,7,.85,.035,0,"ribbon whorls on creeping bed"),
        new("snaptrap","Dionaea muscipula","trap",10,1,.88,.16,2,"radiating paired toothed traps"),
        new("sootband","Equisetum hyemale","whorl",20,5,.22,.008,1,"dense jointed rush shafts"),
        new("starbulb","Allium cristophii","rosette",11,1,.72,.07,6,"strap rosette and globe of stars"),
        new("starveil","Anemone nemorosa","pad",9,3,.76,.16,6,"palmate spring leaves and white stars"),
        new("streamribbon","Vallisneria spiralis","grass",15,1,.52,.065,0,"long underwater tape blades"),
        new("sunstone_rosette","Echeveria agavoides","rosette",18,1,.85,.19,0,"tight fleshy triangular rosette"),
        new("swanfrond","Matteuccia struthiopteris","fern",9,15,.88,.10,0,"soft layered shuttlecock fronds"),
        new("tankbloom","Aechmea fasciata","rosette",14,2,.96,.15,7,"broad tank rosette and bract spike"),
        new("tidebell","fictional identity: low cool blue-green tubular clump","forb",7,4,.55,.17,2,"paired waxy leaves and bells"),
        new("trifold","Trifolium repens","runner",7,4,.92,.17,0,"loose trifoliate meadow stolons"),
        new("veilblade","Phalaris arundinacea","grass",28,1,.86,.065,2,"tall bowing ribbon curtain"),
        new("veilfern","Athyrium filix-femina","fern",8,12,.96,.09,0,"fine arching divided veil"),
        new("widowspire","fictional identity: sparse pink elongated spikes","forb",9,6,.63,.09,8,"tall narrow leaves and flower spires")
    };
    public static IReadOnlyList<GraphSpeciesInfo> Species { get; } = Array.AsReadOnly(Table.Select(s => new GraphSpeciesInfo(s.Id, s.Form, s.Habit, s.Habit)).ToArray());
    public static PlanGenerator? Create(FloraSpeciesDef species)
    {
        var s = Array.Find(Table, x => x.Id == species.Id);
        return s == null ? null : s.Id == "moonfern" ? new MoonfernGenerator() : new Generator(s, species);
    }

    private sealed partial class Generator : PlanGenerator
    {
        private readonly Spec s;
        private readonly double h, r;
        public Generator(Spec spec, FloraSpeciesDef def) { s = spec; h = Math.Max(.025, def.Height); r = Math.Max(.035, def.RadiusAtMax); }
        public override string SpeciesId => s.Id;

        // Perceptual roughness per species leaf: felted/hairy high, waxy/succulent/pad low, ordinary herbaceous mid.
        private static float LeafRough(string id) => id switch
        {
            "burlmantle" => .84f, "mooncoin" => .82f, "hallowsting" => .76f, "duskbristle" => .76f, "ashwire" => .74f, "quillsedge" => .72f,
            "blackneedle" => .70f, "frosttussock" => .70f, "cloudtuft" => .70f, "plumegrass" => .70f, "emberbracken" => .68f,
            "bannerreed" => .68f, "veilblade" => .68f, "veilfern" => .68f, "cinderberry" => .66f, "fenfoam" => .66f, "marrowbloom" => .66f,
            "starveil" => .66f, "swanfrond" => .66f, "glassrush" => .66f, "sceptrefern" => .64f, "heddlelace" => .64f, "coalspike" => .64f,
            "lanternsedge" => .64f, "barbarrow" => .62f, "trifold" => .62f, "hushclover" => .62f, "prismstar" => .62f, "starbulb" => .62f,
            "widowspire" => .62f, "butterglint" => .60f, "paddlerib" => .60f, "mistviolet" => .60f, "ringreed" => .58f, "streamribbon" => .57f,
            "brooklace" => .56f, "sootband" => .56f, "mirepocket" => .55f, "fencomb" => .54f, "coinrunner" => .54f, "deepquill" => .52f,
            "silverweed" => .52f, "siltstars" => .52f, "snaptrap" => .52f, "burrflag" => .57f, "bogfringe" => .50f, "pondstars" => .50f,
            "blue_sundew" => .45f, "pitcher_plant" => .45f, "fenbead" => .44f, "mirrorstrap_fern" => .40f, "tankbloom" => .40f,
            "cragset" => .40f, "tidebell" => .38f, "mirrorleaf" => .36f, "marshkettle" => .36f, "sunstone_rosette" => .36f,
            "glassfinger" => .36f, "glasslily" => .34f, "brightknot" => .34f, _ => .62f
        };
        private static float PetalRough(string id) => id switch
        {
            "butterglint" => .42f, "marshkettle" => .46f, "mirrorleaf" => .48f, "glasslily" => .58f, "starveil" => .64f,
            "hushclover" => .66f, "mistviolet" => .68f, "bogfringe" => .70f, _ => .60f
        };
        // Width is sampled along the midrib, from the petiole to the tip. Keeping
        // these outlines in species groups makes their silhouettes legible even
        // when the same lamina compiler is used for several growth habits.
        private static double LeafWidth(string id, double t, bool blade)
        {
            double sin = Math.Max(0, Math.Sin(Math.PI*t));
            return id switch
            {
                "barbarrow" => .45 * Math.Pow(1-t,.85),
                "paddlerib" or "burlmantle" => Math.Pow(sin,.55) * (.65+.65*t),
                "coinrunner" or "mooncoin" or "bogfringe" or "hushclover" => Math.Pow(sin,.43),
                "cinderberry" or "trifold" => Math.Pow(sin,.65) * (.7+.5*t),
                "blackneedle" => Math.Pow(sin,.7) * (.72+.28*Math.Pow(Math.Cos(3*Math.PI*t),2)),
                "butterglint" or "starveil" => Math.Pow(sin,.65) * (.65+.35*Math.Pow(Math.Cos(3*Math.PI*t),2)),
                "hallowsting" => Math.Pow(sin,.8) * (1.25-.55*t),
                "widowspire" or "tidebell" => Math.Pow(sin,1.1),
                "brightknot" or "cragset" or "sunstone_rosette" => Math.Pow(sin,.55) * (.45+.85*t),
                "tankbloom" or "starbulb" or "mirrorstrap_fern" => Math.Pow(sin,.38) * (1-.35*t),
                "emberbracken" or "sceptrefern" or "swanfrond" or "veilfern" => Math.Pow(sin,.9) * (1.2-.4*t),
                _ when blade => Math.Pow(1-t,.7)*Math.Pow(Math.Sin(Math.PI*t*.5),.18),
                _ => Math.Pow(sin,.75)
            };
        }
        protected override void Design(PlanBuilder b)
        {
            var green = s.Id == "fencomb" ? PlantShapes.Srgb(.78,.67,.32) : s.Id == "deepquill" ? PlantShapes.Srgb(.29,.67,.56) : s.Id == "fenbead" ? PlantShapes.Srgb(.43,.72,.32) : PlantShapes.Srgb(.22,.43,.20);
            bool blade = s.Form == "grass" || (s.Form == "whorl" && s.Width < .05);
            // Cross curl is expressed in leaf lengths, so slender blades need a much
            // smaller value than broad leaves to avoid huge transverse folds.
            float crossCurl = blade ? .0005f : s.Form == "fern" ? .008f : .025f;
            float midribCurl = s.Id is "duskbristle" or "frosttussock" or "quillsedge" or "veilblade" ? .36f
                : blade ? .18f : s.Form == "rosette" ? .12f : .08f;
            ushort leaf = b.AddShape(new OrganShape { Name=s.Id+"_leaf", Type=OrganType.Leaf, SurfaceForm=OrganSurfaceForm.Lamina,
                WidthProfile=PlantShapes.Profile(t => LeafWidth(s.Id, t, blade), 17), MidribCurl=midribCurl, CrossCurl=crossCurl,
                BasalLobeLength=s.Id=="barbarrow" ? .42f : 0f,
                Serration=s.Id is "hallowsting" or "blackneedle" or "cinderberry" ? .16f : 0f,
                SerrationFreq=s.Id=="blackneedle" ? 5f : 9f,
                EdgeWave=s.Id=="bogfringe" ? .18f : s.Id is "burlmantle" or "tankbloom" ? .025f : .008f, EdgeWaveFreq=s.Id=="bogfringe" ? 7f : 3f,
                ColorAdaxial=green, ColorAbaxial=PlantShapes.Srgb(.32,.46,.25), Thickness=.0006f, Roughness=LeafRough(s.Id)});
            bool starBloom = s.Bloom >= 4;
            ushort petal = b.AddShape(new OrganShape { Name=s.Id+"_flower", Type=OrganType.Flower,
                SurfaceForm=starBloom ? OrganSurfaceForm.Star : OrganSurfaceForm.Cup,
                LobeCount=Math.Max(3,Math.Min(9,s.Bloom)), LobeDepth=.42f, RimCurl=.16f,
                WidthProfile=PlantShapes.Profile(t => starBloom ? .5*Math.Sin(Math.PI*t) : .12+.38*Math.Pow(t,.7)),
                ColorAdaxial=s.Id is "glasslily" or "starveil" or "hushclover" or "bogfringe" ? PlantShapes.Srgb(.95,.95,.91)
                    : s.Id is "marshkettle" or "butterglint" or "mirrorleaf" ? PlantShapes.Srgb(0.98,0.795,0.256)
                    : s.Id=="mistviolet" ? PlantShapes.Srgb(.57,.42,.77) : PlantShapes.Srgb(.84,.76,.48),
                ColorAbaxial=PlantShapes.Srgb(.53,.58,.30), Roughness=PetalRough(s.Id) });
            ushort pad = b.AddShape(new OrganShape { Name=s.Id+"_floating_pad", Type=OrganType.Leaf,
                SurfaceForm=OrganSurfaceForm.Disc, WidthProfile=PlantShapes.Profile(t => .5*Math.Sin(Math.PI*t)),
                ColorAdaxial=PlantShapes.Srgb(.19,.38,.20), ColorAbaxial=PlantShapes.Srgb(.31,.31,.17), Wax=.65f, Roughness=s.Id=="glasslily" ? .32f : s.Id=="mirrorleaf" ? .38f : .35f });
            ushort pitcher = b.AddShape(new OrganShape { Name=s.Id+"_pitcher", Type=OrganType.Flower,
                SurfaceForm=OrganSurfaceForm.Cup, WidthProfile=PlantShapes.Profile(t => .12+.38*Math.Pow(t,.7)),
                RimCurl=.25f, ShellThickness=.025f, ColorAdaxial=PlantShapes.Srgb(0.38,0.48,0.191),
                ColorAbaxial=PlantShapes.Srgb(0.45,0.189,0.17), Roughness=.38f });
            ushort trap = b.AddShape(new OrganShape { Name=s.Id+"_trap_lobe", Type=OrganType.Leaf,
                SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*t),.65)),
                Serration=.28f, SerrationFreq=12, CrossCurl=.035f, ColorAdaxial=PlantShapes.Srgb(0.52,0.198,0.189),
                ColorAbaxial=PlantShapes.Srgb(.25,.43,.18), Thickness=.001f, Roughness=s.Id=="snaptrap" ? .50f : .42f });
            ushort club = b.AddShape(new OrganShape { Name=s.Id+"_dew_club", Type=OrganType.Flower,
                SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Sin(Math.PI*t),.65)),
                ColorAdaxial=PlantShapes.Srgb(0.214,0.511,0.72), ColorAbaxial=PlantShapes.Srgb(0.152,0.315,0.46), Roughness=.28f });
            ushort cotton = b.AddShape(new OrganShape { Name=s.Id+"_cotton", Type=OrganType.Flower,
                SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Sin(Math.PI*t),.55)),
                ColorAdaxial=PlantShapes.Srgb(.92,.91,.77), ColorAbaxial=PlantShapes.Srgb(.78,.79,.67), Roughness=.82f });
            ushort spike = b.AddShape(new OrganShape { Name=s.Id+"_spike", Type=OrganType.Flower,
                SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t => .43*Math.Pow(Math.Sin(Math.PI*t),.15)),
                ColorAdaxial=PlantShapes.Srgb(0.16,0.091,0.062), ColorAbaxial=PlantShapes.Srgb(0.25,0.156,0.09), Roughness=.78f });
            ushort pale = b.AddShape(new OrganShape { Name=s.Id+"_pale", Type=OrganType.Flower, SurfaceForm=OrganSurfaceForm.Cup,
                WidthProfile=PlantShapes.Profile(t => .18+.32*Math.Sin(Math.PI*t*.5)), ShellThickness=.015f,
                ColorAdaxial=PlantShapes.Srgb(.95,.94,.89), ColorAbaxial=PlantShapes.Srgb(.82,.82,.77), Roughness=.56f });
            ushort scarlet = b.AddShape(new OrganShape { Name=s.Id+"_bract", Type=s.Id=="tankbloom" ? OrganType.Flower : OrganType.Leaf, SurfaceForm=OrganSurfaceForm.Lamina,
                WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*t),.8)), CrossCurl=.025f,
                ColorAdaxial=PlantShapes.Srgb(s.Id=="tankbloom" ? 1.0 : .78,s.Id=="tankbloom" ? .012 : .08,s.Id=="tankbloom" ? .025 : .14), ColorAbaxial=PlantShapes.Srgb(s.Id=="tankbloom" ? .82 : .53,.01,.025), Roughness=s.Id=="tankbloom" ? .55f : .66f });
            ushort berry = b.AddShape(new OrganShape { Name=s.Id+"_fruit", Type=OrganType.Fruit, SurfaceForm=OrganSurfaceForm.Revolved,
                WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Sin(Math.PI*t),.7)),
                ColorAdaxial=PlantShapes.Srgb(0.88,0.257,0.203), ColorAbaxial=PlantShapes.Srgb(0.65,0.169,0.145), Roughness=s.Id=="cinderberry" ? .22f : .26f });
            ushort lantern = b.AddShape(new OrganShape { Name=s.Id+"_lantern", Type=OrganType.Flower, SurfaceForm=OrganSurfaceForm.Revolved,
                WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Sin(Math.PI*t),.8)),
                ColorAdaxial=PlantShapes.Srgb(.75,.98,.76), ColorAbaxial=PlantShapes.Srgb(.48,.76,.51), Roughness=.72f });
            ushort burr = b.AddShape(new OrganShape { Name=s.Id+"_burr", Type=OrganType.Fruit, SurfaceForm=OrganSurfaceForm.Revolved,
                WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Sin(Math.PI*t),.5)),
                ColorAdaxial=PlantShapes.Srgb(.59,.53,.26), ColorAbaxial=PlantShapes.Srgb(.39,.37,.18), Roughness=.72f });
            int crown=b.AddAxis(-1,0,Vec3.Up,Math.Max(.004,h*.035),Vec3.Zero,Math.Max(.001,r*.035),Math.Max(.0007,r*.025),0,Tissue.Green,0,.12);
            double rotation=b.Range(0,Math.Tau);
            var palette=new HabitPalette(leaf,petal,pad,pitcher,trap,club,cotton,spike,pale,scarlet,berry,lantern,burr);
            if(TryLowHabit(b,crown,rotation,palette)||TryUprightHabit(b,crown,rotation,palette)||TryAquaticFernHabit(b,crown,rotation,palette))
                return;
            if(s.Id=="barbarrow")
            {
                // A small open crown of individual long-petioled sagittate leaves.
                // The blade and its two backward lobes are one connected lamina.
                const int leaves=9;
                for(int i=0;i<leaves;i++)
                {
                    double a=rotation+i*Math.Tau/leaves+b.Range(-.14,.14);
                    var radial=PlantMath.Horizontal(a);
                    var lateral=PlantMath.Horizontal(a+Math.PI/2);
                    int stalk=b.AddAxis(crown,1,Vec3.Up+radial*b.Range(.22,.48),
                        h*b.Range(.35,.62),radial*b.Range(.08,.18)+lateral*b.Range(-.06,.06),
                        r*.007,r*.0025,1,Tissue.Green,.02,.26);
                    b.AddFlat(stalk,1,Vec3.Up*b.Range(.7,1.0)+radial*b.Range(.5,.85),
                        h*b.Range(.25,.34),r*b.Range(.22,.29),.0005,OrganType.Leaf,leaf,2,.07,.22);
                }
                // Each blossom has three discrete white petals around a small gold centre.
                ushort whitePetal=b.AddShape(new OrganShape { Name=s.Id+"_white_petal", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Lamina,
                    WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*t),.65),17),
                    MidribCurl=.08f, CrossCurl=.025f,
                    ColorAdaxial=PlantShapes.Srgb(.97,.97,.93), ColorAbaxial=PlantShapes.Srgb(.86,.88,.80), Thickness=.0004f, Roughness=.62f });
                ushort flowerCenter=b.AddShape(new OrganShape { Name=s.Id+"_flower_center", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t => .5*Math.Sin(Math.PI*t)),
                    ColorAdaxial=PlantShapes.Srgb(0.88,0.722,0.266), ColorAbaxial=PlantShapes.Srgb(0.72,0.591,0.204), Roughness=.70f });
                int scape=b.AddAxis(crown,1,Vec3.Up,h*.86,Vec3.Up*.02,r*.008,r*.002,1,Tissue.Green,.25,.35);
                for(int tier=0;tier<3;tier++) for(int k=0;k<3;k++)
                {
                    double a=rotation+k*Math.Tau/3+tier*.45;
                    var radial=PlantMath.Horizontal(a);
                    int ped=b.AddAxis(scape,.68+tier*.14,radial+Vec3.Up*.4,r*.13,Vec3.Up*.08,
                        r*.002,r*.0007,2,Tissue.Green,.55,.2);
                    b.AddFlat(ped,1,Vec3.Up,h*.016,r*.013,.0004,OrganType.Flower,flowerCenter,3,.65,.16);
                    for(int q=0;q<3;q++)
                    {
                        var ray=PlantMath.Horizontal(a+q*Math.Tau/3)+Vec3.Up*.18;
                        b.AddFlat(ped,1,ray,h*.055,r*.035,.0004,OrganType.Flower,whitePetal,3,.65,.16);
                    }
                }
                return;
            }
            // Preserve the fictional roster identities; counterpart names are only loose references.
            if(s.Id is "ashwire" or "frosttussock")
            {
                ushort fine=b.AddShape(new OrganShape { Name=s.Id+"_pale_blade", Type=OrganType.Leaf,
                    SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(1-t,.8)),
                    MidribCurl=.16f, CrossCurl=.0003f,
                    ColorAdaxial=PlantShapes.Srgb(.66,.73,.67), ColorAbaxial=PlantShapes.Srgb(.49,.61,.58), Thickness=.0003f, Roughness=s.Id=="ashwire" ? .78f : .70f });
                for(int i=0;i<36;i++)
                {
                    double a=rotation+i*Math.Tau/36+b.Range(-.14,.14);
                    var d=PlantMath.Horizontal(a); var side=PlantMath.Horizontal(a+Math.PI/2);
                    int stem=b.AddAxis(crown,1,Vec3.Up+d*b.Range(.06,.36),h*b.Range(.48,1.0),
                        d*b.Range(.04,.18)+side*b.Range(-.08,.08),r*.003,r*.0007,1,Tissue.Green,0,.4);
                    b.AddFlat(stem,.04,Vec3.Up+d*b.Range(.08,.4),h*b.Range(.45,.8),r*b.Range(.008,.018),
                        .0003,OrganType.Leaf,fine,2,.1,.3);
                }
                return;
            }
            if(s.Id=="fenbead")
            {
                ushort small=b.AddShape(new OrganShape { Name=s.Id+"_small_leaf", Type=OrganType.Leaf,
                    SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*t),.8)),
                    ColorAdaxial=PlantShapes.Srgb(.40,.66,.42), ColorAbaxial=PlantShapes.Srgb(.31,.53,.37), Thickness=.0003f, Roughness=LeafRough(s.Id) });
                for(int i=0;i<7;i++)
                {
                    double a=rotation+i*Math.Tau/7+b.Range(-.2,.2); var d=PlantMath.Horizontal(a);
                    int stem=b.AddAxis(crown,1,Vec3.Up+d*b.Range(.22,.55),h*b.Range(.55,.95),d*.15,r*.004,r*.001,1,Tissue.Green,0,.4);
                    for(int j=0;j<4;j++)
                    {
                        double t=.25+j*.18; var ray=PlantMath.Horizontal(a+j*2.4);
                        int branch=b.AddAxis(stem,t,ray+Vec3.Up*.4,r*b.Range(.15,.29),Vec3.Up*.08,r*.002,r*.0005,2,Tissue.Green,.2,.25);
                        for(int k=0;k<3;k++)
                        {
                            double u=.3+k*.3; var ld=PlantMath.Horizontal(a+j*2.4+(k%2==0 ? 1 : -1)*1.2)+Vec3.Up*.2;
                            b.AddFlat(branch,u,ld,r*.065,r*.025,.0003,OrganType.Leaf,small,3,.3,.2);
                        }
                    }
                }
                return;
            }
            if(s.Id=="tidebell")
            {
                ushort tube=b.AddShape(new OrganShape { Name=s.Id+"_cool_tube", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Cup, WidthProfile=PlantShapes.Profile(t => .18+.22*t),
                    ShellThickness=.018f, RimCurl=.1f, ColorAdaxial=PlantShapes.Srgb(.49,.73,.76),
                    ColorAbaxial=PlantShapes.Srgb(.32,.58,.62), Roughness=.48f });
                for(int i=0;i<15;i++)
                {
                    double a=rotation+i*Math.Tau/15+b.Range(-.2,.2); var d=PlantMath.Horizontal(a);
                    int stem=b.AddAxis(crown,1,Vec3.Up+d*b.Range(.25,.65),h*b.Range(.025,.07),d*.1,r*.005,r*.0015,1,Tissue.Green,0,.35);
                    b.AddFlat(stem,1,Vec3.Up+d*.25,h*b.Range(.18,.3),r*b.Range(.055,.09),.0005,OrganType.Flower,tube,2,.3,.3);
                }
                return;
            }
            if(s.Id=="widowspire")
            {
                ushort pink=b.AddShape(new OrganShape { Name=s.Id+"_pink_flower", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Star, LobeCount=5, LobeDepth=.4f,
                    WidthProfile=PlantShapes.Profile(t => .5*Math.Sin(Math.PI*t)),
                    ColorAdaxial=PlantShapes.Srgb(.82,.48,.64), ColorAbaxial=PlantShapes.Srgb(.62,.35,.49), Roughness=.66f });
                for(int i=0;i<6;i++)
                {
                    double a=rotation+i*Math.Tau/6+b.Range(-.15,.15); var d=PlantMath.Horizontal(a);
                    int stem=b.AddAxis(crown,1,Vec3.Up+d*b.Range(.08,.25),h*b.Range(.75,1.05),d*.06,r*.006,r*.0015,1,Tissue.Green,0,.45);
                    for(int j=0;j<4;j++)
                    {
                        double t=.14+j*.14;
                        b.AddFlat(stem,t,PlantMath.Horizontal(a+j*2.4)+Vec3.Up*.4,r*.18,r*.035,.0004,OrganType.Leaf,leaf,2,.2,.2);
                    }
                    for(int j=0;j<10;j++)
                    {
                        double t=.58+j*.04; var ray=PlantMath.Horizontal(a+j*2.4)+Vec3.Up*.2;
                        b.AddFlat(stem,t,ray,h*.025,r*.026,.0004,OrganType.Flower,pink,2,.6,.2);
                    }
                }
                return;
            }
            if(s.Id=="pitcher_plant")
            {
                ushort hood=b.AddShape(new OrganShape { Name=s.Id+"_hood", Type=OrganType.Leaf,
                    SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*t),.6)),
                    MidribCurl=.18f, CrossCurl=.025f, ColorAdaxial=PlantShapes.Srgb(0.67,0.268,0.249),
                    ColorAbaxial=PlantShapes.Srgb(.85,.72,.57), Thickness=.0006f, Roughness=.45f });
                ushort redFlower=b.AddShape(new OrganShape { Name=s.Id+"_red_flower", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Cup, WidthProfile=PlantShapes.Profile(t => .16+.34*t), RimCurl=.18f,
                    ColorAdaxial=PlantShapes.Srgb(0.72,0.204,0.256), ColorAbaxial=PlantShapes.Srgb(0.48,0.136,0.179), Roughness=.60f });
                for(int i=0;i<7;i++)
                {
                    double a=rotation+i*Math.Tau/7+b.Range(-.16,.16); var d=PlantMath.Horizontal(a);
                    int stalk=b.AddAxis(crown,1,Vec3.Up+d*b.Range(.28,.5),h*b.Range(.10,.2),d*.12,r*.009,r*.004,1,Tissue.Green,0,.3);
                    // Shape meshes are cached by species/shape slot, so their aspect
                    // stays stable across individuals; stalk lean and placement vary.
                    double length=h*(.43+.19*((i*3)%7)/6.0),width=r*(.11+.05*((i*5)%7)/6.0);
                    ushort vessel=b.AddShape(new OrganShape {Name=s.Id+"_curved_vessel_"+i,Type=OrganType.Flower,
                        SurfaceForm=OrganSurfaceForm.Cup,WidthProfile=PlantShapes.Profile(t=>.10+.40*Math.Exp(-Math.Pow((t-.22)/.18,2))+.18*Math.Pow(t,3)),
                        MidribCurl=.48f,LengthToWidth=(float)(length/(2*width)),RimCurl=.32f,ShellThickness=.025f,
                        ColorAdaxial=PlantShapes.Srgb(.84,.78,.62),ColorAbaxial=PlantShapes.Srgb(0.57,0.209,0.218), Roughness=.40f});
                    int body=b.AddFlat(stalk,1,Vec3.Up+d*b.Range(.25,.5),length,width,.001,OrganType.Flower,vessel,2,.2,.4);
                    var organ=b[body];var forward=PlantMath.SafeNormal(organ.P1-organ.P0,Vec3.Up);
                    var up=PlantMath.SafeNormal(organ.Up-forward*organ.Up.Dot(forward),PlantMath.Perpendicular(forward));
                    double rimT=1+.32*.08,angle=.48*rimT;
                    var mouth=organ.P0+forward*(length*Math.Sin(angle)/.48)-up*(length*(1-Math.Cos(angle))/.48);
                    var rimSide=up*Math.Cos(angle)+forward*Math.Sin(angle);
                    var mouthAxis=forward*Math.Cos(angle)-up*Math.Sin(angle);
                    int hoodStem=b.AddAxis(stalk,1,mouthAxis,h*.025,Vec3.Zero,r*.002,r*.001,2,Tissue.Green,.48,.15);
                    // Belled base: the mouth is ~.56 of the widest (basal) radius.
                    b[hoodStem].P0=mouth+rimSide*(width*.56);
                    b[hoodStem].P1=b[hoodStem].P0+mouthAxis*(h*.025);
                    b[hoodStem].T0=b[hoodStem].T1=mouthAxis*(h*.025);
                    int lid=b.AddFlat(hoodStem,1,-rimSide+mouthAxis*.15,width*1.4,width*.55,.0006,OrganType.Leaf,hood,3,.55,.16);
                    b[lid].Up=mouthAxis;
                }
                // Thick, gently curved flower stem: two welded segments with bark
                // nodes and alternating scale bracts, ending in a nodding layered bloom.
                ushort bract=b.AddShape(new OrganShape { Name=s.Id+"_bract", Type=OrganType.Leaf,
                    SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*Math.Pow(t,.8)),.7)),
                    MidribCurl=.22f, CrossCurl=.03f, ColorAdaxial=PlantShapes.Srgb(0.52,0.303,0.204),
                    ColorAbaxial=PlantShapes.Srgb(.62,.38,.26), Thickness=.0005f, Roughness=s.Id=="tankbloom" ? .55f : .66f });
                ushort sepal=b.AddShape(new OrganShape { Name=s.Id+"_sepal", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*Math.Pow(t,.75)),.6)),
                    MidribCurl=.30f, CrossCurl=.05f, ColorAdaxial=PlantShapes.Srgb(0.45,0.125,0.159),
                    ColorAbaxial=PlantShapes.Srgb(0.62,0.311,0.234), Thickness=.0006f, Roughness=.70f });
                ushort bloomPetal=b.AddShape(new OrganShape { Name=s.Id+"_petal", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*Math.Pow(t,.7)),.55)),
                    MidribCurl=.40f, CrossCurl=.07f, EdgeWave=.04f, ColorAdaxial=PlantShapes.Srgb(0.82,0.234,0.286),
                    ColorAbaxial=PlantShapes.Srgb(0.6,0.17,0.222), Thickness=.0005f, Roughness=.60f });
                ushort innerPetal=b.AddShape(new OrganShape { Name=s.Id+"_inner_petal", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*Math.Pow(t,.6)),.7)),
                    MidribCurl=.55f, CrossCurl=.09f, ColorAdaxial=PlantShapes.Srgb(.93,.55,.46),
                    ColorAbaxial=PlantShapes.Srgb(0.86,0.347,0.337), Thickness=.0004f, Roughness=.58f });
                ushort anther=b.AddShape(new OrganShape { Name=s.Id+"_anther", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Sin(Math.PI*t),.6)),
                    ColorAdaxial=PlantShapes.Srgb(0.95,0.799,0.3), ColorAbaxial=PlantShapes.Srgb(0.8,0.612,0.22), Roughness=.78f });
                ushort ovary=b.AddShape(new OrganShape { Name=s.Id+"_ovary_disc", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Sin(Math.PI*Math.Pow(t,.7)),.5)),
                    ColorAdaxial=PlantShapes.Srgb(.78,.80,.38), ColorAbaxial=PlantShapes.Srgb(.55,.62,.25), Roughness=.62f });
                var lean=PlantMath.Horizontal(rotation);
                var lateralLean=PlantMath.Horizontal(rotation+Math.PI/2);
                int scape=b.AddAxis(crown,1,Vec3.Up+lean*.05,h*.52,lean*.22+lateralLean*.10,r*.016,r*.012,1,Tissue.Green,.4,.3);
                int scapeTop=b.AddAxis(scape,1,b.TangentAt(scape,1),h*.40,lean*.95-Vec3.Up*.55,r*.012,r*.0075,1,Tissue.Green,.45,.3,OrganFlags.Continuous);
                for(int j=0;j<4;j++)
                {
                    double nodeT=.18+j*.22; double az=rotation+j*2.39996+.6;
                    b.AddAxis(scape,nodeT,b.TangentAt(scape,nodeT),h*.012,Vec3.Zero,r*.019,r*.019,2,Tissue.Bark,.42,.2,OrganFlags.Basal);
                    b.AddFlat(scape,nodeT,PlantMath.Horizontal(az)*.8+Vec3.Up*.7,h*(.085-j*.012),r*(.045-j*.006),.0005,OrganType.Leaf,bract,2,.45+j*.02,.2);
                }
                for(int j=0;j<2;j++)
                {
                    double nodeT=.30+j*.40; double az=rotation+2.2+j*2.39996;
                    b.AddAxis(scapeTop,nodeT,b.TangentAt(scapeTop,nodeT),h*.010,Vec3.Zero,r*.0135,r*.0135,2,Tissue.Bark,.5,.2,OrganFlags.Basal);
                    b.AddFlat(scapeTop,nodeT,PlantMath.Horizontal(az)*.8+Vec3.Up*.5,h*.05,r*.03,.0005,OrganType.Leaf,bract,2,.5,.2);
                }
                // Nodding head: short receptacle, sepals, two petal whorls, stamens, ovary disc.
                int head=b.AddAxis(scapeTop,1,Vec3.Down+lean*.25,h*.03,lean*.10,r*.0075,r*.012,2,Tissue.Green,.6,.2);
                // Bloom sized to read as the plant's focal point at whole-plant distance.
                b.AddFlat(head,.45,Vec3.Down,h*.06,r*.11,.001,OrganType.Flower,redFlower,3,.68,.2);
                for(int k=0;k<5;k++)
                {
                    double az=rotation+k*Math.Tau/5;
                    var rad=PlantMath.Horizontal(az);
                    b.AddFlat(head,.25,rad*1.0+Vec3.Up*.25,h*.12,r*.10,.0006,OrganType.Flower,sepal,3,.62,.2);
                    b.AddFlat(head,.55,rad*.9+Vec3.Down*.55,h*.165,r*.13,.0006,OrganType.Flower,bloomPetal,3,.7,.2);
                    var rad2=PlantMath.Horizontal(az+Math.PI/5);
                    b.AddFlat(head,.8,rad2*.55+Vec3.Down*.95,h*.13,r*.10,.0005,OrganType.Flower,innerPetal,3,.74,.2);
                }
                for(int k=0;k<8;k++)
                {
                    double az=rotation+k*Math.Tau/8+.2;
                    var rad=PlantMath.Horizontal(az);
                    int fil=b.AddAxis(head,.9,Vec3.Down+rad*.22,h*.09*b.Range(.85,1.1),rad*.15,r*.0018,r*.0010,3,Tissue.Green,.78,.2);
                    b.AddFlat(fil,1,b.TangentAt(fil,1),h*.018,r*.014,.0004,OrganType.Flower,anther,4,.82,.2);
                }
                b.AddFlat(head,1,Vec3.Down,h*.055,r*.055,.001,OrganType.Flower,ovary,3,.78,.2);
                return;
            }
            // These species have distinctive legacy silhouettes that do not fit the
            // generic floating-pad, rosette, or central-whorl constructors below.
            if(s.Id is "mistviolet" or "marshkettle")
            {
                bool violet=s.Id=="mistviolet";
                ushort roundLeaf=b.AddShape(new OrganShape { Name=s.Id+"_basal_leaf", Type=OrganType.Leaf,
                    SurfaceForm=OrganSurfaceForm.Lamina,
                    WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*t),.55)),
                    MidribCurl=.05f, CrossCurl=.015f, Wax=violet ? .15f : .55f,
                    ColorAdaxial=green, ColorAbaxial=PlantShapes.Srgb(.32,.46,.25), Thickness=.0006f, Roughness=LeafRough(s.Id) });
                int leaves=violet ? 16 : 12;
                for(int i=0;i<leaves;i++)
                {
                    double a=rotation+i*Math.Tau/leaves+b.Range(-.12,.12);
                    var d=PlantMath.Horizontal(a);
                    int stalk=b.AddAxis(crown,1,d+Vec3.Up*b.Range(.6,1.05),r*b.Range(.12,.27),Vec3.Up*.08,r*.009,r*.003,1,Tissue.Green,0,.3);
                    b.AddFlat(stalk,1,d+Vec3.Up*.18,h*b.Range(violet ? .18 : .22,violet ? .27 : .32),
                        h*b.Range(violet ? .17 : .21,violet ? .27 : .31),.0006,OrganType.Leaf,roundLeaf,2,.2,.25);
                }
                int flowers=violet ? 9 : 8;
                for(int i=0;i<flowers;i++)
                {
                    double a=rotation+i*Math.Tau/flowers+b.Range(-.2,.2);
                    var d=PlantMath.Horizontal(a);
                    int baseAxis=b.AddAxis(crown,1,d,r*b.Range(.08,.32),Vec3.Zero,r*.004,r*.003,1,Tissue.Green,0,.2);
                    int scape=b.AddAxis(baseAxis,1,Vec3.Up+d*(violet ? .42 : .22),h*b.Range(.40,.56),
                        d*.12-Vec3.Up*(violet ? .15 : .03),r*.006,r*.0015,2,Tissue.Green,.3,.25);
                    b.AddFlat(scape,1,Vec3.Up+d*(violet ? .45 : .15),h*.10,h*(violet ? .11 : .15),.001,OrganType.Flower,petal,3,.7,.2);
                }
                return;
            }
            if(s.Id=="mirrorstrap_fern")
            {
                ushort strap=b.AddShape(new OrganShape { Name="mirrorstrap_connected_strap", Type=OrganType.Leaf,
                    SurfaceForm=OrganSurfaceForm.Lamina,
                    WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*t),.38)),
                    MidribCurl=.35f, CrossCurl=.012f, Wax=.45f,
                    ColorAdaxial=green, ColorAbaxial=PlantShapes.Srgb(.32,.46,.25), Thickness=.0006f, Roughness=.40f });
                for(int i=0;i<9;i++)
                {
                    double a=rotation+i*Math.Tau/9+b.Range(-.2,.2);
                    var d=PlantMath.Horizontal(a);
                    // The lamina starts at the crown and extends continuously along the frond.
                    b.AddFlat(crown,1,Vec3.Up+d*b.Range(.3,.85),h*b.Range(.9,1.4),h*b.Range(.22,.30),.0006,OrganType.Leaf,strap,1,.12,.4);
                }
                return;
            }
            if(s.Id=="fenbead")
            {
                // Distinct fleshy leaves follow radiating, individually visible shoots.
                ushort bead=b.AddShape(new OrganShape { Name="fenbead_fleshy_leaf", Type=OrganType.Leaf,
                    SurfaceForm=OrganSurfaceForm.Revolved,
                    WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Sin(Math.PI*t),.65)),
                    ColorAdaxial=green, ColorAbaxial=PlantShapes.Srgb(.35,.62,.25), Roughness=.44f });
                for(int i=0;i<12;i++)
                {
                    double a=rotation+Math.Tau*(i+b.Range(-.3,.3))/12;
                    var d=PlantMath.Horizontal(a); var side=PlantMath.Horizontal(a+Math.PI/2);
                    double upright=i%4==0 ? 1.1 : b.Range(.28,.65);
                    int stem=b.AddAxis(crown,1,d+Vec3.Up*upright,r*b.Range(.65,1.05),Vec3.Up*.18,r*.014,r*.003,1,Tissue.Green,0,.5);
                    for(int j=0;j<6;j++) for(int sign=-1;sign<=1;sign+=2)
                    {
                        double t=Math.Clamp(.2+j*.14+b.Range(-.018,.018),.17,.94);
                        b.AddFlat(stem,t,side*sign*.7+d*.45+Vec3.Up*.4,r*.13,r*.065,.001,OrganType.Leaf,bead,2,.12+t*.15,.2);
                    }
                }
                return;
            }
            if(s.Id=="veilfern")
            {
                // The legacy frond rises from the crown before its outer half
                // arches out and falls nearly to the base. Two connected axes
                // make that rise and droop explicit in the graph geometry.
                for(int i=0;i<12;i++)
                {
                    double a=rotation+i*Math.Tau/12+b.Range(-.1,.1);
                    var d=PlantMath.Horizontal(a); var side=PlantMath.Horizontal(a+Math.PI/2);
                    double scale=b.Range(.88,1.12);
                    int inner=b.AddAxis(crown,1,Vec3.Up+d*.18,h*1.5*scale,
                        d*.85-Vec3.Up*.15,r*.012,r*.005,1,Tissue.Green,0,.4);
                    int outer=b.AddAxis(inner,1,d+Vec3.Up*.12,r*1.9*scale,
                        Vec3.Down*1.65,r*.005,r*.001,2,Tissue.Green,.1,.4);
                    for(int j=0;j<16;j++)
                    {
                        double t=(j+.5)/16.0;
                        int axis=j<8 ? inner : outer;
                        double u=j<8 ? .18+j*.10 : .06+(j-8)*.125;
                        double envelope=Math.Pow(Math.Sin(Math.PI*(.12+.82*t)),.65);
                        for(int sign=-1;sign<=1;sign+=2)
                            b.AddFlat(axis,u,side*sign+d*.25+Vec3.Down*.12,r*.40*envelope,r*.075*envelope,.0004,OrganType.Leaflet,leaf,3,.1+t*.18,.2);
                    }
                }
                return;
            }
            if(s.Id is "deepquill" or "fencomb")
            {
                bool quill=s.Id=="deepquill";
                // A dense set of overlapping whorls gives each shoot its own filled
                // column or tapered plume, rather than isolated hairlike spokes.
                ushort filament=b.AddShape(new OrganShape { Name=s.Id+"_filament", Type=OrganType.Leaf,
                    SurfaceForm=OrganSurfaceForm.Lamina,
                    WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*t),.6)),
                    ColorAdaxial=green, ColorAbaxial=green, Thickness=.0005f, Roughness=LeafRough(s.Id) });
                int shoots=quill ? 8 : 7;
                for(int i=0;i<shoots;i++)
                {
                    double a=rotation+i*2.39996;
                    var d=PlantMath.Horizontal(a);
                    int root=b.AddAxis(crown,1,d,r*b.Range(.05,quill ? .45 : .18),Vec3.Zero,r*.008,r*.006,1,Tissue.Green,0,.15);
                    int stem=b.AddAxis(root,1,Vec3.Up+d*b.Range(.02,quill ? .08 : .15),h*b.Range(.8,1.05),
                        d*b.Range(.02,.10),r*.018,r*.006,2,Tissue.Green,0,.45);
                    for(int j=0;j<15;j++) for(int k=0;k<8;k++)
                    {
                        double t=Math.Clamp(.08+j*.059+b.Range(-.008,.008),.06,.96), az=a+k*Math.Tau/8+j*.52+b.Range(-.10,.10);
                        var radial=PlantMath.Horizontal(az);
                        // Hold the low whorls full, then narrow the upper plume to
                        // about 30% reach while retaining every filament.
                        double taper=quill ? .85 : 1.0-.70*Math.Clamp((t-.20)/.71,0,1);
                        double len=r*(quill ? .25 : .38)*taper;
                        int ray=b.AddAxis(stem,t,radial+Vec3.Up*.25,len,Vec3.Zero,r*.003,r*.0008,3,Tissue.Green,.1,.2);
                        for(int n=0;n<4;n++) for(int sign=-1;sign<=1;sign+=2)
                        {
                            double u=.22+n*.22;
                            var branch=PlantMath.Horizontal(az+sign*1.0)+radial*.35+Vec3.Up*.22;
                            b.AddFlat(ray,u,branch,len*(quill ? .62 : .75)*(1-u*.35),r*(quill ? .018 : .022),.0004,OrganType.Leaflet,filament,4,.2,.15);
                        }
                    }
                }
                return;
            }
            // Latchjaw has a spiralling column of paired traps.
            if(s.Id=="snaptrap")
            {
                int column=b.AddAxis(crown,1,Vec3.Up,h*.80,Vec3.Zero,r*.055,r*.025,1,Tissue.Green,0,.7);
                for(int i=0;i<s.Shoots;i++)
                {
                    double az=rotation+i*2.39996, t=.12+.75*i/Math.Max(1,s.Shoots-1);
                    var d=PlantMath.Horizontal(az); var side=PlantMath.Horizontal(az+Math.PI/2);
                    int arm=b.AddAxis(column,t,d+Vec3.Up*.25,r*.34,Vec3.Up*.1,r*.018,r*.006,2,Tissue.Green,.2,.3);
                    for(int jaw=-1;jaw<=1;jaw+=2)
                        b.AddFlat(arm,1,d+side*jaw*.65+Vec3.Up*.32,r*.31,r*.12,.001,OrganType.Leaf,trap,3,.4,.25);
                }
                b.AddFlat(column,1,Vec3.Up,h*.25,r*.22,.001,OrganType.Flower,pitcher,2,.75,.2);
                return;
            }
            if(s.Id=="marrowbloom")
            {
                for(int i=0;i<s.Shoots;i++)
                {
                    double a=rotation+i*2.39996; var d=PlantMath.Horizontal(a);
                    int stem=b.AddAxis(crown,1,Vec3.Up+d*.12,h*b.Range(.68,1.0),d*.12,r*.025,r*.012,1,Tissue.Green,0,.5);
                    for(int j=0;j<4;j++) b.AddFlat(stem,.15+j*.16,d+Vec3.Up*.7,h*.10,r*.035,.001,OrganType.Leaf,pale,2,.2,.2);
                    int neck=b.AddAxis(stem,1,d+Vec3.Up*.1,h*.12,Vec3.Down*.7,r*.012,r*.008,2,Tissue.Green,.5,.2);
                    b.AddFlat(neck,1,Vec3.Down+d*.2,h*.16,r*.12,.001,OrganType.Flower,pale,3,.65,.2);
                }
                return;
            }
            if(s.Id=="tankbloom")
            {
                int scape=b.AddAxis(crown,1,Vec3.Up,h*.72,Vec3.Zero,r*.018,r*.010,1,Tissue.Green,.2,.5);
                int spikeAxis=b.AddAxis(scape,1,Vec3.Up,h*.28,Vec3.Zero,r*.01,r*.003,2,Tissue.Green,.5,.3);
                for(int w=0;w<7;w++) for(int k=0;k<4;k++)
                {
                    double t=.04+w*.14, a=rotation+k*Math.Tau/4+w*1.1;
                    var d=PlantMath.Horizontal(a)+Vec3.Up*(.7+t*.5);
                    b.AddFlat(spikeAxis,t,d,h*(.085-.04*t),r*(.075-.035*t),.001,OrganType.Leaf,scarlet,3,.55,.2);
                    if(k==0) b.AddFlat(spikeAxis,t,d,h*.025,r*.025,.001,OrganType.Flower,pale,3,.7,.15);
                }
            }
            if(s.Id=="ringreed")
            {
                // The legacy hybrid has a cabbage-like basal crown and a hood around its central shoot.
                for(int k=0;k<7;k++)
                {
                    var d=PlantMath.Horizontal(rotation+k*Math.Tau/7);
                    int petiole=b.AddAxis(crown,1,d+Vec3.Up*.45,r*.32,Vec3.Up*.1,r*.012,r*.004,1,Tissue.Green,0,.3);
                    b.AddFlat(petiole,1,d+Vec3.Up*.5,r*.42,r*.20,.0006,OrganType.Leaf,leaf,2,.2,.25);
                }
                int hoodStem=b.AddAxis(crown,1,Vec3.Up,h*.38,Vec3.Zero,r*.02,r*.008,1,Tissue.Green,.2,.3);
                b.AddFlat(hoodStem,1,Vec3.Up,h*.24,r*.16,.001,OrganType.Flower,pitcher,2,.4,.2);
            }
            if(s.Id=="bannerreed")
            {
                // Reed culms remain upright; the nodding silhouette belongs to the
                // terminal panicle, whose fine one-sided branches carry the glumes.
                ushort glume=b.AddShape(new OrganShape { Name="bannerreed_glume", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Lamina,
                    WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Sin(Math.PI*t),.8)),
                    MidribCurl=.12f, CrossCurl=.001f,
                    ColorAdaxial=PlantShapes.Srgb(.84,.76,.48), ColorAbaxial=PlantShapes.Srgb(.65,.64,.37), Roughness=.78f });
                for(int i=0;i<s.Shoots;i++)
                {
                    double az=rotation+Math.Tau*(i+b.Range(-.30,.30))/s.Shoots;
                    var outward=PlantMath.Horizontal(az);
                    var side=PlantMath.Horizontal(az+Math.PI/2);
                    double height=h*b.Range(.78,.96);
                    int culm=b.AddAxis(crown,1,Vec3.Up+outward*b.Range(.04,.13),height,
                        outward*b.Range(.02,.08),r*.009,r*.004,1,Tissue.Green,.015*i/s.Shoots,.45);
                    for(int j=0;j<4;j++)
                    {
                        double t=.12+j*.19;
                        var bladeDir=PlantMath.Horizontal(az+(j%2==0 ? .75 : -.75))+Vec3.Up*.85;
                        b.AddFlat(culm,t,bladeDir,h*b.Range(.24,.38)*(1-.10*j),r*s.Width*.65,
                            .0004,OrganType.Leaf,leaf,2,.08+t*.15,.32);
                    }
                    double panLen=h*b.Range(.18,.26);
                    int rachis=b.AddAxis(culm,1,Vec3.Up+outward*.65,panLen,
                        outward*.85+Vec3.Down*1.25,r*.004,r*.001,2,Tissue.Green,.5,.25);
                    for(int j=0;j<16;j++)
                    {
                        double t=.06+j*.058;
                        var branchDir=outward+side*b.Range(-.35,.35)+Vec3.Down*b.Range(.12,.35);
                        int branch=b.AddAxis(rachis,t,branchDir,panLen*(.40-.20*t)*b.Range(.8,1.15),
                            Vec3.Down*.55,r*.0018,r*.0005,3,Tissue.Green,.52,.2);
                        for(int k=0;k<3;k++)
                            b.AddFlat(branch,.45+k*.25,branchDir+Vec3.Down*.6+side*b.Range(-.18,.18),
                                h*b.Range(.018,.032),r*.012,.0003,OrganType.Flower,glume,3,.6,.15);
                    }
                }
                return;
            }
            for(int i=0;i<s.Shoots;i++)
            {
                // Keep the radial habit, but vary spacing within each sector. Compact
                // rosettes remain more ordered than loose mats and fountains.
                double spacingJitter=s.Form=="rosette" ? .23 : s.Form=="grass" ? .38 : .34;
                double az=rotation+Math.Tau*(i+b.Range(-spacingJitter,spacingJitter))/s.Shoots;
                Vec3 outwards=PlantMath.Horizontal(az), side=PlantMath.Horizontal(az+Math.PI/2);
                double variance=b.Range(.82,1.14), phase=.015*i/s.Shoots;
                if(s.Form=="fern")
                {
                    double frondLean=s.Spread*b.Range(.78,1.22);
                    int rachis=b.AddAxis(crown,1,outwards*frondLean+Vec3.Up+side*b.Range(-.10,.10), h*variance,
                        -Vec3.Up*b.Range(.32,.55)+side*b.Range(-.09,.09), r*.012,r*.002,1,Tissue.Green,phase,.46);
                    for(int j=0;j<s.Leaves;j++)
                    {
                        double t=Math.Clamp(.18+.78*j/Math.Max(1,s.Leaves-1)+b.Range(-.012,.012),.16,.97);
                        double envelope=s.Id=="emberbracken" ? 1-.78*t : Math.Pow(Math.Sin(Math.PI*t),.75);
                        for(int k=-1;k<=1;k+=2) b.AddFlat(rachis,t,side*k+outwards*b.Range(.20,.40)+Vec3.Up*b.Range(-.08,.08), r*.48*envelope*b.Range(.9,1.1),r*s.Width*envelope,.0004,OrganType.Leaflet,leaf,2,.13+t*.18,.18);
                    }
                }
                else if(s.Form=="whorl")
                {
                    int stem=b.AddAxis(crown,1,Vec3.Up+outwards*s.Spread*b.Range(.2,.5)+side*b.Range(-.1,.1),h*variance,
                        outwards*b.Range(.05,.3)+side*b.Range(-.15,.15),r*.012,r*.004,1,Tissue.Green,phase,.48);
                    // Horsetail-like shafts carry tight collars, not broad whorled leaves.
                    if(s.Id is "sootband" or "ringreed")
                    {
                        for(int j=0;j<s.Leaves;j++)
                        {
                            double t=.15+.75*j/Math.Max(1,s.Leaves-1);
                            for(int k=0;k<8;k++)
                            {
                                var collar=PlantMath.Horizontal(az+k*Math.Tau/8);
                                b.AddFlat(stem,t,collar+Vec3.Up*.25,r*.035,r*.009,.0004,OrganType.Leaf,spike,2,.15+t*.2,.15);
                                if(s.Id=="ringreed" && k%2==0)
                                    b.AddFlat(stem,t,collar+Vec3.Down*.35,r*.19,r*.012,.0004,OrganType.Leaf,leaf,2,.2+t*.15,.2);
                            }
                        }
                        if(s.Id=="sootband" && i%3==0)
                            b.AddFlat(stem,1,Vec3.Up,h*.10,r*.025,.001,OrganType.Flower,burr,2,.7,.15);
                        continue;
                    }
                    for(int j=0;j<s.Leaves;j++) for(int k=0;k<4;k++)
                    {
                        double t=Math.Clamp(.15+.78*j/Math.Max(1,s.Leaves-1)+b.Range(-.012,.012),.12,.96);
                        var d=PlantMath.Horizontal(az+k*Math.PI/2+j*.25+b.Range(-.12,.12));
                        double taper=1.1-.45*j/Math.Max(1,s.Leaves-1);
                        var direction=d+Vec3.Up*(.05+.25*t)+b.TangentAt(stem,t)*.15;
                        if(s.Id is "fencomb" or "mirepocket" or "deepquill")
                        {
                            int rachis=b.AddAxis(stem,t,direction,r*s.Spread*.5*taper,Vec3.Zero,r*.003,r*.001,2,Tissue.Green,.1+t*.15,.2);
                            int pairs=s.Id=="deepquill" ? 3 : 5;
                            for(int n=0;n<pairs;n++) for(int sign=-1;sign<=1;sign+=2)
                            {
                                double u=.2+n*.7/Math.Max(1,pairs-1);
                                var branch=PlantMath.Horizontal(az+k*Math.PI/2+sign*Math.PI/2)+d*.35;
                                b.AddFlat(rachis,u,branch,r*.10*(1-u*.5),r*.008,.0003,OrganType.Leaflet,leaf,3,.2,.15);
                            }
                            if(s.Id=="mirepocket" && (j+k)%3==0)
                                b.AddFlat(rachis,.7,Vec3.Up,r*.045,r*.025,.0005,OrganType.Fruit,burr,3,.4,.15);
                        }
                        else b.AddFlat(stem,t,direction,r*s.Spread*.5*taper,r*s.Width,.0004,OrganType.Leaf,leaf,2,.10+t*.15,.2);
                    }
                    if(s.Bloom>0) b.AddFlat(stem,.98,b.TangentAt(stem,.98),h*.08,r*.07,.001,OrganType.Flower,petal,2,.7,.18);
                }
                else if(s.Form=="runner")
                {
                    int stem=b.AddAxis(crown,1,outwards+side*b.Range(-.16,.16)+Vec3.Up*.10,r*s.Spread*variance,
                        side*b.Range(-.16,.16)+Vec3.Up*.04,r*.012,r*.003,1,Tissue.Green,phase,.5);
                    for(int j=0;j<s.Leaves;j++)
                    {
                        double t=Math.Clamp(.18+.78*j/Math.Max(1,s.Leaves-1)+b.Range(-.025,.025),.15,.98);
                        var d=side*(j%2==0?1:-1)+outwards*b.Range(-.15,.2)+Vec3.Up*b.Range(.22,.38);
                        if(s.Id is "cinderberry" or "trifold")
                        {
                            int petiole=b.AddAxis(stem,t,Vec3.Up+d*.2,h*.35,Vec3.Zero,r*.006,r*.002,2,Tissue.Green,.1,.2);
                            for(int k=-1;k<=1;k++)
                                b.AddFlat(petiole,1,PlantMath.Horizontal(az+k*.85)+Vec3.Up*.18,r*.18,r*s.Width*.55,.0005,OrganType.Leaflet,leaf,3,.25,.2);
                        }
                        else b.AddFlat(stem,t,d,r*.27,r*s.Width,.0005,OrganType.Leaf,leaf,2,.1+t*.15,.18);
                    }
                    if(s.Id=="cinderberry")
                    {
                        int ped=b.AddAxis(stem,.7,Vec3.Up,h*.35,Vec3.Zero,r*.004,r*.002,2,Tissue.Green,.5,.2);
                        b.AddFlat(ped,1,Vec3.Up,h*.12,r*.045,.001,OrganType.Fruit,berry,3,.7,.2);
                    }
                    else if(s.Bloom>0) b.AddFlat(stem,.85,Vec3.Up,r*.13,r*.06,.001,OrganType.Fruit,petal,2,.7,.2);
                }
                else if(s.Form=="grass")
                {
                    // Each culm gets its own lean and arch so a tussock fountains instead of radiating
                    // as identical spokes: inner culms stand taller, outer ones splay and bow more.
                    double splay=s.Spread*b.Range(.45,1.35);
                    var d=outwards*splay+Vec3.Up+side*b.Range(-.12,.12);
                    var arch=-Vec3.Up*splay*b.Range(.3,.65)+side*b.Range(-.15,.15);
                    int culm=b.AddAxis(crown,1,d,h*variance*(1.08-.2*splay/Math.Max(.1,s.Spread)),arch,r*.009,r*.002,1,Tissue.Green,phase,.45);
                    b.AddFlat(culm,.04,d+side*b.Range(-.25,.25)+outwards*b.Range(0,.35),h*b.Range(.55,.9)*variance,r*s.Width*b.Range(.8,1.2),.0004,OrganType.Leaf,leaf,2,.05,.38);
                    if(s.Leaves>1)
                        b.AddFlat(culm,b.Range(.18,.32),Vec3.Up*.65+outwards*s.Spread*b.Range(.7,1.4)+side*b.Range(-.5,.5),h*b.Range(.35,.6),r*s.Width*.8,.0004,OrganType.Leaf,leaf,2,.12,.32);
                    if(s.Id=="lanternsedge" && i%3==0)
                    {
                        for(int ray=0;ray<8;ray++)
                        {
                            var rd=PlantMath.Horizontal(az+ray*Math.Tau/8)+Vec3.Up*.55;
                            int branch=b.AddAxis(culm,1,rd,r*.26,Vec3.Zero,r*.004,r*.001,2,Tissue.Green,.5,.2);
                            b.AddFlat(branch,1,Vec3.Up,h*.035,r*.025,.001,OrganType.Flower,lantern,3,.7,.2);
                        }
                    }
                    else if(s.Id=="burrflag" && i%2==0)
                    {
                        for(int head=0;head<3;head++)
                        {
                            double t=.65+head*.15;
                            b.AddFlat(culm,t,Vec3.Up,h*.055,r*.045,.001,OrganType.Fruit,burr,2,.6,.2);
                            for(int k=0;k<12;k++)
                            {
                                double y=1-2*(k+.5)/12.0, a=k*2.39996;
                                var ray=PlantMath.Horizontal(a)*Math.Sqrt(1-y*y)+Vec3.Up*y;
                                b.AddFlat(culm,t,ray,r*.08,r*.008,.0003,OrganType.Leaf,leaf,2,.6,.15);
                            }
                        }
                    }
                    else if(s.Id=="plumegrass" && i%2==0)
                    {
                        for(int tier=0;tier<7;tier++) for(int k=0;k<5;k++)
                        {
                            double t=.58+tier*.065, a=az+k*Math.Tau/5+tier*.7;
                            var rd=PlantMath.Horizontal(a)*(.7-tier*.06)+Vec3.Up*.6;
                            int branch=b.AddAxis(culm,t,rd,r*(.25-tier*.022),Vec3.Down*.12,r*.003,r*.0008,2,Tissue.Green,.5,.2);
                            for(int n=0;n<3;n++) b.AddFlat(branch,.4+n*.25,rd+Vec3.Up*.3,h*.055,r*.012,.0004,OrganType.Flower,cotton,3,.65,.15);
                        }
                    }
                    else if(s.Id=="cloudtuft" && i%2==0)
                        b.AddFlat(culm,1,Vec3.Up,h*.11,r*.11,.001,OrganType.Flower,cotton,2,.65,.2);
                    else if(s.Id=="coalspike" && i%2==0)
                        b.AddFlat(culm,.92,Vec3.Up,h*.20,r*.065,.001,OrganType.Flower,spike,2,.65,.2);
                    else if(s.Bloom>0 && i%2==0 && s.Id is not ("lanternsedge" or "burrflag" or "plumegrass")) for(int j=0;j<Math.Min(3,s.Bloom);j++)
                    {
                        double bt=.75+j*.09; var tan=b.TangentAt(culm,bt);
                        b.AddFlat(culm,bt,tan+side*(j-1)*.4+Vec3.Down*.15*s.Spread,h*.12,r*.045,.0006,OrganType.Flower,petal,2,.58+j*.04,.18);
                    }
                }
                else if(s.Form=="forb")
                {
                    // Stems lean out and nod; leaves are decussate pairs that follow the stem tangent,
                    // shrinking upward, so the plant tapers into its inflorescence.
                    double lean=s.Spread*b.Range(.2,.55);
                    int stem=b.AddAxis(crown,1,Vec3.Up+outwards*lean+side*b.Range(-.1,.1),h*variance,
                        outwards*b.Range(-.1,.2)-Vec3.Up*b.Range(0,.15)+side*b.Range(-.12,.12),r*.014,r*.003,1,Tissue.Green,phase,.5);
                    double rot=b.Range(0,Math.PI);
                    for(int j=0;j<s.Leaves;j++)
                    {
                        double t=.12+.7*j/Math.Max(1,s.Leaves-1)+b.Range(-.03,.03);
                        var tan=b.TangentAt(stem,t); var lat=PlantMath.SafeNormal(tan.Cross(Vec3.Up),side);
                        var fwd=PlantMath.SafeNormal(lat.Cross(tan),outwards);
                        double ang=rot+j*Math.PI/2; var pairDir=lat*Math.Cos(ang)+fwd*Math.Sin(ang);
                        double size=1.15-.55*j/Math.Max(1,s.Leaves-1);
                        for(int k=-1;k<=1;k+=2)
                        {
                            var leafDir=pairDir*k+tan*.35+Vec3.Up*b.Range(.05,.3);
                            double length=r*.55*size*b.Range(.85,1.1);
                            int leafAxis=stem;
                            double leafAt=t;
                            b.AddFlat(leafAxis,leafAt,leafDir,length,r*s.Width*size,.0005,OrganType.Leaf,leaf,2,.1+t*.15,.2);
                        }
                    }
                    if(s.Id is "heddlelace" or "fenfoam" or "widowspire")
                    {
                        if(s.Id=="widowspire")
                        {
                            for(int tier=0;tier<9;tier++) for(int k=0;k<5;k++)
                            {
                                double t=.57+tier*.047, a=az+k*Math.Tau/5+tier*.7;
                                var ray=PlantMath.Horizontal(a)+Vec3.Up*.2;
                                b.AddFlat(stem,t,ray,h*.035,r*.025,.0005,OrganType.Flower,petal,2,.6,.15);
                            }
                        }
                        else
                        {
                            int branches=s.Id=="heddlelace" ? 10 : 7;
                            for(int k=0;k<branches;k++)
                            {
                                double a=az+k*Math.Tau/branches;
                                var ray=PlantMath.Horizontal(a);
                                double height=s.Id=="heddlelace" ? .42 : .8;
                                int branch=b.AddAxis(stem,.86,ray+Vec3.Up*height,r*(s.Id=="heddlelace" ? .30 : .22),Vec3.Zero,r*.003,r*.001,2,Tissue.Green,.55,.2);
                                for(int n=0;n<5;n++)
                                {
                                    var tip=PlantMath.Horizontal(a+n*Math.Tau/5)+Vec3.Up*.4;
                                    int ped=b.AddAxis(branch,1,tip,r*.055,Vec3.Zero,r*.0015,r*.0005,3,Tissue.Green,.65,.15);
                                    b.AddFlat(ped,1,Vec3.Up,h*.018,r*.018,.0004,OrganType.Flower,petal,4,.75,.12);
                                }
                            }
                        }
                        continue;
                    }
                    for(int j=0;j<Math.Max(1,s.Bloom);j++)
                    {
                        var d=PlantMath.Horizontal(az+Math.Tau*j/Math.Max(1,s.Bloom)+b.Range(-.3,.3));
                        double st=Math.Clamp(.78+b.Range(0,.2),.05,.98);
                        int stalk=b.AddAxis(stem,st,d*b.Range(.4,.9)+Vec3.Up,h*b.Range(.11,.19),d*.25-Vec3.Up*b.Range(.1,.5),r*.004,r*.001,2,Tissue.Green,.6,.2);
                        b.AddFlat(stalk,1,b.TangentAt(stalk,1)+Vec3.Up*.4,h*(starBloom ? .025 : .10),r*.09,.001,OrganType.Flower,petal,3,.75,.15);
                    }
                }
                else if(s.Form=="pitcher")
                {
                    int stalk=b.AddAxis(crown,1,outwards*b.Range(.26,.43)+Vec3.Up,h*.25*variance,outwards*.08,r*.012,r*.006,1,Tissue.Green,phase,.3);
                    b.AddFlat(stalk,1,outwards*.2+Vec3.Up,h*.58,r*.13,.001,OrganType.Flower,pitcher,2,.25,.42);
                    b.AddFlat(stalk,.95,outwards+Vec3.Up*.65,r*.27,r*.14,.001,OrganType.Leaf,leaf,2,.48,.2);
                    // One tall nodding flower scape rises above the pitcher rosette.
                    if(i==0)
                    {
                        int scape=b.AddAxis(crown,1,Vec3.Up+outwards*.12,h, -Vec3.Up*.15,r*.008,r*.002,1,Tissue.Green,.4,.3);
                        b.AddFlat(scape,1,outwards+Vec3.Down*.35,h*.13,r*.10,.001,OrganType.Flower,petal,2,.7,.2);
                    }
                }
                else
                {
                    double rise=s.Form=="pad" ? .15 : s.Form=="trap" ? .28 : .45;
                    int stalk=b.AddAxis(crown,1,outwards*s.Spread+side*b.Range(-.08,.08)+Vec3.Up*rise,r*.48*variance,
                        Vec3.Up*.08+side*b.Range(-.08,.08),r*.011,r*.003,1,Tissue.Green,phase,.35);
                    int count=s.Form=="pad" && s.Id is not ("bogfringe" or "hushclover" or "starveil") ? s.Leaves : 1;
                    for(int j=0;j<count;j++)
                    {
                        var d=PlantMath.Horizontal(az+(j-(count-1)*.5)*.8)+Vec3.Up*rise;
                        if(s.Form=="pad" && s.Id is "bogfringe" or "hushclover" or "starveil")
                        {
                            // Compound and palmate foliage consists of separate leaflets, not discs.
                            int petiole=b.AddAxis(stalk,.9,d+Vec3.Up*.3,r*.12,Vec3.Zero,r*.006,r*.002,2,Tissue.Green,.18,.18);
                            for(int l=0;l<3;l++)
                            {
                                var leaflet=PlantMath.Horizontal(az+(l-1)*.9)+Vec3.Up*.12;
                                b.AddFlat(petiole,1,leaflet,r*.22,r*s.Width*.58,.0005,OrganType.Leaflet,leaf,3,.28,.25);
                            }
                        }
                        else if(s.Form=="pad")
                        {
                            // Short upright radial axis produces a horizontal, broad floating disc.
                            int petiole=b.AddAxis(stalk,.9,d,r*.12,Vec3.Zero,r*.006,r*.002,2,Tissue.Green,.18,.18);
                            b.AddFlat(petiole,1,Vec3.Up,Math.Max(.002,h*.018),r*s.Width,.0005,OrganType.Leaf,pad,3,.28,.25);
                        }
                        else b.AddFlat(stalk,.9,d,r*(s.Id=="tankbloom" ? .65 : .45),r*s.Width*(s.Id=="tankbloom" ? 1.35 : 1),.0006,OrganType.Leaf,leaf,2,.14+j*.025,.28);
                    }
                    if(s.Form=="trap")
                    {
                        if(s.Id=="blue_sundew")
                        {
                            int dewStem=b.AddAxis(stalk,1,Vec3.Up,h*.6,Vec3.Zero,r*.003,r*.001,2,Tissue.Green,.35,.25);
                            b.AddFlat(dewStem,1,Vec3.Up,h*.28,r*.045,.001,OrganType.Flower,club,3,.55,.2);
                            for(int ray=0;ray<6;ray++)
                            {
                                var rayDir=PlantMath.Horizontal(az+ray*Math.Tau/6);
                                b.AddFlat(dewStem,1,rayDir*.45+Vec3.Up,r*.045,r*.014,.0004,OrganType.Flower,club,3,.6,.15);
                            }
                        }
                        else
                        {
                            // Two opposing ciliate blades form a visibly open jaw at each petiole tip.
                            for(int jaw=-1;jaw<=1;jaw+=2)
                                b.AddFlat(stalk,1,outwards+side*jaw*.65+Vec3.Up*.28,r*.25,r*.085,.001,OrganType.Leaf,trap,2,.4,.25);
                        }
                    }
                    if(s.Id=="starbulb" && i==0)
                    {
                        int scape=b.AddAxis(crown,1,Vec3.Up,h,Vec3.Zero,r*.012,r*.004,1,Tissue.Green,.4,.3);
                        for(int n=0;n<20;n++)
                        {
                            double y=1-2*(n+.5)/20.0, a=n*2.39996;
                            var ray=PlantMath.Horizontal(a)*Math.Sqrt(1-y*y)+Vec3.Up*y;
                            int pedicel=b.AddAxis(scape,1,ray,r*.22,Vec3.Zero,r*.003,r*.001,2,Tissue.Green,.6,.2);
                            b.AddFlat(pedicel,1,ray,h*.035,r*.035,.001,OrganType.Flower,petal,3,.75,.2);
                        }
                    }
                    else if(s.Bloom>0 && i%3==0 && s.Id is not ("starbulb" or "tankbloom" or "blue_sundew") && (s.Id is not ("glasslily" or "mirrorleaf") || i==0))
                    {
                        int scape=b.AddAxis(crown,1,Vec3.Up+outwards*.15,h*variance,Vec3.Zero,r*.008,r*.002,1,Tissue.Green,.4,.3);
                        b.AddFlat(scape,1,Vec3.Up,h*(starBloom ? .025 : .12),r*.08,.001,OrganType.Flower,petal,2,.7,.2);
                    }
                }
            }
        }
    }
}
