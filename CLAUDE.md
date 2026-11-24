# CLAUDE.md — Legacy Migration Scanner

.NET 10 `dotnet tool` (`lms`) that scans .NET Framework 4.x solutions and reports migration blockers
(System.Web, WCF, packages, platform APIs, EF6) with effort estimates. Owner: Alex (communicates in Russian).
The goal is a production-grade open-source project (portfolio for "legacy .NET → modern .NET + AI").

## Commands

```bash
dotnet build                     # warnings are errors (AnalysisMode=Recommended)
dotnet test
dotnet format --verify-no-changes
dotnet run --project src/LegacyMigrationScanner.Cli -- scan tests/Fixtures/LegacyShop -f json,md,sarif
```

## Conventions

- Package versions only in `Directory.Packages.props` (CPM); no `Version=` in csproj.
- Rules: metadata in `src/LegacyMigrationScanner.Core/Rules/rules.json`, detection in `Analyzers/`.
  Id ranges: LMS0xx project, 1xx System.Web, 2xx WCF, 3xx platform, 4xx data, 5xx config, 6xx packages.
- Analyzers are syntax-only (ADR 0001), thread-safe, side-effect free. Package resolution: catalog → cache → NuGet V3 (ADR 0002).
- Logging via `[LoggerMessage]` in `Diagnostics/Log.cs` (stable event ids). Network in tests goes through `FakeNuGetFeed`.
- Every change: tests + `CHANGELOG.md` (Unreleased). Small targeted changes over broad rewrites.
- `tests/Fixtures/**` are scan targets (legacy code on purpose) — not part of the build; keep `EndToEndTests` in sync.

## State (0.2.0, 2026-11-23)

Done: stages 0–3, SARIF, DI/async pipeline, CI/CD. See `docs/ROADMAP.md`.
Real-package build verified locally: build, 117 tests, format, sample scan all green. Repo-local `nuget.config`
pins nuget.org (the machine-wide config has a corporate feed that hangs restore).

Published: https://github.com/alexander-bedenko/LegacyMigrationScanner (public, `main`); first CI run green
on Linux + Windows incl. pack/smoke test. `NUGET_API_KEY` secret not set yet (release skips NuGet push).

Stage 6 done (Unreleased): `--baseline` / `--fail-on-new`, code in `Core/Baselines/`; `Finding.Fingerprint` is set by
`MigrationScanner` and reused by SARIF — keep `FindingFingerprint` v1 inputs stable (bump to v2 if they must change).

Stage 4 done (Unreleased): `lms calibrate`, code in `Core/Calibration/` and `Cli/CalibrateCommand.cs`. Fits only
`hoursPerPoint` (ratio of sums); per-rule fitting intentionally out of scope (ADR 0003).

Next candidates (ROADMAP): 5b HTML dashboard, 7 deep mode, 8 MCP server.
