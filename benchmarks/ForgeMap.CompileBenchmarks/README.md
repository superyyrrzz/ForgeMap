# Compile-Time Benchmarks

Measures `dotnet build` wall-clock time for projects using ForgeMap, Mapperly, and AutoMapper at varying numbers of mapping classes. This complements the [runtime benchmarks](../BENCHMARK_RESULTS.md) by answering: **how much does each source generator add to build time?**

AutoMapper is reflection-based (no source generator) and serves as a baseline showing pure compilation cost without generator overhead.

## Prerequisites

- [.NET SDK 9.0+](https://dotnet.microsoft.com/download)
- [PowerShell Core (pwsh)](https://github.com/PowerShell/PowerShell)

## Quick Start

```bash
# Run with default scales (10, 50, 100 classes) and 5 iterations
pwsh ./Run-CompileBenchmarks.ps1

# Custom scales and iterations
pwsh ./Run-CompileBenchmarks.ps1 -Scales 10,50 -Iterations 3

# Generate models only (useful for debugging)
pwsh ./Generate-Models.ps1 -Count 25
```

## How It Works

1. **Generate-Models.ps1** creates N source/destination class pairs (`Source1`/`Dest1` through `SourceN`/`DestN`, each with 10 properties) plus mapper declarations for each library.
2. **Run-CompileBenchmarks.ps1** packs ForgeMap into a local NuGet package first (so all three mappers are consumed as pre-compiled packages), restores once, then for each mapper: cleans, shuts down build servers, and times `dotnet build --no-restore`. This is repeated across iterations; the median is reported.
3. Results are written to `COMPILE_BENCHMARK_RESULTS.md`.

## Project Structure

```
ForgeMap.CompileBenchmarks/
├── Generate-Models.ps1              # Code generator script
├── Run-CompileBenchmarks.ps1        # Benchmark runner
├── NuGet.config                     # Points to local package source
├── Shared/Models/                   # Generated models (gitignored)
├── LocalPackages/                   # Locally-packed ForgeMap NuGet (gitignored)
├── ForgeMap/                        # ForgeMap source-generator project
│   ├── ForgeMap.CompileBench.csproj
│   ├── Program.cs
│   └── Forger.cs                    # Generated (gitignored)
├── Mapperly/                        # Mapperly source-generator project
│   ├── Mapperly.CompileBench.csproj
│   ├── Program.cs
│   └── Mapper.cs                    # Generated (gitignored)
└── AutoMapper/                      # AutoMapper baseline project
    ├── AutoMapper.CompileBench.csproj
    ├── Program.cs
    └── MapperConfig.cs              # Generated (gitignored)
```

## Interpreting Results

- **ForgeMap vs Mapperly** is the key comparison — both are source generators consumed as pre-compiled NuGet packages, so the ratio shows the true relative generator cost.
- **AutoMapper** shows the compilation baseline with no generator overhead.
- Build times include the full `dotnet build` pipeline (parsing, analysis, source generation, emit). Generator cost can be estimated as `(ForgeMap or Mapperly time) - AutoMapper time`.
- Results vary by machine — compare ratios, not absolute times.
