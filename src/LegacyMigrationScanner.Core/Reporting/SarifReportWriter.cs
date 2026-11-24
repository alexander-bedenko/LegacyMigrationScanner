using System.Text.Json;
using System.Text.Json.Nodes;
using LegacyMigrationScanner.Core.Baselines;
using LegacyMigrationScanner.Core.Model;

namespace LegacyMigrationScanner.Core.Reporting;

/// <summary>
/// SARIF 2.1.0 output for GitHub code scanning / Azure DevOps / VS SARIF viewer.
/// Locations are relative to <c>baseDirectory</c> under the %SRCROOT% base id.
/// </summary>
public sealed class SarifReportWriter(string baseDirectory) : IReportWriter
{
    private const string SchemaUri = "https://json.schemastore.org/sarif-2.1.0.json";
    private const string InformationUri = "https://github.com/alexander-bedenko/LegacyMigrationScanner";

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public string Format => "sarif";
    public string FileExtension => ".sarif";

    public void Write(ScanResult result, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(writer);

        var usedRuleIds = result.Findings.Select(f => f.RuleId).ToHashSet(StringComparer.Ordinal);
        var rules = result.Rules.Where(r => usedRuleIds.Contains(r.Id)).OrderBy(r => r.Id, StringComparer.Ordinal).ToList();
        var ruleIndex = rules.Select((r, i) => (r.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);

        var driverRules = new JsonArray();
        foreach (var rule in rules)
        {
            driverRules.Add(new JsonObject
            {
                ["id"] = rule.Id,
                ["name"] = ToPascalName(rule.Title),
                ["shortDescription"] = new JsonObject { ["text"] = rule.Title },
                ["fullDescription"] = new JsonObject { ["text"] = rule.Recommendation ?? rule.Title },
                ["help"] = new JsonObject { ["text"] = rule.Recommendation ?? rule.Title },
                ["defaultConfiguration"] = new JsonObject { ["level"] = Level(rule.Severity) },
                ["properties"] = new JsonObject
                {
                    ["category"] = rule.Category.ToString(),
                    ["tags"] = new JsonArray("migration", rule.Category.ToString()),
                    ["effortPoints"] = rule.BasePoints,
                },
            });
        }

        var results = new JsonArray();
        foreach (var finding in result.Findings)
        {
            results.Add(ToResult(finding, result, ruleIndex));
        }

        var root = new JsonObject
        {
            ["$schema"] = SchemaUri,
            ["version"] = "2.1.0",
            ["runs"] = new JsonArray(new JsonObject
            {
                ["tool"] = new JsonObject
                {
                    ["driver"] = new JsonObject
                    {
                        ["name"] = "LegacyMigrationScanner",
                        ["semanticVersion"] = result.ToolVersion,
                        ["informationUri"] = InformationUri,
                        ["rules"] = driverRules,
                    },
                },
                ["originalUriBaseIds"] = new JsonObject
                {
                    ["SRCROOT"] = new JsonObject { ["uri"] = new Uri(EnsureTrailingSeparator(Path.GetFullPath(baseDirectory))).AbsoluteUri },
                },
                ["results"] = results,
                ["properties"] = new JsonObject
                {
                    ["solution"] = result.Solution,
                    ["estimatedHoursP50"] = result.TotalHoursP50,
                    ["estimatedHoursP90"] = result.TotalHoursP90,
                },
            }),
        };

        writer.Write(root.ToJsonString(Options));
    }

    private JsonObject ToResult(Finding finding, ScanResult result, Dictionary<string, int> ruleIndex)
    {
        var projectPath = result.Projects.FirstOrDefault(p => p.Name == finding.Project)?.RelativePath;
        var file = finding.File ?? projectPath ?? result.Solution;
        var uri = Path.GetRelativePath(baseDirectory, Path.Combine(result.SolutionDirectory, file)).Replace('\\', '/');

        var message = finding.Detail is null ? finding.Title : $"{finding.Title}: {finding.Detail}";
        if (finding.Count > 1)
        {
            message += $" ({finding.Count} occurrences)";
        }

        var physicalLocation = new JsonObject
        {
            ["artifactLocation"] = new JsonObject { ["uri"] = uri, ["uriBaseId"] = "SRCROOT" },
        };
        if (finding.Line is { } line)
        {
            physicalLocation["region"] = new JsonObject { ["startLine"] = line };
        }

        var sarifResult = new JsonObject
        {
            ["ruleId"] = finding.RuleId,
            ["ruleIndex"] = ruleIndex[finding.RuleId],
            ["level"] = Level(finding.Severity),
            ["message"] = new JsonObject { ["text"] = message + (finding.Recommendation is null ? "" : ". " + finding.Recommendation) },
            ["locations"] = new JsonArray(new JsonObject { ["physicalLocation"] = physicalLocation }),
            // Stable across runs so code scanning can track alerts when line numbers shift.
            ["partialFingerprints"] = new JsonObject
            {
                [FindingFingerprint.Name] = finding.Fingerprint ?? FindingFingerprint.Compute(finding.RuleId, finding.Project, file, finding.Detail),
            },
            ["properties"] = new JsonObject
            {
                ["project"] = finding.Project,
                ["occurrences"] = finding.Count,
            },
        };

        if (result.Baseline is { } baseline)
        {
            sarifResult["baselineState"] = baseline.IsNew(finding) ? "new" : "unchanged";
        }

        return sarifResult;
    }

    private static string Level(Severity severity) => severity switch
    {
        Severity.Critical => "error",
        Severity.Warning => "warning",
        _ => "note",
    };

    private static string ToPascalName(string title) =>
        string.Concat(title.Split([' ', '-', '/', '(', ')', '.', ',', '<', '>', '[', ']'], StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));

    private static string EnsureTrailingSeparator(string path) =>
        path.EndsWith(Path.DirectorySeparatorChar) ? path : path + Path.DirectorySeparatorChar;
}
