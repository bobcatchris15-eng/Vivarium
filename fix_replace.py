import sys

with open('src/Vivarium.Sim/Content/ContentLoader.cs', 'r') as f:
    content = f.read()

search = """public sealed class DirectoryContentSource : IContentSource
{
    public string Root { get; }
    private readonly string _rootWithSep;

    public DirectoryContentSource(string root)
    {
        Root = Path.GetFullPath(root).Replace('\\\\', '/');
        _rootWithSep = Root.EndsWith("/") ? Root : Root + "/";
    }

    public string Describe => Root;

    private string GetSafePath(string p)
    {
        var combined = Path.Combine(Root, p);
        var full = Path.GetFullPath(combined).Replace('\\\\', '/');
        if (!full.StartsWith(_rootWithSep, StringComparison.OrdinalIgnoreCase) && full != Root)
        {
            throw new UnauthorizedAccessException($"Path traversal attempt detected: {p}");
        }
        return Path.GetFullPath(combined);
    }

    public bool Exists(string p)
    {
        try { return File.Exists(GetSafePath(p)); }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
    }

    public string ReadText(string p) => File.ReadAllText(GetSafePath(p));
}"""

replace = """public sealed class DirectoryContentSource : IContentSource
{
    public string Root { get; }
    private readonly string _rootWithSep;

    public DirectoryContentSource(string root)
    {
        Root = Path.GetFullPath(root);
        _rootWithSep = Root.EndsWith(Path.DirectorySeparatorChar.ToString()) ? Root : Root + Path.DirectorySeparatorChar;
    }

    public string Describe => Root;

    private string GetSafePath(string p)
    {
        var combined = Path.Combine(Root, p);
        var full = Path.GetFullPath(combined);
        if (!full.StartsWith(_rootWithSep, Path.DirectorySeparatorChar == '\\\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal) && full != Root)
        {
            throw new UnauthorizedAccessException($"Path traversal attempt detected: {p}");
        }
        return full;
    }

    public bool Exists(string p)
    {
        try { return File.Exists(GetSafePath(p)); }
        catch (UnauthorizedAccessException) { return false; }
        catch (ArgumentException) { return false; }
    }

    public string ReadText(string p) => File.ReadAllText(GetSafePath(p));
}"""

if search in content:
    new_content = content.replace(search, replace)
    with open('src/Vivarium.Sim/Content/ContentLoader.cs', 'w') as f:
        f.write(new_content)
    print("Replaced successfully!")
else:
    print("Search string not found!")
