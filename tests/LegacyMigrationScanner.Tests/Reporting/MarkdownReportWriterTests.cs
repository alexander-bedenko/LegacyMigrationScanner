using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Core.Reporting;

namespace LegacyMigrationScanner.Tests.Reporting;

public class MarkdownReportWriterTests
{
    [Fact]
    public void FindingWithoutFile_HasNoEmptyCodeSpan()
    {
        var result = new ScanResult
        {
            ToolVersion = "1.0.0",
            ScannedAt = DateTimeOffset.UnixEpoch,
            Solution = "App.sln",
            Projects = [],
            Findings =
            [
                new Finding
                {
                    RuleId = "LMS003", Title = "Targets .NET Framework", Category = FindingCategory.ProjectFormat,
                    Severity = Severity.Info, Project = "App", File = null,
                },
                new Finding
                {
                    RuleId = "LMS101", Title = "Global.asax", Category = FindingCategory.SystemWeb,
                    Severity = Severity.Warning, Project = "App", File = "App/Global.asax", Line = 3,
                },
            ],
            Estimates = [],
            MigrationOrder = [],
        };

        using var writer = new StringWriter();
        new MarkdownReportWriter().Write(result, writer);
        var markdown = writer.ToString();

        Assert.DoesNotContain("``", markdown, StringComparison.Ordinal);
        Assert.Contains("`App/Global.asax:3`", markdown, StringComparison.Ordinal);
    }
}
