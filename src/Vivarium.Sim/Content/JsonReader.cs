using System.Text.Json;

namespace Vivarium.Sim.Content;

/// <summary>One actionable validation problem: which file, which field (JSON path), what is wrong.</summary>
public readonly record struct ContentError(string File, string Path, string Message)
{
    public override string ToString() => $"{File}: {Path}: {Message}";
}

public sealed class ContentErrors
{
    private readonly List<ContentError> _errors = new();
    public IReadOnlyList<ContentError> All => _errors;
    public bool Any => _errors.Count > 0;
    public void Add(string file, string path, string message) => _errors.Add(new ContentError(file, path, message));
    public override string ToString() => string.Join(Environment.NewLine, _errors);
}

public sealed class ContentValidationException : Exception
{
    public IReadOnlyList<ContentError> Errors { get; }
    public ContentValidationException(IReadOnlyList<ContentError> errors)
        : base($"Content validation failed with {errors.Count} error(s):{Environment.NewLine}{string.Join(Environment.NewLine, errors.Take(25))}")
    { Errors = errors; }
}

/// <summary>
/// Path-tracking JSON reader. Every accessor records a precise error (file + $.path) instead of throwing,
/// so one pass reports every problem in a definition.
/// </summary>
public readonly struct JNode
{
    public readonly JsonElement El;
    public readonly string File;
    public readonly string Path;
    public readonly ContentErrors Errors;
    public readonly bool Exists;

    public JNode(JsonElement el, string file, string path, ContentErrors errors, bool exists = true)
    { El = el; File = file; Path = path; Errors = errors; Exists = exists; }

    public void Error(string message) => Errors.Add(File, Path, message);
    private void ErrorAt(string sub, string message) => Errors.Add(File, $"{Path}.{sub}", message);

    public bool Has(string name) => Exists && El.ValueKind == JsonValueKind.Object && El.TryGetProperty(name, out _);

    public JNode this[string name]
    {
        get
        {
            if (Exists && El.ValueKind == JsonValueKind.Object && El.TryGetProperty(name, out var v))
                return new JNode(v, File, $"{Path}.{name}", Errors);
            return new JNode(default, File, $"{Path}.{name}", Errors, exists: false);
        }
    }

    public JNode Req(string name)
    {
        var n = this[name];
        if (!n.Exists) ErrorAt(name, "required field is missing");
        return n;
    }

    public string Str(string name, string? fallback = null, bool required = true)
    {
        var n = this[name];
        if (!n.Exists) { if (required && fallback == null) ErrorAt(name, "required string is missing"); return fallback ?? string.Empty; }
        if (n.El.ValueKind != JsonValueKind.String) { n.Error($"expected string, found {n.El.ValueKind}"); return fallback ?? string.Empty; }
        var s = n.El.GetString() ?? string.Empty;
        if (required && s.Length == 0) n.Error("must not be empty");
        return s;
    }

    public double Num(string name, double? fallback = null, double min = double.NegativeInfinity, double max = double.PositiveInfinity)
    {
        var n = this[name];
        if (!n.Exists)
        {
            if (fallback == null) { ErrorAt(name, "required number is missing"); return 0; }
            return fallback.Value;
        }
        if (n.El.ValueKind != JsonValueKind.Number) { n.Error($"expected number, found {n.El.ValueKind}"); return fallback ?? 0; }
        double v = n.El.GetDouble();
        if (!double.IsFinite(v)) { n.Error("must be finite"); return fallback ?? 0; }
        if (v < min || v > max) n.Error($"value {v} out of range [{min}, {max}]");
        return v;
    }

    public int Int(string name, int? fallback = null, int min = int.MinValue, int max = int.MaxValue)
    {
        double d = Num(name, fallback, min, max);
        var n = this[name];
        if (n.Exists && n.El.ValueKind == JsonValueKind.Number && Math.Abs(d - Math.Round(d)) > 1e-9) n.Error("expected integer");
        return (int)Math.Round(d);
    }

    public bool Bool(string name, bool fallback)
    {
        var n = this[name];
        if (!n.Exists) return fallback;
        if (n.El.ValueKind is JsonValueKind.True or JsonValueKind.False) return n.El.GetBoolean();
        n.Error($"expected boolean, found {n.El.ValueKind}");
        return fallback;
    }

    public IEnumerable<JNode> Items(string name, bool required = true)
    {
        var n = this[name];
        if (!n.Exists) { if (required) ErrorAt(name, "required array is missing"); yield break; }
        if (n.El.ValueKind != JsonValueKind.Array) { n.Error($"expected array, found {n.El.ValueKind}"); yield break; }
        int i = 0;
        foreach (var item in n.El.EnumerateArray())
            yield return new JNode(item, File, $"{n.Path}[{i++}]", Errors);
    }

    public List<string> StrList(string name, bool required = false)
    {
        var list = new List<string>();
        foreach (var item in Items(name, required))
        {
            if (item.El.ValueKind == JsonValueKind.String) list.Add(item.El.GetString() ?? "");
            else item.Error("expected string");
        }
        return list;
    }

    public double[] Color(string name, double[]? fallback = null)
    {
        var n = this[name];
        if (!n.Exists) { if (fallback == null) ErrorAt(name, "required color [r,g,b] is missing"); return fallback ?? new double[] { 1, 0, 1 }; }
        if (n.El.ValueKind != JsonValueKind.Array || n.El.GetArrayLength() is < 3 or > 4) { n.Error("expected color array [r,g,b] with components 0..1"); return fallback ?? new double[] { 1, 0, 1 }; }
        var c = new double[3];
        int i = 0;
        foreach (var e in n.El.EnumerateArray())
        {
            if (i >= 3) break;
            if (e.ValueKind != JsonValueKind.Number) { n.Error($"component {i} is not a number"); return new double[] { 1, 0, 1 }; }
            c[i] = e.GetDouble();
            if (c[i] < 0 || c[i] > 1) n.Error($"component {i} value {c[i]} out of range [0,1]");
            i++;
        }
        return c;
    }

    /// <summary>Object map of id → number, e.g. substrate affinities.</summary>
    public Dictionary<string, double> NumMap(string name, double min = double.NegativeInfinity, double max = double.PositiveInfinity, bool required = false)
    {
        var map = new Dictionary<string, double>(StringComparer.Ordinal);
        var n = this[name];
        if (!n.Exists) { if (required) ErrorAt(name, "required object is missing"); return map; }
        if (n.El.ValueKind != JsonValueKind.Object) { n.Error("expected object"); return map; }
        foreach (var p in n.El.EnumerateObject())
        {
            var child = new JNode(p.Value, File, $"{n.Path}.{p.Name}", Errors);
            if (p.Value.ValueKind != JsonValueKind.Number) { child.Error("expected number"); continue; }
            double v = p.Value.GetDouble();
            if (v < min || v > max) child.Error($"value {v} out of range [{min}, {max}]");
            map[p.Name] = v;
        }
        return map;
    }

    public void RejectUnknown(params string[] known)
    {
        if (!Exists || El.ValueKind != JsonValueKind.Object) return;
        foreach (var p in El.EnumerateObject())
            if (Array.IndexOf(known, p.Name) < 0 && !p.Name.StartsWith("_", StringComparison.Ordinal))
                Errors.Add(File, $"{Path}.{p.Name}", $"unknown field (expected one of: {string.Join(", ", known)})");
    }
}
