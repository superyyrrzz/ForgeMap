# ForgeMap v1.1 Specification: Mapping Inheritance & Polymorphic Dispatch

> **Tracking issue:** [#19 — Polymorphic mapping and inheritance support](https://github.com/superyyrrzz/ForgeMap/issues/19)

---

## Overview

ForgeMap v1.0 requires explicit forge methods for every concrete source→destination pair, with no mechanism to handle inheritance hierarchies or polymorphic dispatch. This version adds three features to close that gap:

| Feature | Problem Solved |
|---------|---------------|
| Inherited property resolution | Properties from base types in compiled assemblies are invisible to the generator |
| `[IncludeBaseForge]` | Configuration (`[Ignore]`, `[ForgeProperty]`, etc.) must be duplicated across derived forge methods |
| `[ForgeAllDerived]` | No way to dispatch a base-typed reference to the correct derived forge method at runtime |

---

## Feature 1: Inherited Property Resolution (Generator Fix)

### Problem

When the destination (or source) type comes from a compiled assembly (NuGet package), the generator only sees properties declared directly on the type. Inherited properties are silently skipped, producing empty or incomplete mapping bodies.

### Solution

The generator must walk the full `INamedTypeSymbol.BaseType` chain to collect all accessible properties (those with public getters on source types and public setters on destination types), regardless of whether the type is from source or a metadata reference (compiled assembly).

This is a **generator bugfix** — no new attributes or API surface. Existing forge methods that target types with inherited properties will automatically pick up the full hierarchy.

### Behavioral Contract

| Aspect | Behavior |
|--------|----------|
| **Property discovery** | Walk `INamedTypeSymbol.BaseType` recursively until `System.Object` or `null` |
| **Accessibility** | Include properties with public getters (for source) or public setters (for destination) |
| **Shadowing** | If a derived type `new`-shadows a base property, use the derived declaration |
| **Ordering** | Properties are assigned in declaration order, base-first (base properties before derived) |
| **Existing behavior** | Property discovery currently uses `INamedTypeSymbol.GetMembers()` without walking `BaseType`, so inherited properties are not discovered for either syntax-tree source types or metadata references |

### Generated Code Example

Given a compiled NuGet hierarchy:

```csharp
// In compiled NuGet assembly:
public class BaseEntityModel
{
    public string Uid { get; set; }
    public string Name { get; set; }
}
public class NormalSelectQuestion : BaseEntityModel
{
    public string Stem { get; set; }
    public string Kind { get; set; }
}
```

**Before (v1.0) — empty body:**
```csharp
public partial NormalSelectQuestion Forge(NormalSelectQuestion source)
{
    if (source == null) return null;
    return new NormalSelectQuestion { };  // Inherited properties missed
}
```

**After (v1.1) — full hierarchy:**
```csharp
public partial NormalSelectQuestion Forge(NormalSelectQuestion source)
{
    if (source == null) return null;
    return new NormalSelectQuestion
    {
        Uid = source.Uid,       // From BaseEntityModel
        Name = source.Name,     // From BaseEntityModel
        Stem = source.Stem,     // From NormalSelectQuestion
        Kind = source.Kind,     // From NormalSelectQuestion
    };
}
```

---

## Feature 2: `[IncludeBaseForge]` — Configuration Inheritance

### Attribute Definition

```csharp
/// <summary>
/// Inherits attribute-based configuration ([Ignore], [ForgeProperty], [ForgeFrom], [ForgeWith])
/// from a base forge method that maps TBaseSource → TBaseDestination.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
public sealed class IncludeBaseForgeAttribute : Attribute
{
    public IncludeBaseForgeAttribute(Type baseSourceType, Type baseDestinationType);
    public Type BaseSourceType { get; }
    public Type BaseDestinationType { get; }
}
```

### Usage

```csharp
[ForgeMap]
public partial class AppForger
{
    // Base mapping with configuration
    [Ignore(nameof(BaseDto.AuditTrail))]
    [ForgeProperty(nameof(BaseEntity.Uid), nameof(BaseDto.Id))]
    public partial BaseDto Forge(BaseEntity source);

    // Derived mapping inherits [Ignore] and [ForgeProperty] from base
    [IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
    public partial DerivedDto Forge(DerivedEntity source);
}
```

The derived forge method behaves as if it had the base method's `[Ignore(nameof(BaseDto.AuditTrail))]` and `[ForgeProperty(nameof(BaseEntity.Uid), nameof(BaseDto.Id))]` declared on it directly.

### What Is Inherited

| Attribute | Inherited? | Notes |
|-----------|-----------|-------|
| `[Ignore]` | Yes | Ignored properties remain ignored in derived mapping |
| `[ForgeProperty]` | Yes | Renamed properties carry over for inherited properties |
| `[ForgeFrom]` | Yes | Custom resolvers apply to inherited properties |
| `[ForgeWith]` | Yes | Nested forge methods apply to inherited properties |
| `[BeforeForge]` | **No** | Hooks are method-specific; declare explicitly on derived if needed |
| `[AfterForge]` | **No** | Hooks are method-specific; declare explicitly on derived if needed |
| `[ReverseForge]` | **No** | Reverse generation is per-method |
| `[ConvertWith]` | **No** | Converter override applies to the whole method, not individual properties |

### Override Behavior

Explicit attributes on the derived method take precedence over inherited attributes for the same property:

```csharp
[ForgeMap]
public partial class AppForger
{
    [Ignore(nameof(BaseDto.Status))]
    public partial BaseDto Forge(BaseEntity source);

    // Status is NOT ignored here — the explicit [ForgeProperty] overrides the inherited [Ignore]
    [IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
    [ForgeProperty(nameof(DerivedEntity.StatusCode), nameof(DerivedDto.Status))]
    public partial DerivedDto Forge(DerivedEntity source);
}
```

### Chaining

`[IncludeBaseForge]` can chain through multiple levels:

```csharp
[Ignore(nameof(BaseDto.AuditTrail))]
public partial BaseDto Forge(BaseEntity source);

[IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
[Ignore(nameof(MiddleDto.InternalFlag))]
public partial MiddleDto Forge(MiddleEntity source);

// Inherits AuditTrail ignore (from base) and InternalFlag ignore (from middle)
[IncludeBaseForge(typeof(MiddleEntity), typeof(MiddleDto))]
public partial LeafDto Forge(LeafEntity source);
```

### Validation Rules

| Condition | Diagnostic |
|-----------|-----------|
| Referenced base forge method not found in this forger | `FM0019` (Error) |
| Source type does not derive from `BaseSourceType` | `FM0020` (Error) |
| Destination type does not derive from `BaseDestinationType` | `FM0020` (Error) |
| Inherited attribute overridden by explicit attribute on derived method | `FM0021` (Info) |

---

## Feature 3: `[ForgeAllDerived]` — Polymorphic Dispatch

### Attribute Definition

```csharp
/// <summary>
/// Generates a polymorphic dispatch method that inspects the runtime type of the source
/// and delegates to the most-specific derived forge method.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class ForgeAllDerivedAttribute : Attribute { }
```

### Usage

```csharp
[ForgeMap]
public partial class AppForger
{
    // Generates polymorphic dispatch for all derived forge methods
    [ForgeAllDerived]
    public partial BaseDto Forge(BaseEntity source);

    public partial DerivedADto Forge(DerivedAEntity source);
    public partial DerivedBDto Forge(DerivedBEntity source);
}
```

### Generated Code

```csharp
public partial BaseDto Forge(BaseEntity source)
{
    if (source == null) return null;

    // Polymorphic dispatch — most-derived types checked first
    if (source is DerivedAEntity derivedA) return Forge(derivedA);
    if (source is DerivedBEntity derivedB) return Forge(derivedB);

    // Base mapping fallback
    return new BaseDto
    {
        Id = source.Id,
        Name = source.Name,
    };
}
```

### Discovery Rules

The generator scans all forge methods in the same forger class that satisfy **both** conditions:
1. Source parameter type derives from the `[ForgeAllDerived]` method's source type
2. Return type derives from (or equals) the `[ForgeAllDerived]` method's return type

### Dispatch Ordering

Derived types are checked **most-derived first** (deepest inheritance depth). For types at the same depth, ordering is alphabetical by fully qualified type name (deterministic output).

This ensures:
```csharp
// Given: GrandChild : Child : Base
// Check order: GrandChild → Child → Base fallback
if (source is GrandChildEntity gc) return Forge(gc);
if (source is ChildEntity c) return Forge(c);
// Base fallback...
```

### Collection Interop

When `[ForgeAllDerived]` is on a base forge method and a collection forge method exists for the base type, each element is dispatched polymorphically:

```csharp
// User declares:
[ForgeAllDerived]
public partial BaseDto Forge(BaseEntity source);
public partial DerivedDto Forge(DerivedEntity source);
public partial List<BaseDto> Forge(List<BaseEntity> source); // collection-typed partial signature

// Auto-generated body for the collection method calls the polymorphic Forge(BaseEntity):
public partial List<BaseDto> Forge(List<BaseEntity> source)
{
    if (source == null) return null;
    var result = new List<BaseDto>(source.Count);
    foreach (var item in source)
    {
        result.Add(Forge(item));  // Dispatches polymorphically
    }
    return result;
}
```

### Combining with `[IncludeBaseForge]`

`[ForgeAllDerived]` and `[IncludeBaseForge]` are complementary and commonly used together:

```csharp
[ForgeMap]
public partial class AppForger
{
    // Base mapping with config + polymorphic dispatch
    [ForgeAllDerived]
    [Ignore(nameof(BaseDto.AuditTrail))]
    public partial BaseDto Forge(BaseEntity source);

    // Derived mapping inherits base config
    [IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
    public partial DerivedADto Forge(DerivedAEntity source);

    [IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
    public partial DerivedBDto Forge(DerivedBEntity source);
}
```

### Validation Rules

| Condition | Diagnostic |
|-----------|-----------|
| No derived forge methods found for this base type | `FM0022` (Warning) |
| Base source type is abstract and no derived methods cover all known subtypes | `FM0022` (Warning) |
| Combined with `[ConvertWith]` | `FM0023` (Error) — mutually exclusive |

---

## Diagnostics Summary

| Code | Severity | Description |
|------|----------|-------------|
| `FM0019` | Error | `[IncludeBaseForge]` references a base forge method not found in this forger |
| `FM0020` | Error | `[IncludeBaseForge]` type mismatch: types are not in an inheritance relationship |
| `FM0021` | Info | `[IncludeBaseForge]` inherited attribute overridden by explicit attribute on derived method |
| `FM0022` | Warning | `[ForgeAllDerived]` found no derived forge methods, or abstract base type may have unmatched subtypes at runtime |
| `FM0023` | Error | `[ForgeAllDerived]` cannot be combined with `[ConvertWith]` |

---

## Migration Guide from AutoMapper

| AutoMapper | ForgeMap v1.1 |
|------------|---------------|
| `.IncludeBase<TBaseSrc, TBaseDst>()` | `[IncludeBaseForge(typeof(TBaseSrc), typeof(TBaseDst))]` |
| `.Include<TDerivedSrc, TDerivedDst>()` | Not needed — `[ForgeAllDerived]` auto-discovers |
| `.IncludeAllDerived()` | `[ForgeAllDerived]` |
| Inherited properties from compiled types | Automatic (generator fix) |

### Before (AutoMapper)

```csharp
public class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<BaseEntity, BaseDto>()
            .ForMember(d => d.AuditTrail, o => o.Ignore())
            .IncludeAllDerived();

        CreateMap<DerivedAEntity, DerivedADto>()
            .IncludeBase<BaseEntity, BaseDto>();

        CreateMap<DerivedBEntity, DerivedBDto>()
            .IncludeBase<BaseEntity, BaseDto>();
    }
}

// Usage — polymorphic
var dtos = entities.Select(e => mapper.Map<BaseDto>(e)).ToList();
```

### After (ForgeMap v1.1)

```csharp
[ForgeMap]
public partial class AppForger
{
    [ForgeAllDerived]
    [Ignore(nameof(BaseDto.AuditTrail))]
    public partial BaseDto Forge(BaseEntity source);

    [IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
    public partial DerivedADto Forge(DerivedAEntity source);

    [IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
    public partial DerivedBDto Forge(DerivedBEntity source);
}

// Usage — polymorphic
var dtos = entities.Select(e => forger.Forge(e)).ToList();
```

---

## Implementation Phases

| Phase | Scope | Key Changes |
|-------|-------|-------------|
| **Phase 1** | Inherited property resolution | Fix `ForgeCodeEmitter` to walk `INamedTypeSymbol.BaseType` chain for metadata references |
| **Phase 2** | `[IncludeBaseForge]` | New attribute in Abstractions, config merging logic in generator, diagnostics FM0019-FM0021 |
| **Phase 3** | `[ForgeAllDerived]` | New attribute in Abstractions, derived-type scanning, dispatch codegen, diagnostics FM0022-FM0023 |

---

## Appendix: Attribute Reference

```csharp
namespace ForgeMap
{
    /// <summary>
    /// Inherits attribute-based configuration from a base forge method.
    /// The base forge method must exist in the same forger class and map
    /// TBaseSource → TBaseDestination.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed class IncludeBaseForgeAttribute : Attribute
    {
        public IncludeBaseForgeAttribute(Type baseSourceType, Type baseDestinationType);
        public Type BaseSourceType { get; }
        public Type BaseDestinationType { get; }
    }

    /// <summary>
    /// Generates a polymorphic dispatch method that inspects the runtime type
    /// of the source and delegates to the most-specific derived forge method.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class ForgeAllDerivedAttribute : Attribute { }
}
```

---

*Specification Version: 1.1*
*License: MIT*
