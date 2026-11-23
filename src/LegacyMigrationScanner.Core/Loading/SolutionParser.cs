using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace LegacyMigrationScanner.Core.Loading;

/// <summary>Extracts project paths from .sln (text) and .slnx (XML) files.</summary>
public static partial class SolutionParser
{
    // Project("{TYPE-GUID}") = "Name", "Relative\Path.csproj", "{PROJECT-GUID}"
    [GeneratedRegex("""^Project\("\{[^}]+\}"\)\s*=\s*"(?<name>[^"]*)",\s*"(?<path>[^"]*)",""", RegexOptions.Multiline)]
    private static partial Regex SlnProjectLine();

    /// <summary>Returns absolute paths of all project files (any extension) referenced by the solution.</summary>
    public static IReadOnlyList<string> GetProjectPaths(string solutionPath)
    {
        var solutionDir = Path.GetDirectoryName(Path.GetFullPath(solutionPath))!;
        var relativePaths = Path.GetExtension(solutionPath).Equals(".slnx", StringComparison.OrdinalIgnoreCase)
            ? ParseSlnx(solutionPath)
            : ParseSln(File.ReadAllText(solutionPath));

        return relativePaths
            .Where(p => Path.HasExtension(p) && Path.GetExtension(p).EndsWith("proj", StringComparison.OrdinalIgnoreCase))
            .Select(p => FileSystemHelpers.Combine(solutionDir, p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal static IEnumerable<string> ParseSln(string content) =>
        SlnProjectLine().Matches(content).Select(m => m.Groups["path"].Value);

    private static IEnumerable<string> ParseSlnx(string path) =>
        XDocument.Load(path)
            .Descendants()
            .Where(e => e.Name.LocalName == "Project")
            .Select(e => (string?)e.Attribute("Path"))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => p!);
}
