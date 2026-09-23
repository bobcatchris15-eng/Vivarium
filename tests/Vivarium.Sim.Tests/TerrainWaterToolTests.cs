using Vivarium.Sim.Core;
using Vivarium.Sim.Persistence;
using Vivarium.Sim.Tools;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Tests;

[Trait("Suite", "Tools")]
public class TerrainWaterToolTests
{
    [Fact]
    public void RaiseAndLowerChangeGroundUnderTheBrushOnlyAndRespectLimits()
    {
        var w = TestUtil.FlatWorld();
        var t = new ToolActions(w);
        var c = new Vec2(1, 1); var far = new Vec2(-3, -1);
        double h0 = w.SurfaceHeight(c), hFar = w.SurfaceHeight(far);
        int v0 = w.Terrain.Version;
        Assert.True(t.Sculpt(c, 0.8, SculptMode.Raise, 0.5).Ok);
        Assert.True(w.SurfaceHeight(c) > h0 + 0.03);
        Assert.Equal(hFar, w.SurfaceHeight(far), 12);
        Assert.True(w.Terrain.Version > v0);
        for (int i = 0; i < 400; i++) t.Sculpt(c, 0.8, SculptMode.Lower, 0.5);
        Assert.Equal(w.Terrain.MinHeight, w.SurfaceHeight(c), 9);
        t.Sculpt(c, 0.8, SculptMode.Lower, 0.5);
        Assert.Equal(w.Terrain.MinHeight, w.SurfaceHeight(c), 9);   // clamped, never below the limit
        Assert.False(t.Sculpt(new Vec2(40, 0), 0.8, SculptMode.Raise, 0.5).Ok);
    }

    [Fact]
    public void SmoothingFlattensABump()
    {
        var w = TestUtil.FlatWorld();
        var t = new ToolActions(w);
        var c = new Vec2(0, 0);
        t.Sculpt(c, 0.4, SculptMode.Raise, 0.5);
        double bump = w.SurfaceHeight(c) - w.SurfaceHeight(new Vec2(0.9, 0));
        for (int i = 0; i < 20; i++) t.Sculpt(c, 1.0, SculptMode.Smooth, 0.25);
        double after = w.SurfaceHeight(c) - w.SurfaceHeight(new Vec2(0.9, 0));
        Assert.True(after < bump * 0.5, $"bump {bump:0.000} → {after:0.000}");
    }

    [Fact]
    public void DiggingBelowTheWaterTableMakesAPondAndPropsStaySeated()
    {
        var w = TestUtil.FlatWorld(edit: d => d.Water.WaterTable = 0.3);
        var t = new ToolActions(w);
        var c = new Vec2(0.5, -0.5);
        Assert.False(w.Water.IsWet(c));
        var rock = w.Placement.PlaceRock(c + new Vec2(0.5, 0), 0.2, 0, 7);
        Assert.True(rock.Ok);
        for (int i = 0; i < 12; i++) t.Sculpt(c, 1.0, SculptMode.Lower, 0.5);
        t.EndSculptStroke();
        w.Step(60);
        Assert.True(w.Water.IsWet(c), "ground dug below the water table fills with water");
        Assert.True(w.Water.SurfaceAt(c) is var s && Math.Abs(s - 0.3) < 0.01);
        var r = (Rock)w.Props.Find(rock.Id)!;
        Assert.Equal(w.Terrain.Height(r.Position) - r.SizeY * 0.25, r.Y, 9);
        Assert.Empty(w.CheckInvariants());
    }

    [Fact]
    public void SculptedTerrainSurvivesSaveAndLoadExactly()
    {
        var w = TestUtil.FlatWorld();
        var t = new ToolActions(w);
        Assert.Null(w.Terrain.ExportDelta());
        t.Sculpt(new Vec2(1, 0), 1.2, SculptMode.Raise, 0.5);
        t.Sculpt(new Vec2(-1, 1), 0.7, SculptMode.Lower, 0.5);
        t.EndSculptStroke();
        w.Step(30);
        string path = Path.Combine(TestUtil.TempDir(), "sculpt" + SaveSystem.Extension);
        Assert.True(SaveSystem.Save(w, path).Ok);
        var r = SaveSystem.Load(w.Content, path);
        Assert.True(r.Ok, r.Message);
        Assert.Equal(w.Terrain.DigestHex(), r.World!.Terrain.DigestHex());
        Assert.Equal(TestUtil.Digest(w), TestUtil.Digest(r.World));
        w.Step(500); r.World.Step(500);
        Assert.Equal(TestUtil.Digest(w), TestUtil.Digest(r.World));
    }

    [Fact]
    public void PourAndDrainAreTalliedInTheWaterBudget()
    {
        var w = TestUtil.FlatWorld();
        var t = new ToolActions(w);
        var c = new Vec2(0, 0);
        double v0 = w.Water.Volume();
        Assert.True(t.PourWater(c, 0.6, 0.5).Ok);
        double poured = w.Water.Budget.ToolInflow;
        Assert.Equal(w.Content.Tools.PourRate * 0.5, poured, 12);
        Assert.Equal(v0 + poured, w.Water.Volume(), 9);
        Assert.True(t.DrainWater(c, 0.6, 0.5).Ok);
        Assert.Equal(v0 + poured - w.Water.Budget.ToolRemoval, w.Water.Volume(), 9);
        while (t.DrainWater(c, 2.0, 0.5).Ok) { }
        Assert.True(w.Water.Volume() >= 0 && w.Water.AllFinite());
        Assert.False(t.PourWater(new Vec2(40, 0), 0.5, 0.5).Ok);
    }

    [Fact]
    public void SpringsToggleAreCappedAndPersist()
    {
        var w = TestUtil.FlatWorld();
        var t = new ToolActions(w);
        var p = new Vec2(1, 1);
        var add = t.ToggleSpring(p);
        Assert.True(add.Ok);
        Assert.Single(w.Water.Springs);
        double before = w.Water.Budget.SpringInflow;
        w.Step(360);
        Assert.True(w.Water.Budget.SpringInflow > before);
        string path = Path.Combine(TestUtil.TempDir(), "spring" + SaveSystem.Extension);
        Assert.True(SaveSystem.Save(w, path).Ok);
        Assert.Single(SaveSystem.Load(w.Content, path).World!.Water.Springs);
        Assert.True(t.ToggleSpring(p + new Vec2(0.1, 0)).Ok);   // clicking near it removes it
        Assert.Empty(w.Water.Springs);
        for (int i = 0; i < w.Content.Tools.MaxSprings; i++) Assert.True(t.ToggleSpring(new Vec2(-3 + i * 0.8, -2)).Ok);
        Assert.False(t.ToggleSpring(new Vec2(2, 2)).Ok);
    }
}
