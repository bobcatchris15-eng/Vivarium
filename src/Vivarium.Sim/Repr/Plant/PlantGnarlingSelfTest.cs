using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

public sealed class PlantGnarlingSelfTest : IReprSelfTest
{
    public string Name => "plant.axisGnarling";
    public string? Run()
    {
        foreach (int detail in new[] { 0, 1, 2 })
        {
            var organ = new Organ { Parent=-1, Type=OrganType.Axis, Tissue=Tissue.Bark,
                Start=Vec3.Zero, End=Vec3.Up, StartTangent=Vec3.Up, EndTangent=Vec3.Up,
                RadiusStart=.2f, RadiusEnd=.2f, Maturity=1, Health=1, Seed=1234, AxisGnarling=.8f };
            var p = new PlantDescriptor { Organs=new[] { organ } };
            var a=PlantCompiler.Compile(p,detail).Wood;
            var b=PlantCompiler.Compile(p,detail).Wood;
            if(a.VertexCount!=b.VertexCount)return "unstable topology";
            double min=double.MaxValue,max=0;
            for(int i=0;i<a.VertexCount;i++)
            {
                var v=a.Position(i);var n=a.NormalAt(i);
                if(!v.IsFinite||!n.IsFinite||n.LengthSq<.25)return "invalid geometry";
                if((v-b.Position(i)).Length>1e-7)return "non-deterministic geometry";
                double r=Math.Sqrt(v.X*v.X+v.Z*v.Z);
                if(Math.Abs(v.Y-.5)<.001){min=Math.Min(min,r);max=Math.Max(max,r);}
                if(Math.Abs(v.Y)<1e-6&&Math.Abs(r-.2)>1e-5)return "base seam changed";
            }
            if(max-min<.025)return "missing asymmetric lobes at detail "+detail;
            // A half-grown straight axis must have the same radial profile at
            // design position 0.25 as the mature axis.
            p.Organs[0].End=Vec3.Up*.5;
            p.Organs[0].StartTangent=Vec3.Up*.5;
            p.Organs[0].EndTangent=Vec3.Up*.5;
            p.Organs[0].Maturity=.5f;
            var prefix=PlantCompiler.Compile(p,detail).Wood;
            foreach(var mesh in new[] { a, prefix })
            {
                int count=0;
                for(int i=0;i<mesh.VertexCount;i++)if(Math.Abs(mesh.Position(i).Y-.25)<1e-6)count++;
                if(count==0)return "missing prefix comparison ring";
            }
            for(int i=0;i<prefix.VertexCount;i++)
            {
                var v=prefix.Position(i);if(Math.Abs(v.Y-.25)>1e-6)continue;
                bool found=false;
                for(int j=0;j<a.VertexCount;j++)if((v-a.Position(j)).Length<1e-6){found=true;break;}
                if(!found)return "prefix moved design shape at detail "+detail;
            }
        }
        return null;
    }
}
