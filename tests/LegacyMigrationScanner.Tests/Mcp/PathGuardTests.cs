using LegacyMigrationScanner.Mcp;
using LegacyMigrationScanner.Tests.Infrastructure;
using ModelContextProtocol;

namespace LegacyMigrationScanner.Tests.Mcp;

public sealed class PathGuardTests : IDisposable
{
    private readonly string _root = TestHelpers.CreateTempDirectory();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private PathGuard Guard()
    {
        var settings = new McpServerSettings();
        settings.AllowedRoots.Add(_root);
        return new PathGuard(settings);
    }

    [Fact]
    public void RelativePaths_ResolveAgainstFirstRoot()
    {
        Assert.Equal(Path.Combine(_root, "src", "App.sln"), Guard().Resolve(Path.Combine("src", "App.sln")));
    }

    [Fact]
    public void RootItself_AndNestedAbsolutePaths_AreAllowed()
    {
        var guard = Guard();

        Assert.Equal(_root, guard.Resolve(_root + Path.DirectorySeparatorChar));
        Assert.Equal(Path.Combine(_root, "a"), guard.Resolve(Path.Combine(_root, "a")));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../other/App.sln")]
    [InlineData("")]
    [InlineData("   ")]
    public void EscapingOrEmptyPaths_AreRejected(string path) =>
        Assert.Throws<McpException>(() => Guard().Resolve(path));

    [Fact]
    public void SiblingWithSamePrefix_IsRejected() =>
        Assert.Throws<McpException>(() => Guard().Resolve(_root + "-evil"));

    [Fact]
    public void DefaultRoot_IsWorkingDirectory() =>
        Assert.Equal(Path.TrimEndingDirectorySeparator(Environment.CurrentDirectory), Assert.Single(new PathGuard(new McpServerSettings()).Roots));
}
