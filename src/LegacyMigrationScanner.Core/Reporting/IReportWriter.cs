using LegacyMigrationScanner.Core.Model;

namespace LegacyMigrationScanner.Core.Reporting;

public interface IReportWriter
{
    /// <summary>Format key used on the command line (json, md, sarif).</summary>
    string Format { get; }
    string FileExtension { get; }
    void Write(ScanResult result, TextWriter writer);
}

public sealed record ReportOptions
{
    /// <summary>
    /// Directory SARIF locations are made relative to. GitHub code scanning expects paths relative to
    /// the repository root, so in CI this is the checkout directory (default: current directory).
    /// </summary>
    public string? SarifBaseDirectory { get; init; }
}

public static class ReportWriters
{
    public static IReadOnlyList<string> SupportedFormats { get; } = ["json", "md", "sarif", "html"];

    public static IReportWriter? Create(string format, ReportOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(format);
        return format.ToLowerInvariant() switch
        {
            "json" => new JsonReportWriter(),
            "md" or "markdown" => new MarkdownReportWriter(),
            "html" => new HtmlReportWriter(),
            "sarif" => new SarifReportWriter(options?.SarifBaseDirectory ?? Environment.CurrentDirectory),
            _ => null,
        };
    }
}
