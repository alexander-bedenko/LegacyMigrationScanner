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
- Analyzers are syntax-only by default (ADR 0001), thread-safe, side-effect free; `--deep` (ADR 0005) adds a Roslyn
  compilation per project via `AnalysisContext.Deep` (no MSBuild) — deep analyzers must be no-ops without it. Package resolution: catalog → cache → NuGet V3 (ADR 0002).
- Logging via `[LoggerMessage]` in `Diagnostics/Log.cs` (stable event ids). Network in tests goes through `FakeNuGetFeed`.
- Every change: tests + `CHANGELOG.md` (Unreleased). Small targeted changes over broad rewrites.
- `tests/Fixtures/**` are scan targets (legacy code on purpose) — not part of the build; keep `EndToEndTests` in sync.

## State (0.5.0, 2026-11-24)

Done: all ROADMAP stages (see `docs/ROADMAP.md`): SARIF, DI/async pipeline, CI/CD, online packages,
`lms calibrate` (4), `-f html` (5b), `--baseline` / `--fail-on-new` (6), `--deep` (7), `lms mcp` MCP server (8).
Published: https://github.com/alexander-bedenko/LegacyMigrationScanner (public). Releases: push tag `vX.Y.Z` →
`release.yml` builds, tests, packs, takes notes from the `## [X.Y.Z]` CHANGELOG section (fails without it) and creates a
GitHub Release with the .nupkg files, and pushes them to NuGet.org via Trusted Publishing (OIDC, `NuGet/login@v1`; policy
on nuget.org for repo + `release.yml`; repo variable `NUGET_USER`). No API key secret exists or is needed.
`workflow_dispatch` with `tag` re-publishes an existing tag.
Repo-local `nuget.config` pins nuget.org (the machine-wide config has a corporate feed that hangs restore).

Design notes:
- `Finding.Fingerprint` (`Core/Baselines/FindingFingerprint`) is set by `MigrationScanner` and reused by SARIF —
  keep `lmsFindingHash/v1` inputs stable (bump to v2 if they must change).
- `lms calibrate` fits only `hoursPerPoint` (ratio of sums); per-rule fitting intentionally out of scope (ADR 0003).
- `HtmlReportWriter` output must stay a single file free of external requests (tested).
- MCP (ADR 0004): tools in `src/LegacyMigrationScanner.Mcp` (read-only, structured content, errors as `McpException`),
  hosted by `lms mcp` (`Cli/McpCommand.cs`). Stdout is the protocol — never write to Console.Out in that path.
  Every path argument goes through `PathGuard`. `McpProtocolTests` runs the real `lms.dll mcp` over stdio.

Release checklist: move Unreleased → `## [X.Y.Z] - date` + link refs at the bottom of CHANGELOG, bump `<Version>` in
`Directory.Build.props`, regenerate `docs/sample-report.md`, merge, then tag the merge commit.

Deep mode (stage 7): code in `Core/Deep/` + `SemanticCodeAnalyzer`/`BinaryReferenceAnalyzer`. Tests use
the restored `Microsoft.NETFramework.ReferenceAssemblies.net48` package (`TestHelpers.Net48ReferenceAssemblies`), never
the network. All ROADMAP stages are done; next ideas go to the ROADMAP backlog.
