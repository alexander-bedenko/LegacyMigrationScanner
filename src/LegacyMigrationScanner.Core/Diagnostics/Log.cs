using Microsoft.Extensions.Logging;

namespace LegacyMigrationScanner.Core.Diagnostics;

/// <summary>High-performance log messages (source-generated). Event ids are stable and documented.</summary>
internal static partial class Log
{
    [LoggerMessage(1000, LogLevel.Information, "Scanning {Path}")]
    public static partial void ScanStarted(ILogger logger, string path);

    [LoggerMessage(1001, LogLevel.Information, "Loaded {ProjectCount} project(s) from {Solution}")]
    public static partial void SolutionLoaded(ILogger logger, int projectCount, string solution);

    [LoggerMessage(1002, LogLevel.Warning, "{Message}")]
    public static partial void SolutionLoadWarning(ILogger logger, string message);

    [LoggerMessage(1003, LogLevel.Information, "Scan completed: {FindingCount} finding(s) in {ElapsedMs} ms")]
    public static partial void ScanCompleted(ILogger logger, int findingCount, long elapsedMs);

    [LoggerMessage(1004, LogLevel.Debug, "Analyzed {Project}: {FindingCount} finding(s)")]
    public static partial void ProjectAnalyzed(ILogger logger, string project, int findingCount);

    [LoggerMessage(2000, LogLevel.Information, "Resolving {Count} package(s) not covered by the catalog via {SourceCount} NuGet source(s)")]
    public static partial void ResolvingPackages(ILogger logger, int count, int sourceCount);

    [LoggerMessage(2001, LogLevel.Information, "Offline mode: {Count} package(s) unknown to the catalog are reported as LMS603")]
    public static partial void OfflineUnknownPackages(ILogger logger, int count);

    [LoggerMessage(2002, LogLevel.Warning, "Lookup of {PackageId} {Version} on {Source} failed: {Error}")]
    public static partial void PackageLookupFailed(ILogger logger, string packageId, string version, Uri source, string error);

    [LoggerMessage(2003, LogLevel.Debug, "{PackageId} {Version}: {Status} ({Origin})")]
    public static partial void PackageResolved(ILogger logger, string packageId, string version, string status, string origin);

    [LoggerMessage(2004, LogLevel.Debug, "Retrying {Url} in {DelayMs} ms (attempt {Attempt}): {Reason}")]
    public static partial void RequestRetry(ILogger logger, Uri url, int delayMs, int attempt, string reason);

    [LoggerMessage(3000, LogLevel.Debug, "Package cache loaded: {EntryCount} entries from {Path}")]
    public static partial void CacheLoaded(ILogger logger, int entryCount, string path);

    [LoggerMessage(3001, LogLevel.Warning, "Package cache at {Path} is unreadable and will be rebuilt: {Error}")]
    public static partial void CacheUnreadable(ILogger logger, string path, string error);

    [LoggerMessage(3002, LogLevel.Warning, "Failed to save package cache to {Path}: {Error}")]
    public static partial void CacheSaveFailed(ILogger logger, string path, string error);
}
