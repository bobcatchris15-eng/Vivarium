using System;
using System.Linq;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>The veteran bole must have a visible opening and a shaded inner wall at every LOD.</summary>
public sealed class PlantHollowSelfTest : IReprSelfTest
{
    public string Name=>"plant.hollowOpening";
    public string? Run()
    {
        foreach(int detail in new[]{0,1,2})
        {
            var organ=new Organ {Parent=-1,Type=OrganType.Axis,Tissue=Tissue.Bark,Start=Vec3.Zero,End=Vec3.Up,
                StartTangent=Vec3.Up,EndTangent=Vec3.Up,Up=Vec3.Right,RadiusStart=.2f,RadiusEnd=.18f,Maturity=1,Health=1};
            var plant=new PlantDescriptor {Organs=new[]{organ}};
            var solid=PlantCompiler.Compile(plant,detail).Wood;
            plant.Organs[0].Flags=OrganFlags.Hollow;
            var hollow=PlantCompiler.Compile(plant,detail).Wood;
            if(!HitsUpperCenter(solid)||HitsUpperCenter(hollow))return "open center lost at detail "+detail;
            bool inner=false;
            for(int i=0;i<hollow.VertexCount;i++)
            {
                var p=hollow.Position(i);var n=hollow.NormalAt(i);
                if(!p.IsFinite||!n.IsFinite||n.LengthSq<.25)return "invalid hollow wall at detail "+detail;
                var radial=new Vec3(p.X,0,p.Z);
                if(radial.Length>.09&&radial.Length<.13&&n.Dot(radial)<-.05)inner=true;
            }
            if(!inner)return "missing inward wall at detail "+detail;
            plant.Organs[0].Flags|=OrganFlags.HollowWindow;
            plant.Organs[0].Up=Vec3.Forward;
            var window=PlantCompiler.Compile(plant,detail).Wood;
            var eye=Vec3.Up*.5+Vec3.Forward*.5;
            if(HitsRay(window,eye,-Vec3.Forward,.45)||!HitsRay(window,eye,-Vec3.Forward,.9))
                return "side mouth or its interior wall lost at detail "+detail;
        }
        return null;
    }
    private static bool HitsUpperCenter(MeshData mesh)
        =>HitsRay(mesh,Vec3.Up*2,Vec3.Down,1.5);
    private static bool HitsRay(MeshData mesh,Vec3 origin,Vec3 direction,double limit)
    {
        for(int i=0;i<mesh.Indices.Count;i+=3)
        {
            var a=mesh.Position(mesh.Indices[i]);var e1=mesh.Position(mesh.Indices[i+1])-a;
            var e2=mesh.Position(mesh.Indices[i+2])-a;var cross=direction.Cross(e2);
            double determinant=e1.Dot(cross);if(Math.Abs(determinant)<1e-10)continue;
            var delta=origin-a;double u=delta.Dot(cross)/determinant;
            if(u< -1e-6||u>1+1e-6)continue;
            var q=delta.Cross(e1);double v=direction.Dot(q)/determinant;
            if(v< -1e-6||u+v>1+1e-6)continue;
            double distance=e2.Dot(q)/determinant;
            if(distance>0&&distance<limit)return true;
        }
        return false;
    }
}
