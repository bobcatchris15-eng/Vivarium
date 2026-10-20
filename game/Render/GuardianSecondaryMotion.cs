using System;
using Godot;
using Vivarium.Game.App;

namespace Vivarium.Game.Render;

/// <summary>
/// Adds subtle procedural reactive sway on ears, crest, or whiskers
/// responding to heading change angular velocity.
/// </summary>
public partial class GuardianSecondaryMotion : SkeletonModifier3D
{
    private int _earLBone = -1;
    private int _earRBone = -1;
    private int _crestBone = -1;
    private int _whiskersBone = -1;
    private bool _initialized;

    private float _swayAngle;
    private float _swayVelocity;

    public float AngularVelocity { get; set; }
    public float CurrentSway => _swayAngle;

    public void Reset()
    {
        _initialized = false;
        _swayAngle = 0;
        _swayVelocity = 0;
    }

    public override void _ProcessModificationWithDelta(double delta)
    {
        using var timing = FrameProfiler.Measure("Guardian.SecondaryMotion");
        var skeleton = GetSkeleton();
        if (skeleton == null) return;

        if (!_initialized)
        {
            _earLBone = skeleton.FindBone("Ear_L");
            _earRBone = skeleton.FindBone("Ear_R");
            _crestBone = skeleton.FindBone("Crest");
            _whiskersBone = skeleton.FindBone("Whiskers");
            if (_whiskersBone < 0) _whiskersBone = skeleton.FindBone("Whisker");
            _initialized = true;
        }

        if (_earLBone < 0 && _earRBone < 0 && _crestBone < 0 && _whiskersBone < 0)
            return;

        float dt = (float)Math.Clamp(delta, 1e-4, 0.1);

        // Reactive sway: inertia opposes angular velocity
        float targetSway = -Math.Clamp(AngularVelocity * 0.12f, -0.45f, 0.45f);
        float springForce = (targetSway - _swayAngle) * 35.0f;
        float dampingForce = _swayVelocity * 9.0f;
        _swayVelocity += (springForce - dampingForce) * dt;
        _swayAngle += _swayVelocity * dt;

        if (MathF.Abs(_swayAngle) < 1e-4f && MathF.Abs(_swayVelocity) < 1e-4f && MathF.Abs(targetSway) < 1e-4f)
            return;

        // Apply reactive sway to ears (mirrored for left/right)
        if (_earLBone >= 0)
        {
            var poseRot = skeleton.GetBonePoseRotation(_earLBone);
            var swayRot = new Quaternion(Vector3.Back, _swayAngle * 0.8f);
            skeleton.SetBonePoseRotation(_earLBone, poseRot * swayRot);
        }

        if (_earRBone >= 0)
        {
            var poseRot = skeleton.GetBonePoseRotation(_earRBone);
            var swayRot = new Quaternion(Vector3.Back, -_swayAngle * 0.8f);
            skeleton.SetBonePoseRotation(_earRBone, poseRot * swayRot);
        }

        // Apply reactive sway to crest (lateral roll)
        if (_crestBone >= 0)
        {
            var poseRot = skeleton.GetBonePoseRotation(_crestBone);
            var swayRot = new Quaternion(Vector3.Back, _swayAngle * 1.0f);
            skeleton.SetBonePoseRotation(_crestBone, poseRot * swayRot);
        }

        // Apply subtle twitch to whiskers
        if (_whiskersBone >= 0)
        {
            var poseRot = skeleton.GetBonePoseRotation(_whiskersBone);
            var swayRot = new Quaternion(Vector3.Up, _swayAngle * 0.5f);
            skeleton.SetBonePoseRotation(_whiskersBone, poseRot * swayRot);
        }
    }
}
