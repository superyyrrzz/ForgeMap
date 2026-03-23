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

## Documentation

See [SPEC.md](docs/SPEC.md) for the full specification.

## Version Roadmap

| Version | Key Features |
|---------|--------------|
| **v0.1** | Core generator, property matching, `[Ignore]` |
| **v0.2** | `[ForgeProperty]`, `[ForgeFrom]`, nullable handling |
| **v0.3** | Collections, `[ForgeWith]` (nested objects) |
| **v0.4** | Enums, constructor mapping, flattening |
| **v0.5** | `[ReverseForge]` |
| **v0.6** | `[BeforeForge]`, `[AfterForge]`, `ForgeInto()` |
| **v1.0** | DI integration, full diagnostics, NuGet publish |
| **v1.1** | Mapping inheritance, `[IncludeBaseForge]`, `[ForgeAllDerived]` polymorphic dispatch |

## License

MIT
