using System.Xml;
using System.Xml.Linq;
using LegacyMigrationScanner.Core.Model;

namespace LegacyMigrationScanner.Core.Analyzers;

/// <summary>Analyzes web.config / app.config for System.Web and WCF configuration.</summary>
public sealed class ConfigAnalyzer : IProjectAnalyzer
{
    // Handlers registered by IIS/ASP.NET templates that need no migration work.
    private static readonly string[] BuiltInHandlerPrefixes = ["ExtensionlessUrlHandler", "OPTIONSVerbHandler", "TRACEVerbHandler"];

    public IEnumerable<Finding> Analyze(ProjectInfo project, AnalysisContext context)
    {
        foreach (var configPath in project.ConfigFiles)
        {
            XDocument doc;
            try
            {
                doc = XDocument.Load(configPath, LoadOptions.SetLineInfo);
            }
            catch (XmlException)
            {
                continue;
            }

            foreach (var finding in AnalyzeDocument(doc, project.Name, context.Relative(configPath), context))
            {
                yield return finding;
            }
        }
    }

    internal static IEnumerable<Finding> AnalyzeDocument(XDocument doc, string project, string file, AnalysisContext context)
    {
        var root = doc.Root;
        if (root is null)
        {
            yield break;
        }

        var rules = context.Rules;

        // Custom configuration sections (system.* sections are framework-owned).
        var customSections = Path(root, "configSections").SelectMany(s => s.Descendants())
            .Where(e => e.Name.LocalName == "section")
            .Where(e => !((string?)e.Attribute("name") ?? "").StartsWith("system.", StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (Make("LMS500", customSections, e => (string?)e.Attribute("name")) is { } sections)
        {
            yield return sections;
        }

        var modules = Path(root, "system.web", "httpModules").Concat(Path(root, "system.webServer", "modules"))
            .SelectMany(Adds)
            .ToList();
        // The same module is usually registered for both classic and integrated pipeline: count distinct types.
        if (Make("LMS102", DistinctBy(modules, "type"), e => (string?)e.Attribute("name")) is { } mod)
        {
            yield return mod;
        }

        var handlers = Path(root, "system.web", "httpHandlers").Concat(Path(root, "system.webServer", "handlers"))
            .SelectMany(Adds)
            .Where(e => !BuiltInHandlerPrefixes.Any(p => ((string?)e.Attribute("name") ?? "").StartsWith(p, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        if (Make("LMS103", DistinctBy(handlers, "type"), e => (string?)e.Attribute("path")) is { } handler)
        {
            yield return handler;
        }

        if (Make("LMS104", Path(root, "system.web", "sessionState").ToList(), e => $"mode={(string?)e.Attribute("mode") ?? "InProc"}") is { } session)
        {
            yield return session;
        }

        var services = Path(root, "system.serviceModel", "services").SelectMany(s => s.Elements())
            .Where(e => e.Name.LocalName == "service").ToList();
        if (Make("LMS201", services, e => (string?)e.Attribute("name")) is { } server)
        {
            yield return server;
        }

        var clientEndpoints = Path(root, "system.serviceModel", "client").SelectMany(s => s.Elements())
            .Where(e => e.Name.LocalName == "endpoint").ToList();
        if (Make("LMS202", clientEndpoints, e => (string?)e.Attribute("contract")) is { } client)
        {
            yield return client;
        }

        Finding? Make(string ruleId, IReadOnlyList<XElement> elements, Func<XElement, string?> describe)
        {
            if (elements.Count == 0)
            {
                return null;
            }

            var names = elements.Select(describe).Where(n => !string.IsNullOrEmpty(n)).Distinct().Take(5);
            return rules.Create(ruleId, project, file, LineOf(elements[0]), elements.Count, string.Join(", ", names));
        }
    }

    private static IEnumerable<XElement> Path(XElement root, params string[] names)
    {
        IEnumerable<XElement> current = [root];
        foreach (var name in names)
        {
            current = current.SelectMany(e => e.Elements()).Where(e => e.Name.LocalName == name);
        }

        return current;
    }

    private static IEnumerable<XElement> Adds(XElement collection) =>
        collection.Elements().Where(e => e.Name.LocalName == "add");

    private static List<XElement> DistinctBy(IEnumerable<XElement> elements, string attribute) =>
        elements.GroupBy(e => (string?)e.Attribute(attribute) ?? (string?)e.Attribute("name") ?? "")
            .Select(g => g.First())
            .ToList();

    private static int? LineOf(XElement element) =>
        element is IXmlLineInfo info && info.HasLineInfo() ? info.LineNumber : null;
}
