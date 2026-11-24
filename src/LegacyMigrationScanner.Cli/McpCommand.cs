using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyMigrationScanner.Cli;

/// <summary><c>lms mcp</c>: Model Context Protocol server over stdio (ADR 0004).</summary>
internal static class McpCommand
{
    public static async Task<int> RunAsync(CliOptions options, TextWriter stderr, CancellationToken cancellationToken)
    {
        var missing = options.McpRoots.Where(r => !Directory.Exists(r)).ToList();
        if (missing.Count > 0)
        {
            await stderr.WriteLineAsync($"error: --root directory not found: {string.Join(", ", missing)}");
            return ExitCodes.InvalidArguments;
        }

        // DisableDefaults: do not pick up appsettings.json / environment configuration from the scanned repository.
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            DisableDefaults = true,
            ContentRootPath = Environment.CurrentDirectory,
        });

        // Stdout carries the protocol: every log line must go to stderr.
        builder.Logging.ClearProviders();
        builder.Logging.SetMinimumLevel(options.LogLevel);
        builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.Configure<ConsoleLifetimeOptions>(o => o.SuppressStatusMessages = true);

        builder.Services.AddLegacyMigrationScanner(o => CliApplication.ConfigureScanner(o, options));
        builder.Services
            .AddMcpServer()
            .WithStdioServerTransport()
            .WithLegacyMigrationScanner(settings =>
            {
                foreach (var root in options.McpRoots)
                {
                    settings.AllowedRoots.Add(root);
                }
            });

        try
        {
            using var host = builder.Build();
            await host.RunAsync(cancellationToken);
            return ExitCodes.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return ExitCodes.Success;
        }
        catch (OptionsValidationException ex)
        {
            await stderr.WriteLineAsync($"error: {ex.Message}");
            return ExitCodes.InvalidArguments;
        }
    }
}
