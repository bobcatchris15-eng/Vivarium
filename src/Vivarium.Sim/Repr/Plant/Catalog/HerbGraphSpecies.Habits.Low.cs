using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

public static partial class HerbGraphSpecies
{
    private sealed partial class Generator
    {
        private bool TryLowHabit(PlanBuilder b, int crown, double rotation, HabitPalette p)
        {
            bool runner=s.Id is "brooklace" or "cinderberry" or "coinrunner" or "glassfinger" or "mooncoin" or "trifold";
            bool colony=s.Id is "blue_sundew" or "brightknot" or "cragset" or "pondstars" or "siltstars" or "sunstone_rosette";
            bool other=s.Id is "blackneedle" or "bogfringe" or "burlmantle" or "glasslily" or "hushclover"
                or "marshkettle" or "mirrorleaf" or "mistviolet" or "prismstar" or "starbulb"
                or "starveil" or "tankbloom" or "tidebell";
            if(!runner && !colony && !other) return false;

            if (s.Id == "pondstars")
            {
                // Individual floating leaflets scattered over a shared water-level plane.
                var stars = new ushort[3];
                for (int points = 4; points <= 6; points++)
                    stars[points-4] = b.AddShape(new OrganShape {
                        Name = "pondstars_star_" + points, Type = OrganType.Leaf,
                        SurfaceForm = OrganSurfaceForm.Star, LobeCount = points,
                        ColorAdaxial = PlantShapes.Srgb(.34,.55,.25),
                        ColorAbaxial = PlantShapes.Srgb(.23,.39,.17), Roughness = .72f
                    });
                int offset = (int)(b.Range(0, 3));
                for (int i = 0; i < 24; i++)
                {
                    double a = rotation + i * 2.399963229728653 + b.Range(-.16,.16);
                    var radial = PlantMath.Horizontal(a);
                    double distance = r * (.12 + .72 * Math.Sqrt((i+.5)/24.0));
                    int anchor = b.AddAxis(crown, 0, radial, distance, Vec3.Zero,
                        .0001, .0001, 1, Tissue.Green, .01, .08, OrganFlags.Hidden);
                    double direction = a + b.Range(-1.0,1.0);
                    double size = r * b.Range(.085,.13);
                    b.AddFlat(anchor, 1, PlantMath.Horizontal(direction), size*2, size,
                        .0002, OrganType.Leaf, stars[(i+offset)%3], 2, .10+i*.004, .20);
                }
                return true;
            }

            if(other)
            {
                bool aquatic=s.Id is "bogfringe" or "glasslily" or "mirrorleaf";
                bool compound=s.Id is "bogfringe" or "hushclover" or "starveil";
                bool big=s.Id is "blackneedle" or "burlmantle";
                bool central=s.Id is "starbulb" or "tankbloom";
                int nodes=s.Id switch { "glasslily" => 10, "mirrorleaf" => 7, "bogfringe" => 7,
                    "hushclover" => 5, "starveil" => 4, "marshkettle" => 4, "mistviolet" => 4,
                    "prismstar" => 3, "tidebell" => 5, "blackneedle" => 1, "burlmantle" => 1, _ => 1 };
                ushort tube=0;
                if(s.Id=="tidebell")
                    tube=b.AddShape(new OrganShape { Name="tidebell_luminous_bell", Type=OrganType.Flower,
                        SurfaceForm=OrganSurfaceForm.Cup,
                        // Narrow throat, belly swell, then a flared lip that rolls back outward.
                        WidthProfile=PlantShapes.Profile(t => .07+.30*Math.Pow(t,.8)+.17*Math.Pow(Math.Max(0,(t-.62)/.38),1.6)),
                        ShellThickness=.012f, RimCurl=.50f, LobeCount=5, LobeDepth=.16f,
                        RadialAsymmetry=.20f, EdgeWave=.07f, EdgeWaveFreq=5, ColorAdaxial=PlantShapes.Srgb(.92,.76,.46),
                        ColorAbaxial=PlantShapes.Srgb(0.68,0.468,0.256), Roughness=.42f });
                ushort blackSpine=0, thistleBract=0, thistleFloret=0;
                if(s.Id=="blackneedle")
                {
                    blackSpine=b.AddShape(new OrganShape { Name="blackneedle_glass_spine", Type=OrganType.Leaf,
                        SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => 1-t),
                        ColorAdaxial=PlantShapes.Srgb(.018,.021,.018), ColorAbaxial=PlantShapes.Srgb(.018,.021,.018), Thickness=.0004f, Roughness=.30f });
                    thistleBract=b.AddShape(new OrganShape { Name="blackneedle_head_bract", Type=OrganType.Leaf,
                        SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*t),.65)),
                        MidribCurl=.12f, CrossCurl=.015f, ColorAdaxial=PlantShapes.Srgb(0.42,0.381,0.164),
                        ColorAbaxial=PlantShapes.Srgb(.25,.34,.14), Thickness=.0006f, Roughness=.72f });
                    thistleFloret=b.AddShape(new OrganShape { Name="blackneedle_violet_floret", Type=OrganType.Flower,
                        SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => .35+.65*t),
                        ColorAdaxial=PlantShapes.Srgb(.52,.22,.51), ColorAbaxial=PlantShapes.Srgb(0.36,0.144,0.37), Thickness=.0004f, Roughness=.62f });
                }
                int network=crown;
                for(int n=0;n<nodes;n++)
                {
                    double a=rotation+n*2.39996;
                    var d=PlantMath.Horizontal(a);
                    // Rhizome segments form a chain for aquatic pads and spring
                    // colonies. Each node is an independent leaf or flowering shoot.
                    if(s.Id=="glasslily")
                        network=b.AddAxis(crown,1,d,r*(.15+.65*Math.Sqrt((n+.5)/nodes)),Vec3.Zero,r*.005,r*.004,
                            1,Tissue.Green,.01,.12,OrganFlags.Hidden);
                    else if(n>0)
                        network=b.AddAxis(network,1,d,r*(s.Id=="bogfringe" ? .42 : aquatic ? .21 : .17),Vec3.Zero,r*.005,r*.004,
                            1,Tissue.Green,.01,.12,OrganFlags.Hidden);
                    int node=b.AddAxis(network,1,Vec3.Up,Math.Max(.002,h*.025),Vec3.Zero,r*.01,r*.007,
                        2,Tissue.Green,.02,.12,OrganFlags.Hidden|OrganFlags.Basal);
                    if(s.Id is "glasslily" or "mirrorleaf")
                    {
                        int stalk=b.AddAxis(node,1,Vec3.Up+d*.02,h*(s.Id=="glasslily" ? .12 : b.Range(.08,.18)),Vec3.Zero,r*.006,r*.003,
                            3,Tissue.Green,.08,.24);
                        b.AddFlat(stalk,1,Vec3.Up,Math.Max(.002,h*.018),r*s.Width*(s.Id=="glasslily" ? .48 : 1),.0005,OrganType.Leaf,p.Pad,4,.2,.25);
                        if(n==1 || (s.Id=="mirrorleaf" && n==4))
                        {
                            int scape=b.AddAxis(node,1,Vec3.Up,h*(s.Id=="glasslily" ? .65 : .32),Vec3.Zero,
                                r*.006,r*.002,3,Tissue.Green,.3,.25);
                            b.AddFlat(scape,1,Vec3.Up,h*.12,r*.10,.001,OrganType.Flower,p.Flower,4,.6,.2);
                        }
                        continue;
                    }
                    if(compound)
                    {
                        int shoots=s.Id=="bogfringe" ? 2 : 3;
                        for(int j=0;j<shoots;j++)
                        {
                            double az=a+j*Math.Tau/shoots; var ray=PlantMath.Horizontal(az);
                            int stalk=b.AddAxis(node,1,Vec3.Up*(s.Id=="bogfringe" ? .35 : .8)+ray*.5,
                                h*(s.Id=="bogfringe" ? .24 : .3),Vec3.Zero,r*.005,r*.002,3,Tissue.Green,.08,.2);
                            for(int k=-1;k<=1;k++)
                                b.AddFlat(stalk,1,PlantMath.Horizontal(az+k*.9)+Vec3.Up*(s.Id=="bogfringe" ? b.Range(.15,.35) : .12),r*(s.Id=="bogfringe" ? .23 : .18),r*s.Width*.55,
                                    .0005,OrganType.Leaflet,p.Leaf,4,.2,.2);
                        }
                        if(n%2==0 && s.Id!="bogfringe")
                        {
                            int scape=b.AddAxis(node,1,Vec3.Up+d*.2,h*.62,d*.1,r*.004,r*.001,3,Tissue.Green,.3,.25);
                            b.AddFlat(scape,1,Vec3.Up,h*.08,r*.08,.001,OrganType.Flower,p.Flower,4,.6,.2);
                        }
                        if(s.Id=="bogfringe" && n==2)
                        {
                            int scape=b.AddAxis(node,1,Vec3.Up,h*.32,Vec3.Zero,r*.004,r*.001,3,Tissue.Green,.3,.25);
                            b.AddFlat(scape,1,Vec3.Up,h*.08,r*.08,.001,OrganType.Flower,p.Flower,4,.6,.2);
                        }
                        continue;
                    }
                    if(s.Id=="tidebell")
                    {
                        // Leaf-bearing rachises remain rooted while independent flower scapes
                        // extend to the water surface in the live presentation pose.
                        for(int j=0;j<4;j++)
                        {
                            double az=a+j*Math.Tau/4+b.Range(-.2,.2);
                            var ray=PlantMath.Horizontal(az);
                            var side=PlantMath.Horizontal(az+Math.PI/2);
                            int rachis=b.AddAxis(node,1,ray+Vec3.Up*b.Range(.35,.7),r*b.Range(.32,.48),
                                Vec3.Up*.12,r*.004,r*.001,3,Tissue.Green,.04,.25);
                            for(int k=0;k<4;k++) for(int sign=-1;sign<=1;sign+=2)
                                b.AddFlat(rachis,.18+k*.20,side*sign+ray*.3+Vec3.Up*.12,
                                    r*.13,r*.038,.0005,OrganType.Leaflet,p.Leaf,4,.12+k*.025,.2);
                            b.AddFlat(rachis,1,ray+Vec3.Up*.2,r*.14,r*.04,.0005,OrganType.Leaflet,p.Leaf,4,.22,.2);
                        }
                        for(int j=0;j<2;j++)
                        {
                            var ray=PlantMath.Horizontal(a+j*Math.PI);
                            // Thick scape arcs outward and over so the bell nods from a curved pedicel.
                            var arc=PlantMath.Horizontal(a+j*Math.PI+b.Range(-.5,.5));
                            int scape=b.AddAxis(node,1,Vec3.Up+arc*.22,h*b.Range(.42,.60),arc*b.Range(.95,1.35)-Vec3.Up*.35,
                                r*.0085,r*.0048,3,Tissue.Green,.16,.30);
                            b.AddFlat(scape,1,b.TangentAt(scape,1)*.5+Vec3.Down*.85+arc*.2,h*.12,r*b.Range(.10,.125),.0005,
                                OrganType.Flower,tube,4,.40,.25);
                        }
                        continue;
                    }
                    if(s.Id is "marshkettle" or "mistviolet")
                    {
                        bool violet=s.Id=="mistviolet";
                        ushort roundLeaf=b.AddShape(new OrganShape { Name=s.Id+"_cluster_leaf", Type=OrganType.Leaf,
                            SurfaceForm=OrganSurfaceForm.Lamina,
                            WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*t),.55)),
                            MidribCurl=.05f, CrossCurl=.015f, Wax=violet ? .15f : .55f,
                            ColorAdaxial=PlantShapes.Srgb(.22,.43,.20), ColorAbaxial=PlantShapes.Srgb(.32,.46,.25), Thickness=.0006f, Roughness=LeafRough(s.Id) });
                        for(int j=0;j<7;j++)
                        {
                            double az=a+j*2.39996+b.Range(-.12,.12);
                            var ray=PlantMath.Horizontal(az);
                            int stalk=b.AddAxis(node,1,ray+Vec3.Up*b.Range(.6,1.05),r*b.Range(.12,.27),
                                Vec3.Up*.08,r*.009,r*.003,3,Tissue.Green,.04,.25);
                            b.AddFlat(stalk,1,ray+Vec3.Up*b.Range(.15,.4),h*b.Range(violet ? .18 : .22,violet ? .27 : .32),
                                h*b.Range(violet ? .17 : .21,violet ? .27 : .31),.0006,OrganType.Leaf,roundLeaf,4,.2,.25);
                        }
                        for(int j=0;j<2;j++)
                        {
                            var ray=PlantMath.Horizontal(a+j*Math.PI+b.Range(-.25,.25));
                            int scape=b.AddAxis(node,1,Vec3.Up+ray*(violet ? .42 : .22),h*b.Range(.40,.56),
                                ray*.12-Vec3.Up*(violet ? .15 : .03),r*.005,r*.001,3,Tissue.Green,.3,.25);
                            b.AddFlat(scape,1,Vec3.Up+ray*(violet ? .45 : .15),h*.10,h*(violet ? .11 : .15),
                                .001,OrganType.Flower,p.Flower,4,.6,.2);
                        }
                        continue;
                    }
                    ushort burlLeaf=p.Leaf;
                    if(s.Id=="burlmantle")
                        burlLeaf=b.AddShape(new OrganShape { Name="burlmantle_spear_frond", Type=OrganType.Leaf,
                            SurfaceForm=OrganSurfaceForm.Lamina,
                            WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Max(0,Math.Sin(Math.PI*t)),.85)*(1.12-.42*t),17),
                            MidribCurl=.10f, CrossCurl=.018f, EdgeWave=.025f, EdgeWaveFreq=3f,
                            ColorAdaxial=PlantShapes.Srgb(.22,.43,.20), ColorAbaxial=PlantShapes.Srgb(.32,.46,.25), Thickness=.0006f, Roughness=LeafRough("burlmantle")});
                    int leafCount=big ? (s.Id=="blackneedle" ? 12 : 9) : s.Id=="tankbloom" ? 14 : s.Id=="starbulb" ? 9 : 7;
                    for(int j=0;j<leafCount;j++)
                    {
                        var ray=PlantMath.Horizontal(a+j*Math.Tau/leafCount);
                        double rise=s.Id=="tankbloom" ? .9 : s.Id=="starbulb" ? .24 : s.Id=="burlmantle" ? .32+(j%3)*.16 : .25;
                        int stalk=b.AddAxis(node,1,ray+Vec3.Up*rise,r*(s.Id=="burlmantle" ? .22 : .12),Vec3.Zero,r*.009,r*.003,3,Tissue.Green,.04,.2);
                        b.AddFlat(stalk,1,ray+Vec3.Up*rise,r*(s.Id=="burlmantle" ? .86 : big ? .52 : .45),r*s.Width*(s.Id=="burlmantle" ? .54 : s.Id=="starbulb" ? .65 : 1),.0006,
                            OrganType.Leaf,s.Id=="burlmantle" ? burlLeaf : p.Leaf,4,.14,.28);
                        if(s.Id=="blackneedle")
                        {
                            // Separate dark tapered blades sit at the tips of each leaf lobe.
                            var side=PlantMath.Horizontal(a+j*Math.Tau/leafCount+Math.PI/2);
                            int mid=b.AddAxis(stalk,1,ray+Vec3.Up*rise,r*.52,Vec3.Zero,r*.001,r*.0005,
                                4,Tissue.Green,.14,.28,OrganFlags.Hidden);
                            for(int k=1;k<=4;k++) for(int sign=-1;sign<=1;sign+=2)
                            {
                                double t=k/5.0, width=r*s.Width*LeafWidth("blackneedle",t,false)*.75;
                                int edge=b.AddAxis(mid,t,side*sign,width,Vec3.Zero,r*.0005,r*.0003,
                                    5,Tissue.Green,.18,.2,OrganFlags.Hidden);
                                b.AddFlat(edge,1,side*sign+ray*.45+Vec3.Up*.12,r*.055,r*.006,.0004,
                                    OrganType.Leaf,blackSpine,6,.2,.2);
                            }
                            b.AddFlat(mid,1,ray+Vec3.Up*.15,r*.065,r*.007,.0004,OrganType.Leaf,blackSpine,5,.2,.2);
                        }
                    }
                    if(s.Id=="prismstar")
                    {
                        int scape=b.AddAxis(node,1,Vec3.Up,h*.65,Vec3.Zero,r*.004,r*.001,3,Tissue.Green,.3,.25);
                        b.AddFlat(scape,1,Vec3.Up,h*.06,r*.05,.001,OrganType.Flower,p.Flower,4,.6,.2);
                    }
                    else if(central || big)
                    {
                        // Starbulb's flower stem takes a gentle natural arc; the head stays on top.
                        var lean=PlantMath.Horizontal(a+b.Range(0,Math.Tau));
                        int scape=s.Id=="starbulb"
                            ? b.AddAxis(node,1,Vec3.Up+lean*.10,h*.9,lean*.42+Vec3.Up*.05,r*.009,r*.003,3,Tissue.Green,.3,.3)
                            : b.AddAxis(node,1,Vec3.Up,h*.9,Vec3.Zero,r*.009,r*.003,3,Tissue.Green,.3,.3);
                        if(s.Id=="blackneedle")
                        {
                            // Two heads, each built from overlapping bract whorls and a violet brush.
                            for(int head=0;head<2;head++)
                            {
                                var hd=PlantMath.Horizontal(a+head*Math.PI);
                                int ped=b.AddAxis(scape,head==0 ? .82 : 1,Vec3.Up+hd*(head==0 ? .5 : .12),
                                    h*(head==0 ? .13 : .08),Vec3.Zero,r*.004,r*.002,4,Tissue.Green,.48,.2);
                                for(int w=0;w<4;w++) for(int k=0;k<8+w*2;k++)
                                {
                                    double az=a+k*Math.Tau/(8+w*2)+w*.41;
                                    var ray=PlantMath.Horizontal(az);
                                    double len=r*(.10+w*.018);
                                    b.AddFlat(ped,.15+w*.12,ray+Vec3.Up*(.25+w*.4),len,r*.035,.0006,
                                        OrganType.Leaf,thistleBract,5,.52,.2);
                                }
                                for(int k=0;k<24;k++)
                                {
                                    double az=k*2.39996;
                                    var ray=PlantMath.Horizontal(az);
                                    int baselet=b.AddAxis(ped,.58,ray,r*.035*Math.Sqrt((k+.5)/24),Vec3.Zero,
                                        r*.0005,r*.0003,5,Tissue.Green,.55,.15,OrganFlags.Hidden);
                                    b.AddFlat(baselet,1,Vec3.Up+ray*.2,r*.07,r*.008,.0004,
                                        OrganType.Flower,thistleFloret,6,.6,.2);
                                }
                            }
                        }
                        else if(s.Id=="starbulb")
                        {
                            var colors = new[] { PlantShapes.Srgb(.48,.68,.96), PlantShapes.Srgb(.83,.57,.91),
                                PlantShapes.Srgb(.96,.81,.48), PlantShapes.Srgb(.92,.68,.74), PlantShapes.Srgb(.88,.94,1.0) };
                            var blooms = new ushort[colors.Length];
                            for(int c=0;c<colors.Length;c++) blooms[c]=b.AddShape(new OrganShape {
                                Name="starbulb_bloom_"+c, Type=OrganType.Flower, SurfaceForm=OrganSurfaceForm.Star,
                                WidthProfile=PlantShapes.Profile(t=>.5*Math.Sin(Math.PI*t)), LobeCount=6, LobeDepth=.48f,
                                ColorAdaxial=colors[c], ColorAbaxial=colors[c]*.8, Roughness=.55f });
                            const int flowers=112;
                            for(int k=0;k<flowers;k++)
                            {
                                double y=1-2*(k+.5)/flowers, az=k*2.39996;
                                var ray=PlantMath.Horizontal(az)*Math.Sqrt(1-y*y)+Vec3.Up*y;
                                int ped=b.AddAxis(scape,1,ray,r*b.Range(.19,.25),Vec3.Zero,r*.0016,r*.0006,4,Tissue.Green,.55,.2);
                                b.AddFlat(ped,1,ray,h*.045,r*.038,.001,OrganType.Flower,blooms[k%blooms.Length],5,.7,.2);
                            }
                        }
                        else if(s.Id=="tankbloom")
                        {
                            int spike=b.AddAxis(scape,1,Vec3.Up,h*.28,Vec3.Zero,r*.01,r*.003,4,Tissue.Green,.5,.3);
                            for(int w=0;w<7;w++) for(int k=0;k<4;k++)
                            {
                                double t=.04+w*.14, az=a+k*Math.Tau/4+w*1.1;
                                var ray=PlantMath.Horizontal(az)+Vec3.Up*(.7+t*.5);
                                b.AddFlat(spike,t,ray,h*(.085-.04*t),r*(.075-.035*t),.001,
                                    OrganType.Flower,p.Scarlet,5,.55,.2);
                                if(k==0) b.AddFlat(spike,t,ray,h*.025,r*.025,.001,OrganType.Flower,p.Pale,5,.7,.15);
                            }
                        }
                        else b.AddFlat(scape,1,Vec3.Up,h*.12,r*.1,.001,
                            s.Id=="burlmantle" ? OrganType.Fruit : OrganType.Flower,
                            s.Id=="burlmantle" ? p.Burr : p.Flower,4,.6,.2);
                    }
                }
                return true;
            }

            if(runner)
            {
                // Each runner is a continuous, segmented ground-level axis. Its nodes
                // support leaves or ramets; they are not independent radial shoots.
                int paths=s.Id=="glassfinger" ? 10 : s.Id=="coinrunner" ? 4 : s.Id=="brooklace" ? 3 : 2;
                ushort succulent=s.Id=="glassfinger" ? b.AddShape(new OrganShape { Name="glassfinger_fleshy_finger", Type=OrganType.Leaf,
                    SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Sin(Math.PI*t),.65)*(1-.35*t)),
                    MidribCurl=.18f, ColorAdaxial=PlantShapes.Srgb(.52,.68,.65), ColorAbaxial=PlantShapes.Srgb(.37,.55,.51),
                    Roughness=.36f, Transmission=.25f }) : p.Leaf;
                int segments=s.Id is "brooklace" or "glassfinger" ? 6 : s.Id=="mooncoin" ? 5 : 4;
                for(int path=0;path<paths;path++)
                {
                    double a=rotation+path*Math.Tau/paths+b.Range(-.2,.2);
                    int previous=crown;
                    for(int j=0;j<segments;j++)
                    {
                        a+=b.Range(-.32,.32);
                        var forward=PlantMath.Horizontal(a);
                        var side=PlantMath.Horizontal(a+Math.PI/2);
                        int stem=b.AddAxis(previous,1,forward+Vec3.Up*.015,r*(s.Id=="brooklace" ? .14 : .19),
                            side*b.Range(-.18,.18),r*.009,r*.007,1,Tissue.Green,.02+j*.035,.16);
                        if(s.Id is "cinderberry" or "trifold")
                        {
                            // A rooted node carries a compound leaf crown, not a
                            // leaf pinned directly to the length of the stolon.
                            int stalk=b.AddAxis(stem,.92,Vec3.Up+side*.16,h*(s.Id=="cinderberry" ? .30 : .52),
                                Vec3.Zero,r*.005,r*.002,2,Tissue.Green,.12,.2);
                            for(int k=-1;k<=1;k++)
                                b.AddFlat(stalk,1,PlantMath.Horizontal(a+k*.85)+Vec3.Up*.16,r*.18,r*s.Width*.55,
                                    .0005,OrganType.Leaflet,p.Leaf,3,.25,.2);
                            if(s.Id=="cinderberry" && j%2==1)
                            {
                                int ped=b.AddAxis(stem,.85,side+Vec3.Up*.45,h*.20,Vec3.Down*.1,r*.003,r*.001,2,Tissue.Green,.4,.2);
                                b.AddFlat(ped,1,Vec3.Down,h*.12,r*.045,.001,OrganType.Fruit,p.Berry,3,.6,.2);
                            }
                        }
                        else
                        {
                            int pairs=s.Id=="brooklace" ? 2 : 1;
                            for(int n=0;n<pairs;n++) for(int sign=-1;sign<=1;sign+=2)
                            {
                                double at=pairs==1 ? .8 : .35+n*.5;
                                var d=(s.Id=="glassfinger" && j%2==1 ? forward*sign : side*sign)+forward*.35+Vec3.Up*(s.Id=="glassfinger" ? .32 : .08);
                                double length=r*(s.Id=="brooklace" ? .10 : s.Id=="glassfinger" ? .18 : .20);
                                b.AddFlat(stem,at,d,length,r*s.Width*(s.Id=="glassfinger" ? .25 : s.Id=="brooklace" ? .32 : .65),.0005,
                                    OrganType.Leaf,succulent,2,.12+j*.03,.18);
                            }
                        }
                        if(s.Id=="coinrunner" && j==1)
                        {
                            double branchAngle=a+(path%2==0 ? .85 : -.85);
                            var branchForward=PlantMath.Horizontal(branchAngle);
                            var branchSide=PlantMath.Horizontal(branchAngle+Math.PI/2);
                            int branch=stem;
                            for(int k=0;k<2;k++)
                            {
                                branch=b.AddAxis(branch,1,branchForward+Vec3.Up*.015,r*.17,
                                    branchSide*.08,r*.007,r*.005,2,Tissue.Green,.12+k*.04,.16);
                                for(int sign=-1;sign<=1;sign+=2)
                                    b.AddFlat(branch,.8,branchSide*sign+branchForward*.18+Vec3.Up*.08,
                                        r*.20,r*s.Width*.65,.0005,OrganType.Leaf,p.Leaf,3,.2+k*.03,.18);
                            }
                        }
                        previous=stem;
                    }
                }
                return true;
            }

            if(s.Id is "brightknot" or "cragset" or "siltstars" or "sunstone_rosette")
            {
                bool bright=s.Id=="brightknot", crag=s.Id=="cragset", silt=s.Id=="siltstars";
                // Brightknot's plump leaves are solids, rather than flat blades.
                ushort fleshy=0;
                if(bright)
                    fleshy=b.AddShape(new OrganShape { Name=s.Id+"_fleshy_leaf", Type=OrganType.Leaf,
                        SurfaceForm=OrganSurfaceForm.Revolved,
                        WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Sin(Math.PI*t),.65)),
                        ColorAdaxial=PlantShapes.Srgb(.47,.58,.28), ColorAbaxial=PlantShapes.Srgb(.32,.46,.25), Roughness=LeafRough(s.Id) });
                int count=bright ? 11 : crag ? 3 : silt ? 9 : 5;
                var nodes=new int[count];
                nodes[0]=crown;
                for(int n=0;n<count;n++)
                {
                    double angle=rotation+n*2.39996;
                    var outward=PlantMath.Horizontal(angle);
                    if(n>0)
                    {
                        // A branching network retains separate rooted centers and the mat's breadth.
                        int parent=bright || silt ? nodes[(n-1)/2] : crown;
                        double spacing=bright ? .25 : silt ? .30 : crag ? .31 : .42;
                        int connector=b.AddAxis(parent,1,outward,r*spacing,Vec3.Zero,r*.005,r*.003,
                            1,Tissue.Green,.01,.12,OrganFlags.Hidden);
                        nodes[n]=b.AddAxis(connector,1,Vec3.Up,Math.Max(.002,h*.025),Vec3.Zero,
                            r*.008,r*.006,2,Tissue.Green,.04,.12,OrganFlags.Hidden|OrganFlags.Basal);
                    }
                    int node=nodes[n];
                    if(bright)
                    {
                        // Short ascending shoots with alternating fleshy leaves make a branching mat.
                        for(int shoot=0;shoot<3;shoot++)
                        {
                            double az=angle+shoot*Math.Tau/3;
                            var ray=PlantMath.Horizontal(az);
                            int stem=b.AddAxis(node,1,ray*.45+Vec3.Up,r*b.Range(.10,.18),ray*.12,
                                r*.012,r*.006,3,Tissue.Green,.04,.2);
                            for(int k=0;k<5;k++)
                            {
                                var leafDir=PlantMath.Horizontal(az+k*2.39996)*.7+Vec3.Up*(.4+k*.12);
                                b.AddFlat(stem,.15+k*.19,leafDir,r*b.Range(.105,.15),r*.055,.001,
                                    OrganType.Leaf,fleshy,4,.1+k*.02,.2);
                            }
                        }
                    }
                    else if(silt)
                    {
                        // Distinct narrow star nodes spread across the substrate.
                        for(int i=0;i<7;i++)
                        {
                            var ray=PlantMath.Horizontal(angle+i*Math.Tau/7+b.Range(-.08,.08));
                            b.AddFlat(node,1,ray+Vec3.Up*b.Range(.22,.55),r*b.Range(.19,.27),r*.025,.0005,
                                OrganType.Leaf,p.Leaf,3,.1+i*.01,.24);
                        }
                    }
                    else
                    {
                        // The mother sunstone is substantially larger than its pups; cragset stays compact.
                        double scale=crag ? (n==0 ? 1.0 : .68) : (n==0 ? 1.0 : .48);
                        int layers=crag ? 3 : 3;
                        for(int layer=0;layer<layers;layer++)
                        {
                            int leaves=crag ? 9-layer : 11-layer*2;
                            double len=r*scale*(crag ? .43-layer*.065 : .55-layer*.085);
                            double rise=.22+layer*.38;
                            int center=b.AddAxis(node,1,Vec3.Up,Math.Max(.002,h*(.025+layer*.055)),Vec3.Zero,
                                r*.006,r*.004,3,Tissue.Green,.04,.12,OrganFlags.Hidden);
                            for(int i=0;i<leaves;i++)
                            {
                                double az=angle+(i+.5*(layer%2))*Math.Tau/leaves+b.Range(-.06,.06);
                                var ray=PlantMath.Horizontal(az);
                                b.AddFlat(center,1,ray+Vec3.Up*rise,len*b.Range(.9,1.08),
                                    r*s.Width*scale*(crag ? .85 : 1.0),.001,OrganType.Leaf,p.Leaf,4,.1+layer*.04,.24);
                            }
                        }
                        if(!crag && n==0)
                        {
                            int scape=b.AddAxis(node,1,Vec3.Up,h*.75,Vec3.Up*.05,r*.006,r*.002,
                                3,Tissue.Green,.35,.25);
                            for(int k=0;k<5;k++)
                            {
                                var ray=PlantMath.Horizontal(angle+k*Math.Tau/5);
                                int ped=b.AddAxis(scape,1,ray+Vec3.Up*.2,r*.08,Vec3.Zero,r*.002,r*.001,
                                    4,Tissue.Green,.55,.2);
                                b.AddFlat(ped,1,Vec3.Up+ray*.3,h*.055,r*.04,.001,OrganType.Flower,p.Flower,5,.7,.2);
                            }
                        }
                    }
                }
                return true;
            }

            // Compact rosettes repeat at distinct offsets along a concealed
            // substrate connector. Each ramet has its own local leaf center.
            int ramets=s.Id switch { "brightknot" => 7, "pondstars" => 6, "siltstars" => 7,
                "blue_sundew" => 4, "cragset" => 3, _ => 2 };
            int chain=crown;
            for(int n=0;n<ramets;n++)
            {
                double angle=rotation+n*2.39996;
                var outward=PlantMath.Horizontal(angle);
                if(n>0)
                    chain=b.AddAxis(chain,1,outward,r*(s.Id=="siltstars" ? .16 : .21),Vec3.Zero,
                        r*.004,r*.003,1,Tissue.Green,.01,.12,OrganFlags.Hidden);
                int node=b.AddAxis(chain,1,Vec3.Up,Math.Max(.002,h*.025),Vec3.Zero,r*.008,r*.006,
                    2,Tissue.Green,.04,.12,OrganFlags.Hidden|OrganFlags.Basal);
                int leaves=s.Id switch { "brightknot" => 9, "siltstars" => 9, "pondstars" => 8,
                    "blue_sundew" => 8, "cragset" => 11, _ => 14 };
                for(int i=0;i<leaves;i++)
                {
                    double a=angle+i*Math.Tau/leaves;
                    var d=PlantMath.Horizontal(a);
                    double rise=s.Id is "cragset" or "sunstone_rosette" ? .45 : .08;
                    double len=r*(s.Id=="siltstars" ? .13 : s.Id=="pondstars" ? .16 : .23);
                    b.AddFlat(node,1,d+Vec3.Up*rise,len,r*s.Width*(s.Id=="brightknot" ? .45 : .65),
                        .0006,OrganType.Leaf,p.Leaf,3,.1+i*.005,.24);
                    if(s.Id=="blue_sundew" && i%2==0)
                    {
                        int st=b.AddAxis(node,1,d*.35+Vec3.Up,h*.35,d*.08,r*.003,r*.001,3,Tissue.Green,.3,.2);
                        b.AddFlat(st,1,Vec3.Up,h*.22,r*.035,.001,OrganType.Flower,p.Club,4,.5,.2);
                    }
                }
            }
            return true;
        }
    }
}
