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

    // Polymorphic dispatch — most-derived types checked first
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
| FM0022 | Warning | Unchanged | Still raised if no derived methods found |
| **FM0024** | Warning | **New** | `[ForgeAllDerived]` on abstract/interface destination — unmatched source subtypes will throw `NotSupportedException` at runtime |

### Behavioral Contract

| Scenario | Behavior |
|----------|----------|
| Abstract class destination | Dispatch-only body, `throw` fallback |
| Interface destination | Dispatch-only body, `throw` fallback |
| Concrete destination (unchanged) | Is-cascade + base-type mapping fallback |
| No derived methods found | FM0022 warning, base mapping fallback (concrete) or `throw` (abstract) |
| `[ForgeAllDerived]` + `[ConvertWith]` | FM0023 error (unchanged) |
| `[BeforeForge]`/`[AfterForge]` on abstract dispatch | Skipped — hooks require an instance |
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
4. **Search forge methods**: Look for partial forge methods on the forger class where:
   - Source parameter type matches the source property type
   - Return type is assignable to the destination property type
5. **Resolution**:
   - Exactly 1 match → auto-wire (same codegen as `[ForgeWith]`)
   - 0 matches → fall through to normal unmapped behavior (FM0006)
   - Multiple matches → FM0025 warning, skip auto-wiring

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
| FM0011 | Info | Enhanced | Now also reports auto-wired nested mappings (was convention-only) |
| **FM0025** | Warning | **New** | Ambiguous auto-wire: multiple forge methods match for property `X`. Use `[ForgeWith]` to resolve |
| FM0006 | Warning | Unchanged | Still raised for unmapped properties when no forge method is found |

### Breaking Change Note

Properties that were previously unmapped (producing FM0006) are now auto-wired when a matching forge method exists. To restore v1.2 behavior, set `AutoWireNestedMappings = false`. The FM0011 info diagnostic reports all auto-wired properties for visibility.

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
public partial HashSet<TagDto> Forge(HashSet<TagEntity> source);
```

### Design

When `AutoWireNestedMappings = true` (default) and a destination property is a collection type, the generator:

1. Detects the property as a collection type
2. Unwraps element types (`List<TSource>` → `TSource`, `List<TDest>` → `TDest`)
3. Searches for a matching element forge method (`Forge(TSource) → TDest`)
4. Generates **inline iteration code** in the property assignment

Explicitly declared collection forge methods always take precedence.

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

**List with pre-sizing (source has `.Count`):**

```csharp
// Dest property: List<ProductDto> Items
// Source property: List<ProductEntity> Items
Items = source.Items is { } __autoItems
    ? new global::System.Collections.Generic.List<ProductDto>(
        __autoItems.Select(item => Forge(item)).ToList())
    : null!,
```

Or with optimized pre-sized loop:

```csharp
Items = source.Items is { } __autoItems
    ? (() => {
          var __list = new global::System.Collections.Generic.List<ProductDto>(__autoItems.Count);
          foreach (var __item in __autoItems)
              __list.Add(Forge(__item));
          return __list;
      })()
    : null!,
```

> Implementation note: The exact codegen pattern (LINQ `.Select().ToList()` vs. pre-sized `foreach`) is an implementation detail. The pre-sized `foreach` approach is preferred for performance consistency with existing explicit collection method codegen.

**Array:**

```csharp
Tags = source.Tags is { } __autoTags
    ? global::System.Array.ConvertAll(__autoTags, item => Forge(item))
    : null!,
```

**IEnumerable (lazy):**

```csharp
Items = source.Items is { } __autoItems
    ? __autoItems.Select(item => Forge(item))
    : null!,
```

**HashSet:**

```csharp
Labels = source.Labels is { } __autoLabels
    ? new global::System.Collections.Generic.HashSet<LabelDto>(
        __autoLabels.Select(item => Forge(item)))
    : null!,
```

### Supported Collection Conversions

| Source Type | Destination Type | Strategy |
|-------------|-----------------|----------|
| `List<T>` | `List<U>` | Pre-sized `new List<U>(Count)` + `foreach` |
| `IList<T>` | `List<U>` | Pre-sized + `foreach` |
| `ICollection<T>` | `List<U>` | Pre-sized + `foreach` |
| `IEnumerable<T>` | `List<U>` | `.Select().ToList()` |
| `T[]` | `U[]` | `Array.ConvertAll` or indexed loop |
| `IEnumerable<T>` | `IEnumerable<U>` | `.Select()` (lazy) |
| `IReadOnlyList<T>` | `List<U>` | Pre-sized + `foreach` |
| `IReadOnlyCollection<T>` | `List<U>` | Pre-sized + `foreach` |
| `HashSet<T>` | `HashSet<U>` | `new HashSet<U>()` + `foreach` + `Add` |

### Null Handling

Inline collection mapping follows the same null patterns as existing collection methods:

| Source Value | Generated Behavior |
|--------------|--------------------|
| `null` | Returns `null!` (reference types) or `default` (value types) |
| Empty collection | Returns empty collection of destination type |
| Elements are `null` | Preserved — calls `Forge(null)` which returns `null!` per `NullHandling` |

### Precedence Rules

1. **Explicit collection method** declared on forger → used as-is (no inline generation)
2. **`[ForgeWith]`** on the property referencing a collection method → used as-is
3. **Auto-wired inline** → generated when `AutoWireNestedMappings = true` and element method found
4. **No match** → FM0006 (unmapped property)

### Diagnostics

No new diagnostics — reuses FM0006 when element method isn't found, and FM0025 for ambiguous element matches.

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

Enable the FM0011 info diagnostic to see which properties are auto-wired:

```xml
<!-- In .csproj -->
<PropertyGroup>
    <ForgeMap_WarningsAsErrors>FM0011</ForgeMap_WarningsAsErrors>
</PropertyGroup>
```

---

## Limitations

| Limitation | Reason | Workaround |
|-----------|--------|------------|
| Auto-wiring only within same forger class | Cross-class method discovery would require global analysis | Use `[ForgeWith]` to reference methods on other forgers |
| No auto-wiring for `[ForgeFrom]` resolvers | Resolvers have arbitrary signatures, not discoverable | Continue using explicit `[ForgeFrom]` |
| No Span/Memory collection support | Complex codegen for stack-allocated types | Use explicit collection methods |
| Abstract dispatch requires concrete derived methods | Source generator cannot discover types at runtime | Ensure all subtypes have forge methods; FM0024 warns |

---

*Specification Version: 1.3*
*License: MIT*
