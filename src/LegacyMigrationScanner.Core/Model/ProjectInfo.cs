namespace LegacyMigrationScanner.Core.Model;

public sealed record PackageInfo(string Id, string Version, PackageSource Source);

/// <summary>Static description of a project obtained from its .csproj and directory, without building.</summary>
public sealed class ProjectInfo
{
    public required string Name { get; init; }
    /// <summary>Absolute path to the .csproj.</summary>
    public required string FullPath { get; init; }
    public string Directory => Path.GetDirectoryName(FullPath)!;

    public bool IsSdkStyle { get; init; }
    public IReadOnlyList<string> TargetFrameworks { get; init; } = [];
    public IReadOnlyList<string> ProjectTypeGuids { get; init; } = [];
    public ProjectKind Kind { get; set; }
    public string? OutputType { get; init; }

    public IReadOnlyList<string> AssemblyReferences { get; init; } = [];
    /// <summary>Absolute paths of referenced projects.</summary>
    public IReadOnlyList<string> ProjectReferences { get; init; } = [];
    public IReadOnlyList<PackageInfo> Packages { get; init; } = [];

    /// <summary>Absolute paths of C# source files.</summary>
    public IReadOnlyList<string> SourceFiles { get; init; } = [];
    /// <summary>Absolute paths of web.config / app.config (transforms excluded).</summary>
    public IReadOnlyList<string> ConfigFiles { get; init; } = [];
    /// <summary>Absolute paths of other files in the project directory (aspx, svc, edmx, tt, asax ...).</summary>
    public IReadOnlyList<string> OtherFiles { get; init; } = [];

    public int LinesOfCode { get; set; }

    public bool TargetsNetFramework =>
        TargetFrameworks.Any(tf => tf.StartsWith("v", StringComparison.OrdinalIgnoreCase)
                                   || System.Text.RegularExpressions.Regex.IsMatch(tf, @"^net[1-4]\d*$"));
}

public sealed class SolutionInfo
{
    public required string FullPath { get; init; }
    public string Directory => Path.GetDirectoryName(FullPath)!;
    public required IReadOnlyList<ProjectInfo> Projects { get; init; }
    /// <summary>Projects listed in the solution but not found or not parseable.</summary>
    public IReadOnlyList<string> LoadErrors { get; init; } = [];
}
