using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Game.App;
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
    private Vector3[] _iterationStart=Array.Empty<Vector3>(),_fixedPointReference=Array.Empty<Vector3>();
    public long ExactIterationsSkipped {get;private set;}
    public int FixedPointAuditBudget {get;set;}
    public int FixedPointAudits {get;private set;}
    public float MaximumFixedPointAuditError {get;private set;}
    private readonly Dictionary<(float X,float Z,bool BelowTrunk),float> _supportSamples=new();
    public int GroundCacheAuditBudget {get;set;}
    public int GroundCacheSamplesAudited {get;private set;}
    public float MaximumGroundCacheAuditError {get;private set;}
    private float[] _stationRadii=Array.Empty<float>(),_stationGroundRadii=Array.Empty<float>();
    private bool _constraintAuditConfigured;
    public int ConstraintCacheAuditBudget {get;set;}
    public int ConstraintRadiusAuditValues {get;private set;}
    public double MaximumConstraintCacheAuditError {get;private set;}
    private Vector3 _lastRoot;
    private bool _initialized;
    private float _scale = 1;
    // Cost accounting for the constraint solve. The three inner costs dominate the
    // modifier, so they are timed separately instead of guessing from the total.
    private double _vegetationMs, _treeMs, _groundMs;
    private int _solveIterations, _solveSteps, _vegetationQueries, _treeQueries, _groundQueries;
    public double VegetationMs => _vegetationMs;
    public double TreeMs => _treeMs;
    public double GroundMs => _groundMs;
    public int SolveIterations => _solveIterations;
    public int SolveSteps => _solveSteps;
    public int VegetationQueries => _vegetationQueries;
    public int TreeQueries => _treeQueries;
    public int GroundQueries => _groundQueries;
    private readonly float[] _iterationMove = new float[32];
    /// <summary>Largest joint movement observed per constraint iteration (metres).</summary>
    public IReadOnlyList<float> IterationMove => _iterationMove;
    // Convergence study: how many constraint iterations a step needs before the
    // joints stop moving by more than the tolerance.
    public float ConvergenceTolerance { get; set; } = 1e-4f;
    private bool _convergedThisStep;
    private readonly int[] _convergenceHistogram = new int[32];
    private long _convergenceSamples;
    public IReadOnlyList<int> ConvergenceHistogram => _convergenceHistogram;
    public long ConvergenceSamples => _convergenceSamples;
    private readonly struct BoneCapsule
    {
        public readonly int BoneA;
        public readonly int BoneB;
        public readonly float Radius;
        public BoneCapsule(int a, int b, float r) { BoneA = a; BoneB = b; Radius = r; }
    }
    private BoneCapsule[] _bodyCapsules = Array.Empty<BoneCapsule>();
    private bool _capsulesInitialized;
    public void ResetContact() { _initialized = false; _capsulesInitialized = false; }
    public override void _ProcessModificationWithDelta(double delta)
    {
        using var timing=Vivarium.Game.App.FrameProfiler.Measure("Guardian.Appendage");
        long phaseStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        _vegetationMs = 0; _treeMs = 0; _groundMs = 0;
        _solveIterations = 0; _solveSteps = 0; _vegetationQueries = 0; _treeQueries = 0; _groundQueries = 0;
        var skeleton = GetSkeleton();
        if (skeleton == null || World == null || Renderer == null) return;
        if (Renderer.IsClimbing) return;
        if (_bones.Length == 0)
        {
            var bones = new List<int>();
            for (int i = FirstTailIndex; i <= 32; i++) { int b = skeleton.FindBone(ChainPrefix + i.ToString(ChainIndexFormat)); if (b < 0) break; bones.Add(b); }
            _bones = bones.ToArray();
            _points = new Vector3[_bones.Length + 1]; _velocity = new Vector3[_points.Length];_iterationStart=new Vector3[_points.Length];_fixedPointReference=new Vector3[_points.Length];
        }
        if (_bones.Length < 2) return;
        _supportSamples.Clear();
        if(!_constraintAuditConfigured)
        {
            _constraintAuditConfigured=true;
            if(Array.IndexOf(Main.UserArgs,"--guardian-constraint-cache-audit")>=0)ConstraintCacheAuditBudget=128;
        }
        var transform = skeleton.GlobalTransform;
        _scale = transform.Basis.Scale.X;
        int stations=(_points.Length-1)*4+1;
        if(_stationRadii.Length<stations){_stationRadii=new float[stations];_stationGroundRadii=new float[stations];}
        for(int i=0;i<_points.Length;i++)
        {
            _stationRadii[i*4]=Radius(i,_points.Length);
            _stationGroundRadii[i*4]=GroundRadius(i,_points.Length);
        }
        // The vegetation index only has to reach as far as the widest contact
        // this tail uses, so it can search a much smaller area.
        float reach=0;
        for(int i=0;i<_stationRadii.Length;i++)reach=Math.Max(reach,_stationRadii[i]);
        if(Vegetation!=null)Vegetation.ContactReach=reach;
        for(int i=0;i<_points.Length-1;i++)for(int sample=1;sample<4;sample++)
        {
            float t=sample*.25f;
            _stationRadii[i*4+sample]=Radius(i+t,_points.Length);
            _stationGroundRadii[i*4+sample]=GroundRadius(i+t,_points.Length);
        }
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
        // Authored angles and station radii are constant throughout all contact
        // iterations of this call. Keep exactly the original arithmetic/order.
        var minimumChords=new float[lengths.Length];
        for(int i=1;i<lengths.Length;i++)
        {
            var a=(authoredGoals[i]-authoredGoals[i-1]).Normalized();
            var b=(authoredGoals[i+1]-authoredGoals[i]).Normalized();
            float restAngle=MathF.Acos(Math.Clamp(a.Dot(b),-1,1));
            float allowed=Math.Min(MathF.PI-.05f,restAngle+.60f);
            minimumChords[i]=MathF.Sqrt(lengths[i-1]*lengths[i-1]+lengths[i]*lengths[i]+2*lengths[i-1]*lengths[i]*MathF.Cos(allowed));
        }
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
        // A slow frame must not multiply the solve cost. The substep size stays at
        // the stable 1/60 s and at most two substeps run per frame, so the tail lags
        // a little instead of the frame time feeding back into itself. Before this
        // cap a 20 fps frame ran three substeps, which cost a third more again.
        int steps = Math.Clamp((int)Math.Ceiling(dt / (1f / 60)), 1, 2);
        float h = Math.Min(dt / steps, 1f / 60);
        _solveSteps = steps;
        FrameProfiler.Record("Guardian.Appendage.Setup", System.Diagnostics.Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds);
        phaseStarted = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int step = 0; step < steps; step++)
        {
            _points[0] = goals[0];
            // Sample the plants' bend response once per solver step. The bend a
            // plant takes follows the tail's path across frames, and the
            // per-iteration projection below keeps the tail itself out of the
            // stems, so the step's samples carry the same information as the
            // sixty-four per-step samples the solver used to take.
            for (int i = 1; i < _points.Length; i++)
                ResolveVegetation(_points[i], StationRadius(i*4,i,false));
            _convergedThisStep = false;
            for (int i = 1; i < _points.Length; i++)
            {
                // The pose remains the intent; inertia produces secondary follow-through.
                if (h == 0) { _points[i] = goals[i]; continue; }
                _velocity[i] += (goals[i] - _points[i]) * (85 * h);
                _velocity[i] *= MathF.Exp(-11 * h);
                _points[i] += _velocity[i] * h;
            }
            bool auditingFixedPoint=false;
            for (int iteration = 0; iteration < 32; iteration++)
            {
                _solveIterations++;
                _points[0] = goals[0];Array.Copy(_points,_iterationStart,_points.Length);
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
                    float minimumChord = minimumChords[i];
                    var chord = _points[i + 1] - _points[i - 1]; float length = chord.Length();
                    if (length >= minimumChord || length < 1e-6f) continue;
                    var correction = chord / length * ((minimumChord - length) * .85f);
                    if (i == 1) _points[i + 1] += correction;
                    else { _points[i - 1] -= correction * .5f; _points[i + 1] += correction * .5f; }
                }
                for (int i = 1; i < _points.Length; i++)
                {
                    float radius = StationRadius(i*4,i,false);
                    var point = ProjectVegetation(_points[i], radius);
                    if (!Renderer.ReviewSupportPlaneHeight.HasValue && Renderer.ReviewGroundSample == null && World.PilotTree != null)
                    {
                        var hit = TreePenetration(point, radius);
                        if (hit.Depth > 0) point += new Vector3((float)hit.Normal.X, (float)hit.Normal.Y, (float)hit.Normal.Z) * (float)hit.Depth;
                    }
                    point.Y = Math.Max(point.Y, Ground(point) + StationRadius(i*4,i,true));
                    _points[i] = point;
                }
                // Sample the capsule interiors as well as joints: a thin ridge
                // can otherwise pass through the middle of a long tail bone.
                for (int i = 0; i < lengths.Length; i++)
                    for (int sample = 1; sample < 4; sample++)
                    {
                        float t = sample * .25f; var point = _points[i].Lerp(_points[i + 1], t);
                        float radius = StationRadius(i*4+sample,i+t,false);
                        var projected = ProjectVegetation(point, radius);
                        if (!Renderer.ReviewSupportPlaneHeight.HasValue && Renderer.ReviewGroundSample == null && World.PilotTree != null)
                        {
                            var hit = TreePenetration(projected, radius);
                            if (hit.Depth > 0) projected += new Vector3((float)hit.Normal.X, (float)hit.Normal.Y, (float)hit.Normal.Z) * (float)hit.Depth;
                        }
                        projected.Y = Math.Max(projected.Y, Ground(projected) + StationRadius(i*4+sample,i+t,true));
                        var correction = projected - point;
                        // Project a segment sample through its barycentric
                        // endpoint weights. A pinned root has zero inverse mass.
                        float leftWeight = i > 0 ? 1 - t : 0;
                        float denominator = leftWeight * leftWeight + t * t;
                        if (i > 0) _points[i] += correction * (leftWeight / denominator);
                        _points[i + 1] += correction * (t / denominator);
                    }
                // This iteration is a deterministic map while goals, terrain and
                // plant bounds are unchanged inside the call. Once the joints
                // stop moving by more than the tolerance, further iterations
                // repeat the same projection: the chain has settled.
                bool fixedPoint=true;
                float worstMove=0;
                for(int i=0;i<_points.Length;i++)
                {
                    float move=(_points[i]-_iterationStart[i]).LengthSquared();
                    if(move>worstMove)worstMove=move;
                    if(_points[i]!=_iterationStart[i])fixedPoint=false;
                }
                float moveMetres=MathF.Sqrt(worstMove);
                if(moveMetres>_iterationMove[iteration])_iterationMove[iteration]=moveMetres;
                if(moveMetres<ConvergenceTolerance&&!_convergedThisStep){_convergedThisStep=true;_convergenceHistogram[iteration]++;_convergenceSamples++;}
                if(moveMetres<=ConvergenceTolerance)
                {
                    if(FixedPointAuditBudget>0)
                    {
                        FixedPointAuditBudget--;Array.Copy(_points,_fixedPointReference,_points.Length);auditingFixedPoint=true;
                    }
                    else{ExactIterationsSkipped+=31-iteration;break;}
                }
                if(fixedPoint&&!auditingFixedPoint)
                {
                    ExactIterationsSkipped+=31-iteration;break;
                }
            }
            if(auditingFixedPoint)
            {
                FixedPointAudits++;
                for(int i=0;i<_points.Length;i++)MaximumFixedPointAuditError=Math.Max(MaximumFixedPointAuditError,_points[i].DistanceTo(_fixedPointReference[i]));
            }
        }
        // Fit the actual lowest skin/armor samples as well as centerline
        FrameProfiler.Record("Guardian.Appendage.Constraints", System.Diagnostics.Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds);
        phaseStarted = System.Diagnostics.Stopwatch.GetTimestamp();
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
            // For fit > 0 the points are exactly where the previous fit's
            // post-lift pass left them; skip when already reconciled (<0.1 mm).
            bool reconciled = fit > 0;
            for (int i = 0; reconciled && i < lengths.Length; i++)
                if (Math.Abs(_points[i].DistanceTo(_points[i + 1]) - lengths[i]) > 1e-4f) reconciled = false;
            if (!reconciled)
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
        FrameProfiler.Record("Guardian.Appendage.SkinFit", System.Diagnostics.Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds);
        phaseStarted = System.Diagnostics.Stopwatch.GetTimestamp();
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
        // Connected trunk, head, limb, and tail capsules brush the same visible shrubs.
        if (Vegetation != null)
        {
            EnsureCapsules(skeleton);
            for (int i = 0; i < _bodyCapsules.Length; i++)
            {
                var cap = _bodyCapsules[i];
                var pA = transform * skeleton.GetBoneGlobalPose(cap.BoneA).Origin;
                var pB = transform * skeleton.GetBoneGlobalPose(cap.BoneB).Origin;
                Vegetation.ResolveCapsule(pA, pB, cap.Radius * _scale);
            }
            for (int i = 0; i < _points.Length - 1; i++)
            {
                float rA = StationRadius(i * 4, i, false);
                float rB = StationRadius((i + 1) * 4, i + 1, false);
                Vegetation.ResolveCapsule(_points[i], _points[i + 1], Math.Max(rA, rB));
            }
            if (_bodyCapsules.Length == 0)
            {
                foreach (string name in new[] { "Pelvis", "Spine1", "Chest", "Lumbar", "Neck", "Head", "Body", "Body0", "Body1", "Body2", "Body3" })
                { int bone = skeleton.FindBone(name); if (bone >= 0) Vegetation.Resolve(transform * skeleton.GetBoneGlobalPose(bone).Origin, (name == "Head" ? .075f : BodyBrushRadius) * _scale, false); }
            }
        }
        FrameProfiler.Record("Guardian.Appendage.Diagnostics", System.Diagnostics.Stopwatch.GetElapsedTime(phaseStarted).TotalMilliseconds);
        FrameProfiler.Record("Guardian.Appendage.Veg", _vegetationMs);
        FrameProfiler.Record("Guardian.Appendage.Tree", _treeMs);
        FrameProfiler.Record("Guardian.Appendage.Ground", _groundMs);
    }
    private void EnsureCapsules(Skeleton3D skeleton)
    {
        if (_capsulesInitialized) return;
        _capsulesInitialized = true;
        var list = new List<BoneCapsule>();

        // Spine / Torso chain
        int pelvis = skeleton.FindBone("Pelvis");
        int spine1 = skeleton.FindBone("Spine1");
        int lumbar = skeleton.FindBone("Lumbar");
        int chest = skeleton.FindBone("Chest");
        int neck = skeleton.FindBone("Neck");
        if (neck < 0) neck = skeleton.FindBone("Neck1");
        int head = skeleton.FindBone("Head");
        int snout = skeleton.FindBone("Snout");

        if (pelvis >= 0 && spine1 >= 0) list.Add(new BoneCapsule(pelvis, spine1, BodyBrushRadius));
        if (spine1 >= 0 && lumbar >= 0) list.Add(new BoneCapsule(spine1, lumbar, BodyBrushRadius));
        if (lumbar >= 0 && chest >= 0) list.Add(new BoneCapsule(lumbar, chest, BodyBrushRadius));
        else if (spine1 >= 0 && chest >= 0) list.Add(new BoneCapsule(spine1, chest, BodyBrushRadius));
        else if (pelvis >= 0 && chest >= 0) list.Add(new BoneCapsule(pelvis, chest, BodyBrushRadius));

        if (chest >= 0 && neck >= 0) list.Add(new BoneCapsule(chest, neck, BodyBrushRadius * 0.85f));
        if (neck >= 0 && head >= 0) list.Add(new BoneCapsule(neck, head, 0.08f));
        if (head >= 0 && snout >= 0) list.Add(new BoneCapsule(head, snout, 0.05f));

        // Segmented body chains (e.g. serpent / monitor Body0..BodyN)
        for (int i = 0; i < 32; i++)
        {
            int a = skeleton.FindBone("Body" + i);
            int b = skeleton.FindBone("Body" + (i + 1));
            if (a >= 0 && b >= 0) list.Add(new BoneCapsule(a, b, BodyBrushRadius));
            else if (a < 0) break;
        }
        int bodyMain = skeleton.FindBone("Body");
        int body0 = skeleton.FindBone("Body0");
        if (bodyMain >= 0 && body0 >= 0) list.Add(new BoneCapsule(bodyMain, body0, BodyBrushRadius));

        // Limbs: Front Left/Right, Hind Left/Right
        string[] sides = { "L", "R" };
        foreach (var s in sides)
        {
            // Front limbs
            int fu = skeleton.FindBone($"Front_Upper_{s}");
            if (fu < 0) fu = skeleton.FindBone($"FLeg_{s}");
            int fl = skeleton.FindBone($"Front_Lower_{s}");
            if (fl < 0) fl = skeleton.FindBone($"FShin_{s}");
            int ff = skeleton.FindBone($"Foot_Front_{s}");
            if (ff < 0) ff = skeleton.FindBone($"Hand_{s}");
            if (ff < 0) ff = skeleton.FindBone($"FFoot_{s}");

            if (fu >= 0 && fl >= 0) list.Add(new BoneCapsule(fu, fl, 0.07f));
            if (fl >= 0 && ff >= 0) list.Add(new BoneCapsule(fl, ff, 0.05f));

            // Hind limbs
            int hu = skeleton.FindBone($"Hind_Upper_{s}");
            if (hu < 0) hu = skeleton.FindBone($"HLeg_{s}");
            int hl = skeleton.FindBone($"Hind_Lower_{s}");
            if (hl < 0) hl = skeleton.FindBone($"HShin_{s}");
            int hf = skeleton.FindBone($"Foot_Hind_{s}");
            if (hf < 0) hf = skeleton.FindBone($"Foot_{s}");
            if (hf < 0) hf = skeleton.FindBone($"HFoot_{s}");

            if (hu >= 0 && hl >= 0) list.Add(new BoneCapsule(hu, hl, 0.07f));
            if (hl >= 0 && hf >= 0) list.Add(new BoneCapsule(hl, hf, 0.05f));
        }

        _bodyCapsules = list.ToArray();
    }

    private Vector3 ResolveVegetation(Vector3 point, float radius)
    {
        if (Vegetation == null) return point;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = Vegetation.Resolve(point, radius, true);
        _vegetationMs += System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        _vegetationQueries++;
        return result;
    }
    /// <summary>Projects a point out of the stems near it, without resampling the
    /// plants' bend targets. The solver runs this for every constraint
    /// iteration; the bend targets only need the tail's position along its
    /// path, so they are sampled once per solver step instead.</summary>
    private Vector3 ProjectVegetation(Vector3 point, float radius)
    {
        if (Vegetation == null) return point;
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = Vegetation.ProjectStem(point, radius);
        _vegetationMs += System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        _vegetationQueries++;
        return result;
    }
    private float Ground(Vector3 point)
    {
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
        // In production support depends on XZ and one trunk-roof branch. Terrain,
        // props and renderer mode cannot change inside this synchronous modifier call.
        // Review callbacks may be arbitrary functions of XYZ and deliberately bypass it.
        if(Renderer.ReviewGroundSample!=null)return Renderer.SupportHeight(point);
        bool below=World.PilotTree is {} tree&&point.Y<tree.BaseHeight+tree.Def.TrunkHeight-.05;
        var key=(point.X,point.Z,below);
        if(!_supportSamples.TryGetValue(key,out float height))
        {height=Renderer.SupportHeight(point);_supportSamples.Add(key,height);_groundQueries++;}
        else if(GroundCacheAuditBudget>0)
        {
            GroundCacheAuditBudget--;GroundCacheSamplesAudited++;
            MaximumGroundCacheAuditError=Math.Max(MaximumGroundCacheAuditError,Math.Abs(height-Renderer.SupportHeight(point)));
        }
        return height;
        }
        finally { _groundMs += System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds; }
    }
    private float StationRadius(int station,float originalIndex,bool ground)
    {
        float cached=ground?_stationGroundRadii[station]:_stationRadii[station];
        if(ConstraintCacheAuditBudget>0 && ConstraintRadiusAuditValues<128)
        {
            float reference=ground?GroundRadius(originalIndex,_points.Length):Radius(originalIndex,_points.Length);
            ConstraintRadiusAuditValues++;
            MaximumConstraintCacheAuditError=Math.Max(MaximumConstraintCacheAuditError,Math.Abs(cached-reference));
            if(BitConverter.SingleToInt32Bits(cached)!=BitConverter.SingleToInt32Bits(reference))
                throw new InvalidOperationException("Guardian constraint radius cache mismatch");
        }
        return cached;
    }
    private (Vec3 Normal,double Depth) TreePenetration(Vector3 point,float radius)
    {
        // The immutable root footprint index is cheaper than a per-call memo
        // table here: measured exact-query reuse was only about four percent.
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        var result = World.PilotTree!.Penetration(new Vec3(point.X,point.Y,point.Z),radius);
        _treeMs += System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds;
        _treeQueries++;
        return result;
    }
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
