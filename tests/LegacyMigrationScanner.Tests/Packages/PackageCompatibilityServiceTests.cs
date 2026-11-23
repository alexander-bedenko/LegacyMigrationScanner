using System.Net;
using LegacyMigrationScanner.Core.Packages;
using LegacyMigrationScanner.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyMigrationScanner.Tests.Packages;

public class PackageCompatibilityServiceTests
{
    private static async Task<PackageCompatibility> ResolveAsync(FakeNuGetFeed feed, string id, string version, Action<Core.ScannerOptions>? configure = null)
    {
        await using var services = TestHelpers.BuildServices(o =>
        {
            o.NuGetSources.Add(FakeNuGetFeed.ServiceIndex);
            configure?.Invoke(o);
        }, feed, new ManualTimeProvider(DateTimeOffset.UnixEpoch));

        var identity = new PackageIdentity(id, version);
        var results = await services.GetRequiredService<IPackageCompatibilityService>().ResolveAsync([identity], CancellationToken.None);
        return results[identity.Key];
    }

    [Fact]
    public async Task CurrentVersionTargetingNetStandard_IsCompatible()
    {
        var feed = new FakeNuGetFeed().WithPackage("Contoso.Json", ("2.1.0", [".NETFramework4.5", ".NETStandard2.0"]));

        var result = await ResolveAsync(feed, "Contoso.Json", "2.1.0");

        Assert.Equal(PackageStatus.Compatible, result.Status);
        Assert.Equal("netstandard2.0", Assert.Single(result.ModernFrameworks));
        Assert.Equal(FakeNuGetFeed.ServiceIndex.AbsoluteUri, result.Origin);
    }

    [Fact]
    public async Task OnlyNewerVersionSupportsModern_IsUpgradeToLatestStable()
    {
        var feed = new FakeNuGetFeed().WithPackage("Contoso.Pdf",
            ("1.0.0", [".NETFramework4.0"]),
            ("3.2.0", [".NETFramework4.6.2", "net8.0"]),
            ("4.0.0-beta", ["net10.0"]));

        var result = await ResolveAsync(feed, "Contoso.Pdf", "1.0");

        Assert.Equal(PackageStatus.UpgradeRequired, result.Status);
        Assert.Equal("3.2.0", result.RecommendedVersion);
        Assert.Equal("net8.0", Assert.Single(result.ModernFrameworks));
    }

    [Fact]
    public async Task NoVersionSupportsModern_IsIncompatible()
    {
        var feed = new FakeNuGetFeed().WithPackage("Contoso.Legacy", ("1.0.0", [".NETFramework4.0"]), ("1.1.0", ["net45"]));

        var result = await ResolveAsync(feed, "Contoso.Legacy", "1.0.0");

        Assert.Equal(PackageStatus.Incompatible, result.Status);
        Assert.Contains("1.1.0", result.Reason);
    }

    [Fact]
    public async Task NuspecWithoutFrameworkGroups_IsUnknown()
    {
        var feed = new FakeNuGetFeed().WithPackage("Contoso.Old", ("1.0.0", []));

        var result = await ResolveAsync(feed, "Contoso.Old", "1.0.0");

        Assert.Equal(PackageStatus.Unknown, result.Status);
        Assert.Contains("no target frameworks", result.Reason);
    }

    [Fact]
    public async Task MissingPackage_IsNotFound()
    {
        var result = await ResolveAsync(new FakeNuGetFeed(), "Acme.Internal.Billing", "1.4.0");

        Assert.Equal(PackageStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task TransientFailures_AreRetried()
    {
        var feed = new FakeNuGetFeed()
            .WithPackage("Contoso.Json", ("2.1.0", [".NETStandard2.0"]))
            .FailFirst("contoso.json/index.json", 2);

        var result = await ResolveAsync(feed, "Contoso.Json", "2.1.0");

        Assert.Equal(PackageStatus.Compatible, result.Status);
        Assert.Equal(3, feed.Requests.Count(u => u.AbsoluteUri.EndsWith("contoso.json/index.json", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PersistentFailure_DegradesToUnknown_WithoutThrowing()
    {
        var feed = new FakeNuGetFeed().WithResponse(FakeNuGetFeed.ServiceIndex.AbsoluteUri, HttpStatusCode.InternalServerError);

        var result = await ResolveAsync(feed, "Contoso.Json", "2.1.0");

        Assert.Equal(PackageStatus.Unknown, result.Status);
        Assert.Contains("Lookup failed", result.Reason);
    }

    [Fact]
    public async Task CatalogPackages_AreNotLookedUpOnline()
    {
        var feed = new FakeNuGetFeed();

        var result = await ResolveAsync(feed, "Microsoft.AspNet.Mvc", "5.2.9");

        Assert.Equal(PackageStatus.Replace, result.Status);
        Assert.Empty(feed.Requests);
    }

    [Fact]
    public async Task OfflineMode_MakesNoRequests()
    {
        var feed = new FakeNuGetFeed().WithPackage("Contoso.Json", ("2.1.0", [".NETStandard2.0"]));

        var result = await ResolveAsync(feed, "Contoso.Json", "2.1.0", o => o.Offline = true);

        Assert.Equal(PackageStatus.Unknown, result.Status);
        Assert.Empty(feed.Requests);
    }

    [Fact]
    public async Task SecondSource_IsUsedWhenFirstDoesNotHavePackage()
    {
        var feed = new FakeNuGetFeed().WithPackage("Contoso.Json", ("2.1.0", [".NETStandard2.0"]));
        var emptyFeed = new Uri("https://empty.test/v3/index.json");
        feed.WithResponse(emptyFeed.AbsoluteUri, HttpStatusCode.OK,
            """{ "resources": [ { "@id": "https://empty.test/flat/", "@type": "PackageBaseAddress/3.0.0" } ] }""");

        await using var services = TestHelpers.BuildServices(o =>
        {
            o.NuGetSources.Add(emptyFeed);
            o.NuGetSources.Add(FakeNuGetFeed.ServiceIndex);
        }, feed);

        var identity = new PackageIdentity("Contoso.Json", "2.1.0");
        var results = await services.GetRequiredService<IPackageCompatibilityService>().ResolveAsync([identity], CancellationToken.None);

        Assert.Equal(PackageStatus.Compatible, results[identity.Key].Status);
    }
}
