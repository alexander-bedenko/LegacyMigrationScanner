using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LegacyMigrationScanner.Core.Packages;

[JsonConverter(typeof(JsonStringEnumConverter<PackageAction>))]
public enum PackageAction
{
    Compatible,
    Upgrade,
    Replace,
    Remove,
    Incompatible,
    Review,
}

public sealed record KnownPackage
{
    public required string Pattern { get; init; }
    public required PackageAction Action { get; init; }
    public string? MinVersion { get; init; }
    public string? Replacement { get; init; }

    public bool IsPrefix => Pattern.EndsWith('*');
    public string Prefix => Pattern.TrimEnd('*');
}

/// <summary>
/// Curated offline knowledge base. It takes precedence over feed metadata because it encodes migration
/// intent (e.g. Microsoft.AspNet.WebApi is "replace with ASP.NET Core", not merely "incompatible").
/// </summary>
public sealed class PackageCatalog
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private readonly Dictionary<string, KnownPackage> _exact;
    private readonly List<KnownPackage> _prefixes;

    public PackageCatalog(IEnumerable<KnownPackage> packages)
    {
        ArgumentNullException.ThrowIfNull(packages);
        var list = packages.ToList();
        _exact = list.Where(p => !p.IsPrefix)
            .GroupBy(p => p.Pattern, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        _prefixes = [.. list.Where(p => p.IsPrefix).OrderByDescending(p => p.Prefix.Length)];
    }

    public KnownPackage? Find(string packageId)
    {
        ArgumentNullException.ThrowIfNull(packageId);
        return _exact.TryGetValue(packageId, out var exact)
            ? exact
            : _prefixes.FirstOrDefault(p => packageId.StartsWith(p.Prefix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Classifies a package, or returns null when the catalog has no opinion.</summary>
    public PackageCompatibility? Classify(PackageIdentity package)
    {
        var known = Find(package.Id);
        if (known is null)
        {
            return null;
        }

        var status = known.Action switch
        {
            PackageAction.Compatible => PackageStatus.Compatible,
            PackageAction.Upgrade when known.MinVersion is not null
                                      && package.Version.Length > 0
                                      && NuGet.NuGetVersion.Compare(package.Version, known.MinVersion) >= 0 => PackageStatus.Compatible,
            PackageAction.Upgrade => PackageStatus.UpgradeRequired,
            PackageAction.Replace => PackageStatus.Replace,
            PackageAction.Remove => PackageStatus.Remove,
            PackageAction.Incompatible => PackageStatus.Incompatible,
            PackageAction.Review => PackageStatus.Review,
            _ => PackageStatus.Unknown,
        };

        return new PackageCompatibility
        {
            Id = package.Id,
            Version = package.Version,
            Status = status,
            Origin = "catalog",
            RecommendedVersion = status == PackageStatus.UpgradeRequired ? known.MinVersion : null,
            Replacement = known.Replacement,
        };
    }

    public static PackageCatalog LoadDefault()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LegacyMigrationScanner.known-packages.json")
                           ?? throw new InvalidOperationException("Embedded known-packages.json not found.");
        var file = JsonSerializer.Deserialize<CatalogFile>(stream, Options)
                   ?? throw new InvalidDataException("known-packages.json is empty.");
        return new PackageCatalog(file.Packages);
    }

    private sealed record CatalogFile(List<KnownPackage> Packages);
}
