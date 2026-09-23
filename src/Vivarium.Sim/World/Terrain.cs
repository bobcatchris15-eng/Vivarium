using Vivarium.Sim.Content;
using Vivarium.Sim.Core;

namespace Vivarium.Sim.World;

/// <summary>
/// Authoritative terrain surface: a vertex grid (spacing = Step) covering the hexagon's bounds plus one
/// ring of margin. Each grid square is split along its (i,j)-(i+1,j+1) diagonal into two planar triangles.
/// <see cref="Height"/> interpolates on those same triangles, so queries, collision and the rendered mesh
/// agree exactly.
/// </summary>
public sealed class Heightfield
{
    public double Step { get; }
    public double OriginX { get; }
    public double OriginZ { get; }
    public int Nx { get; }   // vertices along x
    public int Nz { get; }
    public double[] H { get; }
    public double MinHeight { get; }
    public double MaxHeight { get; }
    public double Bottom { get; }
    /// <summary>Bumped on every sculpt so renderers and derived caches know to rebuild.</summary>
    public int Version { get; private set; }
    /// <summary>The procedurally generated heights, captured on the first edit (saves store only the difference).</summary>
    private double[]? _baseline;

    private Heightfield(double step, double ox, double oz, int nx, int nz, double min, double max, double bottom)
    {
        Step = step; OriginX = ox; OriginZ = oz; Nx = nx; Nz = nz; MinHeight = min; MaxHeight = max; Bottom = bottom;
        H = new double[nx * nz];
    }

    public static Heightfield Generate(WorldDescriptor d, HexDomain domain)
    {
        double step = d.CellSize / 2;
        var (minX, maxX, minZ, maxZ) = domain.Bounds;
        int nx = (int)Math.Ceiling((maxX - minX) / step) + 3;
        int nz = (int)Math.Ceiling((maxZ - minZ) / step) + 3;
        double ox = -(nx - 1) * step / 2, oz = -(nz - 1) * step / 2;
        var t = d.Terrain;
        var hf = new Heightfield(step, ox, oz, nx, nz, t.MinHeight, t.MaxHeight, t.Bottom);
        ulong seed = Rng.Mix(d.Seed, Hash.Fnv1a64("terrain.height"));
        for (int j = 0; j < nz; j++)
            for (int i = 0; i < nx; i++)
            {
                double x = ox + i * step, z = oz + j * step;
                hf.H[j * nx + i] = RawHeight(seed, t, x, z, domain);
            }
        return hf;
    }

    /// <summary>Procedural height at (x,z) before triangulation: fBm relief + profile features, clamped to limits.</summary>
    public static double RawHeight(ulong seed, TerrainProfile t, double x, double z, HexDomain domain)
    {
        double h = t.BaseHeight + t.Relief * Noise.Fbm(seed, x * t.NoiseScale, z * t.NoiseScale, t.Octaves);
        // fine micro-relief keeps close-up ground organic
        h += 0.04 * Noise.Fbm(Rng.Mix(seed, 77), x * 1.7, z * 1.7, 2);
        var p = new Vec2(x, z);
        foreach (var f in t.Features)
        {
            switch (f.Type)
            {
                case "hill":
                case "basin":
                {
                    double dd = Vec2.Distance(p, new Vec2(f.X, f.Z)) / f.Radius;
                    if (dd < 1) { double w = (1 - dd * dd); w *= w; h += (f.Type == "hill" ? 1 : -1) * Math.Abs(f.Amount) * w; }
                    break;
                }
                case "channel":
                case "ridge":
                {
                    Vec2 a = new(f.X, f.Z), b = new(f.ToX, f.ToZ), ab = b - a;
                    double tt = ab.LengthSq > 1e-12 ? MathD.Clamp01((p - a).Dot(ab) / ab.LengthSq) : 0;
                    double dd = Vec2.Distance(p, a + ab * tt) / f.Width;
                    if (dd < 1) { double w = 0.5 + 0.5 * Math.Cos(Math.PI * dd); h += (f.Type == "ridge" ? 1 : -1) * Math.Abs(f.Amount) * w; }
                    break;
                }
            }
        }
        // soft clamp: compress smoothly within 15 cm of the limits, then hard-clamp
        double lo = t.MinHeight, hi = t.MaxHeight, band = Math.Min(0.15, (hi - lo) / 4);
        if (h > hi - band) h = hi - band + band * Math.Tanh((h - (hi - band)) / band);
        if (h < lo + band) h = lo + band - band * Math.Tanh(((lo + band) - h) / band);
        return MathD.Clamp(h, lo, hi);
    }

    /// <summary>Call before changing <see cref="H"/>; remembers the generated baseline for delta saves.</summary>
    public void BeginEdit() => _baseline ??= (double[])H.Clone();
    public void Touch() => Version++;
    public int VertexIndex(int i, int j) => j * Nx + i;

    /// <summary>Per-vertex difference from the generated terrain, or null when the terrain was never edited.</summary>
    public double[]? ExportDelta()
    {
        if (_baseline == null) return null;
        var d = new double[H.Length];
        bool any = false;
        for (int k = 0; k < H.Length; k++) { d[k] = H[k] - _baseline[k]; any |= d[k] != 0; }
        return any ? d : null;
    }

    /// <summary>Re-applies a saved <see cref="ExportDelta"/> onto freshly generated terrain.</summary>
    public void ApplyDelta(double[] delta)
    {
        if (delta.Length != H.Length) throw new InvalidDataException($"terrain edit grid mismatch: save has {delta.Length} vertices, world has {H.Length}");
        BeginEdit();
        for (int k = 0; k < H.Length; k++)
        {
            double h = _baseline![k] + delta[k];
            if (!double.IsFinite(h) || h < MinHeight - 1e-9 || h > MaxHeight + 1e-9) throw new InvalidDataException($"terrain edit {k} is out of range ({h})");
            H[k] = h;
        }
        Touch();
    }

    public double Vertex(int i, int j) => H[Math.Clamp(j, 0, Nz - 1) * Nx + Math.Clamp(i, 0, Nx - 1)];
    public Vec2 VertexPos(int i, int j) => new(OriginX + i * Step, OriginZ + j * Step);

    /// <summary>Surface elevation at (x,z) interpolated on the render triangulation.</summary>
    public double Height(Vec2 p)
    {
        double fx = (p.X - OriginX) / Step, fz = (p.Z - OriginZ) / Step;
        int i = Math.Clamp((int)Math.Floor(fx), 0, Nx - 2), j = Math.Clamp((int)Math.Floor(fz), 0, Nz - 2);
        double u = MathD.Clamp01(fx - i), v = MathD.Clamp01(fz - j);
        double h00 = H[j * Nx + i], h10 = H[j * Nx + i + 1], h01 = H[(j + 1) * Nx + i], h11 = H[(j + 1) * Nx + i + 1];
        // diagonal (0,0)-(1,1): triangle A = (00,10,11) when u >= v, triangle B = (00,11,01) otherwise
        return u >= v ? h00 + u * (h10 - h00) + v * (h11 - h10) : h00 + v * (h01 - h00) + u * (h11 - h01);
    }

    /// <summary>Unit surface normal of the triangle under (x,z).</summary>
    public Vec3 Normal(Vec2 p)
    {
        double fx = (p.X - OriginX) / Step, fz = (p.Z - OriginZ) / Step;
        int i = Math.Clamp((int)Math.Floor(fx), 0, Nx - 2), j = Math.Clamp((int)Math.Floor(fz), 0, Nz - 2);
        double u = fx - i, v = fz - j;
        double h00 = H[j * Nx + i], h10 = H[j * Nx + i + 1], h01 = H[(j + 1) * Nx + i], h11 = H[(j + 1) * Nx + i + 1];
        double dhdx, dhdz;
        if (u >= v) { dhdx = (h10 - h00) / Step; dhdz = (h11 - h10) / Step; }
        else { dhdz = (h01 - h00) / Step; dhdx = (h11 - h01) / Step; }
        return new Vec3(-dhdx, 1, -dhdz).Normalized();
    }

    public double Slope(Vec2 p) { var n = Normal(p); return Math.Acos(MathD.Clamp(n.Y, -1, 1)); }

    public string DigestHex() { using var d = new DigestBuilder(); d.Add(Step).Add(Nx).Add(Nz); d.Add(H); return d.Hex(); }
}

/// <summary>Simplified soil strata measured downward from the local surface; the last layer fills to the island bottom.</summary>
public sealed class StrataModel
{
    public IReadOnlyList<StratumDef> Layers { get; }
    public StrataModel(IReadOnlyList<StratumDef> layers) { Layers = layers; }

    /// <summary>Depth below surface at which layer k begins (layer 0 begins at 0).</summary>
    public double LayerTop(int k) { double d = 0; for (int i = 0; i < k; i++) d += Layers[i].Thickness; return d; }

    /// <summary>Index of the layer at depth metres below the surface.</summary>
    public int LayerAtDepth(double depth)
    {
        if (depth <= 0) return 0;
        double d = 0;
        for (int i = 0; i < Layers.Count - 1; i++) { d += Layers[i].Thickness; if (depth < d) return i; }
        return Layers.Count - 1;
    }

    public StratumDef LayerAt(Heightfield hf, Vec2 p, double y) => Layers[LayerAtDepth(hf.Height(p) - y)];
}
