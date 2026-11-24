using LegacyMigrationScanner.Core;
using LegacyMigrationScanner.Core.Calibration;
using LegacyMigrationScanner.Core.Reporting;
using LegacyMigrationScanner.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyMigrationScanner.Tests.Calibration;

public sealed class CalibrationTests : IDisposable
{
    private readonly string _dir = TestHelpers.CreateTempDirectory();

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    /// <summary>Scans the fixture and writes its JSON report; returns the path.</summary>
    internal static async Task<string> WriteFixtureReportAsync(string directory)
    {
        await using var services = TestHelpers.BuildServices();
        var result = await services.GetRequiredService<IMigrationScanner>().ScanAsync(TestHelpers.FixtureSolution);
        var path = Path.Combine(directory, "before.json");
        await using (var writer = new StreamWriter(path))
        {
            new JsonReportWriter().Write(result, writer);
        }

        return path;
    }

    private static ReportEstimates Report(string solution, params (string Project, double P50, double P90)[] projects) =>
        new($"{solution}.json", solution, 2, [.. projects.Select(p => new EstimatedProject(solution, p.Project, p.P50, p.P90))]);

    [Fact]
    public void Actuals_SkipHeaderAndComments_SumRepeatedProjects_AcceptSeparators()
    {
        var actuals = ActualEffortFile.Parse([
            "project,hours",
            "# sprint 1",
            "Web, 10.5",
            "",
            "Data;20",
            "web\t4.5",
        ]);

        Assert.Equal(2, actuals.Count);
        Assert.Equal(new ActualEffort("Web", 15), actuals[0]);
        Assert.Equal(new ActualEffort("Data", 20), actuals[1]);
    }

    [Theory]
    [InlineData("Web,10", "Data,ten")]
    [InlineData("Web,10,extra")]
    [InlineData("Web,-1")]
    [InlineData(",5")]
    public void Actuals_MalformedLines_Throw(params string[] lines) =>
        Assert.Throws<InvalidDataException>(() => ActualEffortFile.Parse(lines));

    [Fact]
    public void FromProjects_FitsRatioOfSums_AndReportsAccuracy()
    {
        var report = Report("Shop.sln", ("Web", 50, 80), ("Data", 30, 40), ("Lib", 20, 25));

        var result = Calibrator.FromProjects([report], [new("Web", 150), new("Data", 60), new("Lib", 90)]);

        Assert.Equal(3.0, result.HoursPerPoint); // 300 h / 100 points
        Assert.Equal(2.0, result.PreviousHoursPerPoint);
        var web = result.Projects.Single(p => p.Project == "Web");
        Assert.Equal(150, web.EstimatedHoursP50);
        Assert.Equal(0, web.ErrorShare!.Value, 6);
        var lib = result.Projects.Single(p => p.Project == "Lib");
        Assert.Equal(-1 / 3.0, lib.ErrorShare!.Value, 6); // 60 h estimated vs 90 h actual
        Assert.Equal(0.5, result.Projects.Single(p => p.Project == "Data").ErrorShare!.Value, 6); // 90 vs 60
        Assert.Equal((0 + 0.5 + 1 / 3.0) / 3, result.MeanAbsoluteError!.Value, 6);
        Assert.False(lib.WithinP90); // P90 = 25 × 3 = 75 h < 90 h
        Assert.Equal(2 / 3.0, result.P90Coverage!.Value, 6);
    }

    [Fact]
    public void FromProjects_WarnsAboutUnknownZeroPointAndMissingProjects()
    {
        var report = Report("Shop.sln", ("Web", 50, 80), ("Common", 0, 0), ("Data", 30, 40));

        var result = Calibrator.FromProjects([report], [new("Web", 100), new("Common", 8), new("Ghost", 5)]);

        Assert.Equal(2.16, result.HoursPerPoint); // matched only: (100 + 8) h / (50 + 0) points
        Assert.Contains(result.Warnings, w => w.Contains("'Ghost'", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("'Common'", StringComparison.Ordinal) && w.Contains("0 points", StringComparison.Ordinal));
        Assert.Contains(result.Warnings, w => w.Contains("No actual hours for: Data", StringComparison.Ordinal));
    }

    [Fact]
    public void FromProjects_SameProjectInTwoReports_Throws() =>
        Assert.Throws<InvalidDataException>(() => Calibrator.FromProjects(
            [Report("A.sln", ("Web", 10, 20)), Report("B.sln", ("Web", 10, 20))], [new("Web", 10)]));

    [Fact]
    public void NothingToCalibrate_Throws() =>
        Assert.Throws<InvalidDataException>(() => Calibrator.FromProjects([Report("A.sln", ("Web", 10, 20))], [new("Ghost", 10)]));

    [Fact]
    public void FromTotal_UsesAllReports()
    {
        var result = Calibrator.FromTotal([Report("A.sln", ("Web", 30, 40)), Report("B.sln", ("Api", 20, 30))], 125);

        Assert.Equal(2.5, result.HoursPerPoint);
        Assert.Equal(50, result.TotalPoints);
        Assert.Empty(result.Projects);
        Assert.Null(result.MeanAbsoluteError);
    }

    [Fact]
    public async Task ReportEstimates_LoadsScanJsonReport()
    {
        var report = ReportEstimates.Load(await WriteFixtureReportAsync(_dir));

        Assert.Equal("LegacyShop.sln", report.Solution);
        Assert.Equal(2, report.HoursPerPoint);
        Assert.Equal(4, report.Projects.Count);
        Assert.Contains(report.Projects, p => p.Project == "LegacyShop.Web" && p.PointsP50 > 0 && p.PointsP90 >= p.PointsP50);
    }

    [Fact]
    public void ReportEstimates_RejectsOtherJson()
    {
        var path = Path.Combine(_dir, "other.json");
        File.WriteAllText(path, """{"version":"2.1.0","runs":[]}""");

        Assert.Throws<InvalidDataException>(() => ReportEstimates.Load(path));
    }
}
