using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

public static partial class HerbGraphSpecies
{
    private sealed partial class Generator
    {
        private bool TryAquaticFernHabit(PlanBuilder b,int crown,double rotation,HabitPalette p)
        {
            string id=s.Id;
            if(id=="moonfern") return false;
            if(id=="snaptrap")
            {
                ushort jaw=b.AddShape(new OrganShape { Name="snaptrap_jaw", Type=OrganType.Leaf,
                    SurfaceForm=OrganSurfaceForm.Lamina,
                    WidthProfile=PlantShapes.Profile(t => .22*Math.Pow(1-t,2)+Math.Pow(Math.Sin(Math.PI*t),.60)),
                    Serration=.28f, SerrationFreq=12, CrossCurl=.035f,
                    ColorAdaxial=PlantShapes.Srgb(0.52,0.198,0.189), ColorAbaxial=PlantShapes.Srgb(.25,.43,.18), Thickness=.001f, Roughness=.50f });
                ushort trumpet=b.AddShape(new OrganShape { Name="snaptrap_trumpet", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Trumpet, LobeCount=5, LobeDepth=.18f, RimCurl=.25f, ShellThickness=.025f,
                    WidthProfile=PlantShapes.Profile(t => .10+.40*Math.Pow(t,1.5)),
                    ColorAdaxial=PlantShapes.Srgb(.68,.31,.36), ColorAbaxial=PlantShapes.Srgb(.76,.39,.43), Roughness=.42f });
                // Column, trap origins and the terminal trumpet architecture are
                // locked by plant.sheet17Habit: jaws must pair under one arm that is
                // a direct child of the column, and the trumpet must be terminal on
                // the column itself. Seat arms inside the column and taper into a shared jaw hinge.
                int column=b.AddAxis(crown,1,Vec3.Up+PlantMath.Horizontal(rotation)*.06,h*.78,PlantMath.Horizontal(rotation+1.2)*.18,r*.112,r*.052,1,Tissue.Green,0,.65,OrganFlags.Basal);
                var colPlan=b[column];
                for(int i=0;i<s.Shoots;i++)
                {
                    double a=rotation+i*2.39996+b.Range(-.25,.25);
                    // Internodes crowd toward the growing tip instead of posting
                    // traps at a constant vertical pitch.
                    double t=Math.Clamp(.10+.78*Math.Sin(i/Math.Max(1.0,s.Shoots-1.0)*Math.PI/2)+b.Range(-.03,.03),.04,.94);
                    var d=PlantMath.Horizontal(a); var side=PlantMath.Horizontal(a+Math.PI/2);
                    int arm=b.AddAxis(column,t,d+Vec3.Up*.20,r*b.Range(.05,.08),d*.15,r*.028,r*.0055,2,Tissue.Green,.12,.15);
                    var armPlan=b[arm];
                    armPlan.P0=PlantMath.Hermite(colPlan.P0,colPlan.T0,colPlan.P1,colPlan.T1,t)+d*(r*.035);
                    armPlan.P1=armPlan.P0+(armPlan.T0+armPlan.T1)*.5;
                    var armTipTan=b.TangentAt(arm,1);
                    for(int sign=-1;sign<=1;sign+=2)
                    {
                        int jawFlat=b.AddFlat(arm,1,d+side*sign*.34+Vec3.Up*.58,r*.20,r*.095,.001,OrganType.Leaf,jaw,3,.25,.25);
                        b[jawFlat].P0=armPlan.P1-armTipTan*(r*.010);
                        b[jawFlat].P1=b[jawFlat].P0+b[jawFlat].T0*(r*.20);
                    }
                }
                // A short continuous neck centers the funnel gradually on the column top.
                int neck=b.AddAxis(column,1,b.TangentAt(column,1),h*.06,Vec3.Zero,r*.052,r*.026,2,Tissue.Green,.65,.2,OrganFlags.Continuous);
                b[neck].P0=colPlan.P1;
                b[neck].R0=colPlan.R1;
                b[neck].R1=r*.026;
                b[neck].T0=b.TangentAt(column,1)*(h*.06);
                b[neck].P1=b[neck].P0+(b[neck].T0+b[neck].T1)*.5;
                int bloom=b.AddFlat(neck,1,Vec3.Up,h*.24,r*.24,.001,OrganType.Flower,trumpet,2,.65,.25);
                b[bloom].P0=b[neck].P1-Vec3.Up*(h*.006);
                b[bloom].P1=b[bloom].P0+b[bloom].T0*(h*.24);
                return true;
            }
            if(id=="fencomb")
            {
                // Flexible submerged shoots weave through space; whorled, finely
                // divided leaves follow each internode rather than forming shelves.
                var run=PlantMath.Horizontal(rotation);
                int runner=b.AddAxis(crown,0,run,r*.42,Vec3.Zero,r*.012,r*.006,1,Tissue.Green,0,.2);
                for(int i=0;i<5;i++)
                {
                    double a=rotation+i*2.39996;
                    var outward=PlantMath.Horizontal(a);
                    var side=PlantMath.Horizontal(a+Math.PI/2);
                    int previous=runner;
                    for(int section=0;section<4;section++)
                    {
                        double wave=Math.Sin(section*1.7+i*1.3);
                        var direction=section==0 ? Vec3.Up+outward*.32 : b.TangentAt(previous,1);
                        var bend=side*(wave*.85)+outward*(.28*Math.Cos(section*1.5+i)) + Vec3.Up*.12;
                        double startRadius=r*(.010-section*.002), endRadius=r*(.008-section*.002);
                        int stem=b.AddAxis(previous,section==0 ? .10+i*.19 : 1,direction,h*.24*b.Range(.85,1.12),bend,
                            startRadius,endRadius,2,Tissue.Green,.03+section*.08,.3,
                            OrganFlags.Basal | (section>0 ? OrganFlags.Continuous : OrganFlags.None));
                        for(int j=0;j<4;j++) for(int k=0;k<4;k++)
                        {
                            double u=.12+j*.25+b.Range(-.050,.050), az=a+k*Math.Tau/4+(section*4+j)*.38;
                            var radial=PlantMath.Horizontal(az);
                            double len=r*b.Range(.20,.34)*(1-.40*(section+u)/4);
                            int leaf=b.AddAxis(stem,u,radial+Vec3.Up*.16,len,side*.10,r*.0022,r*.0003,3,Tissue.Green,.12,.18);
                            for(int n=0;n<6;n++) for(int sign=-1;sign<=1;sign+=2)
                            {
                                double v=.12+n*.155;
                                var filament=PlantMath.Horizontal(az+sign*1.16)+radial*.35+Vec3.Up*.10;
                                b.AddFlat(leaf,v,filament,len*.55*(1-v*.55),r*.010,.0002,OrganType.Leaflet,p.Leaf,4,.17,.14);
                            }
                        }
                        previous=stem;
                    }
                }
                return true;
            }
            if(id=="mirepocket")
            {
                // A low, spreading runner supplies separate upright, gently wandering
                // shoots. Their flexible divided branchlets curve instead of making
                // straight spokes at angular junctions.
                var run=PlantMath.Horizontal(rotation);
                var across=PlantMath.Horizontal(rotation+Math.PI/2);
                int runner=b.AddAxis(crown,0,run,r*.95,across*1.12,r*.014,r*.008,1,Tissue.Green,0,.25,OrganFlags.Hidden);
                for(int i=0;i<8;i++)
                {
                    double t=.05+.90*i/7.0, sign=i%2==0?1:-1;
                    var side=across*sign;
                    int offset=b.AddAxis(runner,t,side+run*.10,r*b.Range(.12,.42),run*b.Range(-.20,.20),
                        r*.008,r*.005,2,Tissue.Green,0,.18,OrganFlags.Basal|OrganFlags.Hidden);
                    double a=rotation+i*2.39996;
                    var drift=PlantMath.Horizontal(a);
                    int stem=b.AddAxis(offset,1,Vec3.Up+drift*b.Range(.08,.52),h*(.42+.60*((i*5)%8)/7.0+b.Range(-.04,.04)),
                        drift*b.Range(.22,.52)+side*.12,r*.011,r*.003,2,Tissue.Green,.03,.45,OrganFlags.Basal);
                    for(int j=0;j<8;j++) for(int k=0;k<4;k++)
                    {
                        double u=Math.Clamp(.06+j*.88/7+((i%3)-1)*.026,.03,.98), az=a+k*Math.Tau/4+j*.52;
                        var radial=PlantMath.Horizontal(az);
                        var lateral=PlantMath.Horizontal(az+Math.PI/2);
                        double len=r*.34*(1-.55*u)*b.Range(.70,1.24);
                        int ray=b.AddAxis(stem,u,radial+Vec3.Up*.25,len,
                            lateral*b.Range(-.48,.48)+Vec3.Up*b.Range(.16,.42),r*.003,r*.0008,3,Tissue.Green,.1,.2);
                        for(int n=0;n<5;n++) for(int leafSide=-1;leafSide<=1;leafSide+=2)
                        {
                            double v=.2+n*.7/4;
                            var tangent=b.TangentAt(ray,v);
                            var dir=lateral*leafSide+tangent*.35+Vec3.Up*.22;
                            b.AddFlat(ray,v,dir,len*.75*(1-v*.35),r*.022,.0004,OrganType.Leaflet,p.Leaf,4,.2,.15);
                        }
                        if((j+k)%3==0)
                            b.AddFlat(ray,.7,Vec3.Up,r*.045,r*.025,.0005,OrganType.Fruit,p.Burr,4,.4,.15);
                    }
                }
                return true;
            }
            if(id is "deepquill" or "fencomb" or "mirepocket" or "silverweed" or "fenbead")
            {
                bool quill=id=="deepquill", comb=id=="fencomb", pocket=id=="mirepocket", bead=id=="fenbead";
                // A trailing parent axis distributes the erect and ascending shoots.
                // It leaves the crown at ground level; the visible silverweed stolon
                // arcs clear of the soil while the aquatic runners stay buried.
                var run=PlantMath.Horizontal(rotation); var across=PlantMath.Horizontal(rotation+Math.PI/2);
                int runner=b.AddAxis(crown,0,run,r*(quill?1.25:1.0),
                    across*b.Range(.75,1.15)+Vec3.Up*(quill||bead ? .02 : b.Range(.15,.40)),
                    quill||bead?r*.008:r*.010,r*.004,1,Tissue.Green,0,.25,quill||bead?OrganFlags.Hidden:OrganFlags.None);
                int shoots=quill?8:comb?7:pocket?6:bead?5:7;
                for(int i=0;i<shoots;i++)
                {
                    double t=.08+.84*i/Math.Max(1,shoots-1)+b.Range(-.045,.045), a=rotation+i*2.40+b.Range(-.42,.42);
                    var d=PlantMath.Horizontal(a);
                    // Index-stepped ramps guarantee unequal posture at catalog distance
                    // rather than relying on a single draw landing wide.
                    double reach=bead ? .24+.50*((i*3)%5)/4.0 : .16+.34*((i*5)%4)/3.0;
                    double height=h*((bead ? .40 : .48)+.54*((i*3)%5)/4.0+b.Range(-.05,.05));
                    int stem=b.AddAxis(runner,t,Vec3.Up+d*reach,height,d*b.Range(.18,.58),r*.014,r*.003,2,Tissue.Green,.03,.45,OrganFlags.Basal);
                    if(bead)
                    {
                        for(int j=0;j<5;j++)
                        {
                            var branchDir=PlantMath.Horizontal(a+(j%2==0 ? .55 : -.75))+Vec3.Up*.32;
                            int branch=b.AddAxis(stem,.20+j*.15,branchDir,r*b.Range(.16,.33),Vec3.Up*.12,r*.003,r*.0007,3,Tissue.Green,.15,.25);
                            for(int k=0;k<4;k++) for(int sign=-1;sign<=1;sign+=2)
                                b.AddFlat(branch,.2+k*.23,run*sign+branchDir*.3+Vec3.Up*.2,r*.075,r*.035,.0003,OrganType.Leaf,p.Leaf,4,.25,.2);
                        }
                    }
                    else if(id=="silverweed")
                    {
                        for(int j=0;j<9;j++) for(int k=0;k<3;k++)
                        {
                            double a2=rotation+i*.7+k*Math.Tau/3+j*.32;
                            b.AddFlat(stem,.10+j*.095+b.Range(-.025,.025),PlantMath.Horizontal(a2)+Vec3.Up*b.Range(.08,.62),r*.22,r*.035,.0004,OrganType.Leaf,p.Leaf,3,.15,.2);
                        }
                    }
                    else
                    {
                        int tiers=quill||comb?15:8, rays=quill||comb?8:4;
                        for(int j=0;j<tiers;j++) for(int k=0;k<rays;k++)
                        {
                            // Uneven tier spacing plus a tapered crown keep each shoot
                            // from drawing as a hard-edged ladder of equal shelves.
                            double u=Math.Clamp(.05+j*.90/Math.Max(1,tiers-1)+((j%3)-1)*.020+b.Range(-.008,.008),.03,.97), az=rotation+k*Math.Tau/rays+j*.52;
                            var radial=PlantMath.Horizontal(az);
                            double len=r*(quill?.30:comb?.38:.30)*(1-.48*u)*(quill?1-.42*Math.Pow(u,4):1)*b.Range(.72,1.18);
                            int ray=b.AddAxis(stem,u,radial+Vec3.Up*.25,len,Vec3.Zero,r*.003,r*.0008,3,Tissue.Green,.1,.2);
                            int pairs=pocket?5:4;
                            for(int n=0;n<pairs;n++) for(int sign=-1;sign<=1;sign+=2)
                            {
                                double v=.2+n*.7/Math.Max(1,pairs-1);
                                var dir=PlantMath.Horizontal(az+sign*1.0)+radial*.35+Vec3.Up*.22;
                                b.AddFlat(ray,v,dir,len*(quill?.62:.75)*(1-v*.35),r*(quill?.018:.022),.0004,OrganType.Leaflet,p.Leaf,4,.2,.15);
                            }
                            if(pocket && (j+k)%3==0)
                                b.AddFlat(ray,.7,Vec3.Up,r*.045,r*.025,.0005,OrganType.Fruit,p.Burr,4,.4,.15);
                        }
                    }
                }
                return true;
            }
            if(id is "ringreed" or "sootband")
            {
                // Both are horsetail-like: hollow, jointed, finely ridged stems whose nodes
                // carry fused toothed sheaths. Ringreed is the unbranched scouring-rush type;
                // Sootband is the bottlebrush type with whorled branchlets and a separate
                // pale fertile stem form.
                bool ring=id=="ringreed";
                ushort cone=b.AddShape(new OrganShape { Name=id+"_strobilus", Type=OrganType.Fruit,
                    SurfaceForm=OrganSurfaceForm.Revolved,
                    WidthProfile=PlantShapes.Profile(t => .5*Math.Pow(Math.Sin(Math.PI*Math.Min(1,.15+.85*t)),.55)*(1-.25*t)),
                    ColorAdaxial=ring ? PlantShapes.Srgb(.30,.25,.13) : PlantShapes.Srgb(.62,.52,.30),
                    ColorAbaxial=ring ? PlantShapes.Srgb(.10,.09,.05) : PlantShapes.Srgb(.32,.26,.14), Roughness=.8f });
                ushort tooth=b.AddShape(new OrganShape { Name=id+"_sheath_tooth", Type=OrganType.Leaf,
                    SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => 1-t),
                    ColorAdaxial=PlantShapes.Srgb(.03,.03,.025), ColorAbaxial=PlantShapes.Srgb(.05,.045,.035), Thickness=.0002f, Roughness=.74f });
                if(ring)
                {
                    // The ribbed internode name routes the compiler's longitudinal striation.
                    ushort ribbed=b.AddShape(new OrganShape { Name="ringreed_ribbed_shaft", Type=OrganType.Flower,
                        SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t => .5*(.97-.03*t)),
                        ColorAdaxial=PlantShapes.Srgb(.24,.44,.19), ColorAbaxial=PlantShapes.Srgb(0.135,0.28,0.106), Roughness=.45f });
                    ushort ash=b.AddShape(new OrganShape { Name="ringreed_ash_sheath", Type=OrganType.Flower,
                        SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t => .5*(1.0+.04*t)),
                        ColorAdaxial=PlantShapes.Srgb(.60,.60,.51), ColorAbaxial=PlantShapes.Srgb(.54,.54,.46), Roughness=.7f });
                    ushort black=b.AddShape(new OrganShape { Name="ringreed_black_ring", Type=OrganType.Flower,
                        SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t => .5*(1.04-.06*Math.Abs(2*t-1))),
                        ColorAdaxial=PlantShapes.Srgb(.025,.028,.02), ColorAbaxial=PlantShapes.Srgb(.025,.028,.02), Roughness=.35f });
                    ushort tipCap=b.AddShape(new OrganShape { Name="ringreed_rounded_tip", Type=OrganType.Flower,
                        SurfaceForm=OrganSurfaceForm.Revolved,
                        WidthProfile=PlantShapes.Profile(t => .5*Math.Sqrt(Math.Max(0,1-Math.Pow(t,2.2)))),
                        ColorAdaxial=PlantShapes.Srgb(.05,.06,.035), ColorAbaxial=PlantShapes.Srgb(.03,.035,.02), Roughness=.60f });
                    const int stems=11;
                    for(int i=0;i<stems;i++)
                    {
                        // Feet spread on an uneven ring and rods lean away from the
                        // clump centre, so the lower cluster no longer reads as one
                        // block of parallel shafts.
                        int foot=UprightOffset(b,crown,rotation,i,stems,.55*(.72+.40*((i*3)%5)/4.0));
                        var lean=PlantMath.Horizontal(rotation+i*2.39996+b.Range(-.22,.22))*b.Range(.03,.22);
                        double len=h*(.58+.42*((i*7)%11)/10.0+b.Range(-.05,.05)), rad=r*b.Range(.015,.021);
                        // A hidden straight core carries the visible ribbed internodes and sheaths.
                        int stem=b.AddAxis(foot,1,Vec3.Up+lean,len,Vec3.Zero,rad*.8,rad*.7,2,Tissue.Green,.02,.5,OrganFlags.Hidden|OrganFlags.Basal);
                        var dir=b.TangentAt(stem,0);
                        int nodes=(int)Math.Round(b.Range(7,10));
                        double prev=0;
                        for(int n=1;n<=nodes;n++)
                        {
                            // Internodes lengthen a little toward the middle of the rod.
                            double u=n/(double)nodes, tn=Math.Min(.97,u*.97+.015*Math.Sin(Math.PI*u));
                            double rr=rad*(1-.18*prev);
                            b.AddFlat(stem,prev,dir,len*(tn-prev),rr,.0006,OrganType.Flower,ribbed,3,.05,.4);
                            // Sheath: pale ash band, crowned by a black ring with fine dark teeth.
                            b.AddFlat(stem,tn-.018,dir,len*.018,rr*1.10,.0004,OrganType.Flower,ash,3,.1,.3);
                            b.AddFlat(stem,tn,dir,len*.007,rr*1.12,.0004,OrganType.Flower,black,3,.1,.3);
                            if(n<nodes && n%2==1)
                                for(int k=0;k<6;k++)
                                {
                                    var rd=PlantMath.Horizontal(rotation+k*Math.Tau/6+n*.5);
                                    b.AddFlat(stem,tn+.007,Vec3.Up*1.8+rd*.25,len*.014,rr*.38,.0002,OrganType.Leaf,tooth,4,.1,.2);
                                }
                            prev=tn;
                        }
                        // About half the rods end in a rounded spore cone, the rest in a smooth dark tip.
                        if(i%2==0) b.AddFlat(stem,.97,dir,len*.075,rad*1.25,.0006,OrganType.Fruit,cone,3,.6,.2);
                        else b.AddFlat(stem,.97,dir,len*.035,rad*.85,.0006,OrganType.Flower,tipCap,3,.6,.2);
                    }
                    return true;
                }
                ushort soot=b.AddShape(new OrganShape { Name="sootband_sheath", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t=>.5*(1+.10*t)),
                    ColorAdaxial=PlantShapes.Srgb(.045,.05,.035), ColorAbaxial=PlantShapes.Srgb(.03,.035,.025), Roughness=.6f });
                ushort paleSheath=b.AddShape(new OrganShape { Name="sootband_fertile_sheath", Type=OrganType.Flower,
                    SurfaceForm=OrganSurfaceForm.Revolved, WidthProfile=PlantShapes.Profile(t=>.5*(1+.22*Math.Pow(t,1.5))),
                    ColorAdaxial=PlantShapes.Srgb(.20,.16,.10), ColorAbaxial=PlantShapes.Srgb(.12,.10,.06), Roughness=.7f });
                // Sterile stems: gently curving, with whorls of fine jointed branchlets
                // that shorten upward into a soft spire.
                const int sterile=7;
                for(int i=0;i<sterile;i++)
                {
                    int foot=UprightOffset(b,crown,rotation,i,sterile+3,.44);
                    var lean=PlantMath.Horizontal(rotation+i*2.39996+b.Range(-.5,.5));
                    var sway=PlantMath.Horizontal(rotation+i*2.39996+Math.PI/2);
                    double len=h*(.72+.28*((i*3)%4)/3.0+b.Range(-.03,.03)), rad=r*b.Range(.016,.020);
                    int stem=b.AddAxis(foot,1,Vec3.Up+lean*b.Range(.06,.33),len,lean*b.Range(.08,.22)+sway*b.Range(-.10,.10),
                        rad,rad*.35,2,Tissue.Green,.02,.5,OrganFlags.Basal);
                    int whorls=8+(i%3);
                    for(int n=0;n<whorls;n++)
                    {
                        // Each stem runs its own whorl phase so the colony does not
                        // show level bands of branchlets aligned across every stem.
                        double t=Math.Clamp(.12+.84*n/(whorls-1)+((i%3)-1)*.035+b.Range(-.012,.012),.06,.94), u=n/(double)(whorls-1);
                        var tan=b.TangentAt(stem,t);
                        double rr=rad*(1-.62*t);
                        b.AddFlat(stem,t-.012,tan,len*.014,rr*1.18,.0004,OrganType.Flower,soot,3,.1,.3);
                        int count=7+((n+i)%3);
                        double reach=r*(.58-.40*u)*(.84+.18*((i*5)%4)/3.0)*b.Range(.9,1.08);
                        for(int k=0;k<count;k++)
                        {
                            var rd=PlantMath.Horizontal(rotation+k*Math.Tau/count+n*.37+b.Range(-.12,.12));
                            // Each branchlet rises, then relaxes outward and slightly down.
                            // Elevation cycles within the whorl instead of sharing one angle.
                            int lower=b.AddAxis(stem,t,rd+Vec3.Up*(.26+.56*((k+n)%3)/2.0),reach*.5,rd*.25,r*.0042,r*.0034,3,Tissue.Green,.2+.4*t,.3);
                            int upper=b.AddAxis(lower,1,b.TangentAt(lower,1),reach*.55,rd*.45-Vec3.Up*.35,r*.0034,r*.0012,3,Tissue.Green,.25+.4*t,.35,OrganFlags.Continuous);
                            b[upper].P0=b[lower].P1; b[upper].R0=b[lower].R1; b[upper].T0=b[lower].T1;
                        }
                    }
                }
                // Fertile stems: shorter, pale, unbranched, with loose dark sheaths and a cone.
                for(int i=0;i<3;i++)
                {
                    int foot=UprightOffset(b,crown,rotation,sterile+i,sterile+3,.40);
                    var lean=PlantMath.Horizontal(rotation+i*2.1+b.Range(-.5,.5));
                    double len=h*b.Range(.32,.45), rad=r*b.Range(.012,.015);
                    int stem=b.AddAxis(foot,1,Vec3.Up+lean*.06,len,lean*.12,rad,rad*.85,2,Tissue.Green,.02,.4,OrganFlags.Basal);
                    for(int n=0;n<4;n++)
                    {
                        double t=.18+n*.20; var tan=b.TangentAt(stem,t);
                        b.AddFlat(stem,t,tan,len*.075,rad*1.15,.0004,OrganType.Flower,paleSheath,3,.1,.3);
                    }
                    b.AddFlat(stem,1,b.TangentAt(stem,1),len*.22,rad*1.9,.0006,OrganType.Fruit,cone,3,.6,.2);
                }
                return true;
            }
            if(id is "emberbracken" or "mirrorstrap_fern" or "sceptrefern" or "swanfrond" or "veilfern" or "streamribbon")
            {
                var run=PlantMath.Horizontal(rotation); var side=PlantMath.Horizontal(rotation+Math.PI/2);
                if (id == "emberbracken")
                {
                    // The exposed scarlet rhizome reads through the airy, twice-cut
                    // fronds. Its longitudinal fluting is actual shape geometry.
                    ushort redRunner = b.AddShape(new OrganShape {
                        Name="emberbracken_runner", Type=OrganType.Flower, SurfaceForm=OrganSurfaceForm.Revolved,
                        WidthProfile=PlantShapes.Profile(t => .5 * (.86 + .14 * Math.Sin(Math.PI*t))),
                        ColorAdaxial=PlantShapes.Srgb(0.93,0.247,0.219), ColorAbaxial=PlantShapes.Srgb(0.56,0.133,0.124),
                        Roughness=.74f, Transmission=.03f
                    });
                    b[crown].Flags |= OrganFlags.Hidden;
                    var runners=new int[5];
                    int previous=crown;
                    double heading=.15;
                    for(int joint=0;joint<runners.Length;joint++)
                    {
                        // Integrate a slowly turning heading so the scarlet rhizome
                        // sweeps through an arc instead of kinking joint to joint.
                        heading=Math.Clamp(heading+.40*Math.Sin(joint*1.31+.6)+b.Range(-.10,.10),-.85,.85);
                        var direction=run*Math.Cos(heading)+side*Math.Sin(heading)+Vec3.Up*(.006+.014*Math.Sin(joint*1.9+.3));
                        double length=r*.27*b.Range(.87,1.12);
                        int segment=b.AddAxis(previous,joint==0?0:1,direction,length,Vec3.Zero,
                            r*.022,r*.020,1,Tissue.Green,0,.08,OrganFlags.Hidden|OrganFlags.Basal);
                        int cover=b.AddFlat(segment,0,direction,length*1.025,r*.022,.0005,
                            OrganType.Flower,redRunner,2,.02,.12);
                        // Each coloured internode meets the previous red internode.
                        b[cover].P0=b[segment].P0;
                        b[cover].P1=b[segment].P0+PlantMath.SafeNormal(direction,run)*(length*1.025);
                        runners[joint]=segment;previous=segment;
                    }
                    // Three uneven clusters (mature trio, young pair, two late fronds)
                    // instead of seven arches posted at a constant pitch.
                    double[] cluster={.04,.09,.155,.44,.51,.81,.885};
                    for(int i=0;i<7;i++)
                    {
                        double a=rotation+(i%2==0?1:-1)*b.Range(.72,1.18)+b.Range(-.16,.16);
                        var d=PlantMath.Horizontal(a); var lateral=PlantMath.Horizontal(a+Math.PI/2);
                        double size=(i==0||i==3||i==5) ? b.Range(.88,1.18) : b.Range(.48,.78);
                        double position=Math.Clamp(cluster[i]+b.Range(-.018,.018),.02,.98);
                        int joint=Math.Min(runners.Length-1,(int)(position*runners.Length));
                        double t=position*runners.Length-joint;
                        int inner=b.AddAxis(runners[joint],t,Vec3.Up+d*.27,h*.35*size,d*.35,
                            r*.007,r*.005,2,Tissue.Green,.03,.26,OrganFlags.Basal);
                        int middle=b.AddAxis(inner,1,b.TangentAt(inner,1),h*.34*size,d*.85-Vec3.Up*.80,
                            r*.005,r*.0027,3,Tissue.Green,.08,.24,OrganFlags.Basal|OrganFlags.Continuous);
                        int outer=b.AddAxis(middle,1,b.TangentAt(middle,1),h*.25*size,d*.20-Vec3.Up*.75,
                            r*.0027,r*.0006,4,Tissue.Green,.12,.24,OrganFlags.Basal|OrganFlags.Continuous);
                        // Expose the stalk base and taper the pinnae along the arch.
                        for(int section=0;section<3;section++)
                        {
                            int axis=section==0?inner:section==1?middle:outer;
                            int pairs=section==2?4:5;
                            for(int j=0;j<pairs;j++)
                            {
                                double at=.18+.78*j/(pairs-1.0), u=(section+at)/3;
                                double envelope=Math.Pow(Math.Sin(Math.PI*u),.72)*(1-.25*u);
                                for(int sign=-1;sign<=1;sign+=2)
                                {
                                    var pinnaDir=lateral*sign+d*.32+Vec3.Down*(.04+.2*u);
                                    double length=r*.38*size*envelope;
                                    int pinna=b.AddAxis(axis,at,pinnaDir,length,d*.12+Vec3.Down*.15,
                                        r*.0016,r*.00035,5,Tissue.Green,.13+u*.12,.16);
                                    for(int n=0;n<4;n++) for(int q=-1;q<=1;q+=2)
                                    {
                                        double v=.16+n*.24;
                                        b.AddFlat(pinna,v,d*q+pinnaDir*.30+Vec3.Down*.08,
                                            length*.36*(1-.56*v),r*.017*envelope*(1-.45*v),.00025,
                                            OrganType.Leaflet,p.Leaf,6,.18+u*.14,.15);
                                    }
                                    b.AddFlat(pinna,.92,pinnaDir,length*.27,r*.012*envelope,.0002,
                                        OrganType.Leaflet,p.Leaf,6,.21+u*.14,.14);
                                }
                            }
                        }
                    }
                    return true;
                }
                if (id == "streamribbon")
                {
                    ushort ribbon=b.AddShape(new OrganShape { Name="streamribbon_twining_leaf", Type=OrganType.Leaf,
                        SurfaceForm=OrganSurfaceForm.Lamina,
                        WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*t),.48)*(1-.38*t)),
                        MidribCurl=.62f, Twist=5.2f, TwistDepth=.04f, CrossCurl=.001f,
                        ColorAdaxial=PlantShapes.Srgb(0.311,0.51,0.202), ColorAbaxial=PlantShapes.Srgb(.43,.57,.27), Roughness=.57f });
                    int runner=b.AddAxis(crown,0,run,r*.72,side*.80,r*.012,r*.005,1,Tissue.Green,0,.16);
                    for(int g=0;g<4;g++) for(int k=0;k<3;k++)
                    {
                        double a=rotation+(k-1)*1.20+b.Range(-.38,.38)+g*.67;
                        var d=PlantMath.Horizontal(a);
                        int baseStem=b.AddAxis(runner,.08+.84*g/3.0,Vec3.Up+d*.20,h*.015,Vec3.Zero,r*.005,r*.003,2,Tissue.Green,0,.12);
                        b.AddFlat(baseStem,1,Vec3.Up+d*b.Range(.3,.85),h*b.Range(.95,1.45),r*b.Range(.040,.062),.0005,OrganType.Leaf,ribbon,3,.06,.35);
                    }
                    return true;
                }
                if (id == "swanfrond")
                {
                    ushort shrub=b.AddShape(new OrganShape { Name="swanfrond_shrub_leaf", Type=OrganType.Leaf,
                        SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*t),.55)),
                        MidribCurl=.28f, CrossCurl=.018f, ColorAdaxial=PlantShapes.Srgb(.34,.52,.25),
                        ColorAbaxial=PlantShapes.Srgb(.43,.57,.30), Roughness=.7f });
                    ushort pinna=b.AddShape(new OrganShape { Name="swanfrond_pinna", Type=OrganType.Leaflet,
                        SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*t),.7)*(1-.35*t)),
                        MidribCurl=.15f, CrossCurl=.008f, ColorAdaxial=PlantShapes.Srgb(.43,.58,.31),
                        ColorAbaxial=PlantShapes.Srgb(.53,.64,.37), Roughness=.67f });
                    // Short leafy herbaceous shoots overlap into an uneven mound.
                    for(int i=0;i<13;i++)
                    {
                        double a=rotation+i*2.39996+b.Range(-.2,.2); var d=PlantMath.Horizontal(a);
                        double reach=r*b.Range(.08,.34), height=h*b.Range(.23,.43);
                        int foot=b.AddAxis(crown,0,d,Math.Max(.001,reach),Vec3.Zero,r*.006,r*.004,1,Tissue.Green,0,.08,OrganFlags.Hidden);
                        int shoot=b.AddAxis(foot,1,Vec3.Up+d*b.Range(.15,.48),height,d*b.Range(-.1,.3),r*.008,r*.002,2,Tissue.Green,.03,.22,OrganFlags.Basal);
                        for(int j=0;j<5;j++)
                        {
                            double angle=a+j*2.39996; var leafDir=PlantMath.Horizontal(angle);
                            b.AddFlat(shoot,.26+j*.16,leafDir+Vec3.Up*b.Range(.18,.55),r*b.Range(.13,.24),r*b.Range(.065,.10),.0004,OrganType.Leaf,shrub,3,.09,.2);
                        }
                    }
                    // One rachis arches out on one side, with paired overlapping pinnae.
                    // A tall, long frond rises well above the shrub before arching over.
                    int inner=b.AddAxis(crown,1,Vec3.Up+run*.25,h*.66,run*.30,r*.013,r*.010,1,Tissue.Green,.02,.20,OrganFlags.Basal);
                    int middle=b.AddAxis(inner,1,b.TangentAt(inner,1),h*.66,run*.85-Vec3.Up*.55,r*.010,r*.006,2,Tissue.Green,.08,.20,OrganFlags.Basal|OrganFlags.Continuous);
                    int outer=b.AddAxis(middle,1,b.TangentAt(middle,1),h*.52,run*.25-Vec3.Up*1.9,r*.006,r*.001,3,Tissue.Green,.14,.20,OrganFlags.Basal|OrganFlags.Continuous);
                    int[] rachis={inner,middle,outer};
                    for(int section=0;section<3;section++) for(int j=0;j<8;j++)
                    {
                        // Uneven leaflet intervals and a slight near/far length
                        // asymmetry keep the arch from reading as a stamped comb.
                        double t=Math.Clamp(.15+j*.098+(j%2)*.024+((j%3)-1)*.012+b.Range(-.012,.012),.10,.96), u=(section+t)/3;
                        double envelope=Math.Pow(Math.Sin(Math.PI*u),.65)*(1-.25*u);
                        for(int sign=-1;sign<=1;sign+=2)
                            b.AddFlat(rachis[section],t,side*sign+run*.28+Vec3.Down*(.08+.2*u),
                                r*(sign>0 ? .44 : .40)*envelope*(1-.10*((j%3)-1)),r*.065*envelope,.0003,OrganType.Leaflet,pinna,4,.12+u*.12,.16);
                    }
                    return true;
                }
                // The rhizome is buried and gently curved; fronds are posted at
                // uneven stations so their bases never line up as an exposed rail.
                int rhizome=b.AddAxis(crown,0,run,r*.85,side*.85,r*.014,r*.006,1,Tissue.Green,0,.2,OrganFlags.Hidden);
                if(id=="streamribbon" || id=="mirrorstrap_fern")
                {
                    bool ribbon=id=="streamribbon";
                    int groups=ribbon?5:3, count=ribbon?4:3;
                    // Uneven stations along the buried rhizome plus per-blade lean and
                    // age break the regularly posted row of a wire rack.
                    double[] stations=ribbon?new[]{.06,.24,.55,.72,.93}:new[]{.08,.42,.88};
                    for(int g=0;g<groups;g++) for(int k=0;k<count;k++)
                    {
                        double a=rotation+(k-(count-1)*.5)*(ribbon?.65:.55)+(g%2)*.3+b.Range(-.30,.30); var d=PlantMath.Horizontal(a);
                        int stalk=b.AddAxis(rhizome,Math.Clamp(stations[g]+b.Range(-.03,.03),.04,.96),d*.18+Vec3.Up,h*.08,Vec3.Zero,r*.005,r*.002,2,Tissue.Green,0,.15);
                        b.AddFlat(stalk,1,Vec3.Up+d*(ribbon?.28:.48+.30*((g+k)%3)/2.0),
                            h*(.78+.52*((g*2+k)%3)/2.0),r*(ribbon?.065:.18+.05*((g+k)%2)),.0005,OrganType.Leaf,p.Leaf,3,.1,.4);
                    }
                    return true;
                }
                if(id=="veilfern")
                {
                    ushort pinna=b.AddShape(new OrganShape { Name="veilfern_pinna", Type=OrganType.Leaflet,
                        SurfaceForm=OrganSurfaceForm.Lamina, WidthProfile=PlantShapes.Profile(t => Math.Pow(Math.Sin(Math.PI*t),.8)),
                        MidribCurl=.08f, ColorAdaxial=PlantShapes.Srgb(.22,.43,.20), ColorAbaxial=PlantShapes.Srgb(.32,.46,.25), Thickness=.0004f, Roughness=.66f });
                    // Twelve joined rachis sections retain an ascending base, outward
                    // shoulder and descending tip instead of averaging away the arch.
                    for(int i=0;i<12;i++)
                    {
                        double a=rotation+i*Math.Tau/12+b.Range(-.34,.34);
                        var d=PlantMath.Horizontal(a); var lateral=PlantMath.Horizontal(a+Math.PI/2);
                        // Age varies by station: young fronds stand higher and shorter,
                        // old ones reach farther and droop; the spread opens gaps.
                        double scale=.60+.58*((i*5)%12)/11.0+b.Range(-.05,.05);
                        double lift=.30+.26*((i*2)%3)/2.0;
                        int inner=b.AddAxis(rhizome,Math.Clamp(.06+.88*i/11.0+b.Range(-.02,.02),.03,.97),
                            Vec3.Up+d*(.35+.35*lift),h*.36*scale,d*.25,r*.012,r*.009,2,Tissue.Green,0,.35,OrganFlags.Basal);
                        int middle=b.AddAxis(inner,1,d+Vec3.Up*(.28-.24*lift),h*.37*scale,d*.2-Vec3.Up*.55,r*.009,r*.006,3,Tissue.Green,.1,.35,OrganFlags.Basal);
                        int outer=b.AddAxis(middle,1,d*.75+Vec3.Down*(.55+.45*(1-lift)),h*.38*scale,Vec3.Down*.35,r*.006,r*.002,4,Tissue.Green,.2,.35,OrganFlags.Basal);
                        for(int section=0;section<3;section++)
                        {
                            int axis=section==0?inner:section==1?middle:outer;
                            int pairs=section==0?5:8;
                            for(int j=0;j<pairs;j++)
                            {
                                double t=.14+.80*j/(pairs-1.0);
                                double u=(section+t)/3.0;
                                double env=Math.Pow(Math.Sin(Math.PI*u),.7);
                                for(int sign=-1;sign<=1;sign+=2)
                                {
                                    var dir=lateral*sign+d*.22+Vec3.Down*(.15+.35*u);
                                    b.AddFlat(axis,t,dir,r*.42*env,r*.034*env,.0004,OrganType.Leaflet,pinna,5,.12+u*.2,.18);
                                }
                            }
                        }
                    }
                    return true;
                }
                int fronds=id=="emberbracken"?8:id=="sceptrefern"?8:id=="swanfrond"?12:12;
                // Uneven station pitch along the buried rhizome: fronds cluster in
                // twos and threes with open gaps between the groups.
                double[] sceptreStations={.06,.13,.31,.47,.54,.72,.87,.93};
                for(int i=0;i<fronds;i++)
                {
                    bool bracken=id=="emberbracken", sceptre=id=="sceptrefern", swan=id=="swanfrond";
                    double a=bracken ? rotation+(i%2==0?1:-1)*1.0+.18*(i%3-1)
                        : sceptre ? rotation+i*Math.Tau/fronds+(i%3-1)*.18+b.Range(-.22,.22)
                        : rotation+(i%2==0?1:-1)*(swan?.9:1.25)+.18*(i%3-1);
                    var d=PlantMath.Horizontal(a); var lateral=PlantMath.Horizontal(a+Math.PI/2);
                    double pos=Math.Clamp((bracken?.08+.84*i/(fronds-1):sceptre?sceptreStations[i%8]:.18+.64*(i%4)/3.0)+b.Range(-.05,.05),.04,.96);
                    double tilt=sceptre ? .10+.40*((i*3)%5)/4.0 : (swan ? .25 : .35);
                    int stalk=b.AddAxis(rhizome,pos,Vec3.Up+d*tilt,
                        h*(sceptre ? .62+.38*((i*5)%7)/6.0+b.Range(-.03,.03) : b.Range(.65,1.0)),
                        d*(swan?.6:.35)-Vec3.Up*(sceptre?(.06+.12*((i%3)/2)):.25),r*.012,r*.002,2,Tissue.Green,0,.45);
                    int pairs=bracken?10:sceptre?8:swan?15:16;
                    for(int j=0;j<pairs;j++)
                    {
                        double t=Math.Clamp(.15+.80*j/Math.Max(1,pairs-1)+b.Range(-.025,.025),.08,.97);
                        double env=bracken?1-.78*t:Math.Pow(Math.Sin(Math.PI*t),.75);
                        for(int sign=-1;sign<=1;sign+=2)
                        {
                            var dir=lateral*sign+d*.25+Vec3.Up*(sceptre?(.10+.14*((i%3)/2)):-.08);
                            if(bracken)
                            {
                                int pinna=b.AddAxis(stalk,t,dir,r*.48*env,Vec3.Zero,r*.003,r*.0007,3,Tissue.Green,.12,.2);
                                for(int n=0;n<4;n++) for(int q=-1;q<=1;q+=2)
                                    b.AddFlat(pinna,.18+n*.23,d*q+dir*.35,r*.14*env*(1-n*.12),r*.045*env,.0004,OrganType.Leaflet,p.Leaf,4,.2,.18);
                            }
                            else b.AddFlat(stalk,t,dir,r*(sceptre?.36:.48)*env,r*s.Width*env,.0004,OrganType.Leaflet,p.Leaf,3,.13+t*.18,.18);
                        }
                    }
                }
                return true;
            }
            return false;
        }
    }
}
