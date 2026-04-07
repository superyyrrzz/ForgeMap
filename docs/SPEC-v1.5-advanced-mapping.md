# ForgeMap v1.5 Specification — Migration-Priority Features

## Overview

v1.5 prioritizes three features driven by real-world AutoMapper → ForgeMap migration pain. These gaps were identified during the [Docs.LocalizationContentService](https://dev.azure.com/ceapex/Engineering/_git/Docs.LocalizationContentService) migration, where they collectively forced ~130+ lines of manual boilerplate that ForgeMap should be generating.

| # | Feature | Issue | Effort | Status |
|---|---------|-------|--------|--------|
| 1 | `CoalesceToNew` null-property strategy | [#91](https://github.com/superyyrrzz/ForgeMap/issues/91) | Low | Planned |
| 2 | Collection type coercion | [#90](https://github.com/superyyrrzz/ForgeMap/issues/90) | Medium | Planned |
| 3 | Standalone collection mapping methods | [#89](https://github.com/superyyrrzz/ForgeMap/issues/89) | Medium | Planned |

### Deferred to v1.6

The following features were originally planned for v1.5 but have been moved to v1.6 to prioritize migration-blocking issues #89, #90, and #91:

| Feature | Issue | Notes |
|---------|-------|-------|
| Auto-flattening with `init`/`required` support | [#82](https://github.com/superyyrrzz/ForgeMap/issues/82) | See [SPEC-v1.6-advanced-mapping.md](SPEC-v1.6-advanced-mapping.md) |
| Dictionary-to-typed-object mapping (`[ForgeDictionary]`) | [#83](https://github.com/superyyrrzz/ForgeMap/issues/83) | See [SPEC-v1.6-advanced-mapping.md](SPEC-v1.6-advanced-mapping.md) |

---

## Feature 1: `CoalesceToNew` NullPropertyHandling Strategy

> **Issue:** [#91](https://github.com/superyyrrzz/ForgeMap/issues/91)

### Problem

During AutoMapper → ForgeMap migration, a common pattern is null-coalescing a nested object to an empty instance. AutoMapper does this implicitly — when a source member is `null`, it creates a new destination instance with default values (not `null`). ForgeMap's existing `CoalesceToDefault` strategy produces `default(T)`, which is `null` for reference types — the same as not handling the null at all.

```csharp
// Manual code required today — ForgeMap can't express this
Service = source.Service != null
    ? _forger.ForgeServiceViewObject(source.Service)
    : new ApiModels.ServiceViewObject(),
```

This is especially common for:
- Nested DTOs that consumers expect to be non-null
- View models where `null` would cause `NullReferenceException` in templates
- API responses where the contract requires an object (not `null`)

### Design

Add a new value to the existing `NullPropertyHandling` enum. The generator emits `new T()` for reference types when the source property is null, and `default(T)` for value types (identical to `CoalesceToDefault` for value types).

### API Surface

```csharp
public enum NullPropertyHandling
{
    NullForgiving,      // existing (0): assign with !
    SkipNull,           // existing (1): skip assignment
    CoalesceToDefault,  // existing (2): default(T) — null for reference types
    ThrowException,     // existing (3): throw
    CoalesceToNew,      // NEW (4): new T() for reference types, default(T) for value types
}
```

No new attributes required. Works at all three existing configuration tiers:

```csharp
// Assembly-level
[assembly: ForgeMapDefaults(NullPropertyHandling = NullPropertyHandling.CoalesceToNew)]

// Forger-level
[ForgeMap(NullPropertyHandling = NullPropertyHandling.CoalesceToNew)]

// Per-property
[ForgeProperty("Service", "Service", NullPropertyHandling = NullPropertyHandling.CoalesceToNew)]
```

### Generated Code

**Simple property (no forge method):**

```csharp
// NullPropertyHandling.CoalesceToNew on a reference-type property
__result.Address = source.Address ?? new global::MyApp.Address();

// NullPropertyHandling.CoalesceToNew on a value-type property (same as CoalesceToDefault)
__result.Count = source.Count;  // value types can't be null
```

**With auto-wired forge method:**

When `CoalesceToNew` is active and a matching forge method exists for the property type:

```csharp
// Source non-null → call forge method; source null → new TDestination()
__result.Service = source.Service is { } __v_Service
    ? ForgeServiceViewObject(__v_Service)
    : new global::ApiModels.ServiceViewObject();
```

**In object initializer (init/required properties):**

```csharp
var __result = new OrderDto
{
    // init/required properties with CoalesceToNew
    Service = source.Service is { } __v_Service
        ? ForgeServiceViewObject(__v_Service)
        : new global::ApiModels.ServiceViewObject(),
};
```

**Inline collection properties:**

```csharp
// CoalesceToNew on a collection property — empty collection instead of null
__result.Items = source.Items is { } __v_Items
    ? __v_Items.Select(__item => ForgeItem(__item)).ToList()
    : new global::System.Collections.Generic.List<global::ItemDto>();
```

### Validation

The generator must validate that the destination type is constructible when `CoalesceToNew` is used on a reference-type property:

- **Plain reference types**: Must have an accessible parameterless constructor. Emit **FM0038** if missing.
- **Collection/dictionary properties**: The generator uses type-aware empty collection expressions (`new List<T>()`, `new HashSet<T>()`, `new Dictionary<K,V>()`, `Array.Empty<T>()`, etc.) based on the destination collection type — parameterless constructor validation is not applied to these types. If the destination collection type is not a recognized collection, FM0038 is emitted.
- **Value types**: `CoalesceToNew` is always valid and behaves identically to `CoalesceToDefault` (both produce `default(T)`).

### Interaction with Existing Features

| Feature | Interaction |
|---------|-------------|
| `CoalesceToDefault` | `CoalesceToNew` differs only for reference types: `new T()` vs `null` |
| Auto-wired forge methods | Non-null → forge method; null → `new TDest()` |
| `[ConvertWith]` | `NullHandling` (method-level) controls null source before converter is called; `CoalesceToNew` applies to individual properties, not converter methods |
| `[ForgeFrom]` resolver | Resolver is called when source is non-null; `CoalesceToNew` produces `new T()` when source property is null |
| Inline collection mapping | Empty collection (`new List<T>()`, `new T[0]`, etc.) instead of null |
| `ExistingTarget = true` | `CoalesceToNew` on a null target property creates a new instance and assigns it (same as `CoalesceToDefault` but with `new T()` instead of `default`) |
| `[ReverseForge]` | Reverse method inherits `NullPropertyHandling` — `CoalesceToNew` applies symmetrically |

### Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| **FM0038** | Error | `CoalesceToNew` requires type '{0}' to have an accessible parameterless constructor (not applicable to recognized collection types, which use type-aware empty expressions) |

### Behavioral Contract

| Scenario | Behavior |
|----------|----------|
| Reference-type property, source is null | `new T()` |
| Reference-type property, source is non-null | Assign directly (or call forge method) |
| Value-type property, source is any value | Same as `CoalesceToDefault` — `default(T)` |
| Collection property, source is null | Empty collection (`new List<T>()`, `new HashSet<T>()`, etc.) |
| Destination type has no parameterless constructor | FM0038 error |
| Used with forge method, source null | `new TDestination()` (forge method NOT called) |
| Used with `[ForgeFrom]` resolver, source null | `new TDestination()` (resolver NOT called) |

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.5 |
|--------|-----------|---------|---------------|
| Null → new instance | ✅ Default behavior | ❌ Null produces null | ✅ `CoalesceToNew` opt-in |
| Configuration level | Global only | N/A | ✅ Assembly / forger / per-property |
| Constructor validation | Runtime error | N/A | ✅ Compile-time FM0038 |

---

## Feature 2: Collection Type Coercion

> **Issue:** [#90](https://github.com/superyyrrzz/ForgeMap/issues/90)

### Problem

ForgeMap currently handles inline collection mapping when element types differ (via auto-wired forge methods), but requires the source and destination collection *wrapper* types to be compatible. When they differ — e.g., `List<string>` → `HashSet<string>`, `IDictionary<K,V>` → `ReadOnlyDictionary<K,V>` — the property is skipped or requires manual mapping.

In the LCS migration, this forced ~57 lines of manual mapping across two methods:

```csharp
// ForgeMap can't generate this — List<string> → HashSet<string>
DisallowedLocales = source.DisallowedLocales != null
    ? new HashSet<string>(source.DisallowedLocales)
    : new HashSet<string>(),

// ForgeMap can't generate this — IDictionary<K,V> → ReadOnlyDictionary<K,V>
metadata: source.Metadata?.AsReadOnly(),
locMetadata: source.LocMetadata?.AsReadOnly(),
versionIndependentMetadata: source.VersionIndependentMetadata?.AsReadOnly(),
```

### Design

When the generator detects a property pair where the source and destination are different collection/dictionary types but the element types are compatible (same type, or a forge method exists), it automatically emits the appropriate conversion code. This is a natural extension of ForgeMap's existing collection handling — no new attributes or configuration required.

### Conversion Matrix

#### Sequence Collections

| Source Type | Destination Type | Generated Code |
|-------------|-----------------|----------------|
| `List<T>` / `IList<T>` / `IEnumerable<T>` / `ICollection<T>` | `HashSet<T>` | `new HashSet<T>(source.Prop)` |
| `List<T>` / `IList<T>` / `T[]` | `IReadOnlyList<T>` | `source.Prop` (implicit — `List<T>` implements `IReadOnlyList<T>`) or `.ToList()` for non-list sources |
| `List<T>` / `IList<T>` | `ReadOnlyCollection<T>` | `source.Prop.AsReadOnly()` (for `List<T>`); `new ReadOnlyCollection<T>(source.Prop.ToList())` (for `IList<T>`) |
| `IEnumerable<T>` / `IReadOnlyList<T>` | `T[]` | `source.Prop.ToArray()` |
| `T[]` / `IEnumerable<T>` | `List<T>` | `new List<T>(source.Prop)` |
| `IEnumerable<T>` | `ICollection<T>` | `new List<T>(source.Prop)` |
| `HashSet<T>` | `IReadOnlyCollection<T>` | `source.Prop` (implicit) |

#### Dictionary Collections

| Source Type | Destination Type | Generated Code |
|-------------|-----------------|----------------|
| `IDictionary<K,V>` / `Dictionary<K,V>` | `ReadOnlyDictionary<K,V>` | `new ReadOnlyDictionary<K,V>(new Dictionary<K,V>(source.Prop))` — copies to avoid aliasing |
| `Dictionary<K,V>` | `IReadOnlyDictionary<K,V>` | `new ReadOnlyDictionary<K,V>(new Dictionary<K,V>(source.Prop))` — copies to avoid aliasing |
| `IDictionary<K,V>` | `IReadOnlyDictionary<K,V>` | `new ReadOnlyDictionary<K,V>(new Dictionary<K,V>(source.Prop))` — copies to avoid aliasing |
| `IReadOnlyDictionary<K,V>` | `Dictionary<K,V>` | `new Dictionary<K,V>(source.Prop)` |

#### Element Mapping with Coercion

When elem types differ AND collection types differ, both conversions apply:

```csharp
// List<SourceItem> → HashSet<DestItem> with element-level forge method
__result.Tags = source.Tags is { } __v_Tags
    ? new global::System.Collections.Generic.HashSet<global::DestItem>(
        global::System.Linq.Enumerable.Select(__v_Tags, __item => ForgeItem(__item)))
    : null!;  // NullPropertyHandling governs
```

### Null Handling

Collection type coercion respects `NullPropertyHandling`:

| `NullPropertyHandling` | Behavior |
|-------------------------|----------|
| `NullForgiving` | `source.Prop is { } __v ? <coerce>(__v) : null!` |
| `SkipNull` | `if (source.Prop is { } __v) __result.Prop = <coerce>(__v);` |
| `CoalesceToDefault` | `source.Prop is { } __v ? <coerce>(__v) : default` |
| `CoalesceToNew` | `source.Prop is { } __v ? <coerce>(__v) : new HashSet<T>()` (empty target collection) |
| `ThrowException` | `source.Prop is { } __v ? <coerce>(__v) : throw new ArgumentNullException(...)` |

### Interaction with Existing Features

| Feature | Interaction |
|---------|-------------|
| Inline collection mapping (auto-wiring) | Coercion extends existing inline collection code — element forge methods still called per-element |
| `[ForgeProperty]` | Explicit property mapping takes precedence; coercion applies to the mapped property pair |
| `ExistingTarget = true` | Collection coercion is compatible with `CollectionUpdateStrategy.Replace`; for `Add`/`Sync`, the existing collection's type is preserved |
| `[ConvertWith]` | Converter takes full precedence — coercion does not apply |
| `CoalesceToNew` (Feature 1) | Produces empty target collection type (e.g., `new HashSet<T>()`) when source is null |
| Standalone collection methods (Feature 3) | Coercion applies to return types of collection methods too |

### Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| **FM0039** | Info | Property '{0}' collection type coerced from '{1}' to '{2}' (disabled by default) |
| **FM0040** | Warning | Property '{0}': no known coercion from '{1}' to '{2}'; property skipped |

### Behavioral Contract

| Scenario | Behavior |
|----------|----------|
| Same collection type, same element type | Direct assignment (existing behavior, no coercion needed) |
| Different collection type, same element type | Coerce via conversion matrix |
| Different collection type, different element type with forge method | Coerce + element mapping |
| Unknown collection type pair | FM0040 warning, property skipped |
| Source is null | Follows `NullPropertyHandling` |
| Dictionary key type mismatch | Not supported — FM0040 warning |

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.5 |
|--------|-----------|---------|---------------|
| List→HashSet | ✅ Runtime | ❌ Limited | ✅ Compile-time |
| IDictionary→ReadOnlyDictionary | ✅ Runtime | ❌ Not supported | ✅ Compile-time |
| List→ReadOnlyCollection | ✅ Runtime | ✅ Compile-time | ✅ Compile-time |
| Element mapping + coercion | ✅ Runtime | ✅ Partial | ✅ Compile-time |
| Null handling | Runtime | Compile-time | ✅ Full `NullPropertyHandling` |
| Diagnostic visibility | None | Compile-time | ✅ FM0039/FM0040 |

---

## Feature 3: Standalone Collection Mapping Methods

> **Issue:** [#89](https://github.com/superyyrrzz/ForgeMap/issues/89)

### Problem

AutoMapper auto-maps collections from a single `CreateMap<A, B>()` declaration — calling `mapper.Map<IReadOnlyList<B>>(listOfA)` just works. ForgeMap generates element-level forge methods and handles collections as *properties* inside parent objects (inline iteration), but does **not** generate standalone collection mapping methods.

In the LCS migration, this forced ~78 lines of hand-written collection dispatch:

```csharp
// 5 repetitive blocks like this:
if (elementType == typeof(FileRequest)
    && source is IEnumerable<FileResponse> fileResponses)
{
    return (TDestination)(object)fileResponses
        .Select(s => _forger.ForgeFileRequest(s)).ToList().AsReadOnly();
}
```

Each block is the same pattern: iterate source, call forge method, collect into list. This is pure boilerplate that the source generator should eliminate.

### Design

The user declares a partial method with a collection parameter and collection return type. The generator discovers the matching element-level forge method (by matching source/destination element types) and implements the body. This is consistent with ForgeMap's existing pattern: user declares partial method signature, generator implements.

### Usage

```csharp
[ForgeMap]
public partial class ClientForger
{
    // Element mapping — user declares, generator implements
    public partial FileRequest ForgeFileRequest(FileResponse source);
    public partial GroupItemRequest ForgeGroupItemRequest(GroupItemResponse source);

    // Collection mapping — user declares, generator implements
    // Generator discovers ForgeFileRequest(FileResponse) as the element method
    public partial IReadOnlyList<FileRequest> ForgeFileRequests(
        IEnumerable<FileResponse> source);

    // Array return type
    public partial GroupItemRequest[] ForgeGroupItemRequests(
        IReadOnlyList<GroupItemResponse> source);

    // List return type
    public partial List<FileRequest> ForgeFileRequestList(
        FileResponse[] source);
}
```

### Resolution Algorithm

For each unimplemented partial method on a `[ForgeMap]` class that does not match the element forge method signature pattern (single non-collection parameter → single non-collection return):

1. **Detect collection signature**: The method has exactly one parameter whose type is a recognized collection type (`IEnumerable<T>`, `IReadOnlyList<T>`, `IReadOnlyCollection<T>`, `ICollection<T>`, `IList<T>`, `List<T>`, `T[]`, `HashSet<T>`) and a return type that is also a recognized collection type
2. **Extract element types**: Source element type `TSource` from the parameter's collection, destination element type `TDest` from the return type's collection
3. **Find matching element method**: Search for a forge method on the same forger with signature `TDest MethodName(TSource source)`. The match is by type only — method name does not matter
4. **Validate uniqueness**: If multiple element methods match the same `TSource → TDest` pair, emit **FM0042** (ambiguous). The user must remove one element method or rename to disambiguate
5. **Generate body**: Iterate source collection, call element method per item, collect into declared return type

### Generated Code

**`IReadOnlyList<T>` return type:**

```csharp
public partial IReadOnlyList<FileRequest> ForgeFileRequests(
    IEnumerable<FileResponse> source)
{
    if (source == null) return null!;  // NullHandling.ReturnNull (default)

    var __result = new global::System.Collections.Generic.List<global::FileRequest>();
    foreach (var __item in source)
    {
        __result.Add(ForgeFileRequest(__item));
    }
    return __result;
}
```

**`T[]` return type:**

```csharp
public partial GroupItemRequest[] ForgeGroupItemRequests(
    IReadOnlyList<GroupItemResponse> source)
{
    if (source == null) return null!;

    var __result = new global::GroupItemRequest[source.Count];
    for (var __i = 0; __i < source.Count; __i++)
    {
        __result[__i] = ForgeGroupItemRequest(source[__i]);
    }
    return __result;
}
```

**`List<T>` return type:**

```csharp
public partial List<FileRequest> ForgeFileRequestList(
    FileResponse[] source)
{
    if (source == null) return null!;

    var __result = new global::System.Collections.Generic.List<global::FileRequest>(source.Length);
    foreach (var __item in source)
    {
        __result.Add(ForgeFileRequest(__item));
    }
    return __result;
}
```

**`IEnumerable<T>` return type (lazy):**

```csharp
public partial IEnumerable<FileRequest> ForgeFileRequestsLazy(
    IEnumerable<FileResponse> source)
{
    if (source == null) return null!;

    return global::System.Linq.Enumerable.Select(source,
        __item => ForgeFileRequest(__item));
}
```

**`HashSet<T>` return type:**

```csharp
public partial HashSet<DestItem> ForgeDestItems(
    IEnumerable<SourceItem> source)
{
    if (source == null) return null!;

    var __result = new global::System.Collections.Generic.HashSet<global::DestItem>();
    foreach (var __item in source)
    {
        __result.Add(ForgeItem(__item));
    }
    return __result;
}
```

### Null Handling

Collection methods follow the forger's `NullHandling` setting (method-level), not `NullPropertyHandling` (property-level):

| `NullHandling` | Generated Code |
|----------------|----------------|
| `ReturnNull` (default) | `if (source == null) return null!;` |
| `ThrowException` | `if (source == null) throw new global::System.ArgumentNullException(nameof(source));` |

### Pre-sized Collections

When the source parameter type exposes a cheap `.Count` or `.Length` property (`IReadOnlyCollection<T>`, `ICollection<T>`, `IList<T>`, `List<T>`, `T[]`), the generated `List<T>` is pre-sized:

```csharp
var __result = new List<T>(source.Count);  // or source.Length for arrays
```

When the source is `IEnumerable<T>` (no known count), no pre-sizing is applied:

```csharp
var __result = new List<T>();
```

### Interaction with Existing Features

| Feature | Interaction |
|---------|-------------|
| Inline collection properties | Existing behavior unchanged — inline iteration handles collection properties within parent mappings. Standalone methods are for top-level collection mapping |
| `[ForgeProperty]` / `[ForgeFrom]` / `[Ignore]` | Not applicable — these are method-level, not property-level. Collection methods have no properties to configure |
| `NullHandling` | `ReturnNull` or `ThrowException` — follows forger-level setting |
| `[ConvertWith]` | Mutually exclusive — `[ConvertWith]` takes full control of a method body. Collection methods must not have `[ConvertWith]` |
| `[ForgeAllDerived]` | Not applicable to collection methods |
| `[ReverseForge]` | Not supported on collection methods — declare a separate collection method for the reverse direction |
| Collection type coercion (Feature 2) | Coercion applies to the return type — e.g., element method returns `DestItem`, collection method returns `HashSet<DestItem>` |
| `CoalesceToNew` (Feature 1) | Not applicable — `NullHandling` governs null source, not `NullPropertyHandling` |

### Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| **FM0041** | Warning | Collection method '{0}' declared but no matching element forge method found for '{1}' → '{2}' |
| **FM0042** | Error | Collection method '{0}' is ambiguous: multiple element forge methods match '{1}' → '{2}'. Remove one element method or rename to disambiguate |

### Behavioral Contract

| Scenario | Behavior |
|----------|----------|
| Matching element method exists | Generate collection iteration body |
| No matching element method | FM0041 warning, method body not generated |
| Multiple matching element methods | FM0042 error |
| Source is null, `NullHandling = ReturnNull` | Returns `null!` |
| Source is null, `NullHandling = ThrowException` | Throws `ArgumentNullException` |
| Element method throws for an element | Exception propagates — no per-element error handling |
| Empty source collection | Returns empty destination collection |
| `[ConvertWith]` on collection method | Converter takes precedence, collection generation skipped |
| Collection method + `[ReverseForge]` | Not supported — declare separate method for reverse |

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.5 |
|--------|-----------|---------|---------------|
| Collection from element mapping | ✅ Implicit (runtime) | ✅ Auto-generated (compile-time) | ✅ User declares partial, generator implements |
| Return type control | Limited (`List<T>` default) | Limited | ✅ Full control via declared return type |
| Naming | N/A (same `Map<T>()` call) | Auto-generated | ✅ User controls method name |
| Lazy enumeration | ❌ Always materialized | ❌ Always materialized | ✅ `IEnumerable<T>` return → lazy `Select` |
| Pre-sized collections | ❌ Runtime | ❌ | ✅ When source has `.Count`/`.Length` |
| Null handling | Runtime config | Compile-time | ✅ `NullHandling` on forger |

---

## Diagnostics Summary

| Code | Severity | Category | Feature | Description |
|------|----------|----------|---------|-------------|
| FM0038 | Error | `ForgeMap` | `CoalesceToNew` | `CoalesceToNew` requires type '{0}' to have an accessible parameterless constructor |
| FM0039 | Info | `ForgeMap` | Collection coercion | Property '{0}' collection type coerced from '{1}' to '{2}' (disabled by default) |
| FM0040 | Warning | `ForgeMap` | Collection coercion | Property '{0}': no known coercion from '{1}' to '{2}'; property skipped |
| FM0041 | Warning | `ForgeMap` | Collection methods | Collection method '{0}' declared but no matching element forge method found |
| FM0042 | Error | `ForgeMap` | Collection methods | Collection method '{0}' is ambiguous: multiple element forge methods match |

---

## API Changes Summary

### New Enum Values (v1.5)

| Enum | New Value | Description |
|------|-----------|-------------|
| `NullPropertyHandling` | `CoalesceToNew = 4` | `new T()` for null reference types, `default(T)` for value types |

### No New Attributes

All three features work within the existing attribute surface — no new attributes are introduced.

---

## Migration Guide

### From v1.4 to v1.5

v1.5 introduces no required source changes and no API-surface breaks. Three behavior items to be aware of:

1. **`CoalesceToNew` (opt-in)** — New enum value added to `NullPropertyHandling`. No behavior change unless explicitly opted in via attribute configuration.

2. **Collection type coercion (automatic)** — Properties where source and destination differ only in collection wrapper type (e.g., `List<string>` → `HashSet<string>`) will now be automatically mapped instead of skipped with FM0006. This **may change behavior** for projects that relied on the property being unmapped. To suppress coercion for a specific property, use `[Ignore]`. If the previous unmapped warning (FM0006) was suppressed, the property is now auto-mapped — review the generated code.

3. **Standalone collection methods (opt-in)** — Declaring a partial method with collection types triggers collection method generation. Existing forgers are unaffected unless new partial methods are added.

---

## Limitations

| Limitation | Reason | Workaround |
|-----------|--------|------------|
| `CoalesceToNew` requires parameterless constructor | Generator emits `new T()` — cannot know which constructor parameters to supply | Use `[ForgeFrom]` resolver for types without parameterless constructors |
| Dictionary coercion limited to same key type | Key type conversion adds combinatorial complexity | Use `[ForgeFrom]` resolver for key-type conversions |
| Collection methods don't support `[ReverseForge]` | Reverse collection mapping is the same pattern — declare a separate method | Declare `partial IReadOnlyList<A> Forge(IEnumerable<B> source)` as the reverse |
| Collection methods require exactly one matching element method | Ambiguity is a compile-time error | Remove one element method or rename to disambiguate |

---

*Specification Version: 1.5 (2026-04-07)*
*Status: Planned*
*Deferred features: [SPEC-v1.6-advanced-mapping.md](SPEC-v1.6-advanced-mapping.md)*
*License: MIT*
