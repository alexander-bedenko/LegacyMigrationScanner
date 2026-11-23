using LegacyMigrationScanner.Core;
using LegacyMigrationScanner.Core.Packages;
using LegacyMigrationScanner.Core.Packages.Caching;
using LegacyMigrationScanner.Tests.Infrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace LegacyMigrationScanner.Tests.Packages;

public sealed class FilePackageCacheTests : IDisposable
{
    private readonly string _directory = TestHelpers.CreateTempDirectory();
    private readonly ManualTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private FilePackageCache Create() =>
        new(Options.Create(new ScannerOptions { CacheDirectory = _directory }), _time, NullLogger<FilePackageCache>.Instance);

    private static PackageCompatibility Result(PackageIdentity p, PackageStatus status) =>
        new() { Id = p.Id, Version = p.Version, Status = status, Origin = "test" };

    [Fact]
    public async Task Entries_SurviveSaveAndLoad()
    {
        var package = new PackageIdentity("Contoso.Json", "2.1.0");
        var writer = Create();
        writer.Set(package, Result(package, PackageStatus.Compatible));
        await writer.SaveAsync(CancellationToken.None);

        var reader = Create();
        await reader.LoadAsync(CancellationToken.None);

        Assert.True(reader.TryGet(new PackageIdentity("contoso.json", "2.1"), out var cached));
        Assert.Equal(PackageStatus.Compatible, cached.Status);
        Assert.Equal("contoso.json", cached.Id);
    }

    [Fact]
    public async Task Entries_ExpireAfterTtl_NotFoundExpiresSooner()
    {
        var found = new PackageIdentity("A", "1.0.0");
        var missing = new PackageIdentity("B", "1.0.0");
        var cache = Create();
        cache.Set(found, Result(found, PackageStatus.Compatible));
        cache.Set(missing, Result(missing, PackageStatus.NotFound));
        await cache.SaveAsync(CancellationToken.None);

        _time.Advance(TimeSpan.FromDays(2));

        Assert.True(cache.TryGet(found, out _));
        Assert.False(cache.TryGet(missing, out _));

        _time.Advance(TimeSpan.FromDays(6));
        Assert.False(cache.TryGet(found, out _));
    }

    [Fact]
    public void UnknownResults_AreNotCached()
    {
        var package = new PackageIdentity("A", "1.0.0");
        var cache = Create();

        cache.Set(package, Result(package, PackageStatus.Unknown));

        Assert.False(cache.TryGet(package, out _));
    }

    [Fact]
    public async Task CorruptFile_IsIgnored()
    {
        var cache = Create();
        await File.WriteAllTextAsync(cache.FilePath, "{ not json");

        await cache.LoadAsync(CancellationToken.None);

        Assert.False(cache.TryGet(new PackageIdentity("A", "1.0.0"), out _));
    }

    [Fact]
    public void DifferentFeedLists_UseDifferentFiles()
    {
        var a = FilePackageCache.FeedFingerprint([new Uri("https://a.test/index.json")]);
        var b = FilePackageCache.FeedFingerprint([new Uri("https://b.test/index.json")]);

        Assert.NotEqual(a, b);
        Assert.Equal(a, FilePackageCache.FeedFingerprint([new Uri("HTTPS://A.TEST/index.json")]));
    }
}
