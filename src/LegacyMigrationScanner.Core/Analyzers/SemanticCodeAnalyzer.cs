using LegacyMigrationScanner.Core.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LegacyMigrationScanner.Core.Analyzers;

/// <summary>
/// Deep mode (ADR 0005): resolves every type used from a .NET Framework assembly and checks whether it still exists
/// in modern .NET. System.Web types that are gone → LMS110 (types like HttpUtility that still exist no longer count);
/// other missing types without a dedicated rule → LMS310. No-op without a compilation for the project.
/// </summary>
public sealed class SemanticCodeAnalyzer : IProjectAnalyzer
{
    public const string SystemWebRule = "LMS110";
    public const string MissingApiRule = "LMS310";

    // Namespaces with dedicated (syntax) rules or provided by well-known compatibility packages: not double counted.
    private static readonly string[] CoveredNamespaces =
    [
        "System.ServiceModel", "System.Runtime.Remoting", "System.EnterpriseServices", "System.Data.Entity", "System.Drawing",
        "System.Runtime.Serialization.Formatters.Binary", "Microsoft.Owin", "Owin",
        "System.Configuration", "System.Data.SqlClient",
    ];

    public IEnumerable<Finding> Analyze(ProjectInfo project, AnalysisContext context)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(context);
        if (context.Deep is not { } deep || deep.GetCompilation(project) is not { } compilation)
        {
            yield break;
        }

        foreach (var tree in compilation.SyntaxTrees)
        {
            var model = compilation.GetSemanticModel(tree, ignoreAccessibility: true);
            var hits = new Dictionary<string, CodeAnalyzer.Hit>(StringComparer.Ordinal);

            foreach (var name in tree.GetRoot().DescendantNodes().OfType<SimpleNameSyntax>())
            {
                if (name.FirstAncestorOrSelf<UsingDirectiveSyntax>() is not null || IsLeftPartOfQualifiedName(name))
                {
                    continue; // namespace imports are not usages; A.B.C is resolved once, at its rightmost name
                }

                var info = model.GetSymbolInfo(name);
                if (name.Parent is MemberAccessExpressionSyntax access && access.Expression == name && info.Symbol is INamedTypeSymbol)
                {
                    continue; // Type.Member: the member is counted (with its containing type), not the type name too
                }

                if (TypeOf(info) is not { } type || !deep.IsFrameworkAssembly(type.ContainingAssembly?.Name))
                {
                    continue;
                }

                var ns = type.ContainingNamespace?.ToDisplayString() ?? "";
                var fullName = ns.Length == 0 ? type.MetadataName : ns + "." + type.MetadataName;
                if (deep.ModernApis.ContainsType(fullName) || Classify(ns) is not { } ruleId)
                {
                    continue;
                }

                if (!hits.TryGetValue(ruleId, out var hit))
                {
                    hit = new CodeAnalyzer.Hit();
                    hits[ruleId] = hit;
                }

                hit.Count++;
                hit.FirstLine = Math.Min(hit.FirstLine, name.GetLocation().GetLineSpan().StartLinePosition.Line + 1);
                if (hit.Samples.Count < 5)
                {
                    hit.Samples.Add(fullName);
                }
            }

            var file = context.Relative(tree.FilePath);
            foreach (var (ruleId, hit) in hits.OrderBy(h => h.Key, StringComparer.Ordinal))
            {
                yield return context.Rules.Create(ruleId, project.Name, file, hit.FirstLine, hit.Count, string.Join(", ", hit.Samples));
            }
        }
    }

    /// <summary>LMS110 for System.Web, LMS310 for other namespaces, null when another rule or a package covers it.</summary>
    internal static string? Classify(string ns)
    {
        if (ns == "System.Web" || ns.StartsWith("System.Web.", StringComparison.Ordinal))
        {
            return SystemWebRule;
        }

        return CoveredNamespaces.Any(c => ns == c || ns.StartsWith(c + ".", StringComparison.Ordinal)) ? null : MissingApiRule;
    }

    /// <summary>The type a name refers to: the type itself, or the containing type of a member/constructor.</summary>
    private static INamedTypeSymbol? TypeOf(SymbolInfo info)
    {
        var symbol = info.Symbol ?? (info.CandidateSymbols.Length > 0 ? info.CandidateSymbols[0] : null);
        var type = symbol switch
        {
            INamedTypeSymbol named => named,
            IMethodSymbol { ReducedFrom: { } extension } => extension.ContainingType,
            { ContainingType: { } containing } and not INamespaceSymbol => containing,
            _ => null,
        };

        if (type is null || type.TypeKind == TypeKind.Error)
        {
            return null;
        }

        type = type.OriginalDefinition;
        while (type.ContainingType is { } outer)
        {
            type = outer; // catalog holds top-level types
        }

        return type;
    }

    private static bool IsLeftPartOfQualifiedName(SimpleNameSyntax name) =>
        name.Parent is QualifiedNameSyntax q && q.Left == name;
}
