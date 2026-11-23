using LegacyMigrationScanner.Core;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Core.Reporting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LegacyMigrationScanner.Cli;

/// <summary>Composition root and command execution; separated from Program for testability.</summary>
internal static class CliApplication
{
    public static async Task<int> RunAsync(
        string[] args,
        TextWriter stdout,
        TextWriter stderr,
        CancellationToken cancellationToken,
        Action<IServiceCollection>? configureServices = null)
    {
        CliOptions options;
        try
        {
            options = CliOptions.Parse(args);
        }
        catch (ArgumentException ex)
        {
            await stderr.WriteLineAsync($"error: {ex.Message}");
            await stderr.WriteLineAsync();
            await stderr.WriteLineAsync(CliOptions.Usage);
            return ExitCodes.InvalidArguments;
        }

        if (options.ShowHelp)
        {
            await stdout.WriteLineAsync(CliOptions.Usage);
            return ExitCodes.Success;
        }

        if (options.ShowVersion)
        {
            await stdout.WriteLineAsync(MigrationScanner.ToolVersion);
            return ExitCodes.Success;
        }

        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            logging.SetMinimumLevel(options.LogLevel);
            logging.AddSimpleConsole(console =>
            {
                console.SingleLine = true;
                console.IncludeScopes = false;
                console.TimestampFormat = null;
            });
            // Logs go to stderr so stdout stays machine-friendly.
            logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
        });
        services.AddLegacyMigrationScanner(o =>
        {
            o.HoursPerPoint = options.HoursPerPoint;
            o.RulesOverridePath = options.RulesPath;
            o.Offline = options.Offline;
            o.CacheDirectory = options.NoCache ? null : options.CacheDirectory ?? ScannerOptions.DefaultCacheDirectory;
            foreach (var source in options.NuGetSources)
            {
                o.NuGetSources.Add(source);
            }
        });
        configureServices?.Invoke(services);

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });

        try
        {
            var scanner = provider.GetRequiredService<IMigrationScanner>();
            var result = await scanner.ScanAsync(options.Path!, cancellationToken);

            await WriteReportsAsync(result, options, stdout, cancellationToken);
            ConsoleSummary.Print(result, stdout);

            return options.FailOn is { } threshold && result.Findings.Any(f => f.Severity >= threshold)
                ? ExitCodes.ThresholdExceeded
                : ExitCodes.Success;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await stderr.WriteLineAsync("Scan cancelled.");
            return ExitCodes.Cancelled;
        }
        catch (OptionsValidationException ex)
        {
            await stderr.WriteLineAsync($"error: {ex.Message}");
            return ExitCodes.InvalidArguments;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidOperationException
                                       or IOException or UnauthorizedAccessException or InvalidDataException or System.Xml.XmlException)
        {
            await stderr.WriteLineAsync($"error: {ex.Message}");
            return ExitCodes.ScanFailed;
        }
    }

    private static async Task WriteReportsAsync(ScanResult result, CliOptions options, TextWriter stdout, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(options.OutputDirectory);
        var baseName = Path.GetFileNameWithoutExtension(result.Solution) + ".migration-report";
        var reportOptions = new ReportOptions { SarifBaseDirectory = options.SarifBaseDirectory };

        foreach (var format in options.Formats)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var writer = ReportWriters.Create(format, reportOptions)!;
            var file = Path.Combine(options.OutputDirectory, baseName + writer.FileExtension);
            await using (var stream = new StreamWriter(file, append: false, new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                writer.Write(result, stream);
            }

            await stdout.WriteLineAsync($"Report written: {file}");
        }
    }
}
