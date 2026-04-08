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

- **Minimum ForgeMap version: 1.5.0.** Always install the latest release from NuGet (`dotnet add package ForgeMap`). The migration skill assumes all features up to v1.5 are available.
- **NEVER write manual mapping code.** If ForgeMap can't support a required mapping, **stop and report the gap.** File an issue on `superyyrrzz/ForgeMap` with title `[Migration] <description>` and let the user decide.
- **No git operations.** Do not run any git commands (checkout, commit, push, branch, etc.). The developer controls their own git workflow.

## Phases (each leaves the project green)

1. **Wrap** — `IMappingService` abstraction over AutoMapper. Nothing outside references AutoMapper.
2. **Test** — Unit tests for every mapping through `IMappingService`.
3. **Swap** — Replace AutoMapper impl with ForgeMap. All tests must pass.
4. **Unwrap** — Delete abstraction. Call `_forger.Forge(source)` directly.

Report progress to the user after each phase. **Between successful phases, proceed automatically without asking for confirmation.** If ForgeMap cannot support a required mapping (the unsupported-mapping hard rule) or the build/tests fail and the project is not green at the end of a phase, stop, report the issue, and do not continue to later phases. After all completed phases are done, present a summary so the developer can review the full set of changes.

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
- **Nested maps are auto-wired by default (v1.3+).** When `AutoWireNestedMappings = true` (default), the generator searches for a forge method in the same forger class with matching source parameter and return type — no `[ForgeWith]` needed. Disable per-forger or assembly-wide with `AutoWireNestedMappings = false`.
- **Collection properties are auto-wired inline (v1.3+).** When an element forge method exists, collections (`List`, `IList`, `ICollection`, `IReadOnlyList`, `IReadOnlyCollection`, `HashSet`, arrays, `IEnumerable`) are mapped inline automatically. Explicit collection forge methods still take precedence if declared.
- **Collection type coercion (v1.5+).** The generator automatically coerces concrete collection types to compatible interfaces (e.g., `List<T>` → `IReadOnlyList<T>`, `Dictionary<K,V>` → `IDictionary<K,V>`). No manual conversion needed when the destination expects a collection interface. FM0040 warns if no known coercion is available.
- **Standalone collection mapping methods (v1.5+).** Declare a partial method with collection parameter and return types, and the generator auto-discovers the matching element-level forge method to implement the body. This simplifies collection mapping at call sites — instead of `.Select(x => forger.Forge(x))`, call `forger.ForgeAll(items)` directly. FM0041 reports missing element mappings and FM0042 reports ambiguous matches.
- **No `ProjectTo<T>()`** — materialize first, then map. Warn user about perf implications.
- **`[ConvertWith]` delegates to `ITypeConverter<TSource, TDest>` implementations (v1.4+).** Apply `[ConvertWith(typeof(MyConverter))]` to a forge method to delegate the entire conversion to the converter's `Convert` method. For type-based converters, the generator resolves the instance via: `IServiceScopeFactory` field → `IServiceProvider` field → parameterless constructor. Alternatively, use member-based references via `[ConvertWith(nameof(_field))]` to use a specific DI-injected field or property directly. When `[ConvertWith]` is present, it takes full precedence over `[ForgeProperty]`/`[ForgeFrom]`/`[ForgeWith]` (FM0036 warning).
- **`[ForgeAllDerived]` supports abstract/interface destinations (v1.3+).** When the destination type is abstract or an interface, the generator emits a dispatch-only body (no instantiation) with a `NotSupportedException` fallback. Source-side auto-discovery still requires a class inheritance chain (interfaces not considered), and each derived method's return type must be assignable to the base destination type. Derived forge methods must be declared in the same forger class and share the same method name.
- **String↔enum auto-conversion (v1.4+, default on).** When the source property is `string` and the destination is `enum`, the generator auto-converts using `Enum.Parse` (throws on invalid) by default. Control string→enum behavior per-forger via `[ForgeMap(StringToEnum = StringToEnumConversion.TryParse)]` (falls back to `default(T)` for non-nullable, `null` for nullable enum destinations) or `StringToEnumConversion.None` (requires explicit `[ForgeFrom]`). Assembly default via `[ForgeMapDefaults(StringToEnum = ...)]`. All nullable combinations (`string?`↔`enum?`) are supported, respecting `NullPropertyHandling`. In the enum→string direction, generated code uses `.ToString()`. Reverse via `[ReverseForge]` emits `.ToString()`.
- **Nested existing-target mapping (v1.4+, opt-in).** `[ForgeProperty("Prop", "Prop", ExistingTarget = true)]` updates nested objects in place instead of replacing them — critical for EF Core change tracking. Only valid on `[UseExistingValue]` mutation methods (FM0028). For collections, use `CollectionUpdate = CollectionUpdateStrategy.Sync` with `KeyProperty = "Id"` to match, update, add, and remove elements by key (FM0031/FM0032). Sync requires a `List<T>` destination (other collection types are not supported). For complete matched-item updates and new-item adds, provide element `ForgeInto` and `Forge` methods respectively; if one or both are missing, the generator emits FM0030 as a warning and still compiles, but matched items may not be updated and/or new items may not be added unless the element types are directly assignable.

## Null-property handling

AutoMapper assigns null through by default (`AllowNullDestinationValues = true`). ForgeMap's `NullPropertyHandling` controls nullable-to-non-nullable **reference type** property assignments and constructor-parameter expressions. During migration:

| AutoMapper pattern | ForgeMap equivalent |
|---|---|
| Default (assigns null through) | `NullPropertyHandling.NullForgiving` (default) — no configuration needed |
| `AllowNullCollections = false` (null → empty collection) | `NullPropertyHandling.CoalesceToDefault` — but note this applies to **all** nullable-ref → non-nullable-ref properties (not just collections). Use per-property overrides if you only want collection-only coalescing. |
| Null → default-constructed object | `NullPropertyHandling.CoalesceToNew` (v1.5+) — coalesces null to `new T()`. The destination type must have an accessible parameterless constructor (FM0038 error otherwise). Unlike `CoalesceToDefault` which uses type-specific defaults (`""` for strings, `new List<T>()` for collections), `CoalesceToNew` always instantiates via `new T()`. |
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

**FM0007 is active** — the generator reports a warning for every direct nullable-ref → non-nullable-ref assignment it emits (mappings via `[ForgeFrom]` resolvers, `[ForgeWith]` nested forging, or auto-wired nested mappings do not trigger FM0007). If this is noisy during migration, suppress with `SuppressDiagnostics = new[] { "FM0007" }` on the forger class, or `<NoWarn>FM0007</NoWarn>` in `.csproj`.

**`SkipNull` limitations** — `SkipNull` falls back to `NullForgiving` for constructor parameters (can't omit required args) and init-only properties (can't conditionally assign after initialization).

## `[AfterForge]` migration diagnostics (v1.5+)

v1.5 adds compile-time diagnostics that catch common `[AfterForge]` mistakes during migration:

| Diagnostic | Description | Migration action |
|---|---|---|
| FM0043 | `[AfterForge]` method not found or has wrong signature | Fix the method name or signature: `void Method(S source, D dest)` |
| FM0044 | `[AfterForge]` and `[ConvertWith]` are mutually exclusive | Remove one — `[ConvertWith]` takes full precedence and `[AfterForge]` cannot run |
| FM0045 | `[AfterForge]` is not applicable to collection methods | Move post-mapping logic to the element-level forge method's `[AfterForge]`, or handle it at the call site |

## After migration ends

Do **not** commit or create branches automatically. Instead, summarize what was done and suggest commit message(s) the developer can use.

**If all 4 phases succeeded:**

> All 4 phases complete. You can commit as a single change:
>
> `migrate: replace AutoMapper with ForgeMap`
>
> Or use `git add -p` / selective staging to create separate commits per phase if you prefer granular history:
> 1. `refactor: wrap AutoMapper behind IMappingService`
> 2. `test: add mapping unit tests through IMappingService`
> 3. `refactor: swap AutoMapper impl with ForgeMap`
> 4. `refactor: unwrap IMappingService, call ForgeMap directly`

**If migration stopped early** (unsupported mapping or build/test failure):

> Phases 1–N completed successfully. Phase N+1 stopped because [reason].
> The failed phase may have left partial edits in the worktree. Use `git diff` to review all changes. Since earlier phases may have edited the same files, use `git add -p` to selectively stage only the hunks from completed phases — do not blindly revert entire files.
