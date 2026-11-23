using System.Collections.Concurrent;
using LegacyMigrationScanner.Core.Diagnostics;
using LegacyMigrationScanner.Core.Packages.Caching;
using LegacyMigrationScanner.Core.Packages.NuGet;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyMigrationScanner.Core.Packages;

public interface IPackageCompatibilityService
{
    /// <summary>Classifies every distinct package. Never throws for individual lookup failures (they become Unknown).</summary>
    Task<IReadOnlyDictionary<string, PackageCompatibility>> ResolveAsync(
        IReadOnlyCollection<PackageIdentity> packages,
        CancellationToken cancellationToken);
}

/// <summary>Creates feed clients; abstracted so tests can plug in recorded HTTP responses.</summary>
public interface INuGetFeedClientFactory
{
    NuGetFeedClient Create(Uri serviceIndex);
}

/// <summary>
/// Resolution order: curated catalog -> cache -> NuGet feeds (in configured order).
/// Results are keyed by <see cref="PackageIdentity.Key"/>.
/// </summary>
public sealed class PackageCompatibilityService : IPackageCompatibilityService
{
    private readonly PackageCatalog _catalog;
    private readonly IPackageCache _cache;
    private readonly INuGetFeedClientFactory _clients;
    private readonly ScannerOptions _options;
    private readonly ILogger<PackageCompatibilityService> _logger;

    public PackageCompatibilityService(
        PackageCatalog catalog,
        IPackageCache cache,
        INuGetFeedClientFactory clients,
        IOptions<ScannerOptions> options,
        ILogger<PackageCompatibilityService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _catalog = catalog;
        _cache = cache;
        _clients = clients;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IReadOnlyDictionary<string, PackageCompatibility>> ResolveAsync(
        IReadOnlyCollection<PackageIdentity> packages,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var results = new ConcurrentDictionary<string, PackageCompatibility>(StringComparer.Ordinal);
        var pending = new List<PackageIdentity>();

        foreach (var package in packages.DistinctBy(p => p.Key))
        {
            if (_catalog.Classify(package) is { } known)
            {
                results[package.Key] = known;
            }
            else
            {
                pending.Add(package);
            }
        }

        if (pending.Count == 0)
        {
            return results;
        }

        if (_options.Offline)
        {
            Log.OfflineUnknownPackages(_logger, pending.Count);
            foreach (var package in pending)
            {
                results[package.Key] = Unknown(package, "offline", "Offline mode: not in the package catalog");
            }

            return results;
        }

        await _cache.LoadAsync(cancellationToken);
        var sources = _options.EffectiveNuGetSources.Select(_clients.Create).ToList();
        Log.ResolvingPackages(_logger, pending.Count, sources.Count);

        await Parallel.ForEachAsync(
            pending,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, _options.MaxConcurrentLookups), CancellationToken = cancellationToken },
            async (package, ct) =>
            {
                if (!_cache.TryGet(package, out var result))
                {
                    result = await ResolveOnlineAsync(package, sources, ct);
                    _cache.Set(package, result);
                }

                results[package.Key] = result;
                Log.PackageResolved(_logger, package.Id, package.Version, result.Status.ToString(), result.Origin);
            });

        await _cache.SaveAsync(cancellationToken);
        return results;
    }

    private async Task<PackageCompatibility> ResolveOnlineAsync(
        PackageIdentity package,
        IReadOnlyList<NuGetFeedClient> sources,
        CancellationToken cancellationToken)
    {
        var failures = new List<string>();
        foreach (var source in sources)
        {
            try
            {
                var result = await EvaluateAsync(package, source, cancellationToken);
                if (result is not null)
                {
                    return result;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidDataException or System.Text.Json.JsonException
                                           or KeyNotFoundException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                Log.PackageLookupFailed(_logger, package.Id, package.Version, source.ServiceIndex, ex.Message);
                failures.Add($"{source.ServiceIndex.Host}: {ex.Message}");
            }
        }

        return failures.Count > 0
            ? Unknown(package, "error", "Lookup failed: " + string.Join("; ", failures))
            : new PackageCompatibility
            {
                Id = package.Id,
                Version = package.Version,
                Status = PackageStatus.NotFound,
                Origin = string.Join(", ", sources.Select(s => s.ServiceIndex.Host)),
                Reason = "Not found on configured NuGet sources",
            };
    }

    /// <summary>Returns null when the package does not exist on this feed.</summary>
    internal static async Task<PackageCompatibility?> EvaluateAsync(PackageIdentity package, NuGetFeedClient source, CancellationToken cancellationToken)
    {
        var versions = await source.GetVersionsAsync(package.Id, cancellationToken);
        if (versions is null || versions.Count == 0)
        {
            return null;
        }

        var origin = source.ServiceIndex.AbsoluteUri;
        var current = versions.FirstOrDefault(v => NuGetVersion.Normalize(v) == NuGetVersion.Normalize(package.Version));
        var currentFrameworks = current is null ? null : await source.GetFrameworksAsync(package.Id, current, cancellationToken);
        if (currentFrameworks?.SupportsModern == true)
        {
            return Result(PackageStatus.Compatible, currentFrameworks);
        }

        var stable = versions.Where(v => !NuGetVersion.IsPrerelease(v)).ToList();
        var latest = (stable.Count > 0 ? stable : versions).Max(NuGetVersion.Comparer)!;
        var latestFrameworks = current is not null && NuGetVersion.Compare(latest, current) == 0
            ? currentFrameworks
            : await source.GetFrameworksAsync(package.Id, latest, cancellationToken);

        if (latestFrameworks?.SupportsModern == true)
        {
            return Result(PackageStatus.UpgradeRequired, latestFrameworks) with { RecommendedVersion = latest };
        }

        if (latestFrameworks is { HasFrameworkInfo: false } && currentFrameworks is null or { HasFrameworkInfo: false })
        {
            return Result(PackageStatus.Unknown, latestFrameworks) with
            {
                Reason = "Package metadata declares no target frameworks; inspect the package's lib/ folders manually",
            };
        }

        return Result(PackageStatus.Incompatible, latestFrameworks) with
        {
            Reason = $"Latest version {latest} targets only: {string.Join(", ", latestFrameworks?.TargetFrameworks ?? [])}",
        };

        PackageCompatibility Result(PackageStatus status, NuspecFrameworks? frameworks) => new()
        {
            Id = package.Id,
            Version = package.Version,
            Status = status,
            Origin = origin,
            ModernFrameworks = frameworks?.Modern ?? [],
        };
    }

    private static PackageCompatibility Unknown(PackageIdentity package, string origin, string reason) => new()
    {
        Id = package.Id,
        Version = package.Version,
        Status = PackageStatus.Unknown,
        Origin = origin,
        Reason = reason,
    };
}

/// <summary>Default factory backed by IHttpClientFactory (named client "nuget").</summary>
public sealed class NuGetFeedClientFactory(IHttpClientFactory httpClientFactory, ILoggerFactory loggerFactory, TimeProvider time)
    : INuGetFeedClientFactory
{
    public const string HttpClientName = "nuget";

    public NuGetFeedClient Create(Uri serviceIndex) =>
        new(httpClientFactory.CreateClient(HttpClientName), serviceIndex, loggerFactory.CreateLogger<NuGetFeedClient>(), time);
}
