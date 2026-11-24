namespace LegacyMigrationScanner.Core;

/// <summary>Configuration of a scan. Bound via <c>services.AddLegacyMigrationScanner(o => ...)</c>.</summary>
public sealed class ScannerOptions
{
    public static readonly Uri NuGetOrgSource = new("https://api.nuget.org/v3/index.json");

    /// <summary>Hours per effort point (1 point ≈ 2 h by default). Calibrate on a migration you already did.</summary>
    public double HoursPerPoint { get; set; } = 2;

    /// <summary>Optional JSON file overriding rule points/severity.</summary>
    public string? RulesOverridePath { get; set; }

    /// <summary>When true, packages are classified only by the embedded catalog; no network calls are made.</summary>
    public bool Offline { get; set; }

    /// <summary>NuGet V3 service index URLs queried in order for packages unknown to the catalog.</summary>
    public IList<Uri> NuGetSources { get; } = [];

    /// <summary>Directory of the package lookup cache. Null disables caching.</summary>
    public string? CacheDirectory { get; set; } = DefaultCacheDirectory;

    /// <summary>How long a successful lookup stays valid.</summary>
    public TimeSpan CacheTimeToLive { get; set; } = TimeSpan.FromDays(7);

    /// <summary>How long a "not found" lookup stays valid (internal packages may be published later).</summary>
    public TimeSpan NotFoundCacheTimeToLive { get; set; } = TimeSpan.FromDays(1);

    /// <summary>Maximum number of concurrent package lookups.</summary>
    public int MaxConcurrentLookups { get; set; } = 8;

    /// <summary>Per-request timeout for feed calls.</summary>
    public TimeSpan RequestTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Deep mode (ADR 0005): compile each C# project against .NET Framework reference assemblies and analyze symbols
    /// and binary references. Default: syntax-only analysis (ADR 0001).
    /// </summary>
    public bool Deep { get; set; }

    /// <summary>
    /// Where downloaded .NET Framework reference assemblies are kept (immutable, never expire). Defaults to
    /// <c>refasm</c> under <see cref="CacheDirectory"/> (or the default cache directory when caching is disabled).
    /// </summary>
    public string ReferenceAssembliesDirectory
    {
        get => _referenceAssembliesDirectory ?? Path.Combine(CacheDirectory ?? DefaultCacheDirectory, "refasm");
        set => _referenceAssembliesDirectory = value;
    }

    private string? _referenceAssembliesDirectory;

    public IReadOnlyList<Uri> EffectiveNuGetSources => NuGetSources.Count > 0 ? [.. NuGetSources] : [NuGetOrgSource];

    public static string DefaultCacheDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData, Environment.SpecialFolderOption.Create),
            "LegacyMigrationScanner", "cache");
}
