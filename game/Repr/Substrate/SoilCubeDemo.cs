using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;
using Vivarium.Sim.Repr;
using SVec2 = Vivarium.Sim.Core.Vec2;
using SVec3 = Vivarium.Sim.Core.Vec3;

namespace Vivarium.Repr;

/// <summary>
/// The "living soil cube" (S2): SubstrateSpec.TestCube(seed 7) as a free-standing 1 m block rendered by
/// <see cref="SubstrateRenderer"/>. Variants: dry | wet | exaggerated (wet, exaggerate = 1) | dug (wet, 12 cm bowl).
/// Two substrates are simulated (dry, wet); the wet one is reused for exaggerated and for the dig, which is why
/// the dug shots come last. Every shot starts with a blocking settle so each capture has its level meshes built.
/// </summary>
public sealed class SoilCubeDemo : IReprDemo
{
    public string Name => "soil_cube";

    private readonly List<ReprShot> _shots = new();
    public IReadOnlyList<ReprShot> Shots => _shots;

    private const ulong Seed = 7;
    private LayeredSubstrate _dry = null!, _wet = null!;
    private SubstrateRenderer _rDry = null!, _rWet = null!;
    private ReprDemoContext _ctx = null!;
    private Node3D _root = null!;
    private int _idx;
    private bool _dug;
    private SVec3 _digCentre;
    private const double DigRadius = 0.12;

    private static Vector3 G(double x, double y, double z) => new((float)x, (float)y, (float)z);

    private static LayeredSubstrate MakeSubstrate()
    {
        var s = new LayeredSubstrate(SubstrateSpec.TestCube(Seed));
        double h1 = s.Height(new SVec2(0.12, 0.35)), h2 = s.Height(new SVec2(0.10, 0.70));
        // Fine roots under two spots near the low-X wall so rootlets cross the cut face.
        s.AddRootDensity(new SVec3(0.12, h1, 0.35), 0.24, 0.45, 160);
        s.AddRootDensity(new SVec3(0.10, h2, 0.70), 0.22, 0.40, 120);
        return s;
    }

    private static double MeanTopSat(LayeredSubstrate s, double x0, double x1)
    {
        double sum = 0; int n = 0;
        for (int i = 0; i < 20; i++)
            for (int j = 0; j < 20; j++)
            {
                double x = (i + 0.5) * 0.05;
                if (x < x0 || x > x1) continue;
                int c = s.ColumnAt(new SVec2(x, (j + 0.5) * 0.05));
                if (c < 0 || s.IsOutcrop(c)) continue;
                sum += s.Water.TopSaturation(c); n++;
            }
        return n > 0 ? sum / n : 0;
    }

    public void Build(Node3D root, ReprDemoContext ctx)
    {
        _ctx = ctx; _root = root;
        var sw = Stopwatch.StartNew();
        _dry = MakeSubstrate();
        _wet = MakeSubstrate();
        ctx.Log($"soil_cube: substrates built in {sw.Elapsed.TotalMilliseconds:0} ms; bounds Y {_dry.Bounds.Min.Y:0.000}..{_dry.Bounds.Max.Y:0.000}");

        // ---- dry: evaporation only until the dry side's top layer is dry
        sw.Restart();
        _dry.Water.Step(1, _ => 0, 0, 0); // publish the initial state
        int steps = 0;
        double dryStart = MeanTopSat(_dry, 0.75, 1.0);
        while (steps < 4000 && MeanTopSat(_dry, 0.75, 1.0) > 0.18)
        {
            _dry.Water.Step(900, _ => 0, 1.5e-7, 0);
            steps++;
        }
        ctx.Log($"soil_cube: dry run {steps} x 900 s ({steps * 0.25 / 24.0:0.0} d), dry-side top sat {dryStart:0.00} -> {MeanTopSat(_dry, 0.75, 1.0):0.00}, wet-side {MeanTopSat(_dry, 0.0, 0.25):0.00}; {sw.Elapsed.TotalMilliseconds:0} ms");

        // ---- wet: 2 h of 10 mm/h
        sw.Restart();
        const double rain = 10e-3 / 3600.0;
        for (int i = 0; i < 240; i++) _wet.Water.Step(30, _ => rain, 0, 0);
        double standMax = 0, filmMax = 0;
        for (int c = 0; c < 400; c++)
        {
            var ctr = _wet.ColumnCentre(c);
            if (_wet.SurfaceSample(new SVec3(ctr.X, _wet.Height(ctr), ctr.Z), 0.01, out var ss))
            { standMax = Math.Max(standMax, ss.StandingWater * 1000); filmMax = Math.Max(filmMax, ss.FilmWater * 1000); }
        }
        ctx.Log($"soil_cube: wet run 2 h; top sat dry-side {MeanTopSat(_wet, 0.75, 1.0):0.00} wet-side {MeanTopSat(_wet, 0.0, 0.25):0.00}; max standing {standMax:0.00} mm, max film {filmMax:0.000} mm; {sw.Elapsed.TotalMilliseconds:0} ms");

        // ---- renderers and specimen-block ground
        double yBase = _dry.Bounds.Min.Y;
        root.AddChild(new MeshInstance3D
        {
            Name = "BlockGround", Mesh = new PlaneMesh { Size = new Vector2(30f, 30f) }, Position = new Vector3(0, (float)yBase, 0),
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.035f, 0.035f, 0.04f), Roughness = 1f },
        });
        _rDry = new SubstrateRenderer { Name = "SoilDry" };
        _rWet = new SubstrateRenderer { Name = "SoilWet" };
        root.AddChild(_rDry); root.AddChild(_rWet);
        _rDry.Init(_dry); _rWet.Init(_wet);
        _rDry.Active = false; _rWet.Active = false;
        sw.Restart();
        _rDry.BuildAllNow();
        ctx.Log($"soil_cube: dry first build, all tiles x 3 levels, inline {sw.Elapsed.TotalMilliseconds:0} ms; {_rDry.CountersLine()}");
        sw.Restart();
        _rWet.BuildAllNow();
        ctx.Log($"soil_cube: wet first build {sw.Elapsed.TotalMilliseconds:0} ms; {_rWet.CountersLine()}");

        BuildShots();
    }

    private void BuildShots()
    {
        var s = _wet;
        Func<double, double, double> H = (x, z) => s.Height(new SVec2(Math.Clamp(x, 0, 1), Math.Clamp(z, 0, 1)));
        double yMin = s.Bounds.Min.Y;
        double midY = 0.5 * (yMin + H(0.5, 0.5));

        // ---- outcrop geometry from the column classification
        var rockCols = new List<SVec2>();
        for (int i = 0; i < 20; i++)
            for (int j = 0; j < 20; j++)
            {
                var p = new SVec2((i + 0.5) * 0.05, (j + 0.5) * 0.05);
                if (s.IsOutcrop(s.ColumnAt(p))) rockCols.Add(p);
            }
        SVec2 rc = new(0.5, 0.5);
        if (rockCols.Count > 0)
        {
            double sx = 0, sz = 0; foreach (var p in rockCols) { sx += p.X; sz += p.Z; }
            rc = new SVec2(sx / rockCols.Count, sz / rockCols.Count);
        }
        _ctx.Log($"soil_cube: outcrop columns {rockCols.Count}, centroid ({rc.X:0.00},{rc.Z:0.00})");
        // soil/rock boundary point facing the default camera (+X,+Z)
        SVec2 bp = rc; double bestScore = double.NegativeInfinity;
        foreach (var p in rockCols)
            foreach (var d in new (double, double)[] { (0.05, 0), (-0.05, 0), (0, 0.05), (0, -0.05) })
            {
                var q = new SVec2(p.X + d.Item1, p.Z + d.Item2);
                int c = s.ColumnAt(q);
                if (c < 0 || s.IsOutcrop(c)) continue;
                var mid = new SVec2(p.X + 0.5 * d.Item1, p.Z + 0.5 * d.Item2);
                double score = (mid.X - rc.X) + (mid.Z - rc.Z);
                if (score > bestScore) { bestScore = score; bp = mid; }
            }
        double ox = bp.X - rc.X, oz = bp.Z - rc.Z, ol = Math.Max(Math.Sqrt(ox * ox + oz * oz), 1e-6);
        ox /= ol; oz /= ol;

        // ---- a stone that breaks the surface or the low-X wall
        Stone? pick = null; double pickScore = double.MinValue; string pickWhy = "none";
        foreach (var st in s.StonesIn(s.Bounds))
        {
            double hs = H(st.Centre.X, st.Centre.Z);
            double up = Math.Sqrt(Math.Pow(st.AxisA.Y * st.Semi.X, 2) + Math.Pow(st.AxisB.Y * st.Semi.Y, 2) + Math.Pow(st.AxisC.Y * st.Semi.Z, 2));
            double prot = st.Centre.Y + up - hs;
            bool wall = st.Centre.X < st.Semi.X * 0.7 && st.Centre.Y < hs - 0.03 && st.Centre.Y > yMin + 0.05 && st.Centre.Z > 0.1 && st.Centre.Z < 0.9;
            bool surf = prot > 0.006 && st.Centre.X > 0.05 && st.Centre.X < 0.95 && st.Centre.Z > 0.05 && st.Centre.Z < 0.95;
            if (!wall && !surf) continue;
            double score = (wall ? 1.0 : 0.0) + (wall ? st.Semi.X * 10 : prot * 10) - 0.3 * Math.Abs(st.Centre.Z - 0.45);
            if (score > pickScore) { pickScore = score; pick = st; pickWhy = wall ? "wall" : "surface"; }
        }
        _ctx.Log(pick is { } pk ? $"soil_cube: stone pick ({pickWhy}) centre ({pk.Centre.X:0.000},{pk.Centre.Y:0.000},{pk.Centre.Z:0.000}) semi ({pk.Semi.X:0.000},{pk.Semi.Y:0.000},{pk.Semi.Z:0.000})" : "soil_cube: no stone breaks a surface");

        // ---- dig site: low-X side, 20 cm in, away from the knoll
        double dz = Math.Abs(0.2 - rc.Z) > Math.Abs(0.8 - rc.Z) ? 0.2 : 0.8;
        _digCentre = new SVec3(0.20, H(0.20, dz), dz);

        void Pair(string variant)
        {
            // whole
            _shots.Add(new ReprShot("whole", G(1.7, 1.25, 1.7), G(0.5, midY, 0.5), 40f, 120, variant));
            // top (~50 cm above)
            _shots.Add(new ReprShot("top", G(0.9, 1.1, 0.5), G(0.35, H(0.35, 0.5), 0.5), 40f, 120, variant));
            // top_macro: 6.5 cm above the surface, ~10 cm of ground in view
            _shots.Add(MacroTop("top_macro", variant, 0));
            // wall: low-X cut face, all horizons
            double soilD = s.HorizonBoundaryDepth(new SVec2(0, 0.4), 4);
            double wy = H(0, 0.4) - 0.5 * (soilD + 0.12);
            _shots.Add(new ReprShot("wall", G(-0.45, wy + 0.02, 0.24), G(0, wy, 0.42), 40f, 120, variant));
            // wall_macro: 8 cm from the wall at the humus / mineral boundary
            double d2 = s.HorizonBoundaryDepth(new SVec2(0, 0.35), 2);
            double my = H(0, 0.35) - d2;
            _shots.Add(new ReprShot("wall_macro", G(-0.08, my + 0.004, 0.33), G(0, my, 0.35), 40f, 120, variant));
            // outcrop: ~25 cm from where soil laps onto the knoll
            double bh = H(bp.X, bp.Z);
            _shots.Add(new ReprShot("outcrop", G(bp.X + ox * 0.17, bh + 0.17, bp.Z + oz * 0.17), G(bp.X, bh, bp.Z), 40f, 120, variant));
            // stone: 15 cm from the picked stone
            if (pick is { } st)
            {
                var c = st.Centre;
                Vector3 eye, tgt = G(c.X, c.Y, c.Z);
                if (pickWhy == "wall") eye = G(c.X - 0.15, c.Y + 0.02, c.Z - 0.03);
                else
                {
                    double hh = H(c.X, c.Z);
                    eye = G(c.X + 0.10, hh + 0.11, c.Z + 0.06);
                    tgt = G(c.X, Math.Max(c.Y, hh - 0.005), c.Z);
                }
                _shots.Add(new ReprShot("stone", eye, tgt, 40f, 120, variant));
            }
            // stability pair: identical to top_macro with eye and target translated 1 mm in X
            _shots.Add(MacroTop("top_macro_shift", variant, 0.001));
        }

        Pair("dry");
        Pair("wet");
        _shots.Add(MacroTop("top_macro", "exaggerated", 0));
        double sd = s.HorizonBoundaryDepth(new SVec2(0, 0.4), 4);
        double ey = H(0, 0.4) - 0.5 * (sd + 0.12);
        _shots.Add(new ReprShot("wall", G(-0.45, ey + 0.02, 0.24), G(0, ey, 0.42), 40f, 120, "exaggerated"));
        double e2 = s.HorizonBoundaryDepth(new SVec2(0, 0.35), 2);
        _shots.Add(new ReprShot("wall_macro", G(-0.08, H(0, 0.35) - e2 + 0.004, 0.33), G(0, H(0, 0.35) - e2, 0.35), 40f, 120, "exaggerated"));
        _shots.Add(new ReprShot("seam", G(0.95, 0.72, 0.95), G(0.25, H(0.25, 0.25), 0.25), 55f, 120, "seam"));
        _shots.Add(new ReprShot("far", G(10, 6, 10), G(0.5, midY, 0.5), 40f, 120, "far"));
        _shots.Add(new ReprShot("hole", G(_digCentre.X + 0.20, _digCentre.Y + 0.28, _digCentre.Z), G(_digCentre.X, _digCentre.Y - 0.08, _digCentre.Z), 40f, 120, "dug"));
    }

    private ReprShot MacroTop(string name, string variant, double shiftX)
    {
        var s = _wet;
        double px = 0.30 + shiftX, pz = 0.62;
        double ex = px + 0.075, ez = pz - 0.01;
        double hp = s.Height(new SVec2(px, pz)), he = s.Height(new SVec2(ex, ez));
        return new ReprShot(name, G(ex, he + 0.065, ez), G(px, hp, pz), 38f, 120, variant);
    }

    public void ApplyVariant(string? variant)
    {
        if (variant == null)
        {
            _rDry.Active = false; _rWet.Active = false;
            return;
        }
        var shot = _idx < _shots.Count ? _shots[_idx] : null;
        _idx++;
        bool dry = variant == "dry";
        var r = dry ? _rDry : _rWet;
        (dry ? _rWet : _rDry).Active = false;
        r.Active = true;
        r.Exaggerate = variant == "exaggerated" ? 1f : 0f;
        r.MaxStepPixels = variant == "seam" ? 60 : 9;
        if (variant == "dug" && !_dug)
        {
            _dug = true;
            r.Drain();
            long b0 = r.TilesRebuilt, q0 = r.RebuildRequests;
            var sw = Stopwatch.StartNew();
            _wet.Dig(_digCentre, DigRadius, 7200);
            r.PollDirty();
            r.Drain();
            _ctx.Log($"soil_cube: dig r={DigRadius} m at ({_digCentre.X:0.00},{_digCentre.Y:0.000},{_digCentre.Z:0.00}): rebuild requests {q0}->{r.RebuildRequests}, tiles rebuilt {b0}->{r.TilesRebuilt}, of {r.TileCount} tiles; {sw.Elapsed.TotalMilliseconds:0} ms");
        }
        if (shot != null)
        {
            var vp = _root.GetViewport();
            r.SettleFor(shot.Eye, shot.FovDeg, vp.GetVisibleRect().Size.Y);
            _ctx.Log($"soil_cube: {shot.Name}/{variant}: levels [{r.ShownLevels()}] tris {r.ShownTriangles()}; {r.CountersLine()}");
        }
    }
}
