using System.Diagnostics;
using System.Text.Json;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

// Run from repository root: dotnet run --project scripts/map-review -- OUT SECONDS [PRESET]
// Diagnostics use real props and production fluid, fixed terrain, and no biological time advance.
string output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
int seconds = args.Length > 1 ? int.Parse(args[1]) : 0;
var content = ContentLoader.Load(new DirectoryContentSource(Path.GetFullPath("game/content")));
var summaries = new List<object>();
foreach (var id in content.Presets.Keys.Order())
{
    if (args.Length > 2 && id != args[2]) continue;
    var d = content.PresetOrThrow(id);
    var errors = d.Validate(); if (errors.Count > 0) throw new Exception(id + ": " + string.Join("; ", errors));
    var domain = new HexDomain(d.Diameter); var terrain = Heightfield.Generate(d, domain);
    var wetDepths = new List<double>(); int groundPoints = 0;
    for(int z=0;z<terrain.Nz;z++) for(int x=0;x<terrain.Nx;x++)
    {
        var p=terrain.VertexPos(x,z); if(!domain.Contains(p)) continue; groundPoints++;
        double depth=d.Water.WaterTable-terrain.H[terrain.VertexIndex(x,z)]; if(depth>0)wetDepths.Add(depth);
    }
    double waterFraction=(double)wetDepths.Count/groundPoints;
    var banksDescriptor = d.Clone(); banksDescriptor.Terrain.Features.RemoveAll(f => f.BedHeight.HasValue);
    var banks = Heightfield.Generate(banksDescriptor, domain);
    var profiles = new List<object>(); var cuts = new List<double>(); double maxRise = 0;
    foreach (var f in d.Terrain.Features.Where(f => f.Type == "channel" && f.BedHeight.HasValue))
    {
        var a = new Vec2(f.X, f.Z); var ab = new Vec2(f.ToX, f.ToZ) - a;
        int n = (int)Math.Ceiling(ab.Length / .025); double previous = terrain.Height(a);
        var samples = new List<object>(); double rise = 0;
        for (int i = 0; i <= n; i++)
        {
            var p = a + ab * ((double)i / n); if (!domain.Contains(p)) break;
            double h = terrain.Height(p), cut = banks.Height(p) - h;
            if (i > 0) rise = Math.Max(rise, h - previous); previous = h;
            cuts.Add(cut); samples.Add(new { x = p.X, z = p.Z, h, cut });
        }
        maxRise = Math.Max(maxRise, rise); profiles.Add(new { samples, rise });
    }
    var slopes = new List<double>();
    for (int z = 1; z < terrain.Nz - 1; z++) for (int x = 1; x < terrain.Nx - 1; x++)
    {
        var p = terrain.VertexPos(x,z); if (!domain.Contains(p)) continue;
        if (d.Terrain.Features.Any(f => f.BedHeight.HasValue &&
            (f.Type == "basin" ? (p-new Vec2(f.X,f.Z)).Length < f.Radius+terrain.Step :
            SegmentDistance(p,new(f.X,f.Z),new(f.ToX,f.ToZ)) < f.Width+terrain.Step))) continue;
        double dx = (terrain.H[terrain.VertexIndex(x+1,z)]-terrain.H[terrain.VertexIndex(x-1,z)])/(2*terrain.Step);
        double dz = (terrain.H[terrain.VertexIndex(x,z+1)]-terrain.H[terrain.VertexIndex(x,z-1)])/(2*terrain.Step);
        slopes.Add(Math.Sqrt(dx*dx+dz*dz));
    }
    var timer = Stopwatch.StartNew(); var frames = new List<object>();
    if (seconds > 0)
    {
        var w = VivariumWorld.Create(content,d,populate:false,warmupSurfaceWater:false); w.Liquid.Erosion.Enabled = false;
        double maxObstruction = 0;
        foreach (var f in d.Terrain.Features.Where(f=>f.Type=="channel"&&f.BedHeight.HasValue))
        {
            var a=new Vec2(f.X,f.Z); var ab=new Vec2(f.ToX,f.ToZ)-a;
            for(int i=0;i<=100;i++) { var p=a+ab*(i/100.0); if(w.Domain.Contains(p)) maxObstruction=Math.Max(maxObstruction,w.GroundHeight(p)-terrain.Height(p)); }
        }
        File.WriteAllText(Path.Combine(output,id+"-placement.json"),JsonSerializer.Serialize(new {corner=w.PilotTree?.Corner,maxObstruction,
            rocks=w.Props.Rocks.Select(r=>new{x=r.X,z=r.Z,radius=r.FootprintRadius}),
            logs=w.Props.Logs.Select(l=>new{x=l.X,z=l.Z}) }));
        for (int t = 1; t <= seconds; t++)
        {
            w.Liquid.AdvancePhysical(1);
            if (t != 5 && t != 15 && t != 30 && t != seconds) continue;
            var snapshot = w.Liquid.CaptureRenderState(); var b = w.Water.Budget;
            double balance = w.Liquid.Volume+b.GroundwaterRecharge+b.BoundaryOutflow+b.Evaporation+b.Infiltration-b.SpringInflow;
            if (Math.Abs(balance)>2e-9) throw new Exception(id+": conservation "+balance);
            var samples = snapshot.Cells.Select(c=>new { x=c.Position.X,z=c.Position.Z,depth=c.Depth,volume=c.Volume,vx=c.Velocity.X,vz=c.Velocity.Z }).ToArray();
            File.WriteAllText(Path.Combine(output,$"{id}-{t:000}-water.json"),JsonSerializer.Serialize(samples));
            frames.Add(new { t,cells=samples.Length,volume=w.Liquid.Volume,pond=b.GroundwaterRecharge,boundary=b.BoundaryOutflow,balance,
                furthest=snapshot.Cells.Where(c=>c.Depth>.0002).Select(c=>w.Water.Springs.Min(s=>(c.Position-s.Position).Length)).DefaultIfEmpty(0).Max(),
                medianDepth=Quantile(snapshot.Cells.Where(c=>c.Depth>.0002).Select(c=>c.Depth),.5),airborne=snapshot.Airborne.Length });
        }
        Console.WriteLine(id+" "+JsonSerializer.Serialize(frames.Last()));
    }
    var summary = new { id,name=d.Name,d.Diameter,d.Water.WaterTable,waterFraction,waterDepthMedian=Quantile(wetDepths,.5),waterDepthP90=Quantile(wetDepths,.9),seconds,wall=timer.Elapsed.TotalSeconds,maxRise,
        cutP10=Quantile(cuts,.1),cutMedian=Quantile(cuts,.5),cutP90=Quantile(cuts,.9),landSlopeP95=Quantile(slopes,.95),profiles,frames,
        terrain=new {terrain.Nx,terrain.Nz,terrain.OriginX,terrain.OriginZ,terrain.Step,terrain.H},
        springs=d.Water.Springs.Select(s=>new {s.X,s.Z,s.Discharge}),features=d.Terrain.Features };
    File.WriteAllText(Path.Combine(output,id+".json"),JsonSerializer.Serialize(summary));
    summaries.Add(new { id,maxRise,waterFraction,waterDepthMedian=Quantile(wetDepths,.5),waterDepthP90=Quantile(wetDepths,.9),cutP10=Quantile(cuts,.1),cutMedian=Quantile(cuts,.5),cutP90=Quantile(cuts,.9),landSlopeP95=Quantile(slopes,.95),frames });
}
File.WriteAllText(Path.Combine(output,args.Length>2?args[2]+"-summary.json":"summary.json"),JsonSerializer.Serialize(summaries,new JsonSerializerOptions{WriteIndented=true}));
static double Quantile(IEnumerable<double> values,double p) { var a=values.Order().ToArray();return a.Length==0?0:a[(int)((a.Length-1)*p)]; }
static double SegmentDistance(Vec2 p,Vec2 a,Vec2 b) { var ab=b-a;return (p-a-ab*Math.Clamp((p-a).Dot(ab)/Math.Max(1e-12,ab.LengthSq),0,1)).Length; }
