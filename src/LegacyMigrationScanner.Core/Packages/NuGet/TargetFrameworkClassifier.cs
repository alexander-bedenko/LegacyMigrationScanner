using System.Globalization;
using System.Text.RegularExpressions;

namespace LegacyMigrationScanner.Core.Packages.NuGet;

/// <summary>Decides whether a nuspec target framework (long or short form) can be consumed by modern .NET.</summary>
public static partial class TargetFrameworkClassifier
{
    // Short forms: netstandard2.0, netcoreapp3.1, net8.0, net6.0-windows; long forms: .NETStandard2.0, .NETCoreApp3.1, net5.0
    [GeneratedRegex(@"^(?:\.?netstandard|\.?netcoreapp)\d", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StandardOrCore();

    [GeneratedRegex(@"^net(?<major>\d+)\.\d+(?:-[a-z]+[\d.]*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ModernNet();

    [GeneratedRegex(@"^\.NETStandard(?<v>\d+(?:\.\d+)*)$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex LongNetStandard();

    public static bool IsModern(string? targetFramework)
    {
        if (string.IsNullOrWhiteSpace(targetFramework))
        {
            return false;
        }

        var tfm = targetFramework.Trim();
        if (StandardOrCore().IsMatch(tfm))
        {
            // netstandard1.0 - 1.2 are technically consumable, but such packages usually also need
            // NETStandard.Library shims; still compatible, so accept all netstandard versions.
            return true;
        }

        var modern = ModernNet().Match(tfm);
        return modern.Success && int.Parse(modern.Groups["major"].Value, CultureInfo.InvariantCulture) >= 5;
    }

    /// <summary>True for a modern TFM restricted to Windows (e.g. net8.0-windows).</summary>
    public static bool IsWindowsOnly(string targetFramework) =>
        targetFramework?.Contains("-windows", StringComparison.OrdinalIgnoreCase) ?? false;

    /// <summary>Converts ".NETStandard2.0" to "netstandard2.0" for display; other values are returned lower-cased.</summary>
    public static string ToShortName(string targetFramework)
    {
        ArgumentNullException.ThrowIfNull(targetFramework);
        var match = LongNetStandard().Match(targetFramework);
        if (match.Success)
        {
            return "netstandard" + match.Groups["v"].Value;
        }

        return targetFramework.StartsWith(".NETCoreApp", StringComparison.OrdinalIgnoreCase)
            ? "netcoreapp" + targetFramework[".NETCoreApp".Length..]
            : targetFramework.ToLowerInvariant();
    }
}
