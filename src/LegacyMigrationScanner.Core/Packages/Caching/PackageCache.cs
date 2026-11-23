using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LegacyMigrationScanner.Core.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyMigrationScanner.Core.Packages.Caching;

public interface IPackageCache
{
    Task LoadAsync(CancellationToken cancellationToken);
    bool TryGet(PackageIdentity package, out PackageCompatibility result);
    void Set(PackageIdentity package, PackageCompatibility result);
    Task SaveAsync(CancellationToken cancellationToken);
}

/// <summary>No-op cache (caching disabled).</summary>
public sealed class NullPackageCache : IPackageCache
{
    public Task LoadAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public bool TryGet(PackageIdentity package, out PackageCompatibility result)
    {
        result = null!;
        return false;
    }

    public void Set(PackageIdentity package, PackageCompatibility result)
    {
    }

    public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

/// <summary>
/// JSON file cache of online lookups. Entries are scoped to the configured feed list (a different feed
/// set gets a different file) and expire after <see cref="ScannerOptions.CacheTimeToLive"/>.
/// Writes are atomic (temp file + move), so concurrent scans cannot corrupt the cache.
/// </summary>
public sealed class FilePackageCache : IPackageCache
{
    private const int FormatVersion = 1;

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly ScannerOptions _options;
    private readonly TimeProvider _time;
    private readonly ILogger<FilePackageCache> _logger;
    private bool _dirty;

    public FilePackageCache(IOptions<ScannerOptions> options, TimeProvider time, ILogger<FilePackageCache> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _options = options.Value;
        _time = time;
        _logger = logger;
        FilePath = Path.Combine(_options.CacheDirectory ?? throw new InvalidOperationException("CacheDirectory is not set."),
            $"packages-{FeedFingerprint(_options.EffectiveNuGetSources)}.json");
    }

    public string FilePath { get; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(FilePath))
        {
            return;
        }

        try
        {
            await using var stream = File.OpenRead(FilePath);
            var file = await JsonSerializer.DeserializeAsync<CacheFile>(stream, JsonOptions, cancellationToken);
            if (file?.Version != FormatVersion)
            {
                return;
            }

            foreach (var (key, entry) in file.Entries)
            {
                _entries[key] = entry;
            }

            Log.CacheLoaded(_logger, _entries.Count, FilePath);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            Log.CacheUnreadable(_logger, FilePath, ex.Message);
        }
    }

    public bool TryGet(PackageIdentity package, out PackageCompatibility result)
    {
        if (_entries.TryGetValue(package.Key, out var entry))
        {
            var ttl = entry.Result.Status == PackageStatus.NotFound ? _options.NotFoundCacheTimeToLive : _options.CacheTimeToLive;
            if (_time.GetUtcNow() - entry.FetchedAt < ttl)
            {
                result = entry.Result with { Id = package.Id, Version = package.Version };
                return true;
            }
        }

        result = null!;
        return false;
    }

    public void Set(PackageIdentity package, PackageCompatibility result)
    {
        // Transient failures are not cached: the next scan should retry them.
        if (result.Status == PackageStatus.Unknown)
        {
            return;
        }

        _entries[package.Key] = new CacheEntry(_time.GetUtcNow(), result);
        _dirty = true;
    }

    public async Task SaveAsync(CancellationToken cancellationToken)
    {
        if (!_dirty)
        {
            return;
        }

        var tempPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var file = new CacheFile(FormatVersion, new SortedDictionary<string, CacheEntry>(_entries, StringComparer.Ordinal));
            await using (var stream = File.Create(tempPath))
            {
                await JsonSerializer.SerializeAsync(stream, file, JsonOptions, cancellationToken);
            }

            File.Move(tempPath, FilePath, overwrite: true);
            _dirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log.CacheSaveFailed(_logger, FilePath, ex.Message);
            TryDelete(tempPath);
        }
    }

    internal static string FeedFingerprint(IEnumerable<Uri> sources)
    {
        var joined = string.Join('\n', sources.Select(s => s.AbsoluteUri.ToUpperInvariant()));
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(joined)))[..12];
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    internal sealed record CacheEntry(DateTimeOffset FetchedAt, PackageCompatibility Result);

    private sealed record CacheFile(int Version, IDictionary<string, CacheEntry> Entries);
}
