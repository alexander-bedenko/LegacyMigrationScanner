using System.Text.Json;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Core.Reporting;

namespace LegacyMigrationScanner.Core.Baselines;

/// <summary>A finding as recorded in a previous JSON report.</summary>
public sealed record BaselineFinding
{
    public required string Fingerprint { get; init; }
    public required string RuleId { get; init; }
    public required string Title { get; init; }
    public required Severity Severity { get; init; }
    public required string Project { get; init; }
    public string? File { get; init; }
    public string? Detail { get; init; }
}

/// <summary>Findings of a previous scan, loaded from its JSON report (<c>-f json</c>).</summary>
public sealed record Baseline
{
    public required string Solution { get; init; }
    public string? ToolVersion { get; init; }
    public required IReadOnlyList<BaselineFinding> Findings { get; init; }

    /// <summary>Loads a JSON report. Reports without stored fingerprints (0.2.0) are fingerprinted on load.</summary>
    /// <exception cref="InvalidDataException">The file is not a Legacy Migration Scanner JSON report.</exception>
    public static Baseline Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!System.IO.File.Exists(path))
        {
            throw new FileNotFoundException($"Baseline report not found: {path}", path);
        }

        BaselineReport? report;
        try
        {
            using var stream = System.IO.File.OpenRead(path);
            report = JsonSerializer.Deserialize<BaselineReport>(stream, JsonReportWriter.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Baseline '{path}' is not a valid JSON report: {ex.Message}", ex);
        }

        if (report?.Solution is null || report.Findings is null)
        {
            throw new InvalidDataException($"Baseline '{path}' is not a Legacy Migration Scanner JSON report (expected 'solution' and 'findings'). Use the report written by '-f json'.");
        }

        var projectPaths = (report.Projects ?? [])
            .Where(p => p.Name is not null && p.RelativePath is not null)
            .GroupBy(p => p.Name!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First().RelativePath!, StringComparer.Ordinal);

        var findings = report.Findings
            .Where(f => f.RuleId is not null && f.Project is not null)
            .Select(f => new BaselineFinding
            {
                Fingerprint = f.Fingerprint ?? FindingFingerprint.Compute(
                    f.RuleId!, f.Project!, f.File ?? projectPaths.GetValueOrDefault(f.Project!) ?? report.Solution, f.Detail),
                RuleId = f.RuleId!,
                Title = f.Title ?? f.RuleId!,
                Severity = f.Severity,
                Project = f.Project!,
                File = f.File,
                Detail = f.Detail,
            })
            .ToList();

        return new Baseline { Solution = report.Solution, ToolVersion = report.ToolVersion, Findings = findings };
    }

    // Lenient DTOs: only what the diff needs, tolerant to fields added or removed between versions.
    private sealed record BaselineReport(string? ToolVersion, string? Solution, List<ReportProject>? Projects, List<ReportFinding>? Findings);

    private sealed record ReportProject(string? Name, string? RelativePath);

    private sealed record ReportFinding(string? RuleId, string? Title, Severity Severity, string? Project, string? File, string? Detail, string? Fingerprint);
}
