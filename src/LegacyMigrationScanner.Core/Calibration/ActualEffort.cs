using System.Globalization;

namespace LegacyMigrationScanner.Core.Calibration;

/// <summary>Hours actually spent migrating one project (from a time-tracking export).</summary>
public sealed record ActualEffort(string Project, double Hours);

public static class ActualEffortFile
{
    /// <summary>
    /// Reads <c>project,hours</c> lines (also <c>;</c> or tab separated). A header row, empty lines and
    /// <c>#</c> comments are skipped; repeated projects are summed (e.g. one line per sprint).
    /// </summary>
    /// <exception cref="InvalidDataException">A line is malformed.</exception>
    public static IReadOnlyList<ActualEffort> Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Actuals file not found: {path}", path);
        }

        return Parse(File.ReadAllLines(path), path);
    }

    public static IReadOnlyList<ActualEffort> Parse(IEnumerable<string> lines, string source = "actuals")
    {
        ArgumentNullException.ThrowIfNull(lines);
        var totals = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        var order = new List<string>();
        var lineNumber = 0;
        var seenRow = false;
        foreach (var raw in lines)
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split([',', ';', '\t'], StringSplitOptions.TrimEntries);
            if (parts.Length != 2 || parts[0].Length == 0)
            {
                throw new InvalidDataException($"{source}:{lineNumber}: expected 'project,hours', got '{line}'.");
            }

            var isFirstRow = !seenRow;
            seenRow = true;
            if (!double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var hours))
            {
                if (isFirstRow)
                {
                    continue; // header row, e.g. "project,hours"
                }

                throw new InvalidDataException($"{source}:{lineNumber}: '{parts[1]}' is not a number of hours (use '.' as decimal separator).");
            }

            if (hours < 0 || !double.IsFinite(hours))
            {
                throw new InvalidDataException($"{source}:{lineNumber}: hours must be a non-negative number.");
            }

            var project = parts[0].Trim('"');
            if (!totals.ContainsKey(project))
            {
                order.Add(project);
            }

            totals[project] = totals.GetValueOrDefault(project) + hours;
        }

        return [.. order.Select(p => new ActualEffort(p, totals[p]))];
    }
}
