using System;
using System.IO;

public class Test
{
    public static void Main()
    {
        string root = Path.GetFullPath("testdir");
        Directory.CreateDirectory(root);
        string rootWithSep = root.EndsWith(Path.DirectorySeparatorChar.ToString()) ? root : root + Path.DirectorySeparatorChar;

        Console.WriteLine($"Root: {root}");
        Console.WriteLine($"RootWithSep: {rootWithSep}");

        string p1 = "file.txt";
        string p2 = "../secret.txt";

        Console.WriteLine($"p1 is safe? {IsSafe(root, rootWithSep, p1)}");
        Console.WriteLine($"p2 is safe? {IsSafe(root, rootWithSep, p2)}");
    }

    static bool IsSafe(string root, string rootWithSep, string p)
    {
        string fullPath = Path.GetFullPath(Path.Combine(root, p));
        Console.WriteLine($"  resolved: {fullPath}");
        return fullPath.StartsWith(rootWithSep, Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
}
