using System;
using System.Collections.Generic;
using Godot;

namespace Vivarium.Game.Render;

/// <summary>Retains the authored stance/swing phase while adapting each foot to
/// its own visible support height. Elbows/knees keep their animated bend plane.</summary>
public partial class GuardianTerrainLegModifier : SkeletonModifier3D
{
    public GuardianRenderer Renderer { get; set; } = null!;
    public float MaximumReachError { get; private set; }
    public float MaximumSolePenetration { get; private set; }
    public float BodyHeightAdjustment { get; private set; }
    public float MaximumBodyPenetration { get; private set; }
    public float MaximumSkinFootPenetration { get; private set; }
    public float MaximumSkinBodyPenetration { get; private set; }
    public int SkinFootSampleCount { get; private set; }
    public float MaximumStanceFootGap { get; private set; }
    public float MaximumFootRetargetDistance { get; private set; }
    public float MaximumStanceFootTravel { get; private set; }
    private readonly Dictionary<int, Vector3> _previousFeet = new();
    private static string LegName(Skeleton3D skeleton, string suffix, bool front, int link)
    {
        string standard = (link == 0 ? (front ? "UpperArm_" : "Thigh_") : link == 1 ? (front ? "Forearm_" : "Shin_") : (front ? "Hand_" : "Foot_")) + suffix;
        if (skeleton.FindBone(standard) >= 0) return standard;
        string compact = (front ? "F" : "H") + suffix + (link == 0 ? "Upper" : link == 1 ? "Lower" : "Foot");
        return skeleton.FindBone(compact) >= 0 ? compact : (front ? "Front" : "Hind") + (link == 0 ? "Upper_" : link == 1 ? "Lower_" : "Foot_") + suffix;
    }
    public override void _ProcessModificationWithDelta(double delta)
    {
        using var timing=Vivarium.Game.App.FrameProfiler.Measure("Guardian.Legs");
        var skeleton = GetSkeleton();
        if (skeleton == null || Renderer == null || Renderer.IsClimbing) return;
        var transform = skeleton.GlobalTransform; var inverse = transform.AffineInverse();
        MaximumReachError = 0; MaximumSolePenetration = 0;
        MaximumSkinFootPenetration = 0; SkinFootSampleCount = 0; MaximumStanceFootGap = 0; MaximumFootRetargetDistance = 0; MaximumStanceFootTravel = 0;
        var swings = new Dictionary<int, float>();
        float bodyDrop = 0;
        foreach (string suffix in new[] { "L", "R" }) foreach (bool front in new[] { true, false })
        {
            int upper = skeleton.FindBone(LegName(skeleton, suffix, front, 0));
            int lower = skeleton.FindBone(LegName(skeleton, suffix, front, 1));
            int foot = skeleton.FindBone(LegName(skeleton, suffix, front, 2));
            if (upper < 0 || lower < 0 || foot < 0) continue;
            var a = transform * skeleton.GetBoneGlobalPose(upper).Origin;
            var b = transform * skeleton.GetBoneGlobalPose(lower).Origin;
            var c = transform * skeleton.GetBoneGlobalPose(foot).Origin;
            var rest = transform * skeleton.GetBoneGlobalRest(foot).Origin;
            swings[foot] = Math.Max(0, c.Y - rest.Y);
            float targetY = Renderer.SupportHeight(c) + Math.Max(.01f, rest.Y - Renderer.GlobalPosition.Y) + swings[foot];
            var currentSkin = Renderer.SkinContact?.Evaluate(skeleton.GetBoneName(foot).ToString(), Renderer.SupportHeight);
            if (currentSkin.HasValue && currentSkin.Value.Valid)
                targetY = c.Y + swings[foot] + .001f - currentSkin.Value.Minimum;
            float reach = a.DistanceTo(b) + b.DistanceTo(c) - .002f;
            var horizontal = c - a; horizontal.Y = 0;
            float verticalReach = MathF.Sqrt(Math.Max(.001f, reach * reach - horizontal.LengthSquared()));
            bodyDrop = Math.Min(bodyDrop, targetY + verticalReach - a.Y);
        }
        bodyDrop = Math.Max(bodyDrop, -.20f * transform.Basis.Scale.X);
        // Lower the connected body to keep all four limbs within reach, but
        // retain clearance under the chest/abdomen on visible root shelves.
        foreach (string name in new[] { "Pelvis", "Spine1", "Chest", "Lumbar", "Body", "Body0", "Body1", "Body2", "Body3" })
        {
            int bone = skeleton.FindBone(name); if (bone < 0) continue;
            var p = transform * skeleton.GetBoneGlobalPose(bone).Origin;
            bodyDrop = Math.Max(bodyDrop, Math.Min(0, Renderer.SupportHeight(p) + .145f * transform.Basis.Scale.X - p.Y));
        }
        BodyHeightAdjustment = bodyDrop;
        // 2-bone analytical IK: pelvis/hips depression follows the lowest foot drop.
        int root = skeleton.FindBone("Root");
        if (root < 0) root = skeleton.FindBone("Pelvis");
        if (root < 0) root = skeleton.FindBone("Hips");
        if (root >= 0 && bodyDrop < 0)
        {
            var pose = transform * skeleton.GetBoneGlobalPose(root);
            pose.Origin += Vector3.Up * bodyDrop;
            skeleton.SetBoneGlobalPose(root, inverse * pose);
        }
        var underside = Renderer.SkinContact?.Evaluate("Body", Renderer.SupportHeight);
        float requiredLift = underside.HasValue && underside.Value.Valid ? Math.Max(0, .001f - underside.Value.Minimum) : 0;
        // A pinned tail anchor cannot escape a root shelf in the appendage
        // solver. Keep its attachment supported with the connected trunk.
        if (Renderer.AppendageContact is { } appendage)
        {
            int attachment = skeleton.FindBone("Tail" + appendage.FirstTailIndex);
            if (attachment >= 0)
            {
                var point = transform * skeleton.GetBoneGlobalPose(attachment).Origin;
                requiredLift = Math.Max(requiredLift, Renderer.SupportHeight(point) + appendage.AttachmentGroundRadius * transform.Basis.Scale.X + .001f - point.Y);
            }
        }
        if (root >= 0 && requiredLift > 0)
        {
            float lift = Math.Min(.40f * transform.Basis.Scale.X, requiredLift);
            var pose = transform * skeleton.GetBoneGlobalPose(root);
            pose.Origin += Vector3.Up * lift;
            skeleton.SetBoneGlobalPose(root, inverse * pose);
            BodyHeightAdjustment += lift;
        }
        MaximumBodyPenetration = 0;
        foreach (string name in new[] { "Pelvis", "Spine1", "Chest", "Lumbar", "Body", "Body0", "Body1", "Body2", "Body3" })
        {
            int bone = skeleton.FindBone(name); if (bone < 0) continue;
            var p = transform * skeleton.GetBoneGlobalPose(bone).Origin;
            MaximumBodyPenetration = Math.Max(MaximumBodyPenetration,
                Renderer.SupportHeight(p) + .145f * transform.Basis.Scale.X - p.Y);
        }
        foreach (string suffix in new[] { "L", "R" }) foreach (bool front in new[] { true, false })
        {
            int upper = skeleton.FindBone(LegName(skeleton, suffix, front, 0));
            int lower = skeleton.FindBone(LegName(skeleton, suffix, front, 1));
            int foot = skeleton.FindBone(LegName(skeleton, suffix, front, 2));
            if (upper < 0 || lower < 0 || foot < 0) continue;
            var aPose = transform * skeleton.GetBoneGlobalPose(upper);
            var bPose = transform * skeleton.GetBoneGlobalPose(lower);
            var cPose = transform * skeleton.GetBoneGlobalPose(foot);
            var a = aPose.Origin; var b = bPose.Origin; var c = cPose.Origin;
            var restFoot = transform * skeleton.GetBoneGlobalRest(foot).Origin;
            float sole = Math.Max(.01f, restFoot.Y - Renderer.GlobalPosition.Y);
            float swing = swings.GetValueOrDefault(foot);
            var target = c; target.Y = Renderer.SupportHeight(c) + sole + swing;
            float first = a.DistanceTo(b), second = b.DistanceTo(c);
            if (Renderer.UseLocalFootholds)
            {
                // A foot on the lower side of a buttress can be unreachable
                // while the connected belly must stay above the upper shelf.
                // Look for a nearby reachable visible support instead of
                // silently clamping the leg and leaving a hovering stance.
                var original = target; float reach = first + second - .002f;
                float error = Math.Max(0, a.DistanceTo(target) - reach);
                if (error > .003f || !Renderer.FootTargetAllowed(target))
                {
                    float best = float.PositiveInfinity;
                    foreach (float radius in new[] { .025f, .05f, .075f, .10f, .125f })
                        for (int direction = 0; direction < 16; direction++)
                        {
                            float angle = direction * Mathf.Tau / 16;
                            var probe = original + new Vector3(MathF.Cos(angle), 0, MathF.Sin(angle)) * radius * transform.Basis.Scale.X;
                            if (!Renderer.FootTargetAllowed(probe)) continue;
                            probe.Y = Renderer.SupportHeight(probe) + sole + swing;
                            if (a.DistanceTo(probe) > reach || a.DistanceTo(probe) < Math.Abs(first - second) + .002f) continue;
                            float score = new Vector2(probe.X-original.X,probe.Z-original.Z).LengthSquared() + .2f*(probe.Y-original.Y)*(probe.Y-original.Y);
                            if (score < best) { best = score; target = probe; }
                        }
                    MaximumFootRetargetDistance = Math.Max(MaximumFootRetargetDistance,
                        new Vector2(target.X-original.X,target.Z-original.Z).Length());
                }
            }
            var aBasis = aPose.Basis; var bBasis = bPose.Basis; var cBasis = cPose.Basis;
            const int maximumFits = 8;
            for (int fit = 0; fit < maximumFits; fit++)
            {
            var line = target - a;
            float distance = Math.Clamp(line.Length(), Math.Max(.001f, Math.Abs(first - second) + .0001f), first + second - .0001f);
            var axis = line.Normalized();
            var bend = b - a; bend -= axis * bend.Dot(axis);
            if (bend.LengthSquared() < 1e-8f) bend = aPose.Basis.X.Normalized();
            float along = (first * first - second * second + distance * distance) / (2 * distance);
            var knee = a + axis * along + bend.Normalized() * MathF.Sqrt(Math.Max(0, first * first - along * along));
            var reached = a + axis * distance;
            aPose.Basis = Align(b - a, knee - a) * aBasis;
            bPose.Basis = Align(c - b, reached - knee) * bBasis; bPose.Origin = knee;
            cPose.Origin = reached;
            const float sample = .04f;
            var normal = new Vector3(Renderer.SupportHeight(reached - Vector3.Right * sample) - Renderer.SupportHeight(reached + Vector3.Right * sample),
                2 * sample, Renderer.SupportHeight(reached - Vector3.Back * sample) - Renderer.SupportHeight(reached + Vector3.Back * sample)).Normalized();
            // Blend slopes to avoid snapping the sole across tiny root ridges.
            cPose.Basis = Align(Renderer.GlobalBasis.Y.Normalized(), Vector3.Up.Lerp(normal, .7f).Normalized()) * cBasis;
            skeleton.SetBoneGlobalPose(upper, inverse * aPose);
            skeleton.SetBoneGlobalPose(lower, inverse * bPose);
            skeleton.SetBoneGlobalPose(foot, inverse * cPose);
            if (Renderer.SkinContact?.Valid != true)
            {
                MaximumReachError = Math.Max(MaximumReachError, reached.DistanceTo(target));
                MaximumSolePenetration = Math.Max(MaximumSolePenetration, Renderer.SupportHeight(reached) + sole - reached.Y);
                break;
            }
            var skin = Renderer.SkinContact.Evaluate(skeleton.GetBoneName(foot).ToString(), Renderer.SupportHeight);
            if (!skin.Valid) break;
            float adjustment = swing + .001f - skin.Minimum;
            if (fit == maximumFits - 1 || Math.Abs(adjustment) < .0005f)
            {
                MaximumSkinFootPenetration = Math.Max(MaximumSkinFootPenetration, -skin.Minimum);
                MaximumReachError = Math.Max(MaximumReachError, reached.DistanceTo(target));
                MaximumSolePenetration = Math.Max(MaximumSolePenetration, Renderer.SupportHeight(reached) + sole - reached.Y);
                if (swing < .003f) MaximumStanceFootGap = Math.Max(MaximumStanceFootGap, skin.Minimum);
                if (swing < .003f && _previousFeet.TryGetValue(foot, out var previous))
                    MaximumStanceFootTravel = Math.Max(MaximumStanceFootTravel,
                        new Vector2(reached.X-previous.X,reached.Z-previous.Z).Length());
                _previousFeet[foot] = reached;
                SkinFootSampleCount += skin.SampleCount; break;
            }
            target.Y += Math.Clamp(adjustment, -.12f, .12f) * .95f;
            }
        }
        var bodySkin = Renderer.SkinContact?.Evaluate("Body", Renderer.SupportHeight);
        MaximumSkinBodyPenetration = bodySkin.HasValue && bodySkin.Value.Valid ? Math.Max(0, -bodySkin.Value.Minimum) : 0;
    }
    private static Basis Align(Vector3 from, Vector3 to)
    {
        from = from.Normalized(); to = to.Normalized(); var cross = from.Cross(to);
        return cross.LengthSquared() < 1e-10f ? Basis.Identity : new Basis(cross.Normalized(), MathF.Acos(Math.Clamp(from.Dot(to), -1, 1)));
    }
}
