using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>Animation-following tail with world-space inertia and contact.
/// Runs after AnimationPlayer, so contact never accumulates into the authored pose.</summary>
public partial class GuardianAppendageModifier : SkeletonModifier3D
{
    public VivariumWorld World { get; set; } = null!;
    public GuardianRenderer Renderer { get; set; } = null!;
    public GuardianVegetationContact? Vegetation { get; set; }
    public float RootRadius { get; set; } = .058f;
    public float TipRadius { get; set; } = .009f;
    public float TipSegmentLength { get; set; } = .1356466f;
    public int FirstTailIndex { get; set; } = 1;
    public string ChainPrefix { get; set; } = "Tail";
    public string ChainIndexFormat { get; set; } = "D";
    public bool ContinuousGroundedBody { get; set; }
    public float[] LateralRadii { get; set; } = Array.Empty<float>();
    public float BodyBrushRadius { get; set; } = .12f;
    public float[] GroundRadii { get; set; } = Array.Empty<float>();
    public float AttachmentGroundRadius => GroundRadii.Length > 0 ? GroundRadii[0] : RootRadius;
    public float MaximumLengthError { get; private set; }
    public float MaximumGroundPenetration { get; private set; }
    public float MaximumContactDisplacement { get; private set; }
    public Vector3[] ContactPoints { get; private set; } = Array.Empty<Vector3>();
    public float MaximumSkinTailPenetration { get; private set; }
    public int SkinTailSampleCount { get; private set; }
    public float MaximumBendDeviation { get; private set; }
    public float ContinuousBodyClearance { get; private set; }
    public float ContinuousHeadClearance { get; private set; }
    public int ContinuousBodySampleCount { get; private set; }
    public int ContinuousHeadSampleCount { get; private set; }
    private int[] _bones = Array.Empty<int>();
    private Vector3[] _points = Array.Empty<Vector3>(), _velocity = Array.Empty<Vector3>();
    private Vector3 _lastRoot;
    private bool _initialized;
    private float _scale = 1;
    public void ResetContact() => _initialized = false;
    public override void _ProcessModificationWithDelta(double delta)
    {
        var skeleton = GetSkeleton();
        if (skeleton == null || World == null || Renderer == null) return;
        if (_bones.Length == 0)
        {
            var bones = new List<int>();
            for (int i = FirstTailIndex; i <= 32; i++) { int b = skeleton.FindBone(ChainPrefix + i.ToString(ChainIndexFormat)); if (b < 0) break; bones.Add(b); }
            _bones = bones.ToArray();
            _points = new Vector3[_bones.Length + 1]; _velocity = new Vector3[_points.Length];
        }
        if (_bones.Length < 2) return;
        var transform = skeleton.GlobalTransform;
        _scale = transform.Basis.Scale.X;
        var inverse = transform.AffineInverse();
        var poses = new Transform3D[_bones.Length];
        var goals = new Vector3[_points.Length];
        for (int i = 0; i < _bones.Length; i++) { poses[i] = skeleton.GetBoneGlobalPose(_bones[i]); goals[i] = transform * poses[i].Origin; }
        // Leaf joints have no child head to sample. Use the authored bone end,
        // rather than extrapolating the preceding segment across a tail hook.
        goals[^1] = goals[^2] + (transform.Basis * poses[^1].Basis.Y).Normalized() * TipSegmentLength * _scale;
        var lengths = new float[_bones.Length];
        for (int i = 0; i < lengths.Length; i++) lengths[i] = Math.Max(.001f, goals[i].DistanceTo(goals[i + 1]));
        var authoredGoals = (Vector3[])goals.Clone();
        if (ContinuousGroundedBody)
        {
            var bodyUp = Renderer.GlobalBasis.Y.Normalized();
            for (int i = 0; i < goals.Length; i++)
            {
                var rest = skeleton.GetBoneGlobalRest(_bones[Math.Min(i, _bones.Length - 1)]);
                var restPoint = transform * rest.Origin;
                if (i == _bones.Length)
                    restPoint += (transform.Basis * rest.Basis.Y).Normalized() * TipSegmentLength * _scale;
                // Preserve deliberate neck lifts and body arches above the
                // neutral profile. Ground adaptation must not erase that motion.
                float authoredLift = Math.Max(0, (authoredGoals[i] - restPoint).Dot(bodyUp)) * bodyUp.Y;
                goals[i].Y = Ground(goals[i]) + GroundRadius(i, goals.Length) + .001f + authoredLift;
            }
        }
        var attachedHead = skeleton.FindBone("Head"); var attachedJaw = skeleton.FindBone("Jaw");
        var headPose = attachedHead >= 0 ? skeleton.GetBoneGlobalPose(attachedHead) : Transform3D.Identity;
        var jawPose = attachedJaw >= 0 ? skeleton.GetBoneGlobalPose(attachedJaw) : Transform3D.Identity;
        if (!_initialized || goals[0].DistanceSquaredTo(_lastRoot) > .25f)
        { Array.Copy(goals, _points, goals.Length); Array.Clear(_velocity); _initialized = true; }
        _lastRoot = goals[0];
        float dt = World.Clock.Paused ? 0 : (float)Math.Clamp(delta * World.Clock.SpeedMultiplier, 0, .08);
        int steps = Math.Max(1, (int)Math.Ceiling(dt / (1f / 60))); float h = dt / steps;
        for (int step = 0; step < steps; step++)
        {
            _points[0] = goals[0];
            for (int i = 1; i < _points.Length; i++)
            {
                // The pose remains the intent; inertia produces secondary follow-through.
                if (h == 0) { _points[i] = goals[i]; continue; }
                _velocity[i] += (goals[i] - _points[i]) * (85 * h);
                _velocity[i] *= MathF.Exp(-11 * h);
                _points[i] += _velocity[i] * h;
            }
            for (int iteration = 0; iteration < 32; iteration++)
            {
                _points[0] = goals[0];
                for (int i = 0; i < lengths.Length; i++)
                {
                    var d = _points[i + 1] - _points[i]; float length = d.Length();
                    if (length < 1e-6f) d = (goals[i + 1] - goals[i]).Normalized(); else d /= length;
                    var correction = d * (length - lengths[i]);
                    if (i == 0) _points[i + 1] -= correction;
                    else { _points[i] += correction * .5f; _points[i + 1] -= correction * .5f; }
                }
                // A distance-only chain can fold sharply while retaining its
                // length. Preserve compliant curvature relative to the authored
                // centerline, allowing contact bends rather than sharp hinges.
                for (int i = 1; i < lengths.Length; i++)
                {
                    var a = (authoredGoals[i] - authoredGoals[i - 1]).Normalized();
                    var b = (authoredGoals[i + 1] - authoredGoals[i]).Normalized();
                    float restAngle = MathF.Acos(Math.Clamp(a.Dot(b), -1, 1));
                    float allowed = Math.Min(MathF.PI - .05f, restAngle + .60f);
                    float minimumChord = MathF.Sqrt(lengths[i - 1] * lengths[i - 1] + lengths[i] * lengths[i] +
                        2 * lengths[i - 1] * lengths[i] * MathF.Cos(allowed));
                    var chord = _points[i + 1] - _points[i - 1]; float length = chord.Length();
                    if (length >= minimumChord || length < 1e-6f) continue;
                    var correction = chord / length * ((minimumChord - length) * .85f);
                    if (i == 1) _points[i + 1] += correction;
                    else { _points[i - 1] -= correction * .5f; _points[i + 1] += correction * .5f; }
                }
                for (int i = 1; i < _points.Length; i++)
                {
                    float radius = Radius(i, _points.Length);
                    var point = Vegetation?.Resolve(_points[i], radius, true) ?? _points[i];
                    if (!Renderer.ReviewSupportPlaneHeight.HasValue && Renderer.ReviewGroundSample == null && World.PilotTree != null)
                    {
                        var hit = World.PilotTree.Penetration(new Vec3(point.X, point.Y, point.Z), radius);
                        if (hit.Depth > 0) point += new Vector3((float)hit.Normal.X, (float)hit.Normal.Y, (float)hit.Normal.Z) * (float)hit.Depth;
                    }
                    point.Y = Math.Max(point.Y, Ground(point) + GroundRadius(i, _points.Length));
                    _points[i] = point;
                }
                // Sample the capsule interiors as well as joints: a thin ridge
                // can otherwise pass through the middle of a long tail bone.
                for (int i = 0; i < lengths.Length; i++)
                    for (int sample = 1; sample < 4; sample++)
                    {
                        float t = sample * .25f; var point = _points[i].Lerp(_points[i + 1], t);
                        float radius = Radius(i + t, _points.Length);
                        var projected = Vegetation?.Resolve(point, radius, true) ?? point;
                        if (!Renderer.ReviewSupportPlaneHeight.HasValue && Renderer.ReviewGroundSample == null && World.PilotTree != null)
                        {
                            var hit = World.PilotTree.Penetration(new Vec3(projected.X, projected.Y, projected.Z), radius);
                            if (hit.Depth > 0) projected += new Vector3((float)hit.Normal.X, (float)hit.Normal.Y, (float)hit.Normal.Z) * (float)hit.Depth;
                        }
                        projected.Y = Math.Max(projected.Y, Ground(projected) + GroundRadius(i + t, _points.Length));
                        var correction = projected - point;
                        // Project a segment sample through its barycentric
                        // endpoint weights. A pinned root has zero inverse mass.
                        float leftWeight = i > 0 ? 1 - t : 0;
                        float denominator = leftWeight * leftWeight + t * t;
                        if (i > 0) _points[i] += correction * (leftWeight / denominator);
                        _points[i + 1] += correction * (t / denominator);
                    }
            }
        }
        // Fit the actual lowest skin/armor samples as well as centerline
        // capsules. Rigid plate tips and blended skin can extend beyond those
        // proxies on a tight bend. Tail-root motion blends through its skin
        // attachment instead of raising every foot to clear a distal plate.
        void ApplyTailPose()
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                var from = (authoredGoals[i + 1] - authoredGoals[i]).Normalized();
                var to = (_points[i + 1] - _points[i]).Normalized();
                var axis = from.Cross(to);
                var turn = axis.LengthSquared() < 1e-10f ? Basis.Identity : new Basis(axis.Normalized(), MathF.Acos(Math.Clamp(from.Dot(to), -1, 1)));
                var pose = transform * poses[i]; pose.Origin = _points[i]; pose.Basis = turn * pose.Basis;
                skeleton.SetBoneGlobalPose(_bones[i], inverse * pose);
            }
            if (ContinuousGroundedBody)
            {
                var attachmentDelta = skeleton.GetBoneGlobalPose(_bones[0]) * poses[0].AffineInverse();
                if (attachedHead >= 0) skeleton.SetBoneGlobalPose(attachedHead, attachmentDelta * headPose);
                if (attachedJaw >= 0) skeleton.SetBoneGlobalPose(attachedJaw, attachmentDelta * jawPose);
            }
        }
        for (int fit = 0; fit < 8; fit++)
        {
            // Collision projection is the last operation in the main solve.
            // Reconcile length even when no skin sample needs a lift; otherwise
            // an obstacle can leave a stretched segment with zero penetration.
            for (int iteration = 0; iteration < 24; iteration++)
                for (int i = 0; i < lengths.Length; i++)
                {
                    var d = _points[i + 1] - _points[i]; float length = d.Length();
                    if (length < 1e-6f) continue;
                    var correction = d / length * (length - lengths[i]);
                    if (i == 0) _points[i + 1] -= correction;
                    else { _points[i] += correction * .5f; _points[i + 1] -= correction * .5f; }
                }
            ApplyTailPose();
            var corrections = Renderer.SkinContact?.TailGroundCorrections(Ground);
            if (corrections == null || corrections.Count == 0) break;
            for (int i = 0; i < _bones.Length; i++)
                if (corrections.TryGetValue(_bones[i], out float lift))
                {
                    _points[i].Y += lift;
                    if (i == _bones.Length - 1) _points[i + 1].Y += lift;
                    _velocity[i].Y = Math.Max(0, _velocity[i].Y);
                }
            // Preserve segment lengths around the newly supported attachment.
            for (int iteration = 0; iteration < 24; iteration++)
                for (int i = 0; i < lengths.Length; i++)
                {
                    var d = _points[i + 1] - _points[i]; float length = d.Length();
                    if (length < 1e-6f) continue;
                    var correction = d / length * (length - lengths[i]);
                    if (i == 0) _points[i + 1] -= correction;
                    else { _points[i] += correction * .5f; _points[i + 1] -= correction * .5f; }
                }
        }
        // The last fit can exhaust its budget after changing points. Always
        // apply those final points before evaluating the rendered skin.
        ApplyTailPose();
        MaximumLengthError = 0; MaximumGroundPenetration = 0; MaximumBendDeviation = 0;
        MaximumContactDisplacement = 0;
        for (int i = 0; i < _bones.Length; i++)
        {
            var source = (authoredGoals[i + 1] - authoredGoals[i]).Normalized();
            var destination = (_points[i + 1] - _points[i]).Normalized();
            var cross = source.Cross(destination); float dot = Math.Clamp(source.Dot(destination), -1, 1);
            var rotation = cross.LengthSquared() < 1e-10f ? Basis.Identity : new Basis(cross.Normalized(), MathF.Acos(dot));
            var worldPose = transform * poses[i]; worldPose.Origin = _points[i]; worldPose.Basis = rotation * worldPose.Basis;
            skeleton.SetBoneGlobalPose(_bones[i], inverse * worldPose);
            MaximumLengthError = Math.Max(MaximumLengthError, Math.Abs(_points[i].DistanceTo(_points[i + 1]) - lengths[i]));
            MaximumContactDisplacement = Math.Max(MaximumContactDisplacement, _points[i].DistanceTo(authoredGoals[i]));
            for (int sample = 0; sample <= 4; sample++)
            {
                float t = sample * .25f; var p = _points[i].Lerp(_points[i + 1], t);
                MaximumGroundPenetration = Math.Max(MaximumGroundPenetration, Ground(p) + GroundRadius(i + t, _points.Length) - p.Y);
            }
        }
        ContactPoints = (Vector3[])_points.Clone();
        if (ContinuousGroundedBody)
        {
            var attachmentDelta = skeleton.GetBoneGlobalPose(_bones[0]) * poses[0].AffineInverse();
            if (attachedHead >= 0) skeleton.SetBoneGlobalPose(attachedHead, attachmentDelta * headPose);
            if (attachedJaw >= 0) skeleton.SetBoneGlobalPose(attachedJaw, attachmentDelta * jawPose);
        }
        for (int i = 1; i < _bones.Length; i++)
        {
            float actual = MathF.Acos(Math.Clamp((_points[i] - _points[i - 1]).Normalized().Dot((_points[i + 1] - _points[i]).Normalized()), -1, 1));
            float authored = MathF.Acos(Math.Clamp((authoredGoals[i] - authoredGoals[i - 1]).Normalized().Dot((authoredGoals[i + 1] - authoredGoals[i]).Normalized()), -1, 1));
            MaximumBendDeviation = Math.Max(MaximumBendDeviation, Math.Abs(actual - authored));
        }
        var tailSkin = Renderer.SkinContact?.Evaluate("Tail", Ground);
        MaximumSkinTailPenetration = tailSkin.HasValue && tailSkin.Value.Valid ? Math.Max(0, -tailSkin.Value.Minimum) : 0;
        SkinTailSampleCount = tailSkin.HasValue && tailSkin.Value.Valid ? tailSkin.Value.SampleCount : 0;
        if (ContinuousGroundedBody)
        {
            // Godot discards modified bone poses after applying them to the
            // skin. Cache diagnostics here, while the rendered pose is active.
            var bodySkin = Renderer.SkinContact?.Evaluate("Body", Ground);
            var headSkin = Renderer.SkinContact?.Evaluate("Head", Ground);
            ContinuousBodySampleCount = bodySkin.HasValue && bodySkin.Value.Valid ? bodySkin.Value.SampleCount : 0;
            ContinuousHeadSampleCount = headSkin.HasValue && headSkin.Value.Valid ? headSkin.Value.SampleCount : 0;
            ContinuousBodyClearance = ContinuousBodySampleCount > 0 ? bodySkin!.Value.Minimum : 0;
            ContinuousHeadClearance = ContinuousHeadSampleCount > 0 ? headSkin!.Value.Minimum : 0;
        }
        // Connected trunk and head volumes brush the same visible shrubs.
        foreach (string name in new[] { "Pelvis", "Spine1", "Chest", "Lumbar", "Neck", "Head", "Body", "Body0", "Body1", "Body2", "Body3" })
        { int bone = skeleton.FindBone(name); if (bone >= 0) Vegetation?.Resolve(transform * skeleton.GetBoneGlobalPose(bone).Origin, (name == "Head" ? .075f : BodyBrushRadius) * _scale, false); }
    }
    private float Ground(Vector3 point) => Renderer.SupportHeight(point);
    private float Radius(float index, int count) => LateralRadii.Length > 0 ? ProfileRadius(LateralRadii, index, count)
        : Mathf.Lerp(RootRadius, TipRadius, Math.Clamp(index / (count - 1), 0, 1)) * _scale;
    private float ProfileRadius(float[] profile, float index, int count)
    {
        float station = Math.Clamp(index / (count - 1), 0, 1) * (profile.Length - 1);
        int first = (int)station, second = Math.Min(first + 1, profile.Length - 1);
        return Mathf.Lerp(profile[first], profile[second], station - first) * _scale;
    }
    private float GroundRadius(float index, int count)
    {
        if (GroundRadii.Length == 0) return Radius(index, count);
        return ProfileRadius(GroundRadii, index, count);
    }
}
