using System.IO.Compression;
using LegacyMigrationScanner.Core.Deep;
using LegacyMigrationScanner.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyMigrationScanner.Tests.Deep;

public sealed class ReferenceAssemblyProviderTests : IDisposable
{
    private const string PackageId = "Microsoft.NETFramework.ReferenceAssemblies.net48";

    private readonly string _cache = TestHelpers.CreateTempDirectory();

    public void Dispose() => Directory.Delete(_cache, recursive: true);

    private static byte[] Nupkg(params (string Name, byte[] Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private static byte[] RealMscorlib() => File.ReadAllBytes(Path.Combine(TestHelpers.Net48ReferenceAssemblies, "mscorlib.dll"));

    private NuGetReferenceAssemblyProvider Provider(FakeNuGetFeed feed, bool offline = false)
    {
        var services = TestHelpers.BuildServices(o =>
        {
            o.Offline = offline;
            o.NuGetSources.Add(FakeNuGetFeed.ServiceIndex);
            o.ReferenceAssembliesDirectory = _cache;
        }, handler: feed);
        return ActivatorUtilities.CreateInstance<NuGetReferenceAssemblyProvider>(services);
    }

    [Fact]
    public async Task DownloadsOnce_ExtractsOnlyDlls_ThenServesFromCache()
    {
        var feed = new FakeNuGetFeed().WithNupkg(PackageId, NuGetReferenceAssemblyProvider.PackageVersion, Nupkg(
            ("build/.NETFramework/v4.8/mscorlib.dll", RealMscorlib()),
            ("build/.NETFramework/v4.8/mscorlib.xml", "<doc/>"u8.ToArray()),
            ("build/.NETFramework/v4.8/Facades/netstandard.dll", RealMscorlib())));

        var directory = await Provider(feed).GetDirectoryAsync("v4.8", CancellationToken.None);

        Assert.NotNull(directory);
        Assert.True(File.Exists(Path.Combine(directory, "mscorlib.dll")));
        Assert.True(File.Exists(Path.Combine(directory, "Facades", "netstandard.dll")));
        Assert.False(File.Exists(Path.Combine(directory, "mscorlib.xml")));
        Assert.StartsWith(_cache, directory, StringComparison.Ordinal);

        // A new provider (another process) finds the completed cache and does not hit the feed.
        var requestsBefore = feed.Requests.Count;
        Assert.Equal(directory, await Provider(feed).GetDirectoryAsync("v4.8", CancellationToken.None));
        Assert.Equal(requestsBefore, feed.Requests.Count);
    }

    [Fact]
    public async Task Offline_WithoutCache_ReturnsNull_WithoutRequests()
    {
        var feed = new FakeNuGetFeed();

        Assert.Null(await Provider(feed, offline: true).GetDirectoryAsync("v4.8", CancellationToken.None));
        Assert.Empty(feed.Requests);
    }

    [Fact]
    public async Task PackageMissingOnFeed_ReturnsNull()
    {
        Assert.Null(await Provider(new FakeNuGetFeed()).GetDirectoryAsync("v4.7.2", CancellationToken.None));
    }

    [Fact]
    public async Task NonFrameworkVersion_ReturnsNull() =>
        Assert.Null(await Provider(new FakeNuGetFeed()).GetDirectoryAsync("netstandard2.0", CancellationToken.None));

    [Fact]
    public void Extraction_RejectsPathsEscapingTheDestination()
    {
        var nupkg = Path.Combine(_cache, "evil.nupkg");
        File.WriteAllBytes(nupkg, Nupkg(("build/.NETFramework/../../../evil.dll", [1, 2, 3])));

        Assert.Throws<InvalidDataException>(() => NuGetReferenceAssemblyProvider.ExtractAssemblies(nupkg, Path.Combine(_cache, "out")));
    }
}
