using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Core;
using Vivarium.Sim.Water;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>
/// Draws the spring runoff as actual moving water (Intent WATER-5).
/// </summary>
/// <remarks>
/// One low-poly blob per tracer in a single MultiMesh, so the whole stream is one draw call. This is deliberately
/// a first visual: it reads as discrete droplets rather than a continuous sheet. Turning the tracers into a
/// continuous surface (metaball/marching-cubes, or a particle-to-grid FLIP surface) is the follow-up, and the
/// choice should be made by looking at it, not guessed at.
///
/// The fluid is non-authoritative (see <see cref="SpringFluidSystem"/>), so this runs every frame including while
/// the simulation clock is paused.
/// </remarks>
public partial class WaterFluidRenderer : Node3D
{
    private MultiMeshInstance3D _mmi = null!;
    private VivariumWorld _w = null!;
    private float[] _buffer = System.Array.Empty<float>();

    public override void _Ready()
    {
        var mesh = BuildBlob();
        var mat = new StandardMaterial3D
        {
            AlbedoColor = new Color(0.62f, 0.78f, 0.80f, 0.72f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
            Roughness = 0.08f,
            Metallic = 0.0f,
            SpecularMode = BaseMaterial3D.SpecularModeEnum.SchlickGgx,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _mmi = new MultiMeshInstance3D
        {
            Name = "SpringRunoff",
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            MaterialOverride = mat,
            Multimesh = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                UseColors = false,
                UseCustomData = false,
                Mesh = mesh,
                InstanceCount = 0,
            },
        };
        AddChild(_mmi);
    }

    public void Bind(VivariumWorld world) => _w = world;

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("WaterFluid");
        if (_w == null) return;
        var particles = _w.SpringFluid.Particles;
        int n = particles.Count;
        var mm = _mmi.Multimesh;
        if (mm.InstanceCount != n) mm.InstanceCount = n;
        if (n == 0) return;
        if (_buffer.Length != n * 12) _buffer = new float[n * 12];
        for (int i = 0; i < n; i++)
        {
            var p = particles[i];
            float s = (float)ParticleFluid.Radius(p.Volume);
            int o = i * 12;
            _buffer[o + 0] = s; _buffer[o + 1] = 0f; _buffer[o + 2] = 0f; _buffer[o + 3] = (float)p.Position.X;
            _buffer[o + 4] = 0f; _buffer[o + 5] = s; _buffer[o + 6] = 0f; _buffer[o + 7] = (float)p.Position.Y;
            _buffer[o + 8] = 0f; _buffer[o + 9] = 0f; _buffer[o + 10] = s; _buffer[o + 11] = (float)p.Position.Z;
        }
        mm.Buffer = _buffer;
    }

    /// <summary>A low-poly sphere. Few triangles matter more than shape: a thousand instances are on screen.</summary>
    private static ArrayMesh BuildBlob()
    {
        var st = new SurfaceTool();
        st.Begin(Mesh.PrimitiveType.Triangles);
        const int rings = 6, sectors = 8;
        for (int r = 0; r < rings; r++)
        for (int sIdx = 0; sIdx < sectors; sIdx++)
        {
            float p1 = Mathf.Pi * (r + 0.5f) / rings, p2 = 2f * Mathf.Pi * (sIdx + 0.5f) / sectors;
            float p3 = Mathf.Pi * (r + 1.5f) / rings, p4 = 2f * Mathf.Pi * (sIdx + 1.5f) / sectors;
            Vector3 A = Sph(p1, p2), B = Sph(p3, p2), C = Sph(p3, p4), D = Sph(p1, p4);
            st.SetNormal(A); st.AddVertex(A);
            st.SetNormal(B); st.AddVertex(B);
            st.SetNormal(C); st.AddVertex(C);
            st.SetNormal(A); st.AddVertex(A);
            st.SetNormal(C); st.AddVertex(C);
            st.SetNormal(D); st.AddVertex(D);
        }
        return st.Commit();
    }

    private static Vector3 Sph(float phi, float theta) =>
        new(Mathf.Sin(phi) * Mathf.Cos(theta), Mathf.Cos(phi), Mathf.Sin(phi) * Mathf.Sin(theta));
}