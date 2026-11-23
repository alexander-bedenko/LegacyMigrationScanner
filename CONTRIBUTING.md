# Contributing

## Prerequisites

- .NET SDK 10 (see `global.json`). No Visual Studio or .NET Framework targeting packs are needed.

## Build, test, format

```bash
dotnet build
dotnet test
dotnet format            # CI runs `dotnet format --verify-no-changes`
```

Warnings are errors (`AnalysisMode=Recommended`). Package versions live only in `Directory.Packages.props`.

## Adding a rule

1. Add the rule to `src/LegacyMigrationScanner.Core/Rules/rules.json`: next free id in the category range,
   `title`, `severity`, `basePoints`, `scaling` (`Log` for mechanical fixes, `Linear` for per-unit work), `recommendation`.
2. Detect it in an analyzer (`Analyzers/`). Analyzers must be thread-safe and side-effect free.
3. Add a unit test (inline source/config) and, if useful, extend the fixture in `tests/Fixtures/LegacyShop`
   plus the expectations in `EndToEndTests`.
4. `RuleCatalogTests` fails if an analyzer references an id missing from the catalog.

| Range | Category |
|---|---|
| LMS0xx | Project format |
| LMS1xx | System.Web / ASP.NET |
| LMS2xx | WCF |
| LMS3xx | Platform APIs |
| LMS4xx | Data access |
| LMS5xx | Configuration |
| LMS6xx | Packages |

## Adding a known package

Edit `Packages/known-packages.json`. Exact ids win over prefixes (`Foo.*`); the longest prefix wins among prefixes.
Catalog entries take precedence over NuGet metadata, so only add packages where migration intent matters
(replace / remove / review) or where feed metadata is misleading.

## Commits and releases

- Conventional, small PRs; update `CHANGELOG.md` under **Unreleased**.
- Release: tag `vX.Y.Z` on `main` -> workflow builds, tests, packs, pushes to NuGet (if `NUGET_API_KEY` is set)
  and creates a GitHub release.
