---
_layout: landing
---

# ForgeMap

**Compile-time object mapping for .NET** — zero reflection, full type safety, MIT licensed.

ForgeMap is a Roslyn incremental source generator that writes your mapping code at compile time. The generated code is as fast as hand-written code, fully debuggable, and validated by the compiler.

## Get Started

```bash
dotnet add package ForgeMap
```

```csharp
[ForgeMap]
public partial class AppForger
{
    public partial OrderDto Forge(OrderEntity source);
}
```

[Quick Start](articles/getting-started/quick-start.md) | [API Reference](api/index.md) | [Migration from AutoMapper](articles/migration/from-automapper.md)

## Why ForgeMap?

### Zero Reflection
All mapping code is generated at compile time. No `System.Reflection`, no `Expression.Compile()`, no runtime overhead.

### Type Safe
Mappings are validated by the compiler and 45 dedicated diagnostics (FM0001-FM0045). Catch errors before your code runs.

### Debuggable
Generated `.g.cs` files are readable C# — set breakpoints, step through mapping logic, inspect values.

## Performance

ForgeMap is the fastest in benchmarks against AutoMapper and Mapperly (.NET 9, AMD EPYC 7763):

| Scenario           | ForgeMap    | Mapperly        | AutoMapper       |
|--------------------|-------------|-----------------|------------------|
| Simple (10 props)  | **14.5 ns** | 15.9 ns (1.1x)  | 80.7 ns (5.6x)  |
| Nested (2 levels)  | **27.3 ns** | 30.7 ns (1.1x)  | 92.5 ns (3.4x)  |
| Deep (4 levels)    | **31.3 ns** | 35.8 ns (1.1x)  | 247.0 ns (7.9x) |
| Collection (1000)  | **17.7 us** | 20.1 us (1.1x)  | 22.2 us (1.3x)  |
