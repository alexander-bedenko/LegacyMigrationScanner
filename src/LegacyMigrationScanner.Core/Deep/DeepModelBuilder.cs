using System.Globalization;
using LegacyMigrationScanner.Core.Diagnostics;
using LegacyMigrationScanner.Core.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.Logging;

namespace LegacyMigrationScanner.Core.Deep;

public interface IDeepModelBuilder
{
    Task<DeepModel> BuildAsync(SolutionInfo solution, CancellationToken cancellationToken);
}

/// <summary>
/// Builds a <see cref="CSharpCompilation"/> per .NET Framework project without MSBuild (ADR 0005): Compile items,
/// framework reference assemblies, existing HintPath DLLs and referenced projects' compilations.
/// </summary>
public sealed class DeepModelBuilder(IReferenceAssemblyProvider referenceAssemblies, ILogger<DeepModelBuilder> logger) : IDeepModelBuilder
{
    private static readonly CSharpParseOptions ParseOptions = new(
        LanguageVersion.Latest, DocumentationMode.None, SourceCodeKind.Regular, ["NETFRAMEWORK", "TRACE"]);

    private static readonly CSharpCompilationOptions CompilationOptions = new(
        OutputKind.DynamicallyLinkedLibrary, allowUnsafe: true, nullableContextOptions: NullableContextOptions.Disable);

    public async Task<DeepModel> BuildAsync(SolutionInfo solution, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(solution);
        var warnings = new List<string>();
        var frameworkAssemblies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Reference assemblies per framework version (v4.8, v4.7.2, ...), fetched once each.
        var versions = solution.Projects
            .Select(p => (Project: p, Version: FrameworkVersion(p)))
            .Where(x => x.Version is not null)
            .ToList();
        var referenceSets = new Dictionary<string, IReadOnlyList<MetadataReference>?>(StringComparer.OrdinalIgnoreCase);
        foreach (var version in versions.Select(x => x.Version!).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var directory = await referenceAssemblies.GetDirectoryAsync(version, cancellationToken);
            if (directory is null)
            {
                referenceSets[version] = null;
                var affected = versions.Where(x => string.Equals(x.Version, version, StringComparison.OrdinalIgnoreCase)).Select(x => x.Project.Name);
                warnings.Add($"Deep analysis: .NET Framework {version} reference assemblies are unavailable (offline or feed error); " +
                             $"{string.Join(", ", affected)} analyzed with syntax only.");
                continue;
            }

            var files = ManagedAssemblies(directory);
            frameworkAssemblies.UnionWith(files.Select(Path.GetFileNameWithoutExtension).OfType<string>());
            referenceSets[version] = [.. files.Select(f => (MetadataReference)MetadataReference.CreateFromFile(f))];
        }

        var byPath = solution.Projects.ToDictionary(p => p.FullPath, StringComparer.OrdinalIgnoreCase);
        var compilations = new Dictionary<string, Compilation>(StringComparer.OrdinalIgnoreCase);
        var missingHintPaths = new List<string>();
        var inProgress = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        Compilation? Compile(ProjectInfo project)
        {
            if (compilations.TryGetValue(project.FullPath, out var done))
            {
                return done;
            }

            var version = FrameworkVersion(project);
            if (version is null || referenceSets.GetValueOrDefault(version) is not { } frameworkRefs || !inProgress.Add(project.FullPath))
            {
                return null; // not .NET Framework, no reference assemblies, or a reference cycle
            }

            cancellationToken.ThrowIfCancellationRequested();
            var references = new List<MetadataReference>(frameworkRefs);
            var missing = 0;
            foreach (var hint in project.HintPathReferences)
            {
                if (File.Exists(hint.FullPath) && BinaryInspector.Inspect(hint.FullPath) is not null)
                {
                    references.Add(MetadataReference.CreateFromFile(hint.FullPath));
                }
                else
                {
                    missing++;
                }
            }

            foreach (var reference in project.ProjectReferences)
            {
                if (byPath.TryGetValue(reference, out var referenced) && Compile(referenced) is { } compiled)
                {
                    references.Add(compiled.ToMetadataReference());
                }
            }

            var trees = project.SourceFiles
                .Select(file => TryRead(file) is { } text ? CSharpSyntaxTree.ParseText(text, ParseOptions, file, cancellationToken: cancellationToken) : null)
                .OfType<SyntaxTree>();
            var compilation = CSharpCompilation.Create(project.Name, trees, references, CompilationOptions);

            inProgress.Remove(project.FullPath);
            compilations[project.FullPath] = compilation;
            if (missing > 0)
            {
                missingHintPaths.Add(string.Create(CultureInfo.InvariantCulture, $"{project.Name} ({missing})"));
            }

            Log.ProjectCompiled(logger, project.Name, references.Count, missing);
            return compilation;
        }

        foreach (var project in solution.Projects)
        {
            Compile(project);
        }

        if (missingHintPaths.Count > 0)
        {
            warnings.Add("Deep analysis: HintPath references not found, so types from them are not analyzed " +
                         $"(restore NuGet packages for complete results): {string.Join(", ", missingHintPaths)}.");
        }

        return new DeepModel(compilations, frameworkAssemblies, ModernApiCatalog.Current, warnings);
    }

    /// <summary>"v4.8" for legacy projects, "net48" → "v4.8" for SDK-style ones; null if not .NET Framework.</summary>
    internal static string? FrameworkVersion(ProjectInfo project)
    {
        foreach (var tf in project.TargetFrameworks)
        {
            if (NuGetReferenceAssemblyProvider.ToTargetFrameworkMoniker(tf) is not null)
            {
                return tf.StartsWith('v') || tf.StartsWith('V') ? tf : null;
            }

            if (tf.Length is 5 or 6 && tf.StartsWith("net", StringComparison.OrdinalIgnoreCase) && tf[3..].All(char.IsAsciiDigit) && tf[3] is >= '1' and <= '4')
            {
                return "v" + string.Join('.', tf[3..].ToCharArray());
            }
        }

        return null;
    }

    /// <summary>
    /// Managed assemblies in a reference directory (recursively). The reference packages also ship native helpers
    /// (e.g. System.EnterpriseServices.Thunk.dll) that must not be passed to the compiler.
    /// </summary>
    internal static List<string> ManagedAssemblies(string directory) =>
        [.. Directory.EnumerateFiles(directory, "*.dll", SearchOption.AllDirectories).Where(f => BinaryInspector.Inspect(f) is not null)];

    private static string? TryRead(string file)
    {
        try
        {
            return File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
