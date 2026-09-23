using System.Collections.Generic;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>
/// Tool cursor (footprint + validity colour), placement ghost, selection marker and optional hydrology
/// debug visuals (flow arrows, depth dots, spring markers). Display only: reads the sim, never writes it.
/// </summary>
public partial class OverlayRenderer : Node3D
{
    private MeshInstance3D _ring = null!, _ghost = null!, _selection = null!;
    private StandardMaterial3D _ringMat = null!, _ghostMat = null!, _selMat = null!;
    private MultiMeshInstance3D _arrows = null!, _depthDots = null!, _springs = null!;
    private VivariumWorld? _w;
    private double _debugAccum = 999;
    public bool HydrologyDebug { get; set; }
    public static readonly Color Valid = new(0.25f, 0.95f, 0.45f, 0.75f);
    public static readonly Color Invalid = new(1.0f, 0.3f, 0.25f, 0.8f);

    public override void _Ready()
    {
        _ringMat = Unlit(Valid);
        _ring = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.93f, OuterRadius = 1.0f, Rings = 48, RingSegments = 6 }, MaterialOverride = _ringMat, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_ring);
        _ghostMat = Unlit(new Color(1, 1, 1, 0.35f));
        _ghost = new MeshInstance3D { MaterialOverride = _ghostMat, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_ghost);
        _selMat = Unlit(new Color(1.0f, 0.9f, 0.3f, 0.9f));
        _selection = new MeshInstance3D { Mesh = new TorusMesh { InnerRadius = 0.85f, OuterRadius = 1.0f, Rings = 32, RingSegments = 6 }, MaterialOverride = _selMat, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off };
        AddChild(_selection);

        var arrowMesh = new PrismMesh { Size = new Vector3(0.06f, 0.14f, 0.012f) };
        _arrows = MakeMm(arrowMesh, Unlit(new Color(0.2f, 0.6f, 1f, 0.9f)));
        _depthDots = MakeMm(new SphereMesh { Radius = 0.03f, Height = 0.03f, RadialSegments = 6, Rings = 3 }, Unlit(Colors.White, vertexColor: true));
        _springs = MakeMm(new SphereMesh { Radius = 0.09f, Height = 0.18f }, Unlit(new Color(0.3f, 1f, 1f, 0.9f)));
    }

    private MultiMeshInstance3D MakeMm(Mesh mesh, Material mat)
    {
        var mmi = new MultiMeshInstance3D
        {
            Multimesh = new MultiMesh { TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, UseColors = true, Mesh = mesh },
            MaterialOverride = mat, Visible = false, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(mmi);
        return mmi;
    }

    private static StandardMaterial3D Unlit(Color c, bool vertexColor = false) => new()
    {
        ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        AlbedoColor = c,
        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
        NoDepthTest = !vertexColor,
        VertexColorUseAsAlbedo = vertexColor,
        CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        RenderPriority = 10,
    };

    public void Bind(VivariumWorld w) { _w = w; _debugAccum = 999; }

    public void ShowCursor(Vector3 at, float radius, bool valid)
    {
        _ring.Visible = true;
        _ring.GlobalPosition = at + new Vector3(0, 0.01f, 0);
        _ring.Scale = new Vector3(radius, radius, radius);
        _ringMat.AlbedoColor = valid ? Valid : Invalid;
    }

    public void HideCursor() { _ring.Visible = false; _ghost.Visible = false; }

    public void ShowGhost(Mesh mesh, Transform3D t, bool valid)
    {
        _ghost.Mesh = mesh;
        _ghost.GlobalTransform = t;
        _ghost.Visible = true;
        _ghostMat.AlbedoColor = valid ? new Color(0.6f, 1f, 0.7f, 0.35f) : new Color(1f, 0.5f, 0.45f, 0.4f);
    }

    public void ShowSelection(Vector3? at, float radius)
    {
        _selection.Visible = at.HasValue;
        if (at.HasValue) { _selection.GlobalPosition = at.Value + new Vector3(0, 0.005f, 0); _selection.Scale = new Vector3(radius, radius, radius); }
    }

    /// <summary>Spring markers outside hydrology debug (while a water tool is active).</summary>
    public bool ShowSprings { get; set; }
    private int _springCount = -1;

    public override void _Process(double delta)
    {
        _arrows.Visible = _depthDots.Visible = HydrologyDebug && _w != null;
        _springs.Visible = (HydrologyDebug || ShowSprings) && _w != null;
        if (_w == null) return;
        if (ShowSprings && !HydrologyDebug && _w.Water.Springs.Count != _springCount) UpdateSprings();
        if (!HydrologyDebug) return;
        _debugAccum += delta;
        if (_debugAccum < 0.5) return;
        _debugAccum = 0;
        var w = _w; var g = w.Grid;
        var arrows = new List<Transform3D>(); var arrowCols = new List<Color>();
        var dots = new List<Transform3D>(); var dotCols = new List<Color>();
        foreach (int c in g.DomainCells)
        {
            if (!w.Water.IsWet(c)) continue;
            var p = g.CellCenter(c);
            double surf = w.Water.Bed[c] + w.Water.Depth[c];
            float d = Mathf.Clamp((float)(w.Water.Depth[c] / 0.3), 0, 1);
            dots.Add(new Transform3D(Basis.Identity, new Vector3((float)p.X, (float)surf + 0.02f, (float)p.Z)));
            dotCols.Add(new Color(1 - d, 0.4f + 0.6f * d, 1));
            var flow = new Vector2((float)w.Water.FlowX[c], (float)w.Water.FlowZ[c]);
            if (flow.Length() < 1e-6f || (c % 2 != 0)) continue;
            float mag = Mathf.Clamp(flow.Length() * 2000f, 0.3f, 2.5f);
            float yaw = Mathf.Atan2(flow.X, flow.Y);
            // prism points +Y: lay it flat along the flow direction
            var basis = new Basis(Vector3.Up, yaw) * new Basis(Vector3.Right, Mathf.Pi / 2) * Basis.FromScale(new Vector3(1, mag, 1));
            arrows.Add(new Transform3D(basis, new Vector3((float)p.X, (float)surf + 0.03f, (float)p.Z)));
            arrowCols.Add(new Color(0.2f, 0.6f, 1f));
        }
        Fill(_arrows.Multimesh, arrows, arrowCols);
        Fill(_depthDots.Multimesh, dots, dotCols);
        UpdateSprings();
    }

    private void UpdateSprings()
    {
        var w = _w!;
        _springCount = w.Water.Springs.Count;
        var springs = new List<Transform3D>(); var springCols = new List<Color>();
        foreach (var s in w.Water.Springs)
        {
            float size = Mathf.Clamp((float)(s.Discharge * 3600 / 0.05), 0.5f, 2.5f);
            springs.Add(new Transform3D(Basis.FromScale(Vector3.One * size), new Vector3((float)s.X, (float)w.SurfaceHeight(s.Position) + 0.15f, (float)s.Z)));
            springCols.Add(new Color(0.3f, 1f, 1f));
        }
        Fill(_springs.Multimesh, springs, springCols);
    }

    private static void Fill(MultiMesh mm, List<Transform3D> t, List<Color> c)
    {
        mm.InstanceCount = t.Count;
        for (int i = 0; i < t.Count; i++) { mm.SetInstanceTransform(i, t[i]); mm.SetInstanceColor(i, c[i]); }
    }
}
