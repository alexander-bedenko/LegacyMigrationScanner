using System.Collections.Concurrent;
using System.Diagnostics;
using System.Reflection;
using LegacyMigrationScanner.Core.Analyzers;
using LegacyMigrationScanner.Core.Diagnostics;
using LegacyMigrationScanner.Core.Estimation;
using LegacyMigrationScanner.Core.Loading;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Core.Packages;
using LegacyMigrationScanner.Core.Rules;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyMigrationScanner.Core;

public interface IMigrationScanner
{
    Task<ScanResult> ScanAsync(string path, CancellationToken cancellationToken = default);
}

/// <summary>
/// Pipeline: load solution -> resolve packages (catalog/cache/NuGet) -> run analyzers per project
/// (in parallel) -> estimate effort -> plan migration waves.
/// </summary>
public sealed class MigrationScanner : IMigrationScanner
{
    private readonly RuleCatalog _rules;
    private readonly IReadOnlyList<IProjectAnalyzer> _analyzers;
    private readonly IPackageCompatibilityService _packages;
    private readonly ScannerOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<MigrationScanner> _logger;

    public MigrationScanner(
        RuleCatalog rules,
        IEnumerable<IProjectAnalyzer> analyzers,
        IPackageCompatibilityService packages,
        IOptions<ScannerOptions> options,
        TimeProvider time,
        ILogger<MigrationScanner> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _rules = rules;
        _analyzers = [.. analyzers];
        _packages = packages;
        _options = options.Value;
        _time = time;
        _logger = logger;
    }

    public static string ToolVersion =>
        typeof(MigrationScanner).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    public async Task<ScanResult> ScanAsync(string path, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var stopwatch = Stopwatch.StartNew();
        Log.ScanStarted(_logger, path);

        var solution = SolutionLoader.Load(path);
        Log.SolutionLoaded(_logger, solution.Projects.Count, Path.GetFileName(solution.FullPath));
        foreach (var error in solution.LoadErrors)
        {
            Log.SolutionLoadWarning(_logger, error);
        }

        var packageIdentities = solution.Projects
            .SelectMany(p => p.Packages)
            .Select(p => new PackageIdentity(p.Id, p.Version))
            .DistinctBy(p => p.Key)
            .ToList();
        var packages = await _packages.ResolveAsync(packageIdentities, cancellationToken);

        var context = new AnalysisContext(solution.Directory, _rules, packages);
        var findings = new ConcurrentBag<Finding>();
        await Parallel.ForEachAsync(solution.Projects, cancellationToken, (project, ct) =>
        {
            var projectFindings = _analyzers.SelectMany(a => a.Analyze(project, context)).ToList();
            foreach (var finding in projectFindings)
            {
                findings.Add(finding);
            }

            Log.ProjectAnalyzed(_logger, project.Name, projectFindings.Count);
            return ValueTask.CompletedTask;
        });

        var ordered = findings
            .OrderBy(f => f.Project, StringComparer.OrdinalIgnoreCase)
            .ThenByDescending(f => f.Severity)
            .ThenBy(f => f.RuleId, StringComparer.Ordinal)
            .ThenBy(f => f.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Detail, StringComparer.Ordinal)
            .ToList();

        var estimates = EffortEstimator.Estimate(solution, ordered, _rules, new EstimationOptions { HoursPerPoint = _options.HoursPerPoint });
        var (waves, orderWarnings) = MigrationOrderPlanner.Plan(solution);

        Log.ScanCompleted(_logger, ordered.Count, stopwatch.ElapsedMilliseconds);

        return new ScanResult
        {
            ToolVersion = ToolVersion,
            ScannedAt = _time.GetLocalNow(),
            Solution = Path.GetFileName(solution.FullPath),
            SolutionDirectory = solution.Directory,
            Projects = [.. solution.Projects.Select(p => new ProjectSummary(
                p.Name,
                context.Relative(p.FullPath),
                p.Kind,
                p.IsSdkStyle,
                p.TargetFrameworks,
                p.LinesOfCode,
                p.Packages.Count,
                [.. p.ProjectReferences.Select(Path.GetFileNameWithoutExtension).OfType<string>()]))],
            Findings = ordered,
            Estimates = estimates,
            MigrationOrder = waves,
            HoursPerPoint = _options.HoursPerPoint,
            Warnings = [.. solution.LoadErrors, .. orderWarnings],
            Packages = [.. packages.Values.OrderBy(p => p.Id, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.Version, NuGetVersionComparer)],
            Rules = [.. _rules.All.OrderBy(r => r.Id, StringComparer.Ordinal)],
        };
    }

    private static readonly IComparer<string> NuGetVersionComparer = Packages.NuGet.NuGetVersion.Comparer;
}
