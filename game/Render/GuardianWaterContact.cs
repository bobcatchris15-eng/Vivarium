using System;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Core;
using Vivarium.Sim.Guardians;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>
/// Detects when Guardian is in Drink, Bathe, or walking across wet terrain.
/// Provides water contact state, disturbance radius, and surface ripple positions.
/// </summary>
public partial class GuardianWaterContact : Node3D
{
    public GuardianRenderer? Renderer { get; set; }
    public VivariumWorld? World { get; set; }

    public bool IsInWater { get; private set; }
    public bool IsDrinking { get; private set; }
    public bool IsBathing { get; private set; }
    public bool IsWalkingInWater { get; private set; }
    public float WaterDepth { get; private set; }
    public float SurfaceElevation { get; private set; }
    public float DisturbanceRadius { get; private set; }
    public Vector3[] RipplePositions { get; private set; } = Array.Empty<Vector3>();

    public override void _Process(double delta) => UpdateContact(delta);

    public void UpdateContact(double delta)
    {
        using var timing = FrameProfiler.Measure("Guardian.WaterContact");
        Vector3 pos = Renderer != null ? Renderer.DisplayPosition : GlobalPosition;
        var p = new Vec2(pos.X, pos.Z);

        bool terrainWet = World?.Water.IsWet(p) == true;
        float depth = (float)(World?.Water.OpenWaterDepth(p) ?? 0.0);
        double surfY = World?.Water.SurfaceAt(p) ?? double.NaN;

        var g = World?.Guardian;
        bool drinking = g?.Activity == GuardianActivity.Drink;
        bool bathing = g?.Activity == GuardianActivity.Bathe;
        bool walking = g?.Activity is GuardianActivity.Forage or GuardianActivity.Explore
            or GuardianActivity.Retreat or GuardianActivity.Climb or GuardianActivity.ReturnHome;
        bool walkingInWater = terrainWet && walking;

        IsInWater = terrainWet || drinking || bathing;
        IsDrinking = drinking;
        IsBathing = bathing;
        IsWalkingInWater = walkingInWater;
        WaterDepth = depth;
        SurfaceElevation = double.IsNaN(surfY) ? pos.Y : (float)surfY;

        float scale = (float)(g?.Scale ?? 1.0);

        Vector3 forward = Renderer != null ? -Renderer.GlobalBasis.Z.Normalized() : -GlobalBasis.Z.Normalized();
        Vector3 right = Renderer != null ? Renderer.GlobalBasis.X.Normalized() : GlobalBasis.X.Normalized();

        if (bathing)
        {
            DisturbanceRadius = 0.50f * scale;
            float flankOffset = 0.22f * scale;
            RipplePositions = new[]
            {
                new Vector3(pos.X, SurfaceElevation, pos.Z),
                new Vector3(pos.X - right.X * flankOffset, SurfaceElevation, pos.Z - right.Z * flankOffset),
                new Vector3(pos.X + right.X * flankOffset, SurfaceElevation, pos.Z + right.Z * flankOffset)
            };
        }
        else if (drinking)
        {
            DisturbanceRadius = 0.22f * scale;
            float reach = 0.35f * scale;
            RipplePositions = new[]
            {
                new Vector3(pos.X + forward.X * reach, SurfaceElevation, pos.Z + forward.Z * reach)
            };
        }
        else if (walkingInWater)
        {
            DisturbanceRadius = 0.18f * scale;
            float spread = 0.15f * scale;
            RipplePositions = new[]
            {
                new Vector3(pos.X - right.X * spread, SurfaceElevation, pos.Z - right.Z * spread),
                new Vector3(pos.X + right.X * spread, SurfaceElevation, pos.Z + right.Z * spread)
            };
        }
        else if (terrainWet)
        {
            DisturbanceRadius = 0.12f * scale;
            RipplePositions = new[]
            {
                new Vector3(pos.X, SurfaceElevation, pos.Z)
            };
        }
        else
        {
            DisturbanceRadius = 0f;
            RipplePositions = Array.Empty<Vector3>();
        }
    }
}
