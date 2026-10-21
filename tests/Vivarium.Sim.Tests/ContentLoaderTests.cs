using System;
using System.IO;
using Xunit;
using Vivarium.Sim.Content;

namespace Vivarium.Sim.Tests;

public class ContentLoaderTests : IDisposable
{
    private readonly string _tempDir;

    public ContentLoaderTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
        File.WriteAllText(Path.Combine(_tempDir, "safe.txt"), "safe content");
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void DirectoryContentSource_AllowsValidPaths()
    {
        var source = new DirectoryContentSource(_tempDir);
        Assert.True(source.Exists("safe.txt"));
        Assert.Equal("safe content", source.ReadText("safe.txt"));
    }

    [Fact]
    public void DirectoryContentSource_BlocksPathTraversal()
    {
        var source = new DirectoryContentSource(_tempDir);
        var traversalPath = Path.Combine("..", "some_secret.txt");

        Assert.False(source.Exists(traversalPath));
        Assert.Throws<UnauthorizedAccessException>(() => source.ReadText(traversalPath));
    }
}
