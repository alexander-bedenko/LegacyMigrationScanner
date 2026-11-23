using LegacyMigrationScanner.Core.Model;

namespace LegacyMigrationScanner.Core.Analyzers;

/// <summary>Project-level findings: csproj format, package management, target framework, project type.</summary>
public sealed class ProjectStructureAnalyzer : IProjectAnalyzer
{
    public IEnumerable<Finding> Analyze(ProjectInfo project, AnalysisContext context)
    {
        var rules = context.Rules;
        var csproj = context.Relative(project.FullPath);

        if (!project.IsSdkStyle)
        {
            yield return rules.Create("LMS001", project.Name, csproj);
        }

        var packagesConfig = project.Packages.Where(p => p.Source == PackageSource.PackagesConfig).ToList();
        if (packagesConfig.Count > 0)
        {
            yield return rules.Create("LMS002", project.Name, context.Relative(Path.Combine(project.Directory, "packages.config")),
                count: 1, detail: $"{packagesConfig.Count} packages");
        }

        if (project.TargetsNetFramework)
        {
            yield return rules.Create("LMS003", project.Name, csproj, detail: string.Join(", ", project.TargetFrameworks));
        }

        switch (project.Kind)
        {
            case ProjectKind.AspNetWebApplication:
                yield return rules.Create("LMS004", project.Name, csproj);
                break;
            case ProjectKind.WcfService:
                yield return rules.Create("LMS005", project.Name, csproj);
                break;
        }
    }
}
