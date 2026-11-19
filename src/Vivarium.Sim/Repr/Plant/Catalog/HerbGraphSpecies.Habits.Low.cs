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
                ushort[] tubes=Array.Empty<ushort>();
                if(s.Id=="tidebell")
                {
                    // Three rim/age variants of the luminous bell; the shared prefix keeps
                    // the emission marker, water pose and self-test matching all of them.
                    tubes=new ushort[3];
                    for(int v=0;v<3;v++)
                        tubes[v]=b.AddShape(new OrganShape { Name=v==0?"tidebell_luminous_bell":"tidebell_luminous_bell_"+v, Type=OrganType.Flower,
                            SurfaceForm=OrganSurfaceForm.Cup,
                            // Narrow throat, belly swell, then a flared lip that rolls back outward.
                            WidthProfile=PlantShapes.Profile(t => .07+.30*Math.Pow(t,.8)+.17*Math.Pow(Math.Max(0,(t-.62)/.38),1.6)),
                            ShellThickness=.012f, RimCurl=.50f-.14f*v, LobeCount=5, LobeDepth=.16f+.05f*v,
                            RadialAsymmetry=.20f, EdgeWave=.07f+.03f*v, EdgeWaveFreq=5, ColorAdaxial=PlantShapes.Srgb(.92,.76,.46),
                            ColorAbaxial=PlantShapes.Srgb(0.68,0.468,0.256), Roughness=.42f });
                    tube=tubes[0];
                }
                ushort thistleBract=0, thistleFloret=0;
                if(s.Id=="blackneedle")
                {
                    thistleBract=b.AddShape(new OrganShape { Name="blackneedle_head_bract", Type=OrganType.Flower,
                        SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*t),.65)),
                        MidribCurl=.12f, CrossCurl=.015f, ColorAdaxial=PlantShapes.Srgb(0.42,0.381,0.164),
                        ColorAbaxial=PlantShapes.Srgb(.25,.34,.14), Thickness=.0006f, Roughness=.72f });
                    thistleFloret=b.AddShape(new OrganShape { Name="blackneedle_violet_floret", Type=OrganType.Flower,
                        SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => .35+.65*t),
                        ColorAdaxial=PlantShapes.Srgb(.52,.22,.51), ColorAbaxial=PlantShapes.Srgb(0.36,0.144,0.37), Thickness=.0004f, Roughness=.62f });
                }
                int network=crown;
                ushort mirrorPad=0;
                if(s.Id=="mirrorleaf")
                    mirrorPad=b.AddShape(new OrganShape { Name="mirrorleaf_undulate_pad", Type=OrganType.Leaf,
                        SurfaceForm=OrganSurfaceForm.Disc, WidthProfile=PlantShapes.Profile(t => .5*Math.Sin(Math.PI*t)),
                        LobeCount=9, LobeDepth=.075f, RadialAsymmetry=.16f, Wax=.55f,
                        ColorAdaxial=PlantShapes.Srgb(.19,.38,.20), ColorAbaxial=PlantShapes.Srgb(.31,.31,.17), Roughness=.38f });
                if(nodes==1)
                {
                    b[crown].P1=Vec3.Up*Math.Clamp(h*.018,.004,.014);
                    b[crown].R0=r*.042; b[crown].R1=r*.028;
                }
                for(int n=0;n<nodes;n++)
                {
                    double a=rotation+n*2.39996;
                    var d=PlantMath.Horizontal(a);
                    // Rhizome segments form a chain for aquatic pads and spring
                    // colonies. Each node is an independent leaf or flowering shoot.
                    if(s.Id=="glasslily")
                        network=n==0
                            ? b.AddAxis(crown,0,d,r*b.Range(.20,.30),PlantMath.Horizontal(a+Math.PI/2)*b.Range(-.3,.3),r*.006,r*.005,
                                1,Tissue.Green,.01,.12,OrganFlags.Hidden)
                            : b.AddAxis(network,1,d,r*b.Range(.20,.32),PlantMath.Horizontal(a+Math.PI/2)*b.Range(-.5,.5),r*.005,r*.004,
                                1,Tissue.Green,.01,.12,OrganFlags.Hidden);
                    else if(n>0)
                        network=b.AddAxis(network,network==crown ? 0 : 1,d,r*(s.Id=="bogfringe" ? .42 : aquatic ? .21 : .17)*b.Range(.75,1.22),PlantMath.Horizontal(a+Math.PI/2)*b.Range(-.25,.25),r*.005,r*.004,
                            1,Tissue.Green,.01,.12,OrganFlags.Hidden);
                    // Single-node rosettes attach directly to the seated basal crown; multi-node
                    // non-aquatic colonies expose low grounded shoot crowns (except tidebell whose
                    // node stays Hidden so TidebellWaterPose stretches only the flower petioles).
                    double nodeR=s.Id=="marshkettle" ? .022 : s.Id=="tidebell" ? .022 : .015;
                    int node=nodes==1 ? crown : b.AddAxis(network,network==crown ? 0 : 1,Vec3.Up,
                        s.Id is "glasslily" or "mirrorleaf" ? Math.Max(.002,h*b.Range(.012,.032))
                        : s.Id=="bogfringe" ? Math.Max(.002,h*.014)
                        : s.Id=="tidebell" ? Math.Clamp(h*.014,.003,.008)
                        : Math.Clamp(h*.016,.003,.010),
                        Vec3.Zero,r*nodeR,r*nodeR*.75,
                        2,Tissue.Green,.02,.12,(aquatic || s.Id is "bogfringe" or "tidebell" ? OrganFlags.Hidden : OrganFlags.None)|OrganFlags.Basal);
                    if(s.Id is "glasslily" or "mirrorleaf")
                    {
                        int stalk=b.AddAxis(node,1,Vec3.Up+d*.02,h*(s.Id=="glasslily" ? .12 : b.Range(.08,.18)),Vec3.Zero,r*.006,r*.003,
                            3,Tissue.Green,.08,.24);
                        // Pads sit on a wandering running axis at unequal sizes and tilts;
                        // the oldest blades are the largest, later ramets smaller and higher.
                        double padScale=(s.Id=="glasslily" ? b.Range(.66,1.30) : 1.05-.075*n)*b.Range(.92,1.08);
                        var padDir=PlantMath.SafeNormal(Vec3.Up+d*b.Range(.06,.22),Vec3.Up);
                        b.AddFlat(stalk,1,padDir,Math.Max(.0015,h*(s.Id=="mirrorleaf" ? .010 : .018))*b.Range(.82,1.18),r*s.Width*(s.Id=="glasslily" ? .48 : 1)*padScale,.0004,
                            OrganType.Leaf,s.Id=="mirrorleaf" ? mirrorPad : p.Pad,4,.2,.25);
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
                        // Inner nodes carry taller crowns; peripheral nodes sit lower, so the
                        // canopy reads as an uneven mound rather than a level tabletop.
                        double nodeScale=s.Id=="hushclover" ? 1.10-.11*n : s.Id=="starveil" ? .85+.32*b.Range(.8,1.2) : 1.0;
                        for(int j=0;j<shoots;j++)
                        {
                            double az=a+j*Math.Tau/shoots+b.Range(-.35,.35); var ray=PlantMath.Horizontal(az);
                            double lift=s.Id=="bogfringe" ? .35 : b.Range(.45,1.12);
                            int stalk=b.AddAxis(node,s.Id=="bogfringe" ? 1 : .55+.40*(j/(double)shoots),Vec3.Up*lift+ray*.5,
                                h*(s.Id=="bogfringe" ? .16 : .3)*b.Range(.72,1.25)*nodeScale,ray*b.Range(-.2,.35),r*.005,r*.002,3,Tissue.Green,.08,.2);
                            for(int k=-1;k<=1;k++)
                                b.AddFlat(stalk,1,PlantMath.Horizontal(az+k*.9+(s.Id=="bogfringe" ? 0 : b.Range(-.2,.2)))+Vec3.Up*(s.Id=="bogfringe" ? b.Range(.15,.35) : b.Range(.06,.30)),
                                    r*(s.Id=="bogfringe" ? .23 : .18)*(s.Id=="bogfringe" ? 1 : b.Range(.82,1.18)),r*s.Width*.55,
                                    .0005,OrganType.Leaflet,p.Leaf,4,.2,.2);
                        }
                        if(n%2==0 && s.Id!="bogfringe")
                        {
                            int scape=b.AddAxis(node,.92,Vec3.Up+d*b.Range(.1,.4),h*b.Range(.38,.86),d*b.Range(-.25,.4),r*.007,r*.0025,3,Tissue.Green,.3,.25);
                            b.AddFlat(scape,1,Vec3.Up+d*.15,h*.08,r*.08,.001,OrganType.Flower,p.Flower,4,.6,.2);
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
                        // Unequal, curved petioles: a rising lower stem plus an arched continuous
                        // pedicel forms a smooth shepherd's crook without a hairpin V-kink.
                        int bells=n%2==0 ? 3 : 2;
                        for(int j=0;j<bells;j++)
                        {
                            double spread=a+j*Math.Tau/bells+b.Range(-.3,.3);
                            var arc=PlantMath.Horizontal(spread);
                            double stemLen=h*b.Range(.18,.30);
                            int stem=b.AddAxis(node,.75,Vec3.Up+arc*.22,stemLen,arc*.55,
                                r*.0125,r*.0075,3,Tissue.Green,.14,.24,OrganFlags.Continuous);
                            b[stem].T0=PlantMath.SafeNormal(Vec3.Up*1.1+arc*.22,Vec3.Up)*stemLen;
                            b[stem].T1=PlantMath.SafeNormal(Vec3.Up*.65+arc*.75,Vec3.Up)*stemLen;
                            double scapeLen=h*b.Range(.18,.34);
                            int scape=b.AddAxis(stem,1,b.TangentAt(stem,1),scapeLen,
                                arc*.70+Vec3.Down*b.Range(.65,.95),r*.0075,r*.0045,4,Tissue.Green,.24,.26,OrganFlags.Continuous);
                            b[scape].P0=b[stem].P1;
                            b[scape].R0=b[stem].R1;
                            b[scape].T0=PlantMath.SafeNormal(b[stem].T1,Vec3.Up)*scapeLen;
                            b[scape].T1=PlantMath.SafeNormal(arc*.85+Vec3.Down*b.Range(.55,.85),arc)*scapeLen;
                            b[scape].P1=b[scape].P0+(b[scape].T0+b[scape].T1)*.5;
                            double age=b.Range(.72,1.0);
                            var side=PlantMath.Horizontal(spread+Math.PI/2);
                            b.AddFlat(scape,1,
                                b.TangentAt(scape,1)*.45+Vec3.Down*.85+arc*b.Range(-.1,.35)+side*b.Range(-.22,.22),
                                h*.12*age,r*b.Range(.095,.130)*age,.0005,OrganType.Flower,tubes.Length>0?tubes[(n+j)%3]:tube,4,.40,.25);
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
                            int stalk=b.AddAxis(node,.30+.60*(j/6.0),ray+Vec3.Up*b.Range(violet ? .6 : .30,1.05),r*b.Range(violet ? .12 : .07,violet ? .27 : .36),
                                Vec3.Up*.08,r*.009,r*.003,3,Tissue.Green,.04,.25);
                            b.AddFlat(stalk,1,ray+Vec3.Up*b.Range(violet ? .15 : .02,.4),h*b.Range(violet ? .18 : .16,violet ? .27 : .32),
                                h*b.Range(violet ? .17 : .17,violet ? .27 : .31),.0006,OrganType.Leaf,roundLeaf,4,.2,.25);
                        }
                        for(int j=0;j<2;j++)
                        {
                            var ray=PlantMath.Horizontal(a+j*Math.PI+b.Range(-.25,.25));
                            int scape=b.AddAxis(node,.88,Vec3.Up+ray*(violet ? .42 : .22),h*b.Range(.33,.62),
                                ray*.12-Vec3.Up*(violet ? .15 : .03),r*.005,r*.001,3,Tissue.Green,.3,.25);
                            b.AddFlat(scape,1,Vec3.Up+ray*(violet ? .45 : .15),h*b.Range(.075,.115),h*(violet ? .11 : .15)*b.Range(.8,1.2),
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
                    double blackPmax=0;
                    if(s.Id=="blackneedle")
                        for(int sIdx=0;sIdx<=16;sIdx++) blackPmax=Math.Max(blackPmax,LeafWidth("blackneedle",sIdx/16.0,false));
                    for(int j=0;j<leafCount;j++)
                    {
                        double leafAz=a+j*Math.Tau/leafCount+b.Range(-.22,.22)+(s.Id=="burlmantle" ? .34*Math.Sin(j*2.3) : 0);
                        var ray=PlantMath.Horizontal(leafAz);
                        double rise=s.Id=="tankbloom" ? b.Range(.55,1.05) : s.Id=="starbulb" ? .24 : s.Id=="burlmantle" ? (.32+(j%3)*.16)*b.Range(.9,1.1) : b.Range(.18,.32);
                        double attachFrac=nodes==1 ? .25+.65*(j/Math.Max(1.0,leafCount-1.0)) : .35+.55*(j/Math.Max(1.0,leafCount-1.0));
                        int stalk=b.AddAxis(node,attachFrac,ray+Vec3.Up*rise,r*(s.Id=="burlmantle" ? .22 : s.Id=="blackneedle" ? .038 : .12),Vec3.Zero,s.Id=="blackneedle" ? r*.013 : r*.009,s.Id=="blackneedle" ? r*.007 : r*.003,3,Tissue.Green,.04,.2);
                        double leafLen=r*(s.Id=="burlmantle" ? .86 : s.Id=="blackneedle" ? .58 : big ? .52 : .45)*b.Range(.76,1.16);
                        int leafFlat=b.AddFlat(stalk,1,ray+Vec3.Up*rise,leafLen,r*s.Width*(s.Id=="burlmantle" ? .54 : s.Id=="starbulb" ? .65 : 1),.0006,
                            OrganType.Leaf,s.Id=="burlmantle" ? burlLeaf : p.Leaf,4,.14,.28);
                        if(s.Id=="blackneedle")
                        {
                            // Glossy black marginal spines grow directly out of the curved lamina tooth
                            // peaks, anchored slightly inside the edge so none float beside the blade.
                            var lo=b[leafFlat];
                            var bladeOrigin=lo.P0;
                            var bladeDir=PlantMath.SafeNormal(lo.P1-lo.P0,ray);
                            var bladeUp=PlantMath.SafeNormal(lo.Up-bladeDir*lo.Up.Dot(bladeDir),Vec3.Up);
                            var bladeSide=bladeUp.Cross(bladeDir);
                            foreach(double t in new[]{.30,.50,.70,.90}) for(int sign=-1;sign<=1;sign+=2)
                            {
                                double th=.12*t;
                                double zc=Math.Sin(th)/.12, yc=-(1-Math.Cos(th))/.12;
                                double lw=LeafWidth("blackneedle",t,false);
                                double halfW=(lw/Math.Max(1e-6,blackPmax))*(r*s.Width);
                                double off=.025*lw*lw*3.0+.008*.1*Math.Sin(3*Math.Tau*t);
                                var spineRoot=bladeOrigin+bladeDir*(zc*leafLen)+bladeUp*(yc*leafLen+off*leafLen)+bladeSide*(sign*halfW*.84);
                                var spineDir=PlantMath.SafeNormal(bladeSide*sign+bladeDir*.45+bladeUp*.08,bladeSide*sign);
                                int spine=b.AddAxis(stalk,1,spineDir,r*.044,bladeUp*.06,r*.0038,.0002,5,Tissue.Wood,.2,.2,OrganFlags.GlossyBlack|OrganFlags.Basal);
                                var sp=b[spine];
                                sp.P0=spineRoot;
                                sp.P1=spineRoot+spineDir*(r*.044);
                                sp.T0=spineDir*(r*.044);
                                sp.T1=PlantMath.SafeNormal(spineDir+bladeUp*.06,spineDir)*(r*.044);
                            }
                            double tipTh=.12*.96;
                            var tipPt=bladeOrigin+bladeDir*(Math.Sin(tipTh)/.12*leafLen)+bladeUp*(-(1-Math.Cos(tipTh))/.12*leafLen);
                            var tipDir=PlantMath.SafeNormal(bladeDir+bladeUp*.08,bladeDir);
                            int tipSpine=b.AddAxis(stalk,1,tipDir,r*.050,bladeUp*.05,r*.0040,.0002,5,Tissue.Wood,.2,.2,OrganFlags.GlossyBlack|OrganFlags.Basal);
                            var tsp=b[tipSpine];
                            tsp.P0=tipPt;
                            tsp.P1=tipPt+tipDir*(r*.050);
                            tsp.T0=tsp.T1=tipDir*(r*.050);
                        }
                    }
                    if(s.Id=="prismstar")
                    {
                        int scape=b.AddAxis(node,.90,Vec3.Up+d*.10,h*b.Range(.50,.68),d*.18,r*.011,r*.006,3,Tissue.Green,.3,.26);
                        // The head overlaps the pedicel tip so the thin stalk enters it.
                        b.AddFlat(scape,.97,Vec3.Up+d*.04,h*.06,r*.05,.001,OrganType.Flower,p.Flower,4,.6,.2);
                    }
                    else if(central || big)
                    {
                        // Starbulb's flower stem takes a gentle natural arc; the head stays on top.
                        var lean=PlantMath.Horizontal(a+b.Range(0,Math.Tau));
                        int scape=s.Id=="starbulb"
                            ? b.AddAxis(node,.90,Vec3.Up+lean*.10,h*.9,lean*.42+Vec3.Up*.05,r*.014,r*.0065,3,Tissue.Green,.3,.3)
                            : s.Id=="blackneedle"
                            ? b.AddAxis(node,.90,Vec3.Up+lean*.05,h*.92,lean*.14,r*.015,r*.0065,3,Tissue.Green,.3,.32)
                            : s.Id=="burlmantle"
                            ? b.AddAxis(node,.90,Vec3.Up,h*.9,Vec3.Zero,r*.014,r*.0085,3,Tissue.Green,.3,.3)
                            : b.AddAxis(node,.90,Vec3.Up,h*.9,Vec3.Zero,r*.009,r*.003,3,Tissue.Green,.3,.3);
                        if(s.Id=="blackneedle")
                        {
                            // Two heads, each built from overlapping bract whorls and a violet brush.
                            for(int head=0;head<2;head++)
                            {
                                var hd=PlantMath.Horizontal(a+head*Math.PI);
                                int ped=b.AddAxis(scape,head==0 ? .56 : 1,Vec3.Up+hd*(head==0 ? .85 : .10),
                                    h*(head==0 ? .11 : .075),head==0 ? -Vec3.Up*.02 : Vec3.Zero,r*.0055,r*.003,4,Tissue.Green,.48,.22);
                                // A small involucre whorl blends the peduncle into the bract head.
                                for(int k=0;k<5;k++)
                                {
                                    var collar=PlantMath.Horizontal(a+k*Math.Tau/5+head*.7);
                                    b.AddFlat(ped,.05,collar+Vec3.Up*.85,r*.115,r*.028,.0005,OrganType.Leaf,thistleBract,5,.5,.2);
                                }
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
                                int ped=b.AddAxis(scape,1,ray,r*b.Range(.15,.27),Vec3.Zero,r*.0018,r*.0007,4,Tissue.Green,.55,.2);
                                b.AddFlat(ped,1,ray,h*b.Range(.032,.050),r*b.Range(.027,.041),.001,OrganType.Flower,blooms[k%blooms.Length],5,.7,.2);
                            }
                        }
                        else if(s.Id=="tankbloom")
                        {
                            int spike=b.AddAxis(scape,1,Vec3.Up,h*.28,Vec3.Zero,r*.01,r*.003,4,Tissue.Green,.5,.3);
                            for(int w=0;w<7;w++) for(int k=0;k<4;k++)
                            {
                                double t=Math.Clamp(.04+w*.14+b.Range(-.018,.018),.02,.97), az=a+k*Math.Tau/4+w*1.1+b.Range(-.22,.22);
                                var ray=PlantMath.Horizontal(az)+Vec3.Up*(.7+t*.5+b.Range(-.12,.18));
                                b.AddFlat(spike,t,ray,h*(.085-.04*t)*b.Range(.82,1.18),r*(.075-.035*t)*b.Range(.88,1.12),.001,
                                    OrganType.Flower,p.Scarlet,5,.55,.2);
                                if(k==0) b.AddFlat(spike,t,ray,h*.025,r*.025,.001,OrganType.Flower,p.Pale,5,.7,.15);
                            }
                        }
                        else if(s.Id=="burlmantle")
                        {
                            // A spear-leaf involucre cups the burr, so the stalk thickens into
                            // its head instead of meeting a detached sphere.
                            for(int k=0;k<6;k++)
                            {
                                var collar=PlantMath.Horizontal(a+k*Math.Tau/6+.3);
                                b.AddFlat(scape,.96,collar+Vec3.Up*.55,r*.22,r*.05,.0006,OrganType.Leaf,burlLeaf,4,.55,.2);
                            }
                            b.AddFlat(scape,1,Vec3.Up,h*.115,r*.095,.001,OrganType.Fruit,p.Burr,4,.6,.2);
                        }
                        else b.AddFlat(scape,1,Vec3.Up,h*.12,r*.1,.001,OrganType.Flower,p.Flower,4,.6,.2);
                    }
                }
                if(s.Id=="tankbloom")
                {
                    // Lowest outer whorl integrates into the seated tank crown as a broad
                    // basal collar rather than floating on the floor beneath a bare gap.
                    for(int j=0;j<7;j++)
                    {
                        var skirt=PlantMath.Horizontal(rotation+j*Math.Tau/7+b.Range(-.2,.2));
                        double skirtRise=b.Range(.26,.46);
                        int skirtPet=b.AddAxis(crown,.18+.22*(j/6.0),skirt+Vec3.Up*skirtRise,r*.11,Vec3.Zero,r*.008,r*.003,3,Tissue.Green,.04,.2);
                        b.AddFlat(skirtPet,1,skirt+Vec3.Up*skirtRise,r*b.Range(.34,.46),r*s.Width*.92*b.Range(.85,1.1),.0006,
                            OrganType.Leaf,p.Leaf,4,.06+j*.01,.3);
                    }
                }
                return true;
            }

            if(runner)
            {
                // Each runner is a continuous, segmented ground-level axis. Its nodes
                // support leaves or ramets; they are not independent radial shoots.
                int paths=s.Id=="glassfinger" ? 10 : s.Id=="coinrunner" ? 6 : s.Id=="brooklace" ? 3 : 2;
                ushort succulent=s.Id=="glassfinger" ? b.AddShape(new OrganShape { Name="glassfinger_fleshy_finger", Type=OrganType.Leaf,
                    SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Sin(Math.PI*t),.65)*(1-.35*t)),
                    MidribCurl=.18f, ColorAdaxial=PlantShapes.Srgb(.52,.68,.65), ColorAbaxial=PlantShapes.Srgb(.37,.55,.51),
                    Roughness=.36f, Transmission=.25f }) : p.Leaf;
                int segments=s.Id is "brooklace" or "glassfinger" ? 6 : s.Id=="mooncoin" ? 5 : 4;
                // Brooklace forks from staggered points along one short stolon rather than
                // from a single joint; glassfinger's young fingers leave the older axes at
                // different heights and lengths.
                int stolon=crown;
                if(s.Id=="brooklace")
                {
                    stolon=b.AddAxis(crown,0,PlantMath.Horizontal(rotation+Math.PI*.5),r*.72,Vec3.Zero,r*.007,r*.005,
                        1,Tissue.Green,.005,.14);
                    b[stolon].P0=Vec3.Up*.002;
                    b[stolon].P1=b[stolon].P0+PlantMath.Horizontal(rotation+Math.PI*.5)*(r*.72);
                }
                var gfHost=new int[6]; var gfAngle=new double[4];
                for(int path=0;path<paths;path++)
                {
                    double a=rotation+path*Math.Tau/paths+b.Range(-.2,.2);
                    int previous=crown; double attachT=1;
                    if(s.Id=="brooklace")
                    {
                        previous=stolon; attachT=.16+path*.30;
                        a=rotation+path*Math.Tau/paths+b.Range(-.55,.55);
                    }
                    else if(s.Id=="coinrunner")
                        a=rotation+path*Math.Tau/paths+b.Range(-.38,.38);
                    else if(s.Id=="glassfinger")
                    {
                        a=rotation+path*Math.Tau/paths+b.Range(-.42,.42);
                        if(path>=4)
                        {
                            int host=(path-4)%4;
                            previous=gfHost[host]; attachT=b.Range(.35,.72);
                            a=gfAngle[host]+((path%2==0)?.85:-.85)+b.Range(-.35,.35);
                        }
                    }
                    double pScale=s.Id=="glassfinger" ? (path<4 ? b.Range(.95,1.22) : b.Range(.45,.72)) : 1.0;
                    int segs=s.Id=="glassfinger" ? (path<4 ? 6 : 3) : segments;
                    for(int j=0;j<segs;j++)
                    {
                        a+=b.Range(-.55,.55);
                        var forward=PlantMath.Horizontal(a);
                        var side=PlantMath.Horizontal(a+Math.PI/2);
                        double rise0=s.Id=="cinderberry" ? b.Range(.01,.16) : s.Id=="glassfinger" ? b.Range(.01,.12) : s.Id=="brooklace" ? b.Range(-.006,.014) : b.Range(.005,.07);
                        double bendAmt=s.Id=="cinderberry" ? .30 : .18;
                        int stem=b.AddAxis(previous,j==0 ? attachT : 1,forward+Vec3.Up*rise0,
                            r*(s.Id=="brooklace" ? .14 : .19)*b.Range(.75,1.25)*(s.Id=="glassfinger" ? pScale : 1),
                            side*b.Range(-bendAmt,bendAmt),r*.009,r*.007,1,Tissue.Green,.02+j*.035,.16);
                        if(s.Id=="brooklace")
                        {
                            var prev=b[previous]; var cur=b[stem];
                            if(j==0)
                            {
                                cur.P0=PlantMath.Hermite(prev.P0,prev.T0,prev.P1,prev.T1,attachT);
                            }
                            else
                            {
                                prev.Flags|=OrganFlags.Continuous;
                                cur.Flags|=OrganFlags.Continuous;
                                cur.P0=prev.P1;
                                cur.R0=prev.R1;
                            }
                            cur.P1=cur.P0+(cur.T0+cur.T1)*.5;
                            cur.P1=new Vec3(cur.P1.X,Math.Clamp(cur.P1.Y,.0015,.009),cur.P1.Z);
                        }
                        if(s.Id=="glassfinger" && path<4 && j==1)
                        {
                            gfHost[path]=stem; gfAngle[path]=a;
                        }
                        if(s.Id is "cinderberry" or "trifold")
                        {
                            // A rooted node carries a compound leaf crown, not a
                            // leaf pinned directly to the length of the stolon.
                            int stalk=b.AddAxis(stem,b.Range(.72,.98),
                                Vec3.Up+side*b.Range(-.45,.45)+forward*(s.Id=="trifold" ? b.Range(-.25,.25) : 0),
                                h*(s.Id=="cinderberry" ? .30 : .62)*b.Range(s.Id=="trifold" ? .75 : .72,s.Id=="trifold" ? 1.35 : 1.25),
                                Vec3.Zero,r*.005,r*.002,2,Tissue.Green,.12,.2);
                            for(int k=-1;k<=1;k++)
                                b.AddFlat(stalk,1,PlantMath.Horizontal(a+k*.85+b.Range(-.18,.18))+Vec3.Up*b.Range(.10,.26),
                                    r*.18*b.Range(s.Id=="trifold" ? .82 : .90,1.18),r*s.Width*.55,
                                    .0005,OrganType.Leaflet,p.Leaf,3,.25,.2);
                            if(s.Id=="cinderberry" && j%2==1)
                            {
                                int ped=b.AddAxis(stem,b.Range(.55,.95),side+Vec3.Up*b.Range(.25,.60),h*b.Range(.14,.25),Vec3.Down*.25,r*.003,r*.001,2,Tissue.Green,.4,.2);
                                b.AddFlat(ped,1,Vec3.Down,h*.12,r*.045,.001,OrganType.Fruit,p.Berry,3,.6,.2);
                                if(j%4==3)
                                {
                                    int ped2=b.AddAxis(stem,b.Range(.50,.90),side*-1+Vec3.Up*b.Range(.20,.55),h*b.Range(.10,.18),Vec3.Down*.30,r*.003,r*.001,2,Tissue.Green,.45,.2);
                                    b.AddFlat(ped2,1,Vec3.Down,h*.10,r*.038,.001,OrganType.Fruit,p.Berry,3,.66,.2);
                                }
                            }
                        }
                        else
                        {
                            int pairs=s.Id=="brooklace" ? 2 : 1;
                            for(int n=0;n<pairs;n++) for(int sign=-1;sign<=1;sign+=2)
                            {
                                double at=Math.Clamp((pairs==1 ? .8 : .35+n*.5)+b.Range(s.Id=="brooklace" ? -.24 : -.13,s.Id=="brooklace" ? .24 : .13),.08,.96);
                                var d=(s.Id=="glassfinger" && j%2==1 ? forward*sign : side*sign)
                                    +forward*(s.Id=="mooncoin" ? b.Range(-.1,.40) : .35)
                                    +Vec3.Up*(s.Id=="glassfinger" ? b.Range(.20,.45) : s.Id=="mooncoin" ? b.Range(.02,.14) : b.Range(.04,.12));
                                double length=r*(s.Id=="brooklace" ? .10 : s.Id=="glassfinger" ? .18 : .20)*b.Range(.72,1.24)
                                    *(s.Id=="glassfinger" ? pScale : 1);
                                b.AddFlat(stem,at,d,length,
                                    r*s.Width*(s.Id=="glassfinger" ? .25 : s.Id=="brooklace" ? .32 : .65)*b.Range(s.Id=="coinrunner" ? .72 : .9,s.Id=="mooncoin" ? 1.22 : 1.12),
                                    .0005,OrganType.Leaf,succulent,2,.12+j*.03,.18);
                            }
                        }
                        if(s.Id=="coinrunner" && (j==1 || j==3))
                        {
                            double branchAngle=a+(path%2==0 ? .85 : -.85)+b.Range(-.35,.35);
                            var branchForward=PlantMath.Horizontal(branchAngle);
                            var branchSide=PlantMath.Horizontal(branchAngle+Math.PI/2);
                            int branch=stem;
                            int branchSegs=j==1 ? 2 : 3;
                            double coinScale=b.Range(.72,1.1)*(j==1 ? 1.0 : .84);
                            for(int k=0;k<branchSegs;k++)
                            {
                                branch=b.AddAxis(branch,1,branchForward+Vec3.Up*b.Range(.01,.05),r*.17*b.Range(.8,1.2),
                                    branchSide*.08,r*.007,r*.005,2,Tissue.Green,.12+k*.04,.16);
                                for(int sign=-1;sign<=1;sign+=2)
                                    b.AddFlat(branch,.8,branchSide*sign+branchForward*.18+Vec3.Up*b.Range(.04,.12),
                                        r*.20*coinScale*b.Range(.85,1.15),r*s.Width*.65*coinScale,.0005,OrganType.Leaf,p.Leaf,3,.2+k*.03,.18);
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
                // Older siltstars rosettes are broad; distal ramets are younger and narrower.
                ushort siltOld=0;
                if(silt)
                    siltOld=b.AddShape(new OrganShape { Name="siltstars_leaf_age", Type=OrganType.Leaf, SurfaceForm=OrganSurfaceForm.Lamina,
                        WidthProfile=PlantShapes.Profile(t => LeafWidth(s.Id,t,false), 17), MidribCurl=.10f, CrossCurl=.012f,
                        ColorAdaxial=PlantShapes.Srgb(.33,.48,.29), ColorAbaxial=PlantShapes.Srgb(.40,.50,.32), Thickness=.0005f, Roughness=.56f });
                var nodes=new int[count];
                b[crown].P1=Vec3.Up*.003;
                nodes[0]=crown;
                for(int n=0;n<count;n++)
                {
                    double angle=rotation+n*2.39996;
                    var outward=PlantMath.Horizontal(angle);
                    if(n>0)
                    {
                        // A branching network retains separate rooted centers and the mat's breadth.
                        int parent=bright || silt ? nodes[(n-1)/2] : crown;
                        double spacing=(bright ? .25 : silt ? .30 : crag ? .31 : .42)*b.Range(.75,1.25);
                        int connector=b.AddAxis(parent,0,outward,r*spacing,Vec3.Zero,r*.005,r*.003,
                            1,Tissue.Green,.01,.12,bright ? OrganFlags.None : OrganFlags.Hidden);
                        b[connector].P0=new Vec3(b[parent].P0.X,.0015,b[parent].P0.Z);
                        b[connector].P1=new Vec3(b[connector].P0.X+b[connector].T0.X,.0015,b[connector].P0.Z+b[connector].T0.Z);
                        nodes[n]=b.AddAxis(connector,1,Vec3.Up,.0025,Vec3.Zero,
                            r*.008,r*.006,2,Tissue.Green,.04,.12,OrganFlags.Hidden|OrganFlags.Basal);
                    }
                    int node=nodes[n];
                    if(bright)
                    {
                        // Short ascending shoots with alternating fleshy leaves make a branching mat.
                        // Distal knots are younger, so their lobes shrink away from the basal mass.
                        double nodeScale=1.0-.042*Math.Min(n,8);
                        for(int shoot=0;shoot<3;shoot++)
                        {
                            double az=angle+shoot*Math.Tau/3+b.Range(-.30,.30);
                            var ray=PlantMath.Horizontal(az);
                            int stem=b.AddAxis(node,1,ray*.45+Vec3.Up,r*b.Range(.10,.18)*nodeScale,ray*.12,
                                r*.012,r*.006,3,Tissue.Green,.04,.2);
                            for(int k=0;k<5;k++)
                            {
                                var leafDir=PlantMath.Horizontal(az+k*2.39996)*.7+Vec3.Up*(.4+k*.12);
                                b.AddFlat(stem,.15+k*.19,leafDir,r*b.Range(.085,.16)*nodeScale,r*b.Range(.038,.060)*nodeScale,.001,
                                    OrganType.Leaf,fleshy,4,.1+k*.02,.2);
                            }
                        }
                    }
                    else if(silt)
                    {
                        // Distinct narrow star nodes of unequal age spread across the substrate.
                        double siltScale=(1.12-.42*Math.Sqrt(n/(double)count))*b.Range(.92,1.08);
                        int siltLeaves=5+n%3;
                        for(int i=0;i<siltLeaves;i++)
                        {
                            var ray=PlantMath.Horizontal(angle+i*Math.Tau/siltLeaves+b.Range(-.12,.12));
                            b.AddFlat(node,1,ray+Vec3.Up*b.Range(.16,.60),r*b.Range(.19,.27)*siltScale,r*.025*(i%2==0 ? 1 : .86),.0005,
                                OrganType.Leaf,n%3==1 && i%2==0 ? siltOld : p.Leaf,3,.1+i*.01,.24);
                        }
                    }
                    else
                    {
                        // Golden-angle (137.5078 deg) rosette packing: oldest basal leaves sit flush against the soil
                        // with imbricate 25-30% overlap while successively younger inner leaves rise steeper.
                        double scale = (crag ? (n == 0 ? 1.0 : .68) : (n == 0 ? 1.0 : .48)) * b.Range(.84, 1.12);
                        int totalLeaves = crag ? (n == 0 ? 22 : 16) : (n == 0 ? 26 : 18);
                        double coreRise = Math.Max(.002, h * .045);
                        int center = b.AddAxis(node, 1, Vec3.Up, coreRise, Vec3.Zero,
                            r * .007, r * .004, 3, Tissue.Green, .03, .14, OrganFlags.Hidden);
                        for (int i = 0; i < totalLeaves; i++)
                        {
                            double u = i / Math.Max(1.0, totalLeaves - 1.0); // 0 = outer oldest skirt, 1 = central youngest leaf
                            double az = angle + i * 2.399963229728653 + b.Range(-.045, .045);
                            var ray = PlantMath.Horizontal(az);
                            double attachT = Math.Clamp(.12 + .86 * u, .05, 1.0);
                            double baseRise = crag ? (.05 + 1.05 * Math.Pow(u, 1.25)) : (.08 + 1.20 * Math.Pow(u, 1.20));
                            double len = r * scale * (crag ? (.46 - .24 * Math.Pow(u, 1.05)) : (.58 - .32 * Math.Pow(u, 1.05))) * b.Range(.92, 1.08);
                            double halfW = r * s.Width * scale * (crag ? .86 : 1.0) * (1.0 - .22 * u);
                            b.AddFlat(center, attachT, ray + Vec3.Up * baseRise, len,
                                halfW, .005, OrganType.Leaf, p.Leaf, 4, .05 + .35 * u, .24);
                        }
                        if (crag && n == 0)
                        {
                            // A low outer skirt covers the basal crown peg, so the colony
                            // sits into the rock instead of balancing on a stub.
                            for (int j = 0; j < 8; j++)
                            {
                                var skirt = PlantMath.Horizontal(angle + j * Math.Tau / 8 + b.Range(-.18, .18));
                                b.AddFlat(crown, .45, skirt + Vec3.Down * .07, r * b.Range(.30, .42), r * s.Width * .8 * b.Range(.85, 1.1), .004,
                                    OrganType.Leaf, p.Leaf, 4, .05 + j * .01, .3);
                            }
                        }
                        if (!crag && n == 0)
                        {
                            int scape = b.AddAxis(node, 1, Vec3.Up, h * .75, Vec3.Up * .05, r * .006, r * .002,
                                3, Tissue.Green, .35, .25);
                            for (int k = 0; k < 5; k++)
                            {
                                var ray = PlantMath.Horizontal(angle + k * Math.Tau / 5);
                                int ped = b.AddAxis(scape, 1, ray + Vec3.Up * .2, r * .08, Vec3.Zero, r * .002, r * .001,
                                    4, Tissue.Green, .55, .2);
                                b.AddFlat(ped, 1, Vec3.Up + ray * .3, h * .055, r * .04, .001, OrganType.Flower, p.Flower, 5, .7, .2);
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
            b[crown].P1=Vec3.Up*.003;
            int chain=crown;
            for(int n=0;n<ramets;n++)
            {
                double angle=rotation+n*2.39996;
                var outward=PlantMath.Horizontal(angle);
                if(n>0)
                {
                    chain=b.AddAxis(chain,chain==crown ? 0 : 1,outward,r*(s.Id=="siltstars" ? .16 : .21),Vec3.Zero,
                        r*.004,r*.003,1,Tissue.Green,.01,.12,OrganFlags.Hidden);
                    b[chain].P0=new Vec3(b[chain].P0.X,.0015,b[chain].P0.Z);
                    b[chain].P1=new Vec3(b[chain].P0.X+b[chain].T0.X,.0015,b[chain].P0.Z+b[chain].T0.Z);
                }
                int node=n==0 ? crown : b.AddAxis(chain,1,Vec3.Up,.0025,Vec3.Zero,r*.008,r*.006,
                    2,Tissue.Green,.04,.12,OrganFlags.Hidden|OrganFlags.Basal);
                int leaves=s.Id switch { "brightknot" => 9, "siltstars" => 9, "pondstars" => 8,
                    "blue_sundew" => 8, "cragset" => 11, _ => 14 };
                for(int i=0;i<leaves;i++)
                {
                    double a=angle+i*Math.Tau/leaves+b.Range(-.20,.20);
                    var d=PlantMath.Horizontal(a);
                    // Sundew blades are of mixed ages: some broad and low, newer ones shorter
                    // and tilted up, so the mat is not one flat succulent rosette.
                    double leafScale=s.Id=="blue_sundew" ? .72+.42*b.Range(0,1) : 1;
                    double rise=s.Id is "cragset" or "sunstone_rosette" ? .45 : s.Id=="blue_sundew" ? b.Range(.03,.20) : .08;
                    double len=r*(s.Id=="siltstars" ? .13 : s.Id=="pondstars" ? .16 : .23)*leafScale;
                    b.AddFlat(node,1,d+Vec3.Up*rise,len,r*s.Width*(s.Id=="brightknot" ? .45 : .65)*leafScale,
                        .0006,OrganType.Leaf,p.Leaf,3,.1+i*.005,.24);
                    if(s.Id=="blue_sundew" && i%2==0)
                    {
                        int st=b.AddAxis(node,1,d*b.Range(.2,.5)+Vec3.Up,h*b.Range(.22,.40),d*b.Range(.04,.18),r*.003,r*.001,3,Tissue.Green,.3,.2);
                        b.AddFlat(st,1,Vec3.Up,h*.22*b.Range(.9,1.1),r*.035*b.Range(.82,1.15),.001,OrganType.Flower,p.Club,4,.5,.2);
                        // A low midrib carries short marginal tentacles that lie along the
                        // blade instead of standing over it as separate pins.
                        var bladeDirection=PlantMath.SafeNormal(d+Vec3.Up*rise,d);
                        int midrib=b.AddAxis(node,1,bladeDirection,len,Vec3.Zero,r*.002,r*.001,
                            3,Tissue.Green,.12,.18,OrganFlags.Hidden);
                        var across=PlantMath.Horizontal(a+Math.PI/2);
                        for(int pair=0;pair<4;pair++) for(int sign=-1;sign<=1;sign+=2)
                        {
                            double along=.34+pair*.16;
                            var reach=across*sign+bladeDirection*.55+Vec3.Up*.10;
                            int hair=b.AddAxis(midrib,along,reach,r*.050*b.Range(.8,1.15),Vec3.Up*.4,r*.0012,r*.0006,
                                4,Tissue.Green,.28,.12);
                            b.AddFlat(hair,1,b.TangentAt(hair,1),r*.013,r*.009,.0002,
                                OrganType.Flower,p.Club,5,.4,.15);
                        }
                    }
                }
            }
            return true;
        }
    }
}
