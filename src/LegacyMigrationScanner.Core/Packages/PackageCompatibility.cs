using System.Text.Json.Serialization;

namespace LegacyMigrationScanner.Core.Packages;

[JsonConverter(typeof(JsonStringEnumConverter<PackageStatus>))]
public enum PackageStatus
{
    /// <summary>The referenced version already supports modern .NET.</summary>
    Compatible,
    /// <summary>A newer version supports modern .NET.</summary>
    UpgradeRequired,
    /// <summary>Superseded by a different package or built-in functionality.</summary>
    Replace,
    /// <summary>Not needed on modern .NET.</summary>
    Remove,
    /// <summary>Runs on modern .NET, but a recommended successor exists.</summary>
    Review,
    /// <summary>No version supports modern .NET.</summary>
    Incompatible,
    /// <summary>Not found on any configured feed (often an internal package).</summary>
    NotFound,
    /// <summary>Could not be determined (offline mode, lookup failure, no framework metadata).</summary>
    Unknown,
}

/// <summary>A package reference as it appears in a project.</summary>
public readonly record struct PackageIdentity(string Id, string Version)
{
    public string Key => $"{Id.ToLowerInvariant()}/{NuGet.NuGetVersion.Normalize(Version)}";

    public override string ToString() => $"{Id} {Version}".Trim();
}

/// <summary>Result of classifying one package version.</summary>
public sealed record PackageCompatibility
{
    public required string Id { get; init; }
    public required string Version { get; init; }
    public required PackageStatus Status { get; init; }

    /// <summary>"catalog", the feed URL, or "offline".</summary>
    public required string Origin { get; init; }

    /// <summary>Version to upgrade to, for <see cref="PackageStatus.UpgradeRequired"/>.</summary>
    public string? RecommendedVersion { get; init; }

    /// <summary>Replacement package / technology, when known.</summary>
    public string? Replacement { get; init; }

    /// <summary>Modern target frameworks supported by the evaluated version (online lookups only).</summary>
    public IReadOnlyList<string> ModernFrameworks { get; init; } = [];

    /// <summary>Why the status is Unknown / NotFound, for diagnostics.</summary>
    public string? Reason { get; init; }
}
