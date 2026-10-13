using Vivarium.Sim.Core;
namespace Vivarium.Sim.Geometry;
public static partial class OrganismMeshes
{
    private static void SilverfishScalePlate(MeshData m, Vec3 centre, double halfLength,
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
            var p=new Vec3((2*t-1)*halfLength-.003*edge,
                height*Math.Cos(q)+.0015*Math.Sin(Math.PI*t)-.001*t-layer*.001,
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

    private static void CinematicSilverfish(MeshData m)
    {
        // Continuous dorsoventrally flattened thorax and tapered abdomen.
        var path=new List<Vec3>();var widths=new List<double>();
        for(int i=0;i<=32;i++)
        {
            double t=i/32.0,x=.365-.79*t;
            double w=.130*Math.Pow(1-t,.82)*Math.Min(1,.45+t*8)+.004;
            path.Add(new Vec3(x,.078,0));widths.Add(w);
        }
        int first=m.VertexCount;
        Primitives.Tube(m,path,widths,28,(i,v)=>(Body,0,1-i/32.0,v,Math.Cos(v*Math.Tau)<-.4?3:1,0),new Vec3(0,0,1));
        for(int i=first;i<m.VertexCount;i++)m.Positions[i*3+1]=(float)(.078+(m.Position(i).Y-.078)*.40);
        BodySegment(m,new Vec3(.346,.079,0),new Vec3(.056,.033,.060),16,28,(a,b)=>Region(.96,b,1));
        for(int plate=0;plate<12;plate++)
        {
            double t=plate/12.0,w=.127*Math.Pow(1-t,.82)+.004;
            SilverfishScalePlate(m,new Vec3(.25-.056*plate,.074,0),.036,.040*Math.Pow(1-t,.8)+.004,w,.85-.70*t);
        }
        foreach(double side in new[]{-1.0,1.0})
        {
            BodySegment(m,new Vec3(.360,.090,.054*side),new Vec3(.010,.009,.006),10,16,(a,b)=>Region(.97,b,2));
            var antenna=new Vec3(.384,.080,.033*side);
            DetailedTube(m,antenna,new[]{antenna,new Vec3(.475,.100,.078*side),new Vec3(.593,.105,.142*side),
                new Vec3(.725,.084,.190*side),new Vec3(.810,.057,.197*side)},new[]{.006,.0048,.0036,.0022,.0007},role:3,sides:10);
            for(int leg=0;leg<3;leg++)
            {
                double x=.26-leg*.08,spread=.132+leg*.018;
                var root=new Vec3(x,.053,.064*side);var knee=new Vec3(x-.024,.045,spread*side);
                var toe=new Vec3(x-.063,.008,(spread+.035)*side);
                DetailedTube(m,root,new[]{root,new Vec3(x+.006,.050,.094*side),knee,
                    new Vec3(x-.050,.020,(spread+.025)*side),toe},new[]{.010,.012,.007,.004,.0018},sides:10);
                DetailedTube(m,root,new[]{toe,toe+new Vec3(-.012,-.002,.009*side)},new[]{.0018,.0006},distal:true,sides:6);
            }
        }
        foreach(double side in new[]{-1.0,0.0,1.0})
        {
            var root=new Vec3(-.414,.075,.011*side);
            DetailedTube(m,root,new[]{root,new Vec3(-.495,.085,.060*side),new Vec3(-.620,.098,.135*side),
                new Vec3(-.760,.090,.205*side),new Vec3(-.864,.064,.233*side)},new[]{.006,.0048,.0035,.0022,.0007},role:3,sides:10);
        }
        double lift=.002-m.Bounds().Min.Y;
        for(int i=0;i<m.VertexCount;i++)m.Positions[i*3+1]+=(float)lift;
        m.RecomputeNormals();
    }
}
