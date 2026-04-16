# ForgeMap

[![CI](https://github.com/superyyrrzz/ForgeMap/actions/workflows/ci.yml/badge.svg)](https://github.com/superyyrrzz/ForgeMap/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/v/ForgeMap.svg)](https://www.nuget.org/packages/ForgeMap)
[![NuGet Downloads](https://img.shields.io/nuget/dt/ForgeMap.svg)](https://www.nuget.org/packages/ForgeMap)
[![License: MIT](https://img.shields.io/badge/license-MIT-blue.svg)](https://github.com/superyyrrzz/ForgeMap/blob/main/LICENSE)

A lightweight, MIT-licensed, source-generator-based object transformation library for .NET. ForgeMap generates type conversion code at compile time, providing zero-reflection runtime execution with compile-time type safety.

## Features

- **Source Generator** - Compile-time code generation, no runtime reflection
- **Zero Overhead** - Generated code is as fast as hand-written code
- **Type Safe** - Compile-time validation of mappings
- **Debuggable** - Generated code is readable and debuggable
- **MIT License** - Fully open source, no commercial restrictions

## Installation

```bash
dotnet add package ForgeMap
```

## Quick Start

```csharp
using ForgeMap;

// 1. Define your types
public class OrderEntity
{
    public string Id { get; set; }
    public string CustomerName { get; set; }
    public DateTime OrderDate { get; set; }
}

public class OrderDto
{
    public string Id { get; set; }
    public string CustomerName { get; set; }
    public DateTime OrderDate { get; set; }
}

// 2. Create a forger class
[ForgeMap]
public partial class AppForger
{
    public partial OrderDto Forge(OrderEntity source);
}

// 3. Use it
var forger = new AppForger();
var dto = forger.Forge(entity);
```

## Performance

ForgeMap is the fastest in benchmarks against AutoMapper and Mapperly (.NET 9, AMD EPYC 7763):

| Scenario           | ForgeMap    | Mapperly        | AutoMapper       |
|--------------------|-------------|-----------------|------------------|
| Simple (10 props)  | **14.5 ns** | 15.9 ns (1.1x)  | 80.7 ns (5.6x)  |
| Nested (2 levels)  | **27.3 ns** | 30.7 ns (1.1x)  | 92.5 ns (3.4x)  |
| Deep (4 levels)    | **31.3 ns** | 35.8 ns (1.1x)  | 247.0 ns (7.9x) |
| Collection (1000)  | **17.7 us** | 20.1 us (1.1x)  | 22.2 us (1.3x)  |

See [benchmarks/BENCHMARK_RESULTS.md](benchmarks/BENCHMARK_RESULTS.md) for full details.

## Migrating from AutoMapper

See the [Migration Guide](https://github.com/superyyrrzz/ForgeMap/blob/main/docs/migrating-from-automapper.md) for step-by-step instructions and before/after code examples. ForgeMap also includes an [AI-assisted migration tool](https://github.com/superyyrrzz/ForgeMap/blob/main/.claude/skills/automapper-migration/SKILL.md) (Claude Code skill) that can convert your existing AutoMapper `CreateMap`/`Profile` configurations to ForgeMap attributes automatically. See [ForgeMap vs AutoMapper & Mapperly](https://github.com/superyyrrzz/ForgeMap/blob/main/docs/ForgeMap-vs-AutoMapper-and-Mapperly.md) for a detailed feature comparison.

## Documentation

See [SPEC.md](https://github.com/superyyrrzz/ForgeMap/blob/main/docs/SPEC.md) for the full specification. See [CHANGELOG.md](https://github.com/superyyrrzz/ForgeMap/blob/main/CHANGELOG.md) for version history.

## License

MIT
