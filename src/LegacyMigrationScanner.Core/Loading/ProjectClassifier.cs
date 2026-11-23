using LegacyMigrationScanner.Core.Model;

namespace LegacyMigrationScanner.Core.Loading;

internal static class ProjectClassifier
{
    // Well-known project type GUIDs (lower-case, no braces).
    public const string WebApplicationGuid = "349c5851-65df-11da-9384-00065b846f21";
    public const string WcfGuid = "3d9ad99f-2412-4246-b90b-4eaa41c64699";
    public const string Mvc3Guid = "e53f8fea-eae0-44a6-8774-ffd645390401";
    public const string Mvc4Guid = "e3e379df-f4c6-4180-9b81-6769533abe47";
    public const string WpfGuid = "60dc8134-eba5-43b8-bcc9-bb4bc16c2548";
    public const string TestGuid = "3ac096d0-a1c2-e12c-1390-a8335801fdab";

    private static readonly string[] TestPackages = ["xunit", "nunit", "MSTest.TestFramework", "Microsoft.NET.Test.Sdk"];

    public static ProjectKind Classify(ProjectInfo p)
    {
        var guids = p.ProjectTypeGuids;
        if (guids.Contains(WcfGuid))
        {
            return ProjectKind.WcfService;
        }

        if (guids.Contains(WebApplicationGuid) || guids.Contains(Mvc3Guid) || guids.Contains(Mvc4Guid))
        {
            return ProjectKind.AspNetWebApplication;
        }

        if (guids.Contains(WpfGuid) || HasReference(p, "PresentationFramework"))
        {
            return ProjectKind.Wpf;
        }

        if (guids.Contains(TestGuid) || p.Packages.Any(pk => TestPackages.Contains(pk.Id, StringComparer.OrdinalIgnoreCase)))
        {
            return ProjectKind.Test;
        }

        if (HasReference(p, "System.Windows.Forms") && string.Equals(p.OutputType, "WinExe", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectKind.WinForms;
        }

        if (string.Equals(p.OutputType, "Exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(p.OutputType, "WinExe", StringComparison.OrdinalIgnoreCase))
        {
            return ProjectKind.Console;
        }

        return ProjectKind.ClassLibrary;
    }

    private static bool HasReference(ProjectInfo p, string name) =>
        p.AssemblyReferences.Contains(name, StringComparer.OrdinalIgnoreCase);
}
