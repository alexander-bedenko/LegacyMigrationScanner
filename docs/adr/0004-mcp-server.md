# ADR 0004: MCP server as a subcommand of the same tool

- Status: Accepted
- Date: 2026-11-24

## Context

The scanner produces structured data (findings, estimates, waves, packages) that an AI assistant can turn into a
migration plan, explain a finding in the context of the user's code, or answer "what blocks project X?". The Model
Context Protocol (MCP) is the standard way to expose such data to assistants (Claude Code, Claude Desktop, VS Code,
other MCP clients). We need to choose where the server lives, what it exposes, and how it stays safe.

## Decision

- **Hosting.** `lms mcp` starts an MCP server over **stdio** in the same `dotnet tool`: one install, one version,
  configured like `claude mcp add lms -- lms mcp`. Built on the official C# SDK (`ModelContextProtocol`) and
  `Microsoft.Extensions.Hosting`. Tools, prompt and session live in a separate library
  (`LegacyMigrationScanner.Mcp`) so Core stays free of MCP dependencies and the server can be hosted elsewhere.
- **Stdout is the protocol.** All logging goes to stderr.
- **Tools are read-only** (`ReadOnly = true`, idempotent) and return structured content (JSON) plus text:
  - `scan_solution` — summary: projects with readiness and P50/P90 hours, totals, migration waves, top rules;
  - `get_findings` — paged, filterable findings with fingerprints;
  - `explain_finding` — one finding by fingerprint with its rule (recommendation, effort model, points);
  - `get_packages` — packages that need action.
- **Prompt** `plan_migration` asks the assistant to call the tools and write a phased plan (by migration waves,
  with estimates, risks, quick wins). The LLM does the writing; the server only supplies facts — no model calls or
  API keys in the tool itself.
- **Session cache.** Scan results are cached per solution path for the lifetime of the server (`refresh` re-scans), so
  follow-up calls are instant and consistent with each other.
- **Path guard.** Tools only read inside allowed roots: the working directory by default, or the `--root <dir>`
  options. Relative paths resolve against the first root. Anything outside is rejected with a tool error, so a
  prompt-injected request cannot make the server read arbitrary locations.

## Consequences

- The tool package gains MCP and Hosting dependencies (~a few hundred KB); acceptable for one-install simplicity.
- Network access remains the scanner's NuGet lookups only; `--offline` applies to the server as well.
- HTTP/SSE transport is not provided; it can be added later in the library without changing the tools.
