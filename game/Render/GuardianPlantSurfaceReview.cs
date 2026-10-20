using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Godot;

namespace Vivarium.Game.Render;

internal static class GuardianPlantSurfaceReview
{
    public static async Task<object> Run(Node3D owner, string output)
    {
        var faces = new List<Vector3>();
        void Quad(float left, float right)
        {
            var a = new Vector3(left, .6f, -.2f); var b = new Vector3(right, .6f, -.2f);
            var c = new Vector3(right, .6f, .2f); var d = new Vector3(left, .6f, .2f);
            faces.AddRange(new[] { a, c, b, a, d, c });
        }
        Quad(-1, -.65f); Quad(.65f, 1);
        // Enough separated leaf triangles to exercise hierarchy splits.
        for (int i = 0; i < 16; i++) Quad(-1 + i * .01f, -.65f + i * .01f);
        var surface = new GuardianPlantSurface(faces.ToArray());
        int checks = 0;
        void Check(bool condition, string name)
        { checks++; if (!condition) throw new InvalidOperationException("Plant surface: " + name); }
        Check(!surface.TryClosest(Transform3D.Identity, new(.35f, .6f, 0), .05f, out _), "empty leaf gap must not contact");
        Check(!surface.TryClosest(new Transform3D(Basis.Identity.Scaled(Vector3.Zero), Vector3.Zero), Vector3.Zero, .05f, out _), "collapsed hidden instance must not invert");
        Check(!surface.TryClosest(Transform3D.Identity, Vector3.Zero, 0, out _), "zero reach cannot contact");
        foreach (var transform in new[] {
            Transform3D.Identity,
            new Transform3D(new Basis(Vector3.Up, .7f).Scaled(new Vector3(.4f, 1.7f, .8f)), new Vector3(2, .3f, -1)),
            new Transform3D(new Basis(Vector3.Right, .35f) * new Basis(Vector3.Up, -.8f), new Vector3(-1, .7f, 2)) })
        {
            var normal = transform.Basis.X.Cross(transform.Basis.Z).Normalized();
            var leaf = transform * new Vector3(.8f, .6f, 0);
            Check(surface.TryClosest(transform, leaf + normal * .03f, .05f, out var closest), "near real leaf");
            Check(Math.Abs(closest.DistanceTo(leaf + normal * .03f) - .03f) < .0001f, "world-space distance under transform");
            Check(!surface.TryClosest(transform, leaf + normal * .08f, .05f, out _), "beyond physical reach");
            Check(!surface.TryClosest(transform, transform * new Vector3(.35f, .6f, 0), .02f, out _), "transformed empty gap");
        }
        var degenerate = new GuardianPlantSurface(new[] { Vector3.Zero, Vector3.Zero, Vector3.Zero });
        Check(degenerate.TryClosest(Transform3D.Identity, new(0, .01f, 0), .02f, out _), "degenerate triangle remains finite");
        var broadphase = GuardianVegetationContact.RunBroadphaseSelfTest(owner);

        var stage = new Node3D(); owner.AddChild(stage);
        var environment = new WorldEnvironment { Environment = new Godot.Environment {
            BackgroundMode = Godot.Environment.BGMode.Color, BackgroundColor = Colors.Black,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = Colors.White, AmbientLightEnergy = .7f } };
        stage.AddChild(environment);
        stage.AddChild(new DirectionalLight3D { RotationDegrees = new(-40, -25, 0), LightEnergy = 1 });
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max);
        arrays[(int)Mesh.ArrayType.Vertex] = faces.ToArray();
        var normals = new Vector3[faces.Count]; Array.Fill(normals, Vector3.Up);
        arrays[(int)Mesh.ArrayType.Normal] = normals;
        var mesh = new ArrayMesh(); mesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        mesh.SurfaceSetMaterial(0, new StandardMaterial3D { AlbedoColor = new(.2f, .48f, .12f), CullMode = BaseMaterial3D.CullModeEnum.Disabled });
        var buffer = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, Mesh = mesh, InstanceCount = 1 };
        buffer.SetInstanceTransform(0, Transform3D.Identity);
        var draw = new MultiMeshInstance3D { Multimesh = buffer }; stage.AddChild(draw);
        var contact = new GuardianVegetationContact(); stage.AddChild(contact); contact.SetProcess(false); contact.Register(draw, 0);
        var probe = new MeshInstance3D { Mesh = new SphereMesh { Radius = .05f, Height = .1f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new(.65f, .17f, .08f) } };
        stage.AddChild(probe);
        var camera = new Camera3D { Position = new(1.7f, 1.8f, 2.7f), Projection = Camera3D.ProjectionType.Orthogonal, Size = 2.8f, Current = true };
        stage.AddChild(camera); camera.LookAt(new(0, .45f, 0));
        var records = new List<object>();
        // Real frame updates, no world-save changes; bend recovers between trials.
        for (int frame = 0; frame < 72; frame++)
        {
            float x = frame < 24 ? .35f : frame < 48 ? .8f : .35f;
            probe.Position = new(x, .63f, 0);
            contact.Resolve(probe.Position, .05f, false);
            contact._Process(1 / 30.0);
            if (frame == 23) Check(contact.ContactCount == 0 && contact.MaximumBend == 0, "registered mesh empty gap must stay still");
            if (frame == 24) Check(contact.ContactCount > 0 && contact.MaximumBend > 0, "registered mesh actual leaf must bend");
            records.Add(new { frame, probe = new[] { x, .63f, 0f }, contact.ContactCount, contact.MaximumBend });
            await owner.ToSignal(owner.GetTree(), SceneTree.SignalName.ProcessFrame);
            if (frame % 6 == 0 || frame == 23 || frame == 47 || frame == 71)
            {
                await owner.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                using var image = owner.GetViewport().GetTexture().GetImage();
                image.SavePng(Path.Combine(output, $"plant-contact-{frame:000}.png"));
            }
        }
        File.WriteAllText(Path.Combine(output, "plant-contact-motion.json"), System.Text.Json.JsonSerializer.Serialize(records));
        stage.QueueFree();
        return new { checks, broadphase.Cases, broadphase.Contacts, broadphase.DrawAudits, broadphase.Error,
            frames = records.Count, limitation = "Exact CPU mesh surface; shader wind displacement is not sampled. Focused fixture, not full guardian behavior acceptance." };
    }
}
