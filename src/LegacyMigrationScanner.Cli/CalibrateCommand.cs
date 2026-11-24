using System.Globalization;
using System.Text.Json;
using LegacyMigrationScanner.Core.Calibration;
using LegacyMigrationScanner.Core.Reporting;

namespace LegacyMigrationScanner.Cli;

/// <summary><c>lms calibrate</c>: fits hours-per-point from reports of already migrated solutions and actual hours.</summary>
internal static class CalibrateCommand
{
    public static async Task<int> RunAsync(CliOptions options, TextWriter stdout, TextWriter stderr, CancellationToken cancellationToken)
    {
        CalibrationResult result;
        try
        {
            var reports = options.Reports.Select(ReportEstimates.Load).ToList();
            result = options.ActualHours is { } total
                ? Calibrator.FromTotal(reports, total)
                : Calibrator.FromProjects(reports, ActualEffortFile.Load(options.ActualsPath!));

            if (options.CalibrationOutput is { } output)
            {
                var directory = Path.GetDirectoryName(Path.GetFullPath(output));
                if (directory is not null)
                {
                    Directory.CreateDirectory(directory);
                }

                await File.WriteAllTextAsync(output, JsonSerializer.Serialize(result, JsonReportWriter.Options), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await stderr.WriteLineAsync("Calibration cancelled.");
            return ExitCodes.Cancelled;
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidDataException
                                       or IOException or UnauthorizedAccessException)
        {
            await stderr.WriteLineAsync($"error: {ex.Message}");
            return ExitCodes.ScanFailed;
        }

        Print(result, stdout);
        if (options.CalibrationOutput is not null)
        {
            await stdout.WriteLineAsync($"Calibration written: {options.CalibrationOutput}");
        }

        return ExitCodes.Success;
    }

    private static void Print(CalibrationResult r, TextWriter w)
    {
        var inv = CultureInfo.InvariantCulture;
        w.WriteLine(string.Create(inv, $"Calibrated hours per point: {r.HoursPerPoint:0.##}") +
                    (r.PreviousHoursPerPoint is { } previous ? string.Create(inv, $" (reports used {previous:0.##})") : ""));
        w.WriteLine(string.Create(inv, $"Based on {r.TotalActualHours:0.#} actual hours over {r.TotalPoints:0.#} P50 points."));

        if (r.Projects.Count > 0)
        {
            w.WriteLine();
            var nameWidth = Math.Max(7, r.Projects.Max(p => p.Project.Length));
            w.WriteLine($"{"Project".PadRight(nameWidth)}  {"Points",7}  {"Actual h",8}  {"P50 h",7}  {"Error",6}  {"P90 h",7}");
            foreach (var p in r.Projects.OrderByDescending(p => p.ActualHours))
            {
                var error = p.ErrorShare is { } e ? e.ToString("+0%;-0%;0%", inv) : "n/a";
                var mark = p.WithinP90 ? "" : "  > P90";
                w.WriteLine(string.Create(inv,
                    $"{p.Project.PadRight(nameWidth)}  {p.PointsP50,7:0.#}  {p.ActualHours,8:0.#}  {p.EstimatedHoursP50,7:0.#}  {error,6}  {p.EstimatedHoursP90,7:0.#}{mark}"));
            }

            w.WriteLine();
            if (r.MeanAbsoluteError is { } mae)
            {
                w.WriteLine(string.Create(inv, $"Mean absolute error of P50 per project: {mae:0%}"));
            }

            if (r.P90Coverage is { } coverage)
            {
                w.WriteLine(string.Create(inv, $"Actual hours within P90: {coverage:0%} of projects"));
            }
        }

        foreach (var warning in r.Warnings)
        {
            w.WriteLine($"warning: {warning}");
        }

        w.WriteLine();
        w.WriteLine(string.Create(inv, $"Use it: lms scan <path> --hours-per-point {r.HoursPerPoint:0.##}"));
    }
}
