using LegacyMigrationScanner.Core.Model;

namespace LegacyMigrationScanner.Core.Baselines;

/// <summary>Result of diffing a scan against a <see cref="Baseline"/> by <see cref="FindingFingerprint"/>.</summary>
public sealed class BaselineComparison
{
    /// <summary>Baseline file as given on the command line.</summary>
    public required string BaselinePath { get; init; }
    public required string BaselineSolution { get; init; }
    public string? BaselineToolVersion { get; init; }
    public required int BaselineFindingCount { get; init; }

    /// <summary>Current findings that are not in the baseline.</summary>
    public required IReadOnlyList<Finding> New { get; init; }

    /// <summary>Baseline findings that no longer occur.</summary>
    public required IReadOnlyList<BaselineFinding> Fixed { get; init; }
    public required int Unchanged { get; init; }

    private HashSet<Finding>? _newSet;

    /// <summary>
    /// Compares by fingerprint as a multiset: if the same fingerprint occurs more often now than in the
    /// baseline, the extra occurrences are new (order of <see cref="ScanResult.Findings"/> decides which).
    /// </summary>
    public static BaselineComparison Compare(ScanResult current, Baseline baseline, string baselinePath)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(baseline);

        var remaining = baseline.Findings
            .GroupBy(f => f.Fingerprint, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => new Queue<BaselineFinding>(g), StringComparer.Ordinal);

        var newFindings = new List<Finding>();
        var unchanged = 0;
        foreach (var finding in current.Findings)
        {
            var fingerprint = finding.Fingerprint
                ?? throw new InvalidOperationException($"Finding {finding.RuleId} in {finding.Project} has no fingerprint.");
            if (remaining.TryGetValue(fingerprint, out var queue) && queue.Count > 0)
            {
                queue.Dequeue();
                unchanged++;
            }
            else
            {
                newFindings.Add(finding);
            }
        }

        return new BaselineComparison
        {
            BaselinePath = baselinePath,
            BaselineSolution = baseline.Solution,
            BaselineToolVersion = baseline.ToolVersion,
            BaselineFindingCount = baseline.Findings.Count,
            New = newFindings,
            Fixed = [.. remaining.Values.SelectMany(q => q)
                .OrderBy(f => f.Project, StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(f => f.Severity)
                .ThenBy(f => f.RuleId, StringComparer.Ordinal)
                .ThenBy(f => f.File, StringComparer.OrdinalIgnoreCase)],
            Unchanged = unchanged,
        };
    }

    /// <summary>Whether this exact finding instance (from the compared scan) is new.</summary>
    public bool IsNew(Finding finding) =>
        (_newSet ??= new HashSet<Finding>(New, ReferenceEqualityComparer.Instance)).Contains(finding);
}
