# Changelog

All notable changes to this project are documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
versioning: [SemVer](https://semver.org/).

## [Unreleased]

### Added
- `lms mcp`: Model Context Protocol server over stdio (ADR 0004) for Claude Code, Claude Desktop, VS Code and other
  MCP clients. Read-only tools `scan_solution`, `get_findings`, `explain_finding`, `get_packages` with structured
  output, and the `plan_migration` prompt for an LLM-written phased migration plan. Scan results are cached per
  session; tools only read inside allowed roots (working directory or `--root <dir>`); logs go to stderr.
- `LegacyMigrationScanner.Mcp` library (`IMcpServerBuilder.WithLegacyMigrationScanner()`) to host the tools elsewhere.

## [0.3.0] - 2026-11-24

### Added
- `--baseline <report.json>`: diff against a previous JSON report by finding fingerprint (`lmsFindingHash/v1`,
  ignores line numbers and counts). Console summary and Markdown report show new / fixed / unchanged findings,
  SARIF results get `baselineState`, the JSON report a `baseline` object. 0.2.0 reports are fingerprinted on load.
- `--fail-on-new`: exit code 2 when the scan has findings not in the baseline; with `--fail-on`, only new findings
  at or above that severity count.
- `fingerprint` on every finding in the JSON report (same value as the SARIF partial fingerprint).
- `lms calibrate <report.json>... --actuals <project,hours csv> | --actual-hours <n>`: fits `--hours-per-point`
  from a completed migration (`Σ actual hours / Σ P50 points`), prints per-project error, mean absolute error and
  P90 coverage, warns about unmatched or zero-point projects; `-o` writes the result as JSON.
- HTML report (`-f html`): single-file dashboard with summary tiles, per-project P50/P90 effort bars, migration
  waves, rule summary, filterable findings table (works without JS), packages and baseline changes; no external
  requests, light/dark by system setting.

### Changed
- Dependencies: Microsoft.CodeAnalysis.CSharp 5.12.0, Microsoft.Extensions.* 11.0.0, Microsoft.NET.Test.Sdk 18.12.0,
  xunit.runner.visualstudio 4.0.1, coverlet.collector 10.1.1; GitHub Actions checkout v7, setup-dotnet v6, cache v6, upload-artifact v7.
- Repository-local `nuget.config` (nuget.org only) for reproducible restore independent of machine-wide feeds.
- Repository URLs point to `github.com/alexander-bedenko/LegacyMigrationScanner`.
- Dropped the `Microsoft.SourceLink.GitHub` package (pulled vulnerable `Microsoft.Build.Tasks.Git` 8.0.0,
  GHSA-23fw-v26w-5fgq); Source Link is built into the .NET 8+ SDK.

### Fixed
- Markdown report no longer renders empty code spans (literal backticks) for project-level findings.

### Removed
- Obsolete placeholder test files in the test project root.

## [0.2.0] - 2026-11-23

### Added
- Online package resolution via NuGet V3 (service index -> flat container -> nuspec target frameworks),
  multiple feeds (`--nuget-source`, private feeds), retries on transient errors, graceful degradation.
- File cache of package lookups with TTL (7 days, 1 day for "not found"), scoped per feed list.
- Rule `LMS606` (package not found on configured feeds) and online-driven `LMS601`/`LMS604`.
- SARIF 2.1.0 report (`-f sarif`, `--sarif-root`) for GitHub code scanning, with stable fingerprints.
- Packages section in the Markdown report and `packages` in the JSON report.
- `--offline`, `--cache-dir`, `--no-cache`, `--verbosity`, `--version`; exit code 130 on Ctrl+C.
- CI (build/test on Linux and Windows, format check, pack, tool smoke test), release workflow, Dependabot.

### Changed
- Async, DI-based pipeline (`IMigrationScanner`, `services.AddLegacyMigrationScanner()`), structured logging
  with source-generated `LoggerMessage`, projects analyzed in parallel.
- Central Package Management, SourceLink, deterministic builds, `AnalysisMode=Recommended` with warnings as errors.

## [0.1.0] - 2026-11-23

### Added
- CLI `lms scan` for .sln/.slnx/.csproj without building; JSON and Markdown reports.
- 39 rules: project format, System.Web, WCF, platform APIs, EF6/EDMX, configuration, packages (offline catalog).
- Effort model (log/linear scaling, P50/P90) and migration waves (topological order).
- Synthetic .NET Framework 4.8 fixture solution and xUnit test suite.

[Unreleased]: https://github.com/alexander-bedenko/LegacyMigrationScanner/compare/v0.3.0...HEAD
[0.3.0]: https://github.com/alexander-bedenko/LegacyMigrationScanner/releases/tag/v0.3.0
