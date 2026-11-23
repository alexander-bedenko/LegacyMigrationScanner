using LegacyMigrationScanner.Core.Packages.NuGet;

namespace LegacyMigrationScanner.Tests.Packages;

public class TargetFrameworkClassifierTests
{
    [Theory]
    [InlineData(".NETStandard2.0", true)]
    [InlineData("netstandard1.3", true)]
    [InlineData(".NETCoreApp3.1", true)]
    [InlineData("net5.0", true)]
    [InlineData("net8.0", true)]
    [InlineData("net10.0-windows7.0", true)]
    [InlineData(".NETFramework4.5", false)]
    [InlineData("net45", false)]
    [InlineData("net481", false)]
    [InlineData("portable-net45+win8", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsModern_ClassifiesShortAndLongForms(string? tfm, bool expected) =>
        Assert.Equal(expected, TargetFrameworkClassifier.IsModern(tfm));

    [Theory]
    [InlineData(".NETStandard2.0", "netstandard2.0")]
    [InlineData(".NETCoreApp3.1", "netcoreapp3.1")]
    [InlineData("NET8.0", "net8.0")]
    public void ToShortName_Normalizes(string tfm, string expected) =>
        Assert.Equal(expected, TargetFrameworkClassifier.ToShortName(tfm));
}
