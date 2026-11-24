using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace LegacyMigrationScanner.Core.Deep;

/// <summary>What a managed DLL targets and which assemblies it references, read from metadata only.</summary>
public sealed record BinaryInfo(string AssemblyName, string? TargetFramework, IReadOnlyList<string> References)
{
    /// <summary>Built for .NET Standard or modern .NET (TargetFrameworkAttribute, or netstandard/System.Runtime references).</summary>
    public bool IsPortable =>
        TargetFramework is { } tfm
            ? tfm.StartsWith(".NETStandard", StringComparison.OrdinalIgnoreCase) || tfm.StartsWith(".NETCoreApp", StringComparison.OrdinalIgnoreCase)
            : References.Any(r => r is "netstandard" or "System.Runtime") && !References.Contains("mscorlib", StringComparer.OrdinalIgnoreCase);
}

public static class BinaryInspector
{
    /// <summary>Reads assembly metadata; null for native or non-assembly files.</summary>
    public static BinaryInfo? Inspect(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata)
            {
                return null;
            }

            var md = pe.GetMetadataReader();
            if (!md.IsAssembly)
            {
                return null;
            }

            var references = md.AssemblyReferences
                .Select(h => md.GetString(md.GetAssemblyReference(h).Name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return new BinaryInfo(md.GetString(md.GetAssemblyDefinition().Name), ReadTargetFramework(md), references);
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static string? ReadTargetFramework(MetadataReader md)
    {
        foreach (var handle in md.GetAssemblyDefinition().GetCustomAttributes())
        {
            var attribute = md.GetCustomAttribute(handle);
            if (!IsTargetFrameworkAttribute(md, attribute))
            {
                continue;
            }

            var blob = md.GetBlobReader(attribute.Value);
            if (blob.Length >= 2 && blob.ReadUInt16() == 1)
            {
                return blob.ReadSerializedString();
            }
        }

        return null;
    }

    private static bool IsTargetFrameworkAttribute(MetadataReader md, CustomAttribute attribute)
    {
        StringHandle ns, name;
        switch (attribute.Constructor.Kind)
        {
            case HandleKind.MemberReference:
                var parent = md.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
                if (parent.Kind != HandleKind.TypeReference)
                {
                    return false;
                }

                var typeRef = md.GetTypeReference((TypeReferenceHandle)parent);
                (ns, name) = (typeRef.Namespace, typeRef.Name);
                break;
            case HandleKind.MethodDefinition:
                var typeDef = md.GetTypeDefinition(md.GetMethodDefinition((MethodDefinitionHandle)attribute.Constructor).GetDeclaringType());
                (ns, name) = (typeDef.Namespace, typeDef.Name);
                break;
            default:
                return false;
        }

        return md.StringComparer.Equals(name, "TargetFrameworkAttribute") && md.StringComparer.Equals(ns, "System.Runtime.Versioning");
    }
}
