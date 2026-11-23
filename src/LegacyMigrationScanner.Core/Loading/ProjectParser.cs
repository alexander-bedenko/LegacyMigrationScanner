using System.Xml.Linq;
using LegacyMigrationScanner.Core.Model;

namespace LegacyMigrationScanner.Core.Loading;

/// <summary>
/// Reads a .csproj as plain XML (no MSBuild evaluation). Works for both legacy
/// (ToolsVersion) and SDK-style projects and does not require Visual Studio.
/// </summary>
public static class ProjectParser
{
    private static readonly HashSet<string> InterestingExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".aspx", ".ascx", ".master", ".asax", ".asmx", ".ashx", ".svc", ".edmx", ".tt", ".cshtml",
    };

    public static ProjectInfo Parse(string projectPath)
    {
        projectPath = Path.GetFullPath(projectPath);
        var projectDir = Path.GetDirectoryName(projectPath)!;
        var doc = XDocument.Load(projectPath);
        var root = doc.Root ?? throw new InvalidDataException($"Empty project file: {projectPath}");

        var isSdkStyle = root.Attribute("Sdk") is not null
                         || root.Elements().Any(e => e.Name.LocalName == "Sdk");

        var targetFrameworks = ReadTargetFrameworks(root);
        var typeGuids = Property(root, "ProjectTypeGuids")?
            .Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(g => g.Trim('{', '}').ToLowerInvariant())
            .ToList() ?? [];

        var assemblyRefs = Items(root, "Reference")
            .Select(e => ((string?)e.Attribute("Include") ?? "").Split(',')[0].Trim())
            .Where(n => n.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var projectRefs = Items(root, "ProjectReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => FileSystemHelpers.Combine(projectDir, p!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var packages = new List<PackageInfo>();
        packages.AddRange(Items(root, "PackageReference").Select(e => new PackageInfo(
            (string?)e.Attribute("Include") ?? (string?)e.Attribute("Update") ?? "",
            (string?)e.Attribute("Version") ?? e.Elements().FirstOrDefault(c => c.Name.LocalName == "Version")?.Value ?? "",
            PackageSource.PackageReference)).Where(p => p.Id.Length > 0));
        packages.AddRange(ReadPackagesConfig(projectDir));

        var sourceFiles = isSdkStyle
            ? FileSystemHelpers.EnumerateFiles(projectDir, f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)).ToList()
            : ReadCompileItems(root, projectDir);

        var configFiles = Directory.EnumerateFiles(projectDir)
            .Where(f => Path.GetFileName(f).Equals("web.config", StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileName(f).Equals("app.config", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var otherFiles = FileSystemHelpers
            .EnumerateFiles(projectDir, f => InterestingExtensions.Contains(Path.GetExtension(f)))
            .ToList();

        var info = new ProjectInfo
        {
            Name = Path.GetFileNameWithoutExtension(projectPath),
            FullPath = projectPath,
            IsSdkStyle = isSdkStyle,
            TargetFrameworks = targetFrameworks,
            ProjectTypeGuids = typeGuids,
            OutputType = Property(root, "OutputType"),
            AssemblyReferences = assemblyRefs,
            ProjectReferences = projectRefs,
            Packages = packages,
            SourceFiles = sourceFiles,
            ConfigFiles = configFiles,
            OtherFiles = otherFiles,
        };
        info.Kind = ProjectClassifier.Classify(info);
        info.LinesOfCode = CountLines(sourceFiles);
        return info;
    }

    private static List<string> ReadTargetFrameworks(XElement root)
    {
        var result = new List<string>();
        if (Property(root, "TargetFrameworkVersion") is { } legacy)
        {
            result.Add(legacy);
        }

        if (Property(root, "TargetFramework") is { } single)
        {
            result.Add(single);
        }

        if (Property(root, "TargetFrameworks") is { } multi)
        {
            result.AddRange(multi.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }

        return result.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<string> ReadCompileItems(XElement root, string projectDir)
    {
        var files = new List<string>();
        foreach (var include in Items(root, "Compile").Select(e => (string?)e.Attribute("Include")))
        {
            if (string.IsNullOrWhiteSpace(include) || !include.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (include.Contains('*'))
            {
                // Rare in legacy projects; fall back to a directory glob.
                files.AddRange(FileSystemHelpers.EnumerateFiles(projectDir, f => f.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)));
                continue;
            }

            var full = FileSystemHelpers.Combine(projectDir, include);
            if (File.Exists(full))
            {
                files.Add(full);
            }
        }

        return files.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static List<PackageInfo> ReadPackagesConfig(string projectDir)
    {
        var path = Path.Combine(projectDir, "packages.config");
        if (!File.Exists(path))
        {
            return [];
        }

        return XDocument.Load(path)
            .Descendants()
            .Where(e => e.Name.LocalName == "package")
            .Select(e => new PackageInfo((string?)e.Attribute("id") ?? "", (string?)e.Attribute("version") ?? "", PackageSource.PackagesConfig))
            .Where(p => p.Id.Length > 0)
            .ToList();
    }

    private static int CountLines(IEnumerable<string> files)
    {
        var total = 0;
        foreach (var file in files)
        {
            total += File.ReadLines(file).Count(l => !string.IsNullOrWhiteSpace(l));
        }

        return total;
    }

    private static string? Property(XElement root, string name) =>
        root.Elements()
            .Where(e => e.Name.LocalName == "PropertyGroup")
            .SelectMany(g => g.Elements())
            .FirstOrDefault(e => e.Name.LocalName == name)?.Value.Trim() is { Length: > 0 } v ? v : null;

    private static IEnumerable<XElement> Items(XElement root, string itemType) =>
        root.Elements()
            .Where(e => e.Name.LocalName == "ItemGroup")
            .SelectMany(g => g.Elements())
            .Where(e => e.Name.LocalName == itemType);
}
