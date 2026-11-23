using System.Text.Json;
using LegacyMigrationScanner.Core;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Core.Packages;
using LegacyMigrationScanner.Core.Reporting;
using LegacyMigrationScanner.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyMigrationScanner.Tests;

public class EndToEndTests
{
    private static readonly Lazy<Task<ScanResult>> OfflineResult = new(async () =>
    {
        await using var services = TestHelpers.BuildServices();
        return await services.GetRequiredService<IMigrationScanner>().ScanAsync(TestHelpers.FixtureSolution);
    });

    private static async Task<List<string>> RulesForAsync(string project) =>
        [.. (await OfflineResult.Value).Findings.Where(f => f.Project == project).Select(f => f.RuleId).Distinct()];

    [Fact]
    public async Task Web_HasExpectedBlockers()
    {
        var rules = await RulesForAsync("LegacyShop.Web");

        foreach (var expected in new[]
                 {
                     "LMS001", "LMS002", "LMS003", "LMS004", "LMS100", "LMS101", "LMS102", "LMS103", "LMS104",
                     "LMS110", "LMS111", "LMS113", "LMS114", "LMS202", "LMS211", "LMS500", "LMS600", "LMS601", "LMS602", "LMS603",
                 })
        {
            Assert.Contains(expected, rules);
        }
    }

    [Fact]
    public async Task Services_HasWcfFindings()
    {
        var rules = await RulesForAsync("LegacyShop.Services");

        foreach (var expected in new[] { "LMS005", "LMS200", "LMS201", "LMS210", "LMS212", "LMS301", "LMS113" })
        {
            Assert.Contains(expected, rules);
        }
    }

    [Fact]
    public async Task Data_HasDataAccessAndPlatformFindings()
    {
        var rules = await RulesForAsync("LegacyShop.Data");

        foreach (var expected in new[] { "LMS302", "LMS303", "LMS304", "LMS400", "LMS401", "LMS402", "LMS601", "LMS605" })
        {
            Assert.Contains(expected, rules);
        }
    }

    [Fact]
    public async Task Common_IsClean()
    {
        var result = await OfflineResult.Value;

        Assert.Empty(await RulesForAsync("LegacyShop.Common"));
        Assert.Equal("Green", result.Estimates.Single(e => e.Project == "LegacyShop.Common").Readiness);
    }

    [Fact]
    public async Task Result_IsDeterministic()
    {
        await using var services = TestHelpers.BuildServices();
        var scanner = services.GetRequiredService<IMigrationScanner>();

        var first = await scanner.ScanAsync(TestHelpers.FixtureSolution);
        var second = await scanner.ScanAsync(TestHelpers.FixtureSolution);

        Assert.Equal(
            TestHelpers.Join(first.Findings.Select(f => $"{f.RuleId}|{f.Project}|{f.File}|{f.Count}")),
            TestHelpers.Join(second.Findings.Select(f => $"{f.RuleId}|{f.Project}|{f.File}|{f.Count}")));
    }

    [Fact]
    public async Task FindingPaths_AreRelativeToSolution_WithForwardSlashes()
    {
        Assert.All((await OfflineResult.Value).Findings.Where(f => f.File is not null), f =>
        {
            Assert.False(Path.IsPathRooted(f.File!));
            Assert.DoesNotContain("\\", f.File);
        });
    }

    [Fact]
    public async Task OnlineScan_UsesFeedForUnknownPackages()
    {
        var feed = new FakeNuGetFeed();
        await using var services = TestHelpers.BuildServices(o => o.NuGetSources.Add(FakeNuGetFeed.ServiceIndex), feed);

        var result = await services.GetRequiredService<IMigrationScanner>().ScanAsync(TestHelpers.FixtureSolution);

        var internalPackage = Assert.Single(result.Packages, p => p.Id == "Acme.Internal.Billing");
        Assert.Equal(PackageStatus.NotFound, internalPackage.Status);
        Assert.Contains(result.Findings, f => f.RuleId == "LMS606" && f.Project == "LegacyShop.Web");
    }

    [Fact]
    public async Task Cancellation_IsHonored()
    {
        await using var services = TestHelpers.BuildServices();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => services.GetRequiredService<IMigrationScanner>().ScanAsync(TestHelpers.FixtureSolution, cts.Token));
    }

    [Fact]
    public async Task JsonReport_IsCamelCase_WithStringEnums_AndNoAbsolutePaths()
    {
        using var writer = new StringWriter();
        new JsonReportWriter().Write(await OfflineResult.Value, writer);
        var json = writer.ToString();

        using var doc = JsonDocument.Parse(json);
        var first = doc.RootElement.GetProperty("findings")[0];
        Assert.StartsWith("LMS", first.GetProperty("ruleId").GetString());
        Assert.True(Enum.TryParse<Severity>(first.GetProperty("severity").GetString(), out _));
        Assert.Equal(4, doc.RootElement.GetProperty("projects").GetArrayLength());
        Assert.True(doc.RootElement.GetProperty("packages").GetArrayLength() > 0);
        Assert.DoesNotContain(TestHelpers.FixtureDir, json);
    }

    [Fact]
    public async Task MarkdownReport_ContainsAllSections()
    {
        using var writer = new StringWriter();
        new MarkdownReportWriter().Write(await OfflineResult.Value, writer);
        var md = writer.ToString();

        foreach (var heading in new[] { "## Summary", "## Projects", "## Migration order", "## Issues by rule", "## Details", "### LegacyShop.Web", "## Packages" })
        {
            Assert.Contains(heading, md);
        }
    }
}
