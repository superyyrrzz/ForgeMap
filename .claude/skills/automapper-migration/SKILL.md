---
name: automapper-migration
description: >
  Migrate a project from AutoMapper to ForgeMap in 4 safe, incremental commits.
  Use when user says "migrate from automapper", "replace automapper", "switch to forgemap",
  "automapper migration", "convert automapper to forgemap", or "migrate mapping".
---

# AutoMapper → ForgeMap Migration

## Read `references/api-mapping-guide.md` first

It contains exact API mappings between AutoMapper and ForgeMap. Consult it for every translation.

## Hard rules

- **Minimum ForgeMap version: 1.1.0** — `[IncludeBaseForge]`, `[ForgeAllDerived]`, compatible enum auto-conversion, and inherited property resolution all require it.
- **NEVER write manual mapping code.** If ForgeMap can't support a required mapping, **stop and report the gap.** File an issue on `superyyrrzz/ForgeMap` with title `[Migration] <description>` and let the user decide.

## Step 0: Create a migration branch

Ensure the branch is based on the repo's default branch:

```
git checkout main && git pull && git checkout -b migrate/automapper-to-forgemap
```

Use a user-specified name if provided.

## Commits (each leaves the project green)

1. **Wrap** — `IMappingService` abstraction over AutoMapper. Nothing outside references AutoMapper.
2. **Test** — Unit tests for every mapping through `IMappingService`.
3. **Swap** — Replace AutoMapper impl with ForgeMap. All tests must pass.
4. **Unwrap** — Delete abstraction. Call `_forger.Forge(source)` directly.

## The routing shim (commit 3) — this is the tricky part

AutoMapper's `IMapper.Map<TDest>(object source)` is untyped. ForgeMap is compile-time with strongly-typed `Forge()` overloads. The `ForgeMapMappingService` needs a temporary dispatch shim:

```csharp
// NOT manual mapping — each arm delegates to ForgeMap's generated code.
// Deleted entirely in commit 4.
if (source is null) return default!;
return source switch
{
    User u when typeof(TDestination) == typeof(UserDto) => (TDestination)(object)_forger.Forge(u),
    Order o when typeof(TDestination) == typeof(OrderDto) => (TDestination)(object)_forger.Forge(o),
    _ => throw new NotSupportedException(...)
};
```

## ForgeMap behaviors that differ from AutoMapper

- **Case-sensitive by default** (AutoMapper is case-insensitive). Use `PropertyMatching = PropertyMatching.ByNameCaseInsensitive` if needed.
- **Nested maps are NOT auto-discovered.** Requires explicit `[ForgeWith(nameof(D.Prop), nameof(ForgeNested))]`.
- **Collection properties need explicit wiring.** `List<A>` → `List<B>` on a parent isn't auto-mapped. Declare a collection-level forge method via `[ForgeWith]`, and the element method must share the same method name (overload resolution).
- **No `ProjectTo<T>()`** — materialize first, then map. Warn user about perf implications.
- **`[ConvertWith]` is not functional** — the attribute exists but the generator ignores it. Use `[ForgeFrom]` resolvers instead.
- **`[ForgeAllDerived]` auto-discovery needs same method name** — derived forge methods must be overloads with the same name in the same forger class. Differently-named methods won't be picked up.
