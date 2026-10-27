using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

public static partial class OrganismMeshes
{
    private static void FaunaScutellum(MeshData m)
    {
        // Small triangular shield seated between pronotum and paired elytra.
        var points=new[]{new Vec3(.123,.291,-.029),new Vec3(.123,.291,.029),new Vec3(.066,.321,0),new Vec3(.104,.285,0)};
        int first=m.VertexCount;
        foreach(var p in points)m.AddVertex(p,Vec3.Up,Body,0,(p.X+.45)/.95,.5,1,0);
        m.Indices.AddRange(new[]{first,first+2,first+1,first,first+1,first+3,first+1,first+2,first+3,first+2,first,first+3});
    }

    private static void CinematicBeetle(MeshData m)
    {
        // Coalback: a single shell envelope with a recessed dorsal suture,
        // overlapping pronotum/head collars and six articulated tapering limbs.
        // Signed COLOR offsets and UV2 roles remain the animation attachment data.
        int shellFirst = m.VertexCount;
        BodySegment(m, new Vec3(-.105,.171,0), new Vec3(.335,.161,.207), 24,40,
            (a,b)=>Region(a,b,1));
        for (int i=shellFirst;i<m.VertexCount;i++)
        {
            var p=m.Position(i);
            double groove=.0035*Math.Exp(-p.Z*p.Z/.000040)*Math.Clamp((p.Y-.20)/.09,0,1);
            m.Positions[i*3+1]-=(float)groove;
        }
        BodySegment(m,new Vec3(-.070,.090,0),new Vec3(.31,.052,.184),16,28,(a,b)=>Region(a,b,3));
        BodySegment(m,new Vec3(.229,.161,0),new Vec3(.128,.102,.173),18,28,(a,b)=>Region(a,b,1));
        BodySegment(m,new Vec3(.347,.137,0),new Vec3(.047,.061,.103),12,20,(a,b)=>Region(a,b,3));
        BodySegment(m,new Vec3(.399,.144,0),new Vec3(.087,.063,.102),16,24,(a,b)=>Region(a,b,1));
        FaunaScutellum(m);
        // Shallow longitudinal elytral ribs follow the actual dome, rather
        // than a straight rod floating above it. Fine pits are shaded separately.
        foreach(double side in new[]{-1.0,1.0})
        {
            for(int rib=1;rib<=6;rib++)
            {
                double angle=.13+rib*.185;
                var path=new List<Vec3>();
                for(int j=0;j<=20;j++)
                {
                    double u=-.86+j*.082;
                    double r=Math.Sqrt(Math.Max(.01,1-u*u));
                    path.Add(new Vec3(-.105+.335*u,.171+.161*r*Math.Cos(angle),side*.207*r*Math.Sin(angle)));
                }
                Primitives.Tube(m,path,path.Select(_=>.0018).ToArray(),5,(i,v)=>Region(i/20.0,v,1));
            }
            BodySegment(m,new Vec3(.423,.169,.083*side),new Vec3(.031,.027,.023),16,24,(a,b)=>Region(a,b,2));
            var antennaRoot=new Vec3(.450,.157,.059*side);
            var antenna=new[]{antennaRoot,new Vec3(.485,.173,.092*side),new Vec3(.538,.190,.145*side),
                new Vec3(.599,.198,.180*side),new Vec3(.656,.183,.201*side)};
            DetailedTube(m,antennaRoot,antenna,new[]{.009,.008,.007,.0055,.0035},role:3,sides:8);
            for(int bead=1;bead<=9;bead++)
            {
                double t=bead/10.0,station=t*(antenna.Length-1);int segment=Math.Min(antenna.Length-2,(int)station);
                var p=FaunaCurvePoint(antenna,segment,station-segment);
                int first=m.VertexCount;double r=.006*(1-.35*t);
                BodySegment(m,p,new Vec3(r,r,r),6,10,(a,b)=>Region(a,b,4));AttachSpan(m,first,antennaRoot,3);
            }
            // Mandibular tips project from the connected face, with a short
            // inner edge and no disconnected horn-like antenna roots.
            var mouth=new Vec3(.457,.123,.037*side);
            DetailedTube(m,mouth,new[]{mouth,new Vec3(.486,.118,.036*side),new Vec3(.496,.116,.016*side)},
                new[]{.012,.008,.002},region:3,role:4,sides:8);
            for(int leg=0;leg<3;leg++)
            {
                double x=.24-leg*.16,sweep=(leg-1)*.11;
                var root=new Vec3(x,.095,.110*side);
                var knee=new Vec3(x+sweep,.145,.239*side);
                var ankle=new Vec3(x+sweep*1.70,.035,.322*side);
                var foot=new Vec3(x+sweep*1.92,.010,.353*side);
                DetailedTube(m,root,new[]{root,new Vec3(x+sweep*.30,.116,.167*side),knee,ankle,foot},
                    new[]{.026,.024,.020,.012,.005},sides:10);
                foreach(var joint in new[]{knee,ankle})
                {
                    int first=m.VertexCount;double r=joint==knee?.021:.013;
                    BodySegment(m,joint,new Vec3(r,r,r),8,12,(a,b)=>Region(a,b,4));AttachSpan(m,first,root,1);
                }
                // Segmented tarsus and paired recurved terminal claws share
                // the exact coxal attachment, so the walk shader moves them together.
                for(int tarsus=0;tarsus<5;tarsus++)
                {
                    var p=foot+new Vec3(-.004*tarsus,.001,side*.008*tarsus);
                    int first=m.VertexCount;BodySegment(m,p,new Vec3(.006,.0035,.006),6,10,(a,b)=>Region(a,b,4));
                    AttachSpan(m,first,root,1,distal:true);
                }
                foreach(double claw in new[]{-1.0,1.0})
                {
                    var p=foot+new Vec3(claw*.004,0,side*.017);
                    DetailedTube(m,root,new[]{p,p+new Vec3(claw*.005,.002,side*.008),p+new Vec3(claw*.007,-.004,side*.013)},
                        new[]{.003,.002,.0008},distal:true,sides:6);
                }
            }
        }
        m.RecomputeNormals();
    }
}
