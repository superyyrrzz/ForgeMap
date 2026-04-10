# Multi-Roslyn Targeting for ForgeMap Source Generator

**Date:** 2026-04-10
**Status:** Approved

## Problem

ForgeMap's source generator currently ships a single analyzer DLL compiled against one Roslyn version (`Microsoft.CodeAnalysis.CSharp` 4.12.0). Consumers using a .NET SDK whose bundled Roslyn is older than 4.12.0 (e.g., .NET 8 SDK ships Roslyn 4.8.x) cannot load the generator. The only workaround today is to pin the generator to the oldest supported Roslyn version, which permanently blocks access to newer Roslyn APIs.

## Solution

Ship **three analyzer variants** in the NuGet package, one per supported .NET SDK:

| .NET SDK | Roslyn version | Analyzer folder |
|----------|---------------|-----------------|
| .NET 8   | 4.8.x         | `analyzers/roslyn4.8/dotnet/cs/` |
| .NET 9   | 4.12.x        | `analyzers/roslyn4.12/dotnet/cs/` |
| .NET 10  | 5.0.x         | `analyzers/roslyn5.0/dotnet/cs/` |

The .NET SDK **natively supports** the `analyzers/roslyn{VERSION}/` convention. The SDK's `_ResolveCompilerVersion` target (in `Microsoft.PackageDependencyResolution.targets`) reads the bundled Roslyn compiler version and sets `CompilerApiVersion` to `roslyn{Major}.{Minor}`. NuGet asset resolution then picks the highest matching folder. No custom `.targets` file is needed for version selection.

All three builds produce **identical behavior** — same source code, same generated output. The only difference is which Roslyn binary they're compiled against.

## NuGet Package Layout

```
ForgeMap.nupkg/
├── lib/
│   └── netstandard2.0/
│       ├── ForgeMap.dll
│       └── ForgeMap.Abstractions.dll
├── analyzers/
│   ├── roslyn4.8/dotnet/cs/
│   │   ├── ForgeMap.Generator.dll
│   │   └── ForgeMap.Abstractions.dll
│   ├── roslyn4.12/dotnet/cs/
│   │   ├── ForgeMap.Generator.dll
│   │   └── ForgeMap.Abstractions.dll
│   └── roslyn5.0/dotnet/cs/
│       ├── ForgeMap.Generator.dll
│       └── ForgeMap.Abstractions.dll
```

## Generator Project Changes

### New per-version props files

Three new files in `src/ForgeMap.Generator/`:

**`ForgeMap.Generator.Roslyn4.8.props`:**
```xml
<Project>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" VersionOverride="4.8.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

**`ForgeMap.Generator.Roslyn4.12.props`:**
```xml
<Project>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" VersionOverride="4.12.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

**`ForgeMap.Generator.Roslyn5.0.props`:**
```xml
<Project>
  <ItemGroup>
    <PackageReference Include="Microsoft.CodeAnalysis.CSharp" VersionOverride="5.0.0" PrivateAssets="all" />
  </ItemGroup>
</Project>
```

`VersionOverride` is required because the repo uses central package management (`Directory.Packages.props`).

### Changes to ForgeMap.Generator.csproj

- Add a `ROSLYN_VERSION` property defaulting to `5.0` (latest, for local dev)
- Import the version-specific `.props` file: `<Import Project="ForgeMap.Generator.Roslyn$(ROSLYN_VERSION).props" />`
- Remove the direct `<PackageReference Include="Microsoft.CodeAnalysis.CSharp" />` (it moves into the `.props` files)

## ForgeMap.csproj Packaging Changes

Replace the current `IncludePackageContent` target (which copies a single generator DLL) with one that includes all 3 Roslyn variants:

```xml
<Target Name="IncludePackageContent">
  <ItemGroup>
    <TfmSpecificPackageFile Include="$(OutDir)ForgeMap.Abstractions.dll"
                            PackagePath="lib/netstandard2.0" />

    <TfmSpecificPackageFile Include="$(GeneratorArtifactsDir)\roslyn4.8\ForgeMap.Generator.dll"
                            PackagePath="analyzers/roslyn4.8/dotnet/cs" />
    <TfmSpecificPackageFile Include="$(GeneratorArtifactsDir)\roslyn4.8\ForgeMap.Abstractions.dll"
                            PackagePath="analyzers/roslyn4.8/dotnet/cs" />

    <TfmSpecificPackageFile Include="$(GeneratorArtifactsDir)\roslyn4.12\ForgeMap.Generator.dll"
                            PackagePath="analyzers/roslyn4.12/dotnet/cs" />
    <TfmSpecificPackageFile Include="$(GeneratorArtifactsDir)\roslyn4.12\ForgeMap.Abstractions.dll"
                            PackagePath="analyzers/roslyn4.12/dotnet/cs" />

    <TfmSpecificPackageFile Include="$(GeneratorArtifactsDir)\roslyn5.0\ForgeMap.Generator.dll"
                            PackagePath="analyzers/roslyn5.0/dotnet/cs" />
    <TfmSpecificPackageFile Include="$(GeneratorArtifactsDir)\roslyn5.0\ForgeMap.Abstractions.dll"
                            PackagePath="analyzers/roslyn5.0/dotnet/cs" />
  </ItemGroup>
</Target>
```

`$(GeneratorArtifactsDir)` is set by the pack script.

## Pack Script

New `build/pack.sh` (with PowerShell equivalent for Windows CI):

```bash
#!/usr/bin/env bash
set -euo pipefail

roslyn_versions=("4.8" "4.12" "5.0")
artifacts_dir="artifacts/generator"
config="${1:-Release}"

rm -rf "$artifacts_dir"

for version in "${roslyn_versions[@]}"; do
  dotnet build src/ForgeMap.Generator/ForgeMap.Generator.csproj \
    -c "$config" \
    /p:ROSLYN_VERSION="$version"

  mkdir -p "$artifacts_dir/roslyn$version"
  cp src/ForgeMap.Generator/bin/"$config"/netstandard2.0/ForgeMap.Generator.dll \
     "$artifacts_dir/roslyn$version/"
  cp src/ForgeMap.Generator/bin/"$config"/netstandard2.0/ForgeMap.Abstractions.dll \
     "$artifacts_dir/roslyn$version/"
done

dotnet pack src/ForgeMap/ForgeMap.csproj -c "$config" --no-build \
  /p:GeneratorArtifactsDir="$(pwd)/$artifacts_dir"
```

This is simpler than Mapperly's `zipmerge` approach — all variants are staged before a single `dotnet pack`.

## Test Project Changes

Add `net10.0` to `tests/ForgeMap.Tests/ForgeMap.Tests.csproj`:

```xml
<TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
```

When tests run under each TFM, the corresponding SDK's Roslyn loads the matching analyzer variant, providing natural coverage of all three builds.

The existing `Microsoft.CodeAnalysis.CSharp` reference (for `CSharpGeneratorDriver`-based tests) stays at whatever version is in `Directory.Packages.props`. These programmatic tests exercise one Roslyn version; behavioral tests via normal compilation cover all three.

## Files Changed/Added

| File | Action |
|------|--------|
| `src/ForgeMap.Generator/ForgeMap.Generator.csproj` | Modify: add `ROSLYN_VERSION` property, import `.props`, remove direct Roslyn `PackageReference` |
| `src/ForgeMap.Generator/ForgeMap.Generator.Roslyn4.8.props` | New |
| `src/ForgeMap.Generator/ForgeMap.Generator.Roslyn4.12.props` | New |
| `src/ForgeMap.Generator/ForgeMap.Generator.Roslyn5.0.props` | New |
| `src/ForgeMap/ForgeMap.csproj` | Modify: `IncludePackageContent` target for 3 analyzer variants |
| `tests/ForgeMap.Tests/ForgeMap.Tests.csproj` | Modify: add `net10.0` to `TargetFrameworks` |
| `build/pack.sh` | New |
| `build/pack.ps1` | New (PowerShell equivalent) |

## What Does NOT Change

- `ForgeMap.Abstractions` — stays `netstandard2.0`, no Roslyn dependency
- `ForgeMap` wrapper project — stays `netstandard2.0` for its own output
- `Directory.Build.props` — no changes
- `Directory.Packages.props` — the central `Microsoft.CodeAnalysis.CSharp` version stays for tests/benchmarks
- Benchmark projects — no changes
- Generator source code — no `#if` directives, identical behavior across all variants

## Prior Art

Mapperly (Riok.Mapperly 4.3.1) uses the same pattern, shipping 7 Roslyn variants (`roslyn4.0` through `roslyn5.0`) under `analyzers/roslyn{VERSION}/dotnet/cs/`. They use a `zipmerge` tool to combine per-version `.nupkg` files; ForgeMap uses a simpler pre-build + single pack approach.
