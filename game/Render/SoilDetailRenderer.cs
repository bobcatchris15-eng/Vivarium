using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Fields;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>
/// Camera-local topsoil detail. Mineral crumbs/clods remain substrate dressing; leaves and twigs are a
/// deterministic view of authoritative fine/coarse surface litter mass, not invented organic debris.
/// </summary>
public partial class SoilDetailRenderer : Node3D
{
    private VivariumWorld _w = null!;
    private MultiMesh _crumbs = null!, _clods = null!, _twigs = null!, _flakes = null!, _fruit = null!, _blades = null!, _stalks = null!;
    private MultiMeshInstance3D _crumbMmi = null!, _clodMmi = null!, _twigMmi = null!, _flakeMmi = null!, _fruitMmi = null!, _bladeMmi = null!, _stalkMmi = null!;
    private double _accum = 999;
    private int _terrainVersion = int.MinValue;
    private long _litterRevision = -1;
    private long _lastLocalLitterDigest = -1;
    private int _lastQuality = -1;
    public Camera3D? Camera { get; set; }
    /// <summary>0 disables the layer entirely (lowest quality tier); 1 normal density; 2 dense/close-up.</summary>
    public int Quality { get; set; } = 1;
    // Fine detail reads only up close; keep the radius tight so nothing pops at a distance and so the
    // per-frame instance count (which scales with area) stays cheap.
    private const float CullRadius = 9f;
    private const float FadeStart = 6f;
    // Crumbs and clods are millimetre-scale: past a few metres they cover under a pixel, and sub-pixel triangles
    // are the most expensive thing an iGPU can draw (every one still pays a full 2x2 quad). Only litter and twigs,
    // which are big enough to read, go out to the full radius.
    private const float CrumbRadius = 2.5f, ClodRadius = 4.5f;
    private Vector3 _lastCam = new(float.MaxValue, 0, 0);

    private readonly List<Transform3D> _crumbXf = new(), _clodXf = new(), _twigXf = new(), _flakeXf = new(), _fruitXf = new(), _bladeXf = new(), _stalkXf = new();
    private readonly List<Color> _crumbCol = new(), _clodCol = new(), _twigCol = new(), _flakeCol = new(), _fruitCol = new(), _bladeCol = new(), _stalkCol = new();

    public void Build(VivariumWorld w)
    {
        _w = w;
        _buffers.Clear();
        foreach (var c in GetChildren()) c.QueueFree();

        var mat = new StandardMaterial3D
        {
            VertexColorUseAsAlbedo = true,
            VertexColorIsSrgb = true,
            Roughness = 0.95f,
            Metallic = 0f,
            AlbedoColor = Colors.White,
        };
        _crumbs = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = BuildClod(0.0035f, 6) };
        _clods = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = BuildClod(0.007f, 8) };
        _twigs = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = BuildTwig(mat) };
        _flakes = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = BuildFlake(mat) };
        _fruit = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = BuildClod(0.011f, 11) };
        _blades = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = BuildFillerBlade(mat) };
        _stalks = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = BuildFillerStalk(mat) };
        _crumbMmi = new MultiMeshInstance3D { Name = "SoilCrumbs", Multimesh = _crumbs, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = mat };
        _clodMmi = new MultiMeshInstance3D { Name = "SoilClods", Multimesh = _clods, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = mat };
        _twigMmi = new MultiMeshInstance3D { Name = "SoilTwigs", Multimesh = _twigs, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = mat };
        _flakeMmi = new MultiMeshInstance3D { Name = "LeafLitter", Multimesh = _flakes, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = mat };
        _fruitMmi = new MultiMeshInstance3D { Name = "FallenFruit", Multimesh = _fruit, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = mat };
        _bladeMmi = new MultiMeshInstance3D { Name = "AmbientGroundBlades", Multimesh = _blades, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = mat };
        _stalkMmi = new MultiMeshInstance3D { Name = "AmbientGroundStalks", Multimesh = _stalks, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, MaterialOverride = mat };
        AddChild(_crumbMmi); AddChild(_clodMmi); AddChild(_twigMmi); AddChild(_flakeMmi); AddChild(_fruitMmi); AddChild(_bladeMmi); AddChild(_stalkMmi);
        _accum = 999;
        _terrainVersion = int.MinValue;
        _litterRevision = -1;
        _lastLocalLitterDigest = -1;
        _lastQuality = -1;
    }

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("SoilDetail");
        if (_w == null) return;
        bool enabled = Quality > 0;
        _crumbMmi.Visible = _clodMmi.Visible = _twigMmi.Visible = _flakeMmi.Visible = _fruitMmi.Visible = _bladeMmi.Visible = _stalkMmi.Visible = enabled;
        if (!enabled) { _accum = 999; return; }
        _accum += delta;
        if (_accum < 1.0) return;

        Vector3 cam = Camera?.GlobalPosition ?? Vector3.Zero;
        bool moved = cam.DistanceSquaredTo(_lastCam) > 2.0f * 2.0f;
        bool terrainChanged = _terrainVersion != _w.Terrain.Version;
        bool qualityChanged = _lastQuality != Quality;

        if (!moved && !terrainChanged && !qualityChanged)
        {
            if (_litterRevision == _w.Litter.Revision) return;
            long localDigest = ComputeLocalLitterDigest(cam);
            if (localDigest == _lastLocalLitterDigest)
            {
                _litterRevision = _w.Litter.Revision;
                return;
            }
        }

        _accum = 0;
        _lastCam = cam;
        _terrainVersion = _w.Terrain.Version;
        _litterRevision = _w.Litter.Revision;
        _lastQuality = Quality;
        _lastLocalLitterDigest = ComputeLocalLitterDigest(cam);
        Refresh();
    }

    private long ComputeLocalLitterDigest(Vector3 cam)
    {
        var g = _w.Grid;
        int i0 = Mathf.Max(0, (int)((cam.X - CullRadius - g.OriginX) / g.CellSize));
        int i1 = Mathf.Min(g.Nx - 1, (int)((cam.X + CullRadius - g.OriginX) / g.CellSize));
        int j0 = Mathf.Max(0, (int)((cam.Z - CullRadius - g.OriginZ) / g.CellSize));
        int j1 = Mathf.Min(g.Nz - 1, (int)((cam.Z + CullRadius - g.OriginZ) / g.CellSize));
        int stride = Quality >= 2 ? 1 : 2;
        long hash = 17;
        for (int j = j0 - j0 % stride; j <= j1; j += stride)
        for (int i = i0 - i0 % stride; i <= i1; i += stride)
        {
            if (!g.InDomain(i, j)) continue;
            int idx = g.Index(i, j);
            long f = (long)(_w.Litter.FineMass[idx] * 1000.0);
            long c = (long)(_w.Litter.CoarseMass[idx] * 1000.0);
            long fr = (long)(_w.Litter.FruitMass[idx] * 1000.0);
            hash = hash * 31 + idx * 7 + f * 13 + c * 19 + fr * 23;
        }
        return hash;
    }

    private void Refresh()
    {
        var g = _w.Grid; var f = _w.Fields;
        Vector3 camPos = Camera?.GlobalPosition ?? Vector3.Zero;
        bool haveCam = Camera != null;
        float r2 = CullRadius * CullRadius;
        _crumbXf.Clear(); _crumbCol.Clear();
        _clodXf.Clear(); _clodCol.Clear();
        _twigXf.Clear(); _twigCol.Clear();
        _flakeXf.Clear(); _flakeCol.Clear();
        _fruitXf.Clear(); _fruitCol.Clear();
        _bladeXf.Clear(); _bladeCol.Clear();
        _stalkXf.Clear(); _stalkCol.Clear();

        var nearRocks = new List<Vivarium.Sim.World.Rock>();
        var nearLogs = new List<Vivarium.Sim.World.LogProp>();
        var nearGravel = new List<Vivarium.Sim.World.GravelPatch>();
        float searchRadius = CullRadius + 3.0f;
        var camP = new Vivarium.Sim.Core.Vec2(camPos.X, camPos.Z);
        foreach (var r in _w.Props.Rocks)
        {
            if (!haveCam || Vivarium.Sim.Core.Vec2.Distance(camP, r.Position) <= searchRadius + r.FootprintRadius)
                nearRocks.Add(r);
        }
        foreach (var l in _w.Props.Logs)
        {
            if (!haveCam || Vivarium.Sim.Core.Vec2.Distance(camP, l.Position) <= searchRadius + l.Length * 0.5 + l.Radius)
                nearLogs.Add(l);
        }
        foreach (var gr in _w.Props.Gravel)
        {
            if (!haveCam || Vivarium.Sim.Core.Vec2.Distance(camP, gr.Position) <= searchRadius + gr.Radius)
                nearGravel.Add(gr);
        }

        // Render-only filler deliberately avoids named flora. It can borrow a little colour/silhouette character
        // from the nearest plant outside that exclusion halo, but it never participates in simulation or competition.
        var nearFlora = new List<(Vivarium.Sim.Core.Vec2 P, double Exclusion, Color Tint, bool BladeBias, bool IsWoody, double CollarRadius)>();
        foreach (var plant in _w.Flora.Items)
        {
            var sp = _w.Content.FloraOrThrow(plant.SpeciesId);
            if (!haveCam || Vivarium.Sim.Core.Vec2.Distance(camP, plant.Position) <= searchRadius + Math.Max(0.4, plant.Radius(sp)))
            {
                var tint = new Color((float)sp.Color[0], (float)sp.Color[1], (float)sp.Color[2]);
                bool bladeBias = sp.Shape is "reed" or "tussock" or "veilblade_curtain" or "kinkcane_brake" || sp.Tags.Contains("grass");
                bool isWoody = sp.Shape == "tree" || sp.Tags.Contains("tree") || sp.Tags.Contains("woody");
                double collarR = Math.Max(0.18, plant.Radius(sp) * 0.35);
                nearFlora.Add((plant.Position, Math.Max(0.13, plant.Radius(sp) + 0.075), tint, bladeBias, isWoody, collarR));
            }
        }

        // step over a fine lattice near the camera: this layer only needs to exist within CullRadius, so the
        // scan cost stays bounded regardless of world size.
        int stride = Quality >= 2 ? 1 : 2;
        var rng = new CellRng();
        // scan only the cells inside the cull square around the camera, not the whole island
        int i0 = 0, i1 = g.Nx - 1, j0 = 0, j1 = g.Nz - 1;
        if (haveCam)
        {
            i0 = Mathf.Max(0, (int)((camPos.X - CullRadius - g.OriginX) / g.CellSize)); i1 = Mathf.Min(g.Nx - 1, (int)((camPos.X + CullRadius - g.OriginX) / g.CellSize));
            j0 = Mathf.Max(0, (int)((camPos.Z - CullRadius - g.OriginZ) / g.CellSize)); j1 = Mathf.Min(g.Nz - 1, (int)((camPos.Z + CullRadius - g.OriginZ) / g.CellSize));
        }
        for (int j = j0 - j0 % stride; j <= j1; j += stride)
        for (int i = i0 - i0 % stride; i <= i1; i += stride)
        {
            if (!g.InDomain(i, j)) continue;
            int idx = g.Index(i, j);
            if ((Substrate)f.BaseSubstrate[idx] != Substrate.Soil) continue;
            var p = g.CellCenter(idx);
            var wp = Bridge.V(p, _w.Terrain.Height(p));
            float distFade = 1f, d = 0f;
            if (haveCam)
            {
                float dx = wp.X - camPos.X, dz = wp.Z - camPos.Z;
                float d2 = dx * dx + dz * dz;
                if (d2 > r2) continue;
                d = Mathf.Sqrt(d2);
                distFade = d <= FadeStart ? 1f : 1f - (d - FadeStart) / (CullRadius - FadeStart);
                if (distFade <= 0.05f) continue;
            }
            double moisture = f.Moisture.Values[idx];
            rng.Seed = (ulong)(idx * 2654435761u + 17);

            // Deposition follows local history instead of making every cell an equal random bucket. Hollows and
            // the lee/contact zones around large props retain more litter; exposed slopes retain less.
            double e = g.CellSize;
            double hc = _w.Terrain.Height(p);
            double hx0 = _w.Terrain.Height(p + new Vivarium.Sim.Core.Vec2(-e, 0));
            double hx1 = _w.Terrain.Height(p + new Vivarium.Sim.Core.Vec2(e, 0));
            double hz0 = _w.Terrain.Height(p + new Vivarium.Sim.Core.Vec2(0, -e));
            double hz1 = _w.Terrain.Height(p + new Vivarium.Sim.Core.Vec2(0, e));
            double slope = Math.Sqrt(Math.Pow((hx1 - hx0) / (2 * e), 2) + Math.Pow((hz1 - hz0) / (2 * e), 2));
            double hollow = Math.Clamp(((hx0 + hx1 + hz0 + hz1) * 0.25 - hc) * 7.0, -0.25, 0.35);

            double rockDist = double.PositiveInfinity;
            for (int ri = 0; ri < nearRocks.Count; ri++)
                rockDist = Math.Min(rockDist, Math.Max(0, Vivarium.Sim.Core.Vec2.Distance(p, nearRocks[ri].Position) - nearRocks[ri].FootprintRadius));

            double logDist = double.PositiveInfinity;
            for (int li = 0; li < nearLogs.Count; li++)
                logDist = Math.Min(logDist, Math.Max(0, nearLogs[li].AxisDistance(p) - nearLogs[li].Radius));

            double propDist = Math.Min(logDist, rockDist);
            double shelter = double.IsInfinity(propDist) ? 0 : Math.Exp(-propDist / 0.45);
            double patch = 0.65 + 0.35 * rng.Randf();
            double deposition = Math.Clamp((0.62 - Math.Min(slope, 1.0) * 0.28 + hollow + shelter * 0.24) * patch, 0.12, 1.25);

            // Mineral dressing follows moisture and exposed soil (computed below after flora/cover checks).
            // Organic detail is proportional to stored litter; terrain
            // shelter only affects its arrangement, never creates leaves or twigs without deposited matter.
            // Normal quality visits every second cell. Include its whole block so a deposit in an
            // unvisited cell still produces visible pieces and the two tiers show the same history.
            double fineMass = 0, coarseMass = 0, fruitMass = 0;
            for (int dj = 0; dj < stride; dj++)
            for (int di = 0; di < stride; di++)
            {
                if (!g.InDomain(i + di, j + dj)) continue;
                int sample = g.Index(i + di, j + dj);
                fineMass += _w.Litter.FineMass[sample];
                coarseMass += _w.Litter.CoarseMass[sample];
                fruitMass += _w.Litter.FruitMass[sample];
            }
            double fineCover = 1.0 - Math.Exp(-fineMass * 55.0);
            double coarseCover = 1.0 - Math.Exp(-coarseMass * 65.0);
            int twigCount = Mathf.RoundToInt((1 + rng.RandiRange(0, 1)) * Quality * distFade * coarseCover * deposition);
            int flakeCount = Mathf.RoundToInt((1 + rng.RandiRange(0, 2)) * Quality * distFade * fineCover * deposition);
            double fruitCover = 1.0 - Math.Exp(-fruitMass * 45.0);
            int fruitCount = Mathf.Clamp(Mathf.RoundToInt((2 + rng.RandiRange(0, 3)) * Quality * distFade * fruitCover), 0, 12);
            float darken = (float)Mathf.Clamp(1.0 - moisture * 0.55, 0.45, 1.0);
            Color soilTint = new Color(0.30f, 0.22f, 0.16f) * darken;
            Color twigTint = new Color(0.33f, 0.26f, 0.19f) * darken;
            Color litterTint = new Color(0.34f, 0.26f, 0.15f) * darken;
            // Until source-colour composition is stored per litter cell, stable cell variation keeps fruit
            // readable as reproductive material without pretending every drop is the same generic red berry.
            Color fruitTint = (idx % 3) switch
            {
                0 => new Color(0.50f, 0.09f, 0.07f),
                1 => new Color(0.28f, 0.10f, 0.36f),
                _ => new Color(0.58f, 0.32f, 0.09f),
            };
            fruitTint *= (float)Mathf.Lerp(0.62, 1.0, Math.Min(1.0, fruitMass * 4.0));

            var norm = new Vector3((float)(hx0 - hx1), (float)(2 * e), (float)(hz0 - hz1)).Normalized();
            var cellTilt = SurfaceFrame.TiltTo(norm.Y < 0.5f ? new Vector3(norm.X, 0, norm.Z).Normalized() * 0.866f + Vector3.Up * 0.5f : norm);

            // Ambient vegetation is strongest on ordinary moist exposed soil, but remains intentionally visually
            // subordinate. It is geometry, not a green terrain wash: many short blades plus sparse seed stalks.
            // If the cell already carries ambient ground cover (AmbientGroundCoverRenderer), suppress filler blades
            // here to eliminate double-filling and avoid competing geometry batches.
            double ambientCover = _w.AmbientGroundCover.Cover[idx];
            double nearest = double.PositiveInfinity;
            Color neighbourTint = new Color(0.30f, 0.48f, 0.20f);
            bool neighbourBladeBias = false;
            bool excludedByNamedPlant = false;
            double rootCollarShelter = 0;
            for (int pi = 0; pi < nearFlora.Count; pi++)
            {
                double fd = Vivarium.Sim.Core.Vec2.Distance(p, nearFlora[pi].P);
                if (fd < nearFlora[pi].Exclusion) { excludedByNamedPlant = true; break; }
                if (nearFlora[pi].IsWoody)
                {
                    if (fd < nearFlora[pi].CollarRadius) { excludedByNamedPlant = true; break; }
                    double collarDist = Math.Max(0, fd - nearFlora[pi].CollarRadius);
                    if (collarDist < 0.6) rootCollarShelter = Math.Max(rootCollarShelter, Math.Exp(-collarDist / 0.32));
                }
                if (fd < nearest)
                {
                    nearest = fd;
                    neighbourTint = nearFlora[pi].Tint;
                    neighbourBladeBias = nearFlora[pi].BladeBias;
                }
            }

            // Tree root collar flutes shelter litter accumulation on the ground
            deposition = Math.Clamp(deposition + rootCollarShelter * 0.28, 0.12, 1.50);

            // Mineral crumbs and clods belong on exposed soil; heavy turf and thick litter mat hide crumb microrelief
            double exposedSoil = Math.Clamp(1.0 - ambientCover * 1.4 - fineCover * 0.6, 0.0, 1.0);
            int crumbCount = Mathf.RoundToInt((moisture > 0.55 ? 0 : rng.RandiRange(1, 5) * Quality * deposition)
                * Mathf.Clamp(1f - (d - CrumbRadius * 0.6f) / (CrumbRadius * 0.4f), 0f, 1f) * (float)exposedSoil);
            int clodCount = Mathf.RoundToInt((moisture > 0.3 ? rng.RandiRange(1, 3) : (rng.Randf() < 0.35 ? 1 : 0))
                * deposition * Mathf.Clamp(1f - (d - ClodRadius * 0.6f) / (ClodRadius * 0.4f), 0f, 1f) * (float)exposedSoil);

            // Ambient vegetation filler: only appears on exposed bare soil where ambientCover is absent (< 0.12).
            // Where AmbientGroundCoverRenderer is active, it handles grass/broadleaf instances completely.
            if (!excludedByNamedPlant && slope < 0.72 && moisture > 0.16 && moisture < 0.94 && ambientCover < 0.12)
            {
                double light = _w.FloraSystem.EffectiveLightCell(idx);
                double moistureFit = MathD.Clamp01(1.0 - Math.Abs(moisture - 0.55) / 0.48);
                double lightFit = 0.35 + 0.65 * MathD.Clamp01(light / 0.72);
                double litterPenalty = 1.0 - 0.45 * MathD.Clamp01(fineCover);
                double bareWeight = 1.0 - ambientCover / 0.12;
                double filler = moistureFit * lightFit * litterPenalty * distFade * bareWeight;
                int bladeCount = Mathf.Clamp(Mathf.RoundToInt((Quality >= 2 ? 5 : 8) * filler), 0, Quality >= 2 ? 7 : 10);
                int stalkCount = filler > 0.35 && rng.Randf() < (neighbourBladeBias ? 0.25f : 0.14f) ? 1 : 0;
                var baseGreen = new Color(
                    (float)Mathf.Lerp(0.24, 0.34, moisture),
                    (float)Mathf.Lerp(0.38, 0.58, moisture),
                    (float)Mathf.Lerp(0.16, 0.28, moisture));
                if (nearest < 1.1)
                    baseGreen = baseGreen.Lerp(neighbourTint, (float)(0.12 + 0.16 * (1.0 - nearest / 1.1)));
                for (int k = 0; k < bladeCount; k++)
                    Place(_bladeXf, _bladeCol, g, p, ref rng, baseGreen, 0.72f, 1.38f, DetailKind.Blade, cellTilt, nearRocks, nearLogs, nearGravel);
                for (int k = 0; k < stalkCount; k++)
                    Place(_stalkXf, _stalkCol, g, p, ref rng, baseGreen.Lerp(new Color(0.62f, 0.56f, 0.30f), 0.30f), 0.78f, 1.28f, DetailKind.Stalk, cellTilt, nearRocks, nearLogs, nearGravel);
            }

            for (int k = 0; k < crumbCount; k++) Place(_crumbXf, _crumbCol, g, p, ref rng, soilTint, 0.7f, 1.3f, DetailKind.Crumb, cellTilt, nearRocks, nearLogs, nearGravel);
            for (int k = 0; k < clodCount; k++) Place(_clodXf, _clodCol, g, p, ref rng, soilTint * 1.05f, 0.7f, 1.4f, DetailKind.Clod, cellTilt, nearRocks, nearLogs, nearGravel);
            for (int k = 0; k < twigCount; k++) Place(_twigXf, _twigCol, g, p, ref rng, twigTint, 0.6f, 1.3f, DetailKind.Twig, cellTilt, nearRocks, nearLogs, nearGravel);
            for (int k = 0; k < flakeCount; k++) Place(_flakeXf, _flakeCol, g, p, ref rng, litterTint, 0.7f, 1.3f, DetailKind.Flake, cellTilt, nearRocks, nearLogs, nearGravel);
            for (int k = 0; k < fruitCount; k++) Place(_fruitXf, _fruitCol, g, p, ref rng, fruitTint, 0.75f, 1.45f, DetailKind.Fruit, cellTilt, nearRocks, nearLogs, nearGravel);
        }

        SetInstances(_crumbs, _crumbXf, _crumbCol);
        SetInstances(_clods, _clodXf, _clodCol);
        SetInstances(_twigs, _twigXf, _twigCol);
        SetInstances(_flakes, _flakeXf, _flakeCol);
        SetInstances(_fruit, _fruitXf, _fruitCol);
        SetInstances(_blades, _bladeXf, _bladeCol);
        SetInstances(_stalks, _stalkXf, _stalkCol);
    }

    private enum DetailKind { Crumb, Clod, Twig, Flake, Fruit, Blade, Stalk }

    private void Place(List<Transform3D> xf, List<Color> col, GridSpec g, Vivarium.Sim.Core.Vec2 center, ref CellRng rng, Color tint, float minScale, float maxScale, DetailKind kind, Basis cellTilt, List<Vivarium.Sim.World.Rock> nearRocks, List<Vivarium.Sim.World.LogProp> nearLogs, List<Vivarium.Sim.World.GravelPatch> nearGravel)
    {
        // Triangular jitter favours the middle of each source cell but has no visible axis-aligned edge.
        float jx = (rng.Randf() + rng.Randf() - 1f) * (float)(g.CellSize * (Quality >= 2 ? 0.52 : 1.04));
        float jz = (rng.Randf() + rng.Randf() - 1f) * (float)(g.CellSize * (Quality >= 2 ? 0.52 : 1.04));
        var wp = center + new Vivarium.Sim.Core.Vec2(jx, jz);
        if (!_w.Domain.Contains(wp)) return;
        if (_w.Water.IsWet(wp)) return;
        for (int ri = 0; ri < nearRocks.Count; ri++) if (nearRocks[ri].Covers(wp)) return;
        for (int li = 0; li < nearLogs.Count; li++) if (nearLogs[li].Covers(wp)) return;
        for (int gi = 0; gi < nearGravel.Count; gi++) if (nearGravel[gi].Covers(wp)) return;
        float y = (float)_w.Terrain.Height(wp);
        float scale = rng.RandfRange(minScale, maxScale);
        var basis = new Basis(Vector3.Up, rng.RandfRange(0, Mathf.Tau));

        switch (kind)
        {
            case DetailKind.Crumb:
            case DetailKind.Clod:
                basis = basis * new Basis(Vector3.Right, rng.RandfRange(-0.5f, 0.5f)) * new Basis(Vector3.Back, rng.RandfRange(-0.5f, 0.5f));
                basis = basis.Scaled(new Vector3(scale * rng.RandfRange(0.72f, 1.3f), scale * rng.RandfRange(0.65f, 1.08f), scale * rng.RandfRange(0.72f, 1.3f)));
                break;
            case DetailKind.Fruit:
                basis = basis * new Basis(Vector3.Right, rng.RandfRange(-0.25f, 0.25f));
                basis = basis.Scaled(new Vector3(scale * rng.RandfRange(0.86f, 1.16f), scale * rng.RandfRange(0.78f, 1.20f), scale * rng.RandfRange(0.86f, 1.16f)));
                break;
            case DetailKind.Twig:
                basis = basis * new Basis(Vector3.Right, rng.RandfRange(-0.16f, 0.16f));
                basis = basis.Scaled(new Vector3(scale * rng.RandfRange(0.7f, 1.2f), scale, scale * rng.RandfRange(0.72f, 1.45f)));
                break;
            case DetailKind.Blade:
                basis = basis * new Basis(Vector3.Right, rng.RandfRange(-0.22f, 0.22f)) * new Basis(Vector3.Back, rng.RandfRange(-0.12f, 0.12f));
                basis = basis.Scaled(new Vector3(scale * rng.RandfRange(0.70f, 1.12f), scale * rng.RandfRange(0.75f, 1.35f), scale));
                break;
            case DetailKind.Stalk:
                basis = basis * new Basis(Vector3.Right, rng.RandfRange(-0.12f, 0.12f));
                basis = basis.Scaled(new Vector3(scale, scale * rng.RandfRange(0.86f, 1.28f), scale));
                break;
            default:
                basis = basis * new Basis(Vector3.Right, rng.RandfRange(-0.35f, 0.35f)) * new Basis(Vector3.Back, rng.RandfRange(-0.25f, 0.25f));
                basis = basis.Scaled(new Vector3(scale * rng.RandfRange(0.72f, 1.35f), scale, scale * rng.RandfRange(0.75f, 1.25f)));
                break;
        }

        // Litter follows the substrate slope rather than hovering across it.
        basis = cellTilt * basis;
        xf.Add(new Transform3D(basis, new Vector3((float)wp.X, y + 0.0015f * scale, (float)wp.Z)));
        float v = rng.RandfRange(0.82f, 1.16f);
        col.Add(new Color(tint.R * v, tint.G * v, tint.B * v));
    }

    /// <summary>Managed per-cell RNG. Godot's RandomNumberGenerator is an engine object, so each call crosses into
    /// native code; thousands per rebuild made every refresh a 100 ms hitch.</summary>
    private struct CellRng
    {
        private ulong _s;
        public ulong Seed { set => _s = value * 0x9E3779B97F4A7C15UL + 0x632BE59BD9B4E019UL; }
        private uint Next() { _s ^= _s << 13; _s ^= _s >> 7; _s ^= _s << 17; return (uint)(_s >> 32); }
        public float Randf() => Next() * (1f / 4294967296f);
        public float RandfRange(float a, float b) => a + (b - a) * Randf();
        public int RandiRange(int a, int b) => a + (int)(Next() % (uint)(b - a + 1));
    }

    private readonly Dictionary<MultiMesh, float[]> _buffers = new();

    /// <summary>One packed upload (12 transform + 4 colour floats per instance) instead of two engine calls each.</summary>
    private void SetInstances(MultiMesh mm, List<Transform3D> xf, List<Color> col)
    {
        int n = xf.Count;
        if (mm.InstanceCount != n) mm.InstanceCount = n;
        mm.VisibleInstanceCount = n;
        if (n == 0) return;
        bool resized = !_buffers.TryGetValue(mm, out var buf) || buf.Length != n * 16;
        if (resized) _buffers[mm] = buf = new float[n * 16];
        bool changed = resized;
        for (int i = 0; i < n; i++)
        {
            var t = xf[i]; var c = col[i]; int o = i * 16;
            if (buf![o + 0] != t.Basis.X.X || buf[o + 1] != t.Basis.Y.X || buf[o + 2] != t.Basis.Z.X || buf[o + 3] != t.Origin.X ||
                buf[o + 4] != t.Basis.X.Y || buf[o + 5] != t.Basis.Y.Y || buf[o + 6] != t.Basis.Z.Y || buf[o + 7] != t.Origin.Y ||
                buf[o + 8] != t.Basis.X.Z || buf[o + 9] != t.Basis.Y.Z || buf[o + 10] != t.Basis.Z.Z || buf[o + 11] != t.Origin.Z ||
                buf[o + 12] != c.R || buf[o + 13] != c.G || buf[o + 14] != c.B)
            {
                changed = true;
                buf[o + 0] = t.Basis.X.X; buf[o + 1] = t.Basis.Y.X; buf[o + 2] = t.Basis.Z.X; buf[o + 3] = t.Origin.X;
                buf[o + 4] = t.Basis.X.Y; buf[o + 5] = t.Basis.Y.Y; buf[o + 6] = t.Basis.Z.Y; buf[o + 7] = t.Origin.Y;
                buf[o + 8] = t.Basis.X.Z; buf[o + 9] = t.Basis.Y.Z; buf[o + 10] = t.Basis.Z.Z; buf[o + 11] = t.Origin.Z;
                buf[o + 12] = c.R; buf[o + 13] = c.G; buf[o + 14] = c.B; buf[o + 15] = 1f;
            }
        }
        if (changed || mm.Buffer == null || mm.Buffer.Length != n * 16)
        {
            mm.Buffer = buf;
        }
    }

    /// <summary>
    /// Small rounded, irregular lump for crumbs and clods: an icosphere-ish hull with per-vertex radius jitter
    /// and smooth (position-normalized) normals, so it reads as a soft clump of dirt rather than a faceted
    /// pyramid. <paramref name="radius"/> is the nominal size in world metres; <paramref name="seedJitter"/>
    /// perturbs the hull so every generated mesh looks slightly different without needing per-instance meshes.
    /// </summary>
    private static ArrayMesh BuildClod(float radius, int seedJitter)
    {
        var rng = new RandomNumberGenerator { Seed = (ulong)(seedJitter * 104729 + 7) };

        // Base icosahedron (12 verts, 20 tris) — a much rounder starting hull than a bipyramid/octahedron.
        float t = (1f + Mathf.Sqrt(5f)) / 2f;
        var baseVerts = new[]
        {
            new Vector3(-1,  t,  0), new Vector3( 1,  t,  0), new Vector3(-1, -t,  0), new Vector3( 1, -t,  0),
            new Vector3( 0, -1,  t), new Vector3( 0,  1,  t), new Vector3( 0, -1, -t), new Vector3( 0,  1, -t),
            new Vector3( t,  0, -1), new Vector3( t,  0,  1), new Vector3(-t,  0, -1), new Vector3(-t,  0,  1),
        };
        int[] tris =
        {
            0,11,5, 0,5,1, 0,1,7, 0,7,10, 0,10,11,
            1,5,9, 5,11,4, 11,10,2, 10,7,6, 7,1,8,
            3,9,4, 3,4,2, 3,2,6, 3,6,8, 3,8,9,
            4,9,5, 2,4,11, 6,2,10, 8,6,7, 9,8,1,
        };

        // Per-vertex jitter so the hull reads as an irregular clump, not a perfect sphere; flattened slightly
        // on the vertical axis so it sits like a squashed crumb rather than a marble.
        var verts = new Vector3[baseVerts.Length];
        for (int i = 0; i < baseVerts.Length; i++)
        {
            var n = baseVerts[i].Normalized();
            float jitter = rng.RandfRange(0.75f, 1.15f);
            var p = n * radius * jitter;
            p.Y *= 0.75f;
            verts[i] = p;
        }

        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        st.SetColor(Colors.White);
        for (int i = 0; i < tris.Length; i += 3)
        {
            var a = verts[tris[i]]; var b = verts[tris[i + 1]]; var c = verts[tris[i + 2]];
            var flatN = (b - a).Cross(c - a).Normalized();
            // Blend flat and smooth (position-normalized) normals so facets are visible but softened —
            // reads as a rounded crumb rather than a faceted gem.
            void Vert(Vector3 p)
            {
                var smoothN = p.Normalized();
                var n = (flatN * 0.4f + smoothN * 0.6f).Normalized();
                st.SetNormal(n);
                st.SetColor(Colors.White);
                st.AddVertex(p);
            }
            Vert(a); Vert(b); Vert(c);
        }
        return st.Commit();
    }

    /// <summary>Small bent six-sided twig/root fragment with a real silhouette.</summary>
    private static ArrayMesh BuildTwig(Material mat)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const int sides = 6, segs = 4;
        float radius = 0.0024f, halfLen = 0.03f;
        var rings = new Vector3[segs, sides];
        for (int j = 0; j < segs; j++)
        {
            float t = j / (float)(segs - 1), z = Mathf.Lerp(-halfLen, halfLen, t);
            var centre = new Vector3(Mathf.Sin(t * 3.2f) * 0.0025f, Mathf.Sin(t * Mathf.Pi) * 0.0018f, z);
            for (int k = 0; k < sides; k++)
            {
                float a = Mathf.Tau * k / sides;
                float rr = radius * (1f + 0.12f * Mathf.Sin(a * 2f + t * 2.1f));
                rings[j, k] = centre + new Vector3(Mathf.Cos(a) * rr, Mathf.Sin(a) * rr, 0);
            }
        }
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            var n = (b - a).Cross(c - a).Normalized();
            st.SetNormal(n); st.SetColor(Colors.White); st.AddVertex(a);
            st.SetNormal(n); st.SetColor(Colors.White); st.AddVertex(b);
            st.SetNormal(n); st.SetColor(Colors.White); st.AddVertex(c);
        }
        for (int j = 0; j < segs - 1; j++)
            for (int k = 0; k < sides; k++)
            {
                int n = (k + 1) % sides;
                Tri(rings[j, k], rings[j + 1, k], rings[j + 1, n]);
                Tri(rings[j, k], rings[j + 1, n], rings[j, n]);
            }
        st.SetMaterial(mat);
        return st.Commit();
    }


    /// <summary>A small curved crossed blade cluster used only for anonymous bare-ground visual filler.</summary>
    private static ArrayMesh BuildFillerBlade(Material mat)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            var n = (b - a).Cross(c - a).Normalized();
            st.SetNormal(n); st.SetColor(Colors.White); st.AddVertex(a);
            st.SetNormal(n); st.SetColor(Colors.White); st.AddVertex(b);
            st.SetNormal(n); st.SetColor(Colors.White); st.AddVertex(c);
            st.SetNormal(-n); st.SetColor(Colors.White); st.AddVertex(a);
            st.SetNormal(-n); st.SetColor(Colors.White); st.AddVertex(c);
            st.SetNormal(-n); st.SetColor(Colors.White); st.AddVertex(b);
        }
        void Blade(float yaw, float height, float width, float bend)
        {
            var dir = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw));
            var side = new Vector3(-dir.Z, 0, dir.X);
            var root = Vector3.Zero;
            var mid = dir * bend * 0.35f + Vector3.Up * height * 0.58f;
            var tip = dir * bend + Vector3.Up * height;
            Tri(root - side * width * 0.36f, root + side * width * 0.36f, mid + side * width);
            Tri(root - side * width * 0.36f, mid + side * width, mid - side * width);
            Tri(mid - side * width, mid + side * width, tip);
        }
        Blade(0.1f, 0.085f, 0.0065f, 0.020f);
        Blade(2.05f, 0.066f, 0.0055f, 0.025f);
        Blade(4.15f, 0.105f, 0.0050f, 0.018f);
        st.SetMaterial(mat);
        return st.Commit();
    }

    /// <summary>Occasional wiry reproductive stem that rises above the anonymous blade matrix.</summary>
    private static ArrayMesh BuildFillerStalk(Material mat)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            var n = (b - a).Cross(c - a).Normalized();
            st.SetNormal(n); st.SetColor(Colors.White); st.AddVertex(a);
            st.SetNormal(n); st.SetColor(Colors.White); st.AddVertex(b);
            st.SetNormal(n); st.SetColor(Colors.White); st.AddVertex(c);
            st.SetNormal(-n); st.SetColor(Colors.White); st.AddVertex(a);
            st.SetNormal(-n); st.SetColor(Colors.White); st.AddVertex(c);
            st.SetNormal(-n); st.SetColor(Colors.White); st.AddVertex(b);
        }
        // crossed tapered stem cards
        for (int q = 0; q < 2; q++)
        {
            float yaw = q * Mathf.Pi * 0.5f;
            var side = new Vector3(Mathf.Cos(yaw), 0, Mathf.Sin(yaw)) * 0.0018f;
            var root = Vector3.Zero;
            var mid = new Vector3(0.006f, 0.085f, -0.004f);
            var top = new Vector3(0.012f, 0.17f, -0.007f);
            Tri(root - side, root + side, mid + side * 0.65f);
            Tri(root - side, mid + side * 0.65f, mid - side * 0.65f);
            Tri(mid - side * 0.65f, mid + side * 0.65f, top);
        }
        // loose narrow seed head rather than a cattail blob
        var headBase = new Vector3(0.012f, 0.13f, -0.007f);
        for (int i = 0; i < 5; i++)
        {
            float y = headBase.Y + i * 0.010f;
            float side = (i % 2 == 0 ? 1f : -1f) * (0.010f + i * 0.0015f);
            var p = new Vector3(headBase.X, y, headBase.Z);
            var tip = p + new Vector3(side, 0.014f, side * 0.28f);
            Tri(p + new Vector3(-0.0012f,0,0), p + new Vector3(0.0012f,0,0), tip);
        }
        st.SetMaterial(mat);
        return st.Commit();
    }

    /// <summary>Asymmetric curled leaf fragment with a raised midrib rather than a perfect quad.</summary>
    private static ArrayMesh BuildFlake(Material mat)
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        var left = new[]
        {
            new Vector3(-0.001f, 0.000f, -0.014f),
            new Vector3(-0.008f, 0.001f, -0.005f),
            new Vector3(-0.007f, 0.003f,  0.006f),
            new Vector3(-0.001f, 0.004f,  0.014f),
        };
        var right = new[]
        {
            new Vector3(0.001f, 0.000f, -0.014f),
            new Vector3(0.009f, 0.003f, -0.005f),
            new Vector3(0.006f, 0.001f,  0.006f),
            new Vector3(0.001f, 0.004f,  0.014f),
        };
        var mid = new[]
        {
            new Vector3(0, 0.0015f, -0.014f),
            new Vector3(0, 0.0035f, -0.005f),
            new Vector3(0, 0.0045f,  0.006f),
            new Vector3(0, 0.0055f,  0.014f),
        };
        void Tri(Vector3 a, Vector3 b, Vector3 c)
        {
            var n = (b - a).Cross(c - a).Normalized();
            st.SetNormal(n); st.SetColor(Colors.White); st.AddVertex(a);
            st.SetNormal(n); st.SetColor(Colors.White); st.AddVertex(b);
            st.SetNormal(n); st.SetColor(Colors.White); st.AddVertex(c);
            st.SetNormal(-n); st.AddVertex(a); st.SetNormal(-n); st.AddVertex(c); st.SetNormal(-n); st.AddVertex(b);
        }
        for (int i = 0; i < 3; i++)
        {
            Tri(left[i], mid[i], mid[i + 1]); Tri(left[i], mid[i + 1], left[i + 1]);
            Tri(mid[i], right[i], right[i + 1]); Tri(mid[i], right[i + 1], mid[i + 1]);
        }
        st.SetMaterial(mat);
        return st.Commit();
    }

}
