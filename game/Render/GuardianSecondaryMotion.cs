using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Guardians;

namespace Vivarium.Game.Render;

/// <summary>
/// Adds subtle procedural reactive sway on ears, crest, horns, wings, or whiskers
/// responding to heading change angular velocity, alongside subtle snout twitching
/// and periodic natural blinking.
/// </summary>
public partial class GuardianSecondaryMotion : SkeletonModifier3D
{
    private int _earLBone = -1;
    private int _earRBone = -1;
    private int _crestBone = -1;
    private int _whiskersBone = -1;
    private int _hornLBone = -1;
    private int _hornRBone = -1;
    private int _wingLBone = -1;
    private int _wingRBone = -1;
    private int _snoutBone = -1;
    private readonly List<int> _blinkBones = new(4);
    private bool _initialized;

    private float _swayAngle;
    private float _swayVelocity;
    private float _snoutPhase;

    private float _blinkTimer;
    private float _nextBlinkInterval = 4.5f;
    private float _blinkProgress = -1f;

    public float AngularVelocity { get; set; }
    public GuardianActivity Activity { get; set; } = GuardianActivity.Rest;
    public float CurrentSway => _swayAngle;

    public void Reset()
    {
        _initialized = false;
        _swayAngle = 0;
        _swayVelocity = 0;
        _snoutPhase = 0;
        _blinkTimer = 0;
        _blinkProgress = -1f;
        _nextBlinkInterval = 4.5f;
        _blinkBones.Clear();
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
            _hornLBone = skeleton.FindBone("Horn_L");
            _hornRBone = skeleton.FindBone("Horn_R");
            _wingLBone = skeleton.FindBone("Wing_L");
            _wingRBone = skeleton.FindBone("Wing_R");
            _snoutBone = skeleton.FindBone("SnoutTip");
            if (_snoutBone < 0) _snoutBone = skeleton.FindBone("Snout");

            _blinkBones.Clear();
            int bL = skeleton.FindBone("Blink_L");
            if (bL < 0) bL = skeleton.FindBone("LidUpper_L");
            if (bL >= 0) _blinkBones.Add(bL);

            int bR = skeleton.FindBone("Blink_R");
            if (bR < 0) bR = skeleton.FindBone("LidUpper_R");
            if (bR >= 0) _blinkBones.Add(bR);

            _initialized = true;
        }

        if (_earLBone < 0 && _earRBone < 0 && _crestBone < 0 && _whiskersBone < 0
            && _hornLBone < 0 && _hornRBone < 0 && _wingLBone < 0 && _wingRBone < 0
            && _snoutBone < 0 && _blinkBones.Count == 0)
            return;

        float dt = (float)Math.Clamp(delta, 1e-4, 0.1);

        // Resolve current activity (fallback to breathing modifier if not set directly)
        var breathing = skeleton.GetNodeOrNull<GuardianBreathingModifier>("GuardianBreathingModifier");
        GuardianActivity activity = breathing?.Activity ?? Activity;

        // Reactive sway: inertia opposes angular velocity
        float targetSway = -Math.Clamp(AngularVelocity * 0.12f, -0.45f, 0.45f);
        float springForce = (targetSway - _swayAngle) * 35.0f;
        float dampingForce = _swayVelocity * 9.0f;
        _swayVelocity += (springForce - dampingForce) * dt;
        _swayAngle += _swayVelocity * dt;

        bool hasSway = !(MathF.Abs(_swayAngle) < 1e-4f && MathF.Abs(_swayVelocity) < 1e-4f && MathF.Abs(targetSway) < 1e-4f);

        if (hasSway)
        {
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

            // Apply very subtle inertia (0.15f) for horns (dewhorn)
            if (_hornLBone >= 0)
            {
                var poseRot = skeleton.GetBonePoseRotation(_hornLBone);
                var swayRot = new Quaternion(Vector3.Back, _swayAngle * 0.15f);
                skeleton.SetBonePoseRotation(_hornLBone, poseRot * swayRot);
            }

            if (_hornRBone >= 0)
            {
                var poseRot = skeleton.GetBonePoseRotation(_hornRBone);
                var swayRot = new Quaternion(Vector3.Back, -_swayAngle * 0.15f);
                skeleton.SetBonePoseRotation(_hornRBone, poseRot * swayRot);
            }

            // Apply subtle folded-wing balance settle (0.25f) for avian bipeds (crested_canopy, hushbill)
            if (_wingLBone >= 0)
            {
                var poseRot = skeleton.GetBonePoseRotation(_wingLBone);
                var swayRot = new Quaternion(Vector3.Back, _swayAngle * 0.25f);
                skeleton.SetBonePoseRotation(_wingLBone, poseRot * swayRot);
            }

            if (_wingRBone >= 0)
            {
                var poseRot = skeleton.GetBonePoseRotation(_wingRBone);
                var swayRot = new Quaternion(Vector3.Back, -_swayAngle * 0.25f);
                skeleton.SetBonePoseRotation(_wingRBone, poseRot * swayRot);
            }

            // Apply subtle twitch to whiskers
            if (_whiskersBone >= 0)
            {
                var poseRot = skeleton.GetBonePoseRotation(_whiskersBone);
                var swayRot = new Quaternion(Vector3.Up, _swayAngle * 0.5f);
                skeleton.SetBonePoseRotation(_whiskersBone, poseRot * swayRot);
            }
        }

        // Apply subtle proboscis/nose-twitch oscillation during sniffing/foraging for dewhorn, woodland_omnivore, fernback
        if (_snoutBone >= 0 && activity != GuardianActivity.Sleep)
        {
            bool isSniffing = activity is GuardianActivity.Forage or GuardianActivity.Investigate or GuardianActivity.Explore or GuardianActivity.Feed;
            float speed = isSniffing ? 14f : 5f;
            _snoutPhase = (_snoutPhase + dt * speed) % Mathf.Tau;
            float amp = isSniffing ? 0.04f : 0.015f;
            float twitchPitch = MathF.Sin(_snoutPhase * 2f) * amp;
            float twitchYaw = MathF.Cos(_snoutPhase) * (amp * 0.5f);
            var poseRot = skeleton.GetBonePoseRotation(_snoutBone);
            var twitchRot = new Quaternion(Vector3.Right, twitchPitch) * new Quaternion(Vector3.Up, twitchYaw);
            skeleton.SetBonePoseRotation(_snoutBone, poseRot * twitchRot);
        }

        // Apply periodic natural eye blink every 3.5..6.5 seconds (brief 0.14s smooth close-and-open pulse when not sleeping)
        if (_blinkBones.Count > 0)
        {
            if (activity == GuardianActivity.Sleep)
            {
                // Eyes smoothly closed while sleeping
                float blinkAngle = 0.45f;
                for (int i = 0; i < _blinkBones.Count; i++)
                {
                    int bone = _blinkBones[i];
                    var poseRot = skeleton.GetBonePoseRotation(bone);
                    var closeRot = new Quaternion(Vector3.Right, blinkAngle);
                    skeleton.SetBonePoseRotation(bone, poseRot * closeRot);
                }
            }
            else
            {
                if (_blinkProgress >= 0f)
                {
                    _blinkProgress += dt;
                    const float blinkDuration = 0.14f;
                    if (_blinkProgress >= blinkDuration)
                    {
                        _blinkProgress = -1f;
                        _nextBlinkInterval = 3.5f + (_blinkTimer % 3.0f);
                        _blinkTimer = 0f;
                    }
                    else
                    {
                        float t = _blinkProgress / blinkDuration;
                        float pulse = MathF.Sin(t * MathF.PI);
                        float blinkAngle = pulse * 0.45f;
                        for (int i = 0; i < _blinkBones.Count; i++)
                        {
                            int bone = _blinkBones[i];
                            var poseRot = skeleton.GetBonePoseRotation(bone);
                            var closeRot = new Quaternion(Vector3.Right, blinkAngle);
                            skeleton.SetBonePoseRotation(bone, poseRot * closeRot);
                        }
                    }
                }
                else
                {
                    _blinkTimer += dt;
                    if (_blinkTimer >= _nextBlinkInterval)
                    {
                        _blinkProgress = 0f;
                    }
                }
            }
        }
    }
}
