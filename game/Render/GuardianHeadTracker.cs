using System;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Guardians;

namespace Vivarium.Game.Render;

/// <summary>
/// Procedural head/neck tracking modifier inheriting from SkeletonModifier3D (Node3D).
/// Smoothly rotates head and neck bones toward interest points during Investigate, Alert,
/// Explore, and idle behaviors with a 45-degree angle clamp, blending out smoothly during
/// sleep or rapid movement.
/// </summary>
public partial class GuardianHeadTracker : SkeletonModifier3D
{
    private const float MaxYaw = 0.60f; // ~34 degrees
    private const float MaxPitch = 0.35f; // ~20 degrees

    private int _headBone = -1;
    private int _neckBone = -1;
    private int _jawBone = -1;
    private bool _jawIsSiblingOfHead;
    private bool _initialized;

    private float _blendWeight;
    private float _currentYaw;
    private float _currentPitch;

    public Vector3? TargetLookPoint { get; set; }
    public GuardianActivity Activity { get; set; } = GuardianActivity.Rest;
    public bool IsMovingRapidly { get; set; }

    public float CurrentYaw => _currentYaw;
    public float CurrentPitch => _currentPitch;
    public float BlendWeight => _blendWeight;

    public void SetTarget(Vector3? targetLookPoint, GuardianActivity activity, bool isMovingRapidly = false)
    {
        TargetLookPoint = targetLookPoint;
        Activity = activity;
        IsMovingRapidly = isMovingRapidly;
    }

    public void Reset()
    {
        _initialized = false;
        _jawBone = -1;
        _jawIsSiblingOfHead = false;
        _blendWeight = 0;
        _currentYaw = 0;
        _currentPitch = 0;
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        using var timing = FrameProfiler.Measure("Guardian.HeadTracker");
        var skeleton = GetSkeleton();
        if (skeleton == null) return;

        if (!_initialized)
        {
            _headBone = skeleton.FindBone("Head");
            _neckBone = skeleton.FindBone("Neck");
            if (_neckBone < 0) _neckBone = skeleton.FindBone("Neck1");
            _jawBone = skeleton.FindBone("Jaw");
            _jawIsSiblingOfHead = _headBone >= 0 && _jawBone >= 0 && skeleton.GetBoneParent(_jawBone) != _headBone;
            _initialized = true;
        }

        if (_headBone < 0 || _jawIsSiblingOfHead) return;

        // Blends out when sleeping or walking rapidly
        bool canTrack = !IsMovingRapidly
            && Activity != GuardianActivity.Sleep
            && Activity != GuardianActivity.Retreat;

        float targetWeight = (canTrack && TargetLookPoint.HasValue) ? 1.0f : 0.0f;
        _blendWeight = Mathf.MoveToward(_blendWeight, targetWeight, (float)delta * 3.5f);

        if (_blendWeight <= 1e-4f)
        {
            _currentYaw = Mathf.Lerp(_currentYaw, 0f, (float)delta * 6.0f);
            _currentPitch = Mathf.Lerp(_currentPitch, 0f, (float)delta * 6.0f);
            return;
        }

        Transform3D skelTransform = skeleton.GlobalTransform;
        Transform3D headGlobalPose = skelTransform * skeleton.GetBoneGlobalPose(_headBone);
        Vector3 headOrigin = headGlobalPose.Origin;

        // A target can vanish while blend-out still has nonzero weight. The
        // skeleton modifier is invoked every render frame, including that
        // transition; a nullable .Value here previously threw repeatedly.
        // Keep angular easing but do not demand a target during release.
        if (TargetLookPoint is not Vector3 lookPoint)
        {
            _currentYaw = Mathf.MoveToward(_currentYaw, 0f, (float)delta * 6f);
            _currentPitch = Mathf.MoveToward(_currentPitch, 0f, (float)delta * 6f);
            return;
        }

        if (Activity is not (GuardianActivity.Feed or GuardianActivity.Drink or GuardianActivity.Dig))
        {
            lookPoint.Y += 0.28f * skelTransform.Basis.Scale.X;
        }

        Vector3 toTarget = lookPoint - headOrigin;
        if (toTarget.LengthSquared() < 1e-4f) return;
        Vector3 dir = toTarget.Normalized();

        Vector3 bodyRight = skelTransform.Basis.X.Normalized();
        Vector3 bodyForward = -skelTransform.Basis.Y.Normalized();
        Vector3 bodyUp = skelTransform.Basis.Z.Normalized();

        float forwardDot = dir.Dot(bodyForward);
        float rightDot = dir.Dot(bodyRight);
        float upDot = dir.Dot(bodyUp);

        float targetYaw = MathF.Atan2(rightDot, Math.Max(0.01f, forwardDot));
        float targetPitch = MathF.Asin(Math.Clamp(upDot, -1f, 1f));

        // Natural biological angle clamp
        targetYaw = Math.Clamp(targetYaw, -MaxYaw, MaxYaw);
        targetPitch = Math.Clamp(targetPitch, -MaxPitch, MaxPitch);

        _currentYaw = Mathf.Lerp(_currentYaw, targetYaw, (float)delta * 6.0f);
        _currentPitch = Mathf.Lerp(_currentPitch, targetPitch, (float)delta * 6.0f);

        float effYaw = _currentYaw * _blendWeight;
        float effPitch = _currentPitch * _blendWeight;

        Transform3D inverse = skelTransform.AffineInverse();

        if (_neckBone >= 0)
        {
            float neckYaw = effYaw * 0.35f;
            float neckPitch = effPitch * 0.35f;
            float headYaw = effYaw * 0.65f;
            float headPitch = effPitch * 0.65f;

            Basis neckRot = new Basis(bodyUp, -neckYaw) * new Basis(bodyRight, -neckPitch);
            Basis headRot = new Basis(bodyUp, -headYaw) * new Basis(bodyRight, -headPitch);

            Transform3D neckGlobal = skelTransform * skeleton.GetBoneGlobalPose(_neckBone);
            Transform3D newNeckGlobal = new Transform3D(neckRot * neckGlobal.Basis, neckGlobal.Origin);
            skeleton.SetBoneGlobalPose(_neckBone, inverse * newNeckGlobal);

            headGlobalPose = skelTransform * skeleton.GetBoneGlobalPose(_headBone);
            Transform3D newHeadGlobal = new Transform3D(headRot * headGlobalPose.Basis, headGlobalPose.Origin);
            skeleton.SetBoneGlobalPose(_headBone, inverse * newHeadGlobal);

            if (_jawIsSiblingOfHead)
            {
                Transform3D jawGlobal = skelTransform * skeleton.GetBoneGlobalPose(_jawBone);
                Vector3 jawOffset = jawGlobal.Origin - headGlobalPose.Origin;
                Vector3 newJawOrigin = headGlobalPose.Origin + headRot * jawOffset;
                Basis newJawBasis = headRot * jawGlobal.Basis;
                skeleton.SetBoneGlobalPose(_jawBone, inverse * new Transform3D(newJawBasis, newJawOrigin));
            }
        }
        else
        {
            Basis headRot = new Basis(bodyUp, -effYaw) * new Basis(bodyRight, -effPitch);
            Transform3D newHeadGlobal = new Transform3D(headRot * headGlobalPose.Basis, headGlobalPose.Origin);
            skeleton.SetBoneGlobalPose(_headBone, inverse * newHeadGlobal);

            if (_jawIsSiblingOfHead)
            {
                Transform3D jawGlobal = skelTransform * skeleton.GetBoneGlobalPose(_jawBone);
                Vector3 jawOffset = jawGlobal.Origin - headGlobalPose.Origin;
                Vector3 newJawOrigin = headGlobalPose.Origin + headRot * jawOffset;
                Basis newJawBasis = headRot * jawGlobal.Basis;
                skeleton.SetBoneGlobalPose(_jawBone, inverse * new Transform3D(newJawBasis, newJawOrigin));
            }
        }
    }
}
