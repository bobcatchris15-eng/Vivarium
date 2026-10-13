using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Godot;

// Diagnostic only. Call after AnimationPlayer.Advance(0), modifiers and one
// rendered frame. Does not mutate any bone, mesh, material, or animation.
public static class PangolinLidDump
{
    static object V(Vector3 v) => new { x = v.X, y = v.Y, z = v.Z };
    static object T(Transform3D t) => new { origin = V(t.Origin), basisX = V(t.Basis.X), basisY = V(t.Basis.Y), basisZ = V(t.Basis.Z) };
    static bool Relevant(string name) => name == "Head" || name.Contains("Blink");
    static void Visit(Node node, List<MeshInstance3D> meshes)
    { if (node is MeshInstance3D mesh) meshes.Add(mesh); foreach (Node child in node.GetChildren()) Visit(child, meshes); }
    public static void Dump(Node3D instance, string absolutePath)
    {
        var sk = instance.FindChild("Skeleton3D", true, false) as Skeleton3D;
        if (sk == null) throw new InvalidOperationException("No Skeleton3D in diagnostic instance");

        var bones = new List<object>();
        for (int i = 0; i < sk.GetBoneCount(); i++)
        {
            string name = sk.GetBoneName(i).ToString(); if (!Relevant(name)) continue;
            int parent = sk.GetBoneParent(i); var q = sk.GetBonePoseRotation(i);
            bones.Add(new { index = i, name, parent, parentName = parent < 0 ? "" : sk.GetBoneName(parent).ToString(),
                rest = T(sk.GetBoneRest(i)), globalRest = T(sk.GetBoneGlobalRest(i)),
                globalPose = T(sk.GetBoneGlobalPose(i)), posePosition = V(sk.GetBonePosePosition(i)),
                poseRotation = new { x = q.X, y = q.Y, z = q.Z, w = q.W }, poseScale = V(sk.GetBonePoseScale(i)),
                worldRest = T(sk.GlobalTransform * sk.GetBoneGlobalRest(i)), worldPose = T(sk.GlobalTransform * sk.GetBoneGlobalPose(i)) });
        }
        var meshes = new List<MeshInstance3D>(); Visit(instance, meshes); var bindings = new List<object>();
        foreach (var mesh in meshes)
        {
            var skin = mesh.Skin; if (skin == null || mesh.Mesh == null) continue;
            var binds = new List<object>();
            for (int i = 0; i < skin.GetBindCount(); i++)
            {
                string name = skin.GetBindName(i).ToString(); int index = name.Length > 0 ? sk.FindBone(name) : skin.GetBindBone(i);
                if (index < 0) { binds.Add(new { bind = i, name, unresolvedIndex = index }); continue; }
                if (!Relevant(sk.GetBoneName(index).ToString())) continue;
                var inverseBind = skin.GetBindPose(i);
                binds.Add(new { bind = i, bindName = name, bindBone = skin.GetBindBone(i), resolvedIndex = index,
                    resolvedName = sk.GetBoneName(index).ToString(), inverseBind = T(inverseBind),
                    neutralReconstruction = T(sk.GetBoneGlobalRest(index) * inverseBind),
                    currentDeformation = T(sk.GetBoneGlobalPose(index) * inverseBind) });
            }
            bindings.Add(new { mesh = mesh.Name.ToString(), meshGlobalTransform = T(mesh.GlobalTransform), skeletonPath = mesh.Skeleton.ToString(), binds });
        }
        File.WriteAllText(absolutePath, JsonSerializer.Serialize(new { skeletonGlobalTransform = T(sk.GlobalTransform), bones, bindings }, new JsonSerializerOptions { WriteIndented = true }));
    }
}

