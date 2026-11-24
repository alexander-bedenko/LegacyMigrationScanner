using System.Collections.Concurrent;
using System.Xml;
using LegacyMigrationScanner.Core;
using LegacyMigrationScanner.Core.Model;
using ModelContextProtocol;

namespace LegacyMigrationScanner.Mcp;

/// <summary>
/// Caches scan results per solution path for the lifetime of the server, so follow-up tool calls are instant and
/// consistent with each other. Concurrent requests for the same path share one scan.
/// </summary>
public sealed class ScanSession(IMigrationScanner scanner, PathGuard guard)
{
    private readonly ConcurrentDictionary<string, Lazy<Task<ScanResult>>> _scans = new(PathGuard.PathComparer);

    public async Task<ScanResult> GetAsync(string? path, bool refresh, CancellationToken cancellationToken)
    {
        var fullPath = guard.Resolve(path);
        if (refresh)
        {
            _scans.TryRemove(fullPath, out _);
        }

        // The scan itself is not tied to the caller's token: a cancelled request must not poison the shared result.
        var scan = _scans.GetOrAdd(fullPath, p => new Lazy<Task<ScanResult>>(() => scanner.ScanAsync(p, CancellationToken.None)));
        try
        {
            return await scan.Value.WaitAsync(cancellationToken);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException or InvalidOperationException
                                       or IOException or UnauthorizedAccessException or InvalidDataException or XmlException)
        {
            _scans.TryRemove(new KeyValuePair<string, Lazy<Task<ScanResult>>>(fullPath, scan));
            throw new McpException($"Scan failed: {ex.Message}", ex);
        }
    }
}
