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
    private sealed record Form(string Id, string Habit, int Trunks, int Tiers, int Branches, int Nodes,
        double CrownBase, double Spread, double Rise, double Droop, double LeafLength,
        double LeafWidth, int Arrangement, int Ornament, bool Needle = false, bool Climber = false);
    private static readonly Form[] Forms = {
        new("banksorrow","leaning riverside curtain",1,5,4,8,.33,1.25,.20,.85,.13,.14,0,0),
        new("bitterbine","coarse twining cone vine",2,3,3,7,.10,.75,.8,.25,.13,.38,1,2,false,true),
        new("clinglace","overlapping clinging plates",1,3,2,9,.05,.9,.35,.12,.075,.48,2,0,false,true),
        new("cragparasol","crooked terminal umbrellas",2,3,3,5,.55,1.3,.25,.15,.16,.22,3,0),
        new("embercrown","ascending clonal lantern thicket",7,3,3,6,.20,.65,.85,.18,.12,.20,1,2),
        new("fenforge","wide leaning bank tree",2,5,4,6,.30,1.1,.4,.42,.10,.28,0,2),
        new("fenhook","hooked wetland scramble",3,3,2,6,.05,1.1,.35,.65,.10,.3,0,0,false,true),
        new("glowbriar","arching luminous canes",6,4,2,5,.15,.9,.7,.5,.15,.3,1,1),
        new("greywatch","ascending needle sprays",3,5,4,8,.20,.8,.8,.12,.045,.05,2,2,true),
        new("hollowcrown","pollarded veteran with compact shoots",3,3,5,7,.67,.85,.55,.12,.13,.38,0,0),
        new("hookthicket","interlaced rooting canes",8,3,3,7,.08,1.1,.65,.8,.09,.26,1,0),
        new("kinkcane","tall bare canes with palmate tops",9,1,5,4,.88,1.0,.9,.10,.24,.2,3,0),
        new("lacemantle","horizontal rotating branch shelves",1,5,3,7,.25,1.3,.12,.08,.09,.18,1,0),
        new("lanternbrush","bowed wetland lanterns",6,4,3,5,.16,.9,.7,.55,.16,.4,0,2),
        new("miremyrtle","fine aromatic catkin shrub",7,3,3,8,.12,.85,.55,.35,.065,.20,2,2),
        new("mirespire","narrow tiered conifer",1,8,5,8,.18,.65,.28,.1,.05,.05,2,2,true),
        new("moonbark","airy high pioneer crown",1,5,4,5,.48,1.1,.55,.18,.10,.35,0,0),
        new("nutglade","coppice with broad scalloped leaves",6,4,3,5,.22,.9,.65,.18,.15,.46,0,2),
        new("rainshield","open stems with broad leaf platforms",11,1,1,1,.98,1.05,.95,.04,.31,.48,3,0),
        new("shadebell","tiered broad leaves and pendant bells",5,4,3,5,.2,.9,.7,.25,.17,.4,1,1),
        new("shadewrap","layered lobed trunk wrap",1,3,2,8,.05,.75,.5,.12,.11,.48,1,0,false,true),
        new("shiverleaf","slender trembling round crown",3,5,4,5,.45,.9,.7,.2,.10,.47,0,0),
        new("silverfelt","low dense felted mound",8,3,3,7,.08,1.0,.45,.45,.055,.32,1,2),
        new("spiralvine","narrow-leaved searching spiral",1,4,2,7,.05,1.25,.7,.15,.12,.13,0,0,false,true),
        new("splitcrown","separate high forked crowns",3,3,4,6,.58,1.2,.5,.12,.10,.2,1,0),
        new("thornmantle","spiny blossom thicket",7,4,3,6,.12,1.0,.6,.3,.085,.3,0,1),
        new("trumpetbind","opposite leaves and hanging trumpets",2,4,2,6,.05,.8,.65,.2,.11,.36,1,1,false,true),
        new("vaultfern","elevated arching divided fronds",1,1,12,10,.96,1.25,.2,.65,.095,.16,1,0),
        new("witchberry","feathered spreading berry crown",2,5,4,7,.38,1.0,.55,.25,.095,.19,1,2),
    };
    public static IReadOnlyList<GraphSpeciesInfo> Species { get; } = Array.AsReadOnly(Forms.Select(f =>
        new GraphSpeciesInfo(f.Id, f.Climber ? "climber" : "woody", f.Habit,
            f.Climber ? "Searches and follows available support with foliage along stems" : $"{f.Trunks} basal axes, {f.Tiers} crown tiers, {f.Nodes} foliage nodes", f.Climber)).ToArray());
    public static PlanGenerator? Create(FloraSpeciesDef species)
    {
        var f = Array.Find(Forms, x => x.Id == species.Id);
        if (f == null) return null;
        if (f.Id == "glowbriar") return new GlowbriarGenerator();
        if (f.Id == "shadebell") return new ShadebellGenerator();
        return new WoodyGenerator(f, species);
    }
    private static ushort Leaf(PlanBuilder b, Form f) => b.AddShape(new OrganShape {
        Name = f.Id + "_leaf", Type = OrganType.Leaf,
        SurfaceForm = f.Needle ? OrganSurfaceForm.Needle : OrganSurfaceForm.Lamina,
        WidthProfile = PlantShapes.Profile(t => .5 * Math.Pow(Math.Sin(Math.PI * Math.Pow(t, f.LeafWidth > .4 ? .8 : 1.1)), .8)),
        MidribCurl = (float)(f.Droop * .25), CrossCurl = .12f,
        ColorAdaxial = PlantShapes.Srgb(.13,.32,.14), ColorAbaxial = PlantShapes.Srgb(.28,.46,.24),
        Roughness = .65f, Transmission = .32f
    });
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
        protected override void Design(PlanBuilder b)
        {
            ushort leaf=Leaf(b,f), ornament=Ornament(b,f);
            for (int c=0;c<f.Trunks;c++) {
                double az = c*Math.Tau/f.Trunks + b.Range(-.18,.18);
                var h = PlantMath.Horizontal(az);
                double trunkLen=height * (f.Climber ? .7 : f.CrownBase + (1-f.CrownBase)*.55) * b.Range(.84,1.08);
                var dir=Vec3.Up * (f.Climber ? .45 : 1) + h * (f.Trunks>1 ? .14 : .06);
                int trunk=b.AddAxis(-1,0,dir,trunkLen,h*.08,Math.Max(.003,height*.025/f.Trunks),.003,0,Tissue.Wood,0,.28);
                for(int k=0;k<f.Tiers;k++) for(int j=0;j<f.Branches;j++) {
                    double a=az+j*Math.Tau/f.Branches+k*2.39996+b.Range(-.12,.12);
                    var outward=PlantMath.Horizontal(a);
                    double t=f.CrownBase+(1-f.CrownBase)*(k+.5)/f.Tiers;
                    double reach=radius*f.Spread*(.55+.45*(1.0-k/(double)Math.Max(1,f.Tiers)))*b.Range(.75,1.15);
                    int branch=b.AddAxis(trunk,Math.Clamp(t,.03,.99),outward+Vec3.Up*f.Rise,reach,Vec3.Down*f.Droop,.009,.0015,1,Tissue.Wood,.05,.22);
                    if(f.Id=="vaultfern")
                    {
                        // A frond has long paired pinnae across a rachis, rather
                        // than tiny tree leaves scattered around a bare spoke.
                        var side=PlantMath.Horizontal(a+Math.PI/2);
                        for(int n=0;n<f.Nodes;n++)
                        {
                            double nt=.12+.84*n/(f.Nodes-1.0);
                            double envelope=Math.Pow(Math.Sin(Math.PI*nt),.75);
                            double pinna=Math.Min(.52,radius*.32)*envelope;
                            for(int sign=-1;sign<=1;sign+=2)
                                b.AddFlat(branch,nt,side*sign+outward*.2+Vec3.Down*.12,pinna,pinna*.12,.0004,
                                    OrganType.Leaflet,leaf,2,.12+nt*.12,.18);
                        }
                        continue;
                    }
                    for(int n=0;n<f.Nodes;n++) {
                        double nt=.15+.8*(n+.5)/f.Nodes;
                        int leaves=f.Arrangement==1?2:f.Arrangement==3?3:1;
                        for(int q=0;q<leaves;q++) {
                            double la=a+(f.Arrangement==2?n*2.39996:n*Math.PI/2)+q*Math.Tau/leaves;
                            var ld=PlantMath.Horizontal(la)*.9+Vec3.Up*(f.Id=="vaultfern"?-.25:.35);
                            double ll=Math.Min(height*.22, f.LeafLength)*b.Range(.82,1.18);
                            b.AddFlat(branch,nt,ld,ll,ll*f.LeafWidth,.0004,OrganType.Leaf,leaf,2,.1,.16);
                        }
                    }
                    if(f.Ornament>0) b.AddFlat(branch,.82,Vec3.Down+outward*.3,Math.Min(.07,height*.05),Math.Min(.025,height*.02),.001,
                        f.Ornament==1?OrganType.Flower:OrganType.Fruit,ornament,2,.2,.18);
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
                int pairs=f.Arrangement==1?2:1;
                for(int q=0;q<pairs;q++) {
                    var d=normal*.4+side*((n+q)%2==0?1:-1)+Vec3.Up*.2;
                    b.AddFlat(axis,t,d,f.LeafLength,f.LeafLength*f.LeafWidth,.0004,OrganType.Leaf,leaf,1,0,.12);
                }
                if(f.Ornament>0 && n%3==1) b.AddFlat(axis,t,Vec3.Down+normal*.3,.04,.015,.001,
                    f.Ornament==1?OrganType.Flower:OrganType.Fruit,ornament,1,.05,.12);
            }
        }
        var plan=b.Finish(species.Id);
        var dsc=new PlantDescriptor { PlantId=id,WorldSeed=seed,SpeciesId=species.Id,StructureVersion=1,
            Shapes=plan.Shapes,Organs=PlantGrowth.Materialize(plan,Math.Clamp(age,0,1),.5) };
        for(int i=0;i<dsc.Organs.Length;i++) { var o=dsc.Organs[i]; o.Health=(float)Math.Clamp(health,0,1); dsc.Organs[i]=o; }
        PlanGenerator.Summarize(dsc); return dsc;
    }
}
