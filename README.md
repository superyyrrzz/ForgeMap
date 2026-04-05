# ForgeMap

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

## Documentation

See [SPEC.md](docs/SPEC.md) for the full specification.

## Version Roadmap

| Version | Key Features |
|---------|--------------|
| **v1.0** | Core generator, property matching, collections, enums, constructor mapping, flattening, `[ReverseForge]`, hooks, DI integration, full diagnostics |
| **v1.1** | Mapping inheritance, `[IncludeBaseForge]`, `[ForgeAllDerived]` polymorphic dispatch |
| **v1.2** | Null-safe property assignment: `NullPropertyHandling` with 4 strategies, three-tier config |
| **v1.3** | Auto-wiring nested & collection mappings, abstract destination dispatch for `[ForgeAllDerived]` |
| **v1.4** | Nested existing-target mapping, automatic string↔enum conversion, `[ConvertWith]` custom type converters |

## License

MIT
