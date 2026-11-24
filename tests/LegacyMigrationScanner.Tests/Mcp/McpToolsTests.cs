using LegacyMigrationScanner.Core;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Core.Packages;
using LegacyMigrationScanner.Mcp;
using LegacyMigrationScanner.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;

namespace LegacyMigrationScanner.Tests.Mcp;

public sealed class McpToolsTests : IAsyncLifetime
{
    private static readonly string FixturesRoot = Path.GetDirectoryName(TestHelpers.FixtureDir)!;

    private ServiceProvider _services = null!;
    private ScanSession _session = null!;
    private ScannerTools _tools = null!;

    public Task InitializeAsync()
    {
        _services = TestHelpers.BuildServices();
        var settings = new McpServerSettings();
        settings.AllowedRoots.Add(FixturesRoot);
        _session = new ScanSession(_services.GetRequiredService<IMigrationScanner>(), new PathGuard(settings));
        _tools = new ScannerTools(_session);
        return Task.CompletedTask;
    }

    public async Task DisposeAsync() => await _services.DisposeAsync();

    [Fact]
    public async Task ScanSolution_SummarizesTheScan()
    {
        var summary = await _tools.ScanSolution("LegacyShop");

        Assert.Equal("LegacyShop.sln", summary.Solution);
        Assert.Equal(4, summary.Projects.Count);
        Assert.Equal(summary.Projects.Sum(p => p.HoursP50), summary.TotalHoursP50);
        Assert.Equal(summary.Findings.Critical, summary.Projects.Sum(p => p.Findings.Critical));
        Assert.NotEmpty(summary.MigrationWaves);
        Assert.InRange(summary.TopRules.Count, 1, 10);
        Assert.Equal(Severity.Critical, summary.TopRules[0].Severity);
        Assert.Equal("Red", summary.Projects[0].Readiness); // ordered by effort
    }

    [Fact]
    public async Task Session_CachesPerPath_UntilRefresh()
    {
        var first = await _session.GetAsync("LegacyShop", refresh: false, CancellationToken.None);
        var again = await _session.GetAsync(TestHelpers.FixtureDir, refresh: false, CancellationToken.None);
        var refreshed = await _session.GetAsync("LegacyShop", refresh: true, CancellationToken.None);

        Assert.Same(first, again);
        Assert.NotSame(first, refreshed);
    }

    [Fact]
    public async Task GetFindings_FiltersAndPages()
    {
        var all = await _tools.GetFindings("LegacyShop", limit: 500);
        var critical = await _tools.GetFindings("LegacyShop", minSeverity: "critical", limit: 500);
        var page = await _tools.GetFindings("LegacyShop", limit: 5, offset: 5);
        var web = await _tools.GetFindings("LegacyShop", project: "legacyshop.web", category: "systemweb", limit: 500);

        Assert.Equal(all.Total, all.Returned);
        Assert.All(critical.Findings, f => Assert.Equal(Severity.Critical, f.Severity));
        Assert.Equal(17, critical.Total);
        Assert.Equal(5, page.Returned);
        Assert.True(page.HasMore);
        Assert.Equal(all.Findings[5], page.Findings[0]);
        Assert.NotEmpty(web.Findings);
        Assert.All(web.Findings, f => Assert.True(f.Project == "LegacyShop.Web" && f.Category == FindingCategory.SystemWeb));
        Assert.All(all.Findings, f => Assert.Matches("^[0-9a-f]{32}$", f.Fingerprint));
    }

    [Theory]
    [InlineData("fatal", 50, 0)]
    [InlineData(null, 0, 0)]
    [InlineData(null, 501, 0)]
    [InlineData(null, 50, -1)]
    public async Task GetFindings_InvalidArguments_AreToolErrors(string? minSeverity, int limit, int offset) =>
        await Assert.ThrowsAsync<McpException>(() => _tools.GetFindings("LegacyShop", minSeverity: minSeverity, limit: limit, offset: offset));

    [Fact]
    public async Task ExplainFinding_ReturnsRuleRecommendationAndEffort()
    {
        var target = (await _tools.GetFindings("LegacyShop", ruleId: "LMS111")).Findings[0];

        var explanation = await _tools.ExplainFinding("LegacyShop", target.Fingerprint);

        Assert.Equal(target, explanation.Finding);
        Assert.Equal("LMS111", explanation.Rule.Id);
        Assert.False(string.IsNullOrEmpty(explanation.Recommendation));
        Assert.True(explanation.EffortPoints > 0);
        Assert.Contains("points", explanation.EffortModel, StringComparison.Ordinal);
        Assert.Equal(target.Project, explanation.Project?.Name);
        await Assert.ThrowsAsync<McpException>(() => _tools.ExplainFinding("LegacyShop", "0000"));
    }

    [Fact]
    public async Task GetPackages_HidesCompatibleByDefault()
    {
        var actionable = await _tools.GetPackages("LegacyShop");
        var everything = await _tools.GetPackages("LegacyShop", includeCompatible: true);

        Assert.DoesNotContain(actionable.Packages, p => p.Status == PackageStatus.Compatible);
        Assert.Equal(everything.Total, everything.Packages.Count);
        Assert.Equal(everything.Total - everything.Compatible, actionable.Packages.Count);
    }

    [Fact]
    public async Task PathsOutsideRoots_AndMissingSolutions_AreToolErrors()
    {
        var outside = await Assert.ThrowsAsync<McpException>(() => _tools.ScanSolution(TestHelpers.RepoRoot));
        var missing = await Assert.ThrowsAsync<McpException>(() => _tools.ScanSolution("NoSuchSolution"));

        Assert.Contains("outside", outside.Message, StringComparison.Ordinal);
        Assert.Contains("Scan failed", missing.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void PlanMigrationPrompt_ReferencesToolsAndPath()
    {
        var prompt = MigrationPrompts.PlanMigration("src/App.sln", target: "net10.0", audience: "management");

        Assert.Contains("`src/App.sln`", prompt, StringComparison.Ordinal);
        foreach (var tool in new[] { "scan_solution", "get_findings", "explain_finding", "get_packages" })
        {
            Assert.Contains(tool, prompt, StringComparison.Ordinal);
        }

        Assert.Contains("managers", prompt, StringComparison.Ordinal);
    }
}
