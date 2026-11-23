using System.Text;
using System.Text.RegularExpressions;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Core.Rules;
using LegacyMigrationScanner.Tests.Infrastructure;

namespace LegacyMigrationScanner.Tests;

public class RuleCatalogTests
{
    [Fact]
    public void DefaultCatalog_IsValid()
    {
        var rules = TestHelpers.Rules.All;

        Assert.NotEmpty(rules);
        Assert.All(rules, r =>
        {
            Assert.Matches(@"^LMS\d{3}$", r.Id);
            Assert.False(string.IsNullOrWhiteSpace(r.Title));
            Assert.True(r.BasePoints > 0, $"{r.Id} has no effort points");
        });
    }

    [Fact]
    public void EveryRuleIdUsedInSources_ExistsInCatalog()
    {
        var sourceDir = Path.Combine(TestHelpers.RepoRoot, "src", "LegacyMigrationScanner.Core", "Analyzers");
        var used = Directory.EnumerateFiles(sourceDir, "*.cs")
            .SelectMany(f => Regex.Matches(File.ReadAllText(f), "\"(LMS\\d{3})\"").Select(m => m.Groups[1].Value))
            .ToHashSet();

        Assert.NotEmpty(used);
        foreach (var id in used)
        {
            Assert.NotNull(TestHelpers.Rules.Get(id));
        }
    }

    [Fact]
    public void Overrides_ReplaceOnlySpecifiedRules()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, """
                { "rules": [ { "id": "LMS100", "title": "Web Forms", "category": "SystemWeb", "severity": "Warning", "basePoints": 10, "scaling": "Linear" } ] }
                """, Encoding.UTF8);

            var catalog = RuleCatalog.LoadWithOverrides(path);

            Assert.Equal(10, catalog.Get("LMS100").BasePoints);
            Assert.Equal(Severity.Warning, catalog.Get("LMS100").Severity);
            Assert.Equal(TestHelpers.Rules.Get("LMS001"), catalog.Get("LMS001"));
            Assert.Equal(TestHelpers.Rules.All.Count, catalog.All.Count);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
