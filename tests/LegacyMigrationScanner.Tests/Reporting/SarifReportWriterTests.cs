using System.Text.Json;
using LegacyMigrationScanner.Core;
using LegacyMigrationScanner.Core.Reporting;
using LegacyMigrationScanner.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyMigrationScanner.Tests.Reporting;

public class SarifReportWriterTests
{
    private static async Task<JsonElement> WriteAsync()
    {
        await using var services = TestHelpers.BuildServices();
        var result = await services.GetRequiredService<IMigrationScanner>().ScanAsync(TestHelpers.FixtureSolution);
        using var writer = new StringWriter();
        new SarifReportWriter(TestHelpers.RepoRoot).Write(result, writer);
        return JsonDocument.Parse(writer.ToString()).RootElement.Clone();
    }

    [Fact]
    public async Task Output_IsSarif210_WithRulesAndResults()
    {
        var root = await WriteAsync();

        Assert.Equal("2.1.0", root.GetProperty("version").GetString());
        var run = root.GetProperty("runs")[0];
        Assert.Equal("LegacyMigrationScanner", run.GetProperty("tool").GetProperty("driver").GetProperty("name").GetString());
        Assert.True(run.GetProperty("results").GetArrayLength() > 0);
    }

    [Fact]
    public async Task RuleIndex_PointsToMatchingRule()
    {
        var run = (await WriteAsync()).GetProperty("runs")[0];
        var rules = run.GetProperty("tool").GetProperty("driver").GetProperty("rules");

        foreach (var result in run.GetProperty("results").EnumerateArray())
        {
            var index = result.GetProperty("ruleIndex").GetInt32();
            Assert.Equal(result.GetProperty("ruleId").GetString(), rules[index].GetProperty("id").GetString());
        }
    }

    [Fact]
    public async Task Locations_AreRepoRelative_AndLevelsAreValid()
    {
        var run = (await WriteAsync()).GetProperty("runs")[0];

        foreach (var result in run.GetProperty("results").EnumerateArray())
        {
            var uri = result.GetProperty("locations")[0].GetProperty("physicalLocation").GetProperty("artifactLocation").GetProperty("uri").GetString()!;
            Assert.StartsWith("tests/Fixtures/LegacyShop/", uri);
            Assert.Contains(result.GetProperty("level").GetString(), new[] { "error", "warning", "note" });
            Assert.False(string.IsNullOrEmpty(result.GetProperty("partialFingerprints").GetProperty("lmsFindingHash/v1").GetString()));
        }
    }
}
