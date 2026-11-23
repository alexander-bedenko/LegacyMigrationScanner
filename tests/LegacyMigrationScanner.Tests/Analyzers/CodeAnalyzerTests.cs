using LegacyMigrationScanner.Core.Analyzers;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Tests.Infrastructure;

namespace LegacyMigrationScanner.Tests.Analyzers;

public class CodeAnalyzerTests
{
    private static List<Finding> Analyze(string source) =>
        CodeAnalyzer.AnalyzeSource(source, "P", "File.cs", TestHelpers.Context()).ToList();

    [Fact]
    public void HttpContextCurrent_IsCountedPerOccurrence_WithFirstLine()
    {
        var findings = Analyze("""
            using System.Web;
            class C
            {
                void M()
                {
                    var a = HttpContext.Current.User;
                    var b = HttpContext.Current.Session["x"];
                }
            }
            """);

        var hit = Assert.Single(findings, f => f.RuleId == "LMS113");
        Assert.Equal(2, hit.Count);
        Assert.Equal(6, hit.Line);
        Assert.Equal(Severity.Critical, hit.Severity);
    }

    [Fact]
    public void FullyQualifiedSystemWeb_IsDetectedWithoutUsing()
    {
        var findings = Analyze("""
            class C
            {
                string M() => System.Web.HttpContext.Current.Request.UserHostAddress;
            }
            """);

        Assert.Contains(findings, f => f.RuleId == "LMS110");
        Assert.Contains(findings, f => f.RuleId == "LMS113");
    }

    [Theory]
    [InlineData("using System.Web.Http;", "LMS111")]
    [InlineData("using System.Web.Mvc;", "LMS112")]
    [InlineData("using System.Web;", "LMS110")]
    [InlineData("using Microsoft.Owin;", "LMS114")]
    [InlineData("using System.Runtime.Remoting.Channels;", "LMS300")]
    [InlineData("using System.Data.Entity;", "LMS402")]
    [InlineData("using System.EnterpriseServices;", "LMS305")]
    public void UsingDirective_MapsToRule(string usingLine, string expectedRule)
    {
        var findings = Analyze(usingLine + "\nclass C {}");

        var finding = Assert.Single(findings);
        Assert.Equal(expectedRule, finding.RuleId);
    }

    [Fact]
    public void SystemWebPrefix_DoesNotMatchSimilarNamespace()
    {
        Assert.Empty(Analyze("using System.WebSockets;\nusing Microsoft.Owinish;\nclass C {}"));
    }

    [Fact]
    public void StringsAndComments_AreIgnored()
    {
        Assert.Empty(Analyze("""
            // HttpContext.Current and System.Web are mentioned here
            class C { string S = "HttpContext.Current System.Web.Http"; }
            """));
    }

    [Fact]
    public void WcfConstructs_AreDetected()
    {
        var findings = Analyze("""
            using System.ServiceModel;
            [ServiceContract] interface IA { [OperationContract] void M(); }
            [ServiceContractAttribute] interface IB { }
            class Client : ClientBase<IA> { }
            class Host { void Run() { var h = new ServiceHost(typeof(object)); var f = new ChannelFactory<IA>(); } }
            """);

        var contracts = Assert.Single(findings, f => f.RuleId == "LMS210");
        Assert.Equal(2, contracts.Count);
        Assert.Equal("IA, IB", contracts.Detail);
        Assert.Equal(2, Assert.Single(findings, f => f.RuleId == "LMS211").Count);
        Assert.Single(findings, f => f.RuleId == "LMS212");
    }

    [Fact]
    public void PlatformApis_AreDetected()
    {
        var findings = Analyze("""
            using Microsoft.Win32;
            class C : System.Web.IHttpModule
            {
                void M()
                {
                    var d = System.AppDomain.CreateDomain("x");
                    var f = new System.Runtime.Serialization.Formatters.Binary.BinaryFormatter();
                    var k = Registry.CurrentUser;
                    var s = System.Configuration.ConfigurationManager.AppSettings["a"];
                }
            }
            """);

        var ids = findings.Select(f => f.RuleId).Distinct().ToList();
        foreach (var expected in new[] { "LMS102", "LMS301", "LMS302", "LMS304", "LMS306" })
        {
            Assert.Contains(expected, ids);
        }
    }

    [Fact]
    public void NamespaceRule_IsCountedOncePerFile()
    {
        var findings = Analyze("""
            using System.Web;
            class C { System.Web.HttpRequest R; System.Web.HttpResponse S; }
            """);

        Assert.Equal(1, Assert.Single(findings, f => f.RuleId == "LMS110").Count);
    }
}
