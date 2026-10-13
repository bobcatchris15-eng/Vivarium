using Vivarium.Sim.Core;
namespace Vivarium.Sim.Geometry;
public static partial class OrganismMeshes
{
    private static void CinematicSalamander(MeshData m)
    {
        // One continuous soft head, thorax, abdomen and tail envelope.
        double[][] profile={new[]{.49,.086,.004,.004},new[]{.46,.090,.065,.030},new[]{.39,.105,.111,.050},
            new[]{.30,.105,.106,.051},new[]{.22,.105,.096,.055},new[]{.12,.104,.105,.070},
            new[]{-.06,.103,.108,.074},new[]{-.21,.099,.088,.063},new[]{-.32,.086,.057,.046},
            new[]{-.43,.073,.043,.034},new[]{-.57,.052,.030,.026},new[]{-.72,.032,.016,.016},new[]{-.87,.018,.0015,.0015}};
        var rings=new List<double[]>();
        for(int k=0;k<profile.Length-1;k++)for(int j=0;j<5;j++)
        {
            double t=j/5.0;var a=profile[Math.Max(0,k-1)];var b=profile[k];var c=profile[k+1];var d=profile[Math.Min(profile.Length-1,k+2)];
            var row=new double[4];for(int n=0;n<4;n++)row[n]=.5*(2*b[n]+(-a[n]+c[n])*t+(2*a[n]-5*b[n]+4*c[n]-d[n])*t*t+(-a[n]+3*b[n]-3*c[n]+d[n])*t*t*t);
            row[2]=Math.Max(.001,row[2]);row[3]=Math.Max(.001,row[3]);rings.Add(row);
        }
        rings.Add(profile[^1]);const int sides=32;int start=m.VertexCount;
        for(int i=0;i<rings.Count;i++)for(int j=0;j<=sides;j++)
        {
            var row=rings[i];double a=Math.Tau*j/sides;var p=new Vec3(row[0],row[1]+row[3]*Math.Sin(a),row[2]*Math.Cos(a));
            m.AddVertex(p,Vec3.Up,Body,0,(p.X+.87)/1.36,(double)j/sides,Math.Sin(a)<-.45?3:1,0);
        }
        for(int i=0;i<rings.Count-1;i++)for(int j=0;j<sides;j++)
        {
            int a=start+i*(sides+1)+j,b=a+sides+1;m.Indices.AddRange(new[]{a,a+1,b,a+1,b+1,b});
        }
        foreach(int i in new[]{0,rings.Count-1})
        {
            int centre=m.VertexCount;var row=rings[i];m.AddVertex(new Vec3(row[0],row[1],0),Vec3.Up,Body,0,i==0?1:0,.5,1,0);
            for(int j=0;j<sides;j++){int a=start+i*(sides+1)+j;if(i==0)m.Indices.AddRange(new[]{centre,a+1,a});else m.Indices.AddRange(new[]{centre,a,a+1});}
        }
        var mouth=new List<Vec3>();
        for(int k=0;k<=32;k++){double a=-1.42+2.84*k/32;mouth.Add(new Vec3(.352+.128*Math.Cos(a),.077-.004*Math.Cos(a),.104*Math.Sin(a)));}
        Primitives.Tube(m,mouth,mouth.Select(_=>.0013).ToArray(),8,(i,v)=>Region(i/32.0,v,7));
        foreach(double side in new[]{-1.0,1.0})
        {
            // Fleshy brows and low parotoid glands continue the broad head surface.
            BodySegment(m,new Vec3(.287,.128,.076*side),new Vec3(.072,.032,.034),14,24,(a,b)=>Region(.84,b,1));
            BodySegment(m,new Vec3(.374,.139,.081*side),new Vec3(.025,.018,.024),12,20,(a,b)=>Region(.94,b,1));
            BodySegment(m,new Vec3(.379,.151,.094*side),new Vec3(.015,.013,.010),12,20,(a,b)=>Region(.95,b,2));
            BodySegment(m,new Vec3(.386,.153,.103*side),new Vec3(.008,.009,.003),10,16,(a,b)=>Region(.95,b,5));
            BodySegment(m,new Vec3(.388,.153,.106*side),new Vec3(.004,.006,.0015),8,12,(a,b)=>Region(.95,b,6));
            BodySegment(m,new Vec3(.458,.112,.038*side),new Vec3(.0028,.0017,.0028),6,10,(a,b)=>Region(.99,b,7));
            foreach(bool front in new[]{true,false})
            {
                var root=new Vec3(front?.205:-.195,.095,.071*side);
                var knee=new Vec3(front?.158:-.275,.052,.144*side);
                var foot=new Vec3(front?.266:-.180,.016,(front?.187:.204)*side);
                DetailedTube(m,root,new[]{root,new Vec3(front?.188:-.233,.077,.112*side),knee,
                    new Vec3(front?.230:-.211,.027,(front?.175:.190)*side),foot},new[]{front?.030:.037,front?.030:.036,.023,.013,.009},sides:12);
                int pad=m.VertexCount;BodySegment(m,foot,new Vec3(.022,.008,.020),10,16,(a,b)=>Region(a,b,4));AttachSpan(m,pad,root,1,distal:true);
                int digits=front?4:5;
                for(int digit=0;digit<digits;digit++)
                {
                    double spread=(digit-(digits-1)*.5)*.014,reach=.054-Math.Abs(digit-(digits-1)*.5)*.009;
                    DetailedTube(m,root,new[]{foot,foot+new Vec3(.017,-.006,spread*side),
                        foot+new Vec3(reach*.75,-.010,spread*1.5*side),foot+new Vec3(reach,-.011,spread*1.7*side)},
                        new[]{.0058,.0048,.0028,.0012},distal:true,sides:8);
                }
            }
        }
        double lift=.002-m.Bounds().Min.Y;for(int i=0;i<m.VertexCount;i++)m.Positions[i*3+1]+=(float)lift;
        m.RecomputeNormals();
    }
}
