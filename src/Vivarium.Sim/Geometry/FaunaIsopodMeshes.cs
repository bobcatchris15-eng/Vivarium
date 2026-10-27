using Vivarium.Sim.Core;

namespace Vivarium.Sim.Geometry;

public static partial class OrganismMeshes
{
    // Closed curved tergites: continuous arches and small trailing overlaps,
    // rather than separate low-sided ellipsoids that become saw-tooth fins.
    private static void IsopodPlate(MeshData m, Vec3 centre, double halfLength,
        double height, double width, double bodyU, double pitch=0)
    {
        const int rows=5, sides=28;
        int first=m.VertexCount, layerSize=(rows+1)*(sides+1);
        double cp=Math.Cos(pitch),sp=Math.Sin(pitch);
        Vec3 Turn(Vec3 p)=>new(p.X*cp-p.Y*sp,p.X*sp+p.Y*cp,p.Z);
        for(int layer=0;layer<2;layer++)for(int row=0;row<=rows;row++)for(int k=0;k<=sides;k++)
        {
            double t=(double)row/rows,q=-1.45+2.90*k/sides;
            double edge=Math.Sin(q)*Math.Sin(q);
            var p=new Vec3((2*t-1)*halfLength-.014*edge,
                height*Math.Cos(q)+.009*Math.Sin(Math.PI*t)-.004*t-layer*.004,
                width*Math.Sin(q));
            var n=Turn(new Vec3(.04,Math.Cos(q)/height,Math.Sin(q)/width).Normalized())*(layer==0?1:-1);
            m.AddVertex(centre+Turn(p),n,Body,0,bodyU,(double)k/sides,1,0);
        }
        void Quad(int a,int b,int c,int d,bool reverse=false)
        {
            if(reverse)m.Indices.AddRange(new[]{a,c,b,a,d,c});
            else m.Indices.AddRange(new[]{a,b,c,a,c,d});
        }
        for(int row=0;row<rows;row++)for(int k=0;k<sides;k++)
        {
            int a=first+row*(sides+1)+k,b=a+sides+1;
            Quad(a,b,b+1,a+1,true);Quad(a+layerSize,b+layerSize,b+1+layerSize,a+1+layerSize);
        }
        for(int row=0;row<rows;row++)foreach(int k in new[]{0,sides})
        {
            int a=first+row*(sides+1)+k,b=a+sides+1;
            Quad(a,a+layerSize,b+layerSize,b,k==0);
        }
        foreach(int row in new[]{0,rows})for(int k=0;k<sides;k++)
        {
            int a=first+row*(sides+1)+k;
            Quad(a,a+1,a+1+layerSize,a+layerSize,row==0);
        }
    }

    private static void CinematicIsopod(MeshData m)
    {
        BodySegment(m,new Vec3(.365,.088,0),new Vec3(.075,.062,.145),14,28,(a,b)=>Region(.94,b,1));
        BodySegment(m,new Vec3(-.025,.046,0),new Vec3(.397,.030,.178),14,32,(a,b)=>Region(a,b,3));
        for(int plate=0;plate<7;plate++)
        {
            double t=plate/6.0,dome=Math.Sin(Math.PI*(.15+.70*t));
            IsopodPlate(m,new Vec3(.26-plate*.078,.052,0),.058,.088+.059*dome,.190+.043*dome,.85-.55*t);
        }
        for(int plate=0;plate<5;plate++)
            IsopodPlate(m,new Vec3(-.285-plate*.025,.050,0),.022,.066-plate*.008,.160-plate*.018,.26-.042*plate);
        BodySegment(m,new Vec3(-.429,.055,0),new Vec3(.045,.027,.068),12,24,(a,b)=>Region(.04,b,1));
        foreach(double side in new[]{-1.0,1.0})
        {
            BodySegment(m,new Vec3(.391,.113,.123*side),new Vec3(.014,.012,.008),16,24,(a,b)=>Region(.98,b,2));
            var antenna=new Vec3(.408,.091,.052*side);
            DetailedTube(m,antenna,new[]{antenna,new Vec3(.456,.109,.094*side),new Vec3(.509,.097,.150*side),
                new Vec3(.547,.063,.185*side),new Vec3(.580,.039,.196*side)},new[]{.013,.011,.008,.005,.0025},role:3,sides:8);
            for(int leg=0;leg<7;leg++)
            {
                double x=.26-leg*.078,sweep=(leg-3)*-.010;
                var root=new Vec3(x,.049,.122*side);
                var knee=new Vec3(x+sweep*.6,.052,.188*side);
                var ankle=new Vec3(x+sweep,.023,.248*side);
                var toe=new Vec3(x+sweep*1.4,.007,.274*side);
                DetailedTube(m,root,new[]{root,knee,ankle,toe},new[]{.015,.012,.007,.0035},sides:8);
                int joint=m.VertexCount;
                BodySegment(m,knee,new Vec3(.012,.010,.011),6,10,(a,b)=>Region(a,b,4));AttachSpan(m,joint,root,1);
                DetailedTube(m,root,new[]{toe,toe+new Vec3(-.003,.002,.007*side),toe+new Vec3(-.005,-.004,.011*side)},
                    new[]{.003,.002,.0007},distal:true,sides:6);
            }
            var uro=new Vec3(-.438,.060,.044*side);
            DetailedTube(m,uro,new[]{uro,new Vec3(-.471,.054,.067*side),new Vec3(-.504,.043,.081*side)},
                new[]{.010,.006,.0025},role:3,sides:8);
        }
        NormalizeIsopodSupport(m);
        m.RecomputeNormals();
    }

    private static void NormalizeIsopodSupport(MeshData m)
    {
        double shift=.002-m.Bounds().Min.Y;
        for(int i=0;i<m.VertexCount;i++)m.Positions[i*3+1]+=(float)shift;
    }

    private static void CurledIsopodPlate(MeshData m,double angle,double bodyU)
    {
        const int rows=8,sides=28;
        int first=m.VertexCount,layerSize=(rows+1)*(sides+1);
        for(int layer=0;layer<2;layer++)for(int row=0;row<=rows;row++)for(int side=0;side<=sides;side++)
        {
            double t=(double)row/rows,theta=angle+(t-.5)*.98,phi=-1.45+2.90*side/sides;
            double radius=.182+.002*Math.Sin(Math.PI*t)-layer*.003;
            var p=new Vec3(radius*Math.Cos(theta)*Math.Cos(phi),.223+radius*Math.Sin(theta)*Math.Cos(phi),(.214-layer*.003)*Math.Sin(phi));
            var n=new Vec3(Math.Cos(theta)*Math.Cos(phi),Math.Sin(theta)*Math.Cos(phi),Math.Sin(phi))*(layer==0?1:-1);
            m.AddVertex(p,n,Body,0,bodyU,(double)side/sides,1,0);
        }
        void Quad(int a,int b,int c,int d,bool reverse=false)
        {
            if(reverse)m.Indices.AddRange(new[]{a,c,b,a,d,c});
            else m.Indices.AddRange(new[]{a,b,c,a,c,d});
        }
        for(int row=0;row<rows;row++)for(int side=0;side<sides;side++)
        {
            int a=first+row*(sides+1)+side,b=a+sides+1;
            Quad(a,b,b+1,a+1);Quad(a+layerSize,b+layerSize,b+1+layerSize,a+1+layerSize,true);
        }
        for(int row=0;row<rows;row++)foreach(int side in new[]{0,sides})
        {
            int a=first+row*(sides+1)+side,b=a+sides+1;
            Quad(a,a+layerSize,b+layerSize,b,side==sides);
        }
        foreach(int row in new[]{0,rows})for(int side=0;side<sides;side++)
        {
            int a=first+row*(sides+1)+side;
            Quad(a,a+1,a+1+layerSize,a+layerSize,row==0);
        }
    }

    private static void CinematicIsopodCurled(MeshData m)
    {
        const double cy=.223;
        BodySegment(m,new Vec3(0,cy,0),new Vec3(.170,.170,.195),20,32,(a,b)=>Region(a,b,3));
        for(int plate=0;plate<7;plate++)
        {
            double angle=-.32*Math.PI+plate*(1.64*Math.PI/6);
            CurledIsopodPlate(m,angle,.85-plate*.09);
        }
        var head=new Vec3(.107,.105,0);
        BodySegment(m,head,new Vec3(.060,.037,.142),14,24,(a,b)=>Region(.94,b,1),pitch:-.7);
        foreach(double side in new[]{-1.0,1.0})
            BodySegment(m,head+new Vec3(.016,.018,.126*side),new Vec3(.010,.008,.006),8,12,(a,b)=>Region(a,b,2));
        // Conglobation folds all seven leg pairs into the ventral opening;
        // it does not delete the appendages when the plates close.
        foreach(double side in new[]{-1.0,1.0}) for(int leg=0;leg<7;leg++)
        {
            double angle = -.70 + leg * .22;
            var root = new Vec3(Math.Sin(angle) * .11, cy - Math.Cos(angle) * .11, .095 * side);
            var knee = root + new Vec3(.012, -.012, -.035 * side);
            var ankle = knee + new Vec3(-.020, .016, -.035 * side);
            var toe = ankle + new Vec3(.012, .004, -.012 * side);
            DetailedTube(m, root, new[]{root,knee,ankle,toe}, new[]{.010,.008,.005,.0025}, sides:8);
        }
        NormalizeIsopodSupport(m);m.RecomputeNormals();
    }
}
