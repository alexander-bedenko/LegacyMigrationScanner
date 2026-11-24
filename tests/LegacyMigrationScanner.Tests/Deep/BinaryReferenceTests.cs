using LegacyMigrationScanner.Core;
using LegacyMigrationScanner.Core.Analyzers;
using LegacyMigrationScanner.Core.Deep;
using LegacyMigrationScanner.Core.Model;
using LegacyMigrationScanner.Tests.Infrastructure;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.Extensions.DependencyInjection;

namespace LegacyMigrationScanner.Tests.Deep;

/// <summary>Vendored DLLs are compiled in the test against the net48 reference assemblies — no binaries in the repo.</summary>
public sealed class BinaryReferenceTests : IDisposable
{
    private static readonly Lazy<List<MetadataReference>> Net48 = new(() =>
        [.. DeepModelBuilder.ManagedAssemblies(TestHelpers.Net48ReferenceAssemblies).Select(f => MetadataReference.CreateFromFile(f))]);

    private readonly string _root = TestHelpers.CreateTempDirectory();

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private string EmitLibrary(string relativePath, string targetFramework, string body)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var source = $"[assembly: System.Runtime.Versioning.TargetFramework(\"{targetFramework}\")]\n{body}";
        var compilation = CSharpCompilation.Create(
            Path.GetFileNameWithoutExtension(path),
            [CSharpSyntaxTree.ParseText(source)],
            Net48.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        var emit = compilation.Emit(path);
        Assert.True(emit.Success, string.Join(Environment.NewLine, emit.Diagnostics));
        return path;
    }

    private async Task<ScanResult> ScanAsync(bool deep)
    {
        EmitLibrary("lib/Vendor.Legacy.dll", ".NETFramework,Version=v4.5", "public class A { public System.Web.HttpContext Context; }");
        EmitLibrary("lib/Vendor.Plain.dll", ".NETFramework,Version=v4.0", "public class B { }");
        EmitLibrary("lib/Vendor.Portable.dll", ".NETStandard,Version=v2.0", "public class C { }");
        EmitLibrary("packages/Some.Package.1.0.0/lib/net45/Some.Package.dll", ".NETFramework,Version=v4.5", "public class D { public System.Web.HttpContext Context; }");

        Directory.CreateDirectory(Path.Combine(_root, "App"));
        await File.WriteAllTextAsync(Path.Combine(_root, "App", "Program.cs"), "public static class Program { public static void Main() { new A(); new B(); } }");
        await File.WriteAllTextAsync(Path.Combine(_root, "App", "App.csproj"), """
            <?xml version="1.0" encoding="utf-8"?>
            <Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
              </PropertyGroup>
              <ItemGroup>
                <Reference Include="System" />
                <Reference Include="Vendor.Legacy"><HintPath>..\lib\Vendor.Legacy.dll</HintPath></Reference>
                <Reference Include="Vendor.Plain"><HintPath>..\lib\Vendor.Plain.dll</HintPath></Reference>
                <Reference Include="Vendor.Portable"><HintPath>..\lib\Vendor.Portable.dll</HintPath></Reference>
                <Reference Include="Vendor.Missing"><HintPath>..\lib\Vendor.Missing.dll</HintPath></Reference>
                <Reference Include="Some.Package"><HintPath>..\packages\Some.Package.1.0.0\lib\net45\Some.Package.dll</HintPath></Reference>
              </ItemGroup>
              <ItemGroup>
                <Compile Include="Program.cs" />
              </ItemGroup>
            </Project>
            """);

        await using var services = TestHelpers.BuildServices(o => o.Deep = deep);
        return await services.GetRequiredService<IMigrationScanner>().ScanAsync(Path.Combine(_root, "App", "App.csproj"));
    }

    [Fact]
    public async Task VendoredFrameworkDlls_AreClassified_PackagesAndPortableSkipped()
    {
        var result = await ScanAsync(deep: true);

        var legacy = Assert.Single(result.Findings, f => f.RuleId == "LMS607");
        Assert.Equal("../lib/Vendor.Legacy.dll", legacy.File); // scanned the .csproj: paths are relative to its folder
        Assert.Contains("references System.Web", legacy.Detail, StringComparison.Ordinal);
        Assert.Equal(Severity.Critical, legacy.Severity);

        var plain = Assert.Single(result.Findings, f => f.RuleId == "LMS608");
        Assert.Equal("../lib/Vendor.Plain.dll", plain.File);
        Assert.Contains(".NETFramework,Version=v4.0", plain.Detail, StringComparison.Ordinal);

        Assert.DoesNotContain(result.Findings, f => f.File is not null && (f.File.Contains("Portable", StringComparison.Ordinal) || f.File.Contains("packages/", StringComparison.Ordinal)));
        Assert.Contains(result.Warnings, w => w.Contains("App (1)", StringComparison.Ordinal)); // Vendor.Missing.dll
    }

    [Fact]
    public async Task SyntaxMode_DoesNotInspectBinaries()
    {
        var result = await ScanAsync(deep: false);

        Assert.DoesNotContain(result.Findings, f => f.RuleId is "LMS607" or "LMS608");
    }

    [Fact]
    public void Inspector_ReadsTargetFrameworkAndReferences()
    {
        var info = BinaryInspector.Inspect(EmitLibrary("x/Legacy.dll", ".NETFramework,Version=v4.5", "public class A { public System.Web.HttpContext C; }"))!;

        Assert.Equal("Legacy", info.AssemblyName);
        Assert.Equal(".NETFramework,Version=v4.5", info.TargetFramework);
        Assert.Contains("System.Web", info.References);
        Assert.False(info.IsPortable);

        var notManaged = Path.Combine(_root, "native.dll");
        File.WriteAllBytes(notManaged, [0x4D, 0x5A, 0, 0]);
        Assert.Null(BinaryInspector.Inspect(notManaged));
    }

    [Theory]
    [InlineData("lib/Vendor.dll", true)]
    [InlineData("../shared/ThirdParty/Vendor.dll", true)]
    [InlineData("packages/Vendor.1.0/lib/net45/Vendor.dll", false)]
    [InlineData("App/bin/Debug/Other.dll", false)]
    public void IsVendored_UsesSolutionRelativePath(string relativePath, bool expected) =>
        Assert.Equal(expected, BinaryReferenceAnalyzer.IsVendored(relativePath));
}
