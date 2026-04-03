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

| Feature | Issue | Notes |
|---------|-------|-------|
| Auto-flattening with `init`/`required` support | [#82](https://github.com/superyyrrzz/ForgeMap/issues/82) | See [SPEC-v1.5-advanced-mapping.md](SPEC-v1.5-advanced-mapping.md) |
| Dictionary-to-typed-object mapping (`[ForgeDictionary]`) | [#83](https://github.com/superyyrrzz/ForgeMap/issues/83) | See [SPEC-v1.5-advanced-mapping.md](SPEC-v1.5-advanced-mapping.md) |

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
    => (FileType)Enum.Parse(typeof(FileType), source.Type, true);
```

### Design

When the generator detects that the source property type is `string` and the destination property type is an `enum`, and no explicit `[ForgeFrom]`, `[ForgeProperty]`, or `[Ignore]` overrides the property, it automatically emits `(T)Enum.Parse(typeof(T), value, true)` (case-insensitive, non-generic for `netstandard2.0` compatibility). With `TryParse` strategy, it emits the generic `Enum.TryParse<T>()` which is available in `netstandard2.0`.

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
__result.Type = (global::FileType)global::System.Enum.Parse(typeof(global::FileType), source.Type, true);
```

**TryParse strategy:**

```csharp
if (global::System.Enum.TryParse<global::FileType>(source.Type, true, out var __enum_Type))
    __result.Type = __enum_Type;
else
    __result.Type = default;
```

### Null Handling

When the source `string` is nullable, the generated code respects the forger's `NullPropertyHandling`:

| `NullPropertyHandling` | Generated Code |
|-------------------------|----------------|
| `NullForgiving` | `(T)Enum.Parse(typeof(T), source.Prop!, true)` |
| `SkipNull` | `if (source.Prop is { } __v) __result.Prop = (T)Enum.Parse(typeof(T), __v, true);` |
| `CoalesceToDefault` | `__result.Prop = source.Prop is null ? default : (T)Enum.Parse(typeof(T), source.Prop, true);` (empty strings are still parsed and may throw `ArgumentException`) |
| `ThrowException` | `__result.Prop = (T)Enum.Parse(typeof(T), source.Prop ?? throw new ArgumentNullException(...), true);` |

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
| `string` source → `enum?` destination | `(T)Enum.Parse(typeof(T), source.Prop, true)` assigned to nullable |
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

The attribute already exists in `ForgeMap.Abstractions` with a `Type` constructor. v1.4 adds a second `string` constructor overload for instance-based (field/property) references:

```csharp
// ConvertWithAttribute.cs (existing + new overload)
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ConvertWithAttribute : Attribute
{
    public ConvertWithAttribute(Type converterType) { ... }
    public ConvertWithAttribute(string memberName) { ... }
    public Type? ConverterType { get; }
    public string? MemberName { get; }
}

// ITypeConverter.cs (existing, unchanged)
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
// Reference-type destination, NullHandling = ReturnNull (default):
public partial FailedNotificationStorageModel Forge(SendEventRequest source)
{
    if (source == null) return null!;
    return new SendEventRequestConverter().Convert(source);
}

// Reference-type destination, NullHandling = ThrowException:
public partial FailedNotificationStorageModel Forge(SendEventRequest source)
{
    if (source == null) throw new global::System.ArgumentNullException(nameof(source));
    return new SendEventRequestConverter().Convert(source);
}

// Value-type destination:
public partial int Forge(SourceType source)
{
    // No null check — value-type source cannot be null
    return new SourceToIntConverter().Convert(source);
}
```

> **Note:** The null check follows the method's `NullHandling` setting, consistent with all other forge methods. When the source type is a reference type and the destination is a value type, `NullHandling = ReturnNull` is not applicable (compile-time error for value-type returns).

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
     - If the forger has an `IServiceProvider` constructor parameter, resolve the converter from DI: `_services.GetRequiredService(typeof(T))`
     - If the forger has an `IServiceScopeFactory` constructor parameter, create a scope and resolve: `_scopeFactory.CreateScope().ServiceProvider.GetRequiredService(typeof(T))`
     - Otherwise, require an accessible parameterless constructor and instantiate via `new T()`
   - If constructor argument is a `string` (via `nameof`): locate field/property on the forger class
3. **Validate ITypeConverter**: The converter type must implement `ITypeConverter<TSource, TDest>` where `TSource` and `TDest` match the method's parameter and return types
4. **Check constructor** (type-based only, non-DI path): The converter type must have an accessible parameterless constructor only when it is instantiated directly rather than resolved from DI
5. **Emit**: Generate the appropriate delegation code
6. **Precedence**: `[ConvertWith]` takes full control of the method body — `[ForgeProperty]`, `[ForgeFrom]`, and auto-wiring are all ignored

### Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| **FM0034** | Error | `[ConvertWith]` type '{0}' does not implement `ITypeConverter<{1}, {2}>` for the method's source and destination types |
| **FM0035** | Error | `[ConvertWith]` converter type '{0}' has no accessible parameterless constructor and forger has no DI (IServiceProvider/IServiceScopeFactory) |
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
| Converter type has no parameterless constructor and forger has no DI (type-based) | FM0035 error |
| Combined with `[ForgeProperty]` or `[ForgeFrom]` | FM0036 warning, converter wins |
| Combined with `[ForgeAllDerived]` | FM0023 error |
| Source is null with `NullHandling = ReturnNull` | Returns `null!` (before converter is called) |
| Source is null with `NullHandling = ThrowException` | Throws `ArgumentNullException` (before converter is called) |

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.4 |
|--------|-----------|---------|---------------|
| Custom converter | ✅ `.ConvertUsing<T>()` (runtime) | ❌ No equivalent | ✅ `[ConvertWith(typeof(T))]` (compile-time) |
| DI support | ✅ Runtime resolution | N/A | ✅ Field reference or `IServiceProvider` |
| Compile-time validation | ❌ Runtime errors | N/A | ✅ FM0034/FM0035 diagnostics |

---

## New Diagnostics Summary

| Code | Severity | Category | Feature | Description |
|------|----------|----------|---------|-------------|
| FM0028 | Error | `ForgeMap` | Nested existing-target | `ExistingTarget = true` is only valid on `[UseExistingValue]` mutation methods |
| FM0029 | Error | `ForgeMap` | Nested existing-target | Property '{0}' has no getter — cannot read existing value for in-place update |
| FM0030 | Warning | `ForgeMap` | Nested existing-target | No matching `ForgeInto` method found for nested existing-target property '{0}'. The property will be skipped |
| FM0031 | Error | `ForgeMap` | Nested existing-target | `CollectionUpdateStrategy.Sync` requires `KeyProperty` to be set on `[ForgeProperty]` for property '{0}' |
| FM0032 | Error | `ForgeMap` | Nested existing-target | `KeyProperty` '{0}' not found on element type '{1}' |
| FM0033 | Info | `ForgeMap` | String→enum | Property '{0}' auto-converted from string to enum '{1}' using {Parse\|TryParse} (disabled by default) |
| FM0034 | Error | `ForgeMap` | `[ConvertWith]` | `[ConvertWith]` type '{0}' does not implement `ITypeConverter<{1}, {2}>` |
| FM0035 | Error | `ForgeMap` | `[ConvertWith]` | `[ConvertWith]` converter type '{0}' has no accessible parameterless constructor and forger has no DI |
| FM0036 | Warning | `ForgeMap` | `[ConvertWith]` | `[ConvertWith]` on a method that also has `[ForgeProperty]` / `[ForgeFrom]` — converter takes full precedence |

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

### Existing Abstractions Extended (v1.4)

| Type | Change | Description |
|------|--------|-------------|
| `ConvertWithAttribute` | New `string` constructor overload | Enables instance-based converter references via `nameof(field)` |
| `ITypeConverter<TSource, TDest>` | No change | Now code-generated |

---

## Migration Guide

### From v1.3 to v1.4

v1.4 introduces no required source changes and no API-surface breaks. Three behavior items to be aware of:

1. **String-to-enum auto-conversion (default-on)** — `string` source properties mapped to `enum` destinations will auto-convert using the generated `(TEnum)Enum.Parse(typeof(TEnum), value, true)` pattern. Previously-unmapped properties that now match may change behavior. To restore v1.3 behavior:
   ```csharp
   [ForgeMap(StringToEnum = StringToEnumConversion.None)]
   ```
   or set the assembly-level default via `[ForgeMapDefaults(StringToEnum = StringToEnumConversion.None)]`.

2. **Nested existing-target (opt-in)** — `ExistingTarget = true` must be explicitly set; no behavior changes to existing mutation methods

3. **`[ConvertWith]` code generation (automatic)** — methods with `[ConvertWith]` that previously compiled but produced no generated code will produce generated code once implemented. This is the intended behavior; no migration action needed unless converters were incomplete placeholders

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
*v1.5 deferred features: [SPEC-v1.5-advanced-mapping.md](SPEC-v1.5-advanced-mapping.md)*
*License: MIT*
