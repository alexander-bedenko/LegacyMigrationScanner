using System.Text.Json.Serialization;
using LegacyMigrationScanner.Core.Packages;
using LegacyMigrationScanner.Core.Rules;

namespace LegacyMigrationScanner.Core.Model;

public sealed record EffortBreakdownItem(string RuleId, int Count, double Points);

public sealed record ProjectEstimate
{
    public required string Project { get; init; }
    public required ProjectKind Kind { get; init; }
    public required int LinesOfCode { get; init; }
    public required double PointsP50 { get; init; }
    public required double PointsP90 { get; init; }
    public required double HoursP50 { get; init; }
    public required double HoursP90 { get; init; }
    /// <summary>Green = no blockers, Yellow = warnings only, Red = critical findings.</summary>
    public required string Readiness { get; init; }
    public IReadOnlyList<EffortBreakdownItem> Breakdown { get; init; } = [];
}

public sealed record ProjectSummary(
    string Name,
    string RelativePath,
    ProjectKind Kind,
    bool IsSdkStyle,
    IReadOnlyList<string> TargetFrameworks,
    int LinesOfCode,
    int PackageCount,
    IReadOnlyList<string> ProjectReferences);

public sealed record ScanResult
{
    public required string ToolVersion { get; init; }
    public required DateTimeOffset ScannedAt { get; init; }
    public required string Solution { get; init; }

    /// <summary>Absolute solution directory; excluded from JSON to keep reports machine-independent.</summary>
    [JsonIgnore]
    public string SolutionDirectory { get; init; } = "";
    public required IReadOnlyList<ProjectSummary> Projects { get; init; }
    public required IReadOnlyList<Finding> Findings { get; init; }
    public required IReadOnlyList<ProjectEstimate> Estimates { get; init; }
    /// <summary>Migration waves: projects in the same wave do not depend on each other.</summary>
    public required IReadOnlyList<IReadOnlyList<string>> MigrationOrder { get; init; }
    public double HoursPerPoint { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Classification of every distinct package version referenced by the solution.</summary>
    public IReadOnlyList<PackageCompatibility> Packages { get; init; } = [];

    /// <summary>Diff against a previous report when scanned with <c>--baseline</c>; null otherwise.</summary>
    public Baselines.BaselineComparison? Baseline { get; init; }

    /// <summary>Rule catalog used for the scan (for SARIF rule metadata); not serialized into the JSON report.</summary>
    [JsonIgnore]
    public IReadOnlyList<RuleDefinition> Rules { get; init; } = [];

    public double TotalHoursP50 => Estimates.Sum(e => e.HoursP50);
    public double TotalHoursP90 => Estimates.Sum(e => e.HoursP90);
}
