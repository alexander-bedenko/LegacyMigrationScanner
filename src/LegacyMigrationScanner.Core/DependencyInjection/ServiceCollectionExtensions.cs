using LegacyMigrationScanner.Core;
using LegacyMigrationScanner.Core.Analyzers;
using LegacyMigrationScanner.Core.Packages;
using LegacyMigrationScanner.Core.Packages.Caching;
using LegacyMigrationScanner.Core.Rules;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

// Extension methods live in the DI namespace by convention so they are discoverable without extra usings.
namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the scanner, analyzers, package resolution (with HTTP client) and defaults.</summary>
    public static IServiceCollection AddLegacyMigrationScanner(this IServiceCollection services, Action<ScannerOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = services.AddOptions<ScannerOptions>();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        options.Validate(o => o.HoursPerPoint > 0, "HoursPerPoint must be positive.")
            .Validate(o => o.MaxConcurrentLookups > 0, "MaxConcurrentLookups must be positive.")
            .Validate(o => o.NuGetSources.All(s => s.IsAbsoluteUri && s.Scheme is "https" or "http" or "file"), "NuGet sources must be absolute http(s) URLs.");

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton(sp => RuleCatalog.LoadWithOverrides(sp.GetRequiredService<IOptions<ScannerOptions>>().Value.RulesOverridePath));
        services.TryAddSingleton(_ => PackageCatalog.LoadDefault());

        // Analyzer registration order does not matter: findings are sorted after analysis.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProjectAnalyzer, ProjectStructureAnalyzer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProjectAnalyzer, FileArtifactAnalyzer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProjectAnalyzer, ConfigAnalyzer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProjectAnalyzer, CodeAnalyzer>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IProjectAnalyzer, PackageAnalyzer>());

        services.AddHttpClient(NuGetFeedClientFactory.HttpClientName, (sp, client) =>
        {
            client.Timeout = sp.GetRequiredService<IOptions<ScannerOptions>>().Value.RequestTimeout;
            client.DefaultRequestHeaders.UserAgent.ParseAdd($"LegacyMigrationScanner/{MigrationScanner.ToolVersion}");
        });
        services.TryAddSingleton<INuGetFeedClientFactory, NuGetFeedClientFactory>();
        services.TryAddSingleton<IPackageCache>(sp =>
            sp.GetRequiredService<IOptions<ScannerOptions>>().Value.CacheDirectory is null
                ? new NullPackageCache()
                : ActivatorUtilities.CreateInstance<FilePackageCache>(sp));
        services.TryAddSingleton<IPackageCompatibilityService, PackageCompatibilityService>();
        services.TryAddSingleton<IMigrationScanner, MigrationScanner>();

        return services;
    }
}
