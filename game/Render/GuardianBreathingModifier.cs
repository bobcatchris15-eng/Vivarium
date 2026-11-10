using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Guardians;

namespace Vivarium.Game.Render;

/// <summary>
/// Procedural respiratory chest expansion modifier inheriting from SkeletonModifier3D.
/// Finds rib/chest/spine bones ("Chest", "Spine1", "Pelvis", or "Body") and applies
/// procedural sinusoidal expansion based on guardian activity level without drifting the rest pose.
/// Preserves frame time budget (&lt; 10 μs).
/// </summary>
public partial class GuardianBreathingModifier : SkeletonModifier3D
{
    private readonly List<int> _boneIndices = new(4);
    private readonly List<Vector3> _restScales = new(4);
    private bool _initialized;
    private float _phase;

    public GuardianActivity Activity { get; set; } = GuardianActivity.Rest;
    public float CurrentExpansion { get; private set; }
    public float Phase => _phase;

    public void Reset() => _initialized = false;

    public override void _ProcessModificationWithDelta(double delta)
    {
        using var timing = FrameProfiler.Measure("Guardian.Breathing");
        var skeleton = GetSkeleton();
        if (skeleton == null) return;

        if (!_initialized)
        {
            _boneIndices.Clear();
            _restScales.Clear();

            // 1. Prefer Chest and Spine1 first
            int chest = skeleton.FindBone("Chest");
            int spine1 = skeleton.FindBone("Spine1");
            if (chest >= 0 || spine1 >= 0)
            {
                if (chest >= 0)
                {
                    _boneIndices.Add(chest);
                    _restScales.Add(skeleton.GetBoneRest(chest).Basis.Scale);
                }
                if (spine1 >= 0 && !_boneIndices.Contains(spine1))
                {
                    _boneIndices.Add(spine1);
                    _restScales.Add(skeleton.GetBoneRest(spine1).Basis.Scale);
                }
            }
            else
            {
                // 2. If neither exists, prefer Body1 and Body2 (armored_forest, fernback)
                int body1 = skeleton.FindBone("Body1");
                int body2 = skeleton.FindBone("Body2");
                if (body1 >= 0 || body2 >= 0)
                {
                    if (body1 >= 0)
                    {
                        _boneIndices.Add(body1);
                        _restScales.Add(skeleton.GetBoneRest(body1).Basis.Scale);
                    }
                    if (body2 >= 0 && !_boneIndices.Contains(body2))
                    {
                        _boneIndices.Add(body2);
                        _restScales.Add(skeleton.GetBoneRest(body2).Basis.Scale);
                    }
                }
                else
                {
                    // 3. Or Body ONLY if skeleton.GetBoneParent(bodyIdx) >= 0
                    int bodyIdx = skeleton.FindBone("Body");
                    if (bodyIdx >= 0 && skeleton.GetBoneParent(bodyIdx) >= 0)
                    {
                        _boneIndices.Add(bodyIdx);
                        _restScales.Add(skeleton.GetBoneRest(bodyIdx).Basis.Scale);
                    }
                    else
                    {
                        // 4. For ruffleback (root Body), apply subtle throat/gular pumping on Neck instead
                        int neck = skeleton.FindBone("Neck");
                        if (neck < 0) neck = skeleton.FindBone("Neck1");
                        if (neck >= 0)
                        {
                            _boneIndices.Add(neck);
                            _restScales.Add(skeleton.GetBoneRest(neck).Basis.Scale);
                        }
                    }
                }
            }
            _initialized = true;
        }

        if (_boneIndices.Count == 0) return;

        float rate = Activity switch
        {
            GuardianActivity.Sleep => 0.8f,
            GuardianActivity.Rest => 1.2f,
            GuardianActivity.Alert => 3.5f,
            GuardianActivity.Retreat => 4.0f,
            _ => 1.8f
        };

        float amp = Activity switch
        {
            GuardianActivity.Sleep => 0.04f,
            GuardianActivity.Rest => 0.025f,
            GuardianActivity.Alert => 0.035f,
            _ => 0.015f
        };

        _phase = (_phase + (float)delta * rate * Mathf.Tau) % Mathf.Tau;
        float expansion = MathF.Sin(_phase) * amp;
        CurrentExpansion = expansion;

        // Apply subtle local scale on the chest/spine bone pose without drifting the rest pose.
        for (int i = 0; i < _boneIndices.Count; i++)
        {
            int bone = _boneIndices[i];
            Vector3 restScale = _restScales[i];
            float boneAmp = i == 0 ? expansion : expansion * 0.5f;

            Vector3 scaleMod = new Vector3(
                restScale.X * (1.0f + boneAmp),
                restScale.Y * (1.0f + boneAmp * 0.6f),
                restScale.Z * (1.0f + boneAmp * 0.8f)
            );
            skeleton.SetBonePoseScale(bone, scaleMod);
        }
    }
}
