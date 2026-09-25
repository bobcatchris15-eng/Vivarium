using System;
using Godot;
using Vivarium.Sim.Core;
using Vivarium.Sim.Fauna;
using Vivarium.Sim.Tools;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

/// <summary>
/// Maps the current tool + pointer to validated simulation actions (via SimHost.Tools), and drives the
/// cursor preview so validity is visible before committing. Holds only ids, never simulation objects.
/// </summary>
public partial class ToolController : Node
{
    public GameSession Session { get; set; } = null!;
    public ToolKind Current { get; private set; } = ToolKind.Select;
    public string? FloraSpecies { get; set; } = "creeping_groundcover";
    public string? FaunaSpecies { get; set; } = "springtail";
    public double NutrientRadius { get; set; } = 0.5;
    public double RockScale { get; set; } = 0.3;
    public double LogLength { get; set; } = 1.4;
    public double LogRadius { get; set; } = 0.14;
    public int LogDecay { get; set; } = 1;
    public double LogHeading { get; set; }
    public double GravelRadius { get; set; } = 0.6;
    public double SculptRadius { get; set; } = 0.6;
    public double WaterRadius { get; set; } = 0.4;
    // a brush stroke (sculpt/pour/drain) runs from the click until the button is released
    private bool _stroke, _strokeSculpted;
    private string? _strokeError;
    public WorldHit Hover { get; private set; } = WorldHit.None;
    public WorldHit Selected { get; private set; } = WorldHit.None;
    public EntityId Held { get; private set; } = EntityId.None;
    private Vec3 _heldOrigin;
    public EntityId MovingProp { get; private set; } = EntityId.None;
    /// <summary>Latest preview problem (null = valid) for the UI.</summary>
    public string? Preview { get; private set; }
    /// <summary>Pointer-driven hover/cursor (disabled for scripted runs so the mouse position cannot intrude).</summary>
    public bool PointerEnabled { get; set; } = true;
    public event Action<ToolKind>? ToolChanged;
    public event Action<WorldHit>? SelectionChanged;
    private VivariumWorld? _w;
    private ulong _placeCounter = 1;
    private double _nutrientRepeat;
    private Mesh? _rockGhost, _logGhost, _gravelGhost;
    /// <summary>Where the pointer ray actually lands; the cursor ring is drawn here even when Hover snaps to a critter.</summary>
    private Vec3 _cursorPoint;
    /// <summary>Critter the grab circle has snapped to (none when nothing is inside it).</summary>
    public EntityId GrabTarget { get; private set; } = EntityId.None;

    public void Bind(VivariumWorld w)
    {
        _w = w;
        Held = EntityId.None; MovingProp = EntityId.None;
        Select(WorldHit.None);
    }

    public void SetTool(ToolKind k)
    {
        if (Held.IsNone == false && k != ToolKind.Grab) CancelHold();
        MovingProp = EntityId.None;
        if (_stroke) EndStroke();
        Current = k;
        Session.Overlay.ShowSprings = k is ToolKind.PourWater or ToolKind.DrainWater or ToolKind.Spring;
        ToolChanged?.Invoke(k);
    }

    public void Select(WorldHit hit)
    {
        Selected = hit;
        SelectionChanged?.Invoke(hit);
    }

    private ToolActions? Actions => Session.Host?.Tools;

    // ------------------------------------------------------------------ pointer

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("Tools");
        if (_w == null || Session.Host == null) return;
        var vp = GetViewport();
        var mouse = vp.GetMousePosition();
        bool overUi = vp.GuiGetHoveredControl() != null || !PointerEnabled;
        if (Input.MouseMode == Input.MouseModeEnum.Captured) overUi = true;
        var (o, d) = Session.CameraRig.ScreenRay(mouse);
        Hover = overUi ? WorldHit.None : Selection.Raycast(_w, Bridge.S(o), Bridge.S(d), includeWater: Current is ToolKind.Grab or ToolKind.IntroduceFauna or ToolKind.PourWater or ToolKind.DrainWater || !Held.IsNone);
        _cursorPoint = Hover.Point;
        Hover = SnapToCritter(Hover);
        UpdatePreview();
        if (!Held.IsNone && Hover.IsHit)
        {
            var hold = Hover.Point + new Vec3(0, _w.Content.Tools.GrabHoldHeight, 0);
            Actions!.MoveHeld(Held, hold);
        }
        if (_stroke)
        {
            if (!Input.IsMouseButtonPressed(MouseButton.Left) || !IsBrush(Current)) EndStroke();
            else if (!overUi && Dab(Hover, delta) is { Ok: false } fail && fail.Message != _strokeError) { _strokeError = fail.Message; Feedback(fail); }
        }
        if (Current == ToolKind.Nutrients && Input.IsMouseButtonPressed(MouseButton.Left) && !overUi)
        {
            _nutrientRepeat += delta;
            if (_nutrientRepeat > 0.25) { _nutrientRepeat = 0; ApplyAt(Hover); }
        }
        // selection marker follows the selected entity (display position)
        if (!GrabTarget.IsNone && _w.Fauna.Get(GrabTarget) is { } target)
            Session.Overlay.ShowSelection(Session.Fauna.DisplayPosition(GrabTarget), FaunaMarkerRadius(target));
        else Session.Overlay.ShowSelection(SelectedMarker(), SelectedRadius());
    }

    /// <summary>Grab circle radius in metres: grows with viewing distance so it stays about the same size on screen.</summary>
    public static double GrabRadius(double viewDistance) => Math.Clamp(viewDistance * 0.035, 0.08, 1.5);

    /// <summary>With the grab tool (nothing held), any critter inside the cursor circle becomes the hover target.</summary>
    private WorldHit SnapToCritter(WorldHit hit)
    {
        GrabTarget = EntityId.None;
        if (Current != ToolKind.Grab || !Held.IsNone || !hit.IsHit || _w == null) return hit;
        if (hit.Kind != HitKind.Fauna)
        {
            var f = Selection.NearestFauna(_w, hit.Point.XZ, GrabRadius(hit.Distance));
            if (f == null) return hit;
            hit = new WorldHit(HitKind.Fauna, f.Id, f.Position, hit.Distance);
        }
        GrabTarget = hit.Id;
        return hit;
    }

    private float FaunaMarkerRadius(FaunaIndividual f) =>
        (float)Math.Max(0.02, _w!.FaunaSystem.PhenotypeOf(f).BodySize * _w.Content.FaunaOrThrow(f.SpeciesId).VisualScale * 0.8);

    private Vector3? SelectedMarker()
    {
        if (_w == null || !Selected.IsHit) return null;
        switch (Selected.Kind)
        {
            case HitKind.Fauna: return _w.Fauna.Get(Selected.Id) != null ? Session.Fauna.DisplayPosition(Selected.Id) : null;
            case HitKind.Flora: { var f = _w.Flora.Get(Selected.Id); return f == null ? null : new Vector3((float)f.X, (float)_w.GroundHeight(f.Position), (float)f.Z); }
            case HitKind.Rock or HitKind.Log or HitKind.Gravel:
            {
                var p = _w.Props.Find(Selected.Id);
                return p switch { Rock r => new Vector3((float)r.X, (float)_w.SurfaceHeight(r.Position), (float)r.Z), LogProp l => new Vector3((float)l.X, (float)_w.SurfaceHeight(l.Position), (float)l.Z), GravelPatch g => new Vector3((float)g.X, (float)_w.SurfaceHeight(g.Position), (float)g.Z), _ => null };
            }
            default: return null;
        }
    }

    private float SelectedRadius()
    {
        if (_w == null) return 0.1f;
        return Selected.Kind switch
        {
            HitKind.Fauna when _w.Fauna.Get(Selected.Id) is { } f => FaunaMarkerRadius(f),
            HitKind.Flora when _w.Flora.Get(Selected.Id) is { } fl => (float)fl.Radius(_w.Content.FloraOrThrow(fl.SpeciesId)) * 1.1f,
            HitKind.Rock when _w.Props.Find(Selected.Id) is Rock r => (float)r.FootprintRadius * 1.1f,
            HitKind.Log when _w.Props.Find(Selected.Id) is LogProp l => (float)l.FootprintRadius,
            HitKind.Gravel when _w.Props.Find(Selected.Id) is GravelPatch g => (float)g.Radius,
            _ => 0.1f,
        };
    }

    private void UpdatePreview()
    {
        var ov = Session.Overlay;
        if (!Hover.IsHit || _w == null) { ov.HideCursor(); Preview = null; return; }
        var at = Bridge.V(_cursorPoint);
        var p = Hover.Point.XZ;
        var t = Actions!;
        float r = 0.08f;
        Preview = null;
        switch (Current)
        {
            case ToolKind.Select: r = 0.06f; break;
            case ToolKind.Grab:
                if (!Held.IsNone) { Preview = t.ReleaseProblem(Held, p); r = 0.1f; }
                else { Preview = Hover.Kind == HitKind.Fauna ? null : "bring a critter inside the circle"; r = (float)GrabRadius(Hover.Distance); }
                break;
            case ToolKind.RemovePlant: Preview = Hover.Kind == HitKind.Flora ? null : "point at a plant, moss or lichen"; break;
            case ToolKind.Poke: r = (float)_w.Content.Tools.PokeRadius; break;
            case ToolKind.Nutrients:
                r = (float)t.ClampNutrientRadius(NutrientRadius);
                Preview = _w.Domain.Contains(p) ? null : "outside the island";
                break;
            case ToolKind.PlaceRock:
                Preview = t.PreviewRock(p, RockScale); r = (float)t.ClampRockScale(RockScale);
                _rockGhost ??= new SphereMesh { Radius = 1, Height = 1.4f };
                ov.ShowGhost(_rockGhost, new Transform3D(Basis.FromScale(new Vector3(r * 1.1f, r * 0.7f, r)), at), Preview == null);
                break;
            case ToolKind.PlaceLog:
            {
                double len = t.ClampLogLength(LogLength), rad = t.ClampLogRadius(LogRadius);
                Preview = t.PreviewLog(p, LogHeading, len, rad); r = (float)(len / 2);
                _logGhost ??= new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 1 };
                var basis = Bridge.Yaw(LogHeading) * new Basis(Vector3.Forward, Mathf.Pi / 2) * Basis.FromScale(new Vector3((float)rad, (float)len, (float)rad));
                ov.ShowGhost(_logGhost, new Transform3D(basis, at + new Vector3(0, (float)rad, 0)), Preview == null);
                break;
            }
            case ToolKind.PlaceGravel:
                r = (float)t.ClampGravelRadius(GravelRadius);
                Preview = t.PreviewGravel(p, GravelRadius);
                _gravelGhost ??= new CylinderMesh { TopRadius = 1, BottomRadius = 1, Height = 0.02f };
                ov.ShowGhost(_gravelGhost, new Transform3D(Basis.FromScale(new Vector3(r, 1, r)), at), Preview == null);
                break;
            case ToolKind.IntroduceFlora: Preview = FloraSpecies == null ? "pick a species in the catalog" : t.PreviewFlora(FloraSpecies, p); r = 0.12f; break;
            case ToolKind.IntroduceFauna: Preview = FaunaSpecies == null ? "pick a species in the catalog" : t.PreviewFauna(FaunaSpecies, p); r = 0.15f; break;
            case ToolKind.TerrainRaise or ToolKind.TerrainLower or ToolKind.TerrainSmooth:
                r = (float)t.ClampSculptRadius(SculptRadius);
                Preview = _w.Domain.Contains(p) ? null : "outside the island";
                break;
            case ToolKind.PourWater or ToolKind.DrainWater:
                r = (float)t.ClampWaterRadius(WaterRadius);
                Preview = !_w.Domain.Contains(p) ? "outside the island" : Current == ToolKind.DrainWater && !_w.Water.IsWet(p) ? "no surface water here" : null;
                break;
            case ToolKind.Spring:
                Preview = t.PreviewSpring(p); r = t.SpringNear(p) != null ? 0.25f : 0.12f;
                break;
            case ToolKind.MoveProp:
                if (!MovingProp.IsNone && _w.Props.Find(MovingProp) is { } prop)
                    Preview = prop switch { Rock rk => _w.Placement.ValidateRock(p, rk.SizeX, rk.SizeZ, rk.Id), LogProp lg => _w.Placement.ValidateLog(p, lg.RotationY, lg.Length, lg.Radius, lg.Id), GravelPatch g => _w.Placement.ValidateGravel(p, g.Radius), _ => null };
                else Preview = Hover.Kind is HitKind.Rock or HitKind.Log or HitKind.Gravel ? null : "point at a rock, log or gravel patch";
                r = 0.2f;
                break;
        }
        if (Current is not (ToolKind.PlaceRock or ToolKind.PlaceLog or ToolKind.PlaceGravel)) ov.HideCursor();
        ov.ShowCursor(at, r, Preview == null);
    }

    // ------------------------------------------------------------------ input

    public override void _UnhandledInput(InputEvent e)
    {
        if (_w == null || Session.Host == null) return;
        if (e is InputEventMouseButton mb && mb.Pressed)
        {
            if (mb.ButtonIndex == MouseButton.Left) { ApplyAt(Hover); GetViewport().SetInputAsHandled(); }
            else if ((mb.ButtonIndex is MouseButton.WheelUp or MouseButton.WheelDown) && (mb.CtrlPressed || mb.ShiftPressed))
            {
                ResizeTool(mb.ButtonIndex == MouseButton.WheelUp ? 1 : -1);
                GetViewport().SetInputAsHandled();
            }
        }
        else if (e is InputEventKey k && k.Pressed && !k.Echo && !Session.CameraRig.KeyboardBlocked)
        {
            switch (k.Keycode)
            {
                case Key.Key1: SetTool(ToolKind.Select); break;
                case Key.Key2: SetTool(ToolKind.Grab); break;
                case Key.Key3: SetTool(ToolKind.RemovePlant); break;
                case Key.Key4: SetTool(ToolKind.Nutrients); break;
                case Key.Key5: SetTool(ToolKind.Poke); break;
                case Key.Key6: SetTool(ToolKind.PlaceRock); break;
                case Key.Key7: SetTool(ToolKind.PlaceLog); break;
                case Key.Key8: SetTool(ToolKind.PlaceGravel); break;
                case Key.Key9: SetTool(ToolKind.IntroduceFlora); break;
                case Key.Key0: SetTool(ToolKind.IntroduceFauna); break;
                case Key.G: SetTool(Current switch { ToolKind.TerrainRaise => ToolKind.TerrainLower, ToolKind.TerrainLower => ToolKind.TerrainSmooth, _ => ToolKind.TerrainRaise }); break;
                case Key.H: SetTool(Current switch { ToolKind.PourWater => ToolKind.DrainWater, ToolKind.DrainWater => ToolKind.Spring, _ => ToolKind.PourWater }); break;
                case Key.Bracketleft: ResizeTool(-1); break;
                case Key.Bracketright: ResizeTool(1); break;
                case Key.R: LogHeading = (LogHeading + Math.PI / 8) % Math.PI; break;
                case Key.X or Key.Escape when !Held.IsNone: CancelHold(); GetViewport().SetInputAsHandled(); break;
                case Key.F when Selected.IsHit: FocusSelected(); break;
                case Key.Delete when Selected.Kind is HitKind.Rock or HitKind.Log or HitKind.Gravel: RemoveSelectedProp(); break;
                default: return;
            }
        }
    }

    public void ResizeTool(int dir)
    {
        double f = dir > 0 ? 1.15 : 1 / 1.15;
        switch (Current)
        {
            case ToolKind.Nutrients: NutrientRadius = Actions!.ClampNutrientRadius(NutrientRadius * f); break;
            case ToolKind.PlaceRock: RockScale = Actions!.ClampRockScale(RockScale * f); break;
            case ToolKind.PlaceLog: LogLength = Actions!.ClampLogLength(LogLength * f); break;
            case ToolKind.PlaceGravel: GravelRadius = Actions!.ClampGravelRadius(GravelRadius * f); break;
            case ToolKind.TerrainRaise or ToolKind.TerrainLower or ToolKind.TerrainSmooth: SculptRadius = Actions!.ClampSculptRadius(SculptRadius * f); break;
            case ToolKind.PourWater or ToolKind.DrainWater: WaterRadius = Actions!.ClampWaterRadius(WaterRadius * f); break;
        }
        ToolChanged?.Invoke(Current);
    }

    /// <summary>Applies the current tool at a world hit (used by clicks and by the smoke runner).</summary>
    public ToolResult? ApplyAt(WorldHit hit)
    {
        if (_w == null || Actions == null) return null;
        var t = Actions;
        ToolResult? r = null;
        var p = hit.Point.XZ;
        switch (Current)
        {
            case ToolKind.Select:
                Select(hit);
                return null;
            case ToolKind.Grab:
                if (!Held.IsNone)
                {
                    if (!hit.IsHit) return Feedback(ToolResult.Fail("point at the ground or water to release"));
                    r = t.Release(Held, p);
                    if (r.Value.Ok) Held = EntityId.None;
                }
                else if (hit.Kind == HitKind.Fauna)
                {
                    var f = _w.Fauna.Get(hit.Id);
                    if (f == null) return Feedback(ToolResult.Fail("that critter is gone"));
                    _heldOrigin = f.Position;
                    r = t.Grab(hit.Id);
                    if (r.Value.Ok) { Held = hit.Id; Select(hit); }
                }
                else r = ToolResult.Fail("point at a critter to pick it up");
                break;
            case ToolKind.RemovePlant:
                r = hit.Kind == HitKind.Flora ? t.RemovePlant(hit.Id) : ToolResult.Fail("point at a plant, moss or lichen");
                break;
            case ToolKind.Nutrients:
                if (!hit.IsHit) return null;
                r = t.ApplyNutrients(p, NutrientRadius);
                break;
            case ToolKind.Poke:
            {
                if (!hit.IsHit) return null;
                var cam = Session.CameraRig.Cam.GlobalTransform.Basis.Z * -1;
                var res = t.Poke(new PokeAction(hit.Point, Bridge.S(cam), 1.0, hit));
                Session.Flora.Wobble(res.FloraWobble);
                r = ToolResult.Success(res.Message, res.FaunaDisturbed.ToArray());
                break;
            }
            case ToolKind.PlaceRock: if (!hit.IsHit) return null; r = t.PlaceRock(p, RockScale, NextAngle(), NextSeed()); break;
            case ToolKind.PlaceLog: if (!hit.IsHit) return null; r = t.PlaceLog(p, LogHeading, LogLength, LogRadius, LogDecay, NextSeed()); break;
            case ToolKind.PlaceGravel: if (!hit.IsHit) return null; r = t.PlaceGravel(p, GravelRadius, NextSeed()); break;
            case ToolKind.IntroduceFlora:
                if (!hit.IsHit) return null;
                r = FloraSpecies == null ? ToolResult.Fail("pick a species first") : t.IntroduceFlora(FloraSpecies, p);
                break;
            case ToolKind.IntroduceFauna:
                if (!hit.IsHit) return null;
                r = FaunaSpecies == null ? ToolResult.Fail("pick a species first") : t.IntroduceFauna(FaunaSpecies, p);
                break;
            case ToolKind.TerrainRaise or ToolKind.TerrainLower or ToolKind.TerrainSmooth or ToolKind.PourWater or ToolKind.DrainWater:
            {
                // first dab of a stroke; _Process keeps brushing while the button stays down
                if (!hit.IsHit) return null;
                _stroke = true; _strokeError = null;
                var dab = Dab(hit, 0.1);
                if (dab is { Ok: false } bad) { _strokeError = bad.Message; return Feedback(bad); }
                return dab;
            }
            case ToolKind.Spring:
                if (!hit.IsHit) return null;
                r = t.ToggleSpring(p);
                break;
            case ToolKind.MoveProp:
                if (MovingProp.IsNone)
                {
                    if (hit.Kind is HitKind.Rock or HitKind.Log or HitKind.Gravel) { MovingProp = hit.Id; return Feedback(ToolResult.Success("click where it should go")); }
                    r = ToolResult.Fail("point at a rock, log or gravel patch");
                }
                else if (hit.IsHit)
                {
                    r = t.MoveProp(MovingProp, p);
                    if (r.Value.Ok) MovingProp = EntityId.None;
                }
                break;
        }
        return r.HasValue ? Feedback(r.Value) : null;
    }

    public static bool IsBrush(ToolKind k) => k is ToolKind.TerrainRaise or ToolKind.TerrainLower or ToolKind.TerrainSmooth or ToolKind.PourWater or ToolKind.DrainWater;

    /// <summary>One brush application lasting <paramref name="seconds"/>.</summary>
    private ToolResult? Dab(WorldHit hit, double seconds)
    {
        if (!hit.IsHit || Actions == null) return null;
        var t = Actions; var p = hit.Point.XZ;
        switch (Current)
        {
            case ToolKind.TerrainRaise: _strokeSculpted = true; return t.Sculpt(p, SculptRadius, SculptMode.Raise, seconds);
            case ToolKind.TerrainLower: _strokeSculpted = true; return t.Sculpt(p, SculptRadius, SculptMode.Lower, seconds);
            case ToolKind.TerrainSmooth: _strokeSculpted = true; return t.Sculpt(p, SculptRadius, SculptMode.Smooth, seconds);
            case ToolKind.PourWater: return t.PourWater(p, WaterRadius, seconds);
            case ToolKind.DrainWater: return t.DrainWater(p, WaterRadius, seconds);
            default: return null;
        }
    }

    private void EndStroke()
    {
        _stroke = false;
        if (_strokeSculpted) Actions?.EndSculptStroke();
        _strokeSculpted = false;
    }

    private ToolResult Feedback(ToolResult r)
    {
        Session.Ui?.Toast(r.Message, !r.Ok);
        return r;
    }

    public void CancelHold()
    {
        if (Held.IsNone || Actions == null) return;
        Actions.ReturnHeld(Held, _heldOrigin);
        Session.Ui?.Toast("put the critter back where it was", false);
        Held = EntityId.None;
    }

    public void FocusSelected()
    {
        if (!Selected.IsHit || _w == null) return;
        var sel = Selected;
        var w = _w;
        Session.CameraRig.Focus(() => SelectedMarker() ?? Bridge.V(sel.Point), sel.Kind == HitKind.Fauna ? 0.25f : 0.8f);
    }

    public void RemoveSelectedProp()
    {
        if (Actions == null || !(Selected.Kind is HitKind.Rock or HitKind.Log or HitKind.Gravel)) return;
        Feedback(Actions.RemoveProp(Selected.Id));
        Select(WorldHit.None);
    }

    public void BeginMoveSelectedProp()
    {
        if (!(Selected.Kind is HitKind.Rock or HitKind.Log or HitKind.Gravel)) return;
        SetTool(ToolKind.MoveProp);
        MovingProp = Selected.Id;
        Session.Ui?.Toast("click where it should go", false);
    }

    // variant seeds derive from world state (not wall time) so identical interventions replay identically
    private ulong NextSeed() => Rng.Mix(_w!.Seed, _w.Ids.LastSerial + 1 + _placeCounter++);
    private double NextAngle() => (Rng.Mix(_w!.Seed, _w.Ids.LastSerial + 77) % 6283) / 1000.0;
}
