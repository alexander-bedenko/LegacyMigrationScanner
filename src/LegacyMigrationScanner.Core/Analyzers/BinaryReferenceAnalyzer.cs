using LegacyMigrationScanner.Core.Deep;
using LegacyMigrationScanner.Core.Model;

namespace LegacyMigrationScanner.Core.Analyzers;

/// <summary>
/// Deep mode (ADR 0005): inspects vendored DLLs referenced via HintPath (not NuGet packages, which the package rules
/// cover). .NET Framework builds that reference Framework-only assemblies → LMS607; other Framework builds → LMS608.
/// </summary>
public sealed class BinaryReferenceAnalyzer : IProjectAnalyzer
{
    /// <summary>Assemblies with no modern .NET implementation (prefix match on the assembly name).</summary>
    private static readonly string[] FrameworkOnlyAssemblies =
    [
        "System.Web", "System.EnterpriseServices", "System.Runtime.Remoting", "System.Workflow", "System.Activities",
        "System.ServiceModel.Activation", "System.ServiceModel.Web", "System.IdentityModel.Services", "System.Data.Entity",
        "System.Data.Services", "Microsoft.VisualBasic.Compatibility", "System.Management.Instrumentation",
    ];

    /// <summary>Folders whose DLLs come from NuGet or build output rather than being vendored.</summary>
    private static readonly HashSet<string> NonVendoredFolders = new(StringComparer.OrdinalIgnoreCase) { "packages", "bin", "obj" };

    public IEnumerable<Finding> Analyze(ProjectInfo project, AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(context);
        if (context.Deep is null)
        {
            yield break;
        }

        foreach (var reference in project.HintPathReferences)
        {
            if (!IsVendored(context.Relative(reference.FullPath)) || !File.Exists(reference.FullPath) || BinaryInspector.Inspect(reference.FullPath) is not { } binary
                || binary.IsPortable)
            {
                continue;
            }

            var frameworkOnly = binary.References
                .Where(r => FrameworkOnlyAssemblies.Any(f => r.Equals(f, StringComparison.OrdinalIgnoreCase) || r.StartsWith(f + ".", StringComparison.OrdinalIgnoreCase)))
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var file = context.Relative(reference.FullPath);
            var target = binary.TargetFramework ?? ".NET Framework (no TargetFramework attribute)";
            yield return frameworkOnly.Count > 0
                ? context.Rules.Create("LMS607", project.Name, file, detail: $"{binary.AssemblyName}, {target}: references {string.Join(", ", frameworkOnly)}")
                : context.Rules.Create("LMS608", project.Name, file, detail: $"{binary.AssemblyName}, {target}");
        }
    }

    /// <param name="relativePath">Path relative to the solution directory.</param>
    internal static bool IsVendored(string relativePath) =>
        !relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(NonVendoredFolders.Contains);
}
