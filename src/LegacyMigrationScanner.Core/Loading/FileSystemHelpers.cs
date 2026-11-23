namespace LegacyMigrationScanner.Core.Loading;

internal static class FileSystemHelpers
{
    private static readonly HashSet<string> ExcludedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        "bin", "obj", "packages", "node_modules", ".vs", ".git", "TestResults",
    };

    /// <summary>Converts a path as written in .sln/.csproj (backslashes) to the current OS form.</summary>
    public static string NormalizeSeparators(string path) =>
        path.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);

    public static string Combine(string baseDirectory, string relativePath) =>
        Path.GetFullPath(Path.Combine(baseDirectory, NormalizeSeparators(relativePath)));

    /// <summary>Recursively enumerates files, skipping build output and package folders.</summary>
    public static IEnumerable<string> EnumerateFiles(string root, Func<string, bool> predicate)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var dir = pending.Pop();
            IEnumerable<string> files, dirs;
            try
            {
                files = Directory.EnumerateFiles(dir);
                dirs = Directory.EnumerateDirectories(dir);
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                continue;
            }

            foreach (var file in files)
            {
                if (predicate(file))
                {
                    yield return file;
                }
            }

            foreach (var sub in dirs)
            {
                if (!ExcludedDirectories.Contains(Path.GetFileName(sub)))
                {
                    pending.Push(sub);
                }
            }
        }
    }

    public static string Relative(string baseDirectory, string fullPath) =>
        Path.GetRelativePath(baseDirectory, fullPath).Replace('\\', '/');
}
