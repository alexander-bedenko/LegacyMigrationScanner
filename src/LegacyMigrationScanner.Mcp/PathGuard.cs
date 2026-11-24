using ModelContextProtocol;

namespace LegacyMigrationScanner.Mcp;

/// <summary>Directories the MCP tools may read (ADR 0004). Defaults to the server's working directory.</summary>
public sealed class McpServerSettings
{
    public IList<string> AllowedRoots { get; } = [];
}

/// <summary>
/// Resolves tool path arguments and rejects anything outside the allowed roots, so a prompt-injected
/// request cannot make the server read arbitrary locations.
/// </summary>
public sealed class PathGuard
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly List<string> _roots;

    public PathGuard(McpServerSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var roots = settings.AllowedRoots.Count > 0 ? settings.AllowedRoots : [Environment.CurrentDirectory];
        _roots = [.. roots.Select(r => Path.TrimEndingDirectorySeparator(Path.GetFullPath(r)))];
    }

    public IReadOnlyList<string> Roots => _roots;

    public static StringComparer PathComparer { get; } =
        PathComparison == StringComparison.OrdinalIgnoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>Full path for <paramref name="path"/> (relative paths resolve against the first root).</summary>
    /// <exception cref="McpException">The path is empty or outside every allowed root.</exception>
    public string Resolve(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new McpException("A path to a .sln, .slnx, .csproj or a directory with one solution is required.");
        }

        string full;
        try
        {
            full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.IsPathRooted(path) ? path : Path.Combine(_roots[0], path)));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            throw new McpException($"Invalid path '{path}': {ex.Message}");
        }

        if (!_roots.Any(root => IsUnder(full, root)))
        {
            throw new McpException(
                $"'{path}' is outside the directories this server may read ({string.Join(", ", _roots)}). " +
                "Restart the server with --root <dir> to allow another location.");
        }

        return full;
    }

    private static bool IsUnder(string path, string root) =>
        path.Equals(root, PathComparison) ||
        (path.StartsWith(root, PathComparison) && path.Length > root.Length &&
         (path[root.Length] == Path.DirectorySeparatorChar || path[root.Length] == Path.AltDirectorySeparatorChar ||
          root.EndsWith(Path.DirectorySeparatorChar) || root.EndsWith(Path.AltDirectorySeparatorChar)));
}
