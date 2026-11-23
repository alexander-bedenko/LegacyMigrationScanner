using System.Globalization;

namespace LegacyMigrationScanner.Core.Packages.NuGet;

/// <summary>Minimal NuGet/SemVer handling: normalization (as used in flat-container URLs) and ordering.</summary>
public static class NuGetVersion
{
    /// <summary>
    /// Normalizes like NuGet does: drops build metadata, pads to 3 parts, drops a zero 4th part, lower-cases.
    /// "1.0" -> "1.0.0", "6.2.0.0" -> "6.2.0", "2.0.0-Beta+abc" -> "2.0.0-beta".
    /// </summary>
    public static string Normalize(string version)
    {
        ArgumentNullException.ThrowIfNull(version);
        var withoutMetadata = version.Trim().Split('+')[0];
        var dash = withoutMetadata.IndexOf('-', StringComparison.Ordinal);
        var core = dash >= 0 ? withoutMetadata[..dash] : withoutMetadata;
        var prerelease = dash >= 0 ? withoutMetadata[dash..] : "";

        var parts = core.Split('.').Select(p => int.TryParse(p, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : 0).ToList();
        while (parts.Count < 3)
        {
            parts.Add(0);
        }

        if (parts.Count == 4 && parts[3] == 0)
        {
            parts.RemoveAt(3);
        }

        return (string.Join('.', parts.Select(p => p.ToString(CultureInfo.InvariantCulture))) + prerelease).ToLowerInvariant();
    }

    public static bool IsPrerelease(string version) =>
        version?.Split('+')[0].Contains('-', StringComparison.Ordinal) ?? throw new ArgumentNullException(nameof(version));

    /// <summary>Compares numerically; a release sorts after its prereleases; prerelease labels compare ordinally.</summary>
    public static int Compare(string left, string right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        var (leftCore, leftPre) = Split(left);
        var (rightCore, rightPre) = Split(right);
        var byCore = leftCore.CompareTo(rightCore);
        if (byCore != 0)
        {
            return byCore;
        }

        return (leftPre.Length, rightPre.Length) switch
        {
            (0, 0) => 0,
            (0, _) => 1,
            (_, 0) => -1,
            _ => string.Compare(leftPre, rightPre, StringComparison.OrdinalIgnoreCase),
        };
    }

    public static readonly IComparer<string> Comparer = Comparer<string>.Create(Compare);

    private static (Version Core, string Prerelease) Split(string value)
    {
        var normalized = Normalize(value.Trim('[', ']', '(', ')', ' '));
        var dash = normalized.IndexOf('-', StringComparison.Ordinal);
        var core = dash >= 0 ? normalized[..dash] : normalized;
        var parts = core.Split('.').Select(p => int.Parse(p, CultureInfo.InvariantCulture)).Concat([0, 0, 0, 0]).Take(4).ToArray();
        return (new Version(parts[0], parts[1], parts[2], parts[3]), dash >= 0 ? normalized[(dash + 1)..] : "");
    }
}
