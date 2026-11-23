using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Core.Rules;

namespace LegacyMigrationScanner.Core.Estimation;

public sealed record EstimationOptions
{
    /// <summary>Hours per effort point (default: 1 point ≈ 2 h). Calibrate against a migration you have already done.</summary>
    public double HoursPerPoint { get; init; } = 2;
}

/// <summary>
/// Transparent heuristic model:
/// <code>
/// points(rule) = basePoints × (1 + log2(count))   for mechanical fixes (Scaling = Log)
///              = basePoints × count                for per-unit work (Scaling = Linear)
/// P50          = Σ points × sizeFactor × couplingFactor
/// P90          = P50 × (1.3 + 0.7 × criticalShare)      // more blockers -> wider range
/// </code>
/// </summary>
public static class EffortEstimator
{
    public static IReadOnlyList<ProjectEstimate> Estimate(
        SolutionInfo solution,
        IReadOnlyList<Finding> findings,
        RuleCatalog rules,
        EstimationOptions options)
    {
        var dependents = CountDependents(solution);
        var byProject = findings.GroupBy(f => f.Project).ToDictionary(g => g.Key, g => g.ToList());

        return solution.Projects.Select(project =>
        {
            var projectFindings = byProject.GetValueOrDefault(project.Name) ?? [];

            var breakdown = projectFindings
                .GroupBy(f => f.RuleId)
                .Select(g =>
                {
                    var rule = rules.Get(g.Key);
                    // Each separate finding (e.g. each file) is a unit of work; occurrences inside it scale per rule.
                    var points = g.Sum(f => RuleCatalog.Points(rule, f.Count));
                    return new EffortBreakdownItem(g.Key, g.Sum(f => f.Count), Math.Round(points, 1));
                })
                .OrderByDescending(b => b.Points)
                .ToList();

            var raw = breakdown.Sum(b => b.Points);
            var criticalPoints = breakdown
                .Where(b => projectFindings.Any(f => f.RuleId == b.RuleId && f.Severity == Severity.Critical))
                .Sum(b => b.Points);
            var criticalShare = raw > 0 ? criticalPoints / raw : 0;

            var sizeFactor = 1 + 0.15 * Math.Log10(Math.Max(project.LinesOfCode, 1000) / 1000.0);
            var couplingFactor = Math.Min(1.5, 1 + 0.05 * dependents.GetValueOrDefault(project.FullPath));

            var p50 = raw * sizeFactor * couplingFactor;
            var p90 = p50 * (1.3 + 0.7 * criticalShare);

            return new ProjectEstimate
            {
                Project = project.Name,
                Kind = project.Kind,
                LinesOfCode = project.LinesOfCode,
                PointsP50 = Math.Round(p50, 1),
                PointsP90 = Math.Round(p90, 1),
                HoursP50 = Math.Round(p50 * options.HoursPerPoint, 0),
                HoursP90 = Math.Round(p90 * options.HoursPerPoint, 0),
                Readiness = projectFindings.Any(f => f.Severity == Severity.Critical) ? "Red"
                    : projectFindings.Any(f => f.Severity == Severity.Warning) ? "Yellow"
                    : "Green",
                Breakdown = breakdown,
            };
        }).ToList();
    }

    private static Dictionary<string, int> CountDependents(SolutionInfo solution)
    {
        var result = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var reference in solution.Projects.SelectMany(p => p.ProjectReferences))
        {
            result[reference] = result.GetValueOrDefault(reference) + 1;
        }

        return result;
    }
}
