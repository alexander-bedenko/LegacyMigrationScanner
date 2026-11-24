using System.Text.RegularExpressions;
using LegacyMigrationScanner.Core;
using LegacyMigrationScanner.Core.Baselines;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Core.Reporting;
using LegacyMigrationScanner.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyMigrationScanner.Tests.Reporting;

public partial class HtmlReportWriterTests
{
    private static string Render(ScanResult result)
    {
        using var writer = new StringWriter();
        new HtmlReportWriter().Write(result, writer);
        return writer.ToString();
    }

    private static async Task<ScanResult> ScanAsync()
    {
        await using var services = TestHelpers.BuildServices();
        return await services.GetRequiredService<IMigrationScanner>().ScanAsync(TestHelpers.FixtureSolution);
    }

    [Fact]
    public async Task Fixture_RendersAllSections_AndEveryFinding()
    {
        var result = await ScanAsync();

        var html = Render(result);

        Assert.StartsWith("<!doctype html>", html, StringComparison.Ordinal);
        Assert.EndsWith("</html>" + Environment.NewLine, html, StringComparison.Ordinal);
        foreach (var heading in new[] { "Projects", "Migration order", "Issues by rule", "Findings", "Packages" })
        {
            Assert.Contains($"<h2>{heading}</h2>", html, StringComparison.Ordinal);
        }

        var findingsTable = html[html.IndexOf("<table id=\"findings\">", StringComparison.Ordinal)..];
        findingsTable = findingsTable[..findingsTable.IndexOf("</table>", StringComparison.Ordinal)];
        Assert.Equal(result.Findings.Count, FindingRow().Count(findingsTable));
        Assert.DoesNotContain("Changes since baseline", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Output_IsSelfContained()
    {
        var html = Render(await ScanAsync());

        Assert.DoesNotMatch(@"<(link|img|iframe)\b", html);
        Assert.DoesNotMatch(@"\b(src|href)\s*=", html);
        Assert.DoesNotContain("@import", html, StringComparison.Ordinal);
    }

    [Fact]
    public void UserControlledText_IsEncoded()
    {
        var finding = new Finding
        {
            RuleId = "LMS110",
            Title = "System.Web",
            Category = FindingCategory.SystemWeb,
            Severity = Severity.Warning,
            Project = "<b>App</b>",
            File = "App/a\"b.cs",
            Detail = "<script>alert(1)</script>",
        };
        var result = new ScanResult
        {
            ToolVersion = "1.0.0",
            ScannedAt = DateTimeOffset.UnixEpoch,
            Solution = "A&B.sln",
            Projects = [],
            Findings = [finding],
            Estimates = [],
            MigrationOrder = [["<b>App</b>"]],
        };

        var html = Render(result);

        Assert.DoesNotContain("<script>alert", html, StringComparison.Ordinal);
        Assert.DoesNotContain("<b>App</b>", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;alert(1)&lt;/script&gt;", html, StringComparison.Ordinal);
        Assert.Contains("A&amp;B.sln", html, StringComparison.Ordinal);
        Assert.Contains("App/a&quot;b.cs", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Baseline_RendersChangesSection()
    {
        var result = await ScanAsync();
        var baseline = new Baseline
        {
            Solution = result.Solution,
            Findings = [new BaselineFinding { Fingerprint = "gone", RuleId = "LMS101", Title = "Global.asax", Severity = Severity.Warning, Project = "LegacyShop.Web" }],
        };
        result = result with { Baseline = BaselineComparison.Compare(result, baseline, "base.json") };

        var html = Render(result);

        Assert.Contains("Changes since baseline", html, StringComparison.Ordinal);
        Assert.Contains($"<strong>{result.Findings.Count} new</strong>, 1 fixed", html, StringComparison.Ordinal);
        Assert.Contains("Since baseline", html, StringComparison.Ordinal);
    }

    [GeneratedRegex("<tr data-sev=")]
    private static partial Regex FindingRow();
}
