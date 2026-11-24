using LegacyMigrationScanner.Core;
using LegacyMigrationScanner.Core.Reporting;
using LegacyMigrationScanner.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyMigrationScanner.Tests.Reporting;

public class MarkdownReportWriterTests
{
    [Fact]
    public async Task ProjectLevelFindings_HaveNoEmptyCodeSpans()
    {
        await using var services = TestHelpers.BuildServices();
        var result = await services.GetRequiredService<IMigrationScanner>().ScanAsync(TestHelpers.FixtureSolution);
        Assert.Contains(result.Findings, f => f.File is null);

        using var writer = new StringWriter();
        new MarkdownReportWriter().Write(result, writer);

        Assert.DoesNotContain("``", writer.ToString(), StringComparison.Ordinal);
    }
}
