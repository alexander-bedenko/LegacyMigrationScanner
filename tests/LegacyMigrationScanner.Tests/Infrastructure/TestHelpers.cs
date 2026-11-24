using System.Reflection;
using LegacyMigrationScanner.Core;
using LegacyMigrationScanner.Core.Analyzers;
using LegacyMigrationScanner.Core.Deep;
using LegacyMigrationScanner.Core.Rules;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace LegacyMigrationScanner.Tests.Infrastructure;

internal static class TestHelpers
{
    private static readonly Lazy<string> RepoRootLazy = new(() =>
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "LegacyMigrationScanner.slnx")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("Repository root (LegacyMigrationScanner.slnx) not found.");
    });

    public static string RepoRoot => RepoRootLazy.Value;

    public static string FixtureDir => Path.Combine(RepoRoot, "tests", "Fixtures", "LegacyShop");

    public static string FixtureSolution => Path.Combine(FixtureDir, "LegacyShop.sln");

    public static RuleCatalog Rules { get; } = RuleCatalog.LoadDefault();

    /// <summary>build/.NETFramework/v4.8 of the restored Microsoft.NETFramework.ReferenceAssemblies.net48 package.</summary>
    public static string Net48ReferenceAssemblies { get; } = Path.Combine(
        typeof(TestHelpers).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Single(a => a.Key == "Net48ReferenceAssemblies").Value
            ?? throw new InvalidOperationException("Net48ReferenceAssemblies metadata missing (package not restored)."),
        "build", ".NETFramework", "v4.8");

    public static string Join(IEnumerable<string> items) => string.Join(", ", items);

    public static AnalysisContext Context(string solutionDir = "/") => new(solutionDir, Rules);

    /// <summary>Builds the production DI graph; network goes to <paramref name="handler"/> when given.</summary>
    public static ServiceProvider BuildServices(
        Action<ScannerOptions>? configure = null,
        HttpMessageHandler? handler = null,
        TimeProvider? time = null,
        IReferenceAssemblyProvider? referenceAssemblies = null)
    {
        var services = new ServiceCollection();
        // Deep mode uses the restored net48 reference assemblies: no downloads in tests.
        services.AddSingleton(referenceAssemblies ?? new LocalReferenceAssemblyProvider(Net48ReferenceAssemblies));
        services.AddLogging(b => b.SetMinimumLevel(LogLevel.Warning));
        if (time is not null)
        {
            services.AddSingleton(time);
        }

        services.AddLegacyMigrationScanner(o =>
        {
            o.Offline = handler is null;
            o.CacheDirectory = null;
            configure?.Invoke(o);
        });

        if (handler is not null)
        {
            services.AddHttpClient(Core.Packages.NuGetFeedClientFactory.HttpClientName)
                .ConfigurePrimaryHttpMessageHandler(() => handler);
        }

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    public static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "lms-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

/// <summary>Serves one directory of reference assemblies for every .NET Framework version (or none).</summary>
internal sealed class LocalReferenceAssemblyProvider(string? directory) : IReferenceAssemblyProvider
{
    public Task<string?> GetDirectoryAsync(string targetFrameworkVersion, CancellationToken cancellationToken) => Task.FromResult(directory);
}
