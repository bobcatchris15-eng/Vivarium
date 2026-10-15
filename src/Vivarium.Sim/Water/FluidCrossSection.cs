namespace Vivarium.Sim.Water;

/// <summary>Volume-preserving presentation of an unsupported water face. Thin sheets
/// retract toward a round filament at the Taylor-Culick capillary edge speed.</summary>
public readonly record struct FluidCrossSection(double WidthRadius,double ThicknessRadius,double ShapePower)
{
    /// <summary>Compact surface kernel along an already-connected trajectory. Smooth
    /// area density rather than independent parcel radii, then conserve its line integral.</summary>
    public static double[] ReconstructTubeVolumes(ReadOnlySpan<double> volumes,ReadOnlySpan<double> lengths)
    {
        if(volumes.Length!=lengths.Length)throw new ArgumentException("Trajectory sample counts differ");
        var areas=new double[volumes.Length];var work=new double[volumes.Length];double total=0;
        for(int i=0;i<areas.Length;i++)
        {
            if(!double.IsFinite(volumes[i])||volumes[i]<0||!double.IsFinite(lengths[i])||lengths[i]<=0)throw new ArgumentException("Invalid trajectory sample");
            areas[i]=volumes[i]/lengths[i];total+=volumes[i];
        }
        for(int pass=0;pass<2;pass++)
        {
            for(int i=0;i<areas.Length;i++)work[i]=areas[Math.Max(0,i-1)]*.25+areas[i]*.5+areas[Math.Min(areas.Length-1,i+1)]*.25;
            (areas,work)=(work,areas);
        }
        double integral=0;for(int i=0;i<areas.Length;i++)integral+=areas[i]*lengths[i];
        double scale=integral>0?total/integral:0;
        for(int i=0;i<areas.Length;i++)areas[i]*=lengths[i]*scale;
        return areas;
    }

    public static FluidCrossSection Reconstruct(double volume,double length,double aperture,double age)
    {
        double area=volume/Math.Max(1e-8,length),round=Math.Sqrt(area/Math.PI);
        double initialWidth=Math.Max(round,aperture*.5);
        const double sheetArea=3.7081493546;
        double initialThickness=area/(sheetArea*initialWidth);
        // Water/air surface tension ~0.072 N/m at room temperature; density 1000 kg/m3.
        double retractionSpeed=Math.Sqrt(.144/(1000*Math.Max(1e-10,initialThickness*2)));
        double width=Math.Max(round,initialWidth-retractionSpeed*Math.Max(0,age));
        bool roundJet=width<=round*2;
        double coefficient=roundJet?Math.PI:sheetArea;
        return new(width,area/(coefficient*width),roundJet?1:.5);
    }
}
