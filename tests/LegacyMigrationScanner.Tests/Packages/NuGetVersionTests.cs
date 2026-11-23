using LegacyMigrationScanner.Core.Packages.NuGet;

namespace LegacyMigrationScanner.Tests.Packages;

public class NuGetVersionTests
{
    [Theory]
    [InlineData("1.0", "1.0.0")]
    [InlineData("6.2.0.0", "6.2.0")]
    [InlineData("6.2.0.1", "6.2.0.1")]
    [InlineData("2.0.0-Beta+abc", "2.0.0-beta")]
    [InlineData(" 13.0.3 ", "13.0.3")]
    public void Normalize_FollowsNuGetRules(string input, string expected) =>
        Assert.Equal(expected, NuGetVersion.Normalize(input));

    [Theory]
    [InlineData("1.0.0", "1.0.0", 0)]
    [InlineData("1.0", "1.0.0.0", 0)]
    [InlineData("1.10.0", "1.9.0", 1)]
    [InlineData("2.0.0-beta", "2.0.0", -1)]
    [InlineData("2.0.0-alpha", "2.0.0-beta", -1)]
    [InlineData("[6.3.0]", "6.2.9", 1)]
    public void Compare_OrdersNumericallyAndPrereleaseFirst(string left, string right, int expectedSign) =>
        Assert.Equal(expectedSign, Math.Sign(NuGetVersion.Compare(left, right)));

    [Fact]
    public void IsPrerelease_IgnoresBuildMetadata()
    {
        Assert.True(NuGetVersion.IsPrerelease("1.0.0-rc.1"));
        Assert.False(NuGetVersion.IsPrerelease("1.0.0+sha.abc-def"));
    }
}
