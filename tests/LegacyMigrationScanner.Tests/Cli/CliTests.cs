using LegacyMigrationScanner.Cli;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Tests.Baselines;
using LegacyMigrationScanner.Tests.Calibration;
using LegacyMigrationScanner.Tests.Infrastructure;
using Microsoft.Extensions.Logging;

namespace LegacyMigrationScanner.Tests.Cli;

public sealed class CliTests : IDisposable
{
    private readonly string _output = TestHelpers.CreateTempDirectory();

    public void Dispose() => Directory.Delete(_output, recursive: true);

    private static async Task<(int ExitCode, string Stdout, string Stderr)> RunAsync(params string[] args)
    {
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        var code = await CliApplication.RunAsync(args, stdout, stderr, CancellationToken.None);
        return (code, stdout.ToString(), stderr.ToString());
    }

    [Fact]
    public void Parse_ReadsAllOptions()
    {
        var o = CliOptions.Parse([
            "scan", "app.sln", "-o", "out", "-f", "json,SARIF", "--fail-on", "warning", "--hours-per-point", "1.5",
            "--offline", "--nuget-source", "https://a.test/v3/index.json", "--nuget-source", "https://b.test/index.json",
            "--no-cache", "-v", "detailed", "--sarif-root", ".", "--baseline", "base.json", "--fail-on-new",
        ]);

        Assert.Equal("app.sln", o.Path);
        Assert.Equal("out", o.OutputDirectory);
        Assert.Equal("json, sarif", TestHelpers.Join(o.Formats));
        Assert.Equal(Severity.Warning, o.FailOn);
        Assert.Equal(1.5, o.HoursPerPoint);
        Assert.True(o.Offline);
        Assert.Equal(2, o.NuGetSources.Count);
        Assert.True(o.NoCache);
        Assert.Equal(LogLevel.Debug, o.LogLevel);
        Assert.Equal(".", o.SarifBaseDirectory);
        Assert.Equal("base.json", o.BaselinePath);
        Assert.True(o.FailOnNew);
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("scan a.sln --format pdf")]
    [InlineData("scan a.sln --fail-on fatal")]
    [InlineData("scan a.sln --hours-per-point -1")]
    [InlineData("scan a.sln --nuget-source not-a-url")]
    [InlineData("scan a.sln --unknown")]
    [InlineData("scan a.sln --fail-on-new")]
    [InlineData("scan a.sln --baseline")]
    [InlineData("deploy")]
    [InlineData("mcp --format json")]
    [InlineData("mcp --root")]
    [InlineData("calibrate")]
    [InlineData("calibrate r.json")]
    [InlineData("calibrate r.json --actuals a.csv --actual-hours 10")]
    [InlineData("calibrate r.json --actual-hours 0")]
    [InlineData("calibrate r.json --actual-hours 10 --format json")]
    public async Task InvalidArguments_Return64(string commandLine)
    {
        var (code, _, stderr) = await RunAsync(commandLine.Split(' '));

        Assert.Equal(64, code);
        Assert.Contains("error:", stderr);
    }

    [Fact]
    public async Task Help_And_Version_Succeed()
    {
        Assert.Equal(0, (await RunAsync("--help")).ExitCode);
        var version = await RunAsync("--version");
        Assert.Equal(0, version.ExitCode);
        Assert.Matches(@"^\d+\.\d+\.\d+", version.Stdout);
    }

    [Fact]
    public async Task Scan_WritesAllRequestedReports()
    {
        var (code, stdout, _) = await RunAsync("scan", TestHelpers.FixtureDir, "-o", _output, "-f", "json,md,sarif,html", "--offline");

        Assert.Equal(0, code);
        foreach (var ext in new[] { "json", "md", "sarif", "html" })
        {
            Assert.True(File.Exists(Path.Combine(_output, $"LegacyShop.migration-report.{ext}")), ext);
        }

        Assert.Contains("Estimated effort", stdout);
    }

    [Fact]
    public async Task FailOn_ReturnsThresholdExitCode()
    {
        var (code, _, _) = await RunAsync("scan", TestHelpers.FixtureSolution, "-o", _output, "-f", "json", "--offline", "--fail-on", "critical");

        Assert.Equal(2, code);
    }

    [Fact]
    public void Parse_Mcp_ReadsRootsAndScannerOptions()
    {
        var o = CliOptions.Parse(["mcp", "--root", "a", "--root", "b", "--offline", "--hours-per-point", "3", "-v", "normal"]);

        Assert.Equal(CliCommand.Mcp, o.Command);
        Assert.Equal("a, b", TestHelpers.Join(o.McpRoots));
        Assert.True(o.Offline);
        Assert.Equal(3, o.HoursPerPoint);
        Assert.Equal(LogLevel.Information, o.LogLevel);
    }

    [Fact]
    public async Task Mcp_MissingRoot_ReturnsInvalidArguments()
    {
        var (code, _, stderr) = await RunAsync("mcp", "--root", Path.Combine(_output, "nope"));

        Assert.Equal(64, code);
        Assert.Contains("--root directory not found", stderr);
    }

    [Fact]
    public void Parse_Calibrate()
    {
        var o = CliOptions.Parse(["calibrate", "a.json", "b.json", "--actuals", "hours.csv", "-o", "cal.json"]);

        Assert.Equal(CliCommand.Calibrate, o.Command);
        Assert.Equal("a.json, b.json", TestHelpers.Join(o.Reports));
        Assert.Equal("hours.csv", o.ActualsPath);
        Assert.Null(o.ActualHours);
        Assert.Equal("cal.json", o.CalibrationOutput);
    }

    [Fact]
    public async Task Calibrate_PrintsHoursPerPoint_AndWritesJson()
    {
        var report = await CalibrationTests.WriteFixtureReportAsync(_output);
        var actuals = Path.Combine(_output, "actuals.csv");
        await File.WriteAllLinesAsync(actuals, ["project,hours", "LegacyShop.Web,150", "LegacyShop.Data,60", "LegacyShop.Services,80"]);
        var json = Path.Combine(_output, "out", "calibration.json");

        var (code, stdout, _) = await RunAsync("calibrate", report, "--actuals", actuals, "-o", json);

        Assert.Equal(0, code);
        Assert.Contains("Calibrated hours per point:", stdout);
        Assert.Contains("--hours-per-point", stdout);
        using var doc = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(json));
        Assert.True(doc.RootElement.GetProperty("hoursPerPoint").GetDouble() > 0);
        Assert.Equal(3, doc.RootElement.GetProperty("projects").GetArrayLength());
    }

    [Fact]
    public async Task Calibrate_MissingReport_ReturnsScanFailed()
    {
        var (code, _, stderr) = await RunAsync("calibrate", Path.Combine(_output, "none.json"), "--actual-hours", "100");

        Assert.Equal(1, code);
        Assert.Contains("Report not found", stderr);
    }

    [Fact]
    public async Task FailOnNew_WithIdenticalBaseline_Succeeds_EvenWithCriticalFindings()
    {
        var baseline = BaselineTests.WriteReport(await BaselineTests.ScanFixtureAsync(), _output);

        var (code, stdout, _) = await RunAsync(
            "scan", TestHelpers.FixtureSolution, "-o", _output, "-f", "json", "--offline", "--baseline", baseline, "--fail-on-new");

        Assert.Equal(0, code);
        Assert.Contains("Baseline: 0 new", stdout);
    }

    [Fact]
    public async Task FailOnNew_WithNewFinding_ReturnsThresholdExitCode_AndMarksSarif()
    {
        var result = await BaselineTests.ScanFixtureAsync();
        var warning = result.Findings.First(f => f.Severity == Severity.Warning);
        var baseline = BaselineTests.WriteReport(result, _output, root =>
        {
            var findings = root["findings"]!.AsArray();
            findings.Remove(findings.First(f => (string?)f!["fingerprint"] == warning.Fingerprint));
        });

        var failing = await RunAsync(
            "scan", TestHelpers.FixtureSolution, "-o", _output, "-f", "sarif,md", "--offline", "--baseline", baseline, "--fail-on-new");
        // The only new finding is a warning, so a critical-only gate passes.
        var critical = await RunAsync(
            "scan", TestHelpers.FixtureSolution, "-o", _output, "-f", "json", "--offline", "--baseline", baseline, "--fail-on-new", "--fail-on", "critical");

        Assert.Equal(2, failing.ExitCode);
        Assert.Contains("Baseline: 1 new", failing.Stdout);
        Assert.Equal(0, critical.ExitCode);

        var sarif = File.ReadAllText(Path.Combine(_output, "LegacyShop.migration-report.sarif"));
        Assert.Single(System.Text.RegularExpressions.Regex.Matches(sarif, "\"baselineState\": \"new\""));
        var markdown = File.ReadAllText(Path.Combine(_output, "LegacyShop.migration-report.md"));
        Assert.Contains("## Changes since baseline", markdown);
        Assert.Contains(warning.RuleId, markdown);
    }

    [Fact]
    public async Task MissingBaseline_ReturnsScanFailed()
    {
        var (code, _, stderr) = await RunAsync(
            "scan", TestHelpers.FixtureSolution, "-o", _output, "--offline", "--baseline", Path.Combine(_output, "none.json"));

        Assert.Equal(1, code);
        Assert.Contains("Baseline report not found", stderr);
    }

    [Fact]
    public async Task MissingSolution_ReturnsScanFailed()
    {
        var (code, _, stderr) = await RunAsync("scan", Path.Combine(_output, "missing.sln"), "-o", _output, "--offline");

        Assert.Equal(1, code);
        Assert.Contains("not found", stderr);
    }
}
