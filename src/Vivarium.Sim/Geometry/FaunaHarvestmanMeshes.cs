using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

public static partial class OrganismMeshes
{
    private static void CinematicHarvestman(MeshData m)
    {
        // One fused oval instead of a separate head sphere. The short coxae
        // enter this envelope; long femora and fine curved tarsi read as limbs.
        double[][] profile = {
            new[]{.181,.176,.005,.009}, new[]{.155,.184,.049,.075},
            new[]{.095,.198,.075,.112}, new[]{.010,.205,.083,.132},
            new[]{-.095,.202,.073,.117}, new[]{-.162,.198,.041,.066},
            new[]{-.185,.195,.004,.008}
        };
        var rings = new List<double[]>();
        for (int k=0;k<profile.Length-1;k++) for(int j=0;j<8;j++)
        {
            double t=j/8.0;var a=profile[Math.Max(0,k-1)];var b=profile[k];
            var c=profile[k+1];var d=profile[Math.Min(profile.Length-1,k+2)];
            var row=new double[4];
            for(int n=0;n<4;n++) row[n]=.5*(2*b[n]+(-a[n]+c[n])*t+
                (2*a[n]-5*b[n]+4*c[n]-d[n])*t*t+(-a[n]+3*b[n]-3*c[n]+d[n])*t*t*t);
            row[2]=Math.Max(.003,row[2]);row[3]=Math.Max(.006,row[3]);rings.Add(row);
        }
        rings.Add(profile[^1]);const int sides=32;int first=m.VertexCount;
        for(int i=0;i<rings.Count;i++) for(int j=0;j<=sides;j++)
        {
            var row=rings[i];double a=Math.Tau*j/sides;
            m.AddVertex(new Vec3(row[0],row[1]+row[2]*Math.Sin(a),row[3]*Math.Cos(a)),
                Vec3.Up,Body,0,(row[0]+.185)/.366,(double)j/sides,1,0);
        }
        for(int i=0;i<rings.Count-1;i++) for(int j=0;j<sides;j++)
        {
            int a=first+i*(sides+1)+j,b=a+sides+1;
            m.Indices.AddRange(new[]{a,a+1,b,a+1,b+1,b});
        }
        foreach(int i in new[]{0,rings.Count-1})
        {
            int centre=m.VertexCount;var row=rings[i];
            m.AddVertex(new Vec3(row[0],row[1],0),Vec3.Up,Body,0,i==0?1:0,.5,1,0);
            for(int j=0;j<sides;j++)
            {
                int a=first+i*(sides+1)+j;
                m.Indices.AddRange(i==0?new[]{centre,a+1,a}:new[]{centre,a,a+1});
            }
        }
        HarvestmanOval(m,new Vec3(.072,.272,0),new Vec3(.021,.016,.024),1,12,18);
        foreach(double side in new[]{-1.0,1.0})
        {
            HarvestmanOval(m,new Vec3(.073,.278,.021*side),new Vec3(.007,.006,.0035),2,10,14);
            for(int k=0;k<4;k++)
            {
                double x=.115-k*.075,sweep=(1.5-k)*.17,span=k==1?.74:k==2?.64:.56;
                var root=new Vec3(x,.191,.091*side);
                var knee=new Vec3(x+sweep*.56,.310,span*.46*side);
                var tarsus=new Vec3(x+sweep,.055,span*.83*side);
                var toe=new Vec3(x+sweep+.035,.006,span*side);
                DetailedTube(m,root,new[]{root,root+new Vec3(sweep*.12,.019,.032*side),
                    knee,tarsus,new Vec3(toe.X-.01,.010,(span-.025)*side),toe},
                    new[]{.0085,.008,.0052,.0026,.0018,.0011},sides:10);
                DetailedTube(m,root,new[]{toe,toe+new Vec3(.012,.003,.004*side),
                    toe+new Vec3(.018,.001,.002*side)},new[]{.0012,.0008,.0002},distal:true,sides:6);
            }
            var palp=new Vec3(.143,.157,.042*side);
            DetailedTube(m,palp,new[]{palp,new Vec3(.201,.140,.067*side),
                new Vec3(.234,.092,.088*side),new Vec3(.251,.070,.073*side)},
                new[]{.008,.0065,.004,.0012},role:3,sides:10);
            var jaw=new Vec3(.161,.165,.018*side);
            DetailedTube(m,jaw,new[]{jaw,jaw+new Vec3(.026,-.022,0),jaw+new Vec3(.016,-.030,0)},
                new[]{.007,.005,.001},region:7,role:3,sides:8);
        }
        // Seed zero bypasses inherited morphology, so neutral and inherited
        // specimens must both begin on the same visible support plane.
        double lift=.002-m.Bounds().Min.Y;
        for(int i=0;i<m.VertexCount;i++) m.Positions[i*3+1]+=(float)lift;
        m.RecomputeNormals();
    }

    private static void HarvestmanOval(MeshData m,Vec3 centre,Vec3 radii,int region,int rings,int sides)
    {
        int first=m.Indices.Count;
        BodySegment(m,centre,radii,rings,sides,(a,b)=>Region(a,b,region));
        // The legacy oval helper supplies analytic outward normals but its
        // triangle order reverses them when morphology recomputes normals.
        // Correct only these new harvestman ovals, preserving other assets.
        for(int i=first;i<m.Indices.Count;i+=3)
            (m.Indices[i+1],m.Indices[i+2])=(m.Indices[i+2],m.Indices[i+1]);
    }
}
