using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;

namespace LegacyMigrationScanner.Core.Deep;

/// <summary>
/// Public top-level types (and type forwarders) of a modern .NET shared framework, used to decide whether a
/// .NET Framework type still exists after migration. Read with System.Reflection.Metadata — nothing is loaded.
/// </summary>
public sealed class ModernApiCatalog
{
    private static readonly Lazy<ModernApiCatalog> CurrentRuntime = new(() => FromDirectory(RuntimeEnvironment.GetRuntimeDirectory()));

    private readonly HashSet<string> _types;

    private ModernApiCatalog(HashSet<string> types, string source)
    {
        _types = types;
        Source = source;
    }

    /// <summary>The shared framework the tool runs on (Microsoft.NETCore.App of the current runtime).</summary>
    public static ModernApiCatalog Current => CurrentRuntime.Value;

    public string Source { get; }

    public int TypeCount => _types.Count;

    /// <summary>True if a public type with this metadata name (e.g. <c>System.Web.HttpUtility</c>) exists.</summary>
    public bool ContainsType(string fullName) => _types.Contains(fullName);

    public static ModernApiCatalog FromDirectory(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        var types = new HashSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, "*.dll"))
        {
            AddTypes(file, types);
        }

        return new ModernApiCatalog(types, directory);
    }

    private static void AddTypes(string file, HashSet<string> types)
    {
        try
        {
            using var stream = File.OpenRead(file);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
            {
                return;
            }

            var md = pe.GetMetadataReader();
            foreach (var handle in md.TypeDefinitions)
            {
                var type = md.GetTypeDefinition(handle);
                if (type.GetDeclaringType().IsNil && (type.Attributes & TypeAttributes.VisibilityMask) == TypeAttributes.Public)
                {
                    types.Add(FullName(md.GetString(type.Namespace), md.GetString(type.Name)));
                }
            }

            foreach (var handle in md.ExportedTypes)
            {
                var exported = md.GetExportedType(handle);
                if (exported.Implementation.Kind != HandleKind.ExportedType)
                {
                    types.Add(FullName(md.GetString(exported.Namespace), md.GetString(exported.Name)));
                }
            }
        }
        catch (BadImageFormatException)
        {
            // Native or malformed file in the framework folder: not a source of managed types.
        }
    }

    private static string FullName(string ns, string name) => ns.Length == 0 ? name : ns + "." + name;
}
