using LegacyMigrationScanner.Core;
using LegacyMigrationScanner.Core.Analyzers;
using LegacyMigrationScanner.Core.Deep;
using LegacyMigrationScanner.Core.Loading;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyMigrationScanner.Tests.Deep;

public class DeepModeTests
{
    private const string Notifications = "LegacyShop.Data/OrderNotifications.cs";

    private static readonly Lazy<Task<ScanResult>> SyntaxScan = new(() => ScanAsync(deep: false));
    private static readonly Lazy<Task<ScanResult>> DeepScan = new(() => ScanAsync(deep: true));

    private static async Task<ScanResult> ScanAsync(bool deep, IReferenceAssemblyProvider? provider = null)
    {
        await using var services = TestHelpers.BuildServices(o => o.Deep = deep, referenceAssemblies: provider);
        return await services.GetRequiredService<IMigrationScanner>().ScanAsync(TestHelpers.FixtureSolution);
    }

    [Fact]
    public async Task DeepMode_FindsApisWithoutRules_AndDropsSystemWebFalsePositive()
    {
        var syntax = await SyntaxScan.Value;
        var deep = await DeepScan.Value;

        Assert.False(syntax.Deep);
        Assert.True(deep.Deep);

        // Syntax mode: `using System.Web;` for HttpUtility counts as a System.Web dependency, MSMQ goes unnoticed.
        Assert.Contains(syntax.Findings, f => f.File == Notifications && f.RuleId == "LMS110");
        Assert.DoesNotContain(syntax.Findings, f => f.RuleId == "LMS310");

        // Deep mode: HttpUtility exists on modern .NET; System.Messaging does not.
        Assert.DoesNotContain(deep.Findings, f => f.File == Notifications && f.RuleId == "LMS110");
        var msmq = Assert.Single(deep.Findings, f => f.File == Notifications && f.RuleId == "LMS310");
        Assert.Contains("System.Messaging.MessageQueue", msmq.Detail, StringComparison.Ordinal);
        Assert.Equal(Severity.Warning, msmq.Severity);
    }

    [Fact]
    public async Task DeepMode_ReportsSystemWebBySymbol()
    {
        var deep = await DeepScan.Value;

        var global = Assert.Single(deep.Findings, f => f.File == "LegacyShop.Web/Global.asax.cs" && f.RuleId == "LMS110");
        Assert.Contains("System.Web.HttpApplication", global.Detail, StringComparison.Ordinal);
        var page = Assert.Single(deep.Findings, f => f.File == "LegacyShop.Web/Default.aspx.cs" && f.RuleId == "LMS110");
        Assert.Contains("System.Web.UI.Page", page.Detail, StringComparison.Ordinal);
        Assert.All(deep.Findings, f => Assert.False(string.IsNullOrEmpty(f.Fingerprint)));
    }

    [Fact]
    public async Task DeepMode_KeepsOtherRules_AndWarnsAboutUnrestoredPackages()
    {
        var syntax = await SyntaxScan.Value;
        var deep = await DeepScan.Value;

        var syntaxRules = syntax.Findings.Select(f => f.RuleId).Where(r => r != "LMS110").ToHashSet();
        Assert.Subset(deep.Findings.Select(f => f.RuleId).ToHashSet(), syntaxRules);
        Assert.Contains(deep.Warnings, w => w.Contains("HintPath references not found", StringComparison.Ordinal));
        Assert.DoesNotContain(syntax.Warnings, w => w.Contains("Deep analysis", StringComparison.Ordinal));
    }

    [Fact]
    public async Task MissingReferenceAssemblies_FallBackToSyntax_WithWarning()
    {
        var result = await ScanAsync(deep: true, provider: new LocalReferenceAssemblyProvider(null));

        Assert.True(result.Deep);
        Assert.Contains(result.Warnings, w => w.Contains("reference assemblies are unavailable", StringComparison.Ordinal));
        Assert.Contains(result.Findings, f => f.File == Notifications && f.RuleId == "LMS110"); // syntax rule still applies
        Assert.DoesNotContain(result.Findings, f => f.RuleId == "LMS310");
    }

    [Fact]
    public void ModernApiCatalog_KnowsWhatSurvived()
    {
        var catalog = ModernApiCatalog.Current;

        Assert.True(catalog.TypeCount > 1000);
        Assert.True(catalog.ContainsType("System.String"));
        Assert.True(catalog.ContainsType("System.Web.HttpUtility"));
        Assert.False(catalog.ContainsType("System.Web.HttpContext"));
        Assert.False(catalog.ContainsType("System.Messaging.MessageQueue"));
    }

    [Theory]
    [InlineData("System.Web", "LMS110")]
    [InlineData("System.Web.UI.WebControls", "LMS110")]
    [InlineData("System.Messaging", "LMS310")]
    [InlineData("System.Workflow.Runtime", "LMS310")]
    [InlineData("System.WebX", "LMS310")]
    [InlineData("System.ServiceModel", null)]
    [InlineData("System.Configuration", null)]
    [InlineData("System.Drawing.Imaging", null)]
    public void Classify_AvoidsDoubleCounting(string ns, string? expected) =>
        Assert.Equal(expected, SemanticCodeAnalyzer.Classify(ns));

    [Theory]
    [InlineData("v4.8", "net48")]
    [InlineData("v4.7.2", "net472")]
    [InlineData("v4.0", "net40")]
    [InlineData("v3.5", "net35")]
    [InlineData("netstandard2.0", null)]
    [InlineData("net10.0", null)]
    public void TargetFrameworkVersion_MapsToReferencePackage(string version, string? moniker) =>
        Assert.Equal(moniker, NuGetReferenceAssemblyProvider.ToTargetFrameworkMoniker(version));

    [Theory]
    [InlineData(new[] { "v4.8" }, "v4.8")]
    [InlineData(new[] { "net472" }, "v4.7.2")]
    [InlineData(new[] { "net48", "net8.0" }, "v4.8")]
    [InlineData(new[] { "netstandard2.0" }, null)]
    [InlineData(new[] { "net8.0" }, null)]
    public void FrameworkVersion_HandlesLegacyAndSdkStyle(string[] targetFrameworks, string? expected) =>
        Assert.Equal(expected, DeepModelBuilder.FrameworkVersion(new ProjectInfo { Name = "P", FullPath = "/p/P.csproj", TargetFrameworks = targetFrameworks }));

    [Fact]
    public void Parser_CapturesHintPaths()
    {
        var web = ProjectParser.Parse(Path.Combine(TestHelpers.FixtureDir, "LegacyShop.Web", "LegacyShop.Web.csproj"));

        var webApi = Assert.Single(web.HintPathReferences, r => r.Name == "System.Web.Http");
        Assert.EndsWith(Path.Combine("packages", "Microsoft.AspNet.WebApi.Core.5.2.9", "lib", "net45", "System.Web.Http.dll"), webApi.FullPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("v4.8", web.TargetFrameworkVersion);
    }
}
