# ForgeMap vs AutoMapper & Mapperly — Technical Comparison

**Mapperly** and **ForgeMap** are both Roslyn incremental source generators that produce zero-reflection, compile-time mapping code at comparable speed (~14–15 ns for simple objects, see [benchmark results](../benchmarks/BENCHMARK_RESULTS.md)).

However, ForgeMap addresses **critical gaps** in Mapperly that enterprise codebases will hit during migration — particularly around reverse mapping, polymorphic dispatch, null safety, and configuration inheritance.

---

## Feature Comparison

| Capability | AutoMapper | Mapperly | ForgeMap |
|---|---|---|---|
| **Engine** | Runtime reflection | Source generator | Source generator |
| **Performance (simple flat mapping)** | ~80 ns | ~15 ns | ~14.5 ns |
| **License** | RPL-1.5 / commercial (15.0+) | Apache 2.0 | MIT |
| **Auto reverse mapping** | `.ReverseMap()` | ❌ Manual only | ✅ `[ReverseForge]` with compile-time validation |
| **Polymorphic dispatch** | Runtime reflection | Manual `[MapDerivedType]` per type | ✅ Auto-discovered `[ForgeAllDerived]` |
| **Abstract destination mapping** | Runtime `.As<T>()` | `[MapDerivedType]` dispatch | ✅ Auto-discovered dispatch via `[ForgeAllDerived]` |
| **Null handling strategies** | `NullSubstitute`, `AllowNullCollections` | `AllowNullPropertyAssignment`, `ThrowOnPropertyMappingNullMismatch`, `ThrowOnMappingNullMismatch` | ✅ 4 strategies (`NullForgiving`, `SkipNull`, `CoalesceToDefault`, `ThrowException`), 3-tier config |
| **Per-property null control** | `NullSubstitute` per member | Per-property not configurable | ✅ `[ForgeProperty(..., NullPropertyHandling)]` with type-aware defaults |
| **Base config inheritance** | `.IncludeBase<TSourceBase, TDestinationBase>()` | `[IncludeMappingConfiguration]` | ✅ `[IncludeBaseForge]` |
| **Auto-wire nested mappings** | Runtime registry | Same-mapper auto-discovery; `[UseMapper]` for external mappers | ✅ Compile-time auto-discovery within forger class |
| **Lifecycle hooks** | `.BeforeMap()` / `.AfterMap()` | Manual wrapper methods | ✅ `[BeforeForge]` / `[AfterForge]` with ordered execution |
| **Mutation mapping** | `Map(src, dest)` | `Map(src, dest)` | ✅ Partial-method mutation pattern with `[UseExistingValue]` destination |
| **Collection auto-generation** | Runtime | Auto-generated | ✅ Full (`T[]`, `List<T>`, `IEnumerable<T>`, `HashSet<T>`, etc.) |
| **Inline collection mapping** | N/A | Auto-generated | ✅ Generates inline iteration for collection properties, no explicit method needed |
| **Diagnostics** | Runtime exceptions | ~95 diagnostics (RMG001–RMG095) | 27 diagnostics (FM0001–FM0027) |
| **Debuggable generated code** | ❌ | ✅ | ✅ |

---

## Where ForgeMap Differentiates

### 1. Automatic Reverse Mapping

Mapperly supports reverse-direction mappings, but you must write each reverse method manually. In CRUD-heavy enterprise applications with hundreds of entity/DTO pairs, this doubles the mapping surface you need to maintain.

**ForgeMap** auto-generates the reverse with a single attribute:

```csharp
[ReverseForge]
public partial OrderDto Forge(OrderEntity source);
// ↑ Auto-generates: public partial OrderEntity Forge(OrderDto source);
```

ForgeMap validates the reverse at compile time — `[ForgeFrom]` resolvers that can't be inverted emit `FM0012`, and nested `[ForgeWith]` without a matching reverse emit `FM0015`.

### 2. Auto-Discovered Polymorphic Dispatch

Both Mapperly and ForgeMap support polymorphic mapping via dispatch. Mapperly requires you to **explicitly enumerate every derived type** with `[MapDerivedType]`:

```csharp
// Mapperly — must list every subtype manually
[MapDerivedType<DerivedAEntity, DerivedADto>]
[MapDerivedType<DerivedBEntity, DerivedBDto>]
[MapDerivedType<DerivedCEntity, DerivedCDto>]
public partial BaseDto Map(BaseEntity source);
```

When you add a new subtype, you must remember to update this list or the mapping silently falls through to the base.

**ForgeMap** auto-discovers all derived forge methods:

```csharp
// ForgeMap — auto-discovers all subtypes at compile time
[ForgeAllDerived]
public partial BaseDto Forge(BaseEntity source);

public partial DerivedADto Forge(DerivedAEntity source);
public partial DerivedBDto Forge(DerivedBEntity source);
public partial DerivedCDto Forge(DerivedCEntity source);
// ↑ Dispatch cascade generated automatically, most-derived first
```

Adding a new subtype? Just add its forge method — no base method changes needed, and no risk of silently falling through to the base mapping.

### 3. Granular Null Handling

Mapperly provides `AllowNullPropertyAssignment`, `ThrowOnPropertyMappingNullMismatch`, and `ThrowOnMappingNullMismatch` at the mapper level. ForgeMap goes further with **4 strategies** configurable at **3 levels** (assembly → forger → per-property):

| Strategy | Behavior | Example |
|---|---|---|
| `NullForgiving` | Append `!` operator (default) | `target.Name = source.Name!;` |
| `SkipNull` | Skip assignment if null | `if (source.Name is { } x) target.Name = x;` |
| `CoalesceToDefault` | Type-aware default | `target.Name = source.Name ?? "";` |
| `ThrowException` | Throw `ArgumentNullException` | `target.Name = source.Name ?? throw new ...;` |

Configuration cascades from assembly → forger → per-property:

```csharp
// Assembly-wide default
[assembly: ForgeMapDefaults(NullPropertyHandling = NullPropertyHandling.CoalesceToDefault)]

// Override for a specific forger
[ForgeMap(NullPropertyHandling = NullPropertyHandling.ThrowException)]
public partial class StrictForger { ... }

// Override for a single property
[ForgeProperty("Email", "ContactEmail", NullPropertyHandling = NullPropertyHandling.SkipNull)]
public partial UserDto Forge(UserEntity source);
```

### 4. Configuration Inheritance

Mapperly added `[IncludeMappingConfiguration]` for reusing mapping configurations. ForgeMap's `[IncludeBaseForge]` provides similar functionality with attribute-level inheritance and override detection:

```csharp
// Base mapping config — shared Ignore, ForgeProperty, ForgeFrom rules
[Ignore(nameof(BaseDto.AuditTrail))]
[ForgeProperty(nameof(BaseEntity.Uid), nameof(BaseDto.Id))]
public partial BaseDto Forge(BaseEntity source);

// Derived mapping — inherits all base config, can override
[IncludeBaseForge(typeof(BaseEntity), typeof(BaseDto))]
[ForgeProperty(nameof(DerivedEntity.ExtraField), nameof(DerivedDto.Extra))]
public partial DerivedDto Forge(DerivedEntity source);
```

Explicit attributes on the derived method override inherited ones, with `FM0021` info diagnostic for visibility.

### 5. First-Class Lifecycle Hooks

Mapperly supports custom pre/post-mapping logic via manual wrapper methods (user-implemented methods that call the generated mapping). ForgeMap provides **declarative attribute-driven hooks** with ordered execution and validated signatures:

```csharp
[BeforeForge(nameof(ValidateSource))]
[AfterForge(nameof(EnrichOrder))]
public partial OrderDto Forge(OrderEntity source);

private static void ValidateSource(OrderEntity source)
{
    ArgumentNullException.ThrowIfNull(source.Id);
}

private static void EnrichOrder(OrderEntity source, OrderDto destination)
{
    destination.DisplayName = $"Order #{source.Id}";
}
```

Hooks run in declaration order with validated signatures (`FM0016` for invalid hooks, `FM0018` for unsupported contexts). In Mapperly, the equivalent requires writing a wrapper method that calls the generated mapping manually.

---

## AutoMapper Migration Path

ForgeMap was designed with a **1:1 concept mapping** from AutoMapper, making migration straightforward:

| AutoMapper | ForgeMap | Notes |
|---|---|---|
| `CreateMap<S, D>()` | `public partial D Forge(S source);` | Declaration becomes a method signature |
| `.ForMember(d => d.X, o => o.Ignore())` | `[Ignore(nameof(D.X))]` | |
| `.ForMember(d => d.X, o => o.MapFrom(s => s.Y))` | `[ForgeProperty(nameof(S.Y), nameof(D.X))]` | |
| `.ForMember(d => d.X, o => o.MapFrom(s => Calc(s)))` | `[ForgeFrom(nameof(D.X), nameof(Calc))]` | |
| `.ConvertUsing<TConverter>()` | `[ConvertWith(typeof(TConverter))]` | Planned — attribute defined, not yet code-generated |
| `.IncludeBase<TSourceBase, TDestinationBase>()` | `[IncludeBaseForge(typeof(TSourceBase), typeof(TDestinationBase))]` | |
| `.IncludeAllDerived()` | `[ForgeAllDerived]` | Auto-discovered, no manual listing |
| `.ReverseMap()` | `[ReverseForge]` | With compile-time validation |
| `.BeforeMap()` / `.AfterMap()` | `[BeforeForge]` / `[AfterForge]` | Ordered, validated signatures |
| `mapper.Map<D>(source)` | `forger.Forge(source)` | |
| `mapper.Map(source, existing)` | Void partial method with `[UseExistingValue]` destination | Method name is arbitrary (e.g. `ForgeInto`) |
| `services.AddAutoMapper(cfg => { }, assemblies...)` | `services.AddForgeMaps()` | Registers as singletons by default (optional `ServiceLifetime` parameter) |

Nearly every AutoMapper concept has a direct ForgeMap equivalent (`[ConvertWith]` is defined but not yet code-generated). Teams migrating from AutoMapper can apply mechanical, pattern-based transformations rather than re-thinking their mapping architecture.

### Automated Migration Skill

ForgeMap ships with an [**automated migration skill**](../.claude/skills/automapper-migration/SKILL.md) (`/automapper-migration`) that automates the migration from AutoMapper in 4 safe, incremental phases. Instead of a manual, error-prone find-and-replace effort, teams can run the skill to convert AutoMapper profiles to ForgeMap forgers, update call sites, and validate the result — all within their existing development workflow.

---

## Compile-Time Safety

ForgeMap provides **27 diagnostic rules** (FM0001–FM0027) that catch mapping errors at compile time:

- **Structural errors** — non-partial class/method, missing constructors, circular dependencies
- **Mapping gaps** — unmapped source/destination properties, unmatched constructor parameters
- **Null safety** — nullable-to-non-nullable assignments with actionable fix suggestions
- **Configuration errors** — invalid resolver signatures, broken `[IncludeBaseForge]` references, ambiguous auto-wiring
- **Polymorphic warnings** — missing derived methods, abstract dispatch risks

These diagnostics surface in the IDE as you type, eliminating entire categories of runtime mapping failures.

---

## Summary

| Decision Factor | Recommendation |
|---|---|
| Simple, flat DTO mapping | Either tool works well |
| `Entity ↔ DTO` round-trips | **ForgeMap** — `[ReverseForge]` auto-generates the inverse |
| Deep inheritance hierarchies | **ForgeMap** — `[ForgeAllDerived]` auto-discovers subtypes |
| Mixed nullability across large codebases | **ForgeMap** — 4 strategies, per-property control |
| Migrating from AutoMapper | **ForgeMap** — familiar API concepts, lowest migration friction |
| Declarative lifecycle hooks | **ForgeMap** — `[BeforeForge]` / `[AfterForge]` vs manual wrappers |

ForgeMap gives you comparable or better performance than Mapperly with **auto-discovered polymorphism, declarative hooks, granular null handling, and a smoother migration path from AutoMapper**.
