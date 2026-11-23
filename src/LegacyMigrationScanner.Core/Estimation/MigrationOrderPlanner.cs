using LegacyMigrationScanner.Core.Model;

namespace LegacyMigrationScanner.Core.Estimation;

/// <summary>
/// Orders projects into migration waves (Kahn's topological sort): a project can be migrated
/// once everything it references inside the solution is already migrated.
/// </summary>
public static class MigrationOrderPlanner
{
    public static (IReadOnlyList<IReadOnlyList<string>> Waves, IReadOnlyList<string> Warnings) Plan(SolutionInfo solution)
    {
        var byPath = solution.Projects.ToDictionary(p => p.FullPath, StringComparer.OrdinalIgnoreCase);
        var remainingDeps = solution.Projects.ToDictionary(
            p => p.FullPath,
            p => new HashSet<string>(p.ProjectReferences.Where(byPath.ContainsKey), StringComparer.OrdinalIgnoreCase),
            StringComparer.OrdinalIgnoreCase);

        var waves = new List<IReadOnlyList<string>>();
        var warnings = new List<string>();

        while (remainingDeps.Count > 0)
        {
            var ready = remainingDeps.Where(kv => kv.Value.Count == 0).Select(kv => kv.Key).ToList();
            if (ready.Count == 0)
            {
                var cycle = remainingDeps.Keys.Select(k => byPath[k].Name).OrderBy(n => n).ToList();
                warnings.Add($"Circular project references detected between: {string.Join(", ", cycle)}");
                waves.Add(cycle);
                break;
            }

            waves.Add(ready.Select(k => byPath[k].Name).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList());
            foreach (var key in ready)
            {
                remainingDeps.Remove(key);
            }

            foreach (var deps in remainingDeps.Values)
            {
                deps.ExceptWith(ready);
            }
        }

        return (waves, warnings);
    }
}
