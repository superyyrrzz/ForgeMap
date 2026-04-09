# ForgeMap v1.6 Specification — Migration-Priority Features

## Overview

v1.6 addresses five pain points identified during the OpenPublishing.Build AutoMapper migration. These gaps collectively forced ~300+ lines of manual mapping code across 16 methods in `PullRequestForger` that ForgeMap should eliminate. Features are ordered from smallest to largest scope.

| # | Feature | Issue | Effort | Status |
|---|---------|-------|--------|--------|
| 1 | Null/empty string→enum: default instead of throwing | [#109](https://github.com/superyyrrzz/ForgeMap/issues/109) | Low | Planned |
| 2 | Nullable-safe collection coercion (CS8620) | [#110](https://github.com/superyyrrzz/ForgeMap/issues/110) | Low | Planned |
| 3 | Per-property `ConvertWith` on `[ForgeProperty]` | [#111](https://github.com/superyyrrzz/ForgeMap/issues/111) | Medium | Planned |
| 4 | Built-in type coercions (`DateTimeOffset→DateTime`, generic wrapper unwrapping) | [#112](https://github.com/superyyrrzz/ForgeMap/issues/112) | Medium | Planned |
| 5 | Constructor preference for get-only destination types | [#108](https://github.com/superyyrrzz/ForgeMap/issues/108) | Medium | Planned |

### Deferred to Future Version

The following features were originally envisioned for v1.6 but have been moved to a future version to prioritize these migration-blocking issues:

| Feature | Issue | Notes |
|---------|-------|-------|
| Auto-flattening with `init`/`required` support | [#82](https://github.com/superyyrrzz/ForgeMap/issues/82) | See [SPEC-future-advanced-mapping.md](SPEC-future-advanced-mapping.md) |
| Dictionary-to-typed-object mapping (`[ForgeDictionary]`) | [#83](https://github.com/superyyrrzz/ForgeMap/issues/83) | See [SPEC-future-advanced-mapping.md](SPEC-future-advanced-mapping.md) |

---

## Feature 1: Null/Empty String→Enum Default Coercion

> **Issue:** [#109](https://github.com/superyyrrzz/ForgeMap/issues/109)

### Problem

ForgeMap v1.4 added automatic `string ↔ enum` conversion. However, when the source string is `null` or empty, the generated `Enum.Parse<T>()` call throws `ArgumentNullException` or `ArgumentException`. This is common with REST client models (AutoRest, NSwag) that represent enums as nullable strings. Users must write a manual `[ForgeFrom]` resolver for every such property:

```csharp
// Manual resolver required today for every nullable-string→enum property:
[ForgeFrom(nameof(Dest.Type), nameof(ResolveType))]
public partial Dest ForgeDest(Source source);

private static FileType ResolveType(Source s)
    => string.IsNullOrEmpty(s.Type) ? default : Enum.Parse<FileType>(s.Type);
```

### Design

When converting `string` or `string?` to an enum type, the generator wraps the conversion in a `string.IsNullOrEmpty` guard. This applies to both `StringToEnumConversion.Parse` and `StringToEnumConversion.TryParse` modes. The guard returns `default(TEnum)` for null/empty strings.

This is a behavioral change from v1.5 where `Enum.Parse` was called unconditionally. The new behavior is safer — `Enum.Parse(null)` and `Enum.Parse("")` are always errors, never intentional.

### Generated Code

**`StringToEnumConversion.Parse` (default):**

```csharp
// v1.5: Enum.Parse throws on null/empty
dest.Type = (FileType)global::System.Enum.Parse(typeof(FileType), source.Type, true);

// v1.6: null/empty guard with default fallback
dest.Type = string.IsNullOrEmpty(source.Type)
    ? default(FileType)
    : (FileType)global::System.Enum.Parse(typeof(FileType), source.Type, true);
```

**`StringToEnumConversion.TryParse`:**

```csharp
// v1.5: TryParse already handles null and empty gracefully (returns false, out = default),
// but without an explicit guard the generated code structure differs from Parse mode.
// v1.6: explicit guard for consistency across both modes
dest.Type = !string.IsNullOrEmpty(source.Type)
    && global::System.Enum.TryParse<FileType>(source.Type, true, out var __enumVal_Type)
    ? __enumVal_Type
    : default(FileType);
```

**Nullable string → non-nullable enum with `NullPropertyHandling`:**

The `string.IsNullOrEmpty` guard is applied *within* the existing null-handling wrapper. When the source is `string?` and destination is a non-nullable enum, `NullPropertyHandling` governs the outer null check; the empty-string guard is an inner concern:

```csharp
// NullPropertyHandling.NullForgiving + Parse (default)
dest.Type = string.IsNullOrEmpty(source.Type!)
    ? default(FileType)
    : (FileType)global::System.Enum.Parse(typeof(FileType), source.Type!, true);

// NullPropertyHandling.ThrowException + Parse
dest.Type = source.Type is null
    ? throw new global::System.ArgumentNullException(nameof(source.Type))
    : string.IsNullOrEmpty(source.Type)
        ? default(FileType)
        : (FileType)global::System.Enum.Parse(typeof(FileType), source.Type, true);

// NullPropertyHandling.SkipNull (non-init property)
if (source.Type is { Length: > 0 } __enumStr_Type)
    dest.Type = (FileType)global::System.Enum.Parse(typeof(FileType), __enumStr_Type, true);
```

**Nullable string → nullable enum:**

```csharp
// string? → FileType?
dest.Type = string.IsNullOrEmpty(source.Type)
    ? (FileType?)null
    : (FileType)global::System.Enum.Parse(typeof(FileType), source.Type, true);
```

**Constructor parameters:**

The same guard applies when string→enum conversion is used for constructor parameter matching:

```csharp
var __result = new Dest(
    type: string.IsNullOrEmpty(source.Type)
        ? default(FileType)
        : (FileType)global::System.Enum.Parse(typeof(FileType), source.Type, true)
);
```

### Interaction with Existing Features

| Feature | Interaction |
|---------|-------------|
| `StringToEnumConversion.Parse` | Guard wraps `Enum.Parse` — returns `default(TEnum)` for null/empty instead of throwing |
| `StringToEnumConversion.TryParse` | Guard wraps `TryParse` — explicit `IsNullOrEmpty` check for consistency with `Parse` mode |
| `StringToEnumConversion.None` | No change — auto-conversion disabled, guard not emitted |
| `NullPropertyHandling` | Works inside existing null-handling wrappers — the empty-string guard is an orthogonal concern |
| `[ForgeProperty]` | Applies to explicitly mapped string→enum pairs |
| `[ForgeFrom]` | Resolver takes precedence — guard not emitted when a resolver is defined |
| Constructor mapping (Feature 5) | Guard applies to string→enum ctor parameter conversions, including when constructors are auto-selected via `ConstructorPreference.Auto` |

### Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| **FM0046** | Info | Property '{0}' null/empty string coerced to `default({1})` instead of throwing (disabled by default; enable via `.editorconfig`) |

### Behavioral Contract

| Scenario | v1.5 Behavior | v1.6 Behavior |
|----------|---------------|---------------|
| `null` string → enum (Parse) | `ArgumentNullException` | `default(TEnum)` |
| `""` string → enum (Parse) | `ArgumentException` | `default(TEnum)` |
| `null` string → enum (TryParse) | `default(TEnum)` (TryParse returns false) | `default(TEnum)` (explicit guard) |
| `""` string → enum (TryParse) | `default(TEnum)` (TryParse returns false) | `default(TEnum)` (explicit guard for consistency) |
| Valid string → enum | `Enum.Parse` / `TryParse` as before | Unchanged |
| `null` string → nullable enum | `(TEnum?)null` | `(TEnum?)null` |
| `""` string → nullable enum | `ArgumentException` (Parse) | `(TEnum?)null` |

### Migration Notes

This is a **behavioral change** from v1.5. Projects that relied on `Enum.Parse` throwing on null/empty strings to detect data errors will now silently get `default(TEnum)`. If throw-on-null is desired, use `NullPropertyHandling.ThrowException` for the null case; for empty strings, use a `[ForgeFrom]` resolver.

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.6 |
|--------|-----------|---------|---------------|
| Null string → enum | `default(T)` | `Enum.Parse` (throws) | `default(T)` |
| Empty string → enum | `default(T)` | `Enum.Parse` (throws) | `default(T)` |
| Configuration | None (always default) | None | `StringToEnum` mode + `NullPropertyHandling` |

---

## Feature 2: Nullable-Safe Collection Coercion

> **Issue:** [#110](https://github.com/superyyrrzz/ForgeMap/issues/110)

### Problem

ForgeMap v1.5 added collection type coercion (e.g., `IDictionary<K,V>` → `IReadOnlyDictionary<K,V>`). However, when the element type's nullability differs between source and destination, the generated assignment triggers **CS8620** (nullable reference type variance), which fails under `TreatWarningsAsErrors`.

This is extremely common when mapping between legacy API models (pre-nullable annotations) and modern domain models:

```csharp
// Source (AutoRest-generated, no nullable annotations):
public IDictionary<string, object> Metadata { get; set; }

// Destination (modern C# with nullable annotations):
public IReadOnlyDictionary<string, object?> Metadata { get; init; }

// v1.5 generates:
dest.Metadata = source.Metadata;  // CS8620: nullability mismatch (object vs object?)
```

### Design

When the generator detects a collection or dictionary coercion where element types differ only in nullability annotation (e.g., `object` vs `object?`, `string` vs `string?`), it applies a nullable-safe cast or adapter instead of a direct assignment.

The detection is based on comparing element types after stripping nullable annotations (`WithNullableAnnotation(NullableAnnotation.None)`). If the underlying types match but annotations differ, nullable-safe coercion is applied.

### Generated Code

**Dictionary value nullability widening** (`IDictionary<K,V>` → `IDictionary<K,V?>`):

```csharp
// IDictionary<string, object> → IDictionary<string, object?>
// Widening: non-nullable → nullable is inherently safe, but C# nullable analysis
// treats generic type parameters as invariant, so an explicit conversion is needed.
// Note: same collection wrapper type — only element nullability differs.

// Strategy: materialize with widened value type
dest.Metadata = source.Metadata?.ToDictionary(
    __kv => __kv.Key,
    __kv => (object?)__kv.Value);
```

**Dictionary value nullability narrowing** (`object?` → `object`):

```csharp
// IReadOnlyDictionary<string, object?> → IDictionary<string, object>
// Narrowing: nullable → non-nullable requires filtering or asserting.
// Use NullForgiving (!) since the user explicitly declared the destination as non-nullable.
dest.Metadata = source.Metadata?.ToDictionary(
    __kv => __kv.Key,
    __kv => __kv.Value!);
```

**List/sequence element nullability widening** (`T` → `T?`):

```csharp
// List<string> → IReadOnlyList<string?>
// Widening: safe, but generic variance requires explicit conversion
dest.Items = source.Items is IReadOnlyList<string?> __cast_Items
    ? __cast_Items
    : source.Items?.Select(__item => (string?)__item).ToList();
```

**List/sequence element nullability narrowing** (`T?` → `T`):

```csharp
// IReadOnlyList<string?> → List<string>
// Narrowing: use NullForgiving
dest.Items = source.Items?.Select(__item => __item!).ToList()
    ?? null!;
```

**Combined collection type coercion + nullable element coercion:**

When both the collection wrapper type AND element nullability differ, both conversions are composed:

```csharp
// IDictionary<string, object> → IReadOnlyDictionary<string, object?>
// Collection type coercion (IDictionary → IReadOnlyDictionary) +
// Element nullability widening (object → object?)
dest.Metadata = source.Metadata is IReadOnlyDictionary<string, object?> __cast_Metadata
    ? __cast_Metadata
    : source.Metadata is Dictionary<string, object> __dict_Metadata
        ? new global::System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(
            new global::System.Collections.Generic.Dictionary<string, object?>(
                __dict_Metadata.Select(__kv =>
                    new global::System.Collections.Generic.KeyValuePair<string, object?>(
                        __kv.Key, __kv.Value)),
                __dict_Metadata.Comparer))
        : new global::System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(
            source.Metadata.ToDictionary(
                __kv => __kv.Key,
                __kv => (object?)__kv.Value));
```

### Null Handling

Nullable-safe collection coercion respects `NullPropertyHandling` — the null-handling wrapper encloses the entire coercion expression, same as regular collection coercion in v1.5.

### Interaction with Existing Features

| Feature | Interaction |
|---------|-------------|
| Collection type coercion (v1.5) | This feature extends v1.5 coercion to handle element nullability differences. When both collection type AND element nullability differ, both conversions compose |
| `NullPropertyHandling` | Applied at the outer level — same as v1.5 |
| `[ConvertWith]` | Converter takes full precedence — nullable coercion not applied |
| `[ForgeFrom]` | Resolver takes precedence |
| `TreatWarningsAsErrors` | The fix specifically targets CS8620 compatibility |
| Constructor mapping (Feature 5) | Nullable-safe coercion applies to collection-typed constructor parameters, same as property assignment |

### Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| **FM0047** | Info | Property '{0}' element nullability coerced from '{1}' to '{2}' (disabled by default; enable via `.editorconfig`) |

### Behavioral Contract

| Scenario | v1.5 Behavior | v1.6 Behavior |
|----------|---------------|---------------|
| `IDictionary<K,V>` → `IReadOnlyDictionary<K,V?>` | `dest.X = source.X` (CS8620) | Nullable-safe cast/adapter |
| `List<T?>` → `IReadOnlyList<T>` | `dest.X = source.X` (CS8620) | Select with `!` operator |
| Same nullability, different collection type | v1.5 coercion (unchanged) | Unchanged |
| Same nullability, same collection type | Direct assignment (unchanged) | Unchanged |

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.6 |
|--------|-----------|---------|---------------|
| Nullable element coercion | ✅ Runtime (implicit) | ❌ CS8620 in some cases | ✅ Compile-time safe |
| Combined with type coercion | ✅ Runtime | ❌ | ✅ Composed conversions |
| CS8620 safety | N/A (runtime) | Partial | ✅ Full |

---

## Feature 3: Per-Property `ConvertWith`

> **Issue:** [#111](https://github.com/superyyrrzz/ForgeMap/issues/111)

### Problem

`[ConvertWith]` is currently method-level only (`AttributeTargets.Method`). When a mapping has 20+ properties but only 1-2 need custom conversion, the entire method must stay manual — losing all ForgeMap benefits for the other properties.

```csharp
// 19 out of 20 properties are same-name, same-type — perfect for ForgeMap
// But 1 property (Type) needs StringEnum<T> → T conversion
// So the entire method must be manual:
private static DAO.User ForgeUser(Models.User source) => new()
{
    Login = source.Login,        // simple
    Id = source.Id,              // simple
    // ... 17 more simple properties ...
    Type = GetStringEnumValue<Models.UserType, UserType>(source.Type),  // needs converter
    SiteAdmin = source.SiteAdmin, // simple
};
```

### Design

Add a `ConvertWith` named property to `[ForgeProperty]`. When set, the generator calls the specified method to convert that single property, while all other properties are auto-mapped as usual. The converter method must accept the source property value and return the destination property type.

### API Surface

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ForgePropertyAttribute : Attribute
{
    // ... existing constructor and properties ...

    /// <summary>
    /// The name of a method on the forger class to call for converting this property's value.
    /// The method must accept the source property type and return the destination property type.
    /// Use nameof() for compile-time safety.
    /// When set, the generator calls this method instead of direct assignment for this property.
    /// This is distinct from the method-level [ConvertWith] which takes over the entire method body.
    /// </summary>
    public string? ConvertWith { get; set; }
}
```

### Usage

```csharp
[ForgeMap]
public partial class AppForger
{
    // Only the Type property needs custom conversion — all others auto-map
    [ForgeProperty(nameof(Models.User.Type), nameof(DAO.User.Type),
        ConvertWith = nameof(ConvertUserType))]
    public partial DAO.User ForgeUser(Models.User source);

    private static UserType ConvertUserType(StringEnum<UserType> source)
        => source.Value;
}
```

**Multiple per-property converters:**

```csharp
[ForgeProperty(nameof(Source.CreatedAt), nameof(Dest.CreatedAt),
    ConvertWith = nameof(ToUtcDateTime))]
[ForgeProperty(nameof(Source.UpdatedAt), nameof(Dest.UpdatedAt),
    ConvertWith = nameof(ToUtcDateTime))]
[ForgeProperty(nameof(Source.Type), nameof(Dest.Type),
    ConvertWith = nameof(ConvertUserType))]
public partial Dest ForgeModel(Source source);

private static DateTime ToUtcDateTime(DateTimeOffset source) => source.UtcDateTime;
private static UserType ConvertUserType(StringEnum<UserType> source) => source.Value;
```

**With `NullPropertyHandling`:**

`ConvertWith` and `NullPropertyHandling` can be combined on the same `[ForgeProperty]`. The null check is applied before calling the converter:

```csharp
[ForgeProperty("ClosedAt", "ClosedAt",
    ConvertWith = nameof(ToUtcDateTime),
    NullPropertyHandling = NullPropertyHandling.SkipNull)]
public partial Dest ForgeModel(Source source);
```

### Resolution Algorithm

1. **Detect attribute**: Check if a `[ForgeProperty]` has `ConvertWith` set (non-null)
2. **Resolve method**: Find a method on the forger class with the specified name. The method must:
   - Accept exactly one parameter whose type is assignable from the source property type
   - Return a type assignable to the destination property type
   - Be accessible from the generated code (private, internal, or public — static or instance)
3. **Generate call**: Emit `ConverterMethod(source.SourceProp)` as the assignment expression for the destination property
4. **Null integration**: When `NullPropertyHandling` is also set, the null check wraps the converter call

### Generated Code

**Basic per-property conversion:**

```csharp
public partial DAO.User ForgeUser(Models.User source)
{
    if (source == null) return null!;

    var __result = new DAO.User();

    // Auto-mapped properties (19 of them)
    __result.Login = source.Login;
    __result.Id = source.Id;
    // ... 17 more ...
    __result.SiteAdmin = source.SiteAdmin;

    // Per-property ConvertWith
    __result.Type = ConvertUserType(source.Type);

    return __result;
}
```

**Nullable source property with ConvertWith:**

```csharp
// DateTimeOffset? ClosedAt → DateTime? ClosedAt
// ConvertWith + nullable source → null-conditional call
__result.ClosedAt = source.ClosedAt is { } __v_ClosedAt
    ? ToUtcDateTime(__v_ClosedAt)
    : null;
```

**Non-nullable source → non-nullable dest (simple):**

```csharp
// DateTimeOffset CreatedAt → DateTime CreatedAt
__result.CreatedAt = ToUtcDateTime(source.CreatedAt);
```

**With NullPropertyHandling.SkipNull:**

```csharp
// DateTimeOffset? ClosedAt → DateTime ClosedAt (non-nullable dest, SkipNull)
if (source.ClosedAt is { } __v_ClosedAt)
    __result.ClosedAt = ToUtcDateTime(__v_ClosedAt);
```

**With NullPropertyHandling.CoalesceToDefault:**

```csharp
// DateTimeOffset? ClosedAt → DateTime ClosedAt (non-nullable dest, CoalesceToDefault)
__result.ClosedAt = source.ClosedAt is { } __v_ClosedAt
    ? ToUtcDateTime(__v_ClosedAt)
    : default(DateTime);
```

**init/required properties:**

Per-property `ConvertWith` works with `init` and `required` properties — the converter call is placed in the object initializer:

```csharp
var __result = new Dest
{
    // init/required with ConvertWith
    CreatedAt = ToUtcDateTime(source.CreatedAt),
    Type = ConvertUserType(source.Type),
};
```

**Constructor parameters:**

When a destination type uses constructor-based mapping (Feature 5) and a constructor parameter corresponds to a `[ForgeProperty(ConvertWith = ...)]`, the converter is called in the constructor argument:

```csharp
var __result = new Dest(
    createdAt: ToUtcDateTime(source.CreatedAt),
    type: ConvertUserType(source.Type)
);
```

### Interaction with Existing Features

| Feature | Interaction |
|---------|-------------|
| Method-level `[ConvertWith]` | Separate concern — method-level takes over the entire method body; per-property applies to one property only. Both can exist on the same forger class but not in ways that conflict (method-level makes per-property irrelevant for that method) |
| `[ForgeFrom]` | `[ForgeFrom]` receives the entire source object; per-property `ConvertWith` receives only the source property value. If both target the same destination property, emit **FM0049** |
| `[ForgeWith]` | `[ForgeWith]` calls another forge method; per-property `ConvertWith` calls a converter. If both target the same destination property, emit **FM0049** |
| `[Ignore]` | If a property is both ignored and has `ConvertWith`, the ignore takes precedence (the property is skipped) |
| `NullPropertyHandling` | Per-property override on `[ForgeProperty]` applies to the null-handling wrapper around the converter call |
| `[ReverseForge]` | Reverse method does NOT auto-apply the per-property `ConvertWith` — there is no general way to reverse an arbitrary converter. Use explicit `[ForgeProperty(ConvertWith = ...)]` on the reverse method if needed |
| Auto-wiring | `ConvertWith` takes precedence over auto-wired forge methods for the specified property |
| `[AfterForge]` | `ConvertWith` assignment executes before the `[AfterForge]` callback — the callback can override the converted value |
| `ExistingTarget` / `CollectionUpdate` | `ConvertWith` and `ExistingTarget` can coexist on the same `[ForgeProperty]` — the converter produces the value, then `ExistingTarget` logic applies to how it's assigned |

### Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| **FM0048** | Error | Per-property `ConvertWith` method '{0}' not found on forger class, or has wrong signature. Expected: `{1} {0}({2})` — method must accept the source property type and return the destination property type |
| **FM0049** | Error | Property '{0}' has conflicting conversion attributes: `ConvertWith` on `[ForgeProperty]` cannot be combined with `[ForgeFrom]` or `[ForgeWith]` targeting the same destination property |

### Behavioral Contract

| Scenario | Behavior |
|----------|----------|
| Valid converter method | Converter called for that property; all others auto-mapped |
| Method not found or wrong signature | FM0048 error |
| Conflicts with `[ForgeFrom]` or `[ForgeWith]` | FM0049 error |
| Nullable source, nullable dest | Null-conditional converter call |
| Nullable source, non-nullable dest | `NullPropertyHandling` governs null case; converter called for non-null |
| Converter throws | Exception propagates — no wrapping |
| `init`/`required` destination | Converter call placed in object initializer |
| Constructor parameter destination | Converter call placed in constructor argument |

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.6 |
|--------|-----------|---------|---------------|
| Per-property conversion | ✅ `.ConvertUsing()` per member | ✅ `MapProperty` with user method | ✅ `[ForgeProperty(ConvertWith = ...)]` |
| Compile-time validation | ❌ Runtime errors | ✅ Compile-time | ✅ FM0048 diagnostic |
| Combined with null handling | Runtime config | Limited | ✅ `NullPropertyHandling` on same attribute |
| Scoped to single property | ✅ | ✅ | ✅ |

---

## Feature 4: Built-In Type Coercions

> **Issue:** [#112](https://github.com/superyyrrzz/ForgeMap/issues/112)

### Problem

Two common type mismatches still require manual methods despite ForgeMap's auto-mapping capabilities:

**1. `DateTimeOffset → DateTime`**: Many APIs (Octokit.Webhooks, ASP.NET Core) use `DateTimeOffset`, while domain/DAO models use `DateTime`. A common conversion is `.UtcDateTime`, which preserves the instant in time as a UTC `DateTime`.

**2. Generic wrapper unwrapping (e.g., `StringEnum<T> → T`)**: Libraries like Octokit.Webhooks wrap enums in `StringEnum<T>` for JSON flexibility. The underlying value is accessible via `.Value`.

```csharp
// Must be manual because of these type mismatches:
private static DAO.Milestone ForgeMilestone(Models.Milestone source) => new()
{
    // ... 12 simple properties ...
    CreatedAt = source.CreatedAt.UtcDateTime,   // DateTimeOffset → DateTime
    UpdatedAt = source.UpdatedAt.UtcDateTime,   // DateTimeOffset → DateTime
    Type = source.Type.Value,                    // StringEnum<T> → T
};
```

### Design

The generator recognizes specific type pairs and emits appropriate conversion expressions automatically. This is a compile-time extension of the existing type coercion system (string↔enum, compatible enums) — no new attributes or configuration required.

### Built-In Coercions

#### 4a. `DateTimeOffset → DateTime`

The generator detects when the source property type is `System.DateTimeOffset` and the destination property type is `System.DateTime`, and emits `.UtcDateTime`:

```csharp
// DateTimeOffset → DateTime
__result.CreatedAt = source.CreatedAt.UtcDateTime;

// DateTimeOffset? → DateTime?
__result.ClosedAt = source.ClosedAt?.UtcDateTime;

// DateTimeOffset? → DateTime (non-nullable dest, NullPropertyHandling applies)
// NullForgiving (default):
__result.ClosedAt = source.ClosedAt?.UtcDateTime ?? default!;
// CoalesceToDefault:
__result.ClosedAt = source.ClosedAt?.UtcDateTime ?? default(DateTime);
// ThrowException:
__result.ClosedAt = source.ClosedAt?.UtcDateTime
    ?? throw new global::System.ArgumentNullException(nameof(source.ClosedAt));
```

The reverse direction (`DateTime → DateTimeOffset`) is also supported via the implicit conversion (`new DateTimeOffset(dateTime)`):

```csharp
// DateTime → DateTimeOffset (implicit conversion exists in .NET)
__result.CreatedAt = source.CreatedAt;  // implicit operator handles this

// DateTime? → DateTimeOffset?
__result.ClosedAt = source.ClosedAt.HasValue
    ? new global::System.DateTimeOffset(source.ClosedAt.Value)
    : (global::System.DateTimeOffset?)null;
```

#### 4b. Generic Wrapper Unwrapping (`Wrapper<T> → T`)

The generator detects when:
1. The source property type is an allowlisted generic wrapper type `W<T>` (in v1.6, only `StringEnum<T>`)
2. The destination property type is `T` (the generic type argument)
3. `W<T>` has a public property named `Value` of type `T`

When all conditions are met, the generator emits `.Value`:

```csharp
// StringEnum<UserType> → UserType
__result.Type = source.Type.Value;

// StringEnum<UserType>? → UserType? (nullable wrapper)
__result.Type = source.Type?.Value;

// StringEnum<UserType>? → UserType (non-nullable dest, NullPropertyHandling applies)
// NullForgiving:
__result.Type = source.Type?.Value ?? default!;
// CoalesceToDefault:
__result.Type = source.Type?.Value ?? default(UserType);
```

**Matching criteria for generic wrapper detection:**

1. Source type must be a named type with exactly one type argument: `W<T>`
2. Source type must be an allowlisted wrapper type. For v1.6, the built-in allowlist contains only `StringEnum<T>` (from `Octokit.Webhooks.Models`). Future versions may expand the allowlist or add an opt-in attribute for user-defined wrappers
3. Source type must have a public, non-static property named `Value`
4. The `Value` property's return type must match the destination property type (or be assignable after stripping nullable annotations)
5. The destination type must be `T` (the type argument of `W<T>`)
6. Not triggered when the destination type is `W<T>` (same wrapper type — that's a direct assignment)
7. Explicitly excluded: `Nullable<T>` (handled by existing nullable logic) and `Lazy<T>` (`.Value` has side effects)

This is intentionally narrow to avoid false positives and surprising runtime behavior. Types like `Lazy<T>` have `.Value` properties that trigger computation or throw, so a generic "any type with `.Value`" rule would be unsafe. Only known pure wrappers participate in auto-unwrapping.

### Precedence

Built-in type coercions slot into the existing property assignment priority:

1. Explicit `[ForgeProperty]` remapping, `[ForgeFrom]`, `[Ignore]`, `[ForgeWith]`, or `[ForgeProperty(ConvertWith = ...)]`
2. Direct name match with compatible types (existing)
3. Compatible enum casting (existing)
4. String↔enum auto-conversion (existing)
5. **Built-in type coercions (new — DateTimeOffset→DateTime, wrapper unwrap)**
6. Auto-wire inline collections (existing)
7. Auto-wire forge methods (existing)
8. Unmapped → FM0006

### Interaction with Existing Features

| Feature | Interaction |
|---------|-------------|
| `[ForgeProperty(ConvertWith = ...)]` | `ConvertWith` takes precedence — built-in coercion skipped |
| `[ForgeFrom]` | Resolver takes precedence |
| `NullPropertyHandling` | Applies to the coerced expression (same as other auto-conversions) |
| `[ReverseForge]` | Reverse direction uses the inverse coercion when available (DateTime→DateTimeOffset via implicit conversion, `T → Wrapper<T>` not auto-reversed — use explicit `[ForgeProperty]`) |
| Constructor mapping | Coercions apply to constructor parameter matching (same as string→enum) |
| Auto-wiring | Built-in coercion runs before auto-wire — if a `DateTimeOffset→DateTime` match exists, it does not trigger auto-wire attempts |

### Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| **FM0050** | Info | Property '{0}' auto-coerced from '{1}' to '{2}' via {3} (disabled by default; enable via `.editorconfig`) |

### Behavioral Contract

| Scenario | Behavior |
|----------|----------|
| `DateTimeOffset` → `DateTime` | `.UtcDateTime` |
| `DateTimeOffset?` → `DateTime?` | `?.UtcDateTime` |
| `DateTime` → `DateTimeOffset` | Direct assignment (implicit conversion) |
| `Wrapper<T>` → `T` (allowlisted wrapper with `.Value` property) | `.Value` |
| `Wrapper<T>?` → `T?` | `?.Value` |
| `T` → `Wrapper<T>` | Not auto-reversed — use `[ForgeProperty(ConvertWith = ...)]` |
| No `.Value` property on wrapper type or not allowlisted | No coercion — falls through to auto-wire |

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.6 |
|--------|-----------|---------|---------------|
| `DateTimeOffset→DateTime` | ✅ Runtime conversion | ❌ Manual `MapProperty` | ✅ Built-in `.UtcDateTime` |
| Generic wrapper unwrap | ❌ Manual `ConvertUsing` | ❌ Manual | ✅ Built-in `.Value` detection |
| Compile-time | ❌ Runtime | ✅ | ✅ |
| Reversible | ✅ Runtime | ❌ | ✅ `DateTime→DateTimeOffset` only |

---

## Feature 5: Constructor Preference for Get-Only Destination Types

> **Issue:** [#108](https://github.com/superyyrrzz/ForgeMap/issues/108)

### Problem

ForgeMap currently maps only to settable properties. When a destination type has a parameterized constructor with get-only properties, ForgeMap *does* resolve the constructor and match parameters — but **only when no parameterless constructor exists**. If a parameterless constructor is available, the generator always prefers it (using object initializer), which leaves get-only properties unmapped.

Many domain models enforce immutability with get-only properties set exclusively through the constructor:

```csharp
public class FileViewObjectOutput
{
    public FileViewObjectOutput(string fileId, string locale, FileType type, string version,
                                IReadOnlyDictionary<string, object?>? metadata = null)
    {
        FileId = fileId;
        Locale = locale;
        Type = type;
        Version = version;
        Metadata = metadata;
    }

    public string FileId { get; }     // get-only — can only be set via ctor
    public string Locale { get; }     // get-only
    public FileType Type { get; }     // get-only
    public string Version { get; }    // get-only
    public IReadOnlyDictionary<string, object?>? Metadata { get; }  // get-only
}
```

### Design

The generator applies **automatic constructor preference** when it detects that the destination type has properties that can only be set via constructor. Specifically:

1. **Scan destination properties**: For each property on the destination type, classify as:
   - **Settable**: has a public setter (`set` or `init`)
   - **Get-only**: has only a public getter — can only be set via constructor

2. **Decision rule**: If the destination type has **any get-only properties with matching source properties**, prefer the parameterized constructor over the parameterless one — even when a parameterless constructor exists. The rationale: a parameterless constructor would leave get-only properties at their default values, which is almost never the user's intent when source properties with matching names exist.

3. **Hybrid mapping**: After constructor invocation, remaining settable/init properties that were NOT covered by constructor parameters are mapped via object initializer or post-construction assignment (existing behavior).

This is a refinement of the existing `ResolveConstructor()` logic, which already scores constructors by parameter coverage. The change is in the preference decision: instead of always preferring the parameterless constructor, the generator now evaluates whether skipping it would allow more properties to be mapped.

### Configuration

Constructor preference is controlled by a new `ConstructorPreference` property on `[ForgeMap]` and `[ForgeMapDefaults]`:

```csharp
/// <summary>
/// Controls how the generator selects constructors for destination type instantiation.
/// </summary>
public enum ConstructorPreference
{
    /// <summary>
    /// The generator automatically detects when a parameterized constructor is needed
    /// to map get-only properties. If all destination properties are settable, the
    /// parameterless constructor is preferred. If any get-only properties have matching
    /// source properties, the best-matching parameterized constructor is selected.
    /// Default behavior.
    /// </summary>
    Auto,

    /// <summary>
    /// Always prefer the parameterless constructor when available (v1.5 behavior).
    /// Get-only properties remain unmapped.
    /// </summary>
    PreferParameterless
}
```

```csharp
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ForgeMapAttribute : Attribute
{
    // ... existing properties ...

    /// <summary>
    /// Controls constructor selection for destination types.
    /// Default is <see cref="ConstructorPreference.Auto"/>.
    /// </summary>
    public ConstructorPreference ConstructorPreference { get; set; } = ConstructorPreference.Auto;
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class ForgeMapDefaultsAttribute : Attribute
{
    // ... existing properties ...

    /// <summary>
    /// Assembly-level default for constructor preference. Default is Auto.
    /// </summary>
    public ConstructorPreference ConstructorPreference { get; set; } = ConstructorPreference.Auto;
}
```

### Constructor Selection Algorithm (Updated)

The existing `ResolveConstructor()` method is updated with an additional step:

1. **Enumerate constructors**: Get all public instance constructors
2. **Check for parameterless constructor**: If one exists:
   - **(New)** If `ConstructorPreference == Auto`: scan destination properties for get-only members with matching source properties. If any are found, proceed to step 3 (do not return early)
   - If `ConstructorPreference == PreferParameterless`: return `(null, null)` — use parameterless ctor (v1.5 behavior)
3. **Score parameterized constructors**: For each constructor, count how many parameters can be satisfied from source properties (existing logic — case-insensitive, supports `[ForgeProperty]` remapping, enum compatibility, string→enum, auto-wire)
4. **Select best constructor**:
   - If the best constructor covers ALL get-only properties that have matching source properties, select it
   - If multiple constructors tie on score, emit **FM0013** (existing ambiguous diagnostic)
   - If no constructor can cover the get-only properties, emit **FM0052** (new: constructor cannot satisfy get-only properties)
5. **Hybrid mapping**: Properties covered by constructor parameters are excluded from object initializer / post-construction assignment. Remaining settable/init properties are mapped as usual.

### Usage

```csharp
[ForgeMap]  // ConstructorPreference defaults to Auto
public partial class AppForger
{
    // FileViewObjectOutput has get-only properties → constructor auto-selected
    public partial FileViewObjectOutput Forge(FileViewObjectInput source);

    // OrderDto has all settable properties → parameterless ctor used (unchanged)
    public partial OrderDto Forge(Order source);
}

// Opt out to v1.5 behavior:
[ForgeMap(ConstructorPreference = ConstructorPreference.PreferParameterless)]
public partial class LegacyForger
{
    public partial FileViewObjectOutput Forge(FileViewObjectInput source);
}
```

### Generated Code

**Get-only destination type (constructor preferred):**

```csharp
public partial FileViewObjectOutput Forge(FileViewObjectInput source)
{
    if (source == null) return null!;

    var __result = new FileViewObjectOutput(
        fileId: source.FileId,
        locale: source.Locale,
        type: source.Type,                // direct match
        version: source.Version,
        metadata: source.Metadata          // type coercion may apply (Feature 2/4)
    );

    // No post-construction assignments — all properties are get-only and covered by ctor

    return __result;
}
```

**Hybrid (constructor + settable properties):**

```csharp
public class HybridOutput
{
    public HybridOutput(string id, string name)
    {
        Id = id;
        Name = name;
    }

    public string Id { get; }         // get-only → ctor
    public string Name { get; }       // get-only → ctor
    public string? Description { get; set; }  // settable → post-construction
    public int Count { get; set; }    // settable → post-construction
}

// Generated:
public partial HybridOutput Forge(HybridInput source)
{
    if (source == null) return null!;

    var __result = new HybridOutput(
        id: source.Id,
        name: source.Name
    );

    __result.Description = source.Description;
    __result.Count = source.Count;

    return __result;
}
```

**Constructor with optional parameters and defaults:**

```csharp
public class OutputWithDefaults
{
    public OutputWithDefaults(string id, GroupType groupType = GroupType.None,
                              IReadOnlyDictionary<string, object?>? metadata = null)
    { /* ... */ }

    public string Id { get; }
    public GroupType GroupType { get; }
    public IReadOnlyDictionary<string, object?>? Metadata { get; }
}

// Generated — optional parameters with no matching source property use their defaults:
public partial OutputWithDefaults Forge(Input source)
{
    if (source == null) return null!;

    var __result = new OutputWithDefaults(
        id: source.Id
        // groupType and metadata omitted — use constructor defaults
    );

    return __result;
}
```

If the source *does* have matching properties for optional parameters, they are included:

```csharp
var __result = new OutputWithDefaults(
    id: source.Id,
    groupType: source.GroupType,
    metadata: source.Metadata
);
```

**With `[ForgeProperty]` remapping to constructor parameters:**

`[ForgeProperty]` mappings are used to match source properties to constructor parameter names:

```csharp
[ForgeProperty(nameof(Source.BuildId), nameof(Dest.Id))]
public partial Dest Forge(Source source);

// Constructor parameter "id" matches destination property "Id" via [ForgeProperty]:
var __result = new Dest(
    id: source.BuildId  // remapped via [ForgeProperty]
);
```

**With per-property `ConvertWith` (Feature 3) on constructor parameters:**

```csharp
[ForgeProperty(nameof(Source.CreatedAt), nameof(Dest.CreatedAt),
    ConvertWith = nameof(ToUtcDateTime))]
public partial Dest Forge(Source source);

// Generated:
var __result = new Dest(
    createdAt: ToUtcDateTime(source.CreatedAt)  // ConvertWith applied to ctor arg
);
```

**With built-in type coercions (Feature 4) on constructor parameters:**

```csharp
// Source.CreatedAt is DateTimeOffset, Dest constructor expects DateTime
var __result = new Dest(
    createdAt: source.CreatedAt.UtcDateTime  // built-in coercion
);
```

### Interaction with Existing Features

| Feature | Interaction |
|---------|-------------|
| Existing constructor resolution | This feature modifies the *preference* decision only. The scoring and matching logic remains the same |
| `[ForgeProperty]` | Property remapping is used for constructor parameter matching (existing behavior, unchanged) |
| `[ForgeFrom]` | Resolver-mapped properties are excluded from constructor parameter matching. They are applied post-construction |
| `[ForgeWith]` | ForgeWith-mapped properties are excluded from constructor matching. Applied post-construction |
| Per-property `ConvertWith` (Feature 3) | Converter call is placed in constructor argument expression |
| Built-in type coercions (Feature 4) | Coercion expressions applied to constructor parameter values |
| String→enum (v1.4) | Already supported in constructor parameter matching, unchanged |
| `NullPropertyHandling` | Applied to constructor argument expressions for nullable source → non-nullable parameter |
| `NullHandling` | Source null check still applied before constructor call |
| `[AfterForge]` | Callback invoked after construction and post-construction assignments |
| `init`/`required` properties | `init` properties not covered by the constructor are mapped via object initializer (existing behavior) |
| `ExistingTarget = true` | Not applicable — constructor mapping creates new instances, `ExistingTarget` mutates existing ones |
| `[ReverseForge]` | Reverse method independently resolves constructor for its destination type |
| FM0004 (no accessible constructor) | Still emitted when no constructor can satisfy the destination type |
| FM0013 (ambiguous constructor) | Still emitted when multiple constructors tie on parameter coverage score |
| FM0014 (constructor parameter no matching source) | Still emitted for required parameters with no source match |

### Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| **FM0051** | Info | Constructor preferred over parameterless for type '{0}': {1} get-only properties matched (disabled by default; enable via `.editorconfig`) |
| **FM0052** | Error | Constructor for type '{0}' cannot satisfy get-only properties: {1}. Consider adding `[ForgeProperty]` mappings or using `ConstructorPreference.PreferParameterless` |

### Behavioral Contract

| Scenario | v1.5 Behavior | v1.6 Behavior (Auto) |
|----------|---------------|---------------------|
| All properties settable, parameterless ctor exists | Parameterless ctor | Parameterless ctor (unchanged) |
| Get-only properties, parameterless ctor exists | Parameterless ctor (get-only properties unmapped) | Best parameterized ctor selected |
| Get-only properties, no parameterless ctor | Best parameterized ctor | Best parameterized ctor (unchanged) |
| No constructor matches get-only properties | FM0004 | FM0052 (more specific error) |
| Optional ctor parameters, no matching source | Omitted (use default) | Omitted (use default) — unchanged |
| `ConstructorPreference.PreferParameterless` | N/A | v1.5 behavior restored |

### Migration Notes

This is a **behavioral change** from v1.5 for destination types with get-only properties and a parameterless constructor. In v1.5, the parameterless constructor was always preferred, leaving get-only properties unmapped (FM0006 warning). In v1.6, the generator now attempts to use a parameterized constructor to map those properties.

If this causes issues (e.g., a constructor has side effects), use `ConstructorPreference = ConstructorPreference.PreferParameterless` to restore v1.5 behavior.

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.6 |
|--------|-----------|---------|---------------|
| Constructor mapping | ✅ Runtime reflection | ✅ Compile-time | ✅ Compile-time with auto-preference |
| Get-only property detection | ✅ Automatic | ✅ Automatic | ✅ Automatic |
| Constructor preference config | `.DisableCtorValidation()` | `MapperConstructor` attribute | `ConstructorPreference` enum |
| Hybrid (ctor + setter) | ✅ | ✅ | ✅ |
| Optional parameter handling | Skipped | Skipped | Omitted (uses ctor defaults) |
| Compile-time validation | ❌ Runtime errors | ✅ | ✅ FM0013/FM0014/FM0052 |

---

## Diagnostics Summary

| Code | Severity | Category | Feature | Description |
|------|----------|----------|---------|-------------|
| FM0046 | Info | `ForgeMap` | String→enum guard | Property '{0}' null/empty string coerced to `default({1})` |
| FM0047 | Info | `ForgeMap` | Nullable collection coercion | Property '{0}' element nullability coerced from '{1}' to '{2}' |
| FM0048 | Error | `ForgeMap` | Per-property `ConvertWith` | Per-property `ConvertWith` method '{0}' not found or wrong signature |
| FM0049 | Error | `ForgeMap` | Per-property `ConvertWith` | Property '{0}' has conflicting conversion attributes |
| FM0050 | Info | `ForgeMap` | Built-in type coercions | Property '{0}' auto-coerced from '{1}' to '{2}' via {3} |
| FM0051 | Info | `ForgeMap` | Constructor preference | Constructor preferred over parameterless for type '{0}' |
| FM0052 | Error | `ForgeMap` | Constructor preference | Constructor for type '{0}' cannot satisfy get-only properties: {1} |

---

## API Changes Summary

### New Enum (v1.6)

| Enum | Values | Description |
|------|--------|-------------|
| `ConstructorPreference` | `Auto`, `PreferParameterless` | Controls constructor selection strategy |

### Modified Attributes (v1.6)

| Attribute | Change | Description |
|-----------|--------|-------------|
| `ForgePropertyAttribute` | New property: `ConvertWith` (`string?`) | Per-property converter method name |
| `ForgeMapAttribute` | New property: `ConstructorPreference` | Constructor selection strategy |
| `ForgeMapDefaultsAttribute` | New property: `ConstructorPreference` | Assembly-level constructor selection default |

### No New Attributes

All v1.6 features work within the existing attribute surface (one new property on `ForgePropertyAttribute`, one new enum, two new config properties).

---

## Migration Guide

### From v1.5 to v1.6

v1.6 introduces several behavioral changes that may affect existing forgers:

1. **String→enum null/empty handling (Feature 1)** — `Enum.Parse` on null/empty strings now returns `default(TEnum)` instead of throwing. This is safer but may change behavior for projects that relied on the exception. For projects using `StringToEnumConversion.TryParse`, the generated code now includes an explicit `IsNullOrEmpty` guard for consistency, though runtime behavior is unchanged since `TryParse` already returns `false` for null/empty. **To preserve v1.5 behavior**: use `[ForgeFrom]` with an explicit resolver that calls `Enum.Parse` without the guard.

2. **Constructor preference for get-only types (Feature 5)** — The generator now prefers parameterized constructors when the destination type has get-only properties with matching source properties. This may activate constructor-based mapping for types that previously used the parameterless constructor. **To preserve v1.5 behavior**: set `ConstructorPreference = ConstructorPreference.PreferParameterless` on the forger or assembly.

3. **Nullable collection coercion (Feature 2)** — Properties that previously triggered CS8620 will now generate nullable-safe coercion code. This should only eliminate warnings, not change runtime behavior.

4. **Built-in type coercions (Feature 4)** — `DateTimeOffset → DateTime` and allowlisted generic wrapper unwrapping (`StringEnum<T> → T`) are applied automatically when matching type pairs are detected. Properties that previously required manual methods or were skipped (FM0006) will now be auto-mapped. **To preserve v1.5 behavior**: use `[Ignore]` on the affected property, or use `[ForgeProperty(ConvertWith = ...)]` to control the conversion explicitly.

5. **Per-property ConvertWith (Feature 3)** is opt-in — no behavior change unless explicitly used via `[ForgeProperty(ConvertWith = ...)]`.

---

## Limitations

| Limitation | Reason | Workaround |
|-----------|--------|------------|
| Built-in type coercions are not user-extensible | Hard-coded to avoid combinatorial complexity; per-property `ConvertWith` covers custom cases | Use `[ForgeProperty(ConvertWith = ...)]` for any coercion not built in |
| Generic wrapper unwrap only matches allowlisted types (v1.6: `StringEnum<T>`) | Narrow detection via allowlist to avoid side effects from types like `Lazy<T>` | Use `[ForgeProperty(ConvertWith = ...)]` for wrappers not on the allowlist |
| Reverse direction not supported for `T → Wrapper<T>` | No general way to construct a wrapper from its inner value | Use explicit `[ForgeProperty(ConvertWith = ...)]` on the reverse method |
| Constructor preference may select unexpected constructor | Auto-detection is based on get-only property coverage heuristic | Use `ConstructorPreference.PreferParameterless` to opt out |
| String→enum empty-string guard always uses `default(TEnum)` | No configuration for custom empty-string behavior | Use `[ForgeFrom]` resolver for custom empty-string logic |

---

*Specification Version: 1.6 (2026-04-09)*
*Status: Planned*
*Deferred features: [SPEC-future-advanced-mapping.md](SPEC-future-advanced-mapping.md)*
*License: MIT*
