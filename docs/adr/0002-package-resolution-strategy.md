# ADR 0002: Package resolution = curated catalog first, NuGet metadata second

- Status: Accepted
- Date: 2026-11-23

## Context

"Is this package compatible?" has two different answers: *technical* (does any version target netstandard/net5+?)
and *migration intent* (Microsoft.AspNet.WebApi is technically net45-only, but the right action is "replace with
ASP.NET Core", not "find an alternative package").

## Decision

1. `known-packages.json` (embedded, curated) is consulted first and wins.
2. Otherwise query NuGet V3 feeds in configured order: service index -> `PackageBaseAddress/3.0.0` ->
   versions -> nuspec of the referenced version and of the latest stable version. Modern support is inferred from
   nuspec dependency/frameworkReference groups.
3. Results are cached on disk (TTL 7 days; 1 day for "not found"); failures are not cached and degrade to
   `Unknown` (LMS603) without failing the scan.

## Consequences

- Two small requests per package, no nupkg downloads; works with private feeds that implement V3.
- Packages whose nuspec has no framework groups (very old packages) are reported as Unknown rather than guessed.
- Authenticated feeds are not supported yet (roadmap).
