---
name: automapper-migration
description: >
  Migrate a project from AutoMapper to ForgeMap in 4 safe, incremental commits.
  Use when user says "migrate from automapper", "replace automapper", "switch to forgemap",
  "automapper migration", "convert automapper to forgemap", or "migrate mapping".
---

# AutoMapper → ForgeMap Migration

## Critical: Read `references/api-mapping-guide.md` first

It contains exact API mappings between AutoMapper and ForgeMap for every feature. Consult it for every translation.

## Strategy: 4 incremental commits

Each commit leaves the project green (compiling + tests passing):

1. **Wrap** — Hide AutoMapper behind an `IMappingService` abstraction. No behavior change. No file outside the abstraction should reference AutoMapper.
2. **Test** — Add unit tests exercising every mapping through `IMappingService`. These define the behavioral contract ForgeMap must satisfy.
3. **Swap** — Replace the AutoMapper implementation with ForgeMap. All tests from step 2 must pass.
4. **Unwrap** — Delete the abstraction. All code calls `_forger.Forge(source)` directly.

## What Claude needs to know (non-obvious parts)

### The routing shim in commit 3

AutoMapper's `IMapper.Map<TDest>(object source)` accepts untyped `object` and uses runtime type discovery. ForgeMap uses compile-time source generation with strongly-typed methods (`Forge(User source) → UserDto`). To bridge this gap temporarily, `ForgeMapMappingService` dispatches untyped calls to the correct `Forge()` overload:

```csharp
// This is NOT manual mapping — each arm delegates to ForgeMap's generated code.
// This shim is deleted entirely in commit 4.
return source switch
{
    User u when typeof(TDestination) == typeof(UserDto) => (TDestination)(object)_forger.Forge(u),
    Order o when typeof(TDestination) == typeof(OrderDto) => (TDestination)(object)_forger.Forge(o),
    _ => throw new NotSupportedException(...)
};
```

**All actual property mapping is done by ForgeMap.** The shim only routes. It is temporary and deleted in commit 4.

### Nullable annotations on the abstraction interface

Use `[return: MaybeNull]` on return types and nullable parameter types (`TSource?`) — AutoMapper returns `default(TDestination)` for null sources (null for reference types, zero/false for value types). Without these annotations, callers in nullable-enabled projects will assume non-null returns.

### ForgeMap-specific gotchas

- **Case sensitivity**: AutoMapper is case-insensitive by default; ForgeMap is case-sensitive. Add `PropertyMatching = PropertyMatching.ByNameCaseInsensitive` if the project relies on case-insensitive matching.
- **Nested maps are NOT auto-discovered**: AutoMapper auto-discovers `CreateMap<Address, AddressDto>()` when mapping a parent. ForgeMap requires explicit `[ForgeWith(nameof(D.Prop), nameof(ForgeNested))]`.
- **Collection properties need explicit wiring**: A `List<A>` → `List<B>` property on a parent is NOT auto-mapped just because an element forge exists. Declare a collection-level forge method referenced via `[ForgeWith]`, and the element method must share the same name (overload resolution).
- **No `ProjectTo<T>()`**: ForgeMap is compile-time only. Rewrite to materialize first: `var entities = query.ToList(); var dtos = entities.Select(x => forger.Forge(x)).ToList();`. Warn user about performance implications.
- **No `[ConvertWith]` / global type converters**: Use per-method `[ForgeFrom]` resolvers instead.
- **No `ConstructUsing()` equivalent**: ForgeMap maps constructor/record parameters when the destination has an accessible constructor, but has no custom factory logic. Adjust destination constructors or use `[ForgeFrom]` / `[BeforeForge]` hooks.

### Build diagnostics to watch for

- **FM0005** (unmapped source property): Add `[Ignore]` or `SuppressDiagnostics` if intentional
- **FM0007** (nullable→non-nullable): Make destination nullable, adjust null handling, or use `[ForgeFrom]` fallback
- **FM0015** (`[ForgeWith]` target missing `[ReverseForge]`): Add `[ReverseForge]` to nested method or remove from parent

### Test failure handling in commit 3

1. **Configuration error** → fix the forger
2. **Expected behavioral difference** (e.g., case sensitivity) → adjust ForgeMap config or test, document in commit message
3. **ForgeMap bug** → file issue on `superyyrrzz/ForgeMap` with title `[Migration] <description>`, AutoMapper vs ForgeMap behavior, and reproduction. Keep the failing test in a "known gaps" suite — do not skip or delete it.
