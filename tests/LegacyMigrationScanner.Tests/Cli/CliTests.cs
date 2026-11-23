using LegacyMigrationScanner.Cli;
using LegacyMigrationScanner.Core.Model;
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
            "--no-cache", "-v", "detailed", "--sarif-root", ".",
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
    }

    [Theory]
    [InlineData("scan")]
    [InlineData("scan a.sln --format pdf")]
    [InlineData("scan a.sln --fail-on fatal")]
    [InlineData("scan a.sln --hours-per-point -1")]
    [InlineData("scan a.sln --nuget-source not-a-url")]
    [InlineData("scan a.sln --unknown")]
    [InlineData("deploy")]
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
        var (code, stdout, _) = await RunAsync("scan", TestHelpers.FixtureDir, "-o", _output, "-f", "json,md,sarif", "--offline");

        Assert.Equal(0, code);
        foreach (var ext in new[] { "json", "md", "sarif" })
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
    public async Task MissingSolution_ReturnsScanFailed()
    {
        var (code, _, stderr) = await RunAsync("scan", Path.Combine(_output, "missing.sln"), "-o", _output, "--offline");

        Assert.Equal(1, code);
        Assert.Contains("not found", stderr);
    }
}
