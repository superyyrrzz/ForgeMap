# ForgeMap v1.4 Specification — Advanced Mapping

## Overview

v1.4 targets the three highest-demand gaps identified through Mapperly GitHub issue analysis. These features address real pain points that Mapperly users face today — validated by community reaction counts and open issue longevity.

| Feature | Mapperly Pain Point | Evidence | Impact |
|---------|---------------------|----------|--------|
| Nested existing-target mapping | [riok/mapperly#884](https://github.com/riok/mapperly/issues/884) (13👍), [#1311](https://github.com/riok/mapperly/issues/1311) (11👍), [#665](https://github.com/riok/mapperly/issues/665) (4👍) | 32 reactions across 3 open issues, unresolved since 2023 | EF Core CRUD apps — updates break change tracking |
| Auto-flattening with `init`/`required` support | [riok/mapperly#643](https://github.com/riok/mapperly/issues/643) (6👍), [#589](https://github.com/riok/mapperly/issues/589) (4👍) | Broken in Mapperly since mid-2023 | Modern C# users blocked on `init`/`required` + flattening |
| Dictionary-to-typed-object mapping | [riok/mapperly#1309](https://github.com/riok/mapperly/issues/1309) (10👍) | Top-10 most-reacted open Mapperly issue | Common in deserialization, config, NoSQL, dynamic scenarios |

---

## Feature 1: Nested Existing-Target Mapping

### Problem

When updating an existing entity graph from a DTO (e.g., an API PATCH/PUT handler with EF Core), ForgeMap's current `[UseExistingValue]` mutation pattern only updates top-level properties. Nested objects are **replaced** with new instances rather than **updated in place**, which breaks EF Core change tracking, orphans database records, and causes unnecessary INSERT+DELETE instead of UPDATE.

```csharp
// Current v1.3 behavior: nested Customer gets REPLACED
public partial void ForgeInto(OrderUpdateDto source, [UseExistingValue] Order target);

// Generated code (v1.3):
target.Status = source.Status;
target.Customer = Forge(source.Customer);  // ← NEW object, breaks EF tracking!
target.ShippingAddress = Forge(source.ShippingAddress);  // ← NEW object
```

This is the #1 most-reacted unresolved feature area in Mapperly (32 combined reactions across 3 open issues).

### Design

A new `ExistingTarget` property on `[ForgeProperty]` tells the generator to update the nested object in place rather than replacing it. The generator emits property-by-property assignments on the existing nested object instead of constructing a new one.

### API Surface

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ForgePropertyAttribute : Attribute
{
    // ... existing properties ...

    /// <summary>
    /// When true, the destination property's existing value is updated in place
    /// rather than replaced with a new instance. Requires the destination property
    /// to be a readable reference-type property. Runtime null values on the destination
    /// are handled according to the configured NullPropertyHandling (skip/coalesce/throw).
    /// Used with [UseExistingValue] mutation methods to preserve object identity
    /// (e.g., EF Core change tracking). Default is false.
    /// </summary>
    public bool ExistingTarget { get; set; }
}
```

### Usage

```csharp
[ForgeMap]
public partial class AppForger
{
    // Standard mapping — creates new objects
    public partial OrderDto Forge(Order source);

    // Mutation mapping — updates existing Order in place
    [ForgeProperty("Customer", "Customer", ExistingTarget = true)]
    [ForgeProperty("ShippingAddress", "ShippingAddress", ExistingTarget = true)]
    public partial void ForgeInto(OrderUpdateDto source, [UseExistingValue] Order target);

    // Element-level mappings used for nested updates
    public partial void ForgeInto(CustomerUpdateDto source, [UseExistingValue] Customer target);
    public partial void ForgeInto(AddressUpdateDto source, [UseExistingValue] Address target);
}
```

### Generated Code

```csharp
public partial void ForgeInto(OrderUpdateDto source, [UseExistingValue] Order target)
{
    if (source == null) return; // NullHandling = ReturnNull (default) for void ForgeInto
    if (target == null) throw new global::System.ArgumentNullException(nameof(target));

    target.Status = source.Status;

    // Nested existing-target: updates in place, preserving object identity
    if (source.Customer is { } __src_Customer && target.Customer is { } __tgt_Customer)
    {
        ForgeInto(__src_Customer, __tgt_Customer);
    }

    if (source.ShippingAddress is { } __src_ShippingAddress && target.ShippingAddress is { } __tgt_ShippingAddress)
    {
        ForgeInto(__src_ShippingAddress, __tgt_ShippingAddress);
    }
}
```

### Resolution Algorithm

For each property marked with `ExistingTarget = true`:

1. **Validate context**: Must be on a `[UseExistingValue]` mutation method (void return). Emit **FM0028** if used on a non-mutation method
2. **Validate destination property**: Must have a getter (to read the existing value). Emit **FM0029** if property is write-only
3. **Find matching ForgeInto method**: Search for a void partial method on the forger with:
   - A source parameter matching the source property type
   - A `[UseExistingValue]` parameter matching the destination property type
4. **Auto-wiring**: When `AutoWireNestedMappings = true` (default), the matching `ForgeInto` method is discovered automatically — no explicit attribute needed if a matching mutation method exists
5. **Null handling**: When the source property is null, the nested target is left unchanged (skip). When the target property is null, behavior depends on `NullPropertyHandling`:
   - `NullForgiving` / `SkipNull`: Skip the update (cannot update a null target in place)
   - `CoalesceToDefault`: Create a new instance and assign it (fallback to replacement)
   - `ThrowException`: Throw `InvalidOperationException("Cannot update null target property 'X' in place")`

### Collection Update Strategies

For collection properties marked with `ExistingTarget = true`, a new `CollectionUpdateStrategy` enum controls merge behavior:

```csharp
/// <summary>
/// Specifies how a collection property is updated when ExistingTarget = true.
/// </summary>
public enum CollectionUpdateStrategy
{
    /// <summary>Replace the entire collection (default — same as non-ExistingTarget behavior).</summary>
    Replace,

    /// <summary>Add new items from source to existing collection. Existing items unchanged.</summary>
    Add,

    /// <summary>
    /// Match items by key, update existing, add new, remove missing.
    /// Requires KeyProperty to be set.
    /// </summary>
    Sync
}
```

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ForgePropertyAttribute : Attribute
{
    // ... existing properties ...

    /// <summary>
    /// Specifies how a collection property is updated when ExistingTarget is true.
    /// Ignored when ExistingTarget is false or the property is not a collection type.
    /// Default is Replace.
    /// </summary>
    public CollectionUpdateStrategy CollectionUpdate { get; set; }

    /// <summary>
    /// The property name used as a matching key for CollectionUpdateStrategy.Sync.
    /// Both source and destination element types must have a property with this name.
    /// Required when CollectionUpdate = Sync. Emit FM0031 if missing.
    /// </summary>
    public string? KeyProperty { get; set; }
}
```

#### Usage

```csharp
[ForgeProperty("Items", "Items", ExistingTarget = true,
    CollectionUpdate = CollectionUpdateStrategy.Sync, KeyProperty = "Id")]
public partial void ForgeInto(OrderUpdateDto source, [UseExistingValue] Order target);
```

#### Generated Code (Sync strategy — requires `List<T>` destination)

```csharp
// Collection sync: match by Id, update existing, add new, remove missing
// Note: RemoveAll is a List<T>-specific API; Sync strategy requires List<T> destinations
if (source.Items is { } __src_Items && target.Items is { } __tgt_Items)
{
    var __existing = new global::System.Collections.Generic.Dictionary<int, OrderItem>();
    foreach (var __item in __tgt_Items)
        __existing[__item.Id] = __item;

    var __matched = new global::System.Collections.Generic.HashSet<int>();
    foreach (var __srcItem in __src_Items)
    {
        if (__existing.TryGetValue(__srcItem.Id, out var __tgtItem))
        {
            ForgeInto(__srcItem, __tgtItem);
            __matched.Add(__srcItem.Id);
        }
        else
        {
            __tgt_Items.Add(Forge(__srcItem));
        }
    }

    __tgt_Items.RemoveAll(__item => !__matched.Contains(__item.Id));
}
```

### Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| **FM0028** | Error | `ExistingTarget = true` is only valid on `[UseExistingValue]` mutation methods |
| **FM0029** | Error | Property '{0}' has no getter — cannot read existing value for in-place update |
| **FM0030** | Warning | No matching `ForgeInto` method found for nested existing-target property '{0}'. The property will be skipped |
| **FM0031** | Error | `CollectionUpdateStrategy.Sync` requires `KeyProperty` to be set on `[ForgeProperty]` for property '{0}' |
| **FM0032** | Error | `KeyProperty` '{0}' not found on element type '{1}' |

### Behavioral Contract

| Scenario | Behavior |
|----------|----------|
| `ExistingTarget = true` on mutation method | Update nested object in place |
| `ExistingTarget = true` on non-mutation method | FM0028 error |
| Source property is null | Skip update (leave target property unchanged) |
| Target property is null, `NullPropertyHandling = SkipNull` | Skip update |
| Target property is null, `NullPropertyHandling = CoalesceToDefault` | Create new instance, assign |
| Target property is null, `NullPropertyHandling = ThrowException` | Throw `InvalidOperationException` |
| No matching `ForgeInto` method (auto-wire) | FM0030 warning, property skipped |
| `CollectionUpdate = Sync` without `KeyProperty` | FM0031 error |
| `ExistingTarget = true` + `[ReverseForge]` | Reverse method inherits `ExistingTarget` semantics if a matching reverse `ForgeInto` exists |
| `ExistingTarget = true` on scalar property | Ignored — scalar properties are always assigned directly |

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.4 |
|--------|-----------|---------|---------------|
| Nested update | `Map(src, dest)` updates nested (runtime) | ❌ Not supported (#884, #1311) | ✅ `ExistingTarget = true` |
| Collection sync | Custom `IValueResolver` | ❌ Not supported (#665) | ✅ `CollectionUpdateStrategy.Sync` with `KeyProperty` |
| Change tracking safety | Implicit (runtime) | N/A | ✅ Compile-time guaranteed via ForgeInto chains |
| Null target handling | Runtime exception | N/A | ✅ Configurable via `NullPropertyHandling` |

---

## Feature 2: Auto-Flattening with `init`/`required` Support

### Problem

Users frequently map nested source objects to flattened DTOs (e.g., `Order.Customer.Name` → `OrderDto.CustomerName`). This is one of the most common mapping patterns in enterprise applications:

```csharp
class Order {
    public Customer Customer { get; set; }
    public Address ShippingAddress { get; set; }
}

class OrderDto {
    public string CustomerName { get; set; }
    public string CustomerEmail { get; set; }
    public string ShippingAddressCity { get; set; }
    public required string ShippingAddressZipCode { get; init; }  // modern C#
}
```

Currently, ForgeMap requires explicit `[ForgeProperty]` with dot-path notation for every flattened property:

```csharp
// v1.3: manual [ForgeProperty] for every flattened member
[ForgeProperty("Customer.Name", "CustomerName")]
[ForgeProperty("Customer.Email", "CustomerEmail")]
[ForgeProperty("ShippingAddress.City", "ShippingAddressCity")]
[ForgeProperty("ShippingAddress.ZipCode", "ShippingAddressZipCode")]
public partial OrderDto Forge(Order source);
```

Mapperly auto-flattens by PascalCase convention but has been **broken for `init`/`required` properties since mid-2023** ([#643](https://github.com/riok/mapperly/issues/643), [#589](https://github.com/riok/mapperly/issues/589)).

### Design

The generator automatically resolves unmatched destination properties by splitting the property name into PascalCase segments and walking the source object graph. When the destination property has an `init` or `required` modifier, the assignment is routed to the object initializer or constructor instead of a post-construction setter.

### Configuration

Auto-flattening is controlled by a new `AutoFlatten` property on `[ForgeMap]` and `[ForgeMapDefaults]`:

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ForgeMapAttribute : Attribute
{
    // ... existing properties ...

    /// <summary>
    /// When true, the generator automatically resolves unmatched destination properties
    /// by splitting PascalCase names and walking the source object graph.
    /// e.g., "CustomerName" resolves to source.Customer.Name.
    /// Default is true.
    /// </summary>
    public bool AutoFlatten { get; set; } = true;
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class ForgeMapDefaultsAttribute : Attribute
{
    // ... existing properties ...

    /// <summary>
    /// Assembly-level default for auto-flattening. Default is true.
    /// </summary>
    public bool AutoFlatten { get; set; } = true;
}
```

### Resolution Algorithm

For each unmatched destination property (no direct source match, no `[ForgeProperty]`, no `[ForgeFrom]`, no `[Ignore]`):

1. **Split** the destination property name into PascalCase segments:
   - `CustomerName` → `["Customer", "Name"]`
   - `ShippingAddressZipCode` → `["Shipping", "Address", "Zip", "Code"]`

2. **Greedy match** against the source type's property graph, trying the longest prefix first:
   - Try `source.ShippingAddressZipCode` (direct — already handled before flattening)
   - Try `source.ShippingAddress.ZipCode` (2 segments + 2 segments)
   - Try `source.ShippingAddress.Zip.Code` (2 segments + 1 + 1)
   - Try `source.Shipping.AddressZipCode` (1 segment + 3 segments)
   - Try `source.Shipping.Address.ZipCode` (1 + 1 + 2)
   - ... etc.
   - First match where all segments resolve to accessible properties wins

3. **Case sensitivity** follows the forger's `PropertyMatching` setting:
   - `ByName` (default): exact PascalCase match
   - `ByNameCaseInsensitive`: case-insensitive segment matching

4. **Assignability check**: The leaf property's type must be assignable to the destination property type (same rules as direct property mapping, including `NullPropertyHandling`)

5. **Precedence** (highest to lowest):
   - Explicit `[ForgeProperty]` / `[ForgeFrom]` / `[Ignore]` / `[ForgeWith]`
   - Direct name match (existing behavior)
   - Auto-flattened match (new)
   - Auto-wired nested forge method (existing v1.3 behavior)
   - Unmapped → FM0006

### Usage

```csharp
[ForgeMap]  // AutoFlatten defaults to true
public partial class AppForger
{
    // Auto-flattening resolves all properties automatically:
    //   CustomerName      ← source.Customer.Name
    //   CustomerEmail     ← source.Customer.Email
    //   ShippingAddressCity    ← source.ShippingAddress.City
    //   ShippingAddressZipCode ← source.ShippingAddress.ZipCode
    public partial OrderDto Forge(Order source);
}

// Opt out per-forger:
[ForgeMap(AutoFlatten = false)]
public partial class ExplicitForger { ... }
```

### Generated Code

**Regular setter properties:**

```csharp
public partial OrderDto Forge(Order source)
{
    if (source == null) return null!;

    var __result = new OrderDto
    {
        // init/required properties assigned here
        ShippingAddressZipCode = source.ShippingAddress?.ZipCode!,
    };

    // Regular setters assigned post-construction
    __result.CustomerName = source.Customer?.Name!;
    __result.CustomerEmail = source.Customer?.Email!;
    __result.ShippingAddressCity = source.ShippingAddress?.City!;

    return __result;
}
```

**Null-safe path traversal**: Intermediate path segments use null-conditional access (`?.`) to avoid `NullReferenceException`. The leaf value's null handling follows the configured `NullPropertyHandling`:

| `NullPropertyHandling` | Intermediate null | Leaf null |
|-------------------------|-------------------|-----------|
| `NullForgiving` | Path evaluates to `null`, assigned with `!` | `null!` |
| `SkipNull` | Skip assignment for regular setters; for `init`/`required` members behaves as `NullForgiving` | Skip assignment for regular setters; for `init`/`required` members behaves as `NullForgiving` |
| `CoalesceToDefault` | Coalesce to type default | Coalesce to type default |
| `ThrowException` | Throw | Throw |

**`init`/`required` members and `SkipNull`**: For destination members that are `init`-only or marked `required`, ForgeMap cannot omit the assignment in an object initializer without breaking required-member initialization or preventing assignment to `init`-only properties. When such members are mapped with `NullPropertyHandling.SkipNull`, ForgeMap emits code equivalent to `NullForgiving` (assigning `null!` in the initializer) and may additionally emit a diagnostic to highlight the configuration mismatch.

### Unflattening (Reverse Direction)

When `[ReverseForge]` is used on a method with auto-flattened properties, the reverse direction performs **unflattening** — constructing intermediate objects:

```csharp
// Forward: auto-flattens Order → OrderDto
[ReverseForge]
public partial OrderDto Forge(Order source);

// Reverse generated (unflattening) — emitted by the generator:
public Order Forge(OrderDto source)
{
    if (source == null) return null!;

    var __result = new Order();

    __result.Customer = new Customer
    {
        Name = source.CustomerName,
        Email = source.CustomerEmail,
    };

    __result.ShippingAddress = new Address
    {
        City = source.ShippingAddressCity,
        ZipCode = source.ShippingAddressZipCode,
    };

    return __result;
}
```

**Unflattening constraints:**
- Intermediate types must have accessible parameterless constructors (or constructor parameters matching the properties). Emit **FM0034** if not constructible
- When multiple destination properties unflatten into the same intermediate object, they are grouped into a single object initializer
- `[ReverseForge]` unflattening is best-effort — emit **FM0035** (warning) for any property that cannot be unflattened, with a suggestion to add explicit `[ForgeProperty]` on the reverse method

### Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| **FM0033** | Info | Property '{0}' auto-flattened from '{1}' (disabled by default; enable via `.editorconfig`) |
| **FM0034** | Error | Unflattening requires type '{0}' to have an accessible constructor, but none was found |
| **FM0035** | Warning | Auto-flattened property '{0}' cannot be unflattened for `[ReverseForge]`: {reason} |

### Interaction with Existing Features

| Feature | Interaction |
|---------|-------------|
| `[ForgeProperty]` with dot-path | Explicit dot-path takes precedence over auto-flattening. Both produce the same codegen — auto-flattening eliminates the need for the attribute |
| `AutoWireNestedMappings` | Auto-flattening runs before auto-wiring. A flattened scalar assignment prevents auto-wiring from treating the property as a nested complex type |
| `PropertyMatching = ByNameCaseInsensitive` | Segment matching is case-insensitive |
| `[IncludeBaseForge]` | Auto-flattened properties from the base method are inherited. Derived methods can override with explicit `[ForgeProperty]` |
| `[Ignore]` | Ignored properties are excluded from auto-flattening |

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.4 |
|--------|-----------|---------|---------------|
| Auto-flattening | ✅ Runtime convention | ✅ Compile-time PascalCase | ✅ Compile-time PascalCase |
| `init`/`required` properties | ✅ Runtime (since v13) | ❌ Broken (#643) | ✅ Object initializer routing |
| Unflattening | `.ReverseMap()` with runtime | Manual `MapProperty` paths | ✅ Auto via `[ReverseForge]` |
| Opt-out | `.DisableCtorValidation()` | No opt-out | `AutoFlatten = false` |
| Case sensitivity control | Always case-insensitive | Case-insensitive | Follows `PropertyMatching` |
| Null-safe paths | Runtime null checks | Compile-time `?.` chains | Compile-time `?.` chains |
| Diagnostic visibility | None (runtime only) | Compile-time | FM0033 info (opt-in) |

---

## Feature 3: Dictionary-to-Typed-Object Mapping

### Problem

Many real-world scenarios produce `Dictionary<string, object?>` (or `IDictionary<string, object?>`, `IReadOnlyDictionary<string, object?>`):

- JSON deserialization fallbacks / dynamic JSON
- Configuration providers (`IConfiguration.Get<T>()` pattern)
- NoSQL document stores (Cosmos DB, MongoDB raw documents)
- Dapper dynamic queries
- Form data / query string binding
- CSV/Excel row parsing

Users want to map these to strongly-typed objects with compile-time safety and zero reflection. This is the [#10 most-reacted open issue in Mapperly](https://github.com/riok/mapperly/issues/1309) (10👍).

### Design

A new `[ForgeDictionary]` attribute marks a forge method as a dictionary-to-object mapping. The generator enumerates the destination type's properties and generates `TryGetValue` lookups with type-safe casts.

### API Surface

```csharp
/// <summary>
/// Marks a forge method as a dictionary-to-typed-object mapping.
/// The source parameter must be Dictionary&lt;string, object?&gt;,
/// IDictionary&lt;string, object?&gt;, or IReadOnlyDictionary&lt;string, object?&gt;.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ForgeDictionaryAttribute : Attribute
{
    /// <summary>
    /// Specifies how dictionary keys are matched to destination property names.
    /// Default is <see cref="PropertyMatching.ByName"/> (case-sensitive).
    /// Overrides the forger-level PropertyMatching for this method only.
    /// </summary>
    public PropertyMatching KeyMatching { get; set; } = PropertyMatching.ByName;

    /// <summary>
    /// Specifies the behavior when a dictionary key is missing for a destination property,
    /// or when the key exists but its value is unusable (wrong type or not convertible).
    /// Default is Skip (leave the property at its default value).
    /// </summary>
    public MissingKeyBehavior MissingKeyBehavior { get; set; } = MissingKeyBehavior.Skip;
}

/// <summary>
/// Specifies behavior when a dictionary key is missing or when the value is unusable
/// (wrong type or not convertible to the destination property type).
/// </summary>
public enum MissingKeyBehavior
{
    /// <summary>Leave the destination property at its default value.</summary>
    Skip,

    /// <summary>Throw when the key is missing (KeyNotFoundException) or the value
    /// is unusable (InvalidCastException).</summary>
    Throw
}
```

### Usage

```csharp
[ForgeMap]
public partial class AppForger
{
    // Basic dictionary-to-object mapping
    [ForgeDictionary]
    public partial UserDto Forge(Dictionary<string, object?> source);

    // Case-insensitive key matching
    [ForgeDictionary(KeyMatching = PropertyMatching.ByNameCaseInsensitive)]
    public partial ConfigSettings Forge(IDictionary<string, object?> source);

    // Strict mode: throw on missing keys
    [ForgeDictionary(MissingKeyBehavior = MissingKeyBehavior.Throw)]
    public partial StrictDto ForgeStrict(Dictionary<string, object?> source);
}
```

### Generated Code

**Basic case-sensitive mapping:**

```csharp
public partial UserDto Forge(Dictionary<string, object?> source)
{
    if (source == null) return null!;

    var __result = new UserDto();

    if (source.TryGetValue("Name", out var __v_Name) && __v_Name is string __cast_Name)
        __result.Name = __cast_Name;

    if (source.TryGetValue("Age", out var __v_Age) && __v_Age is int __cast_Age)
        __result.Age = __cast_Age;

    if (source.TryGetValue("Email", out var __v_Email) && __v_Email is string __cast_Email)
        __result.Email = __cast_Email;

    if (source.TryGetValue("IsActive", out var __v_IsActive) && __v_IsActive is bool __cast_IsActive)
        __result.IsActive = __cast_IsActive;

    if (source.TryGetValue("Score", out var __v_Score))
    {
        if (__v_Score is null)
        {
            // NullPropertyHandling governs behavior:
            // NullForgiving (default): __result.Score = default!;
            // CoalesceToDefault: __result.Score = 0.0;
            // SkipNull: skip assignment
            // ThrowException: throw
            __result.Score = default!;
        }
        else
        {
            __result.Score = global::System.Convert.ToDouble(__v_Score);
        }
    }

    return __result;
}
```

**Case-insensitive key matching:**

When `KeyMatching = PropertyMatching.ByNameCaseInsensitive`, the generator uses ordinal-ignore-case comparisons to match ForgeMap's existing `ByNameCaseInsensitive` semantics:

```csharp
public partial ConfigSettings Forge(IDictionary<string, object?> source)
{
    if (source == null) return null!;

    var __result = new ConfigSettings();

    foreach (var __kvp in source)
    {
        var __key = __kvp.Key;

        if (string.Equals(__key, "HostName", global::System.StringComparison.OrdinalIgnoreCase))
        {
            if (__kvp.Value is string __cast_HostName)
                __result.HostName = __cast_HostName;
        }
        else if (string.Equals(__key, "Port", global::System.StringComparison.OrdinalIgnoreCase))
        {
            if (__kvp.Value is int __cast_Port)
                __result.Port = __cast_Port;
        }
        else if (string.Equals(__key, "UsesSsl", global::System.StringComparison.OrdinalIgnoreCase))
        {
            if (__kvp.Value is bool __cast_UsesSsl)
                __result.UsesSsl = __cast_UsesSsl;
        }
    }

    return __result;
}
```

**Strict mode (throw on missing key):**

Strict mode separates missing-key, null-value, and wrong-type cases. Missing keys throw `KeyNotFoundException`. Null values follow `NullPropertyHandling`. Wrong types throw `InvalidCastException`.

```csharp
public partial StrictDto ForgeStrict(Dictionary<string, object?> source)
{
    if (source == null) return null!;

    var __result = new StrictDto();

    if (!source.TryGetValue("Name", out var __v_Name))
        throw new global::System.Collections.Generic.KeyNotFoundException(
            $"Required key 'Name' not found in source dictionary.");
    if (__v_Name is null)
        __result.Name = null!; // NullPropertyHandling (default NullForgiving)
    else if (__v_Name is string __cast_Name)
        __result.Name = __cast_Name;
    else
        throw new global::System.InvalidCastException(
            $"Value for key 'Name' is not convertible to 'System.String'.");

    if (!source.TryGetValue("Age", out var __v_Age))
        throw new global::System.Collections.Generic.KeyNotFoundException(
            $"Required key 'Age' not found in source dictionary.");
    if (__v_Age is null)
    { /* NullPropertyHandling: for non-nullable value type, keep default */ }
    else if (__v_Age is int __cast_Age)
        __result.Age = __cast_Age;
    else
        throw new global::System.InvalidCastException(
            $"Value for key 'Age' is not convertible to 'System.Int32'.");

    return __result;
}
```

### Type Conversion Strategy

The generator applies the following conversion hierarchy for each destination property type:

| Priority | Strategy | Example | Handles |
|----------|----------|---------|---------|
| 1 | Pattern match (`is T`) | `value is int x` | Exact type match, reference conversions |
| 2 | Nullable unwrap | `value is int x` for `int?` dest | `object?` → `Nullable<T>` |
| 3 | `Convert.ToXxx()` | `Convert.ToDouble(value)` | Numeric widening (int→double), string→number |
| 4 | `Enum.Parse` / cast | `value is int i ? (MyEnum)i : value is string s ? Enum.Parse<MyEnum>(s) : /* skip/throw */` | String→enum, int→enum |
| 5 | Nested `[ForgeDictionary]` | `value is IDictionary<string, object?> d ? Forge(d) : value is IReadOnlyDictionary<string, object?> rd ? Forge(rd) : /* skip/throw */` | Nested dictionary-like → nested object |
| 6 | Auto-wired forge method | `value is SourceType s ? Forge(s) : /* skip/throw */` | Complex nested types |
| 7 | `ToString()` | `value?.ToString()` | Any → string (fallback) |

The generator picks the **first applicable** strategy at compile time. If no strategy applies, the property is skipped and **FM0037** is emitted.

For numeric types specifically:

| Destination Type | Generated Code |
|------------------|----------------|
| `int` | `value is int x` (fallback: `Convert.ToInt32(value)`) |
| `long` | `value is long x` (fallback: `Convert.ToInt64(value)`) |
| `double` | `value is double x` (fallback: `Convert.ToDouble(value)`) |
| `decimal` | `value is decimal x` (fallback: `Convert.ToDecimal(value)`) |
| `float` | `value is float x` (fallback: `Convert.ToSingle(value)`) |

The `Convert.ToXxx` fallback handles JSON deserializers that parse `42` as `long` or `int` across different libraries.

### Interaction with Existing Features

| Feature | Interaction |
|---------|-------------|
| `[Ignore]` | Ignored properties are excluded from dictionary lookup |
| `[ForgeProperty]` | `[ForgeProperty("dict_key", "PropertyName")]` overrides the key used for lookup |
| `[ForgeFrom]` | Resolver receives the entire dictionary as source — works as-is |
| `NullPropertyHandling` | Applies to the value after extraction from dictionary (same semantics as regular mapping) |
| `[ReverseForge]` | Reverse generates typed-object-to-dictionary mapping (see below) |
| `init`/`required` properties | Routed to object initializer (same as auto-flattening) |
| Constructor mapping | Dictionary values matched to constructor parameters by name |

### Reverse Mapping (Object-to-Dictionary)

When `[ReverseForge]` is present on a `[ForgeDictionary]` method, the generator creates the inverse mapping:

```csharp
// Forward (user-declared signature; implementation generated from [ForgeDictionary] + [ReverseForge]):
public partial UserDto Forge(Dictionary<string, object?> source);

// Reverse (auto-generated):
public Dictionary<string, object?> Forge(UserDto source)
{
    if (source == null) return null!;

    return new global::System.Collections.Generic.Dictionary<string, object?>
    {
        ["Name"] = source.Name,
        ["Age"] = source.Age,
        ["Email"] = source.Email,
        ["IsActive"] = source.IsActive,
    };
}
```

### Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| **FM0036** | Error | `[ForgeDictionary]` source parameter must be `Dictionary<string, object?>`, `IDictionary<string, object?>`, or `IReadOnlyDictionary<string, object?>` |
| **FM0037** | Warning | Destination property '{0}' of type '{1}' has no applicable conversion from `object?`. The property will be skipped |
| **FM0038** | Info | Property '{0}' mapped from dictionary key '{1}' with conversion '{2}' (disabled by default) |

### Behavioral Contract

| Scenario | Behavior |
|----------|----------|
| Key exists, value is correct type | Assign via pattern match |
| Key exists, value is convertible type | Assign via `Convert.ToXxx` or cast |
| Key exists, value is wrong type (no applicable conversion) | Skip or throw per `MissingKeyBehavior` |
| Key missing | Skip or throw per `MissingKeyBehavior` |
| Key exists, value is `null` | Follows `NullPropertyHandling` |
| Source dictionary is `null` | Follows `NullHandling` (ReturnNull or ThrowException) |
| Nested dictionary value | Auto-wired to nested `[ForgeDictionary]` method or forge method |
| `init`/`required` property | Routed to object initializer |

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.4 |
|--------|-----------|---------|---------------|
| Dictionary→Object | ❌ Not supported | ❌ Not supported (#1309) | ✅ `[ForgeDictionary]` |
| Case-insensitive keys | N/A | N/A | ✅ `KeyMatching` option |
| Missing key behavior | N/A | N/A | ✅ `Skip` or `Throw` |
| Type conversion | N/A | N/A | ✅ 7-tier compile-time strategy |
| Reverse (Object→Dictionary) | N/A | N/A | ✅ Via `[ReverseForge]` |
| Zero reflection | N/A | N/A | ✅ All compile-time generated |

---

## New Diagnostics Summary

| Code | Severity | Category | Description |
|------|----------|----------|-------------|
| FM0028 | Error | `ForgeMap` | `ExistingTarget = true` is only valid on `[UseExistingValue]` mutation methods |
| FM0029 | Error | `ForgeMap` | Property '{0}' has no getter — cannot read existing value for in-place update |
| FM0030 | Warning | `ForgeMap` | No matching `ForgeInto` method found for nested existing-target property '{0}'. The property will be skipped |
| FM0031 | Error | `ForgeMap` | `CollectionUpdateStrategy.Sync` requires `KeyProperty` to be set on `[ForgeProperty]` for property '{0}' |
| FM0032 | Error | `ForgeMap` | `KeyProperty` '{0}' not found on element type '{1}' |
| FM0033 | Info | `ForgeMap` | Property '{0}' auto-flattened from '{1}' (disabled by default) |
| FM0034 | Error | `ForgeMap` | Unflattening requires type '{0}' to have an accessible constructor, but none was found |
| FM0035 | Warning | `ForgeMap` | Auto-flattened property '{0}' cannot be unflattened for `[ReverseForge]`: {reason} |
| FM0036 | Error | `ForgeMap` | `[ForgeDictionary]` source parameter must be `Dictionary<string, object?>`, `IDictionary<string, object?>`, or `IReadOnlyDictionary<string, object?>` |
| FM0037 | Warning | `ForgeMap` | Destination property '{0}' of type '{1}' has no applicable conversion from `object?`. The property will be skipped |
| FM0038 | Info | `ForgeMap` | Property '{0}' mapped from dictionary key '{1}' with conversion '{2}' (disabled by default) |

---

## API Changes Summary

### New Attributes

| Attribute | Target | Description |
|-----------|--------|-------------|
| `ForgeDictionaryAttribute` | Method | Marks a method as dictionary-to-typed-object mapping |

### New Enums

| Enum | Values | Description |
|------|--------|-------------|
| `CollectionUpdateStrategy` | `Replace`, `Add`, `Sync` | How collection properties are updated in existing-target mutation |
| `MissingKeyBehavior` | `Skip`, `Throw` | Behavior when a dictionary key is missing |

### New Properties on Existing Attributes

| Attribute | Property | Type | Default | Description |
|-----------|----------|------|---------|-------------|
| `ForgePropertyAttribute` | `ExistingTarget` | `bool` | `false` | Update nested object in place |
| `ForgePropertyAttribute` | `CollectionUpdate` | `CollectionUpdateStrategy` | `Replace` | Collection update strategy for existing-target |
| `ForgePropertyAttribute` | `KeyProperty` | `string?` | `null` | Key property for `Sync` strategy |
| `ForgeMapAttribute` | `AutoFlatten` | `bool` | `true` | Enable auto-flattening |
| `ForgeMapDefaultsAttribute` | `AutoFlatten` | `bool` | `true` | Assembly-level auto-flattening default |

---

## Migration Guide

### From v1.3 to v1.4

v1.4 introduces no required source changes and no API-surface breaks, but there is an intentional **behavior change** in default mapping semantics due to auto-flattening:

1. **Auto-flattening is on by default (behavior change with opt-out)** — destination properties that were previously unmatched (producing FM0006 by default) may now be mapped via auto-flattening, which can change runtime results compared to v1.3 in previously warning-only scenarios. To restore v1.3 behavior for a given map, disable auto-flattening:
   ```csharp
   [ForgeMap(AutoFlatten = false)]
   ```
   or set the assembly-level default via `[ForgeMapDefaults(AutoFlatten = false)]`.

2. **Explicit `[ForgeProperty]` with dot-paths still works** — auto-flattening produces identical codegen. Explicit attributes take precedence and can be gradually removed

3. **Nested existing-target requires opt-in** — `ExistingTarget = true` must be explicitly set; no behavior changes to existing mutation methods

4. **`[ForgeDictionary]` is a new attribute** — no interaction with existing code unless added

### Identifying auto-flattened properties

FM0033 is an info-level diagnostic (disabled by default). Enable it to see which properties are auto-flattened:

```ini
# In .editorconfig
[*.cs]
dotnet_diagnostic.FM0033.severity = suggestion
```

---

## Limitations

| Limitation | Reason | Workaround |
|-----------|--------|------------|
| Collection `Sync` only supports `List<T>` destinations with `RemoveAll` | `ICollection<T>` lacks `RemoveAll`; generating LINQ-based removal adds complexity | Use `Replace` strategy or implement sync manually |
| Auto-flattening max depth: 4 segments | Prevents combinatorial explosion in segment matching | Use explicit `[ForgeProperty]` with dot-path for deeper nesting |
| `[ForgeDictionary]` value types: `object?` only | `Dictionary<string, string>` or other typed dictionaries use standard property mapping | Use `Dictionary<string, object?>` or wrap in an adapter |
| Case-insensitive dictionary lookup is O(n) regardless of dictionary comparer | Generated code performs a linear scan over `source` keys and compares via `string.Equals(..., OrdinalIgnoreCase)` | For better performance, pre-normalize keys or use case-sensitive lookup |
| `ExistingTarget` auto-wiring requires a suitable `void` method with `[UseExistingValue]` parameter | Auto-wiring selects methods by signature and attributes, not by method name | Use the conventional `ForgeInto` method name or explicit `[ForgeWith]` to control/disambiguate mapping |

---

*Specification Version: 1.4*
*Status: Proposed*
*License: MIT*
