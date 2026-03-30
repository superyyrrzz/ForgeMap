# ForgeMap vs Mapperly — Why ForgeMap Is the Better Migration Target

**Mapperly** and **ForgeMap** are both Roslyn incremental source generators that produce zero-reflection, compile-time mapping code at comparable speed (~14–15 ns for simple objects).

However, ForgeMap addresses **critical gaps** in Mapperly that enterprise codebases will hit during migration — particularly around reverse mapping, polymorphic dispatch, null safety, and configuration inheritance.

---

## Feature Comparison

| Capability | AutoMapper | Mapperly | ForgeMap |
|---|---|---|---|
| **Engine** | Runtime reflection | Source generator | Source generator |
| **Performance** | ~80 ns | ~15 ns | ~14.5 ns |
| **License** | Apache 2.0 | Apache 2.0 | MIT |
| **Reverse mapping** | `.ReverseMap()` | ❌ Not supported | ✅ `[ReverseForge]` |
| **Polymorphic dispatch** | Runtime reflection | Manual `[MapDerivedType]` per type | ✅ Auto-discovered `[ForgeAllDerived]` |
| **Abstract destination mapping** | ❌ | ❌ | ✅ Dispatch-only, no constructor needed |
| **Null handling strategies** | `AllowNullCollections` | Binary (throw or allow) | ✅ 4 strategies, 3-tier config |
| **Per-property null control** | ❌ | ❌ | ✅ `[ForgeProperty(..., NullPropertyHandling)]` |
| **Base config inheritance** | `.IncludeBase<TBase>()` | ❌ (open issue [#2000](https://github.com/riok/mapperly/issues/2000)) | ✅ `[IncludeBaseForge]` |
| **Auto-wire nested mappings** | Runtime registry | Manual `UseMapper` | ✅ Compile-time auto-discovery |
| **Lifecycle hooks** | `.BeforeMap()` / `.AfterMap()` | `BeforeMap` / `AfterMap` | ✅ `[BeforeForge]` / `[AfterForge]` with ordered execution |
| **Mutation mapping** | `Map(src, dest)` | `Map(src, dest)` | ✅ Partial-method mutation pattern with `[UseExistingValue]` destination |
| **Collection auto-generation** | Runtime | Partial | ✅ Full (`T[]`, `List<T>`, `IEnumerable<T>`, `HashSet<T>`, etc.) |
| **Inline collection mapping** | N/A | ❌ | ✅ Generates inline iteration, no explicit method needed |
| **Diagnostics** | Runtime exceptions | ~20 diagnostics | ✅ 27 diagnostics (FM0001–FM0027) |
| **Debuggable generated code** | ❌ | ✅ | ✅ |

---

## Where Mapperly Falls Short

### 1. No Reverse Mapping

Mapperly has **no built-in way to generate the inverse mapping**. If you map `Entity → DTO`, you must write a second mapper method by hand for `DTO → Entity`. In CRUD-heavy enterprise applications with hundreds of entity/DTO pairs, this doubles the mapping surface you need to maintain.

**ForgeMap** solves this with a single attribute:

```csharp
[ReverseForge]
public partial OrderDto Forge(OrderEntity source);
// ↑ Auto-generates: public partial OrderEntity Forge(OrderDto source);
```

ForgeMap validates the reverse at compile time — `[ForgeFrom]` resolvers that can't be inverted emit `FM0012`, and nested `[ForgeWith]` without a matching reverse emit `FM0015`.

### 2. Manual Polymorphic Dispatch

Mapperly requires you to **explicitly enumerate every derived type** on the base mapping method:

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

Adding a new subtype? Just add its forge method — no base method changes needed. ForgeMap even supports **abstract destination types** (dispatch-only with `NotSupportedException` fallback), which Mapperly cannot handle.

### 3. Primitive Null Handling

Mapperly offers a binary choice: throw on null mismatch or allow it. In a real-world codebase with mixed nullability across hundreds of DTOs, you need more control.

**ForgeMap** provides **4 strategies** at **3 levels of configuration**:

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

### 4. No Configuration Inheritance

One of Mapperly's [most-requested features (#2000)](https://github.com/riok/mapperly/issues/2000) is the ability to inherit base mapping configuration in derived type mappings. It remains unimplemented.

**ForgeMap** ships this today:

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

### 5. No Auto-Wiring for Nested Mappings

When a destination property is a complex type with a matching forge method, Mapperly requires explicit `UseMapper` registration. ForgeMap **auto-discovers** matching methods at compile time:

```csharp
[ForgeMap(AutoWireNestedMappings = true)] // default
public partial class AppForger
{
    public partial OrderDto Forge(OrderEntity source);
    public partial CustomerDto Forge(CustomerEntity source);
    public partial AddressDto Forge(AddressEntity source);
    // ↑ OrderDto.Customer and CustomerDto.Address are auto-wired
    //   No [ForgeWith] attributes needed
}
```

This eliminates dozens of boilerplate attributes in large mapping configurations. The feature emits `FM0025` if multiple candidate methods create ambiguity, and `FM0027` (info, disabled by default) when a property is auto-wired.

---

## AutoMapper Migration Path

ForgeMap was designed with a **1:1 concept mapping** from AutoMapper, making migration straightforward:

| AutoMapper | ForgeMap | Notes |
|---|---|---|
| `CreateMap<S, D>()` | `public partial D Forge(S source);` | Declaration becomes a method signature |
| `.ForMember(d => d.X, o => o.Ignore())` | `[Ignore(nameof(D.X))]` | |
| `.ForMember(d => d.X, o => o.MapFrom(s => s.Y))` | `[ForgeProperty(nameof(S.Y), nameof(D.X))]` | |
| `.ForMember(d => d.X, o => o.MapFrom(s => Calc(s)))` | `[ForgeFrom(nameof(D.X), nameof(Calc))]` | |
| `.ConvertUsing<TConverter>()` | `[ConvertWith(typeof(TConverter))]` | |
| `.IncludeBase<TBase>()` | `[IncludeBaseForge(typeof(S), typeof(D))]` | |
| `.IncludeAllDerived()` | `[ForgeAllDerived]` | Auto-discovered, no manual listing |
| `.ReverseMap()` | `[ReverseForge]` | With compile-time validation |
| `.BeforeMap()` / `.AfterMap()` | `[BeforeForge]` / `[AfterForge]` | Ordered, validated signatures |
| `mapper.Map<D>(source)` | `forger.Forge(source)` | |
| `mapper.Map(source, existing)` | `forger.ForgeInto(source, existing)` | |
| `services.AddAutoMapper(assemblies)` | `services.AddForgeMaps()` | Registers as singletons by default (optional `ServiceLifetime` parameter) |

Every AutoMapper concept has a direct ForgeMap equivalent. Teams migrating from AutoMapper can apply mechanical, pattern-based transformations rather than re-thinking their mapping architecture.

### Automated Migration Skill

ForgeMap ships with a [**Claude Code skill**](../.claude/skills/automapper-migration/SKILL.md) (`/automapper-migration`) that automates the migration from AutoMapper in 4 safe, incremental phases. Instead of a manual, error-prone find-and-replace effort, teams can run the skill to convert AutoMapper profiles to ForgeMap forgers, update call sites, and validate the result — all within their existing development workflow.

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
| `Entity ↔ DTO` round-trips | **ForgeMap** — `[ReverseForge]` eliminates duplicate code |
| Deep inheritance hierarchies | **ForgeMap** — `[ForgeAllDerived]` + `[IncludeBaseForge]` |
| Mixed nullability across large codebases | **ForgeMap** — 4 strategies, per-property control |
| Migrating from AutoMapper | **ForgeMap** — 1:1 concept mapping, lowest migration friction |
| Minimizing mapping boilerplate | **ForgeMap** — auto-wiring, reverse mapping, config inheritance |

ForgeMap gives you the same performance as Mapperly with **fewer gaps, less boilerplate, and a smoother migration from AutoMapper**.
