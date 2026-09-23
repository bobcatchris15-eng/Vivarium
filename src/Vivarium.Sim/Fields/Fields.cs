using Vivarium.Sim.Core;
using Vivarium.Sim.World;

namespace Vivarium.Sim.Fields;

/// <summary>
/// Regular square cell grid covering the hexagon's bounding box. Cells whose centre lies inside the
/// hexagon are "in domain"; out-of-domain cells never hold simulation values.
/// </summary>
public sealed class GridSpec
{
    public double CellSize { get; }
    public double OriginX { get; }
    public double OriginZ { get; }
    public int Nx { get; }
    public int Nz { get; }
    public int Count => Nx * Nz;
    public HexDomain Domain { get; }
    private readonly bool[] _inDomain;
    /// <summary>In-domain cell indices in ascending order (deterministic iteration).</summary>
    public int[] DomainCells { get; }
    /// <summary>For each cell, true if at least one 4-neighbour is outside the domain.</summary>
    public bool[] IsBoundaryCell { get; }

    public GridSpec(HexDomain domain, double cellSize)
    {
        Domain = domain;
        CellSize = cellSize;
        var (minX, maxX, minZ, maxZ) = domain.Bounds;
        Nx = (int)Math.Ceiling((maxX - minX) / cellSize);
        Nz = (int)Math.Ceiling((maxZ - minZ) / cellSize);
        OriginX = -Nx * cellSize / 2;
        OriginZ = -Nz * cellSize / 2;
        _inDomain = new bool[Count];
        var list = new List<int>();
        for (int j = 0; j < Nz; j++)
            for (int i = 0; i < Nx; i++)
            {
                int idx = j * Nx + i;
                if (domain.SignedDistance(CellCenter(i, j)) < 0) { _inDomain[idx] = true; list.Add(idx); }
            }
        DomainCells = list.ToArray();
        IsBoundaryCell = new bool[Count];
        foreach (int idx in DomainCells)
        {
            int i = idx % Nx, j = idx / Nx;
            IsBoundaryCell[idx] = !InDomain(i - 1, j) || !InDomain(i + 1, j) || !InDomain(i, j - 1) || !InDomain(i, j + 1);
        }
    }

    public Vec2 CellCenter(int i, int j) => new(OriginX + (i + 0.5) * CellSize, OriginZ + (j + 0.5) * CellSize);
    public Vec2 CellCenter(int idx) => CellCenter(idx % Nx, idx / Nx);
    public bool InBounds(int i, int j) => i >= 0 && j >= 0 && i < Nx && j < Nz;
    public bool InDomain(int i, int j) => InBounds(i, j) && _inDomain[j * Nx + i];
    public bool InDomain(int idx) => idx >= 0 && idx < Count && _inDomain[idx];
    public int Index(int i, int j) => j * Nx + i;

    /// <summary>Cell containing p, or -1 when outside the grid.</summary>
    public int CellAt(Vec2 p)
    {
        int i = (int)Math.Floor((p.X - OriginX) / CellSize);
        int j = (int)Math.Floor((p.Z - OriginZ) / CellSize);
        return InBounds(i, j) ? j * Nx + i : -1;
    }

    /// <summary>Nearest in-domain cell (used for positions on the exact boundary).</summary>
    public int NearestDomainCell(Vec2 p)
    {
        int c = CellAt(p);
        if (c >= 0 && _inDomain[c]) return c;
        return CellAt(Domain.ClampInside(p, CellSize * 0.75));
    }

    /// <summary>Enumerates in-domain cells whose centre is within radius of p (ascending index order).</summary>
    public IEnumerable<int> CellsInRadius(Vec2 p, double radius)
    {
        int i0 = Math.Max(0, (int)Math.Floor((p.X - radius - OriginX) / CellSize));
        int i1 = Math.Min(Nx - 1, (int)Math.Floor((p.X + radius - OriginX) / CellSize));
        int j0 = Math.Max(0, (int)Math.Floor((p.Z - radius - OriginZ) / CellSize));
        int j1 = Math.Min(Nz - 1, (int)Math.Floor((p.Z + radius - OriginZ) / CellSize));
        double r2 = radius * radius;
        for (int j = j0; j <= j1; j++)
            for (int i = i0; i <= i1; i++)
            {
                int idx = j * Nx + i;
                if (_inDomain[idx] && Vec2.DistanceSq(CellCenter(i, j), p) <= r2) yield return idx;
            }
    }
}

/// <summary>Continuous scalar quantity per cell with bilinear interpolation and bounded writes.</summary>
public sealed class ScalarField
{
    public string Name { get; }
    public GridSpec Grid { get; }
    public double Min { get; }
    public double Max { get; }
    public double[] Values { get; }

    public ScalarField(string name, GridSpec grid, double initial = 0, double min = double.NegativeInfinity, double max = double.PositiveInfinity)
    {
        Name = name; Grid = grid; Min = min; Max = max;
        Values = new double[grid.Count];
        Fill(initial);
    }

    public void Fill(double v) { foreach (int idx in Grid.DomainCells) Values[idx] = Clamp(v); }

    private double Clamp(double v) => double.IsFinite(v) ? MathD.Clamp(v, Min, Max) : Math.Max(Min, Math.Min(Max, 0));

    public double this[int idx]
    {
        get => Values[idx];
        set { if (Grid.InDomain(idx)) Values[idx] = Clamp(value); }
    }

    /// <summary>Adds amount (may be negative) and returns the amount actually applied after bounds.</summary>
    public double Add(int idx, double amount)
    {
        if (!Grid.InDomain(idx)) return 0;
        double before = Values[idx];
        Values[idx] = Clamp(before + amount);
        return Values[idx] - before;
    }

    /// <summary>Removes up to amount (≥0) and returns the amount removed.</summary>
    public double Take(int idx, double amount) => amount <= 0 ? 0 : -Add(idx, -amount);

    public double Get(Vec2 p) { int c = Grid.NearestDomainCell(p); return c >= 0 ? Values[c] : 0; }

    /// <summary>Bilinear interpolation between in-domain cell centres (out-of-domain neighbours are ignored).</summary>
    public double Sample(Vec2 p)
    {
        double fx = (p.X - Grid.OriginX) / Grid.CellSize - 0.5, fz = (p.Z - Grid.OriginZ) / Grid.CellSize - 0.5;
        int i0 = (int)Math.Floor(fx), j0 = (int)Math.Floor(fz);
        double tx = fx - i0, tz = fz - j0;
        double sum = 0, wsum = 0;
        for (int dj = 0; dj <= 1; dj++)
            for (int di = 0; di <= 1; di++)
            {
                int i = i0 + di, j = j0 + dj;
                if (!Grid.InDomain(i, j)) continue;
                double w = (di == 0 ? 1 - tx : tx) * (dj == 0 ? 1 - tz : tz);
                sum += w * Values[j * Grid.Nx + i]; wsum += w;
            }
        return wsum > 1e-12 ? sum / wsum : Get(p);
    }

    /// <summary>Explicit diffusion step over in-domain cells (conservative; no flux across the boundary).</summary>
    public void Diffuse(double rate, double[] scratch)
    {
        rate = MathD.Clamp(rate, 0, 0.24);
        if (rate <= 0) return;
        Array.Copy(Values, scratch, Values.Length);
        int nx = Grid.Nx;
        foreach (int idx in Grid.DomainCells)
        {
            int i = idx % nx, j = idx / nx;
            double v = scratch[idx], acc = 0;
            if (Grid.InDomain(i - 1, j)) acc += scratch[idx - 1] - v;
            if (Grid.InDomain(i + 1, j)) acc += scratch[idx + 1] - v;
            if (Grid.InDomain(i, j - 1)) acc += scratch[idx - nx] - v;
            if (Grid.InDomain(i, j + 1)) acc += scratch[idx + nx] - v;
            Values[idx] = Clamp(v + rate * acc);
        }
    }

    public double Total() { double s = 0; foreach (int idx in Grid.DomainCells) s += Values[idx]; return s; }
    public double Mean() => Grid.DomainCells.Length == 0 ? 0 : Total() / Grid.DomainCells.Length;

    public bool AllFinite() { foreach (int idx in Grid.DomainCells) if (!double.IsFinite(Values[idx])) return false; return true; }

    /// <summary>Serialization hook: dense in-domain values in DomainCells order.</summary>
    public double[] ExportDomainValues() { var a = new double[Grid.DomainCells.Length]; for (int k = 0; k < a.Length; k++) a[k] = Values[Grid.DomainCells[k]]; return a; }

    public void ImportDomainValues(double[] a)
    {
        if (a.Length != Grid.DomainCells.Length) throw new InvalidDataException($"Field '{Name}' expects {Grid.DomainCells.Length} values, got {a.Length}.");
        for (int k = 0; k < a.Length; k++)
        {
            if (!double.IsFinite(a[k])) throw new InvalidDataException($"Field '{Name}' value {k} is not finite.");
            Values[Grid.DomainCells[k]] = Clamp(a[k]);
        }
    }

    public string DigestHex() { using var d = new DigestBuilder(); d.Add(Name); foreach (int idx in Grid.DomainCells) d.Add(Values[idx]); return d.Hex(); }
}

/// <summary>Exact-category storage per cell (no interpolation). Out-of-domain queries return the fallback category.</summary>
public sealed class CategoricalField
{
    public string Name { get; }
    public GridSpec Grid { get; }
    public byte[] Values { get; }
    public byte Fallback { get; }

    public CategoricalField(string name, GridSpec grid, byte initial = 0, byte fallback = 0)
    {
        Name = name; Grid = grid; Fallback = fallback;
        Values = new byte[grid.Count];
        foreach (int idx in grid.DomainCells) Values[idx] = initial;
    }

    public byte Get(Vec2 p)
    {
        if (!Grid.Domain.Contains(p)) return Fallback;
        int c = Grid.NearestDomainCell(p);
        return c >= 0 ? Values[c] : Fallback;
    }

    public byte this[int idx]
    {
        get => Grid.InDomain(idx) ? Values[idx] : Fallback;
        set { if (Grid.InDomain(idx)) Values[idx] = value; }
    }

    public byte[] ExportDomainValues() { var a = new byte[Grid.DomainCells.Length]; for (int k = 0; k < a.Length; k++) a[k] = Values[Grid.DomainCells[k]]; return a; }
    public void ImportDomainValues(byte[] a)
    {
        if (a.Length != Grid.DomainCells.Length) throw new InvalidDataException($"Field '{Name}' expects {Grid.DomainCells.Length} values, got {a.Length}.");
        for (int k = 0; k < a.Length; k++) Values[Grid.DomainCells[k]] = a[k];
    }
}
