using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>
/// Glowbriar's original spiral-cane silhouette, grown into an irregular three-order
/// woody shrub. Species-specific legacy geometry: intentionally independent of the
/// unaccepted botanical GLB/axis rebuild.
/// </summary>
internal static class GlowbriarGeometry
{
    public static void Build(MeshData mesh, Rng rng, double[] c1, double[] c2, bool juvenile)
    {
        int detail=mesh.FloraDetailLevel??0;
        int canes=juvenile ? 5 : detail==2 ? 6 : detail==1 ? 7 : 9;
        int sideShoots=juvenile ? 2 : detail==2 ? 2 : 3;
        bool tertiary=detail==0 && !juvenile;
        double scale=juvenile ? .72 : 1.0;
        var oldWood=Primitives.Mix(c1,new[]{.31,.29,.25},.53);
        var newWood=Primitives.Mix(c1,new[]{.34,.29,.25},.31);
        var darkLeaf=Primitives.Mix(c1,new[]{.10,.27,.21},.43);
        var litLeaf=Primitives.Mix(c2,new[]{.34,.55,.40},.45);

        for(int cane=0;cane<canes;cane++)
        {
            double az=Math.PI*2*(cane+.31*rng.Range(-1,1))/canes;
            var radial=new Vec3(Math.Cos(az),0,Math.Sin(az));
            var sideways=new Vec3(-radial.Z,0,radial.X);
            double phase=rng.Range(-.65,.65);
            double reach=scale*rng.Range(.61,.78);
            var primary=new Vec3[9];var thickness=new double[9];
            for(int i=0;i<primary.Length;i++)
            {
                double t=i/8.0;
                // Keep the recognizable corkscrew, but let it open out into a
                // living arch rather than a constant-radius manufactured helix.
                double coil=Math.PI*2*(1.20*t)+phase;
                double spiral=(.018+.105*t)*Math.Sin(coil);
                // The companion vertical phase makes the original sideways
                // weave a genuine three-dimensional helix about the arch.
                double lift=.042*t*Math.Cos(coil);
                primary[i]=radial*(.023+reach*t)+sideways*spiral*scale+
                    Vec3.Up*(scale*(.013+.39*Math.Sin(Math.PI*t*.83)+.062*t+lift));
                double collars=.11*(Math.Exp(-Math.Pow((t-.32)/.11,2))+
                    Math.Exp(-Math.Pow((t-.56)/.11,2))+
                    Math.Exp(-Math.Pow((t-.80)/.11,2)));
                thickness[i]=scale*(.0168-.0118*t)*
                    (1+.055*Math.Sin(5.1*t+cane)+collars);
            }
            Wood(mesh,primary,thickness,oldWood,newWood,detail==2?6:10);

            // Leaf pairs and branch buds share real sequential nodes on the cane.
            for(int node=0;node<(juvenile?4:6);node++)
            {
                double t=.18+(juvenile?.17:.15)*node;
                var p=Sample(primary,t);
                var ahead=Direction(primary,t);
                Pair(mesh,rng,p,ahead,radial,sideways,scale*(.96+.08*rng.Range(-1,1)),
                    darkLeaf,litLeaf,detail,cane*.43+node*.69);
            }
            for(int b=0;b<sideShoots;b++)
            {
                double t=.32+b*.24+(.018*rng.Range(-1,1));
                var basePoint=Sample(primary,t);
                var parentDir=Direction(primary,t);
                double handed=(b%2==0?1:-1);
                double span=scale*rng.Range(.20,.315)*(1-.13*b);
                var outDir=(sideways*handed*.78+radial*(.43+.1*b)+
                            parentDir*.20+Vec3.Up*(.16+.10*b)).Normalized();
                var branch=new Vec3[6];var branchRadii=new double[6];
                var bendAxis=outDir.Cross(Vec3.Up).Normalized();
                for(int k=0;k<branch.Length;k++)
                {
                    double u=k/5.0;
                    branch[k]=basePoint+outDir*span*u+Vec3.Up*
                        (span*(.15*u-.12*u*u))+
                        bendAxis*(handed*span*.105*Math.Sin(Math.PI*u));
                    branchRadii[k]=scale*(.0069*(1-u)+.00175*u)*
                        (1+.11*Math.Exp(-Math.Pow((u-.23)/.12,2)));
                }
                Wood(mesh,branch,branchRadii,newWood,oldWood,detail==2?6:8);
                int nodes=detail==2?3:4;
                for(int n=0;n<nodes;n++)
                {
                    double u=.18+(.72*n/Math.Max(1,nodes-1));
                    var p=Sample(branch,u);
                    Pair(mesh,rng,p,Direction(branch,u),radial,sideways,
                        scale*(.88+.14*rng.Range(-1,1)),darkLeaf,litLeaf,
                        detail,b*.91+n*.48+cane*.17);
                }
                if(tertiary)
                {
                    // Order-three shoots appear from alternating axillary buds.
                    // No miniature radial bouquets or identical repeated forks.
                    double u=.53+.13*((b+cane)%2);
                    var origin=Sample(branch,u);
                    var tangent=Direction(branch,u);
                    double thirdHand=((cane+b)%2==0?1:-1);
                    var thirdDir=(tangent*.50+sideways*thirdHand*.66+
                        Vec3.Up*.27+radial*.14).Normalized();
                    double thirdLength=scale*rng.Range(.105,.18);
                    var twig=new Vec3[4];var twigr=new double[4];
                    for(int k=0;k<4;k++)
                    {
                        double f=k/3.0;
                        twig[k]=origin+thirdDir*thirdLength*f+
                            Vec3.Up*thirdLength*.14*Math.Sin(Math.PI*f)+
                            radial*thirdHand*thirdLength*.06*f*f;
                        twigr[k]=scale*(.0034*(1-f)+.0009*f);
                    }
                    Wood(mesh,twig,twigr,newWood,oldWood,6);
                    for(int n=0;n<2;n++)
                    {
                        double f=.39+n*.46;
                        Pair(mesh,rng,Sample(twig,f),Direction(twig,f),radial,sideways,
                            scale*(.76+.13*rng.Range(-1,1)),darkLeaf,litLeaf,
                            detail,.82*n+b*.47+cane*.29);
                    }
                }
            }

            // Discrete luminous organs remain shaded beneath their host cane,
            // not a bloom painted across the new foliage.
            int nodules=detail==2?3:4;
            for(int n=0;n<nodules;n++)
            {
                double t=.37+.49*(n+.3)/nodules;
                var root=Sample(primary,t);
                var hanging=root-Vec3.Up*(scale*rng.Range(.024,.042))+
                    sideways*(scale*(n%2==0?1:-1)*.012);
                Primitives.Tube(mesh,new[]{root,hanging},
                    new[]{scale*.0033,scale*.0018},6,
                    (i,v)=>(newWood,1,i*.03,v,2,0));
                int begin=mesh.VertexCount;
                var mint=new[]{.45,.95,.66};
                Primitives.Ellipsoid(mesh,hanging,
                    new Vec3(scale*.0115,scale*.016,scale*.0115),4,6,
                    (u,v)=>(mint,1,u,v,1.4,1));
                for(int index=begin;index<mesh.VertexCount;index++)
                    mesh.UV2[index*2]=1.4f;
            }
        }
    }

    private static void Pair(MeshData mesh,Rng rng,Vec3 node,Vec3 stem,
        Vec3 radial,Vec3 sideways,double scale,double[] baseColor,
        double[] endColor,int detail,double phase)
    {
        // Opposite, slightly decussate pairs like honeysuckle; different ranks
        // swivel about the twig, rather than planar fans or random spokes.
        double swivel=.34*Math.Sin(phase*2.7);
        var side=(sideways*Math.Cos(swivel)+radial*Math.Sin(swivel)-
            stem*(sideways*Math.Cos(swivel)+radial*Math.Sin(swivel)).Dot(stem)).Normalized();
        if(side.LengthSq<.00001)side=stem.Cross(Vec3.Up).Normalized();
        for(int sign=-1;sign<=1;sign+=2)
        {
            var spread=(side*sign+radial*(.09+rng.Range(-.03,.03))+
                Vec3.Up*(.12+rng.Range(-.04,.04))).Normalized();
            double size=scale*rng.Range(.82,1.18);
            double petioleLength=.0085*size;
            var join=node+spread*petioleLength;
            Primitives.Tube(mesh,new[]{node,join},
                new[]{.0019*scale,.0012*scale},6,
                (i,v)=>(baseColor,1,i*.012,v,2,0));
            // Long, narrow, entire lanceolate blades: the widest point is
            // proximal to centre, with a long continuously tapering distal end.
            double length=rng.Range(.112,.174)*size;
            var dir=(spread*.94+stem*rng.Range(.13,.33)+Vec3.Up*
                rng.Range(-.13,.15)+side*rng.Range(-.06,.06)).Normalized();
            var tip=join+dir*length;
            var leafSide=new Vec3(-dir.Z,0,dir.X);
            if(leafSide.LengthSq<.00001)leafSide=sideways;
            double width=length*rng.Range(.122,.155);
            var baseTint=Primitives.Mix(baseColor,endColor,rng.Range(.01,.19));
            var tipTint=Primitives.Mix(baseColor,endColor,rng.Range(.35,.66));
            FoliageBlade.Build(mesh,join,tip,leafSide,width,
                baseTint,tipTint,camber:.14,curl:rng.Range(.04,.12),
                roll:rng.Range(-.20,.20),asymmetry:rng.Range(-.06,.06),
                shoulder:rng.Range(.65,.80),segments:detail==2?2:3);
        }
    }

    private static Vec3 Sample(IReadOnlyList<Vec3> p,double t)
    {
        double q=Math.Clamp(t,0,1)*(p.Count-1);
        int index=Math.Min(p.Count-2,(int)q);
        return Vec3.Lerp(p[index],p[index+1],q-index);
    }

    private static Vec3 Direction(IReadOnlyList<Vec3> p,double t)
    {
        return (Sample(p,Math.Min(1,t+.035))-Sample(p,Math.Max(0,t-.035))).Normalized();
    }

    private static void Wood(MeshData mesh,IReadOnlyList<Vec3> path,
        IReadOnlyList<double> radii,double[] aged,double[] young,int sides)
    {
        // Round-oval, lightly five-fluted vascular cross-sections with smooth
        // parallel-transport frames. Older shafts are stouter and gnarlier at
        // nodes; young tips become fine and relatively smooth. UV maps use
        // true axial metres, enabling the existing physical bark shader.
        sides=Math.Max(6,sides);
        if(sides>=8)
        {
            // Extra sampling follows an anatomical cubic centreline instead of
            // adding straight cylinder joints. Preserve the authored station
            // radii and the same end points, node swellings and UV contract.
            var smoothPath=new List<Vec3>();
            var smoothRadii=new List<double>();
            for(int i=0;i<path.Count-1;i++)
            {
                Vec3 p0=path[Math.Max(0,i-1)],p1=path[i],p2=path[i+1],
                    p3=path[Math.Min(path.Count-1,i+2)];
                var t0=(p2-p0)*.5;
                var t1=(p3-p1)*.5;
                for(int j=0;j<2;j++)
                {
                    double u=j*.5,u2=u*u,u3=u2*u;
                    smoothPath.Add(p1*(2*u3-3*u2+1)+t0*(u3-2*u2+u)+
                        p2*(-2*u3+3*u2)+t1*(u3-u2));
                    smoothRadii.Add(radii[i]*(1-u)+radii[i+1]*u);
                }
            }
            smoothPath.Add(path[^1]);smoothRadii.Add(radii[^1]);
            path=smoothPath;radii=smoothRadii;
        }
        var tangent=new Vec3[path.Count];
        for(int i=0;i<path.Count;i++)
        {
            tangent[i]=(path[Math.Min(i+1,path.Count-1)]-
                path[Math.Max(0,i-1)]).Normalized();
        }
        var side=tangent[0].Cross(Vec3.Up);
        if(side.LengthSq<1e-8)side=tangent[0].Cross(new Vec3(1,0,0));
        side=side.Normalized();
        int start=mesh.VertexCount;
        double distance=0;
        for(int i=0;i<path.Count;i++)
        {
            if(i>0)distance+=(path[i]-path[i-1]).Length;
            if(i>0)
            {
                side=(side-tangent[i]*side.Dot(tangent[i])).Normalized();
                if(side.LengthSq<1e-8)side=tangent[i].Cross(Vec3.Up).Normalized();
            }
            var dorsal=side.Cross(tangent[i]).Normalized();
            double progress=i/(double)(path.Count-1);
            for(int j=0;j<=sides;j++)
            {
                double theta=Math.PI*2*j/sides;
                double grain=1+.044*Math.Cos(5*theta+distance*5.3)+
                    .021*Math.Sin(3*theta-distance*3.8);
                double oval=1+.045*Math.Cos(2*theta+progress*.8);
                var outward=(side*Math.Cos(theta)*oval+
                    dorsal*Math.Sin(theta)/oval).Normalized();
                var color=Primitives.Mix(aged,young,progress*.72);
                var position=path[i]+outward*radii[i]*grain;
                mesh.AddVertex(position,outward,color,1,distance,
                    j/(double)sides,2,0);
            }
        }
        for(int i=0;i<path.Count-1;i++)
        for(int j=0;j<sides;j++)
        {
            int a=start+i*(sides+1)+j;
            int b=a+sides+1;
            mesh.AddTriangle(a,a+1,b);
            mesh.AddTriangle(a+1,b+1,b);
        }
        mesh.Structural.Add((start,mesh.VertexCount-start,sides,radii[0]));
    }
}
