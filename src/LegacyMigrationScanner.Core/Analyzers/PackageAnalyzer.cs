using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Core.Packages;

namespace LegacyMigrationScanner.Core.Analyzers;

/// <summary>Turns package classifications (resolved before analysis) into findings.</summary>
public sealed class PackageAnalyzer : IProjectAnalyzer
{
    public IEnumerable<Finding> Analyze(ProjectInfo project, AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(context);

        var packages = project.Packages
            .DistinctBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase);

        foreach (var package in packages)
        {
            var identity = new PackageIdentity(package.Id, package.Version);
            var file = context.Relative(package.Source == PackageSource.PackagesConfig
                ? Path.Combine(project.Directory, "packages.config")
                : project.FullPath);

            var resolved = context.Packages.GetValueOrDefault(identity.Key);
            var detail = identity.ToString();

            var finding = resolved?.Status switch
            {
                null => context.Rules.Create("LMS603", project.Name, file, detail: detail),
                PackageStatus.Compatible => null,
                PackageStatus.UpgradeRequired => context.Rules.Create("LMS601", project.Name, file,
                    detail: resolved.RecommendedVersion is null ? detail : $"{detail} -> {resolved.RecommendedVersion}",
                    recommendationOverride: Recommend(resolved)),
                PackageStatus.Replace => context.Rules.Create("LMS600", project.Name, file, detail: detail,
                    recommendationOverride: Recommend(resolved)),
                PackageStatus.Remove => context.Rules.Create("LMS602", project.Name, file, detail: detail),
                PackageStatus.Review => context.Rules.Create("LMS605", project.Name, file, detail: detail,
                    recommendationOverride: Recommend(resolved)),
                PackageStatus.Incompatible => context.Rules.Create("LMS604", project.Name, file,
                    detail: resolved.Reason is null ? detail : $"{detail}: {resolved.Reason}",
                    recommendationOverride: Recommend(resolved)),
                PackageStatus.NotFound => context.Rules.Create("LMS606", project.Name, file, detail: detail),
                _ => context.Rules.Create("LMS603", project.Name, file,
                    detail: resolved.Reason is null ? detail : $"{detail}: {resolved.Reason}"),
            };

            if (finding is not null)
            {
                yield return finding;
            }
        }
    }

    private static string? Recommend(PackageCompatibility package) => package switch
    {
        { Replacement: not null } => $"Use {package.Replacement}.",
        { RecommendedVersion: not null, ModernFrameworks.Count: > 0 } =>
            $"Upgrade to {package.RecommendedVersion} (supports {string.Join(", ", package.ModernFrameworks)}).",
        { RecommendedVersion: not null } => $"Upgrade to {package.RecommendedVersion} or later.",
        _ => null,
    };
}
