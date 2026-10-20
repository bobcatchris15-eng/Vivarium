using System;
using System.Collections.Generic;
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
    private readonly bool _continuousBody;
    private readonly string _chainPrefix;
    private readonly Dictionary<string, List<Sample>> _samples = new();
    private readonly HashSet<MeshInstance3D> _meshes = new();
    private readonly List<Sample> _nasalPad = new();
    private static readonly string[] Regions = { "Hand_L", "Hand_R", "Foot_L", "Foot_R", "Body", "Tail", "Head" };
    public bool Valid { get; private set; }
    public string Diagnostic { get; private set; } = "Not initialized";
    public float MaximumRestReconstructionError { get; private set; }
    public int RestVerticesChecked { get; private set; }
    public int SourceVerticesScanned { get; private set; }

    private sealed class Sample
    {
        public Vector3 Vertex;
        public int[] Bones = Array.Empty<int>();
        public float[] Weights = Array.Empty<float>();
        // Godot Skin bind poses ARE inverse binds. Do not invert again.
        public Transform3D[] InverseBinds = Array.Empty<Transform3D>();
        public Vector3 RestWorld;
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
        foreach (string region in Regions) _samples[region] = new List<Sample>();
        try
        {
            if (!float.IsFinite(restToleranceMeters) || restToleranceMeters <= 0)
                throw new InvalidOperationException("Invalid rest reconstruction tolerance");
            _restWorld=skeleton.GlobalTransform;RequireTransform(_restWorld,"skeleton world");
            int bones=skeleton.GetBoneCount();_restPoses=new Transform3D[bones];_currentPoses=new Transform3D[bones];_restLoaded=new bool[bones];_poseStamp=new int[bones];
            var candidates = new Dictionary<string, List<Sample>>();
            foreach (string region in Regions) candidates[region] = new List<Sample>();
            int surfaceCount = 0;
            Visit(instance, mesh => ReadMesh(mesh, candidates, ref surfaceCount, restToleranceMeters));
            foreach (string region in Regions) _samples[region] = _continuousBody && region is "Body" or "Tail"
                ? SelectAxialSamples(candidates[region]) : SelectLowestBins(candidates[region]);
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
            Diagnostic = ex.Message;
            foreach (var list in _samples.Values) list.Clear();
            _nasalPad.Clear();
        }
    }

    private static void Visit(Node node, Action<MeshInstance3D> visitor)
    {
        if (node is MeshInstance3D mesh) visitor(mesh);
        foreach (Node child in node.GetChildren()) Visit(child, visitor);
    }

    private void ReadMesh(MeshInstance3D mesh, Dictionary<string, List<Sample>> candidates,
        ref int surfaceCount, float tolerance)
    {
        if (mesh.Mesh == null) return;
        var skin = mesh.Skin ?? mesh.GetSkinReference()?.GetSkin();
        if (skin == null) return; // Unskinned decoration is outside evaluated-skin scope.
        if (mesh.GetNodeOrNull<Skeleton3D>(mesh.Skeleton) != _skeleton)
            throw new InvalidOperationException($"{mesh.Name}: skin targets a different/unresolved skeleton");
        RequireTransform(mesh.GlobalTransform, $"{mesh.Name} world");
        int bindCount = skin.GetBindCount();
        var boneMap = new int[bindCount]; var inverseBinds = new Transform3D[bindCount];
        for (int bind = 0; bind < bindCount; bind++)
        {
            string name = skin.GetBindName(bind).ToString();
            int bone = name.Length > 0 ? _skeleton.FindBone(name) : skin.GetBindBone(bind);
            if (bone < 0 || bone >= _skeleton.GetBoneCount())
                throw new InvalidOperationException($"{mesh.Name}: unmapped skin bind {bind} ({name})");
            boneMap[bind] = bone; inverseBinds[bind] = skin.GetBindPose(bind);
            RequireTransform(inverseBinds[bind], $"{mesh.Name} inverse bind {bind}");
            RequireTransform(_skeleton.GetBoneGlobalRest(bone), $"{mesh.Name} bone rest {bone}");
        }
        bool used = false;
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
            for (int vertex = 0; vertex < vertices.Length; vertex++)
            {
                if (!vertices[vertex].IsFinite()) throw new InvalidOperationException("Nonfinite source vertex");
                var sample = new Sample { Vertex = vertices[vertex], Bones = new int[influences],
                    Weights = new float[influences], InverseBinds = new Transform3D[influences] };
                float sum = 0; var regionWeights = new float[Regions.Length];
                for (int slot = 0; slot < influences; slot++)
                {
                    int offset = vertex * influences + slot; float weight = weights[offset]; int bind = binds[offset];
                    if (!float.IsFinite(weight) || weight < 0 || weight > 1) throw new InvalidOperationException("Invalid skin weight");
                    if (weight == 0) continue; // Zero slots can contain otherwise irrelevant padding indices.
                    if (bind < 0 || bind >= bindCount) throw new InvalidOperationException("Skin bind index out of range");
                    int bone = boneMap[bind]; sample.Bones[slot] = bone; sample.Weights[slot] = weight;
                    sample.InverseBinds[slot] = inverseBinds[bind]; sum += weight;
                    string name = _skeleton.GetBoneName(bone).ToString();
                    string canonical = CanonicalRegion(name);
                    for (int region = 0; region < 4; region++) if (canonical == Regions[region]) regionWeights[region] += weight;
                    if (name is "Pelvis" or "Spine1" or "Chest" or "Lumbar" || name.StartsWith("Body", StringComparison.Ordinal)) regionWeights[4] += weight;
                    if (name.StartsWith(_chainPrefix, StringComparison.Ordinal)) regionWeights[5] += weight;
                    if (name is "Head" or "Jaw") regionWeights[6] += weight;
                }
                if (Math.Abs(sum - 1) > .001f) throw new InvalidOperationException($"{mesh.Name}: weights sum {sum:G6}, expected1");
                sample.RestWorld = EvaluateVertex(sample, true);
                var expected = mesh.GlobalTransform * sample.Vertex;
                float error = sample.RestWorld.DistanceTo(expected);
                RestVerticesChecked++; MaximumRestReconstructionError = Math.Max(MaximumRestReconstructionError, error);
                if (!float.IsFinite(error) || error > tolerance)
                    throw new InvalidOperationException($"{mesh.Name}: rest reconstruction mismatch {error:G6}m at vertex {vertex}; bind/local space invalid");
                // Keep the complete small named surface, including Snout weights.
                // Ground-clearance bins deliberately omit it and cannot certify food contact.
                if (mesh.Name.ToString().Equals("coati_nasal_pad", StringComparison.OrdinalIgnoreCase)
                    || material.Equals("CoatiWetNasalPad", StringComparison.OrdinalIgnoreCase))
                {
                    if (_nasalPad.Count >= 4096) throw new InvalidOperationException("Nasal-pad diagnostic budget exceeded");
                    _nasalPad.Add(sample); used = true;
                }
                for (int region = 0; region < Regions.Length; region++)
                    if (regionWeights[region] >= WeightThreshold) { candidates[Regions[region]].Add(sample); used = true; }
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
    private static List<Sample> SelectLowestBins(List<Sample> candidates)
    {
        var result = new List<Sample>(); if (candidates.Count == 0) return result;
        float minX = float.PositiveInfinity, minZ = minX, maxX = float.NegativeInfinity, maxZ = maxX;
        foreach (var s in candidates)
        { minX = Math.Min(minX, s.RestWorld.X); maxX = Math.Max(maxX, s.RestWorld.X); minZ = Math.Min(minZ, s.RestWorld.Z); maxZ = Math.Max(maxZ, s.RestWorld.Z); }
        var bins = new List<Sample>[64];
        foreach (var s in candidates)
        {
            int x = Math.Clamp((int)((s.RestWorld.X - minX) / Math.Max(.00001f, maxX - minX) * 8), 0, 7);
            int z = Math.Clamp((int)((s.RestWorld.Z - minZ) / Math.Max(.00001f, maxZ - minZ) * 8), 0, 7);
            var bin = bins[z * 8 + x] ??= new List<Sample>(2);
            int at = 0; while (at < bin.Count && bin[at].RestWorld.Y <= s.RestWorld.Y) at++;
            if (at < 2) { bin.Insert(at, s); if (bin.Count > 2) bin.RemoveAt(2); }
        }
        foreach (var bin in bins) if (bin != null) result.AddRange(bin);
        return result;
    }

    private List<Sample> SelectAxialSamples(List<Sample> candidates)
    {
        // Coiled turns can share XZ bins. Keep the two lowest vertices on each
        // side of every axial joint instead, bounded to33*4=132 samples. This
        // follows all sections of a continuous body even when turns overlap.
        var bins = new Dictionary<(int bone, bool right), List<Sample>>();
        foreach (var sample in candidates)
        {
            int bone = -1; float strongest = 0;
            for (int slot = 0; slot < sample.Weights.Length; slot++)
                if (sample.Weights[slot] > strongest && _skeleton.GetBoneName(sample.Bones[slot]).ToString().StartsWith(_chainPrefix, StringComparison.Ordinal))
                { strongest = sample.Weights[slot]; bone = sample.Bones[slot]; }
            if (bone < 0) continue;
            var local = _skeleton.GetBoneGlobalRest(bone).AffineInverse() * _skeleton.GlobalTransform.AffineInverse() * sample.RestWorld;
            var key = (bone, local.X >= 0);
            if (!bins.TryGetValue(key, out var bin)) bins[key] = bin = new List<Sample>(2);
            int at = 0; while (at < bin.Count && bin[at].RestWorld.Y <= sample.RestWorld.Y) at++;
            if (at < 2) { bin.Insert(at, sample); if (bin.Count > 2) bin.RemoveAt(2); }
        }
        var result = new List<Sample>(); foreach (var bin in bins.Values) result.AddRange(bin);
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
        BeginEvaluation();
        foreach (var sample in samples)
        {
            var point = EvaluateVertex(sample, false);
            float lift = supportHeight(point) + .001f - point.Y;
            if (lift <= 0) continue;
            float denominator = 0;
            for (int slot = 0; slot < sample.Weights.Length; slot++)
                if (_skeleton.GetBoneName(sample.Bones[slot]).ToString().StartsWith(_chainPrefix, StringComparison.Ordinal))
                    denominator += sample.Weights[slot] * sample.Weights[slot];
            if (denominator < .001f) continue;
            for (int slot = 0; slot < sample.Weights.Length; slot++)
            {
                int bone = sample.Bones[slot]; float weight = sample.Weights[slot];
                if (weight <= 0 || !_skeleton.GetBoneName(bone).ToString().StartsWith(_chainPrefix, StringComparison.Ordinal)) continue;
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
                result[first] = Math.Max(result.GetValueOrDefault(first), Math.Min(.08f, .001f - head.Minimum));
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
