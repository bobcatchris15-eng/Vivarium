using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Repr.Water;
using Vivarium.Sim.World;

var root = Directory.GetCurrentDirectory();
var content = ContentLoader.Load(new DirectoryContentSource(Path.Combine(root, "game", "content")));
var fixture = MatureSpringFixture.Create(true);
fixture.SeedPool();
var maturity = fixture.Mature();
Console.WriteLine($"MATURITY mature={maturity.IsMature} {maturity.Report}");
if (!maturity.IsMature) throw new Exception("Fixture did not mature");
var state = fixture.Dynamics.Export();
VivariumWorld MakeWorld()
{
    var w = VivariumWorld.CreateBaseline(content, fixture.Descriptor.Clone());
    MatureSpringFixture.ApplyTerrain(w.Terrain, true);
    w.Water.RefreshBed(w.Terrain);
    w.Water.Springs.Add(fixture.Spring);
    w.Liquid.Erosion.Enabled = false;
    w.Liquid.Restore(state);
    return w;
}
var legacy = MakeWorld();
// Separate descriptor: upgrade mutates descriptor as well as physical terrain.
var upgraded = MakeWorld();
var before = upgraded.Liquid.Volume;
if (!SoilSurfaceUpgrade.Apply(upgraded)) throw new Exception("Upgrade did not apply");
if (Math.Abs(upgraded.Liquid.Volume - before) > 1e-9) throw new Exception("Upgrade changed volume");
var points = legacy.Liquid.CaptureRenderState().Cells.Where(c => c.Position.Length < .10 && c.Depth > .01).Select(c => c.Position).ToArray();
if (points.Length == 0) throw new Exception("No pool points");
void Run(string name, VivariumWorld w)
{
    double initial = w.Liquid.Volume, inflow = w.Water.Budget.SpringInflow, outflow = w.Water.Budget.BoundaryOutflow;
    var previous = points.Select(w.Liquid.SurfaceAt).ToArray();
    var prevCells = w.Liquid.CaptureRenderState().Cells.ToDictionary(c => c.Position); double maxCellHead = 0; int wetTransitions = 0; double maxDelta = 0, squared = 0, afterFirstMax = 0; int count = 0, maxStep = -1;
    for (int i = 0; i < 40; i++)
    {
        w.Liquid.AdvancePhysical(.05);
        var cells = w.Liquid.CaptureRenderState().Cells;
        foreach (var c in cells) if (c.Position.Length < .10 && prevCells.TryGetValue(c.Position, out var old)) maxCellHead = Math.Max(maxCellHead, Math.Abs(c.Head-old.Head));
        prevCells = cells.ToDictionary(c => c.Position);
        if (cells.Any(c => !double.IsFinite(c.Volume) || c.Volume < 0 || !double.IsFinite(c.Head) || !double.IsFinite(c.Velocity.Length))) throw new Exception($"{name}: invalid fluid");
        for (int p = 0; p < points.Length; p++)
        {
            double h = w.Liquid.SurfaceAt(points[p]);
            if (double.IsFinite(h) != double.IsFinite(previous[p])) wetTransitions++;
            if (double.IsFinite(h) && double.IsFinite(previous[p])) { double d = Math.Abs(h - previous[p]); if (d > maxDelta) { maxDelta = d; maxStep = i; } if (i > 0) afterFirstMax = Math.Max(afterFirstMax, d); squared += d*d; count++; }
            previous[p] = h;
        }
    }
    double error = w.Liquid.Volume - initial - (w.Water.Budget.SpringInflow-inflow) + (w.Water.Budget.BoundaryOutflow-outflow);
    if (!double.IsFinite(error) || Math.Abs(error) > 1e-7) throw new Exception($"{name}: conservation error {error:R}");
    var pool = w.Liquid.CaptureRenderState().Cells.Where(c => c.Position.Length < .15 && c.Depth > .001).ToArray();
    if (pool.Length == 0 || count == 0) throw new Exception($"{name}: pool lost");
    Console.WriteLine($"RESULT {name} pool_cells={pool.Length} max_cell_head_delta={maxCellHead:R} wet_transitions={wetTransitions} volume={w.Liquid.Volume:R} conservation_error={error:R} mean_head={pool.Average(c=>c.Head):R} max_speed={pool.Max(c=>c.Velocity.Length):R} samples={count} max_step_height_delta={maxDelta:R} max_step_index={maxStep} after_first_max={afterFirstMax:R} rms_step_height_delta={Math.Sqrt(squared/count):R}");
}
Run("legacy", legacy);
Run("upgraded", upgraded);
legacy.Liquid.AdvancePhysical(20); upgraded.Liquid.AdvancePhysical(20);
Run("legacy_settled", legacy); Run("upgraded_settled", upgraded);
