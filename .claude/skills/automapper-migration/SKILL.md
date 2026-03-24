---
name: automapper-migration
description: >
  Migrate a project from AutoMapper to ForgeMap in 4 safe, incremental phases.
  Use when user says "migrate from automapper", "replace automapper", "switch to forgemap",
  "automapper migration", "convert automapper to forgemap", or "migrate mapping".
---

# AutoMapper → ForgeMap Migration

## Read `references/api-mapping-guide.md` first

It contains exact API mappings between AutoMapper and ForgeMap. Consult it for every translation.

## Hard rules

- **Always use the latest ForgeMap NuGet package.** The migration skill assumes all current features are available.
- **NEVER write manual mapping code.** If ForgeMap can't support a required mapping, **stop and report the gap.** File an issue on `superyyrrzz/ForgeMap` with title `[Migration] <description>` and let the user decide.
- **No git operations.** Do not run any git commands (checkout, commit, push, branch, etc.). The developer controls their own git workflow.

## Phases (each leaves the project green)

1. **Wrap** — `IMappingService` abstraction over AutoMapper. Nothing outside references AutoMapper.
2. **Test** — Unit tests for every mapping through `IMappingService`.
3. **Swap** — Replace AutoMapper impl with ForgeMap. All tests must pass.
4. **Unwrap** — Delete abstraction. Call `_forger.Forge(source)` directly.

Report progress to the user after each phase and **wait for confirmation before proceeding** to the next phase. This gives the developer a chance to review the changes, run tests, and commit at their discretion.

## The routing shim (Phase 3) — this is the tricky part

AutoMapper's `IMapper.Map<TDest>(object source)` is untyped. ForgeMap is compile-time with strongly-typed `Forge()` overloads. The `ForgeMapMappingService` needs a temporary dispatch shim:

```csharp
// NOT manual mapping — each arm delegates to ForgeMap's generated code.
// Deleted entirely in Phase 4.
if (source is null) return default!;
return source switch
{
    User u when typeof(TDestination) == typeof(UserDto) => (TDestination)(object)_forger.Forge(u),
    Order o when typeof(TDestination) == typeof(OrderDto) => (TDestination)(object)_forger.Forge(o),
    _ => throw new NotSupportedException($"No ForgeMap mapping for {source.GetType().Name} → {typeof(TDestination).Name}")
};
```

## ForgeMap behaviors that differ from AutoMapper

- **Case-sensitive by default** (AutoMapper is case-insensitive). Use `PropertyMatching = PropertyMatching.ByNameCaseInsensitive` if needed.
- **Nested maps are NOT auto-discovered.** Requires explicit `[ForgeWith(nameof(D.Prop), nameof(ForgeNested))]`.
- **Collection properties need explicit wiring.** `List<A>` → `List<B>` on a parent isn't auto-mapped. Declare a collection-level forge method via `[ForgeWith]`, and the element method must share the same method name (overload resolution).
- **No `ProjectTo<T>()`** — materialize first, then map. Warn user about perf implications.
- **`[ConvertWith]` is not functional** — the attribute exists but the generator ignores it. Use `[ForgeFrom]` resolvers instead.
- **`[ForgeAllDerived]` auto-discovery needs same method name** — derived forge methods must be overloads with the same name in the same forger class. Differently-named methods won't be picked up.

## Null-property handling

AutoMapper assigns null through by default (`AllowNullDestinationValues = true`). ForgeMap's `NullPropertyHandling` controls nullable-to-non-nullable **reference type** property assignments. During migration:

| AutoMapper pattern | ForgeMap equivalent |
|---|---|
| Default (assigns null through) | `NullPropertyHandling.NullForgiving` (default) — no configuration needed |
| `AllowNullCollections = false` (null → empty collection) | `NullPropertyHandling.CoalesceToDefault` (applies to any nullable-ref → non-nullable-ref where a default can be generated, e.g., collections → empty, `string?` → `""`) |
| `.NullSubstitute(value)` | `[ForgeFrom]` resolver returning the substitute value |

**Three-tier configuration** — settings resolve per-property > per-forger > assembly default:

```csharp
// Assembly-level default
[assembly: ForgeMapDefaults(NullPropertyHandling = NullPropertyHandling.CoalesceToDefault)]

// Per-forger override
[ForgeMap(NullPropertyHandling = NullPropertyHandling.SkipNull)]
public partial class StrictForger { ... }

// Per-property override
[ForgeProperty(nameof(Source.Tags), nameof(Dest.Tags),
    NullPropertyHandling = NullPropertyHandling.ThrowException)]
```

**FM0007 is active** — the generator reports a warning for every direct nullable-ref → non-nullable-ref assignment it emits (mappings via `[ForgeFrom]` resolvers or `[ForgeWith]` nested forging do not trigger FM0007). If this is noisy during migration, suppress with `SuppressDiagnostics = new[] { "FM0007" }` on the forger class, or `<NoWarn>FM0007</NoWarn>` in `.csproj`.

## After migration completes

Do **not** commit or create branches automatically. Instead, summarize what was done and suggest commit message(s) the developer can use. Example:

> All 4 phases complete. You can commit as a single change:
>
> `migrate: replace AutoMapper with ForgeMap`
>
> Or as separate commits per phase if you prefer granular history:
> 1. `refactor: wrap AutoMapper behind IMappingService`
> 2. `test: add mapping unit tests through IMappingService`
> 3. `refactor: swap AutoMapper impl with ForgeMap`
> 4. `refactor: unwrap IMappingService, call ForgeMap directly`
