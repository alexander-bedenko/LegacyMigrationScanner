using System.Collections.Concurrent;
using System.Net;
using System.Text;

namespace LegacyMigrationScanner.Tests.Infrastructure;

/// <summary>In-memory NuGet V3 feed (service index + flat container) for deterministic resolver tests.</summary>
internal sealed class FakeNuGetFeed : HttpMessageHandler
{
    public static readonly Uri ServiceIndex = new("https://feed.test/v3/index.json");
    private const string FlatContainer = "https://feed.test/v3-flatcontainer/";

    private readonly Dictionary<string, (HttpStatusCode Status, string Body)> _responses = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, byte[]> _binaries = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, int> _failuresBeforeSuccess = new(StringComparer.OrdinalIgnoreCase);

    public FakeNuGetFeed()
    {
        _responses[ServiceIndex.AbsoluteUri] = (HttpStatusCode.OK, $$"""
            { "version": "3.0.0", "resources": [
              { "@id": "https://feed.test/v3/registration/", "@type": "RegistrationsBaseUrl/3.6.0" },
              { "@id": "{{FlatContainer}}", "@type": "PackageBaseAddress/3.0.0" } ] }
            """);
    }

    public ConcurrentQueue<Uri> Requests { get; } = new();

    /// <summary>Adds a package; each version lists the TFMs of its nuspec dependency groups.</summary>
    public FakeNuGetFeed WithPackage(string id, params (string Version, string[] Frameworks)[] versions)
    {
        var lower = id.ToLowerInvariant();
        var list = string.Join(", ", versions.Select(v => $"\"{v.Version}\""));
        _responses[$"{FlatContainer}{lower}/index.json"] = (HttpStatusCode.OK, $$"""{ "versions": [{{list}}] }""");

        foreach (var (version, frameworks) in versions)
        {
            var groups = new StringBuilder();
            foreach (var tfm in frameworks)
            {
                groups.Append(CultureInvariant($"<group targetFramework=\"{tfm}\" />"));
            }

            var nuspec = $"""
                <?xml version="1.0" encoding="utf-8"?>
                <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                  <metadata><id>{id}</id><version>{version}</version><dependencies>{groups}</dependencies></metadata>
                </package>
                """;
            _responses[$"{FlatContainer}{lower}/{version.ToLowerInvariant()}/{lower}.nuspec"] = (HttpStatusCode.OK, nuspec);
        }

        return this;
    }

    /// <summary>Serves a .nupkg for id/version from the flat container.</summary>
    public FakeNuGetFeed WithNupkg(string id, string version, byte[] content)
    {
        var lower = id.ToLowerInvariant();
        var v = version.ToLowerInvariant();
        _binaries[$"{FlatContainer}{lower}/{v}/{lower}.{v}.nupkg"] = content;
        return this;
    }

    public FakeNuGetFeed FailFirst(string urlSuffix, int times)
    {
        _failuresBeforeSuccess[urlSuffix] = times;
        return this;
    }

    public FakeNuGetFeed WithResponse(string url, HttpStatusCode status, string body = "")
    {
        _responses[url] = (status, body);
        return this;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var url = request.RequestUri!;
        Requests.Enqueue(url);

        foreach (var (suffix, remaining) in _failuresBeforeSuccess)
        {
            if (url.AbsoluteUri.EndsWith(suffix, StringComparison.OrdinalIgnoreCase) && remaining > 0)
            {
                _failuresBeforeSuccess[suffix] = remaining - 1;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
            }
        }

        if (_binaries.TryGetValue(url.AbsoluteUri, out var bytes))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }

        var (status, body) = _responses.TryGetValue(url.AbsoluteUri, out var r) ? r : (HttpStatusCode.NotFound, "");
        return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8) });
    }

    private static string CultureInvariant(FormattableString value) => FormattableString.Invariant(value);
}
