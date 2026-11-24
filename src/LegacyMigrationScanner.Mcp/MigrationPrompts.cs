using System.ComponentModel;
using ModelContextProtocol.Server;

namespace LegacyMigrationScanner.Mcp;

/// <summary>Prompts that turn the scanner's facts into an LLM-written migration plan (ADR 0004).</summary>
[McpServerPromptType]
public static class MigrationPrompts
{
    [McpServerPrompt(Name = "plan_migration", Title = "Plan a migration to modern .NET")]
    [Description("Builds a phased plan for migrating a .NET Framework solution to modern .NET, grounded in Legacy Migration Scanner results.")]
    public static string PlanMigration(
        [Description("Path to the .sln/.slnx/.csproj or the directory with the solution.")] string path,
        [Description("Target framework, e.g. net10.0 (default: net10.0).")] string? target = null,
        [Description("Audience: engineering (default) or management.")] string? audience = null)
    {
        var tfm = string.IsNullOrWhiteSpace(target) ? "net10.0" : target.Trim();
        var forManagement = string.Equals(audience?.Trim(), "management", StringComparison.OrdinalIgnoreCase);
        return $$"""
            Plan the migration of the .NET Framework solution at `{{path}}` to {{tfm}}.

            Use the Legacy Migration Scanner tools; base every statement on their output and say when something is an assumption.
            1. Call `scan_solution` with path `{{path}}`.
            2. Call `get_findings` with minSeverity `critical` for each Red project; use `explain_finding` for the findings that drive
               the estimate (see topCostDrivers) and `get_packages` for package blockers. Read the referenced files when a
               recommendation depends on how the code is written.
            3. Write the plan:
               - Executive summary: total effort as a P50–P90 range in hours and person-days, the main risks, and what can start now.
               - Phases that follow `migrationWaves` (projects in one wave can go in parallel). For each project: readiness,
                 P50/P90 hours, key blockers with the recommended approach (e.g. CoreWCF vs gRPC, YARP + System.Web adapters for
                 incremental ASP.NET migration, EF6 → EF Core), and a definition of done.
               - Quick wins: low-effort items that unblock later phases (SDK-style projects, PackageReference, package upgrades).
               - Risks and unknowns: packages with Unknown/NotFound status, scanner warnings, anything that needs a spike.
               - How to track progress: commit the JSON report as a baseline and gate PRs with `lms scan --baseline <report.json> --fail-on-new`.
            {{(forManagement
                ? "Write for managers: keep technical detail short, emphasise effort ranges, sequencing, risks and decisions needed."
                : "Write for engineers: be concrete about code changes, libraries and the order of work.")}}
            The estimate is a heuristic (points × hours per point); recommend calibrating it with `lms calibrate` against a past migration.
            """;
    }
}
