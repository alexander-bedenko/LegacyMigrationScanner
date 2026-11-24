using LegacyMigrationScanner.Core.Model;
using Microsoft.CodeAnalysis;

namespace LegacyMigrationScanner.Core.Deep;

/// <summary>Compilations and lookup data for deep analysis (ADR 0005). Immutable and safe to share across threads.</summary>
public sealed class DeepModel
{
    private readonly IReadOnlyDictionary<string, Compilation> _compilations;
    private readonly IReadOnlySet<string> _frameworkAssemblies;

    public DeepModel(
        IReadOnlyDictionary<string, Compilation> compilations,
        IReadOnlySet<string> frameworkAssemblies,
        ModernApiCatalog modernApis,
        IReadOnlyList<string> warnings)
    {
        _compilations = compilations;
        _frameworkAssemblies = frameworkAssemblies;
        ModernApis = modernApis;
        Warnings = warnings;
    }

    public ModernApiCatalog ModernApis { get; }

    /// <summary>Problems that limited deep analysis (missing reference assemblies, unrestored packages).</summary>
    public IReadOnlyList<string> Warnings { get; }

    public int CompiledProjects => _compilations.Count;

    /// <summary>The project's compilation, or null when it was analyzed with syntax only.</summary>
    public Compilation? GetCompilation(ProjectInfo project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return _compilations.GetValueOrDefault(project.FullPath);
    }

    /// <summary>True for assemblies of the .NET Framework reference set (mscorlib, System.Web, ...).</summary>
    public bool IsFrameworkAssembly(string? assemblyName) => assemblyName is not null && _frameworkAssemblies.Contains(assemblyName);
}
