using LegacyMigrationScanner.Core.Model;

namespace LegacyMigrationScanner.Core.Analyzers;

/// <summary>Findings based on the presence of framework-specific files (.aspx, .svc, .edmx ...).</summary>
public sealed class FileArtifactAnalyzer : IProjectAnalyzer
{
    private const int MaxListedFiles = 5;

    public IEnumerable<Finding> Analyze(ProjectInfo project, AnalysisContext context)
    {
        var byExtension = project.OtherFiles
            .GroupBy(f => Path.GetExtension(f).ToLowerInvariant())
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList());

        List<string> Files(params string[] extensions) =>
            extensions.SelectMany(e => byExtension.TryGetValue(e, out var list) ? list : []).ToList();

        // Aggregated per project: count matters, individual files are listed in Detail.
        foreach (var (ruleId, extensions) in new (string, string[])[]
                 {
                     ("LMS100", [".aspx", ".ascx", ".master"]),
                     ("LMS200", [".svc"]),
                     ("LMS115", [".asmx"]),
                     ("LMS103", [".ashx"]),
                     ("LMS401", [".tt"]),
                 })
        {
            var files = Files(extensions);
            if (files.Count > 0)
            {
                yield return context.Rules.Create(ruleId, project.Name, context.Relative(files[0]), count: files.Count,
                    detail: Describe(files, context));
            }
        }

        foreach (var asax in Files(".asax"))
        {
            yield return context.Rules.Create("LMS101", project.Name, context.Relative(asax));
        }

        // One finding per EDMX: every model is a separate chunk of work.
        foreach (var edmx in Files(".edmx"))
        {
            yield return context.Rules.Create("LMS400", project.Name, context.Relative(edmx));
        }
    }

    private static string Describe(List<string> files, AnalysisContext context)
    {
        var listed = files.Take(MaxListedFiles).Select(f => Path.GetFileName(f));
        var more = files.Count > MaxListedFiles ? $" (+{files.Count - MaxListedFiles} more)" : "";
        return $"{files.Count} file(s): {string.Join(", ", listed)}{more}";
    }
}
