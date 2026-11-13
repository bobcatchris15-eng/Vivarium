using System;
using System.Collections.Generic;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Repr;

/// <summary>One fixed-camera capture + measurement of a demo. Name + Variant form the PNG file name.</summary>
/// <param name="Variant">Passed to <see cref="IReprDemo.ApplyVariant"/> before the shot (A/B with identical camera).</param>
/// <param name="DayFraction">0..1 time of day to force (cycle seconds = fraction x 1200); null = fixed mid-morning (cycle second 60).</param>
public sealed record ReprShot(string Name, Vector3 Eye, Vector3 Target, float FovDeg = 40f, int MeasureFrames = 120, string? Variant = null, float? DayFraction = null);

/// <summary>An experimental representation demo. Implement with a public parameterless constructor; discovered by reflection.</summary>
public interface IReprDemo
{
    string Name { get; }
    /// <summary>Add all content under <paramref name="root"/>. Everything legacy is hidden; the demo is alone in the scene.</summary>
    void Build(Node3D root, ReprDemoContext ctx);
    IReadOnlyList<ReprShot> Shots { get; }
    void ApplyVariant(string? variant) { }
    void Tick(double dt) { }
}

/// <summary>Services for a demo. Created by the runner.</summary>
public sealed class ReprDemoContext
{
    public ulong WorldSeed { get; init; }
    public VivariumWorld World { get; init; } = null!;
    public GameSession Session { get; init; } = null!;
    public string OutDir { get; init; } = "";

    /// <summary>MeshData to ArrayMesh via Bridge.ToArrayMesh (no material attached).</summary>
    public ArrayMesh ToArrayMesh(MeshData m) => Bridge.ToArrayMesh(m);

    /// <summary>
    /// A legacy flora specimen built by the same code the game uses (FloraVisualProfile, flora.gdshader and
    /// its bindings, OrganismMeshes.FloraTier, FloraVisualProfile.Compile) but synchronously and as a single
    /// MultiMesh instance at mature size (juvenile = half size). Origin = plant base; scale = (RadiusAtMax, Height, RadiusAtMax).
    /// </summary>
    public Node3D? BuildLegacyFlora(string speciesId, int lodTier = 0, int morph = 0, bool juvenile = false)
        => LegacyFlora.Build(this, speciesId, lodTier, morph, juvenile);

    /// <summary>The game's stem (leaf=false) or leaf (leaf=true) material for a species; null if the species is unknown.</summary>
    public Material? LegacyFloraMaterial(string speciesId, bool leaf)
        => LegacyFlora.Material(this, speciesId, leaf);

    public Action<string>? LogSink { get; init; }
    public void Log(string line) { GD.Print("REPR " + line); LogSink?.Invoke(line); }
}
