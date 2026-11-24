# ADR 0005: Deep mode — semantic analysis without MSBuild

- Status: Accepted
- Date: 2026-11-24
- Amends: ADR 0001 (adds an opt-in semantic mode; the default stays syntax-only)

## Context

Syntax-only analysis (ADR 0001) is fast and runs anywhere, but it matches names, not symbols: `using System.Web;`
for `HttpUtility` (still available on modern .NET) is reported as a System.Web dependency, while APIs outside the
hard-coded namespace list go unnoticed. It also cannot look into vendored binaries referenced via `HintPath`.

The roadmap suggested `MSBuildWorkspace`. For .NET Framework projects it needs MSBuild with the .NET Framework
targeting packs (Visual Studio or Build Tools), MSBuildLocator inside a `dotnet tool`, and web targets that are rarely
present on Linux CI. In exactly the environments this tool targets (CI, a laptop without VS), loading would often fail.

## Decision

`--deep` (scan and mcp) builds a Roslyn `CSharpCompilation` per C# project **without MSBuild**:

- **Sources**: the project's Compile items, already parsed by `ProjectParser`.
- **Framework references**: the reference assemblies of the project's `TargetFrameworkVersion`, from the official
  `Microsoft.NETFramework.ReferenceAssemblies.<tfm>` NuGet package, downloaded once through the configured feeds
  (ADR 0002) and cached next to the package cache (`<cache>/refasm`, DLLs only). Offline: cache only.
- **Binary references**: `HintPath` DLLs that exist on disk (restored `packages/` or vendored libraries).
- **Project references**: the referenced projects' compilations.

On top of the compilation:

- **Semantic API check** (`SemanticCodeAnalyzer`): every type used from a .NET Framework assembly is looked up in the
  modern shared framework the tool runs on (public types and forwarders of `Microsoft.NETCore.App`).
  System.Web types that do not exist there produce LMS110 (replacing the syntax rule, so `HttpUtility` alone no
  longer triggers it); other missing types produce **LMS310** — except namespaces already covered by dedicated rules
  (WCF, Remoting, EnterpriseServices, EF6, Drawing, BinaryFormatter) or provided by well-known compatibility packages
  (`System.Configuration.ConfigurationManager`, `System.Data.SqlClient`), to avoid double counting.
- **Binary references** (`BinaryReferenceAnalyzer`): vendored DLLs (not under `packages/`) are read with
  `System.Reflection.Metadata`: built for .NET Framework and referencing .NET Framework-only assemblies → **LMS607**
  (critical); built for .NET Framework otherwise → **LMS608**; .NET Standard / modern .NET → no finding.

A project whose reference assemblies cannot be obtained falls back to syntax analysis, with a scanner warning.
Missing `HintPath` files (packages not restored) are reported as a warning; analysis continues with what resolves.

## Consequences

- Precision improves without new prerequisites; `--deep` needs network once (or a warm cache) and ~30 MB of disk.
- The compilation is approximate: no MSBuild conditions, imports, generated code or conditional symbols beyond
  defaults. Unresolved symbols are simply not reported — deep mode never adds findings on guesses.
- "Available on modern .NET" is judged against the runtime the tool runs on (net10.0), not the user's eventual target.
- Deep and syntax scans can differ (LMS110 suppressed, LMS310/607/608 added): compare baselines from the same mode.
  Reports state the mode.
