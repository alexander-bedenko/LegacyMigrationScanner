# Legacy Migration Scanner

[![CI](https://github.com/alexander-bedenko/LegacyMigrationScanner/actions/workflows/ci.yml/badge.svg)](https://github.com/alexander-bedenko/LegacyMigrationScanner/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](LICENSE)

`lms` is a `dotnet tool` that scans a **.NET Framework 4.x** solution and reports what blocks a migration to
**modern .NET**: System.Web / ASP.NET, WCF, incompatible NuGet packages, platform APIs (BinaryFormatter, AppDomains,
Remoting), EF6/EDMX — with a transparent **effort estimate** and a **migration order** for the projects.

- **No build, no Visual Studio.** Projects are read as XML and C# is analyzed with Roslyn syntax trees,
  so it runs on Linux CI agents ([ADR 0001](docs/adr/0001-static-analysis-without-msbuild.md)).
- **Package intelligence.** A curated catalog plus NuGet V3 lookups (nuget.org and private feeds), cached on disk
  ([ADR 0002](docs/adr/0002-package-resolution-strategy.md)).
- **CI-native output.** JSON (canonical), Markdown (step summary), SARIF 2.1.0 (GitHub code scanning), exit-code gates.
- **Explainable estimates.** Per-rule points with log/linear scaling, P50/P90 ranges, calibration via rule overrides
  ([ADR 0003](docs/adr/0003-transparent-effort-model.md)).

See a [sample report](docs/sample-report.md) produced from the bundled fixture solution.

## Quick start

```bash
dotnet tool install -g LegacyMigrationScanner
lms scan C:\src\MyLegacyApp\MyLegacyApp.sln
```

From source:

```bash
dotnet run --project src/LegacyMigrationScanner.Cli -- scan tests/Fixtures/LegacyShop --offline
```

## Usage

```
lms scan <path> [options]
```

| Option | Default | Description |
|---|---|---|
| `-o, --output <dir>` | `lms-report` | Output directory |
| `-f, --format <list>` | `json,md` | `json`, `md`, `sarif` |
| `--sarif-root <dir>` | current dir | Base for SARIF paths (repository root in CI) |
| `--fail-on <severity>` | — | Exit code `2` if findings of `critical` / `warning` / `info` exist |
| `--hours-per-point <n>` | `2` | Hours per effort point |
| `--rules <file>` | — | JSON overriding rule points/severity (calibration) |
| `--offline` | off | Catalog only, no network |
| `--nuget-source <url>` | nuget.org | NuGet V3 service index; repeatable, queried in order |
| `--cache-dir <dir>` / `--no-cache` | `%LOCALAPPDATA%/LegacyMigrationScanner/cache` | Package lookup cache |
| `-v, --verbosity <level>` | `minimal` | `quiet`, `minimal`, `normal`, `detailed` (logs go to stderr) |

Exit codes: `0` success · `1` scan failed · `2` `--fail-on` threshold exceeded · `64` invalid arguments · `130` cancelled.

### GitHub Actions

Ready-to-copy workflow with SARIF upload: [`docs/examples/lms-scan.yml`](docs/examples/lms-scan.yml).

## What it detects

| Area | Rules | Source |
|---|---|---|
| Project format | LMS001–005: legacy csproj, packages.config, .NET Framework TFM, ASP.NET / WCF project types | `.csproj`, `packages.config` |
| System.Web | LMS100–115: Web Forms, Global.asax, HTTP modules/handlers, session, Web API 2, MVC 5, `HttpContext.Current`, OWIN, ASMX | code, config, files |
| WCF | LMS200–212: `.svc`, server/client config, `[ServiceContract]`, `ClientBase<T>`, `ServiceHost` | code, config, files |
| Platform | LMS300–306: Remoting, AppDomains, BinaryFormatter, System.Drawing, Registry, COM+, ConfigurationManager | code |
| Data access | LMS400–402: EDMX, T4, EF6 | code, files |
| Configuration | LMS500: custom config sections | config |
| Packages | LMS600–606: replace / upgrade / remove / unknown / incompatible / review / not found | catalog + NuGet |

Full catalog: [`rules.json`](src/LegacyMigrationScanner.Core/Rules/rules.json).

## Using the engine as a library

```csharp
var services = new ServiceCollection()
    .AddLogging()
    .AddLegacyMigrationScanner(o =>
    {
        o.NuGetSources.Add(new Uri("https://pkgs.dev.azure.com/contoso/_packaging/internal/nuget/v3/index.json"));
        o.HoursPerPoint = 3;
    })
    .BuildServiceProvider();

var result = await services.GetRequiredService<IMigrationScanner>().ScanAsync("MyLegacyApp.sln", ct);
```

## Architecture

```
src/LegacyMigrationScanner.Core
  Loading/       .sln/.slnx/.csproj/packages.config parsing, project classification
  Analyzers/     ProjectStructure, FileArtifact, Config, Code (Roslyn), Package   (parallel per project)
  Packages/      catalog, NuGet V3 client, compatibility service, file cache
  Rules/         rule catalog (embedded JSON) + overrides
  Estimation/    effort model, migration waves
  Reporting/     JSON, Markdown, SARIF
  DependencyInjection/  AddLegacyMigrationScanner()
src/LegacyMigrationScanner.Cli   `lms` composition root
tests/                           xUnit (unit, end-to-end, CLI) + Fixtures/LegacyShop
```

Pipeline: load solution → resolve packages (catalog → cache → feeds) → analyze projects in parallel →
estimate → plan waves → write reports.

## Development

See [CONTRIBUTING.md](CONTRIBUTING.md), [CHANGELOG.md](CHANGELOG.md) and the [roadmap](docs/ROADMAP.md).
