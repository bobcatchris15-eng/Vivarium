using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.Render;
using Vivarium.Sim.Content;
using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;
using Vivarium.Sim.Geometry;
using Vivarium.Sim.World;

namespace Vivarium.Game.App;

/// <summary>
/// Grows a vine in a fixture world and renders the result, so the stem's size, its foliage and its habit can be
/// judged against each other and against the host it climbs.
/// </summary>
public partial class VineReview : Node3D
{
    public ContentLibrary Content { get; set; } = null!;
    public string SpeciesId { get; set; } = "clinglace";
    public string OutDir { get; set; } = "build/vine-review";
    public float Days { get; set; } = 40f;
    public int Seed { get; set; } = 62;
    /// <summary>Frame the growing tip instead of the whole plant.</summary>
    public bool Tip { get; set; }
    /// <summary>Camera azimuth in degrees.</summary>
    public float Angle { get; set; } = 35f;
    public float Zoom { get; set; } = 1f;
    /// <summary>Grow the vines in a whole populated habitat instead of a single plant on one host.</summary>
    public bool Habitat { get; set; }

    public override void _Ready() => _ = CaptureAsync();

    private async Task CaptureAsync()
    {
        try
        {
            var sp = Content.FloraOrThrow(SpeciesId);
            var cd = sp.Climber ?? throw new InvalidOperationException($"{SpeciesId} is not a climber");

            // 1. A small world with one host to climb.
            var d = Content.PresetOrThrow("default");
            d.Name = "VineReview";
            d.Seed = (ulong)Seed;
            d.Diameter = 16;
            d.BioAcceleration = 1;
            d.Placement = new PlacementProfile { Rocks = 0, Logs = 0, GravelPatches = 0 };
            var w = VivariumWorld.Create(Content, d, populate: Habitat);

            FloraIndividual? support = null;
            FloraSpeciesDef? supportSp = null;
            FloraIndividual vine;
            if (Habitat)
            {
                // A whole habitat: let the vines find their own hosts.
                vine = w.FloraSystem.Establish(sp, new Vec2(0, 0), "vine review", sp.MaxBiomass * 0.7);
            }
            else
            {
                supportSp = Content.FloraOrThrow("shadebell");
                support = w.FloraSystem.Establish(supportSp, new Vec2(1.8, 0), "vine review host", supportSp.MaxBiomass);
                support.Age = supportSp.MaturityAge;
                vine = w.FloraSystem.Establish(sp, Vec2.Zero, "vine review", sp.MaxBiomass * 0.7);
            }

            // 2. Grow it.
            int steps = (int)(Days * 86400 / 600);
            for (int i = 0; i < steps; i++) w.FloraSystem.Step(600);

            if (Habitat)
            {
                // In a habitat the interesting vine is the best established one, on whatever host it found.
                var best = w.Flora.Items
                    .Where(f => f.SpeciesId == sp.Id && f.ClimberSegments is { Count: > 20 })
                    .OrderByDescending(f => f.ClimberSegments!.Count(s => s.Attached))
                    .FirstOrDefault();
                if (best == null) throw new InvalidOperationException("no vine established in the habitat");
                vine = best;
                var hostId = best.ClimberSegments!.First(s => s.Attached).HostId;
                support = w.Flora.Get(hostId);
                if (support != null) supportSp = Content.FloraOrThrow(support.SpeciesId);
            }

            var grown = w.Flora.Get(vine.Id);
            var segs = grown?.ClimberSegments;
            if (segs == null || segs.Count < 2) throw new InvalidOperationException("the vine did not grow");
            GD.Print($"VINE_GROWTH {SpeciesId} nodes={segs.Count} attached={segs.Count(s => s.Attached)} " +
                     $"branches={segs.Count(s => s.ShootOrder > 0)} baseR={segs[0].Radius:0.0000} " +
                     $"capR={cd.BaseRadius:0.0000} topY={segs.Max(s => s.Position.Y):0.00} " +
                     $"plants={w.Flora.Items.Count(f => f.SpeciesId == sp.Id)}");

            // 3. Build the scene.
            GetWindow().Size = new Vector2I(1024, 1024);
            var environment = new EnvironmentRig { Name = "Environment" };
            AddChild(environment);
            environment.ApplyQuality(2);

            var groundSize = Habitat ? 26f : 14f;
            var ground = new MeshInstance3D
            {
                Name = "Ground",
                Mesh = new PlaneMesh { Size = new Vector2(groundSize, groundSize) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.33f, 0.31f, 0.25f), Roughness = 1.0f },
                Position = new Vector3((float)segs[0].Position.X, 0, (float)segs[0].Position.Z),
            };
            AddChild(ground);

            // The host, so the vine can be judged against the thing it climbs.
            if (support != null && supportSp != null)
            {
                var (trunkR, trunkGround, trunkTop) = w.FloraSystem.TreeTrunkProfile(support, supportSp, 1.0);
                AddChild(new MeshInstance3D
                {
                    Name = "HostTrunk",
                    Mesh = new CylinderMesh { TopRadius = (float)trunkR, BottomRadius = (float)(trunkR * 1.15f), Height = (float)(trunkTop - trunkGround), RadialSegments = 24 },
                    MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.26f, 0.20f, 0.15f), Roughness = 0.95f },
                    Position = new Vector3((float)support.X, (float)((trunkGround + trunkTop) * 0.5), (float)support.Z),
                });
            }

            // 4. The stem, exactly as the renderer draws it: one tapering tube per internode.
            var mat = Bridge.Shader("res://Shaders/flora.gdshader");
            mat.SetShaderParameter("stiffness", 2.0f);
            mat.SetShaderParameter("sway", 0.0f);
            Bridge.BindSurface(mat, "moss", Bridge.Surfaces.Moss);
            FloraSurfaceProfiles.Bind(mat, sp, photographedLeaves: true);

            double ratio = Math.Pow(cd.StemTaper, cd.SegmentLength);
            var stemPath = new List<Vec3>();
            var stemRad = new List<double>();
            const double overlap = 0.10;
            for (int i = 0; i <= 3; i++)
            {
                double x = -overlap + (1.0 + 2 * overlap) * (i / 3.0);
                stemPath.Add(new Vec3(x, 0, 0));
                stemRad.Add(Math.Pow(ratio, x));
            }
            var stemMesh = new MeshData();
            var stemCol = Primitives.Mix(sp.Color, sp.Color2, 0.3);
            Primitives.Tube(stemMesh, stemPath, stemRad, 7, (i, v) => (stemCol, 1, i, v, 0, 0));
            var stemArray = Bridge.ToArrayMesh(stemMesh, mat);

            // 5. The foliage, as the renderer draws it: the node mesh at spaced nodes.
            ulong nodeSeed = Rng.Mix(Hash.Fnv1a64("flora.visual." + sp.Id), 0x9E3779B97F4A7C15UL);
            var nodeMesh = Bridge.ToArrayMesh(OrganismMeshes.ClimberNode(sp, nodeSeed, true), mat);
            var tipMesh = Bridge.ToArrayMesh(OrganismMeshes.ClimberNodeTip(sp, nodeSeed), mat);

            var stemMulti = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = stemArray,
                InstanceCount = 0,
            };
            var leafMulti = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = nodeMesh,
                InstanceCount = 0,
            };
            var tendrilMulti = new MultiMesh
            {
                TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
                Mesh = tipMesh,
                InstanceCount = 0,
            };
            var stemInst = new List<Transform3D>();
            var leafInst = new List<Transform3D>();
            var tendrilInst = new List<Transform3D>();

            // A node with no child is the growing tip of its shoot.
            bool[] hasChild = new bool[segs.Count];
            for (int s = 0; s < segs.Count; s++)
            {
                int p = segs[s].ParentIndex;
                if (p >= 0 && p < segs.Count) hasChild[p] = true;
            }

            float leafSpacing = (float)Math.Max(0.11, cd.SegmentLength * 3.0);
            var lastLeaf = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            for (int s = 1; s < segs.Count; s++)
            {
                var seg = segs[s];
                var parent = segs[seg.ParentIndex];
                var a = new Vector3((float)parent.Position.X, (float)parent.Position.Y, (float)parent.Position.Z);
                var b = new Vector3((float)seg.Position.X, (float)seg.Position.Y, (float)seg.Position.Z);
                var dir = b - a;
                if (dir.Length() < 1e-5f) continue;

                // Stem: a tapering tube from the parent's radius to this node's.
                var nrm = new Vector3((float)seg.Normal.X, (float)seg.Normal.Y, (float)seg.Normal.Z);
                if (nrm.LengthSquared() < 1e-8f) nrm = Vector3.Up; else nrm = nrm.Normalized();
                var xAxis = dir;
                var zAxis = xAxis.Cross(nrm);
                if (zAxis.LengthSquared() < 1e-8f) zAxis = xAxis.Cross(Vector3.Up);
                zAxis = zAxis.Normalized();
                var yAxis = zAxis.Cross(xAxis).Normalized();
                float thick = (float)parent.Radius;
                stemInst.Add(new Transform3D(new Basis(xAxis, yAxis * thick, zAxis * thick), a));

                // Foliage on the upper side of the stem, and a bare tendril at the growing tip.
                var fwd = new Vector3((float)seg.Forward.X, (float)seg.Forward.Y, (float)seg.Forward.Z);
                if (fwd.LengthSquared() < 1e-8f) fwd = Vector3.Up; else fwd = fwd.Normalized();
                ClimberGrowth.StemFrame(new Vec3(fwd.X, fwd.Y, fwd.Z), new Vec3(nrm.X, nrm.Y, nrm.Z), out var sideV, out var upV);
                var side = new Vector3((float)sideV.X, (float)sideV.Y, (float)sideV.Z);
                var up = new Vector3((float)upV.X, (float)upV.Y, (float)upV.Z);
                var nodeBasis = new Basis(side, fwd, up);
                bool growingTip = !hasChild[s];
                if (growingTip)
                {
                    tendrilInst.Add(new Transform3D(nodeBasis, b));
                    continue;
                }
                if (b.DistanceTo(lastLeaf) < leafSpacing) continue;
                lastLeaf = b;
                leafInst.Add(new Transform3D(nodeBasis, b));
            }
            stemMulti.InstanceCount = stemInst.Count;
            for (int i = 0; i < stemInst.Count; i++) stemMulti.SetInstanceTransform(i, stemInst[i]);
            leafMulti.InstanceCount = leafInst.Count;
            for (int i = 0; i < leafInst.Count; i++) leafMulti.SetInstanceTransform(i, leafInst[i]);
            tendrilMulti.InstanceCount = tendrilInst.Count;
            for (int i = 0; i < tendrilInst.Count; i++) tendrilMulti.SetInstanceTransform(i, tendrilInst[i]);
            AddChild(new MultiMeshInstance3D { Name = "Stem", Multimesh = stemMulti });
            AddChild(new MultiMeshInstance3D { Name = "Foliage", Multimesh = leafMulti });
            AddChild(new MultiMeshInstance3D { Name = "Tendrils", Multimesh = tendrilMulti });

            // 6. Frame it.
            var target = new Vector3(0, 0, 0);
            float dist = 3.0f;
            if (Tip)
            {
                // The growing tip of the leader, and the last stretch of stem leading to it.
                var apex = segs.OrderByDescending(s => s.Arc).First();
                target = new Vector3((float)apex.Position.X, (float)apex.Position.Y, (float)apex.Position.Z);
                dist = 0.42f;
            }
            else
            {
                // Frame the part that is actually climbing: the runner on the ground is just the approach.
                var framed = segs.Where(s => s.Attached).ToList();
                if (framed.Count < 2) framed = segs.ToList();
                var box = new Aabb();
                bool first = true;
                foreach (var s in framed)
                {
                    var p = new Vector3((float)s.Position.X, (float)s.Position.Y, (float)s.Position.Z);
                    box = first ? new Aabb(p, Vector3.Zero) : box.Expand(p);
                    first = false;
                }
                target = box.GetCenter();
                dist = Math.Max(0.7f, box.Size.Length() * 0.72f) * Zoom;
            }
            var camera = new Camera3D
            {
                Name = "VineCamera",
                Projection = Camera3D.ProjectionType.Perspective,
                Fov = 42f,
                Current = true,
            };
            AddChild(camera);
            float camAngle = Angle * Mathf.Pi / 180f;
            // The tip is looked at from above: the tendril reaches forward of the stem, and from above it is
            // clear of the leaves that crowd around it.
            var offset = Tip
                ? new Vector3(Mathf.Cos(camAngle) * 0.45f, 0.85f, Mathf.Sin(camAngle) * 0.45f)
                : new Vector3(Mathf.Cos(camAngle), 0.30f, Mathf.Sin(camAngle));
            camera.Position = target + offset.Normalized() * dist;
            camera.LookAt(target);

            for (int i = 0; i < 90; i++) await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            Directory.CreateDirectory(OutDir);
            string path = Path.GetFullPath(Path.Combine(OutDir, $"{SpeciesId}{(Tip ? "-tip" : "")}.png"));
            var error = GetViewport().GetTexture().GetImage().SavePng(path);
            if (error != Error.Ok) throw new IOException($"SavePng returned {error}");
            GD.Print($"VINE_REVIEW_OK {path} stemInstances={stemInst.Count} leafInstances={leafInst.Count} tendrilInstances={tendrilInst.Count}");
            GetTree().Quit(0);
        }
        catch (Exception ex)
        {
            GD.PrintErr("VINE_REVIEW_FAILED " + ex);
            GetTree().Quit(1);
        }
    }
}
