using LegacyMigrationScanner.Core.Packages;

namespace LegacyMigrationScanner.Tests.Packages;

public class PackageCatalogTests
{
    private static readonly PackageCatalog Catalog = PackageCatalog.LoadDefault();

    [Fact]
    public void ExactMatch_WinsOverPrefix()
    {
        Assert.Equal(PackageAction.Compatible, Catalog.Find("Microsoft.AspNet.WebApi.Client")!.Action);
        Assert.Equal(PackageAction.Replace, Catalog.Find("Microsoft.AspNet.WebApi.Core")!.Action);
    }

    [Fact]
    public void LongestPrefix_Wins()
    {
        Assert.Contains("authentication", Catalog.Find("Microsoft.Owin.Security.Cookies")!.Replacement);
        Assert.Contains("middleware", Catalog.Find("Microsoft.Owin.Host.SystemWeb")!.Replacement);
    }

    [Theory]
    [InlineData("6.2.0", PackageStatus.UpgradeRequired)]
    [InlineData("6.3.0", PackageStatus.Compatible)]
    [InlineData("6.5.1", PackageStatus.Compatible)]
    public void Upgrade_DependsOnVersion(string version, PackageStatus expected) =>
        Assert.Equal(expected, Catalog.Classify(new PackageIdentity("EntityFramework", version))!.Status);

    [Fact]
    public void UnknownPackage_HasNoOpinion() =>
        Assert.Null(Catalog.Classify(new PackageIdentity("Contoso.Internal", "1.0.0")));

    [Fact]
    public void Matching_IsCaseInsensitive() =>
        Assert.Equal(PackageStatus.Replace, Catalog.Classify(new PackageIdentity("microsoft.aspnet.mvc", "5.2.9"))!.Status);
}
