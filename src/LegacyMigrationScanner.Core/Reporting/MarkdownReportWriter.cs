using System.Globalization;
using System.Text;
using LegacyMigrationScanner.Core.Model;

namespace LegacyMigrationScanner.Core.Reporting;

public sealed class MarkdownReportWriter : IReportWriter
{
    public string Format => "md";
    public string FileExtension => ".md";

    public void Write(ScanResult result, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(writer);
        var r = result;
        var w = writer;
        var inv = CultureInfo.InvariantCulture;
        w.WriteLine($"# Migration report: {r.Solution}");
        w.WriteLine();
        w.WriteLine($"Scanned {r.ScannedAt:yyyy-MM-dd HH:mm} with Legacy Migration Scanner {r.ToolVersion}. " +
                    $"Estimate assumes {r.HoursPerPoint.ToString(inv)} h per point.");
        w.WriteLine();

        w.WriteLine("## Summary");
        w.WriteLine();
        w.WriteLine("| Metric | Value |");
        w.WriteLine("|---|---|");
        w.WriteLine($"| Projects | {r.Projects.Count} |");
        w.WriteLine($"| Findings (critical / warning / info) | {Count(r, Severity.Critical)} / {Count(r, Severity.Warning)} / {Count(r, Severity.Info)} |");
        w.WriteLine($"| Estimated effort, P50 | {r.TotalHoursP50.ToString("0", inv)} h (~{(r.TotalHoursP50 / 8).ToString("0.#", inv)} person-days) |");
        w.WriteLine($"| Estimated effort, P90 | {r.TotalHoursP90.ToString("0", inv)} h (~{(r.TotalHoursP90 / 8).ToString("0.#", inv)} person-days) |");
        w.WriteLine();

        WriteBaseline(r, w);

        if (r.Warnings.Count > 0)
        {
            w.WriteLine("> **Scanner warnings**");
            foreach (var warning in r.Warnings)
            {
                w.WriteLine($"> - {Escape(warning)}");
            }

            w.WriteLine();
        }

        w.WriteLine("## Projects");
        w.WriteLine();
        w.WriteLine("| Project | Kind | Format | Target | LOC | Readiness | P50, h | P90, h | Top cost drivers |");
        w.WriteLine("|---|---|---|---|---:|---|---:|---:|---|");
        foreach (var e in r.Estimates.OrderByDescending(e => e.HoursP50))
        {
            var p = r.Projects.First(x => x.Name == e.Project);
            var drivers = string.Join(", ", e.Breakdown.Take(3).Select(b => $"{b.RuleId} ({b.Points.ToString("0.#", inv)})"));
            w.WriteLine($"| {Escape(e.Project)} | {e.Kind} | {(p.IsSdkStyle ? "SDK" : "Legacy")} | {string.Join(", ", p.TargetFrameworks)} | " +
                        $"{e.LinesOfCode} | {Badge(e.Readiness)} | {e.HoursP50.ToString(inv)} | {e.HoursP90.ToString(inv)} | {drivers} |");
        }

        w.WriteLine();

        w.WriteLine("## Migration order");
        w.WriteLine();
        w.WriteLine("Projects in the same wave do not depend on each other and can be migrated in parallel.");
        w.WriteLine();
        for (var i = 0; i < r.MigrationOrder.Count; i++)
        {
            w.WriteLine($"{i + 1}. {string.Join(", ", r.MigrationOrder[i].Select(Escape))}");
        }

        w.WriteLine();

        w.WriteLine("## Issues by rule");
        w.WriteLine();
        w.WriteLine("| Rule | Severity | Title | Projects | Occurrences | Recommendation |");
        w.WriteLine("|---|---|---|---:|---:|---|");
        foreach (var g in r.Findings.GroupBy(f => f.RuleId).OrderByDescending(g => g.Max(f => f.Severity)).ThenBy(g => g.Key))
        {
            var first = g.First();
            // Package findings carry per-package recommendations; show the generic one here.
            var recommendation = first.Category == FindingCategory.Package ? "See details below" : first.Recommendation;
            w.WriteLine($"| {g.Key} | {first.Severity} | {Escape(first.Title)} | {g.Select(f => f.Project).Distinct().Count()} | " +
                        $"{g.Sum(f => f.Count)} | {Escape(recommendation)} |");
        }

        w.WriteLine();

        w.WriteLine("## Details");
        foreach (var project in r.Findings.GroupBy(f => f.Project))
        {
            w.WriteLine();
            w.WriteLine($"### {Escape(project.Key)}");
            w.WriteLine();
            w.WriteLine("| Severity | Rule | Location | Count | Detail | Recommendation |");
            w.WriteLine("|---|---|---|---:|---|---|");
            foreach (var f in project)
            {
                var location = f.File is null ? "" : f.Line is null ? f.File : $"{f.File}:{f.Line}";
                var recommendation = f.Category == FindingCategory.Package ? f.Recommendation : "";
                w.WriteLine($"| {f.Severity} | {f.RuleId} {Escape(f.Title)} | {Code(location)} | {f.Count} | {Escape(f.Detail)} | {Escape(recommendation)} |");
            }
        }

        WritePackages(r, w);

        var clean = r.Estimates.Where(e => e.Breakdown.Count == 0).Select(e => e.Project).ToList();
        if (clean.Count > 0)
        {
            w.WriteLine();
            w.WriteLine($"No issues found in: {string.Join(", ", clean.Select(Escape))}.");
        }
    }

    private static void WriteBaseline(ScanResult r, TextWriter w)
    {
        if (r.Baseline is not { } b)
        {
            return;
        }

        w.WriteLine("## Changes since baseline");
        w.WriteLine();
        var version = b.BaselineToolVersion is null ? "" : $", scanner {b.BaselineToolVersion}";
        w.WriteLine($"Compared with `{Escape(b.BaselinePath)}` ({b.BaselineFindingCount} findings{version}): " +
                    $"**{b.New.Count} new**, {b.Fixed.Count} fixed, {b.Unchanged} unchanged.");
        w.WriteLine();

        if (b.New.Count > 0)
        {
            w.WriteLine("### New findings");
            w.WriteLine();
            w.WriteLine("| Severity | Rule | Project | Location | Detail |");
            w.WriteLine("|---|---|---|---|---|");
            foreach (var f in b.New)
            {
                var location = f.File is null ? "" : f.Line is null ? f.File : $"{f.File}:{f.Line}";
                w.WriteLine($"| {f.Severity} | {f.RuleId} {Escape(f.Title)} | {Escape(f.Project)} | {Code(location)} | {Escape(f.Detail)} |");
            }

            w.WriteLine();
        }

        if (b.Fixed.Count > 0)
        {
            w.WriteLine("### Fixed since baseline");
            w.WriteLine();
            w.WriteLine("| Severity | Rule | Project | Location | Detail |");
            w.WriteLine("|---|---|---|---|---|");
            foreach (var f in b.Fixed)
            {
                w.WriteLine($"| {f.Severity} | {f.RuleId} {Escape(f.Title)} | {Escape(f.Project)} | {Code(f.File)} | {Escape(f.Detail)} |");
            }

            w.WriteLine();
        }
    }

    private static void WritePackages(ScanResult r, TextWriter w)
    {
        var interesting = r.Packages.Where(p => p.Status != Packages.PackageStatus.Compatible).ToList();
        w.WriteLine();
        w.WriteLine("## Packages");
        w.WriteLine();
        w.WriteLine($"{r.Packages.Count} distinct package version(s), {r.Packages.Count - interesting.Count} compatible as-is.");
        if (interesting.Count == 0)
        {
            return;
        }

        w.WriteLine();
        w.WriteLine("| Package | Version | Status | Target | Source | Note |");
        w.WriteLine("|---|---|---|---|---|---|");
        foreach (var p in interesting.OrderBy(p => p.Status).ThenBy(p => p.Id, StringComparer.OrdinalIgnoreCase))
        {
            var target = p.RecommendedVersion ?? p.Replacement ?? "";
            w.WriteLine($"| {Escape(p.Id)} | {Escape(p.Version)} | {p.Status} | {Escape(target)} | {Escape(p.Origin)} | {Escape(p.Reason)} |");
        }
    }

    private static int Count(ScanResult r, Severity severity) => r.Findings.Count(f => f.Severity == severity);

    private static string Badge(string readiness) => readiness switch
    {
        "Red" => "🔴 Red",
        "Yellow" => "🟡 Yellow",
        _ => "🟢 Green",
    };

    /// <summary>Inline code span; empty for missing values (an empty span renders as literal backticks).</summary>
    private static string Code(string? text) => string.IsNullOrEmpty(text) ? "" : $"`{Escape(text)}`";

    private static string Escape(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            sb.Append(c switch
            {
                '|' => "\\|",
                '\n' or '\r' => " ",
                _ => c.ToString(),
            });
        }

        return sb.ToString();
    }
}
