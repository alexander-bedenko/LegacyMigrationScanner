using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Core.Packages;
using LegacyMigrationScanner.Core.Rules;

namespace LegacyMigrationScanner.Mcp;

// Compact, model-friendly shapes: enough to reason about, small enough to keep tool results cheap in context.

public sealed record SeverityCounts(int Critical, int Warning, int Info)
{
    public static SeverityCounts Of(IEnumerable<Finding> findings)
    {
        var list = findings as IReadOnlyCollection<Finding> ?? [.. findings];
        return new(list.Count(f => f.Severity == Severity.Critical), list.Count(f => f.Severity == Severity.Warning), list.Count(f => f.Severity == Severity.Info));
    }
}

public sealed record ProjectOverview(
    string Name,
    ProjectKind Kind,
    string Readiness,
    bool IsSdkStyle,
    IReadOnlyList<string> TargetFrameworks,
    int LinesOfCode,
    double HoursP50,
    double HoursP90,
    SeverityCounts Findings,
    IReadOnlyList<string> TopCostDrivers);

public sealed record RuleOverview(string RuleId, string Title, Severity Severity, int Occurrences, int Projects);

public sealed record ScanSummary
{
    public required string Solution { get; init; }
    public required string ToolVersion { get; init; }
    public required DateTimeOffset ScannedAt { get; init; }
    public required double HoursPerPoint { get; init; }
    /// <summary>True when the server runs with --deep (semantic analysis + vendored DLLs).</summary>
    public bool Deep { get; init; }
    public required double TotalHoursP50 { get; init; }
    public required double TotalHoursP90 { get; init; }
    public required SeverityCounts Findings { get; init; }
    public required IReadOnlyList<ProjectOverview> Projects { get; init; }
    /// <summary>Projects in the same wave do not depend on each other.</summary>
    public required IReadOnlyList<IReadOnlyList<string>> MigrationWaves { get; init; }
    public required IReadOnlyList<RuleOverview> TopRules { get; init; }
    public required int PackagesNeedingAction { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

public sealed record FindingItem(
    string Fingerprint,
    string RuleId,
    string Title,
    Severity Severity,
    FindingCategory Category,
    string Project,
    string? File,
    int? Line,
    int Count,
    string? Detail)
{
    public static FindingItem From(Finding f) =>
        new(f.Fingerprint ?? "", f.RuleId, f.Title, f.Severity, f.Category, f.Project, f.File, f.Line, f.Count, f.Detail);
}

public sealed record FindingsPage(int Total, int Offset, IReadOnlyList<FindingItem> Findings)
{
    public int Returned => Findings.Count;
    public bool HasMore => Offset + Findings.Count < Total;
}

public sealed record RuleDetails(
    string Id,
    string Title,
    FindingCategory Category,
    Severity Severity,
    double BasePoints,
    EffortScaling Scaling,
    string? Recommendation)
{
    public static RuleDetails From(RuleDefinition r) => new(r.Id, r.Title, r.Category, r.Severity, r.BasePoints, r.Scaling, r.Recommendation);
}

public sealed record FindingExplanation
{
    public required FindingItem Finding { get; init; }
    /// <summary>Recommendation for this finding (package findings carry a package-specific one).</summary>
    public string? Recommendation { get; init; }
    public required RuleDetails Rule { get; init; }
    /// <summary>Effort points this finding contributes before project size/coupling factors.</summary>
    public required double EffortPoints { get; init; }
    public required string EffortModel { get; init; }
    public required int SameRuleElsewhere { get; init; }
    public ProjectOverview? Project { get; init; }
}

public sealed record PackageReport(int Total, int Compatible, IReadOnlyDictionary<PackageStatus, int> ByStatus, IReadOnlyList<PackageCompatibility> Packages);
