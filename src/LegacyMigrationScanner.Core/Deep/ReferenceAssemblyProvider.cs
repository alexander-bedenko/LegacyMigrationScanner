using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text.RegularExpressions;
using LegacyMigrationScanner.Core.Diagnostics;
using LegacyMigrationScanner.Core.Packages;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyMigrationScanner.Core.Deep;

/// <summary>Supplies the .NET Framework reference assemblies used to compile legacy projects in deep mode (ADR 0005).</summary>
public interface IReferenceAssemblyProvider
{
    /// <summary>
    /// Directory with the reference assemblies (mscorlib.dll, System.*.dll, Facades/) for a .NET Framework version
    /// such as "v4.8", or null when they cannot be obtained (offline without cache, feed failure).
    /// </summary>
    Task<string?> GetDirectoryAsync(string targetFrameworkVersion, CancellationToken cancellationToken);
}

/// <summary>
/// Downloads <c>Microsoft.NETFramework.ReferenceAssemblies.&lt;tfm&gt;</c> from the configured feeds once and caches the
/// DLLs under <c>&lt;cache&gt;/refasm</c>. The packages are immutable, so the cache never expires.
/// </summary>
public sealed partial class NuGetReferenceAssemblyProvider : IReferenceAssemblyProvider
{
    public const string PackageVersion = "1.0.3";
    private const string MarkerFile = ".complete";

    private readonly INuGetFeedClientFactory _feeds;
    private readonly ScannerOptions _options;
    private readonly ILogger<NuGetReferenceAssemblyProvider> _logger;
    private readonly ConcurrentDictionary<string, Lazy<Task<string?>>> _directories = new(StringComparer.OrdinalIgnoreCase);

    public NuGetReferenceAssemblyProvider(INuGetFeedClientFactory feeds, IOptions<ScannerOptions> options, ILogger<NuGetReferenceAssemblyProvider> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _feeds = feeds;
        _options = options.Value;
        _logger = logger;
    }

    /// <summary>Maps "v4.7.2" to "net472"; null for anything that is not a .NET Framework version.</summary>
    public static string? ToTargetFrameworkMoniker(string? targetFrameworkVersion)
    {
        if (targetFrameworkVersion is null)
        {
            return null;
        }

        var match = FrameworkVersion().Match(targetFrameworkVersion.Trim());
        return match.Success ? "net" + match.Groups[1].Value + match.Groups[2].Value + match.Groups[3].Value : null;
    }

    public Task<string?> GetDirectoryAsync(string targetFrameworkVersion, CancellationToken cancellationToken)
    {
        var tfm = ToTargetFrameworkMoniker(targetFrameworkVersion);
        if (tfm is null)
        {
            return Task.FromResult<string?>(null);
        }

        // One download per framework version per process; callers that cancel do not cancel it for others.
        var lazy = _directories.GetOrAdd(tfm, t => new Lazy<Task<string?>>(() => ResolveAsync(t, NormalizeVersionFolder(targetFrameworkVersion), CancellationToken.None)));
        return lazy.Value.WaitAsync(cancellationToken);
    }

    private async Task<string?> ResolveAsync(string tfm, string versionFolder, CancellationToken cancellationToken)
    {
        var packageId = $"Microsoft.NETFramework.ReferenceAssemblies.{tfm}";
        var root = Path.Combine(_options.ReferenceAssembliesDirectory, packageId.ToUpperInvariant(), PackageVersion);
        var assemblies = Path.Combine(root, "build", ".NETFramework", versionFolder);
        if (File.Exists(Path.Combine(root, MarkerFile)) && Directory.Exists(assemblies))
        {
            return assemblies;
        }

        if (_options.Offline)
        {
            Log.ReferenceAssembliesUnavailable(_logger, packageId, "offline mode and not in the cache");
            return null;
        }

        foreach (var source in _options.EffectiveNuGetSources)
        {
            try
            {
                var temp = Path.Combine(Path.GetTempPath(), "lms-refasm-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temp);
                try
                {
                    var nupkg = Path.Combine(temp, "package.nupkg");
                    Log.DownloadingReferenceAssemblies(_logger, packageId, PackageVersion, source);
                    bool found;
                    await using (var file = File.Create(nupkg))
                    {
                        found = await _feeds.Create(source).DownloadPackageAsync(packageId, PackageVersion, file, cancellationToken);
                    }

                    if (!found)
                    {
                        continue;
                    }

                    var staging = Path.Combine(temp, "extracted");
                    ExtractAssemblies(nupkg, staging);
                    if (!Directory.Exists(Path.Combine(staging, "build", ".NETFramework", versionFolder)))
                    {
                        throw new InvalidDataException($"{packageId} has no build/.NETFramework/{versionFolder} folder.");
                    }

                    Publish(staging, root);
                    return assemblies;
                }
                finally
                {
                    TryDelete(temp);
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException or TaskCanceledException)
            {
                Log.ReferenceAssembliesUnavailable(_logger, packageId, $"{source}: {ex.Message}");
            }
        }

        Log.ReferenceAssembliesUnavailable(_logger, packageId, "not found on the configured feeds");
        return null;
    }

    /// <summary>Extracts only the DLLs under build/.NETFramework (the package is mostly XML documentation).</summary>
    internal static void ExtractAssemblies(string nupkg, string destination)
    {
        using var zip = ZipFile.OpenRead(nupkg);
        var fullDestination = Path.GetFullPath(destination) + Path.DirectorySeparatorChar;
        foreach (var entry in zip.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (!name.StartsWith("build/.NETFramework/", StringComparison.OrdinalIgnoreCase)
                || !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var target = Path.GetFullPath(Path.Combine(destination, name));
            if (!target.StartsWith(fullDestination, StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Unsafe path in package: {entry.FullName}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            entry.ExtractToFile(target, overwrite: true);
        }
    }

    /// <summary>Moves a fully extracted folder into the cache and marks it complete; tolerates a concurrent winner.</summary>
    private static void Publish(string staging, string root)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(root)!);
        if (Directory.Exists(root) && !File.Exists(Path.Combine(root, MarkerFile)))
        {
            TryDelete(root);
        }

        try
        {
            Directory.Move(staging, root);
        }
        catch (IOException) when (File.Exists(Path.Combine(root, MarkerFile)))
        {
            return; // another process published it first
        }

        File.WriteAllText(Path.Combine(root, MarkerFile), "");
    }

    private static string NormalizeVersionFolder(string targetFrameworkVersion)
    {
        var match = FrameworkVersion().Match(targetFrameworkVersion.Trim());
        var parts = new[] { match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value }.Where(p => p.Length > 0);
        return "v" + string.Join('.', parts);
    }

    private static void TryDelete(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best effort: a leftover temp folder is harmless.
        }
    }

    [GeneratedRegex(@"^v?(\d)\.(\d)(?:\.(\d))?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FrameworkVersion();
}
