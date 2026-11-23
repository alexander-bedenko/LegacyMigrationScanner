using System.Xml.Linq;
using LegacyMigrationScanner.Core.Analyzers;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Tests.Infrastructure;

namespace LegacyMigrationScanner.Tests.Analyzers;

public class ConfigAnalyzerTests
{
    private static List<Finding> Analyze(string xml) =>
        ConfigAnalyzer.AnalyzeDocument(XDocument.Parse(xml, LoadOptions.SetLineInfo), "P", "web.config", TestHelpers.Context()).ToList();

    [Fact]
    public void Module_RegisteredForBothPipelines_IsCountedOnce()
    {
        var findings = Analyze("""
            <configuration>
              <system.web><httpModules><add name="A" type="X.A, X" /></httpModules></system.web>
              <system.webServer><modules><add name="A" type="X.A, X" /><add name="B" type="X.B, X" /></modules></system.webServer>
            </configuration>
            """);

        var modules = Assert.Single(findings);
        Assert.Equal("LMS102", modules.RuleId);
        Assert.Equal(2, modules.Count);
        Assert.Equal(2, modules.Line);
    }

    [Fact]
    public void BuiltInIisHandlers_AreIgnored()
    {
        var findings = Analyze("""
            <configuration>
              <system.webServer>
                <handlers>
                  <remove name="ExtensionlessUrlHandler-Integrated-4.0" />
                  <add name="ExtensionlessUrlHandler-Integrated-4.0" path="*." verb="*" type="System.Web.Handlers.TransferRequestHandler" />
                  <add name="OPTIONSVerbHandler" path="*" verb="OPTIONS" modules="ProtocolSupportModule" />
                </handlers>
              </system.webServer>
            </configuration>
            """);

        Assert.Empty(findings);
    }

    [Fact]
    public void Wcf_ServerAndClient_AreReportedSeparately()
    {
        var findings = Analyze("""
            <configuration>
              <system.serviceModel>
                <services><service name="S1" /><service name="S2" /></services>
                <client><endpoint contract="IFoo" /></client>
              </system.serviceModel>
            </configuration>
            """);

        Assert.Equal(2, Assert.Single(findings, f => f.RuleId == "LMS201").Count);
        Assert.Equal("IFoo", Assert.Single(findings, f => f.RuleId == "LMS202").Detail);
    }

    [Fact]
    public void SystemConfigSections_AreNotCustom()
    {
        var findings = Analyze("""
            <configuration>
              <configSections>
                <sectionGroup name="system.web.webPages.razor"><section name="system.web.webPages.razor.pages" type="X" /></sectionGroup>
                <section name="myApp" type="X" />
              </configSections>
              <system.web><sessionState mode="StateServer" /></system.web>
            </configuration>
            """);

        Assert.Equal("myApp", Assert.Single(findings, f => f.RuleId == "LMS500").Detail);
        Assert.Equal("mode=StateServer", Assert.Single(findings, f => f.RuleId == "LMS104").Detail);
    }
}
