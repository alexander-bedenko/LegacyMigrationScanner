# ADR 0001: Analyze projects without MSBuild evaluation or compilation

- Status: Accepted
- Date: 2026-11-23

## Context

The tool must scan .NET Framework 4.x solutions. Loading them through `MSBuildWorkspace` requires Visual Studio /
Build Tools and .NET Framework targeting packs on the machine, restored packages, and usually Windows. Many target
users want to run the scan in CI on Linux agents or on a laptop without the legacy toolchain.

## Decision

The default mode reads `.sln/.slnx/.csproj/packages.config` as XML and analyzes C# with Roslyn **syntax trees only**
(no `Compilation`, no semantic model).

## Consequences

- Works anywhere the .NET 10 SDK runs; fast (no restore/build); deterministic.
- Detection is name-based: `using System.Web;` + `HttpUtility` yields LMS110 even though `HttpUtility` exists
  on modern .NET; aliases and `global using` in other files are not resolved; MSBuild conditions/imports are ignored.
- A future **deep mode** (roadmap stage 7) can add `MSBuildWorkspace` + semantic model as an opt-in for precision.
