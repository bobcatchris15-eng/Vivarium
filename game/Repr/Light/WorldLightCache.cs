using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;
using Vivarium.Game.App;
using Vivarium.Sim.Core;
using Vivarium.Sim.Flora;
using Vivarium.Sim.Repr;
using Vivarium.Sim.Repr.Light;
using Vivarium.Sim.World;

namespace Vivarium.Repr;

/// <summary>
/// Production controller that feeds authoritative all-world geometry (terrain, ancient pilot tree,
/// rocks, logs, living/dead flora, and climber segments) into a background <see cref="LightCacheWorker"/>
/// and uploads completed 3D sky-visibility textures to <see cref="SkyVisibilityField"/> on the Godot thread.
/// </summary>
public sealed partial class WorldLightCache : Node
{
    private const ulong TerrainSourceId = 1UL;
    private const ulong PilotWoodSourceId = 2UL;
    private const ulong PilotCrownSourceId = 3UL;
    private const ulong PropSourceTag = 1UL << 60;
    private const ulong FloraSourceTag = 2UL << 60;
    private const ulong DeadFloraSourceTag = 3UL << 60;

    private const double DefaultVoxelSize = 0.20;
    private const int WorkerCapacity = 8192;
    private const int BatchSize = 512;
    private const double SyncIntervalSeconds = 0.20;

    private static readonly Vector4 DefaultFoliageShape = new(0.5f, 0.25f, 1.0f, 0.5f);
    private static readonly Vector4 DefaultGroundShape = new(1.0f, 0.0f, 0.0f, 0.0f);
    private const float DefaultSkyVisStrength = 1.0f;
    private const double LeafOpacity = 3.6;

    // Canonical unit box [-0.5..0.5]^3 for rocks and logs (8 vertices, 12 triangles).
    private static readonly float[] UnitBoxPositions =
    {
        -0.5f, -0.5f, -0.5f,  0.5f, -0.5f, -0.5f,  0.5f,  0.5f, -0.5f, -0.5f,  0.5f, -0.5f,
        -0.5f, -0.5f,  0.5f,  0.5f, -0.5f,  0.5f,  0.5f,  0.5f,  0.5f, -0.5f,  0.5f,  0.5f,
    };

    private static readonly int[] UnitBoxIndices =
    {
        0, 1, 2, 0, 2, 3,
        4, 6, 5, 4, 7, 6,
        0, 4, 5, 0, 5, 1,
        1, 5, 6, 1, 6, 2,
        2, 6, 7, 2, 7, 3,
        3, 7, 4, 3, 4, 0,
    };

    // Canonical unit plant canopy (crossed foliar planes + crown cap in [-1..1] X/Z, [0..1] Y; 14 verts, 12 tris).
    private static readonly float[] UnitCanopyPositions =
    {
        // Card X
        -0.85f, 0.10f,  0.00f,  0.85f, 0.10f,  0.00f,  0.70f, 0.95f,  0.00f, -0.70f, 0.95f,  0.00f,
        // Card Z
         0.00f, 0.10f, -0.85f,  0.00f, 0.10f,  0.85f,  0.00f, 0.95f,  0.70f,  0.00f, 0.95f, -0.70f,
        // Horizontal mid-crown disc
        -0.80f, 0.62f, -0.80f,  0.80f, 0.62f, -0.80f,  0.80f, 0.62f,  0.80f, -0.80f, 0.62f,  0.80f,
        // Apex & base
         0.00f, 1.00f,  0.00f,  0.00f, 0.18f,  0.00f,
    };

    private static readonly int[] UnitCanopyIndices =
    {
        0, 1, 2, 0, 2, 3,
        4, 5, 6, 4, 6, 7,
        8, 9, 10, 8, 10, 11,
        8, 9, 12, 9, 10, 12,
        10, 11, 12, 11, 8, 12,
        8, 10, 13, 9, 11, 13,
    };

    public SkyVisibilityField Field { get; private set; } = null!;
    public bool Enabled { get; private set; }

    public int CompletedUploads => Field?.Uploads ?? 0;
    public int WorkerResetCount { get; private set; }
    public int FaultRecoveryCount { get; private set; }
    public int PendingRetriedCount { get; private set; }
    public int TrackedSourceCount => (_terrainSynced ? 1 : 0) + (_pilotSynced ? 2 : 0) + _propSignatures.Count + _floraSignatures.Count + _deadFloraSignatures.Count;
    public Task Quiescence => _lastDisposedQuiescence;

    private VivariumWorld? _world;
    private LightCacheWorker? _worker;
    private Task _lastDisposedQuiescence = Task.CompletedTask;
    private double _sinceSync = 999.0;

    private bool _terrainSynced;
    private int _terrainVersion = int.MinValue;
    private bool _pilotSynced;
    private int _propsVersion = int.MinValue;
    private bool _propsFullySynced;

    private readonly Dictionary<EntityId, ulong> _propSignatures = new();
    private readonly Dictionary<EntityId, ulong> _floraSignatures = new();
    private readonly Dictionary<EntityId, ulong> _deadFloraSignatures = new();
    private readonly HashSet<ulong> _pendingRemovals = new();
    private readonly HashSet<EntityId> _seenIds = new();
    private readonly List<EntityId> _toRemoveIds = new();

    public override void _Ready()
    {
        Field = SkyVisibilityField.CreateUploader();
        AddChild(Field);
        Field.SetStrength(0f);
    }

    public override void _ExitTree()
    {
        DisposeWorker();
        base._ExitTree();
    }

    public void SetEnabled(bool enabled)
    {
        if (Enabled == enabled) return;
        Enabled = enabled;
        if (!Enabled)
        {
            DisposeWorker();
            Field?.SetStrength(0f);
        }
        else if (_world != null)
        {
            ResetWorkerForWorld(_world);
            _sinceSync = 999.0;
        }
    }

    public void BindWorld(VivariumWorld world)
    {
        ArgumentNullException.ThrowIfNull(world);
        _world = world;
        _sinceSync = 999.0;
        if (Enabled)
        {
            ResetWorkerForWorld(world);
        }
    }

    public void NotifyLocalEdit()
    {
        _sinceSync = 999.0;
    }

    /// <summary>
    /// Synchronously advances the cache controller for deterministic tests or review captures.
    /// </summary>
    public void StepImmediate()
    {
        _sinceSync = 999.0;
        TickController(SyncIntervalSeconds);
    }

    public override void _Process(double delta)
    {
        using var prof = FrameProfiler.Measure("LightCache");
        TickController(delta);
    }

    private void TickController(double delta)
    {
        if (!Enabled || _world == null) return;
        if (_worker == null) ResetWorkerForWorld(_world);
        if (_worker == null) return;

        // Fault recovery: if the background worker faulted, discard it and replay authoritative state into a fresh worker.
        if (_worker.Error != null)
        {
            FaultRecoveryCount++;
            ResetWorkerForWorld(_world);
            return;
        }

        // Upload completed snapshot on the Godot thread.
        if (_worker.TryTake(out var snapshot) && snapshot != null)
        {
            Field.UploadPacked(snapshot.Bytes, snapshot.Nx, snapshot.Ny, snapshot.Nz, snapshot.Bounds, snapshot.Voxel);
            Field.SetShapes(DefaultFoliageShape, DefaultGroundShape);
            Field.SetStrength(DefaultSkyVisStrength);
        }

        if (!_worker.Ready) return;

        _sinceSync += delta;
        if (_sinceSync < SyncIntervalSeconds && _pendingRemovals.Count == 0 && _terrainSynced)
        {
            if (_worker.PendingCount > 0) _worker.Start(CompletedUploads == 0 ? Math.Max(BatchSize, _worker.PendingCount) : BatchSize);
            return;
        }
        _sinceSync = 0.0;

        SyncAuthoritativeSources(_world, _worker);
        if (_worker.PendingCount > 0)
        {
            _worker.Start(CompletedUploads == 0 ? Math.Max(BatchSize, _worker.PendingCount) : BatchSize);
        }
    }

    private void ResetWorkerForWorld(VivariumWorld world)
    {
        DisposeWorker();
        double r = world.Domain.Radius + 0.8;
        var bounds = new Vivarium.Sim.Repr.Aabb(new Vec3(-r, -1.2, -r), new Vec3(r, 5.6, r));
        _worker = new LightCacheWorker(bounds, DefaultVoxelSize, WorkerCapacity);
        WorkerResetCount++;
        _terrainSynced = false;
        _terrainVersion = int.MinValue;
        _pilotSynced = false;
        _propsVersion = int.MinValue;
        _propsFullySynced = false;
        _propSignatures.Clear();
        _floraSignatures.Clear();
        _deadFloraSignatures.Clear();
        _pendingRemovals.Clear();
    }

    private void DisposeWorker()
    {
        if (_worker == null) return;
        var old = _worker;
        _worker = null;
        old.Dispose();
        _lastDisposedQuiescence = old.Quiescence;
    }

    private void SyncAuthoritativeSources(VivariumWorld world, LightCacheWorker worker)
    {
        // 1. Drain any pending removals first so slots free up.
        if (_pendingRemovals.Count > 0)
        {
            var removed = new List<ulong>();
            foreach (ulong id in _pendingRemovals)
            {
                if (worker.TryRemove(id)) removed.Add(id);
                else { PendingRetriedCount++; break; }
            }
            foreach (ulong id in removed) _pendingRemovals.Remove(id);
        }

        // 2. Terrain heightfield mesh (coarse 0.5m grid across the island domain).
        if (!_terrainSynced || _terrainVersion != world.Terrain.Version)
        {
            BuildTerrainMesh(world, out var tPos, out var tIdx);
            if (worker.TrySet(TerrainSourceId, tPos, tIdx, null, OcclusionVolume.OpaqueOpacity))
            {
                _terrainSynced = true;
                _terrainVersion = world.Terrain.Version;
            }
            else
            {
                PendingRetriedCount++;
                return;
            }
        }

        // 3. Ancient Pilot Tree (trunk + buttresses + crown foliage shell).
        if (!_pilotSynced)
        {
            if (world.PilotTree == null)
            {
                _pilotSynced = true;
            }
            else
            {
                BuildPilotTreeMeshes(world, out var woodPos, out var woodIdx, out var crownPos, out var crownIdx);
                bool okWood = worker.TrySet(PilotWoodSourceId, woodPos, woodIdx, null, OcclusionVolume.OpaqueOpacity);
                bool okCrown = okWood && worker.TrySet(PilotCrownSourceId, crownPos, crownIdx, null, LeafOpacity * 1.15);
                if (okWood && okCrown)
                {
                    _pilotSynced = true;
                }
                else
                {
                    PendingRetriedCount++;
                    return;
                }
            }
        }

        // 4. Props (Rocks & Logs).
        if (!_propsFullySynced || _propsVersion != world.Props.Version)
        {
            bool allPropsAccepted = SyncProps(world, worker);
            if (allPropsAccepted)
            {
                _propsVersion = world.Props.Version;
                _propsFullySynced = true;
            }
            else
            {
                _propsFullySynced = false;
                PendingRetriedCount++;
            }
        }

        // 5. All-world Living Flora (including vascular plants, colonies, and climber segments).
        SyncLivingFlora(world, worker);

        // 6. All-world Dead Flora.
        SyncDeadFlora(world, worker);
    }

    private bool SyncProps(VivariumWorld world, LightCacheWorker worker)
    {
        _seenIds.Clear();
        bool allAccepted = true;

        foreach (var rock in world.Props.Rocks)
        {
            _seenIds.Add(rock.Id);
            double groundY = world.Terrain.Height(rock.Position);
            ulong sig = HashProp(rock.Position.X, groundY, rock.Position.Z, rock.SizeX, rock.SizeY, rock.SizeZ, rock.RotationY);
            if (_propSignatures.TryGetValue(rock.Id, out ulong prev) && prev == sig) continue;

            var xf = MakeBoxTransform(rock.Position.X, groundY + rock.SizeY * 0.32, rock.Position.Z,
                rock.SizeX, rock.SizeY * 0.85, rock.SizeZ, rock.RotationY);
            if (worker.TrySet(PropSourceTag | rock.Id.Value, UnitBoxPositions, UnitBoxIndices, xf, OcclusionVolume.OpaqueOpacity))
            {
                _propSignatures[rock.Id] = sig;
            }
            else
            {
                allAccepted = false;
                break;
            }
        }

        if (allAccepted)
        {
            foreach (var log in world.Props.Logs)
            {
                _seenIds.Add(log.Id);
                double groundY = world.Terrain.Height(log.Position);
                ulong sig = HashProp(log.Position.X, groundY, log.Position.Z, log.Length, log.Radius * 2.0, log.Radius * 2.0, log.RotationY);
                if (_propSignatures.TryGetValue(log.Id, out ulong prev) && prev == sig) continue;

                var xf = MakeBoxTransform(log.Position.X, groundY + log.Radius * 0.85, log.Position.Z,
                    log.Length, log.Radius * 1.9, log.Radius * 1.9, log.RotationY);
                if (worker.TrySet(PropSourceTag | log.Id.Value, UnitBoxPositions, UnitBoxIndices, xf, OcclusionVolume.OpaqueOpacity))
                {
                    _propSignatures[log.Id] = sig;
                }
                else
                {
                    allAccepted = false;
                    break;
                }
            }
        }

        _toRemoveIds.Clear();
        foreach (var id in _propSignatures.Keys)
        {
            if (!_seenIds.Contains(id)) _toRemoveIds.Add(id);
        }
        foreach (var id in _toRemoveIds)
        {
            _propSignatures.Remove(id);
            ulong srcId = PropSourceTag | id.Value;
            if (!worker.TryRemove(srcId))
            {
                _pendingRemovals.Add(srcId);
                allAccepted = false;
            }
        }

        return allAccepted;
    }

    private void SyncLivingFlora(VivariumWorld world, LightCacheWorker worker)
    {
        _seenIds.Clear();
        foreach (var plant in world.Flora.Items)
        {
            _seenIds.Add(plant.Id);
            var sp = world.Content.FloraById(plant.SpeciesId);
            if (sp == null) continue;

            // Skip microscopic crusts (< 3 cm tall) that do not cast meaningful 20 cm voxel sky occlusion.
            double r = plant.Radius(sp);
            double h = sp.Colony != null
                ? sp.Colony.MaxHeight * (0.15 + 0.85 * plant.HeightFactor)
                : sp.Height * (0.45 + 0.55 * Math.Sqrt(plant.BiomassFraction(sp)));
            if (h < 0.035 && (plant.ClimberSegments == null || plant.ClimberSegments.Count <= 1)) continue;

            double groundY = world.GroundHeight(plant.Position);
            int segCount = plant.ClimberSegments?.Count ?? 0;
            ulong sig = HashPlant(plant.X, groundY, plant.Z, r, h, segCount);
            if (_floraSignatures.TryGetValue(plant.Id, out ulong prev) && prev == sig) continue;

            ulong srcId = FloraSourceTag | plant.Id.Value;
            bool accepted;
            if (sp.Climber != null && plant.ClimberSegments is { Count: > 1 } segs)
            {
                BuildClimberProxy(segs, r, out var cPos, out var cIdx);
                accepted = worker.TrySet(srcId, cPos, cIdx, null, LeafOpacity);
            }
            else
            {
                var xf = MakeCanopyTransform(plant.X, groundY, plant.Z, Math.Max(0.05, r), Math.Max(0.05, h), plant.Id.Value);
                double opacity = sp.Woody != null ? LeafOpacity * 1.35 : LeafOpacity;
                accepted = worker.TrySet(srcId, UnitCanopyPositions, UnitCanopyIndices, xf, opacity);
            }

            if (accepted)
            {
                _floraSignatures[plant.Id] = sig;
            }
            else
            {
                PendingRetriedCount++;
                break;
            }
        }

        _toRemoveIds.Clear();
        foreach (var id in _floraSignatures.Keys)
        {
            if (!_seenIds.Contains(id)) _toRemoveIds.Add(id);
        }
        foreach (var id in _toRemoveIds)
        {
            _floraSignatures.Remove(id);
            ulong srcId = FloraSourceTag | id.Value;
            if (!worker.TryRemove(srcId))
            {
                _pendingRemovals.Add(srcId);
                PendingRetriedCount++;
            }
        }
    }

    private void SyncDeadFlora(VivariumWorld world, LightCacheWorker worker)
    {
        _seenIds.Clear();
        foreach (var dead in world.DeadFlora.Items)
        {
            _seenIds.Add(dead.Id);
            double remain = Math.Sqrt(Math.Clamp(dead.RemainingFraction, 0.0, 1.0));
            double r = dead.OriginalRadius * (0.82 + 0.18 * remain);
            double h = dead.OriginalHeight * (0.78 + 0.22 * remain);
            if (h < 0.05) continue;

            double groundY = world.GroundHeight(dead.Position);
            ulong sig = HashPlant(dead.X, groundY, dead.Z, r, h, (int)dead.Stage);
            if (_deadFloraSignatures.TryGetValue(dead.Id, out ulong prev) && prev == sig) continue;

            var xf = MakeCanopyTransform(dead.X, groundY, dead.Z, Math.Max(0.05, r), Math.Max(0.05, h * 0.75), dead.Id.Value);
            ulong srcId = DeadFloraSourceTag | dead.Id.Value;
            if (worker.TrySet(srcId, UnitCanopyPositions, UnitCanopyIndices, xf, LeafOpacity * 0.75))
            {
                _deadFloraSignatures[dead.Id] = sig;
            }
            else
            {
                PendingRetriedCount++;
                break;
            }
        }

        _toRemoveIds.Clear();
        foreach (var id in _deadFloraSignatures.Keys)
        {
            if (!_seenIds.Contains(id)) _toRemoveIds.Add(id);
        }
        foreach (var id in _toRemoveIds)
        {
            _deadFloraSignatures.Remove(id);
            ulong srcId = DeadFloraSourceTag | id.Value;
            if (!worker.TryRemove(srcId))
            {
                _pendingRemovals.Add(srcId);
                PendingRetriedCount++;
            }
        }
    }

    private static void BuildTerrainMesh(VivariumWorld world, out float[] positions, out int[] indices)
    {
        double r = world.Domain.Radius;
        const int steps = 32;
        double step = (2.0 * r) / steps;
        int stride = steps + 1;
        positions = new float[stride * stride * 3];
        int p = 0;
        for (int iz = 0; iz <= steps; iz++)
        {
            double z = -r + iz * step;
            for (int ix = 0; ix <= steps; ix++)
            {
                double x = -r + ix * step;
                var pt = new Vec2(x, z);
                double y = world.Domain.Contains(pt) ? world.Terrain.Height(pt) : -0.6;
                positions[p++] = (float)x;
                positions[p++] = (float)y;
                positions[p++] = (float)z;
            }
        }

        var idxList = new List<int>(steps * steps * 6);
        for (int iz = 0; iz < steps; iz++)
        {
            for (int ix = 0; ix < steps; ix++)
            {
                int i00 = iz * stride + ix;
                int i10 = i00 + 1;
                int i01 = (iz + 1) * stride + ix;
                int i11 = i01 + 1;
                idxList.Add(i00); idxList.Add(i10); idxList.Add(i11);
                idxList.Add(i00); idxList.Add(i11); idxList.Add(i01);
            }
        }
        indices = idxList.ToArray();
    }

    private static void BuildPilotTreeMeshes(
        VivariumWorld world,
        out float[] woodPos,
        out int[] woodIdx,
        out float[] crownPos,
        out int[] crownIdx)
    {
        var pt = world.PilotTree!;
        double groundY = world.Terrain.Height(pt.Anchor);
        double trunkR = Math.Max(0.32, pt.Def.TrunkRadius * 0.45);
        double crownBaseH = Math.Max(1.8, pt.Def.TrunkHeight * 0.25);
        double trunkH = Math.Max(2.2, crownBaseH + 0.6);

        // Trunk column: 8-sided prism from groundY - 0.2 to groundY + trunkH.
        const int sides = 8;
        woodPos = new float[sides * 2 * 3];
        for (int i = 0; i < sides; i++)
        {
            double ang = i * (2.0 * Math.PI / sides);
            double cx = Math.Cos(ang), cz = Math.Sin(ang);
            double rBot = trunkR * 1.45;
            double rTop = trunkR * 0.78;
            int b = i * 6;
            woodPos[b + 0] = (float)(pt.Anchor.X + cx * rBot);
            woodPos[b + 1] = (float)(groundY - 0.15);
            woodPos[b + 2] = (float)(pt.Anchor.Z + cz * rBot);
            woodPos[b + 3] = (float)(pt.Anchor.X + cx * rTop);
            woodPos[b + 4] = (float)(groundY + trunkH);
            woodPos[b + 5] = (float)(pt.Anchor.Z + cz * rTop);
        }
        woodIdx = new int[sides * 6];
        for (int i = 0; i < sides; i++)
        {
            int next = (i + 1) % sides;
            int b0 = i * 2, t0 = b0 + 1;
            int b1 = next * 2, t1 = b1 + 1;
            int o = i * 6;
            woodIdx[o + 0] = b0; woodIdx[o + 1] = b1; woodIdx[o + 2] = t1;
            woodIdx[o + 3] = b0; woodIdx[o + 4] = t1; woodIdx[o + 5] = t0;
        }

        // Crown shell: scaled canopy proxy centered over the pilot tree crown.
        double crownR = Math.Max(1.8, pt.CanopyRadius * 0.88);
        double crownBase = groundY + Math.Max(1.4, crownBaseH * 0.85);
        double crownH = Math.Max(1.6, pt.Def.TrunkHeight - crownBaseH * 0.75);
        crownPos = new float[UnitCanopyPositions.Length];
        for (int i = 0; i < UnitCanopyPositions.Length; i += 3)
        {
            crownPos[i + 0] = (float)(pt.Anchor.X + UnitCanopyPositions[i + 0] * crownR);
            crownPos[i + 1] = (float)(crownBase + UnitCanopyPositions[i + 1] * crownH);
            crownPos[i + 2] = (float)(pt.Anchor.Z + UnitCanopyPositions[i + 2] * crownR);
        }
        crownIdx = (int[])UnitCanopyIndices.Clone();
    }

    private static void BuildClimberProxy(
        IReadOnlyList<ClimberSegment> segs,
        double baseRadius,
        out float[] positions,
        out int[] indices)
    {
        var posList = new List<float>(segs.Count * 12);
        var idxList = new List<int>(segs.Count * 6);
        float halfW = (float)Math.Clamp(baseRadius * 0.35, 0.04, 0.16);

        for (int i = 0; i < segs.Count; i++)
        {
            var seg = segs[i];
            if (seg.ParentIndex < 0 || seg.ParentIndex >= segs.Count) continue;
            var parent = segs[seg.ParentIndex];
            var a = parent.Position;
            var b = seg.Position;
            double dx = b.X - a.X, dy = b.Y - a.Y, dz = b.Z - a.Z;
            double len = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (len < 1e-3) continue;

            // Crossed ribbon quad along the segment.
            var n = seg.Normal;
            double nl = Math.Sqrt(n.X * n.X + n.Y * n.Y + n.Z * n.Z);
            double sx = nl > 1e-4 ? (dy * n.Z - dz * n.Y) / len : -dz / len;
            double sy = nl > 1e-4 ? (dz * n.X - dx * n.Z) / len : 0.0;
            double sz = nl > 1e-4 ? (dx * n.Y - dy * n.X) / len : dx / len;
            double sl = Math.Sqrt(sx * sx + sy * sy + sz * sz);
            if (sl < 1e-4) { sx = 1.0; sy = 0.0; sz = 0.0; }
            else { sx /= sl; sy /= sl; sz /= sl; }

            int v0 = posList.Count / 3;
            AddVertex(posList, a.X - sx * halfW, a.Y - sy * halfW, a.Z - sz * halfW);
            AddVertex(posList, a.X + sx * halfW, a.Y + sy * halfW, a.Z + sz * halfW);
            AddVertex(posList, b.X + sx * halfW, b.Y + sy * halfW, b.Z + sz * halfW);
            AddVertex(posList, b.X - sx * halfW, b.Y - sy * halfW, b.Z - sz * halfW);
            idxList.Add(v0 + 0); idxList.Add(v0 + 1); idxList.Add(v0 + 2);
            idxList.Add(v0 + 0); idxList.Add(v0 + 2); idxList.Add(v0 + 3);
        }

        if (idxList.Count == 0)
        {
            var root = segs[0].Position;
            positions = new float[]
            {
                (float)root.X - 0.05f, (float)root.Y, (float)root.Z,
                (float)root.X + 0.05f, (float)root.Y, (float)root.Z,
                (float)root.X, (float)root.Y + 0.10f, (float)root.Z,
            };
            indices = new int[] { 0, 1, 2 };
            return;
        }

        positions = posList.ToArray();
        indices = idxList.ToArray();
    }

    private static void AddVertex(List<float> list, double x, double y, double z)
    {
        list.Add((float)x);
        list.Add((float)y);
        list.Add((float)z);
    }

    private static double[] MakeBoxTransform(double x, double y, double z, double sx, double sy, double sz, double yaw)
    {
        double c = Math.Cos(yaw), s = Math.Sin(yaw);
        return new double[]
        {
            c * sx, 0.0, s * sz, x,
            0.0,    sy,  0.0,    y,
            -s * sx, 0.0, c * sz, z,
        };
    }

    private static double[] MakeCanopyTransform(double x, double y, double z, double radius, double height, ulong seed)
    {
        double yaw = ((seed * 0x9E3779B97F4A7C15UL) >> 40) * (2.0 * Math.PI / 16777216.0);
        double c = Math.Cos(yaw), s = Math.Sin(yaw);
        return new double[]
        {
            c * radius, 0.0,    s * radius, x,
            0.0,        height, 0.0,        y,
            -s * radius, 0.0,   c * radius, z,
        };
    }

    private static ulong HashProp(double x, double y, double z, double sx, double sy, double sz, double yaw)
    {
        ulong h = 14695981039346656037UL;
        h = Mix(h, (long)Math.Round(x * 50.0));
        h = Mix(h, (long)Math.Round(y * 50.0));
        h = Mix(h, (long)Math.Round(z * 50.0));
        h = Mix(h, (long)Math.Round(sx * 50.0));
        h = Mix(h, (long)Math.Round(sy * 50.0));
        h = Mix(h, (long)Math.Round(sz * 50.0));
        h = Mix(h, (long)Math.Round(yaw * 20.0));
        return h;
    }

    private static ulong HashPlant(double x, double y, double z, double radius, double height, int extra)
    {
        // Quantize radius and height to 4 cm steps so continuous sub-millimetre growth does not trigger redundant rebakes,
        // while births, deaths, and stage growth update local occlusion promptly.
        ulong h = 14695981039346656037UL;
        h = Mix(h, (long)Math.Round(x * 25.0));
        h = Mix(h, (long)Math.Round(y * 25.0));
        h = Mix(h, (long)Math.Round(z * 25.0));
        h = Mix(h, (long)Math.Round(radius * 25.0));
        h = Mix(h, (long)Math.Round(height * 25.0));
        h = Mix(h, extra);
        return h;
    }

    private static ulong Mix(ulong h, long v)
    {
        h ^= (ulong)v;
        return h * 1099511628211UL;
    }
}
