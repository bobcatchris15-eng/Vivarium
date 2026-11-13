using System.Collections.Generic;
using Godot;

namespace Vivarium.Repr;

/// <summary>Built-in smoke demo: proves cameras, lighting, time of day and metrics work.</summary>
public sealed class ProbeDemo : IReprDemo
{
    public string Name => "harness_probe";

    public IReadOnlyList<ReprShot> Shots { get; } = new[]
    {
        new ReprShot("wide", new Vector3(3f, 1.6f, 3f), new Vector3(0f, 0.5f, 0f)),
        new ReprShot("close", new Vector3(0.9f, 0.6f, 0.9f), new Vector3(0f, 0.4f, 0f)),
        new ReprShot("wide_night", new Vector3(3f, 1.6f, 3f), new Vector3(0f, 0.5f, 0f), DayFraction: 0.9f),
    };

    public void Build(Node3D root, ReprDemoContext ctx)
    {
        var matte = new StandardMaterial3D { AlbedoColor = new Color(0.55f, 0.52f, 0.46f), Roughness = 1f, Metallic = 0f };
        root.AddChild(new MeshInstance3D { Name = "Ground", Mesh = new PlaneMesh { Size = new Vector2(4f, 4f) }, MaterialOverride = matte });
        root.AddChild(new MeshInstance3D
        {
            Name = "Sphere", Position = new Vector3(-0.7f, 0.15f, 0.4f),
            Mesh = new SphereMesh { Radius = 0.15f, Height = 0.3f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.75f, 0.25f, 0.2f), Roughness = 0.6f },
        });
        root.AddChild(new MeshInstance3D
        {
            Name = "Cylinder", Position = new Vector3(0.8f, 0.5f, -0.6f),
            Mesh = new CylinderMesh { TopRadius = 0.03f, BottomRadius = 0.03f, Height = 1f },
            MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.25f, 0.45f, 0.75f), Roughness = 0.6f },
        });
        var flora = ctx.BuildLegacyFlora("embercrown");
        if (flora != null) root.AddChild(flora);
        else ctx.Log("harness_probe: embercrown unavailable");
    }
}
