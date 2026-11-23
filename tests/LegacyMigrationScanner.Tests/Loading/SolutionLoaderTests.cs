using LegacyMigrationScanner.Core.Loading;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Tests.Infrastructure;

namespace LegacyMigrationScanner.Tests.Loading;

public class SolutionLoaderTests
{
    [Fact]
    public void ParseSln_ReturnsProjectsAndSkipsSolutionFolders()
    {
        const string sln = """
            Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "A", "src\A\A.csproj", "{1}"
            EndProject
            Project("{2150E333-8FDC-42A3-9474-1A3956D46DE8}") = "Solution Items", "Solution Items", "{2}"
            EndProject
            """;

        var paths = SolutionParser.ParseSln(sln).ToList();

        Assert.Equal("src\\A\\A.csproj, Solution Items", TestHelpers.Join(paths));
    }

    [Fact]
    public void Load_FixtureSolution_ReadsAllProjects()
    {
        var solution = SolutionLoader.Load(TestHelpers.FixtureSolution);

        Assert.Empty(solution.LoadErrors);
        Assert.Equal(
            "LegacyShop.Common, LegacyShop.Data, LegacyShop.Services, LegacyShop.Web",
            TestHelpers.Join(solution.Projects.Select(p => p.Name).OrderBy(n => n)));
    }

    [Fact]
    public void Load_DirectoryWithSingleSolution_ResolvesSolution()
    {
        var solution = SolutionLoader.Load(TestHelpers.FixtureDir);

        Assert.Equal("LegacyShop.sln", Path.GetFileName(solution.FullPath));
    }

    [Fact]
    public void Load_MissingPath_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => SolutionLoader.Load(Path.Combine(TestHelpers.FixtureDir, "nope")));
    }

    [Fact]
    public void Parse_LegacyWebProject_ExtractsMetadata()
    {
        var web = ProjectParser.Parse(Path.Combine(TestHelpers.FixtureDir, "LegacyShop.Web", "LegacyShop.Web.csproj"));

        Assert.False(web.IsSdkStyle);
        Assert.Equal(ProjectKind.AspNetWebApplication, web.Kind);
        Assert.Equal("v4.8", TestHelpers.Join(web.TargetFrameworks));
        Assert.True(web.TargetsNetFramework);
        Assert.Contains("System.Web", web.AssemblyReferences);
        Assert.Contains("System.Web.Http", web.AssemblyReferences);
        Assert.Equal(2, web.ProjectReferences.Count);
        Assert.Equal(14, web.Packages.Count);
        Assert.True(web.Packages.All(p => p.Source == PackageSource.PackagesConfig));
        Assert.Equal(6, web.SourceFiles.Count);
        Assert.Single(web.ConfigFiles);
        Assert.Contains(web.OtherFiles, f => f.EndsWith("Default.aspx", StringComparison.Ordinal));
        Assert.True(web.LinesOfCode > 0);
    }

    [Fact]
    public void Parse_WcfProject_IsClassifiedAsWcf()
    {
        var services = ProjectParser.Parse(Path.Combine(TestHelpers.FixtureDir, "LegacyShop.Services", "LegacyShop.Services.csproj"));

        Assert.Equal(ProjectKind.WcfService, services.Kind);
    }

    [Fact]
    public void Parse_SdkStyleProject_GlobsSourcesAndReadsPackageReferences()
    {
        var common = ProjectParser.Parse(Path.Combine(TestHelpers.FixtureDir, "LegacyShop.Common", "LegacyShop.Common.csproj"));

        Assert.True(common.IsSdkStyle);
        Assert.False(common.TargetsNetFramework);
        Assert.Equal(ProjectKind.ClassLibrary, common.Kind);
        Assert.Single(common.SourceFiles);
        var package = Assert.Single(common.Packages);
        Assert.Equal(new PackageInfo("Newtonsoft.Json", "13.0.3", PackageSource.PackageReference), package);
    }
}
