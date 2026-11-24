using System.Globalization;
using System.Text.Json;
using LegacyMigrationScanner.Core.Reporting;

namespace LegacyMigrationScanner.Core.Calibration;

/// <summary>Effort points of one project, as recorded in a JSON report.</summary>
public sealed record EstimatedProject(string Solution, string Project, double PointsP50, double PointsP90);

/// <summary>Estimates read from a JSON report written by <c>lms scan -f json</c>.</summary>
public sealed record ReportEstimates(string Path, string Solution, double HoursPerPoint, IReadOnlyList<EstimatedProject> Projects)
{
    /// <exception cref="InvalidDataException">The file is not a Legacy Migration Scanner JSON report.</exception>
    public static ReportEstimates Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Report not found: {path}", path);
        }

        ReportDto? report;
        try
        {
            using var stream = File.OpenRead(path);
            report = JsonSerializer.Deserialize<ReportDto>(stream, JsonReportWriter.Options);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Report '{path}' is not valid JSON: {ex.Message}", ex);
        }

        if (report?.Solution is null || report.Estimates is null)
        {
            throw new InvalidDataException($"'{path}' is not a Legacy Migration Scanner JSON report (expected 'solution' and 'estimates'). Use the report written by '-f json'.");
        }

        return new ReportEstimates(
            path,
            report.Solution,
            report.HoursPerPoint,
            [.. report.Estimates.Where(e => e.Project is not null).Select(e => new EstimatedProject(report.Solution, e.Project!, e.PointsP50, e.PointsP90))]);
    }

    private sealed record ReportDto(string? Solution, double HoursPerPoint, List<EstimateDto>? Estimates);

    private sealed record EstimateDto(string? Project, double PointsP50, double PointsP90);
}

/// <summary>Estimated vs actual effort for one project after calibration.</summary>
public sealed record CalibratedProject
{
    public required string Solution { get; init; }
    public required string Project { get; init; }
    public required double PointsP50 { get; init; }
    public required double ActualHours { get; init; }
    /// <summary>P50 hours with the calibrated hours-per-point.</summary>
    public required double EstimatedHoursP50 { get; init; }
    public required double EstimatedHoursP90 { get; init; }
    /// <summary>(estimate - actual) / actual; null when actual is 0.</summary>
    public double? ErrorShare { get; init; }
    public bool WithinP90 => ActualHours <= EstimatedHoursP90;
}

public sealed record CalibrationResult
{
    /// <summary>Fitted value for <c>--hours-per-point</c>.</summary>
    public required double HoursPerPoint { get; init; }
    /// <summary>Hours-per-point the reports were produced with (null if they differ).</summary>
    public double? PreviousHoursPerPoint { get; init; }
    public required double TotalPoints { get; init; }
    public required double TotalActualHours { get; init; }
    /// <summary>Per-project comparison; empty when calibrated from a solution total.</summary>
    public required IReadOnlyList<CalibratedProject> Projects { get; init; }
    /// <summary>Mean absolute percentage error of P50 over projects (as a share, 0.25 = 25%); null without per-project actuals.</summary>
    public double? MeanAbsoluteError { get; init; }
    /// <summary>Share of projects whose actual hours are within the P90 estimate.</summary>
    public double? P90Coverage { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// Fits hours-per-point from completed migrations: <c>k = Σ actual hours / Σ P50 points</c> over matched projects.
/// The ratio of sums keeps P50 unbiased in total (what a plan is judged by) and is easy to explain;
/// per-project error and P90 coverage show how far individual projects deviate.
/// </summary>
public static class Calibrator
{
    public static CalibrationResult FromProjects(IReadOnlyList<ReportEstimates> reports, IReadOnlyList<ActualEffort> actuals)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(actuals);

        var warnings = new List<string>();
        var byName = reports.SelectMany(r => r.Projects)
            .GroupBy(p => p.Project, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var matched = new List<(EstimatedProject Estimate, double Hours)>();
        foreach (var actual in actuals)
        {
            if (!byName.TryGetValue(actual.Project, out var candidates))
            {
                warnings.Add($"Project '{actual.Project}' from actuals is not in any report; ignored.");
                continue;
            }

            if (candidates.Count > 1)
            {
                throw new InvalidDataException(
                    $"Project '{actual.Project}' occurs in several reports ({string.Join(", ", candidates.Select(c => c.Solution))}); calibrate them separately.");
            }

            matched.Add((candidates[0], actual.Hours));
        }

        foreach (var (estimate, hours) in matched.Where(m => m.Estimate.PointsP50 == 0 && m.Hours > 0))
        {
            warnings.Add(string.Create(CultureInfo.InvariantCulture,
                $"Project '{estimate.Project}' took {hours:0.#} h but has no findings (0 points): work the rules do not capture."));
        }

        var unmatched = byName.Values.SelectMany(v => v)
            .Where(p => p.PointsP50 > 0 && !matched.Any(m => ReferenceEquals(m.Estimate, p)))
            .Select(p => p.Project)
            .ToList();
        if (unmatched.Count > 0)
        {
            warnings.Add($"No actual hours for: {string.Join(", ", unmatched)}; excluded from calibration.");
        }

        var totalPoints = matched.Sum(m => m.Estimate.PointsP50);
        var totalHours = matched.Sum(m => m.Hours);
        var k = Fit(totalHours, totalPoints);

        var projects = matched.Select(m => new CalibratedProject
        {
            Solution = m.Estimate.Solution,
            Project = m.Estimate.Project,
            PointsP50 = m.Estimate.PointsP50,
            ActualHours = m.Hours,
            EstimatedHoursP50 = Math.Round(m.Estimate.PointsP50 * k, 1),
            EstimatedHoursP90 = Math.Round(m.Estimate.PointsP90 * k, 1),
            ErrorShare = m.Hours > 0 ? (m.Estimate.PointsP50 * k - m.Hours) / m.Hours : null,
        }).ToList();

        var withError = projects.Where(p => p.ErrorShare is not null).ToList();
        return new CalibrationResult
        {
            HoursPerPoint = k,
            PreviousHoursPerPoint = PreviousHoursPerPoint(reports),
            TotalPoints = Math.Round(totalPoints, 1),
            TotalActualHours = totalHours,
            Projects = projects,
            MeanAbsoluteError = withError.Count > 0 ? withError.Average(p => Math.Abs(p.ErrorShare!.Value)) : null,
            P90Coverage = projects.Count > 0 ? (double)projects.Count(p => p.WithinP90) / projects.Count : null,
            Warnings = warnings,
        };
    }

    /// <summary>Calibrates from the total hours spent on all solutions in <paramref name="reports"/>.</summary>
    public static CalibrationResult FromTotal(IReadOnlyList<ReportEstimates> reports, double actualHours)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(actualHours);

        var totalPoints = reports.SelectMany(r => r.Projects).Sum(p => p.PointsP50);
        return new CalibrationResult
        {
            HoursPerPoint = Fit(actualHours, totalPoints),
            PreviousHoursPerPoint = PreviousHoursPerPoint(reports),
            TotalPoints = Math.Round(totalPoints, 1),
            TotalActualHours = actualHours,
            Projects = [],
        };
    }

    private static double Fit(double hours, double points) =>
        points > 0 && hours > 0
            ? Math.Round(hours / points, 2)
            : throw new InvalidDataException("Nothing to calibrate: need projects with both effort points and actual hours greater than zero.");

    private static double? PreviousHoursPerPoint(IReadOnlyList<ReportEstimates> reports)
    {
        var values = reports.Select(r => r.HoursPerPoint).Where(v => v > 0).Distinct().ToList();
        return values.Count == 1 ? values[0] : null;
    }
}
