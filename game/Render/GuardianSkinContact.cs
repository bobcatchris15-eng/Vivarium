using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Godot;

namespace Vivarium.Game.Render;

/// <summary>
/// Bounded CPU linear-skinning diagnostic for evaluated feet and body underside.
/// Construct after the imported instance's axis/scale transform is established.
/// Evaluate after animation and SkeletonModifier3D updates, on the main thread.
/// This samples actual vertices; it does not establish triangle-interior clearance.
/// </summary>
public sealed class GuardianSkinContact
{
    public const int MaximumSamplesPerRegion = 128;
    public const int MaximumSurfaces = 128;
    public const int MaximumSourceVertices = 1_000_000;
    private const float WeightThreshold = .6f;
    private readonly Skeleton3D _skeleton;
    private Transform3D[] _restPoses=Array.Empty<Transform3D>(),_currentPoses=Array.Empty<Transform3D>();
    private bool[] _restLoaded=Array.Empty<bool>();
    private int[] _poseStamp=Array.Empty<int>();
    private int _evaluation;
    private Transform3D _restWorld,_evaluationWorld;
    public int LastAuditSampleCount {get;private set;}
    public int ChainCacheAuditBudget { get; set; }
    public int ChainCacheAudits { get; private set; }
    public int ChainCacheAuditedSamples { get; private set; }
    public int ChainCachePenetratingSamples { get; private set; }
    public int ChainCacheAuditedInfluences { get; private set; }
    public float MaximumChainCacheAuditError { get; private set; }
    /// <summary>Audit seam: compares the lowest point with the clearance-based one.</summary>
    public int LowestPointAuditBudget { get; set; }
    public int LowestPointAudits { get; private set; }
    public int LowestPointAuditDifferences { get; private set; }
    public float MaximumLowestPointAuditError { get; private set; }
    private readonly bool _continuousBody;
    private readonly string _chainPrefix;
    private readonly Dictionary<string, List<Sample>> _samples = new();
    private readonly HashSet<MeshInstance3D> _meshes = new();
    private readonly List<Sample> _nasalPad = new();
    private readonly List<MeshScan> _meshScans = new();
    private readonly List<Candidate> _nasalCandidates = new();
    private static readonly string[] Regions = { "Hand_L", "Hand_R", "Foot_L", "Foot_R", "Body", "Tail", "Head" };
    public bool Valid { get; private set; }
    /// <summary>When true, deformation/contact is applied via GPU shader uniforms instead of CPU mesh re-upload.</summary>
    public bool GpuDeformationEnabled { get; set; } = true;
    public string Diagnostic { get; private set; } = "Not initialized";
    public float MaximumRestReconstructionError { get; private set; }
    public int RestVerticesChecked { get; private set; }
    public int SourceVerticesScanned { get; private set; }
    /// <summary>Where the construction time went: array reads, the per-vertex scan, the selection.</summary>
    public readonly Dictionary<string, double> BuildTimings = new();

    private sealed class Sample
    {
        public Vector3 Vertex;
        public int[] Bones = Array.Empty<int>();
        public float[] Weights = Array.Empty<float>();
        // Godot Skin bind poses ARE inverse binds. Do not invert again.
        public Transform3D[] InverseBinds = Array.Empty<Transform3D>();
        public Vector3 RestWorld;
        public bool[] ChainInfluences = Array.Empty<bool>();
        public float ChainDenominator;
    }

    /// <summary>Per-surface scan data kept in flat arrays. A guardian mesh has a
    /// hundred thousand vertices, so the scan records what it learns in arrays
    /// instead of allocating a sample object per vertex: the old form allocated
    /// hundreds of megabytes per world and stalled the frame on garbage
    /// collection, which cost more than a second of world start-up.</summary>
    private sealed class SurfaceScan
    {
        public Vector3[] Vertices = Array.Empty<Vector3>();
        public int[] Binds = Array.Empty<int>();
        public float[] Weights = Array.Empty<float>();
        public int Influences;
        public Vector3[] RestWorld = Array.Empty<Vector3>();
        public byte[] RegionMask = Array.Empty<byte>();
        public int[] ChainBone = Array.Empty<int>();
        public bool NasalPad;
    }

    private sealed class MeshScan
    {
        public MeshInstance3D Mesh = null!;
        public int[] BoneOfBind = Array.Empty<int>();
        public Transform3D[] InverseBinds = Array.Empty<Transform3D>();
        public List<SurfaceScan> Surfaces = new();
        public bool Used;
    }

    /// <summary>A qualifying vertex, addressed by its position in the scan arrays.</summary>
    private readonly struct Candidate
    {
        public readonly int Mesh, Surface, Vertex;
        public Candidate(int mesh, int surface, int vertex) { Mesh = mesh; Surface = surface; Vertex = vertex; }
    }

    public readonly struct Clearance
    {
        public readonly string Region;
        public readonly bool Valid;
        public readonly int SampleCount;
        public readonly float Minimum;
        public readonly Vector3 LowestPoint;
        public Clearance(string region, bool valid, int count, float minimum, Vector3 lowest)
        { Region = region; Valid = valid; SampleCount = count; Minimum = minimum; LowestPoint = lowest; }
    }

    /// <param name="restToleranceMeters">World-space reconstruction tolerance; defaults to 2mm.</param>
    public GuardianSkinContact(Node3D instance, Skeleton3D skeleton, float restToleranceMeters = .002f,
        bool continuousBody = false, string chainPrefix = "Tail", bool biped = false)
    {
        _skeleton = skeleton;
        _continuousBody = continuousBody; _chainPrefix = chainPrefix;
        if (Array.IndexOf(Vivarium.Game.App.Main.UserArgs, "--guardian-chain-cache-audit") >= 0)
            ChainCacheAuditBudget = 32;
        foreach (string region in Regions) _samples[region] = new List<Sample>();
        try
        {
            if (!float.IsFinite(restToleranceMeters) || restToleranceMeters <= 0)
                throw new InvalidOperationException("Invalid rest reconstruction tolerance");
            _restWorld=skeleton.GlobalTransform;RequireTransform(_restWorld,"skeleton world");
            int bones=skeleton.GetBoneCount();_restPoses=new Transform3D[bones];_currentPoses=new Transform3D[bones];_restLoaded=new bool[bones];_poseStamp=new int[bones];
            var candidates = new Dictionary<string, List<Candidate>>();
            foreach (string region in Regions) candidates[region] = new List<Candidate>();
            int surfaceCount = 0;
            double arraysMs = 0, vertexMs = 0;
            int gc0 = GC.CollectionCount(0), gc1 = GC.CollectionCount(1), gc2 = GC.CollectionCount(2);
            long allocated = GC.GetTotalAllocatedBytes(false);
            // The scan reads the rest pose of every bone it touches, so read
            // them all once instead of lazily inside the vertex loop.
            for (int bone = 0; bone < _skeleton.GetBoneCount(); bone++)
            { _restPoses[bone] = _skeleton.GetBoneGlobalRest(bone); _restLoaded[bone] = true; }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Visit(instance, mesh => ScanMesh(mesh, candidates, ref surfaceCount, restToleranceMeters, ref arraysMs, ref vertexMs));
            var selectStart = timer.Elapsed.TotalMilliseconds;
            var selected = new Dictionary<string, List<Candidate>>();
            foreach (string region in Regions) selected[region] = _continuousBody && region is "Body" or "Tail"
                ? SelectAxialSamples(candidates[region]) : SelectLowestBins(candidates[region]);
            // Materialize only the vertices that survived the selection. The
            // sample objects are identical to the ones the old per-vertex form
            // produced, in the same order.
            var materialized = new Dictionary<Candidate, Sample>();
            foreach (string region in Regions) _samples[region] = Materialize(selected[region], materialized);
            foreach (var candidate in _nasalCandidates) _nasalPad.Add(MaterializeOne(candidate, materialized));
            BuildTimings["arrays_ms"] = Math.Round(arraysMs, 1);
            BuildTimings["vertices_ms"] = Math.Round(vertexMs, 1);
            BuildTimings["select_ms"] = Math.Round(timer.Elapsed.TotalMilliseconds - selectStart, 1);
            BuildTimings["total_ms"] = Math.Round(timer.Elapsed.TotalMilliseconds, 1);
            BuildTimings["vertices"] = SourceVerticesScanned;
            BuildTimings["gc0"] = GC.CollectionCount(0) - gc0;
            BuildTimings["gc1"] = GC.CollectionCount(1) - gc1;
            BuildTimings["gc2"] = GC.CollectionCount(2) - gc2;
            BuildTimings["allocated_mb"] = Math.Round((GC.GetTotalAllocatedBytes(false) - allocated) / 1048576.0, 1);
            // Membership and weights are immutable sampler metadata. Preserve
            // the original slot order, including zero-weight padding slots.
            foreach (var sample in _samples["Tail"])
            {
                sample.ChainInfluences = new bool[sample.Weights.Length];
                for (int slot = 0; slot < sample.Weights.Length; slot++)
                    if (sample.ChainInfluences[slot] = _skeleton.GetBoneName(sample.Bones[slot]).ToString().StartsWith(_chainPrefix, StringComparison.Ordinal))
                        sample.ChainDenominator += sample.Weights[slot] * sample.Weights[slot];
            }
            foreach (string region in _continuousBody ? new[] { "Body", "Tail", "Head" } : biped ? new[] { "Foot_L", "Foot_R", "Body" } : new[] { "Hand_L", "Hand_R", "Foot_L", "Foot_R", "Body" })
                if (_samples[region].Count == 0)
                    throw new InvalidOperationException($"No qualifying actual skin vertices for {region}");
            Valid = true;
            Diagnostic = $"Rest reconstruction passed: {RestVerticesChecked} vertices, max error {MaximumRestReconstructionError:G6}m; " +
                (_continuousBody ? "up to132 axial and128 head samples. " : "up to128 samples per region. ") +
                "Linear skinning only, no blend shapes/shader displacement/triangle interiors.";
        }
        catch (Exception ex)
        {
            Valid = false;
            // A parallel scan reports the first failure through an aggregate.
            var inner = ex is AggregateException aggregate && aggregate.InnerExceptions.Count == 1
                ? aggregate.InnerExceptions[0] : ex;
            Diagnostic = inner.Message;
            foreach (var list in _samples.Values) list.Clear();
            _nasalPad.Clear();
        }
    }

    private static void Visit(Node node, Action<MeshInstance3D> visitor)
    {
        if (node is MeshInstance3D mesh) visitor(mesh);
        foreach (Node child in node.GetChildren()) Visit(child, visitor);
    }

    /// <summary>The per-vertex pass of the skin scan. It is a plain object with
    /// no captured state so the work can be split across cores without a
    /// closure: the closure form of this loop was ten times slower and
    /// allocated gigabytes on one of the guardian meshes.</summary>
    private sealed class VertexPass
    {
        public Vector3[] Vertices = Array.Empty<Vector3>();
        public int[] Binds = Array.Empty<int>();
        public float[] Weights = Array.Empty<float>();
        public int Influences;
        public Transform3D[] BindMatrix = Array.Empty<Transform3D>();
        public SurfaceScan Scan = null!;
        public float[] Errors = Array.Empty<float>();
        public int[] RegionMasks = Array.Empty<int>();
        public bool[] ChainBinds = Array.Empty<bool>();
        public int[] BoneMap = Array.Empty<int>();
        public Transform3D MeshWorld, RestWorld;
        public float Tolerance;
        public string MeshName = "";

        public void Run(int from, int to)
        {
            for (int vertex = from; vertex < to; vertex++)
            {
                if (!Vertices[vertex].IsFinite()) throw new InvalidOperationException("Nonfinite source vertex");
                float r0 = 0, r1 = 0, r2 = 0, r3 = 0, r4 = 0, r5 = 0, r6 = 0;
                float sum = 0; int chainBone = -1; float strongestChain = 0;
                for (int slot = 0; slot < Influences; slot++)
                {
                    int offset = vertex * Influences + slot; float weight = Weights[offset]; int bind = Binds[offset];
                    if (!float.IsFinite(weight) || weight < 0 || weight > 1) throw new InvalidOperationException("Invalid skin weight");
                    if (weight == 0) continue; // Zero slots can contain otherwise irrelevant padding indices.
                    if (bind < 0 || bind >= BindMatrix.Length) throw new InvalidOperationException("Skin bind index out of range");
                    sum += weight;
                    int mask = RegionMasks[bind];
                    if ((mask & 1) != 0) r0 += weight;
                    if ((mask & 2) != 0) r1 += weight;
                    if ((mask & 4) != 0) r2 += weight;
                    if ((mask & 8) != 0) r3 += weight;
                    if ((mask & 16) != 0) r4 += weight;
                    if ((mask & 32) != 0) r5 += weight;
                    if ((mask & 64) != 0) r6 += weight;
                    if (ChainBinds[bind] && weight > strongestChain) { strongestChain = weight; chainBone = BoneMap[bind]; }
                }
                if (Math.Abs(sum - 1) > .001f) throw new InvalidOperationException($"{MeshName}: weights sum {sum:G6}, expected1");
                // The same arithmetic the per-vertex sample form used, without
                // the sample: bone rest * inverse bind * vertex, weighted.
                var local = Vector3.Zero;
                var vertexPosition = Vertices[vertex];
                for (int slot = 0; slot < Influences; slot++)
                {
                    int offset = vertex * Influences + slot; float weight = Weights[offset];
                    if (weight == 0) continue;
                    int bind = Binds[offset];
                    local += (BindMatrix[bind] * vertexPosition) * weight;
                }
                var restWorld = RestWorld * local;
                var expected = MeshWorld * vertexPosition;
                float error = restWorld.DistanceTo(expected);
                Errors[vertex] = error;
                if (!float.IsFinite(error) || error > Tolerance)
                    throw new InvalidOperationException($"{MeshName}: rest reconstruction mismatch {error:G6}m at vertex {vertex}; bind/local space invalid");
                byte regionMask = 0;
                if (r0 >= WeightThreshold) regionMask |= 1;
                if (r1 >= WeightThreshold) regionMask |= 2;
                if (r2 >= WeightThreshold) regionMask |= 4;
                if (r3 >= WeightThreshold) regionMask |= 8;
                if (r4 >= WeightThreshold) regionMask |= 16;
                if (r5 >= WeightThreshold) regionMask |= 32;
                if (r6 >= WeightThreshold) regionMask |= 64;
                Scan.RestWorld[vertex] = restWorld;
                Scan.RegionMask[vertex] = regionMask;
                Scan.ChainBone[vertex] = chainBone;
            }
        }
    }

    private void ScanMesh(MeshInstance3D mesh, Dictionary<string, List<Candidate>> candidates,
        ref int surfaceCount, float tolerance, ref double arraysMs, ref double vertexMs)
    {
        if (mesh.Mesh == null) return;
        var skin = mesh.Skin ?? mesh.GetSkinReference()?.GetSkin();
        if (skin == null) return; // Unskinned decoration is outside evaluated-skin scope.
        if (mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton) != _skeleton)
            throw new InvalidOperationException($"{mesh.Name}: skin targets a different/unresolved skeleton");
        // Construction is synchronous: mesh placement and bind names cannot
        // change during this scan. Read them once, not per vertex/influence.
        var meshWorld = mesh.GlobalTransform;
        RequireTransform(meshWorld, $"{mesh.Name} world");
        int bindCount = skin.GetBindCount();
        var boneMap = new int[bindCount]; var inverseBinds = new Transform3D[bindCount];
        var regionMasks = new int[bindCount];
        var chainBinds = new bool[bindCount];
        for (int bind = 0; bind < bindCount; bind++)
        {
            string name = skin.GetBindName(bind).ToString();
            int bone = name.Length > 0 ? _skeleton.FindBone(name) : skin.GetBindBone(bind);
            if (bone < 0 || bone >= _skeleton.GetBoneCount())
                throw new InvalidOperationException($"{mesh.Name}: unmapped skin bind {bind} ({name})");
            boneMap[bind] = bone; inverseBinds[bind] = skin.GetBindPose(bind);
            string boneName = _skeleton.GetBoneName(bone).ToString();
            string canonical = CanonicalRegion(boneName);
            for (int region = 0; region < 4; region++)
                if (canonical == Regions[region]) regionMasks[bind] |= 1 << region;
            if (boneName is "Pelvis" or "Spine1" or "Chest" or "Lumbar" || boneName.StartsWith("Body", StringComparison.Ordinal)) regionMasks[bind] |= 1 << 4;
            chainBinds[bind] = boneName.StartsWith(_chainPrefix, StringComparison.Ordinal);
            if (chainBinds[bind]) regionMasks[bind] |= 1 << 5;
            if (boneName is "Head" or "Jaw") regionMasks[bind] |= 1 << 6;
            RequireTransform(inverseBinds[bind], $"{mesh.Name} inverse bind {bind}");
            RequireTransform(_skeleton.GetBoneGlobalRest(bone), $"{mesh.Name} bone rest {bone}");
        }
        bool used = false;
        int meshIndex = _meshScans.Count;
        var meshScan = new MeshScan { Mesh = mesh, BoneOfBind = boneMap, InverseBinds = inverseBinds };
        _meshScans.Add(meshScan);
        for (int surface = 0; surface < mesh.Mesh.GetSurfaceCount(); surface++)
        {
            if (++surfaceCount > MaximumSurfaces) throw new InvalidOperationException("Surface scan budget exceeded");
            string material = mesh.GetActiveMaterial(surface)?.ResourceName ?? "";
            if (material.Equals("CoatiFineFur", StringComparison.OrdinalIgnoreCase)) continue;
            var arrays = mesh.Mesh.SurfaceGetArrays(surface);
            var vertices = arrays[(int)Mesh.ArrayType.Vertex].AsVector3Array();
            if (vertices.Length == 0) continue;
            SourceVerticesScanned += vertices.Length;
            if (SourceVerticesScanned > MaximumSourceVertices) throw new InvalidOperationException("Vertex scan budget exceeded");
            long arrayStart = System.Diagnostics.Stopwatch.GetTimestamp();
            var boneVariant = arrays[(int)Mesh.ArrayType.Bones];
            int[] binds;
            if (boneVariant.VariantType == Variant.Type.PackedInt32Array) binds = boneVariant.AsInt32Array();
            else if (boneVariant.VariantType == Variant.Type.PackedFloat32Array)
            {
                var values = boneVariant.AsFloat32Array(); binds = new int[values.Length];
                for (int i = 0; i < values.Length; i++)
                {
                    if (!float.IsFinite(values[i]) || values[i] != MathF.Truncate(values[i]))
                        throw new InvalidOperationException("Nonintegral skin bind index");
                    binds[i] = checked((int)values[i]);
                }
            }
            else throw new InvalidOperationException($"{mesh.Name}: unsupported bone array type");
            var weightVariant = arrays[(int)Mesh.ArrayType.Weights];
            float[] weights;
            if (weightVariant.VariantType == Variant.Type.PackedFloat32Array) weights = weightVariant.AsFloat32Array();
            else if (weightVariant.VariantType == Variant.Type.PackedFloat64Array)
            {
                var values = weightVariant.AsFloat64Array(); weights = new float[values.Length];
                for (int i = 0; i < values.Length; i++) weights[i] = (float)values[i];
            }
            else throw new InvalidOperationException($"{mesh.Name}: unsupported weight array type");
            int influences = binds.Length / vertices.Length;
            if ((influences != 4 && influences != 8) || binds.Length != vertices.Length * influences || weights.Length != binds.Length)
                throw new InvalidOperationException($"{mesh.Name}: malformed skin arrays");
            arraysMs += System.Diagnostics.Stopwatch.GetElapsedTime(arrayStart).TotalMilliseconds;
            long vertexStart = System.Diagnostics.Stopwatch.GetTimestamp();
            // One matrix per skin bind: the bone rest pose times the inverse
            // bind. It is the same for every vertex that uses that bind, so it
            // is built once per bind instead of once per vertex influence.
            var bindMatrix = new Transform3D[bindCount];
            for (int bind = 0; bind < bindCount; bind++) bindMatrix[bind] = _restPoses[boneMap[bind]] * inverseBinds[bind];
            int surfaceIndex = meshScan.Surfaces.Count;
            var scan = new SurfaceScan
            {
                Vertices = vertices, Binds = binds, Weights = weights, Influences = influences,
                RestWorld = new Vector3[vertices.Length], RegionMask = new byte[vertices.Length],
                ChainBone = new int[vertices.Length],
                NasalPad = mesh.Name.ToString().Equals("coati_nasal_pad", StringComparison.OrdinalIgnoreCase)
                    || material.Equals("CoatiWetNasalPad", StringComparison.OrdinalIgnoreCase)
            };
            meshScan.Surfaces.Add(scan);
            // Every vertex is independent, so the scan is split across cores.
            // Each vertex writes only its own slots, and the arithmetic per
            // vertex is unchanged, so the result is identical.
            var errors = new float[vertices.Length];
            var pass = new VertexPass
            {
                Vertices = vertices, Binds = binds, Weights = weights, Influences = influences,
                BindMatrix = bindMatrix, Scan = scan, Errors = errors,
                RegionMasks = regionMasks, ChainBinds = chainBinds, BoneMap = boneMap,
                MeshWorld = meshWorld, RestWorld = _restWorld, Tolerance = tolerance,
                MeshName = mesh.Name.ToString()
            };
            // The scan runs on one thread. Splitting it across cores was tried
            // and measured slower, and it allocated gigabytes on one of the
            // guardian meshes: the per-vertex work is small enough that the
            // shared arrays and the thread hand-off cost more than the work.
            pass.Run(0, vertices.Length);
            RestVerticesChecked += vertices.Length;            for (int vertex = 0; vertex < vertices.Length; vertex++)
                MaximumRestReconstructionError = Math.Max(MaximumRestReconstructionError, errors[vertex]);
            vertexMs += System.Diagnostics.Stopwatch.GetElapsedTime(vertexStart).TotalMilliseconds;
            // The qualifying vertices become candidates in the same order the
            // per-vertex form produced them.
            for (int vertex = 0; vertex < vertices.Length; vertex++)
            {
                byte mask = scan.RegionMask[vertex];
                bool nasal = scan.NasalPad;
                if (mask == 0 && !nasal) continue;
                var candidate = new Candidate(meshIndex, surfaceIndex, vertex);
                if (nasal)
                {
                    if (_nasalCandidates.Count >= 4096) throw new InvalidOperationException("Nasal-pad diagnostic budget exceeded");
                    _nasalCandidates.Add(candidate); used = true;
                }
                for (int region = 0; region < Regions.Length; region++)
                    if ((mask & (1 << region)) != 0) { candidates[Regions[region]].Add(candidate); used = true; }
            }
        }
        if (used)
        {
            if (mesh.GetBlendShapeCount() != 0)
                throw new InvalidOperationException($"{mesh.Name}: blend shapes are unsupported for contact samples");
            _meshes.Add(mesh);
        }
    }

    // 8x8 world-rest XZ bins, two lowest actual vertices per occupied bin: <=128.
    // Empty bins remain empty; no fabricated support point is substituted.
    private List<Candidate> SelectLowestBins(List<Candidate> candidates)
    {
        var result = new List<Candidate>(); if (candidates.Count == 0) return result;
        float minX = float.PositiveInfinity, minZ = minX, maxX = float.NegativeInfinity, maxZ = maxX;
        foreach (var c in candidates)
        { var s = RestWorldOf(c); minX = Math.Min(minX, s.X); maxX = Math.Max(maxX, s.X); minZ = Math.Min(minZ, s.Z); maxZ = Math.Max(maxZ, s.Z); }
        var bins = new List<Candidate>[64];
        foreach (var c in candidates)
        {
            var s = RestWorldOf(c);
            int x = Math.Clamp((int)((s.X - minX) / Math.Max(.00001f, maxX - minX) * 8), 0, 7);
            int z = Math.Clamp((int)((s.Z - minZ) / Math.Max(.00001f, maxZ - minZ) * 8), 0, 7);
            var bin = bins[z * 8 + x] ??= new List<Candidate>(2);
            int at = 0; while (at < bin.Count && RestWorldOf(bin[at]).Y <= s.Y) at++;
            if (at < 2) { bin.Insert(at, c); if (bin.Count > 2) bin.RemoveAt(2); }
        }
        foreach (var bin in bins) if (bin != null) result.AddRange(bin);
        return result;
    }

    private Vector3 RestWorldOf(Candidate candidate) =>
        _meshScans[candidate.Mesh].Surfaces[candidate.Surface].RestWorld[candidate.Vertex];

    /// <summary>Builds the sample objects for the vertices that survived the selection.</summary>
    private Sample MaterializeOne(Candidate candidate, Dictionary<Candidate, Sample> cache)
    {
        if (cache.TryGetValue(candidate, out var existing)) return existing;
        var meshScan = _meshScans[candidate.Mesh];
        var surface = meshScan.Surfaces[candidate.Surface];
        var sample = new Sample
        {
            Vertex = surface.Vertices[candidate.Vertex],
            Bones = new int[surface.Influences],
            Weights = new float[surface.Influences],
            InverseBinds = new Transform3D[surface.Influences],
            RestWorld = surface.RestWorld[candidate.Vertex]
        };
        for (int slot = 0; slot < surface.Influences; slot++)
        {
            int offset = candidate.Vertex * surface.Influences + slot; float weight = surface.Weights[offset];
            if (weight == 0) continue; // Zero slots keep their padding values.
            int bind = surface.Binds[offset];
            sample.Bones[slot] = meshScan.BoneOfBind[bind];
            sample.Weights[slot] = weight;
            sample.InverseBinds[slot] = meshScan.InverseBinds[bind];
        }
        cache[candidate] = sample;
        return sample;
    }

    private List<Sample> Materialize(List<Candidate> candidates, Dictionary<Candidate, Sample> cache)
    {
        var result = new List<Sample>(candidates.Count);
        foreach (var c in candidates) result.Add(MaterializeOne(c, cache));
        return result;
    }

    private List<Candidate> SelectAxialSamples(List<Candidate> candidates)
    {
        // Coiled turns can share XZ bins. Keep the two lowest vertices on each
        // side of every axial joint instead, bounded to33*4=132 samples. This
        // follows all sections of a continuous body even when turns overlap.
        var bins = new Dictionary<(int bone, bool right), List<Candidate>>();
        foreach (var candidate in candidates)
        {
            var surface = _meshScans[candidate.Mesh].Surfaces[candidate.Surface];
            int bone = surface.ChainBone[candidate.Vertex];
            if (bone < 0) continue;
            var restWorld = surface.RestWorld[candidate.Vertex];
            var local = _skeleton.GetBoneGlobalRest(bone).AffineInverse() * _skeleton.GlobalTransform.AffineInverse() * restWorld;
            var key = (bone, local.X >= 0);
            if (!bins.TryGetValue(key, out var bin)) bins[key] = bin = new List<Candidate>(2);
            int at = 0; while (at < bin.Count && RestWorldOf(bin[at]).Y <= restWorld.Y) at++;
            if (at < 2) { bin.Insert(at, candidate); if (bin.Count > 2) bin.RemoveAt(2); }
        }
        var result = new List<Candidate>(); foreach (var bin in bins.Values) result.AddRange(bin);
        return result;
    }

    private void BeginEvaluation()
    {
        if(_skeleton.GetBoneCount()!=_currentPoses.Length)throw new InvalidOperationException("Skeleton changed; recreate contact sampler");
        if(_evaluation==int.MaxValue){Array.Clear(_poseStamp);_evaluation=0;}
        _evaluation++;_evaluationWorld=_skeleton.GlobalTransform;RequireTransform(_evaluationWorld,"current skeleton world");
    }
    private Transform3D BonePose(int bone,bool rest)
    {
        if(rest)
        {
            if(!_restLoaded[bone]){_restPoses[bone]=_skeleton.GetBoneGlobalRest(bone);_restLoaded[bone]=true;}
            return _restPoses[bone];
        }
        if(_poseStamp[bone]!=_evaluation)
        {
            var pose=_skeleton.GetBoneGlobalPose(bone);RequireTransform(pose,$"current bone {bone}");
            _currentPoses[bone]=pose;_poseStamp[bone]=_evaluation;
        }
        return _currentPoses[bone];
    }
    private Vector3 EvaluateVertex(Sample sample,bool rest)
    {
        var local=Vector3.Zero;
        for(int i=0;i<sample.Weights.Length;i++)
        {
            if(sample.Weights[i]==0)continue;
            local+=(BonePose(sample.Bones[i],rest)*sample.InverseBinds[i]*sample.Vertex)*sample.Weights[i];
        }
        return (rest?_restWorld:_evaluationWorld)*local;
    }
    /// <summary>Compare cached and original skinning at the current, already-modified pose.
    /// This diagnostic reads the actual rig and vertices without changing animation or contact.</summary>
    public float AuditPoseCache()
    {
        if(!Valid)return float.NaN;
        BeginEvaluation();float error=0;LastAuditSampleCount=0;
        foreach(var samples in _samples.Values)foreach(var sample in samples)
        {
            var reference=Vector3.Zero;
            for(int i=0;i<sample.Weights.Length;i++)if(sample.Weights[i]>0)
                reference+=(_skeleton.GetBoneGlobalPose(sample.Bones[i])*sample.InverseBinds[i]*sample.Vertex)*sample.Weights[i];
            reference=_skeleton.GlobalTransform*reference;
            error=Math.Max(error,reference.DistanceTo(EvaluateVertex(sample,false)));LastAuditSampleCount++;
        }
        return error;
    }

    public Clearance Evaluate(string region, Func<Vector3, float> supportHeight)
    {
        using var timing=Vivarium.Game.App.FrameProfiler.Measure("Guardian.SkinContact");
        region = CanonicalRegion(region);
        if (!Valid || !_samples.TryGetValue(region, out var samples) || samples.Count == 0)
            return new Clearance(region, false, 0, float.NaN, Vector3.Zero);
        try
        {
            if (!GodotObject.IsInstanceValid(_skeleton)) throw new InvalidOperationException("Cached skeleton freed; recreate sampler");
            // A fit may change bones between calls. Cache only within this evaluation,
            // never across fit iterations or frames; the sampled geometry is identical.
            BeginEvaluation();
            foreach (var mesh in _meshes) if (!GodotObject.IsInstanceValid(mesh)) throw new InvalidOperationException("Cached mesh freed; recreate sampler");
            float minimum = float.PositiveInfinity; var lowest = Vector3.Zero;
            foreach (var sample in samples)
            {
                var point = EvaluateVertex(sample, false); float clearance = point.Y - supportHeight(point);
                if (!point.IsFinite() || !float.IsFinite(clearance)) throw new InvalidOperationException("Nonfinite evaluated contact");
                if (clearance < minimum) { minimum = clearance; lowest = point; }
            }
            return new Clearance(region, true, samples.Count, minimum, lowest);
        }
        catch (Exception ex) { Valid = false; Diagnostic = ex.Message; return new Clearance(region, false, 0, float.NaN, Vector3.Zero); }
    }

    /// <summary>The lowest evaluated skin point of a region, measured by the
    /// same ground clearance the full evaluation uses. The clearance costs a
    /// support height per sample - terrain, root shelves and props - and the
    /// trunk avoidance pass only needs one point per region, so the support
    /// height is only asked for the samples that can still win: those within
    /// <see cref="LowestPointBand"/> of the lowest sampled point.</summary>
    public bool TryLowestPoint(string region, Func<Vector3, float> supportHeight, out Vector3 lowest)
    {
        lowest = Vector3.Zero;
        region = CanonicalRegion(region);
        if (!Valid || !_samples.TryGetValue(region, out var samples) || samples.Count == 0) return false;
        try
        {
            if (!GodotObject.IsInstanceValid(_skeleton)) throw new InvalidOperationException("Cached skeleton freed; recreate sampler");
            BeginEvaluation();
            float best = float.PositiveInfinity; var point = Vector3.Zero;
            foreach (var sample in samples)
            {
                var evaluated = EvaluateVertex(sample, false);
                if (!evaluated.IsFinite()) throw new InvalidOperationException("Nonfinite evaluated contact");
                if (evaluated.Y < best) { best = evaluated.Y; point = evaluated; }
            }
            // Only the samples that are close to the lowest one can have a
            // lower clearance: the ground under a body region does not vary by
            // more than the band.
            float limit = best + LowestPointBand;
            float minimum = float.PositiveInfinity;
            foreach (var sample in samples)
            {
                var evaluated = EvaluateVertex(sample, false);
                if (evaluated.Y > limit) continue;
                float clearance = evaluated.Y - supportHeight(evaluated);
                if (!float.IsFinite(clearance)) throw new InvalidOperationException("Nonfinite evaluated contact");
                if (clearance < minimum) { minimum = clearance; point = evaluated; }
            }
            lowest = point;
            if (LowestPointAuditBudget > 0)
            {
                // Audit: the clearance-based point the original pass chose.
                LowestPointAuditBudget--;
                float referenceMinimum = float.PositiveInfinity; var reference = Vector3.Zero;
                foreach (var sample in samples)
                {
                    var evaluated = EvaluateVertex(sample, false);
                    float clearance = evaluated.Y - supportHeight(evaluated);
                    if (clearance < referenceMinimum) { referenceMinimum = clearance; reference = evaluated; }
                }
                float error = point.DistanceTo(reference);
                MaximumLowestPointAuditError = Math.Max(MaximumLowestPointAuditError, error);
                if (error > 1e-4f) LowestPointAuditDifferences++;
                LowestPointAudits++;
            }
            return true;
        }
        catch (Exception ex) { Valid = false; Diagnostic = ex.Message; return false; }
    }

    /// <summary>How far above the lowest sampled point a sample may sit and
    /// still be able to have the lowest ground clearance.</summary>
    public const float LowestPointBand = .35f;
    /// <summary>Nearest actual nasal vertex to a world point. Vertex distance is an
    /// upper bound on triangle-surface distance; missing anatomy remains invalid.</summary>
    public Clearance NasalDistance(Vector3 target)
    {
        if (!Valid || _nasalPad.Count == 0 || !target.IsFinite())
            return new Clearance("NasalPad", false, 0, float.NaN, Vector3.Zero);
        BeginEvaluation();
        float minimum = float.PositiveInfinity; var nearest = Vector3.Zero;
        foreach (var sample in _nasalPad)
        {
            var point = EvaluateVertex(sample, false);
            if (!point.IsFinite()) throw new InvalidOperationException("Nonfinite nasal skin point");
            float distance = point.DistanceTo(target);
            if (distance < minimum) { minimum = distance; nearest = point; }
        }
        return new Clearance("NasalPad", true, _nasalPad.Count, minimum, nearest);
    }

    public Dictionary<int, float> TailGroundCorrections(Func<Vector3, float> supportHeight)
    {
        var result = new Dictionary<int, float>();
        if (!Valid || !_samples.TryGetValue("Tail", out var samples)) return result;
        bool audit = ChainCacheAuditBudget > 0;
        Dictionary<int, float>? reference = audit ? new() : null;
        if (audit) ChainCacheAuditBudget--;
        BeginEvaluation();
        foreach (var sample in samples)
        {
            var point = EvaluateVertex(sample, false);
            float lift = supportHeight(point) + .001f - point.Y;
            if (audit)
            {
                // Reuse the identical pose and support result. Auditing must
                // not repeat callbacks or change contact/ground-cache effects.
                ChainCacheAuditedSamples++;
                float originalDenominator = 0;
                for (int slot = 0; slot < sample.Weights.Length; slot++)
                {
                    bool original = _skeleton.GetBoneName(sample.Bones[slot]).ToString().StartsWith(_chainPrefix, StringComparison.Ordinal);
                    ChainCacheAuditedInfluences++;
                    if (original != sample.ChainInfluences[slot]) throw new InvalidOperationException("Guardian chain membership cache mismatch");
                    if (original) originalDenominator += sample.Weights[slot] * sample.Weights[slot];
                }
                if (BitConverter.SingleToInt32Bits(originalDenominator) != BitConverter.SingleToInt32Bits(sample.ChainDenominator))
                    throw new InvalidOperationException("Guardian chain denominator cache mismatch");
                if (lift > 0)
                {
                    ChainCachePenetratingSamples++;
                    if (originalDenominator >= .001f)
                        for (int slot = 0; slot < sample.Weights.Length; slot++)
                        {
                            int bone = sample.Bones[slot]; float weight = sample.Weights[slot];
                            if (weight <= 0 || !_skeleton.GetBoneName(bone).ToString().StartsWith(_chainPrefix, StringComparison.Ordinal)) continue;
                            reference![bone] = Math.Max(reference.GetValueOrDefault(bone), Math.Min(.08f, lift * weight / originalDenominator));
                        }
                }
            }
            if (lift <= 0) continue;
            float denominator = sample.ChainDenominator;
            if (denominator < .001f) continue;
            for (int slot = 0; slot < sample.Weights.Length; slot++)
            {
                int bone = sample.Bones[slot]; float weight = sample.Weights[slot];
                if (weight <= 0 || !sample.ChainInfluences[slot]) continue;
                result[bone] = Math.Max(result.GetValueOrDefault(bone), Math.Min(.08f, lift * weight / denominator));
            }
        }
        if (_continuousBody)
        {
            // Head and jaw are separate root children. Their rigid attachment
            // follows the first axial joint in the continuous-body modifier.
            int first = _skeleton.FindBone(_chainPrefix + "00");
            var head = Evaluate("Head", supportHeight);
            if (first >= 0 && head.Valid && head.Minimum < .001f)
            {
                result[first] = Math.Max(result.GetValueOrDefault(first), Math.Min(.08f, .001f - head.Minimum));
                if (audit) reference![first] = Math.Max(reference.GetValueOrDefault(first), Math.Min(.08f, .001f - head.Minimum));
            }
        }
        if (audit)
        {
            if (result.Count != reference!.Count) throw new InvalidOperationException("Guardian chain correction key count mismatch");
            foreach (var (bone, expected) in reference)
            {
                if (!result.TryGetValue(bone, out float actual)) throw new InvalidOperationException("Guardian chain correction key mismatch");
                MaximumChainCacheAuditError = Math.Max(MaximumChainCacheAuditError, Math.Abs(actual - expected));
                if (BitConverter.SingleToInt32Bits(actual) != BitConverter.SingleToInt32Bits(expected))
                    throw new InvalidOperationException("Guardian chain correction cache mismatch");
            }
            ChainCacheAudits++;
        }
        return result;
    }
    private static string CanonicalRegion(string name) => name switch
    { "FLFoot" or "FrontFoot_L" => "Hand_L", "FRFoot" or "FrontFoot_R" => "Hand_R", "HLFoot" or "HindFoot_L" => "Foot_L", "HRFoot" or "HindFoot_R" => "Foot_R", _ => name };

    private static void RequireTransform(Transform3D transform, string context)
    {
        float determinant = transform.Basis.Determinant();
        if (!transform.Origin.IsFinite() || !transform.Basis.X.IsFinite() || !transform.Basis.Y.IsFinite() ||
            !transform.Basis.Z.IsFinite() || !float.IsFinite(determinant) || determinant <= 1e-10f)
            throw new InvalidOperationException($"{context}: nonfinite/singular/reflected transform (det={determinant:G6})");
    }
}
