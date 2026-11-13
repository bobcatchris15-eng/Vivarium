// REPRESENTATION REBUILD — shared contracts (I0). Owner: orchestrator. Workers must not edit this file;
// request changes in their report. See .claude/ledger.md "REPRESENTATION REBUILD".
//
// Conventions (binding for every Repr module):
//   * Units: metres, Y up, sim Vec2 = (X,Z) ground plane. Physical time in seconds; ecological time in
//     sim-seconds (Clock tick = 10 sim-s). Colours are LINEAR rgb 0..1. Water amounts are metres of water
//     column (m3 per m2 of ground) unless a field says "fraction".
//   * Identity: all procedural variation derives from ReprId.Hash(worldSeed, entity, feature). Never from
//     camera, frame, time, iteration order or allocation order. Same inputs => bit-identical outputs.
//   * State vs cache: types in Repr.* that end in "State"/"Descriptor"/"Patch"/"Column" are authoritative
//     and serialisable. Anything built from them (meshes, textures, instance buffers, light fields) is a
//     disposable cache keyed by CacheKey and may be dropped at any time.
//   * Renderers never write authoritative state. Ecology never reads a render mesh.
//   * Invalidation is regional: publish a DirtyRecord; never "rebuild everything" unless Bounds is the island.
using System;
using System.Collections.Generic;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr;

// ---------------------------------------------------------------- identity

public static class ReprId
{
    /// Stable 64-bit hash for (world, entity, feature). feature 0 = the entity itself.
    public static ulong Hash(ulong worldSeed, ulong entity, ulong feature = 0) =>
        Rng.Mix(Rng.Mix(worldSeed, entity), feature ^ 0x9E3779B97F4A7C15UL);

    /// Uniform [0,1) from a hash and a salt. Use distinct salts per parameter.
    public static double Unit(ulong hash, uint salt) =>
        (Rng.Mix(hash, salt) >> 11) * (1.0 / 9007199254740992.0);

    /// Uniform in [lo,hi).
    public static double Range(ulong hash, uint salt, double lo, double hi) => lo + (hi - lo) * Unit(hash, salt);
}

// ---------------------------------------------------------------- dirty regions

[Flags]
public enum DirtyDomain
{
    None = 0, Geometry = 1, SoilMaterial = 2, Vegetation = 4, Coverage = 8, Water = 16, Light = 32,
}

public readonly record struct Aabb(Vec3 Min, Vec3 Max)
{
    public bool Intersects(in Aabb o) =>
        Min.X <= o.Max.X && Max.X >= o.Min.X && Min.Y <= o.Max.Y && Max.Y >= o.Min.Y && Min.Z <= o.Max.Z && Max.Z >= o.Min.Z;
    public static Aabb Around(Vec3 c, double r) => new(new Vec3(c.X - r, c.Y - r, c.Z - r), new Vec3(c.X + r, c.Y + r, c.Z + r));
}

/// One change notice. Version is the publishing component's monotonically increasing state version AFTER
/// the change. SimSeconds is ecological time. Reason is a short stable token ("grow", "dig", "wet", "die").
public readonly record struct DirtyRecord(Aabb Bounds, DirtyDomain Domain, ulong Version, string Reason, double SimSeconds);

/// Append-only log with cursor-based consumption; each consumer keeps its own cursor so nobody clears
/// another consumer's records. Thread rule: Publish from the sim thread only; Read from any thread after
/// the sim step that published (snapshot semantics are the caller's job).
public sealed class DirtyLog
{
    private readonly List<DirtyRecord> _records = new();
    private long _base; // sequence number of _records[0]
    public long Head => _base + _records.Count;
    public void Publish(in DirtyRecord r) => _records.Add(r);

    /// Copies records with sequence >= cursor that match domains into dst; returns the new cursor (Head).
    public long Read(long cursor, DirtyDomain domains, List<DirtyRecord> dst)
    {
        for (long i = Math.Max(cursor, _base); i < Head; i++)
        {
            var r = _records[(int)(i - _base)];
            if ((r.Domain & domains) != 0) dst.Add(r);
        }
        return Head;
    }

    /// Drop records older than the slowest consumer's cursor.
    public void Trim(long minCursor)
    {
        long n = Math.Min(minCursor, Head) - _base;
        if (n > 0) { _records.RemoveRange(0, (int)n); _base += n; }
    }
}

/// Key for any disposable cache entry. Detail is a quantised level (0 = finest); it must be chosen from
/// projected size with hysteresis by the planner, never recomputed inside a builder.
public readonly record struct CacheKey(ulong FeatureId, ulong StateVersion, int Detail);

// ---------------------------------------------------------------- substrate

public enum Horizon : byte { Void = 0, Litter = 1, Humus = 2, MineralSoil = 3, Weathered = 4, Bedrock = 5, Water = 6 }

/// Composition and state at a point in the ground. Fractions are volume fractions of the SOLID phase and
/// sum to 1 (CoarseFragments + Sand + Silt + Clay + Organic = 1). Porosity is void volume / bulk volume.
public struct SubstrateSample
{
    public bool Solid;                // false above the surface or inside an open void
    public Horizon Horizon;
    public float CoarseFragments, Sand, Silt, Clay, Organic;
    public float Porosity;            // 0..~0.7
    public float AggregateDiameter;   // m, dominant crumb/ped size (0.0005..0.05)
    public float Compaction;          // 0 loose .. 1 dense
    public float Strength;            // 0..1 resistance to digging/erosion/root entry
    public float WaterContent;        // volumetric fraction 0..Porosity (liquid in pores)
    public float FieldCapacity;       // volumetric fraction held against gravity (capillary retention)
    public float WiltingPoint;        // volumetric fraction unavailable to roots
    public float Permeability;        // saturated hydraulic conductivity, m/s
    public float FineRootDensity;     // m of fine root per m3 of soil
    public float ColonizationCapacity;// 0..1 suitability of this material as an attachment surface
    public Vec3 MineralHue;           // linear rgb of the dry mineral fraction
    public Vec3 BeddingNormal;        // unit normal of strata/bedding planes (rock + weathered), else Up
    public readonly float Saturation => Porosity > 1e-6f ? WaterContent / Porosity : 0f;
}

public struct SurfaceSample
{
    public Vec3 Position;             // nearest exposed surface point
    public Vec3 Normal;
    public SubstrateSample Material;  // material immediately beneath the surface
    public float FilmWater;           // m of free water film on the surface (visible wetness / sheen)
    public float StandingWater;       // m of ponded water above the surface
    public ulong SurfaceId;           // stable id of the surface element (ground cell, rock face, log) for attachment
}

/// The one way every workstream reads the ground. Implementations must be deterministic and thread-safe
/// for concurrent reads between sim steps.
public interface ISubstrate
{
    ulong Version { get; }
    Aabb Bounds { get; }
    SubstrateSample SampleSubstrate(Vec3 position);
    /// Nearest surface to the query within maxDistance; false if none.
    bool SurfaceSample(Vec3 position, double maxDistance, out SurfaceSample s);
    /// Ground height (top of the litter/soil/rock surface) at a ground-plane point; NaN outside the domain.
    double Height(Vec2 p);
}

// ---------------------------------------------------------------- plants

public enum OrganType : byte { Axis = 0, Leaf = 1, Leaflet = 2, Petiole = 3, Flower = 4, Fruit = 5, Bud = 6, Root = 7, Tendril = 8 }
public enum Tissue : byte { Green = 0, Wood = 1, Bark = 2, Lamina = 3, Petal = 4, FruitSkin = 5, RootBark = 6 }

[Flags]
public enum OrganFlags : ushort { None = 0, Dormant = 1, Dead = 2, Emissive = 4, Fertile = 8, Hidden = 16, Terminal = 32 }

/// One organ in a plant's structural graph. Organs are stored parent-before-child. All positions are in
/// PLANT-LOCAL metres (origin at the stem base on the ground, Y up); the plant's world transform is on
/// PlantDescriptor. An Axis runs Start -> End as a cubic Hermite with tangents StartTangent/EndTangent
/// (direction * length scale) and tapers RadiusStart -> RadiusEnd. For Leaf/Leaflet/Flower/Fruit, Start is
/// the attachment point ON the parent surface, StartTangent the outward direction, End the tip, and
/// ShapeId indexes PlantDescriptor.Shapes. AttachT is the parametric position (0..1) along the parent axis.
public struct Organ
{
    public int Parent;                // -1 for the root axis
    public OrganType Type;
    public Tissue Tissue;
    public OrganFlags Flags;
    public ushort ShapeId;            // index into PlantDescriptor.Shapes (leaf/flower/fruit shared component)
    public ushort Order;              // branching order: 0 trunk, 1 primary branch, ...
    public float AttachT;
    public Vec3 Start, End, StartTangent, EndTangent;
    public Vec3 Up;                   // adaxial/"top" reference for flat organs; frame hint for axes
    public float RadiusStart, RadiusEnd; // m; for flat organs RadiusStart = half width, RadiusEnd = thickness
    public float Maturity;            // 0 just initiated .. 1 full size (drives smooth growth, no popping)
    public float Health;              // 0 dead .. 1 healthy
    public uint Seed;                 // per-organ variation seed, from ReprId.Hash(world, plant, organIndex)
}

/// Shared anatomical component (a leaf, leaflet, petal, fruit body). Authored once per species and
/// instanced by every organ that references it. Unit space: base at origin, length 1 along +Z, width
/// along X, adaxial +Y. Outline = half-width profile samples from base (0) to tip (1), each 0..1.
public sealed class OrganShape
{
    public string Name = "";
    public OrganType Type;
    public float[] WidthProfile = Array.Empty<float>();
    public float LengthToWidth = 2.5f;
    public float MidribCurl;          // radians of downward arc along the midrib
    public float CrossCurl;           // radians of fold/cupping across the blade (+ = V up)
    public float Twist;               // radians along the midrib
    public float EdgeWave, EdgeWaveFreq;
    public float Serration, SerrationFreq;
    public float Thickness = 0.0004f; // m at real size
    public int VenationClass;         // 0 pinnate, 1 palmate, 2 parallel, 3 dichotomous
    public Vec3 ColorAdaxial, ColorAbaxial; // linear rgb
    public float Transmission = 0.35f, Roughness = 0.55f, Wax;
}

/// Compact optical/statistical summary of a foliage cluster, used for mid/far aggregates and for light.
public struct FoliageAggregate
{
    public Aabb Bounds;               // plant-local
    public float LeafArea;            // m2 one-sided
    public float Opacity;             // mean projected opacity through the cluster 0..1
    public Vec3 MeanNormal;           // unnormalised mean of leaf normals (length = alignment 0..1)
    public Vec3 Color;                // mean reflectance, linear
    public float Transmission, Roughness;
}

public sealed class PlantDescriptor
{
    public ulong PlantId;             // stable entity id
    public ulong WorldSeed;
    public string SpeciesId = "";
    public ulong StructureVersion;    // bumps when topology changes (organ added/removed)
    public ulong GrowthVersion;       // bumps when only Maturity/Health/radii change
    public Vec3 Position;             // world metres
    public double Yaw;                // radians about Y
    public double Scale = 1.0;
    public Organ[] Organs = Array.Empty<Organ>();
    public OrganShape[] Shapes = Array.Empty<OrganShape>();
    public FoliageAggregate[] Aggregates = Array.Empty<FoliageAggregate>(); // filled by the compiler, cache
    public Aabb LocalBounds;
    // Ecological summaries derived from the graph (the ONLY things ecology reads about shape):
    public double LeafArea;           // m2
    public double WoodVolume;         // m3
    public double Height, CrownRadius;// m
}

/// Species growth rule: deterministic function (species traits, seed, age/vigour) -> organ graph.
/// Grow must be monotone: growing an existing descriptor may add organs and raise Maturity, but must not
/// move or re-randomise organs that already exist (stable identity across growth).
public interface IPlantGenerator
{
    string SpeciesId { get; }
    PlantDescriptor Create(ulong worldSeed, ulong plantId);
    /// Advance to biological age (0 seedling .. 1 mature) with vigour 0..1. Returns true if topology changed.
    bool Grow(PlantDescriptor plant, double age01, double vigour, ISubstrate? substrate);
}

// ---------------------------------------------------------------- surface colonies

public enum ColonyKind : byte { Moss = 0, CrustoseLichen = 1, FolioseLichen = 2, FruticoseLichen = 3, Litter = 4, GroundCover = 5, Algae = 6 }

/// An attached aggregate colony: a set of surface elements with per-element state. NOT one object per
/// shoot. Elements are addressed by ISubstrate SurfaceId so colonies ride on ground, rock faces and logs.
public sealed class SurfaceColonyPatch
{
    public ulong PatchId;
    public string SpeciesId = "";
    public ColonyKind Kind;
    public ulong Version;
    public Aabb Bounds;
    public ulong[] SurfaceIds = Array.Empty<ulong>();
    public float[] Coverage = Array.Empty<float>();   // 0..1 fraction of the element occupied
    public float[] Thickness = Array.Empty<float>();  // m
    public float[] Biomass = Array.Empty<float>();    // kg dry mass per m2
    public float[] Health = Array.Empty<float>();     // 0..1
    public float[] FrontAge = Array.Empty<float>();   // sim-seconds since the growth front arrived (0 = edge)
    public double Area;                               // m2 occupied (sum coverage * element area)
    public double TotalBiomass;                       // kg
}

// ---------------------------------------------------------------- lighting

/// Cached GEOMETRY response only (no sun colour/energy baked in). SkyVisibility = fraction of the upper
/// hemisphere unoccluded; BentNormal = mean unoccluded direction (length encodes cone width);
/// DirectionalOpenness[i] = transmittance toward 6 fixed directions (+X,-X,+Z,-Z, up-high, up-low) so a
/// moving sun can be relit without rebaking.
public struct LightTransportSample
{
    public float SkyVisibility;
    public Vec3 BentNormal;
    public float Open0, Open1, Open2, Open3, Open4, Open5;
}

public interface ILightTransportField
{
    ulong Version { get; }
    LightTransportSample Sample(Vec3 position);
    /// Recompute only where records intersect; must not touch untouched regions.
    void Invalidate(IReadOnlyList<DirtyRecord> records);
}

// ---------------------------------------------------------------- budgets (conservation checks)

/// Every conservative subsystem reports a closed budget each step: Stored(after) - Stored(before) must
/// equal In - Out within Tolerance. Tests and invariants assert this; no silent clamps that create mass.
public readonly record struct Budget(string Quantity, double StoredBefore, double StoredAfter, double In, double Out)
{
    public double Residual => (StoredAfter - StoredBefore) - (In - Out);
    public bool Closed(double tolerance) => Math.Abs(Residual) <= tolerance;
}

// ---------------------------------------------------------------- self tests

/// Headless correctness check discovered by reflection (`--repr-selftest`). Run returns null on pass or a
/// one-line failure reason. Must be deterministic, need no Godot types, and finish in under 5 s.
public interface IReprSelfTest
{
    string Name { get; }
    string? Run();
}
