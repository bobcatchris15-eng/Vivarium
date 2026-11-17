using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Core;
using Vivarium.Sim.Repr;
using Vivarium.Sim.Repr.Plant;
using Vivarium.Repr;

namespace Vivarium.Game.Render;

public partial class FloraRenderer
{
    private PlantAssemblyRenderer? _graphPlants;
    private readonly Dictionary<ulong, GraphInput> _graphInputs = new();
    private readonly HashSet<ulong> _graphSeen = new();
    private readonly HashSet<ulong> _graphPublished = new();
    private readonly Queue<GraphInput> _graphQueue = new();
    private Task<(GraphInput Input, PlantDescriptor Plant)>? _graphTask;
    private int _graphEpoch, _graphTaskEpoch;
    private int _graphScanIndex = -1, _graphFloraVersion = -1, _graphTerrain = -1, _graphProps = -1;
    private long _graphTick = -1;
    private bool _graphScanned;
    private string? _graphError;
    private readonly Dictionary<string, (Material Stem, Material Leaf)> _graphMaterials = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PlanGenerator> _graphGenerators = new(StringComparer.Ordinal)
    {
        ["glowbriar"] = PlantDemoKit.Generator("glowbriar"), ["moonfern"] = PlantDemoKit.Generator("moonfern"),
        ["shadebell"] = PlantDemoKit.Generator("shadebell")
    };
    private readonly record struct GraphInput(ulong Seed, ulong Id, string Species, double X, double Y, double Z,
        int Age, int Biomass, int Health, double Yaw, Vec3 SupportNormal, GraphAxisSegment[]? Segments, double WaterDepth);

    // Arrays belong to a scan snapshot; compare their values, not their identities.
    private static bool SameGraphInput(GraphInput a, GraphInput b) =>
        (a with { Segments = null }) == (b with { Segments = null }) &&
        (ReferenceEquals(a.Segments, b.Segments) ||
         (a.Segments != null && b.Segments != null && a.Segments.SequenceEqual(b.Segments)));

    public bool GraphPlantsEnabled { get; private set; }
    public PlantAssemblyRenderer? GraphPlants => _graphPlants;
    public int GraphPlantCount => _graphInputs.Count;
    public bool GraphStartupReadyForDiagnostics => GraphStartupReady;
    public long GraphDescriptorsPublished { get; private set; }
    public void InvalidateGraphInputs() { _graphTick = -1; _accum = 999; }
    public string GraphPlantsDiagnostics => $"enabled={GraphPlantsEnabled} scanned={_graphScanned} plants={_graphInputs.Count} queued={_graphQueue.Count} working={_graphTask != null} chunks={_graphPlants?.ChunkCount ?? 0} pending={_graphPlants?.PendingBuilds ?? 0} error={_graphError ?? "none"}";
    private bool GraphStartupReady => !GraphPlantsEnabled || (_graphScanned && _graphScanIndex < 0 && _graphQueue.Count == 0 && _graphTask == null && _graphError == null);
    private bool GraphOwns(string species) => GraphPlantsEnabled && PlantGraphCatalog.Supports(species);

    private void GraphBuild()
    {
        _graphEpoch++;
        GraphDescriptorsPublished = 0;
        _graphTask = null; _graphQueue.Clear(); _graphInputs.Clear(); _graphSeen.Clear(); _graphPublished.Clear();
        _graphScanIndex = -1; _graphScanned = false; _graphError = null;
        _graphFloraVersion = _graphTerrain = _graphProps = -1; _graphTick = -1;
        if (_graphPlants != null && IsInstanceValid(_graphPlants)) _graphPlants.QueueFree();
        _graphPlants = null; _graphMaterials.Clear();
        bool review = Main.UserArgs.Any(arg => arg.EndsWith("-review", StringComparison.Ordinal));
        GraphPlantsEnabled = !Main.UserArgs.Contains("--legacy-plants")
            && (Main.UserArgs.Contains("--graph-plants") || !review);
        if (!GraphPlantsEnabled) return;
        _graphPlants = PlantDemoKit.NewRenderer("LiveGraphPlants", GraphMaterial);
        AddChild(_graphPlants);
    }

    private Material? GraphMaterial(string id, bool leaf)
    {
        if (!_graphMaterials.TryGetValue(id, out var pair))
        {
            var sp = _w.Content.FloraOrThrow(id);
            var profile = FloraVisualProfile.Load(id, sp: sp);
            var stem = Bridge.Shader("res://Shaders/flora.gdshader");
            if (sp.Shape == "duskpuff") stem.SetShaderParameter("dry_fungal_cuticle", true);
            NightFloraMaterials.Bind(stem, sp);
            stem.SetShaderParameter("stiffness", sp.Woody != null ? 7f : sp.Shape is "reed" or "herb" ? 1f : 3f);
            stem.SetShaderParameter("surface_mode", sp.Archetype switch { "moss" => 0, "lichen" => 1, "fungus" => 3, _ => 2 });
            stem.SetShaderParameter("deform_leaf_tips", MorphVariantsFor(sp.Shape) == MigratedMorphVariants);
            stem.SetShaderParameter("sway", 0f);
            Bridge.BindSurface(stem, "moss", Bridge.Surfaces.Moss);
            FloraSurfaceProfiles.Bind(stem, sp, photographedLeaves: profile == null);
            var leaves = profile?.LeafMaterial(animated: false);
            if (leaves != null) NightFloraMaterials.Bind(leaves, sp);
            if (profile != null && leaves != null)
            {
                Bridge.BindSurface(stem, "bark", profile.Bark);
                stem.SetShaderParameter("bark_tint", new Vector3(profile.BarkTint[0], profile.BarkTint[1], profile.BarkTint[2]));
                leaves.SetShaderParameter("plant_stiffness", sp.Woody != null ? 7f : 3f);
                leaves.SetShaderParameter("wind_strength", 0f);
            }
            pair = (stem, leaves ?? stem); _graphMaterials[id] = pair;
        }
        return leaf ? pair.Leaf : pair.Stem;
    }

    private void GraphProcess()
    {
        if (!GraphPlantsEnabled || _graphPlants == null) return;
        if (_graphTask is { IsCompleted: true } task)
        {
            _graphTask = null;
            if (_graphTaskEpoch == _graphEpoch)
            {
                if (task.IsFaulted) { _graphError = task.Exception?.GetBaseException().Message ?? "plant build failed"; GD.PushError("Graph plant: " + _graphError); }
                else if (!task.IsCanceled)
                {
                    var result = task.Result;
                    if (_graphInputs.TryGetValue(result.Input.Id, out var current) && SameGraphInput(current, result.Input))
                    {
                        _graphPlants.UpdatePlant(result.Plant); _graphPublished.Add(result.Input.Id);
                        _publishedPlants.Add(new EntityId(result.Input.Id)); GraphDescriptorsPublished++;
                    }
                }
            }
        }
        if (_graphScanIndex < 0 && (_graphTick != _w.Clock.Tick || _graphFloraVersion != _w.Flora.Version || _graphTerrain != _w.Terrain.Version || _graphProps != _w.Props.Version))
        {
            _graphTick = _w.Clock.Tick; _graphFloraVersion = _w.Flora.Version;
            _graphTerrain = _w.Terrain.Version; _graphProps = _w.Props.Version;
            _graphSeen.Clear(); _graphScanIndex = 0;
        }
        long start = Stopwatch.GetTimestamp();
        while (_graphScanIndex >= 0 && Stopwatch.GetElapsedTime(start).TotalMilliseconds < 1.5)
        {
            if (_graphFloraVersion != _w.Flora.Version) { _graphScanIndex = -1; _graphFloraVersion = -1; break; }
            if (_graphScanIndex >= _w.Flora.Items.Count)
            {
                foreach (ulong id in _graphInputs.Keys.Where(id => !_graphSeen.Contains(id)).ToArray())
                { _graphInputs.Remove(id); _graphPublished.Remove(id); _publishedPlants.Remove(new EntityId(id)); _graphPlants.Remove(id); }
                _graphScanIndex = -1; _graphScanned = true; break;
            }
            var f = _w.Flora.Items[_graphScanIndex++];
            if (!GraphOwns(f.SpeciesId)) continue;
            var sp = _w.Content.FloraOrThrow(f.SpeciesId);
            if (!_graphGenerators.ContainsKey(f.SpeciesId))
                _graphGenerators[f.SpeciesId] = PlantGraphCatalog.Create(sp);
            double ground = _w.GroundHeight(f.Position);
            // Floating species are placed on the live water surface only when water exists.
            double surface = sp.Tags.Contains("floating") ? _w.Water.SurfaceAt(f.Position) : double.NaN;
            double y = double.IsFinite(surface) ? surface : ground;
            double x = f.X, z = f.Z;
            Vec3 supportNormal = Vec3.Up;
            if (f.SpeciesId == "emberfan_fungus")
            {
                // An unsupported fruiting body is hidden until bark is available;
                // it must never remain suspended where a removed host once stood.
                if (!_w.FloraSystem.TryEmberfanSupport(f.Position,f.Id.Value,out var contact,out supportNormal)) continue;
                x=contact.X;y=contact.Y;z=contact.Z;
            }
            else if (sp.Climber == null && PlantGraphCatalog.Info(f.SpeciesId).AttachedSurface)
            {
                // GroundHeight samples the upper rendered rock/log surface as well as terrain.
                // Small symmetric differences estimate its local normal at the actual placement.
                const double step = .002;
                double dx = (_w.GroundHeight(new Vec2(f.X + step, f.Z)) - _w.GroundHeight(new Vec2(f.X - step, f.Z))) / (2 * step);
                double dz = (_w.GroundHeight(new Vec2(f.X, f.Z + step)) - _w.GroundHeight(new Vec2(f.X, f.Z - step))) / (2 * step);
                supportNormal = PlantMath.SafeNormal(new Vec3(-dx, 1, -dz), Vec3.Up);
            }
            GraphAxisSegment[]? segments = null;
            if (sp.Climber != null && f.ClimberSegments is { Count: > 0 } nodes)
            {
                // The simulation owns the node limit. Copy every recorded edge in
                // parent-before-child order into the plant-local coordinate frame.
                var copied = new List<GraphAxisSegment>(nodes.Count - 1);
                var incoming = new int[nodes.Count];
                Array.Fill(incoming, -1);
                var origin = new Vec3(f.X, y, f.Z);
                for (int n = 0; n < nodes.Count; n++)
                {
                    var node = nodes[n]; int parent = node.ParentIndex;
                    if (parent < 0 || parent >= n) continue;
                    var edgeStart = nodes[parent].Position - origin;
                    var end = node.Position - origin;
                    if ((end - edgeStart).LengthSq < 1e-14) continue;
                    incoming[n] = copied.Count;
                    copied.Add(new GraphAxisSegment(edgeStart, end, node.Normal, incoming[parent], node.Attached));
                }
                segments = copied.ToArray();
            }
            var input = new GraphInput(_w.Seed, f.Id.Value, f.SpeciesId, x, y, z,
                (int)Math.Round(Math.Clamp(f.Age / Math.Max(1, sp.MaturityAge), 0, 1) * 100),
                (int)Math.Round(Math.Clamp(f.Biomass / Math.Max(1e-9, sp.MaxBiomass), 0, 1) * 100),
                (int)Math.Round(Math.Clamp(f.Health, 0, 1) * 100),
                segments != null ? 0 : Rng.HashUnit(_w.Seed, Hash.Fnv1a64("graph.flora.yaw"), f.Id.Value) * Math.Tau, supportNormal, segments,
                f.SpeciesId == "tidebell" ? Math.Max(0, double.IsFinite(_w.Water.SurfaceAt(f.Position)) ? _w.Water.SurfaceAt(f.Position) - ground : 0) : 0);
            _graphSeen.Add(input.Id);
            if (!_graphInputs.TryGetValue(input.Id, out var old) || !SameGraphInput(old, input))
            { _graphInputs[input.Id] = input; _graphQueue.Enqueue(input); }
        }
        while (_graphTask == null && _graphQueue.Count > 0)
        {
            var input = _graphQueue.Dequeue();
            if (!_graphInputs.TryGetValue(input.Id, out var current) || !SameGraphInput(current, input)) continue;
            var generator = _graphGenerators[input.Species];
            var species = _w.Content.FloraOrThrow(input.Species);
            _graphTaskEpoch = _graphEpoch;
            _graphTask = Task.Run(() =>
            {
                PlantDescriptor plant;
                if (input.Segments != null)
                    // Simulation already grew these edges; do not shrink them again by age.
                    plant = WoodyGraphSpecies.BuildClimber(species, input.Seed, input.Id,
                        input.Segments, 1, input.Health / 100.0);
                else
                {
                    plant = generator.Create(input.Seed, input.Id);
                    // Vigour scales the lifelong design; changing health must not
                    // move every attachment or shrink the plant's support frame.
                    generator.Grow(plant, input.Age / 100.0, 1.0, null);
                }
                // Bake the captured support frame into organ geometry. Renderer yaw is then zero;
                // no mutable world state is read by the worker.
                bool bracket = input.Species == "emberfan_fungus";
                bool oriented = (bracket || input.SupportNormal != Vec3.Up) && input.Segments == null;
                if (oriented)
                {
                    if (bracket) EmberfanSupportPose.Apply(plant,input.SupportNormal);
                    else
                    {
                        var up = input.SupportNormal;
                        var heading = PlantMath.Horizontal(input.Yaw);
                        var right = PlantMath.SafeNormal(heading - up * heading.Dot(up), PlantMath.Perpendicular(up));
                        var forward = right.Cross(up);
                        Vec3 Rotate(Vec3 v) => right * v.X + up * v.Y + forward * v.Z;
                        for (int i = 0; i < plant.Organs.Length; i++)
                        {
                            var o = plant.Organs[i];
                            o.Start = Rotate(o.Start); o.End = Rotate(o.End);
                            o.StartTangent = Rotate(o.StartTangent); o.EndTangent = Rotate(o.EndTangent);
                            o.Up = Rotate(o.Up); plant.Organs[i] = o;
                        }
                        PlanGenerator.Summarize(plant);
                    }
                }
                for (int i = 0; i < plant.Organs.Length; i++) plant.Organs[i].Health = input.Health / 100f;
                plant.Position = new Vec3(input.X, input.Y, input.Z); plant.Yaw = oriented ? 0 : input.Yaw;
                plant.Scale = input.Segments != null ? 1 : 0.45 + 0.55 * Math.Sqrt(input.Biomass / 100.0);
                if (input.Species == "tidebell") TidebellWaterPose.Apply(plant, input.WaterDepth, plant.Scale);
                return (input, plant);
            });
        }
    }
}
