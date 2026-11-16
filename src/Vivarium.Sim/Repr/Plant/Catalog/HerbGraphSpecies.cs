using System;
using System.Collections.Generic;
using System.Linq;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>Individually authored herbaceous architecture. Dimensions scale with content ecology.</summary>
public static class HerbGraphSpecies
{
    // form, number of primary shoots, leaves/pairs per shoot, spread, leaf width, inflorescence character
    private sealed record Spec(string Id, string Form, int Shoots, int Leaves, double Spread, double Width, int Bloom, string Habit);
    private static readonly Spec[] Table = {
        new("ashwire","grass",28,1,.85,.018,2,"fine dry bristle turf"),
        new("bannerreed","grass",11,2,.52,.075,5,"broad marsh reed with hanging panicles"),
        new("barbarrow","forb",7,3,.75,.22,3,"emergent arrow leaf whorls"),
        new("blackneedle","rosette",12,2,.94,.17,5,"spiny basal leaves and bracted crowns"),
        new("blue_sundew","trap",15,1,.95,.10,1,"low sundew colony and raised clubs"),
        new("bogfringe","pad",9,3,.95,.23,3,"floating trifoliate raft"),
        new("brightknot","rosette",23,1,.98,.14,4,"many tight succulent stars"),
        new("brooklace","runner",7,8,.96,.07,0,"creeping aquatic laced stems"),
        new("burlmantle","rosette",9,2,.95,.30,3,"huge felted basal leaves and burs"),
        new("burrflag","grass",9,1,.78,.09,2,"ribbon leaves and burr stalks"),
        new("butterglint","forb",6,3,.65,.18,5,"glossy meadow cups"),
        new("cinderberry","runner",5,5,.95,.14,3,"trifoliate runners and fruit"),
        new("cloudtuft","grass",17,1,.32,.018,4,"narrow stems with cotton heads"),
        new("coalspike","grass",15,2,.38,.08,3,"dense emergent straps with spikes"),
        new("coinrunner","runner",6,7,.98,.20,0,"round leaves along stolons"),
        new("cragset","rosette",15,1,.72,.19,1,"compact sculptural stone rosette"),
        new("deepquill","whorl",7,7,.30,.018,0,"upright forked aquatic quills"),
        new("duskbristle","grass",24,1,.85,.035,1,"shaggy woodland fountain"),
        new("emberbracken","fern",6,10,.95,.13,0,"triangular stiff fronds"),
        new("fenbead","whorl",10,5,.52,.14,0,"short stems of fleshy beads"),
        new("fencomb","whorl",8,11,.62,.025,0,"fine submerged combs"),
        new("fenfoam","forb",8,7,.72,.10,7,"branching froth over pinnate leaves"),
        new("frosttussock","grass",30,1,.77,.025,1,"arching fine blue tussock"),
        new("glassfinger","runner",7,5,.88,.11,0,"fleshy angled gravel runners"),
        new("glasslily","pad",8,1,.95,.32,1,"round pads and tall glass cup"),
        new("glassrush","grass",19,1,.35,.025,0,"compact jointed vertical fans"),
        new("hallowsting","forb",8,6,.54,.17,3,"opposite serrated leaves and tassels"),
        new("heddlelace","forb",6,6,.78,.07,9,"hollow stalks and lace umbels"),
        new("hushclover","pad",8,3,.75,.20,2,"shamrock leaves under hovering flowers"),
        new("lanternsedge","grass",13,2,.45,.055,7,"sedge fans and radiating lantern heads"),
        new("marrowbloom","forb",6,0,.27,.04,1,"leafless nodding pale bells"),
        new("marshkettle","pad",9,1,.85,.24,4,"glossy round leaves and cups"),
        new("mirepocket","whorl",8,8,.83,.025,2,"feathery branches with little bladders"),
        new("mirrorleaf","pad",11,1,.98,.29,2,"broad pond pads and pale cups"),
        new("mirrorstrap_fern","rosette",11,1,.65,.15,0,"undivided upright glossy fern straps"),
        new("mistviolet","pad",7,1,.72,.20,3,"round basal leaves and nodding stars"),
        new("mooncoin","runner",8,6,.96,.16,0,"wiry silver coin mat"),
        new("moonfern","fern",6,14,.75,.18,0,"luminous arching divided fronds"),
        new("paddlerib","forb",7,3,.68,.22,6,"ribbed paddles and airy whorls"),
        new("pitcher_plant","pitcher",7,1,.75,.20,1,"upright hollow pitchers with hoods"),
        new("plumegrass","grass",10,1,.36,.035,6,"layered feathery plume stalks"),
        new("pondstars","rosette",18,1,.99,.14,0,"small floating star colonies"),
        new("prismstar","rosette",10,2,.80,.15,3,"star rosettes with tiny blooms"),
        new("quillsedge","grass",25,1,.94,.025,2,"fine arching fountain sedge"),
        new("ringreed","whorl",12,6,.40,.018,0,"jointed shafts and radial collars"),
        new("sceptrefern","fern",7,8,.68,.11,0,"tall separated pinnae crown"),
        new("siltstars","rosette",22,1,.99,.10,0,"submerged ground-hugging star carpet"),
        new("silverweed","whorl",8,7,.85,.035,0,"ribbon whorls on creeping bed"),
        new("snaptrap","trap",10,1,.88,.16,2,"radiating paired toothed traps"),
        new("sootband","whorl",20,5,.28,.012,1,"dense jointed rush shafts"),
        new("starbulb","rosette",11,1,.72,.07,9,"strap rosette and globe of stars"),
        new("starveil","pad",9,3,.76,.16,5,"palmate spring leaves and white stars"),
        new("streamribbon","grass",15,1,.52,.065,0,"long underwater tape blades"),
        new("sunstone_rosette","rosette",18,1,.85,.19,0,"tight fleshy triangular rosette"),
        new("swanfrond","fern",9,15,.88,.10,0,"soft layered shuttlecock fronds"),
        new("tankbloom","rosette",14,2,.96,.15,7,"broad tank rosette and bract spike"),
        new("tidebell","forb",7,4,.55,.17,2,"paired waxy leaves and bells"),
        new("trifold","runner",7,4,.92,.17,0,"loose trifoliate meadow stolons"),
        new("veilblade","grass",28,1,.86,.07,2,"tall bowing ribbon curtain"),
        new("veilfern","fern",8,12,.96,.09,0,"fine arching divided veil"),
        new("widowspire","forb",9,6,.63,.09,8,"tall narrow leaves and flower spires")
    };
    public static IReadOnlyList<GraphSpeciesInfo> Species { get; } = Array.AsReadOnly(Table.Select(s => new GraphSpeciesInfo(s.Id, s.Form, s.Habit, s.Habit)).ToArray());
    public static PlanGenerator? Create(FloraSpeciesDef species)
    {
        var s = Array.Find(Table, x => x.Id == species.Id);
        return s == null ? null : s.Id == "moonfern" ? new MoonfernGenerator() : new Generator(s, species);
    }

    private sealed class Generator : PlanGenerator
    {
        private readonly Spec s;
        private readonly double h, r;
        public Generator(Spec spec, FloraSpeciesDef def) { s = spec; h = Math.Max(.025, def.Height); r = Math.Max(.035, def.RadiusAtMax); }
        public override string SpeciesId => s.Id;
        protected override void Design(PlanBuilder b)
        {
            var green = PlantShapes.Srgb(.22,.43,.20);
            bool blade = s.Form == "grass" || (s.Form == "whorl" && s.Width < .05);
            // Cross curl is expressed in leaf lengths, so slender blades need a much
            // smaller value than broad leaves to avoid huge transverse folds.
            float crossCurl = blade ? .0005f : s.Form == "fern" ? .008f : .025f;
            float midribCurl = s.Id is "duskbristle" or "frosttussock" or "quillsedge" or "veilblade" ? .36f
                : blade ? .18f : s.Form == "rosette" ? .12f : .08f;
            ushort leaf = b.AddShape(new OrganShape { Name=s.Id+"_leaf", Type=OrganType.Leaf, SurfaceForm=OrganSurfaceForm.Lamina,
                WidthProfile=PlantShapes.Profile(t => blade ? Math.Pow(1-t,.7)*Math.Pow(Math.Sin(Math.PI*t*.5),.18) : Math.Pow(Math.Sin(Math.PI*t),.75)), MidribCurl=midribCurl, CrossCurl=crossCurl,
                ColorAdaxial=green, ColorAbaxial=PlantShapes.Srgb(.32,.46,.25), Thickness=.0006f, Roughness=.57f });
            bool starBloom = s.Bloom >= 4;
            ushort petal = b.AddShape(new OrganShape { Name=s.Id+"_flower", Type=OrganType.Flower,
                SurfaceForm=starBloom ? OrganSurfaceForm.Star : OrganSurfaceForm.Cup,
                LobeCount=Math.Max(3,Math.Min(9,s.Bloom)), LobeDepth=.42f, RimCurl=.16f,
                WidthProfile=PlantShapes.Profile(t => starBloom ? .5*Math.Sin(Math.PI*t) : .12+.38*Math.Pow(t,.7)),
                ColorAdaxial=PlantShapes.Srgb(.84,.76,.48), ColorAbaxial=PlantShapes.Srgb(.53,.58,.30) });
            ushort pad = b.AddShape(new OrganShape { Name=s.Id+"_floating_pad", Type=OrganType.Leaf,
                SurfaceForm=OrganSurfaceForm.Disc, WidthProfile=PlantShapes.Profile(t => .5*Math.Sin(Math.PI*t)),
                ColorAdaxial=PlantShapes.Srgb(.19,.38,.20), ColorAbaxial=PlantShapes.Srgb(.31,.31,.17), Wax=.65f });
            ushort pitcher = b.AddShape(new OrganShape { Name=s.Id+"_pitcher", Type=OrganType.Flower,
                SurfaceForm=OrganSurfaceForm.Cup, WidthProfile=PlantShapes.Profile(t => .12+.38*Math.Pow(t,.7)),
                RimCurl=.25f, ShellThickness=.025f, ColorAdaxial=PlantShapes.Srgb(.38,.48,.19),
                ColorAbaxial=PlantShapes.Srgb(.45,.18,.16) });
            ushort trap = b.AddShape(new OrganShape { Name=s.Id+"_trap_lobe", Type=OrganType.Leaf,
                SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*t),.65)),
                Serration=.28f, SerrationFreq=12, CrossCurl=.035f, ColorAdaxial=PlantShapes.Srgb(.52,.18,.17),
                ColorAbaxial=PlantShapes.Srgb(.25,.43,.18), Thickness=.001f });
            ushort club = b.AddShape(new OrganShape { Name=s.Id+"_dew_club", Type=OrganType.Flower,
                SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Sin(Math.PI*t),.65)),
                ColorAdaxial=PlantShapes.Srgb(.14,.48,.72), ColorAbaxial=PlantShapes.Srgb(.12,.30,.46) });
            int crown=b.AddAxis(-1,0,Vec3.Up,Math.Max(.004,h*.035),Vec3.Zero,Math.Max(.001,r*.035),Math.Max(.0007,r*.025),0,Tissue.Green,0,.12);
            double rotation=b.Range(0,Math.Tau);
            for(int i=0;i<s.Shoots;i++)
            {
                double az=rotation+Math.Tau*i/s.Shoots+b.Range(-.08,.08);
                Vec3 outwards=PlantMath.Horizontal(az), side=PlantMath.Horizontal(az+Math.PI/2);
                double variance=b.Range(.82,1.14), phase=.015*i/s.Shoots;
                if(s.Form=="fern")
                {
                    int rachis=b.AddAxis(crown,1,outwards*s.Spread+Vec3.Up, h*variance, -Vec3.Up*.48, r*.012,r*.002,1,Tissue.Green,phase,.46);
                    for(int j=0;j<s.Leaves;j++)
                    {
                        double t=.18+.78*j/Math.Max(1,s.Leaves-1), envelope=Math.Pow(Math.Sin(Math.PI*t),.75);
                        for(int k=-1;k<=1;k+=2) b.AddFlat(rachis,t,side*k+outwards*.3, r*.48*envelope,r*s.Width*envelope,.0004,OrganType.Leaflet,leaf,2,.13+t*.18,.18);
                    }
                }
                else if(s.Form=="whorl")
                {
                    int stem=b.AddAxis(crown,1,Vec3.Up+outwards*s.Spread*.35,h*variance, outwards*.15,r*.012,r*.004,1,Tissue.Green,phase,.48);
                    for(int j=0;j<s.Leaves;j++) for(int k=0;k<4;k++)
                    {
                        double t=.15+.78*j/Math.Max(1,s.Leaves-1); var d=PlantMath.Horizontal(az+k*Math.PI/2+j*.25);
                        b.AddFlat(stem,t,d+Vec3.Up*.12,r*s.Spread*.5,r*s.Width,.0004,OrganType.Leaf,leaf,2,.10+t*.15,.2);
                    }
                    if(s.Bloom>0) b.AddFlat(stem,.98,Vec3.Up,h*.08,r*.07,.001,OrganType.Flower,petal,2,.7,.18);
                }
                else if(s.Form=="runner")
                {
                    int stem=b.AddAxis(crown,1,outwards+Vec3.Up*.10,r*s.Spread*variance,Vec3.Up*.04,r*.012,r*.003,1,Tissue.Green,phase,.5);
                    for(int j=0;j<s.Leaves;j++)
                    {
                        double t=.18+.78*j/Math.Max(1,s.Leaves-1); var d=side*(j%2==0?1:-1)+Vec3.Up*.3;
                        b.AddFlat(stem,t,d,r*.27,r*s.Width,.0005,OrganType.Leaf,leaf,2,.1+t*.15,.18);
                    }
                    if(s.Bloom>0) b.AddFlat(stem,.85,Vec3.Up,r*.13,r*.06,.001,OrganType.Fruit,petal,2,.7,.2);
                }
                else if(s.Form=="grass")
                {
                    var d=outwards*s.Spread+Vec3.Up; int culm=b.AddAxis(crown,1,d,h*variance,-Vec3.Up*s.Spread*.4,r*.009,r*.002,1,Tissue.Green,phase,.45);
                    b.AddFlat(culm,.04,d+side*b.Range(-.16,.16),h*b.Range(.62,.88)*variance,r*s.Width*b.Range(.8,1.2),.0004,OrganType.Leaf,leaf,2,.05,.38);
                    if(s.Leaves>1)
                        b.AddFlat(culm,.25,Vec3.Up*.65+outwards*s.Spread+side*b.Range(-.4,.4),h*b.Range(.38,.58),r*s.Width*.8,.0004,OrganType.Leaf,leaf,2,.12,.32);
                    if(s.Bloom>0 && i%2==0) for(int j=0;j<Math.Min(3,s.Bloom);j++)
                        b.AddFlat(culm,.75+j*.09,Vec3.Up+side*(j-1)*.4,h*.12,r*.045,.0006,OrganType.Flower,petal,2,.58+j*.04,.18);
                }
                else if(s.Form=="forb")
                {
                    int stem=b.AddAxis(crown,1,Vec3.Up+outwards*s.Spread*.35,h*variance,outwards*.14,r*.014,r*.003,1,Tissue.Green,phase,.5);
                    for(int j=0;j<s.Leaves;j++) for(int k=-1;k<=1;k+=2)
                    {
                        double t=.18+.67*j/Math.Max(1,s.Leaves-1);
                        b.AddFlat(stem,t,side*k+Vec3.Up*.25,r*.55,r*s.Width,.0005,OrganType.Leaf,leaf,2,.1+t*.15,.2);
                    }
                    for(int j=0;j<Math.Max(1,s.Bloom);j++)
                    {
                        var d=PlantMath.Horizontal(az+Math.Tau*j/Math.Max(1,s.Bloom));
                        int stalk=b.AddAxis(stem,.85,d*.5+Vec3.Up,h*.15,Vec3.Zero,r*.004,r*.001,2,Tissue.Green,.6,.2);
                        b.AddFlat(stalk,1,Vec3.Up,h*(starBloom ? .025 : .10),r*.09,.001,OrganType.Flower,petal,3,.75,.15);
                    }
                }
                else if(s.Form=="pitcher")
                {
                    int stalk=b.AddAxis(crown,1,outwards*.35+Vec3.Up,h*.25,Vec3.Zero,r*.012,r*.006,1,Tissue.Green,phase,.3);
                    b.AddFlat(stalk,1,outwards*.2+Vec3.Up,h*.58,r*.13,.001,OrganType.Flower,pitcher,2,.25,.42);
                    b.AddFlat(stalk,.95,outwards+Vec3.Up*.65,r*.27,r*.09,.001,OrganType.Leaf,leaf,2,.48,.2);
                }
                else
                {
                    double rise=s.Form=="pad" ? .15 : s.Form=="trap" ? .28 : .45;
                    int stalk=b.AddAxis(crown,1,outwards*s.Spread+Vec3.Up*rise,r*.48*variance,Vec3.Up*.08,r*.011,r*.003,1,Tissue.Green,phase,.35);
                    int count=s.Form=="pad" ? s.Leaves : 1;
                    for(int j=0;j<count;j++)
                    {
                        var d=PlantMath.Horizontal(az+(j-(count-1)*.5)*.8)+Vec3.Up*rise;
                        if(s.Form=="pad")
                        {
                            // Short upright radial axis produces a horizontal, broad floating disc.
                            int petiole=b.AddAxis(stalk,.9,d,r*.12,Vec3.Zero,r*.006,r*.002,2,Tissue.Green,.18,.18);
                            b.AddFlat(petiole,1,Vec3.Up,Math.Max(.002,h*.018),r*s.Width,.0005,OrganType.Leaf,pad,3,.28,.25);
                        }
                        else b.AddFlat(stalk,.9,d,r*.45,r*s.Width,.0006,OrganType.Leaf,leaf,2,.14+j*.025,.28);
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
                                b.AddFlat(dewStem,.92,rayDir+Vec3.Up*.35,r*.08,r*.014,.0004,OrganType.Leaf,leaf,3,.6,.15);
                            }
                        }
                        else
                        {
                            // Two opposing ciliate blades form a visibly open jaw at each petiole tip.
                            for(int jaw=-1;jaw<=1;jaw+=2)
                                b.AddFlat(stalk,1,outwards+side*jaw*.65+Vec3.Up*.28,r*.25,r*.085,.001,OrganType.Leaf,trap,2,.4,.25);
                        }
                    }
                    if(s.Bloom>0 && i%3==0)
                    {
                        int scape=b.AddAxis(crown,1,Vec3.Up+outwards*.15,h*variance,Vec3.Zero,r*.008,r*.002,1,Tissue.Green,.4,.3);
                        b.AddFlat(scape,1,Vec3.Up,h*(starBloom ? .025 : .12),r*.08,.001,OrganType.Flower,petal,2,.7,.2);
                    }
                }
            }
        }
    }
}
