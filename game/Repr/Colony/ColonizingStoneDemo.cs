using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Sim.Repr;
using SVec2 = Vivarium.Sim.Core.Vec2;

namespace Vivarium.Repr;

/// <summary>Fixed-seed, state-driven growth and moisture comparison on the soil cube.</summary>
public sealed class ColonizingStoneDemo : IReprDemo
{
    public string Name => "colonizing_stone";
    private readonly List<ReprShot> _shots = new();
    public IReadOnlyList<ReprShot> Shots => _shots;
    private readonly Dictionary<string, (SubstrateRenderer soil, ColonyRenderer colony, ColonySystem system)> _states = new();
    private Node3D _root = null!;
    private ReprDemoContext _ctx = null!;
    private int _index;
    private static Vector3 V(double x, double y, double z) => new((float)x, (float)y, (float)z);

    public void Build(Node3D root, ReprDemoContext ctx)
    {
        _root = root; _ctx = ctx;
        foreach (string state in new[] { "early", "mature", "dry" })
        {
            var s = new LayeredSubstrate(SubstrateSpec.TestCube(7));
            const double dt = 21600;
            for (int i = 0; i < 120; i++) s.Water.Step(dt, p => (3.0 - 2.7 * Math.Clamp(p.X, 0, 1)) * 1e-3 / ColonySystem.Day, 2e-3 / ColonySystem.Day, 0);
            var c = new ColonySystem(s, 7, p => 0.6);
            for (int x = 0; x < 4; x++) for (int z = 0; z < 3; z++)
            {
                var p = new SVec2(0.12 + x * 0.24, 0.20 + z * 0.30);
                c.Seed("cushion_moss", p, 0.04); c.Seed("crust_lichen", p, 0.04);
                c.Seed("leaf_lichen", p, 0.035); c.Seed("branch_lichen", p, 0.035);
            }
            int days = state == "early" ? 15 : 365;
            for (int i = 0; i < days * 4; i++) c.Step(dt);
            if (state == "dry")
                for (int i = 0; i < 80; i++) { s.Water.Step(dt, _ => 0, 8e-3 / ColonySystem.Day, 0); c.Step(dt); }
            var soil = new SubstrateRenderer { Name = "Soil_" + state, Active = false };
            var colony = new ColonyRenderer { Name = "Colony_" + state, Visible = false };
            root.AddChild(soil); root.AddChild(colony); soil.Init(s); soil.BindColony(c); colony.Init(c); colony.SetProcess(false);
            _states.Add(state, (soil, colony, c));
            ctx.Log($"colonizing_stone {state}: moss={c.CoveredArea("cushion_moss"):0.0000} crust={c.CoveredArea("crust_lichen"):0.0000} foliose={c.CoveredArea("leaf_lichen"):0.0000} fruticose={c.CoveredArea("branch_lichen"):0.0000}");
        }
        var sub = _states["mature"].system.Substrate;
        double H(double x, double z) => sub.Height(new SVec2(x, z));
        foreach (string state in new[] { "early", "mature", "dry" })
        {
            _shots.Add(new ReprShot("whole", V(1.45, 1.25, 1.45), V(0.5, H(0.5,0.5), 0.5), 40, 120, state));
            _shots.Add(new ReprShot("rock", V(0.85, H(0.6,0.5)+0.22, 0.78), V(0.60, H(0.60,0.50), 0.50), 40, 120, state));
            _shots.Add(new ReprShot("wet_macro", V(0.20, H(0.18,0.30)+0.09, 0.40), V(0.18, H(0.18,0.30), 0.30), 40, 120, state));
            _shots.Add(new ReprShot("dry_macro", V(0.82, H(0.84,0.50)+0.09, 0.60), V(0.84, H(0.84,0.50), 0.50), 40, 120, state));
        }
        _shots.Add(new ReprShot("lod_mid", V(1.8,1.6,1.8), V(0.5,H(0.5,0.5),0.5), 40, 120, "mature"));
        _shots.Add(new ReprShot("lod_far", V(4,3.5,4), V(0.5,H(0.5,0.5),0.5), 40, 120, "mature"));
        _shots.Add(new ReprShot("local_edit", V(1.20,H(0.90,0.90)+0.32,1.20), V(0.90,H(0.90,0.90),0.90), 40, 120, "edit"));
    }
    public void ApplyVariant(string? variant)
    {
        foreach (var pair in _states.Values) { pair.soil.Active = false; pair.colony.Visible = false; pair.colony.SetProcess(false); }
        if (variant == null || !_states.TryGetValue(variant == "edit" ? "mature" : variant, out var state) || _index >= _shots.Count) return;
        var shot = _shots[_index++]; state.soil.Active = true; state.colony.Visible = true;
        long uploadsBefore=state.soil.ColonyTextureUploads, buildsBefore=state.colony.TilesBuilt, invalidBefore=state.colony.TilesInvalidated;
        long dirtyBefore=state.system.DirtyLog.Head;
        double areaBefore=state.system.CoveredArea("branch_lichen");
        if(variant == "edit") state.system.Seed("branch_lichen",new SVec2(0.90,0.90),0.055);
        float height = _root.GetViewport().GetVisibleRect().Size.Y;
        state.soil.SettleFor(shot.Eye, shot.FovDeg, height);
        state.colony.SettleFor(shot.Eye, shot.FovDeg, height);
        _ctx.Log($"colonizing_stone {shot.Name}/{variant}: colony built={state.colony.TilesBuilt} invalidated={state.colony.TilesInvalidated} field_uploads={state.soil.ColonyTextureUploads} field_last_ms={state.soil.LastColonyUploadMs:0.00} field_total_ms={state.soil.TotalColonyUploadMs:0.00}");
        if(variant == "edit")
        {
            double areaChange=state.system.CoveredArea("branch_lichen")-areaBefore;
            long dirtyChange=state.system.DirtyLog.Head-dirtyBefore, uploads=state.soil.ColonyTextureUploads-uploadsBefore;
            _ctx.Log($"colonizing_stone local edit: branch area delta={areaChange:F6} m2; dirty records +{dirtyChange}; field tile uploads +{uploads}; micro tile builds +{state.colony.TilesBuilt-buildsBefore}; micro invalidations +{state.colony.TilesInvalidated-invalidBefore}; substrate tiles={state.soil.TileCount}");
            if(areaChange<=0 || dirtyChange<=0 || uploads<=0) throw new InvalidOperationException("local colony edit did not change state, publish dirtiness, and upload a field tile");
        }
    }
}
