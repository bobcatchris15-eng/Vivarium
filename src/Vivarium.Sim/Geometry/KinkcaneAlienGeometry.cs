using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry.Form;

namespace Vivarium.Sim.Geometry;

/// <summary>Dark, sparse, jointed Kinkcane with one perfectly straight palmate-crowned cane
/// per five. Every leaf attachment is above 80% of the host cane's actual height.</summary>
internal static class KinkcaneAlienGeometry
{
    public static void Build(MeshData m,Rng rng,double[] c1,double[] c2,double aspect)
    {
        var wood=Primitives.Mix(Primitives.Scale(c1,.26),new[]{.045,.055,.064},.62);
        var tipWood=Primitives.Mix(wood,new[]{.12,.12,.15},.32);
        var nodeCol=Primitives.Scale(wood,.83);
        var leafBase=Primitives.Mix(c1,new[]{.12,.21,.15},.24);
        var leafTip=Primitives.Mix(c2,new[]{.31,.43,.28},.27);
        // Count is always divisible by five, with some variation among clumps.
        int count=15+5*rng.NextInt(2);
        for(int k=0;k<count;k++)
        {
            bool straight=k%5==4;
            double az=rng.Range(0,Math.Tau);
            double spread=Math.Sqrt(rng.NextDouble())*.54;
            var outward=new Vec3(Math.Cos(az),0,Math.Sin(az));
            var foot=outward*spread;
            var physical=new Vec3[7];
            if(straight)
            {
                // Not a subtly eased curve: every node lies on exactly one line.
                double height=rng.Range(.88,1.07);
                for(int j=0;j<7;j++)physical[j]=Vec3.Up*(height*j/6.0);
            }
            else
            {
                var light=(Vec3.Up+outward*.18).Normalized();
                var direction=(Vec3.Up+outward*rng.Range(.15,.4)).Normalized();
                for(int j=1;j<7;j++)
                {
                    if(j>1)
                    {
                        // Original physical 34–46 degree kink, always favouring sky.
                        double turn=rng.Range(34,46)*Math.PI/180;
                        var toward=light-direction*light.Dot(direction);
                        if(toward.LengthSq<1e-8)toward=outward-direction*outward.Dot(direction);
                        toward=toward.Normalized();
                        var side=direction.Cross(toward).Normalized();
                        double phi=rng.Range(-.5,.5);
                        direction=(direction*Math.Cos(turn)+(toward*Math.Cos(phi)+
                            side*Math.Sin(phi))*Math.Sin(turn)).Normalized();
                    }
                    physical[j]=physical[j-1]+direction*rng.Range(.15,.21);
                }
            }
            double scale=rng.Range(.79,1.02)/physical[^1].Y;
            var path=physical.Select(p=>foot+new Vec3(p.X*aspect,p.Y,p.Z*aspect)*scale).ToArray();
            var radii=Enumerable.Range(0,7).Select(j=>MathD.Lerp(.0160,.0046,j/6.0)).ToArray();
            Primitives.Tube(m,path,radii,7,(i,v)=>
                (Primitives.Mix(wood,tipWood,i/6.0),1,i/6.0,v,0,0));

            // Dark, narrow swollen junctions remain visible in bare lower 80%.
            for(int j=1;j<6;j++)
            {
                var tangent=(path[j+1]-path[j-1]).Normalized();
                Primitives.Tube(m,new[]{path[j]-tangent*.006,path[j],path[j]+tangent*.007},
                    new[]{radii[j]*1.02,radii[j]*1.21,radii[j]*1.02},7,
                    (i,v)=>(nodeCol,1,i*.5,v,0,0));
            }
            if(straight)Crown(m,rng,path[^1],az,leafBase,leafTip,tipWood);
            else KinkTip(m,rng,path,az,leafBase,leafTip,tipWood);
        }
        // Subterranean runners are still present, but less visually dominant.
        for(int r=0;r<4;r++)
        {
            double a=rng.Range(0,Math.Tau);
            var dir=new Vec3(Math.Cos(a),0,Math.Sin(a));
            Primitives.Tube(m,new[]{dir*.1+Vec3.Up*.015,dir*.4+Vec3.Up*.022,
                dir*.70+Vec3.Up*.010},new[]{.009,.0065,.0035},6,
                (i,v)=>(nodeCol,1,i,v,0,0));
        }
    }

    private static Vec3 AtHeight(IReadOnlyList<Vec3> path,double fraction)
    {
        double y=path[0].Y+(path[^1].Y-path[0].Y)*fraction;
        for(int j=1;j<path.Count;j++)
            if(path[j].Y>=y)
                return Vec3.Lerp(path[j-1],path[j],
                    Math.Clamp((y-path[j-1].Y)/Math.Max(1e-8,path[j].Y-path[j-1].Y),0,1));
        return path[^1];
    }

    private static void KinkTip(MeshData m,Rng rng,Vec3[] path,double az,
        double[] baseCol,double[] tipCol,double[] twigCol)
    {
        for(int tier=0;tier<2;tier++)
        {
            var origin=AtHeight(path,tier==0?.855:.946);
            int leaves=tier==0?2:3;
            for(int n=0;n<leaves;n++)
            {
                double theta=az+tier*1.3+n*Math.Tau/leaves+rng.Range(-.22,.22);
                double pitch=rng.Range(-.05,.05);
                var radial=(new Vec3(Math.Cos(theta),pitch,Math.Sin(theta))).Normalized();
                var side=Axis.RotateAround(new Vec3(-radial.Z,0,radial.X).Normalized(),radial,rng.Range(-.15,.15));
                var petiole=origin+radial*rng.Range(.058,.092)+
                    Vec3.Up*rng.Range(.012,.035);
                Primitives.Tube(m,new[]{origin,petiole},new[]{.003,.00135},6,
                    (i,v)=>(twigCol,1,i,v,0,0));
                double len=rng.Range(.20,.31);
                var tip=petiole+radial*len+Vec3.Up*rng.Range(.025,.060);
                OrganismMeshes.CurvedBlade(m,petiole,
                    petiole+radial*len*.54+Vec3.Up*.053,tip,side,
                    rng.Range(.034,.05),baseCol,tipCol,8,rng.Range(-.21,.21),.92);
            }
        }
    }

    private static void Crown(MeshData m,Rng rng,Vec3 top,double phase,
        double[] baseCol,double[] tipCol,double[] twigCol)
    {
        // Eight palmate leaves around the head. Each leaf has one branching
        // petiole and five broad fingers, giving an unmistakable radial burst.
        const int palms=8;
        for(int p=0;p<palms;p++)
        {
            double angle=phase+(p+.10*rng.Range(-1,1))*Math.Tau/palms;
            var radial=new Vec3(Math.Cos(angle),0,Math.Sin(angle));
            var palmBase=top+radial*rng.Range(.105,.14)+
                Vec3.Up*rng.Range(.022,.042);
            Primitives.Tube(m,new[]{top,Vec3.Lerp(top,palmBase,.58)+Vec3.Up*.021,
                palmBase},new[]{.0046,.0035,.0024},6,
                (i,v)=>(twigCol,1,i*.5,v,0,0));
            for(int finger=-2;finger<=2;finger++)
            {
                double theta=angle+finger*.245+rng.Range(-.055,.055);
                double pitch=rng.Range(-.04,.06);
                var dir=(new Vec3(Math.Cos(theta),pitch,Math.Sin(theta))).Normalized();
                var side=Axis.RotateAround(new Vec3(-dir.Z,0,dir.X).Normalized(),dir,rng.Range(-.14,.14));
                double ratio=1-.14*Math.Abs(finger);
                double length=rng.Range(.29,.39)*ratio;
                var leafRoot=palmBase+dir*.012;
                var tip=leafRoot+dir*length+
                    Vec3.Up*(.018+(.052-.009*Math.Abs(finger))+rng.Range(-.012,.012));
                OrganismMeshes.CurvedBlade(m,leafRoot,
                    leafRoot+dir*length*.56+Vec3.Up*.066,
                    tip,side,(finger==0?.079:.060)*ratio,
                    baseCol,tipCol,9,rng.Range(-.14,.14),.78);
            }
        }
    }
}
