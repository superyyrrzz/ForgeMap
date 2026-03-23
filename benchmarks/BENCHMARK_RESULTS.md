# ForgeMap Benchmark Results

Benchmark comparing **ForgeMap**, **Mapperly** (Riok.Mapperly 4.3.1), and **AutoMapper** (14.0.0) across four mapping scenarios.

## Environment

```
BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.7984)
AMD EPYC 7763, 1 CPU, 16 logical and 8 physical cores
.NET SDK 10.0.200
  [Host]     : .NET 9.0.14 (9.0.1426.11910), X64 RyuJIT AVX2
  DefaultJob : .NET 9.0.14 (9.0.1426.11910), X64 RyuJIT AVX2
```

## Simple Flat Mapping

Single object with 10 properties (int, string, decimal, bool, DateTime, DateTime?).

| Method     | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------- |----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| ForgeMap   |  14.47 ns | 0.356 ns | 0.991 ns |  1.00 |    0.10 | 0.0062 |     104 B |        1.00 |
| Mapperly   |  15.88 ns | 0.382 ns | 0.900 ns |  1.10 |    0.10 | 0.0062 |     104 B |        1.00 |
| AutoMapper |  80.74 ns | 1.224 ns | 1.022 ns |  5.61 |    0.38 | 0.0062 |     104 B |        1.00 |

## Nested Object Mapping

Order with nested Customer and Address objects (2 levels).

| Method     | Mean      | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------- |----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| ForgeMap   |  27.34 ns | 0.585 ns | 0.820 ns |  1.00 |    0.04 | 0.0095 |     160 B |        1.00 |
| Mapperly   |  30.68 ns | 0.607 ns | 0.538 ns |  1.12 |    0.04 | 0.0095 |     160 B |        1.00 |
| AutoMapper |  92.49 ns | 1.823 ns | 2.239 ns |  3.39 |    0.13 | 0.0095 |     160 B |        1.00 |

## Deep Object Graph

4-level nesting: Company → Department → Team → Employee.

| Method     | Mean       | Error    | StdDev   | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|----------- |-----------:|---------:|---------:|------:|--------:|-------:|----------:|------------:|
| ForgeMap   |  31.32 ns  | 0.681 ns | 0.785 ns |  1.00 |    0.03 | 0.0105 |     176 B |        1.00 |
| Mapperly   |  35.83 ns  | 0.737 ns | 0.819 ns |  1.14 |    0.04 | 0.0105 |     176 B |        1.00 |
| AutoMapper | 247.01 ns  | 1.722 ns | 1.611 ns |  7.89 |    0.20 | 0.0105 |     176 B |        1.00 |

## Collection Mapping

Mapping `List<T>` of 100 and 1000 items (simple flat objects).

| Method          | Mean       | Error     | StdDev    | Ratio | RatioSD | Gen0   | Gen1   | Allocated  | Alloc Ratio |
|---------------- |-----------:|----------:|----------:|------:|--------:|-------:|-------:|-----------:|------------:|
| ForgeMap_100    |   1.670 us | 0.0330 us | 0.0560 us |  1.00 |    0.05 | 0.6714 | 0.0267 |  10.99 KB  |        1.00 |
| Mapperly_100    |   2.015 us | 0.0359 us | 0.0788 us |  1.21 |    0.06 | 0.6714 | 0.0267 |  10.99 KB  |        1.00 |
| AutoMapper_100  |   2.444 us | 0.0444 us | 0.0856 us |  1.46 |    0.07 | 0.7515 | 0.0305 |  12.30 KB  |        1.12 |
| ForgeMap_1000   |  17.686 us | 0.3433 us | 0.4700 us | 10.60 |    0.44 | 6.6833 | 1.8311 | 109.43 KB  |        9.96 |
| Mapperly_1000   |  20.082 us | 0.4004 us | 0.8788 us | 12.04 |    0.65 | 6.6833 | 1.8311 | 109.43 KB  |        9.96 |
| AutoMapper_1000 |  22.184 us | 0.4342 us | 0.5169 us | 13.30 |    0.53 | 7.2021 | 2.3804 | 117.77 KB  |       10.71 |

## Summary

| Scenario        | ForgeMap | Mapperly      | AutoMapper      |
|-----------------|----------|---------------|-----------------|
| Simple (10 props) | **14.5 ns** | 15.9 ns (1.1x) | 80.7 ns (5.6x)  |
| Nested (2 levels) | **27.3 ns** | 30.7 ns (1.1x) | 92.5 ns (3.4x)  |
| Deep (4 levels)   | **31.3 ns** | 35.8 ns (1.1x) | 247.0 ns (7.9x) |
| Collection (100)  | **1.67 us** | 2.02 us (1.2x) | 2.44 us (1.5x)  |
| Collection (1000) | **17.7 us** | 20.1 us (1.1x) | 22.2 us (1.3x)  |

**Key findings:**

- **ForgeMap vs Mapperly:** ForgeMap is 10-21% faster across all scenarios. Both are compile-time source generators with identical memory allocations — the difference is in code generation quality.
- **ForgeMap vs AutoMapper:** ForgeMap is 1.3x-7.9x faster. The gap widens with object complexity (simple: 5.6x, deep graph: 7.9x), reflecting the compounding cost of runtime reflection for nested type resolution.
- **Memory:** All three mappers allocate identically for single-object scenarios. AutoMapper allocates ~12% more for collection mapping due to internal runtime overhead.

## Running the Benchmarks

```bash
# Run all benchmarks
dotnet run -c Release --project benchmarks/ForgeMap.Benchmarks

# Run a specific scenario
dotnet run -c Release --project benchmarks/ForgeMap.Benchmarks -- --filter '*Simple*'
```
