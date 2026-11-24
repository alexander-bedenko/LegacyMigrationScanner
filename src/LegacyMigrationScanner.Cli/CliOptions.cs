using System.Globalization;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Core.Reporting;
using Microsoft.Extensions.Logging;

namespace LegacyMigrationScanner.Cli;

internal enum CliCommand
{
    Scan,
    Calibrate,
    Mcp,
}

internal sealed class CliOptions
{
    public const string Usage = """
        Legacy Migration Scanner - finds .NET Framework -> modern .NET migration blockers.

        Usage:
          lms scan <path> [options]
          lms calibrate <report.json>... (--actuals <file> | --actual-hours <n>) [-o <file>]
          lms mcp [--root <dir>]... [package/estimation options]
          lms --version

        Arguments:
          <path>                      .sln, .slnx, .csproj, or a directory with one solution

        Report options:
          -o, --output <dir>          Output directory (default: ./lms-report)
          -f, --format <list>         Comma-separated: json, md, sarif, html (default: json,md)
              --sarif-root <dir>      Base directory for SARIF paths, usually the repo root (default: current dir)
              --fail-on <severity>    Exit with code 2 if findings of this severity exist: critical | warning | info

        Baseline options:
              --baseline <file>       Previous JSON report (-f json); reports mark findings as new/unchanged/fixed
              --fail-on-new           Exit with code 2 if there are new findings vs --baseline
                                      (combined with --fail-on: only new findings of that severity or higher)

        Estimation options:
              --hours-per-point <n>   Hours per effort point (default: 2)
              --rules <file>          JSON file overriding rule points/severity (calibration)

        Analysis options:
              --deep                  Semantic analysis: compile projects against .NET Framework reference assemblies
                                      (downloaded once from the NuGet feed) and inspect vendored DLLs (ADR 0005)

        Package options:
              --offline               Do not query NuGet feeds; use only the built-in package catalog
              --nuget-source <url>    NuGet V3 service index; repeatable (default: nuget.org)
              --cache-dir <dir>       Package lookup cache directory (default: %LOCALAPPDATA%/LegacyMigrationScanner/cache)
              --no-cache              Disable the package lookup cache

        Calibrate (fit --hours-per-point from a completed migration):
          <report.json>...            JSON report(s) of the solution(s) as they were before the migration
              --actuals <file>        CSV 'project,hours' with actual hours per project (repeated rows are summed)
              --actual-hours <n>      Or: total actual hours for all given reports
          -o, --output <file>         Also write the calibration result as JSON

        MCP server (stdio; for Claude Code, Claude Desktop, VS Code and other MCP clients):
              --root <dir>            Directory the tools may read; repeatable (default: current directory)
                                      Package and estimation options above apply too.

        General:
          -v, --verbosity <level>     quiet | minimal | normal | detailed (default: minimal)
          -h, --help                  Show help
              --version               Show version

        Exit codes: 0 success, 1 scan failed, 2 --fail-on/--fail-on-new threshold exceeded, 64 invalid arguments, 130 cancelled.
        """;

    public CliCommand Command { get; private set; } = CliCommand.Scan;
    public string? Path { get; private set; }
    public IReadOnlyList<string> Reports => _reports;
    /// <summary>mcp: directories the server may read (default: current directory).</summary>
    public IReadOnlyList<string> McpRoots => _roots;
    public string? ActualsPath { get; private set; }
    public double? ActualHours { get; private set; }
    /// <summary>calibrate: optional JSON output file.</summary>
    public string? CalibrationOutput { get; private set; }
    public string OutputDirectory { get; private set; } = "lms-report";
    public IReadOnlyList<string> Formats { get; private set; } = ["json", "md"];
    public string? SarifBaseDirectory { get; private set; }
    public double HoursPerPoint { get; private set; } = 2;
    public string? RulesPath { get; private set; }
    public Severity? FailOn { get; private set; }
    public string? BaselinePath { get; private set; }
    public bool FailOnNew { get; private set; }
    public bool Offline { get; private set; }
    public bool Deep { get; private set; }
    public IReadOnlyList<Uri> NuGetSources => _sources;
    public string? CacheDirectory { get; private set; }
    public bool NoCache { get; private set; }
    public LogLevel LogLevel { get; private set; } = LogLevel.Warning;
    public bool ShowHelp { get; private set; }
    public bool ShowVersion { get; private set; }

    private readonly List<Uri> _sources = [];
    private readonly List<string> _reports = [];
    private readonly List<string> _roots = [];

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

        if (args[0] == "calibrate")
        {
            return ParseCalibrate(args, options);
        }

        if (args[0] == "mcp")
        {
            return ParseMcp(args, options);
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
                default:
                    if (TryParseScannerOption(args, ref i, options))
                    {
                        break;
                    }

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

    /// <summary>Options shared by scan and mcp: estimation, package resolution and logging.</summary>
    private static bool TryParseScannerOption(string[] args, ref int i, CliOptions options)
    {
        var arg = args[i];
        switch (arg)
        {
            case "--hours-per-point":
                options.HoursPerPoint = ParsePositiveDouble(Next(args, ref i, arg), arg);
                return true;
            case "--rules":
                options.RulesPath = Next(args, ref i, arg);
                return true;
            case "--offline":
                options.Offline = true;
                return true;
            case "--deep":
                options.Deep = true;
                return true;
            case "--nuget-source":
                var raw = Next(args, ref i, arg);
                options._sources.Add(Uri.TryCreate(raw, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http"
                    ? uri
                    : throw new ArgumentException($"Invalid --nuget-source '{raw}': expected an absolute http(s) URL of a V3 service index."));
                return true;
            case "--cache-dir":
                options.CacheDirectory = Next(args, ref i, arg);
                return true;
            case "--no-cache":
                options.NoCache = true;
                return true;
            case "-v" or "--verbosity":
                options.LogLevel = ParseVerbosity(Next(args, ref i, arg));
                return true;
            default:
                return false;
        }
    }

    private static CliOptions ParseMcp(string[] args, CliOptions options)
    {
        options.Command = CliCommand.Mcp;
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            if (arg is "-h" or "--help")
            {
                options.ShowHelp = true;
                return options;
            }

            if (arg == "--root")
            {
                options._roots.Add(Next(args, ref i, arg));
            }
            else if (!TryParseScannerOption(args, ref i, options))
            {
                throw new ArgumentException($"Unknown option '{arg}' for mcp.");
            }
        }

        return options;
    }

    private static CliOptions ParseCalibrate(string[] args, CliOptions options)
    {
        options.Command = CliCommand.Calibrate;
        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-h" or "--help":
                    options.ShowHelp = true;
                    return options;
                case "--actuals":
                    options.ActualsPath = Next(args, ref i, arg);
                    break;
                case "--actual-hours":
                    options.ActualHours = ParsePositiveDouble(Next(args, ref i, arg), arg);
                    break;
                case "-o" or "--output":
                    options.CalibrationOutput = Next(args, ref i, arg);
                    break;
                case "-v" or "--verbosity":
                    options.LogLevel = ParseVerbosity(Next(args, ref i, arg));
                    break;
                default:
                    if (arg.StartsWith('-'))
                    {
                        throw new ArgumentException($"Unknown option '{arg}' for calibrate.");
                    }

                    options._reports.Add(arg);
                    break;
            }
        }

        if (options._reports.Count == 0)
        {
            throw new ArgumentException("calibrate: missing <report.json> (the JSON report of the solution before migration).");
        }

        if ((options.ActualsPath is null) == (options.ActualHours is null))
        {
            throw new ArgumentException("calibrate: specify exactly one of --actuals <file> or --actual-hours <n>.");
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
