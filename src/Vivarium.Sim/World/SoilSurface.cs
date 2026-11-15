using Vivarium.Sim.Core;
using System.Collections.Concurrent;

namespace Vivarium.Sim.World;

/// <summary>Persist these values with any world that adopts this surface as physical authority.</summary>
public readonly record struct SoilSurfaceParameters(int Version, ulong Seed, double Amplitude);

public readonly record struct SoilSurfaceSample(double Height, Vec3 Normal);

/// <summary>
/// Immutable, terrain-aligned, connected triangular soil surface. This is an isolated foundation;
/// adopting it as terrain authority requires updating all water, collision and rendering consumers.
/// Capture must be made while the source heightfield is not being edited.
/// </summary>
public sealed class SoilSurface
{
    public const double TargetSpacing = 0.0125;
    public const int CurrentVersion = 1;
    private readonly Heightfield _base;
    private const int TileSize = 32;
    private readonly ConcurrentDictionary<(long X, long Z), Lazy<double[]>> _tiles = new();
    public int Subdivisions { get; }
    public double Spacing { get; }
    public double OriginX => _base.OriginX;
    public double OriginZ => _base.OriginZ;
    public SoilSurfaceParameters Parameters { get; }
    public int TerrainVersion { get; }

    public SoilSurface(Heightfield terrain, SoilSurfaceParameters parameters)
    {
        ArgumentNullException.ThrowIfNull(terrain);
        if (parameters.Version != CurrentVersion) throw new ArgumentOutOfRangeException(nameof(parameters), "Unknown soil surface version.");
        if (!double.IsFinite(parameters.Amplitude) || parameters.Amplitude < 0) throw new ArgumentOutOfRangeException(nameof(parameters));
        _base = terrain.MacroSnapshot();
        Subdivisions = checked((int)Math.Ceiling(_base.Step / TargetSpacing));
        Spacing = _base.Step / Subdivisions;
        TerrainVersion = _base.Version;
        Parameters = parameters;
    }

    /// <summary>Number of realized vertex-cache tiles, for diagnostics and cache reuse tests.</summary>
    public int CachedTileCount => _tiles.Count(pair => pair.Value.IsValueCreated);

    /// <summary>True only when the canonical lattice and procedural parameters agree.</summary>
    public bool GeometryCompatible(SoilSurface other) => other != null && Parameters == other.Parameters &&
        _base.Step == other._base.Step && _base.OriginX == other._base.OriginX && _base.OriginZ == other._base.OriginZ &&
        _base.Nx == other._base.Nx && _base.Nz == other._base.Nz;

    /// <summary>
    /// Conservative world-space extent influenced by changed coarse vertices, including fine triangle interpolation.
    /// Returns false for identical compatible bases. Check GeometryCompatible first; incompatible surfaces need a full rebuild.
    /// </summary>
    public bool TryGetChangedBounds(SoilSurface previous, out Vec2 minimum, out Vec2 maximum)
    {
        if (!GeometryCompatible(previous)) throw new ArgumentException("Incompatible soil surfaces", nameof(previous));
        int minI = _base.Nx, minJ = _base.Nz, maxI = -1, maxJ = -1;
        for (int j = 0; j < _base.Nz; j++) for (int i = 0; i < _base.Nx; i++)
        {
            int k = j * _base.Nx + i;
            if (_base.H[k] == previous._base.H[k]) continue;
            minI = Math.Min(minI, i); minJ = Math.Min(minJ, j); maxI = Math.Max(maxI, i); maxJ = Math.Max(maxJ, j);
        }
        if (maxI < 0) { minimum = maximum = default; return false; }
        double pad = _base.Step + Spacing;
        minimum = new Vec2(OriginX + minI * _base.Step - pad, OriginZ + minJ * _base.Step - pad);
        maximum = new Vec2(OriginX + maxI * _base.Step + pad, OriginZ + maxJ * _base.Step + pad);
        return true;
    }

    /// <summary>
    /// Capture edited terrain and reuse realized immutable tiles outside its changed region.
    /// Neither the old surface nor any cached array is mutated. Capture requires terrain to be stable.
    /// </summary>
    public SoilSurface Rebase(Heightfield edited)
    {
        var next = new SoilSurface(edited, Parameters);
        if (!next.GeometryCompatible(this)) return next;
        bool changed = next.TryGetChangedBounds(this, out var lo, out var hi);
        foreach (var (key, lazy) in _tiles)
        {
            // Do not retain an unevaluated factory, which captures its original surface.
            if (!lazy.IsValueCreated) continue;
            double x0 = OriginX + key.X * TileSize * Spacing, z0 = OriginZ + key.Z * TileSize * Spacing;
            double x1 = x0 + (TileSize - 1) * Spacing, z1 = z0 + (TileSize - 1) * Spacing;
            if (changed && x1 >= lo.X && x0 <= hi.X && z1 >= lo.Z && z0 <= hi.Z) continue;
            var values = lazy.Value;
            // This factory captures only the immutable array, never the old surface or its heightfield.
            var reused = new Lazy<double[]>(() => values, LazyThreadSafetyMode.ExecutionAndPublication);
            _ = reused.Value;
            next._tiles.TryAdd(key, reused);
        }
        return next;
    }

    /// <summary>Canonical terrain-aligned lattice position; use these vertices and the 00–11 diagonal. Spacing is at most TargetSpacing.</summary>
    public Vec3 Vertex(long i, long j)
    {
        double x = OriginX + i * Spacing, z = OriginZ + j * Spacing;
        return new Vec3(x, VertexHeight(i, j), z);
    }

    private double VertexHeight(long i, long j)
    {
        long maxX = (long)(_base.Nx - 1) * Subdivisions, maxZ = (long)(_base.Nz - 1) * Subdivisions;
        // Only the finite terrain extent may populate the cache; arbitrary outside queries never grow it.
        if (i < 0 || j < 0 || i > maxX || j > maxZ) return UncachedVertexHeight(i, j);
        long tx = i / TileSize, tz = j / TileSize;
        var key = (tx, tz);
        if (!_tiles.TryGetValue(key, out var entry))
            entry = _tiles.GetOrAdd(key, static (k, owner) => new Lazy<double[]>(() => owner.BuildTile(k.Item1, k.Item2), LazyThreadSafetyMode.ExecutionAndPublication), this);
        var tile = entry.Value;
        return tile[(int)(j % TileSize) * TileSize + (int)(i % TileSize)];
    }

    private double[] BuildTile(long tx, long tz)
    {
        var values = new double[TileSize * TileSize];
        for (int j = 0; j < TileSize; j++) for (int i = 0; i < TileSize; i++)
            values[j * TileSize + i] = UncachedVertexHeight(tx * TileSize + i, tz * TileSize + j);
        return values;
    }

    private double UncachedVertexHeight(long i, long j)
    {
        double x = OriginX + i * Spacing, z = OriginZ + j * Spacing;
        double relief = Parameters.Amplitude == 0 ? 0 : Parameters.Amplitude * (
            0.50 * Field(Parameters.Seed, x / 0.075, z / 0.075) +
            0.30 * Field(Parameters.Seed ^ 0x9e3779b97f4a7c15UL, x / 0.16, z / 0.16) +
            0.20 * Field(Parameters.Seed ^ 0xd1b54a32d192ed03UL, x / 0.0375, z / 0.0375));
        return _base.MacroHeight(new Vec2(x, z)) + relief;
    }

    /// <summary>Exact piecewise planar sample, including normal of the same triangle.</summary>
    public SoilSurfaceSample Sample(double x, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z) || Math.Abs((x - OriginX) / Spacing) > long.MaxValue - 2.0 || Math.Abs((z - OriginZ) / Spacing) > long.MaxValue - 2.0)
            throw new ArgumentOutOfRangeException(nameof(x));
        long i = (long)Math.Floor((x - OriginX) / Spacing), j = (long)Math.Floor((z - OriginZ) / Spacing);
        double u = (x - (OriginX + i * Spacing)) / Spacing, v = (z - (OriginZ + j * Spacing)) / Spacing;
        double a = VertexHeight(i, j), b, c, height, dx, dz;
        if (u >= v)
        {
            b = VertexHeight(i + 1, j); c = VertexHeight(i + 1, j + 1);
            dx = (b - a) / Spacing; dz = (c - b) / Spacing;
            height = a + u * (b - a) + v * (c - b);
        }
        else
        {
            b = VertexHeight(i, j + 1); c = VertexHeight(i + 1, j + 1);
            dz = (b - a) / Spacing; dx = (c - b) / Spacing;
            height = a + v * (b - a) + u * (c - b);
        }
        return new SoilSurfaceSample(height, new Vec3(-dx, 1, -dz).Normalized());
    }

    /// <summary>Height of the same physical triangles without calculating a normal.</summary>
    public double Height(double x, double z) => HeightCore(x, z, false);

    // The uncached path is exposed for diagnostic comparisons, not a different surface.
    public double UncachedHeight(double x, double z) => HeightCore(x, z, true);
    private double HeightCore(double x, double z, bool uncached)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z) || Math.Abs((x - OriginX) / Spacing) > long.MaxValue - 2.0 || Math.Abs((z - OriginZ) / Spacing) > long.MaxValue - 2.0)
            throw new ArgumentOutOfRangeException(nameof(x));
        long i = (long)Math.Floor((x - OriginX) / Spacing), j = (long)Math.Floor((z - OriginZ) / Spacing);
        double u = (x - (OriginX + i * Spacing)) / Spacing, v = (z - (OriginZ + j * Spacing)) / Spacing;
        double Get(long a, long b) => uncached ? UncachedVertexHeight(a, b) : VertexHeight(a, b);
        double a = Get(i, j), c = Get(i + 1, j + 1);
        if (u >= v) { double b = Get(i + 1, j); return a + u * (b - a) + v * (c - b); }
        else { double b = Get(i, j + 1); return a + v * (b - a) + u * (c - b); }
    }

    /// <summary>Interpolated vertex-gradient normal for shading only; physical contact uses Sample.Normal.</summary>
    public Vec3 ShadingNormal(double x, double z)
    {
        if (!double.IsFinite(x) || !double.IsFinite(z) || Math.Abs((x - OriginX) / Spacing) > long.MaxValue - 3.0 || Math.Abs((z - OriginZ) / Spacing) > long.MaxValue - 3.0)
            throw new ArgumentOutOfRangeException(nameof(x));
        long i = (long)Math.Floor((x - OriginX) / Spacing), j = (long)Math.Floor((z - OriginZ) / Spacing);
        double u = (x - (OriginX + i * Spacing)) / Spacing, v = (z - (OriginZ + j * Spacing)) / Spacing;
        Vec3 N(long a, long b) => new(-(VertexHeight(a + 1, b) - VertexHeight(a - 1, b)) / (2 * Spacing), 1, -(VertexHeight(a, b + 1) - VertexHeight(a, b - 1)) / (2 * Spacing));
        var n00 = N(i, j); var n11 = N(i + 1, j + 1);
        var n = u >= v ? n00 * (1 - u) + N(i + 1, j) * (u - v) + n11 * v : n00 * (1 - v) + N(i, j + 1) * (v - u) + n11 * u;
        return n.Normalized();
    }

    // Smooth interpolated, seeded value fields form one continuous sheet of rises and recesses.
    private static double Field(ulong seed, double x, double z)
    {
        long i = (long)Math.Floor(x), j = (long)Math.Floor(z);
        double u = x - i, v = z - j;
        u = u * u * (3 - 2 * u); v = v * v * (3 - 2 * v);
        double a = Value(seed, i, j), b = Value(seed, i + 1, j);
        double c = Value(seed, i, j + 1), d = Value(seed, i + 1, j + 1);
        return (a + u * (b - a)) * (1 - v) + (c + u * (d - c)) * v;
    }

    private static double Value(ulong seed, long i, long j) =>
        (Rng.Mix(Rng.Mix(seed, unchecked((ulong)i)), unchecked((ulong)j)) >> 11) * (1.0 / (1UL << 53)) * 2 - 1;
}
