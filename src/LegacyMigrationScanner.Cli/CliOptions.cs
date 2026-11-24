using System.Globalization;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Core.Reporting;
using Microsoft.Extensions.Logging;

namespace LegacyMigrationScanner.Cli;

internal sealed class CliOptions
{
    public const string Usage = """
        Legacy Migration Scanner - finds .NET Framework -> modern .NET migration blockers.

        Usage:
          lms scan <path> [options]
          lms --version

        Arguments:
          <path>                      .sln, .slnx, .csproj, or a directory with one solution

        Report options:
          -o, --output <dir>          Output directory (default: ./lms-report)
          -f, --format <list>         Comma-separated: json, md, sarif (default: json,md)
              --sarif-root <dir>      Base directory for SARIF paths, usually the repo root (default: current dir)
              --fail-on <severity>    Exit with code 2 if findings of this severity exist: critical | warning | info

        Baseline options:
              --baseline <file>       Previous JSON report (-f json); reports mark findings as new/unchanged/fixed
              --fail-on-new           Exit with code 2 if there are new findings vs --baseline
                                      (combined with --fail-on: only new findings of that severity or higher)

        Estimation options:
              --hours-per-point <n>   Hours per effort point (default: 2)
              --rules <file>          JSON file overriding rule points/severity (calibration)

        Package options:
              --offline               Do not query NuGet feeds; use only the built-in package catalog
              --nuget-source <url>    NuGet V3 service index; repeatable (default: nuget.org)
              --cache-dir <dir>       Package lookup cache directory (default: %LOCALAPPDATA%/LegacyMigrationScanner/cache)
              --no-cache              Disable the package lookup cache

        General:
          -v, --verbosity <level>     quiet | minimal | normal | detailed (default: minimal)
          -h, --help                  Show help
              --version               Show version

        Exit codes: 0 success, 1 scan failed, 2 --fail-on/--fail-on-new threshold exceeded, 64 invalid arguments, 130 cancelled.
        """;

    public string? Path { get; private set; }
    public string OutputDirectory { get; private set; } = "lms-report";
    public IReadOnlyList<string> Formats { get; private set; } = ["json", "md"];
    public string? SarifBaseDirectory { get; private set; }
    public double HoursPerPoint { get; private set; } = 2;
    public string? RulesPath { get; private set; }
    public Severity? FailOn { get; private set; }
    public string? BaselinePath { get; private set; }
    public bool FailOnNew { get; private set; }
    public bool Offline { get; private set; }
    public IReadOnlyList<Uri> NuGetSources => _sources;
    public string? CacheDirectory { get; private set; }
    public bool NoCache { get; private set; }
    public LogLevel LogLevel { get; private set; } = LogLevel.Warning;
    public bool ShowHelp { get; private set; }
    public bool ShowVersion { get; private set; }

    private readonly List<Uri> _sources = [];

    public static CliOptions Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        var options = new CliOptions();
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            options.ShowHelp = true;
            return options;
        }

        if (args[0] == "--version")
        {
            options.ShowVersion = true;
            return options;
        }

        if (args[0] != "scan")
        {
            throw new ArgumentException($"Unknown command '{args[0]}'.");
        }

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--help":
                    options.ShowHelp = true;
                    break;
                case "-o" or "--output":
                    options.OutputDirectory = Next(args, ref i, arg);
                    break;
                case "-f" or "--format":
                    options.Formats = ParseFormats(Next(args, ref i, arg));
                    break;
                case "--sarif-root":
                    options.SarifBaseDirectory = Next(args, ref i, arg);
                    break;
                case "--hours-per-point":
                    options.HoursPerPoint = ParsePositiveDouble(Next(args, ref i, arg), arg);
                    break;
                case "--rules":
                    options.RulesPath = Next(args, ref i, arg);
                    break;
                case "--fail-on":
                    var severity = Next(args, ref i, arg);
                    options.FailOn = Enum.TryParse<Severity>(severity, ignoreCase: true, out var s) && Enum.IsDefined(s)
                        ? s
                        : throw new ArgumentException($"Invalid --fail-on value '{severity}'.");
                    break;
                case "--baseline":
                    options.BaselinePath = Next(args, ref i, arg);
                    break;
                case "--fail-on-new":
                    options.FailOnNew = true;
                    break;
                case "--offline":
                    options.Offline = true;
                    break;
                case "--nuget-source":
                    var raw = Next(args, ref i, arg);
                    options._sources.Add(Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"
                        ? uri
                        : throw new ArgumentException($"Invalid --nuget-source '{raw}': expected an absolute http(s) URL of a V3 service index."));
                    break;
                case "--cache-dir":
                    options.CacheDirectory = Next(args, ref i, arg);
                    break;
                case "--no-cache":
                    options.NoCache = true;
                    break;
                case "-v" or "--verbosity":
                    options.LogLevel = ParseVerbosity(Next(args, ref i, arg));
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        throw new ArgumentException($"Unknown option '{arg}'.");
                    }

                    if (options.Path is not null)
                    {
                        throw new ArgumentException($"Unexpected argument '{arg}'.");
                    }

                    options.Path = arg;
                    break;
            }
        }

        if (options.Path is null && !options.ShowHelp)
        {
            throw new ArgumentException("Missing <path> to a solution or project.");
        }

        if (options.FailOnNew && options.BaselinePath is null && !options.ShowHelp)
        {
            throw new ArgumentException("--fail-on-new requires --baseline <file>.");
        }

        return options;
    }

    private static List<string> ParseFormats(string value)
    {
        var formats = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(f => f.ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var unknown = formats.Where(f => ReportWriters.Create(f) is null).ToList();
        if (formats.Count == 0 || unknown.Count > 0)
        {
            throw new ArgumentException($"Unknown format(s): {string.Join(", ", unknown)}. Supported: {string.Join(", ", ReportWriters.SupportedFormats)}.");
        }

        return formats;
    }

    private static double ParsePositiveDouble(string raw, string option) =>
        double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0 && double.IsFinite(value)
            ? value
            : throw new ArgumentException($"Invalid {option} value '{raw}'.");

    private static LogLevel ParseVerbosity(string raw) => raw.ToLowerInvariant() switch
    {
        "q" or "quiet" => LogLevel.Error,
        "m" or "minimal" => LogLevel.Warning,
        "n" or "normal" => LogLevel.Information,
        "d" or "detailed" or "diag" or "diagnostic" => LogLevel.Debug,
        _ => throw new ArgumentException($"Invalid --verbosity value '{raw}'."),
    };

    private static string Next(string[] args, ref int i, string option) =>
        ++i < args.Length ? args[i] : throw new ArgumentException($"Option '{option}' requires a value.");
}
