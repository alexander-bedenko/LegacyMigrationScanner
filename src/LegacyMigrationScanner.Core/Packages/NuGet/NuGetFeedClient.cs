using System.Net;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using LegacyMigrationScanner.Core.Diagnostics;
using Microsoft.Extensions.Logging;

namespace LegacyMigrationScanner.Core.Packages.NuGet;

/// <summary>Target frameworks declared by a package version's nuspec.</summary>
public sealed record NuspecFrameworks(IReadOnlyList<string> TargetFrameworks)
{
    /// <summary>False when the nuspec has no framework-specific groups (old packages): support cannot be inferred.</summary>
    public bool HasFrameworkInfo => TargetFrameworks.Count > 0;

    public IReadOnlyList<string> Modern =>
        [.. TargetFrameworks.Where(TargetFrameworkClassifier.IsModern).Select(TargetFrameworkClassifier.ToShortName).Distinct(StringComparer.OrdinalIgnoreCase)];

    public bool SupportsModern => Modern.Count > 0;
}

/// <summary>
/// Thin client for one NuGet V3 feed (nuget.org, Azure Artifacts, GitHub Packages, ...).
/// Uses the service index to locate the flat container (PackageBaseAddress/3.0.0) and reads
/// version lists and nuspecs from it - two small JSON/XML requests per package, no nupkg downloads.
/// </summary>
public sealed class NuGetFeedClient
{
    private const string PackageBaseAddressType = "PackageBaseAddress/3.0.0";
    private const int MaxAttempts = 3;

    private readonly HttpClient _http;
    private readonly ILogger _logger;
    private readonly TimeProvider _time;
    private readonly Lazy<Task<Uri>> _baseAddress;

    public NuGetFeedClient(HttpClient http, Uri serviceIndex, ILogger logger, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(serviceIndex);
        _http = http;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        ServiceIndex = serviceIndex;
        _baseAddress = new Lazy<Task<Uri>>(() => ResolveBaseAddressAsync(CancellationToken.None));
    }

    public Uri ServiceIndex { get; }

    /// <summary>All published versions, or null if the package does not exist on this feed.</summary>
    public async Task<IReadOnlyList<string>?> GetVersionsAsync(string packageId, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        var baseAddress = await _baseAddress.Value.WaitAsync(cancellationToken);
        var url = new Uri(baseAddress, $"{Uri.EscapeDataString(packageId.ToLowerInvariant())}/index.json");

        using var response = await SendAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        return doc.RootElement.TryGetProperty("versions", out var versions)
            ? [.. versions.EnumerateArray().Select(v => v.GetString()).OfType<string>()]
            : [];
    }

    /// <summary>Target frameworks of one version, or null if that version's nuspec does not exist.</summary>
    public async Task<NuspecFrameworks?> GetFrameworksAsync(string packageId, string version, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(version);
        var baseAddress = await _baseAddress.Value.WaitAsync(cancellationToken);
        var id = Uri.EscapeDataString(packageId.ToLowerInvariant());
        var normalized = Uri.EscapeDataString(NuGetVersion.Normalize(version));
        var url = new Uri(baseAddress, $"{id}/{normalized}/{id}.nuspec");

        using var response = await SendAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        try
        {
            var doc = await XDocument.LoadAsync(stream, LoadOptions.None, cancellationToken);
            return new NuspecFrameworks(ParseTargetFrameworks(doc));
        }
        catch (XmlException ex)
        {
            throw new InvalidDataException($"Malformed nuspec for {packageId} {version}: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Downloads a .nupkg into <paramref name="destination"/>; returns false if that version does not exist on this feed.
    /// Only the response headers are subject to the HTTP client timeout, so large packages are not cut off.
    /// </summary>
    public async Task<bool> DownloadPackageAsync(string packageId, string version, Stream destination, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        ArgumentNullException.ThrowIfNull(version);
        ArgumentNullException.ThrowIfNull(destination);
        var baseAddress = await _baseAddress.Value.WaitAsync(cancellationToken);
        var id = Uri.EscapeDataString(packageId.ToLowerInvariant());
        var normalized = Uri.EscapeDataString(NuGetVersion.Normalize(version));
        var url = new Uri(baseAddress, $"{id}/{normalized}/{id}.{normalized}.nupkg");

        using var response = await SendAsync(url, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }

        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await stream.CopyToAsync(destination, cancellationToken);
        return true;
    }

    internal static List<string> ParseTargetFrameworks(XDocument nuspec)
    {
        // <dependencies><group targetFramework="..."/></dependencies>, <frameworkReferences><group .../></frameworkReferences>
        return nuspec.Descendants()
            .Where(e => e.Name.LocalName == "group" && e.Parent?.Name.LocalName is "dependencies" or "frameworkReferences")
            .Select(e => (string?)e.Attribute("targetFramework"))
            .Where(tf => !string.IsNullOrWhiteSpace(tf))
            .Select(tf => tf!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<Uri> ResolveBaseAddressAsync(CancellationToken cancellationToken)
    {
        using var response = await SendAsync(ServiceIndex, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        foreach (var resource in doc.RootElement.GetProperty("resources").EnumerateArray())
        {
            var type = resource.GetProperty("@type").GetString();
            if (type is not null && type.StartsWith(PackageBaseAddressType, StringComparison.Ordinal))
            {
                var id = resource.GetProperty("@id").GetString()!;
                return new Uri(id.EndsWith('/') ? id : id + "/");
            }
        }

        throw new InvalidDataException($"Service index {ServiceIndex} has no {PackageBaseAddressType} resource.");
    }

    /// <summary>GET with retries on transient failures (network errors, timeouts, 429, 5xx).</summary>
    private async Task<HttpResponseMessage> SendAsync(Uri url, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            string reason;
            try
            {
                var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                if (!IsTransient(response.StatusCode) || attempt == MaxAttempts)
                {
                    return response;
                }

                reason = $"HTTP {(int)response.StatusCode}";
                response.Dispose();
            }
            catch (HttpRequestException ex) when (attempt < MaxAttempts)
            {
                reason = ex.Message;
            }
            catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested && attempt < MaxAttempts)
            {
                reason = "timeout: " + ex.Message;
            }

            var delay = TimeSpan.FromMilliseconds(200 * Math.Pow(2, attempt - 1));
            Log.RequestRetry(_logger, url, (int)delay.TotalMilliseconds, attempt, reason);
            await Task.Delay(delay, _time, cancellationToken);
        }
    }

    private static bool IsTransient(HttpStatusCode status) =>
        status == HttpStatusCode.TooManyRequests || (int)status >= 500;
}
