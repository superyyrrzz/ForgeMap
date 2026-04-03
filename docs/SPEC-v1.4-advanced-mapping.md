# ForgeMap v1.4 Specification — Advanced Mapping

## Overview

v1.4 delivers nested existing-target mapping plus two AutoMapper migration pain points identified from real-world migration experience.

| # | Feature | Issue | Status |
|---|---------|-------|--------|
| 1 | Nested existing-target mapping | [#77](https://github.com/superyyrrzz/ForgeMap/pull/77) | **Shipped** |
| 2 | Auto-convert string→enum | [#80](https://github.com/superyyrrzz/ForgeMap/issues/80) | Planned |
| 3 | `[ConvertWith]` code generation | [#81](https://github.com/superyyrrzz/ForgeMap/issues/81) | Planned |

### Deferred to v1.5

The following features were originally planned for v1.4 but have been moved to v1.5 to prioritize migration-driven issues #80 and #81:

| Feature | Issue | Original v1.4 Section |
|---------|-------|-----------------------|
| Auto-flattening with `init`/`required` support | [#82](https://github.com/superyyrrzz/ForgeMap/issues/82) | Feature 2 (below, retained for reference) |
| Dictionary-to-typed-object mapping (`[ForgeDictionary]`) | [#83](https://github.com/superyyrrzz/ForgeMap/issues/83) | Feature 3 (below, retained for reference) |

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
| Target property is null, `NullPropertyHandling = NullForgiving` | Skip update (equivalent to `SkipNull` — cannot update a null target in place) |
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

## Feature 2: Auto-Convert String Properties to Enum Destinations

> **Issue:** [#80](https://github.com/superyyrrzz/ForgeMap/issues/80)

### Problem

When a source property is `string` and the destination property is an `enum`, ForgeMap currently requires an explicit `[ForgeFrom]` resolver for each property. AutoMapper and Mapperly handle this by convention. In a real-world migration (Docs.LocalizationContentService), this pattern repeated ~6 times, adding ~40-60 lines of boilerplate:

```csharp
// Current workaround: one resolver per property
[ForgeFrom(nameof(UploadFileViewObjectOutput.Type), nameof(ResolveFileType))]
public partial UploadFileViewObjectOutput ForgeUploadFileViewObjectOutput(FileRequest source);

private static FileType ResolveFileType(FileRequest source)
    => Enum.Parse<FileType>(source.Type);
```

### Design

When the generator detects that the source property type is `string` and the destination property type is an `enum`, and no explicit `[ForgeFrom]`, `[ForgeProperty]`, or `[Ignore]` overrides the property, it automatically emits `Enum.Parse<T>()`.

### Configuration

A `StringToEnumConversion` option on `[ForgeMap]` and `[ForgeMapDefaults]` controls the strategy:

```csharp
public enum StringToEnumConversion
{
    /// <summary>Use Enum.Parse (throws on invalid values). Default.</summary>
    Parse,

    /// <summary>Use Enum.TryParse (falls back to default(T) on failure).</summary>
    TryParse,

    /// <summary>Do not auto-convert; require explicit [ForgeFrom] resolver.</summary>
    None
}
```

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ForgeMapAttribute : Attribute
{
    // ... existing properties ...

    /// <summary>
    /// Controls automatic string-to-enum conversion behavior.
    /// Default is <see cref="StringToEnumConversion.Parse"/>.
    /// </summary>
    public StringToEnumConversion StringToEnum { get; set; } = StringToEnumConversion.Parse;
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class ForgeMapDefaultsAttribute : Attribute
{
    // ... existing properties ...

    /// <summary>
    /// Assembly-level default for string-to-enum conversion.
    /// Default is <see cref="StringToEnumConversion.Parse"/>.
    /// </summary>
    public StringToEnumConversion StringToEnum { get; set; } = StringToEnumConversion.Parse;
}
```

### Generated Code

**Parse strategy (default):**

```csharp
__result.Type = global::System.Enum.Parse<FileType>(source.Type);
```

**TryParse strategy:**

```csharp
if (global::System.Enum.TryParse<FileType>(source.Type, out var __enum_Type))
    __result.Type = __enum_Type;
```

### Null Handling

When the source `string` is nullable, the generated code respects the forger's `NullPropertyHandling`:

| `NullPropertyHandling` | Generated Code |
|-------------------------|----------------|
| `NullForgiving` | `Enum.Parse<T>(source.Prop!)` |
| `SkipNull` | `if (source.Prop is { } __v) __result.Prop = Enum.Parse<T>(__v);` |
| `CoalesceToDefault` | `__result.Prop = string.IsNullOrEmpty(source.Prop) ? default : Enum.Parse<T>(source.Prop);` |
| `ThrowException` | `__result.Prop = Enum.Parse<T>(source.Prop ?? throw new ArgumentNullException(...));` |

### Interaction with Existing Features

| Feature | Interaction |
|---------|-------------|
| `[ForgeFrom]` / `[ForgeProperty]` / `[Ignore]` | Explicit attributes take precedence — auto-conversion is skipped |
| `[ReverseForge]` | Reverse direction emits `source.Prop.ToString()` (enum→string) |
| `[ForgeDictionary]` value conversion | Enum.Parse already exists in the dictionary pipeline (priority 4); this feature applies the same logic to standard property mapping |
| `PropertyMatching` | Does not affect conversion — only type detection matters |

### Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| **FM0033** | Info | Property '{0}' auto-converted from string to enum '{1}' using {Parse\|TryParse} (disabled by default) |

### Behavioral Contract

| Scenario | Behavior |
|----------|----------|
| `string` source → `enum` destination, no override | Auto-convert via configured strategy |
| `string?` source → `enum` destination | Follows `NullPropertyHandling` |
| `string` source → `enum?` destination | `Enum.Parse<T>(source.Prop)` assigned to nullable |
| `StringToEnum = None` | No auto-conversion; unmapped property emits FM0006 if no explicit mapping |
| `TryParse` with invalid value | Falls back to `default(T)` — no exception |
| `Parse` with invalid value | Runtime `ArgumentException` |
| Explicit `[ForgeFrom]` on same property | Explicit resolver takes precedence |

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.4 |
|--------|-----------|---------|---------------|
| String→enum | ✅ Convention (runtime) | ✅ Compile-time | ✅ Compile-time |
| Configurable strategy | ❌ Always Parse | ❌ Always Parse | ✅ Parse / TryParse / None |
| Null handling | Runtime | Compile-time | ✅ Follows `NullPropertyHandling` |

---

## Feature 3: `[ConvertWith]` Code Generation

> **Issue:** [#81](https://github.com/superyyrrzz/ForgeMap/issues/81)

### Problem

The `[ConvertWith(typeof(TConverter))]` attribute and `ITypeConverter<TSource, TDest>` interface are defined in ForgeMap.Abstractions but not yet code-generated. Without it, complex multi-step conversions (JSON deserialization, aggregation, cross-type business logic) require manual helper methods in the forger class, losing the declarative pattern.

In the Docs.LocalizationContentService migration, 4 AutoMapper converters became ~120 lines of manual helper methods.

### Existing API Surface

The attribute and interface already exist in `ForgeMap.Abstractions`:

```csharp
// ConvertWithAttribute.cs
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ConvertWithAttribute : Attribute
{
    public ConvertWithAttribute(Type converterType) { ... }
    public Type ConverterType { get; }
}

// ITypeConverter.cs
public interface ITypeConverter<in TSource, out TDestination>
{
    TDestination Convert(TSource source);
}
```

### Usage

**Type-based (parameterless constructor):**

```csharp
[ForgeMap]
public partial class AppForger
{
    [ConvertWith(typeof(SendEventRequestConverter))]
    public partial FailedNotificationStorageModel Forge(SendEventRequest source);
}

public class SendEventRequestConverter : ITypeConverter<SendEventRequest, FailedNotificationStorageModel>
{
    public FailedNotificationStorageModel Convert(SendEventRequest source)
    {
        // Complex conversion logic here
    }
}
```

**Instance-based (DI-aware via field/property reference):**

```csharp
[ForgeMap]
public partial class AppForger
{
    private readonly SendEventRequestConverter _sendEventRequestConverter;

    public AppForger(SendEventRequestConverter converter)
    {
        _sendEventRequestConverter = converter;
    }

    [ConvertWith(nameof(_sendEventRequestConverter))]
    public partial FailedNotificationStorageModel Forge(SendEventRequest source);
}
```

### Generated Code

**Type-based:**

```csharp
public partial FailedNotificationStorageModel Forge(SendEventRequest source)
{
    if (source == null) return null!;
    return new SendEventRequestConverter().Convert(source);
}
```

**Instance-based (field reference):**

```csharp
public partial FailedNotificationStorageModel Forge(SendEventRequest source)
{
    if (source == null) return null!;
    return _sendEventRequestConverter.Convert(source);
}
```

**DI via IServiceProvider (existing constructor pattern from SPEC.md):**

```csharp
public partial DestType Forge(SourceType source)
{
    if (source == null) return null!;
    var converter = (MyConverter)_services.GetRequiredService(typeof(MyConverter));
    return converter.Convert(source);
}
```

### Resolution Algorithm

1. **Detect attribute**: Check if the forge method has `[ConvertWith]`
2. **Resolve converter reference**:
   - If constructor argument is a `Type`: use type-based instantiation
   - If constructor argument is a `string` (via `nameof`): locate field/property on the forger class
3. **Validate ITypeConverter**: The converter type must implement `ITypeConverter<TSource, TDest>` where `TSource` and `TDest` match the method's parameter and return types
4. **Check constructor** (type-based only): The converter type must have an accessible parameterless constructor
5. **Emit**: Generate the appropriate delegation code
6. **Precedence**: `[ConvertWith]` takes full control of the method body — `[ForgeProperty]`, `[ForgeFrom]`, and auto-wiring are all ignored

### Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| **FM0034** | Error | `[ConvertWith]` type '{0}' does not implement `ITypeConverter<{1}, {2}>` for the method's source and destination types |
| **FM0035** | Error | `[ConvertWith]` converter type '{0}' has no accessible parameterless constructor (for type-based usage) |
| **FM0036** | Warning | `[ConvertWith]` on a method that also has `[ForgeProperty]` / `[ForgeFrom]` attributes — converter takes full precedence, property-level attributes are ignored |

### Interaction with Existing Features

| Feature | Interaction |
|---------|-------------|
| `[ForgeProperty]` / `[ForgeFrom]` | Ignored — converter takes full precedence (FM0036 warning) |
| `[ForgeAllDerived]` | FM0023 error (existing diagnostic — mutually exclusive) |
| `[ReverseForge]` | Reverse not auto-generated — if both directions need conversion, declare two `[ConvertWith]` methods |
| `[UseExistingValue]` mutation | Converter's `Convert` returns a new object; direct mutation not supported. Use `[ForgeFrom]` for mutation patterns |
| `NullHandling` | Null check emitted before converter call (same as all forge methods) |

### Behavioral Contract

| Scenario | Behavior |
|----------|----------|
| `[ConvertWith(typeof(T))]` with valid `ITypeConverter` | Instantiate `T` and delegate |
| `[ConvertWith(nameof(field))]` with valid field | Use field reference |
| Converter type doesn't implement `ITypeConverter<S,D>` | FM0034 error |
| Converter type has no parameterless constructor (type-based) | FM0035 error |
| Combined with `[ForgeProperty]` or `[ForgeFrom]` | FM0036 warning, converter wins |
| Combined with `[ForgeAllDerived]` | FM0023 error |
| Source is null | Returns `null!` (before converter is called) |

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.4 |
|--------|-----------|---------|---------------|
| Custom converter | ✅ `.ConvertUsing<T>()` (runtime) | ❌ No equivalent | ✅ `[ConvertWith(typeof(T))]` (compile-time) |
| DI support | ✅ Runtime resolution | N/A | ✅ Field reference or `IServiceProvider` |
| Compile-time validation | ❌ Runtime errors | N/A | ✅ FM0034/FM0035 diagnostics |

---

## Deferred to v1.5: Auto-Flattening with `init`/`required` Support

> **Deferred:** See [#82](https://github.com/superyyrrzz/ForgeMap/issues/82). Specification retained below for reference.

### Problem (Auto-Flattening)

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

## Deferred to v1.5: Dictionary-to-Typed-Object Mapping

> **Deferred:** See [#83](https://github.com/superyyrrzz/ForgeMap/issues/83). Specification retained below for reference.

### Problem (Dictionary Mapping)

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
| 4 | `Enum.Parse` / cast | `value is int i ? (MyEnum)i : value is string s ? Enum.Parse<MyEnum>(s) : /* skip/throw */` | String→enum, int→enum |
| 5 | Nested `[ForgeDictionary]` | `value is IDictionary<string, object?> d ? Forge(d) : value is IReadOnlyDictionary<string, object?> rd ? Forge(rd) : /* skip/throw */` | Nested dictionary-like → nested object |
| 6 | Auto-wired forge method | `value is SourceType s ? Forge(s) : /* skip/throw */` | Complex nested types |
| 7 | `ToString()` | `value?.ToString()` | Any → string (fallback) |

The generator picks the **first applicable** strategy at compile time. If no strategy applies, the property is skipped and **FM0037** is emitted.

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

### v1.4 Diagnostics (shipping)

| Code | Severity | Category | Feature | Description |
|------|----------|----------|---------|-------------|
| FM0028 | Error | `ForgeMap` | Nested existing-target | `ExistingTarget = true` is only valid on `[UseExistingValue]` mutation methods |
| FM0029 | Error | `ForgeMap` | Nested existing-target | Property '{0}' has no getter — cannot read existing value for in-place update |
| FM0030 | Warning | `ForgeMap` | Nested existing-target | No matching `ForgeInto` method found for nested existing-target property '{0}'. The property will be skipped |
| FM0031 | Error | `ForgeMap` | Nested existing-target | `CollectionUpdateStrategy.Sync` requires `KeyProperty` to be set on `[ForgeProperty]` for property '{0}' |
| FM0032 | Error | `ForgeMap` | Nested existing-target | `KeyProperty` '{0}' not found on element type '{1}' |
| FM0033 | Info | `ForgeMap` | String→enum | Property '{0}' auto-converted from string to enum '{1}' using {Parse\|TryParse} (disabled by default) |
| FM0034 | Error | `ForgeMap` | `[ConvertWith]` | `[ConvertWith]` type '{0}' does not implement `ITypeConverter<{1}, {2}>` |
| FM0035 | Error | `ForgeMap` | `[ConvertWith]` | `[ConvertWith]` converter type '{0}' has no accessible parameterless constructor |
| FM0036 | Warning | `ForgeMap` | `[ConvertWith]` | `[ConvertWith]` on a method that also has `[ForgeProperty]` / `[ForgeFrom]` — converter takes full precedence |

### v1.5 Diagnostics (deferred)

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

### New Enums (v1.4)

| Enum | Values | Description |
|------|--------|-------------|
| `CollectionUpdateStrategy` | `Replace`, `Add`, `Sync` | How collection properties are updated in existing-target mutation |
| `StringToEnumConversion` | `Parse`, `TryParse`, `None` | How string source properties are converted to enum destinations |

### New Properties on Existing Attributes (v1.4)

| Attribute | Property | Type | Default | Description |
|-----------|----------|------|---------|-------------|
| `ForgePropertyAttribute` | `ExistingTarget` | `bool` | `false` | Update nested object in place |
| `ForgePropertyAttribute` | `CollectionUpdate` | `CollectionUpdateStrategy` | `Replace` | Collection update strategy for existing-target |
| `ForgePropertyAttribute` | `KeyProperty` | `string?` | `null` | Key property for `Sync` strategy |
| `ForgeMapAttribute` | `StringToEnum` | `StringToEnumConversion` | `Parse` | String-to-enum conversion strategy |
| `ForgeMapDefaultsAttribute` | `StringToEnum` | `StringToEnumConversion` | `Parse` | Assembly-level string-to-enum default |

### Existing Abstractions Used (v1.4 — no new API surface)

| Type | Description |
|------|-------------|
| `ConvertWithAttribute` | Already defined; now code-generated |
| `ITypeConverter<TSource, TDest>` | Already defined; now code-generated |

### Deferred to v1.5

| Type | Kind | Description |
|------|------|-------------|
| `ForgeDictionaryAttribute` | Attribute | Dictionary-to-typed-object mapping |
| `MissingKeyBehavior` | Enum | Skip / Throw for missing dictionary keys |
| `ForgeMapAttribute.AutoFlatten` | Property | Enable auto-flattening |
| `ForgeMapDefaultsAttribute.AutoFlatten` | Property | Assembly-level auto-flattening default |

---

## Migration Guide

### From v1.3 to v1.4

v1.4 introduces no required source changes and no API-surface breaks. Two behavior changes are opt-in:

1. **String-to-enum auto-conversion is on by default** — `string` source properties mapped to `enum` destinations now auto-convert via `Enum.Parse<T>()`. Previously-unmapped properties that now match may change behavior. To restore v1.3 behavior:
   ```csharp
   [ForgeMap(StringToEnum = StringToEnumConversion.None)]
   ```
   or set the assembly-level default via `[ForgeMapDefaults(StringToEnum = StringToEnumConversion.None)]`.

2. **Nested existing-target requires opt-in** — `ExistingTarget = true` must be explicitly set; no behavior changes to existing mutation methods

3. **`[ConvertWith]` is now code-generated** — methods with `[ConvertWith]` that previously compiled but produced no generated code will now produce generated code. This is the intended behavior; no migration action needed unless converters were incomplete placeholders

---

## Limitations

| Limitation | Reason | Workaround |
|-----------|--------|------------|
| Collection `Sync` only supports `List<T>` destinations with `RemoveAll` | `ICollection<T>` lacks `RemoveAll`; generating LINQ-based removal adds complexity | Use `Replace` strategy or implement sync manually |
| `ExistingTarget` auto-wiring requires a suitable `void` method with `[UseExistingValue]` parameter | Auto-wiring selects methods by signature and attributes, not by method name | Use the conventional `ForgeInto` method name or explicit `[ForgeWith]` to control/disambiguate mapping |
| `[ConvertWith]` instance-based usage requires manually declaring the field | Generator cannot infer DI bindings | Use `nameof(field)` pattern or inject `IServiceProvider` |
| `StringToEnum = TryParse` silently falls back to `default(T)` | By design — mirrors `Enum.TryParse` semantics | Use `Parse` if invalid values should throw |

---

*Specification Version: 1.4 (revised 2026-04-03)*
*Status: In Progress — Feature 1 shipped, Features 2-3 planned*
*License: MIT*
