using LegacyMigrationScanner.Core.Deep;
using LegacyMigrationScanner.Core.Loading;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Core.Packages;
using LegacyMigrationScanner.Core.Rules;

namespace LegacyMigrationScanner.Core.Analyzers;

/// <summary>
/// Produces findings for one project. Implementations must be thread-safe and side-effect free:
/// projects are analyzed in parallel.
/// </summary>
public interface IProjectAnalyzer
{
    IEnumerable<Finding> Analyze(ProjectInfo project, AnalysisContext context);
}

public sealed class AnalysisContext(
    string solutionDirectory,
    RuleCatalog rules,
    IReadOnlyDictionary<string, PackageCompatibility>? packages = null,
    DeepModel? deep = null)
{
    /// <summary>Compilations for deep mode (ADR 0005); null in the default syntax-only mode.</summary>
    public DeepModel? Deep { get; } = deep;

    public string SolutionDirectory { get; } = solutionDirectory;
    public RuleCatalog Rules { get; } = rules;

    /// <summary>Package classification keyed by <see cref="PackageIdentity.Key"/>.</summary>
    public IReadOnlyDictionary<string, PackageCompatibility> Packages { get; } =
        packages ?? new Dictionary<string, PackageCompatibility>(StringComparer.Ordinal);

    public string Relative(string fullPath) => FileSystemHelpers.Relative(SolutionDirectory, fullPath);
}
