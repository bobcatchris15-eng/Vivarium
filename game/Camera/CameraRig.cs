using System;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Observation;
using Vivarium.Sim.World;

namespace Vivarium.Game.Camera;

/// <summary>
/// Avatar-free free-fly camera: RMB-drag look, WASD level flight, Space/Ctrl up/down, Q/E turn, Shift fast, wheel adjusts speed (stepped, persisted),
/// F focus/orbit a target, Esc leaves focus. Tracks the above/under-water medium via the sim's tracker.
/// Camera state is client-only and never reaches the simulation.
/// </summary>
public partial class CameraRig : Node3D
{
    public Camera3D Cam { get; private set; } = null!;
    public double Speed { get; set; } = 1.5;
    public double Sensitivity { get; set; } = 0.25;
    public bool InvertY { get; set; }
    public event Action<double>? SpeedChanged;
    public event Action<bool>? MediumChanged;
    /// <summary>Right button pressed and released without dragging (opens the tool wheel).</summary>
    public event Action<Vector2>? RightClicked;
    private bool _rmbDown;
    private Vector2 _rmbPressPos;
    private float _rmbDrag;
    private ulong _rmbPressMs;

    private float _yaw = 0, _pitch = -0.6f;
    private bool _looking;
    public bool Focused { get; private set; }
    private Func<Vector3>? _focusTarget;
    private float _orbitDistance = 1.2f;
    public WaterMediumTracker Medium { get; } = new();
    public VivariumWorld? World { get; set; }
    /// <summary>Set by UI when a text field has focus so typing doesn't fly the camera.</summary>
    public bool KeyboardBlocked { get; set; }

    public override void _Ready()
    {
        Cam = new Camera3D { Near = 0.005f, Far = 250, Fov = 62, Current = true };
        AddChild(Cam);
        ResetView();
        Medium.Changed += under => MediumChanged?.Invoke(under);
    }

    public void ResetView()
    {
        Position = new Vector3(0, 7.5f, 13.5f);
        _yaw = 0; _pitch = -0.5f;
        Focused = false;
        ApplyRotation();
    }

    public void LookAtPoint(Vector3 eye, Vector3 target)
    {
        Position = eye;
        var d = (target - eye).Normalized();
        _yaw = Mathf.Atan2(-d.X, -d.Z);
        _pitch = Mathf.Asin(Mathf.Clamp(d.Y, -1, 1));
        Focused = false;
        ApplyRotation();
    }

    private void ApplyRotation() => Rotation = new Vector3(_pitch, _yaw, 0);

    public void Focus(Func<Vector3> target, float distance = 0.6f)
    {
        _focusTarget = target;
        _orbitDistance = Mathf.Clamp(distance, 0.05f, 30f);
        Focused = true;
    }

    public void Unfocus() { Focused = false; _focusTarget = null; }

    /// <summary>
    /// Dolly toward (+) or away from (−) whatever is under the cursor. Each notch covers 15 % of the
    /// distance to that point, so zooming is fine near the ground and fast from afar; it never passes
    /// through the surface.
    /// </summary>
    public void Zoom(float notches, Vector2 screenPos)
    {
        var origin = Cam.ProjectRayOrigin(screenPos);
        var dir = Cam.ProjectRayNormal(screenPos);
        float dist = 8f;
        if (World != null)
        {
            double hit = Vivarium.Sim.Tools.Selection.RayTerrain(World, Bridge.S(origin), Bridge.S(dir));
            if (!double.IsInfinity(hit)) dist = (float)hit;
        }
        float step = Mathf.Clamp(dist * 0.15f, 0.004f, 4f) * notches;
        if (notches > 0) step = Mathf.Min(step, Mathf.Max(0f, dist - 0.03f));   // stop short of the surface
        Position += dir * step;
    }

    public void AdjustSpeed(int steps)
    {
        Speed = Math.Clamp(Speed * Math.Pow(1.3, steps), UserSettings.MinCameraSpeed, UserSettings.MaxCameraSpeed);
        SpeedChanged?.Invoke(Speed);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        switch (e)
        {
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Right:
                if (mb.Pressed) { _rmbDown = true; _rmbDrag = 0; _rmbPressPos = mb.Position; _rmbPressMs = Time.GetTicksMsec(); }
                else
                {
                    bool tap = _rmbDown && !_looking && Time.GetTicksMsec() - _rmbPressMs < 450;
                    _rmbDown = false;
                    if (_looking) { _looking = false; Input.MouseMode = Input.MouseModeEnum.Visible; Input.WarpMouse(_rmbPressPos); }
                    if (tap) RightClicked?.Invoke(_rmbPressPos);
                }
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseMotion mm0 when _rmbDown && !_looking:
                // only a real drag starts mouse-look; a quick tap opens the tool wheel instead
                _rmbDrag += mm0.Relative.Length();
                if (_rmbDrag > 6) { _looking = true; Input.MouseMode = Input.MouseModeEnum.Captured; }
                GetViewport().SetInputAsHandled();
                break;
            case InputEventMouseButton mb when mb.Pressed && (mb.ButtonIndex == MouseButton.WheelUp || mb.ButtonIndex == MouseButton.WheelDown):
                if (mb.CtrlPressed || mb.ShiftPressed) break; // tool sizing uses modified wheel
                bool zoomIn = mb.ButtonIndex == MouseButton.WheelUp;
                float notch = mb.Factor > 0 ? mb.Factor : 1f;   // smooth-scrolling touchpads send fractional notches
                if (Focused) _orbitDistance = Mathf.Clamp(_orbitDistance * Mathf.Pow(zoomIn ? 0.88f : 1.14f, notch), 0.05f, 30f);
                else Zoom(zoomIn ? notch : -notch, mb.Position);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey kk when kk.Pressed && !KeyboardBlocked && (kk.Keycode is Key.Equal or Key.Plus or Key.KpAdd):
                AdjustSpeed(1); GetViewport().SetInputAsHandled(); break;
            case InputEventKey kk when kk.Pressed && !KeyboardBlocked && (kk.Keycode is Key.Minus or Key.KpSubtract):
                AdjustSpeed(-1); GetViewport().SetInputAsHandled(); break;
            case InputEventMouseMotion mm when _looking:
                float s = Mathf.DegToRad((float)Sensitivity);
                _yaw -= mm.Relative.X * s;
                _pitch -= mm.Relative.Y * s * (InvertY ? -1 : 1);
                _pitch = Mathf.Clamp(_pitch, -1.55f, 1.55f);
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey k when k.Pressed && !k.Echo && k.Keycode == Key.Escape && Focused:
                Unfocus();
                GetViewport().SetInputAsHandled();
                break;
            case InputEventKey k when k.Pressed && !k.Echo && k.Keycode == Key.Home && !KeyboardBlocked:
                ResetView();
                break;
        }
    }

    /// <summary>Q/E turn rate in radians per second.</summary>
    public float TurnRate { get; set; } = 1.8f;

    public override void _Process(double delta)
    {
        float dt = (float)Math.Min(delta, 0.1);
        // Q / E turn the camera (in focus mode they orbit the target)
        if (!KeyboardBlocked)
        {
            float turn = (Input.IsKeyPressed(Key.Q) ? 1f : 0f) - (Input.IsKeyPressed(Key.E) ? 1f : 0f);
            _yaw += turn * TurnRate * dt;
        }
        if (Focused && _focusTarget != null)
        {
            var t = _focusTarget();
            var back = new Basis(Vector3.Up, _yaw) * new Basis(Vector3.Right, _pitch) * new Vector3(0, 0, 1);
            Position = Position.Lerp(t + back * _orbitDistance, 1 - Mathf.Exp(-dt * 10));
            ApplyRotation();
            if (!KeyboardBlocked && MoveInput() != Vector3.Zero) Unfocus();   // flight input returns to free-fly
        }
        else
        {
            ApplyRotation();
            if (!KeyboardBlocked)
            {
                var move = MoveInput();
                if (move != Vector3.Zero)
                {
                    float spd = (float)Speed * (Input.IsKeyPressed(Key.Shift) ? 4f : 1f) * (Input.IsKeyPressed(Key.Alt) ? 0.2f : 1f);
                    // level flight: W/A/S/D move in the horizontal plane whatever the pitch, Space/Ctrl move vertically,
                    // so looking down while moving no longer dives the camera into the ground
                    var yawOnly = new Basis(Vector3.Up, _yaw);
                    var dir = yawOnly * new Vector3(move.X, 0, move.Z) + new Vector3(0, move.Y, 0);
                    Position += dir.Normalized() * spd * dt;
                }
            }
        }
        if (World != null) Medium.Update(World, Bridge.S(GlobalPosition));
    }

    private static Vector3 MoveInput()
    {
        var v = Vector3.Zero;
        if (Input.IsKeyPressed(Key.W)) v.Z -= 1;
        if (Input.IsKeyPressed(Key.S)) v.Z += 1;
        if (Input.IsKeyPressed(Key.A)) v.X -= 1;
        if (Input.IsKeyPressed(Key.D)) v.X += 1;
        if (Input.IsKeyPressed(Key.Space)) v.Y += 1;
        if (Input.IsKeyPressed(Key.Ctrl)) v.Y -= 1;
        return v;
    }

    /// <summary>World ray under a screen point (for picking against sim geometry).</summary>
    public (Vector3 Origin, Vector3 Dir) ScreenRay(Vector2 screen) => (Cam.ProjectRayOrigin(screen), Cam.ProjectRayNormal(screen));
}
