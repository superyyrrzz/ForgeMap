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

## NEVER write manual mapping code

Every mapping MUST go through ForgeMap's source generator. If you encounter a scenario where ForgeMap cannot support a required mapping, **stop the migration and report the gap to the user.** Do not work around it by writing hand-coded property assignments. The entire point of this migration is to use ForgeMap — manual mapping defeats that purpose and creates unmaintainable code.

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
if (source is null) return default!; // AutoMapper returns default(TDestination) for null
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
- **`[ConvertWith]` is not yet functional**: The attribute exists in the abstractions but the generator does not honor it for conversion. Use per-method `[ForgeFrom]` resolvers instead. The only current effect of `[ConvertWith]` is triggering FM0023 when combined with `[ForgeAllDerived]`.
- **No `ConstructUsing()` equivalent**: ForgeMap maps constructor/record parameters when the destination has an accessible constructor, but has no custom factory logic. Adjust destination constructors or use `[ForgeFrom]` / `[BeforeForge]` hooks.
- **`[IncludeBaseForge]` for configuration inheritance**: Inherits attribute-based configuration (`[Ignore]`, `[ForgeProperty]`, `[ForgeFrom]`, `[ForgeWith]`) from a base forge method. Replaces AutoMapper's `.IncludeBase<TBaseSrc, TBaseDst>()`. Explicit attributes on the derived method override inherited ones. Can chain through multiple levels.
- **`[ForgeAllDerived]` for polymorphic dispatch**: Generates a polymorphic dispatch method that inspects the runtime type and delegates to the most-specific derived forge method (`is` cascade). Replaces AutoMapper's `.IncludeAllDerived()`. Derived methods are auto-discovered — no manual registration needed.
- **Cross-namespace enum auto-conversion**: Enums with identical members, values, declaration order, and matching underlying types in different namespaces are automatically cast using the enum's underlying type (e.g., `(DestEnum)(<underlying-type>)source.Prop`). Works with nullable variants. No forge method or attribute required.
- **Inherited properties from compiled assemblies**: Properties from base types in NuGet packages or compiled assemblies are automatically discovered without any configuration.

### Build diagnostics to watch for

- **FM0005** (unmapped source property): Add `[Ignore]` or `SuppressDiagnostics` if intentional
- **FM0007** (nullable→non-nullable): Make destination nullable, adjust null handling, or use `[ForgeFrom]` fallback
- **FM0015** (`[ForgeWith]` target missing `[ReverseForge]`): Add `[ReverseForge]` to nested method or remove from parent
- **FM0019** (`[IncludeBaseForge]` base not found): The referenced base forge method must exist in the same forger class
- **FM0020** (`[IncludeBaseForge]` type mismatch): Source/destination types must actually derive from the specified base types
- **FM0021** (inherited attribute overridden): Info-level — explicit attribute on derived method takes precedence over inherited one
- **FM0022** (`[ForgeAllDerived]` no derived methods): No derived forge methods found for the base type; check that derived forge methods exist in the same forger
- **FM0023** (`[ForgeAllDerived]` + `[ConvertWith]` conflict): Emitted when a forge method has both `[ForgeAllDerived]` and `[ConvertWith]` — these are mutually exclusive; refactor so only one is applied

### Test failure handling in commit 3

1. **Configuration error** → fix the forger
2. **Expected behavioral difference** (e.g., case sensitivity) → adjust ForgeMap config or test, document in commit message
3. **ForgeMap feature gap** → **stop the migration.** Do NOT implement manual mapping as a workaround. File an issue on `superyyrrzz/ForgeMap` with title `[Migration] <description>`, AutoMapper vs ForgeMap behavior, and reproduction. Report the blocker to the user and let them decide whether to wait for a fix or accept the gap.
