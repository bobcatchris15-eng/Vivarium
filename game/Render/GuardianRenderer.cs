using System;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Core;
using Vivarium.Sim.Guardians;
using Vivarium.Sim.World;

namespace Vivarium.Game.Render;

/// <summary>
/// Controls the visual representation, skeletal animation blending,
/// and terrain contact of the island's unique Guardian Animal.
/// Loads the high-fidelity GLB asset from res://assets/guardians/{archetype}/guardian.glb.
/// </summary>
public partial class GuardianRenderer : Node3D
{
    private VivariumWorld? _w;
    private Node3D? _instance;
    private AnimationPlayer? _animPlayer;
    private Skeleton3D? _skeleton;
    private GuardianDef? _def;

    // Motion smoothing
    private Vector3 _prevPos;
    private Vector3 _curPos;
    private Vector3 _shownPos;
    private float _prevHeading;
    private float _curHeading;
    private float _shownHeading;
    private float _prevShownHeading;
    private double _prevT;
    private double _curT;
    private string _currentAnim = "";
    private float _groundOffset;

    public Camera3D? Camera { get; set; }
    public int Quality { get; set; } = 1;
    public bool Drawn { get; private set; }
    public bool OffScreen { get; private set; }
    public Vector3 DisplayPosition => GlobalPosition;
    public FloraRenderer? Vegetation { get; set; }
    public GuardianAppendageModifier? AppendageContact { get; private set; }
    public GuardianVegetationContact? ShrubContact { get; private set; }
    public GuardianTerrainLegModifier? LegContact { get; private set; }
    public GuardianSkinContact? SkinContact { get; private set; }
    public GuardianBreathingModifier? BreathingModifier { get; private set; }
    public GuardianHeadTracker? HeadTracker { get; private set; }
    public GuardianSecondaryMotion? SecondaryMotion { get; private set; }
    public GuardianWaterContact? WaterContact { get; private set; }
    private readonly System.Collections.Generic.List<ShaderMaterial> _guardianMaterials = new();
    public float BodyTrunkCorrection { get; private set; }
    /// <summary>Wall time of each step of the most recent guardian build, in milliseconds.</summary>
    public readonly System.Collections.Generic.Dictionary<string, double> BuildTimings = new();
    /// <summary>Diagnostic studio support only; null uses production terrain/root contact.</summary>
    public float? ReviewSupportPlaneHeight { get; set; }
    public Func<Vector3, float>? ReviewGroundSample { get; set; }    public bool UseLocalFootholds => _def?.Archetype is "woodland_omnivore" or "armored_forest" or "quilled_arboreal" or "crested_canopy";
    public bool FootTargetAllowed(Vector3 point) => _w?.PilotTree?.BlocksTrunk(new Vec2(point.X, point.Z)) != true;
    public float SupportHeight(Vector3 point)
    {
        if (ReviewGroundSample != null) return ReviewGroundSample(point);
        if (ReviewSupportPlaneHeight.HasValue) return ReviewSupportPlaneHeight.Value;
        if (_w == null) return point.Y;
        var xz = new Vec2(point.X, point.Z);
        // A ground-following boa that brushes a vertical trunk must resolve
        // its side, not acquire the tree roof as an instantaneous support target.
        // Preserve real root shelves and support on top of the trunk.
        if ((AppendageContact?.ContinuousGroundedBody == true || UseLocalFootholds || _def?.Archetype == "great_ground") && _w.PilotTree is { } tree &&
            tree.BlocksTrunk(xz) && point.Y < tree.BaseHeight + tree.Def.TrunkHeight - .05)
            return (float)_w.SurfaceHeight(xz);
        return (float)_w.GroundHeight(xz);
    }

    static readonly string[] BodyBoneNames = { "Pelvis", "Spine1", "Chest", "Lumbar", "Neck", "Head", "Snout", "Body", "Body0", "Body1", "Body2", "Body3" };
    int[] _bodyBoneIdx = new int[BodyBoneNames.Length];
    int[] _handFootBoneIdx = System.Array.Empty<int>();
    int _attachmentBoneIdx = -1;
    Skeleton3D? _boneCacheSkeleton;
    object? _boneCacheAppendage;
    int _boneCacheCount = -1;

    // Bone indices are cached per skeleton instance; refreshed if the skeleton,
    // its bone count, or the appendage config changes.
    void EnsureBoneCache()
    {
        if (_skeleton == null) return;
        int count = _skeleton.GetBoneCount();
        if (ReferenceEquals(_boneCacheSkeleton, _skeleton) && ReferenceEquals(_boneCacheAppendage, AppendageContact) && _boneCacheCount == count) return;
        for (int i = 0; i < BodyBoneNames.Length; i++) _bodyBoneIdx[i] = _skeleton.FindBone(BodyBoneNames[i]);
        var list = new System.Collections.Generic.List<int>();
        for (int b = 0; b < count; b++)
        {
            string n = _skeleton.GetBoneName(b);
            if (n.Contains("Hand") || n.Contains("Foot")) list.Add(b);
        }
        _handFootBoneIdx = list.ToArray();
        _attachmentBoneIdx = AppendageContact == null ? -1
            : _skeleton.FindBone(AppendageContact.ChainPrefix + AppendageContact.FirstTailIndex.ToString(AppendageContact.ChainIndexFormat));
        _boneCacheSkeleton = _skeleton; _boneCacheAppendage = AppendageContact; _boneCacheCount = count;
    }

    public void Build(VivariumWorld w)
    {
        _w = w;
        _guardianMaterials.Clear();
        SkinContact = null;
        // Loading a guardian is the longest single stage of a world start, so the
        // individual costs are timed and reported instead of guessed at.
        var buildTimer = System.Diagnostics.Stopwatch.StartNew();
        double mark = 0;
        void Mark(string name) { double now = buildTimer.Elapsed.TotalMilliseconds; BuildTimings[name] = now - mark; mark = now; }
        BuildTimings.Clear();
        if (_instance != null)        {
            RemoveChild(_instance);
            _instance.QueueFree();
            _instance = null;
            _animPlayer = null;
            _skeleton = null;
            AppendageContact = null;
            LegContact = null;
            BreathingModifier = null;
            HeadTracker = null;
            SecondaryMotion = null;
            if (ShrubContact != null) { ShrubContact.QueueFree(); ShrubContact = null; }
            if (WaterContact != null) { WaterContact.QueueFree(); WaterContact = null; }
        }

        if (w.Guardian == null) return;

        _def = GuardianCatalog.Find(w.Guardian.ArchetypeId);
        if (_def == null) return;

        string glbPath = $"res://assets/guardians/{_def.Archetype}/guardian.glb";
        // The guardian model is the largest asset in the game. When the world
        // start knows which guardian it will need, the load is started on a
        // worker thread so the island, water, props and tree are prepared while
        // the model is still streaming in.
        var threaded = PreloadGuardians.TryTake(glbPath);
        if (ResourceLoader.Exists(glbPath))
        {
            try
            {
                PackedScene? scene = threaded;
                if (scene == null)
                {
                    var status = ResourceLoader.LoadThreadedGetStatus(glbPath);
                    if (status == ResourceLoader.ThreadLoadStatus.Loaded) scene = ResourceLoader.LoadThreadedGet(glbPath) as PackedScene;
                    else if (status == ResourceLoader.ThreadLoadStatus.InProgress) scene = null;
                }
                if (scene == null) scene = GD.Load<PackedScene>(glbPath);
                Mark("LoadPackedScene");
                if (scene != null)
                {
                    if (_def.Archetype == "woodland_omnivore" && Array.IndexOf(Main.UserArgs, "--guardian-review") >= 0
                        && Main.ArgAfter("--guardian-coati-model-candidate") is { } modelPath)
                    {
                        using var document = new GltfDocument();
                        using var state = new GltfState();
                        if (document.AppendFromFile(modelPath, state) != Error.Ok)
                            throw new InvalidOperationException("Could not load coati model candidate: " + modelPath);
                        _instance = document.GenerateScene(state) as Node3D
                            ?? throw new InvalidOperationException("Coati model candidate is not spatial");
                    }
                    else _instance = scene.Instantiate<Node3D>();
                    Mark("Instantiate");
                    if (_def.Archetype == "woodland_omnivore" && Array.IndexOf(Main.UserArgs, "--guardian-review") >= 0
                        && Main.ArgAfter("--guardian-coati-coat-candidate") is { } coatPath)
                        LoadReviewCoat(_instance, coatPath);
                    AddChild(_instance);

                    _animPlayer = _instance.GetNodeOrNull<AnimationPlayer>("AnimationPlayer")
                        ?? _instance.FindChild("AnimationPlayer", recursive: true, owned: false) as AnimationPlayer;
                    _skeleton = _instance.GetNodeOrNull<Skeleton3D>("Skeleton3D")
                        ?? _instance.FindChild("Skeleton3D", recursive: true, owned: false) as Skeleton3D;

                    Log.Info(LogCategory.App, $"GuardianRenderer: Loaded GLB for '{_def.Name}' from {glbPath} (AnimPlayer={_animPlayer != null}, Skeleton={_skeleton != null})");
                }
            }
            catch (Exception ex)
            {
                Log.Warn(LogCategory.App, $"GuardianRenderer: Failed loading {glbPath}: {ex.Message}");
            }
        }

        if (_instance == null)
        {
            // Fallback placeholder representation if GLB asset is unavailable
            _instance = CreateFallbackMesh(_def);
            AddChild(_instance);
            Log.Info(LogCategory.App, $"GuardianRenderer: Created fallback procedural representation for '{_def.Name}'");
        }

        // These assets were authored Y-up inside Blender's Z-up scene. glTF
        // export maps their authored up axis to -Z; restore Y-up at the boundary.
        if (_animPlayer != null)
        {
            foreach (string name in _animPlayer.GetAnimationList())
                if (name != "RESET") _animPlayer.GetAnimation(name).LoopMode = Animation.LoopModeEnum.Linear;
            if (_animPlayer.HasAnimation("idle")) { _animPlayer.Play("idle"); _animPlayer.Advance(0); }
        }
        if (_skeleton != null)
            _instance.Basis = new Basis(Vector3.Right, Mathf.Pi / 2) * _instance.Basis;
        float scale = (float)(_def.VisualScale * w.Guardian.Scale * GuardianDef.BodySizeMultiplier);
        _instance.Scale *= scale;
        ApplySurfaceMaterials(_instance);
        Mark("SurfaceMaterials");
        _groundOffset = Math.Max(0, -LowestPoint(_instance, Transform3D.Identity));
        _instance.Position += Vector3.Up * _groundOffset;
        Mark("LowestPoint");

        // Enable and review each species as its cinematic pass is completed.
        if (_skeleton != null && _def.Archetype is "woodland_omnivore" or "great_ground" or "armored_forest" or "quilled_arboreal" or "canopy_serpent" or "crested_canopy")
        {
            ShrubContact = new GuardianVegetationContact { Name = "GuardianShrubContact", Flora = Vegetation };
            AddChild(ShrubContact);
            if (Array.IndexOf(Main.UserArgs, "--guardian-stem-audit") >= 0) ShrubContact.BeginStemAudit(512);
            AppendageContact = new GuardianAppendageModifier { Name = "GuardianTailContact", World = w,
                Renderer = this, Vegetation = ShrubContact };
            if (_def.Archetype == "woodland_omnivore")
                AppendageContact.TipSegmentLength = .123096f; // Revised source leaf, Y compressed by.88.
            if (_def.Archetype == "great_ground")
            { AppendageContact.RootRadius = .0855f; AppendageContact.TipRadius = .001f;
                // Sixteen deformation spans follow the authored compressed tail profile.
                AppendageContact.TipSegmentLength = 0.09206878f;
                AppendageContact.LateralRadii = new[] { 0.0855000f, 0.0798462f, 0.0742300f, 0.0686538f, 0.0631203f, 0.0576329f, 0.0521951f, 0.0468116f, 0.0414876f, 0.0362300f, 0.0310471f, 0.0259503f, 0.0209551f, 0.0160844f, 0.0113764f, 0.0069077f, 0.0010000f };
                AppendageContact.GroundRadii = new[] { 0.1425000f, 0.1330770f, 0.1237166f, 0.1144229f, 0.1052005f, 0.0960548f, 0.0869919f, 0.0780193f, 0.0691460f, 0.0603833f, 0.0517452f, 0.0432506f, 0.0349252f, 0.0268074f, 0.0189606f, 0.0115128f, 0.0010000f }; }
            if (_def.Archetype == "quilled_arboreal")
            {
                AppendageContact.RootRadius = .059f; AppendageContact.TipRadius = .007684f;
                AppendageContact.TipSegmentLength = .078447425f;
                AppendageContact.BodyBrushRadius = .24f;
            }
            if (_def.Archetype == "armored_forest")
            {
                AppendageContact.FirstTailIndex = 0; AppendageContact.RootRadius = .135f; AppendageContact.TipRadius = .008f; AppendageContact.TipSegmentLength = .060f;
                AppendageContact.BodyBrushRadius = .22f;
                // Flattened armored tail: broad in plan, thinner underneath.
                // Profile cross-sections plus 7mm cover allowance, not width spheres.
                AppendageContact.GroundRadii = new[] { .100f, .085f, .072f, .060f, .048f, .036f, .026f, .016f, .010f };
            }
            if (_def.Archetype == "crested_canopy")
            {
                // Four authored feather-fan joints, including the true leaf.
                AppendageContact.FirstTailIndex = 0;
                AppendageContact.RootRadius = .045f; AppendageContact.TipRadius = .014f;
                AppendageContact.TipSegmentLength = .1308625f;
                AppendageContact.BodyBrushRadius = .18f;
                AppendageContact.LateralRadii = new[] { .045f, .08f, .12f, .14f, .14f };
                AppendageContact.GroundRadii = new[] { .018f, .014f, .010f, .006f, .004f };
            }
            bool continuousBody = _def.Archetype == "canopy_serpent";
            if (continuousBody) ConfigureBoaContact(AppendageContact);
            else if (!(Array.IndexOf(Main.UserArgs, "--guardian-review") >= 0
                && Array.IndexOf(Main.UserArgs, "--guardian-model-no-contact") >= 0))
            {
                LegContact = new GuardianTerrainLegModifier { Name = "GuardianFootContact", Renderer = this };
                _skeleton.AddChild(LegContact);
            }
            _skeleton.AddChild(AppendageContact);
            Mark("ContactModifiers");
            SkinContact = new GuardianSkinContact(_instance, _skeleton, continuousBody: continuousBody, chainPrefix: continuousBody ? "Body" : "Tail", biped: _def.Archetype == "crested_canopy");
            if (Array.IndexOf(Main.UserArgs, "--guardian-lowest-point-audit") >= 0)
                SkinContact.LowestPointAuditBudget = 256;
            Mark("SkinContact");
            GD.Print("GUARDIAN_SKIN_CONTACT " + _def.Archetype + " valid=" + SkinContact.Valid + " " + SkinContact.Diagnostic);
        }

        if (_skeleton != null)
        {
            BreathingModifier = new GuardianBreathingModifier { Name = "GuardianBreathingModifier" };
            _skeleton.AddChild(BreathingModifier);

            HeadTracker = new GuardianHeadTracker { Name = "GuardianHeadTracker" };
            _skeleton.AddChild(HeadTracker);

            SecondaryMotion = new GuardianSecondaryMotion { Name = "GuardianSecondaryMotion" };
            _skeleton.AddChild(SecondaryMotion);
        }

        WaterContact = new GuardianWaterContact { Name = "GuardianWaterContact", Renderer = this, World = w };
        AddChild(WaterContact);

        // Initialize motion track
        var startPos = Bridge.V(w.Guardian.Position);
        _prevPos = _curPos = _shownPos = startPos;
        _prevHeading = _curHeading = _shownHeading = _prevShownHeading = (float)w.Guardian.Heading;
        _curT = w.Clock.SimSeconds;
        _prevT = _curT - VivariumWorld.Cadence.FaunaBehaviour * Vivarium.Sim.Time.SimClock.FixedStepSeconds;
        _currentAnim = "";
        Mark("Total");
    }

    // Review only: replace a single mesh resource while retaining the original
    // imported scene, skeleton, skin binding, face and animation player.
    private static void LoadReviewCoat(Node3D instance, string path)
    {
        using var document = new GltfDocument();
        using var state = new GltfState();
        if (document.AppendFromFile(path, state) != Error.Ok)
            throw new InvalidOperationException("Could not load coat candidate: " + path);
        var candidate = document.GenerateScene(state);
        try
        {
            MeshInstance3D? FindFur(Node root)
            {
                if (root is MeshInstance3D mesh && mesh.Mesh != null)
                    for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
                        if (mesh.GetActiveMaterial(i)?.ResourceName == "CoatiFineFur") return mesh;
                foreach (Node child in root.GetChildren())
                    if (FindFur(child) is { } result) return result;
                return null;
            }
            var target = FindFur(instance) ?? throw new InvalidOperationException("Original fur missing");
            var source = FindFur(candidate) ?? throw new InvalidOperationException("Candidate fur missing");
            if (source.Mesh.GetSurfaceCount() != target.Mesh.GetSurfaceCount())
                throw new InvalidOperationException("Coat surface contract changed");
            target.Mesh = source.Mesh;
        }
        finally { candidate.Free(); }
    }

    private static void ConfigureBoaContact(GuardianAppendageModifier contact)
    {
        // Profile-centered solo v7 rig; exact33stations plus the true terminal leaf.
        contact.ContinuousGroundedBody = true; contact.ChainPrefix = "Body";
        contact.ChainIndexFormat = "D2"; contact.FirstTailIndex = 0;
        contact.TipSegmentLength = 0.006000212394f;
        contact.RootRadius = 0.0419885001f; contact.TipRadius = .003f;
        contact.BodyBrushRadius = .105f;
        contact.LateralRadii = new[] { 0.0419885001f, 0.0470271221f, 0.051122084f, 0.0590299979f, 0.0647173169f, 0.0666023844f, 0.0713485516f, 0.0760184807f, 0.0776514904f, 0.0791961493f, 0.0817319441f, 0.0836959296f, 0.0839308247f, 0.0836628962f, 0.0832969434f, 0.082997007f, 0.0827303847f, 0.079570254f, 0.0746481112f, 0.0707969972f, 0.0693451466f, 0.0643679949f, 0.0589223608f, 0.0568638163f, 0.0507507318f, 0.0449710634f, 0.0415606457f, 0.033094867f, 0.0286559611f, 0.0220217829f, 0.0150052151f, 0.00945751123f, 0.00217846875f, 0.003f };
        contact.GroundRadii = new[] { 0.0144f, 0.040244081f, 0.056619353f, 0.063017101f, 0.067618318f, 0.069380629f, 0.073860392f, 0.078268196f, 0.079802466f, 0.081052143f, 0.08310368f, 0.084692605f, 0.084807928f, 0.084157641f, 0.083269439f, 0.082541466f, 0.082203829f, 0.079860239f, 0.076209919f, 0.073353888f, 0.071948366f, 0.066243917f, 0.060002527f, 0.057679457f, 0.051085223f, 0.044850648f, 0.041338213f, 0.032662715f, 0.028115153f, 0.0215093f, 0.014522689f, 0.01001142f, 0.0041224488f, 0.00408f };
    }

    private static Shader? _guardianShader;
    private static Shader GuardianShader => _guardianShader ??= GD.Load<Shader>("res://Shaders/guardian_surface.gdshader");

    private static int ModeFor(string name)
    {
        string n = name.ToLowerInvariant();
        if (n.Contains("iris") || n.Contains("eye") || n.Contains("cornea") || n.Contains("pupil")) return 4;
        if (n.Contains("keratin") || n.Contains("scute") || n.Contains("claw") || n.Contains("horn") || n.Contains("quill") || n.Contains("tooth") || n.Contains("ivory") || n.Contains("whisker") || n.Contains("vibrissae") || n.Contains("scale") || n.Contains("labial")) return 0;
        if (n.Contains("bristle") || n.Contains("plumage")) return 2;
        if (n.Contains("fur") || n.Contains("underfur") || n.Contains("underplumage") || n.Contains("crest") || n.Contains("contour") || n.Contains("buff") || n.Contains("flight") || n.Contains("streak")) return 1;
        if (n.Contains("skin") || n.Contains("mouth") || n.Contains("tongue") || n.Contains("nasal") || n.Contains("pigment") || n.Contains("lip") || n.Contains("pbr") || n.Contains("facial")) return 3;
        return 3;
    }

    private void ApplySurfaceMaterials(Node node)
    {
        if (node is MeshInstance3D mesh && mesh.Mesh != null)
        {
            for (int i = 0; i < mesh.Mesh.GetSurfaceCount(); i++)
            {
                if (mesh.GetActiveMaterial(i) is not StandardMaterial3D source) continue;
                if (source.ResourceName.Equals("EyeHighlight", StringComparison.OrdinalIgnoreCase))
                {
                    if (mesh.Mesh.GetSurfaceCount() == 1) mesh.Visible = false;
                    else mesh.SetSurfaceOverrideMaterial(i, new StandardMaterial3D
                    {
                        Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
                        AlbedoColor = new Color(0f, 0f, 0f, 0f)
                    });
                    continue;
                }
                var material = new ShaderMaterial { Shader = GuardianShader, ResourceName = source.ResourceName };
                material.SetShaderParameter("mode", ModeFor(source.ResourceName));
                material.SetShaderParameter("base_color", source.AlbedoColor);
                material.SetShaderParameter("roughness_mul", Math.Clamp(source.Roughness <= 0 ? 1f : source.Roughness, 0.05f, 1f));
                material.SetShaderParameter("metallic", source.Metallic);
                material.SetShaderParameter("specular", 0.5f);
                if (source.AlbedoTexture != null)
                {
                    material.SetShaderParameter("albedo_texture", source.AlbedoTexture);
                    material.SetShaderParameter("has_albedo_texture", true);
                }
                if (source.NormalTexture != null)
                {
                    material.SetShaderParameter("normal_texture", source.NormalTexture);
                    material.SetShaderParameter("has_normal_texture", true);
                }
                if (source.RoughnessTexture != null)
                {
                    material.SetShaderParameter("roughness_texture", source.RoughnessTexture);
                    material.SetShaderParameter("has_roughness_texture", true);
                }
                mesh.SetSurfaceOverrideMaterial(i, material);
                _guardianMaterials.Add(material);
            }
        }
        foreach (var child in node.GetChildren()) ApplySurfaceMaterials(child);
    }

    private static float LowestPoint(Node3D node, Transform3D parent)
    {
        var transform = parent * node.Transform;
        float lowest = float.PositiveInfinity;
        if (node is MeshInstance3D mesh && mesh.Mesh != null)
        {
            var box = mesh.Mesh.GetAabb();
            for (int i = 0; i < 8; i++) lowest = Math.Min(lowest, (transform * box.GetEndpoint(i)).Y);
        }
        foreach (var child in node.GetChildren())
            if (child is Node3D spatial) lowest = Math.Min(lowest, LowestPoint(spatial, transform));
        return float.IsFinite(lowest) ? lowest : 0;
    }

    private static Node3D CreateFallbackMesh(GuardianDef def)
    {
        var root = new Node3D { Name = "GuardianFallback" };
        var mi = new MeshInstance3D { Name = "BodyMesh" };

        var box = new BoxMesh
        {
            Size = new Vector3(
                (float)(0.35 * def.VisualScale),
                (float)(0.25 * def.VisualScale),
                (float)(0.90 * def.VisualScale)
            )
        };
        var mat = new StandardMaterial3D
        {
            AlbedoColor = Bridge.C(new[] { def.BaseColor.X, def.BaseColor.Y, def.BaseColor.Z }),
            Roughness = 0.6f
        };
        mi.Mesh = box;
        mi.MaterialOverride = mat;
        root.AddChild(mi);
        return root;
    }

    public override void _Process(double delta)
    {
        using var timing=Vivarium.Game.App.FrameProfiler.Measure("Guardian");
        if (_w == null || _w.Guardian == null || _instance == null) return;

        var g = _w.Guardian;
        var targetPos = Bridge.V(g.Position);
        double now = _w.Clock.SimSeconds;

        // Position & heading tracking
        if (now > _curT && (_curPos.DistanceSquaredTo(targetPos) > 1e-6 || Math.Abs(_curHeading - g.Heading) > 1e-5))
        {
            _prevPos = _curPos;
            _prevHeading = _curHeading;
            _prevT = _curT;
            _curPos = targetPos;
            _curHeading = (float)g.Heading;
            _curT = now;
        }

        double span = Math.Max(1e-4, _curT - _prevT);
        double renderTime = now + _w.Scheduler.TickFraction * Vivarium.Sim.Time.SimClock.FixedStepSeconds
            - VivariumWorld.Cadence.FaunaBehaviour * Vivarium.Sim.Time.SimClock.FixedStepSeconds;
        float alpha = (float)Math.Clamp((renderTime - _prevT) / span, 0.0, 1.0);

        _shownPos = _prevPos.Lerp(_curPos, alpha);
        _shownHeading = Mathf.LerpAngle(_prevHeading, _curHeading, alpha);

        // Frustum culling (disabled in headless mode where Camera projection is uninitialized)
        if (Camera != null && DisplayServer.GetName() != "headless")
        {
            var planes = Camera.GetFrustum();
            bool outside = false;
            foreach (var plane in planes)
            {
                if (plane.DistanceTo(_shownPos) > 1.5f)
                {
                    outside = true;
                    break;
                }
            }

            if (outside)
            {
                _instance.Visible = false;
                OffScreen = true;
                Drawn = false;
                return;
            }
        }

        _instance.Visible = true;
        Drawn = true;
        OffScreen = false;

        // Apply spatial transform
        // Orientation: Godot yaw is negated sim heading
        Basis rot = Bridge.Yaw(_shownHeading + Math.PI / 2);

        // Terrain normal tilt
        var p = new Vec2(_shownPos.X, _shownPos.Z);
        // Root shelves are part of the support surface, rather than bare terrain.
        const double sample = 0.18;
        var normal = new Vector3(
            (float)(_w.GroundHeight(p - new Vec2(sample, 0)) - _w.GroundHeight(p + new Vec2(sample, 0))),
            (float)(2 * sample),
            (float)(_w.GroundHeight(p - new Vec2(0, sample)) - _w.GroundHeight(p + new Vec2(0, sample)))).Normalized();
        normal = Vector3.Up.Lerp(normal, 0.45f).Normalized();
        if (ReviewSupportPlaneHeight.HasValue) normal = Vector3.Up;
        _shownPos.Y = SupportHeight(_shownPos);
        if (normal.Y > 0.4f && normal.LengthSquared() > 0.01f)
        {
            Vector3 forward = -rot.Z;
            Vector3 up = normal.Normalized();
            Vector3 alignedForward = forward - up * forward.Dot(up);
            if (alignedForward.LengthSquared() > 1e-4f)
            {
                alignedForward = alignedForward.Normalized();
                Vector3 alignedRight = alignedForward.Cross(up);
                if (alignedRight.LengthSquared() > 1e-4f)
                {
                    alignedRight = alignedRight.Normalized();
                    rot = new Basis(alignedRight, up, -alignedForward);
                }
            }
        }

        GlobalTransform = new Transform3D(rot, _shownPos);
        BodyTrunkCorrection = 0;
        if (AppendageContact != null && _skeleton != null && !ReviewSupportPlaneHeight.HasValue && ReviewGroundSample == null && _w.PilotTree != null)
        {
            double treeDist = Vec2.Distance(new Vec2(_shownPos.X, _shownPos.Z), _w.PilotTree.Anchor);
            double treeReach = _w.PilotTree.Def.TrunkRadius + 3.0;
            // The old center-only .35 m simulation margin can put the long
            // muzzle into a trunk. Resolve the rendered body footprint too.
            EnsureBoneCache();
            for (int iteration = 0; iteration < 8; iteration++)
            {
                Vector3 correction = Vector3.Zero;
                for (int bi = 0; bi < BodyBoneNames.Length; bi++)
                {
                    string name = BodyBoneNames[bi];
                    int bone = _bodyBoneIdx[bi]; if (bone < 0) continue;
                    var body = _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(bone).Origin;
                    if (name == "Snout" || (name == "Head" && _def?.Archetype == "great_ground"))
                        body += (_skeleton.GlobalBasis * _skeleton.GetBoneGlobalPose(bone).Basis.Y).Normalized()
                            * (name == "Snout" ? .245153f : .380526f) * _skeleton.GlobalBasis.Scale.X;
                    float radius = (name == "Snout" ? .04f : name == "Head" ? .09f : .14f) * _skeleton.GlobalBasis.Scale.X;
                    var hit = _w.PilotTree.Penetration(new Vec3(body.X, body.Y, body.Z), radius);
                    var push = ShrubContact == null ? Vector3.Zero : ShrubContact.Resolve(body, radius, true) - body;
                    if (hit.Depth > 0 && hit.Normal.Y < .5f) push += Bridge.V(hit.Normal) * (float)hit.Depth;
                    if (push.LengthSquared() > correction.LengthSquared()) correction = push;
                }
                // A pinned appendage cannot escape a woody core by bending
                // distal joints. Include its attachment in root avoidance so
                // contact does not stretch the first segment against the tree.
                int attachment = _attachmentBoneIdx;
                if (attachment >= 0)
                {
                    var point = _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(attachment).Origin;
                    float radius = AppendageContact.RootRadius * _skeleton.GlobalBasis.Scale.X;
                    var hit = _w.PilotTree.Penetration(new Vec3(point.X, point.Y, point.Z), radius);
                    var push = ShrubContact == null ? Vector3.Zero : ShrubContact.Resolve(point, radius, true) - point;
                    if (hit.Depth > 0 && hit.Normal.Y < .5f) push += Bridge.V(hit.Normal) * (float)hit.Depth;
                    if (push.LengthSquared() > correction.LengthSquared()) correction = push;
                }
                // Wide paws and curved claws can cross a trunk while the
                // ankle and torso centers remain outside. Include evaluated
                // contact vertices before solving reachable ground targets.
                if (SkinContact?.Valid == true && treeDist < treeReach)
                    foreach (string region in new[] { "Hand_L", "Hand_R", "Foot_L", "Foot_R", "Body", "Head" })
                    {
                        // The lowest point by ground clearance, measured the way
                        // the full evaluation measures it, without asking the
                        // ground about every sample.
                        if (!SkinContact.TryLowestPoint(region, SupportHeight, out var point)) continue;
                        var hit = _w.PilotTree.Penetration(new Vec3(point.X, point.Y, point.Z), .025f);
                        if (hit.Depth > 0 && hit.Normal.Y < .5f)
                        {
                            var push = Bridge.V(hit.Normal) * ((float)hit.Depth + .005f);
                            if (push.LengthSquared() > correction.LengthSquared()) correction = push;
                        }
                    }
                if (correction.LengthSquared() < 1e-8f) break;
                GlobalPosition += correction;
                GlobalPosition = new Vector3(GlobalPosition.X, SupportHeight(GlobalPosition), GlobalPosition.Z);
                // Avoidance can move the body onto a different root shelf.
                // Re-sample its tilt there before fitting short limbs, rather
                // than retaining the normal at the simulation position.
                GlobalBasis = TerrainBasis(GlobalPosition, Bridge.Yaw(_shownHeading + Math.PI / 2));
                BodyTrunkCorrection += correction.Length();
            }
        }
        if (ShrubContact != null) { ShrubContact.Focus = GlobalPosition; ShrubContact.Flora = Vegetation; }

        // Sample the animated feet against the same root/prop support as the sim.
        // A single height at the pelvis lets feet disappear on uneven buttresses.
        if (_skeleton != null && LegContact == null)
        {
            float lift = 0;
            EnsureBoneCache();
            foreach (int bone in _handFootBoneIdx)
            {
                var rest = _skeleton.GlobalTransform * _skeleton.GetBoneGlobalRest(bone).Origin;
                var foot = _skeleton.GlobalTransform * _skeleton.GetBoneGlobalPose(bone).Origin;
                // The ankle-to-sole distance is measured in the neutral mesh pose.
                foot.Y -= Math.Max(0, rest.Y - GlobalPosition.Y);
                var xz = new Vec2(foot.X, foot.Z);
                if (_w.PilotTree?.BlocksTrunk(xz) == true) continue;
                lift = Math.Max(lift, SupportHeight(foot) - foot.Y);
            }
            GlobalPosition += Vector3.Up * Math.Min(lift, 0.4f);
        }

        // Update procedural modifiers
        if (BreathingModifier != null)
            BreathingModifier.Activity = g.Activity;

        if (HeadTracker != null)
        {
            Vector3? targetLookPoint = ResolveInterestPoint(g);
            bool fast = g.Activity == GuardianActivity.Retreat || (g.Activity is GuardianActivity.Forage or GuardianActivity.Explore && _curPos.DistanceTo(_prevPos) > 1.5f);
            HeadTracker.SetTarget(targetLookPoint, g.Activity, fast);
        }

        float headingDelta = Mathf.AngleDifference(_prevShownHeading, _shownHeading);
        float angularVelocity = delta > 1e-5 ? (float)(headingDelta / delta) : 0f;
        _prevShownHeading = _shownHeading;
        if (SecondaryMotion != null)
            SecondaryMotion.AngularVelocity = angularVelocity;

        WaterContact?.UpdateContact(delta);

        // Wetness is gated by the per-fragment waterline. Drying retains the
        // highest contacted line rather than painting the whole animal wet.
        float wetness = WaterContact?.Wetness ?? 0f;
        float waterLevel = WaterContact?.WetHeightWorld ?? -10000f;
        // Skeletal breathing already moves the imported skin and its samplers.
        // Do not add a second uniform pulse to every vertex.
        Vector3 contactSphere = Vector3.Zero;
        float contactRadius = 0f;
        if (SkinContact is { Valid: true, GpuDeformationEnabled: true })
        {
            // One actual evaluated underside contact patch. Never inflate the
            // entire body to hide a bad foothold or an unreachable nasal target.
            var underside = SkinContact.Evaluate("Body", SupportHeight);
            if (underside.Valid && underside.Minimum < 0.015f && underside.Minimum > -0.04f)
            {
                contactRadius = 0.035f;
                contactSphere = new Vector3(underside.LowestPoint.X,
                    SupportHeight(underside.LowestPoint) - 0.025f, underside.LowestPoint.Z);
            }
        }
        float daylight = EnvironmentRig.Instance?.Sun is { } sun
            ? Math.Clamp(sun.LightEnergy / 1.2f, 0f, 1f) : 1f;
        foreach (var mat in _guardianMaterials)
        {
            mat.SetShaderParameter("wetness", wetness);
            mat.SetShaderParameter("water_level", waterLevel);
            mat.SetShaderParameter("contact_sphere", contactSphere);
            mat.SetShaderParameter("contact_radius", contactRadius);
            mat.SetShaderParameter("contact_max_displacement", GuardianSkinContact.MaximumGpuCoatDisplacement);
            mat.SetShaderParameter("day_night", daylight);
        }

        // Manage animation state
        UpdateAnimationState(g);
    }

    private Vector3? ResolveInterestPoint(GuardianIndividual g)
    {
        var pos = Bridge.V(g.Position);
        var target = Bridge.V(g.TargetPosition);

        if (g.Activity is GuardianActivity.Investigate or GuardianActivity.Alert)
        {
            if (target.DistanceSquaredTo(Vector3.Zero) > 1e-4f && target.DistanceSquaredTo(pos) > 0.01f)
                return target;
            return pos - GlobalBasis.Z.Normalized() * 2.0f;
        }

        if (g.Activity is GuardianActivity.Feed or GuardianActivity.Drink)
        {
            if (target.DistanceSquaredTo(Vector3.Zero) > 1e-4f)
                return target;
            return pos - GlobalBasis.Z.Normalized() * 0.5f;
        }

        if (g.Activity is GuardianActivity.Explore or GuardianActivity.Rest)
        {
            if (target.DistanceSquaredTo(Vector3.Zero) > 1e-4f && target.DistanceSquaredTo(pos) > 0.04f)
                return target;
        }

        return null;
    }

    private string ResolvePlayAnim(GuardianIndividual g)
    {
        if (_animPlayer == null) return "idle";
        foreach (string name in new[] { "play_object", "play_sprint", "play_roll", "play_flutter", "play_hang", "play_strike", "play" })
        {
            if (_animPlayer.HasAnimation(name)) return name;
        }
        return "idle";
    }

    private string ResolveInvestigateAnim(GuardianIndividual g)
    {
        if (_animPlayer == null) return "idle";
        foreach (string name in new[] { "investigate_sniff", "investigate_tongue", "investigate_dig", "investigate_scan", "investigate_look", "investigate" })
        {
            if (_animPlayer.HasAnimation(name)) return name;
        }
        return "idle";
    }

    private Basis TerrainBasis(Vector3 point, Basis yaw)
    {
        if (_w == null || ReviewSupportPlaneHeight.HasValue) return yaw;
        const float sample = .18f;
        var normal = new Vector3(SupportHeight(point - Vector3.Right * sample) - SupportHeight(point + Vector3.Right * sample),
            2 * sample, SupportHeight(point - Vector3.Back * sample) - SupportHeight(point + Vector3.Back * sample)).Normalized();
        var up = Vector3.Up.Lerp(normal, .45f).Normalized();
        if (up.Y <= .4f || up.LengthSquared() < .01f) return yaw;
        var forward = -yaw.Z; forward -= up * forward.Dot(up);
        if (forward.LengthSquared() < 1e-4f) return yaw;
        forward = forward.Normalized(); var right = forward.Cross(up).Normalized();
        return new Basis(right, up, -forward);
    }

    private void UpdateAnimationState(GuardianIndividual g)
    {
        if (_animPlayer == null) return;

        bool HasAnim(string name) => _animPlayer.HasAnimation(name);

        string targetAnim = g.Activity switch
        {
            GuardianActivity.Rest => "rest",
            GuardianActivity.Sleep => HasAnim("sleep") ? "sleep" : "rest",
            GuardianActivity.Settle => HasAnim("settle") ? "settle" : "idle",
            GuardianActivity.Stretch => HasAnim("stretch") ? "stretch" : "idle",
            GuardianActivity.Feed => "feed",
            GuardianActivity.Drink => HasAnim("drink") ? "drink" : (HasAnim("feed") ? "feed" : "idle"),
            GuardianActivity.Bathe => HasAnim("bathe") ? "bathe" : "idle",
            GuardianActivity.ShakeOff => HasAnim("shake_off") ? "shake_off" : "idle",
            GuardianActivity.Bask => HasAnim("bask") ? "bask" : "rest",
            GuardianActivity.Dig => HasAnim("dig") ? "dig" : "feed",
            GuardianActivity.Groom => HasAnim("groom_detail") ? "groom_detail" : (HasAnim("groom") ? "groom" : "idle"),
            GuardianActivity.Yawn => HasAnim("yawn") ? "yawn" : "idle",
            GuardianActivity.Play => ResolvePlayAnim(g),
            GuardianActivity.Investigate => ResolveInvestigateAnim(g),
            GuardianActivity.Alert => HasAnim("alert") ? "alert" : "idle",
            GuardianActivity.ScentMark => HasAnim("scent_mark") ? "scent_mark" : "idle",
            GuardianActivity.Forage or GuardianActivity.Explore or GuardianActivity.Climb or GuardianActivity.ReturnHome => "walk",
            GuardianActivity.Retreat => HasAnim("run") ? "run" : "walk",
            _ => "idle"
        };

        if (targetAnim == "walk" && g.Locomotion == LocomotionMode.Serpentine && HasAnim("slither"))
            targetAnim = "slither";

        if (!HasAnim(targetAnim)) targetAnim = "idle";

        float strideScale = targetAnim is "walk" or "slither" ? (float)(1 / Math.Max(0.1, g.Scale)) : 1;

        float activitySpeed = g.Activity switch
        {
            GuardianActivity.Sleep => 0.4f,
            GuardianActivity.Bask => 0.3f,
            GuardianActivity.Settle => 0.7f,
            GuardianActivity.Retreat => 1.8f,
            GuardianActivity.Alert => 1.0f,
            _ => 1.0f
        };

        _animPlayer.SpeedScale = _w!.Clock.Paused ? 0 : (float)_w.Clock.SpeedMultiplier * activitySpeed * strideScale;

        if (targetAnim != _currentAnim && HasAnim(targetAnim))
        {
            float blend = targetAnim is "alert" or "shake_off" ? 0.1f : 0.35f;
            _animPlayer.Play(targetAnim, customBlend: blend);
            _currentAnim = targetAnim;
        }
    }
}
