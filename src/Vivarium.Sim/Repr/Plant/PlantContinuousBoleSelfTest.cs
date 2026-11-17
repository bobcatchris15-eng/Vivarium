using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>A contorted bole is one skin across graph segments, with no internal end caps.</summary>
public sealed class PlantContinuousBoleSelfTest : IReprSelfTest
{
    public string Name=>"plant.continuousBole";
    public string? Run()
    {
        var parent=new Organ {Parent=-1,Type=OrganType.Axis,Tissue=Tissue.Wood,Flags=OrganFlags.Continuous,
            Start=Vec3.Zero,End=Vec3.Up,StartTangent=Vec3.Up,EndTangent=Vec3.Up,Up=Vec3.Right,
            RadiusStart=.18f,RadiusEnd=.25f,Maturity=1,Health=1};
        var child=parent;child.Parent=0;child.AttachT=1;child.Start=Vec3.Up;child.End=Vec3.Up*2;
        child.RadiusStart=.25f;child.RadiusEnd=.12f;
        foreach(int detail in new[]{0,1,2})
        {
            var mesh=PlantCompiler.Compile(new PlantDescriptor {Organs=new[]{parent,child}},detail).Wood;
            int joined=0;
            for(int v=0;v<mesh.VertexCount;v++)
            {
                var p=mesh.Position(v);
                if(!p.IsFinite||!mesh.NormalAt(v).IsFinite||mesh.NormalAt(v).LengthSq<.25)
                    return "invalid continuous skin at LOD "+detail;
                if(p.Y>1.01&&mesh.UV[v*2]<.99)return "bark UV restarted at a trunk joint";
                if(Math.Abs(p.Y-1)>1e-6)continue;
                if(Math.Abs(Math.Sqrt(p.X*p.X+p.Z*p.Z)-.25)>1e-5)
                    return "wrong joint radius at LOD "+detail;
                bool below=false,above=false;
                for(int t=0;t<mesh.Indices.Count;t+=3)
                    if(mesh.Indices[t]==v||mesh.Indices[t+1]==v||mesh.Indices[t+2]==v)
                        for(int j=0;j<3;j++)
                        {
                            double y=mesh.Position(mesh.Indices[t+j]).Y;
                            below|=y<.999;above|=y>1.001;
                        }
                if(below&&above)joined++;
            }
            if(joined<8)return "trunk sections did not share a welded ring at LOD "+detail;
        }
        return null;
    }
}
