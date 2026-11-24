using System.Security.Cryptography;
using System.Text;

namespace LegacyMigrationScanner.Core.Baselines;

/// <summary>
/// Stable identity of a finding across runs (<c>lmsFindingHash/v1</c>): rule, project, location and detail,
/// but not line numbers or occurrence counts, so edits elsewhere in a file do not make a finding "new".
/// The same value is emitted as the SARIF partial fingerprint and in the JSON report.
/// </summary>
public static class FindingFingerprint
{
    public const string Name = "lmsFindingHash/v1";

    /// <param name="ruleId">Rule id, e.g. LMS101.</param>
    /// <param name="project">Project name.</param>
    /// <param name="location">Solution-relative path with '/' separators: the finding's file, else the project file, else the solution file.</param>
    /// <param name="detail">Finding detail (may be null).</param>
    public static string Compute(string ruleId, string project, string location, string? detail) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{ruleId}|{project}|{location}|{detail}")))[..32];
}
