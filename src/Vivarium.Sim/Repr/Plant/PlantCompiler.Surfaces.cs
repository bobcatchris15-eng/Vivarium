using System;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Repr.Plant;

public static partial class PlantCompiler
{
    private static MeshData BuildRadialSurface(OrganShape shape,int detail)
    {
        var mesh=new MeshData();
        bool needle=shape.SurfaceForm==OrganSurfaceForm.Needle;
        bool open=shape.SurfaceForm is OrganSurfaceForm.Cup or OrganSurfaceForm.Trumpet or OrganSurfaceForm.MushroomCap or OrganSurfaceForm.Shelf;
        int radial=needle?(detail==0?6:detail==1?4:3):(detail==0?20:detail==1?12:8);
        int rows=needle?(detail==0?7:detail==1?4:2):(detail==0?10:detail==1?6:3);
        if(shape.RadialPitDepth>0) { radial=detail==0?48:detail==1?24:12; rows=detail==0?32:detail==1?16:8; }
        var color=EncRgb(shape.ColorAdaxial);var innerColor=EncRgb(shape.ColorAbaxial);
        double peak=ProfileMax(shape);
        double Radius(double t)
        {
            if(shape.WidthProfile.Length>0) return Math.Max(0,.5*ProfileAt(shape,t)/peak);
            return shape.SurfaceForm switch {
                OrganSurfaceForm.Cup or OrganSurfaceForm.Trumpet=>.12+.38*Math.Pow(t,.8),
                OrganSurfaceForm.MushroomCap or OrganSurfaceForm.Shelf=>.5*Math.Sqrt(Math.Max(0,1-t*t)),
                _=>.5*Math.Pow(Math.Max(0,Math.Sin(Math.PI*t)),needle?.4:.65) };
        }
        var outer=new int[rows+1,radial];var inner=open?new int[rows+1,radial]:null;
        for(int i=0;i<=rows;i++)for(int j=0;j<radial;j++)
        {
            double t=i/(double)rows,theta=j*Math.Tau/radial;
            double rim=shape.SurfaceForm is OrganSurfaceForm.MushroomCap or OrganSurfaceForm.Shelf?Math.Pow(1-t,4):
                shape.SurfaceForm is OrganSurfaceForm.Star or OrganSurfaceForm.Disc?1:Math.Pow(t,4);
            double wave=shape.LobeCount>0?Math.Cos(shape.LobeCount*theta):0;
            double radius=Math.Max(.00001,Radius(t)*(1+shape.LobeDepth*rim*wave));
            if(shape.RadialPitDepth>0)
            {
                // Staggered cells with raised dividing ridges; angular seam remains periodic.
                double cellY=t*6, row=Math.Floor(cellY), fy=cellY-row-.5;
                double cellX=j*8.0/radial + ((int)row%2)*.5;
                double fx=cellX-Math.Floor(cellX)-.5;
                double edge=Math.Max(Math.Abs(fx)*2, Math.Abs(fy)*2);
                double pit=1-Math.Clamp((edge-.62)/.26,0,1);
                radius*=1-Math.Clamp(shape.RadialPitDepth,0,.5f)*pit*Math.Sin(Math.PI*t);
            }
            double yScale=shape.SurfaceForm==OrganSurfaceForm.Shelf?.55:1;
            double z=t+shape.RimCurl*rim*(1-wave)*.08;
            var p=new Vec3(radius*Math.Cos(theta),radius*Math.Sin(theta)*yScale,z);
            outer[i,j]=mesh.AddVertex(p,new Vec3(Math.Cos(theta),Math.Sin(theta),0),color[0],color[1],color[2],1,t,j/(double)radial,
                shape.Type==OrganType.Fruit?1.75:shape.Type==OrganType.Flower?1.45:0,1);
            if(inner!=null)
            {
                double thickness=Math.Clamp(shape.ShellThickness,.002,.08);
                double rr=Math.Max(.000005,radius-thickness);
                var q=new Vec3(rr*Math.Cos(theta),rr*Math.Sin(theta)*yScale,z);
                inner[i,j]=mesh.AddVertex(q,new Vec3(-Math.Cos(theta),-Math.Sin(theta),0),innerColor[0],innerColor[1],innerColor[2],1,t,j/(double)radial,
                    shape.Type==OrganType.Fruit?1.75:shape.Type==OrganType.Flower?1.45:0,1);
            }
        }
        for(int i=0;i<rows;i++)for(int j=0;j<radial;j++)
        {
            int n=(j+1)%radial,a=outer[i,j],b=outer[i,n],c=outer[i+1,j],d=outer[i+1,n];
            mesh.AddTriangle(a,c,b);mesh.AddTriangle(b,c,d);
            if(inner!=null){a=inner[i,j];b=inner[i,n];c=inner[i+1,j];d=inner[i+1,n];mesh.AddTriangle(a,b,c);mesh.AddTriangle(b,d,c);}
        }
        if(inner!=null)
        {
            foreach(int row in new[]{0,rows})for(int j=0;j<radial;j++)
            {
                int n=(j+1)%radial,a=outer[row,j],b=outer[row,n],c=inner[row,j],d=inner[row,n];
                if(row==0){mesh.AddTriangle(a,b,c);mesh.AddTriangle(b,d,c);}
                else{mesh.AddTriangle(a,c,b);mesh.AddTriangle(b,c,d);}
            }
        }
        else
        {
            foreach(int row in new[]{0,rows})
            {
                var p=mesh.Position(outer[row,0]);int center=mesh.AddVertex(new Vec3(0,0,p.Z),row==0?-Vec3.Forward:Vec3.Forward,color[0],color[1],color[2],1);
                for(int j=0;j<radial;j++)
                    if(row==0)mesh.AddTriangle(center,outer[row,j],outer[row,(j+1)%radial]);
                    else mesh.AddTriangle(center,outer[row,(j+1)%radial],outer[row,j]);
            }
        }
        mesh.RecomputeNormals();
        for(int i=0;i<mesh.Normals.Count;i++)mesh.Normals[i]=-mesh.Normals[i];
        for(int i=0;i<mesh.VertexCount;i++)if(mesh.NormalAt(i).LengthSq<.25)
        {
            var p=mesh.Position(i);var n=PlantMath.SafeNormal(new Vec3(p.X,p.Y,p.Z<.5?-1:1),Vec3.Up);
            mesh.Normals[i*3]=(float)n.X;mesh.Normals[i*3+1]=(float)n.Y;mesh.Normals[i*3+2]=(float)n.Z;
        }
        return mesh;
    }
}
