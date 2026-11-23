using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using LegacyMigrationScanner.Core.Model;

namespace LegacyMigrationScanner.Core.Rules;

/// <summary>How effort grows with the number of occurrences of a rule inside one finding.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<EffortScaling>))]
public enum EffortScaling
{
    /// <summary>Mechanical, repetitive fixes: basePoints × (1 + log2(count)).</summary>
    Log,
    /// <summary>Each occurrence is real work (page, service contract): basePoints × count.</summary>
    Linear,
}

public sealed record RuleDefinition
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required FindingCategory Category { get; init; }
    public required Severity Severity { get; init; }
    public required double BasePoints { get; init; }
    public EffortScaling Scaling { get; init; } = EffortScaling.Log;
    public string? Recommendation { get; init; }
}

/// <summary>Rule metadata (title, severity, effort points). Detection logic lives in analyzers.</summary>
public sealed class RuleCatalog
{
    private readonly Dictionary<string, RuleDefinition> _rules;

    public RuleCatalog(IEnumerable<RuleDefinition> rules) =>
        _rules = rules.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);

    public IReadOnlyCollection<RuleDefinition> All => _rules.Values;

    public RuleDefinition Get(string id) =>
        _rules.TryGetValue(id, out var rule) ? rule : throw new KeyNotFoundException($"Unknown rule id '{id}'.");

    public static RuleCatalog LoadDefault()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LegacyMigrationScanner.rules.json")
                           ?? throw new InvalidOperationException("Embedded rules.json not found.");
        return Load(stream);
    }

    /// <summary>Loads a catalog from JSON; used for custom/calibrated rule files.</summary>
    public static RuleCatalog Load(Stream json)
    {
        var file = JsonSerializer.Deserialize<RuleFile>(json, JsonOptions)
                   ?? throw new InvalidDataException("Rules file is empty.");
        return new RuleCatalog(file.Rules);
    }

    public static RuleCatalog LoadWithOverrides(string? overridesPath)
    {
        var catalog = LoadDefault();
        if (overridesPath is null)
        {
            return catalog;
        }

        using var stream = File.OpenRead(overridesPath);
        var overrides = Load(stream);
        var merged = catalog._rules.Values.ToDictionary(r => r.Id, StringComparer.OrdinalIgnoreCase);
        foreach (var rule in overrides.All)
        {
            merged[rule.Id] = rule;
        }

        return new RuleCatalog(merged.Values);
    }

    /// <summary>Effort points for one finding with <paramref name="count"/> occurrences.</summary>
    public static double Points(RuleDefinition rule, int count)
    {
        count = Math.Max(1, count);
        return rule.Scaling == EffortScaling.Linear
            ? rule.BasePoints * count
            : rule.BasePoints * (1 + Math.Log2(count));
    }

    public Finding Create(
        string ruleId,
        string project,
        string? file = null,
        int? line = null,
        int count = 1,
        string? detail = null,
        Severity? severityOverride = null,
        string? recommendationOverride = null)
    {
        var rule = Get(ruleId);
        return new Finding
        {
            RuleId = rule.Id,
            Title = rule.Title,
            Category = rule.Category,
            Severity = severityOverride ?? rule.Severity,
            Project = project,
            File = file,
            Line = line,
            Count = count,
            Detail = detail,
            Recommendation = recommendationOverride ?? rule.Recommendation,
        };
    }

    internal static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private sealed record RuleFile(List<RuleDefinition> Rules);
}
