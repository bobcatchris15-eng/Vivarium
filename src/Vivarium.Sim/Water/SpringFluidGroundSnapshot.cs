using Vivarium.Sim.Core;
namespace Vivarium.Sim.Water;

/// <summary>Immutable cached ground for render workers; terrain/prop edits produce a new snapshot.</summary>
public sealed class SpringFluidGroundSnapshot
{
    private readonly double[] _height;
    private readonly int _width;
    private readonly double _origin, _step;
    public SpringFluidGroundSnapshot(double[] height, int width, double origin, double step)
    { _height=(double[])height.Clone(); _width=width; _origin=origin; _step=step; }
    public double Height(Vec2 p)
    {
        double x=(p.X-_origin)/_step,z=(p.Z-_origin)/_step;
        int i=Math.Clamp((int)Math.Floor(x),0,_width-2);
        int j=Math.Clamp((int)Math.Floor(z),0,_height.Length/_width-2);
        double u=Math.Clamp(x-i,0,1),v=Math.Clamp(z-j,0,1);
        int k=j*_width+i;
        return (_height[k]*(1-u)+_height[k+1]*u)*(1-v)
            +(_height[k+_width]*(1-u)+_height[k+_width+1]*u)*v;
    }
}
