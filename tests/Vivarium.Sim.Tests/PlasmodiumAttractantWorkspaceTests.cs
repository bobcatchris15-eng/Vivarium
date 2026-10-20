using Vivarium.Sim.Coverage.Plasmodium;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "GrowthLab")]
public class PlasmodiumAttractantWorkspaceTests
{
    // Independently retained allocation-per-step reference of the original
    // 8-Jacobi-iteration attractant field, for exact numeric regression.
    private sealed class Reference
    {
        private readonly PlasmodiumParams _p;
        private int _minX,_minZ,_w,_h;
        private double[] _field=Array.Empty<double>();
        public Reference(PlasmodiumParams p)=>_p=p;
        public double At(int x,int z)
        {
            int lx=x-_minX,lz=z-_minZ;
            if(lx<0||lz<0||lx>=_w||lz>=_h)return 0;
            return _field[(lz+1)*(_w+2)+lx+1];
        }
        public void Step(IEnumerable<(int gx,int gz)> cells,Func<int,int,double> source)
        {
            int margin=Math.Max(1,(int)Math.Ceiling(_p.MarginMeters/_p.CoarseCellSize));
            bool any=false;
            int minX=0,maxX=0,minZ=0,maxZ=0;
            foreach(var (gx,gz) in cells)
            {
                var (x,z)=Attractant.CoarseOf(gx,gz);
                if(!any){minX=maxX=x;minZ=maxZ=z;any=true;continue;}
                minX=Math.Min(minX,x);maxX=Math.Max(maxX,x);
                minZ=Math.Min(minZ,z);maxZ=Math.Max(maxZ,z);
            }
            if(!any){_w=_h=0;_field=Array.Empty<double>();return;}
            minX-=margin;maxX+=margin;minZ-=margin;maxZ+=margin;
            int w=maxX-minX+1,h=maxZ-minZ+1,stride=w+2;
            var f=new double[stride*(h+2)];
            for(int z=0;z<h;z++)
                for(int x=0;x<w;x++)
                    f[(z+1)*stride+x+1]=source(minX+x,minZ+z);
            var cur=new double[stride*(h+2)];
            if(_w>0&&_h>0)
                for(int z=0;z<h;z++)
                {
                    int oldZ=minZ+z-_minZ;
                    if(oldZ<0||oldZ>=_h)continue;
                    for(int x=0;x<w;x++)
                    {
                        int oldX=minX+x-_minX;
                        if(oldX<0||oldX>=_w)continue;
                        cur[(z+1)*stride+x+1]=
                            _field[(oldZ+1)*(_w+2)+oldX+1];
                    }
                }
            var next=new double[stride*(h+2)];
            double h2=_p.CoarseCellSize*_p.CoarseCellSize;
            double inv=1/(_p.Delta+4*_p.Dc/h2),dc=_p.Dc/h2;
            for(int it=0;it<PlasmodiumParams.JacobiIterations;it++)
            {
                for(int z=0;z<h;z++)
                {
                    int row=(z+1)*stride,up=row-stride,down=row+stride;
                    for(int x=0;x<w;x++)
                    {
                        int i=row+x+1;
                        double neighbours=cur[i-1]+cur[i+1]+cur[up+x+1]+cur[down+x+1];
                        double v=dc*neighbours*inv+_p.Sigma*f[i]*inv;
                        next[i]=double.IsFinite(v)?Math.Max(0,v):0;
                    }
                }
                (cur,next)=(next,cur);
            }
            _w=w;_h=h;_minX=minX;_minZ=minZ;_field=cur;
        }
    }

    [Fact]
    public void ReusedWorkspaceMatchesAllocationReferenceAcrossExpansionDriftAndReset()
    {
        var p=new PlasmodiumParams {MarginMeters=.28,Dc=.041,Delta=.12};
        var actual=new Attractant(p);
        var expected=new Reference(p);
        var random=new Random(721513);
        var points=new List<(int gx,int gz)>();
        for(int t=0;t<32;t++)
        {
            points.Clear();
            if(t!=12 && t!=24)
            {
                int centerX=(t/3-5)*4, centerZ=(t%7-3)*4;
                int breadth=7+(t%6)*3;
                for(int i=0;i<300;i++)
                    points.Add((centerX+random.Next(-breadth,breadth+1),
                        centerZ+random.Next(-breadth,breadth+1)));
                if(t%4==0)points.Add((centerX+60,centerZ+47));
            }
            int epoch=t;
            double Source(int x,int z)=>Math.Max(0,
                .1+.03*Math.Sin(x*.13+epoch*.09)+.05*Math.Cos(z*.07-epoch*.11));
            actual.Step(points,Source);
            expected.Step(points,Source);
            for(int z=-85;z<=85;z++)
                for(int x=-85;x<=85;x++)
                    Assert.Equal(BitConverter.DoubleToInt64Bits(expected.At(x,z)),
                        BitConverter.DoubleToInt64Bits(actual.At(x,z)));
        }
    }
}
