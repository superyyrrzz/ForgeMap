# ForgeMap v1.7 Specification — Projection, Conditional Assignment, and Entity↔Primitive Mapping

## Overview

v1.7 closes three AutoMapper-parity gaps surfaced by the [Duende.IdentityServer.Admin migration analysis](https://github.com/skoruba/Duende.IdentityServer.Admin/issues/282). Together these features unblock ~22 mappings in that codebase and address recurring patterns in any EF Core project that uses join-table entities, nullable update DTOs, or projection extraction.

| # | Feature | Issue | Effort | Status |
|---|---------|-------|--------|--------|
| 1 | Per-property LINQ projection (`SelectProperty`) | [#125](https://github.com/superyyrrzz/ForgeMap/issues/125) | Low | Planned |
| 2 | Conditional property assignment (`Condition`, `SkipWhen`) | [#126](https://github.com/superyyrrzz/ForgeMap/issues/126) | Medium | Planned |
| 3 | Entity↔primitive mapping (`[ExtractProperty]`, `[WrapProperty]`) | [#127](https://github.com/superyyrrzz/ForgeMap/issues/127) | Medium | Planned |

Features 1 and 3 compose naturally: `SelectProperty` extracts a primitive from each element in a collection, while `[ExtractProperty]` / `[WrapProperty]` handle the single-element case. Both target the same join-table-entity pattern from opposite ends.

---

## Feature 1: Per-Property LINQ Projection (`SelectProperty`)

> **Issue:** [#125](https://github.com/superyyrrzz/ForgeMap/issues/125)

### Problem

Entity collections frequently hold objects with one meaningful field (e.g., `List<ApiResourceClaim>` where each claim has a `.Type` string), while the corresponding DTO collection is the flat primitive list (`List<string>`). Without projection support, every such mapping needs a `[ForgeFrom]` helper:

```csharp
[ForgeFrom(nameof(ApiResourceDto.UserClaims), nameof(ResolveClaims))]
public partial ApiResourceDto Forge(ApiResource source);

private static List<string> ResolveClaims(ApiResource s)
    => s.UserClaims?.Select(x => x.Type).ToList() ?? new List<string>();
```

For a single mapper with 5–10 such projections (typical of identity/auth domain models), this defeats the source-generator value proposition.

### Design

Add a `SelectProperty` named property to `[ForgeProperty]`. When set, the generator emits `source.Src?.Select(x => x.<member named by SelectProperty>).To<Collection>()` for that property. The element-type's projected property is resolved by name on the source collection's element type.

The destination collection wrapper type is determined by the existing collection-coercion machinery (v1.5 Feature 2). The element type of the destination must be assignable from the element-type's projected property type — otherwise the generator falls back to applying string↔enum or built-in coercions on the projected value (v1.6 Feature 4).

### API Surface

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ForgePropertyAttribute : Attribute
{
    // ... existing constructor and properties ...

    /// <summary>
    /// Name of a property on the source collection's element type to project.
    /// When set, generates `source.Src?.Select(x => x.&lt;member named by SelectProperty&gt;).To&lt;TDest&gt;()`.
    /// Use nameof() for compile-time safety.
    /// Mutually exclusive with ConvertWith and ConvertWithType on the same [ForgeProperty].
    /// </summary>
    public string? SelectProperty { get; set; }
}
```

### Usage

```csharp
[ForgeMap]
public partial class ApiResourceMapper
{
    [ForgeProperty(nameof(ApiResource.UserClaims), nameof(ApiResourceDto.UserClaims),
        SelectProperty = nameof(ApiResourceClaim.Type))]
    [ForgeProperty(nameof(ApiResource.Scopes), nameof(ApiResourceDto.Scopes),
        SelectProperty = nameof(ApiResourceScope.Scope))]
    public partial ApiResourceDto Forge(ApiResource source);
}
```

### Resolution Algorithm

1. **Detect attribute**: Check if a `[ForgeProperty]` has `SelectProperty` set (non-null).
2. **Validate exclusivity**: If `ConvertWith` or `ConvertWithType` is also set on the same `[ForgeProperty]`, emit **FM0058**. The three are mutually exclusive — they all replace the per-property assignment expression and have no defined composition order.
3. **Validate source side**: The source property must be an enumerable type (`IEnumerable<TElement>`, `IReadOnlyCollection<TElement>`, array, etc.). Otherwise emit **FM0055**.
4. **Resolve projection member**: Find a public, non-static, get-accessible property on `TElement` named `SelectProperty`. Otherwise emit **FM0056**.
5. **Validate destination side**: The destination property must be an enumerable type. If it is not, emit **FM0073** (non-enumerable destination). When the destination *is* enumerable, its element type must be either:
   - Directly assignable from the projected property's type, OR
   - Reachable through built-in coercions (string↔enum, `DateTimeOffset→DateTime`, allowlisted wrapper unwrap, nullability widening/narrowing). The selected coercion is applied inside the `Select` lambda.
   - Otherwise emit **FM0057** (element type incompatibility).
6. **Choose materialization**: Use the existing v1.5 collection-coercion logic based on the destination wrapper type (for example, `.ToList()`, `.ToArray()`, `new HashSet<T>(...)`, or `new ReadOnlyCollection<T>(...)`).

### Generated Code

**Basic projection (`List<Entity>` → `List<string>`):**

```csharp
public partial OrderDto Forge(OrderEntity source)
{
    if (source == null) return null!;

    var __result = new OrderDto();
    __result.ProductNames = source.Lines is { } __lines ? __lines.Select(__x => __x.ProductName).ToList() : null!;
    __result.Tags = source.Tags is { } __tags ? __tags.Select(__x => __x.Name).ToList() : null!;
    return __result;
}
```

**With `NullPropertyHandling.SkipNull` on a non-nullable destination collection:**

```csharp
if (source.Lines is { } __v_Lines)
    __result.ProductNames = __v_Lines.Select(__x => __x.ProductName).ToList();
```

**Projection composed with built-in coercion (Feature 4 from v1.6):**

```csharp
// source.Audits: List<AuditEntry>, AuditEntry.At is DateTimeOffset
// dest.AuditTimes: List<DateTime>
__result.AuditTimes = source.Audits?.Select(__x => __x.At.UtcDateTime).ToList();
```

**Projection composed with string→enum (v1.4):**

```csharp
// source.Roles: List<RoleEntity>, RoleEntity.Code is string
// dest.RoleCodes: List<RoleCode> where RoleCode is an enum
__result.RoleCodes = source.Roles?.Select(__x => string.IsNullOrEmpty(__x.Code)
    ? default(RoleCode)
    : (RoleCode)global::System.Enum.Parse(typeof(RoleCode), __x.Code, true)).ToList();
```

**Destination collection coercion (`List<Entity>` → `IReadOnlyList<string>`):**

```csharp
__result.Items = source.Lines is { } __lines ? __lines.Select(__x => __x.ProductName).ToList() : null!;
// Coerced to IReadOnlyList<string> via existing v1.5 collection-coercion machinery.
```

### Reverse Direction

`SelectProperty` is **not auto-reversed** by `[ReverseForge]`. Reversing extraction requires *constructing* an element from a primitive, which has many valid forms (full constructor, object initializer, factory). For the reverse direction, use either:

- Per-property `ConvertWith` (v1.6) with a wrapper helper, or
- `[WrapProperty]` (v1.7 Feature 3) for the single-element case combined with collection auto-wire.

Example reverse using `ConvertWith`:

```csharp
[ForgeProperty(nameof(ApiResourceDto.UserClaims), nameof(ApiResource.UserClaims),
    ConvertWith = nameof(WrapClaims))]
public partial ApiResource ForgeEntity(ApiResourceDto source);

private static List<ApiResourceClaim> WrapClaims(List<string> types)
    => types?.Select(t => new ApiResourceClaim { Type = t }).ToList() ?? new List<ApiResourceClaim>();
```

### Interaction with Existing Features

| Feature | Interaction |
|---------|-------------|
| `ConvertWith` (v1.6) | Mutually exclusive with `SelectProperty` on the same `[ForgeProperty]` — emit **FM0058** |
| `ConvertWithType` (v1.6) | Mutually exclusive with `SelectProperty` on the same `[ForgeProperty]` — emit **FM0058** |
| `[ForgeFrom]` / `[ForgeWith]` | Mutually exclusive with `SelectProperty` on the same destination property — emit **FM0072** rather than silently letting the resolver/forge win |
| `[Ignore]` | Ignore wins — projection skipped |
| `NullPropertyHandling` | Wraps the projection expression (same pattern as collection coercion) |
| Collection coercion (v1.5) | Materialization respects destination wrapper type |
| Built-in coercions (v1.6) | Applied *inside* the `Select` lambda when projected element type and destination element type don't match directly |
| String→enum (v1.4) | Applied inside the `Select` lambda when projected type is `string` and destination element type is an enum |
| Auto-wiring | `SelectProperty` short-circuits auto-wire for that property — no nested forger lookup |
| `[ReverseForge]` | Not auto-reversed — see "Reverse Direction" |
| Dictionaries | Out of scope for v1.7. `SelectProperty` only supports sequence sources |

### Diagnostics

> **Diagnostic ID note**: `docs/SPEC-future-advanced-mapping.md` (a speculative, unscheduled spec) tentatively allocates FM0055–FM0060 to auto-flattening / `[ForgeDictionary]`. v1.7 is the next concrete release, so it claims FM0055–FM0073 here; if the future spec is ever promoted, its diagnostics will be renumbered to start above v1.7's allocation.

| Code | Severity | Description |
|------|----------|-------------|
| **FM0055** | Error | `SelectProperty` set on '{0}' but source property type '{1}' is not enumerable |
| **FM0056** | Error | `SelectProperty = "{0}"` not found on element type '{1}' for property '{2}' (or not a public readable property) |
| **FM0057** | Error | Projected property '{0}' (type '{1}') is not assignable to destination element type '{2}' for property '{3}', and no built-in coercion applies |
| **FM0073** | Error | `SelectProperty` set on '{0}' but destination property type '{1}' is not enumerable |
| **FM0058** | Error | Property '{0}' has more than one of `SelectProperty`, `ConvertWith`, `ConvertWithType` set on the same `[ForgeProperty]` — choose one |
| **FM0072** | Error | Property '{0}' has `SelectProperty` set on `[ForgeProperty]` and is also targeted by `[ForgeFrom]` / `[ForgeWith]` — choose one |
| **FM0059** | Info (disabled) | Projection applied for property '{0}': `{1}.Select(x => x.{2})` |

### Behavioral Contract

| Scenario | Behavior |
|----------|----------|
| Source enumerable, dest enumerable, element types compatible | `Select(x => x.Prop)` materialized to dest wrapper |
| Source property value is `null` | Result property is governed by the generated projection expression (`source.Src?.Select(...)`) and `NullPropertyHandling` for the member; this is distinct from top-level source-parameter `NullHandling` |
| Source empty | Empty destination collection |
| Projected element type needs coercion | Coercion applied inside `Select` lambda |
| `SelectProperty` not found on element type | FM0056 |
| Destination not enumerable | FM0073 |

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.7 |
|--------|-----------|---------|---------------|
| Projection in member mapping | ✅ `MapFrom(s => s.Coll.Select(...))` | ✅ Via `MapProperty` with method | ✅ `[ForgeProperty(SelectProperty = ...)]` |
| Compile-time validation | ❌ Runtime | ✅ | ✅ FM0055–FM0058, FM0072–FM0073 |
| Auto-coerce projected element | ✅ Runtime | Partial | ✅ Composes with v1.4/v1.6 coercions |

---

## Feature 2: Conditional Property Assignment (`Condition`, `SkipWhen`)

> **Issue:** [#126](https://github.com/superyyrrzz/ForgeMap/issues/126)

### Problem

When mapping into an *existing* destination (e.g., `ForgeInto` for entity updates from DTOs), users frequently need to skip an assignment when the source value or source object is in a "do not overwrite" state — most commonly null or default. Today the only workaround is `[AfterForge]` to undo an assignment, which is backwards (assign then revert) and forces a full method-level callback for what should be a per-property concern.

### Design

Add two named properties to `[ForgeProperty]`:

- **`Condition`**: A predicate method invoked with the **source property value**. Returns `true` to assign, `false` to skip.
- **`SkipWhen`**: A predicate method invoked with the **source object**. Returns `true` to skip, `false` to assign.

These two are mutually exclusive on a single `[ForgeProperty]`. They differ in what they receive: `Condition` is the value-level guard (most common case for "skip nulls"), `SkipWhen` is the source-level guard (needed when the decision depends on multiple source fields or comparing the source to context).

### API Surface

```csharp
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class ForgePropertyAttribute : Attribute
{
    // ... existing constructor and properties ...

    /// <summary>
    /// Name of a predicate method on the forger class. Called with the source property value;
    /// when it returns false, the destination assignment is skipped.
    /// Signature: `bool MethodName(TSourceProperty value)`. Static or instance, any accessibility.
    /// Mutually exclusive with SkipWhen.
    /// </summary>
    public string? Condition { get; set; }

    /// <summary>
    /// Name of a predicate method on the forger class. Called with the source object;
    /// when it returns true, the destination assignment is skipped.
    /// Signature: `bool MethodName(TSource source)`. Static or instance, any accessibility.
    /// Mutually exclusive with Condition.
    /// </summary>
    public string? SkipWhen { get; set; }
}
```

### Usage

```csharp
[ForgeMap]
public partial class ConditionalMapper
{
    [ForgeProperty(nameof(SettingsEntity.Protocol), nameof(SettingsDto.Protocol),
        Condition = nameof(IsNotNull))]
    [ForgeProperty(nameof(SettingsEntity.Type), nameof(SettingsDto.Type),
        Condition = nameof(IsNotNull))]
    public partial void ForgeInto(SettingsEntity source, [UseExistingValue] SettingsDto destination);

    private static bool IsNotNull(string? value) => value is not null;
}

[ForgeMap]
public partial class SourceConditionMapper
{
    [ForgeProperty(nameof(RoleDto.Id), nameof(Role.Id),
        SkipWhen = nameof(IdIsDefault))]
    public partial void ForgeInto(RoleDto source, [UseExistingValue] Role destination);

    private static bool IdIsDefault(RoleDto source) => source.Id == 0;
}
```

### Resolution Algorithm

1. **Detect attribute**: Either `Condition` or `SkipWhen` is set on a `[ForgeProperty]`.
2. **Mutual-exclusivity check**: If both are set, emit **FM0060**.
3. **Resolve method** on the forger class:
   - For `Condition`: must accept exactly one parameter assignable from the source property type, return `bool`. Otherwise emit **FM0061**.
   - For `SkipWhen`: must accept exactly one parameter assignable from the source object type, return `bool`. Otherwise emit **FM0061**.
4. **Generate guarded assignment**: Wrap the destination assignment expression in an `if` (for property/object-initializer cases, see Generated Code).
5. **Compose with `ConvertWith` and `SelectProperty`**: The guard runs first; the converter/projection runs only when the guard passes.

### Generated Code

**Plain `Condition` (value-level), property assignment:**

```csharp
public partial void ForgeInto(SettingsEntity source, SettingsDto destination)
{
    if (source == null) return;

    if (IsNotNull(source.Protocol))
        destination.Protocol = source.Protocol;
    if (IsNotNull(source.Type))
        destination.Type = source.Type;
}
```

**`SkipWhen` (source-level):**

```csharp
public partial void ForgeInto(RoleDto source, Role destination)
{
    if (source == null) return;

    if (!IdIsDefault(source))
        destination.Id = source.Id;
}
```

**`Condition` composed with `ConvertWith`:**

```csharp
// Condition runs against the raw source value; converter runs only on assignment.
// Null-flow rule: when the source value's static type is nullable AND the
// converter's parameter type is non-nullable, the generator emits `!` after the
// condition check. The user opts into this contract by declaring `Condition` —
// a predicate that returns true is assumed to have proved the value usable.
if (IsNotNull(source.CreatedAt))
    destination.CreatedAt = ToUtcDateTime(source.CreatedAt!);
```

**`Condition` composed with `SelectProperty`:**

```csharp
// Condition is on the source collection (List<...>); projection runs only on assignment.
// Null-flow rule: same as above — when the predicate returns true, the generator
// drops the `?.` chain and accesses the collection directly. If the predicate
// does NOT actually guard against null, this becomes a NullReferenceException at
// runtime; documenting the contract is the user's responsibility.
if (HasItems(source.Lines))
    destination.ProductNames = source.Lines.Select(__x => __x.ProductName).ToList();
```

> **Design note — why not `source.Lines != null && HasItems(source.Lines)`?**
>
> The generator deliberately does **not** synthesize an extra `!= null` guard around the user's predicate. `Condition` is a contract: the user asserts the predicate returns `true` only when the value is safe to dereference. Adding a redundant null check would (a) double-evaluate predicates with side effects, (b) hide buggy predicates by silently turning a logic error into a no-op, and (c) duplicate the role `NullPropertyHandling` already plays for the "skip null" case. Users who want a pure "skip null" guard should use `NullPropertyHandling.SkipNull` (no predicate needed) or write their predicate to include the null check explicitly. Misuse surfaces as a `NullReferenceException` at runtime — diagnostic **FM0064** (info, opt-in) reports each conditional emit so audits can find risky predicates.

**`Condition`/`SkipWhen` on a *new-instance* mapper (non-`ForgeInto`):**

When the destination is constructed fresh (no existing target), a skipped assignment leaves the destination property at its **default-constructed value** (i.e., whatever the parameterless ctor or object initializer would produce without that property). This means:

```csharp
public partial SettingsDto Forge(SettingsEntity source)
{
    if (source == null) return null!;

    var __result = new SettingsDto();   // Protocol = "default-protocol"
    if (IsNotNull(source.Protocol))
        __result.Protocol = source.Protocol;
    return __result;
}
```

For `init`/`required` properties and constructor parameters, the conditional has no safe semantics (you cannot "skip" a required ctor argument). The generator emits **FM0062** for this case — use `[ForgeFrom]` if conditional default selection is needed.

### Interaction with Existing Features

| Feature | Interaction |
|---------|-------------|
| `ConvertWith` (v1.6) | Guard wraps the converter call |
| `SelectProperty` (v1.7) | Guard wraps the projection expression |
| `[ForgeFrom]` / `[ForgeWith]` | Mutually exclusive with `Condition`/`SkipWhen` on the same destination property — emit **FM0063** |
| `[Ignore]` | Ignore wins |
| `NullPropertyHandling` | Applied inside the conditional branch — `Condition` typically replaces null-handling for the "skip null" case, but they can coexist (`Condition` is a more general predicate) |
| `[UseExistingValue]` / `ForgeInto` | Primary use case — skipped assignments preserve the existing destination value |
| `init`/`required` properties | **Not supported** — emit **FM0062** |
| Constructor parameters | **Not supported** — emit **FM0062** |
| `[ReverseForge]` | Not auto-reversed (predicates rarely have an inverse). Re-declare the conditional on the reverse method |
| `[AfterForge]` | Callback runs after all guarded assignments — can still override |

### Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| **FM0060** | Error | Property '{0}' has both `Condition` and `SkipWhen` set — choose one |
| **FM0061** | Error | Predicate method '{0}' for property '{1}' not found or has the wrong signature. Expected: `bool {0}(T)` where `T` is assignable from the source property/source type |
| **FM0062** | Error | `Condition`/`SkipWhen` cannot be applied to property '{0}' because it is set via constructor or `init`/`required`. Use `[ForgeFrom]` instead |
| **FM0063** | Error | Property '{0}' has conflicting attributes: `Condition`/`SkipWhen` cannot combine with `[ForgeFrom]` or `[ForgeWith]` targeting the same destination |
| **FM0064** | Info (disabled) | Conditional assignment applied for property '{0}' via `{1}` |

### Behavioral Contract

| Scenario | Behavior |
|----------|----------|
| `Condition` returns `true` | Destination assigned |
| `Condition` returns `false` | Assignment skipped — destination preserves prior value |
| `SkipWhen` returns `true` | Assignment skipped |
| `SkipWhen` returns `false` | Destination assigned |
| Predicate throws | Exception propagates |
| Both `Condition` and `SkipWhen` set | FM0060 |
| Targets `init`/`required`/ctor param | FM0062 |
| Targets same property as `[ForgeFrom]`/`[ForgeWith]` | FM0063 |

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.7 |
|--------|-----------|---------|---------------|
| Per-property condition | ✅ `.Condition()` / `.PreCondition()` | ⚠️ Limited (`MapPropertyFromSource` with method) | ✅ `Condition` + `SkipWhen` |
| Source-level vs value-level | ✅ Both | ❌ | ✅ Both (separate attributes) |
| Compile-time validation | ❌ Runtime | ✅ | ✅ FM0060–FM0064 |
| Works with existing-target mapping | ✅ | ✅ | ✅ Primary use case |

---

## Feature 3: Entity↔Primitive Mapping (`[ExtractProperty]`, `[WrapProperty]`)

> **Issue:** [#127](https://github.com/superyyrrzz/ForgeMap/issues/127)

### Problem

EF Core join-table entities frequently model a single string relationship as an entity wrapper:

```csharp
public class ClientGrantType
{
    public int Id { get; set; }
    public int ClientId { get; set; }
    public string GrantType { get; set; }   // the only meaningful field
}
```

DTO/domain layers expect just `string`. Today, ForgeMap supports only object↔object — there is no first-class way to declare "this partial method extracts one property as the return value" or "wrap a string into a new entity." Users either write `[ConvertWith]` with a full converter class (heavyweight) or hand-write the partial body.

### Design

Two new method-level attributes:

- **`[ExtractProperty(name)]`**: Emits a partial body that returns `source.<name>` (with null-guard).
- **`[WrapProperty(name)]`**: Emits a partial body that wraps `source` into `TDest` by assigning `<name>`, using either an object-initializer form (`new TDest { <name> = source }`) or a compatible constructor form (`new TDest(<name>: source)`) as applicable (with null-guard).

Both are method-level (not property-level) because they describe the *shape* of the entire forge method (the destination *is* the projected primitive, or the destination *is* the wrapping entity). They are intentionally narrower than `[ConvertWith]`: the generator validates the shape and emits a one-line body without requiring user code.

### API Surface

```csharp
/// <summary>
/// Marks a partial forge method that returns a single property of the source object.
/// The method must have signature `partial TPrimitive MethodName(TEntity source)`.
/// The generator emits a null-guard governed by `NullHandling`, then returns
/// `source.PropertyName` (with coercion if needed for the declared return type).
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class ExtractPropertyAttribute : Attribute
{
    public ExtractPropertyAttribute(string propertyName) => PropertyName = propertyName;
    public string PropertyName { get; }
}

/// <summary>
/// Marks a partial forge method that constructs a new destination object from the
/// source primitive by assigning or binding it to the named property.
/// The method must have signature `partial TEntity MethodName(TPrimitive source)`.
/// The destination type must either support assigning the named property during
/// construction or object initialization (for example, via a settable or init-only
/// property), or expose an accessible constructor (including one resolvable via the
/// v1.6 ConstructorPreference rules) with a parameter matching that named property name.
/// The generator emits the appropriate construction form with null-guarding as needed.
/// </summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class WrapPropertyAttribute : Attribute
{
    public WrapPropertyAttribute(string propertyName) => PropertyName = propertyName;
    public string PropertyName { get; }
}
```

### Usage

```csharp
[ForgeMap]
public partial class EntityPrimitiveMapper
{
    [ExtractProperty(nameof(ClientScope.Scope))]
    public partial string? ForgeScope(ClientScope source);

    [ExtractProperty(nameof(ClientGrantType.GrantType))]
    public partial string? ForgeGrantType(ClientGrantType source);

    [WrapProperty(nameof(ClientScope.Scope))]
    public partial ClientScope? ForgeScopeEntity(string source);

    [WrapProperty(nameof(ClientGrantType.GrantType))]
    public partial ClientGrantType? ForgeGrantTypeEntity(string source);
}
```

### Resolution Algorithm — `[ExtractProperty]`

1. **Validate signature**: The decorated partial method must have exactly one parameter (the source) and a non-`void` return type.
2. **Resolve property**: The named property must exist on the source parameter type as a public readable instance property.
3. **Validate type compatibility**: The property type must be assignable to the return type, optionally through built-in coercions (string↔enum, `DateTimeOffset→DateTime`, wrapper unwrap, nullability widening/narrowing — narrowing is permitted under the same rules as Feature 1: when the source is annotated nullable but the return is non-nullable, a `!` is applied; the nullability annotation is advisory and does not block compilation).
4. **Generate body**: Emit a null-guarded return.

### Resolution Algorithm — `[WrapProperty]`

1. **Validate signature**: Exactly one parameter (the primitive) and a non-`void` reference-type return.
2. **Resolve property**: The named property must exist on the destination type as either:
   - A settable property (`set` or `init`) — assigned via object initializer, OR
   - A constructor parameter on a constructor that the v1.6 `ConstructorPreference` rules can select (single ctor with one matching parameter, or all other parameters optional with defaults).
3. **Validate type compatibility**: The parameter type must be assignable from the source parameter type, with the same coercion candidates as `[ExtractProperty]` (in reverse).
4. **Constructor selection delegates to the existing pipeline**: When a constructor-path strategy is being evaluated (steps 6–7 below), the generator reuses the v1.6 constructor-resolution logic in full — including `[ForgeConstructor]` (explicit pick), **FM0013** (ambiguous best constructor), and **FM0047** (`[ForgeConstructor]` parameter types not found). `[WrapProperty]` does not introduce a parallel constructor selector; `ConstructorPreference` only governs the *order* in which initializer-vs-constructor strategies are tried, not the constructor-scoring algorithm itself.
5. **Enumerate viable emit strategies** for the named member:
   - **Object initializer** (`new TDest { Prop = source }`): viable when (a) a parameterless constructor exists, (b) the named member is settable or `init`, AND (c) no *other* member of `TDest` is `required` (members only marked `init` without `required` are optional and do **not** block this path).
   - **Constructor with named parameter** (`new TDest(prop: source)`): viable when there is a public constructor (selected via the existing pipeline above — `[ForgeConstructor]` if present, otherwise the highest-scoring auto-resolved constructor) whose parameter matching the named member can be filled, all *other* parameters of that constructor are optional (have default values), AND every `required` member of `TDest` not also a constructor parameter is satisfied. Because `[WrapProperty]` only has one input value to spend, members not satisfied by the constructor *cannot* be filled by an appended object initializer — there is no source data for them.
6. **Pick a strategy** in deterministic precedence — first viable strategy wins, but if the first-preferred strategy is *not* viable the algorithm falls through to the next one rather than failing immediately:
   1. `ConstructorPreference.PreferParameterless` → object initializer if viable, otherwise constructor if viable, otherwise FM0068/FM0071.
   2. `ConstructorPreference.Auto` AND the named member is get-only → constructor if viable, otherwise FM0068.
   3. `ConstructorPreference.Auto` AND the named member is settable/init → object initializer if viable, otherwise constructor if viable, otherwise FM0068/FM0071.
7. **Required-member error**: If neither strategy is viable solely because of unsatisfied `required` members on `TDest`, emit **FM0071** (more specific than FM0068). FM0071 names the unsatisfied `required` members so the user knows what's blocking emit.
8. **Generate body**: Emit `new TDest { Prop = source }` or `new TDest(prop: source)` per the chosen strategy.

### Generated Code

**`[ExtractProperty]` — reference-type source, nullable return:**

```csharp
public partial string? ForgeScope(ClientScope source)
{
    if (source == null) return null!;
    return source.Scope;
}
```

**`[ExtractProperty]` — reference-type source, value-type return:**

```csharp
public partial int ForgeId(ClientScope source)
{
    if (source == null) throw new global::System.ArgumentNullException(nameof(source));
    return source.Id;
}
```

(With the default `NullHandling.ReturnNull`, the guard would emit `if (source == null) return default(int);` instead. The `throw` form above corresponds to `NullHandling.ThrowException`. See the matrix below.)

**`[ExtractProperty]` — value-type source:**

```csharp
public readonly struct LabelToken
{
    public string Label { get; init; }
}

// No null guard emitted — value types cannot be null
public partial string ForgeLabel(LabelToken source)
{
    return source.Label;
}
```

**`[ExtractProperty]` — with built-in coercion:**

```csharp
// source.At is DateTimeOffset, return type is DateTime
public partial DateTime ForgeAt(AuditEntry source)
{
    if (source == null) throw new global::System.ArgumentNullException(nameof(source));
    return source.At.UtcDateTime;
}
```

**Null-source behavior matrix for `[ExtractProperty]`:**

The source-null guard is governed by the existing forger-level **`NullHandling`** enum (`ReturnNull` is the v1.5/v1.6 default; `ThrowException` is the opt-in fail-fast mode). `NullPropertyHandling` is **not** consulted here — that enum governs property-to-property assignments inside a multi-property forger, not the top-level source-null check of a single-element extraction. The same matrix applies to `[WrapProperty]` for null-primitive sources of reference type. In generated code, reference-type returns use `return null!;`, while value-type returns (including nullable value types) use `return default(R);`.

| Source nullability | Return type | `NullHandling = ReturnNull` (default) | `NullHandling = ThrowException` |
|--------------------|-------------|---------------------------------------|---------------------------------|
| Reference type, nullable (`T?`) | Reference type, nullable (`R?`) | `if (source == null) return null!;` then access | `if (source == null) throw new ArgumentNullException(nameof(source));` then access |
| Reference type, nullable (`T?`) | Reference type, non-nullable (`R`) | `if (source == null) return null!;` then access | `if (source == null) throw …;` then access |
| Reference type, nullable (`T?`) | **Value type** (`int`, `DateTime`, etc.) | `if (source == null) return default(R);` then access (the example above shows this case explicitly) | `if (source == null) throw …;` then access |
| Reference type, nullable (`T?`) | Nullable value type (`int?`) | `if (source == null) return default(R);` then access | `if (source == null) throw …;` then access |
| Reference type, non-nullable (`T`) | Any | `if (source == null) return null!;` for reference-type `R`, or `return default(R);` for value-type `R` — guard still emitted because non-nullable annotations are advisory and `NullHandling = ReturnNull` must apply uniformly | `if (source == null) throw new ArgumentNullException(nameof(source));` |
| Value type | Any | No guard emitted (cannot be null) | No guard emitted |

For per-property-style coalescing semantics (e.g., return a non-`default` value on null), users should keep the partial method manual or use a `[ConvertWith]` factory — `[ExtractProperty]`/`[WrapProperty]` are deliberately limited to the two `NullHandling` modes the rest of the API already supports.

**`[WrapProperty]` — settable destination property:**

```csharp
public partial ClientScope? ForgeScopeEntity(string source)
{
    if (source == null) return null!;
    return new ClientScope { Scope = source };
}
```

**`[WrapProperty]` — destination via constructor (e.g., immutable type):**

```csharp
// ClientScope has only `public ClientScope(string scope)` accessible
public partial ClientScope? ForgeScopeEntity(string source)
{
    if (source == null) return null!;
    return new ClientScope(scope: source);
}
```

**`[WrapProperty]` — value-type primitive (no null-guard needed):**

```csharp
public partial Tag ForgeTag(int source)
{
    return new Tag { Id = source };
}
```

### Composition with Collections

`[ExtractProperty]` and `[WrapProperty]` are single-element forge methods. They compose with the existing collection auto-wire (v1.3) — when a *parent* forger maps `List<ClientScope>` to `List<string>`, the generator can wire the extraction forge as the per-element converter:

```csharp
[ForgeMap]
public partial class ClientMapper
{
    // Parent mapping uses Scopes: List<ClientScope> → List<string>
    public partial ClientDto Forge(Client source);

    // Auto-wired single-element forger
    [ExtractProperty(nameof(ClientScope.Scope))]
    public partial string ForgeScope(ClientScope source);
}

// Generated parent body:
__result.Scopes = source.Scopes is { } __scopes ? __scopes.Select(__x => ForgeScope(__x)).ToList() : null!;
```

This makes `[ExtractProperty]` a more discoverable alternative to `SelectProperty` (v1.7 Feature 1) when the same projection is reused across many parents. Both end up with equivalent performance; `SelectProperty` is for one-off inline cases, `[ExtractProperty]` is for shared single-element forgers.

### Interaction with Existing Features

| Feature | Interaction |
|---------|-------------|
| `[ConvertWith]` (method-level) | Mutually exclusive with `[ExtractProperty]`/`[WrapProperty]` — emit **FM0065** |
| `[ForgeFrom]` / `[ForgeWith]` | Mutually exclusive with `[ExtractProperty]`/`[WrapProperty]` — emit **FM0065** |
| `[ForgeProperty]` | Not applicable — single-element forges have no property-level mapping concept |
| `NullHandling` (forger-level) | Applied to the source null-check (defaults consistent with v1.6 behavior) |
| `[ReverseForge]` | **Not auto-reversed**. To get the inverse pair, declare both `[ExtractProperty]` and `[WrapProperty]` methods explicitly |
| `ConstructorPreference` (v1.6) | Used by `[WrapProperty]` to select an appropriate constructor when no parameterless one exists |
| Auto-wiring | A class containing `[ExtractProperty]`/`[WrapProperty]` partials participates in nested-forger discovery — sibling parent mappings can call them |
| Built-in coercions (v1.6) | Applied between the source/destination property type and the partial method's return/parameter type when they don't match exactly |
| Per-property `ConvertWith` (v1.6) | N/A on these methods (no `[ForgeProperty]` allowed) |
| `ExistingTarget` / `ForgeInto` | N/A — `[ExtractProperty]`/`[WrapProperty]` always create fresh values |

### Diagnostics

| Code | Severity | Description |
|------|----------|-------------|
| **FM0065** | Error | Method '{0}' has conflicting attributes: `[ExtractProperty]`/`[WrapProperty]` cannot combine with `[ConvertWith]`, `[ForgeFrom]`, or `[ForgeWith]` |
| **FM0066** | Error | `[ExtractProperty("{0}")]` not found on source type '{1}' for method '{2}', or not a public readable property |
| **FM0067** | Error | `[ExtractProperty]` source property type '{0}' is incompatible with method return type '{1}' for method '{2}', and no supported coercion applies |
| **FM0068** | Error | `[WrapProperty("{0}")]` not found as settable/init property or constructor parameter on destination type '{1}' for method '{2}' |
| **FM0069** | Error | `[WrapProperty]` source parameter type '{0}' is incompatible with destination property/parameter type '{1}' for method '{2}', and no supported coercion applies |
| **FM0070** | Error | `[ExtractProperty]`/`[WrapProperty]` partial method '{0}' has invalid signature — must have exactly one parameter and a non-void return type |
| **FM0071** | Error | `[WrapProperty]` cannot construct '{0}' because these `required` members are unsatisfied: {1}. Add a constructor that accepts these members, mark them `init` without `required`, or write the partial body manually |

### Behavioral Contract

| Scenario | Behavior |
|----------|----------|
| `[ExtractProperty]`, source non-null | Returns `source.PropertyName` (with coercion if needed) |
| `[ExtractProperty]`, source null | Returns `null` (reference-type return) — or throws via `NullHandling` config |
| `[WrapProperty]`, source non-null | Returns `new TDest { Prop = source }` |
| `[WrapProperty]`, source null (reference-type) | Returns `null` |
| `[WrapProperty]`, source default (value-type) | Returns wrapper containing `default(T)` |
| Property/parameter not found | FM0066 / FM0068 |
| Type incompatible | FM0067 / FM0069 |

### Competitor Comparison

| Aspect | AutoMapper | Mapperly | ForgeMap v1.7 |
|--------|-----------|---------|---------------|
| Entity → primitive | ✅ `ConstructUsing(s => s.X)` | ⚠️ Manual `MapProperty` method | ✅ `[ExtractProperty]` |
| Primitive → entity | ✅ `.ConstructUsing()` + `MapFrom` | ⚠️ Manual | ✅ `[WrapProperty]` |
| Compile-time validation | ❌ Runtime | ✅ | ✅ FM0065–FM0071 |
| Composes with collection mapping | ✅ Runtime | ✅ | ✅ Auto-wired into parent forgers |

---

## Diagnostics Summary

| Code | Severity | Category | Feature | Description |
|------|----------|----------|---------|-------------|
| FM0055 | Error | `ForgeMap` | Projection | `SelectProperty` set but source not enumerable |
| FM0056 | Error | `ForgeMap` | Projection | `SelectProperty` not found on element type |
| FM0057 | Error | `ForgeMap` | Projection | Projected element type not assignable to dest element |
| FM0073 | Error | `ForgeMap` | Projection | `SelectProperty` destination type is not enumerable |
| FM0058 | Error | `ForgeMap` | Projection | `SelectProperty` and `ConvertWith` both set |
| FM0059 | Info (disabled) | `ForgeMap` | Projection | Projection applied for property |
| FM0060 | Error | `ForgeMap` | Conditional | `Condition` and `SkipWhen` both set |
| FM0061 | Error | `ForgeMap` | Conditional | Predicate method not found / wrong signature |
| FM0062 | Error | `ForgeMap` | Conditional | Conditional cannot apply to ctor / init / required |
| FM0063 | Error | `ForgeMap` | Conditional | Conditional conflicts with `[ForgeFrom]`/`[ForgeWith]` |
| FM0064 | Info (disabled) | `ForgeMap` | Conditional | Conditional applied for property |
| FM0065 | Error | `ForgeMap` | Extract/Wrap | Conflicts with `[ConvertWith]`/`[ForgeFrom]`/`[ForgeWith]` |
| FM0066 | Error | `ForgeMap` | Extract | Source property not found |
| FM0067 | Error | `ForgeMap` | Extract | Type incompatible |
| FM0068 | Error | `ForgeMap` | Wrap | Destination property/parameter not found |
| FM0069 | Error | `ForgeMap` | Wrap | Type incompatible |
| FM0070 | Error | `ForgeMap` | Extract/Wrap | Invalid partial method signature |
| FM0071 | Error | `ForgeMap` | Wrap | Required members of destination type cannot be satisfied |
| FM0072 | Error | `ForgeMap` | Projection | `SelectProperty` conflicts with `[ForgeFrom]` / `[ForgeWith]` |

---

## API Changes Summary

### New Attributes (v1.7)

| Attribute | Targets | Description |
|-----------|---------|-------------|
| `ExtractPropertyAttribute(string)` | Method | Emit a body returning `source.PropertyName` |
| `WrapPropertyAttribute(string)` | Method | Emit a body constructing destination with `PropertyName = source` |

### Modified Attributes (v1.7)

| Attribute | Change | Description |
|-----------|--------|-------------|
| `ForgePropertyAttribute` | New: `SelectProperty` (`string?`) | Per-element projection |
| `ForgePropertyAttribute` | New: `Condition` (`string?`) | Value-level skip predicate |
| `ForgePropertyAttribute` | New: `SkipWhen` (`string?`) | Source-level skip predicate |

No new enums.

---

## Migration Guide

### From v1.6 to v1.7

All v1.7 features are **opt-in**: existing forgers compile unchanged. There are no behavioral changes to v1.6 code paths.

When adopting features:

1. **Replace `[ForgeFrom]` projection helpers** with `[ForgeProperty(SelectProperty = ...)]` — typically removes 3–5 lines per call site.
2. **Replace `[AfterForge]` undo logic** with `[ForgeProperty(Condition = ...)]` or `SkipWhen` — typically removes a callback method per forger.
3. **Replace hand-written single-property partials** with `[ExtractProperty]` / `[WrapProperty]` — typically removes a partial body per method.

---

## Limitations

| Limitation | Reason | Workaround |
|-----------|--------|------------|
| `SelectProperty` does not support dictionaries | v1.7 scope is sequence projection only | Use `[ForgeFrom]` resolver |
| `SelectProperty` not auto-reversed by `[ReverseForge]` | Wrapping primitive into element has no canonical form | Declare reverse with `ConvertWith` or pair with `[WrapProperty]` |
| `Condition`/`SkipWhen` not allowed on `init`/`required`/ctor params | Cannot "skip" a required argument | Use `[ForgeFrom]` for conditional default selection |
| `Condition`/`SkipWhen` not auto-reversed | Predicates rarely have meaningful inverses | Re-declare on the reverse method |
| `[WrapProperty]` requires settable property or matching ctor | Cannot construct without a valid sink for the value | Use a manual `[ConvertWith]` factory |
| `[ExtractProperty]` cannot project nested paths (e.g., `Owner.Name`) | v1.7 keeps the attribute simple | Use `[ConvertWith]` or chain forgers |

---

*Specification Version: 1.7 (2026-04-20)*
*Status: Draft*
*Predecessor: [SPEC-v1.6-migration-features.md](SPEC-v1.6-migration-features.md)*
*License: MIT*
