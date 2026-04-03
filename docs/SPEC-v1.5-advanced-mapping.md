# ForgeMap v1.5 Specification — Advanced Mapping

## Overview

v1.5 delivers two features deferred from the original v1.4 plan to prioritize migration-driven issues.

| # | Feature | Issue | Status |
|---|---------|-------|--------|
| 1 | Auto-flattening with `init`/`required` support | [#82](https://github.com/superyyrrzz/ForgeMap/issues/82) | Planned |
| 2 | Dictionary-to-typed-object mapping (`[ForgeDictionary]`) | [#83](https://github.com/superyyrrzz/ForgeMap/issues/83) | Planned |

---

## Feature 1: Auto-Flattening with `init`/`required` Support

> **Issue:** [#82](https://github.com/superyyrrzz/ForgeMap/issues/82)

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

**Regular setter properties** (`NullHandling = ReturnNull`, `NullPropertyHandling = NullForgiving` — defaults):

```csharp
public partial OrderDto Forge(Order source)
{
    if (source == null) return null!;  // NullHandling.ReturnNull (default); ThrowException would throw here

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
- Intermediate types must have accessible parameterless constructors (or constructor parameters matching the properties). Emit **FM0038** if not constructible
- When multiple destination properties unflatten into the same intermediate object, they are grouped into a single object initializer
- `[ReverseForge]` unflattening is best-effort — emit **FM0039** (warning) for any property that cannot be unflattened, with a suggestion to add explicit `[ForgeProperty]` on the reverse method

### Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| **FM0037** | Info | Property '{0}' auto-flattened from '{1}' (disabled by default; enable via `.editorconfig`) |
| **FM0038** | Error | Unflattening requires type '{0}' to have an accessible constructor, but none was found |
| **FM0039** | Warning | Auto-flattened property '{0}' cannot be unflattened for `[ReverseForge]`: {reason} |

### Interaction with Existing Features

| Feature | Interaction |
|---------|-------------|
| `[ForgeProperty]` with dot-path | Explicit dot-path takes precedence over auto-flattening. Both produce the same codegen — auto-flattening eliminates the need for the attribute |
| `AutoWireNestedMappings` | Auto-flattening runs before auto-wiring. A flattened scalar assignment prevents auto-wiring from treating the property as a nested complex type |
| `PropertyMatching = ByNameCaseInsensitive` | Segment matching is case-insensitive |
| `[IncludeBaseForge]` | Auto-flattened properties from the base method are inherited. Derived methods can override with explicit `[ForgeProperty]` |
| `[Ignore]` | Ignored properties are excluded from auto-flattening |

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.5 (planned) |
|--------|-----------|---------|---------------|
| Auto-flattening | ✅ Runtime convention | ✅ Compile-time PascalCase | ✅ Compile-time PascalCase |
| `init`/`required` properties | ✅ Runtime (since v13) | ❌ Broken (#643) | ✅ Object initializer routing |
| Unflattening | `.ReverseMap()` with runtime | Manual `MapProperty` paths | ✅ Auto via `[ReverseForge]` |
| Opt-out | `.DisableCtorValidation()` | No opt-out | `AutoFlatten = false` |
| Case sensitivity control | Always case-insensitive | Case-insensitive | Follows `PropertyMatching` |
| Null-safe paths | Runtime null checks | Compile-time `?.` chains | Compile-time `?.` chains |
| Diagnostic visibility | None (runtime only) | Compile-time | FM0037 info (opt-in) |

---

## Feature 2: Dictionary-to-Typed-Object Mapping

> **Issue:** [#83](https://github.com/superyyrrzz/ForgeMap/issues/83)

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

    /// <summary>
    /// Throw when the key is missing (KeyNotFoundException), when there is no
    /// applicable conversion (InvalidCastException), or when a framework conversion
    /// helper (e.g., Convert.ToXxx, Enum.Parse) throws; such exceptions
    /// (FormatException, OverflowException, etc.) are propagated as-is.
    /// </summary>
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

**Basic case-sensitive mapping** (`NullHandling = ReturnNull`, default):

```csharp
public partial UserDto Forge(Dictionary<string, object?> source)
{
    if (source == null) return null!;  // NullHandling.ReturnNull (default); ThrowException would throw here

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
    { /* NullPropertyHandling governs: keep default (NullForgiving/SkipNull), coalesce (CoalesceToDefault), or throw (ThrowException) */ }
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
| 4 | `Enum.Parse` / cast | `value is int i ? (MyEnum)i : value is string s ? (MyEnum)Enum.Parse(typeof(MyEnum), s, true) : /* skip/throw */` | String→enum, int→enum |
| 5 | Nested `[ForgeDictionary]` | `value is IDictionary<string, object?> d ? Forge(d) : value is IReadOnlyDictionary<string, object?> rd ? Forge(rd) : /* skip/throw */` | Nested dictionary-like → nested object |
| 6 | Auto-wired forge method | `value is SourceType s ? Forge(s) : /* skip/throw */` | Complex nested types |
| 7 | `ToString()` | `value?.ToString()` | Any → string (fallback) |

The generator picks the **first applicable** strategy at compile time. If no strategy applies, the property is skipped and **FM0041** is emitted.

For strategies that use framework conversion helpers (e.g., `Convert.ToXxx`, `Enum.Parse`), any exceptions thrown by those helpers (`FormatException`, `OverflowException`, `ArgumentException`, etc.) are propagated as-is; the generator does **not** catch and wrap them into a uniform `InvalidCastException`.

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

// Reverse (auto-generated, NullHandling = ReturnNull default):
public Dictionary<string, object?> Forge(UserDto source)
{
    if (source == null) return null!;  // NullHandling.ReturnNull (default); ThrowException would throw here

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
| **FM0040** | Error | `[ForgeDictionary]` source parameter must be `Dictionary<string, object?>`, `IDictionary<string, object?>`, or `IReadOnlyDictionary<string, object?>` |
| **FM0041** | Warning | Destination property '{0}' of type '{1}' has no applicable conversion from `object?`. The property will be skipped |
| **FM0042** | Info | Property '{0}' mapped from dictionary key '{1}' with conversion '{2}' (disabled by default) |

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

| Aspect | AutoMapper | Mapperly | ForgeMap v1.5 (planned) |
|--------|-----------|---------|---------------|
| Dictionary→Object | ❌ Not supported | ❌ Not supported (#1309) | ✅ `[ForgeDictionary]` |
| Case-insensitive keys | N/A | N/A | ✅ `KeyMatching` option |
| Missing key behavior | N/A | N/A | ✅ `Skip` or `Throw` |
| Type conversion | N/A | N/A | ✅ 7-tier compile-time strategy |
| Reverse (Object→Dictionary) | N/A | N/A | ✅ Via `[ReverseForge]` |
| Zero reflection | N/A | N/A | ✅ All compile-time generated |

---

## Diagnostics Summary

| Code | Severity | Category | Feature | Description |
|------|----------|----------|---------|-------------|
| FM0037 | Info | `ForgeMap` | Auto-flattening | Property '{0}' auto-flattened from '{1}' (disabled by default) |
| FM0038 | Error | `ForgeMap` | Auto-flattening | Unflattening requires type '{0}' to have an accessible constructor |
| FM0039 | Warning | `ForgeMap` | Auto-flattening | Auto-flattened property '{0}' cannot be unflattened for `[ReverseForge]`: {reason} |
| FM0040 | Error | `ForgeMap` | `[ForgeDictionary]` | Source parameter must be `Dictionary<string, object?>`, `IDictionary<string, object?>`, or `IReadOnlyDictionary<string, object?>` |
| FM0041 | Warning | `ForgeMap` | `[ForgeDictionary]` | Destination property '{0}' of type '{1}' has no applicable conversion from `object?` |
| FM0042 | Info | `ForgeMap` | `[ForgeDictionary]` | Property '{0}' mapped from dictionary key '{1}' with conversion '{2}' (disabled by default) |

---

## API Changes Summary

| Type | Kind | Description |
|------|------|-------------|
| `ForgeDictionaryAttribute` | Attribute | Dictionary-to-typed-object mapping |
| `MissingKeyBehavior` | Enum | Skip / Throw for missing dictionary keys |
| `ForgeMapAttribute.AutoFlatten` | Property | Enable auto-flattening |
| `ForgeMapDefaultsAttribute.AutoFlatten` | Property | Assembly-level auto-flattening default |

---

*Specification Version: 1.5 (2026-04-03)*
*Status: Planned*
*License: MIT*
