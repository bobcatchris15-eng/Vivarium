using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// A deliberately spectral fungal anatomy distinct from the conventional Dewbonnet troop.
/// Each living bonnet owns its stipe, thin asymmetric bell, gill membrane, projecting
/// lamellae, and occasional torn veil remnants as one LOD-atomic structural span.
/// Unit-scale mesh; the world renderer applies species radius/height afterward.
/// </summary>
internal static class GhostbonnetGeometry
{
    public static void Build(MeshData mesh,Rng rng,double[] body,double[] highlight,bool juvenile)
    {
        double scale=juvenile?.71:1.0;
        double lod=mesh.Lod?.LeafFraction??1.0;
        int count=juvenile?8+rng.NextInt(4):12+rng.NextInt(5);
        if(mesh.Lod!=null)count=Math.Max(3,(int)Math.Round(count*lod));
        int detail=mesh.FloraDetailLevel??0;
        int around=detail==2?14:detail==1?20:28;
        int rings=detail==2?6:detail==1?8:11;
        if(mesh.Lod!=null)
        {
            around=Math.Max(12,(int)Math.Round(around*(.58+.42*(mesh.Lod.Radial))));
            rings=Math.Max(5,(int)Math.Round(rings*(.65+.35*mesh.Lod.Length)));
        }
        // Damp mycelium remains near the substrate, not a large opaque felt mat.
        for(int cluster=0;cluster<3;cluster++)
        {
            double a=cluster*Math.Tau/3+.35;
            var foot=new Vec3(Math.Cos(a)*.18,.003,Math.Sin(a)*.18)*scale;
            for(int strand=0;strand<3;strand++)
            {
                double angle=a+strand*1.7+rng.Range(-.2,.2);
                var end=foot+new Vec3(Math.Cos(angle)*.075,-.004,Math.Sin(angle)*.075)*scale;
                Primitives.Tube(mesh,new[]{foot,Vec3.Lerp(foot,end,.52),end},
                    new[]{.0022*scale,.0014*scale,.0005*scale},6,
                    (i,v)=>(Primitives.Mix(body,highlight,.38),1,i*.045,v,0,0));
            }
        }
        for(int k=0;k<count;k++)
        {
            int first=mesh.VertexCount;
            // Wide broken crescents of isolated bonnets, with open gaps between
            // them, rather than forty interpenetrating identical parasols.
            int cl=k%3;
            double clusterAngle=cl*Math.Tau/3 + rng.Range(-.40,.40);
            double baseR=(cl==1?.49:cl==2?.51:.44)*scale;
            var group=new Vec3(Math.Cos(clusterAngle)*baseR,0,
                               Math.Sin(clusterAngle)*baseR);
            double rootAngle=rng.Range(0,Math.Tau);
            double scatter=Math.Sqrt(rng.NextDouble())*.18*scale;
            var root=group+new Vec3(Math.Cos(rootAngle)*scatter,0,
                                    Math.Sin(rootAngle)*scatter);
            int stage;
            double stageRoll=rng.NextDouble();
            if(stageRoll<.16)stage=0; // closed spectral teardrop
            else if(stageRoll<.74)stage=1; // long, drooping bell
            else stage=2; // torn, partly opening veil
            double h=scale*(stage==0?rng.Range(.26,.49):
                stage==1?rng.Range(.56,.83):rng.Range(.59,.90));
            double capR=scale*(stage==0?rng.Range(.060,.092):
                stage==1?rng.Range(.105,.155):rng.Range(.135,.185));
            // Two or three exceptional tall fruiting bodies break the roofline.
            if(!juvenile && k%7==0)h*=1.09;
            double lean=rng.Range(.055,.23)*scale;
            double swayAngle=rng.Range(0,Math.Tau);
            var leanVec=new Vec3(Math.Cos(swayAngle)*lean,0,Math.Sin(swayAngle)*lean);
            var mid=root+leanVec*.29+Vec3.Up*h*.48+
                new Vec3(rng.Range(-.025,.025),0,rng.Range(-.025,.025))*scale;
            var neck=root+leanVec+Vec3.Up*h;
            var stemPath=new[]{root,new Vec3(root.X,.06*scale,root.Z),
                mid,Vec3.Lerp(mid,neck,.66),neck};
            double baseRadius=scale*rng.Range(.010,.017);
            double topRadius=scale*rng.Range(.005,.009);
            var ivory=Primitives.Mix(body,new[]{.80,.88,.91},.67);
            var baseColour=Primitives.Mix(body,new[]{.53,.63,.68},.36);
            Primitives.Tube(mesh,stemPath,
                new[]{baseRadius*1.18,baseRadius,baseRadius*.69,topRadius*1.15,topRadius},
                detail==2?6:8,
                (i,v)=>(Primitives.Mix(baseColour,ivory,i/4.0),1,i*.18,v,0,0));

            double phase=rng.Range(0,Math.Tau);
            double offset=rng.Range(-.20,.20);
            double tilt=rng.Range(-.22,.22);
            double amplitude=stage==2?.14:.085;
            var pole=(new Vec3(Math.Cos(swayAngle+1.5),0,Math.Sin(swayAngle+1.5)));
            double crown=capR*(stage==0?1.24:stage==1?.89:.48);
            double rimDroop=capR*(stage==2?.16:.30);
            double[] capBase=Primitives.Mix(body,new[]{.57,.68,.77},.23);
            double[] rimColor=Primitives.Mix(highlight,new[]{.77,.87,.93},.32);
            double[] gillColor=Primitives.Mix(body,new[]{.55,.71,.81},.45);

            Vec3 RimPoint(double t,double th)
            {
                double petal=Math.Cos(th*5+phase)*amplitude+
                    Math.Sin(th*9-phase*1.33)*.048+
                    Math.Cos(th*2+phase*.55)*.13;
                double narrow=Math.Pow(Math.Max(0,Math.Sin(Math.PI*t)),2);
                double f=Math.Clamp(t,0,1);
                double width=capR*f*(1+petal*Math.Pow(f,3));
                // An off-centre pinched crown; asymmetric, hanging and slashed
                // margins become visible at normal in-world camera distance.
                double taper=stage==0 ? 1-Math.Pow(f,1.34) :
                    Math.Pow(Math.Max(0,1-f*f),stage==1?.76:.60);
                double fall=rimDroop*Math.Pow(f,3.5)*
                    (1+.35*Math.Cos(th*3+phase));
                double y=crown*taper-fall+capR*.07*f*f*
                    Math.Sin(3*th+phase);
                return neck+new Vec3(Math.Cos(th)*width,0,Math.Sin(th)*width)+
                    Vec3.Up*y+pole*(capR*(offset*.2+.16*f*f*Math.Sin(th+phase)))+
                    Vec3.Up*(tilt*capR*f*Math.Cos(th-phase));
            }
            int top=mesh.VertexCount;
            for(int row=0;row<=rings;row++)
            {
                double t=(row+.025)/(rings+.025);
                for(int s=0;s<=around;s++)
                {
                    double th=s*Math.Tau/around;
                    var point=RimPoint(t,th);
                    var radial=new Vec3(Math.Cos(th),0,Math.Sin(th));
                    var normal=(radial*(.18+1.45*t)+Vec3.Up*(1.35-.42*t)+
                        pole*(tilt*.20)).Normalized();
                    var pigment=Primitives.Mix(capBase,rimColor,.32+.60*t);
                    mesh.AddVertex(point,normal,pigment,1,s/(double)around,t,0,1);
                }
            }
            for(int row=0;row<rings;row++)
            for(int s=0;s<around;s++)
            {
                int a=top+row*(around+1)+s,b=a+around+1;
                Primitives.TriangleFacing(mesh,a,b,a+1,mesh.NormalAt(a));
                Primitives.TriangleFacing(mesh,a+1,b,b+1,mesh.NormalAt(b));
            }
            // A separate fine, concave ventral membrane joins the stem to the
            // cap margin. Its down-facing vertices drive the original gill glow.
            Vec3 UnderPoint(double t,double th)
            {
                double f=Math.Clamp(t,0,1);
                var rim=RimPoint(1,th);
                return Vec3.Lerp(neck-Vec3.Up*(capR*.025),rim,f)+
                    Vec3.Up*(capR*.035*Math.Sin(Math.PI*f));
            }
            int under=mesh.VertexCount;
            int underRings=3;
            for(int row=0;row<=underRings;row++)
            {
                double t=.08+.92*row/underRings;
                for(int s=0;s<=around;s++)
                {
                    double th=s*Math.Tau/around;
                    mesh.AddVertex(UnderPoint(t,th),-Vec3.Up,
                        Primitives.Mix(gillColor,capBase,t*.32),1,
                        s/(double)around,t,1,0);
                }
            }
            for(int row=0;row<underRings;row++)
            for(int s=0;s<around;s++)
            {
                int a=under+row*(around+1)+s,b=a+around+1;
                Primitives.TriangleFacing(mesh,a,b,a+1,-Vec3.Up);
                Primitives.TriangleFacing(mesh,a+1,b,b+1,-Vec3.Up);
            }
            // Real radial lamellae dangle under the membrane. Vary their
            // insertion radii and vertical extent; don't draw gill lines alone.
            int gills=detail==2?9:detail==1?13:18;
            for(int g=0;g<gills;g++)
            {
                double angle=(g+.16*rng.Range(-1,1))*Math.Tau/gills;
                double inner=.12+(g%3)*.07;
                double startT=inner;
                int begin=mesh.VertexCount;
                var across=new Vec3(-Math.Sin(angle),0,Math.Cos(angle));
                double drop=capR*(.12+.11*rng.NextDouble());
                for(int row=0;row<=4;row++)
                {
                    double t=startT+(1-startT)*row/4.0;
                    var topPoint=UnderPoint(t,angle);
                    double exposure=drop*Math.Sin(Math.PI*(t-startT)/(1-startT))+
                        drop*.36*t;
                    var down=topPoint-Vec3.Up*exposure;
                    // Two very thin face sheets turn these into gills instead
                    // of decorative, front-facing painted stripes.
                    for(int face=0;face<2;face++)
                    {
                        var n=across*(face==0?1:-1);
                        mesh.AddVertex(topPoint,n,gillColor,1,g/(double)gills,t,1,0);
                        mesh.AddVertex(down,n,Primitives.Mix(gillColor,capBase,.28),1,
                            g/(double)gills,t,1,0);
                    }
                }
                for(int row=0;row<4;row++)
                for(int face=0;face<2;face++)
                {
                    int a=begin+row*4+face*2,b=a+4;
                    var normal=across*(face==0?1:-1);
                    Primitives.TriangleFacing(mesh,a,b,a+1,normal);
                    Primitives.TriangleFacing(mesh,a+1,b,b+1,normal);
                }
            }
            // Rare trailing tissue strips, asymmetric in number and length:
            // membranous remnants of the cap, not leaves or beard-like hair.
            if(stage!=0)
            {
                int streamers=stage==2?4+rng.NextInt(4):1+rng.NextInt(3);
                for(int v=0;v<streamers;v++)
                {
                    double a=phase+(v+.31*rng.Range(-1,1))*Math.Tau/streamers;
                    var rim=RimPoint(.98,a);
                    var curtain=RimPoint(.85,a);
                    var tangent=new Vec3(-Math.Sin(a),0,Math.Cos(a));
                    double length=capR*rng.Range(.30,.75)*(stage==2?1.0:.56);
                    var tip=rim-Vec3.Up*length+tangent*(capR*rng.Range(-.08,.08));
                    var side=tangent*capR*rng.Range(.035,.09);
                    int p0=mesh.AddVertex(curtain,Vec3.Up,capBase,1,0,.84,0,1);
                    int p1=mesh.AddVertex(rim-side,Vec3.Up,rimColor,1,.1,.99,0,1);
                    int p2=mesh.AddVertex(rim+side,Vec3.Up,rimColor,1,.9,.99,0,1);
                    int p3=mesh.AddVertex(tip,Vec3.Up,rimColor,1,.5,1,0,1);
                    Primitives.TriangleFacing(mesh,p0,p1,p2,Vec3.Up);
                    Primitives.TriangleFacing(mesh,p1,p3,p2,Vec3.Up);
                }
            }
            mesh.EssentialSpans.Add((first,mesh.VertexCount-first));
        }
    }
}
