using System.ComponentModel;
using System.Globalization;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Core.Packages;
using LegacyMigrationScanner.Core.Rules;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace LegacyMigrationScanner.Mcp;

/// <summary>Read-only MCP tools over <see cref="ScanSession"/> (ADR 0004).</summary>
[McpServerToolType]
public sealed class ScannerTools(ScanSession session)
{
    public const int MaxPageSize = 500;

    [McpServerTool(Name = "scan_solution", Title = "Scan a .NET Framework solution", ReadOnly = true, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Scans a .NET Framework solution (no build needed) for blockers to migrating to modern .NET and returns a summary: " +
                 "per-project readiness (Red/Yellow/Green) and effort in hours (P50/P90), migration waves, the most significant rules and " +
                 "packages needing action. Results are cached for this session; call get_findings / explain_finding / get_packages next.")]
    public async Task<ScanSummary> ScanSolution(
        [Description("Path to a .sln, .slnx or .csproj, or a directory containing one solution. Relative paths resolve against the server's root.")] string path,
        [Description("Re-scan even if this path was scanned earlier in the session (use after the code changed).")] bool refresh = false,
        CancellationToken cancellationToken = default)
    {
        var r = await session.GetAsync(path, refresh, cancellationToken);
        return new ScanSummary
        {
            Solution = r.Solution,
            ToolVersion = r.ToolVersion,
            ScannedAt = r.ScannedAt,
            HoursPerPoint = r.HoursPerPoint,
            TotalHoursP50 = r.TotalHoursP50,
            TotalHoursP90 = r.TotalHoursP90,
            Findings = SeverityCounts.Of(r.Findings),
            Projects = [.. r.Estimates.OrderByDescending(e => e.HoursP50).Select(e => Overview(r, e))],
            MigrationWaves = r.MigrationOrder,
            TopRules = [.. r.Findings.GroupBy(f => f.RuleId)
                .Select(g => new RuleOverview(g.Key, g.First().Title, g.Max(f => f.Severity), g.Sum(f => f.Count), g.Select(f => f.Project).Distinct().Count()))
                .OrderByDescending(x => x.Severity).ThenByDescending(x => x.Occurrences).ThenBy(x => x.RuleId, StringComparer.Ordinal)
                .Take(10)],
            PackagesNeedingAction = r.Packages.Count(p => p.Status != PackageStatus.Compatible),
            Warnings = r.Warnings,
        };
    }

    [McpServerTool(Name = "get_findings", Title = "List migration findings", ReadOnly = true, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Lists findings of a scanned solution, most severe first, with filters and paging. Each finding has a fingerprint for explain_finding.")]
    public async Task<FindingsPage> GetFindings(
        [Description("Same path as passed to scan_solution (scanned on demand if needed).")] string path,
        [Description("Only this project (exact name).")] string? project = null,
        [Description("Minimum severity: critical, warning or info (default: all).")] string? minSeverity = null,
        [Description("Only this rule id, e.g. LMS111.")] string? ruleId = null,
        [Description("Only this category: ProjectFormat, SystemWeb, Wcf, Package, Configuration, Platform, DataAccess.")] string? category = null,
        [Description("Page size, 1-500 (default 50).")] int limit = 50,
        [Description("Number of findings to skip (default 0).")] int offset = 0,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > MaxPageSize)
        {
            throw new McpException($"limit must be between 1 and {MaxPageSize}.");
        }

        if (offset < 0)
        {
            throw new McpException("offset must not be negative.");
        }

        var severity = ParseEnum<Severity>(minSeverity, nameof(minSeverity));
        var cat = ParseEnum<FindingCategory>(category, nameof(category));
        var r = await session.GetAsync(path, refresh: false, cancellationToken);

        var filtered = r.Findings
            .Where(f => project is null || string.Equals(f.Project, project, StringComparison.OrdinalIgnoreCase))
            .Where(f => severity is null || f.Severity >= severity)
            .Where(f => ruleId is null || string.Equals(f.RuleId, ruleId, StringComparison.OrdinalIgnoreCase))
            .Where(f => cat is null || f.Category == cat)
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.Project, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.RuleId, StringComparer.Ordinal)
            .ThenBy(f => f.File, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new FindingsPage(filtered.Count, offset, [.. filtered.Skip(offset).Take(limit).Select(FindingItem.From)]);
    }

    [McpServerTool(Name = "explain_finding", Title = "Explain a finding", ReadOnly = true, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Explains one finding (by fingerprint from get_findings): what the rule means, the recommended fix, how its effort is computed " +
                 "and how it affects its project's estimate. Read the referenced file yourself for code-level advice.")]
    public async Task<FindingExplanation> ExplainFinding(
        [Description("Same path as passed to scan_solution.")] string path,
        [Description("Finding fingerprint from get_findings.")] string fingerprint,
        CancellationToken cancellationToken = default)
    {
        var r = await session.GetAsync(path, refresh: false, cancellationToken);
        var finding = r.Findings.FirstOrDefault(f => string.Equals(f.Fingerprint, fingerprint, StringComparison.OrdinalIgnoreCase))
                      ?? throw new McpException($"No finding with fingerprint '{fingerprint}' in {r.Solution}. Use get_findings to list fingerprints (they are stable across scans).");
        var rule = r.Rules.FirstOrDefault(x => string.Equals(x.Id, finding.RuleId, StringComparison.OrdinalIgnoreCase))
                   ?? throw new McpException($"Rule {finding.RuleId} is not in the rule catalog.");
        var estimate = r.Estimates.FirstOrDefault(e => e.Project == finding.Project);

        return new FindingExplanation
        {
            Finding = FindingItem.From(finding),
            Recommendation = finding.Recommendation ?? rule.Recommendation,
            Rule = RuleDetails.From(rule),
            EffortPoints = Math.Round(RuleCatalog.Points(rule, finding.Count), 2),
            EffortModel = EffortModel(rule, finding.Count, r.HoursPerPoint),
            SameRuleElsewhere = r.Findings.Count(f => f.RuleId == finding.RuleId) - 1,
            Project = estimate is null ? null : Overview(r, estimate),
        };
    }

    [McpServerTool(Name = "get_packages", Title = "Package compatibility", ReadOnly = true, Idempotent = true, OpenWorld = true, UseStructuredContent = true)]
    [Description("Lists NuGet packages referenced by the solution with their modern .NET status (UpgradeRequired, Replace, Remove, Review, " +
                 "Incompatible, NotFound, Unknown) and the recommended version or replacement.")]
    public async Task<PackageReport> GetPackages(
        [Description("Same path as passed to scan_solution.")] string path,
        [Description("Also list packages that are already compatible (default false).")] bool includeCompatible = false,
        CancellationToken cancellationToken = default)
    {
        var r = await session.GetAsync(path, refresh: false, cancellationToken);
        var byStatus = r.Packages.GroupBy(p => p.Status).OrderBy(g => g.Key).ToDictionary(g => g.Key, g => g.Count());
        var listed = r.Packages
            .Where(p => includeCompatible || p.Status != PackageStatus.Compatible)
            .OrderBy(p => p.Status).ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new PackageReport(r.Packages.Count, byStatus.GetValueOrDefault(PackageStatus.Compatible), byStatus, listed);
    }

    private static ProjectOverview Overview(ScanResult r, ProjectEstimate e)
    {
        var p = r.Projects.FirstOrDefault(x => x.Name == e.Project);
        return new ProjectOverview(
            e.Project, e.Kind, e.Readiness, p?.IsSdkStyle ?? false, p?.TargetFrameworks ?? [], e.LinesOfCode,
            e.HoursP50, e.HoursP90, SeverityCounts.Of(r.Findings.Where(f => f.Project == e.Project)),
            [.. e.Breakdown.Take(3).Select(b => b.RuleId)]);
    }

    private static string EffortModel(RuleDefinition rule, int count, double hoursPerPoint)
    {
        var inv = CultureInfo.InvariantCulture;
        var formula = rule.Scaling == EffortScaling.Linear
            ? string.Create(inv, $"Linear: {rule.BasePoints} points × {count} occurrence(s)")
            : string.Create(inv, $"Log: {rule.BasePoints} points × (1 + log2({count}))");
        return formula + string.Create(inv,
            $"; the project total is then scaled by size and coupling factors and converted at {hoursPerPoint} h per point (P90 widens with the share of critical work).");
    }

    private static TEnum? ParseEnum<TEnum>(string? value, string name)
        where TEnum : struct, Enum =>
        string.IsNullOrWhiteSpace(value) ? null
        : Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed) ? parsed
        : throw new McpException($"Invalid {name} '{value}'. Expected one of: {string.Join(", ", Enum.GetNames<TEnum>())}.");
}
