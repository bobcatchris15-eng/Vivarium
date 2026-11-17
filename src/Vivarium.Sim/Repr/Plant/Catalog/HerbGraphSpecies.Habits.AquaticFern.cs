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
                int column=b.AddAxis(crown,1,Vec3.Up,h*.80,Vec3.Zero,r*.055,r*.025,1,Tissue.Green,0,.7);
                for(int i=0;i<s.Shoots;i++)
                {
                    double a=rotation+i*2.39996,t=.12+.75*i/Math.Max(1,s.Shoots-1);
                    var d=PlantMath.Horizontal(a); var side=PlantMath.Horizontal(a+Math.PI/2);
                    int arm=b.AddAxis(column,t,d+Vec3.Up*.25,r*.34,Vec3.Up*.1,r*.018,r*.006,2,Tissue.Green,.2,.3);
                    for(int sign=-1;sign<=1;sign+=2)
                        b.AddFlat(arm,1,d+side*sign*.65+Vec3.Up*.32,r*.31,r*.12,.001,OrganType.Leaf,p.Trap,3,.4,.25);
                }
                b.AddFlat(column,1,Vec3.Up,h*.25,r*.22,.001,OrganType.Flower,p.Pitcher,2,.75,.2);
                return true;
            }
            if(id is "deepquill" or "fencomb" or "mirepocket" or "silverweed" or "fenbead")
            {
                bool quill=id=="deepquill", comb=id=="fencomb", pocket=id=="mirepocket", bead=id=="fenbead";
                // A trailing parent axis distributes the erect and ascending shoots.
                var run=PlantMath.Horizontal(rotation); var across=PlantMath.Horizontal(rotation+Math.PI/2);
                int runner=b.AddAxis(crown,1,run+Vec3.Up*.06,r*.78,Vec3.Up*.04,r*.012,r*.004,1,Tissue.Green,0,.25);
                int shoots=quill?8:comb?7:pocket?6:bead?5:7;
                for(int i=0;i<shoots;i++)
                {
                    double t=.08+.86*i/Math.Max(1,shoots-1), a=rotation+(i%2==0?1:-1)*.8;
                    var d=PlantMath.Horizontal(a);
                    int stem=b.AddAxis(runner,t,Vec3.Up+d*(bead?.48:pocket?.5:.12),h*b.Range(.70,1.02),d*.12,r*.014,r*.003,2,Tissue.Green,.03,.45,OrganFlags.Basal);
                    if(bead)
                    {
                        for(int j=0;j<5;j++)
                        {
                            var branchDir=across*(j%2==0?1:-1)+run*.28+Vec3.Up*.32;
                            int branch=b.AddAxis(stem,.20+j*.15,branchDir,r*.25,Vec3.Up*.12,r*.003,r*.0007,3,Tissue.Green,.15,.25);
                            for(int k=0;k<4;k++) for(int sign=-1;sign<=1;sign+=2)
                                b.AddFlat(branch,.2+k*.23,run*sign+branchDir*.3+Vec3.Up*.2,r*.075,r*.035,.0003,OrganType.Leaf,p.Leaf,4,.25,.2);
                        }
                    }
                    else if(id=="silverweed")
                    {
                        for(int j=0;j<9;j++) for(int k=0;k<3;k++)
                        {
                            double a2=rotation+k*Math.Tau/3+j*.32;
                            b.AddFlat(stem,.10+j*.095,PlantMath.Horizontal(a2)+Vec3.Up*.3,r*.22,r*.035,.0004,OrganType.Leaf,p.Leaf,3,.15,.2);
                        }
                    }
                    else
                    {
                        int tiers=quill||comb?15:8, rays=quill||comb?8:4;
                        for(int j=0;j<tiers;j++) for(int k=0;k<rays;k++)
                        {
                            double u=.08+j*.86/Math.Max(1,tiers-1), az=rotation+k*Math.Tau/rays+j*.52;
                            var radial=PlantMath.Horizontal(az);
                            double len=r*(quill?.25:comb?.38:.30)*(comb?1-.70*Math.Clamp((u-.20)/.71,0,1):1);
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
                bool ring=id=="ringreed"; var run=PlantMath.Horizontal(rotation);
                int rhizome=b.AddAxis(crown,1,run,r*.65,Vec3.Zero,r*.018,r*.01,1,Tissue.Green,0,.2);
                if(ring)
                {
                    // Separate jointed canes arise along the creeping rhizome.
                    for(int i=0;i<7;i++)
                    {
                        double a=rotation+i*2.39996;
                        var d=PlantMath.Horizontal(a);
                        int offset=b.AddAxis(rhizome,.08+.84*i/6.0,d,r*.08,Vec3.Zero,r*.009,r*.006,2,Tissue.Green,0,.15);
                        int cane=b.AddAxis(offset,1,Vec3.Up+d*.025,h*b.Range(.78,1.05),Vec3.Zero,r*.018,r*.014,3,Tissue.Green,.02,.48,OrganFlags.Basal);
                        for(int j=0;j<6;j++)
                        {
                            double t=.12+j*.125;
                            // Short dark cylinders make the ring nodes visible around the shaft.
                            b.AddAxis(cane,t,Vec3.Up,h*.018,Vec3.Zero,r*.018,r*.018,4,Tissue.Bark,.08,.2,OrganFlags.Basal);
                            for(int k=0;k<7;k++)
                            {
                                var ray=PlantMath.Horizontal(a+k*Math.Tau/7+j*.23);
                                b.AddFlat(cane,t,ray+Vec3.Down*.55,r*.20,r*.006,.0003,OrganType.Leaf,p.Leaf,4,.12,.2);
                            }
                        }
                        b.AddFlat(cane,1,Vec3.Up,h*.12,r*.035,.001,OrganType.Flower,p.Burr,4,.65,.18);
                    }
                    return true;
                }
                int count=ring?12:20;
                for(int i=0;i<count;i++)
                {
                    double a=rotation+i*2.39996; var d=PlantMath.Horizontal(a);
                    int offset=b.AddAxis(rhizome,.05+.9*(i%7)/6.0,d,r*(ring?.12:.075),Vec3.Zero,r*.006,r*.003,2,Tissue.Green,0,.15);
                    int culm=b.AddAxis(offset,1,Vec3.Up+d*.06,h*b.Range(.7,1.05),d*.04,r*.012,r*.004,3,Tissue.Green,.02,.48,OrganFlags.Basal);
                    for(int j=0;j<(ring?6:5);j++) for(int k=0;k<8;k++)
                    {
                        double t=.15+j*.13; var ray=PlantMath.Horizontal(a+k*Math.Tau/8);
                        b.AddFlat(culm,t,ray+Vec3.Up*.25,r*.035,r*.009,.0004,OrganType.Leaf,p.Spike,4,.15,.15);
                        if(ring&&k%2==0) b.AddFlat(culm,t,ray+Vec3.Down*.35,r*.19,r*.012,.0004,OrganType.Leaf,p.Leaf,4,.2,.2);
                    }
                    if(!ring&&i%3==0) b.AddFlat(culm,1,Vec3.Up,h*.10,r*.025,.001,OrganType.Flower,p.Burr,4,.7,.15);
                }
                return true;
            }
            if(id is "emberbracken" or "mirrorstrap_fern" or "sceptrefern" or "swanfrond" or "veilfern" or "streamribbon")
            {
                var run=PlantMath.Horizontal(rotation); var side=PlantMath.Horizontal(rotation+Math.PI/2);
                int rhizome=b.AddAxis(crown,1,run,r*.72,Vec3.Zero,r*.014,r*.006,1,Tissue.Green,0,.2);
                if(id=="streamribbon" || id=="mirrorstrap_fern")
                {
                    int groups=id=="streamribbon"?5:3, count=id=="streamribbon"?4:3;
                    for(int g=0;g<groups;g++) for(int k=0;k<count;k++)
                    {
                        double a=rotation+(k-(count-1)*.5)*.65+(g%2)*.3; var d=PlantMath.Horizontal(a);
                        int stalk=b.AddAxis(rhizome,.08+.84*g/Math.Max(1,groups-1),d*.18+Vec3.Up,h*.08,Vec3.Zero,r*.005,r*.002,2,Tissue.Green,0,.15);
                        b.AddFlat(stalk,1,Vec3.Up+d*(id=="streamribbon"?.28:.6),h*b.Range(.8,1.25),r*(id=="streamribbon"?.065:.20),.0005,OrganType.Leaf,p.Leaf,3,.1,.4);
                    }
                    return true;
                }
                if(id=="veilfern")
                {
                    // Three joined rachis sections retain an ascending base, outward
                    // shoulder and descending tip instead of averaging away the arch.
                    for(int i=0;i<12;i++)
                    {
                        double a=rotation+i*Math.Tau/12;
                        var d=PlantMath.Horizontal(a); var lateral=PlantMath.Horizontal(a+Math.PI/2);
                        double scale=b.Range(.88,1.12);
                        int inner=b.AddAxis(rhizome,.2+.6*(i%3)/2.0,Vec3.Up+d*.35,h*.36*scale,d*.25,r*.012,r*.009,2,Tissue.Green,0,.35,OrganFlags.Basal);
                        int middle=b.AddAxis(inner,1,d+Vec3.Up*.28,h*.37*scale,d*.2-Vec3.Up*.55,r*.009,r*.006,3,Tissue.Green,.1,.35,OrganFlags.Basal);
                        int outer=b.AddAxis(middle,1,d*.75+Vec3.Down*.65,h*.38*scale,Vec3.Down*.35,r*.006,r*.002,4,Tissue.Green,.2,.35,OrganFlags.Basal);
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
                                    b.AddFlat(axis,t,dir,r*.42*env,r*s.Width*env,.0004,OrganType.Leaflet,p.Leaf,5,.12+u*.2,.18);
                                }
                            }
                        }
                    }
                    return true;
                }
                int fronds=id=="emberbracken"?8:id=="sceptrefern"?8:id=="swanfrond"?12:12;
                for(int i=0;i<fronds;i++)
                {
                    bool bracken=id=="emberbracken", sceptre=id=="sceptrefern", swan=id=="swanfrond";
                    double a=rotation+(i%2==0?1:-1)*(bracken?1.0:swan?.9:1.25)+.18*(i%3-1);
                    var d=PlantMath.Horizontal(a); var lateral=PlantMath.Horizontal(a+Math.PI/2);
                    double pos=bracken?.08+.84*i/(fronds-1):.18+.64*(i%4)/3.0;
                    int stalk=b.AddAxis(rhizome,pos,Vec3.Up+d*(bracken?.35:sceptre?.12:.25),h*b.Range(.65,1.0),d*(swan?.6:.35)-Vec3.Up*(sceptre?.05:.25),r*.012,r*.002,2,Tissue.Green,0,.45);
                    int pairs=bracken?10:sceptre?8:swan?15:16;
                    for(int j=0;j<pairs;j++)
                    {
                        double t=.15+.80*j/Math.Max(1,pairs-1);
                        double env=bracken?1-.78*t:Math.Pow(Math.Sin(Math.PI*t),.75);
                        for(int sign=-1;sign<=1;sign+=2)
                        {
                            var dir=lateral*sign+d*.25+Vec3.Up*(sceptre?.16:-.08);
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
