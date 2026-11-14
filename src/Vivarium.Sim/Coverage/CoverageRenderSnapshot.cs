using System;
using System.Collections.Generic;

namespace Vivarium.Sim.Coverage;

/// <summary>Small invalidation stamp retained by renderer cache records after build arrays are released.</summary>
public sealed class CoverageRenderRevisionStamp
{
    internal readonly record struct Dependency(int Ti, int Tj, CoverageTile? Tile, long Version, ulong RenderFingerprint);
    private readonly CoverageLayer _source;
    private readonly Dependency[] _dependencies;

    internal CoverageRenderRevisionStamp(CoverageLayer source, Dependency[] dependencies)
    { _source = source; _dependencies = dependencies; }

    public bool IsCurrent()
    {
        foreach (var dependency in _dependencies)
        {
            bool exists = _source.TryGetTile(dependency.Ti, dependency.Tj, out var now) && now != null;
            if (dependency.Tile == null)
            {
                if (exists) return false;
            }
            else if (!ReferenceEquals(now, dependency.Tile) || now!.Version != dependency.Version)
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// Immutable render inputs for one coverage tile and its one-tile neighbor halo.
/// Coverage arrays are copied because the biology step may continue while a renderer
/// worker builds geometry. Terrain and props are separate, renderer-owned snapshots.
/// </summary>
public sealed class CoverageRenderSnapshot
{
    private readonly CoverageLayer _source;
    private readonly CoverageRenderRevisionStamp.Dependency[] _dependencies;

    public CoverageLayer Layer { get; }
    public CoverageTile Tile { get; }
    public long SourceVersion { get; }
    public CoverageRenderRevisionStamp RevisionStamp { get; }

    private CoverageRenderSnapshot(CoverageLayer source, CoverageLayer layer, CoverageTile tile,
        long sourceVersion, CoverageRenderRevisionStamp.Dependency[] dependencies)
    {
        _source = source;
        Layer = layer;
        Tile = tile;
        SourceVersion = sourceVersion;
        _dependencies = dependencies;
        RevisionStamp = new CoverageRenderRevisionStamp(source, dependencies);
    }

    /// <summary>Copies the target tile and adjacent tiles used by corner halo sampling.</summary>
    public static bool TryCapture(CoverageLayer source, int ti, int tj, out CoverageRenderSnapshot? snapshot)
    {
        snapshot = null;
        if (!source.TryGetStableWriteEpoch(out long captureEpoch)) return false;
        var dependencies = new List<CoverageRenderRevisionStamp.Dependency>(9);
        var copied = new List<(CoverageTile source, long version)>(9);
        var result = new CoverageLayer(source.Id, source.WorldSeed);

        for (int dz = -1; dz <= 1; dz++)
        for (int dx = -1; dx <= 1; dx++)
        {
            int x = ti + dx, z = tj + dz;
            if (!source.TryGetTile(x, z, out var tile) || tile == null)
            {
                dependencies.Add(new CoverageRenderRevisionStamp.Dependency(x, z, null, 0, 0));
                continue;
            }

            long version = tile.Version;
            var copy = CopyRenderInputs(tile);
            result.ReplaceSnapshotTile(copy);
            dependencies.Add(new CoverageRenderRevisionStamp.Dependency(x, z, tile, version, RenderFingerprint(copy)));
            copied.Add((tile, version));
        }

        // Do not publish a snapshot if any tracked source changed while its arrays were copied.
        foreach (var (tile, version) in copied)
            if (tile.Version != version) return false;
        foreach (var dependency in dependencies)
        {
            bool exists = source.TryGetTile(dependency.Ti, dependency.Tj, out var now) && now != null;
            if (dependency.Tile == null ? exists : !ReferenceEquals(now, dependency.Tile)) return false;
            if (dependency.Tile != null && RenderFingerprint(now!) != dependency.RenderFingerprint) return false;
        }
        if (!source.TryGetStableWriteEpoch(out long endEpoch) || endEpoch != captureEpoch) return false;

        if (!result.TryGetTile(ti, tj, out var target) || target == null) return false;
        snapshot = new CoverageRenderSnapshot(source, result, target, target.Version, dependencies.ToArray());
        return true;
    }

    /// <summary>Checks that coverage halo and renderer geometry dependencies still match the build.</summary>
    public bool IsCurrent(int terrainVersion, int expectedTerrainVersion,
        int propsVersion, int expectedPropsVersion)
    {
        if (terrainVersion != expectedTerrainVersion || propsVersion != expectedPropsVersion) return false;
        return _source.TryGetStableWriteEpoch(out long startEpoch) && CoverageRevisionsCurrent() && CoverageFingerprintsCurrent() &&
            CoverageRevisionsCurrent() && _source.TryGetStableWriteEpoch(out long endEpoch) && startEpoch == endEpoch;
    }

    /// <summary>Cheap cache-invalidation check for the target and sampled halo revisions.</summary>
    public bool CoverageRevisionsCurrent()
    {
        return RevisionStamp.IsCurrent();
    }

    private bool CoverageFingerprintsCurrent()
    {
        foreach (var dependency in _dependencies)
            if (dependency.Tile != null && RenderFingerprint(dependency.Tile) != dependency.RenderFingerprint)
                return false;
        return true;
    }

    private static CoverageTile CopyRenderInputs(CoverageTile source)
    {
        return new CoverageTile(source.Ti, source.Tj)
        {
            Occ = (byte[])source.Occ.Clone(),
            B = (float[])source.B.Clone(),
            W = (byte[])source.W.Clone(),
            Dorm = (byte[])source.Dorm.Clone(),
            Flags = (byte[])source.Flags.Clone(),
            D2E = (byte[])source.D2E.Clone(),
            Version = source.Version,
        };
    }

    private static ulong RenderFingerprint(CoverageTile tile)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        for (int i = 0; i < CoverageTile.N; i++)
        {
            hash = (hash ^ tile.Occ[i]) * prime;
            hash = (hash ^ (uint)BitConverter.SingleToInt32Bits(tile.B[i])) * prime;
            hash = (hash ^ tile.W[i]) * prime;
            hash = (hash ^ tile.Dorm[i]) * prime;
            hash = (hash ^ tile.Flags[i]) * prime;
            hash = (hash ^ tile.D2E[i]) * prime;
        }
        return hash;
    }
}
