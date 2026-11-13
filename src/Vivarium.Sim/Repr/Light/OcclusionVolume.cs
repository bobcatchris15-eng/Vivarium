// L1 - cached GEOMETRY occlusion volume (no light colour or energy baked in).
// Extinction (optical density per metre) is deposited from triangles / a heightfield into a dense voxel grid,
// remembering per source which voxels it touched so Remove/re-add is local. Bake marches 9 fixed rays per voxel
// through the extinction grid and stores sky visibility, bent normal and 6 directional openness values.
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.Repr.Light;

public sealed class OcclusionVolume : ILightTransportField
{
    /// Opacity-per-area that makes a surface effectively opaque at any voxel size used here.
    public const double OpaqueOpacity = 100.0;
    /// Source id used by <see cref="AddHeightfield"/>.
    public const ulong HeightfieldId = ulong.MaxValue;

    // Fixed-point extinction so that Add/Remove are exactly reversible (integer arithmetic).
    private const double FixedScale = 4096.0;
    private const double MaxSigma = 1000.0;      // per metre cap per deposit
    private const int Stride = 10;               // vis, bent(3), open0..5

    // Ray set: 0 zenith, 1..4 ring at 55 deg (azimuth 0,90,180,270 = +X,+Z,-X,-Z), 5..8 ring at 20 deg (45,135,225,315).
    private static readonly double[] DirX = new double[9], DirY = new double[9], DirZ = new double[9], Weight = new double[9];

    static OcclusionVolume()
    {
        DirX[0] = 0; DirY[0] = 1; DirZ[0] = 0;
        for (int k = 0; k < 4; k++)
        {
            double az = k * Math.PI / 2, el = 55 * Math.PI / 180;
            DirX[1 + k] = Math.Cos(el) * Math.Cos(az); DirY[1 + k] = Math.Sin(el); DirZ[1 + k] = Math.Cos(el) * Math.Sin(az);
            double az2 = az + Math.PI / 4, el2 = 20 * Math.PI / 180;
            DirX[5 + k] = Math.Cos(el2) * Math.Cos(az2); DirY[5 + k] = Math.Sin(el2); DirZ[5 + k] = Math.Cos(el2) * Math.Sin(az2);
        }
        // Cosine-weighted hemisphere share of each elevation band (zenith cap 72.5..90, ring55 37.5..72.5, ring20 0..37.5).
        static double S2(double deg) { double s = Math.Sin(deg * Math.PI / 180); return s * s; }
        Weight[0] = 1 - S2(72.5);
        for (int k = 0; k < 4; k++) { Weight[1 + k] = (S2(72.5) - S2(37.5)) / 4; Weight[5 + k] = S2(37.5) / 4; }
    }

    private sealed class Source
    {
        public readonly Dictionary<int, long> Voxels = new();
        public int MinX = int.MaxValue, MinY = int.MaxValue, MinZ = int.MaxValue, MaxX = int.MinValue, MaxY = int.MinValue, MaxZ = int.MinValue;
        public void Touch(int x, int y, int z)
        {
            if (x < MinX) MinX = x; if (y < MinY) MinY = y; if (z < MinZ) MinZ = z;
            if (x > MaxX) MaxX = x; if (y > MaxY) MaxY = y; if (z > MaxZ) MaxZ = z;
        }
    }

    private readonly Dictionary<ulong, Source> _sources = new();
    private readonly long[] _ext;
    private readonly float[] _baked;
    private readonly Vec3 _min;
    private readonly double _d, _inv;
    private readonly int _nx, _ny, _nz;
    private readonly double _trace;
    private ulong _version;

    public Aabb Bounds { get; }
    public double VoxelSize => _d;
    public double TraceDistance => _trace;
    public int Nx => _nx;
    public int Ny => _ny;
    public int Nz => _nz;
    public long VoxelCount => (long)_nx * _ny * _nz;
    public ulong Version => _version;
    /// CPU bytes held by the dense grids (extinction + baked); per-source records excluded.
    public long CpuBytes => (long)_ext.Length * sizeof(long) + (long)_baked.Length * sizeof(float);
    public long SourceRecordBytes { get { long n = 0; foreach (var s in _sources.Values) n += s.Voxels.Count * 16L; return n; } }
    /// Bytes of one packed RGBA8 volume.
    public long PackedBytes => VoxelCount * 4;

    public OcclusionVolume(Aabb bounds, double voxelSize = 0.10, double traceDistance = 3.0)
    {
        if (voxelSize <= 0) throw new ArgumentException("voxelSize");
        _d = voxelSize; _inv = 1.0 / voxelSize; _trace = traceDistance; _min = bounds.Min;
        _nx = Math.Max(1, (int)Math.Ceiling((bounds.Max.X - bounds.Min.X) * _inv - 1e-9));
        _ny = Math.Max(1, (int)Math.Ceiling((bounds.Max.Y - bounds.Min.Y) * _inv - 1e-9));
        _nz = Math.Max(1, (int)Math.Ceiling((bounds.Max.Z - bounds.Min.Z) * _inv - 1e-9));
        Bounds = new Aabb(bounds.Min, new Vec3(_min.X + _nx * _d, _min.Y + _ny * _d, _min.Z + _nz * _d));
        _ext = new long[VoxelCount];
        _baked = new float[VoxelCount * Stride];
        for (long i = 0; i < VoxelCount; i++) SetOpen(i);
    }

    private void SetOpen(long i)
    {
        long o = i * Stride;
        _baked[o] = 1f; _baked[o + 1] = 0f; _baked[o + 2] = 0.7f; _baked[o + 3] = 0f;
        for (int k = 4; k < Stride; k++) _baked[o + k] = 1f;
    }

    private int Index(int x, int y, int z) => (z * _ny + y) * _nx + x;
    public Vec3 VoxelCentre(int x, int y, int z) => new(_min.X + (x + 0.5) * _d, _min.Y + (y + 0.5) * _d, _min.Z + (z + 0.5) * _d);
    public bool TryIndexOf(Vec3 p, out int x, out int y, out int z)
    {
        x = (int)Math.Floor((p.X - _min.X) * _inv); y = (int)Math.Floor((p.Y - _min.Y) * _inv); z = (int)Math.Floor((p.Z - _min.Z) * _inv);
        return x >= 0 && y >= 0 && z >= 0 && x < _nx && y < _ny && z < _nz;
    }

    /// Extinction (per metre) of the voxel containing p; 0 outside.
    public double ExtinctionAt(Vec3 p) => TryIndexOf(p, out int x, out int y, out int z) ? _ext[Index(x, y, z)] / FixedScale : 0.0;

    /// Copy of the baked array (stride 10: vis, bent xyz, open0..5) for before/after comparisons.
    public float[] SnapshotBaked() => (float[])_baked.Clone();
    public long[] SnapshotExtinction() => (long[])_ext.Clone();
    public int SourceCount => _sources.Count;
    public bool HasSource(ulong id) => _sources.ContainsKey(id);

    // ------------------------------------------------------------------ deposition

    private Aabb SourceBounds(Source s) => s.MinX == int.MaxValue ? new Aabb(_min, _min)
        : new Aabb(new Vec3(_min.X + s.MinX * _d, _min.Y + s.MinY * _d, _min.Z + s.MinZ * _d),
                   new Vec3(_min.X + (s.MaxX + 1) * _d, _min.Y + (s.MaxY + 1) * _d, _min.Z + (s.MaxZ + 1) * _d));

    /// Deposits triangle area into voxels: extinction += area / voxelVolume * opacityPerArea. May be called several
    /// times with the same sourceId (accumulates). Positions are x,y,z float triplets; transform is row-major 3x4
    /// (12 doubles: m00 m01 m02 tx, m10 .. ty, m20 .. tz) or null for identity. Returns the touched bounds.
    public Aabb AddTriangles(ulong sourceId, float[] positions, int[] indices, double[]? transform, double opacityPerArea)
    {
        if (!_sources.TryGetValue(sourceId, out var src)) _sources[sourceId] = src = new Source();
        var acc = new Dictionary<int, double>();
        double vvol = _d * _d * _d, half = 0.5 * _d;
        double[] m = transform ?? new double[] { 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0 };
        Span<double> v = stackalloc double[9];
        for (int t = 0; t + 2 < indices.Length; t += 3)
        {
            for (int k = 0; k < 3; k++)
            {
                int pi = indices[t + k] * 3;
                double x = positions[pi], y = positions[pi + 1], z = positions[pi + 2];
                v[k * 3] = m[0] * x + m[1] * y + m[2] * z + m[3];
                v[k * 3 + 1] = m[4] * x + m[5] * y + m[6] * z + m[7];
                v[k * 3 + 2] = m[8] * x + m[9] * y + m[10] * z + m[11];
            }
            double e1x = v[3] - v[0], e1y = v[4] - v[1], e1z = v[5] - v[2];
            double e2x = v[6] - v[0], e2y = v[7] - v[1], e2z = v[8] - v[2];
            double cx = e1y * e2z - e1z * e2y, cy = e1z * e2x - e1x * e2z, cz = e1x * e2y - e1y * e2x;
            double area = 0.5 * Math.Sqrt(cx * cx + cy * cy + cz * cz);
            if (area <= 1e-14) continue;
            double e3x = v[6] - v[3], e3y = v[7] - v[4], e3z = v[8] - v[5];
            double maxEdge = Math.Sqrt(Math.Max(e1x * e1x + e1y * e1y + e1z * e1z, Math.Max(e2x * e2x + e2y * e2y + e2z * e2z, e3x * e3x + e3y * e3y + e3z * e3z)));
            int n = Math.Max(1, (int)Math.Ceiling(maxEdge / half));
            double a = area / ((double)n * n);
            // n^2 sub-triangles of equal area, deposited at their centroids.
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n - i; j++)
                {
                    DepositAt(acc, v, (i + 1.0 / 3) / n, (j + 1.0 / 3) / n, a, opacityPerArea, vvol);
                    if (j < n - i - 1) DepositAt(acc, v, (i + 2.0 / 3) / n, (j + 2.0 / 3) / n, a, opacityPerArea, vvol);
                }
        }
        foreach (var (idx, sigma) in acc)
        {
            long add = (long)Math.Round(Math.Min(sigma, MaxSigma) * FixedScale);
            if (add == 0) continue;
            _ext[idx] += add;
            src.Voxels[idx] = src.Voxels.TryGetValue(idx, out var o) ? o + add : add;
            int x = idx % _nx, y = (idx / _nx) % _ny, z = idx / (_nx * _ny);
            src.Touch(x, y, z);
        }
        return SourceBounds(src);
    }

    private void DepositAt(Dictionary<int, double> acc, Span<double> v, double u, double w, double area, double opacity, double vvol)
    {
        double px = v[0] + (v[3] - v[0]) * u + (v[6] - v[0]) * w;
        double py = v[1] + (v[4] - v[1]) * u + (v[7] - v[1]) * w;
        double pz = v[2] + (v[5] - v[2]) * u + (v[8] - v[2]) * w;
        if (!TryIndexOf(new Vec3(px, py, pz), out int x, out int y, out int z)) return;
        int idx = Index(x, y, z);
        acc[idx] = (acc.TryGetValue(idx, out var s) ? s : 0.0) + area / vvol * opacity;
    }

    /// Everything below height(x,z) inside region becomes opaque (partial voxels proportionally). Replaces any
    /// earlier heightfield. Returns touched bounds.
    public Aabb AddHeightfield(Func<double, double, double> height, Aabb region)
    {
        if (_sources.ContainsKey(HeightfieldId)) Remove(HeightfieldId);
        var src = new Source(); _sources[HeightfieldId] = src;
        int x0 = Math.Max(0, (int)Math.Floor((region.Min.X - _min.X) * _inv)), x1 = Math.Min(_nx - 1, (int)Math.Floor((region.Max.X - _min.X) * _inv - 1e-9));
        int z0 = Math.Max(0, (int)Math.Floor((region.Min.Z - _min.Z) * _inv)), z1 = Math.Min(_nz - 1, (int)Math.Floor((region.Max.Z - _min.Z) * _inv - 1e-9));
        for (int z = z0; z <= z1; z++)
            for (int x = x0; x <= x1; x++)
            {
                double h = height(_min.X + (x + 0.5) * _d, _min.Z + (z + 0.5) * _d);
                for (int y = 0; y < _ny; y++)
                {
                    double y0 = _min.Y + y * _d;
                    double frac = Math.Clamp((h - y0) * _inv, 0.0, 1.0);
                    if (frac <= 0) break;
                    long add = (long)Math.Round(frac * MaxSigma * FixedScale);
                    int idx = Index(x, y, z);
                    _ext[idx] += add; src.Voxels[idx] = add; src.Touch(x, y, z);
                }
            }
        return SourceBounds(src);
    }

    /// Removes everything a source deposited; returns the bounds it touched (null if unknown id).
    public Aabb? Remove(ulong sourceId)
    {
        if (!_sources.Remove(sourceId, out var src)) return null;
        foreach (var (idx, amt) in src.Voxels) _ext[idx] -= amt;
        return SourceBounds(src);
    }

    // ------------------------------------------------------------------ bake

    /// Bake every voxel.
    public void BakeAll() => BakeBox(0, _nx - 1, 0, _ny - 1, 0, _nz - 1);

    /// Bake the voxels inside region; with expand (default) the region is grown by TraceDistance + one voxel so
    /// neighbours whose light changed are included. Voxels outside the box are left bit-identical.
    public void Bake(Aabb region, bool expand = true)
    {
        double e = expand ? _trace + _d : 0;
        int x0 = Math.Max(0, (int)Math.Floor((region.Min.X - e - _min.X) * _inv)), x1 = Math.Min(_nx - 1, (int)Math.Floor((region.Max.X + e - _min.X) * _inv));
        int y0 = Math.Max(0, (int)Math.Floor((region.Min.Y - e - _min.Y) * _inv)), y1 = Math.Min(_ny - 1, (int)Math.Floor((region.Max.Y + e - _min.Y) * _inv));
        int z0 = Math.Max(0, (int)Math.Floor((region.Min.Z - e - _min.Z) * _inv)), z1 = Math.Min(_nz - 1, (int)Math.Floor((region.Max.Z + e - _min.Z) * _inv));
        if (x0 > x1 || y0 > y1 || z0 > z1) return;
        BakeBox(x0, x1, y0, y1, z0, z1);
    }

    /// Last bake box (voxel indices, inclusive) for upload bookkeeping.
    public (int x0, int x1, int y0, int y1, int z0, int z1) LastBakeBox { get; private set; }
    public long LastBakeVoxels { get; private set; }
    /// Cumulative number of voxels baked since construction.
    public long BakedVoxelCounter { get; private set; }
    public Aabb SnapshotBounds(ulong sourceId) => _sources.TryGetValue(sourceId, out var s) ? SourceBounds(s) : new Aabb(_min, _min);

    private void BakeBox(int x0, int x1, int y0, int y1, int z0, int z1)
    {
        LastBakeBox = (x0, x1, y0, y1, z0, z1);
        LastBakeVoxels = (long)(x1 - x0 + 1) * (y1 - y0 + 1) * (z1 - z0 + 1);
        BakedVoxelCounter += LastBakeVoxels;
        Parallel.For(z0, z1 + 1, z =>
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++) BakeOne(x, y, z);
        });
        _version++;
    }

    private void BakeOne(int ix, int iy, int iz)
    {
        double px = _min.X + (ix + 0.5) * _d, py = _min.Y + (iy + 0.5) * _d, pz = _min.Z + (iz + 0.5) * _d;
        Span<double> T = stackalloc double[9];
        double step = 0.5 * _d, t0 = 0.9 * _d;
        int maxSteps = (int)Math.Ceiling((_trace - t0) / step);
        for (int k = 0; k < 9; k++)
        {
            double tau = 0, dx = DirX[k], dy = DirY[k], dz = DirZ[k];
            double t = t0;
            for (int s = 0; s <= maxSteps; s++, t += step)
            {
                int x = (int)Math.Floor((px + dx * t - _min.X) * _inv);
                int y = (int)Math.Floor((py + dy * t - _min.Y) * _inv);
                int z = (int)Math.Floor((pz + dz * t - _min.Z) * _inv);
                if (x < 0 || y < 0 || z < 0 || x >= _nx || y >= _ny || z >= _nz) break; // left the volume: open beyond
                long e = _ext[(z * _ny + y) * _nx + x];
                if (e != 0)
                {
                    tau += e * (1.0 / FixedScale) * step;
                    if (tau > 8.0) break;
                }
            }
            T[k] = tau > 8.0 ? 0.0 : Math.Exp(-tau);
        }
        double vis = 0, bx = 0, by = 0, bz = 0;
        for (int k = 0; k < 9; k++)
        {
            double w = Weight[k] * T[k];
            vis += w; bx += w * DirX[k]; by += w * DirY[k]; bz += w * DirZ[k];
        }
        long o = (long)Index(ix, iy, iz) * Stride;
        _baked[o] = (float)vis; _baked[o + 1] = (float)bx; _baked[o + 2] = (float)by; _baked[o + 3] = (float)bz;
        _baked[o + 4] = (float)T[1]; _baked[o + 5] = (float)T[3]; _baked[o + 6] = (float)T[2]; _baked[o + 7] = (float)T[4]; // +X -X +Z -Z (ring 55)
        _baked[o + 8] = (float)T[0];                                                                                      // up-high (zenith)
        _baked[o + 9] = (float)((T[5] + T[6] + T[7] + T[8]) * 0.25);                                                      // up-low (ring 20 mean)
    }

    public void Invalidate(IReadOnlyList<DirtyRecord> records)
    {
        // Merge overlapping expanded boxes so shared voxels are baked once.
        var boxes = new List<(int x0, int x1, int y0, int y1, int z0, int z1)>();
        foreach (var r in records)
        {
            if ((r.Domain & (DirtyDomain.Light | DirtyDomain.Geometry | DirtyDomain.Vegetation)) == 0) continue;
            if (!r.Bounds.Intersects(Bounds)) continue;
            double e = _trace + _d;
            var b = (x0: Math.Max(0, (int)Math.Floor((r.Bounds.Min.X - e - _min.X) * _inv)), x1: Math.Min(_nx - 1, (int)Math.Floor((r.Bounds.Max.X + e - _min.X) * _inv)),
                     y0: Math.Max(0, (int)Math.Floor((r.Bounds.Min.Y - e - _min.Y) * _inv)), y1: Math.Min(_ny - 1, (int)Math.Floor((r.Bounds.Max.Y + e - _min.Y) * _inv)),
                     z0: Math.Max(0, (int)Math.Floor((r.Bounds.Min.Z - e - _min.Z) * _inv)), z1: Math.Min(_nz - 1, (int)Math.Floor((r.Bounds.Max.Z + e - _min.Z) * _inv)));
            if (b.x0 > b.x1 || b.y0 > b.y1 || b.z0 > b.z1) continue;
            for (int i = 0; i < boxes.Count; i++)
            {
                var o = boxes[i];
                if (b.x0 <= o.x1 && b.x1 >= o.x0 && b.y0 <= o.y1 && b.y1 >= o.y0 && b.z0 <= o.z1 && b.z1 >= o.z0)
                {
                    b = (Math.Min(b.x0, o.x0), Math.Max(b.x1, o.x1), Math.Min(b.y0, o.y0), Math.Max(b.y1, o.y1), Math.Min(b.z0, o.z0), Math.Max(b.z1, o.z1));
                    boxes.RemoveAt(i--);
                }
            }
            boxes.Add(b);
        }
        foreach (var b in boxes) BakeBox(b.x0, b.x1, b.y0, b.y1, b.z0, b.z1);
    }

    // ------------------------------------------------------------------ sampling

    public LightTransportSample Sample(Vec3 p)
    {
        double fx = (p.X - _min.X) * _inv - 0.5, fy = (p.Y - _min.Y) * _inv - 0.5, fz = (p.Z - _min.Z) * _inv - 0.5;
        if (fx < -0.5 || fy < -0.5 || fz < -0.5 || fx > _nx - 0.5 || fy > _ny - 0.5 || fz > _nz - 0.5)
            return new LightTransportSample { SkyVisibility = 1f, BentNormal = Vec3.Up, Open0 = 1, Open1 = 1, Open2 = 1, Open3 = 1, Open4 = 1, Open5 = 1 };
        int x0 = (int)Math.Floor(fx), y0 = (int)Math.Floor(fy), z0 = (int)Math.Floor(fz);
        double tx = fx - x0, ty = fy - y0, tz = fz - z0;
        Span<double> acc = stackalloc double[Stride];
        for (int c = 0; c < 8; c++)
        {
            int dx = c & 1, dy = (c >> 1) & 1, dz = (c >> 2) & 1;
            int x = Math.Clamp(x0 + dx, 0, _nx - 1), y = Math.Clamp(y0 + dy, 0, _ny - 1), z = Math.Clamp(z0 + dz, 0, _nz - 1);
            double w = (dx == 1 ? tx : 1 - tx) * (dy == 1 ? ty : 1 - ty) * (dz == 1 ? tz : 1 - tz);
            long o = (long)Index(x, y, z) * Stride;
            for (int k = 0; k < Stride; k++) acc[k] += w * _baked[o + k];
        }
        return new LightTransportSample
        {
            SkyVisibility = (float)acc[0], BentNormal = new Vec3(acc[1], acc[2], acc[3]),
            Open0 = (float)acc[4], Open1 = (float)acc[5], Open2 = (float)acc[6], Open3 = (float)acc[7], Open4 = (float)acc[8], Open5 = (float)acc[9],
        };
    }

    // ------------------------------------------------------------------ GPU export
    // Layout of every packed buffer: [y][z][x][channel], x fastest. A Godot Texture3D is built as
    // width = nx, height = nz, depth = ny (one Image per Y layer).

    /// (bentNormal.xyz * 0.5 + 0.5, skyVisibility) for the whole volume.
    public byte[] PackRgba8(out int nx, out int ny, out int nz)
        => PackRegion(new Aabb(Bounds.Min, Bounds.Max), 0, out _, out _, out _, out nx, out ny, out nz);

    /// Open0..3 (+X,-X,+Z,-Z at 55 deg elevation) for the whole volume.
    public byte[] PackOpenRgba8(out int nx, out int ny, out int nz)
        => PackRegion(new Aabb(Bounds.Min, Bounds.Max), 1, out _, out _, out _, out nx, out ny, out nz);

    /// Pack the voxels intersecting region. volume 0 = bent normal + visibility, 1 = Open0..3. Outputs the
    /// voxel origin and size of the packed sub-box.
    public byte[] PackRegion(Aabb region, int volume, out int x0, out int y0, out int z0, out int nx, out int ny, out int nz)
    {
        x0 = Math.Max(0, (int)Math.Floor((region.Min.X - _min.X) * _inv)); int x1 = Math.Min(_nx - 1, (int)Math.Ceiling((region.Max.X - _min.X) * _inv) - 1);
        y0 = Math.Max(0, (int)Math.Floor((region.Min.Y - _min.Y) * _inv)); int y1 = Math.Min(_ny - 1, (int)Math.Ceiling((region.Max.Y - _min.Y) * _inv) - 1);
        z0 = Math.Max(0, (int)Math.Floor((region.Min.Z - _min.Z) * _inv)); int z1 = Math.Min(_nz - 1, (int)Math.Ceiling((region.Max.Z - _min.Z) * _inv) - 1);
        nx = Math.Max(0, x1 - x0 + 1); ny = Math.Max(0, y1 - y0 + 1); nz = Math.Max(0, z1 - z0 + 1);
        var bytes = new byte[(long)nx * ny * nz * 4];
        int w = 0;
        for (int y = y0; y <= y1; y++)
            for (int z = z0; z <= z1; z++)
                for (int x = x0; x <= x1; x++)
                {
                    long o = (long)Index(x, y, z) * Stride;
                    if (volume == 0)
                    {
                        double bx = _baked[o + 1], by = _baked[o + 2], bz = _baked[o + 3];
                        double l = Math.Sqrt(bx * bx + by * by + bz * bz);
                        if (l < 1e-6) { bx = 0; by = 1; bz = 0; } else { bx /= l; by /= l; bz /= l; }
                        bytes[w++] = Q(bx * 0.5 + 0.5); bytes[w++] = Q(by * 0.5 + 0.5); bytes[w++] = Q(bz * 0.5 + 0.5); bytes[w++] = Q(_baked[o]);
                    }
                    else
                    {
                        bytes[w++] = Q(_baked[o + 4]); bytes[w++] = Q(_baked[o + 5]); bytes[w++] = Q(_baked[o + 6]); bytes[w++] = Q(_baked[o + 7]);
                    }
                }
        return bytes;
    }

    private static byte Q(double v) => (byte)Math.Clamp((int)Math.Round(v * 255.0), 0, 255);
}
