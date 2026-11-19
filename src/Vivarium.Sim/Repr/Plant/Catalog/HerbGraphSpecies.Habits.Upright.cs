using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

public static partial class HerbGraphSpecies
{
    private sealed partial class Generator
    {
        // Buried connectors distribute shoots among crowns while keeping the visible
        // above-ground growth independent. The first axis descends before traveling.
        private int UprightOffset(PlanBuilder b, int crown, double rotation, int index, int count, double distance)
        {
            double a=rotation+index*2.399963229728653;
            var d=PlantMath.Horizontal(a);
            int buried=b.AddAxis(crown,1,Vec3.Down, h*.055,Vec3.Zero,r*.002,r*.001,0,Tissue.Green,0,.1,OrganFlags.Hidden);
            int rhizome=b.AddAxis(buried,1,d,r*distance*Math.Sqrt((index+.5)/count),Vec3.Zero,r*.002,r*.001,0,Tissue.Green,0,.1,OrganFlags.Hidden);
            return b.AddAxis(rhizome,1,Vec3.Up,h*.020,Vec3.Zero,r*.035,r*.035,0,Tissue.Green,0,.1,OrganFlags.Hidden|OrganFlags.Basal);
        }

        private bool TryUprightHabit(PlanBuilder b,int crown,double rotation,HabitPalette p)
        {
            ushort leaf=p.Leaf, pale=p.Pale;
            if(s.Id=="widowspire")
            {
                ushort pink=b.AddShape(new OrganShape { Name=s.Id+"_pink_flower", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Star, LobeCount=5, LobeDepth=.4f,
                    WidthProfile=PlantShapes.Profile(t => .5*Math.Sin(Math.PI*t)),
                    ColorAdaxial=PlantShapes.Srgb(.82,.48,.64), ColorAbaxial=PlantShapes.Srgb(.62,.35,.49), Roughness=.66f });
                for(int i=0;i<6;i++)
                {
                    double a=rotation+i*Math.Tau/6+b.Range(-.15,.15); var d=PlantMath.Horizontal(a);
                    int baseNode=UprightOffset(b,crown,rotation,i/2,3,.18);
                    // The terminal hook must survive LOD 2, where the compiler drops
                    // childless axes thinner than 1.5 mm. Stem and curl keep a 4:1 base
                    // taper (asserted by plant.sheet21Geometry) while the welded tip stays
                    // just above the LOD gate and still tapers to a fine point.
                    int stem=b.AddAxis(baseNode,1,Vec3.Up+d*b.Range(.08,.25),h*b.Range(.75,1.05),d*b.Range(-.25,.25),r*.018,r*.0045,1,Tissue.Green,0,.45,OrganFlags.Basal);
                    int curl=b.AddAxis(stem,1,Vec3.Up+d*.12,h*.035,d*.8-Vec3.Up*.45,r*.0045,r*.0045,2,Tissue.Green,.52,.12,OrganFlags.Continuous);
                    int tip=b.AddAxis(curl,1,d+Vec3.Up*.25,h*.025,-d*1.5-Vec3.Up*.65,r*.0045,r*.0010,3,Tissue.Green,.57,.10,OrganFlags.Continuous);
                    // Weld each small hooked continuation to the preceding end ring.
                    b[stem].Flags |= OrganFlags.Continuous;
                    var shift=b[stem].P1-b[curl].P0; b[curl].P0+=shift; b[curl].P1+=shift;
                    b[curl].R0=b[stem].R1;
                    shift=b[curl].P1-b[tip].P0; b[tip].P0+=shift; b[tip].P1+=shift;
                    b[tip].R0=b[curl].R1;
                    for(int j=0;j<4;j++)
                    {
                        double t=.14+j*.14;
                        b.AddFlat(stem,t,PlantMath.Horizontal(a+j*2.4)+Vec3.Up*.4,r*.18,r*.035,.0004,OrganType.Leaf,leaf,2,.2,.2);
                    }
                    for(int j=0;j<10;j++)
                    {
                        double t=Math.Clamp(.58+j*.04+b.Range(-.015,.015),.55,.97); var ray=PlantMath.Horizontal(a+j*2.4)+Vec3.Up*.2;
                        b.AddFlat(stem,t,ray,h*.025,r*.026,.0004,OrganType.Flower,pink,2,.6,.2);
                    }
                }
                return true;
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
                ushort reedBlade=b.AddShape(new OrganShape { Name="bannerreed_curling_blade", Type=OrganType.Leaf,
                    SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(1-t,.72)),
                    MidribCurl=.95f, CrossCurl=.004f, Twist=.38f, EdgeWave=.035f, EdgeWaveFreq=3f,
                    ColorAdaxial=PlantShapes.Srgb(.48,.58,.27), ColorAbaxial=PlantShapes.Srgb(.34,.46,.20), Roughness=.68f });
                for(int i=0;i<s.Shoots;i++)
                {
                    double az=rotation+Math.Tau*(i+b.Range(-.30,.30))/s.Shoots;
                    var outward=PlantMath.Horizontal(az);
                    var side=PlantMath.Horizontal(az+Math.PI/2);
                    double height=h*b.Range(.78,.96);
                    int baseNode=UprightOffset(b,crown,rotation,i, s.Shoots,.45);
                    int culm=b.AddAxis(baseNode,1,Vec3.Up+outward*b.Range(.04,.13),height,
                        outward*b.Range(.07,.22),r*.012,r*.005,1,Tissue.Green,.015*i/s.Shoots,.45);
                    // Owner direction: the longest curling leaves stay packed into the
                    // bottom ~40% of each culm, while a pair of smaller flag leaves is
                    // carried up to meet the nodding panicle.
                    double[] leafAt={.05,.14,.24,.40,.55};
                    double[] leafScale={1.0,.94,.86,.66,.48};
                    for(int j=0;j<leafAt.Length;j++)
                    {
                        double t=Math.Clamp(leafAt[j]+b.Range(-.025,.025),.03,.62);
                        var bladeDir=PlantMath.Horizontal(az+(j%2==0 ? .75 : -.75))+Vec3.Up*b.Range(.30,.52);
                        b.AddFlat(culm,t,bladeDir,h*b.Range(.28,.40)*leafScale[j],r*s.Width*.38*Math.Max(.72,leafScale[j]),
                            .0004,OrganType.Leaf,reedBlade,2,.08+t*.15,.32);
                    }
                    // A hooded flag leaf sheathes the panicle base and closes the bare
                    // gap between the upper foliage and the nodding head.
                    b.AddFlat(culm,.66,PlantMath.Horizontal(az)+Vec3.Up*.55,h*.20,r*s.Width*.30,.0004,
                        OrganType.Leaf,reedBlade,2,.30,.26);
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
                return true;
            }
            if(s.Id is "ashwire" or "frosttussock")
            {
                bool frost=s.Id=="frosttussock";
                ushort fine=b.AddShape(new OrganShape { Name=s.Id+"_pale_blade", Type=OrganType.Leaf,
                    SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(1-t,.8)),
                    MidribCurl=frost ? 1.05f : .48f, CrossCurl=.0003f,
                    ColorAdaxial=frost ? PlantShapes.Srgb(.56,.70,.73) : PlantShapes.Srgb(.62,.70,.65),
                    ColorAbaxial=frost ? PlantShapes.Srgb(.39,.55,.60) : PlantShapes.Srgb(.46,.57,.54), Thickness=.0003f, Roughness=s.Id=="ashwire" ? .78f : .70f });
                ushort glume=b.AddShape(new OrganShape { Name=s.Id+"_silver_glume", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Lamina,
                    WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Sin(Math.PI*t),.8)),
                    MidribCurl=.15f, CrossCurl=.001f,
                    ColorAdaxial=PlantShapes.Srgb(.88,.87,.76), ColorAbaxial=PlantShapes.Srgb(.72,.75,.68), Roughness=.78f });
                const int groups=5;
                var bases=new int[groups];
                for(int g=0;g<groups;g++)bases[g]=UprightOffset(b,crown,rotation,g,groups,frost ? .20 : .32);
                for(int i=0;i<100;i++)
                {
                    int group=i%groups;
                    double a=rotation+i*2.399963229728653+b.Range(-.14,.14);
                    var d=PlantMath.Horizontal(a);
                    int root=bases[group];
                    // A mix of upright inner blades and longer, outward-curving outer blades.
                    double splay=frost ? b.Range(.34,1.05) : b.Range(.20,1.45);
                    d=PlantMath.SafeNormal(d+PlantMath.Horizontal(rotation+group*1.8)*.65,d);
                    b.AddFlat(root,1,Vec3.Up+d*splay,h*b.Range(.48,.85),r*b.Range(.025,.045),
                        .0003,OrganType.Leaf,fine,1,.06,.32);
                }
                for(int i=0;i<9;i++)
                {
                    double a=rotation+i*2.399963229728653;
                    var d=PlantMath.Horizontal(a);
                    // Flowering culms rise from the same tillers as the blades, so the
                    // heart of the tuft is not a separate dark bundle of vertical stems.
                    int root=bases[i%groups];
                    int culm=b.AddAxis(root,1,Vec3.Up+d*b.Range(.08,.24),h*b.Range(.63,1.11),
                        d*b.Range(.06,.42),r*.015,r*.007,1,Tissue.Green,.12,.4);
                    int rachis=b.AddAxis(culm,1,Vec3.Up+d*.25,h*b.Range(.24,.34),
                        d*.25,r*.006,r*.002,2,Tissue.Green,.5,.25);
                    for(int j=0;j<9;j++)
                    {
                        double t=.10+j*.095, az=a+j*2.399963229728653;
                        var ray=PlantMath.Horizontal(az)+Vec3.Up*.5;
                        int branch=b.AddAxis(rachis,t,ray,h*(.13-.07*t)*b.Range(.8,1.2),
                            Vec3.Down*.12,r*.0012,r*.0003,3,Tissue.Green,.55,.2);
                        for(int k=0;k<3;k++)
                            b.AddFlat(branch,.45+k*.23,ray+Vec3.Up*.35,h*b.Range(.025,.04),r*.012,
                                .0003,OrganType.Flower,glume,4,.62,.16);
                    }
                }
                return true;
            }
            if(s.Id=="marrowbloom")
            {
                for(int i=0;i<s.Shoots;i++)
                {
                    double a=rotation+i*2.39996; var d=PlantMath.Horizontal(a);
                    int baseNode=UprightOffset(b,crown,rotation,i,s.Shoots,.24);
                    b[baseNode].P0=new Vec3(b[baseNode].P0.X,0,b[baseNode].P0.Z);
                    b[baseNode].P1=new Vec3(b[baseNode].P1.X,0,b[baseNode].P1.Z);
                    double stemLen=h*b.Range(.58,1.03);
                    int stem=b.AddAxis(baseNode,1,Vec3.Up+d*b.Range(.05,.24),stemLen,Vec3.Up*.62+d*.68,r*.025,r*.012,1,Tissue.Green,0,.5,OrganFlags.Continuous);
                    b[stem].T1=PlantMath.SafeNormal(Vec3.Up*.62+d*.68,Vec3.Up)*stemLen;
                    b[stem].P1=b[stem].P0+(b[stem].T0+b[stem].T1)*.5;
                    for(int j=0;j<4;j++)
                        b.AddFlat(stem,Math.Clamp(.12+j*.17+b.Range(-.04,.04),.06,.72),
                            PlantMath.Horizontal(a+j*2.39996)+Vec3.Up*b.Range(-.25,.8),
                            h*b.Range(.075,.115),r*b.Range(.026,.042),.001,OrganType.Leaf,pale,2,.2,.2);
                    double neckLen=h*b.Range(.10,.16);
                    int neck=b.AddAxis(stem,1,b.TangentAt(stem,1),neckLen,
                        Vec3.Down*.92+d*.42,r*.012,r*.008,2,Tissue.Green,.5,.2,OrganFlags.Continuous);
                    b[neck].P0=b[stem].P1;
                    b[neck].R0=b[stem].R1;
                    b[neck].T0=PlantMath.SafeNormal(b[stem].T1,Vec3.Up)*neckLen;
                    b[neck].T1=PlantMath.SafeNormal(Vec3.Down*.92+d*.42,Vec3.Down)*neckLen;
                    b[neck].P1=b[neck].P0+(b[neck].T0+b[neck].T1)*.5;
                    // The bell continues the neck's fall cleanly from its tip.
                    var bellDir=PlantMath.SafeNormal(b.TangentAt(neck,1)+Vec3.Down*.35+d*b.Range(-.08,.18)+PlantMath.Horizontal(a+1.7)*b.Range(-.15,.15),Vec3.Down);
                    double bellLen=h*b.Range(.13,.19);
                    int bell=b.AddFlat(neck,1,bellDir,bellLen,r*b.Range(.10,.145),.001,OrganType.Flower,pale,3,.65,.2);
                    b[bell].P0=b[neck].P1;
                    b[bell].P1=b[bell].P0+b[bell].T0*bellLen;
                }
                return true;
            }
            if(s.Id=="coalspike")
            {
                // One central flowering culm, with individual basal straps and overlapping
                // lower sheaths rather than a colony of separate flowering shoots.
                ushort strap=b.AddShape(new OrganShape { Name="coalspike_arching_strap", Type=OrganType.Leaf,
                    SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(1-t,.7)),
                    MidribCurl=.65f, CrossCurl=.008f, ColorAdaxial=PlantShapes.Srgb(.20,.33,.15),
                    ColorAbaxial=PlantShapes.Srgb(.34,.44,.20), Roughness=.64f });
                int stem=b.AddAxis(crown,1,Vec3.Up,h*.88,PlantMath.Horizontal(rotation)*.035,
                    r*.033,r*.018,1,Tissue.Green,0,.45,OrganFlags.Basal);
                // A hidden pith mirrors the culm so each basal sheath can be built as a
                // closed collar centred on the stem. Three overlapping collars wrap the
                // lower half of the culm (clear of the densest strap bases); each one
                // flares slightly at its mouth and splits into a strap blade, so the
                // sheathing reads as a tube rather than a lamina leaning on the stalk.
                int pith=b.AddAxis(crown,1,Vec3.Up,h*.88,PlantMath.Horizontal(rotation)*.035,
                    r*.003,r*.002,1,Tissue.Green,0,.45,OrganFlags.Hidden|OrganFlags.Basal);
                for(int sh=0;sh<3;sh++)
                {
                    double t0=.14+sh*.10, len=h*.12;
                    double stemR=(r*.033-(r*.015)*t0)*1.45;
                    int sheath=b.AddAxis(pith,t0,Vec3.Up,len,PlantMath.Horizontal(rotation)*.012,
                        stemR,stemR*.80,2,Tissue.Green,.04+sh*.02,.24,OrganFlags.Basal);
                    for(int split=0;split<2;split++)
                    {
                        double a=rotation+sh*2.39996+split*Math.PI+b.Range(-.25,.25);
                        var d=PlantMath.Horizontal(a);
                        b.AddFlat(sheath,split==0 ? .95 : 1,Vec3.Up+d*b.Range(.35,1.05),
                            h*b.Range(split==0 ? .26 : .17,split==0 ? .44 : .28),r*b.Range(.030,.050),.0004,
                            OrganType.Leaf,strap,2,.09+sh*.02,.30);
                    }
                }
                // Straps rise from a broad distributed clump of rhizome crowns; inner
                // straps stand nearly upright and outer ones arch away.
                const int straps=24;
                for(int i=0;i<straps;i++)
                {
                    double a=rotation+i*2.399963229728653+b.Range(-.15,.15);
                    var d=PlantMath.Horizontal(a);
                    double outer=Math.Sqrt((i+.5)/straps);
                    int foot=UprightOffset(b,crown,rotation,i,straps,.42);
                    b.AddFlat(foot,1,Vec3.Up+d*(.12+outer)*b.Range(.48,1.48)+PlantMath.Horizontal(rotation)*.25,h*b.Range(.36,.66)*(1.1-.3*outer),
                        r*b.Range(.030,.055),.0004,OrganType.Leaf,strap,1,.04,.35);
                }
                for(int j=0;j<4;j++)
                {
                    // Short lips where the culm leaves the sheath zone; uneven heights
                    // and turn directions keep the stack from reading as a machined fan.
                    double a=rotation+j*2.39996+b.Range(-.3,.3); var d=PlantMath.Horizontal(a);
                    b.AddFlat(stem,.24+j*.055+b.Range(-.02,.02),Vec3.Up+d*b.Range(.30,.75),
                        h*b.Range(.13,.20),r*b.Range(.032,.046),.0004,OrganType.Leaf,strap,2,.11,.25);
                }
                b.AddFlat(stem,.91,b.TangentAt(stem,.91),h*.20,r*.065,.001,OrganType.Flower,p.Spike,2,.65,.2);
                return true;
            }
            if(s.Id=="duskbristle")
            {
                ushort seed=b.AddShape(new OrganShape { Name="duskbristle_seed", Type=OrganType.Fruit,
                    SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t => .5*Math.Sin(Math.PI*t)),
                    ColorAdaxial=PlantShapes.Srgb(.31,.24,.13), ColorAbaxial=PlantShapes.Srgb(.20,.16,.09), Roughness=.72f });
                for(int i=0;i<64;i++)
                {
                    double a=rotation+i*2.399963229728653; var ray=PlantMath.Horizontal(a);
                    int foot=UprightOffset(b,crown,rotation,i,64,.58);
                    b.AddFlat(foot,1,Vec3.Up+ray*b.Range(.09,.94)+PlantMath.Horizontal(rotation)*.28,h*b.Range(.48,.88),
                        r*s.Width*b.Range(.8,1.2),.0004,OrganType.Leaf,leaf,1,.04,.35);
                }
                for(int i=0;i<5;i++)
                {
                    int foot=UprightOffset(b,crown,rotation,i,5,.35);
                    var lean=PlantMath.Horizontal(rotation+i*2.4)*b.Range(.06,.35);
                    int stem=b.AddAxis(foot,1,Vec3.Up+lean,h*b.Range(.65,.98),lean*.35,r*.008,r*.004,1,Tissue.Green,.05,.42);
                    int head=b.AddAxis(stem,1,b.TangentAt(stem,1),h*b.Range(.16,.24),Vec3.Zero,r*.014,r*.003,2,Tissue.Green,.55,.20);
                    for(int j=0;j<36;j++)
                    {
                        double t=(j+.5)/36.0, a=rotation+i*1.7+j*2.399963229728653;
                        var ray=PlantMath.Horizontal(a);
                        b.AddFlat(head,t,ray,h*(.055-.025*t)*b.Range(.72,1.28),r*.010,.0003,OrganType.Fruit,seed,3,.65,.18);
                    }
                }
                return true;
            }
            if(s.Id=="glassrush")
            {
                ushort head=b.AddShape(new OrganShape { Name="glassrush_cattail", Type=OrganType.Fruit,
                    SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Sin(Math.PI*t),.22)),
                    ColorAdaxial=PlantShapes.Srgb(.34,.31,.15), ColorAbaxial=PlantShapes.Srgb(.23,.26,.12), Roughness=.85f });
                ushort floret=b.AddShape(new OrganShape { Name="glassrush_lacy_floret", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => .5*Math.Sin(Math.PI*t)),
                    ColorAdaxial=PlantShapes.Srgb(.72,.76,.32), ColorAbaxial=PlantShapes.Srgb(0.525,0.64,0.238), Roughness=.70f });
                for(int i=0;i<24;i++)
                {
                    double a=rotation+i*2.399963229728653; var ray=PlantMath.Horizontal(a);
                    int foot=UprightOffset(b,crown,rotation,i,24,.43);
                    bool tall=i<16;
                    int stem=b.AddAxis(foot,1,Vec3.Up+ray*b.Range(.03,.36),h*b.Range(tall ? .72 : .43,tall ? .98 : .65),
                        ray*.04,r*.008,r*.003,1,Tissue.Green,.02,.42);
                    if(!tall) continue;
                    if(i%2==0)
                        b.AddFlat(stem,1,Vec3.Up,h*.20,r*.046,.0005,OrganType.Fruit,head,2,.58,.2);
                    else
                    {
                        for(int j=0;j<10;j++)
                        {
                            double az=a+j*2.399963229728653; var branchDir=PlantMath.Horizontal(az)+Vec3.Up*b.Range(.25,.8);
                            int branch=b.AddAxis(stem,.87+j*.012,branchDir,r*b.Range(.21,.36),Vec3.Up*.12,r*.002,r*.0004,2,Tissue.Green,.52,.2);
                            for(int k=0;k<3;k++)
                                b.AddFlat(branch,.48+k*.23,branchDir+Vec3.Up*.2,h*.025,r*.015,.0003,OrganType.Flower,floret,3,.65,.15);
                        }
                    }
                }
                return true;
            }
            if(s.Id=="quillsedge")
            {
                ushort curl=b.AddShape(new OrganShape { Name="quillsedge_curled_blade", Type=OrganType.Leaf,
                    SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(1-t,.7)),
                    MidribCurl=1.1f, Twist=5.2f, TwistDepth=.02f, CrossCurl=.002f, EdgeWave=.05f, EdgeWaveFreq=2.5f,
                    ColorAdaxial=PlantShapes.Srgb(0.211,0.42,0.154), ColorAbaxial=PlantShapes.Srgb(.012,.018,.009), Thickness=.0003f, Roughness=.72f });
                for(int i=0;i<72;i++) {
                    // Three swept fans separated by open ground replace the even radial
                    // fountain; a scatter of lone ribbons softens the fan edges.
                    int fan=i%3;
                    double a=rotation+fan*Math.Tau/3+b.Range(-.34,.34)
                        +(i%9==0 ? Math.PI*.8+b.Range(-.30,.30) : 0);
                    var ray=PlantMath.Horizontal(a);
                    var swirl=PlantMath.Horizontal(a+Math.PI/2);
                    int foot=UprightOffset(b,crown,rotation,i,72,.08);
                    double outer=(i+.5)/72.0;
                    // Long, very narrow ribbons that corkscrew as they rise and whorl around the clump.
                    b.AddFlat(foot,1,Vec3.Up+ray*(.30+1.4*outer)*b.Range(.55,1.3)+swirl*b.Range(.08,.55)+PlantMath.Horizontal(rotation)*.32,h*b.Range(.85,1.25),r*b.Range(.0055,.0105),.0003,OrganType.Leaf,curl,1,.03,.34);
                }
                // Centrepiece: a glossy oxblood storage bulb half-sunk in the open heart of the
                // tuft, from which a sheaf of stiff banded quills rises. Each quill is a
                // seed stalk ringed cream and black, ending in a fine golden bristle plume.
                ushort bulb=b.AddShape(new OrganShape { Name="quillsedge_heart_bulb", Type=OrganType.Fruit,
                    SurfaceForm=OrganSurfaceForm.Revolved,
                    WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Sin(Math.PI*Math.Min(1,.08+.92*t)),.55)*(1-.30*Math.Pow(t,3))),
                    ColorAdaxial=PlantShapes.Srgb(0.36,0.097,0.114), ColorAbaxial=PlantShapes.Srgb(0.18,0.046,0.063), Roughness=.22f });
                ushort cream=b.AddShape(new OrganShape { Name="quillsedge_quill_cream", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t => .5*(1-.12*t)),
                    ColorAdaxial=PlantShapes.Srgb(.86,.80,.62), ColorAbaxial=PlantShapes.Srgb(.70,.64,.47), Roughness=.35f });
                ushort ink=b.AddShape(new OrganShape { Name="quillsedge_quill_black", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t => .5*(1-.12*t)),
                    ColorAdaxial=PlantShapes.Srgb(.035,.03,.028), ColorAbaxial=PlantShapes.Srgb(.02,.018,.016), Roughness=.3f });
                ushort bristle=b.AddShape(new OrganShape { Name="quillsedge_bristle", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(1-t,.6)),
                    MidribCurl=.55f, ColorAdaxial=PlantShapes.Srgb(0.93,0.751,0.336), ColorAbaxial=PlantShapes.Srgb(0.75,0.571,0.24), Thickness=.0002f, Roughness=.78f });
                var bulbLean=PlantMath.Horizontal(rotation+b.Range(0,Math.Tau))*.12;
                int heart=b.AddAxis(crown,1,Vec3.Up,h*.16,Vec3.Zero,r*.05,r*.05,1,Tissue.Green,0,.1,OrganFlags.Hidden);
                b.AddFlat(heart,0,Vec3.Up+bulbLean,h*.20,r*.17,.001,OrganType.Fruit,bulb,2,.05,.12);
                int quills=(int)Math.Round(b.Range(7,10));
                for(int q=0;q<quills;q++)
                {
                    double a=rotation+q*2.399963+b.Range(-.25,.25); var d=PlantMath.Horizontal(a);
                    double len=h*b.Range(.60,.92), rad=r*b.Range(.010,.013);
                    // Stiff, slightly splayed and very gently bowed, like a fistful of quills.
                    int quill=b.AddAxis(heart,.95,Vec3.Up+d*b.Range(.10,.32),len,d*b.Range(.05,.15),rad,rad*.5,2,Tissue.Green,.3,.15,OrganFlags.Hidden);
                    // Nine alternating bands: long cream, short black.
                    const int bands=9; double unitSum=5*1.3+4*.7, pos=0;
                    for(int k=0;k<bands;k++)
                    {
                        double t0=.97*pos/unitSum; pos+=k%2==0 ? 1.3 : .7; double t1=.97*pos/unitSum;
                        b.AddFlat(quill,t0,b.TangentAt(quill,t0),len*(t1-t0)*1.02,rad*(1-.45*t0),.0004,
                            OrganType.Flower,k%2==0 ? cream : ink,3,.35,.15);
                    }
                    // Golden bristle plume at the tip, fanning up and out.
                    for(int k=0;k<14;k++)
                    {
                        var rd=PlantMath.Horizontal(a+k*2.399963);
                        b.AddFlat(quill,.97,Vec3.Up*b.Range(.6,1.4)+rd*b.Range(.35,.9),h*b.Range(.05,.08),r*.006,.0002,
                            OrganType.Flower,bristle,3,.7,.2);
                    }
                }
                return true;
            }
            if(s.Id=="veilblade")
            {
                ushort blade=b.AddShape(new OrganShape { Name="veilblade_basal_blade", Type=OrganType.Leaf,
                    SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(1-t,.65)),
                    MidribCurl=.78f, CrossCurl=.001f, ColorAdaxial=PlantShapes.Srgb(.55,.64,.32),
                    ColorAbaxial=PlantShapes.Srgb(.40,.51,.25), Thickness=.0005f, Roughness=.68f });
                ushort seed=b.AddShape(new OrganShape { Name="veilblade_seed", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*t),.7)),
                    ColorAdaxial=PlantShapes.Srgb(.68,.63,.38), ColorAbaxial=PlantShapes.Srgb(.48,.45,.25), Roughness=.76f });
                ushort filament=b.AddShape(new OrganShape { Name="veilblade_filament", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(1-t,.5)),
                    MidribCurl=.55f, EdgeWave=.04f, EdgeWaveFreq=5f,
                    ColorAdaxial=PlantShapes.Srgb(.84,.80,.60), ColorAbaxial=PlantShapes.Srgb(.62,.58,.40), Thickness=.0002f, Roughness=.80f });
                // Broad arching blades gather into three unequal fans with open ground
                // between them, instead of a perfectly radial spray.
                for(int i=0;i<30;i++)
                {
                    int group=i%3;
                    double a=rotation+group*Math.Tau/3+b.Range(-.55,.55)
                        +(i%5==0 ? Math.PI+b.Range(-.45,.45) : 0); var d=PlantMath.Horizontal(a);
                    int foot=UprightOffset(b,crown,rotation,i,30,.24);
                    b.AddFlat(foot,1,Vec3.Up+d*b.Range(.25,1.35)+PlantMath.Horizontal(rotation)*.42,h*b.Range(.48,.68),r*b.Range(.055,.08),.0005,OrganType.Leaf,blade,1,.04,.34);
                }
                for(int i=0;i<3;i++)
                {
                    double a=rotation+i*2.4; var d=PlantMath.Horizontal(a);
                    int foot=UprightOffset(b,crown,rotation,i,3,.13);
                    int stem=b.AddAxis(foot,1,Vec3.Up+d*b.Range(.06,.27),h*b.Range(1.24,1.50),d*.16,r*.024,r*.006,1,Tissue.Green,.05,.42,OrganFlags.Basal);
                    for(int tier=0;tier<16;tier++) for(int side=-3;side<=3;side+=1)
                        if(side!=0 && (Math.Abs(side)<3 || tier%2==0))
                    {
                        double t=.64+tier*.021;
                        var ray=PlantMath.Horizontal(a+side*b.Range(.55,1.15)+tier*.7)+Vec3.Up*b.Range(.10,.45);
                        b.AddFlat(stem,t,ray,h*b.Range(.058,.082),r*.013,.0003,OrganType.Flower,seed,2,.52,.18);
                        if(tier%5<4)
                            b.AddFlat(stem,t+.008,PlantMath.Horizontal(a-side*b.Range(.5,1.1)+tier*.7)+Vec3.Up*b.Range(.0,.55),
                                h*b.Range(.085,.135),r*.0055,.0002,OrganType.Flower,filament,2,.55,.2);
                    }
                }
                return true;
            }
            if(s.Id=="plumegrass")
            {
                // One central flowering culm rising from a narrow tussock of arching
                // basal blades. Heavy pale seeds sit in tiers up the head; each tier is
                // haloed by feathery fronds that grade blue at the base to pink at the tip.
                double[][] colors={ new[]{.20,.56,.73}, new[]{.22,.72,.67}, new[]{.57,.76,.39},
                    new[]{.89,.70,.36}, new[]{.91,.43,.60}, new[]{.72,.32,.70} };
                var fronds=new ushort[colors.Length];
                for(int c=0;c<colors.Length;c++)
                {
                    var col=colors[c];
                    fronds[c]=b.AddShape(new OrganShape { Name="plumegrass_rainbow_frond_"+c,
                        Type=OrganType.Flower, SurfaceForm=OrganSurfaceForm.Lamina,
                        WidthProfile=PlantShapes.Profile(t => Math.Pow(1-t,.6)*(.55+.45*Math.Abs(Math.Sin(t*Math.PI*7)))),
                        MidribCurl=.45f, EdgeWave=.06f, EdgeWaveFreq=7f,
                        ColorAdaxial=PlantShapes.Srgb(col[0],col[1],col[2]),
                        ColorAbaxial=PlantShapes.Srgb(col[0]*.8,col[1]*.8,col[2]*.8), Thickness=.0002f, Roughness=.82f });
                }
                ushort seed=b.AddShape(new OrganShape { Name="plumegrass_heavy_seed", Type=OrganType.Fruit,
                    SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Sin(Math.PI*t),.7)),
                    ColorAdaxial=PlantShapes.Srgb(.86,.80,.62), ColorAbaxial=PlantShapes.Srgb(.66,.60,.44), Roughness=.76f });
                ushort basal=b.AddShape(new OrganShape { Name="plumegrass_basal_blade", Type=OrganType.Leaf,
                    SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(1-t,.75)),
                    MidribCurl=.85f, CrossCurl=.002f, ColorAdaxial=PlantShapes.Srgb(.56,.62,.43),
                    ColorAbaxial=PlantShapes.Srgb(.43,.52,.34), Roughness=.70f });
                for(int j=0;j<34;j++)
                {
                    double a=rotation+j*2.399963229728653+b.Range(-.15,.15);
                    var ray=PlantMath.Horizontal(a);
                    int foot=UprightOffset(b,crown,rotation,j,34,.11);
                    b.AddFlat(foot,1,Vec3.Up+ray*b.Range(.55,1.30),h*b.Range(.30,.55),
                        r*b.Range(.025,.045),.0004,OrganType.Leaf,basal,1,.04,.32);
                }
                var lean=PlantMath.Horizontal(rotation+b.Range(-.4,.4));
                int culm=b.AddAxis(crown,1,Vec3.Up+lean*.04,h*.98,lean*.12,r*.026,r*.008,1,Tissue.Green,0,.45,OrganFlags.Basal);
                for(int k=0;k<3;k++)
                {
                    var d=PlantMath.Horizontal(rotation+k*2.4);
                    b.AddFlat(culm,.05+k*.11,Vec3.Up+d*.22,h*.22,r*.035,.0004,OrganType.Leaf,basal,2,.08,.25);
                }
                const int tiers=9;
                for(int tier=0;tier<tiers;tier++)
                {
                    // Tiers wander up the culm instead of stepping evenly, and each one
                    // carries a different number of seeds and barbs so the rainbow never
                    // resolves into a repeated ring.
                    double t=Math.Clamp(.42+tier*.062+b.Range(-.030,.030),.40,.96), u=tier/(double)(tiers-1);
                    ushort frond=fronds[Math.Min(fronds.Length-1,(int)(u*fronds.Length))];
                    for(int k=0;k<4;k++)
                    {
                        double a=rotation+tier*.9+k*Math.Tau/4+b.Range(-.35,.35);
                        var rd=PlantMath.Horizontal(a)+Vec3.Up*(.45+.40*u);
                        double len=r*(.30-.15*u)*b.Range(.72,1.32);
                        int branch=b.AddAxis(culm,t,rd,len,Vec3.Down*.18,r*.0035,r*.0009,2,Tissue.Green,.45,.2);
                        for(int n=0;n<3;n++)
                            b.AddFlat(branch,.32+n*.26+b.Range(-.04,.04),rd+Vec3.Up*b.Range(.2,.5),
                                h*b.Range(.028,.036),r*b.Range(.016,.021),.0005,OrganType.Fruit,seed,3,.6,.15);
                        // Narrow feather barbs splay past the seeds; their lengths and
                        // stations vary so the plume fringe stays loose and unequal.
                        for(int n=0;n<6;n++) for(int sign=-1;sign<=1;sign+=2)
                        {
                            var barb=PlantMath.Horizontal(a+sign*b.Range(.45,1.50))+Vec3.Up*b.Range(-.10,.70);
                            b.AddFlat(branch,.06+n*.16+b.Range(-.03,.03),barb,h*b.Range(.10,.21)*(1-.35*u),r*b.Range(.012,.026),.0002,
                                OrganType.Flower,frond,3,.62,.18);
                        }
                    }
                }
                // A loose crown of barbs closes the head, so its tip stays feathery
                // rather than ending on the last even ring of seeds.
                for(int n=0;n<10;n++)
                {
                    double a=rotation+n*Math.Tau/10+b.Range(-.22,.22);
                    var rd=PlantMath.Horizontal(a)+Vec3.Up*b.Range(.35,.95);
                    b.AddFlat(culm,1,rd,h*b.Range(.075,.115),r*b.Range(.010,.020),.0002,
                        OrganType.Flower,fronds[^1],4,.68,.18);
                }
                return true;
            }
            if(s.Id is "burrflag" or "cloudtuft" or "coalspike" or "glassrush" or "lanternsedge" or "plumegrass" or "quillsedge" or "veilblade")
            {
                ushort petal=p.Flower, cotton=p.Cotton, spike=p.Spike, lantern=p.Lantern, burr=p.Burr;
                ushort burrBlade=leaf, burrSpine=leaf;
                if(s.Id=="burrflag")
                {
                    burrBlade=b.AddShape(new OrganShape { Name="burrflag_rounded_blade", Type=OrganType.Leaf,
                        SurfaceForm=OrganSurfaceForm.Lamina,
                        WidthProfile=PlantShapes.Profile(t => t<.08 ? .55+.45*t/.08 : t<.82 ? 1 : Math.Sqrt(Math.Max(0,1-Math.Pow((t-.82)/.18,2))),17),
                        MidribCurl=.18f, CrossCurl=.0005f, ColorAdaxial=PlantShapes.Srgb(.22,.43,.20),
                        ColorAbaxial=PlantShapes.Srgb(.32,.46,.25), Thickness=.0006f, Roughness=.57f });
                    burr=b.AddShape(new OrganShape { Name="burrflag_dark_burr", Type=OrganType.Fruit,
                        SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Sin(Math.PI*t),.5)),
                        ColorAdaxial=PlantShapes.Srgb(0.22,0.191,0.084), ColorAbaxial=PlantShapes.Srgb(0.13,0.14,0.053), Roughness=.72f });
                    burrSpine=b.AddShape(new OrganShape { Name="burrflag_dark_spine", Type=OrganType.Leaf,
                        SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => 1-t),
                        ColorAdaxial=PlantShapes.Srgb(0.33,0.29,0.13), ColorAbaxial=PlantShapes.Srgb(0.20,0.19,0.08), Thickness=.0004f, Roughness=.50f });
                }
                ushort[] plumeFronds=Array.Empty<ushort>();
                if(s.Id=="plumegrass")
                {
                    double[][] colors={ new[]{.20,.56,.73}, new[]{.22,.72,.67}, new[]{.57,.76,.39},
                        new[]{.89,.70,.36}, new[]{.91,.43,.60}, new[]{.72,.32,.70} };
                    plumeFronds=new ushort[colors.Length];
                    for(int c=0;c<colors.Length;c++)
                    {
                        var col=colors[c];
                        plumeFronds[c]=b.AddShape(new OrganShape { Name="plumegrass_rainbow_frond_"+c,
                            Type=OrganType.Flower, SurfaceForm=OrganSurfaceForm.Lamina,
                            WidthProfile=PlantShapes.Profile(t => Math.Pow(1-t,.8)), MidribCurl=.28f,
                            ColorAdaxial=PlantShapes.Srgb(col[0],col[1],col[2]),
                            ColorAbaxial=PlantShapes.Srgb(col[0]*.8,col[1]*.8,col[2]*.8), Roughness=.82f });
                    }
                    ushort basal=b.AddShape(new OrganShape { Name="plumegrass_basal_blade", Type=OrganType.Leaf,
                        SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(1-t,.75)),
                        MidribCurl=.85f, CrossCurl=.002f, ColorAdaxial=PlantShapes.Srgb(.56,.62,.43),
                        ColorAbaxial=PlantShapes.Srgb(.43,.52,.34), Roughness=.70f });
                    for(int j=0;j<32;j++)
                    {
                        double a=rotation+j*2.399963229728653+b.Range(-.15,.15);
                        var ray=PlantMath.Horizontal(a);
                        int foot=UprightOffset(b,crown,rotation,j,32,.13);
                        b.AddFlat(foot,1,Vec3.Up+ray*b.Range(.6,1.35),h*b.Range(.36,.68),
                            r*b.Range(.025,.045),.0004,OrganType.Leaf,basal,1,.04,.32);
                    }
                }
                var cloudWisps=Array.Empty<ushort>();
                ushort cloudPuff=0, cloudBract=0;
                if(s.Id=="cloudtuft")
                {
                    // Soft billowing cotton puffs cupped by dark bracts and draped in
                    // broad curling downy wisps so heads read as cloudy tufts, not daisies.
                    cloudPuff=b.AddShape(new OrganShape { Name="cloudtuft_puff", Type=OrganType.Flower,
                        SurfaceForm=OrganSurfaceForm.Revolved,
                        WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Max(0,Math.Sin(Math.PI*t)),.48)),
                        RadialAsymmetry=.26f, ColorAdaxial=PlantShapes.Srgb(.96,.95,.90), ColorAbaxial=PlantShapes.Srgb(.84,.85,.79),
                        Roughness=.92f });
                    cloudBract=b.AddShape(new OrganShape { Name="cloudtuft_involucre_bract", Type=OrganType.Flower,
                        SurfaceForm=OrganSurfaceForm.Lamina,
                        WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Max(0,Math.Sin(Math.PI*t)),.72)),
                        MidribCurl=.35f, ColorAdaxial=PlantShapes.Srgb(.30,.28,.16), ColorAbaxial=PlantShapes.Srgb(.22,.22,.12),
                        Thickness=.0004f, Roughness=.76f });
                    cloudWisps=new ushort[3];
                    for(int w=0;w<3;w++)
                    {
                        cloudWisps[w]=b.AddShape(new OrganShape { Name="cloudtuft_wisp_"+w, Type=OrganType.Flower,
                            SurfaceForm=OrganSurfaceForm.Lamina,
                            WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Max(0,Math.Sin(Math.PI*Math.Pow(t,.68))),.42)*(.78+.22*Math.Sin(t*(w==1 ? 4.4 : 3.1))),13),
                            MidribCurl=.75f+.22f*w, CrossCurl=.028f+.008f*w, Twist=.5f*w, TwistDepth=.004f,
                            EdgeWave=.12f+.03f*w, EdgeWaveFreq=4f+w,
                            ColorAdaxial=PlantShapes.Srgb(.95,.94,.88), ColorAbaxial=PlantShapes.Srgb(.84,.85,.78),
                            Thickness=.00018f, Roughness=.90f });
                    }
                    for(int j=0;j<36;j++)
                    {
                        double a=rotation+j*2.39996;
                        var ray=PlantMath.Horizontal(a);
                        int foot=UprightOffset(b,crown,rotation,j,36,.32);
                        b.AddFlat(foot,1,Vec3.Up+ray*b.Range(.35,1.0),h*b.Range(.35,.65),
                            r*b.Range(.008,.016),.0003,OrganType.Leaf,leaf,1,.04,.32);
                    }
                }
                int groups=s.Id switch { "coalspike" => 4, "burrflag" => 3, "glassrush" => 5, "cloudtuft" => 6, "veilblade" => 5, "plumegrass" => 3, "lanternsedge" => 3, _ => 2 };
                double patch=s.Id switch { "cloudtuft" => .58, "coalspike" => .46, "veilblade" => .44, "burrflag" => .38, "glassrush" => .30, "plumegrass" => .23, "lanternsedge" => .22, _ => .12 };
                for(int i=0;i<s.Shoots;i++)
                {
                    int group=i%groups;
                    // Flags and cattails grow as two-ranked fans; sedges and rushes
                    // make compact, separate crowns; reeds occupy a looser patch.
                    bool fan=s.Id is "burrflag" or "coalspike" or "glassrush";
                    double az=fan ? rotation+(i%2)*Math.PI+b.Range(-.22,.22) : rotation+i*2.399963229728653;
                    Vec3 outwards=PlantMath.Horizontal(az), side=PlantMath.Horizontal(az+Math.PI/2);
                    double variance=b.Range(.82,1.14), phase=.015*i/s.Shoots;
                    // Each culm gets its own lean and arch so a tussock fountains instead of radiating
                    // as identical spokes: inner culms stand taller, outer ones splay and bow more.
                    double splay=s.Spread*b.Range(.45,1.35);
                    var d=outwards*splay+Vec3.Up+side*b.Range(-.12,.12);
                    var arch=-Vec3.Up*splay*b.Range(.3,.65)+side*b.Range(s.Id=="cloudtuft" ? -.38 : -.15,s.Id=="cloudtuft" ? .38 : .15);
                    int baseNode=UprightOffset(b,crown,rotation,group,groups,patch);
                    int culm=b.AddAxis(baseNode,1,d,h*variance*(1.08-.2*splay/Math.Max(.1,s.Spread)),arch,r*(s.Id=="burrflag" ? .021 : .014),r*(s.Id=="burrflag" ? .009 : .005),1,Tissue.Green,phase,.45,OrganFlags.Basal);
                    b.AddFlat(culm,.04,d+side*b.Range(-.25,.25)+outwards*b.Range(0,.35),h*b.Range(.55,.9)*variance,r*s.Width*b.Range(.8,1.2),.0004,OrganType.Leaf,burrBlade,2,.05,.38);
                    if(s.Leaves>1)
                        b.AddFlat(culm,b.Range(.18,.32),Vec3.Up*.65+outwards*s.Spread*b.Range(.7,1.4)+side*b.Range(-.5,.5),h*b.Range(.35,.6),r*s.Width*.8,.0004,OrganType.Leaf,burrBlade,2,.12,.32);
                    if(s.Id=="lanternsedge" && i%3==0)
                    {
                        for(int ray=0;ray<8;ray++)
                        {
                            var rd=PlantMath.Horizontal(az+ray*Math.Tau/8+b.Range(-.16,.16))+Vec3.Up*b.Range(.30,.83);
                            int branch=b.AddAxis(culm,1,rd,r*b.Range(.16,.32),Vec3.Down*.10,r*.007,r*.003,2,Tissue.Green,.5,.2);
                            b.AddFlat(branch,1,Vec3.Up,h*.035,r*.025,.001,OrganType.Flower,lantern,3,.7,.2);
                        }
                    }
                    else if(s.Id=="burrflag" && i%2==0)
                    {
                        for(int head=0;head<3;head++)
                        {
                            double t=.62+head*.14+b.Range(-.02,.02);
                            var hdir=PlantMath.Horizontal(az+head*2.4);
                            // Each burr sits on a short but visibly thick peduncle, so the
                            // supporting culm meets the head instead of vanishing into it.
                            int peduncle=b.AddAxis(culm,t,Vec3.Up+hdir*.18,h*b.Range(.025,.045),hdir*.05,
                                r*.012,r*.010,2,Tissue.Green,.55,.18);
                            b.AddFlat(peduncle,1,Vec3.Up,h*.060,r*.046,.001,OrganType.Fruit,burr,2,.6,.2);
                            int centre=b.AddAxis(peduncle,1,Vec3.Up,h*.030,Vec3.Zero,r*.001,r*.0005,3,Tissue.Green,.6,.1,OrganFlags.Hidden);
                            for(int k=0;k<28;k++)
                            {
                                double y=1-2*(k+.5)/28.0, a=k*2.39996;
                                var ray=PlantMath.Horizontal(a)*Math.Sqrt(1-y*y)+Vec3.Up*y;
                                int root=b.AddAxis(centre,1,ray,r*.048,Vec3.Zero,r*.0005,r*.0003,3,Tissue.Green,.6,.1,OrganFlags.Hidden);
                                b.AddFlat(root,1,PlantMath.SafeNormal(ray+Vec3.Up*b.Range(-.25,.35),ray),
                                    r*b.Range(.055,.075),r*.013,.0003,OrganType.Leaf,burrSpine,3,.6,.15);
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
                            // Fine colored barbs surround the existing pale seed glumes.
                            for(int n=0;n<5;n++) for(int sign=-1;sign<=1;sign+=2)
                            {
                                var barb=PlantMath.Horizontal(a+sign*1.05)+Vec3.Up*.30;
                                b.AddFlat(branch,.18+n*.18,barb,h*.045,r*.006,.0003,
                                    OrganType.Flower,plumeFronds[Math.Min(5,tier*6/7)],3,.64,.15);
                            }
                        }
                    }
                    else if(s.Id=="cloudtuft" && i%2==0)
                    {
                        // Small involucre bracts cup a cluster of asymmetric cotton bolls
                        // enveloped in broad curling downy wisps so the tuft reads as cloud.
                        var tuftTip=b.TangentAt(culm,1);
                        for(int brIdx=0;brIdx<4;brIdx++)
                            b.AddFlat(culm,.94,PlantMath.Horizontal(az+brIdx*Math.Tau/4)*.85+Vec3.Up*.55,
                                h*.022,r*.010,.0004,OrganType.Flower,cloudBract,2,.62,.18);
                        for(int pf=0;pf<4;pf++)
                        {
                            double pAng=az+pf*Math.Tau/4+b.Range(-.3,.3);
                            var pDir=PlantMath.SafeNormal(tuftTip+PlantMath.Horizontal(pAng)*.55+Vec3.Up*b.Range(.15,.55),tuftTip);
                            b.AddFlat(culm,b.Range(.96,1.0),pDir,h*b.Range(.034,.048),r*b.Range(.026,.036),
                                .001,OrganType.Flower,cloudPuff,2,.64,.2);
                        }
                        for(int lockIndex=0;lockIndex<9;lockIndex++)
                        {
                            double angle=az+lockIndex*Math.Tau/9+b.Range(-.28,.28);
                            var radial=PlantMath.Horizontal(angle);
                            var sideways=PlantMath.Horizontal(angle+Math.PI/2);
                            for(int wisp=0;wisp<4;wisp++)
                            {
                                double elev=wisp switch
                                {
                                    0 => lockIndex%3==0 ? b.Range(-.30,-.05) : b.Range(.05,.40),
                                    1 => b.Range(.25,.65),
                                    2 => b.Range(.55,.95),
                                    _ => b.Range(.85,1.25),
                                };
                                var dir=radial*Math.Cos(elev)+Vec3.Up*Math.Sin(elev)+sideways*b.Range(-.32,.32);
                                double len=h*b.Range(.040,.062)*(wisp==3 ? 1.18 : 1.0)*b.Range(.80,1.15);
                                b.AddFlat(culm,b.Range(.955,.995),dir,len,r*b.Range(.032,.054),
                                    .00018,OrganType.Flower,cloudWisps[(lockIndex+wisp)%3],3,.62+wisp*.02,.18);
                            }
                            // Long stray hairs reach past the downy body and keep the
                            // outline ragged at whole-plant scale.
                            for(int stray=0;stray<2;stray++)
                            {
                                double elev=b.Range(.20,1.05);
                                var dir=radial*Math.Cos(elev)+Vec3.Up*Math.Sin(elev)+sideways*b.Range(-.55,.55);
                                b.AddFlat(culm,b.Range(.94,.99),dir,h*b.Range(.062,.092),r*b.Range(.014,.024),
                                    .00016,OrganType.Flower,cloudWisps[2],3,.66,.2);
                            }
                        }
                    }
                    else if(s.Id=="coalspike" && i%2==0)
                        b.AddFlat(culm,.92,Vec3.Up,h*.20,r*.065,.001,OrganType.Flower,spike,2,.65,.2);
                    else if(s.Bloom>0 && i%2==0 && s.Id is not ("lanternsedge" or "burrflag" or "plumegrass")) for(int j=0;j<Math.Min(3,s.Bloom);j++)
                    {
                        double bt=.75+j*.09; var tan=b.TangentAt(culm,bt);
                        b.AddFlat(culm,bt,tan+side*(j-1)*.4+Vec3.Down*.15*s.Spread,h*.12,r*.045,.0006,OrganType.Flower,petal,2,.58+j*.04,.18);
                    }
                }
                return true;
            }
            if(s.Id=="butterglint")
            {
                ushort butterPetal=b.AddShape(new OrganShape { Name="butterglint_layered_petal", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Max(0,Math.Sin(Math.PI*t)),.55)),
                    MidribCurl=.12f, CrossCurl=.018f, ColorAdaxial=PlantShapes.Srgb(0.98,0.795,0.256),
                    ColorAbaxial=PlantShapes.Srgb(0.68,0.52,0.176), Thickness=.0005f, Roughness=PetalRough(s.Id) });
                // Low lobed basal foliage with uneven pitches and sizes, so the ring of
                // leaves is not a level plate under the flowering stem.
                for(int i=0;i<7;i++)
                {
                    double a=rotation+i*2.399963; var d=PlantMath.Horizontal(a);
                    int pet=b.AddAxis(crown,1,d+Vec3.Up*b.Range(.18,.85),r*b.Range(.15,.34),Vec3.Up*b.Range(.04,.14),
                        r*.005,r*.002,1,Tissue.Green,0,.10);
                    for(int k=-1;k<=1;k++)
                        b.AddFlat(pet,1,PlantMath.Horizontal(a+k*.65)+Vec3.Up*b.Range(.15,.60),
                            r*b.Range(.16,.22),r*b.Range(.060,.085),.0004,OrganType.Leaf,p.Leaf,2,.15,.2);
                }
                int stem=b.AddAxis(crown,1,Vec3.Up,h*.62,PlantMath.Horizontal(rotation)*.12,r*.009,r*.003,1,Tissue.Green,.1,.4);
                for(int j=0;j<5;j++)
                {
                    double a=rotation+j*2.4; var d=PlantMath.Horizontal(a);
                    int fork=b.AddAxis(stem,Math.Clamp(.48+j*.095+b.Range(-.025,.025),.4,.95),d+Vec3.Up*.8,
                        h*b.Range(.20,.38),d*.45+Vec3.Down*b.Range(1.10,1.40),r*.003,r*.001,2,Tissue.Green,.4,.25);
                    b.AddFlat(fork,.25,d+Vec3.Up*.4,r*.12,r*.045,.0004,OrganType.Leaf,p.Leaf,3,.25,.2);
                    // Bloom age varies head to head: the outer whorl stays heavy, while
                    // the inner whorl tightens or opens and the whole head scales slightly.
                    double bloom=b.Range(.85,1.15);
                    b.AddFlat(fork,1,Vec3.Up+d*.2,h*.075*bloom,r*.065*bloom,.001,OrganType.Flower,p.Flower,3,.7,.2);
                    for(int layer=0;layer<2;layer++) for(int k=0;k<5;k++)
                    {
                        var ray=PlantMath.Horizontal(a+k*Math.Tau/5+layer*.55);
                        double innerScale=layer==0 ? 1.0 : b.Range(.72,1.12);
                        double lift=layer==0 ? .18 : b.Range(.40,.72);
                        b.AddFlat(fork,1,ray+Vec3.Up*lift,h*(layer==0 ? .095 : .070)*bloom*innerScale,
                            r*(layer==0 ? .034 : .026)*bloom*innerScale,.0005,OrganType.Flower,butterPetal,3,.7,.2);
                    }
                }
                return true;
            }
            if(s.Id=="fenfoam")
            {
                var d=PlantMath.Horizontal(rotation); var side=PlantMath.Horizontal(rotation+Math.PI/2);
                int cane=b.AddAxis(crown,1,Vec3.Up+d*.25+side*b.Range(-.12,.12),h*.84,d*.65-Vec3.Up*.32+side*b.Range(-.7,.7),r*.012,r*.003,1,Tissue.Green,0,.45);
                for(int j=0;j<6;j++)
                {
                    double t=.12+j*.105+b.Range(-.04,.04); var ray=side*(j%2==0?1:-1)*b.Range(.7,1.25)+d*b.Range(.0,.5);
                    int rachis=b.AddAxis(cane,t,ray+Vec3.Up*b.Range(.05,.40),r*b.Range(.22,.40),Vec3.Down*b.Range(.03,.2)+PlantMath.Horizontal(rotation+j*1.7)*b.Range(-.25,.25),r*.003,r*.001,2,Tissue.Green,.1,.25);
                    int nl=j==5?3:4;
                    for(int k=0;k<nl;k++) for(int sign=-1;sign<=1;sign+=2)
                        b.AddFlat(rachis,.2+k*.2+b.Range(-.03,.03),d*sign+ray*.25+Vec3.Up*b.Range(.0,.3),r*b.Range(.09,.15),r*b.Range(.028,.04),.0004,OrganType.Leaflet,p.Leaf,3,.2,.2);
                }
                // A broad compound head: stout primary rays end in overlapping,
                // clustered secondary umbels, each bearing several small florets.
                for(int j=0;j<11;j++)
                {
                    double a=rotation+j*2.39996; var ray=PlantMath.Horizontal(a);
                    // Lopsided head: rays on one side run longer and lift higher.
                    double lop=.75+.5*Math.Max(0,ray.Dot(d)*.5+.5);
                    int branch=b.AddAxis(cane,.76+j*.019,ray*b.Range(.6,1.0)+Vec3.Up*b.Range(.8,1.2), r*b.Range(.16,.31)*lop,
                        Vec3.Down*b.Range(.05,.2)+PlantMath.Horizontal(a+1.6)*b.Range(-.2,.2),r*.004,r*.0015,2,Tissue.Green,.36,.22);
                    for(int k=0;k<7;k++)
                    {
                        double az=a+k*Math.Tau/7+b.Range(-.2,.2); var spoke=PlantMath.Horizontal(az);
                        int umbel=b.AddAxis(branch,1,spoke*b.Range(.6,1.0)+Vec3.Up*b.Range(.35,.8),r*b.Range(.05,.14),
                            Vec3.Down*b.Range(.0,.2)+PlantMath.Horizontal(az+1.6)*b.Range(-.15,.15),r*.0015,r*.0005,3,Tissue.Green,.48,.18);
                        for(int n=0;n<5;n++)
                        {
                            double fa=az+n*Math.Tau/5;
                            var floret=PlantMath.Horizontal(fa)*.45+Vec3.Up;
                            b.AddFlat(umbel,.80+(n%2)*.18,floret,h*.027,r*.023,.0004,
                                OrganType.Flower,p.Flower,4,.58,.16);
                        }
                    }
                }
                return true;
            }
            if(s.Id=="hallowsting")
            {
                for(int i=0;i<3;i++)
                {
                    int root=UprightOffset(b,crown,rotation,i,3,.48);
                    var lean=PlantMath.Horizontal(rotation+i*2.4+b.Range(-.5,.5));
                    int stem=b.AddAxis(root,1,Vec3.Up+lean*b.Range(.03,.10),h*b.Range(.75,1.0),lean*b.Range(-.55,.55)+PlantMath.Horizontal(rotation+i*2.4+1.9)*b.Range(-.5,.5),r*.01,r*.003,1,Tissue.Green,0,.45);
                    for(int j=0;j<8;j++)
                    {
                        double t=Math.Clamp(.08+j*.125+b.Range(-.04,.04),.03,.97), a=rotation+j*Math.PI/2+b.Range(-.4,.4);
                        for(int sign=-1;sign<=1;sign+=2)
                        {
                            var ray=PlantMath.Horizontal(a+b.Range(-.25,.25))*sign;
                            b.AddFlat(stem,t,ray+Vec3.Up*b.Range(.15,.6),r*(.30-j*.025)*b.Range(.85,1.15),r*s.Width*(1-j*.08),.0005,OrganType.Leaf,p.Leaf,2,.15,.2);
                            if(j>=3)
                            {
                                int tassel=b.AddAxis(stem,t,ray+Vec3.Down*b.Range(.05,.35),r*b.Range(.15,.23),Vec3.Down*.5+PlantMath.Horizontal(a+1.5)*b.Range(-.4,.4),r*.002,r*.0006,2,Tissue.Green,.5,.2);
                                for(int k=0;k<5;k++) b.AddFlat(tassel,.2+k*.18,Vec3.Down+ray*.3,h*.025,r*.015,.0004,OrganType.Flower,p.Flower,3,.6,.15);
                            }
                        }
                    }
                }
                return true;
            }
            if(s.Id=="heddlelace")
            {
                b[crown].P1=Vec3.Up*(h*.018);
                b[crown].R0=r*.015;
                b[crown].R1=r*.010;
                b[crown].Flags|=OrganFlags.Continuous;
                var sway=PlantMath.Horizontal(rotation+b.Range(0,Math.Tau));
                int stem=b.AddAxis(crown,1,Vec3.Up+sway*.06,h*.68,sway*b.Range(.5,.9)+PlantMath.Horizontal(rotation+2.0)*b.Range(-.5,.5),r*.010,r*.003,1,Tissue.Green,0,.4,OrganFlags.Continuous);
                b[stem].P0=b[crown].P1;
                b[stem].R0=b[crown].R1;
                b[stem].P1=b[stem].P0+(b[stem].T0+b[stem].T1)*.5;
                for(int j=0;j<4;j++)
                {
                    double a=rotation+j*2.4; var d=PlantMath.Horizontal(a);
                    int rachis=b.AddAxis(stem,.12+j*.13,d+Vec3.Up*.2,r*.28*b.Range(.85,1.15),PlantMath.Horizontal(a+1.6)*b.Range(-.35,.35)+Vec3.Down*b.Range(0,.2),r*.003,r*.001,2,Tissue.Green,.1,.25);
                    for(int k=0;k<3;k++) for(int sign=-1;sign<=1;sign+=2)
                    {
                        var side=PlantMath.Horizontal(a+sign*1.1);
                        int branch=b.AddAxis(rachis,.3+k*.25,side,r*.12*b.Range(.85,1.15),PlantMath.Horizontal(a+sign*1.1+1.6)*b.Range(-.3,.3)+Vec3.Down*b.Range(0,.2),r*.0015,r*.0005,3,Tissue.Green,.2,.2);
                        for(int n=0;n<3;n++) b.AddFlat(branch,.35+n*.28,side+PlantMath.Horizontal(a)*((n-1)*.5),r*.07,r*.015,.0003,OrganType.Leaflet,p.Leaf,4,.2,.15);
                    }
                }
                for(int j=0;j<4;j++)
                {
                    double a=rotation+j*2.4; var d=PlantMath.Horizontal(a);
                    int branch=b.AddAxis(stem,.48+j*.13+b.Range(-.03,.03),d*.7+Vec3.Up,h*(.21+j*.045)+b.Range(-.03,.03),
                        PlantMath.Horizontal(a+1.6)*b.Range(-.5,.5)+d*b.Range(-.2,.4)+Vec3.Down*b.Range(0,.15),r*.0042,r*.0020,2,Tissue.Green,.4,.25);
                    for(int k=0;k<8;k++)
                    {
                        // Every secondary umbel is thrown in its own seeded direction, so
                        // no two lace plates share a plane or a height.
                        double tiltAz=b.Range(0,Math.Tau), tilt=b.Range(.18,.42);
                        var tiltDir=PlantMath.Horizontal(tiltAz);
                        var horizontal=PlantMath.Horizontal(a+k*Math.Tau/8+b.Range(-.12,.12));
                        var ray=horizontal+Vec3.Up*(.30+tiltDir.Dot(horizontal)*tilt)+tiltDir*(tilt*1.4);
                        int umbel=b.AddAxis(branch,1,ray,r*b.Range(.14,.26),
                            Vec3.Down*b.Range(.08,.30)+tiltDir*b.Range(.04,.14),r*.0026,r*.0013,3,Tissue.Green,.55,.2);
                        for(int n=0;n<5;n++)
                        {
                            // Pedicels leave the umbel at staggered stations rather than
                            // one shared tip, giving each floret its own height.
                            var tip=PlantMath.Horizontal(a+n*Math.Tau/5+b.Range(-.3,.3))+Vec3.Up*b.Range(.18,.55);
                            int ped=b.AddAxis(umbel,.55+n*.10,tip,r*b.Range(.045,.095),
                                Vec3.Down*b.Range(.05,.20),r*.0016,r*.0009,4,Tissue.Green,.65,.15);
                            b.AddFlat(ped,1,Vec3.Up,h*b.Range(.013,.021),r*b.Range(.013,.021),.0004,OrganType.Flower,p.Flower,5,.7,.12);
                        }
                    }
                }
                return true;
            }
            if(s.Id=="paddlerib")
            {
                for(int i=0;i<9;i++)
                {
                    double a=rotation+(i%2)*Math.PI+b.Range(-.55,.55); var d=PlantMath.Horizontal(a);
                    // Petioles rise from distinct feet around a small stool and arch out
                    // to their blades, rather than converging on one shared point.
                    int foot=UprightOffset(b,crown,rotation,i,9,.10);
                    int pet=b.AddAxis(foot,1,Vec3.Up+d*.5,h*b.Range(.25,.48),d*b.Range(.10,.55)-Vec3.Up*b.Range(0,.12),r*.007,r*.002,1,Tissue.Green,0,.3);
                    b.AddFlat(pet,1,Vec3.Up*b.Range(.55,1.0)+d*b.Range(.45,.9),h*b.Range(.20,.30),r*b.Range(.18,.22),.0005,OrganType.Leaf,p.Leaf,2,.12,.25);
                }
                int scape=b.AddAxis(crown,1,Vec3.Up+PlantMath.Horizontal(rotation)*.12,h*.85,PlantMath.Horizontal(rotation+1.3)*.3,r*.008,r*.002,1,Tissue.Green,.2,.45);
                for(int tier=0;tier<4;tier++) for(int k=0;k<5;k++)
                {
                    double a=rotation+k*Math.Tau/5+tier*.4+b.Range(-.28,.28); var d=PlantMath.Horizontal(a);
                    int branch=b.AddAxis(scape,Math.Clamp(.40+tier*.14+b.Range(-.05,.05),.34,.96),d+Vec3.Up*b.Range(.30,.60),
                        r*(.35-tier*.055)*b.Range(.8,1.2),Vec3.Up*.1,r*.0025,r*.0007,2,Tissue.Green,.5,.2);
                    for(int n=0;n<3;n++)
                    {
                        var ray=PlantMath.Horizontal(a+(n-1)*.6)+Vec3.Up*b.Range(.25,.55);
                        int ped=b.AddAxis(branch,.4+n*.25,ray,r*b.Range(.065,.095),Vec3.Zero,r*.001,r*.0004,3,Tissue.Green,.6,.15);
                        b.AddFlat(ped,1,Vec3.Up,h*b.Range(.022,.028),r*b.Range(.022,.028),.0004,OrganType.Flower,p.Flower,4,.7,.15);
                    }
                }
                return true;
            }
            return false;
        }
    }
}
