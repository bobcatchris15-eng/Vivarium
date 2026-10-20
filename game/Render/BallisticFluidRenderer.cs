using Godot;
using System;
using System.Linq;
using System.Collections.Generic;
using Vivarium.Sim.Water;
namespace Vivarium.Game.Render;

/// <summary>Conserved falling face parcels form coherent GPU-displaced liquid sheets. Shared rings
/// are reconstructed from actual trajectory samples; isolated splash parcels remain compact droplets.</summary>
public partial class BallisticFluidRenderer : Node3D
{
    private const int Sides = 16;
    private MultiMeshInstance3D _sheets = null!, _drops = null!;
    private ShaderMaterial _material = null!, _dropMaterial = null!;
    private ImageTexture? _positions, _widths, _normals, _velocities;
    private readonly record struct Ring(Vector3 Position, Vector3 Velocity, Vector3 WidthAxis, Vector3 Normal, float WidthRadius, float Thickness, float ShapePower);
    public int BrickCount { get; private set; }
    public int KernelCount { get; private set; }
    public override void _Ready()
    {
        _material = Vivarium.Game.App.Bridge.Shader("res://Shaders/airborne_sheet.gdshader"); _material.SetShaderParameter("debug_surface", System.Environment.GetEnvironmentVariable("VIVARIUM_FLUID_DIAGNOSTIC") == "1");
        var v = new List<Vector3>(); var uv = new List<Vector2>(); var indices = new List<int>();
        for (int end = 0; end < 2; end++) for (int side = 0; side <= Sides; side++) { double angle = side * 2 * Math.PI / Sides; v.Add(new((float)Math.Cos(angle), end, (float)Math.Sin(angle))); uv.Add(new(end, (float)side / Sides)); }
        for (int side = 0; side < Sides; side++) { int a = side, b = side + 1, c = side + Sides + 2, d = side + Sides + 1; indices.AddRange(new[] { a, c, b, a, d, c }); }
        var arrays = new Godot.Collections.Array(); arrays.Resize((int)Mesh.ArrayType.Max); arrays[(int)Mesh.ArrayType.Vertex] = v.ToArray(); arrays[(int)Mesh.ArrayType.TexUV] = uv.ToArray(); arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();
        var topology = new ArrayMesh(); topology.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
        _sheets = new()
        {
            Name = "FallingLiquidSheets",
            MaterialOverride = _material,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            CustomAabb = new(new(-10, -5, -10), new(20, 10, 20)),
            Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = topology }
        }; AddChild(_sheets);
        _dropMaterial = Vivarium.Game.App.Bridge.Shader("res://Shaders/airborne_fluid.gdshader");
        _drops = new()
        {
            Name = "SecondaryLiquidDroplets",
            MaterialOverride = _dropMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseCustomData = true, Mesh = new SphereMesh { Radius = 1, Height = 2, RadialSegments = 12, Rings = 6 } }
        }; AddChild(_drops);
    }
    public void SetCanopyLighting(float energy, Vector3 direction)
    {
        foreach (var mat in new[] { _material, _dropMaterial })
        { mat.SetShaderParameter("sky_energy", energy); mat.SetShaderParameter("light_direction", direction); }
    }
    private static Vector3 V(Vivarium.Sim.Core.Vec3 p) => new((float)p.X, (float)p.Y, (float)p.Z);
    private static ImageTexture Upload(ImageTexture? texture, float[] values, int width, int height)
    {
        var bytes = new byte[values.Length * 4]; Buffer.BlockCopy(values, 0, bytes, 0, bytes.Length); using var image = Image.CreateFromData(width, height, false, Image.Format.Rgbaf, bytes);
        if (texture != null && texture.GetWidth() == width && texture.GetHeight() == height) { texture.Update(image); return texture; }
        texture?.Dispose(); return ImageTexture.CreateFromImage(image);
    }
    public void SetTime(float seconds) => _material.SetShaderParameter("fluid_time", seconds);
    public void SetInterpolation(float seconds) { _material.SetShaderParameter("interpolation_seconds", seconds); _dropMaterial.SetShaderParameter("interpolation_seconds", seconds); }
    private static IEnumerable<FluidParticle[]> ConnectedRuns(IEnumerable<FluidParticle> group)
    {
        var ordered = group.OrderByDescending(p => p.Position.Y).ToArray(); int start = 0;
        for (int i = 1; i <= ordered.Length; i++)
        {
            bool boundary = i == ordered.Length || (ordered[i].Position - ordered[i - 1].Position).Length > Math.Max(.006, ordered[i].Velocity.Length * SurfaceFluidDynamics.StepSeconds * 3);
            if (!boundary) continue;
            yield return ordered[start..i]; start = i;
        }
    }
    public void UpdateFluid(SurfaceFluidSnapshot snapshot)
    {
        var rings = new List<Ring>(); var segments = new List<(int, int)>(); var compact = new List<FluidParticle>();
        var visible = snapshot.Airborne.Where(p => p.Volume > SurfaceFluidDynamics.MinimumResolvedVolume).ToArray();
        foreach (var p in visible) if (p.Stream == 0 || p.Width <= 0) compact.Add(p);
        foreach (var parcels in visible.Where(p => p.Stream != 0 && p.Width > 0).GroupBy(p => p.Stream).OrderBy(p => p.Key).SelectMany(ConnectedRuns))
        {
            // A short connected run is still an emitted face of liquid. Turning it into
            // independent drops breaks a continuous trickle at every ledge and at startup.
            // Only isolated trajectory samples lack evidence for a connecting sheet.
            if (parcels.Length < 2) { compact.AddRange(parcels); continue; }
            var spans=new double[parcels.Length];var volumes=new double[parcels.Length];
            for(int i=0;i<parcels.Length;i++)
            {
                double before=i>0?(parcels[i].Position-parcels[i-1].Position).Length:parcels[i].Velocity.Length*SurfaceFluidDynamics.StepSeconds;
                double after=i+1<parcels.Length?(parcels[i].Position-parcels[i+1].Position).Length:before;
                spans[i]=Math.Max(.0001,(before+after)*.5);volumes[i]=parcels[i].Volume;
            }
            var surfaceVolumes=FluidCrossSection.ReconstructTubeVolumes(volumes,spans);
            int previous = -1;
            for (int i = 0; i < parcels.Length; i++)
            {
                var p = parcels[i]; var at = V(p.Position); var velocity = V(p.Velocity); var axis = V(p.WidthAxis).Normalized();
                var tangent = velocity.LengthSquared() > 1e-8 ? velocity.Normalized() : Vector3.Down; var normal = tangent.Cross(axis).Normalized();
                float before = i > 0 ? at.DistanceTo(V(parcels[i - 1].Position)) : velocity.Length() * (float)SurfaceFluidDynamics.StepSeconds;
                float after = i + 1 < parcels.Length ? at.DistanceTo(V(parcels[i + 1].Position)) : before;
                float length = Math.Max(.0001f, (before + after) * .5f), width = (float)p.Width * .5f;
                // Disconnected small volumes have no continuous face sheet. Reconstruct
                // their conserved volume as compact drops instead of broad paper rectangles.
                bool hasBefore = i > 0 && before <= Math.Max(.006f, velocity.Length() * (float)SurfaceFluidDynamics.StepSeconds * 3);
                bool hasAfter = i + 1 < parcels.Length && after <= Math.Max(.006f, velocity.Length() * (float)SurfaceFluidDynamics.StepSeconds * 3);
                if (!hasBefore && !hasAfter) { compact.Add(p); previous = -1; continue; }
                // A micrometre-thin face cannot remain a full-width rigid rectangle
                // throughout a fall. Capillary edge retraction forms a rounded filament.
                var section=FluidCrossSection.Reconstruct(surfaceVolumes[i],spans[i],p.Width,Math.Max(0,-p.Velocity.Y/9.81));
                width=(float)section.WidthRadius;float thickness=(float)section.ThicknessRadius;float shapePower=(float)section.ShapePower;
                bool continuous = previous >= 0 && before <= Math.Max(.006f, velocity.Length() * (float)SurfaceFluidDynamics.StepSeconds * 3);
                if (!continuous)
                { previous = rings.Count; rings.Add(new(at - tangent * length * .5f, velocity, axis, normal, width, thickness, shapePower)); }
                int current = rings.Count; rings.Add(new(at, velocity, axis, normal, width, thickness, shapePower)); segments.Add((previous, current)); previous = current;
                bool last = i + 1 == parcels.Length || after > Math.Max(.006f, velocity.Length() * (float)SurfaceFluidDynamics.StepSeconds * 3);
                if (last) { int end = rings.Count; rings.Add(new(at + tangent * length * .5f, velocity, axis, normal, width, thickness, shapePower)); segments.Add((previous, end)); previous = -1; }
            }
        }
        KernelCount = visible.Length; BrickCount = segments.Count; var mm = _sheets.Multimesh; mm.InstanceCount = segments.Count;
        if (rings.Count > 0)
        {
            int width = 128, height = (rings.Count + width - 1) / width; var positions = new float[width * height * 4]; var widths = new float[positions.Length]; var normals = new float[positions.Length]; var velocities = new float[positions.Length];
            for (int i = 0; i < rings.Count; i++)
            {
                var r = rings[i]; int k = i * 4; positions[k] = r.Position.X; positions[k + 1] = r.Position.Y; positions[k + 2] = r.Position.Z; positions[k + 3] = r.WidthRadius;
                widths[k] = r.WidthAxis.X; widths[k + 1] = r.WidthAxis.Y; widths[k + 2] = r.WidthAxis.Z; widths[k + 3] = r.Thickness;
                normals[k] = r.Normal.X; normals[k + 1] = r.Normal.Y; normals[k + 2] = r.Normal.Z; normals[k + 3] = r.ShapePower; velocities[k] = r.Velocity.X; velocities[k + 1] = r.Velocity.Y; velocities[k + 2] = r.Velocity.Z;
            }
            _positions = Upload(_positions, positions, width, height); _widths = Upload(_widths, widths, width, height); _normals = Upload(_normals, normals, width, height); _velocities = Upload(_velocities, velocities, width, height);
            _material.SetShaderParameter("rings", _positions); _material.SetShaderParameter("widths", _widths); _material.SetShaderParameter("normals", _normals); _material.SetShaderParameter("velocities", _velocities);
            for (int i = 0; i < segments.Count; i++) { mm.SetInstanceTransform(i, Transform3D.Identity); mm.SetInstanceCustomData(i, new(segments[i].Item1, segments[i].Item2, 0, 0)); }
        }
        var drops = _drops.Multimesh; drops.InstanceCount = compact.Count;
        for (int i = 0; i < compact.Count; i++)
        {
            var p = compact[i]; float radius = (float)Math.Cbrt(3 * p.Volume / (4 * Math.PI)); var velocity = V(p.Velocity); float stretch = 1 + Math.Min(3, velocity.Length() * .5f);
            var basis = Basis.Identity; if (velocity.LengthSquared() > .001f) { var axis = velocity.Normalized(); var cross = Vector3.Up.Cross(axis); if (cross.LengthSquared() > .00001f) basis = new(cross.Normalized(), Mathf.Acos(Mathf.Clamp(Vector3.Up.Dot(axis), -1, 1))); }
            basis *= Basis.FromScale(new(radius / Mathf.Sqrt(stretch), radius * stretch, radius / Mathf.Sqrt(stretch))); drops.SetInstanceTransform(i, new(basis, V(p.Position))); drops.SetInstanceCustomData(i, new(velocity.X, velocity.Y, velocity.Z, radius * 2));
        }
        SetInterpolation(0);
    }
    public override void _ExitTree() { _positions?.Dispose(); _widths?.Dispose(); _normals?.Dispose(); _velocities?.Dispose(); }
}
