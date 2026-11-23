using System.Globalization;
using LegacyMigrationScanner.Core.Model;

namespace LegacyMigrationScanner.Cli;

internal static class ConsoleSummary
{
    public static void Print(ScanResult result, TextWriter output)
    {
        var inv = CultureInfo.InvariantCulture;
        var critical = result.Findings.Count(f => f.Severity == Severity.Critical);

        output.WriteLine();
        output.WriteLine(string.Create(inv, $"{result.Solution}: {result.Projects.Count} projects, {result.Findings.Count} findings ({critical} critical)"));
        output.WriteLine(string.Create(inv, $"Estimated effort: P50 {result.TotalHoursP50:0} h, P90 {result.TotalHoursP90:0} h"));
        output.WriteLine();

        var nameWidth = Math.Max(7, result.Estimates.Select(e => e.Project.Length).DefaultIfEmpty(7).Max());
        output.WriteLine($"{"Project".PadRight(nameWidth)}  {"Readiness",-9}  {"P50 h",6}  {"P90 h",6}");
        var colored = ReferenceEquals(output, Console.Out) && !Console.IsOutputRedirected;
        foreach (var e in result.Estimates.OrderByDescending(e => e.HoursP50))
        {
            if (colored)
            {
                Console.ForegroundColor = e.Readiness switch
                {
                    "Red" => ConsoleColor.Red,
                    "Yellow" => ConsoleColor.Yellow,
                    _ => ConsoleColor.Green,
                };
            }

            output.WriteLine(string.Create(inv, $"{e.Project.PadRight(nameWidth)}  {e.Readiness,-9}  {e.HoursP50,6}  {e.HoursP90,6}"));
            if (colored)
            {
                Console.ResetColor();
            }
        }

        foreach (var warning in result.Warnings)
        {
            output.WriteLine($"warning: {warning}");
        }
    }
}
