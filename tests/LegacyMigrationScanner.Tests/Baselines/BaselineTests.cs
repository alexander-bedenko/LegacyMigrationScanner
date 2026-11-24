using System.Text.Json.Nodes;
using LegacyMigrationScanner.Core;
using LegacyMigrationScanner.Core.Baselines;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Core.Reporting;
using LegacyMigrationScanner.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyMigrationScanner.Tests.Baselines;

public sealed class BaselineTests : IDisposable
{
    private readonly string _dir = TestHelpers.CreateTempDirectory();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    internal static async Task<ScanResult> ScanFixtureAsync()
    {
        await using var services = TestHelpers.BuildServices();
        return await services.GetRequiredService<IMigrationScanner>().ScanAsync(TestHelpers.FixtureSolution);
    }

    /// <summary>Writes <paramref name="result"/> as a JSON report, optionally edited, and returns its path.</summary>
    internal static string WriteReport(ScanResult result, string directory, Action<JsonObject>? edit = null)
    {
        using var writer = new StringWriter();
        new JsonReportWriter().Write(result, writer);
        var root = JsonNode.Parse(writer.ToString())!.AsObject();
        edit?.Invoke(root);
        var path = Path.Combine(directory, $"{Guid.NewGuid():N}.json");
        File.WriteAllText(path, root.ToJsonString());
        return path;
    }

    [Fact]
    public async Task Scanner_AssignsFingerprints_MatchingSarif()
    {
        var result = await ScanFixtureAsync();

        Assert.All(result.Findings, f => Assert.Matches("^[0-9a-f]{32}$", f.Fingerprint));
        var web = result.Findings.First(f => f.File is not null);
        Assert.Equal(FindingFingerprint.Compute(web.RuleId, web.Project, web.File!, web.Detail), web.Fingerprint);
    }

    [Fact]
    public async Task SameReport_HasNoNewOrFixedFindings()
    {
        var result = await ScanFixtureAsync();
        var baseline = Baseline.Load(WriteReport(result, _dir));

        var diff = BaselineComparison.Compare(result, baseline, "base.json");

        Assert.Empty(diff.New);
        Assert.Empty(diff.Fixed);
        Assert.Equal(result.Findings.Count, diff.Unchanged);
        Assert.Equal(result.Findings.Count, diff.BaselineFindingCount);
    }

    [Fact]
    public async Task ReportWithoutStoredFingerprints_IsFingerprintedOnLoad()
    {
        // 0.2.0 reports carry no "fingerprint"; the diff must still match every finding.
        var result = await ScanFixtureAsync();
        var path = WriteReport(result, _dir, root =>
        {
            foreach (var finding in root["findings"]!.AsArray())
            {
                finding!.AsObject().Remove("fingerprint");
            }
        });

        var diff = BaselineComparison.Compare(result, Baseline.Load(path), path);

        Assert.Empty(diff.New);
        Assert.Empty(diff.Fixed);
    }

    [Fact]
    public async Task FindingMissingFromBaseline_IsNew_AndExtraBaselineFinding_IsFixed()
    {
        var result = await ScanFixtureAsync();
        var removed = result.Findings.First(f => f.Severity == Severity.Critical);
        var path = WriteReport(result, _dir, root =>
        {
            var findings = root["findings"]!.AsArray();
            findings.Remove(findings.First(f => (string?)f!["fingerprint"] == removed.Fingerprint));
            findings.Add(new JsonObject
            {
                ["ruleId"] = "LMS101",
                ["title"] = "Gone",
                ["severity"] = "Warning",
                ["project"] = "LegacyShop.Web",
                ["file"] = "LegacyShop.Web/Deleted.cs",
            });
        });

        var diff = BaselineComparison.Compare(result, Baseline.Load(path), path);

        var added = Assert.Single(diff.New);
        Assert.Same(removed, added);
        Assert.True(diff.IsNew(removed));
        var gone = Assert.Single(diff.Fixed);
        Assert.Equal("LegacyShop.Web/Deleted.cs", gone.File);
        Assert.Equal(result.Findings.Count - 1, diff.Unchanged);
    }

    [Fact]
    public async Task LineAndCountChanges_DoNotMakeFindingsNew()
    {
        var result = await ScanFixtureAsync();
        var path = WriteReport(result, _dir, root =>
        {
            foreach (var finding in root["findings"]!.AsArray())
            {
                finding!["line"] = 9999;
                finding["count"] = 42;
                finding.AsObject().Remove("fingerprint");
            }
        });

        Assert.Empty(BaselineComparison.Compare(result, Baseline.Load(path), path).New);
    }

    [Fact]
    public void DuplicateFingerprints_AreComparedAsMultiset()
    {
        var finding = new Finding
        {
            RuleId = "LMS101",
            Title = "t",
            Category = FindingCategory.SystemWeb,
            Severity = Severity.Critical,
            Project = "P",
            File = "a.cs",
            Fingerprint = "f1",
        };
        var twin = finding with { Line = 2 };
        var result = new ScanResult
        {
            ToolVersion = "1",
            ScannedAt = DateTimeOffset.UnixEpoch,
            Solution = "s.sln",
            Projects = [],
            Findings = [finding, twin],
            Estimates = [],
            MigrationOrder = [],
        };
        var baseline = new Baseline
        {
            Solution = "s.sln",
            Findings = [new BaselineFinding { Fingerprint = "f1", RuleId = "LMS101", Title = "t", Severity = Severity.Critical, Project = "P" }],
        };

        var diff = BaselineComparison.Compare(result, baseline, "b.json");

        Assert.Equal(1, diff.Unchanged);
        Assert.Same(twin, Assert.Single(diff.New));
        Assert.False(diff.IsNew(finding));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"version":"2.1.0","runs":[]}""")]
    public void InvalidBaseline_Throws(string content)
    {
        var path = Path.Combine(_dir, "bad.json");
        File.WriteAllText(path, content);

        Assert.Throws<InvalidDataException>(() => Baseline.Load(path));
    }

    [Fact]
    public void MissingBaseline_ThrowsFileNotFound() =>
        Assert.Throws<FileNotFoundException>(() => Baseline.Load(Path.Combine(_dir, "missing.json")));
}
