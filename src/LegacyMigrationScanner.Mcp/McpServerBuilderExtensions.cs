using LegacyMigrationScanner.Core;
using LegacyMigrationScanner.Mcp;
using Microsoft.Extensions.DependencyInjection.Extensions;

// Extension methods live in the DI namespace by convention so they are discoverable without extra usings.
namespace Microsoft.Extensions.DependencyInjection;

public static class LegacyMigrationScannerMcpExtensions
{
    public const string ServerName = "legacy-migration-scanner";

    public const string Instructions =
        "Legacy Migration Scanner analyzes .NET Framework 4.x solutions (without building them) and reports what blocks a move to " +
        "modern .NET: System.Web/ASP.NET, WCF, incompatible NuGet packages, platform APIs, EF6, configuration. Start with scan_solution, " +
        "then drill down with get_findings, explain_finding and get_packages. Effort is a heuristic in hours (P50/P90). " +
        "Use the plan_migration prompt for a phased plan.";

    /// <summary>
    /// Registers the scanner tools and prompts on an MCP server. The scanner itself must be registered with
    /// <c>AddLegacyMigrationScanner()</c>; allowed roots come from <paramref name="configure"/> (default: working directory).
    /// </summary>
    public static IMcpServerBuilder WithLegacyMigrationScanner(this IMcpServerBuilder builder, Action<McpServerSettings>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var settings = new McpServerSettings();
        configure?.Invoke(settings);

        builder.Services.TryAddSingleton(settings);
        builder.Services.TryAddSingleton<PathGuard>();
        builder.Services.TryAddSingleton<ScanSession>();
        builder.Services.Configure<ModelContextProtocol.Server.McpServerOptions>(o =>
        {
            o.ServerInfo = new ModelContextProtocol.Protocol.Implementation
            {
                Name = ServerName,
                Title = "Legacy Migration Scanner",
                Version = MigrationScanner.ToolVersion,
            };
            o.ServerInstructions ??= Instructions;
        });

        // Static prompt class: pass it as a type list (the generic WithPrompts<T>(T) overload would register the Type object itself).
        return builder.WithTools<ScannerTools>().WithPrompts([typeof(MigrationPrompts)]);
    }
}
