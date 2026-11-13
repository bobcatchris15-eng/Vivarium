using System;
using System.Collections.Generic;
using Godot;

namespace Vivarium.Repr;

/// <summary>More representative lighting probe: 6x6 m matte ground, ring of 12 legacy plants, a few box rocks.</summary>
public sealed class LightProbeDemo : IReprDemo
{
    public string Name => "light_probe";

    public IReadOnlyList<ReprShot> Shots { get; } = new[]
    {
        new ReprShot("mid", new Vector3(4f, 1.8f, 4f), new Vector3(0f, 0.6f, 0f), MeasureFrames: 180),
        new ReprShot("under", new Vector3(0.6f, 0.35f, 0.6f), new Vector3(-1.5f, 0.9f, -1.2f), MeasureFrames: 180),
        new ReprShot("night", new Vector3(4f, 1.8f, 4f), new Vector3(0f, 0.6f, 0f), DayFraction: 0.9f, MeasureFrames: 180),
    };

    public void Build(Node3D root, ReprDemoContext ctx)
    {
        var brown = new StandardMaterial3D { AlbedoColor = new Color(0.30f, 0.20f, 0.12f), Roughness = 1f, Metallic = 0f };
        root.AddChild(new MeshInstance3D { Name = "Ground", Mesh = new PlaneMesh { Size = new Vector2(6f, 6f) }, MaterialOverride = brown });

        string[] species = { "embercrown", "shadebell", "glowbriar", "moonfern" };
        for (int i = 0; i < 12; i++)
        {
            float a = i * Mathf.Tau / 12f;
            var flora = ctx.BuildLegacyFlora(species[i % 4]);
            if (flora == null) { ctx.Log($"light_probe: {species[i % 4]} unavailable"); continue; }
            flora.Position = new Vector3(Mathf.Cos(a) * 2f, 0f, Mathf.Sin(a) * 2f);
            flora.RotationDegrees = new Vector3(0f, i * 37f, 0f);
            root.AddChild(flora);
        }

        var rock = new StandardMaterial3D { AlbedoColor = new Color(0.45f, 0.44f, 0.42f), Roughness = 0.95f };
        var rocks = new (Vector3 pos, Vector3 size, float yaw)[]
        {
            (new Vector3(0.5f, 0.15f, -0.4f), new Vector3(0.5f, 0.3f, 0.4f), 20f),
            (new Vector3(-0.8f, 0.1f, 0.6f), new Vector3(0.35f, 0.2f, 0.3f), -35f),
            (new Vector3(0.1f, 0.25f, 0.9f), new Vector3(0.3f, 0.5f, 0.3f), 50f),
            (new Vector3(-0.3f, 0.08f, -0.9f), new Vector3(0.4f, 0.16f, 0.25f), 80f),
        };
        for (int i = 0; i < rocks.Length; i++)
            root.AddChild(new MeshInstance3D
            {
                Name = "Rock" + i, Position = rocks[i].pos, RotationDegrees = new Vector3(0f, rocks[i].yaw, 0f),
                Mesh = new BoxMesh { Size = rocks[i].size }, MaterialOverride = rock,
            });
    }
}
