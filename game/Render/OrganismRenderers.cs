using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>
/// Flora drawn as one MultiMesh per species and detail level, rebuilt from authoritative state on a short
/// cadence. The render instances can be discarded and rebuilt at any time without touching the simulation.
/// </summary>
public partial class FloraRenderer : Node3D
{
    private VivariumWorld _w = null!;
    private const int DefaultMorphVariants = 5;
    private const int MigratedMorphVariants = 12;
    private static int MorphVariantsFor(string shape) => shape is "roundleaf" or "pairedleaf" or "herb" or "trifoliate"
        ? MigratedMorphVariants : DefaultMorphVariants;
    private sealed class VariantLayer { public MultiMeshInstance3D Full = null!; public MultiMeshInstance3D? Fruit; public int FullTris, FruitTris; }
    private sealed class Layer { public VariantLayer[] Variants; public MultiMeshInstance3D? Veins; public int VeinTris; public Layer(int count) => Variants = new VariantLayer[count]; }
    private static string MorphKey(string species, int variant) => species + "\u001f" + variant;
    private readonly Dictionary<string, Layer> _layers = new(StringComparer.Ordinal);
    private readonly Dictionary<EntityId, double> _wobbleStart = new();
    private double _accum = 999;
    private double _clock;
    public Camera3D? Camera { get; set; }
    public int Quality { get; set; } = 1;
    public int Visible_ { get; private set; }
    public long TrianglesDrawn { get; private set; }

    public void Build(VivariumWorld w)
    {
        _w = w;
        foreach (var c in GetChildren()) c.QueueFree();
        _layers.Clear();
        foreach (var sp in w.Content.Flora)
        {
            var mat = Bridge.Shader("res://Shaders/flora.gdshader");
            mat.SetShaderParameter("stiffness", sp.Shape is "reed" or "herb" ? 1.0f : 3.0f);
            mat.SetShaderParameter("surface_mode", sp.Archetype switch { "moss" => 0, "lichen" => 1, "fungus" => 3, "slime_mold" => 4, _ => 2 });
            mat.SetShaderParameter("deform_leaf_tips", MorphVariantsFor(sp.Shape) == MigratedMorphVariants);
            if (sp.Archetype is "fungus" or "slime_mold") mat.SetShaderParameter("sway", 0.0f);
            Bridge.BindSurface(mat, "moss", Bridge.Surfaces.Moss);
            var layer = new Layer(MorphVariantsFor(sp.Shape));
            ulong speciesSeed = Hash.Fnv1a64("flora.visual." + sp.Id);
            for (int v = 0; v < layer.Variants.Length; v++)
            {
                ulong seed = Rng.Mix(speciesSeed, (ulong)(v + 1) * 0x9E3779B97F4A7C15UL);
                var full = OrganismMeshes.Flora(sp, seed);
                bool castShadow = Quality >= 1 && sp.Colony == null && sp.Archetype is "plant" or "fungus" && sp.Height >= 0.055;
                var vl = new VariantLayer { Full = MakeMmi($"Flora_{sp.Id}_{v}", Bridge.ToArrayMesh(full, mat), castShadow), FullTris = full.TriangleCount };
                AddChild(vl.Full);
                if (OrganismMeshes.FloraFruiting(sp, seed) is { } fruit)
                {
                    var fruitMat = (ShaderMaterial)mat.Duplicate();
                    fruitMat.SetShaderParameter("surface_mode", 3);
                    vl.Fruit = MakeMmi($"Flora_{sp.Id}_{v}_fruit", Bridge.ToArrayMesh(fruit, fruitMat), castShadow);
                    vl.FruitTris = fruit.TriangleCount;
                    AddChild(vl.Fruit);
                }
                layer.Variants[v] = vl;
            }
            if (sp.CreepSpeed > 0)
            {
                // veins joining each patch of the network to the patch it grew from (unit tube along +X)
                var vein = new MeshData();
                var col = Primitives.Mix(sp.Color, sp.Color2, 0.3);
                // a slightly arched, pinched tube: thinner at mid-length like a real plasmodial vein
                var vpath = new List<Vec3>(); var vrad = new List<double>();
                for (int i = 0; i <= 8; i++)
                {
                    double t = i / 8.0;
                    vpath.Add(new Vec3(-0.05 + 1.1 * t, 0.1 * Math.Sin(Math.PI * t), 0));
                    vrad.Add(0.75 + 0.25 * Math.Cos(t * 2 * Math.PI));
                }
                Primitives.Tube(vein, vpath, vrad, 6, (i, v) => (col, 1, i, v, 0, 0));
                layer.Veins = MakeMmi($"Flora_{sp.Id}_veins", Bridge.ToArrayMesh(vein, mat));
                layer.VeinTris = vein.TriangleCount;
                AddChild(layer.Veins);
            }
            _layers[sp.Id] = layer;
        }
        _accum = 999;
    }


    private static MultiMeshInstance3D MakeMmi(string name, ArrayMesh mesh, bool castShadow = false) => new()
    {
        Name = name,
        CastShadow = castShadow ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off,
        // UseColor carries per-instance tint (colonial moss/lichen; white = no change), multiplied into the
        // mesh's baked vertex colour by the multimesh pipeline before the shader sees COLOR.
        Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, UseCustomData = true, Mesh = mesh, InstanceCount = 0 },
    };

    public void Wobble(IEnumerable<EntityId> ids) { foreach (var id in ids) _wobbleStart[id] = _clock; }

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("Flora");
        _clock += delta;
        if (_w == null) return;
        _accum += delta;
        bool wobbling = _wobbleStart.Count > 0;
        if (_accum < (wobbling ? 0.05 : 0.4)) return;
        _accum = 0;
        Rebuild();
    }

    private readonly Dictionary<string, (List<Transform3D> T, List<Color> Tint, List<Color> C)> _full = new(), _fruit = new(), _veins = new();

    /// <summary>
    /// Where a climber or bracket attaches: the nearest log or rock (sim angle toward it from p, its top height,
    /// and for logs the point on the trunk surface closest to p). Null when nothing is within reach.
    /// </summary>
    private (double Angle, double Top, double Dist, Vector3 Surface, double Outward)? Anchor(Vector2 p, double reach)
    {
        (double, double, double, Vector3, double)? best = null;
        double bestD = reach;
        var sp = new Vivarium.Sim.Core.Vec2(p.X, p.Y);
        foreach (var l in _w.Props.Logs)
        {
            var axis = Vivarium.Sim.Core.Vec2.FromAngle(l.RotationY);
            var rel = sp - l.Position;
            double along = Math.Clamp(rel.Dot(axis), -l.Length / 2, l.Length / 2);
            var onAxis = l.Position + axis * along;
            var outV = sp - onAxis;
            double d = Math.Max(0, outV.Length - l.Radius);
            if (d > bestD) continue;
            bestD = d;
            double outward = outV.LengthSq > 1e-10 ? outV.Angle : l.RotationY + Math.PI / 2;
            var surf = onAxis + Vivarium.Sim.Core.Vec2.FromAngle(outward) * (l.Radius * 0.92);
            best = ((onAxis - sp).LengthSq > 1e-10 ? (onAxis - sp).Angle : outward + Math.PI, l.Y + l.Radius, d,
                    new Vector3((float)surf.X, (float)l.Y, (float)surf.Z), outward);
        }
        foreach (var r in _w.Props.Rocks)
        {
            double d = Math.Max(0, Vivarium.Sim.Core.Vec2.Distance(sp, r.Position) - r.FootprintRadius * 0.8);
            if (d > bestD) continue;
            bestD = d;
            var to = r.Position - sp;
            double ang = to.LengthSq > 1e-10 ? to.Angle : 0;
            best = (ang, r.Y + r.SizeY, d, new Vector3((float)r.X, (float)(r.Y + r.SizeY * 0.5), (float)r.Z), ang + Math.PI);
        }
        return best;
    }

    public void Rebuild()
    {
        foreach (var k in _layers.Keys)
            for (int v = 0; v < _layers[k].Variants.Length; v++)
            {
                var mk = MorphKey(k, v);
                var fl = Get(_full, mk); fl.T.Clear(); fl.Tint.Clear(); fl.C.Clear();
                var fr = Get(_fruit, mk); fr.T.Clear(); fr.Tint.Clear(); fr.C.Clear();
            }
        var camPos = Camera?.GlobalPosition ?? Vector3.Zero;
        Visible_ = 0; TrianglesDrawn = 0;
        var done = new List<EntityId>();
        foreach (var f in _w.Flora.Items)
        {
            var sp = _w.Content.FloraOrThrow(f.SpeciesId);
            double r = f.Radius(sp);
            double h = sp.Colony != null
                ? sp.Colony.MaxHeight * (0.15 + 0.85 * f.HeightFactor)
                : sp.Height * (0.45 + 0.55 * Math.Sqrt(f.BiomassFraction(sp)));
            var pos = new Vector3((float)f.X, (float)_w.GroundHeight(f.Position), (float)f.Z);
            if (Camera is { } camera && !FloraVisible(camera, pos, (float)r, (float)h)) continue;
            ulong hash = Rng.Mix(f.Id.Value, 0xF10);
            float yaw = (hash % 6283) / 1000f;
            // Mats conform to their substrate. Upright vascular plants respond to the actual local sky-openness
            // field: they lean slightly toward the more open side, while dry/unhealthy specimens lose some turgor
            // in a stable individual direction. This makes variation read as growth history rather than seed noise.
            var yawBasis = new Basis(Vector3.Up, yaw);
            if (sp.Colony != null || sp.Archetype is "moss" or "lichen" or "slime_mold")
            {
                yawBasis = SurfaceFrame.TiltTo(SurfaceFrame.SurfaceNormal(_w, pos.X, pos.Z, Math.Max(r, 0.03))) * yawBasis;
            }
            else if (sp.Archetype == "plant" && sp.Shape != "vine")
            {
                var fp = f.Position;
                double e = Math.Max(_w.Grid.CellSize * 0.55, Math.Min(0.35, Math.Max(r, 0.05)));
                double gx = _w.Fields.Light.Sample(fp + new Vivarium.Sim.Core.Vec2(e, 0))
                          - _w.Fields.Light.Sample(fp - new Vivarium.Sim.Core.Vec2(e, 0));
                double gz = _w.Fields.Light.Sample(fp + new Vivarium.Sim.Core.Vec2(0, e))
                          - _w.Fields.Light.Sample(fp - new Vivarium.Sim.Core.Vec2(0, e));
                double moisture = _w.Fields.Moisture.Sample(fp);
                double stress = MathD.Clamp01((1.0 - f.Health) * 0.7 + Math.Max(0, 0.32 - moisture) * 0.55);
                double stressAngle = ((hash >> 24) % 6283) / 1000.0;
                var growUp = new Vector3(
                    (float)(gx * 0.62 + Math.Cos(stressAngle) * stress * 0.15),
                    1f,
                    (float)(gz * 0.62 + Math.Sin(stressAngle) * stress * 0.15)).Normalized();
                yawBasis = SurfaceFrame.TiltTo(growUp) * yawBasis;
                h *= 0.93 + 0.07 * MathD.Clamp01(0.55 * f.Health + 0.45 * Math.Min(1, moisture / 0.45));
                r *= 1.0 + stress * 0.035;
            }
            var t = new Transform3D(yawBasis.Scaled(new Vector3((float)r, (float)h, (float)r)), pos);
            if (sp.Shape == "vine" && Anchor(new Vector2(pos.X, pos.Z), 0.6) is { } va)
            {
                // climb: turn the stems toward the log/rock and stretch them up and over its top
                float up = Mathf.Clamp((float)(va.Top - pos.Y) + 0.04f, 0.06f, 1.2f) * (float)(0.55 + 0.45 * Math.Sqrt(f.BiomassFraction(sp)));
                float across = Mathf.Max((float)r, (float)(va.Dist + 0.12));
                t = new Transform3D(Bridge.Yaw(va.Angle).Scaled(new Vector3(across, up, (float)r)), pos);
            }
            else if (sp.Shape == "bracket" && Anchor(new Vector2(pos.X, pos.Z), 0.35) is { } ba)
            {
                // shelves grow out of the trunk's side
                var at = ba.Surface + new Vector3(0, (float)(((hash >> 20) % 100) / 100.0 - 0.5) * 0.08f, 0);
                t = new Transform3D(Bridge.Yaw(ba.Outward).Scaled(new Vector3((float)r, (float)(h * 2.5), (float)r)), at - new Vector3(0, (float)h, 0));
            }
            float wobble = 0;
            if (_wobbleStart.TryGetValue(f.Id, out var ws)) { wobble = (float)Math.Max(0, 1 - (_clock - ws) / 1.2); if (wobble <= 0) done.Add(f.Id); }
            var custom = new Color((hash % 1000) / 1000f, (float)f.Health, wobble, ((hash >> 12) % 1000) / 1000f);
            var tint = new Color((float)f.Tint[0], (float)f.Tint[1], (float)f.Tint[2], 1f);
            int variant = (int)((hash >> 8) % (ulong)_layers[sp.Id].Variants.Length);
            var vl = _layers[sp.Id].Variants[variant];
            var bucket = f.Fruiting && vl.Fruit != null ? _fruit : _full;
            var bd = Get(bucket, MorphKey(sp.Id, variant)); bd.T.Add(t); bd.Tint.Add(tint); bd.C.Add(custom);
            Visible_++;
        }
        foreach (var id in done) _wobbleStart.Remove(id);
        // slime-mold veins: thickness follows the biomass flowing through each link
        foreach (var (id, layer) in _layers)
        {
            if (layer.Veins == null) continue;
            var list = Get(_veins, id); list.T.Clear(); list.Tint.Clear(); list.C.Clear();
            foreach (var f in _w.Flora.Items)
            {
                if (f.SpeciesId != id || f.ParentId.IsNone || _w.Flora.Get(f.ParentId) is not { } parent) continue;
                var vsp = _w.Content.FloraOrThrow(id);
                var a = new Vector3((float)parent.X, (float)_w.GroundHeight(parent.Position) + 0.004f, (float)parent.Z);
                var b = new Vector3((float)f.X, (float)_w.GroundHeight(f.Position) + 0.004f, (float)f.Z);
                var d = b - a;
                if (d.Length() < 1e-4f || d.Length() > 0.6f) continue;
                float thick = 0.004f + 0.009f * (float)Math.Sqrt(Math.Min(f.BiomassFraction(vsp), parent.BiomassFraction(vsp)));
                var xAxis = d; var zAxis = xAxis.Cross(Vector3.Up).Normalized() * thick; var yAxis = zAxis.Cross(xAxis).Normalized() * thick * 0.45f;
                list.T.Add(new Transform3D(new Basis(xAxis, yAxis, zAxis), a));
                list.Tint.Add(new Color((float)f.Tint[0], (float)f.Tint[1], (float)f.Tint[2], 1f));
                ulong hash = Rng.Mix(f.Id.Value, 0xF10);
                list.C.Add(new Color((hash % 1000) / 1000f, (float)Math.Min(f.Health, parent.Health), 0, ((hash >> 12) % 1000) / 1000f));
            }
            Fill(layer.Veins.Multimesh, list);
            TrianglesDrawn += (long)list.T.Count * layer.VeinTris;
        }
        foreach (var (id, layer) in _layers)
            for (int v = 0; v < layer.Variants.Length; v++)
            {
                var vl = layer.Variants[v];
                string mk = MorphKey(id, v);
                Fill(vl.Full.Multimesh, Get(_full, mk));
                if (vl.Fruit != null) Fill(vl.Fruit.Multimesh, Get(_fruit, mk));
                TrianglesDrawn += (long)vl.Full.Multimesh.InstanceCount * vl.FullTris
                    + (vl.Fruit != null ? (long)vl.Fruit.Multimesh.InstanceCount * vl.FruitTris : 0);
            }
    }

    private bool FloraVisible(Camera3D camera, Vector3 basePos, float radius, float height)
    {
        // Six conservative probes keep edge-of-frame leaves from popping while still rejecting whole plants that
        // are genuinely outside the frustum. Distance itself never reduces the mesh.
        float mid = Math.Max(height * 0.5f, 0.01f);
        var probes = new[]
        {
            basePos,
            basePos + Vector3.Up * Math.Max(height, 0.02f),
            basePos + new Vector3(radius, mid, 0),
            basePos + new Vector3(-radius, mid, 0),
            basePos + new Vector3(0, mid, radius),
            basePos + new Vector3(0, mid, -radius),
        };
        bool inFrustum = false;
        foreach (var p in probes) if (camera.IsPositionInFrustum(p)) { inFrustum = true; break; }
        if (!inFrustum) return false;

        // Cull only when a substantial opaque surface is clearly in front of the whole plant. The generous
        // silhouette allowance deliberately biases toward drawing when there is any doubt.
        var target = basePos + Vector3.Up * mid;
        var delta = target - camera.GlobalPosition;
        float distance = delta.Length();
        if (distance < 0.25f) return true;
        double hit = Vivarium.Sim.Tools.Selection.RayOpaque(_w, Bridge.S(camera.GlobalPosition), Bridge.S(delta / distance), distance);
        double allowance = Math.Max(0.05, Math.Max(radius, height) * 0.75);
        return double.IsInfinity(hit) || hit >= distance - allowance;
    }

    private static (List<Transform3D> T, List<Color> Tint, List<Color> C) Get(Dictionary<string, (List<Transform3D>, List<Color>, List<Color>)> d, string k)
    {
        if (!d.TryGetValue(k, out var v)) d[k] = v = (new List<Transform3D>(), new List<Color>(), new List<Color>());
        return v;
    }

    /// <summary>Uploads all instances in one packed buffer (12 transform + 4 instance-colour tint + 4 custom floats
    /// each) instead of two engine calls per instance, which caused frame hitches once the island filled with plants.</summary>
    private static readonly Dictionary<MultiMesh, float[]> _buffers = new();

    private static void Fill(MultiMesh mm, (List<Transform3D> T, List<Color> Tint, List<Color> C) data)
    {
        int n = data.T.Count;
        if (mm.InstanceCount != n) mm.InstanceCount = n;
        if (n == 0) return;
        // reuse the buffer while the count is unchanged (the usual case): large per-rebuild arrays triggered full GCs
        if (!_buffers.TryGetValue(mm, out var buf) || buf.Length != n * 20) _buffers[mm] = buf = new float[n * 20];
        for (int i = 0; i < n; i++)
        {
            var t = data.T[i]; var tint = data.Tint[i]; var c = data.C[i]; int o = i * 20;
            buf[o + 0] = t.Basis.X.X; buf[o + 1] = t.Basis.Y.X; buf[o + 2] = t.Basis.Z.X; buf[o + 3] = t.Origin.X;
            buf[o + 4] = t.Basis.X.Y; buf[o + 5] = t.Basis.Y.Y; buf[o + 6] = t.Basis.Z.Y; buf[o + 7] = t.Origin.Y;
            buf[o + 8] = t.Basis.X.Z; buf[o + 9] = t.Basis.Y.Z; buf[o + 10] = t.Basis.Z.Z; buf[o + 11] = t.Origin.Z;
            buf[o + 12] = tint.R; buf[o + 13] = tint.G; buf[o + 14] = tint.B; buf[o + 15] = tint.A;
            buf[o + 16] = c.R; buf[o + 17] = c.G; buf[o + 18] = c.B; buf[o + 19] = c.A;
        }
        mm.Buffer = buf;
    }
}

/// <summary>
/// Fauna drawn from authoritative positions with render-only smoothing. Visible animals retain their full model
/// at any distance; performance comes from visibility rejection and batching, not replacement proxies.
/// </summary>
public partial class FaunaRenderer : Node3D
{
    private VivariumWorld _w = null!;
    private const int MorphVariants = 4;
    private sealed class VariantLayer
    {
        public MultiMeshInstance3D High = null!;
        public MultiMeshInstance3D? Curled;
        public int HighTris, CurledTris;
        public float[] HighBuf = System.Array.Empty<float>(), CurledBuf = System.Array.Empty<float>();
        public int HighCount, CurledCount;
    }
    private sealed class Layer
    {
        public VariantLayer[] Variants = new VariantLayer[MorphVariants];
        public double CycleHz;
    }
    private readonly Dictionary<string, Layer> _layers = new(StringComparer.Ordinal);
    /// <summary>
    /// Render-side motion track per animal: the two most recent authoritative positions with the simulated time
    /// each was reached. Animals are drawn one behaviour interval in the past, interpolated between them, so
    /// motion is continuous instead of move-stop-move. Also carries the smoothed yaw and walk-cycle phase.
    /// </summary>
    private sealed class Track
    {
        public Vector3 Prev, Cur, Shown;
        public double PrevT, CurT;
        public float Yaw, Speed;
        public double Phase;
        public Vector3 Up = Vector3.Up;
    }

    private readonly Dictionary<EntityId, Track> _tracks = new();

    public Camera3D? Camera { get; set; }
    public int Quality { get; set; } = 1;
    /// <summary>Animals drawn this frame (always the full model) and those skipped because they are off-screen.</summary>
    public int Drawn { get; private set; }
    public int OffScreen { get; private set; }
    public long TrianglesDrawn { get; private set; }

    public void Build(VivariumWorld w)
    {
        _w = w;
        foreach (var c in GetChildren()) c.QueueFree();
        _layers.Clear(); _tracks.Clear();
        foreach (var sp in w.Content.Fauna)
        {
            var hiMat = Bridge.Shader("res://Shaders/fauna.gdshader");
            hiMat.SetShaderParameter("base_color", Bridge.C(sp.BaseColor));
            hiMat.SetShaderParameter("ornament_color", Bridge.C(sp.OrnamentColor));
            hiMat.SetShaderParameter("wiggle", sp.Medium == Medium.Aquatic ? 1.0f : sp.Model is "isopod" or "beetle" ? 0.08f : 0.35f);
            hiMat.SetShaderParameter("wiggle_speed", sp.Model == "minnow" ? 11.0f : 7.0f);
            hiMat.SetShaderParameter("translucency", sp.Model is "shrimp" or "minnow" ? 0.35f : 0.1f);
            hiMat.SetShaderParameter("carapace", sp.Model switch { "isopod" => 0.15f, "triops" => 0.45f, "shrimp" => 0.35f, "springtail" => 0.0f, "beetle" => 0.3f, "silverfish" => 0.2f, _ => 0.0f });
            hiMat.SetShaderParameter("segment_rings", sp.Model switch { "springtail" => 5.5f, "silverfish" => 4.5f, _ => 0.0f });
            hiMat.SetShaderParameter("bloom", sp.Model switch { "springtail" => 1.0f, "isopod" => 0.85f, "silverfish" => 0.7f, "beetle" => 0.7f, "triops" or "shrimp" or "minnow" => 0.1f, _ => 0.6f });
            hiMat.SetShaderParameter("wet", sp.Model is "shrimp" or "minnow" or "triops" ? 1.0f : 0.0f);
            hiMat.SetShaderParameter("scales", sp.Model is "minnow" or "silverfish" ? 1.0f : 0.0f);
            var still = (ShaderMaterial)hiMat.Duplicate();
            still.SetShaderParameter("wiggle", 0.0f);
            var layer = new Layer();
            ulong speciesSeed = Hash.Fnv1a64("fauna.visual." + sp.Id);
            for (int v = 0; v < MorphVariants; v++)
            {
                ulong seed = Rng.Mix(speciesSeed, (ulong)(v + 1) * 0x9E3779B97F4A7C15UL);
                var hi = OrganismMeshes.Fauna(sp, seed);
                var vl = new VariantLayer
                {
                    High = MakeMmi($"Fauna_{sp.Id}_{v}_high", Bridge.ToArrayMesh(hi, hiMat)),
                    HighTris = hi.TriangleCount,
                };
                AddChild(vl.High);
                if (OrganismMeshes.FaunaCurled(sp, seed) is { } curled)
                {
                    vl.Curled = MakeMmi($"Fauna_{sp.Id}_{v}_curled", Bridge.ToArrayMesh(curled, still));
                    vl.CurledTris = curled.TriangleCount;
                    AddChild(vl.Curled);
                }
                layer.Variants[v] = vl;
            }
            layer.CycleHz = (sp.Model == "minnow" ? 11.0 : 7.0) / (2 * Math.PI) * 6;
            _layers[sp.Id] = layer;
        }
    }

    private static MultiMeshInstance3D MakeMmi(string name, ArrayMesh mesh) => new()
    {
        Name = name,
        CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = mesh, InstanceCount = 0 },
    };

    /// <summary>Smoothed display position of an animal (falls back to authoritative position).</summary>
    public Vector3 DisplayPosition(EntityId id)
    {
        if (_tracks.TryGetValue(id, out var tr)) return tr.Shown;
        var f = _w?.Fauna.Get(id);
        return f != null ? Bridge.V(f.Position) : Vector3.Zero;
    }

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("Fauna");
        if (_w == null) return;
        float k = 1 - Mathf.Exp(-(float)delta * 8f);
        // draw one behaviour interval in the past, at sub-tick precision
        double behaviourInterval = VivariumWorld.Cadence.FaunaBehaviour * Vivarium.Sim.Time.SimClock.FixedStepSeconds;
        double renderT = (_w.Clock.Tick + _w.Scheduler.TickFraction) * Vivarium.Sim.Time.SimClock.FixedStepSeconds - behaviourInterval;

        var cam = Camera;
        var camPos = cam?.GlobalPosition ?? Vector3.Zero;
        Drawn = OffScreen = 0; TrianglesDrawn = 0;
        // Each species has a small bank of full-detail morphs. Capacity is deliberately conservative so hash
        // imbalance cannot overflow a variant buffer; populations are small enough that this is cheap memory.
        foreach (var (id, layer) in _layers)
        {
            int n = _w.Fauna.CountOf(id);
            foreach (var vl in layer.Variants)
            {
                vl.HighCount = vl.CurledCount = 0;
                vl.HighBuf = Ensure(vl.High.Multimesh, vl.HighBuf, n);
                if (vl.Curled != null) vl.CurledBuf = Ensure(vl.Curled.Multimesh, vl.CurledBuf, n);
            }
        }
        var seen = new HashSet<EntityId>();
        foreach (var f in _w.Fauna.Items)
        {
            seen.Add(f.Id);
            var sp = _w.Content.FaunaOrThrow(f.SpeciesId);
            var ph = _w.FaunaSystem.PhenotypeOf(f);
            var target = Bridge.V(f.Position);
            double now = _w.Clock.SimSeconds;
            if (!_tracks.TryGetValue(f.Id, out var tr))
                _tracks[f.Id] = tr = new Track { Prev = target, Cur = target, Shown = target, PrevT = now, CurT = now, Yaw = (float)-f.Heading };
            else if (f.Grabbed || tr.Cur.DistanceTo(target) > 0.5f)
            {
                // held, released or reintroduced: jump there rather than slide across the island
                tr.Prev = tr.Cur = tr.Shown = target; tr.PrevT = tr.CurT = now;
            }
            else if (tr.Cur.DistanceSquaredTo(target) > 1e-12)
            {
                tr.Prev = tr.Shown; tr.PrevT = Math.Min(renderT, tr.CurT);
                tr.Cur = target; tr.CurT = Math.Max(now, tr.PrevT + 1e-6);
            }
            double span = tr.CurT - tr.PrevT;
            float alpha = span > 1e-9 ? (float)Math.Clamp((renderT - tr.PrevT) / span, 0, 1) : 1f;
            var shown = tr.Prev.Lerp(tr.Cur, alpha);
            float frameDist = new Vector2(shown.X - tr.Shown.X, shown.Z - tr.Shown.Z).Length();
            tr.Shown = shown;
            // face the direction of travel (or the simulated heading when standing), critically damped
            var motion = new Vector2(tr.Cur.X - tr.Prev.X, tr.Cur.Z - tr.Prev.Z);
            float yawTarget = motion.LengthSquared() > 1e-8 ? Mathf.Atan2(-motion.Y, motion.X) : (float)-f.Heading;
            tr.Yaw += Mathf.Wrap(yawTarget - tr.Yaw, -Mathf.Pi, Mathf.Pi) * k;
            // walk/swim cycle advances with on-screen speed (body lengths per real second), accumulated so it never jumps
            float bodyLen = (float)Math.Max(1e-4, ph.BodySize * sp.VisualScale);
            float speed = delta > 1e-6 ? frameDist / (float)delta / bodyLen : 0;
            tr.Speed += (speed - tr.Speed) * k;
            double idle = sp.Medium == Medium.Aquatic ? 0.15 : 0.0;
            tr.Phase = (tr.Phase + (idle + Math.Min(tr.Speed * 0.08, 1.5)) * delta * _layers[sp.Id].CycleHz) % 1.0;
            var d = (Pos: shown, Yaw: tr.Yaw);
            // full detail at any distance; only animals outside the view are skipped (invisible either way)
            float scale = (float)(ph.BodySize * sp.VisualScale);
            if (cam != null && !f.Grabbed && !cam.IsPositionInFrustum(d.Pos) && !cam.IsPositionInFrustum(d.Pos + Vector3.Up * scale)) { OffScreen++; continue; }
            // walkers follow the slope under them (damped so they don't jitter over bumps); swimmers stay level
            var upTarget = sp.Medium == Medium.Aquatic ? Vector3.Up : SurfaceFrame.SurfaceNormal(_w, d.Pos.X, d.Pos.Z, Math.Max(scale * 0.5, 0.01));
            tr.Up = (tr.Up + (upTarget - tr.Up) * k).Normalized();
            var basis = (SurfaceFrame.TiltTo(tr.Up) * new Basis(Vector3.Up, d.Yaw)).Scaled(new Vector3(scale, scale, scale));
            var layer = _layers[sp.Id];
            int variant = (int)(Rng.Mix(f.Id.Value, 0xFA0AUL) % MorphVariants);
            var vl = layer.Variants[variant];
            bool curled = vl.Curled != null && _w.FaunaSystem.IsCurled(f);
            int i = curled ? vl.CurledCount++ : vl.HighCount++;
            var buf = curled ? vl.CurledBuf : vl.HighBuf;
            int o = i * 16;
            buf[o + 0] = basis.X.X; buf[o + 1] = basis.Y.X; buf[o + 2] = basis.Z.X; buf[o + 3] = d.Pos.X;
            buf[o + 4] = basis.X.Y; buf[o + 5] = basis.Y.Y; buf[o + 6] = basis.Z.Y; buf[o + 7] = d.Pos.Y;
            buf[o + 8] = basis.X.Z; buf[o + 9] = basis.Y.Z; buf[o + 10] = basis.Z.Z; buf[o + 11] = d.Pos.Z;
            // custom.w packs the appendage scale (integer thousandths) with the walk-cycle phase (fraction)
            buf[o + 12] = (float)ph.HueShift; buf[o + 13] = (float)ph.OrnamentDensity; buf[o + 14] = (float)ph.PatternStrength;
            buf[o + 15] = (float)(Math.Round(ph.AppendageScale * 1000) + Math.Min(tr.Phase, 0.999));
            Drawn++;
        }
        foreach (var layer in _layers.Values)
            foreach (var vl in layer.Variants)
            {
                Upload(vl.High.Multimesh, vl.HighBuf, vl.HighCount);
                if (vl.Curled != null) Upload(vl.Curled.Multimesh, vl.CurledBuf, vl.CurledCount);
                TrianglesDrawn += (long)vl.HighCount * vl.HighTris + (long)vl.CurledCount * vl.CurledTris;
            }
        if (_tracks.Count > seen.Count + 64)
        {
            var stale = new List<EntityId>();
            foreach (var key in _tracks.Keys) if (!seen.Contains(key)) stale.Add(key);
            foreach (var key in stale) _tracks.Remove(key);
        }
    }

    private static float[] Ensure(MultiMesh mm, float[] buf, int needed)
    {
        if (mm.InstanceCount >= needed && buf.Length == mm.InstanceCount * 16) return buf;
        mm.InstanceCount = needed + needed / 2 + 8;
        return new float[mm.InstanceCount * 16];
    }

    private static void Upload(MultiMesh mm, float[] buf, int count)
    {
        mm.VisibleInstanceCount = count;
        if (count > 0) mm.Buffer = buf;
    }
}

/// <summary>Orients ground-hugging organisms to the surface under them.</summary>
internal static class SurfaceFrame
{
    /// <summary>Normal of whatever the organism stands on (terrain or a log/rock top), by central differences
    /// over <paramref name="e"/> metres so it follows the local slope rather than single-cell noise.</summary>
    public static Vector3 SurfaceNormal(VivariumWorld w, float x, float z, double e)
    {
        double hx0 = w.GroundHeight(new Vec2(x - e, z)), hx1 = w.GroundHeight(new Vec2(x + e, z));
        double hz0 = w.GroundHeight(new Vec2(x, z - e)), hz1 = w.GroundHeight(new Vec2(x, z + e));
        var n = new Vector3((float)(hx0 - hx1), (float)(2 * e), (float)(hz0 - hz1)).Normalized();
        // a sample that falls off a log or rock edge reads as a cliff: cap the tilt at ~60 degrees
        return n.Y < 0.5f ? new Vector3(n.X, 0, n.Z).Normalized() * 0.866f + Vector3.Up * 0.5f : n;
    }

    /// <summary>Shortest rotation taking +Y onto <paramref name="n"/>.</summary>
    public static Basis TiltTo(Vector3 n)
    {
        var axis = Vector3.Up.Cross(n);
        float s = axis.Length();
        return s < 1e-5f ? Basis.Identity : new Basis(axis / s, Mathf.Atan2(s, Vector3.Up.Dot(n)));
    }
}
