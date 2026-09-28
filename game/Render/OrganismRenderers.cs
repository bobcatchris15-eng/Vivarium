using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;
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
    private static int MorphVariantsFor(string shape) => shape is "roundleaf" or "pairedleaf" or "herb" or "trifoliate" or "vine_clinglace" or "vine_spiralvine" or "vine_fenhook"
        ? MigratedMorphVariants : DefaultMorphVariants;
    private sealed class VariantLayer { public MultiMeshInstance3D Full = null!; public MultiMeshInstance3D? Fruit, Juvenile; public int FullTris, FruitTris, JuvenileTris; public bool CanCastShadow; }
    private sealed class Layer
    {
        public VariantLayer[] Variants; public MultiMeshInstance3D? Veins; public int VeinTris;
        public int MorphCount; public FloraVisualProfile? Profile;
        public Layer(int count, FloraVisualProfile? profile = null)
        { MorphCount = count; Profile = profile; Variants = new VariantLayer[count * (profile == null ? 1 : 3)]; }
    }
    private readonly Dictionary<EntityId, int> _visualTiers = new();
    private static string MorphKey(string species, int variant) => species + "\u001f" + variant;
    private readonly Dictionary<string, Layer> _layers = new(StringComparer.Ordinal);
    private readonly Dictionary<EntityId, double> _wobbleStart = new();
    private double _accum = 999;
    private double _clock;
    // Keep the currently drawn buffers intact while preparing the next population snapshot. Each unit of
    // work is one individual or one upload, so camera input never waits for an entire flora rebuild.
    private const double RefreshBudgetMs = 2.0;
    private IEnumerator<bool>? _refresh;
    private readonly List<FloraIndividual> _snapshot = new();
    private readonly List<DeadPlant> _deadSnapshot = new();
    private readonly List<EntityId> _expired = new();
    private int _quality = 1;
    public Camera3D? Camera { get; set; }
    public int Quality
    {
        get => _quality;
        set
        {
            if (_quality == value) return;
            _quality = value;
            foreach (var layer in _layers.Values)
                foreach (var variant in layer.Variants)
                {
                    var shadow = value >= 1 && variant.CanCastShadow
                        ? GeometryInstance3D.ShadowCastingSetting.On : GeometryInstance3D.ShadowCastingSetting.Off;
                    variant.Full.CastShadow = shadow;
                    if (variant.Fruit != null) variant.Fruit.CastShadow = shadow;
                    if (variant.Juvenile != null) variant.Juvenile.CastShadow = shadow;
                }
        }
    }
    public int Visible_ { get; private set; }
    public long TrianglesDrawn { get; private set; }

    public void Build(VivariumWorld w)
    {
        _refresh?.Dispose(); _refresh = null;
        _snapshot.Clear(); _deadSnapshot.Clear(); _wobbleStart.Clear();
        _buffers.Clear(); _full.Clear(); _fruit.Clear(); _juvenile.Clear(); _veins.Clear();
        _visualTiers.Clear();
        Visible_ = 0; TrianglesDrawn = 0;
        _w = w;
        foreach (var c in GetChildren()) c.QueueFree();
        _layers.Clear();
        foreach (var sp in w.Content.Flora)
        {
            var profile = System.Environment.GetEnvironmentVariable("VIVARIUM_LEGACY_FLORA") == "1" ? null : FloraVisualProfile.Load(sp.Id);
            var mat = Bridge.Shader("res://Shaders/flora.gdshader");
            mat.SetShaderParameter("stiffness", sp.Woody != null ? 7.0f : sp.Shape is "reed" or "herb" ? 1.0f : 3.0f);
            mat.SetShaderParameter("surface_mode", sp.Archetype switch { "moss" => 0, "lichen" => 1, "fungus" => 3, "slime_mold" => 4, _ => 2 });
            mat.SetShaderParameter("deform_leaf_tips", MorphVariantsFor(sp.Shape) == MigratedMorphVariants);
            if (sp.Archetype is "fungus" or "slime_mold") mat.SetShaderParameter("sway", 0.0f);
            Bridge.BindSurface(mat, "moss", Bridge.Surfaces.Moss);
            FloraSurfaceProfiles.Bind(mat, sp, photographedLeaves: profile == null);
            var leafMat = profile?.LeafMaterial();
            if (profile != null)
            {
                Bridge.BindSurface(mat, "bark", profile.Bark);
                mat.SetShaderParameter("bark_tint", new Vector3(profile.BarkTint[0],profile.BarkTint[1],profile.BarkTint[2]));
                leafMat!.SetShaderParameter("plant_stiffness", sp.Woody != null ? 7f : 3f);
            }
            var layer = new Layer(MorphVariantsFor(sp.Shape), profile);
            ulong speciesSeed = Hash.Fnv1a64("flora.visual." + sp.Id);
            for (int v = 0; v < layer.Variants.Length; v++)
            {
                int morph = v % layer.MorphCount;
                int? tier = profile == null ? null : v / layer.MorphCount;
                ulong seed = Rng.Mix(speciesSeed, (ulong)(morph + 1) * 0x9E3779B97F4A7C15UL);
                var full = sp.Climber != null ? OrganismMeshes.ClimberNode(sp, seed, attached: true) : OrganismMeshes.Flora(sp, seed, visualDetail: tier);
                bool canCastShadow = sp.Colony == null && sp.Archetype is "plant" or "fungus" && sp.Height >= 0.055;
                bool castShadow = Quality >= 1 && canCastShadow;
                var vl = new VariantLayer { Full = MakeMmi($"Flora_{sp.Id}_{v}", profile == null ? Bridge.ToArrayMesh(full, mat) : profile.Compile(full, mat, leafMat!), castShadow), FullTris = full.TriangleCount, CanCastShadow = canCastShadow };
                AddChild(vl.Full);
                if (sp.Shape is "fern" or "veilfern" || sp.Climber != null || profile != null && sp.Id == "umbraheart")
                {
                    var young = sp.Climber != null ? OrganismMeshes.ClimberNode(sp, seed, attached: false) : OrganismMeshes.Flora(sp, seed, juvenile: true, visualDetail: tier);
                    vl.Juvenile = MakeMmi($"Flora_{sp.Id}_{v}_juvenile", profile == null ? Bridge.ToArrayMesh(young, mat) : profile.Compile(young, mat, leafMat!), castShadow);
                    vl.JuvenileTris = young.TriangleCount;
                    AddChild(vl.Juvenile);
                }
                if (OrganismMeshes.FloraFruiting(sp, seed, tier) is { } fruit)
                {
                    var fruitMat = (ShaderMaterial)mat.Duplicate();
                    if (sp.Reproduction == null) fruitMat.SetShaderParameter("surface_mode", 3);
                    vl.Fruit = MakeMmi($"Flora_{sp.Id}_{v}_fruit", profile == null ? Bridge.ToArrayMesh(fruit, fruitMat) : profile.Compile(fruit, fruitMat, leafMat!), castShadow);
                    vl.FruitTris = fruit.TriangleCount;
                    AddChild(vl.Fruit);
                }
                layer.Variants[v] = vl;
            }
            if (sp.CreepSpeed > 0 || sp.Climber != null)
            {
                // Persistent links joining each network node to the node it grew from (plasmodial veins or climber stems).
                var vein = new MeshData();
                var col = Primitives.Mix(sp.Color, sp.Color2, 0.3);
                // a slightly arched, pinched tube: thinner at mid-length like a real plasmodial vein
                var vpath = new List<Vec3>(); var vrad = new List<double>();
                for (int i = 0; i <= 8; i++)
                {
                    double t = i / 8.0;
                    vpath.Add(new Vec3(-0.02 + 1.04 * t, sp.Climber != null ? 0.008 * Math.Sin(Math.PI * t) : 0.1 * Math.Sin(Math.PI * t), 0));
                    vrad.Add(sp.Climber != null ? 0.85 : 0.75 + 0.25 * Math.Cos(t * 2 * Math.PI));
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
        if (_refresh == null)
        {
            if (_accum < (wobbling ? 0.05 : 0.4)) return;
            _accum = 0;
            _refresh = Refresh().GetEnumerator();
        }
        long started = Stopwatch.GetTimestamp();
        do
        {
            if (_refresh.MoveNext()) continue;
            _refresh.Dispose(); _refresh = null;
            break;
        } while (Stopwatch.GetElapsedTime(started).TotalMilliseconds < RefreshBudgetMs);
    }

    public override void _ExitTree()
    {
        _refresh?.Dispose(); _refresh = null;
        _buffers.Clear(); _snapshot.Clear(); _deadSnapshot.Clear(); _wobbleStart.Clear();
    }

    private readonly Dictionary<string, (List<Transform3D> T, List<Color> Tint, List<Color> C)> _full = new(), _fruit = new(), _juvenile = new(), _veins = new();

    /// <summary>
    /// Where a climber or bracket attaches: the nearest log or rock (sim angle toward it from p, its top height,
    /// and for logs the point on the trunk surface closest to p). Null when nothing is within reach.
    /// </summary>
    private (double Angle, double Top, double Dist, Vector3 Surface, double Outward)? Anchor(Vector2 p, double reach, HashSet<string>? supports = null)
    {
        (double, double, double, Vector3, double)? best = null;
        double bestD = reach;
        var sp = new Vivarium.Sim.Core.Vec2(p.X, p.Y);
        if (supports == null || supports.Contains("log"))
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
        if (supports == null || supports.Contains("rock"))
        foreach (var r in _w.Props.Rocks)
        {
            double d = Math.Max(0, Vivarium.Sim.Core.Vec2.Distance(sp, r.Position) - r.FootprintRadius * 0.8);
            if (d > bestD) continue;
            bestD = d;
            var to = r.Position - sp;
            double ang = to.LengthSq > 1e-10 ? to.Angle : 0;
            best = (ang, r.Y + r.SizeY, d, new Vector3((float)r.X, (float)(r.Y + r.SizeY * 0.5), (float)r.Z), ang + Math.PI);
        }
        if (supports != null && supports.Contains("woody"))
        {
            foreach (var w in _snapshot)
            {
                var wsp = _w.Content.FloraOrThrow(w.SpeciesId);
                if (wsp.Woody == null) continue;
                double d = Vivarium.Sim.Core.Vec2.Distance(sp, w.Position);
                if (d > bestD) continue;
                bestD = d;
                var to = w.Position - sp;
                double ang = to.LengthSq > 1e-10 ? to.Angle : 0;
                double height = wsp.Height * (0.45 + 0.55 * Math.Sqrt(w.BiomassFraction(wsp)));
                double ground = _w.GroundHeight(w.Position);
                double boleR = Math.Max(0.08, wsp.RadiusAtMax * 0.12);
                var toNorm = to.LengthSq > 1e-10 ? to.Normalized() : new Vivarium.Sim.Core.Vec2(1, 0);
                var surfacePt = w.Position - toNorm * boleR;
                best = (ang, ground + height, Math.Max(0, d - boleR),
                    new Vector3((float)surfacePt.X, (float)ground, (float)surfacePt.Z), (-toNorm).Angle);
            }
        }
        return best;
    }

    /// <summary>Explicit callers get a complete synchronous refresh; normal frames use the bounded path.</summary>
    public void Rebuild()
    {
        if (_w == null) return;
        _refresh?.Dispose(); _refresh = null;
        foreach (bool _ in Refresh()) { }
        _accum = 0;
    }

    private IEnumerable<bool> Refresh()
    {
        // The authoritative list may change between frames. Copy references once rather than keeping a
        // population enumerator alive, and check membership before preparing each individual.
        _snapshot.Clear(); _snapshot.AddRange(_w.Flora.Items);
        _deadSnapshot.Clear(); _deadSnapshot.AddRange(_w.DeadFlora.Items);
        var planes = Camera?.GetFrustum(); // one native array per refresh, never one per plant
        Plane[]? frustum = planes == null ? null : new Plane[planes.Count];
        if (planes != null) planes.CopyTo(frustum!, 0); // plane tests below stay entirely in managed code
        _expired.Clear();
        foreach (var (id, start) in _wobbleStart)
            if (_clock - start >= 1.2 || _w.Flora.Get(id) == null) _expired.Add(id);
        foreach (var id in _expired) _wobbleStart.Remove(id);
        foreach (var k in _layers.Keys)
            for (int v = 0; v < _layers[k].Variants.Length; v++)
            {
                var mk = MorphKey(k, v);
                var fl = Get(_full, mk); fl.T.Clear(); fl.Tint.Clear(); fl.C.Clear();
                var fr = Get(_fruit, mk); fr.T.Clear(); fr.Tint.Clear(); fr.C.Clear();
                var ju = Get(_juvenile, mk); ju.T.Clear(); ju.Tint.Clear(); ju.C.Clear();
            }
        int visible = 0;
        long triangles = 0;
        foreach (var f in _snapshot)
        {
            yield return false;
            if (_w.Flora.Get(f.Id) != f) continue;
            var sp = _w.Content.FloraOrThrow(f.SpeciesId);
            double r = f.Radius(sp);
            double h = sp.Colony != null
                ? sp.Colony.MaxHeight * (0.15 + 0.85 * f.HeightFactor)
                : sp.Height * (0.45 + 0.55 * Math.Sqrt(f.BiomassFraction(sp)));
            var pos = new Vector3((float)f.X, (float)_w.GroundHeight(f.Position), (float)f.Z);
            if (sp.Shape == "floatleaf")
            {
                // Pads are authored directly in metres (not the unit-XZ-radius-1 convention every other flora
                // shape uses), so fixing the horizontal scale to 1 keeps pad size constant regardless of the
                // individual's growth radius or local water depth. Only the vertical scale follows depth: the
                // petiole/flower stalks are built to reach unit Y = 1, so scaling Y by (surface - ground) + a
                // small proud offset lands every pad just above the true water surface instead of at a fixed
                // plant height (or, worse, fractionally submerged and hidden by the water surface shader).
                r = 1.0;
                double surface = _w.Water.SurfaceAt(f.Position);
                double rise = double.IsNaN(surface) ? 0.05 : Math.Max(0.02, surface - pos.Y);
                h = rise + 0.003;
            }
            if (frustum != null && !FloraVisible(frustum, pos, (float)r, (float)h)) continue;
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
            else if (sp.Archetype == "plant" && sp.Shape != "vine" && sp.Shape != "floatleaf")
            {
                var fp = f.Position;
                double e = Math.Max(_w.Grid.CellSize * 0.55, Math.Min(0.35, Math.Max(r, 0.05)));
                double gx = _w.FloraSystem.EffectiveLight(fp + new Vivarium.Sim.Core.Vec2(e, 0), f.Id)
                          - _w.FloraSystem.EffectiveLight(fp - new Vivarium.Sim.Core.Vec2(e, 0), f.Id);
                double gz = _w.FloraSystem.EffectiveLight(fp + new Vivarium.Sim.Core.Vec2(0, e), f.Id)
                          - _w.FloraSystem.EffectiveLight(fp - new Vivarium.Sim.Core.Vec2(0, e), f.Id);
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
            if (sp.Climber != null && f.ClimberSegments is { Count: > 1 } segs)
            {
                // Multi-segment climber: each node is rendered at its exact 3D position and orientation
                var cTint = new Color((float)f.Tint[0], (float)f.Tint[1], (float)f.Tint[2], 1f);
                int cVariant = (int)((hash >> 8) % (ulong)_layers[sp.Id].Variants.Length);
                for (int s = 0; s < segs.Count; s++)
                {
                    var seg = segs[s];
                    if (seg.Senescent) continue; // Leaves shed / withered during senescence!

                    var nodePos = new Vector3((float)seg.Position.X, (float)seg.Position.Y, (float)seg.Position.Z);
                    var fwd = new Vector3((float)seg.Forward.X, (float)seg.Forward.Y, (float)seg.Forward.Z);
                    var nrm = new Vector3((float)seg.Normal.X, (float)seg.Normal.Y, (float)seg.Normal.Z);
                    if (fwd.LengthSquared() < 1e-6f) fwd = Vector3.Up; else fwd = fwd.Normalized();
                    if (nrm.LengthSquared() < 1e-6f) nrm = Vector3.Forward; else nrm = nrm.Normalized();
                    var side = fwd.Cross(nrm);
                    if (side.LengthSquared() < 1e-6f) side = fwd.Cross(Vector3.Up);
                    if (side.LengthSquared() < 1e-6f) side = fwd.Cross(Vector3.Right);
                    side = side.Normalized();
                    var orthoNrm = side.Cross(fwd).Normalized();
                    // Mesh X = side, Mesh Y = fwd (along stem), Mesh Z = orthoNrm (outward from host)
                    var nodeBasis = new Basis(side, fwd, orthoNrm);

                    float nodeScale = 1.0f;
                    var nodeT = new Transform3D(nodeBasis.Scaled(new Vector3(nodeScale, nodeScale, nodeScale)), nodePos);
                    var nodeBucket = seg.Attached ? _full : _juvenile;
                    var nodeBd = Get(nodeBucket, MorphKey(sp.Id, cVariant));
                    nodeBd.T.Add(nodeT);
                    nodeBd.Tint.Add(cTint);
                    nodeBd.C.Add(new Color((hash % 1000) / 1000f, (float)f.Health, 0, ((hash >> 12) % 1000) / 1000f));
                    visible++;
                }
                continue;
            }
            if (sp.Climber != null && !f.ClimberAttached)
            {
                h = Math.Min(h, 0.035);
                r = Math.Max(r, 0.06);
            }
            if (sp.Shape == "rain_jelly")
            {
                // Jelly fungi change silhouette within minutes of wetting; keep that as render-time turgor
                // rather than manufacturing/losing authoritative biomass every weather tick.
                double jellyMoisture = _w.Fields.Moisture.Sample(f.Position);
                double swell = MathD.Clamp01((jellyMoisture - 0.24) / 0.62);
                r *= 0.72 + 0.58 * swell;
                h *= 0.52 + 1.05 * swell;
            }
            var t = new Transform3D(yawBasis.Scaled(new Vector3((float)r, (float)h, (float)r)), pos);
            if (sp.Shape == "bracket" && Anchor(new Vector2(pos.X, pos.Z), 0.35) is { } ba)
            {
                // shelves grow out of the trunk's side
                var at = ba.Surface + new Vector3(0, (float)(((hash >> 20) % 100) / 100.0 - 0.5) * 0.08f, 0);
                t = new Transform3D(Bridge.Yaw(ba.Outward).Scaled(new Vector3((float)r, (float)(h * 2.5), (float)r)), at - new Vector3(0, (float)h, 0));
            }
            else if (sp.Shape == "glass_antlers" && Anchor(new Vector2(pos.X, pos.Z), 0.22, new HashSet<string>(StringComparer.Ordinal) { "log" }) is { } ga)
            {
                // Glass Antlers are authored upright from their attachment foot. Plant them directly on the
                // cylindrical log surface, with a slight outward yaw so branches don't disappear into the wood.
                var at = ga.Surface + new Vector3(0, (float)(((hash >> 20) % 100) / 100.0 - 0.5) * 0.05f, 0);
                t = new Transform3D(Bridge.Yaw(ga.Outward).Scaled(new Vector3((float)r, (float)(h * 1.35), (float)r)), at);
            }
            float wobble = 0;
            if (_wobbleStart.TryGetValue(f.Id, out var ws)) wobble = (float)Math.Max(0, 1 - (_clock - ws) / 1.2);
            var custom = new Color((hash % 1000) / 1000f, (float)f.Health, wobble, ((hash >> 12) % 1000) / 1000f);
            var tint = new Color((float)f.Tint[0], (float)f.Tint[1], (float)f.Tint[2], 1f);
            var visualLayer = _layers[sp.Id];
            int variant = (int)((hash >> 8) % (ulong)visualLayer.MorphCount);
            if (visualLayer.Profile != null) variant += VisualTier(f.Id, pos, (float)h) * visualLayer.MorphCount;
            var vl = _layers[sp.Id].Variants[variant];
            bool reproductiveFruit = sp.Reproduction != null
                && f.FruitLoad > sp.Reproduction.MaxAttachedMass * 0.03
                && f.ReproductiveStage is PlantReproductiveStage.Developing or PlantReproductiveStage.Ripe;
            var bucket = vl.Juvenile != null && (f.Stage(sp) == FloraStage.Juvenile || (sp.Climber != null && !f.ClimberAttached)) ? _juvenile
                : (f.Fruiting || reproductiveFruit) && vl.Fruit != null ? _fruit : _full;
            var bd = Get(bucket, MorphKey(sp.Id, variant)); bd.T.Add(t); bd.Tint.Add(tint); bd.C.Add(custom);
            visible++;
        }
        // Dead vascular plants remain visible after leaving the living population. Reuse the species mesh and
        // drive pose/tint from authoritative corpse state so no per-corpse mesh or physics body is required.
        foreach (var dead in _deadSnapshot)
        {
            yield return false;
            if (_w.DeadFlora.Get(dead.Id) != dead) continue;
            var sp = _w.Content.FloraOrThrow(dead.SpeciesId);
            double remain = Math.Sqrt(dead.RemainingFraction);
            double r = dead.OriginalRadius * (0.82 + 0.18 * remain);
            double h = dead.OriginalHeight * (0.78 + 0.22 * remain);
            var pos = new Vector3((float)dead.X, (float)_w.GroundHeight(dead.Position), (float)dead.Z);
            if (frustum != null && !FloraVisible(frustum, pos, (float)r, (float)h)) continue;

            float lean = dead.Stage switch
            {
                DeadPlantStage.StandingDead => 0.05f + 0.10f * (float)dead.StageProgress,
                DeadPlantStage.Collapsing => Mathf.Lerp(0.15f, 1.38f, (float)dead.StageProgress),
                DeadPlantStage.Fallen => 1.42f,
                _ => 1.47f,
            };
            var basis = new Basis(Vector3.Up, (float)dead.CollapseHeading) * new Basis(Vector3.Forward, lean);
            if (dead.Stage >= DeadPlantStage.Fallen) h *= 0.92;
            var t = new Transform3D(basis.Scaled(new Vector3((float)r, (float)h, (float)r)), pos);

            int cell = _w.Grid.NearestDomainCell(dead.Position);
            double moisture = cell >= 0 ? _w.Fields.Moisture.Values[cell] : 0.5;
            double litterMass = cell >= 0 ? _w.Litter.FineMass[cell] + _w.Litter.CoarseMass[cell] : 0;
            double stageFade = dead.Stage switch
            {
                DeadPlantStage.StandingDead => 0.20 + 0.20 * dead.StageProgress,
                DeadPlantStage.Collapsing => 0.42 + 0.16 * dead.StageProgress,
                DeadPlantStage.Fallen => 0.60 + 0.20 * dead.StageProgress,
                _ => 0.82 + 0.18 * dead.StageProgress,
            };
            double litterBlend = MathD.Clamp01(stageFade * (0.7 + Math.Min(0.3, litterMass * 0.08)));
            var localLitter = moisture > 0.62
                ? new Color(0.47f, 0.42f, 0.31f, 1f)
                : moisture < 0.28 ? new Color(0.72f, 0.64f, 0.46f, 1f)
                : new Color(0.60f, 0.53f, 0.36f, 1f);
            var post = sp.PostLife;
            var deadColor = post != null
                ? new Color((float)post.DeadColor[0], (float)post.DeadColor[1], (float)post.DeadColor[2], 1f)
                : new Color(0.62f, 0.54f, 0.38f, 1f);
            if (post != null)
            {
                float dry = (float)(MathD.Clamp01((0.36 - moisture) / 0.36) * post.DryBleach);
                float wet = (float)(MathD.Clamp01((moisture - 0.58) / 0.42) * post.WetDarken);
                deadColor = deadColor.Lerp(Colors.White, dry).Darkened(wet);
            }
            float litterInfluence = (float)(post?.LitterColorInfluence ?? 0.7);
            var tint = deadColor.Lerp(localLitter, (float)(litterBlend * litterInfluence));
            ulong hash = Rng.Mix(dead.Id.Value, 0xD34DUL);
            var custom = new Color((hash % 1000) / 1000f, 0f, 0f, ((hash >> 12) % 1000) / 1000f);
            var visualLayer = _layers[sp.Id];
            int variant = (int)((hash >> 8) % (ulong)visualLayer.MorphCount);
            if (visualLayer.Profile != null) variant += VisualTier(dead.Id, pos, (float)h) * visualLayer.MorphCount;
            var bd = Get(_full, MorphKey(sp.Id, variant));
            bd.T.Add(t); bd.Tint.Add(tint); bd.C.Add(custom);
            visible++;
        }

        // slime-mold veins: thickness follows the biomass flowing through each link
        foreach (var (id, layer) in _layers)
        {
            if (layer.Veins == null) continue;
            var list = Get(_veins, id); list.T.Clear(); list.Tint.Clear(); list.C.Clear();
            foreach (var f in _snapshot)
            {
                yield return false;
                if (_w.Flora.Get(f.Id) != f) continue;
                if (f.SpeciesId != id) continue;
                var vsp = _w.Content.FloraOrThrow(id);

                // Multi-segment climber stems:
                if (vsp.Climber != null && f.ClimberSegments is { Count: > 1 } segs)
                {
                    for (int s = 0; s < segs.Count; s++)
                    {
                        var seg = segs[s];
                        if (seg.ParentIndex < 0 || seg.ParentIndex >= segs.Count) continue;
                        var pSeg = segs[seg.ParentIndex];
                        var pA = new Vector3((float)pSeg.Position.X, (float)pSeg.Position.Y, (float)pSeg.Position.Z);
                        var pB = new Vector3((float)seg.Position.X, (float)seg.Position.Y, (float)seg.Position.Z);
                        var segD = pB - pA;
                        if (segD.Length() < 1e-4f) continue;
                        float cFrac = (float)Math.Sqrt(f.BiomassFraction(vsp));
                        float cThick = (0.0028f + 0.0048f * cFrac) * (seg.Senescent ? 0.6f : 1.0f);
                        var cXAxis = segD;
                        var norm = new Vector3((float)seg.Normal.X, (float)seg.Normal.Y, (float)seg.Normal.Z);
                        if (norm.LengthSquared() < 1e-6f) norm = Vector3.Up; else norm = norm.Normalized();
                        var cZAxis = cXAxis.Cross(norm);
                        if (cZAxis.LengthSquared() < 1e-6f) cZAxis = cXAxis.Cross(Vector3.Up);
                        cZAxis = cZAxis.Normalized() * cThick;
                        var cYAxis = cZAxis.Cross(cXAxis).Normalized() * cThick;
                        list.T.Add(new Transform3D(new Basis(cXAxis, cYAxis, cZAxis), pA));
                        list.Tint.Add(new Color((float)f.Tint[0], (float)f.Tint[1], (float)f.Tint[2], 1f));
                        ulong sHash = Rng.Mix(f.Id.Value, (ulong)s);
                        list.C.Add(new Color((sHash % 1000) / 1000f, (float)(seg.Senescent ? f.Health * 0.4 : f.Health), 0, ((sHash >> 12) % 1000) / 1000f));
                    }
                    continue;
                }

                if (f.ParentId.IsNone || _w.Flora.Get(f.ParentId) is not { } parent) continue;
                var a = new Vector3((float)parent.X, (float)_w.GroundHeight(parent.Position) + 0.004f, (float)parent.Z);
                var b = new Vector3((float)f.X, (float)_w.GroundHeight(f.Position) + 0.004f, (float)f.Z);
                var d = b - a;
                if (d.Length() < 1e-4f || d.Length() > 0.6f) continue;
                float frac = (float)Math.Sqrt(Math.Min(f.BiomassFraction(vsp), parent.BiomassFraction(vsp)));
                float thick = vsp.Climber != null ? 0.0025f + 0.0045f * frac : 0.004f + 0.009f * frac;
                var xAxis = d; var zAxis = xAxis.Cross(Vector3.Up).Normalized() * thick; var yAxis = zAxis.Cross(xAxis).Normalized() * thick * 0.45f;
                list.T.Add(new Transform3D(new Basis(xAxis, yAxis, zAxis), a));
                list.Tint.Add(new Color((float)f.Tint[0], (float)f.Tint[1], (float)f.Tint[2], 1f));
                ulong hash = Rng.Mix(f.Id.Value, 0xF10);
                list.C.Add(new Color((hash % 1000) / 1000f, (float)Math.Min(f.Health, parent.Health), 0, ((hash >> 12) % 1000) / 1000f));
            }
            yield return true;
            Fill(layer.Veins.Multimesh, list);
            triangles += (long)list.T.Count * layer.VeinTris;
        }
        foreach (var (id, layer) in _layers)
            for (int v = 0; v < layer.Variants.Length; v++)
            {
                yield return true;
                var vl = layer.Variants[v];
                string mk = MorphKey(id, v);
                // Full and fruit share one upload step: a phase change never briefly draws both forms.
                Fill(vl.Full.Multimesh, Get(_full, mk));
                if (vl.Fruit != null) Fill(vl.Fruit.Multimesh, Get(_fruit, mk));
                if (vl.Juvenile != null) Fill(vl.Juvenile.Multimesh, Get(_juvenile, mk));
                triangles += (long)vl.Full.Multimesh.InstanceCount * vl.FullTris
                    + (vl.Fruit != null ? (long)vl.Fruit.Multimesh.InstanceCount * vl.FruitTris : 0)
                    + (vl.Juvenile != null ? (long)vl.Juvenile.Multimesh.InstanceCount * vl.JuvenileTris : 0);
            }
        Visible_ = visible; TrianglesDrawn = triangles;
        _snapshot.Clear();
        foreach (var id in _visualTiers.Keys.Where(id => _w.Flora.Get(id) == null && _w.DeadFlora.Get(id) == null).ToArray()) _visualTiers.Remove(id);
    }

    private int VisualTier(EntityId id, Vector3 position, float height)
    {
        if (Camera == null) return 0;
        float viewport = Camera.GetViewport().GetVisibleRect().Size.Y;
        float depth = Math.Max(.05f,(position + Vector3.Up * height * .5f - Camera.GlobalPosition).Dot(-Camera.GlobalBasis.Z));
        float pixels = Camera.Projection == Camera3D.ProjectionType.Orthogonal
            ? height * viewport / Camera.Size
            : height * viewport / (2f * depth * MathF.Tan(Mathf.DegToRad(Camera.Fov) * .5f));
        int tier = FloraDetail.Select(pixels, _visualTiers.TryGetValue(id,out int old) ? old : -1);
        _visualTiers[id] = tier;
        return tier;
    }

    // World-space slack added around the bounding sphere before the frustum test. The instance list is only
    // rebuilt every 0.4s (see _Process), so a plant that is off-frustum now but would rotate into view before
    // the next rebuild must still test as visible; this margin absorbs a normal camera turn across that window.
    private const float FrustumSafetyMargin = 2.0f;

    private static bool FloraVisible(Plane[] frustum, Vector3 basePos, float radius, float height)
    {
        // Occlusion is intentionally never tested here: a plant that is only partly behind a log/rock must never
        // be culled, since any ray-based "opaque in front" test flips on/off with sub-pixel camera motion and
        // reads as pop-in/out. Frustum membership is the only rejection criterion, tested against an inflated
        // bounding sphere so genuinely off-screen plants are still dropped.
        float mid = Math.Max(height * 0.5f, 0.01f);
        var center = basePos + Vector3.Up * mid;
        float sphereRadius = Math.Max(radius, mid) + FrustumSafetyMargin;
        // Godot frustum planes face outward: inside points have negative distance, outside positive.
        foreach (var plane in frustum)
            if (plane.DistanceTo(center) > sphereRadius) return false;
        return true;
    }

    private static (List<Transform3D> T, List<Color> Tint, List<Color> C) Get(Dictionary<string, (List<Transform3D>, List<Color>, List<Color>)> d, string k)
    {
        if (!d.TryGetValue(k, out var v)) d[k] = v = (new List<Transform3D>(), new List<Color>(), new List<Color>());
        return v;
    }

    /// <summary>Uploads all instances in one packed buffer (12 transform + 4 instance-colour tint + 4 custom floats
    /// each) instead of two engine calls per instance, which caused frame hitches once the island filled with plants.</summary>
    // Per-renderer ownership is essential: a static dictionary kept the meshes and buffers of every
    // replaced world alive after their nodes were freed.
    private readonly Dictionary<MultiMesh, float[]> _buffers = new();

    private void Fill(MultiMesh mm, (List<Transform3D> T, List<Color> Tint, List<Color> C) data)
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
        public FaunaAnimationDef Animation = null!;
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
        public float Yaw, Speed, Pitch, Bend, Activity;
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
            FaunaBodyProfiles.Bind(hiMat, sp);
            hiMat.SetShaderParameter("translucency", sp.Model is "shrimp" or "minnow" ? 0.35f : 0.1f);
            hiMat.SetShaderParameter("carapace", sp.Model switch { "isopod" => 0.15f, "triops" => 0.45f, "shrimp" => 0.35f, "springtail" => 0.0f, "beetle" => 0.3f, "silverfish" => 0.2f, _ => 0.0f });
            hiMat.SetShaderParameter("segment_rings", sp.Model switch { "springtail" => 5.5f, "silverfish" => 4.5f, _ => 0.0f });
            hiMat.SetShaderParameter("bloom", sp.Model switch { "springtail" => 1.0f, "isopod" => 0.85f, "silverfish" => 0.7f, "beetle" => 0.7f, "triops" or "shrimp" or "minnow" => 0.1f, _ => 0.6f });
            hiMat.SetShaderParameter("wet", sp.Model is "shrimp" or "minnow" or "triops" ? 1.0f : 0.0f);
            hiMat.SetShaderParameter("scales", sp.Model is "minnow" or "silverfish" ? 1.0f : 0.0f);
            var still = (ShaderMaterial)hiMat.Duplicate();
            still.SetShaderParameter("anim_amplitude", 0.0f);
            var layer = new Layer { Animation = sp.Animation };
            ulong speciesSeed = Hash.Fnv1a64("fauna.visual." + sp.Id);
            for (int v = 0; v < MorphVariants; v++)
            {
                ulong seed = Rng.Mix(speciesSeed, (ulong)(v + 1) * 0x9E3779B97F4A7C15UL);
                var hi = OrganismMeshes.Fauna(sp, seed);
                var vl = new VariantLayer
                {
                    High = MakeMmi($"Fauna_{sp.Id}_{v}_high", Bridge.ToArrayMesh(hi, hiMat, signedVertexData: true)),
                    HighTris = hi.TriangleCount,
                };
                AddChild(vl.High);
                if (OrganismMeshes.FaunaCurled(sp, seed) is { } curled)
                {
                    vl.Curled = MakeMmi($"Fauna_{sp.Id}_{v}_curled", Bridge.ToArrayMesh(curled, still, signedVertexData: true));
                    vl.CurledTris = curled.TriangleCount;
                    AddChild(vl.Curled);
                }
                layer.Variants[v] = vl;
            }
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

    /// <summary>Render phase for deterministic distance-clock checks.</summary>
    public double DisplayPhase(EntityId id) => _tracks.TryGetValue(id, out var tr) ? tr.Phase : 0;

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
        var planes = cam?.GetFrustum();
        Plane[]? frustum = planes == null ? null : new Plane[planes.Count];
        if (planes != null) planes.CopyTo(frustum!, 0);
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
                tr.Bend = tr.Speed = tr.Activity = 0;
            }
            else if (tr.Cur.DistanceSquaredTo(target) > 1e-12)
            {
                tr.Prev = tr.Shown; tr.PrevT = Math.Min(renderT, tr.CurT);
                tr.Cur = target; tr.CurT = Math.Max(now, tr.PrevT + 1e-6);
            }
            double span = tr.CurT - tr.PrevT;
            float alpha = span > 1e-9 ? (float)Math.Clamp((renderT - tr.PrevT) / span, 0, 1) : 1f;
            var shown = tr.Prev.Lerp(tr.Cur, alpha);
            float frameDist = shown.DistanceTo(tr.Shown);
            tr.Shown = shown;
            // face the direction of travel (or the simulated heading when standing), critically damped
            var motion = new Vector2(tr.Cur.X - tr.Prev.X, tr.Cur.Z - tr.Prev.Z);
            float yawTarget = motion.LengthSquared() > 1e-8 ? Mathf.Atan2(-motion.Y, motion.X) : (float)-f.Heading;
            float yawStep = Mathf.Wrap(yawTarget - tr.Yaw, -Mathf.Pi, Mathf.Pi) * k;
            tr.Yaw += yawStep;
            // Animation cadence is species-authored in body-relative units: apparent gait survives genetic size changes
            // and simulation time-scale changes without hard-coding a model name in the renderer.
            float bodyLen = (float)Math.Max(1e-4, ph.BodySize * sp.VisualScale);
            float speed = delta > 1e-6 ? frameDist / (float)delta / bodyLen : 0;
            tr.Speed += (speed - tr.Speed) * k;
            var layer = _layers[sp.Id];
            var anim = layer.Animation;
            // Curvature is heading change per body length travelled. Keep a short trailing memory
            // so the rear straightens after the head, and suppress bending while held or stationary.
            float bendTarget = frameDist > 1e-7f && !f.Grabbed
                ? Mathf.Clamp(yawStep * bodyLen / Math.Max(frameDist, bodyLen * 0.002f), -1.25f, 1.25f) : 0;
            float bendK = 1 - Mathf.Exp(-(float)delta * 4f);
            tr.Bend += (bendTarget - tr.Bend) * bendK;
            tr.Phase = (tr.Phase + FaunaGait.Advance(anim, frameDist, bodyLen, f.Grabbed ? 0 : delta)) % 1.0;
            // Ground stride stays constant; only lift/body activity fades when travel stops.
            float activityTarget = speed > 1e-5f && !f.Grabbed ? 1 : 0;
            tr.Activity += (activityTarget - tr.Activity) * k;
            float speed01 = FaunaGait.GroundSteps(anim) ? tr.Activity : (float)Math.Clamp(tr.Speed / anim.FullSpeed, 0, 1);
            var d = (Pos: shown, Yaw: tr.Yaw);
            // full detail at any distance; only animals outside the view are skipped (invisible either way)
            float scale = (float)(ph.BodySize * sp.VisualScale);
            if (frustum != null && !f.Grabbed)
            {
                float radius = Math.Max(scale * 1.5f, 0.05f);
                bool inView = true;
                for (int pIdx = 0; pIdx < frustum.Length; pIdx++)
                {
                    if (frustum[pIdx].DistanceTo(d.Pos) > radius) { inView = false; break; }
                }
                if (!inView) { OffScreen++; continue; }
            }
            // Walkers follow the surface; swimmers derive pitch from interpolated 3-D travel.
            // This is render-only and never feeds orientation back into ecology.
            var upTarget = sp.Medium == Medium.Aquatic ? Vector3.Up : (frameDist > 1e-4f ? SurfaceFrame.SurfaceNormal(_w, d.Pos.X, d.Pos.Z, Math.Max(scale * 0.5, 0.01)) : tr.Up);
            tr.Up = (tr.Up + (upTarget - tr.Up) * k).Normalized();
            var motion3 = tr.Cur - tr.Prev;
            float horizontal = new Vector2(motion3.X, motion3.Z).Length();
            float pitchTarget = sp.Medium == Medium.Aquatic && horizontal > 1e-5f ? Mathf.Atan2(motion3.Y, horizontal) : 0.0f;
            tr.Pitch += (pitchTarget - tr.Pitch) * k;
            var orient = SurfaceFrame.TiltTo(tr.Up) * new Basis(Vector3.Up, d.Yaw);
            if (sp.Medium == Medium.Aquatic) orient *= new Basis(Vector3.Back, tr.Pitch);
            var basis = orient.Scaled(new Vector3(scale, scale, scale));
            int variant = (int)(Rng.Mix(f.Id.Value, 0xFA0AUL) % MorphVariants);
            var vl = layer.Variants[variant];
            bool curled = vl.Curled != null && _w.FaunaSystem.IsCurled(f);
            int i = curled ? vl.CurledCount++ : vl.HighCount++;
            var buf = curled ? vl.CurledBuf : vl.HighBuf;
            int o = i * 16;
            buf[o + 0] = basis.X.X; buf[o + 1] = basis.Y.X; buf[o + 2] = basis.Z.X; buf[o + 3] = d.Pos.X;
            buf[o + 4] = basis.X.Y; buf[o + 5] = basis.Y.Y; buf[o + 6] = basis.Z.Y; buf[o + 7] = d.Pos.Y;
            buf[o + 8] = basis.X.Z; buf[o + 9] = basis.Y.Z; buf[o + 10] = basis.Z.Z; buf[o + 11] = d.Pos.Z;
            // custom.w packs appendage scale, a 4-bit motion-amplitude bucket, and locomotion phase.
            // Keeping this in one channel preserves the three inherited colour/pattern traits already using xyz.
            int speedBucket = (int)Math.Round(speed01 * 15.0f);
            buf[o + 12] = (float)ph.HueShift; buf[o + 13] = (float)ph.OrnamentDensity; buf[o + 14] = (float)ph.PatternStrength;
            // Flexible bodies reserve the integer part of x for a signed bend bucket;
            // hue remains in the residual. Zero/unpacked x is also a valid straight preview pose.
            buf[o + 12] = FaunaBodyProfiles.PackHue(sp, (float)ph.HueShift, tr.Bend);
            buf[o + 15] = (float)(Math.Round(ph.AppendageScale * 1000) * 16 + speedBucket + Math.Min(tr.Phase, 0.999));
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
