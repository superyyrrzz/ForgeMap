# ForgeMap v1.3 Specification — Auto-Wiring & Abstract Dispatch

## Overview

v1.3 addresses the three biggest remaining friction points in AutoMapper-to-ForgeMap migrations, eliminating ~80% of boilerplate that users encounter when mapping complex object graphs.

| Issue | Feature | Impact |
|-------|---------|--------|
| [#59](https://github.com/superyyrrzz/ForgeMap/issues/59) | Abstract/interface destination in `[ForgeAllDerived]` | Eliminates ~30 lines manual dispatch per forger |
| [#60](https://github.com/superyyrrzz/ForgeMap/issues/60) | Auto-discover nested forge methods | Eliminates ~18 `[ForgeWith]` attributes per forger |
| [#61](https://github.com/superyyrrzz/ForgeMap/issues/61) | Inline collection forge generation | Eliminates ~5 explicit collection method declarations per forger |

---

## Feature 1: Abstract Destination Dispatch

### Problem

`[ForgeAllDerived]` emits FM0004 when the destination type has no accessible constructor (abstract class or interface), forcing users to write manual polymorphic dispatch:

```csharp
// v1.2: ForgeMap cannot generate this — destination is abstract → FM0004
public UDSModels.QuestionnaireChildBase Forge(QuestionnaireChildBase source)
{
    if (source is null) return null!;
    return source switch
    {
        NormalSelectQuestion nsq => Forge(nsq),
        QuestionSet qs => Forge(qs),
        _ => throw new NotSupportedException(
            $"No mapping for subtype {source.GetType().Name}")
    };
}
```

### Design

When `[ForgeAllDerived]` is present and the destination type is abstract or an interface, the generator:

1. **Skips** constructor resolution (no FM0004)
2. **Skips** property assignment (abstract types cannot be instantiated)
3. Emits **only** the is-cascade dispatch + a `throw NotSupportedException` fallback

When the destination is concrete (existing behavior unchanged), the is-cascade is followed by a base-type mapping fallback as in v1.2.

No new API surface — this is a behavioral enhancement to `[ForgeAllDerived]`.

### Usage

```csharp
[ForgeMap]
public partial class AppForger
{
    // v1.3: Works! Generator emits dispatch-only body
    [ForgeAllDerived]
    public partial UDSModels.QuestionnaireChildBase Forge(QuestionnaireChildBase source);

    // Concrete derived mappings (discovered by [ForgeAllDerived])
    public partial UDSModels.NormalSelectQuestion Forge(NormalSelectQuestion source);
    public partial UDSModels.QuestionSet Forge(QuestionSet source);
}
```

### Generated Code

```csharp
public partial UDSModels.QuestionnaireChildBase Forge(QuestionnaireChildBase source)
{
    if (source == null) return null!;

    // Polymorphic dispatch — most-derived types checked first.
    // Each branch returns immediately, so [BeforeForge]/[AfterForge] hooks
    // declared on THIS method do NOT run — the derived method's own hooks
    // run instead when Forge(derived) is called.
    if (source is NormalSelectQuestion normalSelectQuestion) return Forge(normalSelectQuestion);
    if (source is QuestionSet questionSet) return Forge(questionSet);

    throw new global::System.NotSupportedException(
        $"No forge mapping for source type '{source.GetType().FullName}' " +
        $"to abstract destination type 'UDSModels.QuestionnaireChildBase'.");
}
```

### Diagnostics

| Code | Severity | Change | Description |
|------|----------|--------|-------------|
| FM0004 | Error | Modified | No longer raised when `[ForgeAllDerived]` is present on a method with abstract/interface destination |
| FM0022 | Warning | Modified | Message updated: for abstract/interface destinations, warns that dispatch-only body has no base-type fallback (previously stated "will only map the base type") |
| **FM0024** | Warning | **New** | `[ForgeAllDerived]` on abstract/interface destination — unmatched source subtypes will throw `NotSupportedException` at runtime |

### Behavioral Contract

| Scenario | Behavior |
|----------|----------|
| Abstract class destination | Dispatch-only body, `throw` fallback |
| Interface destination | Dispatch-only body, `throw` fallback |
| Concrete destination (unchanged) | Is-cascade + base-type mapping fallback |
| No derived methods found | FM0022 warning, base mapping fallback (concrete) or `throw` (abstract) |
| `[ForgeAllDerived]` + `[ConvertWith]` | FM0023 error (unchanged) |
| `[BeforeForge]` on abstract dispatch | **Not executed** — dispatch branches `return` immediately before hooks are reached; each derived method's own hooks run instead |
| `[AfterForge]` on abstract dispatch | **Not executed** — dispatch branches `return` immediately; no destination instance exists for dispatch-only methods |
| Null source | `return null!` (or throw per `NullHandling`) |

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.3 |
|--------|-----------|---------|---------------|
| API | `.Include<>()` / `.IncludeAllDerived()` | `[MapDerivedType<,>]` per subtype | `[ForgeAllDerived]` (auto-discovers) |
| Discovery | Runtime registry scan | Manual enumeration | Compile-time auto-discovery |
| Dispatch | Runtime reflection | Compile-time `switch` | Compile-time is-cascade |
| Fallback | Maps as base type | Hard `throw` | Base mapping (concrete) or `throw` (abstract) |

---

## Feature 2: Auto-Discover Nested Forge Methods

### Problem

Users must write explicit `[ForgeWith]` for every nested complex property, even when a matching forge method exists:

```csharp
// v1.2: 18 [ForgeWith] attributes needed across forger class
[ForgeWith(nameof(SelfAssessmentSnapshot.Guidance), nameof(Forge))]
[ForgeWith(nameof(SelfAssessmentSnapshot.ActivityLog), nameof(Forge))]
[ForgeWith(nameof(SelfAssessmentSnapshot.RelatedResources), nameof(Forge))]
public partial SelfAssessmentSnapshot Forge(SelfAssessment source);
```

### Design

The generator auto-discovers matching forge methods for nested properties. **Enabled by default** (`AutoWireNestedMappings = true`).

### Usage

```csharp
// v1.3: No [ForgeWith] needed — forge methods are auto-discovered
[ForgeMap]
public partial class AppForger
{
    public partial UserDto Forge(UserEntity source);
    public partial AddressDto Forge(AddressEntity source);
    // Generator finds Forge(AddressEntity) → AddressDto for UserDto.Address
}

// Opt out for explicit-only behavior (v1.2 style):
[ForgeMap(AutoWireNestedMappings = false)]
public partial class ExplicitForger { ... }
```

### Resolution Algorithm

For each destination property during code generation:

1. **Explicit wins**: If the property has `[ForgeWith]`, `[ForgeFrom]`, `[Ignore]`, or a `[ForgeProperty]` to a directly assignable source — no auto-wiring
2. **Find source property**: Match by name convention or `[ForgeProperty]` mapping
3. **Check assignability**: If source type is directly assignable to destination type, or is a primitive/enum/string — use direct assignment (no auto-wiring needed)
4. **Flattening takes precedence**: If auto-flattening resolves the property (e.g., `CustomerName` → `source.Customer.Name`), the flattened scalar value is used — no auto-wiring. Flattened paths always produce direct assignments, not nested forge calls
5. **Search forge methods**: Look for partial forge methods on the forger class where:
   - Source parameter type matches the source property type
   - Return type exactly matches the destination property type
6. **Resolution**:
   - Exactly 1 match → auto-wire (same codegen as `[ForgeWith]`)
   - 0 matches → fall through to normal unmapped behavior (FM0006)
   - Multiple matches → FM0025 warning, skip auto-wiring

> **Note**: `[ForgeProperty]` dot-path mappings (e.g., `[ForgeProperty("Order.Customer", "Customer")]`) resolve to a leaf type via path traversal. If the leaf type is not directly assignable, auto-wiring applies to the leaf type — not the intermediate path segments.

### API Surface

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ForgeMapAttribute : Attribute
{
    // ... existing properties ...

    /// <summary>
    /// Gets or sets whether the generator should auto-discover matching forge methods
    /// for nested complex properties. Default is true.
    /// </summary>
    public bool AutoWireNestedMappings { get; set; } = true;
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class ForgeMapDefaultsAttribute : Attribute
{
    // ... existing properties ...

    /// <summary>
    /// Gets or sets whether auto-discovery of nested forge methods is enabled by default.
    /// Default is true.
    /// </summary>
    public bool AutoWireNestedMappings { get; set; } = true;
}
```

### Configuration Hierarchy

Following the existing three-tier precedent:

| Level | Configuration | Example |
|-------|--------------|---------|
| Assembly | `[assembly: ForgeMapDefaults(AutoWireNestedMappings = false)]` | Disable project-wide |
| Forger | `[ForgeMap(AutoWireNestedMappings = false)]` | Disable per-forger |
| Property | `[ForgeWith]`, `[ForgeFrom]`, `[Ignore]` | Override individual properties |

### Generated Code

Auto-wired nested properties produce identical codegen to explicit `[ForgeWith]`:

```csharp
// Object initializer:
Address = source.Address is { } __forgeWith_Address ? Forge(__forgeWith_Address) : null!,

// Constructor parameter:
Address: source.Address is { } __forgeWith_Address ? Forge(__forgeWith_Address) : null!
```

### Diagnostics

| Code | Severity | Change | Description |
|------|----------|--------|-------------|
| FM0011 | Info | Enhanced | Now also reports auto-wired nested mappings with distinct message: "Property '{0}' auto-wired via forge method '{1}'" (was "The property '{0}' was mapped by name convention"). Disabled by default; enable via `.editorconfig`: `dotnet_diagnostic.FM0011.severity = suggestion` |
| **FM0025** | Warning | **New** | Ambiguous auto-wire: multiple forge methods match for property `X`. Use `[ForgeWith]` to resolve |
| FM0006 | Warning | Unchanged | Still raised for unmapped properties when no forge method is found |

### Breaking Change Note

Properties that were previously unmapped (producing FM0006) are now auto-wired when a matching forge method exists. To restore v1.2 behavior, set `AutoWireNestedMappings = false`. Enable FM0011 (info, disabled by default) via `.editorconfig` (`dotnet_diagnostic.FM0011.severity = suggestion`) for full visibility into which properties are auto-wired vs. convention-mapped.

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.3 |
|--------|-----------|---------|---------------|
| Default | Always implicit | Always implicit | Default true (opt-out) |
| Mechanism | Runtime registry lookup | Private helper generation | Existing user methods |
| Error detection | Runtime / `AssertConfigurationIsValid()` | Compile-time | Compile-time |
| Explicit override | `.ForMember()` | User-defined method | `[ForgeWith]` / `[ForgeFrom]` |
| Ambiguity | Last-wins (silent) | Not applicable (auto-generated) | FM0025 warning |

---

## Feature 3: Inline Collection Mapping

### Problem

Users must declare explicit collection forge methods for every collection type in their mapping graph, even when the element forge method exists:

```csharp
// v1.2: Required even though Forge(ProductEntity) → ProductDto exists
public partial List<ProductDto> Forge(List<ProductEntity> source);
public partial List<AddressDto> Forge(List<AddressEntity> source);
// v1.2: HashSet<T> not supported at all — requires hand-written method
```

### Design

When `AutoWireNestedMappings = true` (default) and a destination property is a collection type, the generator:

1. Detects the property as a collection type
2. Unwraps element types (`List<TSource>` → `TSource`, `List<TDest>` → `TDest`)
3. Searches for a matching element forge method (any partial forge method with signature `TSource → TDest`)
4. Generates **inline iteration code** in the property assignment

Explicitly declared collection forge methods always take precedence.

#### Interaction with `GenerateCollectionMappings`

The existing `GenerateCollectionMappings` flag (default `true`) controls whether **explicitly declared** collection forge methods get auto-generated bodies. The new inline collection feature is separate — it generates collection iteration **inline in property assignments** rather than as standalone methods. The behavior matrix:

| `AutoWireNestedMappings` | `GenerateCollectionMappings` | Behavior |
|--------------------------|------------------------------|----------|
| `true` | `true` (default) | Inline collection iteration for auto-wired properties; explicit collection methods also generated |
| `true` | `false` | Inline collection iteration for auto-wired properties; explicit collection methods must be hand-implemented |
| `false` | `true` | No inline collection iteration; explicit collection methods generated as in v1.2 |
| `false` | `false` | No inline collection iteration; no auto-generated collection methods (v1.2 behavior) |

### Usage

```csharp
[ForgeMap]
public partial class AppForger
{
    public partial OrderDto Forge(OrderEntity source);
    public partial ProductDto Forge(ProductEntity source);
    // No collection methods needed — inline iteration generated for
    // OrderDto.Items (List<ProductDto>) ← OrderEntity.Items (List<ProductEntity>)
}
```

### Generated Code Examples

Multi-statement collection mappings (List, HashSet) are assigned via post-construction statements to avoid IIFE overhead when the destination property has a regular setter. For `init`-only properties, the generator builds the collection into a local variable before object construction and assigns it within the object initializer. Single-expression mappings (Array, IEnumerable) can remain inline in object initializers since they don't require an IIFE or statement-based assignment (note: lambdas still allocate delegates, but avoid the extra closure + IIFE overhead of multi-statement blocks).

> **Note:** The inline `List<T>` / `HashSet<T>` examples that follow assume the default configuration `NullPropertyHandling = NullForgiving` and a settable destination property. When `SkipNull`, `CoalesceToDefault`, or `ThrowException` are used instead, only the outer-collection behavior in the generated code changes, as described in the Null Handling table below. When the property is `init`-only, the post-construction pattern is replaced with a pre-construction local variable assigned in the object initializer.

**List with pre-sizing (source has `.Count`) — post-construction statements:**

```csharp
// Multi-statement → emitted after object construction to avoid IIFE closure overhead
if (source.Items is { } __autoItems)
{
    var __list = new global::System.Collections.Generic.List<ProductDto>(__autoItems.Count);
    foreach (var __item in __autoItems)
        __list.Add(Forge(__item));
    __result.Items = __list;
}
else
{
    __result.Items = null!;
}
```

> Implementation note: The pre-sized `foreach` approach is preferred over LINQ `.Select().ToList()` for performance consistency with existing explicit collection method codegen. The exact codegen pattern is an implementation detail.

**Array — inline expression (no IIFE needed):**

```csharp
// Single expression → safe in object initializer (no IIFE needed)
Tags = source.Tags is { } __autoTags
    ? global::System.Array.ConvertAll(__autoTags, item => Forge(item))
    : null!,
```

**IEnumerable (lazy) — inline expression (no IIFE needed):**

```csharp
// Single expression → safe in object initializer (no IIFE needed)
Items = source.Items is { } __autoItems
    ? __autoItems.Select(item => Forge(item))
    : null!,
```

**HashSet (`foreach` + `Add`) — post-construction statements:**

```csharp
if (source.Labels is { } __autoLabels)
{
    // Parameterless ctor for broad TFM support (netstandard2.0, .NET Framework).
    // The generator may use HashSet<T>(int capacity) or EnsureCapacity when those
    // APIs are available in the current compilation's target framework.
    var __set = new global::System.Collections.Generic.HashSet<LabelDto>();
    foreach (var item in __autoLabels)
        __set.Add(Forge(item));
    __result.Labels = __set;
}
else
{
    __result.Labels = null!;
}
```

### Supported Collection Conversions

| Source Type | Destination Type | Strategy |
|-------------|-----------------|----------|
| `List<T>` | `List<U>` | Pre-sized `new List<U>(Count)` + `foreach` |
| `IList<T>` | `List<U>` | Pre-sized + `foreach` |
| `ICollection<T>` | `List<U>` | Pre-sized + `foreach` |
| `IEnumerable<T>` | `List<U>` | `foreach` + `Add` (no pre-sizing; LINQ `.Select().ToList()` as fallback) |
| `T[]` | `U[]` | `Array.ConvertAll` or indexed loop |
| `IEnumerable<T>` | `IEnumerable<U>` | `.Select()` (lazy) |
| `IReadOnlyList<T>` | `List<U>` | Pre-sized + `foreach` |
| `IReadOnlyCollection<T>` | `List<U>` | Pre-sized + `foreach` |
| `HashSet<T>` | `HashSet<U>` | **New in v1.3.** `new HashSet<U>()` + `foreach` + `Add` |

### Null Handling

Inline collection mapping follows existing `NullPropertyHandling` semantics for the **outer collection reference** (consistent with v1.2 — see [SPEC-v1.2-null-property-handling.md](SPEC-v1.2-null-property-handling.md#collection-properties)), and the element forge method's `NullHandling` for individual elements:

| Source Value | Generated Behavior |
|--------------|--------------------|
| Collection is `null` | Governed by `NullPropertyHandling`: `NullForgiving` → `null!`, `CoalesceToDefault` → empty collection, `SkipNull` → skip assignment, `ThrowException` → throw. Default (`NullForgiving`) matches `[ForgeWith]`-style guarding |
| Collection is empty | Returns an empty collection of the destination type |
| Elements are `null` | Each element (including `null`) is passed to the element forge method; result depends on that method's `NullHandling` (`ReturnNull` → null element preserved; `ThrowException` → throws) |

### `[ReverseForge]` Interaction

Auto-wired nested and collection properties participate in `[ReverseForge]` generation:

| Scenario | Behavior |
|----------|----------|
| Auto-wired nested property + `[ReverseForge]` | Reverse auto-wiring applies if the reverse forge method exists (same discovery algorithm in reverse direction) |
| Auto-wired collection property + `[ReverseForge]` | Reverse collection inline generated if reverse element forge method exists |
| Reverse method not found for auto-wired property | New diagnostic FM0026 (Warning): auto-wired property '{0}' has no reverse forge method (analogous to FM0015 for explicit `[ForgeWith]`) |
| Explicit `[ForgeWith]` on forward + auto-wire on reverse | Each direction resolved independently |

### Precedence Rules

1. **Explicit collection method** declared on forger → used as-is (no inline generation)
2. **`[ForgeWith]`** on the property referencing a collection method → used as-is
3. **Auto-wired inline** → generated when `AutoWireNestedMappings = true` and element method found
4. **No match** → FM0006 (unmapped property)

### Diagnostics

No additional diagnostics introduced by this feature — it reuses FM0006 when an element method isn't found, FM0025 for ambiguous element matches, and FM0026 for missing reverse forge methods on auto-wired properties (all of which are new in v1.3).

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.3 |
|--------|-----------|---------|---------------|
| Default | Always implicit | Always implicit | Default true (tied to `AutoWireNestedMappings`) |
| Codegen | Runtime iteration | Private helper with pre-sizing | Inline with pre-sizing |
| Pre-sizing | No | Yes | Yes |
| `HashSet<T>` | Yes | Yes | Yes |
| Span/Memory | No | Yes | No (future) |
| Explicit override | Custom type converter | User-defined method | Explicit collection method on forger |

---

## New Diagnostics Summary

| Code | Severity | Category | Description |
|------|----------|----------|-------------|
| FM0024 | Warning | `ForgeMap` | `[ForgeAllDerived]` on abstract/interface destination type '{0}': unmatched source subtypes will throw `NotSupportedException` at runtime |
| FM0025 | Warning | `ForgeMap` | Multiple forge methods match for auto-wiring property '{0}' on '{1}'. Use explicit `[ForgeWith]` to resolve the ambiguity |
| FM0026 | Warning | `ForgeMap` | Auto-wired property '{0}' has no reverse forge method; reverse mapping may be incomplete |

---

## API Changes Summary

### New Properties on Existing Attributes

| Attribute | Property | Type | Default | Description |
|-----------|----------|------|---------|-------------|
| `ForgeMapAttribute` | `AutoWireNestedMappings` | `bool` | `true` | Auto-discover forge methods for nested properties |
| `ForgeMapDefaultsAttribute` | `AutoWireNestedMappings` | `bool` | `true` | Assembly-level default for auto-discovery |

### Internal (`ForgerConfig`)

| Property | Type | Default | Description |
|----------|------|---------|-------------|
| `AutoWireNestedMappings` | `bool` | `true` | Resolved config for auto-wiring |

---

## Migration Guide

### From v1.2 to v1.3

**No action required for most users.** The default behavior changes are additive:

1. **Abstract `[ForgeAllDerived]` now works** — remove any manual dispatch methods
2. **Nested `[ForgeWith]` may be removable** — if a matching forge method exists, the attribute is redundant. Removing it is optional but reduces clutter
3. **Collection method declarations may be removable** — if auto-wiring handles them inline

**If you experience unexpected behavior:**

```csharp
// Disable auto-wiring to restore v1.2 behavior
[ForgeMap(AutoWireNestedMappings = false)]
public partial class AppForger { ... }

// Or at assembly level
[assembly: ForgeMapDefaults(AutoWireNestedMappings = false)]
```

### Identifying auto-wired properties

FM0011 is an info-level diagnostic (disabled by default). Enable it to see which properties are auto-wired:

```ini
# In .editorconfig (solution or project root)
[*.cs]
dotnet_diagnostic.FM0011.severity = suggestion
```

FM0011 messages distinguish between convention-mapped and auto-wired properties in their text. Note: `#pragma warning restore` and `<WarningsAsErrors>` do not enable disabled-by-default diagnostics — `.editorconfig` is the correct mechanism.

---

## Limitations

| Limitation | Reason | Workaround |
|-----------|--------|------------|
| Auto-wiring only within same forger class | Cross-class method discovery would require global analysis | Use explicit `[ForgeWith]` referencing a method on the same forger that delegates to the other forger |
| No auto-wiring for `[ForgeFrom]` resolvers | Resolvers have arbitrary signatures, not discoverable | Continue using explicit `[ForgeFrom]` |
| `[ReverseForge]` + auto-wired properties emit FM0026 if reverse method missing | Consistent with FM0015 for explicit `[ForgeWith]` | Add the reverse forge method, or add `[Ignore(nameof(Prop))]` on the reverse forge method's `[ForgeWith]` attributes |
| No Span/Memory collection support | Complex codegen for stack-allocated types | Use explicit collection methods |
| Abstract dispatch requires concrete derived methods | Source generator cannot discover types at runtime | Ensure all subtypes have forge methods; FM0024 warns |

---

*Specification Version: 1.3*
*Status: Proposal — the features, diagnostics (including `AutoWireNestedMappings`, FM0024–FM0026), and behavioral changes described here are not yet implemented in the current codebase.*
*License: MIT*
