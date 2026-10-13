using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

public static partial class OrganismMeshes
{
    private static void CinematicSlug(MeshData m)
    {
        // Continuous visceral mass, mantle and flared muscular foot. The lower
        // envelope stays at the same support plane instead of stacked ellipsoids.
        double[][] profile = {
            new[]{.440,.009,.017}, new[]{.400,.073,.087}, new[]{.335,.096,.120},
            new[]{.255,.116,.173}, new[]{.130,.132,.189}, new[]{.015,.129,.173},
            new[]{-.115,.110,.133}, new[]{-.250,.090,.106}, new[]{-.390,.055,.066},
            new[]{-.485,.021,.027}, new[]{-.535,.0015,.0025}
        };
        var rings = new List<double[]>();
        for (int k=0;k<profile.Length-1;k++) for(int j=0;j<5;j++)
        {
            double t=j/5.0;
            var a=profile[Math.Max(0,k-1)];var b=profile[k];
            var c=profile[k+1];var d=profile[Math.Min(profile.Length-1,k+2)];
            var row=new double[3];
            for(int n=0;n<3;n++) row[n]=.5*(2*b[n]+(-a[n]+c[n])*t+
                (2*a[n]-5*b[n]+4*c[n]-d[n])*t*t+(-a[n]+3*b[n]-3*c[n]+d[n])*t*t*t);
            row[1]=Math.Max(.001,row[1]);row[2]=Math.Max(.002,row[2]);rings.Add(row);
        }
        rings.Add(profile[^1]);const int sides=40;int first=m.VertexCount;
        for(int i=0;i<rings.Count;i++) for(int j=0;j<=sides;j++)
        {
            var row=rings[i];double angle=Math.Tau*j/sides;
            double vertical=Math.Pow((Math.Sin(angle)+1)*.5,.78);
            double width=row[1]*(1+.16*(1-vertical));
            var p=new Vec3(row[0],.002+row[2]*vertical,width*Math.Cos(angle));
            double region=vertical<.20?3:row[0]>-.025&&row[0]<.295?5:1;
            m.AddVertex(p,Vec3.Up,Body,0,(p.X+.535)/.975,(double)j/sides,region,0);
        }
        for(int i=0;i<rings.Count-1;i++) for(int j=0;j<sides;j++)
        {
            int a=first+i*(sides+1)+j,b=a+sides+1;
            m.Indices.AddRange(new[]{a,a+1,b,a+1,b+1,b});
        }
        foreach(int i in new[]{0,rings.Count-1})
        {
            int centre=m.VertexCount;var row=rings[i];
            m.AddVertex(new Vec3(row[0],.002+row[2]*.5,0),Vec3.Up,Body,0,i==0?1:0,.5,1,0);
            for(int j=0;j<sides;j++)
            {
                int a=first+i*(sides+1)+j;
                m.Indices.AddRange(i==0?new[]{centre,a+1,a}:new[]{centre,a,a+1});
            }
        }
        // A recessed breathing pore on the right mantle and the soft lip seam.
        BodySegment(m,new Vec3(.158,.111,.127),new Vec3(.011,.008,.003),10,16,(a,b)=>Region(.73,b,7));
        var lip=new List<Vec3>();
        for(int k=0;k<=20;k++)
        {
            double a=-1.3+2.6*k/20;
            lip.Add(new Vec3(.406+.031*Math.Cos(a),.019,.067*Math.Sin(a)));
        }
        Primitives.Tube(m,lip,lip.Select(_=>.0017).ToArray(),8,(i,v)=>Region(.98,v,7));
        foreach(double side in new[]{-1.0,1.0})
        {
            var root=new Vec3(.335,.092,.051*side);
            var tip=new Vec3(.493,.245,.126*side);
            DetailedTube(m,root,new[]{root,new Vec3(.380,.151,.068*side),new Vec3(.447,.218,.102*side),tip},
                new[]{.015,.012,.008,.0048},region:1,role:3,sides:12);
            SensoryEye(m,tip,new Vec3(.0065,.006,.006),root);
            var lower=new Vec3(.388,.035,.052*side);
            DetailedTube(m,lower,new[]{lower,new Vec3(.435,.047,.077*side),new Vec3(.480,.039,.105*side)},
                new[]{.010,.006,.0023},region:1,role:3,sides:10);
        }
        m.RecomputeNormals();
    }
}
