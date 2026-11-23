using LegacyMigrationScanner.Core.Analyzers;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Core.Packages;
using LegacyMigrationScanner.Tests.Infrastructure;

namespace LegacyMigrationScanner.Tests.Analyzers;

public class PackageAnalyzerTests
{
    private static List<Finding> Analyze(params PackageCompatibility[] resolved)
    {
        var project = new ProjectInfo
        {
            Name = "P",
            FullPath = Path.Combine(Path.GetTempPath(), "P", "P.csproj"),
            Packages = [.. resolved.Select(r => new PackageInfo(r.Id, r.Version, PackageSource.PackagesConfig))],
        };
        var map = resolved.ToDictionary(r => new PackageIdentity(r.Id, r.Version).Key, StringComparer.Ordinal);
        var context = new AnalysisContext(Path.GetTempPath(), TestHelpers.Rules, map);
        return [.. new PackageAnalyzer().Analyze(project, context)];
    }

    private static PackageCompatibility P(string id, PackageStatus status, string? recommended = null) =>
        new() { Id = id, Version = "1.0.0", Status = status, Origin = "test", RecommendedVersion = recommended };

    [Theory]
    [InlineData(PackageStatus.UpgradeRequired, "LMS601")]
    [InlineData(PackageStatus.Replace, "LMS600")]
    [InlineData(PackageStatus.Remove, "LMS602")]
    [InlineData(PackageStatus.Unknown, "LMS603")]
    [InlineData(PackageStatus.Incompatible, "LMS604")]
    [InlineData(PackageStatus.Review, "LMS605")]
    [InlineData(PackageStatus.NotFound, "LMS606")]
    public void Status_MapsToRule(PackageStatus status, string expectedRule) =>
        Assert.Equal(expectedRule, Assert.Single(Analyze(P("A", status))).RuleId);

    [Fact]
    public void CompatiblePackages_ProduceNoFindings() =>
        Assert.Empty(Analyze(P("A", PackageStatus.Compatible)));

    [Fact]
    public void Upgrade_ShowsTargetVersion()
    {
        var finding = Assert.Single(Analyze(P("A", PackageStatus.UpgradeRequired, "3.2.0") with { ModernFrameworks = ["net8.0"] }));

        Assert.Equal("A 1.0.0 -> 3.2.0", finding.Detail);
        Assert.Contains("net8.0", finding.Recommendation);
    }

    [Fact]
    public void UnresolvedPackage_IsReportedAsUnknown()
    {
        var project = new ProjectInfo
        {
            Name = "P",
            FullPath = Path.Combine(Path.GetTempPath(), "P", "P.csproj"),
            Packages = [new PackageInfo("X", "1.0.0", PackageSource.PackageReference)],
        };

        var finding = Assert.Single(new PackageAnalyzer().Analyze(project, TestHelpers.Context(Path.GetTempPath())));

        Assert.Equal("LMS603", finding.RuleId);
    }
}
