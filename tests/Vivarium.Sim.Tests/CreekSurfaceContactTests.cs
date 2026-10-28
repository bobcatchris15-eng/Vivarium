using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.Water;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Hydrology")]
public class CreekSurfaceContactTests
{
    [Fact]
    public void MovingCreekHasNoNearestCellHeightJump()
    {
        var w=TestUtil.FlatWorld();var hf=w.Terrain;hf.BeginEdit();
        for(int z=0;z<hf.Nz;z++)for(int x=0;x<hf.Nx;x++)
            hf.H[hf.VertexIndex(x,z)]=.5-.3*hf.VertexPos(x,z).X;
        hf.Touch();w.Liquid.OnTerrainChanged();const double spacing=.025;
        var cells=new List<SurfaceFluidCell>();
        for(int z=-3;z<=3;z++)for(int x=-4;x<=4;x++)
            cells.Add(new(x,z,(x%2==0?.006:.014)*spacing*spacing,.00001,0,-.3,0,.0003,.0003));
        w.Liquid.Restore(new(cells.ToArray(),[],0,0,spacing,new()));
        for(int x=-2;x<=2;x++)
        {
            double boundary=x*spacing;
            Assert.InRange(Math.Abs(w.Liquid.SurfaceAt(new(boundary-1e-7,.0125))-w.Liquid.SurfaceAt(new(boundary+1e-7,.0125))),0,1e-6);
        }
    }

    [Fact]
    public void SculptedVisibleBedStaysWithinHalfMillimetreOfPhysicalSupport()
    {
        var w = TestUtil.FlatWorld(edit: d => d.Diameter = 5);
        var hf = w.Terrain; hf.BeginEdit();
        for (int z=0; z<hf.Nz; z++) for (int x=0; x<hf.Nx; x++)
        {
            var p=hf.VertexPos(x,z);
            hf.H[hf.VertexIndex(x,z)] = .5 + .08*Math.Sin(p.X*17)*Math.Cos(p.Z*13);
        }
        hf.Touch();
        for (int z=0; z<120; z++) for (int x=0; x<120; x++)
        {
            var p = new Vec2(-1.2+x*.02,-1.2+z*.02);
            Assert.InRange(Math.Abs(TerrainMesh.RenderHeight(hf,p)-hf.Height(p)),0,.000500001);
        }
    }

    [Fact]
    public void DeepMovingPoolReconstructsHeadInsteadOfFollowingBed()
    {
        var w=TestUtil.FlatWorld(); var hf=w.Terrain; hf.BeginEdit();
        for(int z=0;z<hf.Nz;z++)for(int x=0;x<hf.Nx;x++)
            hf.H[hf.VertexIndex(x,z)]=.5+.12*hf.VertexPos(x,z).X;
        hf.Touch(); w.Liquid.OnTerrainChanged();
        const double spacing=.025,head=.58;
        var cells=new List<SurfaceFluidCell>();
        for(int z=-4;z<4;z++)for(int x=-4;x<4;x++)
        {
            double depth=head-(.5+.12*(x+.5)*spacing);
            cells.Add(new(x,z,depth*spacing*spacing,.00001,0,0,0,depth*spacing,depth*spacing));
        }
        w.Liquid.Restore(new(cells.ToArray(),[],0,0,spacing,new()));
        for(int i=0;i<100;i++) Assert.Equal(head,w.Liquid.SurfaceAt(new(-.06+i*.0012,0)),9);
    }
}

