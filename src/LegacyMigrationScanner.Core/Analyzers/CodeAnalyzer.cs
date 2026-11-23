using LegacyMigrationScanner.Core.Model;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace LegacyMigrationScanner.Core.Analyzers;

/// <summary>
/// Syntax-level C# analysis (no compilation / semantic model required), so it works on any
/// machine without restoring packages or having .NET Framework targeting packs installed.
/// </summary>
public sealed class CodeAnalyzer : IProjectAnalyzer
{
    public IEnumerable<Finding> Analyze(ProjectInfo project, AnalysisContext context)
    {
        foreach (var file in project.SourceFiles)
        {
            string text;
            try
            {
                text = File.ReadAllText(file);
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var finding in AnalyzeSource(text, project.Name, context.Relative(file), context))
            {
                yield return finding;
            }
        }
    }

    internal static IEnumerable<Finding> AnalyzeSource(string source, string project, string file, AnalysisContext context)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var walker = new LegacyApiWalker();
        walker.Visit(tree.GetRoot());

        return walker.Hits
            .OrderBy(h => h.Key, StringComparer.Ordinal)
            .Select(h => context.Rules.Create(h.Key, project, file, h.Value.FirstLine, h.Value.Count,
                h.Value.Samples.Count > 0 ? string.Join(", ", h.Value.Samples) : null));
    }

    internal sealed class Hit
    {
        public int Count;
        public int FirstLine = int.MaxValue;
        public SortedSet<string> Samples { get; } = new(StringComparer.Ordinal);
    }

    private sealed class LegacyApiWalker : CSharpSyntaxWalker
    {
        // Rules triggered by a using directive count once per file.
        private static readonly (string Prefix, string RuleId, bool ExactOrChild)[] NamespaceRules =
        [
            ("System.Web.Http", "LMS111", true),
            ("System.Web.Mvc", "LMS112", true),
            ("System.Web", "LMS110", true),
            ("Microsoft.Owin", "LMS114", true),
            ("Owin", "LMS114", true),
            ("System.Runtime.Remoting", "LMS300", true),
            ("System.EnterpriseServices", "LMS305", true),
            ("System.Data.Entity", "LMS402", true),
            ("System.Drawing", "LMS303", true),
            ("System.Runtime.Serialization.Formatters.Binary", "LMS302", true),
        ];

        private readonly HashSet<string> _usingRulesInFile = new(StringComparer.Ordinal);

        public Dictionary<string, Hit> Hits { get; } = new(StringComparer.Ordinal);

        public override void VisitUsingDirective(UsingDirectiveSyntax node)
        {
            var ns = node.NamespaceOrType.ToString();
            var rule = MatchNamespace(ns);
            if (rule is not null && _usingRulesInFile.Add(rule))
            {
                Add(rule, node, ns);
            }
            // Do not descend: names inside using directives are not code usages.
        }

        public override void VisitQualifiedName(QualifiedNameSyntax node)
        {
            if (node.Parent is not QualifiedNameSyntax)
            {
                CheckFullyQualified(node.ToString(), node);
            }

            base.VisitQualifiedName(node);
        }

        public override void VisitMemberAccessExpression(MemberAccessExpressionSyntax node)
        {
            var member = node.Name.Identifier.ValueText;
            var target = LastIdentifier(node.Expression);

            switch (target, member)
            {
                case ("HttpContext", "Current"):
                    Add("LMS113", node);
                    break;
                case ("AppDomain", "CreateDomain"):
                    Add("LMS301", node);
                    break;
                case ("ConfigurationManager", _):
                    Add("LMS306", node, member);
                    break;
                case ("Registry", "LocalMachine" or "CurrentUser" or "ClassesRoot" or "Users" or "CurrentConfig"):
                    Add("LMS304", node, member);
                    break;
            }

            if (node.Parent is not MemberAccessExpressionSyntax)
            {
                CheckFullyQualified(node.ToString(), node);
            }

            base.VisitMemberAccessExpression(node);
        }

        public override void VisitAttribute(AttributeSyntax node)
        {
            switch (StripAttributeSuffix(LastIdentifier(node.Name)))
            {
                case "ServiceContract":
                    var owner = node.FirstAncestorOrSelf<TypeDeclarationSyntax>()?.Identifier.ValueText;
                    Add("LMS210", node, owner);
                    break;
                case "OwinStartup":
                    Add("LMS114", node);
                    break;
            }

            base.VisitAttribute(node);
        }

        public override void VisitObjectCreationExpression(ObjectCreationExpressionSyntax node)
        {
            switch (LastIdentifier(node.Type))
            {
                case "BinaryFormatter":
                    Add("LMS302", node);
                    break;
                case "ServiceHost" or "WebServiceHost":
                    Add("LMS212", node);
                    break;
            }

            base.VisitObjectCreationExpression(node);
        }

        public override void VisitGenericName(GenericNameSyntax node)
        {
            if (node.Identifier.ValueText is "ClientBase" or "ChannelFactory" or "DuplexClientBase")
            {
                Add("LMS211", node, node.Identifier.ValueText);
            }

            base.VisitGenericName(node);
        }

        public override void VisitBaseList(BaseListSyntax node)
        {
            foreach (var baseType in node.Types)
            {
                switch (LastIdentifier(baseType.Type))
                {
                    case "IHttpModule":
                        Add("LMS102", baseType);
                        break;
                    case "IHttpHandler" or "IHttpAsyncHandler":
                        Add("LMS103", baseType);
                        break;
                    case "MarshalByRefObject":
                        Add("LMS300", baseType);
                        break;
                    case "ServicedComponent":
                        Add("LMS305", baseType);
                        break;
                }
            }

            base.VisitBaseList(node);
        }

        private void CheckFullyQualified(string text, SyntaxNode node)
        {
            if (!text.Contains('.'))
            {
                return;
            }

            var rule = MatchNamespace(text);
            if (rule is not null && rule != "LMS302" && _usingRulesInFile.Add(rule))
            {
                Add(rule, node, text.Length > 60 ? text[..60] : text);
            }
        }

        private static string? MatchNamespace(string ns)
        {
            foreach (var (prefix, ruleId, _) in NamespaceRules)
            {
                if (ns == prefix || ns.StartsWith(prefix + ".", StringComparison.Ordinal))
                {
                    return ruleId;
                }
            }

            return null;
        }

        private void Add(string ruleId, SyntaxNode node, string? sample = null)
        {
            if (!Hits.TryGetValue(ruleId, out var hit))
            {
                hit = new Hit();
                Hits[ruleId] = hit;
            }

            hit.Count++;
            var line = node.GetLocation().GetLineSpan().StartLinePosition.Line + 1;
            hit.FirstLine = Math.Min(hit.FirstLine, line);
            if (!string.IsNullOrEmpty(sample) && hit.Samples.Count < 5)
            {
                hit.Samples.Add(sample);
            }
        }

        private static string StripAttributeSuffix(string name) =>
            name.EndsWith("Attribute", StringComparison.Ordinal) ? name[..^"Attribute".Length] : name;

        private static string LastIdentifier(SyntaxNode node) => node switch
        {
            IdentifierNameSyntax id => id.Identifier.ValueText,
            GenericNameSyntax g => g.Identifier.ValueText,
            QualifiedNameSyntax q => LastIdentifier(q.Right),
            AliasQualifiedNameSyntax a => LastIdentifier(a.Name),
            MemberAccessExpressionSyntax m => m.Name.Identifier.ValueText,
            _ => node.ToString(),
        };
    }
}
