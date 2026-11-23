using LegacyMigrationScanner.Core.Model;

namespace LegacyMigrationScanner.Core.Loading;

public static class SolutionLoader
{
    /// <summary>
    /// Loads a solution. <paramref name="path"/> may be a .sln/.slnx file, a single .csproj,
    /// or a directory containing exactly one solution.
    /// </summary>
    public static SolutionInfo Load(string path)
    {
        path = Path.GetFullPath(path);
        var solutionPath = ResolveSolutionPath(path);
        var errors = new List<string>();
        var projects = new List<ProjectInfo>();

        IReadOnlyList<string> projectPaths = solutionPath.EndsWith("proj", StringComparison.OrdinalIgnoreCase)
            ? [solutionPath]
            : SolutionParser.GetProjectPaths(solutionPath);

        foreach (var projectPath in projectPaths)
        {
            if (!projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Skipped unsupported project type: {Path.GetFileName(projectPath)}");
                continue;
            }

            if (!File.Exists(projectPath))
            {
                errors.Add($"Project file not found: {projectPath}");
                continue;
            }

            try
            {
                projects.Add(ProjectParser.Parse(projectPath));
            }
            catch (Exception ex) when (ex is System.Xml.XmlException or IOException or InvalidDataException)
            {
                errors.Add($"Failed to parse {Path.GetFileName(projectPath)}: {ex.Message}");
            }
        }

        return new SolutionInfo { FullPath = solutionPath, Projects = projects, LoadErrors = errors };
    }

    private static string ResolveSolutionPath(string path)
    {
        if (File.Exists(path))
        {
            return path;
        }

        if (!Directory.Exists(path))
        {
            throw new FileNotFoundException($"Path not found: {path}");
        }

        var candidates = Directory.EnumerateFiles(path, "*.sln")
            .Concat(Directory.EnumerateFiles(path, "*.slnx"))
            .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return candidates.Count switch
        {
            1 => candidates[0],
            0 => throw new FileNotFoundException($"No .sln or .slnx file found in {path}"),
            _ => throw new InvalidOperationException(
                $"Multiple solutions found in {path}; pass one explicitly: {string.Join(", ", candidates.Select(Path.GetFileName))}"),
        };
    }
}
