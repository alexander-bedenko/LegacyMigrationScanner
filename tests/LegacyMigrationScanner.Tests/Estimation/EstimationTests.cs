using LegacyMigrationScanner.Core.Estimation;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Core.Rules;
using LegacyMigrationScanner.Tests.Infrastructure;

namespace LegacyMigrationScanner.Tests.Estimation;

public class EstimationTests
{
    [Fact]
    public void Points_LogScaling_GrowsSlowly_LinearScaling_GrowsLinearly()
    {
        var log = new RuleDefinition { Id = "L", Title = "", Category = FindingCategory.Platform, Severity = Severity.Info, BasePoints = 2 };
        var linear = log with { Scaling = EffortScaling.Linear };

        Assert.Equal(2, RuleCatalog.Points(log, 1), 3);
        Assert.Equal(2 * (1 + 3), RuleCatalog.Points(log, 8), 3);
        Assert.Equal(16, RuleCatalog.Points(linear, 8), 3);
        Assert.Equal(2, RuleCatalog.Points(log, 0), 3);
    }

    [Fact]
    public void ProjectWithoutFindings_IsGreenWithZeroEffort()
    {
        var solution = Solution(Project("A"));

        var estimate = Assert.Single(EffortEstimator.Estimate(solution, [], TestHelpers.Rules, new EstimationOptions()));

        Assert.Equal("Green", estimate.Readiness);
        Assert.Equal(0, estimate.HoursP50);
    }

    [Fact]
    public void CriticalFindings_MakeProjectRed_AndWidenRange()
    {
        var solution = Solution(Project("A"));
        var findings = new[]
        {
            TestHelpers.Rules.Create("LMS302", "A", count: 1),
            TestHelpers.Rules.Create("LMS001", "A"),
        };

        var estimate = Assert.Single(EffortEstimator.Estimate(solution, findings, TestHelpers.Rules, new EstimationOptions { HoursPerPoint = 1 }));

        Assert.Equal("Red", estimate.Readiness);
        Assert.Equal(4, estimate.PointsP50, 1); // LMS302 (3) + LMS001 (1), small project => factors = 1
        Assert.True(estimate.PointsP90 > estimate.PointsP50 * 1.3);
        Assert.Equal("LMS302", estimate.Breakdown[0].RuleId);
    }

    [Fact]
    public void MigrationOrder_FixtureSolution_LibrariesFirst()
    {
        var solution = LegacyMigrationScanner.Core.Loading.SolutionLoader.Load(TestHelpers.FixtureSolution);

        var (waves, warnings) = MigrationOrderPlanner.Plan(solution);

        Assert.Empty(warnings);
        Assert.Equal(3, waves.Count);
        Assert.Equal("LegacyShop.Common", TestHelpers.Join(waves[0]));
        Assert.Equal("LegacyShop.Data", TestHelpers.Join(waves[1]));
        Assert.Equal("LegacyShop.Services, LegacyShop.Web", TestHelpers.Join(waves[2]));
    }

    [Fact]
    public void MigrationOrder_Cycle_IsReported()
    {
        var a = Project("A", "B");
        var b = Project("B", "A");

        var (waves, warnings) = MigrationOrderPlanner.Plan(Solution(a, b));

        Assert.Single(warnings);
        Assert.Equal("A, B", TestHelpers.Join(Assert.Single(waves)));
    }

    private static ProjectInfo Project(string name, params string[] references) => new()
    {
        Name = name,
        FullPath = PathFor(name),
        ProjectReferences = references.Select(PathFor).ToList(),
    };

    private static string PathFor(string name) => Path.Combine(Path.GetTempPath(), "sln", name, name + ".csproj");

    private static SolutionInfo Solution(params ProjectInfo[] projects) => new()
    {
        FullPath = Path.Combine(Path.GetTempPath(), "sln", "S.sln"),
        Projects = projects,
    };
}
