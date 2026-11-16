using System;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Repr.Plant;

public sealed class PlantRadialSurfaceSelfTest : IReprSelfTest
{
    public string Name=>"plant.radialOpenings";
    public string? Run()
    {
        foreach(int detail in new[]{0,1,2})
        {
            int rows=detail==0?18:detail==1?12:6, radial=detail==0?48:detail==1?24:12;
            var shape=new OrganShape {Type=OrganType.Flower,SurfaceForm=OrganSurfaceForm.Cup,
                ShellThickness=.008f,ColorAdaxial=Vec3.Up,ColorAbaxial=Vec3.Up,RadialLattice=true};
            double t=1.5/rows,angle=1.5*Math.Tau/radial;
            var direction=new Vec3(Math.Cos(angle),Math.Sin(angle),0);
            var eye=direction*.8+Vec3.Forward*t;
            var net=PlantCompiler.BuildShapeMesh(shape,detail);
            if(Hit(net,eye,-direction,.8))return "veil cell sealed at LOD "+detail;
            // A solid cup control must block that sight line.
            shape.RadialLattice=false;
            var solid=PlantCompiler.BuildShapeMesh(shape,detail);
            if(!Hit(solid,eye,-direction,.8))return "solid control missing at LOD "+detail;
            shape.MidribCurl=.6f;shape.RadialAsymmetry=.12f;
            var curved=PlantCompiler.BuildShapeMesh(shape,detail);
            for(int v=0;v<curved.VertexCount;v++)
                if(!curved.Position(v).IsFinite||!curved.NormalAt(v).IsFinite||curved.NormalAt(v).LengthSq<.25)
                    return "invalid curved vessel at LOD "+detail;
        }
        return null;
    }
    private static bool Hit(MeshData m,Vec3 eye,Vec3 direction,double limit)
    {
        for(int i=0;i<m.Indices.Count;i+=3)
        {
            var a=m.Position(m.Indices[i]);var e1=m.Position(m.Indices[i+1])-a;var e2=m.Position(m.Indices[i+2])-a;
            var p=direction.Cross(e2);double det=e1.Dot(p);if(Math.Abs(det)<1e-10)continue;
            var s=eye-a;double u=s.Dot(p)/det;if(u<0||u>1)continue;
            var q=s.Cross(e1);double v=direction.Dot(q)/det;if(v<0||u+v>1)continue;
            double d=e2.Dot(q)/det;if(d>0&&d<limit)return true;
        }
        return false;
    }
}
