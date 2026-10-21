import sys

with open('src/Vivarium.Sim/Content/ContentLoader.cs', 'r') as f:
    content = f.read()

search = """public sealed class DirectoryContentSource : IContentSource
{
    public string Root { get; }
    public DirectoryContentSource(string root) { Root = Path.GetFullPath(root); }
    public string Describe => Root;
    public bool Exists(string p) => File.Exists(Path.Combine(Root, p));
    public string ReadText(string p) => File.ReadAllText(Path.Combine(Root, p));
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
        var full = Path.GetFullPath(Path.Combine(Root, p));
        if (!full.StartsWith(_rootWithSep, Path.DirectorySeparatorChar == '\\\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException($"Path traversal attempt detected: {p}");
        }
        return full;
    }

    public bool Exists(string p)
    {
        try { return File.Exists(GetSafePath(p)); }
        catch (UnauthorizedAccessException) { return false; }
    }

    public string ReadText(string p) => File.ReadAllText(GetSafePath(p));
}"""

new_content = content.replace(search, replace)

if new_content == content:
    print("Search string not found!")
    sys.exit(1)

with open('src/Vivarium.Sim/Content/ContentLoader.cs', 'w') as f:
    f.write(new_content)

print("Replaced successfully!")
