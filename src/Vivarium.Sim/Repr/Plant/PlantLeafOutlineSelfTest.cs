using System;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Plant;

/// <summary>Arrow leaves retain their backward basal lobes and central notch at every LOD.</summary>
public sealed class PlantLeafOutlineSelfTest : IReprSelfTest
{
    public string Name => "plant.sagittateOutline";
    public string? Run()
    {
        var shape = new OrganShape { Type=OrganType.Leaf, BasalLobeLength=.4f,
            WidthProfile=new float[]{.35f,.5f,.35f,.15f,0}, ColorAdaxial=Vec3.Up, ColorAbaxial=Vec3.Up };
        for(int detail=0;detail<3;detail++)
        {
            var mesh=PlantCompiler.BuildShapeMesh(shape,detail);
            bool left=false,right=false,notch=false,tip=false;
            for(int i=0;i<mesh.VertexCount;i++)
            {
                var p=mesh.Position(i);var n=mesh.NormalAt(i);
                if(!p.IsFinite||!n.IsFinite||n.LengthSq<.25)return "invalid leaf at LOD "+detail;
                left|=p.X<-.2&&p.Z<-.3;right|=p.X>.2&&p.Z<-.3;
                notch|=Math.Abs(p.X)<1e-6&&Math.Abs(p.Z)<1e-6;
                tip|=p.Z>.99;
            }
            if(!left||!right||!notch||!tip)return "arrow outline lost at LOD "+detail;
        }
        return null;
    }
}
