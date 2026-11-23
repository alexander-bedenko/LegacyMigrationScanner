namespace LegacyMigrationScanner.Core.Model;

/// <summary>
/// One detected migration issue. Findings are aggregated per (rule, project, file):
/// <see cref="Count"/> holds the number of occurrences and <see cref="Line"/> the first one.
/// </summary>
public sealed record Finding
{
    public required string RuleId { get; init; }
    public required string Title { get; init; }
    public required FindingCategory Category { get; init; }
    public required Severity Severity { get; init; }
    public required string Project { get; init; }
    /// <summary>Path relative to the solution directory, or null for project-level findings.</summary>
    public string? File { get; init; }
    public int? Line { get; init; }
    public int Count { get; init; } = 1;
    public string? Detail { get; init; }
    public string? Recommendation { get; init; }
}
