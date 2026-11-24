# Changelog

All notable changes to this project are documented here. Format: [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
versioning: [SemVer](https://semver.org/).

## [Unreleased]

### Added
- `--baseline <report.json>`: diff against a previous JSON report by finding fingerprint (`lmsFindingHash/v1`,
  ignores line numbers and counts). Console summary and Markdown report show new / fixed / unchanged findings,
  SARIF results get `baselineState`, the JSON report a `baseline` object. 0.2.0 reports are fingerprinted on load.
- `--fail-on-new`: exit code 2 when the scan has findings not in the baseline; with `--fail-on`, only new findings
  at or above that severity count.
- `fingerprint` on every finding in the JSON report (same value as the SARIF partial fingerprint).

### Changed
- Repository-local `nuget.config` (nuget.org only) for reproducible restore independent of machine-wide feeds.
- Repository URLs point to `github.com/alexander-bedenko/LegacyMigrationScanner`.
- Dropped the `Microsoft.SourceLink.GitHub` package (pulled vulnerable `Microsoft.Build.Tasks.Git` 8.0.0,
  GHSA-23fw-v26w-5fgq); Source Link is built into the .NET 8+ SDK.

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
